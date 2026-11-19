using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        private sealed class BackdropLifetime
        {
            private readonly Window _window;
            private DesktopAcrylicController? _controller;
            private readonly SystemBackdropConfiguration _configuration;
            private bool _isClosed;

            public BackdropLifetime(Window window)
            {
                _window = window;
                _configuration = new SystemBackdropConfiguration { IsInputActive = true };
                window.Activated += OnActivated;
                window.Closed += OnClosed;
                if (window.Content is FrameworkElement content)
                    content.Loaded += OnContentLoaded;
                Application.Current.Resources.TryGetValue("AppPrimaryBackgroundBrush", out object value);
                if (value is SolidColorBrush brush)
                    Update(brush.Color);
            }

            public void Update(Color tint)
            {
                if (_isClosed) return;
                var foregroundBrush = new SolidColorBrush(GetContrastingForeground(tint));
                try
                {
                    if (_window.Content is DependencyObject root)
                        ApplyForeground(root, foregroundBrush);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // A window may close between the closed-state check and visual-tree access.
                    Close();
                    return;
                }

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

            private void OnContentLoaded(object sender, RoutedEventArgs args)
            {
                if (_isClosed || !Application.Current.Resources.TryGetValue("AppPrimaryBackgroundBrush", out object value)
                    || value is not SolidColorBrush brush) return;
                UpdateForegroundOnly(brush.Color);
            }

            public void UpdateForegroundOnly(Color tint)
            {
                if (_isClosed) return;
                try
                {
                    if (_window.Content is DependencyObject root)
                        ApplyForeground(root, new SolidColorBrush(GetContrastingForeground(tint)));
                }
                catch (System.Runtime.InteropServices.COMException) { Close(); }
            }

            private static void ApplyForeground(DependencyObject element, SolidColorBrush foregroundBrush)
            {
                // Only recolor text display elements; preserve button and input control styling.
                if (element is TextBlock textBlock)
                    textBlock.Foreground = foregroundBrush;
                else if (element is RichTextBlock richTextBlock)
                    richTextBlock.Foreground = foregroundBrush;

                int childCount = VisualTreeHelper.GetChildrenCount(element);
                for (int i = 0; i < childCount; i++)
                    ApplyForeground(VisualTreeHelper.GetChild(element, i), foregroundBrush);
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

        private static Color GetContrastingForeground(Color color)
        {
            // WCAG relative luminance keeps the black/white switch consistent across hues.
            static double Linearize(byte channel)
            {
                double value = channel / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            double luminance = 0.2126 * Linearize(color.R)
                             + 0.7152 * Linearize(color.G)
                             + 0.0722 * Linearize(color.B);
            return luminance < 0.179
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(255, 0, 0, 0);
        }
    }
}
