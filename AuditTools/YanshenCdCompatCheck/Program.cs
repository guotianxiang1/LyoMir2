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
    // 中文隧道 `!!!!hq取sj戳` 的原生实现是 0x1005E68A `mov eax,[Self+0xE0]`，
    // 也就是状态走查闩 m_TimedAbilityProcessTick（0x772FF5 每轮走查用 GetTickCount
    // 硬写，走查被 0x772FEA cmp eax,0x1F4 限成 500 ms 一次）。裸构造的对象从未被
    // 走查过，闩是 0；真实服务器上它一直在跟。这里显式播一个值来模拟"已被走查过"，
    // 并在下面断言隧道回的**就是这个闩**而不是当前时钟。
    var latch = Environment.TickCount;
    SeedTimedAbilityLatch(player, latch);
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

        Assert(bridge.CallStandaloneFunction("ys_SetCD_min",
            new List<PasValue> { PasValue.FromInt(111), PasValue.FromInt(112) },
            out result), "ys_SetCD_min was not dispatched");
        Assert(result.AsBool(), "ys_SetCD_min did not execute the eye timestamp tunnel");

        Assert(bridge.CallStandaloneFunction("GetV",
            new List<PasValue> { PasValue.FromInt(111), PasValue.FromInt(112) },
            out result), "GetV was not dispatched");
        var stored = result.AsInt();
        Assert(stored == latch,
            $"ys_SetCD_min stored {stored}, but the native tunnel returns [player+0xE0] = {latch}");

        // 反向验证：闩动了，隧道的返回值必须跟着动 —— 这条排除"其实读的是
        // Environment.TickCount，只是恰好接近闩"的可能。
        SeedTimedAbilityLatch(player, unchecked(latch + 12_345));
        Assert(bridge.CallStandaloneFunction("ys_SetCD_min",
            new List<PasValue> { PasValue.FromInt(111), PasValue.FromInt(113) },
            out result), "ys_SetCD_min was not dispatched for the moved latch");
        Assert(bridge.CallStandaloneFunction("GetV",
            new List<PasValue> { PasValue.FromInt(111), PasValue.FromInt(113) },
            out result), "GetV was not dispatched for the moved latch");
        Assert(result.AsInt() == unchecked(latch + 12_345),
            $"tunnel did not follow [player+0xE0]: got {result.AsInt()}");
        SeedTimedAbilityLatch(player, latch);

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
                PasValue.FromInt(unchecked(latch - 5_001))
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

static void SeedTimedAbilityLatch(TPlayObject player, int tick)
{
    var field = typeof(TBaseObject).GetField("m_TimedAbilityProcessTick",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "m_TimedAbilityProcessTick is gone — the hq取sj戳 tunnel lost its native anchor");
    field.SetValue(player, tick);
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
