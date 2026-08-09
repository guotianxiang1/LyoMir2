using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr.Services;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

CheckCreatePayload();
CheckChannelDecodePath();
CheckCreateAndEnter();
CheckEnterAndExitErrors();
CheckModeKickMuteErrors();
CheckWireLayouts();
CheckScopedResolver();
CheckHandlerSourceContract();

Console.WriteLine("NativeChannelProtocolCheck PASS");
return;

static void CheckCreatePayload()
{
    Assert(!NativeChannelWireCodec.TryDecodeCreatePayload(new byte[24],
        out _, out var error) && error == -99, "create length -99");

    var invalidPassword = CreatePayload("room", true, "bad", 2);
    Assert(!NativeChannelWireCodec.TryDecodeCreatePayload(invalidPassword,
        out _, out error) && error == -5, "create password -5");

    var minusOne = CreatePayload("room", true, "-1", 2);
    Assert(!NativeChannelWireCodec.TryDecodeCreatePayload(minusOne,
        out _, out error) && error == -5, "create password -1 sentinel");

    var lowCapacity = CreatePayload("room", false, "", 1);
    Assert(!NativeChannelWireCodec.TryDecodeCreatePayload(lowCapacity,
        out _, out error) && error == -6, "create capacity lower bound");
    var highCapacity = CreatePayload("room", false, "", 201);
    Assert(!NativeChannelWireCodec.TryDecodeCreatePayload(highCapacity,
        out _, out error) && error == -6, "create capacity upper bound");

    var valid = CreatePayload("room", true, "0", 200);
    Assert(NativeChannelWireCodec.TryDecodeCreatePayload(valid,
        out var request, out error) && error == 0, "create payload valid");
    Equal((byte)1, request.Type, "password channel type");
    Equal(0L, request.Password, "password zero");
    Equal((byte)200, request.Capacity, "capacity 200");
}

// Handler-level encoded-payload proof (fills the audit blind spot: channel bodies are
// 6-bit-ENCODED on the wire, and pre-fix the handler read the raw Payload = garbage).
// Feed a real EncodeBuffer'd create body through the REAL shared DecodeNativeSocialBody,
// then the codec must recover the exact request. Password int64 + target name ride the
// same helper (id/name proven by InProcCorpsGuildRunCheck).
static void CheckChannelDecodePath()
{
    var raw = CreatePayload("测试室", true, "12345", 50);
    var encoded = SystemModule.EDcode.EncodeBuffer(raw);
    var decoded = InvokeDecodeNativeSocialBody(encoded);
    Assert(!encoded.AsSpan().SequenceEqual(decoded),
        "decode-path[channel]: the 6-bit decode transforms the wire body");
    Assert(NativeChannelWireCodec.TryDecodeCreatePayload(decoded, out var request,
               out var error) && error == 0 && request.Type == (byte)1
           && request.Password == 12345L && request.Capacity == (byte)50,
        "decode-path[channel]: decoded create body parses to the exact request");
}

static byte[] InvokeDecodeNativeSocialBody(byte[] encodedPayload)
{
    var method = typeof(GameSvr.TPlayObject).GetMethod("DecodeNativeSocialBody",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TPlayObject.DecodeNativeSocialBody");
    return (byte[])method.Invoke(null, new object[] { encodedPayload });
}

static void CheckCreateAndEnter()
{
    var manager = new NativeChannelManager();
    var lowLevel = Actor(1, "low", 34);
    Equal(-3, manager.CreatePublic(lowLevel,
        Request("low", false, 2)).Code, "create level -3");

    var owner = Actor(2, "owner");
    var open = manager.CreatePublic(owner, Request("open", false, 2));
    Equal(0, open.Code, "open create");
    Equal(1000, open.ChannelId, "first channel id");
    Equal((byte)0, open.Type, "open type");
    Equal(0, manager.Enter(owner, open.ChannelId, 0).Code,
        "open auto-enter");

    var protectedOwner = Actor(3, "protected-owner");
    var protectedChannel = manager.CreatePublic(protectedOwner,
        Request("protected", true, 3, 123));
    Equal(1001, protectedChannel.ChannelId, "second channel id");
    Equal((byte)1, protectedChannel.Type, "protected type");
    Equal(0, manager.Enter(protectedOwner, protectedChannel.ChannelId, 123).Code,
        "protected enter");
    var dupNameActor = Actor(4, "dup-name");
    Equal(-1, manager.CreatePublic(dupNameActor,
        Request("PROTECTED", false, 3)).Code,
        "cross-type duplicate name");
    // -4 (already-in-a-channel) precedes -1 (name exists): owner entered
    // channel 1000 above, so any create by owner is rejected -4 first —
    // over a fresh unique name and over the duplicate name alike.
    Equal(-4, manager.CreatePublic(owner,
        Request("owner-rechannel", false, 3)).Code,
        "in-channel create fresh name -4");
    Equal(-4, manager.CreatePublic(owner,
        Request("PROTECTED", false, 3)).Code,
        "in-channel create dup name -4 precedes -1");

    var first = manager.QueryById(open.ChannelId).Snapshot;
    var second = manager.QueryById(protectedChannel.ChannelId).Snapshot;
    Equal((ushort)1, first.NativeSlot, "first public slot");
    Equal((ushort)2, second.NativeSlot, "second public slot");

    var limited = new NativeChannelManager();
    for (var index = 0; index < 50; index++)
    {
        Equal(0, limited.CreatePublic(owner,
            Request("channel" + index, index % 2 != 0, 2, index)).Code,
            "public limit setup " + index);
    }
    Equal(-2, limited.CreatePublic(owner,
        Request("overflow", false, 2)).Code, "public limit -2");
}

static void CheckEnterAndExitErrors()
{
    var manager = new NativeChannelManager();
    var owner = Actor(10, "owner");
    var member = Actor(11, "member");
    var missing = Actor(0, "missing");
    var channel = manager.CreatePublic(owner,
        Request("protected", true, 2, 77));

    Equal(-7, manager.Enter(missing, 9999, 77).Code,
        "enter missing channel before caller");
    Equal(-99, manager.Enter(missing, channel.ChannelId, 77).Code,
        "enter missing caller");
    Equal(-8, manager.Enter(owner, channel.ChannelId, 76).Code,
        "enter password -8");
    Equal(0, manager.Enter(owner, channel.ChannelId, 77).Code,
        "enter owner");
    Equal(0, manager.Enter(member, channel.ChannelId, 77).Code,
        "enter member");
    Equal(-10, manager.Enter(Actor(12, "third"), channel.ChannelId, 77).Code,
        "enter full -10");

    var empty = new NativeChannelManager();
    var closed = empty.CreatePublic(owner, Request("closed", false, 2));
    Equal(0, empty.Enter(owner, closed.ChannelId, 0).Code,
        "closed setup enter");
    Equal(0, empty.Exit(owner), "closed setup exit");
    Equal(-30, empty.Enter(missing, closed.ChannelId, 0).Code,
        "closed before caller -30");

    var exit = new NativeChannelManager();
    Equal(-99, exit.Exit(missing), "exit caller -99");
    Equal(-13, exit.Exit(owner), "exit no channel -13");
    SetCurrentChannel(exit, owner.Identity, 7777);
    Equal(-11, exit.Exit(owner), "exit missing channel -11");
    // owner is pinned to phantom channel 7777 for the -11 probe, so it now
    // trips the -4 create gate; use a fresh actor for the create so the -12
    // (current channel exists but caller is not a member) path is reached.
    var exitMember = Actor(13, "exit-member");
    var created = exit.CreatePublic(exitMember, Request("exit", false, 3));
    Equal(0, created.Code, "exit create success");
    SetCurrentChannel(exit, exitMember.Identity, created.ChannelId);
    Equal(-12, exit.Exit(exitMember), "exit nonmember -12");
}

static void CheckModeKickMuteErrors()
{
    var missing = Actor(0, "missing");

    var mode = Scenario();
    Equal(-99, mode.Manager.ChangeMode(missing, mode.Id, 1),
        "mode caller -99");
    Equal(-15, mode.Manager.ChangeMode(mode.Outsider, mode.Id, 1),
        "mode channel mismatch -15");
    Equal(-16, mode.Manager.ChangeMode(mode.Outsider, 0, 1),
        "mode channel missing -16");
    Equal(-14, mode.Manager.ChangeMode(mode.Member, mode.Id, 1),
        "mode owner -14");
    Equal(0, mode.Manager.ChangeMode(mode.Owner, mode.Id, 0xFE),
        "mode success");
    Equal((byte)0xFE, mode.Manager.QueryById(mode.Id).Snapshot.Mode,
        "mode byte write");

    var kick = Scenario();
    Equal(-99, kick.Manager.Kick(missing, kick.Id, kick.Outsider),
        "kick caller -99");
    Equal(-18, kick.Manager.Kick(kick.Outsider, kick.Id, kick.Member),
        "kick mismatch -18");
    SetCurrentChannel(kick.Manager, kick.Owner.Identity, 8888);
    Equal(-20, kick.Manager.Kick(kick.Owner, 8888, kick.Member),
        "kick channel missing -20");
    SetCurrentChannel(kick.Manager, kick.Owner.Identity, kick.Id);
    Equal(-17, kick.Manager.Kick(kick.Member, kick.Id, kick.Outsider),
        "kick owner -17");
    Equal(-19, kick.Manager.Kick(kick.Owner, kick.Id, kick.Outsider),
        "kick target member -19");
    Equal(-21, kick.Manager.Kick(kick.Owner, kick.Id, kick.Owner),
        "kick target owner -21");
    Equal(0, kick.Manager.Kick(kick.Owner, kick.Id, kick.Member),
        "kick success");
    Equal(-13, kick.Manager.Exit(kick.Member),
        "kick cleared target current channel");

    var mute = Scenario();
    Equal(-99, mute.Manager.ChangeMute(missing, mute.Id, mute.Outsider, true),
        "mute caller -99");
    Equal(-24, mute.Manager.ChangeMute(mute.Outsider, mute.Id,
        mute.Member, true), "mute mismatch -24");
    SetCurrentChannel(mute.Manager, mute.Owner.Identity, 9999);
    Equal(-23, mute.Manager.ChangeMute(mute.Owner, 9999,
        mute.Member, true), "mute channel missing -23");
    SetCurrentChannel(mute.Manager, mute.Owner.Identity, mute.Id);
    Equal(-22, mute.Manager.ChangeMute(mute.Member, mute.Id,
        mute.Outsider, true), "mute owner -22");
    Equal(-25, mute.Manager.ChangeMute(mute.Owner, mute.Id,
        mute.Outsider, true), "mute target member -25");
    Equal(-26, mute.Manager.ChangeMute(mute.Owner, mute.Id,
        mute.Owner, true), "mute target owner -26");
    Equal(0, mute.Manager.ChangeMute(mute.Owner, mute.Id,
        mute.Member, true), "mute add");
    Equal(0, mute.Manager.ChangeMute(mute.Owner, mute.Id,
        mute.Member, false), "mute remove");
}

static void CheckWireLayouts()
{
    var manager = new NativeChannelManager();
    var owner = Actor(30, "owner");
    var member = Actor(31, "member");
    var channel = manager.CreatePublic(owner,
        Request("voice-room", true, 3, 8));
    manager.Enter(owner, channel.ChannelId, 8);
    manager.Enter(member, channel.ChannelId, 8);
    manager.ChangeMode(owner, channel.ChannelId, 7);
    manager.ChangeMute(owner, channel.ChannelId, member, true);

    var list = manager.GetPublicChannels();
    var listPayload = NativeChannelWireCodec.EncodeChannelList(list);
    Equal(44, listPayload.Length, "4453 record size");
    Equal(channel.ChannelId, ReadInt32(listPayload, 0), "4453 id +0");
    Equal("voice-room", FixedText(listPayload, 4), "4453 name +4");
    Equal(2, ReadInt32(listPayload, 20), "4453 count +20");
    Equal("owner", FixedText(listPayload, 24), "4453 owner +24");
    Equal((byte)1, listPayload[40], "4453 type +40");
    Equal((byte)3, listPayload[41], "4453 capacity +41");
    Equal((ushort)1, ReadUInt16(listPayload, 42), "4453 slot +42");

    var snapshot = manager.QueryById(channel.ChannelId).Snapshot;
    var membersPayload = NativeChannelWireCodec.EncodeMembers(snapshot,
        out var memberCount);
    Equal(2, memberCount, "4454 online count");
    Equal(60, membersPayload.Length, "4454 24+18*n");
    Equal(channel.ChannelId, ReadInt32(membersPayload, 0), "4454 id +0");
    Equal("voice-room", FixedText(membersPayload, 4), "4454 name +4");
    Equal((byte)7, membersPayload[20], "4454 mode +20");
    Equal((byte)0, membersPayload[21], "4454 pad +21");
    Equal((ushort)1, ReadUInt16(membersPayload, 22), "4454 slot +22");
    Equal("owner", FixedText(membersPayload, 24), "4454 owner name");
    Equal((byte)1, membersPayload[40], "4454 owner flag");
    Equal((byte)0, membersPayload[41], "4454 owner mute flag");
    Equal("member", FixedText(membersPayload, 42), "4454 member name");
    Equal((byte)0, membersPayload[58], "4454 member owner flag");
    Equal((byte)1, membersPayload[59], "4454 member mute flag");
    Equal((ushort)0x0102,
        NativeChannelWireCodec.BuildMembersSeries(snapshot.Type, memberCount),
        "4454 type/count series");
}

static void CheckScopedResolver()
{
    var resolver = new FakeMembershipResolver();
    var manager = new NativeChannelManager(resolver);
    var owner = Actor(40, "corps-owner");
    var member = Actor(41, "corps-member");

    Assert(manager.TryResolveMembership(owner, 2, out var membership),
        "type2 injected membership");
    var first = manager.EnterScoped(owner, 2, membership);
    Assert(first.CreateAttempted && first.CreateCode == 0,
        "type2 auto create result");
    Equal(0, first.Enter.Code, "type2 owner enter");
    Equal(1000, first.Enter.ChannelId, "type2 channel id");

    Assert(manager.TryResolveMembership(member, 2, out membership),
        "type2 second membership");
    var second = manager.EnterScoped(member, 2, membership);
    Assert(!second.CreateAttempted, "type2 channel reuse");
    Equal(0, second.Enter.Code, "type2 member enter");
    var snapshot = manager.QueryScoped(2, membership).Snapshot;
    Equal((byte)2, snapshot.Type, "type2 snapshot type");
    Equal((byte)255, snapshot.Capacity, "type2 capacity");
    Equal((ushort)0, snapshot.NativeSlot, "type2 slot");
    Equal(2, snapshot.MemberCount, "type2 member count");

    Assert(!manager.TryResolveMembership(owner, 4, out _),
        "missing injected membership fails closed");
}

static void CheckHandlerSourceContract()
{
    var root = FindRepositoryRoot();
    var handler = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeChannelProtocol.cs"));
    var models = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "NativeChannelModels.cs"));

    foreach (var protocol in new[]
    {
        "CM_CHANNEL_CREATE", "CM_CHANNEL_ENTER", "CM_CHANNEL_EXIT",
        "CM_CHANNEL_CHANGE_MODE", "CM_CHANNEL_CHANGE_MUTE",
        "CM_CHANNEL_KICK_OUT", "CM_QUERY_CHANNEL_LIST",
        "CM_QUERY_CHANNEL_MEMBERS"
    })
    {
        Require(handler, "case Grobal2." + protocol + ":",
            protocol + " route missing");
    }
    Require(handler, "processMessage.wParam", "Series ABI missing");
    Require(handler, "processMessage.nParam1", "Recog ABI missing");
    Require(handler, "DecodeNativeSocialBody(processMessage.Payload)",
        "decoded payload ABI missing");
    Require(handler, "if (processMessage.nParam1 != 0 && processMessage.nParam1 != 1)",
        "4451 invalid mode must be silently claimed");
    Require(handler, "payload.Length, 0, channels.Count, payload",
        "4453 payload length/count header missing");
    Require(models, "record[40] = channel.Type;",
        "4453 +40 must be channel type");
    Require(models, "payload[20] = channel.Mode;",
        "4454 +20 must be channel mode");
}

static ScenarioState Scenario()
{
    var manager = new NativeChannelManager();
    var owner = Actor(20, "owner");
    var member = Actor(21, "member");
    var outsider = Actor(22, "outsider");
    var channel = manager.CreatePublic(owner, Request("scenario", false, 5));
    manager.Enter(owner, channel.ChannelId, 0);
    manager.Enter(member, channel.ChannelId, 0);
    manager.Exit(outsider);
    return new ScenarioState(manager, channel.ChannelId, owner, member,
        outsider);
}

static NativeChannelActor Actor(long id, string name, int level = 35) =>
    new(id, name, level);

static NativeChannelCreateRequest Request(string name, bool password,
    byte capacity, long value = 0) => new(name, password, value, capacity);

static byte[] CreatePayload(string name, bool password, string passwordText,
    byte capacity)
{
    var payload = new byte[25];
    WriteText(payload.AsSpan(0, 15), name);
    payload[16] = password ? (byte)1 : (byte)0;
    WriteText(payload.AsSpan(17, 7), passwordText);
    payload[24] = capacity;
    return payload;
}

static void WriteText(Span<byte> destination, string value)
{
    destination.Clear();
    var bytes = Encoding.GetEncoding(936).GetBytes(value ?? string.Empty);
    bytes.AsSpan(0, Math.Min(bytes.Length, destination.Length))
        .CopyTo(destination);
}

static string FixedText(byte[] payload, int offset)
{
    var span = payload.AsSpan(offset, 16);
    var zero = span.IndexOf((byte)0);
    if (zero >= 0) span = span.Slice(0, zero);
    return Encoding.GetEncoding(936).GetString(span);
}

static int ReadInt32(byte[] payload, int offset) =>
    BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4));

static ushort ReadUInt16(byte[] payload, int offset) =>
    BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset, 2));

static void SetCurrentChannel(NativeChannelManager manager, long identity,
    int channelId)
{
    var field = typeof(NativeChannelManager).GetField("_actorChannels",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("_actorChannels");
    var channels = (Dictionary<long, int>)field.GetValue(manager)!;
    channels[identity] = channelId;
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory,
                 AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")))
                return directory.FullName;
        }
    }
    throw new DirectoryNotFoundException("repository root not found");
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

sealed record ScenarioState(NativeChannelManager Manager, int Id,
    NativeChannelActor Owner, NativeChannelActor Member,
    NativeChannelActor Outsider);

sealed class FakeMembershipResolver : INativeChannelMembershipResolver
{
    public bool TryResolve(NativeChannelActor actor, byte type,
        out NativeChannelMembership membership)
    {
        if (type == 2)
        {
            membership = new NativeChannelMembership("corps-7", "7");
            return true;
        }
        membership = default;
        return false;
    }
}
