using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.StartPointList = new List<TStartPoint>();
M2Share.SafeZoneList = new List<TSafeZoneArea>();

CheckWireLayouts();
CheckServiceTransitions();
CheckRecruitConditionTransitions();
CheckCorpsNoticeTransitions();
CheckMixedSocialPersistenceQueue();
CheckGildNoticeTransitions();
CheckDirectAddProtocol();
CheckExitPreconditions();
CheckExitSuccessAndAtomicFailure();
CheckProtocolBoundaries();
CheckCorpsNoticeProtocol();
CheckGuildCoreProtocol();
CheckGildNoticeProtocolFailures();
CheckGuildRelationTailProtocol();
CheckSourceContract();

Console.WriteLine(
    "NativeCorpsProtocolCheck PASS ids=4500-4632 records=8/56/64/48/32/24/60/64");
return;

static void CheckWireLayouts()
{
    Equal(8, NativeCorpsWireCodec.GuildIdSize, "TGuildID size");
    Equal(56, NativeCorpsWireCodec.GuildDescSize, "TGuildDesc size");
    Equal(64, NativeCorpsWireCodec.CorpsDescSize, "TCorpsDesc size");
    Equal(48, NativeCorpsWireCodec.CorpsMemberSize,
        "TCorpsMemDesc size");
    Equal(32, NativeCorpsWireCodec.CorpsRequestSize,
        "TCorpsRequests size");
    Equal(24, NativeCorpsWireCodec.MemberTitleSize,
        "TMemberTitle size");
    Equal(60, NativeCorpsWireCodec.RecruitConditionSize,
        "TRecruitCondition size");
    Equal(64, NativeCorpsWireCodec.LogDescSize, "TLogDesc size");

    const long id = 0x0102030405060708;
    var encodedId = NativeCorpsWireCodec.EncodeId(id);
    Equal(8, encodedId.Length, "encoded ID length");
    Require(NativeCorpsWireCodec.TryReadId(encodedId, out var decodedId)
            && decodedId == id, "ID little-endian round trip");
    Require(!NativeCorpsWireCodec.TryReadId(new byte[7], out _),
        "truncated ID accepted");

    var corps = BuildSnapshot().CorpsById[100];
    var description = NativeCorpsWireCodec.EncodeCorpsDescription(corps,
        "行会甲", "队长甲", 2);
    Equal(64, description.Length, "Corps description length");
    Equal(100L, BinaryPrimitives.ReadInt64LittleEndian(description),
        "Corps description ID");
    Equal("战队甲", ReadShortString(description, 8, 15),
        "Corps description name");
    Equal("行会甲", ReadShortString(description, 24, 15),
        "Corps description Gild name");
    Equal("队长甲", ReadShortString(description, 40, 15),
        "Corps description captain");
    Equal((byte)3, description[56], "Corps member count");
    Equal((byte)2, description[57], "Corps online count");

    var gild = BuildSnapshot().GildById[200];
    var gildDescription = NativeCorpsWireCodec.EncodeGuildDescription(gild,
        "会长甲", 37, 19);
    Equal(56, gildDescription.Length, "Gild description length");
    Equal("行会甲", ReadShortString(gildDescription, 8, 15),
        "Gild description name");
    Equal(37, BinaryPrimitives.ReadInt32LittleEndian(
        gildDescription.AsSpan(44, 4)), "Gild player count");
    Equal(19, BinaryPrimitives.ReadInt32LittleEndian(
        gildDescription.AsSpan(48, 4)), "Gild online count");

    var members = NativeCorpsWireCodec.EncodeCorpsMembers(new[]
    {
        (corps.Members[0], (byte)4, true)
    });
    Equal(48, members.Length, "member record length");
    Equal("队长甲", ReadShortString(members, 8, 15), "member name");
    Equal((byte)4, members[30], "member position");
    Equal("统领", ReadShortString(members, 31, 15), "member title");

    var requests = NativeCorpsWireCodec.EncodeCorpsRequests(new[]
    {
        new NativeCorpsActor(9, "申请甲", 35, 1, 2)
    });
    Equal(32, requests.Length, "request record length");
    Equal("申请甲", ReadShortString(requests, 8, 15), "request name");
    Equal((ushort)35, BinaryPrimitives.ReadUInt16LittleEndian(
        requests.AsSpan(24, 2)), "request level");

    var memberTitle = new byte[NativeCorpsWireCodec.MemberTitleSize];
    BinaryPrimitives.WriteInt64LittleEndian(memberTitle, 3);
    WriteShortString(memberTitle.AsSpan(8, 16), "新职务");
    Require(NativeCorpsWireCodec.TryDecodeMemberTitle(memberTitle,
            out var memberId, out var title) && memberId == 3
        && title == "新职务", "member title decode");
    Require(!NativeCorpsWireCodec.TryDecodeMemberTitle(new byte[23],
        out _, out _), "truncated member title accepted");

    var recruit = new byte[NativeCorpsWireCodec.RecruitConditionSize];
    recruit[0] = 5;
    BinaryPrimitives.WriteUInt16LittleEndian(recruit.AsSpan(2), 42);
    WriteShortString(recruit.AsSpan(4, 56), "仅限测试");
    Require(NativeCorpsWireCodec.TryDecodeRecruitCondition(recruit,
            out var condition) && condition.Jobs == 5
        && condition.Level == 42 && condition.Notice == "仅限测试",
        "recruit condition decode");
    Require(!NativeCorpsWireCodec.TryDecodeRecruitCondition(new byte[59],
        out _), "truncated recruit condition accepted");
    Require(!NativeCorpsWireCodec.TryDecodeRawText(new byte[] { 0x81 },
        out _), "invalid GBK accepted");

    var rawNotice = new NativeCorpsSnapshot
    {
        Notice = new byte[] { 0x81 }
    };
    _ = rawNotice.NoticeText;
    Require(rawNotice.Notice.SequenceEqual(new byte[] { 0x81 }),
        "Corps notice display decoding changed authoritative bytes");
    rawNotice.NoticeText = "公告";
    Require(rawNotice.Notice.SequenceEqual(
            HUtil32.GbkEncoding.GetBytes("公告")),
        "Corps notice display assignment did not encode GBK bytes");
    rawNotice.Notice = Enumerable.Range(0, 56)
        .Select(index => unchecked((byte)(0x80 + index))).ToArray();
    var rawRecruit = NativeCorpsWireCodec.EncodeRecruitCondition(rawNotice);
    Equal((byte)55, rawRecruit[4],
        "4531 raw notice short-string length");
    Require(rawRecruit.AsSpan(5, 55).SequenceEqual(
            rawNotice.Notice.AsSpan(0, 55)),
        "4531 raw notice was decoded or did not truncate at 55 bytes");

    var longName = new NativeCorpsSnapshot { Id = 300,
        Name = "12345678901234中" };
    var truncated = NativeCorpsWireCodec.EncodeCorpsDescription(longName,
        string.Empty, string.Empty, 0);
    Equal("12345678901234", ReadShortString(truncated, 8, 15),
        "GBK truncation split a multibyte character");
}

static void CheckServiceTransitions()
{
    Equal(30, NativeCorpsService.MaximumMembers,
        "original Corps member limit");

    var service = CreateService(BuildSnapshot());
    Equal(0, service.TransferCaptain(1, 2), "captain transfer");
    Require(service.TryGetCorps(100, out var transferred),
        "transferred Corps missing");
    Equal(2L, transferred.OwnerId, "new captain ID");
    Equal(0L, transferred.ViceOwner1Id,
        "new captain retained vice slot");
    Equal((byte)0, service.GetPosition(transferred, 1),
        "old captain did not become ordinary member");

    service = CreateService(BuildSnapshot());
    Equal(0, service.Exit(2), "vice captain exit");
    Require(service.TryGetCorps(100, out var afterExit),
        "Corps missing after vice exit");
    Equal(0L, afterExit.ViceOwner1Id, "vice slot not cleared on exit");
    Require(afterExit.Members.All(member => member.MemberId != 2),
        "vice captain remained a member after exit");

    service = CreateService(BuildSnapshot());
    Equal(0, service.StepDown(2), "vice captain stepdown");
    Require(service.TryGetCorps(100, out var afterStepDown),
        "Corps missing after stepdown");
    Equal(0L, afterStepDown.ViceOwner1Id,
        "vice slot not cleared on stepdown");
    Require(afterStepDown.Members.Any(member => member.MemberId == 2),
        "stepdown removed the member");

    service = CreateService(BuildSnapshot());
    Equal(0, service.DismissViceCaptain(2, 2),
        "vice captain self-demotion");
    Require(service.TryGetCorps(100, out var afterSelfDemotion),
        "Corps missing after self-demotion");
    Equal(0L, afterSelfDemotion.ViceOwner1Id,
        "vice slot not cleared on self-demotion");
    Require(afterSelfDemotion.Members.Any(member => member.MemberId == 2),
        "self-demotion removed the member");

    service = CreateService(BuildSnapshot());
    Equal(18, service.DismissViceCaptain(1, 0),
        "zero vice target error");
    Equal(NativeCorpsService.PermissionDenied,
        service.DismissViceCaptain(1, 1), "captain self-target error");

    var full = BuildSnapshot(30);
    service = CreateService(full);
    Equal(16, service.RequestJoin(
        new NativeCorpsActor(99, "申请满员", 50, 0, 0), 100),
        "full Corps request error");
    Equal(7, service.RequestJoin(
        new NativeCorpsActor(99, "申请不存在", 50, 0, 0), 999),
        "missing Corps request error");
    Equal(3, service.RequestJoin(
        new NativeCorpsActor(1, "已有成员", 50, 0, 0), 100),
        "existing member request error");

    service = CreateService(BuildSnapshot());
    var firstPage = service.GetCorpsPage(0, 1, out var result);
    Equal(0, result, "first page result");
    Equal(1, firstPage.Count, "first page count");
    var missingPage = service.GetCorpsPage(1, 1, out result);
    Equal(30, result, "out-of-range page result");
    Equal(0, missingPage.Count, "out-of-range page count");
    service.GetMemberPage(999, 0, 1, out result);
    Equal(5, result, "missing Corps member page result");
}

static void CheckRecruitConditionTransitions()
{
    var store = new FakeStore(BuildSnapshot());
    var service = CreateServiceFromStore(store);
    var first = new NativeCorpsRecruitCondition(0xff, 42, "招募条件甲");
    Equal(0, service.SetRecruitCondition(2, first),
        "4532 vice-captain update");
    var second = new NativeCorpsRecruitCondition(0x02, 55, "招募条件乙");
    Equal(0, service.SetRecruitCondition(1, second),
        "4532 captain update");
    WaitForCorpsPersistence(service, "4532 FIFO updates");

    Require(service.TryGetCorps(100, out var corps),
        "4532 Corps missing after update");
    Equal((byte)0x02, corps.RecruitJobSet, "4532 in-memory jobs");
    Equal((ushort)55, corps.RecruitLevelLimit,
        "4532 in-memory level");
    Equal("招募条件乙", corps.NoticeText, "4532 in-memory notice");
    Require(store.CorpsUpdates.SequenceEqual(new[]
        {
            ((byte)0x07, (ushort)42, "招募条件甲"),
            ((byte)0x02, (ushort)55, "招募条件乙")
        }), "4532 persistence was not FIFO or did not snapshot each write");
    Equal("招募条件乙", store.PersistedCorpsNotice,
        "4532 persisted notice");

    var calls = store.UpdateCorpsCalls;
    Equal(NativeCorpsService.PermissionDenied,
        service.SetRecruitCondition(3, second),
        "4532 ordinary-member result");
    Equal(NativeCorpsService.PermissionDenied,
        service.SetRecruitCondition(99, second),
        "4532 no-Corps result");
    Equal(NativeCorpsService.PermissionDenied,
        service.SetRecruitCondition(0, second),
        "4532 zero-ID result");
    Equal(calls, store.UpdateCorpsCalls,
        "4532 rejection reached persistence");

    store = new FakeStore(BuildSnapshot()) { FailUpdateCorps = true };
    service = CreateServiceFromStore(store);
    Equal(0, service.SetRecruitCondition(1,
        new NativeCorpsRecruitCondition(0x04, 60, "持久化失败")),
        "4532 asynchronous failure result");
    WaitForCorpsPersistence(service, "4532 failed update");
    Require(service.TryGetCorps(100, out corps),
        "4532 failed-write Corps missing");
    Equal((byte)0x04, corps.RecruitJobSet,
        "4532 DB failure rolled back in-memory jobs");
    Equal((ushort)60, corps.RecruitLevelLimit,
        "4532 DB failure rolled back in-memory level");
    Equal("持久化失败", corps.NoticeText,
        "4532 DB failure rolled back in-memory notice");
    Equal("公告甲", store.PersistedCorpsNotice,
        "4532 failed DB write changed persisted state");
    Equal((byte)0, store.PersistedCorpsJobs,
        "4532 failed DB write changed persisted jobs");
    Equal((ushort)1, store.PersistedCorpsLevel,
        "4532 failed DB write changed persisted level");

    store = new FakeStore(BuildSnapshot());
    store.BlockCorpsUpdates();
    service = CreateServiceFromStore(store);
    Equal(0, service.SetRecruitCondition(1,
        new NativeCorpsRecruitCondition(0x01, 70, "关服排空")),
        "4532 shutdown setup");
    Require(store.WaitForBlockedCorpsUpdate(TimeSpan.FromSeconds(5)),
        "4532 blocked persistence did not start");
    var shutdown = Task.Run(service.ShutdownAndDrainGildPersistence);
    Require(!shutdown.Wait(TimeSpan.FromMilliseconds(50)),
        "4532 shutdown returned before queued persistence completed");
    store.ReleaseCorpsUpdates();
    Require(shutdown.Wait(TimeSpan.FromSeconds(5)),
        "4532 shutdown drain timed out");
    Equal("关服排空", store.PersistedCorpsNotice,
        "4532 shutdown lost queued persistence");
}

static void CheckCorpsNoticeTransitions()
{
    var store = new FakeStore(BuildSnapshot());
    store.BlockCorpsUpdates();
    var service = CreateServiceFromStore(store);
    var splitGbk = Enumerable.Repeat((byte)0x41, 231).ToArray();
    splitGbk[0] = 0x27;
    splitGbk[100] = 0x27;
    splitGbk[229] = 0xd6;
    splitGbk[230] = 0xd0;
    Equal(0, service.SetNotice(1, splitGbk),
        "4539 231-byte result");
    Require(store.WaitForBlockedCorpsUpdate(TimeSpan.FromSeconds(5)),
        "4539 asynchronous persistence did not start");

    var splitExpected = splitGbk[..230];
    splitExpected[0] = 0x60;
    splitExpected[100] = 0x60;
    Require(service.TryGetCorps(100, out var corps)
            && corps.Notice.SequenceEqual(splitExpected),
        "4539 did not preserve a split GBK byte at the 230-byte boundary");

    var oversized = Enumerable.Repeat((byte)0x42, 501).ToArray();
    oversized[0] = 0x27;
    Equal(24, service.SetNotice(2, oversized),
        "4539 oversized result");
    var oversizedExpected = oversized[..230];
    oversizedExpected[0] = 0x60;
    Require(service.TryGetCorps(100, out corps)
            && corps.Notice.SequenceEqual(oversizedExpected),
        "4539 oversized write did not update memory");

    store.ReleaseCorpsUpdates();
    WaitForCorpsPersistence(service, "4539 FIFO writes");
    Require(store.CorpsUpdateNoticeBytes.Count == 2
            && store.CorpsUpdateNoticeBytes[0].SequenceEqual(splitExpected)
            && store.CorpsUpdateNoticeBytes[1]
                .SequenceEqual(oversizedExpected),
        "4539 persistence was not captured-snapshot FIFO");

    store = new FakeStore(BuildSnapshot());
    service = CreateServiceFromStore(store);
    Equal(0, service.SetNotice(1,
            new byte[] { 0x27, 0x81, 0, 0x41 }),
        "4539 malformed GBK result");
    var malformedExpected = new byte[] { 0x60, 0x81 };
    Require(service.TryGetCorps(100, out corps)
            && corps.Notice.SequenceEqual(malformedExpected),
        "4539 first-NUL or malformed GBK memory payload");
    WaitForCorpsPersistence(service, "4539 malformed GBK write");
    Require(store.LastCorpsNoticeBytes.SequenceEqual(malformedExpected),
        "4539 malformed GBK persistence payload");

    store = new FakeStore(BuildSnapshot()) { FailUpdateCorps = true };
    service = CreateServiceFromStore(store);
    var failedNotice = new byte[] { 0x43, 0x81 };
    Equal(0, service.SetNotice(1, failedNotice),
        "4539 asynchronous failure result");
    WaitForCorpsPersistence(service, "4539 failed write");
    Require(service.TryGetCorps(100, out corps)
            && corps.Notice.SequenceEqual(failedNotice),
        "4539 DB failure rolled back memory");
    Require(store.PersistedCorpsNoticeBytes.SequenceEqual(
            HUtil32.GbkEncoding.GetBytes("公告甲")),
        "4539 failed DB write changed persisted state");

    store = new FakeStore(BuildSnapshot());
    service = CreateServiceFromStore(store);
    Equal(NativeCorpsService.PermissionDenied,
        service.SetNotice(3, new byte[] { 0x41 }),
        "4539 ordinary-member result");
    Equal(5, service.SetNotice(999, new byte[] { 0x41 }),
        "4539 no-Corps result");
    Equal(0, store.UpdateCorpsCalls,
        "4539 rejection reached persistence");
}

static void CheckMixedSocialPersistenceQueue()
{
    CheckMixedSocialPersistenceQueueCase(false);
    CheckMixedSocialPersistenceQueueCase(true);
}

static void CheckMixedSocialPersistenceQueueCase(bool throwCorpsUpdate)
{
    var store = new FakeStore(BuildGildNoticeSnapshot())
    {
        FailUpdateCorps = !throwCorpsUpdate,
        ThrowUpdateCorps = throwCorpsUpdate
    };
    store.BlockGildUpdates();
    var service = CreateServiceFromStore(store);
    var mode = throwCorpsUpdate ? "exception" : "false";

    Equal(0, service.SetGildNotice(1, "混合队列前"),
        $"mixed FIFO {mode} first Gild result");
    Require(store.WaitForBlockedGildUpdate(TimeSpan.FromSeconds(5)),
        $"mixed FIFO {mode} first Gild did not block");
    Equal(0, service.SetRecruitCondition(1,
        new NativeCorpsRecruitCondition(0x04, 61, "混合队列Corps")),
        $"mixed FIFO {mode} Corps result");
    Equal(0, service.SetGildNotice(1, "混合队列后"),
        $"mixed FIFO {mode} second Gild result");

    store.ReleaseGildUpdates();
    WaitForGildPersistence(service, $"mixed FIFO {mode}");
    Require(store.SocialPersistenceEvents.SequenceEqual(new[]
        {
            "Gild:混合队列前",
            "Corps:混合队列Corps",
            "Gild:混合队列后"
        }), $"mixed FIFO {mode} call order or exception continuation");
    Equal(2, store.UpdateGildCalls,
        $"mixed FIFO {mode} Gild call count");
    Equal(1, store.UpdateCorpsCalls,
        $"mixed FIFO {mode} Corps call count");
    Equal("混合队列后", store.PersistedGildNotice,
        $"mixed FIFO {mode} second Gild persistence");
    Equal("公告甲", store.PersistedCorpsNotice,
        $"mixed FIFO {mode} failed Corps persistence side effect");
}

static void CheckGildNoticeTransitions()
{
    var snapshot = BuildGildNoticeSnapshot();
    var store = new FakeStore(snapshot);
    var service = CreateServiceFromStore(store);
    var boundaryNotice = "'" + new string('中', 99) + "a";
    Equal(NativeCorpsService.MaximumGildNoticeBytes,
        HUtil32.GbkEncoding.GetByteCount(boundaryNotice),
        "Gild notice boundary fixture");
    Equal(0, service.SetGildNotice(1, boundaryNotice),
        "Gild owner notice update");
    WaitForGildPersistence(service, "Gild owner notice update");
    var sanitizedBoundary = "`" + new string('中', 99) + "a";
    Equal(1, store.UpdateGildCalls,
        "Gild owner persistence count");
    Equal(sanitizedBoundary, store.LastGildNotice,
        "Gild owner persistence payload");
    Equal(sanitizedBoundary, store.PersistedGildNotice,
        "Gild owner persisted notice");
    Require(service.TryGetGildForPlayer(1, out var gild),
        "Gild owner lookup after update");
    Equal(sanitizedBoundary, HUtil32.GbkEncoding.GetString(gild.Notice),
        "Gild owner in-memory notice");

    snapshot = BuildGildNoticeSnapshot();
    store = new FakeStore(snapshot);
    service = CreateServiceFromStore(store);
    Equal(0, service.SetGildNotice(10, "副会长公告"),
        "Gild vice-owner notice update");
    WaitForGildPersistence(service, "Gild vice-owner notice update");
    Equal(1, store.UpdateGildCalls,
        "Gild vice-owner persistence count");
    Equal("副会长公告", store.PersistedGildNotice,
        "Gild vice-owner persisted notice");

    snapshot = BuildGildNoticeSnapshot();
    store = new FakeStore(snapshot);
    service = CreateServiceFromStore(store);
    foreach (var playerId in new[] { 2L, 3L, 20L, 21L })
        Equal(NativeCorpsService.PermissionDenied,
            service.SetGildNotice(playerId, "越权公告"),
            $"Gild notice permission for player {playerId}");
    Equal(0, store.UpdateGildCalls,
        "unauthorized Gild notice persistence count");
    Equal("行会公告", HUtil32.GbkEncoding.GetString(
            snapshot.GildById[200].Notice),
        "unauthorized Gild notice side effect");

    snapshot = BuildGildNoticeSnapshot();
    store = new FakeStore(snapshot);
    service = CreateServiceFromStore(store);
    Equal(5, service.SetGildNotice(999, "无战队公告"),
        "Gild notice missing Corps result");
    Equal(0, store.UpdateGildCalls,
        "missing Corps Gild notice persistence count");

    snapshot = BuildSnapshot();
    snapshot.GildById.Clear();
    store = new FakeStore(snapshot);
    service = CreateServiceFromStore(store);
    Equal(5, service.SetGildNotice(1, "无行会公告"),
        "Gild notice missing Gild outer result");
    Equal(0, store.UpdateGildCalls,
        "missing Gild notice persistence count");

    snapshot = BuildGildNoticeSnapshot();
    store = new FakeStore(snapshot);
    service = CreateServiceFromStore(store);
    Equal(24, service.SetGildNotice(1, new string('中', 101)),
        "oversized Gild notice result");
    Equal(0, store.UpdateGildCalls,
        "oversized Gild notice persistence count");
    Equal("行会公告", HUtil32.GbkEncoding.GetString(
            snapshot.GildById[200].Notice),
        "oversized Gild notice side effect");

    snapshot = BuildGildNoticeSnapshot();
    store = new FakeStore(snapshot) { FailUpdateGild = true };
    service = CreateServiceFromStore(store);
    Equal(0, service.SetGildNotice(1, "持久化失败"),
        "Gild notice persistence failure result");
    Equal("持久化失败", HUtil32.GbkEncoding.GetString(
            snapshot.GildById[200].Notice),
        "Gild notice memory is updated before persistence");
    WaitForGildPersistence(service, "failed Gild notice update");
    Equal(1, store.UpdateGildCalls,
        "failed Gild notice persistence count");
    Equal("行会公告", store.PersistedGildNotice,
        "failed Gild notice persisted side effect");
    Equal("持久化失败", HUtil32.GbkEncoding.GetString(
            snapshot.GildById[200].Notice),
        "failed Gild notice must not roll back memory");

    snapshot = BuildGildNoticeSnapshot();
    store = new FakeStore(snapshot);
    store.BlockGildUpdates();
    service = CreateServiceFromStore(store);
    Equal(0, service.SetGildNotice(1, "第一条"),
        "first queued Gild notice result");
    Require(store.WaitForBlockedGildUpdate(TimeSpan.FromSeconds(5)),
        "Gild persistence worker did not start");
    Equal(0, service.SetGildNotice(1, "第二条"),
        "service lock was held by Gild persistence");
    Require(service.TryGetGildForPlayer(1, out gild),
        "Gild lookup while persistence is blocked");
    Equal("第二条", HUtil32.GbkEncoding.GetString(gild.Notice),
        "second Gild notice memory update");
    store.ReleaseGildUpdates();
    WaitForGildPersistence(service, "FIFO Gild notice updates");
    Require(store.GildUpdateNotices.SequenceEqual(
            new[] { "第一条", "第二条" }),
        "Gild persistence is not captured-snapshot FIFO");

    snapshot = BuildGildNoticeSnapshot();
    store = new FakeStore(snapshot);
    service = CreateServiceFromStore(store);
    Equal(0, service.SetGildNotice(1,
            new byte[] { 0x41, 0, 0x81, 0x27 }),
        "Gild notice first-NUL update");
    WaitForGildPersistence(service, "Gild notice first-NUL update");
    Require(store.LastGildNoticeBytes.SequenceEqual(new byte[] { 0x41 }),
        "Gild notice did not stop at the first NUL");
    Require(service.TryGetGildForPlayer(1, out gild)
            && gild.Notice.SequenceEqual(new byte[] { 0x41 }),
        "Gild notice first-NUL in-memory payload");

    snapshot = BuildGildNoticeSnapshot();
    store = new FakeStore(snapshot);
    store.BlockGildUpdates();
    service = CreateServiceFromStore(store);
    Equal(0, service.SetGildNotice(1, "关服第一条"),
        "shutdown first queued Gild notice");
    Require(store.WaitForBlockedGildUpdate(TimeSpan.FromSeconds(5)),
        "shutdown Gild persistence worker did not start");
    Equal(0, service.SetGildNotice(1, "关服第二条"),
        "shutdown second queued Gild notice");
    var shutdown = Task.Run(service.ShutdownAndDrainGildPersistence);
    Require(!shutdown.Wait(TimeSpan.FromMilliseconds(100)),
        "shutdown returned before the blocked Gild write completed");
    store.ReleaseGildUpdates();
    Require(shutdown.Wait(TimeSpan.FromSeconds(5)),
        "shutdown did not drain pending Gild writes");
    Require(store.GildUpdateNotices.SequenceEqual(
            new[] { "关服第一条", "关服第二条" }),
        "shutdown did not preserve Gild persistence FIFO");
}

static void CheckExitPreconditions()
{
    CheckRejectedExit(37, safe: false, fight: true,
        "non-safe zone must win over fight map");
    CheckRejectedExit(28, safe: true, fight: true,
        "fight map member exit");
    CheckRejectedExit(29, safe: true, fight: false,
        "free-PK member exit during castle war", underWar: true,
        freePk: true);
    CheckRejectedExit(29, safe: true, fight: false,
        "castle-area member exit during castle war", underWar: true,
        castleArea: true);

    var store = new FakeStore(BuildSnapshot());
    var service = CreateServiceFromStore(store);
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = BuildExitPlayer(99, safe: true, fight: true, packets,
        service);
    Require(InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage { wIdent = Grobal2.CM_CORPS_EXIT }),
        "4538 non-member request was not routed");
    Equal(1, packets.Count, "4538 non-member response count");
    Equal((ushort)5, packets[0].Header.Param,
        "fight-map check ran without a Corps membership");
    Equal(0, store.UpdateCorpsCalls,
        "non-member rejection updated Corps storage");
    Equal(0, store.DeleteMemberCalls,
        "non-member rejection deleted Corps storage");
    Equal(0, store.ExitMemberCalls,
        "non-member rejection entered the exit transaction");
}

static void CheckDirectAddProtocol()
{
    var store = new FakeStore(BuildSnapshot());
    var service = CreateServiceFromStore(store);
    var sourcePackets = new List<(ClientPacket Header, byte[] Body)>();
    var targetPackets = new List<(ClientPacket Header, byte[] Body)>();
    var target = BuildDirectAddPlayer(99, "直加目标", service,
        targetPackets, allowGuild: true);
    var source = BuildDirectAddPlayer(1, "队长甲", service,
        sourcePackets, allowGuild: false, targetResolver: () => target);

    Require(InvokeHandler(source, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_CORPS_DIRECT_ADD_MEMBER
        }), "4533 success request was not routed");
    Equal(1, sourcePackets.Count, "4533 success response count");
    Require(sourcePackets[0].Header.Ident ==
            Grobal2.SM_CORPS_DIRECT_ADD_MEMBER
            && sourcePackets[0].Header.Param == 0,
        "4533 success response header");
    Require(service.TryGetPlayerCorps(99, out var joined)
            && joined.Id == 100
            && joined.Members.Any(member => member.MemberId == 99),
        "4533 did not add the target to the Corps");
    Equal(1, store.InsertMemberCalls, "4533 insert count");
    Require(store.IsMemberPersisted(99),
        "4533 did not persist the new member");
    Equal(2, targetPackets.Count, "4533 target refresh count");
    Require(targetPackets[0].Header.Ident == Grobal2.SM_PLAYER_CORPS
            && targetPackets[0].Header.Param == 0
            && targetPackets[0].Body.Length ==
            NativeCorpsWireCodec.CorpsDescSize,
        "4533 target 4501 refresh");
    Require(targetPackets[1].Header.Ident == Grobal2.SM_PLAYER_GILD
            && targetPackets[1].Header.Param == 0
            && targetPackets[1].Body.Length ==
            NativeCorpsWireCodec.GuildDescSize,
        "4533 target 4500 refresh");

    CheckDirectAddResult(BuildSnapshot(), 99, 50, true, 5,
        "source without Corps");
    CheckDirectAddResult(BuildSnapshot(), 1, null, true, 22,
        "missing facing target");
    CheckDirectAddResult(BuildSnapshot(), 1, 3, true, 3,
        "target already in Corps");
    CheckDirectAddResult(BuildSnapshot(), 1, 99, false, 35,
        "target disallows Corps join");
    CheckDirectAddResult(BuildSnapshot(), 3, 99, true,
        NativeCorpsService.PermissionDenied, "ordinary member strategy");
    CheckDirectAddResult(BuildSnapshot(30), 1, 99, true, 16,
        "full Corps");

    store = new FakeStore(BuildSnapshot()) { FailInsertMember = true };
    service = CreateServiceFromStore(store);
    sourcePackets.Clear();
    targetPackets.Clear();
    target = BuildDirectAddPlayer(99, "写入失败目标", service,
        targetPackets, allowGuild: true);
    source = BuildDirectAddPlayer(1, "队长甲", service,
        sourcePackets, allowGuild: false, targetResolver: () => target);
    InvokeHandler(source, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_CORPS_DIRECT_ADD_MEMBER
        });
    Equal((ushort)NativeCorpsService.UnknownError,
        sourcePackets.Single().Header.Param, "4533 insert failure result");
    Require(!service.TryGetPlayerCorps(99, out _)
            && !store.IsMemberPersisted(99),
        "4533 insert failure changed membership");
    Equal(0, targetPackets.Count,
        "4533 insert failure refreshed target state");
}

static void CheckDirectAddResult(NativeCorpsDataSnapshot snapshot,
    long sourceId, long? targetId, bool allowGuild, int expectedResult,
    string context)
{
    var store = new FakeStore(snapshot);
    var service = CreateServiceFromStore(store);
    var sourcePackets = new List<(ClientPacket Header, byte[] Body)>();
    var targetPackets = new List<(ClientPacket Header, byte[] Body)>();
    var target = targetId.HasValue
        ? BuildDirectAddPlayer(targetId.Value, "4533目标", service,
            targetPackets, allowGuild)
        : null;
    var source = BuildDirectAddPlayer(sourceId, "4533操作者", service,
        sourcePackets, allowGuild: false, targetResolver: () => target);

    InvokeHandler(source, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_CORPS_DIRECT_ADD_MEMBER
        });
    Equal(1, sourcePackets.Count, "4533 " + context + " response count");
    Require(sourcePackets[0].Header.Ident ==
            Grobal2.SM_CORPS_DIRECT_ADD_MEMBER
            && sourcePackets[0].Header.Param ==
            unchecked((ushort)expectedResult),
        "4533 " + context + " result");
    Equal(0, store.InsertMemberCalls,
        "4533 " + context + " persistence side effect");
    Equal(0, targetPackets.Count,
        "4533 " + context + " target refresh side effect");
}

static TPlayObject BuildDirectAddPlayer(long playerId, string name,
    NativeCorpsService service,
    List<(ClientPacket Header, byte[] Body)> packets, bool allowGuild,
    Func<TPlayObject> targetResolver = null)
{
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = name,
        m_boAllowGuild = allowGuild
    };
    player.LoadNativeMailRecipientId(playerId);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)), targetResolver);
    return player;
}

static void CheckExitSuccessAndAtomicFailure()
{
    var store = new FakeStore(BuildSnapshot());
    var service = CreateServiceFromStore(store);
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = BuildExitPlayer(2, safe: true, fight: false, packets,
        service);
    InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage { wIdent = Grobal2.CM_CORPS_EXIT });

    Equal(3, packets.Count, "4538 success refresh count");
    Require(packets[0].Header.Ident == Grobal2.SM_CORPS_EXIT
            && packets[0].Header.Param == 0,
        "4538 success response");
    Require(packets[1].Header.Ident == Grobal2.SM_PLAYER_CORPS
            && packets[1].Header.Param == 5,
        "4538 success 4501 refresh");
    Require(packets[2].Header.Ident == 4628
            && packets[2].Header.Param == 0
            && packets[2].Header.Tag == 0
            && packets[2].Body.Length == 0,
        "4538 success 4628 role refresh");
    Equal(1, store.ExitMemberCalls, "4538 atomic exit count");
    Equal(0L, store.PersistedViceOwner1Id,
        "4538 did not persist vice removal");
    Require(!store.IsMemberPersisted(2),
        "4538 did not persist member removal");

    store = new FakeStore(BuildSnapshot()) { FailExitMember = true };
    service = CreateServiceFromStore(store);
    packets.Clear();
    player = BuildExitPlayer(2, safe: true, fight: false, packets, service);
    InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage { wIdent = Grobal2.CM_CORPS_EXIT });

    Equal(1, packets.Count, "4538 failed transaction response count");
    Require(packets[0].Header.Ident == Grobal2.SM_CORPS_EXIT
            && packets[0].Header.Param ==
            NativeCorpsService.UnknownError,
        "4538 failed transaction result");
    Require(service.TryGetPlayerCorps(2, out var corps)
            && corps.ViceOwner1Id == 2
            && corps.Members.Any(member => member.MemberId == 2),
        "4538 failed transaction changed in-memory membership");
    Equal(2L, store.PersistedViceOwner1Id,
        "4538 failed transaction changed persisted vice");
    Require(store.IsMemberPersisted(2),
        "4538 failed transaction changed persisted membership");
}

static void CheckRejectedExit(int expectedResult, bool safe, bool fight,
    string context, bool underWar = false, bool freePk = false,
    bool castleArea = false)
{
    var store = new FakeStore(BuildSnapshot());
    var service = CreateServiceFromStore(store);
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = BuildExitPlayer(2, safe, fight, packets, service);
    player.m_boInFreePKArea = freePk;
    var previousCastleManager = M2Share.CastleManager;
    if (underWar)
    {
        M2Share.CastleManager = BuildCastleManager(player.m_PEnvir,
            castleArea);
    }

    try
    {
        Require(InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
            new TProcessMessage { wIdent = Grobal2.CM_CORPS_EXIT }),
            context + " was not routed");
    }
    finally
    {
        M2Share.CastleManager = previousCastleManager;
    }
    Equal(1, packets.Count, context + " response count");
    Equal((ushort)Grobal2.SM_CORPS_EXIT, packets[0].Header.Ident,
        context + " response Ident");
    Equal(unchecked((ushort)expectedResult), packets[0].Header.Param,
        context + " result");
    Equal(0, packets[0].Body.Length, context + " response body");

    Require(service.TryGetPlayerCorps(2, out var corps),
        context + " removed the member");
    Equal(2L, corps.ViceOwner1Id, context + " cleared vice captain");
    Require(corps.Members.Any(member => member.MemberId == 2),
        context + " mutated the member list");
    Equal(0, store.UpdateCorpsCalls,
        context + " updated Corps storage");
    Equal(0, store.DeleteMemberCalls,
        context + " deleted Corps storage");
    Equal(0, store.ExitMemberCalls,
        context + " entered the exit transaction");
    Equal(0, service.GetLogPage(2, 1, 0, 20, out var logResult).Count,
        context + " appended a Corps log");
    Equal(30, logResult, context + " changed the empty-log result");
}

static CastleManager BuildCastleManager(Envirnoment playerEnvironment,
    bool includePlayerEnvironment)
{
    var manager = new CastleManager();
    var castle = (TUserCastle)System.Runtime.CompilerServices.RuntimeHelpers
        .GetUninitializedObject(typeof(TUserCastle));
    castle.m_boUnderWar = true;
    castle.m_EnvirList = new List<string>();
    if (includePlayerEnvironment)
    {
        castle.m_MapPalace = playerEnvironment;
    }
    var field = typeof(CastleManager).GetField("_castleList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Castle list field missing");
    var castles = (IList<TUserCastle>)field.GetValue(manager);
    castles.Add(castle);
    return manager;
}

static TPlayObject BuildExitPlayer(long playerId, bool safe, bool fight,
    List<(ClientPacket Header, byte[] Body)> packets,
    NativeCorpsService service)
{
    var environment = new Envirnoment { sMapName = "CORPS_EXIT_TEST" };
    environment.Flag.boSAFE = safe;
    environment.Flag.boFightZone = fight;
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "退出测试",
        m_PEnvir = environment
    };
    player.LoadNativeMailRecipientId(playerId);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));
    return player;
}

static void CheckProtocolBoundaries()
{
    var store = new FakeStore(BuildSnapshot());
    var service = CreateServiceFromStore(store);
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = new TPlayObject { m_boOffLineFlag = true,
        m_sCharName = "队长甲" };
    player.LoadNativeMailRecipientId(1);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));

    var header = TPlayObject.BuildNativeCorpsHeader(4537, 7, 1000, 2, 3);
    Equal((ushort)4537, header.Ident, "header Ident");
    Equal(7, header.Recog, "header Recog");
    Equal((ushort)1000, header.Param, "header Param/result");
    Equal((ushort)2, header.Tag, "header Tag");
    Equal((ushort)3, header.Series, "header Series");

    var plaintextOnly = new TProcessMessage
    {
        wIdent = Grobal2.CM_CORPS_REQUEST_JOIN,
        sMsg = "d\0\0\0\0\0\0\0",
        Payload = Array.Empty<byte>()
    };
    Require(InvokeHandler(player, "TryHandleNativeCorpsCoreProtocol",
        plaintextOnly), "4522 was not routed");
    Equal(0, packets.Count, "plaintext sMsg was treated as binary Payload");

    var list = new TProcessMessage
    {
        wIdent = Grobal2.CM_CORPS_LIST,
        nParam2 = 0,
        nParam3 = 1,
        Payload = Array.Empty<byte>()
    };
    Require(InvokeHandler(player, "TryHandleNativeCorpsCoreProtocol", list),
        "4520 was not routed");
    Equal(1, packets.Count, "4520 response count");
    Equal((ushort)4520, packets[0].Header.Ident, "4520 response Ident");
    Equal(0, packets[0].Header.Recog, "4520 response page/Recog");
    Equal((ushort)0, packets[0].Header.Param, "4520 response result");
    Equal((ushort)1, packets[0].Header.Tag, "4520 response capacity");
    Equal((ushort)1, packets[0].Header.Series, "4520 response count");
    Equal(64, packets[0].Body.Length, "4520 response body");

    packets.Clear();
    list.nParam3 = NativeCorpsService.MaximumPageSize + 1;
    InvokeHandler(player, "TryHandleNativeCorpsCoreProtocol", list);
    Equal(0, packets.Count, "oversized page request produced a response");

    var recruit = new TProcessMessage
    {
        wIdent = Grobal2.CM_CORPS_SET_RECRUIT_CONDITION,
        Payload = new byte[NativeCorpsWireCodec.RecruitConditionSize - 1]
    };
    Require(InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
        recruit), "4532 truncated body was not routed");
    Equal(0, packets.Count, "4532 truncated body produced a response");
    Equal(0, store.UpdateCorpsCalls,
        "4532 truncated body reached persistence");

    recruit.Payload = NativeCorpsWireCodec.EncodeRecruitCondition(
        new NativeCorpsSnapshot
        {
            RecruitJobSet = 0x07,
            RecruitLevelLimit = 35,
            NoticeText = "协议招募条件"
        });
    InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol", recruit);
    Equal(1, packets.Count, "4532 valid body response count");
    Equal((ushort)4532, packets[0].Header.Ident,
        "4532 valid body response Ident");
    Equal((ushort)0, packets[0].Header.Param,
        "4532 valid body response result");
    WaitForCorpsPersistence(service, "4532 protocol write");
    Equal(1, store.UpdateCorpsCalls,
        "4532 valid body persistence count");
    packets.Clear();

    Equal(0, service.RequestJoin(
        new NativeCorpsActor(11, "申请甲", 40, 0, 0), 100),
        "first batch setup");
    Equal(0, service.RequestJoin(
        new NativeCorpsActor(12, "申请乙", 40, 0, 1), 100),
        "second batch setup");
    var batch = new TProcessMessage
    {
        wIdent = Grobal2.CM_CORPS_ACCEPT_REQUEST,
        nParam2 = 2,
        Payload = NativeCorpsWireCodec.EncodeId(11)
    };
    Require(InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
        batch), "4535 was not routed");
    Equal(0, packets.Count, "truncated batch was partially processed");

    batch.Payload = NativeCorpsWireCodec.EncodeId(11)
        .Concat(NativeCorpsWireCodec.EncodeId(12)).ToArray();
    InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol", batch);
    Equal(2, packets.Count, "valid batch response count");
    Require(packets.All(packet => packet.Header.Ident == 4535
        && packet.Header.Param == 0 && packet.Body.Length == 8),
        "valid batch response ABI");
}

static void CheckCorpsNoticeProtocol()
{
    var store = new FakeStore(BuildSnapshot());
    var service = CreateServiceFromStore(store);
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "队长甲"
    };
    player.LoadNativeMailRecipientId(1);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));

    Require(InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_CORPS_NOTICE,
            nParam3 = 7,
            Payload = new byte[] { 0x27, 0x81, 0, 0x41 }
        }), "4539 malformed write was not routed");
    Require(packets.Single().Header.Ident == Grobal2.SM_CORPS_NOTICE
            && packets[0].Header.Param == 0
            && packets[0].Header.Tag == 7
            && packets[0].Body.Length == 0,
        "4539 malformed write response ABI");
    WaitForCorpsPersistence(service, "4539 protocol malformed write");

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_CORPS_GET_RECRUIT_CONDITION,
            Payload = NativeCorpsWireCodec.EncodeId(100)
        });
    Require(packets.Single().Header.Ident
                == Grobal2.SM_CORPS_GET_RECRUIT_CONDITION
            && packets[0].Header.Param == 0
            && packets[0].Body.Length
                == NativeCorpsWireCodec.RecruitConditionSize
            && packets[0].Body[4] == 2
            && packets[0].Body.AsSpan(5, 2).SequenceEqual(
                new byte[] { 0x60, 0x81 }),
        "4531 did not return the malformed raw notice as a full packet");

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_CORPS_NOTICE,
            nParam3 = 0,
            Payload = Array.Empty<byte>()
        });
    Require(packets.Single().Header.Param == 0
            && packets[0].Header.Tag == 0
            && packets[0].Body.SequenceEqual(
                new byte[] { 0x60, 0x81 }),
        "4539 query did not return authoritative raw bytes");

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_CORPS_NOTICE,
            nParam3 = 9,
            Payload = Enumerable.Repeat((byte)0x41, 501).ToArray()
        });
    Require(packets.Single().Header.Param == 24
            && packets[0].Header.Tag == 9,
        "4539 oversized write response ABI");
    WaitForCorpsPersistence(service, "4539 protocol oversized write");
    Require(service.TryGetCorps(100, out var corps)
            && corps.Notice.Length == 230,
        "4539 oversized protocol write was not applied");

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeCorpsAdminProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_CORPS_NOTICE,
            nParam3 = 1,
            Payload = Array.Empty<byte>()
        });
    Require(packets.Single().Header.Param == 0,
        "4539 empty write result");
    WaitForCorpsPersistence(service, "4539 protocol empty write");
    Require(service.TryGetCorps(100, out corps)
            && corps.Notice.Length == 0,
        "4539 empty write was not applied");
}

static void CheckGuildCoreProtocol()
{
    var store = new FakeStore(BuildSnapshot());
    var service = CreateServiceFromStore(store);
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "队长甲"
    };
    player.LoadNativeMailRecipientId(1);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));

    Require(InvokeHandler(player, "TryHandleNativeGuildCoreProtocol",
        new TProcessMessage { wIdent = Grobal2.CM_PLAYER_GILD }),
        "4500 was not routed");
    Equal(1, packets.Count, "4500 response count");
    Equal((ushort)4500, packets[0].Header.Ident, "4500 response Ident");
    Equal((ushort)0, packets[0].Header.Param, "4500 response result");
    Equal(56, packets[0].Body.Length, "4500 response body");
    Equal("行会甲", ReadShortString(packets[0].Body, 8, 15),
        "4500 Gild name");
    Equal("队长甲", ReadShortString(packets[0].Body, 24, 15),
        "4500 president name");
    Equal(3, BinaryPrimitives.ReadInt32LittleEndian(
        packets[0].Body.AsSpan(44, 4)), "4500 player count");

    packets.Clear();
    var list = new TProcessMessage
    {
        wIdent = Grobal2.CM_GILD_LIST,
        nParam2 = 0,
        nParam3 = 1
    };
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol", list);
    Equal(1, packets.Count, "4562 response count");
    Require(packets[0].Header.Recog == 0
            && packets[0].Header.Param == 0
            && packets[0].Header.Tag == 1
            && packets[0].Header.Series == 1,
        "4562 first-page response header");
    Equal(56, packets[0].Body.Length, "4562 response body");

    packets.Clear();
    list.nParam2 = 1;
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol", list);
    Require(packets.Single().Header.Recog == 1
            && packets[0].Header.Param == 30
            && packets[0].Header.Tag == 1
            && packets[0].Header.Series == 0,
        "4562 exhausted-page response header");
    Equal(0, packets[0].Body.Length, "4562 exhausted-page body");

    packets.Clear();
    list.nParam3 = NativeCorpsService.MaximumPageSize + 1;
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol", list);
    Equal(0, packets.Count, "oversized 4562 request produced a response");

    const long gildId = 200;
    var join = new TProcessMessage
    {
        wIdent = Grobal2.CM_GILD_REQUEST_JOIN,
        Payload = NativeCorpsWireCodec.EncodeId(gildId)
    };
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol", join);
    Equal((ushort)1000, packets.Single().Header.Param,
        "4560 unsupported result");
    Equal(gildId, BinaryPrimitives.ReadInt64LittleEndian(
        packets[0].Body), "4560 echoed ID");

    packets.Clear();
    join.Payload = new byte[7];
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol", join);
    Equal(0, packets.Count, "truncated 4560 ID produced a response");

    var notice = new TProcessMessage
    {
        wIdent = Grobal2.CM_GILD_NOTICE,
        nParam3 = 0
    };
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol", notice);
    Equal((ushort)0, packets.Single().Header.Param,
        "4563 query result");
    Equal("行会公告", HUtil32.GbkEncoding.GetString(packets[0].Body),
        "4563 query body");

    packets.Clear();
    notice.nParam3 = 0x4321;
    notice.Payload = HUtil32.GbkEncoding.GetBytes("新'公告");
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol", notice);
    Require(packets.Single().Header.Param == 0
            && packets[0].Header.Tag == 0x4321
            && packets[0].Header.Series == 0,
        "4563 write success and mode echo");
    Equal(0, packets[0].Body.Length, "4563 write response body");
    WaitForGildPersistence(service, "4563 protocol write");
    Equal(1, store.UpdateGildCalls, "4563 write persistence count");
    Equal("新`公告", store.PersistedGildNotice,
        "4563 write persistence payload");

    packets.Clear();
    notice.nParam3 = 0;
    notice.Payload = Array.Empty<byte>();
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol", notice);
    Equal((ushort)0, packets.Single().Header.Param,
        "4563 updated query result");
    Equal("新`公告", HUtil32.GbkEncoding.GetString(packets[0].Body),
        "4563 updated query body");

    packets.Clear();
    player.LoadNativeMailRecipientId(2);
    notice.nParam3 = 7;
    notice.Payload = HUtil32.GbkEncoding.GetBytes("越权公告");
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol", notice);
    Require(packets.Single().Header.Param
            == NativeCorpsService.PermissionDenied
            && packets[0].Header.Tag == 7,
        "4563 permission failure and mode echo");
    Equal(1, store.UpdateGildCalls,
        "4563 permission failure persistence side effect");
    player.LoadNativeMailRecipientId(1);

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol",
        new TProcessMessage { wIdent = Grobal2.CM_GILD_QUERY_CORPS });
    Require(packets.Single().Header.Param == 0
            && packets[0].Header.Tag == 1,
        "4565 response header");
    Equal(64, packets[0].Body.Length, "4565 response body");

    foreach (var ident in new[]
             {
                 Grobal2.CM_GILD_DISMISS_CORPS,
                 Grobal2.CM_GILD_TRANSFER_PRESIDENT,
                 Grobal2.CM_GILD_APPOINT_VICE_PRESIDENT
             })
    {
        packets.Clear();
        InvokeHandler(player, "TryHandleNativeGuildCoreProtocol",
            new TProcessMessage
            {
                wIdent = ident,
                Payload = NativeCorpsWireCodec.EncodeId(100)
            });
        Require(packets.Single().Header.Ident == ident
                && packets[0].Header.Param == 1000
                && packets[0].Body.Length == 0,
            ident + " unsupported response ABI");
    }

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol",
        new TProcessMessage { wIdent = Grobal2.CM_GILD_CREATE });
    Equal((ushort)1000, packets.Single().Header.Param,
        "4564 unsupported result");

    packets.Clear();
    Require(!InvokeHandler(player, "TryHandleNativeGuildCoreProtocol",
            new TProcessMessage
            {
                wIdent = Grobal2.CM_GILD_QUERY_PRESIDENT
            }),
        "4566 must not be claimed");
    Equal(0, packets.Count, "4566 produced a response");
}

static void CheckGildNoticeProtocolFailures()
{
    CheckGildNoticeProtocolResult(BuildSnapshot(), 999, 0x105, "无战队",
        5, "missing Corps");

    var noGild = BuildSnapshot();
    noGild.GildById.Clear();
    CheckGildNoticeProtocolResult(noGild, 1, 0x10c, "无行会", 5,
        "missing Gild outer check");
    var noGildQueryStore = new FakeStore(noGild);
    var noGildQueryService = CreateServiceFromStore(noGildQueryStore);
    var noGildQueryPackets =
        new List<(ClientPacket Header, byte[] Body)>();
    var noGildQueryPlayer = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "无行会查询"
    };
    noGildQueryPlayer.LoadNativeMailRecipientId(1);
    noGildQueryPlayer.SetNativeCorpsServiceForTests(noGildQueryService,
        (header, body) => noGildQueryPackets.Add((header, body)));
    InvokeHandler(noGildQueryPlayer, "TryHandleNativeGuildCoreProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_GILD_NOTICE,
            nParam3 = 0
        });
    Require(noGildQueryPackets.Single().Header.Param == 12
            && noGildQueryPackets[0].Header.Tag == 0,
        "4563 missing Gild query result");
    Equal(0, noGildQueryPackets[0].Body.Length,
        "4563 missing Gild query body");
    Equal(0, noGildQueryStore.UpdateGildCalls,
        "4563 missing Gild query persistence side effect");

    noGildQueryPackets.Clear();
    noGildQueryPlayer.LoadNativeMailRecipientId(999);
    InvokeHandler(noGildQueryPlayer, "TryHandleNativeGuildCoreProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_GILD_NOTICE,
            nParam3 = 0
        });
    Require(noGildQueryPackets.Single().Header.Param == 12
            && noGildQueryPackets[0].Header.Tag == 0,
        "4563 missing Corps query result");
    Equal(0, noGildQueryPackets[0].Body.Length,
        "4563 missing Corps query body");

    CheckGildNoticeProtocolResult(BuildSnapshot(), 1, 0x118,
        new string('中', 101), 24, "oversized notice");
    CheckGildNoticeProtocolResult(BuildSnapshot(), 2, 0x22b, "越权公告",
        NativeCorpsService.PermissionDenied, "permission denied");

    var store = new FakeStore(BuildSnapshot());
    var service = CreateServiceFromStore(store);
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "队长甲"
    };
    player.LoadNativeMailRecipientId(1);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_GILD_NOTICE,
            nParam3 = 9,
            Payload = new byte[] { 0x81 }
        });
    Require(packets.Single().Header.Param == 0
            && packets[0].Header.Tag == 9,
        "4563 raw AnsiString response header");
    WaitForGildPersistence(service, "4563 raw AnsiString write");
    Equal(1, store.UpdateGildCalls,
        "4563 raw AnsiString persistence count");
    Require(store.LastGildNoticeBytes.SequenceEqual(new byte[] { 0x81 }),
        "4563 raw AnsiString persistence bytes");

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_GILD_NOTICE,
            nParam3 = 0
        });
    Require(packets.Single().Header.Param == 0
            && packets[0].Body.SequenceEqual(new byte[] { 0x81 }),
        "4563 raw AnsiString query bytes");
}

static void CheckGildNoticeProtocolResult(
    NativeCorpsDataSnapshot snapshot, long playerId, int mode,
    string notice, int expectedResult, string context)
{
    var store = new FakeStore(snapshot);
    var service = CreateServiceFromStore(store);
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "4563测试"
    };
    player.LoadNativeMailRecipientId(playerId);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));
    InvokeHandler(player, "TryHandleNativeGuildCoreProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_GILD_NOTICE,
            nParam3 = mode,
            Payload = HUtil32.GbkEncoding.GetBytes(notice)
        });
    Equal(1, packets.Count, $"4563 {context} response count");
    Require(packets[0].Header.Ident == Grobal2.SM_GILD_NOTICE
            && packets[0].Header.Param == expectedResult
            && packets[0].Header.Tag == mode
            && packets[0].Header.Series == 0,
        $"4563 {context} response header");
    Equal(0, packets[0].Body.Length,
        $"4563 {context} response body");
    Equal(0, store.UpdateGildCalls,
        $"4563 {context} persistence side effect");
}

static void CheckGuildRelationTailProtocol()
{
    var service = CreateService(BuildSnapshot());
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "队长甲"
    };
    player.LoadNativeMailRecipientId(1);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));

    Require(InvokeHandler(player, "TryHandleNativeGuildRelationProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_GILD_QUERY_REQUEST_JOIN_LIST,
            nParam3 = NativeCorpsService.MaximumPageSize + 1  // Tag=nParam3 = oversized page size
        }), "4570 was not routed");
    Equal(1, packets.Count, "oversized 4570 page did not send an empty page");
    Equal(0, packets[0].Body.Length, "oversized 4570 page was not empty");

    const long requestId = 200;
    foreach (var (ident, handler) in new[]
             {
                 (Grobal2.CM_GILD_REFUSE_REQUEST,
                     "TryHandleNativeGuildRelationProtocol"),
                 (Grobal2.CM_GILD_ACCEPT_REQUEST,
                     "TryHandleNativeGuildTailProtocol")
             })
    {
        packets.Clear();
        Require(InvokeHandler(player, handler, new TProcessMessage
        {
            wIdent = ident,
            nParam1 = 2,
            Payload = NativeCorpsWireCodec.EncodeId(requestId)
        }), ident + " was not routed");
        var packet = packets.Single();
        Require(packet.Header.Ident == ident
                && packet.Header.Recog == 2
                && packet.Header.Param == 1000,
            ident + " decision response header");
        Equal(requestId, BinaryPrimitives.ReadInt64LittleEndian(
            packet.Body), ident + " echoed ID");
    }

    foreach (var (requestIdent, responseIdent) in new[]
             {
                 (Grobal2.CM_GILD_DECLARE_WAR_NAME,
                     Grobal2.SM_GILD_DECLARE_WAR),
                 (Grobal2.CM_GILD_CONCERN_GILD_NAME,
                     Grobal2.SM_GILD_CONCERN_GILD_ID)
             })
    {
        packets.Clear();
        InvokeHandler(player, "TryHandleNativeGuildRelationProtocol",
            new TProcessMessage
            {
                wIdent = requestIdent,
                Payload = HUtil32.GbkEncoding.GetBytes("行会甲")
            });
        var packet = packets.Single();
        Require(packet.Header.Ident == responseIdent
                && packet.Header.Param == 1000,
            requestIdent + " aliased response");
    }

    foreach (var ident in new[]
             {
                 Grobal2.CM_FIND_CORPS_BYNAME,
                 Grobal2.CM_FIND_GILD_BYNAME
             })
    {
        foreach (var body in new[]
                 {
                     Array.Empty<byte>(),
                     new byte[] { 0 }
                 })
        {
            packets.Clear();
            InvokeHandler(player, "TryHandleNativeGuildTailProtocol",
                new TProcessMessage { wIdent = ident, Payload = body });
            Equal(0, packets.Count,
                ident + " empty-name query produced a response");
        }
    }

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeGuildTailProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_FIND_CORPS_BYNAME,
            Payload = HUtil32.GbkEncoding.GetBytes("队甲")
        });
    Require(packets.Single().Header.Ident == Grobal2.SM_FIND_CORPS_BYNAME
            && packets[0].Header.Series == 1,
        "4616 fuzzy response header");
    Equal(64, packets[0].Body.Length, "4616 fuzzy response body");

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeGuildTailProtocol",
        new TProcessMessage
        {
            wIdent = Grobal2.CM_FIND_GILD_BYNAME,
            Payload = HUtil32.GbkEncoding.GetBytes("会甲")
        });
    Require(packets.Single().Header.Ident == Grobal2.SM_FIND_GILD_BYNAME
            && packets[0].Header.Series == 1,
        "4617 fuzzy response header");
    Equal(56, packets[0].Body.Length, "4617 fuzzy response body");

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeGuildTailProtocol",
        new TProcessMessage { wIdent = Grobal2.CM_REFRESH_CORPSINFO });
    Require(packets.Single().Header.Ident == Grobal2.SM_REFRESH_CORPSINFO
            && packets[0].Header.Param == 0,
        "4631 refresh response header");
    Equal(64, packets[0].Body.Length, "4631 refresh response body");

    packets.Clear();
    InvokeHandler(player, "TryHandleNativeGuildTailProtocol",
        new TProcessMessage { wIdent = Grobal2.CM_REFRESH_GILDINFO });
    Require(packets.Single().Header.Ident == Grobal2.SM_REFRESH_GILDINFO
            && packets[0].Header.Param == 0,
        "4632 refresh response header");
    Equal(56, packets[0].Body.Length, "4632 refresh response body");
}

static void CheckSourceContract()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeCorpsProtocol.cs"));
    Require(source.Contains(
        "DecodeNativeSocialBody(processMessage.Payload)",
        StringComparison.Ordinal),
        "Corps body is 6-bit-decoded (via DecodeNativeSocialBody) before use");
    Require(!source.Contains("processMessage.sMsg", StringComparison.Ordinal),
        "Corps handler reads original plaintext sMsg");
    var decodeSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeSocialDecode.cs"));
    Require(decodeSource.Contains(
        "Misc.Decode6BitBufDirect(raw, textLength)", StringComparison.Ordinal),
        "shared social decoder performs the raw 6-bit decode (no GBK)");
    Require(source.Contains(
        "var count = unchecked((ushort)processMessage.nParam2);",
        StringComparison.Ordinal), "batch count is not Param");
    Require(source.Contains("if (body.Length < required) return;",
        StringComparison.Ordinal), "batch length guard missing");
    Require(source.Contains("SendSocket(header, body);",
        StringComparison.Ordinal), "binary response transport missing");
    var directProtocolStart = source.IndexOf(
        "private void DirectAddNativeCorpsMember", StringComparison.Ordinal);
    var directResolverStart = source.IndexOf(
        "private TPlayObject ResolveNativeCorpsDirectTarget",
        directProtocolStart, StringComparison.Ordinal);
    Require(directProtocolStart >= 0
            && directResolverStart > directProtocolStart,
        "4533 protocol method boundary missing");
    var directProtocolSource = source[
        directProtocolStart..directResolverStart];
    var targetCorpsRefresh = directProtocolSource.IndexOf(
        "target.SendNativePlayerCorps", StringComparison.Ordinal);
    var targetGildRefresh = directProtocolSource.IndexOf(
        "target.SendNativePlayerGuild", StringComparison.Ordinal);
    var operatorResult = directProtocolSource.IndexOf(
        "SendNativeCorpsStatus(Grobal2.SM_CORPS_DIRECT_ADD_MEMBER",
        StringComparison.Ordinal);
    Require(targetCorpsRefresh >= 0
            && targetGildRefresh > targetCorpsRefresh
            && operatorResult > targetGildRefresh,
        "4533 target refresh/operator response order changed");

    var guildSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.NativeGuildCoreProtocol.cs"));
    var wireSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Services", "NativeCorpsWireCodec.cs"));
    var serviceSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Services", "NativeCorpsService.cs"));
    var recruitEncodeStart = wireSource.IndexOf(
        "internal static byte[] EncodeRecruitCondition",
        StringComparison.Ordinal);
    var corpsDescriptionStart = wireSource.IndexOf(
        "internal static byte[] EncodeCorpsDescription",
        recruitEncodeStart, StringComparison.Ordinal);
    Require(recruitEncodeStart >= 0
            && corpsDescriptionStart > recruitEncodeStart,
        "4531 encoder method boundary missing");
    var recruitEncodeSource = wireSource[
        recruitEncodeStart..corpsDescriptionStart];
    Require(recruitEncodeSource.Contains("WriteRawShortString",
                StringComparison.Ordinal)
            && recruitEncodeSource.Contains("corps.Notice",
                StringComparison.Ordinal)
            && !recruitEncodeSource.Contains("NoticeText",
                StringComparison.Ordinal),
        "4531 encoder decodes or re-encodes authoritative notice bytes");
    var storeSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Services", "NativeCorpsStore.cs"));
    var exitServiceStart = serviceSource.IndexOf("internal int Exit(",
        StringComparison.Ordinal);
    var logPageStart = serviceSource.IndexOf(
        "internal IReadOnlyList<NativeCorpsLogEntry>", exitServiceStart,
        StringComparison.Ordinal);
    Require(exitServiceStart >= 0 && logPageStart > exitServiceStart,
        "4538 service method boundary missing");
    var exitServiceSource = serviceSource[exitServiceStart..logPageStart];
    Require(exitServiceSource.Contains("_store.TryExitMember",
                StringComparison.Ordinal)
            && !exitServiceSource.Contains("_store.TryUpdateCorps",
                StringComparison.Ordinal)
            && !exitServiceSource.Contains("_store.TryDeleteMember",
                StringComparison.Ordinal),
        "4538 vice/member persistence is not one store transaction");
    var exitStoreStart = storeSource.IndexOf(
        "public bool TryExitMember", StringComparison.Ordinal);
    var titleStoreStart = storeSource.IndexOf(
        "public bool TryUpdateMemberTitle", exitStoreStart,
        StringComparison.Ordinal);
    Require(exitStoreStart >= 0 && titleStoreStart > exitStoreStart,
        "4538 store method boundary missing");
    var exitStoreSource = storeSource[exitStoreStart..titleStoreStart];
    Require(exitStoreSource.Contains("BeginTransaction",
                StringComparison.Ordinal)
            && exitStoreSource.Contains("CreateUpdateCorpsCommand",
                StringComparison.Ordinal)
            && exitStoreSource.Contains("DELETE FROM gamedata.CorpsMember",
                StringComparison.Ordinal)
            && exitStoreSource.Contains("transaction.Commit()",
                StringComparison.Ordinal),
        "4538 store update/delete transaction contract missing");
    var recruitStart = serviceSource.IndexOf(
        "internal int SetRecruitCondition", StringComparison.Ordinal);
    var noticeStart = serviceSource.IndexOf("internal int SetNotice",
        recruitStart, StringComparison.Ordinal);
    Require(recruitStart >= 0 && noticeStart > recruitStart,
        "4532 service method boundary missing");
    var recruitSource = serviceSource[recruitStart..noticeStart];
    Require(recruitSource.Contains("operatorId == 0",
            StringComparison.Ordinal)
            && recruitSource.Contains("return PermissionDenied",
                StringComparison.Ordinal),
        "4532 no-Corps/member strategy result is not locked to 555");
    Require(recruitSource.Contains("_persistence.Enqueue(corps)",
            StringComparison.Ordinal)
            && !recruitSource.Contains("_store.TryUpdateCorps",
                StringComparison.Ordinal),
        "4532 is not an in-memory-first asynchronous write");
    var gildNoticeStart = serviceSource.IndexOf(
        "internal int SetGildNotice", noticeStart, StringComparison.Ordinal);
    Require(gildNoticeStart > noticeStart,
        "4539 service method boundary missing");
    var corpsNoticeSource = serviceSource[noticeStart..gildNoticeStart];
    Require(corpsNoticeSource.Contains(
                "MaximumCorpsNoticeInputBytes", StringComparison.Ordinal)
            && corpsNoticeSource.Contains(
                "MaximumCorpsNoticeStoredBytes", StringComparison.Ordinal)
            && corpsNoticeSource.Contains("value == 0x27",
                StringComparison.Ordinal)
            && corpsNoticeSource.Contains("_persistence.Enqueue(corps)",
                StringComparison.Ordinal)
            && !corpsNoticeSource.Contains("_store.TryUpdateCorps",
                StringComparison.Ordinal),
        "4539 raw normalization or asynchronous write contract missing");
    var noticeProtocolStart = source.IndexOf(
        "private void HandleNativeCorpsNotice", StringComparison.Ordinal);
    var actorStart = source.IndexOf(
        "private NativeCorpsActor CaptureNativeCorpsActor",
        noticeProtocolStart, StringComparison.Ordinal);
    Require(noticeProtocolStart >= 0 && actorStart > noticeProtocolStart,
        "4539 protocol method boundary missing");
    var noticeProtocolSource = source[noticeProtocolStart..actorStart];
    Require(noticeProtocolSource.Contains("corps.Notice",
                StringComparison.Ordinal)
            && noticeProtocolSource.Contains("GetNativeCorpsBody",
                StringComparison.Ordinal)
            && !noticeProtocolSource.Contains("TryDecodeRawText",
                StringComparison.Ordinal),
        "4539 protocol does not preserve raw notice bytes");
    Require(storeSource.Contains(
                "AddBinary(command, \"@notice\", corps.Notice)",
                StringComparison.Ordinal)
            && storeSource.Contains("Notice = ReadBinary(reader, 9)",
                StringComparison.Ordinal),
        "4539 persistence does not preserve raw notice bytes");
    var noticeSources = guildSource + serviceSource;
    Require(noticeSources.Contains("SetGildNotice", StringComparison.Ordinal),
        "4563 write service call missing");
    Require(serviceSource.Contains(
            "position != 3 && position != 4", StringComparison.Ordinal),
        "4563 Gild owner/vice-owner policy missing");
    // Strip whole-line comments first: NativeCorpsService.cs:238 documents the native
    // equivalent ("AssociationManager.Run() line ~159") and a raw substring scan read that
    // prose as if it were a legacy model reference. The assertion is otherwise unchanged and
    // still bites on real code (verified by mutation: putting `Association.Get(` back into
    // either file re-trips it).
    var noticeCode = StripLineComments(noticeSources);
    Require(!noticeCode.Contains("Association", StringComparison.Ordinal)
            && !noticeCode.Contains("GuildManager", StringComparison.Ordinal),
        "4563 write chain uses the legacy Guild model");
}

static string StripLineComments(string source)
{
    var builder = new System.Text.StringBuilder(source.Length);
    foreach (var line in source.Split('\n'))
    {
        // Only whole-line comments are dropped; a trailing comment on a code line would
        // require real tokenising, and no assertion here depends on one.
        if (!line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            builder.Append(line);
        builder.Append('\n');
    }
    return builder.ToString();
}

static NativeCorpsService CreateService(NativeCorpsDataSnapshot snapshot)
{
    return CreateServiceFromStore(new FakeStore(snapshot));
}

static NativeCorpsService CreateServiceFromStore(FakeStore store)
{
    Require(NativeCorpsService.TryCreate(store,
        out var service, out var error), "service creation: " + error);
    return service;
}

static NativeCorpsDataSnapshot BuildSnapshot(int memberCount = 3)
{
    var snapshot = new NativeCorpsDataSnapshot();
    var corps = new NativeCorpsSnapshot
    {
        Id = 100,
        CreateTime = new DateTime(2020, 1, 2),
        Name = "战队甲",
        OwnerId = 1,
        ViceOwner1Id = memberCount >= 2 ? 2 : 0,
        RecruitLevelLimit = 1,
        RecruitJobSet = 0,
        NoticeText = "公告甲"
    };
    for (var index = 1; index <= memberCount; index++)
    {
        corps.Members.Add(new NativeCorpsMemberSnapshot
        {
            MemberId = index,
            Name = index == 1 ? "队长甲" : "成员" + index,
            Level = 50,
            Sex = unchecked((byte)(index % 2)),
            Job = unchecked((byte)(index % 3)),
            Title = index == 1 ? "统领" : string.Empty,
            LastLoginTime = new DateTime(2020, 1, 2)
        });
    }
    snapshot.CorpsById.Add(corps.Id, corps);

    var gild = new NativeGildSnapshot
    {
        Id = 200,
        CreateTime = new DateTime(2020, 1, 2),
        Name = "行会甲",
        OwnerCorpsId = corps.Id,
        Notice = HUtil32.GbkEncoding.GetBytes("行会公告")
    };
    gild.CorpsIds.Add(corps.Id);
    snapshot.GildById.Add(gild.Id, gild);
    return snapshot;
}

static NativeCorpsDataSnapshot BuildGildNoticeSnapshot()
{
    var snapshot = BuildSnapshot();
    var viceOwnerCorps = new NativeCorpsSnapshot
    {
        Id = 101,
        CreateTime = new DateTime(2020, 1, 2),
        Name = "战队乙",
        OwnerId = 10
    };
    viceOwnerCorps.Members.Add(new NativeCorpsMemberSnapshot
    {
        MemberId = 10,
        Name = "副会长甲",
        Level = 50,
        LastLoginTime = new DateTime(2020, 1, 2)
    });
    viceOwnerCorps.Members.Add(new NativeCorpsMemberSnapshot
    {
        MemberId = 11,
        Name = "成员十一",
        Level = 50,
        LastLoginTime = new DateTime(2020, 1, 2)
    });
    snapshot.CorpsById.Add(viceOwnerCorps.Id, viceOwnerCorps);

    var ordinaryCorps = new NativeCorpsSnapshot
    {
        Id = 102,
        CreateTime = new DateTime(2020, 1, 2),
        Name = "战队丙",
        OwnerId = 20,
        ViceOwner1Id = 21
    };
    ordinaryCorps.Members.Add(new NativeCorpsMemberSnapshot
    {
        MemberId = 20,
        Name = "队长丙",
        Level = 50,
        LastLoginTime = new DateTime(2020, 1, 2)
    });
    ordinaryCorps.Members.Add(new NativeCorpsMemberSnapshot
    {
        MemberId = 21,
        Name = "副队长丙",
        Level = 50,
        LastLoginTime = new DateTime(2020, 1, 2)
    });
    snapshot.CorpsById.Add(ordinaryCorps.Id, ordinaryCorps);

    var gild = snapshot.GildById[200];
    gild.ViceOwnerId = viceOwnerCorps.Id;
    gild.CorpsIds.Add(viceOwnerCorps.Id);
    gild.CorpsIds.Add(ordinaryCorps.Id);
    return snapshot;
}

static bool InvokeHandler(TPlayObject player, string name,
    TProcessMessage message)
{
    var method = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(name + " missing");
    // Decode-bug sweep: GateService delivers Payload = the 6-bit-ENCODED wire body and the
    // corps/gild handler now decodes it (GetNativeCorpsBody -> DecodeNativeSocialBody). Every
    // handler-drive in this audit funnels through here (the codec unit-tests call the codecs
    // directly and stay untouched), so encode the RAW fixture body to the production-faithful
    // wire form at this ONE chokepoint. The 6-bit round-trip is exact
    // (DecodeNativeSocialBody(EncodeBuffer(x)) == x, no length change, interior/trailing bytes
    // preserved), so every asserted outcome/length/byte stays UNCHANGED; only the input becomes
    // faithful. Restore the original Payload afterwards so a reused message never double-encodes.
    var originalPayload = message.Payload;
    if (originalPayload is byte[] raw && raw.Length != 0)
        message.Payload = EDcode.EncodeBuffer(raw);
    try
    {
        return (bool)method.Invoke(player, new object[] { message });
    }
    finally
    {
        message.Payload = originalPayload;
    }
}

static string ReadShortString(byte[] body, int offset, int capacity)
{
    Require(body.Length >= offset + capacity + 1,
        "short-string record truncated");
    var length = body[offset];
    Require(length <= capacity, "short-string length exceeds capacity");
    return HUtil32.GbkEncoding.GetString(body, offset + 1, length);
}

static void WriteShortString(Span<byte> destination, string value)
{
    var bytes = HUtil32.GbkEncoding.GetBytes(value);
    Require(bytes.Length < destination.Length,
        "test short-string is too long");
    destination.Clear();
    destination[0] = unchecked((byte)bytes.Length);
    bytes.CopyTo(destination[1..]);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             { Environment.CurrentDirectory, AppContext.BaseDirectory })
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

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void WaitForGildPersistence(NativeCorpsService service,
    string context)
{
    Require(service.WaitForGildPersistenceForTests(TimeSpan.FromSeconds(5)),
        context + " persistence timeout");
}

static void WaitForCorpsPersistence(NativeCorpsService service,
    string context)
{
    Require(service.WaitForCorpsPersistenceForTests(TimeSpan.FromSeconds(5)),
        context + " persistence timeout");
}

sealed class FakeStore : INativeCorpsStore
{
    private readonly NativeCorpsDataSnapshot _snapshot;
    private readonly object _socialSync = new();
    private readonly List<string> _socialPersistenceEvents = new();
    private readonly object _corpsSync = new();
    private readonly List<(byte Jobs, ushort Level, string Notice)>
        _corpsUpdates = new();
    private readonly List<byte[]> _corpsUpdateNoticeBytes = new();
    private readonly ManualResetEventSlim _corpsUpdateEntered = new(false);
    private readonly ManualResetEventSlim _corpsUpdateRelease = new(true);
    private readonly object _gildSync = new();
    private readonly List<string> _gildUpdateNotices = new();
    private readonly ManualResetEventSlim _gildUpdateEntered = new(false);
    private readonly ManualResetEventSlim _gildUpdateRelease = new(true);

    internal int DeleteMemberCalls { get; private set; }
    internal int InsertMemberCalls { get; private set; }
    internal int ExitMemberCalls { get; private set; }
    internal int UpdateCorpsCalls { get; private set; }
    internal int UpdateGildCalls { get; private set; }
    internal bool FailUpdateCorps { get; set; }
    internal bool ThrowUpdateCorps { get; set; }
    internal bool FailUpdateGild { get; set; }
    internal bool FailInsertMember { get; set; }
    internal bool FailExitMember { get; set; }
    internal long PersistedViceOwner1Id { get; private set; }
    internal long PersistedViceOwner2Id { get; private set; }
    internal byte PersistedCorpsJobs { get; private set; }
    internal ushort PersistedCorpsLevel { get; private set; }
    internal string PersistedCorpsNotice { get; private set; }
    internal byte[] PersistedCorpsNoticeBytes { get; private set; } =
        Array.Empty<byte>();
    internal byte[] LastCorpsNoticeBytes { get; private set; } =
        Array.Empty<byte>();
    internal IReadOnlyList<(byte Jobs, ushort Level, string Notice)>
        CorpsUpdates
    {
        get
        {
            lock (_corpsSync) return _corpsUpdates.ToArray();
        }
    }
    internal IReadOnlyList<byte[]> CorpsUpdateNoticeBytes
    {
        get
        {
            lock (_corpsSync)
                return _corpsUpdateNoticeBytes
                    .Select(value => (byte[])value.Clone()).ToArray();
        }
    }
    internal IReadOnlyList<string> SocialPersistenceEvents
    {
        get
        {
            lock (_socialSync) return _socialPersistenceEvents.ToArray();
        }
    }
    internal string LastGildNotice { get; private set; } = string.Empty;
    internal byte[] LastGildNoticeBytes { get; private set; } =
        Array.Empty<byte>();
    internal string PersistedGildNotice { get; private set; }
    private readonly HashSet<long> _persistedMembers = new();

    internal bool IsMemberPersisted(long memberId)
    {
        lock (_corpsSync) return _persistedMembers.Contains(memberId);
    }
    internal IReadOnlyList<string> GildUpdateNotices
    {
        get
        {
            lock (_gildSync) return _gildUpdateNotices.ToArray();
        }
    }

    internal FakeStore(NativeCorpsDataSnapshot snapshot)
    {
        _snapshot = snapshot;
        var corps = snapshot.CorpsById.TryGetValue(100, out var primaryCorps)
            ? primaryCorps
            : snapshot.CorpsById.Values.FirstOrDefault();
        PersistedCorpsJobs = corps?.RecruitJobSet ?? 0;
        PersistedCorpsLevel = corps?.RecruitLevelLimit ?? 0;
        PersistedViceOwner1Id = corps?.ViceOwner1Id ?? 0;
        PersistedViceOwner2Id = corps?.ViceOwner2Id ?? 0;
        foreach (var member in snapshot.CorpsById.Values
                     .SelectMany(value => value.Members))
            _persistedMembers.Add(member.MemberId);
        PersistedCorpsNoticeBytes = (byte[])(corps?.Notice
            ?? Array.Empty<byte>()).Clone();
        PersistedCorpsNotice = HUtil32.GbkEncoding.GetString(
            PersistedCorpsNoticeBytes);
        var notice = snapshot.GildById.Values.SingleOrDefault()?.Notice
                     ?? Array.Empty<byte>();
        PersistedGildNotice = HUtil32.GbkEncoding.GetString(notice);
    }

    internal void BlockCorpsUpdates()
    {
        _corpsUpdateEntered.Reset();
        _corpsUpdateRelease.Reset();
    }

    internal bool WaitForBlockedCorpsUpdate(TimeSpan timeout) =>
        _corpsUpdateEntered.Wait(timeout);

    internal void ReleaseCorpsUpdates() => _corpsUpdateRelease.Set();

    internal void BlockGildUpdates()
    {
        _gildUpdateEntered.Reset();
        _gildUpdateRelease.Reset();
    }

    internal bool WaitForBlockedGildUpdate(TimeSpan timeout) =>
        _gildUpdateEntered.Wait(timeout);

    internal void ReleaseGildUpdates() => _gildUpdateRelease.Set();

    private void RecordSocialPersistence(string value)
    {
        lock (_socialSync) _socialPersistenceEvents.Add(value);
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
        InsertMemberCalls++;
        if (FailInsertMember)
        {
            error = "injected CorpsMember insert failure";
            return false;
        }
        lock (_corpsSync) _persistedMembers.Add(member.MemberId);
        error = string.Empty;
        return true;
    }

    public bool TryDeleteMember(long memberId, out string error)
    {
        DeleteMemberCalls++;
        lock (_corpsSync) _persistedMembers.Remove(memberId);
        error = string.Empty;
        return true;
    }

    public bool TryExitMember(long memberId, NativeCorpsSnapshot corps,
        bool updateCorps, out string error)
    {
        ExitMemberCalls++;
        if (FailExitMember)
        {
            error = "injected atomic Corps exit failure";
            return false;
        }
        lock (_corpsSync)
        {
            if (updateCorps)
            {
                PersistedViceOwner1Id = corps.ViceOwner1Id;
                PersistedViceOwner2Id = corps.ViceOwner2Id;
            }
            _persistedMembers.Remove(memberId);
        }
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
        _corpsUpdateEntered.Set();
        if (!_corpsUpdateRelease.Wait(TimeSpan.FromSeconds(5)))
        {
            error = "timed out waiting for injected Corps update release";
            return false;
        }
        var notice = (byte[])(corps.Notice ?? Array.Empty<byte>()).Clone();
        var noticeText = HUtil32.GbkEncoding.GetString(notice);
        RecordSocialPersistence("Corps:" + noticeText);
        lock (_corpsSync)
        {
            UpdateCorpsCalls++;
            _corpsUpdates.Add((corps.RecruitJobSet,
                corps.RecruitLevelLimit, noticeText));
            _corpsUpdateNoticeBytes.Add(notice);
            LastCorpsNoticeBytes = (byte[])notice.Clone();
            if (ThrowUpdateCorps)
                throw new InvalidOperationException(
                    "injected Corps update exception");
            if (FailUpdateCorps)
            {
                error = "injected Corps update failure";
                return false;
            }
            PersistedCorpsJobs = corps.RecruitJobSet;
            PersistedCorpsLevel = corps.RecruitLevelLimit;
            PersistedViceOwner1Id = corps.ViceOwner1Id;
            PersistedViceOwner2Id = corps.ViceOwner2Id;
            PersistedCorpsNoticeBytes = (byte[])notice.Clone();
            PersistedCorpsNotice = noticeText;
            error = string.Empty;
            return true;
        }
    }

    public bool TryUpdateGild(NativeGildSnapshot gild, out string error)
    {
        _gildUpdateEntered.Set();
        if (!_gildUpdateRelease.Wait(TimeSpan.FromSeconds(5)))
        {
            error = "timed out waiting for injected Gild update release";
            return false;
        }
        RecordSocialPersistence("Gild:" +
            HUtil32.GbkEncoding.GetString(gild.Notice));
        lock (_gildSync)
        {
            UpdateGildCalls++;
            LastGildNoticeBytes = (byte[])gild.Notice.Clone();
            LastGildNotice = HUtil32.GbkEncoding.GetString(gild.Notice);
            _gildUpdateNotices.Add(LastGildNotice);
            if (FailUpdateGild)
            {
                error = "injected Gild update failure";
                return false;
            }
            PersistedGildNotice = LastGildNotice;
            error = string.Empty;
            return true;
        }
    }
}
