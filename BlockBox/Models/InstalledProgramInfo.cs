using System.Collections.Generic;

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
        AppPath,
        StoreApp,
        UwpApp
    }

    /// <summary>.lnk / .url 解析出的启动信息。</summary>
    public sealed class ShortcutInfo
    {
        public string TargetPath { get; init; } = "";
        public string Arguments { get; init; } = "";
        public string WorkingDirectory { get; init; } = "";
    }

    /// <summary>扫描统计：给“导入结果”提供来源和跳过原因。</summary>
    public sealed class ScanStats
    {
        public int EmptyTarget { get; set; }
        public int InvalidExtension { get; set; }
        public int MissingTarget { get; set; }
        public int DuplicatesRemoved { get; set; }
        public int Unlaunchable { get; set; }
        public List<string> UnlaunchableNames { get; } = new();
    }

    public sealed class InstalledProgramInfo
    {
        /// <summary>显示名称（卡片标题）</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>启动路径（.exe 或 .lnk），写进 ShortcutItem.TargetPath</summary>
        public string TargetPath { get; set; } = "";

        public string? ResolvedTargetPath { get; set; }

        /// <summary>启动参数（来自 .lnk）。</summary>
        public string Arguments { get; set; } = "";

        public string WorkingDirectory { get; set; } = "";

        /// <summary>去重身份：解析后的目标 + 参数（见 ProgramIdentity）。</summary>
        public string Identity { get; set; } = "";

        public string? InstallLocation { get; set; }
        public string? Version { get; set; }
        public string? Publisher { get; set; }

        public ProgramSource Source { get; set; }

        public string? LocalIconPath { get; set; }

        public string? PreferredIconSourcePath { get; set; }
    }
}