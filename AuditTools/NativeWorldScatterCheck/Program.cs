using GameSvr;

// Pins 战神 sub_71FA20 segment 3 ("世界掉落", TWorldScatterMgr @ VMT 0x74B678) against
// its four native routines: sub_752D40 (config load), sub_75307C (per-second tick),
// sub_7530D8 (per-record accrual) and sub_752CAC/sub_753124 (query + match).
// This subsystem is NOT the drop control of sub_720278 — the two were conflated once
// already and it cost a scatter-range regression, so the range constant is nailed here
// as well.

var configPath = Path.Combine(Path.GetTempPath(),
    "NativeWorldScatterCheck_" + Guid.NewGuid().ToString("N") + ".txt");

PinConstants();
PinLoader();
PinWarmUpAndTick();
PinAccrual();
PinQuery();

Console.WriteLine(
    "NativeWorldScatterCheck PASS scatter-range=3 warmup=1800000ms run-gate=1000ms " +
    "prize-keys=prize..prize99 min-level=le map-optional consume-disarms " +
    "one-drop-per-run");

// The real log sink is M2Share.MainOutMessage, whose static constructor wants a full
// config tree on disk; the audit only cares about the record contents.
static NativeWorldScatterMgr NewManager()
{
    return new NativeWorldScatterMgr { OutMessage = null };
}

void PinConstants()
{
    Equal(3, NativeWorldScatterMgr.NativeScatterRange, "0x71FF3D mov ecx,3");
    Equal(0x1B7740, NativeWorldScatterMgr.NativeWarmUpMs, "0x752C3A add eax,0x1B7740");
    Equal(0x3E8, NativeWorldScatterMgr.NativeRunIntervalMs, "0x75308E cmp eax,0x3E8");
    Equal(0x64, NativeWorldScatterMgr.NativePrizeKeyLimit, "0x752EEE cmp ebx,0x64");
    // 0x4D9E43 ".txt" concatenated onto the 0x752FA8 key is the file name the binary
    // itself writes into main.ini when the module setting is absent.
    Equal("世界爆率文件1.txt", NativeWorldScatter.DefaultConfigFileName,
        "sub_4D9D38 default file name");
}

void PinLoader()
{
    Write(
        "[setting]", "typeNum=2",
        "[type1]", "minLevel=40", "secSpace=60", "maxPile=5",
        "map=D701 | d702,\tD703",
        "prize=屠龙", "prize1=裁决之杖", "prize3=断层",
        "[type2]", "prize=金创药");

    var mgr = NewManager();
    mgr.LoadConfig(configPath);
    Equal(2, mgr.Records.Count, "typeNum drives the record count");

    var first = mgr.Records[0];
    Equal((ushort)40, first.MinLevel, "minLevel -> word [rec+0]");
    Equal(60, first.SecSpace, "secSpace -> [rec+4]");
    Equal((ushort)5, first.MaxPile, "maxPile -> word [rec+2]");
    // sub_7531D0 splits on {' ', '|', TAB, ','} and upper-cases every token.
    Equal(new[] { "D701", "D702", "D703" }, first.Maps?.ToArray(),
        "map list split and upper-cased");
    // 0x752EAF stops at the first empty value, so prize2 missing hides prize3.
    Equal(new[] { "屠龙", "裁决之杖" }, first.Prizes.ToArray(),
        "prize keys must be consecutive");

    var second = mgr.Records[1];
    Equal((ushort)0, second.MinLevel, "minLevel default 0");
    Equal(0, second.SecSpace, "secSpace default 0");
    Equal((ushort)1, second.MaxPile, "maxPile default 1");
    // A missing `map` leaves [rec+0x14] nil, which the matcher reads as "any map".
    Equal(true, second.Maps == null, "absent map key leaves the list nil");

    // 0x752D7E: a missing file returns before the TIniFile is even constructed.
    File.Delete(configPath);
    mgr.LoadConfig(configPath);
    Equal(0, mgr.Records.Count, "missing file clears and returns");
}

void PinWarmUpAndTick()
{
    Write("[setting]", "typeNum=1", "[type1]", "secSpace=1", "maxPile=9",
        "prize=金creek");
    var mgr = NewManager();
    mgr.LoadConfig(configPath);
    var record = mgr.Records[0];

    // The constructor parks [self+0x24] 30 minutes ahead, so Run is inert until then.
    mgr.Run(Now());
    Equal(false, mgr.Armed, "warm-up keeps the manager disarmed");
    Equal(0, record.LastTick, "warm-up does not even seed the record tick");

    // 0x75308E is a strict `jle`, so exactly 1000 ms is still too soon.
    var t0 = Now() + NativeWorldScatterMgr.NativeWarmUpMs;
    mgr.Run(t0 + NativeWorldScatterMgr.NativeRunIntervalMs);
    Equal(0, record.LastTick, "1000ms exactly does not open the gate");

    var t1 = t0 + NativeWorldScatterMgr.NativeRunIntervalMs + 1;
    mgr.Run(t1);
    Equal(t1, record.LastTick, "first admitted run only seeds [rec+8]");
    Equal(0, record.Pending, "seeding run accrues nothing");
    Equal(false, mgr.Armed, "seeding run leaves the manager disarmed");

    mgr.Run(t1 + 4000);
    Equal(4, record.Pending, "4 s at secSpace=1 accrues 4");
    Equal(true, mgr.Armed, "a ready record arms the manager");

    // 0x753116 rewrites [rec+0x10] outright, so the accrual is level-triggered.
    mgr.Run(t1 + 20000);
    Equal(9, record.Pending, "maxPile clamps the accrual");
}

void PinAccrual()
{
    Write("[setting]", "typeNum=1", "[type1]", "secSpace=0", "prize=X");
    var mgr = NewManager();
    mgr.LoadConfig(configPath);
    var record = mgr.Records[0];
    var t0 = Now() + NativeWorldScatterMgr.NativeWarmUpMs + 2000;
    mgr.Run(t0);
    mgr.Run(t0 + 60000);
    // 0x7530EF cmp dword [rec+4],0 / jle: secSpace 0 disables the record entirely.
    Equal(0, record.Pending, "secSpace<=0 never accrues");
    Equal(false, mgr.Armed, "secSpace<=0 never arms");
}

void PinQuery()
{
    Write(
        "[setting]", "typeNum=2",
        "[type1]", "minLevel=50", "secSpace=1", "maxPile=3", "map=D701",
        "prize=屠龙",
        "[type2]", "minLevel=10", "secSpace=1", "maxPile=3", "prize=裁决之杖");

    var mgr = NewManager();
    mgr.LoadConfig(configPath);
    var t0 = Now() + NativeWorldScatterMgr.NativeWarmUpMs + 2000;
    mgr.Run(t0);
    mgr.Run(t0 + 3000);
    Equal(true, mgr.Armed, "both records ready");

    // 0x753155 `jg` fails the record when minLevel > level, so type1 is out of reach
    // for a level-49 monster and the scan falls through to type2.
    Equal(new[] { "裁决之杖" }, mgr.Query(49, "D701", out var count)?.ToArray(),
        "minLevel gate is <=, not <");
    Equal(3, count, "the pending count becomes the repeat count");
    // 0x752D0C disarms on the first hit: one world drop per Run period, server-wide.
    Equal(true, mgr.Query(99, "D701", out _) == null, "a hit disarms the manager");

    mgr.Run(t0 + 6000);
    // type1's map list rejects any other map; type2 has none and takes everything.
    Equal(new[] { "裁决之杖" }, mgr.Query(99, "D002", out _)?.ToArray(),
        "map list rejects a foreign map");
    mgr.Run(t0 + 9000);
    Equal(new[] { "屠龙" }, mgr.Query(99, "d701", out _)?.ToArray(),
        "map match is case-insensitive and records are scanned in order");

    // 0x753195 clears [rec+0x10] on the hit, so the same record cannot fire twice
    // inside one Run period even after the manager is re-armed by hand.
    mgr.Run(t0 + 12000);
    Equal(new[] { "屠龙" }, mgr.Query(99, "D701", out var again)?.ToArray(),
        "re-armed record fires again");
    Equal(3, again, "accrual is recomputed from [rec+8] after the consume");
}

void Write(params string[] lines)
{
    File.WriteAllLines(configPath, lines, SystemModule.HUtil32.GbkEncoding);
}

static int Now()
{
    return SystemModule.HUtil32.GetTickCount();
}

static void Equal<T>(T expected, T actual, string label)
{
    if (expected is Array expectedArray && actual is Array actualArray)
    {
        var expectedValues = expectedArray.Cast<object>().ToArray();
        var actualValues = actualArray.Cast<object>().ToArray();
        if (expectedValues.SequenceEqual(actualValues)) return;
    }
    else if (EqualityComparer<T>.Default.Equals(expected, actual))
    {
        return;
    }
    throw new InvalidOperationException(
        $"{label}: expected={Format(expected)}, actual={Format(actual)}");
}

static string Format<T>(T value)
{
    return value is System.Collections.IEnumerable sequence and not string
        ? string.Join(",", sequence.Cast<object>())
        : value?.ToString() ?? "<null>";
}
