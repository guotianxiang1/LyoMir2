using GameSvr;
using GameSvr.Services;
using SystemModule;

using Op = GameSvr.NativeStallOp;

// Contract check for the dormant native player-stall (摆摊) write model 4419/4420/4421/4422/4424/
// 4426/4467, locked against the sub_6E78D4 feature gate and the manager ladders sub_61D294 /
// sub_61D3E0 / sub_61BC7C / sub_61BECC / sub_61D4F0 / sub_61C8E0 / sub_61C80C.

const int Silent = int.MinValue;

try
{
    VerifyConstants();
    VerifyGate();
    VerifySetTimeLevel();
    VerifySetName();
    VerifyAddItem();
    VerifyDelItem();
    VerifyStartStall();
    VerifyPauseStall();
    VerifyBuyItem();
    VerifyMessageStall();
    VerifyBoothSetupExecutor();
    VerifyItemMoveConservation();
    VerifyAddItemConservation();

    Console.WriteLine(
        "PASS NativeStallWriteCompatCheck gate=sub_6E78D4 4419=(0/-3/-2/-3/-1/1) 4420=(-3/-1/-2/1) "
        + "4421=(-2/-3/-5/-4/-1/1) 4422=(-1/1) 4424=(-9/-4/-7/-8/core) 4425=(-1/-1/close) "
        + "4426=(-1/-5/-4/-2/-3/-6/fin) "
        + "4467=(silent/-1/-2/1) boothexec(Δgold=Δitems=0) itemmove(out==in) addmove(Dura-conserved) dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeStallWriteCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

static int Eval(Op op, NativeStallContext c) =>
    NativeStallWriteTransaction.Evaluate(op, c);

void VerifyConstants()
{
    Assert(NativeStallWriteTransaction.NoResponse == int.MinValue, "NoResponse sentinel");
    Assert(NativeStallWriteTransaction.VtblSendDefMessage == 0x250, "send slot");
    Assert((int)Op.SetTimeLevel == 4419 && (int)Op.SetName == 4420 && (int)Op.AddItem == 4421
        && (int)Op.DelItem == 4422 && (int)Op.StartStall == 4424 && (int)Op.PauseStall == 4425
        && (int)Op.BuyItem == 4426 && (int)Op.MessageStall == 4467, "op idents");
}

void VerifyGate()
{
    // sub_6E78D4 closed -> no response for any op.
    foreach (Op op in Enum.GetValues<Op>())
        Assert(Eval(op, new NativeStallContext { FeatureEnabled = false }) == Silent,
            $"gate closed {op} -> silent");
}

void VerifySetTimeLevel()
{
    // codec-fidelity 2026-08-01 sub_61D294->sub_61D6B8 (first-fail-wins): create-fail 0(silent) /
    // no-config -3 / duration>maxDur -2 / name-gate -3 / can't-afford -1 / else 1.
    Assert(Eval(Op.SetTimeLevel, new NativeStallContext { SetTimeLevelRecordCreated = false }) == Silent,
        "4419 create-fail -> silent(0)");
    Assert(Eval(Op.SetTimeLevel, new NativeStallContext
    { SetTimeLevelRecordCreated = true, SetTimeLevelConfigPresent = false }) == -3,
        "4419 no config -> -3");
    Assert(Eval(Op.SetTimeLevel, new NativeStallContext
    { SetTimeLevelRecordCreated = true, SetTimeLevelConfigPresent = true, SetTimeLevelDurationWithinMax = false }) == -2,
        "4419 duration>maxDur -> -2");
    Assert(Eval(Op.SetTimeLevel, new NativeStallContext
    { SetTimeLevelRecordCreated = true, SetTimeLevelConfigPresent = true, SetTimeLevelDurationWithinMax = true,
      SetTimeLevelNameGateOk = false }) == -3,
        "4419 name-gate -> -3");
    Assert(Eval(Op.SetTimeLevel, new NativeStallContext
    { SetTimeLevelRecordCreated = true, SetTimeLevelConfigPresent = true, SetTimeLevelDurationWithinMax = true,
      SetTimeLevelNameGateOk = true, SetTimeLevelCanAfford = false }) == -1,
        "4419 can't-afford -> -1");
    Assert(Eval(Op.SetTimeLevel, new NativeStallContext
    { SetTimeLevelRecordCreated = true, SetTimeLevelConfigPresent = true, SetTimeLevelDurationWithinMax = true,
      SetTimeLevelNameGateOk = true, SetTimeLevelCanAfford = true }) == 1,
        "4419 success -> 1");
}

void VerifySetName()
{
    Assert(Eval(Op.SetName, new NativeStallContext { StallRecordFound = false }) == Silent,
        "4420 no record -> silent");
    Assert(Eval(Op.SetName, new NativeStallContext { StallRecordFound = true, StallRunning = true }) == -3,
        "4420 running -> -3");
    Assert(Eval(Op.SetName, new NativeStallContext { StallRecordFound = true, NameTooLong = true }) == -1,
        "4420 name too long -> -1");
    Assert(Eval(Op.SetName, new NativeStallContext { StallRecordFound = true, NameEmpty = true }) == -2,
        "4420 empty name -> -2");
    Assert(Eval(Op.SetName, new NativeStallContext { StallRecordFound = true }) == 1,
        "4420 success -> 1");
}

void VerifyAddItem()
{
    Assert(Eval(Op.AddItem, new NativeStallContext { StallRecordFound = false }) == -2,
        "4421 no stall -> -2");
    Assert(Eval(Op.AddItem, new NativeStallContext { StallRecordFound = true, AddItemFound = false }) == -3,
        "4421 item not found -> -3");
    Assert(Eval(Op.AddItem, new NativeStallContext
    { StallRecordFound = true, AddItemFound = true, AddItemLocked = true }) == -5, "4421 item locked -> -5");
    Assert(Eval(Op.AddItem, new NativeStallContext
    { StallRecordFound = true, AddItemFound = true, AddCountValid = false }) == -4, "4421 count invalid -> -4");
    Assert(Eval(Op.AddItem, new NativeStallContext
    { StallRecordFound = true, AddItemFound = true, AddCountValid = true, AddSucceeded = false }) == -1,
        "4421 add failed -> -1");
    Assert(Eval(Op.AddItem, new NativeStallContext
    { StallRecordFound = true, AddItemFound = true, AddCountValid = true, AddSucceeded = true }) == 1,
        "4421 success -> 1");
}

void VerifyDelItem()
{
    Assert(Eval(Op.DelItem, new NativeStallContext { DelSucceeded = false }) == -1, "4422 fail -> -1");
    Assert(Eval(Op.DelItem, new NativeStallContext { DelSucceeded = true }) == 1, "4422 success -> 1");
}

void VerifyStartStall()
{
    Assert(Eval(Op.StartStall, new NativeStallContext { MapAllowsStall = false }) == -9,
        "4424 map disallows -> -9");
    Assert(Eval(Op.StartStall, new NativeStallContext { StallRecordFound = false }) == -4,
        "4424 no record -> -4");
    Assert(Eval(Op.StartStall, new NativeStallContext { StallRecordFound = true, StartPrecheckA = false }) == -7,
        "4424 precheck A -> -7");
    Assert(Eval(Op.StartStall, new NativeStallContext { StallRecordFound = true, StartPrecheckB = false }) == -8,
        "4424 precheck B -> -8");
    Assert(Eval(Op.StartStall, new NativeStallContext { StallRecordFound = true, StartCoreResult = 3 }) == 3,
        "4424 start core success -> passthrough");
    Assert(Eval(Op.StartStall, new NativeStallContext { StallRecordFound = true, StartCoreResult = 0 }) == Silent,
        "4424 start core 0 -> silent");
}

void VerifyPauseStall()
{
    Assert(Eval(Op.PauseStall, new NativeStallContext { StallRecordFound = false }) == -1,
        "4425 no record -> -1");
    Assert(Eval(Op.PauseStall, new NativeStallContext { StallRecordFound = true, PauseOwnerResolved = false }) == -1,
        "4425 owner unresolved -> -1");
    Assert(Eval(Op.PauseStall, new NativeStallContext
    { StallRecordFound = true, PauseOwnerResolved = true, PauseCloseResult = 1 }) == 1,
        "4425 close success -> passthrough");
    Assert(Eval(Op.PauseStall, new NativeStallContext
    { StallRecordFound = true, PauseOwnerResolved = true, PauseCloseResult = 0 }) == Silent,
        "4425 close 0 -> silent");
}

void VerifyBuyItem()
{
    Assert(Eval(Op.BuyItem, new NativeStallContext { BuyEnabled = false }) == -1, "4426 disabled -> -1");
    Assert(Eval(Op.BuyItem, new NativeStallContext { BuyEnabled = true, BuyTargetStallActive = false }) == -5,
        "4426 target inactive -> -5");
    Assert(Eval(Op.BuyItem, new NativeStallContext
    { BuyEnabled = true, BuyTargetStallActive = true, BuyItemStillPresent = false }) == -4, "4426 item gone -> -4");
    Assert(Eval(Op.BuyItem, new NativeStallContext
    { BuyEnabled = true, BuyTargetStallActive = true, BuyItemStillPresent = true, BuyMoneyType = 1,
      BuyerHasEnoughMoney = false }) == -2, "4426 type1 insufficient -> -2");
    Assert(Eval(Op.BuyItem, new NativeStallContext
    { BuyEnabled = true, BuyTargetStallActive = true, BuyItemStillPresent = true, BuyMoneyType = 0,
      BuyerHasEnoughMoney = false }) == -3, "4426 type0 insufficient -> -3");
    Assert(Eval(Op.BuyItem, new NativeStallContext
    { BuyEnabled = true, BuyTargetStallActive = true, BuyItemStillPresent = true, BuyerHasEnoughMoney = true,
      BuyQtyValid = false }) == -6, "4426 bad qty -> -6");
    Assert(Eval(Op.BuyItem, new NativeStallContext
    { BuyEnabled = true, BuyTargetStallActive = true, BuyItemStillPresent = true, BuyerHasEnoughMoney = true,
      BuyQtyValid = true, BuyFinalizeResult = 7 }) == 7, "4426 type0 finalize -> passthrough");
    Assert(Eval(Op.BuyItem, new NativeStallContext
    { BuyEnabled = true, BuyTargetStallActive = true, BuyItemStillPresent = true, BuyerHasEnoughMoney = true,
      BuyQtyValid = true, BuyFinalizeResult = 0 }) == Silent, "4426 type1 async 0 -> silent");
}

void VerifyMessageStall()
{
    Assert(Eval(Op.MessageStall, new NativeStallContext { MessagePayloadValid = false }) == Silent,
        "4467 short payload -> silent");
    Assert(Eval(Op.MessageStall, new NativeStallContext { MessagePayloadValid = true, StallRecordFound = false }) == -1,
        "4467 no stall -> -1");
    Assert(Eval(Op.MessageStall, new NativeStallContext
    { MessagePayloadValid = true, StallRecordFound = true, MessageAllowed = false }) == -2, "4467 not allowed -> -2");
    Assert(Eval(Op.MessageStall, new NativeStallContext
    { MessagePayloadValid = true, StallRecordFound = true, MessageAllowed = true }) == 1, "4467 success -> 1");
}

// NativeStallBoothSetup executor: resolved input -> SM code + record mutation, with Δgold=Δitems=0 by
// construction (gold is a read-only value param; no item list is ever touched). tier = StallTradConf tier 1
// (maxDur=12, fee=2000). This is the money/item-free booth-setup leaf's conservation proof.
void VerifyBoothSetupExecutor()
{
    var tier = new NativeStallTradTier { MaxDurationHours = 12, Material1Qty = 2000 };

    // ---- SetTimeLevel: ladder + the DuraTime mutation, no coin/item moved ----
    var ok = NewRecord();
    Assert(NativeStallBoothSetup.EvaluateSetTimeLevel(ok, tier, 10000, 5) == 1, "exec 4419 afford(5*2000<=10000) -> 1");
    Assert(ok.DuraTime == 5, "exec 4419 success sets DuraTime=duration");
    Assert(ok.Items.Count == 0, "exec 4419 moves no items");

    var poor = NewRecord();
    Assert(NativeStallBoothSetup.EvaluateSetTimeLevel(poor, tier, 9999, 5) == -1, "exec 4419 can't-afford(10000>9999) -> -1");
    Assert(poor.DuraTime == 0, "exec 4419 fail leaves DuraTime unset");
    Assert(NativeStallBoothSetup.EvaluateSetTimeLevel(NewRecord(), tier, 10000, 13) == -2, "exec 4419 dur>maxDur -> -2");
    Assert(NativeStallBoothSetup.EvaluateSetTimeLevel(NewRecord(), null, 10000, 5) == -3, "exec 4419 no config -> -3");
    Assert(NativeStallBoothSetup.EvaluateSetTimeLevel(null, tier, 10000, 5) == Silent, "exec 4419 null record -> silent");

    // ---- START: ladder + the Running mutation, no coin/item moved ----
    var run = NewRecord();
    run.DuraTime = 5;
    Assert(NativeStallBoothSetup.EvaluateStart(run, true, true, true, tier, 10000) == 1, "exec 4424 afford -> 1");
    Assert(run.Status == StallRecordStatus.Running, "exec 4424 success sets Running");
    Assert(run.Items.Count == 0, "exec 4424 moves no items");

    var s = NewRecord();
    s.DuraTime = 5;
    Assert(NativeStallBoothSetup.EvaluateStart(s, false, true, true, tier, 10000) == -9, "exec 4424 map disallows -> -9");
    Assert(NativeStallBoothSetup.EvaluateStart(null, true, true, true, tier, 10000) == -4, "exec 4424 no record -> -4");
    Assert(NativeStallBoothSetup.EvaluateStart(s, true, false, true, tier, 10000) == -7, "exec 4424 precheckA -> -7");
    Assert(NativeStallBoothSetup.EvaluateStart(s, true, true, false, tier, 10000) == -8, "exec 4424 precheckB -> -8");
    Assert(NativeStallBoothSetup.EvaluateStart(s, true, true, true, tier, 0) == -1, "exec 4424 can't-afford -> -1");
    Assert(s.Status == StallRecordStatus.Initial, "exec 4424 fail leaves status Initial");
}

// NativeStallItemMove (DEL 4422 / PAUSE 4425): whole-item bag<->stall transfers, items-out == items-in by
// construction (add-to-bag before de-list, fail-safe on a full bag). No split (predicate-independent).
void VerifyItemMoveConservation()
{
    // DEL: keyed by ClientItemID (item+0x18), NOT MakeIndex — the item moves stall -> bag; total conserved;
    // the SAME object returns (no dup); the de-listed row is handed back for the wrapper's persist + send.
    var bag = new List<TUserItem>();
    var stall = NewRecord();
    var item = new TUserItem { MakeIndex = 500, ClientItemID = 100, wIndex = 5, Dura = 3 };
    stall.Items.Add(new NativeStallItem { Item = item });
    var before = bag.Count + stall.Items.Count;
    Assert(NativeStallItemMove.TryDelItem(bag, stall, 500, out _) == -1, "DEL by MakeIndex(500) misses -> -1");
    Assert(NativeStallItemMove.TryDelItem(bag, stall, 100, out var removed) == 1, "DEL by ClientItemID(100) -> 1");
    Assert(bag.Count == 1 && stall.Items.Count == 0, "DEL moved stall->bag");
    Assert(bag.Count + stall.Items.Count == before, "DEL conserves total item count");
    Assert(ReferenceEquals(bag[0], item) && ReferenceEquals(removed.Item, item),
        "DEL returns the SAME item object (no dup) + hands back the de-listed row");
    Assert(NativeStallItemMove.TryDelItem(bag, stall, 100, out _) == -1, "DEL now-empty -> -1");

    // DEL is UNBOUNDED (native sub_73CEA8 has no bag-full check): it returns the item even when the bag
    // already holds a large number of items — never a bag-full -1.
    var bigBag = new List<TUserItem>();
    for (var i = 0; i < 60; i++) bigBag.Add(new TUserItem { MakeIndex = i });
    var stallU = NewRecord();
    stallU.Items.Add(new NativeStallItem { Item = new TUserItem { ClientItemID = 200 } });
    var beforeU = bigBag.Count + stallU.Items.Count;
    Assert(NativeStallItemMove.TryDelItem(bigBag, stallU, 200, out _) == 1, "DEL unbounded: returns past a full bag -> 1");
    Assert(stallU.Items.Count == 0 && bigBag.Count + stallU.Items.Count == beforeU, "DEL unbounded conserves + de-lists");

    // PAUSE: all items returned (unbounded); total conserved; all de-listed rows handed back.
    var bagP = new List<TUserItem>();
    var stallP = NewRecord();
    stallP.Items.Add(new NativeStallItem { Item = new TUserItem { MakeIndex = 1 } });
    stallP.Items.Add(new NativeStallItem { Item = new TUserItem { MakeIndex = 2 } });
    var beforeP = bagP.Count + stallP.Items.Count;
    Assert(NativeStallItemMove.ReturnAllItems(bagP, stallP, out var removedP) == 2, "PAUSE returns all 2");
    Assert(bagP.Count == 2 && stallP.Items.Count == 0, "PAUSE all moved to bag");
    Assert(bagP.Count + stallP.Items.Count == beforeP, "PAUSE conserves total item count");
    Assert(removedP.Count == 2, "PAUSE hands back both de-listed rows");
}

// NativeStallItemMove.TryAddItem (ADD 4421): list a bag item onto the stall; TOTAL Dura (bag+stall)
// preserved across whole-move + Dura-conserving split. Stackability (StdMode==7) is resolved by the caller,
// so the seam is pure/auditable. No price gate.
void VerifyAddItemConservation()
{
    // stackable WHOLE (count == Dura): item moves bag->stall, total Dura conserved, same object.
    var bagW = new List<TUserItem>();
    var itemW = new TUserItem { ClientItemID = 1, wIndex = 7, Dura = 5, DuraMax = 100 };
    bagW.Add(itemW);
    var stallW = NewRecord();
    var duraW = TotalDura(bagW, stallW);
    Assert(NativeStallItemMove.TryAddItem(bagW, stallW, itemW, true, 5, 10, 0, out var addedW, out var splitW) == 1,
        "ADD stackable whole -> 1");
    Assert(!splitW && bagW.Count == 0 && stallW.Items.Count == 1, "ADD whole: item moved bag->stall, no split");
    Assert(ReferenceEquals(addedW.Item, itemW), "ADD whole lists the same object (no dup)");
    Assert(TotalDura(bagW, stallW) == duraW, "ADD whole conserves total Dura");

    // stackable SPLIT (count < Dura): source keeps Dura-count, a NEW item of count is listed, total Dura conserved.
    var bagS = new List<TUserItem>();
    var itemS = new TUserItem { ClientItemID = 2, wIndex = 7, Dura = 5, DuraMax = 100 };
    itemS.btValue[10] = 0xAB;
    itemS.btValue[11] = 0xCD;
    bagS.Add(itemS);
    var stallS = NewRecord();
    var duraS = TotalDura(bagS, stallS);
    Assert(NativeStallItemMove.TryAddItem(bagS, stallS, itemS, true, 2, 10, 0, out var addedS, out var splitS) == 1,
        "ADD stackable split -> 1");
    Assert(splitS && bagS.Count == 1 && stallS.Items.Count == 1, "ADD split: source stays in bag, new item listed");
    Assert(itemS.Dura == 3 && addedS.Item.Dura == 2, "ADD split: source keeps Dura-count (3), new item = count (2)");
    Assert(TotalDura(bagS, stallS) == duraS, "ADD split conserves total Dura (3+2==5)");
    Assert(addedS.Item.MakeIndex == 0, "ADD split leaves new MakeIndex 0 for the caller to assign");
    Assert(addedS.Item.btValue[10] == 0xAB && addedS.Item.btValue[11] == 0xCD, "ADD split carries btValue[10..11]");
    Assert(!ReferenceEquals(addedS.Item, itemS), "ADD split lists a NEW item, not the source (no dup)");

    // stackable count mismatch (count > Dura) -> -4, no move.
    var bagM = new List<TUserItem>();
    var itemM = new TUserItem { ClientItemID = 3, wIndex = 7, Dura = 5 };
    bagM.Add(itemM);
    var stallM = NewRecord();
    Assert(NativeStallItemMove.TryAddItem(bagM, stallM, itemM, true, 6, 10, 0, out _, out _) == -4, "ADD count>Dura -> -4");
    Assert(bagM.Count == 1 && stallM.Items.Count == 0, "ADD -4 leaves item in bag (no move)");

    // non-stackable: count==1 whole, else -4.
    var bagN = new List<TUserItem>();
    var itemN = new TUserItem { ClientItemID = 4, wIndex = 10, Dura = 1 };
    bagN.Add(itemN);
    var stallN = NewRecord();
    Assert(NativeStallItemMove.TryAddItem(bagN, stallN, itemN, false, 1, 10, 0, out _, out _) == 1, "ADD non-stackable 1 -> 1");
    Assert(bagN.Count == 0 && stallN.Items.Count == 1, "ADD non-stackable moved");
    var bagN2 = new List<TUserItem>();
    var itemN2 = new TUserItem { ClientItemID = 5, wIndex = 10, Dura = 1 };
    bagN2.Add(itemN2);
    Assert(NativeStallItemMove.TryAddItem(bagN2, NewRecord(), itemN2, false, 2, 10, 0, out _, out _) == -4,
        "ADD non-stackable count!=1 -> -4");
    Assert(bagN2.Count == 1, "ADD non-stackable -4 no move");
}

static int TotalDura(List<TUserItem> bag, NativeStallRecord stall)
{
    var total = 0;
    foreach (var i in bag) total += i?.Dura ?? 0;
    foreach (var si in stall.Items) total += si?.Item?.Dura ?? 0;
    return total;
}

static NativeStallRecord NewRecord() =>
    new NativeStallRecord { OwnerName = "tester", OwnerId = 1 };
