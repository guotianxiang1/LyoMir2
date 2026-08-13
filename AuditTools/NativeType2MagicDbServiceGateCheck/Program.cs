using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "!Setup.txt"),
    "[Server]" + Environment.NewLine);
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "Command.conf"),
    "[Command]" + Environment.NewLine);
// M2Share's static ctor also builds ExpsConfig from ..\Share\PlayerUpgradeExp.ini
// (M2Share.cs:1690); without it IniFile.Load throws and no assertion runs.
var shareDirectory = Path.Combine(Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..")), "Share");
Directory.CreateDirectory(shareDirectory);
File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
    "[PlayerLevelExp]" + Environment.NewLine);
File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
    "[Integer]" + Environment.NewLine);

try
{
    VerifyDbServiceMagicPublicationAndStaticGate();
    Console.WriteLine(
        "PASS NativeType2MagicDbServiceGateCheck " +
        "65=human-only-unpublished 66=dual-table-published-once " +
        "67=monster-published-once 68=stditem-published-once 108=static-gate " +
        "reconnect=process-lifetime-snapshot");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeType2MagicDbServiceGateCheck FAIL: {exception}");
    return 1;
}

static void VerifyDbServiceMagicPublicationAndStaticGate()
{
    var consume = RequiredPrivateMethod(
        "ConsumeStaticInitializationFrame",
        typeof(LegacyDbServerFrame));
    var advanceConnection = RequiredPrivateMethod(
        "AdvanceConnectionGenerationAndResetInboundState");

    var pasEnvir = Path.Combine(AppContext.BaseDirectory,
        "MagicGateEnvir");
    Directory.CreateDirectory(pasEnvir);
    M2Share.PasEngine = new GameSvr.PasEngine.PasScriptHost(pasEnvir);
    using var service = new DBService();
    var catalog = service.MagicRuntimeCatalog;
    var emptyHuman = catalog.HumanDefinitions;
    var emptyHero = catalog.HeroDefinitions;

    Assert(Consume(consume, service, MagicFrame(
            NativeType2MagicSnapshotState.HumanMagicCommand,
            "HumanOriginal", 701, 0x11, 0x21)),
        "human 65 frame was not consumed by static initialization");
    Assert(!service.NativeMagicDefinitionsPublished,
        "human completion published before hero completion");
    Assert(!catalog.Ready,
        "catalog became ready after only human completion");
    Assert(ReferenceEquals(emptyHuman, catalog.HumanDefinitions)
           && ReferenceEquals(emptyHero, catalog.HeroDefinitions),
        "human-only completion replaced the empty publication");

    Assert(Consume(consume, service, MagicFrame(
            NativeType2MagicSnapshotState.HeroMagicCommand,
            "HeroOriginal", 702, 0x12, 0x22)),
        "hero 66 frame was not consumed by static initialization");
    Assert(service.NativeMagicDefinitionsPublished,
        "dual completion did not commit magic publication");
    Assert(catalog.Ready,
        "dual completion did not make the catalog ready");
    Equal(1, ReadPrivateInt(service, "_magicPublicationCommitted"),
        "magic publication commit count/state");

    var humanDefinitions = catalog.HumanDefinitions;
    var heroDefinitions = catalog.HeroDefinitions;
    Equal(1, humanDefinitions.Count, "human definition count");
    Equal(1, heroDefinitions.Count, "hero definition count");
    Assert(!ReferenceEquals(humanDefinitions, heroDefinitions),
        "human and hero catalogs alias the same list");
    AssertDefinition(humanDefinitions[0], "HumanOriginal", 701,
        0x11, 0x21, "human");
    AssertDefinition(heroDefinitions[0], "HeroOriginal", 702,
        0x12, 0x22, "hero");

    Assert(Consume(consume, service, MonsterFrame(
            "GateMonster", 0x12345678)),
        "monster 67 frame was not consumed by static initialization");
    Assert(service.NativeMonsterDefinitionsPublished
        && service.MonsterRuntimeCatalog.Ready,
        "monster completion did not commit publication");
    Equal(1, service.MonsterRuntimeCatalog.Definitions.Count,
        "monster definition count");
    Equal(0x12345678,
        service.MonsterRuntimeCatalog.Definitions[0].HitPoints,
        "monster definition HP");

    Assert(Consume(consume, service, StdItemFrame("GateStdItem")),
        "standard item 68 frame was not consumed by static initialization");
    Assert(service.NativeStdItemDefinitionsPublished
           && service.StdItemRuntimeCatalog.Ready
           && service.StdItemRuntimeCatalog.Count == 2,
        "standard item completion did not commit publication");

    Assert(!service.StaticInitializationCompleted,
        "static initialization gate opened before 108 completion");
    Assert(!service.TryWaitForNativeDefinitionInitialization(0,
            out var before108Error),
        "initialization wait succeeded before 108 completion");
    Equal("等待原生 Type2 静态初始化切换超时", before108Error,
        "pre-108 initialization wait error");

    var generationBefore = ReadPrivateInt(service, "_connectionGeneration");
    advanceConnection.Invoke(service, null);
    Equal(generationBefore + 1,
        ReadPrivateInt(service, "_connectionGeneration"),
        "connection generation advance");
    Assert(!service.StaticInitializationCompleted,
        "reconnect opened the static initialization gate");

    Assert(Consume(consume, service, MagicFrame(
            NativeType2MagicSnapshotState.HumanMagicCommand,
            "HumanReplay", 801, 0x31, 0x41)),
        "replayed human 65 frame left static initialization path");
    Assert(Consume(consume, service, MagicFrame(
            NativeType2MagicSnapshotState.HeroMagicCommand,
            "HeroReplay", 802, 0x32, 0x42)),
        "replayed hero 66 frame left static initialization path");

    Assert(ReferenceEquals(humanDefinitions, catalog.HumanDefinitions)
           && ReferenceEquals(heroDefinitions, catalog.HeroDefinitions),
        "reconnect replay replaced published catalog list references");
    Equal(1, catalog.HumanDefinitions.Count,
        "reconnect replay changed human count");
    Equal(1, catalog.HeroDefinitions.Count,
        "reconnect replay changed hero count");
    AssertDefinition(catalog.HumanDefinitions[0], "HumanOriginal", 701,
        0x11, 0x21, "human after replay");
    AssertDefinition(catalog.HeroDefinitions[0], "HeroOriginal", 702,
        0x12, 0x22, "hero after replay");
    Equal(1, ReadPrivateInt(service, "_magicPublicationCommitted"),
        "reconnect replay recommitted the magic catalog");

    Assert(Consume(consume, service, FieldHeroCompletionFrame()),
        "108 completion frame was not consumed");
    Assert(service.StaticInitializationCompleted,
        "108 completion did not open the static initialization gate");
    Assert(service.TryWaitForNativeDefinitionInitialization(0,
            out var after108Error),
        "initialization wait failed after 65/66/108 completion: " +
        after108Error);
    Equal(string.Empty, after108Error,
        "successful initialization wait error");
    Assert(ReferenceEquals(humanDefinitions, catalog.HumanDefinitions)
           && ReferenceEquals(heroDefinitions, catalog.HeroDefinitions),
        "108 completion replaced the published magic catalog");
}

static MethodInfo RequiredPrivateMethod(string name, params Type[] parameters)
{
    var method = typeof(DBService).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null, types: parameters, modifiers: null);
    return method ?? throw new InvalidOperationException(
        "missing DBService private method " + name);
}

static bool Consume(MethodInfo method, DBService service,
    LegacyDbServerFrame frame)
{
    try
    {
        return (bool)method.Invoke(service, new object[] { frame });
    }
    catch (TargetInvocationException exception)
        when (exception.InnerException != null)
    {
        throw exception.InnerException;
    }
}

static LegacyDbServerFrame MagicFrame(ushort command, string name,
    ushort magicId, byte effectType, byte databaseJob)
{
    var payload = new byte[NativeType2MagicSnapshotState.PacketSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);

    var record = payload.AsSpan(NativeType2MagicSnapshotState.HeaderSize,
        NativeType2MagicSnapshotState.RecordSize);
    var nameBytes = Encoding.ASCII.GetBytes(name);
    Assert(nameBytes.Length <= NativeType2MagicDefinition.NameCapacity,
        "test magic name exceeds native capacity");
    record[0] = checked((byte)nameBytes.Length);
    nameBytes.CopyTo(record.Slice(1));
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(0x10, 2),
        magicId);
    record[0x12] = effectType;
    record[0x1A] = databaseJob;
    return new LegacyDbServerFrame(2, 0, payload);
}

static LegacyDbServerFrame MonsterFrame(string name, int hitPoints)
{
    var payload = new byte[NativeType2MonsterSnapshotState.HeaderSize
                           + NativeType2MonsterSnapshotState.MinimumBodySize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2MonsterSnapshotState.Command);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    var body = payload.AsSpan(NativeType2MonsterSnapshotState.HeaderSize);
    var nameBytes = Encoding.ASCII.GetBytes(name);
    Assert(nameBytes.Length <= NativeType2MonsterDefinition.NameCapacity,
        "test monster name exceeds native capacity");
    body[0x04] = checked((byte)nameBytes.Length);
    nameBytes.CopyTo(body.Slice(0x05));
    BinaryPrimitives.WriteInt32LittleEndian(body.Slice(0x20, 4),
        hitPoints);
    return new LegacyDbServerFrame(2, 0, payload);
}

static LegacyDbServerFrame StdItemFrame(string name)
{
    var payload = new byte[NativeType2StdItemSnapshotState.PacketSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2StdItemSnapshotState.Command);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    var body = payload.AsSpan(NativeType2StdItemSnapshotState.HeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(body, 1);
    var nameBytes = Encoding.ASCII.GetBytes(name);
    Assert(nameBytes.Length <= 15,
        "test standard-item name exceeds native capacity");
    body[0x04] = checked((byte)nameBytes.Length);
    nameBytes.CopyTo(body.Slice(0x05));
    return new LegacyDbServerFrame(2, 0, payload);
}

static LegacyDbServerFrame FieldHeroCompletionFrame()
{
    var payload = new byte[NativeType2FieldHeroSnapshotState.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2FieldHeroSnapshotState.Command);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    return new LegacyDbServerFrame(2, 0, payload);
}

static int ReadPrivateInt(DBService service, string fieldName)
{
    var field = typeof(DBService).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing DBService field " + fieldName);
    return (int)field.GetValue(service);
}

static void AssertDefinition(NativeType2MagicDefinition definition,
    string name, ushort magicId, byte effectType, byte databaseJob,
    string description)
{
    Equal(name, definition.Name, description + " name");
    Equal(magicId, definition.MagicId, description + " magic id");
    Equal(effectType, definition.EffectType, description + " effect type");
    Equal(databaseJob, definition.DatabaseJob,
        description + " database job");
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
