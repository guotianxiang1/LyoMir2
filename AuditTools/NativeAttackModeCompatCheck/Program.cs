using GameSvr;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();

Equal(545, Grobal2.CM_ATTACKMODE, "CM_ATTACKMODE ident");
CheckIngressTagMapping();
CheckAcceptedModes();
CheckRejectedModes();
CheckLowByteSemantics();
CheckDirectBehaviorMatrix();

Console.WriteLine(
    "NativeAttackModeCompatCheck PASS ident=545 modes=0..5 source=Tag/low-byte invalid=silent behavior=players/summons");

static void CheckIngressTagMapping()
{
    var player = NewPlayer();
    player.m_boOffLineFlag = false;
    M2Share.UserEngine.ProcessUserMessage(player, new ClientPacket
    {
        Ident = Grobal2.CM_ATTACKMODE,
        Param = 4,
        Tag = 2,
        Series = 5
    }, string.Empty);

    TProcessMessage queued = null;
    Assert(player.TryTake(ref queued), "545 ingress message was not queued");
    Equal(4, queued.nParam2, "ClientPacket.Param mapping");
    Equal(2, queued.nParam3, "ClientPacket.Tag mapping");

    player.m_boOffLineFlag = true;
    Assert(player.Operate(queued), "545 ingress dispatcher result");
    Equal((byte)2, player.m_btAttatckMode,
        "545 must use Tag instead of Param");
    Packet(player.m_DefMsg, 2, "545 ingress response");
}

static void CheckAcceptedModes()
{
    var player = NewPlayer();
    for (var mode = 0; mode < 6; mode++)
    {
        player.m_DefMsg = null;
        Assert(player.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_ATTACKMODE,
            nParam2 = 5,
            nParam3 = mode
        }), $"mode {mode} dispatcher result");
        Equal((byte)mode, player.m_btAttatckMode,
            $"mode {mode} state");
        Packet(player.m_DefMsg, mode, $"mode {mode} response");
        Equal(0, player.m_MsgList.Count,
            $"mode {mode} must not queue a system message");
    }
}

static void CheckRejectedModes()
{
    foreach (var requested in new[] { 6, 7, 255, -1 })
    {
        var player = NewPlayer();
        player.m_btAttatckMode = 3;
        player.m_DefMsg = null;

        Assert(player.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_ATTACKMODE,
            nParam3 = requested
        }), $"invalid {requested} dispatcher result");
        Equal((byte)3, player.m_btAttatckMode,
            $"invalid {requested} state");
        Assert(player.m_DefMsg == null,
            $"invalid {requested} response must be silent");
        Equal(0, player.m_MsgList.Count,
            $"invalid {requested} message side effect");
    }
}

static void CheckLowByteSemantics()
{
    var player = NewPlayer();
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_ATTACKMODE,
        nParam3 = 0x0105
    }), "high-byte Tag dispatcher result");
    Equal((byte)5, player.m_btAttatckMode, "Tag low-byte state");
    Packet(player.m_DefMsg, 5, "Tag low-byte response");
}

static void CheckDirectBehaviorMatrix()
{
    M2Share.CorpsService = CreateCombatService();
    var self = NewSocialPlayer(1, "self");
    var sameCorps = NewSocialPlayer(2, "same-corps");
    var sameGild = NewSocialPlayer(3, "same-gild");
    var unionGild = NewSocialPlayer(4, "union-gild");
    var hostileGild = NewSocialPlayer(5, "hostile-gild");
    var unrelated = NewSocialPlayer(6, "unrelated");
    var noCorps = NewSocialPlayer(7, "no-corps");

    CheckMode(self, unrelated, 0, true, true, "all");
    CheckMode(self, unrelated, 1, false, true, "peace");
    CheckMode(self, unrelated, 2, true, false, "group outsider");
    SetGroup(self, unrelated);
    CheckMode(self, unrelated, 2, false, true, "group member");
    ClearGroup(self, unrelated);

    CheckMode(self, unrelated, 3, true, false, "gild outsider");
    CheckMode(self, sameGild, 3, false, true, "same gild");
    CheckMode(self, unionGild, 3, false, true, "union gild");

    CheckMode(self, unrelated, 4, false, false, "hostile baseline");
    self.m_DearHuman = unrelated;
    CheckMode(self, unrelated, 4, false, false, "hostile spouse");
    self.m_DearHuman = null;
    SetGroup(self, unrelated);
    CheckMode(self, unrelated, 4, false, false, "hostile group");
    ClearGroup(self, unrelated);
    CheckMode(self, sameCorps, 4, false, false, "hostile same Corps");
    CheckMode(self, sameGild, 4, false, false, "hostile same Gild");
    // Native 0x683204/0x6846B0 test ONLY al==1 (allies) for PK prevention.
    // No native code tests al==2 (war) to enable PK. War relation does NOT
    // make targets attackable in hostile mode.
    CheckMode(self, hostileGild, 4, false, false,
        "hostile Gild relation does not enable PK");

    unrelated.m_boPKFlag = true;
    CheckMode(self, unrelated, 4, true, false, "hostile PK flag");
    unrelated.m_boPKFlag = false;
    unrelated.m_PEnvir = new Envirnoment
    {
        Flag = new TMapFlag { boFightZone = true }
    };
    CheckMode(self, unrelated, 4, true, false, "hostile fight zone");
    unrelated.m_PEnvir = null;
    M2Share.g_Config.nPKPunishPoint = 500;
    unrelated.m_nPkPoint = 499;
    CheckMode(self, unrelated, 4, false, false,
        "hostile below PK threshold");
    unrelated.m_nPkPoint = 500;
    CheckMode(self, unrelated, 4, true, false,
        "hostile at PK threshold");
    unrelated.m_nPkPoint = 0;

    CheckMode(self, sameCorps, 5, false, true, "Corps same");
    CheckMode(self, sameGild, 5, true, false, "Corps different");
    CheckMode(self, noCorps, 5, true, false, "Corps target absent");
    var noCorpsSelf = NewSocialPlayer(8, "no-corps-self");
    CheckMode(noCorpsSelf, noCorps, 5, true, true,
        "Corps both absent");
    CheckMode(noCorpsSelf, self, 5, true, false,
        "Corps self absent");

    var summoned = new TBaseObject { m_Master = sameCorps };
    self.m_btAttatckMode = 5;
    Assert(!self.IsAttackTarget(summoned),
        "same-Corps summon must not be attackable");
    Assert(self.IsProperFriend(summoned),
        "same-Corps summon must be friendly");
    summoned.m_Master = unrelated;
    Assert(self.IsAttackTarget(summoned),
        "unrelated summon must follow owner attack relation");
    Assert(!self.IsProperFriend(summoned),
        "unrelated summon must follow owner friend relation");

    summoned.m_Master = self;
    self.m_btAttatckMode = 0;
    Assert(self.IsAttackTarget(summoned),
        "own summon is attackable only in all mode");
    self.m_btAttatckMode = 3;
    Assert(!self.IsAttackTarget(summoned),
        "own summon must not be attackable outside all mode");
    Assert(self.IsProperFriend(summoned),
        "own summon must always be friendly");
}

static void CheckMode(TPlayObject self, TPlayObject target, byte mode,
    bool attack, bool friend, string label)
{
    self.m_btAttatckMode = mode;
    Equal(attack, self.IsAttackTarget(target), label + " attack");
    Equal(friend, self.IsProperFriend(target), label + " friend");
}

static void SetGroup(TPlayObject leader, TPlayObject member)
{
    leader.m_GroupOwner = leader;
    member.m_GroupOwner = leader;
    leader.m_GroupMembers.Clear();
    leader.m_GroupMembers.Add(leader);
    leader.m_GroupMembers.Add(member);
}

static void ClearGroup(TPlayObject leader, TPlayObject member)
{
    leader.m_GroupOwner = null;
    member.m_GroupOwner = null;
    leader.m_GroupMembers.Clear();
}

static NativeCorpsService CreateCombatService()
{
    var snapshot = new NativeCorpsDataSnapshot();
    AddCorps(snapshot, 100, 1, 2);
    AddCorps(snapshot, 200, 3);
    AddCorps(snapshot, 300, 4);
    AddCorps(snapshot, 400, 5);
    AddCorps(snapshot, 500, 6);
    AddGild(snapshot, 1000, 100, 200);
    AddGild(snapshot, 2000, 300);
    AddGild(snapshot, 3000, 400);
    AddGild(snapshot, 4000, 500);
    snapshot.GildRelations.Add(
        NativeCorpsDataSnapshot.GildRelationKey(1000, 2000),
        (NativeCorpsService.GildUnion, DateTime.MinValue));
    snapshot.GildRelations.Add(
        NativeCorpsDataSnapshot.GildRelationKey(1000, 3000),
        (NativeCorpsService.GildHostile, DateTime.MinValue));

    Assert(NativeCorpsService.TryCreate(new FakeStore(snapshot),
        out var service, out var error), "combat service: " + error);
    return service;
}

static void AddCorps(NativeCorpsDataSnapshot snapshot, long corpsId,
    params long[] memberIds)
{
    var corps = new NativeCorpsSnapshot
    {
        Id = corpsId,
        Name = "corps-" + corpsId,
        OwnerId = memberIds[0]
    };
    foreach (var memberId in memberIds)
    {
        corps.Members.Add(new NativeCorpsMemberSnapshot
        {
            MemberId = memberId,
            Name = "member-" + memberId
        });
    }
    snapshot.CorpsById.Add(corpsId, corps);
}

static void AddGild(NativeCorpsDataSnapshot snapshot, long gildId,
    params long[] corpsIds)
{
    var gild = new NativeGildSnapshot
    {
        Id = gildId,
        Name = "gild-" + gildId,
        OwnerCorpsId = corpsIds[0]
    };
    foreach (var corpsId in corpsIds) gild.CorpsIds.Add(corpsId);
    snapshot.GildById.Add(gildId, gild);
}

static ProbePlayer NewPlayer()
{
    var player = new ProbePlayer
    {
        m_boOffLineFlag = false,
        m_MsgList = new List<SendMessage>()
    };
    M2Share.UserEngine.ProcessUserMessage(player, new ClientPacket
    {
        Ident = Grobal2.CM_LOGINNOTICEOK,
        Recog = 1,
        Param = 0,
        Tag = 0,
        Series = 0
    }, string.Empty);
    player.m_boOffLineFlag = true;
    return player;
}

static ProbePlayer NewSocialPlayer(long id, string name)
{
    var player = NewPlayer();
    player.LoadNativeMailRecipientId(id);
    player.m_sCharName = name;
    return player;
}

static void Packet(ClientPacket packet, int mode, string label)
{
    Assert(packet != null, label + " packet");
    Equal((ushort)Grobal2.CM_ATTACKMODE, packet.Ident,
        label + " ident");
    Equal(mode, packet.Recog, label + " recog");
    Equal((ushort)0, packet.Param, label + " param");
    Equal((ushort)0, packet.Tag, label + " tag");
    Equal((ushort)0, packet.Series, label + " series");
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
    if (!condition) throw new InvalidOperationException(label);
}

sealed class ProbePlayer : TPlayObject
{
    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);
}

sealed class FakeStore : INativeCorpsStore
{
    private readonly NativeCorpsDataSnapshot _snapshot;

    internal FakeStore(NativeCorpsDataSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public bool TryLoad(out NativeCorpsDataSnapshot snapshot,
        out string error)
    {
        snapshot = _snapshot;
        error = string.Empty;
        return true;
    }

    public bool TryInsertMember(long corpsId,
        NativeCorpsMemberSnapshot member, out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryDeleteMember(long memberId, out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryExitMember(long memberId, NativeCorpsSnapshot corps,
        bool updateCorps, out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryUpdateMemberTitle(long memberId, string title,
        out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryUpdateCorps(NativeCorpsSnapshot corps, out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryUpdateGild(NativeGildSnapshot gild, out string error)
    {
        error = string.Empty;
        return true;
    }
}
