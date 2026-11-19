//using BlockBox.Data;
//using BlockBox.Models;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;

//namespace BlockBox.Services
//{
//    public sealed class ProgramImportResult
//    {
//        public int Scanned { get; set; }
//        public int AddedAllApps { get; set; }
//        public int AddedGame { get; set; }
//        public int AddedOffice { get; set; }
//        public int AddedMusic { get; set; }
//        public int Skipped { get; set; }
//        public List<string> Errors { get; } = new();
//    }

//    public static class ProgramImportService
//    {
//        private const string CategoryAllApps = "全部应用";
//        private const string CategoryGame = "游戏";
//        private const string CategoryOffice = "办公";
//        private const string CategoryMusic = "音乐";

//        private const string GlyphAllApps = "\uECAA";
//        private const string GlyphGame = "\uE7FC";
//        private const string GlyphOffice = "\uE8A5";
//        private const string GlyphMusic = "\uE8D6";

//        public static async Task<ProgramImportResult> ScanAndImportAsync(
//            AppMode mode,
//            ProgramFilter? filter,
//            IProgress<string>? progress = null,
//            CancellationToken ct = default)
//        {
//            var result = new ProgramImportResult();

//            progress?.Report("正在扫描已安装程序...");
//            var programs = await Task.Run(() => InstalledProgramScanner.ScanAll(filter), ct);
//            result.Scanned = programs.Count;
//            progress?.Report($"扫描到 {programs.Count} 个程序，正在分类导入...");

//            string catIdAllApps = EnsureCategory(mode, CategoryAllApps, GlyphAllApps);
//            string catIdGame = EnsureCategory(mode, CategoryGame, GlyphGame);
//            string catIdOffice = EnsureCategory(mode, CategoryOffice, GlyphOffice);
//            string catIdMusic = EnsureCategory(mode, CategoryMusic, GlyphMusic);

//            var existingAllApps = GetExistingPaths(catIdAllApps, mode);
//            var existingGame = GetExistingPaths(catIdGame, mode);
//            var existingOffice = GetExistingPaths(catIdOffice, mode);
//            var existingMusic = GetExistingPaths(catIdMusic, mode);

//            foreach (var p in programs)
//            {
//                ct.ThrowIfCancellationRequested();

//                try
//                {
//                    // 全部应用
//                    if (existingAllApps.Add(p.TargetPath))
//                    {
//                        AddShortcut(p, catIdAllApps, mode);
//                        result.AddedAllApps++;
//                    }
//                    else
//                    {
//                        result.Skipped++;
//                    }

//                    // 分类导流
//                    var category = ProgramCategoryClassifier.Classify(p, filter!);
//                    switch (category)
//                    {
//                        case ProgramCategoryClassifier.Category.Game:
//                            if (existingGame.Add(p.TargetPath))
//                            {
//                                AddShortcut(p, catIdGame, mode);
//                                result.AddedGame++;
//                            }
//                            break;

//                        case ProgramCategoryClassifier.Category.Office:
//                            if (existingOffice.Add(p.TargetPath))
//                            {
//                                AddShortcut(p, catIdOffice, mode);
//                                result.AddedOffice++;
//                            }
//                            break;

//                        case ProgramCategoryClassifier.Category.Music:
//                            if (existingMusic.Add(p.TargetPath))
//                            {
//                                AddShortcut(p, catIdMusic, mode);
//                                result.AddedMusic++;
//                            }
//                            break;
//                    }
//                }
//                catch (Exception ex)
//                {
//                    result.Errors.Add($"{p.DisplayName}: {ex.Message}");
//                }
//            }

//            progress?.Report("导入完成");
//            return result;

//        }

//        // ==================== 辅助 ====================

//        private static string EnsureCategory(AppMode mode, string name, string glyph)
//        {
//            var existing = CategoryRepository.GetVisible(mode)
//                .FirstOrDefault(c => !c.IsSeparator && c.Name == name);
//            if (existing != null) return existing.Id;

//            int nextGroup = CategoryRepository.GetNextGroupIndex(mode);

//            var cat = new Category
//            {
//                Name = name,
//                IconGlyph = glyph,
//                IsUserCreated = true,
//                BelongingMode = mode,
//                GroupIndex = nextGroup,
//                ContentKind = ContentKind.Application,   // TODO: 核对枚举名
//                ExtensionList = ""                       // 空 = 不限扩展名
//            };

//            CategoryRepository.Add(cat);
//            return cat.Id;
//        }

//        private static HashSet<string> GetExistingPaths(string categoryId, AppMode mode)
//        {
//            return ShortcutRepository.GetByCategory(mode, categoryId)
//                .Select(s => s.TargetPath)
//                .Where(p => !string.IsNullOrEmpty(p))
//                .ToHashSet(StringComparer.OrdinalIgnoreCase);
//        }

//        private static void AddShortcut(InstalledProgramInfo p, string categoryId, AppMode mode)
//        {
//            var item = new ShortcutItem
//            {
//                Name = p.DisplayName,
//                TargetPath = p.TargetPath,
//                CategoryId = categoryId,
//                BelongingMode = mode,
//                IsFile = false,
//                SortOrder = ShortcutRepository.GetNextSortOrder(categoryId),
//                BackupPath = "",
//                SourceMd5 = "",
//                SourceState = SourceState.Unknown,
//                IsBackedUp = false,
//                CreatedTime = DateTime.Now,

//                // ⭐ UWP 应用：把本地 logo 路径存进 IconPath
//                IconPath = p.LocalIconPath ?? "",
//            };

//            ShortcutRepository.Add(item);
//        }
//    }
//}
using BlockBox.Data;
using BlockBox.Helpers;
using BlockBox.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlockBox.Services
{
    public sealed class ProgramImportResult
    {
        public int Scanned { get; set; }
        public int AddedAllApps { get; set; }
        public int AddedGame { get; set; }
        public int AddedOffice { get; set; }
        public int AddedMusic { get; set; }
        public int FromUserHistory { get; set; }
        public int FromBuiltInRules { get; set; }
        public List<(string Name, string TargetPath, string Category, bool FromHistory)> CategoryMatches { get; } = new();
        public int Skipped { get; set; }
        public List<string> Errors { get; } = new();
    }

    public static class ProgramImportService
    {
        private const string CategoryAllApps = "全部应用";
        private const string CategoryGame = "游戏";
        private const string CategoryOffice = "办公";
        private const string CategoryMusic = "音乐";

        private const string GlyphAllApps = "\uECAA";
        private const string GlyphGame = "\uE7FC";
        private const string GlyphOffice = "\uE8A5";
        private const string GlyphMusic = "\uE8D6";

        // ==================== 对外：单模式导入（保留兼容） ====================
        public static Task<ProgramImportResult> ScanAndImportAsync(
            AppMode mode,
            ProgramFilter? filter,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            return ScanAndImportAsync(new[] { mode }, filter, progress, ct);
        }

        // ==================== 对外：多模式导入 ====================
        public static async Task<ProgramImportResult> ScanAndImportAsync(
            IEnumerable<AppMode> modes,
            ProgramFilter? filter,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var total = new ProgramImportResult();
            var modeList = modes.Distinct().ToList();

            progress?.Report("正在扫描已安装程序...");
            BlockBox.Helpers.StartupLog.Step("导入: 开始扫描已安装程序");
            var programs = await Task.Run(() => InstalledProgramScanner.ScanAll(filter), ct);
            BlockBox.Helpers.StartupLog.Step($"导入: 扫描完成，共 {programs.Count} 个");
            total.Scanned = programs.Count;
            var excludedPaths = ExcludedProgramRepository.GetAll()
                .Select(item => item.TargetPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            programs = programs.Where(program =>
            {
                string? executable = ExcludedProgramRepository.GetExecutablePath(program.ResolvedTargetPath)
                    ?? ExcludedProgramRepository.GetExecutablePath(program.TargetPath);
                return executable == null || !excludedPaths.Contains(executable);
            }).ToList();
            total.Skipped += total.Scanned - programs.Count;
            progress?.Report($"扫描到 {programs.Count} 个程序，正在分类导入...");

            // ⭐ 跨模式共享图标缓存：TargetPath -> IconPath
            var iconCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            progress?.Report("正在写入全局共享程序库...");
            BlockBox.Helpers.StartupLog.Step("导入: 开始写入共享程序库");
            await ImportToSharedAsync(programs, filter, total, iconCache, ct);
            BlockBox.Helpers.StartupLog.Step("导入: 共享程序库写入完成");

            string iconCacheDirectory = Path.Combine(
                BlockBox.Helpers.AppDataPaths.Root,
                "content", "icon-cache");
            progress?.Report("正在准备程序图标...");
            BlockBox.Helpers.StartupLog.Step("导入: 开始提取程序图标");
            await StartupIconCacheService.PrepareAsync(iconCacheDirectory, programs, progress);
            BlockBox.Helpers.StartupLog.Step("导入: 程序图标提取完成");

            progress?.Report("导入完成");
            return total;
        }

        // ==================== 单模式导入 ====================
        private static async Task ImportToSharedAsync(
            List<InstalledProgramInfo> programs,
            ProgramFilter? filter,
            ProgramImportResult total,
            Dictionary<string, string> iconCache,
            CancellationToken ct)
        {
            string catIdAllApps = EnsureSharedCategory(CategoryAllApps);
            string catIdGame = EnsureSharedCategory(CategoryGame);
            string catIdOffice = EnsureSharedCategory(CategoryOffice);
            string catIdMusic = EnsureSharedCategory(CategoryMusic);

            var existingAllApps = GetExistingSharedPaths(catIdAllApps);
            var existingGame = GetExistingSharedPaths(catIdGame);
            var existingOffice = GetExistingSharedPaths(catIdOffice);
            var existingMusic = GetExistingSharedPaths(catIdMusic);
            var history = ProgramCategoryHistory.GetAll()
                .GroupBy(entry => entry.Identity, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var iconCacheDirectory = Path.Combine(
                BlockBox.Helpers.AppDataPaths.Root,
                "content", "icon-cache");
            Directory.CreateDirectory(iconCacheDirectory);

            foreach (var p in programs)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!string.IsNullOrWhiteSpace(p.TargetPath))
                    {
                        int removedRootRecords =
                            ShortcutRepository.RemoveApplicationRootExecutables(p.TargetPath);
                        if (removedRootRecords > 0)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[程序导入] 已清理 Application 根目录程序记录; Target={p.TargetPath}; Count={removedRootRecords}");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(p.ResolvedTargetPath)
                        && !string.Equals(p.TargetPath, p.ResolvedTargetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        int migrated = ShortcutRepository.MigrateSharedApplicationTargetPath(
                            p.ResolvedTargetPath, p.TargetPath, p.DisplayName);
                        if (migrated > 0)
                        {
                            existingAllApps = GetExistingSharedPaths(catIdAllApps);
                            existingGame = GetExistingSharedPaths(catIdGame);
                            existingOffice = GetExistingSharedPaths(catIdOffice);
                            existingMusic = GetExistingSharedPaths(catIdMusic);
                            System.Diagnostics.Debug.WriteLine(
                                $"[程序导入] 已迁移历史目标 {p.ResolvedTargetPath} -> {p.TargetPath}; Count={migrated}");
                        }
                    }

                    // ⭐ 图标缓存：同一 TargetPath 跨模式复用
                    if (!string.IsNullOrEmpty(p.LocalIconPath))
                    {
                        iconCache[p.TargetPath] = p.LocalIconPath;
                    }
                    else if (iconCache.TryGetValue(p.TargetPath, out var cachedIcon))
                    {
                        p.LocalIconPath = cachedIcon;
                    }

                    if (!string.IsNullOrWhiteSpace(p.PreferredIconSourcePath))
                    {
                        string? shortcutIcon = await ExtractShortcutIconAsync(p.PreferredIconSourcePath, iconCacheDirectory, ct);
                        if (!string.IsNullOrWhiteSpace(shortcutIcon))
                            p.LocalIconPath = shortcutIcon;
                    }

                    // ---- 全部应用 ----
                    if (existingAllApps.Add(p.TargetPath))
                    {
                        AddSharedShortcut(p, catIdAllApps);
                        total.AddedAllApps++;
                    }
                    else
                    {
                        UpdateExistingSharedIcon(catIdAllApps, p);
                        total.Skipped++;
                    }

                    // ---- 分类导流 ----
                    string? identity = ProgramCategoryHistory.GetIdentity(p.TargetPath, p.ResolvedTargetPath);
                    var entries = identity != null && history.TryGetValue(identity, out var found)
                        ? found : new List<ProgramCategoryEntry>();
                    var categories = entries.Where(entry => !entry.Excluded)
                        .Select(entry => entry.Category switch
                        {
                            CategoryGame => ProgramCategoryClassifier.Category.Game,
                            CategoryOffice => ProgramCategoryClassifier.Category.Office,
                            CategoryMusic => ProgramCategoryClassifier.Category.Music,
                            _ => ProgramCategoryClassifier.Category.AllApps
                        }).Where(category => category != ProgramCategoryClassifier.Category.AllApps).Distinct().ToList();
                    bool fromHistory = categories.Count > 0;
                    if (!fromHistory && filter != null)
                    {
                        var excluded = entries.Where(entry => entry.Excluded).Select(entry => entry.Category switch
                        {
                            CategoryGame => ProgramCategoryClassifier.Category.Game,
                            CategoryOffice => ProgramCategoryClassifier.Category.Office,
                            CategoryMusic => ProgramCategoryClassifier.Category.Music,
                            _ => ProgramCategoryClassifier.Category.AllApps
                        }).ToHashSet();
                        var classified = ProgramCategoryClassifier.Classify(p, filter, excluded);
                        if (classified != ProgramCategoryClassifier.Category.AllApps) categories.Add(classified);
                    }
                    if (categories.Count > 0)
                    {
                        if (fromHistory) total.FromUserHistory++;
                        else total.FromBuiltInRules++;
                        foreach (var category in categories)
                        {
                            string name = category switch
                            {
                                ProgramCategoryClassifier.Category.Game => CategoryGame,
                                ProgramCategoryClassifier.Category.Office => CategoryOffice,
                                ProgramCategoryClassifier.Category.Music => CategoryMusic,
                                _ => CategoryAllApps
                            };
                            total.CategoryMatches.Add((p.DisplayName, p.TargetPath, name, fromHistory));
                        }
                    }
                    foreach (var category in categories)
                    switch (category)
                    {
                        case ProgramCategoryClassifier.Category.Game:
                            if (existingGame.Add(p.TargetPath))
                            {
                                AddSharedShortcut(p, catIdGame);
                                total.AddedGame++;
                            }
                            else UpdateExistingSharedIcon(catIdGame, p);
                            break;

                        case ProgramCategoryClassifier.Category.Office:
                            if (existingOffice.Add(p.TargetPath))
                            {
                                AddSharedShortcut(p, catIdOffice);
                                total.AddedOffice++;
                            }
                            else UpdateExistingSharedIcon(catIdOffice, p);
                            break;

                        case ProgramCategoryClassifier.Category.Music:
                            if (existingMusic.Add(p.TargetPath))
                            {
                                AddSharedShortcut(p, catIdMusic);
                                total.AddedMusic++;
                            }
                            else UpdateExistingSharedIcon(catIdMusic, p);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    total.Errors.Add($"{p.DisplayName}: {ex.Message}");
                }
            }
        }

        // ==================== 辅助 ====================

        private static string EnsureSharedCategory(string name)
        {
            var existing = CategoryRepository.GetVisible(AppMode.Normal)
                .FirstOrDefault(c => c.IsShared && !c.IsSeparator && c.Name == name);
            if (existing != null) return existing.Id;

            var cat = new Category
            {
                Name = name,
                IconGlyph = name switch
                {
                    CategoryAllApps => GlyphAllApps,
                    CategoryGame => GlyphGame,
                    CategoryOffice => GlyphOffice,
                    _ => GlyphMusic
                },
                IsUserCreated = false,
                IsShared = true,
                GroupIndex = 0,
                ContentKind = ContentKind.Application,
                ExtensionList = ""
            };

            CategoryRepository.Add(cat);
            return cat.Id;
        }

        private static HashSet<string> GetExistingSharedPaths(string categoryId)
        {
            return ShortcutRepository.GetSharedByCategory(categoryId)
                .Select(s => s.TargetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static async Task<string?> ExtractShortcutIconAsync(
            string shortcutPath, string cacheDirectory, CancellationToken ct)
        {
            if (!File.Exists(shortcutPath))
                return null;

            var shortcutInfo = new FileInfo(shortcutPath);
            string cacheIdentity = $"{shortcutPath.ToLowerInvariant()}|{shortcutInfo.LastWriteTimeUtc.Ticks}|{shortcutInfo.Length}|shortcut-icon-v12-url-iconfile-96px";
            string cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheIdentity)));
            string cachePath = Path.Combine(cacheDirectory, cacheKey + ".png");
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
                return cachePath;

            ct.ThrowIfCancellationRequested();
            byte[]? png = await Task.Run(() => ShellIconHelper.GetThumbnailPngBytes(shortcutPath, 96), ct);
            if (png == null || png.Length == 0)
                return null;

            await File.WriteAllBytesAsync(cachePath, png, ct);
            return cachePath;
        }

        private static void UpdateExistingSharedIcon(string categoryId, InstalledProgramInfo program)
        {
            if (string.IsNullOrWhiteSpace(program.LocalIconPath))
                return;

            var existing = ShortcutRepository.GetSharedByCategory(categoryId)
                .FirstOrDefault(item => string.Equals(item.TargetPath, program.TargetPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null && !string.Equals(existing.IconPath, program.LocalIconPath, StringComparison.OrdinalIgnoreCase))
                ShortcutRepository.UpdateIconPath(existing.Id, program.LocalIconPath);
        }

        private static void AddSharedShortcut(InstalledProgramInfo p, string categoryId)
        {
            var item = new ShortcutItem
            {
                Name = p.DisplayName,
                TargetPath = p.TargetPath,
                CategoryId = categoryId,
                IsShared = true,
                IsFile = false,
                SortOrder = ShortcutRepository.GetNextSortOrder(categoryId, shared: true),
                BackupPath = "",
                SourceMd5 = "",
                SourceState = SourceState.Unknown,
                IsBackedUp = false,
                CreatedTime = DateTime.Now,
                IconPath = p.LocalIconPath ?? "",
            };

            ShortcutRepository.Add(item);
        }
    }
}
