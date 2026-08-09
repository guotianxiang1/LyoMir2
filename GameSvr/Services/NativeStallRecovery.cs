using MySql.Data.MySqlClient;
using System.Text;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Startup RECOVERY scan for the STALL subsystem (task #83, wave-2 read side). Rebuilds the in-memory
    /// <see cref="NativeStallManager"/> from <c>gamedata.stall*</c> after a restart, so live booths + their
    /// unsold items survive — mirroring the original paged reload.
    ///
    /// SELECTs are verbatim from the reversed originals (staging/update_clothes_4637_ida_work/stall_exec_out.txt):
    ///   active booths  sub_61C2DC @0x61C6F8 : SELECT idx,ownerID,ownername,stallname,level,duratime,createdate,
    ///                                          posx,posy,mapname,status FROM stall WHERE isEnabled=1 and idx>%u
    ///                                          order by idx limit 1000
    ///   owner items    sub_61F0F0 @0x61F318 : SELECT idx,stallid,srvData,uprice,moneytype,itemcount,isSold,
    ///                                          isGetMoney FROM stallitem WHERE ownerid=%d and stallid=%d and isSold=0
    /// Item bodies decode via <see cref="NativeStallItemRecordCodec"/> (the 208-byte srvData BLOB).
    ///
    /// SCOPE: <see cref="LoadActiveStalls"/> is READ-ONLY hydration (moves NO money and NO items). The
    /// pending-payout REPLAY (sub_61CD8C: closed booths status=3 with unsold/unpaid items → re-seat to the
    /// owner's record + mark IsBoSended) IS now implemented in <see cref="ReturnPendingPayouts"/> (the
    /// confirmed sub_61CA44 in-memory re-seat), conservation-safe (each row returned once, IsBoSended stops a
    /// re-scan). Fail-safe: any DB/decode error is logged and skipped — recovery never throws into startup (a
    /// missing/garbled booth is dropped, not fatal). DORMANT with the subsystem (write-gate OFF) until review.
    /// </summary>
    public static class NativeStallRecovery
    {
        private const int PageLimit = 1000;   // "... order by idx limit 1000"

        // static readonly (not const): the value concatenates PageLimit (an int), which is not a
        // compile-time constant expression (CS0133). Computed once at static-init; SQL text is identical.
        private static readonly string SelectActiveStallsSql =
            "SELECT idx,ownerID,ownername,stallname,level,duratime,createdate,posx,posy,mapname,status " +
            "FROM gamedata.stall WHERE isEnabled=1 AND idx>@minidx ORDER BY idx LIMIT " + PageLimit;

        private const string SelectOwnerItemsSql =
            "SELECT idx,stallid,srvData,uprice,moneytype,itemcount,isSold,isGetMoney " +
            "FROM gamedata.stallitem WHERE ownerid=@owner AND stallid=@stallid AND isSold=0";

        private static readonly Encoding Gbk = HUtil32.GbkEncoding;

        /// <summary>
        /// Load all live booths (paged by idx) + their unsold items into <paramref name="manager"/>.
        /// Returns the number of booths registered; 0 on empty/failure (logged).
        /// </summary>
        public static int LoadActiveStalls(Func<string> connectionString, NativeStallManager manager)
        {
            if (connectionString == null || manager == null) return 0;
            var loaded = 0;
            try
            {
                var minIdx = 0;
                while (true)
                {
                    var page = LoadStallPage(connectionString, minIdx);
                    if (page.Count == 0) break;
                    foreach (var record in page)
                    {
                        LoadStallItems(connectionString, record);
                        manager.Register(record);
                        loaded++;
                        if (record.DbIdx > minIdx) minIdx = record.DbIdx;   // page cursor = last idx
                    }
                    if (page.Count < PageLimit) break;   // last page
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage("原生摆摊恢复扫描失败(active booths): " + ex.Message);
            }
            return loaded;
        }

        private static List<NativeStallRecord> LoadStallPage(Func<string> connectionString, int minIdx)
        {
            var records = new List<NativeStallRecord>();
            using var connection = Open(connectionString);
            using var command = connection.CreateCommand();
            command.CommandText = SelectActiveStallsSql;
            command.Parameters.Add("@minidx", MySqlDbType.Int32).Value = minIdx;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new NativeStallRecord
                {
                    DbIdx = reader.GetInt32(0),
                    OwnerId = Convert.ToInt64(reader.GetValue(1)),
                    OwnerName = ReadGbk(reader, 2),
                    StallName = ReadGbk(reader, 3),
                    Level = reader.GetInt32(4),
                    DuraTime = reader.GetInt32(5),
                    CreateDate = reader.GetDateTime(6),
                    PosX = reader.GetInt32(7),
                    PosY = reader.GetInt32(8),
                    MapName = ReadGbk(reader, 9),
                    Status = (StallRecordStatus)reader.GetInt32(10),
                    IsEnabled = 1,   // WHERE isEnabled=1
                });
            }
            return records;
        }

        private static void LoadStallItems(Func<string> connectionString, NativeStallRecord record)
        {
            try
            {
                using var connection = Open(connectionString);
                using var command = connection.CreateCommand();
                command.CommandText = SelectOwnerItemsSql;
                command.Parameters.Add("@owner", MySqlDbType.UInt64).Value =
                    unchecked((ulong)record.OwnerId);
                command.Parameters.Add("@stallid", MySqlDbType.Int32).Value = record.DbIdx;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var srvData = reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2);
                    if (!NativeStallItemRecordCodec.TryDecode(srvData, out var item, out var decodeError))
                    {
                        M2Share.ErrorMessage(
                            $"原生摆摊恢复:跳过物品 idx={reader.GetInt32(0)} srvData解码失败: {decodeError}");
                        continue;
                    }
                    record.Items.Add(new NativeStallItem
                    {
                        DbIdx = reader.GetInt32(0),
                        Item = item,
                        UnitPrice = reader.GetInt32(3),
                        MoneyType = reader.GetInt32(4),
                        ItemCount = reader.GetInt32(5),
                        IsSold = reader.GetInt32(6) != 0,
                        IsGetMoney = reader.GetInt32(7) != 0,
                    });
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    $"原生摆摊恢复:加载摊位物品失败(stallid={record.DbIdx}): {ex.Message}");
            }
        }

        /// <summary>
        /// Restart/close RECOVERY (sub_61CD8C @0x61CD8C -> sub_61CA44 @0x61CA44 -> sub_6218E8): return every
        /// UNSOLD item sitting in a CLOSED booth to its owner, then mark it dispatched so it is never returned
        /// twice. Closed+unsold is exactly <c>stall.IsEnabled=0 AND stall.status=3 AND stallitem.isSold=0 AND
        /// isGetMoney=0 AND IsBoSended=0</c> (verbatim SQL from stall_exec_out.txt:2087). SOLD items are never
        /// touched — they were delivered to the buyer and the seller was paid by the settlement mail.
        ///
        /// Mechanism (confirmed, sub_61CA44): resolve-or-create the OWNER's in-memory stall record (sub_49F5F4
        /// / sub_621974) and re-seat the decoded 208-byte item into it (sub_6218E8), then UPDATE
        /// <c>IsBoSended=1</c> (sub_61CCA4, SQL 0x61CD44). This is an IN-MEMORY re-seat: the item is returned
        /// to the owner's booth record, from which the owner reclaims it to the bag via the (already
        /// conservation-safe) DEL/PAUSE paths. Conservation: each stallitem row is re-seated EXACTLY once and
        /// IsBoSended=1 makes a re-scan skip it, so an item is never returned twice (no dup). Marking
        /// IsBoSended immediately (like native) trades a rare loss (restart before the owner collects the
        /// in-memory re-seat) for guaranteed no-dup — the economy-safe choice, and native-faithful.
        /// Returns the number of items returned. DORMANT with the rest of the subsystem (gate OFF) until review.
        /// </summary>
        public static int ReturnPendingPayouts(Func<string> connectionString, NativeStallManager manager)
        {
            if (connectionString == null || manager == null) return 0;
            var returned = 0;
            try
            {
                foreach (var row in LoadClosedBoothUnsoldItems(connectionString))
                {
                    // sub_61CA44: resolve-or-create the owner's record + re-seat the item into it.
                    var record = manager.GetOrCreate(row.OwnerName, row.OwnerId);
                    record.Items.Add(new NativeStallItem
                    {
                        DbIdx = row.Idx,
                        Item = row.Item,
                        UnitPrice = row.UnitPrice,
                        MoneyType = row.MoneyType,
                        ItemCount = row.ItemCount,
                        IsSold = false,
                        IsGetMoney = false,
                        IsBoSended = true,   // dispatched back to the owner
                    });
                    // sub_61CCA4: mark IsBoSended=1 so a re-scan never returns it twice (idempotent / no dup).
                    MarkBoSended(connectionString, row.Idx, row.OwnerId);
                    returned++;
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage("原生摆摊恢复:待返还物品处理失败: " + ex.Message);
            }
            return returned;
        }

        // Verbatim closed-booth unsold-item scan (sub_61CD8C SQL 0x61CFB0), extended only with the pricing
        // columns needed to rebuild the in-memory NativeStallItem (same rows, more columns — no filter change).
        private static List<PendingPayoutRow> LoadClosedBoothUnsoldItems(Func<string> connectionString)
        {
            var rows = new List<PendingPayoutRow>();
            using var connection = Open(connectionString);
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT idx,ownerid,ownername,uprice,moneytype,itemcount,srvData FROM gamedata.stallitem " +
                    "WHERE stallid IN (SELECT idx FROM gamedata.stall WHERE IsEnabled=0 AND status=3) " +
                    "AND isSold=0 AND isGetMoney=0 AND IsBoSended=0";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var srvData = reader.IsDBNull(6) ? null : reader.GetFieldValue<byte[]>(6);
                    if (!NativeStallItemRecordCodec.TryDecode(srvData, out var item, out var decodeError))
                    {
                        M2Share.ErrorMessage(
                            $"原生摆摊恢复:跳过待返还物品 idx={reader.GetInt32(0)} 解码失败: {decodeError}");
                        continue;
                    }
                    rows.Add(new PendingPayoutRow
                    {
                        Idx = reader.GetInt32(0),
                        OwnerId = Convert.ToInt64(reader.GetValue(1)),
                        OwnerName = ReadGbk(reader, 2),
                        UnitPrice = reader.GetInt32(3),
                        MoneyType = reader.GetInt32(4),
                        ItemCount = reader.GetInt32(5),
                        Item = item,
                    });
                }
            }
            return rows;
        }

        // sub_61CCA4 @0x61CD44 : UPDATE stallitem SET IsBoSended=1 WHERE idx=%d and ownerid=%d.
        private static void MarkBoSended(Func<string> connectionString, int idx, long ownerId)
        {
            try
            {
                using var connection = Open(connectionString);
                using var command = connection.CreateCommand();
                command.CommandText =
                    "UPDATE gamedata.stallitem SET IsBoSended=1 WHERE idx=@idx AND ownerid=@owner";
                command.Parameters.Add("@idx", MySqlDbType.Int32).Value = idx;
                command.Parameters.Add("@owner", MySqlDbType.UInt64).Value = unchecked((ulong)ownerId);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"原生摆摊恢复:标记IsBoSended失败(idx={idx}): {ex.Message}");
            }
        }

        private sealed class PendingPayoutRow
        {
            public int Idx { get; init; }
            public long OwnerId { get; init; }
            public string OwnerName { get; init; } = string.Empty;
            public int UnitPrice { get; init; }
            public int MoneyType { get; init; }
            public int ItemCount { get; init; }
            public TUserItem Item { get; init; }
        }

        private static MySqlConnection Open(Func<string> connectionString)
        {
            var value = connectionString();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("database connection string is empty");
            var connection = new MySqlConnection(value);
            connection.Open();
            return connection;
        }

        // Names are stored as GBK bytes in latin1/char columns (same as CommonDB); read the raw bytes and
        // GBK-decode. Falls back to the driver string if a byte[] projection is unavailable.
        private static string ReadGbk(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return string.Empty;
            try
            {
                var raw = reader.GetFieldValue<byte[]>(ordinal);
                return Gbk.GetString(raw).TrimEnd('\0').Trim();
            }
            catch
            {
                return (reader.GetValue(ordinal)?.ToString() ?? string.Empty).Trim();
            }
        }
    }
}
