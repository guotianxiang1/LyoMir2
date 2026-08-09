using System.Text;
using GameSvr.Plugins;

try
{
    Run(args);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"YanshenItemConfigCheck FAIL: {exception}");
    return 1;
}

static void Run(string[] args)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();

    var root = Path.Combine(
        Path.GetTempPath(), "yanshen-item-config-check-" + Guid.NewGuid().ToString("N"));
    try
    {
        var envir = Path.Combine(root, "Mir200", "Envir");
        var runtime = Path.Combine(root, "Mir200", "GS1");
        Directory.CreateDirectory(envir);
        Directory.CreateDirectory(runtime);
        WriteGbk(Path.Combine(runtime, "config.json"), "{}");
        WriteOtherMyJsonFiles(runtime);

        var itemConfigPath = Path.Combine(runtime, "MyJson", "items", "config.json");
        var samplePath = args.Length > 0 ? args[0] : ResolveRealSample();
        if (samplePath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(itemConfigPath)!);
            File.Copy(samplePath, itemConfigPath);
        }
        else
        {
            WriteGbk(itemConfigPath, RealSampleFixture());
        }

        var manager = new PluginManager(envir, runtime);
        Assert(!manager.HasValidItemConfig,
            "items configuration loaded before the Yanshen plugin startup path");
        manager.RegisterBuiltinPlugins();
        Assert(manager.LoadPlugin("YanshenCompat"), "YanshenCompat did not load");
        Assert(manager.HasValidItemConfig, "startup did not publish items/config.json");
        Assert(Path.GetFullPath(manager.ItemConfigPath) == Path.GetFullPath(itemConfigPath),
            "items configuration path is not rooted at GS1\\MyJson\\items");

        var initial = manager.GetItemConfig();
        foreach (var key in RealSampleKeys())
            Assert(initial.ContainsKey(key), "real sample key was not loaded: " + key);
        Assert(initial.Count == RealSampleKeys().Length,
            $"real sample key count changed: {initial.Count}/{RealSampleKeys().Length}");
        Assert(initial["无限背包_额外格子"] is long extraSlots && extraSlots == 144,
            "infinite bag numeric value/type was not loaded");
        Assert(initial["无限背包_是否固定"] is string fixedMode &&
               fixedMode == "V变量控制格子",
            "infinite bag string value/type was not loaded");

        var changes = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["无限背包_额外格子"] = 288L,
            ["拆零关小黑屋_是否删除违规物"] = 1L,
            ["一号元素有值绑定_是否勾选"] = 1L,
            ["二号元素有值禁止穿戴_是否勾选"] = 0L,
            ["沙巴克武器升级防破碎_是否勾选"] = 1L,
            ["聚灵珠解除绑定_是否勾选"] = 0L,
            ["晚出未知布尔"] = true,
            ["晚出未知空值"] = null,
            ["晚出未知对象"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["文字"] = "保留",
                ["数组"] = new List<object> { 7L, "八", false },
            },
        };
        Assert(manager.ApplyItemConfigChanges(changes, out var applyError),
            "item changes failed: " + applyError);

        var applied = manager.GetItemConfig();
        Assert(applied.Count == initial.Count + changes.Keys.Count(key => !initial.ContainsKey(key)),
            "Apply dropped an original or unknown key");
        Assert(RealSampleKeys().All(applied.ContainsKey),
            "Apply dropped a real sample key");
        Assert((long)manager.GetItemConfigValue("无限背包_额外格子") == 288,
            "GetItemConfigValue did not expose the applied infinite bag value");
        Assert(applied["晚出未知布尔"] is true &&
               applied.ContainsKey("晚出未知空值") && applied["晚出未知空值"] == null,
            "unknown bool/null JSON types were not preserved");

        var detachedObject = (Dictionary<string, object>)applied["晚出未知对象"];
        detachedObject["文字"] = "被调用方修改";
        Assert(((Dictionary<string, object>)manager.GetItemConfigValue("晚出未知对象"))["文字"]
                   as string == "保留",
            "GetItemConfig exposed mutable live nested data");

        var savedBytes = File.ReadAllBytes(itemConfigPath);
        Assert(Contains(savedBytes, Encoding.GetEncoding(936).GetBytes("无限背包_额外格子")),
            "saved file is not GBK encoded");
        var savedText = Encoding.GetEncoding(936).GetString(savedBytes);
        Assert(!savedText.Contains("\\u65e0\\u9650", StringComparison.OrdinalIgnoreCase),
            "Chinese item keys were escaped instead of written as GBK text");
        Assert(!File.Exists(itemConfigPath + ".tmp"),
            "atomic-save temporary file remained after success");

        var reloaded = new PluginManager(envir, runtime);
        reloaded.RegisterBuiltinPlugins();
        Assert(reloaded.LoadPlugin("YanshenCompat"), "round-trip YanshenCompat did not load");
        var roundTrip = reloaded.GetItemConfig();
        Assert(roundTrip["无限背包_额外格子"] is long roundTripSlots &&
               roundTripSlots == 288,
            "numeric value/type did not survive GBK round-trip");
        Assert(roundTrip["晚出未知布尔"] is true &&
               roundTrip["晚出未知对象"] is Dictionary<string, object> nested &&
               nested["数组"] is List<object> values &&
               values.Count == 3 && values[0] is long && values[1] is string && values[2] is bool,
            "unknown nested JSON types did not survive round-trip");
        Assert(RealSampleKeys().All(roundTrip.ContainsKey),
            "round-trip dropped a real sample key");

        var beforeFailedApply = File.ReadAllBytes(itemConfigPath);
        Assert(!reloaded.ApplyItemConfigChanges(
                   new Dictionary<string, object> { ["GBK不可编码"] = "emoji:\U0001F642" },
                   out var encodingSaveError) &&
               !string.IsNullOrWhiteSpace(encodingSaveError),
            "unencodable GBK edit unexpectedly saved");
        Assert(beforeFailedApply.SequenceEqual(File.ReadAllBytes(itemConfigPath)) &&
               reloaded.GetItemConfigValue("GBK不可编码") == null,
            "failed atomic save changed the file or active snapshot");
        Assert(!File.Exists(itemConfigPath + ".tmp"),
            "atomic-save temporary file remained after failure");

        WriteGbk(itemConfigPath, "{broken");
        Assert(!reloaded.ReloadItemConfig(out var malformedError) &&
               !string.IsNullOrWhiteSpace(malformedError),
            "malformed JSON unexpectedly reloaded");
        Assert((long)reloaded.GetItemConfigValue("无限背包_额外格子") == 288 &&
               reloaded.GetItemConfig().ContainsKey("晚出未知对象"),
            "malformed reload replaced the last valid snapshot");

        File.WriteAllBytes(itemConfigPath, new byte[] { 0x81 });
        Assert(!reloaded.ReloadItemConfig(out var invalidGbkError) &&
               !string.IsNullOrWhiteSpace(invalidGbkError),
            "invalid GBK unexpectedly reloaded");
        Assert((long)reloaded.GetItemConfigValue("无限背包_额外格子") == 288,
            "invalid-GBK reload replaced the last valid snapshot");

        File.WriteAllBytes(itemConfigPath, savedBytes);
        Assert(reloaded.ReloadItemConfig(out var recoveryError),
            "valid snapshot did not reload after malformed input: " + recoveryError);

        Console.WriteLine(
            "YanshenItemConfigCheck PASS sample=real startup=yes GBK=roundtrip " +
            "types=preserved unknown=preserved save=atomic lastValid=preserved infiniteBag=items");
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

static string ResolveRealSample()
{
    var candidate = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "三龙位", "仙缘复工0.3破白猪+眼神", "云起热更区", "mud2.0", "Mir200", "Gs1",
        "MyJson", "items", "config.json");
    return File.Exists(candidate) ? candidate : null;
}

static void WriteOtherMyJsonFiles(string runtime)
{
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

    WriteGbk(Path.Combine(runtime, "MyJson", "recycle.json"),
        "{\"物品种类\":{},\"回收类型\":{}}");
}

static void WriteGbk(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, Encoding.GetEncoding(936));
}

static bool Contains(byte[] source, byte[] value)
{
    for (var index = 0; index <= source.Length - value.Length; index++)
        if (source.AsSpan(index, value.Length).SequenceEqual(value)) return true;
    return value.Length == 0;
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

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string[] RealSampleKeys() => new[]
{
    "npc卖物倍数_是否勾选",
    "绑定物品允许拾取_是否勾选",
    "拆零关小黑屋_超过数字",
    "拆零关小黑屋_等于数字",
    "拆零关小黑屋_是否勾选",
    "拆零关小黑屋_小黑屋",
    "金币绑定_是否勾选",
    "扣除指定持久物品_是否勾选",
    "拾取物品后触发_是否勾选",
    "无限背包_变量v1",
    "无限背包_变量v2",
    "无限背包_额外格子",
    "无限背包_是否勾选",
    "无限背包_是否固定",
    "物品进背包触发_是否勾选",
    "物品相关选项",
    "眼神扔出物品不绑定_是否勾选",
    "自定义聚灵珠_是否勾选",
    "自定义聚灵珠_收集比例",
    "自定义聚灵珠_消耗比例",
    "自定义聚灵珠_消耗类别",
    "自定义聚灵珠_消耗数量",
};

static string RealSampleFixture() => """
{
   "npc卖物倍数_是否勾选" : 0,
   "绑定物品允许拾取_是否勾选" : 0,
   "拆零关小黑屋_超过数字" : 9999,
   "拆零关小黑屋_等于数字" : 0,
   "拆零关小黑屋_是否勾选" : 1,
   "拆零关小黑屋_小黑屋" : "SD000",
   "金币绑定_是否勾选" : 0,
   "扣除指定持久物品_是否勾选" : 0,
   "拾取物品后触发_是否勾选" : 0,
   "无限背包_变量v1" : 20,
   "无限背包_变量v2" : 188,
   "无限背包_额外格子" : 144,
   "无限背包_是否勾选" : 1,
   "无限背包_是否固定" : "V变量控制格子",
   "物品进背包触发_是否勾选" : 0,
   "物品相关选项" : "物品自定义配置，技术不够的别乱改，详情可关注http://pay.510youxi.com",
   "眼神扔出物品不绑定_是否勾选" : 0,
   "自定义聚灵珠_是否勾选" : 0,
   "自定义聚灵珠_收集比例" : 100,
   "自定义聚灵珠_消耗比例" : 0,
   "自定义聚灵珠_消耗类别" : "金币",
   "自定义聚灵珠_消耗数量" : 10
}
""";
