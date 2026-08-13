using System.Buffers.Binary;
using GameSvr;
using GameSvr.Services;
using SystemModule;

try
{
    VerifyExactTypedPublication();
    VerifyIndependentStreamsAndFirstMatch();
    VerifyHighCapDatabaseJobAndTMagicIsolation();
    VerifyNativeNameCapacityAndImmutability();
    VerifyUserEngineAtomicPublication();
    VerifyProductionWiringSourceBoundaries();
    Console.WriteLine(
        "PASS NativeType2MagicRuntimePublishCheck " +
        "streams=101/102-independent fields=60-byte-exact " +
        "order=preserved duplicates=preserved lookup=linear-first " +
        "name=GBK-15-byte database-job=pre-correction " +
        "tmagic=independent+wire76 high-cap=255/99 " +
        "readiness=both-complete user-engine=atomic-once " +
        "routing=hero-five+codec+actor+pas-two+yanshen-hero-cast " +
        "startup=fail-closed+native-only+idempotent-start");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeType2MagicRuntimePublishCheck FAIL: {exception}");
    return 1;
}

static void VerifyExactTypedPublication()
{
    var state = new NativeType2MagicSnapshotState();
    var humanPacket = MagicPacket(
        NativeType2MagicSnapshotState.HumanMagicCommand,
        "火球A", 777, 1,
        effectType: 0x21, effect: 0x22, spell: 0x23,
        power: 0x24, maxPower: 0x25,
        defaultSpell: 0x26, defaultPower: 0x27,
        defaultMaxPower: 0x28, trainingCap: 0x29,
        needLevels: new byte[] { 1, 2, 3, 4, 5 },
        levelTraining: new[] { -1, 2, -3, 0x10203040 },
        delay: 900, coldMilliseconds: 123456,
        spellMilliseconds: -7654321);
    state.Consume(humanPacket);

    var heroPacket = MagicPacket(
        NativeType2MagicSnapshotState.HeroMagicCommand,
        "HeroSkill", 778, 1,
        effectType: 0x31, effect: 0x32, spell: 0x33,
        power: 0x34, maxPower: 0x35,
        defaultSpell: 0x36, defaultPower: 0x37,
        defaultMaxPower: 0x38, trainingCap: 0x39,
        needLevels: new byte[] { 11, 12, 13, 14, 15 },
        levelTraining: new[] { 100, 200, 300, 400 },
        delay: 1200, coldMilliseconds: 0, spellMilliseconds: 0);
    state.Consume(heroPacket);

    var catalog = new NativeType2MagicRuntimeCatalog();
    catalog.Publish(state);
    Assert(catalog.Ready, "both completed readiness");
    Equal((byte)3, catalog.CompletionFlags, "completion flags");

    var human = catalog.HumanDefinitions.Single();
    Equal("火球A", human.Name, "human GBK name");
    Equal((ushort)777, human.MagicId, "human id");
    Equal((byte)0x21, human.EffectType, "human effect type");
    Equal((byte)0x22, human.Effect, "human effect");
    Equal((byte)0x23, human.Spell, "human spell");
    Equal((byte)0x24, human.Power, "human power");
    Equal((byte)0x25, human.MaxPower, "human max power");
    Equal((byte)0x26, human.DefaultSpell, "human default spell");
    Equal((byte)0x27, human.DefaultPower, "human default power");
    Equal((byte)0x28, human.DefaultMaxPower,
        "human default max power");
    Equal((byte)0x29, human.DatabaseJob, "human DatabaseJob");
    Equal((byte)0x29, human.TrainingCap, "human training cap");
    EqualSequence(new byte[] { 1, 2, 3, 4, 5 },
        human.NeedLevels, "human need levels");
    EqualSequence(new[] { -1, 2, -3, 0x10203040 },
        human.LevelTraining, "human level training");
    Equal(900, human.Delay, "human delay");
    Equal(123456, human.ColdMilliseconds, "human cold milliseconds");
    Equal(-7654321, human.SpellMilliseconds,
        "human spell milliseconds");

    var hero = catalog.HeroDefinitions.Single();
    Equal("HeroSkill", hero.Name, "hero name");
    Equal((ushort)778, hero.MagicId, "hero id");
    Equal((byte)0x39, hero.DatabaseJob, "hero DatabaseJob");
    Equal((byte)0x39, hero.TrainingCap, "hero training cap");
    Equal((byte)255, hero.NeedLevel5, "hero NeedLv5 correction");
    Equal(-1, hero.LevelTraining4, "hero LvTrain4 correction");
    Equal(1200, hero.Delay, "hero delay");
    Equal(0, hero.ColdMilliseconds, "hero producer cold milliseconds");
    Equal(0, hero.SpellMilliseconds,
        "hero producer spell milliseconds");

    humanPacket[NativeType2MagicSnapshotState.HeaderSize + 0x12] = 0;
    var nativeCopy = human.CopyNativeRecord();
    nativeCopy[0x12] = 0;
    Equal((byte)0x21, human.EffectType, "published deep copy");
    Equal((byte)0x21, human.CopyNativeRecord()[0x12],
        "native record copy isolation");

    var magic = human.CreateTMagic();
    Equal((ushort)777, magic.wMagicID, "TMagic id");
    Equal("火球A", magic.sMagicName, "TMagic name");
    Equal((byte)0x21, magic.btEffectType, "TMagic effect type");
    Equal((byte)0x22, magic.btEffect, "TMagic effect");
    Equal((ushort)0x23, magic.wSpell, "TMagic spell zero extension");
    Equal((ushort)0x24, magic.wPower, "TMagic power zero extension");
    Equal((ushort)0x25, magic.wMaxPower,
        "TMagic max power zero extension");
    Equal((byte)0x26, magic.btDefSpell, "TMagic default spell");
    Equal((byte)0x27, magic.btDefPower, "TMagic default power");
    Equal((byte)0x28, magic.btDefMaxPower,
        "TMagic default max power");
    Equal((byte)0x29, magic.btTrainLv, "TMagic training cap");
    Equal((byte)0x29, magic.btJob, "TMagic DatabaseJob");
    EqualSequence(new byte[] { 1, 2, 3, 4 }, magic.TrainLevel,
        "TMagic four need levels");
    EqualSequence(new[] { -1, 2, -3, 0x10203040 }, magic.MaxTrain,
        "TMagic four level training values");
    Equal((byte)5, magic.NeedLevel5, "TMagic managed NeedLevel5");
    Equal(900, magic.dwDelayTime, "TMagic delay");
    Equal(123456, magic.ColdMilliseconds,
        "TMagic managed cold milliseconds");
    Equal(-7654321, magic.SpellMilliseconds,
        "TMagic managed spell milliseconds");
    Equal(string.Empty, magic.sDescr, "TMagic native description default");

    var wireA = human.CreateTMagic();
    var wireB = human.CreateTMagic();
    wireB.NeedLevel5 = 0xFE;
    wireB.ColdMilliseconds = int.MinValue;
    wireB.SpellMilliseconds = int.MaxValue;
    Equal(TMagic.RecordSize, wireA.GetBuffer().Length,
        "TMagic serialized record size");
    EqualSequence(wireA.GetBuffer(), wireB.GetBuffer(),
        "managed native fields excluded from 76-byte serializer");
}

static void VerifyIndependentStreamsAndFirstMatch()
{
    var state = new NativeType2MagicSnapshotState();
    state.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HumanMagicCommand,
        "Fire", 0, 0, effectType: 1));
    state.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HumanMagicCommand,
        "fire", 0, 1, effectType: 2));

    var catalog = new NativeType2MagicRuntimeCatalog();
    catalog.Publish(state);
    Assert(catalog.HumanCompleted && !catalog.HeroCompleted,
        "human-only completion");
    Assert(!catalog.Ready, "premature readiness");
    EqualSequence(new byte[] { 1, 2 },
        catalog.HumanDefinitions.Select(item => item.EffectType).ToArray(),
        "human order and duplicates");
    Equal((byte)1, catalog.FindHumanById(0).EffectType,
        "human ID first match including zero");
    Equal((byte)1, catalog.FindHumanByName("FIRE").EffectType,
        "human case-insensitive first name match");
    EqualSequence(new byte[] { 1, 2 },
        catalog.CreateHumanMagicList()
            .Select(item => item.btEffectType).ToArray(),
        "TMagic list preserves duplicate order");

    state.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HeroMagicCommand,
        "Fire", 0, 1, effectType: 9));
    catalog.Publish(state);
    Assert(catalog.Ready, "both-stream readiness");
    Equal((byte)9, catalog.FindHeroById(0).EffectType,
        "hero list independent from human");
    Equal((byte)1, catalog.FindHumanByName("fire").EffectType,
        "hero publication did not replace human list");
    Assert(catalog.FindHeroByName("missing") == null,
        "missing hero name lookup");
}

static void VerifyHighCapDatabaseJobAndTMagicIsolation()
{
    var state = new NativeType2MagicSnapshotState();
    state.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HumanMagicCommand,
        "Human60", 60, 1, trainingCap: 2,
        needLevels: new byte[] { 10, 20, 30, 40, 50 },
        levelTraining: new[] { 100, 200, 300, 400 }));
    state.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HeroMagicCommand,
        "Hero69", 69, 1, trainingCap: 1,
        needLevels: new byte[] { 11, 21, 31, 41, 51 },
        levelTraining: new[] { 101, 201, 301, 401 }));

    var catalog = new NativeType2MagicRuntimeCatalog();
    catalog.Publish(state);
    var human = catalog.HumanDefinitions.Single();
    var hero = catalog.HeroDefinitions.Single();

    Equal((byte)2, human.DatabaseJob,
        "human high-cap raw DatabaseJob");
    Equal(byte.MaxValue, human.TrainingCap,
        "human id60 corrected cap");
    Equal(byte.MaxValue, human.CopyNativeRecord()[0x1A],
        "human corrected native byte");
    var humanMagic = human.CreateTMagic();
    Equal((byte)2, humanMagic.btJob,
        "human TMagic raw DatabaseJob");
    Equal(byte.MaxValue, humanMagic.btTrainLv,
        "human TMagic corrected cap");

    Equal((byte)1, hero.DatabaseJob,
        "hero high-cap raw DatabaseJob");
    Equal((byte)99, hero.TrainingCap, "hero id69 corrected cap");
    var heroMagic = hero.CreateTMagic();
    Equal((byte)1, heroMagic.btJob, "hero TMagic raw DatabaseJob");
    Equal((byte)99, heroMagic.btTrainLv,
        "hero TMagic corrected cap");
    Equal(byte.MaxValue, heroMagic.NeedLevel5,
        "hero TMagic corrected NeedLevel5");
    Equal(-1, heroMagic.MaxTrain[3],
        "hero TMagic corrected LvTrain4");

    var firstHumanList = catalog.CreateHumanMagicList();
    var secondHumanList = catalog.CreateHumanMagicList();
    Assert(!ReferenceEquals(firstHumanList, secondHumanList)
           && !ReferenceEquals(firstHumanList[0], secondHumanList[0])
           && !ReferenceEquals(firstHumanList[0].TrainLevel,
               secondHumanList[0].TrainLevel)
           && !ReferenceEquals(firstHumanList[0].MaxTrain,
               secondHumanList[0].MaxTrain),
        "TMagic list factory aliases mutable state");
    firstHumanList[0].TrainLevel[0] = byte.MaxValue;
    firstHumanList[0].MaxTrain[0] = int.MinValue;
    firstHumanList[0].btTrainLv = 0;
    Equal((byte)10, human.NeedLevel1,
        "TMagic mutation changed immutable definition need level");
    Equal(100, human.LevelTraining1,
        "TMagic mutation changed immutable definition training");
    Equal(byte.MaxValue, human.TrainingCap,
        "TMagic mutation changed immutable definition cap");
    Equal((byte)10, secondHumanList[0].TrainLevel[0],
        "TMagic lists share need-level array");
    Equal(100, secondHumanList[0].MaxTrain[0],
        "TMagic lists share training array");
}

static void VerifyNativeNameCapacityAndImmutability()
{
    var state = new NativeType2MagicSnapshotState();
    var packet = MagicPacket(
        NativeType2MagicSnapshotState.HumanMagicCommand,
        "123456789012345", 901, 1);
    packet[NativeType2MagicSnapshotState.HeaderSize] = 31;
    state.Consume(packet);

    var catalog = new NativeType2MagicRuntimeCatalog();
    catalog.Publish(state);
    var definition = catalog.HumanDefinitions.Single();
    Equal("123456789012345", definition.Name,
        "native 15-byte name capacity");
    Equal(15, definition.CopyNameBytes().Length,
        "native name bytes limited to capacity");

    var nameCopy = definition.CopyNameBytes();
    nameCopy[0] = 0;
    Equal((byte)'1', definition.CopyNameBytes()[0],
        "name bytes copy isolation");

    state.Reset();
    Assert(catalog.HumanDefinitions.Count == 1 && catalog.HumanCompleted,
        "published view changed without publication");
    catalog.Publish(state);
    Assert(catalog.HumanDefinitions.Count == 0 && !catalog.HumanCompleted,
        "replacement publication after reset");
}

static void VerifyUserEngineAtomicPublication()
{
    var engine = new UserEngine();
    var oldHuman = new TMagic
    {
        wMagicID = 900,
        sMagicName = "OldHuman",
        btEffectType = 0x11
    };
    var oldHero = new TMagic
    {
        wMagicID = 901,
        sMagicName = "OldHero",
        btEffectType = 0x12
    };
    engine.m_MagicList.Add(oldHuman);
    engine.m_HeroMagicList.Add(oldHero);
    var oldHumanList = engine.m_MagicList;
    var oldHeroList = engine.m_HeroMagicList;

    var incompleteState = new NativeType2MagicSnapshotState();
    incompleteState.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HumanMagicCommand,
        "DualSkill", 69, 1, effectType: 0x41, trainingCap: 2));
    var incompleteCatalog = new NativeType2MagicRuntimeCatalog();
    incompleteCatalog.Publish(incompleteState);
    Assert(!engine.TryPublishNativeMagicDefinitions(
            incompleteCatalog, out var error)
           && !string.IsNullOrEmpty(error),
        "incomplete native double table was published");
    Assert(!engine.NativeMagicDefinitionsPublished,
        "incomplete publication set the one-shot flag");
    Assert(ReferenceEquals(oldHumanList, engine.m_MagicList)
           && ReferenceEquals(oldHeroList, engine.m_HeroMagicList),
        "incomplete publication changed a definition-list reference");
    Assert(engine.m_MagicList.Count == 1
           && ReferenceEquals(oldHuman, engine.m_MagicList[0])
           && engine.m_HeroMagicList.Count == 1
           && ReferenceEquals(oldHero, engine.m_HeroMagicList[0]),
        "incomplete publication changed an old definition table");

    var completeState = new NativeType2MagicSnapshotState();
    completeState.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HumanMagicCommand,
        "DualSkill", 69, 1, effectType: 0x41, trainingCap: 2));
    completeState.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HeroMagicCommand,
        "DualSkill", 69, 1, effectType: 0x91, trainingCap: 1));
    var completeCatalog = new NativeType2MagicRuntimeCatalog();
    completeCatalog.Publish(completeState);
    Assert(engine.TryPublishNativeMagicDefinitions(
            completeCatalog, out error), error);
    Assert(engine.NativeMagicDefinitionsPublished,
        "complete publication did not set the one-shot flag");

    var publishedHumanList = engine.m_MagicList;
    var publishedHeroList = engine.m_HeroMagicList;
    Assert(!ReferenceEquals(oldHumanList, publishedHumanList)
           && !ReferenceEquals(oldHeroList, publishedHeroList),
        "complete publication did not replace both definition tables");
    Assert(publishedHumanList.Count == 1 && publishedHeroList.Count == 1,
        "complete publication table counts");
    var publishedHuman = publishedHumanList[0];
    var publishedHero = publishedHeroList[0];
    Equal((ushort)69, publishedHuman.wMagicID,
        "published human collision id");
    Equal((ushort)69, publishedHero.wMagicID,
        "published hero collision id");
    Equal("DualSkill", publishedHuman.sMagicName,
        "published human collision name");
    Equal("DualSkill", publishedHero.sMagicName,
        "published hero collision name");
    Equal((byte)0x41, publishedHuman.btEffectType,
        "published human collision marker");
    Equal((byte)0x91, publishedHero.btEffectType,
        "published hero collision marker");
    Equal((byte)2, publishedHuman.btTrainLv,
        "published human collision training cap");
    Equal((byte)99, publishedHero.btTrainLv,
        "published hero collision training cap");
    Assert(ReferenceEquals(publishedHuman, engine.FindMagic(69))
           && ReferenceEquals(publishedHuman,
               engine.FindMagic("dualskill")),
        "human lookup crossed into the hero table");
    Assert(ReferenceEquals(publishedHero, engine.FindHeroMagic(69))
           && ReferenceEquals(publishedHero,
               engine.FindHeroMagic("DUALSKILL")),
        "hero lookup crossed into the human table");

    var replacementState = new NativeType2MagicSnapshotState();
    replacementState.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HumanMagicCommand,
        "DualSkill", 69, 1, effectType: 0x42, trainingCap: 3));
    replacementState.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HeroMagicCommand,
        "DualSkill", 69, 1, effectType: 0x92, trainingCap: 2));
    var replacementCatalog = new NativeType2MagicRuntimeCatalog();
    replacementCatalog.Publish(replacementState);
    Assert(!engine.TryPublishNativeMagicDefinitions(
            replacementCatalog, out error)
           && !string.IsNullOrEmpty(error),
        "second native double-table publication was accepted");
    Assert(ReferenceEquals(publishedHumanList, engine.m_MagicList)
           && ReferenceEquals(publishedHeroList, engine.m_HeroMagicList),
        "rejected second publication changed a list reference");
    Assert(ReferenceEquals(publishedHuman, engine.m_MagicList[0])
           && ReferenceEquals(publishedHero, engine.m_HeroMagicList[0])
           && engine.m_MagicList[0].btEffectType == 0x41
           && engine.m_HeroMagicList[0].btEffectType == 0x91,
        "rejected second publication changed published content");
}

static void VerifyProductionWiringSourceBoundaries()
{
    var repoRoot = AuditRepoRoot.Resolve();
    var heroObject = ReadSource(repoRoot, "GameSvr", "Actors",
        "HeroObject.cs");
    var heroCodec = ReadSource(repoRoot, "GameSvr", "DataStores",
        "NativeHeroRuntimeCodec.cs");
    var baseObject = ReadSource(repoRoot, "GameSvr", "Actors",
        "TBaseObject.cs");
    var pasBridge = ReadSource(repoRoot, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs");
    var yanshenApi = ReadSource(repoRoot, "GameSvr", "Plugins",
        "YanshenApi.cs");
    var gameApp = ReadSource(repoRoot, "GameSvr", "GameApp.cs");
    var appService = ReadSource(repoRoot, "GameSvr", "AppService.cs");
    var gameServer = ReadSource(repoRoot, "GameSvr", "GameServer.cs");
    var dbService = ReadSource(repoRoot, "GameSvr", "Services",
        "DBService.cs");
    var commonDb = ReadSource(repoRoot, "GameSvr", "DataStores",
        "CommonDB.cs");
    var reloadCommand = ReadOptionalSource(repoRoot, "GameSvr", "Command",
        "Commands", "ReloadMagicDBCommand.cs");

    var decodeMagic = Slice(heroCodec,
        "private static void DecodeMagicArea(",
        "private static bool TryWriteMagic(");
    Assert(decodeMagic.Contains("FindHeroMagic(magicId)",
               StringComparison.Ordinal)
           && !decodeMagic.Contains("UserEngine?.FindMagic(magicId)",
               StringComparison.Ordinal),
        "NativeHeroRuntimeCodec hero ID definition routing");

    var heroMethods = new[]
    {
        ("LearnHeroMagic", "public bool LearnHeroMagic(",
            "/// <summary>Delete a hero skill.</summary>"),
        ("UpgradeHeroMagic", "public bool UpgradeHeroMagic(",
            "public int CheckHeroSkill("),
        ("AddHeroSkillExp", "public bool AddHeroSkillExp(",
            "// ===================================================================="),
        ("TryLearnHeroSkillBook", "private bool TryLearnHeroSkillBook(",
            "private bool TryGetNativeUnionSkillBookMasterJob(")
    };
    foreach (var (name, start, end) in heroMethods)
    {
        var method = Slice(heroObject, start, end);
        Assert(method.Contains("UserEngine.FindHeroMagic(",
                   StringComparison.Ordinal)
               && !method.Contains("UserEngine.FindMagic(",
                   StringComparison.Ordinal),
            $"HeroObject.{name} definition routing");
    }

    // DeleteHeroMagic is deliberately NOT in the list above: native sub_73F690
    // (@0x73F690..0x73F7D2, anchored on SM_HERO_DELMAGIC=0xB9C `mov dx,0xB9C`
    // @0x73F74C) resolves the name against the hero's OWN magic TList
    // (`mov eax,[ebx+0x500]` @0x73F6BC, `call 0x40BD78` case-insensitive compare
    // @0x73F6FB) and consults NO global definition pool at all. Routing it through
    // UserEngine.FindHeroMagic was a shape native does not have and made deletion
    // impossible whenever the definition was absent from the published Hero pool.
    // This assertion therefore pins the ABSENCE of the pool lookup plus the exact
    // native traversal order/comparison. See staging/heromagic_mpcost_fix_20260804.md §A.
    var deleteHeroMagic = Slice(heroObject, "public bool DeleteHeroMagic(",
        "/// <summary>Upgrade a hero skill level.</summary>");
    Assert(!deleteHeroMagic.Contains("UserEngine.FindHeroMagic(",
               StringComparison.Ordinal)
           && !deleteHeroMagic.Contains("UserEngine.FindMagic(",
               StringComparison.Ordinal),
        "HeroObject.DeleteHeroMagic must not consult a global definition pool");
    Assert(deleteHeroMagic.Contains(
               "for (int i = m_HeroMagicList.Count - 1; i >= 0; i--)",
               StringComparison.Ordinal),
        "HeroObject.DeleteHeroMagic native downward traversal (@0x73F6C2 dec esi)");
    Assert(deleteHeroMagic.Contains("StringComparison.OrdinalIgnoreCase",
               StringComparison.Ordinal)
           && deleteHeroMagic.Contains("userMagic.MagicInfo.sMagicName",
               StringComparison.Ordinal),
        "HeroObject.DeleteHeroMagic native in-place case-insensitive name match "
        + "(@0x73F6FB sub_40BD78)");

    var addItemSkill = Slice(baseObject, "public void AddItemSkill(",
        "private bool AddToMap(");
    Assert(addItemSkill.Contains("this is HeroObject",
               StringComparison.Ordinal)
           && CountOccurrences(addItemSkill,
               "UserEngine.FindHeroMagic(") == 2
           && CountOccurrences(addItemSkill,
               "UserEngine.FindMagic(") == 2,
        "TBaseObject.AddItemSkill actor definition routing");

    var setScriptLevel = Slice(pasBridge,
        "private static bool TrySetScriptMagicLevel(",
        "private static void QueueScriptMagicLevelUpdate(");
    Assert(setScriptLevel.Contains("owner is HeroObject",
               StringComparison.Ordinal)
           && setScriptLevel.Contains("UserEngine.FindHeroMagic(skillName)",
               StringComparison.Ordinal)
           && setScriptLevel.Contains("UserEngine.FindMagic(skillName)",
               StringComparison.Ordinal),
        "Pas TrySetScriptMagicLevel actor definition routing");

    var getScriptLevel = Slice(pasBridge,
        "case \"getskilllevelext\":",
        "case \"gethumanskillblevelbyscript\":");
    Assert(getScriptLevel.Contains("owner is HeroObject",
               StringComparison.Ordinal)
           && getScriptLevel.Contains("UserEngine.FindHeroMagic(",
               StringComparison.Ordinal)
           && getScriptLevel.Contains("UserEngine.FindMagic(",
               StringComparison.Ordinal),
        "Pas getskilllevel actor definition routing");

    var heroCastSkill = Slice(yanshenApi,
        "public int HeroCastSkill(",
        "public int KillPetByName(");
    var commandedHeroMagic = Slice(heroObject,
        "private bool TrySelectCommandedMagic(",
        "private bool TryReleaseHeroMagic(");
    Assert(heroCastSkill.Contains("YanshenHeroCastState.Set(",
               StringComparison.Ordinal)
           && !heroCastSkill.Contains("MagicManager.DoSpell(",
               StringComparison.Ordinal)
           && commandedHeroMagic.Contains("FindHeroMagicById(magicId)",
               StringComparison.Ordinal)
           && !commandedHeroMagic.Contains("UserEngine.FindMagic(",
               StringComparison.Ordinal),
        "Yanshen command 28 learned hero definition routing");

    var initialize = Slice(gameApp, "public bool Initialize()",
        "public static bool ReloadNormalPrize(");
    Assert(!initialize.Contains("LoadMagicDB(", StringComparison.Ordinal)
           && !initialize.Contains("forcemagic",
               StringComparison.OrdinalIgnoreCase),
        "GameApp retained the direct forcemagic startup path");
    Assert(Order(initialize,
            "M2Share.DataServer.Start();",
            "M2Share.DataServer.TryWaitForNativeDefinitionInitialization(",
            "M2Share.UserEngine.TryPublishNativeMagicDefinitions(",
            "M2Share.LocalDB.LoadMonGen();"),
        "GameApp native definition startup order");
    Assert(CountOccurrences(initialize,
               "M2Share.DataServer.Stop();") >= 2
           && CountOccurrences(initialize, "return false;") >= 2,
        "GameApp native definition failure is not fail-closed");

    var activeLegacyCalls = Directory.EnumerateFiles(
            Path.Combine(repoRoot, "GameSvr"), "*.cs",
            SearchOption.AllDirectories)
        .Where(path => !path.Contains(
            $"{Path.DirectorySeparatorChar}AuditTools" +
            Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        .Sum(path => CountOccurrences(File.ReadAllText(path),
            ".LoadMagicDB("));
    Equal(0, activeLegacyCalls,
        "active CommonDB.LoadMagicDB call count");
    Assert(Order(commonDb,
            "public int LoadMagicDB()",
            "NativeMagicDefinitionsPublished == true",
            "return -1;",
            "select * from mir3.forcemagic"),
        "dormant forcemagic loader is not fail-closed after publication");
    if (reloadCommand == null)
    {
        // d5198c6b deleted ReloadMagicDBCommand.cs with the other 62
        // traditional-GOM commands. A command that does not exist cannot
        // reopen the legacy loader, but the guarantee has to keep holding for
        // the commands that remain, so the check widens instead of vanishing.
        var commandsDirectory = Path.Combine(repoRoot, "GameSvr", "Command",
            "Commands");
        var reopened = Directory.Exists(commandsDirectory)
            ? Directory.EnumerateFiles(commandsDirectory, "*.cs",
                    SearchOption.AllDirectories)
                .Where(file => File.ReadAllText(file)
                    .Contains(".LoadMagicDB(", StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .ToArray()
            : Array.Empty<string>();
        Assert(reopened.Length == 0,
            "ReloadMagicDBCommand.cs is gone, but a surviving command still " +
            "calls LoadMagicDB: " + string.Join(", ", reopened));
    }
    else
    {
        Assert(!reloadCommand.Contains(".LoadMagicDB(",
                   StringComparison.Ordinal)
               && reloadCommand.Contains("拒绝运行期替换",
                   StringComparison.Ordinal),
            "ReloadMagicDB command reopened the legacy definition path");
    }

    var appInitializeEngine = Slice(appService,
        "public bool InitializeEngine()",
        "public override Task StopAsync(");
    Assert(Order(appInitializeEngine,
            "if (!_mirApp.Initialize())",
            "_mirApp.StartEngine();",
            "_mirApp.StartService();"),
        "UserEngine service can start before native definition readiness");
    var onFormReady = Slice(appService, "public void OnFormReady()",
        "public override Task StartAsync(");
    Assert(Order(onFormReady,
            "if (!InitializeEngine()) return;",
            "StartNetwork();",
            "_engineReady = true;"),
        "Gate can start after failed native definition initialization");
    var startNetwork = Slice(appService, "public void StartNetwork()",
        "public void OnFormReady()");
    Assert(Order(startNetwork,
            "if (M2Share.boStartReady)",
            "M2Share.GateManager.Start();"),
        "Gate readiness guard");

    var startService = Slice(gameServer, "public void StartService()",
        "public void Stop()");
    Assert(startService.Contains("M2Share.DataServer.Start();",
            StringComparison.Ordinal),
        "StartService second DB start boundary disappeared");
    var dbStart = Slice(dbService, "public void Start()",
        "public void Stop()");
    Assert(Order(dbStart,
            "Interlocked.CompareExchange(ref _started, 1, 0)",
            "return;",
            "Volatile.Write(ref _stopping, 0);",
            "ConnectIfDue(Environment.TickCount64);")
           && CountOccurrences(dbStart, "ConnectIfDue(") == 1,
        "DBService.Start is not idempotent for StartService's second call");

    var staticConsume = Slice(dbService,
        "private bool ConsumeStaticInitializationFrame(",
        "private void PublishMagicDefinitionsOnceWhenReady()");
    Assert(Order(staticConsume,
            "_magicSnapshot.Consume(frame.Payload);",
            "PublishMagicDefinitionsOnceWhenReady();",
            "_fieldHeroSnapshot.Consume(frame.Payload);"),
        "DBService native magic publication wiring order");
    var publishOnce = Slice(dbService,
        "private void PublishMagicDefinitionsOnceWhenReady()",
        "private bool WaitUntil(");
    Assert(publishOnce.Contains("_magicSnapshot.HumanCompleted",
               StringComparison.Ordinal)
           && publishOnce.Contains("_magicSnapshot.HeroCompleted",
               StringComparison.Ordinal)
           && publishOnce.Contains("_magicPublicationCommitted",
               StringComparison.Ordinal)
           && publishOnce.Contains("_magicDefinitionsPublished.Set();",
               StringComparison.Ordinal),
        "DBService magic publication is not both-complete and one-shot");
}

static byte[] MagicPacket(ushort command, string name, ushort magicId,
    int completionMarker, byte effectType = 0, byte effect = 0,
    byte spell = 0, byte power = 0, byte maxPower = 0,
    byte defaultSpell = 0, byte defaultPower = 0,
    byte defaultMaxPower = 0, byte trainingCap = 0,
    byte[] needLevels = null, int[] levelTraining = null,
    int delay = 0, int coldMilliseconds = 0, int spellMilliseconds = 0)
{
    var packet = new byte[NativeType2MagicSnapshotState.PacketSize];
    BinaryPrimitives.WriteUInt16LittleEndian(packet, command);
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4),
        completionMarker);
    var record = packet.AsSpan(NativeType2MagicSnapshotState.HeaderSize,
        NativeType2MagicSnapshotState.RecordSize);

    var nameBytes = HUtil32.GbkEncoding.GetBytes(name ?? string.Empty);
    if (nameBytes.Length > NativeType2MagicDefinition.NameCapacity)
        throw new InvalidOperationException("test name exceeds native capacity");
    record[0] = checked((byte)nameBytes.Length);
    nameBytes.CopyTo(record.Slice(1));
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(0x10, 2),
        magicId);
    record[0x12] = effectType;
    record[0x13] = effect;
    record[0x14] = spell;
    record[0x15] = power;
    record[0x16] = maxPower;
    record[0x17] = defaultSpell;
    record[0x18] = defaultPower;
    record[0x19] = defaultMaxPower;
    record[0x1A] = trainingCap;

    needLevels ??= new byte[5];
    levelTraining ??= new int[4];
    if (needLevels.Length != 5 || levelTraining.Length != 4)
        throw new InvalidOperationException("invalid test fixture dimensions");
    needLevels.CopyTo(record.Slice(0x1B, 5));
    for (var index = 0; index < levelTraining.Length; index++)
    {
        BinaryPrimitives.WriteInt32LittleEndian(
            record.Slice(0x20 + index * 4, 4), levelTraining[index]);
    }
    BinaryPrimitives.WriteInt32LittleEndian(record.Slice(0x30, 4), delay);
    BinaryPrimitives.WriteInt32LittleEndian(record.Slice(0x34, 4),
        coldMilliseconds);
    BinaryPrimitives.WriteInt32LittleEndian(record.Slice(0x38, 4),
        spellMilliseconds);
    return packet;
}

static void Assert(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
    }
}

static void EqualSequence<T>(IEnumerable<T> expected,
    IEnumerable<T> actual, string description)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected [{string.Join(",", expected)}], " +
            $"actual [{string.Join(",", actual)}]");
    }
}

// For a source file whose absence is itself a legitimate repository state; the
// caller must then re-prove the contract some other way rather than skip it.
static string ReadOptionalSource(string repoRoot, params string[] parts)
{
    var path = parts.Aggregate(repoRoot, Path.Combine);
    return File.Exists(path) ? File.ReadAllText(path) : null;
}

static string ReadSource(string repoRoot, params string[] parts)
{
    var path = parts.Aggregate(repoRoot, Path.Combine);
    if (!File.Exists(path)) throw new FileNotFoundException(path);
    return File.ReadAllText(path);
}

static string Slice(string source, string startNeedle, string endNeedle)
{
    var start = source.IndexOf(startNeedle, StringComparison.Ordinal);
    if (start < 0)
        throw new InvalidOperationException(
            $"source boundary start missing: {startNeedle}");
    var end = source.IndexOf(endNeedle, start + startNeedle.Length,
        StringComparison.Ordinal);
    if (end < 0)
        throw new InvalidOperationException(
            $"source boundary end missing: {endNeedle}");
    return source[start..end];
}

static int CountOccurrences(string source, string needle)
{
    var count = 0;
    var offset = 0;
    while (true)
    {
        offset = source.IndexOf(needle, offset, StringComparison.Ordinal);
        if (offset < 0) return count;
        count++;
        offset += needle.Length;
    }
}

static bool Order(string source, params string[] needles)
{
    var offset = -1;
    foreach (var needle in needles)
    {
        offset = source.IndexOf(needle, offset + 1,
            StringComparison.Ordinal);
        if (offset < 0) return false;
    }
    return true;
}
