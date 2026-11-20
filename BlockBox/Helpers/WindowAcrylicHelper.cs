using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Windows.UI;
using WinRT;

namespace BlockBox.Helpers
{
    internal static class WindowAcrylicHelper
    {
        // 用户选择的字体色；adjustTextContrast=true 时按窗口内实际背景提亮/压暗。
        private static Color _textColor = Color.FromArgb(255, 255, 255, 255);
        private static bool _adjustTextContrast = true;

        private sealed class BackdropLifetime
        {
            private readonly Window _window;
            private DesktopAcrylicController? _controller;
            private readonly SystemBackdropConfiguration _configuration;
            private Color _tint = Color.FromArgb(255, 36, 74, 128);
            private bool _isClosed;

            public BackdropLifetime(Window window)
            {
                _window = window;
                _configuration = new SystemBackdropConfiguration { IsInputActive = true };
                window.Activated += OnActivated;
                window.Closed += OnClosed;
                if (window.Content is FrameworkElement content)
                    content.Loaded += OnContentLoaded;
            }

            public void Update(Color tint)
            {
                if (_isClosed) return;
                _tint = tint;

                ApplyTextColors(tint);

                if (_controller is not null)
                {
                    _controller.TintColor = tint;
                    _controller.FallbackColor = tint;
                    return;
                }

                if (!DesktopAcrylicController.IsSupported()) return;
                var controller = new DesktopAcrylicController
                {
                    Kind = DesktopAcrylicKind.Thin,
                    TintColor = tint,
                    FallbackColor = tint,
                    TintOpacity = 0.18f,
                    LuminosityOpacity = 0.35f
                };
                if (!controller.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>()))
                {
                    controller.Dispose();
                    return;
                }
                controller.SetSystemBackdropConfiguration(_configuration);
                _controller = controller;
            }

            /// <summary>
            /// 只刷新文字颜色，不重设亚克力色调（主色未变、只改字体色时使用）。
            /// </summary>
            public void UpdateTextOnly()
            {
                if (_isClosed) return;
                ApplyTextColors(_tint);
            }

            private void ApplyTextColors(Color tint)
            {
                try
                {
                    if (_window.Content is DependencyObject root)
                    {
                        // 自动模式：每个控件按自己的实际背景校正明度；
                        // 关闭自动：严格使用用户颜色。
                        TextColorHelper.ApplyForegroundToTree(
                            root,
                            _textColor,
                            _adjustTextContrast,
                            tint);
                    }
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // A window may close between the closed-state check and visual-tree access.
                    Close();
                }
            }

            private void OnContentLoaded(object sender, RoutedEventArgs args)
            {
                if (_isClosed) return;
                // 动态内容（标题、按钮文案）是在 Apply 之后才赋值/创建的，
                // 内容真正加载后再补刷一次。
                UpdateTextOnly();
            }

            private void OnActivated(object sender, WindowActivatedEventArgs args)
                => _configuration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;

            private void OnClosed(object sender, WindowEventArgs args) => Close();

            private void Close()
            {
                if (_isClosed) return;
                _isClosed = true;
                _window.Activated -= OnActivated;
                _window.Closed -= OnClosed;
                if (_window.Content is FrameworkElement content)
                    content.Loaded -= OnContentLoaded;
                _controller?.Dispose();
                _controller = null;
                lock (SyncRoot)
                    ActiveLifetimes.RemoveAll(reference =>
                        !reference.TryGetTarget(out var target) || ReferenceEquals(target, this));
            }
        }

        private static readonly ConditionalWeakTable<Window, BackdropLifetime> Lifetimes = new();
        private static readonly object SyncRoot = new();
        private static readonly List<WeakReference<BackdropLifetime>> ActiveLifetimes = new();

        public static void Apply(Window window)
        {
            Color tint = Color.FromArgb(255, 36, 74, 128);
            if (Application.Current.Resources.TryGetValue("AppPrimaryBackgroundBrush", out object value)
                && value is SolidColorBrush brush)
                tint = brush.Color;
            var lifetime = Lifetimes.GetValue(window, key =>
            {
                var created = new BackdropLifetime(key);
                lock (SyncRoot) ActiveLifetimes.Add(new WeakReference<BackdropLifetime>(created));
                return created;
            });
            lifetime.Update(tint);
        }

        /// <summary>
        /// 更新所有已登记窗口的亚克力色调和文字颜色。
        /// 调用方应先把字体色通过 <see cref="SetTextColor"/> 写入，避免二次遍历。
        /// </summary>
        public static void UpdateAll(Color tint)
        {
            lock (SyncRoot)
            {
                for (int i = ActiveLifetimes.Count - 1; i >= 0; i--)
                {
                    if (ActiveLifetimes[i].TryGetTarget(out var lifetime)) lifetime.Update(tint);
                    else ActiveLifetimes.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 记录当前字体色与自动对比度开关。
        /// applyToWindows=false 时只记录，由调用方随后统一刷新。
        /// </summary>
        public static void SetTextColor(Color color, bool adjustForContrast, bool applyToWindows = true)
        {
            bool changed = _textColor != color || _adjustTextContrast != adjustForContrast;
            _textColor = color;
            _adjustTextContrast = adjustForContrast;

            if (applyToWindows && changed)
                UpdateAllTextColors();
        }

        /// <summary>
        /// 给运行中新建的项容器套用当前字体色。列表/搜索结果的 DataTemplate 是
        /// 窗口加载后才创建的，窗口加载时的那次遍历覆盖不到。
        /// </summary>
        public static void ApplyCurrentTextColors(DependencyObject root)
        {
            Color fallback = Color.FromArgb(255, 36, 74, 128);
            if (Application.Current.Resources.TryGetValue("AppPrimaryBackgroundBrush", out object value)
                && value is SolidColorBrush brush)
            {
                fallback = brush.Color;
            }

            TextColorHelper.ApplyForegroundToTree(
                root,
                _textColor,
                _adjustTextContrast,
                fallback);
        }

        /// <summary>
        /// 字体色变化时只刷新文字，不动窗口背景。
        /// </summary>
        public static void UpdateAllTextColors()
        {
            lock (SyncRoot)
            {
                for (int i = ActiveLifetimes.Count - 1; i >= 0; i--)
                {
                    if (ActiveLifetimes[i].TryGetTarget(out var lifetime)) lifetime.UpdateTextOnly();
                    else ActiveLifetimes.RemoveAt(i);
                }
            }
        }
    }
}
