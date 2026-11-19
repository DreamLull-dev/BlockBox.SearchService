using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;

namespace BlockBox.Helpers
{
    public static class ShellIconHelper
    {
        private static readonly ConcurrentDictionary<string, byte[]> _thumbnailCache = new();
        private static readonly Queue<string> _cacheOrder = new();
        private static readonly object _cacheGate = new();
        private const int MaxCacheEntries = 512;

        // ==================== GDI P/Invoke ====================

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int GetObject(IntPtr hObject, int nCount, ref BITMAP lpObject);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(
            IntPtr hdc, IntPtr hbm, uint start, uint cLines,
            byte[] lpvBits, IntPtr lpbmi, uint usage);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        // ==================== IShellItemImageFactory ====================

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [Flags]
        private enum SIIGBF
        {
            RESIZETOFIT = 0x00,
            BIGGERSIZEOK = 0x01,
            MEMORYONLY = 0x02,
            ICONONLY = 0x04,
            THUMBNAILONLY = 0x08,
            INCACHEONLY = 0x10,
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            in Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("shell32.dll", EntryPoint = "SHDefExtractIconW", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHDefExtractIcon(
            string iconFile, int iconIndex, uint flags, out IntPtr largeIcon, out IntPtr smallIcon, uint iconSize);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        // ==================== 主入口 ====================

        public static byte[]? GetThumbnailPngBytes(string filePath, int size = 96, bool preferPowerShell = false)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            if (TryBuildCacheKey(filePath, size, preferPowerShell, out string? cacheKey)
                && cacheKey != null
                && _thumbnailCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            if (preferPowerShell && Path.GetExtension(filePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var shortcutIcon = TryExtractShortcutTargetIcon(filePath, size);
                if (shortcutIcon is { Length: > 0 })
                {
                    if (cacheKey != null)
                        PutCache(cacheKey, shortcutIcon);
                    return shortcutIcon;
                }
            }

            if (Path.GetExtension(filePath).Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                var internetShortcutIcon = TryExtractInternetShortcutIcon(filePath, size);
                if (internetShortcutIcon is { Length: > 0 })
                {
                    if (cacheKey != null)
                        PutCache(cacheKey, internetShortcutIcon);
                    return internetShortcutIcon;
                }
            }

            IShellItemImageFactory? factory = null;
            IntPtr hBitmap = IntPtr.Zero;

            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(filePath, IntPtr.Zero, iid, out factory);
                if (factory == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ShellIcon] factory 创建失败: {filePath}");
                    return TryExtractShortcutTargetIcon(filePath, size);
                }

                var sz = new SIZE { cx = size, cy = size };
                int hr = factory.GetImage(
                    sz, SIIGBF.BIGGERSIZEOK | SIIGBF.RESIZETOFIT, out hBitmap);

                if (hr != 0 || hBitmap == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ShellIcon] GetImage 失败 hr=0x{hr:X8} hBitmap={hBitmap}: {filePath}");
                    return TryExtractShortcutTargetIcon(filePath, size);
                }

                var result = HBitmapToPngBytes(hBitmap);
                if (result == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ShellIcon] 转换 PNG 失败: {filePath}");
                    return TryExtractShortcutTargetIcon(filePath, size);
                }

                if (filePath.Contains("OpenAL", StringComparison.OrdinalIgnoreCase))
                {
                    var bitmap = new BITMAP();
                    int count = GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref bitmap);
                    if (count != 0)
                        System.Diagnostics.Debug.WriteLine(
                            $"[ShellIcon] OpenAL Shell 提取; Path={filePath}; Requested={size}; Actual={bitmap.bmWidth}x{Math.Abs(bitmap.bmHeight)}; PngBytes={result.Length}");
                }

                if (result != null && cacheKey != null)
                    PutCache(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShellIcon] Shell 提取异常; Path={filePath}; Error={ex.Message}");
                return TryExtractShortcutTargetIcon(filePath, size);
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }

        public static void ClearMemoryCache()
        {
            lock (_cacheGate)
            {
                _thumbnailCache.Clear();
                _cacheOrder.Clear();
            }
        }

        private static byte[]? TryExtractInternetShortcutIcon(string shortcutPath, int size)
        {
            try
            {
                string? iconPath = null;
                int iconIndex = 0;
                bool inInternetShortcutSection = false;

                foreach (string rawLine in File.ReadLines(shortcutPath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                        continue;

                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        inInternetShortcutSection = line[1..^1].Trim().Equals(
                            "InternetShortcut", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if (!inInternetShortcutSection)
                        continue;

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;

                    string key = line[..separator].Trim();
                    string value = line[(separator + 1)..].Trim().Trim('"');
                    if (key.Equals("IconFile", StringComparison.OrdinalIgnoreCase))
                    {
                        iconPath = value;
                    }
                    else if (key.Equals("IconIndex", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIndex))
                    {
                        iconIndex = parsedIndex;
                    }
                }

                if (string.IsNullOrWhiteSpace(iconPath))
                    return null;

                iconPath = Environment.ExpandEnvironmentVariables(iconPath);
                if (!Path.IsPathRooted(iconPath))
                    iconPath = Path.Combine(Path.GetDirectoryName(shortcutPath) ?? "", iconPath);
                if (!File.Exists(iconPath))
                    return null;

                IntPtr largeIcon = IntPtr.Zero;
                IntPtr smallIcon = IntPtr.Zero;
                try
                {
                    uint requestedSize = ((uint)size << 16) | (uint)size;
                    int result = SHDefExtractIcon(iconPath, iconIndex, 0, out largeIcon, out smallIcon, requestedSize);
                    if (result < 0 || largeIcon == IntPtr.Zero)
                        return null;

                    using var icon = (Icon)Icon.FromHandle(largeIcon).Clone();
                    using var bitmap = icon.ToBitmap();
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
                finally
                {
                    if (largeIcon != IntPtr.Zero) DestroyIcon(largeIcon);
                    if (smallIcon != IntPtr.Zero) DestroyIcon(smallIcon);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ShellIcon] Internet Shortcut 图标提取失败; Shortcut={shortcutPath}; Error={ex.Message}");
                return null;
            }
        }

        private static byte[]? TryExtractShortcutTargetIcon(string shortcutPath, int size)
        {
            if (!Path.GetExtension(shortcutPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                return null;

            string outputPath = Path.Combine(Path.GetTempPath(), $"BlockBox-{Guid.NewGuid():N}.png");
            string shortcutLiteral = "'" + shortcutPath.Replace("'", "''") + "'";
            string outputLiteral = "'" + outputPath.Replace("'", "''") + "'";
            const string extractorType = @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class BlockBoxIconExtractor
{
    [DllImport(""shell32.dll"", EntryPoint = ""SHDefExtractIconW"", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHDefExtractIcon(string path, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
    [DllImport(""user32.dll"")] private static extern bool DestroyIcon(IntPtr handle);
    public static void SavePng(string source, int index, string output, int size)
    {
        IntPtr large, small;
        uint requested = ((uint)size << 16) | (uint)size;
        int hr = SHDefExtractIcon(source, index, 0, out large, out small, requested);
        if (hr < 0 || large == IntPtr.Zero) Marshal.ThrowExceptionForHR(hr);
        try
        {
        using (var icon = (Icon)Icon.FromHandle(large).Clone())
        using (var bitmap = icon.ToBitmap()) bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        }
        finally
        {
            if (large != IntPtr.Zero) DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
        }
    }
}";
            string encodedExtractor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(extractorType));
            string script = "$ErrorActionPreference='Stop';Add-Type -AssemblyName System.Drawing;"
                + "$extractorSource=[System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encodedExtractor + "'));"
                + "$drawingAssembly=[System.Drawing.Icon].Assembly.Location;"
                + "Add-Type -ReferencedAssemblies $drawingAssembly -TypeDefinition $extractorSource;"
                + "$ws=New-Object -ComObject WScript.Shell;$lnk=$ws.CreateShortcut(" + shortcutLiteral + ");"
                + "$source=$lnk.TargetPath;$index=0;$loc=([string]$lnk.IconLocation).Trim();"
                + "if(-not [string]::IsNullOrWhiteSpace($loc)){$parts=$loc -split ',',2;$candidate=$parts[0].Trim().Trim('\"');"
                + "if(-not [string]::IsNullOrWhiteSpace($candidate)){$source=[Environment]::ExpandEnvironmentVariables($candidate)};"
                + "if($parts.Count -gt 1){$parsed=0;if([int]::TryParse($parts[1].Trim(),[ref]$parsed)){$index=$parsed}}};"
                + "$source=[Environment]::ExpandEnvironmentVariables($source);"
                + "[BlockBoxIconExtractor]::SavePng($source,$index," + outputLiteral + "," + size + ")";
            string encodedCommand = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            string systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string powershellPath = Path.Combine(systemFolder, "WindowsPowerShell", "v1.0", "powershell.exe");

            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = powershellPath,
                    Arguments = $"-NoProfile -NonInteractive -STA -EncodedCommand {encodedCommand}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                });
                if (process == null)
                    return null;

                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || !File.Exists(outputPath))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ShellIcon] PowerShell 图标提取失败; Shortcut={shortcutPath}; PowerShell={powershellPath}; ExitCode={process.ExitCode}; Error={error}");
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(outputPath);
                System.Diagnostics.Debug.WriteLine(
                    $"[ShellIcon] PowerShell 图标提取成功; Shortcut={shortcutPath}; Bytes={bytes.Length}");
                return bytes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ShellIcon] PowerShell 图标回退异常; Shortcut={shortcutPath}; Error={ex}");
                return null;
            }
            finally
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            }
        }

        private static bool TryBuildCacheKey(string filePath, int size, bool preferPowerShell, out string? cacheKey)
        {
            cacheKey = null;
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists)
                    return false;

                long ticks = info.LastWriteTimeUtc.Ticks;
                long length = info.Length;
                cacheKey = $"{filePath.ToLowerInvariant()}|{size}|{ticks}|{length}|{(preferPowerShell ? "powershell" : "shell")}|icon-only-v10-url-iconfile";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void PutCache(string key, byte[] value)
        {
            lock (_cacheGate)
            {
                _thumbnailCache[key] = value;
                _cacheOrder.Enqueue(key);

                while (_cacheOrder.Count > MaxCacheEntries)
                {
                    var oldKey = _cacheOrder.Dequeue();
                    _thumbnailCache.TryRemove(oldKey, out _);
                }
            }
        }

        // ==================== HBITMAP → PNG ====================

        private static byte[]? HBitmapToPngBytes(IntPtr hBitmap)
        {
            IntPtr hdc = IntPtr.Zero;
            IntPtr pBmi = IntPtr.Zero;

            try
            {
                // 1. 拿尺寸
                var bm = new BITMAP();
                int got = GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref bm);
                if (got == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[ShellIcon] GetObject 失败");
                    return null;
                }

                int width = bm.bmWidth;
                int height = Math.Abs(bm.bmHeight);
                if (width <= 0 || height <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[ShellIcon] 尺寸异常 {width}x{height}");
                    return null;
                }

                // 2. 分配 BITMAPINFO（header + 颜色表占位）
                int headerSize = Marshal.SizeOf<BITMAPINFOHEADER>();
                int bmiSize = headerSize + 256 * 4;
                pBmi = Marshal.AllocHGlobal(bmiSize);
                for (int i = 0; i < bmiSize; i++) Marshal.WriteByte(pBmi, i, 0);

                var hdr = new BITMAPINFOHEADER
                {
                    biSize = (uint)headerSize,
                    biWidth = width,
                    biHeight = -height,                 // 负数 = 自上而下
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,                       // BI_RGB
                    biSizeImage = (uint)(width * height * 4),
                };
                Marshal.StructureToPtr(hdr, pBmi, false);

                // 3. 分配像素缓冲
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];

                // 4. 调 GetDIBits
                hdc = CreateCompatibleDC(IntPtr.Zero);
                int lines = GetDIBits(hdc, hBitmap, 0, (uint)height,
                    pixels, pBmi, 0);

                if (lines == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[ShellIcon] GetDIBits 返回 0");
                    return null;
                }

                // 5. 读回 biHeight 符号，判断是否需要翻转
                var outHdr = Marshal.PtrToStructure<BITMAPINFOHEADER>(pBmi);
                bool bottomUp = outHdr.biHeight > 0;

                // 6. 写 Bitmap
                using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var data = bmp.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                try
                {
                    if (bottomUp)
                    {
                        // 自下而上 → 逐行拷贝，反向写入
                        for (int y = 0; y < height; y++)
                        {
                            int srcRow = height - 1 - y;
                            IntPtr dst = IntPtr.Add(data.Scan0, y * data.Stride);
                            Marshal.Copy(pixels, srcRow * stride, dst, stride);
                        }
                    }
                    else
                    {
                        // 已经是自上而下，一次性拷
                        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                    }
                }
                finally
                {
                    bmp.UnlockBits(data);
                }

                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShellIcon] HBitmapToPngBytes 异常: {ex.Message}");
                return null;
            }
            finally
            {
                if (hdc != IntPtr.Zero) DeleteDC(hdc);
                if (pBmi != IntPtr.Zero) Marshal.FreeHGlobal(pBmi);
            }
        }
    }
}