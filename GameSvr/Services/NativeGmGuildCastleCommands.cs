namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant model of the GUILD / CASTLE / SABAK (行会 / 沙巴克 / 攻城) GM command family, reversed 1:1
    // from the original Delphi M2Server ("战神" / God-of-War fork). NOT wired into the live command table
    // — the live handlers stay in GameSvr/Command/Commands/*Command.cs. This type only *describes* the
    // exact original contract so an AuditTools check can lock it and a future port can reproduce it
    // precisely instead of guessing.
    //
    // This is the GM "@"-command family (family 04 of the gm_full_inventory), NOT the CM_GILD_* client
    // social protocol (that is wired separately by the gild-* agents). 25 commands: 19 with a dedicated
    // switch case, 6 registered no-ops.
    //
    // Evidence (IDA/Hex-Rays over unpacked M2Server = m2full.i64, image base 0x00400000; dumps in
    // D:/loym2/staging/update_clothes_4637_ida_work/: disp_decomp.txt, big622820.txt, handler_out.txt,
    // all_strings.txt):
    //
    //   GM command dispatch is the SINGLE switch sub_622820 @0x00622820 (shared with the whole @-family).
    //     * @0x0062284D  mov [ebp+var_D], 1     ; "handled" byte set to 1 OPTIMISTICALLY, before parsing.
    //     * esi = sub_621F28(player, name, callerPerm, &reqPerm)   ; index lookup @0x00621F28, returns
    //         record[+0x18] (dispatchIndex) iff callerPerm >= record[+0x1C] (requiredPerm), else 0.
    //     * @0x00622B0F  cmp esi,0x2EE ; ja def_622B15   (index > 750 -> default)
    //     * @0x00622B15  jmp jpt_622B15[esi*4]           (table @0x00622B1C, 752 slots)
    //
    //   FIVE terminal shapes matter for this family (all end at the shared epilogue loc_62B64C):
    //     (A) IMPLEMENTED case  — a dedicated case body does real work (possibly forwarding to a core sub
    //         and/or sending a SysMsg), then `goto LABEL_1055` (var_D stays 1).
    //     (B) STUBBED-NULLSUB   — a dedicated case body parses its args then `call nullsub_NN`, where
    //         nullsub_NN is a VERIFIED-EMPTY function (IDA nullsub_ prefix; the four here are consecutive
    //         4-byte stubs 0x6C2908..0x6C2914). No state change, no message. var_D stays 1. In the 战神
    //         fork the guild-WAR GM verbs (GuildPoint/GuildWarOn/GuildWarOff/ReportGuildWar) were gutted
    //         this way. Distinct from a no-op only in that a real (empty) case body runs.
    //     (C) STUBBED-"Invalid" — a dedicated case body only sends the literal ASCII string "Invalid"
    //         (aInvalid @0x0062C638, colour 0x38FF) and returns. No state change. In the 战神 fork the
    //         guild-MANAGEMENT GM verbs (SetGuildLord/GuildForbid/selfAddGuild/ReNameGuild) were gutted
    //         this way (each has its own case address that loads the same "Invalid" constant).
    //     (D) EMPTY-CASE no-op  — the jump-table slot points straight at loc_62B64C @0x0062B64C (the
    //         epilogue). var_D stays 1 but the body does literally nothing. MakeGuild(213)/DelGuild(214)
    //         are here.
    //     (E) DEFAULT no-op     — def_622B15 @0x0062B648: `mov [ebp+var_D],0` then epilogue. The index is
    //         registered but the switch has NO case, so it clears the handled byte and returns
    //         not-handled. loadHeroStrike(326)/ChgGuildValue(336)/DreamCastleScore(351)/
    //         ChgDoubleCastleWar(531) are here.
    //   (B),(C),(D),(E) are all faithful SILENT-or-fixed NO-OPs on game state; only (C) sends a message.
    //   The observable "handled" byte is 0 ONLY for (E); it is 1 for (A)-(D).
    //
    //   SysMsg is the virtual call `(*(*player + 0xD4))(LOWORD colour, text)` — vtable slot +0xD4 (212).
    //   Colour words seen in this family (LOWORD of the cx immediate, stored here as the unsigned hex):
    //     0x38FF  (14591)  — ChgMonAtt, WatchGuild, the "Invalid" stubs
    //     0xFFDB  (-37)    — LookSaGold, ChgCastleWar (start branch), Begin/EndAreaCastleMatch,
    //                        serverguildswitch (guard-blocked branch)
    //     0xFCFF  (-769)   — serverguildswitch (open/close branches)
    //
    //   Core-function bodies (sub_6BFEF8/6C19D0/67D3DC/6C74EC/65C080/65D13C/65D158/6AE260) are NOT in the
    //   current dumps (handler_out.txt holds only 10 repeated copies of the sub_622820 dispatcher). They
    //   are marked CoreBodyDeferred=true: the in-dispatcher contract below is authoritative; the deeper
    //   core effect is deferred, not fabricated. The nullsub_85..88 stubs ARE resolved (verified empty),
    //   so they are NOT deferred — we assert they do nothing. LookSaGold / WatchGuild / serverguildswitch
    //   are fully visible inline (global reads/writes + Delphi string helpers), also not deferred.
    //
    //   Globals / struct fields touched (verified in big622820.txt):
    //     off_7D6214   sabak/castle manager object pointer:
    //                    [obj]+0x80 (128)  total funds (总资金)     [LookSaGold reads]
    //                    [obj]+0x84 (132)  today income (今日收入)  [LookSaGold reads]
    //                    [obj]+41   (0x29) war-active flag byte      [ChgCastleWar reads]
    //                    [obj]+43   (0x2B) start-war flag byte       [ChgCastleWar writes 1]
    //     off_7D600C   server-wide guild recruit/kick switch byte    [serverguildswitch writes 0/1]
    //
    //   C# PORT DIVERGENCES observed (live GameSvr/Command/Commands/*Command.cs vs. this original):
    //     * Older live commands declare perm 10 even where original +0x1C is 3/4/5.
    //       ChgMonAtt is now wired at its original permission 4.
    //     GuildWarOff (117) — original is a nullsub (does nothing, no message); live GuildWarOffCommand
    //                    does FindGuild + EndGuildWar on both guilds + two SysMsgs. Live OVER-sends.
    //     DelGuild    (214) — original is an EMPTY case (no effect, no message); live DelGuildCommand
    //                    actually deletes the guild + broadcasts SS_206. Live OVER-implements.
    //     ChgCastleWar(216) — original TOGGLES the single global sabak castle (off_7D6214): if war active
    //                    -> sub_65C080 (end, silent), else set +43 and SysMsg. Live ChgCastleWarCommand
    //                    is START-ONLY and keyed by a castle-NAME param the original never parses.
    //                    Behaviour mismatch.
    //     GuildForbid (404) — original sends literal "Invalid" and does nothing; live is a fail-closed
    //                    NativeCommandFailure notice (also no effect). Message text differs only.
    //     CallTaskMon (101), BeginAreaCastleMatch (394), EndAreaCastleMatch (395) — original does real
    //                    work (forwards a core / starts-ends the match + SysMsg); live is a fail-closed
    //                    NativeCommandFailure stub. Live UNDER-implements (safe direction).
    //     DreamCastleScore (351), ChgDoubleCastleWar (531) — original is a DEFAULT no-op (silent); live
    //                    is a fail-closed notice (over-sends a message the binary never sends).
    //     GuildWar (shell) — live has a "GuildWar" command that has no counterpart in the binary (the
    //                    registered names are GuildWarOn/GuildWarOff); it only tells the GM to use those.
    // ------------------------------------------------------------------------------------------------

    public enum GmGuildCastleCommand
    {
        LookSaGold,
        DoTask,
        CallTaskMon,
        GuildPoint,
        GuildWarOn,
        GuildWarOff,
        ReportGuildWar,
        ChgMonAtt,
        MakeGuild,
        DelGuild,
        ChgCastleOwner,
        ChgCastleWar,
        SetGuildLord,
        LoadHeroStrike,
        ChgGuildValue,
        DreamCastleScore,
        BeginAreaCastleMatch,
        EndAreaCastleMatch,
        GuildForbid,
        WatchGuild,
        SelfAddGuild,
        ReNameGuild,
        ChgDoubleCastleWar,
        ServerGuildSwitch,
        Weimanyuan,
    }

    /// <summary>Terminal shape of the jump-table slot / case body for one GM command.</summary>
    public enum GmGuildCastleHandlerKind
    {
        /// <summary>Dedicated case does real work (may forward a core and/or SysMsg). handled byte = 1.</summary>
        ImplementedCase,
        /// <summary>Dedicated case parses args then calls a verified-EMPTY nullsub. No effect, no msg. handled = 1.</summary>
        StubbedNullsub,
        /// <summary>Dedicated case only sends literal "Invalid" (0x38FF). No state change. handled = 1.</summary>
        StubbedInvalidReply,
        /// <summary>Jump-table slot -> loc_62B64C @0x0062B64C: empty case, no effect. handled byte stays 1.</summary>
        EmptyCaseNoOp,
        /// <summary>Jump-table slot -> def_622B15 @0x0062B648: clears handled byte to 0, no effect, no msg.</summary>
        DefaultNoOp,
    }

    /// <summary>Static command-table facts for one GM command (record name / +0x18 / +0x1C / jump target).</summary>
    public sealed class GmGuildCastleCommandInfo
    {
        public GmGuildCastleCommand Command { get; init; }
        /// <summary>Exact command name as stored in the table (case preserved).</summary>
        public string Name { get; init; }
        /// <summary>Dispatch index (record +0x18, == the value switched on in sub_622820).</summary>
        public int DispatchIndex { get; init; }
        /// <summary>Required GM permission from original record +0x1C.</summary>
        public int RequiredPermission { get; init; }
        /// <summary>Terminal shape of the case / jump-table slot for this index.</summary>
        public GmGuildCastleHandlerKind HandlerKind { get; init; }
        /// <summary>Case body address (implemented/stubbed) or the shared no-op label (0x62B648 / 0x62B64C).</summary>
        public uint CaseAddress { get; init; }
        /// <summary>True when a live GameSvr/Command/Commands/*Command.cs exists for this name.</summary>
        public bool CSharpLivePresent { get; init; }
        /// <summary>Perm declared in the live [GameCommand(...)] attribute (0 when no live command).</summary>
        public int CSharpLivePermission { get; init; }
        /// <summary>True when the live C# stub does MORE than the original (over-sends / over-implements).</summary>
        public bool CSharpStubOverSends { get; init; }
        /// <summary>True when the live C# stub does LESS than the original (fail-closed).</summary>
        public bool CSharpStubUnderImplements { get; init; }
        /// <summary>True when the live C# stub's behaviour differs in shape (not simply more/less).</summary>
        public bool CSharpBehaviorMismatch { get; init; }
    }

    public static class NativeGmGuildCastleCommands
    {
        // dispatcher constants (shared with the whole @-command family)
        public const uint DispatcherEa = 0x00622820;          // sub_622820
        public const uint HandledByteSetEa = 0x0062284D;      // mov [ebp+var_D], 1
        public const uint IndexLookupEa = 0x00621F28;         // sub_621F28
        public const uint JumpTableEa = 0x00622B1C;           // jpt_622B15
        public const int SwitchMaxIndex = 750;                // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;         // def_622B15  (handled=0 no-op)
        public const uint EmptyCaseEa = 0x0062B64C;           // loc_62B64C  (handled=1 empty case)
        public const int SysMsgVtableOffset = 0xD4;           // player vtable slot +212 = SysMsg(colour,text)

        // SysMsg colour words (LOWORD of the cx immediate, stored as unsigned hex)
        public const int ColorErrorRed = 0x38FF;              // ChgMonAtt / WatchGuild / "Invalid" stubs
        public const int ColorGreen = ColorErrorRed;          // compatibility alias for older audit callers
        public const int ColorYellowNotice = 0xFFDB;          // LookSaGold / ChgCastleWar / Area matches / guard-block
        public const int ColorWhiteNotice = 0xFCFF;           // serverguildswitch open/close

        // "Invalid" reply constant (shared by SetGuildLord/GuildForbid/selfAddGuild/ReNameGuild)
        public const uint InvalidStringEa = 0x0062C638;       // aInvalid
        public const string InvalidReplyText = "Invalid";

        // sabak/castle manager object + fields
        public const uint CastleManagerGlobalEa = 0x007D6214; // off_7D6214
        public const int CastleTotalFundsOffset = 0x80;       // [mgr]+0x80  (LookSaGold)
        public const int CastleTodayIncomeOffset = 0x84;      // [mgr]+0x84  (LookSaGold)
        public const int CastleWarActiveFlagOffset = 41;      // [mgr]+0x29  (ChgCastleWar reads)
        public const int CastleStartWarFlagOffset = 43;       // [mgr]+0x2B  (ChgCastleWar writes 1)

        // server-wide guild recruit/kick switch byte
        public const uint ServerGuildSwitchGlobalEa = 0x007D600C; // off_7D600C (serverguildswitch)

        // verified-empty nullsub stubs behind the guild-war verbs (each a 4-byte retn stub)
        public const uint NullsubGuildPointEa = 0x006C2908;   // nullsub_85  (GuildPoint)
        public const uint NullsubGuildWarOnEa = 0x006C290C;   // nullsub_86  (GuildWarOn)
        public const uint NullsubGuildWarOffEa = 0x006C2910;  // nullsub_87  (GuildWarOff)
        public const uint NullsubReportGuildWarEa = 0x006C2914;// nullsub_88 (ReportGuildWar)

        // core targets whose bodies are not in the current dumps (CoreBodyDeferred)
        public const uint CoreDoTaskEa = 0x006BFEF8;          // sub_6BFEF8  (DoTask)
        public const uint CoreCallTaskMonEa = 0x006C19D0;     // sub_6C19D0  (CallTaskMon)
        public const uint CoreChgMonAttEa = 0x0067D3DC;       // sub_67D3DC  (ChgMonAtt)
        public const uint CoreChgCastleOwnerEa = 0x006C74EC;  // sub_6C74EC  (ChgCastleOwner)
        public const uint CoreEndCastleWarEa = 0x0065C080;    // sub_65C080  (ChgCastleWar end path)
        public const uint CoreBeginAreaMatchEa = 0x0065D13C;  // sub_65D13C  (BeginAreaCastleMatch)
        public const uint CoreEndAreaMatchEa = 0x0065D158;    // sub_65D158  (EndAreaCastleMatch)
        public const uint CoreWeimanyuanEa = 0x006AE260;      // sub_6AE260(30,0) (weimanyuan)

        private static readonly GmGuildCastleCommandInfo[] Registry =
        {
            // ---- IMPLEMENTED cases (dedicated body does real work) ----
            new() { Command = GmGuildCastleCommand.LookSaGold,           Name = "LookSaGold",           DispatchIndex =  71, RequiredPermission = 3, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x00624C36 },
            new() { Command = GmGuildCastleCommand.DoTask,               Name = "DoTask",               DispatchIndex = 100, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x00625048 },
            new() { Command = GmGuildCastleCommand.CallTaskMon,          Name = "CallTaskMon",          DispatchIndex = 101, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x0062505B, CSharpLivePresent = true, CSharpLivePermission = 10, CSharpStubUnderImplements = true },
            new() { Command = GmGuildCastleCommand.ChgMonAtt,            Name = "ChgMonAtt",            DispatchIndex = 144, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x00625551, CSharpLivePresent = true, CSharpLivePermission = 4 },
            new() { Command = GmGuildCastleCommand.ChgCastleOwner,       Name = "ChgCastleOwner",       DispatchIndex = 215, RequiredPermission = 5, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x00625EC0 },
            new() { Command = GmGuildCastleCommand.ChgCastleWar,         Name = "ChgCastleWar",         DispatchIndex = 216, RequiredPermission = 5, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x00625ED5, CSharpLivePresent = true, CSharpLivePermission = 10, CSharpBehaviorMismatch = true },
            new() { Command = GmGuildCastleCommand.BeginAreaCastleMatch, Name = "BeginAreaCastleMatch", DispatchIndex = 394, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x0062877B, CSharpLivePresent = true, CSharpLivePermission = 10, CSharpStubUnderImplements = true },
            new() { Command = GmGuildCastleCommand.EndAreaCastleMatch,   Name = "EndAreaCastleMatch",   DispatchIndex = 395, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x006287CC, CSharpLivePresent = true, CSharpLivePermission = 10, CSharpStubUnderImplements = true },
            new() { Command = GmGuildCastleCommand.WatchGuild,           Name = "WatchGuild",           DispatchIndex = 405, RequiredPermission = 3, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x00628851 },
            new() { Command = GmGuildCastleCommand.ServerGuildSwitch,    Name = "serverguildswitch",    DispatchIndex = 534, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x00629697 },
            new() { Command = GmGuildCastleCommand.Weimanyuan,           Name = "weimanyuan",           DispatchIndex = 720, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.ImplementedCase, CaseAddress = 0x0062A94D },

            // ---- STUBBED-NULLSUB (dedicated case -> verified-empty nullsub) ----
            new() { Command = GmGuildCastleCommand.GuildPoint,           Name = "GuildPoint",           DispatchIndex = 115, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.StubbedNullsub, CaseAddress = 0x0062520D },
            new() { Command = GmGuildCastleCommand.GuildWarOn,           Name = "GuildWarOn",           DispatchIndex = 116, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.StubbedNullsub, CaseAddress = 0x00625229 },
            new() { Command = GmGuildCastleCommand.GuildWarOff,          Name = "GuildWarOff",          DispatchIndex = 117, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.StubbedNullsub, CaseAddress = 0x00625236, CSharpLivePresent = true, CSharpLivePermission = 10, CSharpStubOverSends = true },
            new() { Command = GmGuildCastleCommand.ReportGuildWar,       Name = "ReportGuildWar",       DispatchIndex = 118, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.StubbedNullsub, CaseAddress = 0x00625243 },

            // ---- STUBBED-"Invalid" (dedicated case -> sends "Invalid", no effect) ----
            new() { Command = GmGuildCastleCommand.SetGuildLord,         Name = "SetGuildLord",         DispatchIndex = 265, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.StubbedInvalidReply, CaseAddress = 0x006267C6 },
            new() { Command = GmGuildCastleCommand.GuildForbid,          Name = "GuildForbid",          DispatchIndex = 404, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.StubbedInvalidReply, CaseAddress = 0x00628978, CSharpLivePresent = true, CSharpLivePermission = 10, CSharpBehaviorMismatch = true },
            new() { Command = GmGuildCastleCommand.SelfAddGuild,         Name = "selfAddGuild",         DispatchIndex = 453, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.StubbedInvalidReply, CaseAddress = 0x00628B25 },
            new() { Command = GmGuildCastleCommand.ReNameGuild,          Name = "ReNameGuild",          DispatchIndex = 458, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.StubbedInvalidReply, CaseAddress = 0x00628991 },

            // ---- EMPTY-CASE no-ops (jump-table slot -> loc_62B64C) ----
            new() { Command = GmGuildCastleCommand.MakeGuild,            Name = "MakeGuild",            DispatchIndex = 213, RequiredPermission = 5, HandlerKind = GmGuildCastleHandlerKind.EmptyCaseNoOp, CaseAddress = EmptyCaseEa },
            new() { Command = GmGuildCastleCommand.DelGuild,             Name = "DelGuild",             DispatchIndex = 214, RequiredPermission = 5, HandlerKind = GmGuildCastleHandlerKind.EmptyCaseNoOp, CaseAddress = EmptyCaseEa, CSharpLivePresent = true, CSharpLivePermission = 10, CSharpStubOverSends = true },

            // ---- DEFAULT no-ops (def_622B15, clears handled byte) ----
            new() { Command = GmGuildCastleCommand.LoadHeroStrike,       Name = "loadHeroStrike",       DispatchIndex = 326, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmGuildCastleCommand.ChgGuildValue,        Name = "ChgGuildValue",        DispatchIndex = 336, RequiredPermission = 5, HandlerKind = GmGuildCastleHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa },
            new() { Command = GmGuildCastleCommand.DreamCastleScore,     Name = "DreamCastleScore",     DispatchIndex = 351, RequiredPermission = 4, HandlerKind = GmGuildCastleHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa, CSharpLivePresent = true, CSharpLivePermission = 10, CSharpStubOverSends = true },
            new() { Command = GmGuildCastleCommand.ChgDoubleCastleWar,   Name = "ChgDoubleCastleWar",   DispatchIndex = 531, RequiredPermission = 5, HandlerKind = GmGuildCastleHandlerKind.DefaultNoOp, CaseAddress = DefaultCaseEa, CSharpLivePresent = true, CSharpLivePermission = 10, CSharpStubOverSends = true },
        };

        public static GmGuildCastleCommandInfo Info(GmGuildCastleCommand command)
        {
            foreach (var e in Registry)
                if (e.Command == command)
                    return e;
            throw new System.ArgumentOutOfRangeException(nameof(command));
        }

        public static System.Collections.Generic.IReadOnlyList<GmGuildCastleCommandInfo> All => Registry;

        /// <summary>
        /// Final value of the dispatcher "handled" byte (var_D) for a given terminal shape. def_622B15
        /// clears it to 0 ("not handled" -> outer processor may fall through); every other shape leaves
        /// the optimistic 1 set at function entry.
        /// </summary>
        public static bool HandledByteStaysSet(GmGuildCastleHandlerKind kind) =>
            kind != GmGuildCastleHandlerKind.DefaultNoOp;

        /// <summary>
        /// Contract for the registered no-ops (both def_622B15 and the empty case at 0x62B64C). Recognised
        /// by the table (valid index + permission) and permission-gated, but the dispatch mutates nothing
        /// and returns no message. Reuses the shared <see cref="NativeGmDefaultNoOp"/> type; the only
        /// difference between the two sinks (the handled byte) is carried by <see cref="HandledByteStaysSet"/>
        /// and the registry's <see cref="GmGuildCastleCommandInfo.HandlerKind"/>.
        /// </summary>
        public static NativeGmDefaultNoOp EvaluateNoOp(GmGuildCastleCommand command)
        {
            var info = Info(command);
            if (info.HandlerKind != GmGuildCastleHandlerKind.DefaultNoOp
                && info.HandlerKind != GmGuildCastleHandlerKind.EmptyCaseNoOp)
                throw new System.InvalidOperationException($"{info.Name} is not a no-op; use its own Evaluate");
            return new NativeGmDefaultNoOp
            {
                Recognized = true,
                DispatchesToDefaultCase = info.HandlerKind == GmGuildCastleHandlerKind.DefaultNoOp,
                MutatesState = false,
                SendsResponse = false,
            };
        }
    }

    // ===================== StubbedNullsub family =====================
    // GuildPoint(115)/GuildWarOn(116)/GuildWarOff(117)/ReportGuildWar(118).
    // Each case parses its args (GuildPoint parses the 0/1/2 int) then `call nullsub_NN`. nullsub_85..88
    // are verified-empty 4-byte stubs -> no state change, no message. NOT CoreBodyDeferred (body known).
    public sealed class GuildWarStubOutcome
    {
        public uint NullsubEa { get; init; }
        public bool CallsNullsub => true;
        /// <summary>False: the nullsub body IS resolved (verified empty), so nothing is deferred.</summary>
        public bool CoreBodyDeferred => false;
        public bool MutatesState => false;
        public bool SendsSysMsg => false;
    }

    public static class NativeGmGuildWarStub
    {
        public static GuildWarStubOutcome Evaluate(GmGuildCastleCommand command)
        {
            uint ea = command switch
            {
                GmGuildCastleCommand.GuildPoint     => NativeGmGuildCastleCommands.NullsubGuildPointEa,
                GmGuildCastleCommand.GuildWarOn     => NativeGmGuildCastleCommands.NullsubGuildWarOnEa,
                GmGuildCastleCommand.GuildWarOff    => NativeGmGuildCastleCommands.NullsubGuildWarOffEa,
                GmGuildCastleCommand.ReportGuildWar => NativeGmGuildCastleCommands.NullsubReportGuildWarEa,
                _ => throw new System.InvalidOperationException($"{command} is not a nullsub-stub command"),
            };
            return new GuildWarStubOutcome { NullsubEa = ea };
        }
    }

    // ===================== StubbedInvalidReply family =====================
    // SetGuildLord(265)/GuildForbid(404)/selfAddGuild(453)/ReNameGuild(458).
    // Each case loads aInvalid ("Invalid" @0x0062C638) with colour 0x38FF and SysMsgs it. No state change.
    public sealed class GuildInvalidStubOutcome
    {
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmGuildCastleCommands.ColorGreen;
        public string MessageText => NativeGmGuildCastleCommands.InvalidReplyText;
        public uint MessageTextEa => NativeGmGuildCastleCommands.InvalidStringEa;
        public bool MutatesState => false;
    }

    public static class NativeGmGuildInvalidStub
    {
        public static GuildInvalidStubOutcome Evaluate(GmGuildCastleCommand command)
        {
            switch (command)
            {
                case GmGuildCastleCommand.SetGuildLord:
                case GmGuildCastleCommand.GuildForbid:
                case GmGuildCastleCommand.SelfAddGuild:
                case GmGuildCastleCommand.ReNameGuild:
                    return new GuildInvalidStubOutcome();
                default:
                    throw new System.InvalidOperationException($"{command} is not an Invalid-reply command");
            }
        }
    }

    // ===================== Pure core-forward impls (no SysMsg) =====================
    // DoTask(100) sub_6BFEF8(x,y) · CallTaskMon(101) sub_6C19D0(x,y,name,count) ·
    // ChgCastleOwner(215) sub_6C74EC(1, guildName) · weimanyuan(720) sub_6AE260(30,0).
    // The case unconditionally forwards to the core and sends no message; the core body is not in dumps.
    public sealed class GuildCastleCoreForwardOutcome
    {
        public bool CallsCore => true;
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred => true;
        public bool SendsSysMsg => false;
    }

    public static class NativeGmDoTask
    {
        public static GuildCastleCoreForwardOutcome Evaluate(int x, int y) =>
            new() { CoreEa = NativeGmGuildCastleCommands.CoreDoTaskEa };
    }

    public static class NativeGmCallTaskMon
    {
        public static GuildCastleCoreForwardOutcome Evaluate(int x, int y, string monName, int count) =>
            new() { CoreEa = NativeGmGuildCastleCommands.CoreCallTaskMonEa };
    }

    public static class NativeGmChgCastleOwner
    {
        public static GuildCastleCoreForwardOutcome Evaluate(string guildName) =>
            new() { CoreEa = NativeGmGuildCastleCommands.CoreChgCastleOwnerEa };
    }

    public static class NativeGmWeimanyuan
    {
        public static GuildCastleCoreForwardOutcome Evaluate() =>
            new() { CoreEa = NativeGmGuildCastleCommands.CoreWeimanyuanEa };
    }

    // ===================== Core-forward + SysMsg impls =====================
    // ChgMonAtt(144)            sub_67D3DC(&status) then SysMsg(0x38FF, status).
    // BeginAreaCastleMatch(394) sub_65D13C() then SysMsg(0xFFDB, "...开启时间...").
    // EndAreaCastleMatch(395)   sub_65D158() then SysMsg(0xFFDB, "关闭沙巴克积分赛").
    public sealed class GuildCastleCoreMsgOutcome
    {
        public bool CallsCore => true;
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred { get; init; } = true;
        public bool SendsSysMsg => true;
        public int MessageColor { get; init; }
    }

    public static class NativeGmChgMonAtt
    {
        public static GuildCastleCoreMsgOutcome Evaluate() =>
            new()
            {
                CoreEa = NativeGmGuildCastleCommands.CoreChgMonAttEa,
                CoreBodyDeferred = false,
                MessageColor = NativeGmGuildCastleCommands.ColorErrorRed
            };
    }

    public static class NativeGmBeginAreaCastleMatch
    {
        public static GuildCastleCoreMsgOutcome Evaluate() =>
            new() { CoreEa = NativeGmGuildCastleCommands.CoreBeginAreaMatchEa, MessageColor = NativeGmGuildCastleCommands.ColorYellowNotice };
    }

    public static class NativeGmEndAreaCastleMatch
    {
        public static GuildCastleCoreMsgOutcome Evaluate() =>
            new() { CoreEa = NativeGmGuildCastleCommands.CoreEndAreaMatchEa, MessageColor = NativeGmGuildCastleCommands.ColorYellowNotice };
    }

    // ===================== LookSaGold (idx 71) =====================
    // "@LookSaGold"  (query sabak treasury). Reads [off_7D6214]+0x80 (total funds) and +0x84 (today
    //   income), formats a two-number string, SysMsg(0xFFDB). Read-only; fully visible (not deferred).
    public sealed class LookSaGoldOutcome
    {
        public bool ReadsTotalFunds => true;
        public bool ReadsTodayIncome => true;
        public uint ManagerGlobalEa => NativeGmGuildCastleCommands.CastleManagerGlobalEa;
        public int TotalFundsOffset => NativeGmGuildCastleCommands.CastleTotalFundsOffset;
        public int TodayIncomeOffset => NativeGmGuildCastleCommands.CastleTodayIncomeOffset;
        public bool MutatesState => false;
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmGuildCastleCommands.ColorYellowNotice;
    }

    public static class NativeGmLookSaGold
    {
        public static LookSaGoldOutcome Evaluate() => new();
    }

    // ===================== ChgCastleWar (idx 216) =====================
    // "@ChgCastleWar"  (toggle the single global sabak castle war). If [off_7D6214]+41 (war-active) set
    //   -> sub_65C080() ends the war (silent). Else set [off_7D6214]+43 = 1 (schedule start) and
    //   SysMsg(0xFFDB). NOTE: no castle-name param is parsed; it operates on the one global castle.
    public enum ChgCastleWarBranch
    {
        WarActive_End,      // war-active flag set -> sub_65C080 end, no message
        WarInactive_Start,  // set start flag +43 and SysMsg 0xFFDB
    }

    public sealed class ChgCastleWarOutcome
    {
        public ChgCastleWarBranch Branch { get; init; }
        public bool CallsEndCore { get; init; }
        public uint EndCoreEa => NativeGmGuildCastleCommands.CoreEndCastleWarEa;
        /// <summary>sub_65C080 body is not in the current dumps.</summary>
        public bool CoreBodyDeferred => Branch == ChgCastleWarBranch.WarActive_End;
        public bool WritesStartFlag { get; init; }
        public uint ManagerGlobalEa => NativeGmGuildCastleCommands.CastleManagerGlobalEa;
        public int StartFlagOffset => NativeGmGuildCastleCommands.CastleStartWarFlagOffset;
        public bool SendsSysMsg { get; init; }
        public int MessageColor { get; init; }
    }

    public static class NativeGmChgCastleWar
    {
        /// <param name="warActive">value of [off_7D6214]+41 (the war-active flag byte).</param>
        public static ChgCastleWarOutcome Evaluate(bool warActive)
        {
            return warActive
                ? new ChgCastleWarOutcome { Branch = ChgCastleWarBranch.WarActive_End, CallsEndCore = true, WritesStartFlag = false, SendsSysMsg = false, MessageColor = 0 }
                : new ChgCastleWarOutcome { Branch = ChgCastleWarBranch.WarInactive_Start, CallsEndCore = false, WritesStartFlag = true, SendsSysMsg = true, MessageColor = NativeGmGuildCastleCommands.ColorYellowNotice };
        }
    }

    // ===================== WatchGuild (idx 405) =====================
    // "@WatchGuild [行会名/无]"  If a guild-name param is present -> set the watched-guild global +
    //   SysMsg(0x38FF, "关注行会 <name>"). Else clear the watched-guild global + SysMsg(0x38FF). Both
    //   branches are fully visible inline (Delphi string helpers), so nothing is deferred.
    public enum WatchGuildBranch
    {
        SetWatch,    // name given
        ClearWatch,  // no name
    }

    public sealed class WatchGuildOutcome
    {
        public WatchGuildBranch Branch { get; init; }
        public bool SetsWatchGuild { get; init; }
        public bool ClearsWatchGuild { get; init; }
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmGuildCastleCommands.ColorGreen;
    }

    public static class NativeGmWatchGuild
    {
        public static WatchGuildOutcome Evaluate(string guildName)
        {
            bool hasName = !string.IsNullOrEmpty(guildName);
            return hasName
                ? new WatchGuildOutcome { Branch = WatchGuildBranch.SetWatch, SetsWatchGuild = true, ClearsWatchGuild = false }
                : new WatchGuildOutcome { Branch = WatchGuildBranch.ClearWatch, SetsWatchGuild = false, ClearsWatchGuild = true };
        }
    }

    // ===================== serverguildswitch (idx 534) =====================
    // "@serverguildswitch 0/1"  Under an outer guard (v548[62]): parse the int; ==1 -> write off_7D600C=1
    //   + SysMsg(0xFCFF) (open); ==0 -> write off_7D600C=0 + SysMsg(0xFCFF) (close); any other value ->
    //   no write, no message. If the outer guard fails -> SysMsg(0xFFDB), no write. Fully visible inline.
    public enum ServerGuildSwitchBranch
    {
        Open,          // param == 1: write 1 + SysMsg 0xFCFF
        Close,         // param == 0: write 0 + SysMsg 0xFCFF
        NoChange,      // param is neither 0 nor 1: parsed, nothing written, no message
        GuardBlocked,  // outer guard false: SysMsg 0xFFDB, no write
    }

    public sealed class ServerGuildSwitchOutcome
    {
        public ServerGuildSwitchBranch Branch { get; init; }
        public bool WritesGlobal { get; init; }
        public uint GlobalEa => NativeGmGuildCastleCommands.ServerGuildSwitchGlobalEa;
        /// <summary>0 or 1 when WritesGlobal, else -1.</summary>
        public int GlobalValue { get; init; }
        public bool SendsSysMsg { get; init; }
        public int MessageColor { get; init; }
    }

    public static class NativeGmServerGuildSwitch
    {
        /// <param name="guardPass">value of the outer guard v548[62].</param>
        /// <param name="param">the parsed 0/1 argument.</param>
        public static ServerGuildSwitchOutcome Evaluate(bool guardPass, int param)
        {
            if (!guardPass)
                return new ServerGuildSwitchOutcome { Branch = ServerGuildSwitchBranch.GuardBlocked, WritesGlobal = false, GlobalValue = -1, SendsSysMsg = true, MessageColor = NativeGmGuildCastleCommands.ColorYellowNotice };
            if (param == 1)
                return new ServerGuildSwitchOutcome { Branch = ServerGuildSwitchBranch.Open, WritesGlobal = true, GlobalValue = 1, SendsSysMsg = true, MessageColor = NativeGmGuildCastleCommands.ColorWhiteNotice };
            if (param == 0)
                return new ServerGuildSwitchOutcome { Branch = ServerGuildSwitchBranch.Close, WritesGlobal = true, GlobalValue = 0, SendsSysMsg = true, MessageColor = NativeGmGuildCastleCommands.ColorWhiteNotice };
            return new ServerGuildSwitchOutcome { Branch = ServerGuildSwitchBranch.NoChange, WritesGlobal = false, GlobalValue = -1, SendsSysMsg = false, MessageColor = 0 };
        }
    }
}
