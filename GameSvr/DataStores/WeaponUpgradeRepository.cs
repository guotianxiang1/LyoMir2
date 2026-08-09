using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr
{
    internal sealed class WeaponUpgradeRecord
    {
        internal int Idx;
        internal int ItemIdx;
        internal uint ItemId;
        internal byte UpDc;
        internal byte UpSc;
        internal byte UpMc;
        internal byte UpCc;
        internal byte UpDura;
        internal string WeaponData;
        internal bool Built;
    }

    internal sealed class WeaponUpgradeRepository
    {
        private const string TableName = "gamedata.weaponupg";
        private readonly string connectionString;

        internal WeaponUpgradeRepository(string connectionString = null)
        {
            this.connectionString = connectionString;
        }

        internal bool HasPending(string characterName)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT idx FROM {TableName} WHERE BINARY CharName=@charName LIMIT 1";
            AddGbkBinary(command, "@charName", characterName, 15);
            return command.ExecuteScalar() != null;
        }

        internal int Insert(string account, string characterName, TUserItem item,
            byte upDc, byte upSc, byte upMc, byte upCc, byte upDura, string weaponData)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $@"INSERT INTO {TableName}
                (ItemIdx,ItemID,PTID,CharName,IntoTime,UpDc,UpSc,UpMc,UpCc,UpDura,WeaponData)
                VALUES (@itemIdx,@itemId,@ptid,@charName,NOW(),@upDc,@upSc,@upMc,@upCc,@upDura,@weaponData)";
            command.Parameters.Add("@itemIdx", MySqlDbType.Int32).Value = item.wIndex;
            command.Parameters.Add("@itemId", MySqlDbType.UInt32).Value = unchecked((uint)item.MakeIndex);
            AddGbkBinary(command, "@ptid", account ?? string.Empty, 21);
            AddGbkBinary(command, "@charName", characterName, 15);
            command.Parameters.Add("@upDc", MySqlDbType.Int32).Value = upDc;
            command.Parameters.Add("@upSc", MySqlDbType.Int32).Value = upSc;
            command.Parameters.Add("@upMc", MySqlDbType.Int32).Value = upMc;
            command.Parameters.Add("@upCc", MySqlDbType.Int32).Value = upCc;
            command.Parameters.Add("@upDura", MySqlDbType.Int32).Value = upDura;
            command.Parameters.Add("@weaponData", MySqlDbType.Text).Value = weaponData;
            if (command.ExecuteNonQuery() != 1) return 0;
            return checked((int)command.LastInsertedId);
        }

        internal WeaponUpgradeRecord GetByCharacter(string characterName)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $@"SELECT idx,ItemIdx,ItemID,UpDc,UpSc,UpMc,UpCc,UpDura,WeaponData,
                CASE WHEN DATE_ADD(IntoTime,INTERVAL 10 MINUTE)<NOW() THEN 1 ELSE 0 END AS Built
                FROM {TableName} WHERE BINARY CharName=@charName LIMIT 1";
            AddGbkBinary(command, "@charName", characterName, 15);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new WeaponUpgradeRecord
            {
                Idx = reader.GetInt32("idx"),
                ItemIdx = reader.GetInt32("ItemIdx"),
                ItemId = reader.GetUInt32("ItemID"),
                UpDc = unchecked((byte)reader.GetInt32("UpDc")),
                UpSc = unchecked((byte)reader.GetInt32("UpSc")),
                UpMc = unchecked((byte)reader.GetInt32("UpMc")),
                UpCc = unchecked((byte)reader.GetInt32("UpCc")),
                UpDura = unchecked((byte)reader.GetInt32("UpDura")),
                WeaponData = reader.GetString("WeaponData"),
                Built = reader.GetInt32("Built") == 1
            };
        }

        internal bool Delete(int idx)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {TableName} WHERE idx=@idx";
            command.Parameters.Add("@idx", MySqlDbType.Int32).Value = idx;
            return command.ExecuteNonQuery() == 1;
        }

        internal int CleanupExpired()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {TableName} WHERE DATE_ADD(IntoTime,INTERVAL 4 MONTH)<NOW()";
            return command.ExecuteNonQuery();
        }

        private MySqlConnection OpenConnection()
        {
            var connection = new MySqlConnection(connectionString ?? M2Share.g_Config.sConnctionString);
            connection.Open();
            return connection;
        }

        private static void AddGbkBinary(MySqlCommand command, string name, string value, int maxBytes)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            if (bytes.Length == 0 || bytes.Length > maxBytes)
            {
                throw new ArgumentException($"{name} must contain 1-{maxBytes} GBK bytes", name);
            }
            command.Parameters.Add(name, MySqlDbType.VarBinary, bytes.Length).Value = bytes;
        }
    }
}
