using GameSvr;
using GameSvr.Services;
using SystemModule;

CheckExactOrderAndBranches();
CheckEmptyNonNullTable();
CheckUncheckedArithmetic();
CheckWireIndexAndSignedCount();
CheckGuards();

Console.WriteLine("PASS NativeFieldHeroOwnDropCoreCheck " +
                  "order=one-roll-per-record gold=wire0 " +
                  "item=direct-place failure=release success=post-place");

static void CheckExactOrderAndBranches()
{
    var gold = Item("Gold", 0);
    var success = Item("Success", 1);
    var rejected = Item("Rejected", 2);
    var unavailable = Item("Unavailable", 3);
    var miss = Item("Miss", 4);
    var drops = Parse(new[]
    {
        "5/10 Gold 7",
        "5/20 Success 999",
        "5/30 Rejected -77",
        "5/40 Unavailable 123",
        "1/50 Miss 456"
    }, gold, success, rejected, unavailable, miss);
    var events = new List<string>();
    var randomResults = new Queue<int>(new[] { 4, 6, 0, 0, 0, 1 });
    var serial = 0;

    var result = NativeFieldHeroOwnDropCore.Consume(drops, 2, 100,
        value =>
        {
            events.Add("transform:" + value);
            return value + 3;
        },
        maximum =>
        {
            events.Add("random:" + maximum);
            return randomResults.Dequeue();
        },
        standardItem =>
        {
            events.Add("create:" + standardItem.Name);
            var expected = standardItem.Name switch
            {
                "Success" => success,
                "Rejected" => rejected,
                "Unavailable" => unavailable,
                _ => throw new InvalidOperationException(
                    "unexpected direct standard item")
            };
            Check(ReferenceEquals(expected, standardItem),
                "creator receives the bound GoodItem reference");
            if (ReferenceEquals(standardItem, unavailable)) return null;
            return new TUserItem { MakeIndex = ++serial };
        },
        (item, standardItem) =>
            events.Add($"initialize:{standardItem.Name}:{item.MakeIndex}"),
        item =>
        {
            events.Add("place:" + item.MakeIndex);
            return item.MakeIndex == 1;
        },
        item => events.Add("release:" + item.MakeIndex),
        (item, standardItem) =>
            events.Add($"success:{standardItem.Name}:{item.MakeIndex}"));

    Equal(109, result, "gold accumulation");
    Equal(0, randomResults.Count, "all expected random results consumed");
    Sequence(new[]
    {
        "transform:20", "random:23", "random:7",
        "transform:40", "random:43", "create:Success",
        "initialize:Success:1", "place:1", "success:Success:1",
        "transform:60", "random:63", "create:Rejected",
        "initialize:Rejected:2", "place:2", "release:2",
        "transform:80", "random:83", "create:Unavailable",
        "transform:100", "random:103"
    }, events, "native per-record operation order");
    Check(!events.Contains("random:999") && !events.Contains("random:-77"),
        "non-gold Count never consumes RNG");
    Check(!events.Any(value => value.Contains("Miss")),
        "missed record performs no item operation");
}

static void CheckEmptyNonNullTable()
{
    var calls = 0;
    var result = NativeFieldHeroOwnDropCore.Consume(
        Array.Empty<NativeFieldHeroRuntimeDropBinding>(), 1, -7,
        value => { calls++; return value; },
        value => { calls++; return value; },
        _ => { calls++; return new TUserItem(); },
        (_, _) => calls++,
        _ => { calls++; return true; },
        _ => calls++,
        (_, _) => calls++);

    Equal(-7, result, "empty table preserves existing gold");
    Equal(0, calls, "empty non-null table performs no operation");
}

static void CheckUncheckedArithmetic()
{
    var gold = Item("Gold", 0);
    var drops = Parse(new[]
    {
        "$80000000/$7FFFFFFF Gold $7FFFFFFF"
    }, gold);
    var randomArguments = new List<int>();
    var randomResults = new Queue<int>(new[] { 0, int.MaxValue });

    var result = NativeFieldHeroOwnDropCore.Consume(drops, 2, 2,
        value => value,
        maximum =>
        {
            randomArguments.Add(maximum);
            return randomResults.Dequeue();
        },
        _ => throw new InvalidOperationException("gold must not create"),
        (_, _) => throw new InvalidOperationException("gold must not init"),
        _ => throw new InvalidOperationException("gold must not place"),
        _ => throw new InvalidOperationException("gold must not release"),
        (_, _) => throw new InvalidOperationException("gold must not log"));

    Sequence(new[] { -2, int.MaxValue }, randomArguments,
        "unchecked denominator then gold Count RNG");
    Equal(unchecked(2 + int.MaxValue + int.MaxValue / 2), result,
        "unchecked gold accumulator");
}

static void CheckWireIndexAndSignedCount()
{
    var unnamedGold = Item("NotNamedGold", 0);
    var namedNonGold = Item("Gold", 1);
    var drops = Parse(new[]
    {
        "1/1 NotNamedGold -5",
        "1/1 Gold 777"
    }, unnamedGold, namedNonGold);
    var randomArguments = new List<int>();
    var randomResults = new Queue<int>(new[] { 0, -4, 0 });
    GoodItem createdFrom = null;

    var result = NativeFieldHeroOwnDropCore.Consume(drops, 1, 10,
        value => value,
        maximum =>
        {
            randomArguments.Add(maximum);
            return randomResults.Dequeue();
        },
        standardItem =>
        {
            createdFrom = standardItem;
            return new TUserItem();
        },
        (_, _) => { },
        _ => true,
        _ => throw new InvalidOperationException("placement succeeds"),
        (_, _) => { });

    Equal(4, result,
        "negative Count division truncates toward zero, not arithmetic shift");
    Sequence(new[] { 1, -5, 1 }, randomArguments,
        "wire-index branch RNG order");
    Check(ReferenceEquals(namedNonGold, createdFrom),
        "wire1 item named Gold remains a direct non-gold item");
}

static void CheckGuards()
{
    var item = Item("Blade", 1);
    var valid = Parse(new[] { "1/2 Blade 1" }, item);
    Func<int, int> identity = value => value;
    Func<GoodItem, TUserItem> create = _ => new TUserItem();
    Action<TUserItem, GoodItem> initialize = (_, _) => { };
    Func<TUserItem, bool> place = _ => true;
    Action<TUserItem> release = _ => { };
    Action<TUserItem, GoodItem> record = (_, _) => { };

    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroOwnDropCore.Consume(null, 1, 0, identity,
            identity, create, initialize, place, release, record),
        "null table rejected so absence cannot masquerade as empty");
    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroOwnDropCore.Consume(valid, 1, 0, null,
            identity, create, initialize, place, release, record),
        "null denominator transform rejected");
    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroOwnDropCore.Consume(valid, 1, 0, identity,
            null, create, initialize, place, release, record),
        "null RNG rejected");
    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroOwnDropCore.Consume(valid, 1, 0, identity,
            identity, null, initialize, place, release, record),
        "null creator rejected");
    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroOwnDropCore.Consume(valid, 1, 0, identity,
            identity, create, null, place, release, record),
        "null initializer rejected");
    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroOwnDropCore.Consume(valid, 1, 0, identity,
            identity, create, initialize, null, release, record),
        "null placer rejected");
    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroOwnDropCore.Consume(valid, 1, 0, identity,
            identity, create, initialize, place, null, record),
        "null releaser rejected");
    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroOwnDropCore.Consume(valid, 1, 0, identity,
            identity, create, initialize, place, release, null),
        "null success recorder rejected");

    var calls = 0;
    var withNull = new NativeFieldHeroRuntimeDropBinding[]
        { null, valid[0] };
    var afterNull = NativeFieldHeroOwnDropCore.Consume(withNull, 1, 0,
        value => { calls++; return value; },
        value => { calls++; return 0; },
        standardItem => { calls++; return new TUserItem(); },
        (_, _) => calls++,
        _ => { calls++; return true; },
        _ => calls++,
        (_, _) => calls++);
    Equal(0, afterNull, "null record skip leaves gold unchanged");
    Equal(6, calls,
        "null record consumes nothing and the following record still runs");
}

static NativeFieldHeroRuntimeDropBinding[] Parse(
    IReadOnlyList<string> lines, params GoodItem[] items)
{
    var byName = items.ToDictionary(item => item.Name,
        StringComparer.Ordinal);
    return NativeFieldHeroMonItemsParser.Parse(lines,
        name => byName.TryGetValue(name, out var item) ? item : null);
}

static GoodItem Item(string name, ushort wireIndex) => new()
{
    Name = name,
    NativeWireIndex = wireIndex
};

static void Sequence<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string description)
{
    Equal(expected.Count, actual.Count, description + " count");
    for (var index = 0; index < expected.Count; index++)
        Equal(expected[index], actual[index],
            description + " index " + index);
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{description}: expected={expected}, actual={actual}");
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
