using System.Buffers.Binary;
using GameSvr;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;

PrepareRuntimeFiles();
PrepareRuntimeState();

var failures = new List<string>();
Run("0184 exact Type2 request wire", ExactRequestWire);
Run("0132 exact Type1 response decode", ExactResponseDecode);
Run("0132 explicit-length message delivery", ExplicitLengthDelivery);
Run("0132 native online-name lookup gates", OnlineLookup);
Run("0132 malformed and missing-player silence", SilentRejections);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeWhitelistReloadCompatCheck PASS tests=5 " +
                  "request=0184 response=0132 sysmsg=FFDB");
return 0;

void ExactRequestWire()
{
    var player = new TPlayObject { m_sCharName = "GM" };
    Assert(NativeWhitelistReloadClient.TryEncodeRequest(player,
        out var wire, out var error), error);
    Bytes(Convert.FromHexString(
            "77BBAA33020000000E000000840100000000000000000000474D"),
        wire, "complete 0184 wire");
    Equal(0xFFDB, NativeWhitelistReloadClient.NativeSysMsgIdent,
        "native SysMsg ident");

    Equal(false, NativeWhitelistReloadClient.TryEncodeRequest(null,
        out _, out _), "null player request");
    player.m_sCharName = new string('A', 16);
    Equal(false, NativeWhitelistReloadClient.TryEncodeRequest(player,
        out _, out _), "16-byte character name request");
}

void ExactResponseDecode()
{
    var wire = Convert.FromHexString(
        "77BBAA33010000005F0000003201000000000000000000000000000000000000000000000000000000000000000000000002474D000000000000000000000000000000000000000000000000000000000000000057686974654C6973742E747874BCD3D4D8B3C9B9A6A3A1");
    Assert(LegacyDbServerFrameCodec.TryDecode(wire, out var frame,
        out var error), error);
    Assert(NativeWhitelistReloadClient.TryDecodeResponse(frame,
        out var characterName, out var message), "golden 0132 rejected");
    Bytes(HUtil32.GbkEncoding.GetBytes("GM"), characterName,
        "golden character name");
    Bytes(HUtil32.GbkEncoding.GetBytes("WhiteList.txt加载成功！"), message,
        "golden response text");
}

void ExplicitLengthDelivery()
{
    var player = new TPlayObject { m_sCharName = "GM" };
    var messageBytes = HUtil32.GbkEncoding.GetBytes("A\0中文");
    var frame = Response(HUtil32.GbkEncoding.GetBytes("GM"), messageBytes);
    var finds = 0;
    var sends = 0;

    Assert(NativeWhitelistReloadClient.TryProcessResponse(frame,
        name =>
        {
            finds++;
            Bytes(HUtil32.GbkEncoding.GetBytes("GM"), name,
                "lookup character name");
            return player;
        },
        (target, message, foreground, background) =>
        {
            sends++;
            Same(player, target, "message target");
            Equal("A\0中文", message, "length-preserved message");
            Equal((byte)0xDB, foreground, "0xFFDB foreground");
            Equal((byte)0xFF, background, "0xFFDB background");
        }), "valid 0132 was not processed");
    Equal(1, finds, "lookup count");
    Equal(1, sends, "send count");

    var emptySends = 0;
    Assert(NativeWhitelistReloadClient.TryProcessResponse(
        Response(HUtil32.GbkEncoding.GetBytes("GM"), Array.Empty<byte>()),
        _ => player,
        (_, message, _, _) =>
        {
            emptySends++;
            Equal(string.Empty, message, "empty message");
        }), "zero-length 0132 was not processed");
    Equal(1, emptySends, "empty message send count");
}

void OnlineLookup()
{
    var emptyNamePlayer = new TPlayObject
    {
        m_sCharName = string.Empty,
        m_boReadyRun = true,
        m_boGhost = false
    };
    Equal(null, NativeWhitelistReloadClient.FindOnlinePlayer(
        new[] { emptyNamePlayer }, Array.Empty<byte>()),
        "empty native name gate");

    Equal(true, NativeWhitelistReloadClient.NativeNameEquals(
        new byte[] { (byte)'G', (byte)'m', 0x81, 0x41 },
        new byte[] { (byte)'g', (byte)'M', 0x81, 0x61 }),
        "ASCII-only byte folding");
    Equal(false, NativeWhitelistReloadClient.NativeNameEquals(
        HUtil32.GbkEncoding.GetBytes("Ａ"),
        HUtil32.GbkEncoding.GetBytes("ａ")),
        "non-ASCII bytes were folded");

    var ready = new TPlayObject
    {
        m_sCharName = "ReadyGM",
        m_boReadyRun = true,
        m_boGhost = false,
        m_boPasswordLocked = true,
        m_boObMode = true,
        m_boAdminMode = true
    };
    Same(ready, NativeWhitelistReloadClient.FindOnlinePlayer(
        new[] { ready }, HUtil32.GbkEncoding.GetBytes("readygm")),
        "ready current-name match");

    var notReady = new TPlayObject
    {
        m_sCharName = "GateGM",
        m_boReadyRun = false,
        m_boGhost = false
    };
    Equal(null, NativeWhitelistReloadClient.FindOnlinePlayer(
        new[] { notReady }, HUtil32.GbkEncoding.GetBytes("GateGM")),
        "ReadyRun gate");

    var ghost = new TPlayObject
    {
        m_sCharName = "GateGM",
        m_boReadyRun = true,
        m_boGhost = true
    };
    var laterDuplicate = new TPlayObject
    {
        m_sCharName = "GateGM",
        m_boReadyRun = true,
        m_boGhost = false
    };
    Equal(null, NativeWhitelistReloadClient.FindOnlinePlayer(
        new[] { ghost, laterDuplicate },
        HUtil32.GbkEncoding.GetBytes("GateGM")),
        "first indexed match ghost gate");
}

void SilentRejections()
{
    var valid = Response(HUtil32.GbkEncoding.GetBytes("GM"),
        HUtil32.GbkEncoding.GetBytes("ok"));
    var finds = 0;
    var sends = 0;
    TPlayObject Finder(byte[] _)
    {
        finds++;
        return new TPlayObject();
    }
    void Sender(TPlayObject _, string __, byte ___, byte ____)
        => sends++;

    Equal(false, NativeWhitelistReloadClient.TryProcessResponse(null,
        Finder, Sender), "null frame");
    Equal(false, NativeWhitelistReloadClient.TryProcessResponse(
        new LegacyDbServerFrame(2, 0, valid.Payload), Finder, Sender),
        "wrong outer type");

    var wrongCommand = Response(HUtil32.GbkEncoding.GetBytes("GM"),
        Array.Empty<byte>());
    BinaryPrimitives.WriteUInt16LittleEndian(wrongCommand.Payload,
        0x0131);
    Equal(false, NativeWhitelistReloadClient.TryProcessResponse(
        wrongCommand, Finder, Sender), "wrong command");
    Equal(false, NativeWhitelistReloadClient.TryProcessResponse(
        new LegacyDbServerFrame(1, 0, new byte[0x47]), Finder, Sender),
        "truncated Type1 header");

    var excessiveName = Response(Array.Empty<byte>(), Array.Empty<byte>());
    excessiveName.Payload[NativeWhitelistReloadClient.CharacterNameOffset]
        = 16;
    Equal(false, NativeWhitelistReloadClient.TryProcessResponse(
        excessiveName, Finder, Sender), "16-byte name slot");
    Equal(0, finds, "malformed lookup count");
    Equal(0, sends, "malformed send count");

    Equal(false, NativeWhitelistReloadClient.TryProcessResponse(valid,
        _ => null, Sender), "missing player result");
    Equal(0, sends, "missing player send count");
}

static LegacyDbServerFrame Response(byte[] characterName, byte[] message)
{
    if (characterName == null || characterName.Length > 15)
        throw new ArgumentOutOfRangeException(nameof(characterName));
    message ??= Array.Empty<byte>();
    var payload = new byte[0x48 + message.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeWhitelistReloadClient.ResponseCommand);
    payload[NativeWhitelistReloadClient.CharacterNameOffset]
        = (byte)characterName.Length;
    characterName.CopyTo(payload,
        NativeWhitelistReloadClient.CharacterNameOffset + 1);
    message.CopyTo(payload, 0x48);
    return new LegacyDbServerFrame(1, 0, payload);
}

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures.Add("FAIL " + name + ": " + exception.Message);
    }
}

static void PrepareRuntimeFiles()
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

static void PrepareRuntimeState()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
}

static void Bytes(byte[] expected, byte[] actual, string label)
{
    Assert(actual != null && expected.AsSpan().SequenceEqual(actual),
        label + ": expected=" + Convert.ToHexString(expected) +
        " actual=" + (actual == null ? "<null>" : Convert.ToHexString(actual)));
}

static void Same(object expected, object actual, string label)
{
    Assert(ReferenceEquals(expected, actual), label + " reference changed");
}

static void Equal<T>(T expected, T actual, string label)
{
    Assert(EqualityComparer<T>.Default.Equals(expected, actual),
        $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
