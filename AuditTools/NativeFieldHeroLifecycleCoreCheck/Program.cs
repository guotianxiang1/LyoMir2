using GameSvr.Services;

CheckStrictUnsignedRunGate();
CheckAdjacentGhostTransfer();
CheckRunProducedGhostDeferral();
CheckPendingReaper();
CheckPendingReaperExceptionBoundary();
CheckGuardsAndSurface();

Console.WriteLine("PASS NativeFieldHeroLifecycleCoreCheck " +
                  "active=sub_605790 pending=sub_605814 " +
                  "clock=once unsigned=exact cleanup=remove-before-free");

static void CheckStrictUnsignedRunGate()
{
    var log = new List<string>();
    var equal = Entry("equal", log, false, 100, 50);
    var due = Entry("due", log, false, 99, 50);
    var dueSecond = Entry("due-second", log, false, 98, 50);
    var core = new NativeFieldHeroLifecycleCore();
    core.AddActive(equal);
    core.AddActive(due);
    core.AddActive(dueSecond);

    var clockCalls = 0;
    core.ProcessActive(() =>
    {
        clockCalls++;
        return 150;
    });
    Equal(1, clockCalls, "one active-pass clock capture");
    Equal(0, equal.RunCalls, "elapsed equal interval is not due");
    Equal(100, equal.RunTick, "not-due run tick remains unchanged");
    Check(!log.Contains("equal:set:150"),
        "not-due actor does not invoke run-tick setter");
    Equal(1, due.RunCalls, "same elapsed uses same captured now");
    Equal(1, dueSecond.RunCalls, "later due actor also runs");
    Equal(150, due.RunTick, "run tick written to captured now");
    Check(log.IndexOf("due:set:150") < log.IndexOf("due:run"),
        "run tick write precedes Run");
    Check(log.IndexOf("due:run") < log.IndexOf("due-second:run"),
        "due live actors run in active-list order");

    var wrap = Entry("wrap", log, false,
        unchecked((int)0xFFFFFFF0), 31);
    var wrapCore = new NativeFieldHeroLifecycleCore();
    wrapCore.AddActive(wrap);
    wrapCore.ProcessActive(() => 0x10);
    Equal(1, wrap.RunCalls, "unsigned wraparound elapsed is due");
    Equal(0x10, wrap.RunTick, "wraparound run tick update");
}

static void CheckAdjacentGhostTransfer()
{
    var log = new List<string>();
    var first = Entry("first", log, true, 0, 0);
    var second = Entry("second", log, true, 0, 0);
    var live = Entry("live", log, false, 0, 1000);
    var core = new NativeFieldHeroLifecycleCore();
    core.AddActive(first);
    core.AddActive(second);
    core.AddActive(live);
    first.OnVmt7C = () =>
    {
        Check(core.Active.Contains(first),
            "VMT+7C runs while actor remains active");
        Check(!core.Pending.Contains(first),
            "VMT+7C runs before pending append");
    };

    core.ProcessActive(() => 1);
    Equal(1, core.Active.Count, "adjacent ghosts removed without skip");
    Check(ReferenceEquals(live, core.Active[0]),
        "live actor remains active");
    Equal(2, core.Pending.Count, "both ghosts appended pending");
    Check(ReferenceEquals(first, core.Pending[0]),
        "pending preserves first append");
    Check(ReferenceEquals(second, core.Pending[1]),
        "pending preserves second append");
    Equal("first:vmt7c", log[0], "first VMT+7C before transfer");
    Equal("second:vmt7c", log[1], "second VMT+7C before transfer");
    Equal(0, first.RunCalls, "due ghost never runs");
    Equal(0, second.RunCalls, "adjacent due ghost never runs");
}

static void CheckRunProducedGhostDeferral()
{
    var log = new List<string>();
    var entry = Entry("late", log, false, 0, 0);
    entry.RunMakesGhost = true;
    var core = new NativeFieldHeroLifecycleCore();
    core.AddActive(entry);

    core.ProcessActive(() => 1);
    Equal(1, entry.RunCalls, "due actor runs");
    Equal(1, core.Active.Count,
        "Run-produced ghost stays active for current pass");
    Equal(0, core.Pending.Count,
        "Run-produced ghost not transferred immediately");

    core.ProcessActive(() => 2);
    Equal(0, core.Active.Count, "next pass transfers ghost");
    Equal(1, core.Pending.Count, "next pass appends pending");
    Equal(1, entry.Vmt7CCalls, "VMT+7C invoked once");
}

static void CheckPendingReaper()
{
    var log = new List<string>();
    var older = Entry("older", log, true, 0, 0, 0);
    var exact = Entry("exact", log, true, 0, 0, 1);
    var young = Entry("young", log, true, 0, 0, 2);
    var core = new NativeFieldHeroLifecycleCore();
    core.AddActive(older);
    core.AddActive(exact);
    core.AddActive(young);
    core.ProcessActive(() => 10);
    log.Clear();

    older.OnFree = () => Check(!core.Pending.Contains(older),
        "older removed before Free");
    exact.OnFree = () => Check(!core.Pending.Contains(exact),
        "exact removed before Free");
    core.ReapPending(300001);

    Equal(1, core.Pending.Count, "299999ms pending actor retained");
    Check(ReferenceEquals(young, core.Pending[0]),
        "young pending actor identity retained");
    Equal(1, older.FreeCalls, ">300000ms actor freed");
    Equal(1, exact.FreeCalls, "300000ms actor freed");
    Equal(0, young.FreeCalls, "299999ms actor not freed");
    Equal("exact:free", log[0], "pending scan starts at tail");
    Equal("older:free", log[1], "pending scan continues backward");

    var wrapTick = unchecked((int)0xFFFFFFF0);
    var wrap = Entry("wrap-free", log, true, 0, 0, wrapTick);
    var wrapCore = new NativeFieldHeroLifecycleCore();
    wrapCore.AddActive(wrap);
    wrapCore.ProcessActive(() => 0);
    wrapCore.ReapPending(unchecked(wrapTick + 300000));
    Equal(1, wrap.FreeCalls,
        "pending reaper uses unsigned wraparound elapsed");
}

static void CheckPendingReaperExceptionBoundary()
{
    var operationLog = new List<string>();
    var errorLog = new List<string>();
    var earlier = Entry("earlier", operationLog, true, 0, 0, 0);
    var throwingTail = Entry("throwing", operationLog, true, 0, 0, 0);
    throwingTail.ThrowOnFree = true;
    var core = new NativeFieldHeroLifecycleCore(errorLog.Add);
    core.AddActive(earlier);
    core.AddActive(throwingTail);
    core.ProcessActive(() => 1);
    operationLog.Clear();

    core.ReapPending(300000);

    Equal(1, throwingTail.FreeCalls, "throwing tail Free invoked once");
    Check(!core.Pending.Contains(throwingTail),
        "throwing actor removed before Free exception");
    Check(core.Pending.Contains(earlier),
        "exception ends current backward scan");
    Equal(0, earlier.FreeCalls,
        "earlier pending actor deferred to later pass");
    Equal(1, errorLog.Count, "reaper exception logged once");
    Equal(NativeFieldHeroLifecycleCore.PendingReaperExceptionMessage,
        errorLog[0], "exact native reaper exception message");
}

static void CheckGuardsAndSurface()
{
    Equal(300000u, NativeFieldHeroLifecycleCore.PendingFreeDelay,
        "five-minute pending delay");
    Equal("[Exception]:TMonFortress.RefreshDeleteActs",
        NativeFieldHeroLifecycleCore.PendingReaperExceptionMessage,
        "exact pending-reaper exception prefix");
    var core = new NativeFieldHeroLifecycleCore();
    ExpectThrows<ArgumentNullException>(() => core.AddActive(null),
        "null active entry rejected");
    ExpectThrows<ArgumentNullException>(() => core.ProcessActive(null),
        "null clock rejected");
    Check(typeof(NativeFieldHeroLifecycleCore).GetMethods()
            .All(method => method.Name != "SearchViewRange"),
        "manager loop has no SearchViewRange dispatch");
}

static TestEntry Entry(string name, List<string> log, bool ghost,
    int runTick, int runInterval, int ghostTick = 0)
{
    var entry = new TestEntry(name, log)
    {
        IsGhost = ghost,
        RunInterval = runInterval,
        GhostTick = ghostTick
    };
    entry.SetInitialRunTick(runTick);
    return entry;
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected={expected}, actual={actual}");
    }
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void ExpectThrows<T>(Action action, string description)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException(description);
}

sealed class TestEntry : INativeFieldHeroLifecycleEntry
{
    private readonly string _name;
    private readonly List<string> _log;
    private int _runTick;

    public TestEntry(string name, List<string> log)
    {
        _name = name;
        _log = log;
    }

    public bool IsGhost { get; set; }
    public int RunInterval { get; set; }
    public int GhostTick { get; set; }
    public bool RunMakesGhost { get; set; }
    public bool ThrowOnFree { get; set; }
    public int RunCalls { get; private set; }
    public int Vmt7CCalls { get; private set; }
    public int FreeCalls { get; private set; }
    public Action OnFree { get; set; }
    public Action OnVmt7C { get; set; }

    public int RunTick
    {
        get => _runTick;
        set
        {
            _runTick = value;
            _log.Add($"{_name}:set:{value}");
        }
    }

    public void SetInitialRunTick(int value) => _runTick = value;

    public void InvokeNativeVmt7C()
    {
        Vmt7CCalls++;
        OnVmt7C?.Invoke();
        _log.Add(_name + ":vmt7c");
    }

    public void Run()
    {
        RunCalls++;
        _log.Add(_name + ":run");
        if (RunMakesGhost) IsGhost = true;
    }

    public void Free()
    {
        FreeCalls++;
        OnFree?.Invoke();
        if (ThrowOnFree) throw new InvalidOperationException("free failed");
        _log.Add(_name + ":free");
    }
}
