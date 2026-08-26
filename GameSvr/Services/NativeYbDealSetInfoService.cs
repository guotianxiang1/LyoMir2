using System.Globalization;
using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr.Services
{
    internal readonly record struct NativeYbDealSetInfoRow(
        string Ptid, string CharacterName, ushort LimitLevel);

    internal interface INativeYbDealSetInfoStore
    {
        bool TryInitialize(out IReadOnlyList<NativeYbDealSetInfoRow> rows,
            out string error);

        bool TryUpsert(NativeYbDealSetInfoRecord record, out string error);
    }

    internal sealed class NativeYbDealSetInfoRecord
    {
        private NativeYbDealSetInfoRecord(byte[] ptid, byte[] characterName,
            ushort limitLevel)
        {
            PtidBytes = ptid;
            CharacterNameBytes = characterName;
            LimitLevel = limitLevel;
        }

        internal byte[] PtidBytes { get; }

        internal byte[] CharacterNameBytes { get; }

        internal ushort LimitLevel { get; set; }

        internal string Ptid => HUtil32.GbkEncoding.GetString(PtidBytes);

        internal string CharacterName =>
            HUtil32.GbkEncoding.GetString(CharacterNameBytes);

        internal static NativeYbDealSetInfoRecord CreateAttached(
            string ptid, string characterName, ushort limitLevel = 0)
            => new(Encode(ptid, 20), Encode(characterName, 15), limitLevel);

        internal static NativeYbDealSetInfoRecord CreateLoaded(
            NativeYbDealSetInfoRow row)
            => new(NormalizeAscii(Encode(row.Ptid, 20)),
                NormalizeAscii(Encode(row.CharacterName, 15)), row.LimitLevel);

        internal NativeYbDealSetInfoRecord Copy()
            => new((byte[])PtidBytes.Clone(), (byte[])CharacterNameBytes.Clone(),
                LimitLevel);

        internal static string MakeCharacterKey(string characterName)
            => MakeCharacterKey(Encode(characterName, 15));

        internal static string MakeCharacterKey(byte[] characterName)
            => Convert.ToHexString(NormalizeAscii(characterName));

        private static byte[] Encode(string value, int capacity)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            return bytes.Length <= capacity ? bytes : bytes[..capacity];
        }

        private static byte[] NormalizeAscii(byte[] value)
        {
            var normalized = (byte[])value.Clone();
            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] is >= (byte)'A' and <= (byte)'Z')
                    normalized[i] += 0x20;
            }

            return normalized;
        }
    }

    internal sealed class NativeYbDealSetInfoState
    {
        private readonly object _gate = new();
        private NativeYbDealSetInfoRecord _record;
        private bool _dirty;
        private int _version;

        internal bool HasRecord
        {
            get
            {
                lock (_gate) return _record != null;
            }
        }

        internal bool IsDirty
        {
            get
            {
                lock (_gate) return _dirty;
            }
        }

        internal NativeYbDealSetInfoRecord CurrentRecord
        {
            get
            {
                lock (_gate) return _record;
            }
        }

        internal void Attach(NativeYbDealSetInfoRecord record, bool dirty)
        {
            lock (_gate)
            {
                _record = record;
                _dirty = dirty;
                _version++;
            }
        }

        internal bool TrySetLimitLevel(ushort limitLevel)
        {
            lock (_gate)
            {
                if (_record == null || limitLevel > 999) return false;
                _record.LimitLevel = limitLevel;
                _dirty = true;
                _version++;
                return true;
            }
        }

        internal ushort GetLimitLevel()
        {
            lock (_gate) return _record?.LimitLevel ?? 0;
        }

        internal bool TryGetSaveSnapshot(out NativeYbDealSetInfoRecord record,
            out int version)
        {
            lock (_gate)
            {
                record = null;
                version = _version;
                if (!_dirty || _record == null || _record.PtidBytes.Length == 0
                    || _record.CharacterNameBytes.Length == 0
                    || _record.LimitLevel == 0)
                {
                    return false;
                }

                record = _record.Copy();
                return true;
            }
        }

        internal void ClearDirty(int version)
        {
            lock (_gate)
            {
                if (_version == version) _dirty = false;
            }
        }
    }

    internal sealed class NativeYbDealSetInfoService
    {
        internal static readonly NativeYbDealSetInfoService Unavailable =
            new(null);

        private readonly object _gate = new();
        private readonly INativeYbDealSetInfoStore _store;
        private readonly Dictionary<string, NativeYbDealSetInfoRecord> _records =
            new(StringComparer.Ordinal);
        private bool _ready;

        internal NativeYbDealSetInfoService(INativeYbDealSetInfoStore store)
        {
            _store = store;
        }

        internal bool IsReady
        {
            get
            {
                lock (_gate) return _ready;
            }
        }

        internal int Count
        {
            get
            {
                lock (_gate) return _records.Count;
            }
        }

        internal bool TryInitialize(out string error)
        {
            error = string.Empty;
            if (_store == null)
            {
                error = "M2_YB_Deal_SetInfo store is unavailable";
                return false;
            }

            if (!_store.TryInitialize(out var rows, out error))
            {
                lock (_gate) _ready = false;
                return false;
            }

            lock (_gate)
            {
                _records.Clear();
                foreach (var row in rows)
                {
                    var record = NativeYbDealSetInfoRecord.CreateLoaded(row);
                    var key = NativeYbDealSetInfoRecord.MakeCharacterKey(
                        record.CharacterNameBytes);
                    _records.TryAdd(key, record);
                }

                _ready = true;
            }

            return true;
        }

        internal void Attach(NativeYbDealSetInfoState state, string ptid,
            string characterName)
        {
            if (state == null) return;

            lock (_gate)
            {
                if (!_ready) return;
                var key = NativeYbDealSetInfoRecord.MakeCharacterKey(
                    characterName);
                if (_records.TryGetValue(key, out var record))
                {
                    state.Attach(record, false);
                    return;
                }

                record = NativeYbDealSetInfoRecord.CreateAttached(ptid,
                    characterName);
                _records.Add(key, record);
                state.Attach(record, true);
            }
        }

        internal bool TrySetLimitLevel(NativeYbDealSetInfoState state,
            ushort limitLevel)
        {
            if (state == null) return false;
            lock (_gate) return state.TrySetLimitLevel(limitLevel);
        }

        internal ushort GetLimitLevel(NativeYbDealSetInfoState state)
        {
            if (state == null) return 0;
            lock (_gate) return state.GetLimitLevel();
        }

        internal bool Save(NativeYbDealSetInfoState state)
        {
            if (state == null) return false;
            lock (_gate)
            {
                if (!_ready || !state.TryGetSaveSnapshot(out var record,
                        out var version))
                {
                    return false;
                }

                bool saved;
                string error;
                try
                {
                    saved = _store.TryUpsert(record, out error);
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage("[Exception]: SQL错误-" + ex.Message);
                    return false;
                }

                if (!saved && !string.IsNullOrEmpty(error))
                    M2Share.ErrorMessage("[Exception]: SQL错误-" + error);

                // The native manager is single-threaded. Holding its managed
                // counterpart's gate through the synchronous upsert prevents a
                // displaced/reconnecting owner from completing an older snapshot
                // after a newer one.
                state.ClearDirty(version);
                return saved;
            }
        }
    }

    internal sealed class MySqlNativeYbDealSetInfoStore
        : INativeYbDealSetInfoStore
    {
        internal const string CreateTableSql =
            "Create Table if not Exists gamedata.M2_YB_Deal_SetInfo(Idx Int AUTO_INCREMENT PRIMARY KEY,PTID char(20) default NULL, CharName char(15) binary not null,LimitLevel smallint(5) Default 0,ModTime DateTime default '0000-00-00 00:00:00',UNIQUE Index Name_Index1 (PTID, CharName));";

        internal const string DeleteZeroSql =
            "delete from gamedata.M2_YB_Deal_SetInfo where LimitLevel = 0;";

        internal const string SelectSql =
            "Select ptid, charname, limitLevel from gamedata.M2_YB_Deal_SetInfo";

        internal const string UpsertSqlFormat =
            "Insert into gamedata.M2_YB_Deal_SetInfo(ptid, charname, limitLevel, ModTime) values(\"{0}\", \"{1}\", {2}, Now()) on duplicate key update  ptid=\"{0}\", charname=\"{1}\", limitLevel={2}, ModTime=Now();";

        private readonly string _connectionString;

        internal MySqlNativeYbDealSetInfoStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        public bool TryInitialize(out IReadOnlyList<NativeYbDealSetInfoRow> rows,
            out string error)
        {
            var loaded = new List<NativeYbDealSetInfoRow>();
            rows = loaded;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                error = "database connection string is empty";
                return false;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                connection.Open();
                Execute(connection, CreateTableSql);
                Execute(connection, DeleteZeroSql);
                using var command = connection.CreateCommand();
                command.CommandText = SelectSql;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    loaded.Add(new NativeYbDealSetInfoRow(
                        ReadGbkText(reader.GetValue(0)),
                        ReadGbkText(reader.GetValue(1)),
                        Convert.ToUInt16(reader.GetValue(2),
                            CultureInfo.InvariantCulture)));
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool TryUpsert(NativeYbDealSetInfoRecord record,
            out string error)
        {
            error = string.Empty;
            try
            {
                var ptid = MySqlHelper.EscapeString(record.Ptid);
                var characterName = MySqlHelper.EscapeString(
                    record.CharacterName);
                var sql = string.Format(CultureInfo.InvariantCulture,
                    UpsertSqlFormat, ptid, characterName, record.LimitLevel);
                using var connection = new MySqlConnection(_connectionString);
                connection.Open();
                Execute(connection, sql);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void Execute(MySqlConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static string ReadGbkText(object value)
            => value switch
            {
                byte[] bytes => HUtil32.GbkEncoding.GetString(bytes),
                null or DBNull => string.Empty,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                     ?? string.Empty
            };
    }
}
