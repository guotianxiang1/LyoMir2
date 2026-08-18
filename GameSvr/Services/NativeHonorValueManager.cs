using MySql.Data.MySqlClient;
using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    public sealed class NativeHonorValueManager
    {
        internal const int RankingPageSize = 7;
        internal const int RankingRecordSize = 22;
        internal const int RankingNameCapacity = 15;
        internal const int RankingNameLengthOffset = 2;
        internal const int RankingNameOffset = 3;
        internal const int RankingHonorOffset = 18;

        private const string CreateTableSql =
            "Create Table if not exists gamedata.User_Honor(" +
            " Idx int unsigned NOT NULL auto_increment," +
            " ChrName Char(15) binary not null UNIQUE default ''," +
            " HonorValue int not null Default 0," +
            " UpdateTime datetime NOT NULL default '0000-00-00 00:00:00'," +
            " PRIMARY KEY (Idx));";

        private readonly object _schemaLock = new();
        private readonly object _rankingLock = new();
        private bool _schemaReady;
        private NativeHonorRankingRecord[] _rankingRecords =
            Array.Empty<NativeHonorRankingRecord>();
        private DateTime _lastRankingRefreshDate = DateTime.MinValue;
        private DateTime _lastRankingRefreshAttemptDate = DateTime.MinValue;
        private long _lastErrorTick;

        public bool Initialize()
        {
            try
            {
                EnsureSchema();
                try
                {
                    RefreshRankingCore();
                }
                catch (Exception ex)
                {
                    LogError("排行榜初始化", ex);
                }
                return true;
            }
            catch (Exception ex)
            {
                LogError("初始化", ex);
                return false;
            }
        }

        public int Get(string characterName)
        {
            if (!IsNativeName(characterName))
                return -1;

            var online = M2Share.UserEngine?.GetPlayObject(characterName);
            if (online?.m_boHonorValueLoaded == true)
                return online.m_nHonorValue;

            try
            {
                EnsureSchema();
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "Select HonorValue from gamedata.User_Honor where ChrName = @name;";
                command.Parameters.AddWithValue("@name", characterName);
                var value = command.ExecuteScalar();
                var honor = value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
                if (online != null)
                {
                    online.m_nHonorValue = honor;
                    online.m_boHonorValueLoaded = true;
                }
                return honor;
            }
            catch (Exception ex)
            {
                LogError("查询", ex);
                return -1;
            }
        }

        public bool TryLoad(TPlayObject player)
        {
            if (player == null || !IsNativeName(player.m_sCharName))
                return false;

            try
            {
                EnsureSchema();
                using var connection = OpenConnection();
                player.m_nHonorValue = QueryValue(connection, player.m_sCharName);
                player.m_boHonorValueLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                player.m_boHonorValueLoaded = false;
                LogError("登录加载", ex);
                return false;
            }
        }

        public bool TryAdd(string characterName, int amount, out int newValue)
        {
            newValue = -1;
            if (!IsNativeName(characterName) || amount < 0)
                return false;

            try
            {
                EnsureSchema();
                using var connection = OpenConnection();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "Insert into gamedata.User_Honor(ChrName, HonorValue, UpdateTime) " +
                        "Values(@name, @amount, Now()) " +
                        "On Duplicate Key Update " +
                        "HonorValue = Least(2147483647, HonorValue + @amount), UpdateTime = Now();";
                    command.Parameters.AddWithValue("@name", characterName);
                    command.Parameters.AddWithValue("@amount", amount);
                    command.ExecuteNonQuery();
                }
                newValue = QueryValue(connection, characterName);
                UpdateOnline(characterName, newValue);
                return true;
            }
            catch (Exception ex)
            {
                LogError("增加", ex);
                return false;
            }
        }

        public bool TrySubtract(string characterName, int amount, out int newValue)
        {
            newValue = -1;
            if (!IsNativeName(characterName) || amount < 0)
                return false;

            try
            {
                EnsureSchema();
                using var connection = OpenConnection();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "Update gamedata.User_Honor " +
                        "Set HonorValue = Greatest(0, HonorValue - @amount), UpdateTime = Now() " +
                        "where ChrName = @name;";
                    command.Parameters.AddWithValue("@name", characterName);
                    command.Parameters.AddWithValue("@amount", amount);
                    command.ExecuteNonQuery();
                }
                newValue = QueryValue(connection, characterName);
                UpdateOnline(characterName, newValue);
                return true;
            }
            catch (Exception ex)
            {
                LogError("扣减", ex);
                return false;
            }
        }

        internal bool TryCreateRankingPage(int requestedPage,
            string characterName, out int correctedPage, out int lastPage,
            out byte[] body)
        {
            RefreshRankingIfDue();

            NativeHonorRankingRecord[] records;
            lock (_rankingLock)
                records = _rankingRecords;

            correctedPage = requestedPage;
            lastPage = (records.Length - 1) / RankingPageSize;
            body = Array.Empty<byte>();
            // sub_60EFE4 @0x60F168 accepts the sentinel produced by the
            // "my ranking not found" leg and sends an empty SM 1108/-2 frame.
            // A client may also echo that sentinel on its next request.
            if (requestedPage == -2)
                return true;
            if (requestedPage > lastPage || requestedPage < -1)
                return false;

            if (requestedPage == -1)
            {
                var rank = 0;
                var requestedNameBytes = HUtil32.GbkEncoding.GetBytes(
                    characterName ?? string.Empty);
                for (var index = 0; index < records.Length; index++)
                {
                    if (!records[index].NameBytes.AsSpan()
                            .SequenceEqual(requestedNameBytes))
                        continue;
                    rank = index + 1;
                    break;
                }

                if (rank == 0)
                {
                    correctedPage = -2;
                    return true;
                }
                correctedPage = (rank - 1) / RankingPageSize;
            }

            var first = correctedPage * RankingPageSize;
            if (first < 0 || first >= records.Length)
                return false;
            var count = Math.Min(RankingPageSize, records.Length - first);
            body = new byte[count * RankingRecordSize];
            for (var index = 0; index < count; index++)
                records[first + index].WireBytes.CopyTo(body,
                    index * RankingRecordSize);
            return true;
        }

        internal void ReplaceRankingSnapshot(
            IEnumerable<KeyValuePair<string, int>> rankings)
        {
            var records = (rankings ??
                    Enumerable.Empty<KeyValuePair<string, int>>())
                .Take(50)
                .Select((ranking, index) => CreateRankingRecord(
                    index + 1, ranking.Key, ranking.Value))
                .ToArray();
            lock (_rankingLock)
            {
                _rankingRecords = records;
                _lastRankingRefreshDate = DateTime.Today;
                _lastRankingRefreshAttemptDate = DateTime.Today;
            }
        }

        internal static bool IsNativeName(string characterName)
        {
            return !string.IsNullOrWhiteSpace(characterName)
                && HUtil32.GbkEncoding.GetByteCount(characterName) <= 15;
        }

        private void EnsureSchema()
        {
            if (_schemaReady)
                return;
            lock (_schemaLock)
            {
                if (_schemaReady)
                    return;
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = CreateTableSql;
                command.ExecuteNonQuery();
                _schemaReady = true;
            }
        }

        private static int QueryValue(MySqlConnection connection, string characterName)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "Select HonorValue from gamedata.User_Honor where ChrName = @name;";
            command.Parameters.AddWithValue("@name", characterName);
            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        private void RefreshRankingIfDue()
        {
            var today = DateTime.Today;
            lock (_rankingLock)
            {
                if (today.DayOfWeek != DayOfWeek.Saturday
                    || _lastRankingRefreshDate == today
                    || _lastRankingRefreshAttemptDate == today)
                    return;
                _lastRankingRefreshAttemptDate = today;
            }

            try
            {
                RefreshRankingCore();
            }
            catch (Exception ex)
            {
                LogError("排行榜刷新", ex);
            }
        }

        private void RefreshRankingCore()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "Select a.ChrName, a.HonorValue, b.level, b.AdminLevel " +
                "FROM gamedata.user_honor a join mir3.user_index b " +
                "on a.ChrName = b.ChrName WHERE b.AdminLevel = 0 " +
                "Order By a.HonorValue DESC, b.level DESC Limit 50;";
            using var reader = command.ExecuteReader();
            var records = new List<NativeHonorRankingRecord>(50);
            while (reader.Read() && records.Count < 50)
            {
                var name = reader.IsDBNull(0) ? string.Empty
                    : reader.GetString(0);
                var honor = reader.IsDBNull(1) ? 0
                    : Convert.ToInt32(reader.GetValue(1));
                records.Add(CreateRankingRecord(records.Count + 1,
                    name, honor));
            }

            lock (_rankingLock)
            {
                _rankingRecords = records.ToArray();
                _lastRankingRefreshDate = DateTime.Today;
                _lastRankingRefreshAttemptDate = DateTime.Today;
            }
        }

        private static NativeHonorRankingRecord CreateRankingRecord(int rank,
            string characterName, int honorValue)
        {
            var wire = new byte[RankingRecordSize];
            BinaryPrimitives.WriteUInt16LittleEndian(wire,
                unchecked((ushort)rank));
            var nameBytes = HUtil32.GbkEncoding.GetBytes(
                characterName ?? string.Empty);
            var nameLength = Math.Min(nameBytes.Length, RankingNameCapacity);
            // 0x60ECC6..0x60ED22 calls the ShortString[15] writer: byte +2
            // is the byte length and the GBK payload starts at +3. The previous
            // port copied the payload at +2 and shifted every name one byte left.
            wire[RankingNameLengthOffset] = unchecked((byte)nameLength);
            nameBytes.AsSpan(0, nameLength)
                .CopyTo(wire.AsSpan(RankingNameOffset, RankingNameCapacity));
            BinaryPrimitives.WriteInt32LittleEndian(
                wire.AsSpan(RankingHonorOffset, sizeof(int)), honorValue);
            return new NativeHonorRankingRecord(
                nameBytes.AsSpan(0, nameLength).ToArray(), wire);
        }

        private static void UpdateOnline(string characterName, int value)
        {
            var online = M2Share.UserEngine?.GetPlayObject(characterName);
            if (online == null)
                return;
            online.m_nHonorValue = value;
            online.m_boHonorValueLoaded = true;
        }

        private static MySqlConnection OpenConnection()
        {
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("数据库连接字符串为空");
            var connection = new MySqlConnection(connectionString);
            connection.Open();
            return connection;
        }

        private void LogError(string operation, Exception exception)
        {
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastErrorTick) < 10_000)
                return;
            Interlocked.Exchange(ref _lastErrorTick, now);
            M2Share.ErrorMessage($"原生荣誉值{operation}失败: {exception.Message}");
        }

        private sealed class NativeHonorRankingRecord
        {
            public NativeHonorRankingRecord(byte[] nameBytes,
                byte[] wireBytes)
            {
                NameBytes = nameBytes;
                WireBytes = wireBytes;
            }

            public byte[] NameBytes { get; }
            public byte[] WireBytes { get; }
        }
    }
}
