using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;

if (args.Length < 4)
{
    throw new ArgumentException("Usage: ProtocolRegressionCheck <GameSvr build> <GameGate build> <DBSvr build> <YBShopScript.pas>");
}

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936);
var gameSvrDirectory = Path.GetFullPath(args[0]);
var gameGateDirectory = Path.GetFullPath(args[1]);
var dbSvrDirectory = Path.GetFullPath(args[2]);
var mallScriptPath = Path.GetFullPath(args[3]);

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var directory in new[] { gameSvrDirectory, gameGateDirectory, dbSvrDirectory })
    {
        var dependencyPath = Path.Combine(directory, $"{name.Name}.dll");
        if (File.Exists(dependencyPath))
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(dependencyPath);
        }
    }
    return null;
};

var gameSvr = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameSvrDirectory, "GameSvr.dll"));
TestPascalCoreSemantics(gameSvr);
TestPascalHostIntegration(gameSvr, gbk);
TestMakeSlaveSignature(gameSvr);
TestUnsupportedPasApis(gameSvr);
TestNoInventedMarketPersistence(gameSvr, gameSvrDirectory);
var systemModule = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameSvrDirectory, "SystemModule.dll"));
TestHomeAndNormalLogin(gameSvr, systemModule);
TestNativeWeaponUpgradePersistence(gameSvr, systemModule, gameSvrDirectory);
TestHeroWireRecords(gameSvr, systemModule, gbk);
var globalType = systemModule.GetType("SystemModule.Grobal2", throwOnError: true)!;
TestRequestServerFraming(systemModule.GetType("SystemModule.RequestServerFrameParser", throwOnError: true)!);

var hUtilType = systemModule.GetType("SystemModule.HUtil32", throwOnError: true)!;
var nextSequence = (Func<int>)hUtilType.GetMethod("Sequence", BindingFlags.Static | BindingFlags.Public)!
    .CreateDelegate(typeof(Func<int>));
var sequenceIds = new int[100_000];
Parallel.For(0, sequenceIds.Length, i => sequenceIds[i] = nextSequence());
Array.Sort(sequenceIds);
Assert(sequenceIds[0] > 0, "object ID sequence produced a non-positive value");
for (var i = 1; i < sequenceIds.Length; i++)
    Assert(sequenceIds[i] != sequenceIds[i - 1], $"duplicate object ID sequence value: {sequenceIds[i]}");

var enterCritical = (Action<object>)hUtilType.GetMethod("EnterCriticalSection", BindingFlags.Static | BindingFlags.Public)!
    .CreateDelegate(typeof(Action<object>));
var leaveCritical = (Action<object>)hUtilType.GetMethod("LeaveCriticalSection", BindingFlags.Static | BindingFlags.Public)!
    .CreateDelegate(typeof(Action<object>));
var criticalSection = new object();
var protectedCounter = 0;
Parallel.For(0, 100_000, _ =>
{
    enterCritical(criticalSection);
    try { protectedCounter++; }
    finally { leaveCritical(criticalSection); }
});
Assert(protectedCounter == 100_000, $"single critical-section helpers did not serialize access: {protectedCounter}");

AssertConstant(globalType, "SM_SHOPITEMS", 812);
AssertConstant(globalType, "SM_FIRSTSHOP", 815);
AssertConstant(globalType, "SM_DOSHOP_FAIL", 816);
AssertConstant(globalType, "SM_TOHEROBAG_OK", 817);
AssertConstant(globalType, "SM_TOHEROBAG_FAIL", 818);
AssertConstant(globalType, "SM_TOHUMBAG_OK", 819);
AssertConstant(globalType, "SM_TOHUMBAG_FAIL", 820);
AssertConstant(globalType, "CM_HERO_POWERUP", 1108);
AssertConstant(globalType, "CM_HERO_SKILL_HOTKEY", 1109);
AssertConstant(globalType, "CM_MERCHANT_QUERY", 1110);
AssertConstant(globalType, "CM_PILEUPITEM", 1115);
AssertConstant(globalType, "CM_SPLITITEM", 1116);
AssertConstant(globalType, "CM_SECHERO_PRACTICE", 1216);
AssertConstant(globalType, "SM_SECHERO_PRACTICE", 1216);
AssertConstant(globalType, "CM_QUERY_FOCUS_ITEM", 1271);
foreach (var (name, value) in new (string Name, int Value)[]
         {
             ("SM_HERO_QUITMAGIC", 896), ("SM_HERO_LOGMAGIC", 897),
             ("SM_HERO_NAME", 898), ("SM_HERO_LOGON", 899),
             ("SM_HERO_ABILITY", 900), ("SM_HERO_SUBABILITY", 901),
             ("SM_HERO_BAGITEMS", 902), ("SM_HERO_SENDUSEITEMS", 903),
             ("SM_HERO_SENDMYMAGIC", 904), ("SM_HERO_ADDITEM", 905),
             ("SM_HERO_DELITEM", 906), ("SM_HERO_TAKEON_OK", 907),
             ("SM_HERO_TAKEON_FAIL", 908), ("SM_HERO_TAKEOFF_OK", 909),
             ("SM_HERO_TAKEOFF_FAIL", 910), ("SM_HERO_EAT_OK", 911),
             ("SM_HERO_EAT_FAIL", 912), ("SM_HERO_ADDMAGIC", 913),
             ("SM_HERO_LEVELUP", 914), ("SM_HERO_WINEXP", 915),
             ("SM_HERO_MAGIC_LVEXP", 916), ("SM_HERO_DELITEMS", 917),
             ("SM_HERO_LOGOUT", 918), ("SM_HERO_DURACHANGE", 919),
             ("SM_HERO_DROPITEM_SUCCESS", 920), ("SM_HERO_DROPITEM_FAIL", 921),
             ("SM_HERO_BAGITEMDURACHG", 922), ("SM_HERO_UNIONSTATUS", 923),
             ("SM_HERO_SPLITSHADOW", 924), ("SM_HERO_HELPOP_OK", 925)
         })
{
    AssertConstant(globalType, name, value);
}
AssertConstant(globalType, "SM_BAGITEMDURACHG", 641);
AssertConstant(globalType, "SM_STORAGE_ADDITEM", 717);
AssertConstant(globalType, "SM_STORAGEITEMDURACHG", 790);
AssertConstant(globalType, "SM_ITEM_PILEUP_RESULT", 3322);
AssertConstant(globalType, "SM_FETCH_MAIL_LIST", 4460);
AssertConstant(globalType, "SM_FETCH_MAIL_INFO", 4461);
AssertConstant(globalType, "SM_FETCH_ATTACH", 4462);
AssertConstant(globalType, "SM_DEL_MAIL", 4463);
AssertConstant(globalType, "SM_MAIL_INFO", 4464);
AssertConstant(globalType, "CM_SYSTEM_NEWMAIL", 4464);
AssertConstant(globalType, "SM_FETCH_ATTACH_OFFTM", 4468);
AssertConstant(globalType, "SM_CLEAR_ALLMAIL", 4495);
foreach (var inventedHeroCommand in new[]
         {
             "CM_HERO_CHGATTACKMODE", "CM_HERO_SPELL", "CM_HERO_AUTOHIT",
             "CM_HERO_SPEEDHACK", "CM_HERO_RESERVATION", "CM_HERO_OPENDOOR",
             "CM_HERO_BUTCH", "CM_HERO_HEAVYHIT", "CM_HERO_RUN",
             "CM_HERO_WALK", "CM_HERO_TURN"
         })
{
    Assert(globalType.GetField(inventedHeroCommand, BindingFlags.Static | BindingFlags.Public) == null,
        $"invented hero command returned: {inventedHeroCommand}");
}
foreach (var inventedHeroResponse in new[]
         {
             "SM_HERO_UPDATEITEM", "SM_HERO_HEALTHSPELLCHANGED", "SM_HERO_CHANGEMAP",
             "SM_HERO_CHANGEFACE", "SM_HERO_FEATURECHANGED", "SM_HERO_CHARSTATUSCHANGED",
             "SM_HERO_OPENHEALTH", "SM_HERO_CLOSEHEALTH", "SM_HERO_WEIGHTCHANGED",
             "SM_HERO_ATTACKMODE", "SM_HERO_SPACEMOVE", "SM_HERO_STRUCK", "SM_HERO_DEATH",
             "SM_HERO_ALIVE", "SM_HERO_TURN", "SM_HERO_WALK", "SM_HERO_RUN", "SM_HERO_HIT",
             "SM_HERO_HEAVYHIT", "SM_HERO_BIGHIT", "SM_HERO_SPELL", "SM_HERO_POWERHIT",
             "SM_HERO_LONGHIT2", "SM_HERO_WIDEHIT", "SM_HERO_FIREHIT", "SM_HERO_CRSHIT",
             "SM_HERO_TWINHIT"
         })
{
    Assert(globalType.GetField(inventedHeroResponse, BindingFlags.Static | BindingFlags.Public) == null,
        $"invented hero response returned: {inventedHeroResponse}");
}
AssertConstant(globalType, "SM_COMMIT_ITEM", 4634);
AssertConstant(globalType, "SM_OPEN_COMMIT_ITEM", 4635);
AssertConstant(globalType, "SM_STRENGTHEN_EQUIP_QUEST", 4465);
AssertConstant(globalType, "SM_STRENGTHEN_EQUIP", 4466);
AssertConstant(globalType, "SM_UPDATE_CLOTHES", 4637);
AssertConstant(globalType, "SM_SEND_TITLEINFO", 2870);
AssertConstant(globalType, "SM_QUERY_FOCUS_ITEM", 3290);
AssertConstant(globalType, "SM_QUERY_MAP_NPC", 4610);
AssertConstant(globalType, "SM_TASK_BRIEF_INFO", 1504);
AssertConstant(globalType, "SM_TASK_DETAIL_INFO", 1505);
AssertConstant(globalType, "SM_TASK_PROGRESS_INFO", 1506);
AssertConstant(globalType, "SM_TASK_DELETE", 1507);
AssertConstant(globalType, "SM_TASK_CLEAR_ALL", 1508);
AssertConstant(globalType, "SM_TASK_LIST_CHANGED", 1509);
AssertConstant(globalType, "SM_PUSH_SINGLE_TASK", 1530);
AssertConstant(globalType, "CM_QUERY_TASK_DETAIL", 3051);
AssertConstant(globalType, "CM_QUERY_TASK_ALL", 3052);
AssertConstant(globalType, "CM_DO_TASK_COMMAND", 3053);
AssertConstant(globalType, "CM_QUERY_SINGLE_TASK", 3054);
Assert(globalType.GetField("SM_COMMIT_ITEM_RESULT", BindingFlags.Static | BindingFlags.Public) == null,
    "invented commit-item response 4512 returned");
foreach (var inventedResponse in new[] { "SM_STRENGTHEN_RESULT", "SM_TITLE_RESULT", "SM_MAP_NPC_RESULT" })
{
    Assert(globalType.GetField(inventedResponse, BindingFlags.Static | BindingFlags.Public) == null,
        $"invented feature response returned: {inventedResponse}");
}
foreach (var (name, value) in new (string Name, int Value)[]
         {
             ("CM_QUERY_STALL", 4418), ("SM_QUERY_STALL", 4418),
             ("CM_SET_STALL_TIMELV", 4419), ("SM_SET_STALL_TIMELV", 4419),
             ("CM_SET_STALL_NAME", 4420), ("SM_SET_STALL_NAME", 4420),
             ("CM_ADD_STALLITEM", 4421), ("SM_ADD_STALLITEM", 4421),
             ("CM_DEL_STALLITEM", 4422), ("SM_DEL_STALLITEM", 4422),
             ("CM_CANCEL_STALL", 4423), ("SM_CANCEL_STALL", 4423),
             ("CM_START_STALL", 4424), ("SM_START_STALL", 4424),
             ("CM_PAUSE_STALL", 4425), ("SM_PAUSE_STALL", 4425),
             ("CM_BUY_STALLITEM", 4426), ("SM_BUY_STALLITEM", 4426),
             ("SM_UPT_DEL_STALLITEM", 4427),
             ("SM_UPT_ADD_STALLITEM", 4428),
             ("SM_UPT_OTHER_DEL_STALLITEM", 4429),
             ("CM_MESSAGE_STALL", 4467), ("SM_MESSAGE_STALL", 4467),
             ("CM_QUERY_STALL_STATUS", 4481), ("SM_QUERY_STALL_STATUS", 4481)
         })
{
    AssertConstant(globalType, name, value);
}
Assert(globalType.GetField("SM_STALL_RESPONSE", BindingFlags.Static | BindingFlags.Public) == null,
    "invented generic stall response 4480 returned");
Assert(globalType.GetField("CM_QUERYUSERLEVELSORT", BindingFlags.Static | BindingFlags.Public) == null &&
       globalType.GetField("SM_QUERYUSERLEVELSORT", BindingFlags.Static | BindingFlags.Public) == null &&
       globalType.GetField("RM_QUERYUSERLEVELSORT", BindingFlags.Static | BindingFlags.Public) == null,
    "invented user-rank protocol returned on the native 3500 gap");
foreach (var fakeProtocol in new[]
         {
             "CM_SENDSELL", "CM_QUERYMALL", "CM_BUYMALLITEM", "CM_THROW",
             "CM_MOBILE_COMMAND", "CM_SMUGGLE", "CM_CHECKCLIENT_RES"
         })
{
    Assert(globalType.GetField(fakeProtocol, BindingFlags.Static | BindingFlags.Public) == null,
        $"non-native protocol constant returned: {fakeProtocol}");
}
foreach (var wrongVersionSellOffProtocol in new[]
         {
             "RM_SENDDEALOFFFORM", "SM_SENDDEALOFFFORM",
             "CM_SELLOFFADDITEM", "SM_SELLOFFADDITEM_OK", "RM_SELLOFFADDITEM_OK",
             "SM_SellOffADDITEM_FAIL", "RM_SellOffADDITEM_FAIL",
             "CM_SELLOFFDELITEM", "SM_SELLOFFDELITEM_OK", "RM_SELLOFFDELITEM_OK",
             "SM_SELLOFFDELITEM_FAIL", "RM_SELLOFFDELITEM_FAIL",
             "CM_SELLOFFCANCEL", "RM_SELLOFFCANCEL", "SM_SellOffCANCEL",
             "CM_SELLOFFEND", "SM_SELLOFFEND_OK", "RM_SELLOFFEND_OK",
             "SM_SELLOFFEND_FAIL", "RM_SELLOFFEND_FAIL",
             "RM_QUERYYBSELL", "SM_QUERYYBSELL", "RM_QUERYYBDEAL", "SM_QUERYYBDEAL",
             "CM_CANCELSELLOFFITEMING", "CM_SELLOFFBUYCANCEL",
             "CM_SELLOFFBUY", "SM_SELLOFFBUY_OK", "RM_SELLOFFBUY_OK"
         })
{
    Assert(globalType.GetField(wrongVersionSellOffProtocol,
               BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) == null,
        $"wrong-version 23000 sell-off protocol returned: {wrongVersionSellOffProtocol}");
}
AssertConstant(globalType, "CM_SPEEDHACKUSER", 1042);
Assert(globalType.GetField("SM_NEARBY_RESULT", BindingFlags.Static | BindingFlags.Public) == null,
    "invented text-based nearby/group response 4490 returned");
Assert(systemModule.GetType("SystemModule.TUserLevelSort", throwOnError: false) == null,
    "invented user-rank payload type returned");

var textReaderType = gameSvr.GetType("GameSvr.PasEngine.PasScriptTextReader", throwOnError: true)!;
var readAllText = textReaderType.GetMethod("ReadAllText", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
var mallScript = (string)readAllText.Invoke(null, new object[] { mallScriptPath })!;
Assert(mallScript.Contains("超级金创药", StringComparison.Ordinal), "GBK PAS reader lost Chinese text");

var mallManagerType = gameSvr.GetType("GameSvr.Mall.MallManager", throwOnError: true)!;
var mallManager = mallManagerType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
var loadPasMallItems = mallManagerType.GetMethod("LoadPasMallItems", BindingFlags.Instance | BindingFlags.NonPublic)!;
var mallItems = ((IEnumerable)loadPasMallItems.Invoke(mallManager, new object[] { mallScriptPath })!).Cast<object>().ToList();
Assert(mallItems.Count == 34, $"expected 34 mall items, got {mallItems.Count}");
var pageItem = mallItems.Single(item => GetProperty<string>(item, "ItemName") == "书页");
Assert(GetProperty<int>(pageItem, "Price") == 200, "mall price parsing failed for 书页");

var playObjectType = gameSvr.GetType("GameSvr.TPlayObject", throwOnError: true)!;
Assert(gameSvr.GetType("GameSvr.TDealOffInfo", throwOnError: false) == null &&
       gameSvr.GetType("GameSvr.TClientDealOffInfo", throwOnError: false) == null,
    "memory-only 23000 sell-off payload types returned");
foreach (var legacySellOffMember in new[]
         {
             "m_boSellOffOK", "m_SellOffItemList", "ClientAddSellOffItem",
             "ClientBuySellOffItme", "GetSellOffGlod", "SellOffInTime",
             "GetBackSellOffItems", "SelectSellDate"
         })
{
    Assert(playObjectType.GetField(legacySellOffMember,
               BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null &&
           playObjectType.GetMethod(legacySellOffMember,
               BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
        $"memory-only 23000 sell-off member returned: {legacySellOffMember}");
}
var m2ShareType = gameSvr.GetType("GameSvr.M2Share", throwOnError: true)!;
foreach (var legacySellOffGlobal in new[]
         {
             "sSellOffItemList", "g_DisableSellOffList", "sDealYBme",
             "sGetSellOffGlod", "LoadAllowSellOffItem", "SaveAllowSellOffItem"
         })
{
    Assert(m2ShareType.GetField(legacySellOffGlobal,
               BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) == null &&
           m2ShareType.GetMethod(legacySellOffGlobal,
               BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) == null,
        $"memory-only 23000 sell-off global returned: {legacySellOffGlobal}");
}
Assert(gameSvr.GetType("GameSvr.PlayerTask", throwOnError: false) == null &&
       playObjectType.GetField("m_TaskList",
           BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
    "invented per-player task model returned");
foreach (var fakeTaskMethod in new[]
         {
             "FindTask", "SendTaskBriefInfo", "SendTaskDetailInfo",
             "SendTaskProgressInfo", "SendTaskDelete", "SendPushSingleTask"
         })
{
    Assert(playObjectType.GetMethod(fakeTaskMethod,
               BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
        $"invented binary task sender returned: {fakeTaskMethod}");
}
foreach (var nativeTaskMethod in new[]
         {
             "AddTaskToUIList", "UpdateTaskDetail", "UpdateTaskProgress", "DeleteTaskFromUIList"
         })
{
    Assert(playObjectType.GetMethod(nativeTaskMethod, new[] { typeof(int), typeof(int) }) != null,
        $"native Pascal task UI method is missing: {nativeTaskMethod}");
}
var buildMapNpcRecord = playObjectType.GetMethod("BuildMapNpcRecord",
    BindingFlags.Static | BindingFlags.NonPublic)!;
const string mapNpcName = "比奇老兵";
var mapNpcNameBytes = gbk.GetBytes(mapNpcName);
var mapNpcRecord = (byte[])buildMapNpcRecord.Invoke(null, new object[] { mapNpcName, 321, 654 })!;
Assert(mapNpcRecord.Length == 20, $"map NPC record length is {mapNpcRecord.Length}");
Assert(mapNpcRecord[0] == mapNpcNameBytes.Length, "map NPC ShortString length mismatch");
Assert(mapNpcRecord.Skip(1).Take(mapNpcNameBytes.Length).SequenceEqual(mapNpcNameBytes),
    "map NPC GBK ShortString payload mismatch");
Assert(mapNpcRecord.Skip(1 + mapNpcNameBytes.Length).Take(15 - mapNpcNameBytes.Length).All(value => value == 0),
    "map NPC ShortString padding is not zero");
Assert(BitConverter.ToUInt16(mapNpcRecord, 16) == 321 && BitConverter.ToUInt16(mapNpcRecord, 18) == 654,
    "map NPC coordinate offsets mismatch");
var truncatedMapNpcRecord = (byte[])buildMapNpcRecord.Invoke(null,
    new object[] { "ABCDEFGHIJKLMNOP", 1, 2 })!;
Assert(truncatedMapNpcRecord[0] == 15 &&
       Encoding.ASCII.GetString(truncatedMapNpcRecord, 1, 15) == "ABCDEFGHIJKLMNO",
    "map NPC ShortString[15] truncation mismatch");
foreach (var fakeStallField in new[] { "_boStallOpen", "_sStallName", "_nStallTimeLv" })
{
    Assert(playObjectType.GetField(fakeStallField,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
        $"memory-only stall state returned: {fakeStallField}");
}
Assert(playObjectType.GetMethod("RejectUnavailableStallRequest",
           BindingFlags.Instance | BindingFlags.NonPublic) != null,
    "incomplete stall implementation no longer rejects requests explicitly");
foreach (var removedRenameType in new[]
         {
             "GameSvr.ChangeNameCommand", "GameSvr.ChangeMasterNameCommand",
             "GameSvr.HeroRenameCommand", "GameSvr.ReNameGuildCommand"
         })
{
    Assert(gameSvr.GetType(removedRenameType, throwOnError: false) == null,
        $"memory-only rename command returned: {removedRenameType}");
}
Assert(playObjectType.GetMethod("ClientGetUserOrder", BindingFlags.Instance | BindingFlags.NonPublic) == null,
    "invented TBL-based user-rank handler returned");
Assert(playObjectType.GetMethod("ClientHandleCreateGroupRequest", BindingFlags.Instance | BindingFlags.NonPublic) == null,
    "CM_CREATEGROUP again multiplexes non-native 1000/2000/3000 requests");
Assert(playObjectType.GetMethod("ClientQueryMall", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null &&
       playObjectType.GetMethod("ClientBuyMallItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
    "invented 910x text mall implementation returned");
var userEngineType = gameSvr.GetType("GameSvr.UserEngine", throwOnError: true)!;
Assert(userEngineType.GetMethod("HandleMobileCmd", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
    "invented mobile response-id input handler returned");
var mailServiceType = gameSvr.GetType("GameSvr.Services.MailService", throwOnError: true)!;
var mailService = Activator.CreateInstance(mailServiceType)!;
var newFullMailEx = mailServiceType.GetMethod("NewFullMailEx",
    BindingFlags.Instance | BindingFlags.Public, null,
    new[]
    {
        typeof(string), typeof(string), typeof(string), typeof(int), typeof(int),
        typeof(int), typeof(string), typeof(string)
    }, null)!;
Assert(!(bool)newFullMailEx.Invoke(mailService,
        new object[] { "receiver", "title", "context", 1, 100, 0, "item|1", string.Empty })!,
    "mail placeholder acknowledged a delivery without the native transaction");
var baseObjectType = gameSvr.GetType("GameSvr.TBaseObject", throwOnError: true)!;
var buildMobileFeature = baseObjectType.GetMethod("BuildMobileFeatureRecord", BindingFlags.Static | BindingFlags.NonPublic)!;
var mobileMonsterFeature = (byte[])buildMobileFeature.Invoke(null,
    new object[] { (ushort)11, (byte)0, (byte)0, (ushort)3, (ushort)160, (ushort)0 })!;
Assert(mobileMonsterFeature.Length == 10, "mobile monster TFeature length mismatch");
Assert(BitConverter.ToUInt16(mobileMonsterFeature, 0) == 11, "mobile monster race mismatch");
Assert(BitConverter.ToUInt16(mobileMonsterFeature, 4) == 3, "mobile monster weapon mismatch");
Assert(BitConverter.ToUInt16(mobileMonsterFeature, 6) == 160, "mobile monster appearance was lost");
Assert(BitConverter.ToUInt16(mobileMonsterFeature, 0) * 1000 + BitConverter.ToUInt16(mobileMonsterFeature, 6) == 11160,
    "mobile monster config key mismatch");

var buildStruckBody = playObjectType.GetMethod("BuildMobileStruckBody", BindingFlags.Static | BindingFlags.NonPublic)!;
var struck = (byte[])buildStruckBody.Invoke(null, new object[] { 0x10203040, true, 123, 456, 78, 90 })!;
Assert(struck.Length == 32, $"SM_STRUCK body length is {struck.Length}");
Assert(BitConverter.ToInt32(struck, 0) == 0 && BitConverter.ToInt32(struck, 4) == 0, "SM_STRUCK reserved fields are not zero");
Assert(BitConverter.ToInt32(struck, 8) == 0x10203040, "SM_STRUCK attacker offset mismatch");
Assert(BitConverter.ToInt32(struck, 12) == 1, "SM_STRUCK magic flag offset mismatch");
Assert(BitConverter.ToInt32(struck, 16) == 123 && BitConverter.ToInt32(struck, 20) == 456, "SM_STRUCK HP snapshot mismatch");
Assert(BitConverter.ToInt32(struck, 24) == 78 && BitConverter.ToInt32(struck, 28) == 90, "SM_STRUCK MP snapshot mismatch");

var buildNewStateRecord = playObjectType.GetMethod("BuildMobileNewStateRecord", BindingFlags.Static | BindingFlags.NonPublic)!;
const string monsterName = "白野猪";
var mobileFeature = new byte[] { 6, 0, 0, 0, 1, 0, 2, 0, 0, 0 };
var mobileStateObject = RuntimeHelpers.GetUninitializedObject(baseObjectType);
baseObjectType.GetField("m_nCharStatus")!.SetValue(mobileStateObject, 0x1234);
var namedTurn = (byte[])buildNewStateRecord.Invoke(null, new object[]
{
    0x01020304u,
    mobileStateObject,
    (byte)255,
    mobileFeature,
    (byte)1,
    monsterName
})!;
var nameBytes = gbk.GetBytes(monsterName);
Assert(namedTurn.Length == 42 + nameBytes.Length, $"named SM_TURN body length is {namedTurn.Length}");
Assert(BitConverter.ToInt16(namedTurn, 0) == 41, "named SM_TURN fixed record length mismatch");
Assert(BitConverter.ToInt32(namedTurn, 6) == 0x1234, "named SM_TURN body-state offset mismatch");
Assert(namedTurn.AsSpan(41, nameBytes.Length).SequenceEqual(nameBytes), "named SM_TURN GBK name offset mismatch");
Assert(namedTurn[^1] == 0, "named SM_TURN name is not NUL-terminated");

baseObjectType.GetField("m_nCharStatus2")!.SetValue(mobileStateObject, 0x2345);
baseObjectType.GetField("m_nCharStatus3")!.SetValue(mobileStateObject, 0x3456);
baseObjectType.GetField("m_nCharStatus4")!.SetValue(mobileStateObject, 0x4567);
baseObjectType.GetField("m_btRaceServer")!.SetValue(mobileStateObject, (byte)50);
baseObjectType.GetField("m_btRaceImg")!.SetValue(mobileStateObject, (byte)11);
baseObjectType.GetField("m_btMonsterWeapon")!.SetValue(mobileStateObject, (byte)3);
baseObjectType.GetField("m_wAppr")!.SetValue(mobileStateObject, (ushort)160);
var buildActorStateBody = playObjectType.GetMethod("BuildMobileActorStateBody",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var actorStateBody = (byte[])buildActorStateBody.Invoke(null,
    new object[] { 0x01020304, mobileStateObject })!;
Assert(actorStateBody.Length == 32, $"death/alive actor-state body length is {actorStateBody.Length}");
Assert(BitConverter.ToInt32(actorStateBody, 0) == 0x01020304 &&
       BitConverter.ToInt32(actorStateBody, 4) == 0x1234 &&
       BitConverter.ToInt32(actorStateBody, 8) == 0x2345 &&
       BitConverter.ToInt32(actorStateBody, 12) == 0x3456 &&
       BitConverter.ToInt32(actorStateBody, 16) == 0x4567,
    "death/alive actor-state feature or TAllBodyState layout mismatch");
Assert(BitConverter.ToUInt16(actorStateBody, 20) == 11 &&
       BitConverter.ToUInt16(actorStateBody, 24) == 3 &&
       BitConverter.ToUInt16(actorStateBody, 26) == 160,
    "death/alive actor-state mobile feature layout mismatch");
Assert(BitConverter.ToUInt16(actorStateBody, 30) == 0,
    "death/alive actor-state padding is not zero");

var moveEnvironmentType = gameSvr.GetType("GameSvr.Envirnoment", throwOnError: true)!;
Assert(moveEnvironmentType.GetMethod("MoveToMovingObjectForRun", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null,
    "run occupancy commit method is missing");
Assert(playObjectType.GetMethod("CommitRunMove", BindingFlags.Instance | BindingFlags.NonPublic) != null,
    "run movement does not expose the strict commit path");
var edgeMoveEnvironment = Activator.CreateInstance(moveEnvironmentType)!;
moveEnvironmentType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(edgeMoveEnvironment, new object[] { (short)10, (short)10 });
var edgeMoveObject = RuntimeHelpers.GetUninitializedObject(baseObjectType);
baseObjectType.GetField("m_PEnvir")!.SetValue(edgeMoveObject, edgeMoveEnvironment);
baseObjectType.GetField("m_nCurrX")!.SetValue(edgeMoveObject, (short)1);
baseObjectType.GetField("m_nCurrY")!.SetValue(edgeMoveObject, (short)8);
var canMoveOneStep = baseObjectType.GetMethod("CanMove",
    new[] { typeof(short), typeof(short), typeof(bool) })!;
Assert((bool)canMoveOneStep.Invoke(edgeMoveObject, new object[] { (short)2, (short)9, false })!,
    "one-step path check compared the destination Y against the current X");

var gameSvrGateServiceType = gameSvr.GetType("GameSvr.GateService", throwOnError: true)!;
Assert(gameSvrGateServiceType.GetField("_frameParser", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType.Name ==
       "InternalPacket77FrameParser",
    "GameSvr gate receive path is not using the tested 77BBAA33 stream parser");
Assert(gameSvrGateServiceType.GetField("GameBuffer", BindingFlags.Instance | BindingFlags.NonPublic) == null &&
       gameSvrGateServiceType.GetField("nBuffLen", BindingFlags.Instance | BindingFlags.NonPublic) == null,
    "allocation-heavy hand-written GameSvr gate receive buffer returned");
TestGameSvrGateSingleWriter(gameSvr, globalType);
var gameSvrGateManagerType = gameSvr.GetType("GameSvr.GateManager", throwOnError: true)!;
Assert(gameSvrGateManagerType.GetMethod("StartMessageQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null &&
       gameSvrGateManagerType.GetMethod("AddGameGateQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
    "unused GameSvr gate channel that discarded every queued message returned");
var gameSvrPacketTrace = gameSvrGateServiceType.GetMethod("PacketTrace", BindingFlags.Static | BindingFlags.NonPublic)!;
var gameSvrTraceConditional = gameSvrPacketTrace.GetCustomAttribute<System.Diagnostics.ConditionalAttribute>();
Assert(gameSvrTraceConditional?.ConditionString == "GAMESVR_PACKET_TRACE",
    "GameSvr packet trace is active in normal builds");
Assert((gameSvrPacketTrace.GetMethodBody()?.GetILAsByteArray()?.Length ?? int.MaxValue) <= 2,
    "GameSvr packet trace still writes in the production build");

var mapCellType = gameSvr.GetType("GameSvr.MapCellinfo", throwOnError: true)!;
var mapCell = Activator.CreateInstance(mapCellType)!;
Assert(mapCellType.IsValueType, "map cells are still heap objects instead of compact values");
Assert(GetProperty<int>(mapCell, "Count") == 0, "empty map cell count mismatch");
Assert(mapCellType.GetField("ObjList")!.GetValue(mapCell) == null, "empty map cell allocated an object list");

var mapEnvironment = Activator.CreateInstance(moveEnvironmentType)!;
moveEnvironmentType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(mapEnvironment, new object[] { (short)4, (short)3 });
var mapAttributes = (Array)moveEnvironmentType.GetField("MapCellAttributes", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(mapEnvironment)!;
var mapObjectLists = (Array)moveEnvironmentType.GetField("MapCellObjectLists", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(mapEnvironment)!;
Assert(mapAttributes.Length == 12 && mapObjectLists.Length == 12, "compact map backing arrays have the wrong size");
Assert(mapObjectLists.Cast<object?>().All(value => value == null), "empty map cells preallocated object lists");
moveEnvironmentType.GetMethod("EnsureCellObjectList", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(mapEnvironment, new object[] { 1, 1 });
Assert(mapObjectLists.GetValue(4) != null, "occupied map cell did not create its list lazily");
moveEnvironmentType.GetMethod("ReleaseCellObjectList", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(mapEnvironment, new object[] { 1, 1 });
Assert(mapObjectLists.GetValue(4) == null, "empty map cell retained its object list");

var objectManagerType = gameSvr.GetType("GameSvr.ObjectManager", throwOnError: true)!;
Assert(objectManagerType.GetMethod("AddOhter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null &&
       objectManagerType.GetMethod("GetOhter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
    "the removed global one-shot payload side channel returned");
var sendMessageType = gameSvr.GetType("GameSvr.SendMessage", throwOnError: true)!;
var queuedObject = RuntimeHelpers.GetUninitializedObject(baseObjectType);
baseObjectType.GetField("m_MsgList")!.SetValue(queuedObject,
    Activator.CreateInstance(typeof(List<>).MakeGenericType(sendMessageType))!);
var processMsgLock = m2ShareType.GetField("ProcessMsgCriticalSection", BindingFlags.Static | BindingFlags.Public)!;
processMsgLock.SetValue(null, processMsgLock.GetValue(null) ?? new object());
var payload = new object();
baseObjectType.GetMethod("SendMsg", new[]
{
    baseObjectType, typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(string), typeof(object)
})!.Invoke(queuedObject, new object?[] { null, 42, 1, 2, 3, 4, "queued", payload });
var getMessage = baseObjectType.GetMethod("GetMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
var processArgs = new object?[] { null };
Assert((bool)getMessage.Invoke(queuedObject, processArgs)! && processArgs[0] != null,
    "queued object message was not converted to TProcessMessage");
var processMessageType = systemModule.GetType("SystemModule.TProcessMessage", throwOnError: true)!;
Assert(ReferenceEquals(processMessageType.GetField("Payload")!.GetValue(processArgs[0]), payload),
    "queued object payload was lost before TProcessMessage dispatch");
processArgs[0] = null;
Assert(!(bool)getMessage.Invoke(queuedObject, processArgs)!,
    "object message queue retained a consumed payload");

var associationType = gameSvr.GetType("GameSvr.Association", throwOnError: true)!;
var guildRankType = gameSvr.GetType("GameSvr.TGuildRank", throwOnError: true)!;
var guildMemberType = gameSvr.GetType("GameSvr.TGuildMember", throwOnError: true)!;
var association = RuntimeHelpers.GetUninitializedObject(associationType);
var guildRank = Activator.CreateInstance(guildRankType)!;
var guildMember = Activator.CreateInstance(guildMemberType)!;
var guildMemberList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(guildMemberType))!;
var guildRankList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(guildRankType))!;
var guildPlayer = RuntimeHelpers.GetUninitializedObject(playObjectType);
playObjectType.GetField("m_sCharName")!.SetValue(guildPlayer, "audit-player");
guildMemberType.GetField("sMemberName")!.SetValue(guildMember, "audit-player");
guildMemberType.GetField("PlayObject")!.SetValue(guildMember, guildPlayer);
guildMemberList.Add(guildMember);
guildRankType.GetField("MemberList")!.SetValue(guildRank, guildMemberList);
guildRankList.Add(guildRank);
associationType.GetField("m_RankList")!.SetValue(association, guildRankList);
associationType.GetMethod("DelHumanObj")!.Invoke(association, new[] { guildPlayer });
Assert(guildMemberList.Count == 1 && ReferenceEquals(guildMemberList[0], guildMember),
    "guild logout removed the persistent member record");
Assert((string)guildMemberType.GetField("sMemberName")!.GetValue(guildMember)! == "audit-player" &&
       guildMemberType.GetField("PlayObject")!.GetValue(guildMember) == null,
    "guild logout did not clear only the online player reference");

var snapsServiceType = gameSvr.GetType("GameSvr.SnapsmService", throwOnError: true)!;
var maxMirrorConnections = snapsServiceType.GetField("MaxMirrorConnections", BindingFlags.Static | BindingFlags.NonPublic)!;
Assert((int)maxMirrorConnections.GetRawConstantValue()! == 10,
    "mirror socket pool is not bounded to the server slot count");

var gameGate = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameGateDirectory, "GameGate.dll"));
var gateServerType = gameGate.GetType("GameGate.Core.GateServer", throwOnError: true)!;
var packetTrace = gateServerType.GetMethod("Trace", BindingFlags.Instance | BindingFlags.NonPublic)!;
var traceConditional = packetTrace.GetCustomAttribute<System.Diagnostics.ConditionalAttribute>();
Assert(traceConditional?.ConditionString == "GAMEGATE_PACKET_TRACE",
    "GameGate packet trace is active in normal builds");
Assert((packetTrace.GetMethodBody()?.GetILAsByteArray()?.Length ?? int.MaxValue) <= 2,
    "GameGate packet trace still writes in the production build");
var softCloseDelay = gateServerType.GetField("SoftCloseQueryDelayMs", BindingFlags.Static | BindingFlags.NonPublic)!;
Assert((int)softCloseDelay.GetRawConstantValue()! == 600, "soft-close character query delay mismatch");
var createSoftCloseQuery = gateServerType.GetMethod("CreateSoftCloseQueryPacket", BindingFlags.Static | BindingFlags.NonPublic)!;
var softCloseHeader = createSoftCloseQuery.Invoke(null, new object[] { 131532307 })!;
Assert(GetField<int>(softCloseHeader, "Recog") == 131532307, "soft-close query session mismatch");
Assert(GetField<ushort>(softCloseHeader, "Ident") == Convert.ToUInt16(GetConstant(globalType, "CM_QUERYCHR")),
    "soft-close query ident mismatch");
Assert(GetField<ushort>(softCloseHeader, "Param") == 1, "soft-close query marker is missing");

var clientSessionType = gameGate.GetType("GameGate.Models.ClientSession", throwOnError: true)!;
var clientSession = Activator.CreateInstance(clientSessionType)!;
clientSessionType.GetField("Account")!.SetValue(clientSession, "account");
clientSessionType.GetField("CharName")!.SetValue(clientSession, "character");
clientSessionType.GetField("HWID")!.SetValue(clientSession, "hwid");
clientSessionType.GetField("DBSessionId")!.SetValue(clientSession, 123);
clientSessionType.GetField("EncryptKey")!.SetValue(clientSession, 456u);
clientSessionType.GetField("IsTiger")!.SetValue(clientSession, true);
clientSessionType.GetField("TigerKeyOffset")!.SetValue(clientSession, 789u);
clientSessionType.GetField("TurnPack")!.SetValue(clientSession, true);
clientSessionType.GetField("RemoteAddr")!.SetValue(clientSession, "127.0.0.2");
clientSessionType.GetField("RemotePort")!.SetValue(clientSession, 7000);
clientSessionType.GetField("TcpClient")!.SetValue(clientSession, new object());
var stateField = clientSessionType.GetField("State")!;
stateField.SetValue(clientSession, Enum.ToObject(stateField.FieldType, 1));
clientSessionType.GetMethod("Reset")!.Invoke(clientSession, null);
Assert(clientSessionType.GetField("Account")!.GetValue(clientSession) == null &&
       clientSessionType.GetField("CharName")!.GetValue(clientSession) == null &&
       clientSessionType.GetField("HWID")!.GetValue(clientSession) == null,
    "GameGate session reset retained client identity");
Assert(GetField<int>(clientSession, "DBSessionId") == 0 &&
       GetField<uint>(clientSession, "EncryptKey") == 0 &&
       !GetField<bool>(clientSession, "IsTiger") &&
       GetField<uint>(clientSession, "TigerKeyOffset") == 0 &&
       !GetField<bool>(clientSession, "TurnPack"),
    "GameGate session reset retained protocol state");
Assert(GetField<string>(clientSession, "RemoteAddr") == string.Empty &&
       GetField<int>(clientSession, "RemotePort") == 0 &&
       clientSessionType.GetField("TcpClient")!.GetValue(clientSession) == null &&
       Convert.ToInt32(stateField.GetValue(clientSession)) == 0,
    "GameGate session reset retained connection state");

var dbSvr = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dbSvrDirectory, "DBSvr.dll"));
Assert(dbSvr.GetType("DBSvr.Services.MobileGateService", throwOnError: false) == null,
    "invented DBSvr 7010/7110 mobile gate adapter returned");
TestNativeStorageCapacity(dbSvr, systemModule);
TestP6CProtocolAndSessionRepairs(gameSvr, gameGate, dbSvr, systemModule, gbk);
var userSocType = dbSvr.GetType("DBSvr.UserSocService", throwOnError: true)!;
var canRestoreSoftClose = userSocType.GetMethod("CanRestoreSoftCloseSession", BindingFlags.Static | BindingFlags.NonPublic)!;
Assert(CanRestore("111111", 131532307, 1, "111111", 131532307, true),
    "authenticated soft-close session restore was rejected");
Assert(!CanRestore("other", 131532307, 1, "111111", 131532307, true),
    "soft-close restore accepted a different account");
Assert(!CanRestore("111111", 7, 1, "111111", 131532307, true),
    "soft-close restore accepted a different session");
Assert(!CanRestore("111111", 131532307, 0, "111111", 131532307, true),
    "ordinary character query can restore a missing session");
Assert(!CanRestore("111111", 131532307, 1, "111111", 131532307, false),
    "soft-close restore accepted a connection that never selected a character");

var configType = dbSvr.GetType("DBSvr.ConfigManager", throwOnError: true)!;
var iniPath = Path.Combine(Path.GetTempPath(), $"engine-audit-{Guid.NewGuid():N}.ini");
try
{
    File.WriteAllText(iniPath,
        "[GameGates]\r\nListenPort=5100\r\nShared=old\r\n" +
        "[GameGates]\r\nGameGate1=127.0.0.1:7100\r\nShared=new\r\n", gbk);
    var config = Activator.CreateInstance(configType, iniPath)!;
    Assert((string)configType.GetMethod("ReadString")!.Invoke(config, new object[] { "GameGates", "ListenPort", "" })! == "5100",
        "duplicate INI section discarded earlier keys");
    Assert((string)configType.GetMethod("ReadString")!.Invoke(config, new object[] { "GameGates", "GameGate1", "" })! == "127.0.0.1:7100",
        "duplicate INI section discarded later keys");
    Assert((string)configType.GetMethod("ReadString")!.Invoke(config, new object[] { "GameGates", "Shared", "" })! == "new",
        "duplicate INI key did not use the later value");

    configType.GetMethod("WriteInteger", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(config, new object[] { "Persist", "Count", 42 });
    configType.GetMethod("WriteBool", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(config, new object[] { "Persist", "Enabled", true });
    var timestamp = new DateTime(2026, 7, 11, 17, 30, 0, DateTimeKind.Local);
    configType.GetMethod("WriteDateTime", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(config, new object[] { "Persist", "Timestamp", timestamp });

    var reloadedConfig = Activator.CreateInstance(configType, iniPath)!;
    Assert((int)configType.GetMethod("ReadInteger", new[] { typeof(string), typeof(string), typeof(int) })!
        .Invoke(reloadedConfig, new object[] { "Persist", "Count", 0 })! == 42,
        "INI integer write was not persisted");
    Assert((bool)configType.GetMethod("ReadBool", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(reloadedConfig, new object[] { "Persist", "Enabled", false })!,
        "INI boolean write was not persisted");
    Assert((DateTime)configType.GetMethod("ReadDateTime", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(reloadedConfig, new object[] { "Persist", "Timestamp", DateTime.MinValue })! == timestamp,
        "INI date/time write was not persisted");
}
finally
{
    File.Delete(iniPath);
}

Console.WriteLine(
    $"PASS pascal=compare/global/string/makeSlave6 monScript=config/state/context/drops marketPersistence=runtime-only/tick-ref weaponUpg=208HEX/native-table/safe-tail sequence=100000/unique lock=100000 dbFraming=half+sticky+limits ini=merge+persist sessionReset=ok guildLogout=member-preserved shop={mallItems.Count}/GBK/812,815,816 monsterKey=11160 packetTrace=off(game+gate) compactMapCell=ok socketPool=10 oneShotCache=ok struck={struck.Length} namedTurn={namedTurn.Length} runCommit=ok softClose=600ms/marker1/session-bound");

bool CanRestore(string requestedAccount, int requestedSessionId, ushort packetParam,
    string authenticatedAccount, int authenticatedSessionId, bool characterSelected) =>
    (bool)canRestoreSoftClose.Invoke(null, new object[]
    {
        requestedAccount, requestedSessionId, packetParam,
        authenticatedAccount, authenticatedSessionId, characterSelected
    })!;

static object GetConstant(Type type, string name) =>
    type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetRawConstantValue()!;

static void AssertConstant(Type type, string name, int expected) =>
    Assert(Convert.ToInt32(GetConstant(type, name)) == expected, $"{name} constant mismatch");

static void TestPascalCoreSemantics(Assembly gameSvr)
{
    const string source = """
        program SemanticRegression;
        type
          TStringArray = array of String;
        var
          GlobalCounter: Integer;
          GlobalText: string;
          GlobalTexts: array[1..2] of string;
          GlobalNumbers: array[1..2] of Integer;

        procedure WriteGlobals;
        begin
          GlobalCounter := 42;
          GlobalText := 'changed';
        end;

        procedure MutateParams(var Value: Integer; out Doubled: Integer; const Addend: Integer);
        begin
          Value := Value + Addend;
          Doubled := Value * 2;
        end;

        procedure GroupedParams(var First, Second: Integer; out Third, Fourth: Integer);
        begin
          First := Second;
          Third := Fourth;
        end;

        procedure IncrementRef(var Value: Integer);
        begin
          Value := Value + 1;
        end;

        procedure IncrementOut(out Value: Integer);
        begin
          Value := Value + 1;
        end;

        procedure ForwardRef(var Value: Integer);
        begin
          IncrementRef(Value);
        end;

        procedure RunByRef;
        begin
          GlobalCounter := 5;
          GlobalNumbers[1] := 99;
          GlobalNumbers[2] := 99;
          MutateParams(GlobalCounter, GlobalNumbers[1], 7);
          IncrementOut(GlobalNumbers[2]);
          ForwardRef(GlobalNumbers[2]);
        end;

        procedure BumpGlobal;
        begin
          Inc(GlobalCounter);
        end;

        procedure NoOp;
        begin
        end;

        procedure RunBareStatement;
        begin
          BumpGlobal;
        end;

        procedure Schedule;
        begin
          This_Player.CallOut(This_Npc, 1, 'buyBook');
        end;

        procedure BarePlayerStatement;
        begin
          This_Player.ChkIfCanAddUSExp;
        end;

        procedure BareDbStatement;
        begin
          This_DB.PsFirst;
        end;

        function ReadGlobalCounter: Integer;
        begin
          Result := GlobalCounter;
        end;

        function ReadGlobalText: string;
        begin
          Result := GlobalText;
        end;

        function ReadGlobalArrayText: string;
        begin
          Result := GlobalTexts[1];
        end;

        function ReadGlobalNumber1: Integer;
        begin
          Result := GlobalNumbers[1];
        end;

        function ReadGlobalNumber2: Integer;
        begin
          Result := GlobalNumbers[2];
        end;

        function ReadLocalText: string;
        var
          LocalText: string;
        begin
          Result := LocalText;
        end;

        function ReadLocalShadow: Integer;
        var
          GlobalCounter: Integer;
        begin
          GlobalCounter := 77;
          Result := GlobalCounter;
        end;

        function ReadParsedText: string;
        var
          SourceText: string;
          DestText: string;
        begin
          SourceText := 'one/two';
          SourceText := GetValidStr(SourceText, DestText, '/');
          Result := DestText + '|' + SourceText;
        end;

        function PreserveResultAfterStatements: Integer;
        begin
          Result := 123;
          NoOp;
          GetNow;
        end;

        function ReadNowOle: Double;
        begin
          Result := GetNow;
        end;

        function DateDbRoundTrip: Double;
        begin
          Result := ConvertDBToDateTime(ConvertDateTimeToDB(AddDateTimeWithSec(45000, 90)));
        end;

        function SignedSeconds: Integer;
        begin
          Result := minusDataTime(AddDateTimeWithSec(45000, 90), 45000);
        end;

        function AbsoluteSeconds: Integer;
        begin
          Result := SecondsBetween(45000, AddDateTimeWithSec(45000, 90));
        end;

        function RoundedDate: Integer;
        begin
          Result := GetDateNum(45000.6);
        end;

        function DecodedDate: Integer;
        var
          YearPart: Integer;
          MonthPart: Integer;
          DayPart: Integer;
        begin
          PsDecodeDate(45000, YearPart, MonthPart, DayPart);
          Result := YearPart * 10000 + MonthPart * 100 + DayPart;
        end;

        function DecodedTime: Integer;
        var
          HourPart: Integer;
          MinutePart: Integer;
          SecondPart: Integer;
          MillisecondPart: Integer;
        begin
          PsDecodeTime(AddDateTimeWithSec(45000, 3661), HourPart, MinutePart, SecondPart, MillisecondPart);
          Result := HourPart * 10000 + MinutePart * 100 + SecondPart;
        end;

        function ShortCircuitAnd: Integer;
        begin
          if false and ((1 div 0) = 0) then Result := 0 else Result := 1;
        end;

        function ShortCircuitOr: Integer;
        begin
          if true or ((1 div 0) = 0) then Result := 1 else Result := 0;
        end;

        function BarePlayerValue: Integer;
        begin
          Result := This_Player.GetTmpActivePoint;
        end;

        function BareNpcValue: Integer;
        begin
          Result := This_Npc.CheckCurrMapMon;
        end;

        function BareDbProperty: Integer;
        begin
          if This_DB.PsEof then Result := 1 else Result := 0;
        end;

        function CalledDbMethod: Integer;
        begin
          if This_DB.PsEof() then Result := 1 else Result := 0;
        end;

        function CompareEqual: Integer;
        begin
          Result := CompareText('AbC', 'aBc');
        end;

        function CompareLess: Integer;
        begin
          Result := CompareText('abc', 'bcd');
        end;

        function CompareGreater: Integer;
        begin
          Result := CompareText('bcd', 'abc');
        end;

        function DynamicArrayRoundTrip: string;
        var
          Values: TStringArray;
        begin
          SetArrayLength(Values, 2);
          Values[0] := 'left';
          Values[1] := 'right';
          Result := IntToStr(GetArrayLength(Values)) + '|' + Values[0] + '|' + Values[1];
        end;

        function BuildDynamicArray: TStringArray;
        begin
          SetArrayLength(Result, 2);
          Result[0] := 'first';
          Result[1] := 'second';
        end;

        function ReadDynamicArrayResult: string;
        var
          Values: TStringArray;
        begin
          Values := BuildDynamicArray;
          Result := Values[0] + '|' + Values[1];
        end;

        function PascalTypeCasts: string;
        begin
          Result := Integer('12') + '|' + String(34) + '|' + BoolToStr(Boolean(1));
        end;

        function ExceptionContext: string;
        begin
          try
            Assert(False, 'expected failure');
          except
            Result := ExceptionToString(ExceptionType, ExceptionParam);
          end;
        end;

        begin
        end.
        """;

    var lexerType = gameSvr.GetType("GameSvr.PasEngine.PasLexer", throwOnError: true)!;
    var parserType = gameSvr.GetType("GameSvr.PasEngine.PasParser", throwOnError: true)!;
    var programType = gameSvr.GetType("GameSvr.PasEngine.PasProgram", throwOnError: true)!;
    var apiType = gameSvr.GetType("GameSvr.PasEngine.PasApiBridge", throwOnError: true)!;
    var interpreterType = gameSvr.GetType("GameSvr.PasEngine.PasInterpreter", throwOnError: true)!;

    var lexer = Activator.CreateInstance(lexerType, source)!;
    var parser = Activator.CreateInstance(parserType, lexer, string.Empty)!;
    var program = parserType.GetMethod("Parse")!.Invoke(parser, null)!;
    var api = Activator.CreateInstance(apiType)!;
    var interpreter = interpreterType.GetConstructor(new[] { programType, apiType })!
        .Invoke(new[] { program, api });
    var executeProcedure = interpreterType.GetMethod("ExecuteProcedure")!;

    object Execute(string name) =>
        executeProcedure.Invoke(interpreter, new object?[] { name, null })!;
    int AsInt(object value) =>
        (int)value.GetType().GetMethod("AsInt")!.Invoke(value, null)!;
    double AsDouble(object value) =>
        (double)value.GetType().GetMethod("AsDouble")!.Invoke(value, null)!;
    string AsString(object value) =>
        (string)value.GetType().GetMethod("AsString")!.Invoke(value, null)!;
    object Procedure(string name) =>
        ((IEnumerable)GetProperty<object>(program, "Procedures")).Cast<object>()
        .Single(proc => GetProperty<string>(proc, "Name").Equals(name, StringComparison.OrdinalIgnoreCase));
    List<object> Statements(string procedureName) =>
        ((IEnumerable)GetProperty<object>(GetProperty<object>(Procedure(procedureName), "Body"), "Statements"))
        .Cast<object>().ToList();

    var mutateParameters = ((IEnumerable)GetProperty<object>(Procedure("MutateParams"), "Parameters"))
        .Cast<object>().ToList();
    Assert(mutateParameters.Count == 3, "Pascal parameter count was not preserved");
    Assert(GetProperty<object>(mutateParameters[0], "ParameterMode").ToString() == "Var" &&
           GetProperty<bool>(mutateParameters[0], "IsByRef"),
        "Pascal var parameter mode was discarded");
    Assert(GetProperty<object>(mutateParameters[1], "ParameterMode").ToString() == "Out" &&
           GetProperty<bool>(mutateParameters[1], "IsByRef"),
        "Pascal out parameter mode was discarded");
    Assert(GetProperty<object>(mutateParameters[2], "ParameterMode").ToString() == "Const" &&
           !GetProperty<bool>(mutateParameters[2], "IsByRef"),
        "Pascal const parameter mode was discarded");
    var groupedParameters = ((IEnumerable)GetProperty<object>(Procedure("GroupedParams"), "Parameters"))
        .Cast<object>().ToList();
    Assert(groupedParameters.Count == 4 &&
           groupedParameters.Take(2).All(parameter =>
               GetProperty<object>(parameter, "ParameterMode").ToString() == "Var") &&
           groupedParameters.Skip(2).All(parameter =>
               GetProperty<object>(parameter, "ParameterMode").ToString() == "Out"),
        "Pascal grouped parameters did not retain their shared var/out mode");

    var scheduleCall = Statements("Schedule").Single();
    Assert(scheduleCall.GetType().Name == "PasCallStmt" &&
           GetProperty<bool>(scheduleCall, "IsMethod") &&
           GetProperty<string>(scheduleCall, "ObjectName").Equals("This_Player", StringComparison.OrdinalIgnoreCase) &&
           GetProperty<string>(scheduleCall, "Name").Equals("CallOut", StringComparison.OrdinalIgnoreCase),
        "CallOut was not retained as a This_Player method call");
    var scheduleArgs = ((IEnumerable)GetProperty<object>(scheduleCall, "Arguments")).Cast<object>().ToList();
    Assert(scheduleArgs.Count == 3 &&
           GetProperty<string>(scheduleArgs[0], "Name").Equals("This_Npc", StringComparison.OrdinalIgnoreCase) &&
           AsInt(GetProperty<object>(scheduleArgs[1], "Value")) == 1 &&
           AsString(GetProperty<object>(scheduleArgs[2], "Value")) == "buyBook",
        "CallOut AST did not preserve all three arguments in source order");

    var barePlayerAssignment = Statements("BarePlayerValue").Single();
    var barePlayerMember = GetProperty<object>(barePlayerAssignment, "Value");
    var bareNpcAssignment = Statements("BareNpcValue").Single();
    var bareNpcMember = GetProperty<object>(bareNpcAssignment, "Value");
    Assert(barePlayerMember.GetType().Name == "PasMemberAccessExpr" &&
           GetProperty<string>(barePlayerMember, "MemberName") == "GetTmpActivePoint" &&
           bareNpcMember.GetType().Name == "PasMemberAccessExpr" &&
           GetProperty<string>(bareNpcMember, "MemberName") == "CheckCurrMapMon" &&
           Statements("BarePlayerStatement").Single().GetType().Name == "PasMemberAccessExpr",
        "bare Player/NPC zero-argument calls were not preserved for unified dispatch");
    var bareDbStatement = Statements("BareDbStatement").Single();
    var bareDbCondition = GetProperty<object>(Statements("BareDbProperty").Single(), "Condition");
    var calledDbCondition = GetProperty<object>(Statements("CalledDbMethod").Single(), "Condition");
    Assert(bareDbStatement.GetType().Name == "PasMemberAccessExpr" &&
           bareDbCondition.GetType().Name == "PasMemberAccessExpr" &&
           calledDbCondition.GetType().Name == "PasMethodCallExpr",
        "This_DB statement/property/method syntax did not retain distinct AST contexts");

    Assert(AsInt(Execute("CompareEqual")) == 0, "Pascal CompareText equality is not zero");
    Assert(AsInt(Execute("CompareLess")) < 0, "Pascal CompareText less-than is not negative");
    Assert(AsInt(Execute("CompareGreater")) > 0, "Pascal CompareText greater-than is not positive");
    Assert(AsString(Execute("DynamicArrayRoundTrip")) == "2|left|right" &&
           AsString(Execute("ReadDynamicArrayResult")) == "first|second",
        "Pascal dynamic array alias, SetArrayLength, or function return is broken");
    Assert(AsString(Execute("PascalTypeCasts")) == "12|34|TRUE",
        "Pascal scalar type casts are not dispatched as built-ins");
    var exceptionContext = AsString(Execute("ExceptionContext"));
    Assert(exceptionContext.Contains("PasRuntimeException", StringComparison.Ordinal) &&
           exceptionContext.Contains("expected failure", StringComparison.Ordinal),
        "Pascal except block lost ExceptionType/ExceptionParam");
    Assert(AsString(Execute("ReadGlobalText")) == string.Empty,
        "Pascal global string default is not empty");
    Assert(AsString(Execute("ReadGlobalArrayText")) == string.Empty,
        "Pascal string-array element default is not empty");
    Assert(AsString(Execute("ReadLocalText")) == string.Empty,
        "Pascal local string default is not empty");

    Execute("RunByRef");
    Assert(AsInt(Execute("ReadGlobalCounter")) == 12,
        "Pascal scalar var parameter was not written back");
    Assert(AsInt(Execute("ReadGlobalNumber1")) == 24,
        "Pascal array-element out parameter was not written back");
    Assert(AsInt(Execute("ReadGlobalNumber2")) == 2,
        "Pascal out initialization or nested var write-back is incorrect");
    Execute("RunBareStatement");
    Assert(AsInt(Execute("ReadGlobalCounter")) == 13,
        "Pascal bare zero-argument procedure did not share statement dispatch");
    Assert(AsInt(Execute("ReadLocalShadow")) == 77 &&
           AsInt(Execute("ReadGlobalCounter")) == 13,
        "Pascal local declaration did not shadow the persistent global");
    Assert(AsString(Execute("ReadParsedText")) == "one|two",
        "Pascal GetValidStr return or var Dest write-back is incorrect");
    Assert(AsInt(Execute("PreserveResultAfterStatements")) == 123,
        "Pascal statement calls overwrote the enclosing function Result");
    Assert(AsInt(Execute("ShortCircuitAnd")) == 1 &&
           AsInt(Execute("ShortCircuitOr")) == 1,
        "Pascal and/or evaluated a short-circuited divide-by-zero branch");

    var nowOle = AsDouble(Execute("ReadNowOle"));
    Assert(Math.Abs(nowOle - DateTime.Now.ToOADate()) < 1.0 / 86400.0,
        "Pascal GetNow is not an OLE Automation date");
    var expectedAddedDate = 45000.0 + 90.0 / 86400.0;
    Assert(Math.Abs(AsDouble(Execute("DateDbRoundTrip")) - expectedAddedDate) <= 0.5 / 100000.0 + 1e-10,
        "Pascal native date DB conversion did not round-trip");
    var signedSeconds = AsInt(Execute("SignedSeconds"));
    var absoluteSeconds = AsInt(Execute("AbsoluteSeconds"));
    Assert(signedSeconds == 90 && absoluteSeconds == 90,
        $"Pascal native signed/absolute second difference is incorrect: signed={signedSeconds}, absolute={absoluteSeconds}");
    Assert(AsInt(Execute("RoundedDate")) == 45001,
        "Pascal GetDateNum does not use Delphi Round semantics");
    var decodedDate = DateTime.FromOADate(45000);
    Assert(AsInt(Execute("DecodedDate")) == decodedDate.Year * 10000 + decodedDate.Month * 100 + decodedDate.Day &&
           AsInt(Execute("DecodedTime")) == 10101,
        "Pascal OLE date/time var outputs were not written back");

    Execute("WriteGlobals");
    Assert(AsInt(Execute("ReadGlobalCounter")) == 42,
        "Pascal procedure assignment did not update the global integer");
    Assert(AsString(Execute("ReadGlobalText")) == "changed",
        "Pascal procedure assignment did not update the global string");

    interpreterType.GetMethod("Reset")!.Invoke(interpreter, null);
    Assert(AsInt(Execute("ReadGlobalCounter")) == 0,
        "Pascal Reset did not restore the global integer default");
    Assert(AsString(Execute("ReadGlobalText")) == string.Empty,
        "Pascal Reset did not restore the global string default");
    Assert(AsString(Execute("ReadGlobalArrayText")) == string.Empty,
        "Pascal Reset did not restore the string-array element default");
    Assert(AsInt(Execute("ReadGlobalNumber1")) == 0 &&
           AsInt(Execute("ReadGlobalNumber2")) == 0,
        "Pascal Reset did not restore numeric array defaults");
}

static void TestPascalHostIntegration(Assembly gameSvr, Encoding gbk)
{
    var root = Path.Combine(Path.GetTempPath(), "lyomir-pas-host-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "CommonScripts"));
    Directory.CreateDirectory(Path.Combine(root, "PsNpcscripts"));
    Directory.CreateDirectory(Path.Combine(root, "PsMapQuest"));
    Directory.CreateDirectory(Path.Combine(root, "PsItemScript"));
    Directory.CreateDirectory(Path.Combine(root, "PsTaskList"));
    Directory.CreateDirectory(Path.Combine(root, "MonScript"));

    var apiType = gameSvr.GetType("GameSvr.PasEngine.PasApiBridge", throwOnError: true)!;
    var hostType = gameSvr.GetType("GameSvr.PasEngine.PasScriptHost", throwOnError: true)!;
    var pasValueType = gameSvr.GetType("GameSvr.PasEngine.PasValue", throwOnError: true)!;
    var programType = gameSvr.GetType("GameSvr.PasEngine.PasProgram", throwOnError: true)!;
    var interpreterType = gameSvr.GetType("GameSvr.PasEngine.PasInterpreter", throwOnError: true)!;
    var normNpcType = gameSvr.GetType("GameSvr.NormNpc", throwOnError: true)!;
    var playObjectType = gameSvr.GetType("GameSvr.TPlayObject", throwOnError: true)!;
    var baseObjectType = gameSvr.GetType("GameSvr.TBaseObject", throwOnError: true)!;
    var environmentType = gameSvr.GetType("GameSvr.Envirnoment", throwOnError: true)!;
    var listType = typeof(List<>).MakeGenericType(pasValueType);
    var fromInt = pasValueType.GetMethod("FromInt", BindingFlags.Static | BindingFlags.Public)!;
    var fromObject = pasValueType.GetMethod("FromObject", BindingFlags.Static | BindingFlags.Public)!;
    var scriptHostField = apiType.GetField("ScriptHost", BindingFlags.Static | BindingFlags.Public)!;
    var previousScriptHost = scriptHostField.GetValue(null);

    const string stateScript = """
        program HostState;
        var Counter: Integer;

        procedure OnInitialize;
        begin
          Counter := 5;
        end;

        procedure _Bump;
        begin
          Inc(Counter);
        end;

        function _Read: Integer;
        begin
          Result := Counter;
        end;

        {$IFDEF ACTIVE_HOST_TEST}
        function _ReadDefine: Integer;
        begin
          Result := 77;
        end;
        {$ELSE}
        function _ReadDefine: Integer;
        begin
          Result := 13;
        end;
        {$ENDIF}

        function ReadPlayerName(Player: TPlayer): string;
        begin
          Result := Player.Name;
        end;

        function IdentityPlayer(Player: TPlayer): TPlayer;
        begin
          Result := Player;
        end;

        function _ReadPlayerAlias: string;
        begin
          Result := ReadPlayerName(This_Player);
        end;

        procedure _WriteChainedPlayer;
        begin
          IdentityPlayer(This_Player).CallOutParam := 'chained-player';
        end;

        function _ReadChainedPlayer: string;
        begin
          Result := IdentityPlayer(This_Player).Name + '|' + IdentityPlayer(This_Player).CallOutParam;
        end;

        procedure _ParseChainedMethod;
        begin
          IdentityPlayer(This_Player).SysMsg('not executed', 0);
        end;

        procedure CommitItem(AType: Word);
        begin
          Counter := Counter + AType;
          This_Item.AddPa1 := This_Item.AddPa1 + AType;
        end;

        procedure _SetNpcPrices;
        begin
          This_Npc.RepDoorGold := 101;
          This_Npc.RepWallGold := 102;
          This_Npc.HireGuardGold := 103;
          This_Npc.HireArcherGold := 104;
        end;

        function _ReadNpcPrices: Integer;
        begin
          Result := This_Npc.RepDoorGold + This_Npc.RepWallGold +
                    This_Npc.HireGuardGold + This_Npc.HireArcherGold;
        end;

        begin
        end.
        """;

    var stateScriptPath = Path.Combine(root, "PsNpcscripts", "计时器.pas");
    var mapQuestScriptPath = Path.Combine(root, "PsMapQuest", "任务一.pas");
    var groupMapQuestScriptPath = Path.Combine(root, "PsMapQuest", "任务组.pas");
    var itemMapQuestScriptPath = Path.Combine(root, "PsMapQuest", "任务物品.pas");
    try
    {
        File.WriteAllText(Path.Combine(root, "CommonScripts", "Compiler.inc"),
            "ACTIVE_HOST_TEST\r\n;DISABLED_HOST_TEST\r\n", gbk);
        File.WriteAllText(stateScriptPath, stateScript, gbk);
        File.WriteAllText(mapQuestScriptPath, """
            program MapQuest;
            var TriggerCount: Integer;
            begin
              SetV(16, 2, GetV(16, 2) + 1);
              Inc(TriggerCount);
              if TriggerCount = 1 then Exit;
              SetS(16, 2, TriggerCount);
            end.
            """, gbk);
        File.WriteAllText(groupMapQuestScriptPath, """
            program GroupMapQuest;
            begin
              SetV(16, 3, GetV(16, 3) + 1);
            end.
            """, gbk);
        File.WriteAllText(itemMapQuestScriptPath, """
            program ItemMapQuest;
            begin
              SetV(16, 4, GetV(16, 4) + 1);
            end.
            """, gbk);
        File.WriteAllText(Path.Combine(root, "CommonScripts", "TestItem.pas"),
            "function UseItem: Boolean; begin Result := false; end; begin end.", gbk);
        File.WriteAllText(Path.Combine(root, "PsItemScript", "TestItem.pas"), """
            function UseItem: Boolean;
            begin
              This_Item.AddPa1 := This_Item.AddPa1 + 2;
              Result := (This_Item.ClientItemID = 12345) and (This_Item.AddPa1 = 3);
            end;
            begin
            end.
            """, gbk);
        File.WriteAllText(Path.Combine(root, "PsNpcScript.txt"),
            "计时器 3 10 11 计时NPC 0 1 0 120\r\n" +
            "普通脚本 3 12 13 普通NPC 0 1 0 0\r\n", gbk);
        File.WriteAllText(Path.Combine(root, "PsMapQuest.txt"),
            "3 16 2 2 白野猪 1 * 0 任务一 0\r\n" +
            "3 16 3 10 白野猪 1 * 0 任务组 GROUP\r\n" +
            "3 16 4 10 * 0 任务物品 1 任务物品 0\r\n", gbk);
        File.WriteAllText(Path.Combine(root, "PsTaskList", "TaskOne.pas"), """
            program TaskOne;
            var Calls: Integer;
            function GetTaskID: Integer; begin Result := 101; end;
            function GetTaskType: Integer; begin Result := 10; end;
            function GetTaskTitle: string; begin Result := '首个任务'; end;
            function GetTaskState: Integer;
            begin
              Inc(Calls);
              Result := This_Player.GetV(90, 1) + Calls;
            end;
            function GetTaskDetail: string;
            begin
              Result := This_Player.Name + '|' + IntToStr(Calls);
            end;
            function GetTaskProgress: string; begin Result := IntToStr(Calls); end;
            function DoTaskCommand(const Value: string): Boolean;
            begin
              Result := Value = This_Player.Name;
            end;
            begin end.
            """, gbk);
        File.WriteAllText(Path.Combine(root, "PsTaskList", "TaskDuplicate.pas"), """
            program TaskDuplicate;
            function GetTaskID: Integer; begin Result := 101; end;
            function GetTaskType: Integer; begin Result := 11; end;
            function GetTaskTitle: string; begin Result := '重复任务'; end;
            begin end.
            """, gbk);
        File.WriteAllText(Path.Combine(root, "PsTaskList", "TaskTwo.pas"), """
            program TaskTwo;
            function GetTaskID: Integer; begin Result := 202; end;
            function GetTaskType: Integer; begin Result := 20; end;
            function GetTaskTitle: string; begin Result := '图片任务'; end;
            function GetTaskPicId: Integer; begin Result := 55; end;
            function GetTaskState: Integer; begin Result := 2; end;
            function GetTaskDetail: string; begin Result := 'detail'; end;
            function GetTaskProgress: string;
            begin
              raise Exception.Create('task failure');
            end;
            function DoTaskCommand(const Value: string): Boolean; begin Result := false; end;
            begin end.
            """, gbk);
        File.WriteAllText(Path.Combine(root, "PsTaskList", "PsTaskConfig.txt"),
            "; ignored\r\nTaskOne\r\nTaskDuplicate\r\nTaskTwo\r\n", gbk);
        File.WriteAllText(Path.Combine(root, "MonScript", "测试怪.pas"), """
            program MonsterRuntime;
            var Initialized: Integer;
            var Calls: Integer;
            var Spawn: string;

            procedure OnInitialize;
            begin
              Inc(Initialized);
              Spawn := This_Animal.Name + '|' + This_Animal.MapDesc + '|' +
                IntToStr(This_Animal.My_X) + ',' + IntToStr(This_Animal.My_Y);
            end;

            procedure AfterScatterItems(Keys, Values: TStringArray);
            var Second: string;
            begin
              Inc(Calls);
              Second := '';
              if GetArrayLength(Keys) > 1 then
                Second := Keys[1] + ':' + Values[1];
              This_Player.CallOutParam := Spawn + '|' + Keys[0] + ':' + Values[0] +
                '|' + Second + '|' + IntToStr(Initialized) + ':' + IntToStr(Calls);
            end;

            begin end.
            """, gbk);
        File.WriteAllText(Path.Combine(root, "MonScript", "未配置怪.pas"),
            "program Unconfigured; begin end.", gbk);
        File.WriteAllText(Path.Combine(root, "monScript.txt"),
            "; ignored\r\n测试怪\r\n测试怪\r\n缺失怪\r\n", gbk);

        var host = Activator.CreateInstance(hostType, new object[] { root })!;
        scriptHostField.SetValue(null, host);
        var preprocessIncludes = hostType.GetMethod("PreprocessIncludes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        File.WriteAllText(Path.Combine(root, "commented-include.pas"), "const IncludedValue = 9;", gbk);
        var expandedInclude = (string)preprocessIncludes.Invoke(host, new object[]
        {
            "{$I commented-include.pas} // trailing comment",
            root,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        })!;
        Assert(expandedInclude.Contains("IncludedValue = 9", StringComparison.Ordinal),
            "Pascal include followed by a line comment was not expanded");
        try
        {
            preprocessIncludes.Invoke(host, new object[]
            {
                "{$I missing-required-file.pas}\r\nprogram MissingInclude; begin end.",
                root,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            });
            throw new InvalidOperationException("Pascal host silently accepted a missing include");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is FileNotFoundException)
        {
        }
        var npc = RuntimeHelpers.GetUninitializedObject(normNpcType);
        normNpcType.GetField("ObjectId")!.SetValue(npc, 91001);
        normNpcType.GetField("m_sCharName")!.SetValue(npc, "计时NPC");
        normNpcType.GetField("m_sMapName")!.SetValue(npc, "3");

        var callLabel = hostType.GetMethod("CallLabel")!;
        callLabel.Invoke(host, new object?[] { stateScriptPath, "@Bump", null, npc });
        callLabel.Invoke(host, new object?[] { stateScriptPath, "@Bump", null, npc });
        var readValue = callLabel.Invoke(host, new object?[] { stateScriptPath, "@Read", null, npc })!;
        Assert((int)pasValueType.GetMethod("AsInt")!.Invoke(readValue, null)! == 7,
            "NPC interpreter did not retain OnInitialize globals between labels");
        var defineValue = callLabel.Invoke(host,
            new object?[] { stateScriptPath, "@ReadDefine", null, npc })!;
        Assert((int)pasValueType.GetMethod("AsInt")!.Invoke(defineValue, null)! == 77,
            "Compiler.inc enabled symbols were not applied to Pascal conditional compilation");
        callLabel.Invoke(host, new object?[] { stateScriptPath, "@SetNpcPrices", null, npc });
        var npcPriceValue = callLabel.Invoke(host,
            new object?[] { stateScriptPath, "@ReadNpcPrices", null, npc })!;
        Assert((int)pasValueType.GetMethod("AsInt")!.Invoke(npcPriceValue, null)! == 410 &&
               (int)normNpcType.GetField("m_nPasRepDoorGold")!.GetValue(npc)! == 101 &&
               (int)normNpcType.GetField("m_nPasHireArcherGold")!.GetValue(npc)! == 104,
            "Pascal writable NPC price properties did not retain their native per-NPC values");

        var api = hostType.GetProperty("Api")!.GetValue(host)!;
        Assert(apiType.GetProperty("CurrentNpc")!.GetValue(api) == null,
            "Pascal invocation context leaked after a label call");
        Assert(apiType.GetMethod("CallDbMethod", new[]
        {
            typeof(string), listType, pasValueType.MakeByRefType()
        }) != null, "This_DB method bridge is missing");
        Assert(apiType.GetMethod("GetDbProperty", new[]
        {
            typeof(string), pasValueType.MakeByRefType()
        }) != null, "This_DB property bridge is missing");
        var dbBridgeType = gameSvr.GetType("GameSvr.PasEngine.PasDbBridge", throwOnError: true)!;
        Assert(typeof(IDisposable).IsAssignableFrom(dbBridgeType), "This_DB connection scope is not disposable");

        var player = RuntimeHelpers.GetUninitializedObject(playObjectType);
        playObjectType.GetField("ObjectId")!.SetValue(player, 91002);
        playObjectType.GetField("m_sCharName")!.SetValue(player, "alias-player");
        playObjectType.GetField("m_ScriptVVars")!.SetValue(player, new Dictionary<int, int>());
        playObjectType.GetField("m_ScriptSVars")!.SetValue(player, new Dictionary<int, int>());
        var itemListField = playObjectType.GetField("m_ItemList")!;
        var itemListElementType = itemListField.FieldType.GetGenericArguments()[0];
        itemListField.SetValue(player,
            Activator.CreateInstance(typeof(List<>).MakeGenericType(itemListElementType)));
        playObjectType.GetField("m_boOffLineFlag")!.SetValue(player, true);
        playObjectType.GetField("m_nStorageSpaceCount")!.SetValue(player, 48);
        ((Dictionary<int, int>)playObjectType.GetField("m_ScriptVVars")!.GetValue(player)!)[90001] = 10;
        Assert((int)hostType.GetMethod("LoadTaskScripts")!.Invoke(host, null)! == 2,
            "PsTaskConfig order, comments, or duplicate task-id rejection differs from native");
        var taskMetadata = ((IEnumerable)hostType.GetMethod("GetTaskScripts")!
            .Invoke(host, null)!).Cast<object>().ToList();
        Assert(taskMetadata.Count == 2 &&
               GetProperty<int>(taskMetadata[0], "TaskId") == 101 &&
               GetProperty<int>(taskMetadata[0], "TaskType") == 10 &&
               GetProperty<string>(taskMetadata[0], "Title") == "首个任务" &&
               GetProperty<int>(taskMetadata[0], "PicId") == 0 &&
               GetProperty<int>(taskMetadata[1], "TaskId") == 202 &&
               GetProperty<int>(taskMetadata[1], "TaskType") == 20 &&
               GetProperty<int>(taskMetadata[1], "PicId") == 55,
            "PsTaskList metadata was not retained in stable config order");

        var secondTaskPlayer = RuntimeHelpers.GetUninitializedObject(playObjectType);
        playObjectType.GetField("ObjectId")!.SetValue(secondTaskPlayer, 91006);
        playObjectType.GetField("m_sCharName")!.SetValue(secondTaskPlayer, "second-player");
        playObjectType.GetField("m_ScriptVVars")!.SetValue(secondTaskPlayer,
            new Dictionary<int, int> { [90001] = 20 });
        playObjectType.GetField("m_ScriptSVars")!.SetValue(secondTaskPlayer, new Dictionary<int, int>());
        var taskStateMethod = hostType.GetMethod("TryGetTaskState")!;
        var firstTaskState = new object?[] { 101, player, null };
        var secondTaskState = new object?[] { 101, secondTaskPlayer, null };
        Assert((bool)taskStateMethod.Invoke(host, firstTaskState)! && (int)firstTaskState[2]! == 11 &&
               (bool)taskStateMethod.Invoke(host, secondTaskState)! && (int)secondTaskState[2]! == 22,
            "persistent task interpreter or per-call This_Player context was lost");
        var taskDetailArgs = new object?[] { 101, player, null };
        Assert((bool)hostType.GetMethod("TryGetTaskDetail")!.Invoke(host, taskDetailArgs)! &&
               (string)taskDetailArgs[2]! == "alias-player|2",
            "task detail did not reuse the persistent task interpreter");
        var taskCommandArgs = new object?[] { 101, player, "alias-player", null };
        Assert((bool)hostType.GetMethod("TryDoTaskCommand")!.Invoke(host, taskCommandArgs)! &&
               (bool)taskCommandArgs[3]!,
            "DoTaskCommand did not receive its exact value or player context");
        var failingProgressArgs = new object?[] { 202, player, null };
        Assert(!(bool)hostType.GetMethod("TryGetTaskProgress")!.Invoke(host, failingProgressArgs)!,
            "task script exception was swallowed as a successful result");
        Assert(apiType.GetProperty("CurrentPlayer")!.GetValue(hostType.GetProperty("Api")!.GetValue(host)) == null,
            "task script invocation leaked This_Player context");

        Assert((int)hostType.GetMethod("LoadMonsterScripts")!.Invoke(host, null)! == 1,
            "monScript.txt was not the exclusive, duplicate-safe monster script source");
        var monsterStateType = hostType.GetNestedType("MonsterScriptState", BindingFlags.NonPublic)!;
        Assert(monsterStateType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .All(field => !baseObjectType.IsAssignableFrom(field.FieldType)),
            "monster script state retained a live actor reference");

        var mapEnvironment = RuntimeHelpers.GetUninitializedObject(environmentType);
        environmentType.GetField("sMapDesc")!.SetValue(mapEnvironment, "测试地图");
        object NewAnimal(int objectId, string name, short x, short y)
        {
            var animal = RuntimeHelpers.GetUninitializedObject(baseObjectType);
            baseObjectType.GetField("ObjectId")!.SetValue(animal, objectId);
            baseObjectType.GetField("m_sCharName")!.SetValue(animal, name);
            baseObjectType.GetField("m_sMapName")!.SetValue(animal, "3");
            baseObjectType.GetField("m_nCurrX")!.SetValue(animal, x);
            baseObjectType.GetField("m_nCurrY")!.SetValue(animal, y);
            baseObjectType.GetField("m_PEnvir")!.SetValue(animal, mapEnvironment);
            return animal;
        }

        var firstAnimal = NewAnimal(91101, "测试怪", 11, 22);
        var secondAnimal = NewAnimal(91102, "测试怪", 33, 44);
        var unconfiguredAnimal = NewAnimal(91103, "未配置怪", 55, 66);
        var initializeMonsterScript = hostType.GetMethod("TryInitializeMonsterScript")!;
        var callAfterScatterItems = hostType.GetMethod("TryCallAfterScatterItems")!;
        var clearMonsterScriptState = hostType.GetMethod("ClearMonsterScriptState")!;
        Assert((bool)initializeMonsterScript.Invoke(host, new[] { firstAnimal })! &&
               (bool)initializeMonsterScript.Invoke(host, new[] { secondAnimal })! &&
               !(bool)initializeMonsterScript.Invoke(host, new[] { unconfiguredAnimal })!,
            "monster scripts were not bound strictly by configured monster name");
        Assert(apiType.GetProperty("CurrentAnimal")!.GetValue(api) == null,
            "monster OnInitialize leaked This_Animal context");

        var duplicateDrops = new List<KeyValuePair<string, string>>
        {
            new("金币", "100"),
            new("金币", "200")
        };
        Assert((bool)callAfterScatterItems.Invoke(host,
                   new object[] { firstAnimal, player, duplicateDrops })! &&
               (string)playObjectType.GetField("m_sCallOutParam")!.GetValue(player)! ==
               "测试怪|测试地图|11,22|金币:100|金币:200|1:1",
            "AfterScatterItems lost ordered duplicate drops or This_Animal properties");

        var oneDrop = new List<KeyValuePair<string, string>> { new("金条", "2") };
        Assert((bool)callAfterScatterItems.Invoke(host,
                   new object[] { secondAnimal, player, oneDrop })! &&
               (string)playObjectType.GetField("m_sCallOutParam")!.GetValue(player)! ==
               "测试怪|测试地图|33,44|金条:2||1:1",
            "monster script interpreters shared state or actor context across object IDs");
        Assert((bool)callAfterScatterItems.Invoke(host,
                   new object[] { firstAnimal, player, oneDrop })! &&
               (string)playObjectType.GetField("m_sCallOutParam")!.GetValue(player)! ==
               "测试怪|测试地图|11,22|金条:2||1:2",
            "monster script interpreter did not retain per-object callback state");

        clearMonsterScriptState.Invoke(host, new object[] { 91101 });
        Assert(!(bool)callAfterScatterItems.Invoke(host,
                   new object[] { firstAnimal, player, oneDrop })! &&
               (bool)initializeMonsterScript.Invoke(host, new[] { firstAnimal })! &&
               (bool)callAfterScatterItems.Invoke(host,
                   new object[] { firstAnimal, player, oneDrop })! &&
               (string)playObjectType.GetField("m_sCallOutParam")!.GetValue(player)! ==
               "测试怪|测试地图|11,22|金条:2||1:1",
            "monster script state survived explicit actor cleanup");
        Assert(apiType.GetProperty("CurrentAnimal")!.GetValue(api) == null &&
               apiType.GetProperty("CurrentPlayer")!.GetValue(api) == null,
            "AfterScatterItems leaked monster or player execution context");

        var timerNpc = RuntimeHelpers.GetUninitializedObject(normNpcType);
        normNpcType.GetField("ObjectId")!.SetValue(timerNpc, 91003);
        normNpcType.GetField("m_sCharName")!.SetValue(timerNpc, "延迟NPC");
        normNpcType.GetField("m_sMapName")!.SetValue(timerNpc, "3");
        var findItemScript = hostType.GetMethod("FindItemScriptFile")!;
        var itemScriptPath = (string)findItemScript.Invoke(host, new object[] { "TestItem" })!;
        Assert(itemScriptPath == Path.Combine(root, "PsItemScript", "TestItem.pas") &&
               findItemScript.Invoke(host, new object[] { "../TestItem" }) == null &&
               findItemScript.Invoke(host, new object[] { "CommonOnly" }) == null,
            "item script lookup escaped PsItemScript or used the generic script search order");
        var userItemType = playObjectType.GetField("m_ItemList")!.FieldType.GetGenericArguments()[0];
        var userItem = Activator.CreateInstance(userItemType)!;
        userItemType.GetField("MakeIndex")!.SetValue(userItem, 54321);
        userItemType.GetField("ClientItemID")!.SetValue(userItem, 12345);
        userItemType.GetField("wIndex")!.SetValue(userItem, (ushort)1);
        userItemType.GetField("btValue")!.SetValue(userItem, new byte[] { 1, 0, 0, 0, 0 });
        var itemCallArgs = new object?[] { itemScriptPath, "UseItem", player, userItem, null };
        Assert((bool)hostType.GetMethod("TryCallItemProcedure")!.Invoke(host, itemCallArgs)! &&
               (bool)pasValueType.GetMethod("AsBool")!.Invoke(itemCallArgs[4], null)! &&
               ((byte[])userItemType.GetField("btValue")!.GetValue(userItem)!)[0] == 3,
            "PsItemScript execution lost This_Item identity, Result, or AddPa write-back");
        Assert(apiType.GetProperty("CurrentItem")!.GetValue(hostType.GetProperty("Api")!.GetValue(host)) == null,
            "This_Item invocation context leaked after item script execution");
        var npcItemArgs = Array.CreateInstance(pasValueType, 1);
        npcItemArgs.SetValue(fromInt.Invoke(null, new object[] { 4 }), 0);
        var npcItemCallArgs = new object?[] { npc, "CommitItem", player, userItem, null, npcItemArgs };
        Assert((bool)hostType.GetMethod("TryCallNpcItemProcedure")!.Invoke(host, npcItemCallArgs)! &&
               ((byte[])userItemType.GetField("btValue")!.GetValue(userItem)!)[0] == 7,
            "NPC CommitItem did not receive the exact This_Item object or AType");
        var postCommitCounter = callLabel.Invoke(host,
            new object?[] { stateScriptPath, "@Read", player, npc })!;
        Assert((int)pasValueType.GetMethod("AsInt")!.Invoke(postCommitCounter, null)! == 11,
            "NPC CommitItem did not use the persistent NPC interpreter state");
        Assert(apiType.GetProperty("CurrentItem")!.GetValue(hostType.GetProperty("Api")!.GetValue(host)) == null,
            "NPC CommitItem leaked This_Item after execution");
        var stateProgram = hostType.GetMethod("GetOrLoadProgram", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object[] { stateScriptPath })!;
        var chainedProcedure = ((IEnumerable)GetProperty<object>(stateProgram, "Procedures")).Cast<object>()
            .Single(proc => GetProperty<string>(proc, "Name") == "_ParseChainedMethod");
        var chainedStatement = ((IEnumerable)GetProperty<object>(
                GetProperty<object>(chainedProcedure, "Body"), "Statements")).Cast<object>().Single();
        Assert(chainedStatement.GetType().Name == "PasMethodCallExpr" &&
               GetProperty<object>(chainedStatement, "Target") != null &&
               GetProperty<string>(chainedStatement, "MethodName") == "SysMsg",
            "Pascal chained object method call was truncated after the first call");
        var aliasInterpreter = interpreterType.GetConstructor(new[] { programType, apiType })!
            .Invoke(new[] { stateProgram, hostType.GetProperty("Api")!.GetValue(host)! });
        object aliasValue;
        using (var aliasContext = (IDisposable)apiType.GetMethod("PushContext")!.Invoke(
                   hostType.GetProperty("Api")!.GetValue(host), new object?[] { player, npc, false, null })!)
        {
            aliasValue = interpreterType.GetMethod("ExecuteProcedure")!
                .Invoke(aliasInterpreter, new object?[] { "_ReadPlayerAlias", null })!;
        }
        Assert((string)pasValueType.GetMethod("AsString")!.Invoke(aliasValue, null)! == "alias-player",
            "Pascal TPlayer parameter lost its object identity");
        object chainedValue;
        using (var chainedContext = (IDisposable)apiType.GetMethod("PushContext")!.Invoke(
                   hostType.GetProperty("Api")!.GetValue(host), new object?[] { player, npc, false, null })!)
        {
            interpreterType.GetMethod("ExecuteProcedure")!
                .Invoke(aliasInterpreter, new object?[] { "_WriteChainedPlayer", null });
            chainedValue = interpreterType.GetMethod("ExecuteProcedure")!
                .Invoke(aliasInterpreter, new object?[] { "_ReadChainedPlayer", null })!;
        }
        Assert((string)pasValueType.GetMethod("AsString")!.Invoke(chainedValue, null)! ==
               "alias-player|chained-player",
            "Pascal chained object property read or assignment lost its target player");
        var setPlayerProperty = apiType.GetMethod("SetPlayerProperty")!;
        var getPlayerProperty = apiType.GetMethod("GetPlayerProperty")!;
        var fromString = pasValueType.GetMethod("FromString", BindingFlags.Static | BindingFlags.Public)!;

        using (var unsupportedContext = (IDisposable)apiType.GetMethod("PushContext")!.Invoke(api,
                   new object?[] { player, npc, false, null })!)
        {
            var unknownProperty = new object?[] { "missingnativeproperty", null };
            var dynamicRoomProperty = new object?[] { "dynroomidx", null };
            Assert(!(bool)getPlayerProperty.Invoke(api, unknownProperty)!,
                "unknown player property was acknowledged with an invented default");
            Assert((bool)getPlayerProperty.Invoke(api, dynamicRoomProperty)! &&
                   (int)pasValueType.GetMethod("AsInt")!.Invoke(dynamicRoomProperty[1], null)! == -1,
                "non-dynamic map did not expose the native DynRoomIdx=-1 state");

            var noArgs = (IList)Activator.CreateInstance(listType)!;
            var tmpActivePoint = new object?[] { "gettmpactivepoint", noArgs, null };
            var usExp = new object?[] { "chkifcanaddusexp", noArgs, null };
            Assert((bool)apiType.GetMethod("CallPlayerFunc")!.Invoke(api, tmpActivePoint)! &&
                   (int)pasValueType.GetMethod("AsInt")!.Invoke(tmpActivePoint[2], null)! == 0,
                "GetTmpActivePoint without activity configuration did not return native zero");
            Assert((bool)apiType.GetMethod("CallPlayerFunc")!.Invoke(api, usExp)! &&
                   (int)pasValueType.GetMethod("AsInt")!.Invoke(usExp[2], null)! == -1,
                "ChkIfCanAddUSExp did not return native -1 without a summoned hero");

            var addUsExpArgs = (IList)Activator.CreateInstance(listType)!;
            addUsExpArgs.Add(fromInt.Invoke(null, new object[] { 1 }));
            addUsExpArgs.Add(fromInt.Invoke(null, new object[] { 5000 }));
            addUsExpArgs.Add(fromInt.Invoke(null, new object[] { 50 }));
            var addUsExp = new object?[] { "addusexp", addUsExpArgs, null };
            Assert((bool)apiType.GetMethod("CallPlayerFunc")!.Invoke(api, addUsExp)! &&
                   (int)pasValueType.GetMethod("AsInt")!.Invoke(addUsExp[2], null)! == -1,
                "AddUSExp did not return native -1 without a summoned hero");

            var questInfoArgs = (IList)Activator.CreateInstance(listType)!;
            questInfoArgs.Add(fromString.Invoke(null, new object[] { "extension-state" }));
            var showNameBeforeQuestInfo = (string)player.GetType().GetMethod("GetShowName")!
                .Invoke(player, null)!;
            Assert((bool)apiType.GetMethod("CallPlayerMethod")!.Invoke(api,
                       new object[] { "questinfo", questInfoArgs })!,
                "native QuestInfo buffer call was rejected");
            Assert((string)player.GetType().GetMethod("GetShowName")!.Invoke(player, null)! ==
                   showNameBeforeQuestInfo,
                "native QuestInfo buffer leaked into the client name without the eye title hook");

            var fakePluginArgs = (IList)Activator.CreateInstance(listType)!;
            fakePluginArgs.Add(fromInt.Invoke(null, new object[] { 1 }));
            fakePluginArgs.Add(fromInt.Invoke(null, new object[] { 2 }));
            Assert(!(bool)apiType.GetMethod("CallPlayerMethod")!.Invoke(api,
                       new object[] { "givepositivevvalue", fakePluginArgs })!,
                "external GivePositiveVValue still writes an invented V-variable slot");

            var pluginDropArgs = (IList)Activator.CreateInstance(listType)!;
            pluginDropArgs.Add(fromString.Invoke(null, new object[] { "测试物品" }));
            pluginDropArgs.Add(fromInt.Invoke(null, new object[] { 100 }));
            var pluginDropCall = new object?[] { "chgmonitempercent", pluginDropArgs, null };
            var pluginSpawnCall = new object?[] { "npc_creatmons", noArgs, null };
            Assert(!(bool)apiType.GetMethod("CallStandaloneFunction")!.Invoke(api, pluginDropCall)! &&
                   !(bool)apiType.GetMethod("CallStandaloneFunction")!.Invoke(api, pluginSpawnCall)!,
                "external monster plugin functions were exposed as native PAS functions");

            var ybDealArgs = (IList)Activator.CreateInstance(listType)!;
            ybDealArgs.Add(fromObject.Invoke(null, new[] { player }));
            ybDealArgs.Add(fromInt.Invoke(null, new object[] { 1 }));
            var ybDealCall = new object?[] { "ybdealdialogshowmode", ybDealArgs, null };
            Assert(!(bool)apiType.GetMethod("CallNpcMethod")!.Invoke(api, ybDealCall)!,
                "YBDealDialogShowMode still routes native consignment to the shop protocol");

            var deleteAllCall = new object?[] { "delbagitemofall", noArgs, null };
            Assert((bool)apiType.GetMethod("CallStandaloneFunction")!.Invoke(api, deleteAllCall)!,
                "bare DelBagItemOfAll did not route to the native player operation");
        }

        var playerMailArgs = (IList)Activator.CreateInstance(listType)!;
        playerMailArgs.Add(fromString.Invoke(null, new object[] { "title" }));
        playerMailArgs.Add(fromString.Invoke(null, new object[] { "context" }));
        playerMailArgs.Add(fromInt.Invoke(null, new object[] { 1 }));
        playerMailArgs.Add(fromInt.Invoke(null, new object[] { 2 }));
        playerMailArgs.Add(fromInt.Invoke(null, new object[] { 3 }));
        playerMailArgs.Add(fromString.Invoke(null, new object[] { "item|1" }));
        playerMailArgs.Add(fromString.Invoke(null, new object[] { "" }));
        using (var mailContext = (IDisposable)apiType.GetMethod("PushContext")!.Invoke(api,
                   new object?[] { player, npc, false, null })!)
        {
            Assert((bool)apiType.GetMethod("CallPlayerMethod")!.Invoke(api,
                        new object[] { "newfullmailex", playerMailArgs })!,
                "TPlayer.NewFullMailEx procedure was treated as failed when delivery was unavailable");
            playerMailArgs.Add(fromString.Invoke(null, new object[] { "extra" }));
            Assert(!(bool)apiType.GetMethod("CallPlayerMethod")!.Invoke(api,
                       new object[] { "newfullmailex", playerMailArgs })!,
                "TPlayer.NewFullMailEx accepted the global eight-argument signature");
        }

        var globalMailArgs = (IList)Activator.CreateInstance(listType)!;
        globalMailArgs.Add(fromString.Invoke(null, new object[] { "receiver" }));
        for (var mailIndex = 0; mailIndex < 7; mailIndex++)
            globalMailArgs.Add(playerMailArgs[mailIndex]);
        var globalMailCall = new object?[] { "newfullmailex", globalMailArgs, null };
        Assert((bool)apiType.GetMethod("CallStandaloneFunction")!.Invoke(api, globalMailCall)!,
            "global NewFullMailEx procedure was treated as failed when delivery was unavailable");
        globalMailArgs.RemoveAt(globalMailArgs.Count - 1);
        globalMailCall = new object?[] { "newfullmailex", globalMailArgs, null };
        Assert(!(bool)apiType.GetMethod("CallStandaloneFunction")!.Invoke(api, globalMailCall)!,
            "global NewFullMailEx accepted the player-object seven-argument signature");

        var storageArgs = (IList)Activator.CreateInstance(listType)!;
        storageArgs.Add(fromInt.Invoke(null, new object[] { 200 }));
        using (var storageContext = (IDisposable)apiType.GetMethod("PushContext")!.Invoke(api,
                   new object?[] { player, npc, false, null })!)
        {
            var expandCall = new object?[] { "expandstoragespace", storageArgs, null };
            Assert((bool)apiType.GetMethod("CallPlayerFunc")!.Invoke(api, expandCall)! &&
                   (int)pasValueType.GetMethod("AsInt")!.Invoke(expandCall[2], null)! == 144 &&
                   (int)playObjectType.GetField("m_nStorageSpaceCount")!.GetValue(player)! == 192,
                "ExpandStorageSpace did not clamp to 192 or return the actual added count");
            var storagePacket = playObjectType.GetField("m_DefMsg")!.GetValue(player)!;
            Assert((ushort)storagePacket.GetType().GetField("Ident")!.GetValue(storagePacket)! == 718 &&
                   (ushort)storagePacket.GetType().GetField("Series")!.GetValue(storagePacket)! == 192,
                "ExpandStorageSpace did not send native message 718 with capacity in Series");

            var countCall = new object?[]
            {
                "getstoragespacecount", Activator.CreateInstance(listType)!, null
            };
            Assert((bool)apiType.GetMethod("CallPlayerFunc")!.Invoke(api, countCall)! &&
                   (int)pasValueType.GetMethod("AsInt")!.Invoke(countCall[2], null)! == 192,
                "GetStorageSpaceCount did not return the native capacity field");

            storageArgs[0] = fromInt.Invoke(null, new object[] { -1 });
            expandCall = new object?[] { "expandstoragespace", storageArgs, null };
            Assert((bool)apiType.GetMethod("CallPlayerFunc")!.Invoke(api, expandCall)! &&
                   (int)pasValueType.GetMethod("AsInt")!.Invoke(expandCall[2], null)! == -1 &&
                   (int)playObjectType.GetField("m_nStorageSpaceCount")!.GetValue(player)! == 192,
                "ExpandStorageSpace negative-count behavior differs from the native callback");
        }

        var context = (IDisposable)apiType.GetMethod("PushContext")!.Invoke(api,
            new object?[] { player, npc, false, null })!;
        try
        {
            setPlayerProperty.Invoke(api, new[] { "calloutparam", fromString.Invoke(null, new object[] { "延迟参数" })! });
            var callArgs = (IList)Activator.CreateInstance(listType)!;
            callArgs.Add(fromObject.Invoke(null, new[] { timerNpc }));
            callArgs.Add(fromInt.Invoke(null, new object[] { 2 }));
            callArgs.Add(fromString.Invoke(null, new object[] { "ScheduledProc" }));
            Assert((bool)apiType.GetMethod("CallPlayerMethod")!.Invoke(api,
                new object[] { "calloutex", callArgs })!, "CallOutEx dispatch was rejected");
        }
        finally
        {
            context.Dispose();
        }

        var propertyArgs = new object?[] { "calloutparam", null };
        using (var propertyContext = (IDisposable)apiType.GetMethod("PushContext")!.Invoke(api,
                   new object?[] { player, npc, false, null })!)
        {
            Assert((bool)getPlayerProperty.Invoke(api, propertyArgs)!, "CallOutParam property read failed");
        }
        Assert((string)pasValueType.GetMethod("AsString")!.Invoke(propertyArgs[1], null)! == "延迟参数",
            "CallOutParam was not retained on the player object");

        var deferred = (IList)hostType.GetField("_deferredCalls", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host)!;
        Assert(deferred.Count == 1, "CallOutEx did not enqueue exactly one callback");
        var deferredCall = deferred[0]!;
        Assert((string)deferredCall.GetType().GetField("ProcName")!.GetValue(deferredCall)! == "ScheduledProc",
            "CallOutEx read the procedure name from the wrong argument");
        Assert(deferredCall.GetType().GetField("Player") == null &&
               deferredCall.GetType().GetField("Npc") == null &&
               deferredCall.GetType().GetField("Args") == null &&
               (int)deferredCall.GetType().GetField("NpcId")!.GetValue(deferredCall)! == 91003,
            "deferred callback ignored PsNpc or retained strong actor references");

        void DispatchTimer(string method, string procedure, int seconds)
        {
            var timerArgs = (IList)Activator.CreateInstance(listType)!;
            timerArgs.Add(fromObject.Invoke(null, new[] { timerNpc }));
            timerArgs.Add(fromInt.Invoke(null, new object[] { seconds }));
            timerArgs.Add(fromString.Invoke(null, new object[] { procedure }));
            using var timerContext = (IDisposable)apiType.GetMethod("PushContext")!.Invoke(api,
                new object?[] { player, npc, false, null })!;
            Assert((bool)apiType.GetMethod("CallPlayerMethod")!.Invoke(api,
                new object[] { method, timerArgs })!, $"{method} dispatch was rejected");
        }

        DispatchTimer("calloutex", "scheduledproc", 3);
        Assert(deferred.Count == 1,
            "CallOutEx did not reset its case-insensitive matching procedure slot");
        DispatchTimer("calloutex", "OtherProc", 3);
        Assert(deferred.Count == 2,
            "CallOutEx did not retain a distinct procedure slot");
        DispatchTimer("callout", "SingleOne", 3);
        DispatchTimer("callout", "SingleTwo", 4);
        var singleCalls = deferred.Cast<object>().Where(entry =>
            (bool)entry.GetType().GetField("IsSingleSlot")!.GetValue(entry)!).ToList();
        Assert(singleCalls.Count == 1 &&
               (string)singleCalls[0].GetType().GetField("ProcName")!.GetValue(singleCalls[0])! == "SingleTwo",
            "CallOut did not overwrite the player's native single timer slot");
        var countBeforeZeroDelay = deferred.Count;
        DispatchTimer("callout", "IgnoredZeroDelay", 0);
        Assert(deferred.Count == countBeforeZeroDelay,
            "CallOut accepted a non-positive delay rejected by the native callback");

        var member = RuntimeHelpers.GetUninitializedObject(playObjectType);
        playObjectType.GetField("ObjectId")!.SetValue(member, 91004);
        var memberList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(playObjectType))!;
        memberList.Add(player);
        memberList.Add(member);
        playObjectType.GetField("m_GroupMembers")!.SetValue(player, memberList);
        playObjectType.GetField("m_GroupOwner")!.SetValue(player, player);
        playObjectType.GetField("m_GroupOwner")!.SetValue(member, player);
        var groupArgs = (IList)Activator.CreateInstance(listType)!;
        groupArgs.Add(fromObject.Invoke(null, new[] { timerNpc }));
        groupArgs.Add(fromString.Invoke(null, new object[] { "GroupProc" }));
        groupArgs.Add(fromString.Invoke(null, new object[] { "room-2" }));
        groupArgs.Add(fromInt.Invoke(null, new object[] { 5 }));
        using (var groupContext = (IDisposable)apiType.GetMethod("PushContext")!.Invoke(api,
                   new object?[] { player, npc, false, null })!)
        {
            Assert((bool)apiType.GetMethod("CallPlayerMethod")!.Invoke(api,
                new object[] { "groupcallout", groupArgs })!, "GroupCallOut dispatch was rejected");
        }
        Assert((string)playObjectType.GetField("m_sCallOutParam")!.GetValue(player)! == "room-2" &&
               (string)playObjectType.GetField("m_sCallOutParam")!.GetValue(member)! == "room-2",
            "GroupCallOut did not persist CallOutParam on every group member");
        var groupSlots = deferred.Cast<object>().Where(entry =>
            (bool)entry.GetType().GetField("IsSingleSlot")!.GetValue(entry)! &&
            (string)entry.GetType().GetField("ProcName")!.GetValue(entry)! == "GroupProc").ToList();
        Assert(groupSlots.Count == 2 && groupSlots.All(entry =>
                   (int)entry.GetType().GetField("NpcId")!.GetValue(entry)! == 91003),
            "GroupCallOut did not use one native timer slot per member or lost its NPC argument");

        var cancelDeferred = hostType.GetMethod("CancelDeferredCallsForObject")!;
        Assert((int)cancelDeferred.Invoke(host, new object[] { 91004 })! == 1 &&
               !deferred.Cast<object>().Any(entry =>
                   (int)entry.GetType().GetField("PlayerId")!.GetValue(entry)! == 91004),
            "destroying a player retained its native-owned CallOut timer");
        var callsReferencingNpc = deferred.Count;
        Assert((int)cancelDeferred.Invoke(host, new object[] { 91003 })! == callsReferencingNpc &&
               deferred.Count == 0,
            "destroying an NPC did not cancel deferred calls that reference it");

        hostType.GetMethod("LoadNpcScriptMap")!.Invoke(host, null);
        Assert((int)hostType.GetProperty("TimedNpcScriptCount")!.GetValue(host)! == 1,
            "PsNpcScript AutoTime did not use the exact ninth field");
        hostType.GetMethod("LoadMapQuestMap")!.Invoke(host, null);
        var questEntries = ((IEnumerable)hostType.GetMethod("GetMapQuestScripts")!
            .Invoke(host, new object[] { "3" })!).Cast<object>().ToList();
        Assert(questEntries.Count == 3, "ten-column PsMapQuest records were not mapped");
        Assert(questEntries.Any(entry =>
                Path.GetFullPath((string)entry.GetType().GetField("Item2")!.GetValue(entry)!) ==
                Path.GetFullPath(mapQuestScriptPath)),
            "PsMapQuest did not resolve field 8 as the script name");
        foreach (var lifecycleName in new[] { "OnEnter", "OnLeave", "OnDie", "OnReLive" })
        {
            Assert(hostType.GetMethod(lifecycleName, BindingFlags.Instance | BindingFlags.Public) == null,
                $"PsMapQuest retained the non-native {lifecycleName} lifecycle fallback");
        }

        var processMapQuestKill = hostType.GetMethod("ProcessMapQuestKill")!;
        int ProcessKill(object targetPlayer, string monster, bool grouped) =>
            (int)processMapQuestKill.Invoke(host, new object[] { "3", targetPlayer, monster, grouped })!;
        var playerV = (Dictionary<int, int>)playObjectType.GetField("m_ScriptVVars")!.GetValue(player)!;
        var playerS = (Dictionary<int, int>)playObjectType.GetField("m_ScriptSVars")!.GetValue(player)!;

        Assert(ProcessKill(player, "白野猪", false) == 2 &&
               playerV.GetValueOrDefault(16002) == 1 &&
               playerV.GetValueOrDefault(16003) == 1 &&
               playerV.GetValueOrDefault(16004) == 0 &&
               playerS.GetValueOrDefault(16002) == 0,
            "PsMapQuest killer dispatch ignored normal/GROUP matching or triggered an item record");
        Assert(ProcessKill(player, "白野猪", false) == 2 &&
               playerV.GetValueOrDefault(16002) == 2 &&
               playerV.GetValueOrDefault(16003) == 2 &&
               playerS.GetValueOrDefault(16002) == 2,
            "persistent PsMapQuest state or ExecuteMain Exit reset was lost");
        Assert(ProcessKill(player, "白野猪", false) == 1 &&
               playerV.GetValueOrDefault(16002) == 2 &&
               playerV.GetValueOrDefault(16003) == 3,
            "PsMapQuest did not enforce the native V variable upper bound");
        Assert(ProcessKill(player, "稻草人", false) == 0,
            "PsMapQuest accepted a non-matching monster name");

        var groupMember = RuntimeHelpers.GetUninitializedObject(playObjectType);
        playObjectType.GetField("ObjectId")!.SetValue(groupMember, 91005);
        playObjectType.GetField("m_sCharName")!.SetValue(groupMember, "group-member");
        playObjectType.GetField("m_ScriptVVars")!.SetValue(groupMember, new Dictionary<int, int>());
        playObjectType.GetField("m_ScriptSVars")!.SetValue(groupMember, new Dictionary<int, int>());
        Assert(ProcessKill(groupMember, "白野猪", true) == 1,
            "grouped PsMapQuest dispatch did not restrict execution to GROUP records");
        var groupMemberV = (Dictionary<int, int>)playObjectType.GetField("m_ScriptVVars")!
            .GetValue(groupMember)!;
        Assert(groupMemberV.GetValueOrDefault(16002) == 0 &&
               groupMemberV.GetValueOrDefault(16003) == 1,
            "grouped PsMapQuest dispatch executed a non-GROUP record");

        var globalConfigType = gameSvr.GetType("GameSvr.Configs.GlobalConfig", throwOnError: true)!;
        Assert(globalConfigType.GetMethod("SaveConfig", BindingFlags.Instance | BindingFlags.Public) != null,
            "global G/A persistence has no batch save entry point");
        Assert(globalConfigType.BaseType!.GetMethod("SetCachedString",
                   BindingFlags.Instance | BindingFlags.NonPublic) != null,
            "global G/A persistence cannot batch cache values before one disk write");
        var serverBaseType = gameSvr.GetType("GameSvr.ServerBase", throwOnError: true)!;
        var saveInterval = serverBaseType.GetField("_globalSaveIntervalMs",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert((long)saveInterval.GetRawConstantValue()! == 600000L,
            "global G/A persistence interval does not match the native 600000ms cadence");
    }
    finally
    {
        scriptHostField.SetValue(null, previousScriptHost);
        Directory.Delete(root, recursive: true);
    }
}

static void TestNoInventedMarketPersistence(Assembly gameSvr, string gameSvrDirectory)
{
    var localDbType = gameSvr.GetType("GameSvr.LocalDB", throwOnError: true)!;
    foreach (var methodName in new[]
             {
                 "LoadGoodRecord", "SaveGoodRecord", "LoadGoodPriceRecord", "SaveGoodPriceRecord",
                 "LoadUpgradeWeaponRecord", "SaveUpgradeWeaponRecord", "LoadSellOffItemList", "SaveSellOffItemList"
             })
    {
        Assert(localDbType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
            $"non-native LocalDB persistence returned: {methodName}");
    }

    var goodsType = gameSvr.GetType("GameSvr.TGoods", throwOnError: true)!;
    Assert(!goodsType.IsValueType, "merchant refill state is a value type, so dwRefillTick updates are lost");
    var goods = Activator.CreateInstance(goodsType)!;
    goodsType.GetField("dwRefillTick")!.SetValue(goods, 123456);
    Assert((int)goodsType.GetField("dwRefillTick")!.GetValue(goods)! == 123456,
        "merchant refill tick did not persist on the goods object");

    var assemblyBytes = File.ReadAllBytes(Path.Combine(gameSvrDirectory, "GameSvr.dll"));
    foreach (var marker in new[]
             {
                 "Market_Saved", "Market_Prices", "UserData.dat", "TBL_",
                 "YBData.json", "YBShopScript.json"
             })
    {
        Assert(!BinaryContainsText(assemblyBytes, marker, ignoreCase: true),
            $"GameSvr still contains invented persistence marker: {marker}");
    }
}

static void TestMakeSlaveSignature(Assembly gameSvr)
{
    var apiType = gameSvr.GetType("GameSvr.PasEngine.PasApiBridge", throwOnError: true)!;
    var playerType = gameSvr.GetType("GameSvr.TPlayObject", throwOnError: true)!;
    var pasValueType = gameSvr.GetType("GameSvr.PasEngine.PasValue", throwOnError: true)!;
    var api = Activator.CreateInstance(apiType)!;
    apiType.GetProperty("CurrentPlayer")!.SetValue(api, RuntimeHelpers.GetUninitializedObject(playerType));

    var listType = typeof(List<>).MakeGenericType(pasValueType);
    var values = (IList)Activator.CreateInstance(listType)!;
    var fromInt = pasValueType.GetMethod("FromInt", BindingFlags.Static | BindingFlags.Public)!;
    for (var i = 0; i < 7; i++)
        values.Add(fromInt.Invoke(null, new object[] { 0 })!);

    var callArgs = new object?[] { "MakeSlave", values, null };
    var handled = (bool)apiType.GetMethod("CallPlayerFunc")!.Invoke(api, callArgs)!;
    Assert(!handled, "MakeSlave accepted a non-native seven-argument call");
}

static void TestUnsupportedPasApis(Assembly gameSvr)
{
    var apiType = gameSvr.GetType("GameSvr.PasEngine.PasApiBridge", throwOnError: true)!;
    var pasValueType = gameSvr.GetType("GameSvr.PasEngine.PasValue", throwOnError: true)!;
    var playerType = gameSvr.GetType("GameSvr.TPlayObject", throwOnError: true)!;
    var npcType = gameSvr.GetType("GameSvr.NormNpc", throwOnError: true)!;
    var api = Activator.CreateInstance(apiType)!;
    apiType.GetProperty("CurrentNpc")!.SetValue(api, RuntimeHelpers.GetUninitializedObject(npcType));

    var listType = typeof(List<>).MakeGenericType(pasValueType);
    var noArgs = Activator.CreateInstance(listType)!;
    foreach (var (methodName, apiName) in new[]
             {
                 ("CallNpcMethod", "CreateMapEvent"),
                 ("CallNpcMethod", "ClientReqGetBackLostItem"),
                 ("CallNpcFunc", "GetNormalCastleScoreRslt"),
                 ("CallStandaloneFunction", "MakeCattleCrazy")
             })
    {
        var callArgs = new object?[] { apiName, noArgs, null };
        Assert(!(bool)apiType.GetMethod(methodName)!.Invoke(api, callArgs)!,
            $"unsupported native PAS API still reported success: {apiName}");
    }

    var emptyCallbackArgs = new object?[] { "ClickComposeDress", noArgs, null };
    Assert((bool)apiType.GetMethod("CallNpcMethod")!.Invoke(api, emptyCallbackArgs)!,
        "native ClickComposeDress null callback was not retained");

    var player = RuntimeHelpers.GetUninitializedObject(playerType);
    var scriptV = new Dictionary<int, int>();
    playerType.GetField("m_ScriptVVars")!.SetValue(player, scriptV);
    playerType.GetField("m_ScriptSVars")!.SetValue(player, new Dictionary<int, int>());
    apiType.GetProperty("CurrentPlayer")!.SetValue(api, player);
    var oneArg = (IList)Activator.CreateInstance(listType)!;
    oneArg.Add(pasValueType.GetMethod("FromInt", BindingFlags.Static | BindingFlags.Public)!
        .Invoke(null, new object[] { 1 })!);
    Assert((bool)apiType.GetMethod("CallPlayerMethod")!.Invoke(api,
               new object[] { "HeroLearnLNJN", oneArg })! &&
           (bool)apiType.GetMethod("CallPlayerMethod")!.Invoke(api,
               new object[] { "HeroUpLevelLNJN", oneArg })! && scriptV.Count == 0,
        "native hero LNJN null callbacks changed script state");
}

static void TestHomeAndNormalLogin(Assembly gameSvr, Assembly systemModule)
{
    var shareType = gameSvr.GetType("GameSvr.M2Share", throwOnError: true)!;
    var config = shareType.GetField("g_Config", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
    var configType = config.GetType();
    var startPointField = shareType.GetField("StartPointList", BindingFlags.Static | BindingFlags.Public)!;
    var mapManagerField = shareType.GetField("MapManager", BindingFlags.Static | BindingFlags.Public)!;
    var guildManagerField = shareType.GetField("GuildManager", BindingFlags.Static | BindingFlags.Public)!;
    var castleManagerField = shareType.GetField("CastleManager", BindingFlags.Static | BindingFlags.Public)!;
    var userEngineField = shareType.GetField("UserEngine", BindingFlags.Static | BindingFlags.Public)!;
    var objectManagerField = shareType.GetField("ObjectManager", BindingFlags.Static | BindingFlags.Public)!;
    var oldStartPoints = startPointField.GetValue(null);
    var oldMapManager = mapManagerField.GetValue(null);
    var oldGuildManager = guildManagerField.GetValue(null);
    var oldCastleManager = castleManagerField.GetValue(null);
    var oldUserEngine = userEngineField.GetValue(null);
    var oldObjectManager = objectManagerField.GetValue(null);
    var oldHomeMap = configType.GetField("sHomeMap")!.GetValue(config);
    var oldHomeX = configType.GetField("nHomeX")!.GetValue(config);
    var oldHomeY = configType.GetField("nHomeY")!.GetValue(config);
    var oldVentureServer = configType.GetField("boVentureServer")!.GetValue(config);

    try
    {
        const string homeMap = "AUDIT_HOME";
        const short homeX = 20;
        const short homeY = 21;
        configType.GetField("sHomeMap")!.SetValue(config, homeMap);
        configType.GetField("nHomeX")!.SetValue(config, homeX);
        configType.GetField("nHomeY")!.SetValue(config, homeY);
        configType.GetField("boVentureServer")!.SetValue(config, false);

        var startPointType = systemModule.GetType("SystemModule.TStartPoint", throwOnError: true)!;
        startPointField.SetValue(null,
            Activator.CreateInstance(typeof(List<>).MakeGenericType(startPointType))!);
        var userEngineType = gameSvr.GetType("GameSvr.UserEngine", throwOnError: true)!;
        var userEngine = Activator.CreateInstance(userEngineType)!;

        var privateHomeInfo = userEngineType.GetMethod("GetHomeInfo",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(int), typeof(short).MakeByRefType(), typeof(short).MakeByRefType() }, null)!;
        var privateHomeArgs = new object[] { 0, (short)-1, (short)-1 };
        Assert((string)privateHomeInfo.Invoke(userEngine, privateHomeArgs)! == homeMap &&
               (short)privateHomeArgs[1] == homeX && (short)privateHomeArgs[2] == homeY,
            "job-specific GetHomeInfo did not write fallback home coordinates");

        var publicHomeInfo = userEngineType.GetMethod("GetHomeInfo", BindingFlags.Instance | BindingFlags.Public)!;
        var publicHomeArgs = new object[] { (short)-1, (short)-1 };
        Assert((string)publicHomeInfo.Invoke(userEngine, publicHomeArgs)! == homeMap &&
               (short)publicHomeArgs[0] == homeX && (short)publicHomeArgs[1] == homeY,
            "public GetHomeInfo did not write fallback home coordinates");

        var mapManagerType = gameSvr.GetType("GameSvr.MapManager", throwOnError: true)!;
        var mapManager = Activator.CreateInstance(mapManagerType)!;
        var environmentType = gameSvr.GetType("GameSvr.Envirnoment", throwOnError: true)!;
        var environment = Activator.CreateInstance(environmentType)!;
        environmentType.GetField("sMapName")!.SetValue(environment, homeMap);
        environmentType.GetField("sMapDesc")!.SetValue(environment, homeMap);
        environmentType.GetField("m_sMapFileName")!.SetValue(environment, homeMap);
        environmentType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(environment, new object[] { (short)64, (short)64 });
        ((IDictionary)mapManagerType.GetField("m_MapList", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(mapManager)!).Add(homeMap, environment);

        mapManagerField.SetValue(null, mapManager);
        guildManagerField.SetValue(null,
            Activator.CreateInstance(gameSvr.GetType("GameSvr.AssociationManager", throwOnError: true)!)!);
        castleManagerField.SetValue(null,
            Activator.CreateInstance(gameSvr.GetType("GameSvr.CastleManager", throwOnError: true)!)!);
        userEngineField.SetValue(null, userEngine);
        objectManagerField.SetValue(null,
            Activator.CreateInstance(gameSvr.GetType("GameSvr.ObjectManager", throwOnError: true)!)!);

        var humanInfoType = systemModule.GetType("SystemModule.THumDataInfo", throwOnError: true)!;
        var humanInfo = Activator.CreateInstance(humanInfoType)!;
        var humanData = humanInfoType.GetProperty("Data")!.GetValue(humanInfo)!;
        var humanDataType = humanData.GetType();
        humanDataType.GetField("sCharName")!.SetValue(humanData, "audit-character");
        humanDataType.GetField("sCurMap")!.SetValue(humanData, homeMap);
        humanDataType.GetField("wCurX")!.SetValue(humanData, homeX);
        humanDataType.GetField("wCurY")!.SetValue(humanData, homeY);
        humanDataType.GetField("sHomeMap")!.SetValue(humanData, homeMap);
        humanDataType.GetField("wHomeX")!.SetValue(humanData, homeX);
        humanDataType.GetField("wHomeY")!.SetValue(humanData, homeY);
        humanDataType.GetField("sStoragePwd")!.SetValue(humanData, string.Empty);
        humanDataType.GetField("sAccount")!.SetValue(humanData, "audit-account");
        var ability = humanDataType.GetField("Abil")!.GetValue(humanData)!;
        var abilityType = ability.GetType();
        abilityType.GetField("Level")!.SetValue(ability, (ushort)35);
        abilityType.GetField("HP")!.SetValue(ability, (ushort)100);
        abilityType.GetField("MP")!.SetValue(ability, (ushort)80);
        abilityType.GetField("MaxHP")!.SetValue(ability, (ushort)100);
        abilityType.GetField("MaxMP")!.SetValue(ability, (ushort)80);
        abilityType.GetField("MaxExp")!.SetValue(ability, 1000);

        var loadInfoType = systemModule.GetType("SystemModule.TLoadDBInfo", throwOnError: true)!;
        var loadInfo = Activator.CreateInstance(loadInfoType)!;
        loadInfoType.GetField("sAccount")!.SetValue(loadInfo, "audit-account");
        loadInfoType.GetField("sCharName")!.SetValue(loadInfo, "audit-character");
        loadInfoType.GetField("sIPaddr")!.SetValue(loadInfo, "127.0.0.1");
        loadInfoType.GetField("nSessionID")!.SetValue(loadInfo, 0x12345678);

        var openInfoType = gameSvr.GetType("GameSvr.TUserOpenInfo", throwOnError: true)!;
        var openInfo = Activator.CreateInstance(openInfoType)!;
        openInfoType.GetField("sChrName")!.SetValue(openInfo, "audit-character");
        openInfoType.GetField("LoadUser")!.SetValue(openInfo, loadInfo);
        openInfoType.GetField("HumanRcd")!.SetValue(openInfo, humanInfo);
        var player = userEngineType.GetMethod("ProcessHumans_MakeNewHuman",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(userEngine, new[] { openInfo });
        if (player == null)
            Thread.Sleep(1100);
        Assert(player != null, "normal-server MakeNewHuman returned null");
        var loadedName = (string)player!.GetType().GetField("m_sCharName")!.GetValue(player)!;
        var loadedSession = (int)player.GetType().GetField("m_nSessionID")!.GetValue(player)!;
        Assert(loadedName == "audit-character" && loadedSession == 0x12345678,
            $"normal-server MakeNewHuman changed loaded identity: name={loadedName}, session={loadedSession}");
    }
    finally
    {
        startPointField.SetValue(null, oldStartPoints);
        mapManagerField.SetValue(null, oldMapManager);
        guildManagerField.SetValue(null, oldGuildManager);
        castleManagerField.SetValue(null, oldCastleManager);
        userEngineField.SetValue(null, oldUserEngine);
        objectManagerField.SetValue(null, oldObjectManager);
        configType.GetField("sHomeMap")!.SetValue(config, oldHomeMap);
        configType.GetField("nHomeX")!.SetValue(config, oldHomeX);
        configType.GetField("nHomeY")!.SetValue(config, oldHomeY);
        configType.GetField("boVentureServer")!.SetValue(config, oldVentureServer);
    }
}

static void TestNativeStorageCapacity(Assembly dbSvr, Assembly systemModule)
{
    var codecType = dbSvr.GetType("DBSvr.Core.NativeHumanDataCodec", throwOnError: true)!;
    var infoType = systemModule.GetType("SystemModule.THumDataInfo", throwOnError: true)!;
    var info = Activator.CreateInstance(infoType)!;
    var data = infoType.GetProperty("Data")!.GetValue(info)!;
    data.GetType().GetField("StorageSpaceCount")!.SetValue(data, 123);

    var encodeArgs = new object?[] { info, null, null, null };
    Assert((bool)codecType.GetMethod("TryEncode")!.Invoke(null, encodeArgs)!,
        $"native human storage-capacity encode failed: {encodeArgs[3]}");
    var dataBlob = (byte[])encodeArgs[1]!;
    var scriptBlob = (byte[]?)encodeArgs[2];

    var unwrap = codecType.GetMethod("TryUnwrap", BindingFlags.Static | BindingFlags.NonPublic)!;
    var unwrapArgs = new object?[] { dataBlob, 0xEEF8, (ushort)0xEF00, null, 0u, false, null };
    Assert((bool)unwrap.Invoke(null, unwrapArgs)! &&
           BitConverter.ToUInt16((byte[])unwrapArgs[3]!, 0x050E) == 123,
        "native storage capacity was not written to raw human offset 0x050E");

    var decodeArgs = new object?[] { dataBlob, scriptBlob, null, null };
    Assert((bool)codecType.GetMethod("TryDecode")!.Invoke(null, decodeArgs)!,
        $"native human storage-capacity decode failed: {decodeArgs[3]}");
    var decodedData = infoType.GetProperty("Data")!.GetValue(decodeArgs[2])!;
    Assert((int)decodedData.GetType().GetField("StorageSpaceCount")!.GetValue(decodedData)! == 123,
        "native storage capacity did not survive the DBServer round trip");
}

static void TestNativeWeaponUpgradePersistence(Assembly gameSvr, Assembly systemModule, string gameSvrDirectory)
{
    var itemType = systemModule.GetType("SystemModule.TUserItem", throwOnError: true)!;
    var codecType = gameSvr.GetType("GameSvr.LegacyUserItem208Codec", throwOnError: true)!;
    var repositoryType = gameSvr.GetType("GameSvr.WeaponUpgradeRepository", throwOnError: true)!;
    var merchantType = gameSvr.GetType("GameSvr.Merchant", throwOnError: true)!;

    foreach (var methodName in new[] { "HasPending", "Insert", "GetByCharacter", "Delete", "CleanupExpired" })
    {
        Assert(repositoryType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic) != null,
            $"native WeaponUpg repository method is missing: {methodName}");
    }
    Assert(merchantType.GetField("m_UpgradeWeaponList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
        "merchant still keeps the obsolete in-memory weapon upgrade list");
    Assert(merchantType.GetMethod("ClickUpWeaponNow") != null &&
           merchantType.GetMethod("ClickUpWeaponNoBreak") != null &&
           merchantType.GetMethod("ClickGetBackUpWeapon") != null,
        "native Pascal weapon upgrade API is not exposed by Merchant");

    var item = Activator.CreateInstance(itemType)!;
    itemType.GetField("MakeIndex")!.SetValue(item, unchecked((int)0x811F4C26u));
    itemType.GetField("wIndex")!.SetValue(item, (ushort)964);
    itemType.GetField("Dura")!.SetValue(item, (ushort)10000);
    itemType.GetField("DuraMax")!.SetValue(item, (ushort)10000);
    itemType.GetField("UpgradeFlags")!.SetValue(item, (byte)0xC0);
    var values = (byte[])itemType.GetField("btValue")!.GetValue(item)!;
    values[0] = 3;
    values[9] = 0;

    Assert(((byte[])itemType.GetMethod("GetBuffer")!.Invoke(item, null)!).Length == 147,
        "server-only native refine flags changed the mobile TUserItem packet size");
    var tryEncode = codecType.GetMethod("TryEncode", BindingFlags.Static | BindingFlags.NonPublic)!;
    var encodeArgs = new object?[] { item, null, null };
    Assert((bool)tryEncode.Invoke(null, encodeArgs)!, $"native 208-byte item encode failed: {encodeArgs[2]}");
    var weaponData = (string)encodeArgs[1]!;
    Assert(weaponData.Length == 416 && weaponData.All(ch => char.IsDigit(ch) || (ch >= 'A' && ch <= 'F')),
        "WeaponData is not 416-character uppercase HEX");
    Assert(weaponData.StartsWith("264C1F81C40310271027", StringComparison.Ordinal),
        "legacy item core offsets do not match the native NpcSave record");
    Assert(weaponData.Substring(0x27 * 2, 2) == "C0", "native no-break flags are not at item offset 0x27");

    var tryDecode = codecType.GetMethod("TryDecode", BindingFlags.Static | BindingFlags.NonPublic)!;
    var decodeArgs = new object?[] { weaponData, null, null };
    Assert((bool)tryDecode.Invoke(null, decodeArgs)!, $"native 208-byte item decode failed: {decodeArgs[2]}");
    var decoded = decodeArgs[1]!;
    Assert(unchecked((uint)(int)itemType.GetField("MakeIndex")!.GetValue(decoded)!) == 0x811F4C26u &&
           (ushort)itemType.GetField("wIndex")!.GetValue(decoded)! == 964 &&
           (byte)itemType.GetField("UpgradeFlags")!.GetValue(decoded)! == 0xC0,
        "legacy item core or unsigned ItemID did not round-trip");

    itemType.GetField("ys2")!.SetValue(item, (byte)1);
    encodeArgs = new object?[] { item, null, null };
    Assert(!(bool)tryEncode.Invoke(null, encodeArgs)!, "unmapped Yanshen item data was silently written to WeaponData");
    itemType.GetField("ys2")!.SetValue(item, (byte)0);
    var unknownTail = weaponData.Remove(0x30 * 2, 2).Insert(0x30 * 2, "01");
    decodeArgs = new object?[] { unknownTail, null, null };
    Assert(!(bool)tryDecode.Invoke(null, decodeArgs)!, "unmapped native tail data was silently discarded");
    decodeArgs = new object?[] { weaponData.ToLowerInvariant(), null, null };
    Assert(!(bool)tryDecode.Invoke(null, decodeArgs)!, "lowercase/non-native WeaponData was accepted");

    var baseObjectType = gameSvr.GetType("GameSvr.TBaseObject", throwOnError: true)!;
    var checkUpgrade = baseObjectType.GetMethod("CheckWeaponUpgradeStatus", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var baseObject = RuntimeHelpers.GetUninitializedObject(baseObjectType);

    var noBreakItem = Activator.CreateInstance(itemType)!;
    itemType.GetField("wIndex")!.SetValue(noBreakItem, (ushort)964);
    itemType.GetField("UpgradeFlags")!.SetValue(noBreakItem, (byte)0x80);
    ((byte[])itemType.GetField("btValue")!.GetValue(noBreakItem)!)[9] = 1;
    var statusArgs = new[] { noBreakItem };
    Assert(!(bool)checkUpgrade.Invoke(baseObject, statusArgs)! &&
           (ushort)itemType.GetField("wIndex")!.GetValue(noBreakItem)! == 964 &&
           (byte)itemType.GetField("UpgradeFlags")!.GetValue(noBreakItem)! == 0,
        "native no-break failure still destroyed the weapon");

    var brokenItem = Activator.CreateInstance(itemType)!;
    itemType.GetField("wIndex")!.SetValue(brokenItem, (ushort)964);
    ((byte[])itemType.GetField("btValue")!.GetValue(brokenItem)!)[9] = 1;
    statusArgs = new[] { brokenItem };
    Assert(!(bool)checkUpgrade.Invoke(baseObject, statusArgs)! &&
           (ushort)itemType.GetField("wIndex")!.GetValue(brokenItem)! == 0,
        "normal failed refine did not break the weapon");

    var successItem = Activator.CreateInstance(itemType)!;
    itemType.GetField("wIndex")!.SetValue(successItem, (ushort)964);
    ((byte[])itemType.GetField("btValue")!.GetValue(successItem)!)[9] = 12;
    statusArgs = new[] { successItem };
    Assert((bool)checkUpgrade.Invoke(baseObject, statusArgs)! &&
           ((byte[])itemType.GetField("btValue")!.GetValue(successItem)!)[0] == 3,
        "native refine status at btValue[9] did not apply the DC bonus");

    var yanshenType = gameSvr.GetType("GameSvr.Plugins.YanshenApi", throwOnError: true)!;
    var setElement = yanshenType.GetMethod("SetElementValue", BindingFlags.Static | BindingFlags.NonPublic)!;
    var getElement = yanshenType.GetMethod("GetElementValue", BindingFlags.Static | BindingFlags.NonPublic)!;
    var setExtreme = yanshenType.GetMethod("SetExtremeValue", BindingFlags.Static | BindingFlags.NonPublic)!;
    var getExtreme = yanshenType.GetMethod("GetExtremeValue", BindingFlags.Static | BindingFlags.NonPublic)!;
    var pluginItem = Activator.CreateInstance(itemType)!;
    Assert((bool)setElement.Invoke(null, new[] { pluginItem, (object)1, 123456 })! &&
           (bool)setElement.Invoke(null, new[] { pluginItem, (object)9, 99 })! &&
           (int)getElement.Invoke(null, new[] { pluginItem, (object)1 })! == 123456 &&
           (int)getElement.Invoke(null, new[] { pluginItem, (object)9 })! == 99 &&
           ((byte[])itemType.GetField("btValue")!.GetValue(pluginItem)!)[9] == 0,
        "Yanshen elements still overwrite native refine status");
    Assert((bool)setExtreme.Invoke(null, new[] { pluginItem, (object)0, 33 })! &&
           (bool)setExtreme.Invoke(null, new[] { pluginItem, (object)5, 66 })! &&
           (int)getExtreme.Invoke(null, new[] { pluginItem, (object)0 })! == 33 &&
           (int)getExtreme.Invoke(null, new[] { pluginItem, (object)5 })! == 66 &&
           ((byte[])itemType.GetField("btValue")!.GetValue(pluginItem)!)[9] == 0,
        "Yanshen extreme values still overwrite native refine status");

    var smakeType = gameSvr.GetType("GameSvr.SmakeItemCommand", throwOnError: true)!;
    var smake = Activator.CreateInstance(smakeType)!;
    var uninitializedPlayer = RuntimeHelpers.GetUninitializedObject(
        gameSvr.GetType("GameSvr.TPlayObject", throwOnError: true)!);
    var useItemsField = uninitializedPlayer.GetType().GetField("m_UseItems")!;
    var equippedItemCount = (int)systemModule.GetType("SystemModule.Grobal2", throwOnError: true)!
        .GetField("HUMAN_EQUIPPED_ITEM_COUNT")!.GetRawConstantValue()!;
    useItemsField.SetValue(uninitializedPlayer,
        Array.CreateInstance(useItemsField.FieldType.GetElementType()!, equippedItemCount));
    smakeType.GetMethod("SmakeItem")!.Invoke(smake,
        new object[] { new[] { "0", "9", "1" }, uninitializedPlayer });

    var assemblyBytes = File.ReadAllBytes(Path.Combine(gameSvrDirectory, "GameSvr.dll"));
    Assert(BinaryContainsText(assemblyBytes, "gamedata.weaponupg") &&
           BinaryContainsText(assemblyBytes, "INTERVAL 10 MINUTE") &&
           BinaryContainsText(assemblyBytes, "INTERVAL 4 MONTH"),
        "native WeaponUpg table or timing contract is missing");
    Assert(!BinaryContainsText(assemblyBytes, "Create table if not exists WeaponUpg", ignoreCase: true),
        "GameSvr reintroduced WeaponUpg DDL instead of using the native schema");
}

static bool BinaryContainsText(byte[] data, string text, bool ignoreCase = false)
{
    foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode })
    {
        var pattern = encoding.GetBytes(text);
        for (var start = 0; start <= data.Length - pattern.Length; start++)
        {
            var matches = true;
            for (var offset = 0; offset < pattern.Length; offset++)
            {
                var actual = data[start + offset];
                var expected = pattern[offset];
                if (actual == expected) continue;
                if (!ignoreCase || actual > 0x7F || expected > 0x7F ||
                    ToAsciiLower(actual) != ToAsciiLower(expected))
                {
                    matches = false;
                    break;
                }
            }
            if (matches) return true;
        }
    }
    return false;
}

static byte ToAsciiLower(byte value) =>
    value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + ('a' - 'A')) : value;

static T GetProperty<T>(object instance, string name) =>
    (T)instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

static T GetField<T>(object instance, string name) =>
    (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

static void TestGameSvrGateSingleWriter(Assembly gameSvr, Type globalType)
{
    var gateInfoType = gameSvr.GetType("GameSvr.TGateInfo", throwOnError: true)!;
    var gateServiceType = gameSvr.GetType("GameSvr.GateService", throwOnError: true)!;
    var checkCommand = Convert.ToUInt16(globalType.GetField("GM_CHECKSERVER")!.GetRawConstantValue());
    var userIndexCommand = Convert.ToUInt16(
        globalType.GetField("GM_SERVERUSERINDEX")!.GetRawConstantValue());

    using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var accept = listener.AcceptSocketAsync();
    using var receiver = new System.Net.Sockets.TcpClient();
    receiver.Connect(System.Net.IPAddress.Loopback, port);
    using var sender = accept.GetAwaiter().GetResult();

    var gateInfo = Activator.CreateInstance(gateInfoType)!;
    gateInfoType.GetField("Socket")!.SetValue(gateInfo, sender);
    gateInfoType.GetField("boUsed")!.SetValue(gateInfo, true);
    var service = Activator.CreateInstance(gateServiceType, new[] { (object)1, gateInfo })!;
    var stop = gateServiceType.GetMethod("Stop")!;
    try
    {
        gateServiceType.GetMethod("SendCheck")!.Invoke(service, new object[] { checkCommand });
        gateServiceType.GetMethod("SendNewUserMsg", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, new object[] { 0x12345678, 7 });
        Thread.Sleep(50);
        Assert(receiver.Available == 0,
            "GameSvr control frame bypassed the per-gate send queue");

        gateServiceType.GetMethod("StartQueueService")!.Invoke(service, null);
        // 原版 16 字节头: 控制帧恒 16B (BodyLen=0), user-index 数据帧 16+4=20B。
        // Cmd@+0x0C, BodyLen@+0x0E, payload@+0x10 (证据: 构造器 0x5F61C5/0x637AC1 与接收器 0x63B258)。
        var expectedLength = 16 + 20;
        var bytes = new byte[expectedLength];
        receiver.ReceiveTimeout = 3000;
        var stream = receiver.GetStream();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            Assert(read > 0, "GameSvr gate send queue closed before complete frames");
            offset += read;
        }

        Assert(BitConverter.ToUInt32(bytes, 0) == 0x33AABB77 &&
               BitConverter.ToUInt16(bytes, 12) == checkCommand &&
               BitConverter.ToUInt16(bytes, 14) == 0,
            "GameSvr queued check frame is malformed or out of order");
        const int second = 16;
        Assert(BitConverter.ToUInt32(bytes, second) == 0x33AABB77 &&
               BitConverter.ToUInt32(bytes, second + 4) == 0x12345678 &&
               BitConverter.ToUInt16(bytes, second + 12) == userIndexCommand &&
               BitConverter.ToUInt16(bytes, second + 14) == 4 &&
               BitConverter.ToInt32(bytes, second + 16) == 7,
            "GameSvr queued user-index frame is malformed or out of order");
    }
    finally
    {
        stop.Invoke(service, null);
        listener.Stop();
    }
}

static void TestRequestServerFraming(Type parserType)
{
    var append = parserType.GetMethod("TryAppend", BindingFlags.Instance | BindingFlags.Public)!;
    var packetType = parserType.Assembly.GetType("SystemModule.RequestServerPacket", throwOnError: true)!;
    var packet = Activator.CreateInstance(packetType)!;
    packetType.GetProperty("QueryId")!.SetValue(packet, 1234);
    packetType.GetProperty("Message")!.SetValue(packet, new byte[] { 1, 2, 3 });
    packetType.GetProperty("Packet")!.SetValue(packet, new byte[] { 4, 5, 6, 7 });
    packetType.GetProperty("CheckKey")!.SetValue(packet, new byte[] { 8, 9 });
    var wireFrame = (byte[])packetType.GetMethod("GetBuffer")!.Invoke(packet, null)!;
    Assert(wireFrame[0] == (byte)'#' && wireFrame[^1] == (byte)'!',
        "RequestServerPacket wire markers changed");
    Assert(BitConverter.ToInt32(wireFrame, 1) == wireFrame.Length,
        "RequestServerPacket wire length is not the total frame length");
    var wireParser = Activator.CreateInstance(parserType)!;
    var wireResult = Feed(wireParser, append, wireFrame, 0, wireFrame.Length);
    Assert(wireResult.ok && wireResult.frames.Count == 1 && wireResult.frames[0].SequenceEqual(wireFrame),
        "serialized RequestServerPacket did not round-trip through the frame parser");

    var frameA = BuildFrame(22, 0x31);
    var frameB = BuildFrame(67, 0x42);
    var stream = frameA.Concat(frameB).ToArray();

    var stickyParser = Activator.CreateInstance(parserType)!;
    var sticky = Feed(stickyParser, append, stream, 0, stream.Length);
    Assert(sticky.ok && sticky.frames.Count == 2, "sticky RequestServerPacket frames were not split");
    Assert(sticky.frames[0].SequenceEqual(frameA) && sticky.frames[1].SequenceEqual(frameB),
        "sticky RequestServerPacket payload changed during framing");

    var byteParser = Activator.CreateInstance(parserType)!;
    var byteFrames = new List<byte[]>();
    for (var i = 0; i < stream.Length; i++)
    {
        var part = Feed(byteParser, append, stream, i, 1);
        Assert(part.ok, $"byte-split frame failed: {part.error}");
        byteFrames.AddRange(part.frames);
    }
    Assert(byteFrames.Count == 2 && byteFrames[0].SequenceEqual(frameA) && byteFrames[1].SequenceEqual(frameB),
        "byte-split RequestServerPacket frames were not reassembled");

    var halfParser = Activator.CreateInstance(parserType)!;
    Assert(Feed(halfParser, append, frameA, 0, 2).frames.Count == 0, "partial header emitted a frame");
    Assert(Feed(halfParser, append, frameA, 2, 3).frames.Count == 0, "header-only input emitted a frame");
    Assert(Feed(halfParser, append, frameA, 5, frameA.Length - 6).frames.Count == 0, "partial body emitted a frame");
    var tailAndSticky = new byte[1 + frameB.Length];
    tailAndSticky[0] = frameA[^1];
    Buffer.BlockCopy(frameB, 0, tailAndSticky, 1, frameB.Length);
    var completed = Feed(halfParser, append, tailAndSticky, 0, tailAndSticky.Length);
    Assert(completed.ok && completed.frames.Count == 2 &&
           completed.frames[0].SequenceEqual(frameA) && completed.frames[1].SequenceEqual(frameB),
        "partial body followed by a sticky frame was not parsed");

    var invalidMarker = (byte[])frameA.Clone();
    invalidMarker[0] = (byte)'X';
    var malformedParser = Activator.CreateInstance(parserType)!;
    var malformed = Feed(malformedParser, append, invalidMarker, 0, invalidMarker.Length);
    Assert(!malformed.ok && malformed.error.Contains("marker", StringComparison.OrdinalIgnoreCase),
        "invalid RequestServerPacket marker was accepted");

    var invalidTerminator = (byte[])frameA.Clone();
    invalidTerminator[^1] = (byte)'X';
    var invalidTerminatorResult = Feed(malformedParser, append, invalidTerminator, 0, invalidTerminator.Length);
    Assert(!invalidTerminatorResult.ok && invalidTerminatorResult.error.Contains("terminator", StringComparison.OrdinalIgnoreCase),
        "invalid RequestServerPacket terminator was accepted");

    var shortLength = BuildHeader(21);
    var shortResult = Feed(malformedParser, append, shortLength, 0, shortLength.Length);
    Assert(!shortResult.ok && shortResult.error.Contains("length", StringComparison.OrdinalIgnoreCase),
        "short RequestServerPacket length was accepted");

    var maxLength = (int)parserType.GetField("DefaultMaximumFrameLength")!.GetRawConstantValue()!;
    var oversized = BuildHeader(maxLength + 1);
    var oversizedResult = Feed(malformedParser, append, oversized, 0, oversized.Length);
    Assert(!oversizedResult.ok && oversizedResult.error.Contains("length", StringComparison.OrdinalIgnoreCase),
        "oversized RequestServerPacket length was accepted");

    var recovered = Feed(malformedParser, append, frameA, 0, frameA.Length);
    Assert(recovered.ok && recovered.frames.Count == 1 && recovered.frames[0].SequenceEqual(frameA),
        "RequestServerPacket parser did not reset after malformed input");
}

static void TestHeroWireRecords(Assembly gameSvr, Assembly systemModule, Encoding gbk)
{
    var globalType = systemModule.GetType("SystemModule.Grobal2", throwOnError: true)!;
    var magicType = systemModule.GetType("SystemModule.TNewClientMagic", throwOnError: true)!;
    var magic = Activator.CreateInstance(magicType)!;
    SetField(magic, "MagicName", "烈火剑法");
    SetField(magic, "MagicType", (byte)1);
    SetField(magic, "EffectType", (byte)2);
    SetField(magic, "Effect", (byte)3);
    SetField(magic, "MagicId", (ushort)0x1234);
    SetField(magic, "Level", (short)4);
    SetField(magic, "Key", (short)5);
    SetField(magic, "NeedMp", (short)6);
    SetField(magic, "SpellTick", (short)7);
    SetField(magic, "NextNeedLv", (short)8);
    SetField(magic, "ColdTick", 0x11223344);
    SetField(magic, "CurTrain", 0x22334455);
    SetField(magic, "MaxTrain", 0x33445566);
    SetField(magic, "DelayTime", 0x44556677);

    var bytes = (byte[])magicType.GetMethod("GetBuffer")!.Invoke(magic, null)!;
    var nameBytes = gbk.GetBytes("烈火剑法");
    Assert(bytes.Length == 46, $"TNewClientMagic length is {bytes.Length}, expected 46");
    Assert(bytes[0] == nameBytes.Length && bytes.Skip(1).Take(nameBytes.Length).SequenceEqual(nameBytes),
        "TNewClientMagic GBK ShortString payload mismatch");
    Assert(bytes.Skip(1 + nameBytes.Length).Take(14 - nameBytes.Length).All(value => value == 0),
        "TNewClientMagic name padding is not zero");
    Assert(bytes[0x0F] == 1 && bytes[0x10] == 2 && bytes[0x11] == 3,
        "TNewClientMagic byte fields have incorrect offsets");
    Assert(BitConverter.ToUInt16(bytes, 0x12) == 0x1234 &&
           BitConverter.ToInt16(bytes, 0x14) == 4 &&
           BitConverter.ToInt16(bytes, 0x16) == 5 &&
           BitConverter.ToInt16(bytes, 0x18) == 6 &&
           BitConverter.ToInt16(bytes, 0x1A) == 7 &&
           BitConverter.ToInt16(bytes, 0x1C) == 8,
        "TNewClientMagic short fields have incorrect offsets");
    Assert(BitConverter.ToInt32(bytes, 0x1E) == 0x11223344 &&
           BitConverter.ToInt32(bytes, 0x22) == 0x22334455 &&
           BitConverter.ToInt32(bytes, 0x26) == 0x33445566 &&
           BitConverter.ToInt32(bytes, 0x2A) == 0x44556677,
        "TNewClientMagic int fields have incorrect offsets");

    var heroType = gameSvr.GetType("GameSvr.HeroObject", throwOnError: true)!;
    var getCapacity = heroType.GetMethod("GetHeroBagCapacity",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    foreach (var (level, capacity) in new[]
             {
                 (1, 10), (10, 10), (11, 20), (20, 20), (21, 30),
                 (30, 30), (31, 35), (35, 35), (36, 40), (99, 40)
             })
    {
        Assert((int)getCapacity.Invoke(null, new object[] { level })! == capacity,
            $"hero bag capacity mismatch at level {level}");
    }

    var abilityHeader = heroType.GetMethod("BuildHeroAbilityHeader",
        BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { 0x10203040, 2 })!;
    AssertRuntimePacket(abilityHeader, 900, 0x10203040, 2, 0, 0, "hero ability");
    var nameHeader = heroType.GetMethod("BuildHeroNameHeader",
        BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { 2, 7 })!;
    AssertRuntimePacket(nameHeader, 898, 0, 2, 7, 0, "hero identity");

    var takeOnIl = heroType.GetMethod("ClientHeroTakeOn")!.GetMethodBody()!.GetILAsByteArray()!;
    var takeOffIl = heroType.GetMethod("ClientHeroTakeOff")!.GetMethodBody()!.GetILAsByteArray()!;
    Assert(ContainsLdcI4S(takeOnIl, -2), "SM_HERO_TAKEON_FAIL lost the native -2 rejection status");
    Assert(ContainsLdcI4S(takeOffIl, -3), "SM_HERO_TAKEOFF_FAIL lost the native -3 bag rejection status");

    var shareType = gameSvr.GetType("GameSvr.M2Share", throwOnError: true)!;
    var userEngineField = shareType.GetField("UserEngine", BindingFlags.Static | BindingFlags.Public)!;
    var objectManagerField = shareType.GetField("ObjectManager", BindingFlags.Static | BindingFlags.Public)!;
    var previousUserEngine = userEngineField.GetValue(null);
    var previousObjectManager = objectManagerField.GetValue(null);
    try
    {
        var userEngineType = gameSvr.GetType("GameSvr.UserEngine", throwOnError: true)!;
        var objectManagerType = gameSvr.GetType("GameSvr.ObjectManager", throwOnError: true)!;
        objectManagerField.SetValue(null, Activator.CreateInstance(objectManagerType));
        var userEngine = Activator.CreateInstance(userEngineType)!;
        var stdItems = (IList)userEngineType.GetField("StdItemList")!.GetValue(userEngine)!;
        var goodItemType = gameSvr.GetType("GameSvr.GoodItem", throwOnError: true)!;
        stdItems.Add(NewGoodItem("audit-weapon", 7));
        stdItems.Add(NewGoodItem("audit-dress", 9));
        stdItems.Add(NewGoodItem("audit-union", 7, 25));
        userEngineField.SetValue(null, userEngine);

        var hero = Activator.CreateInstance(heroType)!;
        var baseObjectType = gameSvr.GetType("GameSvr.TBaseObject", throwOnError: true)!;
        var useItems = (Array)baseObjectType.GetField("m_UseItems")!.GetValue(hero)!;
        var userItemType = systemModule.GetType("SystemModule.TUserItem", throwOnError: true)!;
        useItems.SetValue(NewUserItem(1), 1);
        useItems.SetValue(NewUserItem(2), 0);
        var checkUsExp = heroType.GetMethod("CheckIfCanAddUSExp",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert((int)checkUsExp.Invoke(hero, null)! == -2,
            "ChkIfCanAddUSExp did not reject a hero without union magic");
        var userMagicType = gameSvr.GetType("GameSvr.TUserMagic", throwOnError: true)!;
        var unionMagic = Activator.CreateInstance(userMagicType)!;
        userMagicType.GetField("wMagIdx")!.SetValue(unionMagic, (ushort)69);
        ((IList)baseObjectType.GetField("m_MagicList")!.GetValue(hero)!).Add(unionMagic);
        Assert((int)checkUsExp.Invoke(hero, null)! == -3,
            "ChkIfCanAddUSExp did not require a TUnionItem in hero slot 9");
        useItems.SetValue(NewUserItem(3), 9);
        Assert((int)checkUsExp.Invoke(hero, null)! == 0,
            "ChkIfCanAddUSExp rejected native magic 69 plus TUnionItem equipment");
        baseObjectType.GetField("m_btRaceImg")!.SetValue(hero, (byte)6);
        baseObjectType.GetField("m_btHair")!.SetValue(hero, (byte)3);
        var logonBody = (byte[])heroType.GetMethod("BuildHeroLogonBody",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hero, null)!;
        Assert(logonBody.Length == 40 && BitConverter.ToInt32(logonBody, 24) == 0,
            "SM_HERO_LOGON native reserved field is not zero");

        var grobalType = systemModule.GetType("SystemModule.Grobal2", throwOnError: true)!;
        var heroRace = (int)grobalType.GetField("RC_HEROOBJECT")!.GetRawConstantValue()!;
        var playerRace = (int)grobalType.GetField("RC_PLAYOBJECT")!.GetRawConstantValue()!;
        var raceServerField = baseObjectType.GetField("m_btRaceServer")!;
        var getMobileFeature = baseObjectType.GetMethod("GetMobileFeature")!;
        var getFeature = baseObjectType.GetMethod("GetFeature")!;

        raceServerField.SetValue(hero, (byte)playerRace);
        var playerMobileFeature = (byte[])getMobileFeature.Invoke(hero, null)!;
        var playerFeature = (int)getFeature.Invoke(hero, new[] { hero })!;
        raceServerField.SetValue(hero, (byte)heroRace);
        var heroMobileFeature = (byte[])getMobileFeature.Invoke(hero, null)!;
        var heroFeature = (int)getFeature.Invoke(hero, new[] { hero })!;

        Assert(heroMobileFeature.SequenceEqual(playerMobileFeature) &&
               BitConverter.ToUInt16(heroMobileFeature, 4) == 7 &&
               BitConverter.ToUInt16(heroMobileFeature, 6) == 9,
            "RC_HEROOBJECT did not serialize live weapon/dress appearance");
        Assert(heroFeature == playerFeature,
            "RC_HEROOBJECT legacy feature did not use the human equipment encoding");

        object NewGoodItem(string name, byte shape, byte stdMode = 0)
        {
            var item = Activator.CreateInstance(goodItemType)!;
            goodItemType.GetField("Name")!.SetValue(item, name);
            goodItemType.GetField("Shape")!.SetValue(item, shape);
            goodItemType.GetField("StdMode")!.SetValue(item, stdMode);
            return item;
        }

        object NewUserItem(ushort index)
        {
            var item = Activator.CreateInstance(userItemType)!;
            userItemType.GetField("wIndex")!.SetValue(item, index);
            return item;
        }
    }
    finally
    {
        userEngineField.SetValue(null, previousUserEngine);
        objectManagerField.SetValue(null, previousObjectManager);
    }

    var processType = systemModule.GetType("SystemModule.TProcessMessage", throwOnError: true)!;
    var buildRuntimePacket = heroType.GetMethod("BuildHeroRuntimePacket",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    Assert(heroType.GetMethod("Operate", BindingFlags.Instance | BindingFlags.Public |
               BindingFlags.DeclaredOnly) != null,
        "HeroObject does not override Operate for native runtime message forwarding");

    AssertRuntimePacket(BuildRuntimePacket("RM_LEVELUP", 0, 0, 0, 0, 0x10203040, 36),
        914, 0x10203040, 36, 40, 0, "hero level-up");
    AssertRuntimePacket(BuildRuntimePacket("RM_WINEXP", 0, 0x55667788, 0, 0, 0x10203040, 36),
        915, 0x10203040, 0x7788, 0x5566, 0, "hero experience");
    AssertRuntimePacket(BuildRuntimePacket("RM_MAGIC_LVEXP", 0, 0x1234, 3, 0x11223344,
            0x10203040, 36),
        916, 0x1234, 3, 0x3344, 0x1122, "hero magic experience");
    AssertRuntimePacket(BuildRuntimePacket("RM_DURACHANGE", 7, 321, 0x55667788, 0,
            0x10203040, 36),
        919, 321, 7, 0x7788, 0x5566, "hero equipment durability");

    object BuildRuntimePacket(string runtimeIdent, int wParam, int nParam1, int nParam2,
        int nParam3, int currentExp, int level)
    {
        var processMessage = Activator.CreateInstance(processType)!;
        SetField(processMessage, "wIdent", (int)globalType.GetField(runtimeIdent)!.GetRawConstantValue()!);
        SetField(processMessage, "wParam", wParam);
        SetField(processMessage, "nParam1", nParam1);
        SetField(processMessage, "nParam2", nParam2);
        SetField(processMessage, "nParam3", nParam3);
        return buildRuntimePacket.Invoke(null, new[] { processMessage, (object)currentExp, level })!;
    }

    static void AssertRuntimePacket(object packet, int ident, int recog, int param, int tag,
        int series, string description)
    {
        var packetType = packet.GetType();
        Assert((ushort)packetType.GetField("Ident")!.GetValue(packet)! == ident &&
               (int)packetType.GetField("Recog")!.GetValue(packet)! == recog &&
               (ushort)packetType.GetField("Param")!.GetValue(packet)! == param &&
               (ushort)packetType.GetField("Tag")!.GetValue(packet)! == tag &&
               (ushort)packetType.GetField("Series")!.GetValue(packet)! == series,
            $"{description} header mapping does not match native THeroAct.Operate");
    }

    static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);
    }

    static bool ContainsLdcI4S(byte[] il, sbyte value)
    {
        for (var i = 0; i + 1 < il.Length; i++)
            if (il[i] == 0x1F && il[i + 1] == unchecked((byte)value))
                return true;
        return false;
    }
}

static (bool ok, List<byte[]> frames, string error) Feed(
    object parser, MethodInfo append, byte[] data, int offset, int count)
{
    var invokeArgs = new object?[] { data, offset, count, null, null };
    var ok = (bool)append.Invoke(parser, invokeArgs)!;
    return (ok, (List<byte[]>)invokeArgs[3]!, (string)invokeArgs[4]!);
}

static void TestP6CProtocolAndSessionRepairs(
    Assembly gameSvr, Assembly gameGate, Assembly dbSvr, Assembly systemModule, Encoding gbk)
{
    var percentParserType = systemModule.GetType(
        "SystemModule.Packet.PercentDollarFrameParser", throwOnError: true)!;
    var append = percentParserType.GetMethod("TryAppend")!;
    var maximum = (int)percentParserType.GetField("DefaultMaximumFrameLength")!.GetRawConstantValue()!;
    var parser = Activator.CreateInstance(percentParserType, new object[] { maximum })!;
    var frameA = gbk.GetBytes("%A7/中文名字$");
    var frameB = gbk.GetBytes("%X7$");
    var sticky = frameA.Concat(frameB).ToArray();
    var parsed = new List<byte[]>();
    for (var i = 0; i < sticky.Length; i++)
    {
        var part = Feed(parser, append, sticky, i, 1);
        Assert(part.ok, $"byte-split %...$ frame failed: {part.error}");
        parsed.AddRange(part.frames);
    }
    Assert(parsed.Count == 2 && parsed[0].SequenceEqual(frameA) && parsed[1].SequenceEqual(frameB),
        "%...$ parser lost a sticky frame or split GBK character");

    var overflow = new byte[maximum + 1];
    overflow[0] = (byte)'%';
    var overflowResult = Feed(parser, append, overflow, 0, overflow.Length);
    Assert(!overflowResult.ok && overflowResult.error.Contains("exceeds", StringComparison.OrdinalIgnoreCase),
        "%...$ parser accepted an oversized unterminated frame");
    var recovered = Feed(parser, append, frameB, 0, frameB.Length);
    Assert(recovered.ok && recovered.frames.Count == 1 && recovered.frames[0].SequenceEqual(frameB),
        "%...$ parser did not recover after overflow");

    var frameType = systemModule.GetType("SystemModule.Packet.Frame44FF44", throwOnError: true)!;
    var frame = Activator.CreateInstance(frameType)!;
    frameType.GetField("Marker")!.SetValue(frame, (ushort)0x1700);
    frameType.GetField("Cmd")!.SetValue(frame, (byte)0xEE);
    frameType.GetField("Flag")!.SetValue(frame, (byte)0xDD);
    frameType.GetField("Payload")!.SetValue(frame, new byte[7]);
    var serializedFrame = (byte[])frameType.GetMethod("ToBytes")!.Invoke(frame, null)!;
    Assert(BitConverter.ToUInt16(serializedFrame, 6) == 7,
        "44FF44FF serialized length does not match its actual payload");

    var classifierType = gameGate.GetType("GameGate.Core.ActionClassifier", throwOnError: true)!;
    var classify = classifierType.GetMethod("TryClassify")!;
    AssertClassified(3011, "WALK");
    AssertClassified(3013, "RUN");
    AssertClassified(3014, "ATTACK");
    AssertClassified(3017, "CAST");
    AssertClassified(3010, "TURN");
    AssertClassified(3030, "CHAT");
    var obsoleteArgs = new object?[] { (ushort)3, null };
    Assert(!(bool)classify.Invoke(null, obsoleteArgs)!, "obsolete walk command 3 is still speed-classified");
    var inventedMallArgs = new object?[] { (ushort)9102, null };
    Assert(!(bool)classify.Invoke(null, inventedMallArgs)!,
        "invented CM_BUYMALLITEM 9102 is still speed-classified");

    var gateConfigType = gameGate.GetType("GameGate.Core.GateConfig", throwOnError: true)!;
    var gateConfig = Activator.CreateInstance(gateConfigType)!;
    gateConfigType.GetField("WalkInterval")!.SetValue(gateConfig, 100);
    var speedType = gameGate.GetType("GameGate.Core.SpeedDetector", throwOnError: true)!;
    var speed = Activator.CreateInstance(speedType, gateConfig)!;
    var sessionType = gameGate.GetType("GameGate.Models.ClientSession", throwOnError: true)!;
    var speedSession = Activator.CreateInstance(sessionType)!;
    var stateField = sessionType.GetField("State")!;
    stateField.SetValue(speedSession, Enum.ToObject(stateField.FieldType, 1));
    var actionType = classify.GetParameters()[1].ParameterType.GetElementType()!;
    var walkAction = Enum.Parse(actionType, "WALK");
    var checkSpeed = speedType.GetMethod("Check")!;
    Assert((bool)checkSpeed.Invoke(speed, new[] { speedSession, walkAction })!,
        "first speed sample was rejected");
    Assert(!(bool)checkSpeed.Invoke(speed, new[] { speedSession, walkAction })!,
        "packet below the configured action interval was accepted");
    Thread.Sleep(130);
    Assert((bool)checkSpeed.Invoke(speed, new[] { speedSession, walkAction })!,
        "packet above the configured action interval was rejected");

    var managerType = gameGate.GetType("GameGate.Core.SessionManager", throwOnError: true)!;
    var manager = Activator.CreateInstance(managerType, new object[] { 1 })!;
    var acquire = managerType.GetMethod("Acquire")!;
    var release = managerType.GetMethod("Release")!;
    var first = acquire.Invoke(manager, new object[] { "127.0.0.1", 1 })!;
    var sessionId = GetField<int>(first, "SessionId");
    var firstGeneration = GetField<long>(first, "Generation");
    Assert((bool)release.Invoke(manager, new object[] { sessionId, firstGeneration })!,
        "current session release failed");
    var second = acquire.Invoke(manager, new object[] { "127.0.0.1", 2 })!;
    var secondGeneration = GetField<long>(second, "Generation");
    Assert(secondGeneration > firstGeneration, "reused session slot did not advance generation");
    Assert(!(bool)release.Invoke(manager, new object[] { sessionId, firstGeneration })!,
        "stale session generation released a reused slot");
    Assert(GetProperty<int>(manager, "ActiveCount") == 1,
        "stale session release changed active session count");

    var delayedPacketType = gameGate.GetType("GameGate.Core.DelayedPacket", throwOnError: true)!;
    Assert(delayedPacketType.GetField("Generation") != null && delayedPacketType.GetField("IsUpstream") != null,
        "delayed packets are not bound to an upstream session generation");
    var gateServerType = gameGate.GetType("GameGate.Core.GateServer", throwOnError: true)!;
    Assert(gateServerType.GetMethod("ForwardDelayedUpstreamAsync", BindingFlags.Instance | BindingFlags.NonPublic) != null,
        "GameGate has no delayed upstream dispatcher");
    Assert(gateServerType.GetMethod("CleanupClientAsync", BindingFlags.Instance | BindingFlags.NonPublic) != null,
        "GameGate cleanup does not own the GM_CLOSE lifecycle");

    var gateManagerType = gameSvr.GetType("GameSvr.GateManager", throwOnError: true)!;
    var maxGateConnections = gateManagerType.GetField(
        "MaxGateConnections", BindingFlags.Static | BindingFlags.NonPublic)!;
    Assert((int)maxGateConnections.GetRawConstantValue()! == 5000,
        "GameSvr gate capacity is below the GameGate session ceiling");
    var socketServerType = systemModule.GetType("SystemModule.Sockets.ISocketServer", throwOnError: true)!;
    Assert(socketServerType.GetField("m_maxNumberAcceptedClients", BindingFlags.Instance | BindingFlags.NonPublic) != null &&
           socketServerType.GetField("m_numConnectedSockets", BindingFlags.Instance | BindingFlags.NonPublic) != null,
        "socket server capacity accounting fields are missing");

    var gameSocType = dbSvr.GetType("DBSvr.GameSocService", throwOnError: true)!;
    Assert(gameSocType.GetMethod("SendSaveResult", BindingFlags.Instance | BindingFlags.NonPublic) != null,
        "DBSvr character save has no same-query result path");
    Assert(gameSocType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .All(field => !field.FieldType.FullName!.StartsWith("System.Threading.Channels.Channel", StringComparison.Ordinal)),
        "DBSvr character save still acknowledges through an uncommitted channel queue");

    var dbLoginType = dbSvr.GetType("DBSvr.LoginSvrService", throwOnError: true)!;
    Assert(dbLoginType.GetNestedType("ServerReceiveState", BindingFlags.NonPublic) != null &&
           dbLoginType.GetMethod("ProcessSocketFrame", BindingFlags.Instance | BindingFlags.NonPublic) != null &&
           dbLoginType.GetMethod("SessionIPMatches", BindingFlags.Static | BindingFlags.NonPublic) != null,
        "DBSvr 5600 per-connection framing or admission IP validation is missing");
    var accountType = gameSvr.GetType("GameSvr.AccountService", throwOnError: true)!;
    Assert(accountType.GetMethod("ClearTimeoutSessions", BindingFlags.Instance | BindingFlags.NonPublic) != null &&
           accountType.GetMethod("SessionIPMatches", BindingFlags.Static | BindingFlags.NonPublic) != null &&
           accountType.GetMethod("SetServerInfo", BindingFlags.Instance | BindingFlags.NonPublic) != null,
        "GameSvr 5600 timeout, 103 dispatch, or admission IP validation is missing");

    void AssertClassified(ushort command, string expected)
    {
        var invokeArgs = new object?[] { command, null };
        Assert((bool)classify.Invoke(null, invokeArgs)!, $"current command {command} was not classified");
        Assert(invokeArgs[1]?.ToString() == expected,
            $"current command {command} classified as {invokeArgs[1]} instead of {expected}");
    }
}

static byte[] BuildFrame(int length, byte fill)
{
    var frame = Enumerable.Repeat(fill, length).ToArray();
    var header = BuildHeader(length);
    Buffer.BlockCopy(header, 0, frame, 0, header.Length);
    frame[^1] = (byte)'!';
    return frame;
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

static byte[] BuildHeader(int length)
{
    var header = new byte[5];
    header[0] = (byte)'#';
    BitConverter.GetBytes(length).CopyTo(header, 1);
    return header;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
