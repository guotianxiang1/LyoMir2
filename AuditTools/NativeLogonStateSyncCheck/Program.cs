// Pins the distinct player and hero RM 0x3010 login-state clusters.
//
// WHY THIS EXISTS
// ---------------
// UserLogon (sub_6B1D64) does not send the login-state cluster inline. At
// 0x6B2358 it enqueues RM 0x3010 (`66 B9 10 30 mov cx,0x3010` -> sub_765E68 with
// edx=eax=Self and six zero params). On the next Run tick the Operate loop's
// secondary dispatcher sub_743AD8 (reached at 0x6B6247 `call 0x743AD8`) turns
// case 0x3010 (`0x743B24 sub eax,0x75F / je 0x743BF3`) into
// `0x743BF7 call [edx+0x204]`. The player VMT selects sub_6E9A98, which fans
// out four legs, in order:
//   0x6E9AA0 call 0x7468B4 -> SM 3324   (persisted at record+0x580/+0x57C)
//   0x6E9AA7 call 0x6F0A50 -> SM 1264   (Param = ServerSwitch.Bin bit31 ? 1 : 0)
//   0x6E9AAE call 0x6E99B8 -> SM 3554   (timed-ability snapshot; RESOLVED, pinned here)
//   0x6E9ABB call 0x74839C -> SM 3556 (cold-time list; empty is silent)
// The four hero VMTs instead select sub_69057C, which sends only:
//   0x690584 call 0x7468B4 -> SM 3324
//   0x690591 call 0x74839C -> SM 4367 (cold-time list; empty is silent)
// This tool freezes both clusters, their source-record fixup, and 3554 parity.
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
CheckMapInfoExLoginSync();
CheckSm3324Builder();
CheckSm1264Builder();
CheckSm3324LogonFlow();
CheckHeroLogonFlow();
CheckColdTimeLogonLegs();
CheckEmptySnapshotFrame();
CheckMultiNodeSnapshotFrame();
CheckRecordParityWith3555();
CheckWiringAndResolvedLegs();

Console.WriteLine(
    "PASS NativeLogonStateSync rm=12304(0x3010) sm=3324(raw+0x580/+0x57C,series=race54) " +
    "sm=1281(mapinfoex,all-zero,sorted-crlf) " +
    "sm=1264(always,param=ServerSwitch.Bin.bit31) " +
    "sm=3554 header=recog0/param=count/tag0/series0 " +
    "record=10B{type,0,remainMs:i32,value:i32}==3555 empty-list=param0/len0 " +
    "player=3324->1264->3554->optional3556 hero=3324->optional4367");
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

static void CheckMapInfoExLoginSync()
{
    Equal(1281, Grobal2.SM_MAPINFO_EX,
        "SM MapInfoEx ident 0x0501");

    var root = Path.Combine(AppContext.BaseDirectory,
        "mapinfoex-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var fileName = Path.Combine(root, "MapInfoEx.txt");
    var oldManager = M2Share.MapManager;
    try
    {
        var manager = new MapManager();
        M2Share.MapManager = manager;

        Equal(false, manager.LoadNativeMapInfoEx(fileName),
            "missing MapInfoEx load result");
        Equal(string.Empty, manager.GetNativeMapInfoExText(),
            "fresh missing MapInfoEx remains empty");

        File.WriteAllText(fileName,
            "zeta\r\nAlpha\r\n\r\n中文\r\nalpha\r\n",
            HUtil32.GbkEncoding);
        Equal(true, manager.LoadNativeMapInfoEx(fileName),
            "existing GBK MapInfoEx loads");
        var lines = manager.SnapshotNativeMapInfoEx();
        Equal(4, lines.Count,
            "sorted TStringList ignores case-insensitive duplicate");
        Equal(true, lines.Contains("Alpha"),
            "first duplicate spelling retained");
        Equal(false, lines.Contains("alpha"),
            "later case-insensitive duplicate ignored");
        Equal(true, lines.Contains(string.Empty),
            "empty line retained");
        for (var i = 1; i < lines.Count; i++)
        {
            Assert(MapManager.CompareNativeMapInfoEx(lines[i - 1], lines[i]) <= 0,
                "MapInfoEx native ANSI order " + i);
        }

        var expectedText = string.Concat(lines.Select(line => line + "\r\n"));
        Equal(expectedText, manager.GetNativeMapInfoExText(),
            "MapInfoEx Text has trailing CRLF on every line");

        File.Delete(fileName);
        Equal(false, manager.LoadNativeMapInfoEx(fileName),
            "missing reload reports false");
        Equal(expectedText, manager.GetNativeMapInfoExText(),
            "missing reload preserves prior list");

        var player = new LogonProbe(0);
        player.SendMapInfoExLogin();
        Equal(1, player.TextPackets.Count,
            "MapInfoEx login sends exactly once");
        var packet = player.TextPackets[0];
        Equal(unchecked((ushort)1281), packet.Header.Ident,
            "MapInfoEx login ident");
        Equal(0, packet.Header.Recog, "MapInfoEx login Recog");
        Equal(unchecked((ushort)0), packet.Header.Param,
            "MapInfoEx login Param");
        Equal(unchecked((ushort)0), packet.Header.Tag,
            "MapInfoEx login Tag");
        Equal(unchecked((ushort)0), packet.Header.Series,
            "MapInfoEx login Series");
        Equal(expectedText, packet.Message,
            "MapInfoEx login exact body");

        File.WriteAllBytes(fileName, Array.Empty<byte>());
        Equal(true, manager.LoadNativeMapInfoEx(fileName),
            "existing empty MapInfoEx loads");
        Equal(string.Empty, manager.GetNativeMapInfoExText(),
            "existing empty MapInfoEx clears prior list");
        var emptyPlayer = new LogonProbe(0);
        emptyPlayer.SendMapInfoExLogin();
        Equal(1, emptyPlayer.TextPackets.Count,
            "empty MapInfoEx still sends header");
        Equal(string.Empty, emptyPlayer.TextPackets[0].Message,
            "empty MapInfoEx sends header-only body");
    }
    finally
    {
        M2Share.MapManager = oldManager;
        if (File.Exists(fileName))
        {
            File.Delete(fileName);
        }
        if (Directory.Exists(root))
        {
            Directory.Delete(root);
        }
    }
}

static void CheckSm3324Builder()
{
    const int recog = unchecked((int)0x89ABCDEFu);
    var normal = TBaseObject.BuildSm3324(recog, 0x5678, false);
    CheckSm3324Header(normal.Header, recog, 0x5678, 0, "builder normal");
    Equal(string.Empty, normal.Msg, "builder normal empty body");

    var hero = TBaseObject.BuildSm3324(recog, 0x5678, true);
    CheckSm3324Header(hero.Header, recog, 0x5678, 1, "builder hero");
    Equal(string.Empty, hero.Msg, "builder hero empty body");
}

static void CheckSm1264Builder()
{
    var disabled = TBaseObject.BuildSm1264(false);
    CheckSm1264Header(disabled.Header, 0, "builder switch disabled");
    Equal(0, disabled.Body.Length, "builder switch disabled empty body");

    var enabled = TBaseObject.BuildSm1264(true);
    CheckSm1264Header(enabled.Header, 1, "builder switch enabled");
    Equal(0, enabled.Body.Length, "builder switch enabled empty body");
}

static void CheckSm3324LogonFlow()
{
    const uint mask = 0x89ABCDEFu;
    const int storedPrereq = 0x12345678;

    SetNewTradeLineSwitch(false);
    var normal = NewLogonProbe(mask, storedPrereq, 0);
    InvokeLogonStateSync(normal);
    Equal(2, normal.TextPackets.Count,
        "normal login captured SM 3324/1264 count");
    CheckSm3324Header(normal.TextPackets[0].Header, unchecked((int)mask), 0x5678,
        0, "normal login");
    Equal(string.Empty, normal.TextPackets[0].Message, "normal login empty body");
    CheckSm1264Header(normal.TextPackets[1].Header, 0,
        "normal login switch disabled");
    Equal(string.Empty, normal.TextPackets[1].Message,
        "normal login SM 1264 empty body");
    CheckCaptured3554(normal, "normal login");
    Assert(normal.Sequence.SequenceEqual(new ushort[]
        { SmIdentConstsA.SM_3324, Grobal2.SM_1264, 3554 }),
        "normal login order 3324 -> 1264 -> 3554");

    SetNewTradeLineSwitch(true);
    var heroRacePlayer = NewLogonProbe(mask, storedPrereq, 0x36);
    InvokeLogonStateSync(heroRacePlayer);
    Equal(2, heroRacePlayer.TextPackets.Count,
        "hero-race player probe captured SM 3324/1264 count");
    CheckSm3324Header(heroRacePlayer.TextPackets[0].Header,
        unchecked((int)mask), 0x5678, 1, "hero-race player probe");
    CheckSm1264Header(heroRacePlayer.TextPackets[1].Header, 1,
        "hero-race player probe switch enabled");
    CheckCaptured3554(heroRacePlayer, "hero-race player probe");

    SetNewTradeLineSwitch(false);
    foreach (var stored in new[] { 0, -1 })
    {
        var fixedUp = NewLogonProbe(0x01234567u, stored, 0);
        InvokeLogonStateSync(fixedUp);
        Equal(2, fixedUp.TextPackets.Count,
            $"stored prereq {stored} captured SM 3324/1264 count");
        CheckSm3324Header(fixedUp.TextPackets[0].Header, 0x01234567, 1, 0,
            $"stored prereq {stored} decoder fixup");
        CheckSm1264Header(fixedUp.TextPackets[1].Header, 0,
            $"stored prereq {stored} switch disabled");
        CheckCaptured3554(fixedUp, $"stored prereq {stored}");
    }

    SetNewTradeLineSwitch(true);
    var shortRecord = new LogonProbe(0)
    {
        m_NativeHumanData = new byte[0x583]
    };
    InvokeLogonStateSync(shortRecord);
    Equal(1, shortRecord.TextPackets.Count,
        "short raw record skips SM 3324 but still sends SM 1264");
    CheckSm1264Header(shortRecord.TextPackets[0].Header, 1,
        "short raw record switch enabled");
    CheckCaptured3554(shortRecord, "short raw record");
    Assert(shortRecord.Sequence.SequenceEqual(new ushort[]
        { Grobal2.SM_1264, 3554 }),
        "short raw record order 1264 -> 3554");
}

static void CheckHeroLogonFlow()
{
    SetNewTradeLineSwitch(false);
    var disabledMaster = new LogonProbe(0);
    var disabledHero = NewHeroLogonProbe(disabledMaster, 0x89ABCDEFu, 0);
    Equal(true, disabledHero.Operate(new TProcessMessage
        { wIdent = Grobal2.RM_NATIVE_LOGON_STATE_SYNC }),
        "real hero disabled-switch RM dispatch");
    Equal(1, disabledMaster.TextPackets.Count,
        "real hero sends only SM 3324 when cold-time list is empty");
    CheckSm3324Header(disabledMaster.TextPackets[0].Header,
        unchecked((int)0x89ABCDEFu), 1, 1,
        "real hero decoder prereq fixup");
    Equal(string.Empty, disabledMaster.TextPackets[0].Message,
        "real hero SM 3324 empty body");
    Equal(0, disabledMaster.BinaryPackets.Count,
        "real hero must not send player-only SM 3554");
    Assert(disabledMaster.Sequence.SequenceEqual(new ushort[]
        { SmIdentConstsA.SM_3324 }),
        "real hero empty cold-time list sends only 3324");

    SetNewTradeLineSwitch(true);
    var enabledMaster = new LogonProbe(0);
    var enabledHero = NewHeroLogonProbe(enabledMaster, 0x01234567u,
        0x12345678);
    Equal(true, enabledHero.Operate(new TProcessMessage
        { wIdent = Grobal2.RM_NATIVE_LOGON_STATE_SYNC }),
        "real hero enabled-switch RM dispatch");
    CheckSm3324Header(enabledMaster.TextPackets[0].Header, 0x01234567,
        0x5678, 1, "real hero persisted source");
    Equal(1, enabledMaster.TextPackets.Count,
        "ServerSwitch bit31 does not add hero SM 1264");
    Equal(0, enabledMaster.BinaryPackets.Count,
        "enabled switch does not add hero SM 3554");
    Assert(enabledMaster.Sequence.SequenceEqual(new ushort[]
        { SmIdentConstsA.SM_3324 }),
        "real hero enabled-switch sequence remains only 3324");

    var detached = new HeroObject();
    detached.m_MsgList.Clear();
    detached.SendHeroLogon();
    var queued = detached.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_NATIVE_LOGON_STATE_SYNC);
    Equal(Grobal2.RM_NATIVE_LOGON_STATE_SYNC, queued.wIdent,
        "detached hero still queues RM 0x3010");
    Equal(true, detached.Operate(new TProcessMessage { wIdent = queued.wIdent }),
        "detached hero queued RM reaches Operate arm");

    var shortMaster = new LogonProbe(0);
    var shortHero = new HeroObject
    {
        m_Master = shortMaster,
        NativeHeroState = new NativeHeroRuntimeState(
            new byte[HeroObject.NativeLogonCapBitmaskRecordOffset + 3],
            new NativeHeroDynamicData(Array.Empty<NativeHeroDynamicSection>()),
            Array.Empty<bool>(), Array.Empty<bool>())
    };
    Equal(true, shortHero.Operate(new TProcessMessage
        { wIdent = Grobal2.RM_NATIVE_LOGON_STATE_SYNC }),
        "short hero record RM dispatch");
    Equal(0, shortMaster.TextPackets.Count,
        "short hero record skips only unavailable 3324 state");
    Equal(0, shortMaster.BinaryPackets.Count,
        "short hero record does not fabricate player-only 3554");
}

static void CheckColdTimeLogonLegs()
{
    const uint key = 0x11223344u;
    const int remaining = 0x55667788;

    var player = NewLogonProbe(0x01020304u, 9, 0);
    player.m_NativeColdTimes.Add(new TBaseObject.NativeColdTimeEntry
    {
        Key = key,
        Remaining = remaining,
        Total = remaining
    });
    player.m_NativeColdTimePacketLog = new();
    InvokeLogonStateSync(player);
    Equal(1, player.m_NativeColdTimePacketLog.Count,
        "player login non-empty cold-time bulk packet count");
    CheckColdTimePacket(player.m_NativeColdTimePacketLog[0], 3556,
        key, remaining, "player login cold-time");

    var master = new LogonProbe(0);
    var hero = NewHeroLogonProbe(master, 0x05060708u, 10);
    hero.m_NativeColdTimes.Add(new TBaseObject.NativeColdTimeEntry
    {
        Key = key,
        Remaining = remaining,
        Total = remaining
    });
    master.m_NativeColdTimePacketLog = new();
    Equal(true, hero.Operate(new TProcessMessage
        { wIdent = Grobal2.RM_NATIVE_LOGON_STATE_SYNC }),
        "hero non-empty cold-time RM dispatch");
    Equal(1, master.m_NativeColdTimePacketLog.Count,
        "hero login non-empty cold-time bulk packet count");
    CheckColdTimePacket(master.m_NativeColdTimePacketLog[0], 4367,
        key, remaining, "hero login cold-time");
    Equal(1, master.TextPackets.Count,
        "hero cold-time cluster still has exactly one text packet (3324)");
    Equal(0, master.BinaryPackets.Count,
        "hero cold-time cluster has no player-only 3554 snapshot");
}

static void CheckColdTimePacket((ClientPacket Header, byte[] Body) packet,
    int ident, uint key, int remaining, string label)
{
    Equal(unchecked((ushort)ident), packet.Header.Ident, label + " ident");
    Equal(0, packet.Header.Recog, label + " Recog");
    Equal((ushort)0, packet.Header.Param, label + " Param");
    Equal((ushort)1, packet.Header.Tag, label + " count Tag");
    Equal((ushort)0, packet.Header.Series, label + " Series");
    Equal(8, packet.Body.Length, label + " body length");
    Equal(key, BinaryPrimitives.ReadUInt32LittleEndian(packet.Body),
        label + " key");
    Equal(remaining,
        BinaryPrimitives.ReadInt32LittleEndian(packet.Body.AsSpan(4)),
        label + " remaining");
}

static HeroObject NewHeroLogonProbe(LogonProbe master, uint capBitmask,
    int storedPrereq)
{
    var raw = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    BinaryPrimitives.WriteInt32LittleEndian(
        raw.AsSpan(HeroObject.NativeLogonPrereqRecordOffset, sizeof(int)),
        storedPrereq);
    BinaryPrimitives.WriteUInt32LittleEndian(
        raw.AsSpan(HeroObject.NativeLogonCapBitmaskRecordOffset, sizeof(uint)),
        capBitmask);
    return new HeroObject
    {
        m_Master = master,
        NativeHeroState = new NativeHeroRuntimeState(raw,
            new NativeHeroDynamicData(Array.Empty<NativeHeroDynamicSection>()),
            Array.Empty<bool>(), Array.Empty<bool>())
    };
}

static void CheckCaptured3554(LogonProbe probe, string label)
{
    Equal(1, probe.BinaryPackets.Count, label + " captured SM 3554 count");
    CheckHeader(probe.BinaryPackets[0].Header, 0, label + " SM 3554");
    Equal(0, probe.BinaryPackets[0].Body.Length,
        label + " empty timed-ability body");
}

static LogonProbe NewLogonProbe(uint mask, int storedPrereq, byte race)
{
    var player = new LogonProbe(race)
    {
        m_NativeHumanData = new byte[0x584]
    };
    BinaryPrimitives.WriteInt32LittleEndian(
        player.m_NativeHumanData.AsSpan(0x57C, sizeof(int)), storedPrereq);
    BinaryPrimitives.WriteUInt32LittleEndian(
        player.m_NativeHumanData.AsSpan(0x580, sizeof(uint)), mask);
    return player;
}

static void SetNewTradeLineSwitch(bool enabled)
{
    var switches = new byte[NativeServerSwitchStore.SwitchByteCount];
    if (enabled)
        switches[3] = 0x80;
    M2Share.ServerSwitches = NativeServerSwitchStore.FromSnapshot(
        string.Empty, switches);
}

static void InvokeLogonStateSync(TPlayObject player)
{
    var method = typeof(TPlayObject).GetMethod("SendNativeLogonStateSync",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("SendNativeLogonStateSync missing");
    method.Invoke(player, null);
}

static void CheckSm3324Header(ClientPacket header, int recog, ushort param,
    ushort series, string label)
{
    Assert(header != null, label + " header present");
    Equal((ushort)SmIdentConstsA.SM_3324, header.Ident, label + " ident");
    Equal(recog, header.Recog, label + " recog");
    Equal(param, header.Param, label + " param");
    Equal((ushort)0, header.Tag, label + " tag");
    Equal(series, header.Series, label + " series");
}

static void CheckSm1264Header(ClientPacket header, ushort param,
    string label)
{
    Assert(header != null, label + " header present");
    Equal((ushort)Grobal2.SM_1264, header.Ident, label + " ident");
    Equal(0, header.Recog, label + " recog");
    Equal(param, header.Param, label + " param");
    Equal((ushort)0, header.Tag, label + " tag");
    Equal((ushort)0, header.Series, label + " series");
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

static void CheckWiringAndResolvedLegs()
{
    var root = FindRepoRoot();

    var grobal = File.ReadAllText(Path.Combine(root, "SystemModule", "Grobal2.cs"));
    Contains(grobal, "public const int RM_NATIVE_LOGON_STATE_SYNC = 12304;",
        "RM constant declaration");

    // UserLogon enqueues the RM *before* the SM 888 send (native 0x6B2358 precedes
    // 0x6B23C6), matching "cluster arrives on the next Run tick, after every direct SM".
    var baseSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Base.cs"));
    var userLogon = Between(baseSource, "public void UserLogon()",
        "private bool WeaptonMakeLuck()");
    Contains(userLogon, "SendNativeMapInfoExLogin();",
        "UserLogon MapInfoEx direct send");
    Before(userLogon, "Initialize();", "SendNativeMapInfoExLogin();",
        "UserLogon initializes before MapInfoEx");
    Before(userLogon, "SendNativeMapInfoExLogin();",
        "SendMsg(this, Grobal2.RM_LOGON",
        "MapInfoEx direct send before RM_LOGON enqueue");
    Equal(1, Count(userLogon, "SendNativeMapInfoExLogin();"),
        "UserLogon sends MapInfoEx exactly once");

    var mapsSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Maps",
        "Maps.cs"));
    Before(mapsSource, "LoadNativeMapInfoEx(mapInfoExFile);",
        "\"MapInfo.txt\"", "MapInfoEx loads before MapInfo.txt");

    Before(baseSource, "SendMsg(this, Grobal2.RM_NATIVE_LOGON_STATE_SYNC, 0, 0, 0, 0, \"\");",
        "SendDefMessage(Grobal2.SM_LOGIN_VER", "UserLogon enqueue before SM 888");

    var message = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Message.cs"));
    var arm = Between(message, "case Grobal2.RM_NATIVE_LOGON_STATE_SYNC:",
        "break;");
    Contains(arm, "SendNativeLogonStateSync();", "Operate dispatch arm");

    // The player sender emits all four native legs in order.
    var sync = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeLogonStateSync.cs"));
    var bodyStart = sync.IndexOf("private void SendNativeLogonStateSync()",
        StringComparison.Ordinal);
    Assert(bodyStart >= 0, "SendNativeLogonStateSync method present");
    var methodEnd = sync.IndexOf(
        "internal virtual void SendNativeLogonStateSnapshot", bodyStart,
        StringComparison.Ordinal);
    Assert(methodEnd > bodyStart, "SendNativeLogonStateSync method boundary");
    var methodBody = sync[bodyStart..methodEnd];
    // 3c43b685 folded the duplicate SM 3554 builder into TBaseObject's
    // BuildTimedAbilityListState (same ident 0xDE2, same [self+0xDC] walk).
    Contains(methodBody, "TrySoulWashSource(", "3324 persisted source gate");
    Contains(methodBody, "BuildSm3324(", "3324 packet builder call");
    Contains(methodBody,
        "M2Share.ServerSwitches?.IsBitSet(3, 0x80) == true",
        "1264 ServerSwitch.Bin bit31 source");
    Contains(methodBody, "BuildSm1264(", "1264 packet builder call");
    Contains(methodBody, "SendSocket(tradeLine.Header, string.Empty)",
        "1264 string-slot send call");
    Contains(methodBody, "BuildTimedAbilityListState()", "3554 snapshot builder call");
    Contains(methodBody, "SendNativeLogonStateSnapshot(snapshot.Header, snapshot.Body)",
        "3554 snapshot send call");
    Contains(methodBody, "SendNativeColdTimeListState();",
        "3556 cold-time list send call");
    Before(methodBody, "BuildSm3324(", "BuildSm1264(",
        "native 3324 before 1264 ordering");
    Before(methodBody, "BuildSm1264(", "BuildTimedAbilityListState()",
        "native 1264 before 3554 ordering");
    Before(methodBody, "BuildTimedAbilityListState()",
        "SendNativeColdTimeListState();",
        "native 3554 before 3556 ordering");
    Equal(2, Count(methodBody, "SendSocket("),
        "exactly two resolved string send primitives");
    Equal(0, Count(methodBody, "SendDefMessage("), "no fabricated direct send");
    Equal(0, Count(methodBody, "SendRefMsg("), "no fabricated broadcast send");

    var hero = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "HeroObject.cs"));
    var heroLogon = Between(hero, "public void SendHeroLogon()",
        "public void SendHeroLogout()");
    const string heroEnqueue =
        "SendMsg(this, Grobal2.RM_NATIVE_LOGON_STATE_SYNC,";
    Contains(heroLogon, heroEnqueue, "hero RM 3010 enqueue");
    Before(heroLogon, "SendHeroName();", heroEnqueue,
        "hero identity before RM 3010 enqueue");
    Before(heroLogon, heroEnqueue, "SendHeroBagItems();",
        "hero RM 3010 enqueue before later direct logon sends");
    var heroOperate = Between(hero, "public override bool Operate(",
        "protected override void QueueTimedAbilitySnapshotAfterRecalc()");
    Contains(heroOperate, "ProcessMsg.wIdent == Grobal2.RM_NATIVE_LOGON_STATE_SYNC",
        "hero RM 3010 dispatch arm");
    Contains(heroOperate, "SendNativeHeroLogonStateSync();",
        "hero login-state cluster dispatch");

    var heroSync = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "HeroObject.NativeLogonStateSync.cs"));
    Contains(heroSync, "NativeLogonPrereqRecordOffset = 0x110",
        "hero prereq record source");
    Contains(heroSync, "NativeLogonCapBitmaskRecordOffset = 0x114",
        "hero cap-bitmask record source");
    Contains(heroSync, "sub_69057C", "hero-specific VMT target evidence");
    Contains(heroSync, "SendNativeColdTimeListState();",
        "hero 4367 cold-time list send call");
    Before(heroSync, "BuildSm3324(", "SendNativeColdTimeListState();",
        "hero native 3324 before 4367 ordering");
    Equal(0, Count(heroSync, "BuildSm1264("),
        "hero cluster has no player-only SM 1264");
    Equal(0, Count(heroSync, "BuildTimedAbilityListState("),
        "hero cluster has no player-only SM 3554");
}

// --- helpers ------------------------------------------------------------------

static (ClientPacket Header, byte[] Body) InvokeSnapshot(TBaseObject actor)
{
    var method = typeof(TBaseObject).GetMethod("BuildTimedAbilityListState",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("BuildTimedAbilityListState missing");
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

sealed class LogonProbe : TPlayObject
{
    internal List<(ClientPacket Header, string Message)> TextPackets { get; } = new();
    internal List<(ClientPacket Header, byte[] Body)> BinaryPackets { get; } = new();
    internal List<ushort> Sequence { get; } = new();

    internal LogonProbe(byte race)
    {
        m_btRaceServer = race;
        m_boOffLineFlag = true;
    }

    internal void SendMapInfoExLogin()
    {
        SendNativeMapInfoExLogin();
    }

    internal override void SendSocket(ClientPacket defMsg, string message)
    {
        TextPackets.Add((defMsg, message ?? string.Empty));
        Sequence.Add(defMsg.Ident);
    }

    internal override void SendNativeLogonStateSnapshot(
        ClientPacket header, byte[] body)
    {
        BinaryPackets.Add((header, body));
        Sequence.Add(header.Ident);
    }
}
