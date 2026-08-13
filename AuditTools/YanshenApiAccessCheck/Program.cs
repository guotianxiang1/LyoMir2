using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Plugins;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();

var failures = new List<string>();
var tempRoot = Path.Combine(Path.GetTempPath(),
    "loym2-yanshen-api-access-" + Guid.NewGuid().ToString("N"));
var keyPrefix = "access-" + Guid.NewGuid().ToString("N");

try
{
    Directory.CreateDirectory(tempRoot);
    InitializeGameState();

    var directPath = Path.Combine(tempRoot, "DirectAccessProbe.pas");
    var directSource = BuildDirectSource(keyPrefix);
    File.WriteAllText(directPath, directSource, new UTF8Encoding(false));
    var bridge = new PasApiBridge();
    var interpreter = ParseInterpreter(directPath, bridge);

    var wrapperPath = Path.Combine(tempRoot, "AllFucWrapperProbe.pas");
    var wrapperSource = BuildWrapperSource();
    File.WriteAllText(wrapperPath, wrapperSource, new UTF8Encoding(false));
    var wrapperBridge = new PasApiBridge();
    var wrapperInterpreter = ParseInterpreter(wrapperPath, wrapperBridge);

    var tunnelPath = Path.Combine(tempRoot, "TunnelAccessProbe.pas");
    var tunnelSource = BuildTunnelSource();
    File.WriteAllText(tunnelPath, tunnelSource, new UTF8Encoding(false));
    var tunnelBridge = new PasApiBridge();
    var tunnelInterpreter = ParseInterpreter(tunnelPath, tunnelBridge);

    var manager = CreateYanshenManager(tempRoot);
    M2Share.PluginManager = manager;
    var plugin = RequiredPlugin(manager);
    var player = new TPlayObject { m_sCharName = "yanshen-access-player" };
    player.m_WAbil.Weight = 17;
    player.m_WAbil.MaxWeight = 29;

    Check("registered plugin is not initialized", () =>
    {
        Equal(PluginState.Registered, plugin.State, "initial plugin state");
        Require(!plugin.IsInitialized, "registered plugin started initialized");
        ExpectDiagnostic(
            () => interpreter.ExecuteProcedure("StatementCall"),
            directSource, directPath, "YSSetG", "StatementCall",
            "YSSetG('statement'", "插件未运行");
    });

    Check("Running without initys is rejected", () =>
    {
        Require(manager.LoadPlugin("YanshenCompat"), "plugin did not load");
        Equal(PluginState.Running, plugin.State, "loaded plugin state");
        Require(!plugin.IsInitialized, "loading the plugin counted as initys");
        manager.SetNativeConfigValue("地面物品消失时间", 1);
        manager.SetNativeConfigValue("地面物品消失时间_时间", 0);
        var runtimeApi = new YanshenApi(player, null, manager);
        Require(!runtimeApi.TryGetFloorItemTimeout(out _),
            "engine feature helper bypassed the initys gate");
        interpreter.ExecuteProcedure("NotInit");
        Require(!plugin.IsInitialized, "an unrelated procedure counted as initys");
        ExpectDiagnostic(
            () => interpreter.ExecuteProcedure("ExpressionCall"),
            directSource, directPath, "YSGetG", "ExpressionCall",
            "YSGetG('expression'", "未初始化（必须先执行 initys）");
    });

    Check("numeric !!!! tunnel rejects Running without initys", () =>
    {
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "NumericTunnel", tunnelSource, tunnelPath, "GetBagItemCount",
            "GetBagItemCount('!!!!集成函数,36,0$')",
            "未初始化（必须先执行 initys）");
    });

    var failedInitializerHost = CreateInitializerHost(
        Path.Combine(tempRoot, "FailedInitializer"), fail: true);
    Check("failed RunQuest initys rolls initialization back", () =>
    {
        Require(!failedInitializerHost.TryInitializeYanshen(player),
            "failing RunQuest initys unexpectedly reported success");
        Require(!plugin.IsInitialized,
            "failing RunQuest initys left the plugin initialized");
        ExpectDiagnostic(
            () => interpreter.ExecuteProcedure("ExpressionCall"),
            directSource, directPath, "YSGetG", "ExpressionCall",
            "YSGetG('expression'", "未初始化（必须先执行 initys）");
    });

    var initializerHost = CreateInitializerHost(tempRoot, fail: false);
    Check("only PsMapQuest/RunQuest.pas initys initializes the plugin", () =>
    {
        interpreter.ExecuteProcedure("initys");
        Require(!plugin.IsInitialized,
            "initys outside PsMapQuest/RunQuest.pas initialized the plugin");
        Require(initializerHost.TryInitializeYanshen(player),
            "PasScriptHost could not invoke PsMapQuest/RunQuest.pas initys");
        Require(plugin.IsInitialized,
            "successful PsMapQuest/RunQuest.pas initys did not initialize the plugin");
    });

    Check("engine feature helpers honor switches, zero timeout, and 15-character maps", () =>
    {
        var runtimeApi = new YanshenApi(player, null, manager);

        manager.SetNativeConfigValue("地面物品消失时间", 0);
        Require(!runtimeApi.TryGetFloorItemTimeout(out _),
            "disabled floor-item timeout was reported active");
        manager.SetNativeConfigValue("地面物品消失时间", 1);
        manager.SetNativeConfigValue("地面物品消失时间_时间", 0);
        Require(runtimeApi.TryGetFloorItemTimeout(out var zeroTimeout) && zeroTimeout == 0,
            "enabled zero-second floor-item timeout lost its active state");
        manager.SetNativeConfigValue("地面物品消失时间_时间", 600);
        Require(runtimeApi.TryGetFloorItemTimeout(out var timeout) && timeout == 600000,
            "floor-item timeout was not converted from seconds to milliseconds");

        player.m_PEnvir = new Envirnoment { sMapName = "D515~6789ABCDEF" };
        manager.SetNativeConfigValue("禁止交易地图", 1);
        manager.SetNativeConfigValue("禁止宝宝休息", 1);
        Require(runtimeApi.IsTradeBanned() && runtimeApi.IsPetRestBlocked(),
            "15-character map rule did not activate");
        player.m_PEnvir.sMapName = "D515~6789ABCDE";
        Require(!runtimeApi.IsTradeBanned() && !runtimeApi.IsPetRestBlocked(),
            "map rule activated for a map name whose length is not 15");

        manager.SetNativeConfigValue("安全区禁止丢物", 1);
        manager.SetNativeConfigValue("下线宝宝死亡", 1);
        Require(runtimeApi.IsSafeNoDrop() && runtimeApi.IsPetDieOffline(),
            "safe-zone drop or offline-pet feature did not pass its enabled gate");
    });

    Check("missing switch is fail-closed for a bare zero-argument API", () =>
    {
        ExpectDiagnostic(
            () => interpreter.ExecuteProcedure("BareMissingSwitch"),
            directSource, directPath, "YSGetG", "BareMissingSwitch",
            "YSGetG;", "开关键缺失（眼神特殊函数）");
    });

    Check("recognized player API checks its own switch", () =>
    {
        using var context = bridge.PushContext(player, null);
        ExpectDiagnostic(
            () => interpreter.ExecuteProcedure("PlayerAliases"),
            directSource, directPath, "YS_GetFZhong", "PlayerAliases",
            "YS_GetFZhong(0)", "开关键缺失（眼神特殊函数）");
    });

    Check("same-name AllFuc wrapper cannot bypass a missing switch", () =>
    {
        ExpectDiagnostic(
            () => wrapperInterpreter.ExecuteProcedure("CallShadowedGetG"),
            wrapperSource, wrapperPath, "YSGetG", "CallShadowedGetG",
            "YSGetG('shadowed')", "开关键缺失（眼神特殊函数）");
    });

    Check("!!!! numeric, Give, and Chinese tunnels reject missing switches", () =>
    {
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "NumericTunnel", tunnelSource, tunnelPath, "GetBagItemCount",
            "GetBagItemCount('!!!!集成函数,36,0$')", "开关键缺失（眼神特殊函数）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "EmbeddedGive", tunnelSource, tunnelPath, "Give",
            "Give('AuditMissingItem!!!!1|2|3|4|5|'", "开关键缺失（自定义元素）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "ChineseTunnel", tunnelSource, tunnelPath, "GetBagItemCount",
            "GetBagItemCount('!!!!hq取sj戳')", "开关键缺失（毫秒级cd记录）");
    });

    // 爱心分割隧道原生没有开关门：入口选择器 sub_1005E4D0 给 `!!!!hq取sj戳`
    // (0x1005E650 cmp [cfg+0x538],0x1F4)、`!!!!zd义回收` (0x1005E6C5 +0x954)、
    // `!!!!给与元素`/`!!!!获取元素` (0x1005E752 +0x664)、`!!!!定义伤害` (0x1005EDA3 +0x510)、
    // `!!!!英雄极品` (0x1005EF7B +0x514) 各配一道门，唯独 `!!!!爱心分割` 比中
    // (0x1005E628 jne) 就直落 0x1005E63D call 0x1005E470 → sub_1005DBA0；
    // 派发器 0x1005DBA0..0x1005E3D5 与 38 个实现体 0x10058ED0..0x1005DBA0 里
    // `A1 <cfg glob>` + `81 38 F4 01 00 00` 门形态 0 命中（集成函数同扫描 40 命中）。
    Check("caret tunnel stays ungated when switches are missing", () =>
    {
        Equal(0, ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "CaretTunnel").AsInt(), "caret tunnel must run without any switch");
    });

    // 入口选择器 sub_1005E4D0 的 8 条前缀一条也比不中时，链尾 0x1005F1D6 →
    // 0x1005F20F `mov eax,0xFFFFF988` 返回 -1656；挂在宿主
    // TPlayObject.GetBagItemCount 0x007447C0 上的钩子 0x58A05264
    // `cmp eax,0xFFFFF988` / 0x58BBAAF5 `je 0x58DBA7B2` 改跑原函数体：
    // 0x7447E7 用名字查 std 物品表（sub_74C1E0 查不到给 -1）、
    // 0x7447EF `jle 0x744868` 跳出口返回计数槽初值 0。
    // `plus伤害` 是附录 A.3 判定的自造名（两版转储五编码全 0 命中），
    // 而 AllFuc.pas 的 ys_MyJn_plus 真的会发它 —— 这条路径可达。
    // 前缀链在所有 `cmp [cfg+disp],0x1F4` 门之前，故三种开关状态下都必须是 0。
    Check("unregistered !!!! prefix falls back to the host bag count (switch missing)", () =>
    {
        Equal(0, ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "FabricatedTunnel").AsInt(),
            "fabricated tunnel must return the host bag count");
    });

    Check("lucker2, libmysql, and CD wrappers reject missing switches", () =>
    {
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "Lucker2Tunnel", tunnelSource, tunnelPath, "GetSignInActPrizer",
            "GetSignInActPrizer('!!!!^1^1', 'lucker2')", "开关键缺失（眼神特殊函数）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "LibmysqlTunnel", tunnelSource, tunnelPath, "GetSignInActPrizer",
            "GetSignInActPrizer('SELECT 1', 'libmysql')", "开关键缺失（眼神特殊函数）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "CdCompareTunnel", tunnelSource, tunnelPath, "ys_CmpTime_min",
            "ys_CmpTime_min(501, 502, 5000)", "开关键缺失（毫秒级cd记录）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "CdSetTunnel", tunnelSource, tunnelPath, "ys_SetCD_min",
            "ys_SetCD_min(501, 502)", "开关键缺失（毫秒级cd记录）");
    });

    manager.SetNativeConfigValue("眼神特殊函数", 0);
    manager.SetNativeConfigValue("自定义元素", 0);
    manager.SetNativeConfigValue("毫秒级cd记录", 0);

    Check("disabled switch rejects the inner nested API", () =>
    {
        ExpectDiagnostic(
            () => interpreter.ExecuteProcedure("NestedCall"),
            directSource, directPath, "YSGetG", "NestedCall",
            "YSGetG('nested-input'", "开关未开启（眼神特殊函数）");
    });

    Check("Pascal try/except receives one structured diagnostic", () =>
    {
        var diagnostic = interpreter.ExecuteProcedure("CatchDenied").AsString();
        AssertDiagnosticText(diagnostic, directSource, directPath,
            "YSGetG", "CatchDenied", "YSGetG('caught'",
            "开关未开启（眼神特殊函数）");
    });

    Check("all compatibility tunnels reject disabled switches", () =>
    {
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "NumericTunnel", tunnelSource, tunnelPath, "GetBagItemCount",
            "GetBagItemCount('!!!!集成函数,36,0$')", "开关未开启（眼神特殊函数）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "EmbeddedGive", tunnelSource, tunnelPath, "Give",
            "Give('AuditMissingItem!!!!1|2|3|4|5|'", "开关未开启（自定义元素）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "ChineseTunnel", tunnelSource, tunnelPath, "GetBagItemCount",
            "GetBagItemCount('!!!!hq取sj戳')", "开关未开启（毫秒级cd记录）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "Lucker2Tunnel", tunnelSource, tunnelPath, "GetSignInActPrizer",
            "GetSignInActPrizer('!!!!^1^1', 'lucker2')", "开关未开启（眼神特殊函数）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "LibmysqlTunnel", tunnelSource, tunnelPath, "GetSignInActPrizer",
            "GetSignInActPrizer('SELECT 1', 'libmysql')", "开关未开启（眼神特殊函数）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "CdCompareTunnel", tunnelSource, tunnelPath, "ys_CmpTime_min",
            "ys_CmpTime_min(501, 502, 5000)", "开关未开启（毫秒级cd记录）");
        ExpectPlayerDiagnostic(tunnelBridge, player, tunnelInterpreter,
            "CdSetTunnel", tunnelSource, tunnelPath, "ys_SetCD_min",
            "ys_SetCD_min(501, 502)", "开关未开启（毫秒级cd记录）");
    });

    Check("caret tunnel stays ungated when switches are off", () =>
    {
        Equal(0, ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "CaretTunnel").AsInt(), "caret tunnel must run with switches off");
    });

    Check("unregistered !!!! prefix falls back to the host bag count (switch off)", () =>
    {
        Equal(0, ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "FabricatedTunnel").AsInt(),
            "fabricated tunnel must return the host bag count");
    });

    var mainPath = Path.Combine(tempRoot, "MainAccessProbe.pas");
    var mainSource = BuildMainSource(keyPrefix);
    File.WriteAllText(mainPath, mainSource, new UTF8Encoding(false));
    var mainInterpreter = ParseInterpreter(mainPath, new PasApiBridge());

    Check("main block reports its source location once", () =>
    {
        ExpectDiagnostic(
            () => mainInterpreter.ExecuteMain(),
            mainSource, mainPath, "YSSetG", "主程序",
            "YSSetG('" + keyPrefix + "-main'", "开关未开启（眼神特殊函数）");
    });

    var includeFixture = CreateIncludeFixture(tempRoot, keyPrefix);
    var host = new PasScriptHost(includeFixture.EnvirPath);
    var includeProgram = LoadThroughHost(host, includeFixture.MainPath);
    var includeInterpreter = new PasInterpreter(includeProgram, host.Api);

    Check("include plus IFDEF keeps original file, line, and column", () =>
    {
        ExpectDiagnostic(
            () => includeInterpreter.ExecuteProcedure("IncludeProbe"),
            includeFixture.IncludeSource, includeFixture.IncludePath,
            "YSSetG", "IncludeProbe", "YSSetG('" + keyPrefix + "-include-active'",
            "开关未开启（眼神特殊函数）");
    });

    manager.SetNativeConfigValue("眼神特殊函数", 1);
    manager.SetNativeConfigValue("自定义元素", 1);
    manager.SetNativeConfigValue("毫秒级cd记录", 1);

    Check("enabled same-name AllFuc wrapper passes the front gate", () =>
    {
        Equal(777, wrapperInterpreter.ExecuteProcedure("CallShadowedGetG").AsInt(),
            "same-name wrapper result");
    });

    Check("multi-switch API rejects a missing secondary switch", () =>
    {
        using var context = wrapperBridge.PushContext(player, null);
        ExpectDiagnostic(
            () => wrapperInterpreter.ExecuteProcedure("CallShadowedPick"),
            wrapperSource, wrapperPath, "YS_Pick", "CallShadowedPick",
            "YS_Pick(0, 0, 0, 0)", "开关键缺失（全屏拾取）");
    });

    manager.SetNativeConfigValue("全屏拾取", 0);
    Check("multi-switch API rejects a disabled secondary switch", () =>
    {
        using var context = wrapperBridge.PushContext(player, null);
        ExpectDiagnostic(
            () => wrapperInterpreter.ExecuteProcedure("CallShadowedPick"),
            wrapperSource, wrapperPath, "YS_Pick", "CallShadowedPick",
            "YS_Pick(0, 0, 0, 0)", "开关未开启（全屏拾取）");
    });

    manager.SetNativeConfigValue("全屏拾取", 1);
    Check("multi-switch same-name wrapper executes when all switches are on", () =>
    {
        using var context = wrapperBridge.PushContext(player, null);
        Equal(208, wrapperInterpreter.ExecuteProcedure("CallShadowedPick").AsInt(),
            "multi-switch wrapper result");
    });

    Check("enabled statement, expression, bare, nested, and aliases execute", () =>
    {
        interpreter.ExecuteProcedure("StatementCall");
        Equal(1, interpreter.ExecuteProcedure("ReadStatementValue").AsInt(),
            "statement API write");
        Equal(-1, interpreter.ExecuteProcedure("ExpressionCall").AsInt(),
            "expression result for an unset key");
        interpreter.ExecuteProcedure("BareZeroArgument");
        Equal(82, interpreter.ExecuteProcedure("AliasRoundTrip").AsInt(),
            "YSSetG/YSGetG round trip");
        Equal(41, interpreter.ExecuteProcedure("NestedCall").AsInt(),
            "nested parameter evaluation");
    });

    Check("enabled API that requires a player executes with context", () =>
    {
        using var context = bridge.PushContext(player, null);
        Equal(46, interpreter.ExecuteProcedure("PlayerAliases").AsInt(),
            "YS_GetFZhong player-context call");
    });

    Check("enabled main and host-preprocessed include execute", () =>
    {
        mainInterpreter.ExecuteMain();
        Equal(208, interpreter.ExecuteProcedure("ReadMainValue").AsInt(),
            "main API write");
        Equal(207, includeInterpreter.ExecuteProcedure("IncludeProbe").AsInt(),
            "active IFDEF include branch");
        Equal(207, interpreter.ExecuteProcedure("ReadIncludeValue").AsInt(),
            "include API write");
    });

    Check("enabled compatibility tunnels execute through their real entry points", () =>
    {
        Equal(17, ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "NumericTunnel").AsInt(), "numeric !!!! tunnel result");
        Equal(0, ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "CaretTunnel").AsInt(), "caret tunnel result");
        Equal(0, ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "FabricatedTunnel").AsInt(),
            "fabricated tunnel must return the host bag count even with switches on");
        _ = ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "ChineseTunnel").AsInt();
        Require(ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "EmbeddedGive").AsBool(), "embedded Give tunnel did not execute");
        Equal("NULL", ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "Lucker2Tunnel").AsString(), "lucker2 tunnel result");
        var sqlResult = ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "LibmysqlTunnel").AsString();
        Require(sqlResult is "" or "1",
            $"libmysql SELECT 1 returned an unexpected value: {sqlResult}");
        Require(ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "CdCompareTunnel").AsBool(), "initial CD comparison did not pass");
        Require(ExecuteWithPlayer(tunnelBridge, player, tunnelInterpreter,
            "CdSetTunnel").AsBool(), "CD setter did not execute");
    });

    Check("concurrent RunQuest initys leaves one initialized state", () =>
    {
        Require(manager.UnloadPlugin("YanshenCompat"), "plugin unload failed");
        Require(manager.LoadPlugin("YanshenCompat"), "plugin reload failed");
        Require(!plugin.IsInitialized, "plugin reload did not clear initialization");

        Parallel.For(0, 8, iteration =>
        {
            var concurrentHost = CreateInitializerHost(
                Path.Combine(tempRoot, "ConcurrentInitializer" + iteration), fail: false);
            if (!concurrentHost.TryInitializeYanshen(player))
                throw new InvalidOperationException(
                    $"concurrent initializer {iteration} failed");
        });

        Require(plugin.IsInitialized,
            "concurrent RunQuest initializers did not publish initialized state");
        Equal(82, interpreter.ExecuteProcedure("AliasRoundTrip").AsInt(),
            "API call after concurrent initialization");
    });
}
finally
{
    M2Share.PluginManager = null;
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

if (failures.Count != 0)
{
    Console.Error.WriteLine($"FAIL yanshen API access checks={failures.Count}");
    foreach (var failure in failures)
        Console.Error.WriteLine(" - " + failure);
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine(
    "PASS yanshen API access plugin=running+initys switches=missing+off+on " +
    "calls=statement+expression+bare+nested+alias+player+main+wrapper " +
    "source=include+ifdef switches=multi init=rollback+concurrent");
return;

void Check(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex}");
    }
}

static PluginManager CreateYanshenManager(string root)
{
    var runtime = Directory.CreateDirectory(Path.Combine(root, "GS1")).FullName;
    var envir = Directory.CreateDirectory(Path.Combine(root, "Envir")).FullName;
    File.WriteAllText(Path.Combine(runtime, "config.json"), "{}",
        Encoding.GetEncoding(936));
    var manager = new PluginManager(envir, runtime);
    manager.RegisterBuiltinPlugins();
    return manager;
}

static PluginInfo RequiredPlugin(PluginManager manager) =>
    manager.GetPlugin("YanshenCompat") ??
    throw new InvalidOperationException("YanshenCompat was not registered");

static PasInterpreter ParseInterpreter(string path, PasApiBridge bridge)
{
    var source = File.ReadAllText(path);
    var program = new PasParser(
        new PasLexer(source, path), Path.GetDirectoryName(path) ?? string.Empty).Parse();
    return new PasInterpreter(program, bridge);
}

static PasProgram LoadThroughHost(PasScriptHost host, string scriptPath)
{
    // PasScriptHost.cs:2333/2336 declare a (string) and a (string, out string)
    // overload, so a name-only lookup is ambiguous.
    var method = typeof(PasScriptHost).GetMethod("GetOrLoadProgram",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(string) }, null);
    Require(method != null, "PasScriptHost.GetOrLoadProgram reflection target missing");
    var program = method.Invoke(host, new object[] { scriptPath }) as PasProgram;
    return program ?? throw new InvalidOperationException(
        "PasScriptHost failed to parse the include fixture");
}

static void ExpectDiagnostic(Func<PasValue> invocation, string source, string sourceFile,
    string api, string procedure, string callNeedle, string reason)
{
    PasRuntimeException captured = null;
    try
    {
        invocation();
    }
    catch (PasRuntimeException ex)
    {
        captured = ex;
    }

    if (captured == null)
        throw new InvalidOperationException(
            $"expected Yanshen API rejection for {api}, but the call succeeded");

    AssertDiagnosticText(captured.Message, source, sourceFile, api, procedure,
        callNeedle, reason);
    Require(captured.InnerException is YanshenApiUnavailableException,
        $"{api} diagnostic lost its YanshenApiUnavailableException cause");
    Equal(1, Count(captured.ToString(), "API函数找不到 |"),
        $"{api} exception-chain diagnostic count");
}

static void ExpectPlayerDiagnostic(PasApiBridge bridge, TPlayObject player,
    PasInterpreter interpreter, string procedure, string source, string sourceFile,
    string api, string callNeedle, string reason)
{
    using var context = bridge.PushContext(player, null);
    ExpectDiagnostic(() => interpreter.ExecuteProcedure(procedure), source, sourceFile,
        api, procedure, callNeedle, reason);
}

static PasValue ExecuteWithPlayer(PasApiBridge bridge, TPlayObject player,
    PasInterpreter interpreter, string procedure)
{
    using var context = bridge.PushContext(player, null);
    return interpreter.ExecuteProcedure(procedure);
}

static void AssertDiagnosticText(string diagnostic, string source, string sourceFile,
    string api, string procedure, string callNeedle, string reason)
{
    var location = Locate(source, callNeedle);
    Equal(1, Count(diagnostic, "API函数找不到 |"),
        $"{api} diagnostic count");
    Require(!diagnostic.Contains('\r') && !diagnostic.Contains('\n'),
        $"{api} diagnostic must be one line: {diagnostic}");
    Require(diagnostic.Contains($"API={api} |", StringComparison.Ordinal),
        $"diagnostic API mismatch: {diagnostic}");
    Require(diagnostic.Contains($"文件={sourceFile} |", StringComparison.Ordinal),
        $"diagnostic file mismatch: {diagnostic}");
    Require(diagnostic.Contains($"过程/函数={procedure} |", StringComparison.Ordinal),
        $"diagnostic procedure mismatch: {diagnostic}");
    Require(diagnostic.Contains($"行={location.Line} | 列={location.Column} |",
            StringComparison.Ordinal),
        $"diagnostic location mismatch, expected {location.Line}:{location.Column}: {diagnostic}");
    Require(diagnostic.EndsWith("原因=" + reason, StringComparison.Ordinal),
        $"diagnostic reason mismatch: {diagnostic}");
}

static (int Line, int Column) Locate(string source, string needle)
{
    var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n');
    var index = normalized.IndexOf(needle, StringComparison.Ordinal);
    if (index < 0)
        throw new InvalidOperationException($"source needle not found: {needle}");
    var line = 1;
    var column = 1;
    for (var i = 0; i < index; i++)
    {
        if (normalized[i] == '\n')
        {
            line++;
            column = 1;
        }
        else
        {
            column++;
        }
    }
    return (line, column);
}

static int Count(string value, string needle)
{
    var count = 0;
    for (var index = 0; (index = value.IndexOf(needle, index,
             StringComparison.Ordinal)) >= 0; index += needle.Length)
        count++;
    return count;
}

static string BuildDirectSource(string keyPrefix) => $$"""
    program YanshenApiAccessProbe;

    procedure NotInit;
    begin
    end;

    procedure initys;
    begin
    end;

    procedure StatementCall;
    begin
      YSSetG('statement', 1);
    end;

    function ReadStatementValue: Integer;
    begin
      Result := YSGetG('statement');
    end;

    function ExpressionCall: Integer;
    begin
      Result := YSGetG('expression');
    end;

    procedure BareMissingSwitch;
    begin
      YSGetG;
    end;

    procedure BareZeroArgument;
    begin
      YSGetOnlinePlayerNum;
    end;

    function NestedCall: Integer;
    begin
      Result := YSSetG('{{keyPrefix}}-nested-result', YSGetG('nested-input'));
    end;

    function CatchDenied: string;
    begin
      try
        Result := YSGetG('caught');
      except
        on E: Exception do
          Result := ExceptionParam;
      end;
    end;

    function AliasRoundTrip: Integer;
    begin
      YSSetG('nested-input', 41);
      Result := YSGetG('nested-input') + YSGetG('nested-input');
    end;

    function PlayerAliases: Integer;
    begin
      Result := YS_GetFZhong(0) + YS_GetFZhong(1);
    end;

    function ReadMainValue: Integer;
    begin
      Result := YSGetG('{{keyPrefix}}-main');
    end;

    function ReadIncludeValue: Integer;
    begin
      Result := YSGetG('{{keyPrefix}}-include-active');
    end;

    begin
    end.
    """;

static string BuildMainSource(string keyPrefix) => $$"""
    program YanshenMainAccessProbe;

    begin
      YSSetG('{{keyPrefix}}-main', 208);
    end.
    """;

static string BuildWrapperSource() => """
    program AllFucWrapperProbe;

    function YSGetG(Key: string): Integer;
    begin
      Result := 777;
    end;

    function YS_Pick(A, B, C, D: Integer): Integer;
    begin
      Result := 208;
    end;

    function CallShadowedGetG: Integer;
    begin
      Result := YSGetG('shadowed');
    end;

    function CallShadowedPick: Integer;
    begin
      Result := YS_Pick(0, 0, 0, 0);
    end;

    begin
    end.
    """;

static string BuildTunnelSource() => """
    program TunnelAccessProbe;

    function NumericTunnel: Integer;
    begin
      Result := This_Player.GetBagItemCount('!!!!集成函数,36,0$');
    end;

    function CaretTunnel: Integer;
    begin
      Result := This_Player.GetBagItemCount('!!!!爱心分割^13^0$');
    end;

    function ChineseTunnel: Integer;
    begin
      Result := This_Player.GetBagItemCount('!!!!hq取sj戳');
    end;

    function FabricatedTunnel: Integer;
    begin
      Result := This_Player.GetBagItemCount('!!!!plus伤害1:2:3:4:5:6:7:8:');
    end;

    function EmbeddedGive: Boolean;
    begin
      Result := This_Player.Give('AuditMissingItem!!!!1|2|3|4|5|', 1);
    end;

    function Lucker2Tunnel: string;
    begin
      Result := This_Player.GetSignInActPrizer('!!!!^1^1', 'lucker2');
    end;

    function LibmysqlTunnel: string;
    begin
      Result := This_Player.GetSignInActPrizer('SELECT 1', 'libmysql');
    end;

    function CdCompareTunnel: Boolean;
    begin
      Result := ys_CmpTime_min(501, 502, 5000);
    end;

    function CdSetTunnel: Boolean;
    begin
      Result := ys_SetCD_min(501, 502);
    end;

    begin
    end.
    """;

static IncludeFixture CreateIncludeFixture(string root, string keyPrefix)
{
    var envirPath = Directory.CreateDirectory(Path.Combine(root, "IncludeEnvir")).FullName;
    var commonPath = Directory.CreateDirectory(
        Path.Combine(envirPath, "CommonScripts")).FullName;
    File.WriteAllText(Path.Combine(commonPath, "Compiler.inc"),
        "ACCESS_DIAGNOSTIC" + Environment.NewLine, new UTF8Encoding(false));

    var includePath = Path.Combine(commonPath, "AccessProbe.inc");
    var includeSource = $$"""
        function IncludeProbe: Integer;
        begin
        {$IFDEF ACCESS_DIAGNOSTIC}
          Result := YSSetG('{{keyPrefix}}-include-active', 207);
        {$ELSE}
          Result := YSSetG('{{keyPrefix}}-include-inactive', 999);
        {$ENDIF}
        end;
        """;
    File.WriteAllText(includePath, includeSource, new UTF8Encoding(false));

    var mainPath = Path.Combine(commonPath, "IncludeHostProbe.pas");
    var mainSource = """
        program IncludeHostProbe;
        {$I AccessProbe.inc}
        begin
        end.
        """;
    File.WriteAllText(mainPath, mainSource, new UTF8Encoding(false));
    return new IncludeFixture(envirPath, mainPath, includePath, includeSource);
}

static PasScriptHost CreateInitializerHost(string root, bool fail)
{
    var envirPath = Path.Combine(root, "Envir");
    var mapQuestPath = Directory.CreateDirectory(
        Path.Combine(envirPath, "PsMapQuest")).FullName;
    var source = fail ? """
        program RunQuest;

        procedure initys;
        begin
          raise 'initializer failure';
        end;

        begin
        end.
        """ : """
        program RunQuest;

        procedure initys;
        begin
        end;

        begin
        end.
        """;
    File.WriteAllText(Path.Combine(mapQuestPath, "RunQuest.pas"), source,
        new UTF8Encoding(false));
    return new PasScriptHost(envirPath);
}

static void InitializeGameState()
{
    M2Share.g_Config = new GameSvrConfig { nCheckBlock = 0 };
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogSystem = new MirLog();
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

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed record IncludeFixture(string EnvirPath, string MainPath,
    string IncludePath, string IncludeSource);
