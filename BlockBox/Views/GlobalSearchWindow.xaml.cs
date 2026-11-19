using BlockBox.Data;
using BlockBox.Helpers;
using BlockBox.Models;
using BlockBox.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.Storage;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class GlobalSearchWindow : Window
    {
        private enum ResultSortMode { Name, ModifiedTime, Size }

        private readonly AppMode _mode;
        private readonly GlobalSearchPipeClient _searchClient = new();
        private SearchResultItem? _highlightedResult;
        private long _searchVersion;
        private bool _isInitialized;
        private bool _entranceAnimated;
        private bool _isLoadingMore;
        private bool _searchHasMore;
        private double _autoLoadOffset = double.NaN;
        private string? _cursorName;
        private string? _cursorVolumeId;
        private long? _cursorFileId;
        private string _activeQuery = "";
        private string _activeKind = "all";
        private int _selectedKindIndex;
        private ResultSortMode _sortMode = ResultSortMode.Name;
        private bool _sortReversed;
        private long _sortRequestVersion;
        private int _iconLoadLimit = 200;
        private readonly SemaphoreSlim _thumbnailSemaphore = new(4, 4);
        public ObservableCollection<SearchResultItem> Results { get; } = new();

        // 缩略图悬停预览：停留 200ms 才弹出，最大 200×200 等比缩放。
        private const double ThumbnailPreviewMaxSize = 200;
        private readonly DispatcherQueueTimer _thumbnailPreviewTimer;
        private readonly Dictionary<string, BitmapImage?> _thumbnailPreviewCache = new(StringComparer.OrdinalIgnoreCase);
        private SearchResultItem? _thumbnailPreviewItem;
        private Windows.Foundation.Point _thumbnailPreviewAnchor;

        public GlobalSearchWindow(AppMode mode)
        {
            InitializeComponent(); _mode = mode; ResultsList.ItemsSource = Results;
            _thumbnailPreviewTimer = DispatcherQueue.CreateTimer();
            _thumbnailPreviewTimer.Interval = TimeSpan.FromMilliseconds(200);
            _thumbnailPreviewTimer.IsRepeating = false;
            _thumbnailPreviewTimer.Tick += (_, _) => _ = ShowThumbnailPreviewAsync();
            // 与回收站等窗口保持同一顺序：先应用亚克力和标题栏样式，最后再把 HWND 置为全透明。
            // 反过来的话，后设置样式的代码会让窗口短暂地以不透明状态绘制一帧，表现为闪烁。
            WindowAcrylicHelper.Apply(this);
            ModalWindowHelper.ConfigureCloseOnlyTitleBar(this);
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
                presenter.IsMinimizable = true;
            }
            RootGrid.SizeChanged += RootGrid_SizeChanged;
            RootGrid.Loaded += RootGrid_Loaded;
            WindowFadeController.Prepare(this);
            ToolTipService.SetToolTip(QuickAddButton, LanguageService.Translate("添加到当前分类"));
            ToolTipService.SetToolTip(HistoryButton, LanguageService.Translate("搜索历史"));
            AutomationProperties.SetName(QuickAddButton, LanguageService.Translate("添加到当前分类"));
            AutomationProperties.SetName(HistoryButton, LanguageService.Translate("搜索历史"));
            AutomationProperties.SetName(ClearSearchButton, LanguageService.Translate("清空"));
            if (App.MainWindow is MainWindow shell)
            {
                shell.CurrentCategoryChanged += CurrentCategoryChanged;
                shell.ShellClosed += ShellClosed;
            }
            LanguageService.RegisterDynamicRoot(RootGrid);
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;
            Closed += GlobalSearchWindow_Closed;
            Title = LanguageService.Translate("全局搜索");
            _isInitialized = true;
            AddButton.IsEnabled = false;
            _ = RefreshServiceStatusAsync();
        }

        /// <summary>
        /// 内容真正加载并完成首次布局后才开始过渡：窗口先保持全透明，等这里再淡入 + 轻微
        /// 缩放，避免“先露出半成品界面、再淡入”的闪烁（回收站等窗口用的就是这个时机）。
        /// </summary>
        private void RootGrid_Loaded(object sender, RoutedEventArgs e) => StartEntranceAnimation();

        private void StartEntranceAnimation()
        {
            if (_entranceAnimated) return;
            _entranceAnimated = true;
            WindowFadeController.FadeAndScaleIn(this, RootGrid);
        }

        private void GlobalSearchWindow_Closed(object sender, WindowEventArgs args)
        {
            HideThumbnailPreview();
            LanguageService.LanguageChanged -= LanguageService_LanguageChanged;
            if (App.MainWindow is MainWindow shell)
            {
                shell.CurrentCategoryChanged -= CurrentCategoryChanged;
                shell.ShellClosed -= ShellClosed;
            }
        }

        private void CurrentCategoryChanged(object? sender, EventArgs e) => UpdateAddButtonState();

        private void ShellClosed(object? sender, EventArgs e) => UpdateAddButtonState();

        /// <summary>
        /// 当前生效的工作模式：以主界面为准。搜索面板是常驻的独立窗口，用户切换模式后
        /// 不能再用构造时的旧模式去列分类或写 BelongingMode。
        /// </summary>
        private AppMode EffectiveMode => (App.MainWindow as MainWindow)?.CurrentMode ?? _mode;

        /// <summary>主窗体还活着（含收在托盘里）才能执行“添加到分类”这类依赖主界面的操作。</summary>
        private bool CanModifyShell => App.MainWindow is MainWindow shell && !shell.IsShellClosed;

        private void LanguageService_LanguageChanged(object? sender, EventArgs e)
        {
            Title = LanguageService.Translate("全局搜索");
            KindSwitcherText.Text = LanguageService.Translate(KindName(_selectedKindIndex));
            if (Results.Count > 0) UpdateResultsStatus();
        }

        /// <summary>分类项的中文源文本（同时作为语言包键）。</summary>
        private static string KindName(int index) => index switch
        {
            1 => "应用程序",
            2 => "图片",
            3 => "音频",
            4 => "视频",
            5 => "压缩文件",
            _ => "所有文件"
        };

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1;
                double width = RootGrid.XamlRoot?.Size.Width ?? RootGrid.ActualWidth;
                var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
                source.SetRegionRects(NonClientRegionKind.Caption, new[]
                {
                    new RectInt32(0, 0, Math.Max(1, (int)((width - 138) * scale)), (int)(Math.Min(32, DragRegion.ActualHeight) * scale))
                });
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[搜索窗体拖拽区] {ex.Message}"); }
        }

        private void PositionBesideMainWindow()
        {
            if (App.MainWindow is not { } main) return;
            var bounds = main.AppWindow.Position;
            var size = main.AppWindow.Size;
            const int width = 380;
            AppWindow.Resize(new SizeInt32(width, size.Height));

            int x = bounds.X + size.Width;
            int y = bounds.Y;
            // 默认贴在主窗体右侧；右侧放不下时贴着屏幕工作区的最右边，纵向也保证不出屏。
            var display = DisplayArea.GetFromPoint(
                    new PointInt32(bounds.X + size.Width / 2, bounds.Y + size.Height / 2),
                    DisplayAreaFallback.Primary)
                ?? DisplayArea.GetFromWindowId(main.AppWindow.Id, DisplayAreaFallback.Primary);
            if (display is not null)
            {
                var work = display.WorkArea;
                x = Math.Clamp(x, work.X, Math.Max(work.X, work.X + work.Width - width));
                y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));
            }
            AppWindow.Move(new PointInt32(x, y));
        }

        public void ShowBesideMainWindow()
        {
            if (App.MainWindow is not { } main) return;
            PositionBesideMainWindow();
            Activate();
            // 状态文字由 Search()/UpdateResultsStatus 接管；这里不再重复查询服务状态，
            // 否则两路异步结果会把底部的文字来回改写一次。
            // Everything shows the whole index before anything is typed; do the same whenever
            // the panel is opened so the list is never empty while the service is ready.
            Search();
            // 正常情况下过渡在 RootGrid_Loaded 里启动；若内容在 Activate 期间就已加载完，
            // 这里补一次作为兜底，保证窗口不会一直停在透明状态。
            if (RootGrid.IsLoaded) StartEntranceAnimation();
        }

        public void RefreshPositionBesideMainWindow() => PositionBesideMainWindow();

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Search();

        private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(QueryBox.Text)) SearchHistoryStore.Add(QueryBox.Text);
            Search();
        }

        /// <summary>搜索历史下拉：点选一条直接填入并搜索。</summary>
        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<string> entries = SearchHistoryStore.Get();
            var flyout = new Flyout();
            var panel = new StackPanel { Width = 260, Spacing = 0 };

            if (entries.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = LanguageService.Translate("暂无搜索历史"),
                    Margin = new Thickness(12, 10, 12, 10),
                    Opacity = 0.7
                });
            }
            else
            {
                foreach (string entry in entries)
                {
                    string query = entry;
                    var button = new Button
                    {
                        Content = query,
                        Width = 260,
                        Height = 34,
                        Padding = new Thickness(12, 0, 8, 0),
                        CornerRadius = new CornerRadius(8),
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorTabBarBrush"],
                        Style = (Style)Application.Current.Resources["MenuItemButtonStyle"]
                    };
                    LanguageService.ApplyGeneratedButtonSize(button);
                    button.Width = 260;
                    button.Height = 34;
                    button.Click += (_, _) =>
                    {
                        flyout.Hide();
                        QueryBox.Text = query;
                        QueryBox.SelectionStart = query.Length;
                        SearchHistoryStore.Add(query);
                        Search();
                    };
                    panel.Children.Add(button);
                }

                panel.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Height = 1,
                    Margin = new Thickness(8, 4, 8, 4),
                    Fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"]
                });
                var clear = new Button
                {
                    Content = LanguageService.Translate("清空历史"),
                    Width = 260,
                    Height = 34,
                    Padding = new Thickness(12, 0, 8, 0),
                    CornerRadius = new CornerRadius(8),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTabBarBrush"],
                    Style = (Style)Application.Current.Resources["MenuItemButtonStyle"]
                };
                LanguageService.ApplyGeneratedButtonSize(clear);
                clear.Width = 260;
                clear.Height = 34;
                clear.Click += (_, _) =>
                {
                    flyout.Hide();
                    SearchHistoryStore.Clear();
                };
                panel.Children.Add(clear);
            }

            flyout.Content = panel;
            flyout.FlyoutPresenterStyle = App.MainWindow is MainWindow main
                ? main.CreateModeFlyoutPresenterStyle()
                : new Style(typeof(FlyoutPresenter));
            flyout.ShouldConstrainToRootBounds = false;
            LanguageService.RegisterDynamicRoot(panel);
            flyout.ShowAt(HistoryButton, new FlyoutShowOptions
            {
                Position = new Windows.Foundation.Point(0, HistoryButton.ActualHeight),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                ShowMode = FlyoutShowMode.Standard
            });
        }

        private async void SortButton_Click(object sender, RoutedEventArgs e)
        {
            var options = new (ResultSortMode Mode, string Name, string Glyph)[]
            {
                (ResultSortMode.Name, "名称", "\uE8CB"),
                (ResultSortMode.ModifiedTime, "修改时间", "\uE823"),
                (ResultSortMode.Size, "大小", "\uE8A5")
            };
            var flyout = new Flyout();
            var panel = new StackPanel { Width = 170, Spacing = 0 };
            foreach (var option in options)
            {
                var content = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.Children.Add(new FontIcon { Glyph = option.Glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                var name = new TextBlock { Text = LanguageService.Translate(option.Name), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(name, 1);
                content.Children.Add(name);
                var direction = new TextBlock
                {
                    Text = LanguageService.Translate(option.Mode == _sortMode && _sortReversed ? "倒序" : "正序"),
                    FontSize = 10,
                    Opacity = 0.72,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(direction, 2);
                content.Children.Add(direction);
                var button = new Button
                {
                    Content = content,
                    Width = 170,
                    Height = 36,
                    Padding = new Thickness(12, 0, 8, 0),
                    CornerRadius = new CornerRadius(8),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTabBarBrush"],
                    Style = (Style)Application.Current.Resources["MenuItemButtonStyle"]
                };
                LanguageService.ApplyGeneratedButtonSize(button);
                button.Width = 170;
                button.Height = 36;
                button.Click += async (_, _) =>
                {
                    flyout.Hide();
                    if (_sortMode == option.Mode)
                        _sortReversed = !_sortReversed;
                    else
                    {
                        _sortMode = option.Mode;
                        _sortReversed = false;
                    }
                    await ApplyCurrentSortAsync();
                };
                panel.Children.Add(button);
            }

            flyout.Content = panel;
            flyout.FlyoutPresenterStyle = App.MainWindow is MainWindow mainWindow
                ? mainWindow.CreateModeFlyoutPresenterStyle()
                : new Style(typeof(FlyoutPresenter));
            flyout.ShouldConstrainToRootBounds = false;
            LanguageService.RegisterDynamicRoot(panel);
            flyout.ShowAt(SortButton, new FlyoutShowOptions
            {
                Position = new Windows.Foundation.Point(0, 0),
                Placement = FlyoutPlacementMode.TopEdgeAlignedLeft,
                ShowMode = FlyoutShowMode.Standard
            });
        }

        private async Task ApplyCurrentSortAsync()
        {
            if (_sortMode == ResultSortMode.Name)
            {
                // 名称顺序来自服务端的顺序分页：换方向要重新取页，而不是只把已加载的行反过来。
                await RunSearchAsync(resetSort: false);
                return;
            }

            long request = ++_sortRequestVersion;
            var items = Results.ToList();
            StatusText.Text = LanguageService.Translate("正在读取文件信息…");
            await Task.Run(() => Parallel.ForEach(items,
                new ParallelOptions { MaxDegreeOfParallelism = 4 }, item => item.LoadSortMetadata()));
            if (request != _sortRequestVersion) return;
            SortLoadedResults();
            UpdateResultsStatus();
        }

        private void SortLoadedResults()
        {
            var snapshot = Results.ToList();
            snapshot.Sort(CompareResults);
            Results.Clear();
            foreach (var item in snapshot) Results.Add(item);
        }

        /// <summary>
        /// 负数表示 <paramref name="left"/> 排在 <paramref name="right"/> 前面。规则与原来
        /// OrderBy/ThenBy 的写法一致：精确匹配优先；名称模式按名称升/降序，其它模式按
        /// 时间/大小排、名称始终升序兜底。
        /// </summary>
        private int CompareResults(SearchResultItem left, SearchResultItem right)
        {
            if (_sortMode == ResultSortMode.Name)
            {
                int rank = IsExactNameMatch(right.Name, _activeQuery).CompareTo(IsExactNameMatch(left.Name, _activeQuery));
                if (rank != 0) return rank;
                return _sortReversed
                    ? StringComparer.OrdinalIgnoreCase.Compare(right.Name, left.Name)
                    : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            }

            int primary = _sortMode == ResultSortMode.ModifiedTime
                ? left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc)
                : left.SizeBytes.CompareTo(right.SizeBytes);
            if (!_sortReversed) primary = -primary;
            if (primary != 0) return primary;
            return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        }

        /// <summary>
        /// 把新一页并进列表。名称升序时服务端返回的顺序就是列表顺序，直接追加；
        /// 其它排序用二分插入。绝不能用 Clear + 重新 Add —— 那会让 ListView 重置，
        /// 滚动位置跳回顶部（之前每加载一页就闪一下、回到顶部的原因）。
        /// </summary>
        private void AppendPage(List<SearchResultItem> pageItems)
        {
            // 名称模式下服务端就按显示顺序（含倒序）分页，直接追加；其它排序是本地排序，
            // 用二分插入。绝不能用 Clear + 重新 Add —— 那会让 ListView 重置滚动位置。
            if (_sortMode == ResultSortMode.Name)
            {
                foreach (var item in pageItems) Results.Add(item);
                return;
            }
            foreach (var item in pageItems) InsertSorted(item);
        }

        private void InsertSorted(SearchResultItem item)
        {
            int low = 0, high = Results.Count;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (CompareResults(Results[middle], item) <= 0) low = middle + 1;
                else high = middle;
            }
            Results.Insert(low, item);
        }

        private void UpdateResultsStatus()
        {
            StatusText.Text = _searchHasMore
                ? LanguageService.TranslateFormat("已加载 {count} 项，继续下滑加载", ("{count}", Results.Count))
                : LanguageService.TranslateFormat("共 {count} 个结果", ("{count}", Results.Count));
        }

        /// <summary>
        /// 服务端返回的状态文案是中文：认识的那几种按格式套语言包，其余直接交给
        /// Translate（有对应条目就翻译，没有就原样显示）。
        /// </summary>
        private static string LocalizeServiceMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return string.Empty;

            var noMatch = Regex.Match(message, @"^无匹配结果（索引 (\d+)/(\d+)）$");
            if (noMatch.Success)
                return LanguageService.TranslateFormat("无匹配结果（索引 {ready}/{total}）",
                    ("{ready}", noMatch.Groups[1].Value), ("{total}", noMatch.Groups[2].Value));

            var ready = Regex.Match(message, @"^(\d+)/(\d+) 个卷已就绪$");
            if (ready.Success)
                return LanguageService.TranslateFormat("{ready}/{total} 个卷已就绪",
                    ("{ready}", ready.Groups[1].Value), ("{total}", ready.Groups[2].Value));

            var building = Regex.Match(message, @"^正在建立索引（(\d+)/(\d+)）$");
            if (building.Success)
                return LanguageService.TranslateFormat("正在建立索引（{ready}/{total}）",
                    ("{ready}", building.Groups[1].Value), ("{total}", building.Groups[2].Value));

            return LanguageService.Translate(message);
        }

        private static bool IsExactNameMatch(string name, string query) =>
            !string.IsNullOrWhiteSpace(query)
            && (name.Equals(query, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(query + ".", StringComparison.OrdinalIgnoreCase));
        private void KindSwitcher_Click(object sender, RoutedEventArgs e)
        {
            var options = new (string Name, string Glyph)[]
            {
                (KindName(0), "\uE8A5"),
                (KindName(1), "\uECAA"),
                (KindName(2), "\uE7C1"),
                (KindName(3), "\uE8D6"),
                (KindName(4), "\uE714"),
                (KindName(5), "\uF012")
            };
            var flyout = new Flyout();
            var panel = new StackPanel { Width = 110, Spacing = 0 };
            for (int i = 0; i < options.Length; i++)
            {
                int selectedIndex = i;
                var option = options[i];
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                content.Children.Add(new FontIcon { Glyph = option.Glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(new TextBlock { Text = option.Name, VerticalAlignment = VerticalAlignment.Center });
                var button = new Button
                {
                    Content = content,
                    Width = 110,
                    Height = 36,
                    Padding = new Thickness(12, 0, 8, 0),
                    CornerRadius = new CornerRadius(8),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTabBarBrush"],
                    Style = (Style)Application.Current.Resources["MenuItemButtonStyle"]
                };
                LanguageService.ApplyGeneratedButtonSize(button);
                button.Width = 110;
                button.Height = 36;
                button.Click += (_, _) =>
                {
                    flyout.Hide();
                    if (_selectedKindIndex == selectedIndex) return;
                    _selectedKindIndex = selectedIndex;
                    KindSwitcherIcon.Glyph = option.Glyph;
                    KindSwitcherText.Text = LanguageService.Translate(option.Name);
                    Search();
                };
                panel.Children.Add(button);
            }

            flyout.Content = panel;
            flyout.FlyoutPresenterStyle = App.MainWindow is MainWindow mainWindow
                ? mainWindow.CreateModeFlyoutPresenterStyle()
                : new Style(typeof(FlyoutPresenter));
            flyout.ShouldConstrainToRootBounds = false;
            LanguageService.RegisterDynamicRoot(panel);
            flyout.ShowAt(KindSwitcher, new FlyoutShowOptions
            {
                Position = new Windows.Foundation.Point(0, KindSwitcher.ActualHeight),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                ShowMode = FlyoutShowMode.Standard
            });
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            QueryBox.Text = string.Empty;
            QueryBox.Focus(FocusState.Programmatic);
        }

        private void ResultRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (GetSearchResultItem(e.OriginalSource) is not { } item) return;
            HighlightResult(item);
            // 鼠标点过一行后把焦点交给列表，方向键/Enter 能接着用。
            ResultsList.Focus(FocusState.Programmatic);
        }

        private void ResultsList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (Results.Count == 0) return;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                {
                    int current = _highlightedResult is null ? -1 : Results.IndexOf(_highlightedResult);
                    int next = e.Key == Windows.System.VirtualKey.Down
                        ? Math.Min(Results.Count - 1, current + 1)
                        : Math.Max(0, current - 1);
                    if (next < 0 || next >= Results.Count) return;
                    HighlightResult(Results[next]);
                    ResultsList.ScrollIntoView(Results[next]);
                    e.Handled = true;
                    return;
                }
                case Windows.System.VirtualKey.Enter:
                    if (_highlightedResult is { } open)
                    {
                        e.Handled = true;
                        _ = OpenSearchResultAsync(open);
                    }
                    return;
                case Windows.System.VirtualKey.Space:
                    if (_highlightedResult is { } check)
                    {
                        check.IsChecked = check.IsChecked != true;
                        UpdateAddButtonState();
                        e.Handled = true;
                    }
                    return;
                case Windows.System.VirtualKey.Delete:
                    if (_highlightedResult is { } delete)
                    {
                        e.Handled = true;
                        _ = DeleteSearchResultsAsync(delete);
                    }
                    return;
                case Windows.System.VirtualKey.C:
                    if (_highlightedResult is { } copy && IsControlDown())
                    {
                        e.Handled = true;
                        _ = CopySelectionToClipboardAsync(copy, move: false);
                    }
                    return;
            }
        }

        private static bool IsControlDown()
        {
            try
            {
                return InputKeyboardSource
                    .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            }
            catch
            {
                return false;
            }
        }

        private void HighlightResult(SearchResultItem item)
        {
            if (ReferenceEquals(_highlightedResult, item)) return;
            if (_highlightedResult is not null) _highlightedResult.IsRowHighlighted = false;
            _highlightedResult = item;
            item.IsRowHighlighted = true;
        }

        private void ResultCheck_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is SearchResultItem item)
                item.IsChecked = item.IsChecked != true;
            UpdateAddButtonState();
        }

        private void UpdateAddButtonState()
        {
            var checkedItems = Results.Where(item => item.IsChecked == true).ToList();
            bool canModify = CanModifyShell;
            AddButton.IsEnabled = canModify && checkedItems.Count > 0;
            // 快速添加：主界面选中了分类，且该分类接受全部勾选文件的类型。
            QuickAddButton.IsEnabled = canModify
                && checkedItems.Count > 0
                && App.MainWindow is MainWindow main
                && main.CurrentCategory is { } category
                && CanAddAllToCategory(category, checkedItems);
        }

        private static bool CanAddAllToCategory(Category category, IReadOnlyList<SearchResultItem> files)
            => !category.IsSeparator && !category.IsSectionHeader
               && files.Count > 0
               && files.All(file => ContentKindHelper.Matches(file.Path, category.ContentKind, category.ExtensionList));

        private void ResultsList_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (GetSearchResultItem(e.OriginalSource) is not { } item) return;
            HighlightResult(item);

            var menu = new MenuFlyout();

            // —— 打开 ——
            var open = new MenuFlyoutItem { Text = LanguageService.Translate("打开"), Icon = new FontIcon { Glyph = "\uE8E5" } };
            open.Click += async (_, _) => await OpenSearchResultAsync(item);
            menu.Items.Add(open);

            var openWith = new MenuFlyoutItem { Text = LanguageService.Translate("打开方式..."), Icon = new FontIcon { Glyph = "\uE8A5" } };
            openWith.Click += (_, _) => ShowOpenWith(item.Path);
            menu.Items.Add(openWith);

            if (ContentKindHelper.IsApplication(item.Path))
            {
                var runAsAdmin = new MenuFlyoutItem { Text = LanguageService.Translate("以管理员身份启动"), Icon = new FontIcon { Glyph = "\uE7EF" } };
                runAsAdmin.Click += async (_, _) => await LaunchAsAdministratorAsync(item);
                menu.Items.Add(runAsAdmin);
            }

            menu.Items.Add(new MenuFlyoutSeparator());

            // —— 编辑：剪切 / 复制 / 重命名 / 复制路径 ——
            var cut = new MenuFlyoutItem { Text = LanguageService.Translate("剪切"), Icon = new FontIcon { Glyph = "\uE8C6" } };
            cut.Click += async (_, _) => await CopySelectionToClipboardAsync(item, move: true);
            menu.Items.Add(cut);

            var copy = new MenuFlyoutItem { Text = LanguageService.Translate("复制"), Icon = new FontIcon { Glyph = "\uE8C8" } };
            copy.Click += async (_, _) => await CopySelectionToClipboardAsync(item, move: false);
            menu.Items.Add(copy);

            var rename = new MenuFlyoutItem { Text = LanguageService.Translate("重命名"), Icon = new FontIcon { Glyph = "\uE8AC" } };
            rename.Click += async (_, _) => await RenameItemAsync(item);
            menu.Items.Add(rename);

            var copyPath = new MenuFlyoutItem { Text = LanguageService.Translate("复制路径"), Icon = new FontIcon { Glyph = "\uE71B" } };
            copyPath.Click += (_, _) =>
            {
                CopyPath(item.Path);
                StatusText.Text = LanguageService.Translate("已复制路径");
            };
            menu.Items.Add(copyPath);

            menu.Items.Add(new MenuFlyoutSeparator());

            // —— 整理：添加到分类 / 在资源管理器中显示 ——
            var add = new MenuFlyoutItem
            {
                Text = LanguageService.Translate("添加到分类"),
                Icon = new FontIcon { Glyph = "\uE710" },
                // 主窗体已真正关闭时，依赖于它的添加功能统一禁用。
                IsEnabled = CanModifyShell
            };
            add.Click += async (_, _) => await AddSelectedToCategoryAsync(item);
            menu.Items.Add(add);

            var reveal = new MenuFlyoutItem { Text = LanguageService.Translate("在资源管理器中显示"), Icon = new FontIcon { Glyph = "\uE838" } };
            reveal.Click += (_, _) => RevealInExplorer(item.Path);
            menu.Items.Add(reveal);

            menu.Items.Add(new MenuFlyoutSeparator());

            // —— 删除 ——
            var delete = new MenuFlyoutItem { Text = LanguageService.Translate("删除"), Icon = new FontIcon { Glyph = "\uE74D" } };
            delete.Click += async (_, _) => await DeleteSearchResultsAsync(item);
            menu.Items.Add(delete);

            e.Handled = true;
            menu.ShowAt(ResultsList, new FlyoutShowOptions { Position = e.GetPosition(ResultsList) });
        }

        /// <summary>右键作用对象：点中的条目已勾选时作用于全部勾选项，否则只作用于它自己。</summary>
        private List<SearchResultItem> GetContextSelection(SearchResultItem clicked)
            => clicked.IsChecked == true
                ? Results.Where(item => item.IsChecked == true).ToList()
                : new List<SearchResultItem> { clicked };

        /// <summary>把文件/文件夹以资源管理器可粘贴的形式放进剪贴板（复制或剪切）。</summary>
        private async Task CopySelectionToClipboardAsync(SearchResultItem clicked, bool move)
        {
            var files = GetContextSelection(clicked);
            var storageItems = new List<IStorageItem>();
            foreach (var file in files)
            {
                try
                {
                    if (Directory.Exists(file.Path))
                        storageItems.Add(await StorageFolder.GetFolderFromPathAsync(file.Path));
                    else if (File.Exists(file.Path))
                        storageItems.Add(await StorageFile.GetFileFromPathAsync(file.Path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FileNotFoundException)
                {
                    System.Diagnostics.Debug.WriteLine($"[全局搜索] 加入剪贴板失败 {file.Path}: {ex.Message}");
                }
            }
            if (storageItems.Count == 0)
            {
                StatusText.Text = LanguageService.Translate("文件已不存在");
                return;
            }

            try
            {
                var package = new DataPackage
                {
                    RequestedOperation = move ? DataPackageOperation.Move : DataPackageOperation.Copy
                };
                package.SetStorageItems(storageItems);
                Clipboard.SetContent(package);
            }
            catch (Exception ex)
            {
                StatusText.Text = LanguageService.TranslateFormat("复制到剪贴板失败：{error}", ("{error}", ex.Message));
                return;
            }

            StatusText.Text = move
                ? LanguageService.TranslateFormat("已剪切 {count} 项", ("{count}", storageItems.Count))
                : LanguageService.TranslateFormat("已复制 {count} 项", ("{count}", storageItems.Count));
        }

        /// <summary>重命名磁盘上的文件/文件夹，成功后同步更新结果行显示。</summary>
        private async Task RenameItemAsync(SearchResultItem item)
        {
            // 主窗体关闭后不能再居中到它，改为居中到搜索面板（并做屏幕内钳制）。
            var window = new TextInputWindow("重命名", item.Name, "", CanModifyShell ? null : this);
            await ModalWindowHelper.ShowModalAsync(this, window);
            string? newName = window.Result?.Trim();
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, item.Name, StringComparison.Ordinal)) return;

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                StatusText.Text = LanguageService.Translate("文件名包含非法字符");
                return;
            }

            string? directory = Path.GetDirectoryName(item.Path);
            if (string.IsNullOrEmpty(directory)) return;
            string target = Path.Combine(directory, newName);
            if (File.Exists(target) || Directory.Exists(target))
            {
                StatusText.Text = LanguageService.Translate("同名文件已存在");
                return;
            }

            try
            {
                if (Directory.Exists(item.Path)) Directory.Move(item.Path, target);
                else File.Move(item.Path, target);
                item.UpdatePath(target);
                StatusText.Text = LanguageService.TranslateFormat("已重命名为 {name}", ("{name}", newName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                StatusText.Text = LanguageService.TranslateFormat("重命名失败：{error}", ("{error}", ex.Message));
            }
        }

        private async void ResultsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (GetSearchResultItem(e.OriginalSource) is { } item)
                await OpenSearchResultAsync(item);
        }

        private static SearchResultItem? GetSearchResultItem(object source)
        {
            var element = source as DependencyObject;
            while (element is not null)
            {
                if (element is FrameworkElement frameworkElement && frameworkElement.DataContext is SearchResultItem item)
                    return item;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private async Task OpenSearchResultAsync(SearchResultItem item)
        {
            // 用户真正打开结果说明这次查询有意义，记进搜索历史。
            if (!string.IsNullOrWhiteSpace(_activeQuery)) SearchHistoryStore.Add(_activeQuery);
            if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
            {
                StatusText.Text = LanguageService.Translate("文件已不存在");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = item.Path,
                    UseShellExecute = true
                });
            }
            catch
            {
                ShowOpenWith(item.Path);
            }
        }

        private async Task LaunchAsAdministratorAsync(SearchResultItem item)
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                await mainWindow.LaunchSearchResultAsAdministratorAsync(item.Path);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = item.Path,
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Exception ex) { StatusText.Text = LanguageService.TranslateFormat("管理员启动失败：{error}", ("{error}", ex.Message)); }
        }

        private async Task DeleteSearchResultsAsync(SearchResultItem clickedItem)
        {
            var targets = clickedItem.IsChecked == true
                ? Results.Where(item => item.IsChecked == true).ToList()
                : new List<SearchResultItem> { clickedItem };
            string prompt = targets.Count == 1
                ? LanguageService.TranslateFormat("确定将「{name}」移到回收站吗？", ("{name}", targets[0].Name))
                : LanguageService.TranslateFormat("确定将选中的 {count} 项移到回收站吗？", ("{count}", targets.Count));
            var dialog = new ContentDialog
            {
                Title = LanguageService.Translate("删除"),
                Content = prompt,
                PrimaryButtonText = LanguageService.Translate("移到回收站"),
                CloseButtonText = LanguageService.Translate("取消"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };
            LanguageService.ApplyDialog(dialog, LanguageService.CurrentLanguageCode);
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            int deleted = 0;
            foreach (var item in targets)
            {
                try
                {
                    if (Directory.Exists(item.Path))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(item.Path,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    else if (File.Exists(item.Path))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    else
                        continue;

                    Results.Remove(item);
                    if (ReferenceEquals(_highlightedResult, item)) _highlightedResult = null;
                    deleted++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[全局搜索删除] {item.Path}: {ex.Message}");
                }
            }
            UpdateAddButtonState();
            StatusText.Text = LanguageService.TranslateFormat("已移到回收站 {count} 项", ("{count}", deleted));
        }

        private void ShowOpenWith(string path)
        {
            try
            {
                var info = new OpenAsInfo { FilePath = path, FileType = null, Flags = 0x4 };
                int result = SHOpenWithDialog(WinRT.Interop.WindowNative.GetWindowHandle(this), ref info);
                if (result < 0) Marshal.ThrowExceptionForHR(result);
            }
            catch (Exception ex)
            {
                StatusText.Text = LanguageService.TranslateFormat("无法打开方式窗口：{error}", ("{error}", ex.Message));
            }
        }

        private static void CopyPath(string path)
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(path);
            Clipboard.SetContent(package);
        }

        private static void RevealInExplorer(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
                else
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true
                    });
            }
            catch { }
        }

        private void ResultsList_Loaded(object sender, RoutedEventArgs e)
        {
            if (FindScrollViewer(ResultsList) is { } scrollViewer)
                scrollViewer.ViewChanged += ResultsScrollViewer_ViewChanged;
        }

        private void ResultsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs e)
        {
            if (e.InRecycleQueue || e.Item is not SearchResultItem item) return;
            int index = e.ItemIndex;
            if (index >= _iconLoadLimit)
                _iconLoadLimit = ((index / 200) + 1) * 200;
            if (index < _iconLoadLimit)
                _ = item.LoadIconAsync(_thumbnailSemaphore);
        }

        private void Thumbnail_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement host || host.DataContext is not SearchResultItem item) return;
            if (item.IsDirectory) return;
            _thumbnailPreviewItem = item;
            _thumbnailPreviewAnchor = e.GetCurrentPoint(RootGrid).Position;
            _thumbnailPreviewTimer.Stop();
            _thumbnailPreviewTimer.Start();
        }

        private void Thumbnail_PointerExited(object sender, PointerRoutedEventArgs e) => HideThumbnailPreview();

        private void HideThumbnailPreview()
        {
            _thumbnailPreviewTimer.Stop();
            _thumbnailPreviewItem = null;
            ThumbnailPreviewPopup.IsOpen = false;
        }

        private async Task ShowThumbnailPreviewAsync()
        {
            _thumbnailPreviewTimer.Stop();
            if (_thumbnailPreviewItem is not { } item) return;

            BitmapImage? bitmap = await LoadPreviewBitmapAsync(item.Path);
            // 加载期间鼠标可能已经移开或换到了别的行。
            if (bitmap is null || !ReferenceEquals(item, _thumbnailPreviewItem)) return;

            double pixelWidth = bitmap.PixelWidth;
            double pixelHeight = bitmap.PixelHeight;
            if (pixelWidth <= 0 || pixelHeight <= 0) return;

            double scale = Math.Min(1d, Math.Min(ThumbnailPreviewMaxSize / pixelWidth, ThumbnailPreviewMaxSize / pixelHeight));
            double imageWidth = Math.Max(1, Math.Round(pixelWidth * scale));
            double imageHeight = Math.Max(1, Math.Round(pixelHeight * scale));
            ThumbnailPreviewImage.Width = imageWidth;
            ThumbnailPreviewImage.Height = imageHeight;
            ThumbnailPreviewImage.Source = bitmap;

            const double chrome = 10;   // 边框 1×2 + 内边距 4×2
            double popupWidth = imageWidth + chrome;
            double popupHeight = imageHeight + chrome;
            double x = _thumbnailPreviewAnchor.X - popupWidth / 2;
            double y = _thumbnailPreviewAnchor.Y - popupHeight - 12;
            double maxX = Math.Max(4, RootGrid.ActualWidth - popupWidth - 4);
            ThumbnailPreviewPopup.HorizontalOffset = Math.Clamp(x, 4, maxX);
            // 上方放不下时翻到鼠标下方，避免盖住搜索框。
            ThumbnailPreviewPopup.VerticalOffset = y < 4 ? _thumbnailPreviewAnchor.Y + 26 : y;
            ThumbnailPreviewPopup.IsOpen = true;
        }

        /// <summary>
        /// 用 256 像素的 Shell 缩略图做预览（Windows 自带缓存，不整张解码原图），
        /// 这里再做一层窗口内缓存，重复悬停同一行时即时弹出。
        /// </summary>
        private async Task<BitmapImage?> LoadPreviewBitmapAsync(string path)
        {
            if (_thumbnailPreviewCache.TryGetValue(path, out BitmapImage? cached)) return cached;
            BitmapImage? bitmap = await SearchResultItem.LoadPreviewImageAsync(path, 256) as BitmapImage;
            if (_thumbnailPreviewCache.Count >= 64) _thumbnailPreviewCache.Clear();
            _thumbnailPreviewCache[path] = bitmap;
            return bitmap;
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer viewer) return viewer;
                if (FindScrollViewer(child) is { } nested) return nested;
            }
            return null;
        }

        private void ResultsScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer viewer) return;
            // 滚动时缩略图会跟着行一起移动，先收起预览，避免它悬在原地指向旧行。
            HideThumbnailPreview();
            if (viewer.ScrollableHeight - viewer.VerticalOffset >= 180) return;
            // 新行插到可视区上方时（按时间/大小排序），系统会补偿滚动偏移，这里会连续触发。
            // 只有用户确实又往下滚了（偏移超过上次触发点）才加载下一页。
            if (!double.IsNaN(_autoLoadOffset) && viewer.VerticalOffset <= _autoLoadOffset + 8) return;
            _autoLoadOffset = viewer.VerticalOffset;
            _ = LoadNextPageAsync(_searchVersion);
        }

        private async Task RefreshServiceStatusAsync()
        {
            try
            {
                var response = await _searchClient.SendAsync("status");
                StatusText.Text = LocalizeServiceMessage(response.Message ?? response.Status);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or System.UnauthorizedAccessException)
            {
                StatusText.Text = LanguageService.Translate("搜索服务未连接，请先安装并启动服务。");
            }
        }

        private async void Search() => await RunSearchAsync(resetSort: true);

        /// <summary>
        /// 清空并按当前查询重新加载第一页。<c>resetSort</c> 为 false 用于切换排序：
        /// 名称顺序由服务端分页决定，倒序必须重新从服务端取页，只把已加载的行反过来会让
        /// 下一页追加到错误的一端。
        /// </summary>
        private async Task RunSearchAsync(bool resetSort)
        {
            if (!_isInitialized) return;
            _sortRequestVersion++;
            if (resetSort)
            {
                _sortMode = ResultSortMode.Name;
                _sortReversed = false;
            }
            long version = ++_searchVersion;
            string query = QueryBox.Text.Trim();
            _activeQuery = query;
            _activeKind = _selectedKindIndex switch { 1 => "application", 2 => "image", 3 => "audio", 4 => "video", 5 => "archive", _ => "all" };
            _cursorName = _cursorVolumeId = null; _cursorFileId = null;
            _searchHasMore = false; _isLoadingMore = false;
            _iconLoadLimit = 200;
            _autoLoadOffset = double.NaN;
            if (_highlightedResult is not null) _highlightedResult.IsRowHighlighted = false;
            _highlightedResult = null;
            Results.Clear(); AddButton.IsEnabled = false; QuickAddButton.IsEnabled = false;
            HideThumbnailPreview();
            StatusText.Text = query.Length == 0
                ? LanguageService.Translate("正在载入文件…")
                : LanguageService.Translate("正在搜索…");
            try
            {
                // A little debounce while typing; nothing to debounce for the empty listing.
                await Task.Delay(query.Length == 0 ? 30 : 140);
                if (version != _searchVersion) return;
                await LoadNextPageAsync(version);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or TimeoutException or System.UnauthorizedAccessException or InvalidDataException)
            {
                if (version == _searchVersion) StatusText.Text = LanguageService.Translate("搜索服务连接失败，请检查服务状态。");
            }
        }

        private async Task LoadNextPageAsync(long version)
        {
            if (version != _searchVersion || _isLoadingMore || !_searchHasMore && Results.Count > 0) return;
            _isLoadingMore = true;
            try
            {
                var response = await _searchClient.SendAsync("search", _activeQuery, _activeKind, 500,
                    afterName: _cursorName, afterVolumeId: _cursorVolumeId, afterFileId: _cursorFileId,
                    shouldContinue: () => version == _searchVersion,
                    descending: _sortMode == ResultSortMode.Name && _sortReversed);
                if (version != _searchVersion) return;
                if (response.Status != "ok") { StatusText.Text = response.Message ?? response.Status; return; }
                var hits = response.Results ?? new();
                var pageItems = hits.Where(hit => KindMatches(hit.Path, _selectedKindIndex))
                    .Select(hit => new SearchResultItem(hit)).ToList();
                if (_sortMode != ResultSortMode.Name)
                {
                    StatusText.Text = LanguageService.Translate("正在读取文件信息…");
                    await Task.Run(() => Parallel.ForEach(pageItems,
                        new ParallelOptions { MaxDegreeOfParallelism = 4 }, item => item.LoadSortMetadata()));
                    if (version != _searchVersion) return;
                }
                AppendPage(pageItems);
                _searchHasMore = response.HasMore;
                _cursorName = response.NextName;
                _cursorVolumeId = response.NextVolumeId;
                _cursorFileId = response.NextFileId;
                UpdateResultsStatus();
                if (Results.Count == 0 && !_searchHasMore && !string.IsNullOrEmpty(response.Message))
                    StatusText.Text = LocalizeServiceMessage(response.Message);
                if (_searchHasMore && hits.Count == 0)
                    DispatcherQueue.TryEnqueue(() => _ = LoadNextPageAsync(version));
                else if (_searchHasMore && pageItems.Count > 0 && FindScrollViewer(ResultsList) is { } scrollViewer
                    && scrollViewer.ScrollableHeight <= 0)
                    // 只有列表根本还不能滚动时才继续补页（例如整页都被分类过滤掉）；能滚动时
                    // 交给滚动事件，避免插入到上方后“贴底”状态反复触发加载。
                    DispatcherQueue.TryEnqueue(() => _ = LoadNextPageAsync(version));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or TimeoutException or System.UnauthorizedAccessException or InvalidDataException)
            {
                if (version == _searchVersion) StatusText.Text = LanguageService.Translate("搜索服务连接失败，请检查服务状态。");
            }
            finally
            {
                if (version == _searchVersion) _isLoadingMore = false;
            }
        }

        private static bool KindMatches(string path, int selectedIndex)
        {
            var kind = selectedIndex switch { 1 => ContentKind.Application, 2 => ContentKind.Image, 3 => ContentKind.Audio, 4 => ContentKind.Video, 5 => ContentKind.Archive, _ => ContentKind.All };
            return ContentKindHelper.Matches(path, kind, "");
        }

        private async void Add_Click(object sender, RoutedEventArgs e) => await AddSelectedToCategoryAsync();

        private async Task AddSelectedToCategoryAsync(SearchResultItem? clickedItem = null)
        {
            if (!CanModifyShell)
            {
                StatusText.Text = LanguageService.Translate("主窗口已关闭，暂时无法添加到分类。");
                return;
            }

            var selected = Results.Where(item => item.IsChecked == true).ToList();
            if (selected.Count == 0 && clickedItem is not null) selected.Add(clickedItem);
            if (selected.Count == 0) return;

            var categories = CategoryRepository.GetVisible(EffectiveMode)
                .Where(c => !c.IsSeparator && !c.IsSectionHeader)
                .ToList();
            if (categories.Count == 0) return;

            // 独立的“添加到”窗口（外观同重命名窗口），选择用和搜索页分类菜单同款的 Flyout。
            var window = new AddToCategoryWindow(categories, selected.Select(item => item.Path).ToList(),
                (App.MainWindow as MainWindow)?.CurrentCategory);
            await ModalWindowHelper.ShowModalAsync(this, window);
            if (window.Result is not Category category) return;
            AddFilesToCategory(category, selected);
        }

        /// <summary>把已勾选的条目直接加进主界面当前选中的分类。</summary>
        private async void QuickAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!CanModifyShell)
            {
                StatusText.Text = LanguageService.Translate("主窗口已关闭，暂时无法添加到分类。");
                return;
            }
            if (App.MainWindow is not MainWindow main || main.CurrentCategory is not { } category) return;
            var selected = Results.Where(item => item.IsChecked == true).ToList();
            if (selected.Count == 0) return;
            if (!CanAddAllToCategory(category, selected))
            {
                UpdateAddButtonState();
                return;
            }
            AddFilesToCategory(category, selected);
        }

        private void AddFilesToCategory(Category category, IReadOnlyList<SearchResultItem> selected)
        {
            // 主窗体已真正关闭时不再写分类记录，避免操作一个已经销毁的界面。
            if (!CanModifyShell) return;

            // 对话框打开期间主界面可能切换了工作模式：用当前模式写入，并确认分类仍属于该模式。
            AppMode mode = EffectiveMode;
            if (!CategoryRepository.GetVisible(mode).Any(c => string.Equals(c.Id, category.Id, StringComparison.Ordinal)))
                return;

            int added = 0;
            foreach (var file in selected)
            {
                if (!ContentKindHelper.Matches(file.Path, category.ContentKind, category.ExtensionList)) continue;
                if (ShortcutRepository.ExistsInCategory(category.Id, mode, file.Path)) continue;
                ShortcutRepository.Add(new ShortcutItem
                {
                    Name = Path.GetFileNameWithoutExtension(file.Path),
                    TargetPath = file.Path,
                    IsFile = !ContentKindHelper.IsApplication(file.Path),
                    CategoryId = category.Id,
                    IsShared = category.IsShared,
                    BelongingMode = mode,
                    SortOrder = ShortcutRepository.GetNextSortOrder(category.Id, category.IsShared)
                });
                added++;
            }
            StatusText.Text = LanguageService.TranslateFormat("已添加 {count} 个文件", ("{count}", added));
            if (App.MainWindow is not MainWindow mainWindow) return;
            mainWindow.RefreshAfterGlobalSearchAdd(category);
            if (!mainWindow.ClearSearchSelectionAfterAdd) return;
            // 设置项开启时，添加完成后取消所有勾选，方便继续挑选下一批。
            foreach (var item in Results) item.IsChecked = false;
            UpdateAddButtonState();
        }
        private void Close_Click(object sender, RoutedEventArgs e) => WindowFadeController.CloseWithAnimation(this);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenAsInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
            [MarshalAs(UnmanagedType.LPWStr)] public string? FileType;
            public uint Flags;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true, EntryPoint = "SHOpenWithDialog")]
        private static extern int SHOpenWithDialog(IntPtr parentWindow, ref OpenAsInfo info);
    }
    public sealed class SearchResultItem : INotifyPropertyChanged
    {
        private ImageSource? _iconSource;
        private int _iconLoadStarted;
        private int _sortMetadataLoaded;
        private bool? _isChecked;
        private bool _isRowHighlighted;
        public string Path { get; private set; }
        public string Name => System.IO.Path.GetFileName(Path);
        public bool IsDirectory { get; }
        public DateTime LastWriteTimeUtc { get; private set; }
        public long SizeBytes { get; private set; }
        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CheckedOpacity)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UncheckedOpacity)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NormalHighlightOpacity)));
            }
        }
        public double CheckedOpacity => IsChecked == true ? 1 : 0;
        public double UncheckedOpacity => IsChecked == true ? 0 : 1;
        public bool IsRowHighlighted
        {
            get => _isRowHighlighted;
            set
            {
                if (_isRowHighlighted == value) return;
                _isRowHighlighted = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRowHighlighted)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowHighlightOpacity)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NormalHighlightOpacity)));
            }
        }
        public double RowHighlightOpacity => IsRowHighlighted ? 1 : 0;
        public double NormalHighlightOpacity => IsChecked == true ? 0 : RowHighlightOpacity;
        public string PlaceholderGlyph { get; }
        public ImageSource? IconSource
        {
            get => _iconSource;
            private set { _iconSource = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        internal SearchResultItem(PipeSearchHit hit)
        {
            Path = hit.Path;
            IsDirectory = hit.IsDirectory;
            PlaceholderGlyph = hit.IsDirectory ? "\uE8B7" : "\uE8A5";
        }

        /// <summary>重命名成功后刷新这一行的路径与显示名（模板用的是 OneWay 绑定）。</summary>
        internal void UpdatePath(string path)
        {
            Path = path;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Path)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }

        internal void LoadSortMetadata()        {
            if (Interlocked.Exchange(ref _sortMetadataLoaded, 1) != 0) return;
            try
            {
                if (IsDirectory)
                {
                    var directory = new DirectoryInfo(Path);
                    if (!directory.Exists) return;
                    LastWriteTimeUtc = directory.LastWriteTimeUtc;
                    SizeBytes = 0;
                }
                else
                {
                    var file = new FileInfo(Path);
                    if (!file.Exists) return;
                    LastWriteTimeUtc = file.LastWriteTimeUtc;
                    SizeBytes = file.Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                // Inaccessible or stale results sort after entries with readable metadata.
            }
        }

        internal async Task LoadIconAsync(SemaphoreSlim throttle)
        {
            if (Interlocked.Exchange(ref _iconLoadStarted, 1) != 0) return;
            try
            {
                await throttle.WaitAsync();
                try
                {
                    if (IsDirectory) return;
                    if (!File.Exists(Path))
                        return;
                    IconSource = await LoadShellIconAsync(Path);
                }
                finally { throttle.Release(); }
            }
            catch (UnauthorizedAccessException)
            {
                // Use the Shell's file icon for protected files that cannot provide a content thumbnail.
                IconSource = await LoadShellIconAsync(Path);
            }
            catch (FileNotFoundException)
            {
                // Search results can become stale when a temporary file is removed after indexing.
            }
            catch (DirectoryNotFoundException)
            {
                // The containing directory may disappear after the result was indexed.
            }
            catch (ArgumentException)
            {
                IconSource = await LoadShellIconAsync(Path);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Fall back to the associated Shell icon when a content thumbnail is unavailable.
                IconSource = await LoadShellIconAsync(Path);
            }
        }

        /// <summary>供悬停预览使用：请求更大尺寸的 Shell 缩略图。</summary>
        internal static Task<ImageSource?> LoadPreviewImageAsync(string path, int size = 256)
            => LoadShellIconAsync(path, size);

        private static async Task<ImageSource?> LoadShellIconAsync(string path, int size = 96)
        {
            try
            {
                byte[]? png = await Task.Run(() => ShellIconHelper.GetThumbnailPngBytes(path, size));
                if (png is null) return null;

                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(png);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                stream.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                return null;
            }
        }
    }
}
