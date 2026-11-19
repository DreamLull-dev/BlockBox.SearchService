using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Windows.Foundation.Numerics;
using System.Numerics;
using Microsoft.UI.Windowing;
using Windows.Foundation;

namespace BlockBox.Helpers
{
    /// <summary>Fades the complete top-level HWND, including its XAML content and navigation.</summary>
    public static class WindowFadeController
    {
        private const int GwlExStyle = -20;
        private const long WsExLayered = 0x00080000L;
        private const uint LwaAlpha = 0x00000002;
        private static readonly ConditionalWeakTable<Window, CloseAnimationState> CloseAnimations = new();

        private sealed class CloseAnimationState
        {
            public required Visual Visual { get; init; }
            public required float InitialScale { get; init; }
            public required Vector3 InitialOffset { get; init; }
            public required TypedEventHandler<AppWindow, AppWindowClosingEventArgs> ClosingHandler { get; set; }
            public bool IsClosing { get; set; }
            public bool AllowClose { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        public static void FadeIn(Window window, TimeSpan? duration = null)
            => Start(window, 0, 255, duration ?? TimeSpan.FromMilliseconds(360), null);

        /// <summary>
        /// 只让窗口内容做透明度+缩放入场，**不动 HWND 的分层属性**。
        /// <para>
        /// 对话框（亚克力背景 + 普通控件）如果对整个 HWND 逐帧 SetLayeredWindowAttributes，
        /// 每一帧都会强制整窗重新合成，指针悬停/焦点状态会跟着反复重算，表现为按钮一闪一闪。
        /// 内容级动画没有这个问题，视觉上也几乎看不出差别。
        /// </para>
        /// </summary>
        public static void FadeContentIn(FrameworkElement root, TimeSpan? duration = null)
        {
            var animationDuration = duration ?? TimeSpan.FromMilliseconds(200);
            try
            {
                root.Opacity = 0;
                // 用 RenderTransformOrigin 居中缩放：Loaded 时 ActualWidth 可能还是 0，
                // 用 CenterX/CenterY 会把缩放中心算到左上角，看上去是从角上弹出。
                root.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                var transform = new CompositeTransform
                {
                    ScaleX = 0.97,
                    ScaleY = 0.97
                };
                root.RenderTransform = transform;

                var storyboard = new Storyboard();
                var fade = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = new Duration(animationDuration),
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(fade, root);
                Storyboard.SetTargetProperty(fade, "Opacity");
                storyboard.Children.Add(fade);

                foreach (string property in new[] { "ScaleX", "ScaleY" })
                {
                    var scale = new DoubleAnimation
                    {
                        From = 0.97,
                        To = 1,
                        Duration = new Duration(animationDuration),
                        EnableDependentAnimation = true
                    };
                    Storyboard.SetTarget(scale, transform);
                    Storyboard.SetTargetProperty(scale, property);
                    storyboard.Children.Add(scale);
                }

                storyboard.Begin();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[窗口内容渐入] 动画失败: {ex}");
                root.Opacity = 1;
            }
        }

        /// <summary>
        /// 内容渐出后关闭窗口。和 <see cref="FadeContentIn"/> 对称，不触碰 HWND 分层属性。
        /// </summary>
        public static Task FadeContentOutAsync(Window window, FrameworkElement root, TimeSpan? duration = null)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var storyboard = new Storyboard();
                var fade = new DoubleAnimation
                {
                    From = root.Opacity,
                    To = 0,
                    Duration = new Duration(duration ?? TimeSpan.FromMilliseconds(180)),
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(fade, root);
                Storyboard.SetTargetProperty(fade, "Opacity");
                storyboard.Children.Add(fade);
                storyboard.Completed += (_, _) =>
                {
                    completion.TrySetResult();
                    window.Close();
                };
                storyboard.Begin();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[窗口内容渐出] 动画失败: {ex}");
                completion.TrySetResult();
                window.Close();
            }
            return completion.Task;
        }

        public static void FadeAndScaleIn(Window window, FrameworkElement root, TimeSpan? duration = null)
        {
            var animationDuration = duration ?? TimeSpan.FromMilliseconds(360);
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(root);
                var compositor = visual.Compositor;
                double scaleFactor = root.XamlRoot?.RasterizationScale ?? 1.0;
                var center = new Vector3((float)(root.ActualWidth / 2), (float)(root.ActualHeight / 2), 0);
                visual.CenterPoint = center;

                float initialScale = 0.78f;
                var initialOffset = Vector3.Zero;
                if (GetCursorPos(out var cursor))
                {
                    var windowPosition = window.AppWindow.Position;
                    float cursorX = (float)((cursor.X - windowPosition.X) / scaleFactor);
                    float cursorY = (float)((cursor.Y - windowPosition.Y) / scaleFactor);
                    initialOffset = new Vector3(
                        (1 - initialScale) * (cursorX - center.X),
                        (1 - initialScale) * (cursorY - center.Y),
                        0);
                }

                RegisterCloseAnimation(window, visual, initialScale, initialOffset);

                visual.Scale = new Vector3(initialScale);
                visual.Offset = initialOffset;

                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
                var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
                scaleAnimation.InsertKeyFrame(1f, Vector3.One, easing);
                scaleAnimation.Duration = animationDuration;
                visual.StartAnimation("Scale", scaleAnimation);

                var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.InsertKeyFrame(1f, Vector3.Zero, easing);
                offsetAnimation.Duration = animationDuration;
                visual.StartAnimation("Offset", offsetAnimation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[窗口缩放渐入] 内容动画初始化失败: {ex}");
            }

            FadeIn(window, animationDuration);
        }

        private static void RegisterCloseAnimation(Window window, Visual visual,
            float initialScale, Vector3 initialOffset)
        {
            if (CloseAnimations.TryGetValue(window, out _))
                return;

            CloseAnimationState? state = null;
            TypedEventHandler<AppWindow, AppWindowClosingEventArgs> closingHandler = (_, args) =>
            {
                if (state == null || state.AllowClose)
                    return;

                args.Cancel = true;
                if (state.IsClosing)
                    return;

                state.IsClosing = true;
                AnimateClose(window, state);
            };

            state = new CloseAnimationState
            {
                Visual = visual,
                InitialScale = initialScale,
                InitialOffset = initialOffset,
                ClosingHandler = closingHandler
            };
            CloseAnimations.Add(window, state);
            window.AppWindow.Closing += closingHandler;
        }

        public static void CloseWithAnimation(Window window)
        {
            if (CloseAnimations.TryGetValue(window, out var state))
            {
                if (!state.IsClosing)
                {
                    state.IsClosing = true;
                    AnimateClose(window, state);
                }
                return;
            }

            window.Close();
        }

        private static void AnimateClose(Window window, CloseAnimationState state)
        {
            var duration = TimeSpan.FromMilliseconds(360);
            try
            {
                state.Visual.StopAnimation("Scale");
                state.Visual.StopAnimation("Offset");

                var compositor = state.Visual.Compositor;
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.7f, 0f), new Vector2(0.84f, 0f));
                var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
                scaleAnimation.InsertKeyFrame(1f, new Vector3(state.InitialScale), easing);
                scaleAnimation.Duration = duration;
                state.Visual.StartAnimation("Scale", scaleAnimation);

                var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.InsertKeyFrame(1f, state.InitialOffset, easing);
                offsetAnimation.Duration = duration;
                state.Visual.StartAnimation("Offset", offsetAnimation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[窗口缩放渐出] 内容动画初始化失败: {ex}");
            }

            Start(window, 255, 0, duration, () =>
            {
                state.AllowClose = true;
                window.AppWindow.Closing -= state.ClosingHandler;
                CloseAnimations.Remove(window);
                window.Close();
            }, easeIn: true);
        }

        /// <summary>Sets whole-window transparency before the HWND is shown, preventing an opaque flash.</summary>
        public static void Prepare(Window window)
        {
            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                EnsureLayered(hwnd);
                SetAlpha(hwnd, 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[窗口淡入淡出] 预置 HWND 透明度失败: {ex}");
            }
        }

        public static Task FadeOutAsync(Window window, TimeSpan? duration = null)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Start(window, 255, 0, duration ?? TimeSpan.FromMilliseconds(280),
                () => completion.TrySetResult(), easeIn: true);
            return completion.Task;
        }

        private static void Start(Window window, byte from, byte to, TimeSpan duration, Action? completed,
            bool easeIn = false)
        {
            IntPtr hwnd;
            try
            {
                hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                EnsureLayered(hwnd);
                SetAlpha(hwnd, from);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[窗口淡入淡出] 初始化 HWND 透明度失败: {ex}");
                Settle(window, to, completed);
                return;
            }

            var timer = window.DispatcherQueue.CreateTimer();
            var clock = Stopwatch.StartNew();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (_, _) =>
            {
                double progress = Math.Clamp(clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
                // Fluent-style ease-out for both entering and leaving transitions.
                double eased = easeIn
                    ? progress * progress * progress
                    : 1 - Math.Pow(1 - progress, 3);
                byte alpha = (byte)Math.Clamp(Math.Round(from + (to - from) * eased), 0, 255);
                try
                {
                    SetAlpha(hwnd, alpha);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[窗口淡入淡出] 更新 HWND 透明度失败: {ex}");
                    timer.Stop();
                    Settle(window, to, completed);
                    return;
                }

                if (progress < 1)
                    return;

                timer.Stop();
                completed?.Invoke();
            };
            timer.Start();
        }

        private static void EnsureLayered(IntPtr hwnd)
        {
            long style = IntPtr.Size == 8
                ? GetWindowLongPtr64(hwnd, GwlExStyle).ToInt64()
                : GetWindowLong32(hwnd, GwlExStyle);
            long layeredStyle = style | WsExLayered;
            if (layeredStyle == style)
                return;

            if (IntPtr.Size == 8)
                SetWindowLongPtr64(hwnd, GwlExStyle, new IntPtr(layeredStyle));
            else
                SetWindowLong32(hwnd, GwlExStyle, unchecked((int)layeredStyle));
        }

        private static void SetAlpha(IntPtr hwnd, byte alpha)
        {
            if (!SetLayeredWindowAttributes(hwnd, 0, alpha, LwaAlpha))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        private static void Settle(Window window, byte alpha, Action? completed)
        {
            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                EnsureLayered(hwnd);
                SetAlpha(hwnd, alpha);
            }
            catch { }
            completed?.Invoke();
        }
    }
}
