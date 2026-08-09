namespace GameSvr
{
    // Dormant model of the native HERO lifecycle WRITE ops (create + delete), reversed 1:1 from the
    // 战神 binaries. NOT wired into any live path; every method is a pure function that reproduces an
    // exact result-code ladder observed in the disassembly. The polymorphic sub-decisions (name
    // filters, duplicate lookups, capacity scans, persistence success) are abstracted as inputs, so
    // the model captures the ladder ORDER and CODES without re-implementing — or faking — the DBServer
    // hero-record persistence, which is an externally-blocked round-trip (see notes below).
    //
    // Images (Hex-Rays verified):
    //   M2Server_unpacked_fixed.exe  (base 0x00400000)   — the game server.
    //   DBServer_fixed3.exe                               — the data server.
    //
    // Evidence: D:\loym2\ENG-050 (create DB protocol), D:\loym2\staging\hero-delete-native-report.md,
    //   D:\loym2\ENG-062 (HaveValidHero), staging\ida_hero_053_m2_detail.txt (0x53 map),
    //   staging\ida_hero_162_m2_targets.txt (sub_6C9C00), staging\ida_hero_162_db_targets.txt
    //   (sub_59AD4C / sub_58B830), staging\ida_hero_delete_db_detail.txt (sub_58D5B0).
    //
    // ===================================================================================================
    // CREATE  (client CreateHero -> M2 pre-validate -> DBServer 0x162 -> DBServer 0x53 -> M2 client map)
    // ---------------------------------------------------------------------------------------------------
    //   [1] M2 local validate     sub_6C9C00 @0x006C9C00  -> EvaluateCreateLocal
    //         default -4 (heroType neither 1 nor 2). Degenerate heroType in {-2,-3} returns -4 WITHOUT
    //         emitting internal msg 0x2732/10034 (SM_BUILDHERO=773) — see CreateLocalSendsBuildHero.
    //         type 1: state bit0|bit2 -> -1; (uint)(code-1)>=6 -> -2; name GBK len not in [4,14] or
    //                 first char in {'+','-','/','\\'} -> -3; else send 0x162 type1, 0.
    //         type 2: HaveValidHero || state bit3 -> -1; (uint)(code-1)>=6 -> -2; else send 0x162, 0.
    //                 (type 2 does NOT validate the hero name at the M2 layer.)
    //   [2] DBServer create rule  sub_59AD4C @0x0059AD4C -> sub_58B830 @0x0058B830 -> EvaluateCreateDbRule
    //         -1 name filter/reserved/blacklist (sub_57F078 || sub_5C22C8, outer, in sub_59AD4C);
    //         -5 (byte)(code-1)>=6 or (byte)(heroType-1)>=2 (range);
    //         -2 global hero-name duplicate (sub_5A8CE8);
    //         -3 hero-name index duplicate (sub_49BAA8);
    //         -4 master capacity/same-type conflict (n2>=2 over the index chain: +62 IsDelete,
    //            +64 Consignation, +63 HeroType);
    //         -6 persistence failed (sub_58BFC8);  success -> the code itself (1..6).
    //         Job = (code-1)%3, Sex = (code-1)/3 (written at record +65/+66).
    //   [3] M2 0x53 client map     sub_6535C0 @0x006535C0 -> EvaluateCreateResponse
    //         master not online (found by +37 MasterName) -> no-op, no message, no bit.
    //         result>0 & heroType in {1,2} -> bts [player+0xB7D],(heroType-1) + "创建成功".
    //         result>0 & heroType not in {1,2} -> "英雄类型错误" (no bit).
    //         -4 "已经有英雄"; -3 "被其他英雄使用"; -2 "与其他玩家同名"; -1 "非法的字符";
    //         0 / -5 / -6 (and any other <=0) -> generic "创建失败,稍后再试".
    //
    // ===================================================================================================
    // DELETE  (@DelHero GM cmd id 0x51 -> M2 entry -> DBServer 0x163 -> DBServer 0x59 -> IGNORED by M2)
    // ---------------------------------------------------------------------------------------------------
    //   [1] M2 delete entry       sub_6BF5AC @0x006BF5AC -> EvaluateDeleteEntry
    //         gate = HaveValidHero(state) (sub_6D6894) && hero not spawned (player+0xBB0 == null).
    //         gate false -> silent no-op (no send, no message). gate true -> send 0x163 (empty HeroName)
    //         then UNCONDITIONALLY clear state bit0 (AND 0xFE) and bit1 (AND 0xFD) — i.e. &= 0xFC —
    //         even if the send failed. Never emits a client message.
    //   [2] DBServer delete rule  sub_58D5B0 @0x0058D5B0 -> EvaluateDeleteDbRule
    //         0 master container not found, or chain empty, or walk reached end with no candidate;
    //         2 container first-byte flag set (business name unrecovered);
    //         3 selected record already deleted (+62 != 0);
    //         1 marked +62 IsDelete=1 and queued to the save list (soft delete; hero_data untouched).
    //         Empty HeroName (the player entry) selects the first record with IsDelete==0 &&
    //         Consignation==0; a non-empty HeroName matches by name.
    //   [3] M2 0x59 response       sub_654140 @0x00654140 case 0x59 -> DEFAULT (ignored).
    //         M2 performs NO state restore/retry/message on the delete result. See DeleteResponseIgnored.
    //
    // ===================================================================================================
    // EXTERNALLY BLOCKED (documented, NOT modeled/faked):
    //   * The actual persistence leaves — create sub_58BFC8 (hero_index/hero_data INSERT) and delete's
    //     hero_index.IsDelete UPDATE — run inside DBServer against a live DB and cannot be exercised
    //     here. They are abstracted as the PersistSucceeded / SelectedRecord* inputs.
    //   * LOAD (0x160 select rule sub_58D16C) is a record-SELECTION algorithm (HeroKind/HeroSlot with a
    //     Job==255 special-hero sentinel), not a write ladder; the record blob it returns is a
    //     DBServer round-trip. SAVE (native quick-list blob write) is fire-and-forget with no result
    //     ladder. Both are read/round-trip concerns owned by DBServer, out of scope for a write ladder.
    //
    // The live C# M2 side of these ops lives in HeroDataService / NativeHeroDbFrameCodec; this class is
    // an independent, verifiable reference and does not call into it.

    /// <summary>What sub_6535C0 decides for the client after a DBServer 0x53 create response.</summary>
    public enum NativeHeroCreateClientOutcome
    {
        /// <summary>MasterName not online (v4 == 0): the handler returns without any message or bit.</summary>
        PlayerOffline,
        /// <summary>result &gt; 0 &amp;&amp; heroType in {1,2}: sets state bit (heroType-1) and reports success.</summary>
        Success,
        /// <summary>result &gt; 0 but heroType not in {1,2}: "你所提交的英雄类型错误。" (no bit set).</summary>
        TypeError,
        /// <summary>result == -1: "英雄的名字不能包含非法的字符。"</summary>
        IllegalName,
        /// <summary>result == -2: "英雄的名字不能与其他玩家同名。"</summary>
        DuplicateName,
        /// <summary>result == -3: "这个名字已经被其他英雄使用了。"</summary>
        NameInUse,
        /// <summary>result == -4: "您已经有英雄了。"</summary>
        AlreadyHaveHero,
        /// <summary>result == 0 or &lt;= -5: "您的英雄创建失败，稍后再试..."</summary>
        GenericFail,
    }

    /// <summary>Outcome of the M2 @DelHero entry gate (sub_6BF5AC).</summary>
    public enum NativeHeroDeleteEntryAction
    {
        /// <summary>Gate closed: no request sent, no bits changed, no message.</summary>
        Ignored,
        /// <summary>Gate open: send 0x163 (empty HeroName) then clear state bits 0 and 1.</summary>
        SendRequestAndClearBits,
    }

    /// <summary>Inputs to the M2-local CreateHero validation (sub_6C9C00).</summary>
    public sealed class NativeHeroCreateLocalContext
    {
        /// <summary>Requested hero type (1 or 2; anything else defaults to -4).</summary>
        public int HeroType { get; init; }
        /// <summary>Combination/job code; valid range is 1..6.</summary>
        public int Code { get; init; }
        /// <summary>The player's native hero state byte (player+0xB7D).</summary>
        public byte StateByte { get; init; }
        /// <summary>GBK byte length of the hero name (type 1 only; valid 4..14).</summary>
        public int NameGbkLength { get; init; }
        /// <summary>First name char is one of '+' '-' '/' '\\' (type 1 only).</summary>
        public bool FirstCharForbidden { get; init; }
    }

    /// <summary>Inputs to the DBServer create rule (sub_59AD4C -&gt; sub_58B830).</summary>
    public sealed class NativeHeroCreateDbContext
    {
        public int HeroType { get; init; }
        public int Code { get; init; }
        /// <summary>Outer name filter: sub_57F078 (length/forbidden char) || sub_5C22C8 (reserved/blacklist).</summary>
        public bool NameRejectedByFilter { get; init; }
        /// <summary>sub_5A8CE8: the hero name already exists globally.</summary>
        public bool GlobalNameDuplicate { get; init; }
        /// <summary>sub_49BAA8: the hero-name index already holds this name.</summary>
        public bool HeroIndexDuplicate { get; init; }
        /// <summary>Count of the master's index records with IsDelete==0 &amp;&amp; Consignation==0.</summary>
        public int ActiveNonConsignedCount { get; init; }
        /// <summary>An IsDelete==0 record already carries the requested HeroType (record+63).</summary>
        public bool SameHeroTypeActiveExists { get; init; }
        /// <summary>sub_58BFC8: the record persisted successfully (externally-blocked leaf).</summary>
        public bool PersistSucceeded { get; init; }
    }

    /// <summary>Inputs to the M2 0x53 create-response client map (sub_6535C0).</summary>
    public sealed class NativeHeroCreateResponseContext
    {
        /// <summary>Echoed DBServer result at response +4.</summary>
        public int Result { get; init; }
        /// <summary>Echoed HeroType at response +2.</summary>
        public int HeroType { get; init; }
        /// <summary>The MasterName at response +37 resolves to an online player (v4 != 0).</summary>
        public bool PlayerOnline { get; init; }
    }

    /// <summary>Inputs to the M2 @DelHero entry (sub_6BF5AC).</summary>
    public sealed class NativeHeroDeleteEntryContext
    {
        /// <summary>The player's native hero state byte (player+0xB7D).</summary>
        public byte StateByte { get; init; }
        /// <summary>A hero runtime object is currently spawned (player+0xBB0 != null).</summary>
        public bool HeroSpawned { get; init; }
    }

    /// <summary>Inputs to the DBServer delete rule (sub_58D5B0).</summary>
    public sealed class NativeHeroDeleteDbContext
    {
        /// <summary>The master's hero index container was found (sub_49BAA8, v6 != 0).</summary>
        public bool MasterContainerFound { get; init; }
        /// <summary>The container's first byte flag is set (*(byte*)v6 != 0).</summary>
        public bool ContainerFlagSet { get; init; }
        /// <summary>A candidate record was selected by the chain walk (empty name: first
        /// IsDelete==0 &amp;&amp; Consignation==0; non-empty name: match by name).</summary>
        public bool SelectedRecordFound { get; init; }
        /// <summary>The selected record is already deleted (record+62 != 0).</summary>
        public bool SelectedRecordAlreadyDeleted { get; init; }
    }

    public static class NativeHeroWriteTransaction
    {
        /// <summary>Sentinel: the path returned without emitting a message (silent).</summary>
        public const int NoResponse = int.MinValue;

        // DBServer opcodes (payload +0).
        public const ushort DbCreateRequestOpcode = 0x162;
        public const ushort DbDeleteRequestOpcode = 0x163;
        public const ushort DbCreateResponseOpcode = 0x53;
        public const ushort DbDeleteResponseOpcode = 0x59;

        /// <summary>M2 internal message the local create emits with its result (10034 == SM_BUILDHERO 773).</summary>
        public const int InternalBuildHeroMessage = 0x2732;

        /// <summary>Player object offset of the native hero state byte.</summary>
        public const int HeroStateByteOffset = 0xB7D;

        /// <summary>M2 discards the DBServer 0x59 delete response (sub_654140 case 0x59 -&gt; default).</summary>
        public const bool DeleteResponseIgnored = true;

        // ---- shared predicates -------------------------------------------------------------------

        /// <summary>sub_6D6894: (state &amp; 0x03) != 0. True even when the hero is owned but not spawned.</summary>
        public static bool HaveValidHero(byte state) => (state & 0x03) != 0;

        /// <summary>Record job written by the create rule: (code-1) % 3 (valid code only).</summary>
        public static int CreateJob(int code) => (code - 1) % 3;

        /// <summary>Record sex written by the create rule: (code-1) / 3 (valid code only).</summary>
        public static int CreateSex(int code) => (code - 1) / 3;

        // ---- [CREATE 1] M2 local validate  sub_6C9C00 --------------------------------------------

        /// <summary>
        /// False only for the degenerate heroType in {-2,-3}, where sub_6C9C00 returns -4 WITHOUT
        /// emitting the internal SM_BUILDHERO message. Every other heroType emits it with the result.
        /// </summary>
        public static bool CreateLocalSendsBuildHero(int heroType) =>
            !((uint)(-heroType - 2) < 2u);

        /// <summary>PAS/client-facing result of the M2-local create validation. -4/-1/-2/-3/0.</summary>
        public static int EvaluateCreateLocal(NativeHeroCreateLocalContext c)
        {
            byte state = c.StateByte;
            switch (c.HeroType)
            {
                case 1:
                    if ((state & 1) != 0 || (state & 4) != 0) return -1;
                    if ((uint)(c.Code - 1) >= 6u) return -2;
                    if ((uint)(c.NameGbkLength - 4) >= 0x0Bu || c.FirstCharForbidden) return -3;
                    return 0; // sends 0x162 with type 1
                case 2:
                    if (HaveValidHero(state) || (state & 8) != 0) return -1;
                    if ((uint)(c.Code - 1) >= 6u) return -2;
                    return 0; // sends 0x162 with type 2
                default:
                    return -4;
            }
        }

        // ---- [CREATE 2] DBServer create rule  sub_59AD4C -> sub_58B830 ----------------------------

        /// <summary>DBServer create result: -1/-5/-2/-3/-4/-6 or the code (1..6) on success.</summary>
        public static int EvaluateCreateDbRule(NativeHeroCreateDbContext c)
        {
            if (c.NameRejectedByFilter) return -1;                         // sub_59AD4C outer
            if ((byte)(c.Code - 1) >= 6 || (byte)(c.HeroType - 1) >= 2)    // sub_58B830 range
                return -5;
            if (c.GlobalNameDuplicate) return -2;                         // sub_5A8CE8
            if (c.HeroIndexDuplicate) return -3;                          // sub_49BAA8
            if (c.ActiveNonConsignedCount >= 2 || c.SameHeroTypeActiveExists)
                return -4;                                                // n2 >= 2
            return c.PersistSucceeded ? c.Code : -6;                      // sub_58BFC8
        }

        // ---- [CREATE 3] M2 0x53 client map  sub_6535C0 -------------------------------------------

        /// <summary>Maps a DBServer 0x53 create response to the client outcome sub_6535C0 produces.</summary>
        public static NativeHeroCreateClientOutcome EvaluateCreateResponse(
            NativeHeroCreateResponseContext c)
        {
            if (!c.PlayerOnline)
                return NativeHeroCreateClientOutcome.PlayerOffline;
            if (c.Result > 0)
            {
                return (ushort)(c.HeroType - 1) >= 2u
                    ? NativeHeroCreateClientOutcome.TypeError
                    : NativeHeroCreateClientOutcome.Success;
            }
            return c.Result switch
            {
                -4 => NativeHeroCreateClientOutcome.AlreadyHaveHero,
                -3 => NativeHeroCreateClientOutcome.NameInUse,
                -2 => NativeHeroCreateClientOutcome.DuplicateName,
                -1 => NativeHeroCreateClientOutcome.IllegalName,
                _ => NativeHeroCreateClientOutcome.GenericFail, // 0, -5, -6, ...
            };
        }

        /// <summary>Only the Success outcome sets state bit (heroType-1) via bts [player+0xB7D].</summary>
        public static bool CreateOutcomeSetsStateBit(NativeHeroCreateClientOutcome outcome) =>
            outcome == NativeHeroCreateClientOutcome.Success;

        // ---- [DELETE 1] M2 delete entry  sub_6BF5AC ----------------------------------------------

        /// <summary>Whether @DelHero sends the 0x163 request and clears the state bits.</summary>
        public static NativeHeroDeleteEntryAction EvaluateDeleteEntry(NativeHeroDeleteEntryContext c) =>
            HaveValidHero(c.StateByte) && !c.HeroSpawned
                ? NativeHeroDeleteEntryAction.SendRequestAndClearBits
                : NativeHeroDeleteEntryAction.Ignored;

        /// <summary>Bit clear the delete entry applies after sending: state &amp;= 0xFC (bits 0 and 1).</summary>
        public static byte ApplyDeleteBitClear(byte state) => (byte)(state & 0xFC);

        // ---- [DELETE 2] DBServer delete rule  sub_58D5B0 -----------------------------------------

        /// <summary>DBServer delete result: 0 not-found / 1 marked+queued / 2 container-flag / 3 already-deleted.</summary>
        public static int EvaluateDeleteDbRule(NativeHeroDeleteDbContext c)
        {
            if (!c.MasterContainerFound) return 0;
            if (c.ContainerFlagSet) return 2;
            if (!c.SelectedRecordFound) return 0;
            if (c.SelectedRecordAlreadyDeleted) return 3;
            return 1; // set +62 IsDelete=1 and queue the record for save
        }
    }
}
