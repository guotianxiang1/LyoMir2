using System.Globalization;
using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr.Services
{
    internal static class NativeYbShopPurchaseStore
    {
        internal const int LingFuGoodsIndex = 113;
        internal const string LingFuGoodsName = "灵符";
        internal const string CreateConsumptionTableSql =
            "Create table if not exists gamedata.YBConsume ( " +
            "Idx int auto_increment Primary key, PTID Char(20) binary not null, " +
            "YBConsume int unsigned NOT NULL default 0," +
            "LastConsumeTime datetime not null default '0000-00-00 00:00:00'," +
            "Unique Key PTID_Index(PTID));";
        internal const string InsertSql =
            "Insert into gamelog.YBGoods_Buy_Log(" +
            "UpdateTime, PTID, UserID, CharName, GoodsIdx, GoodsName, " +
            "GoodsCount,UseCredit, Status, CurrentCredit) " +
            "Select Now(), @account, @userId, @characterName, @goodsIndex, " +
            "@goodsName, @goodsCount, @useCredit, \"Undetermined\", YBNUM " +
            "from gamedata.yb_user_data where UserID = @userId Limit 1;";
        internal const string SelectLastInsertIdSql =
            "Select LAST_INSERT_ID() as LastIdx Limit 1";
        internal const string SetTrueSql =
            "Update gamelog.YBGoods_Buy_Log set Status=\"True\" " +
            "where idx=@buyLogId and Status=\"Undetermined\";";
        internal const string SetFalseSql =
            "Update gamelog.YBGoods_Buy_Log set Status=\"False\" " +
            "where Status=\"Undetermined\" and idx=@buyLogId;";
        internal const string SelectConsumptionSql =
            "SELECT Idx FROM gamedata.YBConsume WHERE PTID=@account;";
        internal const string UpdateConsumptionSql =
            "UPDATE gamedata.YBConsume SET YBConsume=YBConsume+@amount, " +
            "LastConsumeTime=Now() WHERE PTID=@account;";
        internal const string InsertConsumptionSql =
            "INSERT INTO gamedata.YBConsume(PTID, YBConsume, LastConsumeTime) " +
            "VALUES (@account, @amount, Now());";

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
                command.CommandText = CreateConsumptionTableSql;
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static int Begin(long userId, string account,
            string characterName, int amount)
        {
            if (userId <= 0 || amount is < 1 or > 1000) return -1;
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString)) return -1;

            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = InsertSql;
                    AddRawParameter(insert, "@account",
                        EncodeShortString(account, 20));
                    insert.Parameters.Add("@userId", MySqlDbType.Int64).Value = userId;
                    AddRawParameter(insert, "@characterName",
                        EncodeShortString(characterName, 15));
                    insert.Parameters.Add("@goodsIndex", MySqlDbType.Int32).Value =
                        LingFuGoodsIndex;
                    AddRawParameter(insert, "@goodsName",
                        EncodeShortString(LingFuGoodsName, 15));
                    insert.Parameters.Add("@goodsCount", MySqlDbType.Int32).Value = amount;
                    insert.Parameters.Add("@useCredit", MySqlDbType.Int32).Value = amount;
                    if (insert.ExecuteNonQuery() != 1) return -1;
                }

                using var queryId = connection.CreateCommand();
                queryId.CommandText = SelectLastInsertIdSql;
                var value = queryId.ExecuteScalar();
                if (value == null || value == DBNull.Value) return -1;
                var buyLogId = Convert.ToInt32(value,
                    CultureInfo.InvariantCulture);
                return buyLogId > 0 ? buyLogId : -1;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage("[NativeYbShop] begin failed: " + ex.Message);
                return -1;
            }
        }

        internal static void SetStatusBestEffort(int buyLogId, bool success)
        {
            if (buyLogId <= 0) return;
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString)) return;
            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                using var update = connection.CreateCommand();
                update.CommandText = success ? SetTrueSql : SetFalseSql;
                update.Parameters.Add("@buyLogId", MySqlDbType.Int32).Value = buyLogId;
                update.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage("[NativeYbShop] finalize failed: " + ex.Message);
            }
        }

        internal static void AddConsumptionBestEffort(string account, int amount)
        {
            var accountBytes = EncodeShortString(account, 20);
            if (accountBytes == null || accountBytes.Length == 0 || amount <= 0) return;
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString)) return;
            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                var exists = false;
                using (var query = connection.CreateCommand())
                {
                    query.CommandText = SelectConsumptionSql;
                    AddRawParameter(query, "@account", accountBytes);
                    exists = query.ExecuteScalar() is not null and not DBNull;
                }

                using var write = connection.CreateCommand();
                write.CommandText = exists
                    ? UpdateConsumptionSql
                    : InsertConsumptionSql;
                AddRawParameter(write, "@account", accountBytes);
                write.Parameters.Add("@amount", MySqlDbType.Int32).Value = amount;
                write.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage("[NativeYbShop] YBConsume failed: " + ex.Message);
            }
        }

        private static byte[] EncodeShortString(string value, int maxBytes)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            return bytes.Length <= maxBytes ? bytes : bytes[..maxBytes];
        }

        private static void AddRawParameter(MySqlCommand command, string name,
            byte[] bytes)
        {
            command.Parameters.Add(name, MySqlDbType.VarBinary,
                Math.Max(1, bytes.Length)).Value = bytes;
        }
    }
}
