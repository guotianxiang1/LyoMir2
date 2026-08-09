using GameSvr;

var envirRoot = Environment.GetEnvironmentVariable("LYOMIR_PRODUCTION_ENVIR")
    ?? @"D:\lyom2Release\mud2.0\Mir200\Envir";
var idaFile = Environment.GetEnvironmentVariable("LYOMIR_DYNROOM_IDA")
    ?? @"D:\loym2\staging\pas-finish\ida-dynroom-lifecycle.txt";

Assert(NativeDynamicRoomDefinitionLoader.TryLoad(
        Path.Combine(envirRoot, "PsDynNpc.txt"),
        out var definitions, out var errors),
    "production PsDynNpc.txt was rejected: " + string.Join(" | ", errors));
Equal(22, definitions.Count, "definition count");
var skyDefinition = definitions.Single(definition => definition.RoomName == "Sky");
var duckLightDefinition = definitions.Single(definition =>
    definition.RoomName == "DuckLight");
const int currentServerIndex = 0;
var plans = new[]
{
    CreatePlan(skyDefinition, 0, envirRoot, currentServerIndex),
    CreatePlan(skyDefinition, 1, envirRoot, currentServerIndex),
    CreatePlan(duckLightDefinition, 0, envirRoot, currentServerIndex)
};

Equal(3, plans.Length, "explicit materialization plan count");
Equal(3, plans.Select(plan => plan.Owner).Distinct().Count(),
    "unique environment owner count");
Equal(3, plans.Count(plan => plan.Controller.Role == NpcRole.HiddenController),
    "hidden controller count");
Equal(skyDefinition.ConfiguredNpcs.Count * 2
        + duckLightDefinition.ConfiguredNpcs.Count,
    plans.Sum(plan => plan.ConfiguredNpcs.Count),
    "configured NPC count");
Assert(plans.All(plan => !plan.Controller.NativeFlag45C
        && plan.Controller.NativeFlag45D),
    "hidden controller role flags changed");
Assert(plans.SelectMany(plan => plan.ConfiguredNpcs).All(npc =>
        npc.NativeFlag45C && !npc.NativeFlag45D),
    "configured NPC role flags changed");
Assert(plans.GroupBy(plan => plan.Owner)
        .All(group => group.Count() == 1),
    "an NPC plan is shared across environment instances");

var firstSky = plans.Single(plan => plan.Owner.RoomName == "Sky"
    && plan.Owner.InstanceIndex == 0);
var secondSky = plans.Single(plan => plan.Owner.RoomName == "Sky"
    && plan.Owner.InstanceIndex == 1);
Assert(!ReferenceEquals(firstSky.Controller, secondSky.Controller),
    "Sky physical instances share a hidden controller plan");
Assert(!ReferenceEquals(firstSky.ConfiguredNpcs, secondSky.ConfiguredNpcs),
    "Sky physical instances share a configured NPC list");
Assert(!ReferenceEquals(firstSky.ConfiguredNpcs[0], secondSky.ConfiguredNpcs[0]),
    "Sky physical instances share a configured NPC plan");
Equal("1", skyDefinition.RawColumn2, "Sky opaque column-2 metadata");
Equal("2", duckLightDefinition.RawColumn2,
    "DuckLight opaque column-2 metadata");
Assert(plans.All(plan => plan.Owner.RuntimeServerIndex == currentServerIndex),
    "dynamic NPC owner is not the current runtime server");

var hiddenFiles = definitions.Select(definition => Path.Combine(envirRoot,
    "DynRoomScripts", $"DNpc_{definition.RoomName}.pas")).ToList();
var configuredFiles = definitions.SelectMany(definition => definition.ConfiguredNpcs,
    (definition, configuredNpc) => Path.Combine(envirRoot, "DynRoomScripts",
        $"{configuredNpc.ScriptName}-{definition.RoomName}.pas")).ToList();
Equal(10, hiddenFiles.Count(File.Exists), "present room DNpc script count");
Equal(12, hiddenFiles.Count(path => !File.Exists(path)),
    "missing room DNpc script count");
Equal(21, configuredFiles.Count(File.Exists),
    "present configured-NPC script count");
Equal(1, configuredFiles.Count(path => !File.Exists(path)),
    "missing configured-NPC script count");

Equal("Sky", firstSky.Controller.CharacterName, "Sky controller character name");
Equal("D515", firstSky.Controller.MapName, "Sky controller map name");
Assert(firstSky.Controller.ScriptPath.EndsWith("DNpc_Sky.pas",
        StringComparison.OrdinalIgnoreCase),
    "Sky controller script path");
Equal(20, firstSky.ConfiguredNpcs[0].X, "Sky configured NPC X");
Equal(23, firstSky.ConfiguredNpcs[0].Y, "Sky configured NPC Y");
Equal(0, firstSky.ConfiguredNpcs[0].Direction, "Sky configured NPC direction");
Equal(18, firstSky.ConfiguredNpcs[0].Appearance, "Sky configured NPC appearance");
Assert(firstSky.ConfiguredNpcs[0].ScriptPath.EndsWith("-Sky.pas",
        StringComparison.OrdinalIgnoreCase),
    "Sky configured NPC script path");

Assert(File.Exists(idaFile), "IDA lifecycle artifact not found: " + idaFile);
var ida = File.ReadAllText(idaFile);
AssertInOrder(ida,
    "mov     [eax+0A4h], ebx",
    "mov     byte ptr [ebx+45Dh], 1",
    "mov     byte ptr [ebx+45Ch], 0",
    "mov     [ebx+128h], eax",
    "mov     dword ptr [ebx+5E4h], 1");
AssertInOrder(ida,
    "mov     byte ptr [ebx+45Dh], 0",
    "mov     byte ptr [ebx+45Ch], 1",
    "push    offset aDynroomscripts_1",
    "mov     [ebx+12Ch], eax",
    "mov     [ebx+130h], eax",
    "mov     [ebx+154h], al",
    "call    sub_76C310",
    "call    sub_5FD374",
    "call    sub_64A834");
AssertInOrder(ida,
    "mov     ebx, [eax+0A4h]",
    "call    sub_768060",
    "call    sub_5FE1C4",
    "mov     eax, [eax+0E0h]",
    "call    sub_424D4C");

Console.WriteLine("DynRoomNpcOwnershipCheck PASS definitions=22 "
    + "explicit-instances=3 per-instance-ownership=ok "
    + "current-server=0 scripts=hidden:10/22 configured:21/22 ida-order=ok");

static EnvironmentNpcPlan CreatePlan(NativeDynamicRoomDefinition definition,
    int instanceIndex, string envirRoot, int currentServerIndex)
{
    var owner = new EnvironmentOwner(currentServerIndex,
        definition.RoomName, instanceIndex);
    return new EnvironmentNpcPlan(owner,
        new NpcPlan(
            NpcRole.HiddenController,
            definition.RoomName,
            definition.MapFileName,
            Path.Combine(envirRoot, "DynRoomScripts",
                $"DNpc_{definition.RoomName}.pas"),
            null, null, null, null,
            NativeFlag45C: false, NativeFlag45D: true),
        definition.ConfiguredNpcs.Select(configuredNpc =>
            new NpcPlan(
                NpcRole.ConfiguredVisible,
                configuredNpc.NpcName,
                definition.MapFileName,
                Path.Combine(envirRoot, "DynRoomScripts",
                    $"{configuredNpc.ScriptName}-{definition.RoomName}.pas"),
                configuredNpc.X,
                configuredNpc.Y,
                configuredNpc.Face,
                configuredNpc.Body,
                NativeFlag45C: true, NativeFlag45D: false))
            .ToList());
}

static void AssertInOrder(string text, params string[] values)
{
    var offset = 0;
    foreach (var value in values)
    {
        var index = text.IndexOf(value, offset, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException(
                $"IDA sequence token missing or out of order: {value}");
        offset = index + value.Length;
    }
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

internal enum NpcRole
{
    HiddenController,
    ConfiguredVisible
}

internal sealed record EnvironmentOwner(int RuntimeServerIndex, string RoomName,
    int InstanceIndex);

internal sealed record NpcPlan(NpcRole Role, string CharacterName,
    string MapName, string ScriptPath, int? X, int? Y, int? Direction,
    int? Appearance, bool NativeFlag45C, bool NativeFlag45D);

internal sealed record EnvironmentNpcPlan(EnvironmentOwner Owner,
    NpcPlan Controller, IReadOnlyList<NpcPlan> ConfiguredNpcs);
