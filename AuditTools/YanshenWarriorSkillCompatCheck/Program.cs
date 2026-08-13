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
        // Player warrior skills run through sub_7707A8, reached only from the
        // client command dispatcher sub_6EC078 whose callers are the two
        // attack-family CM handlers 0x006D9F06 and 0x006D9FA2. Stab divides by
        // the literal float32 at [0x00771D24] = 00 00 A0 40 = 5.0
        // (0x00771C5A D8 35 24 1D 77 00), never btTrainLv + 2. Fire is pure
        // integer imul/idiv 10 (0x0077233C / 0x00772345) with no rounding.
        const int nativeStab = 80;          // Round(100 / 5.0 * (2 + 2))
        const int nativeStabNoCap = 40;     // effective level clamped to 0
        const int nativeStabLevel4 = 110;   // Round(100 * 1.05_80bit) + 5
        const int nativeFire = 130;         // 100 + 100 * 3 / 10

        Assert(!plugin.IsInitialized &&
            !api.IsStabSword() && !api.IsThrusting() && !api.IsFireSword(),
            "uninitialized Yanshen plugin reported a warrior skill enabled");
        Equal(nativeStab, InvokeStabSword(power, trainLevel, skillLevel, api),
            "uninitialized stab-sword must use the native 5.0 divisor");
        Equal(nativeStabNoCap, InvokeStabSword(power, 0, skillLevel, api),
            "stab divisor must not depend on btTrainLv (0x00771C5A)");
        // 0x00771C23 3C 04 cmp al,4 / 0x00771C2A fld tbyte[0x771D18] / 0x00771C39 add edi,5
        Equal(nativeStabLevel4, InvokeStabSword(power, 8, 4, api),
            "effective level 4 must take the 1.05x + 5 branch");
        Equal(nativeHitPlus, InvokeThrusting(nativeHitPlus, skillLevel, api),
            "uninitialized thrusting must retain the native value");
        Equal(nativeFire, InvokeFireSword(power, nativeHitDouble, skillLevel, api),
            "uninitialized fire-sword must use the native formula");
        // idiv truncates: 7 * 15 / 10 = 10, while Round(7/100.0*150) gives 11.
        Equal(17, InvokeFireSword(7, 15, skillLevel, api),
            "fire-sword must truncate, not round (0x00772345 F7 F9 idiv ecx)");

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
        Equal(nativeStab, InvokeStabSword(power, trainLevel, skillLevel, api),
            "disabled stab-sword must fall back to the native formula");
        manager.SetNativeConfigValue("刺杀剑术", 1L);
        foreach (var invalidB in new[] { "0", "-1" })
        {
            manager.SetNativeConfigValue("刺杀剑术_B值", invalidB);
            Assert(api.StabSwordB() <= 0, "invalid stab-sword B value did not reach YanshenApi");
            Equal(nativeStab, InvokeStabSword(power, trainLevel, skillLevel, api),
                "non-positive stab-sword B must fall back to the native formula: " + invalidB);
        }
        manager.SetNativeConfigValue("刺杀剑术_B值", "5");

        Equal(7, InvokeThrusting(nativeHitPlus, skillLevel, api),
            "enabled thrusting formula A=5 level=2");
        // The plugin caps A at 255 before the write (0x100B3F04 cmp eax,0xFF +
        // cmovg), then host 0x0076B02C `04 05 add al,5` adds it in 8 bits and
        // 0x0076B02E `88 83 90 00 00 00` stores the byte. So 300 becomes 255
        // and 255 + level wraps; it does not stick at 255.
        manager.SetNativeConfigValue("攻杀剑术_A值", "300");
        Equal(1, InvokeThrusting(nativeHitPlus, skillLevel, api),
            "thrusting hit bonus must wrap at 256, not saturate (0x0076B02C add al)");
        manager.SetNativeConfigValue("攻杀剑术", 0L);
        Assert(!api.IsThrusting(), "disabled thrusting switch remained enabled");
        Equal(nativeHitPlus, InvokeThrusting(nativeHitPlus, skillLevel, api),
            "disabled thrusting must retain the native m_nHitPlus value");

        Equal(220, InvokeFireSword(power, nativeHitDouble, skillLevel, api),
            "enabled fire-sword formula A=4 B=4 level=2");
        // A only ever becomes a shift count. 200 is not one of the encodable
        // values (0x100B44BB-0x100B44FD handles 0/2/4/8/16), so the staged
        // default `C1 E0 02` survives and the level is multiplied by 4, not by
        // 200. B=100 lands in the imm8 of 0x0076B0EF `04 04`, giving
        // hitDouble = (byte)(2*4 + 100) = 108, and the untouched native chain
        // at 0x0076A06C then yields 100 + Round(100/10.0*108).
        manager.SetNativeConfigValue("烈火剑法_A值", "200");
        manager.SetNativeConfigValue("烈火剑法_B值", "100");
        Equal(1180, InvokeFireSword(power, nativeHitDouble, skillLevel, api),
            "unencodable fire-sword A must fall back to shl eax,2 (0x0076B0EC)");
        manager.SetNativeConfigValue("烈火剑法_A值", "8");
        manager.SetNativeConfigValue("烈火剑法_B值", "4");
        Equal(300, InvokeFireSword(power, nativeHitDouble, skillLevel, api),
            "encodable fire-sword A=8 must select shl eax,3");
        manager.SetNativeConfigValue("烈火剑法", 0L);
        Assert(!api.IsFireSword(), "disabled fire-sword switch remained enabled");
        Equal(nativeFire, InvokeFireSword(power, nativeHitDouble, skillLevel, api),
            "disabled fire-sword must fall back to the native formula");
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
