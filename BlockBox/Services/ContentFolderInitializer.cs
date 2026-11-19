using BlockBox.Helpers;
using BlockBox.Models;
using System;

namespace BlockBox.Services
{
    public static class ContentFolderInitializer
    {
        /// <summary>
        /// 启动时保证 content/{mode}/ 三个根目录存在
        /// </summary>
        public static void EnsureRootFolders()
        {
            foreach (AppMode mode in BlockBox.Data.ModeRepository.GetAllModes())
            {
                BackupPathHelper.EnsureDirectory(BackupPathHelper.GetModeFolder(mode));
            }
            System.Diagnostics.Debug.WriteLine(
                $"[内容] 根目录: {BackupPathHelper.GetContentRoot()}");
        }

        /// <summary>
        /// 按需创建某个分类的备份目录
        /// </summary>
        public static void EnsureCategoryFolder(AppMode mode, string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return;
            BackupPathHelper.EnsureDirectory(
                BackupPathHelper.GetCategoryFolder(mode, categoryId));
        }
    }
}