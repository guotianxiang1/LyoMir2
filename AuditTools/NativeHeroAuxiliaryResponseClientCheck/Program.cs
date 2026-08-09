using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;

PrepareRuntimeFiles();

var failures = new List<string>();
Run("005D raw tail and Series mapping", ConsignedListFields);
Run("005D invalid count emits empty list", ConsignedListInvalidCount);
Run("005E native dialogs and state masks", RestoreConsignedDialogsAndState);
Run("005E current-NPC dialog packet", RestoreConsignedCurrentNpc);
Run("005E unknown result is silent", RestoreConsignedUnknownResult);
Run("0070 state-load-client response order", BuildThreeSlotOrder);
Run("0070 passes unbounded result through Series", BuildThreeSlotRawResult);
Run("actor lookup first match and ReadyRun gate", FirstMatchLookup);
Run("malformed auxiliary responses stay silent", MalformedFrames);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeHeroAuxiliaryResponseClientCheck PASS tests=9 " +
                  "type1=005D/005E/0070 raw-tail/state/order");
return 0;

void ConsignedListFields()
{
    using var runtime = NewRuntime();
    var owner = Player("列表主人", 0x4A01);
    AddOnline(owner);
    var payload = Payload(NativeHeroDbFrameCodec.ConsignedListResponseCommand,
        2, NativeHeroDbFrameCodec.MessageHeaderSize
           + 2 * NativeHeroDbFrameCodec.ConsignedListEntrySize);
    WriteShortString(payload, 37, 15, owner.m_sCharName);
    for (var index = NativeHeroDbFrameCodec.MessageHeaderSize;
         index < payload.Length; index++)
        payload[index] = unchecked((byte)(0x80 + index));

    var sent = new List<CapturedPacket>();
    var result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        new LegacyDbServerFrame(1, 0xBEEF, payload),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer,
        Capture(sent), _ => throw new InvalidOperationException("005D load"));

    Equal(NativeHeroAuxiliaryResponseDisposition.ConsignedListSent, result,
        "005D disposition");
    Equal(1, sent.Count, "005D packet count");
    Header(sent[0], Grobal2.SM_HEROLISTINFO, owner.ObjectId, 0, 0, 2,
        "005D header");
    Bytes(payload[NativeHeroDbFrameCodec.MessageHeaderSize..], sent[0].Body,
        "005D forwards the original raw 22-byte entries");
}

void ConsignedListInvalidCount()
{
    using var runtime = NewRuntime();
    var owner = Player("列表回退", 0x4A02);
    AddOnline(owner);
    var payload = Payload(NativeHeroDbFrameCodec.ConsignedListResponseCommand,
        0, NativeHeroDbFrameCodec.MessageHeaderSize);
    WriteShortString(payload, 37, 15, owner.m_sCharName);
    var sent = new List<CapturedPacket>();

    var result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        new LegacyDbServerFrame(1, 0, payload),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent), _ => { });

    Equal(NativeHeroAuxiliaryResponseDisposition.ConsignedListEmptySent,
        result, "005D zero-count disposition");
    Equal(1, sent.Count, "005D zero-count packet count");
    Header(sent[0], Grobal2.SM_HEROLISTINFO, owner.ObjectId, 0, 0, 0,
        "005D zero-count header");
    Equal(0, sent[0].Body.Length, "005D zero-count body length");

    payload = Payload(NativeHeroDbFrameCodec.ConsignedListResponseCommand,
        2, NativeHeroDbFrameCodec.MessageHeaderSize
           + NativeHeroDbFrameCodec.ConsignedListEntrySize);
    WriteShortString(payload, 37, 15, owner.m_sCharName);
    sent.Clear();
    result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        new LegacyDbServerFrame(1, 0, payload),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent), _ => { });
    Equal(NativeHeroAuxiliaryResponseDisposition.ConsignedListEmptySent,
        result, "005D mismatched-tail disposition");
    Equal(1, sent.Count, "005D mismatched-tail packet count");
    Header(sent[0], Grobal2.SM_HEROLISTINFO, owner.ObjectId, 0, 0, 0,
        "005D mismatched-tail header");
}

void RestoreConsignedDialogsAndState()
{
    using var runtime = NewRuntime();
    var owner = Player("取回主人", 0x4A03);
    owner.m_NativeScriptData = new byte[0x60];
    owner.m_btNativeHeroState = 0xFC;
    owner.m_NativeScriptData[TPlayObject.NativeHeroStateOffset] = 0xFC;
    AddOnline(owner);
    var sent = new List<CapturedPacket>();

    var success = RestoreResponse(owner.m_sCharName, 1, 1);
    var result = NativeHeroAuxiliaryResponseClient.ProcessResponse(success,
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent), _ => { });
    Equal(NativeHeroAuxiliaryResponseDisposition.RestoreConsignedDialogSent,
        result, "005E success disposition");
    Equal((byte)0xF9, owner.m_btNativeHeroState,
        "005E type1 state mask");
    Equal((byte)0xF9,
        owner.m_NativeScriptData[TPlayObject.NativeHeroStateOffset],
        "005E type1 persisted state");
    Equal(1, sent.Count, "005E success packet count");
    Header(sent[0], Grobal2.SM_MERCHANTSAY, 0, 0, 0, 0,
        "005E success fallback header");
    Gbk("NPC/" + NativeHeroAuxiliaryResponseClient.RestoreSuccessDialog,
        sent[0].Body, "005E success text");

    sent.Clear();
    var stateBeforeFailure = owner.m_btNativeHeroState;
    result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        RestoreResponse(owner.m_sCharName, 0, 0),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent), _ => { });
    Equal(NativeHeroAuxiliaryResponseDisposition.RestoreConsignedDialogSent,
        result, "005E failure disposition");
    Equal(stateBeforeFailure, owner.m_btNativeHeroState,
        "005E failure leaves state unchanged");
    Gbk("NPC/" + NativeHeroAuxiliaryResponseClient.RestoreFailureDialog,
        sent[0].Body, "005E failure text");

    sent.Clear();
    var stateBeforeUnknownType = owner.m_btNativeHeroState;
    result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        RestoreResponse(owner.m_sCharName, 1, unchecked((int)0xFFFFFFFF)),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent), _ => { });
    Equal(NativeHeroAuxiliaryResponseDisposition.RestoreConsignedDialogSent,
        result, "005E arbitrary hero type disposition");
    Equal(stateBeforeUnknownType, owner.m_btNativeHeroState,
        "005E arbitrary hero type does not mutate state");
    Gbk("NPC/" + NativeHeroAuxiliaryResponseClient.RestoreSuccessDialog,
        sent[0].Body, "005E arbitrary hero type still reports success");
}

void RestoreConsignedCurrentNpc()
{
    using var runtime = NewRuntime();
    var owner = Player("NPC主人", 0x4A04);
    var npc = new NormNpc { m_sCharName = "英雄使者" };
    owner.m_NPC = npc;
    AddOnline(owner);
    var sent = new List<CapturedPacket>();

    var result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        RestoreResponse(owner.m_sCharName, 2, 0),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent), _ => { });

    Equal(NativeHeroAuxiliaryResponseDisposition.RestoreConsignedDialogSent,
        result, "005E current-NPC disposition");
    Equal(1, sent.Count, "005E current-NPC packet count");
    Header(sent[0], Grobal2.SM_MERCHANTSAY, npc.ObjectId, 0, 0, 1,
        "005E current-NPC header");
    Gbk(npc.m_sCharName + "/" +
        NativeHeroAuxiliaryResponseClient.RestoreHasHeroDialog, sent[0].Body,
        "005E current-NPC text");
}

void RestoreConsignedUnknownResult()
{
    using var runtime = NewRuntime();
    var owner = Player("静默主人", 0x4A05);
    owner.m_btNativeHeroState = 0xA5;
    AddOnline(owner);
    var sent = new List<CapturedPacket>();

    var result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        RestoreResponse(owner.m_sCharName, 3, 1),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent), _ => { });

    Equal(NativeHeroAuxiliaryResponseDisposition.RestoreConsignedIgnored,
        result, "005E unknown-result disposition");
    Equal(0, sent.Count, "005E unknown-result packet count");
    Equal((byte)0xA5, owner.m_btNativeHeroState,
        "005E unknown-result state");
}

void BuildThreeSlotOrder()
{
    using var runtime = NewRuntime();
    var owner = Player("三槽主人", 0x4A06);
    owner.m_NativeScriptData = new byte[0x60];
    owner.m_btNativeHeroState = 0xFC;
    owner.m_NativeScriptData[TPlayObject.NativeHeroStateOffset] = 0xFC;
    AddOnline(owner);
    var sent = new List<CapturedPacket>();
    var order = new List<string>();
    byte stateAtLoad = 0;

    var result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        BuildResponse(owner.m_sCharName, 1, unchecked((int)0xDEADBEEF)),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer,
        (player, header, body) =>
        {
            order.Add("send");
            sent.Add(new CapturedPacket(player, header, body));
        }, player =>
        {
            order.Add("load");
            stateAtLoad = player.m_btNativeHeroState;
        });

    Equal(NativeHeroAuxiliaryResponseDisposition.BuildThreeSlotSent, result,
        "0070 disposition");
    Equal("load,send", string.Join(',', order), "0070 load/send order");
    Equal((byte)0xF3, stateAtLoad, "0070 state before default load");
    Equal((byte)0xF3, owner.m_btNativeHeroState, "0070 state mask");
    Equal((byte)0xF3,
        owner.m_NativeScriptData[TPlayObject.NativeHeroStateOffset],
        "0070 persisted state");
    Equal(1, sent.Count, "0070 packet count");
    Header(sent[0], Grobal2.SM_SECHERO_EST, 0, 0, 0, 1,
        "0070 success header");
    Equal(0, sent[0].Body.Length, "0070 success body");
}

void BuildThreeSlotRawResult()
{
    using var runtime = NewRuntime();
    var owner = Player("透传主人", 0x4A07);
    owner.m_btNativeHeroState = 0xA5;
    AddOnline(owner);
    var sent = new List<CapturedPacket>();
    var loadCount = 0;

    var result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        BuildResponse(owner.m_sCharName, 0x1234, 0x7F00AA55),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent),
        _ => loadCount++);

    Equal(NativeHeroAuxiliaryResponseDisposition.BuildThreeSlotSent, result,
        "0070 high-result disposition");
    Equal(0, loadCount, "0070 high-result load count");
    Equal((byte)0xA5, owner.m_btNativeHeroState,
        "0070 high-result state");
    Equal(1, sent.Count, "0070 high-result packet count");
    Header(sent[0], Grobal2.SM_SECHERO_EST, 0, 0, 0, 0x1234,
        "0070 high-result header");
}

void FirstMatchLookup()
{
    using var runtime = NewRuntime();
    var firstGhost = Player("SameName", 0x4A08);
    firstGhost.m_boGhost = true;
    var laterReady = Player("samename", 0x4A09);
    Equal<TPlayObject>(null,
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer(
            new[] { firstGhost, laterReady }, "SAMENAME"),
        "ghost first match blocks later duplicate");

    firstGhost.m_boGhost = false;
    firstGhost.m_boReadyRun = false;
    Equal<TPlayObject>(null,
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer(
            new[] { firstGhost, laterReady }, "SameName"),
        "not-ready first match blocks later duplicate");

    firstGhost.m_boReadyRun = true;
    Same(firstGhost, NativeHeroAuxiliaryResponseClient.FindOnlinePlayer(
            new[] { firstGhost, laterReady }, "samename"),
        "first ready match selected");
}

void MalformedFrames()
{
    using var runtime = NewRuntime();
    var owner = Player("畸形主人", 0x4A0A);
    AddOnline(owner);
    var sent = new List<CapturedPacket>();

    var result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        new LegacyDbServerFrame(2, 0,
            new byte[NativeHeroDbFrameCodec.MessageHeaderSize]),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent), _ => { });
    Equal(NativeHeroAuxiliaryResponseDisposition.InvalidFrame, result,
        "wrong outer type");

    var badName = Payload(NativeHeroDbFrameCodec.RestoreConsignedResponseCommand,
        1, NativeHeroDbFrameCodec.MessageHeaderSize);
    badName[37] = 16;
    result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        new LegacyDbServerFrame(1, 0, badName),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent), _ => { });
    Equal(NativeHeroAuxiliaryResponseDisposition.InvalidFrame, result,
        "oversized master ShortString");

    owner.m_boGhost = true;
    result = NativeHeroAuxiliaryResponseClient.ProcessResponse(
        RestoreResponse(owner.m_sCharName, 0, 0),
        NativeHeroAuxiliaryResponseClient.FindOnlinePlayer, Capture(sent), _ => { });
    Equal(NativeHeroAuxiliaryResponseDisposition.PlayerNotFound, result,
        "ghost owner");
    Equal(0, sent.Count, "malformed and missing-owner packet count");
}

static LegacyDbServerFrame RestoreResponse(string masterName, ushort result,
    int heroType)
{
    var payload = Payload(NativeHeroDbFrameCodec.RestoreConsignedResponseCommand,
        result, NativeHeroDbFrameCodec.MessageHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), heroType);
    WriteShortString(payload, 37, 15, masterName);
    return new LegacyDbServerFrame(1, 0, payload);
}

static LegacyDbServerFrame BuildResponse(string masterName, ushort result,
    int reservedDword)
{
    var payload = Payload(NativeHeroDbFrameCodec.BuildThreeSlotResponseCommand,
        result, NativeHeroDbFrameCodec.MessageHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
        reservedDword);
    WriteShortString(payload, 16, 20, masterName);
    return new LegacyDbServerFrame(1, 0, payload);
}

static byte[] Payload(ushort command, ushort result, int length)
{
    var payload = new byte[length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), command);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), result);
    return payload;
}

static void WriteShortString(byte[] payload, int offset, int capacity,
    string value)
{
    var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
    if (bytes.Length > capacity)
        throw new ArgumentOutOfRangeException(nameof(value));
    payload[offset] = (byte)bytes.Length;
    bytes.CopyTo(payload, offset + 1);
}

static TPlayObject Player(string name, int ignoredId) => new()
{
    m_sCharName = name,
    m_sUserID = "Account-" + ignoredId,
    m_boReadyRun = true,
    m_boOffLineFlag = true
};

static Action<TPlayObject, ClientPacket, byte[]> Capture(
    ICollection<CapturedPacket> destination)
{
    return (owner, header, body) =>
        destination.Add(new CapturedPacket(owner, header, body));
}

static void Header(CapturedPacket actual, int ident, int recog, int param,
    int tag, int series, string label)
{
    Equal((ushort)ident, actual.Ident, label + " ident");
    Equal(recog, actual.Recog, label + " recog");
    Equal((ushort)param, actual.Param, label + " param");
    Equal((ushort)tag, actual.Tag, label + " tag");
    Equal((ushort)series, actual.Series, label + " series");
}

static void Gbk(string expected, byte[] actual, string label)
{
    Bytes(HUtil32.GbkEncoding.GetBytes(expected), actual, label);
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

sealed class CapturedPacket
{
    public CapturedPacket(TPlayObject owner, ClientPacket packet, byte[] body)
    {
        Owner = owner;
        Ident = packet.Ident;
        Recog = packet.Recog;
        Param = packet.Param;
        Tag = packet.Tag;
        Series = packet.Series;
        Body = body?.ToArray() ?? Array.Empty<byte>();
    }

    public TPlayObject Owner { get; }
    public ushort Ident { get; }
    public int Recog { get; }
    public ushort Param { get; }
    public ushort Tag { get; }
    public ushort Series { get; }
    public byte[] Body { get; }
}

sealed class RuntimeScope : IDisposable
{
    public void Dispose()
    {
        M2Share.UserEngine = null;
        M2Share.ObjectManager = null;
        M2Share.g_Config = null;
    }
}
