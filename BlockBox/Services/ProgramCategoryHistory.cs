using BlockBox.Data;
using BlockBox.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BlockBox.Services
{
    public sealed class ProgramCategoryEntry
    {
        public string Name { get; set; } = "";
        public string Identity { get; set; } = "";
        public string TargetPath { get; set; } = "";
        public string Category { get; set; } = "";
        public bool Excluded { get; set; }
    }

    public static class ProgramCategoryHistory
    {
        private static readonly object Sync = new();
        private static string FilePath => Path.Combine(BlockBox.Helpers.AppDataPaths.Root, "setting", "program-category-history.json");

        public static string? GetIdentity(string? targetPath, string? resolvedPath = null)
        {
            string? executable = ExcludedProgramRepository.GetExecutablePath(resolvedPath)
                ?? ExcludedProgramRepository.GetExecutablePath(targetPath);
            if (executable != null) return executable;
            if (string.IsNullOrWhiteSpace(targetPath)) return null;
            if (targetPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)) return targetPath.Trim();
            try
            {
                return Path.GetExtension(targetPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFullPath(targetPath) : null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        public static List<ProgramCategoryEntry> GetAll()
        {
            lock (Sync) return Load();
        }

        public static bool Matches(ProgramCategoryEntry entry, string category, string query, string localizedCategory)
            => string.Equals(entry.Category, category, StringComparison.Ordinal)
                && (string.IsNullOrEmpty(query)
                    || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || localizedCategory.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.TargetPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Identity.Contains(query, StringComparison.OrdinalIgnoreCase));

        public static void Remember(ShortcutItem shortcut, Category category, Category? sourceCategory = null)
        {
            bool rememberTarget = IsRecordedCategory(category);
            bool removeSource = sourceCategory != null && IsRecordedCategory(sourceCategory)
                && (!rememberTarget || !string.Equals(sourceCategory.Name, category.Name, StringComparison.Ordinal));
            if (!rememberTarget && !removeSource) return;
            string? identity = GetIdentity(shortcut.TargetPath);
            if (identity == null) return;
            lock (Sync)
            {
                var entries = Load();
                if (removeSource)
                    entries.RemoveAll(e => !e.Excluded && SameKey(e, identity, sourceCategory!.Name));
                if (rememberTarget)
                {
                    entries.RemoveAll(e => SameKey(e, identity, category.Name));
                    entries.Add(new ProgramCategoryEntry
                    {
                        Name = shortcut.Name, Identity = identity, TargetPath = shortcut.TargetPath,
                        Category = category.Name, Excluded = false
                    });
                }
                Save(entries);
            }
        }

        private static bool IsRecordedCategory(Category category)
            => category.IsShared && category.Name is "游戏" or "办公" or "音乐";

        public static void Remove(IEnumerable<ProgramCategoryEntry> selected, bool exclude)
        {
            var selection = selected.ToList();
            lock (Sync)
            {
                var entries = Load();
                foreach (var item in selection)
                {
                    entries.RemoveAll(e => SameKey(e, item.Identity, item.Category));
                    if (exclude)
                    {
                        item.Excluded = true;
                        entries.Add(item);
                    }
                }
                Save(entries);
            }
        }

        public static bool SameKey(ProgramCategoryEntry entry, string identity, string category)
            => string.Equals(entry.Identity, identity, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Category, category, StringComparison.Ordinal);

        private static List<ProgramCategoryEntry> Load()
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<ProgramCategoryEntry>>(File.ReadAllText(FilePath))
                ?? throw new InvalidDataException("Invalid program category history.");
        }

        private static void Save(List<ProgramCategoryEntry> entries)
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, path, true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
