using GameSvr;
using SystemModule;

// Static + policy audit for the 2026-08-04 death-drop / PK-consequence pass.
//
// Every assertion below is re-based on the 战神 CONTRACT (byte-verified over
// M2Server_reunpacked_20260803, CODE 0x401000..0x7A10D0), not on the C# it guards, so a
// regression in either direction fails.  Each one was mutation-checked: the fix was
// reverted and the named assertion observed to FAIL (results in
// staging/deathpk_fix_20260804.md).
//
// Contracts asserted:
//
//  A. THumanKind.Die = sub_741368 @0x7413F6-0x741496 — the death-drop POLICY ladder.
//     Ownership: Die is VMT slot +0x84; THumanKind VMT@0x73BC34 holds sub_741368, and an
//     exhaustive E8 rel32 caller sweep finds exactly TWO callers (0x6C07D8 in TPlayer.Die
//     sub_6C07A0, 0x687125 in THeroAct.Die sub_686E10) => players and heroes only.
//     Monster Die is the separate sub_71E2BC, which contains no FIGHT/FIGHT3/safe-zone
//     test at all.
//
//       7413F6  cmp byte [ebx+0x5D],0 / jne 0x74140E   ; FIGHT      -> arbitration
//       7413FC  cmp byte [ebx+0x5E],0 / jne 0x74140E   ; FIGHT3     -> arbitration
//       741405  call sub_76858C / je 0x74142C          ; InSafeZone -> arbitration
//       741417  cmp byte [+0x76],0 / jne 0x74142C      ; ONLYDROPSPEC re-enables
//       741426  cmp byte [+0x77],0 / je  0x741485      ; neither    -> [vmt+0x21C] empty
//       741435  cmp byte [+0x8C],0 / jne 0x741470      ; sky tri-state -> empty
//       74143E  ONLYDROPSPEC     -> sub_740300 (exclusive)
//       74144E  LIMITBAGITEMDROP -> sub_748D48 (exclusive)
//       74145E  sub_73FC70 (equip, [+0x4C0]) then 0x741466 sub_740078 (bag, [+0x508])
//
//     The empty leaf resolves per class: THumanKind/THeroAct [+0x21C] = sub_741620
//     (55 8B EC 5D C2 08 00 — push ebp; mov ebp,esp; pop ebp; ret 8), while
//     TPlayer/TGdMsgGMAgent [+0x21C] = sub_6EB8CC, a thunk that forwards both stack args
//     and tail-calls sub_741620 @0x6EB8D8.  Either way: no drop.
//
//  B. Map-flag offsets from the parser sub_774D98 (token -> mov byte [ebx+d],v):
//     SAFE +0x5C, FIGHT +0x5D, FIGHT3 +0x5E, ONLYDROPSPEC +0x76 (@0x775ADC),
//     LIMITBAGITEMDROP +0x77 (@0x775B10), and the +0x8C tri-state OLDSKY=1 (@0x774FCE),
//     NEWSKY=2 (@0x775003), MULSKY=3 (@0x775033).
//
//  C. The luck penalty is a SIBLING of the drop policy, not nested in it —
//     sub_6C07A0 @0x6C07EE-0x6C0815 gates AddBodyLuck on FIGHT/FIGHT3 ONLY, with no
//     safe-zone term, and it runs after sub_741368 has already returned.
//
//  D. Archer-guard red-name reset — sub_6C07A0 @0x6C0891-0x6C08B9:
//       6C089E  cmp byte [LastHiter+0x178],0x70   ; race 112 = RC_ARCHERGUARD
//       6C08AA  cmp dword [self+0x160],0xC8       ; MyPKpoint >= 200 (IMMEDIATE, not the
//                                                 ;   configurable [[0x7D5FAC]] global)
//       6C08B9  mov dword [self+0x160],0x64       ; := 100, an assignment not a subtract
//     It sits AFTER the FIGHT/FIGHT3 gate closes at 0x6C0891, so no map-flag gating.

var failures = new List<string>();
void Check(bool cond, string msg)
{
    if (cond) { Console.WriteLine("  PASS  " + msg); return; }
    failures.Add(msg);
    Console.WriteLine("  FAIL  " + msg);
}

Console.WriteLine("== A/B: sub_741368 policy ladder over the native map-flag fields ==");

// --- the ordinary case: no flags, not safe => the normal equip-then-bag pair (0x74145E) ---
Check(Resolve(new TMapFlag(), inSafeZone: false)
        == "NormalEquipThenBag",
    "0x74140C je 0x74142C: plain map, not in a safe zone => normal equip+bag pair");

// --- item 1, the headline: safe zone routes to arbitration whose default leaf is empty ---
Check(Resolve(new TMapFlag(), inSafeZone: true) == "DropNothing",
    "0x741405 sub_76858C true + 0x74142A je 0x741485: SAFE-ZONE death drops NOTHING");

// --- the two legs that were already faithful, re-asserted so they cannot regress ---
Check(Resolve(new TMapFlag { boFightZone = true }, inSafeZone: false) == "DropNothing",
    "0x7413FA jne 0x74140E: FIGHT map with no special flag => empty leaf");
Check(Resolve(new TMapFlag { boFight3Zone = true }, inSafeZone: false) == "DropNothing",
    "0x741400 jne 0x74140E: FIGHT3 map with no special flag => empty leaf");

// --- ONLYDROPSPEC / LIMITBAGITEMDROP re-enable dropping on a suppressed map (0x741417/0x741426) ---
Check(Resolve(new TMapFlag { boONLYDROPSPEC = true }, inSafeZone: true)
        == "OnlyDropSpecWorker",
    "0x74141B jne 0x74142C: ONLYDROPSPEC re-enables the drop inside a safe zone");
Check(Resolve(new TMapFlag { boLIMITBAGITEMDROP = true }, inSafeZone: true)
        == "LimitBagItemDropWorker",
    "0x74142A fallthrough: LIMITBAGITEMDROP re-enables the drop inside a safe zone");
Check(Resolve(new TMapFlag { boONLYDROPSPEC = true }, inSafeZone: false)
        == "OnlyDropSpecWorker",
    "0x74143E jne: ONLYDROPSPEC selects sub_740300 EXCLUSIVELY, not the normal pair");
Check(Resolve(new TMapFlag { boLIMITBAGITEMDROP = true }, inSafeZone: false)
        == "LimitBagItemDropWorker",
    "0x74144E jne: LIMITBAGITEMDROP selects sub_748D48 EXCLUSIVELY");
// ONLYDROPSPEC is tested FIRST at both 0x741417 and 0x74143E, so it wins a tie.
Check(Resolve(new TMapFlag { boONLYDROPSPEC = true, boLIMITBAGITEMDROP = true },
        inSafeZone: false) == "OnlyDropSpecWorker",
    "0x74143E precedes 0x74144E: ONLYDROPSPEC wins when both flags are set");

// --- the sky tri-state suppresses the drop for ALL THREE values (0x741435 is `!= 0`) ---
foreach (var scene in new byte[] { 1, 2, 3 })
{
    Check(Resolve(new TMapFlag { SceneType = scene }, inSafeZone: false) == "DropNothing",
        $"0x741435 cmp byte [+0x8C],0 / jne 0x741470: SceneType={scene} suppresses the drop");
}
// ...and it outranks the special flags, because 0x741435 is tested before 0x74143E.
Check(Resolve(new TMapFlag { SceneType = 1, boONLYDROPSPEC = true }, inSafeZone: false)
        == "DropNothing",
    "0x74143C jne 0x741470 precedes 0x74143E: sky outranks ONLYDROPSPEC");

Console.WriteLine("== B: the two map-flag tokens the parser sets ==");

Check(ParseToken("ONLYDROPSPEC")?.boONLYDROPSPEC == true,
    "sub_774D98 @0x775AC7 token / @0x775ADC mov byte [ebx+0x76],1: ONLYDROPSPEC parsed");
Check(ParseToken("LIMITBAGITEMDROP")?.boLIMITBAGITEMDROP == true,
    "sub_774D98 @0x775AFB token / @0x775B10 mov byte [ebx+0x77],1: LIMITBAGITEMDROP parsed");
// sub_4C6E94 / sub_40BD78 are case-folding comparators, and native's own literals are
// mixed-case (NoRelive, pickup, LimitItemMove), so the parse must be case-insensitive.
Check(ParseToken("onlydropspec")?.boONLYDROPSPEC == true,
    "sub_4C6E94 is case-insensitive: lower-case ONLYDROPSPEC still parses");
// The tri-state must stay a tri-state: OLDSKY=1 / NEWSKY=2 / MULSKY=3 at
// 0x774FCE / 0x775003 / 0x775033 all write the SAME byte [+0x8C].
Check(ParseToken("OLDSKY")?.SceneType == 1, "0x774FCE mov byte [ebx+0x8C],1: OLDSKY=1");
Check(ParseToken("NEWSKY")?.SceneType == 2, "0x775003 mov byte [ebx+0x8C],2: NEWSKY=2");
Check(ParseToken("MULSKY")?.SceneType == 3, "0x775033 mov byte [ebx+0x8C],3: MULSKY=3");

Console.WriteLine("== C/D: the two Die-tail branches, asserted on the real source ==");

// These two are source-shape assertions rather than behavioural ones: driving TPlayObject.Die
// in-process needs the whole engine bootstrap (UserEngine + map + client socket), which is
// what InProcEngineRunCheck is for.  Asserting the ORDER and the GATE TERMS statically still
// bites, because the defects being guarded were exactly a wrong nesting and a missing branch.
var dieSource = ReadRepoFile("GameSvr/Actors/TBaseObject.Base.cs");

// C: the luck penalty must NOT be inside the drop gate, and must not gain a safe-zone term.
// Anchor on the AddBodyLuck call and read BACKWARDS to its enclosing gate: the substring
// "!boFightZone && !boFight3Zone" also occurs at an unrelated earlier gate (the exp/level
// penalty block), so a forward IndexOf would match the wrong one.
var luckCall = dieSource.IndexOf("AddBodyLuck(1);", StringComparison.Ordinal);
Check(luckCall > 0, "0x6C0815: AddBodyLuck(1) present in Die");
var luckPrefix = luckCall > 0 ? dieSource.Substring(0, luckCall) : "";
var luckGate = luckPrefix.LastIndexOf(
    "&& !m_PEnvir.Flag.boFightZone && !m_PEnvir.Flag.boFight3Zone)", StringComparison.Ordinal);
Check(luckGate > 0 && luckCall - luckGate < 400,
    "0x6C07F4/0x6C07FA: AddBodyLuck(1) sits directly under a FIGHT/FIGHT3-only gate");
// The gate immediately above AddBodyLuck must be the player-race one, and must NOT be the
// drop gate (which is what it was nested in before this pass).
Check(luckGate > 0 && luckPrefix.LastIndexOf("m_btRaceServer == Grobal2.RC_PLAYOBJECT",
        StringComparison.Ordinal) < luckGate
      && luckCall - luckPrefix.LastIndexOf("m_btRaceServer == Grobal2.RC_PLAYOBJECT",
        StringComparison.Ordinal) < 400,
    "0x6C07EE: that gate is the player-race + FIGHT/FIGHT3 pair, not the drop gate");
// Read the luck gate's OWN condition text (from its `if (` back-anchor to the `)` that
// `luckGate` points at) and assert it mentions ONLY the two native terms.  Native's gate at
// 0x6C07F4/0x6C07FA reads exactly two map bytes, +0x5D and +0x5E; any drop-policy term here
// would silently cancel the luck penalty for safe-zone/OLDSKY/special-flag deaths, which is
// the precise regression the hoist out of the drop gate was made to prevent.
var gateOpen = luckGate > 0
    ? luckPrefix.LastIndexOf("if (", luckGate, StringComparison.Ordinal)
    : -1;
var luckGateExpr = gateOpen > 0
    ? luckPrefix.Substring(gateOpen, luckGate - gateOpen)
    : "";
Check(luckGateExpr.Length > 0
      && !luckGateExpr.Contains("deathDrop", StringComparison.Ordinal)
      && !luckGateExpr.Contains("InSafeZone", StringComparison.Ordinal)
      && !luckGateExpr.Contains("SceneType", StringComparison.Ordinal)
      && !luckGateExpr.Contains("ONLYDROPSPEC", StringComparison.Ordinal)
      && !luckGateExpr.Contains("LIMITBAGITEMDROP", StringComparison.Ordinal),
    "0x6C07EE-0x6C07FE: the luck gate carries NO drop-policy term "
    + "(it is a sibling of sub_741368, not nested in it)");
// If a future edit re-nests it under the drop policy, this catches it: the drop-policy
// resolve must appear BEFORE the luck gate and must not enclose it.
var policyResolve = dieSource.IndexOf("NativeDeathDropPolicy.Resolve(", StringComparison.Ordinal);
Check(policyResolve > 0 && policyResolve < luckGate,
    "0x6C07D8 precedes 0x6C07EE: sub_741368 runs before the luck/glory gate");
// The drop gate closes before the luck gate opens: no `deathDropsAnything` between them.
var betweenGates = policyResolve > 0 && luckGate > policyResolve
    ? dieSource.Substring(policyResolve, luckGate - policyResolve)
    : "";
Check(betweenGates.Contains("deathDropsAnything", StringComparison.Ordinal),
    "0x74142C-0x741496 drop dispatch is fully between the resolve and the luck gate");
Check(!dieSource.Contains("InSafeZone() && !m_PEnvir.Flag.boFightZone", StringComparison.Ordinal),
    "0x6C07EE-0x6C0815 has NO safe-zone term: dying in town still costs luck");

// D: the archer-guard reset must exist, use race 112, threshold 200 and assign 100.
Check(dieSource.Contains("m_LastHiter.m_btRaceServer == Grobal2.RC_ARCHERGUARD", StringComparison.Ordinal),
    "0x6C089E cmp byte [LastHiter+0x178],0x70: guard-kill branch keyed on RC_ARCHERGUARD");
Check(dieSource.Contains("&& m_nPkPoint >= 200", StringComparison.Ordinal),
    "0x6C08AA cmp dword [self+0x160],0xC8: hardcoded 200, not the [[0x7D5FAC]] global");
Check(dieSource.Contains("m_nPkPoint = 100;", StringComparison.Ordinal),
    "0x6C08B9 mov dword [self+0x160],0x64: ASSIGNMENT to 100, not a subtraction");
// 0x6C08B9 is a raw field store, so no name-colour packet may be sent here.
var guardIdx = dieSource.IndexOf("m_nPkPoint = 100;", StringComparison.Ordinal);
var guardWindow = guardIdx > 0
    ? dieSource.Substring(guardIdx, Math.Min(200, dieSource.Length - guardIdx))
    : "";
Check(!guardWindow.Contains("RefNameColor", StringComparison.Ordinal)
      && !guardWindow.Contains("DecPKPoint", StringComparison.Ordinal),
    "0x6C08B9 is a raw store: native sends NO 10046 refresh from the guard branch");

Console.WriteLine("== E: items 7-10 — behavioural, incl. two anti-regression locks ==");

// Constructing any TBaseObject triggers M2Share's static ctor, which loads config files.
// Same minimal bootstrap the sibling harnesses use (no GameApp.Initialize, no DBSvr, no
// network, no MySQL, no background threads).
PrepareConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.RandomNumber = RandomNumber.GetInstance();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();   // TBaseObject ctor calls RegisterConstructed

var actorSource = ReadRepoFile("GameSvr/Actors/TBaseObject.cs");

// E1. SetLastHiter null guard — sub_767504 @0x76750D `test esi,esi / je 0x767544` makes the
// WHOLE function a no-op on a null hitter.  Driven for real, not pattern-matched.
var animal = new AnimalObject();
var hitter = new AnimalObject();

// E0. The native 0x7623B0 extension arm feeds agg1+0x5E only for ident 201,
// while the four 0x76231B/0x762372/0x762B26/0x762B6A arms set agg2+0x25 for
// idents 128/138.  Both scans admit only positive-durability equipped items.
// GoodItem is the already-published six-slot 2.08 StdItem extension surface;
// this check deliberately does not claim to implement the other extension arms.
M2Share.UserEngine.StdItemList.Clear();
var aggregateItem = new GoodItem { Name = "drop-aggregate" };
aggregateItem.NativeItemExtAbilIdents[0] = 0x01C9;
aggregateItem.NativeItemExtAbilValues[0] = 0x013D;
aggregateItem.NativeItemExtAbilIdents[1] = 0x0180;
aggregateItem.NativeItemExtAbilIdents[2] = 0x01AA;
aggregateItem.NativeItemExtAbilValues[2] = 0x0107;
var secondAggregateItem = new GoodItem { Name = "drop-aggregate-2" };
secondAggregateItem.NativeItemExtAbilIdents[0] = 0x01C9;
secondAggregateItem.NativeItemExtAbilValues[0] = 0x0131;
secondAggregateItem.NativeItemExtAbilIdents[1] = 0x01AE;
secondAggregateItem.NativeItemExtAbilValues[1] = 0x0109;
var brokenAggregateItem = new GoodItem { Name = "drop-aggregate-broken" };
brokenAggregateItem.NativeItemExtAbilIdents[0] = 201;
brokenAggregateItem.NativeItemExtAbilValues[0] = 999;
var unparsedAggregateItem = new GoodItem { Name = "drop-aggregate-unparsed",
    NativeItemExtAbilParsed = false };
unparsedAggregateItem.NativeItemExtAbilIdents[0] = 201;
unparsedAggregateItem.NativeItemExtAbilValues[0] = 999;
unparsedAggregateItem.NativeItemExtAbilIdents[1] = 170;
unparsedAggregateItem.NativeItemExtAbilValues[1] = 999;
M2Share.UserEngine.StdItemList.Add(aggregateItem);
M2Share.UserEngine.StdItemList.Add(secondAggregateItem);
M2Share.UserEngine.StdItemList.Add(brokenAggregateItem);
M2Share.UserEngine.StdItemList.Add(unparsedAggregateItem);
animal.m_UseItems[0] = new TUserItem { wIndex = 1, Dura = 100 };
animal.m_UseItems[1] = new TUserItem { wIndex = 2, Dura = 100 };
animal.m_UseItems[2] = new TUserItem { wIndex = 3, Dura = 0 };
animal.m_UseItems[3] = new TUserItem { wIndex = 4, Dura = 100 };
var aggregateMethod = typeof(TBaseObject).GetMethod(
    "NativeEquipDropRareAggregate",
    System.Reflection.BindingFlags.Instance |
    System.Reflection.BindingFlags.NonPublic)
    ?? throw new MissingMethodException("NativeEquipDropRareAggregate");
var gateMethod = typeof(TBaseObject).GetMethod(
    "NativeDropRareKillerBonusGate",
    System.Reflection.BindingFlags.Instance |
    System.Reflection.BindingFlags.NonPublic)
    ?? throw new MissingMethodException("NativeDropRareKillerBonusGate");
var recalcMethod = typeof(TBaseObject).GetMethod(
    "NativeRecalcDropRareFields",
    System.Reflection.BindingFlags.Instance |
    System.Reflection.BindingFlags.NonPublic)
    ?? throw new MissingMethodException("NativeRecalcDropRareFields");
var physicalAggregateMethod = typeof(TBaseObject).GetMethod(
    "NativeEquipPhysicalReductionAggregate",
    System.Reflection.BindingFlags.Instance |
    System.Reflection.BindingFlags.NonPublic)
    ?? throw new MissingMethodException("NativeEquipPhysicalReductionAggregate");
var physicalRecalcMethod = typeof(TBaseObject).GetMethod(
    "NativeRecalcPhysicalReductionPercent",
    System.Reflection.BindingFlags.Instance |
    System.Reflection.BindingFlags.NonPublic)
    ?? throw new MissingMethodException("NativeRecalcPhysicalReductionPercent");
Check((int)aggregateMethod.Invoke(animal, null)! == 110,
    "0x7620DA/0x7623B2: ident and value both use their low byte; 201 contributes 61+49 and broken/unparsed items are ignored");
Check((bool)gateMethod.Invoke(animal, null)!,
    "0x7620DA/0x76231B: equipped ident low byte 128 sets agg2+0x25 gate");
Check((int)physicalAggregateMethod.Invoke(animal, null)! == 16,
    "0x7620DA/0x7623A8: ident low bytes 170 and 174 contribute value low bytes 7+9; unparsed definitions are ignored");
physicalRecalcMethod.Invoke(animal, null);
Check(animal.m_wNativePhysicalDamageReductionPercent == 36,
    "0x73DEA8/0x73DEC7: physical reduction receives low-word aggregate 16 plus gate 20");
animal.m_WAbil.HP = 2000;
animal.m_WAbil.MaxHP = 2000;
animal.m_WAbil.MP = 0;
Check(animal.ApplyNativePhysicalLandingDamage(1000) == 640 &&
      animal.m_WAbil.HP == 1360,
    "0x73F903..0x73F92C: live 36-percent aggregate reduces 1000 physical landing damage to 640");
recalcMethod.Invoke(animal, null);
Check(animal.m_nNativeDropRareBase == 11,
    "0x73DAC5/0x73DAC1: aggregate 110 uses unsigned WORD / 10 => [+0x18C]=11");
Check(animal.m_btNativeDropRareKillerBonus == 10,
    "0x73DEBE/0x73DECF: agg2+0x25 gate writes killer bonus 10");
animal.m_UseItems[0].Dura = 0;
animal.m_UseItems[1].Dura = 0;
animal.m_UseItems[3].Dura = 0;
recalcMethod.Invoke(animal, null);
Check(animal.m_nNativeDropRareBase == 0 &&
      animal.m_btNativeDropRareKillerBonus == 0,
    "sub_75EE78 positive-durability gate: no live equipped item clears both derived fields");
physicalRecalcMethod.Invoke(animal, null);
Check(animal.m_wNativePhysicalDamageReductionPercent == 0,
    "physical reduction positive-durability gate: no live equipped item clears the derived field");

animal.SetLastHiter(hitter);
Check(ReferenceEquals(animal.m_LastHiter, hitter) && ReferenceEquals(animal.m_ExpHitter, hitter),
    "0x767511/0x76752C: a real hitter sets m_LastHiter AND seeds m_ExpHitter");
var tickBefore = animal.m_LastHiterTick;
animal.SetLastHiter(null);
Check(ReferenceEquals(animal.m_LastHiter, hitter),
    "0x76750F je 0x767544: SetLastHiter(null) must NOT clear m_LastHiter "
    + "(else the victim dies unattributed: no PK point, no drop credit)");
Check(ReferenceEquals(animal.m_ExpHitter, hitter),
    "0x76750F: SetLastHiter(null) must NOT clear m_ExpHitter");
Check(animal.m_LastHiterTick == tickBefore,
    "0x76750F: SetLastHiter(null) must NOT stamp m_LastHiterTick");
// The sticky rule itself (0x767522-0x76753E): a SECOND, different hitter re-points
// m_LastHiter but must leave m_ExpHitter on the FIRST hitter.
var second = new AnimalObject();
animal.SetLastHiter(second);
Check(ReferenceEquals(animal.m_LastHiter, second) && ReferenceEquals(animal.m_ExpHitter, hitter),
    "0x767528 jne 0x76753A: m_ExpHitter is sticky-first-hit, m_LastHiter is last-hit");

// E2. DecPKPoint refresh set — ANTI-REGRESSION LOCK.  sub_6CCB0C @0x6CCB4E-0x6CCB52 is the
// Delphi `x in [1..2]` idiom (`dec` does NOT touch CF; `sub ebx,2` sets CF = borrow; `jae`
// skips).  Native refreshes for oldLevel in {1,2} ONLY — NOT 0, because dec makes 0 into
// 0xFFFFFFFF which is not below 2 unsigned.  The spec for this pass asked to drop C#'s
// `nC > 0` term, which would send a 10046 packet native never sends.  This assertion exists
// to make that "fix" fail loudly.
Check(actorSource.Contains("(PKLevel() != nC) && (nC > 0) && (nC <= 2)", StringComparison.Ordinal),
    "0x6CCB4E dec/0x6CCB4F sub 2/0x6CCB52 jae => refresh set {1,2}: the `nC > 0` term is "
    + "REQUIRED (oldLevel 0 must NOT refresh) — do not 'fix' this");

// E3. AddBodyLuck clamps — sub_7698BC @0x7698E3 (5) and @0x7698F4 (-0xA).  The 500-unit
// divisor is NOT modelled in C# on purpose: all five native callers pass exact multiples of
// 500, so whole-level arithmetic is identical.  Assert the clamps, which are the part that
// is genuinely shared.
var luckActor = new AnimalObject();
for (var i = 0; i < 40; i++) AddBodyLuck(luckActor, 1);
Check(luckActor.m_nBodyLuckLevel == 5,
    "0x7698E3 cmp eax,5 / mov [ebx+0x164],5: LuckNum clamps at +5");
for (var i = 0; i < 80; i++) AddBodyLuck(luckActor, -1);
Check(luckActor.m_nBodyLuckLevel == -10,
    "0x7698F4 cmp eax,-0xA / mov [ebx+0x164],0xFFFFFFF6: LuckNum clamps at -10");

// E4. Instant-kill (spec item 7) is NOT missing — it is 圣言术 / SKILL_KILLUNDEAD.
// sub_76F8D0's victim must be class TAnimal ([0x71D4D0] -> VMT 0x71D51C name='TAnimal' via
// the Delphi `is` helper sub_404828), it has exactly ONE caller (0x6EDCF3), and that caller
// is switch case 32 of the jump table at 0x6ED706 — SpellsDef.SKILL_KILLUNDEAD == 32.
Check(SpellsDef.SKILL_KILLUNDEAD == 32,
    "jmp [eax*4+0x6ED706] case 32 -> 0x6EDCEC -> sub_76F8D0: the instant-kill is skill 32");
var magicSource = ReadRepoFile("GameSvr/Spells/MagicManager.cs");
Check(magicSource.Contains("case SpellsDef.SKILL_KILLUNDEAD:", StringComparison.Ordinal),
    "0x6EDCEC: skill 32 has a C# handler (spec claim 'missing instant-kill' is wrong)");
Check(magicSource.Contains("TargeTBaseObject.SetLastHiter(BaseObject);", StringComparison.Ordinal)
      && magicSource.Contains("TargeTBaseObject.m_WAbil.HP = 0;", StringComparison.Ordinal),
    "0x76F9AC call sub_767504 then 0x76F9B3 mov [ebx+0x2AC],0: "
    + "SetLastHiter precedes HP:=0 so the kill is attributed");

Console.WriteLine("== F: PKD 2026-08-13 — 爆装抽签序列 / 善恶值门 / 死亡链顺序 ==");

// Every F-assertion cites a 战神 EA read off flat_image.bin (ImageBase 0x400000) this pass.
// Where the contract is an ORDER (which draw happens first) the assertion is a source-shape
// one, because driving TPlayObject.Die end-to-end needs the full engine bootstrap that
// InProcEngineRunCheck owns.  Order defects are exactly what this section guards.

var equipSource = ReadRepoFile("GameSvr/Players/TPlayObject.Message.cs");
var bagSource = ReadRepoFile("GameSvr/Players/TPlayObject.Base.cs");

// F1. sub_73FC70 @0x73FCB0 `3B 86 60 01 00 00 cmp eax,[esi+0x160]` / 0x73FCB6 `7D 09 jge`
//     — the red branch (denominator 0x15) is taken ONLY when threshold < PK, i.e. a STRICT
//     PK > 200.  PKLevel() > 2 is PK >= 300 and mis-classifies the whole 201..299 band.
Check(equipSource.Contains("m_nPkPoint > M2Share.g_Config.nPKPunishPoint", StringComparison.Ordinal),
    "0x73FCB6 jge: 装备爆率的红名判据是严格 m_nPkPoint > 200");
Check(!equipSource.Contains("PKLevel() > 2", StringComparison.Ordinal),
    "0x73FCB6: PKLevel() > 2 (== PK>=300) 不是原生判据，必须已被移除");
// F1b. ANTI-REGRESSION: the BAG worker sub_740078 @0x7400BE uses `0F 9E setle`, i.e.
//      threshold <= PK => PK >= 200.  Native is deliberately inconsistent between the two
//      workers; do NOT "harmonise" them.
Check(bagSource.Contains("m_nPkPoint >= M2Share.g_Config.nPKPunishPoint", StringComparison.Ordinal)
      && !bagSource.Contains("boDieRedScatterBagAll && PKLevel()", StringComparison.Ordinal),
    "0x7400BE setle: 背包爆率直接使用 PK >= 200，且不引入额外配置/等级判据");

// F2. sub_73FC70 @0x73FF69 `83 7D F4 02 cmp [ebp-0xC],2` / 0x73FF6D `7F 0A jg` — the
//     3-item ground-drop cap is UNCONDITIONAL native code; the 眼神 patch only rewrites the
//     imm8 (0x100B9D3A A2 6C FF 73 00 -> imm8 of 0x73FF69).
Check(equipSource.Contains("nativeDropCap", StringComparison.Ordinal)
      && equipSource.Contains("dropCount > nativeDropCap", StringComparison.Ordinal),
    "0x73FF69/0x73FF6D: 落地件数上限恒定生效，不依赖眼神补丁");
Check(!equipSource.Contains("if (deathDropPatched) dropCount++;", StringComparison.Ordinal),
    "0x73FD74 inc [ebp-0xC]: Reserved&8 支同样占用件数预算，计数不得依赖补丁开关");

// F3. RNG ORDER.  sub_73FC70 fetches the slot first (0x73FD33 call sub_75EC20) and bails on
//     an empty slot at 0x73FD3C `0F 84 2D 02 00 00 je 0x73FF6F` — BEFORE the draw at
//     0x73FD99 `call sub_403B4C`.  Drawing for empty slots desynchronises the whole LCG.
var candidateRead = equipSource.IndexOf("var candidate = m_UseItems[i];",
    StringComparison.Ordinal);
var nullGuard = candidateRead >= 0
    ? equipSource.IndexOf("if (candidate == null)", candidateRead,
        StringComparison.Ordinal)
    : -1;
var equipDraw = equipSource.IndexOf("M2Share.RandomNumber.Random(nRate)", StringComparison.Ordinal);
Check(candidateRead >= 0 && nullGuard > candidateRead && equipDraw > nullGuard,
    "0x73FD3C je 先于 0x73FD99 call sub_403B4C: 空装备格不消耗抽签");

// F4/F5. sub_740078 per-item order: 0x7400F8 Random(3) -> 0x740111 / 0x74011E / 0x74013A
//        three StdItem gates -> 0x740140 destroy-vs-drop split.  C# had the destroy branch
//        FIRST and ungated, so 未验证/赠品 were destroyed 100% instead of 1/3 and bound /
//        undroppable items were destroyed too.
var bagDraw = bagSource.IndexOf("M2Share.RandomNumber.Random(3)",
    StringComparison.Ordinal);
var bagDestroy = bagSource.IndexOf("NativeItemDropDestroy.ShouldDestroy(", StringComparison.Ordinal);
Check(bagDraw > 0 && bagDestroy > 0 && bagDraw < bagDestroy,
    "0x7400F8 Random(3) 先于 0x740140 销毁/落地分流：销毁支同样要过 1/3 抽签");
Check(bagSource.Contains("NativeReserved02 & 0x0010", StringComparison.Ordinal),
    "0x74010D test byte [std+2],0x10 / jne 0x74025C: 背包该位置位则整件不动");
Check(bagSource.Contains("NativeReserved02 & 0x0200", StringComparison.Ordinal),
    "0x74011A test byte [std+3],2 / jne 0x74025C");
Check(bagSource.Contains("NativeReserved02 & 0x4000", StringComparison.Ordinal)
      && bagSource.Contains("NativeItemAcquisitionStamp.ReadBindWord", StringComparison.Ordinal),
    "0x740126 sub_784720 + 0x740131 sub_784710 cmp ax,1: 已绑定物品不掉不销毁");

// F6. TPlayer.Die sub_6C07A0 murder gate.  0x6C0830 tests FREEPK ([Envir+0x5F]) alongside
//     FIGHT/FIGHT3, and 0x6C0863 `3B 02 / 7F 2A jg` proceeds while victimPK <= 200.
Check(dieSource.Contains("!m_PEnvir.Flag.boFREEPK", StringComparison.Ordinal),
    "0x6C0830 cmp byte [Envir+0x5F],0 / jne: FREEPK 地图不计谋杀");
Check(dieSource.Contains("m_nPkPoint <= M2Share.g_Config.nPKPunishPoint", StringComparison.Ordinal),
    "0x6C0863 jg: 受害者 PK <= 200 才惩罚凶手（PK 恰为 200 仍然惩罚）");
Check(!dieSource.Contains("m_LastHiter != null && PKLevel() < 2", StringComparison.Ordinal),
    "0x6C0863: PKLevel() < 2 (== PK<200) 在 PK==200 处少判一次，必须已被替换");

// F7. 0x6C0875 `cmp byte [eax+0x73],0 / jne` + 0x6C087B `cmp eax,[ebp-4] / je`.
//     +0x73 is m_boGhost (single image-wide writer 0x7680EF inside MakeGhost); +0x74 is
//     m_boDeath (0x766323, first statement of TCreature.Die).  Using m_boDeath here would
//     disable the murder penalty for every kill, since the victim is always dead by now.
Check(dieSource.Contains("!m_LastHiter.m_boGhost", StringComparison.Ordinal),
    "0x6C0875 cmp byte [killer+0x73],0: 幽灵凶手不吃谋杀惩罚（+0x73 = m_boGhost）");
Check(dieSource.Contains("!ReferenceEquals(m_LastHiter, this)", StringComparison.Ordinal),
    "0x6C087B cmp eax,[ebp-4] / je: 自杀不算 PK");
Check(!dieSource.Contains("!m_LastHiter.m_boDeath", StringComparison.Ordinal),
    "反回归: 0x6C0875 读的是 +0x73(m_boGhost)，不是 +0x74(m_boDeath)");

// F8. sub_6C0FE4 @0x6C1019 `mov edx,0xFFFFFE0C / call sub_7698BC` runs BEFORE
//     0x6C1025 `mov byte [ebp-2],0` (guildwarkill := 0) and before every branch below it.
var luckMinus = dieSource.IndexOf("m_LastHiter.AddBodyLuck(-1);", StringComparison.Ordinal);
var guildWarInit = dieSource.IndexOf("var guildwarkill = false;", StringComparison.Ordinal);
var goodKilling = dieSource.IndexOf("m_LastHiter.IsGoodKilling(this)", StringComparison.Ordinal);
Check(luckMinus > 0 && guildWarInit > 0 && luckMinus < guildWarInit,
    "0x6C1020 call sub_7698BC(-500) 先于 0x6C1025 guildwarkill 初始化");
Check(luckMinus > 0 && goodKilling > 0 && luckMinus < goodKilling,
    "0x6C1019: 行会战/攻城战/正当防卫下原生同样扣 1 点幸运");

// F9. 0x6C0936 `mov ecx,0x6C09FC`; the Delphi length dword at 0x6C09F8 is 5 => '#####'.
Check(dieSource.Contains("tStr = \"#####\";", StringComparison.Ordinal),
    "0x6C09FC Delphi 长串长度前缀 = 5: 无凶手占位符是五个 '#'");
Check(!dieSource.Contains("tStr = \"####\";", StringComparison.Ordinal),
    "0x6C09FC: 四个 '#' 会让 19 号日志定宽切分整行错位");

// F10. PK 值衰减.  TPlayer tick @0x6B3705-0x6B3733:
//        6B3705  2B 90 34 07 00 00  sub edx,[self+0x734]
//        6B370B  81 FA C0 D4 01 00  cmp edx,0x1D4C0      ; 120000 ms
//        6B3711  76 25              jbe 0x6B3738         ; <=120000 -> 不衰减（严格 >）
//        6B3719  mov [self+0x734],now                    ; 无论 PK 是否 >0 都刷新时间戳
//        6B3722  83 B8 60 01 00 00 00 / 7E 0D  cmp [self+0x160],0 / jle
//        6B372B  BA 01 00 00 00     mov edx,1            ; DecPKPoint(1)
var cfg = M2Share.g_Config;          // 已由 E 段的 PrepareConfig() 引导
Check(cfg.dwDecPkPointTime == 120000,
    "0x6B370B cmp edx,0x1D4C0: 善恶值衰减周期 = 120000 ms");
Check(cfg.nDecPkPointCount == 1,
    "0x6B372B mov edx,1: 每次衰减 1 点");
Check(dieSource.Contains("(HUtil32.GetTickCount() - m_dwDecPkPointTick) > M2Share.g_Config.dwDecPkPointTime",
        StringComparison.Ordinal),
    "0x6B3711 jbe: 严格大于才衰减，写成 >= 会多衰减一 tick");

// F11. The three PK globals, read out of the image at their initialised addresses.
Check(cfg.nPKPunishPoint == 200,
    "[0x7D5FAC] -> 0x7DCF00 = 0xC8: PK 阈值 200");
Check(cfg.nKillHumanAddPKPoint == 100,
    "[0x7D5AE8] -> 0x7DCF04 = 0x64: 0x6C10FE IncPkPoint 每次 +100");
// F12. sub_740078 @0x7400F8 `mov eax,3` is a HARDCODED 3. There is no config knob:
// "DieScatterBagRate" is 0-hit across GBK / ASCII / UTF-16LE in the image, so the
// faithful C# hardcodes Random(3) and must NOT re-introduce a config field for it.
// The negative half has to look at CODE only: the line right above the draw is a comment
// that names the removed knob, and matching that comment made this闸门 report a false red.
var bagCode = string.Join("\n", bagSource.Split('\n').Select(line =>
{
    var slashes = line.IndexOf("//", StringComparison.Ordinal);
    return slashes >= 0 ? line[..slashes] : line;
}));
Check(bagCode.Contains("M2Share.RandomNumber.Random(3)", StringComparison.Ordinal)
      && !bagCode.Contains("nDieScatterBagRate", StringComparison.Ordinal),
    "0x7400F8 mov eax,3: 背包爆率分母硬编码 3，不得引入配置旋钮");

Console.WriteLine();
if (failures.Count > 0)
{
    Console.WriteLine($"DeathDropPolicyCheck: FAILED ({failures.Count})");
    foreach (var f in failures) Console.WriteLine("  - " + f);
    Environment.Exit(1);
}
Console.WriteLine("DeathDropPolicyCheck: PASS");
Environment.Exit(0);

// ---- helpers -------------------------------------------------------------------------

// NativeDeathDropPolicy is internal to GameSvr; reach it the way the sibling audits do.
static string Resolve(TMapFlag flag, bool inSafeZone)
{
    var t = typeof(TPlayObject).Assembly.GetType("GameSvr.NativeDeathDropPolicy", true);
    var m = t.GetMethod("Resolve",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("NativeDeathDropPolicy.Resolve");
    return m.Invoke(null, new object[] { flag, inSafeZone })!.ToString()!;
}

static TMapFlag ParseToken(string token)
{
    var flag = new TMapFlag();
    var t = typeof(TPlayObject).Assembly.GetType("GameSvr.Maps", true);
    var m = t.GetMethod("TryApplySceneFlag",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Public)
        ?? throw new MissingMethodException("Maps.TryApplySceneFlag");
    var ok = (bool)m.Invoke(null, new object[] { flag, token })!;
    return ok ? flag : null;
}

// Minimal config bootstrap so M2Share's static ctor can run (idiom copied from
// AuditTools/InProcItemConservationCheck).  Writes only into the audit's own bin directory.
static void PrepareConfig()
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

// AddBodyLuck is `protected` on TBaseObject.  Reach it by reflection rather than adding a
// test-only hook to production code (the sibling harnesses use the same idiom).
static void AddBodyLuck(TBaseObject actor, int n)
{
    var m = typeof(TBaseObject).GetMethod("AddBodyLuck",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
        null, new[] { typeof(int) }, null)
        ?? throw new MissingMethodException("TBaseObject.AddBodyLuck");
    m.Invoke(actor, new object[] { n });
}

static string ReadRepoFile(string relative)
{
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 12 && dir != null; i++)
    {
        var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(candidate)) return File.ReadAllText(candidate);
        dir = Path.GetDirectoryName(dir);
    }
    throw new FileNotFoundException("could not locate " + relative + " above " + AppContext.BaseDirectory);
}
