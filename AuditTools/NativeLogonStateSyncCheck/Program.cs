// Pins the SM 3554 login-state-cluster leg (战神 sub_6E99B8) and its RM trigger.
//
// WHY THIS EXISTS
// ---------------
// UserLogon (sub_6B1D64) does not send the login-state cluster inline. At
// 0x6B2358 it enqueues RM 0x3010 (`66 B9 10 30 mov cx,0x3010` -> sub_765E68 with
// edx=eax=Self and six zero params). On the next Run tick the Operate loop's
// secondary dispatcher sub_743AD8 (reached at 0x6B6247 `call 0x743AD8`) turns
// case 0x3010 (`0x743B24 sub eax,0x75F / je 0x743BF3`) into
// `0x743BF7 call [edx+0x204]` = the virtual cluster sub_6E9A98 (VMT base
// 0x62EF8C + 0x204). That cluster fans out four legs, in order:
//   0x6E9AA0 call 0x7468B4 -> SM 3324   (field [self+0x60C]/[+0x610]; UNMAPPED)
//   0x6E9AA7 call 0x6F0A50 -> SM 1264   (Param = config [0x7D7038]+3 bit7; UNMAPPED)
//   0x6E9AAE call 0x6E99B8 -> SM 3554   (timed-ability snapshot; RESOLVED, pinned here)
//   0x6E9ABB call 0x74839C -> SM 3556/4367 (pair list; empty on normal login)
// Only the byte-verified 3554 leg is replicated; the other three stay MISSING
// fail-closed. This tool freezes what IS known so a refactor cannot silently
// (a) renumber/rename the RM trigger, (b) change the 3554 frame header/record
// layout, (c) let the 3554 record drift away from the single-state 3555 record,
// or (d) grow a fabricated 3324/1264/3556 send with no evidence behind it.
//
// sub_6E99B8 body, bytes transcribed (flat_image.bin, VA = 0x400000 + offset):
//   0x6E9A14 8A 52 01        mov dl,[node+1]    -> byte  [+0] = InternalType
//   0x6E9A1D C6 44 70 01 00  mov [buf+1],0      -> byte  [+1] = 0
//   0x6E9A28 8B 52 02        mov edx,[node+2]   -> int32 [+2] = RemainingMilliseconds
//   0x6E9A35 8B 52 0A        mov edx,[node+0xA] -> int32 [+6] = Value
// Send via VMT+0x254 (0x6E9A4C..0x6E9A68): Recog=0 (33 C9), Param=count (push ebx),
// Tag=Series=0 (6A 00/6A 00), Len=count*10 (add eax,eax / lea eax,[eax+eax*4]).
// An empty list still sends (0x6E99EB je 0x6E9A4C with ebx=0) -> Param=0, Len=0.
using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckRmTriggerConstant();
CheckEmptySnapshotFrame();
CheckMultiNodeSnapshotFrame();
CheckRecordParityWith3555();
CheckWiringAndFailClosedLegs();

Console.WriteLine(
    "PASS NativeLogonStateSync rm=12304(0x3010) sm=3554 header=recog0/param=count/tag0/series0 " +
    "record=10B{type,0,remainMs:i32,value:i32}==3555 empty-list=param0/len0 " +
    "legs-emitted=3554-only 3324/1264/3556=MISSING(fail-closed)");
return;

// --- checks -------------------------------------------------------------------

static void CheckRmTriggerConstant()
{
    // The dispatcher case is native 0x3010; a rename that changes the value would
    // route UserLogon's enqueue to the wrong arm (see WireIdentPinCheck F-2 shape).
    var field = typeof(Grobal2).GetField("RM_NATIVE_LOGON_STATE_SYNC",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "Grobal2.RM_NATIVE_LOGON_STATE_SYNC missing");
    Equal(0x3010, (int)field.GetValue(null), "RM trigger == native 0x3010");
    Equal(12304, (int)field.GetValue(null), "RM trigger decimal");
}

static void CheckEmptySnapshotFrame()
{
    var actor = new TBaseObject();
    var (header, body) = InvokeSnapshot(actor);
    CheckHeader(header, 0, "empty");
    Equal(0, body.Length, "empty-list body length");
}

static void CheckMultiNodeSnapshotFrame()
{
    var actor = new TBaseObject();
    // Insert order A,B,C; native walks from head, and the C# builder walks the
    // same singly-linked m_TimedAbilityHead, so the wire order is head-first C,B,A.
    var inserts = new (byte Type, int Remaining, int Value)[]
    {
        (0x20, 1000, 5),
        (0x2D, 0x11223344, unchecked((int)0xFF00AA55)),
        (0x4B, -1, 7)
    };
    foreach (var (type, remaining, value) in inserts)
        InjectNode(actor, type, remaining, value);

    var expected = inserts.Reverse().ToArray();
    var (header, body) = InvokeSnapshot(actor);
    CheckHeader(header, expected.Length, "multi");
    Equal(expected.Length * 10, body.Length, "multi body length");

    for (var i = 0; i < expected.Length; i++)
    {
        var at = i * 10;
        Equal(expected[i].Type, body[at], $"record {i} InternalType");
        Equal((byte)0, body[at + 1], $"record {i} pad byte");
        Equal(expected[i].Remaining,
            BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(at + 2, 4)),
            $"record {i} RemainingMilliseconds");
        Equal(expected[i].Value,
            BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(at + 6, 4)),
            $"record {i} Value");
    }
}

static void CheckRecordParityWith3555()
{
    // Native claims the 3554 record is byte-identical to the single-state 3555
    // record. Compare the one-node snapshot body against BuildTimedAbilityClientState.
    const byte type = 0x2D;
    const int remaining = 0x0BADF00D;
    const int value = 0x12345678;

    var actor = new TBaseObject();
    InjectNode(actor, type, remaining, value);
    var (_, snapshotBody) = InvokeSnapshot(actor);
    Equal(10, snapshotBody.Length, "single-node snapshot length");

    var method = typeof(TBaseObject).GetMethod("BuildTimedAbilityClientState",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildTimedAbilityClientState missing");
    var tuple = method.Invoke(null, new object[] { type, remaining, value, false })
        ?? throw new InvalidOperationException("3555 builder returned null");
    var single = (byte[])tuple.GetType().GetField("Item2").GetValue(tuple);
    Equal(10, single.Length, "3555 record length");
    Assert(snapshotBody.SequenceEqual(single), "3554 record != 3555 record");
}

static void CheckWiringAndFailClosedLegs()
{
    var root = FindRepoRoot();

    var grobal = File.ReadAllText(Path.Combine(root, "SystemModule", "Grobal2.cs"));
    Contains(grobal, "public const int RM_NATIVE_LOGON_STATE_SYNC = 12304;",
        "RM constant declaration");

    // UserLogon enqueues the RM *before* the SM 888 send (native 0x6B2358 precedes
    // 0x6B23C6), matching "cluster arrives on the next Run tick, after every direct SM".
    var baseSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Base.cs"));
    Before(baseSource, "SendMsg(this, Grobal2.RM_NATIVE_LOGON_STATE_SYNC, 0, 0, 0, 0, \"\");",
        "SendDefMessage(Grobal2.SM_LOGIN_VER", "UserLogon enqueue before SM 888");

    var message = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Message.cs"));
    var arm = Between(message, "case Grobal2.RM_NATIVE_LOGON_STATE_SYNC:",
        "break;");
    Contains(arm, "SendNativeLogonStateSync();", "Operate dispatch arm");

    // The sender emits ONLY the resolved 3554 leg. If a later change fabricates a
    // 3324/1264/3556 send without the missing field/config evidence, the extra
    // send primitive trips this guard (fail-closed, REPLICATION_RULES 4.20).
    var sync = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeLogonStateSync.cs"));
    var bodyStart = sync.IndexOf("private void SendNativeLogonStateSync()",
        StringComparison.Ordinal);
    Assert(bodyStart >= 0, "SendNativeLogonStateSync method present");
    var methodBody = sync[bodyStart..];
    Contains(methodBody, "BuildNativeTimedAbilitySnapshot()", "3554 snapshot builder call");
    Equal(1, Count(methodBody, "SendSocket("), "exactly one send primitive");
    Equal(0, Count(methodBody, "SendDefMessage("), "no fabricated direct send");
    Equal(0, Count(methodBody, "SendRefMsg("), "no fabricated broadcast send");
}

// --- helpers ------------------------------------------------------------------

static (ClientPacket Header, byte[] Body) InvokeSnapshot(TBaseObject actor)
{
    var method = typeof(TBaseObject).GetMethod("BuildNativeTimedAbilitySnapshot",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("BuildNativeTimedAbilitySnapshot missing");
    var tuple = method.Invoke(actor, null)
        ?? throw new InvalidOperationException("snapshot returned null");
    var type = tuple.GetType();
    var header = (ClientPacket)type.GetField("Item1").GetValue(tuple);
    var body = (byte[])type.GetField("Item2").GetValue(tuple);
    return (header, body);
}

static void CheckHeader(ClientPacket header, int count, string label)
{
    Assert(header != null, label + " header present");
    Equal((ushort)3554, header.Ident, label + " ident");
    Equal(0, header.Recog, label + " recog");
    Equal((ushort)count, header.Param, label + " param==count");
    Equal((ushort)0, header.Tag, label + " tag");
    Equal((ushort)0, header.Series, label + " series");
}

static void InjectNode(TBaseObject actor, byte internalType, int remaining, int value)
{
    var nodeType = typeof(TBaseObject).GetNestedType("TimedAbilityNode",
        BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TimedAbilityNode missing");
    var node = Activator.CreateInstance(nodeType, nonPublic: true)
        ?? throw new InvalidOperationException("node allocation failed");
    NodeField(nodeType, "Flag").SetValue(node, (byte)0);
    NodeField(nodeType, "InternalType").SetValue(node, internalType);
    NodeField(nodeType, "RemainingMilliseconds").SetValue(node, remaining);
    NodeField(nodeType, "LastTick").SetValue(node, 0);
    NodeField(nodeType, "Value").SetValue(node, value);
    NodeField(nodeType, "Next").SetValue(node, GetHead(actor));
    FindField(typeof(TBaseObject), "m_TimedAbilityHead").SetValue(actor, node);
}

static object GetHead(TBaseObject actor) =>
    FindField(typeof(TBaseObject), "m_TimedAbilityHead").GetValue(actor);

static FieldInfo NodeField(Type type, string name) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic)
    ?? throw new MissingFieldException(type.FullName, name);

static FieldInfo FindField(Type type, string name)
{
    for (var current = type; current != null; current = current.BaseType)
    {
        var field = current.GetField(name, BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field != null)
            return field;
    }
    throw new MissingFieldException(type.FullName, name);
}

static int Count(string source, string value)
{
    var count = 0;
    for (var index = source.IndexOf(value, StringComparison.Ordinal); index >= 0;
         index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        count++;
    return count;
}

static string Between(string source, string startText, string endText)
{
    var start = source.IndexOf(startText, StringComparison.Ordinal);
    Assert(start >= 0, startText + " start anchor");
    var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
    Assert(end > start, endText + " end anchor");
    return source[start..end];
}

static void Before(string source, string first, string second, string label)
{
    var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
    var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
    Assert(firstIndex >= 0 && secondIndex > firstIndex, label);
}

static void Contains(string source, string value, string label) =>
    Assert(source.Contains(value, StringComparison.Ordinal), label);

static string FindRepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start);
             directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")))
                return directory.FullName;
        }
    }
    throw new InvalidOperationException("Repository root not found");
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig { nSendRefMsgRange = 12 };
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.CastleManager = new CastleManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
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

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
}
