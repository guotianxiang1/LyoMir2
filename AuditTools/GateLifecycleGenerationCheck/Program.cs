using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using GameSvr;
using SystemModule;
using SystemModule.Packet;
using SystemModule.Sockets;

PrepareRuntimeFiles();

var failures = new List<string>();
Run("repeated OPEN and stale user generation", RepeatedOpenAndStaleUser);
Run("ConnectionId reuse keeps replacement gate", ConnectionIdReuse);
Run("out-connect send requires current generation", OutConnectGeneration);
Run("published and pre-bind loads are revoked", PublishedLoadIsRevoked);
Run("FrontEngine full threshold is 2000 saves", FrontEngineFullThreshold);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("GateLifecycleGenerationCheck PASS tests=5");
return 0;

void RepeatedOpenAndStaleUser()
{
    var socket = NewSocket();
    var gate = NewGate(socket);
    var service = new GateService(42, gate);
    try
    {
        var frame = OpenFrame(9001, "127.0.0.1");
        Parallel.For(0, 64,
            _ => service.HandleReceiveBuffer(frame.Length, frame));
        var users = gate.UserList.Where(user => user != null).ToArray();
        Equal(1, users.Length, "OPEN slot count");
        Equal(1, gate.nUserCount, "OPEN user count");
        var firstGeneration = users[0].UserGeneration;
        Assert(firstGeneration > 0, "first generation was not assigned");

        var firstPlayer = Player(42, 9001, firstGeneration);
        Assert(service.SetGateUserList(9001, firstPlayer),
            "first generation did not bind");
        Assert(service.CloseUser(9001, firstGeneration),
            "first generation did not close");

        service.HandleReceiveBuffer(frame.Length, frame);
        var replacement = gate.UserList.Single(user => user != null);
        Assert(replacement.UserGeneration > firstGeneration,
            "replacement generation was not monotonic");
        Equal(1, gate.nUserCount, "replacement user count");

        var stalePlayer = Player(42, 9001, firstGeneration);
        Assert(!service.SetGateUserList(9001, stalePlayer),
            "stale player rebound replacement slot");
        Assert(stalePlayer.m_boEmergencyClose && stalePlayer.m_boSoftClose,
            "rejected stale player was left usable");
        Assert(!service.CloseUser(9001, firstGeneration),
            "stale close removed replacement slot");
        Assert(service.IsCurrentUser(9001, replacement.UserGeneration),
            "replacement slot disappeared after stale close");
    }
    finally
    {
        service.Stop();
        socket.Dispose();
    }
}

void ConnectionIdReuse()
{
    var manager = NewManager(out var gates);
    var oldReady = M2Share.boStartReady;
    M2Share.boStartReady = true;
    var sockets = new List<Socket>();
    try
    {
        var oldPair = SocketPair(sockets);
        var duplicatePair = SocketPair(sockets);
        var oldToken = Token(oldPair.Server, 7001);
        var duplicateToken = Token(duplicatePair.Server, 7001);
        InvokeManager(manager, "AddGate", oldToken);
        var oldService = gates[7001];
        InvokeManager(manager, "AddGate", duplicateToken);
        Assert(ReferenceEquals(oldService, gates[7001]),
            "duplicate ConnectionId replaced live gate");
        Assert(duplicatePair.Server.SafeHandle.IsClosed,
            "duplicate socket was not closed");

        InvokeManager(manager, "CloseGate", oldToken);
        Assert(!gates.ContainsKey(7001), "old gate was not removed");

        var replacementPair = SocketPair(sockets);
        var replacementToken = Token(replacementPair.Server, 7001);
        InvokeManager(manager, "AddGate", replacementToken);
        var replacementService = gates[7001];
        InvokeManager(manager, "CloseGate", oldToken);
        Assert(gates.TryGetValue(7001, out var current) &&
               ReferenceEquals(current, replacementService),
            "late old disconnect removed replacement gate");
        InvokeManager(manager, "CloseGate", replacementToken);
    }
    finally
    {
        M2Share.boStartReady = oldReady;
        foreach (var socket in sockets) socket.Dispose();
    }
}

void OutConnectGeneration()
{
    const int gateIndex = 43;
    const int clientSocket = 4301;
    const ushort gateSocketIndex = 7;
    const long currentGeneration = 200;
    var socket = NewSocket();
    var gate = NewGate(socket);
    gate.UserList.Add(new TGateUserInfo
    {
        nSocket = clientSocket,
        nGSocketIdx = gateSocketIndex,
        UserGeneration = currentGeneration
    });
    gate.nUserCount = 1;
    var service = new GateService(gateIndex, gate);
    var manager = NewManager(out var gates);
    gates.TryAdd(gateIndex, service);
    try
    {
        var sendQueue = (SendQueue)typeof(GateService).GetField("_sendQueue",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        Assert(!manager.SendOutConnectMsg(gateIndex, clientSocket,
                gateSocketIndex, currentGeneration - 1),
            "stale generation sent an out-connect message");
        Assert(!manager.SendOutConnectMsg(gateIndex, clientSocket,
                gateSocketIndex + 1, currentGeneration),
            "wrong gate socket index sent an out-connect message");
        Equal(0, sendQueue.GetQueueCount, "rejected out-connect queue count");

        Assert(manager.SendOutConnectMsg(gateIndex, clientSocket,
                gateSocketIndex, currentGeneration),
            "current generation out-connect message was rejected");
        Equal(1, sendQueue.GetQueueCount, "accepted out-connect queue count");
    }
    finally
    {
        service.Stop();
        socket.Dispose();
    }
}

void PublishedLoadIsRevoked()
{
    const int gateIndex = 51;
    const int clientSocket = 5101;
    const long generation = 12345;
    var socket = NewSocket();
    var gate = NewGate(socket);
    gate.UserList.Add(new TGateUserInfo
    {
        nSocket = clientSocket,
        UserGeneration = generation
    });
    gate.nUserCount = 1;
    var service = new GateService(gateIndex, gate);
    var manager = NewManager(out var gates);
    gates.TryAdd(gateIndex, service);
    var oldManager = M2Share.GateManager;
    var oldFront = M2Share.FrontEngine;
    var oldUsers = M2Share.UserEngine;
    var front = new TFrontEngine();
    var users = new UserEngine();
    M2Share.GateManager = manager;
    M2Share.FrontEngine = front;
    M2Share.UserEngine = users;
    try
    {
        var load = new TLoadDBInfo
        {
            nGateIdx = gateIndex,
            nSocket = clientSocket,
            UserGeneration = generation,
            sAccount = "account",
            sCharName = "character"
        };
        var publish = typeof(TFrontEngine).GetMethod("TryPublishLoadedHuman",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert((bool)publish.Invoke(front,
                new object[] { load, new THumDataInfo(), null })!,
            "load did not publish before cancellation");
        Equal(1, users.LoadPlayCount, "published open count");
        var preBindPlayer = Player(gateIndex, clientSocket, generation);
        var newHumans = (IList<TPlayObject>)typeof(UserEngine).GetField(
            "m_NewHumanList", BindingFlags.Instance |
                              BindingFlags.NonPublic)!.GetValue(users)!;
        newHumans.Add(preBindPlayer);
        front.DeleteHuman(gateIndex, clientSocket, generation);
        Equal(0, users.LoadPlayCount, "cancelled published open count");
        Assert(preBindPlayer.m_boEmergencyClose &&
               preBindPlayer.m_boSoftClose,
            "created pre-bind player was left usable");

        var online = (IList<TPlayObject>)typeof(UserEngine).GetField(
            "m_PlayObjectList", BindingFlags.Instance |
                                BindingFlags.NonPublic)!.GetValue(users)!;
        var loadSync = typeof(UserEngine).GetField("m_LoadPlaySection",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(users)!;
        var takeForBinding = typeof(UserEngine).GetMethod(
            "TakeNewHumansForBinding",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var i = 0; i < 100; i++)
        {
            var raceGeneration = generation + i + 1;
            var racePlayer = Player(gateIndex, clientSocket,
                raceGeneration);
            lock (loadSync)
            {
                newHumans.Add(racePlayer);
                online.Add(racePlayer);
            }
            using var start = new ManualResetEventSlim(false);
            var takeTask = Task.Run(() =>
            {
                start.Wait();
                takeForBinding.Invoke(users, null);
            });
            var cancelTask = Task.Run(() =>
            {
                start.Wait();
                users.CancelUserOpen(gateIndex, clientSocket,
                    raceGeneration);
            });
            start.Set();
            Assert(Task.WaitAll(new[] { takeTask, cancelTask }, 5000),
                "cancel/bind snapshot race timed out");
            Assert(racePlayer.m_boEmergencyClose &&
                   racePlayer.m_boSoftClose,
                "cancel/bind snapshot race left player usable");
            lock (loadSync) online.Remove(racePlayer);
        }
    }
    finally
    {
        service.Stop();
        socket.Dispose();
        M2Share.GateManager = oldManager;
        M2Share.FrontEngine = oldFront;
        M2Share.UserEngine = oldUsers;
    }
}

void FrontEngineFullThreshold()
{
    var front = new TFrontEngine();
    var field = typeof(TFrontEngine).GetField("m_SaveRcdList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var saves = (IList<TSaveRcd>)field.GetValue(front)!;
    for (var i = 0; i < 1999; i++) saves.Add(null);
    Assert(!front.IsFull(), "FrontEngine reported full below 2000");
    saves.Add(null);
    Assert(front.IsFull(), "FrontEngine did not report full at 2000");
}

static TPlayObject Player(int gate, int socket, long generation)
{
    var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TPlayObject));
    player.m_nGateIdx = gate;
    player.m_nSocket = socket;
    player.m_UserGeneration = generation;
    player.m_nGSocketIdx = 0;
    player.m_nSessionID = 0;
    player.m_sLoginAccount = string.Empty;
    player.m_sCharName = string.Empty;
    return player;
}

static byte[] OpenFrame(uint connectionId, string address)
{
    var payload = Encoding.ASCII.GetBytes(address);
    return new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = connectionId,
        SeqID = 1,
        FrameLen = (ushort)(InternalPacket77.HEADER_SIZE + payload.Length),
        Cmd = Grobal2.GM_OPEN,
        Field16 = 1,
        Field20 = (uint)payload.Length,
        Payload = payload
    }.ToBytes();
}

static GateManager NewManager(
    out ConcurrentDictionary<int, GateService> gates)
{
    var manager = (GateManager)RuntimeHelpers.GetUninitializedObject(
        typeof(GateManager));
    gates = new ConcurrentDictionary<int, GateService>();
    typeof(GateManager).GetField("_gateDataService",
        BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, gates);
    return manager;
}

static void InvokeManager(GateManager manager, string method,
    AsyncUserToken token)
{
    try
    {
        typeof(GateManager).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(manager, new object[] { token });
    }
    catch (TargetInvocationException exception)
    {
        throw exception.InnerException ?? exception;
    }
}

static AsyncUserToken Token(Socket socket, int connectionId) => new(socket)
{
    ConnectionId = connectionId
};

static (Socket Server, Socket Client) SocketPair(ICollection<Socket> sockets)
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream,
        ProtocolType.Tcp);
    client.Connect(listener.LocalEndpoint);
    var server = listener.AcceptSocket();
    sockets.Add(server);
    sockets.Add(client);
    return (server, client);
}

static Socket NewSocket() => new(AddressFamily.InterNetwork,
    SocketType.Stream, ProtocolType.Tcp);

static TGateInfo NewGate(Socket socket) => new()
{
    Socket = socket,
    boUsed = true,
    UserList = new List<TGateUserInfo>()
};

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

static void Equal<T>(T expected, T actual, string label)
{
    Assert(EqualityComparer<T>.Default.Equals(expected, actual),
        $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
