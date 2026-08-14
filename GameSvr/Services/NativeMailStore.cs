using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr.Services
{
    internal sealed class NativeMailRecord
    {
        internal int Id { get; set; }
        internal long SenderId { get; set; }
        internal string Sender { get; set; } = string.Empty;
        internal string Title { get; set; } = string.Empty;
        internal string Context { get; set; } = string.Empty;
        internal byte MailType { get; set; }
        internal byte MailStatus { get; set; }
        internal byte AttachStatus { get; set; }
        internal int MoneyType { get; set; }
        internal int MoneyCount { get; set; }
        internal int AttachCount { get; set; }
        internal DateTime CreateDate { get; set; }
        internal List<byte[]> RawAttachments { get; } = new();
    }

    internal static class NativeMailStore
    {
        private const string MailColumns =
            "idx,sendId,sendName,title,context,mailType,mailstatus,attachstatus," +
            "moneyType,moneyCount,createDate";
        private const int LoadPageSize = 100;

        private static readonly string[] NativeSchemaStatements =
        {
            "CREATE TABLE IF NOT EXISTS gamedata.mailitem(" +
            "idx int not null AUTO_INCREMENT PRIMARY KEY," +
            "sendId bigint not null,sendName char(20) not null," +
            "recvId bigint not null,recvName char(20) binary not null," +
            "title char(100) binary not null,context char(200) binary not null," +
            "mailType tinyint(1) not null,mailstatus tinyint(1) not null," +
            "attachstatus tinyint(1) not null,moneyType tinyint(1) not null," +
            "moneyCount int not null default 0,attachNum int not null," +
            "sendtime datetime,recvtime datetime,modifyDate datetime," +
            "createDate datetime not null);",
            "CREATE TABLE IF NOT EXISTS gamedata.attachitem(" +
            "idx int not null AUTO_INCREMENT PRIMARY KEY,mailId int not null," +
            "data blob,modifydate datetime,createDate datetime not null);",
            "CREATE TABLE if not exists gamedata.mailitem_b like gamedata.mailitem;",
            "CREATE TABLE if not exists gamedata.attachitem_b like gamedata.attachitem;",
            "CREATE TABLE IF NOT EXISTS gamedata.Money_order(" +
            "idx int not null AUTO_INCREMENT PRIMARY KEY," +
            "sendId bigint not null,sendName char(20) not null," +
            "recvId bigint not null,recvName char(20) binary not null," +
            "title char(100) binary not null,context char(200) binary not null," +
            "mailType tinyint(1) not null,mailstatus tinyint(1) not null," +
            "attachstatus tinyint(1) not null,moneyType tinyint(1) not null," +
            "moneyCount int not null default 0,attachNum int not null," +
            "sendtime datetime,recvtime datetime,moneyStatus tinyint(1) not null," +
            "modifyDate datetime,createDate datetime not null);"
        };

        // sub_70DBCC @0x70DBD4 `bt dword [0x7D3DE8],edx` with dword_7D3DE8 = 7E 8D 40 00
        // and a `cmp dl,7 / ja` guard in front: tags 1..6. See NativeMailCacheService.
        internal static bool IsSupportedTag(int tag) => tag is >= 1 and <= 6;

        internal static bool EnsureNativeSchema(out string error)
        {
            error = string.Empty;
            if (!NativeStartupConfigValidation.TryEnsureGamedataSchema(out error))
            {
                NativeStartupConfigValidation.ReportMailGamedataMissing();
                return false;
            }
            if (!TryOpenConnection(out var connection, out error)) return false;
            try
            {
                using (connection)
                {
                    foreach (var statement in NativeSchemaStatements)
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText = statement;
                        command.ExecuteNonQuery();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "native mail schema initialization failed: " + ex.Message;
                return false;
            }
        }

        internal static bool TryLoadSummaries(out List<NativeMailSummary> summaries,
            out string error)
        {
            summaries = new List<NativeMailSummary>();
            error = string.Empty;
            if (!TryOpenConnection(out var connection, out error)) return false;

            try
            {
                using (connection)
                {
                    for (byte mailStatus = 1; mailStatus <= 2; mailStatus++)
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText =
                            "SELECT recvId,recvName,mailType,COUNT(idx) " +
                            "FROM gamedata.mailitem WHERE mailstatus=@mailStatus " +
                            "GROUP BY recvId,recvName,mailType";
                        command.Parameters.Add("@mailStatus", MySqlDbType.Byte).Value = mailStatus;
                        using var reader = command.ExecuteReader();
                        while (reader.Read())
                        {
                            var recipientId = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                            var tag = Convert.ToByte(reader.GetValue(2));
                            if (recipientId == 0 || !IsSupportedTag(tag)) continue;
                            summaries.Add(new NativeMailSummary(
                                recipientId, ReadGbkString(reader, 1), tag, mailStatus,
                                Convert.ToInt32(reader.GetValue(3))));
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                summaries.Clear();
                error = "native mail summary query failed: " + ex.Message;
                return false;
            }
        }

        internal static bool TryResolveRecipientId(string recipientName,
            out long recipientId, out string error)
        {
            recipientId = 0;
            error = string.Empty;
            if (!TryOpenConnection(out var connection, out error)) return false;
            try
            {
                using (connection)
                    recipientId = ResolveRecipientId(connection, recipientName);
                if (recipientId != 0) return true;
                error = "native mail recipient was not found";
                return false;
            }
            catch (Exception ex)
            {
                error = "native mail recipient lookup failed: " + ex.Message;
                return false;
            }
        }

        internal static bool TryLoadCategoryStatus(long recipientId, string recipientName,
            int tag, byte mailStatus, out List<NativeMailRecord> records, out string error)
        {
            records = new List<NativeMailRecord>();
            error = string.Empty;
            if (recipientId == 0 || !IsSupportedTag(tag)
                || mailStatus is not 1 and not 2)
            {
                error = $"unsupported native mail load: {recipientId}/{tag}/{mailStatus}";
                return false;
            }
            if (!TryOpenConnection(out var connection, out error)) return false;

            try
            {
                using (connection)
                {
                    if (!TryLoadStatus(connection, recipientId, recipientName, tag, mailStatus,
                            requireAttachment: tag == 5 && mailStatus == 2,
                            records, out error))
                    {
                        records.Clear();
                        return false;
                    }
                    LoadAttachments(connection, records);
                }
                return true;
            }
            catch (Exception ex)
            {
                records.Clear();
                error = "native mail status query failed: " + ex.Message;
                return false;
            }
        }

        internal static bool TryLoadCategory(string recipientName, int tag,
            out List<NativeMailRecord> records, out string error)
        {
            records = new List<NativeMailRecord>();
            error = string.Empty;
            if (!IsSupportedTag(tag))
            {
                error = $"unsupported native mail tag {tag}";
                return false;
            }
            if (!TryOpenConnection(out var connection, out error)) return false;

            try
            {
                using (connection)
                {
                    var recipientId = ResolveRecipientId(connection, recipientName);
                    if (!TryLoadStatus(connection, recipientId, recipientName, tag, 1,
                            requireAttachment: false, records, out error)
                        || !TryLoadStatus(connection, recipientId, recipientName, tag, 2,
                            requireAttachment: tag == 5, records, out error))
                    {
                        records.Clear();
                        return false;
                    }

                    LoadAttachments(connection, records);
                }
                return true;
            }
            catch (Exception ex)
            {
                records.Clear();
                error = "native mail list query failed: " + ex.Message;
                return false;
            }
        }

        private static bool TryLoadStatus(MySqlConnection connection, long recipientId,
            string recipientName, int tag, byte mailStatus, bool requireAttachment,
            List<NativeMailRecord> records, out string error)
        {
            error = string.Empty;
            var cursor = 0;
            while (true)
            {
                var page = new List<NativeMailRecord>(LoadPageSize);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        $"SELECT {MailColumns} FROM gamedata.mailitem " +
                        "WHERE ((@recvId<>0 AND recvId=@recvId) " +
                        "OR BINARY recvName=@recvName) AND mailstatus=@mailStatus " +
                        (requireAttachment ? "AND attachstatus=1 " : string.Empty) +
                        "AND mailType=@mailType AND idx>@cursor " +
                        $"ORDER BY idx LIMIT {LoadPageSize}";
                    command.Parameters.Add("@recvId", MySqlDbType.Int64).Value = recipientId;
                    AddRecipientParameter(command, recipientName);
                    command.Parameters.Add("@mailStatus", MySqlDbType.Byte).Value = mailStatus;
                    command.Parameters.Add("@mailType", MySqlDbType.Byte).Value = tag;
                    command.Parameters.Add("@cursor", MySqlDbType.Int32).Value = cursor;
                    using var reader = command.ExecuteReader();
                    while (reader.Read()) page.Add(ReadRecord(reader));
                }

                if (page.Count == 0) return true;
                records.AddRange(page);
                cursor = page[^1].Id;
                if (page.Count < LoadPageSize) return true;
            }
        }

        internal static bool TryReadUnreadCounts(string recipientName, out int[] counts,
            out string error)
        {
            counts = new int[6];
            error = string.Empty;
            if (!TryOpenConnection(out var connection, out error)) return false;
            try
            {
                using (connection)
                using (var command = connection.CreateCommand())
                {
                    var recipientId = ResolveRecipientId(connection, recipientName);
                    command.CommandText =
                        "SELECT mailType,COUNT(idx) FROM gamedata.mailitem " +
                        "WHERE ((@recvId<>0 AND recvId=@recvId) " +
                        "OR BINARY recvName=@recvName) AND mailstatus=1 GROUP BY mailType";
                    command.Parameters.Add("@recvId", MySqlDbType.Int64).Value = recipientId;
                    AddRecipientParameter(command, recipientName);
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var tag = Convert.ToInt32(reader.GetValue(0));
                        if (tag is >= 1 and <= 6)
                            counts[tag - 1] = Convert.ToInt32(reader.GetValue(1));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Array.Clear(counts, 0, counts.Length);
                error = "native unread mail query failed: " + ex.Message;
                return false;
            }
        }

        internal static void MarkReadBestEffort(IEnumerable<int> mailIds)
        {
            if (!TryOpenConnection(out var connection, out _)) return;
            using (connection)
            {
                foreach (var mailId in mailIds)
                {
                    try
                    {
                        using var update = connection.CreateCommand();
                        update.CommandText =
                            "UPDATE gamedata.mailitem SET mailstatus=2,modifydate=Now() " +
                            "WHERE idx=@mailId";
                        update.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                        update.ExecuteNonQuery();
                    }
                    catch
                    {
                        // The native M2 updates its in-memory state even when this SQL write fails.
                    }
                }
            }
        }

        internal static int CreateMoneyOrderBestEffort(NativeMailRecord record,
            string recipientName)
        {
            if (record == null || !TryOpenConnection(out var connection, out _)) return -1;
            try
            {
                using (connection)
                {
                    using (var insert = connection.CreateCommand())
                    {
                        var recipientId = ResolveRecipientId(connection, recipientName);
                        insert.CommandText =
                            "INSERT INTO gamedata.Money_order(" +
                            "sendId,sendName,recvName,recvId,title,context,mailType,mailstatus," +
                            "attachstatus,moneyType,moneyCount,attachNum,moneyStatus,createDate) VALUES(" +
                            "@sendId,@sendName,@recvName,@recvId,@title,@context,@mailType,@mailStatus," +
                            "@attachStatus,@moneyType,@moneyCount,@attachNum,0,Now())";
                        insert.Parameters.Add("@sendId", MySqlDbType.Int64).Value = record.SenderId;
                        AddGbkParameter(insert, "@sendName", record.Sender);
                        AddGbkParameter(insert, "@recvName", recipientName);
                        insert.Parameters.Add("@recvId", MySqlDbType.Int64).Value = recipientId;
                        AddGbkParameter(insert, "@title", record.Title);
                        AddGbkParameter(insert, "@context", record.Context);
                        insert.Parameters.Add("@mailType", MySqlDbType.Byte).Value = record.MailType;
                        insert.Parameters.Add("@mailStatus", MySqlDbType.Byte).Value = record.MailStatus;
                        insert.Parameters.Add("@attachStatus", MySqlDbType.Byte).Value = record.AttachStatus;
                        insert.Parameters.Add("@moneyType", MySqlDbType.Int32).Value = record.MoneyType;
                        insert.Parameters.Add("@moneyCount", MySqlDbType.Int32).Value = record.MoneyCount;
                        insert.Parameters.Add("@attachNum", MySqlDbType.Int32).Value = record.AttachCount;
                        if (insert.ExecuteNonQuery() != 1) return -1;
                    }

                    using var queryId = connection.CreateCommand();
                    queryId.CommandText = "SELECT LAST_INSERT_ID()";
                    var value = queryId.ExecuteScalar();
                    return value == null || value == DBNull.Value ? -1 : Convert.ToInt32(value);
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[NativeMail] AddOrder failed: " + ex.Message);
                return -1;
            }
        }

        // Stall BUY settlement mail (§5b of stall_buy_executor_20260801.md): the seller's proceeds are paid
        // as an in-game MAIL of exactly count*uprice gold (NO fee) — MailType 4, MONEY ONLY (the FLAGGED item
        // copy is omitted: the buyer already holds the item, so attaching it would DUPE). A single direct
        // INSERT of the FINAL values (unlike the native two-phase ctor-then-mutate), so moneyCount is written
        // correctly the first time. Returns true only when the row is inserted (the executor treats the
        // seller credit as a hard precondition — a false here aborts the buy, never charging the buyer
        // without crediting the seller).
        //
        // VERIFIED values (the task's task-1 diligence — against the LIVE C# claim path + the native binary):
        //  * mailType   = 4  — settlement/trade. FetchNativeMailAttachments/DeliverNativeMailAttachments treat
        //                      MailType 4 as "money, no item delivery": the seller's claim credits the gold
        //                      IncGold(MoneyCount) and marks attachstatus=2. (native claim core sub_70B664:
        //                      "mail type 4 sets attachstatus=2 without delivery" then credits money.)
        //  * mailstatus = 1  (UNREAD).  NOT 0.  The live loader ONLY surfaces mailstatus IN (1,2)
        //                      (TryLoadSummaries/TryLoadStatus loop 1..2; TryReadUnreadCounts WHERE mailstatus=1;
        //                      MarkReadBestEffort sets 2). A mailstatus=0 row would be INVISIBLE — the seller
        //                      would never see it and never claim. 1 = unread is the correct "new mail" value.
        //  * attachstatus = 1 (UNCLAIMED CONTENT).  The claim path credits gold for ANY attachStatus != 2
        //                      (FetchNativeMailAttachments: `if (record.AttachStatus == 2) return -2;` then, for
        //                      MoneyType 0 + MoneyCount>0, IncGold(MoneyCount) via VMT+0x28C). Native flags a mail that
        //                      carries CLAIMABLE content with attachstatus=1 (BuildEnvelope sub_70A9EC defaults
        //                      +0x4D=3; AppendAttachment sub_70A954 bumps it to 1 via sub_70CB24 —
        //                      `UPDATE mailitem SET attachstatus=1` — the native stall settlement mail, which
        //                      attaches the sold item, therefore ends at attachstatus=1). Choosing 1 (not the
        //                      raw ctor default 3) is BOTH claim-crediting AND conservation-safe: clear-all
        //                      (IsClearAllEligible: mailStatus==2 && attachStatus∈{2,3}) can NOT purge an
        //                      attachstatus=1 mail, so a read-but-unclaimed settlement mail is protected from
        //                      accidental deletion until the gold is collected. On claim it flips 1 -> 2.
        //  * moneyType  = 0  (gold — the type-0 sale).   moneyCount = count*uprice.   attachNum = 0 (no item).
        internal static bool TryInsertSettlementMail(long sendId, string sendName, long recvId,
            string recvName, string title, string context, int moneyType, int moneyCount, out string error)
        {
            error = string.Empty;
            if (moneyCount <= 0)
            {
                error = "settlement mail requires a positive moneyCount";
                return false;
            }
            if (!TryOpenConnection(out var connection, out error)) return false;
            try
            {
                using (connection)
                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText =
                        "INSERT INTO gamedata.mailitem(" +
                        "sendId,sendName,recvId,recvName,title,context,mailType,mailstatus," +
                        "attachstatus,moneyType,moneyCount,attachNum,createDate) VALUES(" +
                        "@sendId,@sendName,@recvId,@recvName,@title,@context,@mailType,@mailStatus," +
                        "@attachStatus,@moneyType,@moneyCount,@attachNum,Now())";
                    insert.Parameters.Add("@sendId", MySqlDbType.Int64).Value = sendId;
                    AddGbkParameter(insert, "@sendName", sendName);
                    insert.Parameters.Add("@recvId", MySqlDbType.Int64).Value = recvId;
                    AddGbkParameter(insert, "@recvName", recvName);
                    AddGbkParameter(insert, "@title", title);
                    AddGbkParameter(insert, "@context", context);
                    insert.Parameters.Add("@mailType", MySqlDbType.Byte).Value = (byte)4;      // settlement
                    insert.Parameters.Add("@mailStatus", MySqlDbType.Byte).Value = (byte)1;    // UNREAD (loadable)
                    insert.Parameters.Add("@attachStatus", MySqlDbType.Byte).Value = (byte)1;  // UNCLAIMED, clear-all-safe
                    insert.Parameters.Add("@moneyType", MySqlDbType.Byte).Value = (byte)moneyType;
                    insert.Parameters.Add("@moneyCount", MySqlDbType.Int32).Value = moneyCount;
                    insert.Parameters.Add("@attachNum", MySqlDbType.Int32).Value = 0;          // money only
                    return insert.ExecuteNonQuery() == 1;
                }
            }
            catch (Exception ex)
            {
                error = "native settlement mail insert failed: " + ex.Message;
                M2Share.MainOutMessage("[NativeMail] settlement insert failed: " + ex.Message);
                return false;
            }
        }

        internal static void SetMoneyOrderStatusBestEffort(int orderId, byte status)
        {
            if (!TryOpenConnection(out var connection, out _)) return;
            try
            {
                using (connection)
                using (var update = connection.CreateCommand())
                {
                    update.CommandText =
                        "UPDATE gamedata.Money_order SET moneyStatus=@status WHERE idx=@orderId";
                    update.Parameters.Add("@status", MySqlDbType.Byte).Value = status;
                    update.Parameters.Add("@orderId", MySqlDbType.Int32).Value = orderId;
                    update.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[NativeMail] SetOrderStatus failed: " + ex.Message);
            }
        }

        internal static void SetAttachStatusBestEffort(int mailId, byte status)
        {
            if (!TryOpenConnection(out var connection, out _)) return;
            try
            {
                using (connection)
                using (var update = connection.CreateCommand())
                {
                    update.CommandText =
                        "UPDATE gamedata.mailitem SET attachstatus=@status,modifydate=Now() " +
                        "WHERE idx=@mailId";
                    update.Parameters.Add("@status", MySqlDbType.Byte).Value = status;
                    update.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                    update.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[NativeMail] SetAttachStatus failed: " + ex.Message);
            }
        }

        internal static void ArchiveAndDeleteBestEffort(int mailId)
        {
            if (!TryOpenConnection(out var connection, out _)) return;
            try
            {
                using (connection)
                {
                    int archivedMailId;
                    using (var archiveMail = connection.CreateCommand())
                    {
                        archiveMail.CommandText =
                            "INSERT INTO gamedata.mailitem_b(" +
                            "sendId,sendName,recvId,recvName,title,context,mailType,mailstatus," +
                            "attachstatus,moneyType,moneyCount,attachNum,sendtime,recvtime," +
                            "createDate,modifyDate) " +
                            "SELECT sendId,sendName,recvId,recvName,title,context,mailType," +
                            "mailstatus,attachstatus,moneyType,moneyCount,attachNum,sendtime," +
                            "recvtime,createDate,Now() FROM gamedata.mailitem WHERE idx=@mailId";
                        archiveMail.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                        if (archiveMail.ExecuteNonQuery() != 1) return;
                    }

                    using (var queryId = connection.CreateCommand())
                    {
                        queryId.CommandText =
                            "SELECT idx FROM gamedata.mailitem_b WHERE idx=LAST_INSERT_ID()";
                        var value = queryId.ExecuteScalar();
                        if (value == null || value == DBNull.Value) return;
                        archivedMailId = Convert.ToInt32(value);
                    }

                    using (var deleteMail = connection.CreateCommand())
                    {
                        deleteMail.CommandText =
                            "DELETE FROM gamedata.mailitem WHERE idx=@mailId";
                        deleteMail.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                        if (deleteMail.ExecuteNonQuery() != 1) return;
                    }

                    var attachmentIds = new List<int>();
                    using (var queryAttachments = connection.CreateCommand())
                    {
                        queryAttachments.CommandText =
                            "SELECT idx FROM gamedata.attachitem WHERE mailId=@mailId ORDER BY idx";
                        queryAttachments.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                        using var reader = queryAttachments.ExecuteReader();
                        while (reader.Read()) attachmentIds.Add(reader.GetInt32(0));
                    }

                    foreach (var attachmentId in attachmentIds)
                    {
                        using var archiveAttachment = connection.CreateCommand();
                        archiveAttachment.CommandText =
                            "INSERT INTO gamedata.attachitem_b(mailId,data,createDate,modifydate) " +
                            "SELECT @archivedMailId,data,createDate,Now() " +
                            "FROM gamedata.attachitem WHERE idx=@attachmentId";
                        archiveAttachment.Parameters.Add("@archivedMailId", MySqlDbType.Int32)
                            .Value = archivedMailId;
                        archiveAttachment.Parameters.Add("@attachmentId", MySqlDbType.Int32)
                            .Value = attachmentId;
                        if (archiveAttachment.ExecuteNonQuery() != 1) break;
                    }

                    using var deleteAttachments = connection.CreateCommand();
                    deleteAttachments.CommandText =
                        "DELETE FROM gamedata.attachitem WHERE mailId=@mailId";
                    deleteAttachments.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                    deleteAttachments.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                // Native deletion also removes the runtime object when archival SQL fails.
                M2Share.MainOutMessage("[NativeMail] archive/delete failed: " + ex.Message);
            }
        }

        internal static void DeleteRowsBestEffort(int mailId)
        {
            if (!TryOpenConnection(out var connection, out _)) return;
            try
            {
                using (connection)
                {
                    using (var deleteMail = connection.CreateCommand())
                    {
                        deleteMail.CommandText =
                            "DELETE FROM gamedata.mailitem WHERE Idx=@mailId";
                        deleteMail.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                        if (deleteMail.ExecuteNonQuery() != 1) return;
                    }

                    using var deleteAttachments = connection.CreateCommand();
                    deleteAttachments.CommandText =
                        "DELETE FROM gamedata.attachitem WHERE mailid=@mailId";
                    deleteAttachments.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                    deleteAttachments.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[NativeMail] retention delete failed: " + ex.Message);
            }
        }

        private static void LoadAttachments(MySqlConnection connection,
            IEnumerable<NativeMailRecord> records)
        {
            foreach (var record in records)
            {
                if (record.AttachStatus != 1) continue;
                if (!TryReadAttachments(connection, record.Id,
                        record.RawAttachments, out var attachmentError))
                {
                    M2Share.MainOutMessage(
                        $"[NativeMail] attachment load failed for {record.Id}: {attachmentError}");
                    record.RawAttachments.Clear();
                }
                record.AttachCount = record.RawAttachments.Count;
            }
        }

        private static bool TryReadAttachments(MySqlConnection connection, int mailId,
            List<byte[]> attachments, out string error)
        {
            error = string.Empty;
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT data FROM gamedata.attachitem WHERE mailId=@mailId ORDER BY idx";
                command.Parameters.Add("@mailId", MySqlDbType.Int32).Value = mailId;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var data = reader.IsDBNull(0)
                        ? Array.Empty<byte>()
                        : reader.GetValue(0) as byte[] ?? Array.Empty<byte>();
                    attachments.Add(NativeMailAttachmentCodec.NormalizeRecord(data));
                }
                return true;
            }
            catch (Exception ex)
            {
                attachments.Clear();
                error = ex.Message;
                return false;
            }
        }

        private static NativeMailRecord ReadRecord(MySqlDataReader reader)
        {
            return new NativeMailRecord
            {
                Id = reader.GetInt32(0),
                SenderId = reader.GetInt64(1),
                Sender = ReadGbkString(reader, 2),
                Title = ReadGbkString(reader, 3),
                Context = ReadGbkString(reader, 4),
                MailType = Convert.ToByte(reader.GetValue(5)),
                MailStatus = Convert.ToByte(reader.GetValue(6)),
                AttachStatus = Convert.ToByte(reader.GetValue(7)),
                MoneyType = Convert.ToInt32(reader.GetValue(8)),
                MoneyCount = reader.GetInt32(9),
                AttachCount = 0,
                CreateDate = reader.GetDateTime(10)
            };
        }

        private static bool TryOpenConnection(out MySqlConnection connection, out string error)
        {
            connection = null;
            error = string.Empty;
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                error = "native mail database connection is not configured";
                return false;
            }
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
                error = "native mail database connection failed: " + ex.Message;
                return false;
            }
        }

        private static void AddRecipientParameter(MySqlCommand command, string recipientName)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(recipientName ?? string.Empty);
            command.Parameters.Add("@recvName", MySqlDbType.VarBinary,
                Math.Max(1, bytes.Length)).Value = bytes;
        }

        private static void AddGbkParameter(MySqlCommand command, string name, string value)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            command.Parameters.Add(name, MySqlDbType.VarBinary,
                Math.Max(1, bytes.Length)).Value = bytes;
        }

        internal static long ResolveRecipientId(MySqlConnection connection, string recipientName)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT UserId FROM mir3.user_index " +
                    "WHERE BINARY ChrName=@recvName LIMIT 1";
                AddRecipientParameter(command, recipientName);
                var value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value);
            }
            catch
            {
                return 0;
            }
        }

        private static string ReadGbkString(MySqlDataReader reader, int ordinal)
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
                bytes = System.Text.Encoding.Latin1.GetBytes(Convert.ToString(value) ?? string.Empty);
            }
            var length = bytes.Length;
            while (length > 0 && bytes[length - 1] == 0) length--;
            return HUtil32.GbkEncoding.GetString(bytes, 0, length).TrimEnd();
        }
    }
}
