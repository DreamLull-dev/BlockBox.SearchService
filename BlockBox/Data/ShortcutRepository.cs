using BlockBox.Helpers;
using BlockBox.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlockBox.Data
{
    public static class ShortcutRepository
    {
        /// <summary>
        /// 尝试将失效的旧目标迁移到同一应用目录下的最新版本。
        /// 找不到任何新版本时，才删除旧目标及其共享状态。
        /// </summary>
        public static int RepairOrRemoveCopilotTarget(string oldTargetPath)
        {
            if (string.IsNullOrWhiteSpace(oldTargetPath))
                return 0;

            string? applicationDirectory = Path.GetDirectoryName(oldTargetPath);
            if (string.IsNullOrWhiteSpace(applicationDirectory)
                || !Directory.Exists(applicationDirectory))
            {
                return RemoveByTargetPath(oldTargetPath);
            }

            string? latestTarget = Directory.GetDirectories(applicationDirectory)
                .Select(directory => new
                {
                    Directory = directory,
                    Version = Version.TryParse(
                        Path.GetFileName(directory), out Version? version)
                        ? version
                        : null
                })
                .Where(item => item.Version != null
                    && File.Exists(Path.Combine(item.Directory, "mscopilot.exe")))
                .OrderByDescending(item => item.Version)
                .Select(item => Path.Combine(item.Directory, "mscopilot.exe"))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(latestTarget))
                return RemoveByTargetPath(oldTargetPath);

            return UpdateTargetPathForAll(oldTargetPath, latestTarget);
        }

        private static int UpdateTargetPathForAll(string oldTargetPath, string newTargetPath)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var transaction = conn.BeginTransaction();

            int updated = 0;
            using (var normal = conn.CreateCommand())
            {
                normal.Transaction = transaction;
                normal.CommandText = @"
                    UPDATE Shortcuts
                    SET TargetPath=@newTarget
                    WHERE TargetPath=@oldTarget COLLATE NOCASE";
                normal.Parameters.AddWithValue("@oldTarget", oldTargetPath);
                normal.Parameters.AddWithValue("@newTarget", newTargetPath);
                updated += normal.ExecuteNonQuery();
            }

            using (var shared = conn.CreateCommand())
            {
                shared.Transaction = transaction;
                shared.CommandText = @"
                    UPDATE SharedShortcuts
                    SET TargetPath=@newTarget
                    WHERE TargetPath=@oldTarget COLLATE NOCASE";
                shared.Parameters.AddWithValue("@oldTarget", oldTargetPath);
                shared.Parameters.AddWithValue("@newTarget", newTargetPath);
                updated += shared.ExecuteNonQuery();
            }

            transaction.Commit();
            return updated;
        }

        /// <summary>
        /// 删除指定的失效目标及其共享状态。
        /// 仅按完整目标路径匹配，避免影响同名或其他版本的项目。
        /// </summary>
        public static int RemoveByTargetPath(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return 0;

            using var conn = DatabaseContext.OpenConnection();
            using var transaction = conn.BeginTransaction();

            using (var deleteSharedState = conn.CreateCommand())
            {
                deleteSharedState.Transaction = transaction;
                deleteSharedState.CommandText = @"
                    DELETE FROM SharedShortcutModeState
                    WHERE ShortcutId IN (
                        SELECT Id FROM SharedShortcuts
                        WHERE TargetPath=@target COLLATE NOCASE)";
                deleteSharedState.Parameters.AddWithValue("@target", targetPath);
                deleteSharedState.ExecuteNonQuery();
            }

            int removed;
            using (var deleteShared = conn.CreateCommand())
            {
                deleteShared.Transaction = transaction;
                deleteShared.CommandText =
                    "DELETE FROM SharedShortcuts WHERE TargetPath=@target COLLATE NOCASE";
                deleteShared.Parameters.AddWithValue("@target", targetPath);
                removed = deleteShared.ExecuteNonQuery();
            }

            using (var deleteNormal = conn.CreateCommand())
            {
                deleteNormal.Transaction = transaction;
                deleteNormal.CommandText =
                    "DELETE FROM Shortcuts WHERE TargetPath=@target COLLATE NOCASE";
                deleteNormal.Parameters.AddWithValue("@target", targetPath);
                removed += deleteNormal.ExecuteNonQuery();
            }

            transaction.Commit();
            return removed;
        }

        public static int RemoveApplicationRootExecutables(string versionedTargetPath)
        {
            string? versionDirectory = Path.GetDirectoryName(versionedTargetPath);
            string? applicationDirectory = versionDirectory == null
                ? null
                : Path.GetDirectoryName(versionDirectory);
            if (string.IsNullOrWhiteSpace(versionDirectory)
                || string.IsNullOrWhiteSpace(applicationDirectory)
                || !Version.TryParse(Path.GetFileName(versionDirectory), out _)
                || !string.Equals(Path.GetFileName(applicationDirectory), "Application",
                    StringComparison.OrdinalIgnoreCase))
                return 0;

            var rootExecutables = Directory.Exists(applicationDirectory)
                ? Directory.GetFiles(applicationDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            int removed = 0;
            foreach (string rootExecutable in rootExecutables)
                removed += RemoveByTargetPath(rootExecutable);

            return removed;
        }

        public static void UpdateIconPath(string id, string iconPath)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Shortcuts SET IconPath = @icon WHERE Id = @id";
            cmd.Parameters.AddWithValue("@icon", iconPath);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            using var shared = conn.CreateCommand();
            shared.CommandText = "UPDATE SharedShortcuts SET IconPath=@icon WHERE Id=@id";
            shared.Parameters.AddWithValue("@icon", iconPath);
            shared.Parameters.AddWithValue("@id", id);
            shared.ExecuteNonQuery();
        }

        public static bool UpdateSharedTargetPath(string id, string targetPath)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE SharedShortcuts
                SET TargetPath=@target
                WHERE Id=@id AND NOT EXISTS (
                    SELECT 1 FROM SharedShortcuts other
                    WHERE other.CategoryId=SharedShortcuts.CategoryId
                      AND other.TargetPath=@target COLLATE NOCASE
                      AND other.Id<>@id)";
            cmd.Parameters.AddWithValue("@target", targetPath);
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() == 1;
        }

        public static bool UpdateTargetPath(string id, string targetPath, bool isShared)
        {
            if (isShared)
                return UpdateSharedTargetPath(id, targetPath);

            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Shortcuts SET TargetPath=@target WHERE Id=@id";
            cmd.Parameters.AddWithValue("@target", targetPath);
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() == 1;
        }

        public static int MigrateSharedApplicationTargetPath(string oldTargetPath, string newTargetPath, string name)
        {
            if (string.IsNullOrWhiteSpace(oldTargetPath)
                || string.IsNullOrWhiteSpace(newTargetPath)
                || string.Equals(oldTargetPath, newTargetPath, StringComparison.OrdinalIgnoreCase))
                return 0;

            var ids = new List<string>();
            using (var conn = DatabaseContext.OpenConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM SharedShortcuts WHERE TargetPath=@old COLLATE NOCASE";
                cmd.Parameters.AddWithValue("@old", oldTargetPath);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    ids.Add(reader.GetString(0));
            }

            int migrated = 0;
            foreach (string id in ids)
            {
                if (UpdateSharedTargetPath(id, newTargetPath))
                {
                    using var conn = DatabaseContext.OpenConnection();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE SharedShortcuts SET Name=@name WHERE Id=@id";
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                    migrated++;
                }
                else if (MergeSharedShortcutIntoExisting(id, newTargetPath))
                {
                    migrated++;
                }
            }

            return migrated;
        }

        public static bool MergeSharedShortcutIntoExisting(string sourceId, string targetPath)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var transaction = conn.BeginTransaction();

            using (var targetExists = conn.CreateCommand())
            {
                targetExists.Transaction = transaction;
                targetExists.CommandText = @"SELECT EXISTS(
                    SELECT 1 FROM SharedShortcuts source
                    JOIN SharedShortcuts target ON target.CategoryId=source.CategoryId
                        AND target.TargetPath=@target COLLATE NOCASE
                    WHERE source.Id=@source AND target.Id<>source.Id)";
                targetExists.Parameters.AddWithValue("@source", sourceId);
                targetExists.Parameters.AddWithValue("@target", targetPath);
                if (Convert.ToInt32(targetExists.ExecuteScalar()) != 1)
                {
                    transaction.Rollback();
                    return false;
                }
            }

            using (var mergeState = conn.CreateCommand())
            {
                mergeState.Transaction = transaction;
                mergeState.CommandText = @"INSERT INTO SharedShortcutModeState
                    (Mode,ShortcutId,SortOrder,TitleId,IsFavorite,FavoriteTime,LastAccessedTime,AccessCount)
                    SELECT source.Mode,target.Id,source.SortOrder,source.TitleId,source.IsFavorite,
                           source.FavoriteTime,source.LastAccessedTime,source.AccessCount
                    FROM SharedShortcutModeState source
                    JOIN SharedShortcuts sourceShortcut ON sourceShortcut.Id=source.ShortcutId
                    JOIN SharedShortcuts target ON target.CategoryId=sourceShortcut.CategoryId
                        AND target.TargetPath=@target COLLATE NOCASE
                    WHERE source.ShortcutId=@source
                    ON CONFLICT(Mode,ShortcutId) DO UPDATE SET
                        IsFavorite=MAX(SharedShortcutModeState.IsFavorite,excluded.IsFavorite),
                        FavoriteTime=COALESCE(SharedShortcutModeState.FavoriteTime,excluded.FavoriteTime),
                        LastAccessedTime=COALESCE(SharedShortcutModeState.LastAccessedTime,excluded.LastAccessedTime),
                        AccessCount=MAX(SharedShortcutModeState.AccessCount,excluded.AccessCount)";
                mergeState.Parameters.AddWithValue("@source", sourceId);
                mergeState.Parameters.AddWithValue("@target", targetPath);
                mergeState.ExecuteNonQuery();
            }

            using (var deleteSourceState = conn.CreateCommand())
            {
                deleteSourceState.Transaction = transaction;
                deleteSourceState.CommandText = "DELETE FROM SharedShortcutModeState WHERE ShortcutId=@source";
                deleteSourceState.Parameters.AddWithValue("@source", sourceId);
                deleteSourceState.ExecuteNonQuery();
            }

            using (var deleteSource = conn.CreateCommand())
            {
                deleteSource.Transaction = transaction;
                deleteSource.CommandText = "DELETE FROM SharedShortcuts WHERE Id=@source";
                deleteSource.Parameters.AddWithValue("@source", sourceId);
                bool deleted = deleteSource.ExecuteNonQuery() == 1;
                transaction.Commit();
                return deleted;
            }
        }

        public static List<ShortcutItem> GetAll()
        {
            var list = new List<ShortcutItem>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Shortcuts";
            using (var reader = cmd.ExecuteReader()) while (reader.Read()) list.Add(Read(reader));
            using var sharedConn = DatabaseContext.OpenConnection();
            using var sharedCmd = sharedConn.CreateCommand();
            sharedCmd.CommandText = @"SELECT s.*, @mode AS BelongingMode, COALESCE(st.TitleId,'') AS TitleId,
                COALESCE(st.IsFavorite,0) AS IsFavorite, st.FavoriteTime, st.LastAccessedTime,
                COALESCE(st.AccessCount,0) AS AccessCount FROM SharedShortcuts s LEFT JOIN SharedShortcutModeState st
                ON st.ShortcutId=s.Id AND st.Mode=@mode";
            sharedCmd.Parameters.AddWithValue("@mode", (int)AppMode.Normal);
            using (var reader = sharedCmd.ExecuteReader()) while (reader.Read()) list.Add(Read(reader, true));
            return list;
        }
        /// <summary>是否存在指向同一路径的自定义分类记录（Shortcuts 表，不含共享分类）。</summary>
        public static bool HasCustomCopiesByTargetPath(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return false;
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Shortcuts WHERE TargetPath=@p COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@p", targetPath);
            object? result = cmd.ExecuteScalar();
            return result is long count && count > 0;
        }

        public static List<ShortcutItem> GetByCategory(string categoryId)
        {
            var list = new List<ShortcutItem>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT * FROM Shortcuts 
                                WHERE CategoryId = @c ORDER BY SortOrder, Name";
            cmd.Parameters.AddWithValue("@c", categoryId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(Read(reader));
            return list;
        }
        public static List<ShortcutItem> GetByCategory(AppMode mode, string categoryId,
     SortMode sort = SortMode.Custom)
        {
            string orderBy = sort switch
            {
                SortMode.ByName => "Name",
                SortMode.ByCreatedTime => "CreatedTime DESC",
                _ => "SortOrder, Name"
            };

            if (CategoryRepository.IsShared(categoryId))
                return GetSharedByCategory(categoryId, mode, sort);

            var list = new List<ShortcutItem>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"SELECT * FROM Shortcuts 
                        WHERE CategoryId = @c AND BelongingMode = @m
                        ORDER BY {orderBy}";
            cmd.Parameters.AddWithValue("@c", categoryId);
            cmd.Parameters.AddWithValue("@m", (int)mode);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(Read(reader));
            return list;
        }

        public static List<ShortcutItem> GetSharedByCategory(string categoryId, AppMode mode = AppMode.Normal,
            SortMode sort = SortMode.Custom)
        {
            string orderBy = sort switch { SortMode.ByName => "s.Name", SortMode.ByCreatedTime => "s.CreatedTime DESC", _ => "s.SortOrder, s.Name" };
            var list = new List<ShortcutItem>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"SELECT s.*, @mode AS BelongingMode,
                COALESCE(st.TitleId, '') AS TitleId,
                COALESCE(st.IsFavorite, 0) AS IsFavorite, st.FavoriteTime,
                st.LastAccessedTime, COALESCE(st.AccessCount, 0) AS AccessCount
                FROM SharedShortcuts s LEFT JOIN SharedShortcutModeState st
                ON st.ShortcutId=s.Id AND st.Mode=@mode
                WHERE s.CategoryId=@category ORDER BY {orderBy}";
            cmd.Parameters.AddWithValue("@mode", (int)mode);
            cmd.Parameters.AddWithValue("@category", categoryId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(Read(reader, true));
            return list;
        }

        public static int GetNextSortOrder(string categoryId, bool shared = false)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = shared
                ? "SELECT COALESCE(MAX(SortOrder), 0) FROM SharedShortcuts WHERE CategoryId = @c"
                : "SELECT COALESCE(MAX(SortOrder), 0) FROM Shortcuts WHERE CategoryId = @c";
            cmd.Parameters.AddWithValue("@c", categoryId);
            return Convert.ToInt32(cmd.ExecuteScalar()) + 1;
        }
        public static void Add(ShortcutItem s)
        {
            using var conn = DatabaseContext.OpenConnection();
            using (var idCheck = conn.CreateCommand())
            {
                idCheck.CommandText = "SELECT EXISTS(SELECT 1 FROM Shortcuts WHERE Id=@id) OR EXISTS(SELECT 1 FROM SharedShortcuts WHERE Id=@id)";
                idCheck.Parameters.AddWithValue("@id", s.Id);
                if (Convert.ToInt32(idCheck.ExecuteScalar()) == 1)
                    s.Id = Guid.NewGuid().ToString();
            }
            using var cmd = conn.CreateCommand();
            if (s.IsShared)
            {
                cmd.CommandText = @"INSERT INTO SharedShortcuts
                    (Id,Name,TargetPath,Arguments,WorkingDirectory,IconPath,CategoryId,IsFile,SortOrder,
                     BackupPath,SourceMd5,SourceState,LastVerifiedTime,IsBackedUp,CreatedTime)
                    VALUES (@id,@name,@target,@args,@wd,@icon,@cat,@isFile,@sort,@backup,@md5,@state,@verified,@backed,@created)";
                cmd.Parameters.AddWithValue("@id", s.Id); cmd.Parameters.AddWithValue("@name", s.Name);
                cmd.Parameters.AddWithValue("@target", s.TargetPath); cmd.Parameters.AddWithValue("@args", s.Arguments ?? "");
                cmd.Parameters.AddWithValue("@wd", s.WorkingDirectory ?? ""); cmd.Parameters.AddWithValue("@icon", s.IconPath ?? "");
                cmd.Parameters.AddWithValue("@cat", s.CategoryId); cmd.Parameters.AddWithValue("@isFile", s.IsFile ? 1 : 0);
                cmd.Parameters.AddWithValue("@sort", s.SortOrder); cmd.Parameters.AddWithValue("@backup", s.BackupPath ?? "");
                cmd.Parameters.AddWithValue("@md5", s.SourceMd5 ?? ""); cmd.Parameters.AddWithValue("@state", (int)s.SourceState);
                cmd.Parameters.AddWithValue("@verified", s.LastVerifiedTime.HasValue ? s.LastVerifiedTime.Value.ToString("O") : DBNull.Value);
                cmd.Parameters.AddWithValue("@backed", s.IsBackedUp ? 1 : 0); cmd.Parameters.AddWithValue("@created", s.CreatedTime.ToString("O"));
                cmd.ExecuteNonQuery(); return;
            }
            cmd.CommandText = @"
    INSERT INTO Shortcuts 
    (Id, Name, TargetPath, Arguments, WorkingDirectory, IconPath,
     CategoryId, TitleId, IsFile, BelongingMode, SortOrder,
     IsFavorite, FavoriteTime, LastAccessedTime, AccessCount,
     BackupPath, SourceMd5, SourceState, LastVerifiedTime, IsBackedUp,
     CreatedTime)
    VALUES
    (@id, @name, @target, @args, @wd, @icon,
     @cat, @tab, @isFile, @mode, @sort,
     @fav, @favTime, @lastAccess, @access,
     @backup, @md5, @state, @verified, @backed,
     @created)";
            Bind(cmd, s);
            cmd.ExecuteNonQuery();
        }

        public static void UpdateSourceState(string id, SourceState state,
            string md5, DateTime verifiedTime)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE Shortcuts 
                                SET SourceState = @s, SourceMd5 = @m, LastVerifiedTime = @t
                                WHERE Id = @id";
            cmd.Parameters.AddWithValue("@s", (int)state);
            cmd.Parameters.AddWithValue("@m", md5);
            cmd.Parameters.AddWithValue("@t", verifiedTime.ToString("O"));
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            using var sharedState = conn.CreateCommand();
            sharedState.CommandText = "UPDATE SharedShortcuts SET SourceState=@s, SourceMd5=@m, LastVerifiedTime=@t WHERE Id=@id";
            sharedState.Parameters.AddWithValue("@s", (int)state); sharedState.Parameters.AddWithValue("@m", md5);
            sharedState.Parameters.AddWithValue("@t", verifiedTime.ToString("O")); sharedState.Parameters.AddWithValue("@id", id);
            sharedState.ExecuteNonQuery();
        }

        public static void UpdateAccessInfo(string id, DateTime time) => UpdateAccessInfo(id, AppMode.Normal, time);

        public static void UpdateAccessInfo(string id, AppMode mode, DateTime time)
        {
            using var conn = DatabaseContext.OpenConnection();
            if (IsSharedRecord(id))
            {
                using var state = conn.CreateCommand();
                state.CommandText = @"INSERT INTO SharedShortcutModeState(Mode,ShortcutId,LastAccessedTime,AccessCount)
                    VALUES(@mode,@id,@time,1) ON CONFLICT(Mode,ShortcutId) DO UPDATE SET
                    LastAccessedTime=excluded.LastAccessedTime,AccessCount=SharedShortcutModeState.AccessCount+1";
                state.Parameters.AddWithValue("@mode", (int)mode); state.Parameters.AddWithValue("@id", id);
                state.Parameters.AddWithValue("@time", time.ToString("O")); state.ExecuteNonQuery(); return;
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE Shortcuts 
                                SET LastAccessedTime = @t, AccessCount = AccessCount + 1
                                WHERE Id = @id";
            cmd.Parameters.AddWithValue("@t", time.ToString("O"));
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static void Delete(string id)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Shortcuts WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            using var shared = conn.CreateCommand();
            shared.CommandText = "DELETE FROM SharedShortcuts WHERE Id=@id";
            shared.Parameters.AddWithValue("@id", id);
            shared.ExecuteNonQuery();
            using var state = conn.CreateCommand();
            state.CommandText = "DELETE FROM SharedShortcutModeState WHERE ShortcutId=@id";
            state.Parameters.AddWithValue("@id", id);
            state.ExecuteNonQuery();
        }

        public static void DeleteByTargetPath(string targetPath)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var tran = conn.BeginTransaction();

            using (var state = conn.CreateCommand())
            {
                state.Transaction = tran;
                state.CommandText = @"DELETE FROM SharedShortcutModeState
                    WHERE ShortcutId IN (SELECT Id FROM SharedShortcuts WHERE TargetPath=@path COLLATE NOCASE)";
                state.Parameters.AddWithValue("@path", targetPath);
                state.ExecuteNonQuery();
            }

            foreach (string table in new[] { "Shortcuts", "SharedShortcuts" })
            {
                using var command = conn.CreateCommand();
                command.Transaction = tran;
                command.CommandText = $"DELETE FROM {table} WHERE TargetPath=@path COLLATE NOCASE";
                command.Parameters.AddWithValue("@path", targetPath);
                command.ExecuteNonQuery();
            }

            tran.Commit();
        }

        public static void DeleteByCategory(string categoryId)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Shortcuts WHERE CategoryId = @c";
            cmd.Parameters.AddWithValue("@c", categoryId);
            cmd.ExecuteNonQuery();
            using var state = conn.CreateCommand();
            state.CommandText = "DELETE FROM SharedShortcutModeState WHERE ShortcutId IN (SELECT Id FROM SharedShortcuts WHERE CategoryId=@c)";
            state.Parameters.AddWithValue("@c", categoryId); state.ExecuteNonQuery();
            using var shared = conn.CreateCommand();
            shared.CommandText = "DELETE FROM SharedShortcuts WHERE CategoryId=@c";
            shared.Parameters.AddWithValue("@c", categoryId);
            shared.ExecuteNonQuery();
        }

        public static void MoveToCategory(string id, string categoryId, int sortOrder)
        {
            using var conn = DatabaseContext.OpenConnection();
            bool isShared = IsSharedRecord(id);
            bool targetShared = CategoryRepository.IsShared(categoryId);
            if (isShared != targetShared)
                throw new InvalidOperationException("不能在共享记录和模式记录之间移动");
            if (isShared)
            {
                using var scope = conn.CreateCommand();
                scope.CommandText = @"SELECT source.Name, target.Name
                    FROM SharedShortcuts s
                    JOIN SharedCategories source ON source.Id=s.CategoryId
                    JOIN SharedCategories target ON target.Id=@category
                    WHERE s.Id=@id";
                scope.Parameters.AddWithValue("@id", id);
                scope.Parameters.AddWithValue("@category", categoryId);
                using var reader = scope.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("记录或目标分类已不存在。");
                string sourceName = reader.GetString(0), targetName = reader.GetString(1);
                if (string.Equals(sourceName, "全部应用", StringComparison.Ordinal)
                    || string.Equals(targetName, "全部应用", StringComparison.Ordinal))
                    throw new InvalidOperationException("“全部应用”中的记录只能复制到其他分类，不能移动进出。");
            }
            if (!isShared)
            {
                using var scope = conn.CreateCommand();
                scope.CommandText = @"SELECT s.BelongingMode = c.BelongingMode FROM Shortcuts s, Categories c
                    WHERE s.Id=@id AND c.Id=@category";
                scope.Parameters.AddWithValue("@id", id); scope.Parameters.AddWithValue("@category", categoryId);
                if (Convert.ToInt32(scope.ExecuteScalar() ?? 0) != 1)
                    throw new InvalidOperationException("只能在当前模式的分类之间移动");
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = isShared ? @"UPDATE SharedShortcuts
                                SET CategoryId = @categoryId, SortOrder = @sortOrder
                                WHERE Id = @id" : @"UPDATE Shortcuts
                                SET CategoryId = @categoryId, SortOrder = @sortOrder
                                WHERE Id = @id";
            cmd.Parameters.AddWithValue("@categoryId", categoryId);
            cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
            cmd.Parameters.AddWithValue("@id", id);
            if (cmd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("记录已不存在，未执行移动。");
        }

        public static bool IsSharedRecord(string id)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM SharedShortcuts WHERE Id=@id)";
            cmd.Parameters.AddWithValue("@id", id);
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }

        public static bool HasBackupReference(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath)) return false;
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT EXISTS(SELECT 1 FROM Shortcuts WHERE BackupPath=@path COLLATE NOCASE)
                OR EXISTS(SELECT 1 FROM SharedShortcuts WHERE BackupPath=@path COLLATE NOCASE)
                OR EXISTS(SELECT 1 FROM RecycleBin WHERE BackupPath=@path COLLATE NOCASE)";
            cmd.Parameters.AddWithValue("@path", backupPath);
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }

        // ==================== 私有 ====================

        private static void Bind(SqliteCommand cmd, ShortcutItem s)
        {
            cmd.Parameters.AddWithValue("@id", s.Id);
            cmd.Parameters.AddWithValue("@name", s.Name);
            cmd.Parameters.AddWithValue("@target", s.TargetPath);
            cmd.Parameters.AddWithValue("@args", s.Arguments ?? "");
            cmd.Parameters.AddWithValue("@wd", s.WorkingDirectory ?? "");
            cmd.Parameters.AddWithValue("@icon", s.IconPath ?? "");
            cmd.Parameters.AddWithValue("@cat", s.CategoryId ?? "");
            cmd.Parameters.AddWithValue("@tab", s.TitleId ?? "");
            cmd.Parameters.AddWithValue("@isFile", s.IsFile ? 1 : 0);
            cmd.Parameters.AddWithValue("@mode", (int)s.BelongingMode);
            cmd.Parameters.AddWithValue("@sort", s.SortOrder);
            cmd.Parameters.AddWithValue("@fav", s.IsFavorite ? 1 : 0);
            cmd.Parameters.AddWithValue("@favTime",
                s.FavoriteTime.HasValue ? s.FavoriteTime.Value.ToString("O") : DBNull.Value);
            cmd.Parameters.AddWithValue("@lastAccess",
                s.LastAccessedTime.HasValue ? s.LastAccessedTime.Value.ToString("O") : DBNull.Value);
            cmd.Parameters.AddWithValue("@access", s.AccessCount);
            cmd.Parameters.AddWithValue("@backup", s.BackupPath ?? "");
            cmd.Parameters.AddWithValue("@md5", s.SourceMd5 ?? "");
            cmd.Parameters.AddWithValue("@state", (int)s.SourceState);
            cmd.Parameters.AddWithValue("@verified",
                s.LastVerifiedTime.HasValue ? s.LastVerifiedTime.Value.ToString("O") : DBNull.Value);
            cmd.Parameters.AddWithValue("@backed", s.IsBackedUp ? 1 : 0);
            cmd.Parameters.AddWithValue("@created",
    s.CreatedTime.ToString("O"));
        }

        private static ShortcutItem Read(SqliteDataReader r, bool isShared = false)
        {
            return new ShortcutItem
            {
                IsShared = isShared,
                Id = r.GetString(r.GetOrdinal("Id")),
                Name = r.GetString(r.GetOrdinal("Name")),
                TargetPath = r.GetString(r.GetOrdinal("TargetPath")),
                Arguments = r.GetString(r.GetOrdinal("Arguments")),
                WorkingDirectory = r.GetString(r.GetOrdinal("WorkingDirectory")),
                IconPath = r.GetString(r.GetOrdinal("IconPath")),
                CategoryId = r.GetString(r.GetOrdinal("CategoryId")),
                TitleId = r.GetString(r.GetOrdinal("TitleId")),
                IsFile = r.GetInt32(r.GetOrdinal("IsFile")) == 1,
                BelongingMode = (AppMode)r.GetInt32(r.GetOrdinal("BelongingMode")),
                SortOrder = r.GetInt32(r.GetOrdinal("SortOrder")),
                IsFavorite = r.GetInt32(r.GetOrdinal("IsFavorite")) == 1,
                FavoriteTime = r.IsDBNull(r.GetOrdinal("FavoriteTime")) ? null
                    : DateTime.Parse(r.GetString(r.GetOrdinal("FavoriteTime"))),
                LastAccessedTime = r.IsDBNull(r.GetOrdinal("LastAccessedTime")) ? null
                    : DateTime.Parse(r.GetString(r.GetOrdinal("LastAccessedTime"))),
                AccessCount = r.GetInt32(r.GetOrdinal("AccessCount")),
                BackupPath = r.GetString(r.GetOrdinal("BackupPath")),
                SourceMd5 = r.GetString(r.GetOrdinal("SourceMd5")),
                SourceState = (SourceState)r.GetInt32(r.GetOrdinal("SourceState")),
                LastVerifiedTime = r.IsDBNull(r.GetOrdinal("LastVerifiedTime")) ? null
                    : DateTime.Parse(r.GetString(r.GetOrdinal("LastVerifiedTime"))),
                IsBackedUp = r.GetInt32(r.GetOrdinal("IsBackedUp")) == 1,
                CreatedTime = GetDateTimeOrDefault(r, "CreatedTime"),
            };
        }
        private static DateTime GetDateTimeOrDefault(SqliteDataReader r, string column)
        {
            int idx = r.GetOrdinal(column);
            if (r.IsDBNull(idx)) return DateTime.Now;

            string s = r.GetString(idx);
            if (string.IsNullOrEmpty(s)) return DateTime.Now;

            if (DateTime.TryParse(s, out var dt)) return dt;

            return DateTime.Now;
        }
        /// <summary>
        /// 按名称顺序，把分类下所有 shortcut 的 SortOrder 重写为 1、2、3...
        /// </summary>
        public static void ResequenceByName(string categoryId, AppMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();

            // 查
            var list = new List<(string Id, string Name)>();
            using (var q = conn.CreateCommand())
            {
                q.CommandText = CategoryRepository.IsShared(categoryId) ? @"SELECT Id, Name FROM SharedShortcuts
                          WHERE CategoryId=@c ORDER BY Name" : @"SELECT Id, Name FROM Shortcuts 
                          WHERE CategoryId = @c AND BelongingMode = @m
                          ORDER BY Name";
                q.Parameters.AddWithValue("@c", categoryId);
                q.Parameters.AddWithValue("@m", (int)mode);
                using var r = q.ExecuteReader();
                while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
            }

            // 更新
            using var tran = conn.BeginTransaction();
            int order = 1;
            foreach (var item in list)
            {
                using var u = conn.CreateCommand();
                u.Transaction = tran;
                u.CommandText = "UPDATE Shortcuts SET SortOrder = @o WHERE Id = @id";
                u.Parameters.AddWithValue("@o", order++);
                u.Parameters.AddWithValue("@id", item.Id);
                u.ExecuteNonQuery();
                using var shared = conn.CreateCommand(); shared.Transaction = tran;
                shared.CommandText = "UPDATE SharedShortcuts SET SortOrder=@o WHERE Id=@id";
                shared.Parameters.AddWithValue("@o", order - 1); shared.Parameters.AddWithValue("@id", item.Id); shared.ExecuteNonQuery();
            }
            tran.Commit();
        }
        /// <summary>
        /// 按名称顺序，把标签下所有 shortcut 的 SortOrder 重写为 1、2、3...
        /// </summary>
        public static void ResequenceTabByName(string tabId, AppMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();

            var ids = new List<string>();
            using (var q = conn.CreateCommand())
            {
                q.CommandText = @"SELECT Id FROM Shortcuts 
                          WHERE TitleId = @t AND BelongingMode = @m
                          ORDER BY Name";
                q.Parameters.AddWithValue("@t", tabId);
                q.Parameters.AddWithValue("@m", (int)mode);
                using var r = q.ExecuteReader();
                while (r.Read()) ids.Add(r.GetString(0));
            }

            using var tran = conn.BeginTransaction();
            int order = 1;
            foreach (var id in ids)
            {
                using var u = conn.CreateCommand();
                u.Transaction = tran;
                u.CommandText = "UPDATE Shortcuts SET SortOrder = @o WHERE Id = @id";
                u.Parameters.AddWithValue("@o", order++);
                u.Parameters.AddWithValue("@id", id);
                u.ExecuteNonQuery();
            }
            tran.Commit();
            ResequenceSharedTab(tabId, mode, byCreatedTime: false);
        }
        /// <summary>
        /// 按加入时间顺序，把标签下所有 shortcut 的 SortOrder 重写为 1、2、3...
        /// </summary>
        public static void ResequenceTabByCreatedTime(string tabId, AppMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();

            var ids = new List<string>();
            using (var q = conn.CreateCommand())
            {
                q.CommandText = @"SELECT Id FROM Shortcuts 
                          WHERE TitleId = @t AND BelongingMode = @m
                          ORDER BY CreatedTime ASC";
                q.Parameters.AddWithValue("@t", tabId);
                q.Parameters.AddWithValue("@m", (int)mode);
                using var r = q.ExecuteReader();
                while (r.Read()) ids.Add(r.GetString(0));
            }

            using var tran = conn.BeginTransaction();
            int order = 1;
            foreach (var id in ids)
            {
                using var u = conn.CreateCommand();
                u.Transaction = tran;
                u.CommandText = "UPDATE Shortcuts SET SortOrder = @o WHERE Id = @id";
                u.Parameters.AddWithValue("@o", order++);
                u.Parameters.AddWithValue("@id", id);
                u.ExecuteNonQuery();
            }
            tran.Commit();
            ResequenceSharedTab(tabId, mode, byCreatedTime: true);
        }

        private static void ResequenceSharedTab(string tabId, AppMode mode, bool byCreatedTime)
        {
            using var conn = DatabaseContext.OpenConnection();
            var ids = new List<string>();
            using (var query = conn.CreateCommand())
            {
                query.CommandText = byCreatedTime
                    ? @"SELECT st.ShortcutId FROM SharedShortcutModeState st JOIN SharedShortcuts s ON s.Id=st.ShortcutId
                        WHERE st.Mode=@mode AND st.TitleId=@tab ORDER BY s.CreatedTime"
                    : @"SELECT st.ShortcutId FROM SharedShortcutModeState st JOIN SharedShortcuts s ON s.Id=st.ShortcutId
                        WHERE st.Mode=@mode AND st.TitleId=@tab ORDER BY s.Name COLLATE NOCASE";
                query.Parameters.AddWithValue("@mode", (int)mode); query.Parameters.AddWithValue("@tab", tabId);
                using var reader = query.ExecuteReader(); while (reader.Read()) ids.Add(reader.GetString(0));
            }
            using var tran = conn.BeginTransaction();
            int order = 1;
            foreach (var id in ids)
            {
                using var update = conn.CreateCommand(); update.Transaction = tran;
                update.CommandText = "UPDATE SharedShortcutModeState SET SortOrder=@order WHERE Mode=@mode AND ShortcutId=@id";
                update.Parameters.AddWithValue("@order", order++); update.Parameters.AddWithValue("@mode", (int)mode);
                update.Parameters.AddWithValue("@id", id); update.ExecuteNonQuery();
            }
            tran.Commit();
        }
        /// <summary>
        /// 按加入时间顺序，把分类下所有 shortcut 的 SortOrder 重写为 1、2、3...
        /// </summary>
        public static void ResequenceByCreatedTime(string categoryId, AppMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();

            // 第一步：查出所有 Id（按 CreatedTime 排序）
            var ids = new List<string>();
            using (var q = conn.CreateCommand())
            {
                q.CommandText = CategoryRepository.IsShared(categoryId) ? @"SELECT Id FROM SharedShortcuts WHERE CategoryId=@c ORDER BY CreatedTime ASC" : @"SELECT Id FROM Shortcuts 
                          WHERE CategoryId = @c AND BelongingMode = @m
                          ORDER BY CreatedTime ASC";
                q.Parameters.AddWithValue("@c", categoryId);
                q.Parameters.AddWithValue("@m", (int)mode);
                using var r = q.ExecuteReader();
                while (r.Read()) ids.Add(r.GetString(0));
            }

            // 第二步：逐个更新 SortOrder
            using var tran = conn.BeginTransaction();
            int order = 1;
            foreach (var id in ids)
            {
                using var u = conn.CreateCommand();
                u.Transaction = tran;
                u.CommandText = "UPDATE Shortcuts SET SortOrder = @o WHERE Id = @id";
                u.Parameters.AddWithValue("@o", order++);
                u.Parameters.AddWithValue("@id", id);
                u.ExecuteNonQuery();
                using var shared = conn.CreateCommand(); shared.Transaction = tran;
                shared.CommandText = "UPDATE SharedShortcuts SET SortOrder=@o WHERE Id=@id";
                shared.Parameters.AddWithValue("@o", order - 1); shared.Parameters.AddWithValue("@id", id); shared.ExecuteNonQuery();
            }
            tran.Commit();
        }

        public static List<ShortcutItem> GetFavorites(AppMode mode)
        {
            var list = new List<ShortcutItem>();
            CleanOrphanSharedStates();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT * FROM Shortcuts 
                        WHERE BelongingMode = @m AND IsFavorite = 1
                        ORDER BY FavoriteTime DESC";
            cmd.Parameters.AddWithValue("@m", (int)mode);
            using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(Read(r));
            list.AddRange(GetSharedByState(mode, favorites: true));
            list.Sort((a,b) => Nullable.Compare(b.FavoriteTime, a.FavoriteTime));
            return list;
        }

        public static List<ShortcutItem> GetRecent(AppMode mode, int limit = 30)
        {
            var list = new List<ShortcutItem>();
            CleanOrphanSharedStates();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT * FROM Shortcuts 
                        WHERE BelongingMode = @m AND LastAccessedTime IS NOT NULL
                        ORDER BY LastAccessedTime DESC LIMIT @n";
            cmd.Parameters.AddWithValue("@m", (int)mode);
            cmd.Parameters.AddWithValue("@n", limit);
            using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(Read(r));
            list.AddRange(GetSharedByState(mode, favorites: false, limit: limit));
            list.Sort((a,b) => Nullable.Compare(b.LastAccessedTime, a.LastAccessedTime));
            if (list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }

        private static List<ShortcutItem> GetSharedByState(AppMode mode, bool favorites, int limit = int.MaxValue)
        {
            var list = new List<ShortcutItem>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = favorites
                ? @"SELECT s.*, @mode AS BelongingMode, st.TitleId, st.IsFavorite, st.FavoriteTime,
                    st.LastAccessedTime, st.AccessCount FROM SharedShortcuts s JOIN SharedShortcutModeState st
                    ON st.ShortcutId=s.Id WHERE st.Mode=@mode AND st.IsFavorite=1 ORDER BY st.FavoriteTime DESC"
                : @"SELECT s.*, @mode AS BelongingMode, st.TitleId, st.IsFavorite, st.FavoriteTime,
                    st.LastAccessedTime, st.AccessCount FROM SharedShortcuts s JOIN SharedShortcutModeState st
                    ON st.ShortcutId=s.Id WHERE st.Mode=@mode AND st.LastAccessedTime IS NOT NULL ORDER BY st.LastAccessedTime DESC LIMIT @limit";
            cmd.Parameters.AddWithValue("@mode", (int)mode);
            if (!favorites) cmd.Parameters.AddWithValue("@limit", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(Read(reader, true));
            return list;
        }

        private static void CleanOrphanSharedStates()
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM SharedShortcutModeState WHERE NOT EXISTS(SELECT 1 FROM SharedShortcuts s WHERE s.Id=SharedShortcutModeState.ShortcutId)";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 删除快捷方式后清理标签/收藏/最近访问的悬空引用：
        /// 记录本身被删除时，它的收藏/最近访问状态随记录一起消失；
        /// 这里再兜底清掉指向已不存在记录或已删除标签页的残留。
        /// </summary>
        public static void CleanupAfterShortcutDeletion()
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM SharedShortcutModeState
                WHERE NOT EXISTS(SELECT 1 FROM SharedShortcuts s WHERE s.Id=SharedShortcutModeState.ShortcutId);

                UPDATE Shortcuts SET TitleId=''
                WHERE TitleId<>'' AND NOT EXISTS(SELECT 1 FROM Tabs t WHERE t.Id=Shortcuts.TitleId);

                UPDATE SharedShortcutModeState SET TitleId=''
                WHERE TitleId<>'' AND NOT EXISTS(SELECT 1 FROM Tabs t WHERE t.Id=SharedShortcutModeState.TitleId);
            ";
            cmd.ExecuteNonQuery();
        }
        public static List<ShortcutItem> GetByTab(AppMode mode, string tabId, SortMode sort = SortMode.Custom)
        {
            string orderBy = sort switch
            {
                SortMode.ByName => "Name",
                SortMode.ByCreatedTime => "CreatedTime DESC",
                _ => "SortOrder, Name"
            };
            var list = new List<ShortcutItem>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"SELECT * FROM Shortcuts 
                    WHERE TitleId = @t AND BelongingMode = @m
                    ORDER BY {orderBy}";
            cmd.Parameters.AddWithValue("@t", tabId);
            cmd.Parameters.AddWithValue("@m", (int)mode);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Read(r));
            // reader must be closed before querying the shared-table projection.
            r.Close();
            list.AddRange(GetSharedByTab(mode, tabId, sort));
            return sort switch
            {
                SortMode.ByName => list.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                SortMode.ByCreatedTime => list.OrderByDescending(s => s.CreatedTime).ToList(),
                _ => list.OrderBy(s => s.SortOrder).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        public static List<ShortcutItem> Search(AppMode mode, string keyword)
        {
            var list = new List<ShortcutItem>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT * FROM Shortcuts 
                        WHERE BelongingMode = @m 
                        AND (Name LIKE @k OR TargetPath LIKE @k)
                        ORDER BY Name";
            cmd.Parameters.AddWithValue("@m", (int)mode);
            cmd.Parameters.AddWithValue("@k", $"%{keyword}%");
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Read(r));
            list.AddRange(GetSharedSearch(mode, keyword));
            // First collapse duplicate rows for the same stored record, then
            // collapse application entries that point to the same target.
            // Ordinary files intentionally remain distinct, even when paths
            // or names match.
            var uniqueRecords = list
                .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
            var seenApplications = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<ShortcutItem>();
            foreach (var shortcut in uniqueRecords)
            {
                if (shortcut.IsFile)
                {
                    results.Add(shortcut);
                    continue;
                }

                string key = GetApplicationSearchKey(shortcut.TargetPath);
                if (key.Length == 0 || seenApplications.Add(key))
                    results.Add(shortcut);
            }

            return results.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string GetApplicationSearchKey(string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return string.Empty;

            string normalized = targetPath.Trim().Trim('"');
            try
            {
                normalized = Path.GetFullPath(normalized);
            }
            catch
            {
                // Shell targets and non-filesystem identifiers are still
                // comparable after basic separator normalization.
            }

            return normalized.Replace('/', '\\').TrimEnd('\\');
        }

        private static List<ShortcutItem> GetSharedSearch(AppMode mode, string keyword)
        {
            var list = new List<ShortcutItem>();
            using var conn=DatabaseContext.OpenConnection(); using var cmd=conn.CreateCommand();
            cmd.CommandText=@"SELECT s.*, @mode AS BelongingMode, COALESCE(st.TitleId,'') AS TitleId,
                COALESCE(st.IsFavorite,0) AS IsFavorite, st.FavoriteTime,st.LastAccessedTime,
                COALESCE(st.AccessCount,0) AS AccessCount FROM SharedShortcuts s LEFT JOIN SharedShortcutModeState st
                ON st.ShortcutId=s.Id AND st.Mode=@mode WHERE s.Name LIKE @key OR s.TargetPath LIKE @key ORDER BY s.Name";
            cmd.Parameters.AddWithValue("@mode",(int)mode);cmd.Parameters.AddWithValue("@key",$"%{keyword}%");
            using var r=cmd.ExecuteReader();while(r.Read())list.Add(Read(r,true));return list;
        }

        private static List<ShortcutItem> GetSharedByTab(AppMode mode, string tabId, SortMode sort)
        {
            var list = new List<ShortcutItem>();
            using var conn = DatabaseContext.OpenConnection(); using var cmd = conn.CreateCommand();
            string orderBy = sort switch
            {
                SortMode.ByName => "s.Name",
                SortMode.ByCreatedTime => "s.CreatedTime DESC",
                _ => "st.SortOrder,s.Name"
            };
            cmd.CommandText = $@"SELECT s.*, @mode AS BelongingMode, COALESCE(st.TitleId,'') AS TitleId,
                COALESCE(st.IsFavorite,0) AS IsFavorite, st.FavoriteTime,st.LastAccessedTime,
                COALESCE(st.AccessCount,0) AS AccessCount, st.SortOrder AS ModeSortOrder FROM SharedShortcuts s JOIN SharedShortcutModeState st
                ON st.ShortcutId=s.Id AND st.Mode=@mode WHERE st.TitleId=@tab ORDER BY {orderBy}";
            cmd.Parameters.AddWithValue("@mode", (int)mode); cmd.Parameters.AddWithValue("@tab", tabId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var item = Read(r, true);
                item.SortOrder = r.GetInt32(r.GetOrdinal("ModeSortOrder"));
                list.Add(item);
            }
            return list;
        }
        public static void UpdateName(string id, string newName)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Shortcuts SET Name = @n WHERE Id = @id";
            cmd.Parameters.AddWithValue("@n", newName);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            using var shared = conn.CreateCommand(); shared.CommandText = "UPDATE SharedShortcuts SET Name=@n WHERE Id=@id";
            shared.Parameters.AddWithValue("@n", newName); shared.Parameters.AddWithValue("@id", id); shared.ExecuteNonQuery();
        }

        public static void SetFavorite(string id, bool isFav) => SetFavorite(id, AppMode.Normal, isFav);

        public static void SetFavorite(string id, AppMode mode, bool isFav)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            if (IsSharedRecord(id))
            {
                cmd.CommandText = @"INSERT INTO SharedShortcutModeState(Mode,ShortcutId,IsFavorite,FavoriteTime)
                    VALUES(@mode,@id,@f,@t) ON CONFLICT(Mode,ShortcutId) DO UPDATE SET
                    IsFavorite=excluded.IsFavorite,FavoriteTime=excluded.FavoriteTime";
                cmd.Parameters.AddWithValue("@mode", (int)mode);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@f", isFav ? 1 : 0);
                cmd.Parameters.AddWithValue("@t", isFav ? DateTime.Now.ToString("O") : (object)DBNull.Value);
                cmd.ExecuteNonQuery(); return;
            }
            cmd.CommandText = @"UPDATE Shortcuts 
                        SET IsFavorite = @f, FavoriteTime = @t 
                        WHERE Id = @id";
            cmd.Parameters.AddWithValue("@f", isFav ? 1 : 0);
            cmd.Parameters.AddWithValue("@t",
                isFav ? DateTime.Now.ToString("O") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static void ClearRecent(string id) => ClearRecent(id, AppMode.Normal);

        public static void ClearRecent(string id, AppMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();
            if (IsSharedRecord(id))
            {
                using var state = conn.CreateCommand();
                state.CommandText = "UPDATE SharedShortcutModeState SET LastAccessedTime=NULL, AccessCount=0 WHERE Mode=@mode AND ShortcutId=@id";
                state.Parameters.AddWithValue("@mode", (int)mode); state.Parameters.AddWithValue("@id", id);
                state.ExecuteNonQuery(); return;
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE Shortcuts 
                        SET LastAccessedTime = NULL, AccessCount = 0 
                        WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static string GetCategoryName(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return "";
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Categories WHERE Id=@id UNION ALL SELECT Name FROM SharedCategories WHERE Id=@id LIMIT 1";
            cmd.Parameters.AddWithValue("@id", categoryId);
            return cmd.ExecuteScalar() as string ?? "";
        }

        /// <summary>
        /// 设置一个 Shortcut 的标签归属（清空传空串）
        /// </summary>
        public static void SetTitleId(string id, string titleId) => SetTitleId(id, AppMode.Normal, titleId);

        public static void SetTitleId(string id, AppMode mode, string titleId)
        {
            using var conn = DatabaseContext.OpenConnection();
            if (IsSharedRecord(id))
            {
                using var state = conn.CreateCommand();
                state.CommandText = @"INSERT INTO SharedShortcutModeState(Mode,ShortcutId,TitleId,SortOrder)
                    VALUES(@mode,@id,@title,(SELECT COALESCE(MAX(SortOrder),0)+1 FROM SharedShortcutModeState WHERE Mode=@mode AND TitleId=@title))
                    ON CONFLICT(Mode,ShortcutId) DO UPDATE SET TitleId=excluded.TitleId,SortOrder=excluded.SortOrder";
                state.Parameters.AddWithValue("@mode", (int)mode); state.Parameters.AddWithValue("@id", id);
                state.Parameters.AddWithValue("@title", titleId ?? ""); state.ExecuteNonQuery(); return;
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Shortcuts SET TitleId = @t WHERE Id = @id";
            cmd.Parameters.AddWithValue("@t", titleId ?? "");
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 判断某分类下是否已有同一个 TargetPath 的记录
        /// </summary>
        public static bool ExistsInCategory(string categoryId, AppMode mode, string targetPath)
        {
            string identity = BlockBox.Helpers.ProgramIdentity.Get(targetPath);
            string exeKey = BlockBox.Helpers.ProgramIdentity.ResolveTarget(targetPath);
            var shortcutInfo = BlockBox.Services.InstalledProgramScanner.ResolveShortcutInfo(targetPath);
            bool benignArguments = shortcutInfo == null
                || BlockBox.Services.InstalledProgramScanner.IsBenignLaunchArgument(shortcutInfo.Arguments);

            var rows = CategoryRepository.IsShared(categoryId)
                ? GetSharedByCategory(categoryId)
                : GetByCategory(mode, categoryId);

            return rows.Any(item => string.Equals(
                    BlockBox.Helpers.ProgramIdentity.Get(item.TargetPath, item.Arguments),
                    identity,
                    StringComparison.Ordinal)
                || (benignArguments
                    && string.Equals(
                        BlockBox.Helpers.ProgramIdentity.ResolveTarget(item.TargetPath),
                        exeKey,
                        StringComparison.OrdinalIgnoreCase)));
        }
        /// <summary>
        /// 批量更新 SortOrder（拖拽排序后调用）
        /// </summary>
        public static void UpdateSortOrders(List<(string Id, int SortOrder)> updates, AppMode? mode = null, string? tabId = null)
        {
            if (updates.Count == 0) return;

            using var conn = DatabaseContext.OpenConnection();
            using var tran = conn.BeginTransaction();

            foreach (var (id, order) in updates)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tran;
                cmd.CommandText = "UPDATE Shortcuts SET SortOrder = @o WHERE Id = @id";
                cmd.Parameters.AddWithValue("@o", order);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                using var shared = conn.CreateCommand(); shared.Transaction = tran;
                if (mode.HasValue && !string.IsNullOrEmpty(tabId))
                {
                    shared.CommandText = "UPDATE SharedShortcutModeState SET SortOrder=@o WHERE Mode=@mode AND ShortcutId=@id AND TitleId=@tab";
                    shared.Parameters.AddWithValue("@mode", (int)mode.Value);
                    shared.Parameters.AddWithValue("@tab", tabId);
                }
                else
                    shared.CommandText = "UPDATE SharedShortcuts SET SortOrder=@o WHERE Id=@id";
                shared.Parameters.AddWithValue("@o", order); shared.Parameters.AddWithValue("@id", id); shared.ExecuteNonQuery();
            }

            tran.Commit();
        }
    }
}
