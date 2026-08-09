using System.Buffers.Binary;
using System.Reflection;
using DBSvr.Core;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

PrepareRuntimeConfig();
PrepareGameState();

Equal(1085, Grobal2.CM_MERCHANTQUERYEXCHGBOOK, "open request ident");
Equal(1086, Grobal2.CM_EXCHANGEBOOK_ROTATE, "rotate request ident");
Equal(1087, Grobal2.CM_EXCHANGEBOOK_GET_PRIZE, "claim request ident");
Equal(1088, Grobal2.CM_EXCHANGEBOOK_CLOSE, "close request ident");
Equal(961, Grobal2.SM_MERCHANTQUERYEXCHGBOOK, "open response ident");
Equal(962, Grobal2.SM_EXCHANGEBOOK_ROTATE, "rotate response ident");
Equal(963, Grobal2.SM_EXCHANGEBOOK_GET_PRIZE, "claim response ident");

var fixtureRoot = CreateFixtureRoot(writeExchangeBookConfig: true);
M2Share.sRootPath = fixtureRoot;
ResetGlobalRareCounters();
RunPoolWeightMapping();
RunDescriptorShortStringTruthTable();
RunWireBodyShortStringTruthTable();
ResetGlobalRareCounters();
RunRareGuarantees();
RunRareBroadcastTruthTable();
ResetGlobalRareCounters();
RunClientItemIdTruthTable();
RunActionCapacityTruthTable();
RunAlternateOpenRotateTruthTable();
RunSuccessAndDuplicateClaim();
RunGrantFailureCommitsState();
RunAtomicFailure();
RunCloseClearsState();
RunRareCounterPersistence();

var missingRoot = CreateFixtureRoot(writeExchangeBookConfig: false);
M2Share.sRootPath = missingRoot;
RunMissingConfigFailure(missingRoot);

var brokenRoot = CreateFixtureRoot(writeExchangeBookConfig: false);
var brokenFile = Path.Combine(brokenRoot, "Share", "config",
    "赤金天赐2.ini");
File.WriteAllText(brokenFile, string.Empty, HUtil32.GbkEncoding);
M2Share.sRootPath = brokenRoot;
RunBrokenConfigFailure(brokenFile);

Console.WriteLine(
    "NativeExchangeBookFailClosedCheck PASS open=961 rotate=962 claim=963 pools=exact rare=exact rounds=4 client-id=exact descriptor=shortstring20 grant-failure=commit config-failure=consume-first capacity=4 alternate1085=true persistence=0x180 close=true");

static void RunPoolWeightMapping()
{
    var player = CreatePlayer(out _, out _);
    Invoke(player, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertPacket(player, 961, 0, 0, "pool mapping open");

    var slots = Field<Array>(player, "_nativeExchangeBookSlots");
    Equal(12, slots.Length, "pool mapping slot count");
    var visiblePools = new HashSet<int>();
    for (var index = 0; index < 8; index++)
    {
        var reward = slots.GetValue(index);
        var pool = RewardInt(reward, "Pool");
        visiblePools.Add(pool);
        var expectedWeight = pool <= 7 ? pool * 101 : 0;
        Equal(expectedWeight, RewardInt(reward, "Weight"),
            $"visible pool {pool} weight");
    }
    Require(visiblePools.SetEquals(Enumerable.Range(1, 8)),
        "visible pools were not exactly 1..8");

    Equal(12, RewardInt(slots.GetValue(8), "Pool"),
        "centre prize pool");
    Equal(0, RewardInt(slots.GetValue(8), "Weight"),
        "centre prize weight");
    var replacementPools = new HashSet<int>();
    for (var index = 9; index < 12; index++)
    {
        var reward = slots.GetValue(index);
        var pool = RewardInt(reward, "Pool");
        replacementPools.Add(pool);
        var expectedWeight = pool == 9 ? 808 : 0;
        Equal(expectedWeight, RewardInt(reward, "Weight"),
            $"replacement pool {pool} weight");
    }
    Require(replacementPools.SetEquals(Enumerable.Range(9, 3)),
        "replacement pools were not exactly 9..11");
}

static void RunDescriptorShortStringTruthTable()
{
    const string source = "甲乙丙丁戊己庚辛壬癸:1";
    var sourceBytes = HUtil32.GbkEncoding.GetBytes(source);
    Require(sourceBytes.Length > 20,
        "descriptor fixture did not exceed ShortString payload");

    var descriptor = InvokeStaticResult<byte[]>(
        "CreateExchangeBookDescriptor", source);
    Equal(21, descriptor.Length, "descriptor fixed width");
    Equal((byte)20, descriptor[0], "descriptor length prefix");
    Require(descriptor.AsSpan(1, 20).SequenceEqual(
            sourceBytes.AsSpan(0, 20)),
        "descriptor did not preserve the raw CP936 byte prefix");

    var reward = InvokeStaticResult<object>("CreateExchangeBookReward",
        source, 1, 101);
    var rewardDescriptor = RewardBytes(reward, "Descriptor");
    Equal(21, rewardDescriptor.Length, "reward descriptor fixed width");
    Require(rewardDescriptor.SequenceEqual(descriptor),
        "reward descriptor changed ShortString bytes");
}

static void RunWireBodyShortStringTruthTable()
{
    const string name = "甲乙丙丁戊己庚辛";
    var nameBytes = HUtil32.GbkEncoding.GetBytes(name);
    Equal(16, nameBytes.Length, "wire-name fixture length");

    var reward = InvokeStaticResult<object>("CreateExchangeBookReward",
        name + ":1", 1, 101);
    var slots = Array.CreateInstance(reward.GetType(), 12);
    slots.SetValue(reward, 0);
    var body = InvokeStaticResult<byte[]>("BuildExchangeBookWireBody", slots);

    Equal(288, body.Length, "wire body fixed width");
    Equal((byte)15, body[0], "wire name length prefix");
    Require(body.AsSpan(1, 15).SequenceEqual(nameBytes.AsSpan(0, 15)),
        "wire name did not preserve the raw 15-byte CP936 prefix");
    Equal(nameBytes[14], body[15], "wire name GBK split byte");
}

static void RunRareGuarantees()
{
    Require(StaticField<int[]>("ExchangeBookPersonalRareIntervals")
            .SequenceEqual(new[] { 40, 80, 160, 27, 200, 500, 500, 200 }),
        "personal rare intervals");
    Require(StaticField<int[]>("ExchangeBookGlobalRareIntervals")
            .SequenceEqual(new[]
                { 650, 1300, 2600, 433, 2500, 2500, 2500, 2500 }),
        "global rare intervals");

    ResetGlobalRareCounters();
    var personalPlayer = CreatePlayer(out _, out _);
    Field<int[]>(personalPlayer,
        "_nativeExchangeBookPersonalRareCounters")[0] = 39;
    Invoke(personalPlayer, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertRareOpen(personalPlayer, 10, 6, "personal rare open");
    Equal(40, Field<int[]>(personalPlayer,
        "_nativeExchangeBookPersonalRareCounters")[0],
        "personal rare counter");
    Equal(1, GlobalRareCounters()[0], "personal rare global counter");
    Invoke(personalPlayer, "ClientExchangeBookClose");
    Equal(40, Field<int[]>(personalPlayer,
        "_nativeExchangeBookPersonalRareCounters")[0],
        "close changed personal rare counter");

    ResetGlobalRareCounters();
    GlobalRareCounters()[0] = 649;
    var globalPlayer = CreatePlayer(out _, out _);
    Invoke(globalPlayer, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertRareOpen(globalPlayer, 11, 7, "global rare open");
    Equal(650, GlobalRareCounters()[0], "global rare counter");

    ResetGlobalRareCounters();
    GlobalRareCounters()[0] = 649;
    var priorityPlayer = CreatePlayer(out _, out _);
    Field<int[]>(priorityPlayer,
        "_nativeExchangeBookPersonalRareCounters")[0] = 39;
    Invoke(priorityPlayer, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertRareOpen(priorityPlayer, 11, 7,
        "global rare priority over personal");

    ResetGlobalRareCounters();
    var pendingPlayer = CreatePlayer(out _, out _);
    Invoke(pendingPlayer, "ClientMerchantQueryExchgBook", 1001, 1002);
    var pendingSlots = Field<Array>(pendingPlayer,
        "_nativeExchangeBookSlots");
    Field<int[]>(pendingPlayer,
        "_nativeExchangeBookPersonalRareCounters")[0] = 39;
    GlobalRareCounters()[0] = 0;
    _ = InvokeResult<int>(pendingPlayer, "SelectExchangeBookSlot",
        pendingSlots);
    var personalRareIndex = FindPool(pendingSlots, 10);
    Require(personalRareIndex >= 0, "personal rare pool 10 missing");
    Equal(10000, RewardInt(pendingSlots.GetValue(personalRareIndex),
        "Weight"), "personal rare forced weight");
    Equal(6, Field<int>(pendingPlayer, "_nativeExchangeBookRareState"),
        "personal rare pending state");

    var displaced = pendingSlots.GetValue(0);
    var forcedReward = pendingSlots.GetValue(personalRareIndex);
    pendingSlots.SetValue(forcedReward, 0);
    pendingSlots.SetValue(displaced, personalRareIndex);
    var forcedSlot = InvokeResult<int>(pendingPlayer,
        "SelectExchangeBookSlot", pendingSlots);
    Equal(0, forcedSlot, "existing 10000 slot priority");
    Equal(6, Field<int>(pendingPlayer, "_nativeExchangeBookRareState"),
        "existing 10000 slot state");

    ResetGlobalRareCounters();
    var globalPendingPlayer = CreatePlayer(out _, out _);
    Invoke(globalPendingPlayer, "ClientMerchantQueryExchgBook", 1001,
        1002);
    var globalPendingSlots = Field<Array>(globalPendingPlayer,
        "_nativeExchangeBookSlots");
    GlobalRareCounters()[0] = 649;
    _ = InvokeResult<int>(globalPendingPlayer, "SelectExchangeBookSlot",
        globalPendingSlots);
    var globalRareIndex = FindPool(globalPendingSlots, 11);
    Require(globalRareIndex >= 0, "global rare pool 11 missing");
    Equal(10000, RewardInt(globalPendingSlots.GetValue(globalRareIndex),
        "Weight"), "global rare forced weight");
    Equal(7, Field<int>(globalPendingPlayer, "_nativeExchangeBookRareState"),
        "global rare pending state");
}

static void RunRareBroadcastTruthTable()
{
    var player = CreatePlayer(out _, out _);
    player.m_sCharName = "甲乙丙丁戊己庚辛";
    var state = typeof(TPlayObject).GetField("_nativeExchangeBookRareState",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(state != null, "rare broadcast state field missing");

    var descriptor = HUtil32.GbkEncoding.GetBytes("奖励:100");
    state.SetValue(player, 0);
    Require(InvokeResult<LegacyGateType18>(player,
            "BuildNativeExchangeBookRareBroadcast", descriptor) == null,
        "ordinary rotate reward built a rare broadcast");
    state.SetValue(player, 6);
    Require(InvokeResult<LegacyGateType18>(player,
            "BuildNativeExchangeBookRareBroadcast",
            HUtil32.GbkEncoding.GetBytes("奖励")) == null,
        "colonless reward built a rare broadcast");

    var packet = InvokeResult<LegacyGateType18>(player,
        "BuildNativeExchangeBookRareBroadcast", descriptor);
    Require(packet != null, "personal rare broadcast missing");
    Equal((uint)0, packet.IgnoredConnectionId,
        "rare broadcast connection id");
    Equal((uint)0, packet.FilterUserIndex, "rare broadcast filter");
    Equal(0, packet.Recog, "rare broadcast Recog");
    Equal((ushort)Grobal2.SM_SYSMESSAGE, packet.Ident,
        "rare broadcast Ident");
    Equal((ushort)0x38FF, packet.Param, "rare broadcast Param");
    Equal((ushort)0, packet.Tag, "rare broadcast Tag");
    Equal((ushort)0, packet.Series, "rare broadcast Series");

    var greeting = HUtil32.GbkEncoding.GetBytes("恭喜:");
    var name = HUtil32.GbkEncoding.GetBytes(player.m_sCharName);
    var suffix = HUtil32.GbkEncoding.GetBytes("在开启天赐时获得:");
    var rewardName = HUtil32.GbkEncoding.GetBytes("奖励");
    var expected = greeting.Concat(name.AsSpan(0, 14).ToArray())
        .Concat(suffix).Concat(rewardName).ToArray();
    Require(packet.TextBytes.SequenceEqual(expected),
        "rare broadcast did not preserve raw CP936 truncation");
    Equal(name[13], packet.TextBytes[greeting.Length + 13],
        "rare broadcast player-name byte cap");
    Equal((byte)0, packet.ToBytes()[^1],
        "rare broadcast wire terminator");

    state.SetValue(player, 7);
    Require(InvokeResult<LegacyGateType18>(player,
            "BuildNativeExchangeBookRareBroadcast", descriptor) != null,
        "global rare broadcast missing");
}

static void RunSuccessAndDuplicateClaim()
{
    var player = CreatePlayer(out var box, out var firstKey);
    var secondKey = AddBagItem(player, 2, 1003);

    Invoke(player, "ClientMerchantQueryExchgBook", 1001, 1002);
    Equal(0, player.m_ItemList.Count(item =>
        ReferenceEquals(item, box) || ReferenceEquals(item, firstKey)),
        "open consumed both inputs");
    Equal(1, player.m_ItemList.Count, "open preserved unrelated key");
    AssertPacket(player, 961, 0, 0, "open success");

    var body = Field<byte[]>(player, "_nativeExchangeBookWireBody");
    Equal(288, body.Length, "wire body length");
    Require(body.Any(value => value != 0), "wire body was not populated");

    var pendingBox = AddBagItem(player, 1, 1101);
    var pendingKey = AddBagItem(player, 2, 1102);
    Invoke(player, "ClientMerchantQueryExchgBook", 1101, 1102);
    AssertPacket(player, 961, 1, 0, "open while prize pending");
    Require(player.m_ItemList.Contains(pendingBox) &&
            player.m_ItemList.Contains(pendingKey),
        "pending open consumed replacement pair");

    Invoke(player, "ClientExchangeBookRotate", 0, 0);
    Equal(3, player.m_ItemList.Count, "first spin consumed no key");
    Equal((ushort)962, player.m_DefMsg.Ident, "first spin ident");
    Equal(0, player.m_DefMsg.Recog, "first spin recog");
    Require(player.m_DefMsg.Param < 8, "selected slot outside 0..7");

    var firstSpinResponse = player.m_DefMsg;
    Invoke(player, "ClientExchangeBookRotate", 0, 0);
    Require(ReferenceEquals(firstSpinResponse, player.m_DefMsg),
        "round zero without initial prize emitted a response");
    Equal(3, player.m_ItemList.Count,
        "silent round zero rotate consumed an item");

    var beforeExperience = player.m_Abil.Exp;
    Invoke(player, "ClientExchangeBookGetPrize");
    AssertPacket(player, 963, 0, 0, "first claim");
    Equal(beforeExperience + 100, player.m_Abil.Exp,
        "first claim reward");

    Invoke(player, "ClientExchangeBookGetPrize");
    AssertPacket(player, 963, 2, 0, "duplicate claim");
    Equal(beforeExperience + 100, player.m_Abil.Exp,
        "duplicate claim reward mutation");

    var beforeMissingKey = player.m_ItemList.ToArray();
    Invoke(player, "ClientExchangeBookRotate", 0, 1999);
    AssertPacket(player, 962, 1, 0, "missing rotate key");
    Require(beforeMissingKey.SequenceEqual(player.m_ItemList),
        "missing rotate key changed bag contents");

    Invoke(player, "ClientExchangeBookRotate", 0, 1003);
    AssertPacket(player, 962, 0, player.m_DefMsg.Param,
        "second spin");
    Equal(2, player.m_ItemList.Count, "second spin key consumption");
    Require(!player.m_ItemList.Contains(secondKey),
        "second spin retained consumed key");

    Invoke(player, "ClientExchangeBookGetPrize");
    AssertPacket(player, 963, 0, 0, "second claim");
    Equal(beforeExperience + 200, player.m_Abil.Exp,
        "second claim reward");

    AddBagItem(player, 2, 1004);
    Invoke(player, "ClientExchangeBookRotate", 0, 1004);
    AssertPacket(player, 962, 0, player.m_DefMsg.Param, "third spin");
    Invoke(player, "ClientExchangeBookGetPrize");
    AssertPacket(player, 963, 0, 0, "third claim");

    AddBagItem(player, 2, 1005);
    Invoke(player, "ClientExchangeBookRotate", 0, 1005);
    AssertPacket(player, 962, 0, player.m_DefMsg.Param, "fourth spin");
    Invoke(player, "ClientExchangeBookGetPrize");
    AssertPacket(player, 963, 0, 0, "fourth claim");
    Equal(beforeExperience + 400, player.m_Abil.Exp,
        "four-round rewards");
    Equal(0, Field<int>(player, "_nativeExchangeBookRound"),
        "fourth round wrap");
    Require(!HasShortString(Field<byte[]>(player,
                "_nativeExchangeBookInitialPrize")) &&
            !HasShortString(Field<byte[]>(player,
                "_nativeExchangeBookRotatePrize")),
        "fourth round left a pending prize");

    var fourthClaimResponse = player.m_DefMsg;
    Invoke(player, "ClientExchangeBookRotate", 0, 0);
    Require(ReferenceEquals(fourthClaimResponse, player.m_DefMsg),
        "wrapped round zero emitted a response");

    var silverBox = AddBagItem(player, 4, 1201);
    var silverKey = AddBagItem(player, 3, 1202);
    Invoke(player, "ClientMerchantQueryExchgBook", 1201, 1202);
    AssertPacket(player, 961, 1, 0, "retained pair mismatch");
    Require(player.m_ItemList.Contains(silverBox) &&
            player.m_ItemList.Contains(silverKey),
        "retained pair mismatch consumed silver pair");
    Equal(0, Field<int>(player, "_nativeExchangeBookPairIndex"),
        "retained pair mismatch changed pair");

    Invoke(player, "ClientMerchantQueryExchgBook", 1101, 1102);
    AssertPacket(player, 961, 0, 0, "reopen after completed rounds");
    Require(!player.m_ItemList.Contains(pendingBox) &&
            !player.m_ItemList.Contains(pendingKey),
        "completed state did not consume reopened pair");
    Require(player.m_ItemList.Contains(silverBox) &&
            player.m_ItemList.Contains(silverKey),
        "same-pair reopen consumed silver pair");
}

static void RunClientItemIdTruthTable()
{
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_MsgList = new List<SendMessage>()
    };
    var box = AddBagItem(player, 1, 3001, 3101);
    var key = AddBagItem(player, 2, 3002, 3102);
    var response = player.m_DefMsg;

    Invoke(player, "ClientMerchantQueryExchgBook", 9999, 3102);
    Require(ReferenceEquals(response, player.m_DefMsg),
        "missing first ClientItemID emitted a response");
    Invoke(player, "ClientMerchantQueryExchgBook", 3101, 9999);
    Require(ReferenceEquals(response, player.m_DefMsg),
        "missing second ClientItemID emitted a response");
    Equal(1, Field<int>(player, "_nativeExchangeBookBoxStdItemIndex"),
        "box wIndex was not retained at native +0x1808");
    Invoke(player, "ClientMerchantQueryExchgBook", 3001, 3002);
    Require(ReferenceEquals(response, player.m_DefMsg),
        "MakeIndex fallback emitted a response");
    Require(player.m_ItemList.Contains(box) && player.m_ItemList.Contains(key),
        "failed ClientItemID lookup consumed an input");

    Invoke(player, "ClientMerchantQueryExchgBook", 3101, 3102);
    AssertPacket(player, 961, 0, 0, "exact ClientItemID open");
    Require(!player.m_ItemList.Contains(box) &&
            !player.m_ItemList.Contains(key),
        "exact ClientItemID open retained inputs");
}

static void RunActionCapacityTruthTable()
{
    Equal("你没有6个包裹空位，不能领取",
        StaticField<string>("NativeExchangeBookClaimBagCapacityMessage"),
        "claim capacity message");

    var blocked = CreatePlayer(out var blockedBox, out var blockedKey);
    while (blocked.m_ItemList.Count < 45)
        AddBagItem(blocked, 5, 4000 + blocked.m_ItemList.Count);
    Invoke(blocked, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertPacket(blocked, 961, 1, 0, "45-item open capacity");
    Equal(45, blocked.m_ItemList.Count, "45-item open bag mutation");
    Require(blocked.m_ItemList.Contains(blockedBox) &&
            blocked.m_ItemList.Contains(blockedKey),
        "45-item open consumed pair");

    var boundary = CreatePlayer(out _, out _);
    while (boundary.m_ItemList.Count < 44)
        AddBagItem(boundary, 5, 5000 + boundary.m_ItemList.Count);
    Invoke(boundary, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertPacket(boundary, 961, 0, 0, "44-item open capacity");
    Equal(42, boundary.m_ItemList.Count, "44-item open consumption");

    var claimBlocked = CreatePlayer(out _, out _);
    Invoke(claimBlocked, "ClientMerchantQueryExchgBook", 1001, 1002);
    while (claimBlocked.m_ItemList.Count < 43)
        AddBagItem(claimBlocked, 5,
            6000 + claimBlocked.m_ItemList.Count);
    Invoke(claimBlocked, "ClientExchangeBookGetPrize");
    AssertPacket(claimBlocked, 963, 1, 0, "43-item claim capacity");
    Require(HasShortString(Field<byte[]>(claimBlocked,
            "_nativeExchangeBookInitialPrize")),
        "blocked claim cleared its pending prize");

    var rotate = CreatePlayer(out _, out _);
    Invoke(rotate, "ClientMerchantQueryExchgBook", 1001, 1002);
    Invoke(rotate, "ClientExchangeBookRotate", 0, 0);
    Invoke(rotate, "ClientExchangeBookGetPrize");
    Equal(1, Field<int>(rotate, "_nativeExchangeBookRound"),
        "rotate capacity setup round");
    while (rotate.m_ItemList.Count < 44)
        AddBagItem(rotate, 5, 6000 + rotate.m_ItemList.Count);
    var token = AddBagItem(rotate, 5, 6999);
    Invoke(rotate, "ClientExchangeBookRotate", 0, token.ClientItemID);
    AssertPacket(rotate, 962, 1, 0, "45-item rotate capacity");
    Require(rotate.m_ItemList.Contains(token),
        "45-item rotate consumed supplied instance");

    var filler = rotate.m_ItemList.First(item => !ReferenceEquals(item, token));
    rotate.m_ItemList.Remove(filler);
    Invoke(rotate, "ClientExchangeBookRotate", 0, token.ClientItemID);
    Equal((ushort)962, rotate.m_DefMsg.Ident,
        "44-item rotate success ident");
    Equal(0, rotate.m_DefMsg.Recog, "44-item rotate success recog");
    Require(!rotate.m_ItemList.Contains(token),
        "44-item rotate retained supplied instance");
}

static void RunAlternateOpenRotateTruthTable()
{
    var player = CreatePlayer(out _, out _);
    Invoke(player, "ClientMerchantQueryExchgBook", 1001, 1002);
    Invoke(player, "ClientExchangeBookRotate", 0, 0);
    Invoke(player, "ClientExchangeBookGetPrize");
    Equal(1, Field<int>(player, "_nativeExchangeBookRound"),
        "alternate 1085 setup round");

    var slots = Field<Array>(player, "_nativeExchangeBookSlots");
    var wireBody = Field<byte[]>(player, "_nativeExchangeBookWireBody");
    var retained = AddBagItem(player, 5, 7001);
    var consumed = AddBagItem(player, 5, 7002);
    Invoke(player, "ClientMerchantQueryExchgBook", 7001, 7002);
    Equal((ushort)962, player.m_DefMsg.Ident,
        "round 1 1085 alternate ident");
    Equal(0, player.m_DefMsg.Recog, "round 1 1085 alternate recog");
    Require(player.m_ItemList.Contains(retained) &&
            !player.m_ItemList.Contains(consumed),
        "round 1 1085 did not retain first and consume second");
    Require(ReferenceEquals(slots,
            Field<Array>(player, "_nativeExchangeBookSlots")) &&
            ReferenceEquals(wireBody,
                Field<byte[]>(player, "_nativeExchangeBookWireBody")),
        "round 1 1085 rebuilt exchange state");
    Equal(1, Field<int>(player, "_nativeExchangeBookRound"),
        "round 1 1085 advanced before claim");

    Invoke(player, "ClientExchangeBookGetPrize");
    Equal(2, Field<int>(player, "_nativeExchangeBookRound"),
        "alternate 1085 claim round");
    var arbitrary = AddBagItem(player, 5, 7101, 7102);
    Invoke(player, "ClientExchangeBookRotate", 0, 7101);
    AssertPacket(player, 962, 1, 0, "rotate MakeIndex rejection");
    Require(player.m_ItemList.Contains(arbitrary),
        "rotate MakeIndex rejection consumed item");
    Invoke(player, "ClientExchangeBookRotate", 0, 7102);
    Equal((ushort)962, player.m_DefMsg.Ident,
        "arbitrary ClientItemID rotate ident");
    Equal(0, player.m_DefMsg.Recog,
        "arbitrary ClientItemID rotate recog");
    Require(!player.m_ItemList.Contains(arbitrary),
        "arbitrary ClientItemID rotate retained item");
}

static void RunAtomicFailure()
{
    var player = CreatePlayer(out var box, out var key,
        keyStdItemIndex: 3);
    var original = player.m_ItemList.ToArray();

    Invoke(player, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertPacket(player, 961, 1, 0, "mismatched pair");
    Equal(2, player.m_ItemList.Count, "mismatched pair bag count");
    Require(ReferenceEquals(original[0], player.m_ItemList[0]) &&
            ReferenceEquals(original[1], player.m_ItemList[1]),
        "mismatched pair changed bag order or references");
    Require(ReferenceEquals(box, player.m_ItemList[0]) &&
            ReferenceEquals(key, player.m_ItemList[1]),
        "mismatched pair consumed one input");
    Equal(-1, Field<int>(player, "_nativeExchangeBookPairIndex"),
        "mismatched pair activated state");
}

static void RunGrantFailureCommitsState()
{
    var player = CreatePlayer(out _, out _);
    Invoke(player, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertPacket(player, 961, 0, 0, "grant failure setup open");

    const string unavailableReward = "不存在的天赐奖品:1";
    var initialPrize = Field<byte[]>(player,
        "_nativeExchangeBookInitialPrize");
    MakeShortString(unavailableReward).CopyTo(initialPrize, 0);
    var beforeExperience = player.m_Abil.Exp;

    Invoke(player, "ClientExchangeBookGetPrize");
    AssertPacket(player, 963, 0, 0, "grant failure response");
    Require(!HasShortString(Field<byte[]>(player,
                "_nativeExchangeBookInitialPrize")) &&
            !HasShortString(Field<byte[]>(player,
                "_nativeExchangeBookRotatePrize")),
        "grant failure retained a pending descriptor");
    Require(Field<Array>(player, "_nativeExchangeBookSlots")
            .Cast<object>().All(value => value == null),
        "grant failure retained reward state");
    Require(Field<byte[]>(player, "_nativeExchangeBookWireBody")
            .All(value => value == 0),
        "grant failure retained wire state");
    Equal(-1, Field<int>(player, "_nativeExchangeBookPairIndex"),
        "grant failure retained pair");
    Equal(0, Field<int>(player, "_nativeExchangeBookRound"),
        "grant failure advanced round");
    Equal(0, Field<int>(player,
            "_nativeExchangeBookDeferredBoxClientItemId"),
        "grant failure retained native +0x1804");
    Equal(1, Field<int>(player, "_nativeExchangeBookBoxStdItemIndex"),
        "initial claim cleared native +0x1808");
    Equal(beforeExperience, player.m_Abil.Exp,
        "grant failure mutated reward balance");
}

static void RunCloseClearsState()
{
    var player = CreatePlayer(out _, out _);
    Invoke(player, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertPacket(player, 961, 0, 0, "close setup open");
    var responseBeforeClose = player.m_DefMsg;

    Invoke(player, "ClientExchangeBookClose");
    Require(ReferenceEquals(responseBeforeClose, player.m_DefMsg),
        "close unexpectedly emitted a response");
    Equal(-1, Field<int>(player, "_nativeExchangeBookPairIndex"),
        "close pair index");
    Equal(0, Field<int>(player,
            "_nativeExchangeBookDeferredBoxClientItemId"),
        "close native +0x1804");
    Equal(0, Field<int>(player, "_nativeExchangeBookBoxStdItemIndex"),
        "close native +0x1808 box wIndex");
    Equal(0, Field<int>(player, "_nativeExchangeBookRound"),
        "close round");
    Equal(-1, Field<int>(player, "_nativeExchangeBookSelectedSlot"),
        "close selected slot");
    Require(!HasShortString(Field<byte[]>(player,
            "_nativeExchangeBookInitialPrize")),
        "close initial pending prize");
    Require(!HasShortString(Field<byte[]>(player,
            "_nativeExchangeBookRotatePrize")),
        "close rotate pending prize");
    Require(Field<byte[]>(player, "_nativeExchangeBookWireBody")
            .All(value => value == 0),
        "close wire state not zeroed");
    Require(Field<Array>(player, "_nativeExchangeBookSlots")
            .Cast<object>().All(value => value == null),
        "close reward slots not zeroed");

    Invoke(player, "ClientExchangeBookRotate", 0, 0);
    Require(ReferenceEquals(responseBeforeClose, player.m_DefMsg),
        "rotate after close unexpectedly emitted a response");
    Invoke(player, "ClientExchangeBookGetPrize");
    AssertPacket(player, 963, 2, 0, "claim after close");
}

static void RunMissingConfigFailure(string root)
{
    var player = CreatePlayer(out var box, out var key);
    var missingFile = Path.Combine(root, "Share", "config",
        "赤金天赐2.ini");
    Require(!File.Exists(missingFile), "missing fixture unexpectedly exists");

    Invoke(player, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertPacket(player, 961, 1, 0, "missing config");
    Require(!File.Exists(missingFile), "missing config was created");
    Equal(0, player.m_ItemList.Count, "missing config bag count");
    Require(!player.m_ItemList.Contains(box) &&
            !player.m_ItemList.Contains(key),
        "missing config retained an input");
    Equal(0, Field<int>(player, "_nativeExchangeBookPairIndex"),
        "missing config lost the resolved pair");
    Equal(1, Field<int>(player, "_nativeExchangeBookBoxStdItemIndex"),
        "missing config lost native +0x1808 box wIndex");
}

static void RunBrokenConfigFailure(string brokenFile)
{
    Require(File.Exists(brokenFile), "broken fixture missing");
    var player = CreatePlayer(out var box, out var key);

    Invoke(player, "ClientMerchantQueryExchgBook", 1001, 1002);
    AssertPacket(player, 961, 1, 0, "broken config");
    Equal(0, player.m_ItemList.Count, "broken config bag count");
    Require(!player.m_ItemList.Contains(box) &&
            !player.m_ItemList.Contains(key),
        "broken config retained an input");
    Equal(0, Field<int>(player, "_nativeExchangeBookPairIndex"),
        "broken config lost the resolved pair");
    Equal(1, Field<int>(player, "_nativeExchangeBookBoxStdItemIndex"),
        "broken config lost native +0x1808 box wIndex");
}

static void RunRareCounterPersistence()
{
    var expected = new[]
        { 1, 40, 79, 160, 433, 650, 2500, int.MaxValue };
    var record = new THumDataInfo();
    record.Data.ExchangeBookPersonalRareCounters = (int[])expected.Clone();
    Require(NativeHumanDataCodec.TryEncode(record, out var dataBlob,
            out var scriptBlob, out var encodeError),
        "counter encode failed: " + encodeError);
    for (var index = 0; index < expected.Length; index++)
    {
        Equal(expected[index], BinaryPrimitives.ReadInt32LittleEndian(
                record.NativeData.AsSpan(
                    NativeHumanDataCodec.ExchangeBookPersonalRareCountersOffset +
                    index * 4, 4)),
            "counter raw offset " + index);
    }
    Require(NativeHumanDataCodec.TryDecode(dataBlob, scriptBlob,
            out var decoded, out var decodeError),
        "counter decode failed: " + decodeError);
    Require(decoded.Data.ExchangeBookPersonalRareCounters
            .Take(expected.Length).SequenceEqual(expected),
        "counter codec round trip");

    var player = CreatePlayer(out _, out _);
    Invoke(player, "RestoreNativeExchangeBookPersonalRareCounters", expected);
    var save = new THumDataInfo();
    player.MakeSaveRcd(ref save);
    Require(save.Data.ExchangeBookPersonalRareCounters.SequenceEqual(expected),
        "player save did not preserve personal counters");
    Invoke(player, "RestoreNativeExchangeBookPersonalRareCounters",
        new[] { 9, 8 });
    Require(Field<int[]>(player,
            "_nativeExchangeBookPersonalRareCounters")
        .SequenceEqual(new[] { 9, 8, 0, 0, 0, 0, 0, 0 }),
        "short counter restore did not clear stale values");
}

static TPlayObject CreatePlayer(out TUserItem box, out TUserItem key,
    ushort keyStdItemIndex = 2)
{
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_MsgList = new List<SendMessage>()
    };
    player.m_Abil.MaxExp = 1_000_000;
    box = AddBagItem(player, 1, 1001);
    key = AddBagItem(player, keyStdItemIndex, 1002);
    return player;
}

static TUserItem AddBagItem(TPlayObject player, ushort stdItemIndex,
    int makeIndex, int? clientItemId = null)
{
    var item = new TUserItem
    {
        wIndex = stdItemIndex,
        MakeIndex = makeIndex,
        ClientItemID = clientItemId ?? makeIndex
    };
    player.m_ItemList.Add(item);
    return item;
}

static void PrepareGameState()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "赤金天赐" });
    M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "赤金钥匙" });
    M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "白银钥匙" });
    M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "白银天赐" });
    M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "普通物品" });
}

static string CreateFixtureRoot(bool writeExchangeBookConfig)
{
    var root = Path.Combine(AppContext.BaseDirectory,
        "exchange-book-" + Guid.NewGuid().ToString("N"));
    var configDirectory = Path.Combine(root, "Share", "config");
    Directory.CreateDirectory(configDirectory);
    if (!writeExchangeBookConfig) return root;

    var lines = new List<string>();
    for (var pool = 1; pool <= 12; pool++)
    {
        lines.Add($"[{pool}类奖励]");
        lines.Add("奖品1=经验:100/999");
        lines.Add(string.Empty);
    }
    lines.Add("[宝箱1]");
    for (var index = 1; index <= 8; index++)
        lines.Add($"概率{index}={index * 101}");
    File.WriteAllLines(Path.Combine(configDirectory, "赤金天赐2.ini"),
        lines, HUtil32.GbkEncoding);
    return root;
}

static void Invoke(TPlayObject player, string methodName,
    params object[] arguments)
{
    var method = typeof(TPlayObject).GetMethod(methodName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(method != null, methodName + " handler missing");
    method.Invoke(player, arguments);
}

static T InvokeResult<T>(TPlayObject player, string methodName,
    params object[] arguments)
{
    var method = typeof(TPlayObject).GetMethod(methodName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(method != null, methodName + " handler missing");
    return (T)method.Invoke(player, arguments);
}

static T InvokeStaticResult<T>(string methodName,
    params object[] arguments)
{
    var method = typeof(TPlayObject).GetMethod(methodName,
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(method != null, methodName + " static method missing");
    return (T)method.Invoke(null, arguments);
}

static T Field<T>(TPlayObject player, string name)
{
    var field = typeof(TPlayObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(field != null, name + " field missing");
    return (T)field.GetValue(player);
}

static bool HasShortString(byte[] value) =>
    value != null && value.Length == 21 && value[0] != 0;

static byte[] MakeShortString(string value)
{
    var result = new byte[21];
    var source = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
    var length = Math.Min(source.Length, 20);
    result[0] = (byte)length;
    source.AsSpan(0, length).CopyTo(result.AsSpan(1));
    return result;
}

static T StaticField<T>(string name)
{
    var field = typeof(TPlayObject).GetField(name,
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(field != null, name + " static field missing");
    return (T)field.GetValue(null);
}

static int[] GlobalRareCounters() =>
    StaticField<int[]>("ExchangeBookGlobalRareCounters");

static void ResetGlobalRareCounters() =>
    Array.Clear(GlobalRareCounters(), 0, GlobalRareCounters().Length);

static int RewardInt(object reward, string propertyName)
{
    Require(reward != null, propertyName + " reward missing");
    var property = reward.GetType().GetProperty(propertyName,
        BindingFlags.Instance | BindingFlags.NonPublic |
        BindingFlags.Public);
    Require(property != null, propertyName + " reward property missing");
    return (int)property.GetValue(reward);
}

static byte[] RewardBytes(object reward, string propertyName)
{
    Require(reward != null, propertyName + " reward missing");
    var property = reward.GetType().GetProperty(propertyName,
        BindingFlags.Instance | BindingFlags.NonPublic |
        BindingFlags.Public);
    Require(property != null, propertyName + " reward property missing");
    return (byte[])property.GetValue(reward);
}

static int FindPool(Array slots, int pool)
{
    for (var index = 0; index < slots.Length; index++)
    {
        var reward = slots.GetValue(index);
        if (reward != null && RewardInt(reward, "Pool") == pool)
            return index;
    }
    return -1;
}

static void AssertRareOpen(TPlayObject player, int expectedPool,
    int expectedState, string label)
{
    AssertPacket(player, 961, 0, 0, label);
    var selectedSlot = Field<int>(player,
        "_nativeExchangeBookSelectedSlot");
    Require(selectedSlot >= 0 && selectedSlot < 8,
        label + " selected slot");
    var slots = Field<Array>(player, "_nativeExchangeBookSlots");
    Equal(expectedPool, RewardInt(slots.GetValue(selectedSlot), "Pool"),
        label + " selected pool");
    Equal(0, RewardInt(slots.GetValue(selectedSlot), "Weight"),
        label + " selected weight");
    Equal(expectedState, Field<int>(player,
        "_nativeExchangeBookRareState"), label + " state");
    Invoke(player, "ClientExchangeBookRotate", 0, 0);
    AssertPacket(player, 962, 0, selectedSlot,
        label + " rotate param");
}

static void AssertPacket(TPlayObject player, ushort ident, int recog,
    int param, string label)
{
    Require(player.m_DefMsg != null, label + " response missing");
    Equal(ident, player.m_DefMsg.Ident, label + " ident");
    Equal(recog, player.m_DefMsg.Recog, label + " recog");
    Equal((ushort)param, player.m_DefMsg.Param, label + " param");
    Equal((ushort)0, player.m_DefMsg.Tag, label + " tag");
    Equal((ushort)0, player.m_DefMsg.Series, label + " series");
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

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}
