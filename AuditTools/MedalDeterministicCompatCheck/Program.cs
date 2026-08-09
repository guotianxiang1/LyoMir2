using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();

var root = FindRepositoryRoot();
var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr",
    "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
var medalSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeMedal.cs"));

CheckSourceContracts(bridgeSource, medalSource);
InitializeRuntime();
CheckMethodAbiAndClosedShadows();
CheckProductionSelectors();
CheckFailureSemantics();
CheckInjectedInsertionOrdering();

Console.WriteLine(
    "PASS Medal deterministic APIs=Ry+Sw method-ABI=Player+string " +
    "selectors=25 fee-logs=37->9 dialogs=exact " +
    "insert-failure=both-no-refund/Ry-log/Sw-no-log " +
    "function-shadows=closed Rnd=closed");
return;

static void CheckSourceContracts(string bridge, string medal)
{
    var npcMethods = Slice(bridge, "public bool CallNpcMethod",
        "public bool CallNpcFunc");
    var npcFunctions = Slice(bridge, "public bool CallNpcFunc",
        "public bool CallStandaloneFunction");

    foreach (var name in new[]
             {
                 "spegetmedalbyry", "spegetmedalbysw", "rndgetmedal"
             })
    {
        Equal(1, Count(npcMethods, $"case \"{name}\":"),
            name + " NPC-method dispatch count");
        Equal(1, Count(npcFunctions, $"case \"{name}\":"),
            name + " NPC-function dispatch count");
    }

    CheckOpenMethod(npcMethods, "spegetmedalbyry", "Ry");
    CheckOpenMethod(npcMethods, "spegetmedalbysw", "Sw");
    CheckFailClosedCase(npcMethods, "rndgetmedal", "Rnd method");
    CheckFailClosedCase(npcFunctions, "rndgetmedal", "Rnd function");
    CheckFailClosedCase(npcFunctions, "spegetmedalbyry",
        "Ry function shadow");
    CheckFailClosedCase(npcFunctions, "spegetmedalbysw",
        "Sw function shadow");

    RequireMatches(medal,
        "NativeMedalBagFullMessage\\s*=\\s*\"你无法携带更多的物品\"", 1,
        "bag-full prompt changed");
    RequireMatches(medal,
        "NativeMedalRyInsufficientMessage\\s*=\\s*" +
        "\"你的荣誉值，声望值均不符合条件！\"", 1,
        "Ry insufficient prompt changed");
    RequireMatches(medal,
        "NativeMedalSwInsufficientMessage\\s*=\\s*\"你的声望值不够！\"", 1,
        "Sw insufficient prompt changed");
    Require(medal,
        "itemIndex is >= 311 and <= 330 or >= 4335 and <= 4338",
        "Ry native index ranges changed");
    Require(medal, "itemIndex is >= 697 and <= 701 or 4339",
        "Sw native index ranges changed");
    Require(medal, "var fee = 80 * stdItem.Shape;",
        "Ry fee is no longer 80 * Shape");
    Require(medal, "NativeItemFactory.GetClassName(stdItem) == null",
        "medal creation bypasses the native class-factory null decision");
    Require(medal, "internal const int NativeMedalSwFee = 640;",
        "Sw fee changed");
    Require(medal, "string.Join('\\t', 37", "fee log type changed");
    RequireMatches(medal,
        "\"声望值\",\\s*33333333,\\s*fee,\\s*\"系统消耗\"", 1,
        "fee log payload changed");
    Require(medal, "string.Join('\\t', 9", "item log type changed");
    RequireMatches(medal,
        "item\\.MakeIndex,\\s*1,\\s*npc\\?\\.m_sCharName \\?\\? string\\.Empty",
        1, "item log payload changed");
    Require(medal, "\"恭喜你兑换[\" + itemName + \"]成功！\"",
        "success prompt changed");
    Reject(medal, "SetShengWan(",
        "medal exchange gained a non-native RM_ABILITY refresh");
    Reject(medal, "RandomNumber", "deterministic medal path uses legacy RNG");
    Reject(medal, "DelphiRandom", "deterministic medal path uses Delphi RNG");
    Reject(medal, "Random.Shared", "deterministic medal path uses shared RNG");

    var ry = Slice(medal, "internal void ExchangeNativeMedalByRy",
        "internal void ExchangeNativeMedalBySw");
    RequireOrder(ry, "Ry exchange order",
        "var selector = ParseNativeMedalSelector(selectorText);",
        "var itemIndex = unchecked(310 + selector);",
        "if (!IsEnoughBag())",
        "if (!TryCreateNativeMedal",
        "var fee = 80 * stdItem.Shape;",
        "if (fee > m_nShengWan)",
        "m_nShengWan = unchecked(m_nShengWan - fee);",
        "WriteNativeMedalFeeLog(fee);",
        "if (!AddItemToBag(item))",
        "CompleteNativeMedalExchange(npc, item, stdItem.Name);");

    var sw = Slice(medal, "internal void ExchangeNativeMedalBySw",
        "private static bool IsNativeRyMedalIndex");
    RequireOrder(sw, "Sw exchange order",
        "if (m_nShengWan < NativeMedalSwFee)",
        "var selector = ParseNativeMedalSelector(selectorText);",
        "var itemIndex = unchecked(646 + selector);",
        "if (!IsEnoughBag())",
        "if (!TryCreateNativeMedal",
        "m_nShengWan = unchecked(m_nShengWan - NativeMedalSwFee);",
        "if (!AddItemToBag(item))",
        "WriteNativeMedalFeeLog(NativeMedalSwFee);",
        "CompleteNativeMedalExchange(npc, item, stdItem.Name);");
}

static void CheckOpenMethod(string methodRegion, string caseName,
    string suffix)
{
    var body = CaseBody(methodRegion, caseName);
    RequireMatches(body, @"args\.Count\s*!=\s*2", 1,
        suffix + " method must require exactly two arguments");
    RequireMatches(body,
        @"args\[0\]\.ObjVal is not TPlayObject medal" + suffix + @"Player", 1,
        suffix + " method must use the explicit Player argument");
    RequireMatches(body,
        @"args\[1\]\.Type\s*!=\s*PasValueType\.String", 1,
        suffix + " method must require a PAS string selector");
    RequireMatches(body,
        @"medal" + suffix + @"Player\.ExchangeNativeMedalBy" + suffix +
        @"\(CurrentNpc,\s*args\[1\]\.StrVal\)", 1,
        suffix + " method no longer delegates to the explicit player");
    Reject(body, "TryParseNativeDelphiInteger",
        suffix + " bridge parsed before the native business guard");
    Reject(body, "CurrentPlayer", suffix + " method uses context player");
    Reject(body, "RejectUnsupportedNativeApi",
        suffix + " method remains fail-closed");
}

static void CheckFailClosedCase(string region, string caseName, string label)
{
    var body = CaseBody(region, caseName);
    Require(body, "return RejectUnsupportedNativeApi(out result);",
        label + " is not fail-closed");
    foreach (var forbidden in new[]
             {
                 "ExchangeNativeMedal", "m_nShengWan", "AddItemToBag",
                 "RandomNumber", "DelphiRandom"
             })
    {
        Reject(body, forbidden, label + " acquired runtime behavior: " + forbidden);
    }
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig { nItemNumber = 700000 };
    M2Share.UserEngine = BuildItemEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

static UserEngine BuildItemEngine()
{
    var engine = new UserEngine();
    for (var index = 1; index <= 701; index++)
    {
        engine.StdItemList.Add(new GoodItem
        {
            Name = "audit-item-" + index,
            StdMode = 0,
            Shape = 0,
            DuraMax = 1
        });
    }

    for (var selector = 1; selector <= 20; selector++)
    {
        var index = 310 + selector;
        var shape = (selector - 1) / 5 + 1;
        var ordinal = (selector - 1) % 5 + 1;
        engine.StdItemList[index - 1] = MedalItem(
            $"荣誉勋章{shape}{ordinal}号", shape);
    }

    for (var selector = 51; selector <= 55; selector++)
    {
        var index = 646 + selector;
        engine.StdItemList[index - 1] = MedalItem(
            $"荣誉勋章5{selector - 50}号", 5);
    }
    return engine;
}

static GoodItem MedalItem(string name, int shape) => new()
{
    Name = name,
    StdMode = 30,
    Shape = (byte)shape,
    DuraMax = 50000
};

static void CheckMethodAbiAndClosedShadows()
{
    var context = NewPlayer("context", 1777);
    var explicitPlayer = NewPlayer("explicit", 1000);
    var npc = NewNpc();
    var bridge = new PasApiBridge
    {
        CurrentPlayer = context,
        CurrentNpc = npc
    };

    var badCalls = new[]
    {
        new List<PasValue>(),
        Values(explicitPlayer),
        Values(1, "1"),
        Values(explicitPlayer, 1),
        Values(explicitPlayer, "1", 0)
    };
    foreach (var name in new[] { "SpeGetMedalByRy", "SpeGetMedalBySw" })
    {
        foreach (var args in badCalls)
        {
            Assert(!bridge.CallNpcMethod(name, args, out var result),
                name + " accepted an invalid ABI");
            Equal(PasValueType.Nil, result.Type,
                name + " invalid ABI result");
        }
    }
    Equal(1000, explicitPlayer.m_nShengWan,
        "invalid ABI changed explicit player balance");
    Equal(1777, context.m_nShengWan,
        "invalid ABI changed context player balance");
    Equal(0, explicitPlayer.m_ItemList.Count,
        "invalid ABI changed explicit player bag");

    M2Share.LogStringList.Clear();
    Assert(bridge.CallNpcMethod("SpeGetMedalByRy",
            Values(explicitPlayer, "1"), out var procedureResult),
        "Ry method did not accept Player+string");
    Equal(PasValueType.Nil, procedureResult.Type, "Ry procedure result");
    Equal(920, explicitPlayer.m_nShengWan,
        "Ry method did not mutate the explicit player");
    Equal(1777, context.m_nShengWan,
        "Ry method mutated CurrentPlayer instead of explicit Player");
    Equal(1, explicitPlayer.m_ItemList.Count,
        "Ry method did not award explicit Player");
    Equal(0, context.m_ItemList.Count,
        "Ry method awarded CurrentPlayer");

    var before = Snapshot.Capture(explicitPlayer, context);
    foreach (var name in new[]
             {
                 "SpeGetMedalByRy", "SpeGetMedalBySw", "RndGetMedal"
             })
    {
        Assert(!bridge.CallNpcFunc(name, Values(explicitPlayer, "1"),
                out var result), name + " function shadow opened");
        Equal(PasValueType.Nil, result.Type, name + " function result");
    }
    Assert(!bridge.CallNpcMethod("RndGetMedal",
            Values(explicitPlayer, "1"), out var rndResult),
        "RndGetMedal method opened without global RNG closure");
    Equal(PasValueType.Nil, rndResult.Type, "Rnd method result");
    before.AssertUnchanged(explicitPlayer, context,
        "closed medal surfaces");
}

static void CheckProductionSelectors()
{
    for (var selector = 1; selector <= 20; selector++)
    {
        var shape = (selector - 1) / 5 + 1;
        var ordinal = (selector - 1) % 5 + 1;
        CheckSuccess("SpeGetMedalByRy", selector, 310 + selector,
            80 * shape, $"荣誉勋章{shape}{ordinal}号");
    }

    for (var selector = 51; selector <= 55; selector++)
    {
        CheckSuccess("SpeGetMedalBySw", selector, 646 + selector,
            640, $"荣誉勋章5{selector - 50}号");
    }
}

static void CheckSuccess(string api, int selector, int expectedIndex,
    int fee, string itemName)
{
    M2Share.LogStringList.Clear();
    var player = NewPlayer("selector-" + selector, 2000);
    var context = NewPlayer("unused-context", 3000);
    var npc = NewNpc();
    var bridge = new PasApiBridge { CurrentPlayer = context, CurrentNpc = npc };

    Assert(bridge.CallNpcMethod(api, Values(player, selector.ToString()), out _),
        api + " selector dispatch " + selector);
    Equal(2000 - fee, player.m_nShengWan,
        api + " selector fee " + selector);
    Equal(3000, context.m_nShengWan,
        api + " selector changed context player " + selector);
    Equal(1, player.m_ItemList.Count,
        api + " selector bag count " + selector);
    Equal(0, context.m_ItemList.Count,
        api + " selector awarded context player " + selector);

    var item = player.m_ItemList[0];
    Equal((ushort)expectedIndex, item.wIndex,
        api + " selector item index " + selector);
    Equal((ushort)50000, item.Dura,
        api + " selector item durability " + selector);
    Equal((ushort)50000, item.DuraMax,
        api + " selector max durability " + selector);
    Equal((byte)0, item.Bind,
        api + " selector bind flag " + selector);

    var logs = M2Share.LogStringList.Cast<object>().OfType<string>().ToArray();
    Equal(2, logs.Length, api + " selector log count " + selector);
    Equal($"37\taudit-map\t12\t34\tselector-{selector}\t声望值\t33333333\t{fee}\t系统消耗",
        logs[0], api + " selector fee log " + selector);
    Equal($"9\taudit-map\t12\t34\tselector-{selector}\t{itemName}\t{item.MakeIndex}\t1\t勋章守护人",
        logs[1], api + " selector item log " + selector);
    SequenceEqual(new[]
        {
            "勋章守护人/恭喜你兑换[" + itemName + "]成功！"
        }, MerchantDialogs(player),
        api + " selector dialog " + selector);
}

static void CheckFailureSemantics()
{
    CheckNoAward("SpeGetMedalByRy", "1", 79,
        "勋章守护人/你的荣誉值，声望值均不符合条件！",
        "Ry insufficient");
    CheckNoAward("SpeGetMedalBySw", "not-an-integer", 639,
        "勋章守护人/你的声望值不够！",
        "Sw insufficient before selector semantics");

    foreach (var api in new[] { "SpeGetMedalByRy", "SpeGetMedalBySw" })
    {
        M2Share.LogStringList.Clear();
        var player = NewPlayer("full-bag", 2000);
        while (player.m_ItemList.Count < Grobal2.MAXBAGITEM)
            player.m_ItemList.Add(new TUserItem());
        var bridge = new PasApiBridge
        {
            CurrentPlayer = NewPlayer("context", 3000),
            CurrentNpc = NewNpc()
        };
        var selector = api.EndsWith("Ry", StringComparison.Ordinal) ? "1" : "51";
        Assert(bridge.CallNpcMethod(api, Values(player, selector), out _),
            api + " full-bag dispatch");
        Equal(2000, player.m_nShengWan, api + " full-bag charged fee");
        Equal(Grobal2.MAXBAGITEM, player.m_ItemList.Count,
            api + " full-bag changed bag");
        Equal(0, M2Share.LogStringList.Count,
            api + " full-bag wrote logs");
        SequenceEqual(new[] { "勋章守护人/你无法携带更多的物品" },
            MerchantDialogs(player), api + " full-bag dialog");
    }

    foreach (var test in new[]
             {
                 (Api: "SpeGetMedalByRy", Selector: "0", Label: "Ry low invalid"),
                 (Api: "SpeGetMedalByRy", Selector: "21", Label: "Ry high invalid"),
                 (Api: "SpeGetMedalByRy", Selector: "not-an-integer", Label: "Ry malformed"),
                 (Api: "SpeGetMedalByRy", Selector: "2147483647", Label: "Ry overflowed add"),
                 (Api: "SpeGetMedalBySw", Selector: "50", Label: "Sw low invalid"),
                 (Api: "SpeGetMedalBySw", Selector: "56", Label: "Sw high invalid"),
                 (Api: "SpeGetMedalBySw", Selector: "not-an-integer", Label: "Sw malformed")
             })
    {
        CheckNoAward(test.Api, test.Selector, 2000, null, test.Label);
    }

    foreach (var selector in new[] { "4025", "4026", "4027", "4028" })
        CheckNoAward("SpeGetMedalByRy", selector, 2000, null,
            "Ry absent hidden index " + selector);
    CheckNoAward("SpeGetMedalBySw", "3693", 2000, null,
        "Sw absent hidden index 3693");

    var original = M2Share.UserEngine.StdItemList[310];
    try
    {
        M2Share.UserEngine.StdItemList[310] = new GoodItem
        {
            Name = "native-factory-null",
            StdMode = 159,
            Shape = 1,
            DuraMax = 50000
        };
        CheckNoAward("SpeGetMedalByRy", "1", 2000, null,
            "Ry native factory rejection");
    }
    finally
    {
        M2Share.UserEngine.StdItemList[310] = original;
    }
}

static void CheckNoAward(string api, string selector, int balance,
    string expectedDialog, string label)
{
    M2Share.LogStringList.Clear();
    var player = NewPlayer(label, balance);
    var bridge = new PasApiBridge
    {
        CurrentPlayer = NewPlayer("context", 3000),
        CurrentNpc = NewNpc()
    };
    Assert(bridge.CallNpcMethod(api, Values(player, selector), out _),
        label + " dispatch");
    Equal(balance, player.m_nShengWan, label + " changed balance");
    Equal(0, player.m_ItemList.Count, label + " changed bag");
    Equal(0, M2Share.LogStringList.Count, label + " wrote logs");
    var expected = expectedDialog == null
        ? Array.Empty<string>()
        : new[] { expectedDialog };
    SequenceEqual(expected, MerchantDialogs(player), label + " dialogs");
}

static void CheckInjectedInsertionOrdering()
{
    var ryFailure = new InsertionOracle(1000, _ => false);
    ryFailure.ExchangeRy(311, 80, "荣誉勋章11号");
    Equal(920, ryFailure.Balance, "Ry insertion failure refunded fee");
    SequenceEqual(new[]
    {
        "create:311", "debit:80", "fee37:80", "insert:311", "dispose:311"
    }, ryFailure.Events, "Ry insertion failure order");

    var swFailure = new InsertionOracle(1000, _ => false);
    swFailure.ExchangeSw(697, "荣誉勋章51号");
    Equal(360, swFailure.Balance, "Sw insertion failure refunded fee");
    SequenceEqual(new[]
    {
        "create:697", "debit:640", "insert:697", "dispose:697"
    }, swFailure.Events, "Sw insertion failure order");
    Assert(!swFailure.Events.Any(value => value.StartsWith("fee37:",
            StringComparison.Ordinal)),
        "Sw insertion failure wrote the post-insert fee log");

    var rySuccess = new InsertionOracle(1000, _ => true);
    rySuccess.ExchangeRy(311, 80, "荣誉勋章11号");
    SequenceEqual(new[]
    {
        "create:311", "debit:80", "fee37:80", "insert:311",
        "item9:荣誉勋章11号", "dialog:荣誉勋章11号"
    }, rySuccess.Events, "Ry success order");

    var swSuccess = new InsertionOracle(1000, _ => true);
    swSuccess.ExchangeSw(697, "荣誉勋章51号");
    SequenceEqual(new[]
    {
        "create:697", "debit:640", "insert:697", "fee37:640",
        "item9:荣誉勋章51号", "dialog:荣誉勋章51号"
    }, swSuccess.Events, "Sw success order");
}

static TPlayObject NewPlayer(string name, int balance) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_sMapName = "audit-map",
    m_nCurrX = 12,
    m_nCurrY = 34,
    m_nShengWan = balance
};

static NormNpc NewNpc() => new()
{
    m_sCharName = "勋章守护人",
    m_sMapName = "audit-map"
};

static List<PasValue> Values(params object[] values) => values.Select(value =>
    value switch
    {
        int number => PasValue.FromInt(number),
        string text => PasValue.FromString(text),
        _ => PasValue.FromObject(value)
    }).ToList();

static string[] MerchantDialogs(TPlayObject player) => player.m_MsgList
    .Where(message => message.wIdent == Grobal2.RM_MERCHANTSAY)
    .Select(message => message.Buff ?? string.Empty)
    .ToArray();

static string CaseBody(string region, string name)
{
    var match = Regex.Match(region,
        $"case \\\"{Regex.Escape(name)}\\\":(?<body>.*?)(?=\\r?\\n\\s*case \\\"|\\r?\\n\\s*default:)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);
    Assert(match.Success, "missing case body: " + name);
    return match.Groups["body"].Value;
}

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Assert(start >= 0, "missing marker: " + startMarker);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Assert(end > start, "missing marker: " + endMarker);
    return source[start..end];
}

static int Count(string source, string value)
{
    var count = 0;
    for (var offset = 0; (offset = source.IndexOf(value, offset,
             StringComparison.Ordinal)) >= 0; offset += value.Length)
        count++;
    return count;
}

static void RequireOrder(string source, string label, params string[] markers)
{
    var offset = -1;
    foreach (var marker in markers)
    {
        var next = source.IndexOf(marker, offset + 1, StringComparison.Ordinal);
        Assert(next > offset, label + " missing/out-of-order marker: " + marker);
        offset = next;
    }
}

static void RequireMatches(string source, string pattern, int expected,
    string message)
{
    var actual = Regex.Matches(source, pattern,
        RegexOptions.CultureInvariant).Count;
    Equal(expected, actual, message);
}

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.Ordinal), message);

static void Reject(string source, string value, string message) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), message);

static void SequenceEqual<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string message)
{
    Equal(expected.Count, actual.Count, message + " count");
    for (var index = 0; index < expected.Count; index++)
        Equal(expected[index], actual[index], message + "[" + index + "]");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected [{expected}], actual [{actual}]");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory, AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
        }
    }
    throw new DirectoryNotFoundException(
        "repository root containing GameSvr/GameSvr.csproj was not found");
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

file sealed class InsertionOracle
{
    private readonly Func<int, bool> _insert;

    public InsertionOracle(int balance, Func<int, bool> insert)
    {
        Balance = balance;
        _insert = insert;
    }

    public int Balance { get; private set; }
    public List<string> Events { get; } = new();

    public void ExchangeRy(int itemIndex, int fee, string itemName)
    {
        Events.Add("create:" + itemIndex);
        Balance = unchecked(Balance - fee);
        Events.Add("debit:" + fee);
        Events.Add("fee37:" + fee);
        Events.Add("insert:" + itemIndex);
        if (!_insert(itemIndex))
        {
            Events.Add("dispose:" + itemIndex);
            return;
        }
        Complete(itemName);
    }

    public void ExchangeSw(int itemIndex, string itemName)
    {
        const int fee = 640;
        Events.Add("create:" + itemIndex);
        Balance = unchecked(Balance - fee);
        Events.Add("debit:" + fee);
        Events.Add("insert:" + itemIndex);
        if (!_insert(itemIndex))
        {
            Events.Add("dispose:" + itemIndex);
            return;
        }
        Events.Add("fee37:" + fee);
        Complete(itemName);
    }

    private void Complete(string itemName)
    {
        Events.Add("item9:" + itemName);
        Events.Add("dialog:" + itemName);
    }
}

file sealed record Snapshot(
    int ExplicitBalance,
    int ContextBalance,
    int ExplicitBagCount,
    int ContextBagCount,
    int ExplicitMessageCount,
    int ContextMessageCount,
    int LogCount)
{
    public static Snapshot Capture(TPlayObject explicitPlayer,
        TPlayObject context) => new(
        explicitPlayer.m_nShengWan,
        context.m_nShengWan,
        explicitPlayer.m_ItemList.Count,
        context.m_ItemList.Count,
        explicitPlayer.m_MsgList.Count,
        context.m_MsgList.Count,
        M2Share.LogStringList.Count);

    public void AssertUnchanged(TPlayObject explicitPlayer,
        TPlayObject context, string label)
    {
        Ensure(ExplicitBalance == explicitPlayer.m_nShengWan,
            label + " changed explicit balance");
        Ensure(ContextBalance == context.m_nShengWan,
            label + " changed context balance");
        Ensure(ExplicitBagCount == explicitPlayer.m_ItemList.Count,
            label + " changed explicit bag");
        Ensure(ContextBagCount == context.m_ItemList.Count,
            label + " changed context bag");
        Ensure(ExplicitMessageCount == explicitPlayer.m_MsgList.Count,
            label + " changed explicit messages");
        Ensure(ContextMessageCount == context.m_MsgList.Count,
            label + " changed context messages");
        Ensure(LogCount == M2Share.LogStringList.Count,
            label + " changed logs");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
