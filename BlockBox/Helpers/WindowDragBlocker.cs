using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BlockBox.Helpers
{
    internal sealed class WindowDragBlocker : IDisposable
    {
        private const uint WmNcHitTest = 0x0084;
        private const uint WmSysCommand = 0x0112;
        private const long HtCaption = 2;
        private const long HtClient = 1;
        private const long ScMove = 0xF010;
        private static readonly UIntPtr SubclassId = new(1);

        private readonly IntPtr _windowHandle;
        private readonly SubclassProc _subclassProc;
        private bool _disposed;

        public WindowDragBlocker(Window window)
        {
            _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _subclassProc = WindowProc;

            if (!SetWindowSubclass(_windowHandle, _subclassProc, SubclassId, UIntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            RemoveWindowSubclass(_windowHandle, _subclassProc, SubclassId);
            _disposed = true;
        }

        private IntPtr WindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam,
            UIntPtr subclassId, UIntPtr referenceData)
        {
            if (message == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScMove)
                return IntPtr.Zero;

            IntPtr result = DefSubclassProc(windowHandle, message, wParam, lParam);
            if (message == WmNcHitTest && result.ToInt64() == HtCaption)
                return new IntPtr(HtClient);

            return result;
        }

        private delegate IntPtr SubclassProc(IntPtr windowHandle, uint message, IntPtr wParam,
            IntPtr lParam, UIntPtr subclassId, UIntPtr referenceData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(IntPtr windowHandle, SubclassProc subclassProc,
            UIntPtr subclassId, UIntPtr referenceData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(IntPtr windowHandle, SubclassProc subclassProc,
            UIntPtr subclassId);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr windowHandle, uint message,
            IntPtr wParam, IntPtr lParam);
    }
}
