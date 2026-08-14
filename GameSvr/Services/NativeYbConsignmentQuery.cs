using System;
using System.Collections.Generic;
using System.IO;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// The four read-only queries of 战神's 元宝寄售 (YB consignment) subsystem: CM 1252 / 1253 /
    /// 1256 / 1257. These are the only four members of the whole 62-item BLOCKED backlog that
    /// carry real production traffic (64-96 hits each in the live gate counters).
    ///
    /// Dispatch is a jump table: 0x6D8300 `05 50 FB FF FF add eax,-1200` / 0x6D8305
    /// `83 F8 3A cmp eax,0x3A` / 0x6D830E `FF 24 85 15 83 6D 00 jmp [eax*4+0x6D8315]`, so table
    /// index 52/53/56/57 are idents 1252/1253/1256/1257. All four arms are two instructions:
    ///   0x6DA685  8B 45 FC / E8 AF D7 00 00   mov eax,[ebp-4] / call 0x6E7E3C     ; 1252
    ///   0x6DA692  8B 45 FC / E8 F6 D7 00 00   mov eax,[ebp-4] / call 0x6E7E90     ; 1253
    ///   0x6DA6D5  8B 45 FC / E8 CF DC 00 00   mov eax,[ebp-4] / call 0x6E83AC     ; 1256
    ///   0x6DA6E2  8B 45 FC / E8 16 DD 00 00   mov eax,[ebp-4] / call 0x6E8400     ; 1257
    /// and all four callees are the same six lines: take self's name out of the ShortString at
    /// self+0x106 (0x405774) and hand it to one method of the manager at [[0x7D6ABC]]:
    ///   1252 -> 0x632A14 ; 1253 -> 0x632E7C ; 1256 -> 0x632BEC ; 1257 -> 0x632D34
    /// NO packet field is read. Recog/Param/Tag/Series and the body are all ignored.
    ///
    /// WHAT THE SUBSYSTEM IS. The manager talks to MySQL. The DDL it issues (literal at
    /// 0x630378, referenced by 0x630271) settles the question:
    ///
    ///   Create table if not exists gamedata.SellItems (
    ///     idx int unsigned not null Auto_increment primary key,
    ///     PTID char(20) binary not null, CharName Char(14) binary not null,
    ///     userLv int default 0, TargetName Char(14) binary not null default "",
    ///     Credit int default 0,
    ///     Status Enum("Confrim","True","Undetermined","Cancel","TimeOut","GivedSellerYB")
    ///            not null default "Undetermined",
    ///     UpdateTime DateTime not null, Data Blob,
    ///     Index FromName_index (CharName), Index ToName_index (TargetName));
    ///
    /// `CharName` is the seller, `TargetName` the buyer, `Credit` the 元宝 price, `Data` the
    /// item payload, and the `GivedSellerYB` status is the "seller has been paid" terminal
    /// state that CM 1254's callback machine (already modelled in
    /// NativeYbDealPurchaseStateMachine) writes. Completed deals move to `ybDealHis`.
    ///
    /// So the four idents are the four list views a player can ask for:
    ///   1252 incoming offers still awaiting my decision  (SellItems, TargetName = me)
    ///   1253 my own offers not yet settled or timed out  (SellItems, CharName = me)
    ///   1256 my purchase history                         (ybDealHis, TargetName = me)
    ///   1257 my sale history                             (ybDealHis, CharName = me)
    /// </summary>
    public static class NativeYbConsignmentQuery
    {
        // ---- request idents -------------------------------------------------------------
        public const int CmIncomingPending = 1252;
        public const int CmOutgoingPending = 1253;
        public const int CmBuyerHistory = 1256;
        public const int CmSellerHistory = 1257;

        // ---- reply idents ---------------------------------------------------------------
        // The manager does not name the SM ident directly; it hands sub_6E80CC a selector in
        // ECX and that function translates. 0x6E80DE-0x6E8129:
        //   0x6E80E1  2D 7A 04 00 00  sub eax,0x47A / je 0x6E80F8 -> [ebp-0x10] = 0xBB9
        //   0x6E80E8  48 / 74 1E      dec eax       / je 0x6E8109 -> [ebp-0x10] = 0xBBA
        //   0x6E80EB  83 E8 05 / 74   sub eax,5     / je 0x6E811A -> [ebp-0x10] = 0xBBD
        //   0x6E80F0  48 / 74 30      dec eax       / je 0x6E8123 -> [ebp-0x10] = 0xBBE
        //   0x6E80F3  E9 F0 01 00 00  jmp 0x6E82E8 (return without sending)
        public const int SelectorIncomingPending = 0x47A;   // 1146
        public const int SelectorOutgoingPending = 0x47B;   // 1147
        public const int SelectorBuyerHistory = 0x480;      // 1152
        public const int SelectorSellerHistory = 0x481;     // 1153

        public const int SmIncomingPending = 0xBB9;         // 3001
        public const int SmOutgoingPending = 0xBBA;         // 3002
        public const int SmBuyerHistory = 0xBBD;            // 3005
        public const int SmSellerHistory = 0xBBE;           // 3006

        // ---- the map gate, sub_632650 ----------------------------------------------------
        // 0x63266E `lea edx,[player+0x115]` (map name) -> 0x405774 -> 0x40BDCC against the
        // literal at 0x6326E0, and on a miss the same test against 0x6326EC. Anywhere else the
        // request produces no reply at all.
        //
        // 0x40BDCC is SameText, i.e. it returns a BOOLEAN, not a comparison result: its body is
        // `39 D0 cmp eax,edx / 74 19 je 0x40BDE9` ... `0x40BDE9 B0 01 mov al,1 / C3 ret` versus
        // `0x40BDEC 31 C0 xor eax,eax / C3 ret`. Both call sites agree with that reading —
        // 0x632686 `84 C0 test al,al / 75 1F jne 0x6326A9` jumps TO `mov bl,1` on a non-zero
        // return, and 0x6326A5 `74 02 je 0x6326AB` skips it on a zero return. Reading 0x40BDCC as
        // CompareText would invert both arms.
        public const string AllowedMapA = "ga0";            // literal 0x6326E0, declen 3
        public const string AllowedMapB = "SLDG";           // literal 0x6326EC, declen 4

        // ---- the 0x84A intermediate record ----------------------------------------------
        // Built by the four fill functions straight out of the result set; the field->offset
        // binding is FieldByName(...) at 0x630955/0x63096C/0x6309BC/0x6309D3/0x6309EB/0x630A03
        // for 1252 and the parallel sites for the other three.
        public const int RecordSize = 0x84A;                // 0x2A header + 0x820 blob
        public const int RecordNameOffset = 0x00;           // ShortString, 0x4039E4 with cl=0x0F
        public const int RecordNameCapacity = 0x0F;
        public const int RecordIdxOffset = 0x10;            // Idx           (AsInteger, [vmt+0x58])
        public const int RecordCreditOffset = 0x14;         // Credit        (AsInteger)
        public const int RecordStateOffset = 0x18;          // Status+0 as ConsState (1253 only)
        public const int RecordUserLvOffset = 0x20;         // userLv        (AsInteger, word)
        public const int RecordTimeOffset = 0x22;           // UpdateTime / dealDateTime, TDateTime
        public const int RecordBlobOffset = 0x2A;           // Data blob
        public const int RecordBlobSize = 0x820;            // 10 slots x 0xD0 bytes

        // ---- the wire record, written by sub_6E80CC 0x6E8183-0x6E81DD -------------------
        //   0x6E818D  name       0x4039E4 cl=0x0F from src+0x00
        //   0x6E8198  [dst+0x10] = [src+0x10]      (Idx)
        //   0x6E81A1  [dst+0x14] = [src+0x14]      (Credit)
        //   0x6E81AA  [dst+0x1A] = [src+0x19]
        //   0x6E81B3  [dst+0x19] = [src+0x18]      (ConsState)
        //   0x6E81BC  [dst+0x1B] = [src+0x1A]
        //   0x6E81C6  [dst+0x1C] = [src+0x20]      (userLv, word)
        //   0x6E81D0  [dst+0x20] = [src+0x22]      (TDateTime low dword)
        //   0x6E81D6  [dst+0x24] = [src+0x26]      (TDateTime high dword)
        //   0x6E81D9  [dst+0x18] = 0               (then incremented once per emitted item)
        // followed by the per-item payload at dst+0x28.
        public const int WireRecordHeaderSize = 0x28;
        public const int WireNameOffset = 0x00;
        public const int WireIdxOffset = 0x10;
        public const int WireCreditOffset = 0x14;
        public const int WireItemCountOffset = 0x18;
        public const int WireStateOffset = 0x19;
        public const int WireByte1AOffset = 0x1A;
        public const int WireByte1BOffset = 0x1B;
        public const int WireUserLvOffset = 0x1C;
        public const int WireTimeOffset = 0x20;

        /// <summary>Which of the manager's two throttle slots an ident uses.</summary>
        public enum ThrottleSlot
        {
            /// <summary>manager+0x20, shared by 1252 and 1253.</summary>
            Pending = 0,
            /// <summary>manager+0x24, shared by 1256 and 1257.</summary>
            History = 1,
        }

        /// <summary>
        /// How the elapsed-tick difference is tested. Each of the four methods spells its own
        /// comparison out, and they are NOT all the same — three distinct rules across four
        /// idents, so the value has to be read per ident rather than shared by slot.
        /// </summary>
        public enum ThrottleRule
        {
            /// <summary>
            /// Needs &gt; 10 ms. 1252: 0x632A63 `2B 56 20 sub edx,[esi+0x20]` /
            /// 0x632A66 `83 FA 0A cmp edx,0x0A` / 0x632A69 `0F 86 B4 00 00 00 jbe 0x632B23`.
            /// 1253: 0x632ECB / 0x632ECE / 0x632ED1, byte-identical apart from the jump target.
            /// </summary>
            MoreThanTenMs,
            /// <summary>
            /// Needs &gt; 2 ms. 1257 only: 0x632D83 `2B 56 24 sub edx,[esi+0x24]` /
            /// 0x632D86 `83 FA 02 cmp edx,2` / 0x632D89 `0F 86 C4 00 00 00 jbe 0x632E53`.
            /// </summary>
            MoreThanTwoMs,
            /// <summary>
            /// Needs a different tick. 1256 only, and it has no `cmp` at all — the `sub` sets ZF
            /// and the branch reads it directly: 0x632C3B `2B 56 24 sub edx,[esi+0x24]` /
            /// 0x632C3E `0F 84 C4 00 00 00 je 0x632D08`.
            /// </summary>
            DifferentTick,
        }

        public readonly struct Descriptor
        {
            public Descriptor(int cmIdent, int managerVa, int selector, int smIdent,
                ThrottleSlot slot, ThrottleRule rule, int cap, string countSql, string pageSql,
                bool clearsCachedList, bool skipsPageWhenEmpty)
            {
                CmIdent = cmIdent;
                ManagerVa = managerVa;
                Selector = selector;
                SmIdent = smIdent;
                Slot = slot;
                Rule = rule;
                Cap = cap;
                CountSql = countSql;
                PageSql = pageSql;
                ClearsCachedList = clearsCachedList;
                SkipsPageWhenEmpty = skipsPageWhenEmpty;
            }

            public int CmIdent { get; }
            /// <summary>VA of the manager method the two-instruction dispatch arm reaches.</summary>
            public int ManagerVa { get; }
            public int Selector { get; }
            public int SmIdent { get; }
            public ThrottleSlot Slot { get; }
            public ThrottleRule Rule { get; }
            /// <summary>`cmp [ebp-4],N / jle / mov [ebp-4],N` right after the count query.</summary>
            public int Cap { get; }
            public string CountSql { get; }
            public string PageSql { get; }
            /// <summary>
            /// sub_6E80CC runs a per-player list teardown before serialising, but only for the
            /// two pending selectors: 0x6E8102 `call 0x6E7F30` for 0x47A and 0x6E8113
            /// `call 0x6E7EE4` for 0x47B. Both walk and free the linked list at player+0xA0C and
            /// zero the head. The history selectors have no such hook.
            /// </summary>
            public bool ClearsCachedList { get; }
            /// <summary>
            /// Whether a zero count skips the page query. Three of the four open the fill with
            /// `83 7D FC 00 cmp dword[ebp-4],0 / 7E 27 jle` — 0x632F2C (1253), 0x632C99 (1256),
            /// 0x632DE4 (1257) — but 1252 has no such test and runs sub_6308C8 unconditionally
            /// (0x632AD7 pushes [ebp-4] straight into the call at 0x632AF1). Same reply either
            /// way; kept because it is one DB round trip the other three do not make.
            /// </summary>
            public bool SkipsPageWhenEmpty { get; }
        }

        private static readonly Dictionary<int, Descriptor> s_descriptors = new()
        {
            [CmIncomingPending] = new Descriptor(CmIncomingPending, 0x632A14,
                SelectorIncomingPending, SmIncomingPending,
                ThrottleSlot.Pending, ThrottleRule.MoreThanTenMs, 8,
                // literal 0x630C5C, referenced at 0x630BEF inside the count fn sub_630BB0
                "Select Count(*) from gamedata.SellItems where TargetName=\"%s\" " +
                "and Status=\"Undetermined\";",
                // literal 0x630AB4, referenced at 0x63092C inside the page fn sub_6308C8
                "Select idx, UpdateTime, CharName, Credit, userLv, Data from SellItems " +
                "where TargetName=\"%s\" and Status=\"Undetermined\" order by UpdateTime desc Limit 8;",
                clearsCachedList: true, skipsPageWhenEmpty: false),

            [CmOutgoingPending] = new Descriptor(CmOutgoingPending, 0x632E7C,
                SelectorOutgoingPending, SmOutgoingPending,
                ThrottleSlot.Pending, ThrottleRule.MoreThanTenMs, 4,
                // literal 0x63137C, referenced at 0x63130F inside sub_6312D0
                "Select Count(*) from SellItems where CharName=\"%s\" " +
                "and Status in (\"Undetermined\", \"TimeOut\");",
                // literal 0x6315EC, referenced at 0x631440 inside sub_6313DC
                "Select idx, UpdateTime, TargetName, userLv, Status+0 as ConsState, Credit, Data " +
                "from SellItems where CharName=\"%s\" and Status in (\"Undetermined\", \"TimeOut\") " +
                "Order by UpdateTime DESC Limit 4;",
                clearsCachedList: true, skipsPageWhenEmpty: true),

            [CmBuyerHistory] = new Descriptor(CmBuyerHistory, 0x632BEC,
                SelectorBuyerHistory, SmBuyerHistory,
                ThrottleSlot.History, ThrottleRule.DifferentTick, 8,
                // literal 0x6317D0, referenced at 0x631763 inside sub_631724
                "select Count(idx) from ybDealHis where TargetName=\"%s\";",
                // literal 0x6319FC, referenced at 0x63186C inside sub_631808
                "Select idx, dealDateTime, CharName, userLv, Credit, Data from ybDealHis " +
                "where TargetName=\"%s\" order by dealDateTime desc Limit 8;",
                clearsCachedList: false, skipsPageWhenEmpty: true),

            [CmSellerHistory] = new Descriptor(CmSellerHistory, 0x632D34,
                SelectorSellerHistory, SmSellerHistory,
                ThrottleSlot.History, ThrottleRule.MoreThanTwoMs, 8,
                // literal 0x631B94, referenced at 0x631B27 inside sub_631AE8
                "select Count(idx) from ybDealHis where CharName=\"%s\";",
                // literal 0x631DC0, referenced at 0x631C30 inside sub_631BCC
                "Select idx, dealDateTime, TargetName, userLv, Credit, Data from ybDealHis " +
                "where CharName=\"%s\" order by dealDateTime desc Limit 8;",
                clearsCachedList: false, skipsPageWhenEmpty: true),
        };

        public static IReadOnlyDictionary<int, Descriptor> Descriptors => s_descriptors;

        public static bool TryGetDescriptor(int cmIdent, out Descriptor descriptor) =>
            s_descriptors.TryGetValue(cmIdent, out descriptor);

        /// <summary>sub_632650: the request only exists on these two maps.</summary>
        public static bool MapAllowsConsignmentQuery(string mapName)
        {
            if (string.IsNullOrEmpty(mapName)) return false;
            return string.Equals(mapName, AllowedMapA, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapName, AllowedMapB, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The manager's throttle. `elapsed` is `GetTickCount() - slot`. `jbe` after a `sub` is
        /// an unsigned test and it is the REJECT arm — 0x632A69 `jbe 0x632B23` skips the
        /// write-back at 0x632A6F and the emitter call at 0x632B17 — so a tick that went
        /// backwards subtracts to 0xFFFFFFFF, compares ABOVE 0x0A, and is let through.
        /// The original has no wrap guard; do not add one.
        /// </summary>
        public static bool ThrottleAllows(ThrottleRule rule, int elapsed) => rule switch
        {
            ThrottleRule.MoreThanTenMs => (uint)elapsed > 10u,
            ThrottleRule.MoreThanTwoMs => (uint)elapsed > 2u,
            _ => elapsed != 0,
        };

        // The two slots live on the manager singleton at [[0x7D6ABC]] (+0x20 and +0x24), so they
        // are server-wide, not per character. Delphi zero-initialises the object, so both start
        // at 0 and the very first request of each pair always passes.
        private static readonly object s_throttleLock = new();
        private static readonly int[] s_throttleSlots = new int[2];

        /// <summary>
        /// Test the slot and, only on the passing arm, write the tick back — the write-backs are
        /// 0x632A6F / 0x632C44 / 0x632D8F / 0x632ED7 and every one of them sits after the branch.
        /// </summary>
        public static bool TryPassThrottle(Descriptor descriptor, int tick)
        {
            lock (s_throttleLock)
            {
                var slot = (int)descriptor.Slot;
                if (!ThrottleAllows(descriptor.Rule, tick - s_throttleSlots[slot])) return false;
                s_throttleSlots[slot] = tick;
                return true;
            }
        }

        /// <summary>Test hook: the singleton is constructed once, so nothing else resets these.</summary>
        public static void ResetThrottleSlots()
        {
            lock (s_throttleLock)
            {
                s_throttleSlots[0] = 0;
                s_throttleSlots[1] = 0;
            }
        }

        /// <summary>One row of the result set, in the shape the fill functions leave it.</summary>
        public sealed class Record
        {
            /// <summary>Counterparty name: CharName for 1252/1256, TargetName for 1253/1257.</summary>
            public string CounterpartyName { get; init; } = string.Empty;
            public int Idx { get; init; }
            public int Credit { get; init; }
            /// <summary>`Status+0 as ConsState`; only 1253 selects it, the others leave 0.</summary>
            public byte ConsState { get; init; }
            public byte Byte19 { get; init; }
            public byte Byte1A { get; init; }
            public ushort UserLv { get; init; }
            /// <summary>UpdateTime / dealDateTime as a Delphi TDateTime double.</summary>
            public double Time { get; init; }
            /// <summary>
            /// Item payload already in wire form, appended after the 0x28 header. The blob holds
            /// a FIXED ten slots of 0xD0 bytes (0x6E81F3 `6B C3 1A imul eax,ebx,0x1A` /
            /// 0x6E81F9 `8D 44 C2 2A lea eax,[edx+eax*8+0x2A]` = src+0x2A+ebx*0xD0, bounded by
            /// 0x6E823B `83 FB 0A cmp ebx,0x0A / 75 B3 jne`), but only slots whose word at +4 is
            /// non-zero are emitted (0x6E8203 `66 83 78 04 00 cmp word[eax+4],0 / 76 30 jbe`) and
            /// only if the item index resolves (0x6E8214 `call 0x74DAE4`, then `test edi,edi /
            /// je`). So the emitted length is variable even though the source blob is not, and
            /// the copy at 0x6E8268 (`call 0x403260 Move` into buffer+len+0x28) uses the
            /// encoder's own length from 0x7567C4.
            /// </summary>
            public byte[] ItemPayload { get; init; } = Array.Empty<byte>();
            /// <summary>
            /// Value written to wire offset 0x18: the number of items actually emitted, not the
            /// number of occupied slots. Native zeroes it at 0x6E81D9 `C6 46 18 00` and bumps it
            /// once per surviving slot at 0x6E8237 `FE 46 18 inc byte[esi+0x18]`.
            /// </summary>
            public byte ItemCount { get; init; }
        }

        /// <summary>
        /// Backing store for the two MySQL tables. 战神 issues the SQL above through its ADO
        /// connection; when the statement fails or matches nothing, sub_630BB0 leaves EBX at 0
        /// (0x630C08 `dec eax / jne 0x630C26` skips the field read), the count stays 0, the
        /// serialisation loop at 0x6E814D is skipped by `test eax,eax / jle`, and the reply still
        /// goes out with Param 0 and an empty body. That is exactly what the default store below
        /// reproduces for a deployment with no gamedata.SellItems table.
        /// </summary>
        public interface INativeYbConsignmentStore
        {
            int Count(int cmIdent, string charName);
            IReadOnlyList<Record> Page(int cmIdent, string charName, int max);
        }

        private sealed class EmptyStore : INativeYbConsignmentStore
        {
            public int Count(int cmIdent, string charName) => 0;

            public IReadOnlyList<Record> Page(int cmIdent, string charName, int max) =>
                Array.Empty<Record>();
        }

        public static INativeYbConsignmentStore Store { get; set; } = new EmptyStore();

        /// <summary>
        /// Debug strings from native 0x006F1BE8 (four list kinds + row format at 0x6F1E20).
        /// Emitted once per query when <see cref="DebugLoggingEnabled"/> is true.
        /// </summary>
        internal static bool DebugLoggingEnabled;

        private static readonly string[] s_debugListHeaders =
        {
            "查找玩家正在出售订单：",
            "查找玩家已购买订单：",
            "查找玩家已出售订单：",
            "查找玩家保管："
        };

        internal static void EmitQueryDebugLog(TPlayObject player, int cmIdent,
            int total, int start, int count)
        {
            if (!DebugLoggingEnabled || player == null) return;
            var headerIndex = cmIdent switch
            {
                CmOutgoingPending => 0,
                CmBuyerHistory => 1,
                CmSellerHistory => 2,
                CmIncomingPending => 3,
                _ => -1
            };
            if (headerIndex < 0) return;

            player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                0xDB, 0xFF, 0,
                s_debugListHeaders[headerIndex] +
                $" 总数：{total}, 当前起始位置: {start}, 个数：{count}");
        }

        internal static void EmitRowDebugLog(TPlayObject player,
            NativeYbConsignmentQuery.Record record, string buyerName,
            string sellerName)
        {
            if (!DebugLoggingEnabled || player == null || record == null) return;
            player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                0xDB, 0xFF, 0,
                string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "DBIdx: {0}, 道具名: {1}, 道具数: {2}, 总价: {3}, 买家名: {4}, 卖家名: {5}",
                    record.Idx, record.CounterpartyName, record.ItemCount,
                    record.Credit, buyerName ?? string.Empty,
                    sellerName ?? string.Empty));
        }

        /// <summary>
        /// Serialise the page into the reply body: a 0x28-byte header per record followed by that
        /// record's item payload, exactly as sub_6E80CC lays it out. The returned length is the
        /// running total the send site reads out of [ebp-0x14] at 0x6E82C8.
        /// </summary>
        public static byte[] BuildReplyBody(IReadOnlyList<Record> records)
        {
            if (records == null || records.Count == 0) return Array.Empty<byte>();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            foreach (var record in records)
            {
                var header = new byte[WireRecordHeaderSize];
                WriteShortString(header, WireNameOffset, record.CounterpartyName);
                BitConverter.TryWriteBytes(header.AsSpan(WireIdxOffset, 4), record.Idx);
                BitConverter.TryWriteBytes(header.AsSpan(WireCreditOffset, 4), record.Credit);
                header[WireItemCountOffset] = record.ItemCount;
                header[WireStateOffset] = record.ConsState;
                header[WireByte1AOffset] = record.Byte19;
                header[WireByte1BOffset] = record.Byte1A;
                BitConverter.TryWriteBytes(header.AsSpan(WireUserLvOffset, 2), record.UserLv);
                BitConverter.TryWriteBytes(header.AsSpan(WireTimeOffset, 8), record.Time);
                writer.Write(header);
                if (record.ItemPayload is { Length: > 0 }) writer.Write(record.ItemPayload);
            }
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// Delphi ShortString capped at 15 payload bytes, matching 0x4039E4 with CL = 0x0F: one
        /// length byte then the bytes, GBK, truncated on a byte boundary.
        /// </summary>
        private static void WriteShortString(byte[] target, int offset, string value)
        {
            value ??= string.Empty;
            var bytes = HUtil32.GbkEncoding.GetBytes(value);
            while (bytes.Length > RecordNameCapacity && value.Length > 0)
            {
                value = value[..^1];
                bytes = HUtil32.GbkEncoding.GetBytes(value);
            }
            target[offset] = (byte)bytes.Length;
            Buffer.BlockCopy(bytes, 0, target, offset + 1, bytes.Length);
        }
    }
}
