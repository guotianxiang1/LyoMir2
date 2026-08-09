extern alias dbsvr;

using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.Configs;
using GameSvr.PasEngine;
using ProtoBuf;
using SystemModule;
using NativeHumanDataCodec = global::DBSvr.Core.NativeHumanDataCodec;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
var root = FindRepositoryRoot();
var bridgePath = Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs");
var bridge = File.ReadAllText(bridgePath);

Equal(2, Count(bridge, "case \"reqitembygoldid\":"), "ReqItemByGoldID dispatch count");
Equal(2, Count(bridge, "case \"reqitembygoldact\":"), "ReqItemByGoldAct dispatch count");
Equal(2, Count(bridge, "case \"reqgetfirstusedgift\":"), "ReqGetFirstUsedGift dispatch count");
Equal(2, Count(bridge, "case \"clientybbuylf\":"), "ClientYBbuyLF dispatch count");

var npcMethodStart = bridge.IndexOf("public bool CallNpcMethod", StringComparison.Ordinal);
var npcFunctionStart = bridge.IndexOf("public bool CallNpcFunc", StringComparison.Ordinal);
Assert(npcMethodStart >= 0 && npcFunctionStart > npcMethodStart,
    "NPC method/function dispatch regions are missing");
var npcMethodRegion = bridge.Substring(npcMethodStart,
    npcFunctionStart - npcMethodStart);
var npcFunctionRegion = bridge.Substring(npcFunctionStart);
RequireMatches(npcMethodRegion,
    "case \\\"reqitembygoldact\\\":[\\s\\S]{0,900}?" +
    "args\\.Count\\s*!=\\s*1[\\s\\S]{0,500}?" +
    "args\\[0\\]\\.ObjVal is not TPlayObject[\\s\\S]{0,500}?" +
    "\\.ReqItemByGoldAct\\(CurrentNpc\\);", 1,
    "GoldAct procedure must accept exactly one explicit PAS Player argument");
RequireMatches(npcFunctionRegion,
    "case \\\"reqitembygoldact\\\":[\\s\\S]{0,900}?" +
    "return RejectUnsupportedNativeApi\\(out result\\);", 1,
    "GoldAct function dispatch must remain fail closed");
RequireMatches(bridge,
    "case \\\"reqitembygoldid\\\":[\\s\\S]{0,900}?" +
    "return RejectUnsupportedNativeApi\\(out result\\);", 2,
    "GoldID dispatch must remain fail closed");
RequireMatches(bridge,
    "case \\\"reqgetfirstusedgift\\\":[\\s\\S]{0,900}?" +
    "return RejectUnsupportedNativeApi\\(out result\\);", 2,
    "first-use gift dispatch must fail closed");
RequireMatches(bridge,
    "case \\\"clientybbuylf\\\":[\\s\\S]{0,900}?" +
    "args\\[0\\]\\.ObjVal is not TPlayObject [A-Za-z0-9_]+[\\s\\S]{0,120}?" +
    "[A-Za-z0-9_]+\\.ClientYBbuyLF\\(CurrentNpc, args\\[1\\]\\.AsInt\\(\\)\\);", 1,
    "YB-to-LingFu dispatch must use the explicit PAS Player and count");

AssertNoSubstitute(bridge, "case \"reqitembygoldid\":", "case \"reqitembyplatina\":");
AssertNoSubstitute(bridge, "case \"reqgetfirstusedgift\":", "case \"clientybbuylf\":");
AssertNoSubstitute(bridge, "case \"clientybbuylf\":", "case \"buywinefromnpc\":");
CheckNativeRewardStateRoundTrip();
CheckRewardStateWiring();
CheckNativeGbkRewardConfigs();
CheckGoldActRewardStateMachine();
CheckGoldActExplicitPlayerDispatch();
CheckGoldActCredentialLifecycle();
CheckGoldActRealGrantContract();
CheckGoldActGrantContract();

Console.WriteLine(
    "PASS APIs=4 cases=8 GoldAct=method-only/fixed-38FF/log55/bag-guard " +
    "YBbuyLF=native GoldID/FirstGift=closed credential=consume-once/death-guard " +
    "state=raw+protobuf+runtime-mapped configs=GBK-9+10");
return;

void PrepareRuntimeConfig()
{
    var setupPath = Path.Combine(AppContext.BaseDirectory, "!Setup.txt");
    if (!File.Exists(setupPath) || new FileInfo(setupPath).Length == 0)
        File.WriteAllText(setupPath,
            "[Server]\r\nServerName=GoldActAudit\r\n", Encoding.ASCII);
    var commandPath = Path.Combine(AppContext.BaseDirectory, "Command.conf");
    if (!File.Exists(commandPath) || new FileInfo(commandPath).Length == 0)
        File.WriteAllText(commandPath,
            "[Command]\r\nAudit=Audit\r\n", Encoding.ASCII);

    var shareDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "Share"));
    Directory.CreateDirectory(shareDirectory);
    var expPath = Path.Combine(shareDirectory, "PlayerUpgradeExp.ini");
    if (!File.Exists(expPath) || new FileInfo(expPath).Length == 0)
        File.WriteAllText(expPath,
            "[PlayerLevelExp]\r\nLEVEL_1=50\r\n", Encoding.ASCII);

    M2Share.ObjectManager ??= new ObjectManager();
    M2Share.ProcessMsgCriticalSection ??= new object();
    M2Share.LogMsgCriticalSection ??= new object();
}

void CheckGoldActRewardStateMachine()
{
    var method = typeof(TPlayObject).GetMethod(
        "RunNativeGoldActRewardStateMachine",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    Assert(method != null, "GoldAct runtime state machine is missing");

    string Run(TPlayObject player, Func<int, bool> grant) =>
        (string)method!.Invoke(player, new object[] { grant })!;

    var player = new TPlayObject();
    player.m_Abil.Level = 55;
    var calls = new List<int>();
    Equal("您还没有成为热血勇士，不能领取奖励物品",
        Run(player, level => { calls.Add(level); return true; }),
        "inactive GoldAct message");
    Equal(0, calls.Count, "inactive GoldAct must not grant");
    Equal((byte)0, player.m_btGoldActNextLevel,
        "inactive GoldAct state changed");

    player.m_btGoldActNextLevel = 1;
    player.m_Abil.Level = 45;
    Equal("您的等级尚未达到46级，还不能领取热血勇士的奖励",
        Run(player, _ => true), "GoldAct low-level message");
    Equal((byte)1, player.m_btGoldActNextLevel,
        "low-level GoldAct state changed");

    player.m_Abil.Level = 49;
    calls.Clear();
    Equal("祝贺您，您已经获得了相应等级的奖励物品，请查看包裹吧\\如果没有找到的话，请留出足够的包裹空间，再次领取",
        Run(player, level => { calls.Add(level); return level < 47; }),
        "GoldAct final merchant message");
    SequenceEqual(new[] { 46, 47 }, calls,
        "GoldAct must stop at first bag/config failure");
    Equal((byte)47, player.m_btGoldActNextLevel,
        "failed GoldAct reward must not advance");

    calls.Clear();
    Run(player, level => { calls.Add(level); return true; });
    SequenceEqual(new[] { 47, 48, 49 }, calls,
        "GoldAct retry must resume at failed level");
    Equal((byte)50, player.m_btGoldActNextLevel,
        "successful GoldAct rewards did not advance");

    calls.Clear();
    Equal("您已经领取过了该等级的奖励，不能再次领取",
        Run(player, level => { calls.Add(level); return true; }),
        "GoldAct already-claimed message");
    Equal(0, calls.Count, "already-claimed GoldAct granted again");

    player.m_btGoldActNextLevel = 55;
    player.m_Abil.Level = 200;
    calls.Clear();
    Run(player, level => { calls.Add(level); return true; });
    SequenceEqual(new[] { 55 }, calls, "GoldAct level must cap at 55");
    Equal((byte)56, player.m_btGoldActNextLevel,
        "GoldAct level-55 completion state");

    var dialogPlayer = new TPlayObject
    {
        m_btGoldActNextLevel = 1
    };
    dialogPlayer.m_Abil.Level = 46;
    var npc = new NormNpc { m_sCharName = "金牌尊者" };
    dialogPlayer.ReqItemByGoldAct(npc);
    var merchantSay = dialogPlayer.m_MsgList.Last(message =>
        message.wIdent == Grobal2.RM_MERCHANTSAY);
    Equal(0, merchantSay.nParam1,
        "GoldAct merchant response changed native RM parameters");
    Equal("金牌尊者/祝贺您，您已经获得了相应等级的奖励物品，请查看包裹吧\\如果没有找到的话，请留出足够的包裹空间，再次领取",
        merchantSay.Buff, "GoldAct RM_MERCHANTSAY payload");
}

void CheckGoldActExplicitPlayerDispatch()
{
    var contextPlayer = new TPlayObject { m_btGoldActNextLevel = 0 };
    var explicitPlayer = new TPlayObject { m_btGoldActNextLevel = 1 };
    explicitPlayer.m_Abil.Level = 46;
    var npc = new NormNpc { m_sCharName = "金牌尊者" };
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };
    var args = new List<PasValue> { PasValue.FromObject(explicitPlayer) };

    Assert(bridge.CallNpcMethod("ReqItemByGoldAct", args, out _),
        "GoldAct NPC method dispatch failed");
    Equal(0, contextPlayer.m_MsgList.Count,
        "GoldAct NPC method rewarded the context player");
    Assert(explicitPlayer.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_MERCHANTSAY),
        "GoldAct NPC method did not reward the explicit player");

    contextPlayer.m_MsgList.Clear();
    explicitPlayer.m_MsgList.Clear();
    explicitPlayer.m_btGoldActNextLevel = 1;
    Assert(!bridge.CallNpcFunc("ReqItemByGoldAct", args, out var result),
        "procedure-only GoldAct API was exposed as an NPC function");
    Assert(result.Type == PasValueType.Nil,
        "fail-closed GoldAct NPC function result is not Nil");
    Equal(0, contextPlayer.m_MsgList.Count,
        "GoldAct NPC function touched the context player");
    Equal(0, explicitPlayer.m_MsgList.Count,
        "GoldAct NPC function rewarded the explicit player");

    Assert(!bridge.CallNpcMethod("ReqItemByGoldAct", new List<PasValue>(), out _),
        "GoldAct NPC method accepted a missing Player argument");
    Assert(!bridge.CallNpcMethod("ReqItemByGoldAct",
            new List<PasValue> { PasValue.FromInt(1) }, out _),
        "GoldAct NPC method accepted a non-Player argument");
    Assert(!bridge.CallNpcMethod("ReqItemByGoldAct",
            new List<PasValue>
            {
                PasValue.FromObject(explicitPlayer), PasValue.FromInt(1)
            }, out _),
        "GoldAct NPC method accepted an extra argument");
}

void CheckGoldActCredentialLifecycle()
{
    var useItems = typeof(TPlayObject).GetMethod("ClientUseItems",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    Assert(useItems != null, "native ClientUseItems entry is missing");

    var previousEngine = M2Share.UserEngine;
    try
    {
        var engine = new UserEngine();
        engine.StdItemList.Add(new GoodItem
        {
            Name = "热血凭证",
            StdMode = 1,
            Shape = 28,
            DuraMax = 1
        });
        M2Share.UserEngine = engine;

        var player = NewOfflinePlayer();
        player.m_ItemList.Add(Credential(71001));
        useItems!.Invoke(player, new object[] { 71001, 0 });
        Equal((byte)1, player.m_btGoldActNextLevel,
            "first credential use did not activate GoldAct");
        Equal(0, player.m_ItemList.Count,
            "first credential use did not consume the item");
        Assert(player.m_MsgList.Any(message =>
                message.Buff == "本角色成功升级为热血勇士！"),
            "first credential GBK message is missing");
        AssertFixedNativeColor(player.m_MsgList.Last(message =>
                message.Buff == "本角色成功升级为热血勇士！"),
            "first credential message");

        player.m_ItemList.Add(Credential(71002));
        useItems.Invoke(player, new object[] { 71002, 0 });
        Equal((byte)1, player.m_btGoldActNextLevel,
            "repeat credential use changed GoldAct state");
        Equal(1, player.m_ItemList.Count,
            "repeat credential use consumed the item");
        Assert(player.m_MsgList.Any(message =>
                message.Buff == "你已经是热血勇士"),
            "repeat credential GBK message is missing");
        AssertFixedNativeColor(player.m_MsgList.Last(message =>
                message.Buff == "你已经是热血勇士"),
            "repeat credential message");

        var dead = NewOfflinePlayer();
        dead.m_boDeath = true;
        dead.m_ItemList.Add(Credential(71003));
        useItems.Invoke(dead, new object[] { 71003, 0 });
        Equal((byte)0, dead.m_btGoldActNextLevel,
            "dead player activated GoldAct");
        Equal(1, dead.m_ItemList.Count,
            "dead player consumed the credential");
    }
    finally
    {
        M2Share.UserEngine = previousEngine;
    }

    static TPlayObject NewOfflinePlayer() => new()
    {
        m_boOffLineFlag = true,
        m_boCanUseItem = true
    };

    static TUserItem Credential(int makeIndex) => new()
    {
        MakeIndex = makeIndex,
        wIndex = 1,
        Dura = 1,
        DuraMax = 1
    };
}

void CheckGoldActRealGrantContract()
{
    var grant = typeof(TPlayObject).GetMethod(
        "TryGrantNativeGoldActReward",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    Assert(grant != null, "native GoldAct grant entry is missing");

    var previousEngine = M2Share.UserEngine;
    var previousRewards = M2Share.GoldActRewards;
    var previousRandom = M2Share.RandomNumber;
    var previousLogs = M2Share.LogStringList;
    var previousLogLock = M2Share.LogMsgCriticalSection;
    var previousMessageLock = M2Share.ProcessMsgCriticalSection;
    var tempDirectory = Path.Combine(Path.GetTempPath(),
        "gold-act-grant-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    try
    {
        var gbk = Encoding.GetEncoding(936,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var configPath = Path.Combine(tempDirectory, "NewGoldID.ini");
        File.WriteAllText(configPath,
            "[配置1]\r\n奖励1=规范奖励\r\n", gbk);

        var engine = new UserEngine();
        engine.StdItemList.Add(new GoodItem
        {
            Name = "规范奖励",
            StdMode = 5,
            Shape = 1,
            DuraMax = 100
        });
        M2Share.UserEngine = engine;
        M2Share.GoldActRewards = new GoldActRewardLoader(configPath);
        M2Share.RandomNumber = RandomNumber.GetInstance();
        M2Share.LogStringList = new System.Collections.ArrayList();
        M2Share.LogMsgCriticalSection = new object();
        M2Share.ProcessMsgCriticalSection = new object();

        var player = new TPlayObject
        {
            m_boOffLineFlag = true,
            m_sMapName = "比奇",
            m_sCharName = "审计角色",
            m_nCurrX = 123,
            m_nCurrY = 234
        };
        var ok = (bool)grant!.Invoke(player, new object[] { 46 });
        Assert(ok, "native GoldAct reward did not grant a valid configured item");
        Equal(1, player.m_ItemList.Count,
            "native GoldAct reward did not add exactly one bag item");
        var item = player.m_ItemList[0];
        Equal((ushort)1, item.wIndex, "native GoldAct reward item index");

        var successMessage = player.m_MsgList.Last(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE &&
            message.Buff == "恭喜: 你领取到规范奖励");
        AssertFixedNativeColor(successMessage, "GoldAct reward success message");

        var log = M2Share.LogStringList.Cast<object>()
            .OfType<string>()
            .LastOrDefault(value => value.StartsWith("55\t",
                StringComparison.Ordinal));
        Assert(log != null, "native GoldAct reward log was not emitted");
        var fields = log!.Split('\t');
        Equal(9, fields.Length, "native GoldAct reward log field count");
        SequenceEqual(new[]
        {
            "55", "比奇", "123", "234", "审计角色", "规范奖励",
            item.MakeIndex.ToString(), "1", "热血勇士领取"
        }, fields, "native GoldAct reward log fields");

        var fullBag = new TPlayObject
        {
            m_boOffLineFlag = true,
            m_sMapName = "比奇",
            m_sCharName = "满包角色",
            m_btGoldActNextLevel = 46
        };
        fullBag.m_Abil.Level = 46;
        while (fullBag.m_ItemList.Count < Grobal2.MAXBAGITEM)
            fullBag.m_ItemList.Add(new TUserItem());
        M2Share.LogStringList.Clear();
        var failed = (bool)grant.Invoke(fullBag, new object[] { 46 });
        Assert(!failed, "native GoldAct reward ignored a full bag");
        Equal(Grobal2.MAXBAGITEM, fullBag.m_ItemList.Count,
            "full-bag GoldAct grant changed bag size");
        Assert(!fullBag.m_MsgList.Any(message =>
                message.wIdent == Grobal2.RM_SYSMESSAGE &&
                message.Buff?.StartsWith("恭喜: 你领取到",
                    StringComparison.Ordinal) == true),
            "full-bag GoldAct grant emitted a success message");
        Equal(0, M2Share.LogStringList.Count,
            "full-bag GoldAct grant emitted an audit log");

        var stateMachine = typeof(TPlayObject).GetMethod(
            "RunNativeGoldActRewardStateMachine",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert(stateMachine != null,
            "native GoldAct reward state machine is missing");
        var dialog = (string)stateMachine!.Invoke(fullBag, new object[]
        {
            (Func<int, bool>)(level =>
                (bool)grant.Invoke(fullBag, new object[] { level }))
        })!;
        Equal((byte)46, fullBag.m_btGoldActNextLevel,
            "full-bag GoldAct state advanced after failed grant");
        Equal("祝贺您，您已经获得了相应等级的奖励物品，请查看包裹吧\\如果没有找到的话，请留出足够的包裹空间，再次领取",
            dialog,
            "full-bag GoldAct final dialog changed after failed grant");
    }
    finally
    {
        M2Share.UserEngine = previousEngine;
        M2Share.GoldActRewards = previousRewards;
        M2Share.RandomNumber = previousRandom;
        M2Share.LogStringList = previousLogs;
        M2Share.LogMsgCriticalSection = previousLogLock;
        M2Share.ProcessMsgCriticalSection = previousMessageLock;
        Directory.Delete(tempDirectory, recursive: true);
    }
}

void CheckGoldActGrantContract()
{
    var source = Read("GameSvr", "Players", "TPlayObject.NativeGoldGift.cs");
    Require(source, "var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex)",
        "GoldAct grant does not resolve the canonical StdItem");
    Require(source, "var canonicalItemName = stdItem.Name",
        "GoldAct grant logs the configured alias instead of the canonical item name");
    Require(source,
        "SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0,",
        "GoldAct success message does not use native color 0x38FF");
    Require(source, "\"恭喜: 你领取到\" + canonicalItemName",
        "GoldAct per-item success hint is missing");
    Require(source, "string.Join('\\t', 55",
        "GoldAct audit log type is not native type 55");
    Require(source, "\"热血勇士领取\"",
        "GoldAct audit log reason is not native text");
}

void CheckNativeRewardStateRoundTrip()
{
    const int goldActOffset = 0x01D8;
    const int firstGiftOffset = 0x01D9;
    const byte originalGoldAct = 46;
    const byte originalFirstGift = 1;
    const byte updatedGoldAct = 55;
    const byte updatedFirstGift = 2;

    var blob = new byte[NativeHumanDataCodec.DataRecordSize + 8];
    BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4, 4),
        NativeHumanDataCodec.DataRecordSize);
    var raw = blob.AsSpan(8);
    raw[0x3E] = 1;
    raw[goldActOffset] = originalGoldAct;
    raw[firstGiftOffset] = originalFirstGift;
    raw[0x01DA] = 0xA5;

    Assert(NativeHumanDataCodec.TryDecode(blob, null, out var decoded,
            out var error), "native reward-state decode failed: " + error);
    Equal(originalGoldAct, decoded.Data.btGoldActNextLevel,
        "native GoldActNextLevel decode");
    Equal(originalFirstGift, decoded.Data.btFirstUsedGiftStage,
        "native FirstUsedGiftStage decode");

    decoded.Data.btGoldActNextLevel = updatedGoldAct;
    decoded.Data.btFirstUsedGiftStage = updatedFirstGift;
    Assert(NativeHumanDataCodec.TryEncode(decoded, out var encoded,
            out var script, out error),
        "native reward-state encode failed: " + error);
    Assert(NativeHumanDataCodec.TryDecode(encoded, script, out var roundTrip,
            out error), "native reward-state round trip failed: " + error);
    Equal(updatedGoldAct, roundTrip.Data.btGoldActNextLevel,
        "native GoldActNextLevel round trip");
    Equal(updatedFirstGift, roundTrip.Data.btFirstUsedGiftStage,
        "native FirstUsedGiftStage round trip");
    Equal((byte)0xA5, roundTrip.NativeData[0x01DA],
        "native adjacent field preservation");

    roundTrip.PrepareForTransport();
    using var stream = new MemoryStream();
    Serializer.Serialize(stream, roundTrip);
    stream.Position = 0;
    var transported = Serializer.Deserialize<THumDataInfo>(stream);
    transported.RestoreAfterTransport();
    Equal(updatedGoldAct, transported.Data.btGoldActNextLevel,
        "protobuf GoldActNextLevel round trip");
    Equal(updatedFirstGift, transported.Data.btFirstUsedGiftStage,
        "protobuf FirstUsedGiftStage round trip");
}

void CheckRewardStateWiring()
{
    var codec = Read("DBSvr", "Core", "NativeHumanDataCodec.cs");
    Require(codec, "GoldActNextLevelOffset = 0x01D8",
        "native GoldActNextLevel offset is not +0x1D8");
    Require(codec, "FirstUsedGiftStageOffset = 0x01D9",
        "native FirstUsedGiftStage offset is not +0x1D9");

    var player = Read("GameSvr", "Players", "TPlayObject.Base.cs");
    Require(player, "m_btGoldActNextLevel",
        "player GoldActNextLevel runtime field is missing");
    Require(player, "m_btFirstUsedGiftStage",
        "player FirstUsedGiftStage runtime field is missing");

    var loader = Read("GameSvr", "UsrSystem", "UsrEngn.cs");
    Require(loader,
        "m_btGoldActNextLevel = HumData.btGoldActNextLevel",
        "login does not load GoldActNextLevel");
    Require(loader,
        "m_btFirstUsedGiftStage = HumData.btFirstUsedGiftStage",
        "login does not load FirstUsedGiftStage");

    var saver = Read("GameSvr", "Players", "TPlayObject.cs");
    Require(saver,
        "HumData.btGoldActNextLevel = m_btGoldActNextLevel",
        "save does not persist GoldActNextLevel");
    Require(saver,
        "HumData.btFirstUsedGiftStage = m_btFirstUsedGiftStage",
        "save does not persist FirstUsedGiftStage");
}

void CheckNativeGbkRewardConfigs()
{
    var tempDirectory = Path.Combine(Path.GetTempPath(),
        "gold-reward-config-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(tempDirectory, "Config"));
    try
    {
        var gbk = Encoding.GetEncoding(936,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var goldIdFile = Path.Combine(tempDirectory, "GoldID.ini");
        File.WriteAllText(goldIdFile,
            "[配置1]\r\n奖励1=炼狱\r\n奖励2=\r\n奖励3=银蛇\r\n" +
            "[分类2]\r\n奖励1=错误分类\r\n" +
            "[配置3]\r\n奖励1=\r\n奖励2=不应加载\r\n" +
            "[配置9]\r\n奖励1=真魂项链\r\n奖励101=越界物品\r\n", gbk);
        var goldId = new GoldIDLoader(goldIdFile);
        Equal(9, goldId.Pools.Count, "GoldID pool count");
        SequenceEqual(new[] { "炼狱" }, goldId.Pools[1],
            "GoldID must stop a pool at the first empty reward");
        Equal(0, goldId.Pools[2].Count,
            "GoldID must ignore the old incorrect 分类 section");
        Equal(0, goldId.Pools[3].Count,
            "GoldID must not read later rewards when the first reward is empty");
        SequenceEqual(new[] { "真魂项链" }, goldId.Pools[9],
            "GoldID reward100 boundary");

        var newGoldIdFile = Path.Combine(tempDirectory, "Config", "NewGoldID.ini");
        File.WriteAllText(newGoldIdFile,
            "[配置1]\r\n奖励1=天魔神甲\r\n" +
            "[配置10]\r\n奖励1=天之屠龙\r\n奖励3=天之逍遥扇\r\n", gbk);
        var goldAct = new GoldActRewardLoader(newGoldIdFile);
        Equal(10, goldAct.Pools.Count, "NewGoldID pool count");
        SequenceEqual(new[] { "天魔神甲" }, goldAct.Pools[1],
            "NewGoldID level46 pool");
        SequenceEqual(new[] { "天之屠龙", "天之逍遥扇" }, goldAct.Pools[10],
            "NewGoldID level55 ordered nonempty rewards");

        var app = Read("GameSvr", "GameApp.cs");
        Require(app, "Path.Combine(nativeShareDirectory, \"GoldID.ini\")",
            "GameApp does not load Share/GoldID.ini");
        Require(app,
            "Path.Combine(nativeShareDirectory, \"Config\", \"NewGoldID.ini\")",
            "GameApp does not load Share/Config/NewGoldID.ini");
        foreach (var forbidden in new[]
                 {
                     "YBData.json", "YBShopScript.json", "UserData.dat",
                     "Market_Saved", "Market_Prices", "tbl_"
                 })
            if (app.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                Fail("native reward config wiring uses forbidden storage: " + forbidden);
    }
    finally
    {
        Directory.Delete(tempDirectory, recursive: true);
    }
}

static void SequenceEqual<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string message)
{
    if (!expected.SequenceEqual(actual))
        Fail($"{message}: expected=[{string.Join(',', expected)}], " +
             $"actual=[{string.Join(',', actual)}]");
}

static void AssertFixedNativeColor(SendMessage message, string label)
{
    Equal(Grobal2.RM_SYSMESSAGE, message.wIdent, label + " ident");
    Equal(0, message.wParam, label + " wParam");
    Equal(0xFF, message.nParam1, label + " foreground");
    Equal(0x38, message.nParam2, label + " background");
    Equal(0, message.nParam3, label + " nParam3");
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

static void RequireMatches(string source, string pattern, int expected, string message)
{
    var actual = Regex.Matches(source, pattern,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Count;
    Equal(expected, actual, message);
}

static void AssertNoSubstitute(string source, string startMarker, string endMarker)
{
    var start = 0;
    while ((start = source.IndexOf(startMarker, start, StringComparison.Ordinal)) >= 0)
    {
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0) Fail($"missing end marker after {startMarker}");
        var region = source.Substring(start, end - start);
        foreach (var forbidden in new[]
                 {
                     "GetPlayerVar", "SetPlayerVar", "m_ScriptVVars", "m_ScriptSVars",
                     "m_nGameGold +=", "m_nGameGold -=", "m_nGamePoint +=", "m_nGamePoint -="
                 })
        {
            if (region.Contains(forbidden, StringComparison.Ordinal))
                Fail($"{startMarker} uses non-native substitute: {forbidden}");
        }
        start = end;
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
    throw new DirectoryNotFoundException("Repository root containing GameSvr/GameSvr.csproj was not found.");
}

string Read(params string[] relativeParts)
{
    var path = relativeParts.Aggregate(root, Path.Combine);
    if (!File.Exists(path))
        throw new FileNotFoundException("required source is missing: " + path, path);
    return File.ReadAllText(path);
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal)) Fail(message);
}

static void Assert(bool condition, string message)
{
    if (!condition) Fail(message);
}

static void Equal<T>(T expected, T actual, string message)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        Fail($"{message}: expected {expected}, actual {actual}");
}

static void Fail(string message) => throw new InvalidOperationException(message);
