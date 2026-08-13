using MySql.Data.MySqlClient;
using System.Text;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Write side of the original M2Server social-org persistence for the GUILD
    /// family. In the original the writes are enqueued (sub_5E639C into the
    /// global queue off_7D5AC8) and later executed in-process against MySQL by a
    /// background worker that calls TMySQLDB.ExecuteScript (sub_724E48) on the
    /// embedded client off_7D5C40 — i.e. direct SQL to the `gamedata` schema,
    /// never a DBServer socket command. Each execute helper (sub_5EC3B0 family:
    /// sub_5E9620 / sub_5E9B74 / sub_5E9C84 …) formats the SQL text with
    /// sub_40DCC0 and, on ExecuteScript failure, only logs "[SQL Failed] " with
    /// no rollback of the already-published in-memory change.
    ///
    /// This store mirrors <see cref="NativeCorpsMySqlStore"/> (OpenConnection,
    /// StrictGbk, parameterization). It is deliberately fail-safe: a SQL error
    /// is surfaced as a false return + message (the async
    /// <c>NativeSocialPersistenceQueue</c> worker logs it), and no affected-row
    /// count is asserted — the original only checks the ExecuteScript boolean,
    /// exactly as here.
    ///
    /// Exact reversed SQL text lives in the binary's .rdata; each builder cites
    /// the source string address (image base 0x400000) and the command
    /// builder/execute functions it corresponds to.
    /// </summary>
    public interface INativeGildStore
    {
        bool TryCreateGild(long gildId, string name, long ownerCorpsId,
            long viceOwnerId, out string error);

        bool TrySaveGild(long gildId, long ownerCorpsId, long viceOwnerId,
            byte[] notice, out string error);

        bool TryInsertGildMember(long gildId, long corpsId, out string error);

        bool TryDeleteGildMember(long gildId, long corpsId, out string error);

        bool TryInsertGildRelation(long gildId1, long gildId2, int relation,
            DateTime createTime, out string error);

        bool TryDeleteGildRelation(long gildId1, long gildId2,
            out string error);

        bool TryInsertGildConcern(long gildId, long destinationGildId,
            out string error);

        bool TryDeleteGildConcern(long gildId, long destinationGildId,
            out string error);
    }

    /// <summary>
    /// How a <see cref="NativeGildSqlParameter"/> value is bound. Kept as a
    /// store-local enum so the compat-check assembly can inspect a built command
    /// without a reference to MySql.Data. Materialization maps these to the same
    /// MySqlDbType / GBK conventions used by <see cref="NativeCorpsMySqlStore"/>.
    /// </summary>
    public enum NativeGildSqlValueKind
    {
        /// <summary>bigint(20) unsigned id — MySqlDbType.UInt64 (AddId).</summary>
        Id,

        /// <summary>int column — MySqlDbType.Int32.</summary>
        Int32,

        /// <summary>latin1_bin text carried as raw GBK bytes (AddGbk).</summary>
        GbkText,

        /// <summary>latin1_bin blob carried verbatim (AddBinary).</summary>
        Binary,

        /// <summary>datetime — MySqlDbType.DateTime.</summary>
        DateTime
    }

    public readonly struct NativeGildSqlParameter
    {
        public NativeGildSqlParameter(string name, NativeGildSqlValueKind kind,
            object value)
        {
            Name = name;
            Kind = kind;
            Value = value;
        }

        public string Name { get; }
        public NativeGildSqlValueKind Kind { get; }
        public object Value { get; }
    }

    /// <summary>
    /// Pure description of one parameterized statement: the CommandText plus its
    /// ordered bound parameters. Produced by the static Build* methods so the
    /// SQL text can be asserted against the reversed original with test inputs
    /// and no live database.
    /// </summary>
    public sealed class NativeGildSqlCommand
    {
        public NativeGildSqlCommand(string commandText,
            IReadOnlyList<NativeGildSqlParameter> parameters)
        {
            CommandText = commandText;
            Parameters = parameters;
        }

        public string CommandText { get; }
        public IReadOnlyList<NativeGildSqlParameter> Parameters { get; }
    }

    public sealed class NativeGildMySqlStore : INativeGildStore
    {
        // ---- Reversed SQL, parameterized. Each const is the 1:1 translation of
        // ---- the original sprintf template (kept alongside for review). Column
        // ---- lists, order, table-name casing and WHERE clauses match exactly;
        // ---- only the %d/%s placeholders become @named parameters and the
        // ---- '%s'/"%s" quoting is dropped (the value travels as a bound param).

        // sub_5E9414: Insert into gamedata.Gild(ID, CreateTime, GildName,
        //   OwnerCorpsID, ViceOwnerID)   Values(%d, now(), '%s', %d, %d);
        // (create-gild INSERT; make_save_gild family builds the row object)
        public const string CreateGildSql =
            "INSERT INTO gamedata.Gild(ID,CreateTime,GildName,OwnerCorpsID," +
            "ViceOwnerID) VALUES(@id,NOW(),@name,@owner,@vice)";

        // sub_5E9568: update gamedata.Gild set OwnerCorpsID = %d,
        //   ViceOwnerID = %d, GildNotice = '%s' where ID = %d;
        // (save-gild UPDATE; builder make_save_gild sub_5E926C)
        public const string SaveGildSql =
            "UPDATE gamedata.Gild SET OwnerCorpsID=@owner,ViceOwnerID=@vice," +
            "GildNotice=@notice WHERE ID=@id";

        // sub_5E96D4: Insert into gamedata.gildmember(GildID, CorpsID)
        //   Values(%d, %d);
        public const string InsertGildMemberSql =
            "INSERT INTO gamedata.gildmember(GildID,CorpsID) " +
            "VALUES(@gild,@corps)";

        // sub_5E97E4: delete from gamedata.gildmember where GildID = %d
        //   and CorpsID = %d;  (builder make_delete_gild_member sub_5E95E0,
        //   execute sub_5E9620)
        public const string DeleteGildMemberSql =
            "DELETE FROM gamedata.gildmember WHERE GildID=@gild AND " +
            "CorpsID=@corps";

        // sub_5E998C: Insert into gamedata.gildrelation(GildID1, GildID2,
        //   Relation, CreateTime)   Values(%d, %d, %d, "%s");
        // (save_relation sub_5E6E60 -> sub_5E9840; Relation column carries the
        //  1/2/3 relation type, CreateTime is the operation timestamp)
        public const string InsertGildRelationSql =
            "INSERT INTO gamedata.gildrelation(GildID1,GildID2,Relation," +
            "CreateTime) VALUES(@g1,@g2,@relation,@created)";

        // sub_5E9AC0: delete from gamedata.gildrelation where GildID1 = %d
        //   and GildID2 = %d;
        public const string DeleteGildRelationSql =
            "DELETE FROM gamedata.gildrelation WHERE GildID1=@g1 AND " +
            "GildID2=@g2";

        // sub_5E9C28: Insert into gamedata.gildconcern(GildID, DstGildID)
        //   Values(%d, %d);  (execute sub_5E9B74)
        public const string InsertGildConcernSql =
            "INSERT INTO gamedata.gildconcern(GildID,DstGildID) " +
            "VALUES(@gild,@dst)";

        // sub_5E9D38: delete from gamedata.gildconcern where GildID = %d
        //   and DstGildID = %d;  (builder make_delete_concern sub_5E9B20,
        //   execute sub_5E9C84 — matches NativeGildConcernDeleteCommand)
        public const string DeleteGildConcernSql =
            "DELETE FROM gamedata.gildconcern WHERE GildID=@gild AND " +
            "DstGildID=@dst";

        private readonly Func<string> _connectionString;
        private static readonly Encoding StrictGbk = Encoding.GetEncoding(
            HUtil32.GbkEncoding.CodePage, EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        public NativeGildMySqlStore(Func<string> connectionString)
        {
            _connectionString = connectionString ??
                                throw new ArgumentNullException(
                                    nameof(connectionString));
        }

        // ---- Pure command builders (no connection). The compat check drives
        // ---- these with test inputs and asserts CommandText + bound params.

        public static NativeGildSqlCommand BuildCreateGild(long gildId,
            string name, long ownerCorpsId, long viceOwnerId) =>
            new(CreateGildSql, new[]
            {
                Id("@id", gildId),
                Gbk("@name", name),
                Id("@owner", ownerCorpsId),
                Id("@vice", viceOwnerId)
            });

        public static NativeGildSqlCommand BuildSaveGild(long gildId,
            long ownerCorpsId, long viceOwnerId, byte[] notice) =>
            new(SaveGildSql, new[]
            {
                Id("@owner", ownerCorpsId),
                Id("@vice", viceOwnerId),
                Bin("@notice", notice),
                Id("@id", gildId)
            });

        public static NativeGildSqlCommand BuildInsertGildMember(long gildId,
            long corpsId) =>
            new(InsertGildMemberSql, new[]
            {
                Id("@gild", gildId),
                Id("@corps", corpsId)
            });

        public static NativeGildSqlCommand BuildDeleteGildMember(long gildId,
            long corpsId) =>
            new(DeleteGildMemberSql, new[]
            {
                Id("@gild", gildId),
                Id("@corps", corpsId)
            });

        public static NativeGildSqlCommand BuildInsertGildRelation(
            long gildId1, long gildId2, int relation, DateTime createTime) =>
            new(InsertGildRelationSql, new[]
            {
                Id("@g1", gildId1),
                Id("@g2", gildId2),
                new NativeGildSqlParameter("@relation",
                    NativeGildSqlValueKind.Int32, relation),
                new NativeGildSqlParameter("@created",
                    NativeGildSqlValueKind.DateTime, createTime)
            });

        public static NativeGildSqlCommand BuildDeleteGildRelation(
            long gildId1, long gildId2) =>
            new(DeleteGildRelationSql, new[]
            {
                Id("@g1", gildId1),
                Id("@g2", gildId2)
            });

        public static NativeGildSqlCommand BuildInsertGildConcern(long gildId,
            long destinationGildId) =>
            new(InsertGildConcernSql, new[]
            {
                Id("@gild", gildId),
                Id("@dst", destinationGildId)
            });

        public static NativeGildSqlCommand BuildDeleteGildConcern(long gildId,
            long destinationGildId) =>
            new(DeleteGildConcernSql, new[]
            {
                Id("@gild", gildId),
                Id("@dst", destinationGildId)
            });

        // ---- INativeGildStore ----

        public bool TryCreateGild(long gildId, string name, long ownerCorpsId,
            long viceOwnerId, out string error) =>
            Execute(BuildCreateGild(gildId, name, ownerCorpsId, viceOwnerId),
                "Gild create", out error);

        public bool TrySaveGild(long gildId, long ownerCorpsId,
            long viceOwnerId, byte[] notice, out string error) =>
            Execute(BuildSaveGild(gildId, ownerCorpsId, viceOwnerId, notice),
                "Gild save", out error);

        public bool TryInsertGildMember(long gildId, long corpsId,
            out string error) =>
            Execute(BuildInsertGildMember(gildId, corpsId),
                "GildMember insert", out error);

        public bool TryDeleteGildMember(long gildId, long corpsId,
            out string error) =>
            Execute(BuildDeleteGildMember(gildId, corpsId),
                "GildMember delete", out error);

        public bool TryInsertGildRelation(long gildId1, long gildId2,
            int relation, DateTime createTime, out string error) =>
            Execute(BuildInsertGildRelation(gildId1, gildId2, relation,
                createTime), "GildRelation insert", out error);

        public bool TryDeleteGildRelation(long gildId1, long gildId2,
            out string error) =>
            Execute(BuildDeleteGildRelation(gildId1, gildId2),
                "GildRelation delete", out error);

        public bool TryInsertGildConcern(long gildId, long destinationGildId,
            out string error) =>
            Execute(BuildInsertGildConcern(gildId, destinationGildId),
                "gildconcern insert", out error);

        public bool TryDeleteGildConcern(long gildId, long destinationGildId,
            out string error) =>
            Execute(BuildDeleteGildConcern(gildId, destinationGildId),
                "gildconcern delete", out error);

        private bool Execute(NativeGildSqlCommand statement, string label,
            out string error)
        {
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var command = Materialize(connection, statement);
                // The original never asserts affected rows — it only checks the
                // ExecuteScript boolean and, on failure, logs "[SQL Failed] "
                // without rolling back the published in-memory change.
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                error = "native " + label + " failed: " + ex.Message;
                return false;
            }
        }

        private static MySqlCommand Materialize(MySqlConnection connection,
            NativeGildSqlCommand statement)
        {
            var command = connection.CreateCommand();
            command.CommandText = statement.CommandText;
            foreach (var parameter in statement.Parameters)
            {
                switch (parameter.Kind)
                {
                    case NativeGildSqlValueKind.Id:
                        command.Parameters.Add(parameter.Name,
                                MySqlDbType.UInt64).Value =
                            unchecked((ulong)(long)parameter.Value);
                        break;
                    case NativeGildSqlValueKind.Int32:
                        command.Parameters.Add(parameter.Name,
                            MySqlDbType.Int32).Value = (int)parameter.Value;
                        break;
                    case NativeGildSqlValueKind.GbkText:
                        command.Parameters.Add(parameter.Name,
                                MySqlDbType.VarBinary).Value =
                            StrictGbk.GetBytes(
                                (string)parameter.Value ?? string.Empty);
                        break;
                    case NativeGildSqlValueKind.Binary:
                        command.Parameters.Add(parameter.Name,
                                MySqlDbType.VarBinary).Value =
                            (byte[])parameter.Value ?? Array.Empty<byte>();
                        break;
                    case NativeGildSqlValueKind.DateTime:
                        command.Parameters.Add(parameter.Name,
                                MySqlDbType.DateTime).Value =
                            TruncateToSecond((DateTime)parameter.Value);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(statement),
                            "unknown gild SQL parameter kind " +
                            parameter.Kind);
                }
            }
            return command;
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

        // GILD-31. Native never binds a DateTime; it renders one into the SQL text
        // with whole-second precision and lets MySQL parse the string back:
        //   0x5E98D2  FF 73 1C / FF 73 18   push [ebx+0x1C] / push [ebx+0x18]
        //   0x5E98DB  B8 70 99 5E 00        mov eax,0x5E9970   ; "YYYY-MM-DD hh:nn:ss"
        //   0x5E98E0  E8 57 72 E2 FF        call 0x410B3C      ; FormatDateTime
        //   0x5E98EB  C6 45 F8 0B           mov byte [ebp-8],0xB  ; TVarRec vtAnsiString
        //   0x5E98F7  B8 8C 99 5E 00        mov eax,0x5E998C
        //     -> Insert into gamedata.gildrelation(GildID1, GildID2, Relation,
        //        CreateTime)   Values(%d, %d, %d, "%s");
        // The format string carries no fractional field, so the sub-second part is
        // dropped, not rounded. Binding a raw DateTime instead let MySQL apply its
        // own rounding into a DATETIME(0) column (round-half-up since 5.6.4), so a
        // war created at hh:mm:ss.700 persisted one second later than the original
        // would have written it -- and since the war deadline is rebuilt from this
        // column on restart (loader 0x5E8E8B calls sub_5E6D68 with the loaded
        // CreateTime), that second carried straight into the expiry time.
        private static DateTime TruncateToSecond(DateTime value) =>
            new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond,
                value.Kind);

        private static NativeGildSqlParameter Id(string name, long value) =>
            new(name, NativeGildSqlValueKind.Id, value);

        private static NativeGildSqlParameter Gbk(string name, string value) =>
            new(name, NativeGildSqlValueKind.GbkText, value ?? string.Empty);

        private static NativeGildSqlParameter Bin(string name, byte[] value) =>
            new(name, NativeGildSqlValueKind.Binary,
                value ?? Array.Empty<byte>());
    }
}
