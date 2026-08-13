using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
TestCompletionDecode();
TestCompletionRejectsInvalidWire();
TestFailureDialogsAndAck();
TestLevelTable();
TestGrantCalculation();
TestDelphiRandom();
TestBountyDrawIsTheGlobalRandSeed();
TestNativeEvidenceContract();
TestDormantRuntimeBoundary();

Console.WriteLine(
    "PASS QuestDiamond 1122/32 role=permissive-CP936 ACK=105/106 " +
    "level=exact experience=Delphi-overflow bounty=12 " +
    "BF0=unchecked-add ACK=state-matrix relog=name-current " +
    "rawGBK=51/comma runtime=closed " +
    "bounty-draw=global-Delphi-RandSeed");
return;

// TestBountyDrawIsTheGlobalRandSeed touches M2Share, whose static ctor loads
// !Setup.txt / String.ini / Command.conf and ..\Share\PlayerUpgradeExp.ini and throws
// when they are absent — which aborted the run before any assertion reported. Same
// minimal skeleton the other GameSvr audits lay down; nothing else is booted.
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

static void TestCompletionDecode()
{
    var payload = BuildPayload("金刚勇士", 7, 5);
    var frame = new YbDbLegacy77Frame(938_475, 0x12345678,
        NativeQuestDiamondProtocol.CompletionIdent, payload);
    Assert(NativeQuestDiamondProtocol.TryDecodeCompletion(frame,
            out var completion, out var error), error);
    Equal(938_475, completion.Result, "completion result/query id");
    EqualText("金刚勇士", completion.RoleName, "completion role name");
    Equal(7, completion.FirstCount, "completion first count");
    Equal(5, completion.SecondCount, "completion second count");
}

static void TestCompletionRejectsInvalidWire()
{
    Assert(!NativeQuestDiamondProtocol.TryDecodeCompletion(null,
        out _, out _), "null completion accepted");
    Assert(!NativeQuestDiamondProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(1, 2, 1121, new byte[32]),
        out _, out _), "wrong completion ident accepted");
    Assert(!NativeQuestDiamondProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(1, 2, 1122, new byte[31]),
        out _, out _), "short completion accepted");
    Assert(!NativeQuestDiamondProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(1, 2, 1122, new byte[33]),
        out _, out _), "long completion accepted");

    var longRole = new byte[32];
    longRole[0] = 16;
    Assert(!NativeQuestDiamondProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(1, 2, 1122, longRole),
        out _, out _), "16-byte role slot accepted");

    var malformedGbk = new byte[32];
    malformedGbk[0] = 1;
    malformedGbk[1] = 0x81;
    Assert(NativeQuestDiamondProtocol.TryDecodeCompletion(
        new YbDbLegacy77Frame(1, 2, 1122, malformedGbk),
        out var malformed, out var malformedError), malformedError);
    Equal(1, malformed.RoleNameGbkBytes.Length,
        "malformed role raw byte length");
    Equal(0x81, malformed.RoleNameGbkBytes.Span[0],
        "malformed role raw byte");
    Assert(!string.IsNullOrEmpty(malformed.RoleName),
        "permissive CP936 role replacement is empty");
}

static void TestFailureDialogsAndAck()
{
    EqualText("锻造未完成 或 没有锻造\\ \\<返回/@Main>",
        NativeQuestDiamondProtocol.BuildFailureDialog(-1), "result -1");
    EqualText("对不起，你没有完成锻造那么多颗金刚石。\\ \\<返回/@askybdiam>",
        NativeQuestDiamondProtocol.BuildFailureDialog(-3), "result -3");
    EqualText("没有领取金刚石丢失的记录\\ \\<返回/@Main>",
        NativeQuestDiamondProtocol.BuildFailureDialog(-4), "result -4");
    EqualText("领取金刚石失败(0)\\ \\<返回/@Main>",
        NativeQuestDiamondProtocol.BuildFailureDialog(0), "generic result");

    AssertAck(NativeQuestDiamondProtocol.CreateAck(938_475, true),
        105, 938_475);
    AssertAck(NativeQuestDiamondProtocol.CreateAck(938_475, false),
        106, 938_475);
}

static void TestLevelTable()
{
    var cases = new Dictionary<ushort, int>
    {
        [0] = 0, [1] = 57_000, [7] = 57_000,
        [8] = 75_000, [14] = 75_000,
        [15] = 90_000, [18] = 90_000,
        [19] = 105_000, [21] = 105_000,
        [22] = 120_000, [24] = 120_000,
        [25] = 135_000, [27] = 135_000,
        [28] = 150_000, [30] = 150_000,
        [31] = 180_000, [34] = 180_000,
        [35] = 210_000, [37] = 210_000,
        [38] = 240_000, [40] = 240_000,
        [41] = 270_000, [42] = 270_000,
        [43] = 285_000, [44] = 285_000,
        [45] = 300_000, [46] = 330_000,
        [47] = 345_000, [48] = 345_000,
        [49] = 360_000, [50] = 360_000,
        [51] = 375_000, [52] = 375_000,
        [53] = 390_000, [54] = 390_000,
        [55] = 405_000, [56] = 405_000,
        [57] = 420_000, [58] = 420_000,
        [59] = 450_000, [999] = 450_000,
        [ushort.MaxValue] = 450_000
    };

    foreach (var pair in cases)
        Equal(pair.Value,
            NativeQuestDiamondProtocol.GetLevelExperienceBase(pair.Key),
            "level " + pair.Key);
}

static void TestGrantCalculation()
{
    uint observedRange = 0;
    Assert(NativeQuestDiamondProtocol.TryCalculateGrant(1, 2, 3,
        range =>
        {
            observedRange = range;
            return 12_345;
        }, out var grant), "level-one grant failed");
    Equal(5, grant.Total, "grant total");
    Equal(5, grant.DiamondCacheDelta, "BF0 delta");
    Equal(105, unchecked(100 + grant.DiamondCacheDelta),
        "evidence-only BF0 additive projection");
    Equal(57_000, grant.LevelExperienceBase, "level base");
    Equal(456_000, grant.WeightedExperience, "weighted experience");
    Equal(91_200, grant.SignedRandomBound, "signed random bound");
    EqualUInt(91_200, observedRange, "native random range");
    Equal(12_345, grant.RandomValue, "random value");
    Equal(489_255, grant.Experience, "final experience");
    Assert(!grant.ReceivesBounty, "five diamonds received bounty");
    EqualText("你成功领取金刚石 5 颗！获得经验：489255",
        NativeQuestDiamondProtocol.BuildSuccessDialog(
            grant.Total, grant.Experience), "success dialog");
    EqualText("你成功领取金刚石 5 颗！获得经验：489255" +
              "\\ \\ \\<继续领取/@askybdiam>      <关闭/@exit>",
        NativeQuestDiamondProtocol.BuildNpcSuccessDialog(
            grant.Total, grant.Experience), "NPC success dialog");

    Assert(NativeQuestDiamondProtocol.TryCalculateGrant(45, 6, 6,
        _ => 0, out grant), "12-diamond grant failed");
    Assert(grant.ReceivesBounty, "12 diamonds missed bounty");

    var randomCalled = false;
    Assert(!NativeQuestDiamondProtocol.TryCalculateGrant(0, 1, 1,
        _ =>
        {
            randomCalled = true;
            return 0;
        }, out _), "level-zero grant succeeded");
    Assert(!randomCalled, "level-zero grant called Random");

    const int first = int.MaxValue;
    const int second = int.MaxValue;
    var expectedTotal = unchecked(first + second);
    var expectedWeighted = unchecked(450_000 *
        unchecked(first + unchecked(second * 2)));
    Assert(NativeQuestDiamondProtocol.TryCalculateGrant(59, first, second,
        _ => 0, out grant), "overflow grant failed");
    Equal(expectedTotal, grant.Total, "unchecked total");
    Equal(expectedWeighted, grant.WeightedExperience,
        "unchecked weighted experience");

    Assert(NativeQuestDiamondProtocol.TryCalculateGrant(1, -5, 1,
        _ => 0, out grant), "negative-count grant failed");
    Equal(-4, grant.Total, "negative unchecked total");
    Equal(-4, grant.DiamondCacheDelta, "negative BF0 delta");
    Equal(96, unchecked(100 + grant.DiamondCacheDelta),
        "negative evidence-only BF0 additive projection");
    Assert(!grant.ReceivesBounty, "negative total received bounty");
}

static void TestDelphiRandom()
{
    uint state = 0x12345678;
    var expectedState = unchecked(134_775_813u * state + 1u);
    var expected = unchecked((uint)(((ulong)expectedState * 91_200u) >> 32));
    var actual = NativeQuestDiamondProtocol.NextDelphiRandom(
        ref state, 91_200);
    EqualUInt(expectedState, state, "Delphi random state");
    EqualUInt(expected, actual, "Delphi random high product");
}

// POIS-26 closed the blocker this test used to describe. The bounty draw's default
// injectable is `maximum => M2Share.RandomNumber.Random(maximum)`, and that facade now
// IS the global Delphi RandSeed the native bounty path draws from - sub_403B4C
// @0x00403B4C: imul [0x7A2008], 0x08088405 / inc / store / mul bound / keep the high
// 32. So instead of describing which field RandomNumber declares, check that the
// facade and this file's own NextDelphiRandom model produce the same values off the
// same seed. That is the fact the blocker was waiting on.
static void TestBountyDrawIsTheGlobalRandSeed()
{
    M2Share.RandomNumber = RandomNumber.GetInstance();
    Func<int, int> bountyDraw = maximum => M2Share.RandomNumber.Random(maximum);

    var state = 0x12345678u;
    DelphiRandom.Seed = state;
    foreach (uint range in new uint[] { 91_200, 12, 1, 800 })
    {
        var expected = NativeQuestDiamondProtocol.NextDelphiRandom(ref state, range);
        EqualUInt(expected, unchecked((uint)bountyDraw(unchecked((int)range))),
            $"bounty draw range={range} left the global RandSeed");
        EqualUInt(state, DelphiRandom.Seed,
            $"bounty draw range={range} did not advance the shared RandSeed");
    }
}

static void TestNativeEvidenceContract()
{
    // These assertions pin conclusions from the Delphi disassembly. They are
    // evidence-only and do not imply that the dormant C# codec is dispatched.
    var normalOrder = new[]
    {
        "BF0+=total", "AddExp", "success=true", "compose-text",
        "bounty-tokens", "npc-dialog", "capital-10054", "log-33",
        "ACK105"
    };
    EqualText(
        "BF0+=total>AddExp>success=true>compose-text>bounty-tokens>" +
        "npc-dialog>capital-10054>log-33>ACK105",
        string.Join('>', normalOrder), "native normal completion order");

    var ackMatrix = new[]
    {
        "invalid-length:none",
        "result<=0:none",
        "positive-role-missing:106",
        "positive-role-dead:106",
        "positive-role-not-ready:106",
        "positive-level0:106",
        "exception-before-success:106",
        "exception-after-success:105",
        "reward-token-false:105",
        "positive-normal:105"
    };
    EqualText(
        "invalid-length:none|result<=0:none|positive-role-missing:106|" +
        "positive-role-dead:106|positive-role-not-ready:106|" +
        "positive-level0:106|exception-before-success:106|" +
        "exception-after-success:105|reward-token-false:105|" +
        "positive-normal:105",
        string.Join('|', ackMatrix), "native ACK state matrix");

    EqualText("role-name-only/current-object/alive/ReadyRun/" +
              "no-account-ObjectId-generation-correlation",
        "role-name-only/current-object/alive/ReadyRun/" +
        "no-account-ObjectId-generation-correlation",
        "native same-name relog lookup");
    EqualText("raw-51-byte-primary/comma-byte-split/token-result-ignored",
        "raw-51-byte-primary/comma-byte-split/token-result-ignored",
        "native raw GBK reward descriptor contract");
    EqualText("AddLogRec(33,金刚宝石,元宝系统获得,total,empty)",
        "AddLogRec(33,金刚宝石,元宝系统获得,total,empty)",
        "native game-log positional tuple");
}

static void TestDormantRuntimeBoundary()
{
    var root = FindRepositoryRoot();
    var protocol = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "NativeQuestDiamondProtocol.cs"));
    var client = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "YbDbClient.cs"));
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    var cache = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeLingFu.cs"));
    var bounty = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "NativeDiamondBountyConfig.cs"));
    var random = File.ReadAllText(Path.Combine(root, "SystemModule",
        "RandomNumber.cs"));

    Equal(2, Regex.Matches(bridge,
        @"case ""clientquestgetdiam"":\s*" +
        @"return RejectUnsupportedNativeApi\(out result\);",
        RegexOptions.CultureInvariant).Count,
        "ClientQuestGetDiam fail-closed PAS case count");

    var sender = Slice(client, "public bool RequestQuestDiamond(",
        "public bool RequestLingFuAccounting(");
    Require(sender,
        "new YbDbLegacy77Frame(0, amount, 122, payload)",
        "quest-diamond request 122 wire shape changed");
    Require(sender,
        "_outbound.Enqueue(new QueuedSend(_connectionGeneration, frame))",
        "quest-diamond request is not generation-bound at enqueue");
    Reject(sender, "m_nNativeDiamondCache",
        "request 122 mutates the BF0 cache");
    Reject(sender, "GetNativeDiamondCount",
        "request 122 rescans or mutates bag diamonds");
    Reject(sender, "CreateAck(", "request 122 fabricates a completion ACK");

    var completions = Slice(client, "public void ProcessCompletions()",
        "private void SocketConnected(");
    Reject(completions, "NativeQuestDiamondProtocol.CompletionIdent",
        "1122 runtime dispatch was opened through the completion constant");
    Reject(completions, "NativeQuestDiamondProtocol.TryDecodeCompletion",
        "1122 runtime decoder was wired into ProcessCompletions");
    Assert(!Regex.IsMatch(completions,
            @"(?:frame|queued\.Frame)\.Ident\s*==?\s*1122",
            RegexOptions.CultureInvariant),
        "1122 literal runtime dispatch was opened");

    Reject(protocol, "TPlayObject",
        "dormant quest-diamond protocol gained a player dependency");
    Reject(protocol, "m_nNativeDiamondCache",
        "dormant quest-diamond protocol gained a BF0 writer");
    Reject(protocol, "ProcessCompletions",
        "dormant quest-diamond protocol gained runtime dispatch");
    Reject(protocol, "SendMsg(",
        "dormant quest-diamond protocol gained player messaging");

    Equal(1, Regex.Matches(cache,
        @"m_nNativeDiamondCache\s*=\s*GetNativeDiamondCount\(\)",
        RegexOptions.CultureInvariant).Count,
        "native diamond cache login assignment count");
    Assert(!Regex.IsMatch(cache,
            @"m_nNativeDiamondCache\s*\+=",
            RegexOptions.CultureInvariant),
        "quest-diamond BF0 runtime increment opened early");

    Require(bounty, "DescriptorMaximumGbkBytes = 0x33",
        "dormant bounty primary descriptor is not capped at 51 raw bytes");
    Require(bounty, "result.Add((byte)',')",
        "dormant bounty raw descriptor no longer appends byte commas");
    Require(bounty, "public bool TrySelectGbk(out byte[] descriptor)",
        "dormant bounty raw GBK selector is missing");
    Require(bounty, "maximum => M2Share.RandomNumber.Random(maximum)",
        "diamond bounty stopped drawing through the global random owner");
    // 这条断言的字面串 "private static Random random" 现在只出现在
    // RandomNumber.cs 的注释里（解释这个字段已经不属于本类），Contains 命中的
    // 是散文不是代码。改成剥掉行注释后再查真实的 System.Random 构造，并正面
    // 要求 owner 就是 Delphi RandSeed：
    //   0x00403B4C 53 / 31 db / 69 93 08 20 7a 00 05 84 08 08
    //              imul edx,[0x7A2008],0x08088405 / 42 inc edx /
    //              89 93 08 20 7a 00 mov [0x7A2008],edx / f7 e2 mul edx /
    //              89 d0 mov eax,edx / 5b c3
    var randomCode = StripLineComments(random);
    Reject(randomCode, "new Random(",
        "global random owner left the Delphi RandSeed for System.Random");
    Require(randomCode, "DelphiRandomNumberFacade",
        "global random owner is no longer the Delphi RandSeed facade");
}

static string StripLineComments(string source) =>
    Regex.Replace(source, @"//[^\n]*", string.Empty);

static byte[] BuildPayload(string roleName, int firstCount, int secondCount)
{
    var payload = new byte[32];
    var roleBytes = Encoding.GetEncoding(936).GetBytes(roleName);
    Assert(roleBytes.Length <= 15, "test role exceeds native slot");
    payload[0] = (byte)roleBytes.Length;
    roleBytes.CopyTo(payload, 1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), firstCount);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4), secondCount);
    return payload;
}

static void AssertAck(YbDbLegacy77Frame ack, ushort ident,
    int transactionCode)
{
    Equal(NativeQuestDiamondProtocol.CompletionIdent, ack.QueryId,
        "ACK query id");
    Equal(transactionCode, ack.Param, "ACK transaction code");
    Equal(ident, ack.Ident, "ACK ident");
    Equal(0, ack.Payload.Length, "ACK payload length");
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

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Assert(start >= 0, "source start marker missing: " + startMarker);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Assert(end > start, "source end marker missing: " + endMarker);
    return source.Substring(start, end - start);
}

static void Require(string source, string value, string message)
{
    Assert(source.Contains(value, StringComparison.Ordinal), message);
}

static void Reject(string source, string value, string message)
{
    Assert(!source.Contains(value, StringComparison.Ordinal), message);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(start);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException(
        "repository root containing GameSvr/GameSvr.csproj was not found");
}
