using GameSvr;
using System.Runtime.CompilerServices;

var runtimeDirectory = AppContext.BaseDirectory;
File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"), "[Server]\r\n");
File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"), "[String]\r\n");
File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"), "[Command]\r\n");
var shareDirectory = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "Share"));
Directory.CreateDirectory(shareDirectory);
File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
    "[PlayerLevelExp]\r\n");
File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"), "[Integer]\r\n");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

M2Share.g_boMission = false;
M2Share.g_sMissionMap = string.Empty;
M2Share.g_nMissionX = 0;
M2Share.g_nMissionY = 0;

var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
    typeof(TPlayObject));
player.m_sMapName = "TaskMapWithLongName";

Assert(!NativeGmTaskMonsterCommands.TryArmTaskTarget(player, null, "2",
        out _, out _), "missing X must disarm the task");
Assert(!NativeGmTaskMonsterCommands.TaskTargetArmed,
    "missing X left task armed");

Assert(NativeGmTaskMonsterCommands.TryArmTaskTarget(player, "12", "34",
        out var targetX, out var targetY), "valid DoTask did not arm");
Assert(targetX == 12 && targetY == 34, "DoTask parsed coordinates drifted");
Assert(NativeGmTaskMonsterCommands.TaskTargetArmed,
    "valid DoTask did not set armed flag");
Assert(NativeGmTaskMonsterCommands.TaskMapName == "TaskMapWithLong",
    "native ShortString map copy was not bounded to 15 characters");
Assert(NativeGmTaskMonsterCommands.TaskTargetX == 12 &&
       NativeGmTaskMonsterCommands.TaskTargetY == 34,
    "mission globals were not written");

var notArmed = NativeGmTaskMonsterCommands.CallTaskMon("1", "1", "Zuma", "1");
Assert(notArmed.Result == NativeGmTaskMonsterCommands.CallTaskMonResult.InvalidArguments,
    "missing map must reject an otherwise armed spawn");

Assert(!NativeGmTaskMonsterCommands.TryArmTaskTarget(player, string.Empty,
        string.Empty, out _, out _), "empty X must report DoTask failure");
var disarmed = NativeGmTaskMonsterCommands.CallTaskMon("1", "1", "Zuma", "1");
Assert(disarmed.Result == NativeGmTaskMonsterCommands.CallTaskMonResult.NotArmed,
    "CallTaskMon ignored the disarmed state");

Console.WriteLine("PASS NativeGmTaskMonsterCheck: global target, ShortString map, guards, and spawn argument contract");
