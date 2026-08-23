using GameSvr;
using GameSvr.PasEngine;

var root = Path.Combine(Path.GetTempPath(), "loym2-reload-task-"
    + Guid.NewGuid().ToString("N"));
var taskDirectory = Path.Combine(root, "PsTaskList");
Directory.CreateDirectory(taskDirectory);

try
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.sConfigPath = root;

    var configPath = Path.Combine(taskDirectory, "PsTaskConfig.txt");
    var scriptPath = Path.Combine(taskDirectory, "TaskA.pas");
    File.WriteAllText(configPath, "TaskA\r\n");
    File.WriteAllText(scriptPath, "program Mir2;\r\n"
        + "function GetTaskID(): Integer; begin Result := 123; end;\r\n"
        + "function GetTaskType(): Integer; begin Result := 1; end;\r\n"
        + "function GetTaskTitle(): string; begin Result := 'Task A'; end;\r\n"
        + "begin end.\r\n");

    var host = new PasScriptHost(root);
    if (host.LoadTaskScripts() != 1)
        throw new Exception("initial task script load did not produce one task");
    if (host.GetTaskScripts().Single().TaskId != 123)
        throw new Exception("initial task metadata was not loaded");

    File.WriteAllText(configPath, string.Empty);
    if (host.ReloadTaskScripts() != 0 || host.GetTaskScripts().Count != 0)
        throw new Exception("ReloadTask did not clear the task collection");

    Console.WriteLine("PASS ReLoadTaskCompatCheck: task cache reloads independently and reports the new count");
}
finally
{
    if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
}
