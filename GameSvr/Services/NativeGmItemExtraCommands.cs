namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant model — SECOND SUPPLEMENT to NativeGmItemCommands.cs / NativeGmItemCommandsSupplement.cs.
    //
    // The first two files model one slice each of the ITEM / EQUIP / MAKE GM "@" command family
    // (family 01). This file adds the 17 remaining family-01 commands that were present in the native
    // command table but slipped both prior models — so the three files together describe the whole
    // family without overlap. Nothing here is wired into the live command table; the live handlers remain
    // the fail-closed stubs in GameSvr/Command/Commands/*Command.cs. This type only *describes* the exact
    // original contract so an AuditTools check can lock it, and so a future port can reproduce it precisely.
    //
    // Same single-switch dispatcher as the rest of the family (see NativeGmSkillEquipCommands.cs):
    //   sub_6D7D68 -> sub_6BB2F8 -> sub_622820 (the switch); index from sub_621F28 (permission-gated);
    //   cmp esi, 0x2EE (750) ; ja default ; jmp jpt_622B15[esi*4] (table @0x00622B1C).
    //   Default def_622B15 @0x0062B648: silent no-op (handled byte cleared to 0, no message, no forward).
    //   Every implemented case below ends `goto LABEL_1055` (shared epilogue) -> handled byte stays 1.
    //
    // Evidence (IDA/Hex-Rays over unpacked M2Server_unpacked_fixed.exe, base 0x00400000,
    // SHA256 5540f43b...0b14e): disp_decomp.txt (Hex-Rays of sub_622820 case bodies) + big622820.txt
    // (raw disassembly; every CaseAddress below is confirmed annotated "jumptable ... case N" there) +
    // gm_full_inventory_20260731.md (record name/+0x18 index/+0x1C perm). No idat / no dotnet executed.
    //
    // CaseAddress = the case-branch address (the jump-table slot VALUE, i.e. where the case block starts),
    //   NOT the delegated core body. The cores each shim tail-calls (sub_XXXXXX) are NOT in the dumps and
    //   are abstracted as inputs (CoreBodyDeferred=true); this model captures only what the shim proves
    //   (forwarded-arg count, shim-level guard/parse, whether the shim itself sends a SysMsg). It does NOT
    //   invent the deferred core ladders.
    //
    // SysMsg call is the virtual slot [self]+0xD4 invoked as (colour, text). Every message in this slice
    //   uses colour 0xFFDB (the -37 immediate); none use 0x38FF.
    //
    // INVENTORY CORRECTION: gm_full_inventory tags ReloadunBindItem (166) "IMPL" — CORRECT — but the
    //   coordinator's hand-off tentatively grouped it with the reload* no-ops. disp_decomp case 166 is a
    //   real shim `sub_62E630()` (big622820 @0x00625CE2 `call sub_62E630`), so it IS implemented, not a
    //   no-op. Final split for this slice: 9 implemented + 8 registered no-ops (= 17).
    //
    // Per-command facts (Name = exact table spelling; Index = record+0x18; Perm = record+0x1C):
    //   IMPLEMENTED (distinct case block in sub_622820 -> deferred core):
    //     ReloadunBindItem idx 166 perm 4 case@0x00625CE2  sub_62E630(self)                [silent fwd, 0 args]
    //     make             idx 201 perm 5 case@0x00625D32  sub_6BDA34(self, name, count)   [silent fwd, 2 args]
    //     SuperMerchant    idx 297 perm 5 case@0x00626F32  3 ints parsed; guard off_7D6D10; sub_61668C(n) -> msg
    //     reloadRndItem    idx 299 perm 4 case@0x00626FD9  sub_7524A8() bool -> success/fail msg (both 0xFFDB)
    //     reloadStditem    idx 443 perm 4 case@0x00628AC6  sub_713094([off_7D62DC], 0x180, self)  [silent fwd]
    //     SetMaxButchCount idx 515 perm 4 case@0x0062954F  guard; write off_7D6888=n; persist cfg; -> msg
    //     cmdbinditem      idx 544 perm 4 case@0x00625475  guard 2 params; sub_6C64C0(name,itemId) / usage msg
    //     ReloadQKbag      idx 545 perm 4 case@0x006254D8  sub_7536B4() -> always msg (0xFFDB)
    //     chgItemBindDay   idx 562 perm 4 case@0x00623BF7  sub_6F33E4(arg)                 [silent fwd, 1 arg]
    //   REGISTERED BUT UNIMPLEMENTED (index maps to def_622B15 / silent no-op):
    //     loadSuperSmelt(311,p4) ClearAntiInfo(322,p5) CreateAntiItem(324,p5) AddAntiSuper(327,p5)
    //     ChgAntiNormal(421,p5) reloadEquipSplit(447,p4) EquipDrop(457,p4) ReloadComposeConfig(542,p4)
    //     (all verified: no `case` label in disp_decomp -> falls to def_622B15.)
    //
    // C# STUB DRIFT (live GameSvr/Command/Commands, NOT this model — flagged for the port): the live
    //   *Command.cs stubs for these are fail-closed NativeCommandFailure.Report / ShowHelp shims; the
    //   report itself is a SysMsg, so the reload/no-op stubs emit MORE than the silent native path.
    // ------------------------------------------------------------------------------------------------

    public enum GmItemExtraCommand
    {
        // ---- implemented (9) ----
        ReloadUnBindItem,
        Make,
        SuperMerchant,
        ReloadRndItem,
        ReloadStdItem,
        SetMaxButchCount,
        CmdBindItem,
        ReloadQKbag,
        ChgItemBindDay,
        // ---- registered no-ops (8, default sink) ----
        LoadSuperSmelt,
        ClearAntiInfo,
        CreateAntiItem,
        AddAntiSuper,
        ChgAntiNormal,
        ReloadEquipSplit,
        EquipDrop,
        ReloadComposeConfig,
    }

    /// <summary>Static command-table facts for one GM command (record name / +0x18 / +0x1C).</summary>
    public sealed class GmItemExtraCommandInfo
    {
        public GmItemExtraCommand Command { get; init; }
        /// <summary>Exact command name as stored in the table (case preserved).</summary>
        public string Name { get; init; }
        /// <summary>Dispatch index (record +0x18, == runtime record +0x18 used by the switch).</summary>
        public int DispatchIndex { get; init; }
        /// <summary>Required GM permission (record +0x1C). Original value; the C# stubs use 10.</summary>
        public int RequiredPermission { get; init; }
        /// <summary>True when sub_622820 has a real case; false when the index falls on def_622B15.</summary>
        public bool Implemented { get; init; }
        /// <summary>Case-branch address (the jump-table slot VALUE) or the shared default label (0x0062B648).</summary>
        public uint CaseAddress { get; init; }
        /// <summary>Core subroutine the shim tail-calls (0 for no-op commands).</summary>
        public uint CoreEa { get; init; }
        /// <summary>True when the core body is not present in the dumps and is abstracted as an input.</summary>
        public bool CoreBodyDeferred { get; init; }
    }

    public static class NativeGmItemExtraCommands
    {
        // dispatcher constants (identical single switch as the rest of the GM "@" family)
        public const uint DispatcherEa = 0x00622820;   // sub_622820
        public const uint IndexLookupEa = 0x00621F28;  // sub_621F28 (permission gate)
        public const uint JumpTableEa = 0x00622B1C;    // jpt_622B15
        public const int SwitchMaxIndex = 750;         // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;  // def_622B15 (silent no-op)
        public const uint EpilogueEa = 0x0062B64C;     // loc_62B64C (string cleanup, return handled)

        // SysMsg call + colour (every message in this slice is 0xFFDB)
        public const int SysMsgVtableOffset = 0xD4;    // call dword ptr [self]+0xD4 (colour, text)
        public const int ColorInfo = 0xFFDB;           // the -37 immediate

        // core subroutines invoked by the shims — bodies NOT in the dumps (CoreBodyDeferred)
        public const uint ReloadUnBindItemCoreEa = 0x0062E630; // sub_62E630 (ReloadunBindItem)
        public const uint MakeCoreEa = 0x006BDA34;             // sub_6BDA34 (make)
        public const uint SuperMerchantCoreEa = 0x0061668C;    // sub_61668C (SuperMerchant stock set)
        public const uint ReloadRndItemCoreEa = 0x007524A8;    // sub_7524A8 (reload randItems.txt -> bool)
        public const uint ReloadStdItemCoreEa = 0x00713094;    // sub_713094 (reloadStditem; shared w/ UpdateOrder)
        public const uint SetMaxButchMgrEa = 0x00790210;       // sub_790210 (config-manager getter)
        public const uint CmdBindItemCoreEa = 0x006C64C0;      // sub_6C64C0 (cmdbinditem; distinct from cmdBind's sub_6C6408)
        public const uint ReloadQKbagCoreEa = 0x007536B4;      // sub_7536B4 (reload QianKun bag config)
        public const uint ChgItemBindDayCoreEa = 0x006F33E4;   // sub_6F33E4 (chgItemBindDay)

        // globals touched by the shims
        public const uint SuperMerchantMgrGlobalEa = 0x007D6D10; // off_7D6D10 (SuperMerchant guard: mgr present)
        public const uint SetMaxButchGlobalEa = 0x007D6888;      // off_7D6888 (SetMaxButchCount writes the count)
        public const uint ReloadStdItemGlobalEa = 0x007D62DC;    // off_7D62DC (reloadStditem call receiver)
        public const int ReloadStdItemSelector = 0x180;          // dx = 0x180 passed to sub_713094

        // SetMaxButchCount persists via the config-manager vtable slot +12 with these literal keys
        public const string SetMaxButchConfigSection = "MaxBeButchedCount";
        public const string SetMaxButchConfigKey = "setup";
        public const int ConfigManagerPersistVtableOffset = 12; // call dword ptr [mgr]+0xC (section, key, value)

        private static readonly GmItemExtraCommandInfo[] Registry =
        {
            // ---- implemented (9) ----
            new() { Command = GmItemExtraCommand.ReloadUnBindItem, Name = "ReloadunBindItem", DispatchIndex = 166, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625CE2, CoreEa = ReloadUnBindItemCoreEa, CoreBodyDeferred = true },
            new() { Command = GmItemExtraCommand.Make,             Name = "make",             DispatchIndex = 201, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x00625D32, CoreEa = MakeCoreEa,            CoreBodyDeferred = true },
            new() { Command = GmItemExtraCommand.SuperMerchant,    Name = "SuperMerchant",    DispatchIndex = 297, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x00626F32, CoreEa = SuperMerchantCoreEa,   CoreBodyDeferred = true },
            new() { Command = GmItemExtraCommand.ReloadRndItem,    Name = "reloadRndItem",    DispatchIndex = 299, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00626FD9, CoreEa = ReloadRndItemCoreEa,   CoreBodyDeferred = true },
            new() { Command = GmItemExtraCommand.ReloadStdItem,    Name = "reloadStditem",    DispatchIndex = 443, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00628AC6, CoreEa = ReloadStdItemCoreEa,   CoreBodyDeferred = true },
            new() { Command = GmItemExtraCommand.SetMaxButchCount, Name = "SetMaxButchCount", DispatchIndex = 515, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x0062954F, CoreEa = SetMaxButchMgrEa,      CoreBodyDeferred = true },
            new() { Command = GmItemExtraCommand.CmdBindItem,      Name = "cmdbinditem",      DispatchIndex = 544, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625475, CoreEa = CmdBindItemCoreEa,     CoreBodyDeferred = true },
            new() { Command = GmItemExtraCommand.ReloadQKbag,      Name = "ReloadQKbag",      DispatchIndex = 545, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x006254D8, CoreEa = ReloadQKbagCoreEa,     CoreBodyDeferred = true },
            new() { Command = GmItemExtraCommand.ChgItemBindDay,   Name = "chgItemBindDay",   DispatchIndex = 562, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00623BF7, CoreEa = ChgItemBindDayCoreEa,  CoreBodyDeferred = true },

            // ---- registered no-ops (8, def_622B15 @0x0062B648) ----
            new() { Command = GmItemExtraCommand.LoadSuperSmelt,      Name = "loadSuperSmelt",      DispatchIndex = 311, RequiredPermission = 4, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
            new() { Command = GmItemExtraCommand.ClearAntiInfo,       Name = "ClearAntiInfo",       DispatchIndex = 322, RequiredPermission = 5, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
            new() { Command = GmItemExtraCommand.CreateAntiItem,      Name = "CreateAntiItem",      DispatchIndex = 324, RequiredPermission = 5, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
            new() { Command = GmItemExtraCommand.AddAntiSuper,        Name = "AddAntiSuper",        DispatchIndex = 327, RequiredPermission = 5, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
            new() { Command = GmItemExtraCommand.ChgAntiNormal,       Name = "ChgAntiNormal",       DispatchIndex = 421, RequiredPermission = 5, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
            new() { Command = GmItemExtraCommand.ReloadEquipSplit,    Name = "reloadEquipSplit",    DispatchIndex = 447, RequiredPermission = 4, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
            new() { Command = GmItemExtraCommand.EquipDrop,           Name = "EquipDrop",           DispatchIndex = 457, RequiredPermission = 4, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
            new() { Command = GmItemExtraCommand.ReloadComposeConfig, Name = "ReloadComposeConfig", DispatchIndex = 542, RequiredPermission = 4, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
        };

        public static GmItemExtraCommandInfo Info(GmItemExtraCommand command)
        {
            foreach (var e in Registry)
                if (e.Command == command)
                    return e;
            throw new System.ArgumentOutOfRangeException(nameof(command));
        }

        public static System.Collections.Generic.IReadOnlyList<GmItemExtraCommandInfo> All => Registry;

        /// <summary>Jump-table slot address for a dispatch index: jpt_622B15 + index*4.</summary>
        public static uint CasePtr(int dispatchIndex) => JumpTableEa + (uint)dispatchIndex * 4;

        /// <summary>
        /// Contract for the registered-but-unimplemented commands: recognized by the table (valid index +
        /// permission), permission-gated, but the switch lands on def_622B15 — nothing is mutated and
        /// nothing is sent back. Faithful behaviour is a silent no-op. Reuses the shared
        /// <see cref="NativeGmDefaultNoOp"/> contract type declared by NativeGmSkillEquipCommands.
        /// </summary>
        public static NativeGmDefaultNoOp EvaluateUnimplemented(GmItemExtraCommand command)
        {
            var info = Info(command);
            if (info.Implemented)
                throw new System.InvalidOperationException($"{info.Name} is implemented; use its own Evaluate");
            return new NativeGmDefaultNoOp
            {
                Recognized = true,
                DispatchesToDefaultCase = true,
                MutatesState = false,
                SendsResponse = false,
            };
        }
    }

    // ===================== Pure-forwarder shims (silent) =====================
    // The case block does NO validation and sends NO SysMsg; it only marshals the parsed params + self
    // into a deferred core routine. Shim-provable facts: forwarded command-arg count and that self is
    // forwarded (as receiver or argument).
    //   ReloadunBindItem  sub_62E630(self)                          -> 0 command args
    //   make              sub_6BDA34(self, name, count)             -> 2 command args
    //   reloadStditem     sub_713094([off_7D62DC], 0x180, self)     -> 0 command args (global receiver + selector)
    //   chgItemBindDay    sub_6F33E4(arg)                           -> 1 command arg
    public sealed class GmItemExtraForwardOutcome
    {
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred => true;
        /// <summary>Parsed command params forwarded to the core, excluding the always-present self.</summary>
        public int ForwardedArgCount { get; init; }
        public bool ForwardsSelf => true;
        public bool ShimValidates => false;
        public bool ShimSendsSysMsg => false;
    }

    public static class NativeGmItemExtraForwarders
    {
        public static GmItemExtraForwardOutcome ReloadUnBindItem() =>
            new() { CoreEa = NativeGmItemExtraCommands.ReloadUnBindItemCoreEa, ForwardedArgCount = 0 };

        public static GmItemExtraForwardOutcome Make() =>
            new() { CoreEa = NativeGmItemExtraCommands.MakeCoreEa, ForwardedArgCount = 2 };

        public static GmItemExtraForwardOutcome ReloadStdItem() =>
            new() { CoreEa = NativeGmItemExtraCommands.ReloadStdItemCoreEa, ForwardedArgCount = 0 };

        public static GmItemExtraForwardOutcome ChgItemBindDay() =>
            new() { CoreEa = NativeGmItemExtraCommands.ChgItemBindDayCoreEa, ForwardedArgCount = 1 };
    }

    // ===================== Reload-then-message shims =====================
    // ReloadQKbag(545)   sub_7536B4() then ALWAYS SysMsg(0xFFDB, confirm).  MessageVariesByResult = false.
    // reloadRndItem(299) ok = sub_7524A8(); ok -> SysMsg(0xFFDB, success) else SysMsg(0xFFDB, fail).
    //                    Both branches send 0xFFDB, so a message is ALWAYS sent; text varies by result.
    public sealed class ItemExtraReloadOutcome
    {
        public bool CallsCore => true;
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred => true;
        public bool MutatesState => true;           // the core reloads a config table
        public bool SendsSysMsg => true;            // a message is always sent
        public int MessageColor => NativeGmItemExtraCommands.ColorInfo;
        /// <summary>True when the message text depends on the core's success/fail result (reloadRndItem).</summary>
        public bool MessageVariesByResult { get; init; }
    }

    public static class NativeGmItemExtraReloads
    {
        public static ItemExtraReloadOutcome ReloadQKbag() =>
            new() { CoreEa = NativeGmItemExtraCommands.ReloadQKbagCoreEa, MessageVariesByResult = false };

        public static ItemExtraReloadOutcome ReloadRndItem() =>
            new() { CoreEa = NativeGmItemExtraCommands.ReloadRndItemCoreEa, MessageVariesByResult = true };
    }

    // ===================== SuperMerchant (idx 297) =====================
    // "@SuperMerchant 物品类型 库存类型 数量"  parse 3 ints (itemType, stockType, amount). If any <= 0 ->
    //   SysMsg(0xFFDB, usage) and stop. Else, guard the stock manager global off_7D6D10: if present ->
    //   ok = sub_61668C(amount); SysMsg(0xFFDB, ok ? success : fail). If the manager is absent -> SILENT
    //   (no message). Stock-set core body is deferred.
    public enum SuperMerchantBranch
    {
        BadArgs,      // any parsed int <= 0 -> usage message
        MgrAbsent,    // off_7D6D10 == 0 -> silent (no core, no message)
        Applied,      // core sub_61668C(amount) -> success/fail message
    }

    public sealed class SuperMerchantOutcome
    {
        public SuperMerchantBranch Branch { get; init; }
        public bool CallsCore { get; init; }
        public uint CoreEa => NativeGmItemExtraCommands.SuperMerchantCoreEa;
        public uint MgrGlobalEa => NativeGmItemExtraCommands.SuperMerchantMgrGlobalEa;
        public bool CoreBodyDeferred { get; init; }
        public bool SendsSysMsg { get; init; }
        public int MessageColor { get; init; }
    }

    public static class NativeGmSuperMerchant
    {
        public static SuperMerchantOutcome Evaluate(int itemType, int stockType, int amount, bool managerPresent)
        {
            if (itemType <= 0 || stockType <= 0 || amount <= 0)
                return new SuperMerchantOutcome { Branch = SuperMerchantBranch.BadArgs, CallsCore = false, CoreBodyDeferred = false, SendsSysMsg = true, MessageColor = NativeGmItemExtraCommands.ColorInfo };
            if (!managerPresent)
                return new SuperMerchantOutcome { Branch = SuperMerchantBranch.MgrAbsent, CallsCore = false, CoreBodyDeferred = false, SendsSysMsg = false, MessageColor = 0 };
            return new SuperMerchantOutcome { Branch = SuperMerchantBranch.Applied, CallsCore = true, CoreBodyDeferred = true, SendsSysMsg = true, MessageColor = NativeGmItemExtraCommands.ColorInfo };
        }
    }

    // ===================== cmdbinditem (idx 544) =====================
    // "@cmdbinditem 角色名 物品ID"  if BOTH params present -> sub_6C64C0(charName, itemId) (silent core).
    //   Else -> SysMsg(0xFFDB, usage). Distinct from cmdBind(140) which tail-calls sub_6C6408.
    public enum CmdBindItemBranch
    {
        Applied,   // both params -> core, silent
        Usage,     // missing param -> usage message
    }

    public sealed class CmdBindItemOutcome
    {
        public CmdBindItemBranch Branch { get; init; }
        public bool CallsCore { get; init; }
        public uint CoreEa => NativeGmItemExtraCommands.CmdBindItemCoreEa;
        public bool CoreBodyDeferred { get; init; }
        public bool SendsSysMsg { get; init; }
        public int MessageColor { get; init; }
    }

    public static class NativeGmCmdBindItem
    {
        public static CmdBindItemOutcome Evaluate(bool charNamePresent, bool itemIdPresent)
        {
            bool both = charNamePresent && itemIdPresent;
            return both
                ? new CmdBindItemOutcome { Branch = CmdBindItemBranch.Applied, CallsCore = true, CoreBodyDeferred = true, SendsSysMsg = false, MessageColor = 0 }
                : new CmdBindItemOutcome { Branch = CmdBindItemBranch.Usage, CallsCore = false, CoreBodyDeferred = false, SendsSysMsg = true, MessageColor = NativeGmItemExtraCommands.ColorInfo };
        }
    }

    // ===================== SetMaxButchCount (idx 515) =====================
    // "@SetMaxButchCount 次数"  Under an outer guard (v548[65]) and a config-manager getter sub_790210:
    //   if the manager exists -> parse count; write off_7D6888 = count (INLINE); persist via the manager
    //   vtable slot +12 with keys ("MaxBeButchedCount","setup",count); SysMsg(0xFFDB, confirm). If the
    //   outer guard fails -> SysMsg(0xFFDB, usage), no write. (-1 = unlimited is a semantic of the value,
    //   not a separate branch.) The manager getter/persist bodies are deferred; the global write is inline.
    public enum SetMaxButchCountBranch
    {
        Applied,       // guard ok -> inline write off_7D6888 + persist + confirm message
        GuardFailed,   // guard false -> usage message, no write
    }

    public sealed class SetMaxButchCountOutcome
    {
        public SetMaxButchCountBranch Branch { get; init; }
        public bool WritesGlobal { get; init; }
        public uint GlobalEa => NativeGmItemExtraCommands.SetMaxButchGlobalEa;
        public bool PersistsConfig { get; init; }
        public string ConfigSection => NativeGmItemExtraCommands.SetMaxButchConfigSection;
        public string ConfigKey => NativeGmItemExtraCommands.SetMaxButchConfigKey;
        public uint ManagerGetterEa => NativeGmItemExtraCommands.SetMaxButchMgrEa;
        /// <summary>The global write + confirm are inline; only the getter/persist cores are deferred.</summary>
        public bool CoreBodyDeferred { get; init; }
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmItemExtraCommands.ColorInfo;
    }

    public static class NativeGmSetMaxButchCount
    {
        /// <param name="guardPass">value of the outer guard v548[65] (config manager available).</param>
        public static SetMaxButchCountOutcome Evaluate(bool guardPass)
        {
            return guardPass
                ? new SetMaxButchCountOutcome { Branch = SetMaxButchCountBranch.Applied, WritesGlobal = true, PersistsConfig = true, CoreBodyDeferred = true }
                : new SetMaxButchCountOutcome { Branch = SetMaxButchCountBranch.GuardFailed, WritesGlobal = false, PersistsConfig = false, CoreBodyDeferred = false };
        }
    }
}
