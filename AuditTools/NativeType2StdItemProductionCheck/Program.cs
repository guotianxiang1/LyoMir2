using System.Buffers.Binary;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cleanupBootstrap = PrepareM2ShareBootstrap();

try
{
    VerifyCompleteGoodItemPublication();
    VerifySequenceNumericAndExtensionBoundaries();
    VerifyScriptBindingAndAppendOrder();
    VerifyConcurrentSequenceGate();
    VerifyPasScriptPreload();
    VerifyGameLogItemNameListReadOnlyLoad();
    Console.WriteLine(
        "PASS NativeType2StdItemProductionCheck " +
        "target=UserEngine-GoodItem fields=0x134-complete-u16 " +
        "item-ext=six-slots+drug-derived order=idx-equals-live-count " +
        "duplicates=linear-first concurrency=single-append " +
        "script=preload-only+retain-on-failure " +
        "need-identify=GBK-read-only");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeType2StdItemProductionCheck FAIL: {exception}");
    return 1;
}
finally
{
    cleanupBootstrap();
}

static void VerifyCompleteGoodItemPublication()
{
    var original = new GoodItem { Name = "Duplicate" };
    var items = new List<GoodItem> { original };
    var publisher = new NativeType2StdItemRuntimePublisher(items,
        needIdentifyResolver: new FixedNeedIdentifyResolver(true, 7));
    var packet = Packet(1, "duplicate",
        "药品体力值回复:10|药品魔法值回复:20|" +
        "药品魔血值回复:30|药品体力值回复:65535");
    var body = Body(packet);

    WriteUInt16(body, 0x02, 0x0203);
    body[0x14] = 10;
    body[0x15] = 0x15;
    body[0x16] = 0x16;
    body[0x17] = 0x17;
    WriteUInt16(body, 0x18, 0x1819);
    WriteUInt16(body, 0x1A, 0xA1A2);
    WriteUInt16(body, 0x1C, 0x1C1D);
    WriteUInt16(body, 0x1E, 0xB1B2);
    WriteUInt16(body, 0x20, 0xC1C2);
    for (var offset = 0x22; offset <= 0x3A; offset += 2)
        WriteUInt16(body, offset, 0x1000 + offset);
    WriteInt32(body, 0x3C, -1234567);
    body[0x40] = 0x40;
    body[0x41] = 0x41;
    WriteUInt16(body, 0x42, 0x4243);
    WriteUInt16(body, 0x44, 0x4445);
    WriteUInt16(body, 0x46, 0x4647);
    WriteUInt16(body, 0x48, 0x4849);
    WriteUInt16(body, 0x4A, 0x4A4B);
    WriteInt32(body, 0x4C, unchecked((int)0x8C4C4D4E));
    WriteInt32(body, 0x50, 0x50515253);
    WriteInt32(body, 0x54, 0x54555657);
    WriteUInt16(body, 0x58, 0x5859);
    WriteUInt16(body, 0x5A, 0x5A5B);
    WriteUInt16(body, 0x126, 0x2627);
    body[0x128] = 0x28;
    WriteInt32(body, 0x12C, unchecked((int)0xCC2C2D2E));
    WriteUInt16(body, 0x130, 0x3031);

    var result = publisher.Apply(packet);
    Equal(NativeType2StdItemAppendStatus.Appended, result.Status,
        "complete map status");
    Equal(2, items.Count, "live list append");
    var item = items[1];
    Check(ReferenceEquals(item, result.Item), "result/live item identity");
    Equal((ushort)1, item.NativeWireIndex, "wire index");
    Equal((ushort)0x0203, item.NativeReserved02, "reserved +02");
    Equal("duplicate", item.Name, "GBK name");
    Equal((byte)10, item.StdMode, "StdMode");
    Equal(GoodType.ITEM_ARMOR, item.ItemType, "GoodType classification");
    Equal((byte)0x15, item.Shape, "Shape");
    Equal(0x16, item.Need, "Need");
    Equal((short)0x17, item.Source, "Source");
    Equal((ushort)0x1819, item.Looks, "Looks");
    Equal((ushort)0xA1A2, item.Weight, "Weight u16");
    Equal((ushort)0x1C1D, item.DuraMax, "DuraMax");
    Equal((ushort)0xB1B2, item.AniCount, "AniCount u16");
    Equal((ushort)0xC1C2, item.Reserved, "NeedConf u16");
    Equal(0x1022, item.NeedLevel, "NeedLevel");
    Equal((ushort)0x1024, item.Ac, "AC");
    Equal((ushort)0x1026, item.Ac2, "MaxAC");
    Equal((ushort)0x1028, item.Mac, "MAC");
    Equal((ushort)0x102A, item.Mac2, "MaxMAC");
    Equal((ushort)0x102C, item.Dc, "DC");
    Equal((ushort)0x102E, item.Dc2, "MaxDC");
    Equal((ushort)0x1030, item.Mc, "MC");
    Equal((ushort)0x1032, item.Mc2, "MaxMC");
    Equal((ushort)0x1034, item.Sc, "SC");
    Equal((ushort)0x1036, item.Sc2, "MaxSC");
    Equal((ushort)0x1038, item.Cc, "CC");
    Equal((ushort)0x103A, item.Cc2, "MaxCC");
    Equal(-1234567, item.Price, "Price");
    Equal(0x40, item.Outlook, "OutLook byte");
    Equal((byte)0x41, item.AntiqueLevel, "AntiqueLevel");
    Equal((ushort)0x4243, item.ItemScore, "ItemScore");
    Equal((ushort)0x4445, item.SuitEquipType, "SuitEquipType");
    Equal((ushort)0x4647, item.BaseEffectId, "BaseEffectID");
    Equal((ushort)0x4849, item.WordParam1, "wParam1");
    Equal((ushort)0x4A4B, item.WordParam2, "wParam2");
    Equal(unchecked((int)0x8C4C4D4E), item.IntParam1, "intParam1");
    Equal(0x50515253, item.IntParam2, "intParam2");
    Equal(0x54555657, item.IntParam3, "intParam3");
    Equal((ushort)0x5859, item.MaxSteelLevel, "MaxSteelLv");
    Equal((ushort)0x5A5B, item.MaxVeinsLevel, "MaxVeinsLv");
    Equal((ushort)0x2627, item.OutLookWord, "OutLook word");
    Equal((byte)0x28, item.NeedJob, "NeedJob");
    Equal(unchecked((int)0xCC2C2D2E), item.ItemLevel, "ItemLevel");
    Equal((ushort)0x3031, item.ItemConf, "ItemConf");
    Equal((byte)7, item.NeedIdentify, "NeedIdentify");
    Check(item.NativeItemExtAbilParsed, "extension parsed marker");
    EqualSequence(new ushort[] { 33, 32, 96, 33, 0, 0 },
        item.NativeItemExtAbilIdents, "six extension idents");
    EqualSequence(new ushort[] { 10, 20, 30, 65535, 0, 0 },
        item.NativeItemExtAbilValues, "six extension values");
    Equal((ushort)9, item.NativeDrugHealthBonus,
        "drug health unchecked sum");
    Equal((ushort)20, item.NativeDrugSpellBonus, "drug spell sum");
    Equal((ushort)30, item.NativeDrugJobBonus, "drug job sum");
    EqualSequence(body.ToArray(), item.NativeStdItemWireBody,
        "full native body retained");
    Check(publisher.TryGetFirstByName("DUPLICATE", out var found),
        "duplicate lookup");
    Check(ReferenceEquals(original, found), "first duplicate retained");
}

static void VerifySequenceNumericAndExtensionBoundaries()
{
    var items = new List<GoodItem>();
    var publisher = new NativeType2StdItemRuntimePublisher(items);
    var rejected = publisher.Apply(Packet(1, "Wrong"));
    Equal(NativeType2StdItemAppendStatus.SequenceRejected,
        rejected.Status, "sequence rejection");
    Equal(0, items.Count, "sequence rejection leaves live list unchanged");
    EqualSequence(new[]
    {
        NativeType2StdItemRuntimeProtocol.SequenceError,
        "运行期添加道具:Wrong"
    }, rejected.Logs, "sequence exact logs");

    ExpectThrows<NativeType2StdItemNumericException>(() =>
        publisher.Apply(Packet(0, "BadNumber", "攻击下限:no")),
        "numeric conversion escapes publisher");
    Equal(0, items.Count, "numeric failure occurs before append");

    var invalid = publisher.Apply(Packet(0, "BadExt", "不存在:1"));
    Equal(NativeType2StdItemAppendStatus.AppendedWithExtensionError,
        invalid.Status, "unknown extension status");
    Equal(1, items.Count, "unknown extension retained");
    Check(!items[0].NativeItemExtAbilParsed,
        "unknown extension visible on GoodItem");
    EqualSequence(new[]
    {
        "[error]: 错误的道具属性：BadExt: 不存在:1",
        "运行期添加道具:BadExt"
    }, invalid.Logs, "extension exact logs");
}

static void VerifyScriptBindingAndAppendOrder()
{
    var successItems = new List<GoodItem>();
    var successPath = @"D:\Game\PsItemScript\Bound.pas";
    var success = new NativeType2StdItemRuntimePublisher(successItems,
        scriptBinder: new FixedScriptBinder(
            NativeType2StdItemScriptBindingResult.Bound(
                successPath, new object()))).Apply(Packet(0, "Bound"));
    Equal(NativeType2StdItemScriptBindingStatus.Bound,
        success.ScriptBinding.Status, "script bound status");
    Equal(successPath, successItems[0].NativeItemScriptPath,
        "bound path retained on GoodItem");

    var missingItems = new List<GoodItem>();
    var missing = new NativeType2StdItemRuntimePublisher(missingItems,
        scriptBinder: new FixedScriptBinder(
            NativeType2StdItemScriptBindingResult.FileNotFound(
                @"PsItemScript\Missing.pas")))
        .Apply(Packet(0, "Missing"));
    Equal(NativeType2StdItemScriptBindingStatus.FileNotFound,
        missing.ScriptBinding.Status, "missing script status");
    Equal(1, missingItems.Count, "missing script retains item");
    EqualSequence(new[] { "运行期添加道具:Missing" }, missing.Logs,
        "missing script adds no invented log");

    var failedItems = new List<GoodItem>();
    var failedPath = @"D:\Game\PsItemScript\Broken.pas";
    var failed = new NativeType2StdItemRuntimePublisher(failedItems,
        scriptBinder: new FixedScriptBinder(
            NativeType2StdItemScriptBindingResult.CompileFailed(
                failedPath, "line 9")))
        .Apply(Packet(0, "Broken"));
    Equal(NativeType2StdItemScriptBindingStatus.CompileFailed,
        failed.ScriptBinding.Status, "compile failure status");
    Equal(1, failedItems.Count, "compile failure occurs after append");
    Check(ReferenceEquals(failed.Item, failedItems[0]),
        "compile failure retained item identity");
    EqualSequence(new[]
    {
        "[ERROR]: 致命错误 " + failedPath + "物品脚本错误:line 9",
        "运行期添加道具:Broken"
    }, failed.Logs, "compile failure exact logs");
}

static void VerifyConcurrentSequenceGate()
{
    var items = new List<GoodItem>();
    var publisher = new NativeType2StdItemRuntimePublisher(items);
    var results = new NativeType2StdItemRuntimePublishResult[2];
    Parallel.For(0, 2, index =>
        results[index] = publisher.Apply(Packet(0, "Concurrent")));

    Equal(1, publisher.Count, "concurrent same index single append");
    Equal(1, results.Count(result => result.Status ==
        NativeType2StdItemAppendStatus.Appended),
        "concurrent appended result count");
    Equal(1, results.Count(result => result.Status ==
        NativeType2StdItemAppendStatus.SequenceRejected),
        "concurrent rejected result count");
}

static void VerifyPasScriptPreload()
{
    var root = NewTempDirectory("pas");
    try
    {
        var itemDirectory = Path.Combine(root, "PsItemScript");
        Directory.CreateDirectory(itemDirectory);
        var validPath = Path.Combine(itemDirectory, "Valid.pas");
        File.WriteAllText(validPath, "program Mir2; begin end.");
        var brokenPath = Path.Combine(itemDirectory, "Broken.pas");
        File.WriteAllText(brokenPath,
            "program Mir2; begin if then end.");

        var host = new PasScriptHost(root);
        Check(host.TryPreloadItemScript("Valid", out var loadedPath,
                out var validError),
            "valid item script preload: " + validError);
        Equal(Path.GetFullPath(validPath), Path.GetFullPath(loadedPath),
            "valid preload path");
        Check(!host.TryPreloadItemScript("Missing", out var missingPath,
                out var missingError),
            "missing item script preload");
        Check(missingPath == null && string.IsNullOrEmpty(missingError),
            "missing preload boundary");
        Check(!host.TryPreloadItemScript("Broken", out var attemptedPath,
                out var brokenError),
            "broken item script rejected");
        Equal(Path.GetFullPath(brokenPath), Path.GetFullPath(attemptedPath),
            "broken attempted path");
        Check(!string.IsNullOrWhiteSpace(brokenError),
            "broken script compile error surfaced");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void VerifyGameLogItemNameListReadOnlyLoad()
{
    var root = NewTempDirectory("gamelog");
    var oldConfigPath = M2Share.sConfigPath;
    var oldConfig = M2Share.g_Config;
    var oldList = M2Share.g_GameLogItemNameList;
    try
    {
        var envirDirectory = Path.Combine(root, "Envir");
        Directory.CreateDirectory(envirDirectory);
        var path = Path.Combine(envirDirectory,
            "GameLogItemNameList.txt");
        File.WriteAllLines(path,
            new[] { "  IdentifySword  ", "测试药水" },
            HUtil32.GbkEncoding);

        M2Share.sConfigPath = root + Path.DirectorySeparatorChar;
        M2Share.g_Config = new GameSvrConfig
        {
            sEnvirDir = "Envir" + Path.DirectorySeparatorChar
        };
        M2Share.g_GameLogItemNameList = new List<string> { "stale" };
        Check(M2Share.LoadGameLogItemNameList(),
            "GameLogItemNameList read");
        EqualSequence(new[] { "IdentifySword", "测试药水" },
            M2Share.g_GameLogItemNameList, "GBK read and trim");
        Equal((byte)1,
            M2Share.GetGameLogItemNameList("identifysword"),
            "NeedIdentify case-insensitive match");
        Equal((byte)0, M2Share.GetGameLogItemNameList("missing"),
            "NeedIdentify missing");

        File.Delete(path);
        Check(!M2Share.LoadGameLogItemNameList(),
            "missing list returns false");
        Check(!File.Exists(path), "missing list was created");
        EqualSequence(new[] { "IdentifySword", "测试药水" },
            M2Share.g_GameLogItemNameList,
            "missing list does not mutate current values");
    }
    finally
    {
        M2Share.sConfigPath = oldConfigPath;
        M2Share.g_Config = oldConfig;
        M2Share.g_GameLogItemNameList = oldList;
        Directory.Delete(root, true);
    }
}

static byte[] Packet(ushort index, string name,
    string itemExtAbil = "")
{
    var packet = new byte[NativeType2StdItemRuntimeProtocol.PacketSize];
    BinaryPrimitives.WriteUInt16LittleEndian(packet,
        NativeType2StdItemRuntimeProtocol.Command);
    var body = Body(packet);
    WriteUInt16(body, 0, index);
    WriteShortString(body, 0x04, 15, name);
    WriteShortString(body, 0x5C, 200, itemExtAbil);
    return packet;
}

static Span<byte> Body(byte[] packet) => packet.AsSpan(
    NativeType2StdItemRuntimeProtocol.HeaderSize,
    NativeType2StdItemRuntimeProtocol.BodySize);

static void WriteShortString(Span<byte> destination, int offset,
    int capacity, string value)
{
    var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
    if (bytes.Length > capacity)
        throw new InvalidOperationException("fixture short string overflow");
    destination[offset] = checked((byte)bytes.Length);
    bytes.CopyTo(destination.Slice(offset + 1));
}

static void WriteUInt16(Span<byte> destination, int offset, int value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2),
        unchecked((ushort)value));

static void WriteInt32(Span<byte> destination, int offset, int value) =>
    BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4),
        value);

static string NewTempDirectory(string suffix)
{
    var path = Path.Combine(Path.GetTempPath(),
        "LyoMir2-StdItem00CA-" + suffix + "-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static Action PrepareM2ShareBootstrap()
{
    var configRoot = AppContext.BaseDirectory;
    var shareRoot = Path.GetFullPath(Path.Combine(configRoot, "..", "Share"));
    var setupPath = Path.Combine(configRoot, "!Setup.txt");
    var commandPath = Path.Combine(configRoot, "Command.conf");
    var expPath = Path.Combine(shareRoot, "PlayerUpgradeExp.ini");
    var paths = new[] { setupPath, commandPath, expPath };
    if (paths.Any(File.Exists))
    {
        throw new InvalidOperationException(
            "isolated audit bootstrap path already contains config files");
    }

    Directory.CreateDirectory(shareRoot);
    File.WriteAllText(setupPath, "[Setup]" + Environment.NewLine,
        HUtil32.GbkEncoding);
    File.WriteAllText(commandPath, "[Command]" + Environment.NewLine,
        HUtil32.GbkEncoding);
    File.WriteAllText(expPath, "[PlayerLevelExp]" + Environment.NewLine,
        HUtil32.GbkEncoding);

    return () =>
    {
        foreach (var path in paths)
        {
            if (File.Exists(path)) File.Delete(path);
        }
        if (Directory.Exists(shareRoot)
            && !Directory.EnumerateFileSystemEntries(shareRoot).Any())
            Directory.Delete(shareRoot);
    };
}

static void Check(bool condition, string description)
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

sealed class FixedNeedIdentifyResolver :
    INativeType2StdItemNeedIdentifyResolver
{
    private readonly bool _found;
    private readonly byte _value;

    public FixedNeedIdentifyResolver(bool found, byte value)
    {
        _found = found;
        _value = value;
    }

    public bool TryResolve(ReadOnlyMemory<byte> nativeNameBytes,
        out byte needIdentify)
    {
        needIdentify = _value;
        return _found;
    }
}

sealed class FixedScriptBinder : INativeType2StdItemScriptBinder
{
    private readonly NativeType2StdItemScriptBindingResult _result;

    public FixedScriptBinder(
        NativeType2StdItemScriptBindingResult result) => _result = result;

    public NativeType2StdItemScriptBindingResult Bind(
        NativeType2StdItemDefinition definition,
        ReadOnlyMemory<byte> relativePathBytes) => _result;
}
