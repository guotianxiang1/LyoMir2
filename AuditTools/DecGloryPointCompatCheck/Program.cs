using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.LogStringList = new ArrayList();
M2Share.LogMsgCriticalSection = new object();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.PasEngine = null;

var gloryVersion = RequiredField(typeof(NativeCreditCardAccount),
    "GloryPointDirtyVersion");
var creditVersion = RequiredField(typeof(NativeCreditCardAccount),
    "DirtyVersion");
var managerType = typeof(TPlayObject).Assembly.GetType(
    "GameSvr.Services.NativeGloryLogManager", true)!;
var managerRecord = RequiredStaticMethod(managerType, "Record");
var managerRun = RequiredStaticMethod(managerType, "Run");
var managerFlush = RequiredStaticMethod(managerType, "Flush");
var managerPending = RequiredStaticField(managerType, "Pending");
var managerDirty = RequiredStaticField(managerType, "_dirty");
var managerLastTick = RequiredStaticField(managerType, "_lastFlushTick");
var managerSync = RequiredStaticField(managerType, "SyncRoot");

ResetManager();
M2Share.CreditCardService = NativeCreditCardService.Disabled;
var player = NewPlayer();
var bridge = new PasApiBridge { CurrentPlayer = player };
ResetPlayer(player, 1000, 11, 7, 21);
var exactArgs = Args(30086, 100, 2, false, "荣耀兑换");
var beforeMethod = Snapshot(player);
Assert(!bridge.CallPlayerMethod("DecGloryPoint", exactArgs),
    "DecGloryPoint method dispatcher was opened");
Assert(Snapshot(player).Equals(beforeMethod),
    "rejected DecGloryPoint method changed account state");
AssertNoOutput(player, "rejected method");

foreach (var invalidArgs in new[]
         {
             Args(1, 2, 3, false, null).Take(4).ToList(),
             Args(1, 2, 3, false, "x").Append(PasValue.FromInt(6)).ToList()
         })
{
    ResetManager();
    ResetPlayer(player, 1000, 11, 7, 21);
    var before = Snapshot(player);
    Assert(!bridge.CallPlayerFunc("DecGloryPoint", invalidArgs,
            out var invalidResult),
        $"DecGloryPoint accepted arity {invalidArgs.Count}");
    Assert(invalidResult.Type == PasValueType.Nil,
        $"wrong-arity DecGloryPoint {invalidArgs.Count} did not return Nil");
    Assert(Snapshot(player).Equals(before),
        $"wrong-arity DecGloryPoint {invalidArgs.Count} changed state");
    AssertNoOutput(player, $"wrong arity {invalidArgs.Count}");
    AssertManagerEmpty($"wrong arity {invalidArgs.Count}");
}

foreach (var invalid in new[]
         {
             (Price: 0, Quantity: 1, Balance: 1000, Name: "zero"),
             (Price: -1, Quantity: 1, Balance: 1000, Name: "negative"),
             (Price: int.MaxValue, Quantity: 2, Balance: 1000,
                 Name: "negative overflow"),
             (Price: 65536, Quantity: 65536, Balance: 1000,
                 Name: "zero overflow"),
             (Price: 101, Quantity: 1, Balance: 100, Name: "insufficient")
         })
{
    ResetManager();
    ResetPlayer(player, invalid.Balance, 31, 9, 41);
    var before = Snapshot(player);
    Assert(bridge.CallPlayerFunc("DecGloryPoint",
            Args(99, invalid.Price, invalid.Quantity, true, "invalid"),
            out var result), invalid.Name + " exact-five was not handled");
    Assert(result.Type == PasValueType.Boolean && !result.AsBool(),
        invalid.Name + " returned True");
    Assert(Snapshot(player).Equals(before), invalid.Name + " changed state");
    AssertNoOutput(player, invalid.Name);
    AssertManagerEmpty(invalid.Name);
}

ResetManager();
ResetPlayer(player, 20, 50, 0, 60);
Assert(Dispatch(bridge, 77, -2, -3, true, "双负"),
    "double-negative positive product was rejected");
Equal(14, player.m_CreditCard.GloryPointValue,
    "double-negative remaining GloryPoint");
Equal(6, PendingValue(77), "double-negative aggregated value");
AssertSingleRefresh(player, "double-negative");

ResetManager();
ResetPlayer(player, 70000, 70, 0, 80);
Assert(Dispatch(bridge, 78, 65536, 65537, false, "溢出正数"),
    "wrapped-positive product was rejected");
Equal(4464, player.m_CreditCard.GloryPointValue,
    "wrapped-positive remaining GloryPoint");
Equal(65536, PendingValue(78), "wrapped-positive aggregated value");

ResetManager();
ResetPlayer(player, 10000, 100, 7, 200);
M2Share.CreditCardService = CreateMonthlyService();
Assert(Dispatch(bridge, 30086, 8800, 1, false, "荣耀兑换"),
    "normal DecGloryPoint transaction failed");
Equal(1200, player.m_CreditCard.GloryPointValue, "normal remaining GloryPoint");
Assert(player.m_CreditCard.GloryPointDirty,
    "normal transaction did not mark GloryPoint dirty");
EqualLong(101, (long)gloryVersion.GetValue(player.m_CreditCard)!,
    "normal GloryPoint dirty version");
Equal(95, player.m_CreditCard.Value2, "normal Value2 bonus");
Assert(player.m_CreditCard.Dirty, "normal Value2 was not marked dirty");
EqualLong(201, (long)creditVersion.GetValue(player.m_CreditCard)!,
    "normal Value2 dirty version");
AssertSingleRefresh(player, "normal transaction");
Equal(1, M2Share.LogStringList.Count, "normal game log count");
EqualText("42\taudit-map\t12\t34\taudit-role\t荣耀点\t30086\t8800\t" +
          "荣耀兑换:个数1; 剩余：1200", LogAt(0), "normal game log");
Equal(8800, PendingValue(30086), "normal aggregated GloryLog value");
Assert((bool)managerDirty.GetValue(null)!, "normal manager dirty flag");

foreach (var bonusCase in new[]
         {
             (Amount: 99, Expected: 5, Dirty: false, Version: 300L),
             (Amount: 100, Expected: 6, Dirty: true, Version: 301L),
             (Amount: 199, Expected: 6, Dirty: true, Version: 301L)
         })
{
    ResetManager();
    ResetPlayer(player, 1000, 90, 5, 300);
    M2Share.CreditCardService = CreateMonthlyService();
    Assert(Dispatch(bridge, 500, bonusCase.Amount, 1, false, "取整"),
        $"Value2 amount {bonusCase.Amount} transaction failed");
    Equal(bonusCase.Expected, player.m_CreditCard.Value2,
        $"Value2 amount {bonusCase.Amount}");
    Assert(player.m_CreditCard.Dirty == bonusCase.Dirty,
        $"Value2 amount {bonusCase.Amount} dirty state");
    EqualLong(bonusCase.Version,
        (long)creditVersion.GetValue(player.m_CreditCard)!,
        $"Value2 amount {bonusCase.Amount} dirty version");
}

ResetManager();
M2Share.CreditCardService = NativeCreditCardService.Disabled;
var falsePlayer = NewPlayer();
var truePlayer = NewPlayer();
ResetPlayer(falsePlayer, 500, 1, 0, 1);
ResetPlayer(truePlayer, 500, 1, 0, 1);
Assert(Dispatch(new PasApiBridge { CurrentPlayer = falsePlayer },
        901, 25, 2, false, "标志"), "bAddPoint=False failed");
var falseState = Snapshot(falsePlayer);
M2Share.LogStringList.Clear();
Assert(Dispatch(new PasApiBridge { CurrentPlayer = truePlayer },
        901, 25, 2, true, "标志"), "bAddPoint=True failed");
Assert(Snapshot(truePlayer).Equals(falseState),
    "bAddPoint changed transaction state");
Equal(100, PendingValue(901), "bAddPoint aggregate equivalence");

ResetManager();
managerRecord.Invoke(null, new object[] { 44, int.MaxValue });
managerRecord.Invoke(null, new object[] { 44, 1 });
Equal(int.MinValue, PendingValue(44), "manager Int32 aggregation wrap");
Assert((bool)managerDirty.GetValue(null)!, "manager Record dirty flag");
ResetManager();
managerRun.Invoke(null, new object[] { 10000 });
Equal(0, (int)managerLastTick.GetValue(null)!, "timer exact-10000 boundary");
managerRun.Invoke(null, new object[] { 10001 });
Equal(10001, (int)managerLastTick.GetValue(null)!, "timer over-10000 boundary");
managerLastTick.SetValue(null, int.MaxValue - 5);
var wrappedTick = unchecked(int.MinValue + 9995);
managerRun.Invoke(null, new object[] { wrappedTick });
Equal(wrappedTick, (int)managerLastTick.GetValue(null)!,
    "timer unsigned wrap boundary");
ResetManager();
managerRecord.Invoke(null, new object[] { 45, 9 });
var previousConnectionString = M2Share.g_Config.sConnctionString;
M2Share.g_Config.sConnctionString = string.Empty;
Assert(!(bool)managerFlush.Invoke(null, null)!,
    "database-unavailable flush returned True");
M2Share.g_Config.sConnctionString = previousConnectionString;
Equal(0, PendingValue(45), "failed flush did not discard the native batch");
Assert(!(bool)managerDirty.GetValue(null)!,
    "failed flush retained the dirty flag");

ResetManager();
managerRecord.Invoke(null, new object[] { 46, 13 });
M2Share.g_Config.sConnctionString = string.Empty;
new MirrorMessage().ProcessData(Grobal2.ISM_GLORYLOG_FLUSH,
    int.MinValue, "ignored/body");
M2Share.g_Config.sConnctionString = previousConnectionString;
Equal(0, PendingValue(46), "OtherGS 251 did not force the native flush");
Assert(!(bool)managerDirty.GetValue(null)!,
    "OtherGS 251 retained the dirty flag");
Equal(251, Grobal2.ISM_GLORYLOG_FLUSH, "OtherGS GloryLog Ident");

var root = FindRepositoryRoot();
var playerSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeGlory.cs"));
var managerSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
    "NativeGloryLogManager.cs"));
var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
    "PasEngine", "PasApiBridge.cs"));
var appSource = File.ReadAllText(Path.Combine(root, "GameSvr", "GameApp.cs"));
var serverSource = File.ReadAllText(Path.Combine(root, "GameSvr", "GameServer.cs"));
var mirrorSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Snaps",
    "MirrorMessage.cs"));

RequireOrder(playerSource,
    "var total = unchecked(nPrize * nNum);",
    "NativeGloryLogManager.Record(vsId, total);",
    "M2Share.AddGameDataLog",
    "RefreshNativeLingFu();",
    "AddNativeYbShopCreditValue2(total / 100);",
    "NotifyPlayerActivePoint(2, description ?? string.Empty, total, 0);");
Require(playerSource, "_ = bAddPoint;", "bAddPoint ignore marker");
Require(playerSource, "string.Join('\\t', 42", "type-42 game log");
Require(managerSource,
    "Create Table if not Exists gamedata.GloryLog(", "native GloryLog DDL");
Require(managerSource, "UNIQUE KEY uniKey (costId, costDate));",
    "native GloryLog unique key");
Require(managerSource, "costDate=now();", "native GloryLog date select");
Require(managerSource, "unchecked(value + amount)", "database value wrap");
Require(managerSource, "if (entry.Value <= 0) continue;",
    "nonpositive pending skip");
Require(managerSource,
    "unchecked((uint)(currentTick - _lastFlushTick)) <= 10000u",
    "unsigned strict timer boundary");
Require(managerSource, "[exception]: 保存荣耀点消耗日志出错：",
    "native exception prefix");
Reject(managerSource, "Task", "GloryLog manager became asynchronous");
Reject(managerSource, "Queue<", "GloryLog manager uses a queue substitute");
Reject(managerSource, ".json", "GloryLog manager uses JSON");
Reject(managerSource, "tbl_", "GloryLog manager uses a tbl substitute");
var methodDispatcherEnd = bridgeSource.IndexOf("public bool CallPlayerFunc",
    StringComparison.Ordinal);
var methodStart = bridgeSource.IndexOf("case \"decglorypoint\":",
    StringComparison.Ordinal);
Assert(methodStart >= 0 && methodStart < methodDispatcherEnd,
    "DecGloryPoint method dispatch boundary is missing");
var methodEnd = bridgeSource.IndexOf("case \"addguildpoint\":", methodStart,
    StringComparison.Ordinal);
Assert(methodEnd > methodStart, "DecGloryPoint method dispatch end is missing");
var methodSource = bridgeSource.Substring(methodStart, methodEnd - methodStart);
Require(methodSource, "return RejectUnsupportedNativeApi();",
    "method dispatcher remains closed");
var functionStart = bridgeSource.IndexOf("case \"decglorypoint\":",
    methodDispatcherEnd, StringComparison.Ordinal);
var functionEnd = bridgeSource.IndexOf("case \"takediamond\":", functionStart,
    StringComparison.Ordinal);
Assert(functionStart >= 0 && functionEnd > functionStart,
    "DecGloryPoint function dispatch boundary is missing");
var functionSource = bridgeSource.Substring(functionStart,
    functionEnd - functionStart);
Require(functionSource, "if (args.Count != 5) return false;",
    "exact-five function dispatch");
Require(functionSource, "CurrentPlayer.DecNativeGloryPoint(",
    "native DecGloryPoint function target");
Require(appSource, "NativeGloryLogManager.EnsureNativeSchema(",
    "startup GloryLog schema hook");
Require(serverSource, "NativeGloryLogManager.Run(HUtil32.GetTickCount());",
    "main tick GloryLog hook");
Require(serverSource, "NativeGloryLogManager.Flush();",
    "shutdown GloryLog flush hook");
Require(mirrorSource, "case Grobal2.ISM_GLORYLOG_FLUSH:",
    "OtherGS 251 receiver case");
Require(mirrorSource, "NativeGloryLogManager.Flush();",
    "OtherGS 251 forced flush target");
Reject(managerSource, "SendServerGroupMsg",
    "GloryLog flush must not rebroadcast OtherGS 251");

Console.WriteLine(
    "PASS DecGloryPoint function=exact5 method=closed arithmetic=Int32-wrap " +
    "debit=atomic log=42 refresh=10054-once Value2=total/100 " +
    "active=(2,0,total,desc) GloryLog=sync/>10000/forced-flush/native-SQL " +
    "OtherGS251=receiver-only");
return;

bool Dispatch(PasApiBridge target, int vsId, int price, int quantity,
    bool addPoint, string description)
{
    Assert(target.CallPlayerFunc("DecGloryPoint",
            Args(vsId, price, quantity, addPoint, description), out var result),
        "exact-five DecGloryPoint was not handled");
    Assert(result.Type == PasValueType.Boolean,
        "exact-five DecGloryPoint did not return Boolean");
    return result.AsBool();
}

List<PasValue> Args(int vsId, int price, int quantity, bool addPoint,
    string description) =>
    new()
    {
        PasValue.FromInt(vsId), PasValue.FromInt(price),
        PasValue.FromInt(quantity), PasValue.FromBool(addPoint),
        PasValue.FromString(description ?? string.Empty)
    };

TPlayObject NewPlayer() => new()
{
    m_boOffLineFlag = true,
    m_sMapName = "audit-map",
    m_nCurrX = 12,
    m_nCurrY = 34,
    m_sCharName = "audit-role"
};

void ResetPlayer(TPlayObject target, int glory, long gloryVersionValue,
    int value2, long creditVersionValue)
{
    target.m_MsgList.Clear();
    M2Share.LogStringList.Clear();
    var account = target.m_CreditCard;
    account.Loaded = false;
    account.GloryPointDirty = false;
    account.GloryPointValue = glory;
    account.Dirty = false;
    account.Value = 3;
    account.Value2 = value2;
    account.UsedValue = 4;
    gloryVersion.SetValue(account, gloryVersionValue);
    creditVersion.SetValue(account, creditVersionValue);
}

object Snapshot(TPlayObject target)
{
    var account = target.m_CreditCard;
    return (account.Loaded, account.GloryPointDirty, account.GloryPointValue,
        (long)gloryVersion.GetValue(account)!, account.Dirty, account.Value,
        account.Value2, account.UsedValue,
        (long)creditVersion.GetValue(account)!);
}

void ResetManager()
{
    lock (managerSync.GetValue(null)!)
    {
        ((IDictionary)managerPending.GetValue(null)!).Clear();
        managerDirty.SetValue(null, false);
        managerLastTick.SetValue(null, 0);
    }
}

int PendingValue(int costId)
{
    var pending = (IDictionary)managerPending.GetValue(null)!;
    return pending.Contains(costId) ? (int)pending[costId]! : 0;
}

void AssertManagerEmpty(string scenario)
{
    Equal(0, ((IDictionary)managerPending.GetValue(null)!).Count,
        scenario + " manager pending count");
    Assert(!(bool)managerDirty.GetValue(null)!, scenario + " manager dirty flag");
}

static void AssertSingleRefresh(TPlayObject target, string scenario)
{
    Equal(1, target.m_MsgList.Count, scenario + " message count");
    Equal(Grobal2.RM_LINGFU_CHANGED, target.m_MsgList[0].wIdent,
        scenario + " refresh Ident");
}

static void AssertNoOutput(TPlayObject target, string scenario)
{
    Equal(0, target.m_MsgList.Count, scenario + " message count");
    Equal(0, M2Share.LogStringList.Count, scenario + " game log count");
}

static NativeCreditCardService CreateMonthlyService()
{
    var constructor = typeof(NativeCreditCardService).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(bool), typeof(bool), typeof(string), typeof(byte[]) }, null);
    Assert(constructor != null, "NativeCreditCardService constructor is missing");
    var switches = new byte[5];
    switches[2] = 0x08;
    return (NativeCreditCardService)constructor.Invoke(
        new object[] { false, false, string.Empty, switches });
}

static FieldInfo RequiredField(Type type, string name) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("missing field: " + name);

static FieldInfo RequiredStaticField(Type type, string name) =>
    type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("missing static field: " + name);

static MethodInfo RequiredStaticMethod(Type type, string name) =>
    type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("missing static method: " + name);

static string LogAt(int index) => (string)M2Share.LogStringList[index]!;

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

static void Require(string source, string marker, string message)
{
    if (!source.Contains(marker, StringComparison.Ordinal))
        throw new InvalidOperationException(message + " marker missing: " + marker);
}

static void Reject(string source, string marker, string message)
{
    if (source.Contains(marker, StringComparison.Ordinal))
        throw new InvalidOperationException(message + ": " + marker);
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
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualLong(long expected, long actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{message}: expected [{expected}], actual [{actual}]");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
