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

// sub_71FA20 runs the monster's own table (segment 2, 0x71FCFF-0x71FEA1) before
// the controlled world drop (segment 3, head 0x71FEA7).  0x71FD0E `jl 0x71FEA7`
// is the empty-table shortcut and names segment 3 as segment 2's successor, so
// the ordinary drop takes the earlier draw off the shared stream.
var sharedRandom = new Queue<int>(new[] { 11, 22 });
var generationOrder = new List<string>();
NativeDropControlRuntime.RunInNativeOrder(
    () => generationOrder.Add("ordinary:" + sharedRandom.Dequeue()),
    () => generationOrder.Add("controlled:" + sharedRandom.Dequeue()));
Equal(new[] { "ordinary:11", "controlled:22" },
    generationOrder.ToArray(), "shared RNG generation order");

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
// 0x71FF3D  B9 03 00 00 00  mov ecx,3  -> DropItemDown's ItemRange (ECX is the
// third register param; 0x7688B4 `mov ebx,ecx` receives it).  Segment 3 of
// sub_71FA20 hardcodes a radius of 3, not 4.
Equal(3, failedRange, "native fixed scatter range");

var stackPlaced = new List<TUserItem>();
var stackLog = new List<KeyValuePair<string, string>>();
var stackStdItem = new GoodItem { StdMode = 7, DuraMax = 1000 };
NativeDropControlRuntime.Materialize(
    new NativeDropControlPending("stack", 8, 9), stackLog,
    _ => (stackStdItem, Item(8, 1000)), _ => 0,
    (item, range, dieDrop, creator, dropCreator) =>
    {
        stackPlaced.Add(item);
        return true;
    }, null, null);
Equal(1, stackPlaced.Count, "StdMode 7 emits one object");
Equal((ushort)200, stackPlaced[0].Dura,
    "StdMode 7 quantity is overwritten by virtual +0x28");
Equal("9", stackLog.Single().Value, "StdMode 7 reports source quantity");

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
Equal(new ushort[] { 777, 777 }, pileDurabilities.ToArray(),
    "pile retains constructor durability");

Console.WriteLine(
    "NativeDropControlRuntimeCheck PASS timed-uint-rollover " +
    "map-equality-reset world-greater-reset bucket-isolation chain=A,C,B " +
    "rng-order=ordinary,controlled scatter-range=3 failure-lossy=true " +
    "type7-final-dura=virtual pile-init=noop");

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

static string Format<T>(T value)
{
    return value is System.Collections.IEnumerable sequence and not string
        ? string.Join(",", sequence.Cast<object>())
        : value?.ToString() ?? "<null>";
}
