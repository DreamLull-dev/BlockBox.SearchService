using BlockBox.Helpers;
using BlockBox.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Threading.Tasks;

namespace BlockBox
{
    public sealed partial class MainWindow : Window
    {
        /// <summary>搜索按钮完全展开时占用的宽度（30 的按钮 + 10 的间距）。</summary>
        private const double SearchLauncherExtent = 40;

        private bool _searchLauncherShown;
        private int _searchLauncherAnimation;

        private void SearchLauncher_Click(object sender, RoutedEventArgs e) => _ = OpenGlobalSearchFromLauncherAsync();

        /// <summary>宽度动画期间把宿主裁到当前宽度，按钮才不会在展开途中压到 ModeSwitcher 上。</summary>
        private void SearchLauncherHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SearchLauncherHost.Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
            };
        }

        private async Task OpenGlobalSearchFromLauncherAsync()
        {
            // 先让按钮渐出、ModeSwitcher 右移回位，再把搜索窗体渐入，避免两个动画叠在一起。
            await HideSearchLauncherAsync();
            ShowGlobalSearchWindow();
        }

        internal void ShowSearchLauncher()
        {
            if (_searchLauncherShown || IsShellClosed) return;
            _searchLauncherShown = true;
            // 展开前先把宿主裁成 0 宽，避免第一帧（还没测量）时按钮画到 ModeSwitcher 上。
            SearchLauncherHost.Width = 0;
            SearchLauncherHost.Opacity = 0;
            SearchLauncherHost.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 0, 0) };
            SearchLauncherHost.Visibility = Visibility.Visible;
            _ = AnimateSearchLauncherAsync(show: true);
        }

        /// <summary>
        /// 不走动画直接显示入口按钮：启动时若设置成“不自动打开全局搜索”，主窗体还没完成
        /// 首次布局，依赖布局的宽度动画不一定会推进，这里直接把最终状态摆好。
        /// </summary>
        internal void ShowSearchLauncherImmediately()
        {
            if (_searchLauncherShown) return;
            _searchLauncherShown = true;
            SearchLauncherHost.Width = SearchLauncherExtent;
            SearchLauncherHost.Opacity = 1;
            SearchLauncherHost.Visibility = Visibility.Visible;
            SearchLauncherHost.Clip = null;
        }

        private Task HideSearchLauncherAsync()
        {
            if (!_searchLauncherShown)
            {
                SearchLauncherHost.Visibility = Visibility.Collapsed;
                return Task.CompletedTask;
            }
            _searchLauncherShown = false;
            return AnimateSearchLauncherAsync(show: false);
        }

        internal void HideSearchLauncher()
        {
            if (!_searchLauncherShown) return;
            _searchLauncherShown = false;
            _ = AnimateSearchLauncherAsync(show: false);
        }

        /// <summary>
        /// 动画的是宿主的宽度而不是按钮的透明度：宽度从 0 到 40 会让右侧 Auto 列变宽，
        /// ModeSwitcher 随之左移、TabView 所在的比例列同步收窄，两个控件都不用手工量宽度。
        /// </summary>
        private Task AnimateSearchLauncherAsync(bool show)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int token = ++_searchLauncherAnimation;
            var duration = TimeSpan.FromMilliseconds(show ? 220 : 160);
            var easing = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
            // 用实际宽度作起点：动画进行到一半被打断时也不会跳变。
            double fromWidth = SearchLauncherHost.ActualWidth;

            var width = new DoubleAnimation
            {
                From = fromWidth,
                To = show ? SearchLauncherExtent : 0,
                Duration = duration,
                EnableDependentAnimation = true,
                EasingFunction = easing
            };
            Storyboard.SetTarget(width, SearchLauncherHost);
            Storyboard.SetTargetProperty(width, "Width");

            var opacity = new DoubleAnimation
            {
                From = SearchLauncherHost.Opacity,
                To = show ? 1 : 0,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(opacity, SearchLauncherHost);
            Storyboard.SetTargetProperty(opacity, "Opacity");

            var storyboard = new Storyboard();
            storyboard.Children.Add(width);
            storyboard.Children.Add(opacity);
            storyboard.Completed += (_, _) =>
            {
                // 被后一次动画接管时只释放等待者，不再改动可视状态。
                if (token == _searchLauncherAnimation)
                {
                    // 把动画结束值写回基础值，下一次动画的起点才准确。
                    SearchLauncherHost.Width = show ? SearchLauncherExtent : 0;
                    SearchLauncherHost.Opacity = show ? 1 : 0;
                    SearchLauncherHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                }
                completion.TrySetResult();
            };
            storyboard.Begin();
            return completion.Task;
        }

        private void GlobalSearchWindow_Closed(object sender, WindowEventArgs args)
        {
            if (!ReferenceEquals(sender, _globalSearchWindow)) return;
            _globalSearchWindow = null;
            ShowSearchLauncher();
            // 主窗体之前可能因为搜索面板还开着而暂缓关闭（见 MainWindow_AppWindowClosing）。
            // 现在两个窗体都关了，才真正关闭主窗体：托盘图标随之移除，进程退出。
            CompleteDeferredShellClose();
        }

        /// <summary>
        /// 完成因全局搜索窗口仍在运行而被暂缓的主窗体关闭。“退出后进入托盘”只在两个窗体都
        /// 关闭后才失效——只要还有一个窗体在，托盘菜单就必须留着。
        /// </summary>
        private void CompleteDeferredShellClose()
        {
            if (!_deferredShellClose) return;
            _deferredShellClose = false;

            // 等待期间用户可能又把“退出后进入托盘”打开了，那就继续留在托盘里。
            if (_ini.ReadBool("System", "TrayOnExit", SettingsDefaults.TrayOnExit))
                return;

            // 从搜索窗体的 Closed 里直接关主窗体容易和窗口销毁流程重入，排到下一轮消息再关。
            DispatcherQueue.TryEnqueue(() =>
            {
                _allowWindowClose = true;
                Close();
            });
        }
    }
}
