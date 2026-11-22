using BlockBox.Helpers;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using BlockBox.Models;
using BlockBox.Services;
using BlockBox.Views;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace BlockBox
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; } = null!;
        private static Mutex? _singleInstanceMutex;

        public App()
        {
            this.InitializeComponent();
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            bool isFirstInstance;
            _singleInstanceMutex = new Mutex(true, "Local\\BlockBox.ProgramFileManager.SingleInstance", out isFirstInstance);
            if (!isFirstInstance)
            {
                // 已有实例的主窗口可能正收在托盘里（不可见）。原来按窗口类名 FindWindow，
                // 会先命中还开着的搜索面板，消息就丢了，看起来像"点了图标没反应"。
                // 注册消息广播给所有顶层窗口，只有主窗口的托盘 WndProc 会响应。
                var broadcast = new IntPtr(0xFFFF);
                PostMessage(broadcast, unchecked((int)Helpers.TrayIconService.ShowExistingInstanceMessage),
                    IntPtr.Zero, IntPtr.Zero);
                Exit();
                return;
            }

            AppDomain.CurrentDomain.ProcessExit += (_, _) => _singleInstanceMutex?.Dispose();
            StartupLog.Begin();
            AppDomain.CurrentDomain.UnhandledException += (_, e) => StartupLog.Error("UnhandledException", (e.ExceptionObject as Exception)!);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                StartupLog.Error("UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
            System.Diagnostics.Debug.WriteLine("[App] OnLaunched entered");
            LogProcessIdentity();
            StartupLog.Step("OnLaunched 进入（互斥锁已获取）");
            // 启动流程整体兜底：任何异常都不再让窗口停在上一行状态上，而是直接把错误显示出来。
            StartupWindow startupWindow = null!;
            try
            {
            string baseDir = BlockBox.Helpers.AppDataPaths.Root;
            StartupLog.Step("数据目录 = " + baseDir);
            string iniPath = Path.Combine(baseDir, "setting", "setting.ini");
            var ini = new IniHelper(iniPath);
            LanguageService.Configure(Path.Combine(baseDir, "setting", "language.json"));
            string languageCode = ini.ReadValue("System", "LanguageCode", string.Empty);
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                string legacyIndex = ini.ReadValue("System", "LanguageIndex", string.Empty);
                languageCode = int.TryParse(legacyIndex, out int index)
                    ? (index == 1 ? "en-US" : "zh-CN")
                    : ini.ReadValue("System", "Language", "zh-CN");
            }
            LanguageService.SetCurrentLanguage(languageCode);
            StartupLog.Step("语言与 setting.ini 已加载: " + languageCode);

            // 先创建并呈现真实的 WinUI 窗体，避免下面的同步数据库初始化期间只看到系统白色启动画面。
            startupWindow = new StartupWindow();
            startupWindow.Activate();
            System.Diagnostics.Debug.WriteLine("[App] StartupWindow activated");
            StartupLog.Step("启动窗口已显示，等待 ContentLoaded");
            await WaitForContentLoadedAsync(startupWindow);
            StartupLog.Step("ContentLoaded 完成");
            startupWindow.SetStatus("正在准备本地数据...");

            // 初始化配置后执行一次开发阶段的数据重建，让首次扫描重新填入共享表。
            InitializeAppData();
            StartupLog.Step("InitializeAppData 完成");
            if (string.IsNullOrWhiteSpace(ini.ReadValue("System", "LanguageCode", string.Empty)))
                ini.WriteValue("System", "LanguageCode", languageCode);
            StartupLog.Step("SchemaVersion 检查");
            if (!string.Equals(ini.ReadValue("Storage", "SchemaVersion", "0"), "5", StringComparison.Ordinal))
            {
                StartupLog.Step("SchemaVersion 不是 5，删除旧数据库");
                string oldDatabase = Path.Combine(baseDir, "Data", "blockbox.db");
                foreach (string oldDatabaseFile in new[]
                {
                    oldDatabase, oldDatabase + "-wal", oldDatabase + "-shm", oldDatabase + "-journal"
                })
                {
                    if (File.Exists(oldDatabaseFile)) File.Delete(oldDatabaseFile);
                }
                ini.WriteValue("Startup", "InstalledProgramsPrepared", "false");
                ini.WriteValue("Storage", "SchemaVersion", "5");
            }

            // ⭐ 先初始化数据库（放后台线程，避免阻塞启动窗口动画）
            StartupLog.Step("开始 DatabaseContext.Initialize（后台线程）");
            await Task.Run(() =>
            {
                BlockBox.Data.DatabaseContext.Initialize();
                BlockBox.Services.ContentFolderInitializer.EnsureRootFolders();

                // 标签清理必须在主窗体读取导航之前完成。
                BlockBox.Data.TabRepository.CleanEmptyDefaultTabs();
            });
            StartupLog.Step("DatabaseContext.Initialize 完成");
            if (int.TryParse(
                    ini.ReadValue("System", "FontSizeStep", string.Empty),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int storedFontStep))
            {
                FontSizeHelper.CurrentFontSizeStep = Math.Clamp(storedFontStep, 0, 4);
            }
            else
            {
                string legacyValue = ini.ReadValue("System", "FontSizeScale", SettingsDefaults.FontSizeScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
                FontSizeHelper.CurrentFontSizeStep = double.TryParse(
                    legacyValue,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double legacyScale)
                    ? FontSizeHelper.GetFontSizeStepFromScale(legacyScale)
                    : SettingsDefaults.FontSizeStep;
            }
            var startupStopwatch = System.Diagnostics.Stopwatch.StartNew();
            bool firstProgramSetup = !string.Equals(
                ini.ReadValue("Startup", "InstalledProgramsPrepared", "false"), "true",
                StringComparison.OrdinalIgnoreCase);
            StartupLog.Step($"firstProgramSetup={firstProgramSetup}");

            if (firstProgramSetup)
            {
                try
                {
                    var progress = new Progress<string>(startupWindow.SetStatus);
                    string filterPath = Path.Combine(baseDir, "setting", "program-filter.ini");
                    var filter = ProgramFilter.LoadOrCreateDefault(filterPath);
                    StartupLog.Step("开始首次程序扫描导入");
                    await ProgramImportService.ScanAndImportAsync(
    BlockBox.Data.ModeRepository.GetAllModes(),
    filter, progress,
    hideNoIconComponents: ini.ReadBool("Program", "HideNoIconComponentPrograms",
        BlockBox.Helpers.SettingsDefaults.HideNoIconComponentPrograms));
                    StartupLog.Step("首次程序扫描导入完成");
                    ini.WriteValue("Startup", "InstalledProgramsPrepared", "true");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[首次启动初始化] {ex}");
                    StartupLog.Error("首次程序扫描导入", ex);
                    startupWindow.SetStatus("初始化未完成，将在下次启动时重试...");
                    await Task.Delay(900);
                }
            }
            else
            {
                StartupLog.Step("跳过首次程序扫描");
                startupWindow.SetStatus("正在加载已保存的数据...");
                //await Task.Yield();
            }

            var iconProgress = new Progress<string>(startupWindow.SetStatus);

            // ⭐ 补足最短展示时长，后续启动不再一闪而过
            int elapsed = (int)startupStopwatch.ElapsedMilliseconds;
            if (elapsed < 500)
                await Task.Delay(500 - elapsed);
            // 启动主窗口前，首次扫描和图标缓存均已完成。
            startupWindow.SetStatus("正在预载全部应用图标...");
            StartupLog.Step("开始 new MainWindow");
            var mainWindow = new MainWindow();
            StartupLog.Step("new MainWindow 完成");
            MainWindow = mainWindow;
            await mainWindow.PreloadAllApplicationIconsAsync(iconProgress, maxCount: 32);
            StartupLog.Step("预载首屏图标完成");
            MainWindow.Activate();
            StartupLog.Step("主窗口已激活");

            // 备份目录清理与界面无关，放到主窗口显示之后再跑。
            _ = Task.Run(() =>
            {
                try
                {
                    BlockBox.Services.BackupService.CleanExpiredDeletedFolders();
                    BlockBox.Services.BackupService.CleanExpiredExternalDragStagingDirectories();
                    StartupLog.Step("后台备份清理完成");
                }
                catch (Exception ex)
                {
                    StartupLog.Error("后台备份清理", ex);
                }
            });

            await startupWindow.FadeOutAsync();
            // 启动窗口淡出后再补其余图标，避免和主界面淡入抢 UI 线程。
            _ = mainWindow.PreloadRemainingApplicationIconsAsync();
            StartupLog.Step("启动窗口已淡出");
            if (ini.ReadBool("System", "OpenSearchOnStartup", SettingsDefaults.OpenSearchOnStartup))
            {
                // 主窗体是 360ms 淡入、启动窗口淡出约 280ms；再等一小段，保证“主界面渐入完成
                // 后再出现全局搜索面板”，而不是两个淡入叠在一起。
                await Task.Delay(160);
                mainWindow.ShowGlobalSearchWindow();
                StartupLog.Step("全局搜索窗口已打开，启动完成");
            }
            else
            {
                // 不自动打开面板时，把主窗体右上角的搜索入口按钮显示出来。
                mainWindow.ShowSearchLauncherImmediately();
                StartupLog.Step("未自动打开全局搜索，已显示入口按钮");
            }
            StartupLog.Done();
            }
            catch (Exception ex)
            {
                // 关键：把失败原因显示在启动窗口上，而不是让用户对着"正在准备本地数据..."干等。
                StartupLog.Error("启动流程", ex);
                string message = ex.Message;
                if (message.Contains("readonly", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("read-only", StringComparison.OrdinalIgnoreCase))
                    message += "（数据目录不可写：可能被 EFS 加密或被设为只读）";
                try
                {
                    if (startupWindow is not null)
                    {
                        startupWindow.SetStatus("启动失败：" + message);
                        await Task.Delay(TimeSpan.FromSeconds(8));
                    }
                }
                catch
                {
                    // 显示失败也不能再抛。
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        /// <summary>
        /// 把进程身份/完整性相关信息打到调试输出：用于判断是否运行在沙箱/AppContainer 里。
        /// 沙箱进程写不了用户 AppData，会表现为 SQLite "readonly database"。
        /// </summary>
        private static void LogProcessIdentity()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Diagnostics.Debug.WriteLine($"[Token] User={identity.Name} Owner={identity.Owner?.Value} IsSystem={identity.IsSystem} IsAnonymous={identity.IsAnonymous}");
                System.Diagnostics.Debug.WriteLine($"[Token] Environment.UserName={Environment.UserName} LOCALAPPDATA={Environment.GetEnvironmentVariable("LOCALAPPDATA")}");
                LogTokenSecurityState(identity.Token);
                var groups = identity.Groups;
                if (groups != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Token] Groups.Count={groups.Count}");
                    int printed = 0;
                    foreach (var group in groups)
                    {
                        if (printed++ >= 80) { System.Diagnostics.Debug.WriteLine("[Token] …(更多组已省略)"); break; }
                        string name;
                        try { name = group.Translate(typeof(System.Security.Principal.NTAccount)).Value; }
                        catch { name = group.Value; }
                        System.Diagnostics.Debug.WriteLine($"[Token] Group={group.Value} ({name})");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Token] 读取进程身份失败: " + ex.Message);
            }
        }

        private const int TokenRestrictedSids = 11;
        private const int TokenElevationTypeInfo = 18;
        private const int TokenElevationInfo = 20;
        private const int TokenIsAppContainer = 29;
        private const int TokenIntegrityLevel = 25;

        [StructLayout(LayoutKind.Sequential)]
        private struct SidAndAttributes
        {
            public IntPtr Sid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenGroupsHeader
        {
            public uint GroupCount;
            public SidAndAttributes FirstGroup;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            IntPtr tokenInformation,
            int tokenInformationLength,
            out int returnLength);

        private static void LogTokenSecurityState(IntPtr token)
        {
            LogTokenInteger(token, TokenElevationTypeInfo, "ElevationType", value => value switch
            {
                1 => "Default",
                2 => "Full",
                3 => "Limited",
                _ => $"Unknown({value})"
            });
            LogTokenInteger(token, TokenElevationInfo, "IsElevated", value => value == 0 ? "False" : "True");

            if (TryGetTokenInformationBuffer(token, TokenIsAppContainer, out IntPtr appContainerBuffer, out int appContainerLength))
            {
                try
                {
                    bool isAppContainer = appContainerLength >= sizeof(int) && Marshal.ReadInt32(appContainerBuffer) != 0;
                    System.Diagnostics.Debug.WriteLine($"[Token] IsAppContainer={isAppContainer}");
                }
                finally
                {
                    Marshal.FreeHGlobal(appContainerBuffer);
                }
            }

            if (TryGetTokenInformationBuffer(token, TokenIntegrityLevel, out IntPtr integrityBuffer, out _))
            {
                try
                {
                    SidAndAttributes label = Marshal.PtrToStructure<SidAndAttributes>(integrityBuffer);
                    string sid = new System.Security.Principal.SecurityIdentifier(label.Sid).Value;
                    string level = sid switch
                    {
                        "S-1-16-0" => "Untrusted",
                        "S-1-16-4096" => "Low",
                        "S-1-16-8192" => "Medium",
                        "S-1-16-8448" => "MediumPlus",
                        "S-1-16-12288" => "High",
                        "S-1-16-16384" => "System",
                        _ => "Other"
                    };
                    System.Diagnostics.Debug.WriteLine($"[Token] IntegrityLevel={level} SID={sid}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[Token] 读取完整性级别失败: " + ex.Message);
                }
                finally
                {
                    Marshal.FreeHGlobal(integrityBuffer);
                }
            }

            if (!TryGetTokenInformationBuffer(token, TokenRestrictedSids, out IntPtr restrictedSidsBuffer, out int restrictedSidsLength))
                return;

            try
            {
                uint groupCount = unchecked((uint)Marshal.ReadInt32(restrictedSidsBuffer));
                int firstGroupOffset = Marshal.OffsetOf<TokenGroupsHeader>(nameof(TokenGroupsHeader.FirstGroup)).ToInt32();
                int groupSize = Marshal.SizeOf<SidAndAttributes>();
                int availableGroups = Math.Max(0, (restrictedSidsLength - firstGroupOffset) / groupSize);
                int count = (int)Math.Min(groupCount, (uint)availableGroups);

                System.Diagnostics.Debug.WriteLine($"[Token] RestrictedSids.Count={groupCount}");
                for (int i = 0; i < count; i++)
                {
                    IntPtr entryAddress = IntPtr.Add(restrictedSidsBuffer, firstGroupOffset + i * groupSize);
                    SidAndAttributes entry = Marshal.PtrToStructure<SidAndAttributes>(entryAddress);
                    string sid = new System.Security.Principal.SecurityIdentifier(entry.Sid).Value;
                    System.Diagnostics.Debug.WriteLine($"[Token] RestrictedSid={sid} Attributes=0x{entry.Attributes:X8}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Token] 读取受限 SID 失败: " + ex.Message);
            }
            finally
            {
                Marshal.FreeHGlobal(restrictedSidsBuffer);
            }
        }

        private static void LogTokenInteger(IntPtr token, int informationClass, string name, Func<int, string> format)
        {
            if (!TryGetTokenInformationBuffer(token, informationClass, out IntPtr buffer, out int length))
                return;

            try
            {
                if (length >= sizeof(int))
                    System.Diagnostics.Debug.WriteLine($"[Token] {name}={format(Marshal.ReadInt32(buffer))}");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool TryGetTokenInformationBuffer(
            IntPtr token,
            int informationClass,
            out IntPtr buffer,
            out int bufferLength)
        {
            buffer = IntPtr.Zero;
            GetTokenInformation(token, informationClass, IntPtr.Zero, 0, out bufferLength);
            if (bufferLength <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Token] GetTokenInformation({informationClass}) 查询长度失败，Win32={Marshal.GetLastWin32Error()}");
                return false;
            }

            buffer = Marshal.AllocHGlobal(bufferLength);
            if (GetTokenInformation(token, informationClass, buffer, bufferLength, out int returnedLength))
            {
                bufferLength = returnedLength;
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            System.Diagnostics.Debug.WriteLine($"[Token] GetTokenInformation({informationClass}) 失败，Win32={error}");
            return false;
        }

        /// <summary>
        /// 等待窗口 Content 完成首次加载（Loaded 事件），确保已经完成首次布局。
        /// </summary>
        private static Task WaitForContentLoadedAsync(Window window)
        {
            var tcs = new TaskCompletionSource();

            if (window.Content is not FrameworkElement fe)
            {
                tcs.TrySetResult();
                return tcs.Task;
            }

            if (fe.IsLoaded)
            {
                tcs.TrySetResult();
                return tcs.Task;
            }

            void OnLoaded(object s, RoutedEventArgs e)
            {
                fe.Loaded -= OnLoaded;
                tcs.TrySetResult();
            }

            fe.Loaded += OnLoaded;
            return tcs.Task;
        }
        private void InitializeAppData()
        {
            // 打包/免打包统一走 AppDataPaths：优先 LocalFolder，拿不到包身份时回退到同一份 LocalState，
            // 避免 ApplicationData.Current 抛 UnauthorizedAccessException 导致启动失败。
            string baseDir = BlockBox.Helpers.AppDataPaths.Root;

            // 定义目录路径
            string settingDir = Path.Combine(baseDir, "setting");
            string contentDir = Path.Combine(baseDir, "content");
            string workDir = Path.Combine(contentDir, "work");
            string gameDir = Path.Combine(contentDir, "game");
            string defaultDir = Path.Combine(contentDir, "default");

            // 创建目录（如果存在则不会报错）
            Directory.CreateDirectory(settingDir);
            Directory.CreateDirectory(contentDir);
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(gameDir);
            Directory.CreateDirectory(defaultDir);

            // 定义 INI 文件路径
            string iniPath = Path.Combine(settingDir, "setting.ini");
            // 获取绝对路径，并在 VS 的输出窗口里打印出来
            string absolutePath = Path.GetFullPath(iniPath);
            System.Diagnostics.Debug.WriteLine("=========================================");
            System.Diagnostics.Debug.WriteLine("INI 文件路径: " + absolutePath);
            System.Diagnostics.Debug.WriteLine("=========================================");
            // 如果 INI 文件不存在，则创建一个带默认值的
            if (!File.Exists(iniPath))
            {
                IniHelper ini = new IniHelper(iniPath);

                // ================= 左侧菜单 =================
                ini.WriteValue("Sidebar", "EnableCustomBackground", "false");
                ini.WriteValue("Sidebar", "IsVideoBackground", "false");
                ini.WriteValue("Sidebar", "BackgroundPosition", "Stretch");
                ini.WriteValue("Sidebar", "VideoPath", "");
                ini.WriteValue("Sidebar", "BackgroundColor", "#FFFFFFFF");
                ini.WriteValue("Sidebar", "FontColor", "#FFFFFF");
                ini.WriteValue("Sidebar", "ItemFontColor", "#54595b");
                ini.WriteValue("Sidebar", "ItemClickFontColor", "#54595b");
                ini.WriteValue("Sidebar", "ItemHoverColor", "#20FFFFFF");
                ini.WriteValue("Sidebar", "ItemClickColor", "#40FFFFFF");
                ini.WriteValue("Sidebar", "AddButtonBgColor", "#FFFFFFFF");
                ini.WriteValue("Sidebar", "AddButtonFontColor", "#FFFFFF");
                ini.WriteValue("Sidebar", "State", "true");
                // ================= 左侧菜单 =================
                ini.WriteValue("Sidebar", "BackgroundMode", "0");           // 0=纯色 1=图片 2=视频
                ini.WriteValue("Sidebar", "BackgroundImagePath", "");
                ini.WriteValue("Sidebar", "BackgroundVideoPath", "");

                // ================= 标签栏 =================
                ini.WriteValue("TabBar", "BackgroundColor", "#FFFFFFFF");
                ini.WriteValue("TabBar", "EnableCustomBackground", "false");
                ini.WriteValue("TabBar", "IsVideoBackground", "false");
                ini.WriteValue("TabBar", "BackgroundPosition", "Stretch");
                ini.WriteValue("TabBar", "VideoPath", "");

                // ================= 搜索栏 =================
                ini.WriteValue("SearchBar", "BackgroundColor", "#FFFFFFFF");
                ini.WriteValue("SearchBar", "FontColor", "#54595b");
                ini.WriteValue("SearchBar", "ClickFontColor", "#54595b");
                ini.WriteValue("SearchBar", "ButtonHoverColor", "#CC244A80");

                // ================= 内容栏 =================
                ini.WriteValue("ContentArea", "BackgroundColor", "#00000000");
                ini.WriteValue("ContentArea", "EnableCustomBackground", "false");
                ini.WriteValue("ContentArea", "IsVideoBackground", "false");
                ini.WriteValue("ContentArea", "BackgroundPosition", "Stretch");
                ini.WriteValue("ContentArea", "VideoPath", "");
                // ================= 内容栏 =================
                ini.WriteValue("ContentArea", "BackgroundMode", "0");
                ini.WriteValue("ContentArea", "BackgroundImagePath", "");
                ini.WriteValue("ContentArea", "BackgroundVideoPath", "");

                // ================= 内容展示 =================
                ini.WriteValue("ContentDisplay", "BackgroundColor", "#00000000");
                ini.WriteValue("ContentDisplay", "EnableCustomBackground", "false");
                ini.WriteValue("ContentDisplay", "IsVideoBackground", "false");
                ini.WriteValue("ContentDisplay", "BackgroundPosition", "Stretch");
                ini.WriteValue("ContentDisplay", "VideoPath", "");
                ini.WriteValue("ContentDisplay", "ItemSelectedColor", "#40FFFFFF");

                // ================= 全局 =================
                ini.WriteValue("Global", "PrimaryBackground", "#FF244A80");
                ini.WriteValue("Global", "SecondaryBackground", "#FFF0F0F0");
                ini.WriteValue("Global", "PrimaryFontColor", "#FFFFFF");
                ini.WriteValue("Global", "PrimaryFontColorAuto", "true");
                ini.WriteValue("Global", "ShortcutGridFontColor", "#FF000000");
                ini.WriteValue("Global", "ShortcutGridFontColorAuto", "true");
                ini.WriteValue("Global", "SecondaryFontColor", "#A0FFFFFF");

                // ================= 计算后的文字颜色 =================
                ini.WriteValue("ComputedTextColors", "Sidebar", "");
                ini.WriteValue("ComputedTextColors", "Main", "");
                ini.WriteValue("ComputedTextColors", "TabBar", "");
                ini.WriteValue("ComputedTextColors", "Settings", "");
                ini.WriteValue("ComputedTextColors", "SidebarAddButtonNormal", "");
                ini.WriteValue("ComputedTextColors", "BackButtonNormal", "");
                ini.WriteValue("ComputedTextColors", "Secondary", "");

                // ================= 系统设置 =================
                ini.WriteValue("System", "Language", "zh-CN"); 
                ini.WriteValue("System", "FontSizeScale", SettingsDefaults.FontSizeScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ini.WriteValue("System", "FontSizeStep", SettingsDefaults.FontSizeStep.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ini.WriteValue("System", "AutoStart", SettingsDefaults.AutoStart.ToString().ToLowerInvariant());
                ini.WriteValue("System", "TrayOnExit", SettingsDefaults.TrayOnExit.ToString().ToLowerInvariant());
                ini.WriteValue("System", "MinimizeToTray", SettingsDefaults.MinimizeToTray.ToString().ToLowerInvariant());
            }
        }
    }
}
