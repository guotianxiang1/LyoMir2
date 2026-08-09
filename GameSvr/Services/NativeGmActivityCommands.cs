namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant model of the ACTIVITY / TITLE / RANK (活动 / 封号称号 / 排行) GM command family, reversed
    // 1:1 from the original Delphi M2Server ("战神" / God-of-War fork). NOT wired into the live command
    // table — the live handlers stay in GameSvr/Command/Commands/*Command.cs. This type only *describes*
    // the exact original contract so an AuditTools check can lock it and a future port can reproduce it
    // precisely instead of guessing.
    //
    // This is the GM "@"-command family 07 of gm_full_inventory_20260731.md: 40 commands — 39 modeled
    // here (14 with a dedicated case that does real work, 2 with a dedicated case that only replies a
    // fixed string (mutates nothing), and 23 registered no-ops that fall through to the default sink);
    // the 40th, SetActScore (idx 264, a default no-op), is already modeled in the item family model, so
    // it is cross-referenced there (not re-modeled here) to avoid a duplicate registration.
    //
    // NOTE — two inventory "IMPL" tags are corrected here from the decompiled bodies:
    //   * GetGuildMember (355) — inventory addr 0x00627FBC is a distinct jump-table shim, but Hex-Rays
    //     folds cases 265/355/404/453/458 into ONE body that only sends "Invalid" (0x38FF). It does no
    //     work -> modelled as StubbedFixedReply, not a real impl. (SetGuildLord/GuildForbid/selfAddGuild/
    //     ReNameGuild — the other four in that fold — belong to family 04 / NativeGmGuildCastleCommands.)
    //   * addGuildMem (444) — its own case @0x00628AE1 sends only "Can not insert directly" (0x38FF) and
    //     returns; no guild insert happens -> StubbedFixedReply, not a real impl.
    // So this family's real-impl count is 14 (not the inventory's 16).
    //
    // Evidence (IDA/Hex-Rays over unpacked M2Server = m2full.i64, image base 0x00400000; dumps in
    // D:/loym2/staging/update_clothes_4637_ida_work/: disp_decomp.txt, all_strings.txt). Every case body
    // below is present inline in disp_decomp.txt (the sub_622820 dispatcher); nothing was deferred at the
    // case-body level. The deeper CORE subs each case forwards to (sub_XXXXXX) are NOT in the dumps
    // (handler_out.txt holds only repeated copies of sub_622820) and are marked CoreBodyDeferred=true:
    // the in-dispatcher contract here is authoritative, the deeper core effect is deferred, not fabricated.
    //
    //   GM command dispatch is the SINGLE switch sub_622820 @0x00622820 (shared with the whole @-family).
    //     * @0x0062284D  mov [ebp+var_D], 1     ; "handled" byte set OPTIMISTICALLY to 1 before parsing.
    //     * esi = sub_621F28(player, name, callerPerm, &reqPerm)  ; index lookup @0x00621F28, returns
    //         record[+0x18] (dispatchIndex) iff callerPerm >= record[+0x1C] (requiredPerm), else 0.
    //     * @0x00622B0F  cmp esi,0x2EE ; ja def_622B15   (index > 750 -> default)
    //     * @0x00622B15  jmp jpt_622B15[esi*4]           (table @0x00622B1C, 752 slots)
    //     * every case in this family ends with `goto LABEL_1055` (the shared epilogue @0x0062B64C-area):
    //         SEH teardown + Delphi string cleanup; the optimistic handled byte (=1) survives.
    //
    //   TERMINAL SHAPES in this family (all 24 no-ops are the DEFAULT sink; none use the empty-case sink):
    //     (A) ImplementedCase  — dedicated body does real work (forwards a core and/or writes inline
    //         state and/or sends a SysMsg), then `goto LABEL_1055`. handled byte stays 1.
    //     (B) StubbedFixedReply — dedicated body only SysMsgs a fixed literal (0x38FF), no state change,
    //         then `goto LABEL_1055`. handled byte stays 1. (GetGuildMember, addGuildMem.)
    //     (E) DefaultNoOp — the index has NO case; it falls to def_622B15 @0x0062B648 which does
    //         `mov [ebp+var_D],0` (clears the handled byte to 0 / "not handled") then the epilogue. No
    //         effect, no message. All 24 no-ops here are this shape.
    //   (B) and (E) are both faithful NO-OPs on game state; (B) sends one fixed message, (E) is silent.
    //   The observable "handled" byte is 0 ONLY for (E); it is 1 for (A) and (B).
    //
    //   SysMsg is the virtual call `(*(*player + 0xD4))(LOWORD colour, text)` — vtable slot +0xD4 (212).
    //   Colour words seen in this family (LOWORD of the immediate, stored here as the unsigned hex):
    //     0x38FF (14591)  — PushSingleTask, SignInAct usage, the two StubbedFixedReply cases.
    //     0xFFDB (65499, i.e. the -37 immediate) — every other message in the family (AddNWPresent/Accept,
    //                       SignInAct open/close, PushActIdent usage, SimpleActCtrl, AddVote, GMActCtrl,
    //                       setGoldActLv).
    //
    //   C# PORT DIVERGENCES observed (live GameSvr/Command/Commands/*Command.cs vs. this original):
    //     * UNLIKE family 04 (guild/castle) there is NO systematic perm drift here — every live command
    //       declares the SAME perm as the original record +0x1C (AddVote 5, GMActCtrl 4, SetGoldActLv 5,
    //       SignInAct 4, SetAchieve 5, PayScore 4, ShowPayScore 4, ReloadGoddessConfig 4, ReloadSnakeConf 4,
    //       ReloadDailyActiveCfg 4). CSharpLivePermission is recorded for the verified live commands.
    //     * AddVote(331)/GMActCtrl(345)/setGoldActLv(496) — original does real work; live is a fail-closed
    //       NativeCommandFailure.Report stub -> live UNDER-implements (safe direction).
    //     * SignInAct(263) — original opens/closes the sign-in lottery + messages; live SignInActCommand
    //       is a FAITHFUL port (OpenActivity/CloseActivity + the same open/close/usage messages). Neither
    //       over nor under.
    //     * SETACHIEVE(543)/ReloadGoddessConfig(438)/ReloadSnakeConf(512)/ReloadDailyActiveCfg(561) —
    //       original is a silent DefaultNoOp; live sends a NativeCommandFailure.Report notice -> live
    //       OVER-sends a message the binary never sends.
    // ------------------------------------------------------------------------------------------------

    public enum GmActivityCommand
    {
        // ---- real implemented cases (14) ----
        Givetitle,
        Quxiaotitle,
        PushSingleTask,
        UpdateOrder,
        AddNWPresent,
        AddNWAccept,
        GetS,
        SetS,
        SignInAct,
        PushActIdent,
        SimpleActCtrl,
        AddVote,
        GMActCtrl,
        SetGoldActLv,
        // ---- fixed-reply stubs (2) ----
        GetGuildMember,
        AddGuildMem,
        // ---- registered no-ops (23, default sink; SetActScore idx264 covered by the item model) ----
        SetTitle,
        DelTitle,
        QueryTitle,
        CurTitle,
        DeliverTitle,
        DrawTitle,
        OpenTitle,
        TitleDetail,
        LoadTitle,
        PayScore,
        ExecTitleCmd,
        QueryZFF,
        ShowPayScore,
        ReloadGoddessConfig,
        GoddessVote,
        ReloadSnakeConf,
        ViewSnakeCost,
        SetAchieve,
        Achievement,
        DaAddTimes,
        DaClear,
        SetActiveValue,
        ReloadDailyActiveCfg,
    }

    /// <summary>Terminal shape of the jump-table slot / case body for one GM command.</summary>
    public enum GmActivityHandlerKind
    {
        /// <summary>Dedicated case does real work (forwards a core, writes inline state, and/or SysMsg). handled=1.</summary>
        ImplementedCase,
        /// <summary>Dedicated case only SysMsgs a fixed literal (0x38FF); no state change. handled=1.</summary>
        StubbedFixedReply,
        /// <summary>No case; falls to def_622B15 @0x0062B648: clears handled byte to 0, no effect, no msg.</summary>
        DefaultNoOp,
    }

    /// <summary>Static command-table facts for one GM command (record name / +0x18 / +0x1C / jump target).</summary>
    public sealed class GmActivityCommandInfo
    {
        public GmActivityCommand Command { get; init; }
        /// <summary>Exact command name as stored in the table (case preserved).</summary>
        public string Name { get; init; }
        /// <summary>Dispatch index (record +0x18, == the value switched on in sub_622820).</summary>
        public int DispatchIndex { get; init; }
        /// <summary>Required GM permission (record +0x1C).</summary>
        public int RequiredPermission { get; init; }
        /// <summary>Terminal shape of the case / jump-table slot for this index.</summary>
        public GmActivityHandlerKind HandlerKind { get; init; }
        /// <summary>Case-branch address = the jump-table slot value (0x622B1C + idx*4). For no-ops this is
        /// the shared default sink 0x0062B648. NOT the delegated core body address.</summary>
        public uint CaseAddress { get; init; }
        /// <summary>True when a live GameSvr/Command/Commands/*Command.cs exists for this name.</summary>
        public bool CSharpLivePresent { get; init; }
        /// <summary>Perm declared in the live [GameCommand(...)] attribute (0 when no live command). No drift
        /// in this family: equals RequiredPermission for every verified live command.</summary>
        public int CSharpLivePermission { get; init; }
        /// <summary>True when the live C# stub does MORE than the original (over-sends on a silent no-op).</summary>
        public bool CSharpStubOverSends { get; init; }
        /// <summary>True when the live C# stub does LESS than the original (fail-closed on a real impl).</summary>
        public bool CSharpStubUnderImplements { get; init; }
    }

    public static class NativeGmActivityCommands
    {
        // dispatcher constants (shared with the whole @-command family)
        public const uint DispatcherEa = 0x00622820;          // sub_622820
        public const uint HandledByteSetEa = 0x0062284D;      // mov [ebp+var_D], 1
        public const uint IndexLookupEa = 0x00621F28;         // sub_621F28
        public const uint JumpTableEa = 0x00622B1C;           // jpt_622B15
        public const int SwitchMaxIndex = 750;                // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;         // def_622B15  (handled=0 no-op)
        public const uint EmptyCaseEa = 0x0062B64C;           // loc_62B64C  (handled=1 empty case; unused here)
        public const int SysMsgVtableOffset = 0xD4;           // player vtable slot +212 = SysMsg(colour,text)

        // SysMsg colour words (LOWORD of the immediate, stored as unsigned hex)
        public const int ColorGreen = 0x38FF;                 // PushSingleTask / SignInAct-usage / fixed-reply stubs
        public const int ColorYellowNotice = 0xFFDB;          // everything else in the family (the -37 immediate)

        // fixed-reply stub strings (each SysMsg'd with ColorGreen)
        public const string InvalidReplyText = "Invalid";                     // GetGuildMember (folded case)
        public const string CanNotInsertReplyText = "Can not insert directly"; // addGuildMem

        // find-character-by-name helper (returns a char/player pointer); shared by AddVote guard + setGoldActLv
        public const uint CoreFindCharEa = 0x00652784;        // sub_652784

        // setGoldActLv writes the parsed level byte to [char + 6173] (0x181D) inline (no deferred core)
        public const int CharGoldActLvOffset = 6173;

        // CORE targets each implemented case forwards to (bodies NOT in the dumps -> CoreBodyDeferred)
        public const uint CoreGiveTitleEa = 0x006C66C4;       // givetitle    -> sub_6C66C4(name, title)
        public const uint CoreQuxiaoTitleEa = 0x006C67D4;     // quxiaotitle  -> sub_6C67D4(name)
        public const uint CorePushSingleTaskEa = 0x00656924;  // PushSingleTask-> sub_656924()
        public const uint CoreUpdateOrderEa = 0x00713094;     // UpdateOrder  -> sub_713094(0)
        public const uint CoreAddNWPresentEa = 0x00603948;    // AddNWPresent -> sub_603948(count, giftName)
        public const uint CoreAddNWAcceptEa = 0x006036B8;     // AddNWAccept  -> sub_6036B8(count, giftName)
        public const uint CoreGetSEa = 0x006CD6E8;            // GetS         -> sub_6CD6E8(field)
        public const uint CoreSetSEa = 0x006CDA0C;            // SetS         -> sub_6CDA0C(field, value)
        public const uint CoreSignInOpenEa = 0x00616FFC;      // SignInAct open  -> sub_616FFC()
        public const uint CoreSignInCloseEa = 0x00616BA0;     // SignInAct close -> sub_616BA0()
        public const uint CorePushActIdentEa = 0x006E4030;    // PushActIdent -> sub_6E4030(actNum)
        public const uint CoreAddVoteEa = 0x006EAE18;         // AddVote      -> sub_6EAE18(0, votes, 0)
        // SimpleActCtrl (314) sub-command cores
        public const uint CoreSimpleActReloadEa = 0x00723D78; // sub_723D78()  reload
        public const uint CoreSimpleActStatusEa = 0x00724014; // sub_724014()  status
        public const uint CoreSimpleActToggleEa = 0x00723E64; // sub_723E64(openFlag) open/close
        // GMActCtrl (345) sub-command cores
        public const uint CoreGMActReloadEa = 0x00611948;     // sub_611948()  reload
        public const uint CoreGMActStopEa = 0x006122B8;       // sub_6122B8()  stop
        public const uint CoreGMActStartEa = 0x00612230;      // sub_612230()  start
        public const uint CoreGMActStatusEa = 0x00612260;     // sub_612260()  (status/query)
        public const uint CoreGMActActionEa = 0x00612290;     // sub_612290(a,b,c) action/direction

        private static readonly GmActivityCommandInfo[] Registry =
        {
            // ---- IMPLEMENTED cases (dedicated body does real work) ----
            new() { Command = GmActivityCommand.Givetitle,     Name = "givetitle",      DispatchIndex = 142, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x0062552E },
            new() { Command = GmActivityCommand.Quxiaotitle,   Name = "quxiaotitle",    DispatchIndex = 143, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x00625541 },
            new() { Command = GmActivityCommand.PushSingleTask,Name = "PushSingleTask", DispatchIndex = 197, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x006287F1 },
            new() { Command = GmActivityCommand.UpdateOrder,   Name = "UpdateOrder",    DispatchIndex = 200, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x00625D19 },
            new() { Command = GmActivityCommand.AddNWPresent,  Name = "AddNWPresent",   DispatchIndex = 231, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x00626215 },
            new() { Command = GmActivityCommand.AddNWAccept,   Name = "AddNWAccept",    DispatchIndex = 232, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x00626272 },
            new() { Command = GmActivityCommand.GetS,          Name = "GetS",           DispatchIndex = 236, RequiredPermission = 3, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x00625E13 },
            new() { Command = GmActivityCommand.SetS,          Name = "SetS",           DispatchIndex = 238, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x00625E45 },
            new() { Command = GmActivityCommand.SignInAct,     Name = "SignInAct",      DispatchIndex = 263, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x006266F8, CSharpLivePresent = true, CSharpLivePermission = 4 },
            new() { Command = GmActivityCommand.PushActIdent,  Name = "PushActIdent",   DispatchIndex = 284, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x00626E88 },
            new() { Command = GmActivityCommand.SimpleActCtrl, Name = "SimpleActCtrl",  DispatchIndex = 314, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x00627754 },
            new() { Command = GmActivityCommand.AddVote,       Name = "AddVote",        DispatchIndex = 331, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x0062791E, CSharpLivePresent = true, CSharpLivePermission = 5, CSharpStubUnderImplements = true },
            new() { Command = GmActivityCommand.GMActCtrl,     Name = "GMActCtrl",      DispatchIndex = 345, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x00627A37, CSharpLivePresent = true, CSharpLivePermission = 4, CSharpStubUnderImplements = true },
            new() { Command = GmActivityCommand.SetGoldActLv,  Name = "setGoldActLv",   DispatchIndex = 496, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.ImplementedCase, CaseAddress = 0x006292A1, CSharpLivePresent = true, CSharpLivePermission = 5, CSharpStubUnderImplements = true },

            // ---- STUBBED fixed-reply (dedicated case -> one literal SysMsg, no effect) ----
            new() { Command = GmActivityCommand.GetGuildMember,Name = "GetGuildMember", DispatchIndex = 355, RequiredPermission = 3, HandlerKind = GmActivityHandlerKind.StubbedFixedReply, CaseAddress = 0x00627FBC },
            new() { Command = GmActivityCommand.AddGuildMem,   Name = "addGuildMem",    DispatchIndex = 444, RequiredPermission = 3, HandlerKind = GmActivityHandlerKind.StubbedFixedReply, CaseAddress = 0x00628AE1 },

            // ---- DEFAULT no-ops (def_622B15 @0x0062B648, clears handled byte) ----
            // (SetActScore idx264 is a default no-op too, but it is modeled in the item family model.)
            new() { Command = GmActivityCommand.SetTitle,           Name = "SetTitle",            DispatchIndex = 286, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.DelTitle,           Name = "DelTitle",            DispatchIndex = 287, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.QueryTitle,         Name = "QueryTitle",          DispatchIndex = 288, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.CurTitle,           Name = "CurTitle",            DispatchIndex = 289, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.DeliverTitle,       Name = "DeliverTitle",        DispatchIndex = 290, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.DrawTitle,          Name = "DrawTitle",           DispatchIndex = 291, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.OpenTitle,          Name = "OpenTitle",           DispatchIndex = 292, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.TitleDetail,        Name = "TitleDetail",         DispatchIndex = 293, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.LoadTitle,          Name = "LoadTitle",           DispatchIndex = 294, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.PayScore,           Name = "PayScore",            DispatchIndex = 295, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa, CSharpLivePresent = true, CSharpLivePermission = 4 },
            new() { Command = GmActivityCommand.ExecTitleCmd,       Name = "ExecTitleCmd",        DispatchIndex = 302, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.QueryZFF,           Name = "QueryZFF",            DispatchIndex = 303, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.ShowPayScore,       Name = "showPayScore",        DispatchIndex = 310, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa, CSharpLivePresent = true, CSharpLivePermission = 4 },
            new() { Command = GmActivityCommand.ReloadGoddessConfig,Name = "ReloadGoddessConfig", DispatchIndex = 438, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa, CSharpLivePresent = true, CSharpLivePermission = 4, CSharpStubOverSends = true },
            new() { Command = GmActivityCommand.GoddessVote,        Name = "GoddessVote",         DispatchIndex = 439, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.ReloadSnakeConf,    Name = "ReloadSnakeConf",     DispatchIndex = 512, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa, CSharpLivePresent = true, CSharpLivePermission = 4, CSharpStubOverSends = true },
            new() { Command = GmActivityCommand.ViewSnakeCost,      Name = "ViewSnakeCost",       DispatchIndex = 513, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.SetAchieve,         Name = "SETACHIEVE",          DispatchIndex = 543, RequiredPermission = 5, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa, CSharpLivePresent = true, CSharpLivePermission = 5, CSharpStubOverSends = true },
            new() { Command = GmActivityCommand.Achievement,        Name = "achievement",         DispatchIndex = 546, RequiredPermission = 3, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.DaAddTimes,         Name = "DaAddTimes",          DispatchIndex = 558, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.DaClear,            Name = "DaClear",             DispatchIndex = 559, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.SetActiveValue,     Name = "SetActiveValue",      DispatchIndex = 560, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmActivityCommand.ReloadDailyActiveCfg,Name = "ReloadDailyActiveCfg",DispatchIndex = 561, RequiredPermission = 4, HandlerKind = GmActivityHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa, CSharpLivePresent = true, CSharpLivePermission = 4, CSharpStubOverSends = true },
        };

        public static GmActivityCommandInfo Info(GmActivityCommand command)
        {
            foreach (var e in Registry)
                if (e.Command == command)
                    return e;
            throw new System.ArgumentOutOfRangeException(nameof(command));
        }

        public static System.Collections.Generic.IReadOnlyList<GmActivityCommandInfo> All => Registry;

        /// <summary>
        /// Final value of the dispatcher "handled" byte (var_D) for a given terminal shape. def_622B15
        /// clears it to 0 ("not handled"); every other shape leaves the optimistic 1 set at entry.
        /// </summary>
        public static bool HandledByteStaysSet(GmActivityHandlerKind kind) =>
            kind != GmActivityHandlerKind.DefaultNoOp;

        /// <summary>
        /// Contract for the 24 registered no-ops (all the def_622B15 default sink). Recognised by the
        /// table (valid index + permission) and permission-gated, but the dispatch mutates nothing and
        /// returns no message, and clears the handled byte to 0. Reuses the shared
        /// <see cref="NativeGmDefaultNoOp"/> type.
        /// </summary>
        public static NativeGmDefaultNoOp EvaluateNoOp(GmActivityCommand command)
        {
            var info = Info(command);
            if (info.HandlerKind != GmActivityHandlerKind.DefaultNoOp)
                throw new System.InvalidOperationException($"{info.Name} is not a no-op; use its own Evaluate");
            return new NativeGmDefaultNoOp
            {
                Recognized = true,
                DispatchesToDefaultCase = true,
                MutatesState = false,
                SendsResponse = false,
            };
        }
    }

    // ===================== Pure core-forward impls (never send a message) =====================
    // givetitle(142) sub_6C66C4(name,title) · quxiaotitle(143) sub_6C67D4(name) ·
    // UpdateOrder(200) sub_713094(0) · GetS(236) sub_6CD6E8(field) · SetS(238) sub_6CDA0C(field,value).
    // The case parses its args, unconditionally forwards to the core, sends no message; core not in dumps.
    public sealed class ActivityCoreForwardOutcome
    {
        public bool CallsCore => true;
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred => true;
        public bool SendsSysMsg => false;
    }

    public static class NativeGmGiveTitle
    {
        public static ActivityCoreForwardOutcome Evaluate(string charName, string title) =>
            new() { CoreEa = NativeGmActivityCommands.CoreGiveTitleEa };
    }

    public static class NativeGmQuxiaoTitle
    {
        public static ActivityCoreForwardOutcome Evaluate(string charName) =>
            new() { CoreEa = NativeGmActivityCommands.CoreQuxiaoTitleEa };
    }

    public static class NativeGmUpdateOrder
    {
        public static ActivityCoreForwardOutcome Evaluate() =>
            new() { CoreEa = NativeGmActivityCommands.CoreUpdateOrderEa };
    }

    public static class NativeGmGetS
    {
        public static ActivityCoreForwardOutcome Evaluate(int field) =>
            new() { CoreEa = NativeGmActivityCommands.CoreGetSEa };
    }

    public static class NativeGmSetS
    {
        public static ActivityCoreForwardOutcome Evaluate(int field, int value) =>
            new() { CoreEa = NativeGmActivityCommands.CoreSetSEa };
    }

    // ===================== Parse-then-core-then-message impls (message on the SUCCESS branch) =====
    // PushSingleTask(197)  parse actID; if actID>0 -> sub_656924() + SysMsg(0x38FF). (else silent.)
    // AddNWPresent(231)    parse count; if sub_603948(count,gift) -> SysMsg(0xFFDB). (else silent.)
    // AddNWAccept(232)     parse count; if sub_6036B8(count,gift) -> SysMsg(0xFFDB). (else silent.)
    public sealed class ActivitySuccessMsgOutcome
    {
        public bool CallsCore => true;
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred => true;
        /// <summary>The SysMsg fires only on the success branch (parse/core success); silent otherwise.</summary>
        public bool SendsSysMsgOnSuccess => true;
        public int MessageColor { get; init; }
    }

    public static class NativeGmPushSingleTask
    {
        public static ActivitySuccessMsgOutcome Evaluate(int actId) =>
            new() { CoreEa = NativeGmActivityCommands.CorePushSingleTaskEa, MessageColor = NativeGmActivityCommands.ColorGreen };
    }

    public static class NativeGmAddNWPresent
    {
        public static ActivitySuccessMsgOutcome Evaluate(int count, string giftName) =>
            new() { CoreEa = NativeGmActivityCommands.CoreAddNWPresentEa, MessageColor = NativeGmActivityCommands.ColorYellowNotice };
    }

    public static class NativeGmAddNWAccept
    {
        public static ActivitySuccessMsgOutcome Evaluate(int count, string giftName) =>
            new() { CoreEa = NativeGmActivityCommands.CoreAddNWAcceptEa, MessageColor = NativeGmActivityCommands.ColorYellowNotice };
    }

    // ===================== PushActIdent (idx 284) =====================
    // "@PushActIdent 玩家PTID 角色名 活动编号"  parse actNum; if BOTH string args present AND actNum!=-1
    //   -> sub_6E4030(actNum) (silent). Else -> SysMsg(0xFFDB, usage). Core body not in dumps.
    public enum PushActIdentBranch
    {
        Valid_CallCore,   // both strings + valid actNum -> core, no message
        Invalid_Usage,    // otherwise -> usage message, no core
    }

    public sealed class PushActIdentOutcome
    {
        public PushActIdentBranch Branch { get; init; }
        public bool CallsCore { get; init; }
        public uint CoreEa => NativeGmActivityCommands.CorePushActIdentEa;
        public bool CoreBodyDeferred { get; init; }
        public bool SendsSysMsg { get; init; }
        public int MessageColor { get; init; }
    }

    public static class NativeGmPushActIdent
    {
        public static PushActIdentOutcome Evaluate(bool ptidPresent, bool namePresent, int actNum)
        {
            bool valid = ptidPresent && namePresent && actNum != -1;
            return valid
                ? new PushActIdentOutcome { Branch = PushActIdentBranch.Valid_CallCore, CallsCore = true, CoreBodyDeferred = true, SendsSysMsg = false, MessageColor = 0 }
                : new PushActIdentOutcome { Branch = PushActIdentBranch.Invalid_Usage, CallsCore = false, CoreBodyDeferred = false, SendsSysMsg = true, MessageColor = NativeGmActivityCommands.ColorYellowNotice };
        }
    }

    // ===================== AddVote (idx 331) =====================
    // "@AddVote 角色名 票数 投票类型"  guard sub_652784 (find target char): if not found -> SysMsg(0xFFDB,
    //   "角色不存在..."). If found: parse votes+type; if votes<=0 || type<=0 -> SysMsg(0xFFDB, err); else
    //   sub_6EAE18(0, votes, 0) (silent). Core body not in dumps; find-char helper is sub_652784.
    public enum AddVoteBranch
    {
        CharNotFound,   // guard fail -> error message, no core
        BadArgs,        // votes<=0 or type<=0 -> error message, no core
        Applied,        // core sub_6EAE18(0,votes,0), no message
    }

    public sealed class AddVoteOutcome
    {
        public AddVoteBranch Branch { get; init; }
        public bool CallsCore { get; init; }
        public uint CoreEa => NativeGmActivityCommands.CoreAddVoteEa;
        public uint FindCharEa => NativeGmActivityCommands.CoreFindCharEa;
        public bool CoreBodyDeferred { get; init; }
        public bool SendsSysMsg { get; init; }
        public int MessageColor { get; init; }
    }

    public static class NativeGmAddVote
    {
        public static AddVoteOutcome Evaluate(bool charFound, int votes, int voteType)
        {
            if (!charFound)
                return new AddVoteOutcome { Branch = AddVoteBranch.CharNotFound, CallsCore = false, CoreBodyDeferred = false, SendsSysMsg = true, MessageColor = NativeGmActivityCommands.ColorYellowNotice };
            if (votes <= 0 || voteType <= 0)
                return new AddVoteOutcome { Branch = AddVoteBranch.BadArgs, CallsCore = false, CoreBodyDeferred = false, SendsSysMsg = true, MessageColor = NativeGmActivityCommands.ColorYellowNotice };
            return new AddVoteOutcome { Branch = AddVoteBranch.Applied, CallsCore = true, CoreBodyDeferred = true, SendsSysMsg = false, MessageColor = 0 };
        }
    }

    // ===================== setGoldActLv (idx 496) =====================
    // "@setGoldActLv 角色名 等级"  parse level; if level>0 -> find target char (sub_652784), write the
    //   level byte to [char + 6173] INLINE (no deferred core), then SysMsg(0xFFDB, confirm). If level<=0
    //   -> silent (no write, no message). Fully visible inline.
    public sealed class SetGoldActLvOutcome
    {
        /// <summary>True when level>0 (the only path that writes + messages).</summary>
        public bool Applied { get; init; }
        public bool WritesCharField { get; init; }
        public int CharFieldOffset => NativeGmActivityCommands.CharGoldActLvOffset;
        public uint FindCharEa => NativeGmActivityCommands.CoreFindCharEa;
        /// <summary>False: the write is inline in the dispatcher (nothing deferred).</summary>
        public bool CoreBodyDeferred => false;
        public bool SendsSysMsg { get; init; }
        public int MessageColor { get; init; }
    }

    public static class NativeGmSetGoldActLv
    {
        public static SetGoldActLvOutcome Evaluate(int level)
        {
            return level > 0
                ? new SetGoldActLvOutcome { Applied = true, WritesCharField = true, SendsSysMsg = true, MessageColor = NativeGmActivityCommands.ColorYellowNotice }
                : new SetGoldActLvOutcome { Applied = false, WritesCharField = false, SendsSysMsg = false, MessageColor = 0 };
        }
    }

    // ===================== SignInAct (idx 263) =====================
    // "@SignInAct [开启活动/关闭活动]"  if arg=="开启活动" -> sub_616FFC() (open) + SysMsg(0xFFDB).
    //   elif arg=="关闭活动" -> sub_616BA0() (close) + SysMsg(0xFFDB). else -> SysMsg(0x38FF, usage).
    //   Cores not in dumps. Live SignInActCommand is a faithful port of this shape.
    public enum SignInActBranch
    {
        Open,    // sub_616FFC + SysMsg 0xFFDB
        Close,   // sub_616BA0 + SysMsg 0xFFDB
        Usage,   // neither keyword -> SysMsg 0x38FF
    }

    public sealed class SignInActOutcome
    {
        public SignInActBranch Branch { get; init; }
        public bool CallsCore { get; init; }
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred { get; init; }
        public bool SendsSysMsg => true;
        public int MessageColor { get; init; }
    }

    public static class NativeGmSignInAct
    {
        public static SignInActOutcome Evaluate(string arg)
        {
            if (arg == "开启活动")
                return new SignInActOutcome { Branch = SignInActBranch.Open, CallsCore = true, CoreEa = NativeGmActivityCommands.CoreSignInOpenEa, CoreBodyDeferred = true, MessageColor = NativeGmActivityCommands.ColorYellowNotice };
            if (arg == "关闭活动")
                return new SignInActOutcome { Branch = SignInActBranch.Close, CallsCore = true, CoreEa = NativeGmActivityCommands.CoreSignInCloseEa, CoreBodyDeferred = true, MessageColor = NativeGmActivityCommands.ColorYellowNotice };
            return new SignInActOutcome { Branch = SignInActBranch.Usage, CallsCore = false, CoreEa = 0, CoreBodyDeferred = false, MessageColor = NativeGmActivityCommands.ColorGreen };
        }
    }

    // ===================== Multi-action controllers (SimpleActCtrl 314, GMActCtrl 345) =====================
    // Each parses a leading keyword to pick a sub-command; every branch ends with a SysMsg(0xFFDB). The
    // sub-command cores are listed below; their bodies are not in the dumps (CoreBodyDeferred).
    //   SimpleActCtrl: reload sub_723D78 · status sub_724014 · open/close sub_723E64(openFlag).
    //   GMActCtrl:     reload sub_611948 · stop sub_6122B8 · start sub_612230 · status sub_612260 ·
    //                  action sub_612290(actId, actionId, dirId).
    public sealed class ActivityMultiActionOutcome
    {
        public uint[] SubCommandCoreEas { get; init; }
        public bool CoreBodyDeferred => true;
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmActivityCommands.ColorYellowNotice;
    }

    public static class NativeGmSimpleActCtrl
    {
        public static ActivityMultiActionOutcome Evaluate() => new()
        {
            SubCommandCoreEas = new[]
            {
                NativeGmActivityCommands.CoreSimpleActReloadEa,
                NativeGmActivityCommands.CoreSimpleActStatusEa,
                NativeGmActivityCommands.CoreSimpleActToggleEa,
            },
        };
    }

    public static class NativeGmGMActCtrl
    {
        public static ActivityMultiActionOutcome Evaluate() => new()
        {
            SubCommandCoreEas = new[]
            {
                NativeGmActivityCommands.CoreGMActReloadEa,
                NativeGmActivityCommands.CoreGMActStopEa,
                NativeGmActivityCommands.CoreGMActStartEa,
                NativeGmActivityCommands.CoreGMActStatusEa,
                NativeGmActivityCommands.CoreGMActActionEa,
            },
        };
    }

    // ===================== StubbedFixedReply family =====================
    // GetGuildMember(355) -> SysMsg(0x38FF, "Invalid")  (its jump slot 0x00627FBC is a distinct shim, but
    //   Hex-Rays folds cases 265/355/404/453/458 into the one "Invalid" body).
    // addGuildMem(444)    -> SysMsg(0x38FF, "Can not insert directly").
    // Both mutate nothing.
    public sealed class ActivityStubReplyOutcome
    {
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmActivityCommands.ColorGreen;
        public string MessageText { get; init; }
        public bool MutatesState => false;
    }

    public static class NativeGmActivityStubReply
    {
        public static ActivityStubReplyOutcome Evaluate(GmActivityCommand command)
        {
            return command switch
            {
                GmActivityCommand.GetGuildMember => new ActivityStubReplyOutcome { MessageText = NativeGmActivityCommands.InvalidReplyText },
                GmActivityCommand.AddGuildMem => new ActivityStubReplyOutcome { MessageText = NativeGmActivityCommands.CanNotInsertReplyText },
                _ => throw new System.InvalidOperationException($"{command} is not a fixed-reply stub command"),
            };
        }
    }
}
