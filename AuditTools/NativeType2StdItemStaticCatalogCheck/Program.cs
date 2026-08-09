using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

CheckVerifiedGoldSentinel();
CheckTerminalGateAndFullProjection();
CheckAtomicPublication();
CheckUserEnginePublicationAndNativeIndices();
CheckStartupPublicationOrder();

Console.WriteLine("PASS NativeType2StdItemStaticCatalogCheck " +
                  "command=0068 sentinel=140-byte-zero/gold-gbk " +
                  "terminal=required publication=atomic scripts=retained " +
                  "startup=before-rewards/mall");

static void CheckVerifiedGoldSentinel()
{
    Equal(140, NativeType2StdItemStaticCatalog
        .VerifiedGoldSentinelNativeSize, "sentinel native size");
    SequenceEqual(new byte[] { 0x04, 0xBD, 0xF0, 0xB1, 0xD2 },
        NativeType2StdItemStaticCatalog
            .CopyVerifiedGoldSentinelNameShortString(),
        "sentinel native ShortString");
    var nativeImage = NativeType2StdItemStaticCatalog
        .CopyVerifiedGoldSentinelNativeImage();
    Equal(140, nativeImage.Length, "sentinel native image length");
    Check(nativeImage.AsSpan(0, 4).SequenceEqual(new byte[4]),
        "sentinel native index is zero");
    Check(nativeImage.AsSpan(4, 5).SequenceEqual(
            new byte[] { 0x04, 0xBD, 0xF0, 0xB1, 0xD2 }),
        "sentinel native name image");
    Check(nativeImage.AsSpan(9).IndexOfAnyExcept((byte)0) < 0,
        "sentinel remaining native bytes are zero");

    var item = NativeType2StdItemStaticCatalog
        .CreateVerifiedGoldSentinel();
    Equal("金币", item.Name, "sentinel GBK name");
    Equal((ushort)0, item.NativeWireIndex, "sentinel list index");
    Check(item.NativeStdItemWireBody == null,
        "sentinel has no fabricated wire body");
    Check(item.NativeItemScriptPath == null,
        "sentinel has no fabricated script");
    Check(item.NativeItemExtAbilIdents.All(value => value == 0)
          && item.NativeItemExtAbilValues.All(value => value == 0),
        "sentinel extension values remain zero");

    foreach (var field in typeof(GoodItem).GetFields(
                 BindingFlags.Instance | BindingFlags.Public))
    {
        if (field.Name is nameof(GoodItem.Name)
            or nameof(GoodItem.NativeStdItemWireBody)
            or nameof(GoodItem.NativeItemScriptPath)
            or nameof(GoodItem.NativeItemExtAbilIdents)
            or nameof(GoodItem.NativeItemExtAbilValues)
            or nameof(GoodItem.NativeItemExtAbilParsed))
            continue;

        var value = field.GetValue(item);
        if (field.FieldType == typeof(bool))
            Check(!(bool)value, "sentinel boolean zero: " + field.Name);
        else if (field.FieldType.IsEnum)
            Equal(0, Convert.ToInt32(value),
                "sentinel enum zero: " + field.Name);
        else if (field.FieldType.IsPrimitive)
            Equal(0m, Convert.ToDecimal(value),
                "sentinel numeric zero: " + field.Name);
    }
}

static void CheckTerminalGateAndFullProjection()
{
    var snapshot = NativeType2StdItemSnapshotState
        .CreateForVerifiedOriginalStartup();
    snapshot.Consume(CreatePacket(CreateBody(1, "测试甲",
        "药品体力值回复:12|药品魔法值回复:34", configure: body =>
        {
            body[0x14] = 5;
            body[0x15] = 9;
            body[0x16] = 2;
            body[0x17] = 3;
            WriteUInt16(body, 0x02, 0x1234);
            WriteUInt16(body, 0x18, 101);
            WriteUInt16(body, 0x1A, 102);
            WriteUInt16(body, 0x1C, 103);
            WriteUInt16(body, 0x1E, 104);
            WriteUInt16(body, 0x20, 105);
            WriteUInt16(body, 0x22, 106);
            WriteUInt16(body, 0x24, 107);
            WriteUInt16(body, 0x26, 108);
            WriteUInt16(body, 0x28, 109);
            WriteUInt16(body, 0x2A, 110);
            WriteUInt16(body, 0x2C, 111);
            WriteUInt16(body, 0x2E, 112);
            WriteUInt16(body, 0x30, 113);
            WriteUInt16(body, 0x32, 114);
            WriteUInt16(body, 0x34, 115);
            WriteUInt16(body, 0x36, 116);
            WriteUInt16(body, 0x38, 117);
            WriteUInt16(body, 0x3A, 118);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x3C),
                119000);
            body[0x40] = 120;
            body[0x41] = 121;
            WriteUInt16(body, 0x42, 122);
            WriteUInt16(body, 0x44, 123);
            WriteUInt16(body, 0x46, 124);
            WriteUInt16(body, 0x48, 125);
            WriteUInt16(body, 0x4A, 126);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x4C), 127);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x50), 128);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x54), 129);
            WriteUInt16(body, 0x58, 130);
            WriteUInt16(body, 0x5A, 131);
            WriteUInt16(body, 0x126, 132);
            body[0x128] = 133;
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x12C), 134);
            WriteUInt16(body, 0x130, 135);
        }), completed: false));

    var incomplete = new NativeType2StdItemStaticCatalog();
    ExpectThrows<InvalidDataException>(() => incomplete.Publish(snapshot),
        "incomplete snapshot must not publish");
    Check(!incomplete.Ready && incomplete.Count == 0,
        "incomplete snapshot leaves catalog empty");

    snapshot.Consume(CreatePacket(CreateBody(2, "测试乙",
        "不存在属性:9"), completed: true));
    Check(snapshot.Completed, "0068 terminal flag completes snapshot");

    var resolver = new RecordingNeedIdentifyResolver();
    var binder = new RecordingScriptBinder();
    var catalog = new NativeType2StdItemStaticCatalog();
    catalog.Publish(snapshot, resolver, binder);

    Check(catalog.Ready, "completed snapshot published");
    Equal(3, catalog.Count, "sentinel plus two wire records");
    Equal(2, catalog.Definitions.Count, "wire definition count");
    Equal("金币", catalog.Items[0].Name, "sentinel remains index zero");
    Equal("测试甲", catalog.Items[1].Name, "first wire item index");
    Equal("测试乙", catalog.Items[2].Name, "terminal wire item index");

    var first = catalog.Items[1];
    Equal((ushort)1, first.NativeWireIndex, "wire index mapped");
    Equal((ushort)0x1234, first.NativeReserved02, "reserved02 mapped");
    Equal((byte)5, first.StdMode, "StdMode mapped");
    Equal(GoodType.ITEM_WEAPON, first.ItemType, "item classification");
    Equal((byte)7, first.NeedIdentify, "NeedIdentify resolver");
    Equal((ushort)101, first.Looks, "Looks mapped");
    Equal((ushort)102, first.Weight, "Weight remains ushort");
    Equal((ushort)103, first.DuraMax, "DuraMax mapped");
    Equal((ushort)104, first.AniCount, "AniCount remains ushort");
    Equal((ushort)107, first.Ac, "AC mapped");
    Equal((ushort)108, first.Ac2, "MaxAC mapped");
    Equal(119000, first.Price, "Price mapped");
    Equal((ushort)132, first.OutLookWord, "OutLookWord mapped");
    Equal((byte)133, first.NeedJob, "NeedJob mapped");
    Equal(134, first.ItemLevel, "ItemLevel mapped");
    Equal((ushort)135, first.ItemConf, "ItemConf mapped");
    Equal((ushort)12, first.NativeDrugHealthBonus,
        "health extension mapped");
    Equal((ushort)34, first.NativeDrugSpellBonus,
        "spell extension mapped");
    Equal("bound://测试甲", first.NativeItemScriptPath,
        "bound item script attached");

    Check(!catalog.Items[2].NativeItemExtAbilParsed,
        "extension parse failure retains terminal item");
    Check(catalog.Items[2].NativeItemScriptPath == null,
        "compile-failed script is not attached");
    Check(catalog.Logs.Any(log => log.Contains("错误的道具属性")
                                  && log.Contains("测试乙")),
        "extension failure logged");
    Check(catalog.Logs.Any(log => log.Contains("物品脚本错误")
                                  && log.Contains("compile-test")),
        "script compile failure logged");
    Equal(2, resolver.Names.Count, "NeedIdentify called for wire items only");
    Equal(2, binder.Names.Count, "script binder called for wire items only");
    Equal("PsItemScript\\测试甲.pas", binder.RelativePaths[0],
        "native item script path");
    Check(ReferenceEquals(first, catalog.FindByName("测试甲")),
        "catalog name lookup");

    var mutable = catalog.CreateGoodItemList();
    mutable.Add(new GoodItem { Name = "运行期" });
    Equal(4, mutable.Count, "publication produces mutable runtime list");
    Equal(3, catalog.Count, "runtime list does not mutate publication");

    ExpectThrows<InvalidOperationException>(() =>
        catalog.Publish(snapshot, resolver, binder),
        "startup catalog is one-shot");
    Equal(2, binder.Names.Count, "one-shot rejection has no script side effect");

    var wrongBaseline = new NativeType2StdItemSnapshotState(0);
    wrongBaseline.Consume(CreatePacket(Array.Empty<byte>(), completed: true));
    ExpectThrows<InvalidDataException>(() =>
        new NativeType2StdItemStaticCatalog().Publish(wrongBaseline),
        "catalog rejects an unverified startup baseline");

    var shortTerminal = NativeType2StdItemSnapshotState
        .CreateForVerifiedOriginalStartup();
    shortTerminal.Consume(CreatePacket(Array.Empty<byte>(), completed: true));
    var sentinelOnly = new NativeType2StdItemStaticCatalog();
    sentinelOnly.Publish(shortTerminal);
    Equal(1, sentinelOnly.Count,
        "native short terminal publishes verified local sentinel only");
}

static void CheckAtomicPublication()
{
    var snapshot = NativeType2StdItemSnapshotState
        .CreateForVerifiedOriginalStartup();
    snapshot.Consume(CreatePacket(CreateBody(1, "原子", ""),
        completed: true));

    using var entered = new ManualResetEventSlim(false);
    using var release = new ManualResetEventSlim(false);
    var binder = new BlockingBinder(entered, release);
    var catalog = new NativeType2StdItemStaticCatalog();

    var publish = Task.Run(() => catalog.Publish(snapshot, null, binder));
    Check(entered.Wait(TimeSpan.FromSeconds(5)),
        "blocking binder entered before commit");
    Check(!catalog.Ready && catalog.Count == 0
          && catalog.Items.Count == 0,
        "catalog exposes no partial build");
    release.Set();
    publish.GetAwaiter().GetResult();
    Check(catalog.Ready && catalog.Count == 2,
        "catalog exposes complete publication after commit");
}

static void CheckUserEnginePublicationAndNativeIndices()
{
    var snapshot = NativeType2StdItemSnapshotState
        .CreateForVerifiedOriginalStartup();
    snapshot.Consume(CreatePacket(CreateBody(1, "索引物品", "",
        body => WriteUInt16(body, 0x1A, 88)), completed: true));
    var catalog = new NativeType2StdItemStaticCatalog();
    catalog.Publish(snapshot);

    M2Share.g_GameLogItemNameList = new List<string>
    {
        Grobal2.sSTRING_GOLDNAME,
        M2Share.g_sHumanDieEvent,
        M2Share.g_Config.sGameGoldName,
        M2Share.g_Config.sGamePointName
    };

    var engine = new UserEngine();
    Check(engine.TryPublishNativeStdItemDefinitions(catalog, out var error),
        "UserEngine native publication: " + error);
    Check(engine.NativeStdItemDefinitionsPublished,
        "UserEngine publication flag");
    Check(ReferenceEquals(engine.StdItemList[0], engine.GetStdItem(0))
          && engine.GetStdItemName(0) == "金币",
        "native index zero is the local gold sentinel");
    Check(ReferenceEquals(engine.StdItemList[1], engine.GetStdItem(1))
          && engine.GetStdItemName(1) == "索引物品"
          && engine.GetStdItemWeight(1) == 88,
        "native wire index directly addresses live list");
    Equal(1, engine.GetStdItemIdx("索引物品"),
        "name lookup returns native wire index");

    TUserItem created = null;
    Check(engine.CopyToUserItemFromName("索引物品", ref created)
          && created != null && created.wIndex == 1,
        "created user item retains native wire index");
    Check(M2Share.g_boGameLogGold && M2Share.g_boGameLogHumanDie
          && M2Share.g_boGameLogGameGold
          && M2Share.g_boGameLogGamePoint,
        "legacy game-log enable flags initialized after publication");
    Check(!engine.TryPublishNativeStdItemDefinitions(catalog, out error)
          && error.Contains("已发布"),
        "UserEngine refuses a second whole-table replacement");
}

static void CheckStartupPublicationOrder()
{
    var root = FindRepositoryRoot();
    var gameApp = File.ReadAllText(Path.Combine(root, "GameSvr",
        "GameApp.cs"));
    var initializeStart = gameApp.IndexOf("public bool Initialize()",
        StringComparison.Ordinal);
    var initializeEnd = gameApp.IndexOf("public static bool ReloadNormalPrize",
        initializeStart, StringComparison.Ordinal);
    Check(initializeStart >= 0 && initializeEnd > initializeStart,
        "GameApp.Initialize source boundary");
    var initialize = gameApp.Substring(initializeStart,
        initializeEnd - initializeStart);

    var loadAuditNames = Position(initialize,
        "M2Share.LoadGameLogItemNameList()");
    var startDb = Position(initialize, "M2Share.DataServer.Start()");
    var waitDefinitions = Position(initialize,
        "TryWaitForNativeDefinitionInitialization(");
    var publishItems = Position(initialize,
        "TryPublishNativeStdItemDefinitions(");
    var firstRewardConsumer = Position(initialize,
        "M2Share.GoldIDRewards =");
    var mallConsumer = Position(initialize,
        "Mall.MallManager.Instance.LoadMallItems()");
    var publishMonsters = Position(initialize,
        "TryPublishNativeMonsterDefinitions(");
    var publishMagic = Position(initialize,
        "TryPublishNativeMagicDefinitions(");
    var firstMapConsumer = Position(initialize, "Maps.LoadMinMap()");

    Check(loadAuditNames < startDb && startDb < waitDefinitions
          && waitDefinitions < publishItems
          && publishItems < publishMonsters
          && publishMonsters < publishMagic
          && publishMagic < firstRewardConsumer
          && publishMagic < mallConsumer
          && mallConsumer < firstMapConsumer,
        "native static definitions must publish as stditem -> monster -> magic " +
        "before rewards, Mall, and maps");
    Equal(1, CountOccurrences(initialize,
        "M2Share.DataServer.Start()"), "single native DB start");
    Equal(1, CountOccurrences(initialize,
        "TryWaitForNativeDefinitionInitialization("),
        "single native definition wait");
    Equal(1, CountOccurrences(initialize,
        "TryPublishNativeStdItemDefinitions("),
        "single native standard-item publication");
    Equal(1, CountOccurrences(initialize,
        "TryPublishNativeMonsterDefinitions("),
        "single native monster publication");
    Equal(1, CountOccurrences(initialize,
        "TryPublishNativeMagicDefinitions("),
        "single native magic publication");
    Check(!initialize.Contains("CommonDB.LoadItemsDB()",
            StringComparison.Ordinal),
        "startup still reads divergent MySQL stditems");

    var initializeServerStart = gameApp.IndexOf(
        "public void InitializeServer()", StringComparison.Ordinal);
    Check(initializeServerStart >= 0
          && gameApp.IndexOf("M2Share.DataServer = new DBService()",
              initializeServerStart, StringComparison.Ordinal) >
              initializeServerStart
          && gameApp.IndexOf("M2Share.UserEngine = new UserEngine()",
              initializeServerStart, StringComparison.Ordinal) >
              initializeServerStart
          && gameApp.IndexOf("M2Share.PasEngine = new PasEngine.PasScriptHost",
              initializeServerStart, StringComparison.Ordinal) >
              initializeServerStart,
        "InitializeServer must create DB/User/Pas owners before Initialize");

    var appService = File.ReadAllText(Path.Combine(root, "GameSvr",
        "AppService.cs"));
    Check(appService.Contains("_mirApp.InitializeServer();",
              StringComparison.Ordinal)
          && appService.Contains("if (!_mirApp.Initialize())",
              StringComparison.Ordinal),
        "AppService two-phase startup contract");
}

static byte[] CreatePacket(byte[] body, bool completed)
{
    var packet = new byte[NativeType2StdItemSnapshotState.HeaderSize
                          + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(packet,
        NativeType2StdItemSnapshotState.Command);
    if (completed)
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), 1);
    body.CopyTo(packet, NativeType2StdItemSnapshotState.HeaderSize);
    return packet;
}

static byte[] CreateBody(int index, string name, string extension,
    Action<byte[]> configure = null)
{
    var body = new byte[NativeType2StdItemSnapshotState.BodySize];
    WriteUInt16(body, 0, unchecked((ushort)index));
    WriteShortString(body, 0x04, 15, name);
    WriteShortString(body, 0x5C, 200, extension);
    configure?.Invoke(body);
    return body;
}

static void WriteShortString(byte[] body, int offset, int capacity,
    string value)
{
    var bytes = Encoding.GetEncoding(936).GetBytes(value);
    if (bytes.Length > capacity)
        throw new InvalidOperationException("test short string overflow");
    body[offset] = unchecked((byte)bytes.Length);
    bytes.CopyTo(body, offset + 1);
}

static void WriteUInt16(byte[] body, int offset, ushort value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), value);

static void SequenceEqual(byte[] expected, byte[] actual,
    string description)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(description);
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void ExpectThrows<TException>(Action action, string description)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(description);
}

static int Position(string source, string marker)
{
    var index = source.IndexOf(marker, StringComparison.Ordinal);
    if (index < 0)
        throw new InvalidOperationException("missing source marker: " + marker);
    return index;
}

static int CountOccurrences(string source, string marker)
{
    var count = 0;
    var cursor = 0;
    while ((cursor = source.IndexOf(marker, cursor,
               StringComparison.Ordinal)) >= 0)
    {
        count++;
        cursor += marker.Length;
    }
    return count;
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln"))
                && Directory.Exists(Path.Combine(directory.FullName,
                    "GameSvr"))
                && Directory.Exists(Path.Combine(directory.FullName,
                    "AuditTools")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("repository root not found");
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory,
        "PlayerUpgradeExp.ini"), "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

sealed class RecordingNeedIdentifyResolver :
    INativeType2StdItemNeedIdentifyResolver
{
    public List<string> Names { get; } = new();

    public bool TryResolve(ReadOnlyMemory<byte> nativeNameBytes,
        out byte needIdentify)
    {
        var name = Encoding.GetEncoding(936).GetString(nativeNameBytes.Span);
        Names.Add(name);
        needIdentify = name == "测试甲" ? (byte)7 : (byte)99;
        return name == "测试甲";
    }
}

sealed class RecordingScriptBinder : INativeType2StdItemScriptBinder
{
    public List<string> Names { get; } = new();
    public List<string> RelativePaths { get; } = new();

    public NativeType2StdItemScriptBindingResult Bind(
        NativeType2StdItemDefinition definition,
        ReadOnlyMemory<byte> relativePathBytes)
    {
        Names.Add(definition.Name);
        RelativePaths.Add(Encoding.GetEncoding(936)
            .GetString(relativePathBytes.Span));
        return definition.Name == "测试甲"
            ? NativeType2StdItemScriptBindingResult.Bound(
                "bound://测试甲", new object())
            : NativeType2StdItemScriptBindingResult.CompileFailed(
                definition.ScriptRelativePath, "compile-test");
    }
}

sealed class BlockingBinder : INativeType2StdItemScriptBinder
{
    private readonly ManualResetEventSlim _entered;
    private readonly ManualResetEventSlim _release;

    public BlockingBinder(ManualResetEventSlim entered,
        ManualResetEventSlim release)
    {
        _entered = entered;
        _release = release;
    }

    public NativeType2StdItemScriptBindingResult Bind(
        NativeType2StdItemDefinition definition,
        ReadOnlyMemory<byte> relativePathBytes)
    {
        _entered.Set();
        if (!_release.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("atomic publication test timeout");
        return NativeType2StdItemScriptBindingResult.FileNotFound(
            definition.ScriptRelativePath);
    }
}
