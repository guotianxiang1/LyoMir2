namespace GameSvr
{
    // Dormant, fail-closed models of the "B-addr" cluster of PAS-script
    // ("TPsNpc") native handlers of the 战神 M2Server binary
    // (M2Server_unpacked_fixed.exe, base 0x00400000,
    //  SHA256 5540f43bc58d…c049670b14e). These are the registered-body handlers
    // from staging/pas_divergence_census_20260801.md whose bodies clustered in
    // 0x006Exxxx; all were dumped in ONE serialized idat pass
    // (staging/pas_fly_ida_work_20260731/papi_out.txt) and are modeled here
    // dump-only, fly-family style: pure decision ladder, every side-effect
    // executor abstracted as an INPUT, nothing mutates state.
    //
    // Executors abstracted throughout: the group tag readers/writers
    // (sub_727A1C/7279D4/727AD4/727B4C/727C44), the linfu manager (sub_714334),
    // the storage manager (sub_74A4E4/74A2E4/74DE54/73CED0), the hero exp
    // adder (sub_687714), the level buffer (sub_746870), the item movers
    // (sub_6DFA40/6DF2E8/6E95BC/6E1FBC/6E148C), the castle/sabak/vote/act
    // managers (sub_6584EC/6587FC/65C554/65C798/6A4A2C/722928/723D20), the
    // diamond transfer helpers, the SendMsg dispatch (sub_768BE0 / sub_6D3694 /
    // creature vtable[0xD4]/[0x250]) and the RNG/time (sub_408340) — none is
    // synthesised here.

    // =====================================================================
    // Shared outcome enums (many handlers share the same 1-gate shape)
    // =====================================================================

    /// <summary>1-gate: caller has a group object ([self+0x0A80]) or not.</summary>
    public enum NativeGroupGateOutcome { NoGroup, Delegate }

    /// <summary>1-gate: the backing global manager pointer is non-null or not.</summary>
    public enum NativeManagerGateOutcome { ManagerAbsent, Delegate }

    /// <summary>1-gate: caller has a hero object ([self+0x0BB0]) or not.</summary>
    public enum NativeHeroGateOutcome { NoHero, Delegate }

    // =====================================================================
    // FAMILY 1 — group member "tag/v" accessors
    //   [self+0x0A80] is the group object; group ops delegate to sub_727xxx.
    //   The self "tag" array is 20 dwords at [self+0x0A84 .. +0x0AD0]
    //   (index 1..20, i.e. [self+0x0A80 + idx*4]).
    // =====================================================================

    /// <summary>addallgroupmemtag sub_6E3FDC / setallgroupmemtag sub_6E3FBC /
    /// groupchktags sub_6E4F08 / groupchktagv sub_6E4F34 /
    /// queryteammemberlevelinfo sub_6E63C8 — all: group present ? delegate to the
    /// group method : return 0.</summary>
    public static class NativeGroupTagPlanner
    {
        public const int AddAllGroupMemTagAddr = 0x006E3FDC;   // -> sub_727A1C
        public const int SetAllGroupMemTagAddr = 0x006E3FBC;   // -> sub_7279D4
        public const int GroupChkTagsAddr = 0x006E4F08;        // -> sub_727AD4 (2 args)
        public const int GroupChkTagVAddr = 0x006E4F34;        // -> sub_727B4C (2 args)
        public const int QueryTeamMemberLevelInfoAddr = 0x006E63C8; // -> sub_727C44

        public static NativeGroupGateOutcome Plan(bool hasGroup)
            => hasGroup ? NativeGroupGateOutcome.Delegate : NativeGroupGateOutcome.NoGroup;

        /// <summary>Return value when there is no group (native `xor ebx,ebx`
        /// before the gate): 0.</summary>
        public static int NoGroupReturn => 0;
    }

    /// <summary>Outcome of the fixed-slot self-tag accessors.</summary>
    public enum NativeSelfTagOutcome { OutOfRange, InRange }

    /// <summary>getselfgroupmemtag sub_6E4018 (getter) / setselfgroupmemtag
    /// sub_6E3FFC (setter). Valid slot index is 1..20 (native `(idx-1) unsigned
    /// &lt; 0x14`). Getter returns [self+0x0A80+idx*4] in range else -1; setter
    /// writes and returns true in range else false.</summary>
    public static class NativeSelfGroupMemTagPlanner
    {
        public const int GetterAddr = 0x006E4018;
        public const int SetterAddr = 0x006E3FFC;
        public const int MinIndex = 1;
        public const int MaxIndex = 20;      // native compares (idx-1) < 0x14
        public const int SlotBaseOffset = 0x0A80;

        public static NativeSelfTagOutcome Plan(int index)
            => (index >= MinIndex && index <= MaxIndex)
                ? NativeSelfTagOutcome.InRange
                : NativeSelfTagOutcome.OutOfRange;

        /// <summary>Getter return when out of range (native `or esi,-1`): -1.</summary>
        public static int GetterOutOfRangeReturn => -1;
    }

    // =====================================================================
    // FAMILY 2 — LinFu / exp-time
    // =====================================================================

    /// <summary>adddblinfutime sub_6E2160: `procedure AddDblLinFuTime(seconds)`.
    /// seconds &lt;= 0 (unsigned `jbe`) -&gt; no-op; else if [self+0x9A8]==0 set
    /// [self+0x9AC]=now (sub_408340), then [self+0x9A8]+=seconds*1000, and SendMsg
    /// sub_765E68 (wIdent 0x27B0, param 0x3ED, value [self+0x9A8]/1000).</summary>
    public enum NativeAddDblLinFuTimeOutcome { NonPositive, Accumulate }

    public static class NativeAddDblLinFuTimePlanner
    {
        public const int WrapperAddress = 0x006E2160;
        public const int EndTimeOffset = 0x9A8;       // accumulated end-time (ms)
        public const int StartTimeOffset = 0x9AC;     // set to now when starting
        public const int SendMsgAddress = 0x00765E68;
        public const int NotifyWIdent = 0x27B0;
        public const int NotifyParam = 0x3ED;
        public const int MillisPerSec = 0x3E8;

        public static NativeAddDblLinFuTimeOutcome Plan(int seconds)
            => seconds <= 0
                ? NativeAddDblLinFuTimeOutcome.NonPositive
                : NativeAddDblLinFuTimeOutcome.Accumulate;
    }

    /// <summary>boindblinfu sub_6E21E8: `function BoInDblLinFu: Boolean` —
    /// [self+0x9A8] &gt; 0 (signed) i.e. double-linfu currently active.</summary>
    public static class NativeBoInDblLinFuPlanner
    {
        public const int WrapperAddress = 0x006E21E8;
        public const int EndTimeOffset = 0x9A8;
        public static bool Evaluate(int endTime) => endTime > 0;
    }

    /// <summary>clearmulexptime sub_6E3FB0: `procedure ClearMulExpTime` —
    /// unconditionally sets [self+0x0BB8]=0. No gate.</summary>
    public static class NativeClearMulExpTimePlanner
    {
        public const int WrapperAddress = 0x006E3FB0;
        public const int MulExpTimeOffset = 0x0BB8;
        public static bool AlwaysClears => true;
    }

    /// <summary>getlimitlinfu sub_6E1BC4: `function GetLimitLinFu: Integer` —
    /// delegate sub_714334([self+0x1824]) (no gate). showlingfu3 sub_6E1BD4:
    /// returns [[self+0x1824]+0x0C] (no gate).</summary>
    public static class NativeLinFuQueryPlanner
    {
        public const int GetLimitLinFuAddr = 0x006E1BC4;   // -> sub_714334([self+0x1824])
        public const int ShowLingFu3Addr = 0x006E1BD4;     // [[self+0x1824]+0x0C]
        public const int LinFuSubObjOffset = 0x1824;
        public const int ShowLingFu3FieldOffset = 0x0C;
        public static bool BothUnconditional => true;
    }

    // =====================================================================
    // FAMILY 3 — hero exp / level buffer  ([self+0x0BB0] is the hero object)
    // =====================================================================

    /// <summary>giveheroexp sub_6E2C90 (flag 0) / giveherosuperexp sub_6E2CC0
    /// (flag 1): hero present ? sub_687714(hero, amount, flag) + return true :
    /// return false. giveherosuperexploop sub_6E2CEC: hero present ? loop
    /// (count&gt;0) sub_687714 count times + true : false.</summary>
    public static class NativeGiveHeroExpPlanner
    {
        public const int GiveHeroExpAddr = 0x006E2C90;        // flag 0
        public const int GiveHeroSuperExpAddr = 0x006E2CC0;   // flag 1
        public const int GiveHeroSuperExpLoopAddr = 0x006E2CEC;
        public const int HeroObjectOffset = 0x0BB0;
        public const int ExpAdderAddress = 0x00687714;        // sub_687714

        public static NativeHeroGateOutcome Plan(bool hasHero)
            => hasHero ? NativeHeroGateOutcome.Delegate : NativeHeroGateOutcome.NoHero;

        /// <summary>Return value: true iff a hero was present (native `bl=1`
        /// only inside the hero branch). For the Loop variant the loop body only
        /// runs when count &gt; 0, but the return is still "hero present".</summary>
        public static bool ResolveReturn(bool hasHero) => hasHero;
    }

    /// <summary>giveheroforceexp sub_6E2CBC: the native body is `xor eax,eax;
    /// retn` — a NO-OP stub that always returns 0 and does nothing. PasApiBridge
    /// rejecting/no-op-ing it is therefore faithful.</summary>
    public static class NativeGiveHeroForceExpPlanner
    {
        public const int WrapperAddress = 0x006E2CBC;
        public static bool IsNativeNoOpStub => true;
        public static int AlwaysReturns => 0;
    }

    /// <summary>givehumlevelbuffer sub_6F28B0: `function GiveHumLevelBuffer(target,
    /// value): Integer`. target==0 -&gt; apply to self via sub_746870(self,value);
    /// target!=0 -&gt; requires a hero ([self+0x0BB0]) else return -9 (0xFFFFFFF7),
    /// applying via sub_746870(hero,value).</summary>
    public enum NativeGiveHumLevelBufferOutcome { SelfApply, HeroApply, NoHero }

    public static class NativeGiveHumLevelBufferPlanner
    {
        public const int WrapperAddress = 0x006F28B0;
        public const int HeroObjectOffset = 0x0BB0;
        public const int ExecutorAddress = 0x00746870;   // sub_746870
        public const int NoHeroCode = -9;                // 0xFFFFFFF7

        /// <param name="targetFlag">arg_0: 0 = self, non-0 = hero.</param>
        /// <param name="hasHero">[self+0x0BB0] != 0 (only read when target!=0).</param>
        public static NativeGiveHumLevelBufferOutcome Plan(int targetFlag, bool hasHero)
        {
            if (targetFlag == 0)
                return NativeGiveHumLevelBufferOutcome.SelfApply;
            return hasHero
                ? NativeGiveHumLevelBufferOutcome.HeroApply
                : NativeGiveHumLevelBufferOutcome.NoHero;
        }
    }

    // =====================================================================
    // FAMILY 4 — item store / give / send / present
    // =====================================================================

    /// <summary>addstoreitem sub_6E524C: `function AddStoreItem(name): Boolean`.
    /// storage sub_74A4E4([self+0x6D0],1) false -&gt; false; make item
    /// sub_74DE54(name); if null -&gt; false; add sub_74A2E4(storage,item):
    /// true on success, else free (sub_404690) and false.</summary>
    public enum NativeAddStoreItemOutcome { StorageUnavailable, MakeFailed, AddFailed, Added }

    public static class NativeAddStoreItemPlanner
    {
        public const int WrapperAddress = 0x006E524C;
        public const int StorageOffset = 0x6D0;
        public const int StorageCheckAddr = 0x0074A4E4;
        public const int MakeItemAddr = 0x0074DE54;
        public const int StorageAddAddr = 0x0074A2E4;

        /// <param name="storageOk">sub_74A4E4(storage,1).</param>
        /// <param name="itemMade">sub_74DE54(name) != null.</param>
        /// <param name="addOk">sub_74A2E4(storage,item).</param>
        public static NativeAddStoreItemOutcome Plan(bool storageOk, bool itemMade, bool addOk)
        {
            if (!storageOk) return NativeAddStoreItemOutcome.StorageUnavailable;
            if (!itemMade) return NativeAddStoreItemOutcome.MakeFailed;
            return addOk ? NativeAddStoreItemOutcome.Added : NativeAddStoreItemOutcome.AddFailed;
        }

        public static bool ResolveReturn(NativeAddStoreItemOutcome o)
            => o == NativeAddStoreItemOutcome.Added;
    }

    /// <summary>giveitemwithdura sub_6E15E0: `function GiveItemWithDura(name,
    /// count, dura): Boolean`. If free bag slots (sub_7441D8) &lt; count -&gt;
    /// send bag-full sysmsg (vtable[0xD4], wIdent 0xFFDB) and return false. Else
    /// loop count times: make item; set current dura [item+0x26] = min(dura,
    /// [item+0x28] max); add via vtable[0x248]; on success SendMsg (sub_768BE0,
    /// subtype 9) else free item and mark result false. NOTE this contradicts the
    /// current PasApiBridge "corrupts semantics" reject — the native DOES clamp
    /// and apply the requested durability.</summary>
    public enum NativeGiveItemWithDuraOutcome { BagFull, LoopMakeAdd }

    public static class NativeGiveItemWithDuraPlanner
    {
        public const int WrapperAddress = 0x006E15E0;
        public const int FreeSlotsAddr = 0x007441D8;
        public const int MakeItemAddr = 0x0074DE54;
        public const int AddItemVtableSlot = 0x248;
        public const int BagFullWIdent = 0xFFDB;
        public const int GainMsgSubtype = 9;
        public const int DuraMaxOffset = 0x28;   // [item+0x28]
        public const int DuraCurOffset = 0x26;   // [item+0x26]

        /// <param name="freeSlots">sub_7441D8(self).</param>
        public static NativeGiveItemWithDuraOutcome Plan(int freeSlots, int count)
            => freeSlots < count
                ? NativeGiveItemWithDuraOutcome.BagFull
                : NativeGiveItemWithDuraOutcome.LoopMakeAdd;

        /// <summary>Clamped current durability written to [item+0x26]:
        /// min(requestedDura, itemMaxDura). (native `cmp ax,arg_0; jb` picks the
        /// smaller.)</summary>
        public static int ClampDura(int requestedDura, int itemMaxDura)
            => requestedDura < itemMaxDura ? requestedDura : itemMaxDura;
    }

    /// <summary>senditemstoother sub_6E61F8: `function SendItemsToOther(target,
    /// mode, ...): Integer`. Result ladder: target offline (sub_652784==0) -&gt;
    /// -1; target vtable[0x244](mode) false -&gt; -2; self precondition
    /// sub_6DFA40 false -&gt; -3; else execute sub_6DF2E8 and return 1.</summary>
    public enum NativeSendItemsToOtherOutcome
    {
        TargetOffline = -1, TargetRejected = -2, SelfPreconditionFailed = -3, Sent = 1
    }

    public static class NativeSendItemsToOtherPlanner
    {
        public const int WrapperAddress = 0x006E61F8;
        public const int TargetLookupAddr = 0x00652784;
        public const int TargetAcceptVtableSlot = 0x244;
        public const int SelfPreconditionAddr = 0x006DFA40;
        public const int ExecutorAddr = 0x006DF2E8;

        public static NativeSendItemsToOtherOutcome Plan(bool targetOnline,
            bool targetAccepts, bool selfPreconditionOk)
        {
            if (!targetOnline) return NativeSendItemsToOtherOutcome.TargetOffline;
            if (!targetAccepts) return NativeSendItemsToOtherOutcome.TargetRejected;
            if (!selfPreconditionOk) return NativeSendItemsToOtherOutcome.SelfPreconditionFailed;
            return NativeSendItemsToOtherOutcome.Sent;
        }
    }

    /// <summary>giveitemstoother sub_6E93D4: `function GiveItemsToOther(target,
    /// item, mode, count, price, extra): Integer`. Result ladder (native codes):
    /// target empty OR target==self -&gt; 1; price&lt;0 OR count&lt;0 -&gt; 2;
    /// item==null OR [item+0x178]!=0x0A -&gt; -1; target offline -&gt; 3; then
    /// mode==0 (direct): if price*count &gt;= [self+0x760] fall to confirm path,
    /// else stage + confirm-send sub_6D3694 (fail -&gt; 5) then success 0;
    /// mode!=0 (confirm path): sub_6E1FBC ok ? execute sub_6E95BC + 0 : 4.</summary>
    public enum NativeGiveItemsToOtherOutcome
    {
        Success = 0, BadTargetOrSelf = 1, NegativeAmount = 2, InvalidItem = -1,
        TargetOffline = 3, ConfirmPathFailed = 4, DirectSendFailed = 5
    }

    public static class NativeGiveItemsToOtherPlanner
    {
        public const int WrapperAddress = 0x006E93D4;
        public const int GiveableItemType = 0x0A;      // [item+0x178] must == 0x0A
        public const int SelfGoldOffset = 0x760;
        public const int TargetLookupAddr = 0x00652784;
        public const int ConfirmSendAddr = 0x006D3694;  // wIdent 0x277E
        public const int ConfirmExecAddr = 0x006E1FBC;
        public const int TransferExecAddr = 0x006E95BC;

        /// <param name="targetValid">target name non-empty AND target != self.</param>
        /// <param name="amountsNonNegative">price &gt;= 0 AND count &gt;= 0.</param>
        /// <param name="itemValid">item != null AND [item+0x178] == 0x0A.</param>
        /// <param name="targetOnline">sub_652784(target) != null.</param>
        /// <param name="directMode">mode (arg_0) == 0.</param>
        /// <param name="directAffordable">price*count &lt; [self+0x760]. In direct
        /// mode when NOT affordable the native reaches loc_6E951C with arg_0==0 and
        /// returns 4 (it does NOT run the confirm executor).</param>
        /// <param name="directSendOk">sub_6D3694 confirm-send result (direct+affordable).</param>
        /// <param name="confirmOk">sub_6E1FBC result (confirm mode, arg_0!=0).</param>
        public static NativeGiveItemsToOtherOutcome Plan(bool targetValid,
            bool amountsNonNegative, bool itemValid, bool targetOnline,
            bool directMode, bool directAffordable, bool directSendOk, bool confirmOk)
        {
            if (!targetValid) return NativeGiveItemsToOtherOutcome.BadTargetOrSelf;
            if (!amountsNonNegative) return NativeGiveItemsToOtherOutcome.NegativeAmount;
            if (!itemValid) return NativeGiveItemsToOtherOutcome.InvalidItem;
            if (!targetOnline) return NativeGiveItemsToOtherOutcome.TargetOffline;
            if (directMode)
            {
                // arg_0 == 0. Unaffordable falls to loc_6E951C where arg_0==0 -> 4.
                if (!directAffordable) return NativeGiveItemsToOtherOutcome.ConfirmPathFailed;
                return directSendOk
                    ? NativeGiveItemsToOtherOutcome.Success
                    : NativeGiveItemsToOtherOutcome.DirectSendFailed;
            }
            // confirm mode (arg_0 != 0): sub_6E1FBC ok ? execute + 0 : 4.
            return confirmOk
                ? NativeGiveItemsToOtherOutcome.Success
                : NativeGiveItemsToOtherOutcome.ConfirmPathFailed;
        }
    }

    /// <summary>presentitem sub_6EBB6C: `function PresentItem(target, itemName,
    /// bindFlag, genderArg, count): Integer`. Result ladder (native codes):
    /// itemName empty OR count&lt;1 -&gt; -1; std-item lookup sub_74C1E0 &lt; 0
    /// -&gt; -6; target offline -&gt; -2; target==self -&gt; -3; [target+0x71] !=
    /// genderArg AND genderArg&lt;2 -&gt; -7; target bag (sub_7441D8) &lt;= count
    /// -&gt; -4; matched present-able items in self bag &lt; count -&gt; -5; else
    /// transfer loop and return 0.</summary>
    public enum NativePresentItemOutcome
    {
        Success = 0, BadArgs = -1, ItemNotFound = -6, TargetOffline = -2,
        TargetIsSelf = -3, GenderMismatch = -7, TargetBagInsufficient = -4,
        NotEnoughItems = -5
    }

    public static class NativePresentItemPlanner
    {
        public const int WrapperAddress = 0x006EBB6C;
        public const int StdItemLookupAddr = 0x0074C1E0;
        public const int TargetLookupAddr = 0x00652784;
        public const int TargetBagCountAddr = 0x007441D8;
        public const int TargetGenderOffset = 0x71;

        /// <param name="itemNameOk">itemName non-empty.</param>
        /// <param name="countPositive">count &gt;= 1.</param>
        /// <param name="stdItemFound">sub_74C1E0(itemName) &gt;= 0.</param>
        /// <param name="targetOnline">sub_652784(target) != null.</param>
        /// <param name="targetIsSelf">resolved target == self.</param>
        /// <param name="genderOk">[target+0x71]==genderArg OR genderArg&gt;=2.</param>
        /// <param name="targetBagOk">sub_7441D8(target) &gt; count.</param>
        /// <param name="haveEnough">matched present-able items in self bag &gt;= count.</param>
        public static NativePresentItemOutcome Plan(bool itemNameOk, bool countPositive,
            bool stdItemFound, bool targetOnline, bool targetIsSelf, bool genderOk,
            bool targetBagOk, bool haveEnough)
        {
            if (!itemNameOk || !countPositive) return NativePresentItemOutcome.BadArgs;
            if (!stdItemFound) return NativePresentItemOutcome.ItemNotFound;
            if (!targetOnline) return NativePresentItemOutcome.TargetOffline;
            if (targetIsSelf) return NativePresentItemOutcome.TargetIsSelf;
            if (!genderOk) return NativePresentItemOutcome.GenderMismatch;
            if (!targetBagOk) return NativePresentItemOutcome.TargetBagInsufficient;
            if (!haveEnough) return NativePresentItemOutcome.NotEnoughItems;
            return NativePresentItemOutcome.Success;
        }
    }

    /// <summary>getgoodscurrentstorage sub_6E522C: goods-manager (off_7D6D10)
    /// null ? 0 : sub_6166E0(mgr). querygoodsnumbyybnum sub_6E4F88:
    /// unconditionally sub_6161A8(off_7D6D10, ybNum) then stash ybNum-&gt;
    /// [self+0x9D4], result-&gt;[self+0x9D8].</summary>
    public static class NativeGoodsQueryPlanner
    {
        public const int GetGoodsCurrentStorageAddr = 0x006E522C;   // mgr-null -> 0
        public const int QueryGoodsNumByYbNumAddr = 0x006E4F88;     // unconditional stash
        public const int GoodsManagerPtr = 0x007D6D10;

        public static NativeManagerGateOutcome PlanGetGoodsCurrentStorage(bool managerPresent)
            => managerPresent ? NativeManagerGateOutcome.Delegate : NativeManagerGateOutcome.ManagerAbsent;
    }

    // =====================================================================
    // FAMILY 5 — castle / sabak / vote / activity managers (null-gated delegates)
    // =====================================================================

    /// <summary>getcastlegift sub_6EB678: `function GetCastleGift(a, ordId):
    /// Integer`. Requires guild-membership sub_6ADAE4(self)!=0 AND castle-mgr
    /// (off_7D67C0)!=null AND ordId!=0; else -1. Then sub_6584EC(mgr, self,
    /// ordId, a).</summary>
    public enum NativeGetCastleGiftOutcome { Ineligible, Delegate }

    public static class NativeGetCastleGiftPlanner
    {
        public const int WrapperAddress = 0x006EB678;
        public const int GuildCheckAddr = 0x006ADAE4;
        public const int CastleManagerPtr = 0x007D67C0;
        public const int ExecutorAddr = 0x006584EC;
        public const int IneligibleCode = -1;

        public static NativeGetCastleGiftOutcome Plan(bool inGuild, bool managerPresent, bool ordIdNonZero)
            => (inGuild && managerPresent && ordIdNonZero)
                ? NativeGetCastleGiftOutcome.Delegate
                : NativeGetCastleGiftOutcome.Ineligible;
    }

    /// <summary>Null-gated castle/sabak/vote delegates that return a sentinel or
    /// passthrough when the backing manager pointer is null:
    /// getcastleorddesc sub_6EB64C (mgr off_7D67C0 null -&gt; return input string
    ///   unchanged; else sub_6587FC);
    /// getcastlestoneowners sub_6E6474 (mgr off_7D6214 null -&gt; clear out; else
    ///   sub_65C554);
    /// takecastlestone sub_6E6448 (mgr off_7D6214 null -&gt; -1; else sub_65C798);
    /// queryallvotetopten sub_6EB094 (clear out; mgr off_7D71F0 null -&gt; nothing;
    ///   else sub_6A4A2C).</summary>
    public static class NativeCastleVoteDelegatePlanner
    {
        public const int GetCastleOrdDescAddr = 0x006EB64C;   // off_7D67C0
        public const int GetCastleStoneOwnersAddr = 0x006E6474; // off_7D6214
        public const int TakeCastleStoneAddr = 0x006E6448;     // off_7D6214 ; null -> -1
        public const int QueryAllVoteTopTenAddr = 0x006EB094;  // off_7D71F0
        public const int TakeCastleStoneManagerAbsentCode = -1;

        public static NativeManagerGateOutcome Plan(bool managerPresent)
            => managerPresent ? NativeManagerGateOutcome.Delegate : NativeManagerGateOutcome.ManagerAbsent;
    }

    /// <summary>updateeverydayactorder sub_6EB054: `function UpdateEverydayActOrder
    /// (a, b): Integer`. act-mgr (off_7D62D8) null -&gt; -1; sub_723D20(mgr) false
    /// -&gt; -1; else sub_722928(a, b).</summary>
    public enum NativeUpdateEverydayActOrderOutcome { ManagerAbsentOrProbeFail, Delegate }

    public static class NativeUpdateEverydayActOrderPlanner
    {
        public const int WrapperAddress = 0x006EB054;
        public const int ManagerPtr = 0x007D62D8;
        public const int ProbeAddr = 0x00723D20;
        public const int ExecutorAddr = 0x00722928;
        public const int FailCode = -1;

        public static NativeUpdateEverydayActOrderOutcome Plan(bool managerPresent, bool probeOk)
            => (managerPresent && probeOk)
                ? NativeUpdateEverydayActOrderOutcome.Delegate
                : NativeUpdateEverydayActOrderOutcome.ManagerAbsentOrProbeFail;
    }

    // =====================================================================
    // FAMILY 6 — diamond (钻石)
    // =====================================================================

    /// <summary>donatediam sub_6C7E38: `procedure DonateDiam(spec: string)` — a
    /// P2P diamond transfer. Abort (silent, various sysmsgs) on any gate:
    /// [self+0x461]!=0 (locked); sub_772DA8(self) (blocked); spec empty;
    /// !sub_6C7D88(self,1) (precondition); now-[self+0x6FC] &lt;= 0xDBBA0
    /// (15-min self cooldown); spec parse (split by delimiter into name+amount)
    /// yields empty; target offline; target==self; amount&lt;=0 OR amount&gt;=1000;
    /// [self+0x0BF0] (self diamonds) &lt; amount; [target+0x73]!=0 OR
    /// [target+0x461]!=0 OR now-[target+0x6FC] &lt;= 0xDBBA0 (target
    /// ineligible/cooldown). Only when ALL pass does it move amount self-&gt;target
    /// and stamp both cooldowns.</summary>
    public enum NativeDonateDiamOutcome
    {
        SelfLocked, SelfBlocked, EmptySpec, PreconditionFailed, SelfCooldown,
        ParseFailed, TargetOffline, TargetIsSelf, InvalidAmount, InsufficientDiamonds,
        TargetIneligible, Transfer
    }

    public static class NativeDonateDiamPlanner
    {
        public const int WrapperAddress = 0x006C7E38;
        public const int LockFlagOffset = 0x461;
        public const int BlockedCheckAddr = 0x00772DA8;
        public const int PreconditionAddr = 0x006C7D88;
        public const int NowAddr = 0x00408340;
        public const int CooldownStampOffset = 0x6FC;
        public const int CooldownMillis = 0xDBBA0;      // 900000 ms = 15 min
        public const int TargetLookupAddr = 0x00652784;
        public const int SelfDiamondOffset = 0x0BF0;
        public const int MinAmountExclusive = 0;        // amount > 0
        public const int MaxAmountExclusive = 1000;     // amount < 1000
        public const int TargetBanOffset = 0x73;

        /// <summary>Short-circuit fail-closed ladder in the native's exact order.</summary>
        public static NativeDonateDiamOutcome Plan(bool selfLocked, bool selfBlocked,
            bool specNonEmpty, bool preconditionOk, bool selfCooldownElapsed,
            bool parseOk, bool targetOnline, bool targetIsSelf, int amount,
            bool selfHasEnough, bool targetEligible)
        {
            if (selfLocked) return NativeDonateDiamOutcome.SelfLocked;
            if (selfBlocked) return NativeDonateDiamOutcome.SelfBlocked;
            if (!specNonEmpty) return NativeDonateDiamOutcome.EmptySpec;
            if (!preconditionOk) return NativeDonateDiamOutcome.PreconditionFailed;
            if (!selfCooldownElapsed) return NativeDonateDiamOutcome.SelfCooldown;
            if (!parseOk) return NativeDonateDiamOutcome.ParseFailed;
            if (!targetOnline) return NativeDonateDiamOutcome.TargetOffline;
            if (targetIsSelf) return NativeDonateDiamOutcome.TargetIsSelf;
            if (amount <= MinAmountExclusive || amount >= MaxAmountExclusive)
                return NativeDonateDiamOutcome.InvalidAmount;
            if (!selfHasEnough) return NativeDonateDiamOutcome.InsufficientDiamonds;
            if (!targetEligible) return NativeDonateDiamOutcome.TargetIneligible;
            return NativeDonateDiamOutcome.Transfer;
        }
    }

    /// <summary>reqbuilddiamond sub_6C7CB8: `procedure ReqBuildDiamond(amountStr)`.
    /// !sub_6C7D88(self,1) -&gt; "unavailable" sysmsg (vtable[0xD4], 0x38FF);
    /// amount=sub_40CA18(str) not in 1..1000 -&gt; "invalid" sysmsg; else dispatch
    /// SendMsg sub_6D3694 (wIdent 0xCA) with amount.</summary>
    public enum NativeReqBuildDiamondOutcome { Unavailable, InvalidAmount, Dispatch }

    public static class NativeReqBuildDiamondPlanner
    {
        public const int WrapperAddress = 0x006C7CB8;
        public const int PreconditionAddr = 0x006C7D88;
        public const int ParseAddr = 0x0040CA18;
        public const int DispatchAddr = 0x006D3694;   // wIdent 0xCA
        public const int MinAmount = 1;
        public const int MaxAmount = 1000;            // native: amount>0 && amount<=0x3E8

        public static NativeReqBuildDiamondOutcome Plan(bool preconditionOk, int amount)
        {
            if (!preconditionOk) return NativeReqBuildDiamondOutcome.Unavailable;
            if (amount < MinAmount || amount > MaxAmount)
                return NativeReqBuildDiamondOutcome.InvalidAmount;
            return NativeReqBuildDiamondOutcome.Dispatch;
        }
    }

    // =====================================================================
    // FAMILY 7 — misc (jiayou / createtime / transfer-area / paodian / crethp /
    //                  combine-train / newyear-picture)
    // =====================================================================

    /// <summary>decjiayoupoint sub_6F28E8: `procedure DecJiaYouPoint(point)`.
    /// point &lt;= 0 -&gt; no-op; else [self+0x0AF0] -= point, clamped to 0 when
    /// [self+0x0AF0] &lt; point. (64-bit signed compare/subtract.)</summary>
    public enum NativeDecJiaYouPointOutcome { NonPositive, SubtractClamped }

    public static class NativeDecJiaYouPointPlanner
    {
        public const int WrapperAddress = 0x006F28E8;
        public const int PointOffset = 0x0AF0;

        public static NativeDecJiaYouPointOutcome Plan(int point)
            => point <= 0
                ? NativeDecJiaYouPointOutcome.NonPositive
                : NativeDecJiaYouPointOutcome.SubtractClamped;

        /// <summary>Resulting [self+0x0AF0] on the SubtractClamped path.</summary>
        public static long Resolve(long current, long point)
            => current < point ? 0 : current - point;
    }

    /// <summary>#16 PAS shadow-var dedicated native field bindings (gm-playerattr
    /// idat-verified, staging/pas_shadow_field_offsets_20260801.md). The live
    /// PasApiBridge binds PlatLv (RW) and JiaYouPoint (RO) to real player fields
    /// (session-only) instead of the previously-colliding generic V-slots. The
    /// other four shadow vars (DominateLevel V23:1 / TenYearImpress V23:2 /
    /// GuildPoint V10:6 / VExp V12:1) have NO published-RTTI native field/method in
    /// this exe, so they stay fail-closed (the faithful 1:1) and must NOT touch the
    /// shared V-slots.</summary>
    public static class NativePlayerShadowFieldBindings
    {
        public const int PlatLvOffset = 0xB85;        // ObjPlayer.PlatLv, Byte
        public const bool PlatLvReadWrite = true;
        public const int JiaYouPointOffset = 0xAF0;   // ObjPlayer.JiaYouPoint, Cardinal
        public const bool JiaYouPointReadOnly = true;
    }

    /// <summary>getcreatetime sub_6FA6A8: `function GetCreateTime: Double` —
    /// unconditionally returns the 8-byte double at [self+0x1930] (character
    /// creation TDateTime). No gate.</summary>
    public static class NativeGetCreateTimePlanner
    {
        public const int WrapperAddress = 0x006FA6A8;
        public const int CreateTimeOffset = 0x1930;   // 8-byte double
        public static bool IsUnconditionalGetter => true;
    }

    /// <summary>reqstarttransferarea sub_6E72F0: `procedure ReqStartTransferArea
    /// (areaType, sub)`. (areaType-1) unsigned &gt;= 3 -&gt; no-op (valid 1..3).
    /// If already in the requested area/sub (off_7D5C0C==sub AND off_7D6038==arg)
    /// -&gt; "already there" sysmsg. Else leave+transfer self (sub_6E73F0,
    /// sub_6B6510), transfer hero if present ([self+0x0BB0]) via sub_6887E4, then
    /// start (sub_6E7398).</summary>
    public enum NativeReqStartTransferAreaOutcome { InvalidAreaType, AlreadyThere, Transfer }

    public static class NativeReqStartTransferAreaPlanner
    {
        public const int WrapperAddress = 0x006E72F0;
        public const int MinAreaType = 1;
        public const int MaxAreaType = 3;
        public const int HeroObjectOffset = 0x0BB0;

        public static NativeReqStartTransferAreaOutcome Plan(int areaType, bool alreadyThere)
        {
            if (areaType < MinAreaType || areaType > MaxAreaType)
                return NativeReqStartTransferAreaOutcome.InvalidAreaType;
            if (alreadyThere)
                return NativeReqStartTransferAreaOutcome.AlreadyThere;
            return NativeReqStartTransferAreaOutcome.Transfer;
        }
    }

    /// <summary>startpaodian sub_6DEC34: `procedure StartPaoDian(param)`. Requires
    /// env [self+0x128]!=0 AND [env+0x94]!=0 (an active paodian object); else
    /// no-op. Then sub_77CF84([env+0x94], ..., param).</summary>
    public enum NativeStartPaoDianOutcome { NoEnvOrObject, Dispatch }

    public static class NativeStartPaoDianPlanner
    {
        public const int WrapperAddress = 0x006DEC34;
        public const int EnvOffset = 0x128;
        public const int PaoDianObjOffset = 0x94;
        public const int ExecutorAddr = 0x0077CF84;

        public static NativeStartPaoDianOutcome Plan(bool hasEnv, bool hasPaoDianObject)
            => (hasEnv && hasPaoDianObject)
                ? NativeStartPaoDianOutcome.Dispatch
                : NativeStartPaoDianOutcome.NoEnvOrObject;
    }

    /// <summary>psaddcrethp sub_772D64: `function PsAddCretHp(addHp, maxCnt):
    /// Integer` (called on a creature). If maxCnt &gt; 0 AND maxCnt &lt;=
    /// [self+0x0F8] (current spawn count) -&gt; return -1 (limit reached). Else if
    /// addHp==-1 use default [self+0x2B0]; apply sub_769DB4; increment
    /// [self+0x0F8]; return the new count.</summary>
    public enum NativePsAddCretHpOutcome { LimitReached, AddAndCount }

    public static class NativePsAddCretHpPlanner
    {
        public const int WrapperAddress = 0x00772D64;
        public const int CountOffset = 0x0F8;
        public const int DefaultHpOffset = 0x2B0;
        public const int ExecutorAddr = 0x00769DB4;
        public const int LimitReachedCode = -1;
        public const int DefaultHpSentinel = -1;   // addHp == -1 -> use [self+0x2B0]

        /// <param name="maxCnt">requested max spawn count.</param>
        /// <param name="currentCount">[self+0x0F8].</param>
        public static NativePsAddCretHpOutcome Plan(int maxCnt, int currentCount)
            => (maxCnt > 0 && maxCnt <= currentCount)
                ? NativePsAddCretHpOutcome.LimitReached
                : NativePsAddCretHpOutcome.AddAndCount;

        /// <summary>Return value on the AddAndCount path: the post-increment
        /// count (native returns [self+0x0F8] after `inc`).</summary>
        public static int ResolveCount(int currentCount) => currentCount + 1;
    }

    /// <summary>finishcombineherotrain sub_6E1884: `procedure
    /// FinishCombineHeroTrain` — unconditionally clears [self+0x1893]=0 and
    /// [self+0x1892]=0 then sends a sysmsg (vtable[0xD4], wIdent 0xFCFF). No gate.
    /// reqpieceupnewyearpicture sub_6E533C: unconditionally stashes three args to
    /// [self+0x9C0/0x9C4/0x9C8] then sends vtable[0x250] (wIdent 0xB87). No gate.</summary>
    public static class NativeUnconditionalNotifyPlanner
    {
        public const int FinishCombineHeroTrainAddr = 0x006E1884;
        public const int FinishCombineHeroTrainWIdent = 0xFCFF;
        public const int ReqPieceUpNewYearPictureAddr = 0x006E533C;
        public const int ReqPieceUpNewYearPictureWIdent = 0xB87;
        public static bool BothUnconditional => true;
    }
}
