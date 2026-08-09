using System.Buffers.Binary;
using System.Text;
using GameSvr;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
TestRequestValidationAndReply();
TestRequestWire();
TestNativeIdentityByteTruncation();
TestCompletionDecode();
TestCompletionRejectsInvalidWire();
TestFailureDialogs();
TestCompletionRouting();
TestRewardSelectionAndParsing();
TestSuccessPlanOrderAndCalculations();
TestDisabledAndOverflowCalculations();
TestRuntimeRemainsFailClosed();

Console.WriteLine(
    "PASS MakeDiamondWithYB 120/80 1120/32 GBK snapshot-first " +
    "CreditCard+LFMultiple+YBConsume+type30 reward-order " +
    "online-only NPC-route-not-gate no-ACK native-byte-truncation " +
    "runtime=false pas=closed dormant=true");
return;

static void TestRequestValidationAndReply()
{
    Assert(!NativeMakeDiamondWithYbProtocol.IsValidRequestAmount(0),
        "request amount zero accepted");
    Assert(NativeMakeDiamondWithYbProtocol.IsValidRequestAmount(1),
        "request amount one rejected");
    Assert(NativeMakeDiamondWithYbProtocol.IsValidRequestAmount(300),
        "request amount 300 rejected");
    Assert(!NativeMakeDiamondWithYbProtocol.IsValidRequestAmount(301),
        "request amount 301 accepted");

    var reply = NativeMakeDiamondWithYbProtocol.CreateInvalidRequestReply();
    Equal(546, reply.Ident, "invalid request ident");
    Equal(-2, reply.Recog, "invalid request recog");
    Equal(0, reply.Param1, "invalid request param1");
    Equal(0, reply.Param2, "invalid request param2");
    Equal(0, reply.Param3, "invalid request param3");
    Equal(0, reply.Payload.Length, "invalid request payload");
    EqualText("元宝系统暂时关闭中...",
        NativeMakeDiamondWithYbProtocol.RequestUnavailableMessage,
        "request unavailable message");
    Equal(0x38FF,
        NativeMakeDiamondWithYbProtocol.RequestUnavailableMessageParam,
        "request unavailable message param");

    Assert(!NativeMakeDiamondWithYbProtocol.TryEncodeRequest(301,
        new YbDbLegacy77Identity(), out _, out _),
        "out-of-range request encoded");
}

static void TestRequestWire()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "PTID-0000000000000001-extra",
        Field11 = "PTID-0000000000000001-extra",
        RoleName = "锻造勇士",
        Field48 = "SERVER-00000001-extra"
    };
    Assert(NativeMakeDiamondWithYbProtocol.TryEncodeRequest(300,
        identity, out var wire, out var error), error);
    Equal(80, wire.Length, "request wire length");
    Equal(80, NativeMakeDiamondWithYbProtocol.RequestFrameSize,
        "request frame constant");
    EqualUInt(0x33AABB77,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0, 4)),
        "request magic");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(4, 4)),
        "request query id");
    Equal(300, BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(8, 4)),
        "request amount/param");
    Equal(120, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12, 2)),
        "request ident");
    Equal(64, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(14, 2)),
        "request identity length");

    Assert(YbDbLegacy77Codec.TryDecode(wire, out var frame, out error), error);
    Assert(YbDbLegacy77Codec.TryDecodeIdentity(frame.Payload,
        out var decoded, out error), error);
    EqualText("PTID-00000", decoded.Field0,
        "native narrow PTID truncation");
    EqualText("PTID-000000000000000", decoded.Field11,
        "native full PTID truncation");
    EqualText("锻造勇士", decoded.RoleName, "request role name");
    EqualText("SERVER-00000001", decoded.Field48,
        "native field48 truncation");
}

static void TestNativeIdentityByteTruncation()
{
    const string roleName = "甲乙丙丁戊己庚辛";
    var roleBytes = Encoding.GetEncoding(936).GetBytes(roleName);
    Equal(16, roleBytes.Length, "test role GBK length");

    Assert(NativeMakeDiamondWithYbProtocol.TryEncodeRequest(1,
        new YbDbLegacy77Identity { RoleName = roleName },
        out var wire, out var error), error);
    const int roleSlotOffset = YbDbLegacy77Codec.HeaderSize +
                               YbDbLegacy77Codec.IdentityRoleNameOffset;
    Equal(15, wire[roleSlotOffset],
        "native role ShortString byte length");
    for (var index = 0; index < 15; index++)
    {
        Equal(roleBytes[index], wire[roleSlotOffset + 1 + index],
            $"native role byte truncation[{index}]");
    }
}

static void TestCompletionDecode()
{
    var frame = new YbDbLegacy77Frame(60, unchecked((int)0x76543210),
        1120, BuildPayload("锻造勇士", 150, 900, 3600, 25));
    Assert(NativeMakeDiamondWithYbProtocol.TryDecodeCompletion(frame,
        out var completion, out var error), error);
    Equal(60, completion.ResultCode, "completion result/query id");
    EqualText("锻造勇士", completion.RoleName, "completion role");
    Equal(150, completion.CurrentYuanbao, "snapshot yuanbao");
    Equal(900, completion.TotalConsumedYuanbao, "snapshot consumed");
    Equal(3600, completion.DurationSeconds, "snapshot duration");
    Equal(25, completion.DividendConsumed, "snapshot dividend");
}

static void TestCompletionRejectsInvalidWire()
{
    Assert(!NativeMakeDiamondWithYbProtocol.TryDecodeCompletion(null,
        out _, out _), "null completion accepted");
    Assert(!NativeMakeDiamondWithYbProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(1, 2, 1119, new byte[32]),
        out _, out _), "wrong completion ident accepted");
    Assert(!NativeMakeDiamondWithYbProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(1, 2, 1120, new byte[31]),
        out _, out _), "short completion accepted");
    Assert(!NativeMakeDiamondWithYbProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(1, 2, 1120, new byte[33]),
        out _, out _), "long completion accepted");

    var longRole = new byte[32];
    longRole[0] = 16;
    Assert(!NativeMakeDiamondWithYbProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(1, 2, 1120, longRole),
        out _, out _), "16-byte role slot accepted");

    var invalidGbk = new byte[32];
    invalidGbk[0] = 1;
    invalidGbk[1] = 0x81;
    Assert(!NativeMakeDiamondWithYbProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(1, 2, 1120, invalidGbk),
        out _, out _), "invalid GBK role accepted");
}

static void TestFailureDialogs()
{
    EqualText("[失败]：您的元宝数不足！ \\ \\<返回/@main>",
        NativeMakeDiamondWithYbProtocol.BuildFailureDialog(-2),
        "failure -2");
    EqualText("[失败]：您的上次的锻造尚未完成 \\ \\<返回/@main>",
        NativeMakeDiamondWithYbProtocol.BuildFailureDialog(-3),
        "failure -3");
    EqualText("[失败]：请先取回您上次锻造的金刚石 \\ \\<返回/@main>",
        NativeMakeDiamondWithYbProtocol.BuildFailureDialog(-4),
        "failure -4");
    EqualText("[失败]：系统错误: Code=-99",
        NativeMakeDiamondWithYbProtocol.BuildFailureDialog(-99),
        "generic negative failure");
    EqualText("[失败]：系统错误: Code=0",
        NativeMakeDiamondWithYbProtocol.BuildFailureDialog(0),
        "generic zero failure");
    Throws<ArgumentOutOfRangeException>(() =>
        NativeMakeDiamondWithYbProtocol.BuildFailureDialog(1),
        "positive result mapped as failure");
}

static void TestCompletionRouting()
{
    var positive = Decode(60,
        BuildPayload("锻造勇士", 150, 900, 3600, 25));
    var negative = Decode(-2,
        BuildPayload("锻造勇士", 1, 2, 3, 4));

    var route = NativeMakeDiamondWithYbProtocol.EvaluateCompletionRoute(
        positive, false, true);
    Assert(!route.RoleResolved && !route.ExecutePositiveSideEffects,
        "offline positive completion was processed");
    Assert(route.DialogOutput ==
           NativeMakeDiamondWithYbProtocol.DialogOutputDisposition.None,
        "offline positive completion produced dialog output");
    Assert(!route.SendsYbDbAcknowledgement,
        "offline positive completion emitted ACK");

    route = NativeMakeDiamondWithYbProtocol.EvaluateCompletionRoute(
        negative, false, false);
    Assert(!route.RoleResolved && !route.ExecutePositiveSideEffects,
        "offline negative completion was processed");
    EqualText(string.Empty, route.FailureDialog,
        "offline negative completion built a dialog");

    route = NativeMakeDiamondWithYbProtocol.EvaluateCompletionRoute(
        positive, true, true);
    Assert(route.RoleResolved && route.ExecutePositiveSideEffects,
        "online positive completion was not processed");
    Assert(route.DialogOutput ==
           NativeMakeDiamondWithYbProtocol.DialogOutputDisposition.CurrentNpcDialog,
        "online positive current-NPC route");
    Assert(!route.SendsYbDbAcknowledgement,
        "online positive completion emitted ACK");

    route = NativeMakeDiamondWithYbProtocol.EvaluateCompletionRoute(
        positive, true, false);
    Assert(route.RoleResolved && route.ExecutePositiveSideEffects,
        "missing current NPC incorrectly rejected positive completion");
    Assert(route.DialogOutput ==
           NativeMakeDiamondWithYbProtocol.DialogOutputDisposition.MerchantSayNpcPrefix,
        "online positive fallback route");
    Equal(643, NativeMakeDiamondWithYbProtocol.FallbackMerchantMessageIdent,
        "fallback merchant Ident");
    EqualText("NPC/", NativeMakeDiamondWithYbProtocol.FallbackMerchantPrefix,
        "fallback merchant prefix");

    route = NativeMakeDiamondWithYbProtocol.EvaluateCompletionRoute(
        negative, true, false);
    Assert(route.RoleResolved && !route.ExecutePositiveSideEffects,
        "negative completion entered positive side effects");
    Assert(route.DialogOutput ==
           NativeMakeDiamondWithYbProtocol.DialogOutputDisposition.MerchantSayNpcPrefix,
        "online negative fallback route");
    EqualText("[失败]：您的元宝数不足！ \\ \\<返回/@main>",
        route.FailureDialog, "online negative failure dialog");
    Assert(!route.SendsYbDbAcknowledgement,
        "online negative completion emitted ACK");
}

static void TestRewardSelectionAndParsing()
{
    Equal(-1, NativeMakeDiamondWithYbProtocol.GetRewardDescriptorIndex(49),
        "reward tier 49");
    Equal(0, NativeMakeDiamondWithYbProtocol.GetRewardDescriptorIndex(50),
        "reward tier 50");
    Equal(0, NativeMakeDiamondWithYbProtocol.GetRewardDescriptorIndex(300),
        "reward tier 300");
    Equal(1, NativeMakeDiamondWithYbProtocol.GetRewardDescriptorIndex(301),
        "reward tier 301");

    Assert(NativeMakeDiamondWithYbProtocol.ParseRewardDescriptor(null) == null,
        "null descriptor produced reward");
    Assert(NativeMakeDiamondWithYbProtocol.ParseRewardDescriptor(string.Empty)
        == null, "empty descriptor produced reward");
    AssertReward("经验:25", "经验", 25,
        NativeMakeDiamondWithYbProtocol.RewardKind.Experience, 25, 0);
    AssertReward("声望:-2", "声望", -2,
        NativeMakeDiamondWithYbProtocol.RewardKind.Reputation, -2, 0);
    AssertReward("金刚石:9", "金刚石", 9,
        NativeMakeDiamondWithYbProtocol.RewardKind.Diamond, 9, 0);
    AssertReward("祝福油:3", "祝福油", 3,
        NativeMakeDiamondWithYbProtocol.RewardKind.StandardItem, 0, 3);
    AssertReward("祝福油:bad", "祝福油", 1,
        NativeMakeDiamondWithYbProtocol.RewardKind.StandardItem, 0, 1);
    AssertReward("祝福油:-3", "祝福油", -3,
        NativeMakeDiamondWithYbProtocol.RewardKind.StandardItem, 0, 0);
    AssertReward("祝福油", "祝福油", 1,
        NativeMakeDiamondWithYbProtocol.RewardKind.StandardItem, 0, 1);
}

static void TestSuccessPlanOrderAndCalculations()
{
    var completion = Decode(60,
        BuildPayload("锻造勇士", 150, 900, 3600, 25));
    var context = new NativeMakeDiamondWithYbProtocol.SuccessContext
    {
        AccountPreviouslyInitialized = true,
        PreviousYuanbao = 100,
        CreditCardBonusEnabled = true,
        CreditCardValue2 = int.MaxValue - 10,
        LingFuMultiplier = 3,
        NickLingFuEnabled = true,
        NickLingFuBalance = 20,
        NickLingFuCumulative = 30,
        YbConsumePtId = "PTID-120",
        RewardDescriptor = "经验:123"
    };
    Assert(NativeMakeDiamondWithYbProtocol.TryBuildSuccessPlan(completion,
        context, out var plan, out var error), error);

    EqualText("50 个元宝增加", plan.YuanbaoIncreaseMessage,
        "yuanbao increase notice");
    Equal(150, plan.Snapshot.CurrentYuanbao, "plan snapshot");
    Assert(plan.CreditCardValue2Applied, "credit card bonus not applied");
    Assert(plan.CreditCardMarkedDirty, "credit card dirty flag missing");
    Equal(0, plan.CreditCardValue2After, "credit overflow clamp");
    Equal(180, plan.NickLingFuDelta, "LFMultiple delta");
    Assert(plan.NickLingFuApplied, "nick LingFu not applied");
    Equal(200, plan.NickLingFuBalanceAfter, "nick LingFu balance");
    Equal(210, plan.NickLingFuCumulativeAfter,
        "nick LingFu cumulative");
    EqualText("您获得了180张圣殿灵符", plan.NickLingFuMessage,
        "nick LingFu message");
    EqualText("PTID-120", plan.YbConsumePtId, "YBConsume PTID");
    Equal(60, plan.YbConsumeDelta, "YBConsume delta");
    Equal(30, plan.LogType, "game log type");
    EqualText("元宝", plan.LogItemName, "game log item");
    EqualText("申请元宝锻造", plan.LogReason, "game log reason");
    Equal(60, plan.LogQuantity, "game log quantity");
    Equal(111111, plan.LogMakeIndex, "game log MakeIndex");
    Equal(0, plan.RewardDescriptorIndex, "reward descriptor index");
    Equal(123, plan.Reward.ExperienceDelta, "reward experience");
    Equal(2, plan.InternalRefreshCount, "internal refresh count");
    Assert(!plan.SendsYbDbAcknowledgement, "1120 emitted YBDB ACK");
    EqualText("恭喜您申请元宝锻造金刚石成功。" +
              "\\ \\ 并获得了锻造奖品：<经验:123>。" +
              "\\ \\<离开/@exit>", plan.SuccessDialog,
        "success reward dialog");

    var expected = new[]
    {
        NativeMakeDiamondWithYbProtocol.SuccessStep.ShowYuanbaoIncreaseNotice,
        NativeMakeDiamondWithYbProtocol.SuccessStep.ApplyAuthoritativeSnapshot,
        NativeMakeDiamondWithYbProtocol.SuccessStep.QueueFirstInternalRefresh,
        NativeMakeDiamondWithYbProtocol.SuccessStep.ApplyCreditCardValue2,
        NativeMakeDiamondWithYbProtocol.SuccessStep.ApplyNickLingFu,
        NativeMakeDiamondWithYbProtocol.SuccessStep.AccumulateYbConsume,
        NativeMakeDiamondWithYbProtocol.SuccessStep.WriteGameDataLog,
        NativeMakeDiamondWithYbProtocol.SuccessStep.SelectRewardDescriptor,
        NativeMakeDiamondWithYbProtocol.SuccessStep.GrantConfiguredReward,
        NativeMakeDiamondWithYbProtocol.SuccessStep.QueueSecondInternalRefresh,
        NativeMakeDiamondWithYbProtocol.SuccessStep.ShowSuccessDialog
    };
    SequenceEqual(expected, plan.OrderedSteps, "success action order");
}

static void TestDisabledAndOverflowCalculations()
{
    var completion = Decode(49,
        BuildPayload("锻造勇士", int.MinValue, 2, 3, 4));
    var context = new NativeMakeDiamondWithYbProtocol.SuccessContext
    {
        AccountPreviouslyInitialized = true,
        PreviousYuanbao = int.MaxValue,
        CreditCardBonusEnabled = false,
        CreditCardValue2 = 77,
        LingFuMultiplier = int.MaxValue,
        NickLingFuEnabled = false,
        NickLingFuBalance = 88,
        NickLingFuCumulative = 99,
        RewardDescriptor = "经验:999"
    };
    Assert(NativeMakeDiamondWithYbProtocol.TryBuildSuccessPlan(completion,
        context, out var plan, out var error), error);
    EqualText("1 个元宝增加", plan.YuanbaoIncreaseMessage,
        "overflowed yuanbao notice");
    Assert(!plan.CreditCardValue2Applied,
        "disabled credit card bonus applied");
    Equal(77, plan.CreditCardValue2After, "disabled credit value");
    Equal(unchecked(49 * int.MaxValue), plan.NickLingFuDelta,
        "unchecked LFMultiple product");
    Assert(!plan.NickLingFuApplied, "disabled nick LingFu applied");
    Equal(88, plan.NickLingFuBalanceAfter, "disabled nick balance");
    Equal(99, plan.NickLingFuCumulativeAfter,
        "disabled nick cumulative");
    EqualText(string.Empty, plan.NickLingFuMessage,
        "disabled nick message");
    Equal(-1, plan.RewardDescriptorIndex, "low reward descriptor index");
    Assert(plan.Reward == null,
        "low result granted caller-supplied reward descriptor");
    EqualText("恭喜您申请元宝锻造金刚石成功。\\ \\<离开/@exit>",
        plan.SuccessDialog, "success dialog without reward");
    Assert(plan.OrderedSteps.Contains(
        NativeMakeDiamondWithYbProtocol.SuccessStep.SelectRewardDescriptor),
        "low result omitted native clear/select step");
    Assert(!plan.OrderedSteps.Contains(
        NativeMakeDiamondWithYbProtocol.SuccessStep.GrantConfiguredReward),
        "low result includes grant step");

    Assert(!NativeMakeDiamondWithYbProtocol.TryBuildSuccessPlan(
        Decode(0, BuildPayload("锻造勇士", 1, 2, 3, 4)), context,
        out _, out _), "zero result built success plan");
    Assert(!NativeMakeDiamondWithYbProtocol.TryBuildSuccessPlan(null,
        context, out _, out _), "null completion built success plan");
    Assert(!NativeMakeDiamondWithYbProtocol.TryBuildSuccessPlan(completion,
        null, out _, out _), "null context built success plan");
}

static void TestRuntimeRemainsFailClosed()
{
    var root = FindRepositoryRoot();
    var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    var marker = "case \"makediamondwithyb\":";
    var markerOffset = bridgeSource.IndexOf(marker, StringComparison.Ordinal);
    Assert(markerOffset >= 0, "MakeDiamondWithYB PAS case is missing");
    Assert(bridgeSource.IndexOf(marker, markerOffset + marker.Length,
            StringComparison.Ordinal) < 0,
        "MakeDiamondWithYB PAS case is duplicated");
    var bridgeRegion = bridgeSource.Substring(markerOffset,
        Math.Min(360, bridgeSource.Length - markerOffset));
    Assert(bridgeRegion.Contains(
            "return RejectUnsupportedNativeApi(out result);",
            StringComparison.Ordinal),
        "MakeDiamondWithYB PAS case is no longer fail-closed");

    var ybDbSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Services", "YbDbClient.cs"));
    Assert(!ybDbSource.Contains("NativeMakeDiamondWithYbProtocol",
            StringComparison.Ordinal),
        "YbDbClient references the dormant MakeDiamondWithYB model");
    Assert(!ybDbSource.Contains("1120", StringComparison.Ordinal),
        "YbDbClient consumes 1120 before the native transaction is closed");

    var protocolPath = Path.GetFullPath(Path.Combine(root, "GameSvr",
        "Services", "NativeMakeDiamondWithYbProtocol.cs"));
    foreach (var file in Directory.EnumerateFiles(
                 Path.Combine(root, "GameSvr"), "*.cs",
                 SearchOption.AllDirectories))
    {
        var fullPath = Path.GetFullPath(file);
        if (string.Equals(fullPath, protocolPath,
                StringComparison.OrdinalIgnoreCase)
            || fullPath.Contains(Path.DirectorySeparatorChar + "bin" +
                                 Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || fullPath.Contains(Path.DirectorySeparatorChar + "obj" +
                                 Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        Assert(!File.ReadAllText(fullPath).Contains(
                "NativeMakeDiamondWithYbProtocol", StringComparison.Ordinal),
            "dormant MakeDiamondWithYB model has a runtime reference: " +
            Path.GetRelativePath(root, fullPath));
    }
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "repository root containing GameSvr/GameSvr.csproj was not found");
}

static NativeMakeDiamondWithYbProtocol.Completion Decode(int result,
    byte[] payload)
{
    Assert(NativeMakeDiamondWithYbProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(result, 123456, 1120, payload),
        out var completion, out var error), error);
    return completion;
}

static byte[] BuildPayload(string roleName, int yuanbao, int consumed,
    int duration, int dividend)
{
    var payload = new byte[32];
    var roleBytes = Encoding.GetEncoding(936).GetBytes(roleName);
    Assert(roleBytes.Length <= 15, "test role exceeds native slot");
    payload[0] = (byte)roleBytes.Length;
    roleBytes.CopyTo(payload, 1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), yuanbao);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4), consumed);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4), duration);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(28, 4), dividend);
    return payload;
}

static void AssertReward(string descriptor, string name, int count,
    NativeMakeDiamondWithYbProtocol.RewardKind kind, int builtInDelta,
    int itemAttempts)
{
    var reward = NativeMakeDiamondWithYbProtocol.ParseRewardDescriptor(
        descriptor);
    EqualText(descriptor, reward.Descriptor, "reward descriptor");
    EqualText(name, reward.Name, "reward name");
    Equal(count, reward.Count, "reward count");
    Assert(reward.Kind == kind, "reward kind");
    var actualBuiltIn = reward.ExperienceDelta + reward.ReputationDelta +
                        reward.DiamondDelta;
    Equal(builtInDelta, actualBuiltIn, "built-in reward delta");
    Equal(itemAttempts, reward.StandardItemCreateAttempts,
        "item create attempts");
}

static void SequenceEqual<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string message)
{
    Equal(expected.Count, actual.Count, message + " count");
    for (var index = 0; index < expected.Count; index++)
    {
        if (!EqualityComparer<T>.Default.Equals(expected[index], actual[index]))
        {
            throw new InvalidOperationException(
                $"{message}[{index}]: expected {expected[index]}, " +
                $"actual {actual[index]}");
        }
    }
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualUInt(uint expected, uint actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{message}: expected [{expected}], actual [{actual}]");
}

static void Throws<T>(Action action, string message) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
