using GameSvr;
using GameSvr.Configs;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"PASS {name}");
        return;
    }

    failures.Add(name);
    Console.WriteLine($"FAIL {name}");
}

var root = Path.Combine(Path.GetTempPath(), "loym2-chg-open-game-time-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var missingPath = Path.Combine(root, "missing", "!Setup.txt");
    var missing = new ServerConfig(missingPath);
    Check(!File.Exists(missingPath), "missing !Setup.txt remains absent after config construction");
    Check(!missing.TryWriteOpenDay(new DateTime(2026, 8, 22), out _),
        "missing !Setup.txt follows native silent FileExists-false branch");
    Check(!missing.TryWriteGSTaskVersion("10002"),
        "missing !Setup.txt keeps GS_Task_Version silent");
    Check(!File.Exists(missingPath), "missing !Setup.txt is not created by ChgOpenGameTime");

    var setupPath = Path.Combine(root, "!Setup.txt");
    File.WriteAllText(setupPath, "[Setup]\r\nHomeMap=0\r\n", System.Text.Encoding.UTF8);
    var config = new ServerConfig(setupPath);
    Check(config.OpenDay == null, "absent [Setup]OpenDay loads as null");
    Check(config.TryWriteOpenDay(new DateTime(2026, 8, 22), out var formatted),
        "valid date persists when !Setup.txt exists");
    Check(formatted == "2026-08-22", "date is formatted as yyyy-MM-dd");
    Check(config.OpenDay == new DateTime(2026, 8, 22), "runtime OpenDay state updates after write");

    var persisted = File.ReadAllText(setupPath, System.Text.Encoding.UTF8);
    Check(persisted.Contains("[Setup]", StringComparison.Ordinal)
        && persisted.Contains("OpenDay=2026-08-22", StringComparison.Ordinal),
        "[Setup]OpenDay is persisted with the canonical value");

    Check(config.TryWriteGSTaskVersion("10002"),
        "GS_Task_Version persists when !Setup.txt exists");
    var gsPersisted = File.ReadAllText(setupPath, System.Text.Encoding.UTF8);
    Check(gsPersisted.Contains("GS_Task_Version=10002", StringComparison.Ordinal),
        "GS_Task_Version preserves the native string value");

    var reloaded = new ServerConfig(setupPath);
    Check(reloaded.OpenDay == new DateTime(2026, 8, 22),
        "startup config load restores [Setup]OpenDay state");
    Check(reloaded.OpenDay.Value.ToOADate() == new DateTime(2026, 8, 22).ToOADate(),
        "getopengametime contract uses the persisted Delphi/OLE date double");

    var invalidPath = Path.Combine(root, "invalid.txt");
    File.WriteAllText(invalidPath, "[Setup]\r\nOpenDay=22/08/2026\r\n", System.Text.Encoding.UTF8);
    var invalid = new ServerConfig(invalidPath);
    Check(invalid.OpenDay == null, "non-canonical OpenDay is rejected instead of guessing locale");

    var commandSource = File.ReadAllText(Path.Combine(AuditRepoRoot.Resolve(),
        "GameSvr", "Command", "Commands", "ChgGameOpenTimeCommand.cs"));
    Check(commandSource.Contains("PlayObject.SysMsg(\"开区时间:\" + formatted", StringComparison.Ordinal)
        && commandSource.Contains("MsgColor.Green", StringComparison.Ordinal),
        "valid write reports the exact native green success message");
    Check(commandSource.Contains("NativeUsage", StringComparison.Ordinal)
        && commandSource.Contains("string.IsNullOrEmpty(sDate)", StringComparison.Ordinal),
        "no-argument path reports native usage");

    var gsCommandSource = File.ReadAllText(Path.Combine(AuditRepoRoot.Resolve(),
        "GameSvr", "Command", "Commands", "SetGsTaskVersionCommand.cs"));
    Check(gsCommandSource.Contains("GS_Task_Version成功修改为：", StringComparison.Ordinal)
        && gsCommandSource.Contains("TryWriteGSTaskVersion(value)", StringComparison.Ordinal)
        && gsCommandSource.Contains("value.Length == 0", StringComparison.Ordinal),
        "SetGsTaskVersion trims, persists, and stays silent for an empty value");

    var apiSource = File.ReadAllText(Path.Combine(AuditRepoRoot.Resolve(),
        "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    Check(apiSource.Contains("PasValue.FromDouble(M2Share.ServerConf?.OpenDay?.ToOADate() ?? 0d)",
        StringComparison.Ordinal),
        "getopengametime returns OpenDay as an OLE double and zero when absent");
}
finally
{
    Directory.Delete(root, recursive: true);
}

if (failures.Count != 0)
{
    Console.Error.WriteLine($"ChgOpenGameTimeCompatCheck: {failures.Count} failure(s)");
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine("ChgOpenGameTimeCompatCheck: PASS");
}
