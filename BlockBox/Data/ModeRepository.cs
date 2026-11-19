using BlockBox.Helpers;
using BlockBox.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlockBox.Data
{
    public static class ModeRepository
    {
        public static List<ModeDefinition> GetAll()
        {
            var modes = new List<ModeDefinition>();
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id,Name,IsBuiltIn FROM Modes ORDER BY Id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                modes.Add(new ModeDefinition
                {
                    Mode = (AppMode)reader.GetInt32(0),
                    Name = reader.GetString(1),
                    IsBuiltIn = reader.GetInt32(2) != 0
                });
            }
            return modes;
        }

        public static List<AppMode> GetAllModes()
            => GetAll().ConvertAll(mode => mode.Mode);

        public static string GetDisplayName(AppMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name,IsBuiltIn FROM Modes WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", (int)mode);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return mode.ToString();
            string name = reader.GetString(0);
            return reader.GetInt32(1) != 0 ? LanguageService.Translate(name) : name;
        }

        public static AppMode Create(string name)
        {
            string normalizedName = name?.Trim() ?? "";
            if (normalizedName.Length == 0)
                throw new ArgumentException(LanguageService.Translate("视图名称不能为空"), nameof(name));
            if (normalizedName.Length > 32)
                throw new ArgumentException(LanguageService.Translate("视图名称过长"), nameof(name));

            using var conn = DatabaseContext.OpenConnection();
            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT EXISTS(SELECT 1 FROM Modes WHERE Name=@name COLLATE NOCASE)";
                check.Parameters.AddWithValue("@name", normalizedName);
                if (Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                    throw new ArgumentException(LanguageService.Translate("同名视图已存在"), nameof(name));
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Modes(Name,IsBuiltIn) VALUES(@name,0)";
            cmd.Parameters.AddWithValue("@name", normalizedName);
            cmd.ExecuteNonQuery();
            using var idCommand = conn.CreateCommand();
            idCommand.CommandText = "SELECT last_insert_rowid()";
            long id = Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (id > int.MaxValue)
                throw new InvalidOperationException("模式 ID 超出支持范围。");
            return (AppMode)(int)id;
        }

        public static bool Delete(AppMode mode)
        {
            using var conn = DatabaseContext.OpenConnection();
            using var transaction = conn.BeginTransaction();

            using (var check = conn.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT IsBuiltIn FROM Modes WHERE Id=@mode";
                check.Parameters.AddWithValue("@mode", (int)mode);
                var isBuiltIn = check.ExecuteScalar();
                if (isBuiltIn == null || Convert.ToInt32(isBuiltIn, CultureInfo.InvariantCulture) != 0)
                    return false;
            }

            foreach (string commandText in new[]
            {
                "DELETE FROM Shortcuts WHERE BelongingMode=@mode",
                "DELETE FROM SharedShortcutModeState WHERE Mode=@mode",
                "DELETE FROM Categories WHERE BelongingMode=@mode",
                "DELETE FROM Tabs WHERE BelongingMode=@mode",
                "DELETE FROM CategoryPartitions WHERE BelongingMode=@mode",
                "DELETE FROM ModeFilters WHERE Mode=@mode",
                "DELETE FROM Modes WHERE Id=@mode AND IsBuiltIn=0"
            })
            {
                using var command = conn.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = commandText;
                command.Parameters.AddWithValue("@mode", (int)mode);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }
    }
}
