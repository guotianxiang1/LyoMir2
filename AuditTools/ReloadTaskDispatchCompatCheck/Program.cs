using GameSvr;
using GameSvr.PasEngine;

PrepareRuntimeConfig();

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

    var helperQuestPath = Path.Combine(scriptDirectory, "HelperQuest.pas");
    File.WriteAllText(helperQuestPath, "program Mir2;\r\n"
        + "procedure _select; begin end;\r\n"
        + "begin end.\r\n");
    if (!host.TryCallHelperQuestMain(null))
        throw new Exception("HelperQuest @Main was not executed");
    if (!host.TryCallHelperQuestLabel(null, "@select"))
        throw new Exception("HelperQuest button label was not executed");

    var logoutQuestPath = Path.Combine(scriptDirectory, "LogoutQuest.pas");
    File.WriteAllText(logoutQuestPath, "program Mir2;\r\n"
        + "function GetPreQuitInfo: string;\r\n"
        + "begin Result := 'ready'; end;\r\n"
        + "begin end.\r\n");
    if (!host.TryCallLogoutQuestPreQuitInfo(null, out var preQuitInfo)
        || !string.Equals(preQuitInfo.AsString(), "ready",
            StringComparison.Ordinal))
    {
        throw new Exception("LogoutQuest GetPreQuitInfo was not executed");
    }

    File.Delete(helperQuestPath);
    var commonDirectory = Path.Combine(root, "CommonScripts");
    Directory.CreateDirectory(commonDirectory);
    File.WriteAllText(Path.Combine(commonDirectory, "HelperQuest.pas"),
        "program Mir2;\r\nbegin end.\r\n");
    if (host.TryCallHelperQuestMain(null))
        throw new Exception("CM 4417 fell back to CommonScripts HelperQuest");

    File.Delete(logoutQuestPath);
    File.WriteAllText(Path.Combine(commonDirectory, "LogoutQuest.pas"),
        "program Mir2;\r\nfunction GetPreQuitInfo: string;\r\n"
        + "begin Result := 'decoy'; end;\r\nbegin end.\r\n");
    if (host.TryCallLogoutQuestPreQuitInfo(null, out _))
        throw new Exception("CM 1250 fell back to CommonScripts LogoutQuest");

    File.Delete(scriptPath);
    if (host.ReloadTaskDispatch())
        throw new Exception("missing TaskDispatch script reported success");

    Console.WriteLine("PASS ReloadTaskDispatchCompatCheck: targeted TaskDispatch, HelperQuest, and LogoutQuest dispatch");
}
finally
{
    if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
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
