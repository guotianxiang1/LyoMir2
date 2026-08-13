using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var root = FindRepositoryRoot();
var assembly = typeof(TPlayObject).Assembly;

CheckEntryAndMessages(root);
CheckPurchaseSql(assembly, root);
CheckRequestAndAccountLogs(assembly);
CheckAsyncStateMachine(root);
CheckLivePlayerCallback(assembly);
CheckLingFuDelivery(root);
CheckPasDispatch(root);

Console.WriteLine(
    "PASS ClientYBbuyLF sync=guards+BuyLog async=25>1>2>3>26 " +
    "live=UserID+account+character+ghost " +
    "failure=Undetermined delivery=LingFu+YBConsume+Credit messages=GBK-exact");
return;

static void CheckEntryAndMessages(string root)
{
    var type = typeof(TPlayObject);
    Equal("每次兑换不能超过1000张。\\ \\ <返回/@main>",
        Constant(type, "YbShopExchangeLimitMessage"), "exchange limit message");
    Equal("您身上没有那么多的元宝。\\ \\ <返回/@main>",
        Constant(type, "YbShopInsufficientLocalBalanceMessage"),
        "local balance message");
    Equal("元宝系统暂时关闭中...\\ \\ \\ <返回/@main>",
        Constant(type, "YbShopSubmitFailedMessage"), "dead submit message");
    Equal("[失败]: 不存在你想购买的物品: 灵符",
        Constant(type, "YbShopGoodsMissingMessage"), "BuyLog -1 message");

    var source = Read(root, "GameSvr", "Players", "TPlayObject.NativeYbShop.cs");
    var entry = Slice(source, "public void ClientYBbuyLF(",
        "private static void PrepareNativeYbShopYuanbaoCompletion(");
    Ordered(entry,
        "amount is < 1 or > 1000",
        "amount > m_nGameGold",
        "TryGetNativeMailRecipientId(out var userId)",
        "NativeYbShopPurchaseStore.Begin(",
        "buyLogId <= 0",
        "MallManager.Instance.InvalidateHotItems()",
        "NativeYuanbaoRequest.CreateYbShop(",
        "request.SetBeforeOuterCompletionLog(",
        "NativeYuanbaoManager.Enqueue(request)");
    Equal(2, Count(entry, "YbShopGoodsMissingMessage"),
        "recipient/BuyLog failures share native -1 text");
    Require(entry, "if (NativeYuanbaoManager.Enqueue(request)) return;",
        "valid request does not use the dead submit-failed branch");
    Require(entry,
        "result => CompleteNativeYbShopPurchase(request, result)",
        "shop completion must not capture the initiating player");
    Require(entry,
        "result => PrepareNativeYbShopYuanbaoCompletion(request, result)",
        "balance completion must resolve from the request identity");
    Reject(entry, "SetStatusBestEffort", "enqueue failure must not finalize BuyLog");

    Require(source, "var message = \"您成功购买了\" + amount + \"张灵符\";",
        "success prefix drifted from native GBK bytes");
    Require(source, "message + \"。 \\\\ \\\\<返回/@Main>\")",
        "success suffix must include the native ASCII space after punctuation");
}

static void CheckPurchaseSql(Assembly assembly, string root)
{
    var store = NeedType(assembly, "GameSvr.Services.NativeYbShopPurchaseStore");
    Equal(113, IntConstant(store, "LingFuGoodsIndex"), "LingFu goods index");
    Equal("灵符", Constant(store, "LingFuGoodsName"), "LingFu goods name");
    Equal(
        "Insert into gamelog.YBGoods_Buy_Log(" +
        "UpdateTime, PTID, UserID, CharName, GoodsIdx, GoodsName, " +
        "GoodsCount,UseCredit, Status, CurrentCredit) " +
        "Select Now(), @account, @userId, @characterName, @goodsIndex, " +
        "@goodsName, @goodsCount, @useCredit, \"Undetermined\", YBNUM " +
        "from gamedata.yb_user_data where UserID = @userId Limit 1;",
        Constant(store, "InsertSql"), "BuyLog insert SQL");
    Equal("Select LAST_INSERT_ID() as LastIdx Limit 1",
        Constant(store, "SelectLastInsertIdSql"), "BuyLog id SQL");
    Equal("Update gamelog.YBGoods_Buy_Log set Status=\"True\" " +
          "where idx=@buyLogId and Status=\"Undetermined\";",
        Constant(store, "SetTrueSql"), "BuyLog True SQL");
    Equal("Update gamelog.YBGoods_Buy_Log set Status=\"False\" " +
          "where Status=\"Undetermined\" and idx=@buyLogId;",
        Constant(store, "SetFalseSql"), "BuyLog False SQL");

    var source = Read(root, "GameSvr", "Services",
        "NativeYbShopPurchaseStore.cs");
    var begin = Slice(source, "internal static int Begin(",
        "internal static void SetStatusBestEffort(");
    Ordered(begin, "insert.ExecuteNonQuery()", "SelectLastInsertIdSql",
        "queryId.ExecuteScalar()", "return buyLogId > 0 ? buyLogId : -1");
    Require(begin, "return -1", "BuyLog failures must retain native -1 result");
}

static void CheckRequestAndAccountLogs(Assembly assembly)
{
    var requestType = NeedType(assembly, "GameSvr.Services.NativeYuanbaoRequest");
    var resultType = NeedType(assembly, "GameSvr.Services.NativeYuanbaoResult");
    var recordType = NeedType(assembly, "GameSvr.Services.NativeAccountLogRecord");
    var managerType = NeedType(assembly, "GameSvr.Services.NativeAccountLogManager");

    var resultParameter = Expression.Parameter(resultType, "result");
    var completionType = typeof(Action<>).MakeGenericType(resultType);
    var completion = Expression.Lambda(completionType, Expression.Empty(),
        resultParameter).Compile();
    var createRequest = requestType.GetMethod("CreateYbShop",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(requestType.FullName, "CreateYbShop");
    const long userId = 0x0000000200000001L;
    var request = createRequest.Invoke(null,
        new object[] { userId, "帐号甲", "角色乙", 7, 314, completion })!;

    Equal("YbShop", Property<object>(request, "Kind").ToString(), "request kind");
    Equal(userId, Property<long>(request, "UserId"), "64-bit UserID");
    Equal(1, Property<byte>(request, "Operation"), "subtract operation");
    Equal(314, Property<int>(request, "OrderId"), "BuyLog order id");
    Equal(113, Property<int>(request, "GoodsIndex"), "request goods index");
    Equal(7, Property<int>(request, "GoodsCount"), "request goods count");
    Equal("灵符", Decode(Property<byte[]>(request, "ActionBytes")),
        "shop action name");
    Equal("灵符:7", Decode(Property<byte[]>(request, "DescriptorBytes")),
        "delivery descriptor");

    var createContext = requestType.GetMethod("CreateContextIdForEnqueue",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(requestType.FullName,
            "CreateContextIdForEnqueue");
    createContext.Invoke(request, null);
    var context = Property<byte[]>(request, "ContextIdBytes");
    Equal(30, context.Length, "native context length");
    if (context.Any(value => value < (byte)'A' || value > (byte)'Z'))
        throw new InvalidOperationException("native context contains non A-Z bytes");
    var firstContext = context.ToArray();
    createContext.Invoke(request, null);
    Equal(Convert.ToHexString(firstContext),
        Convert.ToHexString(Property<byte[]>(request, "ContextIdBytes")),
        "context must be stable for every stage of one request");

    var createShop = recordType.GetMethod("CreateShop",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var createYuanbao = recordType.GetMethod("CreateYuanbao",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var stage25 = createShop.Invoke(null, new object[]
    {
        request, ByteConstant(managerType, "ShopBeginStage"), 0
    })!;
    var stage1 = createYuanbao.Invoke(null, new object[]
    {
        request, ByteConstant(managerType, "RequestSubtractStage"), 0
    })!;
    var stage2 = createYuanbao.Invoke(null, new object[]
    {
        request, ByteConstant(managerType, "BeforeSqlStage"), 0
    })!;
    var stage2Error = createYuanbao.Invoke(null, new object[]
    {
        request, ByteConstant(managerType, "BeforeSqlStage"), -1500002
    })!;
    var stage3 = createYuanbao.Invoke(null, new object[]
    {
        request, ByteConstant(managerType, "AfterSqlStage"), 0
    })!;
    var stage3Error = createYuanbao.Invoke(null, new object[]
    {
        request, ByteConstant(managerType, "AfterSqlStage"), -1500003
    })!;
    var stage26 = createShop.Invoke(null, new object[]
    {
        request, ByteConstant(managerType, "ShopEndStage"), 0
    })!;

    CheckRecord(stage25, userId, "", "314", "帐号甲", "角色乙", "角色乙",
        "ypYBShop", 113, 7, 7, 0, "ltYBShopBuyBegin", "元宝商城购买");
    CheckRecord(stage1, userId, Decode(context), "314", "角色乙", "帐号甲",
        "角色乙", "ypYBSystem", 0, 0, 7, 0, "ltRequestSubYB",
        "扣除元宝请求");
    Equal("准备修改元宝数", Decode(Property<byte[]>(stage2, "DescriptionBytes")),
        "stage 2 description");
    Equal("元宝数不足", Decode(Property<byte[]>(stage2Error, "DescriptionBytes")),
        "stage 2 account error");
    Equal("元宝改变", Decode(Property<byte[]>(stage3, "DescriptionBytes")),
        "stage 3 success description");
    Equal("元宝修改语句失败",
        Decode(Property<byte[]>(stage3Error, "DescriptionBytes")),
        "stage 3 SQL failure description");
    CheckRecord(stage26, userId, "", "314", "帐号甲", "角色乙", "角色乙",
        "ypYBShop", 0, 0, 7, 0, "ltYBShopBuyEnd",
        "元宝商城购买灵符:7(支付成功)");
}

static void CheckAsyncStateMachine(string root)
{
    var manager = Read(root, "GameSvr", "Services", "NativeYuanbaoManager.cs");
    var enqueue = Slice(manager, "internal static bool Enqueue(",
        "internal static void ProcessCompletions(");
    Ordered(enqueue,
        "NativeAccountLogManager.EnqueueShop",
        "lock (SyncRoot)",
        "request.CreateContextIdForEnqueue()",
        "Requests.Enqueue(request)",
        "NativeAccountLogManager.EnqueueYuanbao",
        "Task.Run(DrainQueue)");
    var completions = Slice(manager, "internal static void ProcessCompletions(",
        "private static void DrainQueue(");
    Ordered(completions, "NativeAccountLogManager.EnqueueShop",
        "ShopEndStage", "completion.Request.Completion(completion.Result)");

    var execute = Slice(manager, "internal static NativeYuanbaoResult Execute(",
        "private static NativeYuanbaoResult CompleteBeforeWrite(");
    Ordered(execute, "NativeAccountLogManager.BeforeSqlStage",
        "write.ExecuteNonQuery()", "NativeAccountLogManager.AfterSqlStage");
    var before = Slice(manager, "private static NativeYuanbaoResult CompleteBeforeWrite(",
        "private static NativeYuanbaoResult CompleteAfterWrite(");
    Reject(before, "AfterSqlStage", "pre-SQL account errors must not emit stage 3");

    var player = Read(root, "GameSvr", "Players", "TPlayObject.NativeYbShop.cs");
    var prepare = Slice(player,
        "private static void PrepareNativeYbShopYuanbaoCompletion(",
        "private static void CompleteNativeYbShopPurchase(");
    Ordered(prepare, "if (result.ErrorCode != 0) return;",
        "ResolveNativeYbShopPlayer(request)", "if (online == null) return;",
        "online.m_nGameGold = result.Balance;",
        "online.RefreshNativeLingFu();");
    Reject(prepare, "\n            m_nGameGold = result.Balance;",
        "balance completion still writes the initiating player");
    var completion = Slice(player,
        "private static void CompleteNativeYbShopPurchase(",
        "private static TPlayObject ResolveNativeYbShopPlayer(");
    Require(completion, "var online = ResolveNativeYbShopPlayer(request);",
        "delivery must resolve the current request owner");
    var accountError = Slice(completion, "if (result.ErrorCode != 0)",
        "if (online == null)");
    Reject(accountError, "SetStatusBestEffort",
        "account failures must leave BuyLog Undetermined");
    Ordered(completion,
        "if (online == null)",
        "SetStatusBestEffort(",
        "if (!online.GrantNativeYbShopLingFu",
        "AddConsumptionBestEffort(",
        "AddNativeYbShopCreditValue2",
        "SetStatusBestEffort(request.OrderId, true)");
    Reject(completion, "refund", "native chain has no refund path");
    Reject(completion, "m_nGameGold",
        "shop outer callback duplicated the common balance refresh");
    Reject(completion, "request.AccountBytes",
        "YBConsume must use the callback player's current PTID");
    RequireRegex(completion,
        @"AddConsumptionBestEffort\(\s*online\.m_sUserID,\s*request\.Amount\)",
        "YBConsume must use the callback player's current PTID");
    var resolve = Slice(player, "private static TPlayObject ResolveNativeYbShopPlayer(",
        "private bool GrantNativeYbShopLingFu(");
    Ordered(resolve,
        "request.UserId <= 0",
        "M2Share.UserEngine",
        "request.AccountBytes",
        "request.CharacterNameBytes",
        "foreach (var candidate in userEngine.PlayObjects)",
        "candidate.m_boGhost",
        "candidate.GetCachedNativeUserId() != request.UserId",
        "candidate.m_sUserID, account",
        "candidate.m_sCharName, characterName",
        "return candidate;");
    Reject(resolve, "GetPlayObject(",
        "shop callback reverted to name-only player resolution");
    Reject(resolve, "m_boReadyRun",
        "native callback has no extra ReadyRun delivery gate");
}

static void CheckLivePlayerCallback(Assembly assembly)
{
    PrepareRuntimeConfig();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();

    var requestType = NeedType(assembly, "GameSvr.Services.NativeYuanbaoRequest");
    var resultType = NeedType(assembly, "GameSvr.Services.NativeYuanbaoResult");
    var resultParameter = Expression.Parameter(resultType, "result");
    var completionType = typeof(Action<>).MakeGenericType(resultType);
    var completion = Expression.Lambda(completionType, Expression.Empty(),
        resultParameter).Compile();
    var createRequest = requestType.GetMethod("CreateYbShop",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(requestType.FullName, "CreateYbShop");
    const long userId = 0x0000000200000077L;
    var request = createRequest.Invoke(null,
        new object[] { userId, "Account-A", "Hero-A", 7, 431, completion })!;

    var loadUserId = typeof(TPlayObject).GetMethod("LoadNativeMailRecipientId",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TPlayObject).FullName,
            "LoadNativeMailRecipientId");
    TPlayObject NewPlayer(string account, string character, long nativeUserId,
        bool ghost, int gold, int lingFu)
    {
        var player = new TPlayObject
        {
            m_sUserID = account,
            m_sCharName = character,
            m_sMapName = "audit-map",
            m_boGhost = ghost,
            m_nGameGold = gold,
            m_nLingFu = lingFu
        };
        loadUserId.Invoke(player, new object[] { nativeUserId });
        return player;
    }

    var stale = NewPlayer("Account-A", "Hero-A", userId, true, 101, 11);
    var wrongUser = NewPlayer("Account-A", "Hero-A", userId + 1, false, 102, 12);
    var wrongAccount = NewPlayer("Account-B", "Hero-A", userId, false, 103, 13);
    var wrongCharacter = NewPlayer("Account-A", "Hero-B", userId, false, 104, 14);
    var current = NewPlayer("account-a", "hero-a", userId, false, 105, 15);

    var listField = typeof(UserEngine).GetField("m_PlayObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(UserEngine).FullName,
            "m_PlayObjectList");
    var players = listField.GetValue(M2Share.UserEngine) as IList<TPlayObject>
        ?? throw new InvalidOperationException("UserEngine player list type drifted");
    players.Add(stale);
    players.Add(wrongUser);
    players.Add(wrongAccount);
    players.Add(wrongCharacter);
    players.Add(current);

    var resolve = typeof(TPlayObject).GetMethod("ResolveNativeYbShopPlayer",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TPlayObject).FullName,
            "ResolveNativeYbShopPlayer");
    var resolved = resolve.Invoke(null, new[] { request });
    if (!ReferenceEquals(current, resolved))
        throw new InvalidOperationException(
            "shop callback did not select the current fully-matching player");

    var resultConstructor = resultType.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null, new[] { typeof(int), typeof(int) }, null)
        ?? throw new MissingMethodException(resultType.FullName, ".ctor(int,int)");
    var prepare = typeof(TPlayObject).GetMethod(
        "PrepareNativeYbShopYuanbaoCompletion",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TPlayObject).FullName,
            "PrepareNativeYbShopYuanbaoCompletion");
    var failure = resultConstructor.Invoke(new object[] { -1500002, 999 });
    prepare.Invoke(null, new[] { request, failure });
    Equal(105, current.m_nGameGold,
        "failed account callback changed current player balance");
    Equal(0, CountMessages(current, Grobal2.RM_LINGFU_CHANGED),
        "failed account callback queued capital refresh");

    var success = resultConstructor.Invoke(new object[] { 0, 777 });
    prepare.Invoke(null, new[] { request, success });
    Equal(777, current.m_nGameGold, "authoritative balance target");
    Equal(101, stale.m_nGameGold, "stale player balance changed");
    Equal(102, wrongUser.m_nGameGold, "wrong-UserID player balance changed");
    Equal(103, wrongAccount.m_nGameGold, "wrong-account player balance changed");
    Equal(104, wrongCharacter.m_nGameGold,
        "wrong-character player balance changed");
    Equal(1, CountMessages(current, Grobal2.RM_LINGFU_CHANGED),
        "authoritative balance capital refresh count");
    Equal(0, CountMessages(stale, Grobal2.RM_LINGFU_CHANGED),
        "stale player received capital refresh");

    var finish = typeof(TPlayObject).GetMethod("CompleteNativeYbShopPurchase",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TPlayObject).FullName,
            "CompleteNativeYbShopPurchase");
    finish.Invoke(null, new[] { request, success });
    Equal(22, current.m_nLingFu, "delivery target LingFu balance");
    Equal(11, stale.m_nLingFu, "stale player received LingFu");
    Equal(12, wrongUser.m_nLingFu, "wrong-UserID player received LingFu");
    Equal(13, wrongAccount.m_nLingFu, "wrong-account player received LingFu");
    Equal(14, wrongCharacter.m_nLingFu,
        "wrong-character player received LingFu");
    Equal(2, CountMessages(current, Grobal2.RM_LINGFU_CHANGED),
        "balance and delivery capital refresh count");
}

static int CountMessages(TPlayObject player, int ident) =>
    player.m_MsgList.Count(message => message.wIdent == ident);

static void CheckLingFuDelivery(string root)
{
    var player = Read(root, "GameSvr", "Players", "TPlayObject.NativeYbShop.cs");
    var grant = Slice(player, "private bool GrantNativeYbShopLingFu(",
        "private void AddNativeYbShopCreditValue2(");
    Ordered(grant,
        "m_nLingFu = unchecked(m_nLingFu + amount)",
        "IncNativeNickLinFu",
        "M2Share.AddGameDataLog",
        "NotifyPlayerActivePoint(1",
        "RefreshNativeLingFu()",
        "SendNativeYbShopSuccess(amount)");
    Require(grant, "string.Join('\\t', 51", "LingFu game log type must be 51");
    Require(grant, "222222, amount", "LingFu MakeIndex must be 222222");
    Require(grant, "\"商城购入\"", "LingFu game log reason drifted");
    RequireRegex(grant,
        @"NotifyPlayerActivePoint\(1,\s*" +
        @"NativeYbShopPurchaseStore\.LingFuGoodsName,\s*amount, 0\)",
        "PlayerActivePoint must receive (1, 0, amount, LingFu)");

    var credit = Slice(player, "private void AddNativeYbShopCreditValue2(",
        "private void SendNativeYbShopScriptDialog(");
    Ordered(credit, "MonthlyLimitedEnabled", "Value2 = unchecked",
        "Value2 < 0", "Dirty = true", "DirtyVersion++");

    var store = Read(root, "GameSvr", "Services",
        "NativeYbShopPurchaseStore.cs");
    Require(store, "gamedata.YBConsume", "native YBConsume table is missing");
    Require(store, "YBConsume=YBConsume+@amount",
        "native YBConsume accumulator is missing");
    var mall = Read(root, "GameSvr", "Mall", "MallManager.cs");
    Require(mall, "public void InvalidateHotItems()",
        "successful BuyLog insertion must invalidate the hot-sales cache");
    var app = Read(root, "GameSvr", "GameApp.cs");
    Require(app, "NativeYbShopPurchaseStore.EnsureNativeSchema(",
        "YBConsume native DDL is not initialized at startup");
}

static void CheckPasDispatch(string root)
{
    var bridge = Read(root, "GameSvr", "ScriptSystem", "PasEngine",
        "PasApiBridge.cs");
    Equal(2, Count(bridge, "case \"clientybbuylf\":"),
        "ClientYBbuyLF PAS dispatch count");
    Equal(1, Regex.Matches(bridge,
        @"[A-Za-z0-9_]+\.ClientYBbuyLF\(CurrentNpc, args\[1\]\.AsInt\(\)\);",
        RegexOptions.CultureInvariant).Count,
        "ClientYBbuyLF procedure-only explicit dispatch");
    var methods = Slice(bridge, "public bool CallNpcMethod",
        "public bool CallNpcFunc");
    Require(methods, "case \"clientybbuylf\":",
        "ClientYBbuyLF procedure dispatch is missing");
    Require(methods, "args.Count != 2",
        "ClientYBbuyLF procedure does not enforce its two native arguments");
    var functions = Slice(bridge, "public bool CallNpcFunc",
        "public bool CallStandaloneFunction");
    var functionCase = Slice(functions, "case \"clientybbuylf\":",
        "case \"buywinefromnpc\":");
    Require(functionCase, "RejectUnsupportedNativeApi(out result)",
        "ClientYBbuyLF function form must remain unsupported");
}

static void CheckRecord(object record, long userId, string context,
    string order, string character, string account, string fromWho,
    string payType, int itemIndex, int itemCount, int amount, int result,
    string action, string description)
{
    Equal(userId, Property<long>(record, "UserId"), "record UserID");
    Equal(context, Decode(Property<byte[]>(record, "ContextIdBytes")),
        "record Context_Id");
    Equal(order, Encoding.ASCII.GetString(Property<byte[]>(record, "OrderIdBytes")),
        "record Order_Id");
    Equal(character, Decode(Property<byte[]>(record, "CharacterNameBytes")),
        "record ChrName");
    Equal(account, Decode(Property<byte[]>(record, "AccountBytes")), "record PTID");
    Equal(fromWho, Decode(Property<byte[]>(record, "FromWhoBytes")),
        "record FromWho");
    Equal(payType, Property<string>(record, "PayType"), "record Pay_Type");
    Equal(itemIndex, Property<int>(record, "ItemIndex"), "record ItemIdx");
    Equal(itemCount, Property<int>(record, "ItemCount"), "record ItemNum");
    Equal(amount, Property<int>(record, "Amount"), "record amount");
    Equal(result, Property<int>(record, "Result"), "record nResult");
    Equal(action, Property<string>(record, "AccountAction"), "record AccountAct");
    Equal(description, Decode(Property<byte[]>(record, "DescriptionBytes")),
        "record ActDesc");
}

static T Property<T>(object instance, string name)
{
    var property = instance.GetType().GetProperty(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMemberException(instance.GetType().FullName, name);
    return (T)property.GetValue(instance)!;
}

static Type NeedType(Assembly assembly, string name) =>
    assembly.GetType(name, throwOnError: true)!;

static string Constant(Type type, string name) =>
    (string)(type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(type.FullName, name)).GetRawConstantValue()!;

static int IntConstant(Type type, string name) =>
    (int)(type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(type.FullName, name)).GetRawConstantValue()!;

static byte ByteConstant(Type type, string name) =>
    (byte)(type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(type.FullName, name)).GetRawConstantValue()!;

static string Decode(byte[] value) =>
    HUtil32.GbkEncoding.GetString(value ?? Array.Empty<byte>());

static string Read(string root, params string[] parts) =>
    File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

static string Slice(string source, string start, string end)
{
    var startIndex = source.IndexOf(start, StringComparison.Ordinal);
    if (startIndex < 0) throw new InvalidOperationException("missing start: " + start);
    var endIndex = source.IndexOf(end, startIndex + start.Length,
        StringComparison.Ordinal);
    if (endIndex < 0) throw new InvalidOperationException("missing end: " + end);
    return source[startIndex..endIndex];
}

static void Ordered(string source, params string[] values)
{
    var offset = 0;
    foreach (var value in values)
    {
        var next = source.IndexOf(value, offset, StringComparison.Ordinal);
        if (next < 0)
            throw new InvalidOperationException("missing/out-of-order source: " + value);
        offset = next + value.Length;
    }
}

static int Count(string source, string value)
{
    var count = 0;
    var offset = 0;
    while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    return count;
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message + ": missing " + value);
}

static void RequireRegex(string source, string pattern, string message)
{
    if (!Regex.IsMatch(source, pattern, RegexOptions.CultureInvariant))
        throw new InvalidOperationException(message + ": missing /" + pattern + "/");
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(message + ": found " + value);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
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
    throw new DirectoryNotFoundException("repository root was not found");
}
