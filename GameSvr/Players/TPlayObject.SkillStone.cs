using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    // ====================================================================================================
    // SkillStone subsystem — faithful port of the two CM handlers cm-1 previously blanket-dropped:
    //   CM 1061  技能石复制 / 火云石"锻炼" (skill-stone copy)   leaf 0x6D9579 -> worker sub_6CBDD4 @0x6CBDD4
    //   CM 1080  强化 / 标准物品表提交 (std-item / strengthen)  leaf 0x6D95D6 -> worker sub_6CF49C @0x6CF49C
    //
    // (unpacked M2Server.exe, image base 0x00400000; capstone x86-32. Flat image mapping verified against
    // the shared no-op sink bytes at 0x6DBC2C = 33 C0 5A 59 59 64 89 10 E9 D5 00 00 00.)
    //
    // TABLE EXPORTABILITY JUDGEMENT (the铁律 question — are the three tables image constants or runtime?):
    // All three "off_" globals used by these workers are DOUBLE-INDIRECT singleton handles
    // (`mov eax,[abs] / mov eax,[eax]`), i.e. `abs` holds the address of the object-pointer slot, and the
    // slot holds a heap object created at unit-init. The object-pointer slots are ZERO in the static image,
    // which is only possible for a runtime-allocated (Create'd) singleton — a heap pointer can never be a
    // compile-time constant:
    //   [0x7D5F20] = 0x7DC6CC ,  [0x7DC6CC] = 0x00000000   (CM 1061 skill-stone-copy manager)
    //   [0x7D6630] = 0x7DC1A8 ,  [0x7DC1A8] = 0x00000000   (strengthen/craft manager)
    //   [0x7D5D6C] = 0x7DCB88 ,  [0x7DCB88] = 0x00000000   (std-item + PowerupItem name->wIndex resolver)
    // => NOT EXPORTABLE. The copy recipe, the name->wIndex resolution and the strengthen result are all
    // functions of runtime table state, so every terminal action that depends on them is fail-closed here
    // (registered with VA + reason in <see cref="SkillStoneFailClosed"/>). What IS a compile-time invariant
    // — the body-length gates, the fixed record layouts and the bag-match ladders — is captured 1:1 in the
    // side-effect-free evaluators below (mirroring <c>NativeItemMerge</c>: the model does not re-derive item
    // internals live, it takes them as a precondition snapshot).
    //
    // NATIVE ITEM/PLAYER OFFSET -> C# MAPPING (established by NativeItemMerge + TUserItem.cs remarks):
    //   item+0x18  ClientItemID   -> TUserItem.ClientItemID (assigned via EnsureClientItemId)
    //   item+0x1c  StdItem def ptr -> UserEngine.GetStdItem(item.wIndex)   (null ptr  <=> no std def)
    //   item+0x20  record start    -> MakeIndex
    //   item+0x24  wIndex (word)   -> TUserItem.wIndex          (StdItem index)
    //   item+0x26  Dura            -> TUserItem.Dura
    //   item+0x28  DuraMax         -> TUserItem.DuraMax
    //   StdItem+4  Name            -> GoodItem.Name (UserEngine.GetStdItemName(item.wIndex))
    //   self+0x508 bag TList       -> m_ItemList
    // ====================================================================================================

    /// <summary>Result of the side-effect-free CM 1061 evaluator.</summary>
    public enum NativeSkillStoneCopyResult
    {
        /// <summary>nBodyLen &lt; 0x3C — worker exits at 0x6CBE11 `jl 0x6CBFD0`; native is SILENT.</summary>
        BodyTooShort,
        /// <summary>
        /// Fewer than 3 bag items matched the 3 record fields (flag byte [ebp-5] stays 0). The manager is
        /// never called, result [ebp-0x14] stays 0, and native answers the FAIL SysMsg
        /// (0x6CBFA6 `mov cx,0x38FF`, string @0x6CC010 "这次无法控制火云石的力量，锻炼失败了"). DERIVABLE.
        /// </summary>
        ShortOfMatches,
        /// <summary>
        /// Exactly 3 items matched (flag set at 0x6CBEC5). Native calls the runtime skill-stone-copy
        /// manager [[0x7D5F20]].sub_6A09F4 (0x6CBEFF), which decides SUCCESS (0x6CBFBC `mov cx,0xFFDB`,
        /// "太好了，锻炼成功了") vs FAIL (0x38FF) and mutates the bag. FAIL-CLOSED (runtime table).
        /// </summary>
        ReachesRuntimeManager,
    }

    /// <summary>
    /// One record field parsed out of the CM 1061 body. The body is 3 of these, stride 0x14 (20) bytes:
    /// <c>int ClientItemId @+0</c> then a <c>ShortString name @+4</c> (16 bytes, GBK). Native `Move`s the
    /// 0x3C-byte body verbatim (0x6CBE44 call sub_403260, ecx=0x3C) into a local before scanning.
    /// </summary>
    public sealed class NativeSkillStoneRecordField
    {
        /// <summary>record[i]+0 — matched against item+0x18 (ClientItemID).</summary>
        public int ClientItemId { get; init; }
        /// <summary>record[i]+4 — ShortString, matched against the item's StdItem.Name.</summary>
        public string Name { get; init; }
    }

    /// <summary>Precondition snapshot of one bag item for the CM 1061 match ladder.</summary>
    public sealed class NativeSkillStoneBagItem
    {
        /// <summary>item+0x18 (ClientItemID).</summary>
        public int ClientItemId { get; init; }
        /// <summary>item+0x1c != 0 — the item has a resolvable StdItem def.</summary>
        public bool HasStdDef { get; init; }
        /// <summary>StdItem.Name (StdItem+4); only consulted when <see cref="HasStdDef"/>.</summary>
        public string StdName { get; init; }
    }

    /// <summary>Side-effect-free input for one CM 1061 request.</summary>
    public sealed class NativeSkillStoneCopyContext
    {
        /// <summary>Wire body length = nBodyLen (payload.Length). Gate: &gt;= 0x3C.</summary>
        public int BodyLen { get; init; }
        /// <summary>The 3 parsed record fields (only valid when BodyLen &gt;= 0x3C).</summary>
        public IReadOnlyList<NativeSkillStoneRecordField> Fields { get; init; }
        /// <summary>Bag items in native scan order (m_ItemList low index -&gt; high, sub_424D4C).</summary>
        public IReadOnlyList<NativeSkillStoneBagItem> Bag { get; init; }
    }

    public sealed class NativeSkillStoneCopyOutcome
    {
        public NativeSkillStoneCopyResult Result { get; init; }
        /// <summary>Number of bag items collected (0..3, capped at 3).</summary>
        public int MatchedCount { get; init; }
        /// <summary>
        /// SysMsg wIdent native would send when derivable: 0x38FF on <see cref="NativeSkillStoneCopyResult.ShortOfMatches"/>;
        /// 0 when the reply is undecidable (fail-closed) or silent.
        /// </summary>
        public int ReplyWIdent { get; init; }
        /// <summary>True when the terminal action depends on the runtime manager and is withheld.</summary>
        public bool FailClosed { get; init; }
    }

    /// <summary>
    /// Side-effect-free 1:1 model of worker sub_6CBDD4 (CM 1061). Reproduces the exact match ladder:
    /// each bag item scans record fields 0..2; on the FIRST field whose id == item.ClientItemID it is
    /// "consumed" — stored only if the item HAS a std def AND its StdItem.Name equals that field's name,
    /// otherwise skipped (native `je/jne 0x6CBED1` go straight to the next ITEM, never the next field). An
    /// id-MISMATCH is the only thing that advances to the next field (0x6CBECB `add ebx,0x14`). A field can
    /// therefore match several items (ebx is re-seeded per item at 0x6CBE81). The scan stops once 3 are
    /// collected (0x6CBEC5 sets the flag). &lt;3 collected =&gt; SM 0x38FF; ==3 =&gt; runtime manager.
    /// </summary>
    public static class NativeSkillStoneCopy
    {
        public const int Ident = 1061;                    // 0x425
        public const uint LeafVa = 0x006D9579;
        public const uint WorkerVa = 0x006CBDD4;
        public const int MinBodyLen = 0x3C;               // 0x6CBE0D cmp [ebp+8],0x3C / jl 0x6CBFD0
        public const int FieldCount = 3;                  // inner loop edi=3
        public const int FieldStride = 0x14;              // 0x6CBECB add ebx,0x14
        public const int RequiredMatches = 3;             // 0x6CBEBF cmp [ebp-0x10],2 / jle => need >2

        // native offsets (documentation)
        public const int ItemClientIdOffset = 0x18;       // 0x6CBE84 mov eax,[esi+0x18]
        public const int ItemStdDefOffset = 0x1C;         // 0x6CBE8B cmp [esi+0x1c],0
        public const int SelfBagListOffset = 0x508;       // 0x6CBE4C mov eax,[eax+0x508]

        // runtime manager (NOT exportable) + its SysMsg replies (vtable+0xD4)
        public const uint CopyManagerHandleVa = 0x007D5F20;   // [0x7D5F20] -> 0x7DC6CC -> 0 (runtime)
        public const uint CopyManagerMethodVa = 0x006A09F4;   // 0x6CBEFF call sub_6A09F4
        public const int FailWIdent = 0x38FF;             // 0x6CBFA6 mov cx,0x38FF ; string @0x6CC010
        public const int SuccessWIdent = 0xFFDB;          // 0x6CBFBC mov cx,0xFFDB ; string @0x6CC040
        public const uint FailStringVa = 0x006CC010;      // "这次无法控制火云石的力量，锻炼失败了"
        public const uint SuccessStringVa = 0x006CC040;   // "太好了，锻炼成功了"

        public static NativeSkillStoneCopyOutcome Evaluate(NativeSkillStoneCopyContext context)
        {
            if (context == null || (context.BodyLen & 0xFFFF) < MinBodyLen)
            {
                return new NativeSkillStoneCopyOutcome
                {
                    Result = NativeSkillStoneCopyResult.BodyTooShort,
                    MatchedCount = 0,
                    ReplyWIdent = 0,
                    FailClosed = false,
                };
            }

            var fields = context.Fields ?? System.Array.Empty<NativeSkillStoneRecordField>();
            var bag = context.Bag ?? System.Array.Empty<NativeSkillStoneBagItem>();

            int matched = 0;
            foreach (var item in bag)
            {
                if (item == null) continue;

                // inner loop: fields 0..FieldCount-1; only an id-MISMATCH advances to the next field.
                for (int f = 0; f < FieldCount && f < fields.Count; f++)
                {
                    var field = fields[f];
                    if (field == null || item.ClientItemId != field.ClientItemId)
                        continue; // 0x6CBE89 jne 0x6CBECB -> next field

                    // id match: the item is consumed here regardless of the std/name outcome.
                    if (item.HasStdDef &&
                        string.Equals(item.StdName, field.Name, System.StringComparison.Ordinal))
                    {
                        matched++; // 0x6CBEB5 store into matched[]
                    }
                    // 0x6CBE8F (std null) / 0x6CBEB3 (name diff) both jump to 0x6CBED1 = next ITEM.
                    break;
                }

                if (matched >= RequiredMatches) // flag set at 0x6CBEC5 -> break the outer scan
                    break;
            }

            if (matched >= RequiredMatches)
            {
                // ==3 collected: native runs [[0x7D5F20]].sub_6A09F4 and lets it decide the reply.
                return new NativeSkillStoneCopyOutcome
                {
                    Result = NativeSkillStoneCopyResult.ReachesRuntimeManager,
                    MatchedCount = RequiredMatches,
                    ReplyWIdent = 0,   // undecidable — manager state
                    FailClosed = true,
                };
            }

            // <3 collected: manager skipped, result stays 0 -> deterministic FAIL SysMsg 0x38FF.
            return new NativeSkillStoneCopyOutcome
            {
                Result = NativeSkillStoneCopyResult.ShortOfMatches,
                MatchedCount = matched,
                ReplyWIdent = FailWIdent,
                FailClosed = false,
            };
        }
    }

    // ----------------------------------------------------------------------------------------------------

    /// <summary>Result of the side-effect-free CM 1080 evaluator.</summary>
    public enum NativeStrengthenTableResult
    {
        /// <summary>nBodyLen &lt; 0x28 — worker exits at 0x6CF4D1 `jl 0x6CF5D6`; native is SILENT.</summary>
        BodyTooShort,
        /// <summary>
        /// One of the four player gate bytes is non-zero (0x6CF4D7..0x6CF505 test self+0xD48/0xD5D/0xF29/
        /// 0xF14). Native falls to the exit and answers SM 0x3B7 with result 0. These four fields are NOT
        /// modelled in this C# server, so the gate cannot be evaluated live — supplied as a precondition.
        /// </summary>
        BlockedByPlayerFlag,
        /// <summary>
        /// Gates passed. Native resolves both names to a wIndex through the runtime std-item table
        /// [[0x7D5D6C]].sub_74C1E0, matches two bag items and runs the runtime strengthen manager
        /// [[0x7D6630]].sub_600F6C, then answers SM 0x3B7 with its result (result==1 =&gt; silent).
        /// FAIL-CLOSED: name resolution AND strengthen result are runtime-table functions.
        /// </summary>
        ReachesRuntimeManager,
    }

    /// <summary>
    /// The CM 1080 body (0x28 = 40 bytes, Move'd at 0x6CF51F): two {int ClientItemId; ShortString name}
    /// pairs, the second at record+0x14. id1 @+0, name1 @+4, id2 @+0x14, name2 @+0x18.
    /// </summary>
    public sealed class NativeStrengthenTableContext
    {
        /// <summary>Wire body length = nBodyLen. Gate: &gt;= 0x28.</summary>
        public int BodyLen { get; init; }
        /// <summary>
        /// True only when all four gate bytes self+0xD48/0xD5D/0xF29/0xF14 are 0. Precondition (fields
        /// absent from this server).
        /// </summary>
        public bool PlayerFlagsAllClear { get; init; }
        public int ClientItemId1 { get; init; }   // record+0
        public string Name1 { get; init; }        // record+4  -> [[0x7D5D6C]] wIndex1
        public int ClientItemId2 { get; init; }   // record+0x14
        public string Name2 { get; init; }        // record+0x18 -> [[0x7D5D6C]] wIndex2
    }

    public sealed class NativeStrengthenTableOutcome
    {
        public NativeStrengthenTableResult Result { get; init; }
        /// <summary>SM_ wIdent native answers with (0x3B7) when a reply is sent; 0 when silent.</summary>
        public int ReplyWIdent { get; init; }
        public bool FailClosed { get; init; }
    }

    /// <summary>
    /// Side-effect-free 1:1 model of worker sub_6CF49C (CM 1080). Only the two provable gates are decided
    /// here (body length, and the supplied player-flag precondition); everything past them — the
    /// name-&gt;wIndex resolution [[0x7D5D6C]].sub_74C1E0, the {ClientItemID +0x18, wIndex +0x24} bag match
    /// and the strengthen manager [[0x7D6630]].sub_600F6C (whose result drives SM 0x3B7, result==1 silent) —
    /// is a runtime-table function and is reported as fail-closed.
    /// </summary>
    public static class NativeStrengthenTableOp
    {
        public const int Ident = 1080;                    // 0x438
        public const uint LeafVa = 0x006D95D6;
        public const uint WorkerVa = 0x006CF49C;
        public const int MinBodyLen = 0x28;               // 0x6CF4CE cmp edi,0x28 / jl 0x6CF5D6
        public const int RecordId2Offset = 0x14;          // second pair at record+0x14 (cmp [ebp-0x28])

        // player gate bytes (UNMODELLED in this server)
        public const int Flag0Offset = 0x0D48;            // 0x6CF4D7
        public const int Flag1Offset = 0x0D5D;            // 0x6CF4E4
        public const int Flag2Offset = 0x0F29;            // 0x6CF4F1
        public const int Flag3Offset = 0x0F14;            // 0x6CF4FE

        // item offsets used by the bag match
        public const int ItemClientIdOffset = 0x18;       // 0x6CF581 cmp [eax+0x18],record.id
        public const int ItemWIndexOffset = 0x24;         // 0x6CF589 movzx edx,word [eax+0x24]

        // runtime tables (NOT exportable) + reply
        public const uint NameResolverHandleVa = 0x007D5D6C;  // [0x7D5D6C] -> 0x7DCB88 -> 0 (runtime)
        public const uint NameResolverMethodVa = 0x0074C1E0;  // 0x6CF539/0x6CF555 call sub_74C1E0
        public const uint StrengthenManagerHandleVa = 0x007D6630; // [0x7D6630] -> 0x7DC1A8 -> 0 (runtime)
        public const uint StrengthenManagerMethodVa = 0x00600F6C; // 0x6CF5C6 call sub_600F6C
        public const int ReplyWIdentValue = 0x3B7;        // 0x6CF5E7 mov dx,0x3B7 ; call [self+0x250]
        public const int SilentResult = 1;                // 0x6CF5D6 cmp [ebp-0xc],1 / je exit

        public static NativeStrengthenTableOutcome Evaluate(NativeStrengthenTableContext context)
        {
            if (context == null || (context.BodyLen & 0xFFFF) < MinBodyLen)
            {
                return new NativeStrengthenTableOutcome
                {
                    Result = NativeStrengthenTableResult.BodyTooShort,
                    ReplyWIdent = 0,
                    FailClosed = false,
                };
            }

            if (!context.PlayerFlagsAllClear)
            {
                // a set flag -> native never resolves names; result stays 0 -> SM 0x3B7 with 0.
                return new NativeStrengthenTableOutcome
                {
                    Result = NativeStrengthenTableResult.BlockedByPlayerFlag,
                    ReplyWIdent = ReplyWIdentValue,
                    FailClosed = false,
                };
            }

            // gates passed: name resolution + strengthen result are runtime-table functions.
            return new NativeStrengthenTableOutcome
            {
                Result = NativeStrengthenTableResult.ReachesRuntimeManager,
                ReplyWIdent = 0,   // undecidable — carries sub_600F6C's result
                FailClosed = true,
            };
        }
    }

    // ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Self-contained fail-closed registry for the SkillStone subsystem. Deliberately NOT reusing
    /// <c>NativeCmQ1FailClosed</c>: that table has an entry for 1061 but NONE for 1080, so a Drop(1080)
    /// there would throw. This records VA + reason once per ident per process and drops the packet, exactly
    /// as 战神 does when the reply would otherwise be invented from runtime table state.
    /// </summary>
    internal static class SkillStoneFailClosed
    {
        internal readonly struct Entry
        {
            public Entry(int ident, uint leafVa, uint workerVa, string subsystem, string blocker)
            {
                Ident = ident; LeafVa = leafVa; WorkerVa = workerVa; Subsystem = subsystem; Blocker = blocker;
            }

            public int Ident { get; }
            public uint LeafVa { get; }
            public uint WorkerVa { get; }
            public string Subsystem { get; }
            public string Blocker { get; }
        }

        private static readonly Dictionary<int, Entry> Entries = new()
        {
            [NativeSkillStoneCopy.Ident] = new Entry(
                NativeSkillStoneCopy.Ident, NativeSkillStoneCopy.LeafVa, NativeSkillStoneCopy.WorkerVa,
                "技能石复制(火云石锻炼)",
                "body>=0x3C 且 3 件背包物品(ClientItemID+0x18 / StdItem.Name)全部命中 record 三字段时，0x6CBEFF " +
                "调运行期技能石复制管理器 [[0x7D5F20]].sub_6A09F4 复制并回 SM 0xFFDB/0x38FF；" +
                "管理器对象槽 [0x7DC6CC]=0(镜像内运行期创建，不可导出)，复制配方与成败不可推导"),
            [NativeStrengthenTableOp.Ident] = new Entry(
                NativeStrengthenTableOp.Ident, NativeStrengthenTableOp.LeafVa, NativeStrengthenTableOp.WorkerVa,
                "强化/标准物品表提交",
                "body>=0x28 且四个门字节 self+0xD48/0xD5D/0xF29/0xF14 全 0 后，经 [[0x7D5D6C]].sub_74C1E0 " +
                "把两名称解析为 wIndex，匹配 {ClientItemID+0x18, wIndex+0x24} 两件物品并调运行期强化管理器 " +
                "[[0x7D6630]].sub_600F6C，回 SM 0x3B7(result)/result==1 静默；对象槽 [0x7DCB88]=0 与 " +
                "[0x7DC1A8]=0(均运行期创建，不可导出) + 四门字段本服未建模，回码不可推导"),
        };

        private static readonly HashSet<int> Reported = new HashSet<int>();
        private static readonly object Gate = new object();

        internal static void Drop(int ident, string charName)
        {
            if (!Entries.TryGetValue(ident, out var e))
            {
                return; // never throw on an unexpected ident — the packet is simply dropped.
            }

            lock (Gate)
            {
                if (!Reported.Add(ident)) return;
            }

            M2Share.MainOutMessage(
                $"[CM未移植/SkillStone] CM {e.Ident} ({e.Subsystem}) 已丢弃; " +
                $"leaf=0x{e.LeafVa:X6} worker=0x{e.WorkerVa:X6}; " +
                $"角色={(string.IsNullOrEmpty(charName) ? "<unknown>" : charName)}; " +
                $"缺口={e.Blocker}");
        }
    }

    // ----------------------------------------------------------------------------------------------------

    public partial class TPlayObject
    {
        // ================================================================================================
        // INTEGRATOR HOOKUP (do NOT edit the Operate() switch or the cm-1 Q1 file yourself):
        // this hook belongs to the CM Q1 segment and MUST run BEFORE TryHandleNativeCmQ1 so the faithful
        // 1061/1080 supersede cm-1's blanket-drop arms. In TPlayObject.Message.cs Operate() default arm:
        //
        //     if (!TryHandleInlayCm(ProcessMsg)
        //         && !TryHandleQiankunCm(ProcessMsg)
        //         && !TryHandleNativeSocialProtocol(ProcessMsg)
        //         && !TryHandleNativeCmTailProtocol(ProcessMsg)
        //         && !TryHandleSkillStoneCm(ProcessMsg)     // <-- insert this line, before Q1
        //         && !TryHandleNativeCmQ1(ProcessMsg)
        //         && !TryHandleNativeCmQ2(ProcessMsg)
        //         && !TryHandleNativeCmQ3(ProcessMsg))
        //     {
        //         result = base.Operate(ProcessMsg);
        //     }
        // ================================================================================================
        private bool TryHandleSkillStoneCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_1061:
                    HandleNativeSkillStoneCopy(processMessage.nBodyLen);
                    return true;
                case Grobal2.CM_1080:
                    HandleNativeStrengthenTable(processMessage.nBodyLen);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 1061 — worker sub_6CBDD4. The empty-body silence (leaf 0x6D9579 `test si,si / jbe 0x6DBC2C`)
        /// is reproduced upstream by NativeClientBodyLengthGate[1061]. Here we reproduce the worker's own
        /// short-body silence (0x6CBE0D `cmp [ebp+8],0x3C / jl 0x6CBFD0`) — native does nothing and sends
        /// nothing for a body under 0x3C bytes. For a full body the terminal action copies a skill stone
        /// through the runtime manager [[0x7D5F20]] (object slot [0x7DC6CC]=0 in the image), so the reply
        /// (SM 0xFFDB success / 0x38FF fail) is undecidable and the packet is fail-closed. See
        /// <see cref="NativeSkillStoneCopy"/> for the full provable ladder incl. the &lt;3-match SM 0x38FF.
        /// </summary>
        private void HandleNativeSkillStoneCopy(int nBodyLen)
        {
            // 0x6CBE0D `cmp [ebp+8],0x3C / jl 0x6CBFD0` — native silence for a short body.
            if ((nBodyLen & 0xFFFF) < NativeSkillStoneCopy.MinBodyLen)
            {
                return;
            }

            SkillStoneFailClosed.Drop(NativeSkillStoneCopy.Ident, m_sCharName);
        }

        /// <summary>
        /// CM 1080 — worker sub_6CF49C. Empty-body silence handled upstream by
        /// NativeClientBodyLengthGate[1080]; the worker's short-body silence (0x6CF4CE `cmp edi,0x28 / jl
        /// 0x6CF5D6`) is reproduced here. Past that gate the worker needs four unmodelled player-flag bytes
        /// (self+0xD48/0xD5D/0xF29/0xF14), the runtime name-&gt;wIndex resolver [[0x7D5D6C]] (slot
        /// [0x7DCB88]=0) and the runtime strengthen manager [[0x7D6630]] (slot [0x7DC1A8]=0) to produce
        /// SM 0x3B7, so the terminal action is fail-closed. See <see cref="NativeStrengthenTableOp"/>.
        /// </summary>
        private void HandleNativeStrengthenTable(int nBodyLen)
        {
            // 0x6CF4CE `cmp edi,0x28 / jl 0x6CF5D6` — native silence for a short body.
            if ((nBodyLen & 0xFFFF) < NativeStrengthenTableOp.MinBodyLen)
            {
                return;
            }

            SkillStoneFailClosed.Drop(NativeStrengthenTableOp.Ident, m_sCharName);
        }
    }
}
