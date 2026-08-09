using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Synchronous MySQL store for the two native sign activities.
    /// </summary>
    public sealed class NativeSignActStore : INativeSignActStore
    {
        internal const string CreateSignActSql =
            "Create Table if not Exists gamedata.SignAct(" +
            "Idx Int AUTO_INCREMENT PRIMARY KEY, " +
            "ChrName char(14) binary not null UNIQUE, " +
            "SignCnt Int default 0," +
            "PrizeType Int default 0," +
            "Index Name_Index (ChrName)," +
            "Index SignCnt_Index (SignCnt));";
        internal const string CreateEverydaySql =
            "Create Table if not Exists gamedata.SignActEveryday(" +
            "Idx int AUTO_INCREMENT PRIMARY KEY," +
            "ChrName char(20) binary not null," +
            "signDate date not null," +
            "prizeTag int default 0," +
            "Unique key u_key(ChrName, signDate)," +
            "Index date_index (signDate));";

        internal const string SelectSignCountSql =
            "select Idx, SignCnt from gamedata.SignAct " +
            "where binary chrName=@characterName;";
        internal const string SelectSignPrizeSql =
            "select idx, prizeType from gamedata.SignAct " +
            "where binary chrName=@characterName;";
        internal const string InsertSignActSql =
            "insert into gamedata.SignAct(chrName, SignCnt) " +
            "values(@characterName, 1);";
        internal const string UpdateSignCountSql =
            "update gamedata.SignAct set SignCnt=@signCount where Idx=@index;";
        internal const string ResetSignActSql =
            "update gamedata.SignAct set prizeType=0, signCnt=0;";
        internal const string HasAnySignActPrizeSql =
            "select Idx from gamedata.SignAct where PrizeType > 0 limit 1;";
        internal const string SelectSignActDrawCandidatesSql =
            "select Idx from gamedata.SignAct where SignCnt >= 5 " +
            "order by rand() limit 3;";
        internal const string UpdateSignActPrizeTypeSql =
            "Update gamedata.SignAct set prizeType=@prizeType where Idx=@index;";
        internal const string SelectSignActWinnersSql =
            "select chrName, prizeType from gamedata.SignAct " +
            "where prizeType > 0;";

        internal const string ReplaceEverydaySignInSql =
            "replace into gamedata.signActEveryday(chrName, signDate) " +
            "values(@characterName, CurDate());";
        internal const string SelectYesterdayPrizeTagsSql =
            "select prizeTag from gamedata.signActEveryday " +
            "where chrname = @characterName and signDate = " +
            "Date_Sub(CurDate(), interval 1 day);";
        internal const string SelectYesterdayEverydayWinnersSql =
            "select chrName, prizeTag from gamedata.SignActEveryday " +
            "where signDate = date_sub(curdate(), interval 1 day) " +
            "and prizeTag > 0;";
        internal const string SelectYesterdayEverydayDrawCandidatesSql =
            "select idx, chrName from gamedata.SignActEveryday " +
            "where signDate = date_sub(curdate(), interval 1 day) " +
            "order by rand() limit 4;";
        internal const string UpdateEverydayPrizeTagSql =
            "update gamedata.SignActEveryday set prizeTag = @prizeTag " +
            "where idx = @index;";

        private readonly string _connectionString;

        public NativeSignActStore(string connectionString)
        {
            _connectionString = connectionString ?? string.Empty;
        }

        public bool EnsureSchemas(out string error)
        {
            var errors = new List<string>(2);
            TryEnsureSchema("SignAct", CreateSignActSql, errors);
            TryEnsureSchema("SignActEveryday", CreateEverydaySql, errors);
            error = string.Join("; ", errors);
            return errors.Count == 0;
        }

        private void TryEnsureSchema(string tableName, string sql,
            ICollection<string> errors)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                errors.Add(tableName + ": " + ex.Message);
            }
        }

        public bool TryGetSignCountRow(string characterName,
            out NativeSignActRow row)
        {
            row = null;
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = SelectSignCountSql;
                AddGbkParameter(command, "@characterName", characterName, 14);
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                row = new NativeSignActRow(
                    ReadInt32(reader, 0), characterName,
                    ReadInt32(reader, 1), 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetSignPrizeRow(string characterName,
            out NativeSignActRow row)
        {
            row = null;
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = SelectSignPrizeSql;
                AddGbkParameter(command, "@characterName", characterName, 14);
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                row = new NativeSignActRow(
                    ReadInt32(reader, 0), characterName,
                    0, ReadInt32(reader, 1));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool InsertSignAct(string characterName)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = InsertSignActSql;
                AddGbkParameter(command, "@characterName", characterName, 14);
                command.ExecuteNonQuery();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool UpdateSignCount(int index, int signCount) => Execute(
            UpdateSignCountSql, command =>
            {
                command.Parameters.Add("@signCount", MySqlDbType.Int32).Value =
                    signCount;
                command.Parameters.Add("@index", MySqlDbType.Int32).Value = index;
            });

        public bool ResetSignAct() => Execute(ResetSignActSql, null);

        public int QueryExistingSignActPrizeCount()
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = HasAnySignActPrizeSql;
                using var reader = command.ExecuteReader();
                return reader.Read() ? 1 : 0;
            }
            catch
            {
                return -1;
            }
        }

        public IReadOnlyList<NativeSignActRow> SelectSignActDrawCandidates(
            out int queryCount)
        {
            var rows = new List<NativeSignActRow>(3);
            queryCount = -1;
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = SelectSignActDrawCandidatesSql;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    rows.Add(new NativeSignActRow(
                        ReadInt32(reader, 0), string.Empty, 0, 0));
                queryCount = rows.Count;
            }
            catch
            {
                rows.Clear();
            }
            return rows;
        }

        public bool UpdateSignActPrizeType(int index, int prizeType) => Execute(
            UpdateSignActPrizeTypeSql, command =>
            {
                command.Parameters.Add("@prizeType", MySqlDbType.Int32).Value =
                    prizeType;
                command.Parameters.Add("@index", MySqlDbType.Int32).Value = index;
            });

        public IReadOnlyList<NativeSignActRow> SelectSignActWinners()
        {
            var rows = new List<NativeSignActRow>();
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = SelectSignActWinnersSql;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    rows.Add(new NativeSignActRow(0,
                        ReadGbkString(reader, 0), 0, ReadInt32(reader, 1)));
            }
            catch
            {
                rows.Clear();
            }
            return rows;
        }

        public bool ReplaceEverydaySignIn(string characterName)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = ReplaceEverydaySignInSql;
                AddGbkParameter(command, "@characterName", characterName, 20);
                command.ExecuteNonQuery();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public IReadOnlyList<int> SelectYesterdayPrizeTags(string characterName)
        {
            var tags = new List<int>();
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = SelectYesterdayPrizeTagsSql;
                AddGbkParameter(command, "@characterName", characterName, 20);
                using var reader = command.ExecuteReader();
                while (reader.Read()) tags.Add(ReadInt32(reader, 0));
            }
            catch
            {
                tags.Clear();
            }
            return tags;
        }

        public IReadOnlyList<NativeSignActEverydayRow>
            SelectYesterdayEverydayWinners(out int queryCount)
        {
            var rows = new List<NativeSignActEverydayRow>();
            queryCount = -1;
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = SelectYesterdayEverydayWinnersSql;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    rows.Add(new NativeSignActEverydayRow(0,
                        ReadGbkString(reader, 0), ReadInt32(reader, 1)));
                queryCount = rows.Count;
            }
            catch
            {
                rows.Clear();
            }
            return rows;
        }

        public IReadOnlyList<NativeSignActEverydayRow>
            SelectYesterdayEverydayDrawCandidates()
        {
            var rows = new List<NativeSignActEverydayRow>(4);
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = SelectYesterdayEverydayDrawCandidatesSql;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    rows.Add(new NativeSignActEverydayRow(
                        ReadInt32(reader, 0), ReadGbkString(reader, 1), 0));
            }
            catch
            {
                rows.Clear();
            }
            return rows;
        }

        public bool UpdateEverydayPrizeTag(int index, int prizeTag) => Execute(
            UpdateEverydayPrizeTagSql, command =>
            {
                command.Parameters.Add("@prizeTag", MySqlDbType.Int32).Value =
                    prizeTag;
                command.Parameters.Add("@index", MySqlDbType.Int32).Value = index;
            });

        private bool Execute(string sql, Action<MySqlCommand> bind)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                bind?.Invoke(command);
                command.ExecuteNonQuery();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private MySqlConnection OpenConnection()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                throw new InvalidOperationException(
                    "SignAct database connection is not configured");
            var connection = new MySqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static void AddGbkParameter(MySqlCommand command, string name,
            string value, int maxBytes)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            if (bytes.Length > maxBytes)
                throw new InvalidOperationException(
                    $"{name} exceeds {maxBytes} GBK bytes");
            command.Parameters.Add(name, MySqlDbType.VarBinary,
                Math.Max(1, bytes.Length)).Value = bytes;
        }

        private static int ReadInt32(MySqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt32(reader.GetValue(ordinal));

        private static string ReadGbkString(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return string.Empty;
            var value = reader.GetValue(ordinal);
            if (value is byte[] bytes)
                return HUtil32.GbkEncoding.GetString(bytes).TrimEnd('\0', ' ');
            var text = Convert.ToString(value) ?? string.Empty;
            if (text.Any(ch => ch > byte.MaxValue))
                return text.TrimEnd('\0', ' ');
            return HUtil32.GbkEncoding.GetString(
                System.Text.Encoding.Latin1.GetBytes(text)).TrimEnd('\0', ' ');
        }
    }
}
