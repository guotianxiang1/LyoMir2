using GameSvr.Services;

TestSignInAndOpen();
TestCloseDrawState();
TestClaimState();
TestSignActWinners();
TestEverydaySignAndTag();
TestEverydaySchedule();
TestEverydayExistingWinners();
TestEverydayDrawContinuesAfterFailure();
TestSourceContracts();

Console.WriteLine(
    "PASS NativeSignAct core=synchronous SignIn=count/open-reset/" +
    "close-draw claim=0..4/-1/no-CAS daily=replace/SQL-date/initial/00:06 " +
    "updates=SignAct-stop/Everyday-continue rewards=none runtime=GameState");
return;

static void TestSignInAndOpen()
{
    var store = new FakeSignActStore();
    var manager = new NativeSignActManager(store);
    Assert(!manager.SignIn(false, "关闭人物"),
        "closed SignIn returned success");
    EqualInt(0, store.SignCountQueryCalls,
        "closed SignIn accessed the store");

    Assert(manager.SignIn(true, "新增人物"), "new SignIn failed");
    EqualInt(1, store.SignRows["新增人物"].SignCount,
        "new SignIn count");
    Assert(manager.SignIn(true, "新增人物"), "existing SignIn failed");
    EqualInt(2, store.SignRows["新增人物"].SignCount,
        "existing SignIn increment");

    store.FailSignCountQuery = true;
    Assert(manager.SignIn(true, "查询失败人物"),
        "query failure did not fall through to native insert");
    EqualInt(1, store.SignRows["查询失败人物"].SignCount,
        "query-failure insert count");
    store.FailSignCountQuery = false;

    store.AddSignRow(20, "溢出人物", int.MaxValue, 4);
    Assert(manager.SignIn(true, "溢出人物"), "overflow SignIn failed");
    EqualInt(int.MinValue, store.SignRows["溢出人物"].SignCount,
        "SignCnt did not use unchecked Int32 increment");

    store.FailSignCountUpdate = true;
    Assert(!manager.SignIn(true, "溢出人物"),
        "failed SignCnt update returned success");
    store.FailSignCountUpdate = false;

    Assert(manager.OpenActivity(), "activity reset failed");
    foreach (var row in store.SignRows.Values)
    {
        EqualInt(0, row.SignCount, "reset SignCnt");
        EqualInt(0, row.PrizeType, "reset PrizeType");
    }
    store.ResetResult = false;
    Assert(!manager.OpenActivity(), "failed reset returned success");
}

static void TestCloseDrawState()
{
    var already = new FakeSignActStore { ForceExistingPrize = true };
    var alreadyManager = new NativeSignActManager(already);
    EqualDraw(NativeSignActDrawResult.AlreadyDrawn,
        alreadyManager.CloseActivity(), "already-drawn result");
    EqualInt(0, already.SignDrawSelectCalls,
        "already-drawn path selected candidates");

    var existingQueryFailed = new FakeSignActStore
    {
        ExistingPrizeQueryCountOverride = -1
    };
    EqualDraw(NativeSignActDrawResult.AlreadyDrawn,
        new NativeSignActManager(existingQueryFailed).CloseActivity(),
        "existing-prize SQL failure must follow native non-zero branch");
    EqualInt(0, existingQueryFailed.SignDrawSelectCalls,
        "existing-prize SQL failure selected candidates");

    var empty = new FakeSignActStore();
    EqualDraw(NativeSignActDrawResult.NoWinners,
        new NativeSignActManager(empty).CloseActivity(), "empty draw result");

    var candidateQueryFailed = new FakeSignActStore
    {
        DrawQueryCountOverride = -1
    };
    EqualDraw(NativeSignActDrawResult.Success,
        new NativeSignActManager(candidateQueryFailed).CloseActivity(),
        "candidate SQL failure must follow native non-zero/empty-dataset branch");
    EqualInt(0, candidateQueryFailed.SignPrizeUpdates.Count,
        "candidate SQL failure fabricated prize updates");

    var success = new FakeSignActStore();
    success.SignDrawCandidates.AddRange(new[]
    {
        SignRow(11, "甲"), SignRow(12, "乙"),
        SignRow(13, "丙"), SignRow(14, "不得写入")
    });
    EqualDraw(NativeSignActDrawResult.Success,
        new NativeSignActManager(success).CloseActivity(), "draw success");
    EqualPrizeUpdates(new[] { (11, 1), (12, 2), (13, 2) },
        success.SignPrizeUpdates, "native three-winner draw");

    var partial = new FakeSignActStore { FailPrizeUpdateCall = 2 };
    partial.SignDrawCandidates.AddRange(new[]
    {
        SignRow(21, "甲"), SignRow(22, "乙"), SignRow(23, "丙")
    });
    EqualDraw(NativeSignActDrawResult.UpdateFailed,
        new NativeSignActManager(partial).CloseActivity(),
        "partial draw failure result");
    EqualPrizeUpdates(new[] { (21, 1), (22, 2) },
        partial.SignPrizeUpdates,
        "SignAct draw did not stop on first failed update");
}

static void TestClaimState()
{
    foreach (var state in new[] { 0, 3, 4, -7, 99 })
    {
        var store = StoreWithPrize(state);
        EqualInt(state, new NativeSignActManager(store).Claim("领奖人物"),
            "unchanged claim state " + state);
        EqualInt(0, store.SignPrizeUpdates.Count,
            "unchanged state performed an update " + state);
    }

    var first = StoreWithPrize(1);
    EqualInt(1, new NativeSignActManager(first).Claim("领奖人物"),
        "primary claim return");
    EqualPrizeUpdates(new[] { (1, 3) }, first.SignPrizeUpdates,
        "primary claim transition");

    var second = StoreWithPrize(2);
    EqualInt(2, new NativeSignActManager(second).Claim("领奖人物"),
        "secondary claim return");
    EqualPrizeUpdates(new[] { (1, 4) }, second.SignPrizeUpdates,
        "secondary claim transition");

    var failed = StoreWithPrize(1);
    failed.FailPrizeUpdateCall = 1;
    EqualInt(-1, new NativeSignActManager(failed).Claim("领奖人物"),
        "failed claim return");

    var missing = new FakeSignActStore();
    EqualInt(0, new NativeSignActManager(missing).Claim("不存在"),
        "missing claim return");
    missing.FailSignPrizeQuery = true;
    EqualInt(0, new NativeSignActManager(missing).Claim("查询失败"),
        "query-failed claim return");
}

static void TestSignActWinners()
{
    var store = new FakeSignActStore();
    store.SignWinnerRows.AddRange(new[]
    {
        SignRow(1, "二等奖甲", 2),
        SignRow(2, "一等奖旧", 1),
        SignRow(3, "二等奖乙", 4),
        SignRow(4, "一等奖新", 3),
        SignRow(5, "第三个二等奖", 2)
    });
    var winners = new NativeSignActManager(store).GetWinners();
    EqualString("一等奖新", winners.Primary, "primary winner overwrite order");
    EqualString("二等奖甲", winners.Lucky1, "first secondary winner");
    EqualString("二等奖乙", winners.Lucky2, "second secondary winner");
}

static void TestEverydaySignAndTag()
{
    var store = new FakeSignActStore { ReplaceEverydayResult = false };
    var manager = new NativeSignActManager(store);
    manager.SignInEveryday("每日人物");
    EqualString("每日人物", store.LastEverydaySignIn,
        "daily REPLACE character");

    EqualInt(0, manager.GetYesterdayPrizeTag("每日人物"),
        "zero-row daily tag");
    store.YesterdayTags.Add(2);
    EqualInt(2, manager.GetYesterdayPrizeTag("每日人物"),
        "one-row daily tag");
    store.YesterdayTags.Add(1);
    EqualInt(0, manager.GetYesterdayPrizeTag("每日人物"),
        "multi-row daily tag");
}

static void TestEverydaySchedule()
{
    var store = new FakeSignActStore();
    var manager = new NativeSignActManager(store);
    EqualDaily(NativeSignActDailyProcessResult.Processed,
        manager.ProcessEveryday(new DateTime(2026, 7, 20, 0, 0, 0)),
        "initial daily process");
    EqualInt(1, store.EverydayWinnerSelectCalls,
        "initial process count");
    EqualDaily(NativeSignActDailyProcessResult.NoDateChange,
        manager.ProcessEveryday(new DateTime(2026, 7, 20, 23, 59, 0)),
        "same-date daily process");

    EqualDaily(NativeSignActDailyProcessResult.WaitingForMinuteSix,
        manager.ProcessEveryday(new DateTime(2026, 7, 21, 0, 5, 59)),
        "00:05 rollover");
    EqualDate(new DateOnly(2026, 7, 20), manager.LastEverydayLocalDate,
        "waiting rollover advanced cached date");
    EqualDaily(NativeSignActDailyProcessResult.Processed,
        manager.ProcessEveryday(new DateTime(2026, 7, 21, 0, 6, 0)),
        "00:06 rollover");
    EqualDate(new DateOnly(2026, 7, 21), manager.LastEverydayLocalDate,
        "processed rollover cached date");

    EqualDaily(NativeSignActDailyProcessResult.WaitingForMinuteSix,
        manager.ProcessEveryday(new DateTime(2026, 7, 22, 12, 5, 0)),
        "date change uses minute field, not hour");
    EqualDaily(NativeSignActDailyProcessResult.Processed,
        manager.ProcessEveryday(new DateTime(2026, 7, 22, 12, 6, 0)),
        "minute-six processing outside midnight hour");
}

static void TestEverydayExistingWinners()
{
    var store = new FakeSignActStore();
    store.EverydayWinnerRows.AddRange(new[]
    {
        EverydayRow(0, "二等奖甲", 2),
        EverydayRow(0, "一等奖旧", 1),
        EverydayRow(0, "二等奖乙", 3),
        EverydayRow(0, "一等奖新", 1)
    });
    var manager = new NativeSignActManager(store);
    manager.ProcessEveryday(new DateTime(2026, 7, 20, 0, 0, 0));
    EqualString("一等奖新", manager.GetEverydayWinners(1),
        "existing daily primary");
    EqualString("二等奖甲, 二等奖乙", manager.GetEverydayWinners(2),
        "existing daily secondary separator/order");
    EqualString(manager.GetEverydayWinners(2), manager.GetEverydayWinners(0),
        "non-primary level must return secondary string");
    EqualInt(0, store.EverydayDrawSelectCalls,
        "existing daily winners triggered redraw");

    var queryFailed = new FakeSignActStore
    {
        EverydayWinnerQueryCountOverride = -1
    };
    var failedManager = new NativeSignActManager(queryFailed);
    failedManager.ProcessEveryday(new DateTime(2026, 7, 20, 0, 0, 0));
    EqualString(string.Empty, failedManager.GetEverydayWinners(1),
        "failed existing-winner query fabricated primary");
    EqualString(string.Empty, failedManager.GetEverydayWinners(2),
        "failed existing-winner query fabricated secondaries");
    EqualInt(0, queryFailed.EverydayDrawSelectCalls,
        "failed existing-winner query incorrectly redrew yesterday");
}

static void TestEverydayDrawContinuesAfterFailure()
{
    var store = new FakeSignActStore();
    store.FailEverydayUpdateIndexes.Add(32);
    store.EverydayDrawCandidates.AddRange(new[]
    {
        EverydayRow(31, "甲"), EverydayRow(32, "乙"),
        EverydayRow(33, "丙"), EverydayRow(34, "丁"),
        EverydayRow(35, "不得写入")
    });
    var manager = new NativeSignActManager(store);
    manager.ProcessEveryday(new DateTime(2026, 7, 20, 0, 0, 0));
    EqualString("甲", manager.GetEverydayWinners(1),
        "drawn daily primary");
    EqualString("乙, 丙, 丁", manager.GetEverydayWinners(2),
        "drawn daily secondaries");
    EqualPrizeUpdates(new[] { (31, 1), (32, 2), (33, 2), (34, 2) },
        store.EverydayPrizeUpdates,
        "daily updates must continue after a failed row");
    EqualDate(new DateOnly(2026, 7, 20), manager.LastEverydayLocalDate,
        "daily date was not cached before partial updates");
}

static void TestSourceContracts()
{
    var root = FindRepositoryRoot();
    var manager = ReadSource(Path.Combine(root, "GameSvr", "Services",
        "NativeSignActManager.cs"));
    var store = ReadSource(Path.Combine(root, "GameSvr", "Services",
        "NativeSignActStore.cs"));
    var startup = ReadSource(Path.Combine(root, "GameSvr", "GameApp.cs"));
    var globals = ReadSource(Path.Combine(root, "GameSvr", "M2Share.cs"));
    var bridge = ReadSource(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));

    foreach (var value in new[]
             {
                 "ChrName char(14) binary not null UNIQUE",
                 "Index SignCnt_Index (SignCnt)",
                 "ChrName char(20) binary not null",
                 "Unique key u_key(ChrName, signDate)",
                 "where binary chrName=@characterName",
                 "SignCnt >= 5 ",
                 "order by rand() limit 3;",
                 "PrizeType > 0 limit 1",
                 "replace into gamedata.signActEveryday",
                 "Date_Sub(CurDate(), interval 1 day)",
                 "date_sub(curdate(), interval 1 day)",
                 "order by rand() limit 4",
                 "AddGbkParameter(command, \"@characterName\", characterName, 14)",
                 "AddGbkParameter(command, \"@characterName\", characterName, 20)"
             })
        Require(store, value, "native store contract " + value);

    Require(manager, "if (_lastEverydayLocalDate.HasValue && localNow.Minute <= 5)",
        "native minute-six guard");
    var cache = RequiredIndex(manager, "_lastEverydayLocalDate = currentDate;",
        "daily cache update");
    var draw = RequiredIndex(manager, "LoadOrDrawEverydayWinners();",
        "daily draw call");
    Assert(cache < draw, "daily cache is not advanced before draw/load");
    Require(manager, "_store.UpdateEverydayPrizeTag(row.Index, prizeTag);",
        "best-effort daily row update");
    Require(manager, "return NativeSignActDrawResult.UpdateFailed;",
        "SignAct draw stop-on-failure");
    Require(manager, "prizeType + 2",
        "claim stores prizeType+2 on success");
    Require(manager, ": -1",
        "claim returns -1 when the prize update fails");

    foreach (var source in new[] { manager, store })
    foreach (var forbidden in new[]
             {
                 "BeginTransaction", "TransactionScope", "FOR UPDATE",
                 "Market_Saved", "Market_Prices", "UserData.dat",
                 "YBData.json", "YBShopScript.json", "tbl_"
             })
        Reject(source, forbidden, "non-native SignAct behavior " + forbidden);
    Reject(manager, "M2Share", "core global runtime access");
    Reject(manager, "Task.", "core task ownership");
    Reject(manager, "System.Threading.Timer", "core timer ownership");
    Reject(manager, "new Timer", "core timer allocation");
    Reject(store, "DateTime.Now", "local-date SQL substitution");

    Require(startup,
        "var signActSchemasReady = signActStore.EnsureSchemas(",
        "GameApp SignAct schema lifecycle");
    Require(startup,
        "M2Share.SignActManager = new NativeSignActManager(signActStore);",
        "GameApp SignAct runtime publication");
    var schemaCheck = RequiredIndex(startup,
        "var signActSchemasReady = signActStore.EnsureSchemas(",
        "GameApp SignAct schema check");
    var publish = RequiredIndex(startup,
        "M2Share.SignActManager = new NativeSignActManager(signActStore);",
        "GameApp SignAct manager publication");
    var schemaBranch = RequiredIndex(startup, "if (signActSchemasReady)",
        "GameApp SignAct schema result branch");
    Assert(schemaCheck < publish && publish < schemaBranch,
        "schema failure must not suppress the native manager lifetime");
    Require(store,
        "TryEnsureSchema(\"SignAct\", CreateSignActSql, errors);",
        "independent SignAct schema attempt");
    Require(store,
        "TryEnsureSchema(\"SignActEveryday\", CreateEverydaySql, errors);",
        "independent SignActEveryday schema attempt");
    Require(globals,
        "public static NativeSignActManager SignActManager = null;",
        "M2Share SignAct manager owner");
    Require(bridge,
        "M2Share.ServerSwitches.IsBitSet(2, 0x40)",
        "SignIn native bit-22 gate");
}

static FakeSignActStore StoreWithPrize(int prizeType)
{
    var store = new FakeSignActStore();
    store.AddSignRow(1, "领奖人物", 5, prizeType);
    return store;
}

static NativeSignActRow SignRow(int index, string name, int prizeType = 0) =>
    new(index, name, 5, prizeType);

static NativeSignActEverydayRow EverydayRow(int index, string name,
    int prizeTag = 0) => new(index, name, prizeTag);

static void EqualPrizeUpdates(IEnumerable<(int Index, int Prize)> expected,
    IReadOnlyList<(int Index, int Prize)> actual, string message)
{
    var expectedArray = expected.ToArray();
    Assert(expectedArray.Length == actual.Count,
        $"{message}: expected {expectedArray.Length}, actual {actual.Count}");
    for (var i = 0; i < expectedArray.Length; i++)
        Assert(expectedArray[i] == actual[i],
            $"{message}[{i}]: expected {expectedArray[i]}, actual {actual[i]}");
}

static string ReadSource(string path) =>
    File.ReadAllText(path).Replace("\r\n", "\n").Replace("\r", "\n");

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("GameSvr/GameSvr.csproj was not found");
}

static int RequiredIndex(string source, string value, string message)
{
    var index = source.IndexOf(value, StringComparison.Ordinal);
    if (index < 0) throw new InvalidOperationException(message + " is missing");
    return index;
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message + " is missing");
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(message + " is present");
}

static void EqualInt(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualString(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{message}: expected '{expected}', actual '{actual}'");
}

static void EqualDraw(NativeSignActDrawResult expected,
    NativeSignActDrawResult actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualDaily(NativeSignActDailyProcessResult expected,
    NativeSignActDailyProcessResult actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualDate(DateOnly expected, DateOnly? actual, string message)
{
    if (actual != expected)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeSignActStore : INativeSignActStore
{
    public Dictionary<string, MutableSignRow> SignRows { get; } =
        new(StringComparer.Ordinal);
    public List<NativeSignActRow> SignDrawCandidates { get; } = new();
    public List<NativeSignActRow> SignWinnerRows { get; } = new();
    public List<(int Index, int Prize)> SignPrizeUpdates { get; } = new();
    public List<int> YesterdayTags { get; } = new();
    public List<NativeSignActEverydayRow> EverydayWinnerRows { get; } = new();
    public List<NativeSignActEverydayRow> EverydayDrawCandidates { get; } = new();
    public List<(int Index, int Prize)> EverydayPrizeUpdates { get; } = new();
    public HashSet<int> FailEverydayUpdateIndexes { get; } = new();

    public bool FailSignCountQuery { get; set; }
    public bool FailSignPrizeQuery { get; set; }
    public bool FailSignCountUpdate { get; set; }
    public bool ResetResult { get; set; } = true;
    public bool ForceExistingPrize { get; set; }
    public int? ExistingPrizeQueryCountOverride { get; set; }
    public int? DrawQueryCountOverride { get; set; }
    public int? EverydayWinnerQueryCountOverride { get; set; }
    public int FailPrizeUpdateCall { get; set; }
    public bool ReplaceEverydayResult { get; set; } = true;
    public int SignCountQueryCalls { get; private set; }
    public int SignDrawSelectCalls { get; private set; }
    public int EverydayWinnerSelectCalls { get; private set; }
    public int EverydayDrawSelectCalls { get; private set; }
    public string LastEverydaySignIn { get; private set; } = string.Empty;

    public bool EnsureSchemas(out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryGetSignCountRow(string characterName,
        out NativeSignActRow row)
    {
        SignCountQueryCalls++;
        row = null;
        if (FailSignCountQuery || !SignRows.TryGetValue(characterName, out var found))
            return false;
        row = found.ToRow();
        return true;
    }

    public bool TryGetSignPrizeRow(string characterName,
        out NativeSignActRow row)
    {
        row = null;
        if (FailSignPrizeQuery || !SignRows.TryGetValue(characterName, out var found))
            return false;
        row = found.ToRow();
        return true;
    }

    public bool InsertSignAct(string characterName)
    {
        if (SignRows.ContainsKey(characterName)) return false;
        AddSignRow(SignRows.Count + 1, characterName, 1, 0);
        return true;
    }

    public bool UpdateSignCount(int index, int signCount)
    {
        if (FailSignCountUpdate) return false;
        var row = SignRows.Values.Single(value => value.Index == index);
        row.SignCount = signCount;
        return true;
    }

    public bool ResetSignAct()
    {
        if (!ResetResult) return false;
        foreach (var row in SignRows.Values)
        {
            row.SignCount = 0;
            row.PrizeType = 0;
        }
        return true;
    }

    public int QueryExistingSignActPrizeCount() =>
        ExistingPrizeQueryCountOverride ??
        (ForceExistingPrize || SignRows.Values.Any(row => row.PrizeType > 0)
            ? 1
            : 0);

    public IReadOnlyList<NativeSignActRow> SelectSignActDrawCandidates(
        out int queryCount)
    {
        SignDrawSelectCalls++;
        queryCount = DrawQueryCountOverride ?? SignDrawCandidates.Count;
        return SignDrawCandidates;
    }

    public bool UpdateSignActPrizeType(int index, int prizeType)
    {
        SignPrizeUpdates.Add((index, prizeType));
        if (FailPrizeUpdateCall == SignPrizeUpdates.Count) return false;
        var row = SignRows.Values.FirstOrDefault(value => value.Index == index);
        if (row != null) row.PrizeType = prizeType;
        return true;
    }

    public IReadOnlyList<NativeSignActRow> SelectSignActWinners() =>
        SignWinnerRows.Count > 0
            ? SignWinnerRows
            : SignRows.Values.Where(row => row.PrizeType > 0)
                .Select(row => row.ToRow()).ToArray();

    public bool ReplaceEverydaySignIn(string characterName)
    {
        LastEverydaySignIn = characterName;
        return ReplaceEverydayResult;
    }

    public IReadOnlyList<int> SelectYesterdayPrizeTags(string characterName) =>
        YesterdayTags;

    public IReadOnlyList<NativeSignActEverydayRow>
        SelectYesterdayEverydayWinners(out int queryCount)
    {
        EverydayWinnerSelectCalls++;
        queryCount = EverydayWinnerQueryCountOverride ?? EverydayWinnerRows.Count;
        return EverydayWinnerRows;
    }

    public IReadOnlyList<NativeSignActEverydayRow>
        SelectYesterdayEverydayDrawCandidates()
    {
        EverydayDrawSelectCalls++;
        return EverydayDrawCandidates;
    }

    public bool UpdateEverydayPrizeTag(int index, int prizeTag)
    {
        EverydayPrizeUpdates.Add((index, prizeTag));
        return !FailEverydayUpdateIndexes.Contains(index);
    }

    public void AddSignRow(int index, string name, int signCount, int prizeType)
    {
        SignRows[name] = new MutableSignRow(index, name, signCount, prizeType);
    }
}

sealed class MutableSignRow
{
    public MutableSignRow(int index, string name, int signCount, int prizeType)
    {
        Index = index;
        Name = name;
        SignCount = signCount;
        PrizeType = prizeType;
    }

    public int Index { get; }
    public string Name { get; }
    public int SignCount { get; set; }
    public int PrizeType { get; set; }
    public NativeSignActRow ToRow() =>
        new(Index, Name, SignCount, PrizeType);
}
