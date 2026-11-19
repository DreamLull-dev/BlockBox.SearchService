using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;

namespace BlockBox.Helpers
{
    /// <summary>
    /// 统一解析每用户数据根目录（原 LocalFolder）。
    ///
    /// <para>
    /// <c>Windows.Storage.ApplicationData.Current</c> 依赖 MSIX 包身份：直接运行 exe、
    /// 从管理员终端启动、或以开发方式安装的包状态损坏时，它会抛
    /// <c>UnauthorizedAccessException: Error trying to open the explicit application data handle</c>。
    /// </para>
    /// <para>
    /// 这里先用不抛异常的 <c>GetCurrentPackageFullName</c> 判断进程有没有包身份：
    /// 没有身份时**根本不调用** ApplicationData，避免在调试器里被一次“首次异常”打断；
    /// 有身份但状态损坏时再兜底。两种情况的回退都指向**同一个** LocalState 目录，
    /// 不会出现“打开后库空了”。
    /// </para>
    /// </summary>
    public static class AppDataPaths
    {
        /// <summary>与 Package.appxmanifest 中 &lt;Identity Name="..."&gt; 保持一致。</summary>
        private const string PackageIdentityName = "513e069a-f9ac-40c3-ae97-726f496e246c";
        private const int ErrorInsufficientBuffer = 122;
        private const int AppModelErrorNoPackage = 15700;

        private static readonly Lazy<string> RootLazy = new(ResolveRoot);

        /// <summary>测试用：设置后直接作为数据根目录，不再探测包身份。</summary>
        public static string? OverrideRoot { get; set; }

        /// <summary>每用户数据根目录（等价于打包模式下 ApplicationData.Current.LocalFolder.Path）。</summary>
        public static string Root => OverrideRoot ?? RootLazy.Value;

        private static string ResolveRoot()
        {
            // 诊断/测试用：强制指定数据根目录（例如把 LocalState 复制一份做隔离复现）。
            string? forced = Environment.GetEnvironmentVariable("BLOCKBOX_DATA_ROOT");
            if (!string.IsNullOrWhiteSpace(forced)) return forced;

            string? packageFullName = TryGetPackageFullName();
            if (packageFullName is not null)
            {
                // 有包身份才调用；它仍可能因为包状态损坏而失败，所以保留兜底。
                try
                {
                    string packaged = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                    if (!string.IsNullOrWhiteSpace(packaged))
                    {
                        // 包数据目录不可写时（例如被 EFS 加密成只读、或被设成只读），
                        // 优先改用用户可写的同名数据副本，避免启动直接失败。
                        if (IsWritable(packaged)) return packaged;
                        Log($"包数据目录不可写：{packaged}");
                        string? writable = FindWritableCopy();
                        if (writable is not null)
                        {
                            Log($"改用可写数据目录：{writable}");
                            return writable;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AppData] ApplicationData 不可用({ex.GetType().Name}): {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("[AppData] 当前进程没有 MSIX 包身份，跳过 ApplicationData。");
            }

            string? localState = FindPackagedLocalState(packageFullName);
            if (localState is not null)
            {
                Log($"回退到包数据目录：{localState}");
                return localState;
            }

            string? copy = FindWritableCopy();
            if (copy is not null)
            {
                Log($"回退到可写数据目录：{copy}");
                return copy;
            }

            // 最后兜底：必须保证返回的一定是可写目录，否则后续 Directory.CreateDirectory / 建库
            // 会把 UnauthorizedAccessException 一路带到启动流程，最终变成 0xc0000005 崩溃。
            foreach (string candidate in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockBox"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockBoxData"),
                Path.Combine(Path.GetTempPath(), "BlockBox")
            })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                if (IsWritable(candidate))
                {
                    Log($"未找到可用数据目录，回退到：{candidate}");
                    return candidate;
                }
            }

            // 不要把不可写的临时目录伪装成有效数据根目录；否则后续初始化会在 setting/ 或数据库处失败。
            throw new UnauthorizedAccessException(
                "没有找到可写的数据目录。请检查 MSIX LocalState 所在卷及当前进程完整性级别。");
        }

        /// <summary>
        /// 用户可写目录中的历史数据副本（含 Data\blockbox.db）。存在才用，避免误用空目录。
        /// </summary>
        private static string? FindWritableCopy()
        {
            foreach (string candidate in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockBox"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockBoxData"),
                Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "", "AppData", "Local", "BlockBoxData"),
                Path.Combine(Path.GetTempPath(), "BlockBoxData")
            })
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    if (!File.Exists(Path.Combine(candidate, "Data", "blockbox.db"))) continue;
                    if (!IsWritable(candidate)) continue;
                    return candidate;
                }
                catch
                {
                    // 试下一个候选。
                }
            }
            return null;
        }

        /// <summary>真实写一个临时文件来判断目录是否可写（EFS/只读/ACL 都能测出来）。</summary>
        private static bool IsWritable(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                           FileOptions.DeleteOnClose))
                {
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppData] 目录不可写: {directory}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 不抛异常地取当前进程的 PackageFullName；没有包身份时返回 null。
        /// 格式：Name_Version_Architecture_ResourceId_PublisherId。
        /// </summary>
        private static string? TryGetPackageFullName()
        {
            try
            {
                uint length = 0;
                int result = GetCurrentPackageFullName(ref length, null);
                if (result == AppModelErrorNoPackage) return null;
                if (result != ErrorInsufficientBuffer || length == 0) return null;
                var buffer = new StringBuilder((int)length);
                return GetCurrentPackageFullName(ref length, buffer) == 0 ? buffer.ToString() : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppData] GetCurrentPackageFullName 失败: {ex.Message}");
                return null;
            }
        }

        private static string? FindPackagedLocalState(string? packageFullName)
        {
            try
            {
                string? familyName = null;
                if (packageFullName is not null)
                {
                    string[] parts = packageFullName.Split('_');
                    if (parts.Length >= 2)
                        familyName = parts[0] + "_" + parts[^1];
                }

                var packagesRoots = new List<string>
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages")
                };

                // MSIX 包数据可能位于非系统卷，例如 E:\WpSystem\<UserSid>\AppData\Local\Packages。
                string? userSid;
                using (System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    userSid = identity.User?.Value;

                if (!string.IsNullOrWhiteSpace(userSid))
                {
                    foreach (DriveInfo drive in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                            packagesRoots.Add(Path.Combine(
                                drive.RootDirectory.FullName, "WpSystem", userSid, "AppData", "Local", "Packages"));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[AppData] 检查卷 {drive.Name} 失败: {ex.Message}");
                        }
                    }
                }

                foreach (string packages in packagesRoots)
                {
                    try
                    {
                        if (!Directory.Exists(packages)) continue;

                        if (familyName is not null)
                        {
                            string byFamily = Path.Combine(packages, familyName, "LocalState");
                            if (Directory.Exists(byFamily) && IsWritable(byFamily)) return byFamily;
                        }

                        // 兼容包身份不可用时，按清单 Identity Name 前缀查找。
                        foreach (string directory in Directory.EnumerateDirectories(packages, PackageIdentityName + "_*"))
                        {
                            string localState = Path.Combine(directory, "LocalState");
                            if (Directory.Exists(localState) && IsWritable(localState)) return localState;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AppData] 搜索包数据目录 {packages} 失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppData] 查找包数据目录失败: {ex.Message}");
            }
            return null;
        }

        /// <summary>回退只发生一次，把原因写到 %LOCALAPPDATA%\BlockBox\appdata-resolve.log。</summary>
        private static void Log(string message)
        {
            // 依次尝试用户目录与临时目录，日志写不进用户目录时自动降级，不再抛“拒绝访问”。
            foreach (string directory in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockBox"),
                Path.Combine(Path.GetTempPath(), "BlockBox")
            })
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(directory)) continue;
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, "appdata-resolve.log"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
                    return;
                }
                catch
                {
                    // 换下一个候选目录。
                }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, StringBuilder? packageFullName);
    }
}
