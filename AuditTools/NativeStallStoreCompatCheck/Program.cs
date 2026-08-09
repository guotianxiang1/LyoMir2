using System.Text.RegularExpressions;
using GameSvr.Services;

// NativeStallStoreCompatCheck — proves the SQL each NativeStallMySqlStore write op builds matches the
// reversed original M2Server stall SQL, with test inputs and no live database, and that the store is
// gated-dormant (NativeStallWriteGate off / store null => callers keep RejectUnavailableStallRequest).
// For every write op we assert (1) parameterized CommandText == reviewed golden literal, (2) it is
// structurally identical to the exact reversed %s/%d/%S original (schema-, placeholder- and
// whitespace-insensitive), (3) ordered params carry the test inputs under the right kind.
// Original strings verbatim from staging/update_clothes_4637_ida_work/all_strings.txt (image base
// 0x400000; the stall SQL parameterizes the schema as %s, unlike the gild store's literal gamedata).

var failures = new List<string>();
var now = new DateTime(2026, 8, 1, 10, 20, 30);

Check("InsertStall",
    NativeStallMySqlStore.BuildInsertStall(new NativeStallHeaderRow
    {
        OwnerId = 11L, OwnerName = "摆摊者", StallName = "小店", ItemCnt = 3, Level = 1, DuraTime = 24,
        IsEnabled = 1, CreateDate = now, PosX = 100, PosY = 200, MapName = "0", Status = 0
    }),
    "INSERT INTO gamedata.stall(ownerID,ownername,stallname,itemcnt,level,DuraTime,isEnabled,createdate," +
    "posx,posy,mapname,status) VALUES(@owner,@ownername,@stallname,@itemcnt,@level,@duratime,@isenabled," +
    "@createdate,@posx,@posy,@mapname,@status)",
    "INSERT INTO %s.stall(ownerID, ownername,stallname,itemcnt,level,DuraTime,isEnabled,createdate,posx," +
    "posy,mapname,status) VALUES (%d,%s,%s,%d,%d,%d,%d,%s,%d,%d,%s,%d);",
    new[]
    {
        P("@owner", NativeStallSqlValueKind.Id, 11L), P("@ownername", NativeStallSqlValueKind.GbkText, "摆摊者"),
        P("@stallname", NativeStallSqlValueKind.GbkText, "小店"), P("@itemcnt", NativeStallSqlValueKind.Int32, 3),
        P("@level", NativeStallSqlValueKind.Int32, 1), P("@duratime", NativeStallSqlValueKind.Int32, 24),
        P("@isenabled", NativeStallSqlValueKind.Int32, 1), P("@createdate", NativeStallSqlValueKind.DateTime, now),
        P("@posx", NativeStallSqlValueKind.Int32, 100), P("@posy", NativeStallSqlValueKind.Int32, 200),
        P("@mapname", NativeStallSqlValueKind.GbkText, "0"), P("@status", NativeStallSqlValueKind.Int32, 0)
    });

Check("UpdateStall",
    NativeStallMySqlStore.BuildUpdateStall(new NativeStallHeaderRow
    {
        StallName = "改名", Level = 2, ItemCnt = 5, ModifyDate = now, IsEnabled = 1, PosX = 1, PosY = 2,
        MapName = "3", Status = 1, CreateDate = now, DuraTime = 48
    }, 11L, 7),
    "UPDATE gamedata.stall SET stallname=@stallname,level=@level,itemcnt=@itemcnt,modifyDate=@modifydate," +
    "isEnabled=@isenabled,posx=@posx,posy=@posy,mapname=@mapname,status=@status,createdate=@createdate," +
    "DuraTime=@duratime WHERE ownerid=@owner AND idx=@idx",
    "UPDATE %s.stall SET stallname = %s, level = %d, itemcnt = %d,  modifyDate = %s, isEnabled = %d, " +
    "posx = %d, posy = %d,  mapname = %s, status = %d,createdate = %s,DuraTime = %d WHERE ownerid = %d and idx = %d;",
    new[]
    {
        P("@stallname", NativeStallSqlValueKind.GbkText, "改名"), P("@level", NativeStallSqlValueKind.Int32, 2),
        P("@itemcnt", NativeStallSqlValueKind.Int32, 5), P("@modifydate", NativeStallSqlValueKind.DateTime, now),
        P("@isenabled", NativeStallSqlValueKind.Int32, 1), P("@posx", NativeStallSqlValueKind.Int32, 1),
        P("@posy", NativeStallSqlValueKind.Int32, 2), P("@mapname", NativeStallSqlValueKind.GbkText, "3"),
        P("@status", NativeStallSqlValueKind.Int32, 1), P("@createdate", NativeStallSqlValueKind.DateTime, now),
        P("@duratime", NativeStallSqlValueKind.Int32, 48), P("@owner", NativeStallSqlValueKind.Id, 11L),
        P("@idx", NativeStallSqlValueKind.Int32, 7)
    });

Check("ExpireStalls",
    NativeStallMySqlStore.BuildExpireStalls(0, 3),
    "UPDATE gamedata.stall SET isEnabled=@isenabled,status=@status WHERE TIME_TO_SEC(TIMEDIFF(NOW()," +
    "createdate)) >= duratime * 60 * 60 AND TIME_TO_SEC(TIMEDIFF(NOW(),createdate)) > 0",
    "UPDATE %s.stall set isEnabled = %d, status = %d WHERE TIME_TO_SEC(TIMEDIFF(NOW(),createdate)) >= " +
    "duratime * 60 * 60 and TIME_TO_SEC(TIMEDIFF(NOW(),createdate)) > 0;",
    new[] { P("@isenabled", NativeStallSqlValueKind.Int32, 0), P("@status", NativeStallSqlValueKind.Int32, 3) });

Check("InsertStallItem",
    NativeStallMySqlStore.BuildInsertStallItem(7, 11L, 5000, 0, 0, 0, now, 1, "摆摊者"),
    "INSERT INTO gamedata.stallitem(stallid,ownerid,uprice,moneytype,isSold,isGetMoney,createdate," +
    "itemcount,ownername) VALUES(@stallid,@owner,@uprice,@moneytype,@issold,@isgetmoney,@createdate," +
    "@itemcount,@ownername)",
    "INSERT INTO %s.stallitem(stallid,ownerid,uprice,moneytype,isSold,isGetMoney,createdate,itemcount," +
    "ownername) VALUES(%d,%d,%d,%d,%d,%d,%s,%d,%S);",
    new[]
    {
        P("@stallid", NativeStallSqlValueKind.Int32, 7), P("@owner", NativeStallSqlValueKind.Id, 11L),
        P("@uprice", NativeStallSqlValueKind.Int32, 5000), P("@moneytype", NativeStallSqlValueKind.Int32, 0),
        P("@issold", NativeStallSqlValueKind.Int32, 0), P("@isgetmoney", NativeStallSqlValueKind.Int32, 0),
        P("@createdate", NativeStallSqlValueKind.DateTime, now), P("@itemcount", NativeStallSqlValueKind.Int32, 1),
        P("@ownername", NativeStallSqlValueKind.GbkText, "摆摊者")
    });

Check("UpdateStallItem",
    NativeStallMySqlStore.BuildUpdateStallItem(6000, 1, 2, 1, 1, now, 7, 42),
    "UPDATE gamedata.stallitem SET uprice=@uprice,moneytype=@moneytype,itemcount=@itemcount,isSold=@issold," +
    "isGetMoney=@isgetmoney,modifydate=@modifydate,stallid=@stallid WHERE Idx=@idx",
    "UPDATE %s.stallitem set uprice =%d, moneytype=%d,itemcount = %d, isSold=%d, isGetMoney = %d, " +
    "modifydate = %s, stallid = %d where Idx =%d;",
    new[]
    {
        P("@uprice", NativeStallSqlValueKind.Int32, 6000), P("@moneytype", NativeStallSqlValueKind.Int32, 1),
        P("@itemcount", NativeStallSqlValueKind.Int32, 2), P("@issold", NativeStallSqlValueKind.Int32, 1),
        P("@isgetmoney", NativeStallSqlValueKind.Int32, 1), P("@modifydate", NativeStallSqlValueKind.DateTime, now),
        P("@stallid", NativeStallSqlValueKind.Int32, 7), P("@idx", NativeStallSqlValueKind.Int32, 42)
    });

Check("DeleteStallItem",
    NativeStallMySqlStore.BuildDeleteStallItem(42),
    "DELETE FROM gamedata.stallitem WHERE idx=@idx",
    "DELETE FROM %s.stallitem where idx = %d ;",
    new[] { P("@idx", NativeStallSqlValueKind.Int32, 42) });

Check("MarkItemBoSended",
    NativeStallMySqlStore.BuildMarkItemBoSended(42, 11L),
    "UPDATE gamedata.stallitem SET IsBoSended=1 WHERE idx=@idx AND ownerid=@owner",
    "UPDATE %s.stallitem SET IsBoSended = 1 WHERE idx = %d and ownerid = %d;",
    new[] { P("@idx", NativeStallSqlValueKind.Int32, 42), P("@owner", NativeStallSqlValueKind.Id, 11L) });

Check("BackupStallItem",
    NativeStallMySqlStore.BuildBackupStallItem(42),
    "INSERT INTO gamedata.stallitem_b(stallid,ownerId,ownername,uprice,moneytype,itemcount,srvdata," +
    "modifydate,createdate,isGetmoney,isSold) SELECT stallid,ownerid,ownername,uprice,moneytype,itemcount," +
    "srvdata,modifydate,Now(),isGetmoney,isSold FROM gamedata.stallitem WHERE idx=@idx",
    "INSERT INTO %s.stallitem_b(stallid,ownerId,ownername,uprice,moneytype,itemcount,srvdata,modifydate," +
    "createdate,isGetmoney,isSold)SELECT stallid,ownerid,ownername,uprice,moneytype,itemcount,srvdata," +
    "modifydate,Now(),isGetmoney,isSold from %s.stallitem where idx = %d;",
    new[] { P("@idx", NativeStallSqlValueKind.Int32, 42) });

Check("InsertStallMsg",
    NativeStallMySqlStore.BuildInsertStallMsg(7, 11L, "摆摊者", 1, now),
    "INSERT INTO gamedata.stallmsglst(stallid,ownerid,ownername,cnt,createdate) VALUES(@stallid,@owner," +
    "@ownername,@cnt,@createdate)",
    "INSERT INTO %s.stallmsglst(stallid, ownerid,ownername,cnt,createdate) VALUES(%d,%d,%s,%d,%s);",
    new[]
    {
        P("@stallid", NativeStallSqlValueKind.Int32, 7), P("@owner", NativeStallSqlValueKind.Id, 11L),
        P("@ownername", NativeStallSqlValueKind.GbkText, "摆摊者"), P("@cnt", NativeStallSqlValueKind.Int32, 1),
        P("@createdate", NativeStallSqlValueKind.DateTime, now)
    });

Check("UpdateStallMsg",
    NativeStallMySqlStore.BuildUpdateStallMsg(5, 9, 7),
    "UPDATE gamedata.stallmsglst SET cnt=@cnt,updatetime=Now() WHERE idx=@idx AND stallid=@stallid",
    "UPDATE %s.stallmsglst SET cnt = %d, updatetime = Now() WHERE idx = %d and stallid = %d;",
    new[]
    {
        P("@cnt", NativeStallSqlValueKind.Int32, 5), P("@idx", NativeStallSqlValueKind.Int32, 9),
        P("@stallid", NativeStallSqlValueKind.Int32, 7)
    });

Check("DeleteStallMsg",
    NativeStallMySqlStore.BuildDeleteStallMsg(7),
    "DELETE FROM gamedata.stallmsglst WHERE stallid=@stallid",
    "DELETE FROM %s.stallmsglst WHERE stallid = %d;",
    new[] { P("@stallid", NativeStallSqlValueKind.Int32, 7) });

Check("ExpireDeleteStallMsg",
    NativeStallMySqlStore.BuildExpireDeleteStallMsg(0, 3),
    "DELETE FROM gamedata.stallmsglst WHERE stallid IN (SELECT idx FROM gamedata.stall WHERE " +
    "isEnabled=@isenabled AND status=@status)",
    "DELETE FROM %s.stallmsglst WHERE stallid in (SELECT idx FROM %s.stall WHERE isEnabled = %d and status = %d);",
    new[] { P("@isenabled", NativeStallSqlValueKind.Int32, 0), P("@status", NativeStallSqlValueKind.Int32, 3) });

Check("InsertBuyerOrder",
    NativeStallMySqlStore.BuildInsertBuyerOrder(11L, "买家", 22L, "卖家", 5000, 0, 1, 5000, 0, 0),
    "INSERT INTO gamedata.buyer_order(buyerid,buyername,sellid,sellname,uprice,moneytype,count,totalprice," +
    "boDecMoney,status,createdate) VALUES(@buyer,@buyername,@sell,@sellname,@uprice,@moneytype,@count," +
    "@totalprice,@bodecmoney,@status,Now())",
    "INSERT INTO %s.buyer_order (buyerid, buyername,sellid,sellname,uprice, moneytype,count,totalprice, " +
    "boDecMoney,status,createdate) VALUES (%d,%s,%d,%s,%d,%d,%d,%d,%d,%d,Now());",
    new[]
    {
        P("@buyer", NativeStallSqlValueKind.Id, 11L), P("@buyername", NativeStallSqlValueKind.GbkText, "买家"),
        P("@sell", NativeStallSqlValueKind.Id, 22L), P("@sellname", NativeStallSqlValueKind.GbkText, "卖家"),
        P("@uprice", NativeStallSqlValueKind.Int32, 5000), P("@moneytype", NativeStallSqlValueKind.Int32, 0),
        P("@count", NativeStallSqlValueKind.Int32, 1), P("@totalprice", NativeStallSqlValueKind.Int32, 5000),
        P("@bodecmoney", NativeStallSqlValueKind.Int32, 0), P("@status", NativeStallSqlValueKind.Int32, 0)
    });

// NOTE: the reversed original @0x00620AE4 has a "%.s" schema-format TYPO ("UPDATE %.s.buyer_order ...");
// the C# builds the intended gamedata.buyer_order. Normalize() strips %.s so the skeleton still matches.
Check("UpdateBuyerOrder",
    NativeStallMySqlStore.BuildUpdateBuyerOrder(1, 2, 9),
    "UPDATE gamedata.buyer_order SET boDecMoney=@bodecmoney,status=@status WHERE idx=@idx",
    "UPDATE %.s.buyer_order set boDecMoney = %d, status = %d WHERE idx = %d;",
    new[]
    {
        P("@bodecmoney", NativeStallSqlValueKind.Int32, 1), P("@status", NativeStallSqlValueKind.Int32, 2),
        P("@idx", NativeStallSqlValueKind.Int32, 9)
    });

Check("InsertBuyItemDetail",
    NativeStallMySqlStore.BuildInsertBuyItemDetail(11L, "买家", 22L, "卖家", 5000, 0, 1),
    "INSERT INTO gamedata.buyitem_detail(buyerid,buyername,sellId,sellname,uprice,moneytype,count," +
    "createdate) VALUES(@buyer,@buyername,@sell,@sellname,@uprice,@moneytype,@count,Now())",
    "INSERT INTO %s.buyitem_detail(buyerid, buyername,sellId,sellname, uprice, moneytype,count,createdate) " +
    "VALUES (%d,%s,%d,%s,%d,%d,%d,Now());",
    new[]
    {
        P("@buyer", NativeStallSqlValueKind.Id, 11L), P("@buyername", NativeStallSqlValueKind.GbkText, "买家"),
        P("@sell", NativeStallSqlValueKind.Id, 22L), P("@sellname", NativeStallSqlValueKind.GbkText, "卖家"),
        P("@uprice", NativeStallSqlValueKind.Int32, 5000), P("@moneytype", NativeStallSqlValueKind.Int32, 0),
        P("@count", NativeStallSqlValueKind.Int32, 1)
    });

VerifyGate();
VerifySrvData();

if (failures.Count == 0)
{
    Console.WriteLine("PASS NativeStallStoreCompatCheck: 15 stall write ops SQL byte-match + gated-dormant " +
        "(gate off/store null => reject fallback) + srvData==208 guard; direct-MySQL gamedata.stall* (sub_724E48)");
    return 0;
}

Console.Error.WriteLine("NativeStallStoreCompatCheck: FAIL");
foreach (var failure in failures) Console.Error.WriteLine("  - " + failure);
return 1;

void Check(string op, NativeStallSqlCommand built, string golden, string original,
    NativeStallSqlParameter[] expected)
{
    if (built.CommandText != golden)
        failures.Add($"{op}: CommandText != golden\n      built = {built.CommandText}\n      const = {golden}");

    var normBuilt = Normalize(built.CommandText);
    var normOriginal = Normalize(original);
    if (normBuilt != normOriginal)
        failures.Add($"{op}: normalized SQL != reversed original\n      built    = {normBuilt}\n" +
                     $"      original = {normOriginal}");

    if (built.Parameters.Count != expected.Length)
    {
        failures.Add($"{op}: parameter count {built.Parameters.Count} != {expected.Length}");
        return;
    }
    for (var i = 0; i < expected.Length; i++)
    {
        var a = built.Parameters[i];
        var e = expected[i];
        if (a.Name != e.Name || a.Kind != e.Kind || !ValueEquals(a.Value, e.Value))
            failures.Add($"{op}: parameter[{i}] mismatch\n      built    = {Describe(a)}\n" +
                         $"      expected = {Describe(e)}");
    }
}

void VerifyGate()
{
    // Default: dormant — no store injected, flag off => Enabled false => callers keep the reject fallback.
    if (NativeStallWriteGate.Enabled)
        failures.Add("gate: Enabled true by default (should be dormant)");
    NativeStallWriteGate.SupportsStallWrites = true;
    if (NativeStallWriteGate.Enabled)
        failures.Add("gate: flag on but store null must stay disabled (reject fallback)");
    NativeStallWriteGate.Store = new NativeStallMySqlStore(() => "dummy");
    if (!NativeStallWriteGate.Enabled)
        failures.Add("gate: store + flag should enable");
    // restore dormant
    NativeStallWriteGate.Store = null;
    NativeStallWriteGate.SupportsStallWrites = false;
    if (NativeStallWriteGate.Enabled)
        failures.Add("gate: reset to dormant failed");
}

void VerifySrvData()
{
    // The 208-byte guard rejects wrong-length blobs BEFORE any DB access (no connection opened).
    var store = new NativeStallMySqlStore(() => "dummy");
    if (store.WriteItemSrvData(1, new byte[10], out var err) || string.IsNullOrEmpty(err))
        failures.Add("srvData: wrong length must fail-closed with a message");
}

static string Normalize(string sql)
{
    var text = sql.ToLowerInvariant();
    text = text.Replace("gamedata", string.Empty);   // literal (built) vs %s schema (original) both drop to ""
    text = Regex.Replace(text, "%\\.?[dsu]", string.Empty);   // %d %s %u %S(->%s) and the %.s buyer_order typo
    text = Regex.Replace(text, "@[a-z0-9_]+", string.Empty);
    text = text.Replace("'", string.Empty).Replace("\"", string.Empty);
    text = Regex.Replace(text, "\\s+", string.Empty);
    return text.TrimEnd(';');
}

static NativeStallSqlParameter P(string name, NativeStallSqlValueKind kind, object value) =>
    new(name, kind, value);

static bool ValueEquals(object a, object b) => Equals(a, b);

static string Describe(NativeStallSqlParameter p) => $"{p.Name} {p.Kind} {Convert.ToString(p.Value)}";
