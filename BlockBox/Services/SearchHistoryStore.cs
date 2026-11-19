using BlockBox.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BlockBox.Services
{
    /// <summary>全局搜索的最近查询（最多 10 条），保存在数据目录的 setting 下。</summary>
    internal static class SearchHistoryStore
    {
        private const int MaxEntries = 10;
        private static readonly object Gate = new();
        private static List<string>? _entries;

        private static string? TryGetFilePath()
        {
            try
            {
                string root = AppDataPaths.Root;
                return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "setting", "search-history.json");
            }
            catch
            {
                // 数据目录解析失败时历史功能静默降级，不影响搜索本身。
                return null;
            }
        }

        internal static IReadOnlyList<string> Get()
        {
            lock (Gate)
            {
                Load();
                return _entries!.ToArray();
            }
        }

        internal static void Add(string? query)
        {
            string value = (query ?? string.Empty).Trim();
            if (value.Length == 0) return;

            lock (Gate)
            {
                Load();
                _entries!.RemoveAll(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
                _entries.Insert(0, value);
                if (_entries.Count > MaxEntries)
                    _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
                Save();
            }
        }

        internal static void Clear()
        {
            lock (Gate)
            {
                _entries = new List<string>();
                Save();
            }
        }

        private static void Load()
        {
            if (_entries is not null) return;
            string? path = TryGetFilePath();
            if (path is null)
            {
                _entries = new List<string>();
                return;
            }
            try
            {
                if (File.Exists(path))
                    _entries = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[搜索历史] 读取失败: {ex.Message}");
            }
            _entries ??= new List<string>();
        }

        private static void Save()
        {
            string? path = TryGetFilePath();
            if (path is null) return;
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, JsonSerializer.Serialize(_entries));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[搜索历史] 写入失败: {ex.Message}");
            }
        }
    }
}
