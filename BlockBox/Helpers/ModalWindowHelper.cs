using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.UI;
using Windows.Graphics;

namespace BlockBox.Helpers
{
    public static class ModalWindowHelper
    {
        private const int GwlHwndParent = -8;

        public static void ConfigureCloseOnlyTitleBar(Window window)
        {
            window.Title = string.Empty;
            window.ExtendsContentIntoTitleBar = true;

            var titleBar = window.AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(32, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(48, 255, 255, 255);

            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
        }

        public static void ConfigureBorderlessWindow(Window window)
        {
            window.Title = string.Empty;
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
        }

        public static Task ShowModalAsync(Window owner, Window window)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ShowModal(owner, window, () => completion.TrySetResult());
            return completion.Task;
        }

        public static void CenterOnOwner(Window owner, Window window)
        {
            var ownerPosition = owner.AppWindow.Position;
            var ownerSize = owner.AppWindow.Size;
            var windowSize = window.AppWindow.Size;
            int x = ownerPosition.X + (ownerSize.Width - windowSize.Width) / 2;
            int y = ownerPosition.Y + (ownerSize.Height - windowSize.Height) / 2;

            // 宿主贴着屏幕边缘时（例如搜索面板在主窗体右侧），居中会把对话框挤出屏幕，这里钳回工作区。
            var display = DisplayArea.GetFromPoint(
                    new PointInt32(ownerPosition.X + ownerSize.Width / 2, ownerPosition.Y + ownerSize.Height / 2),
                    DisplayAreaFallback.Primary)
                ?? DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary);
            if (display is not null)
            {
                var work = display.WorkArea;
                x = Math.Clamp(x, work.X, Math.Max(work.X, work.X + work.Width - windowSize.Width));
                y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Y + work.Height - windowSize.Height));
            }

            window.AppWindow.Move(new PointInt32(x, y));
        }

        public static void ShowModal(Window owner, Window window, Action? closed = null)
        {
            IntPtr ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (ownerHandle == IntPtr.Zero || windowHandle == IntPtr.Zero)
            {
                window.Activate();
                return;
            }

            SetWindowLongPtr(windowHandle, GwlHwndParent, ownerHandle);
            bool ownerWasEnabled = IsWindowEnabled(ownerHandle);
            if (ownerWasEnabled)
                EnableWindow(ownerHandle, false);

            window.Closed += (_, _) =>
            {
                if (ownerWasEnabled)
                {
                    EnableWindow(ownerHandle, true);
                    owner.Activate();
                }

                closed?.Invoke();
            };

            window.Activate();
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowEnabled(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnableWindow(IntPtr windowHandle, [MarshalAs(UnmanagedType.Bool)] bool enable);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr windowHandle, int index, int value);

        private static IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value)
            => IntPtr.Size == 8
                ? SetWindowLongPtr64(windowHandle, index, value)
                : new IntPtr(SetWindowLong32(windowHandle, index, value.ToInt32()));
    }
}
