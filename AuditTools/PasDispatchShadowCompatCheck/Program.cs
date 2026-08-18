using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
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
if (args.Any(arg => string.Equals(arg, "--repair-only",
        StringComparison.OrdinalIgnoreCase)))
{
    RunRepairClickAbiRegression();
    var repairRepositoryRoot = FindRepositoryRoot();
    var repairBridgeSource = File.ReadAllText(Path.Combine(
        repairRepositoryRoot, "GameSvr", "ScriptSystem", "PasEngine",
        "PasApiBridge.cs"));
    var repairNpcMethods = Slice(repairBridgeSource,
        "public bool CallNpcMethod", "public bool CallNpcFunc");
    var repairNpcFunctions = Slice(repairBridgeSource,
        "public bool CallNpcFunc", "public bool CallStandaloneFunction");
    foreach (var procedureName in new[]
             {
                 "click_repair", "click_srepair", "click_repairex",
                 "click_repair_ex"
             })
    {
        Equal(1, Count(repairNpcMethods, $"case \"{procedureName}\":"),
            procedureName + " procedure dispatch count");
        Equal(0, Count(repairNpcFunctions, $"case \"{procedureName}\":"),
            procedureName + " function shadow count");
    }
    Console.WriteLine("PASS repair-click ABI explicit-player mode-byte " +
        "shared-RM function-shadow-free ghost-order");
    return;
}
M2Share.UserEngine.StdItemList.Add(new GoodItem
{
    Name = "normal-gift",
    StdMode = 0,
    DuraMax = 100
});
// 堆叠与否由【物品实例】的运行时类标记决定，不是模板 StdMode。两条 give 核心读的
// 都是实例 +0x14（Give 核心 0x6C89C6 `80 78 14 07`、SysGiveGift 0x6C85C5 同样字节），
// 而实例 +0x14 只由构造器写死：根构造器 sub_783788 @0x7837AE `C6 43 14 00` 写 0，
// 堆叠构造器 sub_7880F0 @0x788118 `C6 46 14 07` 写 7。选哪个构造器由工厂 sub_74C338
// 按模板 StdMode/Shape 派发，所以谓词是【类祖先】。
//
// 这里刻意选 StdMode 3 / Shape 4 = TLuckOil 作为堆叠样本，因为它是唯一能同时否掉两个
// 错误谓词的形状：
//   0074CCE5  A1 AC 1C 78 00  mov  eax,[0x781CAC]  ; TLuckOil
//   0074CCEC  E8 FF B3 03 00  call 0x7880F0        ; TBasePileItem.Create
// StdMode 3 < 150，所以「StdMode >= 150」判不出它（那条只是默认臂
// 0x74D67E `3C 96 cmp al,0x96` / 0x74D680 `72 13 jb` / 0x74D68C `call 0x7880F0`）；
// StdMode 3 != 7，所以「StdMode == 7」也判不出它。改回任一错误谓词都会让下面的
// pile 断言红。
M2Share.UserEngine.StdItemList.Add(new GoodItem
{
    Name = "pile-gift",
    StdMode = 3,
    Shape = 4,
    DuraMax = 100
});
// 反向对照：StdMode 恰好是 7 的护身符族（0x74CE9E 按 Shape 二级派发，Shape 0 ->
// TCryCharm），走的是根构造器，实例 +0x14 恒为 0，必须【不】堆叠。
// pile-gift 与 charm-gift 成对存在，任何单边谓词都过不了这两条。
// 追加在 pile-gift 之后，wIndex 1/2 的既有 fixture（TakeEx 的 NewItem(..., 2, ...)）不变。
M2Share.UserEngine.StdItemList.Add(new GoodItem
{
    Name = "charm-gift",
    StdMode = 7,
    Shape = 0,
    DuraMax = 100
});

var player = new RecalcProbePlayer
{
    m_boOffLineFlag = true,
    m_sCharName = "audit-player",
    m_sMapName = "audit-map",
    m_nCurrX = 12,
    m_nCurrY = 34,
    m_nGold = 101,
    m_nGameGold = 202,
    m_nGamePoint = 303,
    m_nPayMentPoint = 404,
    m_nShengWan = 505
};
player.m_ScriptVVars[91001] = 611;
player.m_ScriptSVars[91001] = 722;
var bridge = new PasApiBridge { CurrentPlayer = player, CurrentNpc = null };

var unsupportedBefore = PlayerSnapshot.Capture(player);
Assert(!bridge.CallPlayerFunc("ScriptRequestAddYBNum", Values(20), out var addResult),
    "ScriptRequestAddYBNum function bypassed its asynchronous account callback");
AssertNil(addResult, "ScriptRequestAddYBNum function");
Assert(bridge.CallPlayerMethod("ScriptRequestAddYBNum", Values(20)),
    "ScriptRequestAddYBNum procedure was not dispatched asynchronously");
Assert(!bridge.CallPlayerFunc("ScriptRequestSubYBNum", Values(10), out var subResult),
    "ScriptRequestSubYBNum function bypassed its asynchronous account callback");
AssertNil(subResult, "ScriptRequestSubYBNum function");
Assert(bridge.CallPlayerMethod("ScriptRequestSubYBNum", Values(10)),
    "ScriptRequestSubYBNum procedure was not dispatched asynchronously");
Assert(bridge.CallPlayerMethod("ScriptRequestAddYBNum", Values(-1)),
    "ScriptRequestAddYBNum rejected the native raw negative amount");
unsupportedBefore.AssertUnchanged(player, "asynchronous YB submission");

player.m_MsgList.Clear();
Assert(bridge.CallPlayerMethod("PlayerDialog", Values("dialog-text")),
    "PlayerDialog procedure was not dispatched");
Equal(Grobal2.SM_MERCHANTSAY, player.m_DefMsg.Ident,
    "PlayerDialog packet ident");
Equal(0, player.m_DefMsg.Recog, "PlayerDialog packet recog without NPC");
Equal(0, player.m_MsgList.Count, "PlayerDialog was queued as a system message");

foreach (var (color, packed) in new[]
         {
             (0, 0x38FF), (1, 0xFFDB), (2, 0xFCFF),
             (3, 0xFDFF), (4, 0xFFFF), (99, 0x38FF)
         })
{
    player.m_MsgList.Clear();
    Assert(bridge.CallPlayerMethod("PlayerNotice", Values("notice-text", color)),
        $"PlayerNotice color {color} was not dispatched");
    var noticeMessages = player.m_MsgList
        .Where(message => message.wIdent == Grobal2.RM_SYSMESSAGE).ToArray();
    Equal(1, noticeMessages.Length, $"PlayerNotice color {color} message count");
    Equal(packed & 0xFF, noticeMessages[0].nParam1,
        $"PlayerNotice color {color} foreground");
    Equal((packed >> 8) & 0xFF, noticeMessages[0].nParam2,
        $"PlayerNotice color {color} background");
    Assert(noticeMessages[0].Buff == "notice-text",
        $"PlayerNotice color {color} text");
}

var ybBeforeSubmit = player.m_nGameGold;
Assert(!bridge.CallPlayerMethod("PsYBConsum", Values(0, "callback", 1, 10, 2)),
    "PsYBConsum procedure shadow remains exposed");
Assert(!bridge.CallPlayerMethod("PsYBConsumEx",
        Values(1, "callback", "description", 1, 10, 2)),
    "PsYBConsumEx procedure shadow remains exposed");
Equal(ybBeforeSubmit, player.m_nGameGold,
    "PsYBConsum procedure shadow changed local currency");
M2Share.PasEngine = new PasScriptHost(Path.Combine(Path.GetTempPath(),
    "native-pas-yb-purchase-check"));
var purchaseNpc = new NormNpc();
Assert(bridge.CallPlayerFunc("PsYBConsum",
        Values(purchaseNpc, "callback", 20001, 10, 2),
        out var ybConsumeResult),
    "PsYBConsum function was not dispatched asynchronously");
Assert(ybConsumeResult.AsBool(),
    "PsYBConsum did not return True after reserving UserId 0");
Assert(bridge.CallPlayerFunc("PsYBConsumEx",
        Values(1, "callback", "description", 1, 10, 2), out ybConsumeResult),
    "PsYBConsumEx tag1 function was not dispatched");
Assert(!ybConsumeResult.AsBool(), "PsYBConsumEx tag1 did not fail closed");
typeof(TPlayObject).GetMethod("LoadNativeMailRecipientId",
    System.Reflection.BindingFlags.Instance |
    System.Reflection.BindingFlags.NonPublic)!.Invoke(player, new object[] { -1L });
Assert(bridge.CallPlayerFunc("PsYBConsumEx",
        Values(2, "callback", "description", 1, 10, 2), out ybConsumeResult),
    "PsYBConsumEx tag2 function was not dispatched asynchronously");
Assert(ybConsumeResult.AsBool(),
    "PsYBConsumEx tag2 did not return True after reserving UserId -1");
Equal(ybBeforeSubmit, player.m_nGameGold,
    "PsYBConsum asynchronous submission changed local currency");

Assert(!bridge.CallPlayerFunc("RandomFlyTo", Values("missing-map"), out var randomResult),
    "RandomFlyTo function still shadows the native procedure");
AssertNil(randomResult, "RandomFlyTo function");
Assert(bridge.CallPlayerMethod("RandomFlyTo", Values("missing-map")),
    "RandomFlyTo procedure dispatch is missing");

var previousEnvironment = player.m_PEnvir;
var previousObMode = player.m_boObMode;
var previousHair = player.m_btHair;
player.m_PEnvir = new Envirnoment();
player.m_boObMode = true;
player.m_btHair = 9;
player.m_MsgList.Clear();
Assert(bridge.CallPlayerFunc("ChgHair", Values(1), out var hairResult),
    "ChgHair function was not dispatched");
Assert(hairResult.AsBool(), "ChgHair valid kind returned False");
Equal((byte)1, player.m_btHair, "ChgHair valid kind");
Equal(1, player.m_MsgList.Count, "ChgHair queued message count");
Equal(Grobal2.RM_FEATURECHANGED, player.m_MsgList[0].wIdent,
    "ChgHair queued message ident");
Assert(bridge.CallPlayerFunc("ChgHair", Values(2), out hairResult),
    "ChgHair invalid-kind function was not dispatched");
Assert(!hairResult.AsBool(), "ChgHair invalid kind returned True");
Equal((byte)1, player.m_btHair, "ChgHair invalid kind changed hair");
Equal(1, player.m_MsgList.Count, "ChgHair invalid kind queued a message");
Assert(!bridge.CallPlayerMethod("ChgHair", Values(1)),
    "ChgHair procedure shadows the native function");
Assert(!bridge.CallPlayerMethod("ChgSelfHair", Values(1)),
    "ChgSelfHair GM command leaked into the PAS procedure surface");
player.m_MsgList.Clear();
player.m_btHair = previousHair;
player.m_boObMode = previousObMode;
player.m_PEnvir = previousEnvironment;

Assert(bridge.CallPlayerFunc("SysGiveGift", Values("normal-gift", 2, false), out var giftResult),
    "SysGiveGift function was not dispatched without an NPC");
Assert(giftResult.AsBool(), "SysGiveGift rejected a valid normal gift");
Equal(2, player.m_ItemList.Count, "normal gift item count");
Assert(player.m_ItemList.All(item => item.wIndex == 1 && item.Bind == 0),
    "normal gifts have incorrect item identity or binding");

player.m_ItemList.Clear();
Assert(bridge.CallPlayerFunc("SysGiveGift", Values("normal-gift", 0, true), out giftResult),
    "SysGiveGift zero-count call was not dispatched");
Assert(giftResult.AsBool(), "SysGiveGift did not normalize a non-positive count to one");
Equal(1, player.m_ItemList.Count, "normalized gift item count");
Equal(1, player.m_ItemList[0].Bind, "bound gift flag");

player.m_ItemList.Clear();
Assert(bridge.CallPlayerFunc("SysGiveGift", Values("pile-gift", 250, true), out giftResult),
    "SysGiveGift pile call was not dispatched");
Assert(giftResult.AsBool(), "SysGiveGift rejected a valid pile gift");
Equal(3, player.m_ItemList.Count, "pile gift split count");
Assert(player.m_ItemList.Select(item => item.Dura).SequenceEqual(new ushort[] { 100, 100, 50 }),
    "pile gift quantities were not split through Dura");
Assert(player.m_ItemList.All(item => item.DuraMax == 100 && item.Bind == 1),
    "pile gift DuraMax or binding is incorrect");

// 反向对照：同一条 SysGiveGift 核心、同一个数量，StdMode==7 只是护身符，
// 必须发 3 件独立物品，Dura 保持模板 DuraMax 而不是被写成堆内件数。
player.m_ItemList.Clear();
Assert(bridge.CallPlayerFunc("SysGiveGift", Values("charm-gift", 3, false),
        out giftResult),
    "SysGiveGift StdMode=7 charm call was not dispatched");
Assert(giftResult.AsBool(), "SysGiveGift rejected a StdMode=7 charm");
Equal(3, player.m_ItemList.Count,
    "SysGiveGift treated StdMode=7 as a pile (native pile gate is the instance "
    + "kind byte at 0x6C85C5, written only by the pile ctor sub_7880F0 @0x788118, "
    + "not the template StdMode)");
Assert(player.m_ItemList.All(item => item.Dura == 100),
    "SysGiveGift overwrote a StdMode=7 charm's Dura with a stack count");

FillBag(player, Grobal2.MAXBAGITEM);
var fullBag = player.m_ItemList.ToArray();
Assert(bridge.CallPlayerFunc("SysGiveGift", Values("normal-gift", 1, false), out giftResult),
    "full-bag SysGiveGift function was not dispatched");
Assert(!giftResult.AsBool(), "full-bag SysGiveGift reported success");
Assert(fullBag.SequenceEqual(player.m_ItemList), "full-bag SysGiveGift changed inventory");

FillBag(player, Grobal2.MAXBAGITEM - 1);
Assert(bridge.CallPlayerFunc("SysGiveGift", Values("normal-gift", 2, false), out giftResult),
    "partial SysGiveGift function was not dispatched");
Assert(giftResult.AsBool(), "partial SysGiveGift lost the native any-success result");
Equal(Grobal2.MAXBAGITEM, player.m_ItemList.Count, "partial SysGiveGift bag count");

player.m_ItemList.Clear();
Assert(bridge.CallPlayerFunc("Give", Values("normal-gift", 2), out var giveResult),
    "Give function was not dispatched");
Assert(giveResult.AsBool(), "Give rejected a valid normal item");
Equal(2, player.m_ItemList.Count, "Give normal item count");
Assert(player.m_ItemList.All(item => item.wIndex == 1 && item.Bind == 0),
    "Give normal item identity or binding");

// 共享 give 核心 sub_6C87B4 的冒号解析：0x6C8812 `B1 3A mov cl,0x3A` +
// 0x6C8817 `call 0x4C6AEC` 按 ':' 一刀两段（头=物品名、尾=数量），
// 0x6C8821 `call 0x40CA18` StrToIntDef(尾串, 入参 count)，
// 0x6C882C `85 DB test ebx,ebx` / 0x6C882E `7F 05 jg` / 0x6C8830 `BB 01 00 00 00
// mov ebx,1` 把非正数归一。
// 堆叠拆分：0x6C89C6 `80 78 14 07` 判实例类标记，0x6C89CF `66 8B 40 28` 取实例
// +0x28(DuraMax)，0x6C89D6 `3B DA cmp ebx,edx` / 0x6C89D8 `7F 0D jg` 决定是写满一堆
// （0x6C89EA `Dura := DuraMax`、0x6C89F5 `2B D8 sub ebx,eax`）还是收尾一堆
// （0x6C89DD `66 89 58 26 Dura := 剩余`、0x6C89E1 `C6 45 E7 01` 置结束标记，
// 由 0x6C8AE4 检测后 0x6C8AE8 跳出）。250 / DuraMax=100 -> 100,100,50。
player.m_ItemList.Clear();
Assert(bridge.CallPlayerFunc("Give", Values("pile-gift:250", 1), out giveResult),
    "Give colon-count function was not dispatched");
Assert(giveResult.AsBool(), "Give rejected a valid colon count");
Assert(player.m_ItemList.Select(item => item.Dura)
        .SequenceEqual(new ushort[] { 100, 100, 50 }),
    "Give colon count did not split the pile through Dura");

// 反向对照：同一条核心、同一个冒号计数，StdMode==7 只是护身符，必须发 3 件独立物品。
player.m_ItemList.Clear();
Assert(bridge.CallPlayerFunc("Give", Values("charm-gift:3", 1), out giveResult),
    "Give StdMode=7 charm function was not dispatched");
Assert(giveResult.AsBool(), "Give rejected a StdMode=7 charm");
Equal(3, player.m_ItemList.Count,
    "Give treated StdMode=7 as a pile (native reads the instance kind byte at "
    + "0x6C89C6; sub_74C338 routes StdMode 7 to the charm family at 0x74CE9E, "
    + "which never reaches the pile ctor)");
Assert(player.m_ItemList.All(item => item.Dura == 100),
    "Give overwrote a StdMode=7 charm's Dura with a stack count");

player.m_ItemList.Clear();
Assert(bridge.CallPlayerFunc("Give", Values("normal-gift:not-a-number", 2),
        out giveResult),
    "Give invalid colon-count function was not dispatched");
Assert(giveResult.AsBool(), "Give did not fall back to the requested count");
Equal(2, player.m_ItemList.Count, "Give invalid colon-count fallback");

foreach (var parserCase in new[]
         {
             (Source: "normal-gift:  +3", Requested: 1, Expected: 3),
             (Source: "normal-gift:0x4", Requested: 1, Expected: 4),
             (Source: "normal-gift:X5", Requested: 1, Expected: 5),
             (Source: "normal-gift:-$FFFFFFFE", Requested: 1, Expected: 2),
             (Source: "normal-gift:$FFFFFFFF", Requested: 7, Expected: 1),
             (Source: "normal-gift:x80000000", Requested: 7, Expected: 1),
             (Source: "normal-gift:3 ", Requested: 2, Expected: 2),
             (Source: "normal-gift:2147483648", Requested: 2, Expected: 2),
             (Source: "normal-gift:999999999999999999999999", Requested: 2,
                 Expected: 2)
         })
{
    player.m_ItemList.Clear();
    Assert(bridge.CallPlayerFunc("Give",
            Values(parserCase.Source, parserCase.Requested), out giveResult),
        "Give Delphi integer parser case was not dispatched: " + parserCase.Source);
    Assert(giveResult.AsBool(),
        "Give Delphi integer parser case returned False: " + parserCase.Source);
    Equal(parserCase.Expected, player.m_ItemList.Count,
        "Give Delphi integer parser case: " + parserCase.Source);
}

player.m_ItemList.Clear();
Assert(bridge.CallPlayerFunc("BindGive", Values("normal-gift", 1), out giveResult),
    "BindGive function was not dispatched");
Assert(giveResult.AsBool(), "BindGive rejected a valid item");
Equal(1, player.m_ItemList.Count, "BindGive item count");
Equal(1, player.m_ItemList[0].Bind, "BindGive item binding");

player.m_ItemList.Clear();
Assert(bridge.CallPlayerMethod("GiveBindItem", Values("normal-gift", 1)),
    "GiveBindItem procedure was not dispatched");
Equal(1, player.m_ItemList.Count, "GiveBindItem item count");
Equal(1, player.m_ItemList[0].Bind, "GiveBindItem item binding");

FillBag(player, Grobal2.MAXBAGITEM);
var nativeGiveFullBag = player.m_ItemList.ToArray();
Assert(bridge.CallPlayerFunc("Give", Values("normal-gift", 1), out giveResult),
    "full-bag Give function was not dispatched");
Assert(!giveResult.AsBool(), "full-bag Give reported success");
Assert(nativeGiveFullBag.SequenceEqual(player.m_ItemList),
    "full-bag Give changed inventory or dropped the item");

FillBag(player, Grobal2.MAXBAGITEM - 1);
Assert(bridge.CallPlayerFunc("Give", Values("normal-gift", 2), out giveResult),
    "partial Give function was not dispatched");
Assert(giveResult.AsBool(), "partial Give lost the native any-success result");
Equal(Grobal2.MAXBAGITEM, player.m_ItemList.Count, "partial Give bag count");

player.m_ItemList.Clear();
Assert(bridge.CallPlayerFunc("LoopGive", Values("normal-gift", 1, 3),
        out giveResult),
    "LoopGive function was not dispatched");
Assert(giveResult.AsBool(), "valid LoopGive returned False");
Equal(3, player.m_ItemList.Count, "LoopGive item count");
Assert(bridge.CallPlayerFunc("LoopGive", Values("missing-item", 1, 2),
        out giveResult),
    "failing LoopGive function was not dispatched");
Assert(giveResult.AsBool(), "LoopGive did not ignore individual Give failures");
Assert(bridge.CallPlayerFunc("LoopGive", Values("normal-gift", 1, 0),
        out giveResult),
    "invalid LoopGive function was not dispatched");
Assert(!giveResult.AsBool(), "invalid LoopGive returned True");

player.m_nGold = 101;
Assert(bridge.CallPlayerFunc("Give", Values("金币", 9), out giveResult),
    "Give gold function was not dispatched");
Assert(giveResult.AsBool(), "Give gold returned False");
Equal(110, player.m_nGold, "Give gold amount");
player.m_nShengWan = 505;
Assert(bridge.CallPlayerFunc("Give", Values("声望", 7), out giveResult),
    "Give reputation function was not dispatched");
Assert(giveResult.AsBool(), "Give reputation returned False");
Equal(512, player.m_nShengWan, "Give reputation amount");

player.m_Abil.Level = 10;
player.m_Abil.Exp = 20;
player.m_Abil.MaxExp = int.MaxValue;
player.m_dwFightExp = 30;
player.m_dBodyLuck = 123.5;
var activeHero = new HeroObject();
activeHero.m_Abil.Level = 10;
activeHero.m_Abil.Exp = 0;
activeHero.m_Abil.MaxExp = int.MaxValue;
activeHero.m_dwFightExp = 0;
player.m_HeroObject = activeHero;
Assert(bridge.CallPlayerFunc("Give", Values("经验", 100), out giveResult),
    "Give experience function was not dispatched");
Assert(giveResult.AsBool(), "Give experience returned False");
Equal(120, player.m_Abil.Exp, "Give experience amount");
Equal(130, player.m_dwFightExp, "Give native fight-experience statistic");
Assert(player.m_dBodyLuck == 123.5,
    "Give experience incorrectly changed body luck");
Assert(activeHero.m_Abil.Exp >= 8 && activeHero.m_Abil.Exp <= 12,
    "Give experience hero share was outside native 8..12 percent range");
Equal(activeHero.m_Abil.Exp, activeHero.m_dwFightExp,
    "Give experience hero-share fight statistic");

var heroFightExperience = activeHero.m_dwFightExp;
var heroExperience = activeHero.m_Abil.Exp;
Assert(bridge.CallPlayerFunc("Give", Values("英雄经验", 30), out giveResult),
    "Give hero experience function was not dispatched");
Assert(giveResult.AsBool(), "Give hero experience returned False");
Equal(heroExperience + 30, activeHero.m_Abil.Exp,
    "Give direct hero experience amount");
Equal(heroFightExperience, activeHero.m_dwFightExp,
    "Give direct hero experience changed the fight statistic");

activeHero.m_Abil.Level = 200;
heroExperience = activeHero.m_Abil.Exp;
Assert(bridge.CallPlayerFunc("Give", Values("英雄经验", 30), out giveResult),
    "Give full-level hero experience function was not dispatched");
Assert(giveResult.AsBool(), "Give full-level hero experience returned False");
Equal(heroExperience, activeHero.m_Abil.Exp,
    "Give full-level hero experience changed experience");

player.m_HeroObject = null;
Assert(bridge.CallPlayerFunc("Give", Values("英雄经验", 30), out giveResult),
    "Give missing-hero experience function was not dispatched");
Assert(giveResult.AsBool(), "Give missing-hero experience returned False");

player.m_Abil.Level = 1;
player.m_Abil.Exp = 0;
player.m_Abil.MaxExp = 10;
M2Share.g_Config.dwNeedExps[2] = 10;
M2Share.g_Config.dwNeedExps[3] = 1000;
Assert(bridge.CallPlayerFunc("Give", Values("经验", 25), out giveResult),
    "Give multi-level experience function was not dispatched");
Assert(giveResult.AsBool(), "Give multi-level experience returned False");
Equal(3, player.m_Abil.Level, "Give experience multi-level result");
Equal(5, player.m_Abil.Exp, "Give experience multi-level remainder");

RunNativeExperienceRegressions();
RunNativeForceRegressions();
RunCanonicalItemGiveLogRegression();

M2Share.CreditCardService = NativeCreditCardService.Disabled;
var lingFuBefore = player.m_nLingFu;
foreach (var accountName in new[] { "灵符", "限时灵符" })
{
    Assert(bridge.CallPlayerFunc("Give", Values(accountName, 10), out giveResult),
        accountName + " Give function was not dispatched");
    Assert(giveResult.AsBool(), accountName + " Give returned False");
}
Equal(lingFuBefore + 20, player.m_nLingFu,
    "Give LingFu account-switch routing");

var gloryPointBefore = player.m_CreditCard.GloryPointValue;
Assert(bridge.CallPlayerFunc("Give", Values("荣耀点", 10), out giveResult),
    "荣耀点 Give function was not dispatched");
Assert(giveResult.AsBool(), "荣耀点 Give returned False");
Equal(gloryPointBefore + 10, player.m_CreditCard.GloryPointValue,
    "Give GloryPoint account value");

var vitalityLingFuBefore = player.m_nLingFu;
var vitalityGloryBefore = player.m_CreditCard.GloryPointValue;
var vitalityValueBefore = player.m_NativeCattle.Value;
Assert(bridge.CallPlayerFunc("Give", Values("牛气值", 10), out giveResult),
    "牛气值 Give function was not dispatched");
Assert(giveResult.AsBool(), "牛气值 Give returned False");
Equal(unchecked(vitalityValueBefore + 10), player.m_NativeCattle.Value,
    "牛气值 Give session value");
Equal(vitalityLingFuBefore, player.m_nLingFu,
    "牛气值 Give changed LingFu");
Equal(vitalityGloryBefore, player.m_CreditCard.GloryPointValue,
    "牛气值 Give changed GloryPoint");
Assert(player.m_MsgList.Any(message =>
        message.wIdent == Grobal2.RM_CATTLE_SYSMESSAGE &&
        message.wParam == 0xFB && message.Buff == "10 点牛气值增加"),
    "牛气值 Give notice");

player.m_ItemList.Clear();
player.m_ItemList.Add(new TUserItem
{
    MakeIndex = 20001,
    wIndex = 1,
    Dura = 100,
    DuraMax = 100
});
M2Share.LogStringList.Clear();
Assert(bridge.CallPlayerFunc("TakeEx", Values("normal-gift", 2, "audit", true),
        out var takeExResult),
    "TakeEx function was not dispatched");
Assert(!takeExResult.AsBool(), "native TakeEx return-value defect was not preserved");
Equal(0, player.m_ItemList.Count, "TakeEx non-atomic shortfall bag count");
Equal(1, M2Share.LogStringList.Count, "TakeEx non-atomic shortfall log count");
Assert((string)M2Share.LogStringList[0] ==
       "10\taudit-map\t12\t34\taudit-player\tnormal-gift\t20001\t1\taudit 收取",
    "TakeEx normal-item log does not match native columns");
Assert(!bridge.CallPlayerMethod("TakeEx", Values("normal-gift", 2, "audit", true)),
    "TakeEx procedure still shadows the native function");

M2Share.LogStringList.Clear();
player.m_UseItems[0] = NewItem(21000, 1, 100);
player.m_UseItems[1] = NewItem(21001, 1, 100);
player.m_ItemList.Add(NewItem(21010, 1, 100));
player.m_ItemList.Add(NewItem(21011, 1, 100));
player.m_ItemList.Add(NewItem(21012, 1, 100));
Assert(bridge.CallPlayerFunc("TakeEx", Values("normal-gift", 2, "equip", true),
        out takeExResult),
    "equipment TakeEx function was not dispatched");
Assert(!takeExResult.AsBool(), "equipment TakeEx returned True");
Assert(player.m_UseItems[0] == null && player.m_UseItems[1] == null,
    "TakeEx did not remove every matching equipped item");
Assert(player.m_ItemList.Select(item => item.MakeIndex).SequenceEqual(new[] { 21010 }),
    "TakeEx did not remove bag items from the tail");
Equal(4, M2Share.LogStringList.Count, "equipment TakeEx log count");
Assert(((string)M2Share.LogStringList[0]).Contains("\t21000\t1\tequip 收取") &&
       ((string)M2Share.LogStringList[1]).Contains("\t21001\t1\tequip 收取") &&
       ((string)M2Share.LogStringList[2]).Contains("\t21012\t1\tequip 收取") &&
       ((string)M2Share.LogStringList[3]).Contains("\t21011\t1\tequip 收取"),
    "TakeEx equipment/bag log order differs from native");
Equal(Grobal2.SM_TAKEOFF_OK, player.m_DefMsg.Ident,
    "TakeEx equipment refresh message");

M2Share.LogStringList.Clear();
player.m_ItemList.Clear();
player.m_ItemList.Add(NewItem(22000, 2, 31));
Assert(bridge.CallPlayerFunc("TakeEx", Values("pile-gift", 30, "pile", false),
        out takeExResult),
    "pile TakeEx function was not dispatched");
Assert(!takeExResult.AsBool(), "pile TakeEx returned True");
Equal(1, player.m_ItemList.Count, "partial pile TakeEx bag count");
Equal(1, player.m_ItemList[0].Dura, "partial pile TakeEx quantity");
Equal(Grobal2.SM_BAGITEMDURACHG, player.m_DefMsg.Ident,
    "partial pile TakeEx refresh message");
Assert(((string)M2Share.LogStringList[0]).EndsWith("\tpile 收取30个"),
    "partial pile TakeEx reason/count log");

M2Share.LogStringList.Clear();
player.m_ItemList.Clear();
player.m_ItemList.Add(NewItem(22010, 2, 40));
player.m_ItemList.Add(NewItem(22011, 2, 25));
Assert(bridge.CallPlayerFunc("TakeEx", Values("pile-gift", 50, "multi", false),
        out takeExResult),
    "multi-pile TakeEx function was not dispatched");
Assert(!takeExResult.AsBool(), "multi-pile TakeEx returned True");
Equal(1, player.m_ItemList.Count, "multi-pile TakeEx bag count");
Equal(15, player.m_ItemList[0].Dura, "multi-pile TakeEx remaining quantity");
Assert(((string)M2Share.LogStringList[0]).EndsWith("\tmulti 收取50个") &&
       ((string)M2Share.LogStringList[1]).EndsWith("\tmulti 收取25个"),
    "multi-pile TakeEx native remaining-count log order");

player.m_nGold = 101;
Assert(bridge.CallPlayerFunc("DecGold", Values(1), out var goldResult),
    "DecGold function was not dispatched");
Assert(goldResult.AsBool(), "DecGold success did not return True");
Equal(100, player.m_nGold, "gold after successful DecGold");
Assert(bridge.CallPlayerFunc("DecGold", Values(101), out goldResult),
    "failed DecGold function was not dispatched");
Assert(!goldResult.AsBool(), "insufficient DecGold returned True");
Equal(100, player.m_nGold, "failed DecGold changed gold");
Assert(!bridge.CallPlayerMethod("DecGold", Values(1)),
    "DecGold procedure still shadows the native function");

M2Share.g_Config.nHumanMaxGold = 101;
player.m_nGoldMax = 101;
Assert(bridge.CallPlayerFunc("AddGold", Values(1), out goldResult),
    "AddGold function was not dispatched");
Assert(goldResult.AsBool(), "AddGold success did not return True");
Equal(101, player.m_nGold, "gold after successful AddGold");
Assert(bridge.CallPlayerFunc("AddGold", Values(1), out goldResult),
    "failed AddGold function was not dispatched");
Assert(!goldResult.AsBool(), "over-limit AddGold returned True");
Equal(101, player.m_nGold, "failed AddGold changed gold");
Assert(!bridge.CallPlayerMethod("AddGold", Values(1)),
    "AddGold procedure still shadows the native function");

var magicInfo = new TMagic
{
    wMagicID = 43,
    sMagicName = "hero-skill",
    btTrainLv = 4,
    MaxTrain = new[] { 1000, 2000, 3000, 4000 }
};
M2Share.UserEngine.m_MagicList.Add(magicInfo);
M2Share.UserEngine.m_HeroMagicList.Add(magicInfo);
var playerMagic = new TUserMagic
{
    MagicInfo = magicInfo,
    wMagIdx = magicInfo.wMagicID
};
var heroMagic = new TUserMagic
{
    MagicInfo = magicInfo,
    wMagIdx = magicInfo.wMagicID
};
player.m_MagicList.Add(playerMagic);
Assert(bridge.CallPlayerFunc("UpGradeHeroSkill", Values(43, 100),
        out var heroSkillResult),
    "missing-hero UpGradeHeroSkill was not dispatched");
Assert(!heroSkillResult.AsBool(), "missing-hero UpGradeHeroSkill returned True");

var hero = new HeroObject();
hero.m_MagicList.Add(heroMagic);
player.m_HeroObject = hero;
hero.m_boDeath = true;
Assert(bridge.CallPlayerFunc("UpGradeHeroSkill", Values(43, 100),
        out heroSkillResult),
    "dead-hero UpGradeHeroSkill was not dispatched");
Assert(!heroSkillResult.AsBool(), "dead-hero UpGradeHeroSkill returned True");
hero.m_boDeath = false;
Assert(bridge.CallPlayerFunc("UpGradeHeroSkill", Values(43, 100),
        out heroSkillResult),
    "valid UpGradeHeroSkill was not dispatched");
Assert(heroSkillResult.AsBool(), "valid UpGradeHeroSkill returned False");
Equal(100, heroMagic.nTranPoint, "hero skill experience");
Equal(0, playerMagic.nTranPoint, "player skill was changed by hero upgrade");
Assert(!bridge.CallPlayerMethod("UpGradeHeroSkill", Values(43, 100)),
    "UpGradeHeroSkill procedure still shadows the native function");

playerMagic.btLevel = 2;
heroMagic.btLevel = 3;
Assert(bridge.CallPlayerFunc("GetSkillLevelExt", Values("hero-skill", false),
        out var levelResult),
    "GetSkillLevelExt player query was not dispatched");
Equal(2, levelResult.AsInt(), "GetSkillLevelExt player level");
Assert(bridge.CallPlayerFunc("GetSkillLevelExt", Values("hero-skill", true),
        out levelResult),
    "GetSkillLevelExt hero query was not dispatched");
Equal(3, levelResult.AsInt(), "GetSkillLevelExt hero level");
Assert(bridge.CallPlayerFunc("GetSkillLevelExt", Values("missing-skill", false),
        out levelResult),
    "GetSkillLevelExt missing query was not dispatched");
Equal(-1, levelResult.AsInt(), "GetSkillLevelExt missing level");
Assert(bridge.CallPlayerFunc("GetSkillLevelByScript", Values("hero-skill", true),
        out levelResult),
    "GetSkillLevelByScript hero query was not dispatched");
Equal(3, levelResult.AsInt(), "GetSkillLevelByScript hero level");
hero.m_boDeath = true;
Assert(bridge.CallPlayerFunc("GetSkillLevelByScript", Values("hero-skill", true),
        out levelResult),
    "GetSkillLevelByScript dead-hero query was not dispatched");
Equal(3, levelResult.AsInt(), "GetSkillLevelByScript dead-hero level");
hero.m_boDeath = false;
hero.m_boGhost = true;
Assert(bridge.CallPlayerFunc("GetSkillLevelByScript", Values("hero-skill", true),
        out levelResult),
    "GetSkillLevelByScript ghost-hero query was not dispatched");
Equal(-1, levelResult.AsInt(), "GetSkillLevelByScript ghost-hero level");
hero.m_boGhost = false;

var multiMagicInfo = new TMagic
{
    wMagicID = 44,
    sMagicName = "multi-skill",
    btTrainLv = 3,
    TrainLevel = new byte[] { 0, 0, 0, 0 },
    MaxTrain = new[] { 100, 200, 300, 400 }
};
var multiMagic = new TUserMagic
{
    MagicInfo = multiMagicInfo,
    wMagIdx = multiMagicInfo.wMagicID
};
M2Share.UserEngine.m_MagicList.Add(multiMagicInfo);
player.m_MagicList.Add(multiMagic);
player.m_boFastTrain = true;
Assert(bridge.CallPlayerFunc("AddSkillExp", Values("MULTI-SKILL", 50),
        out var addSkillExpResult),
    "AddSkillExp function was not dispatched");
Assert(addSkillExpResult.AsBool(), "valid AddSkillExp returned False");
Equal(0, multiMagic.btLevel, "AddSkillExp incorrectly crossed a threshold");
Equal(50, multiMagic.nTranPoint, "AddSkillExp incorrectly applied fast-train multiplier");
player.m_boFastTrain = false;
Assert(bridge.CallPlayerFunc("AddSkillExp", Values("multi-skill", 300),
        out addSkillExpResult),
    "multi-level AddSkillExp function was not dispatched");
Assert(addSkillExpResult.AsBool(), "multi-level AddSkillExp returned False");
Equal(2, multiMagic.btLevel, "AddSkillExp did not cross every available level");
Equal(50, multiMagic.nTranPoint, "AddSkillExp multi-level remainder");
Assert(bridge.CallPlayerFunc("AddSkillExp", Values("multi-skill", 0),
        out addSkillExpResult),
    "zero AddSkillExp function was not dispatched");
Assert(!addSkillExpResult.AsBool(), "zero AddSkillExp returned True");
Equal(2, multiMagic.btLevel, "zero AddSkillExp changed level");
Equal(50, multiMagic.nTranPoint, "zero AddSkillExp changed experience");
multiMagic.btLevel = 0;
multiMagic.nTranPoint = 0;
multiMagicInfo.TrainLevel[0] = 2;
var priorSkillActorLevel = player.m_Abil.Level;
player.m_Abil.Level = 1;
Assert(bridge.CallPlayerFunc("AddSkillExp", Values("multi-skill", 100),
        out addSkillExpResult),
    "level-gated AddSkillExp function was not dispatched");
Assert(!addSkillExpResult.AsBool(), "level-gated AddSkillExp returned True");
Equal(0, multiMagic.btLevel, "level-gated AddSkillExp changed level");
Equal(0, multiMagic.nTranPoint, "level-gated AddSkillExp changed experience");
player.m_Abil.Level = priorSkillActorLevel;
multiMagicInfo.TrainLevel[0] = 0;

var unionMagicInfo = new TMagic
{
    wMagicID = 69,
    sMagicName = "union-skill",
    btTrainLv = 100,
    TrainLevel = new byte[] { 0, 0, 0, 0 }
};
var unionMagic = new TUserMagic
{
    MagicInfo = unionMagicInfo,
    wMagIdx = unionMagicInfo.wMagicID
};
M2Share.UserEngine.m_MagicList.Add(unionMagicInfo);
player.m_MagicList.Add(unionMagic);
Assert(bridge.CallPlayerFunc("AddSkillExp", Values("union-skill", 1000),
        out addSkillExpResult),
    "union AddSkillExp function was not dispatched");
Assert(addSkillExpResult.AsBool(), "union AddSkillExp returned False");
Equal(2, unionMagic.btLevel, "union AddSkillExp level");
Equal(200, unionMagic.nTranPoint, "union AddSkillExp remainder");
var unionHeaderMethod = typeof(PasApiBridge).GetMethod(
    "BuildNativeUnionSkillProgressHeader",
    System.Reflection.BindingFlags.Static |
    System.Reflection.BindingFlags.NonPublic)!;
var unionBodyMethod = typeof(PasApiBridge).GetMethod(
    "BuildNativeUnionSkillProgressBody",
    System.Reflection.BindingFlags.Static |
    System.Reflection.BindingFlags.NonPublic)!;
var unionHeader = (ClientPacket)unionHeaderMethod.Invoke(null,
    new object[] { player, unionMagic })!;
var unionBody = (byte[])unionBodyMethod.Invoke(null,
    new object[] { unionMagic })!;
Equal(2885, unionHeader.Ident, "union progress packet ident");
Equal(player.ObjectId, unionHeader.Recog, "union progress packet recog");
// 原来这里把 Param/Series 反了。sub_744E88 的推参与 sub_6D7BF8 的落位可以对死：
//   0x00744ED7 e8 60 36 d8 ff  call 0x4C853C   -> ax = MagicInfo.wMagicID
//   0x00744EDC 50              push eax        -> 栈第 1 个 = [ebp+0x18]
//   0x00744EDD 6a 00           push 0          -> [ebp+0x14]
//   0x00744EDF 6a 00           push 0          -> [ebp+0x10]
//   0x00744EE9 66 ba 45 0b     mov dx,0xB45    -> Ident 2885
// 收方 sub_6D7BF8（两张 VMT 的 +0x254 都是它）按 TDefaultMessage 逐字段落位：
//   0x006D7C6D 66 8b 45 fe  mov ax,[ebp-2]     / 0x006D7C71 -> [ebp-0x10] Ident
//   0x006D7C75 66 8b 45 18  mov ax,[ebp+0x18]  / 0x006D7C79 -> [ebp-0x0E] Param
//   0x006D7C7D 66 8b 45 14  mov ax,[ebp+0x14]  / 0x006D7C81 -> [ebp-0x0C] Tag
//   0x006D7C85 66 8b 45 10  mov ax,[ebp+0x10]  / 0x006D7C89 -> [ebp-0x0A] Series
// 即 Param=wMagicID、Tag=0、Series=0。
Equal(unionMagicInfo.wMagicID, unionHeader.Param, "union progress packet param");
Equal(0, unionHeader.Tag, "union progress packet tag");
Equal(0, unionHeader.Series, "union progress packet series");
Equal(20, unionBody.Length, "union progress body size");
Equal(unionMagicInfo.wMagicID,
    BinaryPrimitives.ReadInt32LittleEndian(unionBody.AsSpan(0, 4)),
    "union progress body magic id");
Equal(2, BinaryPrimitives.ReadInt32LittleEndian(unionBody.AsSpan(4, 4)),
    "union progress body level");
Equal(1, BinaryPrimitives.ReadInt32LittleEndian(unionBody.AsSpan(8, 4)),
    "union progress body marker");
Equal(200, BinaryPrimitives.ReadInt32LittleEndian(unionBody.AsSpan(12, 4)),
    "union progress body experience");
Equal(700, BinaryPrimitives.ReadInt32LittleEndian(unionBody.AsSpan(16, 4)),
    "union progress body required experience");
unionMagic.btLevel = 99;
unionMagic.nTranPoint = 17;
Assert(bridge.CallPlayerFunc("AddSkillExp", Values("union-skill", 1),
        out addSkillExpResult),
    "capped union AddSkillExp function was not dispatched");
Assert(!addSkillExpResult.AsBool(), "level-99 union AddSkillExp returned True");
Equal(99, unionMagic.btLevel, "level-99 union AddSkillExp changed level");
Equal(17, unionMagic.nTranPoint, "level-99 union AddSkillExp changed experience");

player.m_MagicArr[magicInfo.wMagicID] = playerMagic;
player.m_MsgList.Clear();
playerMagic.nTranPoint = 0;
Assert(bridge.CallPlayerFunc("ChgSkillLv", Values("hero-skill", 3, 77),
        out var skillResult),
    "ChgSkillLv function was not dispatched");
Assert(skillResult.AsBool(), "ChgSkillLv rejected an existing named skill");
Equal(3, playerMagic.btLevel, "ChgSkillLv level");
Equal(0, playerMagic.nTranPoint, "ChgSkillLv ignored the native actor-level gate");
var skillMessages = player.m_MsgList
    .Where(message => message.wIdent == Grobal2.RM_MAGIC_LVEXP).ToArray();
Equal(1, skillMessages.Length, "ChgSkillLv queued magic message count");
var skillMessage = skillMessages[0];
Equal(Grobal2.RM_MAGIC_LVEXP, skillMessage.wIdent, "ChgSkillLv message ident");
Equal(magicInfo.wMagicID, skillMessage.wParam, "ChgSkillLv message magic id");
Equal(3, skillMessage.nParam1, "ChgSkillLv message level");
Equal(0, skillMessage.nParam2, "ChgSkillLv message experience");
Equal(-1, skillMessage.nParam3, "ChgSkillLv message required experience");

playerMagic.btLevel = 0;
playerMagic.nTranPoint = 50;
player.m_MsgList.Clear();
Assert(bridge.CallPlayerFunc("ChgSkillLv", Values("hero-skill", 1, 250),
        out skillResult),
    "additive ChgSkillLv function was not dispatched");
Assert(skillResult.AsBool(), "additive ChgSkillLv returned False");
Equal(1, playerMagic.btLevel, "additive ChgSkillLv level");
Equal(300, playerMagic.nTranPoint, "ChgSkillLv replaced rather than added experience");
skillMessages = player.m_MsgList
    .Where(message => message.wIdent == Grobal2.RM_MAGIC_LVEXP).ToArray();
Equal(1, skillMessages.Length, "additive ChgSkillLv queued magic message count");
skillMessage = skillMessages[0];
Equal(magicInfo.wMagicID, skillMessage.wParam, "additive ChgSkillLv message magic id");
Equal(1, skillMessage.nParam1, "additive ChgSkillLv message level");
Equal(300, skillMessage.nParam2, "additive ChgSkillLv message experience");
Equal(2000, skillMessage.nParam3, "additive ChgSkillLv message required experience");
Assert(!bridge.CallPlayerMethod("ChgSkillLv", Values("hero-skill", 3, 77)),
    "ChgSkillLv procedure still shadows the native function");

Assert(bridge.CallPlayerFunc("DeleteSkill", Values("missing-skill"), out skillResult),
    "missing DeleteSkill function was not dispatched");
Assert(!skillResult.AsBool(), "missing DeleteSkill returned True");
Assert(player.m_MagicList.Contains(playerMagic),
    "missing DeleteSkill changed the skill list");
Assert(bridge.CallPlayerFunc("DeleteSkill", Values("hero-skill"), out skillResult),
    "DeleteSkill function was not dispatched");
Assert(skillResult.AsBool(), "DeleteSkill rejected an existing named skill");
Assert(!player.m_MagicList.Contains(playerMagic), "DeleteSkill retained the skill");
Assert(player.m_MagicArr[magicInfo.wMagicID] == null,
    "DeleteSkill retained the indexed skill reference");
Assert(!bridge.CallPlayerMethod("DeleteSkill", Values("hero-skill")),
    "DeleteSkill procedure still shadows the native function");

M2Share.LogStringList.Clear();
Assert(!bridge.CallPlayerFunc("AddLogRec", Values(9, "log-item", 811152, 7, "reason"),
        out var logResult),
    "AddLogRec was incorrectly exposed as a function");
AssertNil(logResult, "AddLogRec function");
Assert(bridge.CallPlayerMethod("AddLogRec",
        Values(9, "log-item", 811152, 7, "reason")),
    "AddLogRec procedure was not dispatched");
Equal(1, M2Share.LogStringList.Count, "AddLogRec entry count");
Assert((string)M2Share.LogStringList[0] ==
       "9\taudit-map\t12\t34\taudit-player\tlog-item\t811152\t7\treason",
    "AddLogRec did not preserve the native nine-column record");

var permanentAbilityBefore = (player.m_Abil.DC, player.m_Abil.MC, player.m_Abil.SC,
    player.m_Abil.AC, player.m_Abil.MAC, player.m_Abil.MaxHP,
    player.m_Abil.MaxMP, player.m_Abil.Exp);
var baseDcLow = HUtil32.LoWord(player.m_WAbil.DC);
var baseDcHigh = HUtil32.HiWord(player.m_WAbil.DC);
var timedAbilityTick = HUtil32.GetTickCount();
player.ProcessTimedAbilities(timedAbilityTick);
player.m_MsgList.Clear();
Assert(!bridge.CallPlayerFunc("AddPlayerAbil", Values(0, 5, 300),
        out var abilityResult),
    "AddPlayerAbil was incorrectly exposed as a function");
AssertNil(abilityResult, "AddPlayerAbil function");
Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(0, 5, 10)),
    "native AddPlayerAbil procedure was not dispatched");
player.ConsumePendingRecalc();
Equal(baseDcLow + 5, HUtil32.LoWord(player.m_WAbil.DC),
    "AddPlayerAbil DC lower bound");
Equal(baseDcHigh + 5, HUtil32.HiWord(player.m_WAbil.DC),
    "AddPlayerAbil DC upper bound");
Equal(1, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_ABILITY),
    "AddPlayerAbil deferred ability snapshot count");
Assert(!player.m_MsgList.Any(message =>
        message.wIdent == Grobal2.RM_SUBABILITY),
    "AddPlayerAbil queued a non-native subability refresh");

Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(43, 5, 10)),
    "native AddPlayerAbil type 43 was not dispatched");
Assert(player.HasTimedAbility(43),
    "native AddPlayerAbil type 43 did not create a timed state");
Assert(player.HasNativeActiveState(75),
    "native AddPlayerAbil type 43 did not map to internal state 75");
Equal(5, player.GetTimedAbilityValue(43), "native type 43 value");
Equal(10000, player.GetTimedAbilityRemainingMilliseconds(43),
    "native type 43 duration");
Assert(player.RemoveTimedAbility(43), "native type 43 state was not removable");
Assert(!player.HasNativeActiveState(75),
    "native type 43 removal retained internal state 75");

Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(0, 3, 20)),
    "lower-value AddPlayerAbil was not dispatched");
player.ConsumePendingRecalc();
Equal(5, player.GetTimedAbilityValue(0),
    "lower value replaced the active timed ability");
Equal(10000, player.GetTimedAbilityRemainingMilliseconds(0),
    "lower value replaced the active duration");

Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(0, 5, 20)),
    "equal-value AddPlayerAbil extension was not dispatched");
player.ConsumePendingRecalc();
Equal(20000, player.GetTimedAbilityRemainingMilliseconds(0),
    "equal value did not extend the active duration");
Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(0, 5, 5)),
    "shorter equal-value AddPlayerAbil was not dispatched");
player.ConsumePendingRecalc();
Equal(20000, player.GetTimedAbilityRemainingMilliseconds(0),
    "shorter equal value reduced the active duration");

Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(0, 6, 5)),
    "higher-value AddPlayerAbil replacement was not dispatched");
player.ConsumePendingRecalc();
Equal(6, player.GetTimedAbilityValue(0),
    "higher value did not replace the active value");
Equal(5000, player.GetTimedAbilityRemainingMilliseconds(0),
    "higher value did not replace the active duration");
timedAbilityTick += 6000;
player.ProcessTimedAbilities(timedAbilityTick);
player.ConsumePendingRecalc();
Assert(!player.HasTimedAbility(0), "timed ability did not expire");
Equal(baseDcLow, HUtil32.LoWord(player.m_WAbil.DC),
    "expired DC lower bound was not restored");
Equal(baseDcHigh, HUtil32.HiWord(player.m_WAbil.DC),
    "expired DC upper bound was not restored");

Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(299, 5, 300)),
    "low-byte native type 43 was not dispatched through script alias 299");
Assert(player.HasTimedAbility(43) && player.HasNativeActiveState(75),
    "299 script alias did not create the type 43/internal 75 state");
Equal(5, player.GetTimedAbilityValue(43), "299 script alias value");
Equal(300000, player.GetTimedAbilityRemainingMilliseconds(43),
    "299 script alias duration");
Assert(player.RemoveTimedAbility(43), "299 script alias state was not removable");
Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(0, 65535, 0)),
    "zero-duration native clear was not dispatched");
player.ConsumePendingRecalc();
Assert(player.HasTimedAbility(0), "native clear did not create the transient state");
player.ProcessTimedAbilities(timedAbilityTick + 499);
Assert(player.HasTimedAbility(0), "zero-duration native clear expired before 500ms scan");
player.ProcessTimedAbilities(timedAbilityTick + 500);
player.ConsumePendingRecalc();
Assert(!player.HasTimedAbility(0), "zero-duration native clear survived the 500ms scan");
Assert(!bridge.CallPlayerMethod("AddPlayerAbil", Values(1, 2, 3, 4, 5, 6, 7, 8)),
    "invented eight-argument AddPlayerAbil remains exposed");
Assert(permanentAbilityBefore == (player.m_Abil.DC, player.m_Abil.MC,
        player.m_Abil.SC, player.m_Abil.AC, player.m_Abil.MAC,
        player.m_Abil.MaxHP, player.m_Abil.MaxMP, player.m_Abil.Exp),
    "AddPlayerAbil changed permanent ability state");

player.m_boDeath = true;
player.m_WAbil.HP = 1;
var reviveBefore = (player.m_boDeath, player.m_WAbil.HP, player.m_sMapName,
    player.m_nCurrX, player.m_nCurrY);
Assert(!bridge.CallPlayerFunc("DoRelive", Values(5000, 100), out var reviveResult),
    "DoRelive was incorrectly exposed as a function");
AssertNil(reviveResult, "DoRelive function");
Assert(!bridge.CallPlayerMethod("DoRelive", Values(5000, 100)),
    "unimplemented native DoRelive did not fail closed");
Assert(reviveBefore == (player.m_boDeath, player.m_WAbil.HP, player.m_sMapName,
        player.m_nCurrX, player.m_nCurrY),
    "failed DoRelive changed death state or teleported the player");

RunGroupPropertyRegressions();
RunGroupFlyRegressions();
RunClearMonDispatchRegressions();
RunPlayDiceDispatchRegression();
RunRepairClickAbiRegression();
RunCastleClickAbiRegression();

var repositoryRoot = FindRepositoryRoot();
var bridgeSource = File.ReadAllText(Path.Combine(repositoryRoot,
    "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
var nativeGiveSource = File.ReadAllText(Path.Combine(repositoryRoot,
    "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.NativeGive.cs"));
var playerProperties = Slice(bridgeSource, "public bool GetPlayerProperty",
    "public bool SetPlayerProperty");
var playerMethods = Slice(bridgeSource, "public bool CallPlayerMethod", "public bool CallPlayerFunc");
var playerFunctions = Slice(bridgeSource, "public bool CallPlayerFunc", "public bool CallNpcMethod");
var npcMethods = Slice(bridgeSource, "public bool CallNpcMethod", "public bool CallNpcFunc");
var npcFunctions = Slice(bridgeSource, "public bool CallNpcFunc", "public bool CallStandaloneFunction");
Equal(1, Count(playerProperties, "case \"isteammember\":"),
    "IsTeamMember property dispatch count");
Equal(1, Count(playerProperties, "case \"isgroupowner\":"),
    "IsGroupOwner property dispatch count");
Equal(0, Count(playerProperties, "case \"istleammember\":"),
    "non-native IsTleamMember property alias count");
Equal(0, Count(playerFunctions, "case \"isteammember\":"),
    "IsTeamMember function shadow count");
Equal(0, Count(playerFunctions, "case \"isgroupowner\":"),
    "IsGroupOwner function shadow count");
Equal(1, Count(npcMethods, "case \"clearmon\":"),
    "ClearMon procedure dispatch count");
Equal(1, Count(npcMethods, "case \"clearmonex\":"),
    "ClearMonEx procedure dispatch count");
Equal(0, Count(npcFunctions, "case \"clearmon\":"),
    "ClearMon function shadow count");
Equal(1, Count(npcMethods, "case \"playdice\":"),
    "PlayDice procedure dispatch count");
Equal(0, Count(npcFunctions, "case \"playdice\":"),
    "PlayDice function shadow count");
foreach (var procedureName in new[]
         {
             "click_repair", "click_srepair", "click_repairex",
             "click_repair_ex"
         })
{
    Equal(1, Count(npcMethods, $"case \"{procedureName}\":"),
        procedureName + " procedure dispatch count");
    Equal(0, Count(npcFunctions, $"case \"{procedureName}\":"),
        procedureName + " function shadow count");
}
foreach (var procedureName in new[] { "click_repairwall", "click_hirearcher" })
{
    Equal(1, Count(npcMethods, $"case \"{procedureName}\":"),
        procedureName + " procedure dispatch count");
    Equal(0, Count(npcFunctions, $"case \"{procedureName}\":"),
        procedureName + " function shadow count");
}
var repairWallMethod = Slice(npcMethods, "case \"click_repairwall\":",
    "case \"click_hireguard\":");
Require(repairWallMethod,
    @"args\.Count\s*!=\s*2[\s\S]{0,160}?args\[0\]\.Type\s*!=\s*PasValueType\.Object" +
    @"[\s\S]{0,160}?args\[0\]\.ObjVal\s+is\s+not\s+TPlayObject\s+repairWallPlayer" +
    @"[\s\S]{0,160}?args\[1\]\.Type\s*!=\s*PasValueType\.Integer[\s\S]{0,80}?return false;",
    "Click_RepairWall exact Hum/nPos ABI fail-closed gate");
Require(repairWallMethod, @"var\s+wallIdx\s*=\s*args\[1\]\.AsInt\(\);",
    "Click_RepairWall nPos argument slot");
Assert(!repairWallMethod.Contains("CurrentPlayer", StringComparison.Ordinal),
    "Click_RepairWall used ambient player instead of explicit Hum");

var hireArcherMethod = Slice(npcMethods, "case \"click_hirearcher\":",
    "case \"reqcastlewar\":");
Require(hireArcherMethod,
    @"args\.Count\s*!=\s*2[\s\S]{0,160}?args\[0\]\.Type\s*!=\s*PasValueType\.Object" +
    @"[\s\S]{0,160}?args\[0\]\.ObjVal\s+is\s+not\s+TPlayObject\s+hireArcherPlayer" +
    @"[\s\S]{0,160}?args\[1\]\.Type\s*!=\s*PasValueType\.Integer[\s\S]{0,80}?return false;",
    "Click_HireArcher exact Hum/nPos ABI fail-closed gate");
Require(hireArcherMethod, @"var\s+idx\s*=\s*args\[1\]\.AsInt\(\)\s*-\s*1;",
    "Click_HireArcher nPos argument slot");
Assert(!hireArcherMethod.Contains("CurrentPlayer", StringComparison.Ordinal),
    "Click_HireArcher used ambient player instead of explicit Hum");

foreach (var procedureName in new[] { "click_takeoutgold", "click_savegold" })
{
    Equal(1, Count(npcMethods, $"case \"{procedureName}\":"),
        procedureName + " procedure dispatch count");
    Equal(0, Count(npcFunctions, $"case \"{procedureName}\":"),
        procedureName + " function shadow count");
}
var castleGoldWorker = Slice(bridgeSource,
    "private bool TryHandleNativeCastleGoldClick", "public bool SetNpcProperty");
Require(castleGoldWorker,
    @"args\.Count\s*!=\s*2[\s\S]{0,160}?args\[0\]\.Type\s*!=\s*PasValueType\.Object" +
    @"[\s\S]{0,160}?args\[0\]\.ObjVal\s+is\s+not\s+TPlayObject\s+player" +
    @"[\s\S]{0,160}?args\[1\]\.Type\s*!=\s*PasValueType\.String[\s\S]{0,80}?return false;",
    "castle gold exact Hum/GoldNumStr ABI fail-closed gate");
foreach (var required in new[]
         {
             "TryParseNativeDelphiInteger(args[1].AsString()",
             "parsedGold < 0 ? unchecked(-parsedGold) : parsedGold",
             "castle.m_MasterGuild != player.m_MyGuild",
             "player.m_nGuildRankNo != 1",
             "gold > castle.m_nTotalGold", "player.IncGold(gold)",
             "castle.m_nTotalGold -= gold", "player.DecGold(gold)",
             "const int nativeCastleGoldLimit = 100_000_000",
             "unchecked(castle.m_nTotalGold + gold)",
             "castle.m_nTotalGold = totalGold", "player.GoldChanged()",
             "Grobal2.RM_MENU_OK", "CurrentNpc.ObjectId"
         })
    Assert(castleGoldWorker.Contains(required, StringComparison.Ordinal),
        "castle gold worker missing native behavior: " + required);
foreach (var response in new[]
         {
             "只有后述行会掌门人才能使用 ", "城内没有这么多金币。",
             "您无法携带更多的金币了。", "你没有那么多金币。",
             "你已经到达在城内存放金币的限制了"
         })
    Assert(castleGoldWorker.Contains(response, StringComparison.Ordinal),
        "castle gold response changed: " + response);
Require(npcMethods,
    @"case\s+""click_takeoutgold""\s*:\s*return\s+TryHandleNativeCastleGoldClick\(args,\s*true\);",
    "Click_TakeOutGold native worker dispatch");
Require(npcMethods,
    @"case\s+""click_savegold""\s*:\s*return\s+TryHandleNativeCastleGoldClick\(args,\s*false\);",
    "Click_SaveGold native worker dispatch");
Require(npcMethods,
    @"case\s+""clearmon""\s*:[\s\S]{0,420}?ClearEnvironmentMonsters\(map,\s*false\)",
    "ClearMon native environment worker dispatch");
Require(npcMethods,
    @"case\s+""clearmonex""\s*:[\s\S]{0,460}?ClearEnvironmentMonsters\(map,\s*args\[1\]\.AsBool\(\)\)",
    "ClearMonEx native flag dispatch");
Equal(1, Count(playerMethods, "case \"randomflyto\":"),
    "RandomFlyTo procedure dispatch count");
Equal(0, Count(playerFunctions, "case \"randomflyto\":"),
    "RandomFlyTo function dispatch count");
Equal(1, Count(playerMethods, "case \"groupfly\":"),
    "GroupFly procedure dispatch count");
Equal(0, Count(playerFunctions, "case \"groupfly\":"),
    "GroupFly function dispatch count");
Equal(0, Count(playerMethods, "case \"groupflyex\":"),
    "GroupFlyEx procedure shadow count");
Equal(1, Count(playerFunctions, "case \"groupflyex\":"),
    "GroupFlyEx function dispatch count");
foreach (var functionName in new[] { "takeex", "addgold", "decgold", "upgradeheroskill" })
{
    Equal(0, Count(playerMethods, $"case \"{functionName}\":"),
        functionName + " procedure dispatch count");
    Equal(1, Count(playerFunctions, $"case \"{functionName}\":"),
        functionName + " function dispatch count");
}
foreach (var functionName in new[] { "deleteskill", "chgskillv", "chgskilllv" })
{
    Equal(0, Count(playerMethods, $"case \"{functionName}\":"),
        functionName + " procedure dispatch count");
    Equal(1, Count(playerFunctions, $"case \"{functionName}\":"),
        functionName + " function dispatch count");
}
Equal(1, Count(playerMethods, "case \"addlogrec\":"),
    "AddLogRec procedure dispatch count");
Equal(0, Count(playerFunctions, "case \"addlogrec\":"),
    "AddLogRec function dispatch count");
Equal(1, Count(playerMethods, "case \"addplayerabil\":"),
    "AddPlayerAbil procedure dispatch count");
Require(playerMethods,
    @"case\s+""addplayerabil""\s*:[\s\S]{0,700}?IsNativeTimedAbilityType\(" +
    @"[\s\S]{0,250}?IsSupportedTimedAbilityType\([\s\S]{0,250}?AddTimedAbility\(",
    "AddPlayerAbil validated timed-state dispatch");
Equal(0, Count(playerFunctions, "case \"addplayerabil\":"),
    "AddPlayerAbil function dispatch count");
RequireClosed(playerMethods, "dorelive", "DoRelive procedure");
Equal(0, Count(playerFunctions, "case \"dorelive\":"),
    "DoRelive function dispatch count");
Equal(1, Count(playerMethods, "case \"scriptrequestaddybnum\":"),
    "ScriptRequestAddYBNum procedure dispatch count");
Equal(1, Count(playerMethods, "case \"scriptrequestsubybnum\":"),
    "ScriptRequestSubYBNum procedure dispatch count");
Equal(0, Count(playerFunctions, "case \"scriptrequestaddybnum\":"),
    "ScriptRequestAddYBNum function dispatch count");
Equal(0, Count(playerFunctions, "case \"scriptrequestsubybnum\":"),
    "ScriptRequestSubYBNum function dispatch count");
foreach (var functionName in new[] { "psybconsum", "psybconsumex" })
{
    Equal(0, Count(playerMethods, $"case \"{functionName}\":"),
        functionName + " procedure shadow count");
    Equal(1, Count(playerFunctions, $"case \"{functionName}\":"),
        functionName + " asynchronous function count");
}
Require(playerFunctions,
    @"case\s+""psybconsum""\s*:[\s\S]{0,420}?TrySubmitNormal\(\s*" +
    @"CurrentPlayer,\s*purchaseNpc,\s*args\[1\]\.AsString\(\),\s*" +
    @"args\[2\]\.AsInt\(\),\s*args\[3\]\.AsInt\(\),\s*" +
    @"args\[4\]\.AsInt\(\)\)",
    "PsYBConsum native asynchronous dispatch and argument order");
Require(playerFunctions,
    @"case\s+""psybconsumex""\s*:[\s\S]{0,520}?TrySubmitYbShop\(\s*" +
    @"CurrentPlayer,\s*\(byte\)executionTag,\s*args\[1\]\.AsString\(\),\s*" +
    @"args\[2\]\.AsString\(\),\s*args\[3\]\.AsInt\(\),\s*" +
    @"args\[4\]\.AsInt\(\),\s*args\[5\]\.AsInt\(\)\)",
    "PsYBConsumEx native asynchronous dispatch and argument order");
Require(playerMethods,
    @"case\s+""playerdialog""\s*:[\s\S]{0,320}?SM_MERCHANTSAY[\s\S]{0,180}?" +
    @"""NPC/""\s*\+\s*args\[0\]\.AsString\(\)",
    "PlayerDialog native packet body");
Require(playerMethods,
    @"case\s+""scriptrequestaddybnum""\s*:[\s\S]{0,240}?" +
    @"ScriptRequestNativeYuanbao\(args\[0\]\.AsInt\(\),\s*" +
    @"NativeYuanbaoManager\.AddOperation\)",
    "ScriptRequestAddYBNum native async dispatch");
Require(playerMethods,
    @"case\s+""scriptrequestsubybnum""\s*:[\s\S]{0,240}?" +
    @"ScriptRequestNativeYuanbao\(args\[0\]\.AsInt\(\),\s*" +
    @"NativeYuanbaoManager\.SubtractOperation\)",
    "ScriptRequestSubYBNum native async dispatch");
Require(playerMethods,
    @"case\s+""sysgivegift""\s*:[\s\S]{0,100}?TrySysGiveGift\(args\)",
    "SysGiveGift procedure helper dispatch");
Require(playerFunctions,
    @"case\s+""sysgivegift""\s*:[\s\S]{0,120}?TrySysGiveGift\(args\)",
    "SysGiveGift function helper dispatch");
foreach (var procedureName in new[] { "give", "bindgive", "loopgive", "givebinditem" })
    Equal(1, Count(playerMethods, $"case \"{procedureName}\":"),
        procedureName + " procedure dispatch count");
foreach (var functionName in new[] { "give", "bindgive", "loopgive" })
    Equal(1, Count(playerFunctions, $"case \"{functionName}\":"),
        functionName + " function dispatch count");
Equal(0, Count(playerFunctions, "case \"givebinditem\":"),
    "GiveBindItem function dispatch count");
Require(playerMethods,
    @"case\s+""givebinditem""\s*:[\s\S]{0,180}?TryNativeGive\([^;]+true,\s*false\)",
    "GiveBindItem bound native worker dispatch");
Require(playerFunctions,
    @"case\s+""give""\s*:[\s\S]{0,180}?result\s*=\s*PasValue\.FromBool\([\s\S]*?TryNativeGive\(",
    "Give function boolean result dispatch");
Require(nativeGiveSource, @"if\s*\(bind\)\s*item\.Bind\s*=\s*1\s*;",
    "native Give binding write");
Require(nativeGiveSource, @"return\s+gaveAny\s*;",
    "native Give partial-success result");
Require(nativeGiveSource, @"IndexOf\('\:'\)",
    "native Give colon count parser");
foreach (var accountName in new[] { "灵符", "限时灵符", "牛气值", "荣耀点" })
    Assert(nativeGiveSource.Contains(accountName, StringComparison.Ordinal),
        accountName + " native Give account marker is missing");
Assert(!nativeGiveSource.Contains("荣誉点", StringComparison.Ordinal),
    "native Give uses the incorrect 荣誉点 spelling");
Assert(nativeGiveSource.Contains("请先将您的英雄召唤出来！", StringComparison.Ordinal),
    "native Give missing-hero message differs from Delphi");
Assert(nativeGiveSource.Contains("你的英雄级数已满", StringComparison.Ordinal),
    "native Give full-level hero message differs from Delphi");
Require(nativeGiveSource, @"TryParseNativeDelphiInteger\(",
    "native Give Delphi integer parser");

Console.WriteLine(
    "PASS player-dispatch Group=published-property ClearMon=environment-object-lifecycle " +
    "RandomFlyTo=procedure-fallback YB=async-transaction " +
    "SysGiveGift=no-npc+bind+pile+bag-failure Give=native-worker+bind+pile+partial+accounts " +
    "NativeExp=uint-wrap+continuation+hero-types+level-caps " +
    "NativeForce=x87-table+uint-wrap+level-cap+glory-fealty " +
    "GiveLog=canonical-item-name " +
    "TakeEx=atomic-fail-closed " +
    "Gold=boolean HeroSkill=hero-only Skill=name+level+exp+delete Log=nine-column " +
    "PlayerAbil=timed-state+native-precedence DoRelive=scheduled-fail-closed " +
    "PlayDice=explicit-player+packed-v1-v10 " +
    "RepairClick=explicit-player+mode-byte+shared-rm+ghost-order");
return;

static void RunPlayDiceDispatchRegression()
{
    var npc = new NormNpc();
    var explicitPlayer = new TPlayObject { m_sCharName = "dice-player" };
    var ambientPlayer = new TPlayObject { m_sCharName = "ambient-player" };
    // 原来这里种的是 keyed m_ScriptVVars[1..10]，那块原生根本读不到。
    // sub_645200 的十次取值走的是 GROUP-0 GetV：
    //   0x0064522F  33 f6           xor esi,esi        ; i = 0
    //   0x00645234  8d 4e 01        lea ecx,[esi+1]    ; index = i+1
    //   0x00645237  33 d2           xor edx,edx        ; group = 0
    //   0x0064523B  e8 a4 9f 09 00  call 0x6DF1E4      ; GetV(0, i+1)
    //   0x00645246  83 fe 0a        cmp esi,0x0A       ; 十格
    // 而 GetV 在 0x006DF203 `85 f6` test esi,esi / `75 14` jne 把 group 0 分到
    // 内联区 0x006DF20F `mov eax,[ebx+eax*4+0x808]`，keyed 字典里的
    // group*1000+index（sub_6E42CC `imul eax,edx,0x3E8`）永远命不中 <1000 的键。
    for (var index = 1; index <= 10; index++)
        explicitPlayer.m_ScriptVGroup0[index] = 0x100 + index;

    var bridge = new PasApiBridge { CurrentNpc = npc, CurrentPlayer = ambientPlayer };
    var args = Values(explicitPlayer, 6, "dice-result");

    Assert(!bridge.CallNpcFunc("PlayDice", args, out var functionResult),
        "PlayDice function still shadows the native procedure");
    AssertNil(functionResult, "PlayDice function");
    Equal(0, explicitPlayer.m_MsgList.Count, "PlayDice function queued a message");

    Assert(bridge.CallNpcMethod("PlayDice", args, out var methodResult),
        "PlayDice procedure was not dispatched");
    AssertNil(methodResult, "PlayDice procedure");
    Equal(1, explicitPlayer.m_MsgList.Count, "PlayDice queued message count");
    Equal(0, ambientPlayer.m_MsgList.Count, "PlayDice used the ambient player");
    Assert(explicitPlayer.m_sPlayDiceLabel == "dice-result",
        "PlayDice did not store the callback label");

    var message = explicitPlayer.m_MsgList[0];
    Equal(Grobal2.RM_PLAYDICE, message.wIdent, "PlayDice message ident");
    Equal(6, message.wParam, "PlayDice dice count");
    EqualBits(0x04030201, message.nParam1, "PlayDice V1..V4 packing");
    EqualBits(0x08070605, message.nParam2, "PlayDice V5..V8 packing");
    EqualBits(0x00000A09, message.nParam3, "PlayDice V9..V10 packing");
    Assert(message.Buff == "dice-result", "PlayDice message label");
    Assert(ReferenceEquals(npc, message.BaseObject), "PlayDice message NPC");
}

static void RunRepairClickAbiRegression()
{
    var npc = new NormNpc();
    var explicitPlayer = new TPlayObject { m_sCharName = "repair-clicker" };
    var ambientPlayer = new TPlayObject { m_sCharName = "repair-ambient" };
    var bridge = new PasApiBridge
    {
        CurrentNpc = npc,
        CurrentPlayer = ambientPlayer
    };

    foreach (var call in new[]
             {
                 (Name: "Click_Repair", Args: Values(explicitPlayer)),
                 (Name: "Click_SRepair", Args: Values(explicitPlayer)),
                 (Name: "Click_RepairEx", Args: Values(explicitPlayer, 3)),
                 (Name: "Click_Repair_Ex", Args: Values(explicitPlayer, 3))
             })
    {
        Assert(!bridge.CallNpcFunc(call.Name, call.Args, out var functionResult),
            call.Name + " function still shadows the native procedure");
        AssertNil(functionResult, call.Name + " function");
    }

    foreach (var method in new[] { "Click_Repair", "Click_SRepair" })
    {
        foreach (var invalidArgs in new[]
                 {
                     Values(), Values(1), Values(npc),
                     Values(explicitPlayer, 1)
                 })
        {
            Assert(!bridge.CallNpcMethod(method, invalidArgs, out var methodResult),
                method + " accepted an invalid explicit-Clicker ABI");
            AssertNil(methodResult, method + " invalid ABI");
        }
    }

    foreach (var method in new[] { "Click_RepairEx", "Click_Repair_Ex" })
    {
        foreach (var invalidArgs in new[]
                 {
                     Values(), Values(explicitPlayer), Values(1, 3),
                     Values(npc, 3), Values(explicitPlayer, "3"),
                     Values(explicitPlayer, 3, 4)
                 })
        {
            Assert(!bridge.CallNpcMethod(method, invalidArgs, out var methodResult),
                method + " accepted an invalid (Clicker, RepairMode:Word) ABI");
            AssertNil(methodResult, method + " invalid ABI");
        }
    }
    Equal(0, explicitPlayer.m_btNativeRepairMode,
        "invalid repair click changed mode");
    Equal(0, explicitPlayer.m_MsgList.Count,
        "invalid repair click queued a message");

    Verify("Click_Repair", Values(explicitPlayer), 1);
    Verify("Click_SRepair", Values(explicitPlayer), 2);
    Verify("Click_RepairEx", Values(explicitPlayer, 0x0103), 3);
    Verify("Click_Repair_Ex", Values(explicitPlayer, 0x12345), 0x45);
    Equal(0, ambientPlayer.m_btNativeRepairMode,
        "repair click changed the ambient player's mode");
    Equal(0, ambientPlayer.m_MsgList.Count,
        "repair click queued a message on the ambient player");

    explicitPlayer.m_MsgList.Clear();
    explicitPlayer.m_boGhost = true;
    Assert(bridge.CallNpcMethod("Click_RepairEx",
            Values(explicitPlayer, 0x0103), out var ghostResult),
        "ghost Click_RepairEx was not dispatched");
    AssertNil(ghostResult, "ghost Click_RepairEx");
    Equal(3, explicitPlayer.m_btNativeRepairMode,
        "ghost Click_RepairEx did not write the mode before SendMsg");
    Equal(0, explicitPlayer.m_MsgList.Count,
        "ghost Click_RepairEx bypassed the shared SendMsg ghost gate");

    explicitPlayer.m_boGhost = false;
    explicitPlayer.m_boDeath = true;
    Assert(bridge.CallNpcMethod("Click_Repair", Values(explicitPlayer),
            out var deathResult),
        "dead Click_Repair was not dispatched");
    AssertNil(deathResult, "dead Click_Repair");
    Equal(1, explicitPlayer.m_btNativeRepairMode,
        "dead Click_Repair did not persist mode 1");
    Equal(1, explicitPlayer.m_MsgList.Count,
        "dead Click_Repair incorrectly applied a death pre-gate");

    void Verify(string method, List<PasValue> args, int expectedMode)
    {
        explicitPlayer.m_boDeath = false;
        explicitPlayer.m_boGhost = false;
        explicitPlayer.m_MsgList.Clear();
        Assert(bridge.CallNpcMethod(method, args, out var methodResult),
            method + " procedure was not dispatched");
        AssertNil(methodResult, method + " procedure");
        Equal(expectedMode, explicitPlayer.m_btNativeRepairMode,
            method + " mode byte");
        Equal(1, explicitPlayer.m_MsgList.Count,
            method + " queued message count");
        var message = explicitPlayer.m_MsgList[0];
        Equal(Grobal2.RM_SENDUSERREPAIR, message.wIdent,
            method + " message ident");
        Equal(0, message.wParam, method + " message wParam");
        Equal(npc.ObjectId, message.nParam1,
            method + " message NPC ObjectId");
        Equal(0, message.nParam2,
            method + " must not put repair mode in nParam2");
        Equal(0, message.nParam3, method + " message nParam3");
        Assert(string.IsNullOrEmpty(message.Buff), method + " message payload");
        Assert(ReferenceEquals(npc, message.BaseObject),
            method + " message NPC");
    }
}

static void RunCastleClickAbiRegression()
{
    var bridge = new PasApiBridge
    {
        CurrentNpc = new NormNpc(),
        CurrentPlayer = new TPlayObject { m_nGold = 12345 }
    };
    var explicitPlayer = new TPlayObject { m_nGold = 54321 };

    foreach (var method in new[] { "Click_RepairWall", "Click_HireArcher" })
    {
        Assert(!bridge.CallNpcFunc(method, Values(explicitPlayer, 1),
                out var functionResult),
            method + " function still shadows the native procedure");
        AssertNil(functionResult, method + " function");

        foreach (var invalidArgs in new[]
                 {
                     Values(), Values(1, 1), Values(new NormNpc(), 1),
                     Values(explicitPlayer), Values(explicitPlayer, "1"),
                     Values(explicitPlayer, 1, 2)
                 })
        {
            Assert(!bridge.CallNpcMethod(method, invalidArgs, out var methodResult),
                method + " accepted an invalid Hum/nPos ABI");
            AssertNil(methodResult, method + " invalid ABI");
        }
    }

    foreach (var method in new[] { "Click_TakeOutGold", "Click_SaveGold" })
    {
        Assert(!bridge.CallNpcFunc(method, Values(explicitPlayer, "1"),
                out var functionResult),
            method + " function still shadows the native procedure");
        AssertNil(functionResult, method + " function");

        foreach (var invalidArgs in new[]
                 {
                     Values(), Values(1, "1"), Values(new NormNpc(), "1"),
                     Values(explicitPlayer), Values(explicitPlayer, 1),
                     Values(explicitPlayer, "1", "extra")
                 })
        {
            Assert(!bridge.CallNpcMethod(method, invalidArgs, out var methodResult),
                method + " accepted an invalid Hum/GoldNumStr ABI");
            AssertNil(methodResult, method + " invalid ABI");
        }
    }
}

static void RunGroupPropertyRegressions()
{
    M2Share.ObjectManager = new ObjectManager();
    var owner = new TPlayObject { m_sCharName = "group-owner" };
    var member = new TPlayObject { m_sCharName = "group-member" };
    var stale = new TPlayObject { m_sCharName = "group-stale" };
    var solo = new TPlayObject { m_sCharName = "group-solo" };

    owner.m_GroupOwner = owner;
    owner.m_GroupMembers.Clear();
    owner.m_GroupMembers.Add(owner);
    owner.m_GroupMembers.Add(member);
    member.m_GroupOwner = owner;
    stale.m_GroupOwner = owner;

    var bridge = new PasApiBridge();
    const string source = """
        program GroupPropertyProbe;
        function TeamProbe: Boolean;
        begin
          Result := This_Player.IsTeamMember;
        end;
        function OwnerProbe: Boolean;
        begin
          Result := This_Player.IsGroupOwner;
        end;
        begin
        end.
        """;
    var program = new PasParser(new PasLexer(source), FindRepositoryRoot()).Parse();
    var interpreter = new PasInterpreter(program, bridge);

    Verify(solo, false, false, "solo");
    Verify(owner, true, true, "owner");
    Verify(member, true, false, "member");
    Verify(stale, false, false, "stale owner pointer");

    bridge.CurrentPlayer = owner;
    Assert(!bridge.CallPlayerFunc("IsTeamMember", new List<PasValue>(), out var teamFunc),
        "IsTeamMember function still shadows the published property");
    AssertNil(teamFunc, "IsTeamMember function");
    Assert(!bridge.CallPlayerFunc("IsGroupOwner", new List<PasValue>(), out var ownerFunc),
        "IsGroupOwner function still shadows the published property");
    AssertNil(ownerFunc, "IsGroupOwner function");
    Assert(!bridge.GetPlayerProperty("IsTleamMember", out var typoProperty),
        "non-native IsTleamMember property alias remains exposed");
    AssertNil(typoProperty, "IsTleamMember property");

    void Verify(TPlayObject subject, bool expectedTeam, bool expectedOwner, string scenario)
    {
        bridge.CurrentPlayer = subject;
        Assert(bridge.GetPlayerProperty("IsTeamMember", out var team),
            scenario + " IsTeamMember property was not dispatched");
        Assert(bridge.GetPlayerProperty("IsGroupOwner", out var groupOwner),
            scenario + " IsGroupOwner property was not dispatched");
        Equal(expectedTeam ? 1 : 0, team.AsBool() ? 1 : 0,
            scenario + " IsTeamMember direct property");
        Equal(expectedOwner ? 1 : 0, groupOwner.AsBool() ? 1 : 0,
            scenario + " IsGroupOwner direct property");
        Equal(expectedTeam ? 1 : 0,
            interpreter.ExecuteProcedure("TeamProbe").AsBool() ? 1 : 0,
            scenario + " IsTeamMember PAS property");
        Equal(expectedOwner ? 1 : 0,
            interpreter.ExecuteProcedure("OwnerProbe").AsBool() ? 1 : 0,
            scenario + " IsGroupOwner PAS property");
    }
}

static void RunGroupFlyRegressions()
{
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.RandomNumber ??= RandomNumber.GetInstance();

    var source = NewEnvironment("group-fly-source");
    var otherSource = NewEnvironment("group-fly-other-source");
    var target = NewEnvironment("group-fly-target", 64, 48);
    var secondTarget = NewEnvironment("group-fly-second-target");
    Register(source);
    Register(otherSource);
    Register(target);
    Register(secondTarget);

    var owner = NewPlayer(source, "group-fly-owner", 4, 4);
    var follower = NewPlayer(source, "group-fly-follower", 6, 4);
    var dead = NewPlayer(source, "group-fly-dead", 8, 4);
    dead.m_boDeath = true;
    var ghost = NewPlayer(source, "group-fly-ghost", 10, 4);
    ghost.m_boGhost = true;
    var wrongRace = NewPlayer(source, "group-fly-wrong-race", 12, 4);
    wrongRace.m_btRaceServer = Grobal2.RC_ANIMAL;
    var otherMap = NewPlayer(otherSource, "group-fly-other-map", 4, 4);
    var dead2 = NewPlayer(source, "group-fly-dead-2", 14, 4);
    dead2.m_boDeath = true;
    var ghost2 = NewPlayer(source, "group-fly-ghost-2", 16, 4);
    ghost2.m_boGhost = true;
    var wrongRace2 = NewPlayer(source, "group-fly-wrong-race-2", 18, 4);
    wrongRace2.m_btRaceServer = Grobal2.RC_ANIMAL;
    var otherMap2 = NewPlayer(otherSource, "group-fly-other-map-2", 6, 4);
    var dead3 = NewPlayer(source, "group-fly-dead-3", 20, 4);
    dead3.m_boDeath = true;
    var overflow = NewPlayer(source, "group-fly-overflow", 22, 4);

    owner.m_GroupOwner = owner;
    owner.m_GroupMembers.Clear();
    foreach (var member in new[]
             {
                 owner, follower, dead, ghost, wrongRace, otherMap,
                 dead2, ghost2, wrongRace2, otherMap2, dead3, overflow
             })
    {
        member.m_GroupOwner = owner;
        owner.m_GroupMembers.Add(member);
    }

    var seed = 1;
    int expectedOwnerX;
    int expectedOwnerY;
    int expectedFollowerX;
    int expectedFollowerY;
    while (true)
    {
        var prediction = new Random(seed);
        expectedOwnerY = prediction.Next(target.wHeight);
        expectedOwnerX = prediction.Next(target.wWidth);
        var expectedRandomY = prediction.Next(9);
        var expectedRandomX = prediction.Next(9);
        expectedFollowerX = expectedOwnerX + 4 - expectedRandomX;
        expectedFollowerY = expectedOwnerY + 4 - expectedRandomY;
        if (expectedOwnerX > 0 && expectedOwnerX < target.wWidth - 1
            && expectedOwnerY > 0 && expectedOwnerY < target.wHeight - 1
            && (expectedOwnerX < 20 || expectedOwnerY < 20)
            && expectedOwnerX != expectedOwnerY
            && expectedFollowerX > 0 && expectedFollowerX < target.wWidth - 1
            && expectedFollowerY > 0 && expectedFollowerY < target.wHeight - 1
            && (expectedFollowerX != expectedOwnerX
                || expectedFollowerY != expectedOwnerY))
            break;
        seed++;
    }
    // The searched sequence is installed on M2Share.RandomNumber, the field the
    // server assigns at startup. It used to be installed by reflecting
    // RandomNumber's private `random` field, which POIS-26 removed when the
    // facade moved onto the Delphi LCG sub_403B4C; the `!` then dereferenced
    // null and this threw before the group-fly assertions ran. The seed search
    // above is unchanged - the wrapper replays the very same System.Random.
    M2Share.RandomNumber = new SeededProbeRandom(seed);

    const string sourceText = """
        program GroupFlyProbe;
        procedure Fly;
        begin
          This_Player.GroupFly('group-fly-target');
        end;
        procedure FlySecond;
        begin
          This_Player.GroupFly('group-fly-second-target');
        end;
        procedure FlyMissing;
        begin
          This_Player.GroupFly('group-fly-missing-target');
        end;
        function FlyEx: Integer;
        begin
          Result := This_Player.GroupFlyEx('group-fly-second-target');
        end;
        function FlyExWrongCase: Integer;
        begin
          Result := This_Player.GroupFlyEx('GROUP-FLY-SECOND-TARGET');
        end;
        begin
        end.
        """;
    var bridge = new PasApiBridge { CurrentPlayer = owner };
    var program = new PasParser(new PasLexer(sourceText), FindRepositoryRoot()).Parse();
    var interpreter = new PasInterpreter(program, bridge);

    interpreter.ExecuteProcedure("Fly");
    Assert(ReferenceEquals(target, owner.m_PEnvir),
        "GroupFly did not random-move the group owner");
    Equal(expectedOwnerX, owner.m_nCurrX, "GroupFly owner X");
    Equal(expectedOwnerY, owner.m_nCurrY, "GroupFly owner Y");
    Assert(ReferenceEquals(target, follower.m_PEnvir),
        "GroupFly did not move an eligible member");
    Equal(expectedFollowerX, follower.m_nCurrX, "GroupFly member X");
    Equal(expectedFollowerY, follower.m_nCurrY, "GroupFly member Y");

    foreach (var excluded in new[]
             {
                 dead, ghost, wrongRace, dead2, ghost2, wrongRace2, dead3,
                 overflow
             })
    {
        Assert(ReferenceEquals(source, excluded.m_PEnvir),
            $"GroupFly moved excluded member {excluded.m_sCharName}");
    }
    Assert(ReferenceEquals(otherSource, otherMap.m_PEnvir)
           && ReferenceEquals(otherSource, otherMap2.m_PEnvir),
        "GroupFly moved a member outside the owner's old environment");
    Assert(ReferenceEquals(source, overflow.m_PEnvir),
        "GroupFly moved the member beyond the native 11-slot boundary");

    bridge.CurrentPlayer = follower;
    var ownerBeforeNonLeader = (owner.m_PEnvir, owner.m_nCurrX, owner.m_nCurrY);
    var followerBeforeNonLeader = (follower.m_PEnvir, follower.m_nCurrX,
        follower.m_nCurrY);
    interpreter.ExecuteProcedure("FlySecond");
    Assert(ownerBeforeNonLeader == (owner.m_PEnvir, owner.m_nCurrX,
            owner.m_nCurrY)
           && followerBeforeNonLeader == (follower.m_PEnvir,
               follower.m_nCurrX, follower.m_nCurrY),
        "GroupFly allowed a non-owner to teleport the group");
    Equal(0, interpreter.ExecuteProcedure("FlyEx").AsInt(),
        "GroupFlyEx non-owner result");
    Assert(ownerBeforeNonLeader == (owner.m_PEnvir, owner.m_nCurrX,
            owner.m_nCurrY)
           && followerBeforeNonLeader == (follower.m_PEnvir,
               follower.m_nCurrX, follower.m_nCurrY),
        "GroupFlyEx allowed a non-owner to teleport the group");

    wrongRace.SpaceMove(secondTarget.sMapName, 30, 30, 0);
    overflow.SpaceMove(secondTarget.sMapName, 32, 30, 0);
    bridge.CurrentPlayer = owner;
    Equal(3, interpreter.ExecuteProcedure("FlyEx").AsInt(),
        "GroupFlyEx exact-map live member count");
    Assert(ReferenceEquals(secondTarget, owner.m_PEnvir)
           && ReferenceEquals(secondTarget, follower.m_PEnvir),
        "GroupFlyEx did not perform the GroupFly movement first");
    Equal(0, interpreter.ExecuteProcedure("FlyExWrongCase").AsInt(),
        "GroupFlyEx map-name comparison was not case-sensitive");

    bridge.CurrentPlayer = owner;
    var ownerBeforeMissingMap = (owner.m_PEnvir, owner.m_nCurrX, owner.m_nCurrY);
    interpreter.ExecuteProcedure("FlyMissing");
    Assert(ownerBeforeMissingMap == (owner.m_PEnvir, owner.m_nCurrX,
            owner.m_nCurrY),
        "GroupFly changed the owner for a missing target map");

    var solo = NewPlayer(source, "group-fly-solo", 24, 4);
    bridge.CurrentPlayer = solo;
    interpreter.ExecuteProcedure("FlySecond");
    Assert(ReferenceEquals(source, solo.m_PEnvir)
           && solo.m_nCurrX == 24 && solo.m_nCurrY == 4,
        "GroupFly moved a player without a group");

    var missingEnvironmentOwner = new TPlayObject
    {
        m_sCharName = "group-fly-no-environment"
    };
    missingEnvironmentOwner.m_GroupOwner = missingEnvironmentOwner;
    missingEnvironmentOwner.m_GroupMembers.Add(missingEnvironmentOwner);
    bridge.CurrentPlayer = missingEnvironmentOwner;
    interpreter.ExecuteProcedure("FlySecond");
    Assert(missingEnvironmentOwner.m_PEnvir == null,
        "GroupFly changed a group owner without an old environment");

    static Envirnoment NewEnvironment(string name, short width = 60,
        short height = 60)
    {
        var environment = new Envirnoment
        {
            sMapName = name,
            nServerIndex = M2Share.nServerIndex
        };
        typeof(Envirnoment).GetMethod("Initialize",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .Invoke(environment, new object[] { width, height });
        return environment;
    }

    static void Register(Envirnoment environment)
    {
        var maps = (Dictionary<string, Envirnoment>)typeof(MapManager)
            .GetField("m_MapList", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .GetValue(M2Share.MapManager)!;
        maps.Add(environment.sMapName, environment);
    }

    static TPlayObject NewPlayer(Envirnoment environment, string name, short x,
        short y)
    {
        var player = new TPlayObject
        {
            m_sCharName = name,
            m_sMapName = environment.sMapName,
            m_PEnvir = environment,
            m_btRaceServer = Grobal2.RC_PLAYOBJECT,
            m_nCurrX = x,
            m_nCurrY = y
        };
        player.m_WAbil.HP = 100;
        player.m_WAbil.MaxHP = 100;
        Assert(ReferenceEquals(environment.AddToMap(x, y,
                CellType.OS_MOVINGOBJECT, player), player),
            $"GroupFly test player {name} was not placed on the map");
        return player;
    }
}

static void RunClearMonDispatchRegressions()
{
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    var environment = NewEnvironment("clear-audit-map");
    var npc = new NormNpc { m_PEnvir = environment, m_sMapName = environment.sMapName };
    var attacker = new TPlayObject { m_sCharName = "clear-attacker" };
    var bridge = new PasApiBridge { CurrentNpc = npc, CurrentPlayer = attacker };

    var alive = NewActor(environment, Grobal2.RC_ANIMAL, 2, 2, 101);
    alive.m_LastHiter = attacker;
    alive.m_ExpHitter = attacker;
    alive.m_TargetCret = attacker;
    var pet = NewActor(environment, Grobal2.RC_ANIMAL, 5, 5, 102);
    pet.m_Master = attacker;
    var corpse = NewActor(environment, Grobal2.RC_ANIMAL, 8, 8, 0);
    corpse.m_boDeath = true;
    var race158 = NewActor(environment, 158, 11, 11, 103);
    var belowAnimal = NewActor(environment, (byte)(Grobal2.RC_ANIMAL - 1), 14, 14, 104);
    var facing = NewActor(environment, Grobal2.RC_ANIMAL, 17, 17, 105);
    facing.m_btDirection = Grobal2.DR_RIGHT;
    var frontObject = NewActor(environment, Grobal2.RC_PLAYOBJECT, 18, 17, 106);
    var unplaced = NewUnplacedActor(environment, Grobal2.RC_MONSTER, 20, 2, 107);
    var ghost = NewUnplacedActor(environment, Grobal2.RC_MONSTER, 20, 5, 108);
    ghost.m_boGhost = true;
    var hero = new HeroObject
    {
        m_sCharName = "clear-hero",
        m_PEnvir = environment,
        m_sMapName = environment.sMapName,
        m_Master = attacker,
        m_nCurrX = 20,
        m_nCurrY = 8
    };
    hero.m_WAbil.HP = 109;
    hero.m_WAbil.MaxHP = 109;
    var clone = NewUnplacedActor(environment, Grobal2.RC_PLAYCLONE, 20, 11, 110);
    clone.m_Master = attacker;
    var facingDead = NewActor(environment, Grobal2.RC_MONSTER, 22, 22, 111);
    facingDead.m_btDirection = Grobal2.DR_RIGHT;
    var deadFrontObject = NewActor(environment, Grobal2.RC_PLAYOBJECT, 23, 22, 112);
    deadFrontObject.m_boDeath = true;
    var reAliveCorpse = NewUnplacedActor(environment, Grobal2.RC_MONSTER, 25, 2, 0);
    reAliveCorpse.m_boDeath = true;
    reAliveCorpse.m_boCanReAlive = true;
    reAliveCorpse.m_boInvisible = true;
    var otherEnvironment = NewEnvironment("clear-other-map");
    var otherMapActor = NewUnplacedActor(otherEnvironment, Grobal2.RC_MONSTER,
        2, 2, 113);

    Assert(!bridge.CallNpcFunc("ClearMon", Values(""), out var functionResult),
        "ClearMon function still shadows the native procedure");
    AssertNil(functionResult, "ClearMon function");
    Equal(101, alive.m_WAbil.HP, "ClearMon function changed an actor");

    Assert(bridge.CallNpcMethod("ClearMon", Values(""), out var methodResult),
        "ClearMon procedure was not dispatched");
    AssertNil(methodResult, "ClearMon procedure");
    Equal(0, alive.m_WAbil.HP, "ClearMon alive HP");
    Assert(alive.m_boNoItem && alive.m_LastHiter == null && alive.m_TargetCret == null,
        "ClearMon alive no-drop/hitter/target state");
    Assert(ReferenceEquals(attacker, alive.m_ExpHitter),
        "ClearMon incorrectly cleared the experience hitter");
    Assert(!alive.m_boDeath && !alive.m_boGhost,
        "ClearMon directly killed or deleted an alive monster");
    Equal(0, pet.m_WAbil.HP, "ClearMon incorrectly excluded a mastered monster");
    Assert(corpse.m_boGhost, "ClearMon did not immediately delete a corpse");
    Equal(103, race158.m_WAbil.HP, "ClearMon did not preserve race 158");
    Equal(104, belowAnimal.m_WAbil.HP, "ClearMon did not preserve race below 50");
    Equal(105, facing.m_WAbil.HP, "ClearMon ignored GetPoseCreate exclusion");
    Equal(106, frontObject.m_WAbil.HP, "ClearMon changed a player");
    Equal(0, unplaced.m_WAbil.HP,
        "ClearMon missed an environment object outside the map cells");
    Equal(108, ghost.m_WAbil.HP, "ClearMon changed an existing ghost");
    Equal(0, hero.m_WAbil.HP, "ClearMon incorrectly excluded a hero");
    Equal(0, clone.m_WAbil.HP, "ClearMon incorrectly excluded a clone");
    Equal(0, facingDead.m_WAbil.HP,
        "ClearMon treated a dead front object as a live pose target");
    Equal(112, deadFrontObject.m_WAbil.HP, "ClearMon changed a player corpse");
    Assert(reAliveCorpse.m_dwGhostTick != 0,
        "ClearMon missed an unplaced re-alive corpse in the environment index");
    Equal(113, otherMapActor.m_WAbil.HP,
        "ClearMon crossed the environment boundary");

    var untouched = NewActor(environment, Grobal2.RC_ANIMAL, 27, 27, 114);
    Assert(bridge.CallNpcMethod("ClearMon", Values("missing-map"), out _),
        "ClearMon missing-map call was not accepted as a procedure");
    Equal(114, untouched.m_WAbil.HP, "ClearMon missing map changed current environment");

    M2Share.ObjectManager = new ObjectManager();
    var immediateEnvironment = NewEnvironment("clear-immediate-map");
    var immediateNpc = new NormNpc
    {
        m_PEnvir = immediateEnvironment,
        m_sMapName = immediateEnvironment.sMapName
    };
    var immediate = NewActor(immediateEnvironment, Grobal2.RC_ANIMAL, 3, 3, 108);
    var immediateBridge = new PasApiBridge { CurrentNpc = immediateNpc };
    Assert(!immediateBridge.CallNpcFunc("ClearMonEx", Values("", true),
            out var exFunction),
        "ClearMonEx function unexpectedly exists");
    AssertNil(exFunction, "ClearMonEx function");
    Assert(immediateBridge.CallNpcMethod("ClearMonEx", Values("", true), out _),
        "ClearMonEx procedure was not dispatched");
    Assert(immediate.m_boGhost && !immediate.m_boDeath,
        "ClearMonEx true did not directly mark the alive monster as ghost");
    Equal(108, immediate.m_WAbil.HP, "ClearMonEx true changed HP before deletion");
    Assert(!immediate.m_boNoItem, "ClearMonEx true entered the normal death branch");

    static Envirnoment NewEnvironment(string name)
    {
        var environment = new Envirnoment { sMapName = name };
        typeof(Envirnoment).GetMethod("Initialize",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .Invoke(environment, new object[] { (short)30, (short)30 });
        return environment;
    }

    static TBaseObject NewActor(Envirnoment environment, byte race, short x, short y,
        int hp)
    {
        var actor = NewUnplacedActor(environment, race, x, y, hp);
        Assert(ReferenceEquals(environment.AddToMap(x, y, CellType.OS_MOVINGOBJECT, actor),
                actor),
            "ClearMon test actor was not placed on the map");
        return actor;
    }

    static TBaseObject NewUnplacedActor(Envirnoment environment, byte race, short x,
        short y, int hp)
    {
        var actor = new TBaseObject
        {
            m_sCharName = $"clear-actor-{race}-{x}-{y}-{hp}",
            m_PEnvir = environment,
            m_sMapName = environment.sMapName,
            m_btRaceServer = race,
            m_nCurrX = x,
            m_nCurrY = y
        };
        actor.m_WAbil.HP = hp;
        actor.m_WAbil.MaxHP = Math.Max(hp, 1);
        return actor;
    }
}

static void RunNativeExperienceRegressions()
{
    const uint splitLimit = 0xFFB43480u;
    const uint naturalHeroCap = 0xFD51DA7Fu;

    var wrapPlayer = NewExperiencePlayer("wrap-player");
    const uint playerInitialExp = 0xFFB00000u;
    const uint playerRequestedExp = 0x00600000u;
    const uint playerInitialFightExp = 0x10203040u;
    var playerAcceptedExp = unchecked(splitLimit - playerInitialExp);
    var playerRemainderExp = unchecked(playerRequestedExp - playerAcceptedExp);
    wrapPlayer.m_Abil.Level = 1;
    wrapPlayer.m_Abil.Exp = unchecked((int)playerInitialExp);
    wrapPlayer.m_Abil.MaxExp = int.MinValue;
    wrapPlayer.m_dwFightExp = unchecked((int)playerInitialFightExp);
    M2Share.g_Config.dwNeedExps[2] = int.MinValue;
    M2Share.g_Config.dwNeedExps[3] = int.MinValue;
    CallGive(wrapPlayer, "经验", unchecked((int)playerRequestedExp),
        "player wrap Give");

    Assert(wrapPlayer.m_MsgList.Count >= 2,
        "player wrap did not queue continuation and current win-exp messages");
    var playerContinuation = wrapPlayer.m_MsgList[0];
    Equal(Grobal2.RM_NATIVE_EXP_CONTINUE, playerContinuation.wIdent,
        "player wrap continuation order");
    Equal(0, playerContinuation.wParam, "player wrap continuation mode");
    EqualBits(playerRemainderExp, playerContinuation.nParam1,
        "player wrap continuation remainder");
    Equal(1, playerContinuation.nParam2,
        "player wrap continuation hero-share flag");
    Equal(1, playerContinuation.nParam3,
        "player wrap continuation fight-exp flag");
    var playerCurrentChunk = wrapPlayer.m_MsgList[1];
    Equal(Grobal2.RM_WINEXP, playerCurrentChunk.wIdent,
        "player wrap current win-exp order");
    EqualBits(playerAcceptedExp, playerCurrentChunk.nParam1,
        "player wrap accepted chunk notification");

    Equal(1, DrainNativeExperienceContinuations(wrapPlayer),
        "player wrap continuation count");
    EqualBits(unchecked(playerInitialFightExp + playerRequestedExp),
        wrapPlayer.m_dwFightExp, "player wrap final fight-exp accumulation");
    EqualBits(0x00100000u, wrapPlayer.m_Abil.Exp,
        "player wrap final experience after level deductions");

    var thresholdPlayer = NewExperiencePlayer("threshold-player");
    thresholdPlayer.m_Abil.Level = 10;
    thresholdPlayer.m_Abil.Exp = unchecked((int)0xFFB43470u);
    thresholdPlayer.m_Abil.MaxExp = -1;
    thresholdPlayer.m_dwFightExp = 40;
    CallGive(thresholdPlayer, "经验", 0x20,
        "non-wrap split-limit crossing Give");
    EqualBits(0xFFB43490u, thresholdPlayer.m_Abil.Exp,
        "non-wrap split-limit crossing experience");
    Equal(40 + 0x20, thresholdPlayer.m_dwFightExp,
        "non-wrap split-limit crossing fight experience");
    Assert(!thresholdPlayer.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_NATIVE_EXP_CONTINUE),
        "non-wrap split-limit crossing queued a continuation");

    var maxLevelPlayer = NewExperiencePlayer("max-level-player");
    maxLevelPlayer.m_Abil.Level = 998;
    maxLevelPlayer.m_Abil.Exp = 0;
    maxLevelPlayer.m_Abil.MaxExp = 10;
    M2Share.g_Config.dwNeedExps[999] = 10;
    CallGive(maxLevelPlayer, "经验", 35, "998-to-999 Give");
    Equal(999, maxLevelPlayer.m_Abil.Level, "998-to-999 final level");
    Equal(5, maxLevelPlayer.m_Abil.Exp,
        "level-999 repeated experience deductions");
    Equal(2, maxLevelPlayer.m_MsgList.Count(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE &&
            message.Buff == "您的等级已到达上限，不会再获得经验值"),
        "level-999 repeated full-level prompts");

    var seriesPlayer = NewExperiencePlayer("series-player");
    seriesPlayer.m_Abil.Exp = 123;
    Assert(seriesPlayer.Operate(new TProcessMessage
        {
            wIdent = Grobal2.RM_WINEXP,
            wParam = 0x3456,
            nParam1 = unchecked((int)0x12345678u)
        }),
        "player RM_WINEXP Operate returned False");
    Equal(Grobal2.SM_WINEXP, seriesPlayer.m_DefMsg.Ident,
        "player RM_WINEXP client ident");
    Equal(0x3456, seriesPlayer.m_DefMsg.Series,
        "player RM_WINEXP mode-to-Series mapping");

    var heroWrapMaster = NewExperiencePlayer("hero-wrap-master");
    heroWrapMaster.m_nForceLv = 10;
    var wrapHero = NewExperienceHero(heroWrapMaster, 2, 1,
        unchecked((int)0xFFB00000u), int.MinValue);
    M2Share.g_Config.dwNeedExps[2] = int.MinValue;
    M2Share.g_Config.dwNeedExps[3] = int.MinValue;
    const uint heroRequestedExp = 0x00300000u;
    var heroAcceptedBaseExp = unchecked((splitLimit - 0xFFB00000u) >> 1);
    var heroRemainderExp = unchecked(heroRequestedExp - heroAcceptedBaseExp);
    var heroAppliedExp = unchecked(heroAcceptedBaseExp * 2u);
    InvokeGrantNativeHeroExperience(heroWrapMaster, wrapHero,
        unchecked((int)heroRequestedExp), true, false);
    Assert(wrapHero.m_MsgList.Count >= 2,
        "type-2 hero wrap did not queue continuation and current win-exp messages");
    var heroContinuation = wrapHero.m_MsgList[0];
    Equal(Grobal2.RM_NATIVE_EXP_CONTINUE, heroContinuation.wIdent,
        "type-2 hero wrap continuation order");
    Equal(0, heroContinuation.wParam, "hero wrap continuation wParam");
    EqualBits(heroRemainderExp, heroContinuation.nParam1,
        "hero wrap continuation remainder");
    Equal(0, heroContinuation.nParam2,
        "hero wrap continuation natural-mode flag");
    Equal(1, heroContinuation.nParam3,
        "hero wrap continuation fight-exp flag");
    Equal(Grobal2.RM_WINEXP, wrapHero.m_MsgList[1].wIdent,
        "type-2 hero wrap current win-exp order");
    EqualBits(heroAppliedExp, wrapHero.m_MsgList[1].nParam1,
        "type-2 hero wrap doubled notification");
    EqualBits(0x7FB43480u, wrapHero.m_Abil.Exp,
        "type-2 hero wrap accepted experience after level deduction");
    EqualBits(heroAcceptedBaseExp, wrapHero.m_dwFightExp,
        "type-2 hero wrap base fight experience");
    Equal(1, DrainNativeExperienceContinuations(wrapHero),
        "type-2 hero wrap continuation count");
    EqualBits(heroRequestedExp, wrapHero.m_dwFightExp,
        "type-2 hero wrap final base fight experience");
    EqualBits(0x00100000u, wrapHero.m_Abil.Exp,
        "type-2 hero wrap final experience after level deductions");
    Equal(3, wrapHero.m_Abil.Level, "type-2 hero wrap final level");

    var eligibleNaturalMaster = NewExperiencePlayer("eligible-natural-master");
    eligibleNaturalMaster.m_nForceLv = 13;
    var eligibleNaturalHero = NewExperienceHero(eligibleNaturalMaster, 2, 10,
        100, 1000);
    eligibleNaturalHero.m_dwFightExp = 7;
    InvokeGrantNativeHeroExperience(eligibleNaturalMaster, eligibleNaturalHero,
        25, true, false);
    Equal(150, eligibleNaturalHero.m_Abil.Exp,
        "eligible natural type-2 doubled experience");
    Equal(32, eligibleNaturalHero.m_dwFightExp,
        "eligible natural type-2 base fight experience");

    var eligibleDirectMaster = NewExperiencePlayer("eligible-direct-master");
    eligibleDirectMaster.m_nForceLv = 13;
    var eligibleDirectHero = NewExperienceHero(eligibleDirectMaster, 2, 10,
        100, 1000);
    eligibleDirectHero.m_dwFightExp = 7;
    InvokeGrantNativeHeroExperience(eligibleDirectMaster, eligibleDirectHero,
        25, false, true);
    Equal(150, eligibleDirectHero.m_Abil.Exp,
        "eligible direct type-2 doubled experience");
    Equal(7, eligibleDirectHero.m_dwFightExp,
        "eligible direct type-2 fight experience");

    var ineligibleNaturalMaster = NewExperiencePlayer("ineligible-natural-master");
    ineligibleNaturalMaster.m_nForceLv = 12;
    var ineligibleNaturalHero = NewExperienceHero(ineligibleNaturalMaster, 2, 10,
        100, 1000);
    ineligibleNaturalHero.m_dwFightExp = 7;
    InvokeGrantNativeHeroExperience(ineligibleNaturalMaster, ineligibleNaturalHero,
        25, true, false);
    Equal(100, ineligibleNaturalHero.m_Abil.Exp,
        "ineligible natural type-2 main experience");
    Equal(32, ineligibleNaturalHero.m_dwFightExp,
        "ineligible natural type-2 fight statistic ordering");

    var ineligibleDirectMaster = NewExperiencePlayer("ineligible-direct-master");
    ineligibleDirectMaster.m_nForceLv = 12;
    var ineligibleDirectHero = NewExperienceHero(ineligibleDirectMaster, 2, 10,
        100, 1000);
    ineligibleDirectHero.m_dwFightExp = 7;
    InvokeGrantNativeHeroExperience(ineligibleDirectMaster, ineligibleDirectHero,
        25, true, true);
    Equal(100, ineligibleDirectHero.m_Abil.Exp,
        "ineligible direct type-2 main experience");
    Equal(7, ineligibleDirectHero.m_dwFightExp,
        "ineligible direct type-2 fight statistic ordering");

    var fullDirectMaster = NewExperiencePlayer("full-direct-master");
    var fullDirectHero = NewExperienceHero(fullDirectMaster, 1, 999,
        100, 1000);
    fullDirectHero.m_dwFightExp = 7;
    InvokeGrantNativeHeroExperience(fullDirectMaster, fullDirectHero,
        25, true, true);
    Equal(100, fullDirectHero.m_Abil.Exp,
        "full-level direct hero main experience");
    Equal(7, fullDirectHero.m_dwFightExp,
        "full-level direct hero fight statistic ordering");

    var cappedHeroMaster = NewExperiencePlayer("capped-hero-master");
    var cappedHero = NewExperienceHero(cappedHeroMaster, 1, 200,
        unchecked((int)(naturalHeroCap - 5)), -1);
    InvokeGrantNativeHeroExperience(cappedHeroMaster, cappedHero, 10, false, false);
    EqualBits(naturalHeroCap, cappedHero.m_Abil.Exp,
        "natural level-200 hero experience cap");
    InvokeGrantNativeHeroExperience(cappedHeroMaster, cappedHero, 10, false, false);
    EqualBits(naturalHeroCap, cappedHero.m_Abil.Exp,
        "capped natural level-200 hero accepted more experience");

    var overNaturalCapHero = NewExperienceHero(cappedHeroMaster, 1, 201,
        123, 1000);
    overNaturalCapHero.m_dwFightExp = 7;
    InvokeGrantNativeHeroExperience(cappedHeroMaster, overNaturalCapHero,
        10, true, false);
    Equal(123, overNaturalCapHero.m_Abil.Exp,
        "natural level-above-200 hero main experience");
    Equal(17, overNaturalCapHero.m_dwFightExp,
        "natural level-above-200 hero fight experience");

    M2Share.g_Config.dwNeedExps[200] = 10;
    var level200LoopHero = NewExperienceHero(cappedHeroMaster, 1, 200, 5, 10);
    InvokeGrantNativeHeroExperience(cappedHeroMaster, level200LoopHero,
        20, false, false);
    Equal(200, level200LoopHero.m_Abil.Level,
        "natural level-200 hero level changed");
    Equal(5, level200LoopHero.m_Abil.Exp,
        "natural level-200 hero repeated experience deductions");

    var forceSyncMaster = NewExperiencePlayer("force-sync-master");
    forceSyncMaster.m_nForceLv = unchecked((int)0xA5A50001u);
    M2Share.g_Config.dwNeedExps[2] = 10;
    M2Share.g_Config.dwNeedExps[3] = 1000;
    var forceSyncHero = NewExperienceHero(forceSyncMaster, 1, 1, 0, 10);
    InvokeGrantNativeHeroExperience(forceSyncMaster, forceSyncHero,
        25, false, false);
    Equal(3, forceSyncHero.m_Abil.Level, "type-1 hero multi-level result");
    Equal(5, forceSyncHero.m_Abil.Exp, "type-1 hero multi-level remainder");
    EqualBits(0xA5A50003u, forceSyncMaster.m_nForceLv,
        "type-1 hero level did not synchronize the owner ForceLv low word");
}

static void RunNativeForceRegressions()
{
    var tableType = typeof(HeroObject).Assembly.GetType("GameSvr.NativeHeroForceTable")
        ?? throw new InvalidOperationException("NativeHeroForceTable was not found");
    var tableField = tableType.GetField("Thresholds",
        System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("native Force threshold table was not found");
    var thresholds = (int[])tableField.GetValue(null);
    Equal(5000, thresholds.Length, "native Force threshold count");

    var tableBytes = new byte[thresholds.Length * sizeof(int)];
    for (var i = 0; i < thresholds.Length; i++)
        BinaryPrimitives.WriteInt32LittleEndian(tableBytes.AsSpan(i * sizeof(int)),
            thresholds[i]);
    var tableHash = Convert.ToHexString(SHA256.HashData(tableBytes));
    Assert(tableHash ==
           "9813FBA8BA5265AF6C1077700AF46378CE6611F72B9C6C02AF4D2381B4C2AF4F",
        "native Force threshold table hash differs from live M2 memory");

    var fealtyType = typeof(HeroObject).Assembly.GetType("GameSvr.NativeHeroFealtyTable")
        ?? throw new InvalidOperationException("NativeHeroFealtyTable was not found");
    var fealtyField = fealtyType.GetField("Bonuses",
        System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("native hero fealty table was not found");
    var bonuses = (int[])fealtyField.GetValue(null);
    Equal(1001, bonuses.Length, "native hero fealty table count");
    var bonusBytes = new byte[bonuses.Length * sizeof(int)];
    for (var i = 0; i < bonuses.Length; i++)
        BinaryPrimitives.WriteInt32LittleEndian(bonusBytes.AsSpan(i * sizeof(int)),
            bonuses[i]);
    Assert(Convert.ToHexString(SHA256.HashData(bonusBytes)) ==
           "A6F3F6BA531F0C94CCBD33551B4EF15587DFF4AFC48F31A373C1755BC9D270DE",
        "native hero fealty table hash differs from the x87 result");
    foreach (var (glory, expected) in new (int, int)[]
             {
                 (-1, 0), (0, 0), (1, 1), (100, 3), (499, 2477),
                 (500, 2500), (501, 2523), (900, 4997), (999, 4999),
                 (1000, 5000), (1001, 5000)
             })
    {
        Equal(expected, GetNativeFealtyBonus(fealtyType, glory),
            $"native hero fealty bonus glory {glory}");
    }

    foreach (var (level, expected) in new (int, int)[]
             {
                 (-1, 900000), (0, 1003), (499, 25989), (500, 26672),
                 (999, 57158), (1000, 57786), (1499, 89719), (1500, 94225),
                 (1999, 154043), (2000, 160006), (2499, 251963),
                 (2500, 252048), (2999, 255208), (3000, 345454),
                 (3999, 542403), (4000, 572188), (4998, 898755),
                 (4999, 898804), (5000, 900000)
             })
    {
        Equal(expected, GetNativeForceThreshold(tableType, level),
            $"native Force threshold level {level}");
    }

    var packetPlayer = NewExperiencePlayer("force-packet-player");
    Assert(packetPlayer.Operate(new TProcessMessage
        {
            wIdent = Grobal2.RM_GLORYFEALTY,
            nParam1 = 0x12345,
            nParam2 = 0x23456
        }),
        "RM_GLORYFEALTY Operate returned False");
    Equal(Grobal2.SM_GLORYFEALTY, packetPlayer.m_DefMsg.Ident,
        "glory-fealty client ident");
    Equal(0, packetPlayer.m_DefMsg.Recog, "glory-fealty Recog");
    Equal(0x2345, packetPlayer.m_DefMsg.Param, "glory-fealty Param");
    Equal(0x3456, packetPlayer.m_DefMsg.Tag, "glory-fealty Tag");
    Equal(0, packetPlayer.m_DefMsg.Series, "glory-fealty Series");

    var boundaryMaster = NewExperiencePlayer("force-boundary-master");
    SetNativeGlory(boundaryMaster, -7);
    var boundaryHero = NewExperienceHero(boundaryMaster, 1, 1, 0, int.MaxValue);
    InvokeGrantNativeHeroExperience(boundaryMaster, boundaryHero,
        1002, false, false);
    Equal(0, boundaryHero.m_nForceLv, "Force advanced below level-0 threshold");
    Equal(1002, boundaryHero.m_nForceExp, "Force level-0 remainder below threshold");
    Assert(!boundaryMaster.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_GLORYFEALTY),
        "Force queued glory-fealty without a level change");

    InvokeGrantNativeHeroExperience(boundaryMaster, boundaryHero,
        1, false, false);
    Equal(1, boundaryHero.m_nForceLv, "Force level-0 exact threshold");
    Equal(0, boundaryHero.m_nForceExp, "Force exact-threshold remainder");
    Equal(3010, boundaryHero.m_nMaxForceExp,
        "Force level-1 maximum experience");
    var gloryMessage = boundaryMaster.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_GLORYFEALTY);
    Equal(0, gloryMessage.nParam1, "negative native glory was not clamped");
    Equal(1, gloryMessage.nParam2, "Force combined fealty notification");

    var multiMaster = NewExperiencePlayer("force-multi-master");
    var multiHero = NewExperienceHero(multiMaster, 1, 1, 0, int.MaxValue);
    InvokeGrantNativeHeroExperience(multiMaster, multiHero,
        1003 + 3010 + 1, false, false);
    Equal(2, multiHero.m_nForceLv, "Force multi-level result");
    Equal(1, multiHero.m_nForceExp, "Force multi-level remainder");
    Equal(GetNativeForceThreshold(tableType, 2), multiHero.m_nMaxForceExp,
        "Force multi-level next threshold");
    Equal(1, multiMaster.m_MsgList.Count(message =>
            message.wIdent == Grobal2.RM_GLORYFEALTY),
        "Force multi-level notification count");

    var derivedMaster = NewExperiencePlayer("force-derived-master");
    SetNativeGlory(derivedMaster, 500);
    var derivedHero = NewExperienceHero(derivedMaster, 1, 1, 0, int.MaxValue);
    InvokeGrantNativeHeroExperience(derivedMaster, derivedHero,
        1003, false, false);
    var derivedMessage = derivedMaster.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_GLORYFEALTY);
    Equal(500, derivedMessage.nParam1, "derived-fealty glory notification");
    Equal(2501, derivedMessage.nParam2,
        "derived-fealty ForceLv plus x87 bonus");

    var capMaster = NewExperiencePlayer("force-cap-master");
    var capHero = NewExperienceHero(capMaster, 1, 1, 0, int.MaxValue);
    capHero.m_nForceLv = 4999;
    capHero.m_nForceExp = 898803;
    capHero.m_nMaxForceExp = 898804;
    InvokeGrantNativeHeroExperience(capMaster, capHero, 1, false, false);
    Equal(5000, capHero.m_nForceLv, "Force level 4999 to 5000");
    Equal(0, capHero.m_nForceExp, "Force level-5000 remainder");
    Equal(900000, capHero.m_nMaxForceExp, "Force level-5000 threshold");

    InvokeGrantNativeHeroExperience(capMaster, capHero, 12345, false, false);
    Equal(5000, capHero.m_nForceLv, "Force advanced beyond level 5000");
    Equal(0, capHero.m_nForceExp, "Force changed at level 5000");

    var wrapMaster = NewExperiencePlayer("force-wrap-master");
    var wrapHero = NewExperienceHero(wrapMaster, 1, 1, 0, int.MaxValue);
    wrapHero.m_nForceExp = unchecked((int)0xFFFFFFF0u);
    InvokeGrantNativeHeroExperience(wrapMaster, wrapHero, 0x20, false, false);
    Equal(0, wrapHero.m_nForceLv, "Force UInt32 wrap advanced a level");
    Equal(0x10, wrapHero.m_nForceExp, "Force UInt32 wrap remainder");

    var accumulatorPlayer = NewExperiencePlayer("force-accumulator-player");
    InvokeAddNativeHeroExperienceAccumulator(accumulatorPlayer, 999_999_999, 0);
    var accumulator = GetNativeHeroExperienceAccumulator(accumulatorPlayer);
    Equal(24, accumulator.Length, "native hero experience accumulator size");
    Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(accumulator.AsSpan(0, 2)),
        "native accumulator count before threshold");
    EqualBits(999_999_999u, unchecked((int)
        BinaryPrimitives.ReadUInt32LittleEndian(accumulator.AsSpan(8, 4))),
        "native accumulator slot-0 remainder offset");
    InvokeAddNativeHeroExperienceAccumulator(accumulatorPlayer, 1, 0);
    accumulator = GetNativeHeroExperienceAccumulator(accumulatorPlayer);
    Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(accumulator.AsSpan(0, 2)),
        "native accumulator carried on exact threshold");
    EqualBits(1_000_000_000u, unchecked((int)
        BinaryPrimitives.ReadUInt32LittleEndian(accumulator.AsSpan(8, 4))),
        "native accumulator exact threshold remainder");
    InvokeAddNativeHeroExperienceAccumulator(accumulatorPlayer, 1, 0);
    accumulator = GetNativeHeroExperienceAccumulator(accumulatorPlayer);
    Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(accumulator.AsSpan(0, 2)),
        "native accumulator deferred carry count");
    EqualBits(1u, unchecked((int)
        BinaryPrimitives.ReadUInt32LittleEndian(accumulator.AsSpan(8, 4))),
        "native accumulator deferred carry remainder");

    foreach (var (job, magicId, magicName) in new (byte, ushort, string)[]
             {
                 (0, SpellsDef.SKILL_FIRESWORD, "烈火剑法"),
                 (1, SpellsDef.SKILL_WINDTEBO, "灭天火"),
                 (2, SpellsDef.SKILL_FIRECHARM, "灵魂火符")
             })
    {
        var skillMaster = NewExperiencePlayer($"force-skill-master-{job}");
        var skillHero = NewExperienceHero(skillMaster, 1, 44, 0, int.MaxValue);
        skillHero.m_btJob = job;
        skillHero.m_nForceLv = 3000;
        var userMagic = new TUserMagic
        {
            MagicInfo = new TMagic
            {
                wMagicID = magicId,
                sMagicName = magicName,
                MaxTrain = new[] { 10, 20, 30, 40 },
                btTrainLv = 3
            },
            wMagIdx = magicId,
            btLevel = 3,
            nTranPoint = 17
        };
        skillHero.m_HeroMagicList.Add(userMagic);

        InvokeRefreshNativeForceState(skillHero);
        Equal(4, userMagic.btLevel, $"job {job} native primary skill promotion");
        var levelMessage = skillHero.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_MAGIC_LVEXP);
        Equal(magicId, levelMessage.nParam1,
            $"job {job} native primary skill magic id");
        Equal(4, levelMessage.nParam2,
            $"job {job} native primary skill message level");
        Assert(skillMaster.m_MsgList.Any(message =>
                message.wIdent == Grobal2.RM_SYSMESSAGE &&
                message.Buff == $"由于你们亲密的关系，您的英雄已经领悟了4级{magicName}"),
            $"job {job} native primary skill promotion hint");

        var encodedMagic = InvokeEncodeHeroMagic(userMagic);
        Equal(4, BinaryPrimitives.ReadInt16LittleEndian(encodedMagic.AsSpan(20, 2)),
            $"job {job} encoded hero magic level");
        Equal(40, BinaryPrimitives.ReadInt32LittleEndian(encodedMagic.AsSpan(38, 4)),
            $"job {job} encoded level-4 max train clamp");

        skillHero.m_MsgList.Clear();
        skillMaster.m_MsgList.Clear();
        skillHero.m_nForceLv = 2000;
        InvokeRefreshNativeForceState(skillHero);
        Equal(4, userMagic.btLevel,
            $"job {job} native primary skill changed in neutral fealty band");
        Assert(!skillHero.m_MsgList.Any(message =>
                message.wIdent == Grobal2.RM_MAGIC_LVEXP),
            $"job {job} neutral fealty band sent a skill-level message");

        skillHero.m_MsgList.Clear();
        skillMaster.m_MsgList.Clear();
        skillHero.m_nForceLv = 999;
        InvokeRefreshNativeForceState(skillHero);
        Equal(3, userMagic.btLevel, $"job {job} native primary skill demotion");
        Assert(skillMaster.m_MsgList.Any(message =>
                message.wIdent == Grobal2.RM_SYSMESSAGE &&
                message.Buff == $"由于英雄的忠诚度下降超出限制，您的4级{magicName}下降到了3级"),
            $"job {job} native primary skill demotion hint");
    }
}

static int GetNativeForceThreshold(Type tableType, int level)
{
    var method = tableType.GetMethod("GetThreshold",
        System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("native Force threshold getter was not found");
    return (int)method.Invoke(null, new object[] { level });
}

static int GetNativeFealtyBonus(Type tableType, int glory)
{
    var method = tableType.GetMethod("GetBonus",
        System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("native hero fealty getter was not found");
    return (int)method.Invoke(null, new object[] { glory });
}

static void SetNativeGlory(TPlayObject player, int value)
{
    var intimacyField = typeof(TPlayObject).GetField("m_dNativeHeroIntimacy",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("native hero intimacy field was not found");
    var baseField = typeof(TPlayObject).GetField("m_nNativeHeroIntimacyBase",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("native hero intimacy base field was not found");
    var refreshMethod = typeof(TPlayObject).GetMethod("RefreshNativeHeroIntimacy",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("native hero intimacy refresh was not found");
    intimacyField.SetValue(player, (double)value);
    baseField.SetValue(player, 0);
    refreshMethod.Invoke(player, null);
}

static void InvokeAddNativeHeroExperienceAccumulator(TPlayObject player, int amount,
    int slot)
{
    var method = typeof(TPlayObject).GetMethod("AddNativeHeroExperienceAccumulator",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "native hero experience accumulator method was not found");
    method.Invoke(player, new object[] { amount, slot });
}

static byte[] GetNativeHeroExperienceAccumulator(TPlayObject player)
{
    var field = typeof(TPlayObject).GetField("m_NativeHeroExperienceAccumulator",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "native hero experience accumulator field was not found");
    return (byte[])field.GetValue(player);
}

static void InvokeRefreshNativeForceState(HeroObject hero)
{
    var method = typeof(HeroObject).GetMethod("RefreshNativeForceState",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("native Force refresh method was not found");
    method.Invoke(hero, null);
}

static byte[] InvokeEncodeHeroMagic(TUserMagic userMagic)
{
    var method = typeof(HeroObject).GetMethod("EncodeHeroMagic",
        System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("hero magic encoder was not found");
    return (byte[])method.Invoke(null, new object[] { userMagic });
}

static void RunCanonicalItemGiveLogRegression()
{
    var player = NewExperiencePlayer("canonical-log-player");
    player.m_sMapName = "canonical-map";
    player.m_nCurrX = 21;
    player.m_nCurrY = 43;
    M2Share.LogStringList.Clear();
    CallGive(player, "NORMAL-GIFT", 1, "canonical item-name Give");
    Equal(1, M2Share.LogStringList.Count, "canonical item-name Give log count");
    var columns = ((string)M2Share.LogStringList[0]).Split('\t');
    Assert(columns.Length == 9, "canonical item-name Give log column count");
    Assert(columns[5] == "normal-gift",
        "type-9 Give log did not use the canonical standard-item name");
}

static TPlayObject NewExperiencePlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_sMapName = "audit-map",
    m_nCurrX = 12,
    m_nCurrY = 34
};

static HeroObject NewExperienceHero(TPlayObject master, byte heroType, ushort level,
    int experience, int maxExperience)
{
    var hero = new HeroObject
    {
        m_Master = master,
        MasterName = master.m_sCharName,
        HeroLevel = level
    };
    var heroTypeProperty = typeof(HeroObject).GetProperty(nameof(HeroObject.HeroType));
    var heroTypeSetter = heroTypeProperty?.GetSetMethod(true);
    if (heroTypeSetter == null)
        throw new InvalidOperationException("HeroType non-public setter was not found");
    heroTypeSetter.Invoke(hero, new object[] { heroType });
    hero.m_Abil.Level = level;
    hero.m_Abil.Exp = experience;
    hero.m_Abil.MaxExp = maxExperience;
    master.m_HeroObject = hero;
    return hero;
}

static void CallGive(TPlayObject player, string name, int amount, string operation)
{
    var bridge = new PasApiBridge { CurrentPlayer = player, CurrentNpc = null };
    Assert(bridge.CallPlayerFunc("Give", Values(name, amount), out var result),
        operation + " was not dispatched");
    Assert(result.AsBool(), operation + " returned False");
}

static void InvokeGrantNativeHeroExperience(TPlayObject player, HeroObject hero,
    int amount, bool countFightExperience, bool directMode)
{
    var method = typeof(TPlayObject).GetMethod("GrantNativeHeroExperience",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    if (method == null)
        throw new InvalidOperationException("GrantNativeHeroExperience was not found");
    method.Invoke(player,
        new object[] { hero, amount, countFightExperience, directMode });
}

static int DrainNativeExperienceContinuations(TBaseObject target)
{
    var continuationCount = 0;
    var inspectedCount = 0;
    while (target.m_MsgList.Count > 0)
    {
        if (++inspectedCount > 1000)
            throw new InvalidOperationException("native experience continuation did not terminate");

        var queued = target.m_MsgList[0];
        target.m_MsgList.RemoveAt(0);
        if (queued.wIdent != Grobal2.RM_NATIVE_EXP_CONTINUE)
            continue;

        continuationCount++;
        var processMessage = new TProcessMessage
        {
            wIdent = queued.wIdent,
            wParam = queued.wParam,
            nParam1 = queued.nParam1,
            nParam2 = queued.nParam2,
            nParam3 = queued.nParam3,
            BaseObject = queued.BaseObject?.ObjectId ?? queued.ObjectId,
            dwDeliveryTime = queued.dwDeliveryTime,
            boLateDelivery = queued.boLateDelivery,
            sMsg = queued.Buff ?? string.Empty,
            Payload = queued.Payload
        };
        Assert(target.Operate(processMessage),
            "native experience continuation Operate returned False");
    }
    return continuationCount;
}

static void FillBag(TPlayObject player, int count)
{
    player.m_ItemList.Clear();
    for (var i = 0; i < count; i++)
    {
        player.m_ItemList.Add(new TUserItem
        {
            MakeIndex = 10000 + i,
            wIndex = 1,
            Dura = 100,
            DuraMax = 100
        });
    }
}

static TUserItem NewItem(int makeIndex, ushort itemIndex, ushort dura) => new()
{
    MakeIndex = makeIndex,
    wIndex = itemIndex,
    Dura = dura,
    DuraMax = 100
};

static List<PasValue> Values(params object[] values) => values.Select(value => value switch
{
    int number => PasValue.FromInt(number),
    string text => PasValue.FromString(text),
    bool flag => PasValue.FromBool(flag),
    _ => PasValue.FromObject(value)
}).ToList();

static void RequireClosed(string source, string name, string description)
{
    Equal(1, Count(source, $"case \"{name}\":"), description + " dispatch count");
    // The arm must *be* the reject, not merely mention it. A window-limited regex
    // measures how many characters of commentary sit above the reject rather than
    // whether the arm is closed, and it also matches the token inside a comment,
    // so the label's whole body is sliced out and stripped of comments first.
    var marker = $"case \"{name}\":";
    var start = source.IndexOf(marker, StringComparison.Ordinal);
    Assert(start >= 0, description + " case missing");
    var next = source.IndexOf("case \"", start + marker.Length,
        StringComparison.Ordinal);
    var body = next < 0 ? source[start..] : source[start..next];
    var code = string.Concat(body[marker.Length..]
        .Split('\n')
        .Select(line =>
        {
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            return (comment < 0 ? line : line[..comment]) + "\n";
        }));
    code = Regex.Replace(code, @"\s+", " ").Trim();
    Assert(code == "return RejectUnsupportedNativeApi();",
        $"{description} fail-closed dispatch: expected the arm to be exactly "
        + $"`return RejectUnsupportedNativeApi();`, actual `{code}`");
}

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
    if (start < 0 || end < 0) throw new InvalidOperationException(
        $"source slice not found: {startMarker} -> {endMarker}");
    return source.Substring(start, end - start);
}

static int Count(string source, string value)
{
    var count = 0;
    for (var offset = 0;;)
    {
        var index = source.IndexOf(value, offset, StringComparison.Ordinal);
        if (index < 0) return count;
        count++;
        offset = index + value.Length;
    }
}

static void Require(string source, string pattern, string message) =>
    Assert(Regex.IsMatch(source, pattern,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase), message + " missing");

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
        "repository root containing GameSvr/GameSvr.csproj was not found");
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

static void AssertNil(PasValue value, string message) =>
    Assert(value.Type == PasValueType.Nil, message + " failure did not return Nil");

static void Equal(int expected, int actual, string message)
{
    if (expected != actual) throw new InvalidOperationException(
        $"{message}: expected {expected}, actual {actual}");
}

static void EqualBits(uint expected, int actual, string message)
{
    if (expected != unchecked((uint)actual)) throw new InvalidOperationException(
        $"{message}: expected 0x{expected:X8}, actual 0x{unchecked((uint)actual):X8}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class RecalcProbePlayer : TPlayObject
{
    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();
}

// Replays a seeded System.Random through the RandomNumber facade, so the
// group-fly seed search keeps predicting the exact draws the product will take.
sealed class SeededProbeRandom : RandomNumber
{
    private readonly Random _inner;

    internal SeededProbeRandom(int seed) => _inner = new Random(seed);

    public override int Random(int Value) => _inner.Next(Value);

    public override int Random() => 0;

    public override int Random(int minValue, int maxValue) =>
        minValue + _inner.Next(maxValue - minValue);

    public override int GetRandomNumber(int minValue, int maxValue) =>
        minValue + _inner.Next(maxValue - minValue + 1);
}

sealed record PlayerSnapshot(
    KeyValuePair<int, int>[] V,
    KeyValuePair<int, int>[] S,
    int Gold,
    int GameGold,
    int GamePoint,
    int PaymentPoint,
    int ShengWan,
    int MessageCount,
    TUserItem[] Items)
{
    public static PlayerSnapshot Capture(TPlayObject player) => new(
        player.m_ScriptVVars.OrderBy(item => item.Key).ToArray(),
        player.m_ScriptSVars.OrderBy(item => item.Key).ToArray(),
        player.m_nGold,
        player.m_nGameGold,
        player.m_nGamePoint,
        player.m_nPayMentPoint,
        player.m_nShengWan,
        player.m_MsgList.Count,
        player.m_ItemList.ToArray());

    public void AssertUnchanged(TPlayObject player, string operation)
    {
        Ensure(V.SequenceEqual(player.m_ScriptVVars.OrderBy(item => item.Key)),
            operation + " changed V variables");
        Ensure(S.SequenceEqual(player.m_ScriptSVars.OrderBy(item => item.Key)),
            operation + " changed S variables");
        Ensure(Gold == player.m_nGold, operation + " changed Gold");
        Ensure(GameGold == player.m_nGameGold, operation + " changed GameGold");
        Ensure(GamePoint == player.m_nGamePoint, operation + " changed GamePoint");
        Ensure(PaymentPoint == player.m_nPayMentPoint, operation + " changed payment points");
        Ensure(ShengWan == player.m_nShengWan, operation + " changed ShengWan");
        Ensure(MessageCount == player.m_MsgList.Count, operation + " emitted a message");
        Ensure(Items.SequenceEqual(player.m_ItemList), operation + " changed inventory");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
