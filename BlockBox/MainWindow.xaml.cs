using BlockBox.Data;
using BlockBox.Helpers;
using BlockBox.Models;
using BlockBox.Services;
using BlockBox.Views;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.UI;
using WinRT;

namespace BlockBox
{
    public sealed partial class MainWindow : Window
    {
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _acrylicConfiguration;
        private IniHelper _ini;
        private string _iniPath;

        private DispatcherQueueTimer? _dragRegionUpdateTimer;

        private ProgramFilter _programFilter = null!;
        private string _programFilterPath = "";

        // 拖拽悬停期间：临时选中分类，跳过 SelectionChanged 的正常逻辑
        private bool _isDragHoveringCategory = false;
        // 拖拽开始前，导航栏原本选中的分类（拖拽结束后恢复）
        private Category? _dragCategoryOriginalSelection;

        // 左侧菜单是否展开
        private bool _isSidebarExpanded = true;
        private Brush? _sidebarTextBrush;
        // 新建快捷方式卡片时使用的文字颜色与是否自动校正对比度
        private Color _shortcutTextColor = Color.FromArgb(255, 0, 0, 0);
        private bool _shortcutTextAdjust = true;
        private Color _shortcutTextFallback = Color.FromArgb(255, 255, 255, 255);
        //是否显示文件扩展名
        private bool _showFileExtension = true;

        // 展开时的列宽
        private const double SidebarExpandedWidth = 232.0;
        // 收起时的列宽
        private const double SidebarCollapsedWidth = 62.0;
        private const double SidebarLeftInset = 2.4;
        private static double SidebarInnerWidth(double outerWidth) => Math.Max(0, outerWidth - SidebarLeftInset);
        private AppMode _currentMode = AppMode.Normal;

        private CancellationTokenSource? _verifyCts;
        private bool _verifyClosedHandlerRegistered;

        private enum ViewKind { Category, Tab }
        private ViewKind _currentView = ViewKind.Category;
        private MyTabItem? _currentTabItem;
        // 拖拽开始时缓存的槽位边界（固定不变）
        private List<(int ItemIndex, Rect Bounds)> _dragSlotBounds = new();
        private List<ShortcutItem> _shortcutOrderBeforeDrag = new();
        private SortMode? _dragOriginalSortMode;
        private const double InsertionRowTolerance = 4.0;
        private const double DynamicCategoryExpandedOffset = 8.0;
        private const double InsertionThresholdRatio = 0.9;//拖拽判断宽度

        // 面板展开后的宽度（可按需调整）
        private const double EditPanelWidth = 300;
        private const double EditPanelAnimMs = 280;

        private bool _favoritesTabPinned = true;
        private string _searchKeyword = "";
        private bool _recentTabPinned = true;

        private BlockBox.Services.TabHoverPreviewController? _hoverPreview;
        private bool _pendingReloadAfterDrag = false;
        private DateTime _pendingReloadDeadline = DateTime.MinValue;
        private int _dragSettledFrameCount;
        private bool _isShortcutCollectionReordering;
        // 应用内剪贴板
        private static List<ShortcutItem> _internalClipboard = new();
        private static bool _internalClipboardIsCut = false;
        private readonly Dictionary<string, Microsoft.UI.Xaml.Media.Imaging.BitmapImage> _thumbnailImageCache = new();
        private bool _closeAnimationStarted;
        private bool _allowWindowClose;
        /// <summary>
        /// 用户关闭主窗体时全局搜索窗口还开着：先收进托盘，等搜索窗口也关闭后再真正关闭。
        /// </summary>
        private bool _deferredShellClose;
        private TrayIconService? _trayIcon;
        private bool _trayMenuOpen;
        private bool _fontFamilyApplied;
        private string? _appliedFontFamilyName;
        private string? _appliedLanguageCode;
        private RecycleBinWindow? _recycleBinWindow;
        private ViewManagementWindow? _viewManagementWindow;
        private GlobalSearchWindow? _globalSearchWindow;
        // 系统剪贴板（CF_HDROP）读到的文件路径缓存
        private List<string> _systemClipboardPathsCache = new();
        private uint _lastClipboardSeq = 0;

        private readonly List<ShortcutItem> _selectedShortcuts = new();
        private ShortcutItem? _shortcutSelectionAnchor;
        private readonly Dictionary<FrameworkElement, ShortcutMotionState> _shortcutMotionStates = new();
        private readonly Dictionary<FrameworkElement, int> _shortcutLoadAnimatedElements = new();
        private readonly Dictionary<FrameworkElement, Storyboard> _shortcutLoadStoryboards = new();
        private bool _isShortcutMotionRenderingHooked;
        private bool _isShortcutLoadAnimationBatchActive;
        private bool _isShortcutLoadInteractionLocked;
        private bool _shortcutGridHitTestVisibleBeforeLoadAnimation = true;
        private int _shortcutLoadAnimationBatchId;
        private DateTimeOffset _shortcutLoadAnimationBatchUntil = DateTimeOffset.MinValue;
        private DateTimeOffset _lastShortcutMotionSync = DateTimeOffset.MinValue;
        private bool _isShortcutScrollViewerScrolling;
        private bool _isCustomShortcutScrollbarDragging;
        private int _dragSettledLayoutFrameCounter;
        private static readonly TimeSpan ShortcutMotionSyncInterval = TimeSpan.FromMilliseconds(16);
        private const float ShortcutMotionMaxDeltaSeconds = 0.05f;
        private const float ShortcutMotionMaxSubstepSeconds = 1f / 120f;
        private bool _isShortcutDragging;
        private ShortcutItem? _draggingShortcut;
        private bool _isExternalFileDragStarting;
        private bool _isExternalFileDragRunning;
        private bool _externalFileDragPointerReleased;
        private List<Windows.Storage.StorageFile>? _pendingExternalDragFiles;
        private Border? _dragGhostCard;
        private Image? _dragGhostImage;
        private Point _shortcutDragStartPoint;
        private Microsoft.UI.Xaml.Input.Pointer? _shortcutDragPointer;
        private uint _shortcutDragPointerId;   // ⚠️ 新增
        private bool _shortcutDragMoved;
        private bool _pendingShortcutSelectionToggle;
        private bool _isEndingShortcutDrag;
        private const double ShortcutDragThreshold = 6;
        private MyTabItem? _draggingTabItem;
        private TabViewItem? _draggingTabContainer;
        private Pointer? _tabDragPointer;
        private uint _tabDragPointerId;
        private Point _tabDragStartPoint;
        private double _tabDragGrabOffsetX;
        private double _tabDragSourceTop;
        private double _tabDragGhostWidth;
        private double _tabDragOriginalOpacity = 1;
        private List<MyTabItem> _tabDragOriginalOrder = new();
        private readonly List<TabDragVisualState> _tabDragVisualStates = new();
        private readonly List<double> _tabDragSlotLefts = new();
        private Visual? _tabDragGhostVisual;
        private int _tabDragTargetIndex;
        private bool _isTabDragging;
        private bool _isTabDragReordering;
        private bool _isTabDropAnimating;
        private bool _tabDropCommit;
        private DispatcherQueueTimer? _tabDropAnimationTimer;
        private const double TabDragThreshold = 6;
        private Border? _tabDragGhost;
        private const string TabDragAnimationProperty = "Offset";

        private sealed class TabDragVisualState
        {
            public MyTabItem Item { get; init; } = null!;
            public TabViewItem Container { get; init; } = null!;
            public Visual Visual { get; init; } = null!;
            public Rect Bounds { get; init; }
        }
        // Composition Visual 在当前 WinUI 运行时使用 Offset 作为可动画的位移属性。
        // Translation 在部分运行时/Visual 类型上不可动画，不能用于 StartAnimation/StopAnimation。
        private const string ShortcutLayoutAnimationProperty = "Offset";
        private Category? _dragCategoryTarget;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        private DispatcherQueueTimer? _sortPersistTimer;
        private DispatcherQueueTimer? _sidebarAnimationTimer;
        private DispatcherQueueTimer? _dragSettledTimeoutTimer;
        private bool _isBoxSelecting;
        private bool _boxSelectionActive;
        private Point _boxSelectionStart;
        private readonly List<ShortcutItem> _boxSelectionInitial = new();
        private bool _isSettingsVisible;
        private bool _emptyHintWasVisibleBeforeSettings;
        private bool _editHostWasVisibleBeforeSettings;
        private double _editHostWidthBeforeSettings;
        private bool _sidebarBgIsVideo;
        private bool _contentBgIsVideo;

        private sealed class ShortcutMotionState
        {
            public FrameworkElement Element { get; init; } = null!;
            public FrameworkElement MotionHost { get; init; } = null!;
            public Visual MotionVisual { get; init; } = null!;
            public Vector2 LogicalPosition { get; set; }
            public Vector2 VisualPosition { get; set; }
            public Vector2 Velocity { get; set; }
            public bool Initialized { get; set; }
        }

        private void RestoreLastTabIfConfigured()
        {
            if (!_ini.ReadBool("System", "RestoreLastTab", SettingsDefaults.RestoreLastTab))
                return;

            string lastTabId = _ini.ReadValue("System", "LastTabId", "");
            if (string.IsNullOrWhiteSpace(lastTabId))
                return;

            var lastTab = TabItems.FirstOrDefault(tab =>
                tab.BuiltinKind == BuiltinTabKind.None &&
                string.Equals(tab.Id, lastTabId, StringComparison.OrdinalIgnoreCase));
            if (lastTab != null)
                MyTabView.SelectedItem = lastTab;
        }

        // 底部按钮布局常量
        private const double BottomBtnSize = 36;   // 按钮尺寸
        private enum EditPanelKind { None, Category, Tab }
        private EditPanelKind _editPanelKind = EditPanelKind.None;
        private const double BottomBtnPad = 9;   // 展开时左右边距
        private const double BottomRowY = 48;   // 展开时两个按钮的纵向位置（与收起时"展开按钮"对齐）
        private List<ShortcutItem> _missingFiles = new();
        private Task? _verifyTask;
        private bool _missingFilesDialogOpen;
        private bool _verifySucceeded;
        // 左侧菜单数据源
        public ObservableCollection<Category> Categories { get; set; } = new();
        private DispatcherQueueTimer? _titleClockTimer;
        private bool _initialNavigationSelected;
        private bool _hasTabViewCompletedInitialLoad;
        private bool _suppressTabFadeIn;
        private MyTabItem? _tabBeingRestored;
        private readonly HashSet<MyTabItem> _tabsPendingFadeIn = new();
        private bool _isReloadingCategories;
        private Category? _currentCategory;

        /// <summary>主界面当前选中的分类（看标签页或分区标题时为 null）。</summary>
        public Category? CurrentCategory => _currentCategory;

        /// <summary>主界面当前的工作模式，搜索面板用它实时取分类/写归属模式。</summary>
        public AppMode CurrentMode => _currentMode;

        /// <summary>选中分类变化时触发，搜索窗体据此刷新“添加到当前分类”按钮的可用状态。</summary>
        public event EventHandler? CurrentCategoryChanged;

        /// <summary>主窗体是否已经真正关闭（收进托盘不算）。搜索面板据此禁用“添加到分类”。</summary>
        public bool IsShellClosed { get; private set; }

        /// <summary>主窗体关闭时触发；收进托盘（仅隐藏）不会触发。</summary>
        public event EventHandler? ShellClosed;
        private string? _applicationIconPath;
        private readonly Dictionary<string, string> _selectedCategoryByPartition = new(StringComparer.OrdinalIgnoreCase);
        // 标签页数据源
        public ObservableCollection<MyTabItem> TabItems { get; set; } = new();
        // 内容区的快捷方式数据源
        public ObservableCollection<ShortcutItem> CurrentShortcuts { get; set; } = new();
        private (int Mode, string Image, string Video)? _sidebarBgApplied;
        private (int Mode, string Image, string Video)? _contentBgApplied;

        private void InitializeDesktopAcrylic()
        {
            if (!DesktopAcrylicController.IsSupported())
                return;

            _acrylicConfiguration = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = SystemBackdropTheme.Default
            };
            _acrylicController = new DesktopAcrylicController
            {
                Kind = DesktopAcrylicKind.Thin,
                TintOpacity = 0.18f,
                LuminosityOpacity = 0.35f
            };

            if (!_acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>()))
            {
                _acrylicController.Dispose();
                _acrylicController = null;
                _acrylicConfiguration = null;
                return;
            }

            _acrylicController.SetSystemBackdropConfiguration(_acrylicConfiguration);
            // 激活事件在构造函数里统一挂一次；这里再挂会导致每次激活都跑两遍
            // （亚克力输入态和背景视频的暂停/恢复被重复执行）。
        }

        public MainWindow()
        {
            BlockBox.Helpers.StartupLog.Step("MainWindow: InitializeComponent 开始");
            this.InitializeComponent();
            BlockBox.Helpers.StartupLog.Step("MainWindow: InitializeComponent 完成");
            InitializeDesktopAcrylic();
            TextBoxCornerRadiusHelper.Apply(Content);
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;
            ApplyWindowIcon();
            WindowFadeController.Prepare(this);

            AppWindow.Closing += MainWindow_AppWindowClosing;
            Closed += (_, _) =>
            {
                Activated -= MainWindow_Activated;
                _acrylicController?.Dispose();
                _acrylicController = null;
                _acrylicConfiguration = null;
                _allowWindowClose = true;
                _applySettingsDebounce?.Stop();
                LanguageService.LanguageChanged -= LanguageService_LanguageChanged;
            };

            // ============================================================
            // 初始化 INI / 程序过滤配置
            // 必须放在 HoverPreviewController 创建之前
            // 因为 HoverPreviewController 的回调会直接读取 _ini
            // ============================================================
            string baseDir =
                BlockBox.Helpers.AppDataPaths.Root;

            _iniPath = Path.Combine(
                baseDir,
                "setting",
                "setting.ini");

            _programFilterPath = Path.Combine(
                baseDir,
                "setting",
                "program-filter.ini");

            _programFilter =
                ProgramFilter.LoadOrCreateDefault(
                    _programFilterPath);

            _ini = new IniHelper(_iniPath);
            _trayIcon = new TrayIconService(this, _applicationIconPath);
            _trayIcon.RestoreRequested += (_, _) => RestoreMainWindow();
            _trayIcon.ShowExistingInstanceRequested += (_, _) => RestoreMainWindow();
            _trayIcon.GlobalSearchRequested += TrayIcon_GlobalSearchRequested;
            _trayIcon.SettingsRequested += TrayIcon_SettingsRequested;
            _trayIcon.ContextMenuRequested += TrayIcon_ContextMenuRequested;
            _trayIcon.MinimizedRequested += MainWindow_MinimizedRequested;
            _trayIcon.ExitRequested += (_, _) => ExitApplicationCompletely();
            _trayIcon.Initialize();
            Closed += (_, _) =>
            {
                // 搜索面板是独立窗口，主窗体真正关闭后它仍然存活；通知它禁用依赖主窗体的功能。
                IsShellClosed = true;
                ShellClosed?.Invoke(this, EventArgs.Empty);
            };
            BlockBox.Helpers.StartupLog.Step("MainWindow: 托盘图标初始化完成");
            ConfigureMenuKeyboardAccelerators();


            // ============================================================
            // 创建 HoverPreviewController
            // 此时 _ini 已经初始化完成
            // ============================================================
            _hoverPreview =
                new BlockBox.Services.TabHoverPreviewController(
                    MyTabView,
                    DispatcherQueue,
                    OpenTabEditPanelAsync,
                    DeleteTabAsync,
                    () =>
                    {
                        // 从 INI 读取，调用时实时生效
                        // 不需要重启程序
                        string hex = _ini.ReadValue(
                            "Global",
                            "PrimaryBackground",
                            "#FF244A80");

                        return ParseColor(hex);
                    });


            // ============================================================
            // 监听全局点击，用于“点击面板外取消”
            // handledEventsToo = true：
            // 即使控件自己已经处理了 PointerPressed，
            // 这里仍然可以收到事件
            // ============================================================
            if (this.Content is UIElement rootElement)
            {
                rootElement.AddHandler(
                    UIElement.PointerPressedEvent,
                    new PointerEventHandler(
                        RootGrid_PointerPressed),
                    handledEventsToo: true);
            }

            this.Activated += MainWindow_Activated;
            // ============================================================
            // 快捷方式数据绑定
            // ============================================================
            ShortcutGrid.ItemsSource = CurrentShortcuts;

            CurrentShortcuts.CollectionChanged +=
                CurrentShortcuts_CollectionChanged;
            BlockBox.Helpers.StartupLog.Step("MainWindow: 快捷方式列表绑定完成");


            // ============================================================
            // 设置页面
            // ============================================================
            SettingsViewControl.Configure(_iniPath);

            SettingsViewControl.BackRequested +=
                SettingsView_BackRequested;

            SettingsViewControl.SettingsChanged +=
                SettingsView_SettingsChanged;
            SettingsViewControl.CategoryNavigationSettingsChanged +=
                SettingsView_CategoryNavigationSettingsChanged;

            SettingsViewControl.ImportProgramsRequested +=
                SettingsView_ImportProgramsRequested;

            SettingsViewControl.ResetSettingsRequested +=
                SettingsView_ResetSettingsRequested;
            SettingsViewControl.ClearIconCacheRequested +=
                SettingsView_ClearIconCacheRequested;
            SettingsViewControl.DatabaseRestoredRequested +=
                SettingsView_DatabaseRestoredRequested;
            SettingsViewControl.LanguageChanged +=
                SettingsView_LanguageChanged;
            SettingsViewControl.ScrollBarRefreshRequested += SettingsViewControl_ScrollBarRefreshRequested;

            SettingsViewControl.Loaded +=
                SettingsViewControl_Loaded;

            // ============================================================
            // 左侧分类菜单
            // ============================================================
            NavMenuList.ItemsSource = Categories;

            NavMenuList.ContainerContentChanging +=
                NavMenuList_ContainerContentChanging;


            // ============================================================
            // TabView 尺寸变化
            // 窗口缩放、标签数量变化等情况都会触发
            // ============================================================
            MyTabView.SizeChanged +=
                (s, e) =>
                {
                    ScheduleTabWidthUpdate(forceImmediate: true);
                    ScheduleUpdateTabPaths();
                };

            MyTabView.PointerEntered += (_, _) => _isPointerOverTabView = true;
            MyTabView.PointerExited += (_, _) =>
            {
                _isPointerOverTabView = false;
                if (_tabWidthUpdateDeferredUntilPointerExit)
                {
                    _tabWidthUpdateDeferredUntilPointerExit = false;
                    ScheduleTabWidthUpdate(forceImmediate: true);
                }
            };


            // ============================================================
            // Tab 数量变化
            // ============================================================
            TabItems.CollectionChanged +=
                (s, e) =>
                {
                    if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                    {
                        _tabsPendingFadeIn.Clear();
                    }
                    else if ((e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove
                              || e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
                             && e.OldItems != null)
                    {
                        foreach (var removedItem in e.OldItems.OfType<MyTabItem>())
                            _tabsPendingFadeIn.Remove(removedItem);
                    }

                    bool isRemoval = e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove;
                    if (isRemoval && _isPointerOverTabView)
                        _tabWidthUpdateDeferredUntilPointerExit = true;

                    if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move
                        && !_isTabDragReordering)
                    {
                        BlockBox.Data.TabRepository.UpdateVisibleSortOrders(
                            _currentMode,
                            TabItems
                                .Where(tab => tab.BuiltinKind == BuiltinTabKind.None)
                                .Select(tab => tab.Id)
                                .ToList());
                    }

                    // 延迟一点，等待 TabView 完成重新布局
                    DispatcherQueue.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () =>
                        {
                            ScheduleTabWidthUpdate(
                                deferWhilePointerOver: isRemoval,
                                forceImmediate: !isRemoval);
                            ScheduleUpdateTabPaths();
                            RefreshSelectedTabForeground();
                        });
                };

            FileMenuFlyout.OverlayInputPassThroughElement = TitleBarMenuButtons;
            EditMenuFlyout.OverlayInputPassThroughElement = TitleBarMenuButtons;
            ViewMenuFlyout.OverlayInputPassThroughElement = TitleBarMenuButtons;
            HelpMenuFlyout.OverlayInputPassThroughElement = TitleBarMenuButtons;
            // ============================================================
            // TabView 首次加载完成
            // ============================================================
            MyTabView.Loaded +=
                (s, e) =>
                {
                    DispatcherQueue.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () =>
                        {
                            ScheduleUpdateTabPaths();

                            // 首次加载不播放动画
                            ScheduleSelectedTabVisualRefresh();

                            RefreshSelectedTabForeground();
                        });
                };


            // ============================================================
            // 左侧菜单尺寸变化
            // ============================================================
            NavMenuList.SizeChanged +=
                (s, e) =>
                {
                    DispatcherQueue.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () =>
                        {
                            UpdateSeparatorWidths();
                        });
                };


            // ============================================================
            // 窗口关闭时统一清理
            // ============================================================
            this.Closed +=
                (s, e) =>
                {
                    StopMediaElement(SidebarBgVideo);
                    StopMediaElement(ContentBgVideo);
                    _trayIcon?.Dispose();
                    // --------------------------------------------------------
                    // 取消后台任务
                    // --------------------------------------------------------
                    CancelBackgroundVerify();

                    // --------------------------------------------------------
                    // 停止所有 DispatcherQueueTimer
                    // --------------------------------------------------------
                    _sidebarAnimationTimer?.Stop();
                    _sidebarAnimationTimer = null;

                    _sortPersistTimer?.Stop();

                    _titleClockTimer?.Stop();

                    _applySettingsDebounce?.Stop();


                    // --------------------------------------------------------
                    // 停止快捷方式动画 Rendering
                    // --------------------------------------------------------
                    if (_isShortcutMotionRenderingHooked)
                    {
                        CompositionTarget.Rendering -=
                            OnShortcutMotionRendering;

                        _isShortcutMotionRenderingHooked = false;
                    }


                    // --------------------------------------------------------
                    // 清理快捷方式动画状态
                    // --------------------------------------------------------
                    foreach (var state in _shortcutMotionStates.Values)
                    {
                        StopShortcutLayoutAnimation(
                            state.MotionVisual);

                        state.MotionVisual.Offset =
                            Vector3.Zero;
                    }

                    _shortcutMotionStates.Clear();
                    _toastCts?.Cancel();
                    _toastCts = null;
                };


            // ============================================================
            // 标题栏
            // ============================================================
            var titleBar = this.AppWindow.TitleBar;

            // 让 XAML 内容延伸到标题栏下面
            ExtendsContentIntoTitleBar = true;


            // 指定拖拽区域
            //SetTitleBar(TitleBarDragRegion);


            // ============================================================
            // 标题栏时间
            // ============================================================
            UpdateTitleBarClock();

            _titleClockTimer =
                DispatcherQueue.CreateTimer();

            _titleClockTimer.Interval =
                GetTimeUntilNextMinute();

            _titleClockTimer.Tick +=
                (_, _) =>
                {
                    UpdateTitleBarClock();
                    _titleClockTimer.Interval = GetTimeUntilNextMinute();
                };

            _titleClockTimer.Start();


            // ============================================================
            // 标题栏按钮颜色
            // 前景色在 ApplySettings 里跟随字体色刷新，这里只固定背景。
            // ============================================================
            titleBar.ButtonBackgroundColor =
                Windows.UI.Color.FromArgb(
                    0, 0, 0, 0);

            titleBar.ButtonInactiveBackgroundColor =
                Windows.UI.Color.FromArgb(
                    0, 0, 0, 0);


            // ============================================================
            // 应用配置到 UI
            // ============================================================
            ApplySettings();

            StartBackgroundVerify();

            UpdateToolbarButtonStates();

            RefreshShortcutSelectionVisuals();


            // ============================================================
            // Content.Loaded
            // 此时 XamlRoot 已经就绪
            // ============================================================
            if (this.Content is FrameworkElement rootContent)
            {
                rootContent.Loaded +=
                    RootContent_Loaded;
            }
            if (this.Content is FrameworkElement rootForDrag)
            {
                rootForDrag.SizeChanged += (_, _) => UpdateTitleBarDragRegions();
            }
            //ContentAreaBody.SizeChanged += (_, _) =>
            //{
            //    if (_contentBgIsVideo)
            //        ApplyContentVideoCornerClip();
            //};
        }
        private void ApplyWindowIcon()
        {
            try
            {
                _applicationIconPath = WindowIconHelper.ResolveAssetPath("AppIcon.ico");
                WindowIconHelper.Apply(this, _applicationIconPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[图标] 设置失败: {ex.Message}");
            }
        }

        private void UpdateTitleBarLayout(double sidebarWidth)
        {
            var margin = new Thickness(sidebarWidth + 14, 0, 0, 0);
            MenuBarHost.Margin = margin;
            TitleBarDragRegion.Margin = margin;
            //TitleBarLogo.Visibility = sidebarWidth > 100 ? Visibility.Visible : Visibility.Collapsed;
            // ⭐ 收起状态（接近 SidebarCollapsedWidth = 62）显示 "Block..."
            //    展开状态显示 "BlockBox"
            bool collapsed = sidebarWidth < 100;
            TitleBarLogoText.Text = collapsed ? "Block..." : "BlockBox";
            // 侧边栏宽度变化 → 拖拽区跟着变
            ScheduleDragRegionUpdate();
        }
        private void ScheduleDragRegionUpdate()
        {
            _dragRegionUpdateTimer ??= DispatcherQueue.CreateTimer();
            _dragRegionUpdateTimer.Interval = TimeSpan.FromMilliseconds(80);
            _dragRegionUpdateTimer.IsRepeating = false;
            _dragRegionUpdateTimer.Tick -= DragRegionUpdateTimer_Tick;
            _dragRegionUpdateTimer.Tick += DragRegionUpdateTimer_Tick;
            _dragRegionUpdateTimer.Stop();
            _dragRegionUpdateTimer.Start();
        }

        private void DragRegionUpdateTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            UpdateTitleBarDragRegions();
        }
        private void UpdateTitleBarDragRegions()
        {
            try
            {
                if (AppWindow == null) return;
                if (Content?.XamlRoot == null) return;
                if (MenuBarHost == null) return;
                if (MenuBarHost.ActualWidth <= 0) return;

                double scale = Content.XamlRoot.RasterizationScale;
                double windowWidth = Content.XamlRoot.Size.Width;
                const double titleBarHeight = 32;

                // MenuBarHost 在窗口坐标系中的位置
                var topLeft = MenuBarHost
                    .TransformToVisual(Content as UIElement)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
                double menuLeft = topLeft.X;
                double menuRight = menuLeft + MenuBarHost.ActualWidth;

                var rects = new List<RectInt32>();

                // 左侧拖拽区：0 → 菜单左边界
                double leftWidth = Math.Max(0, menuLeft);
                if (leftWidth > 1)
                {
                    rects.Add(new RectInt32(
                        0, 0,
                        (int)(leftWidth * scale),
                        (int)(titleBarHeight * scale)));
                }

                // 右侧拖拽区：菜单右边界 → 窗口右侧
                double rightWidth = Math.Max(0, windowWidth - menuRight);
                if (rightWidth > 1)
                {
                    rects.Add(new RectInt32(
                        (int)(menuRight * scale), 0,
                        (int)(rightWidth * scale),
                        (int)(titleBarHeight * scale)));
                }

                var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
                source.SetRegionRects(NonClientRegionKind.Caption, rects.ToArray());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[拖拽区] 更新失败: {ex.Message}");
            }
        }
        private MenuFlyout? _openTitleBarMenu;

        private MenuFlyout? GetMenuFlyoutByTag(string tag) => tag switch
        {
            "File" => FileMenuFlyout,
            "Edit" => EditMenuFlyout,
            "View" => ViewMenuFlyout,
            "Help" => HelpMenuFlyout,
            _ => null
        };

        private void SetTitleBarFlyoutAcceleratorText(MenuFlyout flyout, bool isVisible)
        {
            (MenuFlyoutItem Item, string Key, string DefaultGesture)[] bindings =
                ReferenceEquals(flyout, FileMenuFlyout)
                    ? new[]
                    {
                        (FileNewCategoryMenuItem, "NewCategory", "Ctrl+2"),
                        (FileNewTabMenuItem, "NewTab", "Ctrl+3")
                    }
                    : ReferenceEquals(flyout, EditMenuFlyout)
                        ? new[]
                        {
                            (EditAddMenuItem, "Add", "Ctrl+1"),
                            (EditSelectAllMenuItem, "SelectAll", "Ctrl+A"),
                            (EditCutMenuItem, "Cut", "Ctrl+X"),
                            (EditCopyMenuItem, "Copy", "Ctrl+C"),
                            (EditPasteMenuItem, "Paste", "Ctrl+V"),
                            (EditRenameMenuItem, "Rename", "Ctrl+R"),
                            (EditDeleteMenuItem, "Delete", "Delete"),
                            (EditFavoriteMenuItem, "Favorite", "Ctrl+Q")
                        }
                        : Array.Empty<(MenuFlyoutItem, string, string)>();

            foreach (var binding in bindings)
            {
                binding.Item.KeyboardAcceleratorTextOverride = isVisible
                    ? HotkeyGesture.Normalize(_ini.ReadValue("Hotkeys", binding.Key, binding.DefaultGesture))
                    : string.Empty;
            }
        }

        // 打开菜单时记录
        private void TitleBarMenuFlyout_Opened(object? sender, object e)
        {
            if (sender is MenuFlyout flyout)
            {
                SetTitleBarFlyoutAcceleratorText(flyout, isVisible: true);

                _openTitleBarMenu = flyout;
                LanguageService.RegisterMenuFlyout(flyout);
            }
        }

        private void RebuildViewMenuItems()
        {
            ViewMenuFlyout.Items.Clear();
            foreach (var mode in ModeRepository.GetAll())
            {
                var item = new MenuFlyoutItem
                {
                    Text = LimitMenuLabel(mode.IsBuiltIn
                        ? LanguageService.Translate(mode.Name)
                        : mode.Name)
                };
                item.Click += (_, _) => SwitchMode(mode.Mode);
                ViewMenuFlyout.Items.Add(item);
            }
            ViewMenuFlyout.Items.Add(new MenuFlyoutSeparator());

            var createModeItem = new MenuFlyoutItem
            {
                Text = LanguageService.Translate("新建视图")
            };
            createModeItem.Click += MenuCreateMode_Click;
            ViewMenuFlyout.Items.Add(createModeItem);

            var deleteModeItem = new MenuFlyoutItem
            {
                Text = LanguageService.Translate("删除视图")
            };
            deleteModeItem.Click += MenuDeleteMode_Click;
            ViewMenuFlyout.Items.Add(deleteModeItem);
        }

        private static string LimitMenuLabel(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            static int CharacterWidth(char character) =>
                character is >= '\u2E80' and <= '\uA4CF'
                    or >= '\uAC00' and <= '\uD7AF'
                    or >= '\uF900' and <= '\uFAFF'
                    or >= '\uFE30' and <= '\uFE6F'
                    or >= '\uFF01' and <= '\uFF60'
                    or >= '\uFFE0' and <= '\uFFE6'
                    ? 2
                    : 1;

            if (text.Sum(CharacterWidth) <= 8)
                return text;

            var displayed = new System.Text.StringBuilder();
            var width = 0;
            foreach (var character in text)
            {
                var characterWidth = CharacterWidth(character);
                if (width + characterWidth > 5)
                    break;

                displayed.Append(character);
                width += characterWidth;
            }

            return displayed.Append("...").ToString();
        }

        // 关闭菜单时清除
        private void TitleBarMenuFlyout_Closed(object? sender, object e)
        {
            if (sender is MenuFlyout flyout)
                SetTitleBarFlyoutAcceleratorText(flyout, isVisible: false);

            if (ReferenceEquals(_openTitleBarMenu, sender))
                _openTitleBarMenu = null;
        }

        // 点击按钮：打开对应菜单
        private void TitleBarMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;
            var flyout = GetMenuFlyoutByTag(tag);
            if (flyout == null) return;

            // 再次点击同一个按钮 → 关闭
            if (ReferenceEquals(_openTitleBarMenu, flyout))
            {
                flyout.Hide();
                return;
            }

            _openTitleBarMenu?.Hide();
            ShowMenuAt(flyout, btn);
        }

        // 鼠标滑过按钮：如果有菜单开着，切换到当前按钮的菜单
        private void TitleBarMenuButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (_openTitleBarMenu is null) return;
            if (sender is not Button btn || btn.Tag is not string tag) return;

            var flyout = GetMenuFlyoutByTag(tag);
            if (flyout == null) return;
            if (ReferenceEquals(flyout, _openTitleBarMenu)) return;

            var oldFlyout = _openTitleBarMenu;
            _openTitleBarMenu = null;   // 先清空，防止 Closed 事件把状态搞乱
            oldFlyout.Hide();

            // 延迟一帧再打开新菜单，等旧菜单彻底关闭
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => ShowMenuAt(flyout, btn));
        }

        // 统一弹出入口：左下对齐、向下
        private void ShowMenuAt(MenuFlyout flyout, Button btn)
        {
            if (ReferenceEquals(flyout, ViewMenuFlyout))
                RebuildViewMenuItems();

            // 在 Flyout 创建/测量菜单项模板之前设置文本覆盖，确保 WinUI 首次展开时显示快捷键。
            // 键盘行为仍由 ConfigureMenuKeyboardAccelerators 在 ContentAreaRoot 上统一处理。
            SetTitleBarFlyoutAcceleratorText(flyout, isVisible: true);
            LanguageService.RegisterMenuFlyout(flyout);
            flyout.ShowAt(btn, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                ShowMode = FlyoutShowMode.Standard,
                Position = new Point(0, btn.ActualHeight)
            });
        }

        // ==================== 文件 ====================
        private async void MenuNewCategory_Click(object sender, RoutedEventArgs e)
        {
            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();
            await CreateNewCategoryAsync();
        }

        private async void MenuNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();
            if (IsEditPanelOpen) return;

            var result = await ShowTabEditPanelAsync(editing: null);
            if (result == null || !result.IsConfirmed) return;

            if (TabItems.Any(t => t.Title == result.Title))
            {
                await ShowMessageAsync("已存在同名标签");
                return;
            }

            var tab = new MyTabItem
            {
                Title = result.Title,
                Note = result.Note,
                IconGlyph = result.Icon,
                ContentKind = result.Kind,
                ExtensionList = result.ExtList,
                IsClosable = true,
                BelongingMode = _currentMode,
                BuiltinKind = BuiltinTabKind.None,
                IsPinned = true
            };

            BlockBox.Data.TabRepository.Add(tab);
            TabItems.Add(tab);
            MyTabView.SelectedItem = tab;
        }

        private async void MenuRecent_Click(object sender, RoutedEventArgs e)
        {
            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();

            var recent = TabItems.FirstOrDefault(t => t.BuiltinKind == BuiltinTabKind.Recent);
            if (recent == null)
            {
                if (!_recentTabPinned)
                {
                    _recentTabPinned = true;
                    _ini.WriteValue("Tabs", "RecentPinned", "true");
                    LoadTabsFromDb();
                    recent = TabItems.FirstOrDefault(t => t.BuiltinKind == BuiltinTabKind.Recent);
                }
            }

            if (recent != null)
                MyTabView.SelectedItem = recent;
            else
                await ShowMessageAsync("最近访问标签不可用");
        }

        private async void MenuRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();
            if (_recycleBinWindow != null)
            {
                _recycleBinWindow.Activate();
                return;
            }

            var window = new RecycleBinWindow(
                _ini.ReadBool("Notification", "ConfirmRecycleBin", SettingsDefaults.ConfirmRecycleBin));
            _recycleBinWindow = window;
            window.DataChanged += RecycleBinWindow_DataChanged;
            window.ThumbnailRequested += RecycleBinWindow_ThumbnailRequested;
            ModalWindowHelper.ShowModal(this, window, () =>
            {
                window.DataChanged -= RecycleBinWindow_DataChanged;
                window.ThumbnailRequested -= RecycleBinWindow_ThumbnailRequested;
                if (ReferenceEquals(_recycleBinWindow, window))
                    _recycleBinWindow = null;
            });
        }

        private void RecycleBinWindow_DataChanged(object? sender, EventArgs e)
        {
            LoadShortcutsFromDb();
        }

        private async void MenuExcludedPrograms_Click(object sender, RoutedEventArgs e)
        {
            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();
            var window = new ExcludedProgramsWindow();
            window.ThumbnailRequested += RecycleBinWindow_ThumbnailRequested;
            ModalWindowHelper.ShowModal(this, window, () =>
                window.ThumbnailRequested -= RecycleBinWindow_ThumbnailRequested);
        }

        private void ShowShortcutProperties(ShortcutItem shortcut)
        {
            var window = new ShortcutPropertiesWindow(shortcut);
            window.ThumbnailRequested += RecycleBinWindow_ThumbnailRequested;
            window.NameChanged += (_, _) => RefreshAllShortcutNames();
            ModalWindowHelper.ShowModal(this, window, () =>
            {
                window.ThumbnailRequested -= RecycleBinWindow_ThumbnailRequested;
                if (window.DeleteRequested)
                    _ = DeleteShortcutsAsync(new List<ShortcutItem> { shortcut });
            });
        }

        private void RecycleBinWindow_ThumbnailRequested(
            object? sender,
            RecycleBinThumbnailRequestedEventArgs e)
        {
            string? path = ResolveThumbnailPath(e.Shortcut);
            if (string.IsNullOrEmpty(path))
                return;

            string cacheKey = BuildThumbnailImageCacheKey(path, 96);
            if (_thumbnailImageCache.TryGetValue(cacheKey, out var cachedImage))
            {
                e.Image.Source = cachedImage;
                e.Image.Visibility = Visibility.Visible;
                e.Icon.Visibility = Visibility.Collapsed;
                return;
            }

            _ = LoadThumbnailAsync(e.Shortcut, e.Icon, e.Image, path, cacheKey);
        }

        private void MenuExitApp_Click(object sender, RoutedEventArgs e) => ExitApplicationCompletely();

        /// <summary>
        /// 退出应用：先关掉所有独立窗口（搜索、回收站、视图管理等），再退出。
        /// 只调用 Application.Exit() 在 WinUI 3 下不会关闭这些次级窗口。
        /// </summary>
        private void ExitApplicationCompletely()
        {
            _allowWindowClose = true;
            _deferredShellClose = false;

            CloseAuxiliaryWindow(_globalSearchWindow);
            _globalSearchWindow = null;
            CloseAuxiliaryWindow(_recycleBinWindow);
            _recycleBinWindow = null;
            CloseAuxiliaryWindow(_viewManagementWindow);
            _viewManagementWindow = null;

            Application.Current.Exit();
        }

        private static void CloseAuxiliaryWindow(Window? window)
        {
            try
            {
                window?.Close();
            }
            catch
            {
                // 窗口可能已经在关闭过程中，忽略。
            }
        }

        private void MenuHideToTray_Click(object sender, RoutedEventArgs e)
        {
            _trayIcon?.Hide();
        }

        // ==================== 编辑 ====================
        private async void MenuAdd_Click(object sender, RoutedEventArgs e)
            => AddFileButton_Click(sender, e);

        private void MenuSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentShortcuts.Count == 0) return;
            _selectedShortcuts.Clear();
            _selectedShortcuts.AddRange(CurrentShortcuts);
            UpdateToolbarButtonStates();
            RefreshShortcutSelectionVisuals();
        }

        private void MenuCut_Click(object sender, RoutedEventArgs e)
            => ToolbarCut_Click(sender, e);

        private void MenuCopy_Click(object sender, RoutedEventArgs e)
            => ToolbarCopy_Click(sender, e);

        private async void MenuPaste_Click(object sender, RoutedEventArgs e)
        {
            await RefreshSystemClipboardCacheAsync();
            await PasteAsync();
        }

        private async void MenuRename_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedShortcuts.Count != 1)
            {
                await ShowMessageAsync("请先选中一项");
                return;
            }
            await RenameShortcutAsync(_selectedShortcuts[0]);
        }

        private async void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = _selectedShortcuts.ToList();
            if (selected.Count == 0)
            {
                await ShowMessageAsync("请先选中至少一项");
                return;
            }
            await DeleteShortcutsAsync(selected);
        }

        private void MenuFavorite_Click(object sender, RoutedEventArgs e)
            => ToolbarFavorite_Click(sender, e);

        // ==================== 视图 ====================
        private async void MenuCreateMode_Click(object sender, RoutedEventArgs e)
        {
            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();

            string? modeName = await ShowTextInputWindowAsync("新建视图", prompt: "输入视图名称");
            if (modeName == null)
                return;

            try
            {
                AppMode newMode = ModeRepository.Create(modeName);
                SwitchMode(newMode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[新建视图] 失败: {ex}");
                await ShowMessageAsync(ex is ArgumentException
                    ? ex.Message
                    : LanguageService.Translate("创建视图失败"));
            }
        }

        private async void MenuDeleteMode_Click(object sender, RoutedEventArgs e)
        {
            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();

            if (_viewManagementWindow != null)
            {
                _viewManagementWindow.Activate();
                return;
            }

            var window = new ViewManagementWindow(() => _currentMode);
            _viewManagementWindow = window;
            window.ModeDeleted += ViewManagementWindow_ModeDeleted;
            ModalWindowHelper.ShowModal(this, window, () =>
            {
                window.ModeDeleted -= ViewManagementWindow_ModeDeleted;
                if (ReferenceEquals(_viewManagementWindow, window))
                    _viewManagementWindow = null;
            });
        }

        private void ViewManagementWindow_ModeDeleted(AppMode mode)
        {
            if (mode == _currentMode)
                ApplySettings(reloadNavigation: true);
        }

        private void MenuSwitchWork_Click(object sender, RoutedEventArgs e)
            => SwitchMode(AppMode.Work);

        private void MenuSwitchGame_Click(object sender, RoutedEventArgs e)
            => SwitchMode(AppMode.Game);

        private void MenuSwitchNormal_Click(object sender, RoutedEventArgs e)
            => SwitchMode(AppMode.Normal);

        private void SwitchMode(AppMode mode)
        {
            if (_currentMode == mode) return;

            _ini.WriteValue("System", "CurrentMode", ((int)mode).ToString());
            ApplySettings(reloadNavigation: true);
        }

        private void ResetNavigationForModeSwitch()
        {
            _tabSelectionStoryboard?.Stop();
            _tabSelectionStoryboard = null;
            SlidingIndicator.Visibility = Visibility.Collapsed;
            _hasActiveTabSelection = false;

            _isSyncingSelection = true;
            try
            {
                MyTabView.SelectedIndex = -1;
                NavMenuList.SelectedIndex = -1;
                TabItems.Clear();
                CurrentShortcuts.Clear();
                _selectedShortcuts.Clear();
                _currentTabItem = null;
                _currentCategory = null;
                CurrentCategoryChanged?.Invoke(this, EventArgs.Empty);
                _currentView = ViewKind.Category;
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        private void ScheduleDefaultTabSelectionAfterModeSwitch()
        {
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    MyTabView.UpdateLayout();

                    var defaultTab = TabItems.FirstOrDefault(tab =>
                        tab.BuiltinKind == BuiltinTabKind.Favorites)
                        ?? TabItems.FirstOrDefault();

                    if (defaultTab != null)
                    {
                        MyTabView.SelectedItem = defaultTab;
                        if (MyTabView.ContainerFromItem(defaultTab) is FrameworkElement container)
                        {
                            container.StartBringIntoView();
                            ScheduleSelectedTabVisualRefresh();
                        }
                        return;
                    }

                    SelectDefaultCategory();
                });
        }

        // ==================== 帮助 ====================
        private async void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            // 直接打开设置里的“关于我们”页，而不是停在默认页。
            if (_isSettingsVisible)
            {
                SettingsViewControl.SelectPage("About");
                return;
            }
            await OpenSettingsAsync("About");
        }






        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_acrylicConfiguration != null)
                _acrylicConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;

            if (args.WindowActivationState == WindowActivationState.Deactivated)
                PauseBackgroundVideos();
            else
                ResumeBackgroundVideos();
        }

        private void PauseBackgroundVideos()
        {
            if (_sidebarBgIsVideo) try { SidebarBgVideo.MediaPlayer.Pause(); } catch { }
            if (_contentBgIsVideo) try { ContentBgVideo.MediaPlayer.Pause(); } catch { }
        }

        private void ResumeBackgroundVideos()
        {
            if (_sidebarBgIsVideo) try { SidebarBgVideo.MediaPlayer.Play(); } catch { }
            if (_contentBgIsVideo) try { ContentBgVideo.MediaPlayer.Play(); } catch { }
        }
        /// <summary>
        /// 根据 INI 应用左侧菜单和内容区的背景（纯色 / 图片 / 视频）。
        /// </summary>
        private void ApplyBackgrounds()
        {
            ApplySidebarBackground();
            ApplyContentBackground();
        }

        private void ApplySidebarBackground()
        {
            int mode = 0;
            int.TryParse(_ini.ReadValue("Sidebar", "BackgroundMode", "0"), out mode);

            string imagePath = _ini.ReadValue("Sidebar", "BackgroundImagePath", "");
            string videoPath = _ini.ReadValue("Sidebar", "BackgroundVideoPath", "");
            // ⭐ 配置没变，跳过重载
            var current = (mode, imagePath, videoPath);
            if (_sidebarBgApplied == current)
                return;
            _sidebarBgApplied = current;
            // 复位
            SidebarBgImageBrush.ImageSource = null;
            SidebarBgVideo.Visibility = Visibility.Collapsed;
            StopMediaElement(SidebarBgVideo);
            _sidebarBgIsVideo = false;

            if (mode == 1 && !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    SidebarBgImageBrush.ImageSource = new BitmapImage(new Uri(imagePath));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[背景] 左侧菜单图片加载失败: {ex.Message}");
                }
            }
            else if (mode == 2 && !string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
            {
                try
                {
                    var file = Windows.Storage.StorageFile.GetFileFromPathAsync(videoPath)
                        .AsTask().GetAwaiter().GetResult();
                    SidebarBgVideo.Source = MediaSource.CreateFromStorageFile(file);
                    SidebarBgVideo.MediaPlayer.IsLoopingEnabled = true;
                    SidebarBgVideo.MediaPlayer.IsMuted = true;
                    SidebarBgVideo.Visibility = Visibility.Visible;
                    SidebarBgVideo.MediaPlayer.Play();
                    _sidebarBgIsVideo = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[背景] 左侧菜单视频加载失败: {ex.Message}");
                }
            }
        }

        private void ApplyContentBackground()
        {
            int mode = 0;
            int.TryParse(_ini.ReadValue("ContentArea", "BackgroundMode", "0"), out mode);

            string imagePath = _ini.ReadValue("ContentArea", "BackgroundImagePath", "");
            string videoPath = _ini.ReadValue("ContentArea", "BackgroundVideoPath", "");
            var current = (mode, imagePath, videoPath);
            if (_contentBgApplied == current)
                return;
            _contentBgApplied = current;
            ContentBgImageBrush.ImageSource = null;
            ContentBgVideo.Visibility = Visibility.Collapsed;
            StopMediaElement(ContentBgVideo);
            _contentBgIsVideo = false;

            if (mode == 1 && !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    ContentBgImageBrush.ImageSource = new BitmapImage(new Uri(imagePath));
                    // 图片模式：卡片区半透明，让背景图透出来
                    ShortcutGridHost.Background = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[背景] 内容区图片加载失败: {ex.Message}");
                    RestoreShortcutGridBackground();
                }
            }
            else if (mode == 2 && !string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
            {
                try
                {
                    var file = Windows.Storage.StorageFile.GetFileFromPathAsync(videoPath)
                        .AsTask().GetAwaiter().GetResult();
                    ContentBgVideo.Source = MediaSource.CreateFromStorageFile(file);
                    ContentBgVideo.MediaPlayer.IsLoopingEnabled = true;
                    ContentBgVideo.MediaPlayer.IsMuted = true;
                    ContentBgVideo.Visibility = Visibility.Visible;
                    ContentBgVideo.MediaPlayer.Play();
                    _contentBgIsVideo = true;
                    //ApplyContentVideoCornerClip();
                    ShortcutGridHost.Background = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[背景] 内容区视频加载失败: {ex.Message}");
                    RestoreShortcutGridBackground();
                }
            }
            else
            {
                RestoreShortcutGridBackground();
            }
        }
        private void ApplyContentVideoCornerClip()
        {
            var visual = ElementCompositionPreview.GetElementVisual(ContentAreaBody);
            var compositor = visual.Compositor;

            var roundedGeo = compositor.CreateRoundedRectangleGeometry();
            roundedGeo.Size = new Vector2((float)ContentAreaBody.ActualWidth, (float)ContentAreaBody.ActualHeight);
            roundedGeo.CornerRadius = new Vector2(8f, 8f); // 四角统一半径

            var geometricClip = compositor.CreateGeometricClip(roundedGeo);
            visual.Clip = geometricClip;
        }
        private void RestoreShortcutGridBackground()
        {
            ShortcutGridHost.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        }

        private static void StopMediaElement(MediaPlayerElement element)
        {
            try
            {
                element.MediaPlayer?.Pause();
                element.Source = null;
            }
            catch { }
        }

        private void SettingsView_ClearIconCacheRequested(object? sender, EventArgs e)
        {
            _ = ClearIconCacheAsync();
        }

        private async Task ClearIconCacheAsync()
        {
            try
            {
                // 1. 清内存缓存
                ShellIconHelper.ClearMemoryCache();
                _thumbnailImageCache.Clear();

                // 2. 清磁盘缓存目录
                string baseDir = BlockBox.Helpers.AppDataPaths.Root;
                string cacheDir = Path.Combine(baseDir, "content", "icon-cache");
                ClearDirectoryContents(cacheDir);

                // 3. 清掉遗留的 LastAction（兼容旧机制）
                _ini.WriteValue("System", "LastAction", "");

                // 4. 立即重载所有可见卡片的图标
                RefreshAllShortcutIcons();

                await ShowSuccessMessageAsync("图标缓存已清空");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(LanguageService.TranslateFormat("清空图标缓存失败：{error}", ("{error}", ex.Message)));
            }
        }
        /// <summary>
        /// 清缓存后强制刷新当前可见卡片的图标。
        /// 关键：把 Image 的 Tag 清空，避免异步加载结果被当作"还没过期的旧结果"丢弃。
        /// </summary>
        private void RefreshAllShortcutIcons()
        {
            int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
            for (int i = 0; i < childCount; i++)
            {
                if (VisualTreeHelper.GetChild(ShortcutGrid, i) is not FrameworkElement element)
                    continue;

                int index = ShortcutGrid.GetElementIndex(element);
                if (index < 0 || index >= CurrentShortcuts.Count)
                    continue;

                // 复位 Image，让 PrepareShortcutVisual 重新走加载分支
                if (FindChildByName<Image>(element, "ItemThumbnail") is Image image)
                {
                    image.Source = null;
                    image.Visibility = Visibility.Collapsed;
                    image.Tag = null;
                }
                if (FindChildByName<FontIcon>(element, "ItemIcon") is FontIcon icon)
                {
                    icon.Visibility = Visibility.Visible;
                }

                PrepareShortcutVisual(element, CurrentShortcuts[index]);
            }
        }
        /// <summary>
        /// 删除目录下所有文件（不删目录本身，避免目录被占用或丢失）。
        /// </summary>
        private static void ClearDirectoryContents(string dir)
        {
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir))
            {
                try { File.Delete(file); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[清缓存] 删文件失败 {file}: {ex.Message}"); }
            }

            foreach (var subDir in Directory.GetDirectories(dir))
            {
                try { Directory.Delete(subDir, recursive: true); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[清缓存] 删目录失败 {subDir}: {ex.Message}"); }
            }
        }
        private void SettingsView_ResetSettingsRequested(object? sender, EventArgs e)
        {
            ResetAllSettings();
        }
        private static readonly (string Section, string Key, string Fallback)[] _preservedKeys =
{
    ("Storage", "SchemaVersion", "5"),
    ("Startup", "InstalledProgramsPrepared", "true"),
    ("System", "FontSizeStep", "2"),
    // 以后有新键往这里加
};
        private void ResetAllSettings()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_iniPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 1. 保存所有要保留的键
                var preserved = new List<(string Section, string Key, string Value)>();
                foreach (var (section, key, fallback) in _preservedKeys)
                {
                    preserved.Add((section, key, _ini.ReadValue(section, key, fallback)));
                }

                // 2. 备份
                if (File.Exists(_iniPath))
                {
                    string backup = _iniPath + ".bak";
                    File.Copy(_iniPath, backup, overwrite: true);
                }

                // 3. 删除
                if (File.Exists(_iniPath))
                    File.Delete(_iniPath);

                // 4. 重建
                _ini = new IniHelper(_iniPath);
                SettingsViewControl.Configure(_iniPath);

                // 5. 写回保留键
                foreach (var (section, key, value) in preserved)
                    _ini.WriteValue(section, key, value);

                // 6. 应用
                ApplySettings(reloadNavigation: true);
                SettingsViewControl.ReloadFromIni();

                _ = ShowSuccessMessageAsync("已恢复默认设置");
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync(LanguageService.TranslateFormat("重置失败：{error}", ("{error}", ex.Message)));
            }
        }

        private bool ShouldConfirmDelete()
=> _ini.ReadBool("Notification", "ConfirmDelete", true);

        private async Task<string?> ShowTextInputWindowAsync(string title, string initialValue = "", string prompt = "")
        {
            var window = new TextInputWindow(title, initialValue, prompt);
            await ModalWindowHelper.ShowModalAsync(this, window);
            return window.Result;
        }

        private async Task<int> ShowConfirmationWindowAsync(string title, object content,
            string primaryText, string cancelText = "取消", string? secondaryText = null, string? tertiaryText = null)
        {
            var window = new ConfirmationWindow(title, content, primaryText, cancelText, secondaryText,
                tertiaryText: tertiaryText);
            await ModalWindowHelper.ShowModalAsync(this, window);
            return window.Result;
        }

        private static bool IsDynamicPartitionItem(Category cat)
    => !cat.IsSeparator && !cat.IsSectionHeader
       && !string.IsNullOrEmpty(cat.PartitionId)
       && !string.Equals(cat.PartitionId, "default", StringComparison.Ordinal);
        private void NavMenuList_ContainerContentChanging(
    ListViewBase sender,
    ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            if (args.Item is not Category cat) return;
            if (args.ItemContainer is not ListViewItem lvi) return;

            FontSizeHelper.ApplyGlobalFontSizeToTree(lvi);

            // ⭐ 每次都重套，避免复用残留
            if (cat.IsSectionHeader)
            {
                // ⭐ 先保证样式正确，模板里才有 SectionHeaderText / SectionHeaderArrow
                lvi.Style = (Style)NavMenuList.Resources["NavSectionHeaderStyle"];

                var headerText = FindChildByName<TextBlock>(lvi, "SectionHeaderLabel");
                if (headerText != null)
                {
                    headerText.Text = _isSidebarExpanded
                        ? cat.LocalizedSectionHeaderText
                        : cat.LocalizedCompactHeaderText;
                    headerText.Width = _isSidebarExpanded ? 188 : double.NaN;
                    if (_sidebarTextBrush != null)
                        headerText.Foreground = _sidebarTextBrush;
                }

                var arrowIcon = FindChildByName<FontIcon>(lvi, "SectionHeaderArrow");
                if (arrowIcon != null)
                {
                    if (_sidebarTextBrush != null)
                        arrowIcon.Foreground = _sidebarTextBrush;
                    arrowIcon.Visibility = Visibility.Visible;          // ⭐ 一直可见
                    arrowIcon.FontSize = _isSidebarExpanded ? 9 : 8;    // 收起时略小
                    SetSectionHeaderArrowShift(arrowIcon, _isSidebarExpanded);
                }

                var headerPanel = FindChildByName<StackPanel>(lvi, "SectionHeaderPanel");
                if (headerPanel != null)
                {
                    if (headerPanel.RenderTransform is not TranslateTransform headerTransform)
                    {
                        headerTransform = new TranslateTransform();
                        headerPanel.RenderTransform = headerTransform;
                    }
                    headerTransform.X = GetPartitionHeaderTargetShift(lvi, headerPanel);
                }
            }
            else if (cat.IsSeparator)
                lvi.Style = (Style)NavMenuList.Resources["NavSeparatorStyle"];
            else
                lvi.Style = (Style)NavMenuList.Resources["NavItemStyle"];

            // 侧边栏展开/收起时的图标平移 + 文字淡入淡出
            if (!cat.IsSeparator && !cat.IsSectionHeader)
            {
                var panel = FindChildByName<StackPanel>(lvi, "CategoryPanel");
                var icon = FindChildByName<FontIcon>(lvi, "CategoryIcon");
                var text = FindChildByName<TextBlock>(lvi, "CategoryText");
                if (panel != null && text != null)
                {
                    if (panel.RenderTransform is not TranslateTransform tt)
                    {
                        tt = new TranslateTransform();
                        panel.RenderTransform = tt;
                    }
                    double iconW = icon?.ActualWidth > 0 ? icon.ActualWidth : 16;
                    double currentLeft = 12;                                 // 与 NavItemStyle 的 Padding 对应
                    double targetLeft = (SidebarInnerWidth(SidebarCollapsedWidth) - iconW) / 2;
                    double collapsedShift = targetLeft - currentLeft;
                    tt.X = GetCategoryTargetShift(cat);
                    text.Opacity = _isSidebarExpanded ? 1 : 0;
                }
            }
        }
        private void UpdateTitleBarClock()
        {
            if (TitleBarClock != null) TitleBarClock.Text = DateTime.Now.ToString("g", CultureInfo.CurrentCulture);
        }

        private static TimeSpan GetTimeUntilNextMinute()
        {
            var now = DateTime.Now;
            var nextMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1);
            return nextMinute - now;
        }

        private void MainWindow_AppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_allowWindowClose)
                return;

            if (_ini.ReadBool("System", "TrayOnExit", SettingsDefaults.TrayOnExit))
            {
                args.Cancel = true;
                _deferredShellClose = false;
                // 搜索面板是独立窗口：主窗体收进托盘时保持原样，不跟着隐藏/关闭。
                _trayIcon?.Hide();
                _trayIcon?.ShowEnteredTrayNotification();
                return;
            }

            // 未开启“退出后进入托盘”，但全局搜索窗口还开着：按约定只要还有任意一个窗体活着，
            // 托盘就必须在。托盘图标挂在主窗体的 HWND 上，主窗体一旦真的销毁图标就会消失，
            // 所以这里先把它收进托盘、暂缓关闭；等搜索窗口也关闭后再完成真正的关闭。
            if (_globalSearchWindow is not null)
            {
                args.Cancel = true;
                _deferredShellClose = true;
                _trayIcon?.Hide();
                return;
            }

            args.Cancel = true;
            _deferredShellClose = false;
            if (_closeAnimationStarted)
                return;

            _closeAnimationStarted = true;
            _applySettingsDebounce?.Stop();
            _ = FadeOutAndCloseAsync();
        }

        private async Task FadeOutAndCloseAsync()
        {
            await WindowFadeController.FadeOutAsync(this);
            _allowWindowClose = true;
            Close();
        }

        private void MainWindow_MinimizedRequested(object? sender, EventArgs e)
        {
            if (_ini.ReadBool("System", "MinimizeToTray", SettingsDefaults.MinimizeToTray))
                _trayIcon?.Hide();
        }

        /// <summary>
        /// 在启动窗体仍可见时预解码全部“全部应用”分类的图标，主窗体打开后可直接显示。
        /// </summary>
        public async Task<int> PreloadAllApplicationIconsAsync(IProgress<string>? progress = null, int maxCount = int.MaxValue)
        {
            var ordered = GetPreloadIconOrder();
            int limit = Math.Min(maxCount, ordered.Count);
            BlockBox.Helpers.StartupLog.Step($"首屏图标预载: 共 {limit} 个（全部 {ordered.Count} 个）");

            int loaded = 0;
            for (int i = 0; i < limit; i++)
            {
                if (await TryPreloadShortcutIconAsync(ordered[i]))
                    loaded++;

                if ((i + 1) % 8 == 0 || i + 1 == limit)
                    progress?.Report($"正在预载首屏图标（{i + 1}/{limit}）...");
            }

            return loaded;
        }

        /// <summary>
        /// 主窗体显示后，后台继续补齐剩余图标；已缓存的会直接跳过。
        /// </summary>
        public async Task PreloadRemainingApplicationIconsAsync()
        {
            try
            {
                foreach (var item in GetPreloadIconOrder())
                {
                    if (_allowWindowClose || _closeAnimationStarted)
                        return;

                    await TryPreloadShortcutIconAsync(item);

                    // 让出 UI 线程，保证主界面交互不被图标解码拖住。
                    await Task.Yield();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[启动图标预载] 后台预载失败: {ex.Message}");
            }
        }

        /// <summary>先当前视图里可见的条目，再补全部应用里的其余条目。</summary>
        private List<ShortcutItem> GetPreloadIconOrder()
        {
            var ordered = new List<ShortcutItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddRange(IEnumerable<ShortcutItem> items)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.Id) && seen.Add(item.Id))
                        ordered.Add(item);
                }
            }

            AddRange(CurrentShortcuts.ToList());
            AddRange(ModeRepository.GetAllModes()
                .SelectMany(mode => BlockBox.Data.CategoryRepository.GetVisible(mode)
                    .Where(category => string.Equals(category.Name, "全部应用", StringComparison.OrdinalIgnoreCase)
                        && !category.IsSeparator)
                    .SelectMany(category => BlockBox.Data.ShortcutRepository.GetByCategory(mode, category.Id))));

            return ordered;
        }

        private async Task<bool> TryPreloadShortcutIconAsync(ShortcutItem item)
        {
            string? path = !string.IsNullOrWhiteSpace(item.IconPath)
                && System.IO.File.Exists(item.IconPath)
                ? item.IconPath
                : null;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string key = BuildThumbnailImageCacheKey(path, 96);
            if (_thumbnailImageCache.ContainsKey(key))
                return false;

            try
            {
                byte[] png = await File.ReadAllBytesAsync(path);
                png = TrimTransparentPng(png);
                if (png is not { Length: > 0 })
                    return false;

                png = TrimTransparentPng(png);
                var bitmap = await CreateBitmapFromPngAsync(png);
                if (bitmap == null)
                    return false;

                _thumbnailImageCache[key] = bitmap;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[启动图标预载] {item.Name}: {ex.Message}");
                return false;
            }
        }

        private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> CreateBitmapFromPngAsync(byte[] png)
        {
            try
            {
                using var stream = new MemoryStream(png);
                using var randomAccessStream = stream.AsRandomAccessStream();
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(randomAccessStream);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[启动图标预载] PNG 解码失败: {ex.Message}");
                return null;
            }
        }

        private async void SettingsView_ImportProgramsRequested(object? sender, EventArgs e)
        {
            await CloseSettingsWithAnimationAsync(restoreEmptyHint: false);

            var confirm = new ContentDialog
            {
                Title = "导入已安装程序",
                Content = "将扫描本机已安装程序，并按类别加入「全部应用 / 游戏 / 办公 / 音乐」四个分类。\n" +
                          "该操作可能耗时数十秒，是否继续？",
                PrimaryButtonText = "开始",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            LanguageService.ApplyDialog(confirm, LanguageService.CurrentLanguageCode);
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            _programFilter = ProgramFilter.LoadOrCreateDefault(_programFilterPath);

            var progressWindow = new ScanProgressWindow();
            progressWindow.Activate();
            var progress = new Progress<string>(msg =>
            {
                progressWindow.SetStatus(msg);
                System.Diagnostics.Debug.WriteLine($"[导入] {msg}");
            });

            try
            {
                // ⭐ 主动扫描：同样为所有模式写入记录
                var result = await ProgramImportService.ScanAndImportAsync(
                    ModeRepository.GetAllModes(),
                    _programFilter,
                    progress,
                    hideNoIconComponents: _ini.ReadBool("Program", "HideNoIconComponentPrograms",
                        SettingsDefaults.HideNoIconComponentPrograms));
                progressWindow.CloseIfOpen();

                // ⭐⭐⭐ 诊断代码：直接查数据库 ⭐⭐⭐
                var allAppsCategory = BlockBox.Data.CategoryRepository.GetVisible(_currentMode)
                    .FirstOrDefault(c => c.Name == "全部应用");
                if (allAppsCategory != null)
                {
                    var check = BlockBox.Data.ShortcutRepository.GetByCategory(_currentMode, allAppsCategory.Id);
                    int total = check.Count;
                    int hasIcon = check.Count(s => !string.IsNullOrEmpty(s.IconPath));
                    int uwpTotal = check.Count(s =>
                        s.TargetPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase));
                    int uwpHasIcon = check.Count(s =>
                        s.TargetPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(s.IconPath));

                    System.Diagnostics.Debug.WriteLine(
                        $"[诊断] 全部应用 {total} 条，{hasIcon} 条有 IconPath；" +
                        $"UWP {uwpTotal} 条，其中 {uwpHasIcon} 条有 IconPath");
                }
                // ⭐⭐⭐ 诊断代码结束 ⭐⭐⭐

                LoadCategoriesFromDb();
                LoadTabsFromDb();
                LoadShortcutsFromDb();

                var msgs = new List<string>
        {
            LanguageService.TranslateFormat(
                "扫描 {count} 个程序（开始菜单 {start} / 桌面 {desktop} / 注册表 {registry} / App Paths {apppaths} / 商店 {store}）",
                ("{count}", result.Scanned),
                ("{start}", result.SourceStartMenu),
                ("{desktop}", result.SourceDesktop),
                ("{registry}", result.SourceRegistry),
                ("{apppaths}", result.SourceAppPath),
                ("{store}", result.SourceStore)),
            LanguageService.TranslateFormat("全部应用 +{count}", ("{count}", result.AddedAllApps)),
            LanguageService.TranslateFormat("游戏 +{count}", ("{count}", result.AddedGame)),
            LanguageService.TranslateFormat("办公 +{count}", ("{count}", result.AddedOffice)),
            LanguageService.TranslateFormat("音乐 +{count}", ("{count}", result.AddedMusic)),
            LanguageService.TranslateFormat("用户分类记录匹配 {count} 个程序", ("{count}", result.FromUserHistory)),
            LanguageService.TranslateFormat("自动分类规则匹配 {count} 个程序", ("{count}", result.FromBuiltInRules)),
            LanguageService.TranslateFormat("重复跳过 {count}", ("{count}", result.DuplicatesSkipped)),
            LanguageService.TranslateFormat("排除项跳过 {count}", ("{count}", result.ExcludedSkipped)),
            LanguageService.TranslateFormat("无法启动跳过 {count}", ("{count}", result.UnlaunchableSkipped)),
            LanguageService.TranslateFormat("组件程序跳过 {count}", ("{count}", result.ComponentSkipped))
        };
                if (result.Errors.Count > 0)
                {
                    msgs.Add(LanguageService.TranslateFormat("错误 {count} 条（见调试输出）", ("{count}", result.Errors.Count)));
                    foreach (var err in result.Errors.Take(20))
                        System.Diagnostics.Debug.WriteLine($"[导入错误] {err}");
                }

                if (ShouldShowSuccessNotification() || result.Errors.Count > 0)
                {
                    var content = new StackPanel { Spacing = 12 };
                    content.Children.Add(new TextBlock { Text = string.Join("\n", msgs), TextWrapping = TextWrapping.Wrap });
                    if (result.CategoryMatches.Count > 0)
                    {
                        content.Children.Add(new TextBlock { Text = LanguageService.Translate("扫描分类来源"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                        var details = new StackPanel { Spacing = 6 };
                        foreach (var match in result.CategoryMatches)
                        {
                            details.Children.Add(new TextBlock
                            {
                                Text = $"{match.Name} · {LanguageService.Translate(match.Category)} · {LanguageService.Translate(match.FromHistory ? "用户分类记录" : "自动分类规则")}\n{match.TargetPath}",
                                TextWrapping = TextWrapping.Wrap
                            });
                        }
                        content.Children.Add(new ScrollViewer { MaxHeight = 260, Content = details });
                    }
                    var dialog = new ContentDialog
                    {
                        Title = LanguageService.Translate("导入完成："), Content = content,
                        CloseButtonText = LanguageService.Translate("确定"), XamlRoot = Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                progressWindow.CloseIfOpen();
                await ShowMessageAsync(LanguageService.TranslateFormat("导入失败：{error}", ("{error}", ex.Message)));
            }
        }


        private bool _initialSizeApplied = false;

        private void RootContent_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialSizeApplied) return;
            _initialSizeApplied = true;

            if (sender is FrameworkElement fe)
                fe.Loaded -= RootContent_Loaded;

            // 此时 XamlRoot 一定不为 null
            var xamlRoot = (sender as FrameworkElement)?.XamlRoot
                           ?? this.Content.XamlRoot;
            var scale = xamlRoot?.RasterizationScale ?? 1.0;

            int physicalWidth = (int)(1070 * scale);
            int physicalHeight = (int)(580 * scale);

            this.AppWindow.ResizeClient(
                new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));

            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var workArea = displayArea.WorkArea;
                var windowSize = AppWindow.Size;
                AppWindow.Move(new PointInt32(
                    workArea.X + (workArea.Width - windowSize.Width) / 2,
                    workArea.Y + (workArea.Height - windowSize.Height) / 2));
            }

            if (this.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = (int)(980 * scale);
                presenter.PreferredMinimumHeight = (int)(570 * scale);
            }
            // 等尺寸、布局和导航栏都就绪后，淡入整个 HWND。
            DispatcherQueue.TryEnqueue(
        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
        UpdateTitleBarDragRegions);

            // 窗口尺寸刚变成 1070×580，标签宽度要立刻按新宽度落好；
            // 否则 SizeChanged 触发的低优先级更新会带着隐式动画再“长”一次。
            (this.Content as FrameworkElement)?.UpdateLayout();
            ApplyTabWidthsWithoutAnimation();
            MyTabView.UpdateLayout();

            WindowFadeController.FadeIn(this);

            // 最终补刷一次：分类项、标签、快捷方式卡片等模板要等布局/首帧之后才真正创建，
            // 前面两次 ApplySettings 可能覆盖不到。延迟一点再套一次颜色，避免“打开设置后才正确”。
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                async () =>
                {
                    await Task.Delay(150);
                    if (_closeAnimationStarted || _allowWindowClose)
                        return;

                    ApplySettings(reloadNavigation: false, reloadBackgrounds: false);
                });
        }
        public void ShowGlobalSearchWindow()
        {
            if (_globalSearchWindow is null)
            {
                _globalSearchWindow = new GlobalSearchWindow(_currentMode);
                _globalSearchWindow.Closed += GlobalSearchWindow_Closed;
            }
            // 搜索窗体出现时入口按钮必须收起（启动时也一样），关闭后再渐入。
            HideSearchLauncher();
            _globalSearchWindow.ShowBesideMainWindow();
        }
        private void BeginShortcutBoxSelection(Point position, bool additive)
        {
            _isBoxSelecting = true;
            _boxSelectionActive = false;
            _boxSelectionStart = position;
            _boxSelectionInitial.Clear();

            if (additive)
                _boxSelectionInitial.AddRange(_selectedShortcuts);
            else
                _selectedShortcuts.Clear();

            UpdateToolbarButtonStates();
            RefreshShortcutSelectionVisuals();
        }

        private void UpdateShortcutBoxSelection(Point currentPosition)
        {
            if (!_isBoxSelecting)
                return;

            double left = Math.Min(_boxSelectionStart.X, currentPosition.X);
            double top = Math.Min(_boxSelectionStart.Y, currentPosition.Y);
            double right = Math.Max(_boxSelectionStart.X, currentPosition.X);
            double bottom = Math.Max(_boxSelectionStart.Y, currentPosition.Y);
            var selectionRect = new Rect(left, top, right - left, bottom - top);

            if (!_boxSelectionActive
                && (Math.Abs(currentPosition.X - _boxSelectionStart.X) > 4
                    || Math.Abs(currentPosition.Y - _boxSelectionStart.Y) > 4))
            {
                _boxSelectionActive = true;
            }

            if (!_boxSelectionActive)
                return;

            ShortcutSelectionRectangle.Visibility = Visibility.Visible;
            Canvas.SetLeft(ShortcutSelectionRectangle, left);
            Canvas.SetTop(ShortcutSelectionRectangle, top);
            ShortcutSelectionRectangle.Width = selectionRect.Width;
            ShortcutSelectionRectangle.Height = selectionRect.Height;

            _selectedShortcuts.Clear();
            _selectedShortcuts.AddRange(_boxSelectionInitial);

            for (int i = 0; i < CurrentShortcuts.Count; i++)
            {
                var element = GetGeneratedShortcutElement(i);
                if (element == null || element.Visibility != Visibility.Visible)
                    continue;

                var origin = element.TransformToVisual(ShortcutSelectionCanvas)
                    .TransformPoint(new Point(0, 0));
                var bounds = new Rect(origin.X, origin.Y,
                    element.ActualWidth, element.ActualHeight);
                bool intersects = bounds.Left < selectionRect.Right
                    && bounds.Right > selectionRect.Left
                    && bounds.Top < selectionRect.Bottom
                    && bounds.Bottom > selectionRect.Top;
                if (intersects
                    && !_selectedShortcuts.Contains(CurrentShortcuts[i]))
                {
                    _selectedShortcuts.Add(CurrentShortcuts[i]);
                }
            }

            UpdateToolbarButtonStates();
            RefreshShortcutSelectionVisuals();
        }

        private void EndShortcutBoxSelection()
        {
            if (!_isBoxSelecting)
                return;

            _isBoxSelecting = false;
            _boxSelectionActive = false;
            _boxSelectionInitial.Clear();
            ShortcutSelectionRectangle.Visibility = Visibility.Collapsed;
            ShortcutInteractionHost.ReleasePointerCaptures();
            UpdateToolbarButtonStates();
            RefreshShortcutSelectionVisuals();
        }

        private FrameworkElement? GetGeneratedShortcutElement(int index)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
            for (int i = 0; i < childCount; i++)
            {
                if (VisualTreeHelper.GetChild(ShortcutGrid, i) is FrameworkElement element
                    && ShortcutGrid.GetElementIndex(element) == index)
                {
                    return element;
                }
            }

            return null;
        }

        private void EnsureShortcutMotionRenderingHook()
        {
            if (_isShortcutMotionRenderingHooked)
                return;

            CompositionTarget.Rendering += OnShortcutMotionRendering;
            _isShortcutMotionRenderingHooked = true;
        }

        private void DisableShortcutMotionRenderingHookIfUnused()
        {
            if (!_isShortcutMotionRenderingHooked || _shortcutMotionStates.Count > 0)
                return;

            CompositionTarget.Rendering -= OnShortcutMotionRendering;
            _isShortcutMotionRenderingHooked = false;
            _lastShortcutMotionSync = DateTimeOffset.MinValue;
        }

        private static void StopShortcutLayoutAnimation(Visual visual)
        {
            try
            {
                visual.StopAnimation(ShortcutLayoutAnimationProperty);
            }
            catch (ArgumentException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void OnShortcutMotionRendering(object? sender, object e)
        {
            var now = DateTimeOffset.UtcNow;
            if (_isShortcutScrollViewerScrolling)
            {
                _lastShortcutMotionSync = now;
                return;
            }

            if (_lastShortcutMotionSync != DateTimeOffset.MinValue
                && now - _lastShortcutMotionSync < ShortcutMotionSyncInterval)
                return;
            float dt = _lastShortcutMotionSync == DateTimeOffset.MinValue
                ? 0f
                : (float)(now - _lastShortcutMotionSync).TotalSeconds;
            _lastShortcutMotionSync = now;
            dt = Math.Clamp(dt, 0f, ShortcutMotionMaxDeltaSeconds);

            if (_isShortcutLoadAnimationBatchActive)
            {
                if (DateTimeOffset.UtcNow > _shortcutLoadAnimationBatchUntil)
                {
                    _isShortcutLoadAnimationBatchActive = false;
                    RestoreAllShortcutVisualsImmediate();   // ⭐ 双保险
                }
                else
                {
                    TryAnimateVisibleShortcuts();
                }
            }

            if (_shortcutMotionStates.Count == 0)
            {
                DisableShortcutMotionRenderingHookIfUnused();
                TryReloadAfterDragSettled();   // ⚠️ 新增
                return;
            }

            foreach (var pair in _shortcutMotionStates)
            {
                var element = pair.Key;
                var state = pair.Value;

                if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                    continue;

                if (!state.MotionHost.IsLoaded || state.MotionHost.ActualWidth <= 0 || state.MotionHost.ActualHeight <= 0)
                    continue;

                Point targetPoint;
                try
                {
                    targetPoint = element.TransformToVisual(ShortcutGrid)
     .TransformPoint(new Point(0, 0));
                }
                catch
                {
                    continue;
                }
                var logical = new Vector2((float)targetPoint.X, (float)targetPoint.Y);

                if (!state.Initialized)
                {
                    state.LogicalPosition = logical;
                    state.VisualPosition = logical;
                    state.Velocity = Vector2.Zero;
                    state.MotionVisual.Offset = Vector3.Zero;
                    state.Initialized = true;
                    continue;
                }

                state.LogicalPosition = logical;

                const float angularFrequency = 18f;
                const float dampingRatio = 0.86f;

                float remainingTime = dt;
                while (remainingTime > 0f)
                {
                    float step = Math.Min(remainingTime, ShortcutMotionMaxSubstepSeconds);
                    var displacement = state.LogicalPosition - state.VisualPosition;
                    var acceleration = angularFrequency * angularFrequency * displacement
                        - 2f * dampingRatio * angularFrequency * state.Velocity;

                    state.Velocity += acceleration * step;
                    state.VisualPosition += state.Velocity * step;
                    remainingTime -= step;
                }

                var followOffset = state.VisualPosition - state.LogicalPosition;
                state.MotionVisual.Offset = new Vector3(followOffset, 0f);
            }
            // ⚠️ 新增：每帧检查弹簧是否收敛
            TryReloadAfterDragSettled();
        }
        /// <summary>
        /// 弹簧收敛后触发拖拽重载
        /// </summary>
        private void TryReloadAfterDragSettled()
        {
            if (!_pendingReloadAfterDrag) return;
            bool timedOut = DateTime.Now > _pendingReloadDeadline;
            if (_isShortcutDragging && !timedOut) return;

            if (timedOut)
            {
                _isShortcutDragging = false;
                _draggingShortcut = null;
            }

            // 等 ItemsRepeater 完成最后一次 Move 的布局，再评估弹簧是否收敛。
            if (++_dragSettledLayoutFrameCounter >= 3)
            {
                ShortcutGrid.UpdateLayout();
                _dragSettledLayoutFrameCounter = 0;
            }

            bool allSettled = true;
            foreach (var state in _shortcutMotionStates.Values)
            {
                var off = state.MotionVisual.Offset;
                var vel = state.Velocity;

                // 位移和速度都接近 0，说明弹簧停了
                if (Math.Abs(off.X) > 0.5f || Math.Abs(off.Y) > 0.5f
                    || Math.Abs(vel.X) > 20f || Math.Abs(vel.Y) > 20f)
                {
                    allSettled = false;
                    break;
                }
            }

            _dragSettledFrameCount = allSettled ? _dragSettledFrameCount + 1 : 0;

            // 多帧稳定后再清理，避免在最后一次布局移动尚未反映到可视元素时提前结束。
            bool stablySettled = _dragSettledFrameCount >= 3;
            // 超时兜底：即使没收敛也强制结束视觉状态
            if (stablySettled || timedOut)
            {
                _pendingReloadAfterDrag = false;
                _dragSettledFrameCount = 0;
                _dragSettledLayoutFrameCounter = 0;
                _dragSettledTimeoutTimer?.Stop();

                // 收敛后只清动画位移，不重绑 ItemsSource；集合顺序已是最终顺序且已写入数据库。
                foreach (var state in _shortcutMotionStates.Values)
                {
                    StopShortcutLayoutAnimation(state.MotionVisual);
                    state.MotionVisual.Offset = Vector3.Zero;
                    state.Velocity = Vector2.Zero;
                    state.Initialized = false;
                }

                int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
                for (int i = 0; i < childCount; i++)
                {
                    if (VisualTreeHelper.GetChild(ShortcutGrid, i) is not FrameworkElement element)
                        continue;

                    int itemIndex = ShortcutGrid.GetElementIndex(element);
                    if (itemIndex < 0 || itemIndex >= CurrentShortcuts.Count)
                        continue;

                    var item = CurrentShortcuts[itemIndex];
                    // ItemsRepeater 的 Move 重排在个别情况下会保留旧容器上下文；
                    // 最终以当前索引的数据项覆盖容器上下文，确保 x:Bind 与代码视觉同步一致。
                    element.DataContext = item;
                    if (FindChildByName<FrameworkElement>(element, "ShortcutMotionHost") is FrameworkElement host)
                        host.Opacity = 1;

                    // ItemsRepeater 可复用容器；同步所有与项目身份有关的视图状态，
                    // 防止末尾让位的容器保留前一个项目的内容。
                    if (FindChildByName<FontIcon>(element, "ItemIcon") is FontIcon icon)
                        icon.Glyph = item.DisplayGlyph;
                    if (FindChildByName<TextBlock>(element, "ItemName") is TextBlock name)
                    {
                        name.Text = GetDisplayName(item);
                    }

                    if (FindChildByName<Border>(element, "ShortcutCard") is Border card)
                    {
                        string full = GetFullName(item);
                        ToolTipService.SetToolTip(card, string.IsNullOrEmpty(full) ? null : full);
                    }
                    if (FindChildByName<Border>(element, "ShortcutDragPlaceholder") is Border placeholder)
                        placeholder.Visibility = item.DragPlaceholderVisibility;

                    PrepareShortcutVisual(element, item);
                }
                RefreshShortcutSelectionVisuals();
            }
        }

        private void DragSettledTimeout_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            TryReloadAfterDragSettled();
        }

        private void EnsureShortcutMotionState(FrameworkElement element)
        {
            if (_shortcutMotionStates.ContainsKey(element))
                return;

            var motionHost = FindChildByName<FrameworkElement>(element, "ShortcutMotionHost");
            if (motionHost == null)
                return;

            var visual = ElementCompositionPreview.GetElementVisual(motionHost);
            StopShortcutLayoutAnimation(visual);
            visual.Offset = Vector3.Zero;

            _shortcutMotionStates[element] = new ShortcutMotionState
            {
                Element = element,
                MotionHost = motionHost,
                MotionVisual = visual,
                LogicalPosition = Vector2.Zero,
                VisualPosition = Vector2.Zero,
                Velocity = Vector2.Zero,
                Initialized = false
            };

            EnsureShortcutMotionRenderingHook();
        }

        private void ResetShortcutMotionState(FrameworkElement element)
        {
            if (!_shortcutMotionStates.TryGetValue(element, out var state))
                return;

            StopShortcutLayoutAnimation(state.MotionVisual);
            state.MotionVisual.Offset = Vector3.Zero;
            state.LogicalPosition = Vector2.Zero;
            state.VisualPosition = Vector2.Zero;
            state.Velocity = Vector2.Zero;
            state.Initialized = false;
        }

        private void RemoveShortcutMotionState(FrameworkElement element)
        {
            _shortcutLoadAnimatedElements.Remove(element);

            // ⭐ 统一走 StopShortcutLoadAnimation：
            //   - Stop 掉 Storyboard（解除 HoldEnd）
            //   - 把 Opacity/X 复位到起始态
            if (FindChildByName<FrameworkElement>(element, "ShortcutMotionHost") is FrameworkElement motionHost)
            {
                StopShortcutLoadAnimation(element, motionHost, resetToStart: false);
            }
            else if (_shortcutLoadStoryboards.Remove(element, out var sb))
            {
                // 极端情况：拿不到 motionHost，至少把 Storyboard 停掉，别让它成孤儿
                sb.Stop();
            }

            if (_shortcutMotionStates.Remove(element, out var state))
            {
                StopShortcutLayoutAnimation(state.MotionVisual);
                state.MotionVisual.Offset = Vector3.Zero;
                state.Initialized = false;
                state.Velocity = Vector2.Zero;
            }

            DisableShortcutMotionRenderingHookIfUnused();
        }

        private DateTimeOffset _shortcutLoadAnimationBatchStartedAt;

        private void BeginShortcutLoadAnimationBatch()
        {
            _shortcutLoadAnimatedElements.Clear();
            _shortcutLoadAnimationBatchId++;
            _isShortcutLoadAnimationBatchActive = true;
            bool useCenterScaleAnimation = UseCenterScaleShortcutLoadAnimation();
            SetShortcutLoadInteractionLocked(useCenterScaleAnimation);
            _shortcutLoadAnimationBatchUntil = DateTimeOffset.UtcNow.AddMilliseconds(
                useCenterScaleAnimation ? 1000 : 4900);
            _shortcutLoadAnimationBatchStartedAt = DateTimeOffset.UtcNow;   // ⭐ 新增
            PrepareVisibleShortcutsForLoadAnimation();
        }

        private void SetShortcutLoadInteractionLocked(bool isLocked)
        {
            if (isLocked)
            {
                if (_isShortcutLoadInteractionLocked)
                    return;

                _shortcutGridHitTestVisibleBeforeLoadAnimation = ShortcutGridHost.IsHitTestVisible;
                _isShortcutLoadInteractionLocked = true;
                ShortcutGridHost.IsHitTestVisible = false;
                return;
            }

            if (!_isShortcutLoadInteractionLocked)
                return;

            ShortcutGridHost.IsHitTestVisible = _shortcutGridHitTestVisibleBeforeLoadAnimation;
            _isShortcutLoadInteractionLocked = false;
        }

        private bool UseCenterScaleShortcutLoadAnimation()
        {
            return int.TryParse(
                    _ini.ReadValue("System", "ShortcutLoadAnimation", SettingsDefaults.ShortcutLoadAnimation.ToString()),
                    out int animationMode)
                && animationMode == 1;
        }

        private void ShortcutScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            RefreshShortcutScrollBar();
            bool wasScrolling = _isShortcutScrollViewerScrolling;
            _isShortcutScrollViewerScrolling = e.IsIntermediate || _isCustomShortcutScrollbarDragging;

            if (!_isShortcutScrollViewerScrolling)
            {
                if (wasScrolling)
                    _lastShortcutMotionSync = DateTimeOffset.MinValue;
                return;
            }

            if (wasScrolling)
                return;

            StopShortcutAnimationsForScroll();
        }

        private void StopShortcutAnimationsForScroll()
        {
            if (_isShortcutLoadAnimationBatchActive)
            {
                _isShortcutLoadAnimationBatchActive = false;
                _shortcutLoadAnimationBatchUntil = DateTimeOffset.MinValue;
                RestoreAllShortcutVisualsImmediate();
            }

            if (_isShortcutDragging)
                return;

            foreach (var state in _shortcutMotionStates.Values)
            {
                StopShortcutLayoutAnimation(state.MotionVisual);
                state.MotionVisual.Offset = Vector3.Zero;
                state.LogicalPosition = Vector2.Zero;
                state.VisualPosition = Vector2.Zero;
                state.Velocity = Vector2.Zero;
                state.Initialized = false;
            }
        }

        private void PrepareVisibleShortcutsForLoadAnimation()
        {
            int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
            for (int i = 0; i < childCount; i++)
            {
                if (VisualTreeHelper.GetChild(ShortcutGrid, i) is not FrameworkElement element)
                    continue;

                var host = FindChildByName<FrameworkElement>(element, "ShortcutMotionHost") ?? element;
                StopShortcutLoadAnimation(element, host);
                host.Opacity = 0;
                var transform = host.RenderTransform as TranslateTransform ?? new TranslateTransform();
                host.RenderTransform = transform;
                transform.X = 18;
            }
        }

        private void TryAnimateVisibleShortcuts()
        {
            if (_isShortcutScrollViewerScrolling || !_isShortcutLoadAnimationBatchActive)
                return;

            for (int i = 0; i < CurrentShortcuts.Count; i++)
            {
                if (GetGeneratedShortcutElement(i) is FrameworkElement element)
                {
                    TryAnimateShortcutAppearance(element, CurrentShortcuts[i], i);
                }
            }
        }

        private void TryAnimateShortcutAppearance(FrameworkElement element, ShortcutItem item, int index)
        {
            if (!_isShortcutLoadAnimationBatchActive)
                return;

            // ⚠️ 拖拽中不动画，避免覆盖源卡片的 Opacity
            if (_isShortcutDragging)
                return;

            if (DateTimeOffset.UtcNow > _shortcutLoadAnimationBatchUntil)
            {
                _isShortcutLoadAnimationBatchActive = false;
                RestoreAllShortcutVisualsImmediate();
                return;
            }

            if (_shortcutLoadAnimatedElements.TryGetValue(element, out int batchId)
                && batchId == _shortcutLoadAnimationBatchId)
            {
                return;
            }

            _shortcutLoadAnimatedElements[element] = _shortcutLoadAnimationBatchId;

            var motionHost = FindChildByName<FrameworkElement>(element, "ShortcutMotionHost") ?? element;
            StopShortcutLoadAnimation(element, motionHost);

            if (UseCenterScaleShortcutLoadAnimation())
            {
                AnimateShortcutFromGridScale(element, motionHost);
                return;
            }

            motionHost.Opacity = 0;
            var translate = motionHost.RenderTransform as TranslateTransform ?? new TranslateTransform();
            motionHost.RenderTransform = translate;
            translate.X = 18;

            var animationBeginTime = TimeSpan.FromMilliseconds(Math.Min(60 + index * 40, 900));
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(360)),
                BeginTime = animationBeginTime,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, motionHost);
            Storyboard.SetTargetProperty(fade, "Opacity");

            var move = new DoubleAnimation
            {
                From = 18,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(420)),
                BeginTime = animationBeginTime,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(move, translate);
            Storyboard.SetTargetProperty(move, "X");

            var sb = new Storyboard();
            sb.Children.Add(fade);
            sb.Children.Add(move);
            _shortcutLoadStoryboards[element] = sb;

            sb.Begin();
        }

        private void AnimateShortcutFromGridScale(FrameworkElement element, FrameworkElement motionHost)
        {
            const double maxScale = 1.8;
            const double maxStaggerMilliseconds = 200;
            const double animationDurationMilliseconds = 600;
            const double centerBounceScale = 0.98;
            const double edgeBounceScale = 0.95;

            if (ShortcutScrollViewer.ActualWidth <= 0 || ShortcutScrollViewer.ActualHeight <= 0
                || element.ActualWidth <= 0 || element.ActualHeight <= 0
                || motionHost.ActualWidth <= 0 || motionHost.ActualHeight <= 0)
            {
                motionHost.Opacity = 1;
                motionHost.RenderTransform = new CompositeTransform();
                return;
            }

            Point cardCenter;
            try
            {
                cardCenter = element.TransformToVisual(ShortcutScrollViewer).TransformPoint(
                    new Point(element.ActualWidth / 2, element.ActualHeight / 2));
            }
            catch
            {
                motionHost.Opacity = 1;
                motionHost.RenderTransform = new CompositeTransform();
                return;
            }

            double parentCenterX = ShortcutScrollViewer.ActualWidth / 2;
            double parentCenterY = ShortcutScrollViewer.ActualHeight / 2;
            double deltaX = cardCenter.X - parentCenterX;
            double deltaY = cardCenter.Y - parentCenterY;
            double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            double maxDistance = Math.Max(1, Math.Sqrt(parentCenterX * parentCenterX + parentCenterY * parentCenterY));
            double distanceRatio = Math.Clamp(distance / maxDistance, 0, 1);
            double stagger = maxStaggerMilliseconds * distanceRatio;
            double bounceScale = Math.Max(edgeBounceScale,
                centerBounceScale - (centerBounceScale - edgeBounceScale) * distanceRatio);

            // Use the parent center as the actual transform pivot. The pivot must be
            // expressed in motionHost-local coordinates; mixing ScrollViewer-space
            // deltas with the host's local transform can make the card scale around
            // its top-left corner when layout/scroll offsets are present.
            var transform = new CompositeTransform();
            motionHost.RenderTransform = transform;
            Point parentCenterInHost;
            try
            {
                parentCenterInHost = ShortcutScrollViewer.TransformToVisual(motionHost)
                    .TransformPoint(new Point(parentCenterX, parentCenterY));
            }
            catch
            {
                motionHost.Opacity = 1;
                motionHost.RenderTransform = new CompositeTransform();
                return;
            }

            transform.CenterX = parentCenterInHost.X;
            transform.CenterY = parentCenterInHost.Y;
            transform.ScaleX = maxScale;
            transform.ScaleY = maxScale;
            motionHost.Opacity = 0;

            var beginTime = TimeSpan.FromMilliseconds(stagger);
            var duration = TimeSpan.FromMilliseconds(animationDurationMilliseconds);
            var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var storyboard = new Storyboard();

            AddShortcutLoadBounceAnimation(storyboard, transform, "ScaleX", maxScale, bounceScale, duration, beginTime);
            AddShortcutLoadBounceAnimation(storyboard, transform, "ScaleY", maxScale, bounceScale, duration, beginTime);

            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(280)),
                BeginTime = beginTime,
                EasingFunction = easing
            };
            Storyboard.SetTarget(fade, motionHost);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);

            _shortcutLoadStoryboards[element] = storyboard;
            storyboard.Begin();
        }

        private static void AddShortcutLoadAnimation(
            Storyboard storyboard,
            CompositeTransform transform,
            string property,
            double from,
            double to,
            TimeSpan duration,
            TimeSpan beginTime,
            EasingFunctionBase easing)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(duration),
                BeginTime = beginTime,
                EasingFunction = easing
            };
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        private static void AddShortcutLoadBounceAnimation(
            Storyboard storyboard,
            CompositeTransform transform,
            string property,
            double initialScale,
            double bounceScale,
            TimeSpan duration,
            TimeSpan beginTime)
        {
            var reboundTime = TimeSpan.FromTicks(duration.Ticks * 3 / 4);
            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(duration),
                BeginTime = beginTime
            };
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = initialScale
            });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(reboundTime),
                Value = bounceScale,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(duration),
                Value = 1,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        /// <summary>
        /// 把所有已实例化卡片的 Opacity 强制恢复为 1、Translate.X 归 0。
        /// 用于批次超时兜底，避免卡片卡在动画起始态。
        /// </summary>
        private void RestoreAllShortcutVisualsImmediate()
        {
            SetShortcutLoadInteractionLocked(false);

            int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
            for (int i = 0; i < childCount; i++)
            {
                if (VisualTreeHelper.GetChild(ShortcutGrid, i) is not FrameworkElement element)
                    continue;

                if (FindChildByName<FrameworkElement>(element, "ShortcutMotionHost") is FrameworkElement host)
                {
                    // 停掉可能还在跑的 Storyboard
                    if (_shortcutLoadStoryboards.Remove(element, out var sb))
                        sb.Stop();

                    host.Opacity = 1;
                    if (host.RenderTransform is TranslateTransform tt)
                        tt.X = 0;
                    else
                        host.RenderTransform = new TranslateTransform { X = 0 };
                }
            }
        }
        private void StopShortcutLoadAnimation(FrameworkElement element, FrameworkElement host, bool resetToStart = true)
        {
            if (_shortcutLoadStoryboards.Remove(element, out var storyboard))
                storyboard.Stop();

            host.Opacity = resetToStart ? 0 : 1;
            if (host.RenderTransform is TranslateTransform transform)
                transform.X = resetToStart ? 18 : 0;
            else
                host.RenderTransform = new TranslateTransform { X = resetToStart ? 18 : 0 };
        }

        private void RefreshShortcutSelectionVisuals()
        {
            int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
            for (int i = 0; i < childCount; i++)
            {
                if (VisualTreeHelper.GetChild(ShortcutGrid, i) is not FrameworkElement element
                    || ShortcutGrid.GetElementIndex(element) < 0
                    || ShortcutGrid.GetElementIndex(element) >= CurrentShortcuts.Count)
                {
                    continue;
                }

                int index = ShortcutGrid.GetElementIndex(element);
                var selectionVisual = FindChildByName<Border>(element, "ShortcutSelectionVisual");
                if (selectionVisual == null) continue;

                double targetOpacity = _selectedShortcuts.Contains(CurrentShortcuts[index]) ? 1.0 : 0.0;

                var visual = ElementCompositionPreview.GetElementVisual(selectionVisual);
                double currentOpacity = visual.Opacity;   // float，直接拿来比

                // 目标值没变 → 跳过，避免重复播放动画
                if (Math.Abs(currentOpacity - targetOpacity) < 0.01) continue;

                PlaySelectionFade(selectionVisual, targetOpacity);
            }
        }

        private void RefreshShortcutSelectionVisual(FrameworkElement element, ShortcutItem item)
        {
            var selectionVisual = FindChildByName<Border>(element, "ShortcutSelectionVisual");
            if (selectionVisual == null) return;

            double targetOpacity = _selectedShortcuts.Contains(item) ? 1.0 : 0.0;
            var visual = ElementCompositionPreview.GetElementVisual(selectionVisual);
            if (Math.Abs(visual.Opacity - targetOpacity) < 0.01) return;

            if (_isShortcutScrollViewerScrolling)
            {
                visual.StopAnimation("Opacity");
                visual.Opacity = (float)targetOpacity;
                selectionVisual.Opacity = targetOpacity;
                return;
            }

            PlaySelectionFade(selectionVisual, targetOpacity);
        }

        /// <summary>
        /// 播放选中方框的渐入/渐出动画
        /// </summary>
        private static void PlaySelectionFade(UIElement element, double targetOpacity)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            var anim = compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(1f, (float)targetOpacity);
            anim.Duration = TimeSpan.FromMilliseconds(160);

            visual.StartAnimation("Opacity", anim);
        }

        private void SettingsViewControl_Loaded(object sender, RoutedEventArgs e)
        {
            // 构造函数里的 ApplySettings 执行时，部分模板（分类容器、设置页控件等）还没创建；
            // 内容真正 Loaded 后需要再刷一次，字体色/背景色才会正确落到这些控件上。
            // 为了让这次补刷尽量便宜，ApplyGlobalFontFamily / ApplyCurrentLanguage 都做了同值跳过。
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    ApplySettings(reloadNavigation: false, reloadBackgrounds: false);
                    RefreshSettingsScrollBar();
                });
        }

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    => await OpenSettingsAsync();

        /// <summary>从托盘恢复主窗体。用户重新打开主窗体后，之前“等搜索窗口关闭再关闭主窗体”的暂缓作废。</summary>
        private void RestoreMainWindow()
        {
            _deferredShellClose = false;
            _trayIcon?.Show();
        }

        private void TrayIcon_GlobalSearchRequested(object? sender, EventArgs e)
        {
            // 托盘菜单的命令是在托盘窗口过程里派发的，直接创建/激活窗口容易和菜单收尾撞在一起；
            // 排到消息队列末尾再执行，行为和点主界面搜索入口保持一致。
            DispatcherQueue.TryEnqueue(() => ShowGlobalSearchWindow());
        }

        private void TrayIcon_ContextMenuRequested(object? sender, TrayMenuRequestEventArgs e)
        {
            // 托盘回调在窗口过程里触发，排到消息队列末尾再创建 WinUI 窗口，避免重入。
            DispatcherQueue.TryEnqueue(() => _ = ShowTrayMenuAsync(e));
        }

        private async Task ShowTrayMenuAsync(TrayMenuRequestEventArgs e)
        {
            if (_trayMenuOpen) return;

            _trayMenuOpen = true;
            try
            {
                var menu = new TrayMenuWindow(e.ScreenX, e.ScreenY);
                menu.Activate();
                var action = await menu.Result;

                switch (action)
                {
                    case TrayMenuAction.Show:
                        RestoreMainWindow();
                        break;
                    case TrayMenuAction.GlobalSearch:
                        DispatcherQueue.TryEnqueue(() => ShowGlobalSearchWindow());
                        break;
                    case TrayMenuAction.Settings:
                        RestoreMainWindow();
                        await OpenSettingsAsync();
                        break;
                    case TrayMenuAction.Hide:
                        _trayIcon?.Hide();
                        break;
                    case TrayMenuAction.Exit:
                        ExitApplicationCompletely();
                        break;
                }
            }
            finally
            {
                _trayMenuOpen = false;
            }
        }

        private async void TrayIcon_SettingsRequested(object? sender, EventArgs e)
        {
            RestoreMainWindow();
            await OpenSettingsAsync();
        }

        private async Task OpenSettingsAsync(string? page = null)
        {
            if (_isSettingsVisible)
                return;

            await EnsureEditPanelClosedAsync();

            _isSettingsVisible = true;
            UpdateToolbarButtonStates();
            RefreshShortcutScrollBar();
            _emptyHintWasVisibleBeforeSettings = EmptyHintPanel.Visibility == Visibility.Visible;
            _editHostWasVisibleBeforeSettings = EditHost.Visibility == Visibility.Visible;
            _editHostWidthBeforeSettings = EditHost.Width;
            ShortcutGrid.Visibility = Visibility.Collapsed;
            EditHost.Visibility = Visibility.Collapsed;
            SetEmptyHintVisibility(false, durationMs: 120);

            SettingsViewControl.Visibility = Visibility.Visible;
            SettingsViewControl.Opacity = 0;
            SettingsViewControl.ResetToDefaultPage();
            if (page is not null) SettingsViewControl.SelectPage(page);

            var transform = new TranslateTransform { X = 40 };
            SettingsViewControl.RenderTransform = transform;
            var storyboard = new Storyboard();
            var slide = new DoubleAnimation
            {
                From = 40,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EnableDependentAnimation = true
            };
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(180))
            };
            Storyboard.SetTarget(slide, transform);
            Storyboard.SetTargetProperty(slide, "X");
            Storyboard.SetTarget(fade, SettingsViewControl);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(slide);
            storyboard.Children.Add(fade);
            storyboard.Begin();

            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => ApplySettings(reloadNavigation: false));
        }
        private void ApplyGlobalFontFamily(string? fontFamilyName)
        {
            fontFamilyName = string.IsNullOrWhiteSpace(fontFamilyName) ? null : fontFamilyName.Trim();
            if (_fontFamilyApplied
                && string.Equals(_appliedFontFamilyName, fontFamilyName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _fontFamilyApplied = true;
            _appliedFontFamilyName = fontFamilyName;

            FontFamily fontFamily;

            if (string.IsNullOrWhiteSpace(fontFamilyName))
            {
                // 系统默认
                fontFamily = FontFamily.XamlAutoFontFamily;
            }
            else
            {
                try
                {
                    fontFamily = new FontFamily(fontFamilyName);
                }
                catch
                {
                    fontFamily = FontFamily.XamlAutoFontFamily;
                }
            }

            // 覆盖所有已知的字体资源键
            string[] fontKeys =
            {
        "XamlAutoFontFamily",
        "ContentControlThemeFontFamily",
        "MTCMediaFontFamily",
        "PhoneFontFamilyNormal",
        "PhoneFontFamilySemiLight",
        "PivotHeaderItemFontFamily",
        "PivotTitleFontFamily",
        "KeyTipFontFamily"
    };

            foreach (string key in fontKeys)
            {
                Application.Current.Resources[key] = fontFamily;
            }

            // SymbolThemeFontFamily 保持 Segoe Fluent Icons，不要覆盖
            // Application.Current.Resources["SymbolThemeFontFamily"] 保持默认

            // 手动刷新关键区域（部分控件不会自动响应资源变更）
            RefreshFontFamilyOnVisualTree(MainContentArea, fontFamily);
            RefreshFontFamilyOnVisualTree(SidebarGrid, fontFamily);
            RefreshFontFamilyOnVisualTree(TabBarGrid, fontFamily);
            RefreshFontFamilyOnVisualTree(ShortcutGridHost, fontFamily);
            RefreshFontFamilyOnVisualTree(EditHost, fontFamily);
        }

        private static void RefreshFontFamilyOnVisualTree(DependencyObject root, FontFamily fontFamily)
        {
            // ⭐ FontIcon 整个子树跳过，图标字体不能动
            if (root is FontIcon) return;

            if (root is Control control)
                control.FontFamily = fontFamily;
            else if (root is TextBlock textBlock)
                textBlock.FontFamily = fontFamily;

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
                RefreshFontFamilyOnVisualTree(VisualTreeHelper.GetChild(root, i), fontFamily);
        }
        private void SettingsView_BackRequested(object? sender, EventArgs e)
        {
            if (!_isSettingsVisible)
                return;

            var transform = new TranslateTransform { X = 0 };
            SettingsViewControl.RenderTransform = transform;
            var storyboard = new Storyboard();
            var slide = new DoubleAnimation
            {
                From = 0,
                To = 40,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EnableDependentAnimation = true
            };
            var fade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(140))
            };
            Storyboard.SetTarget(slide, transform);
            Storyboard.SetTargetProperty(slide, "X");
            Storyboard.SetTarget(fade, SettingsViewControl);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(slide);
            storyboard.Children.Add(fade);
            storyboard.Completed += (s, args) =>
            {
                SettingsViewControl.Visibility = Visibility.Collapsed;
                _isSettingsVisible = false;
                UpdateToolbarButtonStates();
                RefreshShortcutScrollBar();
                SettingsViewControl.RenderTransform = null;
                SetEmptyHintVisibility(_emptyHintWasVisibleBeforeSettings);
                RestoreContentHostsAfterSettings();
                AnimateContentAfterSettingsClosed();
            };
            storyboard.Begin();
        }

        private void CloseSettingsForNavigation()
        {
            _ = CloseSettingsWithAnimationAsync(restoreEmptyHint: false);   // ⚠️ 导航退出不恢复
        }
        /// <summary>
        /// 带渐入渐出地控制 EmptyHintPanel 的显示/隐藏
        /// </summary>
        private void SetEmptyHintVisibility(bool visible, int durationMs = 200)
        {
            if (EmptyHintPanel == null) return;
            if (visible && _isSettingsVisible) return;

            if (visible)
            {
                // 已经可见且不透明度接近 1 → 跳过
                if (EmptyHintPanel.Visibility == Visibility.Visible && EmptyHintPanel.Opacity > 0.99)
                    return;

                EmptyHintPanel.Visibility = Visibility.Visible;

                var sb = new Storyboard();
                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(anim, EmptyHintPanel);
                Storyboard.SetTargetProperty(anim, "Opacity");
                sb.Children.Add(anim);
                sb.Begin();
            }
            else
            {
                // 已经隐藏 → 跳过
                if (EmptyHintPanel.Visibility != Visibility.Visible) return;

                var sb = new Storyboard();
                var anim = new DoubleAnimation
                {
                    From = EmptyHintPanel.Opacity,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(anim, EmptyHintPanel);
                Storyboard.SetTargetProperty(anim, "Opacity");
                sb.Children.Add(anim);

                sb.Completed += (s, e) =>
                {
                    if (EmptyHintPanel.Opacity < 0.01)
                        EmptyHintPanel.Visibility = Visibility.Collapsed;
                };
                sb.Begin();
            }
        }
        private Task CloseSettingsWithAnimationAsync(bool restoreEmptyHint = true)
        {
            if (!_isSettingsVisible)
                return Task.CompletedTask;

            var completion = new TaskCompletionSource<bool>();
            var transform = new TranslateTransform { X = 0 };
            SettingsViewControl.RenderTransform = transform;
            var storyboard = new Storyboard();
            var slide = new DoubleAnimation
            {
                From = 0,
                To = 40,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EnableDependentAnimation = true
            };
            var fade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(140))
            };
            Storyboard.SetTarget(slide, transform);
            Storyboard.SetTargetProperty(slide, "X");
            Storyboard.SetTarget(fade, SettingsViewControl);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(slide);
            storyboard.Children.Add(fade);
            storyboard.Completed += (s, args) =>
            {
                SettingsViewControl.Visibility = Visibility.Collapsed;
                SettingsViewControl.Opacity = 1;
                SettingsViewControl.RenderTransform = null;
                _isSettingsVisible = false;
                UpdateToolbarButtonStates();
                RefreshShortcutScrollBar();
                // ⚠️ 只有"点返回按钮"才恢复 EmptyHintPanel
                // "通过导航退出"时不恢复，让 UpdateEmptyHint 决定
                if (restoreEmptyHint)
                {
                    SetEmptyHintVisibility(_emptyHintWasVisibleBeforeSettings);
                }
                RestoreContentHostsAfterSettings();
                AnimateContentAfterSettingsClosed();
                completion.TrySetResult(true);
            };
            storyboard.Begin();
            return completion.Task;
        }

        private void RestoreContentHostsAfterSettings()
        {
            ShortcutGrid.Visibility = Visibility.Visible;
            EditHost.Visibility = _editHostWasVisibleBeforeSettings
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (_editHostWasVisibleBeforeSettings)
                EditHost.Width = _editHostWidthBeforeSettings;
        }

        private void AnimateContentAfterSettingsClosed()
        {
            var storyboard = new Storyboard();
            bool animateShortcutGrid = ShortcutGrid.Visibility == Visibility.Visible;

            if (animateShortcutGrid)
            {
                ShortcutGrid.Opacity = 0;
                var shortcutAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(220))
                };
                Storyboard.SetTarget(shortcutAnimation, ShortcutGrid);
                Storyboard.SetTargetProperty(shortcutAnimation, "Opacity");
                storyboard.Children.Add(shortcutAnimation);
            }

            // ⚠️ EmptyHintPanel 那段整个删掉，交给 SetEmptyHintVisibility

            if (storyboard.Children.Count == 0)
                return;

            storyboard.Completed += (s, e) =>
            {
                ShortcutGrid.Opacity = 1;
            };
            storyboard.Begin();
        }

        private DispatcherQueueTimer? _applySettingsDebounce;

        private void SettingsView_SettingsChanged(object? sender, EventArgs e)
        {
            if (_closeAnimationStarted || _allowWindowClose)
                return;

            ConfigureMenuKeyboardAccelerators();
            _applySettingsDebounce ??= DispatcherQueue.CreateTimer();
            _applySettingsDebounce.Interval = TimeSpan.FromMilliseconds(100);
            _applySettingsDebounce.IsRepeating = false;
            _applySettingsDebounce.Tick -= ApplySettingsDebounce_Tick;
            _applySettingsDebounce.Tick += ApplySettingsDebounce_Tick;
            _applySettingsDebounce.Stop();
            _applySettingsDebounce.Start();
        }

        private void SettingsView_CategoryNavigationSettingsChanged(object? sender, EventArgs e)
        {
            LoadCategoriesFromDb();
        }

        private void ConfigureMenuKeyboardAccelerators()
        {
            ContentAreaRoot.KeyboardAccelerators.Clear();
            var bindings = new (string Key, string Gesture, MenuFlyoutItem Item, Action<object?, RoutedEventArgs> Execute)[]
            {
                ("Add", "Ctrl+1", EditAddMenuItem, (s, e) => MenuAdd_Click(s, e)),
                ("SelectAll", "Ctrl+A", EditSelectAllMenuItem, (s, e) => MenuSelectAll_Click(s, e)),
                ("Cut", "Ctrl+X", EditCutMenuItem, (s, e) => MenuCut_Click(s, e)),
                ("Copy", "Ctrl+C", EditCopyMenuItem, (s, e) => MenuCopy_Click(s, e)),
                ("Paste", "Ctrl+V", EditPasteMenuItem, (s, e) => MenuPaste_Click(s, e)),
                ("Rename", "Ctrl+R", EditRenameMenuItem, (s, e) => MenuRename_Click(s, e)),
                ("Delete", "Delete", EditDeleteMenuItem, (s, e) => MenuDelete_Click(s, e)),
                ("Favorite", "Ctrl+Q", EditFavoriteMenuItem, (s, e) => MenuFavorite_Click(s, e)),
                ("NewCategory", "Ctrl+2", FileNewCategoryMenuItem, (s, e) => MenuNewCategory_Click(s, e)),
                ("NewTab", "Ctrl+3", FileNewTabMenuItem, (s, e) => MenuNewTab_Click(s, e))
            };

            var seenGestures = new HashSet<(Windows.System.VirtualKey Key, Windows.System.VirtualKeyModifiers Modifiers)>();

            foreach (var binding in bindings)
            {
                string gesture = _ini.ReadValue("Hotkeys", binding.Key, binding.Gesture);
                if (!TryParseKeyboardGesture(gesture, out var key, out var modifiers))
                    continue;
                if ((modifiers & Windows.System.VirtualKeyModifiers.Windows) != 0)
                    continue;   // 不允许 Win 组合

                // 旧配置里可能存在重复手势：只注册第一个，避免同一次按键触发多个动作。
                if (!seenGestures.Add((key, modifiers)))
                    continue;

                var keyboardAccelerator = new KeyboardAccelerator
                {
                    Key = key,
                    Modifiers = modifiers
                };
                keyboardAccelerator.Invoked += (_, args) =>
                {
                    if (_isSettingsVisible)
                    {
                        args.Handled = true;
                        return;
                    }

                    if (binding.Key == "Delete" && FocusManager.GetFocusedElement(MainWindowRoot.XamlRoot) is TextBox)
                        return;

                    binding.Execute(this, new RoutedEventArgs());
                    args.Handled = true;
                };
                ContentAreaRoot.KeyboardAccelerators.Add(keyboardAccelerator);
            }
        }

        private static bool TryParseKeyboardGesture(string gesture, out Windows.System.VirtualKey key, out Windows.System.VirtualKeyModifiers modifiers)
            => HotkeyGesture.TryParse(gesture, out key, out modifiers);

        private void SettingsView_DatabaseRestoredRequested(object? sender, EventArgs e)
        {
            MyTabView.SelectedIndex = -1;
            NavMenuList.SelectedIndex = -1;
            _hasActiveTabSelection = false;

            LoadCategoriesFromDb();
            SelectDefaultCategory();
            LoadTabsFromDb();
            LoadShortcutsFromDb();
            ScheduleUpdateTabPaths();
        }

        private void SettingsView_LanguageChanged(object? sender, EventArgs e)
        {
            if (_closeAnimationStarted || _allowWindowClose)
                return;

            ApplyCurrentLanguage(LanguageService.CurrentLanguageCode);
        }

        private void LanguageService_LanguageChanged(object? sender, EventArgs e)
        {
            if (_closeAnimationStarted || _allowWindowClose)
                return;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_closeAnimationStarted || _allowWindowClose)
                    return;

                Bindings?.Update();
                ApplyCurrentLanguage(LanguageService.CurrentLanguageCode);
            });
        }

        private void ApplyCurrentLanguage(string? requestedLanguageCode = null)
        {
            if (_closeAnimationStarted || _allowWindowClose)
                return;

            string languageCode = requestedLanguageCode ?? LanguageService.CurrentLanguageCode;

            bool languageChanged = !string.Equals(_appliedLanguageCode, languageCode, StringComparison.OrdinalIgnoreCase);
            if (languageChanged)
            {
                _appliedLanguageCode = languageCode;
                LanguageService.ApplyLanguage(Content as DependencyObject, languageCode);
                LanguageService.ApplyMenuFlyout(FileMenuFlyout, languageCode);
                LanguageService.ApplyMenuFlyout(EditMenuFlyout, languageCode);
                LanguageService.ApplyMenuFlyout(ViewMenuFlyout, languageCode);
                LanguageService.ApplyMenuFlyout(HelpMenuFlyout, languageCode);
            }

            foreach (var category in Categories)
                category.RefreshLocalizedProperties();
            foreach (var tab in TabItems)
                tab.RefreshLocalizedTitle();
            UpdateMissingFilesNotice();
            UpdateEmptyHint();
            RefreshNavigationLocalizedTexts();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_closeAnimationStarted || _allowWindowClose)
                    return;

                if (languageChanged && SettingsViewControl.IsLoaded)
                    LanguageService.ApplyLanguage(SettingsViewControl, languageCode);
                RefreshNavigationLocalizedTexts();
            });
        }

        private void RefreshNavigationLocalizedTexts()
        {
            for (int i = 0; i < NavMenuList.Items.Count; i++)
            {
                if (NavMenuList.Items[i] is not Category category
                    || NavMenuList.ContainerFromIndex(i) is not ListViewItem container)
                    continue;

                if (category.IsSectionHeader)
                {
                    var headerText = FindChildByName<TextBlock>(container, "SectionHeaderLabel");
                    if (headerText != null)
                        headerText.Text = _isSidebarExpanded
                            ? category.LocalizedSectionHeaderText
                            : category.LocalizedCompactHeaderText;
                }
                else if (!category.IsSeparator)
                {
                    var categoryText = FindChildByName<TextBlock>(container, "CategoryText");
                    if (categoryText != null)
                        categoryText.Text = category.DisplayName;
                }
            }
        }

        private void ApplySettingsDebounce_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            if (_closeAnimationStarted || _allowWindowClose)
                return;

            ApplySettings(reloadNavigation: false);
            ScheduleUpdateTabPaths();
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!IsEditPanelOpen) return;

            // 拿点击点相对 EditHost 的坐标
            var pt = e.GetCurrentPoint(EditHost).Position;

            // 落在 EditHost 矩形内 → 忽略
            if (pt.X >= 0 && pt.X <= EditHost.ActualWidth
                && pt.Y >= 0 && pt.Y <= EditHost.ActualHeight)
            {
                return;
            }

            // ⭐ 点击在面板外 → 触发对应面板的取消
            switch (_editPanelKind)
            {
                case EditPanelKind.Category:
                    CategoryEditPanelControl.RequestCancel();
                    break;
                case EditPanelKind.Tab:
                    TabEditPanelControl.RequestCancel();
                    break;
            }
        }
        /// <summary>
        /// 滑動開始：把所有 HoverVisual 的 Opacity 淡出到 0
        /// ⚠️ 只改 Opacity，不改 Visibility（避免和 VSM 打架）
        /// </summary>
        private void FadeOutHoverVisuals(int durationMs = 100)
        {
            foreach (var item in TabItems)
            {
                var container = MyTabView.ContainerFromItem(item) as TabViewItem;
                if (container == null) continue;

                var path = FindChildByName<Microsoft.UI.Xaml.Shapes.Path>(container, "HoverVisual");
                if (path == null) continue;

                if (ReferenceEquals(MyTabView.SelectedItem, item))
                {
                    path.Visibility = Visibility.Collapsed;
                    continue;
                }

                var sb = new Storyboard();
                var anim = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(anim, path);
                Storyboard.SetTargetProperty(anim, "Opacity");
                sb.Children.Add(anim);
                sb.Begin();
            }
        }

        /// <summary>
        /// 滑動結束：把所有 HoverVisual 的 Opacity 淡入回 1
        /// </summary>
        private void FadeInHoverVisuals(int durationMs = 150)
        {
            foreach (var item in TabItems)
            {
                var container = MyTabView.ContainerFromItem(item) as TabViewItem;
                if (container == null) continue;

                var path = FindChildByName<Microsoft.UI.Xaml.Shapes.Path>(container, "HoverVisual");
                if (path == null) continue;

                var sb = new Storyboard();
                var anim = new DoubleAnimation
                {
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(anim, path);
                Storyboard.SetTargetProperty(anim, "Opacity");
                sb.Children.Add(anim);
                sb.Begin();
            }
        }
        /// <summary>
        /// 确保当前编辑面板已关闭（如果有），并等待关闭动画完成
        /// </summary>
        private async Task EnsureEditPanelClosedAsync()
        {
            if (!IsEditPanelOpen) return;

            // 触发当前面板取消
            switch (_editPanelKind)
            {
                case EditPanelKind.Category:
                    CategoryEditPanelControl.RequestCancel();
                    break;
                case EditPanelKind.Tab:
                    TabEditPanelControl.RequestCancel();
                    break;
            }

            // 等 Hide 动画完成（Hide 时长 = EditPanelAnimMs - 40）
            await Task.Delay(TimeSpan.FromMilliseconds(EditPanelAnimMs - 40));
        }
        private void StartBackgroundVerify()
        {
            if (!_verifyClosedHandlerRegistered)
            {
                Closed += (_, _) => CancelBackgroundVerify();
                _verifyClosedHandlerRegistered = true;
            }

            var previous = _verifyCts;
            var current = new CancellationTokenSource();
            _verifyCts = current;
            previous?.Cancel(); // 旧任务在自己的 finally 中释放 CTS

            _verifySucceeded = false;
            _verifyTask = RunBackgroundVerifyAsync(current);
        }

        private async Task RunBackgroundVerifyAsync(CancellationTokenSource cts)
        {
            var ct = cts.Token;

            try
            {
                // 稍微延迟，等 UI 稳下来
                // Wait for either the startup grace period or shutdown without
                // canceling Task.Delay (which throws TaskCanceledException).
                if (!await DelayUnlessCanceledAsync(TimeSpan.FromMilliseconds(5000), ct))
                    return;
                if (ct.IsCancellationRequested)
                    return;

                System.Diagnostics.Debug.WriteLine("[校验] 后台全量校验开始");

                var missing = await VerifyService.VerifyAllAsync(ct);
                if (ct.IsCancellationRequested)
                    return;

                System.Diagnostics.Debug.WriteLine($"[校验] 完成，丢失 {missing.Count} 项");

                if (!ReferenceEquals(_verifyCts, cts)) return;
                _missingFiles = missing;
                _verifySucceeded = true;
                UpdateMissingFilesNotice();
                MissingFilesNotice.Visibility = missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine("[校验] 后台校验已取消");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[校验] 后台校验异常: {ex}");
            }
            finally
            {
                if (ReferenceEquals(_verifyCts, cts))
                    _verifyCts = null;

                cts.Dispose();
            }
        }

        private void CancelBackgroundVerify()
        {
            var cts = _verifyCts;
            _verifyCts = null;
            cts?.Cancel(); // 不提前 Dispose；任务结束后自行释放
        }

        private static async Task<bool> DelayUnlessCanceledAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => canceled.TrySetResult(true));
            Task delayTask = Task.Delay(delay);
            Task completed = await Task.WhenAny(delayTask, canceled.Task);
            return completed == delayTask && !cancellationToken.IsCancellationRequested;
        }

        private void UpdateMissingFilesNotice()
        {
            MissingFilesNoticeText.Text = LanguageService.TranslateFormat(
                "已检测到 {count} 项失效文件，是否查看？", ("{count}", _missingFiles.Count));
        }

        private async void MenuMissingFiles_Click(object sender, RoutedEventArgs e)
            => await OpenMissingFilesAsync();

        private async void MissingFilesNoticeView_Click(object sender, RoutedEventArgs e)
            => await OpenMissingFilesAsync();

        private void MissingFilesNoticeClose_Click(object sender, RoutedEventArgs e)
            => MissingFilesNotice.Visibility = Visibility.Collapsed;

        private async Task OpenMissingFilesAsync()
        {
            if (_missingFilesDialogOpen) return;
            _missingFilesDialogOpen = true;
            try
            {
                if (!_verifySucceeded && (_verifyTask == null || _verifyTask.IsCompleted))
                    StartBackgroundVerify();
                if (_verifyTask != null && !_verifyTask.IsCompleted)
                {
                    await ShowMessageAsync("正在检查失效文件...");
                    await _verifyTask;
                }
                if (_allowWindowClose || _closeAnimationStarted) return;
                if (!_verifySucceeded)
                {
                    await ShowMessageAsync("失效文件检查失败，请稍后重试。");
                    return;
                }
                if (_isSettingsVisible)
                    await CloseSettingsWithAnimationAsync();
                MissingFilesNotice.Visibility = Visibility.Collapsed;
                await ShowMissingFilesDialogAsync(new List<ShortcutItem>(_missingFiles));
            }
            finally
            {
                _missingFilesDialogOpen = false;
            }
        }

        private async Task ShowMissingFilesDialogAsync(List<ShortcutItem> missing)
        {
            var categoryNames = ModeRepository.GetAllModes()
                .SelectMany(mode => BlockBox.Data.CategoryRepository.GetVisible(mode))
                .GroupBy(category => category.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.OrdinalIgnoreCase);

            var window = new MissingFilesWindow(missing, categoryNames);
            await ModalWindowHelper.ShowModalAsync(this, window);

            if (window.Result == 1)
            {
                foreach (var s in missing)
                {
                    try { if (!RecycleBinRepository.MoveToRecycleBin(s.Id)) continue; }
                    catch { continue; }
                    var displayedShortcut = CurrentShortcuts.FirstOrDefault(item =>
                        string.Equals(item.Id, s.Id, StringComparison.OrdinalIgnoreCase));
                    if (displayedShortcut != null)
                        CurrentShortcuts.Remove(displayedShortcut);
                }
                StartBackgroundVerify();
            }
            UpdateEmptyHint();
        }

        private bool _updatePending = false;

        private void ScheduleUpdateTabPaths()
        {
            if (_updatePending) return;
            _updatePending = true;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _updatePending = false;
                UpdateTabPaths();
                // 不打断正在进行的选中滑动动画；动画完成回调会同步最终选中背景。
                if (_tabSelectionStoryboard == null)
                    UpdateSlidingIndicator(animate: false);
            });
        }

        private void ScheduleSelectedTabVisualRefresh()
        {
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    if (!MyTabView.IsLoaded)
                        return;

                    // 不让集合重载或容器 Loaded 的延迟刷新打断选中滑动动画。
                    if (_tabSelectionStoryboard != null)
                        return;

                    // 等 TabItemsSource 的容器完成生成及布局后，再基于当前 SelectedItem 更新背景。
                    MyTabView.UpdateLayout();
                    UpdateTabPaths();
                    UpdateSlidingIndicator(animate: false);
                });
        }

        private void RefreshSelectedTabVisualIfCurrent(TabViewItem container)
        {
            if (!ReferenceEquals(MyTabView.SelectedItem, container.DataContext))
                return;

            // 选中滑动动画期间由动画完成回调负责显示最终背景，避免 Loaded 补刷抢先结束动画效果。
            if (_tabSelectionStoryboard != null)
                return;

            UpdateSingleTabPath(container);
            ShowSelectedVisual(container);
        }

        // 用户点击 TabView 上的 "+" 按钮时触发
        private void TabView_AddTabButtonClick(TabView sender, object args)
        {
            TabItems.Add(new MyTabItem
            {
                Title = "新标签",
                IsClosable = true
            });
        }

        private void TabViewItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TabViewItem tabItem)
                return;

            if (tabItem.DataContext is MyTabItem item && _tabsPendingFadeIn.Contains(item))
                tabItem.Opacity = 0;

            if (_tabBeingRestored == null)
                ConfigureTabSizeAnimation(tabItem);

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (tabItem.XamlRoot == null)
                    return;

                if (ElementCompositionPreview.GetElementVisual(tabItem).ImplicitAnimations == null)
                    ConfigureTabSizeAnimation(tabItem);

                TryStartTabFadeIn(tabItem);

                if (ReferenceEquals(MyTabView.SelectedItem, tabItem.DataContext))
                    RefreshSelectedTabVisualIfCurrent(tabItem);

                ScheduleTabWidthUpdate();
            });
        }

        private static void ConfigureTabSizeAnimation(TabViewItem tabItem)
        {
            var visual = ElementCompositionPreview.GetElementVisual(tabItem);
            var compositor = visual.Compositor;
            var animation = compositor.CreateVector2KeyFrameAnimation();
            animation.Target = "Size";
            var easing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.2f, 0f), new Vector2(0.2f, 1f));
            animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
            animation.InsertExpressionKeyFrame(1f, "this.FinalValue", easing);
            animation.Duration = TimeSpan.FromMilliseconds(220);

            var implicitAnimations = compositor.CreateImplicitAnimationCollection();
            implicitAnimations["Size"] = animation;
            visual.ImplicitAnimations = implicitAnimations;
        }

        private void TryStartPendingTabFadeIns()
        {
            foreach (var item in _tabsPendingFadeIn.ToList())
            {
                if (MyTabView.ContainerFromItem(item) is TabViewItem container)
                    TryStartTabFadeIn(container);
            }
        }

        private void TryStartTabFadeIn(TabViewItem container)
        {
            if (container.DataContext is not MyTabItem item
                || !_tabsPendingFadeIn.Remove(item))
            {
                return;
            }

            const int fadeDurationMs = 170;
            container.Opacity = 0;

            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(fadeDurationMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, container);
            Storyboard.SetTargetProperty(fade, "Opacity");

            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Completed += (_, _) =>
            {
                container.Opacity = 1;
                storyboard.Stop();
            };
            storyboard.Begin();
        }

        private async void CustomAddButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();

            await EnsureEditPanelClosedAsync();   // ⭐ 先确保关闭

            var result = await ShowTabEditPanelAsync(editing: null);
            if (result == null || !result.IsConfirmed) return;


            if (TabItems.Any(t => t.Title == result.Title))
            {
                await ShowMessageAsync("已存在同名标签");
                return;
            }

            var tab = new MyTabItem
            {
                Title = result.Title,
                Note = result.Note,
                IconGlyph = result.Icon,               // ⭐
                ContentKind = result.Kind,
                ExtensionList = result.ExtList,
                IsClosable = true,
                BelongingMode = _currentMode,
                BuiltinKind = BuiltinTabKind.None,
                IsPinned = true
            };

            BlockBox.Data.TabRepository.Add(tab);
            TabItems.Add(tab);
            MyTabView.SelectedItem = tab;
        }
        // 用户点击标签页上的 "×" 关闭按钮时触发
        private void TabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            _hoverPreview?.Close();
            if (args.Item is not MyTabItem item) return;
            if (IsEditPanelOpen) return;   // ⭐ 面板开着不允许关闭

            // ============ 内置标签：改字段 + 写 INI ============
            if (item.BuiltinKind == BuiltinTabKind.Favorites)
            {
                _favoritesTabPinned = false;
                _ini.WriteValue("Tabs", "FavoritesPinned", "false");
                TabItems.Remove(item);
            }
            else if (item.BuiltinKind == BuiltinTabKind.Recent)
            {
                _recentTabPinned = false;
                _ini.WriteValue("Tabs", "RecentPinned", "false");
                TabItems.Remove(item);
            }
            // ============ 用户标签：取消固定（保留数据）============
            else
            {
                BlockBox.Data.TabRepository.SetPinned(item.Id, false);
                TabItems.Remove(item);
            }

            // 关的是当前选中 → 内容区清空
            if (MyTabView.SelectedItem == null)
            {
                CurrentShortcuts.Clear();
                _currentTabItem = null;
                _hasActiveTabSelection = false;
            }
        }
        private async void MyTabView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (IsEditPanelOpen) return;

            // 找双击落在哪个 TabViewItem 上
            var source = e.OriginalSource as FrameworkElement;
            TabViewItem? container = null;
            while (source != null && source != MyTabView)
            {
                if (source is TabViewItem tvi) { container = tvi; break; }
                source = VisualTreeHelper.GetParent(source) as FrameworkElement;
            }

            if (container?.Content is not MyTabItem item) return;
            if (item.BuiltinKind != BuiltinTabKind.None) return;   // 内置标签不可编辑

            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();

            var result = await ShowTabEditPanelAsync(editing: item);
            if (result == null) return;

            // 用户点了"删除标签"
            if (result.IsDelete)
            {
                if (ShouldConfirmDelete())
                {
                    var shortcuts = BlockBox.Data.ShortcutRepository
                        .GetByTab(_currentMode, item.Id);
                    string warn = shortcuts.Count > 0
                        ? "\n\n" + LanguageService.TranslateFormat("该标签下有 {count} 个引用，删除后它们会从标签移除（文件本身不动）。", ("{count}", shortcuts.Count))
                        : "";

                    int confirm = await ShowConfirmationWindowAsync(
                        "删除标签", LanguageService.TranslateFormat("确定删除「{name}」吗？", ("{name}", item.Title)) + warn, "删除");
                    if (confirm != 1) return;
                }

                BlockBox.Data.TabRepository.Delete(item.Id);
                TabItems.Remove(item);

                if (MyTabView.SelectedItem == null)
                {
                    CurrentShortcuts.Clear();
                    _currentTabItem = null;
                    _hasActiveTabSelection = false;
                }
                return;
            }

            if (!result.IsConfirmed) return;

            // 保存修改：原地更新，不重建 TabView 容器，避免标签消失/选中条滑动
            bool filterChanged = item.ContentKind != result.Kind
                || !string.Equals(item.ExtensionList, result.ExtList, StringComparison.OrdinalIgnoreCase);

            BlockBox.Data.TabRepository.Update(item.Id, result.Title, result.Note, result.Icon, result.Kind, result.ExtList);

            item.Title = result.Title;          // setter 会通知 DisplayTitle
            item.Note = result.Note;
            item.IconGlyph = result.Icon;       // setter 会通知图标绑定
            item.ContentKind = result.Kind;
            item.ExtensionList = result.ExtList;
            item.RefreshLocalizedTitle();

            UpdateEmptyHint();
            if (filterChanged)
                LoadShortcutsFromDb();
            MyTabView.SelectedItem = item;
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            SearchBox.Focus(FocusState.Programmatic);
        }
        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            // 收集所有"未固定在标签栏"的标签
            var hiddenTabs = new List<MyTabItem>();

            if (!_favoritesTabPinned)
            {
                hiddenTabs.Add(new MyTabItem
                {
                    Title = "★ 收藏",
                    BelongingMode = _currentMode,
                    BuiltinKind = BuiltinTabKind.Favorites
                });
            }
            if (!_recentTabPinned)
            {
                hiddenTabs.Add(new MyTabItem
                {
                    Title = "🕒 最近访问",
                    BelongingMode = _currentMode,
                    BuiltinKind = BuiltinTabKind.Recent
                });
            }

            foreach (var t in BlockBox.Data.TabRepository.GetVisible(_currentMode))
            {
                if (!t.IsPinned)
                    hiddenTabs.Add(t);
            }

            var flyout = new Flyout();
            var panel = new StackPanel
            {
                Width = 220,
                MinHeight = 24,
                Orientation = Orientation.Vertical,
                Spacing = 0
            };

            if (hiddenTabs.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "没有隐藏的标签",
                    Padding = new Thickness(12, 8, 12, 8),
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources["TextFillColorTabBarBrush"]
                };
                panel.Children.Add(empty);
            }
            else
            {
                var tabBarBrush = (Microsoft.UI.Xaml.Media.Brush)
                    Application.Current.Resources["TextFillColorTabBarBrush"];

                foreach (var tab in hiddenTabs)
                {
                    // ⭐ 每行一个 Grid：左标题 + 右删除
                    var row = new Grid
                    {
                        Width = 220,
                        Height = 24,
                    };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // ---- 左：标题按钮（点击钉回）----
                    //var pinBtn = new Button
                    //{
                    //    Content = tab.Title,
                    //    Height = 24,
                    //    FontSize = 8,
                    //    HorizontalAlignment = HorizontalAlignment.Stretch,
                    //    HorizontalContentAlignment = HorizontalAlignment.Left,
                    //    Padding = new Thickness(12, 0, 0, 0),
                    //    Foreground = tabBarBrush,
                    //    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    //    BorderThickness = new Thickness(0),
                    //    Style = (Style)Application.Current.Resources["StaticMenuTextButtonStyle"]
                    //};
                    var pinBtn = new Button
                    {
                        Content = tab.Title,
                        Height = 24,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(12, 0, 0, 0),
                        Foreground = tabBarBrush,
                        Style = (Style)Application.Current.Resources["MenuItemButtonStyle"]
                    };
                    Grid.SetColumn(pinBtn, 0);

                    var capturedPin = tab;
                    pinBtn.Click += (s, args) =>
                    {
                        flyout.Hide();
                        PinTab(capturedPin);
                    };
                    row.Children.Add(pinBtn);

                    // ---- 右：删除按钮（X）----
                    var delBtn = new Button
                    {
                        Width = 32,
                        Height = 24,
                        Padding = new Thickness(0),
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        BorderThickness = new Thickness(0),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Foreground = tabBarBrush,
                        IsEnabled = tab.BuiltinKind == BuiltinTabKind.None,  // ⭐ 内置标签不能删
                        Content = new FontIcon { Glyph = "\uE738", FontSize = 11 }   // 减号
                    };
                    Grid.SetColumn(delBtn, 1);

                    var capturedDel = tab;
                    delBtn.Click += async (s, args) =>
                    {
                        flyout.Hide();
                        await DeleteHiddenTabAsync(capturedDel);
                    };
                    row.Children.Add(delBtn);

                    panel.Children.Add(row);
                }
            }

            flyout.Content = panel;
            // FlyoutPresenterStyle 那几行原样保留
            var presenterStyle = new Style(typeof(FlyoutPresenter));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.MinHeightProperty, 0.0));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty, 0.0));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(6)));

            // 背景色
            string tabBarBgColor = _ini.ReadValue("Global", "PrimaryBackground", SettingsDefaults.PrimaryBackground);
            var tabBarColor = ParseColor(tabBarBgColor);
            const double whitenAmount = 0.30;
            byte BlendWithWhite(byte c) =>
                (byte)Math.Clamp(Math.Round(c + (255 - c) * whitenAmount), 0, 255);

            var semiTransparentColor = Color.FromArgb(
                (byte)Math.Round(255 * 0.45),
                BlendWithWhite(tabBarColor.R),
                BlendWithWhite(tabBarColor.G),
                BlendWithWhite(tabBarColor.B));
            presenterStyle.Setters.Add(new Setter(
                FlyoutPresenter.BackgroundProperty,
                new SolidColorBrush(semiTransparentColor)));

            // ⚠️ 边框色：用背景色减淡/加深一点点
            // 边框：同色系，稍微不那么透明
            var borderColor = Color.FromArgb(
                (byte)Math.Round(255 * 0.5),
                tabBarColor.R,
                tabBarColor.G,
                tabBarColor.B);
            presenterStyle.Setters.Add(new Setter(
                FlyoutPresenter.BorderBrushProperty,
                new SolidColorBrush(borderColor)));

            presenterStyle.Setters.Add(new Setter(
                FlyoutPresenter.BorderBrushProperty,
                new SolidColorBrush(borderColor)));
            presenterStyle.Setters.Add(new Setter(
                FlyoutPresenter.BorderThicknessProperty,
                new Thickness(1)));

            flyout.FlyoutPresenterStyle = presenterStyle;
            LanguageService.RegisterDynamicRoot(panel);

            flyout.ShowAt(ExpandButton, new FlyoutShowOptions
            {
                Position = new Windows.Foundation.Point(0, ExpandButton.ActualHeight),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                ShowMode = FlyoutShowMode.Standard
            });
        }

        /// <summary>
        /// 把一个隐藏的标签"钉回"标签栏
        /// </summary>
        private void PinTab(MyTabItem tab)
        {
            if (tab.BuiltinKind == BuiltinTabKind.Favorites)
            {
                _favoritesTabPinned = true;
                _ini.WriteValue("Tabs", "FavoritesPinned", "true");
            }
            else if (tab.BuiltinKind == BuiltinTabKind.Recent)
            {
                _recentTabPinned = true;
                _ini.WriteValue("Tabs", "RecentPinned", "true");
            }
            else
            {
                BlockBox.Data.TabRepository.SetPinned(tab.Id, true);
            }

            var headerList = _tabHeaderList;
            var previousTransitions = headerList?.ItemContainerTransitions;
            var sizeAnimations = new List<(Visual Visual, ImplicitAnimationCollection? Animations)>();
            foreach (var existing in TabItems)
            {
                if (MyTabView.ContainerFromItem(existing) is TabViewItem container)
                {
                    var visual = ElementCompositionPreview.GetElementVisual(container);
                    sizeAnimations.Add((visual, visual.ImplicitAnimations));
                    visual.ImplicitAnimations = null;
                }
            }

            _tabBeingRestored = tab;
            try
            {
                if (headerList != null)
                    headerList.ItemContainerTransitions = new TransitionCollection();

                LoadTabsFromDb();
                MyTabView.UpdateLayout();
                UpdateTabWidths();
                MyTabView.UpdateLayout();
            }
            finally
            {
                _tabBeingRestored = null;
                foreach (var (visual, animations) in sizeAnimations)
                    visual.ImplicitAnimations = animations;
                if (headerList != null)
                    headerList.ItemContainerTransitions = previousTransitions;
            }

            foreach (var pending in _tabsPendingFadeIn.ToList())
            {
                if (MyTabView.ContainerFromItem(pending) is TabViewItem container)
                    ConfigureTabSizeAnimation(container);
            }
            TryStartPendingTabFadeIns();

            // 自动选中刚钉回来的标签
            var newItem = TabItems.FirstOrDefault(t =>
                t.BuiltinKind == tab.BuiltinKind &&
                (tab.BuiltinKind != BuiltinTabKind.None || t.Id == tab.Id));
            if (newItem != null)
                MyTabView.SelectedItem = newItem;
        }

        private async Task DeleteHiddenTabAsync(MyTabItem tab)
        {
            if (tab.BuiltinKind != BuiltinTabKind.None)
            {
                await ShowMessageAsync("内置标签不能删除");
                return;
            }

            if (ShouldConfirmDelete())
            {
                var shortcuts = BlockBox.Data.ShortcutRepository
                    .GetByTab(_currentMode, tab.Id);

                string warn = shortcuts.Count > 0
                    ? "\n\n" + LanguageService.TranslateFormat("该标签下有 {count} 个引用，删除后它们会从标签移除（文件本身不动）。", ("{count}", shortcuts.Count))
                    : "";

                int result = await ShowConfirmationWindowAsync(
                    "删除标签", LanguageService.TranslateFormat("确定删除「{name}」吗？", ("{name}", tab.Title)) + warn, "删除");
                if (result != 1) return;
            }

            BlockBox.Data.TabRepository.Delete(tab.Id);
        }
        // 防止两个事件互相触发造成死循环
        private bool _isSyncingSelection = false;

        // 左侧菜单选中变化
        private void NavMenuList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isReloadingCategories) return;
            if (_isDragHoveringCategory) return;
            CloseSettingsForNavigation();

            if (!string.IsNullOrEmpty(_searchKeyword))
            {
                SearchBox.Text = string.Empty;
            }
            if (_isSyncingSelection) return;

            if (NavMenuList.SelectedItem is Category c0 && c0.IsSeparator)
            {
                NavMenuList.SelectedIndex = -1;
                return;
            }

            if (NavMenuList.SelectedItem is not Category cat) return;
            _currentCategory = cat;
            CurrentCategoryChanged?.Invoke(this, EventArgs.Empty);
            if (!cat.IsSeparator && !string.IsNullOrWhiteSpace(cat.PartitionId))
                _selectedCategoryByPartition[$"{(int)_currentMode}:{cat.PartitionId}"] = cat.Id;

            _isSyncingSelection = true;
            try
            {
                MyTabView.SelectedIndex = -1;
            }
            finally
            {
                _isSyncingSelection = false;
            }

            // ⭐ 关键：清除标签选中后，主动把标签前景色刷回普通色
            RefreshSelectedTabForeground();

            UpdateSlidingIndicator(animate: false);
            _currentView = ViewKind.Category;
            _currentTabItem = null;

            BeginShortcutLoadAnimationBatch();
            LoadShortcutsFromDb();
            RefreshShortcutScrollBar();
            RefreshShortcutScrollBar();
        }

        private async void SidebarAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();

            await CreateNewCategoryAsync();
        }
        // 右键左侧菜单 → 弹菜单
        private void NavMenuList_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            // 有编辑窗口开着 → 右键什么都不弹
            if (IsEditPanelOpen) return;

            // 找点击落在哪一项上
            var source = e.OriginalSource as FrameworkElement;
            ListViewItem? clickedItem = null;
            while (source != null && source != NavMenuList)
            {
                if (source is ListViewItem lvi) { clickedItem = lvi; break; }
                source = VisualTreeHelper.GetParent(source) as FrameworkElement;
            }

            if (clickedItem?.Content is not Category cat)
            {
                return;
            }

            if (cat.IsSectionHeader && cat.SectionPartitionId != "default")
            {
                var partitionMenu = new MenuFlyout();
                var renamePartition = new MenuFlyoutItem { Text = "重命名", Icon = new FontIcon { Glyph = "\uE8AC" } };
                renamePartition.Click += async (_, _) => await RenamePartitionAsync(cat.SectionPartitionId);
                partitionMenu.Items.Add(renamePartition);

                var deletePartition = new MenuFlyoutItem { Text = "删除空分区", Icon = new FontIcon { Glyph = "\uE74D" } };
                deletePartition.Click += async (_, _) =>
                {
                    if (BlockBox.Data.CategoryRepository.GetPartitionCategoryCount(_currentMode, cat.SectionPartitionId) > 0)
                    {
                        await ShowMessageAsync("该分区中还有分类，请先删除或移动这些分类。");
                        return;
                    }
                    BlockBox.Data.CategoryRepository.DeletePartition(_currentMode, cat.SectionPartitionId);
                    LoadCategoriesFromDb();
                };
                partitionMenu.Items.Add(deletePartition);
                LanguageService.RegisterMenuFlyout(partitionMenu);
                partitionMenu.ShowAt(NavMenuList, new FlyoutShowOptions { Position = e.GetPosition(NavMenuList) });
                return;
            }

            // 默认分区标题与系统分类均不可删除。
            if (cat.IsSeparator || !cat.IsUserCreated) return;

            var flyout = new MenuFlyout();

            var renameItem = new MenuFlyoutItem
            {
                Text = "编辑",
                Icon = new FontIcon { Glyph = "\uE8AC" }
            };
            renameItem.Click += async (s, args) => await RenameCategoryAsync(cat);
            flyout.Items.Add(renameItem);

            var deleteItem = new MenuFlyoutItem
            {
                Text = "删除",
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            deleteItem.Click += async (s, args) => await DeleteCategoryAsync(cat);
            flyout.Items.Add(deleteItem);

            LanguageService.RegisterMenuFlyout(flyout);
            flyout.ShowAt(NavMenuList, new FlyoutShowOptions
            {
                Position = e.GetPosition(NavMenuList)
            });
        }

        private async Task RenamePartitionAsync(string partitionId)
        {
            var partition = BlockBox.Data.CategoryRepository.GetPartitions(_currentMode)
                .FirstOrDefault(p => p.Id == partitionId);
            if (string.IsNullOrEmpty(partition.Id)) return;

            string? input = await ShowTextInputWindowAsync("重命名", partition.Name, "分区名称");
            if (input == null) return;

            string newName = input.Trim();
            if (string.IsNullOrEmpty(newName)) return;
            if (string.Equals(newName, partition.Name, StringComparison.Ordinal)) return;
            if (BlockBox.Data.CategoryRepository.GetPartitions(_currentMode).Any(p =>
                p.Id != partitionId && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                await ShowMessageAsync("已存在同名分区");
                return;
            }

            BlockBox.Data.CategoryRepository.RenamePartition(_currentMode, partitionId, newName);
            LoadCategoriesFromDb();
        }

        private async Task DeleteCategoryAsync(Category cat)
        {
            var affected = ShortcutRepository.GetByCategory(_currentMode, cat.Id);

            if (ShouldConfirmDelete())
            {
                string impact = cat.IsShared
                    ? "\n\n" + LanguageService.Translate("⚠️ 该分类是所有模式共享的，删除后将不再显示。")
                    : "";
                int result = await ShowConfirmationWindowAsync(
                    "删除分类", LanguageService.TranslateFormat("确定删除「{name}」吗？", ("{name}", cat.DisplayName)) + impact, "删除");
                if (result != 1) return;
            }

            RecycleBinRepository.MoveByCategory(cat.Id);
            BlockBox.Data.CategoryRepository.Delete(cat.Id);
            RetireCategoryBackups(cat, affected);
            LoadCategoriesFromDb();
            LoadShortcutsFromDb();
        }

        /// <summary>
        /// 打开编辑窗口，返回关闭后的窗口实例；如果已有窗口在开，会把它激活并返回 null
        /// </summary>
        private void ShowEditPanel(EditPanelKind kind)
        {
            _hoverPreview?.Close();
            if (EditHost.Visibility == Visibility.Visible
                && EditHost.Width >= EditPanelWidth - 1) return;

            // 决定内部显示哪个 Panel
            CategoryEditPanelControl.Visibility =
                kind == EditPanelKind.Category ? Visibility.Visible : Visibility.Collapsed;
            TabEditPanelControl.Visibility =
                kind == EditPanelKind.Tab ? Visibility.Visible : Visibility.Collapsed;

            _editPanelKind = kind;

            EditHost.Visibility = Visibility.Visible;
            EditHost.Width = 0;

            // 找到当前活动的内部 RootGrid（做滑入动画用）
            FrameworkElement? activePanel = kind == EditPanelKind.Category
                ? CategoryEditPanelControl
                : TabEditPanelControl;
            if (activePanel != null)
            {
                FontSizeHelper.ApplyGlobalFontSizeToTree(activePanel);
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (activePanel.IsLoaded && activePanel.Visibility == Visibility.Visible)
                        FontSizeHelper.ApplyGlobalFontSizeToTree(activePanel);
                });
            }

            var innerGrid = FindChildByName<Grid>(activePanel, "RootGrid");

            var sb = new Storyboard();

            var widthAnim = new DoubleAnimation
            {
                From = 0,
                To = EditPanelWidth,
                Duration = TimeSpan.FromMilliseconds(EditPanelAnimMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(widthAnim, EditHost);
            Storyboard.SetTargetProperty(widthAnim, "Width");
            sb.Children.Add(widthAnim);

            EditHost.Opacity = 0;
            var opAnim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(EditPanelAnimMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(opAnim, EditHost);
            Storyboard.SetTargetProperty(opAnim, "Opacity");
            sb.Children.Add(opAnim);

            if (innerGrid != null)
            {
                if (innerGrid.RenderTransform is not TranslateTransform tt)
                {
                    tt = new TranslateTransform();
                    innerGrid.RenderTransform = tt;
                }
                tt.X = 40;

                var slideAnim = new DoubleAnimation
                {
                    From = 40,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(EditPanelAnimMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(slideAnim, tt);
                Storyboard.SetTargetProperty(slideAnim, "X");
                sb.Children.Add(slideAnim);
            }

            sb.Begin();
        }

        private void HideEditPanel()
        {
            if (EditHost.Visibility != Visibility.Visible) return;

            FrameworkElement? activePanel = _editPanelKind == EditPanelKind.Category
                ? CategoryEditPanelControl
                : TabEditPanelControl;
            var innerGrid = FindChildByName<Grid>(activePanel, "RootGrid");

            var sb = new Storyboard();

            var widthAnim = new DoubleAnimation
            {
                From = EditHost.Width,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(EditPanelAnimMs - 40),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(widthAnim, EditHost);
            Storyboard.SetTargetProperty(widthAnim, "Width");
            sb.Children.Add(widthAnim);

            if (innerGrid != null && innerGrid.RenderTransform is TranslateTransform tt)
            {
                var slideAnim = new DoubleAnimation
                {
                    From = 0,
                    To = 40,
                    Duration = TimeSpan.FromMilliseconds(EditPanelAnimMs - 40),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                Storyboard.SetTarget(slideAnim, tt);
                Storyboard.SetTargetProperty(slideAnim, "X");
                sb.Children.Add(slideAnim);
            }

            sb.Completed += (s, e) =>
            {
                EditHost.Visibility = Visibility.Collapsed;
                EditHost.Width = EditPanelWidth;
                _editPanelKind = EditPanelKind.None;
            };

            sb.Begin();
        }

        private bool IsEditPanelOpen => EditHost.Visibility == Visibility.Visible;

        /// <summary>
        /// 打开内嵌编辑面板，等用户提交结果
        /// </summary>
        private Task<CategoryEditResult?> ShowCategoryEditPanelAsync(Category? editing)
        {
            var tcs = new TaskCompletionSource<CategoryEditResult?>();
            var partitions = BlockBox.Data.CategoryRepository.GetPartitions(_currentMode)
                .Select(p => (p.Id, p.Name)).ToList();

            if (editing == null)
                CategoryEditPanelControl.ConfigureAsNew(partitions, partitions.LastOrDefault().Id ?? "");
            else
                CategoryEditPanelControl.ConfigureAsEdit(editing, partitions);

            EventHandler<CategoryEditResult>? handler = null;
            handler = (s, r) =>
            {
                CategoryEditPanelControl.Completed -= handler;
                HideEditPanel();
                tcs.SetResult(r);
            };
            CategoryEditPanelControl.Completed += handler;

            ShowEditPanel(EditPanelKind.Category);   // ⭐ 改这里
            return tcs.Task;
        }
        private Task<TabEditResult?> ShowTabEditPanelAsync(MyTabItem? editing)
        {
            var tcs = new TaskCompletionSource<TabEditResult?>();

            if (editing == null)
                TabEditPanelControl.ConfigureAsNew();
            else
                TabEditPanelControl.ConfigureAsEdit(editing);

            EventHandler<TabEditResult>? handler = null;
            handler = (s, r) =>
            {
                TabEditPanelControl.Completed -= handler;
                HideEditPanel();
                tcs.SetResult(r);
            };
            TabEditPanelControl.Completed += handler;

            ShowEditPanel(EditPanelKind.Tab);
            return tcs.Task;
        }

        // 弹出"新建分类"对话框并写库
        private async Task CreateNewCategoryAsync()
        {
            await EnsureEditPanelClosedAsync();   // ⭐ 先确保关闭

            var result = await ShowCategoryEditPanelAsync(editing: null);

            if (result == null || !result.IsConfirmed) return;

            if (Categories.Any(c => !c.IsSeparator && c.Name == result.Name))
            {
                await ShowMessageAsync("已存在同名分类");
                return;
            }
            if (string.IsNullOrWhiteSpace(result.PartitionId)
                && BlockBox.Data.CategoryRepository.GetPartitions(_currentMode).Any(p => string.Equals(p.Name, result.PartitionName, StringComparison.OrdinalIgnoreCase)))
            {
                await ShowMessageAsync("已存在同名分区");
                return;
            }

            string partitionId = result.PartitionId;
            if (string.IsNullOrEmpty(partitionId))
                partitionId = BlockBox.Data.CategoryRepository.AddPartition(_currentMode, result.PartitionName);
            var category = new Category
            {
                Name = result.Name,
                IconGlyph = result.Icon,
                IsUserCreated = true,
                BelongingMode = _currentMode,
                GroupIndex = result.GroupIndex,
                PartitionId = partitionId,
                PartitionName = result.PartitionName,
                ContentKind = result.Kind,
                ExtensionList = result.ExtList
            };

            BlockBox.Data.CategoryRepository.Add(category);
            LoadCategoriesFromDb();
        }
        private async Task RenameCategoryAsync(Category cat)
        {
            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();

            if (IsEditPanelOpen) return;

            var result = await ShowCategoryEditPanelAsync(editing: cat);

            if (result == null || !result.IsConfirmed) return;

            if (string.IsNullOrWhiteSpace(result.PartitionId)
                && BlockBox.Data.CategoryRepository.GetPartitions(_currentMode).Any(p => string.Equals(p.Name, result.PartitionName, StringComparison.OrdinalIgnoreCase)))
            {
                await ShowMessageAsync("已存在同名分区");
                return;
            }

            bool kindChanged = result.Kind != cat.ContentKind
                            || result.ExtList != cat.ExtensionList;

            if (kindChanged)
            {
                var shortcuts = BlockBox.Data.ShortcutRepository.GetByCategory(_currentMode, cat.Id);
                if (shortcuts.Count > 0)
                {
                    string preview = string.Join("\n",
                        shortcuts.Take(8).Select(s => "• " + s.Name));
                    if (shortcuts.Count > 8)
                        preview += "\n" + LanguageService.TranslateFormat("... 共 {count} 项", ("{count}", shortcuts.Count));

                    int confirm = await ShowConfirmationWindowAsync(
                        "更换存放类型",
                        LanguageService.TranslateFormat("更改类型后，该分类下已有的 {count} 项将被清空（快捷方式记录与备份）：\n\n{preview}\n\n是否继续？", ("{count}", shortcuts.Count), ("{preview}", preview)),
                        "清空并更换");
                    if (confirm != 1)
                        return;

                    BlockBox.Data.ShortcutRepository.DeleteByCategory(cat.Id);
                    RetireCategoryBackups(cat, shortcuts);
                }
            }

            string targetPartitionId = result.PartitionId;
            if (string.IsNullOrEmpty(targetPartitionId))
                targetPartitionId = BlockBox.Data.CategoryRepository.AddPartition(_currentMode, result.PartitionName);
            if (result.Name == cat.Name
                && result.Icon == cat.IconGlyph
                && result.GroupIndex == cat.GroupIndex
                && targetPartitionId == cat.PartitionId
                && result.Kind == cat.ContentKind
                && result.ExtList == cat.ExtensionList) return;

            BlockBox.Data.CategoryRepository.Update(
     cat.Id, result.Name, result.Icon, result.GroupIndex, targetPartitionId,
     result.Kind, result.ExtList, cat.SortMode);

            LoadCategoriesFromDb();
            if (kindChanged) LoadShortcutsFromDb();
        }

        // 通用提示对话框

        // ==================== 通知开关 ====================

        /// <summary>是否允许显示"操作成功"类提示</summary>
        private bool ShouldShowSuccessNotification()
            => _ini.ReadBool("Notification", "ShowSuccess", true);

        /// <summary>是否允许显示"重载配置完成"提示</summary>
        private bool ShouldShowReloadNotification()
            => _ini.ReadBool("Notification", "ShowReloadCompleted", true);

        /// <summary>
        /// 显示纯成功提示（受开关控制）
        /// </summary>
        private async Task ShowSuccessMessageAsync(string message)
        {
            if (!ShouldShowSuccessNotification())
                return;
            await ShowMessageAsync(message);
        }

        /// <summary>
        /// 显示混合结果：有警告/失败时始终显示；纯成功时受开关控制
        /// </summary>
        private async Task ShowResultMessageAsync(string message, bool hasWarnings)
        {
            if (!hasWarnings && !ShouldShowSuccessNotification())
                return;
            await ShowMessageAsync(message);
        }
        // ==================== Toast 通知 ====================

        private CancellationTokenSource? _toastCts;

        /// <summary>
        /// 左下角非模态提示；2 秒后自动淡出。
        /// 连续调用时，新提示顶掉旧提示。
        /// </summary>
        private async Task ShowToastAsync(string message, int? durationMs = null)
        {
            if (ToastHost == null || ToastBorder == null || ToastText == null)
                return;

            // 消息越长停留越久（2s ~ 5s）
            int duration = durationMs
                ?? Math.Clamp(1800 + (message?.Length ?? 0) * 40, 2000, 5000);

            // 取消上一个 Toast（如果还在显示 / 等待中）
            _toastCts?.Cancel();
            var cts = new CancellationTokenSource();
            _toastCts = cts;

            // ---- 应用当前主题色 ----
            ApplyToastColors();

            // ---- 内容 ----
            ToastText.Text = LanguageService.Translate(message);

            // ---- 复位到起始态并显示 ----
            ToastBorder.Opacity = 0;
            if (ToastBorder.RenderTransform is TranslateTransform tt)
                tt.Y = 20;
            ToastHost.Visibility = Visibility.Visible;

            // ---- 入场：淡入 + 上滑 ----
            var enterSb = new Storyboard();
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, ToastBorder);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            enterSb.Children.Add(fadeIn);

            if (ToastBorder.RenderTransform is TranslateTransform enterTt)
            {
                var slideIn = new DoubleAnimation
                {
                    From = 20,
                    To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(slideIn, enterTt);
                Storyboard.SetTargetProperty(slideIn, "Y");
                enterSb.Children.Add(slideIn);
            }
            enterSb.Begin();

            // ---- 停留 ----
            try
            {
                await Task.Delay(duration, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 被新的 Toast 顶掉：直接退出，交给新 Toast 接管 UI
                return;
            }

            // ---- 出场：淡出 + 下滑 ----
            var exitSb = new Storyboard();
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fadeOut, ToastBorder);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");
            exitSb.Children.Add(fadeOut);

            if (ToastBorder.RenderTransform is TranslateTransform exitTt)
            {
                var slideOut = new DoubleAnimation
                {
                    From = 0,
                    To = 12,
                    Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                Storyboard.SetTarget(slideOut, exitTt);
                Storyboard.SetTargetProperty(slideOut, "Y");
                exitSb.Children.Add(slideOut);
            }

            exitSb.Completed += (_, _) =>
            {
                // 只有当自己仍然是"当前 Toast"时才收起来
                if (!cts.IsCancellationRequested && ReferenceEquals(_toastCts, cts))
                {
                    ToastHost.Visibility = Visibility.Collapsed;
                    _toastCts = null;
                }
            };
            exitSb.Begin();
        }

        /// <summary>
        /// 用当前主题色刷新 Toast 的外观（背景、边框、文字）
        /// </summary>
        private void ApplyToastColors()
        {
            if (ToastBorder == null || ToastText == null)
                return;

            // 背景：跟随主内容区背景
            var bgColor = GetMainContentAreaBackgroundColor();
            ToastBorder.Background = new SolidColorBrush(bgColor);

            // 边框：比背景浅一点点，跟 Flyout 保持一致风格
            ToastBorder.BorderBrush = new SolidColorBrush(ApplyWhiteOverlay(bgColor, 0.15));

            // 文字：按 Toast 自身背景校正用户颜色，不能复用全局“浅色卡片”文字色。
            var (globalFontColor, globalAdjust) = ReadFontColorSetting(
                "PrimaryFontColor",
                "PrimaryFontColorAuto",
                SettingsDefaults.PrimaryFontColor,
                whiteMeansAuto: true);
            ToastText.Foreground = new SolidColorBrush(
                ResolveTextColor(bgColor, globalFontColor, globalAdjust));
        }

        /// <summary>
        /// 标题栏的系统按钮（最小化/最大化/关闭）也跟随当前字体色，
        /// 自动模式下则跟随标题栏背景的明暗。
        /// </summary>
        private void ApplyTitleBarTextColors(Color textColor)
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonForegroundColor = textColor;
            titleBar.ButtonInactiveForegroundColor =
                Color.FromArgb(128, textColor.R, textColor.G, textColor.B);
            titleBar.ButtonHoverForegroundColor = textColor;
            titleBar.ButtonPressedForegroundColor = textColor;
            titleBar.ButtonHoverBackgroundColor =
                Color.FromArgb(32, textColor.R, textColor.G, textColor.B);
            titleBar.ButtonPressedBackgroundColor =
                Color.FromArgb(48, textColor.R, textColor.G, textColor.B);
        }

        /// <summary>
        /// 所有提示均以非阻塞 Toast 显示；调用方不等待 Toast 自动消失。
        /// </summary>
        private Task ShowMessageAsync(string message)
        {
            _ = ShowToastAsync(message);
            return Task.CompletedTask;
        }

        private void UpdateSeparatorWidths()
        {
            double w = NavMenuList.ActualWidth;
            if (w <= 0) return;

            double lineWidth = Math.Max(0, w - 24);  // 左右各留 12px 白边

            for (int i = 0; i < NavMenuList.Items.Count; i++)
            {
                if (NavMenuList.Items[i] is not Category c || !c.IsSeparator) continue;
                if (NavMenuList.ContainerFromIndex(i) is not ListViewItem lvi) continue;

                var line = FindChildByName<Border>(lvi, "SeparatorLine");
                if (line != null)
                {
                    line.Width = lineWidth;
                }
            }
        }
        // TabView 选中变化
        private void MyTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isTabDragReordering) return;

            CloseSettingsForNavigation();

            if (!string.IsNullOrEmpty(_searchKeyword))
            {
                SearchBox.Text = string.Empty;   // 会触发 TextChanged 自动清空 _searchKeyword
            }
            _hoverPreview?.Close();
            if (_isSyncingSelection) return;

            // ⚠️ 无选中项时，也要清理指示器
            if (MyTabView.SelectedItem == null)
            {
                UpdateSlidingIndicator(animate: false);
                RefreshSelectedTabForeground();
                return;
            }

            _isSyncingSelection = true;
            try
            {
                NavMenuList.SelectedIndex = -1;
            }
            finally
            {
                _isSyncingSelection = false;
            }

            // ⭐ 新增：切到标签视图
            _currentView = ViewKind.Tab;
            _currentTabItem = MyTabView.SelectedItem as MyTabItem;
            if (_currentTabItem != null && _currentTabItem.BuiltinKind == BuiltinTabKind.None)
                _ini.WriteValue("System", "LastTabId", _currentTabItem.Id);
            RefreshSelectedTabForeground();

            UpdateSlidingIndicator(animate: true);
            if (MyTabView.ContainerFromItem(MyTabView.SelectedItem) is not TabViewItem selectedContainer
                || selectedContainer.ActualWidth <= 0)
            {
                ScheduleSelectedTabVisualRefresh();
            }
            LoadShortcutsFromDb();   // ⭐ 新增
            RefreshShortcutScrollBar();
        }
        private bool _hasActiveTabSelection = false;
        private Storyboard? _tabSelectionStoryboard;
        private void UpdateSlidingIndicator(bool animate)
        {
            _tabSelectionStoryboard?.Stop();
            _tabSelectionStoryboard = null;
            SlidingIndicator.Fill = new SolidColorBrush(GetMainContentAreaBackgroundColor());

            if (MyTabView.SelectedItem == null)
            {
                SlidingIndicator.Visibility = Visibility.Collapsed;
                HideAllSelectedVisuals();

                // ⚠️ 标记：当前无选中
                _hasActiveTabSelection = false;
                return;
            }

            var container = MyTabView.ContainerFromItem(MyTabView.SelectedItem) as TabViewItem;
            if (container == null || container.ActualWidth <= 0)
            {
                // 容器仍在生成时由 SelectionChanged、集合变更或 Loaded 事件安排一次刷新。
                return;
            }

            // ⚠️ 关键：如果之前没有选中标签（比如刚从左侧菜单点过来），强制不走动画
            bool allowAnimate = animate && _hasActiveTabSelection;

            double targetWidth = container.ActualWidth;

            var transform = container.TransformToVisual(TabIndicatorCanvas);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            double targetLeft = point.X - 18;

            if (SlidingIndicator.RenderTransform is not TranslateTransform)
            {
                SlidingIndicator.RenderTransform = new TranslateTransform();
            }
            var translate = (TranslateTransform)SlidingIndicator.RenderTransform;

            if (allowAnimate)
            {
                // ==================== 动画流程 ====================
                HideAllSelectedVisuals();
                FadeOutHoverVisuals(100);      // ⭐ 滑动前淡出 hover

                string data = GenerateTabPath(targetWidth);
                SlidingIndicator.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
                SlidingIndicator.Width = targetWidth + 36;
                SlidingIndicator.Visibility = Visibility.Visible;

                var sb = new Storyboard();

                var posAnim = new DoubleAnimation
                {
                    To = targetLeft,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(posAnim, translate);
                Storyboard.SetTargetProperty(posAnim, "X");
                sb.Children.Add(posAnim);

                var widthAnim = new DoubleAnimation
                {
                    To = targetWidth + 36,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(widthAnim, SlidingIndicator);
                Storyboard.SetTargetProperty(widthAnim, "Width");
                sb.Children.Add(widthAnim);

                sb.Completed += (s, e) =>
                {
                    if (ReferenceEquals(_tabSelectionStoryboard, sb))
                        _tabSelectionStoryboard = null;

                    SlidingIndicator.Visibility = Visibility.Collapsed;
                    if (ReferenceEquals(MyTabView.SelectedItem, container.DataContext))
                        RefreshSelectedTabVisualIfCurrent(container);
                    else
                        ScheduleSelectedTabVisualRefresh();
                    FadeInHoverVisuals(150);      // ⭐ 滑动后淡入 hover
                };

                _tabSelectionStoryboard = sb;
                sb.Begin();
            }
            else
            {
                // ==================== 静止流程 ====================
                SlidingIndicator.Visibility = Visibility.Collapsed;
                HideAllSelectedVisuals();
                ShowSelectedVisual(container);

                // ⚠️ 同步更新 TranslateTransform，避免下次动画从旧位置滑过来
                translate.X = targetLeft;
            }

            // ⚠️ 标记：当前有选中
            _hasActiveTabSelection = true;
        }

        // 隐藏所有 tab 的 SelectedVisual
        private void HideAllSelectedVisuals()
        {
            foreach (var item in TabItems)
            {
                var container = MyTabView.ContainerFromItem(item) as TabViewItem;
                if (container == null) continue;

                var path = FindChildByName<Microsoft.UI.Xaml.Shapes.Path>(container, "SelectedVisual");
                if (path != null)
                    path.Visibility = Visibility.Collapsed;
            }
        }

        // 显示指定 tab 的 SelectedVisual
        private void ShowSelectedVisual(TabViewItem container)
        {
            var path = FindChildByName<Microsoft.UI.Xaml.Shapes.Path>(container, "SelectedVisual");
            if (path == null) return;

            double width = container.ActualWidth;
            if (width <= 0) return;

            // 确保形状、宽度、颜色正确
            string data = GenerateTabPath(width);
            path.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
            path.Width = width + 36;

            path.Fill = new SolidColorBrush(GetMainContentAreaBackgroundColor());

            // 选中背景的视觉优先级高于悬停层；同时隐藏可能残留的悬停锁定层。
            var hoverPath = FindChildByName<Microsoft.UI.Xaml.Shapes.Path>(container, "HoverVisual");
            if (hoverPath != null)
                hoverPath.Visibility = Visibility.Collapsed;
            var hoverLockPath = FindChildByName<Microsoft.UI.Xaml.Shapes.Path>(container, "HoverLockVisual");
            if (hoverLockPath != null)
                hoverLockPath.Visibility = Visibility.Collapsed;

            path.Visibility = Visibility.Visible;
        }
        private void LoadShortcutsCore()
        {
            _selectedShortcuts.Clear();
            ShortcutGrid.ItemsSource = null;

            foreach (var s in CurrentShortcuts) s.IsDragging = false;
            CurrentShortcuts.Clear();


            try
            {
                // ================= 原有填充逻辑开始 =================
                if (!string.IsNullOrEmpty(_searchKeyword))
                {
                    var hits = BlockBox.Data.ShortcutRepository.Search(_currentMode, _searchKeyword);
                    foreach (var s in hits)
                        CurrentShortcuts.Add(s);

                    System.Diagnostics.Debug.WriteLine(
                        $"[搜索] 「{_searchKeyword}」: {hits.Count} 项");
                    return;
                }

                if (_currentView == ViewKind.Tab && _currentTabItem != null)
                {
                    List<ShortcutItem> list = _currentTabItem.BuiltinKind switch
                    {
                        BuiltinTabKind.Favorites => ShortcutRepository.GetFavorites(_currentMode),
                        BuiltinTabKind.Recent => ShortcutRepository.GetRecent(_currentMode),
                        _ => ShortcutRepository.GetByTab(_currentMode, _currentTabItem.Id, _currentTabItem.SortMode)
                    };
                    if (_currentTabItem.BuiltinKind == BuiltinTabKind.None)
                    {
                        list = list.Where(s =>
                            ContentKindHelper.Matches(s.TargetPath,
                                _currentTabItem.ContentKind, _currentTabItem.ExtensionList))
                            .ToList();
                    }
                    foreach (var s in list)
                        CurrentShortcuts.Add(s);

                    System.Diagnostics.Debug.WriteLine(
                        $"[视图] 标签「{_currentTabItem.Title}」: {list.Count} 项");
                    return;
                }

                if (NavMenuList.SelectedItem is not Category cat || cat.IsSeparator)
                    return;

                var raw = BlockBox.Data.ShortcutRepository.GetByCategory(
                    _currentMode, cat.Id, cat.SortMode);
                var filtered = raw.Where(s =>
                    BlockBox.Helpers.ContentKindHelper.Matches(
                        s.TargetPath, cat.ContentKind, cat.ExtensionList))
                    .ToList();

                foreach (var s in filtered)
                    CurrentShortcuts.Add(s);

                System.Diagnostics.Debug.WriteLine(
                    $"[视图] 分类「{cat.Name}」: 原始 {raw.Count} 项，过滤后 {filtered.Count} 项");
                // ================= 原有填充逻辑结束 =================
            }
            finally
            {
                // 场景切换会重建 ItemsSource 并回到顶部；拖拽排序不会调用此加载路径。
                DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () =>
                    {
                        ShortcutGrid.ItemsSource = CurrentShortcuts;
                        ShortcutScrollViewer?.ChangeView(
                            horizontalOffset: null,
                            verticalOffset: 0,
                            zoomFactor: null,
                            disableAnimation: true);
                    });
            }
        }
        private void UpdateEmptyHint()
        {
            if (CurrentShortcuts.Count > 0)
            {
                SetEmptyHintVisibility(false);   // ⚠️ 改这里
                return;
            }

            SetEmptyHintVisibility(true);

            // 搜索中
            if (!string.IsNullOrEmpty(_searchKeyword))
            {
                EmptyHintText.Text = LanguageService.TranslateFormat("没有找到匹配「{keyword}」的内容", ("{keyword}", _searchKeyword));
                return;
            }

            // 标签视图
            if (_currentView == ViewKind.Tab && _currentTabItem != null)
            {
                EmptyHintText.Text = _currentTabItem.BuiltinKind switch
                {
                    BuiltinTabKind.Favorites => LanguageService.Translate("还没有收藏任何内容"),
                    BuiltinTabKind.Recent => LanguageService.Translate("还没有访问记录"),
                    _ => LanguageService.TranslateFormat("「{title}」下还没有内容", ("{title}", _currentTabItem.Title))
                };
                return;
            }

            // 分类视图
            if (NavMenuList.SelectedItem is Category cat && !cat.IsSeparator)
            {
                EmptyHintText.Text = cat.IsShared
                    ? LanguageService.TranslateFormat("共享分类「{category}」中还没有内容\n点击上方 + 添加文件", ("{category}", cat.DisplayName))
                    : LanguageService.TranslateFormat("模式分类「{category}」中还没有内容\n点击上方 + 添加文件", ("{category}", cat.DisplayName));
                return;
            }

            EmptyHintText.Text = LanguageService.Translate("选择左侧分类或顶部标签查看内容");
        }

        private async void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // 1. 如果设置面板开着 → 关掉
            if (_isSettingsVisible)
            {
                await CloseSettingsWithAnimationAsync(restoreEmptyHint: false);
                if (_isSettingsVisible) return;
            }

            // 2. 如果编辑面板开着 → 也顺手关掉（可选，看你需求）
            if (IsEditPanelOpen)
            {
                await EnsureEditPanelClosedAsync();
            }

            // 3. 触发一次搜索
            //    _searchKeyword 目前是空的，直接走一遍 TextChanged 会啥也没发生
            //    所以先把 SearchBox 的当前文本同步到 _searchKeyword，再调 Load
            //_searchKeyword = SearchBox.Text?.Trim() ?? "";
            //LoadShortcutsFromDb();
        }

        // ==================== 右键菜单辅助 ====================

        private void CopyOrCutShortcuts(List<ShortcutItem> items, bool isCut)
        {
            if (items.Count == 0) return;

            // 写 Windows 剪贴板（CF_HDROP）
            var paths = items
                .Select(s => ResolveLaunchPath(s))
                .Where(p => !string.IsNullOrEmpty(p) && System.IO.File.Exists(p))
                .ToList();

            if (paths.Count > 0)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                ClipboardHelper.SetFileDropList(hwnd, paths);
            }

            // ⭐ 记录序列号（写完后读）
            _lastClipboardSeq = ClipboardHelper.GetClipboardSequenceNumberSafe();

            // 内部记录
            _internalClipboard = items.ToList();
            _internalClipboardIsCut = isCut;

            System.Diagnostics.Debug.WriteLine(
                $"[剪贴板] {(isCut ? "剪切" : "复制")} {items.Count} 项，seq={_lastClipboardSeq}");
        }

        /// <summary>
        /// 应用内粘贴：把内部剪贴板的项粘到当前分类
        /// </summary>
        private async Task PasteInternalAsync()
        {
            if (_internalClipboard.Count == 0) return;

            // 目标分类
            if (NavMenuList.SelectedItem is not Category cat || cat.IsSeparator)
            {
                await ShowMessageAsync("请先选择一个分类");
                return;
            }
            var targetMode = cat.BelongingMode ?? _currentMode;
            if (_internalClipboardIsCut && _internalClipboard.Any(src =>
                    src.IsShared != cat.IsShared || (!src.IsShared && src.BelongingMode != targetMode)))
            {
                await ShowMessageAsync("移动只能在同一范围内进行；跨范围请使用“添加到分类”复制。");
                return;
            }
            if (!_internalClipboardIsCut && cat.IsShared && _internalClipboard.Any(src => !src.IsShared))
            {
                await ShowMessageAsync("模式记录不能复制到共享分类。");
                return;
            }

            var existingPaths = BlockBox.Data.ShortcutRepository.GetByCategory(targetMode, cat.Id)
                .Select(s => s.TargetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int added = 0, rejected = 0, duplicated = 0;
            var copiedItems = new List<ShortcutItem>();
            bool wasCut = _internalClipboardIsCut;
            var movedItems = new List<ShortcutItem>();

            foreach (var src in _internalClipboard)
            {
                // 重复检查
                if (existingPaths.Contains(src.TargetPath)) { duplicated++; continue; }

                if (_internalClipboardIsCut)
                {
                    try
                    {
                        BlockBox.Data.ShortcutRepository.MoveToCategory(src.Id, cat.Id,
                            BlockBox.Data.ShortcutRepository.GetNextSortOrder(cat.Id, cat.IsShared));
                        existingPaths.Add(src.TargetPath);
                        movedItems.Add(src);
                        added++;
                    }
                    catch { rejected++; }
                    continue;
                }

                // 类型校验
                if (!BlockBox.Helpers.ContentKindHelper.Matches(
                        src.TargetPath, cat.ContentKind, cat.ExtensionList))
                {
                    rejected++;
                    continue;
                }

                // 复制记录
                var newItem = new ShortcutItem
                {
                    Name = src.Name,
                    TargetPath = src.TargetPath,
                    Arguments = src.Arguments,
                    WorkingDirectory = src.WorkingDirectory,
                    IconPath = src.IconPath,
                    CategoryId = cat.Id,
                    TitleId = "", // 标签属于模式；复制时不沿用源标签
                    IsFile = src.IsFile,
                    IsShared = cat.IsShared,
                    BelongingMode = targetMode,
                    SortOrder = BlockBox.Data.ShortcutRepository.GetNextSortOrder(cat.Id, cat.IsShared),
                    BackupPath = src.BackupPath,
                    SourceMd5 = src.SourceMd5,
                    SourceState = src.SourceState,
                    LastVerifiedTime = src.LastVerifiedTime,
                    IsBackedUp = src.IsBackedUp,
                    CreatedTime = DateTime.Now
                };

                try
                {
                    BlockBox.Data.ShortcutRepository.Add(newItem);
                    copiedItems.Add(newItem);
                    existingPaths.Add(src.TargetPath);
                    added++;
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex)
                    when (ex.SqliteErrorCode == 19)
                {
                    duplicated++;
                }
            }

            foreach (var item in wasCut ? movedItems : copiedItems)
                await RememberProgramCategoryAsync(item, cat, wasCut);

            // 成功剪切移动后清空内部剪贴板
            if (wasCut && added > 0)
            {
                _internalClipboard.Clear();
                _internalClipboardIsCut = false;
            }

            if (wasCut)
            {
                foreach (var item in movedItems) CurrentShortcuts.Remove(item);
                if (_currentView == ViewKind.Category && NavMenuList.SelectedItem is Category target && target.Id == cat.Id)
                {
                    foreach (var item in movedItems) { item.CategoryId = cat.Id; CurrentShortcuts.Add(item); }
                }
            }
            else if (_currentView == ViewKind.Category && NavMenuList.SelectedItem is Category current && current.Id == cat.Id)
                foreach (var item in copiedItems) CurrentShortcuts.Add(item);
            UpdateEmptyHint();

            var msgs = new List<string>();
            if (added > 0) msgs.Add(LanguageService.TranslateFormat("已粘贴 {count} 个", ("{count}", added)));
            if (duplicated > 0) msgs.Add(LanguageService.TranslateFormat("{count} 个已存在", ("{count}", duplicated)));
            if (rejected > 0) msgs.Add(LanguageService.TranslateFormat("{count} 个类型不匹配", ("{count}", rejected)));

            if (msgs.Count > 0)
            {
                // ⚠️ 有"已存在/类型不匹配"时强制显示，纯成功时交给开关
                await ShowResultMessageAsync(
                    string.Join(LanguageService.Translate("，"), msgs) + LanguageService.Translate("。"),
                    hasWarnings: duplicated > 0 || rejected > 0);
            }
        }

        private async Task LaunchShortcutAsAdministratorAsync(ShortcutItem s)
        {
            if (!TryResolveShortcutLaunch(s, out string path, out bool isShellShortcut, out string error))
            {
                await ShowMessageAsync(error);
                return;
            }

            try
            {
                if (Path.GetExtension(path).Equals(".Ink", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"已阻止直接启动异常快捷方式路径：{path}");

                string workingDirectory = isShellShortcut
                    ? ""
                    : string.IsNullOrWhiteSpace(s.WorkingDirectory)
                        ? Path.GetDirectoryName(path) ?? ""
                        : s.WorkingDirectory;
                if (isShellShortcut)
                {
                    string linkPathLiteral = "'" + path.Replace("'", "''") + "'";
                    string diagnosticPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "BlockBox", "admin-launch.log");
                    string logPathLiteral = "'" + diagnosticPath.Replace("'", "''") + "'";
                    string script = "$ErrorActionPreference='Stop';"
                        + "$log=" + logPathLiteral + ";"
                        + "New-Item -ItemType Directory -Path (Split-Path -Parent $log) -Force | Out-Null;"
                        + "try{$ws=New-Object -ComObject WScript.Shell;"
                        + "$lnk=$ws.CreateShortcut(" + linkPathLiteral + ");"
                        + "$target=$lnk.TargetPath;$targetArgs=$lnk.Arguments;$wd=$lnk.WorkingDirectory;"
                        + "Add-Content -LiteralPath $log -Value ('[{0}] Target={1}; Arguments={2}; WorkingDirectory={3}' -f (Get-Date -Format o),$target,$targetArgs,$wd);"
                        + "if(-not $target){throw '快捷方式目标为空'};"
                        + "$start=@{FilePath=$target;PassThru=$true;Verb='RunAs'};if(-not [string]::IsNullOrWhiteSpace($targetArgs)){$start.ArgumentList=$targetArgs};if($wd){$start.WorkingDirectory=$wd};"
                        + "$proc=Start-Process @start;"
                        + "Add-Content -LiteralPath $log -Value ('Elevation requested for target; PID={0}' -f $proc.Id)"
                        + "}catch{Add-Content -LiteralPath $log -Value ('[{0}] ERROR: {1}' -f (Get-Date -Format o),($_ | Out-String));exit 1}";
                    string encodedCommand = Convert.ToBase64String(
                        System.Text.Encoding.Unicode.GetBytes(script));
                    string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    string systemFolder = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                        ? "Sysnative"
                        : "System32";
                    string powershellPath = Path.Combine(
                        systemDirectory, systemFolder, "WindowsPowerShell", "v1.0", "powershell.exe");

                    System.Diagnostics.Debug.WriteLine(
                        $"[启动] 启动普通权限 PowerShell broker，并请求提升目标程序; Name={s.Name}; Shortcut={path}; PowerShell={powershellPath}; Diagnostic={diagnosticPath}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = powershellPath,
                        Arguments = $"-NoProfile -EncodedCommand {encodedCommand}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[启动] 管理员启动; Name={s.Name}; FileName={path}; Arguments={s.Arguments ?? ""}; WorkingDirectory={workingDirectory}; ShellShortcut=False; Verb=runas");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = s.Arguments ?? "",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                System.Diagnostics.Debug.WriteLine($"[启动] 管理员启动被用户取消; Name={s.Name}; ErrorCode={ex.NativeErrorCode}; {ex}");
                await ShowMessageAsync("已取消管理员启动");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[启动] 管理员启动异常; Name={s.Name}; ErrorCode={(ex as System.ComponentModel.Win32Exception)?.NativeErrorCode}; HResult=0x{ex.HResult:X8}; {ex}");
                await ShowMessageAsync(LanguageService.TranslateFormat("管理员启动失败：{error}", ("{error}", ex.Message)));
            }
        }

        public Task LaunchSearchResultAsAdministratorAsync(string path)
        {
            var shortcut = new ShortcutItem
            {
                Name = Path.GetFileNameWithoutExtension(path),
                TargetPath = path,
                IsFile = false,
                BelongingMode = _currentMode
            };
            return LaunchShortcutAsAdministratorAsync(shortcut);
        }

        private async Task RenameShortcutAsync(ShortcutItem s)
        {
            string? input = await ShowTextInputWindowAsync("重命名", s.Name, "快捷方式名称");
            if (input == null) return;

            string newName = input.Trim();
            if (string.IsNullOrEmpty(newName) || newName == s.Name) return;

            BlockBox.Data.ShortcutRepository.UpdateName(s.Id, newName);
            s.Name = newName;
            RefreshAllShortcutNames();
        }

        private void RevealInExplorer(ShortcutItem s)
        {
            string path = ResolveLaunchPath(s);
            if (string.IsNullOrEmpty(path)) return;

            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }

        private void JumpToCategory(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return;

            var target = Categories.FirstOrDefault(c => !c.IsSeparator && c.Id == categoryId);
            if (target == null)
            {
                _ = ShowMessageAsync("该分类不在当前模式下");
                return;
            }

            NavMenuList.SelectedItem = target;
        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchKeyword = SearchBox.Text?.Trim() ?? "";

            LoadShortcutsFromDb();
        }
        public void RefreshAfterGlobalSearchAdd(Category category)
        {
            if (_currentCategory?.Id == category.Id) LoadShortcutsFromDb();
        }

        /// <summary>从全局搜索添加进分类后是否取消结果列表的勾选（设置页“启动 / 系统”）。</summary>
        public bool ClearSearchSelectionAfterAdd =>
            _ini.ReadBool("System", "ClearSearchSelectionAfterAdd", SettingsDefaults.ClearSearchSelectionAfterAdd);
        private void LoadShortcutsFromDb()
        {
            string? pendingAction = _ini.ReadValue("System", "LastAction", "");
            if (string.Equals(pendingAction, "ClearIconCachePending", StringComparison.OrdinalIgnoreCase))
            {
                ShellIconHelper.ClearMemoryCache();
                _thumbnailImageCache.Clear();
                _ini.WriteValue("System", "LastAction", "");
            }

            LoadShortcutsCore();
            RefreshShortcutScrollBar();
            if (CurrentShortcuts.Count == 0)
            {
                _isShortcutLoadAnimationBatchActive = false;
                _shortcutLoadAnimationBatchUntil = DateTimeOffset.MinValue;
                SetShortcutLoadInteractionLocked(false);
            }

            if (_isShortcutLoadAnimationBatchActive)
            {
                DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    TryAnimateVisibleShortcuts);
                //DispatcherQueue.TryEnqueue(
                //    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                //    TryAnimateVisibleShortcuts);
            }

            UpdateEmptyHint();
            UpdateToolbarButtonStates();

        }

        private void UpdateToolbarButtonStates()
        {
            bool canUseToolbar = !_isSettingsVisible;
            bool canManuallyAddFiles = !IsManualFileAddingDisabled();
            bool hasSelection = _selectedShortcuts.Count > 0;
            bool singleSelection = _selectedShortcuts.Count == 1;
            bool allSelectedAreFavorite = hasSelection && _selectedShortcuts.All(shortcut =>
                _currentView == ViewKind.Tab && _currentTabItem?.BuiltinKind == BuiltinTabKind.Favorites
                    || shortcut.IsFavorite);

            AddFileButton.IsEnabled = canUseToolbar && canManuallyAddFiles;
            ToolbarPasteButton.IsEnabled = canUseToolbar && canManuallyAddFiles;
            EditAddMenuItem.IsEnabled = canManuallyAddFiles;
            EditPasteMenuItem.IsEnabled = canManuallyAddFiles;
            ToolbarCutButton.IsEnabled = canUseToolbar && hasSelection;
            ToolbarCopyButton.IsEnabled = canUseToolbar && hasSelection;
            ToolbarRenameButton.IsEnabled = canUseToolbar && singleSelection;
            ToolbarDeleteButton.IsEnabled = canUseToolbar && hasSelection;
            ToolbarFavoriteButton.IsEnabled = canUseToolbar && singleSelection;
            ToolbarFavoriteIcon.Glyph = allSelectedAreFavorite ? "\uE735" : "\uE734";
        }

        private bool IsManualFileAddingDisabled()
            => _currentView == ViewKind.Tab
                && _currentTabItem?.BuiltinKind is BuiltinTabKind.Favorites or BuiltinTabKind.Recent;

        private ShortcutItem? GetShortcutFromEventSource(object source)
        {
            // 从事件源向上找，直到找到 ItemsRepeater 的直接子元素
            var element = source as DependencyObject;
            FrameworkElement? repeaterChild = null;

            while (element != null)
            {
                if (element is FrameworkElement fe)
                {
                    // 检查父级是否为 ShortcutGrid
                    var parent = VisualTreeHelper.GetParent(fe);
                    if (ReferenceEquals(parent, ShortcutGrid))
                    {
                        repeaterChild = fe;
                        break;
                    }
                }
                element = VisualTreeHelper.GetParent(element);
            }

            if (repeaterChild == null)
                return null;

            // ⚠️ 用 GetElementIndex 反查数据项，不依赖 DataContext
            int index = ShortcutGrid.GetElementIndex(repeaterChild);
            if (index < 0 || index >= CurrentShortcuts.Count)
                return null;

            return CurrentShortcuts[index];
        }

        private ShortcutItem? GetShortcutAtPoint(Point point)
        {
            for (int index = 0; index < CurrentShortcuts.Count; index++)
            {
                if (GetGeneratedShortcutElement(index) is not FrameworkElement element)
                    continue;

                if (_isShortcutDragging
                    && _draggingShortcut != null
                    && ReferenceEquals(CurrentShortcuts[index], _draggingShortcut))
                    continue;

                // 布局位置
                var origin = element.TransformToVisual(ShortcutGrid)
                    .TransformPoint(new Point(0, 0));

                // ⚠️ 拖拽期间只用布局位置：让位动画还在跑，视觉位置不准
                // 非拖拽期间加上弹簧 Offset，让命中跟随视觉位置
                Vector3 offset = Vector3.Zero;
                if (!_isShortcutDragging
                    && _shortcutMotionStates.TryGetValue(element, out var state))
                {
                    offset = state.MotionVisual.Offset;
                }

                var bounds = new Rect(
                    origin.X + offset.X,
                    origin.Y + offset.Y,
                    element.ActualWidth,
                    element.ActualHeight);

                if (bounds.Contains(point))
                    return CurrentShortcuts[index];
            }

            return null;
        }

        /// <summary>
        /// 根据指针落在卡片左半部还是右半部，计算拖拽项应插入的位置。
        /// 这里刻意不跳过正在拖拽的卡片：它仍占据一个布局格子，命中它时
        /// 返回它当前的索引，才能让卡片回到原位而不是只能落在原位两侧。
        /// </summary>
        //private int? GetShortcutInsertionIndex(Point point)
        //{
        //    int nearestIndex = -1;
        //    Rect nearestBounds = default;
        //    double nearestDistanceSquared = double.MaxValue;

        //    for (int index = 0; index < CurrentShortcuts.Count; index++)
        //    {
        //        if (GetGeneratedShortcutElement(index) is not FrameworkElement element)
        //            continue;

        //        var origin = element.TransformToVisual(ShortcutGrid)
        //            .TransformPoint(new Point(0, 0));
        //        var bounds = new Rect(
        //            origin.X,
        //            origin.Y,
        //            element.ActualWidth,
        //            element.ActualHeight);

        //        if (!bounds.Contains(point))
        //            continue;

        //        // 拖拽源本身的格子代表“保持当前顺序”。
        //        if (_draggingShortcut != null
        //            && ReferenceEquals(CurrentShortcuts[index], _draggingShortcut))
        //        {
        //            return index;
        //        }

        //        // UniformGrid 从左到右排列；左半部插在前，右半部插在后。
        //        return point.X < bounds.X + bounds.Width / 2
        //            ? index
        //            : index + 1;
        //    }

        //    // 卡片间有间距，容器边缘也有大量空白；不能只接受严格落在卡片
        //    // 矩形内的坐标。空白处吸附到最近的非拖拽卡片，使边缘也容易插入。
        //    for (int index = 0; index < CurrentShortcuts.Count; index++)
        //    {
        //        if (_draggingShortcut != null
        //            && ReferenceEquals(CurrentShortcuts[index], _draggingShortcut))
        //        {
        //            continue;
        //        }

        //        if (GetGeneratedShortcutElement(index) is not FrameworkElement element)
        //            continue;

        //        var origin = element.TransformToVisual(ShortcutGrid)
        //            .TransformPoint(new Point(0, 0));
        //        var bounds = new Rect(
        //            origin.X,
        //            origin.Y,
        //            element.ActualWidth,
        //            element.ActualHeight);

        //        double dx = point.X < bounds.Left
        //            ? bounds.Left - point.X
        //            : point.X > bounds.Right
        //                ? point.X - bounds.Right
        //                : 0;
        //        double dy = point.Y < bounds.Top
        //            ? bounds.Top - point.Y
        //            : point.Y > bounds.Bottom
        //                ? point.Y - bounds.Bottom
        //                : 0;
        //        double distanceSquared = dx * dx + dy * dy;

        //        if (distanceSquared < nearestDistanceSquared)
        //        {
        //            nearestDistanceSquared = distanceSquared;
        //            nearestIndex = index;
        //            nearestBounds = bounds;
        //        }
        //    }

        //    if (nearestIndex < 0)
        //        return null;

        //    // 位于最近卡片上方/下方时，优先按纵向决定；同一行或水平边缘时
        //    // 再根据左右半部决定。这也使首张和末张卡片外侧能直接插入。
        //    if (point.Y < nearestBounds.Top)
        //        return nearestIndex;
        //    if (point.Y > nearestBounds.Bottom)
        //        return nearestIndex + 1;

        //    return point.X < nearestBounds.X + nearestBounds.Width / 2
        //        ? nearestIndex
        //        : nearestIndex + 1;
        //}
        /// <summary>
        /// 返回鼠标所在格子的索引。
        /// 先按 Y 锁定行，再按 X 锁定列，避免在首尾列/边缘处跨行吸附。
        /// </summary>
        private int? GetShortcutInsertionIndex(Point point)
        {
            if (_dragSlotBounds.Count == 0) return null;

            // 1. 把槽位按 Y 分组成行（同一行 Y 相等，用容差防浮点误差）
            var rows = new List<List<(int SlotIndex, Rect Bounds)>>();
            for (int i = 0; i < _dragSlotBounds.Count; i++)
            {
                var (itemIndex, bounds) = _dragSlotBounds[i];
                var row = rows.FirstOrDefault(r =>
                    Math.Abs(r[0].Bounds.Y - bounds.Y) <= InsertionRowTolerance);
                if (row == null)
                {
                    row = new List<(int, Rect)>();
                    rows.Add(row);
                }
                row.Add((itemIndex, bounds));
            }

            rows = rows.OrderBy(r => r[0].Bounds.Y).ToList();
            foreach (var row in rows)
                row.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));

            // 2. 锁定鼠标所在行
            List<(int SlotIndex, Rect Bounds)>? targetRow = null;
            foreach (var row in rows)
            {
                double top = row.Min(c => c.Bounds.Top);
                double bottom = row.Max(c => c.Bounds.Bottom);
                if (point.Y >= top && point.Y <= bottom)
                {
                    targetRow = row;
                    break;
                }
            }
            if (targetRow == null)
            {
                double bestDist = double.MaxValue;
                foreach (var row in rows)
                {
                    double centerY = (row.Min(c => c.Bounds.Top)
                                    + row.Max(c => c.Bounds.Bottom)) / 2;
                    double d = Math.Abs(point.Y - centerY);
                    if (d < bestDist) { bestDist = d; targetRow = row; }
                }
            }
            if (targetRow == null) return null;

            // 3. 行内按 X 扫：鼠标在哪个槽位的左半区 → 插到该槽位
            for (int i = 0; i < targetRow.Count; i++)
            {
                var (slotIndex, bounds) = targetRow[i];
                double switchX = bounds.X + bounds.Width * InsertionThresholdRatio;
                if (point.X < switchX)
                    return slotIndex;
            }

            // 4. 鼠标在该行最右槽位的右半区之外
            return targetRow[^1].SlotIndex + 1;
        }
        private void UpdateShortcutSelection(ShortcutItem shortcut, bool toggle)
        {
            System.Diagnostics.Debug.WriteLine(
        $"[选中] {shortcut.Name}, toggle={toggle}, " +
        $"已选 {_selectedShortcuts.Count} 项");
            if (!toggle)
                _selectedShortcuts.Clear();

            if (_selectedShortcuts.Contains(shortcut))
            {
                if (toggle)
                    _selectedShortcuts.Remove(shortcut);
            }
            else
            {
                _selectedShortcuts.Add(shortcut);
            }

            UpdateToolbarButtonStates();
            RefreshShortcutSelectionVisuals();
            // ⭐ 记录锚点：下次 Shift+左键从这里开始算范围
            _shortcutSelectionAnchor = shortcut;
        }

        private int ReadFontSizeStep()
        {
            string storedStep = _ini.ReadValue("System", "FontSizeStep", string.Empty);
            if (int.TryParse(storedStep, NumberStyles.Integer, CultureInfo.InvariantCulture, out int step))
                return Math.Clamp(step, 0, 4);

            if (double.TryParse(
                    _ini.ReadValue("System", "FontSizeScale", SettingsDefaults.FontSizeScale.ToString(CultureInfo.InvariantCulture)),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double legacyScale))
                return FontSizeHelper.GetFontSizeStepFromScale(legacyScale);

            return SettingsDefaults.FontSizeStep;
        }

        internal Color GetMainContentAreaBackgroundColor()
        {
            var root = Content as FrameworkElement;
            var contentAreaBody = root?.FindName("ContentAreaBody") as Grid;
            if (contentAreaBody?.Background is SolidColorBrush brush)
                return brush.Color;

            var mainContentArea = root?.FindName("MainContentArea") as Grid;
            if (mainContentArea?.Background is SolidColorBrush mainBrush)
                return mainBrush.Color;

            return Color.FromArgb(255, 255, 255, 255);
        }

        internal Color GetTabBarBackgroundColor()
        {
            var root = Content as FrameworkElement;
            var tabBarGrid = root?.FindName("TabBarGrid") as Grid;
            if (tabBarGrid?.Background is SolidColorBrush brush)
                return brush.Color;

            return Color.FromArgb(255, 255, 255, 255);
        }

        // 点击展开/收缩按钮
        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarExpanded = !_isSidebarExpanded;

            // 保存状态到 INI
            _ini.WriteValue("Sidebar", "State", _isSidebarExpanded ? "true" : "false");

            // ===== 只处理不能做动画的属性 =====
            if (_isSidebarExpanded)
            {
                SidebarAddBt.HorizontalAlignment = HorizontalAlignment.Stretch;
                SidebarAddBt.CornerRadius = new CornerRadius(18.5);
                ToggleSidebarIcon.Glyph = "\uE76B";
            }
            else
            {
                SidebarAddBt.HorizontalAlignment = HorizontalAlignment.Center;
                SidebarAddBt.CornerRadius = new CornerRadius(18);
                ToggleSidebarIcon.Glyph = "\uE76C";
            }

            // ===== 三条动画同时跑（时长一致、缓动一致，看起来是一个整体） =====
            AnimateSidebar(_isSidebarExpanded);
            AnimateBottomButtons(_isSidebarExpanded);
            AnimateNavIconShift(_isSidebarExpanded);
            SetSidebarScrollBarVisibility(_isSidebarExpanded);

            // ⚠️ 注意：这里不要再调 ApplyNavMenuStyle()，它会瞬间把 translate 覆盖掉
            ScheduleUpdateTabPaths();
        }

        // 根据 _isSidebarExpanded 应用侧边栏样式
        private void ApplySidebarState()
        {
            if (_isSidebarExpanded)
            {
                LeftBGcolor.Width = SidebarExpandedWidth;
                SidebarGrid.Width = SidebarInnerWidth(SidebarExpandedWidth);
                SidebarColumn.Width = new GridLength(SidebarExpandedWidth);

                SidebarAddBt.Width = 208;
                SidebarAddBt.MinHeight = 37;
                SidebarAddBt.CornerRadius = new CornerRadius(18.5);
                SidebarAddBt.HorizontalAlignment = HorizontalAlignment.Stretch;
                SidebarAddBt.Margin = new Thickness(9);
                AddBtnText.Visibility = Visibility.Visible;
                AddBtnText.Opacity = 1;
                AddBtnIcon.FontSize = 14;
                ToggleSidebarIcon.Glyph = "\uE76B";
            }
            else
            {
                LeftBGcolor.Width = SidebarCollapsedWidth;
                SidebarGrid.Width = SidebarInnerWidth(SidebarCollapsedWidth);
                SidebarColumn.Width = new GridLength(SidebarCollapsedWidth);

                SidebarAddBt.Width = 36.8;
                SidebarAddBt.MinHeight = 36.8;
                SidebarAddBt.CornerRadius = new CornerRadius(18);
                SidebarAddBt.HorizontalAlignment = HorizontalAlignment.Center;
                SidebarAddBt.Margin = new Thickness(9);
                AddBtnText.Visibility = Visibility.Visible;
                AddBtnText.Opacity = 0;
                AddBtnIcon.FontSize = 14;
                ToggleSidebarIcon.Glyph = "\uE76C";
            }
            UpdateTitleBarLayout(_isSidebarExpanded ? SidebarExpandedWidth : SidebarCollapsedWidth);
            // ⭐ 底部两个按钮直接贴到对应位置
            ApplyBottomButtonsState(_isSidebarExpanded);

            // ⭐ 导航图标也直接贴好
            ApplyNavMenuStyle();
        }
        private double GetCategoryTargetShift(Category cat)
        {
            if (!_isSidebarExpanded)
            {
                // 收起态：图标居中
                double iconW = 20;
                double targetLeft = (SidebarInnerWidth(SidebarCollapsedWidth) - iconW) / 2;
                return targetLeft - 12;   // 12 = lvi.Padding.Left
            }

            // 展开态：动态分区右偏
            return IsDynamicPartitionItem(cat) ? DynamicCategoryExpandedOffset : 8;
        }

        private double GetPartitionHeaderTargetShift(ListViewItem container, StackPanel panel)
        {
            if (_isSidebarExpanded)
                return 0;

            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double contentWidth = panel.DesiredSize.Width;
            double availableWidth = SidebarInnerWidth(SidebarCollapsedWidth) - container.Padding.Left - container.Padding.Right;
            return (availableWidth - contentWidth) / 2;
        }

        private static void SetSectionHeaderArrowShift(FontIcon? arrow, bool expanded)
        {
            if (arrow == null)
                return;

            if (arrow.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                arrow.RenderTransform = transform;
            }

            transform.X = expanded ? -12 : 0;
        }

        // 根据展开/收起状态，设置每个 ListViewItem 的对齐、Padding、文字可见性
        private void ApplyNavMenuStyle()
        {
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    for (int i = 0; i < NavMenuList.Items.Count; i++)
                    {
                        if (NavMenuList.Items[i] is not Category cat) continue;
                        if (NavMenuList.ContainerFromIndex(i) is not ListViewItem lvi) continue;

                        lvi.VerticalContentAlignment = VerticalAlignment.Stretch;

                        // ⭐ 分区标题：补齐内边距，避免贴在左边缘
                        if (cat.IsSectionHeader)
                        {
                            lvi.Padding = new Thickness(12, 0, 12, 0);
                            lvi.HorizontalContentAlignment = HorizontalAlignment.Stretch;

                            // ⭐ 找到标题 TextBlock（名字与你 XAML 里一致）
                            var headerText = FindChildByName<TextBlock>(lvi, "SectionHeaderLabel");
                            if (headerText != null)
                            {
                                headerText.Text = _isSidebarExpanded
                                    ? cat.LocalizedSectionHeaderText
                                    : cat.LocalizedCompactHeaderText;
                                headerText.Width = _isSidebarExpanded ? 188 : double.NaN;            // ⭐ 收起态自适应
                                headerText.TextAlignment = _isSidebarExpanded
                                    ? TextAlignment.Left : TextAlignment.Center;
                            }
                            // ⭐ 展开/收起箭头（现有 SectionHeaderGlyph 绑定的控件）
                            var arrowIcon = FindChildByName<FontIcon>(lvi, "SectionHeaderArrow");


                            // 收起时隐藏箭头，让"◆默"居中显得干净
                            if (arrowIcon != null)
                            {
                                arrowIcon.Visibility = Visibility.Visible;          // ⭐ 一直可见
                                arrowIcon.FontSize = _isSidebarExpanded ? 9 : 8;    // 收起时略小
                                SetSectionHeaderArrowShift(arrowIcon, _isSidebarExpanded);
                            }

                            var headerPanel = FindChildByName<StackPanel>(lvi, "SectionHeaderPanel");
                            if (headerPanel != null)
                            {
                                if (headerPanel.RenderTransform is not TranslateTransform headerTransform)
                                {
                                    headerTransform = new TranslateTransform();
                                    headerPanel.RenderTransform = headerTransform;
                                }
                                headerTransform.X = GetPartitionHeaderTargetShift(lvi, headerPanel);
                            }

                            continue;
                        }

                        // ⭐ 分隔项：也补齐
                        if (cat.IsSeparator)
                        {
                            lvi.Padding = new Thickness(12, 0, 12, 0);
                            continue;
                        }

                        var panel = FindChildByName<StackPanel>(lvi, "CategoryPanel");
                        var icon = FindChildByName<FontIcon>(lvi, "CategoryIcon");
                        var text = FindChildByName<TextBlock>(lvi, "CategoryText");
                        if (panel == null || text == null) continue;

                        lvi.Padding = new Thickness(12, 0, 0, 0);
                        lvi.HorizontalContentAlignment = HorizontalAlignment.Stretch;

                        if (panel.RenderTransform is not TranslateTransform tt)
                        {
                            tt = new TranslateTransform();
                            panel.RenderTransform = tt;
                        }

                        tt.X = GetCategoryTargetShift(cat);
                        text.Opacity = _isSidebarExpanded ? 1 : 0;
                    }
                });
        }
        private void AnimateSidebar(bool expand)
        {
            // ============================================================
            // 防止上一次动画还没结束，又启动一个新的动画
            // ============================================================
            _sidebarAnimationTimer?.Stop();
            _sidebarAnimationTimer = null;


            double fromWidth = expand
                ? SidebarCollapsedWidth
                : SidebarExpandedWidth;

            double toWidth = expand
                ? SidebarExpandedWidth
                : SidebarCollapsedWidth;


            // 添加按钮宽度
            const double expandedAddButtonWidth = 208;
            double addFromWidth = expand ? 36.8 : expandedAddButtonWidth;
            double addToWidth = expand ? expandedAddButtonWidth : 36.8;


            double addFromHeight = expand ? 36.8 : 37;
            double addToHeight = expand ? 37 : 36.8;


            // 添加按钮文字透明度
            double textFromOpacity = expand ? 0 : 1;
            double textToOpacity = expand ? 1 : 0;


            // 底部按钮区透明度
            double panelFromOpacity = 1;
            double panelToOpacity = 1;


            var startTime = DateTime.Now;
            var duration = TimeSpan.FromMilliseconds(280);


            var timer = DispatcherQueue.CreateTimer();

            _sidebarAnimationTimer = timer;

            timer.Interval = TimeSpan.FromMilliseconds(16);


            timer.Tick += (s, e) =>
            {
                // 如果这个 Timer 已经不是当前动画 Timer，
                // 说明它已经被新的动画替换，不再允许它继续修改 UI。
                if (!ReferenceEquals(
                        _sidebarAnimationTimer,
                        timer))
                {
                    timer.Stop();
                    return;
                }


                var elapsed = DateTime.Now - startTime;

                double progress = Math.Min(
                    elapsed.TotalMilliseconds /
                    duration.TotalMilliseconds,
                    1.0);


                // Ease-Out Cubic
                double eased =
                    1 - Math.Pow(1 - progress, 3);


                // ========================================================
                // 1. 左侧菜单宽度
                // ========================================================
                double currentWidth =
                    fromWidth +
                    (toWidth - fromWidth) * eased;

                LeftBGcolor.Width = currentWidth;
                SidebarGrid.Width = SidebarInnerWidth(currentWidth);

                SidebarColumn.Width =
                    new GridLength(currentWidth);
                UpdateTitleBarLayout(currentWidth);
                NavMenuList.InvalidateMeasure();


                // ========================================================
                // 2. 添加按钮尺寸
                // ========================================================
                SidebarAddBt.Width =
                    addFromWidth +
                    (addToWidth - addFromWidth) * eased;

                SidebarAddBt.Height =
                    addFromHeight +
                    (addToHeight - addFromHeight) * eased;


                // ========================================================
                // 3. 添加按钮文字透明度
                // ========================================================
                AddBtnText.Opacity =
                    textFromOpacity +
                    (textToOpacity - textFromOpacity) * eased;


                // ========================================================
                // 4. 动画结束
                // ========================================================
                if (progress >= 1.0)
                {
                    timer.Stop();

                    // 只有当前 Timer 才允许清空字段
                    if (ReferenceEquals(
                            _sidebarAnimationTimer,
                            timer))
                    {
                        _sidebarAnimationTimer = null;
                    }


                    LeftBGcolor.Width = toWidth;
                    SidebarGrid.Width = SidebarInnerWidth(toWidth);

                    SidebarColumn.Width =
                        new GridLength(toWidth);
                    SetSidebarScrollBarVisibility(_isSidebarExpanded);

                    SidebarAddBt.Width =
                        addToWidth;

                    SidebarAddBt.Height =
                        addToHeight;

                    AddBtnText.Opacity =
                        textToOpacity;
                    UpdateTitleBarLayout(toWidth);

                    ReapplyAllNavMenuContainers();
                }
            };


            timer.Start();
        }
        private void ReapplyAllNavMenuContainers()
        {
            for (int i = 0; i < NavMenuList.Items.Count; i++)
            {
                if (NavMenuList.Items[i] is not Category cat) continue;
                if (NavMenuList.ContainerFromIndex(i) is not ListViewItem lvi) continue;

                if (cat.IsSectionHeader)
                {
                    lvi.Style = (Style)NavMenuList.Resources["NavSectionHeaderStyle"];   // ⭐ 补上

                    var headerText = FindChildByName<TextBlock>(lvi, "SectionHeaderLabel");
                    if (headerText != null)
                    {
                        headerText.Text = _isSidebarExpanded
                            ? cat.SectionHeaderText
                            : cat.CompactHeaderText;
                        headerText.Width = _isSidebarExpanded ? 188 : double.NaN;
                    }

                    var arrow = FindChildByName<FontIcon>(lvi, "SectionHeaderArrow");
                    if (arrow != null)
                    {
                        arrow.Visibility = Visibility.Visible;          // ⭐ 一直可见
                        arrow.FontSize = _isSidebarExpanded ? 9 : 8;    // 收起时略小
                        SetSectionHeaderArrowShift(arrow, _isSidebarExpanded);
                    }

                    var headerPanel = FindChildByName<StackPanel>(lvi, "SectionHeaderPanel");
                    if (headerPanel != null)
                    {
                        if (headerPanel.RenderTransform is not TranslateTransform headerTransform)
                        {
                            headerTransform = new TranslateTransform();
                            headerPanel.RenderTransform = headerTransform;
                        }
                        headerTransform.X = GetPartitionHeaderTargetShift(lvi, headerPanel);
                    }

                    continue;
                }
                else if (cat.IsSeparator)
                    lvi.Style = (Style)NavMenuList.Resources["NavSeparatorStyle"];
                else
                    lvi.Style = (Style)NavMenuList.Resources["NavItemStyle"];

                var panel = FindChildByName<StackPanel>(lvi, "CategoryPanel");
                var icon = FindChildByName<FontIcon>(lvi, "CategoryIcon");
                var text = FindChildByName<TextBlock>(lvi, "CategoryText");
                if (panel == null || text == null) continue;

                if (panel.RenderTransform is not TranslateTransform tt)
                {
                    tt = new TranslateTransform();
                    panel.RenderTransform = tt;
                }
                double iconW = icon?.ActualWidth > 0 ? icon.ActualWidth : 16;
                double currentLeft = 12;
                double targetLeft = (SidebarInnerWidth(SidebarCollapsedWidth) - iconW) / 2;
                double collapsedShift = targetLeft - currentLeft;
                tt.X = GetCategoryTargetShift(cat);
                text.Opacity = _isSidebarExpanded ? 1 : 0;
            }
        }
        private void AnimateNavIconShift(bool expand)
        {
            var duration = TimeSpan.FromMilliseconds(280);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            for (int i = 0; i < NavMenuList.Items.Count; i++)
            {
                if (NavMenuList.Items[i] is not Category c) continue;
                if (NavMenuList.ContainerFromIndex(i) is not ListViewItem lvi) continue;

                if (c.IsSectionHeader)
                {
                    var headerPanel = FindChildByName<StackPanel>(lvi, "SectionHeaderPanel");
                    var headerText = FindChildByName<TextBlock>(lvi, "SectionHeaderLabel");
                    var arrow = FindChildByName<FontIcon>(lvi, "SectionHeaderArrow");
                    if (headerPanel == null || headerText == null)
                        continue;

                    headerText.Text = expand ? c.LocalizedSectionHeaderText : c.LocalizedCompactHeaderText;
                    headerText.Width = expand ? 188 : double.NaN;
                    headerText.TextAlignment = expand ? TextAlignment.Left : TextAlignment.Center;
                    if (arrow != null)
                    {
                        arrow.Visibility = Visibility.Visible;
                        arrow.FontSize = expand ? 9 : 8;
                        SetSectionHeaderArrowShift(arrow, expand);
                    }

                    if (headerPanel.RenderTransform is not TranslateTransform headerTransform)
                    {
                        headerTransform = new TranslateTransform();
                        headerPanel.RenderTransform = headerTransform;
                    }

                    headerPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double headerShift = expand ? 0 : GetPartitionHeaderTargetShift(lvi, headerPanel);
                    var headerAnimation = new DoubleAnimation
                    {
                        From = headerTransform.X,
                        To = headerShift,
                        Duration = duration,
                        EasingFunction = ease
                    };
                    var headerStoryboard = new Storyboard();
                    Storyboard.SetTarget(headerAnimation, headerTransform);
                    Storyboard.SetTargetProperty(headerAnimation, "X");
                    headerStoryboard.Children.Add(headerAnimation);
                    headerStoryboard.Completed += (_, _) =>
                    {
                        headerStoryboard.Stop();
                        headerTransform.X = headerShift;
                    };
                    headerStoryboard.Begin();
                    continue;
                }

                if (c.IsSeparator) continue;

                var panel = FindChildByName<StackPanel>(lvi, "CategoryPanel");
                var icon = FindChildByName<FontIcon>(lvi, "CategoryIcon");
                var text = FindChildByName<TextBlock>(lvi, "CategoryText");
                if (panel == null || text == null) continue;

                if (panel.RenderTransform is not TranslateTransform tt)
                {
                    tt = new TranslateTransform();
                    panel.RenderTransform = tt;
                }

                double iconW = icon?.ActualWidth > 0 ? icon.ActualWidth : 16;
                double currentLeft = 12;                                   // 展开态 Padding.Left
                double targetLeft = (SidebarInnerWidth(SidebarCollapsedWidth) - iconW) / 2;
                double collapsedShift = targetLeft - currentLeft;

                // ⭐ 关键修复：展开时用 GetCategoryTargetShift，而不是写死 0
                double toShift = expand
                    ? GetCategoryTargetShift(NavMenuList.Items[i] as Category)
                    : collapsedShift;

                var sb = new Storyboard();
                var capturedShift = toShift;
                sb.Completed += (_, _) =>
                {
                    sb.Stop();                    // 解除 HoldEnd
                    tt.X = capturedShift;         // 手动把最终值写回去
                    text.Opacity = expand ? 1 : 0;
                };
                var shiftAnim = new DoubleAnimation
                {
                    From = tt.X,
                    To = toShift,
                    Duration = duration,
                    EasingFunction = ease
                };
                Storyboard.SetTarget(shiftAnim, tt);
                Storyboard.SetTargetProperty(shiftAnim, "X");
                sb.Children.Add(shiftAnim);

                var opAnim = new DoubleAnimation
                {
                    To = expand ? 1 : 0,
                    Duration = duration,
                    EasingFunction = ease
                };
                Storyboard.SetTarget(opAnim, text);
                Storyboard.SetTargetProperty(opAnim, "Opacity");
                sb.Children.Add(opAnim);

                sb.Begin();
            }
        }
        // ============== 动画：展开/收缩时两个按钮的平移 ==============
        private void AnimateBottomButtons(bool expand)
        {
            // 收起状态基准
            double baseX = (SidebarCollapsedWidth - BottomBtnSize) / 2 - 3;
            double sCollapsedY = 4;
            double tCollapsedY = 48;

            // 展开状态目标
            double sExpandedX = BottomBtnPad;
            double sExpandedY = BottomRowY;                                        // ⬇ 设置下滑到这里
            double tExpandedX = SidebarInnerWidth(SidebarExpandedWidth) - BottomBtnSize - BottomBtnPad - 3; // ➡ 展开移到右边
            double tExpandedY = BottomRowY;

            // 基准位置就是收起位置
            Canvas.SetLeft(SettingsButton, baseX);
            Canvas.SetTop(SettingsButton, sCollapsedY);
            Canvas.SetLeft(ToggleSidebarButton, baseX);
            Canvas.SetTop(ToggleSidebarButton, tCollapsedY);

            EnsureTranslate(SettingsButton);
            EnsureTranslate(ToggleSidebarButton);

            double sToX = expand ? (sExpandedX - baseX) : 0;
            double sToY = expand ? (sExpandedY - sCollapsedY) : 0;
            double tToX = expand ? (tExpandedX - baseX) : 0;
            double tToY = expand ? (tExpandedY - tCollapsedY) : 0;

            var duration = TimeSpan.FromMilliseconds(280);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            RunTranslate(SettingsButton, sToX, sToY, duration, ease);
            RunTranslate(ToggleSidebarButton, tToX, tToY, duration, ease);
        }

        // ============== 无动画：直接把两个按钮"贴到"对应位置（初始化用） ==============
        private void ApplyBottomButtonsState(bool expand)
        {
            double baseX = (SidebarCollapsedWidth - BottomBtnSize) / 2 - 3;
            double sCollapsedY = 4;
            double tCollapsedY = 48;

            double sExpandedX = BottomBtnPad;
            double sExpandedY = BottomRowY;
            double tExpandedX = SidebarInnerWidth(SidebarExpandedWidth) - BottomBtnSize - BottomBtnPad - 3;
            double tExpandedY = BottomRowY;

            Canvas.SetLeft(SettingsButton, baseX);
            Canvas.SetTop(SettingsButton, sCollapsedY);
            Canvas.SetLeft(ToggleSidebarButton, baseX);
            Canvas.SetTop(ToggleSidebarButton, tCollapsedY);

            EnsureTranslate(SettingsButton);
            EnsureTranslate(ToggleSidebarButton);

            var sTt = (TranslateTransform)SettingsButton.RenderTransform;
            var tTt = (TranslateTransform)ToggleSidebarButton.RenderTransform;

            sTt.X = expand ? (sExpandedX - baseX) : 0;
            sTt.Y = expand ? (sExpandedY - sCollapsedY) : 0;
            tTt.X = expand ? (tExpandedX - baseX) : 0;
            tTt.Y = expand ? (tExpandedY - tCollapsedY) : 0;
        }

        // 工具：确保元素上有 TranslateTransform
        private void EnsureTranslate(UIElement el)
        {
            if (el.RenderTransform is not TranslateTransform)
                el.RenderTransform = new TranslateTransform();
        }

        // 工具：对 TranslateTransform.X / Y 做 Storyboard 动画
        private void RunTranslate(UIElement el, double toX, double toY,
                          TimeSpan duration, EasingFunctionBase ease)
        {
            var tt = (TranslateTransform)el.RenderTransform;
            var sb = new Storyboard();

            var moveX = new DoubleAnimation { To = toX, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(moveX, tt);
            Storyboard.SetTargetProperty(moveX, "X");
            sb.Children.Add(moveX);

            var moveY = new DoubleAnimation { To = toY, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(moveY, tt);
            Storyboard.SetTargetProperty(moveY, "Y");
            sb.Children.Add(moveY);

            sb.Begin();
        }
        private void LoadCategoriesFromDb()
        {
            ReloadCategoriesRespectingCollapse();
        }

        private void SelectDefaultCategory()
        {
            var visibleCategories = Categories.Where(c => !c.IsSeparator).ToList();
            var target = visibleCategories.FirstOrDefault(c => c.IsShared && c.Name == "全部应用")
                ?? visibleCategories.FirstOrDefault();
            if (target != null) NavMenuList.SelectedItem = target;
        }

        private void PartitionHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: Category header }) return;

            bool collapse = !header.IsPartitionCollapsed;
            if (collapse
                && NavMenuList.SelectedItem is Category selectedCategory
                && !selectedCategory.IsSeparator
                && string.Equals(selectedCategory.PartitionId, header.SectionPartitionId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedCategoryByPartition[$"{(int)_currentMode}:{header.SectionPartitionId}"] = selectedCategory.Id;
            }
            header.IsPartitionCollapsed = collapse;
            header.SectionHeaderGlyph = collapse ? "\uE70D" : "\uE70E";

            _ini.WriteValue("Navigation",
                $"Collapsed.{(int)_currentMode}.{header.SectionPartitionId}",
                collapse ? "true" : "false");

            // ⭐ 关键：重建集合，让 ListView 收到 CollectionChanged 事件重排
            ReloadCategoriesRespectingCollapse();
        }
        private void ReloadCategoriesRespectingCollapse()
        {
            // 保存当前选中项
            var previousSelected = NavMenuList.SelectedItem as Category;
            string? previousPartitionId = previousSelected?.IsSectionHeader == true
                ? previousSelected.SectionPartitionId
                : previousSelected?.PartitionId;
            string? previousCategoryId = previousSelected is { IsSeparator: false }
                ? previousSelected.Id
                : previousPartitionId != null
                    && _selectedCategoryByPartition.TryGetValue($"{(int)_currentMode}:{previousPartitionId}", out var rememberedId)
                        ? rememberedId
                        : null;

            if (previousSelected is { IsSeparator: false } && !string.IsNullOrWhiteSpace(previousSelected.PartitionId))
                _selectedCategoryByPartition[$"{(int)_currentMode}:{previousSelected.PartitionId}"] = previousSelected.Id;

            _isReloadingCategories = true;
            try
            {
                Categories.Clear();

                var flatList = BlockBox.Data.CategoryRepository.GetVisible(_currentMode);
                bool showSeparators = _ini.ReadBool("Category", "ShowSeparator", true);
                bool sortByName = _ini.ReadValue("Category", "SortMode", "0") == "1";
                Func<Category, object> categoryOrder = sortByName
                    ? category => category.Name
                    : category => category.SortOrder;
                var sharedCategories = flatList.Where(c => c.IsShared).OrderBy(categoryOrder).ToList();

                const string defaultPartitionId = "default";
                bool defaultCollapsed = _ini.ReadBool(
                    "Navigation", $"Collapsed.{(int)_currentMode}.{defaultPartitionId}", false);

                if (sharedCategories.Count > 0)
                {
                    if (showSeparators)
                    {
                        Categories.Add(new Category
                        {
                            IsSeparator = true,
                            IsSectionHeader = true,
                            SectionHeaderText = "◆ 默认",
                            SectionHeaderGlyph = defaultCollapsed ? "\uE70D" : "\uE70E",
                            SectionPartitionId = defaultPartitionId,
                            IsPartitionCollapsed = defaultCollapsed
                        });
                    }

                    // ⭐ 只有展开状态才把子分类加进集合
                    if (!showSeparators || !defaultCollapsed)
                    {
                        foreach (var category in sharedCategories)
                        {
                            category.PartitionId = defaultPartitionId;
                            category.PartitionName = "◆ 默认";
                            category.IsPartitionCollapsed = false;
                            Categories.Add(category);
                        }
                    }
                }

                var partitions = BlockBox.Data.CategoryRepository.GetPartitions(_currentMode);
                foreach (var partition in partitions)
                {
                    bool collapsed = _ini.ReadBool(
                        "Navigation", $"Collapsed.{(int)_currentMode}.{partition.Id}", false);

                    if (showSeparators)
                    {
                        Categories.Add(new Category
                        {
                            IsSeparator = true,
                            IsSectionHeader = true,
                            SectionHeaderText = "◆ " + partition.Name,
                            SectionHeaderGlyph = collapsed ? "\uE70D" : "\uE70E",
                            SectionPartitionId = partition.Id,
                            IsPartitionCollapsed = collapsed
                        });
                    }

                    if (showSeparators && collapsed) continue;   // ⭐ 折叠就不加子分类

                    foreach (var cat in flatList
                        .Where(c => !c.IsShared && c.PartitionId == partition.Id)
                        .OrderBy(categoryOrder))
                    {
                        cat.PartitionName = partition.Name;
                        cat.IsPartitionCollapsed = false;
                        Categories.Add(cat);
                    }
                }

                // 分类折叠时把选中态交给分区标题；重新展开后恢复原分类。
                Category? restore = previousCategoryId == null
                    ? null
                    : Categories.FirstOrDefault(c => !c.IsSeparator && c.Id == previousCategoryId);

                if (restore == null && previousPartitionId != null
                    && Categories.Any(c => c.IsSectionHeader
                        && string.Equals(c.SectionPartitionId, previousPartitionId, StringComparison.OrdinalIgnoreCase)))
                {
                    restore = Categories.First(c => c.IsSectionHeader
                        && string.Equals(c.SectionPartitionId, previousPartitionId, StringComparison.OrdinalIgnoreCase));
                }

                if (restore != null)
                    NavMenuList.SelectedItem = restore;
                else if (previousSelected != null)
                    SelectDefaultCategory();
            }
            finally
            {
                _isReloadingCategories = false;
            }

            DispatcherQueue.TryEnqueue(
    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
    () =>
    {
        NavMenuList.UpdateLayout();   // ⭐ 强制容器生成完毕
        ApplyNavMenuStyle();
            SetSidebarScrollBarVisibility(_isSidebarExpanded);
    });
        }

        private void ShortcutGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {// ⭐ 设置打开 / Grid 不可见 → 完全忽略
            if (_isSettingsVisible) return;
            if (ShortcutGrid.Visibility != Visibility.Visible) return;
            if (_isShortcutDragging) return;


            if (_isShortcutDragging)
                return;

            var point = e.GetCurrentPoint(ShortcutSelectionCanvas).Position;
            var properties = e.GetCurrentPoint(ShortcutGrid).Properties;
            bool toggle = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (properties.IsRightButtonPressed)
                return;

            var gridPoint = e.GetCurrentPoint(ShortcutGrid).Position;
            // PointerPressed 的事件源来自当前实际点击的卡片，优先使用它。
            // 仅拖拽移动过程使用几何命中，避免 ItemsRepeater 重排时旧 DataContext 干扰。
            var shortcut = GetShortcutFromEventSource(e.OriginalSource)
     ?? GetShortcutAtPoint(gridPoint);

            bool shift = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (shortcut != null)
            {
                if (shift)
                {
                    // ⭐ Shift+左键：范围选择，不启动拖拽
                    UpdateShortcutSelectionRange(shortcut);
                    ResetPendingShortcutDrag();
                }
                else if (!toggle && CanDragSortInCurrentView())
                {
                    // 启动拖拽准备（原逻辑）
                    _draggingShortcut = shortcut;
                    _shortcutDragStartPoint = e.GetCurrentPoint(ShortcutGridHost).Position;
                    _shortcutDragPointer = e.Pointer;
                    _shortcutDragPointerId = e.Pointer.PointerId;
                    _shortcutDragMoved = false;
                    _pendingShortcutSelectionToggle = toggle;
                }
                else
                {
                    // 普通点击 / Ctrl+点击（原逻辑）
                    UpdateShortcutSelection(shortcut, toggle);
                    ResetPendingShortcutDrag();
                }
            }
            else
            {
                // ⭐ 空白处点击：清空锚点
                _shortcutSelectionAnchor = null;
                ResetPendingShortcutDrag();
                BeginShortcutBoxSelection(point, toggle);
                ShortcutInteractionHost.CapturePointer(e.Pointer);
            }

            UpdateToolbarButtonStates();
        }
        /// <summary>
        /// Shift+左键：从锚点到目标项之间的所有项全部选中
        /// </summary>
        private void UpdateShortcutSelectionRange(ShortcutItem target)
        {
            int targetIndex = CurrentShortcuts.IndexOf(target);
            if (targetIndex < 0) return;

            // 没有锚点（比如新切了分类还没点过任何项）→ 就当成单点选择
            int anchorIndex = _shortcutSelectionAnchor != null
                ? CurrentShortcuts.IndexOf(_shortcutSelectionAnchor)
                : -1;
            if (anchorIndex < 0)
                anchorIndex = targetIndex;

            int start = Math.Min(anchorIndex, targetIndex);
            int end = Math.Max(anchorIndex, targetIndex);

            _selectedShortcuts.Clear();
            for (int i = start; i <= end; i++)
                _selectedShortcuts.Add(CurrentShortcuts[i]);

            UpdateToolbarButtonStates();
            RefreshShortcutSelectionVisuals();
        }
        private void ShortcutGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isExternalFileDragStarting || _isExternalFileDragRunning)
                return;

            if (!_isShortcutDragging
                && _shortcutDragPointer != null
                && _draggingShortcut != null
                && _shortcutDragPointerId == e.Pointer.PointerId)
            {
                // ⭐ 加这个前置判断
                if (_isSettingsVisible || ShortcutGrid.Visibility != Visibility.Visible)
                    return;
                var currentPoint = e.GetCurrentPoint(ShortcutGridHost).Position;
                if (Math.Abs(currentPoint.X - _shortcutDragStartPoint.X) >= ShortcutDragThreshold
                    || Math.Abs(currentPoint.Y - _shortcutDragStartPoint.Y) >= ShortcutDragThreshold)
                {
                    _shortcutDragMoved = true;
                    var sourceIndex = CurrentShortcuts.IndexOf(_draggingShortcut);
                    var sourceElement = sourceIndex >= 0
                        ? GetGeneratedShortcutElement(sourceIndex)
                        : null;
                    BeginShortcutDrag(_draggingShortcut, _shortcutDragStartPoint, sourceElement, e.Pointer);
                }
            }

            if (_isShortcutDragging && _draggingShortcut != null)
            {
                if (IsPointerOutsideMainWindow())
                {
                    _ = StartExternalFileDragAsync(e.GetCurrentPoint(ShortcutGrid));
                    return;
                }

                var hostPoint = e.GetCurrentPoint(ShortcutGridHost).Position;
                UpdateShortcutDragGhostPosition(hostPoint);
                var navPoint = e.GetCurrentPoint(NavMenuList).Position;
                var hitCategory = HitCategoryByPoint(navPoint);
                bool validCategoryTarget = hitCategory != null
                    && CanDropShortcutToCategory(_draggingShortcut, hitCategory);
                int sourceIndex = CurrentShortcuts.IndexOf(_draggingShortcut);
                int? insertionIndex = GetShortcutInsertionIndex(
                    e.GetCurrentPoint(ShortcutGrid).Position);

                //if (sourceIndex >= 0 && insertionIndex.HasValue)
                //{
                //    // Move 的目标索引是“移除源项后的索引”，因此源项在插入点前时要减一。
                //    int targetIndex = insertionIndex.Value;
                //    if (sourceIndex < targetIndex)
                //        targetIndex--;

                //    if (sourceIndex != targetIndex)
                //    {
                //        CurrentShortcuts.Move(sourceIndex, targetIndex);
                //        SchedulePersistShortcutSortOrders();

                //        // ⚠️ 拖拽排序后：把当前视图的排序模式改成“自定义”
                //        EnsureCurrentViewIsCustomSort();
                //    }
                //}

                if (!validCategoryTarget && sourceIndex >= 0 && insertionIndex.HasValue)
                {
                    // 新版 GetShortcutInsertionIndex 返回"槽位编号"
                    // 槽位编号 = 视觉位置编号 = CurrentShortcuts 索引（Repeater 按顺序填槽）
                    // Move(from, to) 的 to 就是卡片最终位置，不做任何偏移
                    int targetIndex = Math.Clamp(insertionIndex.Value, 0, CurrentShortcuts.Count - 1);

                    if (sourceIndex != targetIndex)
                    {
                        MoveShortcutCollectionItem(sourceIndex, targetIndex);
                        SchedulePersistShortcutSortOrders();
                        EnsureCurrentViewIsCustomSort();
                    }
                }

                if (validCategoryTarget
                    && ContentKindHelper.Matches(_draggingShortcut.TargetPath, hitCategory!.ContentKind, hitCategory.ExtensionList))
                {
                    UpdateDragCategoryVisual(hitCategory);
                }
                else
                {
                    UpdateDragCategoryVisual(null);
                }

                return;
            }

            if (_isBoxSelecting)
                UpdateShortcutBoxSelection(e.GetCurrentPoint(ShortcutSelectionCanvas).Position);
        }

        private bool IsPointerOutsideMainWindow()
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            return GetCursorPos(out NativePoint point)
                && GetWindowRect(hwnd, out NativeRect rect)
                && (point.X < rect.Left || point.X >= rect.Right
                    || point.Y < rect.Top || point.Y >= rect.Bottom);
        }

        private async Task StartExternalFileDragAsync(Microsoft.UI.Input.PointerPoint pointerPoint)
        {
            if (_isExternalFileDragStarting || _isExternalFileDragRunning || _draggingShortcut == null)
                return;

            _isExternalFileDragStarting = true;
            _externalFileDragPointerReleased = false;
            bool internalOrderRestored = false;
            var externalDragStagingDirectories = new List<string>();

            try
            {
                var draggedShortcut = _draggingShortcut;
                var candidates = _selectedShortcuts.Count > 1 && _selectedShortcuts.Contains(draggedShortcut)
                    ? _selectedShortcuts.ToList()
                    : new List<ShortcutItem> { draggedShortcut };

                var files = new List<Windows.Storage.StorageFile>();
                var includedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in candidates.Where(item => item.IsFile))
                {
                    if (_externalFileDragPointerReleased)
                        break;

                    string sourcePath = candidate.TargetPath;
                    if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
                    {
                        if (!includedSourcePaths.Add(sourcePath))
                            continue;

                        try
                        {
                            // 源文件存在时保持原有语义：直接拖拽原始文件。
                            files.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(sourcePath));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[外部拖拽] 跳过无效文件 {sourcePath}: {ex.Message}");
                        }
                        continue;
                    }

                    try
                    {
                        if (string.IsNullOrWhiteSpace(candidate.BackupPath))
                            continue;

                        string backupPath = BackupPathHelper.MakeAbsolute(candidate.BackupPath);
                        if (!File.Exists(backupPath))
                            continue;

                        string stagingDirectory = BackupService.CreateExternalDragStagingDirectory();
                        externalDragStagingDirectories.Add(stagingDirectory);
                        string restoredPath = await BackupService.RestoreBackupForExternalDragAsync(
                            backupPath, sourcePath, stagingDirectory);
                        files.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(restoredPath));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[外部拖拽] 无法从备份准备可拖放文件 {candidate.Name}: {ex.Message}");
                    }
                }

                if (_isShortcutDragging)
                {
                    RestoreShortcutOrderAfterCanceledDrag();
                    internalOrderRestored = true;
                }

                if (_externalFileDragPointerReleased || !_isShortcutDragging || files.Count == 0)
                {
                    if (files.Count == 0 && !_externalFileDragPointerReleased)
                        _ = ShowMessageAsync("没有可拖到其他程序的本地文件");
                    return;
                }

                RemoveShortcutDragGhost();
                if (_draggingShortcut != null)
                    _draggingShortcut.IsDragging = false;
                RestoreAllShortcutVisualsImmediate();

                _pendingExternalDragFiles = files;
                _isExternalFileDragRunning = true;
                await ShortcutGrid.StartDragAsync(pointerPoint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[外部拖拽] 启动系统文件拖拽失败: {ex}");
            }
            finally
            {
                foreach (string stagingDirectory in externalDragStagingDirectories)
                    BackupService.ScheduleExternalDragStagingCleanup(stagingDirectory);

                _pendingExternalDragFiles = null;
                _isExternalFileDragRunning = false;
                _isExternalFileDragStarting = false;

                if (_isShortcutDragging)
                {
                    if (!internalOrderRestored)
                        RestoreShortcutOrderAfterCanceledDrag();
                    EndShortcutDrag();
                }
            }
        }

        private void ShortcutInteractionHost_DragStarting(
            UIElement sender,
            Microsoft.UI.Xaml.DragStartingEventArgs e)
        {
            if (_pendingExternalDragFiles is not { Count: > 0 } files)
                return;

            e.Data.SetStorageItems(files);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUI.SetContentFromDataPackage();
        }

        //private void ShortcutGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        //{

        //    if (!_isShortcutDragging
        //        && _shortcutDragPointer != null
        //        && _draggingShortcut != null
        //        && _shortcutDragPointerId == e.Pointer.PointerId)
        //    {
        //        var currentPoint = e.GetCurrentPoint(ShortcutGridHost).Position;
        //        if (Math.Abs(currentPoint.X - _shortcutDragStartPoint.X) >= ShortcutDragThreshold
        //            || Math.Abs(currentPoint.Y - _shortcutDragStartPoint.Y) >= ShortcutDragThreshold)
        //        {
        //            _shortcutDragMoved = true;
        //            var sourceIndex = CurrentShortcuts.IndexOf(_draggingShortcut);
        //            var sourceElement = sourceIndex >= 0
        //                ? GetGeneratedShortcutElement(sourceIndex)
        //                : null;
        //            BeginShortcutDrag(_draggingShortcut, _shortcutDragStartPoint, sourceElement, e.Pointer);
        //        }
        //    }

        //    if (_isShortcutDragging && _draggingShortcut != null)
        //    {
        //        var hostPoint = e.GetCurrentPoint(ShortcutGridHost).Position;
        //        UpdateShortcutDragGhostPosition(hostPoint);

        //        int sourceIndex = CurrentShortcuts.IndexOf(_draggingShortcut);
        //        int? insertionIndex = GetShortcutInsertionIndex(
        //            e.GetCurrentPoint(ShortcutGrid).Position);

        //        if (sourceIndex >= 0 && insertionIndex.HasValue)
        //        {
        //            // ⚠️ 落点语义：insertionIndex 就是卡片要落到的目标索引，不做任何修正
        //            int targetIndex = insertionIndex.Value;

        //            if (sourceIndex != targetIndex)
        //            {
        //                CurrentShortcuts.Move(sourceIndex, targetIndex);
        //                SchedulePersistShortcutSortOrders();
        //                EnsureCurrentViewIsCustomSort();
        //            }
        //        }

        //        var navPoint = e.GetCurrentPoint(NavMenuList).Position;
        //        var hitCategory = HitCategoryByPoint(navPoint);
        //        if (hitCategory != null
        //            && _draggingShortcut.CategoryId != hitCategory.Id
        //            && ContentKindHelper.Matches(_draggingShortcut.TargetPath, hitCategory.ContentKind, hitCategory.ExtensionList))
        //        {
        //            UpdateDragCategoryVisual(hitCategory);
        //        }
        //        else
        //        {
        //            UpdateDragCategoryVisual(null);
        //        }

        //        return;
        //    }

        //    if (_isBoxSelecting)
        //        UpdateShortcutBoxSelection(e.GetCurrentPoint(ShortcutSelectionCanvas).Position);
        //}
        /// <summary>
        /// 拖拽排序后调用：确保当前视图是“自定义”排序模式
        /// </summary>
        private void EnsureCurrentViewIsCustomSort()
        {
            // ==== 分类视图 ====
            if (_currentView == ViewKind.Category
                && NavMenuList.SelectedItem is Category cat
                && !cat.IsSeparator)
            {
                if (cat.SortMode == SortMode.Custom) return;   // 已经是，跳过

                cat.SortMode = SortMode.Custom;
                BlockBox.Data.CategoryRepository.UpdateSortMode(cat.Id, SortMode.Custom);
                return;
            }

            // ==== 用户标签视图 ====
            if (_currentView == ViewKind.Tab
                && _currentTabItem != null
                && _currentTabItem.BuiltinKind == BuiltinTabKind.None)
            {
                if (_currentTabItem.SortMode == SortMode.Custom) return;

                _currentTabItem.SortMode = SortMode.Custom;
                BlockBox.Data.TabRepository.UpdateSortMode(_currentTabItem.Id, SortMode.Custom);
                return;
            }

            // 收藏 / 最近访问 → 不支持拖拽，不用处理
        }
        private async void ShortcutGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isExternalFileDragStarting || _isExternalFileDragRunning)
            {
                if (!_isExternalFileDragRunning)
                    _externalFileDragPointerReleased = true;
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[松开] dragging={_isShortcutDragging}, " +
                $"pointerId={_shortcutDragPointerId}, " +
                $"ePointerId={e.Pointer.PointerId}, " +
                $"moved={_shortcutDragMoved}, " +
                $"dragShortcut={_draggingShortcut?.Name ?? "null"}");

            if (_isShortcutDragging && _draggingShortcut != null)
            {
                var targetCategory = _dragCategoryTarget;
                var draggedShortcut = _draggingShortcut;

                bool validCategoryDrop = targetCategory != null
                    && CanDropShortcutToCategory(draggedShortcut, targetCategory);
                bool copyToCategory = validCategoryDrop && ShouldCopyShortcutOnDrop(draggedShortcut);
                bool moveToOtherCategory = validCategoryDrop && !copyToCategory;
                bool isDuplicate = false;
                bool moveFailed = false;

                if (copyToCategory)
                {
                    // 全部应用是共享源清单，拖入派生分类时复制一条独立记录。
                }
                else if (moveToOtherCategory)
                {
                    try
                    {
                        isDuplicate = ShortcutRepository.ExistsInCategory(
                            targetCategory!.Id, _currentMode, draggedShortcut.TargetPath);
                        if (!isDuplicate)
                        {
                            ShortcutRepository.MoveToCategory(
                                draggedShortcut.Id,
                                targetCategory.Id,
                                ShortcutRepository.GetNextSortOrder(targetCategory.Id, targetCategory.IsShared));
                            var sourceCategoryId = _dragCategoryOriginalSelection?.Id ?? draggedShortcut.CategoryId;
                            if (_currentView == ViewKind.Category
                                && string.Equals(sourceCategoryId, draggedShortcut.CategoryId, StringComparison.Ordinal)
                                && !string.Equals(sourceCategoryId, targetCategory.Id, StringComparison.Ordinal))
                                CurrentShortcuts.Remove(draggedShortcut);
                        }
                    }
                    catch (Exception ex)
                    {
                        moveFailed = true;
                        System.Diagnostics.Debug.WriteLine($"[拖拽移动] {ex}");
                    }
                }

                try
                {
                    // 移动成功 → 从当前分类视图移除
                }
                finally
                {
                    if (_isBoxSelecting)
                        EndShortcutBoxSelection();

                    EndShortcutDrag();   // 里面会 LoadShortcutsFromDb，把顺序/重复状态复原
                }

                // ⚠️ 弹窗要在 finally 之后，避免阻塞 EndShortcutDrag
                if (isDuplicate)
                {
                    var dupName = draggedShortcut.Name;
                    var dupTargetName = targetCategory!.DisplayName;
                    _ = ShowMessageAsync(
                        LanguageService.TranslateFormat("「{category}」中已存在「{name}」，已取消移动。", ("{category}", dupTargetName), ("{name}", dupName)));
                }
                else if (moveFailed)
                {
                    _ = ShowMessageAsync("移动失败，原分类中的记录已保留。");
                }
                else if (copyToCategory && targetCategory != null)
                {
                    await AddToCategoryAsync(draggedShortcut, targetCategory);
                }
                else if (moveToOtherCategory && targetCategory != null)
                {
                    await RememberProgramCategoryAsync(draggedShortcut, targetCategory, isMove: true);
                }

                return;
            }

            if (_isBoxSelecting)
                EndShortcutBoxSelection();

            if (_draggingShortcut != null
                && _shortcutDragPointerId == e.Pointer.PointerId
                && !_shortcutDragMoved)
            {
                UpdateShortcutSelection(_draggingShortcut, _pendingShortcutSelectionToggle);
            }

            ResetPendingShortcutDrag();
            UpdateToolbarButtonStates();
        }

        private void ShortcutGrid_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_isExternalFileDragStarting || _isExternalFileDragRunning)
            {
                if (!_isExternalFileDragRunning)
                    _externalFileDragPointerReleased = true;
                return;
            }

            CancelShortcutPointerInteraction();
        }

        private void ShortcutGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_isExternalFileDragStarting || _isExternalFileDragRunning)
            {
                if (!_isExternalFileDragRunning)
                    _externalFileDragPointerReleased = true;
                return;
            }

            if (_isShortcutDragging || _isBoxSelecting || _shortcutDragPointer != null)
                CancelShortcutPointerInteraction();
        }

        private void ResetPendingShortcutDrag()
        {
            _draggingShortcut = null;
            _shortcutDragPointer = null;
            _shortcutDragPointerId = 0;
            _shortcutDragMoved = false;
            _pendingShortcutSelectionToggle = false;
            _shortcutDragStartPoint = default;
            _dragSlotBounds.Clear();   // ⭐ 新增
        }

        private void CancelShortcutPointerInteraction()
        {
            if (_isShortcutDragging)
            {
                RestoreShortcutOrderAfterCanceledDrag();
                EndShortcutDrag();
            }

            if (_isBoxSelecting)
                EndShortcutBoxSelection();

            ResetPendingShortcutDrag();
            UpdateToolbarButtonStates();
        }

        private void RestoreShortcutOrderAfterCanceledDrag()
        {
            if (_shortcutOrderBeforeDrag.Count == CurrentShortcuts.Count)
            {
                for (int targetIndex = 0; targetIndex < _shortcutOrderBeforeDrag.Count; targetIndex++)
                {
                    int currentIndex = CurrentShortcuts.IndexOf(_shortcutOrderBeforeDrag[targetIndex]);
                    if (currentIndex >= 0 && currentIndex != targetIndex)
                    {
                        MoveShortcutCollectionItem(currentIndex, targetIndex);
                    }
                }
            }

            if (_dragOriginalSortMode is not SortMode originalSortMode)
                return;

            if (_currentView == ViewKind.Category
                && (_dragCategoryOriginalSelection ?? NavMenuList.SelectedItem as Category) is Category category
                && !category.IsSeparator)
            {
                category.SortMode = originalSortMode;
                CategoryRepository.UpdateSortMode(category.Id, originalSortMode);
            }
            else if (_currentView == ViewKind.Tab
                && _currentTabItem?.BuiltinKind == BuiltinTabKind.None)
            {
                _currentTabItem.SortMode = originalSortMode;
                TabRepository.UpdateSortMode(_currentTabItem.Id, originalSortMode);
            }
        }

        private void CurrentShortcuts_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RefreshShortcutScrollBar();
            if (_isShortcutCollectionReordering)
                return;

            _selectedShortcuts.RemoveAll(shortcut => !CurrentShortcuts.Contains(shortcut));
            UpdateToolbarButtonStates();
            RefreshShortcutSelectionVisuals();
        }

        private void MoveShortcutCollectionItem(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex
                || fromIndex < 0 || fromIndex >= CurrentShortcuts.Count
                || toIndex < 0 || toIndex >= CurrentShortcuts.Count)
            {
                return;
            }

            // ItemsRepeater 在快速 Move 通知后可能复用错位的元素容器。
            // Remove + Insert 保留 ShortcutItem 实例，但强制容器重新绑定到最终索引。
            var item = CurrentShortcuts[fromIndex];
            _isShortcutCollectionReordering = true;
            try
            {
                CurrentShortcuts.RemoveAt(fromIndex);
                CurrentShortcuts.Insert(toIndex, item);
            }
            finally
            {
                _isShortcutCollectionReordering = false;
            }

            _selectedShortcuts.RemoveAll(shortcut => !CurrentShortcuts.Contains(shortcut));
            UpdateToolbarButtonStates();
            RefreshShortcutSelectionVisuals();
        }

        private bool CanDragSortInCurrentView()
        {
            if (_searchKeyword.Length > 0)
                return false;

            if (_currentView == ViewKind.Category
                && NavMenuList.SelectedItem is Category cat
                && !cat.IsSeparator)
            {
                return true;
            }

            if (_currentView == ViewKind.Tab && _currentTabItem != null)
            {
                // 收藏顺序由 FavoriteTime 决定；没有独立的手动排序字段，不能安全地拖拽重排。
                return _currentTabItem.BuiltinKind == BuiltinTabKind.None;
            }

            return false;
        }

        private void SchedulePersistShortcutSortOrders()
        {
            _sortPersistTimer ??= DispatcherQueue.CreateTimer();
            _sortPersistTimer.Interval = TimeSpan.FromMilliseconds(300);
            _sortPersistTimer.IsRepeating = false;
            _sortPersistTimer.Tick -= SortPersistTimer_Tick;
            _sortPersistTimer.Tick += SortPersistTimer_Tick;
            _sortPersistTimer.Stop();
            _sortPersistTimer.Start();
        }

        private void SortPersistTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();

            var updates = new List<(string Id, int SortOrder)>();
            for (int i = 0; i < CurrentShortcuts.Count; i++)
                updates.Add((CurrentShortcuts[i].Id, i + 1));

            if (updates.Count > 0)
                ShortcutRepository.UpdateSortOrders(updates, _currentMode, GetCurrentSortableTabId());
        }

        private string? GetCurrentSortableTabId()
            => _currentView == ViewKind.Tab
                && _currentTabItem?.BuiltinKind == BuiltinTabKind.None
                ? _currentTabItem?.Id
                : null;

        private void BeginShortcutDrag(ShortcutItem shortcut, Point startPoint, FrameworkElement? sourceElement, Pointer pointer)
        {
            // ⭐ 设置打开 / Grid 不可见 / 未加载 → 不启动拖拽
            if (_isSettingsVisible) return;
            if (ShortcutGrid.Visibility != Visibility.Visible) return;
            if (!ShortcutGrid.IsLoaded) return;

            if (_isBoxSelecting)
                EndShortcutBoxSelection();

            _isShortcutDragging = true;
            _draggingShortcut = shortcut;
            _shortcutOrderBeforeDrag = CurrentShortcuts.ToList();
            _dragOriginalSortMode = _currentView == ViewKind.Category
                ? (NavMenuList.SelectedItem as Category)?.SortMode
                : _currentTabItem?.SortMode;
            _shortcutDragStartPoint = startPoint;
            _dragCategoryTarget = null;
            _dragCategoryOriginalSelection = null;

            shortcut.IsDragging = true;
            CaptureDragSlotBounds();
            CreateShortcutDragGhost(shortcut, startPoint);

            // ⭐ CapturePointer 兜底 try-catch
            try
            {
                ShortcutGrid.CapturePointer(pointer);
            }
            catch (ArgumentException)
            {
                // 指针已失效 / 元素不可交互 → 放弃本次拖拽
                EndShortcutDrag();
                return;
            }

            if (sourceElement != null
                && FindChildByName<FrameworkElement>(sourceElement, "ShortcutMotionHost") is FrameworkElement host)
            {
                host.Opacity = 0;
            }
        }
        private void CaptureDragSlotBounds()
        {
            _dragSlotBounds.Clear();

            for (int i = 0; i < CurrentShortcuts.Count; i++)
            {
                if (GetGeneratedShortcutElement(i) is not FrameworkElement el)
                    continue;

                var origin = el.TransformToVisual(ShortcutGrid)
                    .TransformPoint(new Point(0, 0));
                int itemIndex = ShortcutGrid.GetElementIndex(el);
                if (itemIndex < 0 || itemIndex >= CurrentShortcuts.Count)
                    continue;
                _dragSlotBounds.Add((itemIndex, new Rect(origin.X, origin.Y,
                    el.ActualWidth, el.ActualHeight)));
            }

            // 按行优先、列次之排序，得到稳定的槽位编号 0..N-1
            _dragSlotBounds = _dragSlotBounds
                .OrderBy(r => r.Bounds.Y)
                .ThenBy(r => r.Bounds.X)
                .ToList();
        }

        private void EndShortcutDrag()
        {
            if (_isEndingShortcutDrag) return;
            _isEndingShortcutDrag = true;

            try
            {
                RemoveShortcutDragGhost();

                if (_draggingShortcut != null)
                    _draggingShortcut.IsDragging = false;

                _isShortcutDragging = false;
                _draggingShortcut = null;

                if (_dragCategoryTarget != null)
                {
                    int idx = NavMenuList.Items.IndexOf(_dragCategoryTarget);
                    if (idx >= 0 && NavMenuList.ContainerFromIndex(idx) is ListViewItem lvi)
                    {
                        var bg = FindChildByName<Border>(lvi, "CategoryRootBackground");
                        bg?.ClearValue(Border.BackgroundProperty);
                    }
                }

                _shortcutDragPointer = null;
                _shortcutDragPointerId = 0;
                _shortcutDragMoved = false;
                _dragCategoryTarget = null;
                ShortcutGrid.ReleasePointerCaptures();

                // ⚠️ 关键①：停掉延迟持久化 timer
                _sortPersistTimer?.Stop();

                // ⚠️ 关键②：立刻把内存顺序写库，保证数据库正确
                var updates = new List<(string Id, int SortOrder)>();
                for (int i = 0; i < CurrentShortcuts.Count; i++)
                    updates.Add((CurrentShortcuts[i].Id, i + 1));
                if (updates.Count > 0)
                    ShortcutRepository.UpdateSortOrders(updates, _currentMode, GetCurrentSortableTabId());

                // 等弹簧结束后清除视觉位移；不触碰 ItemsSource，保留卡片和滚动位置。
                _pendingReloadAfterDrag = true;
                _dragSettledFrameCount = 0;
                _pendingReloadDeadline = DateTime.Now.AddMilliseconds(800);   // 最多等 800ms
                _dragSettledTimeoutTimer ??= DispatcherQueue.CreateTimer();
                _dragSettledTimeoutTimer.Interval = TimeSpan.FromMilliseconds(800);
                _dragSettledTimeoutTimer.IsRepeating = false;
                _dragSettledTimeoutTimer.Tick -= DragSettledTimeout_Tick;
                _dragSettledTimeoutTimer.Tick += DragSettledTimeout_Tick;
                _dragSettledTimeoutTimer.Stop();
                _dragSettledTimeoutTimer.Start();
            }
            finally
            {
                if (_dragCategoryOriginalSelection != null)
                {
                    _isDragHoveringCategory = true;
                    try { NavMenuList.SelectedItem = _dragCategoryOriginalSelection; }
                    finally { _isDragHoveringCategory = false; }
                }
                _dragCategoryOriginalSelection = null;
                _dragCategoryTarget = null;
                _shortcutOrderBeforeDrag.Clear();
                _dragOriginalSortMode = null;
                _isEndingShortcutDrag = false;
            }
            int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
            for (int i = 0; i < childCount; i++)
            {
                if (VisualTreeHelper.GetChild(ShortcutGrid, i) is not FrameworkElement element)
                    continue;
                if (FindChildByName<FrameworkElement>(element, "ShortcutMotionHost")
                    is FrameworkElement host)
                {
                    host.Opacity = 1;
                }
            }
            _dragSlotBounds.Clear();
        }

        //private void RefreshShortcutVisualsAfterDrag()
        //{
        //    int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
        //    for (int i = 0; i < childCount; i++)
        //    {
        //        if (VisualTreeHelper.GetChild(ShortcutGrid, i) is not FrameworkElement element)
        //            continue;

        //        if (FindChildByName<FrameworkElement>(element, "ShortcutMotionHost") is FrameworkElement host)
        //            host.Opacity = 1;

        //        if (FindChildByName<FrameworkElement>(element, "ShortcutDragPlaceholder") is FrameworkElement ph)
        //            ph.Visibility = Visibility.Collapsed;
        //    }
        //}

        private void CreateShortcutDragGhost(ShortcutItem item, Point hostPoint)
        {
            RemoveShortcutDragGhost();

            var ghost = new Border
            {
                Width = 90,
                Height = 110,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(232, 250, 250, 250)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 80, 80, 80)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Opacity = 0.96
            };

            var panel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8
            };

            var iconContainer = new Grid
            {
                Width = 48,
                Height = 48
            };

            var icon = new FontIcon
            {
                Glyph = item.DisplayGlyph,
                FontSize = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconContainer.Children.Add(icon);

            var image = new Image
            {
                Width = 48,
                Height = 48,
                Stretch = Stretch.Uniform,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Tag = item.Id
            };
            iconContainer.Children.Add(image);

            var title = new TextBlock
            {
                Text = item.Name,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            panel.Children.Add(iconContainer);
            panel.Children.Add(title);
            ghost.Child = panel;

            _dragGhostCard = ghost;
            _dragGhostImage = image;
            GlobalDragLayer.Children.Add(ghost);
            UpdateShortcutDragGhostPosition(hostPoint);

            string? path = ResolveThumbnailPath(item);
            if (!string.IsNullOrEmpty(path))
            {
                string cacheKey = BuildThumbnailImageCacheKey(path, 96);
                if (_thumbnailImageCache.TryGetValue(cacheKey, out var cachedImage))
                {
                    image.Source = cachedImage;
                    image.Visibility = Visibility.Visible;
                    icon.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _ = LoadThumbnailAsync(item, icon, image, path, cacheKey);
                }
            }
        }

        private void UpdateShortcutDragGhostPosition(Point hostPoint)
        {
            if (_dragGhostCard == null)
                return;

            // 把 host 坐标转成窗口坐标
            var windowPoint = ShortcutGridHost
                .TransformToVisual(Content as UIElement)
                .TransformPoint(hostPoint);
            Canvas.SetLeft(_dragGhostCard, windowPoint.X - _dragGhostCard.Width / 2);
            Canvas.SetTop(_dragGhostCard, windowPoint.Y - _dragGhostCard.Height / 2);
        }

        private void RemoveShortcutDragGhost()
        {
            if (_dragGhostCard == null) return;

            GlobalDragLayer.Children.Remove(_dragGhostCard);   // ⚠️ 改成 GlobalDragLayer
            if (_dragGhostImage != null)
                _dragGhostImage.Tag = null;
            _dragGhostCard = null;
            _dragGhostImage = null;
        }

        //private void RefreshDraggedShortcutVisualVisibility()
        //{
        //    if (!_isShortcutDragging || _draggingShortcut == null)
        //        return;

        //    int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
        //    for (int i = 0; i < childCount; i++)
        //    {
        //        if (VisualTreeHelper.GetChild(ShortcutGrid, i) is not FrameworkElement element)
        //            continue;

        //        // ⚠️ 关键：用 ItemsRepeater 的索引反查数据项，不依赖 DataContext
        //        int index = ShortcutGrid.GetElementIndex(element);
        //        if (index < 0 || index >= CurrentShortcuts.Count)
        //            continue;

        //        bool isDragged = ReferenceEquals(CurrentShortcuts[index], _draggingShortcut);

        //        // 源卡片隐藏
        //        var host = FindChildByName<FrameworkElement>(element, "ShortcutMotionHost");
        //        if (host != null)
        //            host.Opacity = isDragged ? 0 : 1;

        //        // 占位框显示
        //        var placeholder = FindChildByName<FrameworkElement>(element, "ShortcutDragPlaceholder");
        //        if (placeholder != null)
        //            placeholder.Visibility = isDragged ? Visibility.Visible : Visibility.Collapsed;
        //    }
        //}

        //private void RestoreShortcutVisualsAfterDrag()
        //{
        //    int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
        //    for (int i = 0; i < childCount; i++)
        //    {
        //        if (VisualTreeHelper.GetChild(ShortcutGrid, i) is not FrameworkElement element)
        //            continue;

        //        if (FindChildByName<FrameworkElement>(element, "ShortcutMotionHost") is FrameworkElement host)
        //            host.Opacity = 1;

        //        if (FindChildByName<FrameworkElement>(element, "ShortcutDragPlaceholder") is FrameworkElement ph)
        //            ph.Visibility = Visibility.Collapsed;
        //    }
        //}

        private Category? HitCategoryByPoint(Point p)
        {
            for (int i = 0; i < NavMenuList.Items.Count; i++)
            {
                if (NavMenuList.Items[i] is not Category c || c.IsSeparator)
                    continue;
                if (NavMenuList.ContainerFromIndex(i) is not ListViewItem lvi)
                    continue;

                var origin = lvi.TransformToVisual(NavMenuList).TransformPoint(new Point(0, 0));
                var rect = new Rect(origin.X, origin.Y, lvi.ActualWidth, lvi.ActualHeight);
                if (rect.Contains(p))
                    return c;
            }
            return null;
        }

        private void UpdateDragCategoryVisual(Category? category)
        {
            if (ReferenceEquals(_dragCategoryTarget, category))
                return;

            _dragCategoryTarget = category;

            // 通过临时修改 NavMenuList.SelectedItem 来借用系统选中背景
            _isDragHoveringCategory = true;
            try
            {
                if (category != null)
                {
                    // 第一次进入某个分类时，记录原选中项
                    if (_dragCategoryOriginalSelection == null)
                    {
                        _dragCategoryOriginalSelection = NavMenuList.SelectedItem as Category;
                    }

                    NavMenuList.SelectedItem = category;
                }
                else
                {
                    // 离开所有分类 → 恢复原选中项
                    NavMenuList.SelectedItem = _dragCategoryOriginalSelection;
                }
            }
            finally
            {
                _isDragHoveringCategory = false;
            }
        }

        // ==================== 工具栏按钮 ====================

        private void ToolbarCut_Click(object sender, RoutedEventArgs e)
        {
            var selected = _selectedShortcuts.ToList();
            if (selected.Count == 0)
            {
                _ = ShowMessageAsync("请先选中至少一项");
                return;
            }
            CopyOrCutShortcuts(selected, isCut: true);
        }

        private void ToolbarCopy_Click(object sender, RoutedEventArgs e)
        {
            var selected = _selectedShortcuts.ToList();
            if (selected.Count == 0)
            {
                _ = ShowMessageAsync("请先选中至少一项");
                return;
            }
            CopyOrCutShortcuts(selected, isCut: false);
        }

        private async void ToolbarPaste_Click(object sender, RoutedEventArgs e)
        {
            await RefreshSystemClipboardCacheAsync();
            await PasteAsync();
        }

        private async void ToolbarRename_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedShortcuts.Count != 1)
            {
                await ShowMessageAsync("请先选中一项");
                return;
            }
            var s = _selectedShortcuts[0];
            await RenameShortcutAsync(s);
        }

        private async void ToolbarDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = _selectedShortcuts.ToList();
            if (selected.Count == 0)
            {
                await ShowMessageAsync("请先选中至少一项");
                return;
            }
            await DeleteShortcutsAsync(selected);
        }

        private async Task DeleteShortcutsAsync(List<ShortcutItem> shortcuts)
        {
            if (shortcuts.Count == 0) return;

            bool isCategoryView = _currentView == ViewKind.Category;
            bool isDefaultAppCategoryView = isCategoryView
                && _currentCategory is { } currentCategory
                && IsDefaultApplicationCategory(currentCategory);
            bool isAllAppsView = isCategoryView && IsAllAppsCategory(_currentCategory);
            bool deletesRecords = isCategoryView;
            // 分类视图里选中的全是应用程序时，用一个 4 按钮确认窗搞定，不再弹后续确认。
            bool allApplications = deletesRecords && shortcuts.All(item => !item.IsFile);
            bool deleteBackups = false;
            int deleteScope = 1;   // 1 = 只删当前范围；2 = 全部删除；3 = 全部删除并加入排除项

            if (ShouldConfirmDelete())
            {
                if (allApplications)
                {
                    int result = await ShowConfirmationWindowAsync(
                        "删除",
                        BuildApplicationDeleteDescription(shortcuts, isAllAppsView),
                        "删除",
                        secondaryText: "全部删除",
                        tertiaryText: "全部删除并加入排除项");
                    if (result == 0) return;
                    deleteScope = result;
                }
                else
                {
                    string description = !isCategoryView
                        ? _currentTabItem?.BuiltinKind switch
                        {
                            BuiltinTabKind.Favorites => "确定从收藏中移除选中的项目吗？分类中的卡片不会删除。",
                            BuiltinTabKind.Recent => "确定从最近访问中移除选中的项目吗？分类中的卡片不会删除。",
                            _ => "确定从当前标签中移除选中的项目吗？分类中的卡片不会删除。"
                        }
                        : shortcuts.Count == 1
                            ? LanguageService.TranslateFormat("确定从当前分类中删除「{name}」吗？", ("{name}", shortcuts[0].Name))
                            : LanguageService.TranslateFormat("确定从当前分类中删除选中的 {count} 项吗？", ("{count}", shortcuts.Count));

                    description = LanguageService.Translate(description);

                    if (isCategoryView && !isDefaultAppCategoryView && shortcuts.Count == 1 && shortcuts[0].IsShared)
                    {
                        string catName = ShortcutRepository.GetCategoryName(shortcuts[0].CategoryId);
                        description += "\n\n" + LanguageService.TranslateFormat("⚠️「{category}」是共享分类，移除后在所有模式中都将不再显示。", ("{category}", LanguageService.Translate(catName)));
                    }

                    int result = await ShowConfirmationWindowAsync(
                        "删除",
                        description,
                        deletesRecords ? "删除记录" : "移除",
                        secondaryText: deletesRecords ? "删除记录和备份" : null);
                    if (result == 0) return;
                    deleteBackups = deletesRecords && result == 2;
                }
            }

            bool excludePrograms = deleteScope == 3;
            var defaultCategoryIds = isAllAppsView
                ? BlockBox.Data.CategoryRepository.GetVisible(_currentMode)
                    .Where(IsDefaultApplicationCategory)
                    .Select(category => category.Id)
                    .ToList()
                : new List<string>();

            foreach (var shortcut in shortcuts)
            {
                if (allApplications)
                {
                    if (deleteScope >= 2)
                    {
                        // 全部删除：默认分类 + 自定义分类里的同名程序一起删。
                        RecycleBinRepository.MoveByTargetPath(shortcut.TargetPath);
                    }
                    else if (isAllAppsView)
                    {
                        // 全部应用：只删 4 个默认分类里的记录，自定义分类保留。
                        RecycleBinRepository.MoveByTargetPathInDefaultCategories(shortcut.TargetPath, defaultCategoryIds);
                    }
                    else
                    {
                        // 其他分类：只删当前分类这一条。
                        RecycleBinRepository.MoveToRecycleBin(shortcut.Id);
                    }
                }
                else
                {
                    DeleteShortcutFromCurrentView(shortcut, isAllAppsView, deleteAllCategories: false);
                }

                if (excludePrograms)
                {
                    string? path = ExcludedProgramRepository.GetExecutablePath(shortcut.TargetPath);
                    if (path != null && File.Exists(path))
                        ExcludedProgramRepository.Add(shortcut, path);
                }
                if (deleteBackups)
                    DeleteBackupIfUnreferenced(shortcut.BackupPath);
                CurrentShortcuts.Remove(shortcut);
            }

            UpdateEmptyHint();
            UpdateToolbarButtonStates();
            // 兜底：清掉收藏/最近访问/标签页里指向已删除记录的残留。
            ShortcutRepository.CleanupAfterShortcutDeletion();
            _ = ShowToastAsync(shortcuts.Count == 1
                ? LanguageService.TranslateFormat("已删除「{name}」", ("{name}", shortcuts[0].Name))
                : LanguageService.TranslateFormat("已删除 {count} 项", ("{count}", shortcuts.Count)));
        }

        /// <summary>应用删除确认窗的说明文字：四个按钮分别做什么。</summary>
        private StackPanel BuildApplicationDeleteDescription(List<ShortcutItem> shortcuts, bool isAllAppsView)
        {
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(new TextBlock
            {
                Text = shortcuts.Count == 1
                    ? LanguageService.TranslateFormat("确定删除「{name}」吗？", ("{name}", shortcuts[0].Name))
                    : LanguageService.TranslateFormat("确定删除选中的 {count} 个程序吗？", ("{count}", shortcuts.Count)),
                TextWrapping = TextWrapping.Wrap
            });

            void AddOption(string text)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75
                });
            }

            AddOption(LanguageService.Translate(isAllAppsView
                ? "删除：只从 4 个默认分类（全部应用/游戏/音乐/办公）移除，自定义分类保留。"
                : "删除：只从当前分类移除，其他分类（含其他默认分类和自定义分类）保留。"));
            AddOption(LanguageService.Translate("全部删除：同时删除所有分类中的同名程序。"));
            AddOption(LanguageService.Translate("全部删除并加入排除项：删除所有分类中的同名程序，并加入排除项，下次扫描不再自动添加。"));
            AddOption(LanguageService.Translate("取消：不做任何修改。"));
            return panel;
        }

        private void DeleteShortcutFromCurrentView(ShortcutItem shortcut, bool isAllAppsView = false,
            bool deleteAllCategories = false)
        {
            if (_currentView == ViewKind.Category)
            {
                if (isAllAppsView && deleteAllCategories)
                    RecycleBinRepository.MoveByTargetPath(shortcut.TargetPath);
                else
                    RecycleBinRepository.MoveToRecycleBin(shortcut.Id);   // 默认只删当前分类这一条
                return;
            }

            switch (_currentTabItem?.BuiltinKind ?? BuiltinTabKind.None)
            {
                case BuiltinTabKind.Favorites:
                    ShortcutRepository.SetFavorite(shortcut.Id, _currentMode, false);
                    break;
                case BuiltinTabKind.Recent:
                    ShortcutRepository.ClearRecent(shortcut.Id, _currentMode);
                    break;
                default:
                    ShortcutRepository.SetTitleId(shortcut.Id, _currentMode, "");
                    break;
            }
        }

        private static void DeleteBackupIfUnreferenced(string? backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath)
                || ShortcutRepository.HasBackupReference(backupPath)) return;
            string path = BackupPathHelper.MakeAbsolute(backupPath);
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private void RetireCategoryBackups(Category category, IEnumerable<ShortcutItem> removedItems)
        {
            if (category.IsShared) return;
            var paths = removedItems.Select(item => item.BackupPath)
                .Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
            if (paths.Any(ShortcutRepository.HasBackupReference)) return;
            BackupService.MarkCategoryFolderDeleted(category.BelongingMode ?? _currentMode, category.Id);
        }

        private void ToolbarFavorite_Click(object sender, RoutedEventArgs e)
        {
            var selected = _selectedShortcuts.ToList();
            if (selected.Count == 0)
            {
                _ = ShowMessageAsync("请先选中至少一项");
                return;
            }

            bool favorite = selected.Any(s => !s.IsFavorite);
            foreach (var shortcut in selected)
            {
                BlockBox.Data.ShortcutRepository.SetFavorite(shortcut.Id, _currentMode, favorite);
                shortcut.IsFavorite = favorite;
            }
            if (_currentView == ViewKind.Tab && _currentTabItem?.BuiltinKind == BuiltinTabKind.Favorites && !favorite)
                foreach (var shortcut in selected) CurrentShortcuts.Remove(shortcut);
            UpdateEmptyHint();
            UpdateToolbarButtonStates();
        }

        private enum NewFileKind
        {
            Text,
            Word,
            PowerPoint,
            Excel
        }

        private void AddFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsManualFileAddingDisabled()) return;

            // 全部应用 / 办公 / 音乐 / 游戏：只能添加应用程序，新建文档入口置灰。
            bool applicationsOnly = _currentView == ViewKind.Category
                && IsDefaultApplicationCategory(_currentCategory);

            var menu = new MenuFlyout();
            AddCreateFileMenuItem(menu, "新建 Text", NewFileKind.Text, "\uE8A5", enabled: !applicationsOnly);
            AddCreateFileMenuItem(menu, "新建 Word", NewFileKind.Word, "\uE8A5", enabled: !applicationsOnly);
            AddCreateFileMenuItem(menu, "新建 PPT", NewFileKind.PowerPoint, "\uE786", enabled: !applicationsOnly);
            AddCreateFileMenuItem(menu, "新建 Excel", NewFileKind.Excel, "\uE9F9", enabled: !applicationsOnly);
            menu.Items.Add(new MenuFlyoutSeparator());

            var addFile = new MenuFlyoutItem
            {
                Text = "添加文件",
                Icon = new FontIcon { Glyph = "\uE710" }
            };
            addFile.Click += async (_, _) => await AddExistingFilesAsync();
            menu.Items.Add(addFile);

            LanguageService.RegisterMenuFlyout(menu);
            menu.ShowAt(AddFileButton, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft
            });
        }

        private void AddCreateFileMenuItem(MenuFlyout menu, string text, NewFileKind kind, string glyph, bool enabled = true)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Icon = new FontIcon { Glyph = glyph },
                IsEnabled = enabled
            };
            item.Click += async (_, _) => await CreateNewDocumentAsync(kind);
            menu.Items.Add(item);
        }

        private async Task AddExistingFilesAsync()
        {
            var cat = await GetFileOperationCategoryAsync();
            if (cat == null) return;

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;

            var exts = BlockBox.Helpers.ContentKindHelper.GetFilePickerExtensions(
                cat.IsShared ? ContentKind.Application : cat.ContentKind,
                cat.IsShared ? string.Empty : cat.ExtensionList);
            foreach (var ext in exts) picker.FileTypeFilter.Add(ext);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;

            var paths = files.Select(f => f.Path).ToList();
            await BatchAddShortcutsAsync(paths, cat);
        }

        private async Task<Category?> GetFileOperationCategoryAsync()
        {
            if (!string.IsNullOrEmpty(_searchKeyword))
            {
                await ShowMessageAsync("搜索状态下不能添加文件，请先清空搜索");
                return null;
            }

            if (NavMenuList.SelectedItem is not Category cat || cat.IsSeparator)
            {
                await ShowMessageAsync("请先选择一个分类");
                return null;
            }
            if (IsAllAppsCategory(cat))
            {
                // “全部应用”现在也允许手动添加，但只接受应用程序（由 BatchAddShortcutsAsync 的类型校验兜底）。
                return cat;
            }
            if (cat.IsShared && !IsDefaultApplicationCategory(cat))
            {
                await ShowMessageAsync("该默认分类不支持手动添加程序。");
                return null;
            }

            return cat;
        }

        private async Task CreateNewDocumentAsync(NewFileKind kind)
        {
            var cat = await GetFileOperationCategoryAsync();
            if (cat == null) return;

            // 默认分类只能添加应用程序；菜单项虽已置灰，这里再兜一层（快捷键/其它入口）。
            if (IsDefaultApplicationCategory(cat))
            {
                await ShowMessageAsync(LanguageService.Translate("默认分类只能添加应用程序。"));
                return;
            }

            var (baseName, extension) = kind switch
            {
                NewFileKind.Text => ("新建Text", ".txt"),
                NewFileKind.Word => ("新建Word", ".docx"),
                NewFileKind.PowerPoint => ("新建PPT", ".pptx"),
                NewFileKind.Excel => ("新建Excel", ".xlsx"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

            var contentKind = cat.IsShared ? ContentKind.Application : cat.ContentKind;
            var customExtensions = cat.IsShared ? string.Empty : cat.ExtensionList;
            var testPath = baseName + extension;
            if (!BlockBox.Helpers.ContentKindHelper.Matches(testPath, contentKind, customExtensions))
            {
                await ShowMessageAsync("当前分类不支持此文件类型");
                return;
            }

            try
            {
                string folder = System.IO.Path.Combine(
                    BlockBox.Helpers.AppDataPaths.Root,
                    "CreatedFiles");
                Directory.CreateDirectory(folder);

                string path = GetUniqueNewFilePath(folder, baseName, extension);
                if (kind == NewFileKind.Text)
                    File.WriteAllText(path, string.Empty);
                else
                    BlockBox.Services.OfficeDocumentCreator.Create(path, extension);

                await BatchAddShortcutsAsync(new List<string> { path }, cat);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(LanguageService.TranslateFormat("创建文件失败：{error}", ("{error}", ex.Message)));
            }
        }

        private static string GetUniqueNewFilePath(string folder, string baseName, string extension)
        {
            string path = System.IO.Path.Combine(folder, baseName + extension);
            for (int suffix = 2; File.Exists(path); suffix++)
                path = System.IO.Path.Combine(folder, $"{baseName} ({suffix}){extension}");
            return path;
        }

        /// <summary>
        /// 批量添加：类型校验 + 去重 + 备份 + 写库
        /// </summary>
        private async Task BatchAddShortcutsAsync(List<string> paths, Category cat)
        {
            var existingRows = BlockBox.Data.ShortcutRepository.GetByCategory(_currentMode, cat.Id)
                .Where(s => !string.IsNullOrEmpty(s.TargetPath))
                .ToList();
            var existing = existingRows
                .Select(s => BlockBox.Helpers.ProgramIdentity.Get(s.TargetPath, s.Arguments))
                .ToHashSet(StringComparer.Ordinal);
            var existingExeKeys = existingRows
                .Select(s => BlockBox.Helpers.ProgramIdentity.ResolveTarget(s.TargetPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int added = 0, rejected = 0, duplicated = 0;
            var dupNames = new List<string>();
            var addedNames = new List<string>();

            foreach (var path in paths)
            {
                string identity = BlockBox.Helpers.ProgramIdentity.Get(path);
                string exeKey = BlockBox.Helpers.ProgramIdentity.ResolveTarget(path);
                var shortcutInfo = BlockBox.Services.InstalledProgramScanner.ResolveShortcutInfo(path);
                bool benignArguments = shortcutInfo == null
                    || BlockBox.Services.InstalledProgramScanner.IsBenignLaunchArgument(shortcutInfo.Arguments);
                if (existing.Contains(identity) || (benignArguments && existingExeKeys.Contains(exeKey)))
                {
                    duplicated++;
                    dupNames.Add(System.IO.Path.GetFileName(path));
                    continue;
                }

                if (!BlockBox.Helpers.ContentKindHelper.Matches(
                        path,
                        cat.IsShared ? ContentKind.Application : cat.ContentKind,
                        cat.IsShared ? string.Empty : cat.ExtensionList))
                {
                    rejected++;
                    continue;
                }

                bool ok = await AddOneShortcutAsync(path, cat);
                if (ok)
                {
                    added++;
                    existing.Add(identity);
                    existingExeKeys.Add(exeKey);
                    addedNames.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                }
            }

            UpdateEmptyHint();

            if (added > 0)
            {
                _ = ShowToastAsync(added == 1
                    ? LanguageService.TranslateFormat("已成功添加「{name}」", ("{name}", addedNames[0]))
                    : LanguageService.TranslateFormat("已成功添加 {count} 项", ("{count}", added)));
            }

            var msgs = new List<string>();
            if (added > 0) msgs.Add(LanguageService.TranslateFormat("已添加 {count} 个", ("{count}", added)));
            if (duplicated > 0) msgs.Add(LanguageService.TranslateFormat("{count} 个已存在（跳过）", ("{count}", duplicated)));
            if (rejected > 0) msgs.Add(LanguageService.TranslateFormat("{count} 个类型不匹配（拒绝）", ("{count}", rejected)));

            if (duplicated > 0 && added == 0)
            {
                string list = string.Join("\n",
                    dupNames.Take(8).Select(n => "• " + n));
                if (dupNames.Count > 8) list += "\n" + LanguageService.TranslateFormat("... 共 {count} 个", ("{count}", dupNames.Count));
                _ = ShowMessageAsync(LanguageService.TranslateFormat("以下文件在当前分类中已存在：\n\n{list}", ("{list}", list)));
            }
            else if (msgs.Count > 0 && (duplicated > 0 || rejected > 0))
            {
                _ = ShowResultMessageAsync(
          string.Join(LanguageService.Translate("，"), msgs) + LanguageService.Translate("。"),
         hasWarnings: duplicated > 0 || rejected > 0);
            }
        }
        private async Task<bool> AddOneShortcutAsync(string filePath, Category cat)
        {
            try
            {
                bool isApp = BlockBox.Helpers.ContentKindHelper.IsApplication(filePath);

                BlockBox.Services.BackupResult backup;
                if (isApp)
                {
                    // ⭐ 应用程序 / 快捷方式：不备份
                    backup = new BlockBox.Services.BackupResult
                    {
                        Success = true,
                        IsBackedUp = false,
                        Md5 = "",
                        BackupAbsolutePath = "",
                        BackupRelativePath = ""
                    };
                }
                else
                {
                    // ⭐ 普通文件：备份
                    backup = await BlockBox.Services.BackupService.BackupAsync(
                        filePath, _currentMode, cat.Id);

                    if (!backup.Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[添加] 备份失败: {backup.Error}");
                        return false;
                    }
                }

                var shortcutInfo = BlockBox.Services.InstalledProgramScanner.ResolveShortcutInfo(filePath);
                var item = new ShortcutItem
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                    TargetPath = filePath,
                    Arguments = shortcutInfo?.Arguments ?? "",
                    WorkingDirectory = shortcutInfo?.WorkingDirectory ?? "",
                    CategoryId = cat.Id,
                    IsShared = cat.IsShared,
                    BelongingMode = cat.BelongingMode ?? _currentMode,
                    IsFile = !isApp,                             // ⭐ 应用 → false，文件 → true
                    SortOrder = BlockBox.Data.ShortcutRepository.GetNextSortOrder(cat.Id, cat.IsShared),

                    BackupPath = backup.BackupRelativePath,
                    SourceMd5 = backup.Md5,
                    SourceState = isApp ? SourceState.Unknown : SourceState.Valid,
                    LastVerifiedTime = isApp ? (DateTime?)null : DateTime.Now,
                    IsBackedUp = backup.IsBackedUp,
                    CreatedTime = DateTime.Now,   // ⭐ 新增
                };

                BlockBox.Data.ShortcutRepository.Add(item);
                if (cat.IsShared && !IsAllAppsCategory(cat))
                    AddToAllAppsIfMissing(item);

                await RememberProgramCategoryAsync(item, cat);

                if (_currentView == ViewKind.Category && _currentCategory?.Id == cat.Id)
                    CurrentShortcuts.Add(item);
                return true;
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
                when (ex.SqliteErrorCode == 19)   // 19 = SQLITE_CONSTRAINT
            {
                System.Diagnostics.Debug.WriteLine($"[添加] 唯一约束冲突：{ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[添加] 异常: {ex.Message}");
                return false;
            }
        }

        private void ShortcutGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (GetShortcutFromEventSource(e.OriginalSource) is ShortcutItem s)
            {
                _ = LaunchShortcutAsync(s);
            }
        }

        private async Task LaunchShortcutAsync(ShortcutItem s)
        {

            if (!TryResolveShortcutLaunch(s, out string launchPath, out bool isShellShortcut, out string error))
            {
                await ShowMessageAsync(error);
                return;
            }

            // ⭐ 判断是不是 Store 应用
            if (launchPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{launchPath}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync(LanguageService.TranslateFormat("启动失败：{error}", ("{error}", ex.Message)));
                }
                return;
            }

            if (!System.IO.File.Exists(launchPath))
            {
                await ShowMessageAsync(LanguageService.TranslateFormat("文件不存在，备份也不可用：{name}", ("{name}", s.Name)));
                return;
            }

            try
            {
                if (Path.GetExtension(launchPath).Equals(".Ink", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"已阻止直接启动异常快捷方式路径：{launchPath}");

                string workingDirectory = isShellShortcut
                    ? ""
                    : string.IsNullOrEmpty(s.WorkingDirectory)
                        ? System.IO.Path.GetDirectoryName(launchPath) ?? ""
                        : s.WorkingDirectory;

                if (isShellShortcut)
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{launchPath}\"",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Debug.WriteLine(
                        $"[启动] 通过 Explorer 打开快捷方式; Name={s.Name}; FileName={startInfo.FileName}; Arguments={startInfo.Arguments}");
                    System.Diagnostics.Process.Start(startInfo);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[启动] 普通启动; Name={s.Name}; FileName={launchPath}; WorkingDirectory={workingDirectory}; ShellShortcut={isShellShortcut}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = launchPath,
                        Arguments = s.Arguments ?? "",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true
                    });
                }

                var now = DateTime.Now;
                BlockBox.Data.ShortcutRepository.UpdateAccessInfo(s.Id, _currentMode, now);
                s.LastAccessedTime = now;
                s.AccessCount++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[启动] 普通启动异常; Name={s.Name}; Target={launchPath}; ErrorCode={(ex as System.ComponentModel.Win32Exception)?.NativeErrorCode}; HResult=0x{ex.HResult:X8}; {ex}");
                await ShowMessageAsync(LanguageService.TranslateFormat("启动失败：{error}", ("{error}", ex.Message)));
            }
        }
        /// <summary>
        /// 把 Shortcut 固定到某个用户标签（改 TitleId）
        /// </summary>
        private async Task PinToTabAsync(ShortcutItem s, MyTabItem tab)
        {
            // 类型校验
            if (!BlockBox.Helpers.ContentKindHelper.Matches(
                    s.TargetPath, tab.ContentKind, tab.ExtensionList))
            {
                await ShowMessageAsync(
                    $"「{tab.Title}」只接受「{ContentKindHelper.GetDisplayName(tab.ContentKind)}」类型");
                return;
            }

            BlockBox.Data.ShortcutRepository.SetTitleId(s.Id, _currentMode, tab.Id);
            s.TitleId = tab.Id;
            if (_currentView == ViewKind.Tab && _currentTabItem?.Id == tab.Id && !CurrentShortcuts.Contains(s))
                CurrentShortcuts.Add(s);
            System.Diagnostics.Debug.WriteLine($"[标签] {s.Name} → {tab.Title}");
        }


        /// <summary>
        /// 从当前标签中移除（清空 TitleId 引用）
        /// </summary>
        private void RemoveFromCurrentTab(ShortcutItem s)
        {
            BlockBox.Data.ShortcutRepository.SetTitleId(s.Id, _currentMode, "");
            s.TitleId = "";
            CurrentShortcuts.Remove(s);
            UpdateEmptyHint();
            System.Diagnostics.Debug.WriteLine($"[标签] {s.Name} 已从当前标签移除");
        }

        /// <summary>
        /// 添加到分类（复制一份记录到目标分类，备份共享）
        /// </summary>
        private async Task AddToCategoryAsync(ShortcutItem s, Category cat)
        {
            if (!CanAddShortcutToCategory(s, cat)) return;
            // 类型校验
            if (!BlockBox.Helpers.ContentKindHelper.Matches(
                    s.TargetPath, cat.ContentKind, cat.ExtensionList))
            {
                await ShowMessageAsync(
                    $"「{cat.Name}」只接受「{ContentKindHelper.GetDisplayName(cat.ContentKind)}」类型");
                return;
            }

            var targetMode = cat.BelongingMode ?? _currentMode;

            // 目标分类里已有
            if (BlockBox.Data.ShortcutRepository.ExistsInCategory(cat.Id, targetMode, s.TargetPath))
            {
                await RememberProgramCategoryAsync(s, cat);
                await ShowMessageAsync(LanguageService.TranslateFormat("「{category}」中已存在该文件", ("{category}", cat.DisplayName)));
                return;
            }

            // 复制记录（备份共享）
            var newItem = new ShortcutItem
            {
                Name = s.Name,
                TargetPath = s.TargetPath,
                Arguments = s.Arguments,
                WorkingDirectory = s.WorkingDirectory,
                IconPath = s.IconPath,
                CategoryId = cat.Id,
                TitleId = "",                                 // 新记录不继承标签
                IsFile = s.IsFile,
                IsShared = cat.IsShared,
                BelongingMode = targetMode,
                SortOrder = BlockBox.Data.ShortcutRepository.GetNextSortOrder(cat.Id, cat.IsShared),
                BackupPath = s.BackupPath,
                SourceMd5 = s.SourceMd5,
                SourceState = s.SourceState,
                LastVerifiedTime = s.LastVerifiedTime,
                IsBackedUp = s.IsBackedUp,
                CreatedTime = DateTime.Now
            };

            try
            {
                BlockBox.Data.ShortcutRepository.Add(newItem);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                await ShowMessageAsync(LanguageService.TranslateFormat("「{category}」中已存在该文件", ("{category}", cat.DisplayName)));
                return;
            }

            await RememberProgramCategoryAsync(newItem, cat);
            if (_currentView == ViewKind.Category && NavMenuList.SelectedItem is Category current && current.Id == cat.Id)
                CurrentShortcuts.Add(newItem);
            UpdateEmptyHint();
            System.Diagnostics.Debug.WriteLine($"[分类] {s.Name} → {cat.Name}");
        }
        /// <summary>
        /// 把若干 Shortcut 移动到目标分类（原分类移除）
        /// </summary>
        private async Task MoveToCategoryAsync(List<ShortcutItem> items, Category cat)
        {
            if (items.Count == 0) return;

            var targetMode = cat.BelongingMode ?? _currentMode;
            int moved = 0, rejected = 0, duplicated = 0;
            var movedItems = new List<ShortcutItem>();

            foreach (var s in items)
            {
                if (!CanMoveShortcutToCategory(s, cat) || (!cat.IsShared && s.BelongingMode != targetMode))
                {
                    rejected++;
                    continue;
                }
                // 已经在目标分类里 → 跳过
                if (s.CategoryId == cat.Id) continue;

                // 类型校验
                if (!BlockBox.Helpers.ContentKindHelper.Matches(
                        s.TargetPath, cat.ContentKind, cat.ExtensionList))
                {
                    rejected++;
                    continue;
                }

                // 目标分类里已有同路径 → 跳过
                if (BlockBox.Data.ShortcutRepository.ExistsInCategory(
                        cat.Id, targetMode, s.TargetPath))
                {
                    duplicated++;
                    continue;
                }

                try
                {
                    BlockBox.Data.ShortcutRepository.MoveToCategory(
                        s.Id,
                        cat.Id,
                        BlockBox.Data.ShortcutRepository.GetNextSortOrder(cat.Id, cat.IsShared));
                    movedItems.Add(s);
                    moved++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[移动] 失败: {ex.Message}");
                    rejected++;
                }
            }

            foreach (var item in movedItems)
                await RememberProgramCategoryAsync(item, cat, isMove: true);

            // 保留仍在当前页面的卡片，只移除已成功移走的记录。
            foreach (var item in movedItems)
                CurrentShortcuts.Remove(item);
            UpdateEmptyHint();
            UpdateToolbarButtonStates();

            // 只在有部分失败时提示；全成功就静默
            if (rejected > 0 || duplicated > 0)
            {
                var msgs = new List<string>();
                if (moved > 0) msgs.Add($"已移动 {moved} 项");
                if (duplicated > 0) msgs.Add($"{duplicated} 项目标分类中已存在");
                if (rejected > 0) msgs.Add($"{rejected} 项类型不匹配或失败");
                await ShowResultMessageAsync(string.Join("，", msgs) + "。", hasWarnings: true);
            }
        }

        private static bool IsAllAppsCategory(Category? category)
            => category is { IsShared: true } && string.Equals(category.Name, "全部应用", StringComparison.Ordinal);

        private async Task RememberProgramCategoryAsync(ShortcutItem shortcut, Category category, bool isMove = false)
        {
            try
            {
                var sourceCategory = isMove ? GetShortcutSourceCategory(shortcut) : null;
                ProgramCategoryHistory.Remember(shortcut, category, sourceCategory);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(LanguageService.TranslateFormat("分类记录保存失败：{error}", ("{error}", ex.Message)));
            }
        }

        private static bool IsDefaultApplicationCategory(Category? category)
            => category is { IsShared: true }
                && category.Name is "全部应用" or "办公" or "音乐" or "游戏";

        private void AddToAllAppsIfMissing(ShortcutItem source)
        {
            var allApps = BlockBox.Data.CategoryRepository.GetVisible(_currentMode)
                .FirstOrDefault(IsAllAppsCategory);
            if (allApps == null)
                return;

            string sourceIdentity = BlockBox.Helpers.ProgramIdentity.Get(source.TargetPath, source.Arguments);
            string sourceExeKey = BlockBox.Helpers.ProgramIdentity.ResolveTarget(source.TargetPath);
            bool benignSourceArguments = BlockBox.Services.InstalledProgramScanner.IsBenignLaunchArgument(source.Arguments);
            bool alreadyExists = BlockBox.Data.ShortcutRepository.GetByCategory(_currentMode, allApps.Id)
                .Any(item => string.Equals(
                        BlockBox.Helpers.ProgramIdentity.Get(item.TargetPath, item.Arguments),
                        sourceIdentity,
                        StringComparison.Ordinal)
                    || (benignSourceArguments
                        && string.Equals(
                            BlockBox.Helpers.ProgramIdentity.ResolveTarget(item.TargetPath),
                            sourceExeKey,
                            StringComparison.OrdinalIgnoreCase)));
            if (alreadyExists)
                return;

            BlockBox.Data.ShortcutRepository.Add(new ShortcutItem
            {
                Id = Guid.NewGuid().ToString(),
                Name = source.Name,
                TargetPath = source.TargetPath,
                Arguments = source.Arguments,
                WorkingDirectory = source.WorkingDirectory,
                IconPath = source.IconPath,
                CategoryId = allApps.Id,
                IsShared = true,
                IsFile = false,
                SortOrder = BlockBox.Data.ShortcutRepository.GetNextSortOrder(allApps.Id, shared: true),
                BackupPath = string.Empty,
                SourceMd5 = string.Empty,
                SourceState = SourceState.Unknown,
                IsBackedUp = false,
                CreatedTime = source.CreatedTime
            });
        }

        private Category? GetShortcutSourceCategory(ShortcutItem shortcut)
            => BlockBox.Data.CategoryRepository.GetVisible(_currentMode)
                .FirstOrDefault(category => category.Id == shortcut.CategoryId);

        private bool CanMoveShortcutToCategory(ShortcutItem shortcut, Category target)
        {
            if (target.Id == shortcut.CategoryId) return false;
            if (!shortcut.IsShared)
                return !target.IsShared && target.BelongingMode == _currentMode
                    && shortcut.BelongingMode == _currentMode;

            var source = GetShortcutSourceCategory(shortcut);
            if (IsAllAppsCategory(source) || !target.IsShared || IsAllAppsCategory(target)) return false;
            return true;
        }

        private bool CanAddShortcutToCategory(ShortcutItem shortcut, Category target)
        {
            if (target.Id == shortcut.CategoryId) return false;
            if (!target.IsShared)
                return true;
            if (!shortcut.IsShared) return false;
            return !IsAllAppsCategory(target);
        }

        private bool CanDropShortcutToCategory(ShortcutItem shortcut, Category target)
        {
            if (!shortcut.IsShared)
                return !target.IsShared && target.BelongingMode == _currentMode
                    && shortcut.BelongingMode == _currentMode && target.Id != shortcut.CategoryId;

            var source = GetShortcutSourceCategory(shortcut);
            if (!target.IsShared || target.Id == shortcut.CategoryId || IsAllAppsCategory(target)) return false;
            return source != null;
        }

        private bool ShouldCopyShortcutOnDrop(ShortcutItem shortcut)
            => shortcut.IsShared && IsAllAppsCategory(GetShortcutSourceCategory(shortcut));

        private static void NormalizeMenuFlyoutSeparators(MenuFlyout flyout)
        {
            bool hasEnabledItemAfter = false;
            bool separatorAfterEnabledItem = false;

            for (int i = flyout.Items.Count - 1; i >= 0; i--)
            {
                if (flyout.Items[i] is MenuFlyoutSeparator)
                {
                    if (!hasEnabledItemAfter || separatorAfterEnabledItem)
                    {
                        flyout.Items.RemoveAt(i);
                    }
                    else
                    {
                        separatorAfterEnabledItem = true;
                    }
                }
                else if (flyout.Items[i] is Control element
                         && element.Visibility == Visibility.Visible
                         && element.IsEnabled)
                {
                    hasEnabledItemAfter = true;
                    separatorAfterEnabledItem = false;
                }
            }

            bool hasEnabledItemBefore = false;
            for (int i = 0; i < flyout.Items.Count; i++)
            {
                if (flyout.Items[i] is MenuFlyoutSeparator)
                {
                    if (!hasEnabledItemBefore)
                    {
                        flyout.Items.RemoveAt(i--);
                    }
                }
                else if (flyout.Items[i] is Control element
                         && element.Visibility == Visibility.Visible
                         && element.IsEnabled)
                {
                    hasEnabledItemBefore = true;
                }
            }
        }

        private async void ShortcutGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {// ⭐ 先立刻抓位置，await 之后事件参数可能失效
            var clickPosition = e.GetPosition(ShortcutGrid);

            // ⭐ 刷新系统剪贴板缓存
            await RefreshSystemClipboardCacheAsync();

            var clickedShortcut = GetShortcutFromEventSource(e.OriginalSource)
                ?? GetShortcutAtPoint(clickPosition);

            // ⭐ 右键点击卡片：如果它不在当前选中集合里，就把它设为唯一选中
            if (clickedShortcut is ShortcutItem clicked
                && !_selectedShortcuts.Contains(clicked))
            {
                UpdateShortcutSelection(clicked, toggle: false);
            }

            var flyout = new MenuFlyout();

            // ==================== 项上右键 ====================
            if (clickedShortcut is ShortcutItem s)
            {
                // ⭐ 若右键的项已在多选集合里 → 操作整个选区；否则只操作单项
                List<ShortcutItem> targets =
                    _selectedShortcuts.Contains(s) && _selectedShortcuts.Count > 1
                        ? _selectedShortcuts.ToList()   // 快照，避免后续清空影响
                        : new List<ShortcutItem> { s };

                string countSuffix = targets.Count > 1
                    ? LanguageService.TranslateFormat("（{count} 项）", ("{count}", targets.Count)) : "";
                bool multi = targets.Count > 1;
                // ---- 打开（单项才有意义）----
                var openItem = new MenuFlyoutItem
                {
                    Text = "打开",
                    Icon = new FontIcon { Glyph = "\uE8E5" }
                };
                openItem.Click += async (a, b) => await LaunchShortcutAsync(s);
                flyout.Items.Add(openItem);

                if (string.IsNullOrWhiteSpace(s.TargetPath)
                    || !s.TargetPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                {
                    var adminItem = new MenuFlyoutItem
                    {
                        Text = "以管理员身份运行",
                        Icon = new FontIcon { Glyph = "\uE7EF" }
                    };
                    adminItem.Click += async (a, b) => await LaunchShortcutAsAdministratorAsync(s);
                    flyout.Items.Add(adminItem);
                }

                // ---- 复制（多选：复制全部）----
                var copyItem = new MenuFlyoutItem
                {
                    Text = targets.Count > 1
                        ? LanguageService.TranslateFormat("复制（{count} 项）", ("{count}", targets.Count))
                        : LanguageService.Translate("复制"),
                    Icon = new FontIcon { Glyph = "\uE8C8" }
                };
                copyItem.Click += (a, b) => CopyOrCutShortcuts(targets, isCut: false);
                flyout.Items.Add(copyItem);

                // ---- 剪切（多选：剪切全部）----
                var cutItem = new MenuFlyoutItem
                {
                    Text = targets.Count > 1
                        ? LanguageService.TranslateFormat("剪切（{count} 项）", ("{count}", targets.Count))
                        : LanguageService.Translate("剪切"),
                    Icon = new FontIcon { Glyph = "\uE8C6" }
                };
                cutItem.Click += (a, b) => CopyOrCutShortcuts(targets, isCut: true);
                flyout.Items.Add(cutItem);


                // ---- 收藏/取消收藏 ----
                bool isFav = s.IsFavorite;
                if (_currentTabItem?.BuiltinKind == BuiltinTabKind.Favorites)
                    isFav = true;

                var favItem = new MenuFlyoutItem
                {
                    Text = isFav ? "取消收藏" : "加入收藏",
                    Icon = new FontIcon { Glyph = isFav ? "\uE735" : "\uE734" }
                };
                favItem.Click += (a, b) =>
                {
                    s.IsFavorite = !isFav;
                    BlockBox.Data.ShortcutRepository.SetFavorite(s.Id, _currentMode, s.IsFavorite);
                    if (_currentTabItem?.BuiltinKind == BuiltinTabKind.Favorites && !s.IsFavorite)
                        CurrentShortcuts.Remove(s);
                    UpdateEmptyHint();
                    UpdateToolbarButtonStates();
                };
                flyout.Items.Add(favItem);

                // ---- 从"最近访问"移除 ----
                if (_currentTabItem?.BuiltinKind == BuiltinTabKind.Recent)
                {
                    var removeRecent = new MenuFlyoutItem
                    {
                        Text = "从最近访问中移除",
                        Icon = new FontIcon { Glyph = "\uE74D" }
                    };
                    removeRecent.Click += (a, b) =>
                    {
                        BlockBox.Data.ShortcutRepository.ClearRecent(s.Id, _currentMode);
                        CurrentShortcuts.Remove(s);
                        UpdateEmptyHint();
                    };
                    flyout.Items.Add(removeRecent);
                }

                flyout.Items.Add(new MenuFlyoutSeparator());

                // ---- 固定到标签（子菜单）----
                var userTabs = BlockBox.Data.TabRepository.GetVisible(_currentMode)
                    .Where(t => t.BuiltinKind == BuiltinTabKind.None)
                    .ToList();

                if (userTabs.Count > 0)
                {
                    var tabSub = new MenuFlyoutSubItem
                    {
                        Text = "固定到标签",
                        Icon = new FontIcon { Glyph = "\uE734" }
                    };

                    foreach (var tab in userTabs)
                    {
                        bool already = s.TitleId == tab.Id;
                        var item = new MenuFlyoutItem
                        {
                            Text = tab.Title,
                            IsEnabled = !already
                        };
                        if (already)
                            item.Icon = new FontIcon { Glyph = "\uE73E" };

                        var capturedTab = tab;
                        item.Click += async (a, b) => await PinToTabAsync(s, capturedTab);
                        tabSub.Items.Add(item);
                    }
                    flyout.Items.Add(tabSub);
                }

                // ---- 从当前标签中移除（仅用户标签视图下）----
                if (_currentView == ViewKind.Tab
                    && _currentTabItem != null
                    && _currentTabItem.BuiltinKind == BuiltinTabKind.None
                    && !string.IsNullOrEmpty(s.TitleId))
                {
                    var removeTabItem = new MenuFlyoutItem
                    {
                        Text = "从当前标签中移除",
                        Icon = new FontIcon { Glyph = "\uE711" }
                    };
                    removeTabItem.Click += (a, b) => RemoveFromCurrentTab(s);
                    flyout.Items.Add(removeTabItem);
                }

                // ---- 添加到分类（子菜单）----
                var allCats = BlockBox.Data.CategoryRepository.GetVisible(_currentMode)
                    .Where(c => !c.IsSeparator)
                    .ToList();

                var destinationModeCats = ModeRepository.GetAllModes()
                    .SelectMany(mode => BlockBox.Data.CategoryRepository.GetVisible(mode)
                        .Where(c => !c.IsSeparator && !c.IsShared)
                        .Select(c => (Mode: mode, Category: c)))
                    .GroupBy(x => x.Mode + ":" + x.Category.Id)
                    .Select(g => g.First()).ToList();
                var addDestinations = new List<(string Text, Category Category)>();
                if (s.IsShared)
                {
                    addDestinations.AddRange(allCats
                        .Where(category => category.IsShared && CanAddShortcutToCategory(s, category))
                        .Select(category => (LanguageService.TranslateFormat("{mode} / {category}",
                            ("{mode}", LanguageService.Translate("默认")), ("{category}", category.DisplayName)), category)));
                }
                addDestinations.AddRange(destinationModeCats
                    .Where(destination => CanAddShortcutToCategory(s, destination.Category))
                    .Select(destination =>
                    {
                        string modeName = ModeRepository.GetDisplayName(destination.Mode);
                        return (LanguageService.TranslateFormat("{mode} / {category}",
                            ("{mode}", modeName), ("{category}", destination.Category.DisplayName)), destination.Category);
                    }));

                if (addDestinations.Count > 0)
                {
                    var catSub = new MenuFlyoutSubItem
                    {
                        Text = LanguageService.Translate("添加到分类"),
                        Icon = new FontIcon { Glyph = "\uE8B7" }
                    };

                    foreach (var destination in addDestinations)
                    {
                        var cat = destination.Category;
                        var item = new MenuFlyoutItem { Text = destination.Text };
                        var capturedCat = cat;
                        item.Click += async (a, b) => await AddToCategoryAsync(s, capturedCat);
                        catSub.Items.Add(item);
                    }

                    if (catSub.Items.Count > 0)
                        flyout.Items.Add(catSub);
                }
                // ---- 移动到分类（子菜单）----
                var moveCats = allCats.Where(cat => targets.All(target => CanMoveShortcutToCategory(target, cat))).ToList();
                if (moveCats.Count > 0)
                {
                    var moveSub = new MenuFlyoutSubItem
                    {
                        Text = targets.Count > 1
                            ? LanguageService.TranslateFormat("移动到分类 {countSuffix}", ("{countSuffix}", countSuffix))
                            : LanguageService.Translate("移动到分类"),
                        Icon = new FontIcon { Glyph = "\uE8DE" }
                    };

                    foreach (var cat in moveCats)
                    {
                        if (cat.Id == s.CategoryId) continue;   // 排除当前所在分类

                        var item = new MenuFlyoutItem { Text = cat.DisplayName };
                        var capturedCat = cat;
                        item.Click += async (a, b) => await MoveToCategoryAsync(targets, capturedCat);
                        moveSub.Items.Add(item);
                    }

                    if (moveSub.Items.Count > 0)
                        flyout.Items.Add(moveSub);
                }

                // ---- 所在分类（子菜单）----
                var catName = BlockBox.Data.ShortcutRepository.GetCategoryName(s.CategoryId);
                var locateSub = new MenuFlyoutSubItem
                {
                    Text = "所在的分类",
                    Icon = new FontIcon { Glyph = "\uE8B7" }
                };
                var jumpItem = new MenuFlyoutItem
                {
                    Text = string.IsNullOrEmpty(catName) ? "（已删除）" : catName,
                    IsEnabled = !string.IsNullOrEmpty(catName)
                };
                jumpItem.Click += (a, b) => JumpToCategory(s.CategoryId);
                locateSub.Items.Add(jumpItem);
                flyout.Items.Add(locateSub);

                flyout.Items.Add(new MenuFlyoutSeparator());

                // ---- 资源管理器显示 ----
                var revealItem = new MenuFlyoutItem
                {
                    Text = "在资源管理器中显示",
                    Icon = new FontIcon { Glyph = "\uE838" }
                };
                revealItem.Click += (a, b) => RevealInExplorer(s);
                flyout.Items.Add(revealItem);

                // ---- 重命名 ----
                var renameItem = new MenuFlyoutItem
                {
                    Text = "重命名",
                    Icon = new FontIcon { Glyph = "\uE8AC" }
                };
                renameItem.Click += async (a, b) => await RenameShortcutAsync(s);
                flyout.Items.Add(renameItem);

                var propertiesItem = new MenuFlyoutItem
                {
                    Text = "属性",
                    Icon = new FontIcon { Glyph = "\uE946" }
                };
                propertiesItem.Click += (a, b) => ShowShortcutProperties(s);
                flyout.Items.Add(propertiesItem);

                flyout.Items.Add(new MenuFlyoutSeparator());

                // ---- 删除 ----
                var deleteItem = new MenuFlyoutItem
                {
                    Text = targets.Count > 1
                        ? LanguageService.TranslateFormat("删除（{count} 项）", ("{count}", targets.Count))
                        : "删除",
                    Icon = new FontIcon { Glyph = "\uE74D" }
                };
                deleteItem.Click += async (a, b) => await DeleteShortcutsAsync(targets);
                flyout.Items.Add(deleteItem);
            }
            // ==================== 空白处右键 ====================
            else
            {
                bool canSort =
                    (_currentView == ViewKind.Category
                        && NavMenuList.SelectedItem is Category c
                        && !c.IsSeparator)
                    ||
                    (_currentView == ViewKind.Tab
                        && _currentTabItem != null
                        && _currentTabItem.BuiltinKind == BuiltinTabKind.None);

                bool canAdd =
                    _currentView == ViewKind.Category
                    && NavMenuList.SelectedItem is Category catAdd
                    && !catAdd.IsSeparator
                    && !IsAllAppsCategory(catAdd);

                // ---- 排序方式 ----
                if (canSort)
                {
                    var sortSub = new MenuFlyoutSubItem
                    {
                        Text = "排序方式",
                        Icon = new FontIcon { Glyph = "\uE8CB" }
                    };

                    var currentSort = GetCurrentSortMode();

                    var byName = new MenuFlyoutItem { Text = "按名称" };
                    if (currentSort == SortMode.ByName)
                        byName.Icon = new FontIcon { Glyph = "\uE73E" };
                    byName.Click += async (a, b) => await ApplySortModeAsync(SortMode.ByName);
                    sortSub.Items.Add(byName);

                    var byCreated = new MenuFlyoutItem { Text = "按加入时间" };
                    if (currentSort == SortMode.ByCreatedTime)
                        byCreated.Icon = new FontIcon { Glyph = "\uE73E" };
                    byCreated.Click += async (a, b) => await ApplySortModeAsync(SortMode.ByCreatedTime);
                    sortSub.Items.Add(byCreated);

                    var custom = new MenuFlyoutItem { Text = "自定义（拖拽排序）" };
                    if (currentSort == SortMode.Custom)
                        custom.Icon = new FontIcon { Glyph = "\uE73E" };
                    custom.Click += async (a, b) => await ApplySortModeAsync(SortMode.Custom);
                    sortSub.Items.Add(custom);

                    flyout.Items.Add(sortSub);
                    flyout.Items.Add(new MenuFlyoutSeparator());
                }

                // ---- 粘贴（有内部剪贴板内容才显示）----
                if (canAdd)
                {
                    string? pasteText = BuildPasteMenuText();
                    if (pasteText != null)
                    {
                        var pasteItem = new MenuFlyoutItem
                        {
                            Text = pasteText,
                            Icon = new FontIcon { Glyph = "\uE77F" }
                        };
                        pasteItem.Click += async (a, b) => await PasteAsync();
                        flyout.Items.Add(pasteItem);
                        flyout.Items.Add(new MenuFlyoutSeparator());
                    }
                }

                // ---- 添加 ----
                if (canAdd)
                {
                    var addItem = new MenuFlyoutItem
                    {
                        Text = "添加",
                        Icon = new FontIcon { Glyph = "\uE710" }
                    };
                    addItem.Click += (a, b) => AddFileButton_Click(null, null);
                    flyout.Items.Add(addItem);
                }

                // ---- 复制当前页面 / 清空当前页面 ----
                bool hasContent = CurrentShortcuts.Count > 0;

                if (canSort || canAdd)
                {
                    flyout.Items.Add(new MenuFlyoutSeparator());
                }

                if (hasContent)
                {
                    var copyPageItem = new MenuFlyoutItem
                    {
                        Text = LanguageService.TranslateFormat("复制当前页面（{count} 项）", ("{count}", CurrentShortcuts.Count)),
                        Icon = new FontIcon { Glyph = "\uE8C8" }
                    };
                    copyPageItem.Click += async (a, b) => await CopyCurrentPageAsync();
                    flyout.Items.Add(copyPageItem);

                    var clearPageItem = new MenuFlyoutItem
                    {
                        Text = "清空当前页面",
                        Icon = new FontIcon { Glyph = "\uE74D" }
                    };
                    clearPageItem.Click += async (a, b) => await ClearCurrentPageAsync();
                    flyout.Items.Add(clearPageItem);
                }

                // ---- 个性化 ----
                flyout.Items.Add(new MenuFlyoutSeparator());

                var personalizeItem = new MenuFlyoutItem
                {
                    Text = "个性化",
                    Icon = new FontIcon { Glyph = "\uE790" }
                };
                personalizeItem.Click += async (a, b) => await OpenPersonalizationAsync();
                flyout.Items.Add(personalizeItem);

                if (flyout.Items.Count == 0) return;
            }

            NormalizeMenuFlyoutSeparators(flyout);
            LanguageService.RegisterMenuFlyout(flyout);
            flyout.ShowAt(ShortcutGrid, new FlyoutShowOptions
            {
                Position = e.GetPosition(ShortcutGrid)
            });
        }

        private SortMode GetCurrentSortMode()
        {
            if (_currentView == ViewKind.Category
                && NavMenuList.SelectedItem is Category cat
                && !cat.IsSeparator)
            {
                return cat.SortMode;
            }
            if (_currentView == ViewKind.Tab && _currentTabItem != null)
            {
                return _currentTabItem.SortMode;
            }
            return SortMode.Custom;
        }
        private async Task CopyCurrentPageAsync()
        {
            if (CurrentShortcuts.Count == 0)
            {
                await ShowMessageAsync("当前页面没有内容");
                return;
            }

            CopyOrCutShortcuts(CurrentShortcuts.ToList(), isCut: false);
            await ShowSuccessMessageAsync(LanguageService.TranslateFormat("已复制 {count} 项", ("{count}", CurrentShortcuts.Count)));
        }
        private async Task ClearCurrentPageAsync()
        {
            if (CurrentShortcuts.Count == 0)
            {
                await ShowMessageAsync("当前页面没有内容");
                return;
            }

            // 确定清空的目标
            Category? cat = null;
            MyTabItem? tab = null;

            if (_currentView == ViewKind.Category
                && NavMenuList.SelectedItem is Category c
                && !c.IsSeparator)
            {
                cat = c;
            }
            else if (_currentView == ViewKind.Tab
                && _currentTabItem != null
                && _currentTabItem.BuiltinKind == BuiltinTabKind.None)
            {
                tab = _currentTabItem;
            }
            else
            {
                await ShowMessageAsync("当前视图不支持清空");
                return;
            }

            // 二次确认
            string targetName = cat?.Name ?? tab?.Title ?? "";
            if (ShouldConfirmDelete())
            {
                int result = await ShowConfirmationWindowAsync(
                    "清空当前页面",
                    LanguageService.TranslateFormat("确定清空「{name}」下的 {count} 项吗？",
                        ("{name}", cat?.DisplayName ?? targetName), ("{count}", CurrentShortcuts.Count)) +
                    (cat?.IsShared == true ? "\n\n" + LanguageService.Translate("⚠️ 该分类是所有模式共享的，清空后这些项目会从所有模式中移除。") : "") + "\n\n" +
                    LanguageService.Translate("• 记录会被删除\n• 备份文件保留 7 天后自动清理"),
                    "清空");
                if (result != 1) return;
            }
            
            if (cat != null)
            {
                RecycleBinRepository.MoveByCategory(cat.Id);

                // 备份文件夹改名标记删除
                RetireCategoryBackups(cat, CurrentShortcuts.ToList());
            }
            else if (tab != null)
            {
                // 标签是视图，只清空 TitleId 引用，不删记录
                // ⚠️ 但用户预期可能是"清空标签下的内容"，此时应该清空 TitleId
                foreach (var s in CurrentShortcuts.ToList())
                {
                    BlockBox.Data.ShortcutRepository.SetTitleId(s.Id, _currentMode, "");
                }
            }

            LoadShortcutsFromDb();
            await ShowSuccessMessageAsync(LanguageService.TranslateFormat("已清空 {name}", ("{name}", cat?.DisplayName ?? targetName)));
        }
        private async Task OpenPersonalizationAsync()
        {
            await OpenSettingsAsync();
            // ResetToDefaultPage 已经把选中项复位到"主题/外观"，
            // 如果你想跳到别的页，在这里显式切一下：
            // SettingsViewControl.SwitchToPage("Appearance");
        }
        private async Task ApplySortModeAsync(SortMode mode)
        {
            // ========== 分类视图 ==========
            if (_currentView == ViewKind.Category
                && NavMenuList.SelectedItem is Category cat
                && !cat.IsSeparator)
            {
                if (cat.SortMode == mode) return;

                cat.SortMode = mode;
                BlockBox.Data.CategoryRepository.UpdateSortMode(cat.Id, mode);

                // 把新顺序固化到 SortOrder，切回 Custom 时也是这个顺序
                if (mode == SortMode.ByName)
                    BlockBox.Data.ShortcutRepository.ResequenceByName(cat.Id, _currentMode);
                else if (mode == SortMode.ByCreatedTime)
                    BlockBox.Data.ShortcutRepository.ResequenceByCreatedTime(cat.Id, _currentMode);

                LoadShortcutsFromDb();
                return;
            }

            // ========== 用户标签视图 ==========
            if (_currentView == ViewKind.Tab
                && _currentTabItem != null
                && _currentTabItem.BuiltinKind == BuiltinTabKind.None)
            {
                if (_currentTabItem.SortMode == mode) return;

                _currentTabItem.SortMode = mode;
                BlockBox.Data.TabRepository.UpdateSortMode(_currentTabItem.Id, mode);

                if (mode == SortMode.ByName)
                    BlockBox.Data.ShortcutRepository.ResequenceTabByName(_currentTabItem.Id, _currentMode);
                else if (mode == SortMode.ByCreatedTime)
                    BlockBox.Data.ShortcutRepository.ResequenceTabByCreatedTime(_currentTabItem.Id, _currentMode);

                LoadShortcutsFromDb();
                return;
            }

            // 收藏 / 最近访问 / 空白 → 不支持
            await ShowMessageAsync("当前视图不支持排序");
        }


        private async Task DeleteShortcutAsync(ShortcutItem s)
        {
            await DeleteShortcutsAsync(new List<ShortcutItem> { s });
        }



        /// <summary>
        /// 决定用哪个路径启动：源文件优先，源文件不在就用备份
        /// </summary>
        private string ResolveLaunchPath(ShortcutItem s)
        {
            // ⭐ UWP 应用：TargetPath 本身就能启动
            if (!string.IsNullOrEmpty(s.TargetPath)
                && s.TargetPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                return s.TargetPath;
            if (!string.IsNullOrEmpty(s.TargetPath)
                && System.IO.File.Exists(s.TargetPath)
                && (!IsShortcutFilePath(s.TargetPath) || IsShellShortcutPath(s.TargetPath)))
                return s.TargetPath;

            // 应用程序没有备份 → BackupPath 为空 → 直接跳过
            if (!string.IsNullOrEmpty(s.BackupPath))
            {
                string abs = BackupPathHelper.MakeAbsolute(s.BackupPath);
                if (System.IO.File.Exists(abs)
                    && (!IsShortcutFilePath(abs) || IsShellShortcutPath(abs))) return abs;
            }

            return "";
        }

        private bool TryResolveShortcutLaunch(
            ShortcutItem shortcut,
            out string launchPath,
            out bool isShellShortcut,
            out string error)
        {
            launchPath = ResolveLaunchPath(shortcut);
            isShellShortcut = false;
            error = $"文件不存在或快捷方式无效，无法启动：{shortcut.Name}";

            if (string.IsNullOrWhiteSpace(launchPath))
                return false;

            if (Path.GetExtension(launchPath).Equals(".Ink", StringComparison.OrdinalIgnoreCase))
            {
                string? matchingShortcut = BlockBox.Services.InstalledProgramScanner.FindMatchingStartMenuShortcut(
                    launchPath, shortcut.Name);
                if (!string.IsNullOrWhiteSpace(matchingShortcut))
                {
                    launchPath = matchingShortcut;
                    isShellShortcut = true;
                    bool updated = BlockBox.Data.ShortcutRepository.UpdateTargetPath(
                        shortcut.Id, matchingShortcut, shortcut.IsShared);
                    shortcut.TargetPath = matchingShortcut;
                    System.Diagnostics.Debug.WriteLine(
                        $"[启动] 已纠正异常 .Ink 数据库路径; Name={shortcut.Name}; NewTarget={matchingShortcut}; Persisted={updated}");
                    return true;
                }
            }

            if (IsShellShortcutPath(launchPath))
            {
                isShellShortcut = true;
                return true;
            }

            if (Path.GetExtension(launchPath).Equals(".Ink", StringComparison.OrdinalIgnoreCase))
            {
                error = $"快捷方式路径扩展名异常，且无法唯一匹配到有效的开始菜单 .lnk：{launchPath}；显示名称：{shortcut.Name}";
                System.Diagnostics.Debug.WriteLine(
                    $"[启动] 匹配失败，已阻止直接执行 .Ink; Name={shortcut.Name}; Target={launchPath}; User={Environment.UserDomainName}\\{Environment.UserName}; Base={AppContext.BaseDirectory}");
                return false;
            }

            return File.Exists(launchPath);
        }

        private static bool IsShortcutFilePath(string path)
        {
            string extension = System.IO.Path.GetExtension(path);
            return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".Ink", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsShellShortcutPath(string path)
        {
            string extension = System.IO.Path.GetExtension(path);
            return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
        }

        private void PrepareShortcutVisual(FrameworkElement element, ShortcutItem item)
        {
            if (FindChildByName<TextBlock>(element, "ItemName") is TextBlock nameText)
                nameText.Text = GetDisplayName(item);

            // ⭐ Tooltip 设在卡片上，悬停整张卡片都能看到
            if (FindChildByName<Border>(element, "ShortcutCard") is Border card)
            {
                string full = GetFullName(item);
                ToolTipService.SetToolTip(card, string.IsNullOrEmpty(full) ? null : full);
            }
            var icon = FindChildByName<FontIcon>(element, "ItemIcon");
            var image = FindChildByName<Image>(element, "ItemThumbnail");
            if (icon == null || image == null) return;
            


            string? path = ResolveThumbnailPath(item);
            if (string.IsNullOrEmpty(path))
            {
                icon.Visibility = Visibility.Visible;
                image.Visibility = Visibility.Collapsed;
                image.Source = null;
                image.Tag = item.Id;
                return;
            }

            string cacheKey = BuildThumbnailImageCacheKey(path, 96);
            if (_thumbnailImageCache.TryGetValue(cacheKey, out var cachedImage))
            {
                image.Tag = item.Id;
                image.Source = cachedImage;
                image.Visibility = Visibility.Visible;
                icon.Visibility = Visibility.Collapsed;
                return;
            }

            icon.Visibility = Visibility.Visible;
            image.Visibility = Visibility.Collapsed;
            image.Source = null;
            image.Tag = item.Id;
            _ = LoadThumbnailAsync(item, icon, image, path, cacheKey);
        }

        /// <summary>
        /// 根据“显示文件扩展名”设置，返回卡片上应该显示的名称。
        /// 只对文件（IsFile=true）追加扩展名；应用、UWP 不追加。
        /// </summary>
        /// /// <summary>
        /// 返回完整名称（含扩展名），用于 Tooltip 兜底显示。
        /// 不受“显示文件扩展名”开关影响。
        /// </summary>
        private static string GetFullName(ShortcutItem item)
        {
            if (!item.IsFile || string.IsNullOrEmpty(item.TargetPath))
                return item.Name ?? "";

            try
            {
                string ext = System.IO.Path.GetExtension(item.TargetPath);
                return string.IsNullOrEmpty(ext) ? (item.Name ?? "") : (item.Name ?? "") + ext;
            }
            catch
            {
                return item.Name ?? "";
            }
        }
        private string GetDisplayName(ShortcutItem item)
        {
            if (!_showFileExtension || !item.IsFile || string.IsNullOrEmpty(item.TargetPath))
                return item.Name;

            try
            {
                string ext = System.IO.Path.GetExtension(item.TargetPath);
                return string.IsNullOrEmpty(ext) ? item.Name : item.Name + ext;
            }
            catch
            {
                return item.Name;
            }
        }

        /// <summary>
        /// 把当前所有已实例化卡片的名称刷新为当前设置对应的显示文本。
        /// 设置切换时调用。
        /// </summary>
        private void RefreshAllShortcutNames()
        {
            int childCount = VisualTreeHelper.GetChildrenCount(ShortcutGrid);
            for (int i = 0; i < childCount; i++)
            {
                if (VisualTreeHelper.GetChild(ShortcutGrid, i) is not FrameworkElement element)
                    continue;

                int index = ShortcutGrid.GetElementIndex(element);
                if (index < 0 || index >= CurrentShortcuts.Count)
                    continue;

                if (FindChildByName<TextBlock>(element, "ItemName") is TextBlock nameText
     && FindChildByName<Border>(element, "ShortcutCard") is Border card)
                {
                    var shortcut = CurrentShortcuts[index];
                    nameText.Text = GetDisplayName(shortcut);

                    string full = GetFullName(shortcut);
                    ToolTipService.SetToolTip(card, string.IsNullOrEmpty(full) ? null : full);
                }
            }
        }
        private string? ResolveThumbnailPath(ShortcutItem item)
        {
            // ⭐ 优先用自定义图标（UWP logo 走这里）
            if (!string.IsNullOrEmpty(item.IconPath) && System.IO.File.Exists(item.IconPath))
                return item.IconPath;

            // 共享分类（全部应用/办公/音乐/游戏）里手动拖入的程序没有 IconPath，
            // 这里回退到真实文件路径走 Shell 图标；UWP 的 shell:AppsFolder 伪路径在下面被排除。
            // ⭐ UWP 的 TargetPath 不是真实文件路径，跳过
            if (!string.IsNullOrEmpty(item.TargetPath)
                && !item.TargetPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)
                && System.IO.File.Exists(item.TargetPath))
                return item.TargetPath;

            if (!string.IsNullOrEmpty(item.BackupPath))
            {
                string backup = BackupPathHelper.MakeAbsolute(item.BackupPath);
                if (System.IO.File.Exists(backup))
                    return backup;
            }

            return null;
        }

        private static string BuildThumbnailImageCacheKey(string path, int size)
        {
            try
            {
                var info = new FileInfo(path);
                return $"{path.ToLowerInvariant()}|{size}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            }
            catch
            {
                return $"{path.ToLowerInvariant()}|{size}";
            }
        }

        private void ShortcutRepeater_ElementPrepared(
    ItemsRepeater sender,
    ItemsRepeaterElementPreparedEventArgs args)
        {
            if (args.Element is not FrameworkElement element)
                return;

            int index = sender.GetElementIndex(element);
            if (index < 0 || index >= CurrentShortcuts.Count)
                return;

            FontSizeHelper.ApplyGlobalFontSizeToTree(element);

            var item = CurrentShortcuts[index];
            EnsureShortcutMotionState(element);
            ResetShortcutMotionState(element);
            PrepareShortcutVisual(element, item);

            // 新建/复用的卡片也要立刻套用当前字体色，否则会停留在主题默认黑色。
            TextColorHelper.ApplyForegroundToTree(
                element,
                _shortcutTextColor,
                _shortcutTextAdjust,
                _shortcutTextFallback);

            var motionHost = FindChildByName<FrameworkElement>(element, "ShortcutMotionHost");
            if (motionHost != null)
            {
                if (_isShortcutScrollViewerScrolling)
                {
                    StopShortcutLoadAnimation(element, motionHost, resetToStart: false);
                    motionHost.Opacity = 1;
                    if (motionHost.RenderTransform is TranslateTransform tt)
                        tt.X = 0;
                    else
                        motionHost.RenderTransform = new TranslateTransform { X = 0 };
                }
                else if (_isShortcutLoadAnimationBatchActive)
                {
                    // ⭐ 批次激活：强制设成动画起始态
                    motionHost.Opacity = 0;
                    if (motionHost.RenderTransform is TranslateTransform tt)
                        tt.X = 18;
                    else
                        motionHost.RenderTransform = new TranslateTransform { X = 18 };
                }
                else
                {
                    // 非批次（拖拽 reload、单项增删）：正常显示
                    motionHost.Opacity = 1;
                    if (motionHost.RenderTransform is TranslateTransform tt)
                        tt.X = 0;
                }
            }

            // ItemsRepeater can raise ElementPrepared before the element has its final
            // layout offset. Start center-scale animations from the render callback,
            // which runs after layout, rather than using the temporary offscreen offset.
            if (!_isShortcutLoadAnimationBatchActive || !UseCenterScaleShortcutLoadAnimation())
                TryAnimateShortcutAppearance(element, item, index);
            RefreshShortcutSelectionVisual(element, item);
        }

        private void ShortcutRepeater_ElementClearing(
     ItemsRepeater sender,
     ItemsRepeaterElementClearingEventArgs args)
        {
            if (args.Element is not FrameworkElement element) return;

            RemoveShortcutMotionState(element);

            var icon = FindChildByName<FontIcon>(element, "ItemIcon");
            var image = FindChildByName<Image>(element, "ItemThumbnail");
            var selectionVisual = FindChildByName<Border>(element, "ShortcutSelectionVisual");
            var motionHost = FindChildByName<FrameworkElement>(element, "ShortcutMotionHost");
            if (icon == null || image == null) return;

            // ⭐ Glyph 和 Text 交给 DataContext，别清
            icon.Visibility = Visibility.Visible;
            image.Visibility = Visibility.Collapsed;
            image.Source = null;
            image.Tag = null;

            if (motionHost != null)
            {
                motionHost.Opacity = 0;
                if (motionHost.RenderTransform is TranslateTransform tt)
                    tt.X = 18;
                else
                    motionHost.RenderTransform = new TranslateTransform { X = 18 };
            }

            if (selectionVisual != null)
            {
                var v = ElementCompositionPreview.GetElementVisual(selectionVisual);
                v.StopAnimation("Opacity");
                v.Opacity = 0f;
                selectionVisual.Opacity = 0;
            }
        }
        private async Task LoadThumbnailAsync(
     ShortcutItem item, FontIcon icon, Image image, string path, string cacheKey)
        {
            // ⭐ UWP logo：直接读文件，不走 Shell API
            if (!string.IsNullOrEmpty(item.IconPath)
                && string.Equals(path, item.IconPath, StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"[UWP读取] 进入分支 {item.Name} path={path}");
                try
                {
                    byte[] pngBytes = await File.ReadAllBytesAsync(path);
                    System.Diagnostics.Debug.WriteLine($"[UWP读取] 读到 {pngBytes.Length} bytes");

                    byte[] trimmed = TrimTransparentPng(pngBytes);
                    System.Diagnostics.Debug.WriteLine(
                        $"[UWP读取] 裁剪后 {trimmed.Length} bytes");

                    if (!Equals(image.Tag, item.Id))
                        return;

                    bool ok = await TrySetImageFromPngAsync(image, trimmed);
                    System.Diagnostics.Debug.WriteLine($"[UWP读取] SetImage 结果={ok}");

                    if (ok)
                    {
                        if (image.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bmp)
                            _thumbnailImageCache[cacheKey] = bmp;
                        icon.Visibility = Visibility.Collapsed;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UWP读取] 异常 {ex.GetType().Name}: {ex.Message}");
                }
            }

            // ============ 普通程序：走 IShellItemImageFactory ============
            byte[]? png = await Task.Run(() =>
                BlockBox.Helpers.ShellIconHelper.GetThumbnailPngBytes(path, 96));

            if (!Equals(image.Tag, item.Id))
                return;

            if (png != null && png.Length > 0
                && await TrySetImageFromPngAsync(image, TrimTransparentPng(png)))
            {
                if (!Equals(image.Tag, item.Id))
                {
                    image.Source = null;
                    image.Visibility = Visibility.Collapsed;
                    return;
                }

                if (image.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap)
                    _thumbnailImageCache[cacheKey] = bitmap;

                icon.Visibility = Visibility.Collapsed;
                return;
            }

            // 拿不到 → FontIcon 继续显示
        }
        private static async Task<bool> TrySetImageFromStreamAsync(
    Image image, Windows.Storage.Streams.IRandomAccessStream stream)
        {
            try
            {
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bmp.SetSourceAsync(stream);
                image.Source = bmp;
                image.Visibility = Visibility.Visible;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 裁掉透明边距；仅在边缘背景高度一致且主体明显缩小时，才进一步裁掉背景外圈。
        /// </summary>
        private static byte[] TrimTransparentPng(byte[] png)
        {
            try
            {
                using var ms = new MemoryStream(png);
                using var src = new System.Drawing.Bitmap(ms);
                var rect = new System.Drawing.Rectangle(0, 0, src.Width, src.Height);
                var data = src.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
                byte[] pixels;
                int stride;
                try
                {
                    stride = Math.Abs(data.Stride);
                    pixels = new byte[stride * src.Height];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    for (int y = 0; y < src.Height; y++)
                    {
                        int row = data.Stride >= 0 ? y * stride : (src.Height - 1 - y) * stride;
                        for (int x = 0; x < src.Width; x++)
                        {
                            if (pixels[row + x * 4 + 3] <= 10) continue;
                            minX = Math.Min(minX, x);
                            minY = Math.Min(minY, y);
                            maxX = Math.Max(maxX, x);
                            maxY = Math.Max(maxY, y);
                        }
                    }
                }
                finally
                {
                    src.UnlockBits(data);
                }

                if (maxX < minX || maxY < minY) return png;

                int alphaMinX = minX, alphaMinY = minY, alphaMaxX = maxX, alphaMaxY = maxY;
                int candidateX = (alphaMinX + alphaMaxX) / 2;
                int candidateY = alphaMinY;
                int candidateOffset = (data.Stride >= 0 ? candidateY * stride : (src.Height - 1 - candidateY) * stride)
                    + candidateX * 4;
                byte bgB = pixels[candidateOffset];
                byte bgG = pixels[candidateOffset + 1];
                byte bgR = pixels[candidateOffset + 2];
                byte bgA = pixels[candidateOffset + 3];
                static bool IsNearBackground(byte[] data, int offset, byte b, byte g, byte r, byte a)
                {
                    return Math.Abs(data[offset] - b) <= 18
                        && Math.Abs(data[offset + 1] - g) <= 18
                        && Math.Abs(data[offset + 2] - r) <= 18
                        && Math.Abs(data[offset + 3] - a) <= 18;
                }

                int perimeterSamples = 0, matchingSamples = 0;
                int edgeStep = Math.Max(1, Math.Min(src.Width, src.Height) / 64);
                for (int x = alphaMinX; x <= alphaMaxX; x += edgeStep)
                {
                    int top = (data.Stride >= 0 ? alphaMinY * stride : (src.Height - 1 - alphaMinY) * stride) + x * 4;
                    int bottom = (data.Stride >= 0 ? alphaMaxY * stride : (src.Height - 1 - alphaMaxY) * stride) + x * 4;
                    perimeterSamples += 2;
                    if (IsNearBackground(pixels, top, bgB, bgG, bgR, bgA)) matchingSamples++;
                    if (IsNearBackground(pixels, bottom, bgB, bgG, bgR, bgA)) matchingSamples++;
                }
                for (int y = alphaMinY + edgeStep; y < alphaMaxY; y += edgeStep)
                {
                    int row = data.Stride >= 0 ? y * stride : (src.Height - 1 - y) * stride;
                    perimeterSamples += 2;
                    if (IsNearBackground(pixels, row + alphaMinX * 4, bgB, bgG, bgR, bgA)) matchingSamples++;
                    if (IsNearBackground(pixels, row + alphaMaxX * 4, bgB, bgG, bgR, bgA)) matchingSamples++;
                }

                if (perimeterSamples > 0 && matchingSamples >= perimeterSamples * 0.9)
                {
                    int contentMinX = src.Width, contentMinY = src.Height, contentMaxX = -1, contentMaxY = -1;
                    for (int y = alphaMinY; y <= alphaMaxY; y++)
                    {
                        int row = data.Stride >= 0 ? y * stride : (src.Height - 1 - y) * stride;
                        for (int x = alphaMinX; x <= alphaMaxX; x++)
                        {
                            int offset = row + x * 4;
                            if (pixels[offset + 3] <= 10 || IsNearBackground(pixels, offset, bgB, bgG, bgR, bgA))
                                continue;
                            contentMinX = Math.Min(contentMinX, x);
                            contentMinY = Math.Min(contentMinY, y);
                            contentMaxX = Math.Max(contentMaxX, x);
                            contentMaxY = Math.Max(contentMaxY, y);
                        }
                    }

                    int minCrop = Math.Max(8, Math.Min(src.Width, src.Height) / 12);
                    if (contentMaxX >= contentMinX && contentMaxY >= contentMinY
                        && contentMinX - alphaMinX >= minCrop && contentMinY - alphaMinY >= minCrop
                        && alphaMaxX - contentMaxX >= minCrop && alphaMaxY - contentMaxY >= minCrop
                        && contentMaxX - contentMinX + 1 >= src.Width * 0.3
                        && contentMaxY - contentMinY + 1 >= src.Height * 0.3)
                    {
                        minX = contentMinX;
                        minY = contentMinY;
                        maxX = contentMaxX;
                        maxY = contentMaxY;
                        System.Diagnostics.Debug.WriteLine(
                            $"[图标裁边] 检测到一致色背景; 原尺寸={src.Width}x{src.Height}; 内容框={minX},{minY},{maxX - minX + 1}x{maxY - minY + 1}");
                    }
                }

                int padding = Math.Max(2, Math.Min(src.Width, src.Height) / 32);
                minX = Math.Max(0, minX - padding);
                minY = Math.Max(0, minY - padding);
                maxX = Math.Min(src.Width - 1, maxX + padding);
                maxY = Math.Min(src.Height - 1, maxY + padding);
                var cropRect = new System.Drawing.Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
                using var cropped = src.Clone(cropRect, src.PixelFormat);

                using var outMs = new MemoryStream();
                cropped.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
                return outMs.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[裁剪] 失败: {ex.Message}");
                return png;
            }
        }
        private static async Task<bool> TrySetImageFromPngAsync(Image image, byte[] png)
        {
            try
            {
                using var ms = new System.IO.MemoryStream(png);
                using var ras = ms.AsRandomAccessStream();
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
                {
                    // 只指定一个解码尺寸，避免非正方形图标在解码时被拉伸为 96x96。
                    DecodePixelWidth = 96
                };
                await bmp.SetSourceAsync(ras);
                image.Source = bmp;
                image.Visibility = Visibility.Visible;
                return true;
            }
            catch { return false; }
        }


        // 启动时读取配置并应用
        private void ApplySettings(
            bool reloadNavigation = true,
            bool reloadBackgrounds = true)
        {
            if (_closeAnimationStarted || _allowWindowClose)
                return;

            System.Diagnostics.Debug.WriteLine("========== 当前 INI 路径: " + _iniPath + " ==========");

            // ================= 全局字体族 =================
            string globalFontName = _ini.ReadValue("System", "FontFamilyName", SettingsDefaults.FontFamilyName);
            ApplyGlobalFontFamily(string.IsNullOrEmpty(globalFontName) ? null : globalFontName);

            // ================= 1. 全局字体大小 =================
            FontSizeHelper.CurrentFontSizeStep = ReadFontSizeStep();

            _isSidebarExpanded = _ini.ReadBool("Sidebar", "State", SettingsDefaults.SidebarState);

            // ================= 显示文件扩展名 =================
            bool showExt = _ini.ReadBool("Behavior", "ShowFileExtension", SettingsDefaults.ShowFileExtension);
            bool showExtChanged = _showFileExtension != showExt;
            _showFileExtension = showExt;
            if (showExtChanged)
                RefreshAllShortcutNames();

            ApplySidebarState();

            // ================= 2. 主界面UI背景颜色 =================
            string mUIBgColor = _ini.ReadValue("Global", "PrimaryBackground", SettingsDefaults.PrimaryBackground);
            var primaryBackground = ParseColor(mUIBgColor);
            if (_acrylicController != null)
            {
                _acrylicController.TintColor = primaryBackground;
                _acrylicController.FallbackColor = primaryBackground;
            }
            // DesktopAcrylicBackdrop already provides the window-level blur. Keep this
            // layer as a simple translucent tint so refreshing settings cannot stack a
            // second acrylic material over the window backdrop.
            MainUIView.Background = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(primaryBackground.A * 0.15),
                primaryBackground.R,
                primaryBackground.G,
                primaryBackground.B));

            string mainContentBackground = _ini.ReadValue("Global", "MainContentBackground", SettingsDefaults.MainContentBackground);
            var mainContentColor = ParseColor(mainContentBackground);
            ContentAreaBody.Background = new SolidColorBrush(mainContentColor);
            //var mainContentBrush = new SolidColorBrush(mainContentColor);
            //ContentAreaBody.Background = mainContentBrush;
           // ContentAreaBorder.Background = mainContentBrush;

            // Keep the global dim layer light enough for the window's DesktopAcrylicBackdrop
            // to remain visible through transparent title-bar and edge regions.
            OverlayMask.Opacity = Math.Clamp(
                double.TryParse(
                    _ini.ReadValue("System", "OverlayOpacity",
                        SettingsDefaults.OverlayOpacity.ToString(CultureInfo.InvariantCulture)),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double overlayOpacity)
                    ? overlayOpacity
                    : SettingsDefaults.OverlayOpacity,
                0,
                1) * 0.25;
            OverlayMask.Background = new SolidColorBrush(
                ParseColor(_ini.ReadValue("System", "OverlayColor", SettingsDefaults.OverlayColor)));

            // ================= 2. 左侧菜单背景颜色 =================
            string sidebarBgColor = _ini.ReadValue("Sidebar", "BackgroundColor", SettingsDefaults.SidebarBackgroundColor);
            var sidebarColor = ParseColor(sidebarBgColor);
            var sidebarBrush = new SolidColorBrush(sidebarColor);
            SidebarGrid.Background = sidebarBrush;

            string sharedBackgroundColor = _ini.ReadValue("Global", "PrimaryBackground", SettingsDefaults.PrimaryBackground);
            var sharedColor = ParseColor(sharedBackgroundColor);
            var sharedBackgroundBrush = new SolidColorBrush(sharedColor);
            Application.Current.Resources["AppPrimaryBackgroundBrush"] = sharedBackgroundBrush;
            UpdateCustomScrollBarColors();
            TabBarGrid.Background = sharedBackgroundBrush;
            SidebarGrid.BorderBrush = sharedBackgroundBrush;
            LeftBGcolor.Background = sharedBackgroundBrush;
            ContentAreaBorder.Background = sharedBackgroundBrush;
            SearchBoxBorder.Background = sharedBackgroundBrush;
            // ================= 2. 左侧菜单添加按钮背景颜色 =================
            SidebarAddBt.Background = sharedBackgroundBrush;

            CategoryEditPanelControl.ApplyConfirmButtonBackground(sharedBackgroundBrush);
            TabEditPanelControl.ApplyConfirmButtonBackground(sharedBackgroundBrush);

            // 背景决定文字颜色，必须先于取色执行（图片/视频模式会改卡片区背景）。
            if (reloadBackgrounds)
                ApplyBackgrounds();

            // ================= 3. 文字颜色 =================
            // 自动开关打开：按控件实际背景校正用户颜色的明度；
            // 关闭：严格使用用户颜色，不做任何混色。
            var (globalFontColor, globalAdjust) = ReadFontColorSetting(
                "PrimaryFontColor",
                "PrimaryFontColorAuto",
                SettingsDefaults.PrimaryFontColor,
                whiteMeansAuto: true);

            var (contentFontColor, contentAdjust) = ReadFontColorSetting(
                "ShortcutGridFontColor",
                "ShortcutGridFontColorAuto",
                SettingsDefaults.ShortcutGridFontColor,
                whiteMeansAuto: false);
            // 内容区“跟随全局”只决定使用哪个基础颜色；
            // 是否按背景自动提亮/压暗统一由全局“自动”开关决定。
            Color contentTextColor = contentAdjust ? globalFontColor : contentFontColor;
            bool contentTextAdjust = globalAdjust;

            var sidebarTextColor = ResolveTextColor(sidebarColor, globalFontColor, globalAdjust);
            var tabBarTextColor = ResolveTextColor(sharedColor, globalFontColor, globalAdjust);
            var settingsButtonTextColor = ResolveTextColor(sharedColor, globalFontColor, globalAdjust);
            // 内容区/设置页/弹层默认是浅色卡片表面（应用固定 Light 主题）；
            // 自动模式下白色字会在这里被压暗成可读的深色。
            var cardSurfaceColor = Color.FromArgb(255, 255, 255, 255);
            var cardTextColor = ResolveTextColor(cardSurfaceColor, globalFontColor, globalAdjust);

            // 二级窗口也跟随同一套字体色；先写入，再让 UpdateAll 只遍历一次。
            WindowAcrylicHelper.SetTextColor(globalFontColor, globalAdjust, applyToWindows: false);
            WindowAcrylicHelper.UpdateAll(sharedColor);

            var sidebarTextBrush = new SolidColorBrush(sidebarTextColor);
            _sidebarTextBrush = sidebarTextBrush;
            _shortcutTextColor = contentTextColor;
            _shortcutTextAdjust = contentTextAdjust;
            _shortcutTextFallback = cardSurfaceColor;
            var tabBarTextBrush = new SolidColorBrush(tabBarTextColor);
            var cardTextBrush = new SolidColorBrush(cardTextColor);

            SetTextResources(SidebarGrid, sidebarTextBrush, sidebarTextBrush);
            SetTextResources(MainContentArea, cardTextBrush, cardTextBrush);
            SetTextResources(TabBarGrid, tabBarTextBrush, tabBarTextBrush);
            SetBrushResource(Application.Current.Resources, "TextFillColorPrimaryBrush", cardTextBrush);
            SetBrushResource(Application.Current.Resources, "TextFillColorSecondaryBrush", cardTextBrush);
            SetBrushResource(Application.Current.Resources, "TextFillColorSidebarBrush", sidebarTextBrush);
            SetBrushResource(Application.Current.Resources, "TextFillColorTabBarBrush", tabBarTextBrush);
            Application.Current.Resources["TextFillColorTabBar"] = tabBarTextColor;

            // 所有背景先落地，再统一遍历一次文字。每个控件只在这里被赋值一次：
            // - TabBarGrid / 搜索栏 / 快捷方式卡片 / 编辑面板都在 MainContentArea 内统一处理；
            // - SettingsView 与自绘标签栏各有自己的逻辑，整棵子树跳过；
            // - Tag="SkipForeground" 的主色按钮由专门的按钮着色逻辑处理。
            ModeSwitcher.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            ExpandButton.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));

            TextColorHelper.ApplyForegroundToTree(
                SidebarGrid,
                globalFontColor,
                globalAdjust,
                sidebarColor);
            // 内容区字体色只作用于快捷方式卡片区（ShortcutGridHost）。
            // 标签栏、搜索栏、编辑面板、设置页都使用全局字体色。
            TextColorHelper.ApplyForegroundToTree(
                MainContentArea,
                globalFontColor,
                globalAdjust,
                mainContentColor,
                d => d is SettingsView
                     || ReferenceEquals(d, ShortcutGridHost)
                     || ReferenceEquals(d, MyTabView));
            TextColorHelper.ApplyForegroundToTree(
                ShortcutGridHost,
                contentTextColor,
                contentTextAdjust,
                mainContentColor,
                d => d is SettingsView || ReferenceEquals(d, EmptyHintPanel));

            // 空状态提示不属于“卡片文字”，仍使用全局字体色。
            TextColorHelper.ApplyForegroundToTree(
                EmptyHintPanel,
                globalFontColor,
                globalAdjust,
                mainContentColor);

            SettingsViewControl.ApplyCalculatedTextColor(globalFontColor, globalAdjust, cardSurfaceColor);
            SettingsViewControl.ApplySettingsButtonsTextColor(settingsButtonTextColor);

            // MyTabView 整体带 SkipForeground，标签栏尾部的“+”按钮需要单独补色。
            CustomAddButton.Foreground = tabBarTextBrush;

            var confirmButtonTextBrush = new SolidColorBrush(settingsButtonTextColor);
            CategoryEditPanelControl.ApplyConfirmButtonForeground(confirmButtonTextBrush);
            TabEditPanelControl.ApplyConfirmButtonForeground(confirmButtonTextBrush);

            ApplyTitleBarTextColors(tabBarTextColor);

            AppMode previousMode = _currentMode;
            string savedMode = _ini.ReadValue(
                "System",
                "CurrentMode",
                SettingsDefaults.CurrentMode.ToString(CultureInfo.InvariantCulture));
            _currentMode = int.TryParse(savedMode, NumberStyles.Integer, CultureInfo.InvariantCulture, out int modeInt)
                ? (AppMode)modeInt
                : (AppMode)SettingsDefaults.CurrentMode;
            if (!ModeRepository.GetAllModes().Contains(_currentMode))
            {
                _currentMode = AppMode.Normal;
                _ini.WriteValue("System", "CurrentMode", ((int)_currentMode).ToString(CultureInfo.InvariantCulture));
            }
            bool isModeSwitch = _initialNavigationSelected && previousMode != _currentMode;
            ApplyCurrentLanguage();

            try
            {
                StartupManager.SetEnabled(
                    _ini.ReadBool("System", "AutoStart", SettingsDefaults.AutoStart));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[启动项] 更新失败: {ex.Message}");
            }

            _favoritesTabPinned = _ini.ReadBool("Tabs", "FavoritesPinned", SettingsDefaults.FavoritesPinned);
            _recentTabPinned = _ini.ReadBool("Tabs", "RecentPinned", SettingsDefaults.RecentPinned);

            ModeSwitcherText.Text = LimitMenuLabel(ModeRepository.GetDisplayName(_currentMode));

            if (reloadNavigation)
            {
                if (isModeSwitch)
                    ResetNavigationForModeSwitch();

                LoadCategoriesFromDb();
                LoadTabsFromDb();
                if (!_initialNavigationSelected)
                {
                    _initialNavigationSelected = true;
                    SelectDefaultCategory();
                    RestoreLastTabIfConfigured();
                }
                else if (isModeSwitch)
                {
                    ScheduleDefaultTabSelectionAfterModeSwitch();
                }
            }

            // 标签容器要等导航加载完才存在，所以标签栏放在这里单独刷一次。
            // 标签文字属于全局字体色，不跟随“内容区字体色”。
            var selectedTabTextColor = ResolveTextColor(
                mainContentColor,
                globalFontColor,
                globalAdjust);
            ApplyTabForeground(tabBarTextColor, selectedTabTextColor);

            ApplyToastColors();

            // 搜索框、标签栏右侧按钮、展开按钮：它们位于 TabBarGrid 之外或模板较晚创建，
            // 容易在第一遍遍历时漏掉。这里用标签栏文字色再补一次，保证首次启动就正确。
            TextColorHelper.ApplyForegroundToTree(
                SearchBoxBorder,
                tabBarTextColor,
                globalAdjust,
                sharedColor);
            TextColorHelper.ApplyForegroundToTree(
                TabBarRightHost,
                tabBarTextColor,
                globalAdjust,
                sharedColor);
            TextColorHelper.ApplyForegroundToTree(
                ExpandButton,
                tabBarTextColor,
                globalAdjust,
                sharedColor);
            SearchBox.Foreground = tabBarTextBrush;
            SearchBox.PlaceholderForeground = tabBarTextBrush;
        }
        private async void ReloadSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplySettings();
                if (BgMediaPlayer.Visibility == Visibility.Visible)
                    BgMediaPlayer.MediaPlayer.Pause();
                ScheduleUpdateTabPaths();

                if (ShouldShowReloadNotification())
                    await ShowMessageAsync("配置已重新加载");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("重载配置失败: " + ex.Message);
            }
        }
        // 辅助方法：将 #AARRGGBB / #RRGGBB 字符串转成 Color
        private static Color ParseColor(string hex)
            => TextColorHelper.ParseColor(hex, Color.FromArgb(255, 0, 0, 0));

        /// <summary>
        /// 读取字体色及其“自动/自定义”状态。旧配置没有 Auto 键时按默认哨兵色推断。
        /// </summary>
        private (Color Color, bool Auto) ReadFontColorSetting(
            string colorKey,
            string autoKey,
            string defaultHex,
            bool whiteMeansAuto)
        {
            Color color = ParseColor(_ini.ReadValue("Global", colorKey, defaultHex));
            string raw = _ini.ReadValue("Global", autoKey, string.Empty);
            bool auto = bool.TryParse(raw, out bool saved)
                ? saved
                : (whiteMeansAuto
                    ? TextColorHelper.IsOpaqueWhite(color)
                    : TextColorHelper.IsOpaqueBlack(color));
            return (color, auto);
        }

        private static Color ResolveTextColor(Color background, Color textColor, bool adjustForContrast)
            => TextColorHelper.ResolveTextColor(background, textColor, adjustForContrast);

        private static Color ApplyWhiteOverlay(Color color, double opacity)
        {
            byte BlendWithWhite(byte channel) =>
                (byte)Math.Clamp(Math.Round(channel + (255 - channel) * opacity), 0, 255);

            return Color.FromArgb(
                255,
                BlendWithWhite(color.R),
                BlendWithWhite(color.G),
                BlendWithWhite(color.B));
        }

        private static void SetTextResources(FrameworkElement root, Brush primaryBrush, Brush secondaryBrush)
        {
            SetBrushResource(root.Resources, "TextFillColorPrimaryBrush", primaryBrush);
            SetBrushResource(root.Resources, "TextFillColorSecondaryBrush", secondaryBrush);
            if (root.Name == "SidebarGrid")
                SetBrushResource(root.Resources, "TextFillColorSidebarBrush", primaryBrush);
            else if (root.Name == "TabBarGrid")
                SetBrushResource(root.Resources, "TextFillColorTabBarBrush", primaryBrush);
        }

        private static void SetBrushResource(ResourceDictionary resources, string key, Brush brush)
        {
            resources[key] = brush;
        }

        private void ApplyTabForeground(Color normalColor, Color selectedColor)
        {
            MyTabView.Foreground = new SolidColorBrush(normalColor);
            for (int i = 0; i < TabItems.Count; i++)
            {
                if (MyTabView.ContainerFromIndex(i) is TabViewItem tabItem)
                {
                    FontSizeHelper.ApplyGlobalFontSizeToTree(tabItem);
                    bool isSelected = MyTabView.ContainerFromItem(MyTabView.SelectedItem) == tabItem;
                    Color color = isSelected ? selectedColor : normalColor;

                    // 标签项内部的文字也一并处理，但整棵子树只遍历一次。
                    // 颜色已经按标签背景算好，这里直接使用，不重复校正。
                    TextColorHelper.ApplyForegroundToTree(tabItem, color, false, color);
                }
            }
        }

        private void RefreshSelectedTabForeground()
        {
            var (normalColor, selectedColor) = GetTabForegroundColors();
            ApplyTabForeground(normalColor, selectedColor);
        }

        internal (Color Normal, Color Selected) GetTabForegroundColors()
        {
            var (globalFontColor, globalAdjust) = ReadFontColorSetting(
                "PrimaryFontColor",
                "PrimaryFontColorAuto",
                SettingsDefaults.PrimaryFontColor,
                whiteMeansAuto: true);

            Color normalColor = ResolveTextColor(
                GetTabBarBackgroundColor(),
                globalFontColor,
                globalAdjust);
            Color selectedColor = ResolveTextColor(
                GetMainContentAreaBackgroundColor(),
                globalFontColor,
                globalAdjust);
            return (normalColor, selectedColor);
        }
        private void ShortcutInteractionHost_DragOver(object sender, DragEventArgs e)
        {
            if (_isExternalFileDragStarting || _isExternalFileDragRunning)
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
                return;
            }

            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "添加到当前分类";
                e.DragUIOverride.IsCaptionVisible = true;
            }
            else
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            }
        }

        private async void ShortcutInteractionHost_Drop(object sender, DragEventArgs e)
        {
            if (_isExternalFileDragStarting || _isExternalFileDragRunning)
                return;

            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            // 必须先拿 deferral，因为下面 await 会结束当前事件处理
            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var paths = items.OfType<Windows.Storage.StorageFile>()
                                 .Select(f => f.Path)
                                 .Where(p => !string.IsNullOrEmpty(p))
                                 .ToList();

                if (paths.Count == 0) return;

                // 必须有当前分类
                if (NavMenuList.SelectedItem is not Category cat || cat.IsSeparator)
                {
                    await ShowMessageAsync("请先选择一个分类");
                    return;
                }

                // 全部应用 / 办公 / 音乐 / 游戏：只接受应用程序，非程序给出明确提示。
                if (IsDefaultApplicationCategory(cat))
                {
                    var applications = paths
                        .Where(path => BlockBox.Helpers.ContentKindHelper.IsApplication(path))
                        .ToList();
                    if (applications.Count == 0)
                    {
                        await ShowMessageAsync(LanguageService.Translate("默认分类只能添加应用程序。"));
                        return;
                    }
                    if (applications.Count < paths.Count)
                    {
                        _ = ShowResultMessageAsync(
                            LanguageService.Translate("默认分类只能添加应用程序，已忽略其中的非程序文件。"),
                            hasWarnings: true);
                    }
                    paths = applications;
                }

                // 复用你已有的批量添加逻辑
                await BatchAddShortcutsAsync(paths, cat);
            }
            finally
            {
                deferral.Complete();
            }
        }

    }

}
