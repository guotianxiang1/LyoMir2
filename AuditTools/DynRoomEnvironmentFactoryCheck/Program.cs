using System.Reflection;
using GameSvr;

PrepareRuntimeConfig();

var envirRoot = Environment.GetEnvironmentVariable("LYOMIR_PRODUCTION_ENVIR")
    ?? @"D:\lyom2Release\mud2.0\Mir200\Envir";
var mapRoot = Environment.GetEnvironmentVariable("LYOMIR_PRODUCTION_MAP")
    ?? @"D:\lyom2Release\mud2.0\Mir200\Map";

M2Share.g_Config = new GameSvrConfig();
M2Share.sConfigPath = Path.GetDirectoryName(envirRoot) ?? string.Empty;
M2Share.DynamicRoomManager = new NativeDynamicRoomManager();
const int currentServerIndex = 0;

Assert(NativeDynamicRoomDefinitionLoader.TryLoad(
        Path.Combine(envirRoot, "PsDynNpc.txt"),
        out var definitions, out var errors),
    "production PsDynNpc.txt was rejected: " + string.Join(" | ", errors));

var sky = definitions.Single(definition => definition.RoomName == "Sky");
Equal("10", sky.RawRoomCount, "Sky raw room-count metadata");
Assert(NativeDynamicRoomEnvironmentFactory.TryCreateDormantEnvironment(
        sky, mapRoot, currentServerIndex, out var firstSky, out errors),
    "first Sky environment was rejected: " + string.Join(" | ", errors));
Assert(NativeDynamicRoomEnvironmentFactory.TryCreateDormantEnvironment(
        sky, mapRoot, currentServerIndex, out var secondSky, out errors),
    "second Sky environment was rejected: " + string.Join(" | ", errors));
Assert(!ReferenceEquals(firstSky, secondSky),
    "explicit factory calls returned the same environment");
Equal("D515", firstSky.m_sMapFileName, "Sky map file metadata");
Equal("天关", firstSky.sMapDesc, "Sky description metadata");
Equal("1", sky.RawColumn2, "Sky opaque column-2 metadata");
Equal(currentServerIndex, firstSky.nServerIndex, "Sky current server index");
Equal(true, firstSky.IsDynamicRoom, "Sky dynamic flag");
Equal("Sky", firstSky.DynamicRoomName, "Sky dynamic name");
Equal(-1, firstSky.DynamicRoomIndex, "Sky dormant dynamic index");
Equal(true, firstSky.Flag.boFightZone, "Sky fight flag");
Equal(true, firstSky.Flag.boNORECALL, "Sky no-recall flag");
Equal(true, firstSky.Flag.boNORECONNECT, "Sky reconnect flag");
Equal("0122~1", firstSky.Flag.sNoReConnectMap, "Sky reconnect target");
Assert(firstSky.wWidth > 1 && firstSky.wHeight > 1,
    "Sky map dimensions were not loaded");
Assert(!ReferenceEquals(ReadPrivateField(firstSky, "MapCellAttributes"),
        ReadPrivateField(secondSky, "MapCellAttributes")),
    "Sky map attributes are shared between instances");
Assert(!ReferenceEquals(ReadPrivateField(firstSky, "MapCellObjectLists"),
        ReadPrivateField(secondSky, "MapCellObjectLists")),
    "Sky map object lists are shared between instances");

var duckLightDefinition = definitions.Single(definition =>
    definition.RoomName == "DuckLight");
Assert(NativeDynamicRoomEnvironmentFactory.TryCreateDormantEnvironment(
        duckLightDefinition, mapRoot, currentServerIndex, out var duckLight, out errors),
    "DuckLight environment was rejected: " + string.Join(" | ", errors));
Equal(true, duckLight.Flag.boDarkness, "DuckLight dark flag");
Equal(true, duckLight.Flag.boNOPOSITIONMOVE, "DuckLight no position move flag");
Equal(true, duckLight.Flag.boNORANDOMMOVE, "DuckLight no random move flag");
Equal("DM002~1", duckLight.Flag.sNoReConnectMap, "DuckLight reconnect target");

foreach (var defaultRoomType in new[] { 0, -2, 102 })
{
    var definition = NewDefinition($"DefaultType{defaultRoomType}",
        defaultRoomType, sky.MapFileName);
    Assert(NativeDynamicRoomEnvironmentFactory.TryCreateDormantEnvironment(
            definition, mapRoot, currentServerIndex,
            out var defaultEnvironment, out errors),
        $"default dynamic room type {defaultRoomType} was rejected: "
        + string.Join(" | ", errors));
    Equal(currentServerIndex, defaultEnvironment.nServerIndex,
        $"default dynamic room type {defaultRoomType} server index");
}

foreach (var unsupportedRoomType in new[] { 100, 101, 110 })
{
    var definition = NewDefinition($"SpecialType{unsupportedRoomType}",
        unsupportedRoomType, sky.MapFileName);
    Assert(!NativeDynamicRoomEnvironmentFactory.TryCreateDormantEnvironment(
            definition, mapRoot, currentServerIndex, out _, out errors)
        && errors.Any(error => error.Contains("unsupported",
            StringComparison.Ordinal)),
        $"unsupported special dynamic room type {unsupportedRoomType} was materialized");
}

Assert(!NativeDynamicRoomEnvironmentFactory.TryCreateDormantEnvironment(
        NewDefinition("InvalidType", -1, sky.MapFileName), mapRoot,
        currentServerIndex, out _, out errors)
    && errors.Any(error => error.Contains("type -1 is invalid",
        StringComparison.Ordinal)),
    "programmatic room type -1 was materialized");

Assert(!M2Share.DynamicRoomManager.TryReserveIdleRoom("Sky", null, out _),
    "dormant factory registered rooms into NativeDynamicRoomManager");

var missingMap = new[]
{
    new NativeDynamicRoomDefinition("MissingRoom", 1, 5, "缺图",
        "NO_SUCH_MAP", 1, 1, Array.Empty<string>(),
        Array.Empty<NativeDynamicRoomConfiguredNpcDefinition>(),
        1)
};
Assert(!NativeDynamicRoomEnvironmentFactory.TryCreateDormantEnvironment(
        missingMap[0], mapRoot, currentServerIndex,
        out var failedEnvironment, out errors)
    && failedEnvironment == null
    && errors.Any(error => error.Contains("map load failed", StringComparison.Ordinal)),
    "missing map file was accepted");

Assert(!NativeDynamicRoomEnvironmentFactory.TryCreateDormantEnvironment(
        sky, Path.Combine(mapRoot, "missing"), currentServerIndex,
        out failedEnvironment, out errors)
    && failedEnvironment == null
    && errors.Any(error => error.Contains("map directory not found",
        StringComparison.Ordinal)),
    "missing map directory was accepted");

Assert(!NativeDynamicRoomEnvironmentFactory.TryCreateDormantEnvironment(
        null, mapRoot, currentServerIndex, out failedEnvironment, out errors)
    && failedEnvironment == null
    && errors.Any(error => error.Contains("definition is null",
        StringComparison.Ordinal)),
    "null definition was accepted");

Assert(!NativeDynamicRoomEnvironmentFactory.TryCreateDormantEnvironment(
        sky, mapRoot, -1, out failedEnvironment, out errors)
    && failedEnvironment == null
    && errors.Any(error => error.Contains("invalid current server index",
        StringComparison.Ordinal)),
    "negative current server index was accepted");

Assert(typeof(NativeDynamicRoomEnvironmentFactory).GetMethod(
        "TryCreateDormantEnvironments", BindingFlags.Public | BindingFlags.Static) == null,
    "bulk dynamic environment factory API is still exposed");

Console.WriteLine("DynRoomEnvironmentFactoryCheck PASS definitions=22 "
    + "single-create=ok current-server=0 independent-cells=ok "
    + "flags=ok manager=unregistered");

static object ReadPrivateField(object instance, string name)
{
    var field = instance.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "private field not found: " + name);
    return field.GetValue(instance);
}

static NativeDynamicRoomDefinition NewDefinition(string roomName,
    int roomType, string mapFileName)
{
    return new NativeDynamicRoomDefinition(roomName, "opaque", roomType,
        "Test", mapFileName, "raw", "raw", Array.Empty<string>(),
        Array.Empty<NativeDynamicRoomConfiguredNpcDefinition>(), 1);
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
