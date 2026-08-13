using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.LogStringList = new ArrayList();
M2Share.LogMsgCriticalSection = new object();
M2Share.ProcessMsgCriticalSection = new object();

var player = new TPlayObject
{
    m_sCharName = "glory-check",
    m_sMapName = "glory-map",
    m_nCurrX = 12,
    m_nCurrY = 34
};
var bridge = new PasApiBridge { CurrentPlayer = player };
var bridgeType = typeof(PasApiBridge);
var direct = RequiredMethod(bridgeType, "TryAddNativeGloryPoint");
var generic = RequiredMethod(bridgeType, "TryGiveNativeGloryPoint");
var gloryVersion = RequiredField(typeof(NativeCreditCardAccount),
    "GloryPointDirtyVersion");
var creditVersion = RequiredField(typeof(NativeCreditCardAccount), "DirtyVersion");

Reset(player, gloryVersion, creditVersion, loaded: false, glory: 40,
    gloryVersionValue: 9, creditVersionValue: 101);
M2Share.CreditCardService = NativeCreditCardService.Disabled;
Assert(InvokeBool(direct, bridge, 5), "direct AddGloryPoint rejected positive amount");
Equal(45, player.m_CreditCard.GloryPointValue,
    "direct AddGloryPoint account value");
Assert(player.m_CreditCard.GloryPointDirty,
    "direct AddGloryPoint did not mark GloryPoint dirty");
EqualLong(10L, (long)gloryVersion.GetValue(player.m_CreditCard),
    "direct AddGloryPoint dirty version");
Assert(!player.m_CreditCard.Dirty,
    "direct AddGloryPoint changed CreditCard dirty state");
EqualLong(101L, (long)creditVersion.GetValue(player.m_CreditCard),
    "direct AddGloryPoint changed CreditCard dirty version");
AssertDirectMessages(player, 5, "disabled/unloaded direct");
Equal(0, M2Share.LogStringList.Count,
    "direct AddGloryPoint wrote a business log");

Reset(player, gloryVersion, creditVersion, loaded: true,
    glory: int.MaxValue - 2, gloryVersionValue: 20, creditVersionValue: 202);
M2Share.CreditCardService = CreateCreditCardService(true);
Assert(InvokeBool(direct, bridge, 5),
    "enabled/loaded direct AddGloryPoint rejected positive amount");
Equal(unchecked(int.MaxValue - 2 + 5),
    player.m_CreditCard.GloryPointValue,
    "direct AddGloryPoint did not preserve Int32 wrap");
EqualLong(21L, (long)gloryVersion.GetValue(player.m_CreditCard),
    "wrapped direct AddGloryPoint dirty version");
AssertDirectMessages(player, 5, "enabled/loaded wrapped direct");
Equal(0, M2Share.LogStringList.Count,
    "wrapped direct AddGloryPoint wrote a business log");

foreach (var amount in new[] { 0, -1, int.MinValue })
{
    Reset(player, gloryVersion, creditVersion, loaded: amount == 0,
        glory: 77, gloryVersionValue: 30, creditVersionValue: 303);
    var before = Snapshot(player.m_CreditCard, gloryVersion, creditVersion);
    Assert(!InvokeBool(direct, bridge, amount),
        $"direct AddGloryPoint accepted invalid amount {amount}");
    Assert(Snapshot(player.m_CreditCard, gloryVersion, creditVersion).Equals(before),
        $"invalid direct AddGloryPoint {amount} changed account state");
    Equal(0, player.m_MsgList.Count,
        $"invalid direct AddGloryPoint {amount} emitted a message");
    Equal(0, M2Share.LogStringList.Count,
        $"invalid direct AddGloryPoint {amount} emitted a log");
}

Reset(player, gloryVersion, creditVersion, loaded: false, glory: 80,
    gloryVersionValue: 35, creditVersionValue: 350);
var addArgs = new List<PasValue> { PasValue.FromInt(6) };
Assert(bridge.CallPlayerFunc("AddGloryPoint", addArgs, out var addResult),
    "AddGloryPoint function was not dispatched");
Assert(addResult.AsBool(), "AddGloryPoint function rejected a positive amount");
Equal(86, player.m_CreditCard.GloryPointValue,
    "AddGloryPoint function account value");
AssertDirectMessages(player, 6, "AddGloryPoint function");
Equal(0, M2Share.LogStringList.Count,
    "AddGloryPoint function wrote a business log");
var afterFunction = Snapshot(player.m_CreditCard, gloryVersion, creditVersion);
Assert(!bridge.CallPlayerMethod("AddGloryPoint", addArgs),
    "AddGloryPoint method shadowed the native function");
Assert(Snapshot(player.m_CreditCard, gloryVersion, creditVersion)
        .Equals(afterFunction),
    "rejected AddGloryPoint method changed account state");
foreach (var invalidArgs in new[]
         {
             new List<PasValue>(),
             new List<PasValue> { PasValue.FromInt(1), PasValue.FromInt(2) }
         })
{
    Assert(!bridge.CallPlayerFunc("AddGloryPoint", invalidArgs,
            out var invalidResult),
        "AddGloryPoint function accepted a non-exact argument count");
    Assert(invalidResult.Type == PasValueType.Nil,
        "wrong-arity AddGloryPoint did not return Nil");
    Assert(Snapshot(player.m_CreditCard, gloryVersion, creditVersion)
            .Equals(afterFunction),
        "wrong-arity AddGloryPoint changed account state");
}
Assert(bridge.CallPlayerFunc("AddGloryPoint",
        new List<PasValue> { PasValue.FromInt(0) }, out var zeroResult),
    "exact-arity zero AddGloryPoint was not handled");
Assert(!zeroResult.AsBool(), "zero AddGloryPoint returned True");
Assert(Snapshot(player.m_CreditCard, gloryVersion, creditVersion)
        .Equals(afterFunction),
    "zero AddGloryPoint changed account state");

Reset(player, gloryVersion, creditVersion, loaded: false, glory: 100,
    gloryVersionValue: 40, creditVersionValue: 404);
M2Share.CreditCardService = NativeCreditCardService.Disabled;
// The native audit gate is the CLICKED npc at player+0xCD8, not the script context:
//   0x6DF341 83 BF D8 0C 00 00 00  cmp dword [edi+0xCD8],0
//   0x6DF348 0F 84 06 01 00 00     je 0x6DF454        ; nil -> no 经验/内功经验/荣耀点 audit
//   0x6DF34E 8B 87 D8 0C 00 00     mov eax,[edi+0xCD8]
// and the click handler only writes that field AFTER the dispatch vcall
//   0x6B8BA2 E8 9D 78 06 00        call 0x720444
//   0x6B8BA7 89 B3 D8 0C 00 00     mov [ebx+0xCD8],esi
// so a fixture that binds only the script NPC is asking for a log native would omit.
var gloryNpc = new NormNpc
{
    m_sCharName = "glory-npc",
    m_sMapName = "npc-map"
};
bridge.CurrentNpc = gloryNpc;
player.m_NPC = gloryNpc;
var giveArgs = new List<PasValue>
{
    PasValue.FromString("荣耀点"),
    PasValue.FromInt(7)
};
Assert(bridge.CallPlayerFunc("Give", giveArgs, out var giveResult),
    "generic Give bridge did not handle GloryPoint");
Assert(giveResult.AsBool(), "generic Give reported GloryPoint failure");
Equal(107, player.m_CreditCard.GloryPointValue,
    "generic Give GloryPoint account value");
EqualLong(41L, (long)gloryVersion.GetValue(player.m_CreditCard),
    "generic Give GloryPoint dirty version");
AssertDirectMessages(player, 7, "generic Give");
Equal(2, M2Share.LogStringList.Count,
    "generic Give GloryPoint business log count");
var expectedSystemLog = string.Join('\t', 9, player.m_sMapName, player.m_nCurrX,
    player.m_nCurrY, player.m_sCharName, "荣耀点", 888888, 7, "系统给予");
EqualText(expectedSystemLog, (string)M2Share.LogStringList[0],
    "generic Give GloryPoint system log");
var expectedNpcLog = string.Join('\t', 9, player.m_sMapName, player.m_nCurrX,
    player.m_nCurrY, player.m_sCharName, "荣耀点", 888888, 7,
    "NPC给予：glory-npc-npc-map");
EqualText(expectedNpcLog, (string)M2Share.LogStringList[1],
    "generic Give GloryPoint NPC audit log");

Reset(player, gloryVersion, creditVersion, loaded: false, glory: 100,
    gloryVersionValue: 50, creditVersionValue: 505);
Assert(!InvokeBool(generic, bridge, 0),
    "generic GloryPoint helper accepted zero amount");
Equal(0, player.m_MsgList.Count,
    "invalid generic GloryPoint helper emitted a message");
Equal(0, M2Share.LogStringList.Count,
    "invalid generic GloryPoint helper emitted a log");

var root = FindRepositoryRoot();
var source = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
    "PasEngine", "PasApiBridge.NativeGive.cs"));
var directStart = RequiredIndex(source,
    "private bool TryAddNativeGloryPoint(int count)");
var genericStart = RequiredIndex(source,
    "private bool TryGiveNativeGloryPoint(int count)", directStart);
var writerStart = RequiredIndex(source,
    "private void WriteNativeGiveCurrencyLog", genericStart);
var directSource = source.Substring(directStart, genericStart - directStart);
var genericSource = source.Substring(genericStart, writerStart - genericStart);
RequireOrder(directSource, "if (count <= 0) return false;",
    "CurrentPlayer.SendMsg", "GloryPointValue = unchecked(",
    "GloryPointDirty = true", "GloryPointDirtyVersion++",
    "CurrentPlayer.RefreshNativeLingFu()");
Reject(directSource, "AddGameDataLog",
    "direct AddGloryPoint contains a business log");
Assert(genericSource.Contains("TryAddNativeGloryPoint(count)",
        StringComparison.Ordinal),
    "generic Give does not reuse direct AddGloryPoint semantics");
Assert(genericSource.Contains("GiveGloryPoint, 888888, count, \"系统给予\"",
        StringComparison.Ordinal),
    "generic Give GloryPoint log shape changed");

Console.WriteLine(
    "PASS AddGloryPoint=function/exact-1 method=closed positive-only prompt=DB/FF " +
    "unchecked=Int32 dirty=independent refresh=1 direct-log=0 give-logs=2x9/888888");
return;

static MethodInfo RequiredMethod(Type type, string name) =>
    type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("missing method: " + name);

static FieldInfo RequiredField(Type type, string name) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("missing field: " + name);

static bool InvokeBool(MethodInfo method, object target, int amount) =>
    (bool)(method.Invoke(target, new object[] { amount })
        ?? throw new InvalidOperationException(method.Name + " returned null"));

static void Reset(TPlayObject player, FieldInfo gloryVersion, FieldInfo creditVersion,
    bool loaded, int glory, long gloryVersionValue, long creditVersionValue)
{
    player.m_MsgList.Clear();
    M2Share.LogStringList.Clear();
    var account = player.m_CreditCard;
    account.Loaded = loaded;
    account.Dirty = false;
    account.Value = 11;
    account.Value2 = 22;
    account.UsedValue = 33;
    account.Index = 44;
    account.LastSaveTick = 55;
    account.GloryPointDirty = false;
    account.GloryPointValue = glory;
    account.GloryPointPeriod = 66;
    gloryVersion.SetValue(account, gloryVersionValue);
    creditVersion.SetValue(account, creditVersionValue);
}

static object Snapshot(NativeCreditCardAccount account, FieldInfo gloryVersion,
    FieldInfo creditVersion) =>
    (account.Loaded, account.Dirty, account.Value, account.Value2,
        account.UsedValue, account.Index, account.LastSaveTick,
        account.GloryPointDirty, account.GloryPointValue,
        account.GloryPointPeriod, (long)gloryVersion.GetValue(account),
        (long)creditVersion.GetValue(account));

static void AssertDirectMessages(TPlayObject player, int amount, string scenario)
{
    Equal(2, player.m_MsgList.Count, scenario + " message count");
    var prompt = player.m_MsgList[0];
    Equal(Grobal2.RM_SYSMESSAGE, prompt.wIdent, scenario + " prompt Ident");
    Equal(0, prompt.wParam, scenario + " prompt wParam");
    Equal(0xDB, prompt.nParam1, scenario + " prompt foreground");
    Equal(0xFF, prompt.nParam2, scenario + " prompt background");
    Equal(0, prompt.nParam3, scenario + " prompt nParam3");
    EqualText(amount + "点荣耀点增加", prompt.Buff, scenario + " prompt body");
    var refresh = player.m_MsgList[1];
    Equal(Grobal2.RM_LINGFU_CHANGED, refresh.wIdent,
        scenario + " native refresh Ident");
}

static NativeCreditCardService CreateCreditCardService(bool enabled)
{
    var constructor = typeof(NativeCreditCardService).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(bool), typeof(bool), typeof(string), typeof(byte[]) }, null);
    Assert(constructor != null,
        "NativeCreditCardService constructor reflection target is missing");
    var switches = new byte[5];
    if (enabled) switches[1] = 0x10;
    return (NativeCreditCardService)constructor.Invoke(
        new object[] { enabled, false, string.Empty, switches });
}

static void RequireOrder(string source, params string[] markers)
{
    var previous = -1;
    foreach (var marker in markers)
    {
        var current = source.IndexOf(marker, StringComparison.Ordinal);
        Assert(current > previous, "source order missing or changed at: " + marker);
        previous = current;
    }
}

static void Reject(string source, string marker, string message)
{
    if (source.Contains(marker, StringComparison.Ordinal)) Fail(message);
}

static int RequiredIndex(string source, string marker, int start = 0)
{
    var index = source.IndexOf(marker, start, StringComparison.Ordinal);
    if (index < 0) Fail("source marker missing: " + marker);
    return index;
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("GameSvr/GameSvr.csproj was not found");
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

static void Equal(int expected, int actual, string message)
{
    if (expected != actual) Fail($"{message}: expected {expected}, actual {actual}");
}

static void EqualLong(long expected, long actual, string message)
{
    if (expected != actual) Fail($"{message}: expected {expected}, actual {actual}");
}

static void EqualText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        Fail($"{message}: expected [{expected}], actual [{actual}]");
}

static void Assert(bool condition, string message)
{
    if (!condition) Fail(message);
}

static void Fail(string message) => throw new InvalidOperationException(message);
