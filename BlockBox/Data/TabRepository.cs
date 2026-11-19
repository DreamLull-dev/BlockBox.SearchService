using BlockBox.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace BlockBox.Data
{
    public static class TabRepository
    {
        public static List<MyTabItem> GetVisible(AppMode mode)
        {
            var list = new List<MyTabItem>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT Id, Title, IconGlyph, IsClosable, BelongingMode, SortOrder,
                       ContentKind, ExtensionList, Note, IsPinned, SortMode
                FROM Tabs
                WHERE BelongingMode = @m
                ORDER BY SortOrder";
            cmd.Parameters.AddWithValue("@m", (int)mode);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(Read(reader));

            return list;
        }

        public static void Add(MyTabItem tab)
        {
            using var conn = DatabaseContext.OpenConnection();

            if (tab.SortOrder <= 0)
            {
                using var q = conn.CreateCommand();
                q.CommandText = "SELECT COALESCE(MAX(SortOrder), 0) FROM Tabs WHERE BelongingMode = @m";
                q.Parameters.AddWithValue("@m", (int)tab.BelongingMode);
                tab.SortOrder = Convert.ToInt32(q.ExecuteScalar()) + 1;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Tabs
                (Id, Title, IconGlyph, IsClosable, BelongingMode, SortOrder,
                 ContentKind, ExtensionList, Note, IsPinned, SortMode)
                VALUES
                (@id, @title, @icon, @close, @mode, @sort,
                 @kind, @ext, @note, @pinned, @sortMode)";
            cmd.Parameters.AddWithValue("@id", tab.Id);
            cmd.Parameters.AddWithValue("@title", tab.Title);
            cmd.Parameters.AddWithValue("@icon", tab.IconGlyph);
            cmd.Parameters.AddWithValue("@close", tab.IsClosable ? 1 : 0);
            cmd.Parameters.AddWithValue("@mode", (int)tab.BelongingMode);
            cmd.Parameters.AddWithValue("@sort", tab.SortOrder);
            cmd.Parameters.AddWithValue("@kind", (int)tab.ContentKind);
            cmd.Parameters.AddWithValue("@ext", tab.ExtensionList ?? "");
            cmd.Parameters.AddWithValue("@note", tab.Note ?? "");
            cmd.Parameters.AddWithValue("@pinned", tab.IsPinned ? 1 : 0);
            cmd.Parameters.AddWithValue("@sortMode", (int)tab.SortMode);
            cmd.ExecuteNonQuery();
        }

        public static void Update(string id, string title, string note, string iconGlyph,
            ContentKind contentKind, string extensionList)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE Tabs 
                                SET Title = @t, Note = @n, IconGlyph = @i,
                                    ContentKind = @kind, ExtensionList = @ext
                                WHERE Id = @id";
            cmd.Parameters.AddWithValue("@t", title);
            cmd.Parameters.AddWithValue("@n", note ?? "");
            cmd.Parameters.AddWithValue("@i", iconGlyph);
            cmd.Parameters.AddWithValue("@kind", (int)contentKind);
            cmd.Parameters.AddWithValue("@ext", extensionList ?? "");
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        public static void UpdateSortMode(string id, SortMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Tabs SET SortMode = @s WHERE Id = @id";
            cmd.Parameters.AddWithValue("@s", (int)mode);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static void UpdateVisibleSortOrders(AppMode mode, IReadOnlyList<string> orderedIds)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var transaction = conn.BeginTransaction();

            var allIds = new List<string>();
            using (var query = conn.CreateCommand())
            {
                query.Transaction = transaction;
                query.CommandText = "SELECT Id FROM Tabs WHERE BelongingMode=@mode ORDER BY SortOrder, Id";
                query.Parameters.AddWithValue("@mode", (int)mode);
                using var reader = query.ExecuteReader();
                while (reader.Read())
                    allIds.Add(reader.GetString(0));
            }

            var reorderedIds = new List<string>(allIds.Count);
            var includedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in orderedIds)
            {
                if (allIds.Contains(id) && includedIds.Add(id))
                    reorderedIds.Add(id);
            }

            foreach (var id in allIds)
            {
                if (includedIds.Add(id))
                    reorderedIds.Add(id);
            }

            using (var update = conn.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE Tabs SET SortOrder=@order WHERE Id=@id AND BelongingMode=@mode";
                var orderParameter = update.Parameters.Add("@order", Microsoft.Data.Sqlite.SqliteType.Integer);
                var idParameter = update.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Text);
                update.Parameters.AddWithValue("@mode", (int)mode);

                for (int i = 0; i < reorderedIds.Count; i++)
                {
                    orderParameter.Value = i + 1;
                    idParameter.Value = reorderedIds[i];
                    update.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }

        public static void SetPinned(string id, bool pinned)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Tabs SET IsPinned = @p WHERE Id = @id";
            cmd.Parameters.AddWithValue("@p", pinned ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static void Delete(string id)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var state = conn.CreateCommand();
            state.CommandText = "UPDATE SharedShortcutModeState SET TitleId='' WHERE TitleId=@id";
            state.Parameters.AddWithValue("@id", id);
            state.ExecuteNonQuery();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Tabs WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 启动时清理：名字是"新标签"、没有备注、也没被任何 Shortcut 引用的标签
        /// </summary>
        public static void CleanEmptyDefaultTabs()
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM Tabs
                WHERE Title = '新标签'
                  AND (Note IS NULL OR Note = '')
                  AND Id NOT IN (SELECT DISTINCT TitleId FROM Shortcuts WHERE TitleId != '')
                  AND Id NOT IN (SELECT DISTINCT TitleId FROM SharedShortcutModeState WHERE TitleId != '')";
            int rows = cmd.ExecuteNonQuery();
            System.Diagnostics.Debug.WriteLine($"[清理] 删除 {rows} 个空默认标签");
        }

        // ==================== 私有 ====================

        private static MyTabItem Read(SqliteDataReader r)
        {
            return new MyTabItem
            {
                Id = r.GetString(r.GetOrdinal("Id")),
                Title = r.GetString(r.GetOrdinal("Title")),
                IconGlyph = r.GetString(r.GetOrdinal("IconGlyph")),
                IsClosable = r.GetInt32(r.GetOrdinal("IsClosable")) == 1,
                BelongingMode = (AppMode)r.GetInt32(r.GetOrdinal("BelongingMode")),
                SortOrder = r.GetInt32(r.GetOrdinal("SortOrder")),
                ContentKind = (ContentKind)r.GetInt32(r.GetOrdinal("ContentKind")),
                ExtensionList = r.GetString(r.GetOrdinal("ExtensionList")),
                Note = r.GetString(r.GetOrdinal("Note")),
                IsPinned = r.GetInt32(r.GetOrdinal("IsPinned")) == 1,
                SortMode = (SortMode)r.GetInt32(r.GetOrdinal("SortMode"))
            };
        }
    }
}
