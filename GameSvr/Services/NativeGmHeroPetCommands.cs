namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant model of the HERO / PET / SUMMON GM command family, reversed 1:1 from the original Delphi
    // M2Server ("战神" / God-of-War fork). NOT wired into the live command table — the live handlers stay
    // in GameSvr/Command/Commands/*Command.cs. This type only *describes* the exact original contract so
    // an AuditTools check can lock it and a future port can reproduce it precisely instead of guessing.
    //
    // Evidence (IDA/Hex-Rays over unpacked M2Server_unpacked_fixed.exe = m2full.i64, image base 0x00400000;
    // dumps in staging/update_clothes_4637_ida_work/: disp_decomp.txt, big622820.txt, world_scan_out.txt,
    // all_strings.txt, case_out.txt, raw_out.txt):
    //
    //   GM command dispatch is a SINGLE switch. Entry chain:
    //     sub_6D7D68 (main GM processor) -> sub_6BB2F8 -> sub_622820 (the switch).
    //   sub_622820 @0x00622820:
    //     * @0x0062284D  mov [ebp+var_D], 1     ; "handled" byte set to 1 OPTIMISTICALLY, before parsing.
    //     * splits "@name a:b,c d" into name + up to 6 params (delimiters ':',',',' ').
    //     * esi = sub_621F28(player, name, callerPerm, &reqPerm)   ; command-index lookup @0x00621F28,
    //         returns record[+0x18] (dispatchIndex) iff callerPerm >= record[+0x1C] (requiredPerm), else 0.
    //     * @0x00622B0F  cmp esi,0x2EE ; ja def_622B15   (index > 750 -> default)
    //     * @0x00622B15  jmp jpt_622B15[esi*4]           (table @0x00622B1C, 752 slots)
    //
    //   THREE terminal shapes matter for this family (all end at the shared epilogue loc_62B64C):
    //     (A) IMPLEMENTED case  — a dedicated case body runs, then `goto LABEL_1055` (var_D stays 1).
    //     (B) def_622B15 @0x0062B648 — the DEFAULT label: `mov [ebp+var_D], 0` then epilogue. The index is
    //         registered (name/index/perm/help exist) but the switch has NO case for it, so it clears the
    //         handled byte and returns not-handled. No state change, no message, no plugin forward.
    //     (C) loc_62B64C @0x0062B64C — an EMPTY case: the jump-table slot points straight at the epilogue
    //         (`xor eax,eax` + cleanup). var_D stays 1 (reported handled) but the body does literally
    //         nothing — no state change, no message. Grouped with cases 16-18,31,...,348,442,489,... whose
    //         jump-table entries all target loc_62B64C.
    //   (B) and (C) are both faithful SILENT NO-OPS; they differ only in the returned "handled" byte
    //   (0 for def_622B15, 1 for the empty case). Modeled distinctly via GmHeroPetHandlerKind.
    //
    //   Command record layout (raw_out.txt, verified on AddSkillExp @0x007CB874): the all_strings.txt
    //   address is the ShortString length byte; record[+0x18]=dispatchIndex, record[+0x1C]=requiredPerm.
    //
    // Per-command facts (Name = exact table spelling; Idx=record+0x18; Perm=record+0x1C; Case = jump target):
    //   IMPLEMENTED (dedicated case in sub_622820):
    //     CreateHero          idx 246 perm 5  case@0x00626316  parse job-token 1..6 -> core sub_6C9C00
    //     MakeMyHero          idx 147 perm 4  case@0x0062557D  create hero at GM cell (sub_604E3C) + notice
    //     DelHero             idx  81 perm 4  case@0x00624D86  -> core sub_6BF5AC (delete own hero)
    //     CallHero            idx 250 perm 5  case@0x006263C8  parse 2 ints -> core sub_6C9D28
    //     SetCallHero         idx 270 perm 5  case@0x006263ED  parse 2 ints -> core sub_6C9D88
    //     SetCallHeroInterval idx 271 perm 5  case@0x00626412  write 2 interval globals + confirm SysMsg
    //     AddHeroExp          idx 493 perm 4  case@0x0062926B  guard name+online -> core sub_6E2CC0(1,1e8)
    //     UpUserHeroLv        idx 222 perm 5  case@0x00626155  -> core sub_6D1C20(charName, newLevel)
    //   REGISTERED-BUT-UNIMPLEMENTED — def_622B15 (handled byte cleared to 0):
    //     SetPetSwitch        idx 341 perm 4  help "开启/关闭养宠物活动"
    //     herostrike          idx 325 perm 5  help "打开英雄攻城类别"
    //   RECOGNIZED-EMPTY-CASE — loc_62B64C (handled byte stays 1, still no effect / no message):
    //     LeaveHero           idx 442 perm 4  help "GM寄放自身的英雄"
    //     CreateProtectHero   idx 348 perm 4  help "刷怪(卧龙山庄人形怪)" (a disabled monster-spawn despite name)
    //
    //   Core-function bodies (sub_6C9C00 / sub_6BF5AC / sub_6C9D28 / sub_6C9D88 / sub_6E2CC0 / sub_6D1C20 /
    //   sub_604E3C) are NOT present in the current dumps (handler_out.txt holds only repeated copies of the
    //   sub_622820 dispatcher). They are marked CoreBodyDeferred=true: the in-dispatcher contract below is
    //   authoritative; the deeper core effect is deferred, not fabricated.
    //
    //   C# PORT DIVERGENCES observed (current live stubs vs. this original contract):
    //     SetPetSwitch — live SetPetSwitchCommand TOGGLES M2Share.g_Config.boPetSwitch + logs + SysMsgs;
    //                    original is a def_622B15 pure no-op. Live sends MORE than the binary. (OverSends)
    //     LeaveHero    — live LeaveHeroCommand calls UserEngine.RemoveHero + SysMsgs; original is an empty
    //                    case (no effect / no message). Live sends MORE than the binary. (OverSends)
    //     MakeMyHero   — live MakeMyHeroCommand is a fail-closed NativeCommandFailure stub (creates nothing);
    //                    original actually creates the hero + a notice. Live does LESS (safe direction).
    //
    //   Globals / struct fields touched by implemented handlers:
    //     player + 0x12C (dword field 75)  current X   [MakeMyHero reads]
    //     player + 0x130 (dword field 76)  current Y   [MakeMyHero reads]
    //     off_7D65E8   main-general (主将) hero call interval  [SetCallHeroInterval writes]
    //     off_7D6368   vice-general (副将) hero call interval  [SetCallHeroInterval writes]
    // ------------------------------------------------------------------------------------------------

    public enum GmHeroPetCommand
    {
        CreateHero,
        MakeMyHero,
        DelHero,
        CallHero,
        SetCallHero,
        SetCallHeroInterval,
        AddHeroExp,
        UpUserHeroLv,
        SetPetSwitch,
        Herostrike,
        LeaveHero,
        CreateProtectHero,
    }

    /// <summary>How the dispatcher's jump table terminates a given index.</summary>
    public enum GmHeroPetHandlerKind
    {
        /// <summary>Dedicated case body runs, then falls to the shared epilogue (handled byte = 1).</summary>
        ImplementedCase,
        /// <summary>Index maps to def_622B15 @0x0062B648: clears handled byte to 0, no effect, no message.</summary>
        DefaultNoOp,
        /// <summary>Index maps to loc_62B64C @0x0062B64C: empty case, handled byte stays 1, no effect.</summary>
        EmptyCaseNoOp,
    }

    /// <summary>Static command-table facts for one GM command (record name / +0x18 / +0x1C / jump target).</summary>
    public sealed class GmHeroPetCommandInfo
    {
        public GmHeroPetCommand Command { get; init; }
        /// <summary>Exact command name as stored in the table (case preserved).</summary>
        public string Name { get; init; }
        /// <summary>Dispatch index (record +0x18, == the value switched on in sub_622820).</summary>
        public int DispatchIndex { get; init; }
        /// <summary>Required GM permission (record +0x1C). Original value; several live C# stubs use 10.</summary>
        public int RequiredPermission { get; init; }
        /// <summary>Terminal shape of the jump-table slot for this index.</summary>
        public GmHeroPetHandlerKind HandlerKind { get; init; }
        /// <summary>Case body address (implemented) or the shared no-op label (0x0062B648 / 0x0062B64C).</summary>
        public uint CaseAddress { get; init; }
        /// <summary>True when the current live C# stub does MORE than the original (see header divergences).</summary>
        public bool CSharpStubOverSends { get; init; }
        /// <summary>True when the current live C# stub does LESS than the original (fail-closed).</summary>
        public bool CSharpStubUnderImplements { get; init; }
    }

    public static class NativeGmHeroPetCommands
    {
        // dispatcher constants (shared with the whole @-command family)
        public const uint DispatcherEa = 0x00622820;          // sub_622820
        public const uint HandledByteSetEa = 0x0062284D;      // mov [ebp+var_D], 1
        public const uint IndexLookupEa = 0x00621F28;         // sub_621F28
        public const uint JumpTableEa = 0x00622B1C;           // jpt_622B15
        public const int SwitchMaxIndex = 750;                // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;         // def_622B15  (handled=0 no-op)
        public const uint EmptyCaseEa = 0x0062B64C;           // loc_62B64C  (handled=1 empty case)

        // globals / offsets touched by implemented handlers (see header)
        public const int PlayerCurrXOffset = 0x12C;           // dword field 75  (MakeMyHero)
        public const int PlayerCurrYOffset = 0x130;           // dword field 76  (MakeMyHero)
        public const uint MainCallIntervalGlobalEa = 0x007D65E8; // off_7D65E8 (主将 interval)
        public const uint ViceCallIntervalGlobalEa = 0x007D6368; // off_7D6368 (副将 interval)

        // observed SysMsg colour words (LOWORD of the cx immediate)
        public const int ColorMakeMyHeroNotice = 0xFCFF;      // MakeMyHero success notice
        public const int ColorCallIntervalNotice = 0x38FF;    // SetCallHeroInterval confirm

        // AddHeroExp grants a FIXED amount, hardcoded in the case body (help: "增加玩家的英雄经验100000000")
        public const int AddHeroExpFixedAmount = 100000000;

        // recognised CreateHero job tokens -> selector 1..6, in the exact order the case checks them
        // ("英雄职业([男战|男法|男道|女战|女法|女道])"), unmatched -> silent no-op.
        public const int CreateHeroJobSelectorMin = 1;
        public const int CreateHeroJobSelectorMax = 6;

        // core targets whose bodies are not in the current dumps (CoreBodyDeferred)
        public const uint CoreCreateHeroEa = 0x006C9C00;      // sub_6C9C00
        public const uint CoreDelHeroEa = 0x006BF5AC;         // sub_6BF5AC
        public const uint CoreCallHeroEa = 0x006C9D28;        // sub_6C9D28
        public const uint CoreSetCallHeroEa = 0x006C9D88;     // sub_6C9D88
        public const uint CoreAddHeroExpEa = 0x006E2CC0;      // sub_6E2CC0
        public const uint CoreUpUserHeroLvEa = 0x006D1C20;    // sub_6D1C20
        public const uint CoreMakeMyHeroCreateEa = 0x00604E3C; // sub_604E3C

        private static readonly GmHeroPetCommandInfo[] Registry =
        {
            new() { Command = GmHeroPetCommand.CreateHero,          Name = "CreateHero",          DispatchIndex = 246, RequiredPermission = 5, HandlerKind = GmHeroPetHandlerKind.ImplementedCase, CaseAddress = 0x00626316 },
            new() { Command = GmHeroPetCommand.MakeMyHero,          Name = "MakeMyHero",          DispatchIndex = 147, RequiredPermission = 4, HandlerKind = GmHeroPetHandlerKind.ImplementedCase, CaseAddress = 0x0062557D, CSharpStubUnderImplements = true },
            new() { Command = GmHeroPetCommand.DelHero,             Name = "DelHero",             DispatchIndex =  81, RequiredPermission = 4, HandlerKind = GmHeroPetHandlerKind.ImplementedCase, CaseAddress = 0x00624D86 },
            new() { Command = GmHeroPetCommand.CallHero,            Name = "CallHero",            DispatchIndex = 250, RequiredPermission = 5, HandlerKind = GmHeroPetHandlerKind.ImplementedCase, CaseAddress = 0x006263C8 },
            new() { Command = GmHeroPetCommand.SetCallHero,         Name = "SetCallHero",         DispatchIndex = 270, RequiredPermission = 5, HandlerKind = GmHeroPetHandlerKind.ImplementedCase, CaseAddress = 0x006263ED },
            new() { Command = GmHeroPetCommand.SetCallHeroInterval, Name = "SetCallHeroInterval", DispatchIndex = 271, RequiredPermission = 5, HandlerKind = GmHeroPetHandlerKind.ImplementedCase, CaseAddress = 0x00626412 },
            new() { Command = GmHeroPetCommand.AddHeroExp,          Name = "AddHeroExp",          DispatchIndex = 493, RequiredPermission = 4, HandlerKind = GmHeroPetHandlerKind.ImplementedCase, CaseAddress = 0x0062926B },
            new() { Command = GmHeroPetCommand.UpUserHeroLv,        Name = "UpUserHeroLv",        DispatchIndex = 222, RequiredPermission = 5, HandlerKind = GmHeroPetHandlerKind.ImplementedCase, CaseAddress = 0x00626155 },
            new() { Command = GmHeroPetCommand.SetPetSwitch,        Name = "SetPetSwitch",        DispatchIndex = 341, RequiredPermission = 4, HandlerKind = GmHeroPetHandlerKind.DefaultNoOp,     CaseAddress = DefaultCaseEa, CSharpStubOverSends = true },
            new() { Command = GmHeroPetCommand.Herostrike,          Name = "herostrike",          DispatchIndex = 325, RequiredPermission = 5, HandlerKind = GmHeroPetHandlerKind.DefaultNoOp,     CaseAddress = DefaultCaseEa },
            new() { Command = GmHeroPetCommand.LeaveHero,           Name = "LeaveHero",           DispatchIndex = 442, RequiredPermission = 4, HandlerKind = GmHeroPetHandlerKind.EmptyCaseNoOp,   CaseAddress = EmptyCaseEa, CSharpStubOverSends = true },
            new() { Command = GmHeroPetCommand.CreateProtectHero,   Name = "CreateProtectHero",   DispatchIndex = 348, RequiredPermission = 4, HandlerKind = GmHeroPetHandlerKind.EmptyCaseNoOp,   CaseAddress = EmptyCaseEa },
        };

        public static GmHeroPetCommandInfo Info(GmHeroPetCommand command)
        {
            foreach (var e in Registry)
                if (e.Command == command)
                    return e;
            throw new System.ArgumentOutOfRangeException(nameof(command));
        }

        public static System.Collections.Generic.IReadOnlyList<GmHeroPetCommandInfo> All => Registry;

        /// <summary>
        /// Contract for the registered-but-no-op commands (both def_622B15 and the empty case). Recognised
        /// by the table (valid index + permission) and permission-gated, but the dispatch mutates nothing
        /// and returns no message. The only observable difference is the "handled" byte: 0 for def_622B15,
        /// 1 for the empty case.
        /// </summary>
        public static NativeGmHeroNoOp EvaluateNoOp(GmHeroPetCommand command)
        {
            var info = Info(command);
            if (info.HandlerKind == GmHeroPetHandlerKind.ImplementedCase)
                throw new System.InvalidOperationException($"{info.Name} is implemented; use its own Evaluate");
            return new NativeGmHeroNoOp
            {
                Recognized = true,
                HandlerKind = info.HandlerKind,
                HandledByteSet = info.HandlerKind == GmHeroPetHandlerKind.EmptyCaseNoOp,
                MutatesState = false,
                SendsResponse = false,
            };
        }
    }

    public sealed class NativeGmHeroNoOp
    {
        public bool Recognized { get; init; }
        public GmHeroPetHandlerKind HandlerKind { get; init; }
        /// <summary>Final value of the dispatcher "handled" byte (var_D): true=1 (empty case), false=0 (default).</summary>
        public bool HandledByteSet { get; init; }
        public bool MutatesState { get; init; }
        public bool SendsResponse { get; init; }
    }

    // ===================== CreateHero (idx 246) =====================
    // "@CreateHero 英雄职业 名字 英雄类别([1|2]) 英雄职业([男战|男法|男道|女战|女法|女道])"
    // case @0x00626316: match the job token against the six job names in order -> selector 1..6; if none
    //   match, `goto LABEL_1055` (silent no-op, no core call). Otherwise parse the category int and call
    //   core sub_6C9C00(selector). No SysMsg on any path.
    public enum CreateHeroBranch
    {
        JobUnmatched,   // no recognised job token -> silent no-op
        JobMatched,     // selector 1..6 -> core call
    }

    public sealed class CreateHeroOutcome
    {
        public CreateHeroBranch Branch { get; init; }
        /// <summary>1..6 when matched, 0 when unmatched.</summary>
        public int JobSelector { get; init; }
        public bool CallsCore { get; init; }
        public uint CoreEa => NativeGmHeroPetCommands.CoreCreateHeroEa;
        /// <summary>sub_6C9C00 body is not in the current dumps.</summary>
        public bool CoreBodyDeferred => Branch == CreateHeroBranch.JobMatched;
        public bool SendsSysMsg => false;
    }

    public static class NativeGmCreateHero
    {
        /// <param name="jobSelector">1..6 for a recognised 男战/男法/男道/女战/女法/女道 token, else 0.</param>
        public static CreateHeroOutcome Evaluate(int jobSelector)
        {
            if (jobSelector < NativeGmHeroPetCommands.CreateHeroJobSelectorMin
                || jobSelector > NativeGmHeroPetCommands.CreateHeroJobSelectorMax)
                return new CreateHeroOutcome { Branch = CreateHeroBranch.JobUnmatched, JobSelector = 0, CallsCore = false };
            return new CreateHeroOutcome { Branch = CreateHeroBranch.JobMatched, JobSelector = jobSelector, CallsCore = true };
        }
    }

    // ===================== MakeMyHero (idx 147) =====================
    // "@MakeMyHero 怪物名"  (create the GM's own 卧龙 hero from a monster template)
    // case @0x0062557D: read GM current X/Y (fields 75/76), adjust the target cell (sub_766298), then
    //   sub_604E3C(0, y, x). On success -> sub_60A538() + SysMsg(dword_62BFC8, colour 0xFCFF). On failure
    //   the case does nothing (silent).
    public enum MakeMyHeroBranch
    {
        CreatedWithNotice,
        CreateFailedSilent,
    }

    public sealed class MakeMyHeroOutcome
    {
        public MakeMyHeroBranch Branch { get; init; }
        public bool CallsCreateCore => true; // sub_604E3C is always attempted
        public uint CreateCoreEa => NativeGmHeroPetCommands.CoreMakeMyHeroCreateEa;
        public bool CoreBodyDeferred => true;
        public bool SendsSysMsg { get; init; }
        /// <summary>Colour word (0 when no message).</summary>
        public int MessageColor { get; init; }
    }

    public static class NativeGmMakeMyHero
    {
        public static MakeMyHeroOutcome Evaluate(bool createSucceeded)
        {
            return createSucceeded
                ? new MakeMyHeroOutcome { Branch = MakeMyHeroBranch.CreatedWithNotice, SendsSysMsg = true, MessageColor = NativeGmHeroPetCommands.ColorMakeMyHeroNotice }
                : new MakeMyHeroOutcome { Branch = MakeMyHeroBranch.CreateFailedSilent, SendsSysMsg = false, MessageColor = 0 };
        }
    }

    // ===================== DelHero / CallHero / SetCallHero / UpUserHeroLv =====================
    // These case bodies are unconditional forwards to a core function (no in-case guards, no SysMsg):
    //   DelHero      case@0x00624D86  sub_6BF5AC()                 (delete the GM's own hero)
    //   CallHero     case@0x006263C8  sub_6C9D28(category, job)
    //   SetCallHero  case@0x006263ED  sub_6C9D88(category, job)
    //   UpUserHeroLv case@0x00626155  sub_6D1C20(charName, newLevel)
    // The core bodies are not in the current dumps (CoreBodyDeferred). The faithful in-dispatcher contract
    // is: always call the core, never send a SysMsg from the case.
    public sealed class HeroCoreForwardOutcome
    {
        public bool CallsCore => true;
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred => true;
        public bool SendsSysMsg => false;
    }

    public static class NativeGmDelHero
    {
        public static HeroCoreForwardOutcome Evaluate() =>
            new() { CoreEa = NativeGmHeroPetCommands.CoreDelHeroEa };
    }

    public static class NativeGmCallHero
    {
        public static HeroCoreForwardOutcome Evaluate(int category, int job) =>
            new() { CoreEa = NativeGmHeroPetCommands.CoreCallHeroEa };
    }

    public static class NativeGmSetCallHero
    {
        public static HeroCoreForwardOutcome Evaluate(int category, int job) =>
            new() { CoreEa = NativeGmHeroPetCommands.CoreSetCallHeroEa };
    }

    public static class NativeGmUpUserHeroLv
    {
        public static HeroCoreForwardOutcome Evaluate(string charName, int newLevel) =>
            new() { CoreEa = NativeGmHeroPetCommands.CoreUpUserHeroLvEa };
    }

    // ===================== SetCallHeroInterval (idx 271) =====================
    // "@SetCallHeroInterval 主将英雄召唤时间间隔 副将英雄召唤时间间隔"
    // case @0x00626412: parse two ints; write off_7D65E8 (main-general interval) and off_7D6368 (vice-general
    //   interval); format a confirmation string and SysMsg it (colour 0x38FF). No guards — always applies.
    public sealed class SetCallHeroIntervalOutcome
    {
        public int MainInterval { get; init; }
        public int ViceInterval { get; init; }
        public bool WritesMainGlobal => true;
        public bool WritesViceGlobal => true;
        public uint MainGlobalEa => NativeGmHeroPetCommands.MainCallIntervalGlobalEa;
        public uint ViceGlobalEa => NativeGmHeroPetCommands.ViceCallIntervalGlobalEa;
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmHeroPetCommands.ColorCallIntervalNotice;
    }

    public static class NativeGmSetCallHeroInterval
    {
        public static SetCallHeroIntervalOutcome Evaluate(int mainInterval, int viceInterval) =>
            new() { MainInterval = mainInterval, ViceInterval = viceInterval };
    }

    // ===================== AddHeroExp (idx 493) =====================
    // "@AddHeroExp 角色名"  (help: "增加玩家的英雄经验100000000")
    // case @0x0062926B: require the target-name param non-empty AND FindPlayerByName resolves; then
    //   sub_6E2CC0(1, 100000000) — a FIXED 1e8 hero-exp grant (the amount is a literal in the case body).
    //   No SysMsg on any path. Failing the guard is a silent no-op.
    public enum AddHeroExpBranch
    {
        TargetInvalid,  // empty name or player not found -> silent no-op
        Applied,        // fixed 100000000 hero exp via core sub_6E2CC0
    }

    public sealed class AddHeroExpOutcome
    {
        public AddHeroExpBranch Branch { get; init; }
        public bool CallsCore { get; init; }
        public uint CoreEa => NativeGmHeroPetCommands.CoreAddHeroExpEa;
        public bool CoreBodyDeferred => Branch == AddHeroExpBranch.Applied;
        /// <summary>Fixed exp granted (0 when the guard fails).</summary>
        public int ExpGranted { get; init; }
        public bool SendsSysMsg => false;
    }

    public static class NativeGmAddHeroExp
    {
        public static AddHeroExpOutcome Evaluate(string charName, bool playerFound)
        {
            if (string.IsNullOrEmpty(charName) || !playerFound)
                return new AddHeroExpOutcome { Branch = AddHeroExpBranch.TargetInvalid, CallsCore = false, ExpGranted = 0 };
            return new AddHeroExpOutcome
            {
                Branch = AddHeroExpBranch.Applied,
                CallsCore = true,
                ExpGranted = NativeGmHeroPetCommands.AddHeroExpFixedAmount,
            };
        }
    }
}
