using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();

CheckHeaders();
CheckShortStringsAndRecords();
CheckPendingRequestRules();
CheckGroupStateTransitions();
CheckMalformedQueryBounds();
CheckCrossProtocolSourceGuards();

Console.WriteLine(
    "PASS NativeGroupProtocol ids=4412-4416 records=36/54 pending=exact group=create+join");
return;

static void CheckHeaders()
{
    var header = InvokeStatic<ClientPacket>("BuildNativeGroupHeader",
        4414, -4, 72, 0, 2);
    Require(header.Ident == 4414, "4414 header ident");
    Require(header.Recog == -4, "4414 header recog");
    Require(header.Param == 72, "4414 header param");
    Require(header.Tag == 0, "4414 header tag");
    Require(header.Series == 2, "4414 header series");

    var raw = header.GetBuffer();
    Require(raw.Length == ClientPacket.PackSize, "client header size");
    Require(BinaryPrimitives.ReadInt32LittleEndian(raw) == -4,
        "client header recog offset");
    Require(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(4)) == 4414,
        "client header ident offset");
    Require(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(6)) == 72,
        "client header param offset");
    Require(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(8)) == 0,
        "client header tag offset");
    Require(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(10)) == 2,
        "client header series offset");

    var members = InvokeStatic<ClientPacket>("BuildNativeGroupHeader",
        Grobal2.SM_GROUPMEMBERS, 2, 108, 0, 0);
    Require(members.Ident == 667 && members.Recog == 2
        && members.Param == 108 && members.Series == 0,
        "4416/667 snapshot header");
}

static void CheckShortStringsAndRecords()
{
    var player = NewPlayer("角色甲");
    player.m_Abil.Level = 52;
    player.m_btGender = PlayGender.WoMan;
    player.m_btJob = 2;
    player.m_sMapName = "盟重省";

    var record = InvokeStatic<byte[]>("BuildNativeGroupPlayerRecord",
        player, "行会甲");
    Require(record.Length == 36, "4414 player record size");
    Require(ReadShortString(record, 0, 15) == "角色甲",
        "4414 player name");
    Require(BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(16)) == 52,
        "4414 player level");
    Require(record[18] == 1, "4414 player gender");
    Require(record[19] == 2, "4414 player job");
    Require(ReadShortString(record, 20, 15) == "行会甲",
        "4414 player guild");

    var group = InvokeStatic<byte[]>("BuildNativeNearbyGroupRecord",
        player, 11, "行会乙");
    Require(group.Length == 36, "4415 group record size");
    Require(ReadShortString(group, 0, 15) == "角色甲",
        "4415 leader name");
    Require(BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(16)) == 52,
        "4415 leader level");
    Require(group[18] == 11, "4415 member count");
    Require(ReadShortString(group, 19, 15) == "行会乙",
        "4415 guild");
    Require(group[35] == 0, "4415 trailing padding");

    var member = NewPlayer("成员乙");
    member.m_Abil.Level = 41;
    member.m_btGender = PlayGender.Man;
    member.m_sMapName = "苍月岛";
    var memberRecord = InvokeStatic<byte[]>("BuildNativeGroupMemberRecord",
        member, player);
    Require(memberRecord.Length == 54, "667 member record size");
    Require(ReadShortString(memberRecord, 0, 15) == "成员乙",
        "667 member name");
    Require(BinaryPrimitives.ReadUInt16LittleEndian(
        memberRecord.AsSpan(16)) == 41, "667 member level");
    Require(memberRecord[18] == 0 && memberRecord[19] == 1,
        "667 gender/online flags");
    Require(ReadShortString(memberRecord, 20, 31) == "苍月岛",
        "667 map short string");
    Require(memberRecord[52] == 0 && memberRecord[53] == 0,
        "667 member leader/padding flags");

    player.m_GroupOwner = player;
    player.m_GroupMembers.Add(player);
    player.m_GroupMembers.Add(member);
    member.m_GroupOwner = player;
    var body = InvokeStatic<byte[]>("BuildNativeGroupMembersBody", player);
    Require(body.Length == 108, "667 two-member body size");
    Require(body[52] == 1, "667 leader flag");
    Require(body[54 + 52] == 0, "667 non-leader flag");

    var longName = NewPlayer("12345678901234中");
    var truncated = InvokeStatic<byte[]>("BuildNativeGroupPlayerRecord",
        longName, string.Empty);
    Require(ReadShortString(truncated, 0, 15) == "12345678901234",
        "GBK truncation split a multibyte character");

    var encoded = new byte[16];
    var gbk = HUtil32.GbkEncoding.GetBytes("甲A");
    encoded[0] = (byte)gbk.Length;
    gbk.CopyTo(encoded, 1);
    Require(TryReadProtocolShortString(encoded, 0, 15, out var decoded)
        && decoded == "甲A", "request ShortString decode");
    encoded[0] = 16;
    Require(!TryReadProtocolShortString(encoded, 0, 15, out _),
        "oversized ShortString accepted");
    Require(!TryReadProtocolShortString(new byte[15], 0, 15, out _),
        "truncated ShortString record accepted");
}

static void CheckGroupStateTransitions()
{
    var inviter = RegisterPlayer(NewPlayer("invite-source"));
    var accepter = RegisterPlayer(NewPlayer("invite-target"));

    var staleInviter = RegisterPlayer(NewPlayer("stale-invite"));
    var staleJoiner = RegisterPlayer(NewPlayer("stale-join"));
    QueueRequest(accepter, staleInviter, 0);
    QueueRequest(accepter, staleJoiner, 1);

    QueueRequest(accepter, inviter, 0);
    accepter.Operate(Message(4412, recog: 1, param: 0,
        inviter.m_sCharName));

    Require(ReferenceEquals(inviter.m_GroupOwner, inviter),
        "type-0 accepter did not create requester-led group");
    Require(ReferenceEquals(accepter.m_GroupOwner, inviter),
        "type-0 accepter owner");
    Require(inviter.m_GroupMembers.Count == 2
        && ReferenceEquals(inviter.m_GroupMembers[0], inviter)
        && ReferenceEquals(inviter.m_GroupMembers[1], accepter),
        "type-0 group member order");
    Require(inviter.m_boAllowGroup, "new leader allow-group flag");
    Require(HasPendingRequest(accepter, staleInviter, 0)
        && HasPendingRequest(accepter, staleJoiner, 1),
        "group-request UI dismissal changed native pending state");

    var mismatch = RegisterPlayer(NewPlayer("mismatch"));
    QueueRequest(inviter, mismatch, 1);
    inviter.Operate(Message(4412, recog: 1, param: 0,
        mismatch.m_sCharName));
    Require(mismatch.m_GroupOwner == null,
        "mismatched pending request changed group state");

    var joiner = RegisterPlayer(NewPlayer("joiner"));
    joiner.Operate(Message(4413, recog: 0, param: 0,
        accepter.m_sCharName));
    inviter.Operate(Message(4412, recog: 1, param: 1,
        joiner.m_sCharName));

    Require(ReferenceEquals(joiner.m_GroupOwner, inviter),
        "4413/type-1 join owner");
    Require(inviter.m_GroupMembers.Count == 3
        && inviter.m_GroupMembers.Contains(joiner),
        "4413/type-1 join member list");

    var rejected = RegisterPlayer(NewPlayer("rejected"));
    QueueRequest(inviter, rejected, 1);
    inviter.Operate(Message(4412, recog: 0, param: 1,
        rejected.m_sCharName));
    inviter.Operate(Message(4412, recog: 1, param: 1,
        rejected.m_sCharName));
    Require(rejected.m_GroupOwner == null,
        "rejected request remained pending");
}

static void CheckPendingRequestRules()
{
    var recipient = NewPlayer("pending-recipient");
    var sameRequester = NewPlayer("pending-same");
    QueueRequest(recipient, sameRequester, 0);
    QueueRequest(recipient, sameRequester, 1);
    Require(!TryQueueRequest(recipient, sameRequester, 0),
        "exact duplicate pending request accepted");
    Require(HasPendingRequest(recipient, sameRequester, 0)
        && HasPendingRequest(recipient, sameRequester, 1),
        "pending requests were not keyed by requester and type");

    for (var i = 2; i < 10; i++)
        QueueRequest(recipient, NewPlayer("pending-" + i), 0);
    Require(!TryQueueRequest(recipient, NewPlayer("pending-overflow"), 0),
        "more than ten pending requests accepted");

    var invalidReplyRecipient = RegisterPlayer(
        NewPlayer("invalid-reply-recipient"));
    var invalidReplyRequester = RegisterPlayer(
        NewPlayer("invalid-reply-requester"));
    QueueRequest(invalidReplyRecipient, invalidReplyRequester, 0);
    invalidReplyRecipient.Operate(Message(4412, recog: 2, param: 0,
        invalidReplyRequester.m_sCharName));
    Require(HasPendingRequest(invalidReplyRecipient,
            invalidReplyRequester, 0)
        && invalidReplyRecipient.m_GroupOwner == null
        && invalidReplyRequester.m_GroupOwner == null,
        "4412 accepted a Recog value outside 0/1");
}

static void CheckMalformedQueryBounds()
{
    var query = RegisterPlayer(NewPlayer("query"));
    query.m_PEnvir = new Envirnoment { sMapName = "audit-map" };
    var malformed = new byte[17];
    malformed[0] = 16;
    malformed[16] = 15;
    query.Operate(new TProcessMessage
    {
        wIdent = 4414,
        nParam2 = 3,
        // Encode the malformed record as the wire body would be (6-bit); the handler decodes it
        // back to the same 17 bytes and still bounds-checks the 16-byte records without crashing.
        Payload = EDcode.EncodeBuffer(malformed)
    });

    query.Operate(new TProcessMessage { wIdent = 4415 });
    query.Operate(new TProcessMessage { wIdent = 4416 });
}

static void CheckCrossProtocolSourceGuards()
{
    var root = FindRepositoryRoot();
    var groupSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.NativeGroupProtocol.cs"));
    var relationSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.NativeRelationProtocol.cs"));
    var legacySource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.Operate.cs"));

    Require(groupSource.Contains("requester.AcceptNativeFriend(this)",
        StringComparison.Ordinal), "4412 type-2 friend acceptance route");
    Require(relationSource.Contains(
        "target.QueueNativeGroupRequest(this, 2)", StringComparison.Ordinal),
        "4433 type-2 pending request route");
    Require(legacySource.Contains("private void ClientCreateGroup(",
        StringComparison.Ordinal)
        && legacySource.Contains("private void ClientAddGroupMember(",
            StringComparison.Ordinal),
        "legacy plain group handlers were removed");

    // Message CHANNELS. cx unpacks as FColor = cx & 0xFF, BColor = cx >> 8 (see
    // the playernotice bridge in PasApiBridge), so against GameSvrConfig defaults
    // 0xFFDB == 0xDB/0xFF == MsgColor.Green and 0x38FF == 0xFF/0x38 == MsgColor.Red.
    // The two sends in sub_6F39B4 use DIFFERENT channels and must not be conflated:
    //   0x6F3A20  mov cx,0x38FF  -> "未回复您的邀请..."  (Red)
    //   0x6F3A40  mov cx,0xFFDB  -> "对方正忙..."        (Green)
    // The busy line was ported as Red (wrong channel); fixed 2026-08-08. Assert the
    // derivation too, so the ident and the config pair cannot drift apart silently.
    Require((0xFFDB & 0xFF) == M2Share.g_Config.btGreenMsgFColor
        && ((0xFFDB >> 8) & 0xFF) == M2Share.g_Config.btGreenMsgBColor,
        "0xFFDB must unpack to the btGreenMsg* pair");
    Require((0x38FF & 0xFF) == M2Share.g_Config.btRedMsgFColor
        && ((0x38FF >> 8) & 0xFF) == M2Share.g_Config.btRedMsgBColor,
        "0x38FF must unpack to the btRedMsg* pair");
    Require(groupSource.Contains(
        "requester.SysMsg(\"对方正忙，请稍后再请求。\", MsgColor.Green",
        StringComparison.Ordinal),
        "busy refusal must use the 0xFFDB Green channel (@0x6F3A40), not Red");
    // Anchor on the MESSAGE, not on a bare "MsgColor.Red, MsgType.Hint);" — that
    // substring occurs elsewhere in the file, so it stayed green when the line was
    // flipped to Green (caught by staging/_cx_mut.py).
    Require(System.Text.RegularExpressions.Regex.IsMatch(groupSource,
        "未回复您的邀请，请十秒后再尝试。\"\\s*,\\s*MsgColor\\.Red"),
        "the 0x38FF no-reply line (@0x6F3A20) must stay on the Red channel");
}

static TProcessMessage Message(int ident, int recog, int param, string name)
{
    return new TProcessMessage
    {
        wIdent = ident,
        nParam1 = recog,
        nParam2 = param,
        // Production-faithful wire body: the client sends the name 6-bit-ENCODED and the handler
        // decodes via DecodeNativeSocialBody. Feeding raw GBK bytes here masked the decode bug.
        Payload = EDcode.EncodeBuffer(HUtil32.GbkEncoding.GetBytes(name))
    };
}

static TPlayObject NewPlayer(string name)
{
    return new TPlayObject
    {
        m_boOffLineFlag = true,
        m_boAllowGroup = true,
        m_sCharName = name,
        m_sMapName = "audit-map",
        m_nCurrX = 12,
        m_nCurrY = 34
    };
}

static TPlayObject RegisterPlayer(TPlayObject player)
{
    var field = typeof(UserEngine).GetField("m_PlayObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("UserEngine player list missing");
    var players = (IList<TPlayObject>)field.GetValue(M2Share.UserEngine);
    players.Add(player);
    return player;
}

static void QueueRequest(TPlayObject recipient, TPlayObject requester,
    byte type)
{
    var queued = TryQueueRequest(recipient, requester, type);
    Require(queued, $"failed to queue request type {type}");
}

static bool TryQueueRequest(TPlayObject recipient, TPlayObject requester,
    byte type)
{
    return InvokeInstance<bool>(recipient, "QueueNativeGroupRequest",
        requester, type);
}

static bool HasPendingRequest(TPlayObject recipient, TPlayObject requester,
    byte type)
{
    return InvokeInstance<bool>(recipient, "HasNativeGroupRequest",
        requester, type);
}

static bool TryReadProtocolShortString(byte[] body, int offset, int capacity,
    out string value)
{
    var arguments = new object[] { body, offset, capacity, null };
    var result = InvokeStatic<bool>("TryReadNativeGroupShortString",
        arguments);
    value = (string)arguments[3];
    return result;
}

static string ReadShortString(byte[] body, int offset, int capacity)
{
    var length = body[offset];
    Require(length <= capacity, "encoded ShortString length exceeds capacity");
    for (var i = offset + 1 + length; i < offset + capacity + 1; i++)
        Require(body[i] == 0, "encoded ShortString padding is nonzero");
    return HUtil32.GbkEncoding.GetString(body, offset + 1, length);
}

static T InvokeStatic<T>(string name, params object[] arguments)
{
    var method = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(name + " missing");
    return (T)method.Invoke(null, arguments);
}

static T InvokeInstance<T>(TPlayObject player, string name,
    params object[] arguments)
{
    var method = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(name + " missing");
    return (T)method.Invoke(player, arguments);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")))
                return directory.FullName;
        }
    }
    throw new InvalidOperationException("repository root not found");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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
