using System.Reflection;
using GameSvr.Services;

var assembly = typeof(NativeMailWireCodec).Assembly;
CheckFifo(assembly);
CheckShortStrings(assembly);
CheckBalanceRules(assembly);
CheckErrorText(assembly);
CheckSqlContract(assembly);
CheckAccountLogContract(assembly);
CheckScriptYuanbaoContract(assembly);
CheckAsyncMailSourceContract();

Console.WriteLine("NativeYuanbaoAsyncCheck PASS");

static void CheckFifo(Assembly assembly)
{
    var requestType = assembly.GetType(
        "GameSvr.Services.NativeYuanbaoRequest", throwOnError: true)!;
    var fifoType = assembly.GetType(
        "GameSvr.Services.NativeYuanbaoFifo", throwOnError: true)!;
    var constructor = requestType.GetConstructors(
        BindingFlags.Instance | BindingFlags.NonPublic).Single();
    var fifo = Activator.CreateInstance(fifoType, nonPublic: true)!;
    var enqueue = fifoType.GetMethod("Enqueue",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var tryDequeue = fifoType.GetMethod("TryDequeue",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var count = fifoType.GetProperty("Count",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var orderId = requestType.GetProperty("OrderId",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    foreach (var value in new[] { 17, 4, 99 })
    {
        var request = constructor.Invoke(new object[]
        {
            1L, "account", "character", value, (byte)0, value, null
        });
        enqueue.Invoke(fifo, new[] { request });
    }

    Equal(3, (int)count.GetValue(fifo)!, "FIFO queued count");
    foreach (var expected in new[] { 17, 4, 99 })
    {
        var args = new object[] { null };
        Equal(true, (bool)tryDequeue.Invoke(fifo, args)!, "FIFO dequeue");
        Equal(expected, (int)orderId.GetValue(args[0])!, "FIFO order");
    }
    Equal(0, (int)count.GetValue(fifo)!, "FIFO drained count");
}

static void CheckShortStrings(Assembly assembly)
{
    var requestType = assembly.GetType(
        "GameSvr.Services.NativeYuanbaoRequest", throwOnError: true)!;
    var constructor = requestType.GetConstructors(
        BindingFlags.Instance | BindingFlags.NonPublic).Single();
    var accountBytes = requestType.GetProperty("AccountBytes",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var characterNameBytes = requestType.GetProperty("CharacterNameBytes",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var actionBytes = requestType.GetProperty("ActionBytes",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    var request = constructor.Invoke(new object[]
    {
        1L, new string('a', 19) + "中", new string('b', 14) + "中",
        1, (byte)0, 1, null
    });
    var account = (byte[])accountBytes.GetValue(request)!;
    var characterName = (byte[])characterNameBytes.GetValue(request)!;
    var action = (byte[])actionBytes.GetValue(request)!;
    var gbkChinese = SystemModule.HUtil32.GbkEncoding.GetBytes("中");

    Equal(20, account.Length, "PTID ShortString byte length");
    Equal(15, characterName.Length, "ChrName ShortString byte length");
    Equal(15, action.Length, "action ShortString byte length");
    Equal(gbkChinese[0], account[^1], "PTID preserves half DBCS tail");
    Equal(gbkChinese[0], characterName[^1], "ChrName preserves half DBCS tail");
    Equal(gbkChinese[0], action[^1], "action preserves half DBCS tail");
    if (account.Contains(gbkChinese[1]) || characterName.Contains(gbkChinese[1])
        || action.Contains(gbkChinese[1]))
        throw new InvalidOperationException(
            "ShortString truncation unexpectedly preserved the second DBCS byte");

    var exact = constructor.Invoke(new object[]
    {
        1L, new string('a', 18) + "中", new string('b', 13) + "中",
        1, (byte)0, 1, null
    });
    Equal(20, ((byte[])accountBytes.GetValue(exact)!).Length,
        "PTID exact byte boundary");
    Equal(15, ((byte[])characterNameBytes.GetValue(exact)!).Length,
        "ChrName exact byte boundary");
    Equal(15, ((byte[])actionBytes.GetValue(exact)!).Length,
        "action exact byte boundary");
}

static void CheckBalanceRules(Assembly assembly)
{
    var manager = assembly.GetType(
        "GameSvr.Services.NativeYuanbaoManager", throwOnError: true)!;
    var store = assembly.GetType(
        "GameSvr.Services.NativeYuanbaoStore", throwOnError: true)!;
    var calculate = store.GetMethod("CalculateBalance",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var add = (byte)manager.GetField("AddOperation",
        BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    var subtract = (byte)manager.GetField("SubtractOperation",
        BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    var insufficient = (int)manager.GetField("InsufficientBalance",
        BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    var sqlFailure = (int)manager.GetField("SqlFailure",
        BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    var negativeAmount = (int)manager.GetField("NegativeAmount",
        BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;

    Check(100, 25, add, 0, 125, "add");
    Check(100, 25, subtract, 0, 75, "subtract");
    Check(20, 25, subtract, insufficient, 20, "insufficient");
    Check(20, 1, 9, sqlFailure, 20, "invalid operation");
    Check(20, -1, add, negativeAmount, 20, "negative amount");

    void Check(int current, int amount, byte operation,
        int expectedCode, int expectedBalance, string name)
    {
        var args = new object[] { current, amount, operation, 0 };
        Equal(expectedCode, (int)calculate.Invoke(null, args)!, name + " result");
        Equal(expectedBalance, (int)args[3], name + " balance");
    }
}

static void CheckSqlContract(Assembly assembly)
{
    var store = assembly.GetType(
        "GameSvr.Services.NativeYuanbaoStore", throwOnError: true)!;
    Equal(
        "Select UserID, PTID, ChrName, YBNum, LastModifyYBNumTime " +
        "from gamedata.yb_user_data where (UserID = @userId) limit 1;",
        Constant("SelectSql"), "select SQL");
    Equal(
        "Update gamedata.yb_user_data set PTID = @account, ChrName = @characterName, " +
        "YBNum = @balance, LastModifyYBNumTime = Now() where (UserID = @userId);",
        Constant("UpdateSql"), "update SQL");
    Equal(
        "Insert into gamedata.yb_user_data(" +
        "UserID, PTID, ChrName, YBNum, LastModifyYBNumTime, GHomePayTotal) " +
        "Values(@userId, @account, @characterName, @balance, Now(), 0);",
        Constant("InsertSql"), "insert SQL");

    string Constant(string name) => (string)store.GetField(name,
        BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
}

static void CheckErrorText(Assembly assembly)
{
    var manager = assembly.GetType(
        "GameSvr.Services.NativeYuanbaoManager", throwOnError: true)!;
    var getErrorText = manager.GetMethod("GetErrorText",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    Equal("支付成功", Text(0), "success text");
    Equal("用户ID不合法", Text(-1500001), "invalid user text");
    Equal("元宝数不足", Text(-1500002), "insufficient balance text");
    Equal("系统错误", Text(-1500003), "SQL failure text");
    Equal("不能充值或扣除负数", Text(-1500004), "negative amount text");
    Equal("未知错误", Text(-1500099), "unknown error text");

    string Text(int code) => (string)getErrorText.Invoke(null, new object[] { code })!;
}

static void CheckAccountLogContract(Assembly assembly)
{
    var requestType = assembly.GetType(
        "GameSvr.Services.NativeYuanbaoRequest", throwOnError: true)!;
    var recordType = assembly.GetType(
        "GameSvr.Services.NativeAccountLogRecord", throwOnError: true)!;
    var managerType = assembly.GetType(
        "GameSvr.Services.NativeAccountLogManager", throwOnError: true)!;
    var storeType = assembly.GetType(
        "GameSvr.Services.NativeAccountLogStore", throwOnError: true)!;
    var requestConstructor = requestType.GetConstructors(
        BindingFlags.Instance | BindingFlags.NonPublic).Single();
    var createMail = recordType.GetMethod("CreateMail",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var buildTableName = storeType.GetMethod("BuildTableName",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var buildDdl = storeType.GetMethod("BuildCreateTableSql",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var buildShowColumn = storeType.GetMethod("BuildShowColumnSql",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var buildAlterEnumColumn = storeType.GetMethod("BuildAlterEnumColumnSql",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    Equal((byte)10, ManagerConstant<byte>("MailBeginStage"), "mail begin stage");
    Equal((byte)11, ManagerConstant<byte>("MailEndStage"), "mail end stage");
    Equal("ypMailGetYb", ManagerConstant<string>("MailPayType"), "mail pay type");
    Equal("ltMailAddYbBegin", ManagerConstant<string>("MailBeginAction"),
        "mail begin action");
    Equal("ltMailAddYbEnd", ManagerConstant<string>("MailEndAction"),
        "mail end action");
    Equal(20, ManagerConstant<int>("MaxInsertAttempts"), "log retry count");
    Equal(10, ManagerConstant<int>("RetryDelayMilliseconds"), "log retry delay");
    Equal(60000, ManagerConstant<int>("DdlRefreshMilliseconds"),
        "log DDL refresh");

    Equal("AccountLog202607", TableName(new DateTime(2026, 7, 17)),
        "monthly table name");
    Equal("AccountLog202701", TableName(new DateTime(2027, 1, 1)),
        "monthly table year rollover");

    const string payTypes =
        "'ypYBSystem','ypGHomePay','ypConsignment','ypGMModifyAdd'," +
        "'ypGMModifySub','ypStrengthenEquip','ypYBShop','ypStallBuyItem'," +
        "'ypMailGetYb','ypScriptBuy','ypScriptModifyAdd','ypScriptModifySub'";
    const string accountActions =
        "'ltRequestAddYB','ltRequestSubYB','ltExecSQLBefore'," +
        "'ltExecSQLFinished','ltGHomePayEnd','ltGHomePayStart'," +
        "'ltGMRequestModify','ltGMModyfyEnd','ltStallSubYbBegin'," +
        "'ltStallSubYbEnd','ltMailAddYbBegin','ltMailAddYbEnd'," +
        "'ltConsignmentbegin','ltRequestSubBuyerYB'," +
        "'ltRequestAddSellerYB','ltGiveItemsSucess','ltGiveItemsFail'," +
        "'ltConsignmentEnd','ltTimeOutBegin','ltRequestSubPlayerYB'," +
        "'ltGetBackItemsSucess','ltGetBackItemsFail','ltTimeOutEnd'," +
        "'ltStrengthenEquipBegin','ltStrengthenEquipEnd'," +
        "'ltYBShopBuyBegin','ltYBShopBuyEnd','ltYBScriptBuyBegin'," +
        "'ltYBScriptBuyEnd','ltScriptRequestModify','ltScriptModyfyEnd'";
    Equal(payTypes, StoreConstant("PayTypeEnumSql"), "Pay_Type RTTI list");
    Equal(accountActions, StoreConstant("AccountActionEnumSql"),
        "AccountAct RTTI list");

    var expectedDdl =
        "Create Table if not Exists gamelog.AccountLog202607(idx int not null AUTO_INCREMENT PRIMARY KEY, UpdateTime DateTime not null, Context_Id Char(32) not null,Order_Id Char(32) not null,UserID bigint(20) NOT NULL default 0,PTID varchar(20) character set latin1 collate latin1_bin NOT NULL default '',ChrName char(15) character set latin1 collate latin1_bin NOT NULL default '',FromWho char(15) character set latin1 collate latin1_bin NOT NULL default '',Pay_Type Enum(" +
        payTypes +
        "),ItemIdx int,ItemNum int,amount int, nResult int,AccountAct Enum(" +
        accountActions +
        "),ActDesc varchar(255) character set latin1 collate latin1_bin NOT NULL default '',index Query1_index(UserID, Pay_Type, nResult, UpdateTime), index Query2_index(Context_Id, Order_Id, Pay_Type, nResult, AccountAct), index Time_Index(UpdateTime)) Max_ROWS=20000000000;";
    Equal(expectedDdl,
        (string)buildDdl.Invoke(null, new object[] { "AccountLog202607" })!,
        "native AccountLog DDL");
    Equal(
        "Show COLUMNS From gamelog.AccountLog202607 like 'Pay_Type';",
        (string)buildShowColumn.Invoke(null,
            new object[] { "AccountLog202607", "Pay_Type" })!,
        "native AccountLog SHOW COLUMNS");
    Equal(
        "Alter Table gamelog.AccountLog202607 MODIFY COLUMN AccountAct Enum(" +
        accountActions + ");",
        (string)buildAlterEnumColumn.Invoke(null,
            new object[] { "AccountLog202607", "AccountAct", accountActions })!,
        "native AccountLog ALTER enum");
    Equal(
        "insert into gamelog.%s(UpdateTime, Context_Id, Order_Id, UserID, ChrName, PTID, FromWho,Pay_Type, ItemIdx, ItemNum, amount, nResult, AccountAct, ActDesc) values(Now(),\"%s\",\"%s\", %d, \"%s\", \"%s\", \"%s\", \"%s\", %d, %d, %d, %d, \"%s\", \"%s\");",
        StoreConstant("NativeInsertSqlFormat"), "native AccountLog INSERT");

    const long userId = 0x0000000200000001L;
    var request = requestConstructor.Invoke(new object[]
    {
        userId, "帐号甲", "角色乙", 37, (byte)0, 91, null
    });
    var begin = createMail.Invoke(null, new object[] { request, (byte)10, 0 })!;
    var success = createMail.Invoke(null, new object[] { request, (byte)11, 0 })!;
    var failure = createMail.Invoke(null,
        new object[] { request, (byte)11, -1500003 })!;

    Equal(userId, Property<long>(begin, "UserId"), "log UInt64 UserID");
    Equal(0, Property<byte[]>(begin, "ContextIdBytes").Length, "empty Context_Id");
    Equal(0, Property<byte[]>(begin, "OrderIdBytes").Length, "empty Order_Id");
    Equal("帐号甲", Decode(Property<byte[]>(begin, "AccountBytes")), "log PTID");
    Equal("角色乙", Decode(Property<byte[]>(begin, "CharacterNameBytes")),
        "log ChrName");
    Equal("角色乙", Decode(Property<byte[]>(begin, "FromWhoBytes")),
        "log FromWho");
    Equal("ypMailGetYb", Property<string>(begin, "PayType"), "record Pay_Type");
    Equal(0, Property<int>(begin, "ItemIndex"), "record ItemIdx");
    Equal(0, Property<int>(begin, "ItemCount"), "record ItemNum");
    Equal(37, Property<int>(begin, "Amount"), "record amount");
    Equal(0, Property<int>(begin, "Result"), "begin nResult");
    Equal("ltMailAddYbBegin", Property<string>(begin, "AccountAction"),
        "begin AccountAct");
    Equal("邮件(角色乙)领取元宝",
        Decode(Property<byte[]>(begin, "DescriptionBytes")), "begin ActDesc");
    Equal("领取元宝(角色乙)成功",
        Decode(Property<byte[]>(success, "DescriptionBytes")), "success ActDesc");
    Equal(-1500003, Property<int>(failure, "Result"), "failure nResult");
    Equal("ltMailAddYbEnd", Property<string>(failure, "AccountAction"),
        "end AccountAct");
    Equal("领取元宝(角色乙)失败",
        Decode(Property<byte[]>(failure, "DescriptionBytes")), "failure ActDesc");

    T ManagerConstant<T>(string name) => (T)managerType.GetField(name,
        BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    string StoreConstant(string name) => (string)storeType.GetField(name,
        BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    string TableName(DateTime value) =>
        (string)buildTableName.Invoke(null, new object[] { value })!;
    static T Property<T>(object value, string name) => (T)value.GetType()
        .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(value)!;
    static string Decode(byte[] value) => SystemModule.HUtil32.GbkEncoding.GetString(value);
}

static void CheckScriptYuanbaoContract(Assembly assembly)
{
    var requestType = assembly.GetType(
        "GameSvr.Services.NativeYuanbaoRequest", throwOnError: true)!;
    var recordType = assembly.GetType(
        "GameSvr.Services.NativeAccountLogRecord", throwOnError: true)!;
    var managerType = assembly.GetType(
        "GameSvr.Services.NativeAccountLogManager", throwOnError: true)!;
    var yuanbaoManagerType = assembly.GetType(
        "GameSvr.Services.NativeYuanbaoManager", throwOnError: true)!;
    var createRequest = requestType.GetMethod("CreateScript",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var createRecord = recordType.GetMethod("CreateScript",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var createContext = requestType.GetMethod("CreateContextIdForEnqueue",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var setCallbackCharacter = requestType.GetMethod(
        "SetScriptCallbackCharacterName",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    var addOperation = Constant<byte>(yuanbaoManagerType, "AddOperation");
    var subtractOperation = Constant<byte>(yuanbaoManagerType, "SubtractOperation");
    var beginStage = Constant<byte>(managerType, "ScriptBeginStage");
    var endStage = Constant<byte>(managerType, "ScriptEndStage");
    Equal((byte)29, beginStage, "script begin stage");
    Equal((byte)30, endStage, "script end stage");
    Equal("ypScriptModifyAdd", Constant<string>(managerType, "ScriptAddPayType"),
        "script add pay type");
    Equal("ypScriptModifySub",
        Constant<string>(managerType, "ScriptSubtractPayType"),
        "script subtract pay type");
    Equal("ltScriptRequestModify",
        Constant<string>(managerType, "ScriptBeginAction"),
        "script begin action");
    Equal("ltScriptModyfyEnd", Constant<string>(managerType, "ScriptEndAction"),
        "script end action");

    const long userId = 0x0000000200000001L;
    var addRequest = NewRequest(addOperation, -7);
    var subRequest = NewRequest(subtractOperation, 9);
    var addBegin = NewRecord(addRequest, beginStage, 0);
    var subBegin = NewRecord(subRequest, beginStage, 0);
    AssertBegin(addBegin, "ypScriptModifyAdd", -7,
        "脚本(角色乙)请求充值");
    AssertBegin(subBegin, "ypScriptModifySub", 9,
        "脚本(角色乙)请求扣费");

    createContext.Invoke(addRequest, null);
    var addSuccess = NewRecord(addRequest, endStage, 0);
    var addFailure = NewRecord(addRequest, endStage, -1500003);
    var subSuccess = NewRecord(subRequest, endStage, 0);
    var subFailure = NewRecord(subRequest, endStage, -1500002);
    AssertEnd(addSuccess, "ypScriptModifyAdd", -7, 0,
        "脚本(角色乙)充值成功");
    AssertEnd(addFailure, "ypScriptModifyAdd", -7, -1500003,
        "脚本(角色乙)充值失败");
    AssertEnd(subSuccess, "ypScriptModifySub", 9, 0,
        "脚本(角色乙)扣费成功");
    AssertEnd(subFailure, "ypScriptModifySub", 9, -1500002,
        "脚本(角色乙)扣费失败");
    var callbackNameRequest = NewRequest(addOperation, 5);
    setCallbackCharacter.Invoke(callbackNameRequest, new object[] { "回调丙" });
    var callbackNameEnd = NewRecord(callbackNameRequest, endStage, 0);
    Equal("角色乙", Decode(Property<byte[]>(callbackNameEnd,
        "CharacterNameBytes")), "script callback snapshot ChrName");
    Equal("帐号甲", Decode(Property<byte[]>(callbackNameEnd, "AccountBytes")),
        "script callback snapshot PTID");
    Equal("回调丙", Decode(Property<byte[]>(callbackNameEnd, "FromWhoBytes")),
        "script callback live FromWho");
    Equal("脚本(回调丙)充值成功",
        Decode(Property<byte[]>(callbackNameEnd, "DescriptionBytes")),
        "script callback live ActDesc");

    var managerSource = ReadSource("GameSvr", "Services",
        "NativeYuanbaoManager.cs");
    var playerSource = ReadSource("GameSvr", "Players",
        "TPlayObject.NativeScriptYuanbao.cs");
    Forbid(managerSource, "ScriptRequests",
        "script yuanbao created a second request queue");
    Forbid(playerSource, "Task.Run",
        "script player callback created a second async worker");
    Require(playerSource, "NativeYuanbaoManager.Enqueue(request);",
        "script yuanbao does not use the shared native FIFO");

    var enqueueSource = Slice(managerSource,
        "internal static bool Enqueue(NativeYuanbaoRequest request)",
        "internal static void ProcessCompletions()");
    var beginLog = enqueueSource.IndexOf(
        "NativeAccountLogManager.EnqueueScript(request,", StringComparison.Ordinal);
    var sharedQueue = enqueueSource.IndexOf("Requests.Enqueue(request);",
        StringComparison.Ordinal);
    if (!(beginLog >= 0 && beginLog < sharedQueue))
        throw new InvalidOperationException(
            "script stage 29 must precede the shared yuanbao FIFO enqueue");

    var completionSource = Slice(managerSource,
        "internal static void ProcessCompletions()",
        "private static void DrainQueue()");
    var applyBalance = completionSource.IndexOf(
        "BeforeOuterCompletionLog?.Invoke", StringComparison.Ordinal);
    var endLog = completionSource.IndexOf(
        "NativeAccountLogManager.EnqueueScript(completion.Request,",
        StringComparison.Ordinal);
    var callback = completionSource.IndexOf(
        "completion.Request.Completion(completion.Result);",
        StringComparison.Ordinal);
    if (!(applyBalance >= 0 && applyBalance < endLog && endLog < callback))
        throw new InvalidOperationException(
            "script completion order must be balance/refresh, stage 30, messages");

    var prepareSource = Slice(playerSource,
        "private void PrepareNativeScriptYuanbaoCompletion(",
        "private void CompleteNativeScriptYuanbao(");
    Require(prepareSource, "request.SetScriptCallbackCharacterName(m_sCharName);",
        "script stage 30 must capture the callback-time FromWho name");
    Require(prepareSource, "if (result.ErrorCode != 0) return;",
        "failed script requests must not update local balance");
    var balanceWrite = prepareSource.IndexOf(
        "m_nGameGold = result.Balance;", StringComparison.Ordinal);
    var capitalRefresh = prepareSource.IndexOf("RefreshNativeLingFu();",
        StringComparison.Ordinal);
    if (!(balanceWrite >= 0 && balanceWrite < capitalRefresh))
        throw new InvalidOperationException(
            "script success must write authoritative balance before capital refresh");

    object NewRequest(byte operation, int amount) => createRequest.Invoke(null,
        new object[] { userId, "帐号甲", "角色乙", amount, operation, null, null })!;
    object NewRecord(object request, byte stage, int result) =>
        createRecord.Invoke(null, new[] { request, (object)stage, result })!;

    void AssertBegin(object record, string payType, int amount, string description)
    {
        Equal(-1L, Property<long>(record, "UserId"), "script begin UserID");
        Equal(0, Property<byte[]>(record, "ContextIdBytes").Length,
            "script begin Context_Id");
        Equal(0, Property<byte[]>(record, "OrderIdBytes").Length,
            "script begin Order_Id");
        Equal("角色乙", Decode(Property<byte[]>(record, "CharacterNameBytes")),
            "script begin ChrName");
        Equal(0, Property<byte[]>(record, "AccountBytes").Length,
            "script begin PTID");
        Equal("角色乙", Decode(Property<byte[]>(record, "FromWhoBytes")),
            "script begin FromWho");
        AssertCommon(record, payType, amount, 0, "ltScriptRequestModify",
            description);
    }

    void AssertEnd(object record, string payType, int amount, int result,
        string description)
    {
        Equal(userId, Property<long>(record, "UserId"), "script end UserID");
        Equal(0, Property<byte[]>(record, "ContextIdBytes").Length,
            "script end Context_Id");
        Equal(0, Property<byte[]>(record, "OrderIdBytes").Length,
            "script end Order_Id");
        Equal("角色乙", Decode(Property<byte[]>(record, "CharacterNameBytes")),
            "script end ChrName");
        Equal("帐号甲", Decode(Property<byte[]>(record, "AccountBytes")),
            "script end PTID");
        Equal("角色乙", Decode(Property<byte[]>(record, "FromWhoBytes")),
            "script end FromWho");
        AssertCommon(record, payType, amount, result, "ltScriptModyfyEnd",
            description);
    }

    void AssertCommon(object record, string payType, int amount, int result,
        string action, string description)
    {
        Equal(payType, Property<string>(record, "PayType"), "script Pay_Type");
        Equal(0, Property<int>(record, "ItemIndex"), "script ItemIdx");
        Equal(0, Property<int>(record, "ItemCount"), "script ItemNum");
        Equal(amount, Property<int>(record, "Amount"), "script amount");
        Equal(result, Property<int>(record, "Result"), "script nResult");
        Equal(action, Property<string>(record, "AccountAction"),
            "script AccountAct");
        Equal(description, Decode(Property<byte[]>(record, "DescriptionBytes")),
            "script ActDesc");
    }

    static T Constant<T>(Type type, string name) => (T)type.GetField(name,
        BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    static T Property<T>(object value, string name) => (T)value.GetType()
        .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(value)!;
    static string Decode(byte[] value) =>
        SystemModule.HUtil32.GbkEncoding.GetString(value);
}

static void CheckAsyncMailSourceContract()
{
    var manager = ReadSource("GameSvr", "Services", "NativeYuanbaoManager.cs");
    var accountLog = ReadSource("GameSvr", "Services", "NativeAccountLogManager.cs");
    var player = ReadSource("GameSvr", "Players", "TPlayObject.Mail.cs");
    var userEngine = ReadSource("GameSvr", "UsrSystem", "UsrEngn.cs");

    Require(manager, "private static bool _workerRunning;",
        "manager must enforce one FIFO consumer");
    Require(manager, "_ = Task.Run(DrainQueue);",
        "yuanbao SQL must run outside the player loop");
    Require(manager, "Completions.Enqueue(new Completion(request, result));",
        "worker must marshal completion instead of mutating players");
    Forbid(manager, "LoginCenterPayAuth",
        "mail yuanbao must not use LoginCenterPayAuth");
    Forbid(manager, "HttpClient",
        "mail yuanbao must not invent an HTTP dependency");
    Require(manager, "AccountBytes = EncodeShortString(account, 20);",
        "PTID must use native ShortString[20] byte truncation");
    Require(manager, "CharacterNameBytes = EncodeShortString(characterName, 15);",
        "character name must use native ShortString[15] byte truncation");
    Require(manager, "ActionBytes = EncodeShortString(characterName, 15);",
        "mail action must use the character name as native ShortString[15]");
    Require(manager, "bytes[..maxBytes]",
        "ShortString truncation must operate on raw CP936 bytes");
    Require(manager, "AddRawParameter(write, \"@account\", request.AccountBytes);",
        "SQL must bind the already-truncated PTID bytes");
    Require(manager,
        "AddRawParameter(write, \"@characterName\", request.CharacterNameBytes);",
        "SQL must bind the already-truncated character-name bytes");

    var enqueueMethod = Slice(manager,
        "internal static bool Enqueue(NativeYuanbaoRequest request)",
        "internal static void ProcessCompletions()");
    var beginLog = enqueueMethod.IndexOf(
        "NativeAccountLogManager.EnqueueMail(request,", StringComparison.Ordinal);
    var yuanbaoQueue = enqueueMethod.IndexOf("Requests.Enqueue(request);",
        StringComparison.Ordinal);
    if (!(beginLog >= 0 && beginLog < yuanbaoQueue))
        throw new InvalidOperationException(
            "mail AccountLog stage 10 must be queued before the yuanbao request");

    var completionMethod = Slice(manager,
        "internal static void ProcessCompletions()",
        "private static void DrainQueue()");
    var endLog = completionMethod.IndexOf(
        "NativeAccountLogManager.EnqueueMail(completion.Request,",
        StringComparison.Ordinal);
    var mailCallback = completionMethod.IndexOf(
        "completion.Request.Completion(completion.Result);",
        StringComparison.Ordinal);
    if (!(endLog >= 0 && endLog < mailCallback))
        throw new InvalidOperationException(
            "mail AccountLog stage 11 must be queued before callback effects");

    Require(accountLog,
        "for (var attempt = 1; attempt <= MaxInsertAttempts; attempt++)",
        "AccountLog must retry the current FIFO head in place");
    Require(accountLog, "await Task.Delay(RetryDelayMilliseconds)",
        "AccountLog retries must preserve the native 10ms delay");
    Forbid(accountLog, "SemaphoreSlim",
        "AccountLog must not accumulate semaphore wake counts");
    Forbid(accountLog, "Signal.Release",
        "AccountLog enqueue must not accumulate wake counts");
    Require(accountLog, "NativeAccountLogStore.EnsureCurrentTable(DateTime.Now);",
        "AccountLog worker must periodically maintain the monthly table");
    Require(accountLog,
        "EnsureEnumColumn(connection, tableName, \"Pay_Type\", PayTypeEnumSql);",
        "AccountLog DDL maintenance must inspect Pay_Type");
    Require(accountLog,
        "EnsureEnumColumn(connection, tableName, \"AccountAct\",",
        "AccountLog DDL maintenance must inspect AccountAct");
    Require(accountLog,
        "var tableName = _currentTableName;",
        "AccountLog INSERT must use the maintained monthly table cache");
    Require(accountLog,
        "if (string.IsNullOrEmpty(tableName))",
        "AccountLog INSERT may initialize the table only when the cache is empty");
    Require(accountLog, "DescriptionBytes = BuildDescription(request.ActionBytes",
        "mail AccountLog must preserve the native action ShortString bytes");

    var gameApp = ReadSource("GameSvr", "GameApp.cs");
    var initialize = Slice(gameApp,
        "public bool Initialize()",
        "public void StartEngine()");
    Require(initialize, "NativeAccountLogManager.Start();",
        "AccountLog worker must start during GameApp initialization");

    var pump = userEngine.IndexOf("NativeYuanbaoManager.ProcessCompletions();",
        StringComparison.Ordinal);
    var humans = userEngine.IndexOf("ProcessHumans();", pump, StringComparison.Ordinal);
    if (pump < 0 || humans < pump)
        throw new InvalidOperationException(
            "yuanbao completions must run on the UserEngine loop before player processing");

    var callback = Slice(player,
        "private void CompleteNativeMailYuanbaoClaim(",
        "private TPlayObject ResolveNativeMailClaimPlayer()");
    var refresh = callback.IndexOf("online.m_nGameGold = result.Balance;",
        StringComparison.Ordinal);
    var orderSuccess = callback.IndexOf(
        "NativeMailStore.SetMoneyOrderStatusBestEffort(orderId, 1);",
        StringComparison.Ordinal);
    var deliver = callback.IndexOf(
        "claimResult = online.DeliverNativeMailAttachments(entry);",
        StringComparison.Ordinal);
    var response = callback.LastIndexOf("online?.SendDefMessage(",
        StringComparison.Ordinal);
    if (!(refresh >= 0 && refresh < orderSuccess && orderSuccess < deliver
          && deliver < response))
    {
        throw new InvalidOperationException(
            "successful yuanbao callback order is not refresh/order/delivery/response");
    }

    var failure = callback.IndexOf("if (result.ErrorCode != 0)",
        StringComparison.Ordinal);
    var failureHint = callback.IndexOf("online?.SysMsg(", failure,
        StringComparison.Ordinal);
    var orderFailure = callback.IndexOf(
        "NativeMailStore.SetMoneyOrderStatusBestEffort(orderId, 2);", failure,
        StringComparison.Ordinal);
    var failureResponse = callback.IndexOf(
        "Grobal2.SM_FETCH_ATTACH, -4", orderFailure,
        StringComparison.Ordinal);
    if (!(failure >= 0 && failure < failureHint && failureHint < orderFailure
          && orderFailure < failureResponse))
        throw new InvalidOperationException(
            "failed yuanbao callback must hint before order 2 and response -4");
    Require(callback, "$\"邮件领取{entry.Record.MoneyCount}个元宝！\"",
        "successful yuanbao callback must show the native claim hint");
    Require(callback, "$\"增加元宝失败 玩家:{m_sCharName} 错误信息：\"",
        "failed yuanbao callback must show the native error hint");

    Require(callback,
        "else if (online != null)\n            {\n                claimResult = online.DeliverNativeMailAttachments(entry);",
        "ordinary mail must not deliver or mark attachments after logout");
    Require(callback,
        "if (entry.Record.MailType == 4)",
        "mail type 4 must keep its direct attachstatus completion branch");
    Require(callback,
        "Grobal2.SM_FETCH_ATTACH, claimResult, 0, 0, 0, string.Empty",
        "all asynchronous yuanbao completions must use native SM 4462");
    Forbid(callback, "SM_FETCH_ATTACH_OFFTM",
        "asynchronous 4468 requests must still complete through native SM 4462");

    var claim = Slice(player,
        "private int FetchNativeMailAttachments(",
        "private void CompleteNativeMailYuanbaoClaim(");
    var createOrder = claim.IndexOf("CreateMoneyOrderBestEffort",
        StringComparison.Ordinal);
    var enqueue = claim.IndexOf("NativeYuanbaoManager.Enqueue(request)",
        StringComparison.Ordinal);
    if (!(createOrder >= 0 && createOrder < enqueue))
        throw new InvalidOperationException(
            "money order must be created before the yuanbao request is queued");
    Require(claim, "if (NativeYuanbaoManager.Enqueue(request)) return 0;",
        "async claim must remain silent until completion");
    Forbid(claim, "m_nGameGold += record.MoneyCount",
        "mail claim must not add yuanbao locally");
}

static string ReadSource(params string[] relativeParts) =>
    File.ReadAllText(FindRepositoryFile(relativeParts))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    if (start < 0 || end < 0)
        throw new InvalidOperationException(
            $"could not isolate source between {startMarker} and {endMarker}");
    return source[start..end];
}

static void Require(string source, string expected, string message)
{
    if (!source.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Forbid(string source, string forbidden, string message)
{
    if (source.Contains(forbidden, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static string FindRepositoryFile(params string[] relativeParts)
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(start);
             directory != null;
             directory = directory.Parent)
        {
            var path = relativeParts.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(path)) return path;
        }
    }
    throw new InvalidOperationException("could not locate the repository source");
}

static void Equal<T>(T expected, T actual, string name) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{name}: expected {expected}, got {actual}");
}
