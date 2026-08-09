using System.Numerics;
using GameSvr.Services;

// NativeFieldHeroNineClassesCheck
//
// Cross-checks GameSvr.Services.NativeFieldHeroNineClasses (the dormant model of
// the nine concrete FieldHero/战神 classes) against an INDEPENDENT oracle derived
// straight from the original M2Server.exe disassembly
// (SHA-256 5540f43bc58d8d67673927c4186941e253403bb7d3a2a0b40ebfcf049670b14e).
//
// The oracle recomputes every ability value with exact BigInteger-rational
// round-half-to-even (using the exact 80-bit 2.2 constant 0x8CCCCCCCCCCCCCCD*2^-62),
// so it does not share the model's IEEE-754 double path. If the two disagree for
// any level in 0..65535, the check fails.

// Exact 80-bit x87 constant tbyte_60C070 / tbyte_60C548 == 2.2 (both classes share it).
BigInteger c22Num = 0x8CCCCCCCCCCCCCCDUL;
BigInteger c22Den = BigInteger.One << 62;

// Cache the slot list once; the ability sweep touches it hundreds of thousands
// of times.
NativeFieldHeroAbilitySlot[] allSlots =
    Enum.GetValues<NativeFieldHeroAbilitySlot>();

VerifyRounderAndMaxHelpers();
VerifyClassMetadata();
VerifySkillSetsAndInitOrder();
VerifyAbilityFormulas();

Console.WriteLine(
    "PASS field-hero nine-classes " +
    "sha=5540f43b classes=9(4 ordinary+4 dota+model) " +
    "rounder=fistp-qword-round-half-even max=sub_4C7004 " +
    "abilities=exact-rational-vs-model(L=0..65535) " +
    "skills=war3/12/26/34@3 wiz11/35/31/10@3 taos4/6/13/36@3 ass260/264/268@4 " +
    "order=ass-initialize-first model=no-skills " +
    "dota=ac/mac-unwritten model=currentMP==maxHP(5000)");
return;

void VerifyRounderAndMaxHelpers()
{
    // fistp qword under control word 0x037F is round-half-to-even. Probe both
    // half-integer parities and a non-half value.
    Equal(2, NativeFieldHeroNineClasses.RoundToNativeInt(2.5), "round 2.5 -> even 2");
    Equal(4, NativeFieldHeroNineClasses.RoundToNativeInt(3.5), "round 3.5 -> even 4");
    Equal(4, NativeFieldHeroNineClasses.RoundToNativeInt(4.5), "round 4.5 -> even 4");
    Equal(3, NativeFieldHeroNineClasses.RoundToNativeInt(2.7), "round 2.7 -> 3");
    Equal(-2, NativeFieldHeroNineClasses.RoundToNativeInt(-2.5), "round -2.5 -> even -2");

    // sub_4C7004(a,b) is signed max.
    Equal(1, NativeFieldHeroNineClasses.NativeMax(-1, 1), "max(-1,1)");
    Equal(0, NativeFieldHeroNineClasses.NativeMax(-1, 0), "max(-1,0)");
    Equal(7, NativeFieldHeroNineClasses.NativeMax(7, 3), "max(7,3)");
    Equal(0, NativeFieldHeroNineClasses.NativeMax(0, 0), "max(0,0)");
}

void VerifyClassMetadata()
{
    Equal(9, NativeFieldHeroNineClasses.Classes.Count, "class count");

    // kind, rtti, selector, jobByte, classPtrVar, vmt, ctor, size, abilInit, skillInit
    ExpectClass(NativeType2FieldHeroActorKind.FieldWarHero, "TFieldWarHero",
        0, 0, 0x00607180, 0x006071CC, 0x0060B6EC, 0x6A8, 0x0060B8BC, 0x0060B870);
    ExpectClass(NativeType2FieldHeroActorKind.FieldWizHero, "TFieldWizHero",
        1, 1, 0x006076F0, 0x0060773C, 0x0060C1DC, 0x6A4, 0x0060C3FC, 0x0060C30C);
    ExpectClass(NativeType2FieldHeroActorKind.FieldTaosHero, "TFieldTaosHero",
        2, 2, 0x006079A8, 0x006079F4, 0x0060BD88, 0x6A8, 0x0060BF14, 0x0060BEC0);
    ExpectClass(NativeType2FieldHeroActorKind.FieldAssHero, "TFieldAssHero",
        3, 3, 0x00607438, 0x00607484, 0x00608D68, 0x6A0, 0x00608ED8, 0x00608E98);
    ExpectClass(NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar,
        "TMirDotaMatchHumMon_War", 4, 0, 0x006081E4, 0x00608230, 0x0060CDDC,
        0x6B8, 0x0060D134, 0x0060CE1C);
    ExpectClass(NativeType2FieldHeroActorKind.MirDotaMatchHumMonWiz,
        "TMirDotaMatchHumMon_Wiz", 5, 1, 0x00608774, 0x006087C0, 0x0060D644,
        0x6B4, 0x0060D850, 0x0060D764);
    ExpectClass(NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos,
        "TMirDotaMatchHumMon_Taos", 6, 2, 0x00608A3C, 0x00608A88, 0x0060DA0C,
        0x6B8, 0x0060DB50, 0x0060DB04);
    ExpectClass(NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss,
        "TMirDotaMatchHumMon_Ass", 7, 3, 0x006084AC, 0x006084F8, 0x0060D3C0,
        0x6B0, 0x0060D51C, 0x0060D4DC);

    var model = NativeFieldHeroNineClasses.Get(
        NativeType2FieldHeroActorKind.ModelHero);
    Equal("TModelHero", model.RttiName, "model rtti");
    Assert(model.Selector == null, "model selector is default (null)");
    Equal(0x00607C60u, model.ClassPointerVariable, "model class ptr var");
    Equal(0x00607CACu, model.Vmt, "model vmt");
    Equal(0x00609038u, model.Constructor, "model ctor");
    Equal(0x6A0, model.InstanceSize, "model size");
    Equal(0x00609094u, model.AbilityInitializer, "model ability init");
    Assert(model.SkillInitializer == null,
        "model has no VMT+0x78 override (inherits common Initialize)");

    // Shared native anchors.
    Equal(0x0060913Cu, NativeFieldHeroNineClasses.SkillAppendHelper, "skill append helper");
    Equal(0x00403574u, NativeFieldHeroNineClasses.RounderFunction, "rounder function");
    Equal(0x004C7004u, NativeFieldHeroNineClasses.MaxFunction, "max function");
    Equal(0x006094E8u, NativeFieldHeroNineClasses.BaseConstructor, "base ctor");
    Equal(0x0060C5BCu, NativeFieldHeroNineClasses.DotaBaseConstructor, "dota base ctor");
}

void VerifySkillSetsAndInitOrder()
{
    // (magicId, level) in native append order; assassins append AFTER Initialize.
    ExpectSkills(NativeType2FieldHeroActorKind.FieldWarHero,
        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
        (3, 3), (12, 3), (26, 3), (34, 3));
    ExpectSkills(NativeType2FieldHeroActorKind.FieldWizHero,
        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
        (11, 3), (35, 3), (31, 3), (10, 3));
    ExpectSkills(NativeType2FieldHeroActorKind.FieldTaosHero,
        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
        (4, 3), (6, 3), (13, 3), (36, 3));
    ExpectSkills(NativeType2FieldHeroActorKind.FieldAssHero,
        NativeFieldHeroInitOrder.InitializeBeforeSkills,
        (260, 4), (264, 4), (268, 4));
    ExpectSkills(NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar,
        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
        (3, 3), (12, 3), (26, 3), (34, 3));
    ExpectSkills(NativeType2FieldHeroActorKind.MirDotaMatchHumMonWiz,
        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
        (11, 3), (35, 3), (31, 3), (10, 3));
    ExpectSkills(NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos,
        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
        (4, 3), (6, 3), (13, 3), (36, 3));
    ExpectSkills(NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss,
        NativeFieldHeroInitOrder.InitializeBeforeSkills,
        (260, 4), (264, 4), (268, 4));

    var model = NativeFieldHeroNineClasses.Get(
        NativeType2FieldHeroActorKind.ModelHero);
    Equal(0, model.Skills.Count, "model has no skills");
    Assert(model.InitOrder == NativeFieldHeroInitOrder.NoSkills,
        "model init order is NoSkills");
}

void VerifyAbilityFormulas()
{
    // A non-trivial initial ability block: current HP/MP and mirrors set high so
    // the ClampDownTo epilogue is exercised, and AC/MAC seeded with a sentinel so
    // the Dota classes' unwritten AC/MAC can be proven to survive.
    const int sentinel = 0x11111111;
    foreach (var kind in Enum.GetValues<NativeType2FieldHeroActorKind>())
    {
        var contract = NativeFieldHeroNineClasses.Get(kind);
        for (var level = 0; level <= ushort.MaxValue; level++)
        {
            var initial = SeededBlock(sentinel);
            var expected = ExpectedAppliedBlock(kind, level, initial, sentinel);
            var actual = contract.ComputeAbilities(level).Apply(initial);
            AssertBlockEqual(kind, level, expected, actual);
        }
    }
}

// ---- independent oracle ---------------------------------------------------

Dictionary<NativeFieldHeroAbilitySlot, int> SeededBlock(int sentinel)
{
    var block = new Dictionary<NativeFieldHeroAbilitySlot, int>();
    foreach (var slot in allSlots)
        block[slot] = sentinel;
    return block;
}

Dictionary<NativeFieldHeroAbilitySlot, int> ExpectedAppliedBlock(
    NativeType2FieldHeroActorKind kind, int level,
    IReadOnlyDictionary<NativeFieldHeroAbilitySlot, int> initial, int sentinel)
{
    var b = new Dictionary<NativeFieldHeroAbilitySlot, int>(
        (IDictionary<NativeFieldHeroAbilitySlot, int>)initial);
    BigInteger l = level;

    if (kind == NativeType2FieldHeroActorKind.ModelHero)
    {
        const int maxHp = 5000;
        b[NativeFieldHeroAbilitySlot.MaxHp] = maxHp;
        b[NativeFieldHeroAbilitySlot.MaxMp] = 1000;
        b[NativeFieldHeroAbilitySlot.AcHigh] = 1000;
        b[NativeFieldHeroAbilitySlot.MacHigh] = 1000;
        b[NativeFieldHeroAbilitySlot.CurrentHp] = maxHp;
        b[NativeFieldHeroAbilitySlot.CurrentMp] = maxHp;         // == MaxHP, not MaxMP
        b[NativeFieldHeroAbilitySlot.WorkingMaxHpMirror] = maxHp;
        b[NativeFieldHeroAbilitySlot.WorkingHpMirror] = maxHp;
        // AcLow, MacLow, DC/MC/SC/CC and WorkingMpMirror stay at sentinel.
        return b;
    }

    int maxHpOut, maxMpOut;
    switch (kind)
    {
        case NativeType2FieldHeroActorKind.FieldWarHero:
        {
            maxHpOut = unchecked((int)RoundR(l * (11 * l + 200), 20) + 50);
            if (level > 60) maxHpOut = unchecked(maxHpOut - 3 * (level - 60));
            maxMpOut = unchecked((int)RoundR(7 * l, 2) + 11);
            var n = level / 5;
            b[NativeFieldHeroAbilitySlot.DcLow] = Max(n - 1, 1);
            b[NativeFieldHeroAbilitySlot.DcHigh] = Max(n, 1);
            b[NativeFieldHeroAbilitySlot.McLow] = 0;
            b[NativeFieldHeroAbilitySlot.McHigh] = 0;
            b[NativeFieldHeroAbilitySlot.ScLow] = 0;
            b[NativeFieldHeroAbilitySlot.ScHigh] = 0;
            b[NativeFieldHeroAbilitySlot.CcLow] = 0;
            b[NativeFieldHeroAbilitySlot.CcHigh] = 0;
            b[NativeFieldHeroAbilitySlot.AcLow] = 0;
            b[NativeFieldHeroAbilitySlot.AcHigh] = level / 7;
            b[NativeFieldHeroAbilitySlot.MacLow] = 0;
            b[NativeFieldHeroAbilitySlot.MacHigh] = 0;
            break;
        }
        case NativeType2FieldHeroActorKind.FieldWizHero:
        {
            maxHpOut = unchecked((int)RoundR(l * (l + 75), 15) + 50);
            if (level > 60) maxHpOut = unchecked(maxHpOut + 30 * (level - 60));
            maxMpOut = unchecked((int)RoundR(l * (l + 10) * c22Num, 5 * c22Den) + 13);
            var n = level / 7;
            var lo = Max(n - 1, 0);
            var hi = Max(n, 1);
            b[NativeFieldHeroAbilitySlot.DcLow] = lo;
            b[NativeFieldHeroAbilitySlot.DcHigh] = hi;
            b[NativeFieldHeroAbilitySlot.McLow] = lo;
            b[NativeFieldHeroAbilitySlot.McHigh] = hi;
            b[NativeFieldHeroAbilitySlot.ScLow] = 0;
            b[NativeFieldHeroAbilitySlot.ScHigh] = 0;
            b[NativeFieldHeroAbilitySlot.CcLow] = 0;
            b[NativeFieldHeroAbilitySlot.CcHigh] = 0;
            b[NativeFieldHeroAbilitySlot.AcLow] = 0;
            b[NativeFieldHeroAbilitySlot.AcHigh] = 0;
            b[NativeFieldHeroAbilitySlot.MacLow] = 0;
            b[NativeFieldHeroAbilitySlot.MacHigh] = 0;
            break;
        }
        case NativeType2FieldHeroActorKind.FieldTaosHero:
        {
            maxHpOut = unchecked((int)RoundR(l * (l + 60), 6) + 50);
            if (level > 60) maxHpOut = unchecked(maxHpOut + 33 * (level - 60));
            maxMpOut = unchecked((int)RoundR(l * l * c22Num, 8 * c22Den) + 13);
            var n = level / 7;
            var lo = Max(n - 1, 0);
            var hi = Max(n, 1);
            var n6 = level / 6;
            b[NativeFieldHeroAbilitySlot.DcLow] = lo;
            b[NativeFieldHeroAbilitySlot.DcHigh] = hi;
            b[NativeFieldHeroAbilitySlot.McLow] = 0;
            b[NativeFieldHeroAbilitySlot.McHigh] = 0;
            b[NativeFieldHeroAbilitySlot.ScLow] = lo;
            b[NativeFieldHeroAbilitySlot.ScHigh] = hi;
            b[NativeFieldHeroAbilitySlot.CcLow] = 0;
            b[NativeFieldHeroAbilitySlot.CcHigh] = 0;
            b[NativeFieldHeroAbilitySlot.AcLow] = 0;
            b[NativeFieldHeroAbilitySlot.AcHigh] = 0;
            b[NativeFieldHeroAbilitySlot.MacLow] = n6 / 2;   // == L/12
            b[NativeFieldHeroAbilitySlot.MacHigh] = n6 + 1;  // == L/6 + 1
            break;
        }
        case NativeType2FieldHeroActorKind.FieldAssHero:
        {
            var v = unchecked((int)RoundR(l * (11 * l + 200), 20) + 50);
            maxHpOut = v;
            maxMpOut = v;
            var n = level / 5;
            b[NativeFieldHeroAbilitySlot.DcLow] = 0;
            b[NativeFieldHeroAbilitySlot.DcHigh] = 0;
            b[NativeFieldHeroAbilitySlot.McLow] = 0;
            b[NativeFieldHeroAbilitySlot.McHigh] = 0;
            b[NativeFieldHeroAbilitySlot.ScLow] = 0;
            b[NativeFieldHeroAbilitySlot.ScHigh] = 0;
            b[NativeFieldHeroAbilitySlot.CcLow] = n - 1;   // deliberately unclamped (-1 at low L)
            b[NativeFieldHeroAbilitySlot.CcHigh] = n;
            b[NativeFieldHeroAbilitySlot.AcLow] = 0;
            b[NativeFieldHeroAbilitySlot.AcHigh] = level / 7;
            b[NativeFieldHeroAbilitySlot.MacLow] = 0;
            b[NativeFieldHeroAbilitySlot.MacHigh] = 0;
            break;
        }
        case NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar:
            maxHpOut = unchecked(50000 * level);
            maxMpOut = unchecked(5000 * level);
            SetDotaPairs(b, NativeFieldHeroAbilitySlot.DcLow, unchecked(500 * level));
            break;
        case NativeType2FieldHeroActorKind.MirDotaMatchHumMonWiz:
            maxHpOut = unchecked(5000 * level);
            maxMpOut = unchecked(50000 * level);
            SetDotaPairs(b, NativeFieldHeroAbilitySlot.McLow, unchecked(800 * level));
            break;
        case NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos:
            maxHpOut = unchecked(25000 * level);
            maxMpOut = unchecked(25000 * level);
            SetDotaPairs(b, NativeFieldHeroAbilitySlot.ScLow, unchecked(1000 * level));
            break;
        case NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss:
            maxHpOut = unchecked(25000 * level);
            maxMpOut = unchecked(25000 * level);
            SetDotaPairs(b, NativeFieldHeroAbilitySlot.CcLow, unchecked(500 * level));
            break;
        default:
            throw new InvalidOperationException("unhandled kind " + kind);
    }

    b[NativeFieldHeroAbilitySlot.MaxHp] = maxHpOut;
    b[NativeFieldHeroAbilitySlot.MaxMp] = maxMpOut;
    // Clamp epilogue: only lower current HP/MP and the two working mirrors.
    b[NativeFieldHeroAbilitySlot.CurrentHp] =
        ClampDown(initial[NativeFieldHeroAbilitySlot.CurrentHp], maxHpOut);
    b[NativeFieldHeroAbilitySlot.CurrentMp] =
        ClampDown(initial[NativeFieldHeroAbilitySlot.CurrentMp], maxMpOut);
    b[NativeFieldHeroAbilitySlot.WorkingHpMirror] =
        ClampDown(initial[NativeFieldHeroAbilitySlot.WorkingHpMirror], maxHpOut);
    b[NativeFieldHeroAbilitySlot.WorkingMpMirror] =
        ClampDown(initial[NativeFieldHeroAbilitySlot.WorkingMpMirror], maxMpOut);
    // For ordinary classes AC/MAC were written above; for Dota they remain at
    // the sentinel (proving the initializer never touches them), and
    // WorkingMaxHpMirror (+0x2B0) is never touched by ordinary/Dota init.
    _ = sentinel;
    return b;
}

static void SetDotaPairs(Dictionary<NativeFieldHeroAbilitySlot, int> b,
    NativeFieldHeroAbilitySlot pairLow, int pairValue)
{
    // DC/MC/SC/CC are all written; three to zero, one to the pair multiple.
    // AC (+0x18/+0x1C) and MAC (+0x20/+0x24) are intentionally NOT written.
    b[NativeFieldHeroAbilitySlot.DcLow] =
        pairLow == NativeFieldHeroAbilitySlot.DcLow ? pairValue : 0;
    b[NativeFieldHeroAbilitySlot.DcHigh] =
        pairLow == NativeFieldHeroAbilitySlot.DcLow ? pairValue : 0;
    b[NativeFieldHeroAbilitySlot.McLow] =
        pairLow == NativeFieldHeroAbilitySlot.McLow ? pairValue : 0;
    b[NativeFieldHeroAbilitySlot.McHigh] =
        pairLow == NativeFieldHeroAbilitySlot.McLow ? pairValue : 0;
    b[NativeFieldHeroAbilitySlot.ScLow] =
        pairLow == NativeFieldHeroAbilitySlot.ScLow ? pairValue : 0;
    b[NativeFieldHeroAbilitySlot.ScHigh] =
        pairLow == NativeFieldHeroAbilitySlot.ScLow ? pairValue : 0;
    b[NativeFieldHeroAbilitySlot.CcLow] =
        pairLow == NativeFieldHeroAbilitySlot.CcLow ? pairValue : 0;
    b[NativeFieldHeroAbilitySlot.CcHigh] =
        pairLow == NativeFieldHeroAbilitySlot.CcLow ? pairValue : 0;
}

// Exact round-half-to-even of num/den (den > 0), matching fistp under 0x037F.
static long RoundR(BigInteger num, BigInteger den)
{
    var q = BigInteger.DivRem(num, den, out var r);
    if (r < 0) { q -= 1; r += den; } // floor + non-negative remainder
    var twice = r * 2;
    var cmp = twice.CompareTo(den);
    if (cmp < 0) return (long)q;
    if (cmp > 0) return (long)(q + 1);
    return q.IsEven ? (long)q : (long)(q + 1);
}

static int Max(int a, int b) => a >= b ? a : b;

static int ClampDown(int existing, int candidate)
    => candidate < existing ? candidate : existing;

void AssertBlockEqual(NativeType2FieldHeroActorKind kind, int level,
    IReadOnlyDictionary<NativeFieldHeroAbilitySlot, int> expected,
    IReadOnlyDictionary<NativeFieldHeroAbilitySlot, int> actual)
{
    foreach (var slot in allSlots)
    {
        var e = expected.TryGetValue(slot, out var ev) ? ev : int.MinValue;
        var a = actual.TryGetValue(slot, out var av) ? av : int.MinValue;
        if (e != a)
        {
            throw new InvalidOperationException(
                $"{kind} L={level} slot {slot}: expected {e}, actual {a}");
        }
    }
    // No extra slots may appear in the actual result.
    foreach (var slot in actual.Keys)
    {
        if (!expected.ContainsKey(slot))
        {
            throw new InvalidOperationException(
                $"{kind} L={level} unexpected slot {slot} in model output");
        }
    }
}

// ---- assertion helpers ----------------------------------------------------

void ExpectClass(NativeType2FieldHeroActorKind kind, string rtti, int selector,
    byte job, uint classPtr, uint vmt, uint ctor, int size, uint abil, uint skill)
{
    var c = NativeFieldHeroNineClasses.Get(kind);
    Equal((int)kind, (int)c.ActorKind, rtti + " kind");
    Equal(rtti, c.RttiName, rtti + " rtti");
    Assert(c.Selector == selector, rtti + " selector");
    Equal((int)job, (int)c.JobByte, rtti + " job byte");
    Equal(classPtr, c.ClassPointerVariable, rtti + " class ptr var");
    Equal(vmt, c.Vmt, rtti + " vmt");
    Equal(ctor, c.Constructor, rtti + " ctor");
    Equal(size, c.InstanceSize, rtti + " size");
    Equal(abil, c.AbilityInitializer, rtti + " ability init");
    Assert(c.SkillInitializer == skill, rtti + " skill init");
}

void ExpectSkills(NativeType2FieldHeroActorKind kind,
    NativeFieldHeroInitOrder order, params (int magic, int level)[] skills)
{
    var c = NativeFieldHeroNineClasses.Get(kind);
    Assert(c.InitOrder == order, kind + " init order");
    Equal(skills.Length, c.Skills.Count, kind + " skill count");
    for (var i = 0; i < skills.Length; i++)
    {
        Equal(skills[i].magic, c.Skills[i].MagicId, kind + " skill[" + i + "] magic");
        Equal(skills[i].level, c.Skills[i].Level, kind + " skill[" + i + "] level");
    }
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!object.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
