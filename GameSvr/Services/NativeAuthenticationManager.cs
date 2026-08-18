using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr
{
    public sealed class NativeAuthenticationManager
    {
        private const string CreateTableSql =
            "Create table if not Exists gamedata.AuthenticateUser(" +
            "idx int AUTO_INCREMENT PRIMARY KEY," +
            "PlayerId bigInt NOT NULL UNIQUE," +
            "Status1 int default 0," +
            "Status2 int default 0," +
            "AuthenDate datetime NOT NULL default '0000-00-00 00:00:00'," +
            "Index PlayerId_Index(PlayerId));";
        internal const string SelectPlayerSql =
            "Select PlayerId from gamedata.AuthenticateUser where PlayerId = @playerId;";
        internal const string InsertStatus1Sql =
            "Insert Into gamedata.AuthenticateUser(PlayerId, Status1, AuthenDate, PTID) " +
            "values(@playerId, @status, Now(), @ptid);";
        internal const string UpdateStatus1Sql =
            "update gamedata.AuthenticateUser set Status1 = @status, " +
            "AuthenDate = Now() where PlayerId = @playerId;";
        internal const string SelectHelpOtherSql =
            "Select Idx, PlayerId, HelpOther from gamedata.AuthenticateUser " +
            "where PlayerId = @playerId;";
        internal const string UpdateHelpOtherSql =
            "Update gamedata.AuthenticateUser set HelpOther = 1 where Idx = @idx;";

        private readonly object _schemaLock = new();
        private bool _schemaReady;
        private long _lastErrorTick;

        public bool Initialize()
        {
            try
            {
                EnsureSchema();
                return true;
            }
            catch (Exception ex)
            {
                LogError("初始化", ex);
                return false;
            }
        }

        public bool TryLoad(TPlayObject player)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.m_sCharName))
                return false;

            var loadedStorageCapacity = player.m_nStorageSpaceCount;
            try
            {
                player.ClearNativeAuthenticationIdentity();
                player.SetNativeAuthenticationStatus(0, 0, 0);
                player.ApplyNativeAuthenticationLimits();
                EnsureSchema();
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "Select Coalesce(a.Status1, 0), Coalesce(a.Status2, 0), " +
                    "(Select Count(*) from gamedata.AuthenticateUser h " +
                    " where h.PTID = i.PTID and h.HelpOther = 1), " +
                    "i.UserId, Cast(i.PTID as Binary) " +
                    "from mir3.user_index i " +
                    "left join gamedata.AuthenticateUser a on a.PlayerId = i.UserId " +
                    "where Cast(i.ChrName as Binary) = @chrName and i.IsDelete = 0 limit 1;";
                command.Parameters.Add("@chrName", MySqlDbType.Binary).Value =
                    HUtil32.GbkEncoding.GetBytes(player.m_sCharName);
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    return false;

                player.SetNativeAuthenticationIdentity(
                    ReadInt64(reader, 3), ReadPtidBytes(reader, 4));
                player.SetNativeAuthenticationStatus(
                    unchecked((byte)ReadInt32(reader, 0)),
                    unchecked((byte)ReadInt32(reader, 1)),
                    ReadInt64(reader, 2) > 0 ? (byte)1 : (byte)0);
                player.ApplyNativeAuthenticationLimits();
                return true;
            }
            catch (Exception ex)
            {
                LogError("登录加载", ex);
                return false;
            }
            finally
            {
                player.m_nStorageSpaceCount = ResolveLoadedStorageCapacity(
                    loadedStorageCapacity, player.m_nStorageSpaceCount);
            }
        }

        internal static int ResolveLoadedStorageCapacity(
            int loadedCapacity, int authenticationCapacity)
        {
            // Native load order is authentication baseline first, then the
            // persisted WORD overrides it only when the stored value is > 48.
            return loadedCapacity > TPlayObject.STORAGE_PAGE_SIZE
                ? loadedCapacity
                : authenticationCapacity;
        }

        internal int PersistStatus1(TPlayObject player, byte status)
        {
            if (player == null ||
                !player.TryGetNativeAuthenticationIdentity(out var playerId, out var ptid))
                return -1;

            try
            {
                EnsureSchema();
                using var connection = OpenConnection();
                var rowCount = 0;
                try
                {
                    using var select = connection.CreateCommand();
                    select.CommandText = SelectPlayerSql;
                    select.Parameters.Add("@playerId", MySqlDbType.Int64).Value = playerId;
                    using var reader = select.ExecuteReader();
                    while (reader.Read())
                        rowCount++;
                }
                catch (Exception ex)
                {
                    rowCount = -1;
                    LogError("持久化查询", ex);
                }

                using var write = connection.CreateCommand();
                write.CommandText = rowCount > 0 ? UpdateStatus1Sql : InsertStatus1Sql;
                write.Parameters.Add("@playerId", MySqlDbType.Int64).Value = playerId;
                write.Parameters.Add("@status", MySqlDbType.Int32).Value = status;
                if (rowCount <= 0)
                {
                    write.Parameters.Add("@ptid", MySqlDbType.VarBinary, 20).Value = ptid;
                }
                write.ExecuteNonQuery();
                return 1;
            }
            catch (Exception ex)
            {
                LogError("持久化", ex);
                return -1;
            }
        }

        internal int MarkHelpOther(TPlayObject player)
        {
            if (player == null ||
                !player.TryGetNativeAuthenticationIdentity(out var playerId, out _))
                return 0;

            try
            {
                EnsureSchema();
                using var connection = OpenConnection();
                int idx;
                int helpOther;
                try
                {
                    using var select = connection.CreateCommand();
                    select.CommandText = SelectHelpOtherSql;
                    select.Parameters.Add("@playerId", MySqlDbType.Int64).Value = playerId;
                    using var reader = select.ExecuteReader();
                    if (!reader.Read())
                        return 0;
                    idx = ReadInt32(reader, 0);
                    helpOther = ReadInt32(reader, 2);
                }
                catch (Exception ex)
                {
                    LogError("帮助查询", ex);
                    return 0;
                }

                if (helpOther == 1)
                    return 2;

                try
                {
                    using var update = connection.CreateCommand();
                    update.CommandText = UpdateHelpOtherSql;
                    update.Parameters.Add("@idx", MySqlDbType.Int32).Value = idx;
                    update.ExecuteNonQuery();
                    return 1;
                }
                catch (Exception ex)
                {
                    LogError("帮助更新", ex);
                    return -2;
                }
            }
            catch (Exception ex)
            {
                LogError("帮助查询", ex);
                return 0;
            }
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
                ExecuteNonQuery(connection, CreateTableSql);
                if (!HasColumn(connection, "PTID"))
                {
                    ExecuteNonQuery(connection,
                        "Alter table gamedata.AuthenticateUser " +
                        "add column PTID Char(20) binary not null;");
                    ExecuteNonQuery(connection,
                        "Update gamedata.AuthenticateUser a " +
                        "inner join mir3.user_index b on a.PlayerId = b.UserId " +
                        "set a.PTID = b.PTID where a.PlayerId = b.UserId;");
                }
                if (!HasColumn(connection, "HelpOther"))
                {
                    ExecuteNonQuery(connection,
                        "Alter table gamedata.AuthenticateUser " +
                        "add column HelpOther int default 0;");
                }
                _schemaReady = true;
            }
        }

        private static bool HasColumn(MySqlConnection connection, string columnName)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "Select Count(*) from information_schema.COLUMNS " +
                "where TABLE_SCHEMA = 'gamedata' and TABLE_NAME = 'AuthenticateUser' " +
                "and COLUMN_NAME = @columnName;";
            command.Parameters.AddWithValue("@columnName", columnName);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private static void ExecuteNonQuery(MySqlConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static int ReadInt32(MySqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static long ReadInt64(MySqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal));
        }

        private static byte[] ReadPtidBytes(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                throw new InvalidDataException("AuthenticateUser PTID is NULL");

            var value = reader.GetValue(ordinal);
            if (value is not byte[] binary)
                throw new InvalidDataException(
                    $"AuthenticateUser PTID provider type is {value?.GetType().FullName ?? "null"}");
            return binary.ToArray();
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
            M2Share.ErrorMessage($"原生身份认证{operation}失败: {exception.Message}");
        }
    }
}
