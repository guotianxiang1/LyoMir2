using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
TestInvalidFrameIsIgnored();
TestNegativeResultsNeverAck();
TestPositiveEligibilityFailuresAck106();
TestLevelZeroAck106();
TestNormalOrderAndAck105();
TestRawRewardTokensAndIgnoredFalse();
TestExceptionStateFlag();
TestDuplicateCompletionHasNoDedupe();
TestAckFailureIsNotRetried();
TestDormantIntegrationBoundary();

Console.WriteLine(
    "PASS QuestDiamondCompletion 1122 state-machine ACK=positive-only " +
    "failure-dialog=negative eligible=name/alive/ready " +
    "partial-exception=success-flag rewards=raw-byte/no-rollback " +
    "duplicate=no-dedupe runtime=dormant blockers=RandSeed+generation");
return;

static void TestInvalidFrameIsIgnored()
{
    var host = new FakeHost { Target = new FakeTarget() };
    var disposition = NativeQuestDiamondCompletionStateMachine.Process(
        new YbDbLegacy77Frame(1, 0, 1122, new byte[31]), host);
    Equal(NativeQuestDiamondCompletionDisposition.InvalidFrameIgnored,
        disposition, "invalid frame disposition");
    Equal(0, host.Events.Count, "invalid frame side effects");
}

static void TestNegativeResultsNeverAck()
{
    var cases = new Dictionary<int, string>
    {
        [-1] = "锻造未完成 或 没有锻造\\ \\<返回/@Main>",
        [-3] = "对不起，你没有完成锻造那么多颗金刚石。\\ \\<返回/@askybdiam>",
        [-4] = "没有领取金刚石丢失的记录\\ \\<返回/@Main>",
        [-9] = "领取金刚石失败(-9)\\ \\<返回/@Main>"
    };

    foreach (var pair in cases)
    {
        var host = new FakeHost { Target = new FakeTarget() };
        var disposition = NativeQuestDiamondCompletionStateMachine.Process(
            Frame(pair.Key, 7, 5), host);
        Equal(NativeQuestDiamondCompletionDisposition.NegativeResultNoAck,
            disposition, "negative disposition " + pair.Key);
        Sequence(new[] { "resolve:测试角色", "failure:" + pair.Value },
            host.Events, "negative events " + pair.Key);
        Equal(0, host.Acks.Count, "negative ACK " + pair.Key);
    }

    foreach (var target in new FakeTarget[]
             {
                 null,
                 new() { IsDead = true },
                 new() { IsReadyRun = false }
             })
    {
        var host = new FakeHost { Target = target };
        _ = NativeQuestDiamondCompletionStateMachine.Process(
            Frame(-1, 1, 1), host);
        Sequence(new[] { "resolve:测试角色" }, host.Events,
            "ineligible negative events");
        Equal(0, host.Acks.Count, "ineligible negative ACK");
    }
}

static void TestPositiveEligibilityFailuresAck106()
{
    foreach (var target in new FakeTarget[]
             {
                 null,
                 new() { IsDead = true },
                 new() { IsReadyRun = false }
             })
    {
        var host = new FakeHost { Target = target };
        var disposition = NativeQuestDiamondCompletionStateMachine.Process(
            Frame(991, 1, 1), host);
        Equal(NativeQuestDiamondCompletionDisposition.PositiveFailureAck,
            disposition, "ineligible positive disposition");
        Sequence(new[] { "resolve:测试角色", "ack:106:991" },
            host.Events, "ineligible positive events");
        AssertAck(host.Acks.Single(), 106, 991);
    }
}

static void TestLevelZeroAck106()
{
    var host = new FakeHost { Target = new FakeTarget { Level = 0 } };
    var disposition = NativeQuestDiamondCompletionStateMachine.Process(
        Frame(992, 7, 5), host);
    Equal(NativeQuestDiamondCompletionDisposition.PositiveFailureAck,
        disposition, "level-zero disposition");
    Sequence(new[] { "resolve:测试角色", "ack:106:992" }, host.Events,
        "level-zero events");
}

static void TestNormalOrderAndAck105()
{
    var host = new FakeHost
    {
        Target = new FakeTarget { HasNpc = true },
        RandomResult = 12_345
    };
    var disposition = NativeQuestDiamondCompletionStateMachine.Process(
        Frame(993, 2, 3), host);
    Equal(NativeQuestDiamondCompletionDisposition.PositiveSuccessAck,
        disposition, "normal disposition");
    Sequence(new[]
    {
        "resolve:测试角色",
        "cache:5",
        "random:91200",
        "experience:489255:True:True:0",
        "success:你成功领取金刚石 5 颗！获得经验：489255" +
        "\\ \\ \\<继续领取/@askybdiam>      <关闭/@exit>",
        "refresh",
        "log:33:金刚宝石:元宝系统获得:5:",
        "ack:105:993"
    }, host.Events, "normal event order");
    AssertAck(host.Acks.Single(), 105, 993);
}

static void TestRawRewardTokensAndIgnoredFalse()
{
    var host = new FakeHost
    {
        Target = new FakeTarget(),
        Bounty = new byte[]
        {
            0x81, (byte)':', (byte)'1', (byte)',', (byte)',',
            (byte)'A', (byte)','
        },
        RewardResults = new Queue<bool>(new[] { false, false, true })
    };
    var disposition = NativeQuestDiamondCompletionStateMachine.Process(
        Frame(994, 6, 6), host);
    Equal(NativeQuestDiamondCompletionDisposition.PositiveSuccessAck,
        disposition, "raw reward disposition");
    Equal(3, host.RewardTokens.Count, "raw reward token count");
    Bytes(new byte[] { 0x81, (byte)':', (byte)'1' }, host.RewardTokens[0],
        "raw first token");
    Bytes(Array.Empty<byte>(), host.RewardTokens[1],
        "raw empty middle token");
    Bytes(new byte[] { (byte)'A' }, host.RewardTokens[2],
        "raw final token");
    Assert(host.Events.IndexOf("reward:41") < host.Events.IndexOf("refresh"),
        "reward did not precede refresh");
    AssertAck(host.Acks.Single(), 105, 994);
}

static void TestExceptionStateFlag()
{
    var before = new FakeHost
    {
        Target = new FakeTarget(),
        ThrowEvent = "random"
    };
    var disposition = NativeQuestDiamondCompletionStateMachine.Process(
        Frame(995, 2, 3), before);
    Equal(NativeQuestDiamondCompletionDisposition.PositiveFailureAck,
        disposition, "pre-success exception disposition");
    Sequence(new[]
    {
        "resolve:测试角色", "cache:5", "random:91200",
        "exception:random", "ack:106:995"
    }, before.Events, "pre-success exception events");

    var after = new FakeHost
    {
        Target = new FakeTarget(),
        ThrowEvent = "refresh"
    };
    disposition = NativeQuestDiamondCompletionStateMachine.Process(
        Frame(996, 2, 3), after);
    Equal(NativeQuestDiamondCompletionDisposition.PositiveSuccessAck,
        disposition, "post-success exception disposition");
    Assert(after.Events.Contains("experience:501600:True:True:0"),
        "post-success exception missed experience");
    Assert(after.Events.Contains("exception:refresh"),
        "post-success exception was not reported");
    Assert(!after.Events.Any(value => value.StartsWith("log:",
        StringComparison.Ordinal)), "post-success exception continued to log");
    AssertAck(after.Acks.Single(), 105, 996);
}

static void TestDuplicateCompletionHasNoDedupe()
{
    var host = new FakeHost { Target = new FakeTarget() };
    var frame = Frame(997, 1, 1);
    _ = NativeQuestDiamondCompletionStateMachine.Process(frame, host);
    _ = NativeQuestDiamondCompletionStateMachine.Process(frame, host);
    Equal(2, host.Events.Count(value => value == "cache:2"),
        "duplicate cache mutation count");
    Equal(2, host.Events.Count(value => value == "ack:105:997"),
        "duplicate ACK count");
    Equal(2, host.Acks.Count, "duplicate frame was deduplicated");
}

static void TestAckFailureIsNotRetried()
{
    var host = new FakeHost
    {
        Target = new FakeTarget(),
        AckResult = false
    };
    var disposition = NativeQuestDiamondCompletionStateMachine.Process(
        Frame(998, 1, 1), host);
    Equal(NativeQuestDiamondCompletionDisposition.PositiveSuccessAck,
        disposition, "failed ACK send changed completion result");
    Equal(1, host.Acks.Count, "failed ACK was retried");
}

static void TestDormantIntegrationBoundary()
{
    var root = FindRepositoryRoot();
    var helper = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "NativeQuestDiamondCompletionStateMachine.cs"));
    var client = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "YbDbClient.cs"));
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    var random = File.ReadAllText(Path.Combine(root, "SystemModule",
        "RandomNumber.cs"));

    foreach (var forbidden in new[]
             {
                 "DelphiRandom", "RandomNumber", "Generation", "ObjectId",
                 "Ptid", "Pending"
             })
    {
        Reject(helper, forbidden,
            "completion helper gained forbidden correlation/runtime owner: " +
            forbidden);
    }

    var completions = Slice(client, "public void ProcessCompletions()",
        "private void SocketConnected(");
    Reject(completions, "NativeQuestDiamondCompletionStateMachine",
        "dormant completion helper was wired into YbDbClient");
    Reject(completions, "NativeQuestDiamondProtocol.CompletionIdent",
        "1122 completion dispatcher was opened");
    Require(completions,
        "if (!IsCurrentSessionLocked(_currentSocket, queued.Generation))",
        "YbDb generation blocker unexpectedly disappeared");
    Equal(2, Regex.Matches(bridge,
        @"case ""clientquestgetdiam"":\s*" +
        @"return RejectUnsupportedNativeApi\(out result\);",
        RegexOptions.CultureInvariant).Count,
        "ClientQuestGetDiam fail-closed PAS count");
    // 原断言要求 RandomNumber.cs 里不许出现 DelphiRandom —— 那是切换尚未收口
    // 时期的护栏。切换已经收口并且有字节：sub_403B4C @0x00403B4C
    //   53 / 31 db / 69 93 08 20 7a 00 05 84 08 08 (imul edx,[0x7A2008],0x08088405)
    //   42 / 89 93 08 20 7a 00 / f7 e2 (mul edx) / 89 d0 / 5b c3
    // 即 result = high32(bound * (seed*0x08088405 + 1))，种子在 0x007A2008。
    // 专用闸 DelphiRandomNumberFacadeCompatCheck / NativePasRandomContractCompatCheck
    // / RngTraceSinkOffIdenticalCheck 三把均 PASS。这里保留的仍是「进程级 owner
    // 只能有一个」这个契约，只是方向反过来：必须是 Delphi 门面，且不得再 new 一个
    // System.Random。
    Require(random, "DelphiRandomNumberFacade",
        "process-wide RandomNumber is no longer the Delphi RandSeed facade");
    Reject(Regex.Replace(random, @"//[^\n]*", string.Empty), "new Random(",
        "process-wide RandomNumber took a System.Random back");
}

static YbDbLegacy77Frame Frame(int result, int first, int second)
{
    var payload = new byte[32];
    var role = Encoding.GetEncoding(936).GetBytes("测试角色");
    payload[0] = (byte)role.Length;
    role.CopyTo(payload, 1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), first);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4), second);
    return new YbDbLegacy77Frame(result, 0x12345678, 1122, payload);
}

static void AssertAck(YbDbLegacy77Frame ack, ushort ident, int transaction)
{
    Equal(1122, ack.QueryId, "ACK query id");
    Equal(transaction, ack.Param, "ACK transaction");
    Equal(ident, ack.Ident, "ACK ident");
    Equal(0, ack.Payload.Length, "ACK payload");
}

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "GameSvr",
                "GameSvr.csproj")))
            return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("repository root not found");
}

static string Slice(string text, string start, string end)
{
    var startIndex = text.IndexOf(start, StringComparison.Ordinal);
    if (startIndex < 0) throw new InvalidOperationException("missing: " + start);
    var endIndex = text.IndexOf(end, startIndex, StringComparison.Ordinal);
    if (endIndex < 0) throw new InvalidOperationException("missing: " + end);
    return text.Substring(startIndex, endIndex - startIndex);
}

static void Require(string text, string value, string message)
{
    if (!text.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Reject(string text, string value, string message)
{
    if (text.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Bytes(byte[] expected, byte[] actual, string message)
{
    if (!expected.AsSpan().SequenceEqual(actual))
        throw new InvalidOperationException(message + ": expected " +
            Convert.ToHexString(expected) + ", actual " +
            Convert.ToHexString(actual));
}

static void Sequence(IReadOnlyList<string> expected,
    IReadOnlyList<string> actual, string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(message + ": expected [" +
            string.Join(" | ", expected) + "], actual [" +
            string.Join(" | ", actual) + "]");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeHost : INativeQuestDiamondCompletionHost
{
    public FakeTarget Target { get; set; }
    public int RandomResult { get; set; }
    public byte[] Bounty { get; set; }
    public Queue<bool> RewardResults { get; set; } = new();
    public string ThrowEvent { get; set; }
    public bool AckResult { get; set; } = true;
    public List<string> Events { get; } = new();
    public List<byte[]> RewardTokens { get; } = new();
    public List<YbDbLegacy77Frame> Acks { get; } = new();

    public INativeQuestDiamondCompletionTarget FindCurrentRole(string roleName)
    {
        Events.Add("resolve:" + roleName);
        if (Target != null) Target.Host = this;
        return Target;
    }

    public int NextNativeRandom(int range)
    {
        Events.Add("random:" + range);
        ThrowIf("random");
        return RandomResult;
    }

    public bool TrySelectBountyGbk(out byte[] descriptor)
    {
        Events.Add("bounty");
        ThrowIf("bounty");
        descriptor = Bounty;
        return descriptor != null;
    }

    public bool EnqueueAck(YbDbLegacy77Frame frame)
    {
        Acks.Add(frame);
        Events.Add("ack:" + frame.Ident + ":" + frame.Param);
        return AckResult;
    }

    public void ReportGiveException(Exception exception) =>
        Events.Add("exception:" + exception.Message);

    internal void ThrowIf(string eventName)
    {
        if (string.Equals(ThrowEvent, eventName, StringComparison.Ordinal))
            throw new InvalidOperationException(eventName);
    }
}

sealed class FakeTarget : INativeQuestDiamondCompletionTarget
{
    public FakeHost Host { get; set; }
    public ushort Level { get; set; } = 1;
    public bool IsDead { get; set; }
    public bool IsReadyRun { get; set; } = true;
    public bool HasNpc { get; set; }

    public void AddDiamondCacheUnchecked(int amount)
    {
        Host.Events.Add("cache:" + amount);
        Host.ThrowIf("cache");
    }

    public void GrantExperience(int amount, bool shareWithHero,
        bool countAsFightExperience, int experienceMode)
    {
        Host.Events.Add($"experience:{amount}:{shareWithHero}:" +
                        $"{countAsFightExperience}:{experienceMode}");
        Host.ThrowIf("experience");
    }

    public bool ExecuteRewardTokenGbk(ReadOnlyMemory<byte> descriptor)
    {
        var token = descriptor.ToArray();
        Host.RewardTokens.Add(token);
        Host.Events.Add("reward:" + Convert.ToHexString(token));
        Host.ThrowIf("reward");
        return Host.RewardResults.Count == 0 || Host.RewardResults.Dequeue();
    }

    public void ShowFailureDialog(string text)
    {
        Host.Events.Add("failure:" + text);
        Host.ThrowIf("failure");
    }

    public void ShowNpcSuccessDialog(string text)
    {
        Host.Events.Add("success:" + text);
        Host.ThrowIf("success");
    }

    public void RefreshCapital()
    {
        Host.Events.Add("refresh");
        Host.ThrowIf("refresh");
    }

    public void WriteGameLog(int type, string itemName, string reason,
        int count, string detail)
    {
        Host.Events.Add($"log:{type}:{itemName}:{reason}:{count}:{detail}");
        Host.ThrowIf("log");
    }
}
