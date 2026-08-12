using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using MySql.Data.MySqlClient;

namespace DBSvr.Core
{
    public sealed class NativeType2RankingRow
    {
        public byte[] Name { get; init; } = Array.Empty<byte>();
        public byte[] HeroName { get; init; } = Array.Empty<byte>();
        public uint Value { get; init; }
        public uint SfLevel { get; init; }
        public ushort Level { get; init; }
    }

    public static class NativeType2RankingPacketBuilder
    {
        private const int RowsPerPacket = 7;

        public static List<byte[]> Create(int category,
            IReadOnlyList<NativeType2RankingRow> rows)
        {
            if (category is < 0 or > 13 or 11 or 12)
                throw new ArgumentOutOfRangeException(nameof(category));
            rows ??= Array.Empty<NativeType2RankingRow>();
            var hero = category is >= 4 and <= 7;
            var payloadLength = hero ? 0x124 : 0xB4;
            var rowLength = hero ? 40 : 24;
            var result = new List<byte[]>((rows.Count + RowsPerPacket - 1)
                                          / RowsPerPacket);
            for (var first = 0; first < rows.Count; first += RowsPerPacket)
            {
                var payload = new byte[payloadLength];
                BinaryPrimitives.WriteUInt16LittleEndian(payload,
                    NativeType2InitializationProtocol.SecondaryEndCommand);
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                    category);
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4),
                    first / RowsPerPacket);
                var count = Math.Min(RowsPerPacket, rows.Count - first);
                for (var i = 0; i < count; i++)
                {
                    var row = rows[first + i] ?? new NativeType2RankingRow();
                    var offset = NativeType2Protocol.HeaderSize + i * rowLength;
                    WriteShortString(payload, offset, 15, row.Name);
                    if (hero)
                    {
                        WriteShortString(payload, offset + 16, 14, row.HeroName);
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            payload.AsSpan(offset + 32, 2), row.Level);
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            payload.AsSpan(offset + 36, 4), row.SfLevel);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            payload.AsSpan(offset + 16, 4), row.Value);
                        if (category is >= 0 and <= 3 or 13)
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                payload.AsSpan(offset + 20, 4), row.SfLevel);
                    }
                }
                result.Add(payload);
            }
            return result;
        }

        private static void WriteShortString(byte[] destination, int offset,
            int capacity, byte[] value)
        {
            value ??= Array.Empty<byte>();
            var length = Math.Min(capacity, value.Length);
            destination[offset] = (byte)length;
            value.AsSpan(0, length).CopyTo(destination.AsSpan(offset + 1));
        }
    }

    public interface INativeType2RankingLoader
    {
        bool TryLoad(out List<byte[]> records);
    }

    public sealed class MySqlNativeType2RankingLoader : INativeType2RankingLoader
    {
        private static readonly int[] CategoryOrder =
            { 0, 1, 2, 3, 8, 9, 10, 13, 4, 5, 6, 7, 11, 12 };

        public bool TryLoad(out List<byte[]> records)
        {
            records = new List<byte[]>();
            try
            {
                using var connection = OpenConnection();
                if (connection == null) return false;
                foreach (var category in CategoryOrder)
                {
                    if (category == 0)
                        CreateAvailableUsers(connection, heroes: false);
                    if (category == 4)
                        CreateAvailableUsers(connection, heroes: true);
                    if (category is not 11 and not 12)
                    {
                        var rows = ReadCategory(connection, category);
                        records.AddRange(
                            NativeType2RankingPacketBuilder.Create(category, rows));
                    }
                    Thread.Sleep(200);
                }
                return true;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    "[NativeType2Ranking] reload failed: " + ex.Message);
                records.Clear();
                return false;
            }
        }

        private static void CreateAvailableUsers(MySqlConnection connection,
            bool heroes)
        {
            using (var drop = new MySqlCommand(
                       "DROP TEMPORARY TABLE IF EXISTS _AvailUser", connection))
                drop.ExecuteNonQuery();
            using (var create = new MySqlCommand(
                       "CREATE TEMPORARY TABLE _AvailUser(Idx INT PRIMARY KEY)",
                       connection))
                create.ExecuteNonQuery();
            var sql = heroes
                ? @"INSERT INTO _AvailUser
                    SELECT Idx FROM mir3.hero_index
                    WHERE DATE_ADD(ModifyDate, INTERVAL 1 MONTH)>NOW()"
                : @"INSERT INTO _AvailUser
                    SELECT Idx FROM mir3.user_index
                    WHERE Level>0 AND AdminLevel=0
                      AND DATE_ADD(ModifyDate, INTERVAL 1 MONTH)>NOW()";
            using var insert = new MySqlCommand(sql, connection);
            insert.ExecuteNonQuery();
        }

        private static List<NativeType2RankingRow> ReadCategory(
            MySqlConnection connection, int category)
        {
            var sql = CategorySql(category);
            using var command = new MySqlCommand(sql, connection);
            using var reader = command.ExecuteReader();
            var rows = new List<NativeType2RankingRow>();
            while (reader.Read())
            {
                if (category is >= 4 and <= 7)
                {
                    rows.Add(new NativeType2RankingRow
                    {
                        Name = ReadAnsi(reader, 0),
                        HeroName = ReadAnsi(reader, 1),
                        Level = unchecked((ushort)Convert.ToInt32(reader.GetValue(2))),
                        SfLevel = unchecked((uint)Convert.ToInt32(reader.GetValue(3)))
                    });
                }
                else
                {
                    rows.Add(new NativeType2RankingRow
                    {
                        Name = ReadAnsi(reader, 0),
                        Value = unchecked((uint)Convert.ToInt32(reader.GetValue(1))),
                        SfLevel = category is >= 0 and <= 3 or 13
                            ? unchecked((uint)Convert.ToInt32(reader.GetValue(2)))
                            : 0
                    });
                }
            }
            return rows;
        }

        private static string CategorySql(int category) => category switch
        {
            0 or 1 or 2 or 13 =>
                $@"SELECT ChrName, Level, sfLevel
                    FROM mir3.user_index, _AvailUser
                    WHERE _AvailUser.Idx=mir3.user_index.Idx AND Job={(category == 13 ? 3 : category)}
                    ORDER BY Level DESC, sfLevel DESC, ForceLv DESC,
                             Exp DESC, lvChangeTime LIMIT 100",
            3 => @"SELECT ChrName, Level, sfLevel
                   FROM mir3.user_index, _AvailUser
                   WHERE _AvailUser.Idx=mir3.user_index.Idx
                   ORDER BY Level DESC, sfLevel DESC, ForceLv DESC,
                            Exp DESC, lvChangeTime LIMIT 100",
            8 => @"SELECT ChrName, ApprenticeNum
                   FROM mir3.user_index, _AvailUser
                   WHERE _AvailUser.Idx=mir3.user_index.Idx AND ApprenticeNum>0
                   ORDER BY ApprenticeNum DESC, Level DESC, Exp DESC LIMIT 100",
            9 => @"SELECT ChrName, FightPoints
                   FROM mir3.user_index, _AvailUser
                   WHERE _AvailUser.Idx=mir3.user_index.Idx AND FightPoints>0
                   ORDER BY FightPoints DESC, Level DESC, Exp DESC LIMIT 100",
            10 => @"SELECT ChrName, ForceLv
                    FROM mir3.user_index, _AvailUser
                    WHERE _AvailUser.Idx=mir3.user_index.Idx AND ForceLv>0
                    ORDER BY ForceLv DESC, Level DESC, Exp DESC LIMIT 100",
            // NOTE (2026-08-03): the native image also contains a mirStars pair
            //   VA 0x479148: select ChrName, nValue from gamedata.mirStars where sex = 0
            //                Order by nValue desc, level desc, exp desc limit 100;
            //   VA 0x4791C4: ... where sex = 1
            // They are NOT wired here on purpose: the SQL text is Tier-1 (verbatim
            // from the repaired image) but the ranking-category number they map to
            // is NOT proven — those 16 ranking SQL strings have no plain-CODE xref
            // (the builder appears virtualized), so "they are category 11/12" would
            // rest only on the C#-internal CategoryOrder array (Tier-2). Categories
            // 11/12 stay rejected, matching DbSvrServiceRegressionCheck's locked
            // "hidden ranking category" assertion. See task and the census doc.
            4 or 5 or 6 =>
                $@"SELECT MasterName, HeroName, Level, sfLevel
                    FROM mir3.hero_index, _AvailUser
                    WHERE _AvailUser.Idx=mir3.hero_index.Idx AND Job={category - 4}
                    ORDER BY Level DESC, sfLevel DESC, ForceLv DESC,
                             Exp DESC, lvChangeTime LIMIT 100",
            7 => @"SELECT MasterName, HeroName, Level, sfLevel
                   FROM mir3.hero_index, _AvailUser
                   WHERE _AvailUser.Idx=mir3.hero_index.Idx
                   ORDER BY Level DESC, sfLevel DESC, ForceLv DESC,
                            Exp DESC, lvChangeTime LIMIT 100",
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };

        private static byte[] ReadAnsi(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return Array.Empty<byte>();
            var value = reader.GetValue(ordinal);
            if (value is byte[] bytes) return (byte[])bytes.Clone();
            return Encoding.Latin1.GetBytes(Convert.ToString(value) ?? string.Empty);
        }

        private static MySqlConnection OpenConnection()
        {
            MySqlConnection connection = null;
            try
            {
                connection = new MySqlConnection(DBShare.DBConnection);
                connection.Open();
                using var session = new MySqlCommand(
                    "SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED; "
                    + "SET SESSION wait_timeout=2073600", connection);
                session.ExecuteNonQuery();
                return connection;
            }
            catch
            {
                connection?.Dispose();
                return null;
            }
        }
    }

    public sealed class NativeType2RankingReloadCoordinator
    {
        private readonly NativeType2InitializationCache _cache;
        private readonly INativeType2RankingLoader _loader;
        private int _workerRunning;
        private long _lastStartedDateTicks;

        public NativeType2RankingReloadCoordinator(
            NativeType2InitializationCache cache,
            INativeType2RankingLoader loader)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        public event Action RankingsPublished;

        public DateTime LastStartedLocalDate => new(
            Interlocked.Read(ref _lastStartedDateTicks), DateTimeKind.Local);

        public bool TryStartReload()
        {
            if (Interlocked.CompareExchange(ref _workerRunning, 1, 0) != 0)
                return false;
            if (!_cache.TryBeginRankingReload())
            {
                Volatile.Write(ref _workerRunning, 0);
                return false;
            }
            Interlocked.Exchange(ref _lastStartedDateTicks,
                DateTime.Today.Ticks);
            if (!ThreadPool.QueueUserWorkItem(_ => Reload()))
                Reload();
            return true;
        }

        private void Reload()
        {
            try
            {
                if (!_loader.TryLoad(out var records)) return;
                _cache.PublishRankings(records);
                var handlers = RankingsPublished;
                if (handlers == null) return;
                foreach (Action handler in handlers.GetInvocationList())
                {
                    try { handler(); }
                    catch (Exception ex)
                    {
                        DBShare.MainOutMessage(
                            "[NativeType2Ranking] publish callback failed: "
                            + ex.Message);
                    }
                }
            }
            finally { Interlocked.Exchange(ref _workerRunning, 0); }
        }
    }
}
