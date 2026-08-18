using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();

Equal(16, Grobal2.HUMAN_EQUIPPED_ITEM_COUNT, "equipment slot count");
Equal(14, Grobal2.U_WARDRUM, "war drum slot");
Equal(Grobal2.U_WARDRUM, Grobal2.U_YUPEI, "jade pendant compatibility alias");
Equal(15, Grobal2.U_MOUNT, "mount slot");
Equal(Grobal2.U_MOUNT, Grobal2.U_SHIELD, "shield compatibility alias");
Equal(Grobal2.U_MOUNT, Grobal2.U_HORSE, "horse compatibility alias");

Accepts(Grobal2.U_RIGHTHAND, 30);
Rejects(Grobal2.U_RIGHTHAND, 28);
Rejects(Grobal2.U_RIGHTHAND, 29);
Accepts(Grobal2.U_WARDRUM, 29);
Rejects(Grobal2.U_WARDRUM, 30);
Accepts(Grobal2.U_MOUNT, 34);
Rejects(Grobal2.U_MOUNT, 29);

foreach (var name in new[] { "\u7389\u4f69", "\u6218\u9f13", "\u519b\u9f13" })
    Equal(Grobal2.U_WARDRUM, M2Share.GetUseItemIdx(name), $"war drum alias {name}");
foreach (var name in new[] { "\u76fe\u724c", "\u9a6c\u724c", "\u5750\u9a91" })
    Equal(Grobal2.U_MOUNT, M2Share.GetUseItemIdx(name), $"mount alias {name}");

Equal("\u7389\u4f69", M2Share.GetUseItemName(Grobal2.U_WARDRUM),
    "war drum legacy script name");
Equal("\u76fe\u724c", M2Share.GetUseItemName(Grobal2.U_MOUNT),
    "mount legacy script name");

CheckNativeFeatureRefreshSlots();

Console.WriteLine("PASS equipment slot compatibility");

static void CheckNativeFeatureRefreshSlots()
{
    var predicate = typeof(TPlayObject).GetMethod(
        "NativeEquipmentSlotChangesFeature",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(typeof(TPlayObject).FullName,
            "NativeEquipmentSlotChangesFeature");

    for (byte slot = 0; slot < Grobal2.HUMAN_EQUIPPED_ITEM_COUNT; slot++)
    {
        var expected = slot == Grobal2.U_DRESS
            || slot == Grobal2.U_WEAPON
            || slot == Grobal2.U_HELMET
            || slot == Grobal2.U_MASK;
        var actual = (bool)(predicate.Invoke(null, new object[] { slot })
            ?? throw new InvalidOperationException("feature-slot predicate returned null"));
        Equal(expected, actual, $"native feature refresh slot {slot}");
    }

    var root = FindRepositoryRoot()
        ?? throw new DirectoryNotFoundException("repository root was not found");
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Operate.cs"));
    var takeOn = Between(source, "private void ClientTakeOnItems(",
        "private void ClientTakeOffItems(");
    var takeOff = Between(source, "private void ClientTakeOffItems(",
        "private void ClientUseItems(");
    CheckGuardedFeatureRefresh(takeOn, "take-on");
    CheckGuardedFeatureRefresh(takeOff, "take-off");
}

static void CheckGuardedFeatureRefresh(string body, string operation)
{
    var compact = string.Concat(body.Where(c => !char.IsWhiteSpace(c)));
    const string guard = "if(NativeEquipmentSlotChangesFeature(btWhere)){FeatureChanged();}";
    if (!compact.Contains(guard, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{operation} feature refresh is not guarded by the native slot predicate");

    var featureCallCount = compact.Split("FeatureChanged();",
        StringSplitOptions.None).Length - 1;
    if (featureCallCount != 1)
        throw new InvalidOperationException(
            $"{operation} contains {featureCallCount} FeatureChanged calls; expected exactly one guarded call");
}

static string Between(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    if (start < 0 || end <= start)
        throw new InvalidOperationException($"source boundary missing: {startMarker}");
    return source[start..end];
}

static void Accepts(int slot, byte stdMode)
{
    if (!M2Share.CheckUserItems(slot, new GoodItem { StdMode = stdMode }))
        throw new InvalidOperationException($"slot {slot} rejected StdMode {stdMode}");
}

static void Rejects(int slot, byte stdMode)
{
    if (M2Share.CheckUserItems(slot, new GoodItem { StdMode = stdMode }))
        throw new InvalidOperationException($"slot {slot} accepted StdMode {stdMode}");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
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
