using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Plugins;
using SystemModule;

try
{
    Run();
    Console.WriteLine("PASS YanshenWarriorSkillCompatCheck gbk=strings formula=enabled fallback=uninitialized+disabled+invalid-stab-b caps=thrusting+fire");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"YanshenWarriorSkillCompatCheck FAIL: {exception}");
    return 1;
}

static void Run()
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();

    var root = Path.Combine(Path.GetTempPath(),
        "loym2-yanshen-warrior-skills-" + Guid.NewGuid().ToString("N"));
    try
    {
        var envir = Directory.CreateDirectory(Path.Combine(root, "Mir200", "Envir")).FullName;
        var runtime = Directory.CreateDirectory(Path.Combine(root, "Mir200", "GS1")).FullName;
        var configPath = Path.Combine(runtime, "config.json");
        WriteGbk(configPath,
            "{\r\n" +
            "  \"刺杀剑术\": 1,\r\n" +
            "  \"刺杀剑术_A值\": \"2\",\r\n" +
            "  \"刺杀剑术_B值\": \"5\",\r\n" +
            "  \"攻杀剑术\": 1,\r\n" +
            "  \"攻杀剑术_A值\": \"5\",\r\n" +
            "  \"烈火剑法\": 1,\r\n" +
            "  \"烈火剑法_A值\": \"4\",\r\n" +
            "  \"烈火剑法_B值\": \"4\"\r\n" +
            "}\r\n");

        var gbk = Encoding.GetEncoding(936);
        Assert(Contains(File.ReadAllBytes(configPath), gbk.GetBytes("刺杀剑术_A值")),
            "temporary warrior-skill config was not written as GBK");

        var manager = new PluginManager(envir, runtime);
        manager.RegisterBuiltinPlugins();
        Assert(manager.LoadPlugin("YanshenCompat"),
            "YanshenCompat did not enter Running state");

        var plugin = manager.GetPlugin("YanshenCompat")
            ?? throw new InvalidOperationException("YanshenCompat was not registered");
        var api = new YanshenApi(null, null, manager);
        const int power = 100;
        const int trainLevel = 3;
        const int skillLevel = 2;
        const int nativeHitPlus = 19;
        const int nativeHitDouble = 3;
        var legacyStab = LegacyStabSwordPower(power, trainLevel, skillLevel);
        var legacyFire = LegacyFireSwordPower(power, nativeHitDouble);

        Assert(!plugin.IsInitialized &&
            !api.IsStabSword() && !api.IsThrusting() && !api.IsFireSword(),
            "uninitialized Yanshen plugin reported a warrior skill enabled");
        Equal(legacyStab, InvokeStabSword(power, trainLevel, skillLevel, api),
            "uninitialized stab-sword must retain the legacy formula");
        Equal(nativeHitPlus, InvokeThrusting(nativeHitPlus, skillLevel, api),
            "uninitialized thrusting must retain the legacy value");
        Equal(legacyFire, InvokeFireSword(power, nativeHitDouble, skillLevel, api),
            "uninitialized fire-sword must retain the legacy formula");

        plugin.IsInitialized = true;
        Assert(api.IsStabSword() && api.StabSwordA() == 2 && api.StabSwordB() == 5,
            "GBK stab-sword configuration did not reach YanshenApi");
        Assert(api.IsThrusting() && api.ThrustingA() == 5,
            "GBK thrusting configuration did not reach YanshenApi");
        Assert(api.IsFireSword() && api.FireSwordA() == 4 && api.FireSwordB() == 4,
            "GBK fire-sword configuration did not reach YanshenApi");

        Equal(80, InvokeStabSword(power, trainLevel, skillLevel, api),
            "enabled stab-sword formula A=2 B=5 level=2");
        manager.SetNativeConfigValue("刺杀剑术", 0L);
        Assert(!api.IsStabSword(), "disabled stab-sword switch remained enabled");
        Equal(legacyStab, InvokeStabSword(power, trainLevel, skillLevel, api),
            "disabled stab-sword must retain the legacy formula");
        manager.SetNativeConfigValue("刺杀剑术", 1L);
        foreach (var invalidB in new[] { "0", "-1" })
        {
            manager.SetNativeConfigValue("刺杀剑术_B值", invalidB);
            Assert(api.StabSwordB() <= 0, "invalid stab-sword B value did not reach YanshenApi");
            Equal(legacyStab, InvokeStabSword(power, trainLevel, skillLevel, api),
                "non-positive stab-sword B must retain the legacy formula: " + invalidB);
        }
        manager.SetNativeConfigValue("刺杀剑术_B值", "5");

        Equal(7, InvokeThrusting(nativeHitPlus, skillLevel, api),
            "enabled thrusting formula A=5 level=2");
        manager.SetNativeConfigValue("攻杀剑术_A值", "300");
        Equal(255, InvokeThrusting(nativeHitPlus, skillLevel, api),
            "thrusting hit bonus must be capped at 255");
        manager.SetNativeConfigValue("攻杀剑术", 0L);
        Assert(!api.IsThrusting(), "disabled thrusting switch remained enabled");
        Equal(nativeHitPlus, InvokeThrusting(nativeHitPlus, skillLevel, api),
            "disabled thrusting must retain the legacy value");

        Equal(220, InvokeFireSword(power, nativeHitDouble, skillLevel, api),
            "enabled fire-sword formula A=4 B=4 level=2");
        manager.SetNativeConfigValue("烈火剑法_A值", "200");
        manager.SetNativeConfigValue("烈火剑法_B值", "100");
        Equal(2550, InvokeFireSword(power, nativeHitDouble, skillLevel, api),
            "fire-sword multiplier must be capped at 25.5");
        manager.SetNativeConfigValue("烈火剑法", 0L);
        Assert(!api.IsFireSword(), "disabled fire-sword switch remained enabled");
        Equal(legacyFire, InvokeFireSword(power, nativeHitDouble, skillLevel, api),
            "disabled fire-sword must retain the legacy formula");
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

static int InvokeStabSword(int power, int trainLevel, int skillLevel, YanshenApi api) =>
    InvokeHelper("CalculateStabSwordLongAttackPower",
        new[] { typeof(int), typeof(int), typeof(int), typeof(YanshenApi) },
        power, trainLevel, skillLevel, api);

static int InvokeThrusting(int nativeHitPlus, int skillLevel, YanshenApi api) =>
    InvokeHelper("CalculateThrustingHitPlus",
        new[] { typeof(int), typeof(int), typeof(YanshenApi) },
        nativeHitPlus, skillLevel, api);

static int InvokeFireSword(int power, int nativeHitDouble, int skillLevel, YanshenApi api) =>
    InvokeHelper("CalculateFireSwordAttackPower",
        new[] { typeof(int), typeof(int), typeof(int), typeof(YanshenApi) },
        power, nativeHitDouble, skillLevel, api);

static int InvokeHelper(string name, Type[] parameterTypes, params object[] arguments)
{
    var helper = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic,
        binder: null,
        types: parameterTypes,
        modifiers: null)
        ?? throw new MissingMethodException(typeof(TBaseObject).FullName, name);

    return (int)(helper.Invoke(null, arguments)
        ?? throw new InvalidOperationException(name + " returned null"));
}

static int LegacyStabSwordPower(int power, int trainLevel, int skillLevel) =>
    HUtil32.Round(power / (trainLevel + 2) * (skillLevel + 2));

static int LegacyFireSwordPower(int power, int nativeHitDouble) =>
    power + HUtil32.Round(power / 100 * (nativeHitDouble * 10));

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
