using System;
using System.IO;
using System.Reflection;

namespace BlockBox.Helpers
{
    public static class StartupManager
    {
        private const string ShortcutName = "BlockBox.lnk";

        public static void SetEnabled(bool enabled)
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
