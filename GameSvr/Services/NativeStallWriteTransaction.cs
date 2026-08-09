namespace GameSvr
{
    // Dormant model of the native player-STALL (摆摊 / personal booth) write ops, which are currently
    // fail-closed stubs in C# ("摆摊功能当前不可用", TPlayObject.Message.cs). Hex-Rays verified against
    // M2Server (image base 0x00400000). Not wired / performs no writes.
    //
    // Every stall handler (0x006E79xx-0x006E7Dxx) shares the same shape:
    //   1. common feature gate sub_6E78D4 @0x006E78D4 (stall system enabled). If it returns false the
    //      handler returns WITHOUT sending anything (silent drop) -> modeled as NoResponse.
    //   2. call the stall-manager method (sub_61xxxx) which returns a result code.
    //   3. SendDefMessage(ident, wParam=result) ONLY when result != 0; result == 0 sends nothing
    //      (manager handled it / no def-message) -> NoResponse.
    //
    // Covered ops (handler -> manager, exact ladders):
    //   4419 SetTimeLevel  sub_6E7938 -> sub_61D294 @0x0061D294 : record/precheck fail 0(silent) / 1 success.
    //   4420 SetName       sub_6E7984 -> sub_61D3E0 @0x0061D3E0 : no record 0(silent) / running -3 /
    //                                    name >30 chars -1 / empty name -2 / 1 success.
    //   4421 AddItem       sub_6E7CF4 -> sub_61BC7C @0x0061BC7C : no stall -2 / item not found -3 /
    //                                    item locked -5 / count/qty invalid -4 / add failed -1 / 1 success.
    //   4422 DelItem       sub_6E7D4C -> sub_61BECC @0x0061BECC : fail -1 / 1 success.
    //   4424 StartStall    sub_6E7C38 -> sub_61D4F0 @0x0061D4F0 : map disallows stall -9 (sub_7684A0) /
    //                                    no record -4 / precheck A -7 (sub_61F3D8) / precheck B -8
    //                                    (sub_61EE88) / else start core result (>0 success; abstract).
    //   4425 PauseStall    sub_6E7CB0 -> sub_61D640 @0x0061D640 -> sub_61E02C @0x0061E02C : no caller
    //                                    record -1 / owner-name unresolved (sub_656C14) -1 / else close
    //                                    core sub_61A36C+sub_61FEAC (>0 success; sets rec[+0x40]=2).
    //   4426 BuyItem       sub_6E7A04 -> sub_61C8E0 -> sub_61E8EC @0x0061E8EC : disabled -1 (sub_7481F4) /
    //                                    target inactive -5 / item gone -4 (sub_61EE34) / insufficient
    //                                    money -2 (type1 bal[+0x760]) or -3 (type0 gold[+0x15C]) /
    //                                    bad qty -6 / else finalize (type1 -> 0 async; type0 -> sub_61E0C8).
    //   4467 MessageStall  sub_6E7A64 -> sub_61C80C @0x0061C80C : payload <64 bytes NoResponse /
    //                                    no stall -1 / message not allowed -2 (sub_61FCE4) / 1 success.
    //
    // Buy (sub_61E8EC) and pause (sub_61E02C) ladders are now CONCRETE (idat 2026-08-01); only their
    // deepest leaves stay as passthrough inputs: BuyFinalizeResult (type1 async = 0 / type0 sub_61E0C8)
    // and PauseCloseResult (sub_61A36C+sub_61FEAC). The start-core success value (sub_61D4F0 tail,
    // internals sub_61D6B8/sub_61A4C0/sub_61DFC4) is still abstracted as StartCoreResult. All are
    // integer inputs where 0 => NoResponse per the send-if-nonzero rule.

    public enum NativeStallOp
    {
        SetTimeLevel = 4419,
        SetName = 4420,
        AddItem = 4421,
        DelItem = 4422,
        StartStall = 4424,
        PauseStall = 4425,
        BuyItem = 4426,
        MessageStall = 4467,
    }

    public sealed class NativeStallContext
    {
        /// <summary>Common gate sub_6E78D4 (stall system enabled). False => handler sends nothing.</summary>
        public bool FeatureEnabled { get; init; } = true;

        // 4419 / 4420 / 4421 / 4422 / 4426 / 4467: the caller's stall record resolved (sub_49F5F4).
        public bool StallRecordFound { get; init; }
        /// <summary>Stall is currently running/active (*(record+0x40) == 1).</summary>
        public bool StallRunning { get; init; }

        // 4419 SetTimeLevel — sub_61D294 -> sub_61D6B8 affordability gate (codec-fidelity 2026-08-01, one
        // serial idat). CHECK-ONLY: compares duration*fee vs GoldNum, never deducts (Δ=0). Populated by
        // NativeStallBoothSetup from the resolved record + the selected StallTradConf tier + player GoldNum.
        public bool SetTimeLevelRecordCreated { get; init; }      // get-or-create ok; else 0 (silent edge)
        public bool SetTimeLevelConfigPresent { get; init; }      // tier config found; else -3
        public bool SetTimeLevelDurationWithinMax { get; init; }  // duration <= cfg maxDur (cfg+0x08); else -2
        public bool SetTimeLevelNameGateOk { get; init; } = true; // name-length gate (cfg+0x10); passes for valid config; else -3
        public bool SetTimeLevelCanAfford { get; init; }          // duration*fee(cfg+0x20) <= GoldNum; else -1

        // 4420 SetName
        public bool NameTooLong { get; init; }   // > 30 chars
        public bool NameEmpty { get; init; }

        // 4421 AddItem
        public bool AddItemFound { get; init; }   // sub_73CF08
        public bool AddItemLocked { get; init; }  // sub_784710 -> -5
        public bool AddCountValid { get; init; }  // stackable count == req / splittable, or qty==1
        public bool AddSucceeded { get; init; }   // sub_61DCF0 == 1

        // 4422 DelItem
        public bool DelSucceeded { get; init; }   // stall+item found and sub_74DAE4 removed it

        // 4424 StartStall
        public bool MapAllowsStall { get; init; } = true; // sub_7684A0 -> false => -9
        public bool StartPrecheckA { get; init; } = true; // sub_61F3D8 -> false => -7
        public bool StartPrecheckB { get; init; } = true; // sub_61EE88 -> false => -8
        public int StartCoreResult { get; init; }         // sub_61D4F0 tail (>0 success); abstract

        // 4425 PauseStall  (sub_6E7CB0 -> sub_61D640 -> sub_61E02C; idat-reversed 2026-08-01)
        public bool PauseOwnerResolved { get; init; }     // sub_61E02C: sub_656C14(rec ownername) resolved; else -1
        public int PauseCloseResult { get; init; }        // sub_61A36C+sub_61FEAC close core (>0 success; sets rec[+0x40]=2); deepest-abstract

        // 4426 BuyItem  (sub_6E7A04 -> sub_61C8E0 -> sub_61E8EC; idat-reversed 2026-08-01)
        public bool BuyEnabled { get; init; } = true;     // sub_7481F4 -> false => -1
        public bool BuyTargetStallActive { get; init; }   // sub_61C8E0: target found and *(target+0x40)==1; else -5
        public bool BuyItemStillPresent { get; init; }    // sub_61E8EC: sub_61EE34 != 0; else -4
        public int BuyMoneyType { get; init; }            // stallitem[+0xF4]; 1 => balance[+0x760], 0 => gold[+0x15C]
        public bool BuyerHasEnoughMoney { get; init; }    // buyer balance >= count*unitPrice; else -2 (type1) / -3 (type0)
        public bool BuyQtyValid { get; init; }            // stackable(stdmode 7): itemCount>=qty; else qty==1; else -6
        public int BuyFinalizeResult { get; init; }       // success: type1 -> 0 (async, silent); type0 -> sub_61E0C8 result; deepest-abstract

        // 4467 MessageStall
        public bool MessagePayloadValid { get; init; } = true; // >= 64 bytes
        public bool MessageAllowed { get; init; }              // sub_61FCE4
    }

    public static class NativeStallWriteTransaction
    {
        /// <summary>Sentinel: the handler returned without calling SendDefMessage (silent).</summary>
        public const int NoResponse = int.MinValue;

        public const int VtblSendDefMessage = 0x250;

        /// <summary>Raw result code sent as SendDefMessage wParam, or NoResponse when nothing is sent.</summary>
        public static int Evaluate(NativeStallOp op, NativeStallContext c)
        {
            if (!c.FeatureEnabled)
                return NoResponse; // sub_6E78D4 gate closed

            int code = op switch
            {
                NativeStallOp.SetTimeLevel => SetTimeLevel(c),
                NativeStallOp.SetName => SetName(c),
                NativeStallOp.AddItem => AddItem(c),
                NativeStallOp.DelItem => DelItem(c),
                NativeStallOp.StartStall => StartStall(c),
                NativeStallOp.PauseStall => PauseStall(c),
                NativeStallOp.BuyItem => BuyItem(c),
                NativeStallOp.MessageStall => MessageStall(c),
                _ => 0,
            };
            // send-if-nonzero: result 0 means the handler sends no def-message.
            return code == 0 ? NoResponse : code;
        }

        // 4419 sub_61D294 -> sub_61D6B8 gate (codec-fidelity 2026-08-01, first-fail-wins check-order):
        //   create-fail -> 0 (silent) ; no-config -> -3 ; duration>maxDur -> -2 ; name-gate -> -3 ;
        //   can't-afford (duration*fee > GoldNum) -> -1 ; else 1. Handler sub_6E7938 sends the code IFF != 0.
        private static int SetTimeLevel(NativeStallContext c)
        {
            if (!c.SetTimeLevelRecordCreated) return 0;      // silent get-or-create edge
            if (!c.SetTimeLevelConfigPresent) return -3;     // no tier config
            if (!c.SetTimeLevelDurationWithinMax) return -2; // duration > cfg maxDur
            if (!c.SetTimeLevelNameGateOk) return -3;        // name-length gate (passes for valid config)
            if (!c.SetTimeLevelCanAfford) return -1;         // duration * fee > GoldNum (never deducts)
            return 1;
        }

        // 4420 sub_61D3E0.
        private static int SetName(NativeStallContext c)
        {
            if (!c.StallRecordFound) return 0;   // silent
            if (c.StallRunning) return -3;
            if (c.NameTooLong) return -1;
            if (c.NameEmpty) return -2;
            return 1;
        }

        // 4421 sub_61BC7C.
        private static int AddItem(NativeStallContext c)
        {
            if (!c.StallRecordFound) return -2;
            if (!c.AddItemFound) return -3;
            if (c.AddItemLocked) return -5;
            if (!c.AddCountValid) return -4;
            return c.AddSucceeded ? 1 : -1;
        }

        // 4422 sub_61BECC.
        private static int DelItem(NativeStallContext c) =>
            c.DelSucceeded ? 1 : -1;

        // 4424 sub_6E7C38 + sub_61D4F0.
        private static int StartStall(NativeStallContext c)
        {
            if (!c.MapAllowsStall) return -9;
            if (!c.StallRecordFound) return -4;
            if (!c.StartPrecheckA) return -7;
            if (!c.StartPrecheckB) return -8;
            return c.StartCoreResult; // >0 success (abstract); 0 => NoResponse
        }

        // 4426 sub_6E7A04 -> sub_61C8E0 -> sub_61E8EC (idat-reversed): -1 disabled / -5 target inactive /
        //   -4 item gone / -2 (type1) or -3 (type0) insufficient money / -6 bad qty / else finalize.
        private static int BuyItem(NativeStallContext c)
        {
            if (!c.BuyEnabled) return -1;               // sub_7481F4
            if (!c.BuyTargetStallActive) return -5;     // sub_61C8E0 target active
            if (!c.BuyItemStillPresent) return -4;      // sub_61E8EC: sub_61EE34 resolves the item
            if (!c.BuyerHasEnoughMoney) return c.BuyMoneyType == 1 ? -2 : -3;
            if (!c.BuyQtyValid) return -6;              // stackable count / non-stackable qty==1
            return c.BuyFinalizeResult;                 // type1 => 0 (async, silent); type0 => sub_61E0C8
        }

        // 4425 sub_6E7CB0 -> sub_61D640 -> sub_61E02C (idat-reversed): no caller record -> -1;
        //   owner name unresolved (sub_656C14) -> -1; else close core sub_61A36C+sub_61FEAC
        //   (>0 success; sets rec[+0x40]=2 "paused"). 0 => NoResponse via send-if-nonzero.
        private static int PauseStall(NativeStallContext c)
        {
            if (!c.StallRecordFound) return -1;   // sub_61D640
            if (!c.PauseOwnerResolved) return -1; // sub_61E02C: sub_656C14
            return c.PauseCloseResult;
        }

        // 4467 sub_6E7A64 + sub_61C80C.
        private static int MessageStall(NativeStallContext c)
        {
            if (!c.MessagePayloadValid) return 0; // handler skips send when payload < 64 bytes
            if (!c.StallRecordFound) return -1;
            if (!c.MessageAllowed) return -2;
            return 1;
        }
    }
}
