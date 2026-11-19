using Microsoft.Data.Sqlite;

namespace BlockBox.SearchService;

/// <summary>
/// Durable per-volume index. Connections are short lived so service restarts release the DB cleanly.
/// The SQLite snapshot is only the crash-safe copy: startup loads it into <see cref="SearchCatalog"/>
/// and every query is answered from memory, never from SQL.
/// </summary>
internal sealed class SearchIndexStore
{
    private readonly string _databasePath;
    private readonly SearchCatalog _catalog = new();

    internal SearchIndexStore(string databasePath)
    {
        _databasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using (var connection = Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA journal_size_limit=67108864;
                PRAGMA busy_timeout=15000;
                CREATE TABLE IF NOT EXISTS Entries(
                    VolumeId TEXT NOT NULL,
                    FileId INTEGER NOT NULL,
                    ParentId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    IsDirectory INTEGER NOT NULL,
                    PRIMARY KEY(VolumeId, FileId, Name));
                CREATE TABLE IF NOT EXISTS Volumes(
                    VolumeId TEXT PRIMARY KEY,
                    RootPath TEXT NOT NULL,
                    JournalId INTEGER NOT NULL DEFAULT 0,
                    NextUsn INTEGER NOT NULL DEFAULT 0,
                    State TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    ScanCursor INTEGER NOT NULL DEFAULT 0);
                """;
            command.ExecuteNonQuery();
            EnsureColumn(connection, "Volumes", "ScanCursor", "INTEGER NOT NULL DEFAULT 0");
            DropLegacyFullTextIndex(connection);
        }
        LoadCatalog();
    }

    /// <summary>
    /// SQL-side substring matching (FTS5 trigram) is dead weight now that queries scan the
    /// in-memory catalog: the table roughly doubled database size and every insert fired three
    /// triggers. Drop all of it once; older service builds simply recreate it.
    /// </summary>
    private static void DropLegacyFullTextIndex(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TRIGGER IF EXISTS TR_Entries_Fts_Insert;
            DROP TRIGGER IF EXISTS TR_Entries_Fts_Delete;
            DROP TRIGGER IF EXISTS TR_Entries_Fts_Update;
            DROP TABLE IF EXISTS EntryNameFts;
            DROP INDEX IF EXISTS IX_Entries_Name;
            DROP INDEX IF EXISTS IX_Entries_Parent;
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Loads every volume that can answer queries (ready or mid-scan/resume) into memory.</summary>
    private void LoadCatalog()
    {
        using var connection = Open();
        var volumes = new List<(string VolumeId, string RootPath, string State)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT VolumeId,RootPath,State FROM Volumes";
            using var reader = command.ExecuteReader();
            while (reader.Read()) volumes.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        foreach (var volume in volumes)
        {
            // "Indexing" volumes are loaded too: an interrupted scan resumes from its cursor and
            // only appends the remaining records, so the first part would otherwise be missing.
            if (volume.State is not ("Ready" or "Indexing")) continue;
            var (count, characters) = CountEntries(connection, volume.VolumeId);
            _catalog.LoadVolume(volume.VolumeId, volume.RootPath, volume.State, count, characters,
                ReadEntries(connection, volume.VolumeId));
        }
    }

    private static (int Count, long Characters) CountEntries(SqliteConnection connection, string volumeId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*),COALESCE(SUM(LENGTH(Name)),0) FROM Entries WHERE VolumeId=$id";
        command.Parameters.AddWithValue("$id", volumeId);
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    private static IEnumerable<(long FileId, long ParentId, string Name, bool IsDirectory)> ReadEntries(
        SqliteConnection connection, string volumeId)
    {
        using var command = connection.CreateCommand();
        // No ORDER BY: the primary key already covers the lookup, and forcing rowid order would
        // make SQLite sort a million rows into a temporary b-tree before the first one loads.
        command.CommandText = "SELECT FileId,ParentId,Name,IsDirectory FROM Entries WHERE VolumeId=$id";
        command.Parameters.AddWithValue("$id", volumeId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            yield return (reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt64(3) != 0);
    }

    /// <summary>Starts a full rebuild: drops the volume's rows and stores the USN baseline the scan is based on.</summary>
    internal void BeginVolume(string volumeId, string rootPath, ulong journalId, long baselineUsn)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Volumes(VolumeId,RootPath,JournalId,NextUsn,State,UpdatedUtc,ScanCursor)
            VALUES($id,$root,$journal,$baseline,'Indexing',datetime('now'),0)
            ON CONFLICT(VolumeId) DO UPDATE SET RootPath=excluded.RootPath,JournalId=excluded.JournalId,
                NextUsn=excluded.NextUsn,State='Indexing',ScanCursor=0,UpdatedUtc=datetime('now');
            DELETE FROM Entries WHERE VolumeId=$id;
            """;
        command.Parameters.AddWithValue("$id", volumeId);
        command.Parameters.AddWithValue("$root", rootPath);
        command.Parameters.AddWithValue("$journal", unchecked((long)journalId));
        command.Parameters.AddWithValue("$baseline", baselineUsn);
        command.ExecuteNonQuery();
        _catalog.BeginVolume(volumeId, rootPath);
    }

    /// <summary>Persists how far the initial MFT scan got so a restart can continue instead of rescanning.</summary>
    internal void UpdateScanCursor(string volumeId, long scanCursor)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Volumes SET ScanCursor=$cursor,UpdatedUtc=datetime('now') WHERE VolumeId=$id";
        command.Parameters.AddWithValue("$cursor", scanCursor);
        command.Parameters.AddWithValue("$id", volumeId);
        command.ExecuteNonQuery();
    }

    internal VolumeCheckpoint? GetCheckpoint(string volumeId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT JournalId,NextUsn,State,ScanCursor FROM Volumes WHERE VolumeId=$id";
        command.Parameters.AddWithValue("$id", volumeId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new VolumeCheckpoint(unchecked((ulong)reader.GetInt64(0)), reader.GetInt64(1), reader.GetString(2),
                reader.GetInt64(3))
            : null;
    }

    internal (string State, int ReadyVolumes, int TotalVolumes) GetStatus()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(CASE WHEN State='Ready' THEN 1 ELSE 0 END),0),COUNT(*) FROM Volumes";
        using var reader = command.ExecuteReader();
        reader.Read();
        int ready = reader.GetInt32(0), total = reader.GetInt32(1);
        return (ready > 0 ? "ready" : total > 0 ? "indexing" : "unavailable", ready, total);
    }

    internal void AddBatch(string volumeId, IReadOnlyCollection<MftEntry> entries)
    {
        if (entries.Count == 0) return;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Entries(VolumeId,FileId,ParentId,Name,IsDirectory) VALUES($volume,$file,$parent,$name,$dir)
            ON CONFLICT(VolumeId,FileId,Name) DO UPDATE SET ParentId=excluded.ParentId,IsDirectory=excluded.IsDirectory;
            """;
        var volume = command.Parameters.Add("$volume", SqliteType.Text);
        var file = command.Parameters.Add("$file", SqliteType.Integer);
        var parent = command.Parameters.Add("$parent", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var directory = command.Parameters.Add("$dir", SqliteType.Integer);
        foreach (var entry in entries)
        {
            volume.Value = volumeId;
            file.Value = entry.FileReference;
            parent.Value = entry.ParentReference;
            name.Value = entry.Name;
            directory.Value = entry.IsDirectory ? 1 : 0;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        _catalog.AddBatch(volumeId, entries);
    }

    internal void CompleteVolume(string volumeId, long journalId, long nextUsn)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Volumes SET JournalId=$journal,NextUsn=$usn,State='Ready',ScanCursor=0,UpdatedUtc=datetime('now') WHERE VolumeId=$id";
        command.Parameters.AddWithValue("$journal", journalId);
        command.Parameters.AddWithValue("$usn", nextUsn);
        command.Parameters.AddWithValue("$id", volumeId);
        command.ExecuteNonQuery();
        TryTruncateWal(connection);
        _catalog.SetState(volumeId, "Ready");
    }

    /// <summary>
    /// Keeps the WAL file bounded. Without a size limit SQLite leaves the WAL at its high-water
    /// mark (hundreds of MB after a large initial scan) even after every frame has been checkpointed.
    /// </summary>
    private static void TryTruncateWal(SqliteConnection connection)
    {
        try
        {
            using var checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            checkpoint.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // A reader is still active; the size limit keeps the WAL bounded until the next pass.
        }
    }

    internal void ApplyJournalBatch(string volumeId, ulong journalId, JournalReadResult batch, bool markReady)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO Entries(VolumeId,FileId,ParentId,Name,IsDirectory) VALUES($volume,$file,$parent,$name,$dir)
            ON CONFLICT(VolumeId,FileId,Name) DO UPDATE SET ParentId=excluded.ParentId,IsDirectory=excluded.IsDirectory;
            """;
        var volume = upsert.Parameters.Add("$volume", SqliteType.Text);
        var file = upsert.Parameters.Add("$file", SqliteType.Integer);
        var parent = upsert.Parameters.Add("$parent", SqliteType.Integer);
        var name = upsert.Parameters.Add("$name", SqliteType.Text);
        var directory = upsert.Parameters.Add("$dir", SqliteType.Integer);
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM Entries WHERE VolumeId=$volume AND FileId=$file AND Name=$name";
        delete.Parameters.Add("$volume", SqliteType.Text).Value = volumeId;
        var deleteFile = delete.Parameters.Add("$file", SqliteType.Integer);
        var deleteName = delete.Parameters.Add("$name", SqliteType.Text);
        foreach (var entry in batch.Entries)
        {
            // FILE_DELETE and RENAME_OLD_NAME invalidate exactly this hard-link name.
            if ((entry.Reason & (0x00000200u | 0x00001000u)) != 0)
            {
                deleteFile.Value = entry.FileReference;
                deleteName.Value = entry.Name;
                delete.ExecuteNonQuery();
            }
            if ((entry.Reason & (0x00000100u | 0x00002000u | 0x00008000u)) != 0)
            {
                volume.Value = volumeId;
                file.Value = entry.FileReference;
                parent.Value = entry.ParentReference;
                name.Value = entry.Name;
                directory.Value = entry.IsDirectory ? 1 : 0;
                upsert.ExecuteNonQuery();
            }
        }
        using var state = connection.CreateCommand();
        state.Transaction = transaction;
        state.CommandText = "UPDATE Volumes SET JournalId=$journal,NextUsn=$usn,State=$state,UpdatedUtc=datetime('now') WHERE VolumeId=$id";
        state.Parameters.AddWithValue("$journal", unchecked((long)journalId));
        state.Parameters.AddWithValue("$usn", batch.NextUsn);
        state.Parameters.AddWithValue("$state", markReady ? "Ready" : "Indexing");
        state.Parameters.AddWithValue("$id", volumeId);
        state.ExecuteNonQuery();
        transaction.Commit();
        _catalog.ApplyJournal(volumeId, batch);
        if (markReady) _catalog.SetState(volumeId, "Ready");
    }

    internal void SetVolumeState(string volumeId, string state)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Volumes SET State=$state,UpdatedUtc=datetime('now') WHERE VolumeId=$id";
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$id", volumeId);
        command.ExecuteNonQuery();
        _catalog.SetState(volumeId, state);
    }

    /// <summary>Answered entirely from the in-memory catalog; the database is never touched here.</summary>
    internal IReadOnlyList<SearchHit> SearchNames(string query, string kind, int limit,
        string? afterName = null, string? afterVolumeId = null, long? afterFileId = null,
        bool descending = false) =>
        _catalog.Search(query, kind, limit, afterName, afterVolumeId, afterFileId, descending);

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(" + table + ")";
            using var reader = info.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return;
        }
        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE " + table + " ADD COLUMN " + column + " " + definition;
        alter.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        // Shared cache made every writer serialize behind readers and kept WAL checkpoints from
        // completing; the default (private) cache with a busy timeout is both faster and safer.
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 30
        }.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=15000; PRAGMA synchronous=NORMAL; PRAGMA journal_size_limit=67108864;";
        pragma.ExecuteNonQuery();
        return connection;
    }
}

internal sealed record VolumeCheckpoint(ulong JournalId, long NextUsn, string State, long ScanCursor);
