using System.Buffers.Binary;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Text;
using DBSvr.Core;
using GameSvr;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;

PrepareRuntimeFiles();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var failures = new List<string>();
await Run("0056 exact response layout and fail-closed decoder",
    ResponseLayoutAndDecoder);
await Run("0056 +25 sender lookup and exact failure notification",
    FailureLookupAndMessage);
await Run("0056 online actor gates stay silent", ActorGatesStaySilent);
await Run("0056 success removes first match and preserves native ordering",
    SuccessFirstMatchPacketLogAndMessageOrder);
await Run("0056 StdMode 7 logs Dura and removes whole item",
    DurabilityCountAndWholeRemoval);
await Run("0056 missing or undefined item stays silent and unchanged",
    MissingAndUndefinedStaySilent);
await Run("0056 zero MakeIndex remains an exact lookup value",
    ZeroMakeIndexIsNotSpecialCased);

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeItemInjectionClientCheck PASS tests=7 " +
                  "type1=0056 lookup=sender+25 commit=remove/del/log/msg " +
                  "count=StdMode7:Dura no-weight-refresh");
return 0;

Task ResponseLayoutAndDecoder()
{
    var attachment = new byte[NativeItemInjectionProtocol.ItemSize];
    BinaryPrimitives.WriteInt32LittleEndian(attachment,
        unchecked((int)0x89ABCDEF));
    var mail = Request("AccountField", "SenderField", "TargetField",
        attachment);
    Assert(NativeItemInjectionProtocol.TryDecodeMail(mail,
        out var request, out var error), "0154 seed decode: " + error);

    var frame = NativeItemInjectionProtocol.CreateMailResponse(request, 7);
    Equal(1, frame.Type, "outer type");
    Equal(NativeItemInjectionProtocol.HeaderSize, frame.Payload.Length,
        "payload size");
    Equal(NativeItemInjectionProtocol.MailResponseCommand,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload), "command");
    Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(
        frame.Payload.AsSpan(2)), "status offset +2");
    Equal(unchecked((int)0x89ABCDEF),
        BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(4)),
        "MakeIndex offset +4");

    Assert(NativeItemInjectionProtocol.TryDecodeMailResponse(frame,
        out var response, out error), "0056 decode: " + error);
    Equal((ushort)7, response.Status, "decoded status");
    Equal(unchecked((int)0x89ABCDEF), response.MakeIndex,
        "decoded MakeIndex");
    Text("AccountField", response.Account, "account +10");
    Text("SenderField", response.CharacterName, "sender +25");
    Text("TargetField", response.TargetName, "target +35");

    Assert(!NativeItemInjectionProtocol.TryDecodeMailResponse(null,
        out _, out _), "null frame decoded");
    Assert(!NativeItemInjectionProtocol.TryDecodeMailResponse(
        new LegacyDbServerFrame(2, 0, frame.Payload), out _, out _),
        "wrong outer type decoded");
    Assert(!NativeItemInjectionProtocol.TryDecodeMailResponse(
        new LegacyDbServerFrame(1, 0,
            frame.Payload.AsSpan(0,
                NativeItemInjectionProtocol.HeaderSize - 1).ToArray()),
        out _, out _), "short payload decoded");

    var malformed = (byte[])frame.Payload.Clone();
    malformed[0] = 0x55;
    Assert(!NativeItemInjectionProtocol.TryDecodeMailResponse(
        new LegacyDbServerFrame(1, 0, malformed), out _, out _),
        "wrong command decoded");
    foreach (var field in new[] { (Offset: 0x10, Capacity: 20),
                 (Offset: 0x25, Capacity: 15),
                 (Offset: 0x35, Capacity: 15) })
    {
        malformed = (byte[])frame.Payload.Clone();
        malformed[field.Offset] = checked((byte)(field.Capacity + 1));
        Assert(!NativeItemInjectionProtocol.TryDecodeMailResponse(
            new LegacyDbServerFrame(1, 0, malformed), out _, out _),
            "oversized ShortString decoded at " + field.Offset);
    }

    var tailed = new byte[frame.Payload.Length + 9];
    frame.Payload.CopyTo(tailed, 0);
    tailed[^1] = 0xA5;
    Assert(NativeItemInjectionProtocol.TryDecodeMailResponse(
        new LegacyDbServerFrame(1, 0, tailed), out response, out error),
        "native callback-compatible trailing bytes: " + error);
    Equal(unchecked((int)0x89ABCDEF), response.MakeIndex,
        "tailed MakeIndex");
    return Task.CompletedTask;
}

Task FailureLookupAndMessage()
{
    using var runtime = NewRuntime();
    var sender = NewPlayer("SenderLookup", ready: true);
    var accountActor = NewPlayer("AccountActor", ready: true);
    var targetActor = NewPlayer("TargetActor", ready: true);
    AddOnline(sender);
    AddOnline(accountActor);
    AddOnline(targetActor);
    var item = Item(81001);
    sender.m_ItemList.Add(item);

    NativeItemInjectionClient.ProcessResponse(Response("AccountActor",
        "SenderLookup", "TargetActor", status: 2, makeIndex: 81001));

    Same(item, sender.m_ItemList.Single(), "failure bag item");
    AssertSystemMessage(sender,
        "给予失败: 没有这个玩家或 TargetActor背包空位不足。",
        M2Share.g_Config.btRedMsgFColor,
        M2Share.g_Config.btRedMsgBColor, "failure exact text");
    Equal(0, accountActor.m_MsgList.Count, "account field actor message");
    Equal(0, targetActor.m_MsgList.Count, "target field actor message");
    Equal(0, M2Share.LogStringList.Count, "failure log count");
    Equal(0, sender.DeletePackets.Count, "failure delete packet count");
    return Task.CompletedTask;
}

Task ActorGatesStaySilent()
{
    using var runtime = NewRuntime();
    NativeItemInjectionClient.ProcessResponse(Response("Account",
        "MissingSender", "Target", status: 2, makeIndex: 82001));
    Equal(0, M2Share.LogStringList.Count, "missing sender log");

    var sender = NewPlayer("GatedSender", ready: true);
    sender.m_ItemList.Add(Item(82001));
    AddOnline(sender);

    sender.m_boGhost = true;
    NativeItemInjectionClient.ProcessResponse(Response("Account",
        sender.m_sCharName, "GhostTarget", status: 2, makeIndex: 82001));
    sender.m_boGhost = false;
    Equal(0, sender.m_MsgList.Count, "ghost message count");

    sender.m_boReadyRun = false;
    NativeItemInjectionClient.ProcessResponse(Response("Account",
        sender.m_sCharName, "NotReadyTarget",
        NativeItemInjectionProtocol.Success, 82001));
    Equal(1, sender.m_ItemList.Count, "not-ready bag count");
    Equal(0, sender.m_MsgList.Count, "not-ready message count");
    Equal(0, sender.DeletePackets.Count, "gated delete packets");
    Equal(0, M2Share.LogStringList.Count, "gated log count");
    return Task.CompletedTask;
}

Task SuccessFirstMatchPacketLogAndMessageOrder()
{
    using var runtime = NewRuntime();
    var sender = NewPlayer("CommitSender", ready: true, offline: false);
    sender.m_sMapName = "CommitMap";
    sender.m_nCurrX = 12;
    sender.m_nCurrY = 34;
    AddOnline(sender);

    var first = Item(unchecked((int)0x89ABCDEF), dura: 41);
    first.ClientItemID = 0x13572468;
    var duplicate = Item(unchecked((int)0x89ABCDEF), dura: 99);
    var unrelated = Item(83002);
    sender.m_ItemList.Add(first);
    sender.m_ItemList.Add(duplicate);
    sender.m_ItemList.Add(unrelated);
    sender.ExpectedDeletedItem = first;

    var logCountWhenSuccessWasQueued = -1;
    var messages = new ObservableCollection<SendMessage>();
    messages.CollectionChanged += (_, args) =>
    {
        if (args.Action != NotifyCollectionChangedAction.Add
            || args.NewItems == null)
            return;
        foreach (SendMessage message in args.NewItems)
        {
            if (message.wIdent == Grobal2.RM_SYSMESSAGE
                && message.Buff == "成功交易 审计物品 给 OfflineTarget")
                logCountWhenSuccessWasQueued = M2Share.LogStringList.Count;
        }
    };
    sender.m_MsgList = messages;

    NativeItemInjectionClient.ProcessResponse(Response("IgnoredAccount",
        sender.m_sCharName, "OfflineTarget",
        NativeItemInjectionProtocol.Success,
        unchecked((int)0x89ABCDEF)));

    Equal(2, sender.m_ItemList.Count, "success bag count");
    Assert(!sender.m_ItemList.Contains(first), "first duplicate remained");
    Same(duplicate, sender.m_ItemList[0], "second duplicate order");
    Same(unrelated, sender.m_ItemList[1], "unrelated item order");
    Equal(1, sender.DeletePackets.Count, "SM_DELITEM count");
    var delete = sender.DeletePackets.Single();
    Equal((ushort)Grobal2.SM_DELITEM, delete.Ident, "SM_DELITEM ident");
    Equal(first.ClientItemID, delete.Recog, "SM_DELITEM client id");
    Equal((ushort)1, delete.Series, "SM_DELITEM series");
    Assert(!delete.ItemWasPresent, "item was present when SM_DELITEM sent");
    Equal(0, delete.LogCount, "log existed before SM_DELITEM");
    Equal(0, delete.SystemMessageCount,
        "success message existed before SM_DELITEM");

    Equal(1, M2Share.LogStringList.Count, "success log count");
    Sequence(new[]
    {
        "8", "CommitMap", "12", "34", "CommitSender", "审计物品",
        "2309737967", "1", "OfflineTarget"
    }, ((string)M2Share.LogStringList[0]).Split('\t'), "success log");
    Equal(1, logCountWhenSuccessWasQueued,
        "log must precede success message");
    AssertSystemMessage(sender, "成功交易 审计物品 给 OfflineTarget",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor, "success exact text");
    Equal(0, sender.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_WEIGHTCHANGED),
        "success weight refresh");
    return Task.CompletedTask;
}

Task DurabilityCountAndWholeRemoval()
{
    using var runtime = NewRuntime(stdMode: 7);
    var sender = NewPlayer("DuraSender", ready: true);
    sender.m_sMapName = "DuraMap";
    AddOnline(sender);
    var item = Item(84001, dura: 23);
    sender.m_ItemList.Add(item);

    NativeItemInjectionClient.ProcessResponse(Response("Account",
        sender.m_sCharName, "DuraTarget",
        NativeItemInjectionProtocol.Success, item.MakeIndex));

    Equal(0, sender.m_ItemList.Count, "StdMode7 whole-item removal");
    Equal((ushort)23, item.Dura, "StdMode7 Dura mutation");
    Equal(1, M2Share.LogStringList.Count, "StdMode7 log count");
    Sequence(new[]
    {
        "8", "DuraMap", "0", "0", "DuraSender", "审计物品",
        "84001", "23", "DuraTarget"
    }, ((string)M2Share.LogStringList[0]).Split('\t'),
        "StdMode7 log");
    return Task.CompletedTask;
}

Task MissingAndUndefinedStaySilent()
{
    using var runtime = NewRuntime();
    var sender = NewPlayer("SilentSender", ready: true);
    AddOnline(sender);
    var known = Item(85001);
    var undefined = Item(85002, itemIndex: 2);
    sender.m_ItemList.Add(known);
    sender.m_ItemList.Add(undefined);

    NativeItemInjectionClient.ProcessResponse(Response("Account",
        sender.m_sCharName, "MissingTarget",
        NativeItemInjectionProtocol.Success, 85999));
    NativeItemInjectionClient.ProcessResponse(Response("Account",
        sender.m_sCharName, "UndefinedTarget",
        NativeItemInjectionProtocol.Success, 85002));

    Equal(2, sender.m_ItemList.Count, "silent bag count");
    Same(known, sender.m_ItemList[0], "known item");
    Same(undefined, sender.m_ItemList[1], "undefined item");
    Equal(0, sender.m_MsgList.Count, "silent message count");
    Equal(0, sender.DeletePackets.Count, "silent delete packet count");
    Equal(0, M2Share.LogStringList.Count, "silent log count");
    return Task.CompletedTask;
}

Task ZeroMakeIndexIsNotSpecialCased()
{
    using var runtime = NewRuntime();
    var sender = NewPlayer("ZeroSender", ready: true);
    AddOnline(sender);
    var zero = Item(0);
    sender.m_ItemList.Add(zero);

    NativeItemInjectionClient.ProcessResponse(Response("Account",
        sender.m_sCharName, "ZeroTarget",
        NativeItemInjectionProtocol.Success, 0));

    Equal(0, sender.m_ItemList.Count, "zero MakeIndex bag count");
    Equal(1, sender.DeletePackets.Count, "zero MakeIndex delete count");
    Equal(1, M2Share.LogStringList.Count, "zero MakeIndex log count");
    var fields = ((string)M2Share.LogStringList[0]).Split('\t');
    Equal("0", fields[6], "zero MakeIndex log");
    return Task.CompletedTask;
}

async Task Run(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures.Add("FAIL " + name + ": " + Unwrap(exception).Message);
    }
}

static RuntimeScope NewRuntime(byte stdMode = 1)
{
    M2Share.g_Config = new GameSvrConfig
    {
        nCheckBlock = 0,
        btRedMsgFColor = 251,
        btRedMsgBColor = 3,
        btGreenMsgFColor = 252,
        btGreenMsgBColor = 4
    };
    M2Share.ObjectManager = new ObjectManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
    M2Share.LogonCostLogList = new ArrayList();
    M2Share.StartPointList = new List<TStartPoint>();
    M2Share.DataServer = null;
    var engine = new UserEngine();
    engine.StdItemList.Add(new GoodItem
    {
        Name = "审计物品",
        StdMode = stdMode,
        DuraMax = 100
    });
    M2Share.UserEngine = engine;
    return new RuntimeScope();
}

static RecordingPlayer NewPlayer(string name, bool ready,
    bool offline = true) => new()
{
    m_sCharName = name,
    m_sUserID = "User-" + name,
    m_boReadyRun = ready,
    m_boOffLineFlag = offline
};

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

static TUserItem Item(int makeIndex, ushort itemIndex = 1,
    ushort dura = 10) => new()
{
    MakeIndex = makeIndex,
    wIndex = itemIndex,
    Dura = dura,
    DuraMax = 100
};

static LegacyDbServerFrame Request(string account, string sender,
    string target, byte[] attachment)
{
    var payload = new byte[NativeItemInjectionProtocol.HeaderSize
                           + attachment.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeItemInjectionProtocol.MailRequestCommand);
    WriteShortString(payload, 0x10, 20, account);
    WriteShortString(payload, 0x25, 15, sender);
    WriteShortString(payload, 0x35, 15, target);
    attachment.CopyTo(payload, NativeItemInjectionProtocol.HeaderSize);
    return new LegacyDbServerFrame(1, 0, payload);
}

static LegacyDbServerFrame Response(string account, string sender,
    string target, ushort status, int makeIndex)
{
    var payload = new byte[NativeItemInjectionProtocol.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeItemInjectionProtocol.MailResponseCommand);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), status);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), makeIndex);
    WriteShortString(payload, 0x10, 20, account);
    WriteShortString(payload, 0x25, 15, sender);
    WriteShortString(payload, 0x35, 15, target);
    return new LegacyDbServerFrame(1, 0, payload);
}

static void WriteShortString(Span<byte> payload, int offset, int capacity,
    string value)
{
    var bytes = Encoding.GetEncoding(936).GetBytes(value ?? string.Empty);
    var length = Math.Min(bytes.Length, capacity);
    payload[offset] = checked((byte)length);
    bytes.AsSpan(0, length).CopyTo(payload.Slice(offset + 1));
}

static void AssertSystemMessage(TPlayObject player, string text,
    byte foreground, byte background, string label)
{
    var messages = player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE && message.Buff == text)
        .ToArray();
    Equal(1, messages.Length, label + " count");
    Equal(0, messages[0].wParam, label + " wParam");
    Equal((int)foreground, messages[0].nParam1, label + " foreground");
    Equal((int)background, messages[0].nParam2, label + " background");
    Equal(0, messages[0].nParam3, label + " nParam3");
}

static void Text(string expected, byte[] actual, string label) =>
    Equal(expected, Encoding.GetEncoding(936).GetString(actual), label);

static void Sequence<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string label)
{
    Assert(expected.SequenceEqual(actual),
        $"{label}: expected=[{string.Join(',', expected)}], " +
        $"actual=[{string.Join(',', actual)}]");
}

static Exception Unwrap(Exception exception)
{
    while (exception is TargetInvocationException { InnerException: not null })
        exception = exception.InnerException;
    return exception;
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

static void Same(object expected, object actual, string label) =>
    Assert(ReferenceEquals(expected, actual), label + " reference changed");

static void Equal<T>(T expected, T actual, string label)
{
    Assert(EqualityComparer<T>.Default.Equals(expected, actual),
        $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class RecordingPlayer : TPlayObject
{
    internal TUserItem ExpectedDeletedItem { get; set; }
    internal List<DeleteObservation> DeletePackets { get; } = new();

    internal override void SendSocket(ClientPacket defMsg, string message)
    {
        if (defMsg?.Ident != Grobal2.SM_DELITEM)
            return;
        DeletePackets.Add(new DeleteObservation(defMsg.Ident, defMsg.Recog,
            defMsg.Series,
            ExpectedDeletedItem != null
            && m_ItemList.Contains(ExpectedDeletedItem),
            M2Share.LogStringList?.Count ?? 0,
            m_MsgList.Count(candidate =>
                candidate.wIdent == Grobal2.RM_SYSMESSAGE)));
    }
}

readonly record struct DeleteObservation(ushort Ident, int Recog,
    ushort Series, bool ItemWasPresent, int LogCount,
    int SystemMessageCount);

sealed class RuntimeScope : IDisposable
{
    public void Dispose()
    {
        M2Share.DataServer = null;
        M2Share.UserEngine = null;
        M2Share.LogStringList = null;
    }
}
