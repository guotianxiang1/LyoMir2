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
        var legacy = LegacyPower(power, trainLevel, skillLevel);

        Assert(!plugin.IsInitialized && !api.IsHalfMoon(),
            "uninitialized Yanshen plugin reported half-moon enabled");
        Equal(legacy, InvokeHelper(power, trainLevel, skillLevel, api),
            "uninitialized plugin must retain the legacy formula");

        plugin.IsInitialized = true;
        Assert(api.IsHalfMoon() && api.HalfMoonA() == 2 && api.HalfMoonB() == 15,
            "GBK half-moon configuration did not reach YanshenApi");
        Equal(HUtil32.Round(power * (api.HalfMoonA() + skillLevel) / api.HalfMoonB()),
            InvokeHelper(power, trainLevel, skillLevel, api),
            "enabled half-moon formula");

        manager.SetNativeConfigValue("半月弯刀", 0L);
        Assert(!api.IsHalfMoon(), "disabled half-moon switch remained enabled");
        Equal(legacy, InvokeHelper(power, trainLevel, skillLevel, api),
            "disabled half-moon switch must retain the legacy formula");

        manager.SetNativeConfigValue("半月弯刀", 1L);
        foreach (var invalidB in new[] { "0", "-1" })
        {
            manager.SetNativeConfigValue("半月弯刀_B值", invalidB);
            Assert(api.HalfMoonB() <= 0, "invalid B value did not reach YanshenApi");
            Equal(legacy, InvokeHelper(power, trainLevel, skillLevel, api),
                "non-positive half-moon B must retain the legacy formula: " + invalidB);
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

static int LegacyPower(int power, int trainLevel, int skillLevel) =>
    HUtil32.Round(power / (trainLevel + 10) * (skillLevel + 2));

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
