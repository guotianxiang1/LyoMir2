using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

var gameDirectory = ResolveGameSvrBuild(args);
if (gameDirectory == null)
{
    Console.Error.WriteLine("INCOMPLETE: no GameSvr build directory was supplied and "
        + "none was found under GameSvr/bin. Usage: NativeItemUseCheck [GameSvr build]");
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

var systemModule = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDirectory, "SystemModule.dll"));
var gameSvr = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDirectory, "GameSvr.dll"));
var globalType = systemModule.GetType("SystemModule.Grobal2", throwOnError: true)!;
var userItemType = systemModule.GetType("SystemModule.TUserItem", throwOnError: true)!;
var shareType = gameSvr.GetType("GameSvr.M2Share", throwOnError: true)!;
var objectManagerType = gameSvr.GetType("GameSvr.ObjectManager", throwOnError: true)!;
var userEngineType = gameSvr.GetType("GameSvr.UserEngine", throwOnError: true)!;
var goodItemType = gameSvr.GetType("GameSvr.GoodItem", throwOnError: true)!;
var nativeFactoryType = gameSvr.GetType("GameSvr.NativeItemFactory", throwOnError: true)!;
var clientItemType = gameSvr.GetType("GameSvr.TOClientItem", throwOnError: true)!;
var playerType = gameSvr.GetType("GameSvr.TPlayObject", throwOnError: true)!;
var baseObjectType = gameSvr.GetType("GameSvr.TBaseObject", throwOnError: true)!;
var environmentType = gameSvr.GetType("GameSvr.Envirnoment", throwOnError: true)!;
var scriptHostType = gameSvr.GetType("GameSvr.PasEngine.PasScriptHost", throwOnError: true)!;

Assert((int)globalType.GetField("CM_1069")!.GetRawConstantValue()! == 1069,
    "native item-use alias 1069 is missing");
Assert(GetMember(clientItemType, Activator.CreateInstance(clientItemType)!, "Item") != null,
    "TOClientItem must initialize its fixed-size item packet");

var isPile = FindMethod(playerType, "IsNativePileItem", BindingFlags.Static | BindingFlags.NonPublic,
    goodItemType);
var getNativeClass = FindMethod(nativeFactoryType, "GetClassName",
    BindingFlags.Static | BindingFlags.NonPublic, goodItemType);
string NativeClass(byte mode, byte shape = 0, ushort duraMax = 10) =>
    (string)getNativeClass.Invoke(null, new[] { CreateGoodItem("FactoryProbe", mode, shape, duraMax) });

Assert(NativeClass(0, 1) == "TQuickDrug", "StdMode 0/Shape 1 factory class mismatch");
Assert(NativeClass(0, 3) == "TSlowDrug", "StdMode 0 default factory class mismatch");
Assert(NativeClass(1, 29) == "TBaseItem", "StdMode 1 fallback factory class mismatch");
Assert(NativeClass(2, 20) == "TBaseItem", "StdMode 2 fallback factory class mismatch");
Assert(NativeClass(3, 11) == "TNoEffectItem", "StdMode 3/Shape 11 factory class mismatch");
Assert(NativeClass(5, 6, 100) == "TBrokenWeapon", "broken-weapon factory predicate mismatch");
Assert(NativeClass(5, 6, 99) == "TLWeapon", "normal weapon factory predicate mismatch");
Assert(NativeClass(5, 61) == "TSpade", "spade factory range mismatch");
Assert(NativeClass(31, 1) == "TNormalBox", "normal-box factory range mismatch");
Assert(NativeClass(31, 81) == "TBundleItem", "bundle factory range mismatch");
Assert(NativeClass(31, 18) == null, "invalid StdMode 31 shape must not create an object");
Assert(NativeClass(56, 2) == "TIdentifyScrollItem", "identify-scroll factory predicate mismatch");
Assert(NativeClass(56, 0) == null, "invalid StdMode 56 shape must not create an object");
Assert(NativeClass(152, 16) == "TJingXiuBook", "pile-item factory special case mismatch");
Assert(NativeClass(155, 0) == null, "invalid StdMode 155 shape must not create an object");
Assert(NativeClass(156, 1) == "TPileFlower", "pile-flower factory predicate mismatch");
Assert(NativeClass(156, 0) == null, "invalid StdMode 156 shape must not create an object");
Assert(NativeClass(159, 0) == null, "StdMode 159 must not create an object");
Assert(NativeClass(160, 0) == "TBasePileItem", "StdMode above native table must use TBasePileItem");

Assert(!(bool)isPile.Invoke(null, new[] { CreateGoodItem("BelowPile", 149) })!, "StdMode 149 is not a pile item");
Assert((bool)isPile.Invoke(null, new[] { CreateGoodItem("Pile", 150) })!, "StdMode 150 must be a pile item");
Assert(!(bool)isPile.Invoke(null, new[] { CreateGoodItem("Invalid", 159) })!, "StdMode 159 has no native item object");
Assert((bool)isPile.Invoke(null, new[] { CreateGoodItem("PileAbove", 160) })!, "StdMode 160 must use TBasePileItem");
Assert(!(bool)isPile.Invoke(null, new[] { CreateGoodItem("Invalid155", 155, 0) })!,
    "invalid StdMode 155 shape is not a pile item");
Assert((bool)isPile.Invoke(null, new[] { CreateGoodItem("Valid155", 155, 1) })!,
    "valid StdMode 155 class must retain pile semantics");

SetStaticMember(shareType, "ObjectManager", Activator.CreateInstance(objectManagerType)!);
SetStaticMember(shareType, "ProcessMsgCriticalSection", new object());
SetStaticMember(shareType, "LogMsgCriticalSection", new object());
var userEngine = Activator.CreateInstance(userEngineType)!;
SetStaticMember(shareType, "UserEngine", userEngine);
var stdItems = (IList)GetMember(userEngineType, userEngine, "StdItemList");

var environment = Activator.CreateInstance(environmentType)!;
FindMethod(environmentType, "Initialize", BindingFlags.Instance | BindingFlags.NonPublic,
        typeof(short), typeof(short))
    .Invoke(environment, new object[] { (short)12, (short)12 });

var player = Activator.CreateInstance(playerType)!;
SetMember(playerType, player, "m_boCanUseItem", true);
SetMember(playerType, player, "m_boDeath", false);
SetMember(playerType, player, "m_boOffLineFlag", true);
SetMember(playerType, player, "m_PEnvir", environment);
var bag = (IList)GetMember(playerType, player, "m_ItemList");
var clientUseItems = FindMethod(playerType, "ClientUseItems", BindingFlags.Instance | BindingFlags.NonPublic,
    typeof(int), typeof(int));

var builtIn = CreateGoodItem("BuiltIn", 2);
stdItems.Add(builtIn);
var builtInItem = CreateUserItem(1001, 1, 1, 1);
bag.Add(builtInItem);
clientUseItems.Invoke(player, new object[] { 1001, 2 });
Assert(bag.Contains(builtInItem), "non-zero native use mode consumed a bag item");
clientUseItems.Invoke(player, new object[] { 1001, 0 });
Assert(!bag.Contains(builtInItem), "mode zero did not execute the built-in item effect");

var hungerBefore = (int)GetMember(playerType, player, "m_nHungerStatus");
stdItems.Add(CreateGoodItem("NativeNoEffect", 1, 0));
var nativeNoEffect = CreateUserItem(1002, 2, 1, 1);
bag.Add(nativeNoEffect);
clientUseItems.Invoke(player, new object[] { 1002, 0 });
Assert(!bag.Contains(nativeNoEffect), "TNoEffectItem.Use=true did not consume the item");
Assert((int)GetMember(playerType, player, "m_nHungerStatus") == hungerBefore,
    "TNoEffectItem incorrectly changed hunger state");

// StdMode 1 / Shape 20 = THappyCake, which has no arm in the item-use switch, so
// it must still fall through without consuming. (Shape 1 used to sit here, but it
// is TDoubleExpProp and that class is now ported from sub_786390 / VMT 0x77F288
// slot +0x18, so it legitimately consumes -- asserted right below.)
stdItems.Add(CreateGoodItem("UnsupportedSpecial", 1, 20));
var unsupportedSpecial = CreateUserItem(1003, 3, 1, 1);
bag.Add(unsupportedSpecial);
clientUseItems.Invoke(player, new object[] { 1003, 0 });
Assert(bag.Contains(unsupportedSpecial), "unimplemented native special class was consumed");

stdItems.Add(CreateGoodItem("NativeBase", 2, 20));
var nativeBase = CreateUserItem(1004, 4, 1, 1);
bag.Add(nativeBase);
clientUseItems.Invoke(player, new object[] { 1004, 0 });
Assert(bag.Contains(nativeBase), "TBaseItem.Use=false consumed the item");

stdItems.Add(CreateGoodItem("NoLottery", 3, 11));
var noLottery = CreateUserItem(1005, 5, 1, 1);
bag.Add(noLottery);
clientUseItems.Invoke(player, new object[] { 1005, 0 });
Assert(!bag.Contains(noLottery), "StdMode 3/Shape 11 did not follow TNoEffectItem.Use=true");

stdItems.Add(CreateGoodItem("UnsupportedBundle", 31, 81));
var unsupportedBundle = CreateUserItem(1006, 6, 1, 1);
bag.Add(unsupportedBundle);
clientUseItems.Invoke(player, new object[] { 1006, 0 });
Assert(bag.Contains(unsupportedBundle), "unimplemented TBundleItem was consumed by legacy fallback");

var workingAbility = GetMember(baseObjectType, player, "m_WAbil");
SetMember(workingAbility.GetType(), workingAbility, "HP", (ushort)100);
SetMember(workingAbility.GetType(), workingAbility, "MaxHP", (ushort)100);
SetMember(baseObjectType, player, "m_nIncHealth", 490);
SetMember(baseObjectType, player, "m_nIncSpell", 498);
var slowDrug = CreateGoodItem("NativeSlowDrug", 0, 0);
SetMember(goodItemType, slowDrug, "Ac", (ushort)20);
SetMember(goodItemType, slowDrug, "Mac", (ushort)7);
stdItems.Add(slowDrug);
var slowDrugItem = CreateUserItem(1007, 7, 1, 1);
bag.Add(slowDrugItem);
clientUseItems.Invoke(player, new object[] { 1007, 0 });
Assert(!bag.Contains(slowDrugItem), "TSlowDrug effect did not consume the item");
Assert((int)GetMember(baseObjectType, player, "m_nIncHealth") == 500,
    "TSlowDrug did not apply the native delayed-HP cap");
Assert((int)GetMember(baseObjectType, player, "m_nIncSpell") == 500,
    "TSlowDrug did not apply the native delayed-MP cap");

var tempRoot = Path.Combine(Path.GetTempPath(), "loym2-native-item-use-" + Guid.NewGuid().ToString("N"));
try
{
    var itemScripts = Directory.CreateDirectory(Path.Combine(tempRoot, "PsItemScript")).FullName;
    Directory.CreateDirectory(Path.Combine(tempRoot, "CommonScripts"));
    var mapQuestScripts = Directory.CreateDirectory(Path.Combine(tempRoot, "PsMapQuest")).FullName;
    File.WriteAllText(Path.Combine(itemScripts, "StackPass.pas"),
        "function UseItem: Boolean; begin Result := This_Item.ClientItemID = 2001; end; begin end.");
    File.WriteAllText(Path.Combine(itemScripts, "StackFail.pas"),
        "function UseItem: Boolean; begin Result := false; end; begin end.");
    File.WriteAllText(Path.Combine(itemScripts, "InvalidFactory.pas"),
        "function UseItem: Boolean; begin Result := true; end; begin end.");
    File.WriteAllText(Path.Combine(tempRoot, "CommonScripts", "StackFail.pas"),
        "function UseItem: Boolean; begin Result := true; end; begin end.");
    File.WriteAllText(Path.Combine(mapQuestScripts, "RunQuest.pas"),
        "procedure PlayerActivePoint(payType, payNo, payNum: Integer; payName: string); " +
        "begin if (payType = 0) and (payNo = 0) and (payNum = 0) and (payName = 'StackPass') " +
        "then This_Player.AddGold(7); end; begin end.");

    var scriptHost = Activator.CreateInstance(scriptHostType, tempRoot)!;
    SetStaticMember(shareType, "PasEngine", scriptHost);

    stdItems.Add(CreateGoodItem("StackPass", 150));
    var stacked = CreateUserItem(2001, 8, 2, 10);
    bag.Add(stacked);
    clientUseItems.Invoke(player, new object[] { 2001, 0 });
    Assert(bag.Contains(stacked), "first pile-item use removed the whole stack");
    Assert((ushort)GetMember(userItemType, stacked, "Dura") == 1, "first pile-item use did not decrement Dura");
    Assert((int)GetMember(baseObjectType, player, "m_nGold") == 7,
        "successful item use did not call PlayerActivePoint with native arguments");
    clientUseItems.Invoke(player, new object[] { 2001, 0 });
    Assert(!bag.Contains(stacked), "depleted pile item was not removed");
    Assert((int)GetMember(baseObjectType, player, "m_nGold") == 14,
        "PlayerActivePoint was not called once per successful item use");

    stdItems.Add(CreateGoodItem("StackFail", 150));
    var rejected = CreateUserItem(2002, 9, 2, 10);
    bag.Add(rejected);
    clientUseItems.Invoke(player, new object[] { 2002, 0 });
    Assert(bag.Contains(rejected), "false item-script result consumed the item");
    Assert((ushort)GetMember(userItemType, rejected, "Dura") == 2, "false item-script result changed Dura");
    Assert((int)GetMember(baseObjectType, player, "m_nGold") == 14,
        "failed item use incorrectly called PlayerActivePoint");

    stdItems.Add(CreateGoodItem("InvalidFactory", 159));
    var invalidFactoryItem = CreateUserItem(2003, 10, 2, 10);
    bag.Add(invalidFactoryItem);
    clientUseItems.Invoke(player, new object[] { 2003, 0 });
    Assert(bag.Contains(invalidFactoryItem), "factory-null item executed an item script and was consumed");
    Assert((ushort)GetMember(userItemType, invalidFactoryItem, "Dura") == 2,
        "factory-null item changed stack durability");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

Console.WriteLine("PASS factory=0x74C338 native-class-dispatch invalid=reject no-effect=true base=false pile=dura-decrement active-point=native-args");

object CreateGoodItem(string name, byte stdMode, byte shape = 0, ushort duraMax = 10)
{
    var item = Activator.CreateInstance(goodItemType)!;
    SetMember(goodItemType, item, "Name", name);
    SetMember(goodItemType, item, "StdMode", stdMode);
    SetMember(goodItemType, item, "Shape", shape);
    SetMember(goodItemType, item, "DuraMax", duraMax);
    return item;
}

object CreateUserItem(int makeIndex, ushort itemIndex, ushort dura, ushort duraMax)
{
    var item = Activator.CreateInstance(userItemType)!;
    SetMember(userItemType, item, "MakeIndex", makeIndex);
    SetMember(userItemType, item, "ClientItemID", makeIndex);
    SetMember(userItemType, item, "wIndex", itemIndex);
    SetMember(userItemType, item, "Dura", dura);
    SetMember(userItemType, item, "DuraMax", duraMax);
    return item;
}

static MethodInfo FindMethod(Type type, string name, BindingFlags flags, params Type[] parameterTypes)
{
    for (var current = type; current != null; current = current.BaseType)
    {
        var method = current.GetMethod(name, flags | BindingFlags.DeclaredOnly, null, parameterTypes, null);
        if (method != null) return method;
    }
    throw new MissingMethodException(type.FullName, name);
}

static object GetMember(Type type, object instance, string name)
{
    var member = FindMember(type, name);
    return member switch
    {
        FieldInfo field => field.GetValue(instance),
        PropertyInfo property => property.GetValue(instance),
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

static MemberInfo FindMember(Type type, string name, BindingFlags scope = BindingFlags.Instance)
{
    for (var current = type; current != null; current = current.BaseType)
    {
        var flags = scope | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var member = (MemberInfo)current.GetField(name, flags) ?? current.GetProperty(name, flags);
        if (member != null) return member;
    }
    throw new MissingMemberException(type.FullName, name);
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
