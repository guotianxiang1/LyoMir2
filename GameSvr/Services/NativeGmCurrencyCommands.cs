namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant model of the CURRENCY / LINGFU (灵符) / C2C GM command family (gm-inventory taxonomy
    // family 06), reversed 1:1 from the original Delphi M2Server. NOT wired into the live command table
    // — the live commands remain the C# handlers in GameSvr/Command/Commands/*Command.cs. This type only
    // *describes* the exact original contract so an AuditTools check can lock it and a future port can
    // reproduce it precisely.
    //
    // Same single-switch dispatcher as the ITEM/EQUIP and SKILL/HERO families (see
    // NativeGmItemCommands.cs / NativeGmSkillEquipCommands.cs):
    //   sub_6D7D68 -> sub_6BB2F8 -> sub_622820 (the switch); index from sub_621F28 (permission-gated:
    //   perm value at a2[1653], elevated 4->5 when off_7D617C is set, gate is `perm >= 3`);
    //   cmp esi, 0x2EE (750) ; ja default ; jmp jpt_622B15[esi*4] (table @0x00622B1C).
    //
    // TWO no-op sinks exist in this dispatcher (both silent: handled, no message, no forward):
    //   * def_622B15   @0x0062B648  — the switch DEFAULT label. All THREE family-06 no-ops land here.
    //   * empty-exit   @0x0062B64C  — a distinct do-nothing case body (used by MakeGuild/DelGuild/
    //                                 SetLTState/SetLTLimit in OTHER families). No family-06 command
    //                                 uses it; it is modelled here only to distinguish the two sinks.
    //
    // Evidence (IDA/Hex-Rays over the unpacked image m2full.i64, base 0x00400000), dump-only:
    //   * staging/update_clothes_4637_ida_work/disp_decomp.txt  — Hex-Rays of the switch (case bodies).
    //   * staging/update_clothes_4637_ida_work/big622820.txt     — raw disassembly of each case block.
    //   * gm_full_inventory_20260731.md family-06 roster         — exact name/idx/perm/handler-addr/help.
    //
    // SysMsg call is the virtual slot [self]+0xD4 (offset 212) invoked as (colour, text). Three colours
    // appear in this family:
    //   0xFFDB (-37)   info/confirm channel    0x38FF (14591) error channel    0xFCFF (-769) notice channel
    //
    // KEY FINDINGS (what the shim itself proves — core bodies are NOT in the dumps => CoreBodyDeferred):
    //   * AddLinFu (112) is a DIRECT self-write, not a forward: `add [self+0xBD8], normalize(count)`
    //     (raw: 0x006251C4 `add [eax+0BD8h], ebx`). self+0xBD8 is the DWORD lingfu field; the value and
    //     offset are shim-proven; the normalize (sub_4C7004) and refresh (sub_6B99E4) bodies are deferred.
    //   * ChgUserLinFu (220) and ChgUserLinFu2 (221) call the SAME core sub_6C78A8 with a discriminator
    //     0 (normal lingfu) vs 1 (extended/扩展 lingfu). Shim-proven.
    //   * TransferCredit (249) / SetNickLF (267) / SendYuanBaoText (334) are GUARDED forwards: a missing/
    //     zero argument sends an error SysMsg and does NOT call the core.
    //   * SetLingfu3 (260) / SetGloryPoint (274) resolve the target by name (sub_652784); target absent
    //     => error SysMsg; SetLingfu3 additionally treats count == -1 / empty as a QUERY (no write).
    //   * ServerSwitch (127) iterates 37 named switches (off_7D61FC) and specifically BLOCKS switch
    //     index 9 (圣殿灵符 / nick-lingfu) on close with an error SysMsg — matches the help note
    //     "但是不包括圣殿灵符的开启".
    //   * CreditCard (192) is a 4-subcommand handler (open/close/ClearMonLingfu/ClearAll) toggling the
    //     lingfu-usage flags at off_7D7038 with state-guarded SysMsgs.
    //   * reshuaGP (277) / loadEquipRecycle (440) reload a config file then SysMsg-confirm.
    //   * YbBuyLf (32) and TransferCredit (249) tail-call the 元宝 (yuanbao) settlement cores
    //     (sub_62E4A8 / sub_6E286C). The authoritative 元宝/金刚石 settlement is the EXTERNAL YBDB path
    //     (YBDB-6108, NO-GO) — modelled here ONLY as the GM-command contract (arg/gate/forward); the
    //     settlement core itself is CoreBodyDeferred and is NOT fabricated.
    //
    // C# DRIFT (live GameSvr/Command/Commands, NOT this model — flagged for the port; see the staging md):
    //   * Permission (GameCommand attribute 4th arg) vs native record perm:
    //       AddCoin          native 5  live attr 10 (+ in-code gate `m_btPermission < 6`)
    //       ReloadC2CItems   native 5  live attr 10
    //       SendYuanBaoText  native 4  live attr 10
    //       AddLinFu/ClearNickLinfu/CreditCard/SetNickLF  native 4  live attr 4  (MATCH)
    //   * AddCoinCommand.cs operates on m_nGamePoint ("代币"/token, calls GameGoldChanged) — the native
    //     @AddCoin (idx 204) help says 金币 (gold) and tail-calls the gold core sub_6C6B40. Different
    //     currency field. Live also adds a FindOtherServerUser fallback with no analog in the shim.
    // ------------------------------------------------------------------------------------------------

    public enum GmCurrencyCommand
    {
        YbBuyLf,
        CancelYBDeal,
        AddLinFu,
        ServerSwitch,
        AddCardValue,          // registered no-op (def_622B15)
        ClearNickLinfu,
        CreditCard,
        LesCoin,
        AddCoin,
        ChgUserLinFu,
        ChgUserLinFu2,
        ChguserGlory,          // exact table name "chguserGlory"
        GiveSdNickLinfu,
        TransferCredit,
        ReloadC2CItems,
        SetLingfu3,
        SellC2CGoods,          // registered no-op (def_622B15)
        SetNickLF,
        SetGloryPoint,
        ReshuaGP,              // exact table name "reshuaGP"
        SendYuanBaoText,
        C2cTest,               // exact table name "c2ctest"
        C2cQuery,              // exact table name "c2cQuery"
        C2cOperate,            // exact table name "c2cOperate"
        LoadEquipRecycle,      // exact table name "loadEquipRecycle"
        SetZillionCount,       // registered no-op (def_622B15)
    }

    /// <summary>Static command-table facts for one GM command (record name / +0x18 / +0x1C).</summary>
    public sealed class GmCurrencyCommandInfo
    {
        public GmCurrencyCommand Command { get; init; }
        /// <summary>Exact command name as stored in the table (case preserved).</summary>
        public string Name { get; init; }
        /// <summary>Dispatch index (static record +0x18, == runtime record +0x18 used by the switch).</summary>
        public int DispatchIndex { get; init; }
        /// <summary>Required GM permission (static record +0x1C). Original value; see the drift note.</summary>
        public int RequiredPermission { get; init; }
        /// <summary>True when sub_622820 has a real case; false when the index falls on def_622B15.</summary>
        public bool Implemented { get; init; }
        /// <summary>Address of the case/shim block (implemented) or the shared default label (0x0062B648).</summary>
        public uint CaseAddress { get; init; }
        /// <summary>Primary core/data the shim reaches (0 for no-op commands).</summary>
        public uint CoreEa { get; init; }
        /// <summary>True when the core body is not present in the dumps and is abstracted as an input.</summary>
        public bool CoreBodyDeferred { get; init; }
    }

    public static class NativeGmCurrencyCommands
    {
        // dispatcher constants (identical single switch as the item/skill families)
        public const uint DispatcherEa = 0x00622820;   // sub_622820
        public const uint IndexLookupEa = 0x00621F28;  // sub_621F28
        public const uint JumpTableEa = 0x00622B1C;    // jpt_622B15
        public const int SwitchMaxIndex = 750;         // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;  // def_622B15 (silent no-op) — the 3 family-06 no-ops
        public const uint EmptyExitCaseEa = 0x0062B64C;// distinct empty-exit sink (no family-06 cmd uses it)

        // SysMsg call + colours ([self]+0xD4 -> (colour, text))
        public const int SysMsgVtableOffset = 0xD4;    // call dword ptr [self]+0xD4
        public const int ColorInfo = 0xFFDB;           // -37   confirm/echo
        public const int ColorError = 0x38FF;          // 14591 error
        public const int ColorNotice = 0xFCFF;         // -769  notice (TransferCredit zero-amount refusal)

        // parse helpers
        public const uint StrToIntWithDefaultEa = 0x0040CA18; // sub_40CA18(str, default) -> int
        public const uint StrToIntAltEa = 0x0040CAB8;         // sub_40CAB8 (c2ctest variant parse)

        // AddLinFu (112) — shim-proven DIRECT self-write
        public const int LingfuSelfFieldOffset = 0xBD8;   // self+0xBD8 DWORD lingfu field (add [eax+0BD8h],ebx)
        public const int AddLinFuDefaultCount = 1;        // edx=1 default to sub_40CA18, then sub_4C7004(count,1)

        // ChgUserLinFu / ChgUserLinFu2 — shared core discriminator
        public const int LingfuKindNormal = 0;            // ChgUserLinFu  -> sub_6C78A8(0)
        public const int LingfuKindExtended = 1;          // ChgUserLinFu2 -> sub_6C78A8(1)

        // ServerSwitch (127) — 37 named switches, index 9 (nick-lingfu) excluded
        public const uint ServerSwitchTableEa = 0x007D61FC;  // off_7D61FC descriptor array
        public const int ServerSwitchCount = 37;             // n37 == 37 loop bound
        public const int ServerSwitchNickLingfuIndex = 9;    // n37 == 9 -> blocked on close (error SysMsg)

        // CreditCard (192) — lingfu-usage flag state
        public const uint CreditCardFlagStateEa = 0x007D7038; // off_7D7038 (bit1&0x10, bit2&8)
        public const int CreditCardSubcommandCount = 4;       // open / close / ClearMonLingfu / ClearAll

        // shared "resolve player by name" helper
        public const uint ResolveByNameEa = 0x00652784;  // sub_652784(name) -> TPlayObject*/null

        // core subroutines invoked by the shims — bodies NOT in the dumps (CoreBodyDeferred)
        public const uint YbBuyLfCoreEa = 0x0062E4A8;       // sub_62E4A8 (元宝->灵符 purchase; YBDB external settlement)
        public const uint CancelYBDealCoreEa = 0x006D731C;  // sub_6D731C
        public const uint AddLinFuNormalizeEa = 0x004C7004; // sub_4C7004 (normalize count)
        public const uint AddLinFuRefreshEa = 0x006B99E4;   // sub_6B99E4 (refresh/notify after write)
        public const uint ClearNickLinfuCoreEa = 0x006D3694;// sub_6D3694(0,0,0)
        public const uint CreditCardCoreEa = 0x00724208;    // sub_724208 (primary); also sub_724490
        public const uint CreditCardCore2Ea = 0x00724490;   // sub_724490
        public const uint LesCoinGoldCoreEa = 0x006C69EC;   // sub_6C69EC (gold decrement core)
        public const uint AddCoinGoldCoreEa = 0x006C6B40;   // sub_6C6B40 (gold increment core)
        public const uint ChgUserLinFuCoreEa = 0x006C78A8;  // sub_6C78A8 (shared normal/extended lingfu)
        public const uint ChguserGloryCoreEa = 0x006D2AD8;  // sub_6D2AD8(self, charName, gloryStr)
        public const uint GiveSdNickLinfuCoreEa = 0x006D7050;// sub_6D7050
        public const uint TransferCreditCoreEa = 0x006E286C;// sub_6E286C (元宝 transfer; YBDB external settlement)
        public const uint ReloadC2CItemsCoreEa = 0x0075516C;// sub_75516C (reload c2cForbidItems.txt)
        public const uint SetLingfu3CoreEa = 0x00714B48;    // sub_714B48 (set timed lingfu)
        public const uint SetNickLFCoreEa = 0x0062EAE4;     // sub_62EAE4(ratio, self)
        public const uint SetGloryPointCoreEa = 0x006E2134; // sub_6E2134 (set glory point)
        public const uint ReshuaGPCoreEa = 0x0063C1D4;      // sub_63C1D4 (reload GPForbidItems.txt)
        public const uint SendYuanBaoTextCoreEa = 0x006EA1A4;// sub_6EA1A4(1,0) (broadcast)
        public const uint C2cTestCoreEa = 0x006F228C;       // sub_6F228C (player.C2C_Cmd_Test)
        public const uint C2cQueryCoreEa = 0x006F1A50;      // sub_6F1A50
        public const uint C2cOperateCoreEa = 0x006F1844;    // sub_6F1844
        public const uint LoadEquipRecycleCoreEa = 0x00752648;// sub_752648 (reload ItemRecycleBase.ini)

        private static readonly GmCurrencyCommandInfo[] Registry =
        {
            new() { Command = GmCurrencyCommand.YbBuyLf,          Name = "YbBuyLf",          DispatchIndex = 32,  RequiredPermission = 0, Implemented = true,  CaseAddress = 0x00623B7A, CoreEa = YbBuyLfCoreEa,        CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.CancelYBDeal,     Name = "CancelYBDeal",     DispatchIndex = 96,  RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00624FF8, CoreEa = CancelYBDealCoreEa,   CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.AddLinFu,         Name = "AddLinFu",         DispatchIndex = 112, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x006251A8, CoreEa = AddLinFuNormalizeEa,  CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.ServerSwitch,     Name = "ServerSwitch",     DispatchIndex = 127, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625324, CoreEa = ServerSwitchTableEa,  CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.AddCardValue,     Name = "AddCardValue",     DispatchIndex = 148, RequiredPermission = 4, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
            new() { Command = GmCurrencyCommand.ClearNickLinfu,   Name = "ClearNickLinfu",   DispatchIndex = 164, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625CAB, CoreEa = ClearNickLinfuCoreEa, CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.CreditCard,       Name = "CreditCard",       DispatchIndex = 192, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625FFD, CoreEa = CreditCardCoreEa,     CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.LesCoin,          Name = "LesCoin",          DispatchIndex = 203, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x00625D63, CoreEa = LesCoinGoldCoreEa,    CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.AddCoin,          Name = "AddCoin",          DispatchIndex = 204, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x00625D76, CoreEa = AddCoinGoldCoreEa,    CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.ChgUserLinFu,     Name = "ChgUserLinFu",     DispatchIndex = 220, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x00625FAD, CoreEa = ChgUserLinFuCoreEa,   CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.ChgUserLinFu2,    Name = "ChgUserLinFu2",    DispatchIndex = 221, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x00625FCD, CoreEa = ChgUserLinFuCoreEa,   CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.ChguserGlory,     Name = "chguserGlory",     DispatchIndex = 226, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x006261B2, CoreEa = ChguserGloryCoreEa,   CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.GiveSdNickLinfu,  Name = "GiveSdNickLinfu",  DispatchIndex = 230, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x00626202, CoreEa = GiveSdNickLinfuCoreEa,CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.TransferCredit,   Name = "TransferCredit",   DispatchIndex = 249, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x006264A8, CoreEa = TransferCreditCoreEa, CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.ReloadC2CItems,   Name = "ReloadC2CItems",   DispatchIndex = 255, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x006264E3, CoreEa = ReloadC2CItemsCoreEa, CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.SetLingfu3,       Name = "SetLingfu3",       DispatchIndex = 260, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x006265FC, CoreEa = SetLingfu3CoreEa,     CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.SellC2CGoods,     Name = "SellC2CGoods",     DispatchIndex = 262, RequiredPermission = 5, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
            new() { Command = GmCurrencyCommand.SetNickLF,        Name = "SetNickLF",        DispatchIndex = 267, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x0062678B, CoreEa = SetNickLFCoreEa,      CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.SetGloryPoint,    Name = "SetGloryPoint",    DispatchIndex = 274, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x006269B8, CoreEa = SetGloryPointCoreEa,  CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.ReshuaGP,         Name = "reshuaGP",         DispatchIndex = 277, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00626B71, CoreEa = ReshuaGPCoreEa,       CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.SendYuanBaoText,  Name = "SendYuanBaoText",  DispatchIndex = 334, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00627D29, CoreEa = SendYuanBaoTextCoreEa,CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.C2cTest,          Name = "c2ctest",          DispatchIndex = 372, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x00628242, CoreEa = C2cTestCoreEa,        CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.C2cQuery,         Name = "c2cQuery",         DispatchIndex = 376, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x006282B0, CoreEa = C2cQueryCoreEa,       CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.C2cOperate,       Name = "c2cOperate",       DispatchIndex = 377, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x006282CF, CoreEa = C2cOperateCoreEa,     CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.LoadEquipRecycle, Name = "loadEquipRecycle", DispatchIndex = 440, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x0062765B, CoreEa = LoadEquipRecycleCoreEa,CoreBodyDeferred = true },
            new() { Command = GmCurrencyCommand.SetZillionCount,  Name = "SetZillionCount",  DispatchIndex = 478, RequiredPermission = 4, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false },
        };

        public static GmCurrencyCommandInfo Info(GmCurrencyCommand command)
        {
            foreach (var e in Registry)
                if (e.Command == command)
                    return e;
            throw new System.ArgumentOutOfRangeException(nameof(command));
        }

        public static System.Collections.Generic.IReadOnlyList<GmCurrencyCommandInfo> All => Registry;

        /// <summary>
        /// Contract for the registered-but-unimplemented commands (AddCardValue / SellC2CGoods /
        /// SetZillionCount): recognized by the table (valid index + permission), permission-gated, but the
        /// switch lands on def_622B15 @0x0062B648 — nothing is mutated and nothing is sent back.
        /// </summary>
        public static NativeGmDefaultNoOp EvaluateUnimplemented(GmCurrencyCommand command)
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

    // ===================== Pure-forwarder shims =====================
    // The case block does no shim-level guard and sends no SysMsg itself; it only marshals parsed params
    // (+ maybe self) into a deferred core. Shim-provable facts: which core, whether self is forwarded,
    // how many numeric params the shim itself parses (sub_40CA18/sub_40CAB8), and mutate/reload intent.
    //   YbBuyLf(32)         sub_62E4A8(count, self)          parses 1  forwards self  [元宝 settlement -> deferred]
    //   CancelYBDeal(96)    sub_6D731C(charName)             parses 0
    //   ClearNickLinfu(164) sub_6D3694(0, 0, 0)              parses 0  (3 literal-zero args)
    //   LesCoin(203)        sub_6C69EC(charName, count)      parses 0  [gold core]
    //   AddCoin(204)        sub_6C6B40(charName, count)      parses 0  [gold core]
    //   chguserGlory(226)   sub_6D2AD8(self, charName, str)  parses 0  forwards self
    //   GiveSdNickLinfu(230)sub_6D7050(charName, count)      parses 0
    //   ReloadC2CItems(255) sub_75516C()                     parses 0  reload (no shim SysMsg)
    //   c2ctest(372)        sub_6F228C(p1, p2)               parses 2 (sub_40CAB8)
    //   c2cQuery(376)       sub_6F1A50(start=-1 default)     parses 1 (default -1 -> query)
    //   c2cOperate(377)     sub_6F1844(orderIdx, param)      parses 2
    public sealed class CurrencyForwardOutcome
    {
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred => true;
        public bool ForwardsSelf { get; init; }
        /// <summary>Numeric args the shim itself parses via Str_ToInt before the forward.</summary>
        public int ParsesNumericArgs { get; init; }
        public bool ShimValidates => false;
        public bool ShimSendsSysMsg => false;
        /// <summary>True when the forward reloads a config or writes currency state (via the deferred core).</summary>
        public bool MutatesState { get; init; }
    }

    public static class NativeGmCurrencyForwarders
    {
        public static CurrencyForwardOutcome YbBuyLf() =>
            new() { CoreEa = NativeGmCurrencyCommands.YbBuyLfCoreEa, ForwardsSelf = true, ParsesNumericArgs = 1, MutatesState = true };

        public static CurrencyForwardOutcome CancelYBDeal() =>
            new() { CoreEa = NativeGmCurrencyCommands.CancelYBDealCoreEa, ForwardsSelf = false, ParsesNumericArgs = 0, MutatesState = true };

        public static CurrencyForwardOutcome ClearNickLinfu() =>
            new() { CoreEa = NativeGmCurrencyCommands.ClearNickLinfuCoreEa, ForwardsSelf = false, ParsesNumericArgs = 0, MutatesState = true };

        public static CurrencyForwardOutcome LesCoin() =>
            new() { CoreEa = NativeGmCurrencyCommands.LesCoinGoldCoreEa, ForwardsSelf = false, ParsesNumericArgs = 0, MutatesState = true };

        public static CurrencyForwardOutcome AddCoin() =>
            new() { CoreEa = NativeGmCurrencyCommands.AddCoinGoldCoreEa, ForwardsSelf = false, ParsesNumericArgs = 0, MutatesState = true };

        public static CurrencyForwardOutcome ChguserGlory() =>
            new() { CoreEa = NativeGmCurrencyCommands.ChguserGloryCoreEa, ForwardsSelf = true, ParsesNumericArgs = 0, MutatesState = true };

        public static CurrencyForwardOutcome GiveSdNickLinfu() =>
            new() { CoreEa = NativeGmCurrencyCommands.GiveSdNickLinfuCoreEa, ForwardsSelf = false, ParsesNumericArgs = 0, MutatesState = true };

        public static CurrencyForwardOutcome ReloadC2CItems() =>
            new() { CoreEa = NativeGmCurrencyCommands.ReloadC2CItemsCoreEa, ForwardsSelf = false, ParsesNumericArgs = 0, MutatesState = true };

        public static CurrencyForwardOutcome C2cTest() =>
            new() { CoreEa = NativeGmCurrencyCommands.C2cTestCoreEa, ForwardsSelf = false, ParsesNumericArgs = 2, MutatesState = true };

        public static CurrencyForwardOutcome C2cQuery() =>
            new() { CoreEa = NativeGmCurrencyCommands.C2cQueryCoreEa, ForwardsSelf = false, ParsesNumericArgs = 1, MutatesState = false };

        public static CurrencyForwardOutcome C2cOperate() =>
            new() { CoreEa = NativeGmCurrencyCommands.C2cOperateCoreEa, ForwardsSelf = false, ParsesNumericArgs = 2, MutatesState = true };
    }

    // ===================== AddLinFu (idx 112) — DIRECT self-write =====================
    // case @0x006251A8:  count = sub_40CA18(param, /*default*/1);  n = sub_4C7004(count, 1);
    //   `add [self+0xBD8], n`  (raw 0x006251C4);  sub_6B99E4(self)  [refresh].  No SysMsg.
    // The additive write and the 0xBD8 offset are shim-proven; the normalize/refresh cores are deferred.
    public sealed class AddLinFuOutcome
    {
        public bool WritesSelfField => true;
        public int SelfFieldOffset => NativeGmCurrencyCommands.LingfuSelfFieldOffset; // 0xBD8
        public bool IsAdditive => true;                 // add, not set
        public bool TargetsSelf => true;
        public int DefaultCount => NativeGmCurrencyCommands.AddLinFuDefaultCount;      // 1
        public uint NormalizeCoreEa => NativeGmCurrencyCommands.AddLinFuNormalizeEa;   // sub_4C7004
        public uint RefreshCoreEa => NativeGmCurrencyCommands.AddLinFuRefreshEa;       // sub_6B99E4
        public bool CoreBodyDeferred => true;           // normalize/refresh bodies not in dumps
        public bool MutatesState => true;
        public bool SendsSysMsg => false;               // silent on every path
    }

    public static class NativeGmAddLinFu
    {
        public static AddLinFuOutcome Evaluate() => new();
    }

    // ===================== ChgUserLinFu (220) / ChgUserLinFu2 (221) — shared core discriminator ==========
    // case @0x00625FAD:  sub_40CA18(); sub_6C78A8(0);   // normal lingfu
    // case @0x00625FCD:  sub_40CA18(); sub_6C78A8(1);   // extended (扩展) lingfu
    // Same core, discriminator is the only difference. Shim parses the count then forwards; no SysMsg.
    public sealed class LingfuChangeOutcome
    {
        public uint SharedCoreEa => NativeGmCurrencyCommands.ChgUserLinFuCoreEa; // sub_6C78A8
        public int Discriminator { get; init; }         // 0 normal, 1 extended
        public bool IsExtended => Discriminator == NativeGmCurrencyCommands.LingfuKindExtended;
        public bool ParsesCount => true;
        public bool CoreBodyDeferred => true;
        public bool ShimSendsSysMsg => false;
    }

    public static class NativeGmChgUserLinFu
    {
        public static LingfuChangeOutcome Normal() =>
            new() { Discriminator = NativeGmCurrencyCommands.LingfuKindNormal };

        public static LingfuChangeOutcome Extended() =>
            new() { Discriminator = NativeGmCurrencyCommands.LingfuKindExtended };
    }

    // ===================== Guarded forwards: TransferCredit / SetNickLF / SendYuanBaoText =====================
    // A missing/zero argument sends an error SysMsg and does NOT reach the core.
    //   TransferCredit(249): amount = sub_40CA18();  amount != 0 -> sub_6E286C(amount, ptid) [元宝 settlement];
    //                        amount == 0 -> SysMsg(0xFCFF, "amount required"), no forward.
    //   SetNickLF(267):      param present -> ratio = sub_40CA18(); sub_62EAE4(ratio, self);
    //                        param absent  -> SysMsg(0x38FF, "ratio required"), no forward.
    //   SendYuanBaoText(334):content present -> sub_6EA1A4(1,0) [broadcast];
    //                        content absent  -> SysMsg(0x38FF, "content required"), no forward.
    public sealed class GuardedForwardOutcome
    {
        public bool GuardSatisfied { get; init; }
        public bool CallsCore => GuardSatisfied;
        public bool SendsErrorSysMsg => !GuardSatisfied;
        public uint CoreEa { get; init; }               // reached only when GuardSatisfied
        public int ErrorColor { get; init; }            // colour used on the refusal path
        public bool CoreBodyDeferred => true;
        /// <summary>True when the satisfied path changes persistent state (false for the broadcast-only text).</summary>
        public bool MutatesStateWhenSatisfied { get; init; }
    }

    public static class NativeGmTransferCredit
    {
        // guardSatisfied == (parsed ±amount != 0)
        public static GuardedForwardOutcome Evaluate(bool amountNonZero) =>
            new()
            {
                GuardSatisfied = amountNonZero,
                CoreEa = NativeGmCurrencyCommands.TransferCreditCoreEa,
                ErrorColor = NativeGmCurrencyCommands.ColorNotice,   // 0xFCFF
                MutatesStateWhenSatisfied = true,
            };
    }

    public static class NativeGmSetNickLF
    {
        // guardSatisfied == (ratio param present)
        public static GuardedForwardOutcome Evaluate(bool ratioPresent) =>
            new()
            {
                GuardSatisfied = ratioPresent,
                CoreEa = NativeGmCurrencyCommands.SetNickLFCoreEa,
                ErrorColor = NativeGmCurrencyCommands.ColorError,    // 0x38FF
                MutatesStateWhenSatisfied = true,
            };
    }

    public static class NativeGmSendYuanBaoText
    {
        // guardSatisfied == (message content present); broadcast only -> no persistent mutation
        public static GuardedForwardOutcome Evaluate(bool contentPresent) =>
            new()
            {
                GuardSatisfied = contentPresent,
                CoreEa = NativeGmCurrencyCommands.SendYuanBaoTextCoreEa,
                ErrorColor = NativeGmCurrencyCommands.ColorError,    // 0x38FF
                MutatesStateWhenSatisfied = false,
            };
    }

    // ===================== Target-resolve: SetLingfu3 (260) / SetGloryPoint (274) =====================
    // Both resolve the target player by name via sub_652784; target absent -> error SysMsg (0x38FF).
    //   SetLingfu3(260): found -> count = sub_40CA18(); count == -1 (or empty) -> QUERY (confirm self only,
    //                    no write); count != -1 -> sub_714B48 (set timed lingfu) + notify target + confirm
    //                    self (both 0xFFDB).
    //   SetGloryPoint(274): found -> sub_6E2134 (set glory) then confirm self (0xFFDB); absent -> error.
    public enum TargetResolveBranch
    {
        TargetNotFound,   // error SysMsg, no core
        Query,            // SetLingfu3 only: count == -1 / empty, no write
        Set,              // core called, target mutated
    }

    public sealed class TargetResolveOutcome
    {
        public TargetResolveBranch Branch { get; init; }
        public uint ResolveHelperEa => NativeGmCurrencyCommands.ResolveByNameEa; // sub_652784
        public uint SetCoreEa { get; init; }
        public bool CallsSetCore => Branch == TargetResolveBranch.Set;
        public bool SendsErrorSysMsg => Branch == TargetResolveBranch.TargetNotFound;
        public bool SendsConfirmSysMsg => Branch != TargetResolveBranch.TargetNotFound;
        public bool MutatesState => Branch == TargetResolveBranch.Set;
        public bool CoreBodyDeferred => true;
    }

    public static class NativeGmSetLingfu3
    {
        // targetFound false -> not found; else countIsQuery (count == -1 / empty) selects query vs set.
        public static TargetResolveOutcome Evaluate(bool targetFound, bool countIsQuery)
        {
            TargetResolveBranch branch = !targetFound
                ? TargetResolveBranch.TargetNotFound
                : (countIsQuery ? TargetResolveBranch.Query : TargetResolveBranch.Set);
            return new TargetResolveOutcome { Branch = branch, SetCoreEa = NativeGmCurrencyCommands.SetLingfu3CoreEa };
        }
    }

    public static class NativeGmSetGloryPoint
    {
        // no query mode: found -> set; absent -> error.
        public static TargetResolveOutcome Evaluate(bool targetFound) =>
            new()
            {
                Branch = targetFound ? TargetResolveBranch.Set : TargetResolveBranch.TargetNotFound,
                SetCoreEa = NativeGmCurrencyCommands.SetGloryPointCoreEa,
            };
    }

    // ===================== Reload+confirm: reshuaGP (277) / loadEquipRecycle (440) =====================
    //   reshuaGP(277):         if sub_63C1D4() -> SysMsg(0xFFDB, ok)   (confirm on success only)
    //   loadEquipRecycle(440): sub_752648(); SysMsg(0xFFDB, ok)        (always confirms)
    public sealed class ReloadConfirmOutcome
    {
        public uint ReloadCoreEa { get; init; }
        public bool CoreBodyDeferred => true;
        public bool MutatesState => true;               // reloads a global config table
        public bool SendsSysMsg => true;
        public int MessageColor => NativeGmCurrencyCommands.ColorInfo; // 0xFFDB
        /// <summary>reshuaGP confirms only on a successful reload; loadEquipRecycle always confirms.</summary>
        public bool ConfirmsOnlyOnSuccess { get; init; }
    }

    public static class NativeGmReloadConfirm
    {
        public static ReloadConfirmOutcome ReshuaGP() =>
            new() { ReloadCoreEa = NativeGmCurrencyCommands.ReshuaGPCoreEa, ConfirmsOnlyOnSuccess = true };

        public static ReloadConfirmOutcome LoadEquipRecycle() =>
            new() { ReloadCoreEa = NativeGmCurrencyCommands.LoadEquipRecycleCoreEa, ConfirmsOnlyOnSuccess = false };
    }

    // ===================== ServerSwitch (idx 127) =====================
    // case @0x00625324: iterate 37 named switch descriptors (off_7D61FC); match param name; when found at
    //   index n37, apply open/close (LABEL_477/LABEL_479). SPECIAL: closing switch index 9 (圣殿灵符 /
    //   nick-lingfu) is BLOCKED with SysMsg(0x38FF) — matches help "但是不包括圣殿灵符的开启". Name not
    //   found among the 37 -> silent exit.
    public sealed class ServerSwitchOutcome
    {
        public uint SwitchTableEa => NativeGmCurrencyCommands.ServerSwitchTableEa; // off_7D61FC
        public int SwitchCount => NativeGmCurrencyCommands.ServerSwitchCount;       // 37
        public int ExcludedSwitchIndex => NativeGmCurrencyCommands.ServerSwitchNickLingfuIndex; // 9
        public bool TogglesGlobalSwitch => true;
        public bool MutatesState => true;
        public int ExcludedErrorColor => NativeGmCurrencyCommands.ColorError;       // 0x38FF on the blocked path
        public bool CoreBodyDeferred => true;   // per-switch apply detail not in dumps
    }

    public static class NativeGmServerSwitch
    {
        public static ServerSwitchOutcome Evaluate() => new();
    }

    // ===================== CreditCard (idx 192) =====================
    // case @0x00625FFD: 4-subcommand handler (open / close / ClearMonLingfu / ClearAll) selected by
    //   sub_40BD78 string compares; gates on the lingfu-usage flag bits at off_7D7038 (byte1 & 0x10,
    //   byte2 & 8); toggles those flags / clears the monthly + extended lingfu data via sub_724208 /
    //   sub_724490 (+ sub_713890 refresh); sends state SysMsgs (0x38FF on wrong-state).
    public sealed class CreditCardOutcome
    {
        public int SubcommandCount => NativeGmCurrencyCommands.CreditCardSubcommandCount; // 4
        public uint FlagStateEa => NativeGmCurrencyCommands.CreditCardFlagStateEa;        // off_7D7038
        public uint PrimaryCoreEa => NativeGmCurrencyCommands.CreditCardCoreEa;           // sub_724208
        public uint SecondaryCoreEa => NativeGmCurrencyCommands.CreditCardCore2Ea;        // sub_724490
        public bool MutatesState => true;
        public bool SendsStateSysMsg => true;
        public bool CoreBodyDeferred => true;
    }

    public static class NativeGmCreditCard
    {
        public static CreditCardOutcome Evaluate() => new();
    }
}
