using BlockBox.Models;
using BlockBox.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace BlockBox.Data
{
    public static class ExcludedProgramRepository
    {
        public static string? GetExecutablePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    path = InstalledProgramScanner.ResolveShortcutTarget(path);
                if (string.IsNullOrWhiteSpace(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    return null;
                return Path.GetFullPath(path);
            }
            catch { return null; }
        }

        public static List<ShortcutItem> GetAll()
        {
            var result = new List<ShortcutItem>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TargetPath, Name, IconPath FROM ExcludedPrograms ORDER BY Name COLLATE NOCASE";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new ShortcutItem { TargetPath = reader.GetString(0), Name = reader.GetString(1), IconPath = reader.GetString(2) });
            return result;
        }

        public static void Add(ShortcutItem shortcut, string executablePath)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO ExcludedPrograms (TargetPath, Name, IconPath) VALUES ($path, $name, $icon)";
            cmd.Parameters.AddWithValue("$path", executablePath);
            cmd.Parameters.AddWithValue("$name", shortcut.Name);
            cmd.Parameters.AddWithValue("$icon", shortcut.IconPath ?? "");
            cmd.ExecuteNonQuery();
        }

        public static void Remove(string path)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ExcludedPrograms WHERE TargetPath = $path COLLATE NOCASE";
            cmd.Parameters.AddWithValue("$path", path);
            cmd.ExecuteNonQuery();
        }
    }
}
