using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();
VerifyEffectiveLevel();
VerifyDamageFormula();
VerifyStatePackets();
VerifyStatusLifecycle();
VerifyOneShotBonusAndConsumption();
VerifySourceWiring();

Console.WriteLine(
    "PASS native-skill152 level=1..3 jobs=DC/MC/SC job3=isolated " +
    "status=30s+strict250ms packet=3556/4367+8bytes " +
    "one-shot=strict10s+skills1/35/59/232+positive-consume");
return;

static void VerifyEffectiveLevel()
{
    Equal((byte)2, EffectiveLevel(1, 1, 3),
        "native level bonus was ignored");
    Equal((byte)3, EffectiveLevel(2, 2, 3),
        "training cap was ignored");
    Equal((byte)1, EffectiveLevel(255, 2, 3),
        "native level byte addition did not wrap");
    Equal((byte)0, EffectiveLevel(1, 0, 0),
        "zero training cap was ignored");
}

static void VerifyDamageFormula()
{
    Assert(Calculate(0, 1, Stat(123), Stat(999), Stat(888),
        out int warrior), "warrior level1 rejected");
    Equal(600, warrior, "warrior level1 truncation");

    Assert(Calculate(1, 2, Stat(999), Stat(123), Stat(888),
        out int wizard), "wizard level2 rejected");
    Equal(1220, wizard, "wizard level2 truncation");

    Assert(Calculate(2, 3, Stat(999), Stat(888), Stat(4999),
        out int taoist), "taoist level3 rejected");
    Equal(99980, taoist, "taoist level3");

    Assert(Calculate(0, 3, Stat(6000), 0, 0, out int capped),
        "5000 cap rejected");
    Equal(100000, capped, "5000 cap");

    Assert(Calculate(0, 1, Stat(3, 65535), 0, 0,
        out int lowerIgnored), "packed DC rejected");
    Equal(0, lowerIgnored, "lower word leaked into main stat");

    Assert(!Calculate(0, 0, Stat(100), 0, 0, out _),
        "level0 admitted");
    Assert(!Calculate(0, 4, Stat(100), 0, 0, out _),
        "level4 admitted");
    Assert(!Calculate(3, 3, Stat(100), Stat(200), Stat(300),
        out int fourthJob), "job3 guessed an absent carrier");
    Equal(0, fourthJob, "job3 failure leaked damage");
}

static void VerifyStatePackets()
{
    var player = BuildStatePacket(false, 30000);
    Equal((ushort)3556, player.Header.Ident, "player status command");
    Equal(30000, player.Header.Recog, "player status remaining");
    Equal((ushort)0, player.Header.Param, "player status param");
    Equal((ushort)1, player.Header.Tag, "player status tag");
    Equal((ushort)152, player.Header.Series, "player status type");
    Equal(8, player.Body.Length, "player status body length");
    Equal(152, BitConverter.ToInt32(player.Body, 0),
        "player status body type");
    Equal(30000, BitConverter.ToInt32(player.Body, 4),
        "player status body remaining");

    var hero = BuildStatePacket(true, 30000);
    Equal((ushort)4367, hero.Header.Ident, "hero status command");
    Assert(hero.Body.SequenceEqual(player.Body),
        "hero status body diverged from player");

    var removed = BuildStatePacket(false, 0);
    Equal(0, removed.Header.Recog, "removed status remaining");
    Equal(152, BitConverter.ToInt32(removed.Body, 0),
        "removed status body type");
    Equal(0, BitConverter.ToInt32(removed.Body, 4),
        "removed status body remaining");
}

static void VerifyStatusLifecycle()
{
    var actor = new TBaseObject();
    SetField(actor, "m_nNativeSkill152CooldownRemaining", 30000);
    SetField(actor, "m_dwNativeSkill152StatusProcessTick", 1000);
    SetField(actor, "m_nNativeOneShotMagicDamage", 100);

    ProcessStatus(actor, 11000);
    Equal(20000, GetField<int>(actor,
        "m_nNativeSkill152CooldownRemaining"),
        "status exact10s remaining");
    Equal(100, GetField<int>(actor, "m_nNativeOneShotMagicDamage"),
        "one-shot cleared at elapsed==10000");

    ProcessStatus(actor, 11250);
    Equal(20000, GetField<int>(actor,
        "m_nNativeSkill152CooldownRemaining"),
        "status processed at elapsed==250");

    ProcessStatus(actor, 11251);
    Equal(19749, GetField<int>(actor,
        "m_nNativeSkill152CooldownRemaining"),
        "status strict250 decrement");
    Equal(0, GetField<int>(actor, "m_nNativeOneShotMagicDamage"),
        "one-shot survived elapsed>10000");

    ProcessStatus(actor, 31001);
    Equal(0, GetField<int>(actor,
        "m_nNativeSkill152CooldownRemaining"),
        "30s cooldown did not expire");
}

static void VerifyOneShotBonusAndConsumption()
{
    var actor = new TBaseObject();
    SetField(actor, "m_nNativeOneShotMagicDamage", 250);
    foreach (int skillId in new[] { 1, 35, 59, 232 })
        Equal(350, ApplyBonus(actor, skillId, 100),
            $"skill{skillId} bonus");
    Equal(100, ApplyBonus(actor, 34, 100), "unlisted skill bonus");
    Equal(250, ApplyBonus(actor, 1, 0), "zero damage addition");
    Equal(249, ApplyBonus(actor, 1, -1), "negative damage addition");
    SetField(actor, "m_nNativeOneShotMagicDamage", 1);
    Equal(int.MinValue, ApplyBonus(actor, 1, int.MaxValue),
        "one-shot addition must wrap as native Int32");
    SetField(actor, "m_nNativeOneShotMagicDamage", 250);

    Consume(actor, 34);
    Equal(250, GetField<int>(actor, "m_nNativeOneShotMagicDamage"),
        "unlisted skill consumed one-shot");
    Consume(actor, 1);
    Equal(0, GetField<int>(actor, "m_nNativeOneShotMagicDamage"),
        "listed skill did not consume one-shot");
}

static void VerifySourceWiring()
{
    string root = FindRepoRoot();
    string skill = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeSkill152.cs"));
    Contains(skill, "还需要{seconds}秒才能释放该技能",
        "cooldown text");
    Contains(skill, "进入绝杀之意状态，下次攻击会造成额外伤害",
        "success text");
    Contains(skill, "绝杀之意状态消失", "expiry text");
    Contains(skill, "(uint)m_nNativeSkill152CooldownRemaining",
        "unsigned cooldown seconds");
    Contains(skill, "oneShotElapsed > NativeSkill152OneShotMilliseconds",
        "strict10s gate");
    Contains(skill, "skillId is 1 or 35 or 59 or 232",
        "one-shot skill whitelist");
    Contains(skill,
        "unchecked((byte)(magic.btLevel + magic.NativeLevelBonus))",
        "effective-level byte-wrap contract");
    Contains(skill, "m_nNativeOneShotMagicDamage > 0 &&",
        "positive stored one-shot gate");
    Assert(!skill.Contains("damage > 0 &&",
        StringComparison.Ordinal),
        "native one-shot consumer has no current-damage gate");

    string damage = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeMagicDamage.cs"));
    int hq = damage.IndexOf("ApplyNativeHumanHqReduction(",
        StringComparison.Ordinal);
    int bonus = damage.IndexOf("ApplyNativeSkill152OneShotBonus(",
        StringComparison.Ordinal);
    int breakBonus = damage.IndexOf(
        "damage = unchecked(damage + breakBonus);",
        StringComparison.Ordinal);
    int skill151 = damage.IndexOf("ApplyNativeSkill151BurstDamage(",
        StringComparison.Ordinal);
    int skill154 = damage.IndexOf("ApplyNativeSkill154BurstDamage(",
        StringComparison.Ordinal);
    int cap = damage.IndexOf("ApplyNativeState16MagicDamageCap(",
        StringComparison.Ordinal);
    int contest = damage.IndexOf("ApplyNativeState16LevelContest(",
        StringComparison.Ordinal);
    int shield153 = damage.IndexOf(
        "ApplyNativeSkill153ShieldToMagicDamage(", StringComparison.Ordinal);
    Assert(hq >= 0 && breakBonus > hq && bonus > breakBonus &&
        skill151 > bonus && skill154 > skill151 && cap > skill154 &&
        contest > cap &&
        shield153 > contest,
        "one-shot resolver order differs from target human VMT+0xF4");
    Assert(!damage.Contains("(effectiveFlags & 0x0C) == 0",
        StringComparison.Ordinal),
        "flags 4/8 must not bypass the native Skill152 consumer");

    string effects = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeState26Effects.cs"));
    // The one-shot reset is sub_772468 (`sub dx,1 / -0x22 / -0x18 / -0xAD`
    // = ids 1, 35, 59, 232, then `mov [eax+0x3F0],0`). An E8 scan of the
    // whole image finds exactly THREE call sites: 0x76DEB1 in sub_76DE1C
    // (single), 0x76E0A3 in sub_76DF5C (line) and 0x76E24E in sub_76E0B4
    // (area). The DIRECT carrier sub_76E268 (0x76E268-0x76E377) contains
    // none, so the count is three call sites plus the declaration.
    // Previously this asserted 5 and the direct carrier carried a call
    // that native does not make.
    Equal(4, Count(effects,
        "ConsumeNativeOneShotMagicDamage("),
        "single/line/area consumption plus declaration count");
    Assert(!Regex.IsMatch(effects,
        @"ApplyNativeDirectMagicEffect[\s\S]{0,1600}?ConsumeNativeOneShotMagicDamage\("),
        "sub_76E268 has no call to sub_772468; the direct carrier must not "
        + "reset the one-shot damage");
    Contains(effects, "if (positiveCount > 0)",
        "batch positive consumption gate");

    string timed = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.TimedAbility.cs"));
    Contains(timed, "ProcessNativeSkill152Status(now);",
        "status run hook");
    Contains(timed, "ClearNativeSkill152StateOnExit();",
        "status exit cleanup");

    string manager = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Spells", "MagicManager.cs"));
    Contains(manager, "case SpellsDef.SKILL_152:",
        "player producer dispatch");
    int case152 = manager.IndexOf("case SpellsDef.SKILL_152:",
        StringComparison.Ordinal);
    int case152End = manager.IndexOf("break;", case152,
        StringComparison.Ordinal);
    Assert(case152 >= 0 && case152End > case152 &&
        !manager.Substring(case152, case152End - case152)
            .Contains("boTrain = true", StringComparison.Ordinal),
        "native skill152 incorrectly trains on success");

    string player = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.cs"));
    int mpDebit = player.IndexOf("DamageSpell(nSpellPoint);",
        StringComparison.Ordinal);
    int playerDispatch = player.IndexOf(
        "M2Share.MagicManager.DoSpell(this, UserMagic",
        StringComparison.Ordinal);
    Assert(mpDebit >= 0 && playerDispatch > mpDebit,
        "player MP is not debited before native dispatch");

    int rangeGate = manager.IndexOf(
        "Math.Abs(PlayObject.m_nCurrX - nTargetX)",
        StringComparison.Ordinal);
    int spellAnimation = manager.IndexOf("SendNativeSpell(", rangeGate,
        StringComparison.Ordinal);
    int activation = manager.IndexOf("TryActivateNativeSkill152(",
        StringComparison.Ordinal);
    int fireAnimation = manager.IndexOf("SendNativeMagicFire(",
        activation, StringComparison.Ordinal);
    Assert(rangeGate >= 0 && spellAnimation > rangeGate &&
        activation > spellAnimation && fireAnimation > activation,
        "distance/spell/activation/fire ordering diverged");

    string hero = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "HeroObject.cs"));
    Contains(hero, "TryActivateNativeSkill152(userMagic, dwCurTick)",
        "hero producer dispatch");
    int heroMpDebit = hero.IndexOf("m_WAbil.MP -= spellPoint;",
        StringComparison.Ordinal);
    int heroActivation = hero.IndexOf(
        "TryActivateNativeSkill152(userMagic, dwCurTick)",
        StringComparison.Ordinal);
    Assert(heroMpDebit >= 0 && heroActivation > heroMpDebit,
        "hero MP is not debited before native activation");
}

static byte EffectiveLevel(byte level, byte bonus, byte trainingCap)
{
    var magic = new TUserMagic
    {
        btLevel = level,
        MagicInfo = new TMagic { btTrainLv = trainingCap }
    };
    typeof(TUserMagic).GetField("NativeLevelBonus",
        BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(magic,
        bonus);
    return (byte)(Method("GetNativeSkill152EffectiveLevel",
        BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
        new object[] { magic }) ?? byte.MaxValue);
}

static bool Calculate(byte job, byte level, int dc, int mc, int sc,
    out int damage)
{
    var method = Method("TryCalculateNativeSkill152Damage",
        BindingFlags.Static | BindingFlags.NonPublic);
    object[] args = { job, level, dc, mc, sc, 0 };
    bool result = (bool)(method.Invoke(null, args) ?? false);
    damage = (int)args[5];
    return result;
}

static (ClientPacket Header, byte[] Body) BuildStatePacket(bool hero,
    int remaining)
{
    var method = Method("BuildNativeSkill152StatePacket",
        BindingFlags.Static | BindingFlags.NonPublic);
    object value = method.Invoke(null, new object[] { hero, remaining })
        ?? throw new InvalidOperationException("state packet result");
    Type tupleType = value.GetType();
    var header = (ClientPacket)(tupleType.GetField("Item1")?.GetValue(value)
        ?? throw new InvalidOperationException("state packet header"));
    var body = (byte[])(tupleType.GetField("Item2")?.GetValue(value)
        ?? throw new InvalidOperationException("state packet body"));
    return (header, body);
}

static int ApplyBonus(TBaseObject actor, int skillId, int damage)
{
    return (int)(Method("ApplyNativeSkill152OneShotBonus",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(actor,
        new object[] { skillId, damage }) ?? int.MinValue);
}

static void Consume(TBaseObject actor, ushort skillId)
{
    Method("ConsumeNativeOneShotMagicDamage",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(actor,
        new object[] { skillId });
}

static void ProcessStatus(TBaseObject actor, int now)
{
    Method("ProcessNativeSkill152Status",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(actor,
        new object[] { now });
}

static MethodInfo Method(string name, BindingFlags flags)
{
    return typeof(TBaseObject).GetMethod(name, flags)
        ?? throw new MissingMethodException(name);
}

static int Stat(ushort high, ushort low = 0)
{
    return unchecked((int)((uint)high << 16 | low));
}

static void SetField<T>(TBaseObject actor, string name, T value)
{
    var field = typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name);
    field.SetValue(actor, value);
}

static T GetField<T>(TBaseObject actor, string name)
{
    var field = typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name);
    return (T)field.GetValue(actor);
}

static int Count(string text, string value)
{
    int count = 0;
    for (int index = 0;;)
    {
        index = text.IndexOf(value, index, StringComparison.Ordinal);
        if (index < 0)
            return count;
        count++;
        index += value.Length;
    }
}

static void Contains(string text, string value, string name)
{
    Assert(text.Contains(value, StringComparison.Ordinal), name);
}

static string FindRepoRoot()
{
    string workingDirectory = Directory.GetCurrentDirectory();
    if (File.Exists(Path.Combine(workingDirectory, "LyoMir2.sln")))
        return workingDirectory;

    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")) &&
            Directory.Exists(Path.Combine(directory.FullName, "GameSvr")) &&
            Directory.Exists(Path.Combine(directory.FullName, "AuditTools")))
        {
            return directory.FullName;
        }
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("repository root");
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

static void PrepareRuntimeConfig()
{
    var directory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
    File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
    File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
    var share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]");
    File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{name}: expected={expected}, actual={actual}");
    }
}

static void Assert(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
}
