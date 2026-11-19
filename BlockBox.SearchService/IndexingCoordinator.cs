using System.Runtime.InteropServices;

namespace BlockBox.SearchService;

/// <summary>Coordinates bounded MFT batches and publishes a volume only after its initial scan commits.</summary>
internal sealed class IndexingCoordinator(SearchIndexStore store)
{
    private const int BatchSize = 2000;
    private const int MaxAttempts = 3;
    private const long FileReferenceMask = 0x0000FFFFFFFFFFFF;
    private readonly SearchIndexStore _store = store;
    private readonly CancellationTokenSource _shutdown = new();

    internal async Task IndexFixedNtfsVolumesAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed
            && string.Equals(d.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)).ToArray();
        ServiceLog.Write($"Fixed NTFS volumes: {string.Join(", ", drives.Select(d => d.RootDirectory.FullName))}");
        // IndexVolumeAsync performs synchronous MFT enumeration before its first await.
        // Schedule each volume separately so one large drive cannot delay discovery and
        // initial indexing of every other eligible volume.
        await Task.WhenAll(drives.Select(drive =>
            Task.Run(() => IndexVolumeWithRetriesAsync(drive, linked.Token), linked.Token)));
    }

    /// <summary>
    /// One failing volume must never stop the service or the other volumes: failures are logged,
    /// retried with a short backoff, and finally reported through the volume state.
    /// </summary>
    private async Task IndexVolumeWithRetriesAsync(DriveInfo drive, CancellationToken cancellationToken)
    {
        string root = drive.RootDirectory.FullName;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await IndexVolumeAsync(drive, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                ServiceLog.Write($"Volume {root} indexing failed (attempt {attempt}/{MaxAttempts})", ex);
                if (attempt >= MaxAttempts)
                {
                    try { _store.SetVolumeState(GetVolumeGuid(root), "NeedsRebuild"); }
                    catch (Exception stateError) { ServiceLog.Write($"Volume {root}: cannot record failure state", stateError); }
                    return;
                }
                try { await Task.Delay(TimeSpan.FromSeconds(5 * attempt), cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task IndexVolumeAsync(DriveInfo drive, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await RunVolumeAsync(drive, cancellationToken)) return;
            ServiceLog.Write($"Volume {drive.RootDirectory.FullName}: USN journal was reset, rebuilding the index.");
        }
    }

    /// <returns><c>true</c> when the journal was reset and the volume has to be rebuilt from scratch.</returns>
    private async Task<bool> RunVolumeAsync(DriveInfo drive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = drive.RootDirectory.FullName;
        string volumeId = GetVolumeGuid(root);
        using var journal = new UsnJournalReader(root);
        JournalState baseline = journal.Query();
        VolumeCheckpoint? checkpoint = _store.GetCheckpoint(volumeId);

        if (checkpoint is { State: "Ready" } && checkpoint.JournalId == baseline.JournalId
            && checkpoint.NextUsn >= baseline.FirstUsn)
        {
            long resumeCursor = checkpoint.NextUsn;
            try
            {
                ReplayUntilCurrent(journal, volumeId, baseline, ref resumeCursor, cancellationToken);
                return await WatchJournalAsync(journal, volumeId, baseline.JournalId, resumeCursor, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                _store.SetVolumeState(volumeId, "NeedsRebuild");
                throw;
            }
        }

        long replayCursor;
        long scanCursor;
        if (checkpoint is { State: "Indexing", ScanCursor: > 0 } && checkpoint.JournalId == baseline.JournalId
            && checkpoint.NextUsn >= baseline.FirstUsn)
        {
            // A previous run was interrupted while enumerating the MFT. Continue from the last
            // committed record instead of deleting everything and starting the whole scan again.
            scanCursor = checkpoint.ScanCursor;
            replayCursor = checkpoint.NextUsn;
            ServiceLog.Write($"Volume {root}: resuming the interrupted scan at file reference {scanCursor}.");
        }
        else
        {
            _store.BeginVolume(volumeId, root, baseline.JournalId, baseline.NextUsn);
            scanCursor = 0;
            replayCursor = baseline.NextUsn;
            ServiceLog.Write($"Volume {root}: starting a full scan.");
        }

        // If anything below fails the volume stays in "Indexing", so the retry (or the next
        // service start) resumes the scan from ScanCursor instead of deleting everything.
        var batch = new List<MftEntry>(BatchSize);
        long persistedCursor = scanCursor;
        foreach (var entry in NtfsMftEnumerator.Enumerate(root, cancellationToken, scanCursor))
        {
            batch.Add(entry);
            persistedCursor = Math.Max(persistedCursor, entry.FileReference & FileReferenceMask);
            if (batch.Count < BatchSize) continue;
            _store.AddBatch(volumeId, batch);
            _store.UpdateScanCursor(volumeId, persistedCursor);
            batch.Clear();
        }
        if (batch.Count > 0)
        {
            _store.AddBatch(volumeId, batch);
            _store.UpdateScanCursor(volumeId, persistedCursor);
        }

        JournalState current = journal.Query();
        if (current.JournalId != baseline.JournalId || baseline.NextUsn < current.FirstUsn)
            throw new InvalidOperationException("USN journal changed or rolled over during the initial MFT scan; a full rebuild is required.");

        ReplayUntilCurrent(journal, volumeId, current, ref replayCursor, cancellationToken);
        return await WatchJournalAsync(journal, volumeId, baseline.JournalId, replayCursor, cancellationToken);
    }

    private void ReplayUntilCurrent(UsnJournalReader journal, string volumeId, JournalState current,
        ref long cursor, CancellationToken cancellationToken)
    {
        if (cursor < current.FirstUsn) throw new InvalidOperationException("USN journal rolled over; a full rebuild is required.");
        while (cursor < current.NextUsn)
        {
            var changes = journal.Read(cursor, current.JournalId, waitForChanges: false, cancellationToken);
            if (changes.NextUsn <= cursor) break;
            _store.ApplyJournalBatch(volumeId, current.JournalId, changes, markReady: false);
            cursor = changes.NextUsn;
        }
        if (cursor < current.NextUsn) throw new InvalidOperationException("USN journal replay stopped before reaching the current cursor.");
        _store.CompleteVolume(volumeId, unchecked((long)current.JournalId), cursor);
    }

    private async Task<bool> WatchJournalAsync(UsnJournalReader journal, string volumeId, ulong journalId,
        long cursor, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            JournalState live = journal.Query();
            if (live.JournalId != journalId || cursor < live.FirstUsn)
            {
                _store.SetVolumeState(volumeId, "NeedsRebuild");
                return true;
            }
            var changes = journal.Read(cursor, journalId, waitForChanges: true, cancellationToken);
            if (changes.NextUsn > cursor)
            {
                _store.ApplyJournalBatch(volumeId, journalId, changes, markReady: true);
                cursor = changes.NextUsn;
            }
            else await Task.Delay(250, cancellationToken);
        }
        return false;
    }

    internal void Stop() => _shutdown.Cancel();

    private static string GetVolumeGuid(string root)
    {
        var buffer = new char[1024];
        if (!GetVolumeNameForVolumeMountPointW(root, buffer, (uint)buffer.Length))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"Cannot resolve volume GUID for {root}");
        return new string(buffer).TrimEnd('\0').TrimEnd('\\');
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, [Out] char[] volumeName, uint bufferLength);
}
