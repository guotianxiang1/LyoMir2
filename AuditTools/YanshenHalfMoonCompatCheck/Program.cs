using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Plugins;
using SystemModule;

try
{
    Run();
    Console.WriteLine("PASS YanshenHalfMoonCompatCheck gbk=strings formula=enabled fallback=uninitialized+disabled+b<=0");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"YanshenHalfMoonCompatCheck FAIL: {exception}");
    return 1;
}

static void Run()
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();

    var root = Path.Combine(Path.GetTempPath(),
        "loym2-yanshen-halfmoon-" + Guid.NewGuid().ToString("N"));
    try
    {
        var envir = Directory.CreateDirectory(Path.Combine(root, "Mir200", "Envir")).FullName;
        var runtime = Directory.CreateDirectory(Path.Combine(root, "Mir200", "GS1")).FullName;
        var configPath = Path.Combine(runtime, "config.json");
        WriteGbk(configPath,
            "{\r\n" +
            "  \"半月弯刀\": 1,\r\n" +
            "  \"半月弯刀_A值\": \"2\",\r\n" +
            "  \"半月弯刀_B值\": \"15\"\r\n" +
            "}\r\n");

        var gbk = Encoding.GetEncoding(936);
        Assert(Contains(File.ReadAllBytes(configPath), gbk.GetBytes("半月弯刀_A值")),
            "temporary half-moon config was not written as GBK");

        var manager = new PluginManager(envir, runtime);
        manager.RegisterBuiltinPlugins();
        Assert(manager.LoadPlugin("YanshenCompat"),
            "YanshenCompat did not enter Running state");

        var config = manager.GetNativeConfig();
        Assert(config["半月弯刀_A值"] is string a && a == "2",
            "half-moon A lost its native JSON string value");
        Assert(config["半月弯刀_B值"] is string b && b == "15",
            "half-moon B lost its native JSON string value");

        var plugin = manager.GetPlugin("YanshenCompat")
            ?? throw new InvalidOperationException("YanshenCompat was not registered");
        var api = new YanshenApi(null, null, manager);
        const int power = 100;
        const int trainLevel = 3;
        const int skillLevel = 2;
        // Native _Attack sub_769F90 half-moon branch: the divisor is the literal
        // float32 at [0x0076A5C8] = 00 00 70 41 = 15.0
        // (0x0076A160 D8 35 C8 A5 76 00 fdiv dword ptr [0x76A5C8]), never
        // btTrainLv + 10. Expected values are written out so a re-introduced
        // train-level divisor cannot pass.
        const int nativeFallback = 27;      // Round(100 / 15.0 * (2 + 2))
        const int nativeFallbackNoCap = 13; // effective level clamped to 0
        const int nativeFallbackAboveCap = power; // level > 3 swings unscaled

        Assert(!plugin.IsInitialized && !api.IsHalfMoon(),
            "uninitialized Yanshen plugin reported half-moon enabled");
        Equal(nativeFallback, InvokeHelper(power, trainLevel, skillLevel, api),
            "uninitialized plugin must use the native 15.0 divisor");
        Equal(nativeFallbackNoCap, InvokeHelper(power, 0, skillLevel, api),
            "half-moon divisor must not depend on btTrainLv (0x0076A160)");
        // 0x0076A13B 3C 03 cmp al,3 / 0x0076A13D 76 05 jbe / 0x0076A13F mov ebx,[ebp-8]
        Equal(nativeFallbackAboveCap, InvokeHelper(power, 8, 4, api),
            "effective level above 3 must swing at unscaled power");

        plugin.IsInitialized = true;
        Assert(api.IsHalfMoon() && api.HalfMoonA() == 2 && api.HalfMoonB() == 15,
            "GBK half-moon configuration did not reach YanshenApi");
        // Plugin GUI control 0x00030B2C: 默认系数A=2,B=15,伤害倍数=(A+Level)/B
        Equal(27, InvokeHelper(power, trainLevel, skillLevel, api),
            "enabled half-moon formula A=2 B=15 level=2");

        manager.SetNativeConfigValue("半月弯刀", 0L);
        Assert(!api.IsHalfMoon(), "disabled half-moon switch remained enabled");
        Equal(nativeFallback, InvokeHelper(power, trainLevel, skillLevel, api),
            "disabled half-moon switch must fall back to the native formula");

        manager.SetNativeConfigValue("半月弯刀", 1L);
        foreach (var invalidB in new[] { "0", "-1" })
        {
            manager.SetNativeConfigValue("半月弯刀_B值", invalidB);
            Assert(api.HalfMoonB() <= 0, "invalid B value did not reach YanshenApi");
            Equal(nativeFallback, InvokeHelper(power, trainLevel, skillLevel, api),
                "non-positive half-moon B must fall back to the native formula: " + invalidB);
        }
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

static int InvokeHelper(int power, int trainLevel, int skillLevel, YanshenApi api)
{
    var helper = typeof(TBaseObject).GetMethod(
        "CalculateHalfMoonWideAttackPower",
        BindingFlags.Static | BindingFlags.NonPublic,
        binder: null,
        types: new[] { typeof(int), typeof(int), typeof(int), typeof(YanshenApi) },
        modifiers: null)
        ?? throw new MissingMethodException(typeof(TBaseObject).FullName,
            "CalculateHalfMoonWideAttackPower(int, int, int, YanshenApi)");

    return (int)(helper.Invoke(null, new object[] { power, trainLevel, skillLevel, api })
        ?? throw new InvalidOperationException("half-moon helper returned null"));
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

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}
