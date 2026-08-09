// NativeHeroMagicMpCostCheck — one audit covering both 2026-08-04 fixes:
//
//   (A) hero-magic name resolution / deletion, and
//   (B) the MP-cost family divisor.
//
// Every assertion below is a RUNTIME assertion against real GameSvr methods (no
// source-text greps), so it bites on behaviour rather than on wording.
//
// Authoritative binary: D:/loym2/staging/M2Server_reunpacked_20260803.exe
// Full evidence + raw disassembly: staging/heromagic_mpcost_fix_20260804.md
//
// ---------------------------------------------------------------------------
// (A) Native sub_73F690 @0x73F690..0x73F7D2 — hero magic DELETE.
//     Anchored via SM_HERO_DELMAGIC = 2972 = 0xB9C (`mov dx,0xB9C` @0x73F74C); a full
//     CODE-segment sweep for that immediate in every 16-bit load form finds exactly
//     2 sites, both inside the hero-object region.
//       @0x73F6B4  test edi,edi / je       -> nil/empty name is False
//       @0x73F6BC  mov eax,[ebx+0x500]     -> the HERO'S OWN magic TList; NO global pool
//       @0x73F6C2  mov esi,[eax+8]/dec esi -> iterate Count-1 DOWNTO 0
//       @0x73F6EF  mov edx,[edx]           -> UserMagic+0x00 = MagicInfo*, name at +0x00
//       @0x73F6FB  call 0x40BD78           -> CASE-INSENSITIVE compare (repe cmpsb, then
//                                             upper-cases the mismatching byte pair
//                                             @0x40BD9E..0x40BDA8)
//       @0x73F708  mov al,[ebx+0x178]      -> m_btRaceServer: 0 -> SM_DELMAGIC 0xD4,
//       @0x73F735  cmp al,0x36             -> 54=RC_HEROOBJECT -> SM_HERO_DELMAGIC 0xB9C
//       @0x73F797  call 0x424B30           -> TList.Delete(i)
//       @0x73F79C  mov byte [ebp-1],1/jmp  -> True, and STOP after the FIRST match
//     Native never consults a global definition pool here, so C# must not either: the
//     old `UserEngine.FindHeroMagic(name)` prelude made deletion IMPOSSIBLE whenever
//     the definition was absent from the published Hero pool (the MySQL loader in
//     CommonDB.LoadMagicDB only ever fills the Human half).
//
// ---------------------------------------------------------------------------
// (B) Native sub_4C8888 @0x4C8888..0x4C88C5 — the ONLY MP-cost producer (18 callers).
//       @0x4C8896  mov al,[esi+0x14]         -> wSpell (BYTE)
//       @0x4C889C  fild dword [ebp-4]        -> to x87 BEFORE dividing (no integer div)
//       @0x4C889F  fdiv dword ptr [0x4C88C8] -> float32 4.0 (raw 00 00 80 40). The `D8 /6`
//                                              encoding fixes the operand width at 4
//                                              bytes, so the tbyte/xword trap can't apply.
//       @0x4C88A7  mov al,[ebx+0x0C] / inc   -> btLevel + 1
//       @0x4C88B1  fmulp st(1)
//       @0x4C88B3  call 0x403574             -> fistp qword = round-half-to-EVEN
//                                              (sub_403580 is the truncating twin: it
//                                              or's RC=11 into the control word first)
//       @0x4C88BA  mov dl,[esi+0x17] / add ax,dx -> + btDefSpell, INSIDE the function
//     btTrainLv (+0x1A) is NEVER read in the body; natively it is only the level CAP
//     (sub_4C88EC @0x4C88F4, sub_4C896C @0x4C8974). The discarded `(btTrainLv + 1)`
//     divisor equals 4.0 exactly when btTrainLv == 3 — and CommonDB.cs hardcodes
//     btTrainLv = 3 — which is why the defect stayed invisible.
//     Structural proof no field divisor can exist: all 202 `fdiv dword ptr [imm32]`
//     sites in CODE dereference an absolute literal pool address; none takes a
//     computed operand.

using System.Reflection;
using GameSvr;
using SystemModule;

int checks = 0;

try
{
    PrepareRuntimeConfig();
    InitializeRuntime();

    VerifyMpCostFormula();
    VerifyMpCostCallersDoNotDoubleAddDefSpell();
    VerifyHeroMagicDeleteIgnoresGlobalPool();
    VerifyHeroMagicDeleteNativeSemantics();

    Console.WriteLine(
        "PASS NativeHeroMagicMpCostCheck checks=" + checks +
        " mp=sub_4C8888(4.0f@0x4C88C8,round-half-even@0x403574,btDefSpell-folded)" +
        " delete=sub_73F690(own-list,no-pool,downward,case-insensitive,first-match)");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("NativeHeroMagicMpCostCheck FAIL: " + ex);
    return 1;
}

// ===========================================================================
// (B) MP cost
// ===========================================================================

// The whole point of the fix: with btTrainLv != 3 the native 4.0 divisor and the old
// (btTrainLv + 1) divisor DISAGREE, so these cases pin the native one.
void VerifyMpCostFormula()
{
    // Worked example straight from the deliverable: wSpell=100, btLevel=2, btDefSpell=3,
    // btTrainLv=1.
    //   native : Round((100 / 4.0) * (2 + 1)) + 3 = Round(75.0) + 3 = 78
    //   old C# : (100 / (1 + 1)) * (2 + 1) + 3    = 153   (also INTEGER-divided, i.e.
    //            truncating before the multiply, where native fild's first)
    Equal((ushort)78, MpCost(spell: 100, level: 2, defSpell: 3, trainLv: 1),
        "MP cost wSpell=100 btLevel=2 btDefSpell=3 btTrainLv=1 (native 4.0 @0x4C88C8)");

    // btTrainLv is not read at all: holding everything else fixed, sweeping btTrainLv
    // over its whole byte range must NOT move the answer. This single assertion kills
    // any divisor that reads +0x1A, no matter its algebraic form.
    ushort baseline = MpCost(spell: 100, level: 2, defSpell: 3, trainLv: 0);
    for (int trainLv = 0; trainLv <= 255; trainLv++)
    {
        Equal(baseline, MpCost(spell: 100, level: 2, defSpell: 3,
                unchecked((byte)trainLv)),
            "MP cost must ignore btTrainLv (+0x1A never read in sub_4C8888) at btTrainLv="
            + trainLv);
    }

    // Rounding must be banker's (sub_403574 `fistp qword`, default RC=00 =
    // round-to-nearest-EVEN), NOT truncation (sub_403580 or's RC=11 first) and NOT
    // round-half-away-from-zero.
    //   wSpell=10, btLevel=0 -> (10/4.0)*1 = 2.5 -> ToEven = 2   (truncate would be 2 too)
    //   wSpell=30, btLevel=0 -> (30/4.0)*1 = 7.5 -> ToEven = 8   (truncate would be 7,
    //                                                             away-from-zero also 8)
    //   wSpell=10, btLevel=1 -> (10/4.0)*2 = 5.0 -> 5            (exact)
    //   wSpell=14, btLevel=0 -> (14/4.0)*1 = 3.5 -> ToEven = 4   (truncate would be 3)
    //   wSpell=18, btLevel=0 -> (18/4.0)*1 = 4.5 -> ToEven = 4   (away-from-zero -> 5,
    //                                                             so THIS case separates
    //                                                             banker's from both)
    Equal((ushort)2, MpCost(spell: 10, level: 0, defSpell: 0, trainLv: 1),
        "MP cost 2.5 rounds to even 2 (sub_403574)");
    Equal((ushort)8, MpCost(spell: 30, level: 0, defSpell: 0, trainLv: 1),
        "MP cost 7.5 rounds to even 8 (sub_403574, not truncation)");
    Equal((ushort)5, MpCost(spell: 10, level: 1, defSpell: 0, trainLv: 1),
        "MP cost exact 5.0");
    Equal((ushort)4, MpCost(spell: 14, level: 0, defSpell: 0, trainLv: 1),
        "MP cost 3.5 rounds to even 4 (not truncation)");
    Equal((ushort)4, MpCost(spell: 18, level: 0, defSpell: 0, trainLv: 1),
        "MP cost 4.5 rounds to even 4 (round-half-to-even, NOT half-away-from-zero)");

    // Native divides in FLOAT (fild then fdiv), so an odd wSpell must not truncate
    // before the multiply. wSpell=5, btLevel=3: native (5/4.0)*4 = 5.0 -> 5.
    // An integer-division port would compute (5/4)*4 = 1*4 = 4.
    Equal((ushort)5, MpCost(spell: 5, level: 3, defSpell: 0, trainLv: 1),
        "MP cost divides in float before multiplying (@0x4C889C fild then @0x4C889F fdiv)");

    // btDefSpell is added AFTER the rounding (@0x4C88BA/@0x4C88BD), never scaled by it.
    Equal((ushort)(MpCost(spell: 100, level: 2, defSpell: 0, trainLv: 1) + 7),
        MpCost(spell: 100, level: 2, defSpell: 7, trainLv: 1),
        "btDefSpell is added post-round, unscaled (@0x4C88BD add ax,dx)");
}

// Native folds btDefSpell in and every one of the 18 call sites consumes AX directly
// (`movzx eax,ax` / `mov word ptr [..],ax`, never a further add). So the three
// 半月/烈火半月/圆月 branches of AttackDir (native @0x6BC832 / @0x6BC8AE / @0x6BC96F) and
// the client-magic encoder (native @0x4C850C -> @0x4C8511 mov word [ebx+0x18],ax) must
// all agree bit-for-bit with the single helper.
void VerifyMpCostCallersDoNotDoubleAddDefSpell()
{
    var magic = NewMagic(spell: 100, level: 2, defSpell: 3, trainLv: 1);
    ushort expected = TPlayObject.GetNativeMagicProducerMpCost(magic);
    Equal((ushort)78, expected, "fixture sanity: native MP cost is 78");

    // TBaseObject.GetMagicSpell — the shared producer used by the AttackDir branches.
    var actor = new TPlayObject();
    Equal((short)expected, actor.GetMagicSpell(magic),
        "TBaseObject.GetMagicSpell returns the COMPLETE native cost incl. btDefSpell "
        + "(@0x4C88BA); native @0x6BC837 uses AX directly with no further add");

    // HeroObject.GetHeroSpellPoint — the hero cast path (native @0x76A600 -> @0x76A61B
    // movzx eax,di, again no further add).
    var heroSpellPoint = typeof(HeroObject).GetMethod("GetHeroSpellPoint",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("HeroObject.GetHeroSpellPoint");
    Equal(expected, (ushort)heroSpellPoint.Invoke(new HeroObject(),
            new object[] { magic }),
        "HeroObject.GetHeroSpellPoint matches the native MP producer");

    // TPlayObject.GetSpellPoint — the player cast path.
    Equal(expected, actor.GetSpellPoint(magic),
        "TPlayObject.GetSpellPoint matches the native MP producer");

    // The encoded packet's NeedMp field (offset 24) is what native writes at
    // @0x4C8511 `mov word ptr [ebx+0x18],ax`.
    var encoder = typeof(TPlayObject).GetMethod("EncodeClientMagic",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TPlayObject.EncodeClientMagic");
    var encoded = (byte[])encoder.Invoke(null, new object[] { magic });
    Equal((short)expected, BitConverter.ToInt16(encoded, 24),
        "client magic NeedMp equals the native MP producer (@0x4C850C/@0x4C8511)");
}

// ===========================================================================
// (A) hero magic delete
// ===========================================================================

// The regression that motivated the fix: a skill the hero really owns must be deletable
// even when the global definition pools are EMPTY, because native sub_73F690 never
// consults them. This also covers the historical failure mode where the definition sat
// in the Human half (which is all CommonDB.LoadMagicDB ever fills) while the lookup read
// the Hero half.
void VerifyHeroMagicDeleteIgnoresGlobalPool()
{
    var previousEngine = M2Share.UserEngine;
    try
    {
        // A pristine UserEngine publishes two EMPTY definition lists, so
        // FindHeroMagic/FindMagic would both return null for any name.
        M2Share.UserEngine = new UserEngine();
        Equal(0, M2Share.UserEngine.m_HeroMagicList.Count,
            "fixture: hero definition pool is empty");
        Equal(0, M2Share.UserEngine.m_MagicList.Count,
            "fixture: human definition pool is empty");

        const ushort magicId = 17;
        var hero = NewHeroWithMagic(magicId, "pool-independent", out var owned);

        Assert(hero.DeleteHeroMagic("pool-independent"),
            "DeleteHeroMagic must succeed with EMPTY definition pools — native "
            + "sub_73F690 @0x73F6BC reads only the hero's own [+0x500] list");
        Equal(0, hero.m_HeroMagicList.Count, "deleted entry left the hero list");
        Assert(hero.m_MagicArr[magicId] == null,
            "deleted entry left the indexed magic array");

        // The same skill filed ONLY in the Human half must still be deletable: the
        // pool is simply not consulted.
        hero = NewHeroWithMagic(magicId, "human-half-only", out owned);
        M2Share.UserEngine.m_MagicList.Add(owned.MagicInfo);
        Assert(hero.DeleteHeroMagic("human-half-only"),
            "DeleteHeroMagic must not depend on which pool half holds the definition");
    }
    finally
    {
        M2Share.UserEngine = previousEngine;
    }
}

void VerifyHeroMagicDeleteNativeSemantics()
{
    var previousEngine = M2Share.UserEngine;
    try
    {
        M2Share.UserEngine = new UserEngine();

        // @0x73F6B4 `test edi,edi / je` — a nil/empty name is False, not a crash.
        var hero = NewHeroWithMagic(17, "guard", out _);
        Assert(!hero.DeleteHeroMagic(null),
            "null name returns False (@0x73F6B4 test edi,edi)");
        Assert(!hero.DeleteHeroMagic(string.Empty),
            "empty name returns False (@0x73F6B4)");
        Equal(1, hero.m_HeroMagicList.Count,
            "a rejected delete must not disturb the hero list");

        // An unknown name returns False after exhausting the loop (@0x73F7A2..0x73F7A6).
        Assert(!hero.DeleteHeroMagic("no-such-skill"),
            "unknown name returns False after the full scan (@0x73F7A6)");
        Equal(1, hero.m_HeroMagicList.Count, "failed delete left the list intact");

        // @0x73F6FB sub_40BD78 upper-cases the mismatching byte pair, i.e. the compare
        // is CASE-INSENSITIVE.
        hero = NewHeroWithMagic(21, "CaseFolded", out _);
        Assert(hero.DeleteHeroMagic("casefolded"),
            "name compare is case-insensitive (@0x73F6FB sub_40BD78 folds a-z)");

        // @0x73F79C `mov byte [ebp-1],1` then `jmp` to the epilogue: native stops after
        // the FIRST match, so a duplicate name loses exactly one entry per call. And
        // because native walks Count-1 DOWNTO 0 (@0x73F6C2 dec esi), the entry removed
        // first is the LAST one.
        hero = new HeroObject();
        var lowIndex = NewMagic(spell: 1, level: 0, defSpell: 0, trainLv: 1);
        lowIndex.MagicInfo.wMagicID = 31;
        lowIndex.MagicInfo.sMagicName = "dup";
        lowIndex.wMagIdx = 31;
        var highIndex = NewMagic(spell: 1, level: 0, defSpell: 0, trainLv: 1);
        highIndex.MagicInfo.wMagicID = 32;
        highIndex.MagicInfo.sMagicName = "dup";
        highIndex.wMagIdx = 32;
        hero.m_HeroMagicList.Add(lowIndex);
        hero.m_HeroMagicList.Add(highIndex);
        hero.m_MagicArr[31] = lowIndex;
        hero.m_MagicArr[32] = highIndex;

        Assert(hero.DeleteHeroMagic("dup"), "duplicate-name delete succeeds");
        Equal(1, hero.m_HeroMagicList.Count,
            "exactly ONE entry is removed per call (@0x73F7A0 jmp out after first match)");
        Assert(ReferenceEquals(hero.m_HeroMagicList[0], lowIndex),
            "native iterates Count-1 DOWNTO 0 (@0x73F6C2 dec esi), so the LAST "
            + "duplicate is removed first");
        Assert(hero.m_MagicArr[32] == null && hero.m_MagicArr[31] != null,
            "only the removed entry is cleared from the indexed magic array");
    }
    finally
    {
        M2Share.UserEngine = previousEngine;
    }
}

// ===========================================================================
// helpers
// ===========================================================================

ushort MpCost(int spell, byte level, byte defSpell, byte trainLv) =>
    TPlayObject.GetNativeMagicProducerMpCost(
        NewMagic(spell, level, defSpell, trainLv));

TUserMagic NewMagic(int spell, byte level, byte defSpell, byte trainLv) =>
    new TUserMagic
    {
        btLevel = level,
        MagicInfo = new TMagic
        {
            wMagicID = 50,
            sMagicName = "mp-fixture",
            wSpell = (ushort)spell,
            btDefSpell = defSpell,
            btTrainLv = trainLv
        }
    };

HeroObject NewHeroWithMagic(ushort magicId, string name, out TUserMagic owned)
{
    var hero = new HeroObject();
    owned = NewMagic(spell: 4, level: 0, defSpell: 1, trainLv: 3);
    owned.MagicInfo.wMagicID = magicId;
    owned.MagicInfo.sMagicName = name;
    owned.wMagIdx = magicId;
    hero.m_HeroMagicList.Add(owned);
    hero.m_MagicArr[magicId] = owned;
    return hero;
}

void Assert(bool condition, string label)
{
    checks++;
    if (!condition)
        throw new InvalidOperationException(label);
}

void Equal<T>(T expected, T actual, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

void PrepareRuntimeConfig()
{
    string runtime = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtime, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtime, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtime, "Command.conf"),
        "[Command]" + Environment.NewLine);
    string share = Path.Combine(Path.GetFullPath(
        Path.Combine(runtime, "..")), "Share");
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(share, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}
