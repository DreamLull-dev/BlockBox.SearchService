using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace BlockBox.SearchService;

/// <summary>
/// Enumerates NTFS file-name records through FSCTL_ENUM_USN_DATA. The caller must run
/// with an account that can open the raw volume (the installed service will run as LocalSystem).
/// </summary>
internal static class NtfsMftEnumerator
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FsctlEnumUsnData = 0x000900B3;
    private const int BufferSize = 1024 * 1024;

    /// <param name="startAt">
    /// MFT record number to start at, used to resume an interrupted initial scan. The record with
    /// this number is returned again, so callers must treat replayed entries as upserts.
    /// </param>
    internal static IEnumerable<MftEntry> Enumerate(string driveRoot, CancellationToken cancellationToken, long startAt = 0)
    {
        string volume = ToVolumeDevicePath(driveRoot);
        using SafeFileHandle handle = CreateFileW(volume, GenericRead, ShareRead | ShareWrite,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open NTFS volume {volume}");

        long startReference = startAt & 0x0000FFFFFFFFFFFF;
        var output = new byte[BufferSize];
        while (!cancellationToken.IsCancellationRequested)
        {
            var request = new MftEnumData { StartFileReferenceNumber = startReference, LowUsn = 0, HighUsn = long.MaxValue };
            int inputSize = Marshal.SizeOf<MftEnumData>();
            IntPtr input = Marshal.AllocHGlobal(inputSize);
            int bytesReturned;
            try
            {
                Marshal.StructureToPtr(request, input, false);
                if (!DeviceIoControl(handle, FsctlEnumUsnData, input, inputSize, output, output.Length,
                    out bytesReturned, IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 38) yield break; // ERROR_HANDLE_EOF
                    throw new Win32Exception(error, $"FSCTL_ENUM_USN_DATA failed for {volume}");
                }
            }
            finally { Marshal.FreeHGlobal(input); }

            if (bytesReturned < sizeof(long)) yield break;
            startReference = BitConverter.ToInt64(output, 0);
            int offset = sizeof(long);
            while (offset + 60 <= bytesReturned)
            {
                int recordLength = BitConverter.ToInt32(output, offset);
                if (recordLength < 60 || offset + recordLength > bytesReturned) break;
                ushort majorVersion = BitConverter.ToUInt16(output, offset + 4);
                if (majorVersion == 2)
                {
                    long fileReference = BitConverter.ToInt64(output, offset + 8);
                    long parentReference = BitConverter.ToInt64(output, offset + 16);
                    uint attributes = BitConverter.ToUInt32(output, offset + 52);
                    ushort nameLength = BitConverter.ToUInt16(output, offset + 56);
                    ushort nameOffset = BitConverter.ToUInt16(output, offset + 58);
                    if (nameOffset + nameLength <= recordLength && nameLength % 2 == 0)
                    {
                        string name = Encoding.Unicode.GetString(output, offset + nameOffset, nameLength);
                        yield return new MftEntry(fileReference, parentReference, name,
                            (attributes & 0x10) != 0);
                    }
                }
                offset += recordLength;
            }
            if (bytesReturned == sizeof(long)) yield break;
        }
    }

    private static string ToVolumeDevicePath(string driveRoot)
    {
        string root = Path.GetPathRoot(driveRoot) ?? throw new ArgumentException("Invalid drive root", nameof(driveRoot));
        if (root.Length < 2 || root[1] != ':') throw new ArgumentException("Expected a drive-letter volume", nameof(driveRoot));
        return "\\\\.\\" + root[..2];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MftEnumData
    {
        public long StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr inputBuffer,
        int inputBufferSize, byte[] outputBuffer, int outputBufferSize, out int bytesReturned, IntPtr overlapped);
}

internal sealed record MftEntry(long FileReference, long ParentReference, string Name, bool IsDirectory);
