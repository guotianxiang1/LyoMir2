namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant model of the ITEM / EQUIPMENT GM command family, reversed 1:1 from the original Delphi
    // M2Server. NOT wired into the live command table — the live commands remain the fail-closed stubs
    // in GameSvr/Command/Commands/*Command.cs. This type only *describes* the exact original contract
    // so an AuditTools check can lock it, and so a future port can reproduce it precisely.
    //
    // Same single-switch dispatcher as the SKILL/HERO/EQUIP family (see NativeGmSkillEquipCommands.cs):
    //   sub_6D7D68 -> sub_6BB2F8 -> sub_622820 (the switch); index from sub_621F28 (permission-gated);
    //   cmp esi, 0x2EE (750) ; ja default ; jmp jpt_622B15[esi*4] (table @0x00622B1C).
    //   Default def_622B15 @0x0062B648: silent no-op (handled=0, no message, no plugin forward).
    //
    // Evidence (IDA/Hex-Rays over the unpacked image, base 0x00400000):
    //   * world_scan_out.txt gives every registry record (name/+0x18 index/+0x1C perm/handler addr).
    //   * big622820.txt is the raw disassembly of each case block (the "shim").
    //   * disp_decomp.txt is the Hex-Rays of the same switch.
    //
    // KEY FINDING: every implemented ITEM/EQUIP case block is a THIN SHIM. It marshals the parsed
    // params + self and tail-calls a core subroutine (sub_6C1C20/sub_6C22B0/... ). Those core bodies
    // are NOT present in the dumps, so per the fail-closed rule they are abstracted as inputs
    // (CoreBodyDeferred=true) — this model captures only what the shim itself proves: which params are
    // forwarded, any shim-level default/guard, and whether the shim sends a SysMsg. It does NOT invent
    // the core's internal ladders.
    //
    // SysMsg call is the virtual slot [self]+0xD4 invoked as (colour, text). Two colours appear here:
    //   0xFFDB info/echo channel; 0x38FF error channel (no ITEM/EQUIP shim uses 0x38FF).
    //
    // Str_ToInt-with-default helper sub_40CA18(str@eax, default@edx): returns the parsed int, or the
    // default when the string is empty/non-numeric. cmdBind uses it with default = 1.
    //
    // Per-command facts (Name = exact table spelling; Index = static+0x18; Perm = static+0x1C):
    //   IMPLEMENTED (distinct case block in sub_622820 -> deferred core):
    //     ShopItem      idx 85  perm 4  case@0x00624DD6  query sub_63B4E4  -> ALWAYS SysMsg(0xFFDB,result)
    //     ReLoadPItem   idx 86  perm 4  case@0x00624E05  reload sub_74DEDC -> ALWAYS SysMsg(0xFFDB,count)
    //     DelSelfItem   idx 103 perm 4  case@0x00625098  sub_6C1C20(self, itemName, count)          [silent shim]
    //     GetUserItem   idx 109 perm 4  case@0x00625152  sub_6C22B0(self, charName, itemId)         [silent shim]
    //     GiveUserItem  idx 110 perm 4  case@0x00625165  sub_6C253C(self, charName, itemId, bindTime)[silent shim]
    //     DecDuarg      idx 113 perm 4  case@0x006251D7  weapon(use-slot 1) present -> sub_784598(item,100); else no-op
    //     cmdBind       idx 140 perm 4  case@0x006254FD  bindType=sub_40CA18(param1,1); sub_6C6408(self,charName,bindType)
    //     ClearBind     idx 141 perm 4  case@0x0062551E  sub_6C6608(self, charName)                 [silent shim]
    //   REGISTERED BUT UNIMPLEMENTED (index maps to def_622B15 / silent no-op):
    //     EquipExchange       idx 557 perm 3   help "加载装备兑换配置"
    //     SetActScore         idx 264 perm 4   help "设置活动积分 @SetActScore 活动类型(0：九周年；1：富贵兽) 积分"
    //   (SetActScore is semantically activity-family but is kept here as its only coverage.
    //    EquipDropProtectOne idx 469 is ALSO a def_622B15 no-op but is owned solely by
    //    NativeGmSkillEquipCommands.cs — intentionally NOT duplicated here.)
    //
    // Struct offsets touched by the shims (self = TPlayObject, item = TUserItem):
    //     self + 0x4C0   use-items container pointer  (DecDuarg reads the weapon slot from *(self+0x4C0))
    //     item + 0x26    durability WORD              (DecDuarg target: set to 100 via sub_784598)
    //
    // NOTE ON SCOPE: this family is exactly the decoded ITEM/EQUIP command list; there is no "MakeItem"
    // GM command in the switch (item creation for GiveUserItem lives inside the deferred core, and the
    // separate MakeItem currency path is covered by NativeMakeItemUseDiamTransactionCheck).
    //
    // C# STUB DRIFT (live GameSvr/Command/Commands, NOT this model — flagged for the port):
    //   * GiveUserItemCommand.cs 的参数错位已修（2026-08-13）：param[1] 现在按原生当物品 ID、
    //     param[2] 当绑定时间；自造的现造道具 + RandomUpgradeItem + 50 件上限已移除，
    //     整条改为明确拒绝，直到 sub_6C253C 的绑定写入与离线记录 0x154 被移植。
    //   * GetUserItemCommand.cs ("@GetUserItem <PlayerName> <ItemID>") matches the native (charName,itemId);
    //     its core is separately validated by NativeGetUserItemCompatCheck.
    //   * All live stubs use RequiredPermission 10; the native records use 3/4 (see registry below).
    // ------------------------------------------------------------------------------------------------

    public enum GmItemCommand
    {
        ShopItem,
        ReLoadPItem,
        DelSelfItem,
        GetUserItem,
        GiveUserItem,
        DecDuarg,
        CmdBind,
        ClearBind,
        EquipExchange,          // registered no-op
        SetActScore,            // registered no-op (activity-family; kept here as sole coverage)
    }

    /// <summary>Static command-table facts for one GM command (record name / +0x18 / +0x1C).</summary>
    public sealed class GmItemCommandInfo
    {
        public GmItemCommand Command { get; init; }
        /// <summary>Exact command name as stored in the table (case preserved).</summary>
        public string Name { get; init; }
        /// <summary>Dispatch index (static record +0x18, == runtime record +0x18 used by the switch).</summary>
        public int DispatchIndex { get; init; }
        /// <summary>Required GM permission (static record +0x1C). Original value; the C# stubs use 10.</summary>
        public int RequiredPermission { get; init; }
        /// <summary>True when sub_622820 has a real case; false when the index falls on def_622B15.</summary>
        public bool Implemented { get; init; }
        /// <summary>Address of the case/shim block (implemented) or the shared default label (0x0062B648).</summary>
        public uint CaseAddress { get; init; }
        /// <summary>Core subroutine the shim tail-calls (0 for no-op commands).</summary>
        public uint CoreEa { get; init; }
        /// <summary>True when the core body is not present in the dumps and is abstracted as an input.</summary>
        public bool CoreBodyDeferred { get; init; }
    }

    public static class NativeGmItemCommands
    {
        // dispatcher constants (identical single switch as the skill/equip family)
        public const uint DispatcherEa = 0x00622820;   // sub_622820
        public const uint IndexLookupEa = 0x00621F28;  // sub_621F28
        public const uint JumpTableEa = 0x00622B1C;    // jpt_622B15
        public const int SwitchMaxIndex = 750;         // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;  // def_622B15 (silent no-op)

        // SysMsg call + colours
        public const int SysMsgVtableOffset = 0xD4;    // call dword ptr [self]+0xD4 (colour, text)
        public const int ColorInfo = 0xFFDB;           // ShopItem / ReLoadPItem echo channel
        public const int ColorError = 0x38FF;          // shared error channel (unused by these shims)

        // struct offsets touched by the shims
        public const int UseItemsContainerOffset = 0x4C0; // DecDuarg: *(self+0x4C0) -> use-items container
        public const int ItemDuraOffset = 0x26;           // TUserItem.Dura WORD (DecDuarg target)
        public const int WeaponUseSlot = 1;               // sub_75EC20(container, 1)
        public const int DecDuargValue = 100;             // dx = 0x64 passed to sub_784598

        // parse helper + shim-level default
        public const uint StrToIntWithDefaultEa = 0x0040CA18; // sub_40CA18(str, default)
        public const int CmdBindDefaultType = 1;              // cmdBind: sub_40CA18(param1, 1)

        // core subroutines invoked by the shims — bodies NOT in the dumps (CoreBodyDeferred)
        public const uint ShopItemQueryEa = 0x0063B4E4;   // sub_63B4E4 (shop lookup -> result string)
        public const uint ReloadPItemEa = 0x0074DEDC;     // sub_74DEDC (reload PowerupItem.ini -> count)
        public const uint DelSelfItemCoreEa = 0x006C1C20; // sub_6C1C20
        public const uint GetUserItemCoreEa = 0x006C22B0; // sub_6C22B0 (validated by NativeGetUserItemCompatCheck)
        public const uint GiveUserItemCoreEa = 0x006C253C;// sub_6C253C
        public const uint DecDuargApplyEa = 0x00784598;   // sub_784598 (set dura)
        public const uint CmdBindCoreEa = 0x006C6408;     // sub_6C6408
        public const uint ClearBindCoreEa = 0x006C6608;   // sub_6C6608

        private static readonly GmItemCommandInfo[] Registry =
        {
            new() { Command = GmItemCommand.ShopItem,      Name = "ShopItem",      DispatchIndex = 85,  RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00624DD6, CoreEa = ShopItemQueryEa,   CoreBodyDeferred = true },
            new() { Command = GmItemCommand.ReLoadPItem,   Name = "ReLoadPItem",   DispatchIndex = 86,  RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00624E05, CoreEa = ReloadPItemEa,     CoreBodyDeferred = true },
            new() { Command = GmItemCommand.DelSelfItem,   Name = "DelSelfItem",   DispatchIndex = 103, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625098, CoreEa = DelSelfItemCoreEa, CoreBodyDeferred = true },
            new() { Command = GmItemCommand.GetUserItem,   Name = "GetUserItem",   DispatchIndex = 109, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625152, CoreEa = GetUserItemCoreEa, CoreBodyDeferred = true },
            new() { Command = GmItemCommand.GiveUserItem,  Name = "GiveUserItem",  DispatchIndex = 110, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625165, CoreEa = GiveUserItemCoreEa,CoreBodyDeferred = true },
            new() { Command = GmItemCommand.DecDuarg,      Name = "DecDuarg",      DispatchIndex = 113, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x006251D7, CoreEa = DecDuargApplyEa,   CoreBodyDeferred = true },
            new() { Command = GmItemCommand.CmdBind,       Name = "cmdBind",       DispatchIndex = 140, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x006254FD, CoreEa = CmdBindCoreEa,     CoreBodyDeferred = true },
            new() { Command = GmItemCommand.ClearBind,     Name = "ClearBind",     DispatchIndex = 141, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x0062551E, CoreEa = ClearBindCoreEa,   CoreBodyDeferred = true },
            new() { Command = GmItemCommand.EquipExchange, Name = "EquipExchange", DispatchIndex = 557, RequiredPermission = 3, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
            new() { Command = GmItemCommand.SetActScore,   Name = "SetActScore",   DispatchIndex = 264, RequiredPermission = 4, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
        };

        public static GmItemCommandInfo Info(GmItemCommand command)
        {
            foreach (var e in Registry)
                if (e.Command == command)
                    return e;
            throw new System.ArgumentOutOfRangeException(nameof(command));
        }

        public static System.Collections.Generic.IReadOnlyList<GmItemCommandInfo> All => Registry;

        /// <summary>
        /// Contract for the registered-but-unimplemented commands: recognized by the table (valid index
        /// + permission), permission-gated, but the switch lands on def_622B15 — nothing is mutated and
        /// nothing is sent back. Faithful behaviour is a silent no-op.
        /// </summary>
        public static NativeGmDefaultNoOp EvaluateUnimplemented(GmItemCommand command)
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

    // ===================== ShopItem (idx 85) =====================
    // case @0x00624DD6: result = sub_63B4E4(shopMgr@off_7D5D98, itemName=param0, &result);
    //   SysMsg(self, 0xFFDB, result).  No guard on the item name; ALWAYS echoes the (deferred) query
    //   result. Read-only — no state mutation in either the shim or the query core.
    public sealed class ShopItemOutcome
    {
        public bool QueriesShop => true;
        public uint QueryCoreEa => NativeGmItemCommands.ShopItemQueryEa;
        public bool CoreBodyDeferred => true;   // sub_63B4E4 lookup content not in dumps
        public bool MutatesState => false;      // read-only query
        public bool SendsSysMsg => true;        // always echoes the result string
        public int MessageColor => NativeGmItemCommands.ColorInfo;
    }

    public static class NativeGmShopItem
    {
        // itemName is forwarded verbatim to the deferred query core; the shim never gates on it.
        public static ShopItemOutcome Evaluate(string itemName)
        {
            _ = itemName;
            return new ShopItemOutcome();
        }
    }

    // ===================== ReLoadPItem (idx 86) =====================
    // case @0x00624E05: count = sub_74DEDC(mgr@off_7D5D6C)  [reloads PowerupItem.ini];
    //   msg = concat(prefix@62BE6C, IntToStr(count), suffix@62BE48);  SysMsg(self, 0xFFDB, msg).
    //   Always reloads and always confirms with the reloaded count.
    public sealed class ReLoadPItemOutcome
    {
        public bool ReloadsConfig => true;
        public uint ReloadCoreEa => NativeGmItemCommands.ReloadPItemEa;
        public bool CoreBodyDeferred => true;   // sub_74DEDC body not in dumps
        public bool MutatesState => true;       // reloads the PowerupItem.ini global table
        public bool SendsSysMsg => true;        // always confirms with the reloaded count
        public int MessageColor => NativeGmItemCommands.ColorInfo;
    }

    public static class NativeGmReLoadPItem
    {
        public static ReLoadPItemOutcome Evaluate() => new();
    }

    // ===================== DecDuarg (idx 113) =====================
    // case @0x006251D7: item = sub_75EC20(*(self+0x4C0), useSlot=1)  [weapon];
    //   item == null -> jz epilogue (silent no-op);
    //   item != null -> sub_784598(item, 0x64)  [set weapon Dura = 100].  Self-only, never SysMsg.
    public enum DecDuargBranch
    {
        WeaponAbsent,
        WeaponDuraSet,
    }

    public sealed class DecDuargOutcome
    {
        public DecDuargBranch Branch { get; init; }
        public bool TargetsSelf => true;
        public int UseSlot => NativeGmItemCommands.WeaponUseSlot;    // 1
        public bool CallsApplyCore { get; init; }                   // sub_784598(item, 100)
        public int DuraValue => NativeGmItemCommands.DecDuargValue; // 100 (dx = 0x64)
        /// <summary>sub_784598 actual write not in the dumps; the value 100 is proven by the shim.</summary>
        public bool CoreBodyDeferred { get; init; }
        public bool SendsSysMsg => false;                           // silent on every path
    }

    public static class NativeGmDecDuarg
    {
        public static DecDuargOutcome Evaluate(bool weaponPresent) =>
            weaponPresent
                ? new DecDuargOutcome { Branch = DecDuargBranch.WeaponDuraSet, CallsApplyCore = true, CoreBodyDeferred = true }
                : new DecDuargOutcome { Branch = DecDuargBranch.WeaponAbsent, CallsApplyCore = false, CoreBodyDeferred = false };
    }

    // ===================== cmdBind (idx 140) =====================
    // case @0x006254FD: bindType = sub_40CA18(param1, /*default*/ 1);  sub_6C6408(self, charName=param0, bindType).
    //   The default type 1 is a SHIM fact (edx=1 preset before Str_ToInt). Bind core is deferred; the
    //   shim itself sends no SysMsg.
    public sealed class CmdBindOutcome
    {
        public int BindType { get; init; }
        /// <summary>True when param1 was empty/non-numeric and sub_40CA18 returned the default (1).</summary>
        public bool UsedDefault { get; init; }
        public bool CallsCore => true;              // sub_6C6408(self, charName, bindType)
        public uint CoreEa => NativeGmItemCommands.CmdBindCoreEa;
        public bool CoreBodyDeferred => true;
        public bool ShimSendsSysMsg => false;
    }

    public static class NativeGmCmdBind
    {
        // Mirrors sub_40CA18(param1, 1): parse int, else default to 1.
        public static CmdBindOutcome Evaluate(string bindTypeParam)
        {
            bool ok = int.TryParse(bindTypeParam, out int v);
            return new CmdBindOutcome
            {
                BindType = ok ? v : NativeGmItemCommands.CmdBindDefaultType,
                UsedDefault = !ok,
            };
        }
    }

    // ===================== Pure-forwarder shims =====================
    // DelSelfItem / GetUserItem / GiveUserItem / ClearBind: the case block does NO validation and sends
    // NO SysMsg itself; it only marshals the parsed params + self into a deferred core routine. The only
    // shim-provable facts are the forwarded-argument count and that self is always the first argument.
    //   DelSelfItem   sub_6C1C20(self, itemName, count)             -> 2 forwarded args
    //   GetUserItem   sub_6C22B0(self, charName, itemId)            -> 2 forwarded args
    //   GiveUserItem  sub_6C253C(self, charName, itemId, bindTime)  -> 3 forwarded args
    //   ClearBind     sub_6C6608(self, charName)                    -> 1 forwarded arg
    public sealed class GmItemForwardOutcome
    {
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred => true;
        /// <summary>Params forwarded to the core, excluding the always-present self receiver.</summary>
        public int ForwardedArgCount { get; init; }
        public bool ForwardsSelf => true;
        public bool ShimValidates => false;
        public bool ShimSendsSysMsg => false;
    }

    public static class NativeGmItemForwarders
    {
        public static GmItemForwardOutcome DelSelfItem() =>
            new() { CoreEa = NativeGmItemCommands.DelSelfItemCoreEa, ForwardedArgCount = 2 };

        public static GmItemForwardOutcome GetUserItem() =>
            new() { CoreEa = NativeGmItemCommands.GetUserItemCoreEa, ForwardedArgCount = 2 };

        public static GmItemForwardOutcome GiveUserItem() =>
            new() { CoreEa = NativeGmItemCommands.GiveUserItemCoreEa, ForwardedArgCount = 3 };

        public static GmItemForwardOutcome ClearBind() =>
            new() { CoreEa = NativeGmItemCommands.ClearBindCoreEa, ForwardedArgCount = 1 };
    }
}
