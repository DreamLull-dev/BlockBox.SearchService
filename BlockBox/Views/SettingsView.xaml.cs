using BlockBox.Helpers;
using BlockBox.Services;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;

namespace BlockBox.Views
{
    internal static class SystemFontHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LOGFONTW
        {
            public int lfHeight;
            public int lfWidth;
            public int lfEscapement;
            public int lfOrientation;
            public int lfWeight;
            public byte lfItalic;
            public byte lfUnderline;
            public byte lfStrikeOut;
            public byte lfCharSet;
            public byte lfOutPrecision;
            public byte lfClipPrecision;
            public byte lfQuality;
            public byte lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string lfFaceName;
        }

        private delegate int EnumFontFamExProc(
            IntPtr lpelfe, IntPtr lpntme, uint fontType, IntPtr lParam);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int EnumFontFamiliesExW(
            IntPtr hdc,
            ref LOGFONTW lpLogfont,
            EnumFontFamExProc lpEnumFontFamExProc,
            IntPtr lParam,
            uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>
        /// 返回当前系统语言下的字体族名称列表（中文系统上就是中文名）。
        /// </summary>
        public static List<string> GetLocalizedFontFamilies()
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return result;

            try
            {
                var lf = new LOGFONTW
                {
                    lfCharSet = 1,          // DEFAULT_CHARSET：枚举所有字符集
                    lfFaceName = string.Empty
                };

                EnumFontFamExProc callback = (lpelfe, lpntme, fontType, lParam) =>
                {
                    var logFont = Marshal.PtrToStructure<LOGFONTW>(lpelfe);
                    string name = logFont.lfFaceName;

                    // ⭐ 跳过竖排字体（@ 前缀）
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith("@"))
                        return 1;

                    if (seen.Add(name))
                        result.Add(name);

                    return 1;   // 非 0 表示继续枚举
                };

                EnumFontFamiliesExW(hdc, ref lf, callback, IntPtr.Zero, 0);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }

            // 排序：中文在前，英文在后；同组内按名称
            return result
                .OrderBy(n => n.Length > 0 && n[0] > 0x2E80 ? 0 : 1)
                .ThenBy(n => n, StringComparer.CurrentCulture)
                .ToList();
        }
    }
    public class FontFamilyOption
    {
        public string DisplayName { get; set; } = "";
        public string? FontFamilyName { get; set; }   // null 表示系统默认
        public override string ToString() => DisplayName;
    }
    public class LanguageSelectionOption
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public override string ToString() => DisplayName;
    }
    public sealed partial class SettingsView : UserControl
    {
        /// <summary>“关于我们”里显示的版本号（打包版取商店包版本，开发版取程序集版本）。</summary>
        public string VersionText =>
            LanguageService.TranslateFormat("版本 {version}", ("{version}", AppUpdateService.VersionText));

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            // 和标题栏“帮助 → 检查更新”走同一套流程。
            if (App.MainWindow is not { } owner) return;
            await UpdatePromptService.CheckAsync(owner);
        }

        private async void FeedbackButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is not { } owner) return;
            await UpdatePromptService.ShowFeedbackAsync(owner);
        }
        public double SettingsFontSize => Math.Clamp(
            FontSizeHelper.SettingsBodyInitialFontSize + FontSizeHelper.CurrentFontSizeOffset,
            8,
            32);
        public string PrimaryColorHex
    => $"#{PrimaryColorPicker.Color.R:X2}{PrimaryColorPicker.Color.G:X2}{PrimaryColorPicker.Color.B:X2}";

        public string MainContentColorHex
            => $"#{MainContentColorPicker.Color.R:X2}{MainContentColorPicker.Color.G:X2}{MainContentColorPicker.Color.B:X2}";

        public string SidebarBackgroundColorHex
            => $"#{SidebarBackgroundColorPicker.Color.R:X2}{SidebarBackgroundColorPicker.Color.G:X2}{SidebarBackgroundColorPicker.Color.B:X2}";

        public string FontColorHex
            => $"#{FontColorPicker.Color.R:X2}{FontColorPicker.Color.G:X2}{FontColorPicker.Color.B:X2}";

        public string ShortcutGridFontColorHex
            => $"#{ShortcutGridFontColorPicker.Color.R:X2}{ShortcutGridFontColorPicker.Color.G:X2}{ShortcutGridFontColorPicker.Color.B:X2}";

        public string OverlayColorHex
            => $"#{OverlayColorPicker.Color.R:X2}{OverlayColorPicker.Color.G:X2}{OverlayColorPicker.Color.B:X2}";
        public SolidColorBrush PrimaryButtonBackground { get; private set; } = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 199, 209, 229));
        public SolidColorBrush PrimaryColorBrush { get; private set; } = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 199, 209, 229));
        public SolidColorBrush MainContentColorBrush { get; private set; } = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        public SolidColorBrush SidebarBackgroundColorBrush { get; private set; } = new SolidColorBrush(Windows.UI.Color.FromArgb(204, 30, 30, 30));
        public SolidColorBrush FontColorBrush { get; private set; } = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        public SolidColorBrush ShortcutGridFontColorBrush { get; private set; } = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        public SolidColorBrush OverlayColorBrush { get; private set; } = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 0, 0, 0));

        private string _iniPath = string.Empty;
        private IniHelper? _ini;
        private bool _loading;
        private Windows.UI.Color? _pendingTextColor;
        private Windows.UI.Color? _pendingButtonTextColor;
        private TextBox? _recordingHotkeyTextBox;
        private string _recordingHotkeyOriginalText = string.Empty;
        private bool _allowHotkeyFocusFromPointer;
        private bool _isSettingsScrollBarDragging;
        private double _settingsScrollBarPointerOffset;
        private double _settingsScrollBarThumbOffset;
        public void UpdateSettingsScrollBar(double viewportHeight, double scrollableHeight, double trackHeight)
        {
            if (viewportHeight <= 0 || scrollableHeight <= 1 || trackHeight <= 0)
            {
                SettingsScrollBarTrack.Opacity = 0;
                SettingsScrollBarTrack.IsHitTestVisible = false;
                return;
            }

            SettingsScrollBarTrack.Opacity = 1;
            SettingsScrollBarTrack.IsHitTestVisible = true;
            double extent = viewportHeight + scrollableHeight;
            double thumbHeight = Math.Clamp(trackHeight * viewportHeight / extent, Math.Min(28, trackHeight), trackHeight);
            double maxOffset = Math.Max(0, trackHeight - thumbHeight);
            _settingsScrollBarThumbOffset = maxOffset * Math.Clamp(ScrollViewerSettingView.VerticalOffset / scrollableHeight, 0, 1);
            SettingsScrollBarThumb.Height = thumbHeight;
            SettingsScrollBarThumb.Margin = new Thickness(0);
            SettingsScrollBarThumb.VerticalAlignment = VerticalAlignment.Top;
            SettingsScrollBarThumb.RenderTransform = new TranslateTransform { Y = _settingsScrollBarThumbOffset };
        }

        public void RefreshScrollBar()
        {
            if (!IsLoaded)
                return;

            DispatcherQueue.TryEnqueue(() =>
                UpdateSettingsScrollBar(
                    ScrollViewerSettingView.ViewportHeight,
                    ScrollViewerSettingView.ScrollableHeight,
                    SettingsScrollBarTrack.ActualHeight));
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                UpdateSettingsScrollBar(
                    ScrollViewerSettingView.ViewportHeight,
                    ScrollViewerSettingView.ScrollableHeight,
                    SettingsScrollBarTrack.ActualHeight));
        }

        public event EventHandler? BackRequested;
        public event EventHandler? SettingsChanged;
        public event EventHandler? CategoryNavigationSettingsChanged;
        public event EventHandler? ImportProgramsRequested;
        public event EventHandler? ResetSettingsRequested;

        public event EventHandler? ClearIconCacheRequested;
        public event EventHandler? DatabaseRestoredRequested;
        public event EventHandler? LanguageChanged;
        public event EventHandler? ScrollBarRefreshRequested;

        public void ApplyScrollBarPrimaryBrush(Brush brush)
        {
            SettingsScrollBarThumb.Background = brush;
        }

        private void SettingsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScrollBarRefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SettingsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            ScrollBarRefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SettingsScrollBarTrack_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScrollBarRefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SettingsScrollBarTrack_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            double thumbHeight = SettingsScrollBarThumb.ActualHeight;
            double availableTravel = Math.Max(1, SettingsScrollBarTrack.ActualHeight - thumbHeight);
            double ratio = Math.Clamp((e.GetCurrentPoint(SettingsScrollBarTrack).Position.Y - thumbHeight / 2) / availableTravel, 0, 1);
            ScrollViewerSettingView.ChangeView(null, ratio * ScrollViewerSettingView.ScrollableHeight, null, true);
            ScrollBarRefreshRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void SettingsScrollBarThumb_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            SettingsScrollBarThumb.CapturePointer(e.Pointer);
            _settingsScrollBarPointerOffset = e.GetCurrentPoint(SettingsScrollBarTrack).Position.Y - _settingsScrollBarThumbOffset;
            _isSettingsScrollBarDragging = true;
            e.Handled = true;
        }

        private void SettingsScrollBarThumb_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isSettingsScrollBarDragging)
                return;

            double maxOffset = Math.Max(0, SettingsScrollBarTrack.ActualHeight - SettingsScrollBarThumb.ActualHeight);
            if (maxOffset <= 0)
                return;

            double y = e.GetCurrentPoint(SettingsScrollBarTrack).Position.Y;
            double thumbOffset = Math.Clamp(y - _settingsScrollBarPointerOffset, 0, maxOffset);
            _settingsScrollBarThumbOffset = thumbOffset;
            SettingsScrollBarThumb.RenderTransform = new TranslateTransform { Y = thumbOffset };
            ScrollViewerSettingView.ChangeView(null, thumbOffset / maxOffset * ScrollViewerSettingView.ScrollableHeight, null, true);
            e.Handled = true;
        }

        private void SettingsScrollBarThumb_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndSettingsScrollBarDrag();
        }

        private void SettingsScrollBarThumb_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            EndSettingsScrollBarDrag();
        }

        private void EndSettingsScrollBarDrag()
        {
            _isSettingsScrollBarDragging = false;
            ScrollBarRefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        public SettingsView()
        {
            // XAML 初始化控件时可能触发 SelectionChanged；此时 Configure 尚未设置 _ini。
            _loading = true;
            InitializeComponent();
            TextBoxCornerRadiusHelper.Apply(this);
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;
            foreach (TextBox textBox in GetHotkeyTextBoxes())
            {
                textBox.GettingFocus += HotkeyTextBox_GettingFocus;
                textBox.GotFocus += HotkeyTextBox_GotFocus;
            }
            SettingsRoot.AddHandler(
                UIElement.PointerPressedEvent,
                new PointerEventHandler(SettingsRoot_PointerPressed),
                handledEventsToo: true);
            SettingsRoot.AddHandler(
                UIElement.PointerReleasedEvent,
                new PointerEventHandler(SettingsRoot_PointerReleased),
                handledEventsToo: true);
            SettingsCategoryList.SelectedIndex = 0;
            SettingsCategoryList.Loaded += SettingsCategoryList_Loaded;
            SettingsCategoryList.ContainerContentChanging += SettingsCategoryList_ContainerContentChanging;
            _loading = false;
            Loaded += SettingsView_Loaded;
            Unloaded += SettingsView_Unloaded;
        }

        private void LanguageService_LanguageChanged(object? sender, EventArgs e)
        {
            if (!IsLoaded)
                return;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsLoaded)
                {
                    Bindings?.Update();
                    UpdateSidebarBgControls();
                    UpdateContentBgControls();
                }
            });
        }
        //获取系统安装的字体族列表
        private List<string> GetInstalledFontFamilies()
        {
            try
            {
                return CanvasTextFormat.GetSystemFontFamilies()
                    .OrderBy(f => f)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[字体] 枚举失败: {ex.Message}");
                return new List<string>();
            }
        }
        private void LoadFontFamilies()
        {
            var options = new List<FontFamilyOption>
    {
        new FontFamilyOption { DisplayName = "系统默认", FontFamilyName = null }
    };

            try
            {
                var fonts = SystemFontHelper.GetLocalizedFontFamilies();
                System.Diagnostics.Debug.WriteLine($"[字体] 枚举到 {fonts.Count} 个字体");

                foreach (var name in fonts)
                {
                    options.Add(new FontFamilyOption
                    {
                        DisplayName = name,
                        FontFamilyName = name   // 本地化名同时作为 FontFamilyName
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[字体] 加载失败: {ex.Message}");
            }

            FontFamilyComboBox.ItemsSource = options;
        }

        private void LoadLanguages(string selectedLanguageCode)
        {
            LanguageComboBox.ItemsSource = LanguageService.GetLanguages()
                .Select(language => new LanguageSelectionOption
                {
                    Code = language.Code,
                    DisplayName = language.DisplayName
                })
                .ToList();

            var languages = LanguageComboBox.ItemsSource as List<LanguageSelectionOption>;
            int selectedIndex = languages?.FindIndex(language =>
                string.Equals(language.Code, selectedLanguageCode, StringComparison.OrdinalIgnoreCase)) ?? -1;
            LanguageComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }
        private void ImportInstalledPrograms_Click(object sender, RoutedEventArgs e)
        {
            // 把请求抛给 MainWindow
            ImportProgramsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ManageProgramCategoryHistory_Click(object sender, RoutedEventArgs e)
        {
            var window = new ProgramCategoryHistoryWindow();
            ModalWindowHelper.ShowModal(App.MainWindow, window);
        }
        private void SettingsView_FontSizeChanged(object? sender, EventArgs e)
        {
            if (!IsLoaded)
                return;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsLoaded)
                    Bindings?.Update();
            });
        }

        public void Configure(string iniPath)
        {
            _iniPath = iniPath;
            _ini = new IniHelper(iniPath);
        }

        public void ApplyCalculatedTextColor(Windows.UI.Color color)
        {
            _pendingTextColor = color;
            if (!IsLoaded)
                return;

            DispatcherQueue.TryEnqueue(ApplyPendingTextColor);
        }

        public void ApplyBackButtonTextColor(Windows.UI.Color color)
        {
            var brush = new SolidColorBrush(color);
            BackButton.Foreground = brush;

            int childCount = VisualTreeHelper.GetChildrenCount(BackButton);
            for (int i = 0; i < childCount; i++)
                ApplyForegroundToVisualTree(VisualTreeHelper.GetChild(BackButton, i), brush);
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            FontSizeHelper.FontSizeChanged -= SettingsView_FontSizeChanged;
            FontSizeHelper.FontSizeChanged += SettingsView_FontSizeChanged;
            _loading = true;
            // ⭐ 先枚举字体、填充 ComboBox
            LoadFontFamilies();
            if (_ini != null)
                LoadSettings();
            else
                FontSizeScaleSlider.Value = FontSizeHelper.CurrentFontSizeStep;
            _loading = false;

            FontSizeHelper.ApplyGlobalFontSizeToTree(this);

            // 官网/GitHub 链接：没配置就隐藏，避免留一个打不开的死链。
            if (string.IsNullOrWhiteSpace(SupportInfo.GitHubUrl))
            {
                WebsiteButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                WebsiteButton.Visibility = Visibility.Visible;
                WebsiteButton.NavigateUri = new Uri(SupportInfo.GitHubUrl);
            }

            DispatcherQueue.TryEnqueue(ApplyPendingTextColor);
            DispatcherQueue.TryEnqueue(ApplySettingsButtonTextColors);
        }

        private void ApplyPendingTextColor()
        {
            if (!_pendingTextColor.HasValue || !IsLoaded)
                return;
            System.Diagnostics.Debug.WriteLine(
        $"[SettingsView] ApplyPendingTextColor = " +
        $"#{_pendingTextColor.Value.A:X2}{_pendingTextColor.Value.R:X2}" +
        $"{_pendingTextColor.Value.G:X2}{_pendingTextColor.Value.B:X2}");
            ApplyForegroundToVisualTree(this, new SolidColorBrush(_pendingTextColor.Value));
            ApplySettingsButtonTextColors();
        }

        private void SettingsCategoryList_Loaded(object sender, RoutedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(ApplyPendingTextColor);
        }

        private void SettingsCategoryList_ContainerContentChanging(
            ListViewBase sender,
            ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
                return;

            FontSizeHelper.ApplyGlobalFontSizeToTree(args.ItemContainer);

            if (!_pendingTextColor.HasValue)
                return;

            var brush = new SolidColorBrush(_pendingTextColor.Value);
            args.ItemContainer.Foreground = brush;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_pendingTextColor.HasValue)
                    return;

                ApplyForegroundToVisualTree(
                    args.ItemContainer,
                    new SolidColorBrush(_pendingTextColor.Value));
            });
        }
        public SolidColorBrush BackButtonForeground { get; private set; }
    = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

        private void ApplyForegroundToVisualTree(DependencyObject root, Brush brush)
        {
            if (root is FrameworkElement fe && (fe.Tag as string) == "SkipForeground") return;

            if (root is Control control)
                control.Foreground = brush;
            else if (root is TextBlock textBlock)
                textBlock.Foreground = brush;
            else if (root is FontIcon fontIcon)
                fontIcon.Foreground = brush;

            if (root is TextBox textBox)
                textBox.PlaceholderForeground = brush;

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
                ApplyForegroundToVisualTree(VisualTreeHelper.GetChild(root, i), brush);
        }

        private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            FontSizeHelper.FontSizeChanged -= SettingsView_FontSizeChanged;
        }

        private void LoadSettings()
        {
            _loading = true;

            int savedThemePreset = ReadInt("Theme", "Preset", SettingsDefaults.ThemePreset);
            ThemeComboBox.SelectedIndex = ClampComboBoxIndex(ThemeComboBox, savedThemePreset);
            if (ThemeComboBox.SelectedIndex != savedThemePreset)
                _ini?.WriteValue("Theme", "Preset", ThemeComboBox.SelectedIndex.ToString(CultureInfo.InvariantCulture));

            PrimaryColorPicker.Color = ParseColor(Read("Global", "PrimaryBackground", SettingsDefaults.PrimaryBackground));
            PrimaryButtonBackground = new SolidColorBrush(PrimaryColorPicker.Color);
            PrimaryColorBrush = new SolidColorBrush(PrimaryColorPicker.Color);

            MainContentColorPicker.Color = ParseColor(Read("Global", "MainContentBackground", SettingsDefaults.MainContentBackground));
            SidebarBackgroundColorPicker.Color = ParseColor(Read("Sidebar", "BackgroundColor", SettingsDefaults.SidebarBackgroundColor));
            FontColorPicker.Color = ParseColor(Read("Global", "PrimaryFontColor", SettingsDefaults.PrimaryFontColor));
            ShortcutGridFontColorPicker.Color = ParseColor(Read("Global", "ShortcutGridFontColor", SettingsDefaults.ShortcutGridFontColor));
            OverlayColorPicker.Color = ParseColor(Read("System", "OverlayColor", SettingsDefaults.OverlayColor));

            MainContentColorBrush = new SolidColorBrush(MainContentColorPicker.Color);
            SidebarBackgroundColorBrush = new SolidColorBrush(SidebarBackgroundColorPicker.Color);
            FontColorBrush = new SolidColorBrush(FontColorPicker.Color);
            ShortcutGridFontColorBrush = new SolidColorBrush(ShortcutGridFontColorPicker.Color);
            OverlayColorBrush = new SolidColorBrush(OverlayColorPicker.Color);

            Bindings?.Update();

            SidebarBackgroundModeComboBox.SelectedIndex = ClampComboBoxIndex(
                SidebarBackgroundModeComboBox,
                ReadInt("Sidebar", "BackgroundMode", SettingsDefaults.SidebarBackgroundMode));
            ContentBackgroundModeComboBox.SelectedIndex = ClampComboBoxIndex(
                ContentBackgroundModeComboBox,
                ReadInt("ContentArea", "BackgroundMode", SettingsDefaults.ContentBackgroundMode));
            UpdateSidebarBgControls();
            UpdateContentBgControls();

            OverlayOpacitySlider.Value = ReadDouble("System", "OverlayOpacity", SettingsDefaults.OverlayOpacity);
            string storedFontStep = Read("System", "FontSizeStep", string.Empty);
            int initialFontStep;
            if (int.TryParse(storedFontStep, NumberStyles.Integer, CultureInfo.InvariantCulture, out int savedStep))
            {
                initialFontStep = Math.Clamp(savedStep, 0, 4);
            }
            else
            {
                double legacyScale = ReadDouble("System", "FontSizeScale", SettingsDefaults.FontSizeScale);
                initialFontStep = FontSizeHelper.GetFontSizeStepFromScale(legacyScale);
            }
            FontSizeScaleSlider.Value = initialFontStep;
            FontSizeHelper.SetGlobalFontSizeStep(initialFontStep);
            if (!int.TryParse(storedFontStep, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                _ini?.WriteValue("System", "FontSizeStep", initialFontStep.ToString(CultureInfo.InvariantCulture));

            string savedFont = Read("System", "FontFamilyName", SettingsDefaults.FontFamilyName);
            var fontOptions = FontFamilyComboBox.ItemsSource as List<FontFamilyOption>;
            if (fontOptions != null)
            {
                int idx = fontOptions.FindIndex(o =>
                    string.Equals(o.FontFamilyName, savedFont, StringComparison.OrdinalIgnoreCase));
                FontFamilyComboBox.SelectedIndex = ClampComboBoxIndex(FontFamilyComboBox, idx >= 0 ? idx : 0);
            }

            SidebarExpandedToggle.IsOn = ReadBool("Sidebar", "State", SettingsDefaults.SidebarState);
            ShowExtensionToggle.IsOn = ReadBool("Behavior", "ShowFileExtension", SettingsDefaults.ShowFileExtension);
            ShortcutLoadAnimationComboBox.SelectedIndex = ClampComboBoxIndex(
                ShortcutLoadAnimationComboBox,
                ReadInt("System", "ShortcutLoadAnimation", SettingsDefaults.ShortcutLoadAnimation));
            SuccessNotificationToggle.IsOn = ReadBool("Notification", "ShowSuccess", SettingsDefaults.ShowSuccess);
            ReloadNotificationToggle.IsOn = ReadBool("Notification", "ShowReloadCompleted", SettingsDefaults.ShowReloadCompleted);
            ConfirmDeleteToggle.IsOn = ReadBool("Notification", "ConfirmDelete", SettingsDefaults.ConfirmDelete);

            CategorySortComboBox.SelectedIndex = ClampComboBoxIndex(
                CategorySortComboBox,
                ReadInt("Category", "SortMode", 0));
            CategorySeparatorToggle.IsOn = ReadBool("Category", "ShowSeparator", true);

            string languageCode = Read("System", "LanguageCode", "");
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                languageCode = LanguageService.CurrentLanguageCode;
                _ini?.WriteValue("System", "LanguageCode", languageCode);
            }
            LoadLanguages(languageCode);
            AutoStartToggle.IsOn = ReadBool("System", "AutoStart", SettingsDefaults.AutoStart);
            TrayOnExitToggle.IsOn = ReadBool("System", "TrayOnExit", SettingsDefaults.TrayOnExit);
            HideOnMinimizeToggle.IsOn = ReadBool("System", "MinimizeToTray", SettingsDefaults.MinimizeToTray);
            RestoreLastTabToggle.IsOn = ReadBool("System", "RestoreLastTab", SettingsDefaults.RestoreLastTab);
            OpenSearchOnStartupToggle.IsOn = ReadBool("System", "OpenSearchOnStartup", SettingsDefaults.OpenSearchOnStartup);
            ClearSearchSelectionAfterAddToggle.IsOn = ReadBool("System", "ClearSearchSelectionAfterAdd", SettingsDefaults.ClearSearchSelectionAfterAdd);
            ConfirmRecycleBinToggle.IsOn = ReadBool("Notification", "ConfirmRecycleBin", SettingsDefaults.ConfirmRecycleBin);

            HotkeyAddTextBox.Text = Read("Hotkeys", "Add", "Ctrl+1");
            HotkeyCutTextBox.Text = Read("Hotkeys", "Cut", "Ctrl+X");
            HotkeyCopyTextBox.Text = Read("Hotkeys", "Copy", "Ctrl+C");
            HotkeyPasteTextBox.Text = Read("Hotkeys", "Paste", "Ctrl+V");
            HotkeyRenameTextBox.Text = Read("Hotkeys", "Rename", "Ctrl+R");
            HotkeyDeleteTextBox.Text = Read("Hotkeys", "Delete", "Delete");
            HotkeyNewCategoryTextBox.Text = Read("Hotkeys", "NewCategory", "Ctrl+2");
            HotkeyNewTabTextBox.Text = Read("Hotkeys", "NewTab", "Ctrl+3");
            HotkeySelectAllTextBox.Text = Read("Hotkeys", "SelectAll", "Ctrl+A");
            HotkeyFavoriteTextBox.Text = Read("Hotkeys", "Favorite", "Ctrl+Q");

            _loading = false;
            LanguageService.ApplyLanguage(this, languageCode);
            UpdateSidebarBgControls();
            UpdateContentBgControls();
        }

        private void SettingsCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsCategoryList.SelectedItem is not ListViewItem item)
                return;

            string category = item.Tag?.ToString() ?? "Appearance";
            AppearanceSection.Visibility = category == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
            GeneralSection.Visibility = category == "General" ? Visibility.Visible : Visibility.Collapsed;
            HotkeysSection.Visibility = category == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
            DataSection.Visibility = category == "Data" ? Visibility.Visible : Visibility.Collapsed;
            NotificationsSection.Visibility = category == "Notifications" ? Visibility.Visible : Visibility.Collapsed;
            AboutSection.Visibility = category == "About" ? Visibility.Visible : Visibility.Collapsed;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsLoaded)
                    FontSizeHelper.ApplyGlobalFontSizeToTree(this);
            });
            ScrollBarRefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        private string Read(string section, string key, string fallback) => _ini?.ReadValue(section, key, fallback) ?? fallback;
        private int ReadInt(string section, string key, int fallback) => int.TryParse(Read(section, key, fallback.ToString()), out var value) ? value : fallback;
        private static int ClampComboBoxIndex(ComboBox comboBox, int index)
            => comboBox.Items.Count == 0 ? -1 : Math.Clamp(index, 0, comboBox.Items.Count - 1);
        private double ReadDouble(string section, string key, double fallback)
            => double.TryParse(Read(section, key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value)
                ? value
                : fallback;
        private bool ReadBool(string section, string key, bool fallback) => _ini?.ReadBool(section, key, fallback) ?? fallback;

        private void SaveColor(string section, string key, Windows.UI.Color color)
            => _ini?.WriteValue(section, key, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
        private void ApplyThemePresetAndNotify(int preset)
        {
            if (_loading) return;

            _ini?.WriteValue("Theme", "Preset", preset.ToString());
            ApplyThemePreset(preset);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void TraceHotkey(string stage, TextBox? textBox = null, string? detail = null)
        {
            var focused = FocusManager.GetFocusedElement(XamlRoot) as FrameworkElement;
            System.Diagnostics.Debug.WriteLine(
                $"[HotkeyDebug] {stage} | source={textBox?.Name ?? "-"} | recording={_recordingHotkeyTextBox?.Name ?? "-"} | focus={focused?.Name ?? focused?.GetType().Name ?? "-"} | value='{textBox?.Text ?? ""}' | {detail}");
        }

        private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            TraceHotkey("GotFocus", textBox);
            if (textBox != null && ReferenceEquals(_recordingHotkeyTextBox, textBox))
                _allowHotkeyFocusFromPointer = false;
        }

        private void HotkeyTextBox_GettingFocus(UIElement sender, GettingFocusEventArgs e)
        {
            if (e.NewFocusedElement is not TextBox textBox || !GetHotkeyTextBoxes().Contains(textBox))
                return;

            if (_allowHotkeyFocusFromPointer && ReferenceEquals(_recordingHotkeyTextBox, textBox))
            {
                TraceHotkey("GettingFocusAllowed", textBox);
                return;
            }

            TraceHotkey("GettingFocusCanceled", textBox, $"inputDevice={e.InputDevice}; pointerAllowed={_allowHotkeyFocusFromPointer}");
            e.TryCancel();
        }

        private void HotkeyTextBox_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                TraceHotkey("PointerPressed", textBox);
                BeginHotkeyRecording(textBox);
            }
        }

        private void BeginHotkeyRecording(TextBox? textBox)
        {
            TraceHotkey("BeginRecording", textBox);
            if (textBox == null || ReferenceEquals(_recordingHotkeyTextBox, textBox))
            {
                TraceHotkey("BeginRecordingSkipped", textBox);
                return;
            }

            if (_recordingHotkeyTextBox != null)
                CancelHotkeyRecording(_recordingHotkeyTextBox);

            _recordingHotkeyTextBox = textBox;
            _recordingHotkeyOriginalText = textBox.Text;
            textBox.Text = LanguageService.Translate("请按下按键");
            TraceHotkey("RecordingStarted", textBox, $"original='{_recordingHotkeyOriginalText}'");
        }

        private void SettingsRoot_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;
            TextBox? textBox = source == null
                ? null
                : GetHotkeyTextBoxes().FirstOrDefault(candidate => IsDescendantOf(source, candidate));
            _allowHotkeyFocusFromPointer = textBox != null;
            TraceHotkey("RootPointerPressed", textBox, $"pointerSource={source?.GetType().Name ?? "-"}");
            BeginHotkeyRecording(textBox);
            if (textBox != null)
                TraceHotkey("PointerFocusRequested", textBox, $"accepted={textBox.Focus(FocusState.Pointer)}");
        }

        private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            TraceHotkey("LostFocus", textBox);
            if (_recordingHotkeyTextBox != textBox)
                return;

            CancelHotkeyRecording(textBox);
        }

        private void SettingsRoot_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            TraceHotkey("RootPointerReleased", _recordingHotkeyTextBox, $"pointerSource={source?.GetType().Name ?? "-"}");
            DispatcherQueue.TryEnqueue(() => _allowHotkeyFocusFromPointer = false);
            if (_recordingHotkeyTextBox == null)
                return;

            if (source != null && IsDescendantOf(source, _recordingHotkeyTextBox))
            {
                TraceHotkey("PointerReleasedInsideRecordingBox", _recordingHotkeyTextBox);
                return;
            }

            CancelHotkeyRecording(_recordingHotkeyTextBox);
        }

        private void CancelHotkeyRecording(TextBox textBox)
        {
            if (_recordingHotkeyTextBox != textBox)
            {
                TraceHotkey("CancelSkipped", textBox);
                return;
            }

            TraceHotkey("CancelRecording", textBox);
            textBox.Text = _recordingHotkeyOriginalText;
            _recordingHotkeyTextBox = null;
            textBox.SelectionStart = 0;
            textBox.SelectionLength = 0;
        }

        private void CompleteHotkeyRecording(TextBox textBox, string gesture)
        {
            TraceHotkey("CompleteRecording", textBox, $"gesture='{gesture}'");
            textBox.Text = gesture;
            SaveHotkey(textBox, gesture);
            _recordingHotkeyTextBox = null;
        }

        private static bool IsDescendantOf(DependencyObject source, DependencyObject ancestor)
        {
            DependencyObject? current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            var keyModifiers = GetCurrentModifiers();
            TraceHotkey("PreviewKeyDown", textBox, $"key={e.Key}; modifiers={keyModifiers}");
            if (_recordingHotkeyTextBox != textBox)
                return;

            e.Handled = true;

            if (IsModifierKey(e.Key))
            {
                string modifierHint = FormatModifiers(keyModifiers);
                textBox.Text = string.IsNullOrEmpty(modifierHint) ? LanguageService.Translate("请按下按键") : modifierHint + "+...";
                TraceHotkey("ModifierRecorded", textBox, $"modifiers={keyModifiers}");
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Escape &&
                keyModifiers == Windows.System.VirtualKeyModifiers.None)
            {
                TraceHotkey("EscapeCanceled", textBox);
                textBox.Text = _recordingHotkeyOriginalText;
                _recordingHotkeyTextBox = null;
                return;
            }

            if (!IsValidHotkey(e.Key, keyModifiers))
            {
                TraceHotkey("InvalidHotkey", textBox, $"key={e.Key}; modifiers={keyModifiers}");
                return;
            }

            string gesture = FormatHotkey(e.Key, keyModifiers);
            if (string.Equals(gesture, _recordingHotkeyOriginalText, StringComparison.OrdinalIgnoreCase))
            {
                TraceHotkey("UnchangedHotkey", textBox, $"gesture='{gesture}'");
                textBox.Text = _recordingHotkeyOriginalText;
                _recordingHotkeyTextBox = null;
                return;
            }

            string? conflict = FindHotkeyConflict(textBox, gesture);
            if (conflict != null)
            {
                TraceHotkey("HotkeyConflict", textBox, $"gesture='{gesture}'; conflict='{conflict}'");
                textBox.Text = _recordingHotkeyOriginalText;
                _recordingHotkeyTextBox = null;
                _ = ShowHotkeyConflictAsync(gesture, conflict);
                return;
            }

            CompleteHotkeyRecording(textBox, gesture);
        }

        private string? FindHotkeyConflict(TextBox currentTextBox, string gesture)
        {
            foreach (TextBox textBox in GetHotkeyTextBoxes())
            {
                if (ReferenceEquals(textBox, currentTextBox))
                    continue;

                if (string.Equals(textBox.Text, gesture, StringComparison.OrdinalIgnoreCase))
                    return GetHotkeyDisplayName(textBox);
            }

            return null;
        }

        private IEnumerable<TextBox> GetHotkeyTextBoxes()
        {
            yield return HotkeyAddTextBox;
            yield return HotkeyCutTextBox;
            yield return HotkeyCopyTextBox;
            yield return HotkeyPasteTextBox;
            yield return HotkeyRenameTextBox;
            yield return HotkeyDeleteTextBox;
            yield return HotkeyNewCategoryTextBox;
            yield return HotkeyNewTabTextBox;
            yield return HotkeySelectAllTextBox;
            yield return HotkeyFavoriteTextBox;
        }

        private static string GetHotkeyDisplayName(TextBox textBox)
            => textBox.Name switch
            {
                "HotkeyAddTextBox" => "添加文件",
                "HotkeyCutTextBox" => "剪切",
                "HotkeyCopyTextBox" => "复制",
                "HotkeyPasteTextBox" => "粘贴",
                "HotkeyRenameTextBox" => "重命名",
                "HotkeyDeleteTextBox" => "删除",
                "HotkeyNewCategoryTextBox" => "新建分类",
                "HotkeyNewTabTextBox" => "新建标签",
                "HotkeySelectAllTextBox" => "全选",
                "HotkeyFavoriteTextBox" => "收藏",
                _ => textBox.Name
            };

        private async Task ShowHotkeyConflictAsync(string gesture, string conflictName)
        {
            var dialog = new ContentDialog
            {
                Title = LanguageService.Translate("快捷键冲突"),
                Content = LanguageService.TranslateFormat("快捷键 {gesture} 已被“{name}”使用，请选择其他组合键。",
                    ("{gesture}", gesture), ("{name}", conflictName)),
                CloseButtonText = LanguageService.Translate("确定"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
                LanguageService.ApplyDialog(dialog, LanguageService.CurrentLanguageCode);
            await dialog.ShowAsync();
        }

        private string ReadHotkeyValue(TextBox textBox)
        {
            string key = GetHotkeyKey(textBox);
            return string.IsNullOrEmpty(key) ? string.Empty : Read("Hotkeys", key, string.Empty);
        }

        private static bool IsModifierKey(Windows.System.VirtualKey key)
            => key is Windows.System.VirtualKey.Control
                or Windows.System.VirtualKey.LeftControl or Windows.System.VirtualKey.RightControl
                or Windows.System.VirtualKey.Shift
                or Windows.System.VirtualKey.LeftShift or Windows.System.VirtualKey.RightShift
                or Windows.System.VirtualKey.Menu
                or Windows.System.VirtualKey.LeftMenu or Windows.System.VirtualKey.RightMenu
                or Windows.System.VirtualKey.LeftWindows or Windows.System.VirtualKey.RightWindows;

        private static bool IsValidHotkey(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers)
        {
            if (IsModifierKey(key)) return false;
            if (modifiers != Windows.System.VirtualKeyModifiers.None) return true;
            return key is >= Windows.System.VirtualKey.F1 and <= Windows.System.VirtualKey.F24
                or Windows.System.VirtualKey.Delete
                or Windows.System.VirtualKey.Insert
                or Windows.System.VirtualKey.Home
                or Windows.System.VirtualKey.End
                or Windows.System.VirtualKey.PageUp
                or Windows.System.VirtualKey.PageDown;
        }

        private static Windows.System.VirtualKeyModifiers GetCurrentModifiers()
        {
            var modifiers = Windows.System.VirtualKeyModifiers.None;
            if (IsKeyDown(Windows.System.VirtualKey.Control))
                modifiers |= Windows.System.VirtualKeyModifiers.Control;
            if (IsKeyDown(Windows.System.VirtualKey.Menu))
                modifiers |= Windows.System.VirtualKeyModifiers.Menu;
            if (IsKeyDown(Windows.System.VirtualKey.Shift))
                modifiers |= Windows.System.VirtualKeyModifiers.Shift;
            if (IsKeyDown(Windows.System.VirtualKey.LeftWindows) ||
                IsKeyDown(Windows.System.VirtualKey.RightWindows))
                modifiers |= Windows.System.VirtualKeyModifiers.Windows;
            return modifiers;
        }

        private static bool IsKeyDown(Windows.System.VirtualKey key)
            => Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(key)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        private static string FormatModifiers(Windows.System.VirtualKeyModifiers modifiers)
        {
            var parts = new List<string>();
            if (modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Menu)) parts.Add("Alt");
            if (modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Windows)) parts.Add("Win");
            return string.Join("+", parts);
        }

        private static string FormatHotkey(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers)
        {
            string keyName = key switch
            {
                Windows.System.VirtualKey.Back => "Backspace",
                Windows.System.VirtualKey.Escape => "Esc",
                Windows.System.VirtualKey.Space => "Space",
                Windows.System.VirtualKey.Enter => "Enter",
                Windows.System.VirtualKey.Tab => "Tab",
                Windows.System.VirtualKey.Delete => "Delete",
                Windows.System.VirtualKey.PageUp => "PageUp",
                Windows.System.VirtualKey.PageDown => "PageDown",
                _ => key.ToString()
            };
            string prefix = FormatModifiers(modifiers);
            return string.IsNullOrEmpty(prefix) ? keyName : $"{prefix}+{keyName}";
        }

        private void SaveHotkey(TextBox textBox, string gesture)
        {
            string key = GetHotkeyKey(textBox);
            if (string.IsNullOrEmpty(key)) return;

            _ini?.WriteValue("Hotkeys", key, gesture);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string GetHotkeyKey(TextBox textBox)
        {
            return textBox.Name switch
            {
                "HotkeyAddTextBox" => "Add",
                "HotkeyCutTextBox" => "Cut",
                "HotkeyCopyTextBox" => "Copy",
                "HotkeyPasteTextBox" => "Paste",
                "HotkeyRenameTextBox" => "Rename",
                "HotkeyDeleteTextBox" => "Delete",
                "HotkeyNewCategoryTextBox" => "NewCategory",
                "HotkeyNewTabTextBox" => "NewTab",
                "HotkeySelectAllTextBox" => "SelectAll",
                "HotkeyFavoriteTextBox" => "Favorite",
                _ => string.Empty
            };
        }
        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyThemePresetAndNotify(ThemeComboBox.SelectedIndex);
        }

        private void ApplyThemePreset(int preset)
        {
            (string primary, string secondary) = preset switch
            {
                0 => ("#FF244A80", "#FFF0F0F0"),
                1 => ("#FF202020", "#FF303030"),
                2 => ("#FF6B7D99", "#FFA9B8CB"),
                3 => ("#FFC62828", "#FFFFCDD2"),
                4 => ("#FF6D4C41", "#FFD7CCC8"),
                _ => (string.Empty, string.Empty)
            };

            if (string.IsNullOrEmpty(primary) || _ini == null)
                return;

            _loading = true;
            Windows.UI.Color primaryColor = ParseColor(primary);
            Windows.UI.Color secondaryColor = ParseColor(secondary);
            Windows.UI.Color sidebarColor = LightenColor(primaryColor, 0.85);
            PrimaryColorPicker.Color = primaryColor;
            MainContentColorPicker.Color = secondaryColor;
            SidebarBackgroundColorPicker.Color = sidebarColor;
            PrimaryButtonBackground = new SolidColorBrush(primaryColor);
            PrimaryColorBrush = new SolidColorBrush(primaryColor);
            MainContentColorBrush = new SolidColorBrush(secondaryColor);
            SidebarBackgroundColorBrush = new SolidColorBrush(sidebarColor);
            _ini.WriteValue("Global", "PrimaryBackground", primary);
            _ini.WriteValue("Global", "MainContentBackground", secondary);
            SaveColor("Sidebar", "BackgroundColor", sidebarColor);
            Bindings?.Update();
            _loading = false;
        }

        private static Windows.UI.Color LightenColor(Windows.UI.Color color, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            byte Lighten(byte channel) => (byte)Math.Round(channel + (255 - channel) * amount);
            return Windows.UI.Color.FromArgb(255, Lighten(color.R), Lighten(color.G), Lighten(color.B));
        }

        private void ThemePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var preset))
                return;

            if (ThemeComboBox.SelectedIndex != preset)
                ThemeComboBox.SelectedIndex = preset;   // 触发 SelectionChanged → 走公共方法
            else
                ApplyThemePresetAndNotify(preset);      // 相同预设，手动调用
        }

        private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (_loading) return;
            if (sender == PrimaryColorPicker)
            {
                SaveColor("Global", "PrimaryBackground", args.NewColor);
                var primaryBrush = new SolidColorBrush(args.NewColor);
                Application.Current.Resources["AppPrimaryBackgroundBrush"] = primaryBrush;
                BlockBox.Helpers.WindowAcrylicHelper.UpdateAll(args.NewColor);
            }
            else if (sender == MainContentColorPicker) SaveColor("Global", "MainContentBackground", args.NewColor);
            else if (sender == SidebarBackgroundColorPicker) SaveColor("Sidebar", "BackgroundColor", args.NewColor);
            else if (sender == OverlayColorPicker) SaveColor("System", "OverlayColor", args.NewColor);
            else if (sender == ShortcutGridFontColorPicker) SaveColor("Global", "ShortcutGridFontColor", args.NewColor);
            else SaveColor("Global", "PrimaryFontColor", args.NewColor);
            if (sender == PrimaryColorPicker)
            {
                PrimaryButtonBackground = new SolidColorBrush(args.NewColor);
                PrimaryColorBrush = new SolidColorBrush(args.NewColor);
            }
            else if (sender == MainContentColorPicker)
                MainContentColorBrush = new SolidColorBrush(args.NewColor);
            else if (sender == SidebarBackgroundColorPicker)
                SidebarBackgroundColorBrush = new SolidColorBrush(args.NewColor);
            else if (sender == FontColorPicker)
                FontColorBrush = new SolidColorBrush(args.NewColor);
            else if (sender == ShortcutGridFontColorPicker)
                ShortcutGridFontColorBrush = new SolidColorBrush(args.NewColor);
            else if (sender == OverlayColorPicker)
                OverlayColorBrush = new SolidColorBrush(args.NewColor);
            Bindings?.Update();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ColorSwatch_Tapped(object sender, TappedRoutedEventArgs e)
        {
            DependencyObject? current = sender as DependencyObject;
            while (current != null && current is not Expander)
                current = VisualTreeHelper.GetParent(current);

            if (current is Expander expander)
            {
                expander.IsExpanded = true;
                e.Handled = true;
            }
        }

        private void ColorPickerDone_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.Tag is not string flyoutName) return;
            if (FindName(flyoutName) is Flyout flyout)
                flyout.Hide();
        }

        private void OverlayOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_loading)
            {
                _ini?.WriteValue("System", "OverlayOpacity", e.NewValue.ToString(CultureInfo.InvariantCulture));
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void FontSizeScaleSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loading || !double.IsFinite(e.NewValue)) return;
            int value = Math.Clamp((int)Math.Round(e.NewValue), 0, 4);
            if (Math.Abs(FontSizeScaleSlider.Value - value) > 0.001)
            {
                FontSizeScaleSlider.Value = value;
                return;
            }

            _ini?.WriteValue("System", "FontSizeStep", value.ToString(CultureInfo.InvariantCulture));
            FontSizeHelper.SetGlobalFontSizeStep(value);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            if (FontFamilyComboBox.SelectedItem is FontFamilyOption option)
            {
                _ini?.WriteValue("System", "FontFamilyName",
                    option.FontFamilyName ?? "");
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ShortcutLoadAnimationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || ShortcutLoadAnimationComboBox.SelectedIndex < 0)
                return;

            _ini?.WriteValue(
                "System",
                "ShortcutLoadAnimation",
                ShortcutLoadAnimationComboBox.SelectedIndex.ToString(CultureInfo.InvariantCulture));
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading && LanguageComboBox.SelectedItem is LanguageSelectionOption language)
            {
                _ini?.WriteValue("System", "LanguageCode", language.Code);
                LanguageService.ApplyLanguage(this, language.Code);
                Bindings?.Update();
                LanguageChanged?.Invoke(this, EventArgs.Empty);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ComboSetting_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || sender is not ComboBox combo) return;

            string section;
            string key;
            switch (combo.Name)
            {
                case "SidebarBackgroundModeComboBox":
                    section = "Sidebar";
                    key = "BackgroundMode";
                    break;
                case "ContentBackgroundModeComboBox":
                    section = "ContentArea";
                    key = "BackgroundMode";
                    break;
                case "CategorySortComboBox":
                    section = "Category";
                    key = "SortMode";
                    break;
                default:
                    return;
            }

            _ini?.WriteValue(section, key, combo.SelectedIndex.ToString());
            if (combo.Name == "SidebarBackgroundModeComboBox")
                UpdateSidebarBgControls();
            else if (combo.Name == "ContentBackgroundModeComboBox")
                UpdateContentBgControls();
            else if (combo.Name == "CategorySortComboBox")
                CategoryNavigationSettingsChanged?.Invoke(this, EventArgs.Empty);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        private async void SidebarBgPickButton_Click(object sender, RoutedEventArgs e)
        {
            int mode = SidebarBackgroundModeComboBox.SelectedIndex;
            string? path = mode == 1
                ? await PickMediaFileAsync(isVideo: false)
                : mode == 2
                    ? await PickMediaFileAsync(isVideo: true)
                    : null;

            if (string.IsNullOrEmpty(path)) return;

            string key = mode == 1 ? "BackgroundImagePath" : "BackgroundVideoPath";
            _ini?.WriteValue("Sidebar", key, path);
            UpdateSidebarBgControls();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void ContentBgPickButton_Click(object sender, RoutedEventArgs e)
        {
            int mode = ContentBackgroundModeComboBox.SelectedIndex;
            string? path = mode == 1
                ? await PickMediaFileAsync(isVideo: false)
                : mode == 2
                    ? await PickMediaFileAsync(isVideo: true)
                    : null;

            if (string.IsNullOrEmpty(path)) return;

            string key = mode == 1 ? "BackgroundImagePath" : "BackgroundVideoPath";
            _ini?.WriteValue("ContentArea", key, path);
            UpdateContentBgControls();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SidebarBgClearButton_Click(object sender, RoutedEventArgs e)
        {
            int mode = SidebarBackgroundModeComboBox.SelectedIndex;
            string key = mode == 1 ? "BackgroundImagePath" : "BackgroundVideoPath";
            _ini?.WriteValue("Sidebar", key, "");
            UpdateSidebarBgControls();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ContentBgClearButton_Click(object sender, RoutedEventArgs e)
        {
            int mode = ContentBackgroundModeComboBox.SelectedIndex;
            string key = mode == 1 ? "BackgroundImagePath" : "BackgroundVideoPath";
            _ini?.WriteValue("ContentArea", key, "");
            UpdateContentBgControls();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private static async Task<string?> PickMediaFileAsync(bool isVideo)
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = isVideo ? PickerLocationId.VideosLibrary : PickerLocationId.PicturesLibrary
            };

            if (isVideo)
            {
                picker.FileTypeFilter.Add(".mp4");
                picker.FileTypeFilter.Add(".mkv");
                picker.FileTypeFilter.Add(".webm");
                picker.FileTypeFilter.Add(".mov");
                picker.FileTypeFilter.Add(".avi");
            }
            else
            {
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".gif");
                picker.FileTypeFilter.Add(".webp");
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(BlockBox.App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }

        private void UpdateSidebarBgControls()
        {
            int mode = SidebarBackgroundModeComboBox.SelectedIndex;
            string path = mode switch
            {
                1 => Read("Sidebar", "BackgroundImagePath", ""),
                2 => Read("Sidebar", "BackgroundVideoPath", ""),
                _ => ""
            };

            SidebarBgPickButton.IsEnabled = mode != 0;
            SidebarBgClearButton.IsEnabled = mode != 0 && !string.IsNullOrEmpty(path);
            SidebarBgPathText.Text = string.IsNullOrEmpty(path)
                ? LanguageService.Translate(mode == 0 ? "纯色模式" : "未选择文件")
                : System.IO.Path.GetFileName(path);
        }

        private void UpdateContentBgControls()
        {
            int mode = ContentBackgroundModeComboBox.SelectedIndex;
            string path = mode switch
            {
                1 => Read("ContentArea", "BackgroundImagePath", ""),
                2 => Read("ContentArea", "BackgroundVideoPath", ""),
                _ => ""
            };

            ContentBgPickButton.IsEnabled = mode != 0;
            ContentBgClearButton.IsEnabled = mode != 0 && !string.IsNullOrEmpty(path);
            ContentBgPathText.Text = string.IsNullOrEmpty(path)
                ? LanguageService.Translate(mode == 0 ? "纯色模式" : "未选择文件")
                : System.IO.Path.GetFileName(path);
        }
        private void SidebarExpandedToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;   // ⭐ 加

            _ini?.WriteValue("Sidebar", "State",
                SidebarExpandedToggle.IsOn.ToString().ToLowerInvariant());
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ShowExtensionToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;   // ⭐ 加这一行

            _ini?.WriteValue("Behavior", "ShowFileExtension",
                ShowExtensionToggle.IsOn.ToString().ToLowerInvariant());
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void GenericSetting_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading || sender is not ToggleSwitch toggle) return;
            string key = toggle.Name switch
            {
                "SuccessNotificationToggle" => "ShowSuccess",
                "ReloadNotificationToggle" => "ShowReloadCompleted",
                "ConfirmDeleteToggle" => "ConfirmDelete",
                "ConfirmRecycleBinToggle" => "ConfirmRecycleBin",
                "CategorySeparatorToggle" => "ShowSeparator",
                "AutoStartToggle" => "AutoStart",
                "TrayOnExitToggle" => "TrayOnExit",
                "HideOnMinimizeToggle" => "MinimizeToTray",
                "RestoreLastTabToggle" => "RestoreLastTab",
                "OpenSearchOnStartupToggle" => "OpenSearchOnStartup",
                "ClearSearchSelectionAfterAddToggle" => "ClearSearchSelectionAfterAdd",
                // 兜底：开关名默认等于“键 + Toggle”，避免再出现写进去的键名和读取的键名不一致。
                _ => toggle.Name.EndsWith("Toggle", StringComparison.Ordinal)
                    ? toggle.Name[..^"Toggle".Length]
                    : toggle.Name
            };
            string section = toggle.Name is "CategorySeparatorToggle" ? "Category" : toggle.Name.Contains("Notification") || toggle.Name.Contains("Confirm") ? "Notification" : "System";
            _ini?.WriteValue(section, key, toggle.IsOn.ToString().ToLowerInvariant());
            if (toggle.Name == "CategorySeparatorToggle")
                CategoryNavigationSettingsChanged?.Invoke(this, EventArgs.Empty);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

        private static Windows.UI.Color ParseColor(string value)
        {
            string hex = value.TrimStart('#');
            if (hex.Length == 6) hex = "FF" + hex;
            if (hex.Length != 8) return Windows.UI.Color.FromArgb(255, 255, 255, 255);
            return Windows.UI.Color.FromArgb(
                byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber));
        }

        /// <summary>切到指定 Tag 的设置页（例如从“关于 Block Box”直接打开“关于我们”）。</summary>
        public void SelectPage(string tag)
        {
            for (int i = 0; i < SettingsCategoryList.Items.Count; i++)
            {
                if (SettingsCategoryList.Items[i] is ListViewItem item &&
                    string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
                {
                    SettingsCategoryList.SelectedIndex = i;
                    return;
                }
            }
        }

        public void ResetToDefaultPage()
        {
            // 复位到"主题 / 外观"（第一项）
            // 先设 -1 再设 0：即使当前已经是 0，也能确保触发一次
            // SelectionChanged，让内容区刷新成默认页
            SettingsCategoryList.SelectedIndex = -1;
            SettingsCategoryList.SelectedIndex = 0;

            // 内容滚动也复位到顶部（否则默认页里之前滚动到的位置会保留）
            ScrollViewerSettingView?.ChangeView(
                horizontalOffset: null,
                verticalOffset: 0,
                zoomFactor: null,
                disableAnimation: true);
        }

        public void ReloadFromIni()
        {
            if (_ini == null) return;
            LoadSettings();
        }
        public string DebugBackButtonForeground()
        {
            var fg = BackButton.Foreground;
            if (fg is SolidColorBrush s)
                return $"#{s.Color.A:X2}{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}";
            return fg?.GetType().Name ?? "null";
        }

        public void ApplySettingsButtonsTextColor(Windows.UI.Color color)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BtnColor] ApplySettingsButtonsTextColor = #{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

            var brush = new SolidColorBrush(color);
            _pendingButtonTextColor = color;

            // ⭐ 只改 BackButton：更新绑定源 + 刷新 x:Bind
            //    不写 BackButton.Foreground = brush，那会破坏 x:Bind 绑定
            BackButtonForeground = brush;

            // 其他 6 个按钮保持原逻辑（你想只改 BackButton 的话，把下面这些删掉就行）
            ExportSettingsButton.Foreground = brush;
            ImportSettingsButton.Foreground = brush;
            BackupDatabaseButton.Foreground = brush;
            RestoreDatabaseButton.Foreground = brush;
            ClearIconCacheButton.Foreground = brush;
            ResetSettingsButton.Foreground = brush;
            Bindings?.Update();
            if (IsLoaded)
                ApplySettingsButtonTextColors();
        }

        private void ApplySettingsButtonTextColors()
        {
            if (!_pendingButtonTextColor.HasValue)
                return;

            var brush = new SolidColorBrush(_pendingButtonTextColor.Value);
            Button[] buttons =
            {
                BackButton,
                ImportProgramsButton,
                ManageProgramCategoryHistoryButton,
                ExportSettingsButton,
                ImportSettingsButton,
                BackupDatabaseButton,
                RestoreDatabaseButton,
                ClearIconCacheButton,
                ResetSettingsButton,
                CheckUpdatesButton,
                FeedbackButton
            };

            foreach (var button in buttons)
            {
                button.Foreground = brush;
                int childCount = VisualTreeHelper.GetChildrenCount(button);
                for (int i = 0; i < childCount; i++)
                    ApplyForegroundToVisualTree(VisualTreeHelper.GetChild(button, i), brush);
            }

            WebsiteButton.Foreground = brush;
            int websiteChildCount = VisualTreeHelper.GetChildrenCount(WebsiteButton);
            for (int i = 0; i < websiteChildCount; i++)
                ApplyForegroundToVisualTree(VisualTreeHelper.GetChild(WebsiteButton, i), brush);
        }
        private async void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_ini == null || string.IsNullOrEmpty(_iniPath) || !File.Exists(_iniPath))
                    throw new FileNotFoundException("当前配置文件不存在，无法导出。", _iniPath);

                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = "BlockBox-settings.ini"
                };
                picker.FileTypeChoices.Add("INI 配置文件", new[] { ".ini" });

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(BlockBox.App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                var file = await picker.PickSaveFileAsync();
                if (file == null)
                    return;

                var sourceFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(_iniPath);
                await sourceFile.CopyAndReplaceAsync(file);
                await ShowOperationMessageAsync("导出配置", "配置已成功导出。", isError: false);
            }
            catch (Exception ex)
            {
                await ShowOperationMessageAsync("导出配置失败", ex.Message, isError: true);
            }
        }

        private async void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ini == null || string.IsNullOrEmpty(_iniPath))
                return;

            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary
                };
                picker.FileTypeFilter.Add(".ini");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(BlockBox.App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                var file = await picker.PickSingleFileAsync();
                if (file == null)
                    return;

                var confirm = new ContentDialog
                {
                    Title = LanguageService.Translate("导入配置"),
                    Content = LanguageService.Translate("导入后将覆盖当前设置，当前配置会先备份为 setting.ini.bak。是否继续？"),
                    PrimaryButtonText = LanguageService.Translate("导入"),
                    CloseButtonText = LanguageService.Translate("取消"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot
                };
                LanguageService.ApplyDialog(confirm, LanguageService.CurrentLanguageCode);
                if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                    return;

                string? directory = Path.GetDirectoryName(_iniPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                if (File.Exists(_iniPath))
                    File.Copy(_iniPath, _iniPath + ".bak", overwrite: true);

                File.Copy(file.Path, _iniPath, overwrite: true);
                LoadSettings();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                await ShowOperationMessageAsync("导入配置", "配置已成功导入。", isError: false);
            }
            catch (Exception ex)
            {
                await ShowOperationMessageAsync("导入配置失败", ex.Message, isError: true);
            }
        }

        private async Task ShowOperationMessageAsync(string title, string message, bool isError)
        {
            if (XamlRoot == null)
                return;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = LanguageService.Translate("确定"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            LanguageService.ApplyDialog(dialog, LanguageService.CurrentLanguageCode);
            await dialog.ShowAsync();
        }
        private async void BackupDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dbPath = BlockBox.Data.DatabaseContext.DbPath;
                if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                    throw new FileNotFoundException(LanguageService.Translate("数据库文件不存在。"), dbPath);

                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = "BlockBox-database.db"
                };
                picker.FileTypeChoices.Add(LanguageService.Translate("SQLite 数据库"), new[] { ".db" });

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(BlockBox.App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                var file = await picker.PickSaveFileAsync();
                if (file == null)
                    return;

                CheckpointDatabase();
                var sourceFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(dbPath);
                await sourceFile.CopyAndReplaceAsync(file);
                await ShowOperationMessageAsync("备份数据库", "数据库已成功备份。", isError: false);
            }
            catch (Exception ex)
            {
                await ShowOperationMessageAsync("备份数据库失败", ex.Message, isError: true);
            }
        }

        private async void RestoreDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary
                };
                picker.FileTypeFilter.Add(".db");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(BlockBox.App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                var file = await picker.PickSingleFileAsync();
                if (file == null)
                    return;

                var confirm = new ContentDialog
                {
                    Title = LanguageService.Translate("恢复数据库"),
                    Content = LanguageService.Translate("恢复数据库会覆盖当前分类、标签和快捷方式数据，当前数据库会先备份为 blockbox.db.bak。是否继续？"),
                    PrimaryButtonText = LanguageService.Translate("恢复"),
                    CloseButtonText = LanguageService.Translate("取消"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot
                };
                LanguageService.ApplyDialog(confirm, LanguageService.CurrentLanguageCode);
                if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                    return;

                string dbPath = BlockBox.Data.DatabaseContext.DbPath;
                if (string.IsNullOrEmpty(dbPath))
                    throw new InvalidOperationException(LanguageService.Translate("数据库尚未初始化。"));

                CheckpointDatabase();
                if (File.Exists(dbPath))
                    File.Copy(dbPath, dbPath + ".bak", overwrite: true);

                File.Copy(file.Path, dbPath, overwrite: true);
                DeleteDatabaseSidecarFiles(dbPath);
                DatabaseRestoredRequested?.Invoke(this, EventArgs.Empty);
                await ShowOperationMessageAsync("恢复数据库", "数据库已成功恢复。", isError: false);
            }
            catch (Exception ex)
            {
                await ShowOperationMessageAsync("恢复数据库失败", ex.Message, isError: true);
            }
        }

        private static void CheckpointDatabase()
        {
            using var connection = BlockBox.Data.DatabaseContext.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }

        private static void DeleteDatabaseSidecarFiles(string dbPath)
        {
            foreach (string suffix in new[] { "-wal", "-shm", "-journal" })
            {
                string sidecarPath = dbPath + suffix;
                if (File.Exists(sidecarPath))
                    File.Delete(sidecarPath);
            }
        }
        private async void ClearIconCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = LanguageService.Translate("清空图标缓存"),
                Content = LanguageService.Translate("清空缓存提示内容"),
                PrimaryButtonText = LanguageService.Translate("清空"),
                CloseButtonText = LanguageService.Translate("取消"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            ClearIconCacheRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ini == null) return;

            var dialog = new ContentDialog
            {
                Title = LanguageService.Translate("重置所有设置"),
                Content = LanguageService.Translate("重置设置提示内容"),
                PrimaryButtonText = LanguageService.Translate("重置"),
                CloseButtonText = LanguageService.Translate("取消"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            ResetSettingsRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
