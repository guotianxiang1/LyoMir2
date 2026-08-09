using System.Globalization;
using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr.Services
{
    internal static class NativeGloryLogManager
    {
        internal const string CreateTableSql =
            "Create Table if not Exists gamedata.GloryLog(" +
            "Idx Int AUTO_INCREMENT PRIMARY KEY," +
            "costId int NOT NULL," +
            "costDate Date not NULL," +
            "value int default 0," +
            "UNIQUE KEY uniKey (costId, costDate));";
        internal const string SelectSql =
            "select idx, value from gamedata.GloryLog " +
            "where costId=@costId and costDate=now();";
        internal const string UpdateSql =
            "Update gamedata.GloryLog set costId=@costId, value=@value " +
            "where idx=@idx;";
        internal const string InsertSql =
            "Insert Into gamedata.GloryLog(costId, costDate, value) " +
            "values(@costId, now(), @value);";

        private static readonly object SyncRoot = new();
        private static readonly Dictionary<int, int> Pending = new(1023);
        private static bool _dirty;
        private static int _lastFlushTick;

        internal static bool EnsureNativeSchema(out string error)
        {
            error = string.Empty;
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                error = "database connection string is empty";
                return false;
            }

            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = CreateTableSql;
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static void Record(int costId, int amount)
        {
            lock (SyncRoot)
            {
                Pending.TryGetValue(costId, out var current);
                Pending[costId] = unchecked(current + amount);
                _dirty = true;
            }
        }

        internal static void Run(int currentTick)
        {
            if (unchecked((uint)(currentTick - _lastFlushTick)) <= 10000u)
                return;
            _lastFlushTick = currentTick;
            Flush();
        }

        internal static bool Flush()
        {
            Dictionary<int, int> batch;
            lock (SyncRoot)
            {
                if (!_dirty) return true;
                _dirty = false;
                batch = new Dictionary<int, int>(Pending);
                foreach (var costId in batch.Keys)
                    Pending[costId] = 0;
            }

            if (!batch.Values.Any(value => value > 0)) return true;
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                ReportError("database connection string is empty");
                return false;
            }

            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                var success = true;
                foreach (var entry in batch)
                {
                    if (entry.Value <= 0) continue;
                    try
                    {
                        FlushEntry(connection, entry.Key, entry.Value);
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        ReportError(ex.Message);
                    }
                }
                return success;
            }
            catch (Exception ex)
            {
                ReportError(ex.Message);
                return false;
            }
        }

        private static void FlushEntry(MySqlConnection connection, int costId,
            int amount)
        {
            var rowCount = 0;
            var index = 0;
            var value = 0;
            using (var select = connection.CreateCommand())
            {
                select.CommandText = SelectSql;
                select.Parameters.Add("@costId", MySqlDbType.Int32).Value = costId;
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    rowCount++;
                    if (rowCount != 1) continue;
                    index = Convert.ToInt32(reader.GetValue(0),
                        CultureInfo.InvariantCulture);
                    value = Convert.ToInt32(reader.GetValue(1),
                        CultureInfo.InvariantCulture);
                }
            }

            if (rowCount == 1)
            {
                using var update = connection.CreateCommand();
                update.CommandText = UpdateSql;
                update.Parameters.Add("@costId", MySqlDbType.Int32).Value = costId;
                update.Parameters.Add("@value", MySqlDbType.Int32).Value =
                    unchecked(value + amount);
                update.Parameters.Add("@idx", MySqlDbType.Int32).Value = index;
                update.ExecuteNonQuery();
                return;
            }

            using var insert = connection.CreateCommand();
            insert.CommandText = InsertSql;
            insert.Parameters.Add("@costId", MySqlDbType.Int32).Value = costId;
            insert.Parameters.Add("@value", MySqlDbType.Int32).Value = amount;
            insert.ExecuteNonQuery();
        }

        private static void ReportError(string message)
        {
            M2Share.ErrorMessage("[exception]: 保存荣耀点消耗日志出错：" + message);
        }
    }
}
