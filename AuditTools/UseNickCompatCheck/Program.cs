extern alias dbsvr;

using System.Buffers.Binary;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;
using NativeHumanDataCodec = global::DBSvr.Core.NativeHumanDataCodec;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
// M2Share 的静态构造会加载 Share/*.ini，第一个碰到 M2Share 的测试就会触发它，
// 所以配置必须在任何测试之前铺好（原先只在 TestNativeBalance 里调用，
// 而 TestSetNickLFCommandAndMirror 更早就触碰了 M2Share）。
PrepareRuntimeConfig();
TestPrizeFileAndCycle();
var enabledNickLinFuState = TestNativeStartupSwitch();
TestSetNickLFCommandAndMirror();
TestNativeRecordRoundTrip();
TestProtobufRoundTrip();
TestNativeBalance();
TestPasPropertyAndMethods(enabledNickLinFuState);
TestSourceContracts();

Console.WriteLine(
    "PASS UseNick GBK=4x25 cycle=1000 costs=1/10/100 callbacks=3 " +
    "NickLinFu=native:0x1CC protobuf=61 switch=byte1:0x02 multiplier=INI " +
    "SetNickLF=249 substitutes=0");
return;

static void TestPrizeFileAndCycle()
{
    var directory = Path.Combine(Path.GetTempPath(),
        "UseNickCompatCheck-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var fileName = Path.Combine(directory, "SearchNormalPrizeNew.Txt");
        var lines = new List<string>();
        for (var pool = 0; pool < NativeNickPrizeManager.PoolCount; pool++)
        for (var entry = 0; entry < NativeNickPrizeManager.EntriesPerPool; entry++)
            lines.Add($"\u7075\u7b26_{pool}_{entry}:{entry + 1}");
        File.WriteAllLines(fileName, lines, Encoding.GetEncoding(936));

        var thresholdCalls = 0;
        var prizeCalls = 0;
        int Random(int maximum)
        {
            if (maximum == NativeNickPrizeManager.CycleSize)
                return thresholdCalls++ == 0 ? 149 : 499;
            return prizeCalls++ % maximum;
        }

        Assert(NativeNickPrizeManager.TryLoad(fileName, Random, out var manager,
            out var error), "GBK prize file load failed: " + error);
        Equal(4, NativeNickPrizeManager.PoolCount, "prize pool count");
        Equal(25, manager.GetPool(0).Count, "prize entries per pool");
        EqualString("\u7075\u7b26_3_24", manager.GetPool(3)[24].ItemName,
            "GBK final prize name");
        Equal(25, manager.GetPool(3)[24].Count, "prize count is not a weight");
        Equal(150, manager.WinningThreshold, "initial random threshold");

        var specialCount = 0;
        for (var i = 0; i < 11; i++)
        {
            Assert(manager.TrySelect(3, out var prize, out var special),
                "type 3 selection rejected");
            Assert(prize != null, "type 3 selection returned no prize");
            if (special) specialCount++;
        }
        Equal(1, specialCount, "threshold must report only once per cycle");
        Equal(0, manager.CyclePosition, "cycle resets only after exceeding 1000");
        Equal(500, manager.WinningThreshold, "next cycle threshold");

        File.WriteAllLines(fileName, lines.Take(99), Encoding.GetEncoding(936));
        Assert(!NativeNickPrizeManager.TryLoad(fileName, Random, out _, out _),
            "99-row prize file was accepted");
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static NativeNickLinFuState TestNativeStartupSwitch()
{
    var directory = Path.Combine(Path.GetTempPath(),
        "NickLinFuStateCheck-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(directory, "Config"));
    try
    {
        File.WriteAllText(Path.Combine(directory, "Mir2Actor.ini"),
            "[Setup]\r\nLFMultiple=2\r\n", Encoding.GetEncoding(936));
        var switches = new byte[] { 0xB8, 0x04, 0x00, 0x04, 0x01 };
        File.WriteAllBytes(Path.Combine(directory, "Config", "ServerSwitch.Bin"),
            switches);

        Assert(NativeNickLinFuState.TryLoad(directory, out var disabled, out var error),
            "disabled native switch load failed: " + error);
        Equal(2, disabled.Multiplier, "startup LFMultiple");
        Assert(!disabled.Enabled, "baseline byte1=0x04 enabled NickLinFu");

        switches[1] |= 0x02;
        File.WriteAllBytes(Path.Combine(directory, "Config", "ServerSwitch.Bin"),
            switches);
        Assert(NativeNickLinFuState.TryLoad(directory, out var enabled, out error),
            "enabled native switch load failed: " + error);
        Assert(enabled.Enabled, "ServerSwitch byte1 mask 0x02 was ignored");
        Equal(2, enabled.Multiplier, "enabled LFMultiple");
        return enabled;
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static void TestSetNickLFCommandAndMirror()
{
    var directory = Path.Combine(Path.GetTempPath(),
        "SetNickLFCompatCheck-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var state = NativeNickLinFuState.Disabled;
        Assert(!NativeNickLinFuState.TryEnableAndPersist(directory, 5,
                ref state, out _),
            "SetNickLF accepted missing Mir2Actor.ini");
        Assert(!state.Enabled, "missing Mir2Actor.ini changed runtime enable state");
        Equal(1, state.Multiplier, "missing Mir2Actor.ini changed multiplier");
        Assert(!File.Exists(Path.Combine(directory, "Mir2Actor.ini")),
            "SetNickLF created missing Mir2Actor.ini");

        var actorFile = Path.Combine(directory, "Mir2Actor.ini");
        File.WriteAllText(actorFile,
            "[Setup]\r\nLFMultiple=2\r\n[Preserved]\r\nValue=1\r\n",
            Encoding.GetEncoding(936));
        Assert(!NativeNickLinFuState.TryEnableAndPersist(directory, 0,
                ref state, out _),
            "SetNickLF accepted multiplier 0");
        Assert(!NativeNickLinFuState.TryEnableAndPersist(directory, 11,
                ref state, out _),
            "SetNickLF accepted multiplier 11");
        Assert(!state.Enabled, "invalid SetNickLF multiplier enabled runtime");

        Assert(NativeNickLinFuState.TryEnableAndPersist(directory, 7,
                ref state, out var error),
            "SetNickLF failed: " + error);
        Assert(state.Enabled, "SetNickLF did not enable runtime");
        Equal(7, state.Multiplier, "SetNickLF runtime multiplier");
        var persisted = File.ReadAllText(actorFile, Encoding.GetEncoding(936));
        Assert(persisted.Contains("LFMultiple=7", StringComparison.Ordinal),
            "SetNickLF did not persist [Setup] LFMultiple");
        Assert(persisted.Contains("[Preserved]", StringComparison.Ordinal) &&
               persisted.Contains("Value=1", StringComparison.Ordinal),
            "SetNickLF discarded unrelated INI data");

        M2Share.NickLinFuState = state;
        var mirror = new MirrorMessage();
        mirror.ProcessData(Grobal2.ISM_SETNICKLF, 0, "invalid");
        mirror.ProcessData(Grobal2.ISM_SETNICKLF, 0, "11");
        Equal(7, M2Share.NickLinFuState.Multiplier,
            "invalid mirror multiplier changed runtime");
        mirror.ProcessData(Grobal2.ISM_SETNICKLF, 0, "9");
        Assert(M2Share.NickLinFuState.Enabled,
            "valid mirror multiplier did not enable runtime");
        Equal(9, M2Share.NickLinFuState.Multiplier,
            "valid mirror multiplier");
        EqualString(persisted,
            File.ReadAllText(actorFile, Encoding.GetEncoding(936)),
            "mirror SetNickLF wrote Mir2Actor.ini");
    }
    finally
    {
        M2Share.NickLinFuState = NativeNickLinFuState.Disabled;
        Directory.Delete(directory, true);
    }
}

static void TestNativeRecordRoundTrip()
{
    // rec[0x1CC] <- obj+0x70C：0x6B14E8 mov eax,[ebx+0x70c] /
    // 0x6B14EE mov [esi+0x1cc],eax。此前写的 0x1D4 无原版依据（该偏移
    // 在存档记录里不属于 NickLinFu），会让断言必然读到 0。
    const int dataOffset = 0x01CC;
    const int physicalOffset = dataOffset + 8;
    const int unrelatedOffset = 0x01D0;
    const int originalValue = 123456789;
    const int updatedValue = 7654321;
    const int adjacentSentinel = unchecked((int)0x55667788);

    var blob = new byte[NativeHumanDataCodec.DataRecordSize + 8];
    BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4, 4),
        NativeHumanDataCodec.DataRecordSize);
    var raw = blob.AsSpan(8);
    raw[0x3E] = 1;
    BinaryPrimitives.WriteInt32LittleEndian(raw.Slice(dataOffset, 4), originalValue);
    BinaryPrimitives.WriteInt32LittleEndian(raw.Slice(unrelatedOffset, 4),
        adjacentSentinel);

    Equal(originalValue,
        BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(physicalOffset, 4)),
        "physical NickLinFu offset");
    Assert(NativeHumanDataCodec.TryDecode(blob, null, out var decoded, out var error),
        "native decode failed: " + error);
    Equal(originalValue, decoded.Data.nNickLinFu, "native NickLinFu decode");

    decoded.Data.nNickLinFu = updatedValue;
    Assert(NativeHumanDataCodec.TryEncode(decoded, out var encoded, out var script,
        out error), "native encode failed: " + error);
    Assert(NativeHumanDataCodec.TryDecode(encoded, script, out var roundTrip, out error),
        "native round-trip decode failed: " + error);
    Equal(updatedValue, roundTrip.Data.nNickLinFu, "native NickLinFu round trip");
    Equal(updatedValue,
        BinaryPrimitives.ReadInt32LittleEndian(roundTrip.NativeData.AsSpan(dataOffset, 4)),
        "native NickLinFu raw write");
    Equal(adjacentSentinel,
        BinaryPrimitives.ReadInt32LittleEndian(
            roundTrip.NativeData.AsSpan(unrelatedOffset, 4)),
        "native unrelated field preservation");
}

static void TestProtobufRoundTrip()
{
    var source = new THumDataInfo();
    source.Data.nNickLinFu = -20260717;
    source.PrepareForTransport();
    var payload = ProtoBufDecoder.Serialize(source);
    var decoded = ProtoBufDecoder.DeSerialize<THumDataInfo>(payload);
    Assert(decoded?.Data != null, "protobuf THumDataInfo decode failed");
    Equal(source.Data.nNickLinFu, decoded.Data.nNickLinFu,
        "protobuf NickLinFu round trip");
}

static void TestNativeBalance()
{
    PrepareRuntimeConfig();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.ProcessMsgCriticalSection = new object();

    var player = new TPlayObject { m_nNickLinFu = 111 };
    player.DecNativeNickLinFu(10);
    Equal(101, player.m_nNickLinFu, "type 2 cost");
    Equal(10, player.m_nDecNickLinFu, "type 2 deduction accumulator");
    player.DecNativeNickLinFu(1);
    Equal(100, player.m_nNickLinFu, "type 1 cost");
    player.DecNativeNickLinFu(100);
    Equal(0, player.m_nNickLinFu, "type 3 cost");
    Equal(111, player.m_nDecNickLinFu, "deduction accumulator");

    player.m_nNickLinFu = 7;
    player.DecNativeNickLinFu(100);
    Equal(0, player.m_nNickLinFu, "saturating deduction balance");
    Equal(118, player.m_nDecNickLinFu, "saturating deduction actual amount");
}

static void TestPasPropertyAndMethods(NativeNickLinFuState enabledState)
{
    var player = new TPlayObject { m_nNickLinFu = 321 };
    var bridge = new PasApiBridge { CurrentPlayer = player };
    Assert(bridge.GetPlayerProperty("NickLinFu", out var value),
        "NickLinFu getter rejected native property");
    Equal(321, value.AsInt(), "NickLinFu PAS getter");

    M2Share.NickLinFuState = enabledState;
    Assert(bridge.CallPlayerMethod("IncNickLinFu",
        new List<PasValue> { PasValue.FromInt(5) }),
        "IncNickLinFu enabled dispatch rejected");
    Equal(331, player.m_nNickLinFu, "IncNickLinFu multiplier");
    Equal(10, player.m_nIncNickLinFu, "IncNickLinFu increase accumulator");
    var increaseMessage = player.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE);
    EqualString("您获得了10张圣殿灵符", increaseMessage.Buff,
        "IncNickLinFu native success message");
    Equal(0xFF, increaseMessage.nParam1,
        "IncNickLinFu native foreground color");
    Equal(0xFC, increaseMessage.nParam2,
        "IncNickLinFu native background color");

    M2Share.NickLinFuState = NativeNickLinFuState.Disabled;
    var messageCount = player.m_MsgList.Count;
    Assert(bridge.CallPlayerMethod("IncNickLinFu",
        new List<PasValue> { PasValue.FromInt(5) }),
        "IncNickLinFu disabled dispatch rejected");
    Equal(331, player.m_nNickLinFu, "disabled IncNickLinFu balance");
    Equal(10, player.m_nIncNickLinFu, "disabled IncNickLinFu accumulator");
    Equal(messageCount, player.m_MsgList.Count,
        "disabled IncNickLinFu emitted a success message");
}

static void TestSourceContracts()
{
    var root = FindRepositoryRoot();
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    var nativeNick = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.NativeNick.cs"));
    var integration = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasIntegration.cs"));
    var codec = File.ReadAllText(Path.Combine(root, "DBSvr", "Core",
        "NativeHumanDataCodec.cs"));
    var loader = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
        "UsrEngn.cs"));
    var saver = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.cs"));
    var stateSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "NativeNickLinFuState.cs"));
    var setNickCommand = File.ReadAllText(Path.Combine(root, "GameSvr", "Command",
        "Commands", "SetNickLFCommand.cs"));
    var mirror = File.ReadAllText(Path.Combine(root, "GameSvr", "Snaps",
        "MirrorMessage.cs"));
    var globals = File.ReadAllText(Path.Combine(root, "SystemModule", "Grobal2.cs"));

    Equal(2, Count(bridge, "case \"usenick\":"), "UseNick dispatch count");
    Equal(2, Count(bridge, "return TryUseNativeNick(args, out result);"),
        "UseNick native dispatch count");
    Equal(1, Count(bridge, "CurrentPlayer.DecNativeNickLinFu(args[0].AsInt());"),
        "DecNickLinFu native dispatch");
    Equal(1, Count(bridge, "CurrentPlayer.IncNativeNickLinFu(args[0].AsInt(),"),
        "IncNickLinFu native dispatch");
    var playerNick = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeNick.cs"));
    Assert(playerNick.Contains("\"您获得了\" + increase + \"张圣殿灵符\"",
            StringComparison.Ordinal) &&
           playerNick.Contains("Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0xFC",
            StringComparison.Ordinal),
        "IncNickLinFu native success message contract");
    Equal(1, Count(bridge, "case \"nicklinfu\":"),
        "NickLinFu property case count");
    Equal(1, Count(bridge,
        "result = PasValue.FromInt(CurrentPlayer.m_nNickLinFu);"),
        "NickLinFu native property dispatch");
    Assert(stateSource.Contains("EnabledByteOffset = 1", StringComparison.Ordinal) &&
           stateSource.Contains("EnabledMask = 0x02", StringComparison.Ordinal),
        "native ServerSwitch NickLinFu bit");
    Assert(stateSource.Contains("\"Mir2Actor.ini\"", StringComparison.Ordinal) &&
           stateSource.Contains("\"LFMultiple\"", StringComparison.Ordinal),
        "native LFMultiple startup load");
    Assert(setNickCommand.Contains(
            "SendServerGroupMsg(Grobal2.ISM_SETNICKLF, 0,",
            StringComparison.Ordinal),
        "SetNickLF group broadcast surface");
    Assert(globals.Contains("ISM_SETNICKLF = 249", StringComparison.Ordinal),
        "SetNickLF mirror ident");
    Assert(mirror.Contains("case Grobal2.ISM_SETNICKLF:",
            StringComparison.Ordinal) &&
           mirror.Contains("TryApplyMirror(multiplier,",
            StringComparison.Ordinal),
        "SetNickLF mirror dispatch");
    Reject(mirror, "Mir2Actor.ini", "mirror SetNickLF disk access");

    foreach (var label in new[] { "@NotEnoughBag", "@NotEnoughNick", "@UseNick_OK" })
        Equal(1, Count(nativeNick, label), "native callback " + label);
    // 0x6B14E8 mov eax,[ebx+0x70c] / 0x6B14EE mov [esi+0x1cc],eax
    Assert(codec.Contains("NickLinFuOffset = 0x01CC", StringComparison.Ordinal),
        "native NickLinFu offset");
    Assert(loader.Contains("m_nNickLinFu = HumData.nNickLinFu", StringComparison.Ordinal),
        "login NickLinFu load");
    Assert(saver.Contains("HumData.nNickLinFu = m_nNickLinFu", StringComparison.Ordinal),
        "logout NickLinFu save");

    Reject(integration, "V[10, 3] = NickLinFu",
        "NickLinFu V-variable documentation substitute");
    foreach (var source in new[]
             {
                 bridge, nativeNick, codec, loader, saver, stateSource,
                 setNickCommand, mirror, globals
             })
    foreach (var forbidden in new[]
    {
        "Market_Saved", "Market_Prices", "UserData.dat", "TBL_GOLDSALES",
        "YBData.json", "YBShopScript.json", "tbl_"
    })
        Reject(source, forbidden, "non-native persistence " + forbidden);
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

static int Count(string source, string value)
{
    var count = 0;
    for (var offset = 0;;)
    {
        var index = source.IndexOf(value, offset, StringComparison.Ordinal);
        if (index < 0) return count;
        count++;
        offset = index + value.Length;
    }
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException(
        "Repository root containing GameSvr/GameSvr.csproj was not found.");
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
        Fail(message + " is present");
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual) Fail($"{message}: expected {expected}, actual {actual}");
}

static void EqualString(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        Fail($"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) Fail(message);
}

static void Fail(string message) => throw new InvalidOperationException(message);
