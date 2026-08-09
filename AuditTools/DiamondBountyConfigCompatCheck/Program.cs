using System.Text;
using GameSvr;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
TestGbkAndInclusiveBoundaries();
TestApplicationRewardPools();
TestFirstEmptyAndNativeFallback();
TestFixedHundredEntryLimit();
TestNativeOddGbkByteBoundary();
TestMissingAndInvalidInputs();
Console.WriteLine(
    "PASS DiamondBounty GBK sections=4 entries=100 random=0..99 " +
    "inclusive=true first-empty=true fallback=first dormant=true");
return;

static void TestGbkAndInclusiveBoundaries()
{
    WithConfig(
        "[领取奖励]\r\n" +
        "奖品1=矿洞组队卷轴/18\r\n" +
        "奖品2=神殿组队卷轴/36\r\n" +
        "奖品3=祝福油/99\r\n" +
        "[领取额外奖励]\r\n" +
        "奖品1=金刚石:1\r\n" +
        "奖品2=灵符:2\r\n",
        fileName =>
        {
            var rolls = new Queue<int>(new[] { 0, 18, 19, 36, 37, 99 });
            int Random(int maximum)
            {
                Equal(NativeDiamondBountyConfig.RandomRange, maximum,
                    "native random maximum");
                return rolls.Dequeue();
            }

            Load(fileName, Random, out var config);
            Assert(config.SourceLoaded, "GBK source was not marked loaded");
            Equal(3, config.ClaimRewards.Count, "claim reward count");
            Equal(2, config.AdditionalRewards.Count,
                "additional reward count");
            EqualText("矿洞组队卷轴", config.ClaimRewards[0].Descriptor,
                "GBK first descriptor");
            EqualText("金刚石:1", config.AdditionalRewards[0],
                "GBK additional descriptor");

            var expected = new[]
            {
                "矿洞组队卷轴,金刚石:1,灵符:2",
                "矿洞组队卷轴,金刚石:1,灵符:2",
                "神殿组队卷轴,金刚石:1,灵符:2",
                "神殿组队卷轴,金刚石:1,灵符:2",
                "祝福油,金刚石:1,灵符:2",
                "祝福油,金刚石:1,灵符:2"
            };
            foreach (var descriptor in expected)
            {
                Assert(config.TrySelect(out var actual),
                    "configured selection failed");
                EqualText(descriptor, actual, "inclusive threshold selection");
            }
            Equal(0, rolls.Count, "deterministic random call count");
        });
}

static void TestApplicationRewardPools()
{
    WithConfig(
        "[领取奖励]\r\n" +
        "[申请奖励1]\r\n" +
        "奖品1=经验:50/10\r\n" +
        "奖品2=声望:3/99\r\n" +
        "[申请奖励2]\r\n" +
        "奖品1=金刚石:2/20\r\n" +
        "奖品2=屠龙:1/99\r\n",
        fileName =>
        {
            var rolls = new Queue<int>(new[] { 10, 11, 20, 21, 0 });
            Load(fileName, _ => rolls.Dequeue(), out var config);
            Equal(2, config.ApplicationRewards1.Count,
                "application reward 1 count");
            Equal(2, config.ApplicationRewards2.Count,
                "application reward 2 count");

            Assert(config.TrySelectApplicationReward(0, out var reward10),
                "application reward 1 first selection failed");
            EqualText("经验:50", reward10,
                "application reward 1 inclusive threshold");
            Assert(config.TrySelectApplicationReward(0, out var reward11),
                "application reward 1 second selection failed");
            EqualText("声望:3", reward11,
                "application reward 1 next threshold");
            Assert(config.TrySelectApplicationReward(1, out var reward20),
                "application reward 2 first selection failed");
            EqualText("金刚石:2", reward20,
                "application reward 2 inclusive threshold");
            Assert(config.TrySelectApplicationRewardGbk(1,
                    out var reward21),
                "application reward 2 raw selection failed");
            EqualBytes(Encoding.GetEncoding(936).GetBytes("屠龙:1"),
                reward21, "application reward 2 GBK descriptor");

            Assert(!config.TrySelectApplicationReward(-1, out _),
                "negative descriptor index selected a reward");
            Assert(!config.TrySelectApplicationReward(2, out _),
                "descriptor index two selected a reward");
            Equal(1, rolls.Count,
                "invalid descriptor index consumed random state");
        });
}

static void TestFirstEmptyAndNativeFallback()
{
    WithConfig(
        "[领取奖励]\r\n" +
        "奖品1=第一项/10\r\n" +
        "奖品2=\r\n" +
        "奖品3=不得读取/99\r\n" +
        "[领取额外奖励]\r\n" +
        "奖品1=附加一\r\n" +
        "奖品2=\r\n" +
        "奖品3=不得附加\r\n" +
        "[申请奖励1]\r\n" +
        "奖品1=申请一/10\r\n" +
        "奖品2=\r\n" +
        "奖品3=不得申请/99\r\n",
        fileName =>
        {
            Load(fileName, _ => 99, out var config);
            Equal(1, config.ClaimRewards.Count,
                "claim first-empty termination");
            Equal(1, config.AdditionalRewards.Count,
                "additional first-empty termination");
            Equal(1, config.ApplicationRewards1.Count,
                "application first-empty termination");
            Assert(config.TrySelect(out var descriptor),
                "uncovered tail did not keep first reward");
            EqualText("第一项,附加一", descriptor,
                "native uncovered-tail fallback");
        });

    WithConfig(
        "[领取奖励]\r\n" +
        "奖品1=第一项/10\r\n" +
        "奖品2=倒序项/5\r\n" +
        "奖品3=第三项/99\r\n",
        fileName =>
        {
            Load(fileName, _ => 11, out var config);
            Equal(2, config.ClaimRewards.Count,
                "invalid row was not skipped");
            Assert(config.TrySelect(out var descriptor),
                "selection after skipped row failed");
            EqualText("第三项", descriptor,
                "scan did not continue after invalid row");
        });
}

static void TestFixedHundredEntryLimit()
{
    var source = new StringBuilder("[领取奖励]\r\n");
    for (var number = 1; number <= 100; number++)
        source.Append("奖品").Append(number).Append("=项目")
            .Append(number).Append('/').Append(number - 101).Append("\r\n");
    source.Append("奖品101=不得读取/99\r\n");
    source.Append("[领取额外奖励]\r\n");
    for (var number = 1; number <= 101; number++)
        source.Append("奖品").Append(number).Append("=附加")
            .Append(number).Append("\r\n");

    WithConfig(source.ToString(), fileName =>
    {
        Load(fileName, _ => 99, out var config);
        Equal(NativeDiamondBountyConfig.MaximumEntries,
            config.ClaimRewards.Count, "fixed claim entry limit");
        Equal(NativeDiamondBountyConfig.MaximumEntries,
            config.AdditionalRewards.Count, "fixed additional entry limit");
        Assert(config.TrySelect(out var descriptor),
            "100-row fallback selection failed");
        Assert(descriptor.StartsWith("项目1,附加1,", StringComparison.Ordinal),
            "additional rewards were not appended in native order");
        Assert(descriptor.EndsWith(",附加100", StringComparison.Ordinal),
            "101st additional key escaped native limit");
        Assert(!descriptor.Contains("附加101", StringComparison.Ordinal),
            "101st additional key escaped native limit");
    });
}

static void TestNativeOddGbkByteBoundary()
{
    var descriptor = new string('甲', 26);
    WithConfig(
        "[领取奖励]\r\n" +
        "奖品1=" + descriptor + "/99\r\n" +
        "[领取额外奖励]\r\n" +
        "奖品1=灵符:2\r\n",
        fileName =>
        {
            Load(fileName, _ => 0, out var config);
            var sourceBytes = Encoding.GetEncoding(936).GetBytes(descriptor);
            var expectedDescriptor = sourceBytes.AsSpan(0, 0x33).ToArray();
            EqualBytes(expectedDescriptor,
                config.ClaimRewards[0].DescriptorGbkBytes.Span,
                "claim raw 51-byte truncation");
            var legacyDescriptor = new string('甲', 25);
            EqualText(legacyDescriptor,
                config.ClaimRewards[0].Descriptor,
                "claim public string compatibility");
            Equal(sourceBytes[50],
                config.ClaimRewards[0].DescriptorGbkBytes.Span[50],
                "claim dangling GBK lead byte");
            EqualBytes(Encoding.GetEncoding(936).GetBytes("灵符:2"),
                config.GetAdditionalRewardGbkBytes(0).Span,
                "additional raw GBK retention");

            Assert(config.TrySelectGbk(out var selected),
                "odd-boundary raw selection failed");
            var expected = expectedDescriptor
                .Concat(new[] { (byte)',' })
                .Concat(Encoding.GetEncoding(936).GetBytes("灵符:2"))
                .ToArray();
            EqualBytes(expected, selected,
                "selected descriptor raw concatenation");
            Assert(config.TrySelect(out var selectedText),
                "odd-boundary public selection failed");
            EqualText(legacyDescriptor + ",灵符:2", selectedText,
                "selected descriptor public string compatibility");
        });
}

static void TestMissingAndInvalidInputs()
{
    var missing = Path.Combine(Path.GetTempPath(),
        "missing-diamond-bounty-" + Guid.NewGuid().ToString("N") + ".ini");
    Assert(NativeDiamondBountyConfig.TryLoad(missing, _ => 0,
            out var unloaded, out var error),
        "missing native file load failed: " + error);
    Assert(!unloaded.SourceLoaded, "missing source was marked loaded");
    Assert(!unloaded.TrySelect(out _), "missing source selected a reward");

    Assert(!NativeDiamondBountyConfig.TryLoad(string.Empty, _ => 0,
            out _, out _), "empty path was accepted");
    Assert(!NativeDiamondBountyConfig.TryLoad(missing, null,
            out _, out _), "null random source was accepted");

    WithConfig("[领取奖励]\r\n奖品1=奖励/99\r\n", fileName =>
    {
        Load(fileName, _ => 100, out var config);
        ExpectThrows<InvalidOperationException>(() => config.TrySelect(out _),
            "out-of-range random result was accepted");
    });
}

static void Load(string fileName, Func<int, int> random,
    out NativeDiamondBountyConfig config)
{
    Assert(NativeDiamondBountyConfig.TryLoad(fileName, random,
            out config, out var error), "config load failed: " + error);
}

static void WithConfig(string source, Action<string> action)
{
    var root = Path.Combine(Path.GetTempPath(),
        "DiamondBountyConfigCheck-" + Guid.NewGuid().ToString("N"));
    var directory = Path.Combine(root, "Share");
    Directory.CreateDirectory(directory);
    var fileName = Path.Combine(directory, "DiamondBounty.ini");
    try
    {
        var gbk = Encoding.GetEncoding(936);
        File.WriteAllText(fileName, source, gbk);
        var bytes = File.ReadAllBytes(fileName);
        var marker = source.FirstOrDefault(character => character > 0x7F);
        Assert(marker != '\0' && Contains(bytes,
                gbk.GetBytes(marker.ToString())),
            "fixture is not GBK encoded");
        action(fileName);
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static bool Contains(byte[] source, byte[] value)
{
    for (var offset = 0; offset <= source.Length - value.Length; offset++)
    {
        if (source.AsSpan(offset, value.Length).SequenceEqual(value))
            return true;
    }
    return false;
}

static void ExpectThrows<TException>(Action action, string message)
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
    throw new InvalidOperationException(message);
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualBytes(ReadOnlySpan<byte> expected,
    ReadOnlySpan<byte> actual, string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{message}: expected {Convert.ToHexString(expected)}, " +
            $"actual {Convert.ToHexString(actual)}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
