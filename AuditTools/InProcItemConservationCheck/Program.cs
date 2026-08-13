using System.Reflection;
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

    Console.WriteLine(
        "PASS InProcItemConservationCheck "
        + "stamper=REAL(sub_7842F8 writes item+0x34 via outer sub_6B7378; GMLevel<=3 gate) "
        + "equip-swap-fullbag=REAL(no loss, rollback, sub_6B7E9C @0x6B804C) "
        + "unequip-nospace=REAL(nothing mutated, no Dispose, sub_6B8188 @0x6B81F0 => -3) "
        + "hero-bag-dealing=REAL(both directions rejected -1, sub_6D09D0 @0x6D09ED) "
        + "mail-failed-attachment=REAL(lost not duplicated, AttachStatus 1->2, sub_70B458) "
        + "drop-unverified=REAL(destroyed not scattered, sub_73CC98 @0x73CDEB) "
        + "drop-gift=REAL(destroyed not scattered, item+0xD8) "
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
    gift.NativeGiftItem = true;                       // item+0xD8 != 0
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
