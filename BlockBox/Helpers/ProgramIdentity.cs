using System;
using System.Collections.Concurrent;
using System.IO;

namespace BlockBox.Helpers
{
    /// <summary>
    /// 程序身份：把 .lnk / .url / exe / UWP 路径统一成同一个键，用于扫描与手动添加去重。
    /// 身份 = 解析后的真实目标（小写、去尾斜杠）+ 启动参数；
    /// UWP/商店包按包家族名归一，根目录启动器与版本目录 exe 归一。
    /// </summary>
    internal static class ProgramIdentity
    {
        // 扫描时会对数据库里几百条 .lnk 反复求身份，缓存解析结果避免重复走 COM。
        private static readonly ConcurrentDictionary<string, (long Ticks, string Target)> ResolveCache
            = new(StringComparer.OrdinalIgnoreCase);

        public static string Get(string targetPath, string? arguments = null)
        {
            string target = ResolveTarget(targetPath);
            string args = (arguments ?? string.Empty).Trim();
            return args.Length == 0 ? target : target + "|" + args;
        }

        public static string ResolveTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            string value = path.Trim().Trim('"');

            // UWP 的 AUMID 直接按包家族名归一。
            if (TryGetUwpIdentity(value, out string uwpIdentity))
                return uwpIdentity;

            string extension = Path.GetExtension(value);
            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                value = ResolveLnkTarget(value);
                // 快捷方式可能指向 WindowsApps 包内的 exe，也要归到包身份。
                if (TryGetUwpIdentity(value, out uwpIdentity))
                    return uwpIdentity;
            }
            else if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                string? url = TryReadInternetShortcutUrl(value);
                if (!string.IsNullOrWhiteSpace(url)) value = url;
            }

            value = NormalizeVersionedExecutablePath(value);
            return NormalizePath(value);
        }

        /// <summary>
        /// UWP/商店包：AUMID（shell:AppsFolder\...!App）和 WindowsApps 包内的 exe
        /// 都映射到同一个 "uwp:包家族名" 身份。
        /// </summary>
        public static bool TryGetUwpIdentity(string path, out string identity)
        {
            identity = string.Empty;
            string value = (path ?? string.Empty).Trim().Trim('"');
            if (value.Length == 0) return false;

            const string appsFolder = "shell:AppsFolder\\";
            if (value.StartsWith(appsFolder, StringComparison.OrdinalIgnoreCase))
            {
                string aumid = value[appsFolder.Length..];
                int bang = aumid.IndexOf('!');
                string family = bang > 0 ? aumid[..bang] : aumid;
                if (family.Length == 0) return false;
                identity = "uwp:" + family.ToLowerInvariant();
                return true;
            }

            string? packageFullName = TryGetWindowsAppsPackageFullName(value);
            if (packageFullName == null) return false;

            // 包全名：Name_Version_Architecture__PublisherId
            string[] parts = packageFullName.Split('_');
            if (parts.Length < 2) return false;

            identity = "uwp:" + (parts[0] + "_" + parts[^1]).ToLowerInvariant();
            return true;
        }

        private static string? TryGetWindowsAppsPackageFullName(string path)
        {
            foreach (string? root in new[]
            {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            })
            {
                if (string.IsNullOrWhiteSpace(root)) continue;

                string prefix = Path.Combine(root, "WindowsApps") + "\\";
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                string rest = path[prefix.Length..];
                int separator = rest.IndexOf('\\');
                return separator > 0 ? rest[..separator] : rest;
            }

            return null;
        }

        /// <summary>根目录启动器优先：&lt;appDir&gt;\x.exe 存在时，把 &lt;appDir&gt;\&lt;版本号&gt;\x.exe 归一到根路径。</summary>
        private static string NormalizeVersionedExecutablePath(string path)
        {
            if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                return path;

            string? versionDirectory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(versionDirectory)
                || !Version.TryParse(Path.GetFileName(versionDirectory), out _))
                return path;

            string? applicationDirectory = Path.GetDirectoryName(versionDirectory);
            if (string.IsNullOrWhiteSpace(applicationDirectory)) return path;

            string rootExecutable = Path.Combine(applicationDirectory, Path.GetFileName(path));
            return File.Exists(rootExecutable) ? rootExecutable : path;
        }

        private static string ResolveLnkTarget(string lnkPath)
        {
            long ticks = 0;
            try
            {
                var file = new FileInfo(lnkPath);
                if (file.Exists) ticks = file.LastWriteTimeUtc.Ticks;
            }
            catch { }

            if (ResolveCache.TryGetValue(lnkPath, out var cached) && cached.Ticks == ticks)
                return cached.Target;

            var info = BlockBox.Services.InstalledProgramScanner.ResolveShortcutInfo(lnkPath);
            if (info == null || string.IsNullOrWhiteSpace(info.TargetPath))
                return lnkPath;

            ResolveCache[lnkPath] = (ticks, info.TargetPath);
            return info.TargetPath;
        }

        private static string NormalizePath(string value)
        {
            string result = value.Trim().Trim('"');
            try
            {
                result = Path.GetFullPath(result);
            }
            catch
            {
                // 保留原值（比如命令行、相对路径无法规范化时）
            }

            return result.TrimEnd('\\').ToLowerInvariant();
        }

        private static string? TryReadInternetShortcutUrl(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                foreach (string rawLine in File.ReadLines(path))
                {
                    string line = rawLine.Trim();
                    if (!line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string url = line[4..].Trim().Trim('"');
                    return url.Length == 0 ? null : url;
                }
            }
            catch
            {
                // 读不到就回退成 .url 文件路径
            }

            return null;
        }
    }
}
