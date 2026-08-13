using MySql.Data.MySqlClient;
using System.Text;
using SystemModule;

namespace GameSvr.Services
{
    internal interface INativeCorpsStore
    {
        bool TryLoad(out NativeCorpsDataSnapshot snapshot, out string error);

        bool TryInsertMember(long corpsId, NativeCorpsMemberSnapshot member,
            out string error);

        // 4524 create-corps INSERT gamedata.Corps (native CreateCorpsManager
        // sub_5EA28C -> sub_5EC230 builder, STRING 0x005EC340). A default no-op
        // success keeps the many test/fake INativeCorpsStore implementers
        // compiling unchanged; the production NativeCorpsMySqlStore overrides it
        // with the real row insert.
        bool TryInsertCorps(NativeCorpsSnapshot corps, out string error)
        {
            error = string.Empty;
            return true;
        }

        bool TryDeleteMember(long memberId, out string error);

        bool TryExitMember(long memberId, NativeCorpsSnapshot corps,
            bool updateCorps, out string error);

        bool TryUpdateMemberTitle(long memberId, string title,
            out string error);

        bool TryUpdateCorps(NativeCorpsSnapshot corps, out string error);

        bool TryUpdateGild(NativeGildSnapshot gild, out string error);
    }

    internal sealed class NativeCorpsMySqlStore : INativeCorpsStore
    {
        private readonly Func<string> _connectionString;
        private static readonly Encoding StrictGbk = Encoding.GetEncoding(
            HUtil32.GbkEncoding.CodePage, EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        internal NativeCorpsMySqlStore(Func<string> connectionString)
        {
            _connectionString = connectionString ??
                                throw new ArgumentNullException(
                                    nameof(connectionString));
        }

        public bool TryLoad(out NativeCorpsDataSnapshot snapshot,
            out string error)
        {
            snapshot = new NativeCorpsDataSnapshot();
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                LoadCorps(connection, snapshot);
                LoadCorpsMembers(connection, snapshot);
                LoadGilds(connection, snapshot);
                LoadGildMembers(connection, snapshot);
                LoadGildRelations(connection, snapshot);
                try
                {
                    LoadGildConcerns(connection, snapshot);
                }
                catch (Exception concernEx)
                {
                    // Concern rows are best-effort: a missing/locked gildconcern
                    // table must NOT fail the whole Corps/Gild load and break the
                    // existing Guild read path. Degrade to an empty concern set.
                    snapshot.GildConcerns.Clear();
                    M2Share.ErrorMessage(
                        "native gildconcern load skipped: " + concernEx.Message);
                }
                return true;
            }
            catch (Exception ex)
            {
                snapshot = new NativeCorpsDataSnapshot();
                error = "native Corps/Gild load failed: " + ex.Message;
                return false;
            }
        }

        public bool TryInsertMember(long corpsId,
            NativeCorpsMemberSnapshot member, out string error)
        {
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO gamedata.CorpsMember " +
                    "(CorpsID,MemberID,MemberName,MemberLevel,MemberSex," +
                    "MemberJob,Title,LastLoginTime) VALUES " +
                    "(@corpsId,@memberId,@name,@level,@sex,@job,@title,@lastLogin)";
                AddId(command, "@corpsId", corpsId);
                AddId(command, "@memberId", member.MemberId);
                AddGbk(command, "@name", member.Name);
                command.Parameters.Add("@level", MySqlDbType.Int32).Value =
                    member.Level;
                command.Parameters.Add("@sex", MySqlDbType.Int32).Value =
                    member.Sex;
                command.Parameters.Add("@job", MySqlDbType.Int32).Value =
                    member.Job;
                AddGbk(command, "@title", member.Title);
                command.Parameters.Add("@lastLogin", MySqlDbType.DateTime)
                    .Value = member.LastLoginTime;
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "CorpsMember insert affected an unexpected row count");
                transaction.Commit();
                return true;
            }
            catch (Exception ex)
            {
                error = "native CorpsMember insert failed: " + ex.Message;
                return false;
            }
        }

        // 4524 create-corps row insert. Reversed SQL text (STRING 0x005EC340,
        // builder sub_5EC230): "Insert into gamedata.corps Values(%d, now(),
        // '%s', %d, %d, %d, %d, %d, %d, '%s');" — a positional INSERT in Corps
        // column order (ID, CreateTime, CorpsName, OwnerID, ViceOwner1ID,
        // ViceOwner2ID, BanRecruit, RecruitLevelLimit, RecruitJobSet,
        // CorpsNotice). CreateTime = NOW() server-side; the %d/%s placeholders
        // become named parameters and an explicit column list is used (matching
        // this store's other builders). A fresh corps has vice slots 0, no ban,
        // level/jobs 0, empty notice.
        public bool TryInsertCorps(NativeCorpsSnapshot corps, out string error)
        {
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO gamedata.Corps " +
                    "(ID,CreateTime,CorpsName,OwnerID,ViceOwner1ID," +
                    "ViceOwner2ID,BanRecruit,RecruitLevelLimit,RecruitJobSet," +
                    "CorpsNotice) VALUES " +
                    "(@id,NOW(),@name,@owner,@vice1,@vice2,@ban,@level,@jobs," +
                    "@notice)";
                AddId(command, "@id", corps.Id);
                AddGbk(command, "@name", corps.Name);
                AddId(command, "@owner", corps.OwnerId);
                AddId(command, "@vice1", corps.ViceOwner1Id);
                AddId(command, "@vice2", corps.ViceOwner2Id);
                command.Parameters.Add("@ban", MySqlDbType.Int32).Value =
                    corps.BanRecruit ? 1 : 0;
                command.Parameters.Add("@level", MySqlDbType.Int32).Value =
                    corps.RecruitLevelLimit;
                command.Parameters.Add("@jobs", MySqlDbType.Int32).Value =
                    corps.RecruitJobSet;
                AddBinary(command, "@notice", corps.Notice);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "Corps insert affected an unexpected row count");
                transaction.Commit();
                return true;
            }
            catch (Exception ex)
            {
                error = "native Corps insert failed: " + ex.Message;
                return false;
            }
        }

        public bool TryDeleteMember(long memberId, out string error)
        {
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "DELETE FROM gamedata.CorpsMember WHERE MemberID=@memberId";
                AddId(command, "@memberId", memberId);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "CorpsMember delete affected an unexpected row count");
                return true;
            }
            catch (Exception ex)
            {
                error = "native CorpsMember delete failed: " + ex.Message;
                return false;
            }
        }

        public bool TryExitMember(long memberId, NativeCorpsSnapshot corps,
            bool updateCorps, out string error)
        {
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                if (updateCorps)
                {
                    using var update = CreateUpdateCorpsCommand(connection,
                        transaction, corps);
                    if (update.ExecuteNonQuery() > 1)
                        throw new InvalidOperationException(
                            "Corps update affected multiple rows");
                }

                using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText =
                    "DELETE FROM gamedata.CorpsMember WHERE MemberID=@memberId";
                AddId(delete, "@memberId", memberId);
                if (delete.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "CorpsMember delete affected an unexpected row count");
                transaction.Commit();
                return true;
            }
            catch (Exception ex)
            {
                error = "native CorpsMember exit failed: " + ex.Message;
                return false;
            }
        }

        public bool TryUpdateMemberTitle(long memberId, string title,
            out string error)
        {
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "UPDATE gamedata.CorpsMember SET Title=@title " +
                    "WHERE MemberID=@memberId";
                AddGbk(command, "@title", title);
                AddId(command, "@memberId", memberId);
                if (command.ExecuteNonQuery() > 1)
                    throw new InvalidOperationException(
                        "CorpsMember title update affected multiple rows");
                return true;
            }
            catch (Exception ex)
            {
                error = "native CorpsMember title update failed: " +
                        ex.Message;
                return false;
            }
        }

        public bool TryUpdateCorps(NativeCorpsSnapshot corps,
            out string error)
        {
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var command = CreateUpdateCorpsCommand(connection, null,
                    corps);
                if (command.ExecuteNonQuery() > 1)
                    throw new InvalidOperationException(
                        "Corps update affected multiple rows");
                return true;
            }
            catch (Exception ex)
            {
                error = "native Corps update failed: " + ex.Message;
                return false;
            }
        }

        private static MySqlCommand CreateUpdateCorpsCommand(
            MySqlConnection connection, MySqlTransaction transaction,
            NativeCorpsSnapshot corps)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "UPDATE gamedata.Corps SET OwnerID=@owner," +
                "ViceOwner1ID=@vice1,ViceOwner2ID=@vice2," +
                "BanRecruit=@ban,RecruitLevelLimit=@level," +
                "RecruitJobSet=@jobs,CorpsNotice=@notice WHERE ID=@id";
            AddId(command, "@owner", corps.OwnerId);
            AddId(command, "@vice1", corps.ViceOwner1Id);
            AddId(command, "@vice2", corps.ViceOwner2Id);
            command.Parameters.Add("@ban", MySqlDbType.Int32).Value =
                corps.BanRecruit ? 1 : 0;
            command.Parameters.Add("@level", MySqlDbType.Int32).Value =
                corps.RecruitLevelLimit;
            command.Parameters.Add("@jobs", MySqlDbType.Int32).Value =
                corps.RecruitJobSet;
            AddBinary(command, "@notice", corps.Notice);
            AddId(command, "@id", corps.Id);
            return command;
        }

        public bool TryUpdateGild(NativeGildSnapshot gild, out string error)
        {
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "UPDATE gamedata.Gild SET OwnerCorpsID=@owner," +
                    "ViceOwnerID=@vice,GildNotice=@notice WHERE ID=@id";
                AddId(command, "@owner", gild.OwnerCorpsId);
                AddId(command, "@vice", gild.ViceOwnerId);
                AddBinary(command, "@notice", gild.Notice);
                AddId(command, "@id", gild.Id);
                if (command.ExecuteNonQuery() > 1)
                    throw new InvalidOperationException(
                        "Gild update affected multiple rows");
                return true;
            }
            catch (Exception ex)
            {
                error = "native Gild update failed: " + ex.Message;
                return false;
            }
        }

        private MySqlConnection OpenConnection()
        {
            var connectionString = _connectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "database connection string is empty");
            var connection = new MySqlConnection(connectionString);
            connection.Open();
            return connection;
        }

        private static void LoadCorps(MySqlConnection connection,
            NativeCorpsDataSnapshot snapshot)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT ID,CreateTime,CAST(CorpsName AS BINARY),OwnerID," +
                "ViceOwner1ID,ViceOwner2ID,BanRecruit,RecruitLevelLimit," +
                "RecruitJobSet,CAST(CorpsNotice AS BINARY) " +
                "FROM gamedata.Corps ORDER BY ID";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var corps = new NativeCorpsSnapshot
                {
                    Id = ReadId(reader, 0),
                    CreateTime = reader.GetDateTime(1),
                    Name = ReadGbk(reader, 2),
                    OwnerId = ReadId(reader, 3),
                    ViceOwner1Id = ReadNullableId(reader, 4),
                    ViceOwner2Id = ReadNullableId(reader, 5),
                    BanRecruit = !reader.IsDBNull(6)
                                 && Convert.ToInt32(reader.GetValue(6)) != 0,
                    RecruitLevelLimit = reader.IsDBNull(7)
                        ? (ushort)0
                        : unchecked((ushort)Convert.ToInt32(
                            reader.GetValue(7))),
                    RecruitJobSet = reader.IsDBNull(8)
                        ? (byte)0
                        : unchecked((byte)Convert.ToInt32(
                            reader.GetValue(8))),
                    Notice = ReadBinary(reader, 9)
                };
                if (corps.Id == 0 || !snapshot.CorpsById.TryAdd(corps.Id,
                        corps))
                    throw new InvalidDataException(
                        $"invalid or duplicate Corps ID {corps.Id}");
            }
        }

        private static void LoadCorpsMembers(MySqlConnection connection,
            NativeCorpsDataSnapshot snapshot)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT CorpsID,MemberID,CAST(MemberName AS BINARY)," +
                "MemberLevel,MemberSex,MemberJob,CAST(Title AS BINARY)," +
                "LastLoginTime FROM gamedata.CorpsMember ORDER BY MemberID";
            using var reader = command.ExecuteReader();
            var memberIds = new HashSet<long>();
            while (reader.Read())
            {
                var corpsId = ReadId(reader, 0);
                var memberId = ReadId(reader, 1);
                if (!snapshot.CorpsById.TryGetValue(corpsId, out var corps)
                    || memberId == 0 || !memberIds.Add(memberId))
                    continue;
                corps.Members.Add(new NativeCorpsMemberSnapshot
                {
                    MemberId = memberId,
                    Name = ReadGbk(reader, 2),
                    Level = unchecked((ushort)Convert.ToInt32(
                        reader.GetValue(3))),
                    Sex = unchecked((byte)Convert.ToInt32(reader.GetValue(4))),
                    Job = unchecked((byte)Convert.ToInt32(reader.GetValue(5))),
                    Title = ReadGbk(reader, 6),
                    LastLoginTime = reader.GetDateTime(7)
                });
            }
        }

        private static void LoadGilds(MySqlConnection connection,
            NativeCorpsDataSnapshot snapshot)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT ID,CreateTime,CAST(GildName AS BINARY),OwnerCorpsID," +
                "ViceOwnerID,CAST(GildNotice AS BINARY) " +
                "FROM gamedata.Gild ORDER BY ID";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var gild = new NativeGildSnapshot
                {
                    Id = ReadId(reader, 0),
                    CreateTime = reader.GetDateTime(1),
                    Name = ReadGbk(reader, 2),
                    OwnerCorpsId = ReadId(reader, 3),
                    ViceOwnerId = ReadNullableId(reader, 4),
                    Notice = ReadBinary(reader, 5)
                };
                if (gild.Id == 0 || !snapshot.GildById.TryAdd(gild.Id, gild))
                    throw new InvalidDataException(
                        $"invalid or duplicate Gild ID {gild.Id}");
            }
        }

        private static void LoadGildMembers(MySqlConnection connection,
            NativeCorpsDataSnapshot snapshot)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT GildID,CorpsID FROM gamedata.GildMember " +
                "ORDER BY GildID,CorpsID";
            using var reader = command.ExecuteReader();
            var corpsIds = new HashSet<long>();
            while (reader.Read())
            {
                var gildId = ReadId(reader, 0);
                var corpsId = ReadId(reader, 1);
                if (!snapshot.GildById.TryGetValue(gildId, out var gild)
                    || !snapshot.CorpsById.ContainsKey(corpsId)
                    || !corpsIds.Add(corpsId))
                    continue;
                gild.CorpsIds.Add(corpsId);
            }
        }

        private static void LoadGildRelations(MySqlConnection connection,
            NativeCorpsDataSnapshot snapshot)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT GildID1,GildID2,Relation,CreateTime " +
                "FROM gamedata.GildRelation ORDER BY GildID1,GildID2";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var first = ReadId(reader, 0);
                var second = ReadId(reader, 1);
                var relation = unchecked((byte)Convert.ToInt32(
                    reader.GetValue(2)));
                var createTime = reader.GetDateTime(3);
                // Native loader 0x5E8D80 `8A45E0 mov al,[ebp-0x20]` /
                // 0x5E8D83 `2C04 sub al,4` / 0x5E8D85 `72 38 jb` admits the
                // whole 0..3 domain, and 0x5E8EAA `33C9 xor ecx,ecx` /
                // `8A4DE0 mov cl,[ebp-0x20]` / 0x5E8EB5 `call 0x49F9C8` puts
                // every admitted row into the relation map unconditionally.
                // Only 2 (0x5E8E64) and 1 (0x5E8E90) additionally join the
                // hostile/union lists; 0 and 3 are map-only.
                if (first == 0 || second == 0 || first == second
                    || !snapshot.GildById.ContainsKey(first)
                    || !snapshot.GildById.ContainsKey(second)
                    || relation > 3)
                    continue;
                var key = NativeCorpsDataSnapshot.GildRelationKey(first,
                    second);
                if (!snapshot.GildRelations.TryAdd(key, (relation, createTime)))
                    throw new InvalidDataException(
                        $"duplicate Gild relation {first}/{second}");
            }
        }

        // gamedata.gildconcern (source GildID -> destination DstGildID), the
        // watch/concern list. Reversed loader SELECT is Idx-paged
        // (0x5E8890); a single ordered SELECT loads the same rows here (matching
        // the other Load* helpers). Only the source gild must exist; a dangling
        // destination is kept verbatim (membership checks handle it later).
        private static void LoadGildConcerns(MySqlConnection connection,
            NativeCorpsDataSnapshot snapshot)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT GildID,DstGildID FROM gamedata.gildconcern " +
                "ORDER BY GildID,DstGildID";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var gildId = ReadId(reader, 0);
                var dstGildId = ReadId(reader, 1);
                if (gildId == 0 || dstGildId == 0
                    || !snapshot.GildById.ContainsKey(gildId))
                    continue;
                if (!snapshot.GildConcerns.TryGetValue(gildId, out var list))
                {
                    list = new List<long>();
                    snapshot.GildConcerns.Add(gildId, list);
                }
                if (!list.Contains(dstGildId)) list.Add(dstGildId);
            }
        }

        private static long ReadId(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return 0;
            return unchecked((long)Convert.ToUInt64(reader.GetValue(ordinal)));
        }

        private static long ReadNullableId(MySqlDataReader reader,
            int ordinal) => reader.IsDBNull(ordinal) ? 0 : ReadId(reader, ordinal);

        private static string ReadGbk(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return string.Empty;
            var value = reader.GetValue(ordinal);
            if (value is byte[] bytes) return StrictGbk.GetString(bytes);
            return Convert.ToString(value) ?? string.Empty;
        }

        private static void AddId(MySqlCommand command, string name, long id)
        {
            command.Parameters.Add(name, MySqlDbType.UInt64).Value =
                unchecked((ulong)id);
        }

        private static void AddGbk(MySqlCommand command, string name,
            string value)
        {
            command.Parameters.Add(name, MySqlDbType.VarBinary).Value =
                StrictGbk.GetBytes(value ?? string.Empty);
        }

        private static byte[] ReadBinary(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return Array.Empty<byte>();
            if (reader.GetValue(ordinal) is byte[] bytes)
                return (byte[])bytes.Clone();
            throw new InvalidDataException(
                $"column {ordinal} did not return binary data");
        }

        private static void AddBinary(MySqlCommand command, string name,
            byte[] value)
        {
            command.Parameters.Add(name, MySqlDbType.VarBinary).Value =
                value ?? Array.Empty<byte>();
        }
    }
}
