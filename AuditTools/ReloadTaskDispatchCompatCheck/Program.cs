using GameSvr;
using GameSvr.PasEngine;

var root = Path.Combine(Path.GetTempPath(), "loym2-reload-task-dispatch-"
    + Guid.NewGuid().ToString("N"));
var scriptDirectory = Path.Combine(root, "PsMapQuest");
Directory.CreateDirectory(scriptDirectory);

try
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.sConfigPath = root;

    var scriptPath = Path.Combine(scriptDirectory, "TaskDispatch.pas");
    File.WriteAllText(scriptPath, "program Mir2;\r\n"
        + "procedure OnInitialize; begin end;\r\n"
        + "begin end.\r\n");

    var host = new PasScriptHost(root);
    if (!host.ReloadTaskDispatch())
        throw new Exception("TaskDispatch OnInitialize was not executed");

    File.Delete(scriptPath);
    if (host.ReloadTaskDispatch())
        throw new Exception("missing TaskDispatch script reported success");

    Console.WriteLine("PASS ReloadTaskDispatchCompatCheck: targeted script cache reload and OnInitialize dispatch");
}
finally
{
    if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
}
