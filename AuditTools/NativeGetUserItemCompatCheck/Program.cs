using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using DBSvr.Core;
using GameSvr;
using GameSvr.CommandSystem;
using SystemModule;
using SystemModule.Packet;

PrepareRuntimeFiles();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var failures = new List<string>();
await Run("command permission 4 and native help color", CommandPermissionAndHelp);
await Run("0153 exact fields, byte truncation, UserID, invalid id zero",
    RequestFieldsAndInvalidId);
await Run("0153 uint32 decimal ItemID preserves int32 bits",
    RequestUnsignedItemId);
await Run("bag-capacity gate blocks 0153", BagCapacityGate);
await Run("online player equipment-bag-storage order and DB fallback",
    OnlinePlayerOrderAndFallback);
await Run("online hero extraction then DB fallback", OnlineHeroAndFallback);
await Run("equipment feature-slot matrix and no weight refresh",
    EquipmentFeatureSlotsAndNoWeightRefresh);
await Run("0055 exact success preserves record and emits SM_ADDITEM",
    ResponseSuccessAndAddItemPacket);
await Run("0055 failure and 207/209 body mismatch", ResponseFailureShapes);
await Run("0055 missing requester and invalid item stay silent",
    ResponseSilentCases);
await Run("0055 full bag keeps native success ordering without SM_ADDITEM",
    ResponseFullBagOrdering);

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeGetUserItemCompatCheck PASS tests=11 " +
                  "command=4 type1=0153/0055 online=player+hero " +
                  "player-order=equip/bag/storage response-body=208");
return 0;

Task CommandPermissionAndHelp()
{
    using var runtime = NewRuntime();
    using var db = NewDbService();
    var command = NewCommand(out var metadata);

    Equal("GetUserItem", metadata.Name, "command name");
    Equal("<PlayerName> <ItemID>", metadata.Help, "command attribute help");
    Equal((byte)4, metadata.nPermissionMin, "minimum permission");

    // The refusal is built from two image literals — 0x62B768 (len 10,
    // B8 C3 C3 FC C1 EE D0 E8 D2 AA = "该命令需要") and 0x62B77C (len 12,
    // BC B6 47 4D B2 C5 C4 DC CA B9 D3 C3 = "级GM才能使用") — with IntToStr(N)
    // spliced between them. M2Share.g_sGameCommandPermissionTooLow ("权限不够!!!")
    // is 0 hits in the whole image, so it cannot be what native replies here.
    var denied = NewPlayer("Denied", permission: 3);
    Equal("该命令需要4级GM才能使用",
        command.Handle("Target 7", denied), "permission 3 result");
    Equal(0, db.Value.PendingNativeSendCount,
        "permission 3 queued native request");

    var allowed = NewPlayer("Allowed", permission: 4);
    Equal(string.Empty, command.Handle(string.Empty, allowed) ?? string.Empty,
        "permission 4 no-argument result");
    AssertSystemMessage(allowed, "@GetUserItem <PlayerName> <ItemID>",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor, "native help");
    Equal(0, db.Value.PendingNativeSendCount,
        "help queued native request");
    return Task.CompletedTask;
}

Task RequestFieldsAndInvalidId()
{
    using var runtime = NewRuntime();
    using var db = NewDbService();
    var command = NewCommand(out _);
    var requester = NewPlayer("请求者ABCDE甲乙丙丁", permission: 4);
    requester.m_sUserID = "账户ABCDEFGHIJKLMNO甲乙";
    requester.m_sLoginAccount = "LOGIN-MUST-NOT-BE-USED";
    const string targetName = "目标角色ABCDE甲乙丙丁";

    command.Handle(targetName + " not-a-number", requester);
    Equal(1, db.Value.PendingNativeSendCount, "0153 queue count");
    var frame = DecodeOnlyPendingFrame(db.Value);
    Equal(1, frame.Type, "0153 outer type");
    Equal(NativeItemExtractionProtocol.HeaderSize, frame.Payload.Length,
        "0153 header size");
    Assert(NativeItemExtractionProtocol.TryDecode(frame, out var request,
        out var error), "0153 decode: " + error);
    Equal(0, request.MakeIndex, "invalid ItemID default");
    Bytes(TruncateGbk(requester.m_sUserID, 20), request.Account,
        "0153 account uses m_sUserID");
    Assert(!request.Account.AsSpan().SequenceEqual(
            TruncateGbk(requester.m_sLoginAccount, 20)),
        "0153 account used m_sLoginAccount");
    Bytes(TruncateGbk(requester.m_sCharName, 15), request.RequesterName,
        "0153 requester ShortString");
    Bytes(TruncateGbk(targetName, 15), request.TargetName,
        "0153 target ShortString");
    Equal((byte)20, frame.Payload[0x10], "0153 account length byte");
    Equal((byte)15, frame.Payload[0x25], "0153 requester length byte");
    Equal((byte)15, frame.Payload[0x35], "0153 target length byte");
    return Task.CompletedTask;
}

Task RequestUnsignedItemId()
{
    using var runtime = NewRuntime();
    using var db = NewDbService();
    var requester = NewPlayer("UnsignedRequester", permission: 4);

    NewCommand(out _).Handle("UnsignedTarget 4294967286", requester);
    Equal(1, db.Value.PendingNativeSendCount,
        "uint32 ItemID queue count");
    var frame = DecodeOnlyPendingFrame(db.Value);
    Assert(NativeItemExtractionProtocol.TryDecode(frame, out var request,
        out var error), "uint32 ItemID decode: " + error);
    Equal(unchecked((int)0xFFFFFFF6u), request.MakeIndex,
        "uint32 ItemID int32 bit pattern");
    Equal(0xFFFFFFF6u,
        BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4, 4)),
        "uint32 ItemID wire bits");
    return Task.CompletedTask;
}

Task BagCapacityGate()
{
    using var runtime = NewRuntime();
    using var db = NewDbService();
    var requester = NewPlayer("FullAtCommand", permission: 4);
    FillBag(requester);

    NewCommand(out _).Handle("OfflineTarget 12", requester);
    Equal(0, db.Value.PendingNativeSendCount,
        "full command bag queued 0153");
    Equal(Grobal2.MAXBAGITEM, requester.m_ItemList.Count,
        "command bag count");
    AssertSystemMessage(requester, "请确认有足够的包裹空位。",
        M2Share.g_Config.btRedMsgFColor,
        M2Share.g_Config.btRedMsgBColor, "command bag gate");
    return Task.CompletedTask;
}

Task OnlinePlayerOrderAndFallback()
{
    using var runtime = NewRuntime();
    using var db = NewDbService();
    var command = NewCommand(out _);
    var requester = NewPlayer("Collector", permission: 4);
    var target = NewPlayer("OnlineOwner");
    AddOnline(target);

    const int makeIndex = 73001;
    var equipped = Item(makeIndex);
    var bagged = Item(makeIndex);
    var stored = Item(makeIndex);
    target.m_UseItems[3] = equipped;
    target.m_ItemList.Add(bagged);
    target.m_StorageItemList.Add(stored);

    command.Handle(target.m_sCharName + " " + makeIndex, requester);
    Assert(target.m_UseItems[3] == null, "equipment was not searched first");
    Assert(target.m_ItemList.Contains(bagged), "bag was searched before equipment");
    Assert(target.m_StorageItemList.Contains(stored),
        "storage was searched before equipment");
    Same(equipped, requester.m_ItemList[^1], "equipped transfer");
    AssertMessageCount(target, Grobal2.RM_ABILITY, 1,
        "player equipment ability refresh");
    AssertMessageCount(target, Grobal2.RM_WEIGHTCHANGED, 0,
        "player equipment weight refresh");
    target.m_MsgList.Clear();

    command.Handle(target.m_sCharName + " " + makeIndex, requester);
    Assert(!target.m_ItemList.Contains(bagged), "bag item was not removed second");
    Assert(target.m_StorageItemList.Contains(stored),
        "storage was searched before bag");
    Same(bagged, requester.m_ItemList[^1], "bag transfer");
    AssertMessageCount(target, Grobal2.RM_ABILITY, 0,
        "player bag ability refresh");
    AssertMessageCount(target, Grobal2.RM_WEIGHTCHANGED, 0,
        "player bag weight refresh");

    command.Handle(target.m_sCharName + " " + makeIndex, requester);
    Assert(!target.m_StorageItemList.Contains(stored),
        "storage item was not removed third");
    Same(stored, requester.m_ItemList[^1], "storage transfer");
    AssertMessageCount(target, Grobal2.RM_ABILITY, 0,
        "player storage ability refresh");
    AssertMessageCount(target, Grobal2.RM_WEIGHTCHANGED, 0,
        "player storage weight refresh");
    Equal(0, db.Value.PendingNativeSendCount,
        "online transfer queued DB request");

    var zeroIndexDefinition = Item(73002, itemIndex: 0);
    target.m_ItemList.Add(zeroIndexDefinition);
    command.Handle(target.m_sCharName + " 73002", requester);
    Assert(!target.m_ItemList.Contains(zeroIndexDefinition),
        "MakeIndex match incorrectly required wIndex != 0");
    Same(zeroIndexDefinition, requester.m_ItemList[^1],
        "wIndex-zero MakeIndex transfer");

    command.Handle(target.m_sCharName + " " + makeIndex, requester);
    Equal(1, db.Value.PendingNativeSendCount,
        "online miss did not fall back to 0153");
    var success = requester.m_MsgList.Last(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE &&
        message.Buff == "成功得到OnlineOwner的身上物品审计物品(73001)");
    AssertColor(success, M2Share.g_Config.btRedMsgFColor,
        M2Share.g_Config.btRedMsgBColor, "online success");
    Equal(4, M2Share.LogStringList.Count,
        "online extraction log count");
    return Task.CompletedTask;
}

Task OnlineHeroAndFallback()
{
    using var runtime = NewRuntime();
    using var db = NewDbService();
    var requester = NewPlayer("HeroCollector", permission: 4);
    var target = NewPlayer("HeroOwner");
    var hero = new HeroObject
    {
        m_sCharName = "HeroActor",
        MasterName = target.m_sCharName,
        m_Master = target,
        m_boOffLineFlag = true
    };
    target.m_HeroObject = hero;
    AddOnline(target);
    var heroEquipped = Item(74001);
    var heroBagged = Item(74001);
    hero.m_UseItems[3] = heroEquipped;
    hero.m_ItemList.Add(heroBagged);

    var command = NewCommand(out _);
    command.Handle(target.m_sCharName + " 74001", requester);
    Assert(hero.m_UseItems[3] == null,
        "hero equipment was not searched first");
    Assert(hero.m_ItemList.Contains(heroBagged),
        "hero bag was searched before equipment");
    Same(heroEquipped, requester.m_ItemList[^1], "hero equipment transfer");
    AssertMessageCount(hero, Grobal2.RM_ABILITY, 1,
        "hero equipment ability refresh");
    AssertMessageCount(hero, Grobal2.RM_WEIGHTCHANGED, 0,
        "hero equipment weight refresh");
    hero.m_MsgList.Clear();

    command.Handle(target.m_sCharName + " 74001", requester);
    Assert(!hero.m_ItemList.Contains(heroBagged), "hero bag item was not removed");
    Same(heroBagged, requester.m_ItemList[^1], "hero bag transfer");
    AssertMessageCount(hero, Grobal2.RM_ABILITY, 0,
        "hero bag ability refresh");
    AssertMessageCount(hero, Grobal2.RM_WEIGHTCHANGED, 0,
        "hero bag weight refresh");
    Equal(0, db.Value.PendingNativeSendCount,
        "hero transfer queued DB request");

    command.Handle(target.m_sCharName + " 74001", requester);
    Equal(1, db.Value.PendingNativeSendCount,
        "hero miss did not fall back to 0153");

    var ghostItem = Item(74002);
    hero.m_ItemList.Add(ghostItem);
    hero.m_boGhost = true;
    command.Handle(target.m_sCharName + " 74002", requester);
    Assert(hero.m_ItemList.Contains(ghostItem), "ghost hero item was extracted");
    Equal(2, db.Value.PendingNativeSendCount,
        "ghost hero did not fall back to 0153");
    return Task.CompletedTask;
}

Task EquipmentFeatureSlotsAndNoWeightRefresh()
{
    var extractionType = typeof(DBService).Assembly.GetType(
        "GameSvr.Services.NativeOnlineItemExtraction")
        ?? throw new TypeLoadException(
            "GameSvr.Services.NativeOnlineItemExtraction");
    var method = extractionType.GetMethod(
        "AffectsFeature", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            extractionType.FullName, "AffectsFeature");
    var expected = new HashSet<int>
    {
        Grobal2.U_DRESS,
        Grobal2.U_WEAPON,
        Grobal2.U_HELMET,
        Grobal2.U_MASK
    };
    for (var slot = 0; slot < Grobal2.HUMAN_EQUIPPED_ITEM_COUNT; slot++)
    {
        var actual = (bool)(method.Invoke(null, new object[] { slot })
            ?? throw new InvalidOperationException(
                "AffectsFeature returned null"));
        Equal(expected.Contains(slot), actual,
            "equipment feature slot " + slot);
    }
    Sequence(new[] { 0, 1, 4, 13 }, expected.OrderBy(value => value).ToArray(),
        "native feature slots");
    return Task.CompletedTask;
}

async Task ResponseSuccessAndAddItemPacket()
{
    using var runtime = NewRuntime(stdMode: 7, itemWeight: 9);
    var requester = NewPlayer("ResponseUser", ready: true, offline: false);
    requester.m_sMapName = "AuditMap";
    requester.m_nCurrX = 12;
    requester.m_nCurrY = 34;
    AddOnline(requester);
    await using var gate = await GateCapture.Create();
    gate.Attach(requester);

    var record = NativeRecord(unchecked((int)0x89ABCDEF), 1, dura: 23,
        duraMax: 80);
    record[0x40] = 0xA5;
    record[0xC7] = 0x5A;
    DispatchResponse(Response(requester.m_sCharName, "OfflineOwner",
        NativeItemExtractionProtocol.Success, record));

    Equal(1, requester.m_ItemList.Count, "0055 success bag count");
    var item = requester.m_ItemList[0];
    Bytes(record, item.NativeRecord, "0055 opaque native record");
    Equal(9, requester.m_WAbil.Weight, "0055 weight refresh");
    AssertSystemMessage(requester,
        "成功收取 OfflineOwner 的 审计物品(2309737967)",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor, "0055 success");

    Equal(1, M2Share.LogStringList.Count, "0055 success log count");
    var fields = ((string)M2Share.LogStringList[0]).Split('\t');
    Sequence(new[]
    {
        "8", "AuditMap", "12", "34", "ResponseUser", "审计物品",
        "2309737967", "23", "OfflineOwner"
    }, fields, "0055 success log");

    var packet = await gate.Read();
    Equal((ushort)Grobal2.GM_DATA, packet.Cmd, "gate command");
    Assert(packet.Payload.Length >= ClientPacket.PackSize + 16,
        "SM_ADDITEM payload is too short");
    var client = Packets.ToPacket<ClientPacket>(
        packet.Payload.AsSpan(0, ClientPacket.PackSize).ToArray());
    Assert(client != null, "SM_ADDITEM client header decode");
    Equal((ushort)Grobal2.SM_ADDITEM, client.Ident, "SM_ADDITEM ident");
    Equal(requester.ObjectId, client.Recog, "SM_ADDITEM owner recog");
    Equal((ushort)1, client.Series, "SM_ADDITEM series");
    Equal((ushort)1, BitConverter.ToUInt16(packet.Payload,
            ClientPacket.PackSize + 4),
        "SM_ADDITEM item index");
    Equal((ushort)23, BitConverter.ToUInt16(packet.Payload,
            ClientPacket.PackSize + 6),
        "SM_ADDITEM dura");
}

Task ResponseFailureShapes()
{
    using var runtime = NewRuntime();
    var requester = NewPlayer("FailureUser", ready: true);
    AddOnline(requester);

    DispatchResponse(Response(requester.m_sCharName, "NoItemOwner",
        NativeItemExtractionProtocol.ItemNotFound));
    AssertOnlyFailure(requester, "NoItemOwner");

    requester.m_MsgList.Clear();
    var valid = Response(requester.m_sCharName, "ShortOwner",
        NativeItemExtractionProtocol.Success, NativeRecord(75001));
    DispatchResponse(new LegacyDbServerFrame(1, 0,
        valid.Payload.AsSpan(0,
            NativeItemExtractionProtocol.HeaderSize + 207).ToArray()));
    AssertOnlyFailure(requester, "ShortOwner");

    requester.m_MsgList.Clear();
    valid = Response(requester.m_sCharName, "LongOwner",
        NativeItemExtractionProtocol.Success, NativeRecord(75002));
    var oversized = new byte[valid.Payload.Length + 1];
    valid.Payload.CopyTo(oversized, 0);
    oversized[^1] = 0xCC;
    DispatchResponse(new LegacyDbServerFrame(1, 0, oversized));
    AssertOnlyFailure(requester, "LongOwner");

    Equal(0, requester.m_ItemList.Count, "failed 0055 changed bag");
    Equal(0, M2Share.LogStringList.Count, "failed 0055 emitted success log");
    return Task.CompletedTask;
}

Task ResponseSilentCases()
{
    using var runtime = NewRuntime();
    var record = NativeRecord(76001);

    DispatchResponse(Response("MissingUser", "Owner",
        NativeItemExtractionProtocol.Success, record));
    Equal(0, M2Share.LogStringList.Count, "missing requester logged");

    var requester = NewPlayer("SilentUser", ready: true);
    AddOnline(requester);
    requester.m_boGhost = true;
    DispatchResponse(Response(requester.m_sCharName, "Owner",
        NativeItemExtractionProtocol.Success, record));
    Equal(0, requester.m_MsgList.Count, "ghost requester got response");
    requester.m_boGhost = false;

    requester.m_boReadyRun = false;
    DispatchResponse(Response(requester.m_sCharName, "Owner",
        NativeItemExtractionProtocol.Success, record));
    Equal(0, requester.m_MsgList.Count, "not-ready requester got response");
    requester.m_boReadyRun = true;

    var noDefinition = NativeRecord(76002, itemIndex: 0);
    DispatchResponse(Response(requester.m_sCharName, "Owner",
        NativeItemExtractionProtocol.Success, noDefinition));
    Equal(0, requester.m_MsgList.Count, "decoder-null item emitted message");
    Equal(0, requester.m_ItemList.Count, "decoder-null item changed bag");

    DispatchResponse(Response(requester.m_sCharName, "Owner",
        NativeItemExtractionProtocol.Success, NativeRecord(0)));
    Equal(0, requester.m_MsgList.Count, "MakeIndex zero emitted message");
    Equal(0, requester.m_ItemList.Count, "MakeIndex zero changed bag");
    Equal(0, M2Share.LogStringList.Count, "silent cases emitted log");
    return Task.CompletedTask;
}

async Task ResponseFullBagOrdering()
{
    using var runtime = NewRuntime();
    var requester = NewPlayer("FullResponse", ready: true, offline: false);
    AddOnline(requester);
    FillBag(requester);
    await using var gate = await GateCapture.Create();
    gate.Attach(requester);

    DispatchResponse(Response(requester.m_sCharName, "FullBagOwner",
        NativeItemExtractionProtocol.Success, NativeRecord(77001)));
    Equal(Grobal2.MAXBAGITEM, requester.m_ItemList.Count,
        "full 0055 changed bag");
    AssertSystemMessage(requester,
        "成功收取 FullBagOwner 的 审计物品(77001)",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor, "full-bag success ordering");
    Equal(1, M2Share.LogStringList.Count,
        "full-bag success log ordering");
    await Task.Delay(100);
    Equal(0, gate.Available, "full-bag emitted SM_ADDITEM");
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

static RuntimeScope NewRuntime(byte stdMode = 1, int itemWeight = 1)
{
    M2Share.g_Config = new GameSvrConfig { nCheckBlock = 0 };
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
        Weight = checked((byte)itemWeight),
        DuraMax = 100
    });
    M2Share.UserEngine = engine;
    return new RuntimeScope();
}

static DbScope NewDbService()
{
    var service = new DBService();
    M2Share.DataServer = service;
    return new DbScope(service);
}

static BaseCommond NewCommand(out GameCommandAttribute metadata)
{
    var type = typeof(GetUserItemCommand);
    metadata = type.GetCustomAttribute<GameCommandAttribute>()
               ?? throw new InvalidOperationException(
                   "GetUserItem command attribute is missing");
    var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                 BindingFlags.NonPublic)
        .SingleOrDefault(candidate => candidate.DeclaringType == type &&
            candidate.GetCustomAttribute<DefaultCommand>() != null)
        ?? throw new InvalidOperationException(
            "GetUserItem default command method is missing");
    var command = (BaseCommond)(Activator.CreateInstance(type)
        ?? throw new InvalidOperationException("GetUserItem activation failed"));
    command.Register(metadata, method);
    return command;
}

static TPlayObject NewPlayer(string name, byte permission = 0,
    bool ready = false, bool offline = true) => new()
{
    m_sCharName = name,
    m_sUserID = "UserId-" + name,
    m_sLoginAccount = "Login-" + name,
    m_btPermission = permission,
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
        throw new InvalidOperationException("unexpected online-player list type");
    players.Add(player);
}

static TUserItem Item(int makeIndex, ushort itemIndex = 1) => new()
{
    MakeIndex = makeIndex,
    wIndex = itemIndex,
    Dura = 10,
    DuraMax = 100
};

static void FillBag(TPlayObject player)
{
    while (player.m_ItemList.Count < Grobal2.MAXBAGITEM)
        player.m_ItemList.Add(Item(80000 + player.m_ItemList.Count));
}

static LegacyDbServerFrame DecodeOnlyPendingFrame(DBService service)
{
    var field = typeof(DBService).GetField("_pendingSends",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(DBService).FullName,
            "_pendingSends");
    if (field.GetValue(service) is not IEnumerable<byte[]> queue)
        throw new InvalidOperationException("unexpected DB native-send queue type");
    var wire = queue.Single();
    Assert(LegacyDbServerFrameCodec.TryDecode(wire, out var frame,
        out var error), "queued DB frame decode: " + error);
    return frame;
}

static LegacyDbServerFrame Response(string requesterName, string targetName,
    ushort status, byte[] record = null)
{
    var requestFrame = NativeItemExtractionProtocol.CreateRequest(
        record == null || record.Length < 4
            ? 0 : BinaryPrimitives.ReadInt32LittleEndian(record),
        Encoding.GetEncoding(936).GetBytes("AuditAccount"),
        Encoding.GetEncoding(936).GetBytes(requesterName),
        Encoding.GetEncoding(936).GetBytes(targetName));
    Assert(NativeItemExtractionProtocol.TryDecode(requestFrame,
        out var request, out var error), "response request seed: " + error);
    return NativeItemExtractionProtocol.CreateResponse(request, status, record);
}

static byte[] NativeRecord(int makeIndex, ushort itemIndex = 1,
    ushort dura = 10, ushort duraMax = 100)
{
    var record = new byte[NativeItemExtractionProtocol.ItemSize];
    BinaryPrimitives.WriteInt32LittleEndian(record, makeIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), itemIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), dura);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8), duraMax);
    return record;
}

static void DispatchResponse(LegacyDbServerFrame frame)
{
    var method = typeof(DBService).GetMethod("ProcessNativeType1",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(DBService).FullName,
            "ProcessNativeType1");
    try
    {
        method.Invoke(null, new object[] { frame });
    }
    catch (TargetInvocationException exception)
        when (exception.InnerException != null)
    {
        throw exception.InnerException;
    }
}

static void AssertOnlyFailure(TPlayObject requester, string targetName)
{
    var messages = requester.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE).ToArray();
    Equal(1, messages.Length, targetName + " failure message count");
    Equal(targetName + " 身上没有该物品。", messages[0].Buff,
        targetName + " failure text");
    AssertColor(messages[0], M2Share.g_Config.btRedMsgFColor,
        M2Share.g_Config.btRedMsgBColor, targetName + " failure");
}

static void AssertSystemMessage(TPlayObject player, string text,
    byte foreground, byte background, string label)
{
    var messages = player.m_MsgList.Where(candidate =>
        candidate.wIdent == Grobal2.RM_SYSMESSAGE && candidate.Buff == text)
        .ToArray();
    if (messages.Length == 0)
        throw new InvalidOperationException(label + " message is missing");
    var message = messages[^1];
    AssertColor(message, foreground, background, label);
}

static void AssertMessageCount(TBaseObject owner, int ident, int expected,
    string label)
{
    var actual = owner.m_MsgList.Count(message => message.wIdent == ident);
    Equal(expected, actual, label);
}

static void AssertColor(SendMessage message, byte foreground,
    byte background, string label)
{
    Equal(Grobal2.RM_SYSMESSAGE, message.wIdent, label + " ident");
    Equal(0, message.wParam, label + " wParam");
    Equal((int)foreground, message.nParam1, label + " foreground");
    Equal((int)background, message.nParam2, label + " background");
    Equal(0, message.nParam3, label + " nParam3");
}

static byte[] TruncateGbk(string value, int capacity) =>
    Encoding.GetEncoding(936).GetBytes(value).Take(capacity).ToArray();

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

static void Sequence<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string label)
{
    Assert(expected.SequenceEqual(actual),
        $"{label}: expected=[{string.Join(',', expected)}], " +
        $"actual=[{string.Join(',', actual)}]");
}

static void Bytes(byte[] expected, byte[] actual, string label)
{
    Assert(actual != null && expected.AsSpan().SequenceEqual(actual),
        $"{label}: expected={Convert.ToHexString(expected)}, " +
        $"actual={(actual == null ? "<null>" : Convert.ToHexString(actual))}");
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

sealed class RuntimeScope : IDisposable
{
    public void Dispose()
    {
        M2Share.DataServer = null;
        M2Share.UserEngine = null;
        M2Share.LogStringList = null;
    }
}

sealed class DbScope : IDisposable
{
    public DbScope(DBService value) => Value = value;
    public DBService Value { get; }

    public void Dispose()
    {
        if (ReferenceEquals(M2Share.DataServer, Value)) M2Share.DataServer = null;
        Value.Dispose();
    }
}

sealed class GateCapture : IAsyncDisposable
{
    private static int _nextGateIndex = 30000;
    private readonly TcpListener _listener;
    private readonly Socket _peer;
    private readonly Socket _serviceSocket;
    private readonly GateService _service;
    private readonly ConcurrentDictionary<int, GateService> _services;
    private readonly int _gateIndex;

    private GateCapture(TcpListener listener, Socket peer, Socket serviceSocket,
        GateService service, ConcurrentDictionary<int, GateService> services,
        int gateIndex)
    {
        _listener = listener;
        _peer = peer;
        _serviceSocket = serviceSocket;
        _service = service;
        _services = services;
        _gateIndex = gateIndex;
    }

    public int Available => _peer.Available;

    public static async Task<GateCapture> Create()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptSocketAsync();
        var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream,
            ProtocolType.Tcp) { NoDelay = true };
        await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        var serviceSocket = await accept.WaitAsync(TimeSpan.FromSeconds(3));
        serviceSocket.NoDelay = true;

        var gateIndex = Interlocked.Increment(ref _nextGateIndex);
        var info = new TGateInfo
        {
            boUsed = true,
            Socket = serviceSocket,
            SocketId = gateIndex,
            UserList = new List<TGateUserInfo>()
        };
        var service = new GateService(gateIndex, info);
        service.StartQueueService();
        var manager = GateManager.Instance;
        var field = typeof(GateManager).GetField("_gateDataService",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(GateManager).FullName,
                "_gateDataService");
        var services = field.GetValue(manager)
                       as ConcurrentDictionary<int, GateService>
                       ?? throw new InvalidOperationException(
                           "unexpected GateManager service registry type");
        Require(services.TryAdd(gateIndex, service),
            "could not register audit gate");
        M2Share.GateManager = manager;
        return new GateCapture(listener, peer, serviceSocket, service,
            services, gateIndex);
    }

    public void Attach(TPlayObject player)
    {
        player.m_nGateIdx = _gateIndex;
        player.m_nSocket = 0x153;
        player.m_nGSocketIdx = 1;
        player.m_boOffLineFlag = false;
    }

    public async Task<InternalPacket77> Read()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var header = new byte[InternalPacket77.HEADER_SIZE];
        await ReadExactly(header, timeout.Token);
        // 16-byte transport header: +0x0C is Cmd (0x637AC7 `66 89 78 0C`
        // mov [eax+0xC],di) and +0x0E is BodyLen (0x637AD7 `66 89 58 0E`
        // mov [eax+0xE],bx); the sender advances by 0x10 at 0x637ADE. Total frame
        // length is therefore 0x10 + word[+0x0E], not word[+0x0C].
        var bodyLength = BitConverter.ToUInt16(header, 14);
        var frameLength = InternalPacket77.HEADER_SIZE + bodyLength;
        Require(frameLength <= InternalPacket77.MAX_FRAME_SIZE,
            "invalid gate frame length " + frameLength);
        var bytes = new byte[frameLength];
        header.CopyTo(bytes, 0);
        if (frameLength > header.Length)
        {
            var body = bytes.AsMemory(header.Length,
                frameLength - header.Length);
            await ReadExactly(body, timeout.Token);
        }
        return InternalPacket77.FromBytes(bytes, 0, bytes.Length)
               ?? throw new InvalidOperationException("gate frame decode failed");
    }

    private Task ReadExactly(byte[] buffer, CancellationToken token) =>
        ReadExactly(buffer.AsMemory(), token);

    private async Task ReadExactly(Memory<byte> buffer,
        CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await _peer.ReceiveAsync(buffer[offset..],
                SocketFlags.None, token);
            if (count <= 0) throw new EndOfStreamException("gate socket EOF");
            offset += count;
        }
    }

    public ValueTask DisposeAsync()
    {
        _services.TryRemove(_gateIndex, out _);
        _service.Stop();
        _peer.Dispose();
        _serviceSocket.Dispose();
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
