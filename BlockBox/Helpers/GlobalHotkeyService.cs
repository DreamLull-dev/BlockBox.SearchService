using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BlockBox.Helpers
{
    public sealed class GlobalHotkeyService : IDisposable
    {
        private const int GwlWndProc = -4;
        private const uint WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;
        private readonly IntPtr _hwnd;
        private readonly WndProc _wndProc;
        private readonly Dictionary<int, string> _actions = new();
        private IntPtr _oldWndProc;
        private int _nextId = 1;
        private bool _disposed;

        public event EventHandler<string>? HotkeyPressed;

        public GlobalHotkeyService(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _wndProc = WindowProc;
        }

        public void Register(IReadOnlyDictionary<string, string> hotkeys)
        {
            Clear();
            if (_oldWndProc == IntPtr.Zero)
                _oldWndProc = SetWindowLongPtr(_hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_wndProc));

            foreach (var pair in hotkeys)
            {
                if (!TryParse(pair.Value, out uint modifiers, out uint key))
                    continue;

                int id = _nextId++;
                if (RegisterHotKey(_hwnd, id, modifiers, key))
                    _actions[id] = pair.Key;
            }
        }

        public void Clear()
        {
            foreach (int id in _actions.Keys)
                UnregisterHotKey(_hwnd, id);
            _actions.Clear();
        }

        private IntPtr WindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out string? action))
            {
                HotkeyPressed?.Invoke(this, action);
                return IntPtr.Zero;
            }
            return CallWindowProc(_oldWndProc, hWnd, message, wParam, lParam);
        }

        private static bool TryParse(string text, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;
            if (!HotkeyGesture.TryParse(text, out Windows.System.VirtualKey virtualKey,
                    out Windows.System.VirtualKeyModifiers virtualModifiers))
                return false;

            modifiers = (uint)virtualModifiers;
            key = (uint)virtualKey;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Clear();
            if (_oldWndProc != IntPtr.Zero)
                SetWindowLongPtr(_hwnd, GwlWndProc, _oldWndProc);
        }

        private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern IntPtr SetWindowLong32(IntPtr hWnd, int index, IntPtr value);
        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, index, value) : SetWindowLong32(hWnd, index, value);
    }
}
