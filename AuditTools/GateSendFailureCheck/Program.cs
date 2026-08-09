using System.Net.Sockets;
using System.Reflection;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

PrepareRuntimeFiles();

var failures = new List<string>();
var oldTimeout = M2Share.g_dwSocCheckTimeOut;
var oldCheckBlock = M2Share.g_Config.nCheckBlock;
try
{
    M2Share.g_dwSocCheckTimeOut = int.MaxValue;
    Run("ACK resume closes on data queue overflow", () =>
        AckResumeClosesGate(SendQueue.DefaultCapacity, int.MaxValue));
    Run("ACK resume closes on control queue overflow", () =>
        AckResumeClosesGate(SendQueue.DefaultCapacity - 1, 1));
    Run("timed resume closes on delayed invalid frame",
        TimedResumeClosesOnDelayedInvalidFrame);
}
finally
{
    M2Share.g_dwSocCheckTimeOut = oldTimeout;
    M2Share.g_Config.nCheckBlock = oldCheckBlock;
}

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("GateSendFailureCheck PASS tests=3");
return 0;

void AckResumeClosesGate(int queuedPackets, int checkBlock)
{
    using var socket = new Socket(AddressFamily.InterNetwork,
        SocketType.Stream, ProtocolType.Tcp);
    var gate = new TGateInfo
    {
        Socket = socket,
        boUsed = true,
        UserList = new List<TGateUserInfo>(),
        nSendChecked = 1,
        dwSendCheckTick = HUtil32.GetTickCount()
    };
    var service = new GateService(1, gate);
    try
    {
        M2Share.g_Config.nCheckBlock = checkBlock;
        Assert(service.HandleSendBuffer(BuildPendingPacket()),
            "pending packet was rejected before ACK");

        var sendQueue = (SendQueue)typeof(GateService).GetField("_sendQueue",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        for (var i = 0; i < queuedPackets; i++)
            sendQueue.AddToQueue(new byte[] { (byte)i });

        var ack = new InternalPacket77
        {
            Magic = InternalPacket77.MAGIC,
            ConnID = 0,
            SeqID = 1,
            FrameLen = InternalPacket77.HEADER_SIZE,
            Cmd = Grobal2.GM_RECEIVE_OK,
            Payload = Array.Empty<byte>()
        }.ToBytes();
        service.HandleReceiveBuffer(ack.Length, ack);

        Assert(!gate.boUsed, "queue failure left gate active");
        Assert(SpinWait.SpinUntil(() => socket.SafeHandle.IsClosed,
                TimeSpan.FromSeconds(2)),
            "queue failure did not close the gate socket");
    }
    finally
    {
        service.Stop();
    }
}

void TimedResumeClosesOnDelayedInvalidFrame()
{
    using var socket = new Socket(AddressFamily.InterNetwork,
        SocketType.Stream, ProtocolType.Tcp);
    var gate = new TGateInfo
    {
        Socket = socket,
        boUsed = true,
        UserList = new List<TGateUserInfo>(),
        nSendChecked = 1,
        dwSendCheckTick = HUtil32.GetTickCount()
    };
    var service = new GateService(2, gate);
    try
    {
        var invalid = new byte[24 + InternalPacket77.MAX_PAYLOAD_SIZE + 1];
        Assert(service.HandleSendBuffer(invalid),
            "delayed invalid frame was rejected before resume");

        M2Share.g_dwSocCheckTimeOut = 0;
        gate.dwSendCheckTick = unchecked(HUtil32.GetTickCount() - 1);
        service.ResumeFlowControlIfTimedOut();

        Assert(!gate.boUsed, "invalid delayed frame left gate active");
        Assert(SpinWait.SpinUntil(() => socket.SafeHandle.IsClosed,
                TimeSpan.FromSeconds(2)),
            "invalid delayed frame did not close the gate socket");
    }
    finally
    {
        M2Share.g_dwSocCheckTimeOut = int.MaxValue;
        service.Stop();
    }
}

static byte[] BuildPendingPacket()
{
    var packet = new byte[24];
    BitConverter.GetBytes(1234).CopyTo(packet, 8);
    BitConverter.GetBytes(Grobal2.GM_DATA).CopyTo(packet, 14);
    return packet;
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
        failures.Add("FAIL " + name + ": " + exception);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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
