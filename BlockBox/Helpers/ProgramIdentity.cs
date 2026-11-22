using System;
using System.Collections.Concurrent;
using System.IO;

namespace BlockBox.Helpers
{
    /// <summary>
    /// 程序身份：把 .lnk / .url / exe / UWP 路径统一成同一个键，用于扫描与手动添加去重。
    /// 身份 = 解析后的真实目标（小写、去尾斜杠）+ 启动参数。
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
            if (value.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                return NormalizeShellPath(value);

            string extension = Path.GetExtension(value);
            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                value = ResolveLnkTarget(value);
            }
            else if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                string? url = TryReadInternetShortcutUrl(value);
                if (!string.IsNullOrWhiteSpace(url)) value = url;
            }

            return NormalizePath(value);
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

        private static string NormalizeShellPath(string value)
            => value.Trim().TrimEnd('\\').ToLowerInvariant();

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
