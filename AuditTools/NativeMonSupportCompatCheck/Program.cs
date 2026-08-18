using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

var checks = 0;
var tempDirectory = Path.Combine(Path.GetTempPath(),
    "NativeMonSupportCompatCheck_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDirectory);

const string DefaultWave =
    "[Mon1]\r\nName=Wave\r\nMap=WaveMap\r\nX=10\r\nY=20\r\nRange=2\r\nNum=1\r\n";

try
{
    CheckPathAndLoader();
    CheckLoaderValidationAndWaveScan();
    CheckToggleAndScheduler();
    CheckWaitingWindowBoundaries();
    CheckBossStateBranches();
    CheckWaveStateAndPersistentQuota();
    CheckExceptionBoundary();
    CheckCoordinateAndPacketContracts();
    CheckProductionWiringSource();

    Console.WriteLine($"PASS NativeMonSupportCompatCheck ({checks} checks): " +
        "loader/path/preservation, toggle, 500ms scheduler, daily windows, " +
        "boss delay, forward waves, failure quota, persistent current, " +
        "exception boundary, XY RNG order, type18 bytes, production wiring.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("NativeMonSupportCompatCheck FAIL: " + exception);
    return 1;
}
finally
{
    try
    {
        Directory.Delete(tempDirectory, true);
    }
    catch
    {
    }
}

void CheckPathAndLoader()
{
    var root = Path.Combine(tempDirectory, "Mir200");
    var expected = Path.GetFullPath(Path.Combine(root, "Share", "Config",
        NativeMonSupport.ConfigFileName));
    Equal(expected, NativeMonSupport.ResolveConfigPath(root, "Share"),
        "root/BaseDir config path");

    var fileName = NewFile("preserve.ini", BuildIni());
    var harness = new Harness();
    harness.Service.Load(fileName);
    True(harness.Service.Loaded && harness.Service.Enabled,
        "valid config loaded/enabled");
    Equal(1, harness.Service.WaveCount, "valid wave count");
    Equal(43200, harness.Service.StartSeconds1, "StartTime1 seconds");
    Equal(120, harness.Service.DelaySeconds, "DelayTime");

    harness.Tick = 10001;
    harness.Now = DateTime.Today.AddHours(12).AddSeconds(1);
    harness.Service.ProcessIfDue(500);
    Equal((byte)1, harness.Service.State, "boss started before preserve load");
    var bossActor = harness.Service.Boss.Actor;
    var oldNotice = harness.Service.StartNotice;
    var oldBossMap = harness.Service.Boss.MapName;
    var oldStateTick = harness.Service.StateTick;
    var oldOuterTick = harness.Service.OuterTick;

    harness.Service.Load(Path.Combine(tempDirectory, "missing.ini"));
    True(!harness.Service.Loaded && !harness.Service.Enabled,
        "missing file clears readiness flags");
    Equal((byte)0, harness.Service.State, "missing file resets state");
    Equal(0, harness.Service.WaveCount, "missing file clears waves");
    True(harness.Service.Cursor == null, "missing file clears cursor");
    True(ReferenceEquals(bossActor, harness.Service.Boss.Actor),
        "missing file preserves boss actor");
    Equal(oldNotice, harness.Service.StartNotice,
        "missing file preserves notice");
    Equal(oldBossMap, harness.Service.Boss.MapName,
        "missing file preserves boss fields");
    Equal(oldStateTick, harness.Service.StateTick,
        "missing file preserves state tick");
    Equal(oldOuterTick, harness.Service.OuterTick,
        "missing file preserves outer tick");
}

void CheckLoaderValidationAndWaveScan()
{
    var missingNotice = new Harness();
    missingNotice.Service.Load(NewFile("missing_notice.ini",
        BuildIni(startNotice: string.Empty)));
    True(!missingNotice.Service.Loaded && !missingNotice.Service.Enabled,
        "empty notice fails load");

    var shortDelay = new Harness();
    shortDelay.Service.Load(NewFile("short_delay.ini", BuildIni(delay: 119)));
    True(!shortDelay.Service.Loaded, "DelayTime below 120 fails load");

    var badBossMap = new Harness();
    badBossMap.Service.Load(NewFile("bad_boss.ini",
        BuildIni(bossMap: "RemoteMap")));
    True(!badBossMap.Service.Loaded, "remote boss map fails whole load");

    const string mixedWaves =
        "[Mon1]\r\nName=BadWave\r\nMap=RemoteMap\r\nX=1\r\nY=2\r\nRange=3\r\nNum=4\r\n" +
        "[Mon2]\r\nName=GoodWave\r\nMap=WaveMap2\r\nX=5\r\nY=6\r\nRange=7\r\nNum=8\r\n";
    var mixed = new Harness();
    var mixedFile = NewFile("mixed.ini", BuildIni(waves: mixedWaves));
    mixed.Service.Load(mixedFile);
    True(mixed.Service.Loaded && mixed.Service.Enabled,
        "invalid wave does not fail whole load");
    Equal(1, mixed.Service.WaveCount, "invalid wave discarded, scan continues");
    Equal("GoodWave", mixed.Service.Waves[0].MonsterName,
        "later valid wave retained");
    Equal(mixedFile + "[Error]: 配置错误 - RemoteMap 不在本GS！",
        mixed.Logs.Single(), "invalid wave exact log");

    const string emptyFirst =
        "[Mon1]\r\nName=\r\nMap=WaveMap\r\n" +
        "[Mon2]\r\nName=MustNotLoad\r\nMap=WaveMap\r\nNum=1\r\n";
    var zero = new Harness();
    var zeroFile = NewFile("zero.ini", BuildIni(waves: emptyFirst));
    zero.Service.Load(zeroFile);
    True(zero.Service.Loaded && zero.Service.Enabled,
        "zero-wave config still loaded/enabled");
    Equal(0, zero.Service.WaveCount, "first empty Name stops scan");
    Equal(NativeMonSupport.ReloadFailed, zero.Service.Reload(zeroFile),
        "reload reports failure for zero waves");

    var valid = new Harness();
    var validFile = NewFile("reload_ok.ini", BuildIni());
    Equal(NativeMonSupport.ReloadSucceeded, valid.Service.Reload(validFile),
        "reload success requires a wave");

    var badTime = new Harness();
    badTime.Service.Load(NewFile("bad_time.ini",
        BuildIni(start1: "not-a-time", start2: "01:02:03")));
    True(badTime.Service.Loaded, "invalid single time does not fail load");
    Equal(0, badTime.Service.StartSeconds1, "invalid StartTime1 becomes zero");
    Equal(3723, badTime.Service.StartSeconds2,
        "StartTime2 parses independently");
}

void CheckToggleAndScheduler()
{
    var notReady = new Harness();
    Equal(NativeMonSupport.NotReady, notReady.Service.Toggle(),
        "not-ready toggle text");
    True(!notReady.Service.Enabled, "not-ready toggle preserves disabled");

    var harness = new Harness();
    harness.Service.Load(NewFile("toggle.ini", BuildIni()));
    Equal(NativeMonSupport.Stopped, harness.Service.Toggle(),
        "enabled toggle stops");
    True(!harness.Service.Enabled, "toggle disabled state");
    Equal(NativeMonSupport.Started, harness.Service.Toggle(),
        "disabled toggle starts");
    True(harness.Service.Enabled, "toggle enabled state");

    harness.Tick = 0;
    harness.Now = DateTime.Today.AddHours(6);
    harness.Service.ProcessIfDue(499);
    Equal(0, harness.Service.OuterTick, "499ms does not schedule");
    harness.Service.ProcessIfDue(500);
    Equal(500, harness.Service.OuterTick, "500ms schedules");
    harness.Service.ProcessIfDue(999);
    Equal(500, harness.Service.OuterTick, "next 499ms does not schedule");
    harness.Service.ProcessIfDue(1000);
    Equal(1000, harness.Service.OuterTick, "next 500ms schedules");
}

void CheckWaitingWindowBoundaries()
{
    CheckWindowDelta(0, false, "delta 0 excluded");
    CheckWindowDelta(1, true, "delta 1 included");
    CheckWindowDelta(59, true, "delta 59 included");
    CheckWindowDelta(60, false, "delta 60 excluded");

    var strictTick = new Harness();
    strictTick.Service.Load(NewFile("strict_tick.ini", BuildIni()));
    strictTick.Tick = 10000;
    strictTick.Now = DateTime.Today.AddHours(12).AddSeconds(1);
    strictTick.Service.ExecuteEvent();
    Equal(0, strictTick.Spawns.Count, "10000ms state check excluded");
    strictTick.Tick = 10001;
    strictTick.Service.ExecuteEvent();
    Equal(1, strictTick.Spawns.Count, "10001ms state check included");

    var preservedBoss = new Harness();
    var fileName = NewFile("existing_boss.ini", BuildIni());
    preservedBoss.Service.Load(fileName);
    preservedBoss.Tick = 10001;
    preservedBoss.Now = DateTime.Today.AddHours(12).AddSeconds(1);
    preservedBoss.Service.ExecuteEvent();
    var actor = preservedBoss.Service.Boss.Actor;
    preservedBoss.Service.Load(fileName);
    preservedBoss.Tick = 20002;
    preservedBoss.Now = DateTime.Today.AddHours(12).AddSeconds(2);
    preservedBoss.Service.ExecuteEvent();
    Equal(1, preservedBoss.Spawns.Count,
        "existing boss actor suppresses a second spawn");
    True(ReferenceEquals(actor, preservedBoss.Service.Boss.Actor),
        "existing boss actor retained");
    Equal(2, preservedBoss.Broadcasts.Count,
        "existing boss still broadcasts StartNotice");
}

void CheckWindowDelta(int delta, bool starts, string label)
{
    var harness = new Harness();
    harness.Service.Load(NewFile("window_" + delta + ".ini", BuildIni()));
    harness.Tick = 10001;
    harness.Now = DateTime.Today.AddHours(12).AddSeconds(delta);
    harness.Service.ExecuteEvent();
    Equal(starts ? 1 : 0, harness.Spawns.Count, label + " spawn");
    Equal(starts ? (byte)1 : (byte)0, harness.Service.State,
        label + " state");
    if (starts)
    {
        Equal(10, harness.Spawns[0].Range, label + " boss range 10");
        Equal("START", harness.Broadcasts.Single(),
            label + " StartNotice");
    }
}

void CheckBossStateBranches()
{
    var alive = StartBossHarness("boss_alive.ini");
    alive.Tick = 130001;
    alive.Service.ExecuteEvent();
    Equal((byte)1, alive.Service.State, "Delay*1000 equality excluded");
    alive.Tick = 130002;
    alive.Service.ExecuteEvent();
    Equal((byte)0, alive.Service.State, "alive boss returns to waiting");
    Equal("FAIL", alive.Broadcasts.Last(), "alive boss FailNotice");

    var missing = new Harness { SpawnImpl = _ => null };
    StartBoss(missing, "boss_null.ini");
    missing.Tick = 130002;
    missing.Service.ExecuteEvent();
    Equal((byte)2, missing.Service.State, "null boss enters waves");
    Equal("ATTACK", missing.Broadcasts.Last(), "null boss AttackNotice");

    var ghost = StartBossHarness("boss_ghost.ini");
    ghost.Service.Boss.Actor.m_boGhost = true;
    ghost.Tick = 130002;
    ghost.Service.ExecuteEvent();
    Equal((byte)2, ghost.Service.State, "ghost boss enters waves");
    True(ghost.Service.Boss.Actor == null, "ghost boss pointer cleared");

    var dead = StartBossHarness("boss_dead.ini");
    dead.Service.Boss.Actor.m_boDeath = true;
    dead.Tick = 130002;
    dead.Service.ExecuteEvent();
    Equal((byte)2, dead.Service.State, "dead boss enters waves");
}

Harness StartBossHarness(string fileName)
{
    var harness = new Harness();
    StartBoss(harness, fileName);
    return harness;
}

void StartBoss(Harness harness, string fileName, string waves = DefaultWave)
{
    harness.Service.Load(NewFile(fileName, BuildIni(waves: waves)));
    harness.Tick = 10001;
    harness.Now = DateTime.Today.AddHours(12).AddSeconds(1);
    harness.Service.ExecuteEvent();
    Equal((byte)1, harness.Service.State, fileName + " boss state");
}

void CheckWaveStateAndPersistentQuota()
{
    const string waves =
        "[Mon1]\r\nName=WaveOne\r\nMap=WaveMap\r\nX=10\r\nY=20\r\nRange=3\r\nNum=2\r\n" +
        "[Mon2]\r\nName=WaveTwo\r\nMap=WaveMap2\r\nX=30\r\nY=40\r\nRange=4\r\nNum=1\r\n";
    var waveOneCalls = 0;
    var harness = new Harness();
    harness.SpawnImpl = call =>
    {
        if (call.Name == "WaveOne" && ++waveOneCalls == 2)
            return null;
        return NewActor(call.X + 7, call.Y + 9);
    };
    StartBoss(harness, "waves.ini", waves);
    harness.Service.Boss.Actor.m_boDeath = true;
    harness.Tick = 130002;
    harness.Service.ExecuteEvent();
    Equal((byte)2, harness.Service.State, "dead boss opens wave state");
    Equal("WaveOne", harness.Service.Cursor.MonsterName,
        "tail-to-head links preserve forward order");

    harness.Service.ExecuteEvent();
    Equal(1, harness.Service.Waves[0].Current, "first wave quota increment");
    var firstWaveActor = harness.Service.Waves[0].Actor;
    True(firstWaveActor.m_boMission, "spawned wave gets mission flag");
    Equal(firstWaveActor.m_nCurrX, firstWaveActor.m_nMissionX,
        "mission X uses actual actor coordinate");
    Equal(firstWaveActor.m_nCurrY, firstWaveActor.m_nMissionY,
        "mission Y uses actual actor coordinate");
    Equal("WaveOne", harness.Service.Cursor.MonsterName,
        "first quota keeps cursor");

    harness.Service.ExecuteEvent();
    Equal(2, harness.Service.Waves[0].Current,
        "failed spawn still consumes quota");
    Equal("WaveTwo", harness.Service.Cursor.MonsterName,
        "quota advances to next wave");
    True(harness.Logs.Contains("[ERROR]: BuildAttackMon WaveOne"),
        "failed wave exact log");

    harness.Service.ExecuteEvent();
    Equal(1, harness.Service.Waves[1].Current, "second wave quota");
    True(harness.Service.Cursor == null, "last quota clears cursor");
    Equal((byte)2, harness.Service.State,
        "last quota does not reset state in same call");
    harness.Service.ExecuteEvent();
    Equal((byte)0, harness.Service.State,
        "next call resets final wave state");
    Equal(new[] { "Boss", "WaveOne", "WaveOne", "WaveTwo" },
        harness.Spawns.Select(call => call.Name).ToArray(),
        "one spawn attempt per wave call");

    harness.Tick = 140003;
    harness.Now = DateTime.Today.AddHours(12).AddSeconds(2);
    harness.Service.ExecuteEvent();
    Equal((byte)1, harness.Service.State, "second round starts");
    harness.Tick = 260004;
    harness.Service.ExecuteEvent();
    Equal((byte)2, harness.Service.State, "second round opens waves");
    harness.Service.ExecuteEvent();
    Equal(3, harness.Service.Waves[0].Current,
        "wave Current is not reset between rounds");
    Equal("WaveTwo", harness.Service.Cursor.MonsterName,
        "over-quota first wave advances after one attempt");
}

void CheckExceptionBoundary()
{
    var harness = new Harness
    {
        SpawnImpl = _ => throw new InvalidOperationException("boom")
    };
    harness.Service.Load(NewFile("exception.ini", BuildIni()));
    harness.Tick = 10001;
    harness.Now = DateTime.Today.AddHours(12).AddSeconds(1);
    harness.Service.ExecuteEvent();
    Equal((byte)0, harness.Service.State, "spawn exception preserves state 0");
    Equal(10001, harness.Service.StateTick,
        "spawn exception does not roll back prior tick write");
    Equal(NativeMonSupport.EventException, harness.Logs.Single(),
        "event exception exact log");
    Equal(0, harness.Broadcasts.Count, "exception before broadcast");
}

void CheckCoordinateAndPacketContracts()
{
    var bounds = new List<int>();
    var values = new Queue<int>(new[] { 0, 6 });
    NativeMonSupport.ResolveSpawnCoordinates(100, 200, 3,
        bound =>
        {
            bounds.Add(bound);
            return values.Dequeue();
        }, out var x, out var y);
    Equal(new[] { 7, 7 }, bounds.ToArray(), "XY RNG bounds/order");
    Equal(97, x, "X draw applied first");
    Equal(203, y, "Y draw applied second");

    var calls = 0;
    NativeMonSupport.ResolveSpawnCoordinates(10, 20, 0,
        _ =>
        {
            calls++;
            return 0;
        }, out x, out y);
    Equal(0, calls, "range zero consumes no RNG");
    Equal(10, x, "range zero X");
    Equal(20, y, "range zero Y");

    const string text = "攻城开始";
    var packet = UserEngine.CreateNativeMonSupportNoticePacket(text);
    var frame = packet.ToBytes();
    Equal(InternalPacket77.MAGIC,
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)),
        "type18 magic");
    Equal((ushort)18,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(12, 2)),
        "type18 message type");
    Equal((byte)0, frame[^1], "type18 text NUL");
    var parsed = LegacyGateType18.FromBytes(frame, 0, frame.Length);
    True(parsed != null, "type18 parses");
    Equal(0U, parsed.FilterUserIndex, "type18 filter");
    Equal(0, parsed.Recog, "type18 recog");
    Equal((ushort)100, parsed.Ident, "type18 ident");
    Equal((ushort)0x38FF, parsed.Param, "type18 param");
    Equal((ushort)0, parsed.Tag, "type18 tag");
    Equal((ushort)0, parsed.Series, "type18 series");
    Equal(text, HUtil32.GbkEncoding.GetString(parsed.TextBytes),
        "type18 GBK text");
}

void CheckProductionWiringSource()
{
    var root = Directory.GetCurrentDirectory();
    var adapter = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
        "UserEngine.NativeMonSupport.cs"));
    Ordered(adapter, "TryGetMonsterInfo(monsterName",
        "ResolveSpawnCoordinates(x, y, range",
        "AddBaseObject(environment",
        "RegisterNativeMagicTowerRuntimeMonster(monster)",
        "TryInitializeMonsterScript(monster)");
    True(adapter.Contains("if (!TryGetMonsterInfo(monsterName") &&
         adapter.Contains("return null;"),
        "missing ordinary template remains fail-closed");

    var app = File.ReadAllText(Path.Combine(root, "GameSvr", "GameApp.cs"));
    Ordered(app, "Maps.LoadMapInfo()", "LoadNativeMonSupport()",
        "M2Share.LocalDB.LoadMonGen()");
    var engine = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
        "UsrEngn.cs"));
    Ordered(engine, "ProcessNativeMagicTowerRuntimeMonsters();",
        "ProcessNativeMagicTowerDeferredSpawns();",
        "_nativeMonSupport.ProcessIfDue(dwCurrentTick);");
}

string NewFile(string name, string contents)
{
    var path = Path.Combine(tempDirectory, name);
    File.WriteAllText(path, contents, HUtil32.GbkEncoding);
    return path;
}

static string BuildIni(string start1 = "12:00:00",
    string start2 = "23:00:00", string startNotice = "START",
    string attackNotice = "ATTACK", string failNotice = "FAIL",
    int delay = 120, string bossMap = "BossMap",
    string waves = DefaultWave)
{
    return "[Setup]\r\n" +
           "StartTime1=" + start1 + "\r\n" +
           "StartTime2=" + start2 + "\r\n" +
           "StartNotice=" + startNotice + "\r\n" +
           "AttackNotice=" + attackNotice + "\r\n" +
           "FailNotice=" + failNotice + "\r\n" +
           "DelayTime=" + delay + "\r\n" +
           "[boss]\r\nMap=" + bossMap +
           "\r\nX=50\r\nY=60\r\nName=Boss\r\n" + waves;
}

static TBaseObject NewActor(int x, int y)
{
    var actor = (Monster)RuntimeHelpers.GetUninitializedObject(
        typeof(Monster));
    actor.m_nCurrX = unchecked((short)x);
    actor.m_nCurrY = unchecked((short)y);
    return actor;
}

void Ordered(string text, params string[] values)
{
    var position = -1;
    foreach (var value in values)
    {
        var next = text.IndexOf(value, position + 1, StringComparison.Ordinal);
        True(next > position, "source order: " + value);
        position = next;
    }
}

void True(bool value, string label)
{
    checks++;
    if (!value)
        throw new InvalidOperationException(label);
}

void Equal<T>(T expected, T actual, string label)
{
    checks++;
    if (expected is Array expectedArray && actual is Array actualArray)
    {
        if (expectedArray.Cast<object>().SequenceEqual(actualArray.Cast<object>()))
            return;
    }
    else if (EqualityComparer<T>.Default.Equals(expected, actual))
    {
        return;
    }
    throw new InvalidOperationException(
        $"{label}: expected={Format(expected)} actual={Format(actual)}");
}

static string Format(object value)
{
    return value is Array array
        ? "[" + string.Join(",", array.Cast<object>()) + "]"
        : value?.ToString() ?? "<null>";
}

internal sealed class Harness
{
    private readonly HashSet<string> _localMaps = new(StringComparer.Ordinal)
    {
        "BossMap", "WaveMap", "WaveMap2"
    };

    internal Harness()
    {
        Service = new NativeMonSupport(
            map => _localMaps.Contains(map),
            (map, name, x, y, range) =>
            {
                var call = new SpawnCall(map, name, x, y, range);
                Spawns.Add(call);
                return SpawnImpl != null
                    ? SpawnImpl(call)
                    : NewDefaultActor(x, y);
            },
            Broadcasts.Add,
            Logs.Add,
            () => Tick,
            () => Now);
    }

    internal NativeMonSupport Service { get; }
    internal int Tick { get; set; }
    internal DateTime Now { get; set; } = DateTime.Today;
    internal Func<SpawnCall, TBaseObject> SpawnImpl { get; set; }
    internal List<SpawnCall> Spawns { get; } = new();
    internal List<string> Broadcasts { get; } = new();
    internal List<string> Logs { get; } = new();

    private static TBaseObject NewDefaultActor(int x, int y)
    {
        var actor = (Monster)RuntimeHelpers.GetUninitializedObject(
            typeof(Monster));
        actor.m_nCurrX = unchecked((short)x);
        actor.m_nCurrY = unchecked((short)y);
        return actor;
    }
}

internal sealed record SpawnCall(string Map, string Name, int X, int Y,
    int Range);
