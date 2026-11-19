using BlockBox.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace BlockBox.Data
{
    public static class CategoryRepository
    {
        /// <summary>
        /// 读取当前模式下可见的分类（系统固定 + 当前模式的用户分类）
        /// 按 GroupIndex、SortOrder 排序
        /// </summary>
        public static List<Category> GetVisible(AppMode mode)
        {
            var list = new List<Category>();

            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
    SELECT Id, Name, IconGlyph, IsUserCreated, NULL AS BelongingMode, 'default' AS PartitionId, '默认' AS PartitionName, GroupIndex, SortOrder,
           ContentKind, ExtensionList, SortMode, 1 AS IsShared, 0 AS PartitionSortOrder
    FROM SharedCategories
    UNION ALL
    SELECT c.Id, c.Name, c.IconGlyph, c.IsUserCreated, c.BelongingMode, c.PartitionId,
           p.Name AS PartitionName, c.GroupIndex, c.SortOrder, c.ContentKind, c.ExtensionList,
           c.SortMode, 0 AS IsShared, COALESCE(p.SortOrder,c.GroupIndex) AS PartitionSortOrder
    FROM Categories c LEFT JOIN CategoryPartitions p ON p.Id=c.PartitionId
    WHERE c.BelongingMode = @m
    ORDER BY PartitionSortOrder, IsShared DESC, SortOrder";
            cmd.Parameters.AddWithValue("@m", (int)mode);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadCategory(reader));
            }

            return list;
        }

        /// <summary>
        /// 新增一个用户分类，自动算 SortOrder
        /// </summary>
        public static void Add(Category category)
        {
            using var conn = DatabaseContext.OpenConnection();

            if (category.IsShared)
            {
                if (category.SortOrder <= 0)
                {
                    using var order = conn.CreateCommand();
                    order.CommandText = "SELECT COALESCE(MAX(SortOrder),0)+1 FROM SharedCategories WHERE GroupIndex=@g";
                    order.Parameters.AddWithValue("@g", category.GroupIndex);
                    category.SortOrder = Convert.ToInt32(order.ExecuteScalar());
                }
                using var sharedCmd = conn.CreateCommand();
                sharedCmd.CommandText = @"INSERT INTO SharedCategories
                    (Id, Name, IconGlyph, IsUserCreated, GroupIndex, SortOrder, ContentKind, ExtensionList, SortMode)
                    VALUES (@id, @name, @icon, @user, @group, @sort, @kind, @ext, @sortMode)";
                sharedCmd.Parameters.AddWithValue("@id", category.Id);
                sharedCmd.Parameters.AddWithValue("@name", category.Name);
                sharedCmd.Parameters.AddWithValue("@icon", category.IconGlyph);
                sharedCmd.Parameters.AddWithValue("@user", category.IsUserCreated ? 1 : 0);
                sharedCmd.Parameters.AddWithValue("@group", category.GroupIndex);
                sharedCmd.Parameters.AddWithValue("@sort", category.SortOrder);
                sharedCmd.Parameters.AddWithValue("@kind", (int)category.ContentKind);
                sharedCmd.Parameters.AddWithValue("@ext", category.ExtensionList ?? "");
                sharedCmd.Parameters.AddWithValue("@sortMode", (int)category.SortMode);
                sharedCmd.ExecuteNonQuery();
                return;
            }

            // 没指定排序 → 追加到该分区末尾
            if (category.SortOrder <= 0)
            {
                category.SortOrder = GetMaxSortOrder(conn, category.GroupIndex, category.BelongingMode) + 1;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
    INSERT INTO Categories 
    (Id, Name, IconGlyph, IsUserCreated, BelongingMode, PartitionId, GroupIndex, SortOrder, ContentKind, ExtensionList, SortMode)
    VALUES (@id, @name, @icon, @user, @mode, @partition, @group, @sort, @kind, @ext, @sortMode)";
            cmd.Parameters.AddWithValue("@id", category.Id);
            cmd.Parameters.AddWithValue("@name", category.Name);
            cmd.Parameters.AddWithValue("@icon", category.IconGlyph);
            cmd.Parameters.AddWithValue("@user", category.IsUserCreated ? 1 : 0);
            cmd.Parameters.AddWithValue("@mode",
                category.BelongingMode.HasValue ? (object)(int)category.BelongingMode.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@partition", category.PartitionId ?? "");
            cmd.Parameters.AddWithValue("@group", category.GroupIndex);
            cmd.Parameters.AddWithValue("@sort", category.SortOrder);
            cmd.Parameters.AddWithValue("@kind", (int)category.ContentKind);
            cmd.Parameters.AddWithValue("@ext", category.ExtensionList ?? "");
            cmd.Parameters.AddWithValue("@sortMode", (int)category.SortMode);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 更新分类（名字 + 图标 + 分区）
        /// </summary>
        public static void Update(string id, string name, string icon, int groupIndex, string partitionId,
                          ContentKind kind, string extList, SortMode sortMode)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = IsShared(id) ? @"UPDATE SharedCategories
                        SET Name = @n, IconGlyph = @i, GroupIndex = @g,
                            ContentKind = @k, ExtensionList = @e, SortMode = @s
                        WHERE Id = @id" : @"UPDATE Categories 
                        SET Name = @n, IconGlyph = @i, PartitionId=@partition, GroupIndex = @g,
                            ContentKind = @k, ExtensionList = @e, SortMode = @s
                        WHERE Id = @id";
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@i", icon);
            cmd.Parameters.AddWithValue("@g", groupIndex);
            cmd.Parameters.AddWithValue("@partition", partitionId ?? "");
            cmd.Parameters.AddWithValue("@k", (int)kind);
            cmd.Parameters.AddWithValue("@e", extList ?? "");
            cmd.Parameters.AddWithValue("@s", (int)sortMode);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 删除用户分类（系统固定分类不能删，由调用方判断）
        /// </summary>
        public static void Delete(string id)
        {
            using var conn = DatabaseContext.OpenConnection();
            bool shared = IsShared(id);
            using var tran = conn.BeginTransaction();
            using (var items = conn.CreateCommand())
            {
                items.Transaction = tran;
                items.CommandText = shared
                    ? "DELETE FROM SharedShortcutModeState WHERE ShortcutId IN (SELECT Id FROM SharedShortcuts WHERE CategoryId=@id); DELETE FROM SharedShortcuts WHERE CategoryId=@id;"
                    : "DELETE FROM Shortcuts WHERE CategoryId=@id AND BelongingMode=(SELECT BelongingMode FROM Categories WHERE Id=@id);";
                items.Parameters.AddWithValue("@id", id);
                items.ExecuteNonQuery();
            }
            using (var category = conn.CreateCommand())
            {
                category.Transaction = tran;
                category.CommandText = shared ? "DELETE FROM SharedCategories WHERE Id=@id" : "DELETE FROM Categories WHERE Id=@id";
                category.Parameters.AddWithValue("@id", id); category.ExecuteNonQuery();
            }
            tran.Commit();
        }

        public static bool IsShared(string id)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM SharedCategories WHERE Id=@id)";
            cmd.Parameters.AddWithValue("@id", id);
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }

        public static List<(string Id, string Name, int SortOrder)> GetPartitions(AppMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id,Name,SortOrder FROM CategoryPartitions WHERE BelongingMode=@m ORDER BY SortOrder,Name";
            cmd.Parameters.AddWithValue("@m", (int)mode);
            using var reader = cmd.ExecuteReader();
            var result = new List<(string, string, int)>();
            while (reader.Read()) result.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
            return result;
        }

        public static string AddPartition(AppMode mode, string name)
        {
            using var conn = DatabaseContext.OpenConnection();
            string id = Guid.NewGuid().ToString();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO CategoryPartitions(Id,Name,BelongingMode,SortOrder) VALUES(@id,@name,@mode,(SELECT COALESCE(MAX(SortOrder),0)+1 FROM CategoryPartitions WHERE BelongingMode=@mode))";
            cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@name", name.Trim()); cmd.Parameters.AddWithValue("@mode", (int)mode);
            cmd.ExecuteNonQuery();
            return id;
        }

        public static void RenamePartition(AppMode mode, string partitionId, string name)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE CategoryPartitions SET Name=@name WHERE BelongingMode=@mode AND Id=@id";
            cmd.Parameters.AddWithValue("@name", name.Trim());
            cmd.Parameters.AddWithValue("@mode", (int)mode);
            cmd.Parameters.AddWithValue("@id", partitionId);
            cmd.ExecuteNonQuery();
        }

        public static int GetPartitionCategoryCount(AppMode mode, string partitionId)
        {
            using var conn = DatabaseContext.OpenConnection(); using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Categories WHERE BelongingMode=@mode AND PartitionId=@id";
            cmd.Parameters.AddWithValue("@mode", (int)mode); cmd.Parameters.AddWithValue("@id", partitionId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public static void DeletePartition(AppMode mode, string partitionId)
        {
            if (GetPartitionCategoryCount(mode, partitionId) != 0)
                throw new InvalidOperationException("请先移除该分区中的分类。");
            using var conn = DatabaseContext.OpenConnection(); using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CategoryPartitions WHERE BelongingMode=@mode AND Id=@id";
            cmd.Parameters.AddWithValue("@mode", (int)mode); cmd.Parameters.AddWithValue("@id", partitionId); cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 取当前模式下下一个可用的分区序号（用于"+ 新建分区"）
        /// </summary>
        public static int GetNextGroupIndex(AppMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT COALESCE(MAX(GroupIndex), 0) FROM Categories
                                WHERE BelongingMode = @m";
            cmd.Parameters.AddWithValue("@m", (int)mode);
            return Convert.ToInt32(cmd.ExecuteScalar()) + 1;
        }
        public static void UpdateSortMode(string id, SortMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = IsShared(id)
                ? "UPDATE SharedCategories SET SortMode=@s WHERE Id=@id"
                : "UPDATE Categories SET SortMode = @s WHERE Id = @id";
            cmd.Parameters.AddWithValue("@s", (int)mode);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        // ==================== 私有工具 ====================

        private static int GetMaxSortOrder(SqliteConnection conn, int groupIndex, AppMode? mode)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT COALESCE(MAX(SortOrder), 0) FROM Categories 
                                WHERE GroupIndex = @g 
                                AND (BelongingMode IS NULL OR BelongingMode = @m)";
            cmd.Parameters.AddWithValue("@g", groupIndex);
            cmd.Parameters.AddWithValue("@m",
                mode.HasValue ? (object)(int)mode.Value : DBNull.Value);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private static Category ReadCategory(SqliteDataReader reader)
        {
            return new Category
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                IconGlyph = reader.GetString(2),
                IsUserCreated = reader.GetInt32(3) == 1,
                BelongingMode = reader.IsDBNull(4) ? null : (AppMode)reader.GetInt32(4),
                PartitionId = reader.GetString(reader.GetOrdinal("PartitionId")),
                PartitionName = reader.IsDBNull(reader.GetOrdinal("PartitionName")) ? "未命名分区" : reader.GetString(reader.GetOrdinal("PartitionName")),
                GroupIndex = reader.GetInt32(reader.GetOrdinal("GroupIndex")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                ContentKind = (ContentKind)reader.GetInt32(reader.GetOrdinal("ContentKind")),
                ExtensionList = reader.GetString(reader.GetOrdinal("ExtensionList")),
                SortMode = (SortMode)reader.GetInt32(reader.GetOrdinal("SortMode")),
                IsShared = reader.GetInt32(reader.GetOrdinal("IsShared")) == 1,
            };
        }
    }
}
