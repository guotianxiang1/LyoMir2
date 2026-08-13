using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using GameSvr;
using GameSvr.Plugins;
using SystemModule;

try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();
    VerifyApiNormalizationAndState();
    VerifyHeroSelection();
    VerifySourceContracts();

    Console.WriteLine(
        "PASS YanshenHeroCastCommand28Check " +
        "handler=magic-positive+clamp255+return-normalized-isRun " +
        "state=ordinal-player-key+one-shot-clear+continuous-retain " +
        "consumer=mage-tao+learned-magic+host-target+unknown-blocks-fallback " +
        "lifecycle=pending-through-hero-absence");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"YanshenHeroCastCommand28Check FAIL: {exception}");
    return 1;
}

static void VerifyApiNormalizationAndState()
{
    var root = Path.Combine(Path.GetTempPath(),
        "loym2-yanshen-command28-" + Guid.NewGuid().ToString("N"));
    try
    {
        var envir = Directory.CreateDirectory(
            Path.Combine(root, "Mir200", "Envir")).FullName;
        var runtime = Directory.CreateDirectory(
            Path.Combine(root, "Mir200", "GS1")).FullName;
        File.WriteAllText(Path.Combine(runtime, "config.json"),
            "{\r\n" +
            "  \"指定英雄放技能\": 1,\r\n" +
            "  \"英雄施法速度\": 0\r\n" +
            "}\r\n", Encoding.GetEncoding(936));

        var manager = new PluginManager(envir, runtime);
        manager.RegisterBuiltinPlugins();
        Assert(manager.LoadPlugin("YanshenCompat"),
            "YanshenCompat did not enter Running state");
        var plugin = manager.GetPlugin("YanshenCompat")
            ?? throw new InvalidOperationException(
                "YanshenCompat was not registered");
        plugin.IsInitialized = true;

        var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
            typeof(TPlayObject));
        player.m_sCharName = Unique("api");
        var api = new YanshenApi(player, null, manager);

        Equal(-1, api.HeroCastSkill(0, 1),
            "non-positive magic result");
        Assert(!YanshenHeroCastState.TryPeek(player.m_sCharName,
            out _, out _), "invalid magic created state");

        Equal(0, api.HeroCastSkill(300, 0),
            "one-shot return must be normalized isRun");
        Peek(player.m_sCharName, 255, 0, "magic clamp");
        Consume(player.m_sCharName, 255, "one-shot first consume");
        Assert(!YanshenHeroCastState.TryConsume(player.m_sCharName, out _),
            "one-shot command was not cleared");

        Equal(1, api.HeroCastSkill(58, 7),
            "positive isRun normalization");
        Peek(player.m_sCharName, 58, 1, "continuous state");
        Consume(player.m_sCharName, 58, "continuous first consume");
        Consume(player.m_sCharName, 58, "continuous second consume");

        Equal(-1, api.HeroCastSkill(59, -1),
            "negative isRun return preservation");
        Peek(player.m_sCharName, 59, 255, "negative isRun low byte");
        Equal(-1, api.HeroCastSkill(-8, 0),
            "invalid magic must still return -1");
        Peek(player.m_sCharName, 59, 255,
            "invalid magic must not overwrite prior command");
        Consume(player.m_sCharName, 59,
            "negative nonzero isRun must remain continuous");

        Equal(int.MinValue, api.HeroCastSkill(60, int.MinValue),
            "non-positive isRun full return preservation");
        Peek(player.m_sCharName, 60, 0,
            "isRun stores only its low byte");
        Consume(player.m_sCharName, 60, "low-byte zero one-shot");
        Assert(!YanshenHeroCastState.TryConsume(player.m_sCharName, out _),
            "low-byte zero state was not cleared");
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

static void VerifyHeroSelection()
{
    var select = typeof(HeroObject).GetMethod("TrySelectCommandedMagic",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(HeroObject).FullName,
            "TrySelectCommandedMagic");

    var learned = new TUserMagic
    {
        MagicInfo = new TMagic { wMagicID = 77, sMagicName = "LEARNED" },
        wMagIdx = 77,
        btLevel = 4
    };
    var hero = (HeroObject)RuntimeHelpers.GetUninitializedObject(
        typeof(HeroObject));
    hero.m_btJob = M2Share.jWizard;
    hero.m_HeroMagicList = new List<TUserMagic> { learned };
    var master = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TPlayObject));
    master.m_sCharName = Unique("hero");

    Equal(0, YanshenHeroCastState.Set(master.m_sCharName, 77, 0),
        "one-shot state return");
    var selected = InvokeSelect(select, hero, master, out var magic);
    Assert(selected && ReferenceEquals(learned, magic),
        "selector did not return the hero's learned TUserMagic instance");
    Equal(4, magic.btLevel, "learned skill level was replaced");
    Assert(!InvokeSelect(select, hero, master, out magic) && magic == null,
        "one-shot selection did not fall back after clearing");

    Equal(1, YanshenHeroCastState.Set(master.m_sCharName, 77, 1),
        "continuous state return");
    Assert(InvokeSelect(select, hero, master, out magic) &&
           ReferenceEquals(learned, magic),
        "continuous first selection");
    Assert(InvokeSelect(select, hero, master, out magic) &&
           ReferenceEquals(learned, magic),
        "continuous second selection");

    var invalidName = Unique("invalid");
    master.m_sCharName = invalidName;
    YanshenHeroCastState.Set(invalidName, 88, 0);
    Assert(InvokeSelect(select, hero, master, out magic) && magic == null,
        "invalid learned magic must consume as an explicit override");
    Assert(!InvokeSelect(select, hero, master, out magic),
        "invalid one-shot override was not cleared");

    var continuousInvalidName = Unique("continuous-invalid");
    master.m_sCharName = continuousInvalidName;
    YanshenHeroCastState.Set(continuousInvalidName, 89, 1);
    Assert(InvokeSelect(select, hero, master, out magic) && magic == null,
        "continuous invalid override did not block first fallback");
    Assert(InvokeSelect(select, hero, master, out magic) && magic == null,
        "continuous invalid override did not block repeated fallback");
    Peek(continuousInvalidName, 89, 1,
        "continuous invalid override must remain pending");

    var pendingName = Unique("absent");
    YanshenHeroCastState.Set(pendingName, 77, 0);
    Peek(pendingName, 77, 0,
        "request must survive while no hero consumer exists");
    master.m_sCharName = pendingName;
    hero.m_btJob = M2Share.jWarr;
    Assert(!InvokeSelect(select, hero, master, out magic),
        "warrior hero consumed mage/tao-only override");
    Peek(pendingName, 77, 0,
        "warrior hero changed pending override");
    hero.m_btJob = M2Share.jTaos;
    Assert(InvokeSelect(select, hero, master, out magic) &&
           ReferenceEquals(learned, magic),
        "tao hero did not consume pending override");

    var exactName = Unique("CaseName");
    YanshenHeroCastState.Set(exactName, 77, 0);
    Assert(!YanshenHeroCastState.TryConsume(exactName.ToLowerInvariant(),
        out _), "player-name key was not ordinal/case-sensitive");
    Consume(exactName, 77, "exact player-name key");
}

static void VerifySourceContracts()
{
    var root = FindRepositoryRoot();
    var api = File.ReadAllText(Path.Combine(root,
        "GameSvr", "Plugins", "YanshenApi.cs"));
    var apiMethod = SliceMethod(api, "public int HeroCastSkill(");
    Assert(apiMethod.Contains("Enabled(\"指定英雄放技能\")") &&
           apiMethod.Contains("YanshenHeroCastState.Set(") &&
           !apiMethod.Contains("MagicManager.DoSpell") &&
           !apiMethod.Contains("btLevel = 3"),
        "HeroCastSkill still performs the old player/fixed-level cast");

    var commands = File.ReadAllText(Path.Combine(root,
        "GameSvr", "Plugins", "YanshenCommands.cs"));
    Assert(commands.Contains("[28]=\"指定英雄放技能\"") &&
           commands.Contains("28 => _api.HeroCastSkill("),
        "tunnel command 28 feature/dispatch routing");

    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.Yanshen.cs"));
    // 2.08 AllFuc.pas declares Ys_SetHeroCSkill; the earlier `ys_setheroskill` /
    // `herocastskill` spellings were C#-invented and are gone, so this pins the
    // authentic name instead of the old literal argument list.
    var heroCastFeature = SliceMethod(bridge,
        "private static IReadOnlyDictionary<string, string[]> BuildYanshenApiFeatures()");
    Assert(heroCastFeature.Contains("\"指定英雄放技能\"") &&
           heroCastFeature.Contains("\"ys_setherocskill\"") &&
           !heroCastFeature.Contains("\"ys_setheroskill\"") &&
           !heroCastFeature.Contains("\"herocastskill\"") &&
           bridge.Contains("case \"ys_setherocskill\":") &&
           bridge.Contains("api.HeroCastSkill("),
        "direct Pascal command 28 feature/dispatch routing");

    var hero = File.ReadAllText(Path.Combine(root,
        "GameSvr", "Actors", "HeroObject.cs"));
    var selector = SliceMethod(hero,
        "private bool TrySelectCommandedMagic(");
    Assert(selector.Contains("m_btJob != M2Share.jWizard") &&
           selector.Contains("m_btJob != M2Share.jTaos") &&
           selector.Contains("FindHeroMagicById(magicId)"),
        "hero command selector job/learned-skill contract");
    var tryCast = SliceMethod(hero, "private void TryCastSkill(");
    Assert(tryCast.Contains(
               "if (TrySelectCommandedMagic(master, out var commandedMagic))") &&
           tryCast.Contains("TryReleaseHeroMagic(commandedMagic") &&
           tryCast.IndexOf("return;", tryCast.IndexOf(
               "TryReleaseHeroMagic(commandedMagic",
               StringComparison.Ordinal), StringComparison.Ordinal) >= 0,
        "commanded unknown magic no-fallback contract");
    var release = SliceMethod(hero,
        "private bool TryReleaseHeroMagic(");
    // Native hero cast sub_71BB8C reaches the same fused power helper the player
    // path uses (sub_4C8648 -> sub_4C8658 @0x71BD51/@0x71C13F), which draws the
    // MPow roll INSIDE itself and divides by the hardcoded float32 4.0 at
    // [0x4C86B8]. So the hero must call Magic.GetPower(userMagic) with no separate
    // Magic.MPow pre-roll (that would double-draw RandSeed) and must never divide
    // by btTrainLv — in native that field is only a level cap.
    // See staging/spellpower_formula_exact_20260803.md.
    Assert(release.Contains("GetHeroSpellPoint(userMagic)") &&
           release.Contains("Magic.GetPower(userMagic)") &&
           !release.Contains("Magic.MPow(") &&
           !release.Contains("btTrainLv") &&
           release.Contains("m_TargetCret.GetMagStruckDamage(this") &&
           !release.Contains("MagicManager.DoSpell"),
        "hero caster/level/current-target contract");
}

static bool InvokeSelect(MethodInfo method, HeroObject hero,
    TPlayObject master, out TUserMagic magic)
{
    object[] args = { master, null };
    var result = (bool)method.Invoke(hero, args);
    magic = (TUserMagic)args[1];
    return result;
}

static void Peek(string name, int expectedMagic, int expectedRepeat,
    string label)
{
    Assert(YanshenHeroCastState.TryPeek(name, out var magic,
        out var repeat), label + " missing");
    Equal(expectedMagic, magic, label + " magic");
    Equal(expectedRepeat, repeat, label + " repeat");
}

static void Consume(string name, int expectedMagic, string label)
{
    Assert(YanshenHeroCastState.TryConsume(name, out var magic),
        label + " missing");
    Equal(expectedMagic, magic, label + " magic");
}

static string SliceMethod(string source, string marker)
{
    var start = source.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0) throw new InvalidOperationException("Missing " + marker);
    var brace = source.IndexOf('{', start);
    var depth = 0;
    for (var index = brace; index < source.Length; index++)
    {
        if (source[index] == '{') depth++;
        if (source[index] != '}' || --depth != 0) continue;
        return source[start..(index + 1)];
    }
    throw new InvalidOperationException("Unclosed " + marker);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory, AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName,
                    "GameSvr")) &&
                Directory.Exists(Path.Combine(directory.FullName,
                    "AuditTools")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException(
        "GameSvr and AuditTools repository root was not found");
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

static string Unique(string prefix) =>
    prefix + "-" + Guid.NewGuid().ToString("N");

static void Equal(int expected, int actual, string label)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{label}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
