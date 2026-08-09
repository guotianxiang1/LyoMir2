using System;
using System.Collections.Generic;
using GameSvr;

// Dormant-model compat check for NativeRegisteredBodyScriptApiLadders.cs — the
// B-addr 0x006Exxxx registered-body PAS handler cluster (~38 handlers). Every
// branch of every modeled ladder is asserted. Single generic Equal<T>.

int checks = 0;
void Equal<T>(T actual, T expected, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        Console.Error.WriteLine($"FAIL: {label}: expected <{expected}>, got <{actual}>");
        Environment.Exit(1);
    }
}

// ---- FAMILY 1: group tags ----
Equal(NativeGroupTagPlanner.Plan(false), NativeGroupGateOutcome.NoGroup, "grouptag no group");
Equal(NativeGroupTagPlanner.Plan(true), NativeGroupGateOutcome.Delegate, "grouptag delegate");
Equal(NativeGroupTagPlanner.NoGroupReturn, 0, "grouptag no-group return 0");
Equal(NativeSelfGroupMemTagPlanner.Plan(0), NativeSelfTagOutcome.OutOfRange, "selftag idx 0");
Equal(NativeSelfGroupMemTagPlanner.Plan(1), NativeSelfTagOutcome.InRange, "selftag idx 1");
Equal(NativeSelfGroupMemTagPlanner.Plan(20), NativeSelfTagOutcome.InRange, "selftag idx 20");
Equal(NativeSelfGroupMemTagPlanner.Plan(21), NativeSelfTagOutcome.OutOfRange, "selftag idx 21");
Equal(NativeSelfGroupMemTagPlanner.GetterOutOfRangeReturn, -1, "selftag getter oob -1");

// ---- FAMILY 2: linfu / exp-time ----
Equal(NativeAddDblLinFuTimePlanner.Plan(0), NativeAddDblLinFuTimeOutcome.NonPositive, "adddbl 0");
Equal(NativeAddDblLinFuTimePlanner.Plan(-5), NativeAddDblLinFuTimeOutcome.NonPositive, "adddbl neg");
Equal(NativeAddDblLinFuTimePlanner.Plan(10), NativeAddDblLinFuTimeOutcome.Accumulate, "adddbl pos");
Equal(NativeBoInDblLinFuPlanner.Evaluate(0), false, "boindbl 0 false");
Equal(NativeBoInDblLinFuPlanner.Evaluate(-1), false, "boindbl neg false");
Equal(NativeBoInDblLinFuPlanner.Evaluate(5), true, "boindbl pos true");
Equal(NativeClearMulExpTimePlanner.AlwaysClears, true, "clearmulexp unconditional");
Equal(NativeLinFuQueryPlanner.BothUnconditional, true, "linfu query unconditional");

// ---- FAMILY 3: hero exp / level buffer ----
Equal(NativeGiveHeroExpPlanner.Plan(false), NativeHeroGateOutcome.NoHero, "giveheroexp no hero");
Equal(NativeGiveHeroExpPlanner.Plan(true), NativeHeroGateOutcome.Delegate, "giveheroexp delegate");
Equal(NativeGiveHeroExpPlanner.ResolveReturn(false), false, "giveheroexp return false");
Equal(NativeGiveHeroExpPlanner.ResolveReturn(true), true, "giveheroexp return true");
Equal(NativeGiveHeroForceExpPlanner.IsNativeNoOpStub, true, "giveheroforceexp is noop stub");
Equal(NativeGiveHeroForceExpPlanner.AlwaysReturns, 0, "giveheroforceexp returns 0");
Equal(NativeGiveHumLevelBufferPlanner.Plan(0, false), NativeGiveHumLevelBufferOutcome.SelfApply, "humbuf self (no hero)");
Equal(NativeGiveHumLevelBufferPlanner.Plan(0, true), NativeGiveHumLevelBufferOutcome.SelfApply, "humbuf self");
Equal(NativeGiveHumLevelBufferPlanner.Plan(1, false), NativeGiveHumLevelBufferOutcome.NoHero, "humbuf hero missing");
Equal(NativeGiveHumLevelBufferPlanner.Plan(1, true), NativeGiveHumLevelBufferOutcome.HeroApply, "humbuf hero");
Equal(NativeGiveHumLevelBufferPlanner.NoHeroCode, -9, "humbuf no-hero code -9");

// ---- FAMILY 4: item store/give/send/present ----
Equal(NativeAddStoreItemPlanner.Plan(false, true, true), NativeAddStoreItemOutcome.StorageUnavailable, "store no storage");
Equal(NativeAddStoreItemPlanner.Plan(true, false, true), NativeAddStoreItemOutcome.MakeFailed, "store make fail");
Equal(NativeAddStoreItemPlanner.Plan(true, true, false), NativeAddStoreItemOutcome.AddFailed, "store add fail");
Equal(NativeAddStoreItemPlanner.Plan(true, true, true), NativeAddStoreItemOutcome.Added, "store added");
Equal(NativeAddStoreItemPlanner.ResolveReturn(NativeAddStoreItemOutcome.Added), true, "store added=true");
Equal(NativeAddStoreItemPlanner.ResolveReturn(NativeAddStoreItemOutcome.AddFailed), false, "store addfail=false");

Equal(NativeGiveItemWithDuraPlanner.Plan(4, 5), NativeGiveItemWithDuraOutcome.BagFull, "givedura bag full");
Equal(NativeGiveItemWithDuraPlanner.Plan(5, 5), NativeGiveItemWithDuraOutcome.LoopMakeAdd, "givedura exact fits");
Equal(NativeGiveItemWithDuraPlanner.Plan(6, 5), NativeGiveItemWithDuraOutcome.LoopMakeAdd, "givedura fits");
Equal(NativeGiveItemWithDuraPlanner.ClampDura(10, 5), 5, "givedura clamp to max");
Equal(NativeGiveItemWithDuraPlanner.ClampDura(3, 5), 3, "givedura keep requested");

Equal(NativeSendItemsToOtherPlanner.Plan(false, true, true), NativeSendItemsToOtherOutcome.TargetOffline, "send offline");
Equal(NativeSendItemsToOtherPlanner.Plan(true, false, true), NativeSendItemsToOtherOutcome.TargetRejected, "send rejected");
Equal(NativeSendItemsToOtherPlanner.Plan(true, true, false), NativeSendItemsToOtherOutcome.SelfPreconditionFailed, "send self precond");
Equal(NativeSendItemsToOtherPlanner.Plan(true, true, true), NativeSendItemsToOtherOutcome.Sent, "send ok");
Equal((int)NativeSendItemsToOtherOutcome.TargetOffline, -1, "send code -1");
Equal((int)NativeSendItemsToOtherOutcome.Sent, 1, "send code 1");

Equal(NativeGiveItemsToOtherPlanner.Plan(false, true, true, true, true, true, true, true), NativeGiveItemsToOtherOutcome.BadTargetOrSelf, "give bad target");
Equal(NativeGiveItemsToOtherPlanner.Plan(true, false, true, true, true, true, true, true), NativeGiveItemsToOtherOutcome.NegativeAmount, "give negative");
Equal(NativeGiveItemsToOtherPlanner.Plan(true, true, false, true, true, true, true, true), NativeGiveItemsToOtherOutcome.InvalidItem, "give bad item");
Equal(NativeGiveItemsToOtherPlanner.Plan(true, true, true, false, true, true, true, true), NativeGiveItemsToOtherOutcome.TargetOffline, "give offline");
Equal(NativeGiveItemsToOtherPlanner.Plan(true, true, true, true, true, true, true, true), NativeGiveItemsToOtherOutcome.Success, "give direct success");
Equal(NativeGiveItemsToOtherPlanner.Plan(true, true, true, true, true, true, false, true), NativeGiveItemsToOtherOutcome.DirectSendFailed, "give direct send fail");
Equal(NativeGiveItemsToOtherPlanner.Plan(true, true, true, true, true, false, false, true), NativeGiveItemsToOtherOutcome.ConfirmPathFailed, "give direct-unaffordable -> code 4");
Equal(NativeGiveItemsToOtherPlanner.Plan(true, true, true, true, false, false, false, true), NativeGiveItemsToOtherOutcome.Success, "give confirm-mode success");
Equal(NativeGiveItemsToOtherPlanner.Plan(true, true, true, true, false, false, false, false), NativeGiveItemsToOtherOutcome.ConfirmPathFailed, "give confirm-mode fail");
Equal((int)NativeGiveItemsToOtherOutcome.InvalidItem, -1, "give code -1");

Equal(NativePresentItemPlanner.Plan(false, true, true, true, false, true, true, true), NativePresentItemOutcome.BadArgs, "present bad name");
Equal(NativePresentItemPlanner.Plan(true, false, true, true, false, true, true, true), NativePresentItemOutcome.BadArgs, "present count<1");
Equal(NativePresentItemPlanner.Plan(true, true, false, true, false, true, true, true), NativePresentItemOutcome.ItemNotFound, "present item not found");
Equal(NativePresentItemPlanner.Plan(true, true, true, false, false, true, true, true), NativePresentItemOutcome.TargetOffline, "present offline");
Equal(NativePresentItemPlanner.Plan(true, true, true, true, true, true, true, true), NativePresentItemOutcome.TargetIsSelf, "present self");
Equal(NativePresentItemPlanner.Plan(true, true, true, true, false, false, true, true), NativePresentItemOutcome.GenderMismatch, "present gender");
Equal(NativePresentItemPlanner.Plan(true, true, true, true, false, true, false, true), NativePresentItemOutcome.TargetBagInsufficient, "present target bag");
Equal(NativePresentItemPlanner.Plan(true, true, true, true, false, true, true, false), NativePresentItemOutcome.NotEnoughItems, "present not enough");
Equal(NativePresentItemPlanner.Plan(true, true, true, true, false, true, true, true), NativePresentItemOutcome.Success, "present success");
Equal((int)NativePresentItemOutcome.NotEnoughItems, -5, "present code -5");

Equal(NativeGoodsQueryPlanner.PlanGetGoodsCurrentStorage(false), NativeManagerGateOutcome.ManagerAbsent, "goods mgr absent");
Equal(NativeGoodsQueryPlanner.PlanGetGoodsCurrentStorage(true), NativeManagerGateOutcome.Delegate, "goods delegate");

// ---- FAMILY 5: castle / vote / act ----
Equal(NativeGetCastleGiftPlanner.Plan(false, true, true), NativeGetCastleGiftOutcome.Ineligible, "castlegift no guild");
Equal(NativeGetCastleGiftPlanner.Plan(true, false, true), NativeGetCastleGiftOutcome.Ineligible, "castlegift no mgr");
Equal(NativeGetCastleGiftPlanner.Plan(true, true, false), NativeGetCastleGiftOutcome.Ineligible, "castlegift ord 0");
Equal(NativeGetCastleGiftPlanner.Plan(true, true, true), NativeGetCastleGiftOutcome.Delegate, "castlegift delegate");
Equal(NativeCastleVoteDelegatePlanner.Plan(false), NativeManagerGateOutcome.ManagerAbsent, "castlevote mgr absent");
Equal(NativeCastleVoteDelegatePlanner.Plan(true), NativeManagerGateOutcome.Delegate, "castlevote delegate");
Equal(NativeCastleVoteDelegatePlanner.TakeCastleStoneManagerAbsentCode, -1, "takecastlestone absent -1");
Equal(NativeUpdateEverydayActOrderPlanner.Plan(false, true), NativeUpdateEverydayActOrderOutcome.ManagerAbsentOrProbeFail, "actorder mgr absent");
Equal(NativeUpdateEverydayActOrderPlanner.Plan(true, false), NativeUpdateEverydayActOrderOutcome.ManagerAbsentOrProbeFail, "actorder probe fail");
Equal(NativeUpdateEverydayActOrderPlanner.Plan(true, true), NativeUpdateEverydayActOrderOutcome.Delegate, "actorder delegate");

// ---- FAMILY 6: diamond ----
Equal(NativeDonateDiamPlanner.Plan(true, false, true, true, true, true, true, false, 500, true, true), NativeDonateDiamOutcome.SelfLocked, "donate self locked");
Equal(NativeDonateDiamPlanner.Plan(false, true, true, true, true, true, true, false, 500, true, true), NativeDonateDiamOutcome.SelfBlocked, "donate self blocked");
Equal(NativeDonateDiamPlanner.Plan(false, false, false, true, true, true, true, false, 500, true, true), NativeDonateDiamOutcome.EmptySpec, "donate empty spec");
Equal(NativeDonateDiamPlanner.Plan(false, false, true, false, true, true, true, false, 500, true, true), NativeDonateDiamOutcome.PreconditionFailed, "donate precond");
Equal(NativeDonateDiamPlanner.Plan(false, false, true, true, false, true, true, false, 500, true, true), NativeDonateDiamOutcome.SelfCooldown, "donate self cooldown");
Equal(NativeDonateDiamPlanner.Plan(false, false, true, true, true, false, true, false, 500, true, true), NativeDonateDiamOutcome.ParseFailed, "donate parse fail");
Equal(NativeDonateDiamPlanner.Plan(false, false, true, true, true, true, false, false, 500, true, true), NativeDonateDiamOutcome.TargetOffline, "donate target offline");
Equal(NativeDonateDiamPlanner.Plan(false, false, true, true, true, true, true, true, 500, true, true), NativeDonateDiamOutcome.TargetIsSelf, "donate target self");
Equal(NativeDonateDiamPlanner.Plan(false, false, true, true, true, true, true, false, 0, true, true), NativeDonateDiamOutcome.InvalidAmount, "donate amount 0");
Equal(NativeDonateDiamPlanner.Plan(false, false, true, true, true, true, true, false, 1000, true, true), NativeDonateDiamOutcome.InvalidAmount, "donate amount 1000");
Equal(NativeDonateDiamPlanner.Plan(false, false, true, true, true, true, true, false, 500, false, true), NativeDonateDiamOutcome.InsufficientDiamonds, "donate insufficient");
Equal(NativeDonateDiamPlanner.Plan(false, false, true, true, true, true, true, false, 500, true, false), NativeDonateDiamOutcome.TargetIneligible, "donate target ineligible");
Equal(NativeDonateDiamPlanner.Plan(false, false, true, true, true, true, true, false, 500, true, true), NativeDonateDiamOutcome.Transfer, "donate transfer");
Equal(NativeReqBuildDiamondPlanner.Plan(false, 500), NativeReqBuildDiamondOutcome.Unavailable, "reqbuild unavailable");
Equal(NativeReqBuildDiamondPlanner.Plan(true, 0), NativeReqBuildDiamondOutcome.InvalidAmount, "reqbuild amount 0");
Equal(NativeReqBuildDiamondPlanner.Plan(true, 1001), NativeReqBuildDiamondOutcome.InvalidAmount, "reqbuild amount 1001");
Equal(NativeReqBuildDiamondPlanner.Plan(true, 1), NativeReqBuildDiamondOutcome.Dispatch, "reqbuild amount 1");
Equal(NativeReqBuildDiamondPlanner.Plan(true, 1000), NativeReqBuildDiamondOutcome.Dispatch, "reqbuild amount 1000");

// ---- FAMILY 7: misc ----
Equal(NativeDecJiaYouPointPlanner.Plan(0), NativeDecJiaYouPointOutcome.NonPositive, "decjiayou 0");
Equal(NativeDecJiaYouPointPlanner.Plan(-1), NativeDecJiaYouPointOutcome.NonPositive, "decjiayou neg");
Equal(NativeDecJiaYouPointPlanner.Plan(5), NativeDecJiaYouPointOutcome.SubtractClamped, "decjiayou pos");
Equal(NativeDecJiaYouPointPlanner.Resolve(10, 5), 5L, "decjiayou 10-5");
Equal(NativeDecJiaYouPointPlanner.Resolve(3, 5), 0L, "decjiayou clamp 0");
// #16 shadow-var dedicated-field bindings (live PasApiBridge binds these two).
Equal(NativePlayerShadowFieldBindings.PlatLvOffset, 0xB85, "#16 PlatLv field offset +0xB85");
Equal(NativePlayerShadowFieldBindings.PlatLvReadWrite, true, "#16 PlatLv is RW");
Equal(NativePlayerShadowFieldBindings.JiaYouPointOffset, 0xAF0, "#16 JiaYouPoint field offset +0xAF0");
Equal(NativePlayerShadowFieldBindings.JiaYouPointReadOnly, true, "#16 JiaYouPoint is RO");
Equal(NativeDecJiaYouPointPlanner.PointOffset, NativePlayerShadowFieldBindings.JiaYouPointOffset, "#16 decjiayou mutates the JiaYouPoint field");
Equal(NativeGetCreateTimePlanner.IsUnconditionalGetter, true, "getcreatetime unconditional");
Equal(NativeReqStartTransferAreaPlanner.Plan(0, false), NativeReqStartTransferAreaOutcome.InvalidAreaType, "transfer area 0");
Equal(NativeReqStartTransferAreaPlanner.Plan(4, false), NativeReqStartTransferAreaOutcome.InvalidAreaType, "transfer area 4");
Equal(NativeReqStartTransferAreaPlanner.Plan(2, true), NativeReqStartTransferAreaOutcome.AlreadyThere, "transfer already there");
Equal(NativeReqStartTransferAreaPlanner.Plan(1, false), NativeReqStartTransferAreaOutcome.Transfer, "transfer area 1");
Equal(NativeReqStartTransferAreaPlanner.Plan(3, false), NativeReqStartTransferAreaOutcome.Transfer, "transfer area 3");
Equal(NativeStartPaoDianPlanner.Plan(false, true), NativeStartPaoDianOutcome.NoEnvOrObject, "paodian no env");
Equal(NativeStartPaoDianPlanner.Plan(true, false), NativeStartPaoDianOutcome.NoEnvOrObject, "paodian no obj");
Equal(NativeStartPaoDianPlanner.Plan(true, true), NativeStartPaoDianOutcome.Dispatch, "paodian dispatch");
Equal(NativePsAddCretHpPlanner.Plan(5, 10), NativePsAddCretHpOutcome.LimitReached, "crethp limit reached");
Equal(NativePsAddCretHpPlanner.Plan(0, 10), NativePsAddCretHpOutcome.AddAndCount, "crethp maxcnt<=0");
Equal(NativePsAddCretHpPlanner.Plan(15, 10), NativePsAddCretHpOutcome.AddAndCount, "crethp maxcnt>count");
Equal(NativePsAddCretHpPlanner.Plan(-1, 10), NativePsAddCretHpOutcome.AddAndCount, "crethp maxcnt -1");
Equal(NativePsAddCretHpPlanner.ResolveCount(10), 11, "crethp count++");
Equal(NativeUnconditionalNotifyPlanner.BothUnconditional, true, "unconditional notify");

// ---- evidence: a few wrapper addresses ----
Equal(NativeDonateDiamPlanner.WrapperAddress, 0x006C7E38, "donatediam addr");
Equal(NativePresentItemPlanner.WrapperAddress, 0x006EBB6C, "presentitem addr");
Equal(NativeGiveItemsToOtherPlanner.WrapperAddress, 0x006E93D4, "giveitemstoother addr");
Equal(NativePsAddCretHpPlanner.WrapperAddress, 0x00772D64, "psaddcrethp addr");

Console.WriteLine($"PASS NativeRegisteredBodyScriptApiLaddersCompatCheck: {checks} checks");
return 0;
