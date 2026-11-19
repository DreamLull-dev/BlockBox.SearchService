using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BlockBox.Helpers
{
    public static class ClipboardHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        private const uint CF_HDROP = 15;
        private const uint GMEM_MOVEABLE = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct DROPFILES
        {
            public uint pFiles;
            public int x;
            public int y;
            public int fNC;
            public int fWide;
        }

        /// <summary>
        /// 把文件列表放入系统剪贴板（CF_HDROP），资源管理器可以粘贴
        /// </summary>
        public static bool SetFileDropList(IntPtr hwnd, IEnumerable<string> filePaths)
        {
            var sb = new StringBuilder();
            int count = 0;
            foreach (var p in filePaths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                sb.Append(p);
                sb.Append('\0');
                count++;
            }
            if (count == 0) return false;
            sb.Append('\0');

            byte[] strBytes = Encoding.Unicode.GetBytes(sb.ToString());
            int dropSize = Marshal.SizeOf<DROPFILES>();
            int totalSize = dropSize + strBytes.Length;

            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)totalSize);
            if (hGlobal == IntPtr.Zero) return false;

            IntPtr ptr = GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero) { GlobalFree(hGlobal); return false; }

            try
            {
                var df = new DROPFILES { pFiles = (uint)dropSize, fWide = 1 };
                Marshal.StructureToPtr(df, ptr, false);
                Marshal.Copy(strBytes, 0, ptr + dropSize, strBytes.Length);
            }
            finally { GlobalUnlock(hGlobal); }

            if (!OpenClipboard(hwnd)) { GlobalFree(hGlobal); return false; }

            try
            {
                EmptyClipboard();
                if (SetClipboardData(CF_HDROP, hGlobal) == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    return false;
                }
                // 成功时所有权转移给系统，不要 Free
            }
            finally { CloseClipboard(); }

            return true;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile,
            System.Text.StringBuilder? lpszFile, uint cch);

        /// <summary>
        /// 从系统剪贴板读文件路径列表（CF_HDROP）。
        /// 资源管理器复制的文件，这个方法能拿到完整路径。
        /// </summary>
        public static List<string> GetFileDropList()
        {
            var result = new List<string>();
            if (!OpenClipboard(IntPtr.Zero)) return result;

            try
            {
                IntPtr hDrop = GetClipboardData(CF_HDROP);
                if (hDrop == IntPtr.Zero) return result;

                uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);

                for (uint i = 0; i < count; i++)
                {
                    uint len = DragQueryFile(hDrop, i, null, 0);
                    if (len == 0) continue;

                    var sb = new System.Text.StringBuilder((int)len + 1);
                    DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                    string path = sb.ToString();
                    if (!string.IsNullOrEmpty(path)) result.Add(path);
                }
            }
            finally { CloseClipboard(); }

            return result;
        }
        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        /// <summary>
        /// 获取剪贴板序列号（内容变化时会改变）
        /// </summary>
        public static uint GetClipboardSequenceNumberSafe()
        {
            try { return GetClipboardSequenceNumber(); }
            catch { return 0; }
        }
    }
}