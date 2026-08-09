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
M2Share.ObjectManager = new ObjectManager();   // TBaseObject ctor calls RegisterConstructed

var actorSource = ReadRepoFile("GameSvr/Actors/TBaseObject.cs");

// E1. SetLastHiter null guard — sub_767504 @0x76750D `test esi,esi / je 0x767544` makes the
// WHOLE function a no-op on a null hitter.  Driven for real, not pattern-matched.
var animal = new AnimalObject();
var hitter = new AnimalObject();
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
