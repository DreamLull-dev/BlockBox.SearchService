namespace BlockBox.Models
{
    public enum ProgramSource
    {
        Registry64,
        Registry32,
        RegistryCurrentUser,
        StartMenuUser,
        StartMenuCommon,
        DesktopUser,
        DesktopCommon,
        StoreApp,
        UwpApp
    }

    public sealed class InstalledProgramInfo
    {
        /// <summary>显示名称（卡片标题）</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>启动路径（.exe 或 .lnk），写进 ShortcutItem.TargetPath</summary>
        public string TargetPath { get; set; } = "";

        public string? ResolvedTargetPath { get; set; }

        public string? InstallLocation { get; set; }
        public string? Version { get; set; }
        public string? Publisher { get; set; }

        public ProgramSource Source { get; set; }

        public string? LocalIconPath { get; set; }

        public string? PreferredIconSourcePath { get; set; }
    }
}