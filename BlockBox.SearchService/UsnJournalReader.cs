using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace BlockBox.SearchService;

internal sealed class UsnJournalReader : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 1;
    private const uint ShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint FsctlQueryUsnJournal = 0x000900F4;
    private const uint FsctlReadUsnJournal = 0x000900BB;
    private readonly SafeFileHandle _volume;
    private readonly string _volumePath;
    // Reused across reads: a 1 MB allocation per call lands on the large object heap, and the
    // watcher calls this roughly once a second even when the machine is idle. Reusing the
    // buffers keeps the service from generating steady GC pressure while nothing happens.
    private readonly byte[] _queryBuffer = new byte[64];
    private readonly byte[] _recordBuffer = new byte[1024 * 1024];

    internal UsnJournalReader(string driveRoot)
    {
        string root = Path.GetPathRoot(driveRoot) ?? throw new ArgumentException("Invalid volume root", nameof(driveRoot));
        if (root.Length < 2 || root[1] != ':') throw new ArgumentException("Expected a drive-letter volume", nameof(driveRoot));
        _volumePath = "\\\\.\\" + root[..2];
        _volume = CreateFileW(_volumePath, GenericRead, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (_volume.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open volume {_volumePath}");
    }

    internal JournalState Query()
    {
        byte[] bytes = _queryBuffer;
        if (!DeviceIoControl(_volume, FsctlQueryUsnJournal, IntPtr.Zero, 0, bytes, bytes.Length, out int returned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot query USN journal for {_volumePath}");
        if (returned < 56) throw new InvalidDataException("USN journal response is truncated");
        return new JournalState(BitConverter.ToUInt64(bytes, 0), BitConverter.ToInt64(bytes, 8),
            BitConverter.ToInt64(bytes, 16), BitConverter.ToInt64(bytes, 24));
    }

    internal JournalReadResult Read(long startUsn, ulong journalId, bool waitForChanges, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = new ReadJournalData
        {
            StartUsn = startUsn,
            ReasonMask = uint.MaxValue,
            ReturnOnlyOnClose = 0,
            Timeout = waitForChanges ? 1UL : 0UL,
            BytesToWaitFor = waitForChanges ? 1UL : 0UL,
            UsnJournalId = journalId
        };
        int requestSize = Marshal.SizeOf<ReadJournalData>();
        IntPtr requestPtr = Marshal.AllocHGlobal(requestSize);
        byte[] output = _recordBuffer;
        try
        {
            Marshal.StructureToPtr(request, requestPtr, false);
            if (!DeviceIoControl(_volume, FsctlReadUsnJournal, requestPtr, requestSize,
                output, output.Length, out int returned, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot read USN journal for {_volumePath}");
            if (returned < sizeof(long)) return new JournalReadResult(startUsn, Array.Empty<JournalEntry>());
            long nextUsn = BitConverter.ToInt64(output, 0);
            var records = new List<JournalEntry>();
            int offset = sizeof(long);
            while (offset + 60 <= returned)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int length = BitConverter.ToInt32(output, offset);
                if (length < 60 || offset + length > returned) break;
                if (BitConverter.ToUInt16(output, offset + 4) == 2)
                {
                    long fileId = BitConverter.ToInt64(output, offset + 8);
                    long parentId = BitConverter.ToInt64(output, offset + 16);
                    long usn = BitConverter.ToInt64(output, offset + 24);
                    uint reason = BitConverter.ToUInt32(output, offset + 40);
                    uint attributes = BitConverter.ToUInt32(output, offset + 52);
                    ushort nameLength = BitConverter.ToUInt16(output, offset + 56);
                    ushort nameOffset = BitConverter.ToUInt16(output, offset + 58);
                    if (nameLength % 2 == 0 && nameOffset + nameLength <= length)
                    {
                        string name = Encoding.Unicode.GetString(output, offset + nameOffset, nameLength);
                        records.Add(new JournalEntry(fileId, parentId, usn, reason, name, (attributes & 0x10) != 0));
                    }
                }
                offset += length;
            }
            return new JournalReadResult(nextUsn, records);
        }
        finally { Marshal.FreeHGlobal(requestPtr); }
    }

    public void Dispose() => _volume.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadJournalData
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr inputBuffer,
        int inputBufferSize, byte[] outputBuffer, int outputBufferSize, out int bytesReturned, IntPtr overlapped);
}

internal sealed record JournalState(ulong JournalId, long FirstUsn, long NextUsn, long LowestValidUsn);
internal sealed record JournalEntry(long FileReference, long ParentReference, long Usn, uint Reason, string Name, bool IsDirectory);
internal sealed record JournalReadResult(long NextUsn, IReadOnlyList<JournalEntry> Entries);
