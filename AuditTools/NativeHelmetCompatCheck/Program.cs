using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

try
{
    PrepareRuntimeConfig();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
    SetDefinitions();

    VerifyPreflightAndCooldown();
    VerifyPileCountAndClearSemantics();
    VerifyConsumptionAndExclusions();
    VerifyProbabilityTiers();
    VerifyRewardCreationFailure();
    VerifyPasHumRouting();
    VerifySourceContracts();

    Console.WriteLine(
        "NativeHelmetCompatCheck PASS abi=Hum cooldown=60s " +
        "required=1/1/1/1/2 bonus=2/1/4/3/4/1 " +
        "thresholds=1/5/10 inclusive consume=bag-jewelry reward=黄金头盔");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "NativeHelmetCompatCheck FAIL: " + exception);
    return 1;
}

static void VerifyPreflightAndCooldown()
{
    var player = NewPlayer();
    AddRequired(player, omit: "地苦胆");
    var before = ItemNames(player);
    var randomCalls = 0;
    Equal("MissingMaterials", Run(player, 60_000, _ =>
    {
        randomCalls++;
        return 0;
    }), "missing-material result");
    EqualSequence(before, ItemNames(player),
        "preflight consumed material");
    Equal(0, randomCalls, "preflight consumed RNG");
    Equal("Cooldown", Run(player, 119_999, _ => 0),
        "failed attempt did not start cooldown");
    Equal("MissingMaterials", Run(player, 120_000, _ => 0),
        "60-second boundary was not inclusive");
}

static void VerifyConsumptionAndExclusions()
{
    var player = NewPlayer();
    AddRequired(player);
    Add(player, "黑铁头盔(极品)");
    Add(player, "普通戒指");
    Add(player, "保留项链");
    Add(player, "保留手镯");
    int randomRange = 0;
    Equal("Failed", Run(player, 60_000, range =>
    {
        randomRange = range;
        return 2;
    }), "base-tier failure result");
    Equal(100, randomRange, "helmet RNG range");
    EqualSequence(new[] { "保留手镯", "保留项链" }, ItemNames(player),
        "generic jewelry deletion/exclusions");
}

static void VerifyPileCountAndClearSemantics()
{
    var hammer = Definition("天工之锤");
    var originalMode = hammer.StdMode;
    hammer.StdMode = 7;
    try
    {
        var player = NewPlayer();
        AddRequired(player, omit: "天工之锤");
        Add(player, "天工之锤");
        player.m_ItemList[^1].Dura = 2;
        Equal("MissingMaterials", Run(player, 60_000, _ => 0),
            "pile count/instance clear result");
        Equal(0, player.m_ItemList.Count,
            "whole pile instance was not consumed before count mismatch");
    }
    finally
    {
        hammer.StdMode = originalMode;
    }
}

static void VerifyProbabilityTiers()
{
    var exact = NewPlayer();
    AddRequired(exact);
    foreach (var entry in GetBonusMaterials())
        Add(exact, entry.Name, entry.Count);
    Add(exact, "圣战头盔");
    Equal("Success", Run(exact, 60_000, _ => 10),
        "exact-material threshold must accept roll 10");
    EqualSequence(new[] { "黄金头盔" }, ItemNames(exact),
        "successful synthesis inventory");

    var partial = NewPlayer();
    AddRequired(partial);
    Add(partial, "骑士手镯");
    var soul = Definition("灵魂项链");
    var originalShape = soul.Shape;
    soul.Shape = 120;
    Add(partial, "灵魂项链");
    Add(partial, "天尊头盔");
    try
    {
        Equal("Success", Run(partial, 60_000, _ => 5),
            "partial-material threshold must accept roll 5");
        EqualSequence(new[] { "灵魂项链", "黄金头盔" }, ItemNames(partial),
            "bonus mismatch did not exit to the generic jewelry sweep");
    }
    finally
    {
        soul.Shape = originalShape;
    }
}

static void VerifyRewardCreationFailure()
{
    var player = NewPlayer();
    AddRequired(player);
    var definitions = M2Share.UserEngine.StdItemList;
    var reward = definitions[^1];
    definitions.RemoveAt(definitions.Count - 1);
    try
    {
        Equal("RewardCreateFailed", Run(player, 60_000, _ => 0),
            "missing reward definition result");
        Equal(0, player.m_ItemList.Count,
            "reward construction failure restored consumed materials");
    }
    finally
    {
        definitions.Add(reward);
    }
}

static void VerifyPasHumRouting()
{
    var sentinel = NewPlayer();
    var target = NewPlayer();
    var tickField = typeof(TPlayObject).GetField(
        "_nativeHelmetUpgradeTick",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new InvalidOperationException("helmet cooldown field missing");
    tickField.SetValue(sentinel, 123);
    var targetTickBefore = unchecked(HUtil32.GetTickCount() - 60_000);
    tickField.SetValue(target, targetTickBefore);

    var bridge = new PasApiBridge
    {
        CurrentPlayer = sentinel,
        CurrentNpc = new NormNpc { m_sCharName = "封印人" }
    };
    Assert(bridge.CallNpcMethod("UpHelmet",
            new List<PasValue> { PasValue.FromObject(target) }, out var result) &&
           result.Equals(PasValue.Nil),
        "UpHelmet procedure was not dispatched");
    Equal(123, Convert.ToInt32(tickField.GetValue(sentinel)),
        "UpHelmet mutated CurrentPlayer instead of Hum");
    Assert(Convert.ToInt32(tickField.GetValue(target)) != targetTickBefore,
        "UpHelmet did not invoke Hum");
    Assert(!bridge.CallNpcMethod("UpHelmet",
            new List<PasValue> { PasValue.FromInt(0) }, out _),
        "UpHelmet accepted an integer upType argument");
}

static void VerifySourceContracts()
{
    var root = FindRepositoryRoot();
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    var start = RequiredIndex(bridge, "case \"uphelmet\":",
        "UpHelmet dispatch");
    var end = RequiredIndex(bridge, "case \"callout\":",
        "UpHelmet dispatch terminator", start);
    var block = bridge[start..end];
    Contains(block, "args.Count != 1", "UpHelmet arity");
    Contains(block, "args[0].ObjVal is not TPlayObject",
        "UpHelmet Hum type gate");
    Contains(block, "UpgradeNativeHelmet(CurrentNpc)",
        "UpHelmet native worker");
    Reject(block, "PerformWeaponUpgrade", "weapon upgrade leak");
    Reject(block, "U_WEAPON", "weapon slot leak");

    var worker = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeHelmet.cs"));
    foreach (var text in new[]
             {
                 "黑铁头盔(极品)", "真视秘籍", "地苦胆", "四叶参",
                 "天工之锤", "骑士手镯", "灵魂项链", "恶魔铃铛",
                 "龙之戒指", "思贝儿手镯", "三眼手镯", "圣战头盔",
                 "天尊头盔", "法神头盔", "黄金头盔"
             })
        Contains(worker, text, "native item table " + text);
    Contains(worker, "random(100) >", "inclusive native roll");
    Contains(worker, "RM_MERCHANTDLGCLOSE",
        "terminal failure dialog close");
}

static (string Name, int Count)[] GetBonusMaterials() => new[]
{
    ("骑士手镯", 2), ("灵魂项链", 1), ("恶魔铃铛", 4),
    ("龙之戒指", 3), ("思贝儿手镯", 4), ("三眼手镯", 1)
};

static void SetDefinitions()
{
    M2Share.UserEngine.StdItemList.Clear();
    Define("黑铁头盔(极品)", 15, 0);
    Define("真视秘籍", 1, 5);
    Define("地苦胆", 0, 1);
    Define("四叶参", 0, 1);
    Define("天工之锤", 47, 2);
    Define("骑士手镯", 26, 134);
    Define("灵魂项链", 20, 135);
    Define("恶魔铃铛", 20, 135);
    Define("龙之戒指", 22, 133);
    Define("思贝儿手镯", 26, 134);
    Define("三眼手镯", 26, 134);
    Define("圣战头盔", 15, 0);
    Define("天尊头盔", 15, 0);
    Define("法神头盔", 15, 0);
    Define("普通戒指", 19, 0);
    Define("保留项链", 22, 111);
    Define("保留手镯", 20, 120);
    Define("黄金头盔", 15, 0);
}

static void Define(string name, byte stdMode, byte shape) =>
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = name,
        StdMode = stdMode,
        Shape = shape,
        DuraMax = 100
    });

static GoodItem Definition(string name)
{
    var index = M2Share.UserEngine.GetStdItemIdx(name);
    return M2Share.UserEngine.GetStdItem(index) ??
           throw new InvalidOperationException("missing definition: " + name);
}

static TPlayObject NewPlayer() => new() { m_boOffLineFlag = true };

static void AddRequired(TPlayObject player, string omit = null)
{
    foreach (var entry in new[]
             {
                 ("黑铁头盔(极品)", 1), ("真视秘籍", 1),
                 ("地苦胆", 1), ("四叶参", 1), ("天工之锤", 2)
             })
        if (!string.Equals(entry.Item1, omit, StringComparison.Ordinal))
            Add(player, entry.Item1, entry.Item2);
}

static void Add(TPlayObject player, string name, int count = 1)
{
    var itemIndex = M2Share.UserEngine.GetStdItemIdx(name);
    Assert(itemIndex > 0, "missing test definition: " + name);
    for (var index = 0; index < count; index++)
        player.m_ItemList.Add(new TUserItem
        {
            MakeIndex = 10_000 + player.m_ItemList.Count,
            wIndex = unchecked((ushort)itemIndex),
            Dura = 100,
            DuraMax = 100
        });
}

static string Run(TPlayObject player, int tick, Func<int, int> random)
{
    var method = typeof(TPlayObject).GetMethod("RunNativeHelmetUpgrade",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new InvalidOperationException("helmet worker missing");
    return method.Invoke(player, new object[] { tick, random })?.ToString() ??
           string.Empty;
}

static string[] ItemNames(TPlayObject player) => player.m_ItemList
    .Select(item => M2Share.UserEngine.GetStdItemName(item.wIndex))
    .OrderBy(name => name, StringComparer.Ordinal)
    .ToArray();

static string FindRepositoryRoot()
{
    foreach (var origin in new[]
             {
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(origin);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("repository root not found");
}

static void PrepareRuntimeConfig()
{
    var directory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(directory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(directory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(directory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var share = Path.Combine(Path.GetFullPath(Path.Combine(directory, "..")),
        "Share");
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
}

static int RequiredIndex(string source, string value, string message,
    int start = 0)
{
    var index = source.IndexOf(value, start, StringComparison.Ordinal);
    if (index < 0) throw new InvalidOperationException(message + " missing");
    return index;
}

static void Contains(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.Ordinal),
        message + " missing");

static void Reject(string source, string value, string message) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), message);

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void EqualSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual,
    string message) => Assert(expected.SequenceEqual(actual), message);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
