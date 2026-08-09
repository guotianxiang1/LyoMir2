using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Plugins;
using SystemModule;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
M2Share.LogSystem = new MirLog();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.ProcessHumanCriticalSection = new object();

var tempRoot = Path.Combine(Path.GetTempPath(),
    "loym2-yanshen-cd-" + Guid.NewGuid().ToString("N"));
try
{
    var envirPath = Directory.CreateDirectory(Path.Combine(tempRoot, "Envir")).FullName;
    File.WriteAllText(Path.Combine(tempRoot, "config.json"),
        "{\"\u6beb\u79d2\u7ea7cd\u8bb0\u5f55\":1}", HUtil32.GbkEncoding);
    var pluginManager = new PluginManager(envirPath);
    pluginManager.RegisterBuiltinPlugins();
    Assert(pluginManager.LoadPlugin("YanshenCompat"), "YanshenCompat did not load");
    M2Share.PluginManager = pluginManager;

    var player = new TPlayObject { m_sCharName = "CD测试角色" };
    var bridge = new PasApiBridge();
    InitializeYanshen(bridge, envirPath);
    using (bridge.PushContext(player, null))
    {
        Assert(bridge.CallStandaloneFunction("ys_CmpTime_min",
            new List<PasValue>
            {
                PasValue.FromInt(111), PasValue.FromInt(112), PasValue.FromInt(5_000)
            }, out var result), "ys_CmpTime_min was not dispatched");
        Assert(result.AsBool(), "zero timestamp did not pass ys_CmpTime_min");

        var before = Environment.TickCount;
        Assert(bridge.CallStandaloneFunction("ys_SetCD_min",
            new List<PasValue> { PasValue.FromInt(111), PasValue.FromInt(112) },
            out result), "ys_SetCD_min was not dispatched");
        Assert(result.AsBool(), "ys_SetCD_min did not execute the eye timestamp tunnel");

        Assert(bridge.CallStandaloneFunction("GetV",
            new List<PasValue> { PasValue.FromInt(111), PasValue.FromInt(112) },
            out result), "GetV was not dispatched");
        var stored = result.AsInt();
        Assert(unchecked((uint)(stored - before)) <= 5_000,
            $"stored millisecond tick is outside the execution window: {stored}");

        Assert(bridge.CallStandaloneFunction("ys_CmpTime_min",
            new List<PasValue>
            {
                PasValue.FromInt(111), PasValue.FromInt(112), PasValue.FromInt(5_000)
            }, out result), "ys_CmpTime_min immediate comparison was not dispatched");
        Assert(!result.AsBool(), "fresh timestamp was reported expired");

        Assert(bridge.CallStandaloneFunction("SetV",
            new List<PasValue>
            {
                PasValue.FromInt(111), PasValue.FromInt(112),
                PasValue.FromInt(unchecked(Environment.TickCount - 5_001))
            }, out result), "SetV was not dispatched");
        Assert(bridge.CallStandaloneFunction("ys_CmpTime_min",
            new List<PasValue>
            {
                PasValue.FromInt(111), PasValue.FromInt(112), PasValue.FromInt(5_000)
            }, out result), "ys_CmpTime_min expired comparison was not dispatched");
        Assert(result.AsBool(), "expired timestamp did not pass ys_CmpTime_min");

        Assert(bridge.CallStandaloneFunction("ys_SetCD_min",
            new List<PasValue> { PasValue.FromInt(111) }, out result),
            "invalid ys_SetCD_min call was treated as an unknown API");
        Assert(!result.AsBool(), "invalid ys_SetCD_min call reported success");

        Assert(bridge.CallStandaloneFunction("ys_CmpTime_min",
            new List<PasValue> { PasValue.FromInt(111), PasValue.FromInt(112) },
            out result), "invalid ys_CmpTime_min call was treated as an unknown API");
        Assert(!result.AsBool(), "invalid ys_CmpTime_min call reported success");
    }

    M2Share.PluginManager = null;
    using (bridge.PushContext(player, null))
    {
        try
        {
            bridge.CallStandaloneFunction("ys_CmpTime_min",
                new List<PasValue>
                {
                    PasValue.FromInt(111), PasValue.FromInt(112), PasValue.FromInt(5_000)
                }, out _);
            throw new InvalidOperationException("plugin-off ys_CmpTime_min did not report API unavailable");
        }
        catch (YanshenApiUnavailableException ex)
        {
            Assert(ex.FailureReason == "插件未运行",
                $"plugin-off reason mismatch: {ex.FailureReason}");
        }
    }

    Console.WriteLine("YanshenCdCompatCheck PASS");
}
finally
{
    M2Share.PluginManager = null;
    if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void InitializeYanshen(PasApiBridge bridge, string envirPath)
{
    const string source = "program RunQuest; procedure initys; begin end; begin end.";
    var sourceFile = Path.Combine(envirPath, "PsMapQuest", "RunQuest.pas");
    var program = new PasParser(new PasLexer(source, sourceFile), envirPath).Parse();
    new PasInterpreter(program, bridge).ExecuteProcedure("initys");
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
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
