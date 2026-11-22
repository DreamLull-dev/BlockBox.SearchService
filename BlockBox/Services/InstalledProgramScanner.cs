using BlockBox.Helpers;
using BlockBox.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Management.Deployment;
using Windows.Storage.Streams;

namespace BlockBox.Services
{
    public static class InstalledProgramScanner
    {
        /// <summary>
        /// 白名单：这些应用强制使用 standard logo（磁盘文件），
        /// 用于修正"真图标被判为占位图"的情况。
        /// </summary>
        private static readonly HashSet<string> _forceUseStandardLogoPackages =
            new(StringComparer.OrdinalIgnoreCase)
            {
        //"Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe",
        // 后续遇到误判的应用，加包名（pkg.Id.Name）
            };
        // ==================== Windows 系统工具过滤 ====================
        private static readonly string[] WindowsSystemKeywords =
        {
            "windows 管理工具", "windows 附件", "windows 系统", "windows 辅助工具",
            "windows administrative tools", "windows accessories",
            "windows system", "windows ease of access",
            "windows 轻松使用",
            "windows powershell", "windows terminal", "windows 安全中心",
            "windows defender", "远程桌面连接", "remote desktop connection",
            "计算机管理", "磁盘管理", "设备管理器", "任务计划程序",
            "事件查看器", "服务和应用程序", "资源监视器", "性能监视器",
            "注册表编辑器", "regedit", "命令提示符", "command prompt",
            "控制面板", "control panel", "系统配置", "msconfig",
            "windows 更新", "windows update",
            "windows 功能", "windows features",
            "windows 应用", "windows 邮件", "windows 地图", "windows 相机",
            "windows 便笺", "windows 日历",
        };

        private static bool IsWindowsSystemItem(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return false;

            string lower = displayName.ToLowerInvariant();
            foreach (var kw in WindowsSystemKeywords)
                if (lower.Contains(kw, StringComparison.Ordinal)) return true;

            return false;
        }
        /// <summary>
        /// 开始菜单里"系统工具文件夹"——这些文件夹下的所有 .lnk 全部排除。
        /// 用路径片段匹配，大小写不敏感。
        /// </summary>
        private static readonly string[] ExcludedStartMenuPathFragments =
        {
    // 英文
    @"\administrative tools\",
    @"\windows administrative tools\",
    @"\accessories\",
    @"\windows accessories\",
    @"\system tools\",
    @"\windows system\",
    @"\ease of access\",
    @"\windows ease of access\",
    @"\windows powershell\",
    @"\powershell\",
    @"\windows terminal\",
    @"\accessibility\",

    // 中文
    @"\管理工具\",
    @"\windows 管理工具\",
    @"\附件\",
    @"\windows 附件\",
    @"\系统工具\",
    @"\windows 系统\",
    @"\轻松使用\",
    @"\windows 轻松使用\",
    @"\辅助功能\",
};

        private static bool IsInExcludedStartMenuFolder(string lnkFullPath)
        {
            string lower = lnkFullPath.ToLowerInvariant();
            foreach (var frag in ExcludedStartMenuPathFragments)
            {
                if (lower.Contains(frag))
                    return true;
            }
            return false;
        }
        /// <summary>
        /// 这些目录下的任何文件都不作为启动路径（系统缓存/安装器缓存）。
        /// 用前缀匹配，大小写不敏感。
        /// </summary>
        private static readonly string[] ExcludedPathPrefixes =
        {
    @"c:\programdata\package cache\",
    @"c:\programdata\microsoft\visualstudio\packages\",
    @"c:\windows\installer\",
    @"c:\windows\winsxs\",
    @"c:\$windows.~bt\",
    @"c:\$windows.~ws\",
};

        private static bool IsInExcludedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            string lower = path.ToLowerInvariant();
            foreach (var prefix in ExcludedPathPrefixes)
            {
                if (lower.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
        private static bool IsLaunchableTargetExtension(string path)
        {
            // ⭐ UWP 应用特殊路径：直接放行
            if (path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                return true;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".exe"
                || ext == ".url"
                || ext == ".bat"
                || ext == ".cmd"
                || ext == ".com"
                || ext == ".msc"
                || ext == ".cpl"
                || ext == ".ps1"
                || ext == ".jar"
                || ext == ".appref-ms";
        }
        // ==================== 主入口 ====================
        private static void ScanStoreApps(List<InstalledProgramInfo> results, ProgramFilter? filter)
        {
            try
            {
                var pm = new PackageManager();

                // 枚举当前用户的包（传 null 表示当前用户）
                IEnumerable<Package> packages = pm.FindPackagesForUser(string.Empty);

                foreach (var pkg in packages)
                {
                    try
                    {
                        // 过滤：框架包、资源包、系统包
                        if (pkg.IsFramework) continue;
                        if (pkg.IsResourcePackage) continue;

                        // 过滤系统应用（Microsoft 官方 Store 应用可选保留或排除）
                        var signatureKind = pkg.SignatureKind;
                        if (signatureKind == PackageSignatureKind.System) continue;

                        // 拿显示名（从 AppxManifest 里的 DisplayName，多语言会有多个）
                        string displayName = GetPackageDisplayName(pkg);
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        // 过滤用户配置
                        if (filter?.ShouldExclude(displayName) == true) continue;
                        if (IsWindowsSystemItem(displayName)) continue;

                        // 拿 AUMID（Application User Model ID），用于启动
                        string? aumid = GetPackageAumid(pkg);
                        if (string.IsNullOrEmpty(aumid)) continue;

                        // 启动路径用 shell:AppsFolder\ 前缀
                        string launchPath = $"shell:AppsFolder\\{aumid}";

                        results.Add(new InstalledProgramInfo
                        {
                            DisplayName = displayName.Trim(),
                            TargetPath = launchPath,
                            Version = $"{pkg.Id.Version.Major}.{pkg.Id.Version.Minor}.{pkg.Id.Version.Build}.{pkg.Id.Version.Revision}",
                            Publisher = pkg.PublisherDisplayName,
                            Source = ProgramSource.StoreApp
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string GetPackageDisplayName(Package pkg)
        {
            try
            {
                // 优先中文，其次英文，最后取第一个
                var manifest = pkg.GetAppListEntries();
                if (manifest.Count > 0)
                    return manifest[0].DisplayInfo.DisplayName;
            }
            catch { }
            return "";
        }
        /// <summary>
        /// 在 UWP 包的安装目录里找标准的居中 logo（Square150x150Logo）。
        /// 找不到返回 null。
        /// </summary>
        private static string? FindStandardLogo(string installPath)
        {
            try
            {
                // 大多数包的 logo 在 Assets 子目录
                string assetsDir = Path.Combine(installPath, "Assets");
                if (!Directory.Exists(assetsDir))
                    assetsDir = installPath;

                // 按优先级：scale-400 > scale-200 > scale-100 > 原始
                string[] candidates =
                {
            "Square150x150Logo.scale-400.png",
            "Square150x150Logo.scale-200.png",
            "Square150x150Logo.scale-100.png",
            "Square150x150Logo.png",
            "Square310x310Logo.scale-200.png",
            "Square310x310Logo.png",
        };

                foreach (var name in candidates)
                {
                    string full = Path.Combine(assetsDir, name);
                    if (File.Exists(full)) return full;
                }

                // 兜底：Assets 下任何 Square150x150Logo 开头的（取最大的）
                var any = Directory.EnumerateFiles(assetsDir, "Square150x150Logo*.png")
                                   .OrderByDescending(f => new FileInfo(f).Length)
                                   .FirstOrDefault();
                return any;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 把源 logo 拷贝到缓存目录，返回缓存路径。
        /// </summary>
        private static string? CopyLogoToCache(string sourcePath, string aumid)
        {
            try
            {
                string baseDir = BlockBox.Helpers.AppDataPaths.Root;
                string cacheDir = Path.Combine(baseDir, "icon-cache", "uwp");
                Directory.CreateDirectory(cacheDir);

                byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(aumid));
                string safeName = Convert.ToHexString(hash)[..16];
                string destPath = Path.Combine(cacheDir, safeName + ".png");

                // ⭐ 强制覆盖（不再判断 File.Exists）
                File.Copy(sourcePath, destPath, overwrite: true);

                System.Diagnostics.Debug.WriteLine(
                    $"[图标] CopyLogoToCache: {aumid} ← {sourcePath} ({new FileInfo(destPath).Length} bytes)");
                return destPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[图标] 拷贝失败 {aumid}: {ex.Message}");
                return null;
            }
        }

        private static string? SaveBytesToCache(byte[] png, string aumid)
        {
            try
            {
                string baseDir = BlockBox.Helpers.AppDataPaths.Root;
                string cacheDir = Path.Combine(baseDir, "icon-cache", "uwp");
                Directory.CreateDirectory(cacheDir);

                byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(aumid));
                string safeName = Convert.ToHexString(hash)[..16];
                string filePath = Path.Combine(cacheDir, safeName + ".png");

                // ⭐ 强制覆盖
                File.WriteAllBytes(filePath, png);

                System.Diagnostics.Debug.WriteLine(
                    $"[图标] SaveBytesToCache: {aumid} ({png.Length} bytes)");
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[图标] 写缓存失败 {aumid}: {ex.Message}");
                return null;
            }
        }
        private static string? GetPackageAumid(Package pkg)
        {
            try
            {
                var entries = pkg.GetAppListEntries();
                if (entries.Count == 0) return null;

                // 取第一个可启动入口的 AUMID
                return entries[0].AppUserModelId;
            }
            catch { }
            return null;
        }
        private static void ScanUwpApps(List<InstalledProgramInfo> results, ProgramFilter? filter)
        {
            int accepted = 0, logoOk = 0, logoFail = 0;

            try
            {
                var pm = new PackageManager();
                var packages = pm.FindPackagesForUser(string.Empty);

                foreach (var pkg in packages)
                {
                    try
                    {
                        if (pkg.IsFramework) continue;
                        if (pkg.IsResourcePackage) continue;

                        var entries = pkg.GetAppListEntries();
                        if (entries.Count == 0) continue;

                        foreach (var entry in entries)
                        {
                            string displayName = entry.DisplayInfo.DisplayName;
                            string aumid = entry.AppUserModelId;

                            if (string.IsNullOrWhiteSpace(displayName)) continue;
                            if (filter?.ShouldExclude(displayName) == true) continue;
                            if (IsWindowsSystemItem(displayName)) continue;

                            string? localIcon = null;
                            try
                            {
                                byte[]? logoFromStandard = TryReadStandardLogoBytes(pkg.InstalledLocation.Path);
                                byte[]? logoFromGetLogo = TryReadGetLogoBytes(entry);

                                System.Diagnostics.Debug.WriteLine(
                                    $"[图标] {pkg.Id.Name} | standard={(logoFromStandard?.Length ?? 0)}B, " +
                                    $"getLogo={(logoFromGetLogo?.Length ?? 0)}B");

                                byte[]? chosen = PickBetterLogo(pkg.Id.Name, displayName,
                                    logoFromStandard, logoFromGetLogo);

                                if (chosen != null)
                                {
                                    localIcon = SaveBytesToCache(chosen, aumid);
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[图标] {pkg.Id.Name} 两个源都是空");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[图标] 提取失败 {pkg.Id.Name}: {ex.Message}");
                            }

                            results.Add(new InstalledProgramInfo
                            {
                                DisplayName = displayName.Trim(),
                                TargetPath = $"shell:AppsFolder\\{aumid}",
                                Publisher = pkg.PublisherDisplayName,
                                Version = $"{pkg.Id.Version.Major}.{pkg.Id.Version.Minor}.{pkg.Id.Version.Build}.{pkg.Id.Version.Revision}",
                                LocalIconPath = localIcon,
                                Source = ProgramSource.UwpApp
                            });
                            accepted++;
                        }
                    }
                    catch { }
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[UWP图标] 汇总: 接受={accepted}, 图标成功={logoOk}, 图标失败={logoFail}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UWP] 整体失败: {ex}");
            }
        }
        
        /// <summary>读包目录里的标准 logo，返回字节；找不到返回 null。</summary>
        private static byte[]? TryReadStandardLogoBytes(string installPath)
        {
            try
            {
                string? path = FindStandardLogo(installPath);
                if (path == null) return null;
                return File.ReadAllBytes(path);
            }
            catch { return null; }
        }

        /// <summary>读 GetLogo 的字节；失败返回 null。</summary>
        private static byte[]? TryReadGetLogoBytes(AppListEntry entry)
        {
            try
            {
                var logoRef = entry.DisplayInfo.GetLogo(new Size(300, 300));
                if (logoRef == null) return null;

                using var stream = logoRef.OpenReadAsync().AsTask().GetAwaiter().GetResult();
                using var netStream = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                netStream.CopyTo(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        /// <summary>
        /// 两个候选里选"非透明图案占图像比例大"的那个。
        /// targetsize 变体因为图案只在角落，占比会很小，会被淘汰。
        /// </summary>
        private static byte[]? PickBetterLogo(
    string packageName, string appName, byte[]? a, byte[]? b)
        {
            // ① getLogo 来自系统 API，优先使用（除非它是全透明空图）
            if (b != null && !IsFullyTransparent(b))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[图标] {appName}: 采用 getLogo（{b.Length} bytes）");
                return b;
            }

            // ② 白名单：只在 standard 确实不是占位图时才强制用
            if (_forceUseStandardLogoPackages.Contains(packageName)
                && a != null && !IsPlaceholderLogo(a))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[图标] 包名白名单命中 {packageName} → standard（{a.Length} bytes）");
                return a;
            }

            // ③ 常规兜底
            if (a == null) return b;
            if (b == null) return a;

            bool aIsPlaceholder = IsPlaceholderLogo(a);
            bool bIsPlaceholder = IsPlaceholderLogo(b);

            if (aIsPlaceholder && !bIsPlaceholder) return b;
            if (bIsPlaceholder && !aIsPlaceholder) return a;

            double ratioA = GetOpaqueRatio(a);
            double ratioB = GetOpaqueRatio(b);

            System.Diagnostics.Debug.WriteLine(
                $"[图标] 占比比较 {appName}: standard={ratioA:P1}(占位={aIsPlaceholder}), " +
                $"getLogo={ratioB:P1}(占位={bIsPlaceholder})");

            return ratioA >= ratioB ? a : b;
        }
        /// <summary>判断图片是否全透明（没有任何可见像素）。</summary>
        private static bool IsFullyTransparent(byte[] png)
        {
            try
            {
                using var ms = new MemoryStream(png);
                using var bmp = new System.Drawing.Bitmap(ms);

                int step = Math.Max(1, bmp.Width / 32);
                for (int y = 0; y < bmp.Height; y += step)
                    for (int x = 0; x < bmp.Width; x += step)
                        if (bmp.GetPixel(x, y).A > 10)
                            return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// 判断是否是 UWP 的默认占位图（灰色 X）。
        /// 特征：非透明像素中，绝大多数是灰色（R≈G≈B）。
        /// </summary>
        private static bool IsPlaceholderLogo(byte[] png)
        {
            try
            {
                using var ms = new MemoryStream(png);
                using var bmp = new System.Drawing.Bitmap(ms);

                int opaqueCount = 0;
                int grayCount = 0;
                int step = Math.Max(1, bmp.Width / 32);   // 采样 32×32 网格

                for (int y = 0; y < bmp.Height; y += step)
                {
                    for (int x = 0; x < bmp.Width; x += step)
                    {
                        var c = bmp.GetPixel(x, y);
                        if (c.A < 10) continue;   // 跳过透明像素
                        opaqueCount++;

                        int dr = Math.Abs(c.R - c.G);
                        int dg = Math.Abs(c.G - c.B);
                        int db = Math.Abs(c.R - c.B);
                        int maxDiff = Math.Max(dr, Math.Max(dg, db));

                        // R/G/B 三通道差异小于 20 → 判定为灰
                        if (maxDiff < 20) grayCount++;
                    }
                }

                if (opaqueCount == 0) return true;   // 全透明也是坏图

                double grayRatio = (double)grayCount / opaqueCount;

                // 超过 85% 是灰色 → 判定为占位图
                bool isPlaceholder = grayRatio > 0.85;
                if (isPlaceholder)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[图标] 检测到占位图: 灰度比例={grayRatio:P0}");
                }
                return isPlaceholder;
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// 计算图像非透明像素占"整个图像面积"的比例。
        /// 越接近 1 → 图案越占满。
        /// </summary>
        private static double GetOpaqueRatio(byte[] png)
        {
            try
            {
                using var ms = new MemoryStream(png);
                using var bmp = new System.Drawing.Bitmap(ms);

                int opaque = 0;
                // 采样步长（4 像素采 1 个），大图不用全扫
                int step = Math.Max(1, bmp.Width / 64);

                for (int y = 0; y < bmp.Height; y += step)
                    for (int x = 0; x < bmp.Width; x += step)
                        if (bmp.GetPixel(x, y).A > 10) opaque++;

                int totalSamples = ((bmp.Height + step - 1) / step)
                                 * ((bmp.Width + step - 1) / step);

                return totalSamples == 0 ? 0 : (double)opaque / totalSamples;
            }
            catch
            {
                return 0;
            }
        }
        /// <summary>把 UWP logo 保存到本地缓存</summary>
        private static string? SaveUwpLogo(RandomAccessStreamReference logoRef, string aumid)
        {
            string filePath = "";
            try
            {
                string baseDir = BlockBox.Helpers.AppDataPaths.Root;
                string cacheDir = Path.Combine(baseDir, "icon-cache", "uwp");
                Directory.CreateDirectory(cacheDir);

                byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(aumid));
                string safeName = Convert.ToHexString(hash)[..16];
                filePath = Path.Combine(cacheDir, safeName + ".png");

                if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                    return filePath;

                // ⭐ 关键：用同步方式读流
                using var stream = logoRef.OpenReadAsync().AsTask().GetAwaiter().GetResult();
                using var netStream = stream.AsStreamForRead();
                using var fileStream = File.Create(filePath);
                netStream.CopyTo(fileStream);
                fileStream.Flush();

                long size = new FileInfo(filePath).Length;
                System.Diagnostics.Debug.WriteLine($"[图标] OK {aumid} → {size} bytes");
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[图标] 保存失败 {aumid}: {ex.GetType().Name} {ex.Message}");
                try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
                return null;
            }
        }

        

        public static List<InstalledProgramInfo> ScanAll(ProgramFilter? filter = null)
            => ScanAll(filter, out _);

        public static List<InstalledProgramInfo> ScanAll(ProgramFilter? filter, out ScanStats stats)
        {
            stats = new ScanStats();
            var results = new List<InstalledProgramInfo>();

            System.Diagnostics.Debug.WriteLine(
                $"[程序扫描] 开始; User={Environment.UserDomainName}\\{Environment.UserName}; Base={AppContext.BaseDirectory}; Is64Bit={Environment.Is64BitProcess}");

            ScanRegistry(results, filter, stats);
            ScanShortcutFolders(results, filter);
            ScanAppPaths(results, filter);
            //ScanStoreApps(results, filter);
            ScanUwpApps(results, filter);

            // ⭐ 解析真实目标 → 过滤非可执行 → 按目标去重
            var valid = new List<(InstalledProgramInfo Info, string RealTarget)>();
            int emptyTargetCount = 0;
            int invalidExtensionCount = 0;
            int missingTargetCount = 0;            foreach (var p in results)
            {
                if (string.IsNullOrWhiteSpace(p.TargetPath))
                {
                    emptyTargetCount++;
                    System.Diagnostics.Debug.WriteLine(
                        $"[程序扫描] 忽略空目标; Name={p.DisplayName}; Source={p.Source}");
                    continue;
                }

                string originalTargetPath = p.TargetPath;
                bool isScannedShortcut = (p.Source is ProgramSource.StartMenuUser or ProgramSource.StartMenuCommon
                    or ProgramSource.DesktopUser or ProgramSource.DesktopCommon)
                    && (Path.GetExtension(originalTargetPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(originalTargetPath).Equals(".url", StringComparison.OrdinalIgnoreCase));
                string? realTarget = NormalizeTargetPath(originalTargetPath);
                if (string.IsNullOrWhiteSpace(realTarget))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[程序扫描] 忽略未解析快捷方式; Name={p.DisplayName}; Source={p.Source}; Original={originalTargetPath}; Resolved=<null>");
                    continue;
                }

                if (Path.GetExtension(realTarget).Equals(".url", StringComparison.OrdinalIgnoreCase)
                    && !IsSteamInternetShortcut(realTarget))
                    continue;

                if (!IsLaunchableTargetExtension(realTarget) && !IsShortcutPath(realTarget))
                {
                    invalidExtensionCount++;
                    System.Diagnostics.Debug.WriteLine(
                        $"[程序扫描] 忽略不支持扩展名; Name={p.DisplayName}; Source={p.Source}; Target={realTarget}; Extension={Path.GetExtension(realTarget)}");
                    continue;
                }

                bool isUwpTarget = realTarget.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase);
                bool targetExists = isUwpTarget || File.Exists(realTarget);
                bool shortcutExists = isScannedShortcut && File.Exists(originalTargetPath);
                bool useShortcutFallback = !targetExists && shortcutExists;
                string storedTarget = useShortcutFallback ? originalTargetPath : realTarget;
                bool exists = targetExists || useShortcutFallback;
                System.Diagnostics.Debug.WriteLine(
                    $"[程序扫描] 目标探测; Name={p.DisplayName}; Source={p.Source}; Shortcut={originalTargetPath}; Resolved={realTarget}; TargetExists={targetExists}; ShortcutExists={shortcutExists}; UseShortcutFallback={useShortcutFallback}");
                if (!exists)
                {
                    missingTargetCount++;
                    string fullPath;
                    try { fullPath = Path.GetFullPath(realTarget); }
                    catch (Exception ex) { fullPath = $"<解析失败:{ex.GetType().Name}:{ex.Message}>"; }
                    System.Diagnostics.Debug.WriteLine(
                        $"[程序扫描] 忽略目标不存在的记录; Name={p.DisplayName}; Source={p.Source}; Shortcut={originalTargetPath}; Resolved={realTarget}; FullPath={fullPath}; CurrentDirectory={Environment.CurrentDirectory}; User={Environment.UserDomainName}\\{Environment.UserName}; Base={AppContext.BaseDirectory}");
                    continue;
                }

                p.ResolvedTargetPath = useShortcutFallback ? realTarget : null;
                if (isScannedShortcut)
                {
                    p.PreferredIconSourcePath = originalTargetPath;
                    var shortcutInfo = ResolveShortcutInfo(originalTargetPath);
                    if (shortcutInfo != null)
                    {
                        p.Arguments = shortcutInfo.Arguments;
                        p.WorkingDirectory = shortcutInfo.WorkingDirectory;
                    }
                }

                // 快捷方式记录保留 .lnk 路径（启动时能带上参数/工作目录），其余用解析后的真实目标。
                p.TargetPath = isScannedShortcut ? originalTargetPath : storedTarget;
                // .url 的 realTarget 仍是 .url 路径，身份要按里面的 URL 计算，才能和数据库去重。
                p.Identity = Path.GetExtension(originalTargetPath).Equals(".url", StringComparison.OrdinalIgnoreCase)
                    ? ProgramIdentity.Get(originalTargetPath)
                    : BuildIdentity(realTarget, p.Arguments);
                valid.Add((p, realTarget));
            }

            stats.EmptyTarget = emptyTargetCount;
            stats.InvalidExtension = invalidExtensionCount;
            stats.MissingTarget = missingTargetCount;

            valid = RemoveVersionIndependentApplicationLaunchers(valid);
            valid = PreferUrlEntriesWithSameName(valid);

            // WindowsApps 包内的 exe 不作为独立卡片：统一由 UWP 扫描提供 shell:AppsFolder AUMID 入口，
            // 避免 Apple Music 这类商店应用同时出现 exe 和 AUMID 两张卡。
            int windowsAppsExecutables = valid.RemoveAll(item =>
                ProgramIdentity.TryGetUwpIdentity(item.RealTarget, out _)
                && item.Info.Source is not (ProgramSource.UwpApp or ProgramSource.StoreApp));
            if (windowsAppsExecutables > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[程序扫描] 忽略 WindowsApps 包内 exe 候选: {windowsAppsExecutables}");
            }

            // 快捷方式优先：同一 exe 已有快捷方式时，注册表 / App Paths 的裸 exe 记录丢弃。
            var shortcutExeKeys = valid
                .Where(item => IsShortcutSource(item.Info.Source))
                .Select(item => ProgramIdentity.ResolveTarget(item.RealTarget))
                .ToHashSet(StringComparer.Ordinal);

            valid = valid
                .Where(item => IsShortcutSource(item.Info.Source)
                    || !shortcutExeKeys.Contains(ProgramIdentity.ResolveTarget(item.RealTarget)))
                .ToList();

            // 先按“真实目标”分组（同一个 exe），再处理启动参数：
            // · 参数只是启动入口/最小化之类（/run:desktop、/run:winmenu…）→ 只保留桌面/完整客户端一条；
            // · 参数有实际差异（比如 Clash 的不同模式）→ 都保留，并在名称后加参数区分。
            var grouped = valid.GroupBy(x => ProgramIdentity.ResolveTarget(x.RealTarget), StringComparer.Ordinal);
            var final = new List<InstalledProgramInfo>();
            int duplicateCount = 0;

            foreach (var group in grouped)
            {
                var entries = group
                    .GroupBy(x => (x.Info.Arguments ?? "").Trim(), StringComparer.Ordinal)
                    .Select(g => g.OrderByDescending(x => SourcePriority(x.Info.Source))
                        .ThenByDescending(x => (x.Info.DisplayName ?? "").Length)
                        .First())
                    .ToList();

                duplicateCount += group.Count() - entries.Count;

                var meaningful = entries
                    .Where(x => !IsBenignLaunchArgument(x.Info.Arguments))
                    .ToList();

                if (meaningful.Count == 0)
                {
                    // 都是入口/最小化参数：优先桌面（完整客户端），其次按来源优先级。
                    var desktopEntry = entries.FirstOrDefault(x =>
                        (x.Info.Arguments ?? "").IndexOf("desktop", StringComparison.OrdinalIgnoreCase) >= 0);
                    var chosen = desktopEntry.Info != null
                        ? desktopEntry
                        : entries.OrderByDescending(x => SourcePriority(x.Info.Source)).First();

                    duplicateCount += entries.Count - 1;
                    final.Add(MaterializeProgram(chosen, group));
                    continue;
                }

                duplicateCount += entries.Count - meaningful.Count;

                var nameCounts = meaningful
                    .GroupBy(x => (x.Info.DisplayName ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                foreach (var entry in meaningful)
                {
                    var program = MaterializeProgram(entry, group);
                    string name = (program.DisplayName ?? "").Trim();
                    if (nameCounts.TryGetValue(name, out int count) && count > 1)
                        program.DisplayName = BuildVariantDisplayName(name, entry.Info.Arguments);
                    final.Add(program);
                }
            }

            stats.DuplicatesRemoved = duplicateCount;

            System.Diagnostics.Debug.WriteLine(
                $"[程序扫描] 完成; 原始记录={results.Count}; 有效记录={valid.Count}; 去重后={final.Count}; 空目标={emptyTargetCount}; 扩展名不支持={invalidExtensionCount}; 文件不存在={missingTargetCount}; 重复={duplicateCount}; 无法启动={stats.Unlaunchable}");

            return final
                .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static List<(InstalledProgramInfo Info, string RealTarget)>
            RemoveVersionIndependentApplicationLaunchers(
                List<(InstalledProgramInfo Info, string RealTarget)> valid)
        {
            var filtered = valid
                .Where(item => !IsVersionIndependentApplicationLauncher(
                    item))
                .ToList();

            int removed = valid.Count - filtered.Count;
            if (removed > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[程序扫描] 已忽略被版本目录替代的 Application 启动器: {removed}");
            }

            return filtered;
        }

        private static List<(InstalledProgramInfo Info, string RealTarget)>
            PreferUrlEntriesWithSameName(
                List<(InstalledProgramInfo Info, string RealTarget)> valid)
        {
            var urlNames = valid
                .Where(item => Path.GetExtension(item.Info.TargetPath)
                    .Equals(".url", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Info.DisplayName?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

            if (urlNames.Count == 0)
                return valid;

            var filtered = valid
                .Where(item =>
                {
                    string extension = Path.GetExtension(item.Info.TargetPath);
                    bool isUrl = extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
                    if (isUrl)
                        return true;

                    bool isExeOrLnk = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
                    return !isExeOrLnk
                        || !urlNames.Contains(item.Info.DisplayName?.Trim() ?? string.Empty);
                })
                .ToList();

            int removed = valid.Count - filtered.Count;
            if (removed > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[程序扫描] 同名 URL 优先，已忽略 EXE/LNK: {removed}");
            }

            return filtered;
        }

        private static bool IsVersionIndependentApplicationLauncher(
            (InstalledProgramInfo Info, string RealTarget) item)
        {
            string targetPath = item.RealTarget;
            string? applicationDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(applicationDirectory)
                || !string.Equals(Path.GetFileName(applicationDirectory), "Application",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var versionDirectories = Directory.GetDirectories(applicationDirectory)
                    .Where(directory => Version.TryParse(
                        Path.GetFileName(directory), out _))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (versionDirectories.Count == 0)
                    return false;

                return versionDirectories.Any(directory =>
                    Directory.Exists(directory)
                    && Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly).Length > 0);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsVersionedApplicationExecutable(string targetPath)
        {
            string? versionDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(versionDirectory)
                || !Version.TryParse(Path.GetFileName(versionDirectory), out _))
                return false;

            string? applicationDirectory = Path.GetDirectoryName(versionDirectory);
            return !string.IsNullOrWhiteSpace(applicationDirectory)
                && string.Equals(Path.GetFileName(applicationDirectory), "Application",
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 从一组"指向同一文件"的条目里挑一条最有代表性的。
        /// 优先级：
        ///   1. 注册表来源优先（信息更全：Version / Publisher）
        ///   2. 同来源里，DisplayName 更长优先（"Google Chrome" &gt; "Chrome"）
        ///   3. 再不行取第一条
        /// </summary>
        private static InstalledProgramInfo PickBestEntry(List<InstalledProgramInfo> list)
            => list.OrderByDescending(p => SourcePriority(p.Source))
                .ThenByDescending(p => (p.DisplayName ?? "").Length)
                .First();

        /// <summary>
        /// 把组内选定的条目整理成最终记录：快捷方式保留 .lnk（带参数），否则用解析后的真实目标。
        /// </summary>
        private static InstalledProgramInfo MaterializeProgram(
            (InstalledProgramInfo Info, string RealTarget) entry,
            IEnumerable<(InstalledProgramInfo Info, string RealTarget)> iconSourceGroup)
        {
            var info = entry.Info;
            info.TargetPath = IsShortcutSource(info.Source) ? info.TargetPath : entry.RealTarget;
            info.ResolvedTargetPath ??= entry.RealTarget;
            info.PreferredIconSourcePath = iconSourceGroup
                .Where(x => IsShortcutSource(x.Info.Source))
                .OrderByDescending(x => SourcePriority(x.Info.Source))
                .Select(x => x.Info.PreferredIconSourcePath)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            return info;
        }

        /// <summary>同 exe 的不同参数变体保留时，在名称后加参数，避免用户看到两个一模一样的名字。</summary>
        private static string BuildVariantDisplayName(string name, string arguments)
        {
            string suffix = (arguments ?? "").Trim();
            if (suffix.Length > 30) suffix = suffix[..30] + "…";
            return $"{name} ({suffix})";
        }

        /// <summary>
        /// 没有实际含义的启动参数：空参数、安装器的 /run:* 入口、最小化/托盘/后台启动等。
        /// 这类只保留一条（优先桌面/完整客户端）。
        /// </summary>
        public static bool IsBenignLaunchArgument(string? arguments)
        {
            string value = (arguments ?? "").Trim();
            if (value.Length == 0) return true;

            string lower = value.ToLowerInvariant();
            if (lower.StartsWith("/run:", StringComparison.Ordinal)
                || lower.StartsWith("-run:", StringComparison.Ordinal)
                || lower.StartsWith("--run:", StringComparison.Ordinal))
                return true;

            return lower is "--minimized" or "-minimized" or "/minimized"
                or "--min" or "-min" or "/min"
                or "--tray" or "-tray" or "/tray"
                or "--background" or "-background" or "/background";
        }

        private static bool IsShortcutSource(ProgramSource source)
            => source is ProgramSource.StartMenuUser or ProgramSource.StartMenuCommon
                or ProgramSource.DesktopUser or ProgramSource.DesktopCommon;

        private static int SourcePriority(ProgramSource source) => source switch
        {
            ProgramSource.StartMenuUser => 100,
            ProgramSource.StartMenuCommon => 95,
            ProgramSource.DesktopUser => 90,
            ProgramSource.DesktopCommon => 85,
            ProgramSource.RegistryCurrentUser => 55,
            ProgramSource.Registry64 => 50,
            ProgramSource.Registry32 => 45,
            ProgramSource.AppPath => 40,
            ProgramSource.UwpApp => 200,
            ProgramSource.StoreApp => 190,
            _ => 10
        };

        private static string NormalizeIdentityPath(string path)
            => ProgramIdentity.ResolveTarget(path);

        private static string BuildIdentity(string realTarget, string arguments)
            => ProgramIdentity.Get(realTarget, arguments);

        // ==================== 注册表 ====================

        private static void ScanRegistry(List<InstalledProgramInfo> results, ProgramFilter? filter, ScanStats stats)
        {
            const string uninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

            using (var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = hklm64.OpenSubKey(uninstallKey))
            {
                ReadSubKeys(key, results, ProgramSource.Registry64, filter, stats);
            }

            using (var hklm32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (var key = hklm32.OpenSubKey(uninstallKey))
            {
                ReadSubKeys(key, results, ProgramSource.Registry32, filter, stats);
            }

            using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
            using (var key = hkcu.OpenSubKey(uninstallKey))
            {
                ReadSubKeys(key, results, ProgramSource.RegistryCurrentUser, filter, stats);
            }
        }

        /// <summary>
        /// App Paths：应用主动注册的可启动 exe，用来补充“已安装但没有开始菜单/桌面快捷方式”的程序。
        /// </summary>
        private static void ScanAppPaths(List<InstalledProgramInfo> results, ProgramFilter? filter)
        {
            const string appPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

            AddFromHive(RegistryHive.LocalMachine, RegistryView.Registry64);
            AddFromHive(RegistryHive.LocalMachine, RegistryView.Registry32);
            AddFromHive(RegistryHive.CurrentUser, RegistryView.Default);

            void AddFromHive(RegistryHive hive, RegistryView view)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var root = baseKey.OpenSubKey(appPathsKey);
                    if (root == null) return;

                    foreach (string subName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var sub = root.OpenSubKey(subName);
                            if (sub?.GetValue(null) is not string raw || string.IsNullOrWhiteSpace(raw))
                                continue;

                            string exePath = raw.Trim().Trim('"').Trim();
                            if (!File.Exists(exePath) || !IsLaunchableExtension(exePath))
                                continue;
                            if (IsInExcludedPath(exePath))
                                continue;

                            string name = Path.GetFileNameWithoutExtension(subName);
                            if (string.IsNullOrWhiteSpace(name))
                                name = Path.GetFileNameWithoutExtension(exePath);
                            if (string.IsNullOrWhiteSpace(name))
                                continue;
                            if (filter?.ShouldExclude(name) == true)
                                continue;
                            if (IsWindowsSystemItem(name))
                                continue;

                            results.Add(new InstalledProgramInfo
                            {
                                DisplayName = name,
                                TargetPath = exePath,
                                Source = ProgramSource.AppPath,
                                InstallLocation = sub.GetValue("Path") as string
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private static void ReadSubKeys(
            RegistryKey? parent,
            List<InstalledProgramInfo> results,
            ProgramSource source,
            ProgramFilter? filter,
            ScanStats stats)
        {
            if (parent == null) return;

            foreach (var subName in parent.GetSubKeyNames())
            {
                try
                {
                    using var sub = parent.OpenSubKey(subName);
                    if (sub == null) continue;

                    // 系统更新 / 补丁
                    if (sub.GetValue("SystemComponent") is int sys && sys == 1) continue;
                    if (sub.GetValue("ParentKeyName") is string pk && !string.IsNullOrEmpty(pk)) continue;
                    if (sub.GetValue("ReleaseType") is string rt
                        && (rt.Contains("Update", StringComparison.OrdinalIgnoreCase)
                            || rt.Contains("Hotfix", StringComparison.OrdinalIgnoreCase))) continue;

                    string? displayName = sub.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    // ⭐ 用户配置的过滤器
                    if (filter?.ShouldExclude(displayName) == true) continue;

                    // ⭐ Windows 系统工具
                    if (IsWindowsSystemItem(displayName)) continue;

                    string? exePath = ResolveExecutablePath(sub);
                    if (string.IsNullOrWhiteSpace(exePath))
                    {
                        stats.Unlaunchable++;
                        if (stats.UnlaunchableNames.Count < 100)
                            stats.UnlaunchableNames.Add(displayName.Trim());
                        System.Diagnostics.Debug.WriteLine(
                            $"[程序扫描] 无法定位启动程序; Name={displayName}; Source={source}; InstallLocation={sub.GetValue("InstallLocation")}");
                        continue;
                    }
                    // ⭐ 新增：排除系统缓存目录
                    if (IsInExcludedPath(exePath)) continue;

                    // exe 名也过滤一次
                    if (filter?.ShouldExclude(Path.GetFileNameWithoutExtension(exePath)) == true) continue;

                    results.Add(new InstalledProgramInfo
                    {
                        DisplayName = displayName.Trim(),
                        TargetPath = exePath,
                        InstallLocation = sub.GetValue("InstallLocation") as string,
                        Version = sub.GetValue("DisplayVersion") as string,
                        Publisher = sub.GetValue("Publisher") as string,
                        Source = source
                    });
                }
                catch { }
            }
        }

        private static string? ResolveExecutablePath(RegistryKey sub)
        {
            // 1. DisplayIcon：只接受可执行扩展名
            if (sub.GetValue("DisplayIcon") is string displayIcon && !string.IsNullOrWhiteSpace(displayIcon))
            {
                string cleaned = displayIcon.Trim('"');
                int comma = cleaned.LastIndexOf(',');
                if (comma > 0 && int.TryParse(cleaned[(comma + 1)..], out _))
                    cleaned = cleaned[..comma];

                cleaned = cleaned.Trim('"').Trim();
                if (File.Exists(cleaned)
             && IsLaunchableExtension(cleaned)
             && !IsInExcludedPath(cleaned))       // ⭐
                    return cleaned;

            }

            // 2. InstallLocation 下的 exe
            if (sub.GetValue("InstallLocation") is string loc && Directory.Exists(loc))
            {// ⭐ 整个 InstallLocation 在排除目录里，直接跳过
                if (IsInExcludedPath(loc + "\\"))
                    return null;

                try
                {
                    var exes = Directory.GetFiles(loc, "*.exe", SearchOption.TopDirectoryOnly)
                        .Where(path => !IsLikelyNonMainExecutable(path))
                        .ToList();
                    if (exes.Count == 1) return exes[0];

                    string? displayName = sub.GetValue("DisplayName") as string;
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        string simple = new string(displayName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                        if (simple.Length > 0)
                        {
                            var exact = exes.FirstOrDefault(e =>
                                Path.GetFileNameWithoutExtension(e).Equals(simple, StringComparison.OrdinalIgnoreCase));
                            if (exact != null) return exact;

                            var best = exes.FirstOrDefault(e =>
                            {
                                string exeName = Path.GetFileNameWithoutExtension(e).ToLowerInvariant();
                                return exeName.Contains(simple) || simple.Contains(exeName);
                            });
                            if (best != null) return best;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        // ⭐ 新增：只允许这些扩展名作为启动路径
        private static bool IsLaunchableExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".exe" || ext == ".lnk" || ext == ".bat" || ext == ".cmd"
                || ext == ".com" || ext == ".msc" || ext == ".cpl"
                || ext == ".ps1" || ext == ".jar" || ext == ".appref-ms";
        }

        /// <summary>明显不是主程序的 exe（卸载器、安装器、更新器、崩溃报告等）。</summary>
        private static bool IsLikelyNonMainExecutable(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return name.StartsWith("unins") || name.StartsWith("uninstall")
                || name.StartsWith("setup") || name.StartsWith("update") || name.StartsWith("updater")
                || name.StartsWith("crash") || name.StartsWith("report") || name.StartsWith("helper")
                || name.StartsWith("vcredist") || name.StartsWith("dxsetup") || name.StartsWith("install")
                || name.StartsWith("nsis");
        }

        // ==================== 开始菜单与桌面快捷方式 ====================

        private static bool IsSteamInternetShortcut(string shortcutPath)
        {
            try
            {
                bool inInternetShortcutSection = false;
                foreach (string rawLine in File.ReadLines(shortcutPath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                        continue;

                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        inInternetShortcutSection = line[1..^1].Trim().Equals(
                            "InternetShortcut", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if (!inInternetShortcutSection)
                        continue;

                    int separator = line.IndexOf('=');
                    if (separator <= 0
                        || !line[..separator].Trim().Equals("URL", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string value = line[(separator + 1)..].Trim().Trim('"');
                    return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                        && uri.Scheme.Equals("steam", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[程序扫描] Steam URL 快捷方式读取失败; Path={shortcutPath}; Error={ex.Message}");
            }

            return false;
        }

        private static void ScanShortcutFolders(List<InstalledProgramInfo> results, ProgramFilter? filter)
        {
            var folders = new (string Path, ProgramSource Source)[]
            {
                (Environment.GetFolderPath(Environment.SpecialFolder.Programs),      ProgramSource.StartMenuUser),
                (Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ProgramSource.StartMenuCommon),
                (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProgramSource.DesktopUser),
                (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ProgramSource.DesktopCommon),
            };

            foreach (var (folder, source) in folders)
            {
                if (!Directory.Exists(folder)) continue;

                try
                {
                    var enumeration = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = 0
                    };

                    // 开始菜单和桌面都同时枚举 .lnk / .url；桌面以前只扫 .url，导致桌面快捷方式全部漏掉。
                    IEnumerable<string> shortcutPaths = Directory.EnumerateFiles(folder, "*.lnk", enumeration)
                        .Concat(Directory.EnumerateFiles(folder, "*.url", enumeration));

                    foreach (var shortcutPath in shortcutPaths)
                    {
                        try
                        {
                            string name = Path.GetFileNameWithoutExtension(shortcutPath);
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            // ⭐ 新增：整个文件夹排除
                            if (IsInExcludedStartMenuFolder(shortcutPath)) continue;
                            // ⭐ 用户配置的过滤器
                            if (filter?.ShouldExclude(name) == true) continue;

                            if (Path.GetExtension(shortcutPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                            {
                                string? targetPath = ResolveShortcutTarget(shortcutPath);
                                string targetName = Path.GetFileNameWithoutExtension(targetPath);
                                if (filter?.ShouldExclude(targetName) == true) continue;
                            }

                            // ⭐ Windows 系统工具
                            if (IsWindowsSystemItem(name)) continue;

                            results.Add(new InstalledProgramInfo
                            {
                                DisplayName = name,
                                TargetPath = shortcutPath,
                                Source = source
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // ==================== .lnk 解析 ====================

        private const int MaxLnkDepth = 10;

        private static readonly Dictionary<string, string?> _normalizeCache
    = new(StringComparer.OrdinalIgnoreCase);

        private static bool IsShortcutPath(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".Ink", StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeTargetPath(string path)
        { // ⭐ UWP 路径原样返回
            if (path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                return path;

            if (!IsShortcutPath(path))
                return path;
            if (_normalizeCache.TryGetValue(path, out var cached))
                return cached;

            string? result = NormalizeTargetPathInternal(path);
            _normalizeCache[path] = result;
            return result;
        }


        private static string? NormalizeTargetPathInternal(string path)
        {
            if (!IsShortcutPath(path))
                return path;

            // ⭐ 防止 A → B → A 循环
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string current = path;
            for (int depth = 0; depth < MaxLnkDepth; depth++)
            {
                if (!IsShortcutPath(current))
                    return current;

                if (!visited.Add(current))
                    return null;

                string? next = ResolveLnkOnce(current)?.TargetPath;
                if (string.IsNullOrEmpty(next) || string.Equals(next, current, StringComparison.OrdinalIgnoreCase))
                    return null;

                current = next;
            }

            return IsShortcutPath(current) ? null : current;
        }

        /// <summary>解析一层 .lnk，失败返回 null</summary>
        private static ShortcutInfo? ResolveLnkOnce(string lnkPath)
        {
            object? shellObj = null;
            object? shortcutObj = null;
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return null;

                shellObj = Activator.CreateInstance(shellType);
                if (shellObj == null) return null;

                dynamic shell = shellObj;
                shortcutObj = shell.CreateShortcut(lnkPath);
                dynamic shortcut = shortcutObj;

                string target = (string)shortcut.TargetPath;
                if (string.IsNullOrWhiteSpace(target)) return null;

                string arguments = "";
                string workingDirectory = "";
                try { arguments = (string)shortcut.Arguments ?? ""; } catch { }
                try { workingDirectory = (string)shortcut.WorkingDirectory ?? ""; } catch { }

                return new ShortcutInfo
                {
                    TargetPath = target,
                    Arguments = arguments.Trim(),
                    WorkingDirectory = workingDirectory.Trim()
                };
            }
            catch
            {
                return null;
            }
            finally
            {
                if (shortcutObj != null)
                    try { Marshal.ReleaseComObject(shortcutObj); } catch { }
                if (shellObj != null)
                    try { Marshal.ReleaseComObject(shellObj); } catch { }
            }
        }

        public static ShortcutInfo? ResolveShortcutInfo(string shortcutPath)
        {
            if (string.IsNullOrWhiteSpace(shortcutPath)) return null;

            string extension = Path.GetExtension(shortcutPath);
            if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".Ink", StringComparison.OrdinalIgnoreCase))
                return null;

            return ResolveLnkOnce(shortcutPath);
        }

        public static string? ResolveShortcutTarget(string shortcutPath)
            => ResolveShortcutInfo(shortcutPath)?.TargetPath;

        public static string? FindMatchingStartMenuShortcut(string shortcutPath, string displayName)
        {
            var expectedNames = new[]
            {
                Path.GetFileNameWithoutExtension(shortcutPath),
                displayName
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizeShortcutName)
            .Where(name => name.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            string inputName = NormalizeShortcutName(Path.GetFileNameWithoutExtension(shortcutPath));
            var candidates = new List<(string Path, string Name, int Score)>();

            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            };

            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder))
                    continue;

                try
                {
                    foreach (string candidate in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
                    {
                        string candidateName = NormalizeShortcutName(Path.GetFileNameWithoutExtension(candidate));
                        int score = expectedNames
                            .Where(expected => candidateName.Equals(expected, StringComparison.OrdinalIgnoreCase))
                            .Select(_ => 1000)
                            .DefaultIfEmpty(0)
                            .Max();

                        if (score == 0 && inputName.Length >= 8
                            && candidateName.Contains(inputName, StringComparison.OrdinalIgnoreCase))
                            score = 800;

                        if (score == 0 && expectedNames.Any(expected => expected.Length >= 8
                            && (candidateName.Contains(expected, StringComparison.OrdinalIgnoreCase)
                                || expected.Contains(candidateName, StringComparison.OrdinalIgnoreCase))))
                            score = 600;

                        if (score == 0)
                            continue;

                        if (string.IsNullOrWhiteSpace(ResolveShortcutTarget(candidate)))
                            continue;

                        candidates.Add((candidate, candidateName, score));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[启动] 搜索开始菜单快捷方式失败; Folder={folder}; Error={ex.Message}");
                }
            }

            var bestCandidates = candidates
                .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(candidate => candidate.Score)
                .ToList();

            if (bestCandidates.Count == 0
                || bestCandidates.Count > 1 && bestCandidates[0].Score == bestCandidates[1].Score)
                return null;

            System.Diagnostics.Debug.WriteLine(
                $"[启动] 通过开始菜单名称匹配恢复快捷方式; Input={shortcutPath}; Name={displayName}; Match={bestCandidates[0].Path}; Score={bestCandidates[0].Score}");
            return bestCandidates[0].Path;
        }

        private static string NormalizeShortcutName(string name)
        {
            return new string(name.Where(char.IsLetterOrDigit).ToArray());
        }
    }
}