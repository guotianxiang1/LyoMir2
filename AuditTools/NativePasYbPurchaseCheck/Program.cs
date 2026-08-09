using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.ProcessHumanCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new System.Collections.ArrayList();

CheckValidation();
CheckExtendedSubmissionRoute();
CheckNormalSubmissionNpcState();
CheckLiveNpcCallbackTarget();
CheckInvalidUserIdStore();
CheckCallbackArguments();
CheckSingleFlight();
CheckSuccessfulTransaction();
CheckDebitFailure();
CheckPreDebitFailures();
CheckCallbackExceptionStillFinalizes();
CheckSqlContract();
CheckAccountLogContract();

Console.WriteLine("NativePasYbPurchaseCheck PASS");

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

static void CheckValidation()
{
    Equal(false, ValidateNormal("cb", 20000, 1, 1, out _),
        "normal vsId lower boundary");
    Equal(true, ValidateNormal("cb", 20001, 32767, 2, out var maximum),
        "normal total 65534");
    Equal(65534, maximum.TotalCost, "normal total value");
    Equal(false, ValidateNormal("cb", 20001, 21845, 3, out _),
        "normal total 65535");
    Equal(false, ValidateNormal("cb", 20001, int.MaxValue, 2, out _),
        "normal multiplication overflow");
    Equal(false, ValidateNormal("", 20001, 1, 1, out _),
        "empty callback");
    Equal(true, ValidateNormal(new string('中', 6) + "a", 20001, 1, 1,
        out var callback13), "callback 13 GBK bytes");
    Equal(14, callback13.CallbackBytes.Length,
        "stored callback includes at sign");
    Equal((byte)'@', callback13.CallbackBytes[0], "callback at sign");
    Equal(false, ValidateNormal(new string('中', 7), 20001, 1, 1, out _),
        "callback 14 GBK bytes");

    Equal(true, NativePasYbPurchaseValidation.TryValidateYbShop(1, "cb",
        "goods", 1, 1, out _), "tag1 accepted");
    Equal(true, NativePasYbPurchaseValidation.TryValidateYbShop(3, "cb",
        "goods", 1, 1, out _), "unknown nonzero tag accepted");
    Equal(false, NativePasYbPurchaseValidation.TryValidateYbShop(0, "cb",
        "goods", 1, 1, out _), "tag0 rejected");
    Equal(true, NativePasYbPurchaseValidation.TryValidateYbShop(2, "cb",
        new string('a', 19) + "中", 1, 1, out var ybShop),
        "tag2 accepted");
    Equal(20, ybShop.DescriptorBytes.Length,
        "descriptor ShortString byte truncation");
    var chinese = HUtil32.GbkEncoding.GetBytes("中");
    Equal(chinese[0], ybShop.DescriptorBytes[^1],
        "descriptor preserves half DBCS tail");

    var identity = new NativePasYbPurchase(NativePasYbPurchaseRoute.Npc, 1,
        new string('a', 19) + "中", new string('b', 14) + "中", maximum,
        20001, 32767, 2, 65534, null, null);
    Equal(20, identity.AccountBytes.Length, "PTID raw byte boundary");
    Equal(15, identity.CharacterNameBytes.Length,
        "character raw byte boundary");
    Equal(chinese[0], identity.AccountBytes[^1],
        "PTID preserves half DBCS tail");
    Equal(chinese[0], identity.CharacterNameBytes[^1],
        "character preserves half DBCS tail");
}

static void CheckExtendedSubmissionRoute()
{
    var player = new TPlayObject();
    foreach (var executionTag in new byte[] { 1, 2, 3, byte.MaxValue })
    {
        NativePasYbPurchase captured = null;
        Equal(true, NativePasYbPurchaseService.TrySubmitYbShop(player,
            executionTag, "cb", "goods", 7, 1, 1, purchase =>
            {
                captured = purchase;
                return true;
            }), $"tag{executionTag} submission");
        Equal(executionTag, captured.ExecutionTag,
            $"tag{executionTag} preserved");
    }

    var submitted = false;
    Equal(false, NativePasYbPurchaseService.TrySubmitYbShop(player,
        0, "cb", "goods", 7, 1, 1, _ =>
        {
            submitted = true;
            return true;
        }), "tag0 submission");
    Equal(false, submitted, "tag0 reached submit callback");
}

static void CheckNormalSubmissionNpcState()
{
    var player = new TPlayObject();
    var race10Npc = new NormNpc();
    NativePasYbPurchase captured = null;
    Equal(true, NativePasYbPurchaseService.TrySubmitNormal(player, race10Npc,
        "cb", 20001, 1, 1, purchase =>
        {
            captured = purchase;
            return true;
        }), "race10 normal submission");
    Equal(race10Npc, player.m_NPC,
        "accepted normal submission stores current NPC");
    Equal<NpcPasScriptInteractionHandle>(null, captured.NpcInteraction,
        "normal submission does not capture NPC interaction");

    var race0Npc = new NormNpc { m_btRaceServer = 0 };
    Equal(true, NativePasYbPurchaseService.TrySubmitNormal(player, race0Npc,
        "cb", 20001, 1, 1, _ => true), "race0 normal submission");
    Equal(race0Npc, player.m_NPC,
        "race0 submission replaces current NPC");

    var rejectedNpc = new NormNpc();
    Equal(false, NativePasYbPurchaseService.TrySubmitNormal(player,
        rejectedNpc, "cb", 20001, 1, 1, _ => false),
        "rejected normal submission");
    Equal(race0Npc, player.m_NPC,
        "rejected submission preserves current NPC");

    var invalidRaceNpc = new NormNpc { m_btRaceServer = 11 };
    var invalidSubmitCalled = false;
    Equal(false, NativePasYbPurchaseService.TrySubmitNormal(player,
        invalidRaceNpc, "cb", 20001, 1, 1, _ =>
        {
            invalidSubmitCalled = true;
            return true;
        }), "invalid race normal submission");
    Equal(false, invalidSubmitCalled, "invalid race rejected before submit");
    Equal(race0Npc, player.m_NPC,
        "invalid race preserves current NPC");
}

static void CheckLiveNpcCallbackTarget()
{
    var player = new TPlayObject();
    var purchase = NewPurchase(NativePasYbPurchaseRoute.Npc, 2,
        "cb", string.Empty, 20001, 1, 1, 1, player);
    var submittedNpc = new NormNpc();
    player.m_NPC = submittedNpc;
    Equal(true, NativePasYbProductionRuntime.TryGetCurrentNpcCallbackTarget(
        purchase, player, out var target), "submitted NPC callback target");
    Equal(submittedNpc, target, "submitted NPC selected");

    var switchedRace10Npc = new NormNpc();
    player.m_NPC = switchedRace10Npc;
    Equal(true, NativePasYbProductionRuntime.TryGetCurrentNpcCallbackTarget(
        purchase, player, out target), "switched race10 callback target");
    Equal(switchedRace10Npc, target,
        "completion routes switched race10 NPC");

    var switchedRace0Npc = new NormNpc { m_btRaceServer = 0 };
    player.m_NPC = switchedRace0Npc;
    Equal(true, NativePasYbProductionRuntime.TryGetCurrentNpcCallbackTarget(
        purchase, player, out target), "switched race0 callback target");
    Equal(switchedRace0Npc, target, "completion routes switched race0 NPC");

    player.m_NPC = null;
    Equal(false, NativePasYbProductionRuntime.TryGetCurrentNpcCallbackTarget(
        purchase, player, out _), "null current NPC skips callback");

    player.m_NPC = new NormNpc { m_btRaceServer = 11 };
    Equal(false, NativePasYbProductionRuntime.TryGetCurrentNpcCallbackTarget(
        purchase, player, out _), "invalid current NPC race skips callback");

    var replacementPlayer = new TPlayObject { m_NPC = switchedRace10Npc };
    Equal(false, NativePasYbProductionRuntime.TryGetCurrentNpcCallbackTarget(
        purchase, replacementPlayer, out _),
        "replacement player object skips callback");
}

static void CheckInvalidUserIdStore()
{
    var invalid = NewPurchase(NativePasYbPurchaseRoute.Npc, 0,
        "cb", string.Empty, 20001, 1, 1, 1);
    Equal(-1, NativePasYbPurchaseStore.Begin(invalid),
        "UserId zero fails before script log or debit");
}

static void CheckCallbackArguments()
{
    var normal = NewPurchase(NativePasYbPurchaseRoute.Npc, 10,
        "NormalCb", string.Empty, 20001, 17, 3, 100);
    var normalArgs = NativePasYbProductionRuntime.BuildCallbackArguments(normal);
    Equal(2, normalArgs.Length, "normal callback arity");
    Equal(17, normalArgs[0].AsInt(), "normal callback price first");
    Equal(3, normalArgs[1].AsInt(), "normal callback quantity second");

    var ybShop = NewPurchase(NativePasYbPurchaseRoute.YbShop, 11,
        "YBShopBuy_YB", "屠龙", 7, 29, 2, 100);
    var ybShopArgs = NativePasYbProductionRuntime.BuildCallbackArguments(ybShop);
    Equal(3, ybShopArgs.Length, "YBShop callback arity");
    Equal("屠龙", ybShopArgs[0].AsString(),
        "YBShop callback descriptor first");
    Equal(29, ybShopArgs[1].AsInt(), "YBShop callback price second");
    Equal(2, ybShopArgs[2].AsInt(), "YBShop callback quantity third");

    var taskDispatch = NewPurchase(NativePasYbPurchaseRoute.TaskDispatch, 12,
        "TaskCb", "ignored", 8, 31, 2, 100);
    var taskArgs = NativePasYbProductionRuntime.BuildCallbackArguments(
        taskDispatch);
    Equal(2, taskArgs.Length, "TaskDispatch callback arity");
    Equal(31, taskArgs[0].AsInt(), "TaskDispatch price first");
    Equal(2, taskArgs[1].AsInt(), "TaskDispatch quantity second");
}

static void CheckSingleFlight()
{
    var harness = new Harness();
    var first = NewPurchase(NativePasYbPurchaseRoute.Npc, 21,
        "cb", string.Empty, 20001, 1, 1, 10);
    var duplicate = NewPurchase(NativePasYbPurchaseRoute.YbShop, 21,
        "cb2", "goods", 2, 1, 1, 10);
    Equal(true, harness.Transaction.TryReserve(first), "first reserve");
    Equal(false, harness.Transaction.TryReserve(duplicate),
        "duplicate UserId reserve");
    Equal(1, harness.Transaction.PendingCount, "single pending UserId");
}

static void CheckSuccessfulTransaction()
{
    var harness = new Harness { CallbackResult = false };
    var purchase = NewPurchase(NativePasYbPurchaseRoute.YbShop, 31,
        "YBShopBuy_YB", "goods", 2, 10, 2, 100);
    Equal(true, harness.Transaction.TryReserve(purchase), "success reserve");
    harness.Transaction.Stage(purchase);
    Sequence(new[] { "begin", "debit" }, harness.Events,
        "success begin order");
    Equal(73, purchase.ScriptLogId, "script log id propagated");

    harness.Debit.Before(new NativeYuanbaoResult(0, 80));
    harness.Debit.Complete(new NativeYuanbaoResult(0, 80));
    Sequence(new[] { "begin", "debit", "apply:80", "callback", "true:73" },
        harness.Events, "success completion order");
    Equal(0, harness.Transaction.PendingCount,
        "reserve removed before terminal callback");
    Equal(1, harness.Store.TrueCount,
        "callback false still marks script log True");
    Equal(Environment.CurrentManagedThreadId, harness.Store.SetTrueThreadId,
        "SetTrue runs synchronously on debit completion thread");
    Equal(false, PasValue.Nil.AsBool(), "Nil callback result is false");
}

static void CheckDebitFailure()
{
    var harness = new Harness();
    var purchase = NewPurchase(NativePasYbPurchaseRoute.Npc, 41,
        "cb", string.Empty, 20001, 10, 2, 100);
    Equal(true, harness.Transaction.TryReserve(purchase), "failure reserve");
    harness.Transaction.Stage(purchase);
    harness.Debit.Before(new NativeYuanbaoResult(
        NativeYuanbaoManager.InsufficientBalance, 100));
    harness.Debit.Complete(new NativeYuanbaoResult(
        NativeYuanbaoManager.InsufficientBalance, 100));
    Sequence(new[] { "begin", "debit", "failure:-1500002" }, harness.Events,
        "debit failure order");
    Equal(0, harness.Transaction.PendingCount, "failure releases reserve");
    Equal(0, harness.Store.TrueCount,
        "debit failure leaves log Undetermined");
    Equal(0, harness.CallbackCount, "debit failure skips callback");
}

static void CheckPreDebitFailures()
{
    var insufficient = new Harness();
    var purchase = NewPurchase(NativePasYbPurchaseRoute.Npc, 51,
        "cb", string.Empty, 20001, 6, 1, 5);
    Equal(true, insufficient.Transaction.TryReserve(purchase),
        "snapshot reserve");
    insufficient.Transaction.Stage(purchase);
    Equal(0, insufficient.Transaction.PendingCount,
        "snapshot failure releases before main completion");
    Equal(0, insufficient.Store.BeginCount,
        "snapshot failure skips script log");
    Equal(1, insufficient.Posted.Count,
        "snapshot failure marshalled to main thread");
    insufficient.DrainPosted();
    Sequence(new[] { "failure:-99" }, insufficient.Events,
        "snapshot failure report");

    var logFailure = new Harness { BeginResult = -1 };
    var logPurchase = NewPurchase(NativePasYbPurchaseRoute.Npc, 52,
        "cb", string.Empty, 20001, 1, 1, 5);
    Equal(true, logFailure.Transaction.TryReserve(logPurchase),
        "log failure reserve");
    logFailure.Transaction.Stage(logPurchase);
    logFailure.DrainPosted();
    Sequence(new[] { "begin", "failure:-1500003" }, logFailure.Events,
        "log failure order");
    Equal(0, logFailure.Debit.EnqueueCount,
        "log failure skips debit");

    var enqueueFailure = new Harness { DebitAccepted = false };
    var enqueuePurchase = NewPurchase(NativePasYbPurchaseRoute.Npc, 53,
        "cb", string.Empty, 20001, 1, 1, 5);
    Equal(true, enqueueFailure.Transaction.TryReserve(enqueuePurchase),
        "enqueue failure reserve");
    enqueueFailure.Transaction.Stage(enqueuePurchase);
    enqueueFailure.DrainPosted();
    Sequence(new[] { "begin", "debit", "failure:-1500003" },
        enqueueFailure.Events, "enqueue failure order");
    Equal(0, enqueueFailure.Store.TrueCount,
        "enqueue failure leaves log Undetermined");
}

static void CheckCallbackExceptionStillFinalizes()
{
    var harness = new Harness { ThrowInCallback = true };
    var purchase = NewPurchase(NativePasYbPurchaseRoute.Npc, 61,
        "cb", string.Empty, 20001, 1, 1, 5);
    Equal(true, harness.Transaction.TryReserve(purchase), "throw reserve");
    harness.Transaction.Stage(purchase);
    harness.Debit.Before(new NativeYuanbaoResult(0, 4));
    try
    {
        harness.Debit.Complete(new NativeYuanbaoResult(0, 4));
        throw new InvalidOperationException("callback exception was swallowed");
    }
    catch (TestCallbackException)
    {
    }
    Sequence(new[] { "begin", "debit", "apply:4", "callback", "true:73" },
        harness.Events, "callback exception finalization order");
}

static void CheckSqlContract()
{
    Equal(
        "Insert into gamelog.YB_Script_Buy_Log(" +
        "UpdateTime, PTID, UserID, CharName, CostType, PsBkFuncName, " +
        "CostNum,UseCredit, Status, CurrentCredit) " +
        "Select Now(), @account, @userId, @characterName, @costType, " +
        "@callback, @costNum, @useCredit, \"Undetermined\", YBNUM " +
        "from gamedata.yb_user_data where UserID = @userId Limit 1;",
        NativePasYbPurchaseStore.InsertSql, "script purchase INSERT");
    Equal(
        "Update gamelog.YB_Script_Buy_Log set Status=\"True\" " +
        "where idx=@scriptLogId and Status=\"Undetermined\";",
        NativePasYbPurchaseStore.SetTrueSql, "script purchase True update");
}

static void CheckAccountLogContract()
{
    var callback = HUtil32.GbkEncoding.GetBytes("@cb");
    var descriptor = HUtil32.GbkEncoding.GetBytes("goods");
    var request = NativeYuanbaoRequest.CreatePasScriptPurchase(71,
        "account", "character", 30, 73, 20009, 3, callback, descriptor,
        null, _ => { });
    Equal(NativeYuanbaoRequestKind.PasScriptPurchase, request.Kind,
        "request kind");
    Equal(73, request.GoodsIndex, "request script log id");
    Equal(3, request.GoodsCount, "request quantity");
    Equal(20009, request.ReferenceId, "request vsId");
    Equal("@cb", Decode(request.ActionBytes), "request callback bytes");

    var begin = NativeAccountLogRecord.CreatePasScriptPurchase(request,
        NativeAccountLogManager.PasScriptPurchaseBeginStage, 0);
    var end = NativeAccountLogRecord.CreatePasScriptPurchase(request,
        NativeAccountLogManager.PasScriptPurchaseEndStage, -1500002);
    Equal("ypScriptBuy", begin.PayType, "account log pay type");
    Equal("20009", Encoding.ASCII.GetString(begin.OrderIdBytes),
        "account log vsId order id");
    Equal(73, begin.ItemIndex, "account log script log id");
    Equal(3, begin.ItemCount, "account log quantity");
    Equal(30, begin.Amount, "account log total");
    Equal("@cb", Decode(begin.FromWhoBytes), "account log callback");
    Equal("ltYBScriptBuyBegin", begin.AccountAction,
        "account log begin action");
    Equal("ltYBScriptBuyEnd", end.AccountAction, "account log end action");
    Equal(-1500002, end.Result, "account log end result");
}

static bool ValidateNormal(string callbackName, int vsId, int unitPrice,
    int quantity, out NativePasYbValidatedArguments arguments) =>
    NativePasYbPurchaseValidation.TryValidateNormal(callbackName, vsId,
        unitPrice, quantity, out arguments);

static NativePasYbPurchase NewPurchase(NativePasYbPurchaseRoute route,
    long userId, string callbackName, string descriptor, int vsId,
    int unitPrice, int quantity, int balance,
    TPlayObject originalPlayer = null)
{
    NativePasYbValidatedArguments arguments;
    var valid = route == NativePasYbPurchaseRoute.Npc
        ? NativePasYbPurchaseValidation.TryValidateNormal(callbackName, vsId,
            unitPrice, quantity, out arguments)
        : NativePasYbPurchaseValidation.TryValidateYbShop((byte)route, callbackName,
            descriptor, unitPrice, quantity, out arguments);
    Equal(true, valid, "test purchase validation");
    return new NativePasYbPurchase(route, userId, "account", "character",
        arguments, vsId, unitPrice, quantity, balance, originalPlayer, null);
}

static string Decode(byte[] bytes) => HUtil32.GbkEncoding.GetString(bytes);

static void Sequence(IReadOnlyList<string> expected,
    IReadOnlyList<string> actual, string name)
{
    Equal(expected.Count, actual.Count, name + " count");
    for (var i = 0; i < expected.Count; i++)
        Equal(expected[i], actual[i], name + "[" + i + "]");
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{name}: expected {expected}, got {actual}");
}

sealed class Harness
{
    internal List<string> Events { get; } = new();
    internal Queue<Action> Posted { get; } = new();
    internal FakeStore Store { get; }
    internal FakeDebitQueue Debit { get; }
    internal NativePasYbPurchaseTransaction Transaction { get; }
    internal int BeginResult { set => Store.BeginResult = value; }
    internal bool DebitAccepted { set => Debit.Accepted = value; }
    internal bool CallbackResult { get; set; } = true;
    internal bool ThrowInCallback { get; set; }
    internal int CallbackCount { get; private set; }

    internal Harness()
    {
        Store = new FakeStore(Events);
        Debit = new FakeDebitQueue(Events);
        Transaction = new NativePasYbPurchaseTransaction(Store, Debit,
            new FakeRuntime(this), action => Posted.Enqueue(action));
    }

    internal void DrainPosted()
    {
        while (Posted.Count != 0) Posted.Dequeue()();
    }

    private sealed class FakeRuntime : INativePasYbPurchaseRuntime
    {
        private readonly Harness _owner;

        internal FakeRuntime(Harness owner) => _owner = owner;

        public void ApplyBalance(NativePasYbPurchase purchase,
            NativeYuanbaoResult result) =>
            _owner.Events.Add("apply:" + result.Balance);

        public void ReportFailure(NativePasYbPurchase purchase, int errorCode) =>
            ReportAfterRelease(errorCode);

        public bool InvokeCallback(NativePasYbPurchase purchase)
        {
            if (_owner.Transaction.PendingCount != 0)
                throw new InvalidOperationException(
                    "reserve must be released before callback");
            _owner.CallbackCount++;
            _owner.Events.Add("callback");
            if (_owner.ThrowInCallback) throw new TestCallbackException();
            return _owner.CallbackResult;
        }

        private void ReportAfterRelease(int errorCode)
        {
            if (_owner.Transaction.PendingCount != 0)
                throw new InvalidOperationException(
                    "reserve must be released before failure reporting");
            _owner.Events.Add("failure:" + errorCode);
        }
    }
}

sealed class FakeStore : INativePasYbPurchaseStore
{
    private readonly List<string> _events;
    internal int BeginResult { get; set; } = 73;
    internal int BeginCount { get; private set; }
    internal int TrueCount { get; private set; }
    internal int SetTrueThreadId { get; private set; }

    internal FakeStore(List<string> events) => _events = events;

    public int Begin(NativePasYbPurchase purchase)
    {
        BeginCount++;
        _events.Add("begin");
        return BeginResult;
    }

    public void SetTrueBestEffort(int scriptLogId)
    {
        TrueCount++;
        SetTrueThreadId = Environment.CurrentManagedThreadId;
        _events.Add("true:" + scriptLogId);
    }
}

sealed class FakeDebitQueue : INativePasYbDebitQueue
{
    private readonly List<string> _events;
    internal bool Accepted { get; set; } = true;
    internal int EnqueueCount { get; private set; }
    internal Action<NativeYuanbaoResult> Before { get; private set; }
    internal Action<NativeYuanbaoResult> Complete { get; private set; }

    internal FakeDebitQueue(List<string> events) => _events = events;

    public bool Enqueue(NativePasYbPurchase purchase,
        Action<NativeYuanbaoResult> beforeCompletion,
        Action<NativeYuanbaoResult> completion)
    {
        EnqueueCount++;
        _events.Add("debit");
        Before = beforeCompletion;
        Complete = completion;
        return Accepted;
    }
}

sealed class TestCallbackException : Exception
{
}
