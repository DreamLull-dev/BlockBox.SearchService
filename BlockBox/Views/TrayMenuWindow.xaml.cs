using BlockBox.Helpers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;
using WinRT.Interop;

namespace BlockBox.Views
{
    public enum TrayMenuAction
    {
        Show,
        GlobalSearch,
        Settings,
        Hide,
        Exit
    }

    /// <summary>
    /// 托盘右键菜单：无边框 + 亚克力 + 图标文本，风格与程序内菜单一致。
    /// </summary>
    public sealed partial class TrayMenuWindow : Window
    {
        private const int WidthDip = 236;
        private const int ProvisionalHeightDip = 260;

        private readonly TaskCompletionSource<TrayMenuAction?> _result = new();
        private TrayMenuAction? _pending;
        private bool _closing;
        private bool _activatedOnce;
        private int _screenX;
        private int _screenY;
        private double _scale = 1.0;

        public TrayMenuWindow(int screenX, int screenY)
        {
            InitializeComponent();

            // 保留亚克力背景，但不让 WindowAcrylicHelper 把全局白色字体刷进来：
            // 菜单固定使用深色文字，清晰可读。
            WindowAcrylicHelper.ApplyBackdropOnly(this);
            ModalWindowHelper.ConfigureBorderlessWindow(this);
            WindowFadeController.Prepare(this);

            var hwnd = WindowNative.GetWindowHandle(this);
            // 托盘菜单不进任务栏/Alt-Tab。
            AppWindow.IsShownInSwitchers = false;
            int extendedStyle = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, extendedStyle | WsExToolWindow);
            double scale = Math.Max(1.0, GetDpiForWindow(hwnd) / 96.0);
            _screenX = screenX;
            _screenY = screenY;
            _scale = scale;

            // 先给一个足够高的临时尺寸；Loaded 时再按实际内容高度收缩，避免底部文字被裁。
            MoveMenu((int)(WidthDip * scale), (int)(ProvisionalHeightDip * scale));

            Activated += TrayMenuWindow_Activated;
            Closed += (_, _) => _result.TrySetResult(_pending);
        }

        public Task<TrayMenuAction?> Result => _result.Task;

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // 按实际内容高度收缩窗口（Border 顶部对齐，实际高度就是内容高度）。
            double contentHeight = MenuBorder.ActualHeight
                                   + RootGrid.Padding.Top
                                   + RootGrid.Padding.Bottom;
            int height = (int)(Math.Max(120, contentHeight) * _scale);
            MoveMenu((int)(WidthDip * _scale), height);

            WindowFadeController.FadeAndScaleIn(this, RootGrid);
            ShowButton.Focus(FocusState.Programmatic);
        }

        private void MoveMenu(int width, int height)
        {
            var work = DisplayArea.GetFromPoint(
                new PointInt32(_screenX, _screenY), DisplayAreaFallback.Nearest).WorkArea;
            int left = Math.Clamp(_screenX - width / 8, work.X, Math.Max(work.X, work.X + work.Width - width));
            int top = Math.Clamp(_screenY - height, work.Y, Math.Max(work.Y, work.Y + work.Height - height));
            AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
        }

        private void TrayMenuWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                if (_activatedOnce)
                    CloseMenu(null);
                return;
            }

            _activatedOnce = true;
        }

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CloseMenu(null);
            }
        }

        private void ShowButton_Click(object sender, RoutedEventArgs e) => CloseMenu(TrayMenuAction.Show);

        private void GlobalSearchButton_Click(object sender, RoutedEventArgs e) => CloseMenu(TrayMenuAction.GlobalSearch);

        private void SettingsButton_Click(object sender, RoutedEventArgs e) => CloseMenu(TrayMenuAction.Settings);

        private void HideButton_Click(object sender, RoutedEventArgs e) => CloseMenu(TrayMenuAction.Hide);

        private void ExitButton_Click(object sender, RoutedEventArgs e) => CloseMenu(TrayMenuAction.Exit);

        private void CloseMenu(TrayMenuAction? action)
        {
            if (_closing) return;
            _closing = true;
            _pending = action;
            WindowFadeController.CloseWithAnimation(this);
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private const int GwlExStyle = -20;
        private const int WsExToolWindow = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        private static int GetWindowLong(IntPtr hwnd, int index)
            => IntPtr.Size == 8 ? (int)GetWindowLongPtr64(hwnd, index) : GetWindowLong32(hwnd, index);

        private static void SetWindowLong(IntPtr hwnd, int index, int value)
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(hwnd, index, new IntPtr(value));
            else
                SetWindowLong32(hwnd, index, value);
        }
    }
}
