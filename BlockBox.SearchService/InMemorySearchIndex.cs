using System.Text;

namespace BlockBox.SearchService;

/// <summary>服务端排序键：名称、大小、修改时间（后两者由后台元数据补齐提供）。</summary>
internal enum SearchSort
{
    Name,
    Size,
    Time
}

/// <summary>
/// Everything-style in-memory index. File names live in two flat pools (UTF-16 for display,
/// ASCII-folded UTF-8 for matching) and every query is a scan over those arrays: no SQLite,
/// no file-system calls, and paths are rebuilt from parent references only for the rows that
/// are actually returned.
/// </summary>
internal sealed class SearchCatalog
{
    private const uint FileCreate = 0x00000100;
    private const uint FileDelete = 0x00000200;
    private const uint RenameOldName = 0x00001000;
    private const uint RenameNewName = 0x00002000;
    private const uint BasicInfoChange = 0x00008000;

    private static readonly byte[][] ApplicationExtensions = Extensions(".exe", ".lnk", ".url", ".bat", ".cmd", ".msi");
    private static readonly byte[][] ImageExtensions = Extensions(".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff");
    private static readonly byte[][] AudioExtensions = Extensions(".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".wma");
    private static readonly byte[][] VideoExtensions = Extensions(".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v");
    private static readonly byte[][] ArchiveExtensions = Extensions(".zip", ".rar", ".7z", ".tar", ".gz");
    private static readonly byte[][] NoExtensions = Array.Empty<byte[]>();

    private readonly object _sync = new();
    private readonly Dictionary<string, VolumeIndex> _volumes = new(StringComparer.Ordinal);

    /// <summary>Starts a full rebuild: the previous in-memory contents of the volume are dropped.</summary>
    internal void BeginVolume(string volumeId, string rootPath)
    {
        lock (_sync) _volumes[volumeId] = new VolumeIndex(volumeId, rootPath, "Indexing", 0, 0);
    }

    /// <summary>Rebuilds a volume from its durable snapshot. Runs before queries are served.</summary>
    internal void LoadVolume(string volumeId, string rootPath, string state, int count, long characters,
        IEnumerable<(long FileId, long ParentId, string Name, bool IsDirectory, long Size, long ModifiedTicks)> entries)
    {
        var volume = new VolumeIndex(volumeId, rootPath, state, count, characters);
        foreach (var entry in entries)
        {
            int index = volume.Upsert(entry.FileId, entry.ParentId, entry.Name, entry.IsDirectory);
            if (entry.ModifiedTicks != 0 || entry.Size >= 0)
                volume.SetMetadata(index, entry.Size, entry.ModifiedTicks);
        }
        lock (_sync) _volumes[volumeId] = volume;
    }

    internal void AddBatch(string volumeId, IReadOnlyCollection<MftEntry> entries)
    {
        if (entries.Count == 0) return;
        lock (_sync)
        {
            VolumeIndex volume = GetOrCreate(volumeId);
            foreach (MftEntry entry in entries)
                volume.Upsert(entry.FileReference, entry.ParentReference, entry.Name, entry.IsDirectory);
            volume.MaybeCompact();
        }
    }

    internal void ApplyJournal(string volumeId, JournalReadResult batch)
    {
        lock (_sync)
        {
            VolumeIndex volume = GetOrCreate(volumeId);
            foreach (JournalEntry entry in batch.Entries)
            {
                // Mirrors the durable store: FILE_DELETE and RENAME_OLD_NAME invalidate exactly
                // this hard-link name; create, rename-new and basic-info changes upsert.
                if ((entry.Reason & (FileDelete | RenameOldName)) != 0)
                    volume.Delete(entry.FileReference, entry.Name);
                if ((entry.Reason & (FileCreate | RenameNewName | BasicInfoChange)) != 0)
                    volume.Upsert(entry.FileReference, entry.ParentReference, entry.Name, entry.IsDirectory);
            }
            volume.MaybeCompact();
        }
    }

    internal void SetState(string volumeId, string state)
    {
        lock (_sync)
        {
            if (_volumes.TryGetValue(volumeId, out VolumeIndex? volume)) volume.State = state;
        }
    }

    /// <summary>
    /// Returns at most <paramref name="limit"/> hits in (exact-match, name, volume, file id) order.
    /// An empty query lists everything (Everything's "show all files" mode), and candidates are
    /// filtered by the cursor while scanning so a later page does not re-sort earlier pages.
    /// <paramref name="descending"/> reverses the name/volume/file-id part of that order while
    /// keeping exact matches first, so the client can append every page at the bottom.
    /// </summary>
    internal List<SearchHit> Search(string query, string kind, int limit,
        string? afterName, string? afterVolumeId, long? afterFileId,
        long? afterSize = null, long? afterModified = null, bool descending = false, SearchSort sort = SearchSort.Name)
    {
        byte[] needle = SearchText.Fold(query);
        byte[] exactPrefix = SearchText.Fold(query + ".");
        byte[][] extensions = ExtensionsFor(kind);
        bool hasCursor = afterName is not null && afterVolumeId is not null && afterFileId is not null;
        Cursor cursor = default;
        if (hasCursor)
        {
            byte[] afterFolded = SearchText.Fold(afterName!);
            byte rank = needle.Length == 0 || SearchText.IsExactMatch(afterFolded, needle, exactPrefix)
                ? (byte)0 : (byte)1;
            cursor = new Cursor(afterName!, afterFolded, afterVolumeId!, afterFileId!.Value,
                afterSize ?? -1, afterModified ?? 0, rank, descending, sort);
        }

        var comparer = new MatchComparer(descending, sort);
        lock (_sync)
        {
            VolumeIndex[] ready = _volumes.Values
                .Where(volume => string.Equals(volume.State, "Ready", StringComparison.Ordinal))
                .ToArray();
            if (ready.Length == 0) return new List<SearchHit>();

            List<Match> selected;
            if (ready.Length == 1)
            {
                var single = new MatchBuffer(limit + 1, comparer);
                ready[0].Collect(needle, exactPrefix, extensions, cursor, hasCursor, single);
                selected = single.Drain();
            }
            else
            {
                // 每个卷独立扫描、各自只保留“页大小”的候选，最后合并：多卷机器上扫描耗时
                // 按卷摊开，单卷查询仍然走上面的直通路径。
                var buffers = new MatchBuffer[ready.Length];
                Parallel.For(0, ready.Length, i =>
                {
                    var buffer = new MatchBuffer(limit + 1, comparer);
                    ready[i].Collect(needle, exactPrefix, extensions, cursor, hasCursor, buffer);
                    buffers[i] = buffer;
                });

                var merged = new List<Match>((limit + 1) * ready.Length);
                foreach (var buffer in buffers) merged.AddRange(buffer.Drain());
                merged.Sort(comparer);
                if (merged.Count > limit + 1) merged.RemoveRange(limit + 1, merged.Count - limit - 1);
                selected = merged;
            }

            var results = new List<SearchHit>(selected.Count);
            foreach (Match match in selected) results.Add(match.Volume.CreateHit(match.Index));
            return results;
        }
    }

    /// <summary>
    /// Keeps only the smallest <c>capacity</c> matches seen so far. Broad queries can match
    /// hundreds of thousands of rows, so the page is selected while scanning instead of
    /// materialising and sorting every hit.
    /// </summary>
    internal sealed class MatchBuffer
    {
        private readonly IComparer<Match> _comparer;
        private readonly PriorityQueue<Match, Match> _heap;
        private readonly int _capacity;

        internal MatchBuffer(int capacity, IComparer<Match> comparer)
        {
            _capacity = Math.Max(capacity, 1);
            _comparer = comparer;
            // The heap is a max-heap under the display order, so its root is the row that would
            // be dropped first when a better one arrives.
            _heap = new PriorityQueue<Match, Match>(_capacity,
                Comparer<Match>.Create((left, right) => comparer.Compare(right, left)));
        }

        internal void Add(in Match match)
        {
            if (_heap.Count < _capacity) { _heap.Enqueue(match, match); return; }
            // The heap root is the worst row currently kept; anything that does not beat it is
            // dropped before it can grow an intermediate result set.
            if (_comparer.Compare(match, _heap.Peek()) >= 0) return;
            _heap.Dequeue();
            _heap.Enqueue(match, match);
        }

        internal List<Match> Drain()
        {
            var items = new List<Match>(_heap.Count);
            while (_heap.TryDequeue(out Match item, out _)) items.Add(item);
            items.Sort(_comparer);
            return items;
        }
    }

    private VolumeIndex GetOrCreate(string volumeId)
    {
        if (_volumes.TryGetValue(volumeId, out VolumeIndex? volume)) return volume;
        volume = new VolumeIndex(volumeId, string.Empty, "Indexing", 0, 0);
        _volumes[volumeId] = volume;
        return volume;
    }

    private static byte[][] ExtensionsFor(string kind) => kind switch
    {
        "application" => ApplicationExtensions,
        "image" => ImageExtensions,
        "audio" => AudioExtensions,
        "video" => VideoExtensions,
        "archive" => ArchiveExtensions,
        _ => NoExtensions
    };

    private static byte[][] Extensions(params string[] values)
    {
        var folded = new byte[values.Length][];
        for (int i = 0; i < values.Length; i++) folded[i] = SearchText.Fold(values[i]);
        return folded;
    }

    internal sealed record MetadataUpdate(string VolumeId, long FileId, string Name, long Size, long ModifiedTicks);

    /// <summary>
    /// 后台把大小/修改时间补进索引：建索引时只枚举 MFT，不 stat 文件。这里按批读盘、
    /// 批量回写；锁只用于“取路径”和“写回”，扫描期间查询不受影响。
    /// </summary>
    internal async Task EnrichMetadataAsync(Func<string, IReadOnlyList<MetadataUpdate>, Task> persist,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (VolumeIndex volume in _volumes.Values.ToArray())
            {
                if (!string.Equals(volume.State, "Ready", StringComparison.Ordinal)) continue;
                await EnrichVolumeAsync(volume, persist, token);
            }
            ServiceLog.Write("metadata enrichment pass finished.");

            try
            {
                // 两分钟后再扫一遍，接手这期间新出现的文件（它们的元数据是未知的）。
                await Task.Delay(TimeSpan.FromMinutes(2), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task EnrichVolumeAsync(VolumeIndex volume,
        Func<string, IReadOnlyList<MetadataUpdate>, Task> persist, CancellationToken token)
    {
        const int batchSize = 512;
        var targets = new List<(int Index, long FileId, string Name, string Path)>(batchSize);
        var updates = new List<MetadataUpdate>(4096);
        int cursor = 0;

        while (!token.IsCancellationRequested)
        {
            targets.Clear();
            lock (_sync)
            {
                int scanned = 0;
                while (cursor < volume.Count && targets.Count < batchSize && scanned < 8192)
                {
                    int index = cursor++;
                    scanned++;
                    if (!volume.IsMetadataUnknown(index)) continue;
                    targets.Add((index, volume.GetFileId(index), volume.GetName(index).ToString(), volume.GetFullPath(index)));
                }
            }

            if (targets.Count == 0)
            {
                if (cursor >= volume.Count) break;
                continue;
            }

            var stats = new (long Size, long Ticks)[targets.Count];
            Parallel.For(0, targets.Count, i => stats[i] = StatPath(targets[i].Path));

            lock (_sync)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    (long size, long ticks) = stats[i];
                    if (ticks == 0 && size < 0) continue;   // 读不到（已消失/无权限）就保持未知
                    if (!volume.TrySetMetadata(targets[i].Index, targets[i].FileId, targets[i].Name, size, ticks)) continue;
                    updates.Add(new MetadataUpdate(volume.VolumeId, targets[i].FileId, targets[i].Name, size, ticks));
                }
            }

            if (updates.Count >= 4096)
            {
                await persist(volume.VolumeId, updates);
                updates = new List<MetadataUpdate>(4096);
            }
        }

        if (updates.Count > 0) await persist(volume.VolumeId, updates);
    }

    private static (long Size, long Ticks) StatPath(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.Exists) return (file.Length, file.LastWriteTimeUtc.Ticks);
            var directory = new DirectoryInfo(path);
            if (directory.Exists) return (-1, directory.LastWriteTimeUtc.Ticks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
        return (0, 0);
    }

    internal readonly struct Match(VolumeIndex volume, int index, byte rank)
    {
        internal VolumeIndex Volume { get; } = volume;
        internal int Index { get; } = index;
        internal byte Rank { get; } = rank;
    }

    private sealed class MatchComparer(bool descending, SearchSort sort) : IComparer<Match>
    {
        public int Compare(Match x, Match y) =>
            VolumeIndex.CompareEntries(x.Volume, x.Index, x.Rank, y.Volume, y.Index, y.Rank, sort, descending);
    }

    internal readonly struct Cursor
    {
        private readonly byte _rank;
        private readonly byte[] _folded;
        private readonly string _volumeId;
        private readonly long _fileId;
        private readonly string _name;
        private readonly long _size;
        private readonly long _modified;
        private readonly bool _descending;
        private readonly SearchSort _sort;

        internal Cursor(string name, byte[] folded, string volumeId, long fileId,
            long size, long modified, byte rank, bool descending, SearchSort sort)
        {
            _name = name;
            _folded = folded;
            _volumeId = volumeId;
            _fileId = fileId;
            _size = size;
            _modified = modified;
            _rank = rank;
            _descending = descending;
            _sort = sort;
        }

        /// <summary>Positive when the candidate sorts after the cursor.</summary>
        internal int Compare(VolumeIndex volume, int index, byte rank)
        {
            int result = rank.CompareTo(_rank);
            if (result != 0) return result;

            if (_sort == SearchSort.Size)
            {
                long size = volume.GetSize(index);
                bool unknown = size < 0, cursorUnknown = _size < 0;
                if (unknown != cursorUnknown) return unknown ? 1 : -1;
                if (!unknown)
                {
                    result = size.CompareTo(_size);
                    if (result != 0) return _descending ? result : -result;
                }
            }
            else if (_sort == SearchSort.Time)
            {
                long modified = volume.GetModified(index);
                bool unknown = modified == 0, cursorUnknown = _modified == 0;
                if (unknown != cursorUnknown) return unknown ? 1 : -1;
                if (!unknown)
                {
                    result = modified.CompareTo(_modified);
                    if (result != 0) return _descending ? result : -result;
                }
            }
            else
            {
                result = volume.GetFold(index).SequenceCompareTo(_folded);
                if (result != 0) return _descending ? -result : result;
            }

            // 主键相同时按名称、卷、文件号继续，和 CompareEntries 完全一致，分页才稳定。
            result = volume.GetFold(index).SequenceCompareTo(_folded);
            if (result != 0) return result;
            result = string.CompareOrdinal(volume.VolumeId, _volumeId);
            if (result != 0) return result;
            result = volume.GetFileId(index).CompareTo(_fileId);
            if (result != 0) return result;
            result = volume.GetName(index).SequenceCompareTo(_name);
            return result;
        }
    }
}

/// <summary>
/// One volume's index: file names in flat pools plus one 40-byte record per hard-link name.
/// Entries are append-only; deletions are tombstoned and compacted away in bulk so a USN
/// burst never has to shift megabytes of memory.
/// </summary>
internal sealed class VolumeIndex
{
    /// <summary>Compact record. 56 bytes: two ids, two name slices, a hard-link link and metadata.</summary>
    private struct Entry
    {
        public long FileId;
        public long ParentId;
        public int NameOffset;
        public int NameLength;
        public int FoldOffset;
        public int FoldLength;
        public int NextSibling;
        public long Size;            // -1 = 元数据还没补齐
        public long ModifiedTicks;   // 0 = 元数据还没补齐
        public bool IsDirectory;
        public bool Deleted;
    }

    private const int CompactThreshold = 65536;

    private Entry[] _entries;
    private int _count;
    private int _deleted;
    private char[] _names;
    private int _nameUsed;
    private byte[] _foldedNames;
    private int _foldedUsed;
    private LongIntMap _heads;   // 见 LongIntMap：文件引用号 → 最近一条记录下标

    internal string VolumeId { get; }
    internal string RootPath { get; }
    internal string State { get; set; }

    internal VolumeIndex(string volumeId, string rootPath, string state, int capacity, long characters)
    {
        VolumeId = volumeId;
        RootPath = rootPath;
        State = state;
        capacity = Math.Max(capacity, 4);
        _entries = new Entry[capacity];
        _heads = new LongIntMap(capacity);
        int nameCapacity = (int)Math.Clamp(characters + characters / 8, 256, int.MaxValue - 64);
        _names = new char[nameCapacity];
        // UTF-8 of a mostly-ASCII name is about as long as its UTF-16 form, so this skips the
        // repeated doubling (and the temporary garbage) that a 1 MB seed would cause.
        _foldedNames = new byte[Math.Clamp(characters, 256, int.MaxValue - 64)];
    }

    internal static int CompareEntries(VolumeIndex left, int leftIndex, byte leftRank,
        VolumeIndex right, int rightIndex, byte rightRank, SearchSort sort, bool descending)
    {
        // Exact matches stay first in both directions.
        int result = leftRank.CompareTo(rightRank);
        if (result != 0) return result;

        if (sort == SearchSort.Size)
        {
            long leftSize = left.GetSize(leftIndex);
            long rightSize = right.GetSize(rightIndex);
            bool leftUnknown = leftSize < 0, rightUnknown = rightSize < 0;
            if (leftUnknown != rightUnknown) return leftUnknown ? 1 : -1;   // 未知元数据排最后
            if (!leftUnknown)
            {
                result = leftSize.CompareTo(rightSize);
                if (result != 0) return descending ? result : -result;     // 默认大的在前
            }
        }
        else if (sort == SearchSort.Time)
        {
            long leftModified = left.GetModified(leftIndex);
            long rightModified = right.GetModified(rightIndex);
            bool leftUnknown = leftModified == 0, rightUnknown = rightModified == 0;
            if (leftUnknown != rightUnknown) return leftUnknown ? 1 : -1;
            if (!leftUnknown)
            {
                result = leftModified.CompareTo(rightModified);
                if (result != 0) return descending ? result : -result;     // 默认新的在前
            }
        }
        else
        {
            result = left.GetFold(leftIndex).SequenceCompareTo(right.GetFold(rightIndex));
            if (result != 0) return descending ? -result : result;
        }

        // 平局：名称升序、卷、文件号，保证分页稳定。
        result = left.GetFold(leftIndex).SequenceCompareTo(right.GetFold(rightIndex));
        if (result != 0) return result;
        result = string.CompareOrdinal(left.VolumeId, right.VolumeId);
        if (result != 0) return result;
        result = left.GetFileId(leftIndex).CompareTo(right.GetFileId(rightIndex));
        if (result != 0) return result;
        return left.GetName(leftIndex).SequenceCompareTo(right.GetName(rightIndex));
    }

    /// <summary>Adds or refreshes one name of a file. A second name for the same file id is a hard link.</summary>
    internal int Upsert(long fileId, long parentId, string name, bool isDirectory)
    {
        if (_heads.TryGetValue(fileId, out int head))
        {
            for (int entry = head; entry >= 0; entry = _entries[entry].NextSibling)
            {
                if (_entries[entry].Deleted || _entries[entry].NameLength != name.Length) continue;
                if (!GetName(entry).SequenceEqual(name)) continue;
                _entries[entry].ParentId = parentId;
                _entries[entry].IsDirectory = isDirectory;
                return entry;
            }
        }
        return Append(fileId, parentId, name, isDirectory);
    }

    internal void Delete(long fileId, string name)
    {
        if (!_heads.TryGetValue(fileId, out int entry)) return;
        for (; entry >= 0; entry = _entries[entry].NextSibling)
        {
            if (_entries[entry].Deleted || _entries[entry].NameLength != name.Length) continue;
            if (!GetName(entry).SequenceEqual(name)) continue;
            _entries[entry].Deleted = true;
            _deleted++;
            return;
        }
    }

    private int Append(long fileId, long parentId, string name, bool isDirectory)
    {
        if (_count == _entries.Length) Array.Resize(ref _entries, _entries.Length * 2);
        EnsureNameCapacity(name.Length);
        int nameOffset = _nameUsed;
        name.CopyTo(0, _names, _nameUsed, name.Length);
        _nameUsed += name.Length;

        int foldLength = Encoding.UTF8.GetByteCount(name);
        EnsureFoldedCapacity(foldLength);
        int written = SearchText.FoldAsciiLower(name, _foldedNames.AsSpan(_foldedUsed, foldLength));
        int foldOffset = _foldedUsed;
        _foldedUsed += written;

        int index = _count++;
        _entries[index] = new Entry
        {
            FileId = fileId,
            ParentId = parentId,
            NameOffset = nameOffset,
            NameLength = name.Length,
            FoldOffset = foldOffset,
            FoldLength = written,
            NextSibling = _heads.TryGetValue(fileId, out int head) ? head : -1,
            Size = -1,
            ModifiedTicks = 0,
            IsDirectory = isDirectory
        };
        _heads.Set(fileId, index);
        return index;
    }

    private void EnsureNameCapacity(int additional)
    {
        if (_nameUsed + additional <= _names.Length) return;
        int size = Math.Max(_names.Length * 2, _nameUsed + additional);
        Array.Resize(ref _names, size);
    }

    private void EnsureFoldedCapacity(int additional)
    {
        if (_foldedUsed + additional <= _foldedNames.Length) return;
        int size = Math.Max(_foldedNames.Length * 2, _foldedUsed + additional);
        Array.Resize(ref _foldedNames, size);
    }

    /// <summary>Scans this volume in append order and offers every live match to the page buffer.</summary>
    internal void Collect(ReadOnlySpan<byte> needle, ReadOnlySpan<byte> exactPrefix, byte[][] extensions,
        in SearchCatalog.Cursor cursor, bool hasCursor, SearchCatalog.MatchBuffer matches)
    {
        Entry[] entries = _entries;
        ReadOnlySpan<byte> pool = _foldedNames.AsSpan(0, _foldedUsed);
        for (int i = 0; i < _count; i++)
        {
            ref Entry entry = ref entries[i];
            if (entry.Deleted) continue;
            // "Show everything" lists files only: folders (and the volume's "." record) are not
            // what this panel offers for opening or adding to a category.
            if (needle.Length == 0 && entry.IsDirectory) continue;
            ReadOnlySpan<byte> folded = pool.Slice(entry.FoldOffset, entry.FoldLength);
            if (folded.IndexOf(needle) < 0) continue;
            // The kind filters only ever select files, matching the old SQL predicate.
            if (extensions.Length != 0 && (entry.IsDirectory || !EndsWithAny(folded, extensions))) continue;
            byte rank = needle.Length == 0 || SearchText.IsExactMatch(folded, needle, exactPrefix) ? (byte)0 : (byte)1;
            if (hasCursor && cursor.Compare(this, i, rank) <= 0) continue;
            matches.Add(new SearchCatalog.Match(this, i, rank));
        }
    }

    private static bool EndsWithAny(ReadOnlySpan<byte> value, byte[][] extensions)
    {
        foreach (byte[] extension in extensions)
            if (value.EndsWith(extension)) return true;
        return false;
    }

    internal SearchHit CreateHit(int index)
    {
        ref Entry entry = ref _entries[index];
        string name = GetName(index).ToString();
        return new SearchHit(VolumeId, entry.FileId, entry.ParentId, name, BuildPath(index, name),
            entry.IsDirectory, entry.Size, entry.ModifiedTicks);
    }

    /// <summary>Rebuilds the full path by walking parent references; nothing is stored per entry.</summary>
    private string BuildPath(int index, string name)
    {
        var segments = new List<string>(8) { name };
        long parentId = _entries[index].ParentId;
        for (int depth = 0; depth < 512; depth++)
        {
            if (!_heads.TryGetValue(parentId, out int parent)) break;
            ref Entry entry = ref _entries[parent];
            if (entry.Deleted || !entry.IsDirectory) break;
            ReadOnlySpan<char> parentName = GetName(parent);
            if (parentId == 5 || parentName.SequenceEqual(".")) break;
            segments.Add(parentName.ToString());
            parentId = entry.ParentId;
        }
        segments.Reverse();
        return Path.Combine(new[] { RootPath }.Concat(segments).ToArray());
    }

    internal void MaybeCompact()
    {
        if (_deleted < CompactThreshold || _deleted * 4 < _count) return;
        Compact();
    }

    private void Compact()
    {
        int live = _count - _deleted;
        var entries = new Entry[Math.Max(4, live)];
        var names = new char[Math.Max(256, _nameUsed)];
        var folded = new byte[Math.Max(256, _foldedUsed)];
        var heads = new LongIntMap(live);
        int index = 0, nameOffset = 0, foldOffset = 0;
        for (int i = 0; i < _count; i++)
        {
            ref Entry source = ref _entries[i];
            if (source.Deleted) continue;
            _names.AsSpan(source.NameOffset, source.NameLength).CopyTo(names.AsSpan(nameOffset));
            _foldedNames.AsSpan(source.FoldOffset, source.FoldLength).CopyTo(folded.AsSpan(foldOffset));
            entries[index] = new Entry
            {
                FileId = source.FileId,
                ParentId = source.ParentId,
                NameOffset = nameOffset,
                NameLength = source.NameLength,
                FoldOffset = foldOffset,
                FoldLength = source.FoldLength,
                NextSibling = heads.TryGetValue(source.FileId, out int head) ? head : -1,
                Size = source.Size,
                ModifiedTicks = source.ModifiedTicks,
                IsDirectory = source.IsDirectory
            };
            heads.Set(source.FileId, index);
            nameOffset += source.NameLength;
            foldOffset += source.FoldLength;
            index++;
        }
        _entries = entries;
        _count = index;
        _deleted = 0;
        _names = names;
        _nameUsed = nameOffset;
        _foldedNames = folded;
        _foldedUsed = foldOffset;
        _heads = heads;
    }

    internal ReadOnlySpan<char> GetName(int index) =>
        _names.AsSpan(_entries[index].NameOffset, _entries[index].NameLength);

    internal ReadOnlySpan<byte> GetFold(int index) =>
        _foldedNames.AsSpan(_entries[index].FoldOffset, _entries[index].FoldLength);

    internal long GetFileId(int index) => _entries[index].FileId;

    internal long GetSize(int index) => _entries[index].Size;

    internal long GetModified(int index) => _entries[index].ModifiedTicks;

    internal int Count => _count;

    /// <summary>元数据（大小/时间）还没补齐、且仍然有效的记录。</summary>
    internal bool IsMetadataUnknown(int index) =>
        !_entries[index].Deleted && _entries[index].ModifiedTicks == 0;

    internal void SetMetadata(int index, long size, long ticks)
    {
        _entries[index].Size = size;
        _entries[index].ModifiedTicks = ticks;
    }

    /// <summary>后台补齐线程回调：只有记录还是同一行、且仍未知时才写入。</summary>
    internal bool TrySetMetadata(int index, long fileId, ReadOnlySpan<char> name, long size, long ticks)
    {
        if ((uint)index >= (uint)_count) return false;
        ref Entry entry = ref _entries[index];
        if (entry.Deleted || entry.ModifiedTicks != 0 || entry.FileId != fileId) return false;
        if (!GetName(index).SequenceEqual(name)) return false;
        entry.Size = size;
        entry.ModifiedTicks = ticks;
        return true;
    }

    internal string GetFullPath(int index) => BuildPath(index, GetName(index).ToString());
}

/// <summary>Folding shared by indexing and querying: ASCII lower-cased UTF-8, like SQLite's NOCASE.</summary>
internal static class SearchText
{
    internal static byte[] Fold(string value)
    {
        byte[] buffer = new byte[value.Length * 3 + 4];
        int length = FoldAsciiLower(value, buffer);
        return buffer.AsSpan(0, length).ToArray();
    }

    internal static bool IsExactMatch(ReadOnlySpan<byte> folded, ReadOnlySpan<byte> needle, ReadOnlySpan<byte> exactPrefix) =>
        folded.SequenceEqual(needle) || folded.StartsWith(exactPrefix);

    internal static int FoldAsciiLower(ReadOnlySpan<char> source, Span<byte> destination)
    {
        int written = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char value = source[i];
            if (value < '\u0080')
            {
                destination[written++] = (byte)(value is >= 'A' and <= 'Z' ? value + 32 : value);
            }
            else if (value < '\u0800')
            {
                destination[written++] = (byte)(0xC0 | (value >> 6));
                destination[written++] = (byte)(0x80 | (value & 0x3F));
            }
            else if (char.IsHighSurrogate(value) && i + 1 < source.Length && char.IsLowSurrogate(source[i + 1]))
            {
                int codePoint = char.ConvertToUtf32(value, source[++i]);
                destination[written++] = (byte)(0xF0 | (codePoint >> 18));
                destination[written++] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
                destination[written++] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                destination[written++] = (byte)(0x80 | (codePoint & 0x3F));
            }
            else
            {
                destination[written++] = (byte)(0xE0 | (value >> 12));
                destination[written++] = (byte)(0x80 | ((value >> 6) & 0x3F));
                destination[written++] = (byte)(0x80 | (value & 0x3F));
            }
        }
        return written;
    }
}
