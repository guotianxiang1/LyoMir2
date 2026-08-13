using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

var gameDirectory = ResolveGameSvrBuild(args);
if (gameDirectory == null)
{
    Console.Error.WriteLine("INCOMPLETE: no GameSvr build directory was supplied and "
        + "none was found under GameSvr/bin. "
        + "Usage: NativeQuickDrugType60CompatCheck [GameSvr build]");
    Environment.Exit(2);
}

PrepareRuntimeConfig();

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var dependency = Path.Combine(gameDirectory, $"{name.Name}.dll");
    return File.Exists(dependency)
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency)
        : null;
};

var systemModule = AssemblyLoadContext.Default.LoadFromAssemblyPath(
    Path.Combine(gameDirectory, "SystemModule.dll"));
var gameSvr = AssemblyLoadContext.Default.LoadFromAssemblyPath(
    Path.Combine(gameDirectory, "GameSvr.dll"));

var globalType = TypeOf(systemModule, "SystemModule.Grobal2");
var userItemType = TypeOf(systemModule, "SystemModule.TUserItem");
var shareType = TypeOf(gameSvr, "GameSvr.M2Share");
var objectManagerType = TypeOf(gameSvr, "GameSvr.ObjectManager");
var userEngineType = TypeOf(gameSvr, "GameSvr.UserEngine");
var goodItemType = TypeOf(gameSvr, "GameSvr.GoodItem");
var baseObjectType = TypeOf(gameSvr, "GameSvr.TBaseObject");
var playerType = TypeOf(gameSvr, "GameSvr.TPlayObject");
var heroType = TypeOf(gameSvr, "GameSvr.HeroObject");

SetStaticMember(shareType, "ObjectManager",
    Activator.CreateInstance(objectManagerType)!);
SetStaticMember(shareType, "ProcessMsgCriticalSection", new object());
SetStaticMember(shareType, "LogMsgCriticalSection", new object());
var userEngine = Activator.CreateInstance(userEngineType)!;
SetStaticMember(shareType, "UserEngine", userEngine);

var calculate = FindNamedMethod(baseObjectType,
    "CalculateNativeQuickDrugRestore", BindingFlags.Static | BindingFlags.NonPublic);
var apply = FindNamedMethod(baseObjectType,
    "TryApplyNativeQuickDrug", BindingFlags.Instance | BindingFlags.NonPublic);
var buildAbility = FindNamedMethod(baseObjectType,
    "BuildNativeAbilityPacket", BindingFlags.Instance | BindingFlags.NonPublic);
var applyTimed = FindNamedMethod(baseObjectType,
    "ApplyTimedAbilityBonuses", BindingFlags.Instance | BindingFlags.NonPublic);
var supported = FindNamedMethod(baseObjectType,
    "IsSupportedTimedAbilityType", BindingFlags.Static | BindingFlags.NonPublic);

CheckFormulaMatrix();
CheckApplySemantics();
CheckSharedPlayerHeroDispatch();
CheckTimedType60();
CheckEquipmentAggregation();
CheckAbilityPacket();

Console.WriteLine(
    "PASS quick-drug=0x784FB4 jobs=0/1/2/3 threshold=10000 " +
    "overflow=consume-without-refresh states=62/102 actors=player+hero " +
    "type60=internal92 packet=A0-MP/A2-HP/A4-job");
return;

void CheckFormulaMatrix()
{
    Equal((457, 122), Calculate(137, 83, 23, 47, 211, 0),
        "job0 integer branch");
    Equal((168, 297), Calculate(137, 83, 23, 47, 211, 1),
        "job1 integer branch");
    Equal((313, 209), Calculate(137, 83, 23, 47, 211, 2),
        "job2 half-carrier branch");
    Equal((313, 209), Calculate(137, 83, 23, 47, 211, 3),
        "job3 half-carrier branch");

    Equal((707, 11), Calculate(7, 11, 3, 5, 10000, 0),
        "carrier 10000 must remain on integer branch");

    Equal((13838, 248), Calculate(137, 83, 99, 199, 10001, 0),
        "job0 x87 curve branch");
    Equal((272, 8466), Calculate(137, 83, 99, 199, 10001, 1),
        "job1 x87 curve branch");
    Equal((6987, 4316), Calculate(137, 83, 99, 199, 10001, 2),
        "job2 x87 truncation branch");
    Equal((6987, 4316), Calculate(137, 83, 99, 199, 10001, 3),
        "job3 x87 truncation branch");

    Equal((137, 83), Calculate(137, 83, 65535, 65535, 65535, 9),
        "unknown job native base-value fallback");
    Equal((-16735672, 10), Calculate(65535, 10, 29900, 0, 10000, 0),
        "native signed imul overflow");
}

void CheckApplySemantics()
{
    var item = NewGoodItem(100, 50);
    var actor = NewActor(baseObjectType, hp: 10, mp: 20, maxHp: 1000, maxMp: 1000);
    SetMember(baseObjectType, actor, "m_btJob", (byte)9);

    InvokeState(actor, "SetNativeActiveState", 102);
    var used = Apply(actor, item, out var refresh);
    Assert(used && refresh, "state102 quick drug result/refresh");
    Equal(60, Ability(actor, "HP"), "state102 HP halving");
    Equal(45, Ability(actor, "MP"), "state102 MP halving");

    InvokeState(actor, "ClearNativeActiveState", 102);
    SetAbility(actor, hp: 980, mp: 990, maxHp: 1000, maxMp: 1000);
    used = Apply(actor, item, out refresh);
    Assert(used && refresh, "quick drug cap result/refresh");
    Equal(1000, Ability(actor, "HP"), "quick drug HP cap");
    Equal(1000, Ability(actor, "MP"), "quick drug MP cap");

    var overflowItem = NewGoodItem(65535, 10);
    SetAbility(actor, hp: 100, mp: 200, maxHp: int.MaxValue, maxMp: int.MaxValue);
    SetMember(baseObjectType, actor, "m_btJob", (byte)0);
    SetMember(baseObjectType, actor, "m_wNativeDrugHealthBonus", (ushort)29900);
    SetMember(baseObjectType, actor, "m_wNativeDrugSpellBonus", (ushort)0);
    SetMember(baseObjectType, actor, "m_wNativeDrugJobBonus", (ushort)10000);
    used = Apply(actor, overflowItem, out refresh);
    Assert(used && !refresh,
        "negative native overflow must consume without ability refresh");
    Equal(100, Ability(actor, "HP"), "negative overflow changed HP");
    Equal(200, Ability(actor, "MP"), "negative overflow changed MP");

    SetAbility(actor, hp: 300, mp: 400, maxHp: 1000, maxMp: 1000);
    InvokeState(actor, "SetNativeActiveState", 62);
    var beforeMessages = ((IList)GetMember(baseObjectType, actor, "m_MsgList")).Count;
    used = Apply(actor, item, out refresh);
    Assert(!used && !refresh, "state62 must reject quick drug");
    Equal(300, Ability(actor, "HP"), "state62 changed HP");
    Equal(400, Ability(actor, "MP"), "state62 changed MP");

    var messages = (IList)GetMember(baseObjectType, actor, "m_MsgList");
    Equal(beforeMessages + 1, messages.Count, "state62 message count");
    var message = messages[messages.Count - 1]!;
    Equal((int)globalType.GetField("RM_SYSMESSAGE")!.GetRawConstantValue()!,
        (int)GetMember(message.GetType(), message, "wIdent"), "state62 message ident");
    Equal(0xFF, (int)GetMember(message.GetType(), message, "nParam1"),
        "state62 foreground color");
    Equal(0x38, (int)GetMember(message.GetType(), message, "nParam2"),
        "state62 background color");
    Equal("你被凝冰,无法使用",
        (string)GetMember(message.GetType(), message, "Buff"), "state62 exact text");
}

void CheckSharedPlayerHeroDispatch()
{
    var playerUse = FindNamedMethod(playerType, "UseNativeQuickDrug",
        BindingFlags.Instance | BindingFlags.NonPublic);
    var heroUse = FindNamedMethod(heroType, "ClientHeroUseItem",
        BindingFlags.Instance | BindingFlags.Public);
    Assert(Calls(playerUse, apply), "player quick path does not call shared helper");
    Assert(Calls(heroUse, apply), "hero quick path does not call shared helper");

    var item = NewGoodItem(17, 29);
    var player = NewActor(playerType, 100, 200, 1000, 1000);
    var hero = NewActor(heroType, 100, 200, 1000, 1000);
    foreach (var actor in new[] { player, hero })
    {
        SetMember(baseObjectType, actor, "m_btJob", (byte)2);
        SetMember(baseObjectType, actor, "m_wNativeDrugHealthBonus", (ushort)31);
        SetMember(baseObjectType, actor, "m_wNativeDrugSpellBonus", (ushort)43);
        SetMember(baseObjectType, actor, "m_wNativeDrugJobBonus", (ushort)79);
        Assert(Apply(actor, item, out var refresh) && refresh,
            $"{actor.GetType().Name} shared quick apply");
    }
    Equal(Ability(player, "HP"), Ability(hero, "HP"),
        "player/hero shared HP result");
    Equal(Ability(player, "MP"), Ability(hero, "MP"),
        "player/hero shared MP result");

    var slowUse = FindNamedMethod(playerType, "UseNativeSlowDrug",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(!Calls(slowUse, apply), "slow drug was routed through quick-only helper");
}

void CheckTimedType60()
{
    Assert((bool)supported.Invoke(null, new object[] { 60 })!,
        "script type60 admission");

    var actor = NewActor(baseObjectType, 1, 1, 10, 10);
    baseObjectType.GetMethod("AddTimedAbility")!
        .Invoke(actor, new object[] { 60, 10, 30 });
    Assert((bool)baseObjectType.GetMethod("HasTimedAbility")!
        .Invoke(actor, new object[] { 60 })!, "type60 node");
    Assert((bool)baseObjectType.GetMethod("HasNativeActiveState")!
        .Invoke(actor, new object[] { 92 })!, "type60 internal state92");

    SetMember(baseObjectType, actor, "m_wNativeDrugJobBonus", (ushort)65530);
    applyTimed.Invoke(actor, null);
    Equal((ushort)4,
        (ushort)GetMember(baseObjectType, actor, "m_wNativeDrugJobBonus"),
        "type60 UInt16 carrier wrap");

    Assert((bool)baseObjectType.GetMethod("RemoveTimedAbility")!
        .Invoke(actor, new object[] { 60 })!, "type60 removal");
    Assert(!(bool)baseObjectType.GetMethod("HasNativeActiveState")!
        .Invoke(actor, new object[] { 92 })!, "type60 state92 removal");
}

void CheckEquipmentAggregation()
{
    var stdItems = (IList)GetMember(userEngineType, userEngine, "StdItemList");
    stdItems.Clear();
    var item = NewGoodItem(0, 0);
    SetMember(goodItemType, item, "NativeDrugHealthBonus", (ushort)0x1122);
    SetMember(goodItemType, item, "NativeDrugSpellBonus", (ushort)0x3344);
    SetMember(goodItemType, item, "NativeDrugJobBonus", (ushort)0x5566);
    stdItems.Add(item);

    var actor = NewActor(playerType, 1, 1, 10, 10);
    var equipped = (Array)GetMember(baseObjectType, actor, "m_UseItems");
    var userItem = Activator.CreateInstance(userItemType)!;
    SetMember(userItemType, userItem, "wIndex", (ushort)1);
    SetMember(userItemType, userItem, "Dura", (ushort)100);
    SetMember(userItemType, userItem, "DuraMax", (ushort)100);
    equipped.SetValue(userItem, 0);
    baseObjectType.GetMethod("RecalcAbilitys")!.Invoke(actor, null);

    Equal((ushort)0x1122,
        (ushort)GetMember(baseObjectType, actor, "m_wNativeDrugHealthBonus"),
        "equipment HP drug carrier");
    Equal((ushort)0x3344,
        (ushort)GetMember(baseObjectType, actor, "m_wNativeDrugSpellBonus"),
        "equipment MP drug carrier");
    Equal((ushort)0x5566,
        (ushort)GetMember(baseObjectType, actor, "m_wNativeDrugJobBonus"),
        "equipment job drug carrier");
}

void CheckAbilityPacket()
{
    var actor = NewActor(baseObjectType, 1, 1, 10, 10);
    SetMember(baseObjectType, actor, "m_wNativeDrugHealthBonus", (ushort)0x1122);
    SetMember(baseObjectType, actor, "m_wNativeDrugSpellBonus", (ushort)0x3344);
    SetMember(baseObjectType, actor, "m_wNativeDrugJobBonus", (ushort)0x5566);
    var body = (byte[])buildAbility.Invoke(actor, null)!;
    Equal(184, body.Length, "ability packet length");
    Equal((ushort)0x3344, BitConverter.ToUInt16(body, 0xA0),
        "ability packet A0 MP carrier");
    Equal((ushort)0x1122, BitConverter.ToUInt16(body, 0xA2),
        "ability packet A2 HP carrier");
    Equal((ushort)0x5566, BitConverter.ToUInt16(body, 0xA4),
        "ability packet A4 job carrier");
}

(int Health, int Spell) Calculate(ushort baseHealth, ushort baseSpell,
    ushort healthBonus, ushort spellBonus, ushort jobBonus, byte job)
{
    object[] values =
    {
        baseHealth, baseSpell, healthBonus, spellBonus, jobBonus, job, 0, 0
    };
    calculate.Invoke(null, values);
    return ((int)values[6], (int)values[7]);
}

bool Apply(object actor, object item, out bool refresh)
{
    object[] values = { item, false };
    var used = (bool)apply.Invoke(actor, values)!;
    refresh = (bool)values[1];
    return used;
}

object NewGoodItem(ushort health, ushort spell)
{
    var item = Activator.CreateInstance(goodItemType)!;
    SetMember(goodItemType, item, "Ac", health);
    SetMember(goodItemType, item, "Mac", spell);
    return item;
}

object NewActor(Type type, int hp, int mp, int maxHp, int maxMp)
{
    var actor = Activator.CreateInstance(type)!;
    SetMember(baseObjectType, actor, "m_boGhost", false);
    SetAbility(actor, hp, mp, maxHp, maxMp);
    return actor;
}

void SetAbility(object actor, int hp, int mp, int maxHp, int maxMp)
{
    var ability = GetMember(baseObjectType, actor, "m_WAbil");
    SetMember(ability.GetType(), ability, "HP", hp);
    SetMember(ability.GetType(), ability, "MP", mp);
    SetMember(ability.GetType(), ability, "MaxHP", maxHp);
    SetMember(ability.GetType(), ability, "MaxMP", maxMp);
}

int Ability(object actor, string name)
{
    var ability = GetMember(baseObjectType, actor, "m_WAbil");
    return (int)GetMember(ability.GetType(), ability, name);
}

void InvokeState(object actor, string method, int state)
{
    Assert((bool)baseObjectType.GetMethod(method)!
        .Invoke(actor, new object[] { state })!, $"{method}({state})");
}

static bool Calls(MethodInfo caller, MethodInfo target)
{
    var il = caller.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
    for (var i = 0; i + 4 < il.Length; i++)
    {
        if (il[i] is not (0x28 or 0x6F))
            continue;
        if (BitConverter.ToInt32(il, i + 1) == target.MetadataToken)
            return true;
    }
    return false;
}

static Type TypeOf(Assembly assembly, string name) =>
    assembly.GetType(name, throwOnError: true)!;

static MethodInfo FindNamedMethod(Type type, string name, BindingFlags flags)
{
    for (var current = type; current != null; current = current.BaseType)
    {
        var method = current.GetMethods(flags | BindingFlags.DeclaredOnly)
            .SingleOrDefault(candidate => candidate.Name == name);
        if (method != null) return method;
    }
    throw new MissingMethodException(type.FullName, name);
}

static object GetMember(Type type, object instance, string name)
{
    var member = FindMember(type, name);
    return member switch
    {
        FieldInfo field => field.GetValue(instance)!,
        PropertyInfo property => property.GetValue(instance)!,
        _ => throw new MissingMemberException(type.FullName, name)
    };
}

static void SetMember(Type type, object instance, string name, object value)
{
    var member = FindMember(type, name);
    switch (member)
    {
        case FieldInfo field:
            field.SetValue(instance, value);
            break;
        case PropertyInfo property:
            property.SetValue(instance, value);
            break;
        default:
            throw new MissingMemberException(type.FullName, name);
    }
}

static void SetStaticMember(Type type, string name, object value)
{
    var member = FindMember(type, name, BindingFlags.Static);
    switch (member)
    {
        case FieldInfo field:
            field.SetValue(null, value);
            break;
        case PropertyInfo property:
            property.SetValue(null, value);
            break;
        default:
            throw new MissingMemberException(type.FullName, name);
    }
}

static MemberInfo FindMember(Type type, string name,
    BindingFlags scope = BindingFlags.Instance)
{
    for (var current = type; current != null; current = current.BaseType)
    {
        var flags = scope | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly;
        var member = (MemberInfo)current.GetField(name, flags) ??
                     current.GetProperty(name, flags);
        if (member != null) return member;
    }
    throw new MissingMemberException(type.FullName, name);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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

// run_audits.py invokes every audit with no arguments, so a tool that hard-requires
// a GameSvr build directory reported FAIL without evaluating a single assertion.
// Falling back to the checkout's own build output keeps the assertions exactly as
// they were; when no build exists the tool exits 2 (INCOMPLETE) rather than
// pretending to have checked anything.
static string ResolveGameSvrBuild(string[] args)
{
    if (args.Length > 0)
    {
        var candidate = Path.GetFullPath(args[0]);
        if (IsGameSvrBuild(candidate)) return candidate;
        var nested = FindGameSvrBuildUnder(candidate);
        if (nested != null) return nested;
    }
    return FindGameSvrBuild();
}

static bool IsGameSvrBuild(string directory) =>
    File.Exists(Path.Combine(directory, "GameSvr.dll"))
    && File.Exists(Path.Combine(directory, "SystemModule.dll"));

static string FindGameSvrBuildUnder(string root)
{
    // GameSvr.csproj's Debug OutputPath is ..\..\Build\Mir200 relative to GameSvr\,
    // i.e. a Build\ tree one level ABOVE the checkout. GameSvr\bin therefore never
    // exists in a normal build and probing only there always reported INCOMPLETE.
    var configured = GameSvrConfiguredBuild(root);
    if (configured != null) return configured;
    var binRoot = Path.Combine(root, "GameSvr", "bin");
    if (!Directory.Exists(binRoot)) return null;
    return NewestGameSvrBuild(binRoot);
}

static string GameSvrConfiguredBuild(string root)
{
    var parent = Directory.GetParent(root)?.FullName;
    foreach (var candidate in new[]
             {
                 parent == null ? null : Path.Combine(parent, "Build", "Mir200"),
                 Path.Combine(root, "Build", "Mir200")
             })
    {
        if (candidate != null && IsGameSvrBuild(candidate)) return candidate;
    }
    return null;
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameSvr", "GameSvr.csproj")))
                return current.FullName;
            current = current.Parent;
        }
    }
    return null;
}

static string FindGameSvrBuild()
{
    var repositoryRoot = FindRepositoryRoot();
    if (repositoryRoot == null)
        return null;
    return FindGameSvrBuildUnder(repositoryRoot);
}

static string NewestGameSvrBuild(string binRoot)
{
    var debug = $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}";
    foreach (var candidate in Directory
                 .EnumerateFiles(binRoot, "GameSvr.dll", SearchOption.AllDirectories)
                 .OrderByDescending(path => path.Contains(debug, StringComparison.OrdinalIgnoreCase))
                 .ThenByDescending(File.GetLastWriteTimeUtc))
    {
        var directory = Path.GetDirectoryName(candidate);
        if (directory != null && IsGameSvrBuild(directory))
            return directory;
    }
    return null;
}
