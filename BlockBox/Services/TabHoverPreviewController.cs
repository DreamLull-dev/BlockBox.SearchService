using BlockBox.Helpers;
using BlockBox.Models;
using Windows.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using Windows.Foundation;

namespace BlockBox.Services
{
    /// <summary>
    /// 标签页悬浮预览菜单：
    ///   - 首次悬停 1 秒 → 弹出预览，随后切换标签时立即更新
    ///   - 鼠标在标签栏或菜单内 → 保持
    ///   - 鼠标离开 → 0.3 秒后关闭
    /// </summary>
    public class TabHoverPreviewController
    {
        private const int ShowDelayMs = 1000;
        private const int HideDelayMs = 300;
        private const double ZonePaddingPx = 8;   // ⭐ 矩形外扩，防抖

        private readonly TabView _tabView;
        private readonly DispatcherQueue _dispatcher;
        private readonly Func<MyTabItem, Task> _onEditRequested;
        private readonly Func<MyTabItem, Task> _onDeleteRequested;

        private DispatcherQueueTimer? _hoverShowTimer;
        private DispatcherQueueTimer? _hoverHideTimer;
        private Flyout? _previewFlyout;
        private MyTabItem? _hoverTabItem;
        private Microsoft.UI.Xaml.Shapes.Path? _hoverLockVisual;
        private readonly Func<Windows.UI.Color> _getBackgroundColor;

        // ⭐ 几何区域（根坐标系）
        private Rect _flyoutBounds = Rect.Empty;
        private bool _pointerMovedHooked = false;

        public TabHoverPreviewController(
            TabView tabView,
            DispatcherQueue dispatcher,
            Func<MyTabItem, Task> onEditRequested,
            Func<MyTabItem, Task> onDeleteRequested,
            Func<Windows.UI.Color> getBackgroundColor)
        {
            _tabView = tabView;
            _dispatcher = dispatcher;
            _onEditRequested = onEditRequested;
            _onDeleteRequested = onDeleteRequested;
            _getBackgroundColor = getBackgroundColor;

            _hoverShowTimer = dispatcher.CreateTimer();
            _hoverShowTimer.Interval = TimeSpan.FromMilliseconds(ShowDelayMs);
            _hoverShowTimer.IsRepeating = false;
            _hoverShowTimer.Tick += (s, e) => OnShowTick();

            _hoverHideTimer = dispatcher.CreateTimer();
            _hoverHideTimer.Interval = TimeSpan.FromMilliseconds(HideDelayMs);
            _hoverHideTimer.IsRepeating = false;
            _hoverHideTimer.Tick += (s, e) => Close();
        }

        public void HookContainer(TabViewItem container)
        {
            container.PointerEntered += (s, e) => OnEnter(container);
            container.PointerExited += (s, e) => OnExit(container);
        }

        public void Close()
        {
            if (_hoverLockVisual != null)
            {
                _hoverLockVisual.Visibility = Visibility.Collapsed;
                _hoverLockVisual = null;
            }
            _hoverShowTimer?.Stop();
            _hoverHideTimer?.Stop();

            var f = _previewFlyout;
            _previewFlyout = null;
            _hoverTabItem = null;
            _flyoutBounds = Rect.Empty;

            f?.Hide();
        }

        // ==================== 进入 / 离开 tab ====================

        private void OnEnter(TabViewItem container)
        {
            if (container.DataContext is not MyTabItem item) return;
            if (item.BuiltinKind != BuiltinTabKind.None)
            {
                Close();
                return;
            }

            if (_previewFlyout != null)
            {
                _hoverHideTimer?.Stop();
                if (_hoverTabItem?.Id != item.Id)
                    ShowFlyout(container, item);
                return;
            }

            Close();

            _hoverTabItem = item;

            _hoverShowTimer?.Stop();
            _hoverShowTimer?.Start();
        }

        private void OnExit(TabViewItem container)
        {
            if (_previewFlyout == null && ReferenceEquals(container.DataContext, _hoverTabItem))
                _hoverShowTimer?.Stop();
        }

        private void OnShowTick()
        {
            if (_hoverTabItem == null) return;

            var container = _tabView.ContainerFromItem(_hoverTabItem) as TabViewItem;
            if (container == null) return;

            ShowFlyout(container, _hoverTabItem);
        }

        // ==================== 构建 Flyout ====================

        private void ShowFlyout(TabViewItem container, MyTabItem item)
        {
            Close();
            _hoverTabItem = item;

            var panel = new StackPanel
            {
                Width = 190,
                Spacing = 0,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            };

            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(item.Title) ? LanguageService.Translate("未命名") : item.Title,
                FontSize = FontSizeHelper.TabPreviewTitleFontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 12, 12, 4)
            });

            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(item.Note) ? LanguageService.Translate("（无备注）") : item.Note,
                FontSize = FontSizeHelper.TabPreviewNoteFontSize,
                TextWrapping = TextWrapping.Wrap,
                Opacity = string.IsNullOrEmpty(item.Note) ? 0.4 : 0.7,
                MaxHeight = 100,
                Margin = new Thickness(12, 0, 12, 8)
            });

            //panel.Children.Add(new Border
            //{
            //    Height = 1,
            //    Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"]
            //});
            var bgColor = _getBackgroundColor();
            var editBtn = new Button
            {
                Content = LanguageService.Translate("编辑标签"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinWidth = 0,
                FontSize = FontSizeHelper.DialogControlFontSize,
                Margin = new Thickness(0),
                Padding = new Thickness(12, 8, 12, 8),
                //Background = new SolidColorBrush(bgColor),
                BorderThickness = new Thickness(0)
            };
            var deleteBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE8BB", FontSize = 12 },
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0)
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(deleteBtn, LanguageService.Translate("删除标签"));
            editBtn.SizeChanged += (s, e) =>
            {
                double height = e.NewSize.Height;
                editBtn.CornerRadius = new CornerRadius(Math.Max(0, height / 2 - 1));
                deleteBtn.Width = height;
                deleteBtn.Height = height;
                deleteBtn.CornerRadius = new CornerRadius(height / 2);
            };
            // ⭐ 用透明度区分状态
            var c = bgColor;
            var normalBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb((byte)(c.A * 0.2), c.R, c.G, c.B));
            var hoverBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb((byte)(c.A * 0.5), c.R, c.G, c.B));   // 更透明
            var pressedBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb((byte)(c.A * 0.7), c.R, c.G, c.B));

            editBtn.Resources["ButtonBackground"] = normalBrush;
            editBtn.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
            editBtn.Resources["ButtonBackgroundPressed"] = pressedBrush;
            editBtn.Resources["ButtonBackgroundDisabled"] = normalBrush;
            deleteBtn.Resources["ButtonBackground"] = normalBrush;
            deleteBtn.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
            deleteBtn.Resources["ButtonBackgroundPressed"] = pressedBrush;
            deleteBtn.Resources["ButtonBackgroundDisabled"] = normalBrush;
            editBtn.Click += async (s, e) =>
            {
                Close();
                await _onEditRequested(item);
            };
            deleteBtn.Click += async (s, e) =>
            {
                Close();
                await _onDeleteRequested(item);
            };
            var buttonRow = new Grid { Margin = new Thickness(8, 0, 8, 8), ColumnSpacing = 6 };
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(deleteBtn, 1);
            buttonRow.Children.Add(editBtn);
            buttonRow.Children.Add(deleteBtn);
            panel.Children.Add(buttonRow);

            var wrapper = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Child = panel
            };

            var flyout = new Flyout { Content = wrapper };
            flyout.ShouldConstrainToRootBounds = false;
            flyout.OverlayInputPassThroughElement = _tabView;
            flyout.FlyoutPresenterStyle = BuildPresenterStyle();
            wrapper.PointerEntered += (s, e) =>
            {
                if (_previewFlyout == flyout)
                    _hoverHideTimer?.Stop();
            };
            wrapper.PointerExited += (s, e) =>
            {
                if (_previewFlyout != flyout) return;

                var root = _tabView.XamlRoot?.Content as UIElement;
                if (root != null && GetBoundsInRoot(_tabView).Contains(e.GetCurrentPoint(root).Position))
                    _hoverHideTimer?.Stop();
                else
                    StartHideTimer();
            };

            flyout.Opened += (s, e) =>
            {
                // ⭐ 延迟一帧后计算 flyout 矩形（此时布局已完成）
                _dispatcher.TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () =>
                    {
                        if (_previewFlyout == flyout)
                            UpdateFlyoutBounds();
                    });
            };

            flyout.Closed += (s, e) =>
            {
                if (_previewFlyout != flyout) return;

                _previewFlyout = null;
                _hoverTabItem = null;
                _flyoutBounds = Rect.Empty;
                _hoverHideTimer?.Stop();

                if (_hoverLockVisual != null)
                {
                    _hoverLockVisual.Visibility = Visibility.Collapsed;
                    _hoverLockVisual = null;
                }
            };
            // ⭐ 锁住 hover 背景
            _hoverLockVisual = FindChildByName<Microsoft.UI.Xaml.Shapes.Path>(
                container, "HoverLockVisual");
            if (_hoverLockVisual != null)
                _hoverLockVisual.Visibility = Visibility.Visible;
            _previewFlyout = flyout;
            HookPointerMoved();

            flyout.ShowAt(container, new FlyoutShowOptions
            {
                Position = new Point(0, container.ActualHeight),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                ShowMode = FlyoutShowMode.Standard
            });
        }
        private static T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T e && e.Name == name) return e;
                var r = FindChildByName<T>(child, name);
                if (r != null) return r;
            }
            return null;
        }
        // ==================== 几何计算 ====================

        private static Rect GetBoundsInRoot(FrameworkElement el)
        {
            try
            {
                var root = el.XamlRoot?.Content as UIElement;
                if (root == null) return Rect.Empty;

                var t = el.TransformToVisual(root);
                return t.TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
            }
            catch
            {
                return Rect.Empty;
            }
        }

        private void UpdateFlyoutBounds()
        {
            var root = _tabView.XamlRoot;
            if (root == null) return;

            try
            {
                var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(root);
                if (popups.Count == 0) return;

                // 最新的 popup
                var popup = popups[popups.Count - 1];
                if (popup.Child is FrameworkElement fe)
                {
                    var rootContent = root.Content as UIElement;
                    if (rootContent == null) return;

                    var t = fe.TransformToVisual(rootContent);
                    _flyoutBounds = t.TransformBounds(
                        new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                }
            }
            catch
            {
                _flyoutBounds = Rect.Empty;
            }
        }

        // ==================== 全局 PointerMoved ====================

        private void HookPointerMoved()
        {
            if (_pointerMovedHooked) return;

            var root = _tabView.XamlRoot?.Content as UIElement;
            if (root == null) return;

            root.AddHandler(
                UIElement.PointerMovedEvent,
                new PointerEventHandler(OnGlobalPointerMoved),
                handledEventsToo: true);
            root.AddHandler(
                UIElement.PointerExitedEvent,
                new PointerEventHandler(OnRootPointerExited),
                handledEventsToo: true);

            _pointerMovedHooked = true;
        }

        private void OnGlobalPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_previewFlyout == null) return;

            var root = _tabView.XamlRoot?.Content as UIElement;
            if (root == null) return;

            Point pt;
            try
            {
                pt = e.GetCurrentPoint(root).Position;
            }
            catch { return; }

            // ⭐ 外扩后的判断区域
            var tabZone = Inflate(GetBoundsInRoot(_tabView), ZonePaddingPx);
            var flyoutZone = Inflate(_flyoutBounds, ZonePaddingPx);

            bool inside = tabZone.Contains(pt) || flyoutZone.Contains(pt);

            if (inside)
            {
                _hoverHideTimer?.Stop();
            }
            else
            {
                StartHideTimer();
            }
        }

        private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_previewFlyout == null || sender is not FrameworkElement root) return;

            var point = e.GetCurrentPoint(root).Position;
            if (point.X <= 0 || point.Y <= 0 || point.X >= root.ActualWidth || point.Y >= root.ActualHeight)
                StartHideTimer();
        }

        private void StartHideTimer()
        {
            if (_hoverHideTimer != null && !_hoverHideTimer.IsRunning)
                _hoverHideTimer.Start();
        }

        private static Rect Inflate(Rect r, double px)
        {
            if (r.IsEmpty) return r;
            return new Rect(r.X - px, r.Y - px, r.Width + px * 2, r.Height + px * 2);
        }

        private static Style BuildPresenterStyle()
        {
            var style = new Style(typeof(FlyoutPresenter));
            style.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(0.4)));
            style.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(12)));
            style.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty, 0.0));
            style.Setters.Add(new Setter(FlyoutPresenter.MinHeightProperty, 0.0));
            style.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty, 400.0));
            return style;
        }
    }
}