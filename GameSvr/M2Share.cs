using GameSvr.CommandSystem;
using GameSvr.Configs;
using GameSvr.Services;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public struct TItemBind
    {
        public int nMakeIdex;
        public int nItemIdx;
        public string sBindName;
    }

    public static class M2Share
    {
        #region 全局变量

        
        
        
        public static int nServerIndex = 0;
        
        
        
        public static int g_dwStartTick = 0;
        public static int ShareFileNameNum = 0;
        public static int g_nServerTickDifference = 0;
        /// <summary>
        /// Native <c>off_7D6EC8 -&gt; 0x7DCED4</c>. Startup clears this byte at
        /// 0x794293 and GM case 307 writes 1/0 at 0x627252/0x627282. The native
        /// M2 image has no other xref, so no downstream fountain behavior is
        /// inferred here.
        /// </summary>
        public static byte NativeFountSwitch = 0;
        public static ObjectManager ObjectManager = null;
        public static ServerConfig ServerConf = null;
        public static GameCmdConfig CommandConf = null;
        public static StringConfig StringConf = null;
        public static ExpsConfig ExpConf = null;
        public static GlobalConfig GlobalConf = null;
        public static ZhanshenConfig ZhanshenConf = null;
        
        
        
        public static TFindPath g_FindPath;
        
        
        
        public static CommandManager CommandSystem = null;
        public static LocalDB LocalDB = null;
        public static CommonDB CommonDB = null;
        internal static readonly WeaponUpgradeRepository WeaponUpgrades = new();
        public static MirLog LogSystem = null;
        public static RandomNumber RandomNumber = null;
        public static DBService DataServer = null;
        public static GameSvr.PasEngine.PasScriptHost PasEngine { get; set; }
        public static GameSvr.Plugins.PluginManager PluginManager { get; set; }
        public static GateManager GateManager = null;
        public static ArrayList LogStringList = null;
        public static ArrayList LogonCostLogList = null;
        public static MapManager MapManager = null;
        public static NativeDynamicRoomManager DynamicRoomManager = null;
        public static NativeDynamicRoomPasScriptRouteTable
            DynamicRoomPasRoutes = null;
        public static NativeDynamicRoomRuntime DynamicRoomRuntime = null;
        public static NativeDynamicRoomNpcOwner DynamicRoomNpcOwner = null;
        public static NativeDynamicRoomNpcMaterializer
            DynamicRoomNpcMaterializer = null;
        public static NativeDynamicRoomService DynamicRoomService = null;
        internal static NativeDropControlState NativeWorldDropControl { get; } =
            new(NativeDropControlBucketField.MonsterName);
        public static NativeMagicTowerRouteSequencer MagicTowerRouteSequencer = null;
        public static NativeActivityPointManager ActivityPointManager = null;
        public static NativeFastnessTable NativeFastnessUnionTable = new();
        public static NativeFastnessHqTable NativeFastnessHqTable = new();
        public static NativeFastnessTable NativeFastnessNearHitTable = new();
        public static NativeFastnessTable NativeFastnessMagicTable = new();
        public static NativeFastnessTable NativeFastnessSoulTable = new();
        public static GoldIDLoader GoldIDRewards = null;
        public static GoldActRewardLoader GoldActRewards = null;
        public static NativeNickPrizeManager NickPrizeManager = null;
        public static NativeConfigPrizeManager ConfigPrizeManager = null;
        public static NativeDiamondFoundry DiamondFoundry =
            NativeDiamondFoundry.Unavailable;
        public static NativeSecHeroPracticePrizeManager SecHeroPracticePrizeManager = null;
        public static NativeServerSwitchStore ServerSwitches =
            NativeServerSwitchStore.Unavailable;
        public static NativeSignActManager SignActManager = null;
        public static NativeFestivalConfig FestivalConfig = null;
        public static NativeSuperMerchantManager SuperMerchantManager = null;
        public static NativeNickLinFuState NickLinFuState = NativeNickLinFuState.Disabled;
        public static NativeCreditCardService CreditCardService = NativeCreditCardService.Disabled;
        internal static NativeCorpsService CorpsService = NativeCorpsService.Unavailable;
        public static NativeHonorValueManager HonorValueManager = null;
        public static NativeAuthenticationManager AuthenticationManager = null;
        public static ItemUnit ItemUnit = null;
        public static MagicManager MagicManager = null;
        public static NoticeManager NoticeManager = null;
        public static AssociationManager GuildManager = null;
        public static EventManager EventManager = null;
        public static CastleManager CastleManager = null;
        public static TFrontEngine FrontEngine = null;
        public static UserEngine UserEngine = null;
        public static Dictionary<string, IList<TMakeItem>> g_MakeItemList = null;
        public static IList<TStartPoint> StartPointList = null;
        public static IList<TSafeZoneArea> SafeZoneList = null;
        public static TStartPoint g_RedStartPoint = null;
        public static TRouteInfo[] ServerTableList = null;
        public static ConcurrentDictionary<string, long> g_DenySayMsgList = null;
        public static Dictionary<string, int> MiniMapList = null;
        public static Dictionary<int, string> g_UnbindList = null;
        public static IList<string> LineNoticeList = null;
        public static StringList AbuseTextList = null;
        
        
        
        public static Dictionary<string, IList<TMonSayMsg>> g_MonSayMsgList = null;
        
        
        
        public static IList<string> g_DisableMakeItemList = null;
        
        
        
        public static IList<string> g_EnableMakeItemList = null;
        
        
        
        public static IList<string> g_DisableMoveMapList = null;

        /// <summary>
        /// 传送石禁用地图 — the 定位石 (TFixedCoordStone) recall blacklist.
        /// 战神 keeps it in the TStringList behind <c>off_7D5918</c>, constructed at
        /// 0x792386-0x792399 (<c>sub_404660</c> ctor, then <c>xor edx,edx</c> ->
        /// <c>sub_428588</c> = <c>CaseSensitive := False</c>) and populated by
        /// LoadFromFile (VMT+0x68 = 0x42720C) at 0x794525, guarded by a FileExists
        /// (<c>sub_40CF2C</c> at 0x7944F5). The path is the shared Envir dir global
        /// <c>[0x7D6530]</c> concatenated with the literal at 0x794A3C
        /// (len 18, GBK <c>b4abcbcdcaafbdfbd3c3b5d8cdbc2e747874</c>).
        /// </summary>
        public static IList<string> g_FixedCoordDisableMapList = null;
        private static readonly object FixedCoordDisableMapSync = new object();
        
        
        
        public static IList<string> g_DisableSendMsgList = null;
        
        
        
        public static Dictionary<string, TMonDrop> g_MonDropLimitLIst = null;
        
        
        
        public static IList<string> g_DisableTakeOffList = null;
        private static readonly object ChatLogSync = new object();
        public static IList<string> g_ChatLoggingList = null;
        public static IList<TItemBind> g_ItemBindIPaddr = null;
        public static IList<TItemBind> g_ItemBindAccount = null;
        public static IList<TItemBind> g_ItemBindCharName = null;
        public static IList<string> g_UnMasterList = null;
        
        public static IList<string> g_UnForceMasterList = null;
        
        public static IList<string> g_GameLogItemNameList = null;
        
        public static bool g_boGameLogGold = false;
        public static bool g_boGameLogGameGold = false;
        public static bool g_boGameLogGamePoint = false;
        public static bool g_boGameLogHumanDie = false;
        public static IList<string> g_DenyIPAddrList = null;
        
        public static IList<string> g_DenyChrNameList = null;
        
        public static IList<string> g_DenyAccountList = null;
        
        public static IList<string> g_NoClearMonLIst = null;
        
        public static IList<string> g_NoHptoexpMonLIst = null;
        
        public static object LogMsgCriticalSection = null;
        public static object ProcessMsgCriticalSection = null;
        public static object UserDBSection = null;
        public static object ProcessHumanCriticalSection = null;
        public static int g_nTotalHumCount = 0;
        public static bool g_boMission = false;
        public static volatile bool g_boYbDoubleForge = false;
        public static string g_sMissionMap = string.Empty;
        public static short g_nMissionX = 0;
        public static short g_nMissionY = 0;
        public static bool boStartReady = false;
        public static bool boFilterWord = false;
        public static int g_nBaseObjTimeMin = 0;
        public static int g_nBaseObjTimeMax = 0;
        public static int g_nSockCountMin = 0;
        public static int g_nSockCountMax = 0;
        public static int g_nHumCountMin = 0;
        public static int g_nHumCountMax = 0;
        public static int dwUsrRotCountMin = 0;
        public static int dwUsrRotCountMax = 0;
        public static int g_dwUsrRotCountTick = 0;
        public static int g_nProcessHumanLoopTime = 0;
        public static int g_dwHumLimit = 30;
        public static int g_dwMonLimit = 30;
        public static int g_dwZenLimit = 5;
        public static int g_dwNpcLimit = 5;
        public static int g_dwSocLimit = 10;
        public static int g_dwSocCheckTimeOut = 50;
        public static int nDecLimit = 20;
        public static string sConfigPath = "";
        public static string sRootPath = ""; // 共享资源根路径 (Envir/Map/Notice 等比 sConfigPath 高一级)
        // Read from 战神 original Delphi !Setup.txt (GBK encoded)
        public const string sConfigFileName = "!Setup.txt";
        public const string sExpConfigFileName = "PlayerUpgradeExp.ini"; // in Share/
        public const string sCommandFileName = "Command.conf";
        public const string sGlobalConfigFileName = "ServerData.ini"; // in Share/
        public static int dwRunDBTimeMax = 0;
        public static int g_nGameTime = 0;
        public static NormNpc g_ManageNPC = null;
        public static Merchant g_FunctionNPC = null;
        public static Merchant g_PsFunctionNPC = null;
        public static Dictionary<string, TDynamicVar> g_DynamicVarList = null;
        public static char g_GMRedMsgCmd = '!';
        public static int g_nGMREDMSGCMD = 6;
        public static int g_dwSendOnlineTick = 0;
        public static readonly object HighStatLock = new object();
        public static object g_HighLevelHuman = null;
        public static object g_HighPKPointHuman = null;
        public static object g_HighDCHuman = null;
        public static object g_HighMCHuman = null;
        public static object g_HighSCHuman = null;
        public static object g_HighOnlineHuman = null;
        public static int g_dwSpiritMutinyTick = 0;
        public static GameSvrConfig g_Config = null;
        public static int[] g_dwOldNeedExps = new int[Grobal2.MAXCHANGELEVEL];
        public static TGameCommand g_GameCommand = new TGameCommand();
        public static string sClientSoftVersionError = "游戏版本错误!!!";
        public static string sDownLoadNewClientSoft = "请到网站上下载最新版本游戏客户端软件?";
        public static string sForceDisConnect = "连接被强行中?!!!";
        public static string sClientSoftVersionTooOld = "您现在使用的客户端软件版本太老了，大量的游戏效果新将无法使用?";
        public static string sDownLoadAndUseNewClient = "为了更好的进行游戏，请下载最新的客户端软?!!!";
        public static string sOnlineUserFull = "可允许的玩家数量已满";
        public static string sYouNowIsTryPlayMode = "你现在处于测试中，你可以在七级以前使用，但是会限制你的一些功?.";
        public static string g_sNowIsFreePlayMode = "当前服务器运行于测试模式.";
        public static string sAttackModeOfAll = "[攻击模式: 全体攻击]";
        public static string sAttackModeOfPeaceful = "[攻击模式: 和平攻击]";
        public static string sAttackModeOfDear = "[攻击模式: 夫妻攻击]";
        public static string sAttackModeOfMaster = "[攻击模式: 师徒攻击]";
        public static string sAttackModeOfGroup = "[攻击模式: 编组攻击]";
        public static string sAttackModeOfGuild = "[攻击模式: 行会攻击]";
        public static string sAttackModeOfHostile = "[攻击模式: 敌对攻击]";
        public static string sAttackModeOfCorps = "[攻击模式: 战队攻击]";
        public static string sAttackModeOfRedWhite = "[攻击模式: 红名攻击]";
        public static string sStartChangeAttackModeHelp = "使用组合快捷? CTRL-H 更改攻击模式...";
        public static string sStartNoticeMsg = "欢迎进入本服务器进行游戏...";
        public static string sThrustingOn = "启用刺杀剑法";
        public static string sThrustingOff = "关闭刺杀剑法";
        public static string sHalfMoonOn = "开启半月弯刀";
        public static string sHalfMoonOff = "关闭半月弯刀";
        public static string sTwinHitOn = "开启龙影剑?";
        public static string sTwinHitOff = "关闭龙影剑法";
        public static string sFireSpiritsSummoned = "召唤烈火精灵成功...";
        public static string sFireSpiritsFail = "召唤烈火精灵失败";
        public static string sSpiritsGone = "召唤烈火结束!!!";
        public static string sMateDoTooweak = "冲撞力不?!!!";
        public static string g_sTheWeaponBroke = "武器破碎!!!";
        public static string sTheWeaponRefineSuccessfull = "升级成功!!!";
        public static string sYouPoisoned = "中毒?!!!";
        // @Rest (dispatch index 27) receipts. Delphi long strings, length dword at ptr-4:
        //   0x62B8D8 len 14  CF C2 CA F4 D0 D0 B6 AF 3A 20 D0 DD CF A2  '下属行动: 休息'
        //   0x62B8F0 len 14  CF C2 CA F4 D0 D0 B6 AF 3A 20 B9 A5 BB F7  '下属行动: 攻击'
        //   0x62B908 len 14  B8 C3 B5 D8 CD BC CE DE B7 A8 CA B9 D3 C3  '该地图无法使用'
        // The separator is ASCII colon 0x3A + ASCII space 0x20, not a full-width colon.
        public static string sPetRest = "下属行动: 休息";
        public static string sPetAttack = "下属行动: 攻击";
        public static string sPetRestMapForbidden = "该地图无法使用";
        public static string sWearNotOfWoMan = "非女性用?!!!";
        public static string sWearNotOfMan = "非男性用?!!!";
        public static string sHandWeightNot = "腕力不够!!!";
        public static string sWearWeightNot = "负重力不?!!!";
        public static string g_sItemIsNotThisAccount = "此物品不为此帐号所?!!!";
        public static string g_sItemIsNotThisIPaddr = "此物品不为此IP所?!!!";
        public static string g_sItemIsNotThisCharName = "此物品不为你所?!!!";
        public static string g_sLevelNot = "等级不够!!!";
        public static string g_sJobOrLevelNot = "职业不对或等级不?!!!";
        public static string g_sJobOrDCNot = "职业不对或攻击力不够!!!";
        public static string g_sJobOrMCNot = "职业不对或魔法力不够!!!";
        public static string g_sJobOrSCNot = "职业不对或道术不?!!!";
        public static string g_sDCNot = "攻击力不?!!!";
        public static string g_sMCNot = "魔法力不?!!!";
        public static string g_sSCNot = "道术不够!!!";
        public static string g_sCreditPointNot = "声望点不?!!!";
        public static string g_sReNewLevelNot = "转生等级不够!!!";
        public static string g_sGuildNot = "加入了行会才可以使用此物?!!!";
        public static string g_sGuildMasterNot = "行会掌门才可以使用此物品!!!";
        public static string g_sSabukHumanNot = "沙城成员才可以使用此物品!!!";
        public static string g_sSabukMasterManNot = "沙城城主才可以使用此物品!!!";
        public static string g_sMemberNot = "会员才可以使用此物品!!!";
        public static string g_sMemberTypeNot = "指定类型的会员可以使用此物品!!!";
        public static string g_sCanottWearIt = "此物品不适使?!!!";
        public static string sCanotUseDrugOnThisMap = "此地图不允许使用任何药品!!!";
        public static string sGameMasterMode = "已进入管理员模式";
        public static string sReleaseGameMasterMode = "已退出管理员模式";
        public static string sObserverMode = "已进入隐身模?";
        public static string g_sReleaseObserverMode = "已退出隐身模?";
        public static string sSupermanMode = "已进入无敌模?";
        public static string sReleaseSupermanMode = "已退出无敌模?";
        public static string sYouFoundNothing = "未获取任何物?!!!";
        public static string g_sNoPasswordLockSystemMsg = "游戏密码保护系统还没有启?!!!";
        public static string g_sAlreadySetPasswordMsg = "仓库早已设置了一个密码，如需要修改密码请使用修改密码命令!!!";
        public static string g_sReSetPasswordMsg = "请重复输入一次仓库密码：";
        public static string g_sPasswordOverLongMsg = "输入的密码长度不正确!!!，密码长度必须在 4 - 7 的范围内，请重新设置密码?";
        public static string g_sReSetPasswordOKMsg = "密码设置成功!!，仓库已经自动上锁，请记好您的仓库密码，在取仓库时需要使用此密码开锁?";
        public static string g_sReSetPasswordNotMatchMsg = "二次输入的密码不一致，请重新设置密?!!!";
        public static string g_sPleaseInputUnLockPasswordMsg = "请输入仓库密码：";
        public static string g_sStorageUnLockOKMsg = "密码输入成功!!!，仓库已经开锁?";
        public static string g_sPasswordUnLockOKMsg = "密码输入成功!!!，密码系统已经开锁?";
        public static string g_sStorageAlreadyUnLockMsg = "仓库早已解锁!!!";
        public static string g_sStorageNoPasswordMsg = "仓库还没设置密码!!!";
        public static string g_sUnLockPasswordFailMsg = "密码输入错误!!!，请检查好再输入?";
        public static string g_sLockStorageSuccessMsg = "仓库加锁成功?";
        public static string g_sStoragePasswordClearMsg = "仓库密码已清?!!!";
        public static string g_sPleaseUnloadStoragePasswordMsg = "请先解锁密码再使用此命令清除密码!!!";
        public static string g_sStorageAlreadyLockMsg = "仓库早已加锁?!!!";
        public static string g_sStoragePasswordLockedMsg = "由于密码输入错误超过三次，仓库密码已被锁?!!!";
        public static string g_sSetPasswordMsg = "请输入一个长度为 4 - 7 位的仓库密码: ";
        public static string g_sPleaseInputOldPasswordMsg = "请输入原仓库密码: ";
        public static string g_sOldPasswordIsClearMsg = "密码已清除?";
        public static string g_sPleaseUnLockPasswordMsg = "请先解锁仓库密码后再用此命令清除密码!!!";
        public static string g_sNoPasswordSetMsg = "仓库还没设置密码，请用设置密码命令设置仓库密?!!!";
        public static string g_sOldPasswordIncorrectMsg = "输入的原仓库密码不正?!!!";
        public static string g_sStorageIsLockedMsg = "仓库已被加锁，请先输入仓库正确的开锁密码，再取物品!!!";
        public static string g_sActionIsLockedMsg = "你当前已启用密码保护系统，请先输入正确的密码，才可以正常游戏!!!";
        public static string g_sPasswordNotSetMsg = "对不起，没有设置仓库密码此功能无法使用，设置仓库密码请输入指? @{0}";
        public static string g_sNotPasswordProtectMode = "你正处于非保护模式，如想你的装备更加安全，请输入指令 @{0}";
        public static string g_sCanotDropGoldMsg = "太少的金币不允许扔在地上!!!";
        public static string g_sCanotDropInSafeZoneMsg = "安全区不允许扔东西在地上!!!";
        public static string g_sCanotDropItemMsg = "当前无法进行此操?!!!";
        public static string g_sCanotUseItemMsg = "当前无法进行此操?!!!";
        public static string g_sCanotTryDealMsg = "当前无法进行此操?!!!";
        public static string g_sPleaseTryDealLaterMsg = "请稍候再交易!!!";
        public static string g_sDealItemsDenyGetBackMsg = "交易的金币或物品不可以取回，要取回请取消再重新交?!!!";
        public static string g_sDisableDealItemsMsg = "交易功能暂时关闭!!!";
        public static string g_sDealActionCancelMsg = "交易取消!!!";
        public static string g_sPoseDisableDealMsg = "对方拒绝和你交易。";
        public static string g_sDealSuccessMsg = "交易成功...";
        public static string g_sDealOKTooFast = "过早按了成交按钮?";
        public static string g_sYouDealOKMsg = "你已经确认交易了?";
        public static string g_sPoseDealOKMsg = "对方已经确认交易了?";
        public static string g_sKickClientUserMsg = "请不要使用非法外挂软?!!!";
        public static string g_sStartMarryManMsg = "[{0}]: {1} ? {2} 的婚礼现在开?...";
        public static string g_sStartMarryWoManMsg = "[{0}]: {1} ? {2} 的婚礼现在开?...";
        public static string g_sStartMarryManAskQuestionMsg = "[{0}]: {1} 你愿意娶 {2} 小姐为妻，并照顾她一生一世吗?";
        public static string g_sStartMarryWoManAskQuestionMsg = "[{0}]: {1} 你愿意娶 {2} 小姐为妻，并照顾她一生一世吗?";
        public static string g_sMarryManAnswerQuestionMsg = "[{0}]: 我愿?!!!，{1} 小姐我会尽我一生的时间来照顾您，让您过上快乐美满的日子的?";
        public static string g_sMarryManAskQuestionMsg = "[{0}]: {1} 你愿意嫁? {2} 先生为妻，并照顾他一生一世吗?";
        public static string g_sMarryWoManAnswerQuestionMsg = "[{0}]: 我愿?!!!，{2} 先生我愿意让你来照顾我，保护我?";
        public static string g_sMarryWoManGetMarryMsg = "[{0}]: 我宣? {1} 先生? {2} 小姐正式成为合法夫妻?";
        public static string g_sMarryWoManDenyMsg = "[{0}]: {1} 你这个好色之徒，谁会愿意嫁给你呀!!!，癞蛤蟆想吃天鹅肉?";
        public static string g_sMarryWoManCancelMsg = "[{0}]: 真是可惜，二个人这个时候才翻脸，你们培养好感情后再来找我吧!!!";
        public static string g_sfUnMarryManLoginMsg = "你的老婆{0}已经强行与你脱离了夫妻关系了!!!";
        public static string g_sfUnMarryWoManLoginMsg = "你的老公{0}已经强行与你脱离了夫妻关系了!!!";
        public static string g_sManLoginDearOnlineSelfMsg = "你的老婆{0}当前位于{1}({2}:{3})?";
        public static string g_sManLoginDearOnlineDearMsg = "你的老公{0}?:{1}({2}:{3})上线?!!!?";
        public static string g_sWoManLoginDearOnlineSelfMsg = "你的老公当前位于{0}({1}:{2})?";
        public static string g_sWoManLoginDearOnlineDearMsg = "你的老婆{0}?:{1}({2}:{3}) 上线?!!!?";
        public static string g_sManLoginDearNotOnlineMsg = "你的老婆现在不在?!!!";
        public static string g_sWoManLoginDearNotOnlineMsg = "你的老公现在不在?!!!";
        public static string g_sManLongOutDearOnlineMsg = "你的老公?:{0}({1}:{2})下线?!!!?";
        public static string g_sWoManLongOutDearOnlineMsg = "你的老婆?:{0}({1}:{2})下线?!!!?";
        public static string g_sYouAreNotMarryedMsg = "你都没结婚查什么？";
        public static string g_sYourWifeNotOnlineMsg = "你的老婆还没有上?!!!";
        public static string g_sYourHusbandNotOnlineMsg = "你的老公还没有上?!!!";
        public static string g_sYourWifeNowLocateMsg = "你的老婆现在位于:";
        public static string g_sYourHusbandSearchLocateMsg = "你的老公正在找你，他现在位于:";
        public static string g_sYourHusbandNowLocateMsg = "你的老公现在位于:";
        public static string g_sYourWifeSearchLocateMsg = "你的老婆正在找你，他现在位于:";
        public static string g_sfUnMasterLoginMsg = "你的徒弟{0}已经背判师门?!!!";
        public static string g_sfUnMasterListLoginMsg = "你的师父{0}已经将你逐出师门?!!!";
        public static string g_sMasterListOnlineSelfMsg = "你的师父{0}当前位于{1}({2}:{3})?";
        public static string g_sMasterListOnlineMasterMsg = "你的徒弟{0}?:{1}({2}:{3})上线?!!!?";
        public static string g_sMasterOnlineSelfMsg = "你的徒弟当前位于{0}({1}:{2})?";
        public static string g_sMasterOnlineMasterListMsg = "你的师父{0}?:{1}({2}:{3}) 上线?!!!?";
        public static string g_sMasterLongOutMasterListOnlineMsg = "你的师父?:{0}({1}:{2})下线?!!!?";
        public static string g_sMasterListLongOutMasterOnlineMsg = "你的徒弟{0}?:{1}({2}:{3})下线?!!!?";
        public static string g_sMasterListNotOnlineMsg = "你的师父现不在线!!!";
        public static string g_sMasterNotOnlineMsg = "你的徒弟现不在线!!!";
        public static string g_sYouAreNotMasterMsg = "你都没师徒关系查什么？";
        public static string g_sYourMasterNotOnlineMsg = "你的师父还没有上?!!!";
        public static string g_sYourMasterListNotOnlineMsg = "你的徒弟还没有上?!!!";
        public static string g_sYourMasterNowLocateMsg = "你的师父现在位于:";
        public static string g_sYourMasterListSearchLocateMsg = "你的徒弟正在找你，他现在位于:";
        public static string g_sYourMasterListNowLocateMsg = "你的徒弟现在位于:";
        public static string g_sYourMasterSearchLocateMsg = "你的师父正在找你，他现在位于:";
        public static string g_sYourMasterListUnMasterOKMsg = "你的徒弟{0}已经圆满出师?!!!";
        public static string g_sYouAreUnMasterOKMsg = "你已经出师了!!!";
        public static string g_sUnMasterLoginMsg = "你的一个徒弟已经圆满出师了!!!";
        public static string g_sNPCSayUnMasterOKMsg = "[{0}]: 我宣布{1}与{2}正式脱离师徒关系?";
        public static string g_sNPCSayForceUnMasterMsg = "[{0}]: 我宣布{1}与{2}已经正式脱离师徒关系!!!";
        public static string g_sMyInfo = string.Empty;
        public static string g_sSendOnlineCountMsg = "当前在线人数: {0}";
        public static string g_sOpenedDealMsg = "开始交易?";
        public static string g_sSendCustMsgCanNotUseNowMsg = "祝福语功能还没有开?!!!";
        public static string g_sSubkMasterMsgCanNotUseNowMsg = "城主发信息功能还没有开?!!!";
        public static string g_sWeaponRepairSuccess = "武器修复成功...";
        public static string g_sDefenceUpTime = "防御力增加{0}?";
        public static string g_sMagDefenceUpTime = "魔法防御力增加{0}?";
        public static string g_sAttPowerUpTime = "物理攻击力增加{0}分钟{1}? ";
        public static string g_sAttPowerDownTime = "物理攻击力减少了{0}分钟{1}?";
        public static string g_sWinLottery1Msg = "祝贺您，中了一等奖?";
        public static string g_sWinLottery2Msg = "祝贺您，中了二等奖?";
        public static string g_sWinLottery3Msg = "祝贺您，中了三等奖?";
        public static string g_sWinLottery4Msg = "祝贺您，中了四等奖?";
        public static string g_sWinLottery5Msg = "祝贺您，中了五等奖?";
        public static string g_sWinLottery6Msg = "祝贺您，中了六等奖?";
        public static string g_sNotWinLotteryMsg = "等下次机会吧!!!";
        public static string g_sWeaptonMakeLuck = "武器被加幸运?...";
        public static string g_sWeaptonNotMakeLuck = "无效!!!";
        public static string g_sTheWeaponIsCursed = "你的武器被诅咒了!!!";
        public static string g_sCanotTakeOffItem = "无法取下物品!!!";
        public static string g_sJoinGroup = "{0} 已加入小?.";
        public static string g_sTryModeCanotUseStorage = "试玩模式不可以使用仓库功?!!!";
        public static string g_sCanotGetItems = "无法携带更多的东?!!!";
        public static string g_sEnableDearRecall = "允许夫妻传?!!!";
        public static string g_sDisableDearRecall = "禁止夫妻传?!!!";
        public static string g_sEnableMasterRecall = "允许师徒传?!!!";
        public static string g_sDisableMasterRecall = "禁止师徒传?!!!";
        public static string g_sNowCurrDateTime = "当前日期时间: ";

        // ---- sub_6CCBC4, the 10s timed-buff decrement pass -------------------
        // Three Delphi long-string literals, decoded with the length word at
        // EA-4 (the byte AT the EA is data, not a length prefix):
        //   0x6CCCE0 len=4  "您的"            -- part 1 of the 3-part concat
        //   0x6CCCF0 len=14 "倍经验时间结束"  -- part 3, part 2 is IntToStr(mult)
        //   0x6CCD08 len=16 "您的真视时间结束"
        // Both are emitted with cx=0xFCFF (0x6CCC52 / 0x6CCC7E), i.e. FColor 0xFF
        // and BColor 0xFC == MsgColor.Blue -- NOT Red. The obj+0xBD4 block at
        // 0x6CCC91 has no message at all, so there is no third literal here.
        public static string g_sNativeExpBuffExpiredPrefix = "您的";
        public static string g_sNativeExpBuffExpiredSuffix = "倍经验时间结束";
        public static string g_sNativeTrueSightExpired = "您的真视时间结束";

        // TDoubleExpProp.Use (sub_786390) message literals. Long-string length
        // dwords sit at EA-4, so these are decoded from the exact byte runs:
        //   0x78653C len=30 -- multiplier-conflict refusal, colour 0x38FF
        //   0x786598 len=26 -- success notice,              colour 0xFCFF
        public static string g_sNativeExpBuffConflict = "您处于%d倍经验状态，不能使用%s";
        public static string g_sNativeExpBuffGranted = "您获得了%d小时%d倍经验时间";

        // 0x786564 len=40 -- the 网吧 refusal in TDoubleExpProp.Use, colour 0x38FF.
        // No format specifiers, so it is sent verbatim.
        public static string g_sNativeExpBuffNetCafeRefusal =
            "您已经享受特权网吧双倍卷轴，不需再使用！";

        // 0x62D204 len=31 (length dword at 0x62D200) -- the only message GM
        // dispatch index 401 (handler 0x6281AE) can emit, on the missing-file leg
        // at 0x628204 with cx=0x38FF. Success is silent. No format specifiers.
        public static string g_sNativeFixedCoordDisableMapMissing =
            "传送石禁用地图.txt 文件不存在！";
        public static string g_sEnableHearWhisper = "[允许私聊]";
        public static string g_sDisableHearWhisper = "[禁止私聊]";
        public static string g_sEnableShoutMsg = "[允许群聊]";
        public static string g_sDisableShoutMsg = "[禁止群聊]";
        public static string g_sEnableDealMsg = "[允许交易]";
        public static string g_sDisableDealMsg = "[禁止交易]";
        public static string g_sEnableGuildChat = "[允许行会聊天]";
        public static string g_sDisableGuildChat = "[禁止行会聊天]";
        public static string g_sEnableJoinGuild = "[允许加入行会]";
        public static string g_sDisableJoinGuild = "[禁止加入行会]";
        public static string g_sEnableAuthAllyGuild = "[允许行会联盟]";
        public static string g_sDisableAuthAllyGuild = "[禁止行会联盟]";
        public static string g_sEnableGroupRecall = "[允许天地合一]";
        public static string g_sDisableGroupRecall = "[禁止天地合一]";
        public static string g_sEnableGuildRecall = "[允许行会合一]";
        public static string g_sDisableGuildRecall = "[禁止行会合一]";
        public static string g_sPleaseInputPassword = "请输入密?:";
        public static string g_sTheMapDisableMove = "地图{0}({1})不允许传?!!!";
        public static string g_sTheMapNotFound = "{0} 此地图号不存?!!!";
        public static string g_sYourIPaddrDenyLogon = "你当前登录的IP地址已被禁止登录?!!!";
        public static string g_sYourAccountDenyLogon = "你当前登录的帐号已被禁止登录?!!!";
        public static string g_sYourCharNameDenyLogon = "你当前登录的人物已被禁止登录?!!!";
        public static string g_sCanotPickUpItem = "在一定时间以内无法捡起此物品!!!";
        public static string g_sQUERYBAGITEMS = "一定时间内不能连续刷新背包物品...";
        public static string g_sCanotSendmsg = "无法发送信?.";
        public static string g_sUserDenyWhisperMsg = " 拒绝私聊!!!";
        public static string g_sUserNotOnLine = "  没有在线!!!";
        public static string g_sRevivalRecoverMsg = "复活戒指生效，体力恢?.";
        public static string g_sClientVersionTooOld = "由于您使用的客户端版本太老了，无法正确显示人物信?!!!";
        public static string g_sCastleGuildName = "(%castlename)%guildname[%rankname]";
        public static string g_sNoCastleGuildName = "%guildname[%rankname]";
        public static string g_sWarrReNewName = "%chrname\\*<?>*";
        public static string g_sWizardReNewName = "%chrname\\*<?>*";
        public static string g_sTaosReNewName = "%chrname\\*<?>*";
        public static string g_sRankLevelName = "{0}\\平民";
        public static string g_sManDearName = "{0}的老公";
        public static string g_sWoManDearName = "{0}的妻?";
        public static string g_sMasterName = "{0}的师?";
        public static string g_sNoMasterName = "{0}的徒?";
        public static string g_sHumanShowName = "%chrname\\%guildname\\%dearname\\%mastername";
        public static string g_sChangePermissionMsg = "当前权限等级?:{0}";
        public static string g_sChangeKillMonExpRateMsg = "经验倍数:{0} 时长{1}?";
        public static string g_sChangePowerRateMsg = "攻击力倍数:{0} 时长{1}?";
        public static string g_sChangeMemberLevelMsg = "当前会员等级?:{0}";
        public static string g_sChangeMemberTypeMsg = "当前会员类型?:{0}";
        public static string g_sScriptChangeHumanHPMsg = "当前HP值为:{0}";
        public static string g_sScriptChangeHumanMPMsg = "当前MP值为:{0}";
        public static string g_sScriptGuildAuraePointNoGuild = "你还没加入行?!!!";
        public static string g_sScriptGuildAuraePointMsg = "你的行会人气度为:{0}";
        public static string g_sScriptGuildBuildPointNoGuild = "你还没加入行?!!!";
        public static string g_sScriptGuildBuildPointMsg = "你的行会的建筑度?:{0}";
        public static string g_sScriptGuildFlourishPointNoGuild = "你还没加入行?!!!";
        public static string g_sScriptGuildFlourishPointMsg = "你的行会的繁荣度?:{0}";
        public static string g_sScriptGuildStabilityPointNoGuild = "你的行会的建筑度?:{0}";
        public static string g_sScriptGuildStabilityPointMsg = "你的行会的安定度?:{0}";
        public static string g_sScriptChiefItemCountMsg = "你的行会的超级装备数?:{0}";
        public static string g_sDisableSayMsg = "[由于你重复发相同的内容，{0}分钟内你将被禁止发言...]";
        public static string g_sOnlineCountMsg = "在线?: {0}";
        public static string g_sTotalOnlineCountMsg = "总在线数: {0}";
        public static string g_sYouNeedLevelMsg = "你的等级要在{0}级以上才能用此功?!!!";
        public static string g_sThisMapDisableSendCyCyMsg = "本地图禁止喊话";
        public static string g_sYouCanSendCyCyLaterMsg = "{0}秒后才可以再发文?!!!";
        public static string g_sYouIsDisableSendMsg = "禁止聊天!!!";
        public static string g_sYouMurderedMsg = "你犯了谋杀?!!!";
        public static string g_sYouKilledByMsg = "你被{0}杀害了!!!";
        public static string g_sYouProtectedByLawOfDefense = "[你受到正当规则保护。]";
        public static string g_sYourUseItemIsNul = "你的{0}处没有放上装?!!!";

        public static string g_sVersionDate = "1.0";
        public const string g_sVersion = "引擎版本: 1.00 Build 20161001";
        public const string g_sUpDateTime = "更新日期: 2016/10/01";

        private const string sSTATUS_FAIL = "+FL/{0}";
        private const string sSTATUS_GOOD = "+GD/{0}";

        // 战神 三处等级上限：0x69079C / 0x6C04DB / 0x6C051C 均为 cmp word [+0x278],0x3E7
        // 0x3E7 = 999；C# 原值 500 无原版依据。
        public const int MAXUPLEVEL = 999;
        public const int MAXHUMPOWER = 1000;
        public const int BODYLUCKUNIT = 5000;
        public const int HAM_ALL = 0;
        public const int HAM_PEACE = 1;
        public const int HAM_DEAR = 2;
        public const int HAM_MASTER = 3;
        public const int HAM_GROUP = 4;
        public const int HAM_GUILD = 5;
        
        
        
        public const int HAM_PKATTACK = 6;
        public const int DEFHIT = 5;
        public const int DEFSPEED = 15;
        public const int jWarr = 0;
        public const int jWizard = 1;
        public const int jTaos = 2;
        public const int SIZEOFTHUMAN = 3588;
        public const int MONSTER_SANDMOB = 3;
        public const int MONSTER_ROCKMAN = 4;
        public const int MONSTER_RON = 9;
        public const int MONSTER_MINORNUMA = 18;
        public const int ARCHER_POLICE = 20;
        public const int SUPREGUARD = 11;
        public const int PETSUPREGUARD = 12;
        public const int ANIMAL_CHICKEN = 51;
        public const int ANIMAL_DEER = 52;
        public const int ANIMAL_WOLF = 53;
        public const int TRAINER = 55;
        public const int MONSTER_OMA = 80;
        public const int MONSTER_OMAKNIGHT = 81;
        public const int MONSTER_SPITSPIDER = 82;
        public const int MONSTER_STICK = 85;
        public const int MONSTER_DUALAXE = 87;
        public const int MONSTER_THONEDARK = 93;
        public const int MONSTER_LIGHTZOMBI = 94;
        public const int MONSTER_DIGOUTZOMBI = 95;
        public const int MONSTER_ZILKINZOMBI = 96;
        public const int MONSTER_WHITESKELETON = 100;
        public const int MONSTER_BEEQUEEN = 103;
        public const int MONSTER_BEE = 125;
        public const int MONSTER_MAGUNGSA = 143;
        public const int MONSTER_SCULTURE = 101;
        public const int MONSTER_SCULTUREKING = 102;
        public const int MONSTER_ARCHERGUARD = 112;
        public const int MONSTER_ELFMONSTER = 113;
        public const int MONSTER_ELFWARRIOR = 114;
        public const string sMAN = "MAN";
        public const string sSUNRAISE = "SUNRAISE";
        public const string sDAY = "DAY";
        public const string sSUNSET = "SUNSET";
        public const string sNIGHT = "NIGHT";
        public const string sWarrior = "Warrior";
        public const string sWizard = "Wizard";
        public const string sTaos = "Taoist";
        public const string sSUN = "SUN";
        public const string sMON = "MON";
        public const string sTUE = "TUE";
        public const string sWED = "WED";
        public const string sTHU = "THU";
        public const string sFRI = "FRI";
        public const string sSAT = "SAT";
        
        public const string sCHECK = "CHECK";
        public const int nCHECK = 1;
        public const string sRANDOM = "RANDOM";
        public const int nRANDOM = 2;
        public const string sGENDER = "GENDER";
        public const int nGENDER = 3;
        public const string sDAYTIME = "DAYTIME";
        public const int nDAYTIME = 4;
        public const string sCHECKOPEN = "CHECKOPEN";
        public const int nCHECKOPEN = 5;
        public const string sCHECKUNIT = "CHECKUNIT";
        public const int nCHECKUNIT = 6;
        public const string sCHECKLEVEL = "CHECKLEVEL";
        public const int nCHECKLEVEL = 7;
        public const string sCHECKJOB = "CHECKJOB";
        public const int nCHECKJOB = 8;
        public const string sCHECKBBCOUNT = "CHECKBBCOUNT";
        public const int nCHECKBBCOUNT = 9;
        public const string sCHECKITEM = "CHECKITEM";
        public const int nCHECKITEM = 20;
        public const string sCHECKITEMW = "CHECKITEMW";
        public const int nCHECKITEMW = 21;
        public const string sCHECKGOLD = "CHECKGOLD";
        public const int nCHECKGOLD = 22;
        public const string sISTAKEITEM = "ISTAKEITEM";
        public const int nISTAKEITEM = 23;
        public const string sCHECKDURA = "CHECKDURA";
        public const int nCHECKDURA = 24;
        public const string sCHECKDURAEVA = "CHECKDURAEVA";
        public const int nCHECKDURAEVA = 25;
        public const string sDAYOFWEEK = "DAYOFWEEK";
        public const int nDAYOFWEEK = 26;
        public const string sHOUR = "HOUR";
        public const int nHOUR = 27;
        public const string sMIN = "MIN";
        public const int nMIN = 28;
        public const string sCHECKPKPOINT = "CHECKPKPOINT";
        public const int nCHECKPKPOINT = 29;
        public const string sCHECKLUCKYPOINT = "CHECKLUCKYPOINT";
        public const int nCHECKLUCKYPOINT = 30;
        public const string sCHECKMONMAP = "CHECKMONMAP";
        public const int nCHECKMONMAP = 31;
        public const string sCHECKMONAREA = "CHECKMONAREA";
        public const int nCHECKMONAREA = 32;
        public const string sCHECKHUM = "CHECKHUM";
        public const int nCHECKHUM = 33;
        public const string sCHECKBAGGAGE = "CHECKBAGGAGE";
        public const int nCHECKBAGGAGE = 34;
        public const string sEQUAL = "EQUAL";
        public const int nEQUAL = 35;
        public const string sLARGE = "LARGE";
        public const int nLARGE = 36;
        public const string sSMALL = "SMALL";
        public const int nSMALL = 37;
        public const string sSC_CHECKMAGIC = "CHECKMAGIC";
        public const int nSC_CHECKMAGIC = 38;
        public const string sSC_CHKMAGICLEVEL = "CHKMAGICLEVEL";
        public const int nSC_CHKMAGICLEVEL = 39;
        public const string sSC_CHECKMONRECALL = "CHECKMONRECALL";
        public const int nSC_CHECKMONRECALL = 40;
        public const string sSC_CHECKHORSE = "CHECKHORSE";
        public const int nSC_CHECKHORSE = 41;
        public const string sSC_CHECKRIDING = "CHECKRIDING";
        public const int nSC_CHECKRIDING = 42;
        public const string sSC_STARTDAILYQUEST = "STARTDAILYQUEST";
        public const int nSC_STARTDAILYQUEST = 45;
        public const string sSC_CHECKDAILYQUEST = "CHECKDAILYQUEST";
        public const int nSC_CHECKDAILYQUEST = 46;
        public const string sSC_RANDOMEX = "RANDOMEX";
        public const int nSC_RANDOMEX = 47;
        public const string sCHECKNAMELIST = "CHECKNAMELIST";
        public const int nCHECKNAMELIST = 48;
        public const string sSC_CHECKWEAPONLEVEL = "CHECKWEAPONLEVEL";
        public const int nSC_CHECKWEAPONLEVEL = 49;
        public const string sSC_CHECKWEAPONATOM = "CHECKWEAPONATOM";
        public const int nSC_CHECKWEAPONATOM = 50;
        public const string sSC_CHECKREFINEWEAPON = "CHECKREFINEWEAPON";
        public const int nSC_CHECKREFINEWEAPON = 51;
        public const string sSC_CHECKWEAPONMCTYPE = "CHECKWEAPONMCTYPE";
        public const int nSC_CHECKWEAPONMCTYPE = 52;
        public const string sSC_CHECKREFINEITEM = "CHECKREFINEITEM";
        public const int nSC_CHECKREFINEITEM = 53;
        public const string sSC_HASWEAPONATOM = "HASWEAPONATOM";
        public const int nSC_HASWEAPONATOM = 54;
        public const string sSC_ISGUILDMASTER = "ISGUILDMASTER";
        public const int nSC_ISGUILDMASTER = 55;
        public const string sSC_CANPROPOSECASTLEWAR = "CANPROPOSECASTLEWAR";
        public const int nSC_CANPROPOSECASTLEWAR = 56;
        public const string sSC_CANHAVESHOOTER = "CANHAVESHOOTER";
        public const int nSC_CANHAVESHOOTER = 57;
        public const string sSC_CHECKFAME = "CHECKFAME";
        public const int nSC_CHECKFAME = 58;
        public const string sSC_ISONCASTLEWAR = "ISONCASTLEWAR";
        public const int nSC_ISONCASTLEWAR = 59;
        public const string sSC_ISONREADYCASTLEWAR = "ISONREADYCASTLEWAR";
        public const int nSC_ISONREADYCASTLEWAR = 60;
        public const string sSC_ISCASTLEGUILD = "ISCASTLEGUILD";
        public const int nSC_ISCASTLEGUILD = 61;
        public const string sSC_ISATTACKGUILD = "ISATTACKGUILD";
        
        public const int nSC_ISATTACKGUILD = 63;
        public const string sSC_ISDEFENSEGUILD = "ISDEFENSEGUILD";
        
        public const int nSC_ISDEFENSEGUILD = 65;
        public const string sSC_CHECKSHOOTER = "CHECKSHOOTER";
        public const int nSC_CHECKSHOOTER = 66;
        public const string sSC_CHECKSAVEDSHOOTER = "CHECKSAVEDSHOOTER";
        public const int nSC_CHECKSAVEDSHOOTER = 67;
        public const string sSC_HASGUILD = "HAVEGUILD";
        
        public const int nSC_HASGUILD = 68;
        public const string sSC_CHECKCASTLEDOOR = "CHECKCASTLEDOOR";
        
        public const int nSC_CHECKCASTLEDOOR = 69;
        public const string sSC_CHECKCASTLEDOOROPEN = "CHECKCASTLEDOOROPEN";
        
        public const int nSC_CHECKCASTLEDOOROPEN = 70;
        public const string sSC_CHECKPOS = "CHECKPOS";
        public const int nSC_CHECKPOS = 71;
        public const string sSC_CANCHARGESHOOTER = "CANCHARGESHOOTER";
        public const int nSC_CANCHARGESHOOTER = 72;
        public const string sSC_ISATTACKALLYGUILD = "ISATTACKALLYGUILD";
        
        public const int nSC_ISATTACKALLYGUILD = 73;
        public const string sSC_ISDEFENSEALLYGUILD = "ISDEFENSEALLYGUILD";
        
        public const int nSC_ISDEFENSEALLYGUILD = 74;
        public const string sSC_TESTTEAM = "TESTTEAM";
        public const int nSC_TESTTEAM = 75;
        public const string sSC_ISSYSOP = "ISSYSOP";
        public const int nSC_ISSYSOP = 76;
        public const string sSC_ISADMIN = "ISADMIN";
        public const int nSC_ISADMIN = 77;
        public const string sSC_CHECKBONUS = "CHECKBONUS";
        public const int nSC_CHECKBONUS = 78;
        public const string sSC_CHECKMARRIAGE = "CHECKMARRIAGE";
        public const int nSC_CHECKMARRIAGE = 79;
        public const string sSC_CHECKMARRIAGERING = "CHECKMARRIAGERING";
        public const int nSC_CHECKMARRIAGERING = 80;
        public const string sSC_CHECKGMETERM = "CHECKGMETERM";
        public const int nSC_CHECKGMETERM = 100;
        public const string sSC_CHECKOPENGME = "CHECKOPENGME";
        public const int nSC_CHECKOPENGME = 101;
        public const string sSC_CHECKENTERGMEMAP = "CHECKENTERGMEMAP";
        public const int nSC_CHECKENTERGMEMAP = 102;
        public const string sSC_CHECKSERVER = "CHECKSERVER";
        public const int nSC_CHECKSERVER = 103;
        public const string sSC_ELARGE = "ELARGE";
        public const int nSC_ELARGE = 104;
        public const string sSC_ESMALL = "ESMALL";
        public const int nSC_ESMALL = 105;
        public const string sSC_CHECKGROUPCOUNT = "CHECKGROUPCOUNT";
        public const int nSC_CHECKGROUPCOUNT = 106;
        public const string sSC_CHECKACCESSORY = "CHECKACCESSORY";
        public const int nSC_CHECKACCESSORY = 107;
        public const string sSC_ONERROR = "ONERROR";
        public const int nSC_ONERROR = 108;
        public const string sSC_CHECKARMOR = "CHECKARMOR";
        public const int nSC_CHECKARMOR = 109;
        public const string sCHECKACCOUNTLIST = "CHECKACCOUNTLIST";
        public const int nCHECKACCOUNTLIST = 135;
        public const string sCHECKIPLIST = "CHECKIPLIST";
        public const int nCHECKIPLIST = 136;
        public const string sCHECKCREDITPOINT = "CHECKCREDITPOINT";
        public const int nCHECKCREDITPOINT = 137;
        public const string sSC_CHECKPOSEDIR = "CHECKPOSEDIR";
        public const int nSC_CHECKPOSEDIR = 138;
        public const string sSC_CHECKPOSELEVEL = "CHECKPOSELEVEL";
        public const int nSC_CHECKPOSELEVEL = 139;
        public const string sSC_CHECKPOSEGENDER = "CHECKPOSEGENDER";
        public const int nSC_CHECKPOSEGENDER = 140;
        public const string sSC_CHECKLEVELEX = "CHECKLEVELEX";
        public const int nSC_CHECKLEVELEX = 141;
        public const string sSC_CHECKBONUSPOINT = "CHECKBONUSPOINT";
        public const int nSC_CHECKBONUSPOINT = 142;
        public const string sSC_CHECKMARRY = "CHECKMARRY";
        public const int nSC_CHECKMARRY = 143;
        public const string sSC_CHECKPOSEMARRY = "CHECKPOSEMARRY";
        public const int nSC_CHECKPOSEMARRY = 144;
        public const string sSC_CHECKMARRYCOUNT = "CHECKMARRYCOUNT";
        public const int nSC_CHECKMARRYCOUNT = 145;
        public const string sSC_CHECKMASTER = "CHECKMASTER";
        public const int nSC_CHECKMASTER = 146;
        public const string sSC_HAVEMASTER = "HAVEMASTER";
        public const int nSC_HAVEMASTER = 147;
        public const string sSC_CHECKPOSEMASTER = "CHECKPOSEMASTER";
        public const int nSC_CHECKPOSEMASTER = 148;
        public const string sSC_POSEHAVEMASTER = "POSEHAVEMASTER";
        public const int nSC_POSEHAVEMASTER = 149;
        public const string sSC_CHECKISMASTER = "CHECKPOSEISMASTER";
        public const int nSC_CHECKISMASTER = 150;
        public const string sSC_CHECKPOSEISMASTER = "CHECKISMASTER";
        public const int nSC_CHECKPOSEISMASTER = 151;
        public const string sSC_CHECKNAMEIPLIST = "CHECKNAMEIPLIST";
        public const int nSC_CHECKNAMEIPLIST = 152;
        public const string sSC_CHECKACCOUNTIPLIST = "CHECKACCOUNTIPLIST";
        public const int nSC_CHECKACCOUNTIPLIST = 153;
        public const string sSC_CHECKSLAVECOUNT = "CHECKSLAVECOUNT";
        public const int nSC_CHECKSLAVECOUNT = 154;
        public const string sSC_CHECKCASTLEMASTER = "ISCASTLEMASTER";
        public const int nSC_CHECKCASTLEMASTER = 155;
        public const string sSC_ISNEWHUMAN = "ISNEWHUMAN";
        public const int nSC_ISNEWHUMAN = 156;
        public const string sSC_CHECKMEMBERTYPE = "CHECKMEMBERTYPE";
        public const int nSC_CHECKMEMBERTYPE = 157;
        public const string sSC_CHECKMEMBERLEVEL = "CHECKMEMBERLEVEL";
        public const int nSC_CHECKMEMBERLEVEL = 158;
        public const string sSC_CHECKGAMEGOLD = "CHECKGAMEGOLD";
        public const int nSC_CHECKGAMEGOLD = 159;
        public const string sSC_CHECKGAMEPOINT = "CHECKGAMEPOINT";
        public const int nSC_CHECKGAMEPOINT = 160;
        public const string sSC_CHECKNAMELISTPOSITION = "CHECKNAMELISTPOSITION";
        public const int nSC_CHECKNAMELISTPOSITION = 161;
        public const string sSC_CHECKGUILDLIST = "CHECKGUILDLIST";
        public const int nSC_CHECKGUILDLIST = 162;
        public const string sSC_CHECKRENEWLEVEL = "CHECKRENEWLEVEL";
        public const int nSC_CHECKRENEWLEVEL = 163;
        public const string sSC_CHECKSLAVELEVEL = "CHECKSLAVELEVEL";
        public const int nSC_CHECKSLAVELEVEL = 164;
        public const string sSC_CHECKSLAVENAME = "CHECKSLAVENAME";
        public const int nSC_CHECKSLAVENAME = 165;
        public const string sSC_CHECKCREDITPOINT = "CHECKCREDITPOINT";
        public const int nSC_CHECKCREDITPOINT = 166;
        public const string sSC_CHECKOFGUILD = "CHECKOFGUILD";
        public const int nSC_CHECKOFGUILD = 167;
        public const string sSC_CHECKPAYMENT = "CHECKPAYMENT";
        public const int nSC_CHECKPAYMENT = 168;
        public const string sSC_CHECKUSEITEM = "CHECKUSEITEM";
        public const int nSC_CHECKUSEITEM = 169;
        public const string sSC_CHECKBAGSIZE = "CHECKBAGSIZE";
        public const int nSC_CHECKBAGSIZE = 170;
        public const string sSC_CHECKLISTCOUNT = "CHECKLISTCOUNT";
        public const int nSC_CHECKLISTCOUNT = 171;
        public const string sSC_CHECKDC = "CHECKDC";
        public const int nSC_CHECKDC = 172;
        public const string sSC_CHECKMC = "CHECKMC";
        public const int nSC_CHECKMC = 173;
        public const string sSC_CHECKSC = "CHECKSC";
        public const int nSC_CHECKSC = 174;
        public const string sSC_CHECKHP = "CHECKHP";
        public const int nSC_CHECKHP = 175;
        public const string sSC_CHECKMP = "CHECKMP";
        public const int nSC_CHECKMP = 176;
        public const string sSC_CHECKITEMTYPE = "CHECKITEMTYPE";
        public const int nSC_CHECKITEMTYPE = 180;
        public const string sSC_CHECKEXP = "CHECKEXP";
        public const int nSC_CHECKEXP = 181;
        public const string sSC_CHECKCASTLEGOLD = "CHECKCASTLEGOLD";
        public const int nSC_CHECKCASTLEGOLD = 182;
        public const string sSC_PASSWORDERRORCOUNT = "PASSWORDERRORCOUNT";
        public const int nSC_PASSWORDERRORCOUNT = 183;
        public const string sSC_ISLOCKPASSWORD = "ISLOCKPASSWORD";
        public const int nSC_ISLOCKPASSWORD = 184;
        public const string sSC_ISLOCKSTORAGE = "ISLOCKSTORAGE";
        public const int nSC_ISLOCKSTORAGE = 185;
        public const string sSC_CHECKBUILDPOINT = "CHECKGUILDBUILDPOINT";
        public const int nSC_CHECKBUILDPOINT = 186;
        public const string sSC_CHECKAURAEPOINT = "CHECKGUILDAURAEPOINT";
        public const int nSC_CHECKAURAEPOINT = 187;
        public const string sSC_CHECKSTABILITYPOINT = "CHECKGUILDSTABILITYPOINT";
        public const int nSC_CHECKSTABILITYPOINT = 188;
        public const string sSC_CHECKFLOURISHPOINT = "CHECKGUILDFLOURISHPOINT";
        public const int nSC_CHECKFLOURISHPOINT = 189;
        public const string sSC_CHECKCONTRIBUTION = "CHECKCONTRIBUTION";
        
        public const int nSC_CHECKCONTRIBUTION = 190;
        public const string sSC_CHECKRANGEMONCOUNT = "CHECKRANGEMONCOUNT";
        
        public const int nSC_CHECKRANGEMONCOUNT = 191;
        public const string sSC_CHECKITEMADDVALUE = "CHECKITEMADDVALUE";
        public const int nSC_CHECKITEMADDVALUE = 192;
        public const string sSC_CHECKINMAPRANGE = "CHECKINMAPRANGE";
        public const int nSC_CHECKINMAPRANGE = 193;
        public const string sSC_CASTLECHANGEDAY = "CASTLECHANGEDAY";
        public const int nSC_CASTLECHANGEDAY = 194;
        public const string sSC_CASTLEWARDAY = "CASTLEWARAY";
        public const int nSC_CASTLEWARDAY = 195;
        public const string sSC_ONLINELONGMIN = "ONLINELONGMIN";
        public const int nSC_ONLINELONGMIN = 196;
        public const string sSC_CHECKGUILDCHIEFITEMCOUNT = "CHECKGUILDCHIEFITEMCOUNT";
        public const int nSC_CHECKGUILDCHIEFITEMCOUNT = 197;
        public const string sSC_CHECKNAMEDATELIST = "CHECKNAMEDATELIST";
        public const int nSC_CHECKNAMEDATELIST = 198;
        public const string sSC_CHECKMAPHUMANCOUNT = "CHECKMAPHUMANCOUNT";
        public const int nSC_CHECKMAPHUMANCOUNT = 199;
        public const string sSC_CHECKMAPMONCOUNT = "CHECKMAPMONCOUNT";
        public const int nSC_CHECKMAPMONCOUNT = 200;
        public const string sSC_CHECKVAR = "CHECKVAR";
        public const int nSC_CHECKVAR = 201;
        public const string sSC_CHECKSERVERNAME = "CHECKSERVERNAME";
        public const int nSC_CHECKSERVERNAME = 202;
        public const string sSC_CHECKMAP = "CHECKMAP";
        public const int nSC_CHECKMAP = 203;
        public const string sSC_REVIVESLAVE = "REVIVESLAVES";
        public const int nSC_REVIVESLAVE = 206;
        public const string sSC_CHECKMAGICLVL = "CHECKMAGICLVL";
        public const int nSC_CHECKMAGICLVL = 207;
        public const string sSC_CHECKGROUPCLASS = "CHECKGROUPCLASS";
        public const int nSC_CHECKGROUPCLASS = 208;
        
        public const string sCheckDiemon = "CHECKDIEMON";
        
        public const int nCheckDiemon = 209;
        public const string scheckkillplaymon = "CHECKKILLPLAYMON";
        
        public const int ncheckkillplaymon = 210;
        public const string sSC_CHECKRANDOMNO = "CHECKRANDOMNO";
        
        public const int nSC_CHECKRANDOMNO = 212;
        public const string sSC_CHECKISONMAP = "ISONMAP";
        
        public const int nSC_CHECKISONMAP = 213;
        public const string sSC_KILLBYHUM = "KILLBYHUM";
        
        public const int nSC_KILLBYHUM = 214;
        public const string sSC_KILLBYMON = "KILLBYMON";
        
        public const int nSC_KILLBYMON = 215;
        public const string sSC_CHECKINSAFEZONE = "INSAFEZONE";
        
        public const int nSC_CHECKINSAFEZONE = 216;
        public const string sSC_ISGROUPMASTER = "ISGROUPMASTER";
        
        public const int nSC_ISGROUPMASTER = 217;
        
        public const string sSET = "SET";
        public const int nSET = 1;
        public const string sTAKE = "TAKE";
        public const int nTAKE = 2;
        public const string sSC_GIVE = "GIVE";
        public const int nSC_GIVE = 3;
        public const string sTAKEW = "TAKEW";
        public const int nTAKEW = 4;
        public const string sCLOSE = "CLOSE";
        public const int nCLOSE = 5;
        public const string sRESET = "RESET";
        public const int nRESET = 6;
        public const string sSETOPEN = "SETOPEN";
        public const int nSETOPEN = 7;
        public const string sSETUNIT = "SETUNIT";
        public const int nSETUNIT = 8;
        public const string sRESETUNIT = "RESETUNIT";
        public const int nRESETUNIT = 9;
        public const string sBREAK = "BREAK";
        public const int nBREAK = 10;
        public const string sTIMERECALL = "TIMERECALL";
        public const int nTIMERECALL = 11;
        public const string sSC_PARAM1 = "PARAM1";
        public const int nSC_PARAM1 = 12;
        public const string sSC_PARAM2 = "PARAM2";
        public const int nSC_PARAM2 = 13;
        public const string sSC_PARAM3 = "PARAM3";
        public const int nSC_PARAM3 = 14;
        public const string sSC_PARAM4 = "PARAM4";
        public const int nSC_PARAM4 = 15;
        public const string sSC_EXEACTION = "EXEACTION";
        public const int nSC_EXEACTION = 16;
        public const string sMAPMOVE = "MAPMOVE";
        public const int nMAPMOVE = 19;
        public const string sMAP = "MAP";
        public const int nMAP = 20;
        public const string sTAKECHECKITEM = "TAKECHECKITEM";
        public const int nTAKECHECKITEM = 21;
        public const string sMONGEN = "MONGEN";
        public const int nMONGEN = 22;
        public const string sSC_MONGENP = "MONGENP";
        public const int nSC_MONGENP = 23;
        public const string sMONCLEAR = "MONCLEAR";
        public const int nMONCLEAR = 24;
        public const string sMOV = "MOV";
        public const int nMOV = 25;
        public const string sINC = "INC";
        public const int nINC = 26;
        public const string sDEC = "DEC";
        public const int nDEC = 27;
        public const string sSUM = "SUM";
        public const int nSUM = 28;
        
        
        
        public const string sSC_DIV = "DIV";
        public const int nSC_DIV = 241;
        
        
        
        public const string sSC_MUL = "MUL";
        public const int nSC_MUL = 242;
        
        
        
        public const string sSC_PERCENT = "PERCENT";
        public const int nSC_PERCENT = 243;
        public const string sBREAKTIMERECALL = "BREAKTIMERECALL";
        public const int nBREAKTIMERECALL = 29;
        public const string sSENDMSG = "SENDMSG";
        public const int nSENDMSG = 30;
        public const string sCHANGEMODE = "CHANGEMODE";
        public const int nCHANGEMODE = 31;
        public const string sPKPOINT = "PKPOINT";
        public const int nPKPOINT = 32;
        public const string sCHANGEXP = "CHANGEXP";
        public const int nCHANGEXP = 33;
        public const string sSC_RECALLMOB = "RECALLMOB";
        public const int nSC_RECALLMOB = 34;
        public const string sKICK = "KICK";
        public const int nKICK = 35;
        public const string sMOVR = "MOVR";
        public const int nMOVR = 50;
        public const string sEXCHANGEMAP = "EXCHANGEMAP";
        public const int nEXCHANGEMAP = 51;
        public const string sRECALLMAP = "RECALLMAP";
        public const int nRECALLMAP = 52;
        public const string sADDBATCH = "ADDBATCH";
        public const int nADDBATCH = 53;
        public const string sBATCHDELAY = "BATCHDELAY";
        public const int nBATCHDELAY = 54;
        public const string sBATCHMOVE = "BATCHMOVE";
        public const int nBATCHMOVE = 55;
        public const string sPLAYDICE = "PLAYDICE";
        public const int nPLAYDICE = 56;
        public const string sSC_PASTEMAP = "PASTEMAP";
        public const string sSC_LOADGEN = "LOADGEN";
        public const string sADDNAMELIST = "ADDNAMELIST";
        public const int nADDNAMELIST = 57;
        public const string sDELNAMELIST = "DELNAMELIST";
        public const int nDELNAMELIST = 58;
        public const string sADDGUILDLIST = "ADDGUILDLIST";
        public const int nADDGUILDLIST = 59;
        public const string sDELGUILDLIST = "DELGUILDLIST";
        public const int nDELGUILDLIST = 60;
        public const string sADDACCOUNTLIST = "ADDACCOUNTLIST";
        public const int nADDACCOUNTLIST = 61;
        public const string sDELACCOUNTLIST = "DELACCOUNTLIST";
        public const int nDELACCOUNTLIST = 62;
        public const string sADDIPLIST = "ADDIPLIST";
        public const int nADDIPLIST = 63;
        public const string sDELIPLIST = "DELIPLIST";
        public const int nDELIPLIST = 64;
        public const string sGOQUEST = "GOQUEST";
        public const int nGOQUEST = 100;
        public const string sENDQUEST = "ENDQUEST";
        public const int nENDQUEST = 101;
        public const string sGOTO = "GOTO";
        public const int nGOTO = 102;
        public const string sSC_HAIRCOLOR = "HAIRCOLOR";
        public const int nSC_HAIRCOLOR = 104;
        public const string sSC_WEARCOLOR = "WEARCOLOR";
        public const int nSC_WEARCOLOR = 105;
        public const string sSC_HAIRSTYLE = "HAIRSTYLE";
        public const int nSC_HAIRSTYLE = 106;
        public const string sSC_MONRECALL = "MONRECALL";
        public const int nSC_MONRECALL = 107;
        public const string sSC_HORSECALL = "HORSECALL";
        public const int nSC_HORSECALL = 108;
        public const string sSC_HAIRRNDCOL = "HAIRRNDCOL";
        public const int nSC_HAIRRNDCOL = 109;
        public const string sSC_RANDSETDAILYQUEST = "RANDSETDAILYQUEST";
        public const int nSC_RANDSETDAILYQUEST = 110;
        public const string sSC_REFINEWEAPON = "REFINEWEAPON";
        public const int nSC_REFINEWEAPON = 113;
        public const string sSC_RECALLGROUPMEMBERS = "RECALLGROUPMEMBERS";
        public const int nSC_RECALLGROUPMEMBERS = 117;
        public const string sSC_MAPTING = "MAPTING";
        public const int nSC_MAPTING = 118;
        public const string sSC_WRITEWEAPONNAME = "WRITEWEAPONNAME";
        public const int nSC_WRITEWEAPONNAME = 119;
        public const string sSC_DELAYGOTO = "DELAYGOTO";
        public const int nSC_DELAYGOTO = 120;
        public const string sSC_ENABLECMD = "ENABLECMD";
        public const int nSC_ENABLECMD = 121;
        public const string sSC_LINEMSG = "LINEMSG";
        public const int nSC_LINEMSG = 122;
        public const string sSC_EVENTMSG = "EVENTMSG";
        public const int nSC_EVENTMSG = 123;
        public const string sSC_SOUNDMSG = "SOUNDMSG";
        public const int nSC_SOUNDMSG = 124;
        public const string sSC_SETMISSION = "SETMISSION";
        public const int nSC_SETMISSION = 125;
        public const string sSC_CLEARMISSION = "CLEARMISSION";
        public const int nSC_CLEARMISSION = 126;
        public const string sSC_MONPWR = "MONPWR";
        public const int nSC_MONPWR = 127;
        public const string sSC_ENTER_OK = "ENTER_OK";
        public const int nSC_ENTER_OK = 128;
        public const string sSC_ENTER_FAIL = "ENTER_FAIL";
        public const int nSC_ENTER_FAIL = 129;
        public const string sSC_MONADDITEM = "MONADDITEM";
        public const int nSC_MONADDITEM = 130;
        public const string sSC_CHANGEWEATHER = "CHANGEWEATHER";
        public const int nSC_CHANGEWEATHER = 131;
        public const string sSC_CHANGEWEAPONATOM = "CHANGEWEAPONATOM";
        public const int nSC_CHANGEWEAPONATOM = 132;
        public const string sSC_GETREPAIRCOST = "GETREPAIRCOST";
        public const int nSC_GETREPAIRCOST = 134;
        public const string sSC_KILLHORSE = "KILLHORSE";
        public const int nSC_KILLHORSE = 133;
        public const string sSC_REPAIRITEM = "REPAIRITEM";
        public const int nSC_REPAIRITEM = 135;
        public const string sSC_USEREMERGENCYCLOSE = "USEREMERGENCYCLOSE";
        public const int nSC_USEREMERGENCYCLOSE = 138;
        public const string sSC_BUILDGUILD = "BUILDGUILD";
        public const int nSC_BUILDGUILD = 139;
        public const string sSC_GUILDWAR = "GUILDWAR";
        public const int nSC_GUILDWAR = 140;
        public const string sSC_CHANGEUSERNAME = "CHANGEUSERNAME";
        public const int nSC_CHANGEUSERNAME = 141;
        public const string sSC_CHANGEMONLEVEL = "CHANGEMONLEVEL";
        public const int nSC_CHANGEMONLEVEL = 142;
        public const string sSC_DROPITEMMAP = "DROPITEMMAP";
        public const int nSC_DROPITEMMAP = 143;
        public const string sSC_CLEARITEMMAP = "CLEARITEMMAP";
        public const int nSC_CLEARITEMMAP = 170;
        public const string sSC_PROPOSECASTLEWAR = "PROPOSECASTLEWAR";
        public const int nSC_PROPOSECASTLEWAR = 144;
        public const string sSC_FINISHCASTLEWAR = "FINISHCASTLEWAR";
        public const int nSC_FINISHCASTLEWAR = 145;
        public const string sSC_MOVENPC = "MOVENPC";
        public const int nSC_MOVENPC = 146;
        public const string sSC_SPEAK = "SPEAK";
        public const int nSC_SPEAK = 147;
        public const string sSC_SENDCMD = "SENDCMD";
        public const int nSC_SENDCMD = 148;
        public const string sSC_INCFAME = "INCFAME";
        public const int nSC_INCFAME = 149;
        public const string sSC_DECFAME = "DECFAME";
        public const int nSC_DECFAME = 150;
        public const string sSC_CAPTURECASTLEFLAG = "CAPTURECASTLEFLAG";
        public const int nSC_CAPTURECASTLEFLAG = 151;
        public const string sSC_MAKESHOOTER = "MAKESHOOTER";
        public const int nSC_MAKESHOOTER = 153;
        public const string sSC_KILLSHOOTER = "KILLSHOOTER";
        public const int nSC_KILLSHOOTER = 154;
        public const string sSC_LEAVESHOOTER = "LEAVESHOOTER";
        public const int nSC_LEAVESHOOTER = 155;
        public const string sSC_CHANGEMAPATTR = "CHANGEMAPATTR";
        public const int nSC_CHANGEMAPATTR = 157;
        public const string sSC_RESETMAPATTR = "RESETMAPATTR";
        public const int nSC_RESETMAPATTR = 158;
        public const string sSC_MAKECASTLEDOOR = "MAKECASTLEDOOR";
        public const int nSC_MAKECASTLEDOOR = 159;
        public const string sSC_REPAIRCASTLEDOOR = "REPAIRCASTLEDOOR";
        public const int nSC_REPAIRCASTLEDOOR = 160;
        public const string sSC_CHARGESHOOTER = "CHARGESHOOTER";
        public const int nSC_CHARGESHOOTER = 161;
        public const string sSC_SETAREAATTR = "SETAREAATTR";
        public const int nSC_SETAREAATTR = 162;
        public const string sSC_CLEARDELAYGOTO = "CLEARDELAYGOTO";
        public const int nSC_CLEARDELAYGOTO = 163;
        public const string sSC_TESTFLAG = "TESTFLAG";
        public const int nSC_TESTFLAG = 164;
        public const string sSC_APPLYFLAG = "APPLYFLAG";
        public const int nSC_APPLYFLAG = 165;
        public const string sSC_PASTEFLAG = "PASTEFLAG";
        public const int nSC_PASTEFLAG = 166;
        public const string sSC_GETBACKCASTLEGOLD = "GETBACKCASTLEGOLD";
        public const int nSC_GETBACKCASTLEGOLD = 167;
        public const string sSC_GETBACKUPGITEM = "GETBACKUPGITEM";
        public const int nSC_GETBACKUPGITEM = 168;
        public const string sSC_TINGWAR = "TINGWAR";
        public const int nSC_TINGWAR = 169;
        public const string sSC_SAVEPASSWD = "SAVEPASSWD";
        public const int nSC_SAVEPASSWD = 171;
        public const string sSC_CREATENPC = "CREATENPC";
        public const int nSC_CREATENPC = 172;
        public const string sSC_TAKEBONUS = "TAKEBONUS";
        public const int nSC_TAKEBONUS = 173;
        public const string sSC_SYSMSG = "SYSMSG";
        public const int nSC_SYSMSG = 174;
        public const string sSC_LOADVALUE = "LOADVALUE";
        public const int nSC_LOADVALUE = 175;
        public const string sSC_SAVEVALUE = "SAVEVALUE";
        public const int nSC_SAVEVALUE = 176;
        public const string sSC_SAVELOG = "SAVELOG";
        public const int nSC_SAVELOG = 177;
        public const string sSC_GETMARRIED = "GETMARRIED";
        public const int nSC_GETMARRIED = 178;
        public const string sSC_DIVORCE = "DIVORCE";
        public const int nSC_DIVORCE = 189;
        public const string sSC_CAPTURESAYING = "CAPTURESAYING";
        public const int nSC_CAPTURESAYING = 190;
        public const string sSC_CANCELMARRIAGERING = "CANCELMARRIAGERING";
        public const int nSC_CANCELMARRIAGERING = 191;
        public const string sSC_OPENUSERMARKET = "OPENUSERMARKET";
        public const int nSC_OPENUSERMARKET = 192;
        public const string sSC_SETTYPEUSERMARKET = "SETTYPEUSERMARKET";
        public const int nSC_SETTYPEUSERMARKET = 193;
        public const string sSC_CHECKSOLDITEMSUSERMARKET = "CHECKSOLDITEMSUSERMARKET";
        public const int nSC_CHECKSOLDITEMSUSERMARKET = 194;
        public const string sSC_SETGMEMAP = "SETGMEMAP";
        public const int nSC_SETGMEMAP = 200;
        public const string sSC_SETGMEPOINT = "SETGMEPOINT";
        public const int nSC_SETGMEPOINT = 201;
        public const string sSC_SETGMETIME = "SETGMETIME";
        public const int nSC_SETGMETIME = 209;
        public const string sSC_STARTNEWGME = "STARTNEWGME";
        public const int nSC_STARTNEWGME = 202;
        public const string sSC_MOVETOGMEMAP = "MOVETOGMEMAP";
        public const int mSC_MOVETOGMEMAP = 203;
        public const string sSC_FINISHGME = "FINISHGME";
        public const int nSC_FINISHGME = 204;
        public const string sSC_CONTINUEGME = "CONTINUEGME";
        public const int nSC_CONTINUEGME = 205;
        public const string sSC_SETGMEPLAYTIME = "SETGMEPLAYTIME";
        public const int nSC_SETGMEPLAYTIME = 206;
        public const string sSC_SETGMEPAUSETIME = "SETGMEPAUSETIME";
        public const int nSC_SETGMEPAUSETIME = 207;
        public const string sSC_SETGMELIMITUSER = "SETGMELIMITUSER";
        public const int nSC_SETGMELIMITUSER = 208;
        public const string sSC_SETEVENTMAP = "SETEVENTMAP";
        public const int nSC_SETEVENTMAP = 210;
        public const string sSC_RESETEVENTMAP = "RESETEVENTMAP";
        public const int nSC_RESETEVENTMAP = 211;
        public const string sSC_TESTREFINEPOINTS = "TESTREFINEPOINTS";
        public const int nSC_TESTREFINEPOINTS = 220;
        public const string sSC_RESETREFINEWEAPON = "RESETREFINEWEAPON";
        public const int nSC_RESETREFINEWEAPON = 221;
        public const string sSC_TESTREFINEACCESSORIES = "TESTREFINEACCESSORIES";
        public const int nSC_TESTREFINEACCESSORIES = 222;
        public const string sSC_REFINEACCESSORIES = "REFINEACCESSORIES";
        public const int nSC_REFINEACCESSORIES = 223;
        public const string sSC_APPLYMONMISSION = "APPLYMONMISSION";
        public const int nSC_APPLYMONMISSION = 225;
        public const string sSC_MAPMOVER = "MAPMOVER";
        public const int nSC_MAPMOVER = 226;
        public const string sSC_ADDSTR = "ADDSTR";
        public const int nSC_ADDSTR = 227;
        public const string sSC_SETEVENTDAMAGE = "SETEVENTDAMAGE";
        public const int nSC_SETEVENTDAMAGE = 228;
        public const string sSC_FORMATSTR = "FORMATSTR";
        public const int nSC_FORMATSTR = 229;
        public const string sSC_CLEARPATH = "CLEARPATH";
        public const int nSC_CLEARPATH = 230;
        public const string sSC_ADDPATH = "ADDPATH";
        public const int nSC_ADDPATH = 231;
        public const string sSC_APPLYPATH = "APPLYPATH";
        public const int nSC_APPLYPATH = 232;
        public const string sSC_MAPSPELL = "MAPSPELL";
        public const int nSC_MAPSPELL = 233;
        public const string sSC_GIVEEXP = "GIVEEXP";
        public const int nSC_GIVEEXP = 234;
        public const string sSC_GROUPMOVE = "GROUPMOVE";
        public const int nSC_GROUPMOVE = 235;
        public const string sSC_GIVEEXPMAP = "GIVEEXPMAP";
        public const int nSC_GIVEEXPMAP = 236;
        public const string sSC_APPLYMONEX = "APPLYMONEX";
        public const int nSC_APPLYMONEX = 237;
        public const string sSC_CLEARNAMELIST = "CLEARNAMELIST";
        public const int nSC_CLEARNAMELIST = 238;
        public const string sSC_TINGCASTLEVISITOR = "TINGCASTLEVISITOR";
        public const int nSC_TINGCASTLEVISITOR = 239;
        public const string sSC_MAKEHEALZONE = "MAKEHEALZONE";
        public const int nSC_MAKEHEALZONE = 240;
        public const string sSC_MAKEDAMAGEZONE = "MAKEDAMAGEZONE";
        public const int nSC_MAKEDAMAGEZONE = 241;
        public const string sSC_CLEARZONE = "CLEARZONE";
        public const int nSC_CLEARZONE = 242;
        public const string sSC_READVALUESQL = "READVALUESQL";
        public const int nSC_READVALUESQL = 250;
        public const string sSC_READSTRINGSQL = "READSTRINGSQL";
        public const int nSC_READSTRINGSQL = 255;
        public const string sSC_WRITEVALUESQL = "WRITEVALUESQL";
        public const int nSC_WRITEVALUESQL = 251;
        public const string sSC_INCVALUESQL = "INCVALUESQL";
        public const int nSC_INCVALUESQL = 252;
        public const string sSC_DECVALUESQL = "DECVALUESQL";
        public const int nSC_DECVALUESQL = 253;
        public const string sSC_UPDATEVALUESQL = "UPDATEVALUESQL";
        public const int nSC_UPDATEVALUESQL = 254;
        public const string sSC_KILLSLAVE = "KILLSLAVE";
        public const int nSC_KILLSLAVE = 260;
        public const string sSC_SETITEMEVENT = "SETITEMEVENT";
        public const int nSC_SETITEMEVENT = 261;
        public const string sSC_REMOVEITEMEVENT = "REMOVEITEMEVENT";
        public const int nSC_REMOVEITEMEVENT = 262;
        public const string sSC_RETURN = "RETURN";
        public const int nSC_RETURN = 263;
        public const string sSC_CLEARCASTLEOWNER = "CLEARCASTLEOWNER";
        public const int nSC_CLEARCASTLEOWNER = 270;
        public const string sSC_DISSOLUTIONGUILD = "DISSOLUTIONGUILD";
        public const int nSC_DISSOLUTIONGUILD = 271;
        public const string sSC_CHANGEGENDER = "CHANGEGENDER";
        public const int nSC_CHANGEGENDER = 272;
        public const string sSC_SETFAME = "SETFAME";
        public const int nSC_SETFAME = 273;
        public const string sSC_CHANGELEVEL = "CHANGELEVEL";
        public const int nSC_CHANGELEVEL = 300;
        public const string sSC_MARRY = "MARRY";
        public const int nSC_MARRY = 301;
        public const string sSC_UNMARRY = "UNMARRY";
        public const int nSC_UNMARRY = 302;
        public const string sSC_GETMARRY = "GETMARRY";
        public const int nSC_GETMARRY = 303;
        public const string sSC_GETMASTER = "GETMASTER";
        public const int nSC_GETMASTER = 304;
        public const string sSC_CLEARSKILL = "CLEARSKILL";
        public const int nSC_CLEARSKILL = 305;
        public const string sSC_DELNOJOBSKILL = "DELNOJOBSKILL";
        public const int nSC_DELNOJOBSKILL = 306;
        public const string sSC_DELSKILL = "DELSKILL";
        public const int nSC_DELSKILL = 307;
        public const string sSC_ADDSKILL = "ADDSKILL";
        public const int nSC_ADDSKILL = 308;
        public const string sSC_SKILLLEVEL = "SKILLLEVEL";
        public const int nSC_SKILLLEVEL = 309;
        public const string sSC_CHANGEPKPOINT = "CHANGEPKPOINT";
        public const int nSC_CHANGEPKPOINT = 310;
        public const string sSC_CHANGEEXP = "CHANGEEXP";
        public const int nSC_CHANGEEXP = 311;
        public const string sSC_CHANGEJOB = "CHANGEJOB";
        public const int nSC_CHANGEJOB = 312;
        public const string sSC_MISSION = "MISSION";
        public const int nSC_MISSION = 313;
        public const string sSC_MOBPLACE = "MOBPLACE";
        public const int nSC_MOBPLACE = 314;
        public const string sSC_SETMEMBERTYPE = "SETMEMBERTYPE";
        public const int nSC_SETMEMBERTYPE = 315;
        public const string sSC_SETMEMBERLEVEL = "SETMEMBERLEVEL";
        public const int nSC_SETMEMBERLEVEL = 316;
        public const string sSC_GAMEGOLD = "GAMEGOLD";
        public const int nSC_GAMEGOLD = 317;
        public const string sSC_AUTOADDGAMEGOLD = "AUTOADDGAMEGOLD";
        public const int nSC_AUTOADDGAMEGOLD = 318;
        public const string sSC_AUTOSUBGAMEGOLD = "AUTOSUBGAMEGOLD";
        public const int nSC_AUTOSUBGAMEGOLD = 319;
        public const string sSC_CHANGENAMECOLOR = "CHANGENAMECOLOR";
        public const int nSC_CHANGENAMECOLOR = 320;
        public const string sSC_CLEARPASSWORD = "CLEARPASSWORD";
        public const int nSC_CLEARPASSWORD = 321;
        public const string sSC_RENEWLEVEL = "RENEWLEVEL";
        public const int nSC_RENEWLEVEL = 322;
        public const string sSC_KILLMONEXPRATE = "KILLMONEXPRATE";
        public const int nSC_KILLMONEXPRATE = 323;
        public const string sSC_POWERRATE = "POWERRATE";
        public const int nSC_POWERRATE = 324;
        public const string sSC_CHANGEMODE = "CHANGEMODE";
        public const int nSC_CHANGEMODE = 325;
        public const string sSC_CHANGEPERMISSION = "CHANGEPERMISSION";
        public const int nSC_CHANGEPERMISSION = 326;
        public const string sSC_KILL = "KILL";
        public const int nSC_KILL = 327;
        public const string sSC_KICK = "KICK";
        public const int nSC_KICK = 328;
        public const string sSC_BONUSPOINT = "BONUSPOINT";
        public const int nSC_BONUSPOINT = 329;
        public const string sSC_RESTRENEWLEVEL = "RESTRENEWLEVEL";
        public const int nSC_RESTRENEWLEVEL = 330;
        public const string sSC_DELMARRY = "DELMARRY";
        public const int nSC_DELMARRY = 331;
        public const string sSC_DELMASTER = "DELMASTER";
        public const int nSC_DELMASTER = 332;
        public const string sSC_MASTER = "MASTER";
        public const int nSC_MASTER = 333;
        public const string sSC_UNMASTER = "UNMASTER";
        public const int nSC_UNMASTER = 334;
        public const string sSC_CREDITPOINT = "CREDITPOINT";
        public const int nSC_CREDITPOINT = 335;
        public const string sSC_CLEARNEEDITEMS = "CLEARNEEDITEMS";
        public const int nSC_CLEARNEEDITEMS = 336;
        public const string sSC_CLEARMAKEITEMS = "CLEARMAKEITEMS";
        public const int nSC_CLEARMAEKITEMS = 337;
        public const string sSC_SETSENDMSGFLAG = "SETSENDMSGFLAG";
        public const int nSC_SETSENDMSGFLAG = 338;
        public const string sSC_UPGRADEITEMS = "UPGRADEITEM";
        public const int nSC_UPGRADEITEMS = 339;
        public const string sSC_UPGRADEITEMSEX = "UPGRADEITEMEX";
        public const int nSC_UPGRADEITEMSEX = 340;
        public const string sSC_MONGENEX = "MONGENEX";
        public const int nSC_MONGENEX = 341;
        public const string sSC_CLEARMAPMON = "CLEARMAPMON";
        public const int nSC_CLEARMAPMON = 342;
        public const string sSC_SETMAPMODE = "SETMPAMODE";
        public const int nSC_SETMAPMODE = 343;
        public const string sSC_GAMEPOINT = "GAMEPOINT";
        public const int nSC_GAMEPOINT = 344;
        public const string sSC_PKZONE = "PKZONE";
        public const int nSC_PKZONE = 345;
        public const string sSC_RESTBONUSPOINT = "RESTBONUSPOINT";
        public const int nSC_RESTBONUSPOINT = 346;
        public const string sSC_TAKECASTLEGOLD = "TAKECASTLEGOLD";
        public const int nSC_TAKECASTLEGOLD = 347;
        public const string sSC_HUMANHP = "HUMANHP";
        public const int nSC_HUMANHP = 348;
        public const string sSC_HUMANMP = "HUMANMP";
        public const int nSC_HUMANMP = 349;
        public const string sSC_BUILDPOINT = "GUILDBUILDPOINT";
        public const int nSC_BUILDPOINT = 350;
        public const string sSC_AURAEPOINT = "GUILDAURAEPOINT";
        public const int nSC_AURAEPOINT = 351;
        public const string sSC_STABILITYPOINT = "GUILDSTABILITYPOINT";
        public const int nSC_STABILITYPOINT = 352;
        public const string sSC_FLOURISHPOINT = "GUILDFLOURISHPOINT";
        public const int nSC_FLOURISHPOINT = 353;
        
        public const string sSC_OPENMAGICBOX = "OPENITEMBOX";
        public const int nSC_OPENMAGICBOX = 354;
        public const string sSC_SETRANKLEVELNAME = "SETRANKLEVELNAME";
        public const int nSC_SETRANKLEVELNAME = 355;
        public const string sSC_GMEXECUTE = "GMEXECUTE";
        public const int nSC_GMEXECUTE = 356;
        public const string sSC_GUILDCHIEFITEMCOUNT = "GUILDCHIEFITEMCOUNT";
        public const int nSC_GUILDCHIEFITEMCOUNT = 357;
        public const string sSC_ADDNAMEDATELIST = "ADDNAMEDATELIST";
        public const int nSC_ADDNAMEDATELIST = 358;
        public const string sSC_DELNAMEDATELIST = "DELNAMEDATELIST";
        public const int nSC_DELNAMEDATELIST = 359;
        public const string sSC_MOBFIREBURN = "MOBFIREBURN";
        public const int nSC_MOBFIREBURN = 360;
        public const string sSC_MESSAGEBOX = "MESSAGEBOX";
        public const int nSC_MESSAGEBOX = 361;
        public const string sSC_SETSCRIPTFLAG = "SETSCRIPTFLAG";
        
        public const int nSC_SETSCRIPTFLAG = 362;
        public const string sSC_SETAUTOGETEXP = "SETAUTOGETEXP";
        public const int nSC_SETAUTOGETEXP = 363;
        public const string sSC_VAR = "VAR";
        public const int nSC_VAR = 364;
        public const string sSC_LOADVAR = "LOADVAR";
        public const int nSC_LOADVAR = 365;
        public const string sSC_SAVEVAR = "SAVEVAR";
        public const int nSC_SAVEVAR = 366;
        public const string sSC_CALCVAR = "CALCVAR";
        public const int nSC_CALCVAR = 367;
        public const string sSC_GUILDRECALL = "GUILDRECALL";
        public const int nSC_GUILDRECALL = 368;
        public const string sSC_GROUPADDLIST = "GROUPADDLIST";
        public const int nSC_GROUPADDLIST = 369;
        public const string sSC_CLEARLIST = "CLEARLIST";
        public const int nSC_CLEARLIST = 370;
        public const string sSC_GROUPRECALL = "GROUPRECALL";
        public const int nSC_GROUPRECALL = 371;
        public const string sSC_GROUPMOVEMAP = "GROUPMOVEMAP";
        public const int nSC_GROUPMOVEMAP = 372;
        public const string sSC_SAVESLAVES = "SAVESLAVES";
        public const int nSC_SAVESLAVES = 373;
        
        public const string sCHECKUSERDATE = "CHECKUSERDATE";
        
        public const int nCHECKUSERDATE = 375;
        public const string sADDUSERDATE = "ADDUSERDATE";
        
        public const int nADDUSERDATE = 376;
        public const string sDELUSERDATE = "DELUSERDATE";
        
        public const int nDELUSERDATE = 377;
        public const string sSC_OffLine = "OFFLINE";
        
        public const int nSC_OffLine = 379;
        public const string sSC_REPAIRALL = "REPAIRALL";
        
        public const int nSC_REPAIRALL = 380;
        public const string sSC_SETRANDOMNO = "SETRANDOMNO";
        
        public const int nSC_SETRANDOMNO = 381;
        public const string sSC_QUERYBAGITEMS = "QUERYBAGITEMS";
        
        public const int nSC_QUERYBAGITEMS = 382;
        public const string sSC_ISHIGH = "ISHIGH";
        public const int nSC_ISHIGH = 383;
        
        
        
        public const string sTHROWITEM = "THROWITEM";
        public const string sDROPITEMMAP = "DROPITEMMAP";
        public const int nTHROWITEM = 384;
        
        
        
        public const string sOPENYBDEAL = "OPENYBDEAL";
        
        
        
        public const int nOPENYBDEAL = 252;
        public const string sQUERYYBSELL = "QUERYYBSELL";
        
        
        
        public const int nQUERYYBSELL = 253;
        public const string sQUERYYBDEAL = "QUERYYBDEAL";
        public const int nQUERYYBDEAL = 254;
        
        
        
        public const string sDELAYGOTO = "DELAYGOTO";
        public const string sDELAYCALL = "DELAYCALL";
        public const int nDELAYGOTO = 255;
        public const string sCLEARDELAYGOTO = "CLEARDELAYGOTO";
        public const int nCLEARDELAYGOTO = 256;
        
        
        
        public const string sSCHECKDEATHPLAYMON = "CHECKDEATHPLAYMON";
        
        
        
        public const string sSCHECKKILLMOBNAME = "CHECKKILLMONNAME";
        public const int nSCHECKDEATHPLAYMON = 257;

        
        
        
        
        
        
        
        public const string sybdeal = "@ybdeal";


        public const string sOFFLINEMSG = "@@offlinemsg";
        
        public const string sSL_SENDMSG = "@@sendmsg";
        public const string sSUPERREPAIR = "@s_repair";
        public const string sSUPERREPAIROK = "@SRepairDone";
        public const string sSUPERREPAIRFAIL = "@fail_s_repair";
        public const string sREPAIR = "@repair";
        public const string sREPAIROK = "@RepairDone";
        public const string sBUY = "@buy";
        public const string sSELL = "@sell";
        public const string sMAKEDURG = "@makedrug";
        public const string sPRICES = "@prices";
        public const string sSTORAGE = "@storage";
        public const string sGETBACK = "@getback";
        public const string sGETNEXTPAGE = "@getnextpage";
        public const string sGETPREVIOUSPAGE = "@getPreviouspage";
        public const string sUPGRADENOW = "@upgradenow";
        public const string sUPGRADEING = "~@upgradenow_ing";
        public const string sUPGRADEOK = "~@upgradenow_ok";
        public const string sUPGRADEFAIL = "~@upgradenow_fail";
        public const string sGETBACKUPGNOW = "@getbackupgnow";
        public const string sGETBACKUPGOK = "~@getbackupgnow_ok";
        public const string sGETBACKUPGFAIL = "~@getbackupgnow_fail";
        public const string sGETBACKUPGFULL = "~@getbackupgnow_bagfull";
        public const string sGETBACKUPGING = "~@getbackupgnow_ing";
        public const string sEXIT = "@exit";
        public const string sBACK = "@back";
        public const string sMAIN = "@main";
        public const string sFAILMAIN = "~@main";
        public const string sGETMASTER = "@@getmaster";
        public const string sGETMARRY = "@@getmarry";
        public const string sUSEITEMNAME = "@@useitemname";
        public const string sBUILDGUILDNOW = "@@buildguildnow";
        public const string sSCL_GUILDWAR = "@@guildwar";
        public const string sDONATE = "@@donate";
        public const string sREQUESTCASTLEWAR = "@requestcastlewarnow";
        public const string sCASTLENAME = "@@castlename";
        public const string sWITHDRAWAL = "@@withdrawal";
        public const string sRECEIPTS = "@@receipts";
        public const string sOPENMAINDOOR = "@openmaindoor";
        public const string sCLOSEMAINDOOR = "@closemaindoor";
        public const string sREPAIRDOORNOW = "@repairdoornow";
        public const string sREPAIRWALLNOW1 = "@repairwallnow1";
        public const string sREPAIRWALLNOW2 = "@repairwallnow2";
        public const string sREPAIRWALLNOW3 = "@repairwallnow3";
        public const string sHIREARCHERNOW = "@hirearchernow";
        public const string sHIREGUARDNOW = "@hireguardnow";
        public const string sHIREGUARDOK = "@hireguardok";
        public const string sNpc_def = "Npc_def";
        public const string sPsNpcscripts = "PsNpcscripts";
        public const string sPsMapQuest = "PsMapQuest";
        public const string g_sGameLogMsg1 = "{0}\09{1}\09{2}\09{3}\09{4}\09{5}\09{6}\09{7}\09{8}";
        public const string g_sHumanDieEvent = "人物死亡事件";
        public const string g_sHitOverSpeed = "[攻击超速] {0} 间隔:{1} 数量:{2}";
        public const string g_sRunOverSpeed = "[跑步超速] {0} 间隔:{1} 数量:{2}";
        public const string g_sWalkOverSpeed = "[行走超速] {0} 间隔:{1} 数量:{2}";
        public const string g_sSpellOverSpeed = "[魔法超速] {0} 间隔:{1} 数量:{2}";
        public const string g_sBunOverSpeed = "[游戏超速] {0} 间隔:{1} 数量:{2}";
        public const string g_sGameCommandPermissionTooLow = "权限不够!!!";
        public const string g_sGameCommandParamUnKnow = "命令格式: @{0} {1}";
        public const string g_sGameCommandMoveHelpMsg = "地图?";
        public const string g_sGameCommandPositionMoveHelpMsg = "地图? 座标X 座标Y";
        public const string g_sGameCommandPositionMoveCanotMoveToMap = "无法移动到地?: {0} X:{1} Y:{2}";
        public const string g_sGameCommandInfoHelpMsg = "人物名称";
        public const string g_sNowNotOnLineOrOnOtherServer = "{0} 现在不在线，或在其它服务器上!!!";
        public const string g_sGameCommandMobCountHelpMsg = "地图?";
        public const string g_sGameCommandMobCountMapNotFound = "指定的地图不存在!!!";
        public const string g_sGameCommandMobCountMonsterCount = "怪物数量：{0}";
        public const string g_sGameCommandHumanCountHelpMsg = "地图?";
        public const string g_sGameCommandKickHumanHelpMsg = "人物名称";
        public const string g_sGameCommandTingHelpMsg = "人物名称";
        public const string g_sGameCommandSuperTingHelpMsg = "人物名称 范围(0-10)";
        public const string g_sGameCommandMapMoveHelpMsg = "源地?  目标地图";
        public const string g_sGameCommandMapMoveMapNotFound = "地图{0}不存?!!!";
        public const string g_sGameCommandShutupHelpMsg = "人物名称 [时间数|无]";
        public const string g_sGameCommandShutupHumanMsg = "{0} 禁止聊天：{1}秒";
        public const string g_sGameCommandGamePointHelpMsg = "人物名称 控制?(+,-,=) 游戏点数(1-100000000)";
        public const string g_sGameCommandGamePointHumanMsg = "你的游戏点已增加{0}点，当前总点数为{1}点?";
        public const string g_sGameCommandGamePointGMMsg = "{0}的游戏点已增加{1}点，当前总点数为{2}点?";
        public const string g_sGameCommandCreditPointHelpMsg = "人物名称 控制?(+,-,=) 声望点数(0-255)";
        public const string g_sGameCommandCreditPointHumanMsg = "你的声望点已增加{0}点，当前总声望点数为{1}点?";
        public const string g_sGameCommandCreditPointGMMsg = "{0}的声望点已增加{1}点，当前总声望点数为{2}点?";
        public const string g_sGameCommandGameGoldHelpMsg = " 人物名称 控制?(+,-,=) 游戏?(1-200000000)";
        public const string g_sGameCommandGameGoldHumanMsg = "你的{0}已增加{1}，当前拥有{2}{3}?";
        public const string g_sGameCommandGameGoldGMMsg = "{0}的{1}已增加{2}，当前拥有{3}{4}?";
        public const string g_sGameCommandMapInfoMsg = "地图名称: {0}({1})";
        public const string g_sGameCommandMapInfoSizeMsg = "地图大小: X({0}) Y({1})";
        public const string g_sGameCommandShutupReleaseHelpMsg = "人物名称";
        public const string g_sGameCommandShutupReleaseCanSendMsg = "你已经恢复聊天功?!!!";
        public const string g_sGameCommandShutupReleaseHumanCanSendMsg = "解除禁言成功！";
        // 0x0062BB24（长度前缀 12 = 六个汉字），由 @LookOutSay 的 0x006242CC 引用
        public const string g_sGameCommandShutupListIsNullMsg = "禁言名单为空";
        public const string g_sGameCommandLevelConsoleMsg = "[等级调整] {0} ({1} -> {2})";
        public const string g_sGameCommandSbkGoldHelpMsg = "城堡名称 控制?(=?-?+) 金币?(1-100000000)";
        public const string g_sGameCommandSbkGoldCastleNotFoundMsg = "城堡{0}未找?!!!";
        public const string g_sGameCommandSbkGoldShowMsg = "{0}的金币数?: {1} 今天收入: {2}";
        public const string g_sGameCommandRecallHelpMsg = "人物名称";
        public const string g_sGameCommandReGotoHelpMsg = "人物名称";
        public const string g_sGameCommandShowHumanFlagHelpMsg = "人物名称 标识?";
        public const string g_sGameCommandShowHumanFlagONMsg = "{0}: [{1}] = ON";
        public const string g_sGameCommandShowHumanFlagOFFMsg = "{0}: [{1}] = OFF";
        public const string g_sGameCommandShowHumanUnitHelpMsg = "人物名称 单元?";
        public const string g_sGameCommandShowHumanUnitONMsg = "{0}: [{1}] = ON";
        public const string g_sGameCommandShowHumanUnitOFFMsg = "{0}: [{1}] = OFF";
        public const string g_sGameCommandMobHelpMsg = "怪物名称 数量 等级";
        public const string g_sGameCommandMobMsg = "怪物名称不正确或其它未问?!!!";
        public const string g_sGameCommandMobNpcHelpMsg = "NPC名称 脚本文件? 外形(数字) 属沙?(0,1)";
        public const string g_sGameCommandNpcScriptHelpMsg = "？？？？";
        public const string g_sGameCommandDelNpcMsg = "命令使用方法不正确，必须与NPC面对面，才能使用此命?!!!";
        public const string g_sGameCommandRecallMobHelpMsg = "怪物名称 数量 等级";
        public const string g_sGameCommandLuckPointHelpMsg = "人物名称 控制? 幸运点数";
        public const string g_sGameCommandLuckPointMsg = "{0} 的幸运点数为:{1}/{2} 幸运值为:{3}";
        public const string g_sGameCommandLotteryTicketMsg = "已中彩票?:{0} 未中彩票?:{1} 一等奖:{2} 二等?:{3} 三等?:{4} 四等?:{5} 五等?:{6} 六等?:{7} ";
        public const string g_sGameCommandReloadGuildHelpMsg = "行会名称";
        public const string g_sGameCommandReloadGuildOnMasterserver = "此命令只能在主游戏服务器上执?!!!";
        public const string g_sGameCommandReloadGuildNotFoundGuildMsg = "未找到行会{0}!!!";
        public const string g_sGameCommandReloadGuildSuccessMsg = "行会{0}重加载成?...";
        public const string g_sGameCommandReloadLineNoticeSuccessMsg = "重新加载公告设置信息完成?";
        public const string g_sGameCommandReloadLineNoticeFailMsg = "重新加载公告设置信息失败!!!";
        public const string g_sGameCommandFreePKHelpMsg = "人物名称";
        public const string g_sGameCommandFreePKHumanMsg = "你的PK值已经被清除...";
        public const string g_sGameCommandFreePKMsg = "{0}的PK值已经被清除...";
        public const string g_sGameCommandPKPointHelpMsg = "人物名称";
        public const string g_sGameCommandPKPointMsg = "{0}的PK点数?:{1}";
        public const string g_sGameCommandIncPkPointHelpMsg = "人物名称 PK点数";
        public const string g_sGameCommandIncPkPointAddPointMsg = "{0}的PK值已增加%d?...";
        public const string g_sGameCommandIncPkPointDecPointMsg = "{0}的PK值已减少%d?...";
        public const string g_sGameCommandHumanLocalHelpMsg = "人物名称";
        public const string g_sGameCommandHumanLocalMsg = "{0}来自:{1}";
        public const string g_sGameCommandPrvMsgHelpMsg = "人物名称";
        public const string g_sGameCommandPrvMsgUnLimitMsg = "{0} 已从禁止私聊列表中删?...";
        public const string g_sGameCommandPrvMsgLimitMsg = "{0} 已被加入禁止私聊列表...";
        public const string g_sGamecommandMakeHelpMsg = " 物品名称  数量";
        public const string g_sGamecommandMakeItemNameOrPerMissionNot = "输入的物品名称不正确，或权限不够!!!";
        public const string g_sGamecommandMakeInCastleWarRange = "攻城区域，禁止使用此功能!!!";
        public const string g_sGamecommandMakeInSafeZoneRange = "非安全区，禁止使用此功能!!!";
        public const string g_sGamecommandMakeItemNameNotFound = "{0} 物品名称不正?!!!";
        public const string g_sGamecommandSuperMakeHelpMsg = "身上没指定物?!!!";
        public const string g_sGameCommandViewWhisperHelpMsg = " 人物名称";
        public const string g_sGameCommandViewWhisperMsg1 = "已停止侦听{0}的私聊信?...";
        public const string g_sGameCommandViewWhisperMsg2 = "正在侦听{0}的私聊信?...";
        public const string g_sGameCommandReAliveHelpMsg = " 人物名称";
        public const string g_sGameCommandReAliveMsg = "{0} 已获重生.";
        public const string g_sGameCommandChangeJobHelpMsg = " 人物名称 职业类型(Warr Wizard Taos)";
        public const string g_sGameCommandChangeJobMsg = "{0} 的职业更改成功?";
        public const string g_sGameCommandChangeJobHumanMsg = "职业更改成功?";
        public const string g_sGameCommandTestGetBagItemsHelpMsg = "(用于测试升级武器方面参数)";
        public const string g_sGameCommandShowUseItemInfoHelpMsg = "人物名称";
        public const string g_sGameCommandBindUseItemHelpMsg = "人物名称 物品类型 绑定方法";
        public const string g_sGameCommandBindUseItemNoItemMsg = "{0}的{1}没有戴物?!!!";
        public const string g_sGameCommandBindUseItemAlreadBindMsg = "{0}的{1}上的物品早已绑定过了!!!";
        public const string g_sGameCommandMobFireBurnHelpMsg = "命令格式: {0} {1} {2} {3} {4} {5} {6}";
        public const string g_sGameCommandMobFireBurnMapNotFountMsg = "地图{0} 不存?";
        public const string U_DRESSNAME = "衣服";
        public const string U_WEAPONNAME = "武器";
        public const string U_RIGHTHANDNAME = "照明?";
        public const string U_NECKLACENAME = "项链";
        public const string U_HELMETNAME = "头盔";
        public const string U_ARMRINGLNAME = "左手?";
        public const string U_ARMRINGRNAME = "右手?";
        public const string U_RINGLNAME = "左戒?";
        public const string U_RINGRNAME = "右戒?";
        public const string U_BUJUKNAME = "物品";
        public const string U_BELTNAME = "腰带";
        public const string U_BOOTSNAME = "鞋子";
        public const string U_CHARMNAME = "宝石";
        public const string U_MASKNAME = "\u6597\u7b20";
        public const string U_YUPEINAME = "\u7389\u4f69";
        public const string U_WARDRUMNAME = "\u6218\u9f13";
        public const string U_MILITARYDRUMNAME = "\u519b\u9f13";
        public const string U_HORSENAME = "\u76fe\u724c";
        public const string U_MOUNTNAME = "\u9a6c\u724c";
        public const string U_MOUNTDISPLAYNAME = "\u5750\u9a91";

        #endregion

        static M2Share()
        {
            
            // 战神: 工作目录 = 可执行文件所在目录 (GS1/)
            sConfigPath = AppContext.BaseDirectory;
            sRootPath = Path.GetFullPath(Path.Combine(sConfigPath, "..")); // 父目录=Mir200\
            // Read 战神 original !Setup.txt (GBK encoded, in GS1/ directory)
            string setupPath = Path.Combine(sConfigPath, sConfigFileName);
            ServerConf = new ServerConfig(setupPath);
            StringConf = new StringConfig(setupPath);
            // Exp from Share/PlayerUpgradeExp.ini
            ExpConf = new ExpsConfig(Path.Combine(sRootPath, "Share", sExpConfigFileName));
            // Global from Share/ServerData.ini
            GlobalConf = new GlobalConfig(Path.Combine(sRootPath, "Share", sGlobalConfigFileName));
            CommandConf = new GameCmdConfig(Path.Combine(sConfigPath, sCommandFileName));
            LogSystem = new MirLog();
            g_Config = new GameSvrConfig();
            // 共享资源路径修正: 当 sRootPath != sConfigPath 时, Envir/Map/Notice 用 sRootPath
            if (sRootPath.Length > 0 && sRootPath != sConfigPath)
            {
                g_Config.sEnvirDir = Path.GetFullPath(Path.Combine(sRootPath, g_Config.sEnvirDir));
                g_Config.sMapDir = Path.GetFullPath(Path.Combine(sRootPath, g_Config.sMapDir));
                g_Config.sNoticeDir = Path.GetFullPath(Path.Combine(sRootPath, g_Config.sNoticeDir));
                g_Config.sLogDir = Path.GetFullPath(Path.Combine(sRootPath, g_Config.sLogDir));
            }
            // Original tower catalogs are captured during startup in this
            // order (0x0064E889, 0x0064E8CF, 0x0064E8D6).
            TPlayObject.InitializeNativeMagicTowerChallengeCatalog(sRootPath);
            NormNpc.InitializeNativeMagicTowerMonsterCatalog(sRootPath,
                g_Config.sBaseDir);
            TPlayObject.InitializeNativeMagicTowerPrizeCatalog(sRootPath);
            NativeStartupConfigValidation.ValidateMagicTowerBoxPrizeConfigs();
            NativeStartupConfigValidation.ValidateWoLongConfigAtStartup();
            NativeStartupConfigValidation.ValidateEncryptorDllAtStartup();
            var heroIniPath = NativeStartupConfigValidation.ResolveShareConfigPath(
                "Hero.ini");
            if (!File.Exists(heroIniPath) || new FileInfo(heroIniPath).Length == 0)
                NativeStartupConfigValidation.ReportHeroIniMissing(heroIniPath);
            var shareConfigDirectory = Path.GetFullPath(Path.Combine(
                sRootPath, g_Config.sBaseDir, "config"));
            NativeJewelStoneTable.TryLoadFromShareConfig(shareConfigDirectory);
            _ = NativeBufferConf.TryLoad(
                Path.GetFullPath(Path.Combine(sRootPath, g_Config.sBaseDir)),
                out _, out var bufferConfError);
            if (!string.IsNullOrEmpty(bufferConfError))
            {
                M2Share.ErrorMessage("BufferConf加载失败: " + bufferConfError);
            }
            RandomNumber = RandomNumber.GetInstance();
            MagicTowerRouteSequencer = new NativeMagicTowerRouteSequencer(
                RandomNumber.Random);
            SecHeroPracticePrizeManager =
                NativeSecHeroPracticePrizeManager.CreateEmpty(RandomNumber.Random);
        }

        public static string GetGoodTick => string.Format(sSTATUS_GOOD, HUtil32.GetTickCount());

        public static void CopyStdItemToOStdItem(TStdItem StdItem, TOStdItem OStdItem)
        {
            OStdItem.Name = StdItem.Name;
            OStdItem.StdMode = StdItem.StdMode;
            OStdItem.Shape = StdItem.Shape;
            OStdItem.Weight = StdItem.Weight;
            OStdItem.AniCount = StdItem.AniCount;
            OStdItem.Source = (byte)Math.Min((int)StdItem.Source, 255);
            OStdItem.Reserved = StdItem.reserved;
            OStdItem.NeedIdentify = StdItem.NeedIdentify;
            OStdItem.Looks = StdItem.Looks;
            OStdItem.DuraMax = (ushort)StdItem.DuraMax;
            OStdItem.AC = HUtil32.MakeWord(HUtil32._MIN(byte.MaxValue, HUtil32.LoWord(StdItem.AC)), HUtil32._MIN(byte.MaxValue, HUtil32.HiWord(StdItem.AC)));
            OStdItem.MAC = HUtil32.MakeWord(HUtil32._MIN(byte.MaxValue, HUtil32.LoWord(StdItem.MAC)), HUtil32._MIN(byte.MaxValue, HUtil32.HiWord(StdItem.MAC)));
            OStdItem.DC = HUtil32.MakeWord(HUtil32._MIN(byte.MaxValue, HUtil32.LoWord(StdItem.DC)), HUtil32._MIN(byte.MaxValue, HUtil32.HiWord(StdItem.DC)));
            OStdItem.MC = HUtil32.MakeWord(HUtil32._MIN(byte.MaxValue, HUtil32.LoWord(StdItem.MC)), HUtil32._MIN(byte.MaxValue, HUtil32.HiWord(StdItem.MC)));
            OStdItem.SC = HUtil32.MakeWord(HUtil32._MIN(byte.MaxValue, HUtil32.LoWord(StdItem.SC)), HUtil32._MIN(byte.MaxValue, HUtil32.HiWord(StdItem.SC)));
            OStdItem.Need = (byte)StdItem.Need;
            OStdItem.NeedLevel = (byte)StdItem.NeedLevel;
            OStdItem.Price = (int)StdItem.Price;
        }

        public static bool LoadLineNotice(string FileName)
        {
            var result = false;
            int i;
            string sText;
            StringList LoadList = null;
            if (File.Exists(FileName))
            {
                LoadList = new StringList();
                LoadList.LoadFromFile(FileName);
                i = 0;
                while (true)
                {
                    if (LoadList.Count <= i)
                    {
                        break;
                    }
                    sText = LoadList[i].Trim();
                    if (string.IsNullOrEmpty(sText))
                    {
                        LoadList.RemoveAt(i);
                        continue;
                    }
                    LineNoticeList.Add(sText);
                    i++;
                }
                result = true;
            }
            return result;
        }

        
        
        
        
        
        
        
        public static bool GetMultiServerAddrPort(byte btServerIndex, ref string sIPaddr, ref int nPort)
        {
            TRouteInfo RouteInfo;
            var result = false;
            for (var i = 0; i < ServerTableList.Length; i++)
            {
                RouteInfo = ServerTableList[i];
                if (RouteInfo == null)
                {
                    continue;
                }
                if (RouteInfo.nGateCount <= 0)
                {
                    continue;
                }
                if (RouteInfo.nServerIdx == btServerIndex)
                {
                    sIPaddr = GetRandpmRoute(RouteInfo, ref nPort);
                    result = true;
                    break;
                }
            }
            return result;
        }

        private static string GetRandpmRoute(TRouteInfo RouteInfo, ref int nGatePort)
        {
            var nC = RandomNumber.Random(RouteInfo.nGateCount);
            nGatePort = RouteInfo.nGameGatePort[nC];
            return RouteInfo.sGameGateIP[nC];
        }

        private const int MaxGuiLogLines = 5000;
        private static readonly ConcurrentQueue<string> _logQueue = new();
        private static int _logQueueCount;

        public static void MainOutMessage(string Msg)
        {
            var line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + Msg;
            Console.WriteLine(line);
            var count = Interlocked.Increment(ref _logQueueCount);
            _logQueue.Enqueue(line);
            while (count > MaxGuiLogLines && _logQueue.TryDequeue(out _))
            {
                count = Interlocked.Decrement(ref _logQueueCount);
            }
        }

        public static void FlushLogQueue(RichTextBox box)
        {
            while (_logQueue.TryDequeue(out var line))
            {
                Interlocked.Decrement(ref _logQueueCount);
                if (box.TextLength > 50000) { box.Select(0, box.TextLength / 4); box.SelectedText = ""; }
                box.AppendText(line + Environment.NewLine);
            }
        }

        public static void MainOutMessage(string Msg, MessageType messageType = MessageType.Success, MessageLevel messageLevel = MessageLevel.None, ConsoleColor messageColor = ConsoleColor.White)
        {
            LogSystem.LogInfo(Msg, messageType, messageLevel: messageLevel, messageColor: messageColor);
        }

        public static void ErrorMessage(string Msg, MessageType messageType = MessageType.Error, MessageLevel messageLevel = MessageLevel.None, ConsoleColor messageColor = ConsoleColor.Red)
        {
            LogSystem.LogInfo(Msg, messageType, messageLevel: messageLevel, messageColor: messageColor);
        }

        public static int GetExVersionNO(int nVersionDate, ref int nOldVerstionDate)
        {
            var result = 0;
            if (nVersionDate > 100000000)
            {
                while (nVersionDate > 100000000)
                {
                    nVersionDate -= 100000000;
                    result += 100000000;
                }
            }
            nOldVerstionDate = nVersionDate;
            return result;
        }

        // 战神 sub_764A90(eax=sx, edx=sy, ecx=dx, [ebp+8]=dy), ret 4.
        // MOVE-47: the no-match sink is 0x764BB6 `33 C0 xor eax,eax`, i.e. 0
        // (DR_UP), not 4. GetNextDirection(x,y,x,y) must answer "up".
        public static byte GetNextDirection(int sx, int sy, int dx, int dy)
        {
            int flagx;
            int flagy;
            byte result = Grobal2.DR_UP;
            if (sx < dx)
            {
                flagx = 1;
            }
            else if (sx == dx)
            {
                flagx = 0;
            }
            else
            {
                flagx = -1;
            }
            if (Math.Abs(sy - dy) > 2)
            {
                if (sx >= dx - 1 && sx <= dx + 1)
                {
                    flagx = 0;
                }
            }
            if (sy < dy)
            {
                flagy = 1;
            }
            else if (sy == dy)
            {
                flagy = 0;
            }
            else
            {
                flagy = -1;
            }
            if (Math.Abs(sx - dx) > 2)
            {
                // MOVE-47: the Y suppressor is asymmetric with the X one above.
                //   0x764B10  3B F0  cmp esi,eax   ; sy vs dy-1
                //   0x764B12  7E 0A  jle  skip     ; run only when sy >  dy-1
                //   0x764B14  47     inc edi       ; dy+1
                //   0x764B15  3B F7  cmp esi,edi
                //   0x764B17  7D 05  jge  skip     ; run only when sy <  dy+1
                // `jge` makes the upper bound strict; `<=` let sy == dy+1 in and
                // flattened diagonals (dir 1/7) into pure horizontals (dir 2/6).
                if (sy > dy - 1 && sy < dy + 1)
                {
                    flagy = 0;
                }
            }
            if (flagx == 0 && flagy == -1)
            {
                result = Grobal2.DR_UP;
            }
            if (flagx == 1 && flagy == -1)
            {
                result = Grobal2.DR_UPRIGHT;
            }
            if (flagx == 1 && flagy == 0)
            {
                result = Grobal2.DR_RIGHT;
            }
            if (flagx == 1 && flagy == 1)
            {
                result = Grobal2.DR_DOWNRIGHT;
            }
            if (flagx == 0 && flagy == 1)
            {
                result = Grobal2.DR_DOWN;
            }
            if (flagx == -1 && flagy == 1)
            {
                result = Grobal2.DR_DOWNLEFT;
            }
            if (flagx == -1 && flagy == 0)
            {
                result = Grobal2.DR_LEFT;
            }
            if (flagx == -1 && flagy == -1)
            {
                result = Grobal2.DR_UPLEFT;
            }
            return result;
        }

        /// <summary>
        /// sub_764BC4, the second and quite different heading helper. Where
        /// <see cref="GetNextDirection"/> (sub_764A90) buckets the two axis
        /// signs, this one takes the ratio
        /// <c>(|dy| * dy) / (dx*dx + dy*dy)</c> — dx = tx-sx at 0x764BCD,
        /// dy = sy-ty at 0x764BD1 — rounds it to float32 (0x764C05
        /// `D9 5D FC fstp dword [ebp-4]`) and compares it against four 80-bit
        /// thresholds, picking the right or left fan from the sign of dx
        /// (0x764C09 `85 DB / 7E 4F`, so dx == 0 takes the left fan).
        ///
        ///   0x764CB0  9A 99 99 99 99 99 99 D9 FE BF  = -0.85
        ///   0x764CBC  9A 99 99 99 99 99 99 99 FC BF  = -0.15
        ///   0x764CC8  9A 99 99 99 99 99 99 99 FC 3F  = +0.15
        ///   0x764CD4  9A 99 99 99 99 99 99 D9 FE 3F  = +0.85
        ///
        /// Each test is `fld const / fcomp ratio / jbe`, so the arm fires on
        /// <c>ratio &lt; const</c>. Same source and target gives 0 (0x764BDC).
        /// </summary>
        public static byte GetNextDirectionByRatio(int sx, int sy, int dx, int dy)
        {
            int deltaX = dx - sx;
            int deltaY = sy - dy;
            if (deltaX == 0 && deltaY == 0)
            {
                return Grobal2.DR_UP;
            }

            int numerator = unchecked(Math.Abs(deltaY) * deltaY);
            int denominator = unchecked(deltaX * deltaX + deltaY * deltaY);
            float ratio = (float)((double)numerator / denominator);

            if (ratio < -0.85f)
            {
                return Grobal2.DR_DOWN;
            }
            if (deltaX > 0)
            {
                if (ratio < -0.15f)
                    return Grobal2.DR_DOWNRIGHT;
                if (ratio < 0.15f)
                    return Grobal2.DR_RIGHT;
                if (ratio < 0.85f)
                    return Grobal2.DR_UPRIGHT;
                return Grobal2.DR_UP;
            }
            if (ratio < -0.15f)
                return Grobal2.DR_DOWNLEFT;
            if (ratio < 0.15f)
                return Grobal2.DR_LEFT;
            if (ratio < 0.85f)
                return Grobal2.DR_UPLEFT;
            return Grobal2.DR_UP;
        }

        public static bool CheckUserItems(int nIdx, GoodItem StdItem)
        {
            var result = false;
            switch (nIdx)
            {
                case Grobal2.U_DRESS:
                    if (StdItem.StdMode == 10 || StdItem.StdMode == 11)
                    {
                        result = true;
                    }
                    break;
                case Grobal2.U_WEAPON:
                    if (StdItem.StdMode == 5 || StdItem.StdMode == 6)
                    {
                        result = true;
                    }
                    break;
                case Grobal2.U_RIGHTHAND:
                    result = StdItem.StdMode == 30;
                    break;
                case Grobal2.U_NECKLACE:
                    if (StdItem.StdMode == 19 || StdItem.StdMode == 20 || StdItem.StdMode == 21)
                    {
                        result = true;
                    }
                    break;
                case Grobal2.U_HELMET:
                    if (StdItem.StdMode == 15)
                    {
                        result = true;
                    }
                    break;
                // DURA-16/17 — 全部 15 个 VMT+0x60 谓词体 + StdMode 派发链已逐字节反演
                // (flat_image base 0x400000)，本 switch 是原生 per-class 谓词族的中心化转置，
                // 与原生 16 槽 slot->StdMode 精确等价 (PARTIAL 已升级为 FAITHFUL)。
                //
                // 槽位资格在原生是每个物品类的 VMT+0x60 谓词，配合 0x74C338 的
                // StdMode 派发表（byte 表 0x74C374 + 跳表 0x74C414）决定 StdMode 走哪个类；
                // 每个谓词体已确认接受的 slot(dl)：
                //   0x7639D4 test dl,dl/sete            slot0  TManClothes/TWomanClothes  -> 10,11
                //   0x7608CC cmp dl,1/sete              slot1  TLWeapon/TBrokenWeapon/TSpade -> 5,6
                //   0x760488 cmp dl,2/sete              slot2  TRWeapon    -> 30
                //   0x761784 cmp dl,3/sete              slot3  TNecklace   -> 19,20,21
                //   0x7611C0 cmp dl,4/sete              slot4  THelmet     -> 15
                //   0x7625AC cmp dl,5 je/cmp dl,6 je    slot5|6 TArmRing   -> 24,26
                //   0x761CB4 cmp dl,7 je/cmp dl,8 je    slot7|8 TRing      -> 22,23
                //   0x762C64 cmp dl,9/sete              slot9  TEquipBujuk 族 -> 25
                //   0x762D30 cmp dl,0xA/sete            slotA  TBelt       -> 27
                //   0x7630CC cmp dl,0xB/sete            slotB  TBoots      -> 28
                //   0x763390 cmp dl,0xC/sete            slotC  TCharm 族    -> 7
                //   0x760F3C cmp dl,0xD/sete            slotD  THeadMask   -> 16
                //   0x7610C0 cmp dl,0xE/sete            slotE  TWarDrum    -> 29
                //   0x763254 cmp dl,0xF/sete            slotF  TMaPai      -> 34
                // 闭合方式：每个可装备 StdMode 经 dl=byte[0x74C374+StdMode]、
                // arm=[0x74C414+dl*4] 到构造臂，臂里 `mov eax,[classref]` 取元类，
                // u32(classref)=VMT、u32(VMT+0x60)=谓词；同一 StdMode 的所有 Shape 子类
                // (如武器 5 有 5 个变体) 都落在同一 slot，故 StdMode->slot 唯一确定。
                //
                // DURA-17：裸基类 TEquipItem 的 +0x60 = 0x75FE18 `33 C0 C3`(xor eax,eax/ret)，
                // 对任何 slot 返回 false。C# 此 switch result=false 起始且无 default，
                // 未列 StdMode 对所有槽保持 false，即 fail-closed 等价"永不可穿"。
                //
                // StdMode 51/52/53/54/63/64 落到默认臂 0x74D67E（0x74D680 cmp al,0x96
                // 之下即 TBaseItem），62 走 TAnimalMascot，两者的父链都是
                // TBaseItem<TBaseObj<TObject —— 不是 TEquipItem，VMT 里根本没有 +0x60
                // 这一格（TBasePileItem VMT 0x781C24 的 +0x60 落在类名串 0x781C88 上，
                // TAnimalMascot VMT 0x782614 的 +0x60 落在 0x782678 同理），原生穿不上；
                // 派发字节表里其它有专属臂的 StdMode(0-4,8,31-33,40,42,43,47…) 构造的类
                // 其 VMT+0x60 均非谓词(落类名串/垃圾)，同样不可装备，本 switch 无遗漏。
                case Grobal2.U_ARMRINGL:
                    if (StdItem.StdMode == 24 || StdItem.StdMode == 26)
                    {
                        result = true;
                    }
                    break;
                case Grobal2.U_ARMRINGR:
                    if (StdItem.StdMode == 24 || StdItem.StdMode == 26)
                    {
                        result = true;
                    }
                    break;
                case Grobal2.U_RINGL:
                case Grobal2.U_RINGR:
                    if (StdItem.StdMode == 22 || StdItem.StdMode == 23)
                    {
                        result = true;
                    }
                    break;
                case Grobal2.U_BUJUK:
                    if (StdItem.StdMode == 25)
                    {
                        result = true;
                    }
                    break;
                case Grobal2.U_BELT:
                    if (StdItem.StdMode == 27)
                    {
                        result = true;
                    }
                    break;
                case Grobal2.U_BOOTS:
                    if (StdItem.StdMode == 28)
                    {
                        result = true;
                    }
                    break;
                case Grobal2.U_CHARM:
                    if (StdItem.StdMode == 7)
                    {
                        result = true;
                    }
                    break;
                case Grobal2.U_MASK:
                    result = StdItem.StdMode == 16;
                    break;
                case Grobal2.U_YUPEI:
                    result = StdItem.StdMode == 29;
                    break;
                case Grobal2.U_SHIELD:
                    result = StdItem.StdMode == 34;
                    break;
            }
            return result;
        }

        private static readonly int[] CommonYearMonthDays =
            { 0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        public static DateTime AddDateTimeOfDay(DateTime dateTime, int nDay)
        {
            if (nDay <= 0) return dateTime;

            nDay--;
            var year = dateTime.Year;
            var month = dateTime.Month;
            var day = dateTime.Day;
            while (CommonYearMonthDays[month] < day + nDay)
            {
                nDay = day + nDay - CommonYearMonthDays[month] - 1;
                day = 1;
                if (month <= 11)
                {
                    month++;
                    continue;
                }

                month = 1;
                year = year == 99 ? 2000 : year + 1;
            }

            day += nDay;
            return new DateTime(year, month, day);
        }

        public static ushort GetGoldShape(int nGold)
        {
            ushort result = 112;
            if (nGold >= 30)
            {
                result = 113;
            }
            if (nGold >= 70)
            {
                result = 114;
            }
            if (nGold >= 300)
            {
                result = 115;
            }
            if (nGold >= 1000)
            {
                result = 116;
            }
            return result;
        }

        
        public static int GetRandomLook(int nBaseLook, int nRage)
        {
            var result = nBaseLook + RandomNumber.Random(nRage);
            return result;
        }

        public static bool CheckGuildName(string sGuildName)
        {
            var result = true;
            if (sGuildName.Length > g_Config.nGuildNameLen)
            {
                result = false;
                return result;
            }
            for (var i = 0; i <= sGuildName.Length - 1; i++)
            {
                if (sGuildName[i] < '0' || sGuildName[i] == '/' || sGuildName[i] == '\\' || sGuildName[i] == ':' || sGuildName[i] == '*' || sGuildName[i] == ' '
                    || sGuildName[i] == '\"' || sGuildName[i] == '\'' || sGuildName[i] == '<' || sGuildName[i] == '|' || sGuildName[i] == '?' || sGuildName[i] == '>')
                {
                    result = false;
                }
            }
            return result;
        }

        public static int GetItemNumber()
        {
            while (true)
            {
                var current = Volatile.Read(ref g_Config.nItemNumber);
                var next = unchecked((uint)current + 3u);
                if (next > 0xFFFFFFF6u)
                {
                    next = unchecked((uint)Volatile.Read(
                        ref g_Config.nItemNumberSeed));
                }
                var nextBits = unchecked((int)next);
                if (Interlocked.CompareExchange(ref g_Config.nItemNumber,
                        nextBits, current) == current)
                {
                    return nextBits;
                }
            }
        }

        public static int GetItemNumberEx() => GetItemNumber();

        public static string FilterShowName(string sName)
        {
            var result = "";
            var sC = "";
            var bo11 = false;
            if (string.IsNullOrEmpty(sName))
            {
                return sName;
            }
            for (var i = 0; i <= sName.Length - 1; i++)
            {
                if (sName[i] >= '0' && sName[i] <= '9' || sName[i] == '-')
                {
                    result = sName.Substring(0, i);
                    sC = sName.Substring(i, sName.Length - i);
                    bo11 = true;
                    break;
                }
            }
            if (!bo11)
            {
                result = sName;
            }
            return result;
        }

        public static byte sub_4B2F80(int nDir, int nRage)
        {
            return (byte)((nDir + nRage) % 8);
        }

        
        
        
        
        
        public static int GetValNameNo(string sText)
        {
            var result = -1;
            int nValNo;
            if (sText.Length >= 2)
            {
                var valType = char.ToUpper(sText[0]);
                switch (valType)
                {
                    case 'P':
                        if (sText.Length == 3)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(1, 2), -1);
                            if ((nValNo >= 0) && (nValNo < 100))
                            {
                                result = nValNo;
                            }
                        }
                        else
                        {
                            nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                            if ((nValNo >= 0) && (nValNo < 10))
                            {
                                result = nValNo;
                            }
                        }
                        break;
                    case 'G':
                        if (sText.Length == 4)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(1, 3), -1);
                            if ((nValNo < 500) && (nValNo > 99))
                            {
                                result = nValNo + 700;
                            }
                        }
                        if (sText.Length == 3)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(1, 2), -1);
                            if ((nValNo >= 0) && (nValNo < 100))
                            {
                                result = nValNo + 100;
                            }
                        }
                        else
                        {
                            nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                            if ((nValNo >= 0) && (nValNo < 10))
                            {
                                result = nValNo + 100;
                            }
                        }
                        break;
                    case 'M':
                        if (sText.Length == 3)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(1, 2), -1);
                            if ((nValNo >= 0) && (nValNo < 100))
                            {
                                result = nValNo + 300;
                            }
                        }
                        else
                        {
                            nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                            if ((nValNo >= 0) && (nValNo < 10))
                            {
                                result = nValNo + 300;
                            }
                        }
                        break;
                    case 'I':
                        if (sText.Length == 3)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(1, 2), -1);
                            if ((nValNo >= 0) && (nValNo < 100))
                            {
                                result = nValNo + 400;
                            }
                        }
                        else
                        {
                            nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                            if ((nValNo >= 0) && (nValNo < 10))
                            {
                                result = nValNo + 400;
                            }
                        }
                        break;
                    case 'D':
                        if (sText.Length == 3)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(1, 2), -1);
                            if ((nValNo >= 0) && (nValNo < 100))
                            {
                                result = nValNo + 200;
                            }
                        }
                        else
                        {
                            nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                            if ((nValNo >= 0) && (nValNo < 10))
                            {
                                result = nValNo + 200;
                            }
                        }
                        break;
                    case 'N':
                        if (sText.Length == 3)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(1, 2), -1);
                            if ((nValNo >= 0) && (nValNo < 100))
                            {
                                result = nValNo + 500;
                            }
                        }
                        else
                        {
                            nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                            if ((nValNo >= 0) && (nValNo < 10))
                            {
                                result = nValNo + 500;
                            }
                        }
                        break;
                    case 'S':
                        if (sText.Length == 3)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(2 - 1, 2), -1);
                            if ((nValNo >= 0) && (nValNo < 100))
                            {
                                result = nValNo + 600;
                            }
                        }
                        else
                        {
                            nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                            if ((nValNo >= 0) && (nValNo < 10))
                            {
                                result = nValNo + 600;
                            }
                        }
                        break;
                    case 'A':
                        if (sText.Length == 4)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(1, 3), -1);
                            if ((nValNo < 500) && (nValNo > 99))
                            {
                                result = nValNo + 1100;
                            }
                        }
                        else
                        {
                            if (sText.Length == 3)
                            {
                                nValNo = HUtil32.Str_ToInt(sText.Substring(1, 2), -1);
                                if ((nValNo >= 0) && (nValNo < 100))
                                {
                                    result = nValNo + 700;
                                }
                            }
                            else
                            {
                                nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                                if ((nValNo >= 0) && (nValNo < 10))
                                {
                                    result = nValNo + 700;
                                }
                            }
                        }
                        break;
                    case 'T':
                        if (sText.Length == 3)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(2 - 1, 3), -1);
                            if ((nValNo >= 0) && (nValNo < 100))
                            {
                                result = nValNo + 700;
                            }
                        }
                        else
                        {
                            nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                            if ((nValNo >= 0) && (nValNo < 10))
                            {
                                result = nValNo + 700;
                            }
                        }
                        break;
                    case 'E':
                        if (sText.Length == 3)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(2 - 1, 2), -1);
                            if ((nValNo >= 0) && (nValNo < 100))
                            {
                                result = nValNo + 1600;
                            }
                        }
                        else
                        {
                            nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                            if ((nValNo >= 0) && (nValNo < 10))
                            {
                                result = nValNo + 1600;
                            }
                        }
                        break;
                    case 'W':
                        if (sText.Length == 3)
                        {
                            nValNo = HUtil32.Str_ToInt(sText.Substring(2 - 1, 2), -1);
                            if ((nValNo >= 0) && (nValNo < 100))
                            {
                                result = nValNo + 1700;
                            }
                        }
                        else
                        {
                            nValNo = HUtil32.Str_ToInt(sText[1].ToString(), -1);
                            if ((nValNo >= 0) && (nValNo < 10))
                            {
                                result = nValNo + 1700;
                            }
                        }
                        break;
                }
            }
            return result;
        }

        public static bool IsAccessory(int nIndex)
        {
            bool result;
            var Item = UserEngine.GetStdItem(nIndex);
            if (new ArrayList(new byte[] { 19, 20, 21, 22, 23, 24, 26 }).Contains(Item.StdMode))// 修正错误
            {
                result = true;
            }
            else
            {
                result = false;
            }
            return result;
        }

        public static IList<TMakeItem> GetMakeItemInfo(string sItemName)
        {
            if (g_MakeItemList.TryGetValue(sItemName, out var itemList))
            {
                return itemList;
            }
            return null;
        }

        public static string GetStartPointInfo(int nIndex, ref short nX, ref short nY)
        {
            var result = string.Empty;
            nX = 0;
            nY = 0;
            if (nIndex >= 0 && nIndex < StartPointList.Count)
            {
                var StartPoint = StartPointList[nIndex];
                if (StartPoint != null)
                {
                    nX = StartPoint.m_nCurrX;
                    nY = StartPoint.m_nCurrY;
                    result = StartPoint.m_sMapName;
                }
            }
            return result;
        }

        // 原生 sub_79D3D8 构造 196 字节记录，sub_4A0684 深拷贝入 FIFO；后台
        // sub_49FF64/sub_4A080C 最多聚合 4096 字节后经 UDP sendto 投递 LogServer。
        // LogStringList 继续保留为既有审计探针。只有完成原版 ABI 复核的调用点才走
        // AddNativeGameDataLog；旧 TAB 文本不反向猜测类型和字段，避免把历史畸形行错误发包。
        public const int LogRecordBufferCap = 20000;

        private static void AppendBoundedLog(ArrayList list, string sMsg)
        {
            list.Add(sMsg);
            if (list.Count > LogRecordBufferCap)
            {
                list.RemoveRange(0, list.Count - LogRecordBufferCap * 3 / 4);
            }
        }

        public static void AddGameDataLog(string sMsg)
        {
            HUtil32.EnterCriticalSection(LogMsgCriticalSection);
            try
            {
                AppendBoundedLog(LogStringList, sMsg);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(LogMsgCriticalSection);
            }

        }

        public static bool AddNativeGameDataLog(TBaseObject actor, byte logType,
            string itemName, int makeIndex, int quantity, string reason)
        {
            if (actor == null) return false;
            var record = new NativeGameDataLogRecord(logType,
                actor.m_sMapName, unchecked((ushort)actor.m_nCurrX),
                unchecked((ushort)actor.m_nCurrY), actor.m_sCharName, itemName,
                makeIndex, quantity, reason);
            var probe = string.Join('\t', logType, actor.m_sMapName,
                actor.m_nCurrX, actor.m_nCurrY, actor.m_sCharName, itemName,
                makeIndex, quantity, reason);

            HUtil32.EnterCriticalSection(LogMsgCriticalSection);
            try
            {
                AppendBoundedLog(LogStringList, probe);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(LogMsgCriticalSection);
            }
            return NativeGameDataLogService.Instance.TryEnqueue(record);
        }

        public static void AddLogonCostLog(string sMsg)
        {
            HUtil32.EnterCriticalSection(LogMsgCriticalSection);
            try
            {
                AppendBoundedLog(LogonCostLogList, sMsg);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(LogMsgCriticalSection);
            }
        }

        public static void TrimStringList(StringList sList)
        {
            int n8;
            string sC;
            n8 = 0;
            while (true)
            {
                if (sList.Count <= n8)
                {
                    break;
                }
                sC = sList[n8].Trim();
                if (sC == "")
                {
                    sList.RemoveAt(n8);
                    continue;
                }
                n8++;
            }
        }

        public static bool CanMakeItem(string sItemName)
        {
            bool result;
            result = false;
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool CanMoveMap(string sMapName)
        {
            bool result;
            int I;
            result = true;
            try
            {
                for (I = 0; I < g_DisableMoveMapList.Count; I++)
                {
                    
                    
                    
                    
                    
                }
            }
            finally
            {
            }
            return result;
        }

        public static bool LoadItemBindIPaddr()
        {
            bool result;
            ArrayList LoadList;
            var sFileName = string.Empty;
            var sLineText = string.Empty;
            var sMakeIndex = string.Empty;
            var sItemIndex = string.Empty;
            var sBindName = string.Empty;
            result = false;
            sFileName = g_Config.sEnvirDir + "ItemBindIPaddr.txt";
            LoadList = new ArrayList();
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveItemBindIPaddr()
        {
            bool result;
            result = false;
            var sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "ItemBindIPaddr.txt";
            
            
            
            
            
            
            
            
            
            
            
            result = true;
            return result;
        }

        public static bool LoadItemBindAccount()
        {
            ArrayList LoadList;
            var sMakeIndex = string.Empty;
            var sItemInde = string.Empty;
            var sBindName = string.Empty;
            var result = false;
            string sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "ItemBindAccount.txt";
            LoadList = new ArrayList();
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveItemBindAccount()
        {
            var result = false;
            var sFileName = g_Config.sEnvirDir + "ItemBindAccount.txt";
            
            
            
            
            
            
            
            
            
            
            

            

            
            result = true;
            return result;
        }

        public static bool LoadItemBindCharName()
        {
            var sMakeIndex = string.Empty;
            var sBindName = string.Empty;
            var result = false;
            string sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "ItemBindChrName.txt";
            
            
            
            
            
            
            
            
            

            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            

            
            

            
            return result;
        }

        public static bool SaveItemBindCharName()
        {
            var result = false;
            var sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "ItemBindChrName.txt";
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool LoadDisableMakeItem()
        {
            var result = false;
            var sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DisableMakeItem.txt";
            var LoadList = new ArrayList();
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveDisableMakeItem()
        {
            string sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DisableMakeItem.txt";
            
            return true;
        }

        public static bool LoadUnMasterList()
        {
            bool result = false;
            string sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "UnMaster.txt";
            ArrayList LoadList = new ArrayList();
            
            
            
            
            

            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveUnMasterList()
        {
            string sFileName = g_Config.sEnvirDir + "UnMaster.txt";
            
            return true;
        }

        public static bool LoadUnForceMasterList()
        {
            var result = false;
            var sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "UnForceMaster.txt";
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveUnForceMasterList()
        {
            string sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "UnForceMaster.txt";
            
            return true;
        }

        public static bool LoadEnableMakeItem()
        {
            var result = false;
            string sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "EnableMakeItem.txt";
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveEnableMakeItem()
        {
            string sFileName = g_Config.sEnvirDir + "EnableMakeItem.txt";
            
            return true;
        }

        /// <summary>
        /// Loads 传送石禁用地图.txt. Mirrors 战神 0x7944D7-0x794528: build the path,
        /// FileExists, then LoadFromFile. At startup the list is empty anyway, so
        /// the absent-file case leaves every map allowed.
        /// <para>
        /// ORDER IS LOAD-BEARING, and it only shows on the RELOAD path. Both native
        /// call sites test existence FIRST and touch the list only on success:
        /// startup <c>0x7944F5 call FileExists / 0x7944FA test al,al / 0x7944FC je
        /// 0x794528</c> (straight to the epilogue), and GM 401 <c>0x6281CC /
        /// 0x6281D1 / 0x6281D3 je 0x628204</c> (straight to the error message). The
        /// <c>LoadFromFile</c> at 0x794525 / 0x6281FC is what clears and repopulates
        /// — Delphi's <c>TStrings.LoadFromFile</c> does the Clear internally, so a
        /// missing file leaves the previously-loaded blacklist INTACT.
        /// </para>
        /// <para>
        /// Clearing before the existence check would silently un-ban every map the
        /// moment an admin reloads with the file missing or renamed — the opposite
        /// of native, and a permissive divergence on a gate. The Clear therefore
        /// belongs after the check, standing in for LoadFromFile's own clear.
        /// </para>
        /// </summary>
        public static bool LoadFixedCoordDisableMap()
        {
            var sFileName = sConfigPath + g_Config.sEnvirDir + "传送石禁用地图.txt";
            lock (FixedCoordDisableMapSync)
            {
                g_FixedCoordDisableMapList ??= new List<string>();
                if (!File.Exists(sFileName))
                {
                    return false;
                }

                // Stands in for TStrings.LoadFromFile's internal Clear (0x794525 /
                // 0x6281FC), which is reached ONLY when the file exists.
                g_FixedCoordDisableMapList.Clear();
                var loaded = File.ReadAllLines(sFileName, HUtil32.GbkEncoding);
                for (var i = 0; i < loaded.Length; i++)
                {
                    // TStrings.LoadFromFile keeps every line verbatim, including
                    // blanks; IndexOf on an empty entry can never match a real map
                    // name, so retaining them is harmless and byte-faithful.
                    g_FixedCoordDisableMapList.Add(loaded[i].Trim());
                }
            }
            return true;
        }

        /// <summary>
        /// 战神 <c>TStringList.IndexOf</c> on the 传送石禁用地图 list
        /// (0x6E9C06-0x6E9C16 in the setter <c>sub_6E9BAC</c>, and again at
        /// 0x6281F3 in the matching GM command). Native tests
        /// <c>IndexOf(map.sMapName) + 1 == 0</c>, i.e. allow only when the result is
        /// -1. The list is built with <c>CaseSensitive := False</c>, so
        /// <c>CompareStrings</c> (VMT+0x34 = 0x49F630) takes the <c>je</c> at
        /// 0x49F637 into the case-insensitive <c>sub_40BD78</c> — hence
        /// OrdinalIgnoreCase here.
        /// </summary>
        public static bool IsNativeFixedCoordBannedMap(string sMapName)
        {
            if (string.IsNullOrEmpty(sMapName))
            {
                return false;
            }
            lock (FixedCoordDisableMapSync)
            {
                var list = g_FixedCoordDisableMapList;
                if (list == null)
                {
                    return false;
                }
                for (var i = 0; i < list.Count; i++)
                {
                    if (string.Equals(list[i], sMapName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool LoadDisableMoveMap()
        {
            var result = false;
            var sFileName = g_Config.sEnvirDir + "DisableMoveMap.txt";
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveDisableMoveMap()
        {
            string sFileName = g_Config.sEnvirDir + "DisableMoveMap.txt";
            
            return true;
        }

        public static bool SaveChatLog()
        {
            var fileName = Path.Combine(sConfigPath, "ChatLog.txt");
            lock (ChatLogSync)
            {
                if (File.Exists(fileName))
                {
                    using var reader = new StreamReader(fileName, HUtil32.GbkEncoding,
                        detectEncodingFromByteOrderMarks: false);
                    while (reader.ReadLine() is { } line)
                        g_ChatLoggingList.Add(line);
                }

                var content = new StringBuilder();
                for (var i = 0; i < g_ChatLoggingList.Count; i++)
                    content.Append(g_ChatLoggingList[i]).Append("\r\n");
                File.WriteAllText(fileName, content.ToString(), HUtil32.GbkEncoding);
            }
            return true;
        }

        internal static void AppendChatLog(string characterName, string message)
        {
            lock (ChatLogSync)
            {
                var line = '[' + DateTime.Now.ToString("G", CultureInfo.CurrentCulture) + "] "
                           + characterName + ": " + message;
                g_ChatLoggingList.Add(line);
            }
        }

        public static int GetUseItemIdx(string sName)
        {
            int result = -1;
            if (string.Compare(sName, U_DRESSNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 0;
            }
            else if (string.Compare(sName, U_WEAPONNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 1;
            }
            else if (string.Compare(sName, U_RIGHTHANDNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 2;
            }
            else if (string.Compare(sName, U_NECKLACENAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 3;
            }
            else if (string.Compare(sName, U_HELMETNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 4;
            }
            else if (string.Compare(sName, U_ARMRINGLNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 5;
            }
            else if (string.Compare(sName, U_ARMRINGRNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 6;
            }
            else if (string.Compare(sName, U_RINGLNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 7;
            }
            else if (string.Compare(sName, U_RINGRNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 8;
            }
            else if (string.Compare(sName, U_BUJUKNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 9;
            }
            else if (string.Compare(sName, U_BELTNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 10;
            }
            else if (string.Compare(sName, U_BOOTSNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 11;
            }
            else if (string.Compare(sName, U_CHARMNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = 12;
            }
            else if (string.Compare(sName, U_MASKNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = Grobal2.U_MASK;
            }
            else if (string.Compare(sName, U_YUPEINAME, StringComparison.OrdinalIgnoreCase) == 0
                     || string.Compare(sName, U_WARDRUMNAME, StringComparison.OrdinalIgnoreCase) == 0
                     || string.Compare(sName, U_MILITARYDRUMNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = Grobal2.U_WARDRUM;
            }
            else if (string.Compare(sName, U_HORSENAME, StringComparison.OrdinalIgnoreCase) == 0
                     || string.Compare(sName, U_MOUNTNAME, StringComparison.OrdinalIgnoreCase) == 0
                     || string.Compare(sName, U_MOUNTDISPLAYNAME, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = Grobal2.U_MOUNT;
            }
            return result;
        }

        public static string GetUseItemName(int nIndex)
        {
            var result = string.Empty;
            switch (nIndex)
            {
                case 0:
                    result = U_DRESSNAME;
                    break;
                case 1:
                    result = U_WEAPONNAME;
                    break;
                case 2:
                    result = U_RIGHTHANDNAME;
                    break;
                case 3:
                    result = U_NECKLACENAME;
                    break;
                case 4:
                    result = U_HELMETNAME;
                    break;
                case 5:
                    result = U_ARMRINGLNAME;
                    break;
                case 6:
                    result = U_ARMRINGRNAME;
                    break;
                case 7:
                    result = U_RINGLNAME;
                    break;
                case 8:
                    result = U_RINGRNAME;
                    break;
                case 9:
                    result = U_BUJUKNAME;
                    break;
                case 10:
                    result = U_BELTNAME;
                    break;
                case 11:
                    result = U_BOOTSNAME;
                    break;
                case 12:
                    result = U_CHARMNAME;
                    break;
                case Grobal2.U_MASK:
                    result = U_MASKNAME;
                    break;
                case Grobal2.U_YUPEI:
                    result = U_YUPEINAME;
                    break;
                case Grobal2.U_HORSE:
                    result = U_HORSENAME;
                    break;
            }
            return result;
        }

        public static bool LoadDisableSendMsgList()
        {
            var result = false;
            string sFileName = g_Config.sEnvirDir + "DisableSendMsgList.txt";
            ArrayList LoadList = new ArrayList();
            
            
            

            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool LoadMonDropLimitList()
        {
            var sLineText = string.Empty;
            var sItemName = string.Empty;
            var sItemCount = string.Empty;
            var result = false;
            string sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "MonDropLimitList.txt";
            var LoadList = new ArrayList();
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveMonDropLimitList()
        {
            bool result;
            var sFileName = g_Config.sEnvirDir + "MonDropLimitList.txt";
            var LoadList = new ArrayList();
            
            

            
            
            
            
            
            
            result = true;
            return result;
        }

        public static bool LoadDisableTakeOffList()
        {
            var sItemName = string.Empty;
            var result = false;
            var sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DisableTakeOffList.txt";
            var LoadList = new ArrayList();
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveDisableTakeOffList()
        {
            bool result;
            ArrayList LoadList;
            string sFileName;
            sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DisableTakeOffList.txt";
            LoadList = new ArrayList();
            
            
            
            
            
            
            
            
            
            
            
            
            result = true;
            return result;
        }

        public static bool InDisableTakeOffList(int nItemIdx)
        {
            bool result = false;
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveDisableSendMsgList()
        {
            bool result;
            string sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DisableSendMsgList.txt";
            ArrayList LoadList = new ArrayList();
            for (var i = 0; i < g_DisableSendMsgList.Count; i++)
            {
                LoadList.Add(g_DisableSendMsgList[i]);
            }
            
            result = true;
            return result;
        }

        public static bool GetDisableSendMsgList(string sHumanName)
        {
            return NativeMirrorChatBan.Contains(sHumanName);
        }

        public static bool LoadGameLogItemNameList()
        {
            var sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "GameLogItemNameList.txt";
            if (!File.Exists(sFileName) || g_GameLogItemNameList == null)
                return false;

            var loaded = File.ReadAllLines(sFileName, HUtil32.GbkEncoding);
            lock (g_GameLogItemNameList)
            {
                g_GameLogItemNameList.Clear();
                for (var i = 0; i < loaded.Length; i++)
                    g_GameLogItemNameList.Add(loaded[i].Trim());
            }
            
            
            
            
            

            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return true;
        }

        public static byte GetGameLogItemNameList(string sItemName)
        {
            var list = g_GameLogItemNameList;
            if (list == null) return 0;

            lock (list)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (string.Compare(sItemName, list[i],
                            StringComparison.OrdinalIgnoreCase) == 0)
                        return 1;
                }
            }
            
            
            
            
            
            
            
            
            return 0;
        }

        public static bool SaveGameLogItemNameList()
        {
            bool result;
            var sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "GameLogItemNameList.txt";
            try
            {

                
            }
            finally
            {
            }
            result = true;
            return result;
        }

        public static bool LoadDenyIPAddrList()
        {
            var result = false;
            var sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DenyIPAddrList.txt";
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool GetDenyIPAddrList(string sIPaddr)
        {
            bool result = false;
            try
            {
                for (var i = 0; i < g_DenyIPAddrList.Count; i++)
                {
                    
                    
                    
                    
                    
                }
            }
            finally
            {
            }
            return result;
        }

        public static bool SaveDenyIPAddrList()
        {
            bool result;
            string sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DenyIPAddrList.txt";
            
            
            
            
            

            
            
            
            
            

            
            
            
            

            
            result = true;
            return result;
        }

        public static bool LoadDenyChrNameList()
        {
            var result = false;
            string sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DenyChrNameList.txt";
            
            
            
            
            

            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool GetDenyChrNameList(string sChrName)
        {
            bool result;
            result = false;
            try
            {
                
                
                
                
                
                
                
                
            }
            finally
            {
            }
            return result;
        }

        public static bool SaveDenyChrNameList()
        {
            bool result;
            string sFileName;
            sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DenyChrNameList.txt";
            
            
            
            
            

            
            
            
            
            

            
            
            
            
            
            result = true;
            return result;
        }

        public static bool LoadDenyAccountList()
        {
            var result = false;
            var sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DenyAccountList.txt";
            new ArrayList();
            if (File.Exists(sFileName))
            {
                try
                {
                    g_DenyAccountList.Clear();
                    
                    
                    
                    
                    
                }
                finally
                {
                }
                result = true;
            }
            else
            {
                
            }
            
            return result;
        }

        public static bool GetDenyAccountList(string sAccount)
        {
            bool result = false;
            try
            {
                for (var I = 0; I < g_DenyAccountList.Count; I++)
                {
                    
                    
                    
                    
                    
                }
            }
            finally
            {
            }
            return result;
        }

        public static bool SaveDenyAccountList()
        {
            bool result;
            string sFileName;
            sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "DenyAccountList.txt";
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            result = true;
            return result;
        }

        public static bool LoadNoClearMonList()
        {
            var result = false;
            var sFileName = Path.Combine(M2Share.sConfigPath, g_Config.sEnvirDir, "NoClearMonList.txt");
            StringList LoadList = null;
            if (File.Exists(sFileName))
            {
                LoadList = new StringList();
                g_NoClearMonLIst.Clear();
                LoadList.LoadFromFile(sFileName);
                for (var i = 0; i < LoadList.Count; i++)
                {
                    g_NoClearMonLIst.Add(LoadList[i].Trim());
                }
                result = true;
            }
            if (LoadList != null)
            {
                LoadList.SaveToFile(sFileName);
            }
            return result;
        }

        public static bool GetNoHptoexpMonList(string sMonName)
        {
            bool result;
            result = false;
            try
            {
                
                
                
                
                
                
                
                
            }
            finally
            {
            }
            return result;
        }

        public static bool GetNoClearMonList(string sMonName)
        {
            var result = false;
            
            
            
            
            
            
            
            
            return result;
        }

        public static bool SaveNoHptoexpMonList()
        {
            bool result;
            string sFileName;
            sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "NoHptoExpMonList.txt";
            
            
            
            
            
            
            
            
            
            
            
            
            result = true;
            return result;
        }

        public static bool SaveNoClearMonList()
        {
            bool result;
            int I;
            string sFileName;
            StringList SaveList;
            sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "NoClearMonList.txt";
            SaveList = new StringList();
            try
            {
                for (I = 0; I < g_NoClearMonLIst.Count; I++)
                {
                    
                }
                SaveList.SaveToFile(sFileName);
            }
            finally
            {
            }
            
            result = true;
            return result;
        }

        public static bool LoadMonSayMsg()
        {
            var sStatus = string.Empty;
            var sRate = string.Empty;
            var sColor = string.Empty;
            var sMonName = string.Empty;
            var sSayMsg = string.Empty;
            int nStatus;
            int nRate;
            int nColor;
            StringList LoadList;
            string sLineText;
            TMonSayMsg MonSayMsg;
            var result = false;
            var sFileName = M2Share.sConfigPath + g_Config.sEnvirDir + "GenMsg.txt";
            if (File.Exists(sFileName))
            {
                g_MonSayMsgList.Clear();
                LoadList = new StringList();
                LoadList.LoadFromFile(sFileName);
                for (var i = 0; i < LoadList.Count; i++)
                {
                    sLineText = LoadList[i].Trim();
                    if (sLineText != "" && sLineText[1] < ';')
                    {
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sStatus, new string[] { " ", "/", ",", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sRate, new string[] { " ", "/", ",", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sColor, new string[] { " ", "/", ",", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sMonName, new string[] { " ", "/", ",", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sSayMsg, new string[] { " ", "/", ",", "\t" });
                        if (sStatus != "" && sRate != "" && sColor != "" && sMonName != "" && sSayMsg != "")
                        {
                            nStatus = HUtil32.Str_ToInt(sStatus, -1);
                            nRate = HUtil32.Str_ToInt(sRate, -1);
                            nColor = HUtil32.Str_ToInt(sColor, -1);
                            if (nStatus >= 0 && nRate >= 0 && nColor >= 0)
                            {
                                MonSayMsg = new TMonSayMsg();
                                switch (nStatus)
                                {
                                    case 0:
                                        MonSayMsg.State = MonStatus.KillHuman;
                                        break;
                                    case 1:
                                        MonSayMsg.State = MonStatus.UnderFire;
                                        break;
                                    case 2:
                                        MonSayMsg.State = MonStatus.Die;
                                        break;
                                    case 3:
                                        MonSayMsg.State = MonStatus.MonGen;
                                        break;
                                    default:
                                        MonSayMsg.State = MonStatus.UnderFire;
                                        break;
                                }
                                switch (nColor)
                                {
                                    case 0:
                                        MonSayMsg.Color = MsgColor.Red;
                                        break;
                                    case 1:
                                        MonSayMsg.Color = MsgColor.Green;
                                        break;
                                    case 2:
                                        MonSayMsg.Color = MsgColor.Blue;
                                        break;
                                    case 3:
                                        MonSayMsg.Color = MsgColor.White;
                                        break;
                                    default:
                                        MonSayMsg.Color = MsgColor.White;
                                        break;
                                }
                                MonSayMsg.nRate = nRate;
                                MonSayMsg.sSayMsg = sSayMsg;
                                
                                
                                
                                

                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                            }
                        }
                    }
                }
                
                result = true;
            }
            return result;
        }

        public static void LoadConfig()
        {
            ServerConf.LoadConfig();
            StringConf.LoadString();
            ExpConf.LoadConfig();
            GlobalConf.LoadConfig();
            // Apply 战神 Share/*.ini exp/rate data over hardcoded defaults
            ZhanshenConf?.ApplyToM2Share();
        }

        public static string GetIPLocal(string sIPaddr)
        {
            return "未知!!!";
        }

        
        
        
        public static bool IsCheapStuff(byte tByte)
        {
            bool result;
            if (tByte < 0)
            {
                result = true;
            }
            else
            {
                result = false;
            }
            return result;
        }

        
        
        
        public static bool CompareIPaddr(string sIPaddr, string dIPaddr)
        {
            var result = false;
            if (sIPaddr == "" || dIPaddr == "")
            {
                return result;
            }
            if (dIPaddr[1] == '*')
            {
                result = true;
                return result;
            }
            var nPos = dIPaddr.IndexOf('*');
            if (nPos > 0)
            {
                result = HUtil32.CompareLStr(sIPaddr, dIPaddr, nPos - 1);
            }
            else
            {
                result = sIPaddr.CompareTo(dIPaddr) == 0;
            }
            return result;
        }
    }
}
