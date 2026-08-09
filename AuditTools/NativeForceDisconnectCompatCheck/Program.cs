using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;

PrepareRuntimeFiles();

var failures = new List<string>();
Run("0052 exact Type1 header and account ShortString", HeaderAndAccount);
Run("0052 malformed frames stay silent", MalformedFrames);
Run("0052 native ASCII-only byte folding", NativeByteFolding);
Run("0052 account lookup uses m_sUserID without state gates", AccountLookup);
Run("0052 kick-message-soft-close exact order", StateAndMessageOrder);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeForceDisconnectCompatCheck PASS tests=5 " +
                  "type1=0052 account=GBK/20 order=kick/message/soft-close");
return 0;

void HeaderAndAccount()
{
    using var runtime = NewRuntime();
    var account = HUtil32.GbkEncoding.GetBytes("Account-甲乙");
    var frame = Response(account, reserved: 0xBEEF, tailLength: 13);
    frame.Payload[0x02] = 0xA5;
    frame.Payload[0x47] = 0x5A;
    frame.Payload[^1] = 0xCC;

    Assert(NativeForceDisconnectClient.TryDecodeAccount(frame,
        out var decoded), "valid 0052 was rejected");
    Bytes(account, decoded, "account ShortString");
    Equal(0x48 + 13, frame.Payload.Length, "tail-preserving payload length");

    var maximum = Enumerable.Range(0, 20)
        .Select(value => (byte)('a' + value % 26)).ToArray();
    Assert(NativeForceDisconnectClient.TryDecodeAccount(Response(maximum),
        out decoded), "20-byte account was rejected");
    Bytes(maximum, decoded, "20-byte account");

    Assert(NativeForceDisconnectClient.TryDecodeAccount(
        Response(Array.Empty<byte>()), out decoded),
        "empty native ShortString was rejected");
    Equal(0, decoded.Length, "empty account length");
}

void MalformedFrames()
{
    using var runtime = NewRuntime();
    var account = HUtil32.GbkEncoding.GetBytes("MalformedUser");
    var player = Player("MalformedUser", "MalformedUser");
    AddOnline(player);
    var sent = 0;
    void Sender(TPlayObject _, short __, int ___, string ____) => sent++;

    Equal(false, NativeForceDisconnectClient.TryProcessResponse(null, Sender),
        "null frame");
    Equal(false, NativeForceDisconnectClient.TryProcessResponse(
        new LegacyDbServerFrame(2, 0, Response(account).Payload), Sender),
        "wrong outer type");

    var wrongCommand = Response(account);
    BinaryPrimitives.WriteUInt16LittleEndian(
        wrongCommand.Payload.AsSpan(0, 2), 0x0051);
    Equal(false, NativeForceDisconnectClient.TryProcessResponse(
        wrongCommand, Sender), "wrong command");

    var truncated = Response(account);
    Equal(false, NativeForceDisconnectClient.TryProcessResponse(
        new LegacyDbServerFrame(1, 0,
            truncated.Payload.AsSpan(0, 0x47).ToArray()), Sender),
        "truncated header");

    var excessiveLength = Response(account);
    excessiveLength.Payload[NativeForceDisconnectClient.AccountOffset] = 21;
    Equal(false, NativeForceDisconnectClient.TryProcessResponse(
        excessiveLength, Sender), "account length 21");
    Equal(0, sent, "malformed send count");
    Equal(false, player.m_boKickFlag, "malformed kick flag");
    Equal(false, player.m_boEmergencyClose, "malformed emergency flag");
    Equal(false, player.m_boSoftClose, "malformed soft-close flag");
}

void NativeByteFolding()
{
    Equal(true, NativeForceDisconnectClient.NativeAccountEquals(
        new byte[] { (byte)'A', (byte)'z', 0x81, 0x41 },
        new byte[] { (byte)'a', (byte)'Z', 0x81, 0x61 }),
        "ASCII bytes including a DBCS trail are folded");
    Equal(false, NativeForceDisconnectClient.NativeAccountEquals(
        new byte[] { 0xC0 }, new byte[] { 0xE0 }),
        "non-ASCII bytes were folded");
    Equal(false, NativeForceDisconnectClient.NativeAccountEquals(
        new byte[] { (byte)'a' }, new byte[] { (byte)'a', 0 }),
        "different byte lengths matched");
}

void AccountLookup()
{
    using var runtime = NewRuntime();
    var wrongIdentity = Player("RightAccount", "OtherAccount");
    wrongIdentity.m_sLoginAccount = "RightAccount";
    wrongIdentity.m_sCharName = "RightAccount";
    AddOnline(wrongIdentity);

    var target = Player("RIGHTaccount", "RIGHTaccount");
    target.m_sLoginAccount = "MustNotBeUsed";
    target.m_sCharName = "MustNotBeUsedEither";
    target.m_boGhost = true;
    target.m_boReadyRun = false;
    AddOnline(target);

    TPlayObject captured = null;
    var processed = NativeForceDisconnectClient.TryProcessResponse(
        Response(HUtil32.GbkEncoding.GetBytes("rightACCOUNT")),
        (player, _, _, _) => captured = player);
    Equal(true, processed, "mixed-case m_sUserID lookup");
    Same(target, captured, "selected online player");
    Equal(false, wrongIdentity.m_boKickFlag,
        "login-account/character-name false match");

    var sends = 0;
    Equal(false, NativeForceDisconnectClient.TryProcessResponse(
        Response(HUtil32.GbkEncoding.GetBytes("MissingAccount")),
        (_, _, _, _) => sends++), "missing account result");
    Equal(0, sends, "missing account send count");
}

void StateAndMessageOrder()
{
    using var runtime = NewRuntime();
    var target = Player("OrderUser", "OrderUser");
    AddOnline(target);
    M2Share.g_FunctionNPC = new Merchant();
    var sends = 0;

    var processed = NativeForceDisconnectClient.TryProcessResponse(
        Response(HUtil32.GbkEncoding.GetBytes("OrderUser")),
        (player, ident, recog, message) =>
        {
            sends++;
            Same(target, player, "message target");
            Equal(true, player.m_boKickFlag,
                "kick flag before merchant message");
            Equal(false, player.m_boEmergencyClose,
                "emergency flag before merchant message");
            Equal(false, player.m_boSoftClose,
                "soft-close flag at merchant message");
            Equal((short)Grobal2.SM_MERCHANTSAY, ident,
                "merchant message ident");
            Equal(M2Share.g_FunctionNPC.ObjectId, recog,
                "function-NPC message recog");
            Equal("NPC/与服务器断开连接...", message,
                "merchant message text");
        });

    Equal(true, processed, "processed result");
    Equal(1, sends, "merchant message count");
    Equal(true, target.m_boKickFlag, "final kick flag");
    Equal(false, target.m_boEmergencyClose, "final emergency flag");
    Equal(true, target.m_boSoftClose, "final soft-close flag");
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

static LegacyDbServerFrame Response(byte[] account, ushort reserved = 0,
    int tailLength = 0)
{
    if (account == null || account.Length > 20)
        throw new ArgumentOutOfRangeException(nameof(account));
    var payload = new byte[NativeForceDisconnectClient.HeaderSize + tailLength];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2),
        NativeForceDisconnectClient.ResponseCommand);
    payload[NativeForceDisconnectClient.AccountOffset] = (byte)account.Length;
    account.CopyTo(payload,
        NativeForceDisconnectClient.AccountOffset + 1);
    return new LegacyDbServerFrame(1, reserved, payload);
}

static TPlayObject Player(string account, string userId) => new()
{
    m_sUserID = userId,
    m_sLoginAccount = "Login-" + account,
    m_sCharName = "Character-" + account,
    m_boOffLineFlag = true
};

static RuntimeScope NewRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.UserEngine = new UserEngine();
    return new RuntimeScope();
}

static void AddOnline(TPlayObject player)
{
    var field = typeof(UserEngine).GetField("m_PlayObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(UserEngine).FullName,
            "m_PlayObjectList");
    if (field.GetValue(M2Share.UserEngine) is not IList<TPlayObject> players)
        throw new InvalidOperationException("unexpected online-player list");
    players.Add(player);
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

sealed class RuntimeScope : IDisposable
{
    public void Dispose()
    {
        M2Share.g_FunctionNPC = null;
        M2Share.UserEngine = null;
    }
}
