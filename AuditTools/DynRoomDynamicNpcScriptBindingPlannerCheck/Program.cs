using System.Text;
using GameSvr;

var envirRoot = Environment.GetEnvironmentVariable("LYOMIR_PRODUCTION_ENVIR")
    ?? @"D:\lyom2Release\mud2.0\Mir200\Envir";

Assert(NativeDynamicRoomDefinitionLoader.TryLoad(
        Path.Combine(envirRoot, "PsDynNpc.txt"),
        out var definitions, out var errors),
    "production PsDynNpc.txt was rejected: " + string.Join(" | ", errors));
Equal(22, definitions.Count, "production dynamic room count");

Assert(NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
        definitions, envirRoot, 0, out var allBindings, out errors),
    "current-GS dynamic NPC bindings were rejected: " + string.Join(" | ", errors));
Equal(44, allBindings.Count, "binding description count");
Equal(22, allBindings.Count(binding =>
    binding.Role == NativeDynamicRoomDynamicNpcScriptRole.HiddenController),
    "hidden controller binding count");
Equal(22, allBindings.Count(binding =>
    binding.Role == NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible),
    "configured NPC binding count");
Equal(22, allBindings.Select(binding => binding.Definition)
        .Distinct().Count(),
    "definition count after opaque column-2 handling");
ExpectBinding(allBindings, NativeDynamicRoomDynamicNpcScriptRole.HiddenController,
    "Sky", null, "DNpc_Sky.pas", true);
ExpectBinding(allBindings, NativeDynamicRoomDynamicNpcScriptRole.HiddenController,
    "NewSky", null, "DNpc_NewSky.pas", true);
ExpectBinding(allBindings, NativeDynamicRoomDynamicNpcScriptRole.HiddenController,
    "Dare", null, "DNpc_Dare.pas", false);
ExpectBinding(allBindings, NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible,
    "Sky", "天关统领", "天关统领-Sky.pas", true);

Assert(NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
        definitions, envirRoot, 7, out var alternateBindings, out errors),
    "alternate current-GS bindings were rejected: " + string.Join(" | ", errors));
Assert(allBindings.Select(binding => binding.ScriptFileName).SequenceEqual(
        alternateBindings.Select(binding => binding.ScriptFileName),
        StringComparer.Ordinal),
    "column-2 metadata changed binding selection");
foreach (var roomName in new[]
         {
             "qifuRoom", "fuMoRoom", "DiXue2", "FengYin2",
             "KuangDong2", "ShenDian2", "ShiMu2", "XieKu2"
         })
{
    ExpectBinding(allBindings, NativeDynamicRoomDynamicNpcScriptRole.HiddenController,
        roomName, null, $"DNpc_{roomName}.pas", true);
}
Equal(10, allBindings.Count(binding => binding.HasScript
        && binding.Role == NativeDynamicRoomDynamicNpcScriptRole.HiddenController),
    "total hidden controller script count");
Equal(21, allBindings.Count(binding => binding.HasScript
        && binding.Role == NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible),
    "total configured NPC script count");
Assert(allBindings.Where(binding => binding.HasScript)
        .All(binding => binding.ScriptByteLength > 0
            && !string.IsNullOrWhiteSpace(binding.FirstLine)
            && Path.GetFullPath(binding.ScriptPath).StartsWith(
                Path.GetFullPath(Path.Combine(envirRoot, "DynRoomScripts")),
                StringComparison.OrdinalIgnoreCase)),
    "production dynamic NPC path or GBK text validation failed");
Assert(allBindings.Where(binding => binding.HasScript
        && binding.Role == NativeDynamicRoomDynamicNpcScriptRole.HiddenController)
        .All(binding => binding.FirstLine == "program Mir2;"),
    "hidden controller script header changed");

var tempRoot = Path.Combine(Path.GetTempPath(), "dynroom-dnpc-binding-"
    + Guid.NewGuid().ToString("N"));
try
{
    var dynRoomScripts = Path.Combine(tempRoot, "DynRoomScripts");
    Directory.CreateDirectory(dynRoomScripts);
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    File.WriteAllBytes(Path.Combine(dynRoomScripts, "DNpc_TestRoom.pas"),
        Encoding.GetEncoding(936).GetBytes("program Mir2;\r\nbegin\r\nend.\r\n"));
    File.WriteAllBytes(Path.Combine(dynRoomScripts, "Visible-TestRoom.pas"),
        Encoding.GetEncoding(936).GetBytes("program Mir2;\r\nbegin\r\nend.\r\n"));

    var safeDefinition = NewDefinition("TestRoom", "Visible");
    Assert(NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { safeDefinition }, tempRoot, 0, out var bindings, out errors),
        "safe temp dynamic NPC binding failed: " + string.Join(" | ", errors));
    Equal(2, bindings.Count, "safe binding count");
    var safeController = FindBinding(bindings,
        NativeDynamicRoomDynamicNpcScriptRole.HiddenController, "TestRoom", null);
    var safeConfigured = FindBinding(bindings,
        NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible, "TestRoom", "Visible");
    Equal("DNpc_TestRoom.pas", safeController.ScriptFileName,
        "safe binding file name");
    Equal("Visible-TestRoom.pas", safeConfigured.ScriptFileName,
        "safe configured binding file name");
    Equal(true, safeController.HasScript, "safe controller binding existence");
    Equal(true, safeConfigured.HasScript, "safe configured binding existence");
    Equal("program Mir2;", safeConfigured.FirstLine,
        "safe configured binding first line");

    var missingDefinition = NewDefinition("NoScript");
    Assert(NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { missingDefinition }, tempRoot, 0, out bindings, out errors),
        "missing optional dynamic NPC binding failed: " + string.Join(" | ", errors));
    var missingController = FindBinding(bindings,
        NativeDynamicRoomDynamicNpcScriptRole.HiddenController, "NoScript", null);
    Equal(false, missingController.HasScript, "missing binding existence");
    Equal("DNpc_NoScript.pas", missingController.ScriptFileName,
        "missing binding file name");

    var escapingDefinition = NewDefinition("..\\Escape");
    Assert(!NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { escapingDefinition }, tempRoot, 0, out _, out errors)
        && errors.Any(error => error.Contains("unsafe dynamic NPC script file name",
            StringComparison.Ordinal)),
        "path escape dynamic NPC binding was accepted");

    var escapingConfiguredDefinition = NewDefinition("ConfigEscape", "..\\Escape");
    Assert(!NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { escapingConfiguredDefinition }, tempRoot, 0, out _, out errors)
        && errors.Any(error => error.Contains("unsafe dynamic NPC script file name",
            StringComparison.Ordinal)),
        "configured NPC path escape was accepted");

    Assert(!NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new NativeDynamicRoomDefinition[] { null }, tempRoot, 0,
            out _, out errors)
        && errors.Any(error => error.Contains("definition is null",
            StringComparison.Ordinal)),
        "null dynamic room definition was accepted");

    var nullConfiguredNpcs = new NativeDynamicRoomDefinition("NullConfigured", 1,
        5, "Test", "D411", 1, 1, Array.Empty<string>(), null, 1);
    Assert(!NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { nullConfiguredNpcs }, tempRoot, 0, out _, out errors)
        && errors.Any(error => error.Contains("configured NPC definitions are null",
            StringComparison.Ordinal)),
        "null configured NPC definitions were accepted");

    Assert(!NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { NewDefinition("") }, tempRoot, 0, out _, out errors)
        && errors.Any(error => error.Contains("empty room name",
            StringComparison.Ordinal)),
        "empty room name was accepted");

    Assert(!NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { NewDefinition("EmptyScript", "") }, tempRoot, 0,
            out _, out errors)
        && errors.Any(error => error.Contains("empty script name",
            StringComparison.Ordinal)),
        "empty configured NPC script name was accepted");

    var duplicateA = NewDefinition("Same");
    var duplicateB = NewDefinition("Same");
    Assert(!NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { duplicateA, duplicateB }, tempRoot, 0, out _, out errors)
        && errors.Any(error => error.Contains("duplicate dynamic NPC script binding",
            StringComparison.Ordinal)),
        "duplicate dynamic NPC binding was accepted");

    File.WriteAllBytes(Path.Combine(dynRoomScripts, "DNpc_Repeated.pas"),
        Encoding.GetEncoding(936).GetBytes("program Mir2;\r\nbegin\r\nend.\r\n"));
    File.WriteAllBytes(Path.Combine(dynRoomScripts, "Shared-Repeated.pas"),
        Encoding.GetEncoding(936).GetBytes("program Mir2;\r\nbegin\r\nend.\r\n"));
    Assert(NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { NewDefinition("Repeated", "Shared", "Shared") },
            tempRoot, 0, out bindings, out errors),
        "same-script configured NPC bindings were rejected: "
        + string.Join(" | ", errors));
    Equal(2, bindings.Count(binding => binding.Role
        == NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible),
        "same-script configured NPC binding count");

    File.WriteAllBytes(Path.Combine(dynRoomScripts, "DNpc_Empty.pas"),
        Array.Empty<byte>());
    Assert(!NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { NewDefinition("Empty") }, tempRoot, 0, out _, out errors)
        && errors.Any(error => error.Contains("dynamic NPC script is empty",
            StringComparison.Ordinal)),
        "empty dynamic NPC script was accepted");

    var lockedScript = Path.Combine(dynRoomScripts, "DNpc_Locked.pas");
    File.WriteAllBytes(lockedScript, Encoding.GetEncoding(936).GetBytes(
        "program Mir2;\r\nbegin\r\nend.\r\n"));
    using (new FileStream(lockedScript, FileMode.Open, FileAccess.Read,
               FileShare.None))
    {
        Assert(!NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
                new[] { NewDefinition("Locked") }, tempRoot, 0,
                out _, out errors)
            && errors.Any(error => error.Contains("could not be read",
                StringComparison.Ordinal)),
            "locked dynamic NPC script was accepted or threw");
    }

    Assert(!NativeDynamicRoomDynamicNpcScriptBindingPlanner.TryPlanBindings(
            new[] { safeDefinition }, tempRoot, -1, out _, out errors)
        && errors.Any(error => error.Contains("invalid current server index",
            StringComparison.Ordinal)),
        "negative current server index was accepted");
}
finally
{
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, true);
}

Console.WriteLine("DynRoomDynamicNpcScriptBindingPlannerCheck PASS "
    + "definitions=22 bindings=44 hidden=10 configured=21 gbk=ok "
    + "current-server=0 metadata=opaque missing=optional path=guarded");

static void ExpectBinding(
    IEnumerable<NativeDynamicRoomDynamicNpcScriptBinding> bindings,
    NativeDynamicRoomDynamicNpcScriptRole role, string roomName,
    string configuredScriptName, string fileName, bool hasScript)
{
    var binding = FindBinding(bindings, role, roomName, configuredScriptName);
    Assert(binding != null, $"binding missing for room {roomName}");
    Equal(fileName, binding.ScriptFileName, $"file name for room {roomName}");
    Equal(hasScript, binding.HasScript, $"script existence for room {roomName}");
}

static NativeDynamicRoomDynamicNpcScriptBinding FindBinding(
    IEnumerable<NativeDynamicRoomDynamicNpcScriptBinding> bindings,
    NativeDynamicRoomDynamicNpcScriptRole role, string roomName,
    string configuredScriptName)
{
    return bindings.SingleOrDefault(item => item.Role == role
        && item.Definition.RoomName == roomName
        && (role != NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible
            || item.ConfiguredNpc?.ScriptName == configuredScriptName));
}

static NativeDynamicRoomDefinition NewDefinition(string roomName,
    params string[] configuredScriptNames)
{
    var configuredNpcs = configuredScriptNames.Select((scriptName, index) =>
        new NativeDynamicRoomConfiguredNpcDefinition(scriptName, index + 1,
            index + 2, "Npc" + index, 0, 15, index + 2)).ToArray();
    return new NativeDynamicRoomDefinition(roomName, "not-a-server", 5, "Test", "D411",
        1, 1, Array.Empty<string>(), configuredNpcs, 1);
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
