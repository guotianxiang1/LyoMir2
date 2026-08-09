namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant model — SUPPLEMENT to NativeGmItemCommands.cs.
    //
    // NativeGmItemCommands.cs already models one slice of the ITEM/EQUIP GM "@" command family
    // (ShopItem/ReLoadPItem/DelSelfItem/GetUserItem/GiveUserItem/DecDuarg/cmdBind/ClearBind + no-ops).
    // This file adds the ITEM/EQUIP commands that are present in the native command table but were NOT
    // covered there — so together the two files describe the whole family without overlap. Nothing here
    // is wired into the live command table; the live handlers remain the fail-closed stubs in
    // GameSvr/Command/Commands/*Command.cs. This type only *describes* the exact original contract so an
    // AuditTools check can lock it, and so a future port can reproduce it precisely.
    //
    // Same single-switch dispatcher as the rest of the family (see NativeGmSkillEquipCommands.cs):
    //   sub_6D7D68 -> sub_6BB2F8 -> sub_622820 (the switch); index from sub_621F28 (permission-gated);
    //   cmp esi, 0x2EE (750) ; ja default ; jmp jpt_622B15[esi*4] (table @0x00622B1C).
    //   Default def_622B15 @0x0062B648: silent no-op. Shared string-cleanup epilogue loc_62B64C
    //   @0x0062B64C returns the "handled" flag; implemented cases return 1, the default returns 0.
    //   Confirmed silent: the caller sub_6BB2F8 only reacts to a not-handled '@' when the 2nd char is
    //   one of '!' '#' '$' '*'; a plain unrecognized / no-op "@Cmd" produces NO message.
    //
    // sub_621F28 permission gate (see AuditTools note): record[+0x1C] is written to the out-param and
    //   the record[+0x18] index is returned ONLY when callerPerm >= record[+0x1C]; otherwise it returns
    //   0. Index 0 == jump-table slot 0 == def_622B15, so an under-privileged (or unknown) command is a
    //   SILENT no-op, not an error message.
    //
    // Evidence (IDA/Hex-Rays over the unpacked image M2Server_unpacked_fixed.exe, base 0x00400000):
    //   * ida_award_case584_command_registry_20260720.txt — the command table @0x007B4654 (430 records,
    //     stride 0x78) with per-record name / +0x18 dispatch index / +0x1C permission. Values below are
    //     taken verbatim from that dump and cross-checked against the disassembly case labels.
    //   * big622820.txt — raw disassembly of every case block (the "shim").
    //   * disp_decomp.txt — Hex-Rays of the same switch (case bodies).
    //
    // KEY FINDING (identical to the sibling file): every implemented ITEM/EQUIP case here is a THIN SHIM
    // that marshals the parsed params + self and tail-calls a core subroutine whose body is NOT present
    // in the current dumps. Per the fail-closed rule those cores are abstracted as inputs
    // (CoreBodyDeferred=true); this model captures only what the shim itself proves (forwarded-arg count,
    // any shim-level parse/guard, whether the shim sends a SysMsg). It does NOT invent core ladders.
    //
    // Per-command facts (Name = exact table spelling; Index = record+0x18; Perm = record+0x1C):
    //   IMPLEMENTED (distinct case block in sub_622820 -> deferred core):
    //     StorageItem     idx 167 perm 4  case@0x00625CF2  sub_62E730(ctx, self)                 [0 parsed args]
    //     GetBackItem     idx 168 perm 4  case@0x00625D02  sub_62E7CC(ctx, self, p0, p1)         [2 parsed args]
    //     LookUserItemId  idx 191 perm 4  case@0x00625FED  sub_6D07C4(self, p0=itemId)           [1 parsed arg]
    //     ChgEquipLevel   idx 229 perm 5  case@0x006261EF  sub_6D6DEC(self, p0=name, p1=slot)    [2 parsed args]
    //     SetItemTimeOut  idx 434 perm 4  case@0x00627714  sub_6BD8F8(self, token(p1), int(p0))  [shim parses]
    //   REGISTERED BUT UNIMPLEMENTED (index maps to def_622B15 / silent no-op):
    //     SetEquipComposeAbil idx 499 perm 5  help "设置装备合成属性"  (distinct from the skill/equip peer's
    //         SetEquipComposelv idx 498 / ClearEquipCompose idx 500, which are also no-ops)
    //
    // SetItemTimeOut shim detail (case 434): tokenizes param1 by ' ' via sub_4C6AEC, parses param0 to an
    //   int via sub_40CAB8(p0,0,0), then sub_6BD8F8(self, token, intValue). The tokenizer/int helpers are
    //   proven by the shim; the effect (what the timeout is applied to) lives in the deferred core.
    //
    // C# STUB DRIFT (live GameSvr/Command/Commands, NOT this model — flagged for the port):
    //   * ChgEquipLevelCommand.cs / GetBackItemCommand.cs are fail-closed NativeCommandFailure.Report
    //     stubs; the report itself is a SysMsg. The native shims send NO SysMsg on the dispatch path, so
    //     the stubs currently emit MORE than the original.
    //   * StorageItemCommand.cs help advertises an optional target name; the native shim forwards NO
    //     parsed arg (operates on self/context) — any name targeting would have to live in the deferred
    //     core, so the stub's name argument is not proven by the binary.
    //   * SetEquipComposeAbilCommand.cs is a fail-closed stub that sends ShowHelp / a failure report; the
    //     native command is a SILENT no-op (index 499 -> def_622B15). The stub emits MORE than original.
    //   * All live stubs use RequiredPermission 10; the native records use 4/5 (see registry below).
    // ------------------------------------------------------------------------------------------------

    public enum GmItemSupCommand
    {
        StorageItem,
        GetBackItem,
        LookUserItemId,
        ChgEquipLevel,
        SetItemTimeOut,
        SetEquipComposeAbil,   // registered no-op
    }

    /// <summary>Static command-table facts for one GM command (record name / +0x18 / +0x1C).</summary>
    public sealed class GmItemSupCommandInfo
    {
        public GmItemSupCommand Command { get; init; }
        /// <summary>Exact command name as stored in the table (case preserved).</summary>
        public string Name { get; init; }
        /// <summary>Dispatch index (record +0x18, == runtime record +0x18 used by the switch).</summary>
        public int DispatchIndex { get; init; }
        /// <summary>Required GM permission (record +0x1C). Original value; the C# stubs use 10.</summary>
        public int RequiredPermission { get; init; }
        /// <summary>True when sub_622820 has a real case; false when the index falls on def_622B15.</summary>
        public bool Implemented { get; init; }
        /// <summary>Address of the case/shim block (implemented) or the shared default label (0x0062B648).</summary>
        public uint CaseAddress { get; init; }
        /// <summary>Core subroutine the shim tail-calls (0 for no-op commands).</summary>
        public uint CoreEa { get; init; }
        /// <summary>True when the core body is not present in the dumps and is abstracted as an input.</summary>
        public bool CoreBodyDeferred { get; init; }
        /// <summary>Number of parsed command params forwarded to the core (self excluded).</summary>
        public int ForwardedArgCount { get; init; }
    }

    public static class NativeGmItemCommandsSupplement
    {
        // dispatcher constants (identical single switch as the rest of the GM "@" family)
        public const uint DispatcherEa = 0x00622820;   // sub_622820
        public const uint IndexLookupEa = 0x00621F28;  // sub_621F28 (permission gate)
        public const uint JumpTableEa = 0x00622B1C;    // jpt_622B15
        public const int SwitchMaxIndex = 750;         // cmp esi, 0x2EE
        public const uint DefaultCaseEa = 0x0062B648;  // def_622B15 (silent no-op)
        public const uint EpilogueEa = 0x0062B64C;     // loc_62B64C (string cleanup, return handled)

        // command record table (source of the index/perm facts)
        public const uint CommandTableEa = 0x007B4654;      // 430 records
        public const int CommandTableRecordStride = 0x78;   // 120 bytes per record
        public const int RecordIndexFieldOffset = 0x18;     // record+0x18 = dispatch index
        public const int RecordPermFieldOffset = 0x1C;      // record+0x1C = required permission

        // core subroutines invoked by the shims — bodies NOT in the dumps (CoreBodyDeferred)
        public const uint StorageItemCoreEa = 0x0062E730;    // sub_62E730
        public const uint GetBackItemCoreEa = 0x0062E7CC;    // sub_62E7CC
        public const uint LookUserItemIdCoreEa = 0x006D07C4; // sub_6D07C4
        public const uint ChgEquipLevelCoreEa = 0x006D6DEC;  // sub_6D6DEC
        public const uint SetItemTimeOutCoreEa = 0x006BD8F8; // sub_6BD8F8

        // SetItemTimeOut shim-level parse helpers (proven by the case block)
        public const uint StrToIntEa = 0x0040CAB8;          // sub_40CAB8(str,0,0) -> int
        public const uint TokenizeEa = 0x004C6AEC;          // sub_4C6AEC(src,&sep,delim,&dst) tokenizer
        public const int SetItemTimeOutDelimiter = 0x20;    // ' ' (cl = 0x20)

        private static readonly GmItemSupCommandInfo[] Registry =
        {
            new() { Command = GmItemSupCommand.StorageItem,    Name = "StorageItem",    DispatchIndex = 167, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625CF2, CoreEa = StorageItemCoreEa,    CoreBodyDeferred = true, ForwardedArgCount = 0 },
            new() { Command = GmItemSupCommand.GetBackItem,    Name = "GetBackItem",    DispatchIndex = 168, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625D02, CoreEa = GetBackItemCoreEa,    CoreBodyDeferred = true, ForwardedArgCount = 2 },
            new() { Command = GmItemSupCommand.LookUserItemId, Name = "LookUserItemId", DispatchIndex = 191, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00625FED, CoreEa = LookUserItemIdCoreEa, CoreBodyDeferred = true, ForwardedArgCount = 1 },
            new() { Command = GmItemSupCommand.ChgEquipLevel,  Name = "ChgEquipLevel",  DispatchIndex = 229, RequiredPermission = 5, Implemented = true,  CaseAddress = 0x006261EF, CoreEa = ChgEquipLevelCoreEa,  CoreBodyDeferred = true, ForwardedArgCount = 2 },
            new() { Command = GmItemSupCommand.SetItemTimeOut, Name = "SetItemTimeOut", DispatchIndex = 434, RequiredPermission = 4, Implemented = true,  CaseAddress = 0x00627714, CoreEa = SetItemTimeOutCoreEa, CoreBodyDeferred = true, ForwardedArgCount = 2 },
            new() { Command = GmItemSupCommand.SetEquipComposeAbil, Name = "SetEquipComposeAbil", DispatchIndex = 499, RequiredPermission = 5, Implemented = false, CaseAddress = DefaultCaseEa, CoreEa = 0, CoreBodyDeferred = false, ForwardedArgCount = 0 },
        };

        public static GmItemSupCommandInfo Info(GmItemSupCommand command)
        {
            foreach (var e in Registry)
                if (e.Command == command)
                    return e;
            throw new System.ArgumentOutOfRangeException(nameof(command));
        }

        public static System.Collections.Generic.IReadOnlyList<GmItemSupCommandInfo> All => Registry;

        /// <summary>Jump-table slot address for a dispatch index: jpt_622B15 + index*4.</summary>
        public static uint CasePtr(int dispatchIndex) => JumpTableEa + (uint)dispatchIndex * 4;

        /// <summary>
        /// sub_621F28 gate: the record index is returned (and the command dispatches) only when the
        /// caller's permission is at least the record's required permission; otherwise it returns 0,
        /// which routes to def_622B15 (a silent no-op — NOT a permission-denied message).
        /// </summary>
        public static bool PermitsDispatch(int callerPermission, int requiredPermission) =>
            callerPermission >= requiredPermission;

        /// <summary>
        /// Contract for the registered-but-unimplemented commands: recognized by the table (valid index
        /// + permission), permission-gated, but the switch lands on def_622B15 — nothing is mutated and
        /// nothing is sent back. Faithful behaviour is a silent no-op. Reuses the shared
        /// <see cref="NativeGmDefaultNoOp"/> contract type declared by NativeGmSkillEquipCommands.
        /// </summary>
        public static NativeGmDefaultNoOp EvaluateUnimplemented(GmItemSupCommand command)
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
    // StorageItem / GetBackItem / LookUserItemId / ChgEquipLevel: the case block does NO validation and
    // sends NO SysMsg itself; it only marshals the parsed params + self into a deferred core routine.
    // The only shim-provable facts are the forwarded-argument count and that self is always forwarded.
    //   StorageItem     sub_62E730(ctx, self)               -> 0 parsed args (self/context only)
    //   GetBackItem     sub_62E7CC(ctx, self, p0, p1)       -> 2 parsed args (p0 = target name, p1)
    //   LookUserItemId  sub_6D07C4(self, p0)                -> 1 parsed arg  (p0 = item id)
    //   ChgEquipLevel   sub_6D6DEC(self, p0, p1)            -> 2 parsed args (p0 = name, p1 = equip slot)
    public sealed class GmItemSupForwardOutcome
    {
        public uint CoreEa { get; init; }
        public bool CoreBodyDeferred => true;
        /// <summary>Params forwarded to the core, excluding the always-present self receiver.</summary>
        public int ForwardedArgCount { get; init; }
        public bool ForwardsSelf => true;
        public bool ShimValidates => false;
        public bool ShimSendsSysMsg => false;
    }

    public static class NativeGmItemSupForwarders
    {
        public static GmItemSupForwardOutcome StorageItem() =>
            new() { CoreEa = NativeGmItemCommandsSupplement.StorageItemCoreEa, ForwardedArgCount = 0 };

        public static GmItemSupForwardOutcome GetBackItem() =>
            new() { CoreEa = NativeGmItemCommandsSupplement.GetBackItemCoreEa, ForwardedArgCount = 2 };

        public static GmItemSupForwardOutcome LookUserItemId() =>
            new() { CoreEa = NativeGmItemCommandsSupplement.LookUserItemIdCoreEa, ForwardedArgCount = 1 };

        public static GmItemSupForwardOutcome ChgEquipLevel() =>
            new() { CoreEa = NativeGmItemCommandsSupplement.ChgEquipLevelCoreEa, ForwardedArgCount = 2 };
    }

    // ===================== SetItemTimeOut (idx 434) =====================
    // case @0x00627714: token = sub_4C6AEC(...,' ')  [tokenize param1 by space];
    //   value = sub_40CAB8(param0, 0, 0)  [Str_ToInt];  sub_6BD8F8(self, token, value).
    //   The tokenizer + int parse are shim-level facts; what the timeout is applied to is in the
    //   deferred core sub_6BD8F8. The shim itself sends no SysMsg.
    public sealed class SetItemTimeOutOutcome
    {
        public bool TokenizesArg => true;                                   // param1 split by ' '
        public int Delimiter => NativeGmItemCommandsSupplement.SetItemTimeOutDelimiter;
        public bool ParsesIntArg => true;                                   // param0 -> int value
        public bool CallsCore => true;                                      // sub_6BD8F8(self, token, value)
        public uint CoreEa => NativeGmItemCommandsSupplement.SetItemTimeOutCoreEa;
        public bool CoreBodyDeferred => true;
        public bool ForwardsSelf => true;
        public bool ShimSendsSysMsg => false;
    }

    public static class NativeGmSetItemTimeOut
    {
        public static SetItemTimeOutOutcome Evaluate() => new();
    }
}
