using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.LogMsgCriticalSection ??= new object();
M2Share.ProcessMsgCriticalSection ??= new object();
M2Share.LogStringList ??= new ArrayList();
M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "金刚石" });
M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "金刚石矿" });

var player = new TPlayObject();
player.m_nLingFu = 11;
player.m_nGameGold = 22;
player.m_ItemList.Add(new TUserItem { wIndex = 1, Dura = 13 });
player.m_ItemList.Add(new TUserItem { wIndex = 1, Dura = 19 });
player.m_ItemList.Add(new TUserItem { wIndex = 2, Dura = 99 });
player.m_CreditCard.Value = 44;
player.m_CreditCard.Loaded = true;
player.m_CreditCard.GloryPointValue = 55;
player.m_CreditCard.GloryPointPeriod = 66;
player.m_sMapName = "pas-map";
player.m_nCurrX = 31;
player.m_nCurrY = 47;
player.m_sCharName = "pas-player";
player.m_NPC = new NormNpc
{
    m_sCharName = "pas-npc",
    m_sMapName = "npc-map"
};
var diamondCacheField = typeof(TPlayObject).GetField("m_nNativeDiamondCache",
    BindingFlags.Instance | BindingFlags.NonPublic);
Assert(diamondCacheField != null, "native diamond cache field is missing");
diamondCacheField.SetValue(player, 33);
var capitalBuilder = typeof(TPlayObject).GetMethod("BuildNativeCapitalInfoBody",
    BindingFlags.Instance | BindingFlags.NonPublic);
Assert(capitalBuilder != null, "native capital packet builder is missing");
var capitalBody = (byte[])capitalBuilder.Invoke(player, null)!;
Equal(24, capitalBody.Length, "native capital body length");
foreach (var pair in new[]
         {
             (Offset: 0, Value: 11), (Offset: 4, Value: 22),
             (Offset: 8, Value: 33), (Offset: 12, Value: 44),
             (Offset: 16, Value: 55), (Offset: 20, Value: 66)
         })
{
    Equal(pair.Value,
        BinaryPrimitives.ReadInt32LittleEndian(capitalBody.AsSpan(pair.Offset, 4)),
        $"native capital field at offset {pair.Offset}");
}
Equal(1202, Grobal2.SM_GETDIAMNUM_EXT, "native capital client ident");
var periodMethod = typeof(NativeCreditCardService).GetMethod(
    "CalculateGloryPointPeriod", BindingFlags.Static | BindingFlags.NonPublic);
Assert(periodMethod != null, "GloryPoint period calculator is missing");
foreach (var pair in new[]
         {
             (Now: new DateTime(2026, 7, 1), Close: new DateTime(2026, 7, 15)),
             (Now: new DateTime(2026, 7, 15), Close: new DateTime(2026, 7, 15)),
             (Now: new DateTime(2026, 7, 16), Close: new DateTime(2026, 7, 31)),
             (Now: new DateTime(2028, 2, 28), Close: new DateTime(2028, 2, 29)),
             (Now: new DateTime(2027, 2, 28), Close: new DateTime(2027, 2, 28))
         })
{
    Equal((int)pair.Close.ToOADate(),
        (int)periodMethod.Invoke(null, new object[] { pair.Now })!,
        $"GloryPoint period for {pair.Now:yyyy-MM-dd}");
}
player.m_ScriptVVars[10002] = 77;
player.m_ScriptVVars[10003] = 33;
var bridge = new PasApiBridge { CurrentPlayer = player };

Assert(bridge.GetPlayerProperty("MyLFnum", out var nativeBalance),
    "MyLFnum native property is not exposed");
Assert(nativeBalance.Type == PasValueType.Integer,
    "MyLFnum native property did not return Integer");
Equal(11, nativeBalance.AsInt(), "MyLFnum native property balance");
foreach (var property in new[] { "My_LFnum", "LingFuValue" })
{
    Assert(!bridge.GetPlayerProperty(property, out var balance),
        $"non-native alias {property} was exposed");
    Assert(balance.Type == PasValueType.Nil, $"{property} failure did not return Nil");
}

M2Share.LogStringList.Clear();
player.m_MsgList.Clear();
var addArgs = new List<PasValue> { PasValue.FromInt(23_001), PasValue.FromInt(10) };
Assert(bridge.CallPlayerMethod("AddLF", addArgs),
    "AddLF native player procedure is not exposed");
Equal(21, player.m_nLingFu, "AddLF native balance");
Equal(1, player.m_MsgList.Count, "AddLF capital refresh count");
Equal(Grobal2.RM_LINGFU_CHANGED, player.m_MsgList[0].wIdent,
    "AddLF internal capital refresh ident");
Equal(1, M2Share.LogStringList.Count, "AddLF business log count");
EqualText("9\tpas-map\t31\t47\tpas-player\t灵符\t23001\t10\tnpc给予：pas-npc-npc-map",
    (string)M2Share.LogStringList[0]!, "AddLF exact business log");

M2Share.LogStringList.Clear();
player.m_MsgList.Clear();
var addLimitedArgs = new List<PasValue>
    { PasValue.FromInt(23_002), PasValue.FromInt(6) };
Assert(bridge.CallPlayerMethod("AddLimLF", addLimitedArgs),
    "AddLimLF native player procedure is not exposed");
Equal(50, player.m_CreditCard.Value, "AddLimLF CreditCard.Value");
Assert(player.m_CreditCard.Dirty, "AddLimLF did not mark CreditCard dirty");
Equal(1, player.m_MsgList.Count, "AddLimLF capital refresh count");
Equal(Grobal2.RM_LINGFU_CHANGED, player.m_MsgList[0].wIdent,
    "AddLimLF internal capital refresh ident");
Equal(1, M2Share.LogStringList.Count, "AddLimLF business log count");
EqualText("9\tpas-map\t31\t47\tpas-player\t限时灵符\t23002\t6\tnpc给予pas-npc-npc-map",
    (string)M2Share.LogStringList[0]!, "AddLimLF exact business log");

var ordinaryBeforeInvalid = player.m_nLingFu;
var limitedBeforeInvalid = player.m_CreditCard.Value;
M2Share.LogStringList.Clear();
player.m_MsgList.Clear();
Assert(bridge.CallPlayerMethod("AddLF", new List<PasValue>
    { PasValue.FromInt(-1), PasValue.FromInt(0) }),
    "AddLF zero amount was not handled as a procedure no-op");
Assert(bridge.CallPlayerMethod("AddLimLF", new List<PasValue>
    { PasValue.FromInt(-1), PasValue.FromInt(-1) }),
    "AddLimLF negative amount was not handled as a procedure no-op");
Equal(ordinaryBeforeInvalid, player.m_nLingFu,
    "AddLF invalid amount changed the native balance");
Equal(limitedBeforeInvalid, player.m_CreditCard.Value,
    "AddLimLF invalid amount changed CreditCard.Value");
Equal(0, player.m_MsgList.Count, "invalid AddLF/AddLimLF queued a refresh");
Equal(0, M2Share.LogStringList.Count, "invalid AddLF/AddLimLF wrote a log");

M2Share.CreditCardService = NativeCreditCardService.Disabled;
M2Share.LogStringList.Clear();
player.m_MsgList.Clear();
var decArgs = new List<PasValue>
{
    PasValue.FromInt(0), PasValue.FromInt(10), PasValue.FromBool(false)
};
Assert(bridge.CallPlayerMethod("DecLF", decArgs),
    "DecLF native player procedure is not exposed");
Equal(11, player.m_nLingFu, "DecLF ordinary LingFu balance");
Equal(10, player.m_nUsedLingFu, "DecLF used-LingFu accounting");
Equal(1, player.m_MsgList.Count, "DecLF capital refresh count");
Equal(Grobal2.RM_LINGFU_CHANGED, player.m_MsgList[0].wIdent,
    "DecLF internal capital refresh ident");
Equal(1, M2Share.LogStringList.Count, "DecLF business log count");
EqualText("10\tpas-map\t31\t47\tpas-player\t灵符\t0\t10\tnpc扣除pas-npc-npc-map",
    (string)M2Share.LogStringList[0]!, "DecLF exact business log");
var buckets = ReadNativeLingFuReasonBuckets(player);
Equal(10, buckets[0], "DecLF reason bucket 0");
Assert(buckets.Skip(1).All(value => value == 0),
    "DecLF changed an unrelated reason bucket");

M2Share.LogStringList.Clear();
player.m_MsgList.Clear();
var balanceBeforeInsufficient = player.m_nLingFu;
Assert(bridge.CallPlayerMethod("DecLF", new List<PasValue>
    { PasValue.FromInt(1), PasValue.FromInt(999), PasValue.FromBool(true) }),
    "insufficient DecLF was not handled as a native procedure");
Equal(balanceBeforeInsufficient, player.m_nLingFu,
    "insufficient DecLF changed the balance");
Equal(10, player.m_nUsedLingFu,
    "insufficient DecLF changed used-LingFu accounting");
Equal(0, M2Share.LogStringList.Count,
    "insufficient DecLF wrote a business log");
var insufficientMessages = player.m_MsgList.Where(entry =>
    entry.wIdent == Grobal2.RM_SYSMESSAGE && entry.nParam1 == 0xFF &&
    entry.nParam2 == 0xFC && entry.Buff == "灵符不足").ToArray();
Equal(1, insufficientMessages.Length,
    "insufficient DecLF exact 0xFCFF message count");
Equal(10, ReadNativeLingFuReasonBuckets(player)[0],
    "insufficient DecLF changed reason buckets");

M2Share.LogStringList.Clear();
player.m_MsgList.Clear();
Assert(bridge.CallPlayerMethod("DecLF", new List<PasValue>
    { PasValue.FromInt(30_003), PasValue.FromInt(1), PasValue.FromBool(false) }),
    "special-reason DecLF was not handled");
Equal(10, player.m_nLingFu, "special-reason DecLF balance");
Equal(11, player.m_nUsedLingFu,
    "special-reason DecLF used-LingFu accounting");
Equal(0, M2Share.LogStringList.Count,
    "reason 30003 did not suppress the type-10 business log");
Equal(1, player.m_MsgList.Count(entry =>
        entry.wIdent == Grobal2.RM_LINGFU_CHANGED),
    "special-reason DecLF capital refresh count");

var invalidDecSnapshot = (player.m_nLingFu, player.m_nUsedLingFu,
    Messages: player.m_MsgList.Count, Logs: M2Share.LogStringList.Count);
Assert(bridge.CallPlayerMethod("DecLF", new List<PasValue>
    { PasValue.FromInt(-1), PasValue.FromInt(1), PasValue.FromBool(true) }),
    "negative-reason DecLF procedure was not recognized");
Equal(invalidDecSnapshot.m_nLingFu, player.m_nLingFu,
    "negative-reason DecLF changed balance");
Equal(invalidDecSnapshot.m_nUsedLingFu, player.m_nUsedLingFu,
    "negative-reason DecLF changed used accounting");
Assert(!bridge.CallPlayerMethod("DecLF", decArgs.Take(2).ToList()),
    "DecLF accepted a non-native arity");
foreach (var method in new[] { "AddLF", "AddLimLF", "DecLF" })
{
    Assert(!bridge.CallPlayerFunc(method, addArgs, out var functionResult),
        $"{method} non-native function dispatch was exposed");
    Assert(functionResult.Type == PasValueType.Nil,
        $"{method} function failure did not return Nil");
}

Equal(77, player.m_ScriptVVars[10002], "LingFu operation changed V[10,2]");
Equal(33, player.m_ScriptVVars[10003], "LingFu operation changed V[10,3]");

var root = FindRepositoryRoot();
var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine",
    "PasApiBridge.cs"));
var integrationSource = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine",
    "PasIntegration.cs"));
var mallSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Mall", "MallManager.cs"));
var commandDirectory = Path.Combine(root, "GameSvr", "Command", "Commands");
var lingFuSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeLingFu.cs"));
var playerMessageSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.Message.cs"));

RequireMatches(lingFuSource,
    "RefreshNativeLingFu\\(\\)[\\s\\S]{0,180}?SendMsg\\(this,\\s*Grobal2\\.RM_LINGFU_CHANGED",
    1, "capital refresh must enqueue native internal message 10054");
Reject(lingFuSource, "SendDefMessage(Grobal2.RM_LINGFU_CHANGED",
    "internal capital message 10054 was sent to the client");
RequireMatches(playerMessageSource,
    "case Grobal2\\.RM_LINGFU_CHANGED:[\\s\\S]{0,100}?SendNativeCapitalInfo\\(\\)",
    1, "internal capital message 10054 is not consumed by player Operate");
RequireMatches(lingFuSource,
    "MakeDefaultMsg\\(Grobal2\\.SM_GETDIAMNUM_EXT,\\s*0,\\s*0,\\s*0,\\s*0\\)[\\s\\S]{0,100}?" +
    "SendSocket\\(header, BuildNativeCapitalInfoBody\\(\\)\\)",
    1, "native capital response must be client 1202 with a raw 24-byte body");

var propertyDispatcher = ExtractControlBlock(bridgeSource,
    "public bool GetPlayerProperty(", "player property dispatcher");
var methodDispatcher = ExtractControlBlock(bridgeSource,
    "public bool CallPlayerMethod(", "player method dispatcher");
var functionDispatcher = ExtractControlBlock(bridgeSource,
    "public bool CallPlayerFunc(", "player function dispatcher");

RequireMatches(propertyDispatcher,
    "case \\\"mylfnum\\\":[\\s\\S]{0,220}?TryGetNativeLingFuBalance\\(out var " +
    "lingFuBalance\\)[\\s\\S]{0,180}?PasValue\\.FromInt\\(lingFuBalance\\)",
    1, "MyLFnum property does not use the native balance transaction");
RequireMatches(propertyDispatcher,
    "case \\\"my_lfnum\\\":\\s*case \\\"lingfuvalue\\\":[\\s\\S]{0,180}?" +
    "RejectUnsupportedNativeApi\\(out result\\)",
    1, "non-native MyLFnum aliases must fail closed");
Reject(methodDispatcher, "case \"mylfnum\":",
    "MyLFnum was published outside the property dispatcher");
Reject(functionDispatcher, "case \"mylfnum\":",
    "MyLFnum was published as a function");

RequireMatches(methodDispatcher,
    "case \\\"addlf\\\":[\\s\\S]{0,120}?args\\.Count != 2[\\s\\S]{0,160}?" +
    "AddNativeLingFu\\(args\\[0\\]\\.AsInt\\(\\), args\\[1\\]\\.AsInt\\(\\)\\)" +
    "[\\s\\S]{0,80}?return true;",
    1, "AddLF native player procedure dispatch is missing");
RequireMatches(methodDispatcher,
    "case \\\"addlimlf\\\":[\\s\\S]{0,120}?args\\.Count != 2[\\s\\S]{0,180}?" +
    "AddNativeLimitedLingFu\\([\\s\\S]{0,100}?args\\[0\\]\\.AsInt\\(\\), " +
    "args\\[1\\]\\.AsInt\\(\\)\\)[\\s\\S]{0,80}?return true;",
    1, "AddLimLF native player procedure dispatch is missing");
RequireMatches(methodDispatcher,
    "case \\\"declf\\\":[\\s\\S]{0,100}?args\\.Count != 3[\\s\\S]{0,180}?" +
    "DecNativeLingFu\\([\\s\\S]{0,100}?args\\[0\\]\\.AsInt\\(\\), " +
    "args\\[1\\]\\.AsInt\\(\\)\\)[\\s\\S]{0,80}?return true;",
    1, "DecLF native player procedure dispatch is missing");
foreach (var method in new[] { "addlf", "addlimlf", "declf" })
{
    RequireMatches(functionDispatcher,
        $"case \\\"{method}\\\":[\\s\\S]{{0,220}}?" +
        "RejectUnsupportedNativeApi\\(out result\\)",
        1, $"{method} function dispatch must remain fail closed");
}

var addNativeBlock = ExtractControlBlock(lingFuSource,
    "public bool AddNativeLingFu(", "native AddLF transaction");
var addLimitedBlock = ExtractControlBlock(lingFuSource,
    "public bool AddNativeLimitedLingFu(", "native AddLimLF transaction");
foreach (var pair in new[]
         {
             (Block: addNativeBlock,
                 LogCall: "AddNativeLingFuLog(9, \"灵符\", reason, amount, \"npc给予：\")",
                 Name: "AddLF"),
             (Block: addLimitedBlock,
                 LogCall: "AddNativeLingFuLog(9, \"限时灵符\", reason, amount, \"npc给予\")",
                 Name: "AddLimLF")
         })
{
    Assert(pair.Block.Contains("if (amount <= 0) return false",
            StringComparison.Ordinal),
        $"{pair.Name} positive-amount boundary is missing");
    var refresh = RequiredIndex(pair.Block, "RefreshNativeLingFu()",
        $"{pair.Name} native 10054 refresh is missing");
    var log = RequiredIndex(pair.Block, pair.LogCall,
        $"{pair.Name} exact type-9 log call is missing");
    Assert(refresh < log, $"{pair.Name} log occurs before the native refresh");
}
Assert(addNativeBlock.Contains(
        "m_nLingFu = unchecked(m_nLingFu + amount)", StringComparison.Ordinal),
    "AddLF does not use the native ordinary LingFu field");
Assert(addLimitedBlock.Contains("m_CreditCard.Value = value < 0 ? 0 : value",
        StringComparison.Ordinal) &&
       addLimitedBlock.Contains("m_CreditCard.Dirty = true",
           StringComparison.Ordinal),
    "AddLimLF does not update and dirty CreditCard.Value");
Reject(addLimitedBlock, "service.Enabled",
    "AddLimLF incorrectly depends on the display/debit feature switch");
var addLogBlock = ExtractControlBlock(lingFuSource,
    "private void AddNativeLingFuLog(", "native LingFu log formatter");
Assert(addLogBlock.Contains(
        "npcPrefix + m_NPC.m_sCharName + '-' + m_NPC.m_sMapName",
        StringComparison.Ordinal),
    "native LingFu NPC description columns are incomplete");
Assert(addLogBlock.Contains("string.Join('\\t', type, m_sMapName",
        StringComparison.Ordinal) &&
       addLogBlock.Contains("itemName, reason, amount", StringComparison.Ordinal),
    "native LingFu business log column order is incomplete");

Reject(integrationSource, "V[10, 2] = LingFu", "LingFu V-variable documentation substitute");
AssertLingFuDispatchesHaveNoSubstitute(bridgeSource);
Reject(mallSource, "GetPlayerVariable(player.m_ScriptVVars, 10, 2)",
    "mall LingFu balance uses V[10,2]");
Reject(mallSource, "SetPlayerVariable(player.m_ScriptVVars, 10, 2",
    "mall LingFu deduction uses V[10,2]");
// 灵符在原生商城里是**被发放的商品**，不是付款货币。发货核心 sub_6CC420 对商品名等于
// '灵符'（长串 0x6CC768，len=4，GBK c1 e9 b7 fb）的分支是一条加法：
//   006CC4F1  ba 68 c7 6c 00     mov edx, 0x6CC768        ; '灵符'
//   006CC4FB  0f 85 c9 00 00 00  jne 0x6CC5CA             ; 不是灵符 -> 物品路径
//   006CC504  01 86 d8 0b 00 00  add [esi+0xBD8], eax     ; 余额 += count
//   006CC518  68 0e 64 03 00     push 0x3640E             ; 日志 GoodsIdx = 222222
//   006CC52B  66 ba 33 00        mov dx, 0x33             ; AddLogRec 类型 51
// 整个商城链路（sub_6CB7E4 / sub_6CC420）没有任何一条余额减法，商品表也没有货币类型字段
// （sub_636D68 的 10 个字段见 MallCurrency4CompatCheck）。所以这里钉的不再是"灵符付款要
// fail closed"，而是更强的一条：MallManager 不许出现任何本地货币扣减。
Reject(mallSource, "CurrencyType", "mall still models a non-native currency-type field");
Reject(mallSource, "DeductCurrency", "mall still has a local currency deduction path");
Reject(mallSource, "m_nLingFu -=", "mall debits the LingFu balance");
Assert(mallSource.Contains("if (!TrySettleYuanbaoPayment", StringComparison.Ordinal),
    "mall purchase no longer goes through the single settlement gate");
Assert(mallSource.IndexOf("if (!TrySettleYuanbaoPayment", StringComparison.Ordinal)
       < mallSource.IndexOf("player.m_ItemList.Add", StringComparison.Ordinal),
    "mall grants items before the settlement gate");

var addLinFuSource = File.ReadAllText(Path.Combine(commandDirectory, "AddLinFuCommand.cs"));
RequireMatches(addLinFuSource,
    "GameCommand\\(\"AddLinFu\",\\s*\"增加自身灵符数量\",\\s*\"灵符数量\",\\s*4\\)",
    1, "AddLinFu original self/amount command contract is missing");
Reject(addLinFuSource, "NativeCommandFailure.Report",
    "AddLinFu still fails closed");
Assert(addLinFuSource.Contains("HUtil32.Str_ToInt(@Params[0], 1)",
        StringComparison.Ordinal),
    "AddLinFu missing native default value 1");
Assert(addLinFuSource.Contains("if (value < 1)", StringComparison.Ordinal) &&
       addLinFuSource.Contains("PlayObject.m_nLingFu", StringComparison.Ordinal) &&
       addLinFuSource.Contains("RefreshNativeLingFu", StringComparison.Ordinal),
    "AddLinFu missing native minimum/add/refresh sequence");

var creditCardSource = File.ReadAllText(
    Path.Combine(commandDirectory, "CreditCardCommand.cs"));
Reject(creditCardSource, "NativeCommandFailure.Report",
    "CreditCard still fails closed");
RequireMatches(creditCardSource,
    "GameCommand\\(\"CreditCard\",[\\s\\S]{0,160}?open\\|close\\|ClearMonLingfu\\|ClearAll[\\s\\S]{0,80}?4\\)",
    1, "CreditCard original permission/parameter contract is missing");
foreach (var value in new[]
         {
             "需要先关闭每月限时灵符的应用",
             "清除每月限时灵符数据成功",
             "需要先关闭扩展灵符的应用",
             "清除CreditCard表数据成功",
             "ISM_CS_SERVERSWITCH",
             "ISM_CREDITCARD_CLEARALL",
             "ISM_CREDITCARD_CLEARMONTHLY"
         })
{
    Assert(creditCardSource.Contains(value, StringComparison.Ordinal),
        $"CreditCard missing native surface: {value}");
}
Assert(!creditCardSource.Contains("ISM_SERVERSWITCH",
        StringComparison.Ordinal),
    "CreditCard still uses obsolete native 207 server-switch alias");

var serviceSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
    "NativeCreditCardService.cs"));
foreach (var value in new[]
         {
             "_serverSwitches.TrySetBit(1, 0x10",
             "_serverSwitches.IsBitSet(2, 0x08)",
             "update CreditCard set Value2=0;",
             "Alter Table CreditCard rename CreditCard",
             "Create Table if not Exists CreditCard",
             "Create Table if not Exists gamedata.GloryPoint",
             "select Idx, Value from GloryPoint where",
             "update GloryPoint set Value=@value where Idx=@index;",
             "Insert Into GloryPoint(PTID, CharName, datePhase, Value)",
             "CalculateGloryPointPeriod(DateTime.Now)",
             "account.GloryPointValue = gloryPointValue",
             "account.GloryPointDirtyVersion == gloryPointDirtyVersion",
             "player.m_CreditCard.ClearMonthly()",
             "player.m_CreditCard.ClearAll(currentTick)"
         })
{
    Assert(serviceSource.Contains(value, StringComparison.Ordinal),
        $"CreditCard service missing native behavior: {value}");
}
Reject(serviceSource, "DELETE FROM CreditCard",
    "CreditCard ClearAll uses delete instead of native archive");
Reject(serviceSource, "TRUNCATE",
    "CreditCard ClearAll uses truncate instead of native archive");
Reject(serviceSource, "if (!account.Loaded || !account.Dirty)",
    "GloryPoint phase check is skipped when CreditCard is clean");

var saveMethodStart = RequiredIndex(serviceSource, "public bool TrySaveDue(",
    "CreditCard timed/forced save entry is missing");
var saveMethodEnd = RequiredIndex(serviceSource, "private void EnsureSchema()",
    "CreditCard save method boundary is missing");
Assert(saveMethodEnd > saveMethodStart, "CreditCard save method boundary is invalid");
var saveMethodSource = serviceSource.Substring(saveMethodStart,
    saveMethodEnd - saveMethodStart);
var timedBlock = ExtractControlBlockContaining(saveMethodSource, "if (!force)",
    "account.LastSaveTick = currentTick", "CreditCard timed save guard");
RequireMatches(timedBlock,
    "if \\(unchecked\\(\\(uint\\)\\(currentTick - account\\.LastSaveTick\\)\\) < 10_000u\\)\\s*" +
    "return true;",
    1, "CreditCard timed save must run at an unsigned tick difference of 10000");
RequireMatches(saveMethodSource, "account\\.LastSaveTick = currentTick", 1,
    "CreditCard LastSaveTick update must remain inside the non-forced timed guard");

var phaseBlock = ExtractControlBlockContaining(saveMethodSource, "if (!force)",
    "CalculateGloryPointPeriod(DateTime.Now)",
    "forced CreditCard save phase guard");
Assert(phaseBlock.Contains("CalculateGloryPointPeriod(DateTime.Now)",
        StringComparison.Ordinal),
    "GloryPoint phase recomputation is not guarded by !force");
Assert(phaseBlock.Contains("account.GloryPointValue = 0", StringComparison.Ordinal) &&
       phaseBlock.Contains("player.RefreshNativeLingFu()", StringComparison.Ordinal),
    "GloryPoint phase rollover must clear Value and enqueue a capital refresh");
Reject(phaseBlock, "account.GloryPointDirty =",
    "GloryPoint phase rollover marks the account dirty");
RequireMatches(saveMethodSource, "CalculateGloryPointPeriod\\(DateTime\\.Now\\)", 1,
    "GloryPoint phase recomputation must remain inside the non-forced save guard");

var creditBlock = ExtractControlBlock(saveMethodSource, "if (creditCardDirty)",
    "CreditCard dirty save branch");
var gloryBlock = ExtractControlBlock(saveMethodSource, "if (gloryPointDirty)",
    "GloryPoint dirty save branch");
var creditStart = RequiredIndex(saveMethodSource, "if (creditCardDirty)",
    "CreditCard dirty save branch is missing");
var gloryStart = RequiredIndex(saveMethodSource, "if (gloryPointDirty)",
    "GloryPoint dirty save branch is missing");
Assert(gloryStart >= creditStart + creditBlock.Length,
    "GloryPoint save is nested in the CreditCard dirty branch");
Reject(creditBlock, "return ",
    "CreditCard failure returns before the independent GloryPoint branch");

var creditRefresh = RequiredIndex(creditBlock, "player.RefreshNativeLingFu()",
    "CreditCard dirty save does not enqueue the native capital refresh");
var creditExecute = RequiredIndex(creditBlock, "command.ExecuteNonQuery()",
    "CreditCard dirty SQL execution is missing");
Assert(creditRefresh < creditExecute,
    "CreditCard dirty refresh is queued after its SQL execution");
var reloadIndex = RequiredIndex(creditBlock, "ReloadCreditCardIndex(connection",
    "CreditCard insert/update does not reselect the persisted index");
Assert(reloadIndex > creditExecute,
    "CreditCard index is reselected before the insert/update succeeds");
var reloadBlock = ExtractControlBlock(serviceSource,
    "private static uint? ReloadCreditCardIndex(",
    "CreditCard index reload helper");
Assert(reloadBlock.Contains("command.CommandText = SelectSql", StringComparison.Ordinal),
    "CreditCard index reload helper does not use the native SelectSql query");
Reject(serviceSource, "LastInsertedId",
    "CreditCard insert uses connector LastInsertedId instead of native reselect");

var clearGloryDirty = RequiredIndex(gloryBlock,
    "account.GloryPointDirty = false",
    "GloryPoint dirty flag is not cleared before persistence");
var saveGlory = RequiredIndex(gloryBlock, "SaveGloryPoint(connection",
    "GloryPoint persistence call is missing");
Assert(clearGloryDirty < saveGlory,
    "GloryPoint dirty flag is cleared after SaveGloryPoint");

var mirrorSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Snaps",
    "MirrorMessage.cs"));
foreach (var value in new[]
         {
             "M2Share.ServerSwitches?.TryApplySwitchWord(",
             "switchWordExt, out _",
             ".ResetOnlineAll()",
             ".ResetOnlineMonthly()"
         })
{
    Assert(mirrorSource.Contains(value, StringComparison.Ordinal),
        $"CreditCard mirror missing native dispatch: {value}");
}

Assert(!File.Exists(Path.Combine(commandDirectory, "GameGirdCommand.cs")),
    "non-native GameGird command remains registered");
// @GameGlory 不在原生 430 行注册表里（只有 SetGloryPoint 274 / chguserGlory 226），
// 已整条移除；这里改钉「不得再被注册」，比原来校验它是不是静默 no-op 更强。
foreach (var path in Directory.GetFiles(Path.Combine(root, "GameSvr"), "*.cs",
             SearchOption.AllDirectories))
{
    Assert(!File.ReadAllText(path).Contains("[GameCommand(\"GameGlory\"",
               StringComparison.OrdinalIgnoreCase),
        "GameGlory is absent from the native registry and must not be registered");
}

Console.WriteLine(
    "PASS MyLFnum=property AddLF=procedure AddLimLF=procedure DecLF=procedure " +
    "functions=closed aliases=closed logs=type9+npc-exact 10054=internal " +
    "mall-currency2=closed AddLinFu=native CreditCard=native GameGird=absent substitutes=0");
return;

static int[] ReadNativeLingFuReasonBuckets(TPlayObject player)
{
    var method = typeof(TPlayObject).GetMethod(
        "TryGetNativeLingFuReasonBuckets",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, "native LingFu reason-bucket snapshot method is missing");
    object[] parameters = { null! };
    Assert((bool)method.Invoke(player, parameters)!,
        "native LingFu reason buckets unexpectedly had a non-positive sum");
    Assert(parameters[0] is int[],
        "native LingFu reason-bucket snapshot type is invalid");
    return (int[])parameters[0];
}

static void AssertLingFuDispatchesHaveNoSubstitute(string source)
{
    foreach (var marker in new[]
             {
                 "case \"mylfnum\":", "case \"my_lfnum\":",
                 "case \"lingfuvalue\":", "case \"addlf\":",
                 "case \"addlimlf\":", "case \"declf\":"
             })
    {
        var offset = 0;
        while ((offset = source.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            var end = source.IndexOf("RejectUnsupportedNativeApi", offset + marker.Length,
                StringComparison.Ordinal);
            if (end < 0) Fail($"missing fail-closed return after {marker}");
            var region = source.Substring(offset, end - offset);
            foreach (var forbidden in new[]
                     {
                         "GetPlayerVar", "SetPlayerVar", "m_ScriptVVars", "m_ScriptSVars",
                         "m_nGameGold", "m_nGamePoint"
                     })
            {
                if (region.Contains(forbidden, StringComparison.Ordinal))
                    Fail($"{marker} uses non-native substitute: {forbidden}");
            }
            offset = end + 1;
        }
    }
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException(
        "Repository root containing GameSvr/GameSvr.csproj was not found.");
}

static int RequiredIndex(string source, string value, string message)
{
    var index = source.IndexOf(value, StringComparison.Ordinal);
    if (index < 0) Fail(message);
    return index;
}

static string ExtractControlBlock(string source, string marker, string message)
{
    var start = RequiredIndex(source, marker, $"{message} is missing");
    return ExtractControlBlockAt(source, start, marker.Length, message);
}

static string ExtractControlBlockContaining(string source, string marker,
    string requiredValue, string message)
{
    var start = 0;
    while ((start = source.IndexOf(marker, start, StringComparison.Ordinal)) >= 0)
    {
        var block = ExtractControlBlockAt(source, start, marker.Length, message);
        if (block.Contains(requiredValue, StringComparison.Ordinal)) return block;
        start += block.Length;
    }

    Fail($"{message} containing {requiredValue} is missing");
    return string.Empty;
}

static string ExtractControlBlockAt(string source, int start, int markerLength,
    string message)
{
    var openBrace = source.IndexOf('{', start + markerLength);
    if (openBrace < 0) Fail($"{message} opening brace is missing");

    var depth = 0;
    for (var index = openBrace; index < source.Length; index++)
    {
        if (source[index] == '{') depth++;
        else if (source[index] == '}' && --depth == 0)
            return source.Substring(start, index - start + 1);
    }

    Fail($"{message} closing brace is missing");
    return string.Empty;
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

static void RequireMatches(string source, string pattern, int expected, string message)
{
    var actual = Regex.Matches(source, pattern,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Count;
    Equal(expected, actual, message);
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
        Fail($"{message} is present");
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual) Fail($"{message}: expected {expected}, actual {actual}");
}

static void EqualText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        Fail($"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) Fail(message);
}

static void Fail(string message) => throw new InvalidOperationException(message);
