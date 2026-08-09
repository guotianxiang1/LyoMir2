using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();

SetDefinitions(
    new GoodItem { Name = "宝藏钥匙", StdMode = 0, Looks = 501 },
    new GoodItem { Name = "宝藏钥匙", StdMode = 0, Looks = 502 },
    new GoodItem
    {
        Name = "宝藏钥匙", StdMode = 7, Looks = 503, DuraMax = 100
    });

var playerType = typeof(TPlayObject);
var open = RequiredMethod("TryOpenNativeNeedKeyBox");
var select = RequiredMethod("TrySelectNativeNeedKeyBox");
var claim = RequiredMethod("TryClaimNativeNeedKeyBox");
var beginYuanbao = RequiredMethod("TryBeginNativeNeedKeyBoxYuanbao");
var completeYuanbao = RequiredMethod(
    "TryCompleteNativeNeedKeyBoxYuanbaoSuccess");
var failYuanbao = RequiredMethod("CompleteNativeNeedKeyBoxYuanbaoFailure");
var clear = RequiredMethod("ClearNativeNeedKeyBoxState");
var sendOpenPacket = RequiredMethod("SendNativeNeedKeyBoxOpenPacket");
var clientClaim = RequiredMethod("ClientNativeNeedKeyBoxClaimPrize");
var takeByName = RequiredMethod("TryTakeNativeNeedKeyBoxItemByName");
var nextRare = RequiredMethod("NextNativeNeedKeyBoxRareKind");
var setOtherPending = RequiredMethod(
    "SetNativeNeedKeyBoxOtherPendingRewardPredicate");
var rollRepeat = playerType.GetMethod("RollNativeNeedKeyBoxRepeat",
    BindingFlags.Static | BindingFlags.NonPublic);
var validateYuanbaoTuple = playerType.GetMethod(
    "IsNativeNeedKeyBoxYuanbaoTuple",
    BindingFlags.Static | BindingFlags.NonPublic);
var randomField = RequiredField("_nativeNeedKeyBoxRandom");
var repeatField = RequiredField("_nativeNeedKeyBoxRepeatEligible");
var pendingField = RequiredField("_nativeNeedKeyBoxYuanbaoPending");
var defaultRewardField = RequiredField("_nativeNeedKeyBoxDefaultReward");
var rareModeField = RequiredField("_nativeCattleNeedKeyBoxMode");
var personalCounter = RequiredField(
    "_nativeNeedKeyBoxPersonalRareCounter");
var globalCounter = playerType.GetField(
    "s_nativeNeedKeyBoxGlobalRareCounter",
    BindingFlags.Static | BindingFlags.NonPublic);
Assert(rollRepeat != null, "repeat roll helper is missing");
Assert(globalCounter != null, "global rare counter is missing");
Assert(validateYuanbaoTuple != null,
    "NeedKeyBox YB tuple validator is missing");
Assert((bool)validateYuanbaoTuple.Invoke(null,
        new object[] { 125, 10000, 0, 0, 1 })!,
    "exact NeedKeyBox YB tuple was rejected");
foreach (var invalidTuple in new[]
         {
             new[] { 124, 10000, 0, 0, 1 },
             new[] { 125, 9999, 0, 0, 1 },
             new[] { 125, 10000, 1, 0, 1 },
             new[] { 125, 10000, 0, 1, 1 },
             new[] { 125, 10000, 0, 0, 2 }
         })
{
    Assert(!(bool)validateYuanbaoTuple.Invoke(null,
            invalidTuple.Cast<object>().ToArray())!,
        "NeedKeyBox YB tuple accepted a mismatched field");
}

var configPath = Path.Combine(AppContext.BaseDirectory,
    "NeedKeyBoxCoreCompat.ini");
File.WriteAllText(configPath, BuildConfig(), Encoding.GetEncoding(936));
var initializeConfig = playerType.GetMethod(
    "InitializeNativeNeedKeyBoxConfigFromPath",
    BindingFlags.Static | BindingFlags.NonPublic);
Assert(initializeConfig != null,
    "NeedKeyBox configuration initializer is missing");
var caseConfigPath = Path.Combine(AppContext.BaseDirectory,
    "NeedKeyBoxCaseCompat.ini");
File.WriteAllText(caseConfigPath, BuildConfig()
    .Replace("[Setup]", "[setup]", StringComparison.Ordinal)
    .Replace("ValuedItem=", "valueditem=", StringComparison.Ordinal),
    Encoding.GetEncoding(936));
Assert((bool)initializeConfig.Invoke(null, new object[] { caseConfigPath })!,
    "NeedKeyBox INI lookup became case-sensitive");
Assert((bool)initializeConfig.Invoke(null, new object[] { configPath })!,
    "NeedKeyBox configuration did not load after item definitions");

var bridgeSentinel = NewPlayer(40, 1);
var bridge = new PasApiBridge
{
    CurrentPlayer = bridgeSentinel,
    CurrentNpc = new NormNpc()
};
var bridgePlayer = NewPlayer(40, 1);
bridgePlayer.m_ItemList.Add(NewItem(90, 1, 1));
var bridgeArgs = new List<PasValue>
    { PasValue.FromObject(bridgePlayer) };
Assert(!bridge.CallNpcFunc("OpenNeedKeyBox", bridgeArgs,
        out var bridgeFunctionResult) &&
       bridgeFunctionResult.Equals(PasValue.Nil),
    "OpenNeedKeyBox function shadow was not fail-closed");
Assert(bridge.CallNpcMethod("OpenNeedKeyBox", bridgeArgs,
        out var bridgeMethodResult) &&
       bridgeMethodResult.Equals(PasValue.Nil),
    "OpenNeedKeyBox procedure was not dispatched");
Equal(0, bridgePlayer.m_ItemList.Count,
    "OpenNeedKeyBox did not use the explicit player argument");
Equal(950, bridgePlayer.m_DefMsg.Ident,
    "OpenNeedKeyBox procedure response");
Assert(bridgeSentinel.m_DefMsg == null,
    "OpenNeedKeyBox mutated CurrentPlayer instead of its argument");

var bridgeYuanbaoPlayer = NewPlayer(40, 10000);
var bridgeYuanbaoArgs = new List<PasValue>
    { PasValue.FromObject(bridgeYuanbaoPlayer) };
Assert(!bridge.CallNpcFunc("OpenNeedKeyBox2", bridgeYuanbaoArgs,
        out bridgeFunctionResult) &&
       bridgeFunctionResult.Equals(PasValue.Nil),
    "OpenNeedKeyBox2 function shadow was not fail-closed");
Assert(bridge.CallNpcMethod("OpenNeedKeyBox2", bridgeYuanbaoArgs,
        out bridgeMethodResult) && bridgeMethodResult.Equals(PasValue.Nil),
    "OpenNeedKeyBox2 procedure was not dispatched");
Assert(!(bool)pendingField.GetValue(bridgeYuanbaoPlayer)!,
    "OpenNeedKeyBox2 set pending after backend enqueue failure");
Assert(bridgeSentinel.m_DefMsg == null,
    "OpenNeedKeyBox2 mutated CurrentPlayer instead of its argument");

var cattleField = playerType.GetField("m_NativeCattle",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
Assert(cattleField != null, "native cattle state is missing");
var cattleBlocked = NewPlayer(40, 1);
randomField.SetValue(cattleBlocked, new Func<int, int>(_ => 0));
cattleBlocked.m_ItemList.Add(NewItem(99, 1, 1));
var cattle = cattleField.GetValue(cattleBlocked)!;
var cattleType = cattle.GetType();
var revealPending = cattleType.GetField("_revealPending",
    BindingFlags.Instance | BindingFlags.NonPublic);
var claimPending = cattleType.GetField("_claimPending",
    BindingFlags.Instance | BindingFlags.NonPublic);
var cattleMode = cattleType.GetProperty("PrizeMode",
    BindingFlags.Instance | BindingFlags.NonPublic);
Assert(revealPending != null && claimPending != null && cattleMode != null,
    "native cattle pending/mode bridge is missing");
revealPending.SetValue(cattle, new byte[] { 1 });
cattleMode.SetValue(cattle, (byte)3);
EqualText("Busy", open.Invoke(cattleBlocked,
        new object[] { true, null })?.ToString(),
    "cattle reveal-pending did not block NeedKeyBox");
Equal(3, Convert.ToInt32(rareModeField.GetValue(cattleBlocked)),
    "blocked NeedKeyBox overwrote shared cattle mode");
revealPending.SetValue(cattle, Array.Empty<byte>());
claimPending.SetValue(cattle, new byte[] { 1 });
EqualText("Busy", open.Invoke(cattleBlocked,
        new object[] { true, null })?.ToString(),
    "cattle claim-pending did not block NeedKeyBox");
Equal(1, cattleBlocked.m_ItemList.Count,
    "cattle pending consumed the NeedKeyBox key");

var packetPlayer = NewPlayer(40, 1);
Assert(!(bool)sendOpenPacket.Invoke(packetPlayer,
        new object[] { new byte[215] })!,
    "SM950 accepted a non-216-byte body");
Assert((bool)sendOpenPacket.Invoke(packetPlayer,
        new object[] { new byte[216] })!,
    "SM950 rejected its fixed 216-byte body");
Equal(950, packetPlayer.m_DefMsg.Ident, "SM950 ident");
Equal(0, packetPlayer.m_DefMsg.Recog, "SM950 Recog");
Equal(0, packetPlayer.m_DefMsg.Param, "SM950 Param");
Equal(0, packetPlayer.m_DefMsg.Tag, "SM950 Tag");
Equal(0, packetPlayer.m_DefMsg.Series, "SM950 Series");

var claimPacketPlayer = NewPlayer(40, 1);
clientClaim.Invoke(claimPacketPlayer, null);
Equal(953, claimPacketPlayer.m_DefMsg.Ident, "SM953 ident");
Equal(2, claimPacketPlayer.m_DefMsg.Recog, "SM953 no-reward Recog");
Equal(1, claimPacketPlayer.m_DefMsg.Param, "SM953 Param");
Equal(0, claimPacketPlayer.m_DefMsg.Tag, "SM953 Tag");
Equal(0, claimPacketPlayer.m_DefMsg.Series, "SM953 Series");

var player = NewPlayer(40, 1);
randomField.SetValue(player, new Func<int, int>(_ => 0));
player.m_ItemList.Add(NewItem(100, 1, 1));

var openArguments = new object[] { false, null };
EqualText("Opened", open.Invoke(player, openArguments)?.ToString(),
    "normal open result");
var body = (byte[])openArguments[1];
Equal(216, body.Length, "open body size");
Equal(0, player.m_ItemList.Count, "key commit count");
Equal(4, body[0], "slot zero GBK name length");
Equal(0, body[15], "slot zero short-name tail byte");
Equal(1186, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(16, 4)),
    "slot zero looks");
Equal(1000, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(20, 4)),
    "slot zero amount");
Equal(11000, BinaryPrimitives.ReadInt32LittleEndian(
        body.AsSpan(8 * 24 + 20, 4)),
    "default slot amount");
Equal(950, player.m_DefMsg.Ident, "normal open SM950 ident");

var selectArguments = new object[] { 0 };
Assert((bool)select.Invoke(player, selectArguments)!,
    "first select did not consume the default reward");
Equal(1, (int)selectArguments[0], "selected one-based slot");
Assert(!(bool)select.Invoke(player, new object[] { 0 })!,
    "second select was not ignored");

byte[] granted = null;
player.m_NPC = new TBaseObject();
var claimResult = claim.Invoke(player, new object[]
{
    new Func<byte[], bool>(descriptor =>
    {
        granted = descriptor;
        return true;
    })
});
EqualText("Success", claimResult?.ToString(), "normal claim result");
EqualByteSequence(Encoding.GetEncoding(936).GetBytes("经验:1000"), granted,
    "selected raw reward descriptor");
Equal(1, (int)RequiredField("_nativeNeedKeyBoxSelectedSlot")
    .GetValue(player)!, "claim cleared native selected slot");
Assert(((byte[])RequiredField("_nativeNeedKeyBoxWireBody")
        .GetValue(player)!).Any(value => value != 0),
    "claim cleared native wire state");
Assert((bool)repeatField.GetValue(player)!,
    "claim-time script context did not set repeat eligibility");

var staleContext = NewPlayer(40, 1);
randomField.SetValue(staleContext, new Func<int, int>(_ => 0));
staleContext.m_ItemList.Add(NewItem(101, 1, 1));
var staleOpen = new object[] { true, null };
EqualText("Opened", open.Invoke(staleContext, staleOpen)?.ToString(),
    "stale-context setup open result");
Assert((bool)select.Invoke(staleContext, new object[] { 0 })!,
    "stale-context select result");
EqualText("Success", claim.Invoke(staleContext, new object[]
    {
        new Func<byte[], bool>(_ => true)
    })?.ToString(), "stale-context claim result");
Assert(!(bool)repeatField.GetValue(staleContext)!,
    "open-time script snapshot incorrectly enabled repeat");

var submittedTuple = Array.Empty<int>();
var beginResult = beginYuanbao.Invoke(player, new object[]
{
    true,
    new Func<int, int, int, int, int, bool>((ident, selector, p1, p2,
        amount) =>
    {
        submittedTuple = new[] { ident, selector, p1, p2, amount };
        return true;
    })
});
EqualText("Submitted", beginResult?.ToString(), "YB submit result");
EqualSequence(new[] { 125, 10000, 0, 0, 1 }, submittedTuple,
    "YB tuple");
Assert(!(bool)repeatField.GetValue(player)!,
    "accepted YB request retained repeat eligibility");
Assert((bool)pendingField.GetValue(player)!,
    "accepted YB request did not set pending");

failYuanbao.Invoke(player, null);
Assert(!(bool)repeatField.GetValue(player)! &&
       (bool)pendingField.GetValue(player)!,
    "YB failure did not preserve native F44=0/F45=1 state");

var completionArguments = new object[] { null };
Assert((bool)completeYuanbao.Invoke(player, completionArguments)!,
    "successful YB response did not rebuild reward state");
Equal(216, ((byte[])completionArguments[0]).Length,
    "paid open body size");
Equal(950, player.m_DefMsg.Ident, "paid open SM950 ident");
Assert((bool)pendingField.GetValue(player)!,
    "paid open success cleared pending before claim");

claimResult = claim.Invoke(player, new object[]
{
    new Func<byte[], bool>(_ => true)
});
EqualText("Success", claimResult?.ToString(), "paid claim result");
Assert(!(bool)repeatField.GetValue(player)! &&
       !(bool)pendingField.GetValue(player)!,
    "paid claim did not clear repeat and pending together");

var zeroBalance = NewPlayer(40, 0);
randomField.SetValue(zeroBalance, new Func<int, int>(_ => 0));
setOtherPending.Invoke(zeroBalance,
    new object[] { new Func<bool>(() => false) });
zeroBalance.m_ItemList.Add(NewItem(104, 1, 1));
var zeroBalanceOpen = new object[] { true, null };
EqualText("Opened", open.Invoke(zeroBalance, zeroBalanceOpen)?.ToString(),
    "zero-balance setup open result");
repeatField.SetValue(zeroBalance, false);
pendingField.SetValue(zeroBalance, true);
claimResult = claim.Invoke(zeroBalance, new object[]
{
    new Func<byte[], bool>(_ => true)
});
EqualText("Success", claimResult?.ToString(),
    "zero-balance paid claim result");
Assert(!(bool)repeatField.GetValue(zeroBalance)! &&
       (bool)pendingField.GetValue(zeroBalance)!,
    "zero-balance paid claim did not preserve native F44=0/F45=1 state");

repeatField.SetValue(player, true);
var rejected = beginYuanbao.Invoke(player, new object[]
{
    true,
    new Func<int, int, int, int, int, bool>((_, _, _, _, _) => false)
});
EqualText("EnqueueFailed", rejected?.ToString(),
    "rejected YB enqueue result");
Assert((bool)repeatField.GetValue(player)! &&
       !(bool)pendingField.GetValue(player)!,
    "rejected YB enqueue mutated state");

var rollback = NewPlayer(40, 1);
rollback.m_ItemList.Add(NewItem(200, 1, 1));
Assert(!(bool)takeByName.Invoke(rollback,
        new object[] { "宝藏钥匙", 2 })!,
    "insufficient name transaction succeeded");
Equal(1, rollback.m_ItemList.Count,
    "insufficient name transaction changed the bag");

var crossIndex = NewPlayer(40, 1);
crossIndex.m_ItemList.Add(NewItem(201, 1, 1));
crossIndex.m_ItemList.Add(NewItem(202, 2, 1));
Assert((bool)takeByName.Invoke(crossIndex,
        new object[] { "宝藏钥匙", 2 })!,
    "same-name cross-index transaction failed");
Equal(0, crossIndex.m_ItemList.Count,
    "same-name cross-index transaction count");

var pile = NewPlayer(40, 1);
var pileItem = NewItem(203, 3, 3);
pile.m_ItemList.Add(pileItem);
var pileLogCount = M2Share.LogStringList.Count;
Assert((bool)takeByName.Invoke(pile,
        new object[] { "宝藏钥匙", 1 })!,
    "pile transaction failed");
Equal(2, pileItem.Dura, "pile partial durability");
Equal(1, pile.m_ItemList.Count, "pile partial removed the item");
Equal(pileLogCount + 1, M2Share.LogStringList.Count,
    "pile partial did not write native take log");

globalCounter.SetValue(null, 1999);
personalCounter.SetValue(player, 99);
Equal(2, (int)nextRare.Invoke(player, null)!,
    "global 2000 rare kind must win");
globalCounter.SetValue(null, 0);
personalCounter.SetValue(player, 99);
Equal(1, (int)nextRare.Invoke(player, null)!,
    "personal 100 rare kind");
globalCounter.SetValue(null, 0);
personalCounter.SetValue(player, 0);
Equal(0, (int)nextRare.Invoke(player, null)!, "ordinary rare kind");

AssertRepeat(39, 0, false);
AssertRepeat(40, 89, true);
AssertRepeat(40, 90, false);
AssertRepeat(47, 49, true);
AssertRepeat(47, 50, false);
AssertRepeat(56, 29, true);
AssertRepeat(56, 30, false);
AssertRepeat(60, 9, true);
AssertRepeat(60, 10, false);

clear.Invoke(player, null);
Assert(!(bool)repeatField.GetValue(player)! &&
       !(bool)pendingField.GetValue(player)!,
    "full cleanup retained repeat/YB state");
Equal(1, (int)personalCounter.GetValue(player)!,
    "full cleanup reset the native personal counter");

globalCounter.SetValue(null, 0);
var tracePlayer = NewPlayer(40, 1);
var trace = new List<int>();
randomField.SetValue(tracePlayer, new Func<int, int>(range =>
{
    trace.Add(range);
    return 0;
}));
setOtherPending.Invoke(tracePlayer,
    new object[] { new Func<bool>(() => false) });
tracePlayer.m_ItemList.Add(NewItem(204, 1, 1));
EqualText("Opened", open.Invoke(tracePlayer,
        new object[] { true, null })?.ToString(),
    "RNG trace open result");
EqualSequence(new[]
    {
        1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000,
        8, 7, 6, 5, 4, 3, 2, 1, 1000
    }, trace.ToArray(), "non-empty pool RNG sequence");

var emptyPoolPath = Path.Combine(AppContext.BaseDirectory,
    "NeedKeyBoxEmptyPoolCompat.ini");
File.WriteAllText(emptyPoolPath, BuildConfig(emptyPool: 1),
    Encoding.GetEncoding(936));
Assert((bool)initializeConfig.Invoke(null, new object[] { emptyPoolPath })!,
    "NeedKeyBox empty-pool configuration did not load");
globalCounter.SetValue(null, 0);
var emptyTracePlayer = NewPlayer(40, 1);
var emptyTrace = new List<int>();
randomField.SetValue(emptyTracePlayer, new Func<int, int>(range =>
{
    emptyTrace.Add(range);
    return 0;
}));
setOtherPending.Invoke(emptyTracePlayer,
    new object[] { new Func<bool>(() => false) });
emptyTracePlayer.m_ItemList.Add(NewItem(205, 1, 1));
EqualText("Opened", open.Invoke(emptyTracePlayer,
        new object[] { true, null })?.ToString(),
    "empty-pool open result");
EqualSequence(new[]
    {
        1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000,
        7, 6, 5, 4, 3, 2, 1, 1000
    }, emptyTrace.ToArray(), "empty-pool RNG sequence");

const string boundaryName = "ABCDEFGHIJKLMNOPQRS中";
M2Share.UserEngine.StdItemList.Add(new GoodItem
{
    Name = boundaryName,
    StdMode = 0,
    Looks = 777
});
var boundaryPath = Path.Combine(AppContext.BaseDirectory,
    "NeedKeyBoxBoundaryCompat.ini");
File.WriteAllText(boundaryPath,
    BuildConfig(rewardName: boundaryName, rewardAmount: 7),
    Encoding.GetEncoding(936));
Assert((bool)initializeConfig.Invoke(null, new object[] { boundaryPath })!,
    "NeedKeyBox boundary configuration did not load");
globalCounter.SetValue(null, 1999);
var boundaryPlayer = NewPlayer(40, 1);
randomField.SetValue(boundaryPlayer, new Func<int, int>(_ => 0));
setOtherPending.Invoke(boundaryPlayer,
    new object[] { new Func<bool>(() => false) });
boundaryPlayer.m_ItemList.Add(NewItem(206, 1, 1));
var boundaryOpen = new object[] { true, null };
EqualText("Opened", open.Invoke(boundaryPlayer, boundaryOpen)?.ToString(),
    "boundary open result");
var boundaryBody = (byte[])boundaryOpen[1];
var boundaryBytes = Encoding.GetEncoding(936).GetBytes(boundaryName);
Equal(15, boundaryBody[0], "15-byte name length");
EqualByteSequence(boundaryBytes.AsSpan(0, 15).ToArray(),
    boundaryBody.AsSpan(1, 15).ToArray(), "15-byte name payload");
Equal(777, BinaryPrimitives.ReadInt32LittleEndian(
        boundaryBody.AsSpan(16, 4)), "untruncated-name Looks resolution");
EqualByteSequence(boundaryBytes.AsSpan(0, 20).ToArray(),
    (byte[])defaultRewardField.GetValue(boundaryPlayer)!,
    "20-byte raw descriptor");
Equal(4, Convert.ToInt32(rareModeField.GetValue(boundaryPlayer)),
    "rare-mode field");

var cattleConfigPath = Path.Combine(AppContext.BaseDirectory,
    "NeedKeyBoxSharedCattleMode.ini");
File.WriteAllText(cattleConfigPath, BuildCattleConfig(),
    Encoding.GetEncoding(936));
var initializeCattleConfig = playerType.GetMethod(
    "InitializeNativeCattlePrizeConfigFromPath",
    BindingFlags.Static | BindingFlags.NonPublic);
Assert(initializeCattleConfig != null &&
       (bool)initializeCattleConfig.Invoke(null,
           new object[] { cattleConfigPath })!,
    "shared-mode cattle configuration did not load");
var boundaryCattle = cattleField.GetValue(boundaryPlayer)!;
var createCattlePrize = boundaryCattle.GetType().GetMethod(
    "TryCreatePrizeState", BindingFlags.Instance | BindingFlags.NonPublic);
Assert(createCattlePrize != null,
    "shared-mode cattle state constructor is missing");
var cattleBuildArguments = new object[]
{
    1, new Func<int, int>(_ => 0), null
};
Assert((bool)createCattlePrize.Invoke(boundaryCattle,
        cattleBuildArguments)!,
    "cattle state did not build over a pending NeedKeyBox");
Equal(216, ((byte[])cattleBuildArguments[2]).Length,
    "shared-mode cattle body size");
Equal(3, Convert.ToInt32(rareModeField.GetValue(boundaryPlayer)),
    "cattle state did not overwrite shared NeedKey mode with 3");
byte[] boundaryGranted = null;
EqualText("Success", claim.Invoke(boundaryPlayer, new object[]
    {
        new Func<byte[], bool>(descriptor =>
        {
            boundaryGranted = descriptor;
            return true;
        })
    })?.ToString(), "split-GBK boundary claim result");
EqualByteSequence(boundaryBytes.AsSpan(0, 20).ToArray(), boundaryGranted,
    "claim normalized the raw 20-byte descriptor");

var root = FindRepositoryRoot();
var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeNeedKeyBox.cs"));
Require(source, "internal const int NativeNeedKeyBoxOpenMessage = 950;",
    "local SM_OPENBOX contract");
Require(source, "NativeNeedKeyBoxWireBodySize = 216;",
    "216-byte body contract");
Require(source, "F44=0/F45=1 stays stuck here",
    "native YB failure-stall contract");
Require(source, "cattle.HasRevealPending || cattle.HasClaimPending",
    "shared cattle pending-state gate");
Require(source, "body.Length != NativeNeedKeyBoxWireBodySize",
    "fixed 216-byte SM950 send gate");
Require(source, "m_nGameGold > 0 && m_NPC != null",
    "claim-time script-context gate");
Require(source, "Func<byte[], bool> giveReward",
    "raw NeedKeyBox claim callback");
Require(source, "giveReward?.Invoke(descriptor.ToArray());",
    "raw NeedKeyBox descriptor clone");
Assert(!source.Contains("GetString(descriptor)", StringComparison.Ordinal),
    "NeedKeyBox claim decodes its raw descriptor through Unicode");

var cattleSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeCattle.cs"));
Require(cattleSource, "TryNativeExchangeBookGiveGbk(descriptor)",
    "raw NeedKeyBox Give executor");

var yuanbaoSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeNeedKeyBoxYuanbao.cs"));
Require(yuanbaoSource,
    "NativeNeedKeyBoxHasExactYbDb125Transport = false",
    "local-backend protocol residual marker");
Require(yuanbaoSource,
    "ConcurrentDictionary<long,",
    "account-level NeedKeyBox YB busy guard");
Require(yuanbaoSource, "Interlocked.CompareExchange(",
    "NeedKeyBox YB correlation guard");
Require(yuanbaoSource, "ReferenceEquals(candidate, original)",
    "NeedKeyBox YB original-online-object check");
Require(yuanbaoSource,
    "ReferenceEquals(online.m_NPC, interaction.Npc)",
    "NeedKeyBox YB live script-context check");
Require(yuanbaoSource,
    "online.CompleteNativeNeedKeyBoxYuanbaoFailure();",
    "NeedKeyBox YB failure completion");
Assert(!yuanbaoSource.Contains("online.ClearNativeNeedKeyBoxState",
        StringComparison.Ordinal),
    "NeedKeyBox YB failure clears F44/F45 state");
RequireOrder(yuanbaoSource,
    "if (!TryReleaseNativeNeedKeyBoxYuanbao(submission)) return;",
    "TryCompleteNativeNeedKeyBoxYuanbaoSuccess(out _)",
    "InvokeNativeNeedKeyBoxYuanbaoSuccessCallback(submission, online)",
    "online.IncNativeNickLinFu(",
    "NativeYbShopPurchaseStore.AddConsumptionBestEffort(",
    "online.m_nGameGold = result.Balance;",
    "online.RefreshNativeLingFu();",
    "online.AddNativeYbShopCreditValue2(");

File.Delete(configPath);
File.Delete(caseConfigPath);
File.Delete(emptyPoolPath);
File.Delete(boundaryPath);
initializeCattleConfig.Invoke(null, new object[]
{
    Path.Combine(AppContext.BaseDirectory, "missing-cattle.ini")
});
File.Delete(cattleConfigPath);
Console.WriteLine(
    "PASS NeedKeyBox core body=950/216 transaction=name/rollback " +
    "rng=empty/nonempty raw=15/20 mode=4 repeat=90/50/30/10 " +
    "shared=cattle-pending/mode3 sm953=2/1/0/0 " +
    "yb=125/10000/0/0/1 guard/correlation/local-backend " +
    "zero-credit=F44:0/F45:1 pas=procedure/function-shadow");
return;

void AssertRepeat(int level, int roll, bool expected)
{
    var actual = (bool)rollRepeat.Invoke(null, new object[]
    {
        level, new Func<int, int>(range =>
        {
            Equal(100, range, $"level {level} repeat random range");
            return roll;
        })
    })!;
    Equal(expected ? 1 : 0, actual ? 1 : 0,
        $"level {level} repeat roll {roll}");
}

MethodInfo RequiredMethod(string name)
{
    var method = playerType.GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic);
    if (method == null)
        throw new InvalidOperationException($"missing method: {name}");
    return method;
}

FieldInfo RequiredField(string name)
{
    var field = playerType.GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic);
    if (field == null)
        throw new InvalidOperationException($"missing field: {name}");
    return field;
}

static TPlayObject NewPlayer(ushort level, int gameGold)
{
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_nGameGold = gameGold
    };
    player.m_Abil.Level = level;
    return player;
}

static TUserItem NewItem(int makeIndex, ushort index, ushort dura) => new()
{
    MakeIndex = makeIndex,
    wIndex = index,
    Dura = dura,
    DuraMax = 100,
    btValue = new byte[14]
};

static void SetDefinitions(params GoodItem[] definitions)
{
    M2Share.UserEngine.StdItemList.Clear();
    foreach (var definition in definitions)
        M2Share.UserEngine.StdItemList.Add(definition);
}

static string BuildConfig(int emptyPool = 0, string rewardName = "经验",
    int rewardAmount = 0)
{
    var builder = new StringBuilder();
    builder.AppendLine("[Setup]");
    builder.AppendLine("ValuedItem=宝藏钥匙");
    for (var pool = 1; pool <= 11; pool++)
    {
        builder.AppendLine();
        builder.AppendLine($"[{pool}类奖励]");
        if (pool != emptyPool)
        {
            var amount = rewardAmount == 0 ? pool * 1000 : rewardAmount;
            builder.AppendLine($"奖品1={rewardName}:{amount}/999");
        }
    }
    builder.AppendLine();
    builder.AppendLine("[宝箱1]");
    for (var index = 1; index <= 7; index++)
        builder.AppendLine($"概率{index}=999");
    return builder.ToString();
}

static string BuildCattleConfig()
{
    var builder = new StringBuilder();
    for (var tier = 1; tier <= 4; tier++)
    {
        builder.AppendLine($"[配置{tier}]");
        builder.AppendLine("奖品1=经验:1/9999");
        builder.AppendLine();

        builder.AppendLine($"[个人奖{tier}]");
        builder.AppendLine("奖品1=经验:1/9999");
        builder.AppendLine();
        builder.AppendLine($"[宝箱{tier}]");
        for (var index = 1; index <= 8; index++)
        {
            var threshold = index == 8 ? 9999 : index * 1000;
            builder.AppendLine(
                $"奖品{index}=经验:{index}/{threshold}");
        }
        builder.AppendLine();
    }
    builder.AppendLine("[金牛装备]");
    builder.AppendLine("奖品1=经验:1/9999");
    return builder.ToString();
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
}

static string FindRepositoryRoot()
{
    foreach (var origin in new[]
             {
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(origin);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("repository root not found");
}

static void EqualSequence(int[] expected, int[] actual, string message)
{
    Assert(expected.SequenceEqual(actual), message +
        $": expected [{string.Join(",", expected)}], " +
        $"actual [{string.Join(",", actual)}]");
}

static void EqualByteSequence(byte[] expected, byte[] actual, string message)
{
    Assert(expected.SequenceEqual(actual), message +
        $": expected {Convert.ToHexString(expected)}, " +
        $"actual {Convert.ToHexString(actual)}");
}

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.Ordinal), message);

static void RequireOrder(string source, params string[] values)
{
    var previous = -1;
    foreach (var value in values)
    {
        var current = source.IndexOf(value, previous + 1,
            StringComparison.Ordinal);
        Assert(current > previous,
            "source order mismatch at: " + value);
        previous = current;
    }
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{message}: expected '{expected}', actual '{actual}'");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
