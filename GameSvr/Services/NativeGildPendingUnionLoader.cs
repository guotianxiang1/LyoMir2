using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr.Services
{
    internal static class NativeGildPendingUnionLoader
    {
        internal static bool TryLoadPendingUnions(
            string connectionString,
            NativeGildRequestLedger ledger,
            IReadOnlyDictionary<long, NativeGildSnapshot> gildById,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(connectionString))
            {
                error = "connection string is null or empty";
                return false;
            }

            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT GildID1,GildID2,CreateTime " +
                    "FROM gamedata.GildRelation " +
                    "WHERE Relation=3 " +
                    "ORDER BY CreateTime";

                using var reader = command.ExecuteReader();
                var loadedCount = 0;

                while (reader.Read())
                {
                    var requestingGildId = ReadId(reader, 0);
                    var targetGildId = ReadId(reader, 1);
                    var createTime = reader.GetDateTime(2);

                    if (requestingGildId == 0 || targetGildId == 0
                        || !gildById.ContainsKey(requestingGildId)
                        || !gildById.ContainsKey(targetGildId))
                    {
                        continue;
                    }

                    var request = new NativeGildPendingRequest
                    {
                        UniqueId = ledger.NextUniqueId(),
                        RequestId = 0,
                        SecondaryKey = requestingGildId,
                        TargetKey = targetGildId,
                        Kind = NativeGildRequestKind.Union,
                        CreatedTime = createTime,
                        UsesSecondaryKey = true
                    };

                    var addResult = ledger.Add(request);
                    if (addResult == NativeGildRequestLedger.DuplicateCode)
                    {
                        continue;
                    }

                    loadedCount++;
                }

                if (loadedCount > 0)
                {
                    M2Share.MainOutMessage(
                        $"Loaded {loadedCount} pending guild union proposal(s) from database",
                        messageColor: ConsoleColor.Green);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "failed to load pending union requests: " + ex.Message;
                return false;
            }
        }

        private static long ReadId(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return 0;
            return unchecked((long)Convert.ToUInt64(reader.GetValue(ordinal)));
        }
    }
}
