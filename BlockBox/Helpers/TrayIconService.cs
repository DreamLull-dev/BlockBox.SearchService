using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BlockBox.Helpers
{
    public sealed class TrayIconService : IDisposable
    {
        private const int GwlWndProc = -4;
        private const int WmUser = 0x0400;
        private const int WmTray = WmUser + 1;
        private const int WmLButtonDblClk = 0x0203;
        private const int WmRButtonUp = 0x0205;
        private const int WmSize = 0x0005;
        /// <summary>
        /// 第二个实例用它唤醒已有实例：注册消息在会话内数值一致，主窗体即使收在托盘里
        /// （不可见）也能收到；改用广播后不再依赖"按窗口类名找窗口"，避免找错搜索面板。
        /// </summary>
        public static readonly uint ShowExistingInstanceMessage = RegisterWindowMessage("BlockBox.ShowExistingInstance");
        private const int WmCommand = 0x0111;
        private const int SizeMinimized = 1;
        private const uint NimAdd = 0;
        private const uint NimModify = 1;
        private const uint NimDelete = 2;
        private const uint NifMessage = 1;
        private const uint NifIcon = 2;
        private const uint NifTip = 4;
        private const uint NifInfo = 0x10;
        private const uint NiifInfo = 1;
        private const uint LoadIconType = 1;
        private const int IdiApplication = 32512;
        private const int SwHide = 0;
        private const int SwShow = 5;
        private const uint MfString = 0x0000;
        private const uint MfGrayed = 0x0001;
        private const uint TpmLeftButton = 0x0000;
        private const uint TpmRightButton = 0x0002;
        private const uint TpmLeftAlign = 0x0000;
        private const uint TpmRightAlign = 0x0008;
        private const uint TpmBottomAlign = 0x0020;
        private const uint TpmReturnCommand = 0x0100;
        private const uint MenuShow = 1001;
        private const uint MenuExit = 1002;
        private const uint MenuHide = 1003;
        private const uint MenuSettings = 1004;
        private const uint MenuGlobalSearch = 1005;

        private readonly Window _window;
        private readonly IntPtr _hwnd;
        private readonly WndProc _wndProc;
        private readonly string? _iconPath;
        private IntPtr _oldWndProc;
        private IntPtr _trayIconHandle;
        private uint _iconId;
        private bool _added;
        private bool _disposed;

        public event EventHandler? RestoreRequested;
        public event EventHandler? MinimizedRequested;
        public event EventHandler? ExitRequested;
        public event EventHandler? SettingsRequested;
        public event EventHandler? GlobalSearchRequested;
        public event EventHandler? ShowExistingInstanceRequested;

        public TrayIconService(Window window, string? iconPath = null)
        {
            _window = window;
            _iconPath = iconPath;
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _wndProc = WindowProc;
        }

        public void Initialize()
        {
            if (_added || _disposed)
                return;

            _oldWndProc = SetWindowLongPtr(_hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_wndProc));
            _iconId = unchecked((uint)Environment.TickCount);
            if (!string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath))
                _trayIconHandle = LoadImage(IntPtr.Zero, _iconPath, ImageIcon, 0, 0, LoadFromFile);

            var data = CreateData();
            Shell_NotifyIcon(NimAdd, ref data);
            _added = true;
        }

        public void Hide()
        {
            if (!_disposed)
                ShowWindow(_hwnd, SwHide);
        }

        public void Show()
        {
            if (_disposed)
                return;

            ShowWindow(_hwnd, SwShow);
            _window.Activate();
        }

        public void ShowEnteredTrayNotification()
        {
            if (_disposed || !_added)
                return;

            var data = CreateData();
            data.uFlags |= NifInfo;
            data.szInfoTitle = LanguageService.Translate("BlockBox");
            data.szInfo = LanguageService.Translate("程序已进入托盘");
            data.dwInfoFlags = NiifInfo;
            Shell_NotifyIcon(NimModify, ref data);
        }

        private NOTIFYICONDATA CreateData()
        {
            return new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = _iconId,
                uFlags = NifMessage | NifIcon | NifTip,
                uCallbackMessage = WmTray,
                hIcon = _trayIconHandle != IntPtr.Zero
                    ? _trayIconHandle
                    : LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication)),
                szTip = "BlockBox"
            };
        }

        private IntPtr WindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == ShowExistingInstanceMessage)
            {
                ShowExistingInstanceRequested?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            }

            if (message == WmTray)
            {
                int notification = unchecked((int)lParam.ToInt64());
                if (notification == WmLButtonDblClk)
                    RestoreRequested?.Invoke(this, EventArgs.Empty);
                else if (notification == WmRButtonUp)
                    ShowContextMenu();
                return IntPtr.Zero;
            }

            if (message == WmCommand)
            {
                uint command = unchecked((uint)wParam.ToInt64()) & 0xFFFF;
                if (command == MenuShow)
                    RestoreRequested?.Invoke(this, EventArgs.Empty);
                else if (command == MenuExit)
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                else if (command == MenuHide)
                {
                    if (IsWindowVisible(_hwnd))
                        Hide();
                }
                else if (command == MenuSettings)
                    SettingsRequested?.Invoke(this, EventArgs.Empty);
                else if (command == MenuGlobalSearch)
                    GlobalSearchRequested?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            }

            if (message == WmSize && wParam.ToInt32() == SizeMinimized)
                MinimizedRequested?.Invoke(this, EventArgs.Empty);

            return CallWindowProc(_oldWndProc, hWnd, message, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
                return;

            try
            {
                AppendMenu(menu, MfString, MenuShow, LanguageService.Translate("显示 BlockBox"));
                AppendMenu(menu, MfString, MenuGlobalSearch, LanguageService.Translate("显示全局搜索"));
                AppendMenu(menu, MfString, MenuSettings, LanguageService.Translate("设置"));
                uint hideMenuFlags = IsWindowVisible(_hwnd) ? MfString : MfString | MfGrayed;
                AppendMenu(menu, hideMenuFlags, MenuHide, LanguageService.Translate("隐藏 BlockBox 界面"));
                AppendMenu(menu, MfString, MenuExit, LanguageService.Translate("退出"));
                GetCursorPos(out POINT point);
                SetForegroundWindow(_hwnd);
                uint alignment = IsCursorInRightHalf(point) ? TpmRightAlign : TpmLeftAlign;
                uint command = TrackPopupMenu(
                    menu,
                    TpmLeftButton | TpmRightButton | TpmBottomAlign | alignment | TpmReturnCommand,
                    point.X,
                    point.Y,
                    0,
                    _hwnd,
                    IntPtr.Zero);
                if (command == MenuShow)
                    RestoreRequested?.Invoke(this, EventArgs.Empty);
                else if (command == MenuHide)
                {
                    if (IsWindowVisible(_hwnd))
                        Hide();
                }
                else if (command == MenuExit)
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                else if (command == MenuSettings)
                    SettingsRequested?.Invoke(this, EventArgs.Empty);
                else if (command == MenuGlobalSearch)
                    GlobalSearchRequested?.Invoke(this, EventArgs.Empty);
                PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        private static bool IsCursorInRightHalf(POINT point)
        {
            IntPtr monitor = MonitorFromPoint(point, 2);
            if (monitor == IntPtr.Zero)
                return point.X >= GetSystemMetrics(0) / 2;

            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            return GetMonitorInfo(monitor, ref info)
                && point.X >= info.rcWork.Left + (info.rcWork.Right - info.rcWork.Left) / 2;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_added)
            {
                var data = CreateData();
                Shell_NotifyIcon(NimDelete, ref data);
                _added = false;
            }

            if (_oldWndProc != IntPtr.Zero)
                SetWindowLongPtr(_hwnd, GwlWndProc, _oldWndProc);

            if (_trayIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_trayIconHandle);
                _trayIconHandle = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        private const uint ImageIcon = 1;
        private const uint LoadFromFile = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string message);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr iconName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInstance, string name, uint type, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int index, IntPtr value);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value) =>
            IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, index, value) : SetWindowLong32(hWnd, index, value);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr menu, uint flags, uint id, string text);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    }
}
