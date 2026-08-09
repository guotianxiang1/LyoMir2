using GameSvr;
using SystemModule;

AssertExactMapping(
    NativeDynamicRoomFlagMapper.CreateMapFlag(new[]
    {
        "FIGHT",
        "DARK",
        "NORECALL",
        "NOPOSITIONMOVE",
        "NORANDOMMOVE",
        "NORECONNECT(DM002~1)"
    }),
    fight: true,
    dark: true,
    noRecall: true,
    noPositionMove: true,
    noRandomMove: true,
    noReconnect: true,
    reconnectMap: "DM002~1",
    "all requested dynamic room flags");

AssertExactMapping(
    NativeDynamicRoomFlagMapper.CreateMapFlag(new[]
    {
        " fight ",
        "dark",
        "norecall",
        "nopositionmove",
        "norandommove",
        "noreconnect(0122~1)"
    }),
    fight: true,
    dark: true,
    noRecall: true,
    noPositionMove: true,
    noRandomMove: true,
    noReconnect: true,
    reconnectMap: "0122~1",
    "case-insensitive trimmed flags");

AssertExactMapping(
    NativeDynamicRoomFlagMapper.CreateMapFlag(new[] { "UNKNOWN", string.Empty }),
    fight: false,
    dark: false,
    noRecall: false,
    noPositionMove: false,
    noRandomMove: false,
    noReconnect: false,
    reconnectMap: null,
    "unknown flags remain ignored");

foreach (var flag in new[]
{
    "FIGHT",
    "DARK",
    "NORECALL",
    "NOPOSITIONMOVE",
    "NORANDOMMOVE",
    "NORECONNECT(3)"
})
{
    Assert(NativeDynamicRoomFlagMapper.CanMap(flag), flag + " was not recognized");
}

Assert(!NativeDynamicRoomFlagMapper.CanMap("SAFE"),
    "mapper accepted a non-PsDynNpc dynamic-room flag");

var dynNpcFile = FindDynNpcFile();
var productionChecked = false;
if (dynNpcFile != null)
{
    Assert(NativeDynamicRoomDefinitionLoader.TryLoad(
            dynNpcFile, out var definitions, out var errors),
        "PsDynNpc.txt was rejected: " + string.Join(" | ", errors));

    foreach (var definition in definitions)
    {
        var unknownFlags = definition.Flags
            .Where(flag => !NativeDynamicRoomFlagMapper.CanMap(flag))
            .ToArray();
        Assert(unknownFlags.Length == 0,
            definition.RoomName + " has unmapped dynamic flags: "
            + string.Join(", ", unknownFlags));
    }

    var sky = RequireRoom(definitions, "Sky");
    AssertExactMapping(
        NativeDynamicRoomFlagMapper.CreateMapFlag(sky.Flags),
        fight: true,
        dark: false,
        noRecall: true,
        noPositionMove: false,
        noRandomMove: false,
        noReconnect: true,
        reconnectMap: "0122~1",
        "Sky production flags");

    var duckLight = RequireRoom(definitions, "DuckLight");
    AssertExactMapping(
        NativeDynamicRoomFlagMapper.CreateMapFlag(duckLight.Flags),
        fight: true,
        dark: true,
        noRecall: true,
        noPositionMove: true,
        noRandomMove: true,
        noReconnect: true,
        reconnectMap: "DM002~1",
        "DuckLight production flags");

    productionChecked = true;
}

Console.WriteLine("DynRoomFlagMapperCheck PASS fixture=ok "
    + "psdynnpc=" + (productionChecked ? dynNpcFile : "not-found"));

static string FindDynNpcFile()
{
    var configured = Environment.GetEnvironmentVariable("LYOMIR_DYNROOM_PSDYNNPC");
    var candidates = new[]
    {
        configured,
        Path.Combine(
            Environment.GetEnvironmentVariable("LYOMIR_PRODUCTION_ENVIR")
                ?? @"D:\lyom2Release\mud2.0\Mir200\Envir",
            "PsDynNpc.txt"),
        @"D:\loym2\staging\pas-include-context-20260714\Envir\PsDynNpc.txt"
    };

    return candidates.FirstOrDefault(path =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path));
}

static NativeDynamicRoomDefinition RequireRoom(
    IEnumerable<NativeDynamicRoomDefinition> definitions, string name)
{
    var room = definitions.FirstOrDefault(definition =>
        definition.RoomName == name);
    Assert(room != null, "room not found: " + name);
    return room;
}

static void AssertExactMapping(
    TMapFlag flag,
    bool fight,
    bool dark,
    bool noRecall,
    bool noPositionMove,
    bool noRandomMove,
    bool noReconnect,
    string reconnectMap,
    string context)
{
    Equal(fight, flag.boFightZone, context + " FIGHT");
    Equal(dark, flag.boDarkness, context + " DARK");
    Equal(noRecall, flag.boNORECALL, context + " NORECALL");
    Equal(noPositionMove, flag.boNOPOSITIONMOVE,
        context + " NOPOSITIONMOVE");
    Equal(noRandomMove, flag.boNORANDOMMOVE, context + " NORANDOMMOVE");
    Equal(noReconnect, flag.boNORECONNECT, context + " NORECONNECT");
    Equal(reconnectMap, flag.sNoReConnectMap, context + " reconnect target");

    Equal(false, flag.boSAFE, context + " SAFE");
    Equal(false, flag.boDayLight, context + " DAY");
    Equal(false, flag.boFight3Zone, context + " FIGHT3");
    Equal(false, flag.boNOHORSE, context + " NOHORSE");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
