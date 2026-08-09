using System.Globalization;
using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr
{
    public sealed class NativeCreditCardService
    {
        internal const string CreateTableSql =
            "Create Table if not Exists CreditCard(" +
            "Idx Int AUTO_INCREMENT PRIMARY KEY," +
            "PTID char(32) binary not null," +
            "CharName char(15) binary not null UNIQUE," +
            "Value int default 0," +
            "UsedValue int default 0," +
            "Value2 int default 0," +
            "Index CharName_Idx (CharName));";
        internal const string SelectSql =
            "select Idx, Value, UsedValue, Value2 from CreditCard " +
            "where CharName=@characterName;";
        internal const string InsertSql =
            "Insert Into CreditCard(PTID, CharName, Value, UsedValue, Value2) " +
            "values(@account,@characterName,@value,@usedValue,@value2);";
        internal const string UpdateSql =
            "update CreditCard set Value=@value, UsedValue=@usedValue, " +
            "Value2=@value2 where Idx=@index;";
        internal const string QueryValue2Sql =
            "Show Fields from CreditCard Like 'Value2';";
        internal const string AddValue2Sql =
            "Alter Table CreditCard add Value2 int default 0;";
        internal const string ClearMonthlySql =
            "update CreditCard set Value2=0;";
        internal const string CreateGloryPointTableSql =
            "Create Table if not Exists gamedata.GloryPoint(" +
            "Idx Int AUTO_INCREMENT PRIMARY KEY," +
            "PTID char(32) binary not null," +
            "CharName char(15) binary not null," +
            "datePhase int not null," +
            "Value int default 0," +
            "UNIQUE KEY uniKey(CharName, datePhase));";
        internal const string SelectGloryPointSql =
            "select Idx, Value from GloryPoint where " +
            "CharName=@characterName and datePhase=@datePhase;";
        internal const string UpdateGloryPointSql =
            "update GloryPoint set Value=@value where Idx=@index;";
        internal const string InsertGloryPointSql =
            "Insert Into GloryPoint(PTID, CharName, datePhase, Value) " +
            "values(@account, @characterName, @datePhase, @value);";

        private readonly object _schemaLock = new();
        private readonly bool _databaseAvailable;
        private readonly NativeServerSwitchStore _serverSwitches;
        private volatile bool _enabled;
        private bool _schemaReady;
        private long _lastErrorTick;

        private NativeCreditCardService(bool enabled, bool databaseAvailable,
            NativeServerSwitchStore serverSwitches)
        {
            _enabled = enabled;
            _databaseAvailable = databaseAvailable;
            _serverSwitches = serverSwitches ?? NativeServerSwitchStore.Unavailable;
        }

        private NativeCreditCardService(bool enabled, bool databaseAvailable,
            string switchFile, byte[] switches)
            : this(enabled, databaseAvailable,
                NativeServerSwitchStore.FromSnapshot(switchFile, switches))
        {
        }

        public static NativeCreditCardService Disabled { get; } =
            new(false, false, NativeServerSwitchStore.Unavailable);
        public bool Enabled => _enabled;

        public bool MonthlyLimitedEnabled
        {
            get => _serverSwitches.IsBitSet(2, 0x08);
        }

        public static bool TryCreate(string shareDirectory, out NativeCreditCardService service,
            out string error)
        {
            service = Disabled;
            error = string.Empty;
            if (!NativeServerSwitchStore.TryLoad(shareDirectory,
                    out var switches, out error))
                return false;
            return TryCreate(switches, out service, out error);
        }

        public static bool TryCreate(NativeServerSwitchStore switches,
            out NativeCreditCardService service, out string error)
        {
            service = Disabled;
            error = string.Empty;
            if (switches == null || !switches.Available)
            {
                error = "ServerSwitch.Bin is unavailable";
                return false;
            }
            try
            {
                var enabled = switches.IsBitSet(1, 0x10);
                service = new NativeCreditCardService(enabled, true, switches);
                service.EnsureSchema();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool TrySetEnabled(bool enabled, out uint switchWord)
        {
            switchWord = 0;
            if (!_serverSwitches.TrySetBit(1, 0x10, enabled,
                    out switchWord, out var error))
            {
                if (!string.IsNullOrEmpty(error))
                    LogSwitchError("开关修改", error);
                return false;
            }
            _enabled = enabled;
            return true;
        }

        public bool TryApplySwitchWord(uint switchWord, bool persist)
        {
            if (!_serverSwitches.TryApplySwitchWord(switchWord, out var error))
            {
                if (!string.IsNullOrEmpty(error))
                    LogSwitchError("镜像开关应用", error);
                return false;
            }
            _enabled = (switchWord & 0x00001000u) != 0;
            return !persist || TryPersistSwitches();
        }

        public bool TryPersistSwitches()
        {
            if (_serverSwitches.TryPersist(out var error))
                return true;
            if (!string.IsNullOrEmpty(error))
            {
                LogSwitchError("开关保存", error);
            }
            return false;
        }

        public bool TryClearMonthly()
        {
            if (!_databaseAvailable)
                return false;
            try
            {
                EnsureSchema();
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = ClearMonthlySql;
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                LogError("清除每月限时灵符", ex);
                return false;
            }
        }

        public bool TryArchiveAll()
        {
            if (!_databaseAvailable)
                return false;
            try
            {
                lock (_schemaLock)
                {
                    var suffix = DateTime.Now.ToString("yyyyMMdd",
                        CultureInfo.InvariantCulture);
                    using var connection = OpenConnection();
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "Alter Table CreditCard rename CreditCard" + suffix + ";";
                    command.ExecuteNonQuery();
                    _schemaReady = false;

                    command.CommandText = CreateTableSql;
                    command.ExecuteNonQuery();
                    command.CommandText = QueryValue2Sql;
                    var hasValue2 = false;
                    using (var reader = command.ExecuteReader())
                        hasValue2 = reader.Read();
                    if (!hasValue2)
                    {
                        command.CommandText = AddValue2Sql;
                        command.ExecuteNonQuery();
                    }
                    command.CommandText = CreateGloryPointTableSql;
                    command.ExecuteNonQuery();
                    _schemaReady = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError("归档清除", ex);
                return false;
            }
        }

        public void ResetOnlineMonthly()
        {
            if (MonthlyLimitedEnabled)
                return;
            var players = M2Share.UserEngine?.PlayObjects.ToArray();
            if (players == null)
                return;
            foreach (var player in players)
            {
                if (player == null || !player.m_boReadyRun || player.m_boGhost)
                    continue;
                lock (player.m_CreditCard.SyncRoot)
                    player.m_CreditCard.ClearMonthly();
                player.RefreshNativeLingFu();
            }
        }

        public void ResetOnlineAll()
        {
            if (Enabled)
                return;
            var players = M2Share.UserEngine?.PlayObjects.ToArray();
            if (players == null)
                return;
            var currentTick = HUtil32.GetTickCount();
            foreach (var player in players)
            {
                if (player == null || !player.m_boReadyRun || player.m_boGhost)
                    continue;
                lock (player.m_CreditCard.SyncRoot)
                    player.m_CreditCard.ClearAll(currentTick);
                player.RefreshNativeLingFu();
            }
        }

        public bool TryLoad(TPlayObject player)
        {
            if (player == null || !_databaseAvailable) return false;
            var account = player.m_CreditCard;
            lock (account.SyncRoot)
                account.Reset(HUtil32.GetTickCount());
            var gloryPointPeriod = CalculateGloryPointPeriod(DateTime.Now);
            var periodChanged = false;
            lock (account.SyncRoot)
            {
                if (account.GloryPointPeriod != gloryPointPeriod)
                {
                    account.GloryPointPeriod = gloryPointPeriod;
                    account.GloryPointValue = 0;
                    periodChanged = true;
                }
            }
            if (periodChanged) player.RefreshNativeLingFu();

            try
            {
                using var connection = OpenConnection();
                var creditCardLoaded = false;
                var gloryPointLoaded = false;
                try
                {
                    uint index = 0;
                    var value = 0;
                    var usedValue = 0;
                    var value2 = 0;
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = SelectSql;
                        AddGbkParameter(command, "@characterName", player.m_sCharName, 15);
                        using var reader = command.ExecuteReader();
                        if (reader.Read())
                        {
                            index = reader.IsDBNull(0)
                                ? 0u
                                : Convert.ToUInt32(reader.GetValue(0));
                            value = reader.IsDBNull(1)
                                ? 0
                                : Convert.ToInt32(reader.GetValue(1));
                            usedValue = reader.IsDBNull(2)
                                ? 0
                                : Convert.ToInt32(reader.GetValue(2));
                            value2 = reader.IsDBNull(3)
                                ? 0
                                : Convert.ToInt32(reader.GetValue(3));
                        }
                    }

                    lock (account.SyncRoot)
                    {
                        account.Index = index;
                        account.Value = value;
                        account.UsedValue = usedValue;
                        account.Value2 = value2;
                        account.Loaded = true;
                    }
                    creditCardLoaded = true;
                }
                catch (Exception ex)
                {
                    LogError("CreditCard登录加载", ex);
                }

                try
                {
                    var gloryPointValue = 0;
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = SelectGloryPointSql;
                        AddGbkParameter(command, "@characterName", player.m_sCharName, 15);
                        command.Parameters.Add("@datePhase", MySqlDbType.Int32).Value =
                            gloryPointPeriod;
                        using var reader = command.ExecuteReader();
                        if (reader.Read() && !reader.IsDBNull(1))
                            gloryPointValue = Convert.ToInt32(reader.GetValue(1));
                    }

                    lock (account.SyncRoot)
                    {
                        account.GloryPointValue = gloryPointValue;
                        account.GloryPointDirty = false;
                        account.GloryPointDirtyVersion = 0;
                    }
                    gloryPointLoaded = true;
                }
                catch (Exception ex)
                {
                    LogError("GloryPoint登录加载", ex);
                }
                return creditCardLoaded && gloryPointLoaded;
            }
            catch (Exception ex)
            {
                LogError("登录加载", ex);
                return false;
            }
        }

        public bool TrySaveDue(TPlayObject player, int currentTick, bool force = false)
        {
            if (player == null || !_databaseAvailable) return false;
            var account = player.m_CreditCard;
            uint index;
            int value;
            int usedValue;
            int value2;
            long dirtyVersion;
            bool creditCardDirty;
            bool gloryPointDirty;
            int gloryPointValue;
            int gloryPointPeriod;
            long gloryPointDirtyVersion;
            lock (account.SyncRoot)
            {
                if (!force)
                {
                    if (unchecked((uint)(currentTick - account.LastSaveTick)) < 10_000u)
                        return true;
                    account.LastSaveTick = currentTick;
                }
            }

            if (!force)
            {
                var currentPeriod = CalculateGloryPointPeriod(DateTime.Now);
                var periodChanged = false;
                lock (account.SyncRoot)
                {
                    if (account.GloryPointPeriod != currentPeriod)
                    {
                        account.GloryPointPeriod = currentPeriod;
                        account.GloryPointValue = 0;
                        periodChanged = true;
                    }
                }
                if (periodChanged) player.RefreshNativeLingFu();
            }

            lock (account.SyncRoot)
            {
                index = account.Index;
                value = account.Value;
                usedValue = account.UsedValue;
                value2 = account.Value2;
                dirtyVersion = account.DirtyVersion;
                creditCardDirty = account.Loaded && account.Dirty;
                gloryPointDirty = account.GloryPointDirty;
                gloryPointValue = account.GloryPointValue;
                gloryPointPeriod = account.GloryPointPeriod;
                gloryPointDirtyVersion = account.GloryPointDirtyVersion;
            }
            if (!creditCardDirty && !gloryPointDirty) return true;

            var succeeded = true;
            if (creditCardDirty)
            {
                player.RefreshNativeLingFu();
                try
                {
                    using var connection = OpenConnection();
                    using var command = connection.CreateCommand();
                    command.CommandText = index == 0 ? InsertSql : UpdateSql;
                    command.Parameters.Add("@value", MySqlDbType.Int32).Value = value;
                    command.Parameters.Add("@usedValue", MySqlDbType.Int32).Value = usedValue;
                    command.Parameters.Add("@value2", MySqlDbType.Int32).Value = value2;
                    if (index == 0)
                    {
                        AddGbkParameter(command, "@account", player.m_sUserID, 32);
                        AddGbkParameter(command, "@characterName", player.m_sCharName, 15);
                    }
                    else
                    {
                        command.Parameters.Add("@index", MySqlDbType.UInt32).Value = index;
                    }
                    if (command.ExecuteNonQuery() != 1)
                    {
                        succeeded = false;
                    }
                    else
                    {
                        lock (account.SyncRoot)
                        {
                            if (account.DirtyVersion == dirtyVersion)
                                account.Dirty = false;
                        }

                        var persistedIndex = ReloadCreditCardIndex(connection,
                            player.m_sCharName);
                        if (persistedIndex.HasValue)
                        {
                            lock (account.SyncRoot)
                                account.Index = persistedIndex.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError("CreditCard保存", ex);
                    succeeded = false;
                }
            }

            if (gloryPointDirty)
            {
                lock (account.SyncRoot)
                {
                    if (account.GloryPointDirtyVersion == gloryPointDirtyVersion
                        && account.GloryPointPeriod == gloryPointPeriod)
                        account.GloryPointDirty = false;
                }
                player.RefreshNativeLingFu();
                try
                {
                    using var connection = OpenConnection();
                    if (!SaveGloryPoint(connection, player, gloryPointPeriod,
                            gloryPointValue))
                        succeeded = false;
                }
                catch (Exception ex)
                {
                    LogError("GloryPoint保存", ex);
                    succeeded = false;
                }
            }
            return succeeded;
        }

        private void EnsureSchema()
        {
            if (_schemaReady) return;
            lock (_schemaLock)
            {
                if (_schemaReady) return;
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = CreateTableSql;
                command.ExecuteNonQuery();
                command.CommandText = QueryValue2Sql;
                var hasValue2 = false;
                using (var reader = command.ExecuteReader())
                    hasValue2 = reader.Read();
                if (!hasValue2)
                {
                    command.CommandText = AddValue2Sql;
                    command.ExecuteNonQuery();
                }
                command.CommandText = CreateGloryPointTableSql;
                command.ExecuteNonQuery();
                _schemaReady = true;
            }
        }

        internal static int CalculateGloryPointPeriod(DateTime now)
        {
            var closingDay = now.Day <= 15
                ? 15
                : DateTime.DaysInMonth(now.Year, now.Month);
            return unchecked((int)new DateTime(now.Year, now.Month, closingDay)
                .ToOADate());
        }

        private static uint? ReloadCreditCardIndex(MySqlConnection connection,
            string characterName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = SelectSql;
            AddGbkParameter(command, "@characterName", characterName, 15);
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0)) return null;
            return Convert.ToUInt32(reader.GetValue(0));
        }

        private static bool SaveGloryPoint(MySqlConnection connection,
            TPlayObject player, int period, int value)
        {
            var index = 0;
            var exactlyOneRow = false;
            using (var select = connection.CreateCommand())
            {
                select.CommandText = SelectGloryPointSql;
                AddGbkParameter(select, "@characterName", player.m_sCharName, 15);
                select.Parameters.Add("@datePhase", MySqlDbType.Int32).Value = period;
                using var reader = select.ExecuteReader();
                if (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                        index = Convert.ToInt32(reader.GetValue(0));
                    exactlyOneRow = !reader.Read();
                }
            }

            using var command = connection.CreateCommand();
            command.CommandText = exactlyOneRow
                ? UpdateGloryPointSql
                : InsertGloryPointSql;
            command.Parameters.Add("@value", MySqlDbType.Int32).Value = value;
            if (!exactlyOneRow)
            {
                AddGbkParameter(command, "@account", player.m_sUserID, 32);
                AddGbkParameter(command, "@characterName", player.m_sCharName, 15);
                command.Parameters.Add("@datePhase", MySqlDbType.Int32).Value = period;
            }
            else
            {
                command.Parameters.Add("@index", MySqlDbType.Int32).Value = index;
            }
            return command.ExecuteNonQuery() == 1;
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

        private static void AddGbkParameter(MySqlCommand command, string name, string value,
            int maxBytes)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            if (bytes.Length > maxBytes)
                throw new InvalidOperationException($"{name} exceeds {maxBytes} GBK bytes");
            command.Parameters.Add(name, MySqlDbType.VarBinary,
                Math.Max(1, bytes.Length)).Value = bytes;
        }

        private void LogError(string operation, Exception exception)
        {
            LogSwitchError(operation, exception.Message);
        }

        private void LogSwitchError(string operation, string message)
        {
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastErrorTick) < 10_000) return;
            Interlocked.Exchange(ref _lastErrorTick, now);
            M2Share.ErrorMessage($"原生CreditCard{operation}失败: {message}");
        }
    }
}
