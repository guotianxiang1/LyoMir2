using MySql.Data.MySqlClient;
using System.Text;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Write side of the original M2Server player-STALL (摆摊 / personal booth)
    /// persistence. Like the guild/corps social writes, the stall tables live in
    /// the <c>gamedata</c> schema and are written IN-PROCESS via direct MySQL —
    /// <c>TMySQLDB.ExecuteScript</c> (<c>sub_724E48</c>) on the embedded client
    /// <c>off_7D5C40</c> — NOT through a DBServer socket. Reversed from the
    /// exclusive idat pass (staging/update_clothes_4637_ida_work/stall_exec_out.txt)
    /// + all_strings.txt; each SQL const cites its .rdata source address (image
    /// base 0x400000) and the writer sub it belongs to.
    ///
    /// This store mirrors <see cref="NativeGildMySqlStore"/> exactly (OpenConnection,
    /// StrictGbk, parameterization) and is deliberately fail-safe: the original only
    /// checks the ExecuteScript boolean and, on failure, logs "[SQL Failed]" with NO
    /// rollback of the already-published in-memory change — so a SQL error here is a
    /// false return + message, and no affected-row count is asserted.
    ///
    /// DORMANT: not injected by default. <see cref="NativeStallWriteGate"/> gates
    /// live use (store null / gate off => callers keep the existing
    /// RejectUnavailableStallRequest fallback, so DormantBoundary guards stay green).
    /// The 208-byte item record (srvData BLOB) is written by the original via an
    /// updatable result-set stream after LAST_INSERT_ID(), not a SQL template, so it
    /// is surfaced as <see cref="WriteItemSrvData"/> (documented, no golden template).
    /// </summary>
    public interface INativeStallStore
    {
        // ---- stall (booth header) ----
        bool TryInsertStall(NativeStallHeaderRow row, out int newIdx, out string error);
        bool TryUpdateStall(NativeStallHeaderRow row, long ownerId, int idx, out string error);
        bool TryExpireStalls(int isEnabled, int status, out string error);
        // Startup hydration: load every live booth (isEnabled=1) + its listed items into detached
        // records, so NativeStallManager can be primed before the write-gate flips. Read-only.
        bool TryLoadActiveStalls(out List<NativeStallRecord> records, out string error);

        // ---- stallitem ----
        bool TryInsertStallItem(int stallId, long ownerId, int uprice, int moneyType,
            int isSold, int isGetMoney, DateTime createDate, int itemCount, string ownerName,
            out int newIdx, out string error);
        // 208-byte item record (srvData BLOB) streamed after LAST_INSERT_ID() (see class remarks);
        // targets the row TryInsertStallItem just created.
        bool WriteItemSrvData(int idx, byte[] srvData208, out string error);
        bool TryUpdateStallItem(int uprice, int moneyType, int itemCount, int isSold,
            int isGetMoney, DateTime modifyDate, int stallId, int idx, out string error);
        bool TryDeleteStallItem(int idx, out string error);
        bool TryMarkItemBoSended(int idx, long ownerId, out string error);
        bool TryBackupStallItem(int idx, out string error);

        // ---- stallmsglst (booth 留言) ----
        bool TryInsertStallMsg(int stallId, long ownerId, string ownerName, int cnt,
            DateTime createDate, out string error);
        bool TryUpdateStallMsg(int cnt, int idx, int stallId, out string error);
        bool TryDeleteStallMsg(int stallId, out string error);
        bool TryExpireDeleteStallMsg(int isEnabled, int status, out string error);

        // ---- buyer_order / buyitem_detail (purchase ledger + recovery) ----
        bool TryInsertBuyerOrder(long buyerId, string buyerName, long sellId, string sellName,
            int uprice, int moneyType, int count, int totalPrice, int boDecMoney, int status, out string error);
        bool TryUpdateBuyerOrder(int boDecMoney, int status, int idx, out string error);
        bool TryInsertBuyItemDetail(long buyerId, string buyerName, long sellId, string sellName,
            int uprice, int moneyType, int count, out string error);
    }

    /// <summary>Column bag for the stall (booth header) INSERT/UPDATE.</summary>
    public sealed class NativeStallHeaderRow
    {
        public long OwnerId { get; init; }
        public string OwnerName { get; init; } = string.Empty;
        public string StallName { get; init; } = string.Empty;
        public int ItemCnt { get; init; }
        public int Level { get; init; }
        public int DuraTime { get; init; }
        public int IsEnabled { get; init; }
        public DateTime CreateDate { get; init; }
        public DateTime ModifyDate { get; init; }
        public int PosX { get; init; }
        public int PosY { get; init; }
        public string MapName { get; init; } = string.Empty;
        public int Status { get; init; }
    }

    /// <summary>How a <see cref="NativeStallSqlParameter"/> value is bound (mirrors the gild store kinds).</summary>
    public enum NativeStallSqlValueKind
    {
        /// <summary>bigint(20) id — MySqlDbType.UInt64.</summary>
        Id,
        /// <summary>int column — MySqlDbType.Int32.</summary>
        Int32,
        /// <summary>char(...) binary text carried as raw GBK bytes.</summary>
        GbkText,
        /// <summary>datetime — MySqlDbType.DateTime.</summary>
        DateTime
    }

    public readonly struct NativeStallSqlParameter
    {
        public NativeStallSqlParameter(string name, NativeStallSqlValueKind kind, object value)
        {
            Name = name;
            Kind = kind;
            Value = value;
        }

        public string Name { get; }
        public NativeStallSqlValueKind Kind { get; }
        public object Value { get; }
    }

    /// <summary>Pure description of one parameterized statement (CommandText + ordered params).</summary>
    public sealed class NativeStallSqlCommand
    {
        public NativeStallSqlCommand(string commandText, IReadOnlyList<NativeStallSqlParameter> parameters)
        {
            CommandText = commandText;
            Parameters = parameters;
        }

        public string CommandText { get; }
        public IReadOnlyList<NativeStallSqlParameter> Parameters { get; }
    }

    /// <summary>
    /// Dormant injection point for the stall store. Defaults OFF / null so the live
    /// stall message handlers keep their RejectUnavailableStallRequest fallback (guards
    /// green). A future full-stack cutover sets <see cref="Store"/> and enables
    /// <see cref="SupportsStallWrites"/>. <see cref="Enabled"/> is the single check a
    /// router uses: only route to the store when a store is present AND the flag is on.
    /// </summary>
    public static class NativeStallWriteGate
    {
        public static INativeStallStore Store { get; set; }
        public static bool SupportsStallWrites { get; set; }
        public static bool Enabled => SupportsStallWrites && Store != null;
    }

    public sealed class NativeStallMySqlStore : INativeStallStore
    {
        // ---- Reversed SQL, parameterized. Each const is the 1:1 translation of the
        // ---- original sprintf template; the "%s" schema becomes the literal
        // ---- "gamedata", %d/%u/%s/%S placeholders become @named params, and the
        // ---- '%s'/"%s" quoting is dropped (value travels as a bound param).

        // sub_62016C @0x0061FBFC : INSERT INTO %s.stall(ownerID, ownername,stallname,itemcnt,level,
        //   DuraTime,isEnabled,createdate,posx,posy,mapname,status) VALUES (%d,%s,%s,%d,%d,%d,%d,%s,%d,%d,%s,%d);
        public const string InsertStallSql =
            "INSERT INTO gamedata.stall(ownerID,ownername,stallname,itemcnt,level,DuraTime,isEnabled," +
            "createdate,posx,posy,mapname,status) VALUES(@owner,@ownername,@stallname,@itemcnt,@level," +
            "@duratime,@isenabled,@createdate,@posx,@posy,@mapname,@status)";

        // @0x0062009C : UPDATE %s.stall SET stallname = %s, level = %d, itemcnt = %d, modifyDate = %s,
        //   isEnabled = %d, posx = %d, posy = %d, mapname = %s, status = %d,createdate = %s,DuraTime = %d
        //   WHERE ownerid = %d and idx = %d;
        public const string UpdateStallSql =
            "UPDATE gamedata.stall SET stallname=@stallname,level=@level,itemcnt=@itemcnt," +
            "modifyDate=@modifydate,isEnabled=@isenabled,posx=@posx,posy=@posy,mapname=@mapname," +
            "status=@status,createdate=@createdate,DuraTime=@duratime WHERE ownerid=@owner AND idx=@idx";

        // sub_61C068 @0x0061C110 : UPDATE %s.stall set isEnabled = %d, status = %d WHERE
        //   TIME_TO_SEC(TIMEDIFF(NOW(),createdate)) >= duratime * 60 * 60 and
        //   TIME_TO_SEC(TIMEDIFF(NOW(),createdate)) > 0;   (booth TTL expiry)
        public const string ExpireStallsSql =
            "UPDATE gamedata.stall SET isEnabled=@isenabled,status=@status WHERE " +
            "TIME_TO_SEC(TIMEDIFF(NOW(),createdate)) >= duratime * 60 * 60 AND " +
            "TIME_TO_SEC(TIMEDIFF(NOW(),createdate)) > 0";

        // sub_62016C @0x00620420 : INSERT INTO %s.stallitem(stallid,ownerid,uprice,moneytype,isSold,
        //   isGetMoney,createdate,itemcount,ownername) VALUES(%d,%d,%d,%d,%d,%d,%s,%d,%S);
        //   (srvData BLOB is written separately, see WriteItemSrvData.)
        public const string InsertStallItemSql =
            "INSERT INTO gamedata.stallitem(stallid,ownerid,uprice,moneytype,isSold,isGetMoney," +
            "createdate,itemcount,ownername) VALUES(@stallid,@owner,@uprice,@moneytype,@issold," +
            "@isgetmoney,@createdate,@itemcount,@ownername)";

        // sub_62054C @0x006207DC : UPDATE %s.stallitem set uprice =%d, moneytype=%d,itemcount = %d,
        //   isSold=%d, isGetMoney = %d, modifydate = %s, stallid = %d where Idx =%d;
        public const string UpdateStallItemSql =
            "UPDATE gamedata.stallitem SET uprice=@uprice,moneytype=@moneytype,itemcount=@itemcount," +
            "isSold=@issold,isGetMoney=@isgetmoney,modifydate=@modifydate,stallid=@stallid WHERE Idx=@idx";

        // sub_6208B8 @0x00620970 : DELETE FROM %s.stallitem where idx = %d ;
        public const string DeleteStallItemSql =
            "DELETE FROM gamedata.stallitem WHERE idx=@idx";

        // sub_61CCA4 @0x0061CD44 : UPDATE %s.stallitem SET IsBoSended = 1 WHERE idx = %d and ownerid = %d;
        public const string MarkItemBoSendedSql =
            "UPDATE gamedata.stallitem SET IsBoSended=1 WHERE idx=@idx AND ownerid=@owner";

        // sub_6213EC @0x00621490 : INSERT INTO %s.stallitem_b(...)SELECT ... from %s.stallitem where idx = %d;
        public const string BackupStallItemSql =
            "INSERT INTO gamedata.stallitem_b(stallid,ownerId,ownername,uprice,moneytype,itemcount," +
            "srvdata,modifydate,createdate,isGetmoney,isSold) SELECT stallid,ownerid,ownername,uprice," +
            "moneytype,itemcount,srvdata,modifydate,Now(),isGetmoney,isSold FROM gamedata.stallitem WHERE idx=@idx";

        // @0x0061F740 : INSERT INTO %s.stallmsglst(stallid, ownerid,ownername,cnt,createdate) VALUES(%d,%d,%s,%d,%s);
        public const string InsertStallMsgSql =
            "INSERT INTO gamedata.stallmsglst(stallid,ownerid,ownername,cnt,createdate) " +
            "VALUES(@stallid,@owner,@ownername,@cnt,@createdate)";

        // @0x0061F8A8 : UPDATE %s.stallmsglst SET cnt = %d, updatetime = Now() WHERE idx = %d and stallid = %d;
        public const string UpdateStallMsgSql =
            "UPDATE gamedata.stallmsglst SET cnt=@cnt,updatetime=Now() WHERE idx=@idx AND stallid=@stallid";

        // @0x0061F9A8 : DELETE FROM %s.stallmsglst WHERE stallid = %d;
        public const string DeleteStallMsgSql =
            "DELETE FROM gamedata.stallmsglst WHERE stallid=@stallid";

        // sub_61C1B8 @0x0061C26C : DELETE FROM %s.stallmsglst WHERE stallid in (SELECT idx FROM %s.stall
        //   WHERE isEnabled = %d and status = %d);   (expiry companion of ExpireStalls)
        public const string ExpireDeleteStallMsgSql =
            "DELETE FROM gamedata.stallmsglst WHERE stallid IN (SELECT idx FROM gamedata.stall " +
            "WHERE isEnabled=@isenabled AND status=@status)";

        // sub_620B2C @0x00620DCC : INSERT INTO %s.buyer_order (buyerid, buyername,sellid,sellname,uprice,
        //   moneytype,count,totalprice, boDecMoney,status,createdate) VALUES (%d,%s,%d,%s,%d,%d,%d,%d,%d,%d,Now());
        public const string InsertBuyerOrderSql =
            "INSERT INTO gamedata.buyer_order(buyerid,buyername,sellid,sellname,uprice,moneytype,count," +
            "totalprice,boDecMoney,status,createdate) VALUES(@buyer,@buyername,@sell,@sellname,@uprice," +
            "@moneytype,@count,@totalprice,@bodecmoney,@status,Now())";

        // @0x00620AE4 : UPDATE %.s.buyer_order set boDecMoney = %d, status = %d WHERE idx = %d;
        //   (NOTE: the original has a "%.s" schema-format TYPO; the intent is gamedata.buyer_order.)
        public const string UpdateBuyerOrderSql =
            "UPDATE gamedata.buyer_order SET boDecMoney=@bodecmoney,status=@status WHERE idx=@idx";

        // sub_6210B0 @0x00621300 : INSERT INTO %s.buyitem_detail(buyerid, buyername,sellId,sellname,
        //   uprice, moneytype,count,createdate) VALUES (%d,%s,%d,%s,%d,%d,%d,Now());
        public const string InsertBuyItemDetailSql =
            "INSERT INTO gamedata.buyitem_detail(buyerid,buyername,sellId,sellname,uprice,moneytype,count," +
            "createdate) VALUES(@buyer,@buyername,@sell,@sellname,@uprice,@moneytype,@count,Now())";

        private readonly Func<string> _connectionString;
        private static readonly Encoding StrictGbk = Encoding.GetEncoding(
            HUtil32.GbkEncoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        public NativeStallMySqlStore(Func<string> connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        // ---- Pure command builders (no connection). The compat check drives these and asserts CommandText + params.

        public static NativeStallSqlCommand BuildInsertStall(NativeStallHeaderRow r) =>
            new(InsertStallSql, new[]
            {
                Id("@owner", r.OwnerId), Gbk("@ownername", r.OwnerName), Gbk("@stallname", r.StallName),
                I32("@itemcnt", r.ItemCnt), I32("@level", r.Level), I32("@duratime", r.DuraTime),
                I32("@isenabled", r.IsEnabled), Dt("@createdate", r.CreateDate), I32("@posx", r.PosX),
                I32("@posy", r.PosY), Gbk("@mapname", r.MapName), I32("@status", r.Status)
            });

        public static NativeStallSqlCommand BuildUpdateStall(NativeStallHeaderRow r, long ownerId, int idx) =>
            new(UpdateStallSql, new[]
            {
                Gbk("@stallname", r.StallName), I32("@level", r.Level), I32("@itemcnt", r.ItemCnt),
                Dt("@modifydate", r.ModifyDate), I32("@isenabled", r.IsEnabled), I32("@posx", r.PosX),
                I32("@posy", r.PosY), Gbk("@mapname", r.MapName), I32("@status", r.Status),
                Dt("@createdate", r.CreateDate), I32("@duratime", r.DuraTime), Id("@owner", ownerId), I32("@idx", idx)
            });

        public static NativeStallSqlCommand BuildExpireStalls(int isEnabled, int status) =>
            new(ExpireStallsSql, new[] { I32("@isenabled", isEnabled), I32("@status", status) });

        public static NativeStallSqlCommand BuildInsertStallItem(int stallId, long ownerId, int uprice,
            int moneyType, int isSold, int isGetMoney, DateTime createDate, int itemCount, string ownerName) =>
            new(InsertStallItemSql, new[]
            {
                I32("@stallid", stallId), Id("@owner", ownerId), I32("@uprice", uprice),
                I32("@moneytype", moneyType), I32("@issold", isSold), I32("@isgetmoney", isGetMoney),
                Dt("@createdate", createDate), I32("@itemcount", itemCount), Gbk("@ownername", ownerName)
            });

        public static NativeStallSqlCommand BuildUpdateStallItem(int uprice, int moneyType, int itemCount,
            int isSold, int isGetMoney, DateTime modifyDate, int stallId, int idx) =>
            new(UpdateStallItemSql, new[]
            {
                I32("@uprice", uprice), I32("@moneytype", moneyType), I32("@itemcount", itemCount),
                I32("@issold", isSold), I32("@isgetmoney", isGetMoney), Dt("@modifydate", modifyDate),
                I32("@stallid", stallId), I32("@idx", idx)
            });

        public static NativeStallSqlCommand BuildDeleteStallItem(int idx) =>
            new(DeleteStallItemSql, new[] { I32("@idx", idx) });

        public static NativeStallSqlCommand BuildMarkItemBoSended(int idx, long ownerId) =>
            new(MarkItemBoSendedSql, new[] { I32("@idx", idx), Id("@owner", ownerId) });

        public static NativeStallSqlCommand BuildBackupStallItem(int idx) =>
            new(BackupStallItemSql, new[] { I32("@idx", idx) });

        public static NativeStallSqlCommand BuildInsertStallMsg(int stallId, long ownerId, string ownerName,
            int cnt, DateTime createDate) =>
            new(InsertStallMsgSql, new[]
            {
                I32("@stallid", stallId), Id("@owner", ownerId), Gbk("@ownername", ownerName),
                I32("@cnt", cnt), Dt("@createdate", createDate)
            });

        public static NativeStallSqlCommand BuildUpdateStallMsg(int cnt, int idx, int stallId) =>
            new(UpdateStallMsgSql, new[] { I32("@cnt", cnt), I32("@idx", idx), I32("@stallid", stallId) });

        public static NativeStallSqlCommand BuildDeleteStallMsg(int stallId) =>
            new(DeleteStallMsgSql, new[] { I32("@stallid", stallId) });

        public static NativeStallSqlCommand BuildExpireDeleteStallMsg(int isEnabled, int status) =>
            new(ExpireDeleteStallMsgSql, new[] { I32("@isenabled", isEnabled), I32("@status", status) });

        public static NativeStallSqlCommand BuildInsertBuyerOrder(long buyerId, string buyerName, long sellId,
            string sellName, int uprice, int moneyType, int count, int totalPrice, int boDecMoney, int status) =>
            new(InsertBuyerOrderSql, new[]
            {
                Id("@buyer", buyerId), Gbk("@buyername", buyerName), Id("@sell", sellId),
                Gbk("@sellname", sellName), I32("@uprice", uprice), I32("@moneytype", moneyType),
                I32("@count", count), I32("@totalprice", totalPrice), I32("@bodecmoney", boDecMoney),
                I32("@status", status)
            });

        public static NativeStallSqlCommand BuildUpdateBuyerOrder(int boDecMoney, int status, int idx) =>
            new(UpdateBuyerOrderSql, new[] { I32("@bodecmoney", boDecMoney), I32("@status", status), I32("@idx", idx) });

        public static NativeStallSqlCommand BuildInsertBuyItemDetail(long buyerId, string buyerName, long sellId,
            string sellName, int uprice, int moneyType, int count) =>
            new(InsertBuyItemDetailSql, new[]
            {
                Id("@buyer", buyerId), Gbk("@buyername", buyerName), Id("@sell", sellId),
                Gbk("@sellname", sellName), I32("@uprice", uprice), I32("@moneytype", moneyType), I32("@count", count)
            });

        // ---- INativeStallStore ----

        public bool TryInsertStall(NativeStallHeaderRow row, out int newIdx, out string error)
        {
            // The native re-selects LAST_INSERT_ID() after the stall INSERT to fill rec+0x18 (the DB idx used
            // by later UpdateStall). Capture it here so the wrapper can gate INSERT(first)->UPDATE(subsequent).
            newIdx = 0;
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var command = Materialize(connection, BuildInsertStall(row));
                command.ExecuteNonQuery();
                newIdx = (int)command.LastInsertedId;
                return true;
            }
            catch (Exception ex)
            {
                error = "native stall insert failed: " + ex.Message;
                return false;
            }
        }

        public bool TryUpdateStall(NativeStallHeaderRow row, long ownerId, int idx, out string error) =>
            Execute(BuildUpdateStall(row, ownerId, idx), "stall update", out error);

        public bool TryExpireStalls(int isEnabled, int status, out string error) =>
            Execute(BuildExpireStalls(isEnabled, status), "stall expire", out error);

        public bool TryInsertStallItem(int stallId, long ownerId, int uprice, int moneyType, int isSold,
            int isGetMoney, DateTime createDate, int itemCount, string ownerName, out int newIdx, out string error)
        {
            // Native re-selects LAST_INSERT_ID() after the stallitem INSERT so the 208-byte srvData
            // (WriteItemSrvData) can target the row. Mirrors the TryInsertStall out-idx pattern.
            newIdx = 0;
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var command = Materialize(connection, BuildInsertStallItem(stallId, ownerId, uprice,
                    moneyType, isSold, isGetMoney, createDate, itemCount, ownerName));
                command.ExecuteNonQuery();
                newIdx = (int)command.LastInsertedId;
                return true;
            }
            catch (Exception ex)
            {
                error = "native stallitem insert failed: " + ex.Message;
                return false;
            }
        }

        public bool TryUpdateStallItem(int uprice, int moneyType, int itemCount, int isSold, int isGetMoney,
            DateTime modifyDate, int stallId, int idx, out string error) =>
            Execute(BuildUpdateStallItem(uprice, moneyType, itemCount, isSold, isGetMoney, modifyDate, stallId, idx),
                "stallitem update", out error);

        public bool TryDeleteStallItem(int idx, out string error) =>
            Execute(BuildDeleteStallItem(idx), "stallitem delete", out error);

        public bool TryMarkItemBoSended(int idx, long ownerId, out string error) =>
            Execute(BuildMarkItemBoSended(idx, ownerId), "stallitem IsBoSended", out error);

        public bool TryBackupStallItem(int idx, out string error) =>
            Execute(BuildBackupStallItem(idx), "stallitem_b backup", out error);

        public bool TryInsertStallMsg(int stallId, long ownerId, string ownerName, int cnt, DateTime createDate,
            out string error) =>
            Execute(BuildInsertStallMsg(stallId, ownerId, ownerName, cnt, createDate), "stallmsg insert", out error);

        public bool TryUpdateStallMsg(int cnt, int idx, int stallId, out string error) =>
            Execute(BuildUpdateStallMsg(cnt, idx, stallId), "stallmsg update", out error);

        public bool TryDeleteStallMsg(int stallId, out string error) =>
            Execute(BuildDeleteStallMsg(stallId), "stallmsg delete", out error);

        public bool TryExpireDeleteStallMsg(int isEnabled, int status, out string error) =>
            Execute(BuildExpireDeleteStallMsg(isEnabled, status), "stallmsg expire-delete", out error);

        public bool TryInsertBuyerOrder(long buyerId, string buyerName, long sellId, string sellName, int uprice,
            int moneyType, int count, int totalPrice, int boDecMoney, int status, out string error) =>
            Execute(BuildInsertBuyerOrder(buyerId, buyerName, sellId, sellName, uprice, moneyType, count,
                totalPrice, boDecMoney, status), "buyer_order insert", out error);

        public bool TryUpdateBuyerOrder(int boDecMoney, int status, int idx, out string error) =>
            Execute(BuildUpdateBuyerOrder(boDecMoney, status, idx), "buyer_order update", out error);

        public bool TryInsertBuyItemDetail(long buyerId, string buyerName, long sellId, string sellName, int uprice,
            int moneyType, int count, out string error) =>
            Execute(BuildInsertBuyItemDetail(buyerId, buyerName, sellId, sellName, uprice, moneyType, count),
                "buyitem_detail insert", out error);

        /// <summary>
        /// The 208-byte item record (srvData BLOB). The original does not use a SQL template for this — after
        /// the stallitem INSERT it re-selects by LAST_INSERT_ID() and writes the fixed 208-byte struct into
        /// the srvData column via an updatable result-set stream (sub_62016C: qmemcpy(dst[208],src,208) then
        /// write(208,dst)==208). Reproduced here as a parameterized UPDATE of the blob (no golden template to
        /// byte-match; asserted only for length == 208 by the compat check).
        /// </summary>
        public bool WriteItemSrvData(int idx, byte[] srvData208, out string error)
        {
            error = string.Empty;
            if (srvData208 == null || srvData208.Length != 208)
            {
                error = "stall srvData must be exactly 208 bytes";
                return false;
            }
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE gamedata.stallitem SET srvData=@srv WHERE idx=@idx";
                command.Parameters.Add("@srv", MySqlDbType.Blob).Value = srvData208;
                command.Parameters.Add("@idx", MySqlDbType.Int32).Value = idx;
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                error = "native stall srvData write failed: " + ex.Message;
                return false;
            }
        }

        private bool Execute(NativeStallSqlCommand statement, string label, out string error)
        {
            error = string.Empty;
            try
            {
                using var connection = OpenConnection();
                using var command = Materialize(connection, statement);
                // The original only checks the ExecuteScript boolean and, on failure, logs "[SQL Failed]"
                // without rolling back the published in-memory change — no affected-row assertion here either.
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                error = "native " + label + " failed: " + ex.Message;
                return false;
            }
        }

        public bool TryLoadActiveStalls(out List<NativeStallRecord> records, out string error)
        {
            records = new List<NativeStallRecord>();
            error = null;
            try
            {
                using var connection = OpenConnection();
                var byIdx = new Dictionary<int, NativeStallRecord>();
                using (var stallCmd = connection.CreateCommand())
                {
                    stallCmd.CommandText =
                        "SELECT idx,ownerID,ownername,stallname,level,DuraTime,isEnabled," +
                        "createdate,modifyDate,posx,posy,mapname,status FROM gamedata.stall WHERE isEnabled=1";
                    using var reader = stallCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var record = new NativeStallRecord
                        {
                            DbIdx = reader.GetInt32(0),
                            OwnerId = unchecked((long)reader.GetUInt64(1)),
                            OwnerName = ReadGbkString(reader, 2),
                            StallName = ReadGbkString(reader, 3),
                            Level = reader.GetInt32(4),
                            DuraTime = reader.GetInt32(5),
                            IsEnabled = reader.GetInt32(6),
                            CreateDate = reader.IsDBNull(7) ? default : reader.GetDateTime(7),
                            ModifyDate = reader.IsDBNull(8) ? default : reader.GetDateTime(8),
                            PosX = reader.GetInt32(9),
                            PosY = reader.GetInt32(10),
                            MapName = ReadGbkString(reader, 11),
                            Status = (StallRecordStatus)reader.GetInt32(12),
                        };
                        byIdx[record.DbIdx] = record;
                        records.Add(record);
                    }
                }
                if (byIdx.Count > 0)
                {
                    using var itemCmd = connection.CreateCommand();
                    itemCmd.CommandText =
                        "SELECT idx,stallid,uprice,moneytype,itemcount,isSold,isGetmoney,IsBoSended,srvdata " +
                        "FROM gamedata.stallitem WHERE stallid IN (" + string.Join(",", byIdx.Keys) + ")";
                    using var reader = itemCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        if (!byIdx.TryGetValue(reader.GetInt32(1), out var record))
                            continue;
                        var item = new NativeStallItem
                        {
                            DbIdx = reader.GetInt32(0),
                            UnitPrice = reader.GetInt32(2),
                            MoneyType = reader.GetInt32(3),
                            ItemCount = reader.GetInt32(4),
                            IsSold = reader.GetInt32(5) != 0,
                            IsGetMoney = reader.GetInt32(6) != 0,
                            IsBoSended = reader.GetInt32(7) != 0,
                        };
                        if (!reader.IsDBNull(8) && reader.GetValue(8) is byte[] srvData
                            && NativeStallItemRecordCodec.TryDecode(srvData, out var item208, out _))
                        {
                            item.Item = item208;
                        }
                        record.Items.Add(item);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                records = new List<NativeStallRecord>();
                error = ex.Message;
                return false;
            }
        }

        private static string ReadGbkString(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return string.Empty;
            return reader.GetValue(ordinal) is byte[] bytes
                ? StrictGbk.GetString(bytes)
                : reader.GetValue(ordinal)?.ToString() ?? string.Empty;
        }

        private static MySqlCommand Materialize(MySqlConnection connection, NativeStallSqlCommand statement)
        {
            var command = connection.CreateCommand();
            command.CommandText = statement.CommandText;
            foreach (var parameter in statement.Parameters)
            {
                switch (parameter.Kind)
                {
                    case NativeStallSqlValueKind.Id:
                        command.Parameters.Add(parameter.Name, MySqlDbType.UInt64).Value =
                            unchecked((ulong)(long)parameter.Value);
                        break;
                    case NativeStallSqlValueKind.Int32:
                        command.Parameters.Add(parameter.Name, MySqlDbType.Int32).Value = (int)parameter.Value;
                        break;
                    case NativeStallSqlValueKind.GbkText:
                        command.Parameters.Add(parameter.Name, MySqlDbType.VarBinary).Value =
                            StrictGbk.GetBytes((string)parameter.Value ?? string.Empty);
                        break;
                    case NativeStallSqlValueKind.DateTime:
                        command.Parameters.Add(parameter.Name, MySqlDbType.DateTime).Value =
                            (DateTime)parameter.Value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(statement),
                            "unknown stall SQL parameter kind " + parameter.Kind);
                }
            }
            return command;
        }

        private MySqlConnection OpenConnection()
        {
            var connectionString = _connectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("database connection string is empty");
            var connection = new MySqlConnection(connectionString);
            connection.Open();
            return connection;
        }

        private static NativeStallSqlParameter Id(string name, long value) =>
            new(name, NativeStallSqlValueKind.Id, value);

        private static NativeStallSqlParameter I32(string name, int value) =>
            new(name, NativeStallSqlValueKind.Int32, value);

        private static NativeStallSqlParameter Gbk(string name, string value) =>
            new(name, NativeStallSqlValueKind.GbkText, value ?? string.Empty);

        private static NativeStallSqlParameter Dt(string name, DateTime value) =>
            new(name, NativeStallSqlValueKind.DateTime, value);
    }
}
