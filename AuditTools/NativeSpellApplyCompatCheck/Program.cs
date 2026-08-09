// NativeSpellApplyCompatCheck — the spell-APPLY layer (cast gates, charm
// consumption, summon levels, delayed AoE delivery) against 战神 M2Server.exe.
//
// Binary of record: D:/loym2/staging/M2Server_reunpacked_20260803.exe
// (flat image, ImageBase 0x400000). Every constant asserted below carries the
// 战神 EA it was read from. Evidence doc: staging/spellapply_fix_20260804.md.
//
// Two prior claims this audit deliberately pins AGAINST, because a discovery
// doc got them wrong and following it would have shipped a bug:
//   * "native passes nExpLevel = literal 10 to MakeSlave" — FALSE. sub_6CB070
//     @0x6CB2F9-0x6CB302 writes the EFFECTIVE MAGIC LEVEL (ecx) to BOTH
//     m_btSlaveMakeLevel (+0x483) and m_btSlaveExpLevel (+0x482); the literal 10
//     is stack slot [ebp+8] and lands in the DWORD +0x48C, a percentage field
//     (sub_71E50C @0x71E706 does `HP = HP * [+0x48C] / 100`).
//   * "the generic CheckAmulet also decrements a fixed 100" — FALSE.
//     sub_73E93C @0x73E989 is `imul eax,[ebp-4],0x64` = nCount * 100. Only the
//     inline 施毒术 path @0x6ED986 uses a bare literal 100.

using System.Reflection;
using System.Text;
using GameSvr;
using SystemModule;

try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();
    InitializeRuntime();

    VerifyNoSkillZoneGate();
    VerifyAmuletCountPredicate();
    VerifySummonEffectiveLevels();
    VerifySourceContracts();

    Console.WriteLine(
        "PASS NativeSpellApplyCompatCheck " +
        "noskillzone=cellflag-then-idlist+prefetch-position " +
        "poison=slot9-only+literal100+free-last-cast+autoremove " +
        "amulet=ncount100-le-dura50+shape1or2 " +
        "summon=effective-level-both-fields " +
        "range=hardcoded9 aoe=600ms-category3-queue");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeSpellApplyCompatCheck FAIL: {exception}");
    return 1;
}

// ---------------------------------------------------------------------------
// Native sub_772A50 @0x772A5A-0x772A8E — the two-OR skill-forbid predicate,
// short-circuiting cell-flag FIRST. sub_77BE88 @0x77BE8D-0x77BEAB reads the
// byte at map[+0x38] + ((x*map[+0x40]+y)*12) + 4 and returns 0 (= ALLOWED) for
// every out-of-bounds coordinate (x<0 / y<0 / x>=Width[+0x3C] /
// y>=Height[+0x40]) — a deliberate fail-OPEN. sub_77BCF4 @0x77BD11-0x77BD19
// linear-scans the map's +0x28 int TList and returns TRUE (= DENY) on a hit.
static void VerifyNoSkillZoneGate()
{
    var envir = new Envirnoment();
    SetMapSize(envir, 8, 8);

    Equal(true, envir.IsSkillAllowedAt(3, 3, 17),
        "clean cell + empty ban list must allow (sub_772A50 bl=1 default)");

    // (a) the per-cell flag: any NON-ZERO byte denies (native tests `test al,al`
    //     @0x772A73, not a specific bit).
    SetMapCellSkillFlag(envir, 3, 3, 1);
    Equal(false, envir.IsSkillAllowedAt(3, 3, 17),
        "cell flag 1 must deny (sub_77BE88 -> jne @0x772A75)");
    SetMapCellSkillFlag(envir, 3, 3, 200);
    Equal(false, envir.IsSkillAllowedAt(3, 3, 17),
        "cell flag 200 must deny — the test is != 0, not a bit mask");
    SetMapCellSkillFlag(envir, 3, 3, 0);
    Equal(true, envir.IsSkillAllowedAt(3, 3, 17),
        "cell flag back to 0 must allow again");

    // out-of-bounds fails OPEN, matching sub_77BE88's four early `jl`/`jge`.
    Equal(true, envir.IsSkillAllowedAt(-1, 3, 17),
        "x<0 must fail OPEN (sub_77BE88 @0x77BE8F jl -> ecx stays 0)");
    Equal(true, envir.IsSkillAllowedAt(3, 999, 17),
        "y>=Height must fail OPEN (sub_77BE88 @0x77BE9A jge)");

    // (b) the per-map id list, keyed on the RAW wire skill index.
    envir.LimitSkillIds.Add(23);
    Equal(false, envir.IsSkillAllowedAt(3, 3, 23),
        "listed skill must deny (sub_77BCF4 @0x77BD14 cmp esi,[ecx+edx*4])");
    Equal(true, envir.IsSkillAllowedAt(3, 3, 24),
        "unlisted skill must still be allowed");

    // The cast path must consult the gate BEFORE resolving the skill, because
    // native calls sub_772A50 @0x6BC546 while GetMagicInfo (VMT+0xE8) is not
    // reached until @0x6BC5CB.
    string attack = ReadSource("GameSvr", "Players",
        "TPlayObject.Attack.cs");
    int gateIndex = attack.IndexOf("m_PEnvir.IsSkillAllowedAt(m_nCurrX",
        StringComparison.Ordinal);
    Assert(gateIndex >= 0,
        "ClientSpellXY must call IsSkillAllowedAt (native @0x6BC546)");
    int resolveIndex = attack.IndexOf("var UserMagic = GetMagicInfo(nKey);",
        StringComparison.Ordinal);
    Assert(resolveIndex >= 0, "ClientSpellXY must resolve the magic");
    Assert(gateIndex < resolveIndex,
        "the skill-forbid gate must run BEFORE GetMagicInfo — native order is " +
        "sub_772A50 @0x6BC546 then VMT+0xE8 @0x6BC5CB");
    int tickIndex = attack.IndexOf("HUtil32.GetTickCount() - m_dwMagicAttackTick",
        StringComparison.Ordinal);
    Assert(tickIndex < 0 || gateIndex < tickIndex,
        "the gate must also precede the interval bookkeeping " +
        "(native GetTickCount is @0x6BC597, after the gate)");
}

// ---------------------------------------------------------------------------
// Native sub_73E93C @0x73E989-0x73E999:
//   imul eax,[ebp-4],0x64   ; nCount * 100
//   mov  dx,[edi+0x26]      ; Dura
//   add  ecx,0x32           ; Dura + 50
//   cmp  eax,ecx ; jg fail  ; PASS iff nCount*100 <= Dura + 50
// The replaced C# predicate was HUtil32.Round(Dura/100.0) >= nCount, whose
// banker's rounding pushes the exact .5 case DOWN. The two forms differ at
// exactly Dura=50/nCount=1 and Dura=450/nCount=5 over Dura in [0,1200].
static void VerifyAmuletCountPredicate()
{
    // Both historically divergent points must now ALLOW the cast.
    Equal(true, CheckAmulet(dura: 50, shape: 5, count: 1, type: 1),
        "Dura=50,nCount=1 must pass: 100 <= 50+50 (native @0x73E997 cmp/jg)");
    Equal(true, CheckAmulet(dura: 450, shape: 5, count: 5, type: 1),
        "Dura=450,nCount=5 must pass: 500 <= 450+50");
    // One tick below each boundary must refuse.
    Equal(false, CheckAmulet(dura: 49, shape: 5, count: 1, type: 1),
        "Dura=49,nCount=1 must fail: 100 > 49+50");
    Equal(false, CheckAmulet(dura: 449, shape: 5, count: 5, type: 1),
        "Dura=449,nCount=5 must fail: 500 > 449+50");

    // Exhaustive equivalence against the native form, so a future refactor
    // cannot silently reintroduce a rounding-based predicate.
    for (int dura = 0; dura <= 1200; dura++)
    {
        foreach (int count in new[] { 1, 2, 5 })
        {
            bool expected = count * 100 <= dura + 50;
            Equal(expected, CheckAmulet(dura, 5, count, 1),
                $"native predicate at Dura={dura}, nCount={count}");
        }
    }

    // The type test: native `is TBujuk` (type 1) is StdMode 25 + Shape 5;
    // `is TPoisons` (type 2) is Shape in {1,2} ONLY. Shape 0 reaches a
    // DIFFERENT class at 0x74D12B in the item factory's Shape switch
    // (jumptab @0x74D07B, reached only from StdMode 25 via bytetab 0x74C374),
    // so Shape 0 must be refused by both arms.
    Equal(false, CheckAmulet(dura: 1000, shape: 0, count: 1, type: 2),
        "Shape 0 is not TPoisons (factory 0x74D07B sends it to 0x74D12B)");
    Equal(true, CheckAmulet(dura: 1000, shape: 1, count: 1, type: 2),
        "Shape 1 is TPoisons (0x74D0A8)");
    Equal(true, CheckAmulet(dura: 1000, shape: 2, count: 1, type: 2),
        "Shape 2 is TPoisons (0x74D0A8)");
    Equal(false, CheckAmulet(dura: 1000, shape: 5, count: 1, type: 2),
        "Shape 5 is TBujuk, not TPoisons");
    Equal(false, CheckAmulet(dura: 1000, shape: 1, count: 1, type: 1),
        "type 1 requires TBujuk (Shape 5)");

    // Slot discipline: native reads ONLY slot 9. Every spell-path caller passes
    // dl=9 (0x6ED949, 0x73E95F, 0x73EA50, 0x73CC53, 0x73E9D6, 0x73EBA8), so a
    // charm worn in U_ARMRINGL must NOT satisfy the gate.
    var player = NewPlayer();
    player.m_UseItems[Grobal2.U_ARMRINGL] = NewCharm(1000);
    RegisterStdItem(1, shape: 5);
    short index = -1;
    Equal(false, Magic.CheckAmulet(player, 1, 1, ref index),
        "U_ARMRINGL must never satisfy the charm gate — native only reads " +
        "slot 9 (sub_75EC20 with dl=9 at every spell-path call site)");
    Equal((short)0, index, "the out index must stay 0 when the gate refuses");
}

// ---------------------------------------------------------------------------
// Native summon producers sub_76EDFC (骷髅 17) / sub_76EE7C (神兽 30) /
// sub_76EEF4 (天使) each push `1 / 0xD2F00 / 0 / 0xA` then load
// `sub_4C896C -> cl` = the EFFECTIVE magic level, and TPlayer.MakeSlave
// (VMT+0xEC = sub_6CB070) writes that ecx byte to BOTH slave-level fields:
//   6CB2F9  mov al,byte [ebp-8]        ; ecx = effective level
//   6CB2FC  mov byte [esi+0x483],al    ; m_btSlaveMakeLevel
//   6CB302  mov byte [esi+0x482],al    ; m_btSlaveExpLevel
// Field ids come from GainSlaveExp sub_71F3D0 @0x71F427-0x71F442 and from
// TMonster.RecalcAbilitys sub_71DF70 (VMT+0x8C) reading +0x482 @0x71DFB3.
static void VerifySummonEffectiveLevels()
{
    // The helper the summon paths must use is sub_4C896C: btLevel + bonus,
    // clamped to btTrainLv (`mov dl,[eax+0xC]; add dl,[eax+0x18];
    // mov cl,[[eax]+0x1A]; cmp dl,cl; jbe`).
    var magic = MakeMagic(17, level: 2, trainLevel: 5);
    SetMagicLevelBonus(magic, 2);
    Equal(4, TPlayObject.GetNativeMagicProducerEffectiveLevel(magic),
        "effective level must add NativeLevelBonus (sub_4C896C @0x4C896F)");
    magic.MagicInfo.btTrainLv = 3;
    Equal(3, TPlayObject.GetNativeMagicProducerEffectiveLevel(magic),
        "effective level must clamp to btTrainLv (sub_4C896C @0x4C8977)");

    // Every summon site must pass the EFFECTIVE level, and must pass the SAME
    // value for makeLevel and expLevel — never the literal 10, never raw
    // btLevel. Enforced on the source because MakeSlave needs a live map.
    string manager = ReadSource("GameSvr", "Spells", "MagicManager.cs");
    foreach (string producer in new[]
             { "MagMakeSlave", "MagMakeSinSuSlave", "MagMakeAngelSlave" })
    {
        string body = MethodBody(manager, producer);
        Assert(body.Contains("GetNativeMagicProducerEffectiveLevel(UserMagic)",
                StringComparison.Ordinal),
            $"{producer} must derive the summon level from sub_4C896C " +
            "(native @0x76EE29 / @0x76EEA3 / @0x76EF21)");
        Assert(!body.Contains("= UserMagic.btLevel", StringComparison.Ordinal),
            $"{producer} must not use the RAW btLevel — native loads " +
            "sub_4C896C, so a +skill-level item would be dropped");
        Assert(!body.Contains("nExpLevel = 10", StringComparison.Ordinal),
            $"{producer} must NOT set nExpLevel = 10: the literal 0xA at " +
            "@0x76EE27 is stack slot [ebp+8] and lands in the DWORD +0x48C " +
            "(a percentage, sub_71E50C @0x71E706), not in a level field");
    }
}

// ---------------------------------------------------------------------------
static void VerifySourceContracts()
{
    string manager = ReadSource("GameSvr", "Spells", "MagicManager.cs");
    string magic = ReadSource("GameSvr", "Spells", "Magic.cs");

    // The no-skill-zone refusal MESSAGE CHANNEL. Native @0x6BC54F sends
    // `mov cx,0xFFDB` through vtable+0xD4 with the literal at 0x6BCD18. cx
    // unpacks as FColor = cx & 0xFF and BColor = cx >> 8 (see the playernotice
    // bridge in PasApiBridge), so 0xFFDB is the 0xDB/0xFF pair == MsgColor.Green
    // in GameSvrConfig. It was ported as MsgColor.Red (the 0x38FF pair), i.e.
    // the wrong channel; fixed 2026-08-08. Assert the derivation as well as the
    // call so the two cannot drift apart silently.
    var config = new GameSvrConfig();
    Equal(0xFFDB & 0xFF, (int)config.btGreenMsgFColor,
        "0xFFDB low byte must be btGreenMsgFColor (0xFFDB is the Green channel)");
    Equal((0xFFDB >> 8) & 0xFF, (int)config.btGreenMsgBColor,
        "0xFFDB high byte must be btGreenMsgBColor");
    string attack = ReadSource("GameSvr", "Players", "TPlayObject.Attack.cs");
    Require(attack,
        "SysMsg(\"当前区域不可使用该技能\", MsgColor.Green, MsgType.Hint);",
        "no-skill-zone refusal must use the 0xFFDB Green channel (@0x6BC54F), not Red");

    // Spell range: native sub_6ED62C @0x6ED67B is `cmp eax,9` for EVERY spell,
    // with no config read anywhere in the body.
    Require(manager, "const int magicAttackRange = 9;",
        "spell range must be the native hardcoded 9 (@0x6ED67B)");
    Reject(manager, "M2Share.g_Config.nMagicAttackRage",
        "spell range must not read config — native reads none");

    // 施毒术: literal 100, slot 9, and the free last cast. Native @0x6ED97C
    // `cmp word [eax+0x26],0x64; jb 0x6ED9B0` skips the decrement yet still
    // reaches the poison applier, so `Dura >= 100` must guard ONLY the
    // decrement — never the cast.
    string poison = SwitchCase(manager, "SpellsDef.SKILL_AMYOUNSUL");
    Require(poison, "poisonCharm.Dura -= 100;",
        "poison must decrement the native literal 100 (@0x6ED986)");
    Require(poison, "if (poisonCharm.Dura >= 100)",
        "the 100 decrement must be guarded by Dura >= 100 (@0x6ED97C jb)");
    Require(poison, "m_UseItems[Grobal2.U_BUJUK]",
        "poison must read slot 9 only (mov dl,9 @0x6ED949)");
    Reject(poison, "Magic.UseAmulet",
        "poison must NOT route through UseAmulet: that subtracts nCount*100 " +
        "(@0x73E989 imul ...,0x64) and drained charms 2x too fast");
    Reject(poison, "Magic.CheckAmulet",
        "poison must NOT route through CheckAmulet: native @0x6ED945 inlines " +
        "its own slot-9 fetch and TPoisons test, and CheckAmulet also " +
        "admitted U_ARMRINGL");
    Require(poison, "ConsumeSpentPoisonCharm(PlayObject, poisonCharm);",
        "the unconditional post-cast charm hook sub_73CC18 (@0x6ED9F0)");
    // The hook is unconditional in native, so it must sit AFTER the applier and
    // outside the AntiPoison roll.
    int applierIndex = poison.IndexOf("Grobal2.RM_POISON",
        StringComparison.Ordinal);
    int hookIndex = poison.IndexOf("ConsumeSpentPoisonCharm",
        StringComparison.Ordinal);
    Assert(applierIndex >= 0 && hookIndex > applierIndex,
        "sub_73CC18 runs after the poison applier (@0x6ED9EB follows the " +
        "VMT+0x110/+0x114 calls)");
    string hook = MethodBody(manager, "ConsumeSpentPoisonCharm");
    Require(hook, "charm.Dura >= 100",
        "sub_73CC18 only removes the charm once Dura < 100 (@0x73CC33 jae)");
    Require(hook, "PlayObject.m_UseItems[Grobal2.U_BUJUK] = null;",
        "sub_75F27C nulls the slot pointer (@0x75F2BB)");
    Require(hook, "PlayObject.RecalcAbilitys();",
        "sub_75F27C recalcs via VMT+0x8C (@0x75F2D9)");
    Reject(hook, "SysMsg",
        "\"持久耗尽\" is the reason column of the sub_768BE0 game-data log " +
        "(@0x75F328), NOT a player-visible message — do not invent one");

    // Generic UseAmulet keeps nCount*100 and must remove, not zero-and-keep.
    string useAmulet = MethodBody(magic, "UseAmulet");
    Require(useAmulet, "nCount * 100",
        "generic consume is nCount*100 (@0x73E989 imul eax,[ebp-4],0x64)");
    Require(useAmulet, "PlayObject.m_UseItems[Idx] = null;",
        "shortfall must remove via the sub_75F27C shape (@0x73E9DE)");
    Reject(useAmulet, "wIndex = 0",
        "native never zeroes wIndex in place — sub_75F27C nulls the slot");
    string checkAmulet = MethodBody(magic, "CheckAmulet");
    Require(checkAmulet, "nCount * 100 <= charm.Dura + 50",
        "native count predicate (@0x73E989-0x73E999)");
    Reject(checkAmulet, "HUtil32.Round",
        "the banker's-rounding predicate must stay deleted");
    Reject(checkAmulet, "U_ARMRINGL",
        "native reads only slot 9 (mov dl,9 @0x73E95F)");

    // Delayed AoE: 爆裂火焰 23 / 冰咆哮 33 queue a 600 ms category-3 effect and
    // apply nothing at cast time (sub_76F21C @0x76F26B push 0x258, @0x76F272
    // push 3, @0x76F270 push 1 = range).
    string blast = MethodBody(manager, "QueueNativeAreaBlast");
    Require(blast, "QueueNativeMagicEffect(3, null, rawDamage,",
        "AoE must queue dispatchCategory 3 with a nil target (@0x76F272 " +
        "push 3, @0x76F27E xor edx,edx)");
    Require(blast, "nTargetX, nTargetY, 1, true, 0,",
        "AoE range slot is 1 and arg0 is true (@0x76F270 / @0x76F27A)");
    Require(blast, "600);",
        "AoE delay is 600 ms (@0x76F26B push 0x258)");
    foreach (string skill in new[]
             { "SpellsDef.SKILL_FIREBOOM", "SpellsDef.SKILL_SNOWWIND" })
    {
        string body = SwitchCase(manager, skill);
        Require(body, "QueueNativeAreaBlast(PlayObject, UserMagic,",
            $"{skill} must use the native 600 ms queue");
        Reject(body, "MagBigExplosion",
            $"{skill} must not resolve damage at cast time — native applies " +
            "nothing until the 10177 receiver fires 600 ms later");
        Reject(body, "RM_MAGSTRUCK",
            $"{skill} must not take the legacy RM_MAGSTRUCK route, which " +
            "bypasses the category-3 branch of ResolveFullMagicDamage");
    }
}

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------

static bool CheckAmulet(int dura, byte shape, int count, int type)
{
    var player = NewPlayer();
    player.m_UseItems[Grobal2.U_BUJUK] = NewCharm(dura);
    RegisterStdItem(1, shape);
    short index = -1;
    return Magic.CheckAmulet(player, count, type, ref index);
}

static TUserItem NewCharm(int dura) => new TUserItem
{
    wIndex = 1,
    Dura = (ushort)dura,
    DuraMax = 20000,
    MakeIndex = 1,
};

static void RegisterStdItem(int index, byte shape)
{
    var items = M2Share.UserEngine.StdItemList;
    items.Clear();
    // GetStdItem(int) subtracts one unless the list carries the native
    // sentinel, so seed slot 0 as index 1.
    items.Add(new GoodItem
    {
        Name = "毒药",
        StdMode = 25,
        Shape = shape,
    });
}

static TPlayObject NewPlayer()
{
    var player = new TPlayObject();
    player.m_UseItems = new TUserItem[Grobal2.HUMAN_EQUIPPED_ITEM_COUNT];
    return player;
}

static void SetMapSize(Envirnoment envir, int width, int height)
{
    // Native sub_77BE88 indexes as (x * map[+0x40] + y) * 12 + 4 with the
    // bounds x<map[+0x3C], y<map[+0x40]; C# TryGetMapCellIndex is
    // `nX * wHeight + nY` guarded by wWidth/wHeight — the same shape.
    envir.wWidth = (short)width;
    envir.wHeight = (short)height;
    Set(envir, "MapCellSkillFlags", new byte[width * height]);
}

static void SetMapCellSkillFlag(Envirnoment envir, int x, int y, byte value)
{
    var flags = (byte[])Get(envir, "MapCellSkillFlags");
    var method = typeof(Envirnoment).GetMethod("TryGetMapCellIndex",
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TryGetMapCellIndex");
    object[] arguments = { x, y, 0 };
    if (!(bool)method.Invoke(envir, arguments))
        throw new InvalidOperationException($"cell ({x},{y}) out of range");
    flags[(int)arguments[2]] = value;
}

static void Set(object target, string name, object value)
{
    var type = target.GetType();
    const BindingFlags all = BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
    for (var t = type; t != null; t = t.BaseType)
    {
        var field = t.GetField(name, all);
        if (field != null) { field.SetValue(target, value); return; }
        var property = t.GetProperty(name, all);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, value);
            return;
        }
        var backing = t.GetField($"<{name}>k__BackingField", all);
        if (backing != null) { backing.SetValue(target, value); return; }
    }
    throw new MissingMemberException(type.FullName, name);
}

static object Get(object target, string name)
{
    const BindingFlags all = BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
    for (var t = target.GetType(); t != null; t = t.BaseType)
    {
        var field = t.GetField(name, all);
        if (field != null) return field.GetValue(target);
        var property = t.GetProperty(name, all);
        if (property != null) return property.GetValue(target);
    }
    throw new MissingMemberException(target.GetType().FullName, name);
}

static TUserMagic MakeMagic(ushort id, byte level, byte trainLevel = 3)
{
    return new TUserMagic
    {
        wMagIdx = id,
        btLevel = level,
        MagicInfo = new TMagic
        {
            wMagicID = id,
            btTrainLv = trainLevel,
            TrainLevel = new byte[] { 0, 0, 0, 0 },
            MaxTrain = new[] { 1000, 1000, 1000, 1000 },
        }
    };
}

static void SetMagicLevelBonus(TUserMagic magic, byte bonus)
{
    var field = typeof(TUserMagic).GetField("NativeLevelBonus",
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TUserMagic).FullName,
            "NativeLevelBonus");
    field.SetValue(magic, bonus);
}

// Extract a `case <label>:` arm so a Require/Reject is scoped to one spell
// instead of the whole 900-line switch. The arm ends at the first `break;`
// that is at the arm's own brace depth — a naive "first break;" would stop
// inside a nested switch (the 施毒术 arm contains one) and silently check only
// a prefix, which is how a Require can fail against code that is actually
// present.
static string SwitchCase(string source, string label)
{
    int start = source.IndexOf($"case {label}:", StringComparison.Ordinal);
    if (start < 0)
        throw new InvalidOperationException($"case {label} not found");
    int depth = 0;
    for (int i = start; i < source.Length; i++)
    {
        char c = source[i];
        if (c == '{') depth++;
        else if (c == '}')
        {
            depth--;
            if (depth < 0) return source[start..i];
        }
        else if (depth == 0 && c == 'b' &&
                 string.CompareOrdinal(source, i, "break;", 0, 6) == 0)
        {
            return source[start..(i + 6)];
        }
    }
    throw new InvalidOperationException($"case {label} has no break");
}

// Extract a method body by brace matching from its DECLARATION. Anchoring on
// the bare name would match a call site first (QueueNativeAreaBlast is invoked
// twice above its declaration) and then brace-match the enclosing method,
// producing a body that silently contains the wrong code.
static string MethodBody(string source, string name)
{
    int signature = -1;
    for (int probe = 0; ; )
    {
        int hit = source.IndexOf($" {name}(", probe, StringComparison.Ordinal);
        if (hit < 0) break;
        int lineStart = source.LastIndexOf('\n', hit) + 1;
        string prefix = source[lineStart..hit];
        if (prefix.Contains("void", StringComparison.Ordinal) ||
            prefix.Contains("bool", StringComparison.Ordinal) ||
            prefix.Contains("int", StringComparison.Ordinal) ||
            prefix.Contains("static", StringComparison.Ordinal))
        {
            signature = hit;
            break;
        }
        probe = hit + 1;
    }
    if (signature < 0)
        throw new InvalidOperationException(
            $"method {name} declaration not found");
    int open = source.IndexOf('{', signature);
    if (open < 0)
        throw new InvalidOperationException($"method {name} has no body");
    int depth = 0;
    for (int i = open; i < source.Length; i++)
    {
        if (source[i] == '{') depth++;
        else if (source[i] == '}')
        {
            depth--;
            if (depth == 0) return source[open..(i + 1)];
        }
    }
    throw new InvalidOperationException($"method {name} body unterminated");
}

static string ReadSource(params string[] parts)
{
    string path = Path.Combine(RepositoryRoot(), Path.Combine(parts));
    if (!File.Exists(path))
        throw new FileNotFoundException(path);
    return File.ReadAllText(path);
}

static string RepositoryRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "GameSvr",
                "GameSvr.csproj")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("GameSvr/GameSvr.csproj");
}

static void PrepareRuntimeConfig()
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

static void InitializeRuntime()
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

static void Require(string source, string value, string label) =>
    Assert(source.Contains(value, StringComparison.Ordinal), label);

static void Reject(string source, string value, string label) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), label);

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
    }
}

static void Assert(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
}
