using System.Data;
using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr.Services
{
    internal sealed class NativeRelationMySqlStore : INativeRelationStore
    {
        private const string Columns =
            "Idx,RelationState,FirstPlayerID,FirstChrName,FirstLevel," +
            "FirstJob,FirstFocusColor,SecPlayerID,SecChrName,SecLevel," +
            "SecJob,SecFocusColor";

        private readonly Func<string> _connectionString;

        internal NativeRelationMySqlStore(Func<string> connectionString)
        {
            _connectionString = connectionString ??
                                throw new ArgumentNullException(
                                    nameof(connectionString));
        }

        public bool TryLoad(long ownerId, NativeRelationKind kind,
            out IReadOnlyList<NativeRelationEntry> entries)
        {
            entries = Array.Empty<NativeRelationEntry>();
            if (!TryOpen(out var connection)) return false;
            try
            {
                using (connection)
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        $"SELECT {Columns} FROM gamedata.relation " +
                        "WHERE FirstPlayerID=@ownerId OR SecPlayerID=@ownerId " +
                        "ORDER BY Idx";
                    command.Parameters.Add("@ownerId", MySqlDbType.Int64)
                        .Value = ownerId;
                    var rows = ReadRows(command);
                    entries = rows
                        .Where(row => row.Has(ownerId, kind))
                        .Select(row => new
                        {
                            row.Index,
                            Entry = row.GetOther(ownerId)
                        })
                        .OrderByDescending(item => item.Entry.Level)
                        .ThenBy(item => item.Index)
                        .Take(NativeRelationService.Limit)
                        .Select(item => item.Entry)
                        .ToArray();
                }
                return true;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[NativeRelation] query failed: " + ex.Message);
                entries = Array.Empty<NativeRelationEntry>();
                return false;
            }
        }

        public bool TryInspect(long ownerId, long targetId,
            NativeRelationKind kind, out int count, out bool contains)
        {
            count = 0;
            contains = false;
            if (!TryOpen(out var connection)) return false;
            try
            {
                using (connection)
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        $"SELECT {Columns} FROM gamedata.relation " +
                        "WHERE FirstPlayerID=@ownerId OR SecPlayerID=@ownerId " +
                        "ORDER BY Idx";
                    command.Parameters.Add("@ownerId", MySqlDbType.Int64)
                        .Value = ownerId;
                    var rows = ReadRows(command);
                    count = rows.Count(row => row.Has(ownerId, kind));
                    contains = rows.Any(row => row.IsPair(ownerId, targetId)
                                               && row.Has(ownerId, kind));
                }
                return true;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[NativeRelation] inspect failed: " + ex.Message);
                count = 0;
                contains = false;
                return false;
            }
        }

        public NativeRelationStoreResult TryAddDirected(
            NativeRelationPlayer owner, NativeRelationPlayer target,
            NativeRelationKind kind, byte focusColor, int limit)
        {
            if (owner == null || target == null || owner.UserId == 0
                || target.UserId == 0 || owner.UserId == target.UserId
                || kind == NativeRelationKind.Friend)
                return NativeRelationStoreResult.Failed;
            if (!TryOpen(out var connection))
                return NativeRelationStoreResult.Failed;

            try
            {
                using (connection)
                using (var transaction = connection.BeginTransaction(
                           IsolationLevel.Serializable))
                {
                    var rows = LockRows(connection, transaction, owner.UserId);
                    if (rows.Count(row => row.Has(owner.UserId, kind)) >= limit)
                        return Rollback(transaction,
                            NativeRelationStoreResult.Full);

                    var pair = rows.FirstOrDefault(row =>
                        row.IsPair(owner.UserId, target.UserId));
                    if (pair != null && pair.Has(owner.UserId, kind))
                        return Rollback(transaction,
                            NativeRelationStoreResult.Duplicate);

                    var written = pair == null
                        ? InsertPair(connection, transaction, owner, target,
                            NativeRelationStateBits.ForOwner(kind, true),
                            0, kind == NativeRelationKind.Attention
                                ? focusColor
                                : (byte)0)
                        : AddToPair(connection, transaction, pair, owner,
                            target, kind, focusColor);
                    if (!written)
                        return Rollback(transaction,
                            NativeRelationStoreResult.Failed);

                    transaction.Commit();
                    return NativeRelationStoreResult.Success;
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[NativeRelation] directed add failed: " + ex.Message);
                return NativeRelationStoreResult.Failed;
            }
        }

        public NativeRelationStoreResult TryAddFriend(
            NativeRelationPlayer requester, NativeRelationPlayer accepter,
            int limit)
        {
            if (requester == null || accepter == null || requester.UserId == 0
                || accepter.UserId == 0 || requester.UserId == accepter.UserId)
                return NativeRelationStoreResult.Failed;
            if (!TryOpen(out var connection))
                return NativeRelationStoreResult.Failed;

            try
            {
                using (connection)
                using (var transaction = connection.BeginTransaction(
                           IsolationLevel.Serializable))
                {
                    var rows = LockRows(connection, transaction,
                        requester.UserId, accepter.UserId);
                    var pair = rows.FirstOrDefault(row =>
                        row.IsPair(requester.UserId, accepter.UserId));
                    if (pair != null
                        && (pair.Has(requester.UserId,
                                NativeRelationKind.Friend)
                            || pair.Has(accepter.UserId,
                                NativeRelationKind.Friend)))
                        return Rollback(transaction,
                            NativeRelationStoreResult.Duplicate);

                    if (rows.Count(row => row.Has(requester.UserId,
                            NativeRelationKind.Friend)) >= limit
                        || rows.Count(row => row.Has(accepter.UserId,
                            NativeRelationKind.Friend)) >= limit)
                        return Rollback(transaction,
                            NativeRelationStoreResult.Full);

                    var written = pair == null
                        ? InsertPair(connection, transaction, requester,
                            accepter, NativeRelationStateBits.Friend, 0, 0)
                        : AddToPair(connection, transaction, pair, requester,
                            accepter, NativeRelationKind.Friend, 0);
                    if (!written)
                        return Rollback(transaction,
                            NativeRelationStoreResult.Failed);

                    transaction.Commit();
                    return NativeRelationStoreResult.Success;
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[NativeRelation] friend add failed: " + ex.Message);
                return NativeRelationStoreResult.Failed;
            }
        }

        public NativeRelationStoreResult TryRemove(long ownerId,
            string targetName, NativeRelationKind kind)
        {
            if (ownerId == 0 || string.IsNullOrEmpty(targetName))
                return NativeRelationStoreResult.Missing;
            if (!TryOpen(out var connection))
                return NativeRelationStoreResult.Failed;

            try
            {
                using (connection)
                using (var transaction = connection.BeginTransaction(
                           IsolationLevel.Serializable))
                {
                    var rows = LockRows(connection, transaction, ownerId);
                    var pair = rows.FirstOrDefault(row =>
                        row.Has(ownerId, kind)
                        && string.Equals(row.GetOther(ownerId).Name,
                            targetName, StringComparison.OrdinalIgnoreCase));
                    if (pair == null)
                        return Rollback(transaction,
                            NativeRelationStoreResult.Missing);

                    var ownerIsFirst = pair.First.UserId == ownerId;
                    var state = pair.State &
                                ~NativeRelationStateBits.ForOwner(kind,
                                    ownerIsFirst);
                    var written = state == 0
                        ? DeletePair(connection, transaction, pair.Index)
                        : UpdateState(connection, transaction, pair.Index,
                            state);
                    if (!written)
                        return Rollback(transaction,
                            NativeRelationStoreResult.Failed);

                    transaction.Commit();
                    return NativeRelationStoreResult.Success;
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[NativeRelation] delete failed: " + ex.Message);
                return NativeRelationStoreResult.Failed;
            }
        }

        public NativeRelationStoreResult TryUpdateAttentionColor(long ownerId,
            string targetName, byte color)
        {
            if (ownerId == 0 || string.IsNullOrEmpty(targetName))
                return NativeRelationStoreResult.Missing;
            if (!TryOpen(out var connection))
                return NativeRelationStoreResult.Failed;

            try
            {
                using (connection)
                using (var transaction = connection.BeginTransaction(
                           IsolationLevel.Serializable))
                {
                    var rows = LockRows(connection, transaction, ownerId);
                    var pair = rows.FirstOrDefault(row =>
                        row.Has(ownerId, NativeRelationKind.Attention)
                        && string.Equals(row.GetOther(ownerId).Name,
                            targetName, StringComparison.OrdinalIgnoreCase));
                    if (pair == null)
                        return Rollback(transaction,
                            NativeRelationStoreResult.Missing);

                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = pair.First.UserId == ownerId
                        ? "UPDATE gamedata.relation SET SecFocusColor=@color," +
                          "ModifyDate=Now() WHERE Idx=@idx"
                        : "UPDATE gamedata.relation SET FirstFocusColor=@color," +
                          "ModifyDate=Now() WHERE Idx=@idx";
                    update.Parameters.Add("@color", MySqlDbType.Byte).Value =
                        color;
                    update.Parameters.Add("@idx", MySqlDbType.Int32).Value =
                        pair.Index;
                    if (update.ExecuteNonQuery() < 0)
                        return Rollback(transaction,
                            NativeRelationStoreResult.Failed);

                    transaction.Commit();
                    return NativeRelationStoreResult.Success;
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[NativeRelation] color update failed: " + ex.Message);
                return NativeRelationStoreResult.Failed;
            }
        }

        private static List<RelationRow> LockRows(MySqlConnection connection,
            MySqlTransaction transaction, long firstId, long? secondId = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = secondId.HasValue
                ? $"SELECT {Columns} FROM gamedata.relation WHERE " +
                  "FirstPlayerID=@firstId OR SecPlayerID=@firstId OR " +
                  "FirstPlayerID=@secondId OR SecPlayerID=@secondId " +
                  "ORDER BY Idx FOR UPDATE"
                : $"SELECT {Columns} FROM gamedata.relation WHERE " +
                  "FirstPlayerID=@firstId OR SecPlayerID=@firstId " +
                  "ORDER BY Idx FOR UPDATE";
            command.Parameters.Add("@firstId", MySqlDbType.Int64).Value =
                firstId;
            if (secondId.HasValue)
                command.Parameters.Add("@secondId", MySqlDbType.Int64).Value =
                    secondId.Value;
            return ReadRows(command);
        }

        private static List<RelationRow> ReadRows(MySqlCommand command)
        {
            var rows = new List<RelationRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new RelationRow(
                    Convert.ToInt32(reader.GetValue(0)),
                    Convert.ToUInt32(reader.GetValue(1)),
                    new NativeRelationPlayer(
                        Convert.ToInt64(reader.GetValue(2)),
                        ReadGbk(reader, 3),
                        Convert.ToUInt16(reader.GetValue(4)),
                        Convert.ToByte(reader.GetValue(5))),
                    Convert.ToByte(reader.GetValue(6)),
                    new NativeRelationPlayer(
                        Convert.ToInt64(reader.GetValue(7)),
                        ReadGbk(reader, 8),
                        Convert.ToUInt16(reader.GetValue(9)),
                        Convert.ToByte(reader.GetValue(10))),
                    Convert.ToByte(reader.GetValue(11))));
            }
            return rows;
        }

        private static bool AddToPair(MySqlConnection connection,
            MySqlTransaction transaction, RelationRow row,
            NativeRelationPlayer owner, NativeRelationPlayer target,
            NativeRelationKind kind, byte focusColor)
        {
            var ownerIsFirst = row.First.UserId == owner.UserId;
            var first = ownerIsFirst ? owner : target;
            var second = ownerIsFirst ? target : owner;
            var firstFocus = row.FirstFocusColor;
            var secondFocus = row.SecondFocusColor;
            if (kind == NativeRelationKind.Attention)
            {
                if (ownerIsFirst) secondFocus = focusColor;
                else firstFocus = focusColor;
            }

            var state = row.State |
                        NativeRelationStateBits.ForOwner(kind, ownerIsFirst);
            return UpdatePair(connection, transaction, row.Index, state,
                first, firstFocus, second, secondFocus);
        }

        private static bool InsertPair(MySqlConnection connection,
            MySqlTransaction transaction, NativeRelationPlayer first,
            NativeRelationPlayer second, uint state, byte firstFocus,
            byte secondFocus)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO gamedata.relation(" +
                "RelationState,FirstPlayerID,FirstChrName,FirstLevel," +
                "FirstJob,FirstFocusColor,SecPlayerID,SecChrName,SecLevel," +
                "SecJob,SecFocusColor,CreateDate,ModifyDate) VALUES(" +
                "@state,@firstId,@firstName,@firstLevel,@firstJob," +
                "@firstFocus,@secondId,@secondName,@secondLevel,@secondJob," +
                "@secondFocus,Now(),Now())";
            BindPair(insert, state, first, firstFocus, second, secondFocus);
            return insert.ExecuteNonQuery() == 1;
        }

        private static bool UpdatePair(MySqlConnection connection,
            MySqlTransaction transaction, int index, uint state,
            NativeRelationPlayer first, byte firstFocus,
            NativeRelationPlayer second, byte secondFocus)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                "UPDATE gamedata.relation SET RelationState=@state," +
                "FirstPlayerID=@firstId,FirstChrName=@firstName," +
                "FirstLevel=@firstLevel,FirstJob=@firstJob," +
                "FirstFocusColor=@firstFocus,SecPlayerID=@secondId," +
                "SecChrName=@secondName,SecLevel=@secondLevel," +
                "SecJob=@secondJob,SecFocusColor=@secondFocus," +
                "ModifyDate=Now() WHERE Idx=@idx";
            BindPair(update, state, first, firstFocus, second, secondFocus);
            update.Parameters.Add("@idx", MySqlDbType.Int32).Value = index;
            return update.ExecuteNonQuery() == 1;
        }

        private static void BindPair(MySqlCommand command, uint state,
            NativeRelationPlayer first, byte firstFocus,
            NativeRelationPlayer second, byte secondFocus)
        {
            command.Parameters.Add("@state", MySqlDbType.UInt32).Value = state;
            command.Parameters.Add("@firstId", MySqlDbType.Int64).Value =
                first.UserId;
            AddGbkBinary(command, "@firstName", first.Name);
            command.Parameters.Add("@firstLevel", MySqlDbType.UInt16).Value =
                first.Level;
            command.Parameters.Add("@firstJob", MySqlDbType.Byte).Value =
                first.Job;
            command.Parameters.Add("@firstFocus", MySqlDbType.Byte).Value =
                firstFocus;
            command.Parameters.Add("@secondId", MySqlDbType.Int64).Value =
                second.UserId;
            AddGbkBinary(command, "@secondName", second.Name);
            command.Parameters.Add("@secondLevel", MySqlDbType.UInt16).Value =
                second.Level;
            command.Parameters.Add("@secondJob", MySqlDbType.Byte).Value =
                second.Job;
            command.Parameters.Add("@secondFocus", MySqlDbType.Byte).Value =
                secondFocus;
        }

        private static bool DeletePair(MySqlConnection connection,
            MySqlTransaction transaction, int index)
        {
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText =
                "DELETE FROM gamedata.relation WHERE Idx=@idx";
            delete.Parameters.Add("@idx", MySqlDbType.Int32).Value = index;
            return delete.ExecuteNonQuery() == 1;
        }

        private static bool UpdateState(MySqlConnection connection,
            MySqlTransaction transaction, int index, uint state)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                "UPDATE gamedata.relation SET RelationState=@state," +
                "ModifyDate=Now() WHERE Idx=@idx";
            update.Parameters.Add("@state", MySqlDbType.UInt32).Value = state;
            update.Parameters.Add("@idx", MySqlDbType.Int32).Value = index;
            return update.ExecuteNonQuery() == 1;
        }

        private bool TryOpen(out MySqlConnection connection)
        {
            connection = null;
            var connectionString = _connectionString();
            if (string.IsNullOrWhiteSpace(connectionString)) return false;
            try
            {
                connection = new MySqlConnection(connectionString);
                connection.Open();
                return true;
            }
            catch (Exception ex)
            {
                connection?.Dispose();
                connection = null;
                M2Share.ErrorMessage(
                    "[NativeRelation] database open failed: " + ex.Message);
                return false;
            }
        }

        private static NativeRelationStoreResult Rollback(
            MySqlTransaction transaction, NativeRelationStoreResult result)
        {
            transaction.Rollback();
            return result;
        }

        private static void AddGbkBinary(MySqlCommand command, string name,
            string value)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            if (bytes.Length > NativeRelationWireCodec.NameSize)
                bytes = bytes[..NativeRelationWireCodec.NameSize];
            command.Parameters.Add(name, MySqlDbType.VarBinary,
                Math.Max(1, bytes.Length)).Value = bytes;
        }

        private static string ReadGbk(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return string.Empty;
            var value = reader.GetValue(ordinal);
            byte[] bytes;
            if (value is byte[] binary)
            {
                bytes = binary;
            }
            else if (value is string text && text.Any(ch => ch > byte.MaxValue))
            {
                return text.TrimEnd('\0', ' ');
            }
            else
            {
                bytes = System.Text.Encoding.Latin1.GetBytes(
                    Convert.ToString(value) ?? string.Empty);
            }

            var length = bytes.Length;
            while (length > 0
                   && (bytes[length - 1] == 0 || bytes[length - 1] == 0x20))
                length--;
            return HUtil32.GbkEncoding.GetString(bytes, 0, length);
        }

        private sealed class RelationRow
        {
            internal RelationRow(int index, uint state,
                NativeRelationPlayer first, byte firstFocusColor,
                NativeRelationPlayer second, byte secondFocusColor)
            {
                Index = index;
                State = state;
                First = first;
                FirstFocusColor = firstFocusColor;
                Second = second;
                SecondFocusColor = secondFocusColor;
            }

            internal int Index { get; }
            internal uint State { get; }
            internal NativeRelationPlayer First { get; }
            internal byte FirstFocusColor { get; }
            internal NativeRelationPlayer Second { get; }
            internal byte SecondFocusColor { get; }

            internal bool IsPair(long firstId, long secondId)
            {
                return First.UserId == firstId && Second.UserId == secondId
                       || First.UserId == secondId && Second.UserId == firstId;
            }

            internal bool Has(long ownerId, NativeRelationKind kind)
            {
                if (First.UserId != ownerId && Second.UserId != ownerId)
                    return false;
                var bit = NativeRelationStateBits.ForOwner(kind,
                    First.UserId == ownerId);
                return bit != 0 && (State & bit) == bit;
            }

            internal NativeRelationEntry GetOther(long ownerId)
            {
                var ownerIsFirst = First.UserId == ownerId;
                var other = ownerIsFirst ? Second : First;
                var focus = ownerIsFirst
                    ? SecondFocusColor
                    : FirstFocusColor;
                return new NativeRelationEntry(other.UserId, other.Name,
                    other.Level, other.Job, focus);
            }
        }
    }
}
