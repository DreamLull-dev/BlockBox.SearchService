using BlockBox.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace BlockBox.Data
{
    public static class RecycleBinRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static bool MoveToRecycleBin(string id)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var transaction = conn.BeginTransaction();

            ShortcutData? shortcut = ReadShortcut(conn, transaction, id, isShared: false);
            List<ModeStateData> modeStates = new();
            if (shortcut == null)
            {
                shortcut = ReadShortcut(conn, transaction, id, isShared: true);
                if (shortcut == null)
                    return false;
                modeStates = ReadModeStates(conn, transaction, id);
            }

            var payload = new RecyclePayload { Shortcut = shortcut, ModeStates = modeStates };
            using (var insert = conn.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = @"INSERT INTO RecycleBin
                    (Id,Payload,DeletedAt,Name,TargetPath,BackupPath,CategoryId,BelongingMode,IsShared)
                    VALUES(@id,@payload,@deleted,@name,@path,@backup,@category,@mode,@shared)";
                insert.Parameters.AddWithValue("@id", shortcut.Id);
                insert.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(payload, JsonOptions));
                insert.Parameters.AddWithValue("@deleted", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("@name", shortcut.Name);
                insert.Parameters.AddWithValue("@path", shortcut.TargetPath);
                insert.Parameters.AddWithValue("@backup", shortcut.BackupPath);
                insert.Parameters.AddWithValue("@category", shortcut.CategoryId);
                insert.Parameters.AddWithValue("@mode", (int)shortcut.BelongingMode);
                insert.Parameters.AddWithValue("@shared", shortcut.IsShared ? 1 : 0);
                insert.ExecuteNonQuery();
            }

            if (shortcut.IsShared)
            {
                using var deleteState = conn.CreateCommand();
                deleteState.Transaction = transaction;
                deleteState.CommandText = "DELETE FROM SharedShortcutModeState WHERE ShortcutId=@id";
                deleteState.Parameters.AddWithValue("@id", id);
                deleteState.ExecuteNonQuery();

                using var delete = conn.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM SharedShortcuts WHERE Id=@id";
                delete.Parameters.AddWithValue("@id", id);
                delete.ExecuteNonQuery();
            }
            else
            {
                using var delete = conn.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM Shortcuts WHERE Id=@id";
                delete.Parameters.AddWithValue("@id", id);
                delete.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }

        public static int MoveByTargetPath(string targetPath)
        {
            var ids = GetIds("SELECT Id FROM Shortcuts WHERE TargetPath=@value COLLATE NOCASE UNION SELECT Id FROM SharedShortcuts WHERE TargetPath=@value COLLATE NOCASE", targetPath);
            return MoveIds(ids);
        }

        /// <summary>
        /// 只把共享分类（全部应用/办公/音乐/游戏）里的同名记录移入回收站，保留用户自定义分类里的记录。
        /// </summary>
        public static int MoveByTargetPathShared(string targetPath)
        {
            var ids = GetIds("SELECT Id FROM SharedShortcuts WHERE TargetPath=@value COLLATE NOCASE", targetPath);
            return MoveIds(ids);
        }

        /// <summary>
        /// 把同名记录从“默认分类集合”里移入回收站：既处理共享分类表，也处理历史遗留的
        /// Shortcuts 表里挂在默认分类下的记录，但保留自定义分类里的记录。
        /// </summary>
        public static int MoveByTargetPathInDefaultCategories(string targetPath, IReadOnlyList<string> defaultCategoryIds)
        {
            var ids = GetIds("SELECT Id FROM SharedShortcuts WHERE TargetPath=@value COLLATE NOCASE", targetPath);

            if (defaultCategoryIds.Count > 0)
            {
                using var conn = DatabaseContext.OpenConnection();
                using var cmd = conn.CreateCommand();
                var names = new List<string>(defaultCategoryIds.Count);
                for (int i = 0; i < defaultCategoryIds.Count; i++)
                {
                    string name = "@c" + i;
                    names.Add(name);
                    cmd.Parameters.AddWithValue(name, defaultCategoryIds[i]);
                }
                cmd.CommandText = "SELECT Id FROM Shortcuts WHERE TargetPath=@value COLLATE NOCASE AND CategoryId IN (" +
                                  string.Join(",", names) + ")";
                cmd.Parameters.AddWithValue("@value", targetPath);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) ids.Add(reader.GetString(0));
            }

            return MoveIds(ids);
        }

        public static int MoveByCategory(string categoryId)
        {
            var ids = GetIds("SELECT Id FROM Shortcuts WHERE CategoryId=@value UNION SELECT Id FROM SharedShortcuts WHERE CategoryId=@value", categoryId);
            return MoveIds(ids);
        }

        public static List<RecycleBinEntry> GetAll()
        {
            var entries = new List<RecycleBinEntry>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Payload,DeletedAt FROM RecycleBin ORDER BY DeletedAt DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var payload = JsonSerializer.Deserialize<RecyclePayload>(reader.GetString(0), JsonOptions);
                if (payload?.Shortcut == null)
                    continue;
                entries.Add(new RecycleBinEntry
                {
                    Shortcut = payload.Shortcut.ToItem(),
                    DeletedAt = ParseDate(reader.GetString(1))
                });
            }
            return entries;
        }

        public static bool Restore(string id, string? destinationCategoryId = null)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var transaction = conn.BeginTransaction();
            RecyclePayload? payload;
            using (var read = conn.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT Payload FROM RecycleBin WHERE Id=@id";
                read.Parameters.AddWithValue("@id", id);
                var json = read.ExecuteScalar() as string;
                if (string.IsNullOrWhiteSpace(json))
                    return false;
                payload = JsonSerializer.Deserialize<RecyclePayload>(json, JsonOptions);
            }
            if (payload?.Shortcut == null)
                return false;

            var shortcut = payload.Shortcut;
            string categoryId = destinationCategoryId ?? shortcut.CategoryId;
            bool destinationIsShared;
            AppMode destinationMode;
            using (var category = conn.CreateCommand())
            {
                category.Transaction = transaction;
                category.CommandText = "SELECT 1 FROM SharedCategories WHERE Id=@id";
                category.Parameters.AddWithValue("@id", categoryId);
                destinationIsShared = category.ExecuteScalar() != null;
            }
            if (destinationIsShared)
            {
                destinationMode = shortcut.BelongingMode;
            }
            else
            {
                using var category = conn.CreateCommand();
                category.Transaction = transaction;
                category.CommandText = "SELECT BelongingMode FROM Categories WHERE Id=@id";
                category.Parameters.AddWithValue("@id", categoryId);
                var mode = category.ExecuteScalar();
                if (mode == null || mode == DBNull.Value)
                    return false;
                destinationMode = (AppMode)Convert.ToInt32(mode, CultureInfo.InvariantCulture);
            }

            if (destinationIsShared != shortcut.IsShared)
                return false;
            shortcut.CategoryId = categoryId;
            shortcut.BelongingMode = destinationMode;
            InsertShortcut(conn, transaction, shortcut, payload.ModeStates);

            using (var delete = conn.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM RecycleBin WHERE Id=@id";
                delete.Parameters.AddWithValue("@id", id);
                delete.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }

        public static string? PermanentlyDelete(string id)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var transaction = conn.BeginTransaction();
            string? backupPath = null;
            using (var read = conn.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT Payload FROM RecycleBin WHERE Id=@id";
                read.Parameters.AddWithValue("@id", id);
                if (read.ExecuteScalar() is string json)
                    backupPath = JsonSerializer.Deserialize<RecyclePayload>(json, JsonOptions)?.Shortcut?.BackupPath;
            }
            using (var delete = conn.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM RecycleBin WHERE Id=@id";
                delete.Parameters.AddWithValue("@id", id);
                if (delete.ExecuteNonQuery() == 0)
                    return null;
            }
            transaction.Commit();
            return backupPath;
        }

        public static List<string> Empty()
        {
            var backupPaths = new List<string>();
            using var conn = DatabaseContext.OpenConnection();
            using var transaction = conn.BeginTransaction();
            using (var read = conn.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT Payload FROM RecycleBin";
                using var reader = read.ExecuteReader();
                while (reader.Read())
                {
                    if (JsonSerializer.Deserialize<RecyclePayload>(reader.GetString(0), JsonOptions)?.Shortcut.BackupPath is { Length: > 0 } path)
                        backupPaths.Add(path);
                }
            }
            using (var delete = conn.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM RecycleBin";
                delete.ExecuteNonQuery();
            }
            transaction.Commit();
            return backupPaths;
        }

        private static ShortcutData? ReadShortcut(SqliteConnection conn, SqliteTransaction transaction, string id, bool isShared)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = isShared
                ? @"SELECT s.*, @mode AS BelongingMode, COALESCE(st.TitleId,'') AS TitleId,
                    COALESCE(st.IsFavorite,0) AS IsFavorite, st.FavoriteTime, st.LastAccessedTime,
                    COALESCE(st.AccessCount,0) AS AccessCount
                    FROM SharedShortcuts s LEFT JOIN SharedShortcutModeState st
                    ON st.ShortcutId=s.Id AND st.Mode=@mode WHERE s.Id=@id"
                : "SELECT * FROM Shortcuts WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            if (isShared)
                cmd.Parameters.AddWithValue("@mode", (int)AppMode.Normal);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;
            return ShortcutData.FromReader(reader, isShared);
        }

        private static List<ModeStateData> ReadModeStates(SqliteConnection conn, SqliteTransaction transaction, string id)
        {
            var states = new List<ModeStateData>();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"SELECT Mode,SortOrder,TitleId,IsFavorite,FavoriteTime,LastAccessedTime,AccessCount
                FROM SharedShortcutModeState WHERE ShortcutId=@id";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                states.Add(new ModeStateData
                {
                    Mode = reader.GetInt32(0),
                    SortOrder = reader.GetInt32(1),
                    TitleId = reader.GetString(2),
                    IsFavorite = reader.GetInt32(3) != 0,
                    FavoriteTime = ReadDate(reader, 4),
                    LastAccessedTime = ReadDate(reader, 5),
                    AccessCount = reader.GetInt32(6)
                });
            }
            return states;
        }

        private static void InsertShortcut(SqliteConnection conn, SqliteTransaction transaction, ShortcutData s, List<ModeStateData> states)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            if (s.IsShared)
            {
                cmd.CommandText = @"INSERT INTO SharedShortcuts
                    (Id,Name,TargetPath,Arguments,WorkingDirectory,IconPath,CategoryId,IsFile,SortOrder,
                     BackupPath,SourceMd5,SourceState,LastVerifiedTime,IsBackedUp,CreatedTime)
                    VALUES(@id,@name,@target,@args,@wd,@icon,@category,@isFile,@sort,@backup,@md5,@state,@verified,@backed,@created)";
            }
            else
            {
                cmd.CommandText = @"INSERT INTO Shortcuts
                    (Id,Name,TargetPath,Arguments,WorkingDirectory,IconPath,CategoryId,TitleId,IsFile,BelongingMode,
                     SortOrder,IsFavorite,FavoriteTime,LastAccessedTime,AccessCount,BackupPath,SourceMd5,SourceState,
                     LastVerifiedTime,IsBackedUp,CreatedTime)
                    VALUES(@id,@name,@target,@args,@wd,@icon,@category,@title,@isFile,@mode,@sort,@favorite,
                     @favoriteTime,@lastAccessed,@accessCount,@backup,@md5,@state,@verified,@backed,@created)";
                cmd.Parameters.AddWithValue("@title", s.TitleId);
                cmd.Parameters.AddWithValue("@mode", (int)s.BelongingMode);
                cmd.Parameters.AddWithValue("@favorite", s.IsFavorite ? 1 : 0);
                cmd.Parameters.AddWithValue("@favoriteTime", DbDate(s.FavoriteTime));
                cmd.Parameters.AddWithValue("@lastAccessed", DbDate(s.LastAccessedTime));
                cmd.Parameters.AddWithValue("@accessCount", s.AccessCount);
            }
            cmd.Parameters.AddWithValue("@id", s.Id);
            cmd.Parameters.AddWithValue("@name", s.Name);
            cmd.Parameters.AddWithValue("@target", s.TargetPath);
            cmd.Parameters.AddWithValue("@args", s.Arguments);
            cmd.Parameters.AddWithValue("@wd", s.WorkingDirectory);
            cmd.Parameters.AddWithValue("@icon", s.IconPath);
            cmd.Parameters.AddWithValue("@category", s.CategoryId);
            cmd.Parameters.AddWithValue("@isFile", s.IsFile ? 1 : 0);
            cmd.Parameters.AddWithValue("@sort", s.SortOrder);
            cmd.Parameters.AddWithValue("@backup", s.BackupPath);
            cmd.Parameters.AddWithValue("@md5", s.SourceMd5);
            cmd.Parameters.AddWithValue("@state", (int)s.SourceState);
            cmd.Parameters.AddWithValue("@verified", DbDate(s.LastVerifiedTime));
            cmd.Parameters.AddWithValue("@backed", s.IsBackedUp ? 1 : 0);
            cmd.Parameters.AddWithValue("@created", s.CreatedTime.ToString("O", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();

            if (!s.IsShared)
                return;
            foreach (var state in states)
            {
                using var stateCmd = conn.CreateCommand();
                stateCmd.Transaction = transaction;
                stateCmd.CommandText = @"INSERT INTO SharedShortcutModeState
                    (Mode,ShortcutId,SortOrder,TitleId,IsFavorite,FavoriteTime,LastAccessedTime,AccessCount)
                    VALUES(@mode,@id,@sort,@title,@favorite,@favoriteTime,@lastAccessed,@accessCount)";
                stateCmd.Parameters.AddWithValue("@mode", state.Mode);
                stateCmd.Parameters.AddWithValue("@id", s.Id);
                stateCmd.Parameters.AddWithValue("@sort", state.SortOrder);
                stateCmd.Parameters.AddWithValue("@title", state.TitleId);
                stateCmd.Parameters.AddWithValue("@favorite", state.IsFavorite ? 1 : 0);
                stateCmd.Parameters.AddWithValue("@favoriteTime", DbDate(state.FavoriteTime));
                stateCmd.Parameters.AddWithValue("@lastAccessed", DbDate(state.LastAccessedTime));
                stateCmd.Parameters.AddWithValue("@accessCount", state.AccessCount);
                stateCmd.ExecuteNonQuery();
            }
        }

        private static List<string> GetIds(string sql, string value)
        {
            var ids = new List<string>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@value", value);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetString(0));
            return ids;
        }

        private static int MoveIds(IEnumerable<string> ids)
        {
            int count = 0;
            foreach (string id in ids)
                if (MoveToRecycleBin(id)) count++;
            return count;
        }

        private static object DbDate(DateTime? value)
            => value.HasValue ? value.Value.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value;

        private static DateTime? ReadDate(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return null;
            return DateTime.TryParse(reader.GetString(ordinal), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var value) ? value : null;
        }

        private static DateTime ParseDate(string value)
            => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        private sealed class RecyclePayload
        {
            public ShortcutData Shortcut { get; set; } = new();
            public List<ModeStateData> ModeStates { get; set; } = new();
        }

        private sealed class ShortcutData
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string TargetPath { get; set; } = "";
            public string Arguments { get; set; } = "";
            public string WorkingDirectory { get; set; } = "";
            public string IconPath { get; set; } = "";
            public string CategoryId { get; set; } = "";
            public string TitleId { get; set; } = "";
            public bool IsFile { get; set; }
            public AppMode BelongingMode { get; set; }
            public bool IsShared { get; set; }
            public int SortOrder { get; set; }
            public bool IsFavorite { get; set; }
            public DateTime? FavoriteTime { get; set; }
            public DateTime? LastAccessedTime { get; set; }
            public int AccessCount { get; set; }
            public string BackupPath { get; set; } = "";
            public string SourceMd5 { get; set; } = "";
            public SourceState SourceState { get; set; }
            public DateTime? LastVerifiedTime { get; set; }
            public bool IsBackedUp { get; set; }
            public DateTime CreatedTime { get; set; }

            public static ShortcutData FromReader(SqliteDataReader reader, bool isShared)
                => new()
                {
                    Id = reader.GetString(reader.GetOrdinal("Id")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    TargetPath = reader.GetString(reader.GetOrdinal("TargetPath")),
                    Arguments = reader.GetString(reader.GetOrdinal("Arguments")),
                    WorkingDirectory = reader.GetString(reader.GetOrdinal("WorkingDirectory")),
                    IconPath = reader.GetString(reader.GetOrdinal("IconPath")),
                    CategoryId = reader.GetString(reader.GetOrdinal("CategoryId")),
                    TitleId = reader.GetString(reader.GetOrdinal("TitleId")),
                    IsFile = reader.GetInt32(reader.GetOrdinal("IsFile")) != 0,
                    BelongingMode = (AppMode)reader.GetInt32(reader.GetOrdinal("BelongingMode")),
                    IsShared = isShared,
                    SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                    IsFavorite = reader.GetInt32(reader.GetOrdinal("IsFavorite")) != 0,
                    FavoriteTime = ReadDate(reader, reader.GetOrdinal("FavoriteTime")),
                    LastAccessedTime = ReadDate(reader, reader.GetOrdinal("LastAccessedTime")),
                    AccessCount = reader.GetInt32(reader.GetOrdinal("AccessCount")),
                    BackupPath = reader.GetString(reader.GetOrdinal("BackupPath")),
                    SourceMd5 = reader.GetString(reader.GetOrdinal("SourceMd5")),
                    SourceState = (SourceState)reader.GetInt32(reader.GetOrdinal("SourceState")),
                    LastVerifiedTime = ReadDate(reader, reader.GetOrdinal("LastVerifiedTime")),
                    IsBackedUp = reader.GetInt32(reader.GetOrdinal("IsBackedUp")) != 0,
                    CreatedTime = ReadDate(reader, reader.GetOrdinal("CreatedTime")) ?? DateTime.Now
                };

            public ShortcutItem ToItem()
                => new()
                {
                    Id = Id,
                    Name = Name,
                    TargetPath = TargetPath,
                    Arguments = Arguments,
                    WorkingDirectory = WorkingDirectory,
                    IconPath = IconPath,
                    CategoryId = CategoryId,
                    TitleId = TitleId,
                    IsFile = IsFile,
                    BelongingMode = BelongingMode,
                    IsShared = IsShared,
                    SortOrder = SortOrder,
                    IsFavorite = IsFavorite,
                    FavoriteTime = FavoriteTime,
                    LastAccessedTime = LastAccessedTime,
                    AccessCount = AccessCount,
                    BackupPath = BackupPath,
                    SourceMd5 = SourceMd5,
                    SourceState = SourceState,
                    LastVerifiedTime = LastVerifiedTime,
                    IsBackedUp = IsBackedUp,
                    CreatedTime = CreatedTime
                };
        }

        private sealed class ModeStateData
        {
            public int Mode { get; set; }
            public int SortOrder { get; set; }
            public string TitleId { get; set; } = "";
            public bool IsFavorite { get; set; }
            public DateTime? FavoriteTime { get; set; }
            public DateTime? LastAccessedTime { get; set; }
            public int AccessCount { get; set; }
        }
    }
}
