using BlockBox.Data;
using BlockBox.Helpers;
using BlockBox.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BlockBox.Services
{
    public static class StartupIconCacheService
    {
        public static async Task<int> PrepareAsync(
            string cacheDirectory,
            IReadOnlyCollection<InstalledProgramInfo> programs,
            IProgress<string>? progress = null)
        {
            var stopwatch = Stopwatch.StartNew();
            Directory.CreateDirectory(cacheDirectory);
            int repaired = VerifyService.RepairSharedApplicationTargetPaths();
            if (repaired > 0)
                System.Diagnostics.Debug.WriteLine($"[启动图标缓存] 已修复 {repaired} 条共享程序路径");

            foreach (var program in programs.Where(program =>
                         !string.IsNullOrWhiteSpace(program.ResolvedTargetPath)))
            {
                int migrated = ShortcutRepository.MigrateSharedApplicationTargetPath(
                    program.ResolvedTargetPath!, program.TargetPath, program.DisplayName);
                if (migrated > 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[启动图标缓存] 已迁移历史程序路径 {program.ResolvedTargetPath} -> {program.TargetPath}; Count={migrated}");
                }
            }

            var allItems = ShortcutRepository.GetAll();
            System.Diagnostics.Debug.WriteLine(
                $"[启动图标缓存] 总记录={allItems.Count}; 可访问目标={allItems.Count(item => File.Exists(item.TargetPath))}; 用户={Environment.UserDomainName}\\{Environment.UserName}; Base={AppContext.BaseDirectory}");

            var preferredIconSources = programs
                .Where(program => !string.IsNullOrWhiteSpace(program.PreferredIconSourcePath))
                .SelectMany(program => new[]
                {
                    (Target: program.TargetPath, Icon: program.PreferredIconSourcePath!),
                    (Target: program.ResolvedTargetPath, Icon: program.PreferredIconSourcePath!)
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Target))
                .GroupBy(entry => entry.Target!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Icon, StringComparer.OrdinalIgnoreCase);
            var resolvedTargets = programs
                .Where(program => !string.IsNullOrWhiteSpace(program.PreferredIconSourcePath)
                    && !string.IsNullOrWhiteSpace(program.ResolvedTargetPath))
                .SelectMany(program => new[]
                {
                    (Target: program.TargetPath, Resolved: program.ResolvedTargetPath!),
                    (Target: program.ResolvedTargetPath, Resolved: program.ResolvedTargetPath!)
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Target))
                .GroupBy(entry => entry.Target!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Resolved, StringComparer.OrdinalIgnoreCase);

            var shortcuts = allItems
                .Where(item => !item.TargetPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)
                    && (File.Exists(item.TargetPath)
                        || preferredIconSources.TryGetValue(item.TargetPath, out var iconSource)
                            && File.Exists(iconSource)))
                .ToList();

            if (shortcuts.Count == 0)
            {
                stopwatch.Stop();
                System.Diagnostics.Debug.WriteLine(
                    $"[启动图标缓存] 完成; 待处理=0; 耗时={stopwatch.ElapsedMilliseconds}ms");
                return 0;
            }

            int prepared = 0;
            int cacheHits = 0;
            int extracted = 0;
            for (int i = 0; i < shortcuts.Count; i++)
            {
                var item = shortcuts[i];
                try
                {
                    bool isBilibiliSubmissionTool = item.Name.Contains("哔哩哔哩投稿工具", StringComparison.OrdinalIgnoreCase)
                        && !item.Name.Contains("卸载", StringComparison.OrdinalIgnoreCase);
                    bool hasPreferredPath = preferredIconSources.TryGetValue(item.TargetPath, out var preferredPath)
                        && File.Exists(preferredPath);
                    string iconSource = !isBilibiliSubmissionTool && hasPreferredPath
                        ? preferredPath!
                        : item.TargetPath;
                    bool traceIcon = item.Name.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase)
                        || item.Name.Contains("Opera GX", StringComparison.OrdinalIgnoreCase)
                        || item.Name.Contains("OpenAL", StringComparison.OrdinalIgnoreCase)
                        || isBilibiliSubmissionTool;
                    if (traceIcon)
                        System.Diagnostics.Debug.WriteLine(
                            $"[启动图标缓存] 开始提取; Name={item.Name}; Target={item.TargetPath}; PreferredSource={(hasPreferredPath ? preferredPath : "<none>")}; IconSource={iconSource}; OldIcon={item.IconPath}");

                    bool preferPowerShell = resolvedTargets.TryGetValue(item.TargetPath, out var resolvedTarget)
                        && !File.Exists(resolvedTarget);
                    var cacheResult = await CacheIconAsync(iconSource, cacheDirectory, preferPowerShell);
                    string? pngPath = cacheResult.Path;
                    if (cacheResult.CacheHit) cacheHits++; else if (pngPath != null) extracted++;
                    if (pngPath == null
                        && !string.Equals(iconSource, item.TargetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        cacheResult = await CacheIconAsync(item.TargetPath, cacheDirectory, preferPowerShell);
                        pngPath = cacheResult.Path;
                        if (cacheResult.CacheHit) cacheHits++; else if (pngPath != null) extracted++;
                    }
                    if (pngPath == null)
                    {
                        if (traceIcon)
                            System.Diagnostics.Debug.WriteLine($"[启动图标缓存] 提取失败; Name={item.Name}; Target={item.TargetPath}");
                        continue;
                    }

                    ShortcutRepository.UpdateIconPath(item.Id, pngPath);
                    prepared++;
                    if (traceIcon)
                        System.Diagnostics.Debug.WriteLine(
                            $"[启动图标缓存] 已写回; Name={item.Name}; IconPath={pngPath}; Bytes={new FileInfo(pngPath).Length}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[启动图标缓存] {item.Name}: {ex.Message}");
                }

                if ((i + 1) % 10 == 0 || i + 1 == shortcuts.Count)
                    progress?.Report($"正在准备程序图标（{i + 1}/{shortcuts.Count}）...");
            }

            return prepared;
        }

        private static async Task<(string? Path, bool CacheHit)> CacheIconAsync(
            string sourcePath, string cacheDirectory, bool preferPowerShell)
        {
            if (!File.Exists(sourcePath))
                return (null, false);

            var info = new FileInfo(sourcePath);
            int iconSize = preferPowerShell ? 192 : 96;
            string identity = $"{sourcePath.ToLowerInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}|{(preferPowerShell ? "powershell" : "shell")}|shortcut-icon-v13-url-iconfile-{iconSize}px";
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            string pngPath = Path.Combine(cacheDirectory, key + ".png");
            if (File.Exists(pngPath) && new FileInfo(pngPath).Length > 0)
            {
                if (preferPowerShell)
                    System.Diagnostics.Debug.WriteLine($"[启动图标缓存] 磁盘缓存命中; Source={sourcePath}");
                return (pngPath, true);
            }

            var png = await Task.Run(() => ShellIconHelper.GetThumbnailPngBytes(sourcePath, iconSize, preferPowerShell));
            if (png == null || png.Length == 0)
                return (null, false);

            await File.WriteAllBytesAsync(pngPath, png);
            return (pngPath, false);
        }
    }
}
