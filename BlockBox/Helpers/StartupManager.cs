using BlockBox.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace BlockBox.Helpers
{
    public static class StartupManager
    {
        private const string ShortcutName = "BlockBox.lnk";
        // 必须与 Package.appxmanifest 里 uap5:StartupTask 的 TaskId 一致。
        private const string PackagedStartupTaskId = "BlockBoxStartup";

        public static void SetEnabled(bool enabled)
        {
            // 打包（Microsoft Store / MSIX）应用不能写“启动”文件夹，必须用 StartupTask。
            if (AppUpdateService.IsPackaged)
            {
                _ = SetPackagedStartupAsync(enabled);
                return;
            }
            SetShortcutEnabled(enabled);
        }

        private static async Task SetPackagedStartupAsync(bool enabled)
        {
            try
            {
                StartupTask? task = await StartupTask.GetAsync(PackagedStartupTaskId);
                if (task is null)
                {
                    // 清单里没有 windows.startupTask 扩展时无法启用；写日志便于排查。
                    Debug.WriteLine("[启动项] 包清单未声明 startupTask 扩展。");
                    return;
                }

                if (enabled)
                {
                    StartupTaskState state = await task.RequestEnableAsync();
                    Debug.WriteLine($"[启动项] StartupTask 状态: {state}");
                }
                else
                {
                    task.Disable();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[启动项] StartupTask 更新失败: {ex.Message}");
            }
        }

        private static void SetShortcutEnabled(bool enabled)
        {
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string shortcutPath = Path.Combine(startupFolder, ShortcutName);

            if (!enabled)
            {
                if (File.Exists(shortcutPath))
                    File.Delete(shortcutPath);
                return;
            }

            string? executablePath = Environment.ProcessPath ?? Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                throw new InvalidOperationException("无法确定应用程序路径。");

            Directory.CreateDirectory(startupFolder);
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("系统不支持创建启动快捷方式。");

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            shortcut.Description = "BlockBox 程序文件管理器";
            shortcut.Save();
        }
    }
}

