using BlockBox.Models;
using System;
using System.IO;

namespace BlockBox.Helpers
{
    public static class BackupPathHelper
    {
        public static string GetContentRoot()
        {
            string baseDir = AppDataPaths.Root;
            return Path.Combine(baseDir, "content");
        }

        public static string GetModeFolder(AppMode mode)
            => Path.Combine(GetContentRoot(), mode.ToString().ToLowerInvariant());

        public static string GetCategoryFolder(AppMode mode, string categoryId)
            => Path.Combine(GetModeFolder(mode), categoryId);

        /// <summary>
        /// 生成带时间戳的备份文件名：wallpaper_20260919_143022.png
        /// </summary>
        public static string MakeBackupFileName(string originalFileName)
        {
            string name = Path.GetFileNameWithoutExtension(originalFileName);
            string ext = Path.GetExtension(originalFileName);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{name}_{stamp}{ext}";
        }

        public static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        /// <summary>
        /// 绝对路径 → 相对 LocalState 的路径（存库用）
        /// </summary>
        public static string MakeRelative(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return "";
            string baseDir = AppDataPaths.Root;
            if (absolutePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return absolutePath.Substring(baseDir.Length).TrimStart('\\', '/');
            return absolutePath;
        }

        /// <summary>
        /// 相对路径 → 绝对路径（读文件用）
        /// </summary>
        public static string MakeAbsolute(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (Path.IsPathRooted(path)) return path;
            string baseDir = AppDataPaths.Root;
            return Path.Combine(baseDir, path);
        }
    }
}