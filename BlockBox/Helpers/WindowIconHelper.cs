using Microsoft.UI.Xaml;
using System;
using System.IO;

namespace BlockBox.Helpers
{
    /// <summary>
    /// 统一的窗口图标解析与应用入口。
    /// 打包运行时从包安装目录取 Assets，非打包运行时从程序目录取，避免两处各写一份。
    /// </summary>
    public static class WindowIconHelper
    {
        public static string? ResolveAssetPath(string fileName)
        {
            try
            {
                string installPath = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
                string packagedPath = Path.Combine(installPath, "Assets", fileName);
                if (File.Exists(packagedPath))
                    return packagedPath;
            }
            catch
            {
                // 非打包运行没有包身份，直接走程序目录。
            }

            string localPath = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            return File.Exists(localPath) ? localPath : null;
        }

        /// <summary>同时设置任务栏图标（任务栏缩略图左上角）和标题栏图标。</summary>
        public static bool Apply(Window window, string? iconPath)
        {
            if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
            {
                System.Diagnostics.Debug.WriteLine($"[图标] 找不到: {iconPath}");
                return false;
            }

            try
            {
                window.AppWindow.SetTaskbarIcon(iconPath);
                window.AppWindow.SetTitleBarIcon(iconPath);
                System.Diagnostics.Debug.WriteLine($"[图标] 已设置: {iconPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[图标] 设置失败: {ex.Message}");
                return false;
            }
        }
    }
}
