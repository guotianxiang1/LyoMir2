using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
CheckHierarchyAndDormantBoundary();
CheckOriginalClassMetadata();
CheckBaseConstructorCapture();
CheckConcreteConstructorState();
CheckOrdinaryAbilities();
CheckModelAbility();
CheckDotaAbilities();
CheckSkillContracts();

Console.WriteLine("PASS NativeType2FieldHeroActorContractCheck " +
                  "classes=9 hierarchy=AiMon constructor-scalars=verified " +
                  "container-core=closed fill-core=closed production=NO-GO formulas=closed " +
                  "skills=ordered runtime=NO-GO");

static void CheckHierarchyAndDormantBoundary()
{
    Equal(typeof(AiMon), typeof(TFieldHero).BaseType,
        "FieldHero direct managed base");
    Check(typeof(TFieldHero).IsAbstract, "FieldHero base is abstract");
    Equal(typeof(TFieldHero), typeof(TMirDotaMatchHumMon).BaseType,
        "Dota common base");
    Check(typeof(TMirDotaMatchHumMon).IsAbstract,
        "Dota common actor is abstract");
    var fieldHeroConstructor = typeof(TFieldHero).GetConstructors(
        BindingFlags.NonPublic | BindingFlags.Instance).Single();
    Check(fieldHeroConstructor.IsFamilyAndAssembly,
        "FieldHero construction is private protected");
    var dotaConstructor = typeof(TMirDotaMatchHumMon).GetConstructors(
        BindingFlags.NonPublic | BindingFlags.Instance).Single();
    Check(dotaConstructor.IsFamilyAndAssembly,
        "Dota base construction is private protected");

    var concrete = new[]
    {
        typeof(TFieldWarHero),
        typeof(TFieldWizHero),
        typeof(TFieldTaosHero),
        typeof(TFieldAssHero),
        typeof(TModelHero),
        typeof(TMirDotaMatchHumMon_War),
        typeof(TMirDotaMatchHumMon_Wiz),
        typeof(TMirDotaMatchHumMon_Taos),
        typeof(TMirDotaMatchHumMon_Ass)
    };
    Equal(9, concrete.Distinct().Count(), "nine distinct actor types");
    foreach (var type in concrete)
    {
        Check(type.IsSealed && !type.IsAbstract,
            type.Name + " is a sealed concrete actor");
        Check(typeof(TFieldHero).IsAssignableFrom(type),
            type.Name + " derives from FieldHero");
        Equal(0, type.GetConstructors(BindingFlags.Public |
                                      BindingFlags.Instance).Length,
            type.Name + " has no public construction entry");
        var constructors = type.GetConstructors(BindingFlags.NonPublic |
                                                BindingFlags.Instance);
        Equal(1, constructors.Length, type.Name + " internal constructor");
        var parameters = constructors[0].GetParameters();
        Equal(2, parameters.Length, type.Name + " constructor arity");
        Equal(typeof(NativeType2FieldHeroSpawnPlan),
            parameters[0].ParameterType,
            type.Name + " retains the spawn plan handle");
        Equal(typeof(NativeType2FieldHeroMaterialization),
            parameters[1].ParameterType,
            type.Name + " retains the materialization handle");
    }

    Check(!TFieldHero.ProductionReady, "production gate remains closed");
    Check(TFieldHero.ProductionNoGoReason.StartsWith("NO-GO:",
            StringComparison.Ordinal),
        "NO-GO reason is explicit");
    Equal(typeof(TFieldHero), typeof(TFieldHero).GetMethod(
            nameof(TFieldHero.Initialize))!.DeclaringType,
        "Initialize is owned by dormant base");
    Equal(typeof(TFieldHero), typeof(TFieldHero).GetMethod(
            nameof(TFieldHero.Run))!.DeclaringType,
        "Run cannot fall through to AiMon");
}

static void CheckOriginalClassMetadata()
{
    Equal(0x00606F1C, TFieldHero.OriginalVmtAddress, "FieldHero VMT");
    Equal(0x006094E8, TFieldHero.OriginalConstructorAddress,
        "FieldHero constructor");
    Equal(0x69C, TFieldHero.OriginalInstanceSize, "FieldHero size");

    CheckMetadata(TFieldWarHero.OriginalVmt,
        TFieldWarHero.OriginalConstructor, TFieldWarHero.OriginalSize,
        0x006071CC, 0x0060B6EC, 0x6A8, "FieldWarHero");
    CheckMetadata(TFieldWizHero.OriginalVmt,
        TFieldWizHero.OriginalConstructor, TFieldWizHero.OriginalSize,
        0x0060773C, 0x0060C1DC, 0x6A4, "FieldWizHero");
    CheckMetadata(TFieldTaosHero.OriginalVmt,
        TFieldTaosHero.OriginalConstructor, TFieldTaosHero.OriginalSize,
        0x006079F4, 0x0060BD88, 0x6A8, "FieldTaosHero");
    CheckMetadata(TFieldAssHero.OriginalVmt,
        TFieldAssHero.OriginalConstructor, TFieldAssHero.OriginalSize,
        0x00607484, 0x00608D68, 0x6A0, "FieldAssHero");
    CheckMetadata(TModelHero.OriginalVmt,
        TModelHero.OriginalConstructor, TModelHero.OriginalSize,
        0x00607CAC, 0x00609038, 0x6A0, "ModelHero");
    CheckMetadata(TMirDotaMatchHumMon_War.OriginalVmt,
        TMirDotaMatchHumMon_War.OriginalConstructor,
        TMirDotaMatchHumMon_War.OriginalSize,
        0x00608230, 0x0060CDDC, 0x6B8, "DotaWar");
    CheckMetadata(TMirDotaMatchHumMon_Wiz.OriginalVmt,
        TMirDotaMatchHumMon_Wiz.OriginalConstructor,
        TMirDotaMatchHumMon_Wiz.OriginalSize,
        0x006087C0, 0x0060D644, 0x6B4, "DotaWiz");
    CheckMetadata(TMirDotaMatchHumMon_Taos.OriginalVmt,
        TMirDotaMatchHumMon_Taos.OriginalConstructor,
        TMirDotaMatchHumMon_Taos.OriginalSize,
        0x00608A88, 0x0060DA0C, 0x6B8, "DotaTaos");
    CheckMetadata(TMirDotaMatchHumMon_Ass.OriginalVmt,
        TMirDotaMatchHumMon_Ass.OriginalConstructor,
        TMirDotaMatchHumMon_Ass.OriginalSize,
        0x006084F8, 0x0060D3C0, 0x6B0, "DotaAss");
}

static void CheckBaseConstructorCapture()
{
    var calls = new List<string>();
    var randomValue = NativeFieldHeroBaseConstructorCapture
        .CaptureRandom05F8(maximum =>
        {
            calls.Add("random:" + maximum);
            return 3;
        });
    var tick = NativeFieldHeroBaseConstructorCapture.CaptureTick(() =>
    {
        calls.Add("tick");
        return 0x12345678;
    });

    Equal(33, randomValue, "base constructor Random(34)+30");
    Equal(0x12345678, tick, "base constructor tick capture");
    Equal(2, calls.Count, "base constructor source call count");
    Equal("random:34", calls[0], "base constructor random first");
    Equal("tick", calls[1], "base constructor clock second");
    ExpectThrows<ArgumentNullException>(() =>
            NativeFieldHeroBaseConstructorCapture.CaptureRandom05F8(null),
        "null constructor random rejected");
    ExpectThrows<ArgumentNullException>(() =>
            NativeFieldHeroBaseConstructorCapture.CaptureTick(null),
        "null constructor clock rejected");
}

static void CheckConcreteConstructorState()
{
    var originalRandom = M2Share.RandomNumber;
    var originalObjectManager = M2Share.ObjectManager;
    var random = new RecordingRandomNumber();
    M2Share.RandomNumber = random;
    M2Share.ObjectManager = new ObjectManager();
    try
    {
        var adapter = CreateRuntimeAdapter();
        var cases = new (byte Selector, Type Type, byte Job,
            bool Dota, bool Assassin)[]
        {
            (0, typeof(TFieldWarHero), 0, false, false),
            (1, typeof(TFieldWizHero), 1, false, false),
            (2, typeof(TFieldTaosHero), 2, false, false),
            (3, typeof(TFieldAssHero), 3, false, true),
            (4, typeof(TMirDotaMatchHumMon_War), 0, true, false),
            (5, typeof(TMirDotaMatchHumMon_Wiz), 1, true, false),
            (6, typeof(TMirDotaMatchHumMon_Taos), 2, true, false),
            (7, typeof(TMirDotaMatchHumMon_Ass), 3, true, true),
            (8, typeof(TModelHero), 0, false, false)
        };

        foreach (var testCase in cases)
        {
            random.Clear();
            var beforeBaseTick = HUtil32.GetTickCount();
            var actor = CreateActor(adapter, testCase.Selector,
                testCase.Type);
            var afterBaseTick = HUtil32.GetTickCount();
            Equal(1, random.Bounds.Count(value => value == 34),
                testCase.Type.Name + " consumes one Random(34)");
            Check(TickWithin(actor.NativeRaw0634, beforeBaseTick,
                    afterBaseTick),
                testCase.Type.Name + " captures a live base tick");
            CheckCommonConstructorState(actor, testCase.Type.Name,
                testCase.Dota);
            Equal(testCase.Job, actor.m_btJob,
                testCase.Type.Name + " job");
            Equal(testCase.Assassin ? 500 : 1000,
                actor.m_nNextHitTime,
                testCase.Type.Name + " attack interval");
            if (testCase.Selector <= 3)
            {
                Equal((ushort)45, actor.m_Abil.Level,
                    testCase.Type.Name + " ability level");
                Equal((ushort)45, actor.m_WAbil.Level,
                    testCase.Type.Name + " work ability level");
            }

            if (testCase.Dota)
            {
                var dota = (TMirDotaMatchHumMon)actor;
                Equal(0, dota.NativeRaw0608,
                    testCase.Type.Name + " Dota raw +608 reset");
                Equal(0, dota.NativeRaw05F8,
                    testCase.Type.Name + " Dota random carrier reset");
                Equal(0, dota.NativeRaw05FC,
                    testCase.Type.Name + " Dota raw +5FC reset");
                Equal((byte)0, dota.NativeRaw05F4,
                    testCase.Type.Name + " Dota raw +5F4");
                Equal(1, dota.NativeRaw06A0Length,
                    testCase.Type.Name + " Dota +6A0 length");
                Equal(0, dota.NativeRaw06A4,
                    testCase.Type.Name + " Dota raw +6A4");
                Equal(0, dota.NativeRaw06A8,
                    testCase.Type.Name + " Dota raw +6A8");
                Equal(9, dota.m_nViewRange,
                    testCase.Type.Name + " Dota view range");
            }
            else
            {
                Equal(7, actor.m_nViewRange,
                    testCase.Type.Name + " ordinary view range");
            }
        }

        var beforeTaos = HUtil32.GetTickCount();
        var taos = (TFieldTaosHero)CreateActor(adapter, 2,
            typeof(TFieldTaosHero));
        var afterTaos = HUtil32.GetTickCount();
        Check(TickWithin(taos.NativeRaw06A4, beforeTaos, afterTaos),
            "ordinary Taos captures subclass tick");
        var beforeDotaTaos = HUtil32.GetTickCount();
        var dotaTaos = (TMirDotaMatchHumMon_Taos)CreateActor(adapter, 6,
            typeof(TMirDotaMatchHumMon_Taos));
        var afterDotaTaos = HUtil32.GetTickCount();
        Check(TickWithin(dotaTaos.NativeRaw06B4, beforeDotaTaos,
                afterDotaTaos),
            "Dota Taos captures subclass tick");
        var model = (TModelHero)CreateActor(adapter, 8,
            typeof(TModelHero));
        Equal((byte)1, model.NativeRaw02E1, "model raw +2E1");
        Equal((byte)1, model.NativeRaw02E0, "model raw +2E0");
    }
    finally
    {
        M2Share.RandomNumber = originalRandom;
        M2Share.ObjectManager = originalObjectManager;
    }
}

static void CheckCommonConstructorState(TFieldHero actor,
    string description, bool dota)
{
    Equal((byte)1, actor.NativeRaw03AC, description + " raw +3AC");
    Equal(700, actor.m_nWalkSpeed, description + " walk speed");
    Equal(2000, actor.NativeSpellCooldownInterval,
        description + " spell interval");
    Equal((byte)1, actor.m_btHair, description + " hair");
    Equal((byte)0x83, actor.m_btRaceServer,
        description + " race server");
    Equal((byte)1, actor.NativeRaw02E8, description + " raw +2E8");
    if (!dota)
    {
        Equal(10, actor.NativeRaw0608, description + " raw +608");
        Equal(30, actor.NativeRaw05F8,
            description + " deterministic Random(34)+30");
        Equal(8, actor.NativeRaw05FC, description + " raw +5FC");
    }
    Equal(0, actor.m_MagicList.Count,
        description + " fresh empty magic list");
    Equal(0, actor.NativeDropItems.Count,
        description + " non-null empty borrowed drop table");
    Equal(actor.NativeRaw0634, actor.NativeLastAttackTick,
        description + " common tick +638");
    Equal(actor.NativeRaw0634, actor.NativeRaw060C,
        description + " common tick +60C");
    Equal(actor.NativeRaw0634, actor.NativeLastSpellTick,
        description + " common tick +624");
    Equal(actor.NativeRaw0634, actor.NativeSpecialSkillTick,
        description + " common tick +628");
    Equal(actor.NativeRaw0634, actor.NativeRaw0610,
        description + " common tick +610");
    Equal(0, actor.NativeLifetimeRemaining,
        description + " lifetime initial value");
    Equal(0, actor.NativeFameRank,
        description + " fame rank initial value");
}

static bool TickWithin(int value, int start, int end) =>
    unchecked((uint)(value - start)) <= unchecked((uint)(end - start));

static void CheckOrdinaryAbilities()
{
    var war = TFieldWarHero.CalculateNativeAbility(45);
    Equal(1614, war.MaxHp, "war level 45 MaxHP");
    Equal(169, war.MaxMp, "war level 45 MaxMP");
    CheckPair(war.AC, 0, 6, "war AC");
    CheckPair(war.DC, 8, 9, "war DC");
    Equal(21, TFieldWarHero.CalculateNativeAbility(3).MaxMp,
        "war x87 tie-to-even projection");
    Equal(2704, TFieldWarHero.CalculateNativeAbility(61).MaxHp,
        "war post-60 subtraction");

    var wiz = TFieldWizHero.CalculateNativeAbility(45);
    Equal(410, wiz.MaxHp, "wiz level 45 MaxHP");
    Equal(1102, wiz.MaxMp, "wiz level 45 MaxMP");
    CheckPair(wiz.DC, 5, 6, "wiz DC");
    CheckPair(wiz.MC, 5, 6, "wiz MC");
    Equal(633, TFieldWizHero.CalculateNativeAbility(61).MaxHp,
        "wiz post-60 addition");

    var taos = TFieldTaosHero.CalculateNativeAbility(45);
    Equal(838, taos.MaxHp, "taos level 45 MaxHP");
    Equal(570, taos.MaxMp, "taos level 45 MaxMP");
    CheckPair(taos.MAC, 3, 8, "taos MAC");
    CheckPair(taos.DC, 5, 6, "taos DC");
    CheckPair(taos.SC, 5, 6, "taos SC");
    Equal(1313, TFieldTaosHero.CalculateNativeAbility(61).MaxHp,
        "taos post-60 addition");

    var ass = TFieldAssHero.CalculateNativeAbility(45);
    Equal(1614, ass.MaxHp, "ass level 45 MaxHP");
    Equal(1614, ass.MaxMp, "ass level 45 MaxMP");
    CheckPair(ass.AC, 0, 6, "ass AC");
    CheckPair(ass.CC, 8, 9, "ass CC");
    CheckPair(TFieldAssHero.CalculateNativeAbility(0).CC, -1, 0,
        "ass low-level CC remains unclamped");
}

static void CheckModelAbility()
{
    var model = TModelHero.CalculateNativeAbility(65535);
    Equal(5000, model.MaxHp, "model fixed MaxHP");
    Equal(1000, model.MaxMp, "model fixed MaxMP");
    CheckPair(model.AC, 0, 1000, "model AC");
    CheckPair(model.MAC, 0, 1000, "model MAC");
    Equal((int?)5000, model.ForcedCurrentHp, "model current HP");
    Equal((int?)5000, model.ForcedCurrentMp,
        "model current MP exceeds MaxMP exactly");
}

static void CheckDotaAbilities()
{
    var war = TMirDotaMatchHumMon_War.CalculateNativeAbility(2);
    Equal(100000, war.MaxHp, "Dota war MaxHP");
    Equal(10000, war.MaxMp, "Dota war MaxMP");
    CheckPair(war.DC, 1000, 1000, "Dota war DC");

    var wiz = TMirDotaMatchHumMon_Wiz.CalculateNativeAbility(2);
    Equal(10000, wiz.MaxHp, "Dota wiz MaxHP");
    Equal(100000, wiz.MaxMp, "Dota wiz MaxMP");
    CheckPair(wiz.MC, 1600, 1600, "Dota wiz MC");

    var taos = TMirDotaMatchHumMon_Taos.CalculateNativeAbility(2);
    Equal(50000, taos.MaxHp, "Dota taos MaxHP");
    Equal(50000, taos.MaxMp, "Dota taos MaxMP");
    CheckPair(taos.SC, 2000, 2000, "Dota taos SC");

    var ass = TMirDotaMatchHumMon_Ass.CalculateNativeAbility(2);
    Equal(50000, ass.MaxHp, "Dota ass MaxHP");
    Equal(50000, ass.MaxMp, "Dota ass MaxMP");
    CheckPair(ass.CC, 1000, 1000, "Dota ass CC");
}

static void CheckSkillContracts()
{
    CheckSkills(TFieldWarHero.SkillContracts,
        TFieldWarHero.SkillPlacementContract,
        new ushort[] { 3, 12, 26, 34 }, 3, "ordinary war");
    CheckSkills(TFieldWizHero.SkillContracts,
        TFieldWizHero.SkillPlacementContract,
        new ushort[] { 11, 35, 31, 10 }, 3, "ordinary wiz");
    CheckSkills(TFieldTaosHero.SkillContracts,
        TFieldTaosHero.SkillPlacementContract,
        new ushort[] { 4, 6, 13, 36 }, 3, "ordinary taos");
    CheckSkills(TFieldAssHero.SkillContracts,
        TFieldAssHero.SkillPlacementContract,
        new ushort[] { 260, 264, 268 }, 4, "ordinary ass");
    Equal(0, TModelHero.SkillContracts.Count, "model has no skills");
    Equal(NativeFieldHeroSkillPlacement.None,
        TModelHero.SkillPlacementContract, "model skill placement");
    CheckSkills(TMirDotaMatchHumMon_War.SkillContracts,
        TMirDotaMatchHumMon_War.SkillPlacementContract,
        new ushort[] { 3, 12, 26, 34 }, 3, "Dota war");
    CheckSkills(TMirDotaMatchHumMon_Wiz.SkillContracts,
        TMirDotaMatchHumMon_Wiz.SkillPlacementContract,
        new ushort[] { 11, 35, 31, 10 }, 3, "Dota wiz");
    CheckSkills(TMirDotaMatchHumMon_Taos.SkillContracts,
        TMirDotaMatchHumMon_Taos.SkillPlacementContract,
        new ushort[] { 4, 6, 13, 36 }, 3, "Dota taos");
    CheckSkills(TMirDotaMatchHumMon_Ass.SkillContracts,
        TMirDotaMatchHumMon_Ass.SkillPlacementContract,
        new ushort[] { 260, 264, 268 }, 4, "Dota ass");
}

static void CheckSkills(IReadOnlyList<NativeFieldHeroSkillContract> actual,
    NativeFieldHeroSkillPlacement placement, IReadOnlyList<ushort> ids,
    byte level, string description)
{
    Equal(ids.Count, actual.Count, description + " skill count");
    for (var index = 0; index < ids.Count; index++)
    {
        Equal(ids[index], actual[index].MagicId,
            description + " skill order " + index);
        Equal(level, actual[index].Level,
            description + " skill level " + index);
    }

    Equal(level == 4
            ? NativeFieldHeroSkillPlacement.AfterCommonInitialize
            : NativeFieldHeroSkillPlacement.BeforeCommonInitialize,
        placement, description + " initialization placement");
}

static NativeType2FieldHeroRuntimeCatalogAdapter CreateRuntimeAdapter()
{
    var standardSnapshot = NativeType2StdItemSnapshotState
        .CreateForVerifiedOriginalStartup();
    standardSnapshot.Consume(CreatePacket(
        NativeType2StdItemSnapshotState.Command,
        NativeType2StdItemSnapshotState.HeaderSize,
        Array.Empty<byte>(), true));
    var standardItems = new NativeType2StdItemStaticCatalog();
    standardItems.Publish(standardSnapshot);

    var body = new byte[NativeType2FieldHeroSnapshotState.BodySize];
    var name = Encoding.ASCII.GetBytes("CtorHero");
    body[0] = checked((byte)name.Length);
    name.CopyTo(body, 1);
    body[0x10] = 0;
    BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0x12, 2), 45);
    var fieldSnapshot = new NativeType2FieldHeroSnapshotState();
    fieldSnapshot.Consume(CreatePacket(
        NativeType2FieldHeroSnapshotState.Command,
        NativeType2FieldHeroSnapshotState.HeaderSize, body, true));
    var fieldCatalog = new NativeType2FieldHeroStaticCatalog();
    fieldCatalog.Publish(fieldSnapshot, standardItems);

    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(fieldCatalog, standardItems);
    return adapter;
}

static TFieldHero CreateActor(
    NativeType2FieldHeroRuntimeCatalogAdapter adapter, byte selector,
    Type actorType)
{
    if (!adapter.TryResolveTemplate("CtorHero", out var template))
        throw new InvalidOperationException("missing constructor template");
    var selection = template.CaptureSelectionAfterPlacement(selector);
    var plan = NativeType2FieldHeroSpawnPlanFactory.Create(selection);
    var materialization = plan.MaterializeEquipment();
    using var registration = M2Share.ObjectManager
        .BeginDeferredRegistration();
    var actor = Activator.CreateInstance(actorType,
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new object[] { plan, materialization }, null) as TFieldHero;
    if (actor == null)
        throw new InvalidOperationException(
            "could not construct " + actorType.Name);
    Equal(actorType, actor.GetType(), actorType.Name + " concrete type");
    return actor;
}

static byte[] CreatePacket(ushort command, int headerSize, byte[] body,
    bool completed)
{
    var packet = new byte[headerSize + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(packet, command);
    if (completed)
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0x08, 4), 1);
    body.CopyTo(packet, headerSize);
    return packet;
}

static void CheckMetadata(int vmt, int constructor, int size,
    int expectedVmt, int expectedConstructor, int expectedSize,
    string description)
{
    Equal(expectedVmt, vmt, description + " VMT");
    Equal(expectedConstructor, constructor,
        description + " constructor");
    Equal(expectedSize, size, description + " size");
}

static void CheckPair(NativeFieldHeroAbilityPair actual, int low, int high,
    string description)
{
    Equal(low, actual.Low, description + " low");
    Equal(high, actual.High, description + " high");
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected={expected}, actual={actual}");
    }
}

static void ExpectThrows<T>(Action action, string description)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException(description);
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
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

sealed class RecordingRandomNumber : RandomNumber
{
    public List<int> Bounds { get; } = new();

    public void Clear() => Bounds.Clear();

    public override int Random() => 0;

    public override int Random(int value)
    {
        Bounds.Add(value);
        return 0;
    }

    public override int Random(int minValue, int maxValue) => minValue;

    public override int GetRandomNumber(int minValue, int maxValue) =>
        minValue;
}
