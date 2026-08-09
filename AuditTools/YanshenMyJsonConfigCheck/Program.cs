using System.Collections.Concurrent;
using System.Text;
using GameSvr.Plugins;

try
{
    Run();
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"YanshenMyJsonConfigCheck FAIL: {exception}");
    return 1;
}

static void Run()
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();

    var root = Path.Combine(
        Path.GetTempPath(), "yanshen-myjson-check-" + Guid.NewGuid().ToString("N"));
    try
    {
        var envir = Path.Combine(root, "Mir200", "Envir");
        var runtime = Path.Combine(root, "Mir200", "GS1");
        Directory.CreateDirectory(envir);
        Directory.CreateDirectory(runtime);
        WriteGbk(Path.Combine(runtime, "config.json"), "{}");

        var expectedPaths = new Dictionary<YanshenMyJsonKind, string>
        {
            [YanshenMyJsonKind.Role] = Path.Combine("roles", "config.json"),
            [YanshenMyJsonKind.SkillConfig] = Path.Combine("skills", "config.json"),
            [YanshenMyJsonKind.SkillExt] = Path.Combine("skills", "skillext.json"),
            [YanshenMyJsonKind.MonsterSkillExt] = Path.Combine("skills", "monskillext.json"),
            [YanshenMyJsonKind.DropRate] = "眼神爆率.json",
            [YanshenMyJsonKind.GuaranteedDrop] = "全区可爆.json",
        };

        WriteGbk(Path.Combine(runtime, "MyJson", expectedPaths[YanshenMyJsonKind.Role]),
            RoleFixture());
        WriteGbk(Path.Combine(runtime, "MyJson", expectedPaths[YanshenMyJsonKind.SkillConfig]),
            "{\"技能主配置\":5}");
        WriteGbk(Path.Combine(runtime, "MyJson", expectedPaths[YanshenMyJsonKind.SkillExt]),
            "{\"技能旧入口\":1}");
        WriteGbk(Path.Combine(runtime, "MyJson", expectedPaths[YanshenMyJsonKind.MonsterSkillExt]),
            "{\"怪物技能旧入口\":2}");
        WriteGbk(Path.Combine(runtime, "MyJson", expectedPaths[YanshenMyJsonKind.DropRate]),
            "{\"爆率旧入口\":3}\r\n}");
        WriteGbk(Path.Combine(runtime, "MyJson", expectedPaths[YanshenMyJsonKind.GuaranteedDrop]),
            "{\"全区旧入口\":4}");
        WriteGbk(Path.Combine(runtime, "MyJson", "items", "config.json"), "{}");
        WriteGbk(Path.Combine(runtime, "MyJson", "recycle.json"),
            "{\"物品种类\":{},\"回收类型\":{}}");

        var manager = new PluginManager(envir, runtime);
        manager.LoadAllMyJsonConfigs();

        Assert(Enum.GetValues<YanshenMyJsonKind>().Length == expectedPaths.Count,
            "kind API does not expose exactly the six independent documents");
        foreach (var (kind, relativePath) in expectedPaths)
        {
            var expected = Path.GetFullPath(Path.Combine(runtime, "MyJson", relativePath));
            Assert(Path.GetFullPath(manager.GetMyJsonConfigPath(kind)) == expected,
                $"wrong path for {kind}");
            Assert(manager.GetMyJsonConfig(kind).Count > 0,
                $"LoadAllMyJsonConfigs did not publish {kind}");
        }

        Assert(manager.GetRoleConfig().ContainsKey("未知对象") &&
               manager.GetSkillExtConfig().ContainsKey("技能旧入口") &&
               manager.GetMonSkillExtConfig().ContainsKey("怪物技能旧入口") &&
               manager.GetDropRateConfig().ContainsKey("爆率旧入口") &&
               manager.GetGuaranteedDropConfig().ContainsKey("全区旧入口"),
            "legacy getters are not backed by the kind API snapshots");

        var strictRoleBefore = manager.GetMyJsonConfig(YanshenMyJsonKind.Role);
        WriteGbk(manager.GetMyJsonConfigPath(YanshenMyJsonKind.Role), "{\"不应兼容\":1}\r\n}");
        Assert(!manager.ReloadMyJsonConfig(YanshenMyJsonKind.Role, out _) &&
               manager.GetMyJsonConfig(YanshenMyJsonKind.Role).ContainsKey("未知对象"),
            "the legacy trailing-brace exception escaped the drop-rate document");
        WriteGbk(manager.GetMyJsonConfigPath(YanshenMyJsonKind.Role), RoleFixture());
        Assert(manager.ReloadMyJsonConfig(YanshenMyJsonKind.Role, out var strictRoleRecoveryError),
            "role fixture did not recover after the strict trailing-brace check: " +
            strictRoleRecoveryError);

        foreach (var (kind, _) in expectedPaths)
        {
            var before = expectedPaths.Keys.ToDictionary(
                candidate => candidate,
                candidate => File.ReadAllBytes(manager.GetMyJsonConfigPath(candidate)));
            var document = manager.GetMyJsonConfig(kind);
            var marker = "kind路由审计_" + kind;
            document[marker] = (long)kind + 100;
            Assert(manager.ApplyMyJsonConfig(kind, document, out var routeError),
                $"{kind} route apply failed: {routeError}");
            Assert(!before[kind].SequenceEqual(File.ReadAllBytes(manager.GetMyJsonConfigPath(kind))),
                $"{kind} route apply did not change its own file");
            foreach (var otherKind in expectedPaths.Keys.Where(candidate => candidate != kind))
                Assert(before[otherKind].SequenceEqual(
                        File.ReadAllBytes(manager.GetMyJsonConfigPath(otherKind))),
                    $"{kind} route apply changed {otherKind}");
        }

        var routeReload = new PluginManager(envir, runtime);
        foreach (var kind in expectedPaths.Keys)
        {
            Assert(routeReload.ReloadMyJsonConfig(kind, out var routeReloadError),
                $"{kind} route result did not reload: {routeReloadError}");
            Assert(routeReload.GetMyJsonConfig(kind).ContainsKey("kind路由审计_" + kind),
                $"{kind} route marker was not persisted in its own document");
        }

        var detached = manager.GetMyJsonConfig(YanshenMyJsonKind.Role);
        var detachedNested = (Dictionary<string, object>)detached["未知对象"];
        detachedNested["字符串"] = "调用方篡改";
        var detachedArray = (List<object>)detachedNested["数组"];
        ((Dictionary<string, object>)detachedArray[3])["深层"] = "调用方篡改";
        AssertNestedDocument(manager.GetMyJsonConfig(YanshenMyJsonKind.Role));

        var completeDocument = manager.GetMyJsonConfig(YanshenMyJsonKind.Role);
        completeDocument["既有开关"] = 2L;
        completeDocument["保存后新字段"] = "仍为GBK";
        Assert(manager.ApplyMyJsonConfig(
                   YanshenMyJsonKind.Role, completeDocument, out var applyError),
            "complete-document apply failed: " + applyError);
        completeDocument["既有开关"] = 99L;
        ((Dictionary<string, object>)completeDocument["未知对象"])["字符串"] = "保存后篡改";
        var applied = manager.GetMyJsonConfig(YanshenMyJsonKind.Role);
        Assert(applied["既有开关"] is long savedSwitch && savedSwitch == 2 &&
               applied["保存后新字段"] is string,
            "applied snapshot was not detached from the caller");
        AssertNestedDocument(applied);

        var rolePath = manager.GetMyJsonConfigPath(YanshenMyJsonKind.Role);
        var savedBytes = File.ReadAllBytes(rolePath);
        Assert(Contains(savedBytes, Encoding.GetEncoding(936).GetBytes("保存后新字段")),
            "saved document is not GBK text");
        Assert(!Encoding.GetEncoding(936).GetString(savedBytes)
                   .Contains("\\u4fdd\\u5b58", StringComparison.OrdinalIgnoreCase),
            "Chinese keys were escaped instead of saved as GBK text");
        Assert(!File.Exists(rolePath + ".tmp"),
            "atomic-save temporary file remained after success");

        var reloaded = new PluginManager(envir, runtime);
        Assert(reloaded.ReloadMyJsonConfig(YanshenMyJsonKind.Role, out var reloadError),
            "saved document did not round-trip: " + reloadError);
        AssertNestedDocument(reloaded.GetMyJsonConfig(YanshenMyJsonKind.Role));

        WriteGbk(rolePath, "{broken");
        Assert(!reloaded.ReloadMyJsonConfig(YanshenMyJsonKind.Role, out var jsonError) &&
               !string.IsNullOrWhiteSpace(jsonError),
            "malformed JSON unexpectedly reloaded");
        reloaded.LoadRoleConfig();
        AssertNestedDocument(reloaded.GetRoleConfig());

        var skillPath = reloaded.GetMyJsonConfigPath(YanshenMyJsonKind.SkillExt);
        Assert(reloaded.ReloadMyJsonConfig(YanshenMyJsonKind.SkillExt, out _),
            "valid skill fixture did not load before bad-GBK check");
        File.WriteAllBytes(skillPath, new byte[] { 0x81 });
        Assert(!reloaded.ReloadMyJsonConfig(YanshenMyJsonKind.SkillExt, out var gbkError) &&
               !string.IsNullOrWhiteSpace(gbkError),
            "invalid GBK unexpectedly reloaded");
        Assert(reloaded.GetSkillExtConfig().ContainsKey("技能旧入口"),
            "bad GBK replaced the last valid snapshot");

        File.WriteAllBytes(rolePath, savedBytes);
        Assert(reloaded.ReloadMyJsonConfig(YanshenMyJsonKind.Role, out var recoveryError),
            "valid role snapshot did not recover: " + recoveryError);
        var beforeFailedSave = File.ReadAllBytes(rolePath);
        var unencodable = reloaded.GetMyJsonConfig(YanshenMyJsonKind.Role);
        unencodable["GBK不可编码"] = "emoji:\U0001F642";
        Assert(!reloaded.ApplyMyJsonConfig(
                   YanshenMyJsonKind.Role, unencodable, out var encodingError) &&
               !string.IsNullOrWhiteSpace(encodingError),
            "unencodable complete document unexpectedly saved");
        Assert(beforeFailedSave.SequenceEqual(File.ReadAllBytes(rolePath)) &&
               !reloaded.GetMyJsonConfig(YanshenMyJsonKind.Role).ContainsKey("GBK不可编码"),
            "failed atomic save changed the file or active snapshot");
        Assert(!File.Exists(rolePath + ".tmp"),
            "atomic-save temporary file remained after failure");

        var concurrencyErrors = new ConcurrentQueue<string>();
        Parallel.For(0, 24, index =>
        {
            var candidate = reloaded.GetMyJsonConfig(YanshenMyJsonKind.Role);
            candidate["并发序号"] = (long)index;
            if (!reloaded.ApplyMyJsonConfig(
                    YanshenMyJsonKind.Role, candidate, out var error))
                concurrencyErrors.Enqueue(error);
            AssertNestedDocument(reloaded.GetMyJsonConfig(YanshenMyJsonKind.Role));
        });
        Assert(concurrencyErrors.IsEmpty, "concurrent apply failed: " +
            string.Join(" | ", concurrencyErrors));
        Assert(reloaded.ReloadMyJsonConfig(YanshenMyJsonKind.Role, out var concurrentReloadError),
            "concurrent writes left an invalid document: " + concurrentReloadError);
        Assert(!File.Exists(rolePath + ".tmp"),
            "concurrent atomic saves left a temporary file");

        Console.WriteLine(
            "YanshenMyJsonConfigCheck PASS kinds=6 routing=isolated legacy=compatible GBK=strict " +
            "lastValid=preserved deepClone=yes nestedTypes=preserved save=atomic threadSafe=yes");
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

static void AssertNestedDocument(Dictionary<string, object> document)
{
    Assert(document["未知对象"] is Dictionary<string, object> nested &&
           nested["字符串"] is string text && text == "保留" &&
           nested["整数"] is long integer && integer == 7 &&
           nested["小数"] is decimal number && number == 1.25m &&
           nested["布尔"] is true &&
           nested.ContainsKey("空值") && nested["空值"] == null &&
           nested["数组"] is List<object> values &&
           values.Count == 4 && values[0] is long && values[1] is string &&
           values[2] is bool &&
           values[3] is Dictionary<string, object> deep &&
           deep["深层"] as string == "值",
        "unknown nested JSON structure or value types were not preserved");
}

static string RoleFixture() => """
{
  "既有开关": 1,
  "未知对象": {
    "字符串": "保留",
    "整数": 7,
    "小数": 1.25,
    "布尔": true,
    "空值": null,
    "数组": [1, "二", false, { "深层": "值" }]
  }
}
""";

static bool Contains(byte[] source, byte[] value)
{
    for (var index = 0; index <= source.Length - value.Length; index++)
        if (source.AsSpan(index, value.Length).SequenceEqual(value)) return true;
    return value.Length == 0;
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
    var shareDirectory = Path.Combine(
        Path.GetFullPath(Path.Combine(runtimeDirectory, "..")), "Share");
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
