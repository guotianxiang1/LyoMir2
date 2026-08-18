using System.Collections;
using System.Reflection;
using System.Text;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();

Equal(1290, Grobal2.CM_YANHUA_TEXT, "CM_YANHUA_TEXT ident");
Equal(23, Grobal2.ET_YANHUA_TEXT, "ET_YANHUA_TEXT type");
CheckGlyphPlanning();
CheckIngressMapping();
CheckMissingItemIsSilent();
CheckEmptyBodyIsSilent();
CheckForgedMakeIndexIsSilent();
CheckOutOfBoundsIsAtomic();
CheckBlockedCellFireworksArePlaced();
CheckBroadcastPacketAbi();
CheckSuccessPath();
CheckInvalidGbkWirePreservation();

Console.WriteLine(
    "NativeFireworkTextCompatCheck PASS ident=1290 raw=min(12,len-1)/GBK coords=x+2*i atomic=all-bounds event=23/88000 delete=SM_DELITEM broadcast=0x38FF");

static void CheckGlyphPlanning()
{
    var ascii = Plan(Body("ABC"), 10, 20, out var asciiLog);
    Equal("ABC", asciiLog, "ASCII log text");
    Glyph(ascii, 0, "A", 10, 20, "ASCII glyph 0");
    Glyph(ascii, 1, "B", 12, 20, "ASCII glyph 1");
    Glyph(ascii, 2, "C", 14, 20, "ASCII glyph 2");

    var mixed = Plan(Body("传A情"), 7, 9, out var mixedLog);
    Equal("传A情", mixedLog, "mixed log text");
    Glyph(mixed, 0, "传", 7, 9, "mixed glyph 0");
    Glyph(mixed, 1, "A", 9, 9, "mixed glyph 1");
    Glyph(mixed, 2, "情", 11, 9, "mixed glyph 2");

    var capped = Plan(Body("ABCDEFGHIJKLMNO"), 0, 0, out var cappedLog);
    Equal(12, capped.Count, "12-byte cap glyph count");
    Equal("ABCDEFGHIJKL", cappedLog, "12-byte cap log text");

    var gbk = Encoding.GetEncoding(936);
    var finalGlyph = gbk.GetBytes("传");
    var boundaryBody = new byte[14];
    for (var index = 0; index < 11; index++) boundaryBody[index] = (byte)'A';
    boundaryBody[11] = finalGlyph[0];
    boundaryBody[12] = finalGlyph[1];
    boundaryBody[13] = 0;
    var boundary = Plan(boundaryBody, 2, 3, out _);
    Equal(12, boundary.Count, "boundary GBK glyph count");
    Glyph(boundary, 11, "传", 24, 3,
        "GBK glyph beginning at byte 12 consumes its second byte");

    var nonNullTerminator = Plan(new byte[] { (byte)'A', (byte)'B',
        (byte)'C', (byte)'X' }, 0, 0, out var nonNullLog);
    Equal(3, nonNullTerminator.Count, "last byte is always excluded");
    Equal("ABC", nonNullLog, "unconditional final-byte exclusion");

    var malformed = Plan(new byte[] { 0x81, 0x30, 0 }, 0, 0, out _);
    Equal(1, malformed.Count, "malformed GBK glyph count");
    BytesEqual(new byte[] { 0x81, 0x30 }, malformed[0].RawBytes,
        "malformed GBK raw pair");
}

static void CheckMissingItemIsSilent()
{
    ResetRuntime();
    var player = NewPlayer(30, 30);
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_YANHUA_TEXT,
        nParam1 = 999,
        Payload = EDcode.EncodeBuffer(Body("A"))
    }), "missing-item dispatcher result");
    Equal(0, player.m_ItemList.Count, "missing-item bag");
    Equal(0, player.m_MsgList.Count, "missing-item messages");
    Equal(0, M2Share.LogStringList.Count, "missing-item log");
}

static void CheckEmptyBodyIsSilent()
{
    ResetRuntime();
    var player = NewPlayer(30, 30);
    var item = AddFirework(player, 321, 654);

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_YANHUA_TEXT,
        nParam1 = item.ClientItemID,
        Payload = new byte[] { 0 }
    }), "empty-body dispatcher result");

    Equal(1, player.m_ItemList.Count, "empty-body item retained");
    Equal(0, player.m_MsgList.Count, "empty-body messages");
    Equal(0, M2Share.LogStringList.Count, "empty-body log");
    Assert(M2Share.EventManager.GetEvent(player.m_PEnvir, 10, 10,
        Grobal2.ET_YANHUA_TEXT) == null, "empty-body created an event");
}

static void CheckIngressMapping()
{
    ResetRuntime();
    var player = NewPlayer(30, 30);
    player.m_boOffLineFlag = false;
    typeof(TPlayObject).GetField("m_boNativeClientVersionHandshakeDone",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(player, true);
    var payload = Body("传A");
    M2Share.UserEngine.ProcessUserMessage(player, new ClientPacket
    {
        Ident = Grobal2.CM_YANHUA_TEXT,
        Recog = 7654
    }, string.Empty, payload);

    TProcessMessage queued = null;
    Assert(player.TryTake(ref queued), "1290 ingress was not queued");
    Equal(7654, queued.nParam1, "1290 Recog mapping");
    Assert(ReferenceEquals(payload, queued.Payload),
        "1290 raw payload was replaced");
}

static void CheckForgedMakeIndexIsSilent()
{
    ResetRuntime();
    var player = NewPlayer(30, 30);
    var item = AddFirework(player, 321, 654);

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_YANHUA_TEXT,
        nParam1 = item.MakeIndex,
        Payload = EDcode.EncodeBuffer(Body("A"))
    }), "forged-MakeIndex dispatcher result");

    Equal(1, player.m_ItemList.Count, "forged-MakeIndex item retained");
    Equal(0, player.m_MsgList.Count, "forged-MakeIndex messages");
    Equal(0, M2Share.LogStringList.Count, "forged-MakeIndex log");
    Assert(M2Share.EventManager.GetEvent(player.m_PEnvir, 10, 10,
        Grobal2.ET_YANHUA_TEXT) == null,
        "forged-MakeIndex created an event");
}

static void CheckOutOfBoundsIsAtomic()
{
    ResetRuntime();
    var player = NewPlayer(14, 30);
    var item = AddFirework(player, 321, 654);

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_YANHUA_TEXT,
        nParam1 = item.ClientItemID,
        Payload = EDcode.EncodeBuffer(Body("ABC"))
    }), "out-of-bounds dispatcher result");

    Equal(1, player.m_ItemList.Count, "out-of-bounds item retained");
    Assert(M2Share.EventManager.GetEvent(player.m_PEnvir, 10, 10, 23) == null,
        "out-of-bounds created a partial event");
    var failure = player.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE);
    Equal(0xDB, failure.nParam1, "out-of-bounds foreground");
    Equal(0xFF, failure.nParam2, "out-of-bounds background");
    Equal("此处无法施放传情烟花", failure.Buff,
        "out-of-bounds text");
    Equal(0, M2Share.LogStringList.Count, "out-of-bounds log");
}

static void CheckBlockedCellFireworksArePlaced()
{
    ResetRuntime();
    var player = NewPlayer(30, 30);
    player.m_PEnvir.SetMapXYFlag(10, 10, false);
    player.m_PEnvir.SetMapXYFlag(12, 10, false);
    var item = AddFirework(player, 4321, 8765);

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_YANHUA_TEXT,
        nParam1 = item.ClientItemID,
        Payload = EDcode.EncodeBuffer(Body("AB"))
    }), "blocked-cell dispatcher result");

    var first = M2Share.EventManager.GetEvent(player.m_PEnvir, 10, 10,
        Grobal2.ET_YANHUA_TEXT);
    var second = M2Share.EventManager.GetEvent(player.m_PEnvir, 12, 10,
        Grobal2.ET_YANHUA_TEXT);
    Assert(first != null && second != null,
        "blocked-cell firework events missing from the manager");

    var found = false;
    var firstCell = player.m_PEnvir.GetMapCellInfo(10, 10, ref found);
    Assert(found && !firstCell.Valid && firstCell.ObjList?.Any(node =>
            node.CellType == CellType.OS_EVENTOBJECT &&
            ReferenceEquals(node.CellObj, first)) == true,
        "TFireworksEvent was not linked into the first non-walkable cell");
    var secondCell = player.m_PEnvir.GetMapCellInfo(12, 10, ref found);
    Assert(found && !secondCell.Valid && secondCell.ObjList?.Any(node =>
            node.CellType == CellType.OS_EVENTOBJECT &&
            ReferenceEquals(node.CellObj, second)) == true,
        "TFireworksEvent was not linked into the second non-walkable cell");

    var ordinary = new Event(player.m_PEnvir, 14, 10, 22, 1000, false);
    player.m_PEnvir.SetMapXYFlag(14, 10, false);
    Assert(player.m_PEnvir.AddToMap(14, 10, CellType.OS_EVENTOBJECT,
               ordinary) == null,
        "the non-walkable-cell exception leaked from TFireworksEvent to Event");
    var ordinaryCell = player.m_PEnvir.GetMapCellInfo(14, 10, ref found);
    Assert(found && (ordinaryCell.ObjList == null || ordinaryCell.ObjList.All(
            node => !ReferenceEquals(node.CellObj, ordinary))),
        "ordinary Event was linked into a non-walkable cell");
}

static void CheckSuccessPath()
{
    ResetRuntime();
    var player = NewPlayer(50, 30);
    var item = AddFirework(player, 1234, 7654);

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_YANHUA_TEXT,
        nParam1 = item.ClientItemID,
        // Production-faithful 6-bit-ENCODED wire body; the fixed handler decodes it.
        // encode -> handler decode -> render "传A" IS the firework decode-path proof.
        Payload = EDcode.EncodeBuffer(Body("传A"))
    }), "success dispatcher result");

    Equal(0, player.m_ItemList.Count, "success item consumed");
    Equal((ushort)Grobal2.SM_DELITEM, player.m_DefMsg.Ident,
        "success delete ident");
    Equal(item.ClientItemID, player.m_DefMsg.Recog,
        "success delete item id");
    Equal((ushort)1, player.m_DefMsg.Series, "success delete series");

    var first = M2Share.EventManager.GetEvent(player.m_PEnvir, 10, 10, 23);
    var second = M2Share.EventManager.GetEvent(player.m_PEnvir, 12, 10, 23);
    Assert(first != null && second != null, "success events missing");
    Equal("传", first.m_sEventOwnerName, "success first glyph");
    Equal("A", second.m_sEventOwnerName, "success second glyph");
    Equal(88000, ContinueTime(first), "success event lifetime");
    Equal(88000, ContinueTime(second), "success event lifetime 2");

    Assert(M2Share.LogStringList.Cast<string>().Single().StartsWith(
        "100\tTEST\t10\t10\t测试角色\t传情烟花\t7654\t1\t传A",
        StringComparison.Ordinal), "success transaction log");
    Assert(!player.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE &&
            message.Buff.Contains("请大家前往观看", StringComparison.Ordinal)),
        "success queued a per-player broadcast");
}

static void CheckBroadcastPacketAbi()
{
    const string text = "测试角色在测试地图[10,10]施放传情烟花，请大家前往观看.";
    var method = typeof(TPlayObject).GetMethod(
        "BuildNativeFireworkBroadcastPacket",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("firework broadcast builder missing");
    var packet = (LegacyGateType18)(method.Invoke(null, new object[] { text })
        ?? throw new InvalidOperationException("firework broadcast builder returned null"));
    var textBytes = Encoding.GetEncoding(936).GetBytes(text);

    Equal(0u, packet.FilterUserIndex, "broadcast filter user");
    Equal(0, packet.Recog, "broadcast Recog");
    Equal((ushort)Grobal2.SM_SYSMESSAGE, packet.Ident, "broadcast Ident");
    Equal((ushort)0x38FF, packet.Param, "broadcast Param");
    Equal((ushort)0, packet.Tag, "broadcast Tag");
    Equal((ushort)0, packet.Series, "broadcast Series");
    BytesEqual(textBytes, packet.TextBytes, "broadcast text bytes");

    var frame = packet.ToBytes();
    Equal(LegacyGateType18.MagicValue, BitConverter.ToUInt32(frame, 0),
        "broadcast frame magic");
    Equal(0u, BitConverter.ToUInt32(frame, 4),
        "broadcast ignored connection");
    Equal(0u, BitConverter.ToUInt32(frame, 8),
        "broadcast frame filter");
    Equal(LegacyGateType18.MessageType, BitConverter.ToUInt16(frame, 12),
        "broadcast frame type");
    Equal((ushort)(LegacyGateType18.ClientPacketSize + textBytes.Length + 1),
        BitConverter.ToUInt16(frame, 14), "broadcast payload length");
    Equal(0, BitConverter.ToInt32(frame, 16), "broadcast wire Recog");
    Equal((ushort)Grobal2.SM_SYSMESSAGE, BitConverter.ToUInt16(frame, 20),
        "broadcast wire Ident");
    Equal((ushort)0x38FF, BitConverter.ToUInt16(frame, 22),
        "broadcast wire Param");
    Equal((ushort)0, BitConverter.ToUInt16(frame, 24),
        "broadcast wire Tag");
    Equal((ushort)0, BitConverter.ToUInt16(frame, 26),
        "broadcast wire Series");
    BytesEqual(textBytes, frame.AsSpan(28, textBytes.Length).ToArray(),
        "broadcast wire text");
    Equal((byte)0, frame[^1], "broadcast wire terminator");
}

static void CheckInvalidGbkWirePreservation()
{
    ResetRuntime();
    var player = NewPlayer(30, 30);
    var item = AddFirework(player, 1234, 7654);

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_YANHUA_TEXT,
        nParam1 = item.ClientItemID,
        Payload = EDcode.EncodeBuffer(new byte[] { 0x81, 0x30, 0 })
    }), "malformed-GBK dispatcher result");

    var mapEvent = M2Share.EventManager.GetEvent(player.m_PEnvir, 10, 10,
        Grobal2.ET_YANHUA_TEXT);
    Assert(mapEvent != null, "malformed-GBK event missing");
    var builder = typeof(TPlayObject).GetMethod("BuildShowEventBody",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("SHOWEVENT builder missing");
    var body = (byte[])(builder.Invoke(null, new object[] { mapEvent, 0 })
        ?? throw new InvalidOperationException("SHOWEVENT builder returned null"));
    Equal((byte)2, body[8], "malformed-GBK SHOWEVENT length");
    BytesEqual(new byte[] { 0x81, 0x30 }, body[9..11],
        "malformed-GBK SHOWEVENT bytes");
}

static List<(string Text, int X, int Y, byte[] RawBytes)> Plan(byte[] payload,
    int x, int y,
    out string logText)
{
    var method = typeof(TPlayObject).GetMethod(
        "BuildNativeFireworkTextGlyphs",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("firework planner missing");
    var arguments = new object[] { payload, x, y, null };
    var result = method.Invoke(null, arguments)
                 ?? throw new InvalidOperationException("planner returned null");
    logText = (string)arguments[3];
    return ((IEnumerable<(string Text, int X, int Y, byte[] RawBytes)>)result)
        .ToList();
}

static void Glyph(
    IReadOnlyList<(string Text, int X, int Y, byte[] RawBytes)> glyphs,
    int index, string text, int x, int y, string label)
{
    Assert(index < glyphs.Count, label + " missing");
    Equal(text, glyphs[index].Text, label + " text");
    Equal(x, glyphs[index].X, label + " x");
    Equal(y, glyphs[index].Y, label + " y");
}

static ProbePlayer NewPlayer(short width, short height)
{
    var environment = new Envirnoment
    {
        sMapDesc = "测试地图",
        sMapName = "TEST"
    };
    typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(environment,
        new object[] { width, height });
    return new ProbePlayer
    {
        m_PEnvir = environment,
        m_sMapName = environment.sMapName,
        m_sCharName = "测试角色",
        m_nCurrX = 10,
        m_nCurrY = 10,
        m_MsgList = new List<SendMessage>(),
        m_boOffLineFlag = true
    };
}

static TUserItem AddFirework(TPlayObject player, int makeIndex,
    int clientItemId)
{
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "传情烟花",
        Weight = 1
    });
    var item = new TUserItem
    {
        wIndex = 1,
        MakeIndex = makeIndex,
        ClientItemID = clientItemId
    };
    player.m_ItemList.Add(item);
    return item;
}

static int ContinueTime(Event value)
{
    return (int)(typeof(Event).GetField("m_dwContinueTime",
        BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value)
        ?? throw new InvalidOperationException("event lifetime field missing"));
}

static byte[] Body(string text)
{
    var bytes = Encoding.GetEncoding(936).GetBytes(text);
    var result = new byte[bytes.Length + 1];
    Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
    return result;
}

static void ResetRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.EventManager = new EventManager();
    M2Share.GateManager = null;
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
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

static void BytesEqual(byte[] expected, byte[] actual, string label)
{
    if (!expected.AsSpan().SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{label}: expected={Convert.ToHexString(expected)}, actual={Convert.ToHexString(actual)}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

sealed class ProbePlayer : TPlayObject
{
    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);
}
