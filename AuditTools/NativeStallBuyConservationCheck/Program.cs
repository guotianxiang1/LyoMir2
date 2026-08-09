using GameSvr;
using GameSvr.Services;
using SystemModule;

using Exec = GameSvr.Services.NativeStallBuyExecutor;

// ================================================================================================
// CONSERVATION PROOF for the type-0 (gold) stall BUY finalize (NativeStallBuyExecutor.Execute), the
// economy-critical CM_BUY_STALLITEM (4426) core reversed in staging/stall_buy_executor_20260801.md.
// The seam is PURE (no player / store / I/O — all coin+item motion runs through a single `total` and
// two IO delegates), so every safety property is provable in isolation here:
//
//   (a) MONEY conservation: per BUY, buyer -total == seller +total (Δ total gold == 0), NO fee.
//   (b) ITEM conservation: out-of-stall == into-buyer, for a whole stack, a non-stackable, and a
//       Dura-conserving stackable split — total Dura is invariant across the move.
//   (c) ALL-OR-NOTHING: a bag-full seat AND a failed settlement-mail credit each leave the buyer's
//       gold AND the stall item completely UNCHANGED (no partial mutation).
//   (d) type-1 (balance/元宝) stays DORMANT: a faithful external-boundary reject, no in-process debit.
//
// The reject ladder is pinned to the reversed rungs (-1/-5/-4/-2/-3/-6), matching the dormant model in
// NativeStallWriteTransaction.BuyItem (pinned separately by NativeStallWriteCompatCheck).
// ================================================================================================

const int MaxBag = Grobal2.MAXBAGITEM;

try
{
    VerifyRejectLadder();
    VerifyWholeStackableConservation();
    VerifyPartialSplitConservation();
    VerifyNonStackableConservation();
    VerifyBagFullAllOrNothing();
    VerifyMailFailAllOrNothing();
    VerifyType1Dormant();
    VerifyNoFeeExact();

    Console.WriteLine(
        "PASS NativeStallBuyConservationCheck 4426(type0) ladder=(-1/-5/-4/-2/-3/-6) "
        + "money(buyer-total==seller+total,no-fee) item(out==in;whole/split/nonstack;Dura-conserved) "
        + "all-or-nothing(bagfull+mailfail=>unchanged) type1=dormant-reject");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeStallBuyConservationCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

static NativeStallItem StackItem(int uprice, int stock, int moneyType = 0) =>
    new NativeStallItem
    {
        Item = new TUserItem { wIndex = 7, Dura = (ushort)stock, DuraMax = 5000, ClientItemID = 11 },
        UnitPrice = uprice,
        MoneyType = moneyType,
        ItemCount = stock,
        DbIdx = 501,
    };

static NativeStallItem NonStackItem(int uprice, int moneyType = 0) =>
    new NativeStallItem
    {
        Item = new TUserItem { wIndex = 21, Dura = 40, DuraMax = 40, ClientItemID = 22 },
        UnitPrice = uprice,
        MoneyType = moneyType,
        ItemCount = 1,
        DbIdx = 502,
    };

// Drive Execute exactly as the wrapper does: a modeled buyer bag (capped at MAXBAGITEM) + gold, and a
// seller ledger credited by the settlement-mail delegate. Applies BuyerGoldDelta on success (the wrapper's
// m_nGold step). Returns everything the invariants need.
static Result RunBuy(NativeStallItem stallItem, bool isStackable, int count, long buyerGold,
    bool buyEnabled = true, bool targetActive = true, int bagPrefill = 0, bool mailSucceeds = true)
{
    var bag = new List<TUserItem>();
    for (var i = 0; i < bagPrefill; i++) bag.Add(new TUserItem { wIndex = 1, Dura = 0 });
    long sellerReceived = 0;

    var outcome = Exec.Execute(
        stallItem, isStackable, count, buyerGold,
        buyEnabled, targetActive,
        seatIntoBuyerBag: item =>
        {
            if (bag.Count >= MaxBag) return false;   // bag full (AddItemToBag semantics)
            bag.Add(item);
            return true;
        },
        creditSellerMoney: total =>
        {
            if (!mailSucceeds) return false;         // settlement-mail INSERT failure
            sellerReceived += total;
            return true;
        },
        unseatFromBuyerBag: item => bag.Remove(item));

    long buyerGoldAfter = buyerGold;
    if (outcome.Succeeded) buyerGoldAfter += outcome.BuyerGoldDelta;   // wrapper's m_nGold += delta

    var bagDura = 0;
    foreach (var it in bag) bagDura += it?.Dura ?? 0;

    return new Result(outcome, buyerGoldAfter, sellerReceived, bag.Count, bagDura);
}

// ---- (a) reject ladder: exact rungs, and NO mutation on any reject ----
void VerifyRejectLadder()
{
    var item = StackItem(100, 5);
    long snapDura = item.Item.Dura, snapCount = item.ItemCount;
    bool snapSold = item.IsSold;

    Assert(RunBuy(item, true, 5, 100000, buyEnabled: false).Outcome.Code == Exec.Disabled,
        "reject: disabled -> -1");
    Assert(RunBuy(item, true, 5, 100000, targetActive: false).Outcome.Code == Exec.TargetInactive,
        "reject: target inactive -> -5");
    Assert(RunBuy(null, true, 5, 100000).Outcome.Code == Exec.ItemGone,
        "reject: no item -> -4");
    // money gate (type0): buyerGold < total -> -3.
    Assert(RunBuy(item, true, 5, 499).Outcome.Code == Exec.InsufficientGold,
        "reject: insufficient gold (499 < 500) -> -3");
    // bad qty: stackable stock(5) < count(10), affordable so the qty gate is reached -> -6.
    Assert(RunBuy(item, true, 10, 100000).Outcome.Code == Exec.BadQty,
        "reject: bad qty (stock 5 < 10) -> -6");
    // non-stackable count != 1 -> -6.
    Assert(RunBuy(NonStackItem(100), false, 2, 100000).Outcome.Code == Exec.BadQty,
        "reject: non-stackable count 2 -> -6");

    Assert(item.Item.Dura == snapDura && item.ItemCount == snapCount && item.IsSold == snapSold,
        "reject ladder mutated the stall item");
}

// ---- (a)+(b) whole stackable: money conserved, item moved out-of-stall == into-buyer ----
void VerifyWholeStackableConservation()
{
    var item = StackItem(100, 5);       // stack of 5 @ 100 => total 500
    long duraBefore = item.Item.Dura;
    var r = RunBuy(item, true, 5, 10000);

    Assert(r.Outcome.Succeeded, "whole: success -> 1");
    Assert(r.Outcome.WholeSold && !r.Outcome.PartialSplit, "whole: WholeSold flagged");
    Assert(item.IsSold, "whole: stall item marked isSold");
    // money: buyer -500, seller +500, sum 0, no fee.
    Assert(r.Outcome.Total == 500, "whole: total == 5*100");
    Assert(r.Outcome.BuyerGoldDelta == -500 && r.Outcome.SellerMailMoney == 500, "whole: -500/+500 deltas");
    Assert(r.Outcome.BuyerGoldDelta + r.Outcome.SellerMailMoney == 0, "whole: Δ total gold == 0");
    Assert(r.BuyerGoldAfter == 10000 - 500, "whole: buyer gold 10000 -> 9500");
    Assert(r.SellerReceived == 500 && r.Outcome.SellerMailMoney == r.SellerReceived,
        "whole: seller mail credited exactly the plan amount");
    // item: the SAME booth item is now in the buyer bag; the stall no longer contributes it (sold).
    Assert(ReferenceEquals(r.Outcome.SeatedItem, item.Item), "whole: buyer receives the booth item itself");
    Assert(r.BagCount == 1 && r.BagDura == duraBefore, "whole: buyer holds the whole stack (Dura 5)");
    long stallDuraAfter = item.IsSold ? 0 : item.Item.Dura;
    Assert(r.BagDura + stallDuraAfter == duraBefore, "whole: item conserved (out-of-stall == into-buyer)");
}

// ---- (b) stackable PARTIAL split: total Dura conserved (buyer count + stall remainder == original) ----
void VerifyPartialSplitConservation()
{
    var item = StackItem(100, 5);       // stack of 5; buy 2 => total 200
    long duraBefore = item.Item.Dura;
    var r = RunBuy(item, true, 2, 10000);

    Assert(r.Outcome.Succeeded, "partial: success -> 1");
    Assert(r.Outcome.PartialSplit && !r.Outcome.WholeSold, "partial: PartialSplit flagged");
    Assert(!item.IsSold, "partial: stall row stays listed (not sold)");
    // money.
    Assert(r.Outcome.Total == 200 && r.Outcome.BuyerGoldDelta == -200 && r.Outcome.SellerMailMoney == 200,
        "partial: -200/+200");
    Assert(r.Outcome.BuyerGoldDelta + r.Outcome.SellerMailMoney == 0, "partial: Δ total gold == 0");
    Assert(r.SellerReceived == 200, "partial: seller mail credited 200");
    // item: a NEW split item of exactly count; the booth keeps the remainder.
    Assert(!ReferenceEquals(r.Outcome.SeatedItem, item.Item), "partial: buyer gets a NEW split item (no dup ref)");
    Assert(r.Outcome.SeatedItem.Dura == 2, "partial: buyer item Dura == count(2)");
    Assert(item.Item.Dura == 3 && item.ItemCount == 3, "partial: booth keeps remainder (Dura/itemcount 3)");
    Assert(r.BagDura + item.Item.Dura == duraBefore, "partial: total Dura conserved (2 + 3 == 5)");
    Assert(r.Outcome.SeatedItem.MakeIndex == 0, "partial: split leaves MakeIndex 0 for the wrapper to assign");
}

// ---- (b) non-stackable whole ----
void VerifyNonStackableConservation()
{
    var item = NonStackItem(250);       // single item @ 250, count 1 => total 250
    long duraBefore = item.Item.Dura;
    var r = RunBuy(item, false, 1, 10000);

    Assert(r.Outcome.Succeeded && r.Outcome.WholeSold, "nonstack: success + WholeSold");
    Assert(r.Outcome.Total == 250 && r.Outcome.BuyerGoldDelta == -250 && r.Outcome.SellerMailMoney == 250,
        "nonstack: -250/+250");
    Assert(r.SellerReceived == 250, "nonstack: seller credited 250");
    Assert(ReferenceEquals(r.Outcome.SeatedItem, item.Item) && item.IsSold, "nonstack: booth item seated + sold");
    long stallDuraAfter = item.IsSold ? 0 : item.Item.Dura;
    Assert(r.BagDura + stallDuraAfter == duraBefore, "nonstack: item conserved");
}

// ---- (c) all-or-nothing: bag full => buyer gold + stall item UNCHANGED ----
void VerifyBagFullAllOrNothing()
{
    var item = StackItem(100, 5);
    long duraBefore = item.Item.Dura;
    var r = RunBuy(item, true, 5, 10000, bagPrefill: MaxBag);   // bag already full

    Assert(r.Outcome.Code == Exec.SeatFailed, "bagfull: seat fail -> -5");
    Assert(!r.Outcome.Succeeded, "bagfull: not a success");
    Assert(r.Outcome.BuyerGoldDelta == 0 && r.Outcome.SellerMailMoney == 0, "bagfull: no money deltas");
    Assert(r.BuyerGoldAfter == 10000, "bagfull: buyer gold UNCHANGED");
    Assert(r.SellerReceived == 0, "bagfull: seller NOT credited");
    Assert(!item.IsSold && item.Item.Dura == duraBefore && item.ItemCount == 5, "bagfull: stall item UNCHANGED");
    Assert(r.BagCount == MaxBag, "bagfull: nothing added to the (full) bag");
}

// ---- (c) all-or-nothing: settlement-mail credit fails => item un-seated, everything UNCHANGED ----
void VerifyMailFailAllOrNothing()
{
    // whole
    var whole = StackItem(100, 5);
    long wDura = whole.Item.Dura;
    var rw = RunBuy(whole, true, 5, 10000, mailSucceeds: false);
    Assert(rw.Outcome.Code == Exec.SellerCreditFailed, "mailfail(whole): -> -5");
    Assert(rw.Outcome.BuyerGoldDelta == 0 && rw.SellerReceived == 0, "mailfail(whole): no money moved");
    Assert(rw.BuyerGoldAfter == 10000, "mailfail(whole): buyer gold UNCHANGED");
    Assert(!whole.IsSold && whole.Item.Dura == wDura && whole.ItemCount == 5, "mailfail(whole): stall UNCHANGED");
    Assert(rw.BagCount == 0, "mailfail(whole): item un-seated from the bag");

    // partial (proves the split source Dura is NOT decremented on the abort)
    var part = StackItem(100, 5);
    long pDura = part.Item.Dura;
    var rp = RunBuy(part, true, 2, 10000, mailSucceeds: false);
    Assert(rp.Outcome.Code == Exec.SellerCreditFailed, "mailfail(partial): -> -5");
    Assert(rp.BuyerGoldAfter == 10000 && rp.SellerReceived == 0, "mailfail(partial): no money moved");
    Assert(!part.IsSold && part.Item.Dura == pDura && part.ItemCount == 5,
        "mailfail(partial): booth stock UNCHANGED (no split committed)");
    Assert(rp.BagCount == 0, "mailfail(partial): split item un-seated");
}

// ---- (d) type-1 (balance) stays DORMANT: faithful reject, no in-process debit, no item move ----
void VerifyType1Dormant()
{
    var item = StackItem(100, 5, moneyType: 1);
    long duraBefore = item.Item.Dura;
    var r = RunBuy(item, true, 5, 10000);

    Assert(r.Outcome.IsBalanceDormant, "type1: flagged external-boundary dormant");
    Assert(r.Outcome.Code == Exec.BalanceExternalDormant, "type1: dormant reject code");
    Assert(!r.Outcome.Succeeded, "type1: not a success");
    Assert(r.Outcome.BuyerGoldDelta == 0 && r.Outcome.SellerMailMoney == 0, "type1: NO in-process debit/credit");
    Assert(r.BuyerGoldAfter == 10000 && r.SellerReceived == 0, "type1: buyer gold + seller UNCHANGED");
    Assert(!item.IsSold && item.Item.Dura == duraBefore, "type1: stall item UNCHANGED (no item move)");
    Assert(r.BagCount == 0, "type1: no item seated");

    // Balance insufficiency is still the type-1 gate rung (-2), before the dormant boundary.
    Assert(RunBuy(StackItem(100, 5, moneyType: 1), true, 5, 499).Outcome.Code == Exec.InsufficientBalance,
        "type1: insufficient balance -> -2");
}

// ---- (a) NO FEE: buyer pays exactly count*uprice; seller receives exactly count*uprice ----
void VerifyNoFeeExact()
{
    foreach (var (uprice, stock, count) in new[] { (137, 9, 9), (1000, 7, 3), (1, 5000, 5000) })
    {
        var item = StackItem(uprice, stock);
        long expected = (long)uprice * count;
        var r = RunBuy(item, true, count, long.MaxValue / 2);
        Assert(r.Outcome.Succeeded, $"nofee: buy {count}@{uprice} success");
        Assert(-r.Outcome.BuyerGoldDelta == expected, $"nofee: buyer pays exactly {expected}");
        Assert(r.SellerReceived == expected, $"nofee: seller receives exactly {expected}");
        Assert(-r.Outcome.BuyerGoldDelta == r.SellerReceived, "nofee: buyer-out == seller-in (no skim)");
    }
}

readonly record struct Result(
    NativeStallBuyOutcome Outcome, long BuyerGoldAfter, long SellerReceived, int BagCount, int BagDura);
