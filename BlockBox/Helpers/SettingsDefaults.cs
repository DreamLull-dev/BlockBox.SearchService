namespace BlockBox.Helpers
{
    public static class SettingsDefaults
    {
        // Theme
        public const int ThemePreset = 0;

        // Global
        public const string PrimaryBackground = "#FF244A80";
        public const string MainContentBackground = "#FFF0F0F0";
        public const string PrimaryFontColor = "#FFFFFFFF";      // 统一用 8 位
        public const string ShortcutGridFontColor = "#FF000000";
        // PrimaryFontColorAuto = 是否按背景自动校正字体色明度；
        // ShortcutGridFontColorAuto = 内容区是否跟随全局字体色。
        public const bool PrimaryFontColorAuto = true;
        public const bool ShortcutGridFontColorAuto = true;

        // Sidebar
        public const string SidebarBackgroundColor = "#FFF7F8FB";
        public const int SidebarBackgroundMode = 0;
        public const bool SidebarState = true;
        public const bool SidebarEnableCustomBackground = false;   // ⭐ 补
        public const bool SidebarIsVideoBackground = false;        // ⭐ 补
        public const string SidebarVideoPath = "";                 // ⭐ 补

        // ContentArea
        public const int ContentBackgroundMode = 0;

        // System
        public const string OverlayColor = "#80000000";
        public const double OverlayOpacity = 0.5;
        public const int FontSizeStep = 2;
        public const double FontSizeScale = 1.0;
        public const string FontFamilyName = "";
        public const int ShortcutLoadAnimation = 0;
        public const string LanguageCode = "zh-CN";
        public const int LanguageIndex = 0;
        public const bool AutoStart = true;
        public const bool TrayOnExit = true;
        public const bool MinimizeToTray = false;
        public const bool RestoreLastTab = true;
        /// <summary>程序启动后自动打开全局搜索面板。</summary>
        public const bool OpenSearchOnStartup = false;
        /// <summary>从全局搜索添加进分类后，取消结果列表的勾选状态。</summary>
        public const bool ClearSearchSelectionAfterAdd = false;
        public const int CurrentMode = 2;

        // Behavior
        public const bool ShowFileExtension = true;

        // Notification
        public const bool ShowSuccess = true;
        public const bool ShowReloadCompleted = true;
        public const bool ConfirmDelete = true;
        public const bool ConfirmRecycleBin = true;

        // Tabs
        public const bool FavoritesPinned = true;
        public const bool RecentPinned = true;
    }
}
