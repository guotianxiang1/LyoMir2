using GameSvr;
using SystemModule;

var timedState = State(NativeDropControlBucketField.ItemName,
    Record("A", "timed", 1, 1, 1, 1, int.MaxValue - 500));
var timed = NativeDropControlRuntime.SelectMap(timedState,
    NativeDropControlType.Timed, int.MinValue + 600, _ => 0);
Equal(1, timed.Count, "unsigned tick rollover trigger");
Equal(int.MinValue + 600,
    timedState.Snapshot(NativeDropControlType.Timed)[0].Tick,
    "timed trigger updates tick first");

var mapRecord = Record("A", "counted", 4, 3, 2, 2, 0);
var mapState = State(NativeDropControlBucketField.ItemName, mapRecord);
Equal(0, SelectMapCounted(mapState, _ => 1).Count, "map count 1");
Equal(1, SelectMapCounted(mapState, _ => 1).Count,
    "map exact selected threshold");
Equal(0, SelectMapCounted(mapState, _ => 1).Count,
    "map cycle boundary");
var mapAfterReset = mapState.Snapshot(NativeDropControlType.Counted)[0];
Equal((ushort)0, mapAfterReset.Counter, "map equality reset counter");
Equal((ushort)2, mapAfterReset.RandomThreshold,
    "map reset selects Random(N)+1");

var mapPastBoundary = Record("A", "map-past", 1, 3, 3, 9, 0);
mapPastBoundary.Counter = 5;
var mapPastState = State(NativeDropControlBucketField.ItemName,
    mapPastBoundary);
SelectMapCounted(mapPastState, _ => 0);
Equal((ushort)6,
    mapPastState.Snapshot(NativeDropControlType.Counted)[0].Counter,
    "map reset is equality only");

var worldPastBoundary = Record("MobA", "world-past", 1, 3, 4, 9, 0);
worldPastBoundary.Counter = 5;
var worldState = State(NativeDropControlBucketField.MonsterName,
    worldPastBoundary,
    Record("MobB", "other-bucket", 1, 1, 5, 1, 0));
NativeDropControlRuntime.SelectWorld(worldState,
    NativeDropControlType.Counted, "MobA", 0, _ => 0);
var worldSnapshot = worldState.Snapshot(NativeDropControlType.Counted);
var worldAfterReset = worldSnapshot.Single(x => x.ItemName == "world-past");
var otherBucket = worldSnapshot.Single(x => x.ItemName == "other-bucket");
Equal((ushort)0, worldAfterReset.Counter, "world greater-than reset");
Equal((ushort)1, worldAfterReset.RandomThreshold,
    "world reset selects Random(N)+1");
Equal((ushort)0, otherBucket.Counter, "unmatched world bucket untouched");

var chainState = new NativeDropControlState(
    NativeDropControlBucketField.ItemName);
chainState.AddUnsafe(NativeDropControlType.Counted,
    Record("M", "same", 1, 1, 10, 1, 0));
chainState.AddUnsafe(NativeDropControlType.Counted,
    Record("M", "same", 1, 1, 20, 1, 0));
chainState.AddUnsafe(NativeDropControlType.Counted,
    Record("M", "same", 1, 1, 30, 1, 0));
var chain = SelectMapCounted(chainState, _ => 0);
Equal(new[] { 10, 30, 20 }, chain.Select(x => x.ItemIndex).ToArray(),
    "same-key native head insertion order");

// The order is fixed by sub_71F46C, the monster VMT slot +0x1FC, which is
// nothing but two sibling calls:
//   0071F47E  E8 F5 0D 00 00  call 0x720278  ; drop control, this class
//   0071F491  E8 8A 05 00 00  call 0x71FA20  ; segments 1-4 + @AfterScatterItems
// sub_720278 and sub_71FA20 each have exactly one E8 caller in the whole image
// and zero dword references, so this single site settles it and the controlled
// drop takes the earlier draw off the shared stream.  This assertion previously
// read ordinary-first on the strength of "controlled = sub_71FA20 segment 3",
// an attribution the record layout disproves: segment 3 queries singleton
// [0x7D71F4] through sub_752CAC, while this class's records match sub_77C580 /
// sub_77C738 field for field and materialise through sub_72016C.
var sharedRandom = new Queue<int>(new[] { 11, 22 });
var generationOrder = new List<string>();
var gateEvaluatedAfter = -1;
var orderBlocked = NativeDropControlRuntime.RunInNativeOrder(
    () => generationOrder.Add("controlled:" + sharedRandom.Dequeue()),
    () =>
    {
        gateEvaluatedAfter = generationOrder.Count;
        return false;
    },
    () => generationOrder.Add("ordinary:" + sharedRandom.Dequeue()));
Equal(new[] { "controlled:11", "ordinary:22" },
    generationOrder.ToArray(), "shared RNG generation order");
Equal(false, orderBlocked, "ordinary gate result reaches the caller");
// 0x71FA6C arms the sentinel inside sub_71FA20, i.e. after 0x720278 returned.
Equal(1, gateEvaluatedAfter, "ordinary gate evaluated after the controlled arm");

// The three sub_71FA20 exits all jump to 0x720092, its own frame exit, so they
// cannot reach back into the sibling that already returned at 0x71F47E.
var blockedOrder = new List<string>();
Equal(true, NativeDropControlRuntime.RunInNativeOrder(
        () => blockedOrder.Add("controlled"),
        () => true,
        () => blockedOrder.Add("ordinary")),
    "blocked ordinary gate reported to the caller");
Equal(new[] { "controlled" }, blockedOrder.ToArray(),
    "sub_71FA20 gates do not cover sub_720278");

var failedCreates = 0;
var failedPlacements = 0;
var failedRange = 0;
var failedRandomCalls = 0;
var failedStdItem = new GoodItem { StdMode = 1, DuraMax = 1000 };
NativeDropControlRuntime.Materialize(
    new NativeDropControlPending("normal", 7, 3), null,
    _ =>
    {
        failedCreates++;
        return (failedStdItem, Item(7, 1000));
    },
    range =>
    {
        Equal(80, range, "durability random range");
        failedRandomCalls++;
        return 30;
    },
    (item, range, dieDrop, creator, dropCreator) =>
    {
        failedPlacements++;
        failedRange = range;
        Equal((ushort)500, item.Dura, "virtual +0x28 durability init");
        Equal(true, dieDrop, "controlled drop death flag");
        Equal<TBaseObject>(null, creator, "test item creator");
        Equal<TBaseObject>(null, dropCreator, "test drop creator");
        return false;
    }, null, null);
Equal(3, failedCreates, "placement failure consumes all quantity");
Equal(3, failedPlacements, "placement attempted for every quantity");
Equal(3, failedRandomCalls, "failed placement does not roll back init");
// 本测试跑的是 NativeDropControlRuntime.Materialize，即**掉落控制腿**，其原生本体
// 是 sub_720278 而非 sub_71FA20 段3。两条腿半径不同，实测字节：
//   0x720213  B9 04 00 00 00  mov ecx,4   ; sub_720278  掉落控制  -> call 0x7688A0
//   0x71FF3D  B9 03 00 00 00  mov ecx,3   ; sub_71FA20 段3 世界掉落 -> call 0x7688A0
// ECX 是 DropItemDown 的第三个寄存器参（0x7688B4 `mov ebx,ecx` 收）。
// 段3 的 3 已由 NativeWorldScatter 及 NativeWorldScatterCheck 各自守着，
// 不要拿它来钉这条腿 —— 那正是 f3354457 把 ScatterRange 从 4 改成 3 的误归属根源。
Equal(4, failedRange, "native fixed scatter range");

var stackPlaced = new List<TUserItem>();
var stackLog = new List<KeyValuePair<string, string>>();
var stackRandomCalls = 0;
var stackStdItem = new GoodItem { StdMode = 7, DuraMax = 1000 };
NativeDropControlRuntime.Materialize(
    new NativeDropControlPending("stack", 8, 9), stackLog,
    _ => (stackStdItem, Item(8, 1000)), _ =>
    {
        stackRandomCalls++;
        return 0;
    },
    (item, range, dieDrop, creator, dropCreator) =>
    {
        stackPlaced.Add(item);
        return true;
    }, null, null);
Equal(9, stackPlaced.Count,
    "StdMode 7 charm emits one ordinary object per quantity");
Equal(9, stackRandomCalls,
    "StdMode 7 charm uses the ordinary durability roll per object");
Equal(9, stackPlaced.Count(item => item.Dura == 200),
    "StdMode 7 charm durability uses the ordinary 20-percent roll");
Equal(9, stackLog.Count,
    "StdMode 7 charm reports one quantity per emitted object");
Require(stackLog.All(entry => entry.Value == "1"),
    "StdMode 7 charm log quantities are all one");

var pileRandomCalls = 0;
var pileDurabilities = new List<ushort>();
var pileStdItem = new GoodItem { StdMode = 154, DuraMax = 777 };
NativeDropControlRuntime.Materialize(
    new NativeDropControlPending("pile", 9, 2), null,
    _ => (pileStdItem, Item(9, 777)),
    _ =>
    {
        pileRandomCalls++;
        return 0;
    },
    (item, range, dieDrop, creator, dropCreator) =>
    {
        pileDurabilities.Add(item.Dura);
        return true;
    }, null, null);
Equal(0, pileRandomCalls, "pile virtual +0x28 is no-op");
Equal(new ushort[] { 2 }, pileDurabilities.ToArray(),
    "pile writes the remaining quantity into Dura once");

Console.WriteLine(
    "NativeDropControlRuntimeCheck PASS timed-uint-rollover " +
    "map-equality-reset world-greater-reset bucket-isolation chain=A,C,B " +
    "rng-order=controlled,ordinary gate-scope=ordinary-only " +
    "scatter-range=4 failure-lossy=true " +
    "type7-charm-per-item=true pile-quantity-dura=true");

static TUserItem Item(ushort index, ushort duraMax)
{
    return new TUserItem
    {
        wIndex = index,
        MakeIndex = index,
        Dura = duraMax,
        DuraMax = duraMax
    };
}

static IReadOnlyList<NativeDropControlPending> SelectMapCounted(
    NativeDropControlState state, Func<int, int> random)
{
    return NativeDropControlRuntime.SelectMap(state,
        NativeDropControlType.Counted, 0, random);
}

static NativeDropControlState State(NativeDropControlBucketField bucketField,
    params NativeDropControlRecord[] records)
{
    var state = new NativeDropControlState(bucketField);
    foreach (var record in records)
        state.AddUnsafe(NativeDropControlType.Counted, record);
    if (records.Length == 1 && records[0].ItemName == "timed")
    {
        state.Clear();
        state.AddUnsafe(NativeDropControlType.Timed, records[0]);
    }
    return state;
}

static NativeDropControlRecord Record(string monsterName, string itemName,
    ushort quantity, int periodOrRange, int itemIndex,
    ushort randomThreshold, int tick)
{
    return new NativeDropControlRecord(
        HUtil32.GbkEncoding.GetBytes(monsterName),
        HUtil32.GbkEncoding.GetBytes(itemName), quantity, periodOrRange,
        itemIndex, randomThreshold, tick);
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

static void Require(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
}

static string Format<T>(T value)
{
    return value is System.Collections.IEnumerable sequence and not string
        ? string.Join(",", sequence.Cast<object>())
        : value?.ToString() ?? "<null>";
}
