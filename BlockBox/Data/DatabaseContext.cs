using BlockBox.Models;
using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace BlockBox.Data
{
    public static class DatabaseContext
    {
        // 数据库文件路径：LocalFolder/blockbox.db
        public static string DbPath { get; private set; } = string.Empty;

        // 连接字符串（供各个 Repository 使用）
        public static string ConnectionString { get; private set; } = string.Empty;

        /// <summary>
        /// 初始化数据库：
        /// 1. 计算路径
        /// 2. 建表（不存在才建）
        /// 3. 插入种子数据（首次运行时）
        /// </summary>
        public static void Initialize()
        {
            // 1. 路径
            string baseDir = BlockBox.Helpers.AppDataPaths.Root;
            string dataDir = Path.Combine(baseDir, "Data");
            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);

            DbPath = Path.Combine(dataDir, "blockbox.db");
            ConnectionString = $"Data Source={DbPath}";

            // 2. 建表
            BlockBox.Helpers.StartupLog.Step("DB: CreateTables 开始");
            CreateTables();
            BlockBox.Helpers.StartupLog.Step("DB: CreateTables 完成");

            // 修复已确认失效的旧版 Microsoft Copilot 目标：
            // 找到新版本则更新路径，找不到才清理旧记录。
            const string staleCopilotPath =
                @"C:\Program Files (x86)\Microsoft\Copilot\Application\154.0.4258.37\mscopilot.exe";
            BlockBox.Helpers.StartupLog.Step("DB: 修复 Copilot 记录开始");
            int repairedCopilotRecords =
                ShortcutRepository.RepairOrRemoveCopilotTarget(staleCopilotPath);
            BlockBox.Helpers.StartupLog.Step("DB: 修复 Copilot 记录完成");
            if (repairedCopilotRecords > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DB] 已修复或清理失效 Copilot 记录: {repairedCopilotRecords}");
            }

            // 3. 种子数据
            BlockBox.Helpers.StartupLog.Step("DB: SeedData 开始");
            SeedData();
            BlockBox.Helpers.StartupLog.Step("DB: SeedData 完成");
            BlockBox.Helpers.StartupLog.Step("DB: SeedModeFilters 开始");
            SeedModeFilters();
            BlockBox.Helpers.StartupLog.Step("DB: SeedModeFilters 完成");

            System.Diagnostics.Debug.WriteLine($"[DB] 初始化完成: {DbPath}");
        }

        /// <summary>
        /// 打开一个新连接（每次用完要 Dispose）
        /// </summary>
        public static SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            // 开启外键约束（SQLite 默认是关的，每个连接都要开）
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();

            return conn;
        }

        // ==================== 建表 ====================
        private static void CreateTables()
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"

CREATE TABLE IF NOT EXISTS ExcludedPrograms (
    TargetPath TEXT PRIMARY KEY COLLATE NOCASE,
    Name TEXT NOT NULL,
    IconPath TEXT NOT NULL DEFAULT ''
);

-- 分类
CREATE TABLE IF NOT EXISTS Categories (
    Id             TEXT PRIMARY KEY,
    Name           TEXT NOT NULL,
    IconGlyph      TEXT NOT NULL DEFAULT '\uE71D',
    IsUserCreated  INTEGER NOT NULL DEFAULT 0,
    BelongingMode  INTEGER NOT NULL,
    PartitionId    TEXT NOT NULL DEFAULT '',
    GroupIndex     INTEGER NOT NULL DEFAULT 0,
    SortOrder      INTEGER NOT NULL DEFAULT 0,
    ContentKind    INTEGER NOT NULL DEFAULT 0,
    ExtensionList  TEXT NOT NULL DEFAULT '',
    SortMode       INTEGER NOT NULL DEFAULT 2
);
CREATE INDEX IF NOT EXISTS IX_Categories_Group ON Categories(GroupIndex);
CREATE INDEX IF NOT EXISTS IX_Categories_Mode  ON Categories(BelongingMode);

-- 全局共享分类（与模式分类分表）
CREATE TABLE IF NOT EXISTS SharedCategories (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL UNIQUE COLLATE NOCASE,
    IconGlyph TEXT NOT NULL DEFAULT '\uE71D',
    IsUserCreated INTEGER NOT NULL DEFAULT 0,
    GroupIndex INTEGER NOT NULL DEFAULT 0,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    ContentKind INTEGER NOT NULL DEFAULT 0,
    ExtensionList TEXT NOT NULL DEFAULT '',
    SortMode INTEGER NOT NULL DEFAULT 2
);

-- 用户分区按模式隔离；固定默认分区仅用于共享分类，不可删除。
CREATE TABLE IF NOT EXISTS CategoryPartitions (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    BelongingMode INTEGER NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    UNIQUE(BelongingMode, Name COLLATE NOCASE)
);

-- 标签
CREATE TABLE IF NOT EXISTS Tabs (
    Id             TEXT PRIMARY KEY,
    Title          TEXT NOT NULL,
    IconGlyph      TEXT NOT NULL DEFAULT '\uE8A5',
    IsClosable     INTEGER NOT NULL DEFAULT 1,
    BelongingMode  INTEGER NOT NULL DEFAULT 2,
    SortOrder      INTEGER NOT NULL DEFAULT 0,
    ContentKind    INTEGER NOT NULL DEFAULT 0,
    ExtensionList  TEXT NOT NULL DEFAULT '',
    Note           TEXT NOT NULL DEFAULT '',    
    IsPinned       INTEGER NOT NULL DEFAULT 1,
    SortMode       INTEGER NOT NULL DEFAULT 2
);
CREATE INDEX IF NOT EXISTS IX_Tabs_Mode ON Tabs(BelongingMode);

-- 快捷方式
CREATE TABLE IF NOT EXISTS Shortcuts (
    Id                TEXT PRIMARY KEY,
    Name              TEXT NOT NULL,
    TargetPath        TEXT NOT NULL,
    Arguments         TEXT NOT NULL DEFAULT '',
    WorkingDirectory  TEXT NOT NULL DEFAULT '',
    IconPath          TEXT NOT NULL DEFAULT '',
    CategoryId        TEXT NOT NULL DEFAULT '',
    TitleId           TEXT NOT NULL DEFAULT '',
    IsFile            INTEGER NOT NULL DEFAULT 0,
    BelongingMode     INTEGER NOT NULL DEFAULT 2,
    SortOrder         INTEGER NOT NULL DEFAULT 0,
    IsFavorite        INTEGER NOT NULL DEFAULT 0,
    FavoriteTime      TEXT,
    LastAccessedTime  TEXT,
    AccessCount       INTEGER NOT NULL DEFAULT 0,
    BackupPath        TEXT NOT NULL DEFAULT '',
    SourceMd5         TEXT NOT NULL DEFAULT '',
    SourceState       INTEGER NOT NULL DEFAULT 0,
    LastVerifiedTime  TEXT,
    IsBackedUp        INTEGER NOT NULL DEFAULT 1,
    CreatedTime       TEXT NOT NULL DEFAULT '',
    UNIQUE (BelongingMode, CategoryId, TargetPath COLLATE NOCASE)
);
CREATE INDEX IF NOT EXISTS IX_Shortcuts_Category ON Shortcuts(CategoryId);
CREATE INDEX IF NOT EXISTS IX_Shortcuts_Title    ON Shortcuts(TitleId);
CREATE INDEX IF NOT EXISTS IX_Shortcuts_Mode     ON Shortcuts(BelongingMode);
CREATE INDEX IF NOT EXISTS IX_Shortcuts_Favorite ON Shortcuts(IsFavorite);
CREATE INDEX IF NOT EXISTS IX_Shortcuts_Access   ON Shortcuts(LastAccessedTime);

-- 全局共享快捷方式：故意不包含 BelongingMode
CREATE TABLE IF NOT EXISTS SharedShortcuts (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    TargetPath TEXT NOT NULL,
    Arguments TEXT NOT NULL DEFAULT '',
    WorkingDirectory TEXT NOT NULL DEFAULT '',
    IconPath TEXT NOT NULL DEFAULT '',
    CategoryId TEXT NOT NULL,
    IsFile INTEGER NOT NULL DEFAULT 0,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    BackupPath TEXT NOT NULL DEFAULT '',
    SourceMd5 TEXT NOT NULL DEFAULT '',
    SourceState INTEGER NOT NULL DEFAULT 0,
    LastVerifiedTime TEXT,
    IsBackedUp INTEGER NOT NULL DEFAULT 1,
    CreatedTime TEXT NOT NULL DEFAULT '',
    UNIQUE (CategoryId, TargetPath COLLATE NOCASE)
);
CREATE INDEX IF NOT EXISTS IX_SharedShortcuts_Category ON SharedShortcuts(CategoryId);
CREATE INDEX IF NOT EXISTS IX_SharedShortcuts_Target ON SharedShortcuts(TargetPath);

-- 共享记录的标签、收藏和最近访问均按模式单独记录；普通模式记录仍使用 Shortcuts 自身字段。
CREATE TABLE IF NOT EXISTS SharedShortcutModeState (
    Mode INTEGER NOT NULL,
    ShortcutId TEXT NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    TitleId TEXT NOT NULL DEFAULT '',
    IsFavorite INTEGER NOT NULL DEFAULT 0,
    FavoriteTime TEXT,
    LastAccessedTime TEXT,
    AccessCount INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (Mode, ShortcutId)
);
CREATE INDEX IF NOT EXISTS IX_SharedShortcutModeState_Favorite ON SharedShortcutModeState(Mode, IsFavorite);
CREATE INDEX IF NOT EXISTS IX_SharedShortcutModeState_Access ON SharedShortcutModeState(Mode, LastAccessedTime);

-- 模式过滤
CREATE TABLE IF NOT EXISTS ModeFilters (
    Id        TEXT PRIMARY KEY,
    Mode      INTEGER NOT NULL,
    Pattern   TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS IX_ModeFilters_Mode ON ModeFilters(Mode);

-- 应用模式；内置模式继续使用既有整数 ID
CREATE TABLE IF NOT EXISTS Modes (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name      TEXT NOT NULL UNIQUE COLLATE NOCASE,
    IsBuiltIn INTEGER NOT NULL DEFAULT 0
);
INSERT OR IGNORE INTO Modes(Id,Name,IsBuiltIn) VALUES(0,'工作模式',1);
INSERT OR IGNORE INTO Modes(Id,Name,IsBuiltIn) VALUES(1,'游戏模式',1);
INSERT OR IGNORE INTO Modes(Id,Name,IsBuiltIn) VALUES(2,'常规模式',1);

-- 已删除的快捷方式记录，可从回收站恢复
CREATE TABLE IF NOT EXISTS RecycleBin (
    Id          TEXT PRIMARY KEY,
    Payload     TEXT NOT NULL,
    DeletedAt   TEXT NOT NULL,
    Name        TEXT NOT NULL,
    TargetPath  TEXT NOT NULL,
    BackupPath  TEXT NOT NULL DEFAULT '',
    CategoryId  TEXT NOT NULL,
    BelongingMode INTEGER NOT NULL,
    IsShared    INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_RecycleBin_DeletedAt ON RecycleBin(DeletedAt);
";
            cmd.ExecuteNonQuery();
        }

        // ==================== 种子数据 ====================
        private static void SeedData()
        {
            using var conn = OpenConnection();

            using var tran = conn.BeginTransaction();

            try
            {
                // 新版共享类别由 SharedCategories 单独持有。
                EnsureSharedCategory(conn, tran, "全部应用", "\uE71D", 0);
                EnsureSharedCategory(conn, tran, "游戏", "\uE7FC", 1);
                EnsureSharedCategory(conn, tran, "音乐", "\uE8D6", 2);
                EnsureSharedCategory(conn, tran, "办公", "\uE8A5", 3);

                tran.Commit();
                System.Diagnostics.Debug.WriteLine("[DB] 种子数据已写入");
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }
        private static void SeedModeFilters()
        {
            using var conn = OpenConnection();

            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(*) FROM ModeFilters";
                long count = (long)check.ExecuteScalar()!;
                if (count > 0) return;   // 已有数据
            }

            using var tran = conn.BeginTransaction();
            try
            {
                // 游戏模式：过滤掉常见办公软件
                var gameFilters = new[]
                {
            "Word", "Excel", "PowerPoint", "PowerPnt",
            "WPS", "Office", "Outlook", "OneNote", "Teams"
        };

                foreach (var pattern in gameFilters)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tran;
                    cmd.CommandText = @"INSERT INTO ModeFilters (Id, Mode, Pattern)
                                VALUES (@id, @mode, @pattern)";
                    cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                    cmd.Parameters.AddWithValue("@mode", (int)AppMode.Game);
                    cmd.Parameters.AddWithValue("@pattern", pattern);
                    cmd.ExecuteNonQuery();
                }

                tran.Commit();
                System.Diagnostics.Debug.WriteLine("[DB] ModeFilters 种子已写入");
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }
        private static void InsertCategory(SqliteConnection conn, SqliteTransaction tran,
            string name, string icon, bool isUserCreated, AppMode? mode, int groupIndex, int sortOrder)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tran;
            cmd.CommandText = @"INSERT INTO Categories 
                        (Id, Name, IconGlyph, IsUserCreated, BelongingMode, PartitionId, GroupIndex, SortOrder)
                        VALUES (@id, @name, @icon, @user, @mode, '', @group, @sort)";
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@icon", icon);
            cmd.Parameters.AddWithValue("@user", isUserCreated ? 1 : 0);
            cmd.Parameters.AddWithValue("@mode", mode.HasValue ? (object)(int)mode.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@group", groupIndex);
            cmd.Parameters.AddWithValue("@sort", sortOrder);
            cmd.ExecuteNonQuery();
        }

        private static void EnsureSharedCategory(SqliteConnection conn, SqliteTransaction tran,
            string name, string icon, int sortOrder)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tran;
            cmd.CommandText = @"INSERT OR IGNORE INTO SharedCategories
                (Id, Name, IconGlyph, IsUserCreated, GroupIndex, SortOrder, ContentKind)
                VALUES (@id, @name, @icon, 0, 0, @sort, @kind)";
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@icon", icon);
            cmd.Parameters.AddWithValue("@sort", sortOrder);
            cmd.Parameters.AddWithValue("@kind", (int)ContentKind.Application);
            cmd.ExecuteNonQuery();
            using var update = conn.CreateCommand(); update.Transaction = tran;
            update.CommandText = "UPDATE SharedCategories SET IconGlyph=@icon,GroupIndex=0,SortOrder=@sort,ContentKind=@kind WHERE Name=@name COLLATE NOCASE";
            update.Parameters.AddWithValue("@icon", icon); update.Parameters.AddWithValue("@sort", sortOrder);
            update.Parameters.AddWithValue("@kind", (int)ContentKind.Application); update.Parameters.AddWithValue("@name", name);
            update.ExecuteNonQuery();
        }
    }
}
