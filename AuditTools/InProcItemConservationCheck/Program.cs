using System.Reflection;
using System.Buffers.Binary;
using DBSvr.Core;
using GameSvr;
using SystemModule;

// In-process ITEM-CONSERVATION harness for the dupe / permanent-loss defects fixed in the
// 2026-08-04 item-lifecycle pass. Machine-safety FIRST: SINGLE process, NO network stack,
// NO DBSvr, NO MySQL, NO background engine threads; strictly serial; Environment.Exit at
// the end. Same bootstrap technique as InProcMailRunCheck / InProcEngineRunCheck: build
// the M2Share singletons directly (bypassing GameApp.Initialize and the 30 s DBSvr
// native-def gate), inject the StdItem defs the DBSvr Type2 stream would supply, then
// drive the REAL TPlayObject handlers and assert CONSERVATION over the real containers.
//
// Every assertion below is a dupe-or-loss invariant: after each operation the total number
// of item OBJECT REFERENCES across (bag + equip slots + hero bag + ground) must equal the
// count before, and no reference may appear in two containers at once.
//
// Native contracts asserted (战神, byte-verified over M2Server_reunpacked_20260803):
//
//  A. EQUIP SWAP with a FULL BAG loses nothing — sub_6B7E9C @0x6B8041/@0x6B804C
//     `push 0; xor ecx,ecx; call [vmt+0x248]` then `test al,al`: native CHECKS the
//     displaced item's AddItemToBag. C# discarded the result, so a full bag mid-swap
//     silently destroyed the old gear.
//
//  B. HERO-BAG TRANSFER WHILE DEALING is rejected — sub_6D09D0 @0x6D09ED and
//     sub_6D0B00 @0x6D0B1D `cmp byte [ebx+0x461],0; jne` => -1. Without it a staged
//     trade item can be shunted into the hero bag: deal list + hero bag both hold the
//     same reference => two-container dupe.
//
//  C. UNEQUIP WITH NO BAG SPACE mutates nothing — sub_6B8188 @0x6B81F0
//     `mov dl,1; call [vmt+0x244]; test al,al; je 0x6B82DD` where 0x6B82DD sets
//     esi = -3 (SM_TAKEOFF_FAIL) BEFORE the slot is touched. C# had no pre-gate and ran
//     Dispose() on the player's own item when the add failed.
//
//  D. A FAILED MAIL ATTACHMENT IS LOST, NOT DUPLICATED — sub_70B458 @0x70B4F0/@0x70B4F6
//     `je 0x70B5D9` goes to the loop INCREMENT, and the AttachStatus:=2 write at
//     @0x70B5E3/@0x70B5E9 is reached unconditionally, so the mail is closed even on a
//     partial delivery. The earlier "leave it claimable" hardening rested on native
//     supposedly not hard-deleting claimed mail; it does — clear-all sub_70D2D0
//     @0x70D318/@0x70D321 accepts AttachStatus in {2,3} and sub_70D350 runs sub_70B0F0
//     (INSERT INTO mailitem_b ... SELECT, then delete) and frees the object @0x70D3C6.
//
//  E. A DROPPED UNVERIFIED / GIFT ITEM IS DESTROYED, NOT SCATTERED — sub_73CC98
//     @0x73CD23-0x73CDFB: `cmp byte [esi+0x178],0; jne` / `mov cl,4; call sub_617A38` /
//     `cmp byte [ebx+0xD8],0; je` then `call sub_424B30` (remove) + `sub_768BE0(dx=0x5E)`
//     (GBK notice) + `call sub_404690` (TObject.Free) — it NEVER reaches DropItemDown.
//     C# put it on the ground, where an alt could pick it up (gift/bind laundering).
//
//  F. STAGE-1 ROOT CAUSE: the acquisition stamper sub_7842F8 actually writes
//     word[item+0x34] through the outer AddItemToBag sub_6B7378 (VMT +0x248), and the
//     GMLevel <= 3 gate at @0x6B73A3 suppresses it for GMs.
//
// Evidence goes to stdout and inproc_itemconservation_evidence.txt next to the executable.

int rc = 0;
var evidence = new List<string>();
void Log(string s) { evidence.Add(s); Console.WriteLine("  " + s); }
void Assert(bool cond, string msg) { if (!cond) throw new Exception("ASSERT FAILED: " + msg); }

// ---- non-public REAL TPlayObject handlers driven by reflection (idiom from the sibling harnesses) ----
var miTakeOn = typeof(TPlayObject).GetMethod("ClientTakeOnItems",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(byte), typeof(int), typeof(string) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientTakeOnItems");
var miTakeOff = typeof(TPlayObject).GetMethod("ClientTakeOffItems",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(byte), typeof(int), typeof(string) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientTakeOffItems");
var miDropItem = typeof(TPlayObject).GetMethod("ClientDropItem",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(string), typeof(int) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientDropItem");
var miScatterBagItems = typeof(TPlayObject).GetMethod("ScatterBagItems",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(TBaseObject) }, null)
    ?? throw new MissingMethodException("TPlayObject.ScatterBagItems");
var miDropUseItems = typeof(TPlayObject).GetMethod("DropUseItems",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(TBaseObject) }, null)
    ?? throw new MissingMethodException("TPlayObject.DropUseItems");
var miHeroToHeroBag = typeof(TPlayObject).GetMethod("ClientHeroMoveToHeroBag",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(TProcessMessage) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientHeroMoveToHeroBag");
var miHeroToHumBag = typeof(TPlayObject).GetMethod("ClientHeroMoveToHumBag",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(TProcessMessage) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientHeroMoveToHumBag");

// ---- internal native-mail types (idiom from NativeMailCacheLifecycleCheck / InProcMailRunCheck) ----
var gameAssembly = typeof(TPlayObject).Assembly;
var mailCacheType = gameAssembly.GetType("GameSvr.Services.NativeMailCacheService", true);
var mailRecordType = gameAssembly.GetType("GameSvr.Services.NativeMailRecord", true);
var mailEntryType = gameAssembly.GetType("GameSvr.Services.NativeMailCacheEntry", true);
var miFetchAttach = typeof(TPlayObject).GetMethod("FetchNativeMailAttachments",
    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { mailEntryType }, null)
    ?? throw new MissingMethodException("TPlayObject.FetchNativeMailAttachments");
// The INNER delivery loop == native sub_70B458 itself. The outer FetchNativeMailAttachments
// carries the once-only pre-flight gate (Mail.cs:137, native sub_70B664 @0x70B6A7 `call
// sub_7481F4` / @0x70B6AF `cmp edi,eax; jg`, verified FAITHFUL), so the per-item failure is
// only reachable through the core - exactly the 48-slot race native tolerates at @0x70B4F6,
// and the arm native itself takes at @0x70B294 `call sub_70B458` from the async yuanbao
// callback, which has no pre-flight gate at all.
var miDeliverAttach = typeof(TPlayObject).GetMethod("DeliverNativeMailAttachments",
    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { mailEntryType }, null)
    ?? throw new MissingMethodException("TPlayObject.DeliverNativeMailAttachments");
var fiMailRecipientId = typeof(TPlayObject).GetField("_nativeMailRecipientId",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingFieldException("TPlayObject._nativeMailRecipientId");

try
{
    PrepareConfig();
    BootSingletons();
    InjectNativeDefs();
    Log("BOOT singletons + StdItem defs injected (no GameApp.Initialize, no DBSvr gate, "
        + "no network, no background threads)");

    VerifyStamperContract();
    VerifyEquipSwapFullBagLosesNothing();
    VerifyUnequipNoSpaceMutatesNothing();
    VerifyHeroBagDealingRejected();
    VerifyFailedMailAttachmentIsLostNotDuplicated();
    VerifyUnverifiedDropIsDestroyed();
    VerifyGiftDropIsDestroyed();
    VerifyMode5DropItemDownGate();
    VerifyDeathBagMode5Protection();
    VerifyDeathEquipMode5Protection();
    VerifyDeathEquipReserved08Ordering();
    VerifyHeroDeathEquipWorker();
    VerifyHeroDeathBagWorker();
    VerifyHeroDeathRoutingAndSm917();
    VerifyHumanDeathTailAfterDropException();
    VerifyNativeDeathDropNotice();
    VerifyNativeItemMovementSms();
    VerifyNativeAmuletConsumeGate();

    Console.WriteLine(
        "PASS InProcItemConservationCheck "
        + "stamper=REAL(sub_7842F8 writes item+0x34 via outer sub_6B7378; GMLevel<=3 gate) "
        + "equip-swap-fullbag=REAL(no loss, rollback, sub_6B7E9C @0x6B804C) "
        + "unequip-nospace=REAL(nothing mutated, no Dispose, sub_6B8188 @0x6B81F0 => -3) "
        + "hero-bag-dealing=REAL(both directions rejected -1, sub_6D09D0 @0x6D09ED) "
        + "mail-failed-attachment=REAL(lost not duplicated, AttachStatus 1->2, sub_70B458) "
        + "drop-unverified=REAL(destroyed not scattered, sub_73CC98 @0x73CDEB) "
        + "drop-gift=REAL(destroyed not scattered, item+0xD8) "
        + "drop-mode5=REAL(index5 std+2&0x20; index4 masks not reused) "
        + "death-bag-mode5=REAL(nonzero keeps item before auth/gift Free) "
        + "death-equip-mode5=REAL(player override, random->auth/gift->0x10->mode5) "
        + "death-equip-reserved08=REAL(per-slot std+2&8 before Random/cap) "
        + "hero-death-equip=REAL(16 slots/order/Reserved08/ClassFc/cap) "
        + "hero-death-bag=REAL(reverse/PK>=threshold/ClassFc bypass/range2) "
        + "hero-death-routing=REAL(owner preserved/equip-before-bag/SM917 count*4) "
        + "human-death-exception=REAL(worker catch/search-tick-clear/RM_DEATH) "
        + "death-drop-notice=REAL(sub_73E4C4 owner/mode/RNG/text/raw 0x38FF) "
        + "item-movement-sms=REAL(suffix bit/hero snapshot/0x78 GBK/three death legs) "
        + "amulet-consume-gate=REAL(player+hero found&boConsume matrix) "
        + "single-process no-network no-DBSvr no-MySQL");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL InProcItemConservationCheck: " + ex);
    rc = 1;
}

try
{
    File.WriteAllLines(
        Path.Combine(AppContext.BaseDirectory, "inproc_itemconservation_evidence.txt"),
        evidence);
}
catch { /* evidence file is best-effort */ }

Environment.Exit(rc);

// ===================== F. STAGE-1 ROOT CAUSE: the acquisition stamper ============================

void VerifyStamperContract()
{
    // 战神 sub_7842F8 @0x784307 `test byte [[item+0x1C]+3],2` => Reserved02 & 0x0200.
    var bindOnAcquire = new GoodItem
    {
        Name = "绑定长剑", ItemType = GoodType.ITEM_WEAPON, StdMode = 5,
        NativeReserved02 = 0x0200, DuraMax = 5000
    };
    var plain = new GoodItem
    {
        Name = "普通长剑", ItemType = GoodType.ITEM_WEAPON, StdMode = 5,
        NativeReserved02 = 0, DuraMax = 5000
    };

    // BEFORE the fix the outer layer did not exist at all, so item+0x34 stayed 0 forever
    // and every gate that reads it (drop mode-5, pickup refusal, death-drop, equip gate,
    // CM_1017 merge, pile compat) was inert.
    var player = NewPlayer("stamp-owner", "stamp-user");
    player.m_btPermission = 0;               // 0x6B73A3 `cmp byte [+0x675],3; ja` -> stamp

    var stamped = MakeRawItem(bindOnAcquire);
    Assert(ReadBindWord(stamped) == 0, "fresh item starts with item+0x34 == 0");
    // 0x6B7708 pickup shape: `push 4; mov cl,1; call [vmt+0x248]`.
    Assert(player.AddItemToBag(stamped, 4, true), "outer AddItemToBag seats the item");
    Assert(ReadBindWord(stamped) != 0 || true, "stamp ran (value depends on daysOnline)");
    // daysOnline is 0 in-proc (the +0x780 online base has no C# field yet), so the
    // bind-on-acquire branch writes (0 - 2) & 0xFFFF = 0xFFFE — a NON-ZERO lock word,
    // which is what every downstream gate tests.
    Assert(ReadBindWord(stamped) == 0xFFFE,
        "sub_7842F8 branch 1 wrote word[item+0x34] = (daysOnline-2) & 0xFFFF");
    Log($"STAMPER bind-on-acquire (Reserved02&0x0200): item+0x34 0 -> 0x{ReadBindWord(stamped):X4} "
        + "(sub_7842F8 @0x784319 `sub dx,2` + sub_784718 `mov word[eax+0x34],dx`)");

    // 0x784311-0x784317: the reason ladder selects {1,2,4} only; reason 3 is skipped and
    // reason 0 (63 of the 68 native sites) never stamps.
    var reason3 = MakeRawItem(bindOnAcquire);
    Assert(player.AddItemToBag(reason3, 3, true), "seat reason-3 item");
    Assert(ReadBindWord(reason3) == 0,
        "acquisitionReason 3 is EXCLUDED by the `sub al,1; jne` ladder (@0x784315)");
    var reason0 = MakeRawItem(bindOnAcquire);
    Assert(player.AddItemToBag(reason0, 0, true), "seat reason-0 item");
    Assert(ReadBindWord(reason0) == 0, "acquisitionReason 0 does not stamp");
    Log("STAMPER reason ladder: {1,2,4} stamp; 3 and 0 do not (@0x78430D-0x784317)");

    // A non-bind-class item is never stamped regardless of reason.
    var untouched = MakeRawItem(plain);
    Assert(player.AddItemToBag(untouched, 4, true), "seat plain item");
    Assert(ReadBindWord(untouched) == 0,
        "no Reserved02 bind class -> sub_7842F8 falls straight through (@0x78430B je)");

    // 0x6B73A3 `cmp byte [edi+0x675],3; ja 0x6B73C7` — GM-acquired items are NOT stamped,
    // but the plain add at 0x6B73C7 still runs (the gate skips the stamper, not the add).
    var gm = NewPlayer("gm-owner", "gm-user");
    gm.m_btPermission = 10;
    var gmItem = MakeRawItem(bindOnAcquire);
    Assert(gm.AddItemToBag(gmItem, 4, true), "GM add still seats the item (0x6B73C7)");
    Assert(ReadBindWord(gmItem) == 0,
        "GMLevel > 3 suppresses the stamper (sub_6B7378 @0x6B73A3 `cmp byte[+0x675],3; ja`)");
    Log("STAMPER GM gate: permission 10 seats the item but leaves item+0x34 == 0 "
        + "(@0x6B73A3); permission 0 stamps");

    // stampEnable == false (native `xor ecx,ecx`, 35 of the 68 sites) also suppresses it.
    var noStamp = MakeRawItem(bindOnAcquire);
    Assert(player.AddItemToBag(noStamp, 4, false), "stamper-disabled add still seats");
    Assert(ReadBindWord(noStamp) == 0,
        "stampEnable=false (native `xor ecx,ecx`) suppresses the stamper (@0x6B739F test al,bl)");
}

// ===================== A. EQUIP SWAP WITH A FULL BAG =============================================

void VerifyEquipSwapFullBagLosesNothing()
{
    var player = NewPlayer("swap-owner", "swap-user");
    const byte Slot = 1;                                  // U_WEAPON

    var oldGear = MakeItem("铁剑");
    var newGear = MakeItem("铁剑");
    Assert(oldGear != null && newGear != null, "built two 铁剑 for the swap");
    player.m_UseItems[Slot] = oldGear;                    // already equipped

    // Build the state in which the displaced add GENUINELY FAILS. AddItemToBag rejects at
    // Count >= MAXBAGITEM, and the swap frees exactly one slot (DelBagItem of the
    // candidate), so the add only fails when the bag is OVER-full: Count > MAXBAGITEM
    // before the swap. That is reachable in production because several paths append to
    // m_ItemList directly, bypassing the cap (GM @GetUserItems / @GiveUserItem /
    // @MakeItem, MallManager delivery, NativeHeroRuntimeCodec relog restore) — the same
    // class of over-fill native tolerates because its own gate is only checked on the
    // sub_73D078 path.
    player.m_ItemList.Add(newGear);
    while (player.m_ItemList.Count <= Grobal2.MAXBAGITEM)
        player.m_ItemList.Add(MakeItem("铁剑"));
    Assert(player.m_ItemList.Count > Grobal2.MAXBAGITEM,
        "bag is OVER-full so the displaced AddItemToBag genuinely fails");

    var totalBefore = CountRefs(player);
    var bagBefore = player.m_ItemList.Count;
    var slotBefore = player.m_UseItems[Slot];

    // Drive the REAL handler. With a full bag the swap must either complete with total
    // conservation or roll back completely — never destroy the displaced gear.
    var newGearId = player.EnsureClientItemId(newGear);
    miTakeOn.Invoke(player, new object[] { Slot, newGearId, "铁剑" });

    var totalAfter = CountRefs(player);
    Assert(totalAfter == totalBefore,
        $"equip swap on a FULL bag conserves every item reference "
        + $"({totalBefore} -> {totalAfter}); native sub_6B7E9C @0x6B804C CHECKS the "
        + "displaced AddItemToBag, C# used to discard it and destroy the old gear");
    Assert(!(player.m_ItemList.Contains(newGear) && player.m_UseItems[Slot] == newGear),
        "no reference is in BOTH the bag and the equip slot (the both-containers dupe)");
    Assert(player.m_UseItems[Slot] != null, "the equip slot is never left empty by a swap");
    Assert(player.m_ItemList.Contains(oldGear) || player.m_UseItems[Slot] == oldGear,
        "the DISPLACED item is still reachable (bag or slot) — it was not destroyed");
    // The rollback must be COMPLETE, not partial: native either performs the whole swap
    // (sub_75F044 slot-write + sub_73D140 bag-remove + checked sub_6B7378 add) or leaves
    // the player exactly as it found them. A partial state (old gear gone from the slot
    // AND absent from the bag) is the permanent-loss bug this assertion exists to catch.
    Assert(player.m_ItemList.Contains(newGear) || player.m_UseItems[Slot] == newGear,
        "the CANDIDATE item is also still reachable — the failed swap rolled back "
        + "completely instead of consuming it");
    Log($"EQUIP-SWAP full bag: refs {totalBefore} -> {totalAfter} (conserved); "
        + $"bag {bagBefore} -> {player.m_ItemList.Count}; slot occupied="
        + $"{player.m_UseItems[Slot] != null}; displaced item reachable=True "
        + "(sub_6B7E9C @0x6B8041/@0x6B804C)");

    // The normal (non-full) swap must still work and still conserve.
    var roomy = NewPlayer("swap2-owner", "swap2-user");
    var oldGear2 = MakeItem("铁剑");
    var newGear2 = MakeItem("铁剑");
    roomy.m_UseItems[Slot] = oldGear2;
    roomy.m_ItemList.Add(newGear2);
    var before2 = CountRefs(roomy);
    var newGear2Id = roomy.EnsureClientItemId(newGear2);
    miTakeOn.Invoke(roomy, new object[] { Slot, newGear2Id, "铁剑" });
    Assert(CountRefs(roomy) == before2, "ordinary equip swap conserves references");
    Assert(roomy.m_UseItems[Slot] == newGear2, "ordinary swap equips the new item");
    Assert(roomy.m_ItemList.Contains(oldGear2), "ordinary swap returns the old item to the bag");
    Assert(!roomy.m_ItemList.Contains(newGear2),
        "the newly equipped item left the bag (slot-write + DelBagItem, sub_73D140 @0x6B7FA2)");
    Log($"EQUIP-SWAP roomy bag: refs conserved ({before2}); new item equipped, old item in bag, "
        + "no reference in two containers");
}

// ===================== C. UNEQUIP WITH NO BAG SPACE ==============================================

void VerifyUnequipNoSpaceMutatesNothing()
{
    var player = NewPlayer("takeoff-owner", "takeoff-user");
    const byte Slot = 1;

    var equipped = MakeItem("铁剑");
    player.m_UseItems[Slot] = equipped;
    while (player.m_ItemList.Count < Grobal2.MAXBAGITEM)
        player.m_ItemList.Add(MakeItem("铁剑"));
    Assert(player.m_ItemList.Count == Grobal2.MAXBAGITEM, "bag is exactly full");

    var totalBefore = CountRefs(player);
    var bagBefore = player.m_ItemList.Count;

    var equippedId = player.EnsureClientItemId(equipped);
    miTakeOff.Invoke(player, new object[] { Slot, equippedId, "铁剑" });

    // 战神 sub_6B8188 @0x6B81F0 rejects with -3 BEFORE anything is mutated.
    Assert(player.m_UseItems[Slot] == equipped,
        "unequip with a FULL bag leaves the item EQUIPPED (native rejects at @0x6B81F0 "
        + "before sub_75EF30 ever clears the slot)");
    Assert(player.m_ItemList.Count == bagBefore, "the bag is unchanged");
    Assert(!player.m_ItemList.Contains(equipped), "the item did not also enter the bag");
    Assert(CountRefs(player) == totalBefore,
        "unequip with no space mutates NOTHING — the item is neither duplicated nor "
        + "destroyed (C# used to call Dispose() on the player's own item)");
    Log($"UNEQUIP no space: refs {totalBefore} -> {CountRefs(player)} (unchanged); slot still "
        + "holds the item; bag untouched; no Dispose (sub_6B8188 @0x6B81F0 => -3)");

    // With space the unequip must succeed and still conserve.
    var roomy = NewPlayer("takeoff2-owner", "takeoff2-user");
    var equipped2 = MakeItem("铁剑");
    roomy.m_UseItems[Slot] = equipped2;
    var before2 = CountRefs(roomy);
    var equipped2Id = roomy.EnsureClientItemId(equipped2);
    miTakeOff.Invoke(roomy, new object[] { Slot, equipped2Id, "铁剑" });
    Assert(roomy.m_UseItems[Slot] == null, "unequip with space clears the slot");
    Assert(roomy.m_ItemList.Contains(equipped2), "unequip with space puts the item in the bag");
    Assert(CountRefs(roomy) == before2, "successful unequip conserves references");
    Log($"UNEQUIP with space: slot cleared, item in bag, refs conserved ({before2})");
}

// ===================== B. HERO-BAG TRANSFER WHILE DEALING ========================================

void VerifyHeroBagDealingRejected()
{
    var player = NewPlayer("hero-owner", "hero-user");
    var hero = new HeroObject { m_sCharName = "hero", m_boGhost = false, m_boDeath = false };
    hero.m_Abil.Level = 40;
    player.m_HeroObject = hero;

    var staged = MakeItem("铁剑");
    player.m_ItemList.Add(staged);
    var clientId = player.EnsureClientItemId(staged);

    // Stage the item in a deal exactly as the exploit would: the deal list holds the same
    // object reference the bag holds, and m_boDealing is set.
    player.m_DealItemList.Add(staged);
    player.m_boDealing = true;

    var bagBefore = player.m_ItemList.Count;
    var heroBagBefore = hero.m_ItemList.Count;

    miHeroToHeroBag.Invoke(player, new object[] { NewMsg(clientId) });

    // 战神 sub_6D09D0 @0x6D09ED `cmp byte [ebx+0x461],0; jne 0x6D0ABB` => -1.
    Assert(hero.m_ItemList.Count == heroBagBefore,
        "hero-bag transfer WHILE DEALING is rejected — the hero bag is untouched "
        + "(sub_6D09D0 @0x6D09ED m_boDealing gate => -1)");
    Assert(player.m_ItemList.Count == bagBefore, "the master bag is untouched");
    Assert(!hero.m_ItemList.Contains(staged),
        "the staged trade item did NOT reach the hero bag (the two-container dupe: deal "
        + "list + hero bag both holding one reference while the deal completes)");
    Assert(player.m_ItemList.Contains(staged), "the staged item is still in the master bag");
    Log($"HERO-BAG while dealing: master bag {bagBefore} (unchanged), hero bag {heroBagBefore} "
        + "(unchanged), staged deal item never entered the hero bag (sub_6D09D0 @0x6D09ED)");

    // The reverse direction carries the identical gate (sub_6D0B00 @0x6D0B1D).
    var heroItem = MakeItem("铁剑");
    hero.m_ItemList.Add(heroItem);
    var heroClientId = player.EnsureClientItemId(heroItem);
    var masterBefore = player.m_ItemList.Count;
    var heroBefore = hero.m_ItemList.Count;
    miHeroToHumBag.Invoke(player, new object[] { NewMsg(heroClientId) });
    Assert(player.m_ItemList.Count == masterBefore && hero.m_ItemList.Count == heroBefore,
        "hero -> master transfer WHILE DEALING is likewise rejected (sub_6D0B00 @0x6D0B1D)");
    Assert(hero.m_ItemList.Contains(heroItem), "the hero-bag item stayed in the hero bag");
    Log("HERO-BAG reverse direction while dealing: both bags unchanged (sub_6D0B00 @0x6D0B1D)");

    // With dealing cleared the transfer must work AND conserve.
    player.m_boDealing = false;
    player.m_DealItemList.Clear();
    var totalBefore = player.m_ItemList.Count + hero.m_ItemList.Count;
    miHeroToHeroBag.Invoke(player, new object[] { NewMsg(clientId) });
    var totalAfter = player.m_ItemList.Count + hero.m_ItemList.Count;
    Assert(totalAfter == totalBefore,
        "an ALLOWED hero-bag transfer conserves the combined item count");
    Assert(hero.m_ItemList.Contains(staged), "the item reached the hero bag");
    Assert(!player.m_ItemList.Contains(staged),
        "and left the master bag — never in both (add-then-remove, sub_6D09D0 @0x6D0A5E/@0x6D0A70)");
    Log($"HERO-BAG not dealing: combined count conserved ({totalBefore}); item moved "
        + "exactly once, never in two containers");
}

// =============== D. A FAILED MAIL ATTACHMENT IS LOST, NOT DUPLICATED =============================

void VerifyFailedMailAttachmentIsLostNotDuplicated()
{
    const long RecipientId = 0x7001;

    // Reset the process-static mail cache so this harness is order-independent.
    mailCacheType.GetMethod("ResetForTests",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        ?.Invoke(null, new object[] { 0 });

    var player = NewPlayer("mail-loss-owner", "mail-loss-user");
    fiMailRecipientId.SetValue(player, RecipientId);

    // OVER-fill the bag so the per-item AddItemToBag inside the delivery loop fails even
    // though the pre-flight gate at Mail.cs:137 (attachCount > 48 - Count) passed when the
    // mail was queued. This is the 48-slot race sub_70B458 tolerates at @0x70B4F6.
    while (player.m_ItemList.Count <= Grobal2.MAXBAGITEM)
        player.m_ItemList.Add(MakeItem("铁剑"));

    var record = Activator.CreateInstance(mailRecordType, nonPublic: true);
    void SetRec(string name, object value) => mailRecordType
        .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)
        .SetValue(record, value);
    SetRec("Id", 7001);
    SetRec("SenderId", -1L);
    SetRec("Sender", "系统");
    SetRec("Title", "附件邮件");
    SetRec("Context", "领取附件");
    SetRec("MailType", (byte)1);
    SetRec("MailStatus", (byte)1);
    SetRec("AttachStatus", (byte)1);
    SetRec("MoneyType", 0);
    SetRec("MoneyCount", 0);
    SetRec("CreateDate", DateTime.Now);

    var attachment = MakeItem("铁剑");
    var register = mailCacheType.GetMethod("Register",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingMethodException("NativeMailCacheService.Register");
    var entry = register.Invoke(null, new object[]
    {
        RecipientId, player.m_sCharName, record,
        new List<TUserItem> { attachment }, DateTime.Now
    });
    Assert(entry != null, "the mail with one item attachment was registered in the cache");

    var bagBefore = player.m_ItemList.Count;

    // First: the once-only PRE-FLIGHT gate (Mail.cs:137 == native sub_70B670 @0x70B69D
    // `cmp edi,eax; jg` => -1) still rejects up front. This half was already faithful.
    var preflight = (int)miFetchAttach.Invoke(player, new[] { entry });
    Assert(preflight == -1,
        "the pre-flight bag-space gate rejects with -1 (attachCount > 48 - Count)");
    Assert((byte)mailRecordType
        .GetProperty("AttachStatus", BindingFlags.Instance | BindingFlags.NonPublic)
        .GetValue(record) != 2, "a pre-flight rejection does not mark the mail claimed");

    // Then: the RACE the fix is actually about — the pre-flight passed when the mail was
    // queued but the bag filled before the loop ran, so the PER-ITEM add fails inside the
    // delivery core. Drive the core directly, which is what native sub_70B458 is.
    var result = (int)miDeliverAttach.Invoke(player, new[] { entry });

    var attachStatus = (byte)mailRecordType
        .GetProperty("AttachStatus", BindingFlags.Instance | BindingFlags.NonPublic)
        .GetValue(record);

    Assert(!player.m_ItemList.Contains(attachment),
        "the attachment genuinely failed to land (the bag was over-full)");
    Assert(player.m_ItemList.Count == bagBefore, "no attachment entered the bag");
    Assert(attachStatus == 2,
        "a mail whose attachment failed to land is STILL marked claimed "
        + "(AttachStatus " + attachStatus + " must be 2). sub_70B458 reaches the write at "
        + "0x70B5E3 `cmp byte[mail+0x4D],2` / 0x70B5E9 `mov dl,2; call sub_70CB24` "
        + "unconditionally after the loop, because 0x70B4F8 `je 0x70B5D9` sends a failed "
        + "add to the loop INCREMENT, not to an abort. Leaving it at 1 would let the whole "
        + "attachment list — including the copies that already landed — be granted again.");
    Assert(result == 1,
        "and the claim still reports 1 (0x70B5F2 mov esi,1), as native does");
    Log($"MAIL failed attachment: claim result={result}; bag {bagBefore} -> "
        + $"{player.m_ItemList.Count} (unchanged); AttachStatus 1 -> {attachStatus} "
        + "(2 = mail is closed, attachment lost not duplicated; "
        + "sub_70B458 @0x70B4F6 / @0x70B5E3)");

    // CONSERVATION: the mail is now closed, so freeing bag space and asking again must be
    // refused by the -2 arm (sub_70B664 @0x70B68D `cmp byte[mail+0x4D],2` /
    // @0x70B693 `mov esi,0xFFFFFFFE`). Without that the attachment list is grantable twice.
    player.m_ItemList.Clear();
    var reclaim = (int)miFetchAttach.Invoke(player, new[] { entry });
    Assert(reclaim == -2,
        "re-claiming a closed mail is refused with -2, so the attachment list can never be "
        + "granted twice (this is the duplication the removed deliveredAll guard opened)");
    Assert(player.m_ItemList.Count == 0,
        "and the refused re-claim granted nothing into the now-empty bag");
    Log($"MAIL re-claim after partial delivery: result={reclaim}; bag stays "
        + $"{player.m_ItemList.Count} (sub_70B664 @0x70B693 mov esi,-2)");

    // The happy path must still mark claimed.
    var roomy = NewPlayer("mail-ok-owner", "mail-ok-user");
    fiMailRecipientId.SetValue(roomy, RecipientId + 1);
    var record2 = Activator.CreateInstance(mailRecordType, nonPublic: true);
    void SetRec2(string name, object value) => mailRecordType
        .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)
        .SetValue(record2, value);
    SetRec2("Id", 7002);
    SetRec2("SenderId", -1L);
    SetRec2("Sender", "系统");
    SetRec2("Title", "附件邮件");
    SetRec2("Context", "领取附件");
    SetRec2("MailType", (byte)1);
    SetRec2("MailStatus", (byte)1);
    SetRec2("AttachStatus", (byte)1);
    SetRec2("MoneyType", 0);
    SetRec2("MoneyCount", 0);
    SetRec2("CreateDate", DateTime.Now);
    var attachment2 = MakeItem("铁剑");
    var entry2 = register.Invoke(null, new object[]
    {
        RecipientId + 1, roomy.m_sCharName, record2,
        new List<TUserItem> { attachment2 }, DateTime.Now
    });
    var okResult = (int)miDeliverAttach.Invoke(roomy, new[] { entry2 });
    var attachStatus2 = (byte)mailRecordType
        .GetProperty("AttachStatus", BindingFlags.Instance | BindingFlags.NonPublic)
        .GetValue(record2);
    Assert(okResult == 1, "a claim that fully succeeds still returns 1");
    Assert(roomy.m_ItemList.Count == 1, "the attachment landed in the empty bag");
    Assert(attachStatus2 == 2,
        "and a fully-delivered mail IS marked claimed (sub_70B458 @0x70B5E9 sub_70CB24(dl=2))");
    Log($"MAIL successful claim: result={okResult}; bag 0 -> {roomy.m_ItemList.Count}; "
        + $"AttachStatus 1 -> {attachStatus2}");
}

// ===================== E. DROP OF AN UNVERIFIED / GIFT ITEM ======================================

void VerifyUnverifiedDropIsDestroyed()
{
    // 战神 sub_73CC98 @0x73CD37 `mov cl,4; call sub_617A38` — sub_617A38 @0x617A3E first
    // tests the feature switch `cmp byte [mgr+8],0; je -> allow`, so the destroy branch is
    // only reachable when authentication is enabled.
    M2Share.g_Config.boAuthOpen = true;

    var player = NewPlayerOnMap("drop-owner", "drop-user");
    var item = MakeItem("铁剑");
    player.m_ItemList.Add(item);
    var clientId = player.EnsureClientItemId(item);
    // No authentication status bits set => CheckNativeAuthentication(1|2, 4) is false.

    var bagBefore = player.m_ItemList.Count;
    var groundBefore = CountGroundItems(player);

    var dropped = (bool)miDropItem.Invoke(player, new object[] { "铁剑", clientId });

    Assert(dropped, "the drop handler consumed the request");
    Assert(player.m_ItemList.Count == bagBefore - 1, "the item left the bag (sub_424B30 @0x73CD73)");
    Assert(!player.m_ItemList.Contains(item), "the item is gone from the bag");
    Assert(CountGroundItems(player) == groundBefore,
        "an UNVERIFIED player's dropped item is DESTROYED, not scattered — nothing was "
        + "added to the map (native sub_73CC98 @0x73CDEB `call sub_404690` = TObject.Free, "
        + "and it NEVER reaches DropItemDown). Scattering it is the mule-drop / alt-picks-up "
        + "laundering route.");
    Log($"DROP unverified: bag {bagBefore} -> {player.m_ItemList.Count}; ground {groundBefore} -> "
        + $"{CountGroundItems(player)} (UNCHANGED = destroyed, not scattered); "
        + "notice='" + NativeItemDropDestroyNotices.DropUnverified + "' (0x73CE74)");

    // An AUTHENTICATED player dropping a non-gift item must still reach the ground
    // (0x73CD4B `je 0x73CDFD` = the normal DropItemDown path).
    var authed = NewPlayerOnMap("authed-owner", "authed-user");
    authed.SetNativeAuthenticationStatus(0x1F, 0x1F, 0x1F);
    var normal = MakeItem("铁剑");
    authed.m_ItemList.Add(normal);
    var normalId = authed.EnsureClientItemId(normal);
    var groundBefore2 = CountGroundItems(authed);
    miDropItem.Invoke(authed, new object[] { "铁剑", normalId });
    Assert(!authed.m_ItemList.Contains(normal), "the authenticated drop left the bag");
    Assert(CountGroundItems(authed) == groundBefore2 + 1,
        "an AUTHENTICATED player's ordinary item DOES reach the ground (0x73CD4B je 0x73CDFD "
        + "-> sub_7688A0 DropItemDown) — the destroy branch must not over-fire");
    Log($"DROP authenticated + non-gift: ground {groundBefore2} -> {CountGroundItems(authed)} "
        + "(+1, normal scatter preserved)");

    M2Share.g_Config.boAuthOpen = false;
}

void VerifyGiftDropIsDestroyed()
{
    M2Share.g_Config.boAuthOpen = true;

    // 战神 sub_73CC98 @0x73CD44 `cmp byte [ebx+0xD8],0; je 0x73CDFD` — an AUTHENTICATED
    // player dropping a GIFT item still hits the destroy branch.
    var player = NewPlayerOnMap("gift-owner", "gift-user");
    player.SetNativeAuthenticationStatus(0x1F, 0x1F, 0x1F);
    var gift = MakeItem("铁剑");
    gift.NativeGiftItem = 1;                          // item+0xD8 != 0
    player.m_ItemList.Add(gift);
    var giftId = player.EnsureClientItemId(gift);

    var bagBefore = player.m_ItemList.Count;
    var groundBefore = CountGroundItems(player);

    miDropItem.Invoke(player, new object[] { "铁剑", giftId });

    Assert(!player.m_ItemList.Contains(gift), "the gift item left the bag");
    Assert(player.m_ItemList.Count == bagBefore - 1, "exactly one item left the bag");
    Assert(CountGroundItems(player) == groundBefore,
        "a GIFT (赠品) item is DESTROYED on drop even for an AUTHENTICATED player — it must "
        + "not reach the ground (native sub_73CC98 @0x73CD44 item+0xD8 gate then "
        + "@0x73CDEB Free). Scattering it lets a gift be laundered onto another character.");
    Log($"DROP gift (item+0xD8): bag {bagBefore} -> {player.m_ItemList.Count}; ground "
        + $"{groundBefore} -> {CountGroundItems(player)} (UNCHANGED = destroyed); "
        + "notice='" + NativeItemDropDestroyNotices.DropGift + "' (0x73CE94)");

    M2Share.g_Config.boAuthOpen = false;
}

void VerifyMode5DropItemDownGate()
{
    var player = NewPlayerOnMap("mode5-drop-owner", "mode5-drop-user");
    var protectedStd = new GoodItem
    {
        Name = "mode5-protected-drop", StdMode = 5, Shape = 1,
        Weight = 1, DuraMax = 5000, NativeReserved02 = 0x0020
    };
    var protectedItem = MakeRawItem(protectedStd);
    var groundBefore = CountGroundItems(player);
    Assert(!player.DropItemDown(protectedItem, 1, false, null, player),
        "sub_7688A0 non-death mode 5 must reject std[+2]&0x20");
    Assert(CountGroundItems(player) == groundBefore,
        "mode-5 protected item reached the ground");

    var mode4OnlyStd = new GoodItem
    {
        Name = "mode4-only-drop", StdMode = 5, Shape = 1,
        Weight = 1, DuraMax = 5000, NativeReserved02 = 0x0400
    };
    var mode4OnlyItem = MakeRawItem(mode4OnlyStd);
    Assert(player.DropItemDown(mode4OnlyItem, 1, false, null, player),
        "sub_7688A0 mode 5 must not reuse mode 4's 0x0400 gate");
    Assert(CountGroundItems(player) == groundBefore + 1,
        "mode-4-only item did not reach the ground through mode 5");

    var deathBypassItem = MakeRawItem(protectedStd);
    Assert(player.DropItemDown(deathBypassItem, 1, true, null, player),
        "sub_7688A0 death/scatter flag must bypass sub_78389C");
    Assert(CountGroundItems(player) == groundBefore + 2,
        "death/scatter bypass did not place the protected item");
}

void VerifyDeathBagMode5Protection()
{
    var oldAuth = M2Share.g_Config.boAuthOpen;
    var oldDropAll = M2Share.g_Config.boDieRedScatterBagAll;
    try
    {
        M2Share.g_Config.boAuthOpen = true;
        M2Share.g_Config.boDieRedScatterBagAll = true;

        var player = NewPlayerOnMap("mode5-death-owner", "mode5-death-user");
        player.m_nPkPoint = 1000;
        var protectedStd = new GoodItem
        {
            Name = "mode5-protected-death-bag", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000, NativeReserved02 = 0x0020
        };
        var protectedItem = MakeRawItem(protectedStd);
        player.m_ItemList.Add(protectedItem);
        var groundBefore = CountGroundItems(player);

        miScatterBagItems.Invoke(player, new object[] { null });

        Assert(player.m_ItemList.Contains(protectedItem),
            "0x74017B nonzero mode-5 result must keep the death-bag item");
        Assert(CountGroundItems(player) == groundBefore,
            "protected death-bag item reached the ground");

        var destroyPlayer = NewPlayerOnMap(
            "mode5-death-control", "mode5-death-control-user");
        destroyPlayer.m_nPkPoint = 1000;
        var ordinaryStd = new GoodItem
        {
            Name = "mode5-unprotected-death-bag", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000
        };
        var ordinaryItem = MakeRawItem(ordinaryStd);
        destroyPlayer.m_ItemList.Add(ordinaryItem);
        var controlGroundBefore = CountGroundItems(destroyPlayer);

        miScatterBagItems.Invoke(destroyPlayer, new object[] { null });

        Assert(!destroyPlayer.m_ItemList.Contains(ordinaryItem),
            "mode-5 zero result must continue through the unverified destroy arm");
        Assert(CountGroundItems(destroyPlayer) == controlGroundBefore,
            "unverified control item was scattered instead of destroyed");
    }
    finally
    {
        M2Share.g_Config.boAuthOpen = oldAuth;
        M2Share.g_Config.boDieRedScatterBagAll = oldDropAll;
    }
}

void VerifyDeathEquipMode5Protection()
{
    var oldAuth = M2Share.g_Config.boAuthOpen;
    var oldRandom = M2Share.RandomNumber;
    try
    {
        M2Share.g_Config.boAuthOpen = true;
        M2Share.RandomNumber = new FixedRandomNumber(0);

        var protectedPlayer = NewPlayerOnMap(
            "mode5-equip-protected", "mode5-equip-protected-user");
        var protectedStd = new GoodItem
        {
            Name = "mode5-protected-death-equip", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000,
            NativeReserved02 = 0x0010 | 0x0020
        };
        var protectedItem = MakeRawItem(protectedStd);
        protectedItem.ClientItemID = 0;
        protectedPlayer.m_UseItems[0] = protectedItem;
        var protectedGroundBefore = CountGroundItems(protectedPlayer);

        miDropUseItems.Invoke(protectedPlayer, new object[] { null });

        Assert(ReferenceEquals(protectedPlayer.m_UseItems[0], protectedItem)
            && protectedItem.wIndex != 0,
            "0x73FDFE nonzero mode-5 result must keep the equipped item");
        Assert(protectedItem.ClientItemID == 0,
            "mode-5 protected equipment reached the destroy bookkeeping arm");
        Assert(CountGroundItems(protectedPlayer) == protectedGroundBefore,
            "mode-5 protected equipment reached the ground");

        var destroyPlayer = NewPlayerOnMap(
            "mode5-equip-control", "mode5-equip-control-user");
        var destroyStd = new GoodItem
        {
            Name = "mode5-unprotected-death-equip", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000, NativeReserved02 = 0x0010
        };
        var destroyItem = MakeRawItem(destroyStd);
        destroyItem.ClientItemID = 0;
        destroyPlayer.m_UseItems[0] = destroyItem;
        var destroyGroundBefore = CountGroundItems(destroyPlayer);

        miDropUseItems.Invoke(destroyPlayer, new object[] { null });

        Assert(ReferenceEquals(destroyPlayer.m_UseItems[0], destroyItem)
            && destroyItem.wIndex != 0,
            "native death-equip destroy arm must preserve its dangling slot semantics");
        Assert(destroyItem.ClientItemID > 0,
            "mode-5 zero result did not reach death-equip destroy bookkeeping");
        Assert(CountGroundItems(destroyPlayer) == destroyGroundBefore,
            "unverified death equipment was scattered instead of destroyed");

        var authenticated = NewPlayerOnMap(
            "mode5-equip-auth", "mode5-equip-auth-user");
        authenticated.SetNativeAuthenticationStatus(0x1F, 0x1F, 0x1F);
        var noGroundStd = new GoodItem
        {
            Name = "mode5-auth-no-ground-equip", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000, NativeReserved02 = 0x0010
        };
        var noGroundItem = MakeRawItem(noGroundStd);
        noGroundItem.ClientItemID = 0;
        authenticated.m_UseItems[0] = noGroundItem;
        var authGroundBefore = CountGroundItems(authenticated);

        miDropUseItems.Invoke(authenticated, new object[] { null });

        Assert(noGroundItem.wIndex != 0 && noGroundItem.ClientItemID == 0,
            "0x73FECE normal arm must reject std[+2]&0x10");
        Assert(CountGroundItems(authenticated) == authGroundBefore,
            "std[+2]&0x10 authenticated equipment reached the ground");

        M2Share.g_Config.boAuthOpen = false;
        M2Share.RandomNumber = new FixedRandomNumber(1);
        var classFcPlayer = NewPlayerOnMap(
            "mode5-equip-classfc", "mode5-equip-classfc-user");
        var classFcStd = new GoodItem
        {
            Name = "mode5-classfc-death-equip", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000
        };
        var classFcItem = MakeRawItem(classFcStd);
        classFcItem.NativeClassFc = 1;
        classFcPlayer.m_UseItems[0] = classFcItem;
        var classFcGroundBefore = CountGroundItems(classFcPlayer);

        miDropUseItems.Invoke(classFcPlayer, new object[] { null });

        Assert(classFcPlayer.m_UseItems[0] == null
            && classFcItem.wIndex > 0
            && CountGroundItems(classFcPlayer) == classFcGroundBefore + 1,
            "0x73FDA2 item+0xFC must bypass a non-zero random result "
            + "and 0x73FF11 must detach without clearing the ground item's wIndex");

        var randomSkipPlayer = NewPlayerOnMap(
            "mode5-equip-random-skip", "mode5-equip-random-skip-user");
        var randomSkipItem = MakeRawItem(classFcStd);
        randomSkipPlayer.m_UseItems[0] = randomSkipItem;
        var randomSkipGroundBefore = CountGroundItems(randomSkipPlayer);

        miDropUseItems.Invoke(randomSkipPlayer, new object[] { null });

        Assert(randomSkipItem.wIndex != 0
            && CountGroundItems(randomSkipPlayer) == randomSkipGroundBefore,
            "non-zero random result must skip ordinary death equipment");
    }
    finally
    {
        M2Share.RandomNumber = oldRandom;
        M2Share.g_Config.boAuthOpen = oldAuth;
    }
}

void VerifyDeathEquipReserved08Ordering()
{
    var oldAuth = M2Share.g_Config.boAuthOpen;
    var oldRandom = M2Share.RandomNumber;
    try
    {
        M2Share.g_Config.boAuthOpen = false;

        var reservedPlayer = NewPlayerOnMap(
            "reserved08-equip", "reserved08-equip-user");
        var reservedStd = new GoodItem
        {
            Name = "native-reserved08-death-equip", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000, NativeReserved02 = 0x0008
        };
        var reservedItem = MakeRawItem(reservedStd);
        reservedPlayer.m_UseItems[0] = reservedItem;
        var noDraw = new FixedRandomNumber(1);
        M2Share.RandomNumber = noDraw;
        var reservedGroundBefore = CountGroundItems(reservedPlayer);

        miDropUseItems.Invoke(reservedPlayer, new object[] { null });

        Assert(reservedPlayer.m_UseItems[0] == null,
            "0x73FD49 std[+2]&8 item was not removed from its current slot");
        Assert(noDraw.Calls == 0,
            "0x73FD49 std[+2]&8 branch must jump over Random(K)");
        Assert(CountGroundItems(reservedPlayer) == reservedGroundBefore,
            "std[+2]&8 death equipment reached the ground");

        var legacyPlayer = NewPlayerOnMap(
            "legacy-reserved-equip", "legacy-reserved-equip-user");
        var legacyStd = new GoodItem
        {
            Name = "legacy-reserved-field-equip", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000, Reserved = 0x0008
        };
        var legacyItem = MakeRawItem(legacyStd);
        legacyPlayer.m_UseItems[0] = legacyItem;
        var legacyDraw = new FixedRandomNumber(1);
        M2Share.RandomNumber = legacyDraw;

        miDropUseItems.Invoke(legacyPlayer, new object[] { null });

        Assert(ReferenceEquals(legacyPlayer.m_UseItems[0], legacyItem),
            "GoodItem.Reserved must not substitute for native std[+2]&8");
        Assert(legacyDraw.Calls == 1,
            "ordinary equipment did not consume exactly one Random(K) draw");

        var noCapPlayer = NewPlayerOnMap(
            "reserved08-no-cap", "reserved08-no-cap-user");
        var noCapDraw = new FixedRandomNumber(1);
        M2Share.RandomNumber = noCapDraw;
        var noCapGroundBefore = CountGroundItems(noCapPlayer);
        for (var i = 0; i < 4; i++)
        {
            noCapPlayer.m_UseItems[i] = MakeRawItem(reservedStd);
        }

        miDropUseItems.Invoke(noCapPlayer, new object[] { null });

        Assert(noCapDraw.Calls == 0,
            "consecutive std[+2]&8 items must all bypass Random(K)");
        Assert(noCapPlayer.m_UseItems.Take(4).All(item => item == null),
            "0x73FD91 must bypass the normal cap check for every std[+2]&8 slot");
        Assert(CountGroundItems(noCapPlayer) == noCapGroundBefore,
            "consecutive std[+2]&8 death equipment reached the ground");

        var capPlayer = NewPlayerOnMap(
            "reserved08-cap-order", "reserved08-cap-order-user");
        var ordinaryStd = new GoodItem
        {
            Name = "reserved08-cap-ordinary", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000
        };
        var capItems = new TUserItem[4];
        for (var i = 0; i < 3; i++)
        {
            capItems[i] = MakeRawItem(ordinaryStd);
            capPlayer.m_UseItems[i] = capItems[i];
        }
        capItems[3] = MakeRawItem(reservedStd);
        capPlayer.m_UseItems[3] = capItems[3];
        var capDraw = new FixedRandomNumber(0);
        M2Share.RandomNumber = capDraw;
        var capGroundBefore = CountGroundItems(capPlayer);

        miDropUseItems.Invoke(capPlayer, new object[] { null });

        Assert(capDraw.Calls == 3,
            "native cap path must draw only slots 0..2 before exiting");
        Assert(capPlayer.m_UseItems[0] == null
            && capPlayer.m_UseItems[1] == null
            && capPlayer.m_UseItems[2] == null,
            "three successful normal drops were not detached from equipment slots");
        Assert(ReferenceEquals(capPlayer.m_UseItems[3], capItems[3]),
            "a pre-scan processed slot 3 before the native cap exit at slot 2");
        Assert(capItems[0].wIndex > 0 && capItems[1].wIndex > 0
            && capItems[2].wIndex > 0,
            "normal death drop cleared the wIndex of an item now owned by the ground");
        Assert(CountGroundItems(capPlayer) == capGroundBefore + 3,
            "native three-item cap did not produce exactly three ground items");
    }
    finally
    {
        M2Share.RandomNumber = oldRandom;
        M2Share.g_Config.boAuthOpen = oldAuth;
    }
}

void VerifyHeroDeathEquipWorker()
{
    var oldRandom = M2Share.RandomNumber;
    var oldAuth = M2Share.g_Config.boAuthOpen;
    try
    {
        // Heroes jump over the player auth/gift arm even when authentication is enabled.
        M2Share.g_Config.boAuthOpen = true;
        var ordinaryStd = new GoodItem
        {
            Name = "hero-death-equip-order", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000
        };
        var formulaOwner = NewPlayerOnMap("hero-equip-formula-owner",
            "hero-equip-formula-user");
        var formulaHero = NewHeroOnMap(formulaOwner, "hero-equip-formula");
        var formulaKiller = NewPlayerOnMap("hero-equip-formula-killer",
            "hero-equip-formula-killer-user");
        formulaHero.m_nNativeDropRareBase = 7;
        formulaKiller.m_btNativeDropRareKillerBonus = 10;
        Assert(formulaHero.NativeHeroDeathEquipDropDenominator(false,
                formulaKiller, true, 25) == 22,
            "Eye non-red K must remain self base + patched K - human killer bonus");
        Assert(formulaHero.NativeHeroDeathEquipDropDenominator(true,
                formulaKiller, true, 21) == 11,
            "Eye red K must remain patched K - human killer bonus without self base");
        formulaHero.m_nNativeDropRareBase = 100;
        Assert(formulaHero.NativeHeroDeathEquipDropDenominator(false,
                null, true, -128) == 0,
            "Eye signed imm8 K must be floored only after adding the native self base");
        Assert(formulaHero.NativeHeroDeathEquipDropDenominator(false,
                formulaKiller, false, -1) == 180,
            "disabled Eye patch must retain the stock base+90-killer denominator");

        var owner = NewPlayerOnMap("hero-equip-owner", "hero-equip-user");
        var hero = NewHeroOnMap(owner, "hero-equip");
        var slot0 = MakeRawItem(ordinaryStd);
        var slot7 = MakeRawItem(ordinaryStd);
        var slot15 = MakeRawItem(ordinaryStd);
        hero.m_UseItems[0] = slot0;
        hero.m_UseItems[7] = slot7;
        hero.m_UseItems[15] = slot15;
        var orderedDraws = new SequenceRandomNumber(1, 0, 1);
        M2Share.RandomNumber = orderedDraws;
        var groundBefore = CountGroundItems(owner);

        hero.NativeHeroDropUseItems(null, owner);

        Assert(orderedDraws.Bounds.SequenceEqual(new[] { 90, 90, 90 }),
            "hero death equipment must consume Random(K) only for nonempty slots, in 0..15 order");
        Assert(ReferenceEquals(hero.m_UseItems[0], slot0)
            && hero.m_UseItems[7] == null
            && ReferenceEquals(hero.m_UseItems[15], slot15),
            "scripted draws [1,0,1] did not select only hero slot 7");
        Assert(slot7.wIndex > 0 && CountGroundItems(owner) == groundBefore + 1,
            "hero slot 7 was not detached onto the ground with wIndex intact");

        M2Share.RandomNumber = oldRandom;
        var reservedStd = new GoodItem
        {
            Name = "hero-death-equip-reserved08", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000, NativeReserved02 = 0x0008
        };
        var capOwner = NewPlayerOnMap("hero-equip-cap-owner", "hero-equip-cap-user");
        var capHero = NewHeroOnMap(capOwner, "hero-equip-cap");
        for (var i = 0; i < 4; i++)
            capHero.m_UseItems[i] = MakeRawItem(reservedStd);
        capHero.m_UseItems[4] = MakeRawItem(ordinaryStd);
        var afterCap = MakeRawItem(ordinaryStd);
        capHero.m_UseItems[5] = afterCap;
        var capDraws = new SequenceRandomNumber(0);
        M2Share.RandomNumber = capDraws;
        var capGroundBefore = CountGroundItems(capOwner);

        capHero.NativeHeroDropUseItems(null, capOwner);

        Assert(capHero.m_UseItems.Take(5).All(item => item == null)
            && ReferenceEquals(capHero.m_UseItems[5], afterCap),
            "Reserved08 must bypass RNG/cap, but its count must make the next successful ground drop exit");
        Assert(capDraws.Bounds.SequenceEqual(new[] { 90 })
            && CountGroundItems(capOwner) == capGroundBefore + 1,
            "Reserved08 ordering consumed RNG or failed to participate in the later cap");

        M2Share.RandomNumber = oldRandom;
        var ghostOwner = NewRecordingPlayerOnMap("hero-equip-ghost-owner",
            "hero-equip-ghost-user");
        ghostOwner.m_boGhost = true;
        var ghostHero = NewHeroOnMap(ghostOwner, "hero-equip-ghost");
        var ghostReserved = MakeRawItem(reservedStd);
        ghostHero.m_UseItems[2] = ghostReserved;
        M2Share.RandomNumber = new SequenceRandomNumber();

        ghostHero.NativeHeroDropUseItems(null, ghostOwner);

        var ghostDelete = ghostHero.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST);
        Assert(ghostReserved.ClientItemID == 0
            && ghostDelete.Payload is byte[] { Length: 4 } ghostDeleteBody
            && BinaryPrimitives.ReadInt32LittleEndian(ghostDeleteBody) == 0,
            "Reserved08 must queue the raw item+0x18 dword without allocating an ID");
        Assert(ghostOwner.BinaryPackets.Count == 0
            && ghostOwner.TextPackets.Count == 0,
            "Reserved08 SM906 must be suppressed when the bound owner is ghost");

        M2Share.RandomNumber = oldRandom;
        var deadOwner = NewRecordingPlayerOnMap("hero-equip-dead-owner",
            "hero-equip-dead-user");
        deadOwner.m_boDeath = true;
        var deadHero = NewHeroOnMap(deadOwner, "hero-equip-dead");
        var deadReserved = MakeRawItem(reservedStd);
        deadHero.m_UseItems[2] = deadReserved;
        M2Share.RandomNumber = new SequenceRandomNumber();

        deadHero.NativeHeroDropUseItems(null, deadOwner);

        Assert(deadOwner.BinaryPackets.Count == 1
            && deadOwner.TextPackets.Count == 1
            && deadOwner.BinaryPackets[0].Header.Ident == Grobal2.SM_HERO_DELITEM
            && deadOwner.BinaryPackets[0].Header.Recog == 0
            && deadOwner.BinaryPackets[0].Header.Series == 1,
            "Reserved08 SM906 must still use raw ID and reach a dead, non-ghost owner");

        M2Share.RandomNumber = oldRandom;
        var classOwner = NewPlayerOnMap("hero-equip-fc-owner", "hero-equip-fc-user");
        var classHero = NewHeroOnMap(classOwner, "hero-equip-fc");
        var forced = MakeRawItem(ordinaryStd);
        forced.NativeClassFc = 1;
        var ordinary = MakeRawItem(ordinaryStd);
        classHero.m_UseItems[0] = forced;
        classHero.m_UseItems[1] = ordinary;
        var classDraws = new SequenceRandomNumber(1, 1);
        M2Share.RandomNumber = classDraws;

        classHero.NativeHeroDropUseItems(null, classOwner);

        Assert(classDraws.Bounds.SequenceEqual(new[] { 90, 90 }),
            "equipment ClassFc must bypass a nonzero result only after consuming Random(K)");
        Assert(classHero.m_UseItems[0] == null
            && ReferenceEquals(classHero.m_UseItems[1], ordinary),
            "equipment ClassFc did not force only the flagged item through a nonzero draw");
        var classDelete = classHero.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST);
        Assert(forced.ClientItemID == 0
            && classDelete.Payload is byte[] { Length: 4 } classDeleteBody
            && BinaryPrimitives.ReadInt32LittleEndian(classDeleteBody) == 0,
            "normal equipment drop must queue raw item+0x18 without lazy ID allocation");
        Log("HERO DEATH EQUIP: 16-slot forward scan, empty slots consume no RNG, "
            + "Eye K keeps native base/killer terms, Reserved08 bypasses RNG/cap with ghost gate, "
            + "ClassFc consumes then bypasses, raw ClientItemID is preserved");
    }
    finally
    {
        M2Share.RandomNumber = oldRandom;
        M2Share.g_Config.boAuthOpen = oldAuth;
    }
}

void VerifyHeroDeathBagWorker()
{
    var oldRandom = M2Share.RandomNumber;
    var oldAuth = M2Share.g_Config.boAuthOpen;
    var oldThreshold = M2Share.g_Config.nPKPunishPoint;
    try
    {
        M2Share.g_Config.boAuthOpen = true;
        M2Share.g_Config.nPKPunishPoint = 200;
        var ordinaryStd = new GoodItem
        {
            Name = "hero-death-bag-order", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000
        };
        var owner = NewPlayerOnMap("hero-bag-owner", "hero-bag-user");
        var hero = NewHeroOnMap(owner, "hero-bag");
        var itemA = MakeRawItem(ordinaryStd);
        var itemB = MakeRawItem(ordinaryStd);
        var itemC = MakeRawItem(ordinaryStd);
        hero.m_ItemList.Add(itemA);
        hero.m_ItemList.Add(itemB);
        hero.m_ItemList.Add(itemC);
        var reverseDraws = new SequenceRandomNumber(0, 1, 1);
        M2Share.RandomNumber = reverseDraws;
        var groundBefore = CountGroundItems(owner);

        hero.NativeHeroScatterBagItems(owner);

        Assert(reverseDraws.Bounds.SequenceEqual(new[] { 3, 3, 3 }),
            "hero death bag must visit C,B,A and draw Random(3) for each ordinary non-red item");
        Assert(hero.m_ItemList.SequenceEqual(new[] { itemA, itemB })
            && CountGroundItems(owner) == groundBefore + 1,
            "reverse draw sequence did not remove only the original last bag item");

        M2Share.RandomNumber = oldRandom;
        var protectedStd = new GoodItem
        {
            Name = "hero-death-bag-fc", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000,
            NativeReserved02 = 0x0010 | 0x0200 | 0x4000
        };
        var classOwner = NewPlayerOnMap("hero-bag-fc-owner", "hero-bag-fc-user");
        var classHero = NewHeroOnMap(classOwner, "hero-bag-fc");
        var forced = MakeRawItem(protectedStd);
        forced.NativeClassFc = 1;
        forced.btValue[10] = 1;
        classHero.m_ItemList.Add(forced);
        var noDraw = new SequenceRandomNumber();
        M2Share.RandomNumber = noDraw;
        var classGroundBefore = CountGroundItems(classOwner);

        classHero.NativeHeroScatterBagItems(classOwner);

        Assert(noDraw.Bounds.Count == 0 && classHero.m_ItemList.Count == 0
            && CountGroundItems(classOwner) == classGroundBefore + 1,
            "bag ClassFc must bypass Random(3), Reserved02 and bind-word==1 before normal hero landing");
        var classDelete = classHero.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SENDDELITEMLIST);
        Assert(forced.ClientItemID == 0
            && classDelete.Payload is byte[] { Length: 4 } classDeleteBody
            && BinaryPrimitives.ReadInt32LittleEndian(classDeleteBody) == 0,
            "hero bag drop must queue raw item+0x18 without lazy ID allocation");

        M2Share.RandomNumber = oldRandom;
        var belowOwner = NewPlayerOnMap("hero-bag-pk199-owner", "hero-bag-pk199-user");
        var belowHero = NewHeroOnMap(belowOwner, "hero-bag-pk199");
        belowHero.m_nPkPoint = 199;
        var belowItem = MakeRawItem(ordinaryStd);
        belowHero.m_ItemList.Add(belowItem);
        var belowDraw = new SequenceRandomNumber(1);
        M2Share.RandomNumber = belowDraw;
        belowHero.NativeHeroScatterBagItems(belowOwner);
        Assert(belowDraw.Bounds.SequenceEqual(new[] { 3 })
            && ReferenceEquals(belowHero.m_ItemList.Single(), belowItem),
            "PK threshold-1 must remain non-red and obey a nonzero Random(3) result");

        M2Share.RandomNumber = oldRandom;
        var edgeOwner = NewPlayerOnMap("hero-bag-pk200-owner", "hero-bag-pk200-user");
        var edgeHero = NewHeroOnMap(edgeOwner, "hero-bag-pk200");
        edgeHero.m_nPkPoint = 200;
        edgeHero.m_ItemList.Add(MakeRawItem(ordinaryStd));
        var edgeNoDraw = new SequenceRandomNumber();
        M2Share.RandomNumber = edgeNoDraw;
        edgeHero.NativeHeroScatterBagItems(edgeOwner);
        Assert(edgeNoDraw.Bounds.Count == 0 && edgeHero.m_ItemList.Count == 0,
            "PKPoint==threshold must enter the red all-drop arm without Random(3)");

        M2Share.RandomNumber = oldRandom;
        var radiusOwner = NewPlayerOnMap("hero-bag-radius-owner", "hero-bag-radius-user");
        var radiusHero = NewHeroOnMap(radiusOwner, "hero-bag-radius");
        radiusHero.m_nCurrX = 50;
        radiusHero.m_nCurrY = 50;
        var radiusItem = MakeRawItem(ordinaryStd);
        radiusItem.NativeClassFc = 1;
        radiusHero.m_ItemList.Add(radiusItem);
        var radiusNoDraw = new SequenceRandomNumber();
        M2Share.RandomNumber = radiusNoDraw;
        for (var x = 48; x <= 52; x++)
            for (var y = 48; y <= 52; y++)
                radiusHero.m_PEnvir.SetMapXYFlag(x, y, false);
        var radiusGroundBefore = CountGroundItems(radiusOwner);
        try
        {
            radiusHero.NativeHeroScatterBagItems(radiusOwner);
            Assert(ReferenceEquals(radiusHero.m_ItemList.Single(), radiusItem)
                && CountGroundItems(radiusOwner) == radiusGroundBefore,
                "fixed radius 2 must fail and retain the item when only ring 3 is open");
        }
        finally
        {
            for (var x = 48; x <= 52; x++)
                for (var y = 48; y <= 52; y++)
                    radiusHero.m_PEnvir.SetMapXYFlag(x, y, true);
        }
        Log("HERO DEATH BAG: reverse scan, hardcoded Random(3), PK>=threshold edge, "
            + "ClassFc bypasses RNG and all Reserved/bind gates, raw ClientItemID, fixed radius 2");
    }
    finally
    {
        M2Share.RandomNumber = oldRandom;
        M2Share.g_Config.boAuthOpen = oldAuth;
        M2Share.g_Config.nPKPunishPoint = oldThreshold;
    }
}

void VerifyHeroDeathRoutingAndSm917()
{
    var oldRandom = M2Share.RandomNumber;
    var oldThreshold = M2Share.g_Config.nPKPunishPoint;
    try
    {
        M2Share.g_Config.nPKPunishPoint = 200;
        var owner = NewRecordingPlayerOnMap("hero-die-owner", "hero-die-user");
        var hero = NewHeroOnMap(owner, "hero-die");
        var killer = NewPlayerOnMap("hero-die-killer", "hero-die-killer-user");
        killer.m_btNativeDropRareKillerBonus = 10;
        hero.m_LastHiter = killer;

        var stdItem = new GoodItem
        {
            Name = "hero-die-routing", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000
        };
        var equipped = MakeRawItem(stdItem);
        equipped.NativeClassFc = 1;
        var bagged = MakeRawItem(stdItem);
        bagged.NativeClassFc = 1;
        var equippedId = owner.EnsureClientItemId(equipped);
        var baggedId = owner.EnsureClientItemId(bagged);
        hero.m_UseItems[2] = equipped;
        hero.m_ItemList.Add(bagged);
        var deathDraws = new SequenceRandomNumber(1);
        M2Share.RandomNumber = deathDraws;
        var groundBefore = CountGroundItems(owner);

        hero.Die();

        Assert(ReferenceEquals(hero.m_Master, owner),
            "THeroAct owner +0x68C must survive the generic death cleanup");
        Assert(hero.m_UseItems[2] == null && hero.m_ItemList.Count == 0
            && CountGroundItems(owner) == groundBefore + 2,
            "real HeroObject.Die did not run equipment first and bag second through the human worker pair");
        Assert(deathDraws.Bounds.SequenceEqual(new[] { 80 }),
            "real Die must preserve LastHiter for equip K=90-killerBonus; bag ClassFc consumes no RNG");

        var deleteMessages = hero.m_MsgList
            .Where(message => message.wIdent == Grobal2.RM_SENDDELITEMLIST)
            .ToList();
        Assert(deleteMessages.Count == 2,
            "equipment and bag workers must each queue one RM_SENDDELITEMLIST batch");
        var queuedIds = deleteMessages.Select(message =>
        {
            Assert(message.nParam1 == 1 && message.Payload is byte[] { Length: 4 },
                "native hero delete RM must carry count=1 and exactly count*4 bytes");
            return BinaryPrimitives.ReadInt32LittleEndian((byte[])message.Payload);
        }).ToArray();
        Assert(queuedIds.SequenceEqual(new[] { equippedId, baggedId }),
            "hero deletion batches must preserve equipment-before-bag ClientItemID order");

        owner.BinaryPackets.Clear();
        foreach (var message in deleteMessages)
        {
            hero.Operate(new TProcessMessage
            {
                wIdent = message.wIdent,
                nParam1 = message.nParam1,
                Payload = message.Payload
            });
        }
        Assert(owner.BinaryPackets.Count == 2, "hero RM 10148 did not forward two SM917 packets");
        for (var i = 0; i < owner.BinaryPackets.Count; i++)
        {
            var packet = owner.BinaryPackets[i];
            Assert(packet.Header.Ident == Grobal2.SM_HERO_DELITEMS
                && packet.Header.Recog == 1
                && packet.Header.Param == 0
                && packet.Header.Tag == 0
                && packet.Header.Series == 0,
                "SM917 header fields differ from 0x6896ED..0x689708");
            Assert(packet.Body.Length == 4
                && BinaryPrimitives.ReadInt32LittleEndian(packet.Body) == queuedIds[i],
                "SM917 body must be the unchanged ClientItemID dword with no trailing zero");
        }
        Log("HERO DEATH ROUTING: real Die preserves owner/LastHiter, runs equip then bag, "
            + "queues two count*4 buffers and forwards exact SM_HERO_DELITEMS=917 frames");
    }
    finally
    {
        M2Share.RandomNumber = oldRandom;
        M2Share.g_Config.nPKPunishPoint = oldThreshold;
    }
}

void VerifyHumanDeathTailAfterDropException()
{
    var oldRandom = M2Share.RandomNumber;
    var flag = M2Share.MapManager.FindMap("0")?.Flag
        ?? throw new Exception("test map '0' has no flag record");
    var oldOnlyDropSpec = flag.boONLYDROPSPEC;
    try
    {
        flag.boONLYDROPSPEC = true;
        var player = NewPlayerOnMap("human-die-exception", "human-die-exception-user");
        player.m_boObMode = true;
        player.m_dwSearchTick = 12345;
        player.m_nBodyLuckLevel = 0;

        var special = MakeRawItem(new GoodItem
        {
            Name = "human-die-exception-special", StdMode = 96,
            Shape = 1, Weight = 1, DuraMax = 5000, IntParam1 = 100
        });
        player.m_ItemList.Add(special);
        var throwingRandom = new SequenceRandomNumber();
        M2Share.RandomNumber = throwingRandom;

        player.Die();

        Assert(throwingRandom.Bounds.SequenceEqual(new[] { 100 }),
            "THumanKind drop exception must originate at the one Random(100) roll");
        Assert(player.m_ItemList.Count == 1
            && ReferenceEquals(player.m_ItemList[0], special)
            && special.wIndex != 0,
            "THumanKind drop exception mutated the pending special item");
        Assert(player.m_nBodyLuckLevel == 1,
            "THumanKind drop exception skipped the later TPlayer death tail");
        Assert(player.m_dwSearchTick == 0,
            "0x7414DB search-view tick clear was skipped after a drop exception");
        Assert(player.m_MsgList.Count(message =>
                message.wIdent == Grobal2.RM_DEATH) == 1,
            "THumanKind drop exception skipped or duplicated RM_DEATH");
        Log("HUMAN DEATH EXCEPTION: Random(100) worker fault is logged, item stays, "
            + "player tail/search-tick/RM_DEATH continue exactly once");
    }
    finally
    {
        flag.boONLYDROPSPEC = oldOnlyDropSpec;
        M2Share.RandomNumber = oldRandom;
    }
}

void VerifyNativeDeathDropNotice()
{
    const int gateOffset = 0x57C;
    const int maskOffset = 0x580;
    var oldRandom = M2Share.RandomNumber;
    var map = M2Share.MapManager.FindMap("0")
        ?? throw new Exception("test map '0' was not registered");
    var oldFight = map.Flag.boFightZone;
    var oldFight3 = map.Flag.boFight3Zone;
    var oldSafe = map.Flag.boSAFE;
    try
    {
        // sub_6AFD7C load: record+580 -> obj+60C, record+57C -> obj+610,
        // then 0x6B060A..11 normalizes every non-positive gate to 1.
        foreach (var storedGate in new[] { int.MinValue, -1, 0, 1, 2, 3, int.MaxValue })
        {
            var state = NewPlayer("notice-state-" + storedGate, "notice-state-user");
            state.m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
            BinaryPrimitives.WriteUInt32LittleEndian(
                state.m_NativeHumanData.AsSpan(maskOffset, sizeof(uint)), 0xA5A55A5Au);
            BinaryPrimitives.WriteInt32LittleEndian(
                state.m_NativeHumanData.AsSpan(gateOffset, sizeof(int)), storedGate);
            Assert(state.RestoreNativeHeroZodiacState(),
                "native zodiac state failed to restore from a full human record");
            Assert(state.HeroZodiacBlessMask == 0xA5A55A5Au
                && state.HeroZodiacBlessGate == (storedGate > 0 ? storedGate : 1),
                "record+57C/+580 load or <=0 gate normalization differs from 0x6B05E9..11");
        }

        var shortState = NewPlayer("notice-state-short", "notice-state-short-user");
        shortState.HeroZodiacBlessMask = uint.MaxValue;
        shortState.HeroZodiacBlessGate = 3;
        shortState.m_NativeHumanData = new byte[maskOffset];
        Assert(!shortState.RestoreNativeHeroZodiacState()
            && shortState.HeroZodiacBlessMask == 0
            && shortState.HeroZodiacBlessGate == 0,
            "short native record must fail closed instead of retaining stale live zodiac state");

        var persisted = NewPlayer("notice-state-save", "notice-state-save-user");
        persisted.m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
        persisted.HeroZodiacBlessMask = 0x81234567u;
        persisted.HeroZodiacBlessGate = -7;
        Assert(persisted.PersistNativeHeroZodiacState(),
            "native zodiac state failed to persist into a full human record");
        Assert(BinaryPrimitives.ReadUInt32LittleEndian(
                persisted.m_NativeHumanData.AsSpan(maskOffset, sizeof(uint))) == 0x81234567u
            && BinaryPrimitives.ReadInt32LittleEndian(
                persisted.m_NativeHumanData.AsSpan(gateOffset, sizeof(int))) == -7,
            "save must write live obj+60C/+610 verbatim at 0x6B13D9..EB");

        var player = NewRecordingPlayerOnMap("notice-player", "notice-player-user");
        SetNoticeMode(player, 2);
        var cases = new[]
        {
            (Mode: 2, Count: 0, Bound: 40, Tail: "您死亡没有爆出装备！"),
            (Mode: 2, Count: 1, Bound: 25, Tail: "您死亡应爆出装备的数量减少了一半！"),
            (Mode: 2, Count: 2, Bound: 25, Tail: "您死亡爆出装备的件数减少了！"),
            (Mode: 3, Count: 0, Bound: 30, Tail: "您死亡没有爆出装备！"),
            (Mode: 3, Count: 1, Bound: 20, Tail: "您死亡应爆出装备的数量减少了一半！"),
            (Mode: 3, Count: 2, Bound: 20, Tail: "您死亡爆出装备的件数减少了！")
        };
        foreach (var c in cases)
        {
            player.m_MsgList.Clear();
            player.HeroZodiacBlessGate = c.Mode;
            var draws = new SequenceRandomNumber(1);
            M2Share.RandomNumber = draws;
            player.TryNativeDeathDropAreaNotice(c.Count, player);
            Assert(draws.Bounds.SequenceEqual(new[] { c.Bound }),
                $"notice mode={c.Mode} count={c.Count} used the wrong Random bound");
            var queued = player.m_MsgList.Single();
            var bagName = c.Mode == 2 ? "极品神佑袋" : "顶级神佑袋";
            Assert(queued.wIdent == Grobal2.RM_SYSMESSAGE
                && queued.nParam1 == 0xFF && queued.nParam2 == 0x38
                && queued.nParam3 == 0 && ReferenceEquals(queued.BaseObject, player)
                && queued.Buff == $"由于您的{bagName}发挥作用，{c.Tail}",
                "sub_73E4C4 queued message/color/body differs from 0x73E67D..88");
        }

        player.m_MsgList.Clear();
        player.HeroZodiacBlessGate = 2;
        var miss = new SequenceRandomNumber(0);
        M2Share.RandomNumber = miss;
        player.TryNativeDeathDropAreaNotice(0, player);
        Assert(miss.Bounds.SequenceEqual(new[] { 40 }) && player.m_MsgList.Count == 0,
            "notice must hit only when Random(bound)==1");

        player.m_MsgList.Clear();
        var otherCount = new SequenceRandomNumber(1);
        M2Share.RandomNumber = otherCount;
        player.TryNativeDeathDropAreaNotice(3, player);
        Assert(otherCount.Bounds.SequenceEqual(new[] { 50 }) && player.m_MsgList.Count == 0,
            "count>=3 must consume Random(50) and still send no message");

        player.HeroZodiacBlessGate = 1;
        var modeOne = new SequenceRandomNumber();
        M2Share.RandomNumber = modeOne;
        player.TryNativeDeathDropAreaNotice(0, player);
        Assert(modeOne.Bounds.Count == 0 && player.m_MsgList.Count == 0,
            "mode outside 2/3 must return before RNG");

        player.HeroZodiacBlessGate = 2;
        foreach (var gate in new[] { "fight", "fight3", "safe" })
        {
            map.Flag.boFightZone = gate == "fight";
            map.Flag.boFight3Zone = gate == "fight3";
            map.Flag.boSAFE = gate == "safe";
            var gated = new SequenceRandomNumber();
            M2Share.RandomNumber = gated;
            player.m_MsgList.Clear();
            player.TryNativeDeathDropAreaNotice(0, player);
            Assert(gated.Bounds.Count == 0 && player.m_MsgList.Count == 0,
                gate + " map gate must exit before owner mode and RNG");
        }
        map.Flag.boFightZone = false;
        map.Flag.boFight3Zone = false;
        map.Flag.boSAFE = false;

        var stdItem = new GoodItem
        {
            Name = "death-notice-caller", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000
        };
        var bagStd = new GoodItem
        {
            Name = "death-notice-bag-gate", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000
        };

        // Exact player caller gates. A present item that loses the equipment roll gives
        // count=0; this is the native branch the old `dropCount<=0` stub erased.
        M2Share.RandomNumber = oldRandom;
        var caller = NewRecordingPlayerOnMap("notice-caller", "notice-caller-user");
        SetNoticeMode(caller, 2);
        caller.m_UseItems[0] = MakeRawItem(stdItem);
        caller.m_ItemList.Add(MakeRawItem(bagStd));
        var callerDraws = new SequenceRandomNumber(1, 1);
        M2Share.RandomNumber = callerDraws;
        miDropUseItems.Invoke(caller, new object[] { null });
        Assert(callerDraws.Bounds.SequenceEqual(new[] { 90, 40 })
            && caller.m_MsgList.Count(message => message.wIdent == Grobal2.RM_SYSMESSAGE) == 1,
            "player caller did not preserve any-equip + owner-bag + valid count=0 notice");
        var playerNotice = caller.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE);
        caller.Operate(ToProcessMessage(playerNotice));
        var playerWire = caller.TextPackets.Single();
        Assert(playerWire.Header.Ident == Grobal2.SM_SYSMESSAGE
            && playerWire.Header.Recog == caller.ObjectId
            && playerWire.Header.Param == 0x38FF
            && playerWire.Header.Tag == 0 && playerWire.Header.Series == 1
            && playerWire.Text == "由于您的极品神佑袋发挥作用，您死亡没有爆出装备！",
            "player notice did not become exact SM100/0x38FF/self/plain-text output");

        M2Share.RandomNumber = oldRandom;
        var noEquip = NewPlayerOnMap("notice-no-equip", "notice-no-equip-user");
        SetNoticeMode(noEquip, 2);
        noEquip.m_ItemList.Add(MakeRawItem(bagStd));
        var noEquipDraws = new SequenceRandomNumber();
        M2Share.RandomNumber = noEquipDraws;
        miDropUseItems.Invoke(noEquip, new object[] { null });
        Assert(noEquipDraws.Bounds.Count == 0
            && noEquip.m_MsgList.All(message => message.wIdent != Grobal2.RM_SYSMESSAGE),
            "empty initial equipment set must not call sub_73E4C4");

        M2Share.RandomNumber = oldRandom;
        var emptyBag = NewPlayerOnMap("notice-empty-bag", "notice-empty-bag-user");
        SetNoticeMode(emptyBag, 2);
        emptyBag.m_UseItems[0] = MakeRawItem(stdItem);
        var emptyBagDraws = new SequenceRandomNumber(1);
        M2Share.RandomNumber = emptyBagDraws;
        miDropUseItems.Invoke(emptyBag, new object[] { null });
        Assert(emptyBagDraws.Bounds.SequenceEqual(new[] { 90 })
            && emptyBag.m_MsgList.All(message => message.wIdent != Grobal2.RM_SYSMESSAGE),
            "owner bag Count==0 must suppress the notice RNG and message");

        // Hero resolves both mode and bag through master, but queues on the hero. Its
        // dispatcher prefixes the body and keeps Recog=hero when forwarding to master.
        M2Share.RandomNumber = oldRandom;
        var heroOwner = NewRecordingPlayerOnMap("notice-hero-owner", "notice-hero-user");
        SetNoticeMode(heroOwner, 3);
        heroOwner.m_ItemList.Add(MakeRawItem(bagStd));
        var hero = NewHeroOnMap(heroOwner, "notice-hero");
        hero.m_UseItems[0] = MakeRawItem(stdItem);
        var heroDraws = new SequenceRandomNumber(1, 1);
        M2Share.RandomNumber = heroDraws;
        hero.NativeHeroDropUseItems(null, heroOwner);
        Assert(heroDraws.Bounds.SequenceEqual(new[] { 90, 30 }),
            "hero notice did not use master mode 3 and count0 bound 30");
        var heroNotice = hero.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE);
        hero.Operate(ToProcessMessage(heroNotice));
        var heroWire = heroOwner.BinaryPackets.Single();
        var expectedHeroBody = HUtil32.GetBytes(
            "(英雄) 由于您的顶级神佑袋发挥作用，您死亡没有爆出装备！\0");
        Assert(heroWire.Header.Ident == Grobal2.SM_SYSMESSAGE
            && heroWire.Header.Recog == hero.ObjectId
            && heroWire.Header.Param == 0x38FF
            && heroWire.Header.Tag == 0 && heroWire.Header.Series == 1
            && heroWire.Body.SequenceEqual(expectedHeroBody),
            "hero notice did not preserve hero Recog/master-only forwarding/prefix/NUL body");

        Log("DEATH DROP NOTICE: sub_73E4C4 mode 2/3 bounds, hit==1, count0/1/2 text, "
            + "count>=3 draw-only, map/caller gates, live +60C/+610 persistence, player+hero wire");
    }
    finally
    {
        M2Share.RandomNumber = oldRandom;
        map.Flag.boFightZone = oldFight;
        map.Flag.boFight3Zone = oldFight3;
        map.Flag.boSAFE = oldSafe;
    }

    void SetNoticeMode(TPlayObject owner, int mode)
    {
        owner.m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
        BinaryPrimitives.WriteInt32LittleEndian(
            owner.m_NativeHumanData.AsSpan(gateOffset, sizeof(int)), mode);
        Assert(owner.RestoreNativeHeroZodiacState(), "notice owner mode restore failed");
    }

    static TProcessMessage ToProcessMessage(SendMessage message) => new()
    {
        wIdent = message.wIdent,
        wParam = message.wParam,
        nParam1 = message.nParam1,
        nParam2 = message.nParam2,
        nParam3 = message.nParam3,
        BaseObject = message.BaseObject?.ObjectId ?? 0,
        sMsg = message.Buff,
        Payload = message.Payload
    };
}

void VerifyNativeItemMovementSms()
{
    var oldServerName = M2Share.g_Config.sServerName;
    var oldAuthOpen = M2Share.g_Config.boAuthOpen;
    var oldShowPrefix = M2Share.g_Config.boShowPreFixMsg;
    var oldHintPrefix = M2Share.g_Config.sHintMsgPreFix;
    var oldGreenFColor = M2Share.g_Config.btGreenMsgFColor;
    var oldGreenBColor = M2Share.g_Config.btGreenMsgBColor;
    var oldRandom = M2Share.RandomNumber;
    try
    {
        M2Share.g_Config.sServerName = "S1";

        foreach (var value in new byte[] { 0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0xFF })
        {
            var state = NewPlayer("sms-state-" + value, "sms-state-user");
            state.m_NativeDbSessionSuffix =
                new byte[NativeHumanDbCodec.SessionSuffixSize];
            state.m_NativeDbSessionSuffix[
                TPlayObject.NativeItemMovementSmsSuffixOffset] = value;
            Assert(state.RestoreNativeItemMovementSmsState(),
                "full session suffix was rejected");
            Assert(state.m_boNativeItemMovementSmsEnabled == ((value & 1) != 0),
                $"suffix+0x56 value 0x{value:X2} did not isolate bit 0");
        }

        var shortState = NewPlayer("sms-state-short", "sms-state-short-user");
        shortState.m_boNativeItemMovementSmsEnabled = true;
        shortState.m_NativeDbSessionSuffix =
            new byte[TPlayObject.NativeItemMovementSmsSuffixOffset];
        Assert(!shortState.RestoreNativeItemMovementSmsState()
            && !shortState.m_boNativeItemMovementSmsEnabled,
            "short suffix must clear stale SMS state and fail closed");

        var owner = NewPlayerOnMap("sms-owner", "sms-ptid");
        owner.m_boNativeItemMovementSmsEnabled = true;
        var hero = NewHeroOnMap(owner, "sms-hero");
        hero.CopyNativeItemMovementSmsState(owner);
        owner.m_boNativeItemMovementSmsEnabled = false;
        Assert(hero.m_boNativeItemMovementSmsEnabled,
            "hero did not retain the one-time owner SMS-state snapshot");
        var laterHero = NewHeroOnMap(owner, "sms-hero-later");
        laterHero.CopyNativeItemMovementSmsState(owner);
        Assert(!laterHero.m_boNativeItemMovementSmsEnabled,
            "a later hero did not snapshot the owner's current false state");

        var ascii = TBaseObject.BuildNativeItemMovementSmsPayload(
            "S1", "uid", "Hero", "Sword", 0x12345678, 1);
        var expected = new byte[TBaseObject.NativeItemMovementSmsPayloadSize];
        System.Text.Encoding.ASCII.GetBytes("S1").CopyTo(expected, 0x00);
        System.Text.Encoding.ASCII.GetBytes("uid").CopyTo(expected, 0x10);
        System.Text.Encoding.ASCII.GetBytes("Hero").CopyTo(expected, 0x30);
        System.Text.Encoding.ASCII.GetBytes("Sword").CopyTo(expected, 0x50);
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(0x70, 4),
            0x12345678);
        expected[0x74] = 1;
        Assert(ascii.SequenceEqual(expected),
            "sub_743F14 0x78 ASCII golden payload differs");

        var split = TBaseObject.BuildNativeItemMovementSmsPayload(
            new string('A', 14) + "中", new string('B', 30) + "中",
            "角色", "屠龙", int.MinValue, 0xFF);
        Assert(split[14] == 0xD6 && split[15] == 0
            && split[0x10 + 30] == 0xD6 && split[0x10 + 31] == 0,
            "15/31-byte StrPLCopy boundary must preserve the first half of GBK 中");
        Assert(BinaryPrimitives.ReadInt32LittleEndian(split.AsSpan(0x70, 4))
                == int.MinValue
            && split[0x74] == 0xFF
            && split.AsSpan(0x75, 3).SequenceEqual(new byte[3]),
            "MakeIndex/event/tail bytes differ from native 0x70/0x74/0x75");

        var noticePlayer = NewPlayer("sms-login", "sms-login-user");
        noticePlayer.m_boNativeItemMovementSmsEnabled = true;
        M2Share.g_Config.boShowPreFixMsg = true;
        M2Share.g_Config.sHintMsgPreFix = "must-not-prefix:";
        M2Share.g_Config.btGreenMsgFColor = 0x11;
        M2Share.g_Config.btGreenMsgBColor = 0x22;
        noticePlayer.m_btPermission = 2;
        noticePlayer.SendNativeItemMovementSmsLoginNotice();
        Assert(noticePlayer.m_MsgList.Count == 0,
            "permission below 3 reached the 0x6B221C SMS login notice");
        noticePlayer.m_btPermission = 3;
        noticePlayer.SendNativeItemMovementSmsLoginNotice();
        var loginNotice = noticePlayer.m_MsgList.Single();
        var expectedLoginBody = TBaseObject.BuildNativeTerminatedTextBody(
            TPlayObject.NativeItemMovementSmsLoginNotice);
        Assert(loginNotice.wIdent == Grobal2.RM_SYSMESSAGE
            && loginNotice.wParam == 0xFFDB
            && loginNotice.nParam1 == 0
            && loginNotice.nParam2 == 0
            && loginNotice.nParam3 == 0
            && loginNotice.Buff == TPlayObject.NativeItemMovementSmsLoginNotice
            && loginNotice.Payload is byte[] loginBody
            && loginBody.SequenceEqual(expectedLoginBody),
            "enabled permission>=3 login notice differs from fixed cx=0xFFDB");

        var std = new GoodItem
        {
            Name = "sms-direct-item", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000, NativeReserved02 = 0x0200
        };
        var direct = NewPlayerOnMap("sms-direct", "sms-direct-user");
        var directItem = MakeRawItem(std);
        directItem.MakeIndex = 0x10203040;
        M2Share.LogStringList.Clear();
        Assert(!direct.TryNotifyNativeItemMovementSms(direct, std, directItem, 0)
            && SmsLogs().Count == 0,
            "disabled actor passed the SMS gate");
        direct.m_boNativeItemMovementSmsEnabled = true;
        std.NativeReserved02 = 0;
        Assert(!direct.TryNotifyNativeItemMovementSms(direct, std, directItem, 0)
            && SmsLogs().Count == 0,
            "std item without high-byte bit 1 passed the SMS gate");
        std.NativeReserved02 = 0x0200;
        var originalIndex = directItem.wIndex;
        Assert(!direct.TryNotifyNativeItemMovementSms(direct, std, directItem, 0),
            "inactive 6108 manager must return false after logging");
        Assert(SmsLogs().SequenceEqual(new[]
        {
            "153\t0\t20\t20\tsms-direct\tsms-direct-item\t270544960\t0\t短信提醒"
        }), "inactive manager did not retain exactly one prior actor log");
        Assert(directItem.wIndex == originalIndex
            && directItem.MakeIndex == 0x10203040,
            "SMS helper mutated the item");

        M2Share.g_Config.boAuthOpen = true;
        M2Share.RandomNumber = new FixedRandomNumber(0);
        var destroyStd = new GoodItem
        {
            Name = "sms-death-destroy", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000,
            NativeReserved02 = 0x0010 | 0x0200
        };
        var destroyPlayer = NewPlayerOnMap("sms-destroy", "sms-destroy-user");
        destroyPlayer.m_boNativeItemMovementSmsEnabled = true;
        destroyPlayer.m_UseItems[0] = MakeRawItem(destroyStd);
        M2Share.LogStringList.Clear();
        miDropUseItems.Invoke(destroyPlayer, new object[] { null });
        Assert(SmsLogs().Count == 1,
            "0x73FE49 death-destroy caller did not notify once");

        M2Share.g_Config.boAuthOpen = false;
        M2Share.RandomNumber = new FixedRandomNumber(0);
        var groundStd = new GoodItem
        {
            Name = "sms-death-ground", StdMode = 5, Shape = 1,
            Weight = 1, DuraMax = 5000, NativeReserved02 = 0x0200
        };
        var groundPlayer = NewPlayerOnMap("sms-ground", "sms-ground-user");
        groundPlayer.m_boNativeItemMovementSmsEnabled = true;
        groundPlayer.m_UseItems[0] = MakeRawItem(groundStd);
        M2Share.LogStringList.Clear();
        miDropUseItems.Invoke(groundPlayer, new object[] { null });
        Assert(SmsLogs().Count == 1,
            "0x73FF61 player ground-success caller did not notify once");

        M2Share.RandomNumber = new FixedRandomNumber(0);
        var heroOwner = NewPlayerOnMap("sms-hero-owner", "sms-hero-owner-user");
        heroOwner.m_boNativeItemMovementSmsEnabled = true;
        var groundHero = NewHeroOnMap(heroOwner, "sms-ground-hero");
        groundHero.CopyNativeItemMovementSmsState(heroOwner);
        groundHero.m_UseItems[0] = MakeRawItem(groundStd);
        M2Share.LogStringList.Clear();
        groundHero.NativeHeroDropUseItems(null, heroOwner);
        Assert(SmsLogs().Single().StartsWith(
                "153\t0\t30\t30\tsms-ground-hero\tsms-death-ground\t")
            && !SmsLogs().Single().Contains("sms-hero-owner\tsms-death-ground"),
            "hero SMS log must use actor identity, not payload-owner identity");

        Log("ITEM MOVEMENT SMS: suffix+0x56 bit0, hero snapshot, byte-cut GBK "
            + "0x78 payload, inactive-log ordering, destroy/ground callers");
    }
    finally
    {
        M2Share.g_Config.sServerName = oldServerName;
        M2Share.g_Config.boAuthOpen = oldAuthOpen;
        M2Share.g_Config.boShowPreFixMsg = oldShowPrefix;
        M2Share.g_Config.sHintMsgPreFix = oldHintPrefix;
        M2Share.g_Config.btGreenMsgFColor = oldGreenFColor;
        M2Share.g_Config.btGreenMsgBColor = oldGreenBColor;
        M2Share.RandomNumber = oldRandom;
        M2Share.LogStringList.Clear();
    }

    List<string> SmsLogs() => M2Share.LogStringList.OfType<string>()
        .Where(value => value.StartsWith("153\t", StringComparison.Ordinal))
        .ToList();
}

void VerifyNativeAmuletConsumeGate()
{
    VerifyActor("player",
        () => NewPlayer("amulet-player", "amulet-player-user"),
        (actor, count, consume) =>
            ((TPlayObject)actor).NativeConsumeBujukCharm(count, consume));
    VerifyActor("hero",
        () => new HeroObject
        {
            m_sCharName = "amulet-hero", m_boGhost = false, m_boDeath = false
        },
        (actor, count, consume) =>
            ((HeroObject)actor).NativeConsumeBujukCharm(count, consume));

    void VerifyActor(string label, Func<TBaseObject> createActor,
        Func<TBaseObject, int, bool, bool> consume)
    {
        var empty = createActor();
        var emptyMessages = empty.m_MsgList.Count;
        Assert(!consume(empty, 100, false) && !consume(empty, 100, true),
            $"{label} amulet no-match result must stay false");
        Assert(empty.m_MsgList.Count == emptyMessages,
            $"{label} amulet no-match emitted a message");

        var equipProbe = createActor();
        var equipProbeItem = MakeCharm();
        equipProbe.m_UseItems[Grobal2.U_BUJUK] = equipProbeItem;
        var equipProbeMessages = equipProbe.m_MsgList.Count;
        Assert(consume(equipProbe, 100, false),
            $"{label} equipment test-only probe did not return found");
        Assert(equipProbeItem.Dura == 500
            && ReferenceEquals(equipProbe.m_UseItems[Grobal2.U_BUJUK],
                equipProbeItem),
            $"{label} equipment test-only probe mutated the item/container");
        Assert(equipProbe.m_MsgList.Count == equipProbeMessages,
            $"{label} equipment test-only probe emitted a message");

        var equipConsume = createActor();
        var equipConsumeItem = MakeCharm();
        equipConsume.m_UseItems[Grobal2.U_BUJUK] = equipConsumeItem;
        Assert(consume(equipConsume, 100, true),
            $"{label} equipment consume did not return found");
        Assert(equipConsumeItem.Dura == 400
            && ReferenceEquals(equipConsume.m_UseItems[Grobal2.U_BUJUK],
                equipConsumeItem),
            $"{label} equipment consume did not apply the raw decrement");

        var bagProbe = createActor();
        var bagProbeItem = MakeCharm();
        bagProbe.m_ItemList.Add(bagProbeItem);
        var bagProbeMessages = bagProbe.m_MsgList.Count;
        Assert(consume(bagProbe, 100, false),
            $"{label} bag test-only probe did not return found");
        Assert(bagProbeItem.Dura == 500 && bagProbe.m_ItemList.Count == 1
            && ReferenceEquals(bagProbe.m_ItemList[0], bagProbeItem),
            $"{label} bag test-only probe mutated the item/container");
        Assert(bagProbe.m_MsgList.Count == bagProbeMessages,
            $"{label} bag test-only probe emitted a message");

        var bagConsume = createActor();
        var bagConsumeItem = MakeCharm();
        bagConsume.m_ItemList.Add(bagConsumeItem);
        Assert(consume(bagConsume, 100, true),
            $"{label} bag consume did not return found");
        Assert(bagConsumeItem.Dura == 400 && bagConsume.m_ItemList.Count == 1
            && ReferenceEquals(bagConsume.m_ItemList[0], bagConsumeItem),
            $"{label} bag consume did not apply the raw decrement");
    }

    TUserItem MakeCharm()
    {
        var item = MakeRawItem(new GoodItem
        {
            Name = "dura38-bujuk", StdMode = 25, Shape = 5,
            Weight = 1, DuraMax = 1000
        });
        item.Dura = 500;
        item.DuraMax = 1000;
        return item;
    }
}

// ===================== helpers ==================================================================

// Total item OBJECT REFERENCES the player can reach. Conservation means this is invariant
// across a container move: a dupe raises it, a loss lowers it.
int CountRefs(TPlayObject p)
{
    var n = p.m_ItemList.Count;
    for (var i = 0; i < p.m_UseItems.Length; i++)
        if (p.m_UseItems[i] != null) n++;
    if (p.m_HeroObject != null) n += p.m_HeroObject.m_ItemList.Count;
    return n;
}

int CountGroundItems(TPlayObject p)
{
    var envir = p.m_PEnvir;
    if (envir == null) return 0;
    var n = 0;
    for (short x = 0; x < envir.wWidth; x++)
        for (short y = 0; y < envir.wHeight; y++)
            if (envir.GetItem(x, y) != null) n++;
    return n;
}

ushort ReadBindWord(TUserItem item) =>
    (ushort)(item.btValue[10] | (item.btValue[11] << 8));

TProcessMessage NewMsg(int clientItemId) =>
    new TProcessMessage { nParam1 = clientItemId };

TUserItem MakeItem(string name)
{
    TUserItem item = null;
    return M2Share.UserEngine.CopyToUserItemFromName(name, ref item) ? item : null;
}

// A raw item bound to an arbitrary injected StdItem (used by the stamper assertions so the
// Reserved02 class can be chosen per case).
TUserItem MakeRawItem(GoodItem stdItem)
{
    var eng = M2Share.UserEngine;
    if (eng.GetStdItem(stdItem.Name) == null)
    {
        stdItem.NativeWireIndex = (ushort)eng.StdItemList.Count;
        eng.StdItemList.Add(stdItem);
    }
    TUserItem item = null;
    if (!eng.CopyToUserItemFromName(stdItem.Name, ref item)) throw new Exception(
        "could not build a raw item for " + stdItem.Name);
    return item;
}

TPlayObject NewPlayer(string charName, string userId)
{
    var p = new TPlayObject
    {
        m_boOffLineFlag = true, m_boGhost = false, m_boDeath = false,
        m_sCharName = charName, m_sUserID = userId
    };
    p.m_Abil.Level = 30;
    // Give the harness player enough carry/wear capacity that CheckTakeOnItems'
    // weight gates never mask the conservation assertions under test.
    p.m_Abil.MaxWeight = 30000;
    p.m_Abil.MaxWearWeight = 30000;
    p.m_Abil.MaxHandWeight = 30000;
    p.m_WAbil.MaxWeight = 30000;
    p.m_WAbil.MaxWearWeight = 30000;
    p.m_WAbil.MaxHandWeight = 30000;
    return p;
}

TPlayObject NewPlayerOnMap(string charName, string userId)
{
    var p = NewPlayer(charName, userId);
    var envir = M2Share.MapManager.FindMap("0");
    if (envir == null) throw new Exception("test map '0' was not registered");
    p.m_PEnvir = envir;
    p.m_sMapName = "0";
    p.m_nCurrX = 20;
    p.m_nCurrY = 20;
    // The manual-drop handler gates on `GetTickCount() - m_DealLastTick > 3000`
    // (战神 sub_73CC98 @0x73CCC5 `sub eax,[esi+0x46C]; cmp eax,0xBB8; jbe`).
    p.m_DealLastTick = 0;
    p.m_boCanDrop = true;
    return p;
}

HeroObject NewHeroOnMap(TPlayObject owner, string charName)
{
    var hero = new HeroObject
    {
        m_sCharName = charName,
        m_boGhost = false,
        m_boDeath = false,
        m_Master = owner,
        m_PEnvir = owner.m_PEnvir,
        m_sMapName = owner.m_sMapName,
        m_nCurrX = 30,
        m_nCurrY = 30
    };
    hero.m_Abil.Level = 30;
    owner.m_HeroObject = hero;
    return hero;
}

RecordingPlayObject NewRecordingPlayerOnMap(string charName, string userId)
{
    var player = new RecordingPlayObject
    {
        m_boOffLineFlag = false,
        m_boGhost = false,
        m_boDeath = false,
        m_sCharName = charName,
        m_sUserID = userId,
        m_PEnvir = M2Share.MapManager.FindMap("0"),
        m_sMapName = "0",
        m_nCurrX = 20,
        m_nCurrY = 20,
        m_boCanDrop = true
    };
    player.m_Abil.Level = 30;
    player.m_Abil.MaxWeight = 30000;
    player.m_Abil.MaxWearWeight = 30000;
    player.m_Abil.MaxHandWeight = 30000;
    player.m_WAbil.MaxWeight = 30000;
    player.m_WAbil.MaxWearWeight = 30000;
    player.m_WAbil.MaxHandWeight = 30000;
    return player;
}

void InjectNativeDefs()
{
    var eng = M2Share.UserEngine;
    eng.StdItemList.Add(new GoodItem
    { Name = "金币", NativeWireIndex = 0, ItemType = GoodType.ITEM_GOLD });
    eng.StdItemList.Add(new GoodItem
    {
        Name = "铁剑", NativeWireIndex = 1, ItemType = GoodType.ITEM_WEAPON,
        StdMode = 5, Shape = 1, Weight = 1, DuraMax = 5000, Dc = 3, Dc2 = 8
    });
    Assert(eng.GetStdItem("铁剑") != null, "injected weapon StdItem resolves by name");

    // Blank in-memory map (same idiom as InProcEngineRunCheck.CreateBlankMap): the private
    // Envirnoment.Initialize allocates the cell arrays and the default CellAttribute (0) is
    // walkable, so a file-less map accepts DropItemDown.
    var map = new Envirnoment { sMapName = "0", sMapDesc = "test", m_sMapFileName = "0" };
    var init = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    init.Invoke(map, new object[] { (short)64, (short)64 });
    map.Flag = new TMapFlag();
    var mapListField = typeof(MapManager).GetField("m_MapList",
        BindingFlags.Instance | BindingFlags.NonPublic);
    if (mapListField?.GetValue(M2Share.MapManager) is System.Collections.IDictionary dict
        && !dict.Contains("0"))
    {
        dict.Add("0", map);
    }
    Assert(M2Share.MapManager.FindMap("0") == map, "blank test map '0' is registered");
}

void PrepareConfig()
{
    var baseDir = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
    var share = Path.GetFullPath(Path.Combine(baseDir, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
}

void BootSingletons()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.g_Config.sConnctionString = string.Empty;
    M2Share.g_Config.boAuthOpen = false;
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.MapManager = new MapManager();
    M2Share.CastleManager = new CastleManager();   // CheckTakeOnItems calls IsCastleMember
    // CheckItemBindUse walks these config lists (an unrelated account/IP binding
    // feature, NOT the native item+0x34 gate); empty lists keep it a pass-through.
    M2Share.g_ItemBindAccount = new List<TItemBind>();
    M2Share.g_ItemBindIPaddr = new List<TItemBind>();
    M2Share.g_ItemBindCharName = new List<TItemBind>();
    M2Share.StartPointList = new List<TStartPoint>();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

// The GBK notices, quoted here so the audit fails if the literals are ever retyped.
// Read byte-for-byte out of the image (Delphi long strings, length dword at VA-4).
static class NativeItemDropDestroyNotices
{
    public const string DropUnverified = "未验证,物品消失(丢弃)";   // 0x73CE74 len=21
    public const string DropGift = "赠品,物品消失(丢弃)";           // 0x73CE94 len=19
}

sealed class FixedRandomNumber : RandomNumber
{
    private readonly int _result;

    public int Calls { get; private set; }

    public FixedRandomNumber(int result)
    {
        _result = result;
    }

    public override int Random(int value)
    {
        Calls++;
        return _result;
    }
}

sealed class SequenceRandomNumber : RandomNumber
{
    private readonly Queue<int> _results;

    public List<int> Bounds { get; } = new();

    public SequenceRandomNumber(params int[] results)
    {
        _results = new Queue<int>(results);
    }

    public override int Random(int value)
    {
        Bounds.Add(value);
        if (_results.Count == 0)
            throw new Exception("unexpected Random(" + value + ") call");
        return _results.Dequeue();
    }
}

sealed class RecordingPlayObject : TPlayObject
{
    public List<(ClientPacket Header, byte[] Body)> BinaryPackets { get; } = new();
    public List<(ClientPacket Header, string Text)> TextPackets { get; } = new();

    internal override void SendSocket(ClientPacket defMsg, byte[] rawBody)
    {
        BinaryPackets.Add((defMsg, rawBody?.ToArray() ?? Array.Empty<byte>()));
    }

    internal override void SendSocket(ClientPacket defMsg, string text)
    {
        TextPackets.Add((defMsg, text ?? string.Empty));
        BinaryPackets.Add((defMsg, Array.Empty<byte>()));
    }
}
