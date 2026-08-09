// NativeStruckDamageStageOrderCheck — pins the ORDER of the native damage
// stages inside TBaseObject.StruckDamage, and the fact that the two armour
// getters do NOT contain the super-force reduction or the charge shield.
//
// Native truth (verified 2026-08-04 by capstone reads of
// D:/loym2/staging/_reunpack_work/flat_image.bin, ImageBase 0x400000,
// backing M2Server_reunpacked_20260803.exe):
//
//   StruckDamage = VMT slot +0x0A8 and is CLASS-SPLIT:
//     sub_73F9FC = THumanKind.StruckDamage   (VMT 0x73BC34+0xA8)
//     sub_767A18 = TCreature.StruckDamage    (VMT 0x764608+0xA8)
//   Both are `ret 8` and take the ATTACKER in ecx
//     (0x73FA05 mov [ebp-4],ecx  /  0x767A1F mov edi,ecx).
//
//   Human body order (sub_73F9FC):
//     0x73FA0C  states 0x34 (52) / 0x37 (55) -> damage := 0, return
//     0x73FA30  durability roll Random(10)+5
//     0x73FA40  amplify: state 0x35 x1.3, else state 0x1E (lvl 4 x1.25 / x1.2)
//     0x73FAE0  block/parry proc [+0x4C8], Random(100) < 10, 30..70 %
//     0x73FB5A  durability worker sub_73FBE8
//     0x73FB5F  CHARGE SHIELD word [+0x3FC]  <-- stage 11 of 19
//     0x73FBA6  test esi,esi; jle -> return WITHOUT landing
//     0x73FBB7  call [+0x1AC] = the land helper
//
//   Creature body order (sub_767A18):
//     0x767A25  states 0x34 / 0x37 -> 0
//     0x767A46  one-shot execute (global [0x7D617C] + attacker [+0x3FF])
//     0x767A70  amplify: same three constants
//     0x767AE4  SUPER-FORCE sub_767CBC(eax=self, edx=dmg, cl=[attacker+0x72])
//     0x767B1D  call [+0x1AC]
//
//   Neither armour getter contains either step: sub_767958 ends
//   0x7679A9 call sub_76FFE8 / 0x7679AE pop esi; pop ebx; pop ecx; pop ebp;
//   0x7679B2 ret 4 — and its MAC twin sub_7679B8 ends identically at
//   0x767A09/0x767A0E. `_cs_field.py 3FC` lists no ref in either body, and
//   `_cs_find.py callers 767CBC` returns only 0x767AE4 and 0x76DB20.

using System.Reflection;
using GameSvr;
using SystemModule;

int failures = 0;

PrepareRuntimeConfig();
InitializeRuntime();

// ---------------------------------------------------------------- static pins
string root = FindRepositoryRoot();
string combat = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
    "TBaseObject.cs"));

// The two moved steps must NOT be in the armour getters. Slice the file at the
// StruckDamage body so "absent from the getters" is a real claim, not a
// whole-file grep that the StruckDamage occurrence would satisfy.
int struckAt = combat.IndexOf("public void StruckDamage(int nDamage, "
    + "TBaseObject attacker)", StringComparison.Ordinal);
Check(struckAt > 0, "StruckDamage(int, TBaseObject) overload is missing "
    + "(native +0x0A8 is ret 8 with ecx = the attacker)");
int gettersAt = combat.IndexOf("public ushort GetHitStruckDamage(",
    StringComparison.Ordinal);
Check(gettersAt > 0 && gettersAt < struckAt,
    "GetHitStruckDamage must precede StruckDamage in the file");
string getters = combat[gettersAt..struckAt];
string struck = combat[struckAt..];

Check(!getters.Contains("ConsumeNativeSkill153ShieldCharge",
        StringComparison.Ordinal),
    "0x7679A9/0x767A09: the armour getters tail-call sub_76FFE8 and return; "
    + "they never read word [+0x3FC]. Charge shield belongs in StruckDamage "
    + "at 0x73FB5F.");
Check(!getters.Contains("ApplyNativeMonsterSuperForceReduction",
        StringComparison.Ordinal),
    "sub_767958/sub_7679B8 never call sub_767CBC; its only callers are "
    + "0x767AE4 (TCreature.StruckDamage) and 0x76DB20 (the magic pipeline).");
Check(struck.Contains("ApplyNativeMonsterSuperForceReduction(attacker,",
        StringComparison.Ordinal),
    "0x767ADD mov cl,[edi+0x72]: super-force reads the ATTACKER ARGUMENT's "
    + "job byte, not m_LastHiter.");
Check(!struck.Contains("m_LastHiter", StringComparison.Ordinal),
    "StruckDamage must not source the attacker from m_LastHiter (+0x354): 19 "
    + "of the 23 native +0x0A8 callers never call SetLastHiter (sub_767504), "
    + "and sub_766A70 calls it at 0x766BC1 AFTER StruckDamage at 0x766BA6.");
// Match the CONFIG READ, not the bare identifier — the deleted block is named
// in an explanatory comment right at the deletion site, and a bare-name grep
// would flag that comment (it did, on first run).
Check(!struck.Contains("g_Config.nWarrMon", StringComparison.Ordinal)
        && !struck.Contains("g_Config.nWizardMon", StringComparison.Ordinal)
        && !struck.Contains("g_Config.nTaosMon", StringComparison.Ordinal)
        && !struck.Contains("g_Config.nMonHum", StringComparison.Ordinal),
    "the nWarrMon/nWizardMon/nTaosMon/nMonHum config scaling is an invention: "
    + "no function on the native damage chain reads any config global.");

// Native ORDER inside the body: amplify (0x73FA40) -> super-force (0x767AE4)
// -> block proc (0x73FAE0) -> durability worker (0x73FB5A) -> charge shield
// (0x73FB5F) -> the jle return gate (0x73FBA6) -> land (0x73FBB7).
int iAmplify = struck.IndexOf("ApplyNativeStruckAmplifyStates",
    StringComparison.Ordinal);
int iSuper = struck.IndexOf("ApplyNativeMonsterSuperForceReduction",
    StringComparison.Ordinal);
int iBlock = struck.IndexOf("TryApplyNativePhysicalBlockProc",
    StringComparison.Ordinal);
int iShield = struck.IndexOf("ConsumeNativeSkill153ShieldCharge",
    StringComparison.Ordinal);
int iLand = struck.IndexOf("DamageHealth(", StringComparison.Ordinal);
Check(iAmplify > 0 && iSuper > 0 && iBlock > 0 && iShield > 0 && iLand > 0,
    "one of the five ordered stages is missing from StruckDamage");
Check(iAmplify < iSuper,
    "0x767A70 amplify precedes 0x767AE4 super-force");
Check(iSuper < iBlock,
    "super-force must precede the block proc "
    + "(sub_767A18 has no block proc; the human 0x73FAE0 proc follows the "
    + "amplify tier that super-force is anchored to)");
Check(iBlock < iShield,
    "0x73FAE0 block proc precedes 0x73FB5F charge shield");
Check(iShield < iLand,
    "0x73FB5F charge shield precedes the 0x73FBB7 land call");

// -------------------------------------------------------------- runtime pins
// Stage 11: the charge shield reduction is trunc(HiAbility_by_job * 2.5).
// 0x76CD8C picks the ability pair from byte [+0x72]; the call site passes
// dl = 1, so 0x76CD6E returns the HIGH word deterministically (no Random).
// 0x73FB78 fmul dword [0x73FBE4] where those 4 bytes are `00002040` = 2.5
// (the following 4 bytes are `55 8B EC` = the sub_73FBE8 prologue, which
// proves the constant is 4 bytes wide, not 8).
var warrior = NewActor(M2Share.jWarr, dcHigh: 40, hp: 500);
SetShieldCharges(warrior, 2);
warrior.SetNativeActiveState(59);
warrior.StruckDamage(150);
Equal(450, warrior.m_WAbil.HP,
    "0x73FB83 sub esi,eax: 150 - trunc(40*2.5)=100 lands as 50");
Equal((ushort)1, ShieldCharges(warrior),
    "0x73FB85 dec word [ebx+0x3FC]");

// 0x73FBA6 `test esi,esi; jle 0x73FBBD` is a RETURN gate, not a clamp: when
// the shield over-absorbs, native returns the negative value and does NOT
// land. The charge is still spent (0x73FB85 dec runs before the gate).
// The victim is seeded BELOW MaxHP on purpose: DamageHealth's else-branch
// (native sub_767D14 @0x767E04, `HP = min(MaxHP, HP - dmg)`) would HEAL on a
// negative value, so a missing gate is only observable when HP < MaxHP.
var over = NewActor(M2Share.jWarr, dcHigh: 101, hp: 500);
over.m_WAbil.HP = 300;
SetShieldCharges(over, 2);
over.SetNativeActiveState(59);
over.StruckDamage(200);
Equal(300, over.m_WAbil.HP,
    "0x73FBA6 jle: an over-absorbed hit must neither land NOR heal "
    + "(without the gate, DamageHealth(-52) would raise HP)");
Equal((ushort)1, ShieldCharges(over),
    "0x73FB85 dec runs before the 0x73FBA6 gate");

// 0x73FB8C-0x73FBA4: the LAST charge clears state 0x3B (=59) and broadcasts.
var last = NewActor(M2Share.jWarr, dcHigh: 4, hp: 500);
SetShieldCharges(last, 1);
last.SetNativeActiveState(59);
Check(last.HasNativeActiveState(59), "state 59 setup");
last.StruckDamage(100);
Equal((ushort)0, ShieldCharges(last), "final charge consumed");
Check(!last.HasNativeActiveState(59),
    "0x73FB96 mov dl,0x3B; call sub_7729A8 must clear state 59 at zero");

// Stage 4 of the creature body: super-force. 0x767CBC gates:
//   0x767CCA mask == 0            -> passthrough
//   0x767CD3 percent == 0         -> passthrough
//   0x767CDE (byte)job - 4 >= 0   -> passthrough
//   0x767CEC ((1<<job) & mask)==0 -> passthrough
//   else max(0, dmg - dmg*percent/100)
var mob = NewActor(M2Share.jWarr, dcHigh: 0, hp: 1000);
mob.m_nNativeMonsterSuperForceMask = 2;              // bit 1 -> job 1 only
mob.m_nNativeMonsterSuperForceReductionPercent = 25;
var mage = new Monster { m_btJob = 1 };
var warr = new Monster { m_btJob = 0 };
var job4 = new Monster { m_btJob = 4 };

mob.StruckDamage(100, mage);
Equal(925, mob.m_WAbil.HP,
    "0x767CF6 imul [edi+0x440]: 100 - 100*25/100 = 75");

mob.m_WAbil.HP = 1000;
mob.StruckDamage(100, warr);
Equal(900, mob.m_WAbil.HP,
    "0x767CEC and edx,[edi+0x43C]: a clear mask bit passes through");

mob.m_WAbil.HP = 1000;
mob.StruckDamage(100, job4);
Equal(900, mob.m_WAbil.HP, "0x767CDE sub dl,4; jae: job >= 4 passes through");

// 0x767AD9 `test edi,edi; je 0x767B13` — a NIL attacker skips the stage.
// This is a real native shape: 0x73F3AD and 0x73F43E both pass `xor ecx,ecx`.
mob.m_WAbil.HP = 1000;
mob.StruckDamage(100, null);
Equal(900, mob.m_WAbil.HP, "0x767AD9: nil attacker skips super-force");

// And m_LastHiter must have NO influence, since native reads the argument.
mob.m_WAbil.HP = 1000;
mob.SetLastHiter(mage);
mob.StruckDamage(100, warr);
Equal(900, mob.m_WAbil.HP,
    "m_LastHiter (+0x354) must not feed the reduction; native reads ecx");

mob.m_WAbil.HP = 1000;
mob.SetLastHiter(warr);
mob.StruckDamage(100, mage);
Equal(925, mob.m_WAbil.HP,
    "a mismatched m_LastHiter must not suppress the real attacker");

// The armour getters must pass damage through untouched (AC/MAC zeroed).
var plain = NewActor(M2Share.jWarr, dcHigh: 40, hp: 1000);
plain.m_nNativeMonsterSuperForceMask = 2;
plain.m_nNativeMonsterSuperForceReductionPercent = 25;
SetShieldCharges(plain, 2);
plain.SetNativeActiveState(59);
Equal((ushort)100, plain.GetHitStruckDamage(mage, 100),
    "0x7679AE ret 4: sub_767958 applies neither super-force nor the shield");
Equal(100, plain.GetMagStruckDamage(mage, 100),
    "0x767A0E ret 4: sub_7679B8 applies neither either");
Equal((ushort)2, ShieldCharges(plain),
    "the armour getters must not spend a charge");

if (failures != 0)
{
    Console.WriteLine($"FAIL {failures}");
    return 1;
}
Console.WriteLine("NativeStruckDamageStageOrderCheck: PASS");
return 0;

// ------------------------------------------------------------------- helpers
Monster NewActor(byte job, int dcHigh, int hp)
{
    var actor = new Monster { m_btJob = job };
    actor.m_WAbil.DC = HUtil32.MakeLong((short)0, (ushort)dcHigh);
    actor.m_WAbil.MC = HUtil32.MakeLong((short)0, (ushort)dcHigh);
    actor.m_WAbil.SC = HUtil32.MakeLong((short)0, (ushort)dcHigh);
    actor.m_WAbil.AC = 0;
    actor.m_WAbil.MAC = 0;
    actor.m_WAbil.HP = (ushort)hp;
    actor.m_WAbil.MaxHP = (ushort)hp;
    actor.m_WAbil.MP = 0;
    return actor;
}

void SetShieldCharges(TBaseObject actor, ushort charges) =>
    typeof(TBaseObject)
        .GetField("m_wNativeSkill153ShieldCharges",
            BindingFlags.Instance | BindingFlags.NonPublic
            | BindingFlags.Public)!
        .SetValue(actor, charges);

ushort ShieldCharges(TBaseObject actor) => (ushort)typeof(TBaseObject)
    .GetField("m_wNativeSkill153ShieldCharges",
        BindingFlags.Instance | BindingFlags.NonPublic
        | BindingFlags.Public)!
    .GetValue(actor)!;

void Check(bool condition, string description)
{
    if (condition) return;
    Console.WriteLine("FAIL: " + description);
    failures++;
}

void Equal<T>(T expected, T actual, string description)
{
    if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
    Console.WriteLine(
        $"FAIL: {description} (expected {expected}, actual {actual})");
    failures++;
}

static string FindRepositoryRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "LyoMir2.sln")))
            return dir.FullName;
        dir = dir.Parent;
    }
    // Fall back to walking up from the build output (Build/AuditTools/<name>/
    // <tfm>/) so the audit works from either cwd, like its siblings.
    dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "LyoMir2-master",
            "LyoMir2.sln");
        if (File.Exists(candidate))
            return Path.Combine(dir.FullName, "LyoMir2-master");
        dir = dir.Parent;
    }
    throw new InvalidOperationException(
        "LyoMir2.sln not found above the current directory or the build dir");
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}
