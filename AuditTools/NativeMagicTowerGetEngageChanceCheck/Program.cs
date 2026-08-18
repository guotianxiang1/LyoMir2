using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.RandomNumber = RandomNumber.GetInstance();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();
M2Share.CreditCardService = NativeCreditCardService.Disabled;

CheckAbiAndPropertyGate();
CheckPendingAndCountFailures();
CheckInsufficientLingFu();
CheckOrdinaryDebitAndRepeat();
CheckCreditValue2Debit();
CheckCreditValueDebit();
CheckOrdinaryFallback();
CheckSourceOrder();

Console.WriteLine(
    "PASS NativeMagicTowerGetEngageChanceCheck abi=npc-function(player) " +
    "gate=property12/pending/sbyte-count debit=Value2/Value/ordinary " +
    "order=used/10054/dual-reason/selector101/pending failures=exact");

static void CheckAbiAndPropertyGate()
{
    var context = NewContext(addProperty: false);
    var before = Snapshot(context.Player);
    Assert(!context.Player.GetNativeMagicTowerEngageChance(context.Npc),
        "property gate was accepted");
    Equal(before, Snapshot(context.Player), "property gate side effects");
    Equal(0, context.Player.m_MsgList.Count, "property gate was not silent");
    Equal(0, M2Share.LogStringList.Count, "property gate wrote a log");

    Assert(context.Bridge.CallNpcFunc("GetEngageChance", context.Args,
        out var result), "GetEngageChance valid function ABI was rejected");
    Assert(result.Type == PasValueType.Boolean && !result.AsBool(),
        "property-gated GetEngageChance result mismatch");
    Assert(!context.Bridge.CallNpcMethod("GetEngageChance", context.Args,
        out _), "function was exposed through the procedure dispatcher");
    Assert(!context.Bridge.CallNpcFunc("GetEngageChance",
        new List<PasValue>(), out _),
        "GetEngageChance accepted a missing player argument");
    Assert(!context.Bridge.CallNpcFunc("GetEngageChance",
        new List<PasValue> { PasValue.FromInt(1) }, out _),
        "GetEngageChance accepted a non-player argument");
    Assert(!context.Bridge.CallNpcFunc("GetEngageChance",
        new List<PasValue>
        {
            PasValue.FromObject(context.Player), PasValue.FromInt(1)
        }, out _), "GetEngageChance accepted an extra argument");
}

static void CheckPendingAndCountFailures()
{
    var pending = NewContext();
    SetField(pending.Player, "m_btNativeMagicTowerEngageChance", (byte)1);
    pending.Player.m_nLingFu = 5;
    AssertFalse(pending, "pending chance");
    Equal("tower-npc/你尚有一次召唤弓箭手的机会\\您可以选择“摆放弓箭手位置”进行摆放。",
        MerchantMessage(pending.Player), "pending chance dialog");
    Equal(5, pending.Player.m_nLingFu, "pending chance debit");

    var limit = NewContext();
    SetField(limit.Player, "m_sbNativeMagicTowerArcherCount", (sbyte)10);
    limit.Player.m_nLingFu = 5;
    AssertFalse(limit, "archer limit");
    Equal("tower-npc/你已经拥有了10个弓箭手，不能继续。",
        MerchantMessage(limit.Player), "archer limit dialog");
    Equal(5, limit.Player.m_nLingFu, "archer limit debit");

    var signed = NewContext();
    SetField(signed.Player, "m_sbNativeMagicTowerArcherCount", (sbyte)-1);
    signed.Player.m_nLingFu = 1;
    AssertTrue(signed, "signed negative archer count");
}

static void CheckInsufficientLingFu()
{
    var context = NewContext();
    var before = Snapshot(context.Player);
    AssertFalse(context, "insufficient LingFu");
    Equal(before, Snapshot(context.Player), "insufficient LingFu side effects");
    Equal("tower-npc/召唤弓箭手需要1张灵符",
        MerchantMessage(context.Player), "insufficient LingFu dialog");
    Equal(0, context.Player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE),
        "generic insufficient LingFu message leaked");
    Equal(0, M2Share.LogStringList.Count, "insufficient LingFu log");
}

static void CheckOrdinaryDebitAndRepeat()
{
    var context = NewContext();
    context.Player.m_nLingFu = 2;
    context.Player.m_nUsedLingFu = 4;
    AssertTrue(context, "ordinary LingFu");
    Equal(1, context.Player.m_nLingFu, "ordinary LingFu balance");
    Equal(5, context.Player.m_nUsedLingFu, "used LingFu");
    Equal((byte)1, ReadField<byte>(context.Player,
        "m_btNativeMagicTowerEngageChance"), "pending chance commit");
    Equal(1, context.Player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_LINGFU_CHANGED), "10054 refresh count");
    Equal(1, ReadReasonBucket(context.Player,
        "m_NativeLingFuReasonSessionBuckets", 1), "session reason 1");
    Equal(1, ReadReasonBucket(context.Player,
        "m_NativeLingFuReasonBuckets", 1), "persistent reason 1");
    Equal(1, M2Share.LogStringList.Count, "selector 101 log count");
    // Re-based 2026-08-04: the reason field is 战神 sub_646F40 @0x646F89 `mov dl,1`, not 0.
    // This pin previously encoded production's hardcoded 0, so it was green while the
    // divergence was live — a false green, not evidence. The same `ebx` also indexes the two
    // accumulators asserted just above (@0x6D245C / @0x6D2463, both `& 0x7F` @0x6D2459),
    // which already used 1, so the log field was the odd one out.
    Equal("101\tplayer-map\t10\t20\tplayer\t魔王岭消耗灵符\t1\t1\ttower-npc-npc-map",
        (string)M2Share.LogStringList[0], "selector 101 log payload");

    context.Player.m_MsgList.Clear();
    M2Share.LogStringList.Clear();
    AssertFalse(context, "repeat call");
    Equal(1, context.Player.m_nLingFu, "repeat call LingFu balance");
    Equal(5, context.Player.m_nUsedLingFu, "repeat call used LingFu");
    Equal(0, context.Player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_LINGFU_CHANGED), "repeat call refresh");
    Equal(0, M2Share.LogStringList.Count, "repeat call log");
}

static void CheckCreditValue2Debit()
{
    WithCreditService(() =>
    {
        var context = NewContext();
        context.Player.m_CreditCard.Loaded = true;
        context.Player.m_CreditCard.Value2 = 2;
        context.Player.m_CreditCard.Value = 3;
        context.Player.m_CreditCard.UsedValue = 4;
        context.Player.m_nLingFu = 7;
        AssertTrue(context, "credit Value2");
        Equal(1, context.Player.m_CreditCard.Value2, "Value2 debit");
        Equal(3, context.Player.m_CreditCard.Value, "Value isolation");
        Equal(4, context.Player.m_CreditCard.UsedValue,
            "Value2 UsedValue isolation");
        Equal(7, context.Player.m_nLingFu, "Value2 ordinary isolation");
        Assert(context.Player.m_CreditCard.Dirty, "Value2 dirty flag");
    });
}

static void CheckCreditValueDebit()
{
    WithCreditService(() =>
    {
        var context = NewContext();
        context.Player.m_CreditCard.Loaded = true;
        context.Player.m_CreditCard.Value = 3;
        context.Player.m_CreditCard.UsedValue = 4;
        AssertTrue(context, "credit Value");
        Equal(2, context.Player.m_CreditCard.Value, "Value debit");
        Equal(5, context.Player.m_CreditCard.UsedValue,
            "Value UsedValue accounting");
    });
}

static void CheckOrdinaryFallback()
{
    WithCreditService(() =>
    {
        var context = NewContext();
        context.Player.m_CreditCard.Loaded = true;
        context.Player.m_CreditCard.Value = -1;
        context.Player.m_CreditCard.Value2 = 1;
        context.Player.m_nLingFu = 2;
        AssertTrue(context, "ordinary fallback");
        Equal(1, context.Player.m_nLingFu, "ordinary fallback debit");
        Equal(-1, context.Player.m_CreditCard.Value,
            "ordinary fallback Value isolation");
        Equal(1, context.Player.m_CreditCard.Value2,
            "ordinary fallback Value2 isolation");
    });
}

static void CheckSourceOrder()
{
    var root = FindRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeMagicTower.cs"));
    var start = RequiredIndex(source,
        "internal bool GetNativeMagicTowerEngageChance(NormNpc npc)",
        "transaction method");
    var end = RequiredIndex(source, "internal void EnterNativeMagicTowerRoute",
        "next method", start);
    var method = source[start..end];
    var used = RequiredIndex(method, "m_nUsedLingFu = unchecked",
        "used LingFu update");
    var refresh = RequiredIndex(method, "RefreshNativeLingFu();",
        "10054 refresh");
    var counters = RequiredIndex(method, "AddNativeMagicTowerLingFuUsage();",
        "dual reason counters");
    var log = RequiredIndex(method, "M2Share.AddGameDataLog",
        "selector 101 log");
    var pending = RequiredIndex(method,
        "m_btNativeMagicTowerEngageChance = 1;", "pending commit");
    Assert(used < refresh && refresh < counters && counters < log &&
           log < pending, "success side-effect order drifted");
}

static Context NewContext(bool addProperty = true)
{
    M2Share.LogStringList.Clear();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "player",
        m_sMapName = "player-map",
        m_nCurrX = 10,
        m_nCurrY = 20
    };
    var npc = new NormNpc
    {
        m_sCharName = "tower-npc",
        m_sMapName = "npc-map"
    };
    if (addProperty) npc.AddNativePasProperty(12);
    var bridge = new PasApiBridge
    {
        CurrentPlayer = player,
        CurrentNpc = npc
    };
    return new Context(player, npc, bridge,
        new List<PasValue> { PasValue.FromObject(player) });
}

static void AssertTrue(Context context, string name)
{
    Assert(context.Player.GetNativeMagicTowerEngageChance(context.Npc),
        name + " transaction was not accepted");
}

static void AssertFalse(Context context, string name)
{
    Assert(!context.Player.GetNativeMagicTowerEngageChance(context.Npc),
        name + " transaction was accepted");
}

static string MerchantMessage(TPlayObject player)
{
    var messages = player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_MERCHANTSAY).ToArray();
    Equal(1, messages.Length, "merchant dialog count");
    return messages[0].Buff;
}

static PlayerSnapshot Snapshot(TPlayObject player) => new(
    player.m_nLingFu, player.m_nUsedLingFu, player.m_CreditCard.Value,
    player.m_CreditCard.Value2, player.m_CreditCard.UsedValue,
    player.m_CreditCard.Dirty,
    ReadField<byte>(player, "m_btNativeMagicTowerEngageChance"),
    ReadField<sbyte>(player, "m_sbNativeMagicTowerArcherCount"));

static int ReadReasonBucket(TPlayObject player, string fieldName, int index)
{
    var buckets = ReadField<int[]>(player, fieldName);
    return buckets[index];
}

static T ReadField<T>(object target, string name)
{
    var field = target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert(field != null, "missing field " + name);
    return (T)field.GetValue(target);
}

static void SetField<T>(object target, string name, T value)
{
    var field = target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert(field != null, "missing field " + name);
    field.SetValue(target, value);
}

static void WithCreditService(Action action)
{
    var previous = M2Share.CreditCardService;
    try
    {
        M2Share.CreditCardService = CreateCreditCardService();
        action();
    }
    finally
    {
        M2Share.CreditCardService = previous;
    }
}

static NativeCreditCardService CreateCreditCardService()
{
    var constructor = typeof(NativeCreditCardService).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(bool), typeof(bool), typeof(string), typeof(byte[]) },
        null);
    Assert(constructor != null, "credit service constructor not found");
    return (NativeCreditCardService)constructor.Invoke(
        new object[] { true, false, string.Empty, new byte[5] });
}

static string FindRoot()
    => AuditRepoRoot.Resolve();

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

static int RequiredIndex(string text, string value, string name, int start = 0)
{
    var index = text.IndexOf(value, start, StringComparison.Ordinal);
    Assert(index >= 0, name + " source marker missing");
    return index;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

readonly record struct Context(TPlayObject Player, NormNpc Npc,
    PasApiBridge Bridge, List<PasValue> Args);

readonly record struct PlayerSnapshot(int LingFu, int UsedLingFu,
    int CreditValue, int CreditValue2, int CreditUsedValue, bool CreditDirty,
    byte PendingChance, sbyte ArcherCount);
