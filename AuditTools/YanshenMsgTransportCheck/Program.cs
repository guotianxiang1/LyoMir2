using System.Reflection;
using System.Text;
using GameSvr;
using SystemModule;
using SystemModule.Packages;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
M2Share.ProcessMsgCriticalSection = new object();
M2Share.ProcessHumanCriticalSection = new object();
M2Share.ObjectManager = new ObjectManager();

var execGateMsg = typeof(GateService).GetMethod("ExecGateMsg",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException(typeof(GateService).FullName, "ExecGateMsg");
var getMessage = typeof(TBaseObject).GetMethod("GetMessage",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException(typeof(TBaseObject).FullName, "GetMessage");

var failures = new List<string>();
Run("terminal NUL is retained in Payload and removed from text only",
    TestTerminalNull);
Run("non-terminal NUL and final nonzero byte remain in the text view",
    TestNonTerminalNull);
Run("custom binary command preserves every payload byte",
    TestCustomBinaryPayload);
Run("equipment slots 13..15 survive the original 12-byte MSG",
    TestEquipmentSlotHeader);

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("Yanshen MSG transport regression checks passed.");
return 0;

void TestTerminalNull()
{
    var body = new byte[] { (byte)'y', (byte)'s', (byte)'2', (byte)'0', (byte)'7', 0 };
    var result = DispatchThroughGate(Grobal2.CM_SAY, body);

    Equal("ys207", result.Queued.Buff, "queued text view");
    Equal("ys207", result.Process.sMsg, "process text view");
    Bytes(body, result.Payload, "terminal-NUL payload");
    Same(result.Payload, result.Process.Payload, "SendMessage -> TProcessMessage payload");
}

void TestNonTerminalNull()
{
    var body = new byte[] { (byte)'A', 0, (byte)'B', 1 };
    var result = DispatchThroughGate(Grobal2.CM_SAY, body);
    var expectedText = HUtil32.GetString(body, 0, body.Length);

    Equal(expectedText, result.Queued.Buff, "queued text view");
    Equal(body.Length, result.Queued.Buff.Length, "text view length");
    Equal('\0', result.Queued.Buff[1], "embedded NUL");
    Equal('\u0001', result.Queued.Buff[^1], "final nonzero byte");
    Bytes(body, result.Payload, "non-terminal-NUL payload");
    Same(result.Payload, result.Process.Payload, "SendMessage -> TProcessMessage payload");
}

void TestCustomBinaryPayload()
{
    const ushort customIdent = 1271;
    var body = new byte[] { 0x10, 0x00, 0xFE, 0x7F, 0x01 };
    var result = DispatchThroughGate(customIdent, body);

    Equal(customIdent, result.Process.wIdent, "custom ident");
    Bytes(body, result.Payload, "custom binary payload");
    Same(result.Payload, result.Process.Payload, "SendMessage -> TProcessMessage payload");
}

void TestEquipmentSlotHeader()
{
    const int makeIndex = 0x10203040;
    foreach (var ident in new ushort[] { Grobal2.CM_TAKEONITEM, Grobal2.CM_TAKEOFFITEM })
    {
        for (ushort slot = Grobal2.U_MASK; slot <= Grobal2.U_HORSE; slot++)
        {
            var result = DispatchThroughGate(ident, new byte[] { 0 },
                makeIndex, slot, 0x55AA, 0x1234);
            Equal((int)ident, result.Process.wIdent, $"equipment ident {ident}");
            Equal(makeIndex, result.Process.nParam1, $"equipment MakeIndex slot {slot}");
            Equal((int)slot, result.Process.nParam2, $"equipment nParam2 slot {slot}");
            Equal(0x55AA, result.Process.nParam3, $"equipment nParam3 slot {slot}");
            Equal(0x1234, result.Process.wParam, $"equipment wParam slot {slot}");
        }
    }
}

TransportResult DispatchThroughGate(ushort ident, byte[] expectedBody,
    int recog = 0x10203040, ushort param = 2, ushort tag = 3, ushort series = 4)
{
    const int socket = 0x207;
    var engine = new UserEngine();
    var player = new TPlayObject();
    var gate = new TGateInfo
    {
        // GateService hands TGateInfo.Socket straight to SendQueue, which
        // rejects null (SendQueue.cs:18). A real gate always has one; the
        // fixture did not, so construction threw before any transport
        // assertion ran. An unconnected socket satisfies the guard and is
        // never written to: StartQueueService is not called here.
        Socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp),
        UserList = new List<TGateUserInfo>
        {
            new()
            {
                nSocket = socket,
                boCertification = true,
                PlayObject = player,
                UserEngine = engine
            }
        }
    };
    M2Share.UserEngine = engine;

    var clientPacket = new ClientPacket
    {
        Recog = recog,
        Ident = ident,
        Param = param,
        Tag = tag,
        Series = series
    }.GetBuffer();
    Equal(ClientPacket.PackSize, clientPacket.Length, "client header size");

    var declaredLength = clientPacket.Length + expectedBody.Length;
    var wireBuffer = new byte[declaredLength + 3];
    Buffer.BlockCopy(clientPacket, 0, wireBuffer, 0, clientPacket.Length);
    Buffer.BlockCopy(expectedBody, 0, wireBuffer, clientPacket.Length, expectedBody.Length);
    wireBuffer[^3] = 0xDE;
    wireBuffer[^2] = 0xAD;
    wireBuffer[^1] = 0xBE;

    var header = new PacketHeader
    {
        PacketCode = Grobal2.RUNGATECODE,
        Socket = socket,
        Ident = Grobal2.GM_DATA,
        UserIndex = 1,
        PackLength = declaredLength
    };
    using var service = new GateServiceScope(new GateService(0, gate));
    execGateMsg.Invoke(service.Value, new object[] { 0, gate, header, wireBuffer, declaredLength });

    Equal(1, player.m_MsgList.Count, "queued message count");
    var queued = player.m_MsgList[0];
    Assert(queued.Payload is byte[], "SendMessage.Payload is not byte[]");
    var queuedPayload = (byte[])queued.Payload;
    Bytes(expectedBody, queuedPayload, "12-byte-header body extraction");

    object[] getMessageArgs = { null };
    Assert((bool)getMessage.Invoke(player, getMessageArgs), "GetMessage returned false");
    var process = getMessageArgs[0] as TProcessMessage
        ?? throw new InvalidOperationException("GetMessage did not return TProcessMessage");
    Equal(0, player.m_MsgList.Count, "queue drained");
    Bytes(expectedBody, process.Payload as byte[], "TProcessMessage payload");

    return new TransportResult(queued, process, queuedPayload);
}

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures.Add("FAIL " + name + ": " + Unwrap(ex));
    }
}

static Exception Unwrap(Exception exception)
{
    while (exception is TargetInvocationException { InnerException: not null })
        exception = exception.InnerException;
    return exception;
}

static void Bytes(byte[] expected, byte[] actual, string name)
{
    Assert(actual != null, name + " is null");
    Assert(expected.AsSpan().SequenceEqual(actual),
        $"{name}: expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}");
}

static void Same(object expected, object actual, string name)
{
    Assert(ReferenceEquals(expected, actual), name + " reference changed");
}

static void Equal<T>(T expected, T actual, string name)
{
    Assert(EqualityComparer<T>.Default.Equals(expected, actual),
        $"{name}: expected {expected}, got {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

readonly record struct TransportResult(SendMessage Queued, TProcessMessage Process,
    byte[] Payload);

sealed class GateServiceScope : IDisposable
{
    public GateServiceScope(GateService value) => Value = value;

    public GateService Value { get; }

    public void Dispose() => Value.Stop();
}
