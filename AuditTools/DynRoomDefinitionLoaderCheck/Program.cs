using GameSvr;

var envirRoot = Environment.GetEnvironmentVariable("LYOMIR_PRODUCTION_ENVIR")
    ?? @"D:\lyom2Release\mud2.0\Mir200\Envir";
var mapRoot = Environment.GetEnvironmentVariable("LYOMIR_PRODUCTION_MAP")
    ?? @"D:\lyom2Release\mud2.0\Mir200\Map";
var dynNpcFile = Path.Combine(envirRoot, "PsDynNpc.txt");

Assert(NativeDynamicRoomDefinitionLoader.TryLoad(dynNpcFile,
        out var definitions, out var errors),
    "production PsDynNpc.txt was rejected: " + string.Join(" | ", errors));

Equal(22, definitions.Count, "production dynamic room definition count");
Equal(22, definitions.Sum(room => room.ConfiguredNpcs.Count),
    "production configured dynamic NPC total");
Equal(7, definitions.Max(room => room.RoomType), "maximum room type");
Equal(1, definitions.Min(room => room.RoomType), "minimum room type");
Assert(definitions.Select(room => room.RoomName).Distinct(StringComparer.Ordinal)
        .Count() == definitions.Count,
    "production dynamic room names are not ordinal-unique");

var sky = RequireRoom(definitions, "Sky");
Equal("1", sky.RawColumn2, "Sky opaque column-2 metadata");
Equal(1, sky.RoomType, "Sky room type");
Equal("天关", sky.Description, "Sky GBK description");
Equal("D515", sky.MapFileName, "Sky map file");
Equal("10", sky.RawRoomCount, "Sky raw room-count metadata");
Equal("2", sky.RawBalanceCount, "Sky raw balance-count metadata");
var skyNpc = sky.ConfiguredNpcs.Single();
Equal("天关统领", skyNpc.ScriptName, "Sky configured NPC script");
Equal(20, skyNpc.X, "Sky configured NPC X");
Equal(23, skyNpc.Y, "Sky configured NPC Y");
Assert(sky.Flags.Contains("FIGHT", StringComparer.Ordinal)
       && sky.Flags.Contains("NORECALL", StringComparer.Ordinal)
       && sky.Flags.Contains("NORECONNECT(0122~1)", StringComparer.Ordinal),
    "Sky flags were not preserved");

var duckLight = RequireRoom(definitions, "DuckLight");
Equal(4, duckLight.RoomType, "DuckLight room type");
Equal("DM002~1", ExtractReconnect(duckLight), "DuckLight reconnect target");
Assert(duckLight.Flags.Contains("NORANDOMMOVE", StringComparer.Ordinal),
    "DuckLight NORANDOMMOVE flag missing");

var drinkwater = RequireRoom(definitions, "Drinkwater");
Equal(7, drinkwater.RoomType, "Drinkwater room type");
Equal("神秘护卫", drinkwater.ConfiguredNpcs.Single().NpcName,
    "Drinkwater configured NPC GBK name");

var mapErrors = NativeDynamicRoomDefinitionLoader.ValidateMapFiles(
    definitions, mapRoot);
Assert(mapErrors.Count == 0, "production dynamic room maps missing: "
    + string.Join(" | ", mapErrors));

Assert(NativeDynamicRoomDefinitionLoader.TryParse("""
    Empty 1 5 无配置NPC D411 2 2 [DARK]
    """, out var emptyDefinitions, out errors)
    && emptyDefinitions.Single().ConfiguredNpcs.Count == 0,
    "room without configured NPCs was rejected");

Assert(NativeDynamicRoomDefinitionLoader.TryParse("""
    Metadata not-a-server 5 原始元数据 D411 not-a-number -7 [DARK]
    """, out var metadataDefinitions, out errors),
    "opaque room-count metadata was rejected: " + string.Join(" | ", errors));
var metadata = metadataDefinitions.Single();
Equal("not-a-server", metadata.RawColumn2,
    "column-2 metadata was not preserved verbatim");
Equal("not-a-number", metadata.RawRoomCount,
    "room-count metadata was not preserved verbatim");
Equal("-7", metadata.RawBalanceCount,
    "balance-count metadata was not preserved verbatim");

Assert(NativeDynamicRoomDefinitionLoader.TryParse("""
    Multi 1 5 多配置NPC D411 2 2 [DARK]
    [ScriptOne 1 2 DisplayOne 3 4]
    [ScriptTwo 5 6 DisplayTwo 7 8]
    """, out var multiDefinitions, out errors)
    && multiDefinitions.Single().ConfiguredNpcs.Count == 2,
    "multiple configured NPC rows were rejected");
var multiNpcs = multiDefinitions.Single().ConfiguredNpcs;
Equal("ScriptOne", multiNpcs[0].ScriptName,
    "configured NPC script field order");
Equal("DisplayOne", multiNpcs[0].NpcName,
    "configured NPC display field order");
Equal(7, multiNpcs[1].Face, "second configured NPC direction");
Equal(8, multiNpcs[1].Body, "second configured NPC appearance");

Assert(!NativeDynamicRoomDefinitionLoader.TryParse("""
    [ScriptOnly 1 2 DisplayOnly 3 4]
    """, out _, out errors)
    && errors.Any(error => error.Contains("configured NPC without room",
        StringComparison.Ordinal)),
    "configured NPC without room was accepted");

Assert(!NativeDynamicRoomDefinitionLoader.TryParse("""
    BadNpc 1 5 错误配置 D411 2 2 [DARK]
    [ScriptOnly 1 2]
    """, out _, out errors)
    && errors.Any(error => error.Contains("configured NPC needs 6 fields",
        StringComparison.Ordinal)),
    "short configured NPC row was accepted");

Assert(!NativeDynamicRoomDefinitionLoader.TryParse("""
    Dup 1 5 一 D411 2 2 [DARK]
    [甲 1 1 甲 0 15]
    Dup 1 5 二 D412 2 2 [DARK]
    [乙 1 1 乙 0 15]
    """, out _, out errors)
    && errors.Any(error => error.Contains("duplicate room",
        StringComparison.Ordinal)),
    "duplicate room name was accepted");

foreach (var roomType in new[] { 100, 101, 110, 0, -2, 102 })
{
    Assert(NativeDynamicRoomDefinitionLoader.TryParse(
            $"Type{roomType} opaque {roomType} 类型 D411 2 2 [DARK]",
            out var arbitraryTypeDefinitions, out errors),
        $"native room type {roomType} was rejected: " + string.Join(" | ", errors));
    Equal(roomType, arbitraryTypeDefinitions.Single().RoomType,
        $"native room type {roomType}");
}

foreach (var invalidType in new[] { "-1", "not-a-number" })
{
    Assert(!NativeDynamicRoomDefinitionLoader.TryParse(
            $"Bad opaque {invalidType} 错 D411 2 2 [DARK]",
            out _, out errors)
        && errors.Any(error => error.Contains("room type field is invalid",
            StringComparison.Ordinal)),
        $"invalid room type {invalidType} was accepted");
}

Console.WriteLine("DynRoomDefinitionLoaderCheck PASS definitions=22 metadata=raw "
    + "types=all-int-except--1 gbk=ok maps=ok no-runtime-registration");

static NativeDynamicRoomDefinition RequireRoom(
    IEnumerable<NativeDynamicRoomDefinition> definitions, string name)
{
    var room = definitions.FirstOrDefault(definition =>
        definition.RoomName == name);
    Assert(room != null, "room not found: " + name);
    return room;
}

static string ExtractReconnect(NativeDynamicRoomDefinition definition)
{
    const string prefix = "NORECONNECT(";
    var flag = definition.Flags.FirstOrDefault(value =>
        value.StartsWith(prefix, StringComparison.Ordinal)
        && value.EndsWith(")", StringComparison.Ordinal));
    return flag == null
        ? string.Empty
        : flag.Substring(prefix.Length, flag.Length - prefix.Length - 1);
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
