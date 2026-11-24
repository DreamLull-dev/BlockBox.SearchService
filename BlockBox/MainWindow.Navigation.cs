using BlockBox.Data;
using BlockBox.Helpers;
using BlockBox.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Windows.Foundation;
using Windows.UI;

namespace BlockBox
{
    public sealed partial class MainWindow : Window
    {
        private void ModeSwitcher_Click(object sender, RoutedEventArgs e)
        {
            var flyout = new Flyout();
            var panel = new StackPanel
            {
                Width = 100,
                Orientation = Orientation.Vertical,
                Spacing = 0
            };

            foreach (var mode in ModeRepository.GetAll())
            {
                var btn = new Button
                {
                    Content = mode.IsBuiltIn
                        ? LimitMenuLabel(LanguageService.Translate(mode.Name))
                        : LimitMenuLabel(mode.Name),
                    Width = 100,
                    Height = 36,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(0,-3,0,0),
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)
                            Application.Current.Resources["TextFillColorTabBarBrush"],

                    Style = (Style)Application.Current.Resources["MenuItemButtonStyle"]
                };
                LanguageService.ApplyGeneratedButtonSize(btn);
                btn.Width = 100;

                btn.Click += (s, args) =>
                {
                    flyout.Hide();
                    SwitchMode(mode.Mode);
                };

                panel.Children.Add(btn);
            }

            flyout.Content = panel;
            flyout.FlyoutPresenterStyle = CreateModeFlyoutPresenterStyle();
            LanguageService.RegisterDynamicRoot(panel);

            // ⚠️ 关键：禁止系统约束到窗口内，避免向上翻转
            flyout.ShouldConstrainToRootBounds = false;

            flyout.ShowAt(ModeSwitcher, new FlyoutShowOptions
            {
                Position = new Windows.Foundation.Point(0, ModeSwitcher.ActualHeight),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                ShowMode = FlyoutShowMode.Standard
            });
        }

        internal Style CreateModeFlyoutPresenterStyle()
        {
            string tabBarBgColor = _ini.ReadValue("Global", "PrimaryBackground", SettingsDefaults.PrimaryBackground);
            var tabBarColor = ParseColor(tabBarBgColor);
            var presenterStyle = new Style(typeof(FlyoutPresenter));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(12)));

            const double whitenAmount = 0.30;
            byte BlendWithWhite(byte c) =>
                (byte)Math.Clamp(Math.Round(c + (255 - c) * whitenAmount), 0, 255);
            var backgroundColor = Color.FromArgb(
                (byte)Math.Round(255 * 0.45),
                BlendWithWhite(tabBarColor.R),
                BlendWithWhite(tabBarColor.G),
                BlendWithWhite(tabBarColor.B));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, new SolidColorBrush(backgroundColor)));

            var borderColor = Color.FromArgb(
                (byte)Math.Round(255 * 0.5),
                tabBarColor.R,
                tabBarColor.G,
                tabBarColor.B);
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.BorderBrushProperty, new SolidColorBrush(borderColor)));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(1)));
            return presenterStyle;
        }

        private void LoadTabsFromDb()
        {
            // Build the desired list first, then reconcile it with the existing
            // collection. Clearing and rebuilding prevents TabView from seeing
            // an individual add/remove when a hidden tab becomes visible.
            var desiredTabs = new List<MyTabItem>();

            if (_favoritesTabPinned)
            {
                desiredTabs.Add(new MyTabItem
                {
                    Title = "收藏",
                    IconGlyph = "\uE734",              // ⭐
                    IsClosable = true,
                    BelongingMode = _currentMode,
                    BuiltinKind = BuiltinTabKind.Favorites,
                    IsPinned = true
                });
            }

            if (_recentTabPinned)
            {
                desiredTabs.Add(new MyTabItem
                {
                    Title = "最近访问",
                    IconGlyph = "\uE81C",              // ⭐
                    IsClosable = true,
                    BelongingMode = _currentMode,
                    BuiltinKind = BuiltinTabKind.Recent,
                    IsPinned = true
                });
            }

            foreach (var t in BlockBox.Data.TabRepository.GetVisible(_currentMode))
            {
                if (t.IsPinned)
                    desiredTabs.Add(t);
            }

            // Remove only entries that are no longer visible.
            for (int i = TabItems.Count - 1; i >= 0; i--)
            {
                if (!desiredTabs.Any(wanted => IsSameTabIdentity(TabItems[i], wanted)))
                    TabItems.RemoveAt(i);
            }

            // Insert newly visible tabs and move existing ones only when needed.
            for (int targetIndex = 0; targetIndex < desiredTabs.Count; targetIndex++)
            {
                var wanted = desiredTabs[targetIndex];
                int currentIndex = -1;
                for (int i = 0; i < TabItems.Count; i++)
                {
                    if (IsSameTabIdentity(TabItems[i], wanted))
                    {
                        currentIndex = i;
                        break;
                    }
                }

                if (currentIndex < 0)
                {
                    if (_tabBeingRestored != null && IsSameTabIdentity(wanted, _tabBeingRestored))
                        _tabsPendingFadeIn.Add(wanted);
                    TabItems.Insert(Math.Min(targetIndex, TabItems.Count), wanted);
                    continue;
                }

                // MyTabItem is not observable; replace changed records so x:Bind
                // refreshes the header text/icon without rebuilding every tab.
                if (!HasSameTabData(TabItems[currentIndex], wanted))
                    TabItems[currentIndex] = wanted;

                if (currentIndex != targetIndex)
                    TabItems.Move(currentIndex, targetIndex);
            }
        }

        /// <summary>
        /// TabView 的 Header 是 OneTime x:Bind；集合项被替换后容器可能还显示旧 Header。
        /// 先尝试直接改 Header；如果容器没刷新，就把集合项移除再插回，强制重建容器。
        /// </summary>
        private void RefreshTabHeader(MyTabItem item)
        {
            int index = TabItems.IndexOf(item);
            if (index < 0) return;

            MyTabView.UpdateLayout();
            if (MyTabView.ContainerFromIndex(index) is TabViewItem container)
            {
                container.Header = item;
                container.UpdateLayout();
            }

            // 最可靠：移除再插回，TabView 会重新套用 TabItemTemplate，Header 一定指向新对象。
            TabItems.RemoveAt(index);
            TabItems.Insert(index, item);
            MyTabView.UpdateLayout();
        }

        private static bool IsSameTabIdentity(MyTabItem left, MyTabItem right)
        {
            if (left.BuiltinKind != BuiltinTabKind.None || right.BuiltinKind != BuiltinTabKind.None)
            {
                return left.BuiltinKind != BuiltinTabKind.None
                    && left.BuiltinKind == right.BuiltinKind
                    && left.BelongingMode == right.BelongingMode;
            }

            return left.BelongingMode == right.BelongingMode
                && string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSameTabData(MyTabItem left, MyTabItem right)
        {
            return left.Title == right.Title
                && left.IconGlyph == right.IconGlyph
                && left.IsClosable == right.IsClosable
                && left.BelongingMode == right.BelongingMode
                && left.SortOrder == right.SortOrder
                && left.BuiltinKind == right.BuiltinKind
                && left.Note == right.Note
                && left.IsPinned == right.IsPinned
                && left.ContentKind == right.ContentKind
                && left.ExtensionList == right.ExtensionList
                && left.SortMode == right.SortMode;
        }
        private readonly HashSet<TabViewItem> _hookedContainers = new();
        private ScrollViewer? _navMenuScrollViewer;
        private ThumbDragState? _activeCustomScrollThumb;

        private sealed class ThumbDragState
        {
            public ScrollViewer ScrollViewer { get; init; } = null!;
            public FrameworkElement Track { get; init; } = null!;
            public FrameworkElement Thumb { get; init; } = null!;
            public double PointerOffset { get; init; }
        }

        private void NavMenuList_Loaded(object sender, RoutedEventArgs e)
        {
            NavMenuList.ApplyTemplate();
            _navMenuScrollViewer = FindScrollViewer(NavMenuList);
            if (_navMenuScrollViewer == null)
                return;

            _navMenuScrollViewer.ViewChanged += NavMenuScrollViewer_ViewChanged;
            _navMenuScrollViewer.SizeChanged += CustomScrollViewer_SizeChanged;
            NavMenuList.SizeChanged += NavMenuList_SizeChanged;
            NavScrollBarTrack.SizeChanged += NavScrollBarTrack_SizeChanged;
            UpdateCustomScrollBarColors();
            QueueCustomScrollBarUpdate(_navMenuScrollViewer, NavScrollBarTrack, NavScrollBarThumb);
        }

        private void NavMenuList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_navMenuScrollViewer != null)
                QueueCustomScrollBarUpdate(_navMenuScrollViewer, NavScrollBarTrack, NavScrollBarThumb);
        }

        private void ShortcutScrollViewer_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshShortcutScrollBar();
        }

        private void RefreshShortcutScrollBar()
        {
            if (ShortcutScrollViewer == null || ShortcutScrollBarTrack == null || ShortcutScrollBarThumb == null)
                return;

            QueueCustomScrollBarUpdate(ShortcutScrollViewer, ShortcutScrollBarTrack, ShortcutScrollBarThumb);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                QueueCustomScrollBarUpdate(ShortcutScrollViewer, ShortcutScrollBarTrack, ShortcutScrollBarThumb));
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                QueueCustomScrollBarUpdate(ShortcutScrollViewer, ShortcutScrollBarTrack, ShortcutScrollBarThumb));
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer scrollViewer)
                    return scrollViewer;

                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void NavMenuScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_navMenuScrollViewer != null)
                QueueCustomScrollBarUpdate(_navMenuScrollViewer, NavScrollBarTrack, NavScrollBarThumb);
        }

        private void ShortcutScrollBarTrack_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueCustomScrollBarUpdate(ShortcutScrollViewer, ShortcutScrollBarTrack, ShortcutScrollBarThumb);
        }

        private void NavScrollBarTrack_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_navMenuScrollViewer != null)
                QueueCustomScrollBarUpdate(_navMenuScrollViewer, NavScrollBarTrack, NavScrollBarThumb);
        }

        private void CustomScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
                return;

            if (ReferenceEquals(scrollViewer, ShortcutScrollViewer))
                QueueCustomScrollBarUpdate(scrollViewer, ShortcutScrollBarTrack, ShortcutScrollBarThumb);
            else if (ReferenceEquals(scrollViewer, _navMenuScrollViewer))
                QueueCustomScrollBarUpdate(scrollViewer, NavScrollBarTrack, NavScrollBarThumb);
        }

        private void QueueCustomScrollBarUpdate(ScrollViewer scrollViewer, FrameworkElement track, FrameworkElement thumb)
        {
            DispatcherQueue.TryEnqueue(() => UpdateCustomScrollBar(scrollViewer, track, thumb));
        }

        private void UpdateCustomScrollBar(ScrollViewer scrollViewer, FrameworkElement track, FrameworkElement thumb)
        {
            double viewport = scrollViewer.ViewportHeight;
            double scrollableHeight = scrollViewer.ScrollableHeight;
            double extent = viewport + scrollableHeight;
            double trackHeight = track.ActualHeight;
            if (viewport <= 0 || scrollableHeight <= 1 || trackHeight <= 0)
            {
                track.Opacity = 0;
                track.IsHitTestVisible = false;
                return;
            }

            bool isSidebarTrack = ReferenceEquals(track, NavScrollBarTrack);
            bool visible = isSidebarTrack ? _isSidebarExpanded : !_isSettingsVisible;
            track.Opacity = visible ? 1 : 0;
            track.IsHitTestVisible = visible;
            double thumbHeight = Math.Clamp(trackHeight * viewport / extent, Math.Min(28, trackHeight), trackHeight);
            double maxThumbOffset = Math.Max(0, trackHeight - thumbHeight);
            double thumbOffset = maxThumbOffset * Math.Clamp(scrollViewer.VerticalOffset / scrollableHeight, 0, 1);
            thumb.Height = thumbHeight;
            thumb.Margin = new Thickness(0);
            thumb.VerticalAlignment = VerticalAlignment.Top;
            thumb.RenderTransform = new TranslateTransform { Y = thumbOffset };
        }

        private void UpdateCustomScrollBarColors()
        {
            if (!Application.Current.Resources.TryGetValue("AppPrimaryBackgroundBrush", out var value)
                || value is not Brush brush)
            {
                return;
            }

            NavScrollBarThumb.Background = brush;
            if (FindChildByName<Border>(NavScrollBarTrack, "NavScrollBarTrackFill") is Border navTrackFill)
                navTrackFill.Background = brush;
            ShortcutScrollBarThumb.Background = brush;
            if (FindChildByName<Border>(ShortcutScrollBarTrack, "ShortcutScrollBarTrackFill") is Border shortcutTrackFill)
                shortcutTrackFill.Background = brush;
            SettingsViewControl?.ApplyScrollBarPrimaryBrush(brush);
        }

        private void RefreshSettingsScrollBar()
        {
            SettingsViewControl?.RefreshScrollBar();
        }

        private void SettingsViewControl_ScrollBarRefreshRequested(object? sender, EventArgs e)
        {
            RefreshSettingsScrollBar();
        }

        private void SetSidebarScrollBarVisibility(bool visible)
        {
            bool canScroll = _navMenuScrollViewer?.ScrollableHeight > 1;
            bool show = visible && canScroll;
            NavScrollBarTrack.Opacity = show ? 1 : 0;
            NavScrollBarTrack.IsHitTestVisible = show;
        }

        private void ShortcutRepeater_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueCustomScrollBarUpdate(ShortcutScrollViewer, ShortcutScrollBarTrack, ShortcutScrollBarThumb);
        }

        private void CustomScrollBarTrack_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement track || track.Name == "NavScrollBarTrack" && _navMenuScrollViewer == null)
                return;

            var scrollViewer = track.Name == "NavScrollBarTrack" ? _navMenuScrollViewer : ShortcutScrollViewer;
            if (scrollViewer == null || track.ActualHeight <= 0)
                return;

            var point = e.GetCurrentPoint(track).Position;
            double thumbHeight = track.Name == "NavScrollBarTrack" ? NavScrollBarThumb.ActualHeight : ShortcutScrollBarThumb.ActualHeight;
            double availableTravel = Math.Max(1, track.ActualHeight - thumbHeight);
            double ratio = Math.Clamp((point.Y - thumbHeight / 2) / availableTravel, 0, 1);
            if (ReferenceEquals(scrollViewer, ShortcutScrollViewer))
            {
                _isShortcutScrollViewerScrolling = true;
                StopShortcutAnimationsForScroll();
            }
            scrollViewer.ChangeView(null, ratio * scrollViewer.ScrollableHeight, null, true);
            e.Handled = true;
        }

        private void CustomScrollBarThumb_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement thumb || thumb.Parent is not Grid track)
                return;

            var scrollViewer = track.Name == "NavScrollBarTrack" ? _navMenuScrollViewer : ShortcutScrollViewer;
            if (scrollViewer == null)
                return;

            _activeCustomScrollThumb = new ThumbDragState
            {
                ScrollViewer = scrollViewer,
                Track = track,
                Thumb = thumb,
                PointerOffset = e.GetCurrentPoint(track).Position.Y - (thumb.RenderTransform is TranslateTransform transform ? transform.Y : 0)
            };
            if (ReferenceEquals(scrollViewer, ShortcutScrollViewer))
            {
                _isCustomShortcutScrollbarDragging = true;
                _isShortcutScrollViewerScrolling = true;
                StopShortcutAnimationsForScroll();
            }
            thumb.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void CustomScrollBarThumb_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_activeCustomScrollThumb is not { } drag
                || (!ReferenceEquals(sender, drag.Thumb) && !ReferenceEquals(sender, drag.Track)))
                return;

            double trackHeight = drag.Track.ActualHeight;
            double thumbHeight = drag.Thumb.ActualHeight;
            double maxThumbOffset = Math.Max(0, trackHeight - thumbHeight);
            if (maxThumbOffset <= 0)
                return;

            double pointerY = e.GetCurrentPoint(drag.Track).Position.Y;
            double thumbOffset = Math.Clamp(pointerY - drag.PointerOffset, 0, maxThumbOffset);
            double ratio = thumbOffset / maxThumbOffset;
            drag.ScrollViewer.ChangeView(null, ratio * drag.ScrollViewer.ScrollableHeight, null, true);
            e.Handled = true;
        }

        private void CustomScrollBarThumb_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_activeCustomScrollThumb is { } drag
                && (ReferenceEquals(sender, drag.Thumb) || ReferenceEquals(sender, drag.Track)))
            {
                EndCustomScrollBarThumbDrag(drag.Thumb);
            }
        }

        private void CustomScrollBarThumb_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_activeCustomScrollThumb is { } drag
                && (ReferenceEquals(sender, drag.Thumb) || ReferenceEquals(sender, drag.Track)))
            {
                EndCustomScrollBarThumbDrag(drag.Thumb);
            }
        }

        private void EndCustomScrollBarThumbDrag(FrameworkElement thumb)
        {
            if (_activeCustomScrollThumb is { } drag && ReferenceEquals(drag.Thumb, thumb))
            {
                bool wasShortcutScrollbar = ReferenceEquals(drag.ScrollViewer, ShortcutScrollViewer);
                _activeCustomScrollThumb = null;
                if (wasShortcutScrollbar)
                {
                    _isCustomShortcutScrollbarDragging = false;
                    _isShortcutScrollViewerScrolling = false;
                    _lastShortcutMotionSync = DateTimeOffset.MinValue;
                }
            }
        }

        private void MyTabView_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is DependencyObject root)
                ConfigureTabHeaderItemTransitions(root);

            _hasTabViewCompletedInitialLoad = true;
            // 首次先不用隐式尺寸动画，直接把宽度落好；后续尺寸变化再走 220ms 动画。
            ApplyTabWidthsWithoutAnimation();
            ScheduleTabWidthUpdate(forceImmediate: true);
        }

        /// <summary>
        /// 首次布局 / 窗口调整大小时直接应用标签宽度，临时关掉 Size 隐式动画，
        /// 避免启动后先按内容宽度显示、100ms 后又“从左往右长开”的二次加载观感。
        /// </summary>
        private void ApplyTabWidthsWithoutAnimation()
        {
            var saved = new List<(Visual Visual, ImplicitAnimationCollection? Animations)>();
            foreach (var item in TabItems)
            {
                if (MyTabView.ContainerFromItem(item) is not TabViewItem container)
                    continue;

                var visual = ElementCompositionPreview.GetElementVisual(container);
                saved.Add((visual, visual.ImplicitAnimations));
                visual.ImplicitAnimations = null;
            }

            try
            {
                UpdateTabWidths();
                MyTabView.UpdateLayout();
            }
            finally
            {
                foreach (var (visual, animations) in saved)
                    visual.ImplicitAnimations = animations;
            }
        }

        private ListViewBase? _tabHeaderList;

        private void ConfigureTabHeaderItemTransitions(DependencyObject root)
        {
            if (root is ListViewBase listView)
            {
                _tabHeaderList = listView;
                listView.ItemContainerTransitions = new TransitionCollection
                {
                    new AddDeleteThemeTransition(),
                    new RepositionThemeTransition()
                };
                return;
            }

            // Avoid changing list transitions inside tab content.
            if (root is TabViewItem)
                return;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                ConfigureTabHeaderItemTransitions(VisualTreeHelper.GetChild(root, i));
        }

        private const double TabHeaderMinimumWidth = 60;
        private const double TabHeaderMaximumWidth = 152;
        private bool _tabWidthUpdatePending;
        private bool _isPointerOverTabView;
        private bool _tabWidthUpdateDeferredUntilPointerExit;

        private void ScheduleTabWidthUpdate(bool deferWhilePointerOver = false, bool forceImmediate = false)
        {
            if (deferWhilePointerOver && _isPointerOverTabView)
            {
                _tabWidthUpdateDeferredUntilPointerExit = true;
                return;
            }

            if (forceImmediate)
            {
                _tabWidthUpdateDeferredUntilPointerExit = false;
            }
            else if (_tabWidthUpdateDeferredUntilPointerExit)
            {
                if (_isPointerOverTabView)
                    return;

                _tabWidthUpdateDeferredUntilPointerExit = false;
            }

            if (_tabWidthUpdatePending)
                return;

            _tabWidthUpdatePending = true;
            bool enqueued = DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                () =>
                {
                    _tabWidthUpdatePending = false;

                    if (_tabWidthUpdateDeferredUntilPointerExit && _isPointerOverTabView)
                        return;

                    _tabWidthUpdateDeferredUntilPointerExit = false;

                    UpdateTabWidths();
                });

            if (!enqueued)
                _tabWidthUpdatePending = false;
        }

        private void UpdateTabWidths()
        {
            int count = TabItems.Count;
            double availableWidth = MyTabView.ActualWidth;
            if (count == 0 || availableWidth <= 0)
                return;

            double itemWidth = Math.Clamp(
                availableWidth / count, TabHeaderMinimumWidth, TabHeaderMaximumWidth);
            foreach (var item in TabItems)
            {
                if (MyTabView.ContainerFromItem(item) is TabViewItem container)
                {
                    container.MinWidth = itemWidth;
                    container.MaxWidth = itemWidth;
                    container.Width = itemWidth;
                }
            }
        }

        private void TabHeader_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_tabDragPointer != null || _isTabDragging || _isTabDropAnimating || IsEditPanelOpen
                || sender is not TabViewItem container
                || container.DataContext is not MyTabItem item
                || item.BuiltinKind != BuiltinTabKind.None
                || !e.GetCurrentPoint(container).Properties.IsLeftButtonPressed)
                return;

            var source = e.OriginalSource as DependencyObject;
            while (source != null && !ReferenceEquals(source, container))
            {
                if (source is Button)
                    return;
                source = VisualTreeHelper.GetParent(source);
            }

            var point = e.GetCurrentPoint(TabDragOverlay).Position;
            var containerOrigin = container.TransformToVisual(TabDragOverlay).TransformPoint(new Windows.Foundation.Point(0, 0));
            _draggingTabItem = item;
            _draggingTabContainer = container;
            _tabDragPointer = e.Pointer;
            _tabDragPointerId = e.Pointer.PointerId;
            _tabDragStartPoint = point;
            _tabDragGrabOffsetX = point.X - containerOrigin.X;
            _tabDragSourceTop = containerOrigin.Y;
            _tabDragGhostWidth = container.ActualWidth;
            _tabDragOriginalOpacity = container.Opacity;
            _tabDragOriginalOrder = TabItems.ToList();

            if (!container.CapturePointer(e.Pointer))
                ResetTabDragState();
        }

        private void TabHeader_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_tabDragPointer == null || _tabDragPointerId != e.Pointer.PointerId
                || _draggingTabItem == null)
                return;

            var point = e.GetCurrentPoint(TabDragOverlay).Position;
            if (!_isTabDragging)
            {
                if (Math.Abs(point.X - _tabDragStartPoint.X) < TabDragThreshold)
                    return;

                BeginTabHeaderDrag();
            }

            if (!_isTabDragging || _tabDragGhost == null)
                return;

            var maxLeft = Math.Max(0, TabDragOverlay.ActualWidth - _tabDragGhostWidth);
            var left = Math.Clamp(point.X - _tabDragGrabOffsetX, 0, maxLeft);
            if (_tabDragGhostVisual != null)
            {
                _tabDragGhostVisual.Offset = new Vector3(
                    (float)left, (float)_tabDragSourceTop, 0);
            }
            ReorderTabAt(left + _tabDragGhostWidth / 2);
            e.Handled = true;
        }

        private void TabHeader_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_tabDragPointerId != e.Pointer.PointerId)
                return;

            BeginTabDropAnimation(commit: true);
        }

        private void TabHeader_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_tabDragPointerId == e.Pointer.PointerId)
                BeginTabDropAnimation(commit: false);
        }

        private void TabHeader_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_tabDragPointerId == e.Pointer.PointerId)
                BeginTabDropAnimation(commit: false);
        }

        private void BeginTabHeaderDrag()
        {
            if (_draggingTabItem == null || _draggingTabContainer == null)
                return;

            var item = _draggingTabItem;
            var container = _draggingTabContainer;
            _tabDragVisualStates.Clear();
            _tabDragSlotLefts.Clear();
            foreach (var tab in _tabDragOriginalOrder)
            {
                if (MyTabView.ContainerFromItem(tab) is not TabViewItem tabContainer)
                    continue;

                var origin = tabContainer.TransformToVisual(TabDragOverlay)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
                var bounds = new Rect(origin.X, origin.Y, tabContainer.ActualWidth, tabContainer.ActualHeight);
                var visual = ElementCompositionPreview.GetElementVisual(tabContainer);
                visual.StopAnimation(TabDragAnimationProperty);
                visual.StopAnimation("Scale");
                // XAML positions items through ActualOffset. Composition Offset is
                // an absolute child position, so restore that layout position rather
                // than zeroing every header onto the left edge.
                visual.Offset = tabContainer.ActualOffset;
                visual.Scale = Vector3.One;
                _tabDragVisualStates.Add(new TabDragVisualState
                {
                    Item = tab,
                    Container = tabContainer,
                    Visual = visual,
                    Bounds = bounds
                });
                _tabDragSlotLefts.Add(origin.X);
            }

            var sourceState = _tabDragVisualStates.FirstOrDefault(state => ReferenceEquals(state.Item, item));
            if (sourceState == null)
                return;

            _tabDragTargetIndex = _tabDragOriginalOrder.IndexOf(item);
            _tabDragGhostWidth = sourceState.Bounds.Width;
            _tabDragSourceTop = sourceState.Bounds.Y;
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(new FontIcon
            {
                Glyph = item.IconGlyph,
                FontSize = 14,
                Foreground = container.Foreground,
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 14,
                Foreground = container.Foreground,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            _tabDragGhost = new Border
            {
                Width = _tabDragGhostWidth,
                Height = container.ActualHeight,
                CornerRadius = new CornerRadius(8),
                Background = container.Background,
                BorderBrush = container.BorderBrush,
                BorderThickness = container.BorderThickness,
                Opacity = 0.96,
                Child = panel,
                IsHitTestVisible = false
            };
            TabDragOverlay.Children.Add(_tabDragGhost);
            Canvas.SetLeft(_tabDragGhost, sourceState.Bounds.X);
            Canvas.SetTop(_tabDragGhost, Math.Clamp(_tabDragSourceTop, 0,
                Math.Max(0, TabDragOverlay.ActualHeight - container.ActualHeight)));
            _tabDragGhostVisual = ElementCompositionPreview.GetElementVisual(_tabDragGhost);
            _tabDragGhostVisual.CenterPoint = new Vector3(
                (float)(_tabDragGhostWidth / 2), (float)(container.ActualHeight / 2), 0);
            _tabDragGhostVisual.Scale = new Vector3(1.035f, 1.035f, 1f);
            var initialLeft = Math.Clamp(_tabDragStartPoint.X - _tabDragGrabOffsetX, 0,
                Math.Max(0, TabDragOverlay.ActualWidth - _tabDragGhostWidth));
            _tabDragGhostVisual.Offset = new Vector3(
                (float)initialLeft, (float)_tabDragSourceTop, 0);

            _isTabDragging = true;
            sourceState.Container.Opacity = 0;
            SlidingIndicator.Visibility = Visibility.Collapsed;
        }

        private void ReorderTabAt(double pointerCenterX)
        {
            if (_draggingTabItem == null || _tabDragVisualStates.Count == 0)
                return;

            var targetIndex = 0;
            foreach (var state in _tabDragVisualStates)
            {
                if (ReferenceEquals(state.Item, _draggingTabItem))
                    continue;

                if (pointerCenterX <= state.Bounds.X + state.Bounds.Width / 2)
                    break;

                targetIndex++;
            }

            if (targetIndex == _tabDragTargetIndex)
                return;

            _tabDragTargetIndex = targetIndex;
            AnimateTabNeighborsToTarget(targetIndex);
        }

        private void AnimateTabNeighborsToTarget(int targetIndex)
        {
            if (_draggingTabItem == null)
                return;

            var targetOrder = _tabDragOriginalOrder.ToList();
            targetOrder.Remove(_draggingTabItem);
            targetOrder.Insert(Math.Clamp(targetIndex, 0, targetOrder.Count), _draggingTabItem);

            for (int slotIndex = 0; slotIndex < targetOrder.Count && slotIndex < _tabDragSlotLefts.Count; slotIndex++)
            {
                var item = targetOrder[slotIndex];
                if (ReferenceEquals(item, _draggingTabItem))
                    continue;

                var state = _tabDragVisualStates.FirstOrDefault(entry => ReferenceEquals(entry.Item, item));
                if (state == null)
                    continue;

                var targetOffset = state.Container.ActualOffset + new Vector3(
                    (float)(_tabDragSlotLefts[slotIndex] - state.Bounds.X), 0, 0);
                AnimateTabVisual(state.Visual, TabDragAnimationProperty, targetOffset, 180);
            }
        }

        private static void AnimateTabVisual(Visual visual, string property, Vector3 target, int durationMs)
        {
            var compositor = visual.Compositor;
            var animation = compositor.CreateVector3KeyFrameAnimation();
            var easing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.2f, 0f), new Vector2(0.2f, 1f));
            animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
            animation.InsertKeyFrame(1f, target, easing);
            animation.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation(property, animation);
        }

        private void BeginTabDropAnimation(bool commit)
        {
            _tabDragPointer = null;
            _tabDragPointerId = 0;
            if (!_isTabDragging || _tabDragGhostVisual == null || _draggingTabItem == null)
            {
                ResetTabDragState();
                return;
            }

            _isTabDragging = false;
            _isTabDropAnimating = true;
            _tabDropCommit = commit;

            var sourceIndex = _tabDragOriginalOrder.IndexOf(_draggingTabItem);
            var targetIndex = commit ? _tabDragTargetIndex : sourceIndex;
            targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, _tabDragSlotLefts.Count - 1));
            var sourceState = _tabDragVisualStates.First(state => ReferenceEquals(state.Item, _draggingTabItem));
            var targetLeft = commit ? _tabDragSlotLefts[targetIndex] : sourceState.Bounds.X;
            AnimateTabVisual(_tabDragGhostVisual, TabDragAnimationProperty,
                new Vector3((float)targetLeft, (float)_tabDragSourceTop, 0), 180);
            AnimateTabVisual(_tabDragGhostVisual, "Scale", Vector3.One, 180);

            if (!commit)
            {
                foreach (var state in _tabDragVisualStates)
                {
                    if (!ReferenceEquals(state.Item, _draggingTabItem))
                        AnimateTabVisual(state.Visual, TabDragAnimationProperty,
                            state.Container.ActualOffset, 180);
                }
            }

            _tabDropAnimationTimer?.Stop();
            _tabDropAnimationTimer = DispatcherQueue.CreateTimer();
            _tabDropAnimationTimer.Interval = TimeSpan.FromMilliseconds(190);
            _tabDropAnimationTimer.IsRepeating = false;
            _tabDropAnimationTimer.Tick += TabDropAnimationTimer_Tick;
            _tabDropAnimationTimer.Start();
        }

        private void TabDropAnimationTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            sender.Tick -= TabDropAnimationTimer_Tick;
            _tabDropAnimationTimer = null;
            CompleteTabDropAnimation(_tabDropCommit);
        }

        private void CompleteTabDropAnimation(bool commit)
        {
            var item = _draggingTabItem;
            var selectedItemBeforeMove = MyTabView.SelectedItem;
            var persistOrder = commit && item != null;
            if (commit && item != null)
            {
                var currentIndex = TabItems.IndexOf(item);
                if (currentIndex >= 0 && currentIndex != _tabDragTargetIndex)
                {
                    _isTabDragReordering = true;
                    try
                    {
                        TabItems.Move(currentIndex, _tabDragTargetIndex);
                        MyTabView.UpdateLayout();
                        if (selectedItemBeforeMove != null)
                            MyTabView.SelectedItem = selectedItemBeforeMove;
                    }
                    finally
                    {
                        _isTabDragReordering = false;
                    }
                }
            }

            foreach (var state in _tabDragVisualStates)
            {
                var container = MyTabView.ContainerFromItem(state.Item) as TabViewItem ?? state.Container;
                var visual = ElementCompositionPreview.GetElementVisual(container);
                visual.StopAnimation(TabDragAnimationProperty);
                visual.StopAnimation("Scale");
                visual.Offset = container.ActualOffset;
                visual.Scale = Vector3.One;
                if (ReferenceEquals(state.Item, item))
                {
                    // The source container may have been recycled for another tab by Move.
                    state.Container.Opacity = _tabDragOriginalOpacity;
                    container.Opacity = _tabDragOriginalOpacity;
                }
            }

            if (_tabDragGhost != null)
                TabDragOverlay.Children.Remove(_tabDragGhost);
            _tabDragGhost = null;
            _tabDragGhostVisual = null;

            _isTabDropAnimating = false;
            ResetTabDragState();
            UpdateTabPaths();
            UpdateSlidingIndicator(animate: false);

            if (persistOrder)
            {
                TabRepository.UpdateVisibleSortOrders(
                    _currentMode,
                    TabItems.Where(tab => tab.BuiltinKind == BuiltinTabKind.None)
                        .Select(tab => tab.Id)
                        .ToList());
            }
        }

        private void ResetTabDragState()
        {
            _tabDragPointer = null;
            _tabDragPointerId = 0;
            _draggingTabItem = null;
            _draggingTabContainer = null;
            _tabDragOriginalOrder.Clear();
            _tabDragVisualStates.Clear();
            _tabDragSlotLefts.Clear();
            _tabDragGhostVisual = null;
            _isTabDragging = false;
            _isTabDropAnimating = false;
        }

        // 遍历所有 TabViewItem，根据实际宽度重新生成 Path.Data
        private void UpdateTabPaths()
        {
            if (MyTabView == null || TabItems.Count == 0) return;

            foreach (var item in TabItems)
            {
                var container = MyTabView.ContainerFromItem(item) as TabViewItem;
                if (container == null) continue;

                // 首次遇到这个容器时，挂上所有必要的事件
        if (_hookedContainers.Add(container))
        {
            // 1. 尺寸变化
            container.SizeChanged += (s, e) =>
            {
                if (s is TabViewItem tvi) UpdateSingleTabPath(tvi);
            };

            // 2. 鼠标进入（TabView 内部会重新布局，触发时机比 SizeChanged 更早）
            container.PointerEntered += (s, e) =>
            {
                if (s is TabViewItem tvi)
                {
                    // 延迟一帧，等布局完成
                    DispatcherQueue.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () => UpdateSingleTabPath(tvi));
                }
            };

            // 3. 鼠标离开（同样的原因）
            container.PointerExited += (s, e) =>
            {
                if (s is TabViewItem tvi)
                {
                    DispatcherQueue.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () => UpdateSingleTabPath(tvi));
                }
            };

            container.AddHandler(
                UIElement.PointerPressedEvent,
                new PointerEventHandler(TabHeader_PointerPressed),
                handledEventsToo: true);
            container.AddHandler(
                UIElement.PointerMovedEvent,
                new PointerEventHandler(TabHeader_PointerMoved),
                handledEventsToo: true);
            container.AddHandler(
                UIElement.PointerReleasedEvent,
                new PointerEventHandler(TabHeader_PointerReleased),
                handledEventsToo: true);
            container.AddHandler(
                UIElement.PointerCanceledEvent,
                new PointerEventHandler(TabHeader_PointerCanceled),
                handledEventsToo: true);
            container.AddHandler(
                UIElement.PointerCaptureLostEvent,
                new PointerEventHandler(TabHeader_PointerCaptureLost),
                handledEventsToo: true);

                    _hoverPreview?.HookContainer(container);
                    container.AddHandler(
        UIElement.DoubleTappedEvent,
        new DoubleTappedEventHandler(TabViewItem_DoubleTapped),
        handledEventsToo: true);   // ⭐ 关键：即使被内部处理过也接收
                }

        // 立即更新一次
        UpdateSingleTabPath(container);
        if (ReferenceEquals(MyTabView.SelectedItem, container.DataContext))
            RefreshSelectedTabVisualIfCurrent(container);
            }
        }
        private void TabViewItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (IsEditPanelOpen) return;

            if (sender is not TabViewItem container) return;

            // ⭐ 用 DataContext，不是 Content
            if (container.DataContext is not MyTabItem item) return;
            if (item.BuiltinKind != BuiltinTabKind.None) return;

            e.Handled = true;
            _ = OpenTabEditPanelAsync(item);
        }
        private async Task OpenTabEditPanelAsync(MyTabItem item)
        {
            _hoverPreview?.Close();

            if (_isSettingsVisible)
                await CloseSettingsWithAnimationAsync();

            await EnsureEditPanelClosedAsync();   // ⭐ 先确保关闭
            var result = await ShowTabEditPanelAsync(editing: item);
            if (result == null) return;

            // 用户点了"删除标签"
            if (result.IsDelete)
            {
                await DeleteTabAsync(item);
                return;
            }

            if (!result.IsConfirmed) return;

            // 保存
            BlockBox.Data.TabRepository.Update(item.Id, result.Title, result.Note, result.Icon, result.Kind, result.ExtList);

            // 先让数据库新记录替换集合项（悬停菜单/数据立即变新名），再刷新旧 Header 的绑定。
            LoadTabsFromDb();

            item.Title = result.Title;
            item.Note = result.Note;
            item.IconGlyph = result.Icon;
            item.ContentKind = result.Kind;
            item.ExtensionList = result.ExtList;
            item.RefreshLocalizedTitle();

            var updated = TabItems.FirstOrDefault(t => t.Id == item.Id);
            if (updated != null)
            {
                updated.RefreshLocalizedTitle();
                RefreshTabHeader(updated);
                MyTabView.SelectedItem = updated;
            }
            MyTabView.UpdateLayout();
        }

        private async Task DeleteTabAsync(MyTabItem item)
        {
            var shortcuts = BlockBox.Data.ShortcutRepository.GetByTab(_currentMode, item.Id);
            string warn = shortcuts.Count > 0
                ? "\n\n" + LanguageService.TranslateFormat("该标签下有 {count} 个引用，删除后它们会从标签移除（文件本身不动）。", ("{count}", shortcuts.Count))
                : "";

            int confirm = await ShowConfirmationWindowAsync(
                "删除标签", LanguageService.TranslateFormat("确定删除「{name}」吗？", ("{name}", item.Title)) + warn, "删除");
            if (confirm != 1) return;

            BlockBox.Data.TabRepository.Delete(item.Id);
            TabItems.Remove(item);

            if (MyTabView.SelectedItem == null)
            {
                CurrentShortcuts.Clear();
                _currentTabItem = null;
                _hasActiveTabSelection = false;
            }
        }

        // 只更新某一个 TabViewItem 的 Path
        private void UpdateSingleTabPath(TabViewItem container)
        {
            double width = container.ActualWidth;
            if (width <= 0) return;

            // 读 INI 颜色
            var tabBarColor = GetTabBarBackgroundColor();
            var selectedBrush = new SolidColorBrush(GetMainContentAreaBackgroundColor());
            var hoverBrush = new SolidColorBrush(CalculateHoverColor(tabBarColor));

            var selectedPath = FindChildByName<Microsoft.UI.Xaml.Shapes.Path>(container, "SelectedVisual");
            var hoverPath = FindChildByName<Microsoft.UI.Xaml.Shapes.Path>(container, "HoverVisual");

            string data = GenerateTabPath(width);

            // ⚠️ 更新形状、宽度、颜色（不动 Visibility）
            if (selectedPath != null)
            {
                selectedPath.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
                selectedPath.Width = width + 36;
                selectedPath.Fill = selectedBrush;
                // ⚠️ 不设置 Visibility，交给 ShowSelectedVisual / HideAllSelectedVisuals 控制
            }

            if (hoverPath != null)
            {
                hoverPath.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
                hoverPath.Width = width + 36;
                hoverPath.Fill = hoverBrush;
            }
            var lockPath = FindChildByName<Microsoft.UI.Xaml.Shapes.Path>(container, "HoverLockVisual");
            if (lockPath != null)
            {
                lockPath.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
                lockPath.Width = width + 36;
                lockPath.Fill = hoverBrush;
            }
        }

        internal static Windows.UI.Color CalculateHoverColor(Windows.UI.Color background)
        {
            double luminance =
                0.2126 * background.R / 255.0 +
                0.7152 * background.G / 255.0 +
                0.0722 * background.B / 255.0;

            if (luminance > 0.55)
            {
                const double darkenAmount = 0.18;
                return Windows.UI.Color.FromArgb(
                    255,
                    (byte)(background.R * (1 - darkenAmount)),
                    (byte)(background.G * (1 - darkenAmount)),
                    (byte)(background.B * (1 - darkenAmount)));
            }

            const double lightenAmount = 0.22;
            return Windows.UI.Color.FromArgb(
                255,
                (byte)(background.R + (255 - background.R) * lightenAmount),
                (byte)(background.G + (255 - background.G) * lightenAmount),
                (byte)(background.B + (255 - background.B) * lightenAmount));
        }

        // 根据实际宽度生成标签的形状路径
        internal static string GenerateTabPath(double contentWidth)
        {
            // 参数（和之前 XAML 里的一致）
            double left = 18;              // 主体左边缘在 Path 坐标系中的位置
            double right = 18 + contentWidth;
            double topRadiusH = 8;
            double topRadiusV = 6;
            double bottomRadiusH = 18;
            double bottomRadiusV = 11;
            double height = 38;

            return $"M {left + topRadiusH},0 " +
                   $"L {right - topRadiusH},0 " +
                   $"A {topRadiusH},{topRadiusV} 0 0 1 {right},{topRadiusV} " +
                   $"L {right},{height - bottomRadiusV} " +
                   $"A {bottomRadiusH},{bottomRadiusV} 0 0 0 {right + bottomRadiusH},{height} " +
                   $"L 0,{height} " +
                   $"A {bottomRadiusH},{bottomRadiusV} 0 0 0 {left},{height - bottomRadiusV} " +
                   $"L {left},{topRadiusV} " +
                   $"A {topRadiusH},{topRadiusV} 0 0 1 {left + topRadiusH},0 Z";
        }

        // 递归查找子元素
        private T FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                    return element;

                var result = FindChildByName<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private T FindChildByTag<T>(DependencyObject parent, string tag) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && string.Equals(element.Tag?.ToString(), tag, StringComparison.Ordinal))
                    return element;

                var result = FindChildByTag<T>(child, tag);
                if (result != null) return result;
            }
            return null;
        }
    }
}
