using System.Globalization;
using MySql.Data.MySqlClient;

namespace GameSvr.Services
{
    internal static class NativePasYbPurchaseStore
    {
        internal const string InsertSql =
            "Insert into gamelog.YB_Script_Buy_Log(" +
            "UpdateTime, PTID, UserID, CharName, CostType, PsBkFuncName, " +
            "CostNum,UseCredit, Status, CurrentCredit) " +
            "Select Now(), @account, @userId, @characterName, @costType, " +
            "@callback, @costNum, @useCredit, \"Undetermined\", YBNUM " +
            "from gamedata.yb_user_data where UserID = @userId Limit 1;";
        internal const string SelectLastInsertIdSql =
            "Select LAST_INSERT_ID() as LastIdx Limit 1;";
        internal const string SetTrueSql =
            "Update gamelog.YB_Script_Buy_Log set Status=\"True\" " +
            "where idx=@scriptLogId and Status=\"Undetermined\";";

        internal static int Begin(NativePasYbPurchase purchase)
        {
            if (purchase == null || purchase.UserId <= 0) return -1;
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString)) return -1;

            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = InsertSql;
                    AddRawParameter(insert, "@account", purchase.AccountBytes);
                    insert.Parameters.Add("@userId", MySqlDbType.Int64).Value =
                        purchase.UserId;
                    AddRawParameter(insert, "@characterName",
                        purchase.CharacterNameBytes);
                    insert.Parameters.Add("@costType", MySqlDbType.Int32).Value =
                        purchase.VsId;
                    AddRawParameter(insert, "@callback", purchase.CallbackBytes);
                    insert.Parameters.Add("@costNum", MySqlDbType.Int32).Value =
                        purchase.Quantity;
                    insert.Parameters.Add("@useCredit", MySqlDbType.Int32).Value =
                        purchase.TotalCost;
                    if (insert.ExecuteNonQuery() != 1) return -1;
                }

                using var queryId = connection.CreateCommand();
                queryId.CommandText = SelectLastInsertIdSql;
                var value = queryId.ExecuteScalar();
                if (value == null || value == DBNull.Value) return -1;
                var scriptLogId = Convert.ToInt32(value,
                    CultureInfo.InvariantCulture);
                return scriptLogId > 0 ? scriptLogId : -1;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[NativePasYbPurchase] SQL begin failed: " + ex.Message);
                return -1;
            }
        }

        internal static void SetTrueBestEffort(int scriptLogId)
        {
            if (scriptLogId <= 0) return;
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString)) return;
            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                using var update = connection.CreateCommand();
                update.CommandText = SetTrueSql;
                update.Parameters.Add("@scriptLogId", MySqlDbType.Int32).Value =
                    scriptLogId;
                update.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[NativePasYbPurchase] SQL finalize failed: " + ex.Message);
            }
        }

        private static void AddRawParameter(MySqlCommand command, string name,
            byte[] bytes)
        {
            bytes ??= Array.Empty<byte>();
            command.Parameters.Add(name, MySqlDbType.VarBinary,
                Math.Max(1, bytes.Length)).Value = bytes;
        }
    }
}
