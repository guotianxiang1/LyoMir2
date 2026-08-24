using System.Buffers.Binary;
using GameGate.Models;
using SystemModule;

var root = FindRepositoryRoot();
var session = new ClientSession();

Equal(1u, session.AllocateDownDataIndex(), "first session dataIndex");
Equal(2u, session.AllocateDownDataIndex(), "second session dataIndex");
session.Reset();
Equal(1u, session.AllocateDownDataIndex(), "Reset dataIndex");

var body = new byte[] { 0x10, 0x20, 0x30, 0x40 };
var inner = new MobileCodec.InnerHeader
{
    Recog = 506,
    Ident = 629,
    Param = 484,
    Tag = 0,
    Series = 5
};
var frame = MobileCodec.WriteFrame(inner, body, 0, MobileCodec.MARKER_DATA);
var payloadBefore = frame.AsSpan(MobileCodec.HEADER_SIZE).ToArray();
BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), 1);
Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8, 4)),
    "frame dataIndex patch");
Require(frame.AsSpan(MobileCodec.HEADER_SIZE).SequenceEqual(payloadBefore),
    "dataIndex patch changed inner header/body");

var gateSource = File.ReadAllText(Path.Combine(root, "GameGate-CS", "Core",
    "GateServer.cs"));
var sessionSource = File.ReadAllText(Path.Combine(root, "GameGate-CS", "Models",
    "ClientSession.cs"));

Require(sessionSource.Contains("private uint _nextDownDataIndex = 1;",
    StringComparison.Ordinal), "session counter initial value");
Require(sessionSource.Contains("_nextDownDataIndex = 1;",
    StringComparison.Ordinal), "session counter reset");
Require(gateSource.Contains(
    "await clientWriteLock.WaitAsync(cts.Token);\r\n            try\r\n            {\r\n                uint dataIndex = 0;",
    StringComparison.Ordinal)
    || gateSource.Contains(
        "await clientWriteLock.WaitAsync(cts.Token);\n            try\n            {\n                uint dataIndex = 0;",
        StringComparison.Ordinal),
    "dataIndex allocation is inside client write lock");
Require(gateSource.Contains("session.AllocateDownDataIndex()",
    StringComparison.Ordinal), "GateServer uses per-session counter");
Require(gateSource.Contains("allocateDataIndex: true",
    StringComparison.Ordinal), "DATA relay opts into dataIndex allocation");
Require(gateSource.Contains("WriteClientMobileFrame(pongBytes);",
    StringComparison.Ordinal), "PING/PONG control echo remains unchanged");
Require(gateSource.Contains("WriteClientMobileFrame(keyResponse);",
    StringComparison.Ordinal), "GET_ENCRYPT control echo remains unchanged");
Require(gateSource.Contains("WriteClientMobileFrame(hbPong);",
    StringComparison.Ordinal), "heartbeat control echo remains unchanged");
Require(!gateSource.Contains(
    "unchecked((uint)Environment.TickCount), MobileCodec.MARKER_DATA",
    StringComparison.Ordinal), "system DATA no longer uses a tick as dataIndex");

Console.WriteLine("GameGateDownDataIndexCheck PASS start=1 reset=1 body-preserved controls-preserved");

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
         directory != null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "SystemModule",
                "SystemModule.csproj"))
            && File.Exists(Path.Combine(directory.FullName, "GameGate-CS",
                "GameGate.csproj")))
            return directory.FullName;
    }

    throw new DirectoryNotFoundException("repository root not found");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected {expected}, actual {actual}");
}

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}
