using System;
using System.Collections.Generic;
using GameSvr;

// Dormant-model compat check for NativeAuthAndCreateScriptApiLadders.cs — the
// AUTH family + ClientSellerCancelYbDeal + CreateCampAnimal + CreateSelfCorps +
// CreateSelfGild decision ladders. Every branch of every modeled ladder is
// asserted. Single generic assertion helper (no overloaded local Equal).

int checks = 0;

void Equal<T>(T actual, T expected, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        Console.Error.WriteLine(
            $"FAIL: {label}: expected <{expected}>, got <{actual}>");
        Environment.Exit(1);
    }
}

// --------------------------------------------------------------------------
// CLUSTER 1 — AUTH family
// --------------------------------------------------------------------------
// shared order validator sub_6F9994: 3(invalid) / 2(already) / 1(ok/no-commit) / persist
Equal(NativeAuthenOrderValidator.Validate(0, false, false, 1), 3, "authval order 0 invalid");
Equal(NativeAuthenOrderValidator.Validate(4, false, false, 1), 3, "authval order 4 invalid");
Equal(NativeAuthenOrderValidator.Validate(1, true, false, 1), 2, "authval already authed");
Equal(NativeAuthenOrderValidator.Validate(2, false, false, 99), 1, "authval no-commit success");
Equal(NativeAuthenOrderValidator.Validate(3, false, true, 1), 1, "authval commit persist ok");
Equal(NativeAuthenOrderValidator.Validate(3, false, true, 7), 7, "authval commit persist fail=7");

// ActiveAuthen / ActiveDelAuthen wrapper
Equal(NativeActiveAuthenPlanner.Plan(false, 1), NativeActiveAuthenOutcome.Disabled, "active disabled");
Equal(NativeActiveAuthenPlanner.Plan(true, 1), NativeActiveAuthenOutcome.Success, "active success");
Equal(NativeActiveAuthenPlanner.Plan(true, 2), NativeActiveAuthenOutcome.Failure, "active failure code2");
Equal(NativeActiveAuthenPlanner.Plan(true, 3), NativeActiveAuthenOutcome.Failure, "active failure code3");
Equal(NativeActiveAuthenPlanner.ResolveReturn(false, 5), 0, "active disabled returns 0");
Equal(NativeActiveAuthenPlanner.ResolveReturn(true, 5), 5, "active enabled returns code");
Equal(NativeActiveAuthenPlanner.ActiveAuthenAddress, 0x006F977C, "ActiveAuthen addr");
Equal(NativeActiveAuthenPlanner.ActiveDelAuthenAddress, 0x006F9888, "ActiveDelAuthen addr");

// AuthByHelped wrapper
Equal(NativeAuthByHelpedPlanner.Plan(false, false, true), NativeAuthByHelpedOutcome.NoPending, "abh no pending");
Equal(NativeAuthByHelpedPlanner.Plan(true, true, true), NativeAuthByHelpedOutcome.PrecheckBlocked, "abh precheck blocked");
Equal(NativeAuthByHelpedPlanner.Plan(true, false, false), NativeAuthByHelpedOutcome.Ineligible, "abh ineligible");
Equal(NativeAuthByHelpedPlanner.Plan(true, false, true), NativeAuthByHelpedOutcome.Delegated, "abh delegated");
Equal(NativeAuthByHelpedPlanner.ResolveReturn(NativeAuthByHelpedOutcome.NoPending, 9), 5, "abh nopending=5");
Equal(NativeAuthByHelpedPlanner.ResolveReturn(NativeAuthByHelpedOutcome.PrecheckBlocked, 9), 5, "abh blocked=5");
Equal(NativeAuthByHelpedPlanner.ResolveReturn(NativeAuthByHelpedOutcome.Ineligible, 9), 4, "abh ineligible=4");
Equal(NativeAuthByHelpedPlanner.ResolveReturn(NativeAuthByHelpedOutcome.Delegated, 9), 9, "abh delegated=code");

// HelpOtherAuthen wrapper
Equal(NativeHelpOtherAuthenPlanner.SendsSuccessMessage(1), true, "hoa success msg on 1");
Equal(NativeHelpOtherAuthenPlanner.SendsSuccessMessage(2), false, "hoa no msg on 2");
Equal(NativeHelpOtherAuthenPlanner.ResolveReturn(7), 7, "hoa passthrough return");

// --------------------------------------------------------------------------
// CLUSTER 2 — ClientSellerCancelYbDeal (sub_6CB9F0)
// --------------------------------------------------------------------------
Equal(NativeYbDealSellerCancelPlanner.Plan(false, 5, 1u, true),
    NativeYbDealSellerCancelOutcome.NoCancelableDeal, "ybcancel no precondition");
Equal(NativeYbDealSellerCancelPlanner.Plan(true, 0, 1u, true),
    NativeYbDealSellerCancelOutcome.NoCancelableDeal, "ybcancel count<=0");
Equal(NativeYbDealSellerCancelPlanner.Plan(true, -1, 1u, true),
    NativeYbDealSellerCancelOutcome.NoCancelableDeal, "ybcancel count negative");
Equal(NativeYbDealSellerCancelPlanner.Plan(true, 5, 0u, true),
    NativeYbDealSellerCancelOutcome.NoCancelableDeal, "ybcancel dealId 0");
Equal(NativeYbDealSellerCancelPlanner.Plan(true, 5, 1u, true),
    NativeYbDealSellerCancelOutcome.ExecuteCancel, "ybcancel execute");
Equal(NativeYbDealSellerCancelPlanner.Plan(true, 5, 1u, false),
    NativeYbDealSellerCancelOutcome.RejectNotCancelable, "ybcancel reject");
Equal(NativeYbDealSellerCancelPlanner.CancelWIdent, 0x75, "ybcancel wIdent");

// --------------------------------------------------------------------------
// CLUSTER 3 — CreateCampAnimal (sub_6EB7D8)
// --------------------------------------------------------------------------
Equal(NativeCreateCampAnimalPlanner.AlwaysDispatches, true, "campanimal always dispatch");
Equal(NativeCreateCampAnimalPlanner.NotifyWIdent, 0xFFDB, "campanimal wIdent 0xFFDB");
Equal(NativeCreateCampAnimalPlanner.MessageArgCount, 3, "campanimal 3 msg args");
Equal(NativeCreateCampAnimalPlanner.WrapperAddress, 0x006EB7D8, "campanimal addr");

// --------------------------------------------------------------------------
// CLUSTER 4 — CreateSelfCorps (sub_6ADD08)
// --------------------------------------------------------------------------
Equal(NativeCreateSelfCorpsPlanner.Plan(true),
    NativeCreateSelfCorpsOutcome.AlreadyHasCorps, "corps already has");
Equal(NativeCreateSelfCorpsPlanner.Plan(false),
    NativeCreateSelfCorpsOutcome.DelegateCreate, "corps delegate");
Equal(NativeCreateSelfCorpsPlanner.ResolveReturn(true, 9), 3, "corps already=3");
Equal(NativeCreateSelfCorpsPlanner.ResolveReturn(false, 9), 9, "corps delegate=code");
Equal(NativeCreateSelfCorpsPlanner.ResultWIdent, 0x11AC, "corps result wIdent 4524");

// --------------------------------------------------------------------------
// CLUSTER 5 — CreateSelfGild (sub_6ADDA8)
// --------------------------------------------------------------------------
Equal(NativeCreateSelfGildPlanner.AlwaysDelegates, true, "gild always delegate");
Equal(NativeCreateSelfGildPlanner.ResolveReturn(9), 9, "gild passthrough=code");
Equal(NativeCreateSelfGildPlanner.ResultWIdent, 4564, "gild result wIdent 4564");
Equal(NativeCreateSelfGildPlanner.WrapperAddress, 0x006ADDA8, "gild addr");

Console.WriteLine($"PASS NativeAuthAndCreateScriptApiLaddersCompatCheck: {checks} checks");
return 0;
