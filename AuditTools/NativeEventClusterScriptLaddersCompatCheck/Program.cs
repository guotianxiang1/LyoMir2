// Compat audit for NativeEventClusterScriptLadders — asserts every modeled
// branch of the five dynamic-room / 天关-关卡 / event-cluster PAS-script
// handler ladders against the 战神 M2Server binary evidence
// (image base 0x00400000). Pure decision oracle; no runtime state required.

using GameSvr;

void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"FAIL {label}: expected <{expected}>, actual <{actual}>");
}

// --- 1. GroupFlyToDynRoomInRange (sub_6E0734) -------------------------------
Equal(NativeGroupFlyToDynRoomInRangeOutcome.NoGroup,
    NativeGroupFlyToDynRoomInRangePlanner.Plan(hasGroup: false,
        roomResolvesToActiveEnv: false), "GFTDRIR no group / no room");
Equal(NativeGroupFlyToDynRoomInRangeOutcome.NoGroup,
    NativeGroupFlyToDynRoomInRangePlanner.Plan(hasGroup: false,
        roomResolvesToActiveEnv: true), "GFTDRIR no group gate first");
Equal(NativeGroupFlyToDynRoomInRangeOutcome.RoomNotActive,
    NativeGroupFlyToDynRoomInRangePlanner.Plan(hasGroup: true,
        roomResolvesToActiveEnv: false), "GFTDRIR room not active");
Equal(NativeGroupFlyToDynRoomInRangeOutcome.DispatchRangeMove,
    NativeGroupFlyToDynRoomInRangePlanner.Plan(hasGroup: true,
        roomResolvesToActiveEnv: true), "GFTDRIR dispatch");

// Exact register/stack source mapping fed into sub_727678.
// Delphi ABI: extra params pushed left-to-right -> ECX<-x, arg_4=y, arg_0=iRange
// (centerX=x, centerY=y, radius=iRange), same as sibling GroupFlyInRange.
var gftdrir = NativeGroupFlyToDynRoomInRangePlanner.BuildDispatch(
    roomName: "GuanRoom", roomIdx: 2, x: 30, y: 40, iRange: 5);
Equal("GuanRoom", gftdrir.RoomName, "GFTDRIR room name");
Equal(2, gftdrir.RoomIdx, "GFTDRIR room idx");
Equal(30, gftdrir.EcxValue, "GFTDRIR ecx <- x (centerX)");
Equal(40, gftdrir.FirstPushedValue, "GFTDRIR first-push = y (centerY)");
Equal(5, gftdrir.LastPushedValue, "GFTDRIR last-push = iRange (radius)");
Equal("x", NativeGroupRangeMoveDispatch.EcxSource, "GFTDRIR ecx source");
Equal("y", NativeGroupRangeMoveDispatch.FirstPushedSource, "GFTDRIR first source");
Equal("iRange", NativeGroupRangeMoveDispatch.LastPushedSource, "GFTDRIR last source");
Equal(0x005FCB78, NativeGroupFlyToDynRoomInRangePlanner.RoomResolverAddress,
    "GFTDRIR resolver addr");
Equal(0x00727678, NativeGroupFlyToDynRoomInRangePlanner.RangeMoverAddress,
    "GFTDRIR mover addr");
Console.WriteLine("PASS GroupFlyToDynRoomInRange (sub_6E0734) ladder + dispatch");

// --- 2. AddGuanMoPoint (sub_6EFB08) -----------------------------------------
Equal("Select * from gamedata.LiPaoObPoint where CharName=\"Hero\";",
    NativeGuanMoPointAccumulator.BuildProbeSql("Hero"), "GuanMo probe sql");

var gmUpdate = NativeGuanMoPointAccumulator.Plan(
    probeRowCount: 1, gmPoint: 5, charName: "Hero", ptid: "PT1");
Equal(NativeGuanMoPointStatement.Update, gmUpdate.Statement, "GuanMo update branch");
Equal("Update gamedata.LiPaoObPoint Set ObPoint=ObPoint+5 where Charname=\"Hero\";",
    gmUpdate.Sql, "GuanMo update sql");

var gmUpdateNeg = NativeGuanMoPointAccumulator.Plan(
    probeRowCount: 3, gmPoint: -3, charName: "Hero", ptid: "PT1");
Equal(NativeGuanMoPointStatement.Update, gmUpdateNeg.Statement, "GuanMo negative update branch");
Equal("Update gamedata.LiPaoObPoint Set ObPoint=ObPoint+-3 where Charname=\"Hero\";",
    gmUpdateNeg.Sql, "GuanMo negative update sql");

var gmInsertZero = NativeGuanMoPointAccumulator.Plan(
    probeRowCount: 0, gmPoint: 7, charName: "Hero", ptid: "PT1");
Equal(NativeGuanMoPointStatement.Insert, gmInsertZero.Statement, "GuanMo zero->insert");
Equal("insert into gamedata.LiPaoObPoint(PTID, CharName, ObPoint) values(\"PT1\",\"Hero\",7);",
    gmInsertZero.Sql, "GuanMo insert sql");

var gmInsertNeg = NativeGuanMoPointAccumulator.Plan(
    probeRowCount: -1, gmPoint: 7, charName: "Hero", ptid: "PT1");
Equal(NativeGuanMoPointStatement.Insert, gmInsertNeg.Statement,
    "GuanMo failed-probe fail-open insert");
Console.WriteLine("PASS AddGuanMoPoint (sub_6EFB08) SELECT->UPDATE|INSERT ladder");

// --- 3. RandomFlyTo (sub_6DF7A8) --------------------------------------------
Equal(NativeRandomFlyToOutcome.EmptyMapName,
    NativeRandomFlyToPlanner.Plan(""), "RandomFlyTo empty string");
Equal(NativeRandomFlyToOutcome.EmptyMapName,
    NativeRandomFlyToPlanner.Plan(null), "RandomFlyTo null string");
Equal(NativeRandomFlyToOutcome.PostRandomFly,
    NativeRandomFlyToPlanner.Plan("0114"), "RandomFlyTo non-empty");
Equal(0x2747, NativeRandomFlyToPlanner.PostMessageId, "RandomFlyTo post msg id");
Equal(0x00765E68, NativeRandomFlyToPlanner.SendMsgExecutorAddress,
    "RandomFlyTo executor addr");
Console.WriteLine("PASS RandomFlyTo (sub_6DF7A8) empty-gate + post-message ladder");

// --- 4. CoupleFly (sub_6E036C -> sub_6CEF14) --------------------------------
Equal(NativeCoupleFlyOutcome.NotMarried,
    NativeCoupleFlyPlanner.Plan(false, "Wife", "0114", true, false, false, true),
    "CoupleFly not married");
Equal(NativeCoupleFlyOutcome.NoSpouseName,
    NativeCoupleFlyPlanner.Plan(true, "", "0114", true, false, false, true),
    "CoupleFly empty spouse name");
Equal(NativeCoupleFlyOutcome.EmptyTargetMap,
    NativeCoupleFlyPlanner.Plan(true, "Wife", "", true, false, false, true),
    "CoupleFly empty target map");
Equal(NativeCoupleFlyOutcome.SpouseOffline,
    NativeCoupleFlyPlanner.Plan(true, "Wife", "0114", false, false, false, true),
    "CoupleFly spouse offline");
Equal(NativeCoupleFlyOutcome.SpouseIsSelf,
    NativeCoupleFlyPlanner.Plan(true, "Wife", "0114", true, true, false, true),
    "CoupleFly spouse is self");
Equal(NativeCoupleFlyOutcome.SpouseBlocked,
    NativeCoupleFlyPlanner.Plan(true, "Wife", "0114", true, false, true, true),
    "CoupleFly spouse blocked");
Equal(NativeCoupleFlyOutcome.DifferentEnvironment,
    NativeCoupleFlyPlanner.Plan(true, "Wife", "0114", true, false, false, false),
    "CoupleFly different environment");
Equal(NativeCoupleFlyOutcome.MoveBoth,
    NativeCoupleFlyPlanner.Plan(true, "Wife", "0114", true, false, false, true),
    "CoupleFly move both");
Equal(0x006CEF14, NativeCoupleFlyPlanner.ExecutorAddress, "CoupleFly executor addr");
Console.WriteLine("PASS CoupleFly (sub_6E036C/sub_6CEF14) seven-gate fail-closed ladder");

// --- 5. DoRelive (sub_6E13C8) -----------------------------------------------
var reliveZero = NativeDoRelivePlanner.Plan(delayTime: 0, hp: 500);
Equal(NativeDoReliveOutcome.NonPositiveDelay, reliveZero.Outcome, "DoRelive zero delay");
Equal(0, reliveZero.DelayMilliseconds, "DoRelive zero delay ms");

var reliveNeg = NativeDoRelivePlanner.Plan(delayTime: -5, hp: 500);
Equal(NativeDoReliveOutcome.NonPositiveDelay, reliveNeg.Outcome, "DoRelive negative delay");

var reliveOk = NativeDoRelivePlanner.Plan(delayTime: 3, hp: 500);
Equal(NativeDoReliveOutcome.Schedule, reliveOk.Outcome, "DoRelive schedule");
Equal(3000, reliveOk.DelayMilliseconds, "DoRelive delay*1000");
Equal(500, reliveOk.Hp, "DoRelive hp preserved");
Equal(0x27B1, NativeDoRelivePlanner.DelayedMessageId, "DoRelive delayed msg id");
Equal(0x27B0, NativeDoRelivePlanner.ImmediateMessageId, "DoRelive immediate msg id");
Equal(0x3E8, NativeDoRelivePlanner.MillisecondsPerSecond, "DoRelive ms/sec");
Console.WriteLine("PASS DoRelive (sub_6E13C8) delay-gate + schedule ladder");

// --- 6. Shared dynroom-fly eligibility gate (sub_5FB584 / sub_5FB714) --------
Equal(true, NativeDynRoomFlyEligibility.IsEligible(true, false, true),
    "eligibility all-ok");
Equal(false, NativeDynRoomFlyEligibility.IsEligible(false, false, true),
    "eligibility null player");
Equal(false, NativeDynRoomFlyEligibility.IsEligible(true, true, true),
    "eligibility ghost [+0x73]");
Equal(false, NativeDynRoomFlyEligibility.IsEligible(true, false, false),
    "eligibility non-playobject race [+0x178]");
Console.WriteLine("PASS DynRoomFly eligibility gate (ghost+0x73 / race+0x178)");

// --- 7. FlyToDynRoom (sub_6DF088 -> sub_5FB584), returns int -----------------
Equal(NativeFlyToDynRoomOutcome.Ineligible,
    NativeFlyToDynRoomPlanner.Plan(false, true, true, false, 4).Outcome,
    "FlyToDynRoom ineligible");
Equal(-1, NativeFlyToDynRoomPlanner.Plan(false, true, true, false, 4).ResultIndex,
    "FlyToDynRoom ineligible -> -1");
Equal(NativeFlyToDynRoomOutcome.DefinitionMissing,
    NativeFlyToDynRoomPlanner.Plan(true, false, true, false, 4).Outcome,
    "FlyToDynRoom definition missing");
Equal(-1, NativeFlyToDynRoomPlanner.Plan(true, false, true, false, 4).ResultIndex,
    "FlyToDynRoom definition missing -> -1");
Equal(NativeFlyToDynRoomOutcome.AcquisitionFailed,
    NativeFlyToDynRoomPlanner.Plan(true, true, false, false, 4).Outcome,
    "FlyToDynRoom acquisition failed");
Equal(NativeFlyToDynRoomOutcome.EnvironmentBlocked,
    NativeFlyToDynRoomPlanner.Plan(true, true, true, true, 4).Outcome,
    "FlyToDynRoom env blocked [+0xF1]");
Equal(-1, NativeFlyToDynRoomPlanner.Plan(true, true, true, true, 4).ResultIndex,
    "FlyToDynRoom env blocked -> -1 (diagnostic only)");
var flyOk = NativeFlyToDynRoomPlanner.Plan(true, true, true, false, 7);
Equal(NativeFlyToDynRoomOutcome.MovedReturnsIndex, flyOk.Outcome, "FlyToDynRoom moved");
Equal(7, flyOk.ResultIndex, "FlyToDynRoom returns env dynamic index [+0xD4]");
Equal(0x005FB584, NativeFlyToDynRoomPlanner.ResolverAddress, "FlyToDynRoom resolver addr");
Equal(0x006BD294, NativeFlyToDynRoomPlanner.PlayerMoveVirtual, "FlyToDynRoom move virtual");
Console.WriteLine("PASS FlyToDynRoom (sub_6DF088/sub_5FB584) acquire-and-fly int ladder");

// --- 8. FlyToDynEnvirWithIdx (sub_6DF020 -> sub_5FB714), returns bool --------
Equal(NativeFlyToDynEnvirWithIdxOutcome.Ineligible,
    NativeFlyToDynEnvirWithIdxPlanner.Plan(false, true, true, false).Outcome,
    "FlyIdx ineligible");
Equal(false, NativeFlyToDynEnvirWithIdxPlanner.Plan(false, true, true, false).Result,
    "FlyIdx ineligible -> false");
Equal(NativeFlyToDynEnvirWithIdxOutcome.DefinitionMissing,
    NativeFlyToDynEnvirWithIdxPlanner.Plan(true, false, true, false).Outcome,
    "FlyIdx definition missing");
Equal(NativeFlyToDynEnvirWithIdxOutcome.IndexNotActive,
    NativeFlyToDynEnvirWithIdxPlanner.Plan(true, true, false, false).Outcome,
    "FlyIdx index not active (state+0xF0!=2 or index+0xD4!=idx)");
Equal(NativeFlyToDynEnvirWithIdxOutcome.EnvironmentBlocked,
    NativeFlyToDynEnvirWithIdxPlanner.Plan(true, true, true, true).Outcome,
    "FlyIdx env blocked [+0xF1]");
Equal(false, NativeFlyToDynEnvirWithIdxPlanner.Plan(true, true, true, true).Result,
    "FlyIdx env blocked -> false (diagnostic only)");
var flyIdxOk = NativeFlyToDynEnvirWithIdxPlanner.Plan(true, true, true, false);
Equal(NativeFlyToDynEnvirWithIdxOutcome.MovedReturnsTrue, flyIdxOk.Outcome, "FlyIdx moved");
Equal(true, flyIdxOk.Result, "FlyIdx returns true");
Equal(0x005FB714, NativeFlyToDynEnvirWithIdxPlanner.ResolverAddress, "FlyIdx resolver addr");
Equal(0x005FEA90, NativeFlyToDynEnvirWithIdxPlanner.ActiveLookupVirtual, "FlyIdx active lookup");
Console.WriteLine("PASS FlyToDynEnvirWithIdx (sub_6DF020/sub_5FB714) indexed-fly bool ladder");

Console.WriteLine("ALL PASS NativeEventClusterScriptLadders (7 handler ladders)");
return 0;
