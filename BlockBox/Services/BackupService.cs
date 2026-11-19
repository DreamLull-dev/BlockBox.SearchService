using BlockBox.Helpers;
using BlockBox.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BlockBox.Services
{
    public static class BackupService
    {
        public const long MaxBackupSize = 200L * 1024 * 1024;  // 200MB
        private static readonly TimeSpan ExternalDragFileRetention = TimeSpan.FromHours(24);

        /// <summary>
        /// 创建系统外部拖放用的独立暂存目录。暂存区位于系统临时目录，避免把应用私有备份直接交给桌面。
        /// </summary>
        public static string CreateExternalDragStagingDirectory()
        {
            string root = Path.Combine(Path.GetTempPath(), "BlockBox", "ExternalDrag");
            Directory.CreateDirectory(root);
            string stagingDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);
            return stagingDirectory;
        }

        /// <summary>
        /// 将备份还原成保留原始文件名和扩展名的独立副本，供外部程序/桌面使用；不修改备份本身。
        /// 当前备份格式是源文件的原样副本，因此此处按字节还原，扩展名以原始目标路径为准。
        /// </summary>
        public static async Task<string> RestoreBackupForExternalDragAsync(
            string backupPath, string originalFilePath, string stagingDirectory)
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("备份文件不存在", backupPath);
            if (string.IsNullOrWhiteSpace(stagingDirectory))
                throw new ArgumentException("暂存目录不能为空", nameof(stagingDirectory));

            Directory.CreateDirectory(stagingDirectory);

            string fileName = Path.GetFileName(originalFilePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = Path.GetFileName(backupPath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "RestoredFile";

            string destinationPath = Path.Combine(stagingDirectory, fileName);
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            int collisionIndex = 2;
            while (File.Exists(destinationPath))
            {
                destinationPath = Path.Combine(stagingDirectory,
                    $"{nameWithoutExtension} ({collisionIndex++}){extension}");
            }

            await using var source = new FileStream(
                backupPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, useAsync: true);
            await using var destination = new FileStream(
                destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);
            await source.CopyToAsync(destination);
            await destination.FlushAsync();
            return destinationPath;
        }

        /// <summary>拖放结束后延迟清理暂存副本，避免目标程序尚未读取时文件被过早删除。</summary>
        public static void ScheduleExternalDragStagingCleanup(string stagingDirectory)
        {
            if (string.IsNullOrWhiteSpace(stagingDirectory)) return;

            _ = Task.Run(async () =>
            {
                await Task.Delay(ExternalDragFileRetention);
                TryDeleteExternalDragStagingDirectory(stagingDirectory);
            });
        }

        /// <summary>清理上次运行遗留且超过保留时间的外部拖放暂存目录。</summary>
        public static void CleanExpiredExternalDragStagingDirectories()
        {
            string root = Path.Combine(Path.GetTempPath(), "BlockBox", "ExternalDrag");
            if (!Directory.Exists(root)) return;

            foreach (string directory in Directory.GetDirectories(root))
            {
                try
                {
                    if (DateTime.UtcNow - Directory.GetCreationTimeUtc(directory) >= ExternalDragFileRetention)
                        TryDeleteExternalDragStagingDirectory(directory);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[外部拖拽] 清理暂存目录失败: {ex.Message}");
                }
            }
        }

        private static void TryDeleteExternalDragStagingDirectory(string stagingDirectory)
        {
            try
            {
                string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BlockBox", "ExternalDrag"))
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullDirectory = Path.GetFullPath(stagingDirectory);
                if (fullDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(fullDirectory))
                {
                    Directory.Delete(fullDirectory, recursive: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[外部拖拽] 清理暂存目录失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 把源文件备份到 content/{mode}/{categoryId}/ 下
        /// </summary>
        public static async Task<BackupResult> BackupAsync(
            string sourceFilePath, AppMode mode, string categoryId)
        {
            var result = new BackupResult();

            if (!File.Exists(sourceFilePath))
            {
                result.Success = false;
                result.Error = "源文件不存在";
                return result;
            }

            // 算源文件 MD5（后台线程）
            result.Md5 = await Md5Helper.ComputeAsync(sourceFilePath);

            var fi = new FileInfo(sourceFilePath);

            // 超过阈值 → 只记录不备份
            if (fi.Length > MaxBackupSize)
            {
                result.Success = true;
                result.IsBackedUp = false;
                System.Diagnostics.Debug.WriteLine(
                    $"[备份] {fi.Name} 超过 200MB，跳过备份");
                return result;
            }

            // 建目标目录
            string folder = BackupPathHelper.GetCategoryFolder(mode, categoryId);
            BackupPathHelper.EnsureDirectory(folder);

            // 复制
            string targetName = BackupPathHelper.MakeBackupFileName(fi.Name);
            string targetPath = Path.Combine(folder, targetName);
            File.Copy(sourceFilePath, targetPath, overwrite: false);

            result.Success = true;
            result.IsBackedUp = true;
            result.BackupAbsolutePath = targetPath;
            result.BackupRelativePath = BackupPathHelper.MakeRelative(targetPath);

            System.Diagnostics.Debug.WriteLine($"[备份] {fi.Name} → {targetPath}");
            return result;
        }

        /// <summary>
        /// 删除分类时，把它的备份文件夹改名为 xxx_deleted_{时间戳}，
        /// 便于 7 天后清理
        /// </summary>
        public static void MarkCategoryFolderDeleted(AppMode mode, string categoryId)
        {
            string folder = BackupPathHelper.GetCategoryFolder(mode, categoryId);
            if (!Directory.Exists(folder)) return;

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string newName = $"{categoryId}_deleted_{stamp}";
            string newPath = Path.Combine(BackupPathHelper.GetModeFolder(mode), newName);

            Directory.Move(folder, newPath);
            System.Diagnostics.Debug.WriteLine($"[备份] 已标记删除: {newPath}");
        }

        /// <summary>
        /// 清理超过 7 天的已删除分类文件夹
        /// </summary>
        public static void CleanExpiredDeletedFolders()
        {
            foreach (AppMode mode in BlockBox.Data.ModeRepository.GetAllModes())
            {
                string modeFolder = BackupPathHelper.GetModeFolder(mode);
                if (!Directory.Exists(modeFolder)) continue;

                foreach (var dir in Directory.GetDirectories(modeFolder, "*_deleted_*"))
                {
                    try
                    {
                        // 从文件夹名尾部的时间戳解析删除时间
                        string name = Path.GetFileName(dir);
                        int idx = name.LastIndexOf("_deleted_");
                        if (idx < 0) continue;

                        string stamp = name.Substring(idx + "_deleted_".Length);
                        if (DateTime.TryParseExact(stamp, "yyyyMMdd_HHmmss",
                            null, System.Globalization.DateTimeStyles.None, out var deletedAt))
                        {
                            if ((DateTime.Now - deletedAt).TotalDays > 7)
                            {
                                Directory.Delete(dir, recursive: true);
                                System.Diagnostics.Debug.WriteLine($"[清理] 已删除: {dir}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[清理] 失败: {ex.Message}");
                    }
                }
            }
        }
    }

    public class BackupResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public bool IsBackedUp { get; set; }
        public string Md5 { get; set; } = "";
        public string BackupAbsolutePath { get; set; } = "";
        public string BackupRelativePath { get; set; } = "";
    }
}