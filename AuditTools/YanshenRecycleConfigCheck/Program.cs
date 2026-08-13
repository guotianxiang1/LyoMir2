using System.Text;
using GameSvr;
using GameSvr.Plugins;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
InitializeGameState();

var root = Path.Combine(Path.GetTempPath(),
    "yanshen-recycle-check-" + Guid.NewGuid().ToString("N"));

try
{
    var (manager, runtime) = CreateManager(Path.Combine(root, "valid"), includeRecycle: true);
    var recyclePath = Path.Combine(runtime, "MyJson", "recycle.json");

    Assert(manager.HasValidRecycleConfig, "startup did not publish recycle.json");
    Assert(manager.RecycleConfigItemCount == 3, "startup item count mismatch");
    Assert(manager.IsRecycleItemConfigured("屠龙") &&
           manager.IsRecycleItemConfigured("丹书铁卷"),
        "startup snapshot lost configured item names");

    var plugin = manager.GetPlugin("YanshenCompat");
    plugin.IsInitialized = true;
    var api = new YanshenApi(new TPlayObject(), null, manager);
    // 原生 AutoRecycle 不返回件数：入口 0x1006CF10 在配置无效时 0x1006CF20 返回 -999，
    // 正常出口 0x1006CECC mov eax,1 恒返回 1。
    Assert(api.AutoRecycle() == 1,
        "valid cached config did not execute against an empty bag");

    WriteGbk(recyclePath, "{bad json");
    Assert(!manager.ReloadRecycleConfig(out var syntaxError) &&
           !string.IsNullOrWhiteSpace(syntaxError),
        "malformed JSON unexpectedly reloaded");
    Assert(manager.RecycleConfigItemCount == 3 &&
           manager.IsRecycleItemConfigured("屠龙"),
        "malformed reload replaced the last valid snapshot");
    Assert(api.AutoRecycle() == 1,
        "AutoRecycle stopped consuming the last valid snapshot after a failed reload");

    // 生产 recycle.json 的 可叠材料 指向出厂模板遗留的 "类型2"，而它的 回收类型 段里没有
    // 这个名字。整份判废等于那台服务器一件都回收不了，所以只有那件物品失去结算规则，
    // 其余照常加载；没有结算规则就永远不回收，删不掉也就付不了。
    WriteGbk(recyclePath,
        "{\"物品种类\":{\"屠龙\":\"不存在\",\"怒斩\":\"类型1\"}," +
        "\"回收类型\":{\"类型1\":{\"金币\":1}}}");
    Assert(manager.ReloadRecycleConfig(out var danglingError),
        "undefined recycle type rejected the whole document: " + danglingError);
    Assert(manager.RecycleConfigItemCount == 1 &&
           manager.IsRecycleItemConfigured("怒斩") &&
           !manager.IsRecycleItemConfigured("屠龙"),
        "undefined recycle type did not drop exactly the unresolvable item");
    Assert(api.AutoRecycle() == 1,
        "an item without a settlement rule must never be recycled");

    // 装载校验 0x1009103E / 0x10091056 两键缺一就把有效位 0x1031B8C5 置 0，
    // 而入口 0x1006CF16 一读到 0 就 -999 收工。可叠材料 不算数。
    WriteGbk(recyclePath,
        "{\"可叠材料\":{\"新材料\":\"类型2\"}," +
        "\"回收类型\":{\"类型2\":{\"金币\":1}}}");
    Assert(!manager.ReloadRecycleConfig(out var rootKeyError) &&
           rootKeyError.Contains("物品种类", StringComparison.Ordinal),
        "config without 物品种类 was accepted");

    WriteGbk(recyclePath,
        "{\"物品种类\":{\"屠龙\":\"类型1\"}," +
        "\"回收类型\":{\"类型1\":{\"金币\":1,\"没这个键\":1}}}");
    Assert(!manager.ReloadRecycleConfig(out var schemaError) &&
           schemaError.Contains("回收类型", StringComparison.Ordinal),
        "unknown 回收类型 field was not rejected");
    Assert(manager.RecycleConfigItemCount == 1,
        "schema failure replaced the last valid snapshot");

    WriteGbk(recyclePath,
        "{\"可叠材料\":{\"新材料\":\"类型2\"},\"物品种类\":{}," +
        "\"回收类型\":{\"类型2\":{\"元宝\":20}}}");
    Assert(manager.ReloadRecycleConfig(out var replacementError),
        "valid replacement failed: " + replacementError);
    Assert(manager.RecycleConfigItemCount == 1 &&
           manager.IsRecycleItemConfigured("新材料") &&
           !manager.IsRecycleItemConfigured("屠龙"),
        "valid replacement was not atomically published");

    File.WriteAllBytes(recyclePath, new byte[] { 0x81 });
    Assert(!manager.ReloadRecycleConfig(out var encodingError) &&
           !string.IsNullOrWhiteSpace(encodingError),
        "invalid GBK byte sequence unexpectedly reloaded");
    Assert(manager.IsRecycleItemConfigured("新材料"),
        "encoding failure replaced the last valid snapshot");

    Assert(new YanshenApi(null, null, manager).AutoRecycle() == -999,
        "AutoRecycle exception path did not return -999");

    var (missingManager, _) = CreateManager(
        Path.Combine(root, "missing"), includeRecycle: false);
    missingManager.GetPlugin("YanshenCompat").IsInitialized = true;
    var missingApi = new YanshenApi(new TPlayObject(), null, missingManager);
    Assert(!missingManager.HasValidRecycleConfig && missingApi.AutoRecycle() == -999,
        "missing initial recycle config did not return -999");

    Console.WriteLine(
        "YanshenRecycleConfigCheck PASS startup=GBK schema=validated rootKeys=物品种类+回收类型 " +
        "reload=atomic lastValid=preserved autoRecycle=1/-999");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static (PluginManager Manager, string Runtime) CreateManager(
    string root, bool includeRecycle)
{
    var envir = Path.Combine(root, "Mir200", "Envir");
    var runtime = Path.Combine(root, "Mir200", "GS1");
    Directory.CreateDirectory(envir);
    Directory.CreateDirectory(runtime);
    WriteGbk(Path.Combine(runtime, "config.json"), "{\"高级回收\":1}");

    foreach (var relativePath in new[]
             {
                 Path.Combine("skills", "config.json"),
                 Path.Combine("skills", "skillext.json"),
                 Path.Combine("skills", "monskillext.json"),
                 Path.Combine("roles", "config.json"),
                 "眼神爆率.json",
                 "全区可爆.json",
             })
        WriteGbk(Path.Combine(runtime, "MyJson", relativePath), "{}");

    if (includeRecycle)
    {
        WriteGbk(Path.Combine(runtime, "MyJson", "recycle.json"),
            "{\"可叠材料\":{\"丹书铁卷\":\"类型2\"}," +
            "\"物品种类\":{\"屠龙\":\"类型1\",\"怒斩\":\"类型2\"}," +
            "\"回收类型\":{" +
            "\"类型1\":{\"总开关\":{\"v1\":123,\"v2\":1,\"关闭值\":100},\"金币\":100}," +
            "\"类型2\":{\"元宝\":20}}}");
    }

    var manager = new PluginManager(envir, runtime);
    manager.RegisterBuiltinPlugins();
    Assert(manager.LoadPlugin("YanshenCompat"), "YanshenCompat did not load");
    return (manager, runtime);
}

static void WriteGbk(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, Encoding.GetEncoding(936));
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
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

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
