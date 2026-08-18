using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;

PrepareRuntimeConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();

var state = new NativeType2SecondaryRankingState();

Check(state.Consume(new byte[NativeType2SecondaryRankingState.HeaderSize - 1])
      == NativeType2SecondaryRankingResult.Ignored,
    "short secondary header");
Check(state.Consume(Packet(0, Array.Empty<byte>(), command: 0x006A))
      == NativeType2SecondaryRankingResult.Ignored,
    "other secondary command");
Check(state.Consume(Packet(-1, new byte[] { 1 }))
      == NativeType2SecondaryRankingResult.Ignored
      && state.Consume(Packet(NativeType2SecondaryRankingState.BucketCount,
          new byte[] { 2 })) == NativeType2SecondaryRankingResult.Ignored,
    "secondary category bounds");

var empty = Packet(0, Array.Empty<byte>(), param2: -1);
Check(state.Consume(empty) == NativeType2SecondaryRankingResult.RecordAppended
      && state.TotalRecordCount == 1
      && state.GetBucket(0).Count == 1
      && state.GetBucket(0)[0].CopyBody().Length == 0,
    "secondary zero-length record");

var body = Enumerable.Range(0, 0x118)
    .Select(index => unchecked((byte)(0x30 + index))).ToArray();
var last = Packet(NativeType2SecondaryRankingState.BucketCount - 1,
    body, param2: unchecked((int)0x87654321));
Check(state.Consume(last) == NativeType2SecondaryRankingResult.RecordAppended
      && state.TotalRecordCount == 2
      && state.GetBucket(13).Count == 1
      && state.GetBucket(13)[0].CopyBody().AsSpan().SequenceEqual(body),
    "secondary category 13 arbitrary body");
body[0] = 0;
last[NativeType2SecondaryRankingState.HeaderSize + 1] = 0;
Check(state.GetBucket(13)[0].CopyBody()[0] == 0x30
      && state.GetBucket(13)[0].CopyBody()[1] == 0x31,
    "secondary record deep copy");

var topSeven = new byte[7 * 24];
var levels = new uint[] { 998, 999, uint.MaxValue, 0, 1000, 1, 999 };
for (var row = 0; row < levels.Length; row++)
    BinaryPrimitives.WriteUInt32LittleEndian(
        topSeven.AsSpan(row * 24 + 16, sizeof(uint)), levels[row]);
Check(state.Consume(Packet(3, topSeven))
      == NativeType2SecondaryRankingResult.RecordAppended
      && state.TotalRecordCount == 3,
    "secondary bucket 3 top-seven record");

var finalize = Packet(NativeType2SecondaryRankingState.FinalizeCategory,
    new byte[] { 0xAA, 0xBB }, param2: unchecked((int)0xABCDC0DE));
Check(state.Consume(finalize)
      == NativeType2SecondaryRankingResult.BatchFinalized
      && state.TotalRecordCount == 3
      && state.LastFinalizeValue == 0xC0DE
      && state.Level999OrHigherCount == 4,
    "secondary finalize low word and level-999 count");

var clear = Packet(999, new byte[] { 1, 2, 3 }, command:
    NativeType2SecondaryRankingState.ClearCommand);
Check(state.Consume(clear) == NativeType2SecondaryRankingResult.BucketsCleared
      && state.TotalRecordCount == 0
      && state.LastFinalizeValue == 0xC0DE
      && state.Level999OrHigherCount == 4
      && Enumerable.Range(0, NativeType2SecondaryRankingState.BucketCount)
          .All(category => state.GetBucket(category).Count == 0),
    "secondary clear preserves finalized fields");
Check(state.Consume(Packet(
          NativeType2SecondaryRankingState.FinalizeCategory,
          Array.Empty<byte>()))
      == NativeType2SecondaryRankingResult.BatchFinalized
      && state.Level999OrHigherCount == 0,
    "secondary empty finalize recomputes level-999 count");

CheckThrows<ArgumentOutOfRangeException>(() => state.GetBucket(-1),
    "negative secondary bucket lookup");
CheckThrows<ArgumentOutOfRangeException>(() => state.GetBucket(
        NativeType2SecondaryRankingState.BucketCount),
    "oversized secondary bucket lookup");

CheckPageSelection();
CheckQuestOrderResponse();
CheckNativeHonorRecipientLookup();
CheckTopSevenOnlineNotifications();

Console.WriteLine("PASS NativeType2SecondaryRankingCheck " +
                  "commands=0069/0074 buckets=0..13 body=opaque " +
                  "quest-order=1060/order-list=1108 page-clamp/my-rank " +
                  "finalize=100/top7-online-notify/type18-broadcast/" +
                  "param2-low16/level999-count lifetime=process");

static void CheckPageSelection()
{
    var pageState = new NativeType2SecondaryRankingState();
    var first = Pattern(168, 0x11);
    var last = Pattern(168, 0x22);
    var hero = Pattern(280, 0x44);
    Check(pageState.Consume(Packet(0, first)) ==
          NativeType2SecondaryRankingResult.RecordAppended
          && pageState.Consume(Packet(0, last)) ==
          NativeType2SecondaryRankingResult.RecordAppended
          && pageState.Consume(Packet(4, hero)) ==
          NativeType2SecondaryRankingResult.RecordAppended,
        "ranking page setup");

    var page = -1;
    Check(pageState.TryCopyPage(0, ref page, out var length, out var body)
          && page == 1 && length == 168 && body.AsSpan().SequenceEqual(last),
        "negative page clamps to last normal page");
    body[0] = 0;
    page = int.MaxValue;
    Check(pageState.TryCopyPage(0, ref page, out length, out body)
          && page == 1 && length == 168 && body[0] == 0x22,
        "high page clamp and returned deep copy");
    page = 0;
    Check(pageState.TryCopyPage(4, ref page, out length, out body)
          && page == 0 && length == 280 && body.AsSpan().SequenceEqual(hero),
        "hero page wire length");
    page = 7;
    Check(pageState.TryCopyPage(12, ref page, out length, out body)
          && page == -1 && length == 0 && body.Length == 0,
        "empty ranking bucket");
    page = 7;
    Check(!pageState.TryCopyPage(14, ref page, out length, out body)
          && page == 7 && length == 0 && body.Length == 0,
        "invalid ranking category leaves page unchanged");
}

static void CheckQuestOrderResponse()
{
    using var service = new DBService();
    var rankingState = GetSecondaryRankings(service);
    for (var pageIndex = 0; pageIndex < 4; pageIndex++)
    {
        Check(rankingState.Consume(Packet(1, Pattern(168,
                  unchecked((byte)(0x10 + pageIndex))))) ==
              NativeType2SecondaryRankingResult.RecordAppended,
            "quest order category 1 setup");
        Check(rankingState.Consume(Packet(3, Pattern(168,
                  unchecked((byte)(0x30 + pageIndex))))) ==
              NativeType2SecondaryRankingResult.RecordAppended,
            "quest order category 3 setup");
        Check(rankingState.Consume(Packet(8, Pattern(168,
                  unchecked((byte)(0x50 + pageIndex))))) ==
              NativeType2SecondaryRankingResult.RecordAppended,
            "quest order category 8 setup");
        Check(rankingState.Consume(Packet(4, Pattern(280,
                  unchecked((byte)(0x70 + pageIndex))))) ==
              NativeType2SecondaryRankingResult.RecordAppended,
            "quest order category 4 setup");
        Check(rankingState.Consume(Packet(7, Pattern(280,
                  unchecked((byte)(0x90 + pageIndex))))) ==
              NativeType2SecondaryRankingResult.RecordAppended,
            "quest order category 7 setup");
    }

    Check(service.TryGetSecondaryRankingPage(1, 999, out var page,
              out var length, out var snapshot)
          && page == 3 && length == 168 && snapshot[0] == 0x13,
        "DBService locked ranking page snapshot");

    var player = new TPlayObject
    {
        m_btJob = 1,
        m_NativeDbSessionSuffix = new byte[0x62]
    };
    BinaryPrimitives.WriteUInt16LittleEndian(
        player.m_NativeDbSessionSuffix.AsSpan(
            TPlayObject.NativeCurrentPersonalRankingOffset, 2), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(
        player.m_NativeDbSessionSuffix.AsSpan(
            TPlayObject.NativeOverallPersonalRankingOffset, 2), 15);
    BinaryPrimitives.WriteUInt16LittleEndian(
        player.m_NativeDbSessionSuffix.AsSpan(
            TPlayObject.NativeApprenticeRankingOffset, 2), 22);

    var heroRecord = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    BinaryPrimitives.WriteUInt16LittleEndian(heroRecord.AsSpan(0xB2, 2), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(heroRecord.AsSpan(0xB0, 2), 15);
    player.m_HeroObject = new HeroObject
    {
        m_btJob = 0,
        NativeHeroState = new NativeHeroRuntimeState(heroRecord,
            new NativeHeroDynamicData(Array.Empty<NativeHeroDynamicSection>()),
            Array.Empty<bool>(), Array.Empty<bool>())
    };

    var previousService = M2Share.DataServer;
    var previousHonorManager = M2Share.HonorValueManager;
    M2Share.DataServer = service;
    var honorManager = new NativeHonorValueManager();
    honorManager.ReplaceRankingSnapshot(Enumerable.Range(1, 10)
        .Select(rank => new KeyValuePair<string, int>(
            rank == 8 ? "荣誉本人" : rank == 10 ? "1234567890ABCDEF"
                : $"荣誉{rank}", 1000 - rank)));
    M2Share.HonorValueManager = honorManager;
    player.m_sCharName = "荣誉本人";
    try
    {
        CheckResponse(player, -1, 1, 1, 0x11, 168,
            "job my-rank page");
        CheckResponse(player, -1, 3, 2, 0x32, 168,
            "overall my-rank page");
        CheckResponse(player, -1, 8, 3, 0x53, 168,
            "apprentice my-rank page");
        CheckResponse(player, -1, 4, 1, 0x71, 280,
            "hero job my-rank page");
        CheckResponse(player, -1, 7, 2, 0x92, 280,
            "hero overall my-rank page");
        CheckResponse(player, 999, 1, 3, 0x13, 168,
            "high requested page clamps to last");

        Check(player.TryCreateNativeQuestOrderResponse(-1, 0,
                  out var missing, out var missingBody)
              && missing.Recog == -2 && missingBody.Length == 0,
            "job mismatch my-rank response");
        Check(player.TryCreateNativeQuestOrderResponse(0, 12,
                  out var empty, out var emptyBody)
              && empty.Recog == -1 && empty.Param == 12
              && emptyBody.Length == 0,
            "empty bucket response");
        Check(!player.TryCreateNativeQuestOrderResponse(0, 9,
                  out _, out _)
              && !player.TryCreateNativeQuestOrderResponse(0, 10,
                  out _, out _),
            "categories 9 and 10 are silent");
        Check(player.TryCreateNativeQuestOrderResponse(-1, 16,
                  out var honorMine, out var honorMineBody)
              && honorMine.Recog == 1 && honorMine.Param == 16
              && honorMine.Tag == 1 && honorMine.Series == 0
              && honorMineBody.Length == 3 *
                  NativeHonorValueManager.RankingRecordSize
              && BinaryPrimitives.ReadUInt16LittleEndian(honorMineBody) == 8
              && BinaryPrimitives.ReadInt32LittleEndian(
                  honorMineBody.AsSpan(
                      NativeHonorValueManager.RankingHonorOffset, 4)) == 992,
            "category 16 personal honor page and 22-byte record");
        Check(player.TryCreateNativeQuestOrderResponse(0, 16,
                  out var honorFirst, out var honorFirstBody)
              && honorFirst.Recog == 0 && honorFirst.Param == 16
              && honorFirst.Tag == 1 && honorFirst.Series == 0
              && honorFirstBody.Length == 7 *
                  NativeHonorValueManager.RankingRecordSize,
            "category 16 explicit honor page");
        var firstHonorName = HUtil32.GbkEncoding.GetBytes("荣誉1");
        Check(honorFirstBody[NativeHonorValueManager.RankingNameLengthOffset]
                  == firstHonorName.Length
              && honorFirstBody.AsSpan(
                      NativeHonorValueManager.RankingNameOffset,
                      firstHonorName.Length).SequenceEqual(firstHonorName)
              && honorFirstBody[
                  NativeHonorValueManager.RankingNameOffset
                  + firstHonorName.Length] == 0,
            "category 16 record uses GBK ShortString[15] length at +2 and payload at +3");
        Check(player.TryCreateNativeQuestOrderResponse(-2, 16,
                  out var honorSentinel, out var honorSentinelBody)
              && honorSentinel.Recog == -2 && honorSentinel.Param == 16
              && honorSentinel.Tag == 1 && honorSentinel.Series == 0
              && honorSentinelBody.Length == 0,
            "category 16 explicit -2 sentinel returns the native empty frame");
        player.m_sCharName = "荣誉榜外";
        Check(player.TryCreateNativeQuestOrderResponse(-1, 16,
                  out var honorMissing, out var honorMissingBody)
              && honorMissing.Recog == -2 && honorMissing.Param == 16
              && honorMissing.Tag == 1 && honorMissing.Series == 0
              && honorMissingBody.Length == 0,
            "category 16 missing personal rank response");
        Check(!player.TryCreateNativeQuestOrderResponse(2, 16,
                  out _, out _),
            "category 16 high page is silent");
        player.m_sCharName = "1234567890ABCDEF";
        Check(player.TryCreateNativeQuestOrderResponse(-1, 16,
                  out var honorLongMissing, out var honorLongMissingBody)
              && honorLongMissing.Recog == -2
              && honorLongMissingBody.Length == 0,
            "category 16 personal lookup compares the truncated 15-byte record");
        player.m_sCharName = "1234567890ABCDE";
        Check(player.TryCreateNativeQuestOrderResponse(-1, 16,
                  out var honorTruncatedMatch, out var honorTruncatedBody)
              && honorTruncatedMatch.Recog == 1
              && honorTruncatedBody.Length == 3 *
                  NativeHonorValueManager.RankingRecordSize,
            "category 16 personal lookup can match the exact truncated record bytes");
    }
    finally
    {
        M2Share.DataServer = previousService;
        M2Share.HonorValueManager = previousHonorManager;
    }

    void CheckResponse(TPlayObject source, int requestedPage, byte category,
        int expectedPage, byte expectedFirst, int expectedLength, string label)
    {
        Check(source.TryCreateNativeQuestOrderResponse(requestedPage, category,
                  out var header, out var body)
              && header.Ident == TPlayObject.NativeOrderListResponseIdent
              && header.Recog == expectedPage && header.Param == category
              && header.Tag == 2 && header.Series == 8
              && body.Length == expectedLength && body[0] == expectedFirst,
            label);
    }
}

static void CheckNativeHonorRecipientLookup()
{
    var engine = new UserEngine();
    var field = typeof(UserEngine).GetField("m_PlayObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(UserEngine),
            "m_PlayObjectList");
    var players = (IList<TPlayObject>)field.GetValue(engine);
    var player = new TPlayObject
    {
        m_sCharName = "HonorReceiverA",
        m_boReadyRun = true,
        m_boGhost = false
    };
    players.Add(player);

    Check(ReferenceEquals(engine.GetNativeReadyPlayObject("HonorReceiverA"),
            player)
          && ReferenceEquals(engine.GetNativeReadyPlayObject("honorreceivera"),
              player),
        "sub_652784 name lookup is case-insensitive and accepts the ready non-ghost player");
    player.m_sCharName = "ReceiverＡ";
    Check(string.Equals("ReceiverＡ", "Receiverａ",
              StringComparison.OrdinalIgnoreCase)
          && !UserEngine.NativeAnsiNameEquals("ReceiverＡ", "Receiverａ")
          && engine.GetNativeReadyPlayObject("Receiverａ") == null,
        "sub_40BCBC folds ASCII A-Z bytes only, not full-width Unicode case pairs");

    var upperTrailName = HUtil32.GbkEncoding.GetString(
        new byte[] { 0x81, 0x41 });
    var lowerTrailName = HUtil32.GbkEncoding.GetString(
        new byte[] { 0x81, 0x61 });
    player.m_sCharName = upperTrailName;
    Check(HUtil32.GbkEncoding.GetBytes(upperTrailName)
              .SequenceEqual(new byte[] { 0x81, 0x41 })
          && HUtil32.GbkEncoding.GetBytes(lowerTrailName)
              .SequenceEqual(new byte[] { 0x81, 0x61 })
          && UserEngine.NativeAnsiNameEquals(upperTrailName, lowerTrailName)
          && ReferenceEquals(engine.GetNativeReadyPlayObject(lowerTrailName),
              player),
        "sub_40BCBC folds an ASCII A-Z byte even when it is a GBK trail byte");

    var highByteLeft = HUtil32.GbkEncoding.GetString(
        new byte[] { 0xD6, 0xD0 });
    var highByteRight = HUtil32.GbkEncoding.GetString(
        new byte[] { 0xF6, 0xD0 });
    Check(!UserEngine.NativeAnsiNameEquals(highByteLeft, highByteRight),
        "sub_40BCBC must leave all GBK bytes >=0x80 unchanged");
    player.m_sCharName = "HonorReceiverA";

    player.m_boReadyRun = false;
    Check(engine.GetNativeReadyPlayObject(player.m_sCharName) == null,
        "sub_652784 rejects ReadyRun=0");
    player.m_boReadyRun = true;
    player.m_boGhost = true;
    Check(engine.GetNativeReadyPlayObject(player.m_sCharName) == null,
        "sub_652784 rejects ghost players");

    player.m_boGhost = false;
    player.m_boPasswordLocked = true;
    player.m_boObMode = true;
    player.m_boAdminMode = true;
    Check(engine.GetPlayObject(player.m_sCharName) == null
          && ReferenceEquals(engine.GetNativeReadyPlayObject(player.m_sCharName),
              player),
        "sub_652784 must not inherit the generic password/observer/admin rejection");

    var previousEngine = M2Share.UserEngine;
    M2Share.UserEngine = engine;
    try
    {
        Check(ReferenceEquals(player.ResolveNativeQuestOrderRecipient(16), player)
              && ReferenceEquals(player.ResolveNativeQuestOrderRecipient(0), player),
            "honor category uses native ready-name lookup; ordinary categories stay on Self");
    }
    finally
    {
        M2Share.UserEngine = previousEngine;
    }
}

static void CheckTopSevenOnlineNotifications()
{
    const int previousRankingOffset = 0x0172;
    var names = new[]
    {
        "排行甲", "排行乙", "排行丙", "排行丁", "离线戊", "排行己", "排行庚"
    };
    var rise = Player("角色甲", 5);
    var decline = Player("角色乙", 1);
    var equal = Player("角色丙", 3);
    var first = Player("角色丁", 0);
    var lateRise = Player("角色己", 9);
    var lateDecline = Player("角色庚", 2);
    var players = new Dictionary<string, TPlayObject>(
        StringComparer.OrdinalIgnoreCase)
    {
        [names[0]] = rise,
        [names[1]] = decline,
        [names[2]] = equal,
        [names[3]] = first,
        [names[5]] = lateRise,
        [names[6]] = lateDecline
    };
    var lookups = new List<string>();
    var broadcasts = new List<LegacyGateType18>();
    var publisher = new NativeType2SecondaryRankingPublisher(
        name =>
        {
            lookups.Add(name);
            return players.TryGetValue(name, out var player) ? player : null;
        }, broadcasts.Add);
    var notificationState = new NativeType2SecondaryRankingState(publisher);

    Check(notificationState.Consume(Packet(3, RankingPage(names)))
          == NativeType2SecondaryRankingResult.RecordAppended,
        "top-seven first page append");
    Check(notificationState.Consume(Packet(3,
              RankingPage(new[] { "不应查找", "", "", "", "", "", "" })))
          == NativeType2SecondaryRankingResult.RecordAppended,
        "top-seven second page append");
    Check(notificationState.Consume(Packet(
              NativeType2SecondaryRankingState.FinalizeCategory,
              Array.Empty<byte>()))
          == NativeType2SecondaryRankingResult.BatchFinalized,
        "top-seven finalize");

    Check(lookups.SequenceEqual(names),
        "top-seven lookup order and first-page-only source");
    Check(broadcasts.Count == 2, "top-seven rise broadcast count");
    CheckLegacyBroadcast(broadcasts[0],
        "玛法群英榜十强动态：和上次在榜中的排名相比，角色甲 的个人排行上升了4位，目前位居玛法群英榜第1位!");
    CheckLegacyBroadcast(broadcasts[1],
        "玛法群英榜十强动态：和上次在榜中的排名相比，角色己 的个人排行上升了3位，目前位居玛法群英榜第6位!");
    Check(rise.m_MsgList.Count == 0 && lateRise.m_MsgList.Count == 0,
        "finalize rise suppresses personal message when cl=0");

    CheckPersonalDecline(decline, 2,
        "您的个人排行目前在玛法群英榜中名列第2名，和上一次您在榜中的排名相比，您下降了1位。");
    CheckPersonalDecline(lateDecline, 7,
        "您的个人排行目前在玛法群英榜中名列第7名，和上一次您在榜中的排名相比，您下降了5位。");
    Check(equal.m_wNativeCurrentPersonalRanking == 3
          && Previous(equal) == 3 && equal.m_MsgList.Count == 0,
        "equal ranking does not notify");
    Check(first.m_wNativeCurrentPersonalRanking == 4
          && Previous(first) == 0 && first.m_MsgList.Count == 0,
        "first ranking keeps zero previous rank and does not notify");
    Check(rise.m_wNativeCurrentPersonalRanking == 1 && Previous(rise) == 1
          && lateRise.m_wNativeCurrentPersonalRanking == 6
          && Previous(lateRise) == 6,
        "rise updates current and previous ranking");

    var malformedLookups = 0;
    var malformedState = new NativeType2SecondaryRankingState(
        new NativeType2SecondaryRankingPublisher(
            _ =>
            {
                malformedLookups++;
                return null;
            }, _ => { }));
    Check(malformedState.Consume(Packet(3, new byte[] { 16 }))
          == NativeType2SecondaryRankingResult.RecordAppended
          && malformedState.Consume(Packet(
              NativeType2SecondaryRankingState.FinalizeCategory,
              Array.Empty<byte>()))
          == NativeType2SecondaryRankingResult.BatchFinalized
          && malformedLookups == 0,
        "malformed top-seven name fails closed");

    TPlayObject Player(string name, ushort previous)
    {
        var player = new TPlayObject
        {
            m_sCharName = name,
            m_NativeHumanData = new byte[0xEEF8]
        };
        BinaryPrimitives.WriteUInt16LittleEndian(
            player.m_NativeHumanData.AsSpan(previousRankingOffset, 2), previous);
        return player;
    }

    ushort Previous(TPlayObject player) =>
        BinaryPrimitives.ReadUInt16LittleEndian(
            player.m_NativeHumanData.AsSpan(previousRankingOffset, 2));

    void CheckPersonalDecline(TPlayObject player, ushort current, string text)
    {
        Check(player.m_wNativeCurrentPersonalRanking == current
              && Previous(player) == current,
            "decline updates current and previous ranking");
        Check(player.m_MsgList.Count == 1, "decline personal message count");
        var message = player.m_MsgList[0];
        Check(message.wIdent == Grobal2.RM_SYSMESSAGE
              && message.wParam == 0
              && message.nParam1 == 0xFF
              && message.nParam2 == 0xFC
              && message.nParam3 == 0
              && ReferenceEquals(message.BaseObject, player)
              && message.Buff == text,
            "decline personal RM_SYSMESSAGE/color/text");
    }
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

static byte[] RankingPage(IReadOnlyList<string> names)
{
    var body = new byte[NativeType2SecondaryRankingPublisher.RowCount
                        * NativeType2SecondaryRankingPublisher.RowSize];
    for (var row = 0;
         row < Math.Min(names.Count,
             NativeType2SecondaryRankingPublisher.RowCount);
         row++)
    {
        var bytes = HUtil32.GbkEncoding.GetBytes(names[row] ?? string.Empty);
        Check(bytes.Length <= NativeType2SecondaryRankingPublisher.NameCapacity,
            "ranking test name capacity");
        var offset = row * NativeType2SecondaryRankingPublisher.RowSize;
        body[offset] = unchecked((byte)bytes.Length);
        bytes.CopyTo(body, offset + 1);
    }
    return body;
}

static NativeType2SecondaryRankingState GetSecondaryRankings(DBService service)
{
    var field = typeof(DBService).GetField("_secondaryRankings",
        BindingFlags.Instance | BindingFlags.NonPublic);
    return field?.GetValue(service) as NativeType2SecondaryRankingState
           ?? throw new InvalidOperationException(
               "DBService secondary ranking state missing");
}

static byte[] Pattern(int length, byte value)
{
    var body = new byte[length];
    body.AsSpan().Fill(value);
    return body;
}

static void CheckLegacyBroadcast(LegacyGateType18 packet, string text)
{
    var expectedText = HUtil32.GbkEncoding.GetBytes(text);
    Check(packet.FilterUserIndex == 0 && packet.Recog == 0
          && packet.Ident == Grobal2.SM_SYSMESSAGE
          && packet.Param == 0x38FF && packet.Tag == 0 && packet.Series == 0
          && packet.TextBytes.AsSpan().SequenceEqual(expectedText),
        "rise legacy type18 fields/text");

    var frame = packet.ToBytes();
    Check(frame.Length == LegacyGateType18.HeaderSize
                         + LegacyGateType18.ClientPacketSize
                         + expectedText.Length + 1
          && BinaryPrimitives.ReadUInt32LittleEndian(frame) ==
          LegacyGateType18.MagicValue
          && BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4, 4)) == 0
          && BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8, 4)) == 0
          && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(12, 2)) == 18
          && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(14, 2)) ==
          LegacyGateType18.ClientPacketSize + expectedText.Length + 1
          && BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16, 4)) == 0
          && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(20, 2)) ==
          Grobal2.SM_SYSMESSAGE
          && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(22, 2)) ==
          0x38FF
          && frame[^1] == 0,
        "rise legacy type18 exact wire");
    var parsed = LegacyGateType18.FromBytes(frame, 0, frame.Length);
    Check(parsed != null && parsed.ToBytes().AsSpan().SequenceEqual(frame)
          && parsed.TextBytes.AsSpan().SequenceEqual(expectedText),
        "rise legacy type18 encode/decode round trip");
}

static byte[] Packet(int category, byte[] body, int param2 = 0,
    ushort command = NativeType2SecondaryRankingState.RecordCommand)
{
    body ??= Array.Empty<byte>();
    var payload = new byte[NativeType2SecondaryRankingState.HeaderSize
                           + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), category);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), param2);
    body.CopyTo(payload, NativeType2SecondaryRankingState.HeaderSize);
    return payload;
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void CheckThrows<TException>(Action action, string description)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(description);
}
