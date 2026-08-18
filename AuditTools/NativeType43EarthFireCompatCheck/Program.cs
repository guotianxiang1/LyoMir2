using System.Buffers.Binary;
using System.Reflection;
using DBSvr.Core;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

if (args.Any(arg => string.Equals(arg, "--fire-only",
        StringComparison.OrdinalIgnoreCase)))
{
    CheckFireBurnEvent();
    Console.WriteLine("PASS native fire-burn duration+damage compatibility");
    return;
}

CheckType43AbiAndLifecycle();
CheckType43PacketGoldens();
CheckFastnessHqTable();
CheckBubbleTimedState();
CheckStandardEarthFireResolver();
CheckNativeLandingModifiers();
CheckNativeShieldShapeSources();
CheckNativeSearchAndUserMoveCommands();
CheckHealthSpellDirtyCadence();
CheckEventManagerCadence();
CheckFireBurnEvent();
CheckSourceContracts();

Console.WriteLine(
    "PASS native-type43 ABI=byte/word internal75 lifecycle=500ms+wrap+two-phase " +
    "wire=SM657/3555 HQ=parse+signed-min+unchecked resolver=earthfire22/category1/flags0 " +
    "landing=141+full/standard/half event=250ms+FIFO " +
    "search=necklace121+254/3 usermove=ring112+254/4 " +
    "commands=Searching+UserMove10056/two-phase/env-bound/1500ms " +
    "fire=ApplyTo+3000ms+RM10027");
return;

static void CheckType43AbiAndLifecycle()
{
    var map = NewMap(64, 64);
    var player = NewPlayer("type43-abi", map);
    SetField(player, "m_nHitSpeed", (ushort)0x5566);
    var bridge = new PasApiBridge { CurrentPlayer = player };

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(43, 7, 30)),
        "AddPlayerAbil type43 dispatch");
    Assert(player.HasTimedAbility(43), "type43 node");
    Assert(player.HasNativeActiveState(75), "type43 -> internal75");
    Equal(7, player.GetTimedAbilityValue(43), "type43 value");
    Equal(30_000, player.GetTimedAbilityRemainingMilliseconds(43),
        "type43 duration");
    Equal(1, player.TimedStates.Count, "type43 client callback count");
    CheckTimedState(player.TimedStates[0], 75, 30_000, 7, false,
        "type43 add callback");

    var active75 = new byte[]
    {
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 8, 0, 0, 0, 0, 0, 0
    };
    Bytes(active75, player.GetBodyStateBuffer(), "internal75 body state");

    var status = player.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_CHARSTATUSCHANGED);
    Equal(0, status.wParam, "type43 RM10139 wParam");
    Equal(0x5566, status.nParam1, "type43 RM10139 hit speed slot");
    Equal(0, status.nParam2, "type43 RM10139 nParam2");
    Equal(0, status.nParam3, "type43 RM10139 nParam3");
    Assert(ReferenceEquals(player, status.BaseObject),
        "type43 RM10139 source");
    Bytes(active75, status.Payload as byte[] ?? Array.Empty<byte>(),
        "type43 RM10139 16-byte payload");

    var prompt = player.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE);
    Equal(0xDB, prompt.nParam1, "type43 prompt foreground");
    Equal(0xFF, prompt.nParam2, "type43 prompt background");
    Equal("火墙抗性瞬间提高30秒", prompt.Buff, "type43 add prompt");

    TProcessMessage processMessage = null;
    Assert(player.TryTake(ref processMessage), "type43 RM10139 dequeue");
    Equal(Grobal2.RM_CHARSTATUSCHANGED, processMessage.wIdent,
        "type43 first queued ident");
    Assert(player.Operate(processMessage), "type43 RM10139 dispatch");
    Bytes(BuildHeaderBytes(player.ObjectId, Grobal2.SM_CHARSTATUSCHANGED,
            0, 0, 0x5566),
        HeaderBytes(player.m_DefMsg), "type43 SM657 golden header");

    var alias = NewPlayer("type299-alias", map);
    var aliasBridge = new PasApiBridge { CurrentPlayer = alias };
    Assert(aliasBridge.CallPlayerMethod("AddPlayerAbil",
            Values(299, 65_537, 65_537)),
        "AddPlayerAbil type299 dispatch");
    Assert(alias.HasTimedAbility(43), "type299 low-byte aliases type43");
    Equal(1, alias.GetTimedAbilityValue(43), "PAS Word value coercion");
    Equal(1_000, alias.GetTimedAbilityRemainingMilliseconds(43),
        "PAS Word seconds coercion");

    var refresh = NewPlayer("type43-refresh", map);
    refresh.AddTimedAbility(43, 7, 30);
    refresh.ConsumePendingRecalcForCheck();
    Equal(7, refresh.m_nNativeHqFastness, "type43 deferred HQ recalc");

    refresh.ClearNotifications();
    refresh.AddTimedAbility(43, 6, 40);
    Equal(7, refresh.GetTimedAbilityValue(43), "lower value preserved");
    Equal(30_000, refresh.GetTimedAbilityRemainingMilliseconds(43),
        "lower duration preserved");
    Assert(!refresh.IsAbilityRecalcPending,
        "lower refresh marked recalc dirty");
    CheckLastPrompt(refresh, "火墙抗性瞬间提高30秒",
        "lower refresh retained-duration prompt");

    refresh.ClearNotifications();
    refresh.AddTimedAbility(43, 7, 20);
    Equal(30_000, refresh.GetTimedAbilityRemainingMilliseconds(43),
        "equal shorter duration preserved");
    Assert(!refresh.IsAbilityRecalcPending,
        "equal shorter refresh marked recalc dirty");

    refresh.ClearNotifications();
    refresh.AddTimedAbility(43, 7, 40);
    Equal(40_000, refresh.GetTimedAbilityRemainingMilliseconds(43),
        "equal longer duration extended");
    Assert(!refresh.IsAbilityRecalcPending,
        "equal longer refresh marked recalc dirty");
    CheckLastPrompt(refresh, "火墙抗性瞬间提高40秒",
        "equal longer prompt");

    refresh.ClearNotifications();
    refresh.AddTimedAbility(43, 8, 5);
    Equal(8, refresh.GetTimedAbilityValue(43), "higher value replaced");
    Equal(5_000, refresh.GetTimedAbilityRemainingMilliseconds(43),
        "higher duration replaced");
    Assert(refresh.IsAbilityRecalcPending,
        "higher replacement did not mark recalc dirty");
    Assert(refresh.TimedStates[^1].DirtyAtCallback,
        "higher replacement callback did not observe dirty state");
    refresh.ConsumePendingRecalcForCheck();
    Equal(8, refresh.m_nNativeHqFastness, "higher value deferred HQ recalc");

    var baseline = NewPlayer("type43-fixed-baseline", map);
    baseline.m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
    BinaryPrimitives.WriteUInt16LittleEndian(
        baseline.m_NativeHumanData.AsSpan(0x148, sizeof(ushort)), 5);
    baseline.RecalcAbilitys();
    Equal(5, baseline.m_nNativeHqFastness, "type43 fixed HQ baseline");
    baseline.AddTimedAbility(43, 3, 0);
    baseline.ConsumePendingRecalcForCheck();
    Equal(8, baseline.m_nNativeHqFastness,
        "type43 timed value added to fixed HQ baseline");
    baseline.ClearNotifications();
    SetField(baseline, "m_TimedAbilityProcessTick", 15_000);
    SetAllTimedNodeTicks(baseline, 15_000);
    baseline.ProcessTimedAbilities(15_500);
    Assert(!baseline.HasTimedAbility(43),
        "type43 fixed-baseline node survived expiry");
    Equal(8, baseline.m_nNativeHqFastness,
        "type43 expiry recalculated before Run tail");
    baseline.ConsumePendingRecalcForCheck();
    Equal(5, baseline.m_nNativeHqFastness,
        "type43 expiry did not restore fixed HQ baseline");

    var forever = NewPlayer("type43-forever", map);
    forever.AddTimedAbility(43, 9, -1);
    forever.ConsumePendingRecalcForCheck();
    forever.ClearNotifications();
    forever.AddTimedAbility(43, 9, 10);
    Equal(10_000, forever.GetTimedAbilityRemainingMilliseconds(43),
        "signed equal refresh replaces -1 with finite duration");
    Assert(!forever.IsAbilityRecalcPending,
        "equal -1 to finite refresh marked recalc dirty");

    forever.ClearNotifications();
    Assert(forever.RemoveTimedAbility(43), "type43 explicit removal");
    Assert(!forever.HasTimedAbility(43), "type43 removal node");
    Assert(!forever.HasNativeActiveState(75), "type43 removal state75");
    Equal(1, forever.TimedStates.Count, "type43 removal callback count");
    var removal = forever.TimedStates[0];
    Assert(removal.Removed, "type43 removal callback flag");
    Assert(!removal.Type43Present && !removal.DirtyAtCallback,
        "type43 removal callback ordering");
    Assert(forever.IsAbilityRecalcPending,
        "type43 removal did not mark dirty after callback");
    CheckLastPrompt(forever, "火墙抗性回复正常", "type43 removal prompt");

    var zero = NewPlayer("type43-zero", map);
    zero.AddTimedAbility(43, 1, 0);
    zero.ClearNotifications();
    SetField(zero, "m_TimedAbilityProcessTick", 10_000);
    SetAllTimedNodeTicks(zero, 10_000);
    zero.ProcessTimedAbilities(10_499);
    Assert(zero.HasTimedAbility(43), "zero-second expired before 500ms scan");
    Equal(0, zero.TimedStates.Count, "zero-second early callback");
    zero.ProcessTimedAbilities(10_500);
    Assert(!zero.HasTimedAbility(43), "zero-second survived eligible scan");
    Equal(1, zero.TimedStates.Count, "zero-second expiry callback");

    var wrap = NewPlayer("type43-wrap", map);
    wrap.AddTimedAbility(43, 1, 1);
    wrap.ClearNotifications();
    var wrapStart = int.MaxValue - 100;
    SetField(wrap, "m_TimedAbilityProcessTick", wrapStart);
    SetAllTimedNodeTicks(wrap, wrapStart);
    wrap.ProcessTimedAbilities(unchecked(wrapStart + 600));
    Equal(400, wrap.GetTimedAbilityRemainingMilliseconds(43),
        "tick-wrap elapsed deduction");

    var batch = NewPlayer("type43-batch", map);
    batch.AddTimedAbility(43, 1, 0);
    batch.AddTimedAbility(0, 1, 0);
    batch.ConsumePendingRecalcForCheck();
    batch.ClearNotifications();
    SetField(batch, "m_TimedAbilityProcessTick", 20_000);
    SetAllTimedNodeTicks(batch, 20_000);
    batch.ProcessTimedAbilities(20_500);
    Equal(2, batch.TimedStates.Count, "two-phase expiry callback count");
    Equal((byte)75, batch.TimedStates[0].InternalType,
        "two-phase oldest callback first");
    Equal((byte)32, batch.TimedStates[1].InternalType,
        "two-phase newest callback second");
    Assert(batch.TimedStates.All(state =>
            !state.Type43Present && !state.Type0Present),
        "two-phase callbacks observed a partially linked batch");
}

static void CheckType43PacketGoldens()
{
    var add = BuildTimedAbilityPacket(75, 30_000, 0x12345678, false);
    Bytes(new byte[]
    {
        0x30, 0x75, 0, 0, 0xE3, 0x0D, 0x4B, 0, 0, 0, 0, 0
    }, HeaderBytes(add.Header), "3555 add header golden");
    Bytes(new byte[]
    {
        0x4B, 0, 0x30, 0x75, 0, 0, 0x78, 0x56, 0x34, 0x12
    }, add.Body, "3555 add body golden");

    var remove = BuildTimedAbilityPacket(75, -123, 0x12345678, true);
    Bytes(new byte[]
    {
        0, 0, 0, 0, 0xE3, 0x0D, 0x4B, 0, 0, 0, 0, 0
    }, HeaderBytes(remove.Header), "3555 removal header golden");
    Equal(0, remove.Body.Length, "3555 removal body length");
}

static void CheckFastnessHqTable()
{
    var directory = Directory.CreateTempSubdirectory("m2-hq-audit-");
    try
    {
        var file = Path.Combine(directory.FullName, "FASTNESS_HQ.txt");
        File.WriteAllText(file, string.Join(Environment.NewLine, new[]
        {
            "# comment", ";comment", "0 9 9", "-2 -0.25 -7",
            "1 0.25 100", "2 0.3333333333333333 100", "4 2.0 15",
            "4 0.5 7", "6 -0.5 3", "bad 1 2"
        }));

        var table = new NativeFastnessHqTable();
        Assert(table.Load(file), "HQ table load");
        Equal(5, table.Count, "HQ parsed entry count");
        Equal(6, table.MaximumPositiveKey, "HQ maximum positive key");
        Equal(76, table.ApplyReduction(101, 1), "HQ positive truncation");
        Equal(-76, table.ApplyReduction(-101, 1), "HQ negative truncation");
        Equal(68, table.ApplyReduction(101, 2), "HQ repeating ratio");
        Equal(93, table.ApplyReduction(100, 4), "HQ duplicate last wins");
        Equal(151, table.ApplyReduction(101, 99), "HQ selector cap");
        Equal(125, table.ApplyReduction(100, -2), "HQ signed minimum");
        Equal(100, table.ApplyReduction(100, 3), "HQ missing exact key");

        File.WriteAllText(file, string.Join(Environment.NewLine, new[]
        {
            "1 4294967296.5 2147483647",
            "2 4294967297 2147483647",
            "3 -1 2147483647"
        }));
        Assert(table.Load(file), "HQ overflow table load");
        Equal(1, table.ApplyReduction(1, 1), "HQ candidate low32 zero");
        Equal(0, table.ApplyReduction(1, 2), "HQ candidate low32 one");
        Equal(-2, table.ApplyReduction(int.MaxValue, 3),
            "HQ unchecked subtraction overflow");

        var beforeMissing = table.Count;
        Assert(!table.Load(Path.Combine(directory.FullName, "missing.txt")),
            "HQ missing file result");
        Equal(beforeMissing, table.Count, "HQ missing file preserved table");

        File.WriteAllText(file, string.Empty);
        Assert(table.Load(file), "HQ empty existing load");
        Equal(0, table.Count, "HQ empty existing clears table");
        Equal(0, table.MaximumPositiveKey, "HQ empty existing clears max");

        File.WriteAllText(file, "-2 0.5 10");
        Assert(table.Load(file), "HQ negative-only load");
        Equal(0, table.MaximumPositiveKey, "HQ negative-only max");
        Equal(100, table.ApplyReduction(100, 99),
            "HQ no positive max means no selector cap");
    }
    finally
    {
        directory.Delete(true);
    }
}

static void CheckBubbleTimedState()
{
    M2Share.NativeFastnessHqTable = new NativeFastnessHqTable();
    var source = new HostileSource();
    var map = NewMap(16, 16);

    var bubble = NewPlayer("earthfire-bubble", map);
    SetDamageAbility(bubble, 500, 500, 100);
    Assert(bubble.MagBubbleDefenceUp(2, 2), "bubble initial add result");
    Assert(bubble.HasNativeActiveState(20), "bubble internal20 active");
    Assert(!bubble.HasNativeActiveState(19),
        "level2 bubble created internal19 companion");
    // legacy 槽已经不是第二权威了，它是 Self+0xDC 节点链的转发视图
    // （TBaseObject.LegacyStatusTimeView.cs）：slot i 就是 state 31-i，
    // slot 11 == internal20，读数是同一个节点的剩余毫秒向上取整成秒。
    // 上一行刚断言过 internal20 存在，下一行断言该节点 duration=2000ms，
    // 所以这里必须读回 2 而不是 0；断言 0 是 4.18 双权威时代的旧契约。
    Equal(2, bubble.m_wStatusTimeArr[Grobal2.STATE_BUBBLEDEFENCEUP],
        "bubble legacy slot11 must mirror the internal20 node");
    Equal(1, bubble.TimedStates.Count, "bubble add callback count");
    CheckTimedState(bubble.TimedStates[0], 20, 2_000, 2, false,
        "bubble add callback");
    Assert(!bubble.IsAbilityRecalcPending,
        "bubble add incorrectly marked recalc");

    bubble.m_nCharStatus = bubble.GetCharStatus();
    Assert(bubble.HasNativeActiveState(20),
        "GetCharStatus erased internal20");
    bubble.ClearNotifications();
    Equal(32, ResolveFullMagicDamage(bubble, source, 22, 1, 100),
        "bubble level2 damage");
    Equal(-1_000, GetNativeTimedRemaining(bubble, 20),
        "bubble hit did not subtract 3000ms");
    Assert(bubble.HasNativeActiveState(20),
        "bubble hit removed negative node immediately");
    Equal(0, bubble.TimedStates.Count,
        "bubble hit emitted lifecycle callback");

    SetField(bubble, "m_TimedAbilityProcessTick", 10_000);
    SetAllTimedNodeTicks(bubble, 10_000);
    bubble.ProcessTimedAbilities(10_499);
    Assert(bubble.HasNativeActiveState(20),
        "bubble expired before 500ms scan");
    bubble.ProcessTimedAbilities(10_500);
    Assert(!bubble.HasNativeActiveState(20),
        "bubble survived eligible expiry scan");
    Equal(1, bubble.TimedStates.Count, "bubble removal callback count");
    CheckTimedState(bubble.TimedStates[0], 20, -1_500, 2, true,
        "bubble removal callback");
    Assert(!bubble.IsAbilityRecalcPending,
        "bubble removal incorrectly marked recalc");

    var gated = NewPlayer("earthfire-bubble-gated", map);
    Assert(gated.SetNativeActiveState(52), "bubble state52 setup");
    Assert(gated.MagBubbleDefenceUp(2, 10),
        "bubble state52 training return");
    Assert(!gated.HasNativeActiveState(20),
        "bubble state52 created node");
    Equal(0, gated.TimedStates.Count, "bubble state52 notification");

    var state7 = NewPlayer("earthfire-bubble-state7", map);
    SetDamageAbility(state7, 500, 500, 100);
    Assert(state7.MagBubbleDefenceUp(2, 10), "bubble state7 add");
    int beforeState7 = GetNativeTimedRemaining(state7, 20);
    Assert(state7.SetNativeActiveState(7), "bubble state7 setup");
    Equal(30, ResolveFullMagicDamage(state7, source, 22, 1, 100),
        "state7 precedes bubble");
    Equal(beforeState7, GetNativeTimedRemaining(state7, 20),
        "state7 consumed bubble duration");

    var high = NewPlayer("earthfire-bubble-level4", map);
    SetDamageAbility(high, 1_000, 1_000, 100);
    Assert(high.MagBubbleDefenceUp(4, 10), "level4 bubble add");
    Assert(high.HasNativeActiveState(19) &&
           high.HasNativeActiveState(20),
        "level4 bubble companion body bits");
    Equal(2, high.TimedStates.Count, "level4 add callback count");
    Equal((byte)19, high.TimedStates[0].InternalType,
        "level4 companion add order");
    Equal((byte)20, high.TimedStates[1].InternalType,
        "level4 bubble add order");
    Assert(high.TimedStates.All(state => !state.Removed),
        "level4 add callback removal flag");
    Assert(!high.IsAbilityRecalcPending,
        "level4 bubble or companion marked recalc");

    high.ClearNotifications();
    for (var i = 0; i < 4; i++)
    {
        Equal(30, ResolveFullMagicDamage(high, source, 22, 1, 100),
            $"level4 bubble hit {i}");
    }
    Equal(-2_000, GetNativeTimedRemaining(high, 20),
        "level4 bubble accumulated duration debt");
    Equal(0, high.TimedStates.Count,
        "level4 hits emitted lifecycle callback");
    SetField(high, "m_TimedAbilityProcessTick", 20_000);
    SetAllTimedNodeTicks(high, 20_000);
    high.ProcessTimedAbilities(20_500);
    Assert(!high.HasNativeActiveState(20) &&
           !high.HasNativeActiveState(19),
        "level4 expiry left companion active");
    Equal(2, high.TimedStates.Count, "level4 removal callback count");
    Equal((byte)20, high.TimedStates[0].InternalType,
        "level4 bubble removal order");
    Equal((byte)19, high.TimedStates[1].InternalType,
        "level4 companion removal order");
    Assert(high.TimedStates.All(state => state.Removed),
        "level4 removal flags");
    Assert(!high.IsAbilityRecalcPending,
        "level4 removal marked recalc");
}

static void CheckStandardEarthFireResolver()
{
    M2Share.NativeFastnessHqTable = LoadHqTable("1 0.25 100");
    var source = new HostileSource();

    var target = NewDamageTarget(500, 500, 123);
    target.m_nNativeHqFastness = 1;
    Equal(75, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "HQ resolver damage");
    Equal(425, target.m_WAbil.HP, "HQ resolver HP");
    Equal(123, target.m_WAbil.MP, "HQ resolver MP unchanged");

    target = NewDamageTarget(500, 500, 123);
    target.m_nNativeHqFastness = 1;
    Equal(100, ResolveFullMagicDamage(target, source, 21, 1, 100),
        "HQ wrong skill gate");
    Equal(400, target.m_WAbil.HP, "HQ wrong skill HP");

    target = NewDamageTarget(500, 500, 123);
    target.m_nNativeHqFastness = 1;
    Equal(100, ResolveFullMagicDamage(target, source, 22, 2, 100),
        "HQ wrong category gate");

    target = NewDamageTarget(500, 500, 123);
    target.m_nNativeHqFastness = 1;
    Equal(75, ResolveFullMagicDamage(target, source, 22, 257, 100),
        "HQ category low-byte coercion");

    target = NewDamageTarget(30, 500, 123);
    Equal(80, ResolveFullMagicDamage(target, source, 22, 1, 80),
        "resolver return is not HP-capped");
    Equal(0, target.m_WAbil.HP, "resolver HP clamps at zero");
    Equal(123, target.m_WAbil.MP, "resolver no-shield MP unchanged");

    target = NewDamageTarget(500, 500, 123);
    target.m_WAbil.MAC = HUtil32.MakeLong(20, 20);
    Equal(80, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "fixed MAC reduction");
    Equal(420, target.m_WAbil.HP, "fixed MAC HP");

    target = NewDamageTarget(500, 500, 123);
    target.m_WAbil.MAC = HUtil32.MakeLong(20, 20);
    Assert(target.SetNativeActiveState(17), "resolver state17 setup");
    Equal(100, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "state17 skips MAC but is not immunity");
    Equal(400, target.m_WAbil.HP, "state17 HP");

    var playerMap = NewMap(16, 16);
    var luckyTarget = NewPlayer("earthfire-lucky-mac", playerMap);
    luckyTarget.m_WAbil.HP = 500;
    luckyTarget.m_WAbil.MaxHP = 500;
    luckyTarget.m_WAbil.MP = 100;
    luckyTarget.m_WAbil.MaxMP = 100;
    luckyTarget.m_WAbil.MAC = HUtil32.MakeLong(10, 20);
    luckyTarget.m_nBodyLuckLevel = 5;
    Equal(80, ResolveFullMagicDamage(luckyTarget, source, 22, 1, 100),
        "player body-luck forces high MAC");

    target = NewDamageTarget(500, 500, 123);
    Assert(target.SetNativeActiveState(7), "resolver state7 setup");
    Equal(3, ResolveFullMagicDamage(target, source, 22, 1, 11),
        "resolver state7 integer reduction");
    Equal(497, target.m_WAbil.HP, "resolver state7 HP");

    foreach (var immunityState in new[] { 52, 55 })
    {
        target = NewDamageTarget(500, 500, 123);
        Assert(target.SetNativeActiveState(immunityState),
            $"resolver state{immunityState} setup");
        Equal(0, ResolveFullMagicDamage(target, source, 22, 1, 80),
            $"resolver state{immunityState} immunity");
        Equal(500, target.m_WAbil.HP,
            $"resolver state{immunityState} HP unchanged");
        Equal(123, target.m_WAbil.MP,
            $"resolver state{immunityState} MP unchanged");
    }

    target = NewDamageTarget(500, 500, 123);
    Assert(target.SetNativeActiveState(63), "resolver state63 setup");
    Equal(41, ResolveFullMagicDamage(target, source, 22, 1, 81),
        "resolver state63 odd damage");
    Equal(459, target.m_WAbil.HP, "resolver state63 HP");

    target = NewDamageTarget(500, 500, 150);
    target.m_boMagicShield = true;
    Equal(1, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "standard shield full absorb return floor");
    Equal(500, target.m_WAbil.HP, "standard shield full absorb HP");
    Equal(0, target.m_WAbil.MP, "standard shield full absorb MP");

    target = NewDamageTarget(500, 500, 75);
    target.m_boMagicShield = true;
    Equal(50, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "standard shield partial absorb");
    Equal(450, target.m_WAbil.HP, "standard shield partial HP");
    Equal(0, target.m_WAbil.MP, "standard shield partial MP");

    target = NewDamageTarget(500, 500, 4);
    target.m_boMagicShield = true;
    Equal(1, ResolveFullMagicDamage(target, source, 22, 1, 3),
        "standard shield x87 ties-to-even");
    Equal(500, target.m_WAbil.HP, "ties-to-even full absorb HP");
    Equal(0, target.m_WAbil.MP, "ties-to-even full absorb MP");

    target = NewDamageTarget(500, 500, 123);
    Equal(25, ResolveFullMagicDamage(target, source, 22, 1, 25),
        "non-player callback damage");
    var callback = target.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_STRUCK);
    Equal(25, callback.wParam, "RM_STRUCK wParam");
    Equal(25, callback.nParam1, "RM_STRUCK nParam1");
    Equal(0, callback.nParam2, "RM_STRUCK nParam2");
    Equal(source.ObjectId, callback.nParam3, "RM_STRUCK source id");
    Assert(ReferenceEquals(target, callback.BaseObject),
        "RM_STRUCK target sender");

    target = NewDamageTarget(500, 500, 123);
    Equal(25, ResolveFullMagicDamage(target, source, 22, 4, 25),
        "category4 landing damage");
    Equal(0, target.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_STRUCK),
        "category4 suppresses RM_STRUCK callback");

    var playerTarget = NewPlayer("earthfire-player-callback", playerMap);
    playerTarget.m_WAbil.HP = 500;
    playerTarget.m_WAbil.MaxHP = 500;
    playerTarget.m_WAbil.MP = 100;
    playerTarget.m_WAbil.MaxMP = 100;
    playerTarget.m_WAbil.MAC = 0;
    Equal(25, ResolveFullMagicDamage(playerTarget, source, 22, 1, 25),
        "player landing damage");
    Equal(0, playerTarget.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_STRUCK),
        "player suppresses RM_STRUCK callback");
}

static void CheckNativeLandingModifiers()
{
    (ushort Id, ushort Value)[] standardProperties =
    {
        (72, 1),
        (141, 250),
        (254, 0),
        (73, 1),
        (141, 10),
        (254, 4)
    };
    (ushort Id, ushort Value)[] instanceProperties =
    {
        (72, 1),
        (73, 1),
        (141, 77),
        (254, 0),
        (254, 4),
        (254, 3)
    };
    var mapped = ApplyNativeEffectItemParameters(standardProperties,
        instanceProperties);
    Equal((byte)4, mapped.NativeMagicDamageReductionPercent,
        "property141 unchecked byte accumulation");
    Assert(mapped.NativeFullMagicShield, "property73 full shield mapping");
    Assert(mapped.NativeStandardMagicShield,
        "property72 standard shield mapping");
    Assert(mapped.NativeHalfMagicShield,
        "property254 selector0 half shield mapping");
    Assert(mapped.NativeUserMove,
        "property254 selector4 UserMove mapping");
    Assert(!mapped.NativeSearchHuman,
        "property254 selector4 must not grant SearchHuman");

    var rawOnly = ApplyNativeEffectItemParameters(
        Array.Empty<(ushort Id, ushort Value)>(), instanceProperties);
    Equal((byte)0, rawOnly.NativeMagicDamageReductionPercent,
        "embedded user-item extension pairs ignored");
    Assert(!rawOnly.NativeFullMagicShield &&
           !rawOnly.NativeStandardMagicShield &&
           !rawOnly.NativeHalfMagicShield &&
           !rawOnly.NativeUserMove &&
           !rawOnly.NativeSearchHuman,
        "embedded user-item extension flags ignored");

    var recalc = RecalcNativeLandingItem(15, 0, Grobal2.U_HELMET,
        standardProperties, instanceProperties);
    Equal((byte)4,
        GetField<byte>(recalc, "m_btNativeMagicDamageReductionPercent"),
        "native landing properties recalc percentage");
    Assert(GetField<bool>(recalc, "m_boNativeFullMagicShield"),
        "native landing properties recalc full shield");
    Assert(recalc.m_boMagicShield,
        "native landing properties recalc standard shield");
    Assert(GetField<bool>(recalc, "m_boNativeHalfMagicShield"),
        "native landing properties recalc half shield");
    Assert(GetField<bool>(recalc, "m_boNativeUserMove"),
        "native landing properties recalc UserMove");
    Assert(!recalc.m_boProbeNecklace,
        "selector4 must not grant SearchHuman");
    recalc.m_UseItems[Grobal2.U_HELMET] = null;
    recalc.RecalcAbilitys();
    Equal((byte)0,
        GetField<byte>(recalc, "m_btNativeMagicDamageReductionPercent"),
        "native landing properties recalc percentage reset");
    Assert(!GetField<bool>(recalc, "m_boNativeFullMagicShield"),
        "native landing properties recalc full shield reset");
    Assert(!recalc.m_boMagicShield,
        "native landing properties recalc standard shield reset");
    Assert(!GetField<bool>(recalc, "m_boNativeHalfMagicShield"),
        "native landing properties recalc half shield reset");
    Assert(!GetField<bool>(recalc, "m_boNativeUserMove"),
        "native landing properties recalc UserMove reset");
    Assert(!recalc.m_boProbeNecklace,
        "native landing properties recalc SearchHuman reset");

    var searchSelector = ApplyNativeEffectItemParameters(
        new[] { ((ushort)254, (ushort)3) },
        Array.Empty<(ushort Id, ushort Value)>());
    Assert(searchSelector.NativeSearchHuman,
        "property254 selector3 SearchHuman mapping");
    Assert(!searchSelector.NativeUserMove,
        "property254 selector3 must not grant UserMove");

    var searchRecalc = RecalcNativeLandingItem(15, 0, Grobal2.U_HELMET,
        new[] { ((ushort)254, (ushort)3) },
        Array.Empty<(ushort Id, ushort Value)>());
    Assert(searchRecalc.m_boProbeNecklace,
        "property254 selector3 SearchHuman recalc publish");
    Assert(!GetField<bool>(searchRecalc, "m_boNativeUserMove"),
        "property254 selector3 UserMove isolation");
    searchRecalc.m_UseItems[Grobal2.U_HELMET] = null;
    searchRecalc.RecalcAbilitys();
    Assert(!searchRecalc.m_boProbeNecklace,
        "property254 selector3 SearchHuman recalc reset");
    Assert(!GetField<bool>(searchRecalc, "m_boNativeUserMove"),
        "property254 selector3 UserMove reset isolation");

    var maskedSelector = ApplyNativeEffectItemParameters(
        new[] { ((ushort)254, (ushort)0x80) },
        Array.Empty<(ushort Id, ushort Value)>());
    Assert(maskedSelector.NativeHalfMagicShield,
        "property254 bit7 selector mask");
    var maskedSearchSelector = ApplyNativeEffectItemParameters(
        new[] { ((ushort)254, (ushort)0x83) },
        Array.Empty<(ushort Id, ushort Value)>());
    Assert(maskedSearchSelector.NativeSearchHuman,
        "property254 SearchHuman bit7 selector mask");
    Assert(!maskedSearchSelector.NativeUserMove,
        "property254 masked SearchHuman must not grant UserMove");
    var maskedUserMoveSelector = ApplyNativeEffectItemParameters(
        new[] { ((ushort)254, (ushort)0x84) },
        Array.Empty<(ushort Id, ushort Value)>());
    Assert(maskedUserMoveSelector.NativeUserMove,
        "property254 UserMove bit7 selector mask");
    Assert(!maskedUserMoveSelector.NativeSearchHuman,
        "property254 masked UserMove must not grant SearchHuman");
    var wrongSelector = ApplyNativeEffectItemParameters(
        new[] { ((ushort)254, (ushort)1) },
        Array.Empty<(ushort Id, ushort Value)>());
    Assert(!wrongSelector.NativeHalfMagicShield,
        "property254 nonzero selector gate");
    Assert(!wrongSelector.NativeUserMove,
        "property254 wrong UserMove selector gate");
    Assert(!wrongSelector.NativeSearchHuman,
        "property254 wrong SearchHuman selector gate");
    var lowByteValue = ApplyNativeEffectItemParameters(
        new[] { ((ushort)141, (ushort)0x101) },
        Array.Empty<(ushort Id, ushort Value)>());
    Equal((byte)1, lowByteValue.NativeMagicDamageReductionPercent,
        "property141 value low-byte coercion");

    var source = new HostileSource();
    var target = NewDamageTarget(500, 500, 0);
    SetField(target, "m_btNativeMagicDamageReductionPercent",
        mapped.NativeMagicDamageReductionPercent);
    Equal(96, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "property141 mapped overflow reaches resolver");

    target = NewDamageTarget(500, 500, 0);
    SetField(target, "m_btNativeMagicDamageReductionPercent", (byte)1);
    Equal(99, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "property141 lower active boundary");

    target = NewDamageTarget(500, 500, 0);
    SetField(target, "m_btNativeMagicDamageReductionPercent", (byte)99);
    Equal(1, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "property141 upper active boundary");
    Equal(499, target.m_WAbil.HP,
        "property141 upper active boundary HP");

    target = NewDamageTarget(500, 500, 0);
    SetField(target, "m_btNativeMagicDamageReductionPercent", (byte)100);
    Equal(100, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "property141 value100 inactive");

    target = NewDamageTarget(500, 500, 0);
    Assert(target.SetNativeActiveState(63),
        "property141 state63 ordering setup");
    SetField(target, "m_btNativeMagicDamageReductionPercent", (byte)34);
    Equal(2, ResolveFullMagicDamage(target, source, 22, 1, 3),
        "state63 precedes property141 reduction");

    target = NewDamageTarget(int.MaxValue, int.MaxValue, 0);
    SetField(target, "m_btNativeMagicDamageReductionPercent", (byte)50);
    Equal(int.MaxValue,
        ResolveFullMagicDamage(target, source, 22, 1, int.MaxValue),
        "property141 unchecked int32 multiplication");
    Equal(0, target.m_WAbil.HP,
        "property141 unchecked int32 landing HP");

    target = NewDamageTarget(500, 500, 100);
    SetField(target, "m_boNativeFullMagicShield",
        mapped.NativeFullMagicShield);
    Equal(1, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "property73 full absorb return floor");
    Equal(500, target.m_WAbil.HP, "property73 full absorb HP");
    Equal(0, target.m_WAbil.MP, "property73 full absorb MP");

    target = NewDamageTarget(500, 500, 40);
    SetField(target, "m_boNativeFullMagicShield", true);
    Equal(60, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "property73 insufficient MP residual");
    Equal(440, target.m_WAbil.HP, "property73 insufficient MP HP");
    Equal(0, target.m_WAbil.MP, "property73 insufficient MP drain");

    target = NewDamageTarget(500, 500, 0);
    SetField(target, "m_boNativeFullMagicShield", true);
    Equal(100, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "property73 zero MP skips shield");
    Equal(400, target.m_WAbil.HP, "property73 zero MP HP");

    target = NewDamageTarget(500, 500, 4);
    target.m_boMagicShield = mapped.NativeStandardMagicShield;
    Equal(1, ResolveFullMagicDamage(target, source, 22, 1, 3),
        "property72 standard shield ties-to-even");
    Equal(500, target.m_WAbil.HP,
        "property72 ties-to-even full absorb HP");
    Equal(0, target.m_WAbil.MP,
        "property72 ties-to-even full absorb MP");

    target = NewDamageTarget(500, 500, 75);
    target.m_boMagicShield = true;
    Equal(50, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "property72 insufficient MP residual");
    Equal(450, target.m_WAbil.HP, "property72 insufficient MP HP");
    Equal(0, target.m_WAbil.MP, "property72 insufficient MP drain");

    target = NewDamageTarget(500, 500, 10);
    SetField(target, "m_boNativeHalfMagicShield",
        mapped.NativeHalfMagicShield);
    Equal(3, ResolveFullMagicDamage(target, source, 22, 1, 5),
        "property254 half shield odd damage");
    Equal(497, target.m_WAbil.HP, "property254 half shield HP");
    Equal(8, target.m_WAbil.MP, "property254 half shield MP");

    target = NewDamageTarget(500, 500, 1);
    SetField(target, "m_boNativeHalfMagicShield", true);
    Equal(4, ResolveFullMagicDamage(target, source, 22, 1, 5),
        "property254 insufficient MP residual");
    Equal(496, target.m_WAbil.HP, "property254 insufficient MP HP");
    Equal(0, target.m_WAbil.MP, "property254 insufficient MP drain");

    target = NewDamageTarget(500, 500, 60);
    SetField(target, "m_boNativeFullMagicShield", true);
    target.m_boMagicShield = true;
    SetField(target, "m_boNativeHalfMagicShield", true);
    Equal(40, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "shield priority full over standard and half");
    Equal(460, target.m_WAbil.HP, "full shield priority HP");
    Equal(0, target.m_WAbil.MP, "full shield priority MP");

    target = NewDamageTarget(500, 500, 60);
    target.m_boMagicShield = true;
    SetField(target, "m_boNativeHalfMagicShield", true);
    Equal(60, ResolveFullMagicDamage(target, source, 22, 1, 100),
        "shield priority standard over half");
    Equal(440, target.m_WAbil.HP, "standard shield priority HP");
    Equal(0, target.m_WAbil.MP, "standard shield priority MP");

    target = NewDamageTarget(480, 500, 10);
    SetField(target, "m_boNativeFullMagicShield", true);
    Equal(1, ApplyStandardEarthFireLanding(target, -20),
        "nonpositive landing return floor");
    Equal(500, target.m_WAbil.HP, "nonpositive landing healing");
    Equal(10, target.m_WAbil.MP, "nonpositive landing skips shields");
}

static void CheckHealthSpellDirtyCadence()
{
    var player = NewPlayer("earthfire-health-dirty", NewMap(16, 16));
    CheckHealthSpellDirtyActor(player, "dwTick57C",
        tick => RunNativeHealthSpellDirty(player, tick), "player");

    var hero = new HeroObject();
    CheckHealthSpellDirtyActor(hero, "m_dwNativeHealthSpellDirtyTick",
        tick => RunHeroNativeHealthSpellDirty(hero, tick), "hero");

    var master = NewPlayer("hero-health-master", NewMap(16, 16));
    hero.m_Master = master;
    Assert(hero.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_HEALTHSPELLCHANGED,
        BaseObject = hero.ObjectId
    }), "hero health-spell Operate dispatch");
}

static void CheckHealthSpellDirtyActor(TBaseObject actor, string tickField,
    Action<int> run, string label)
{
    SetDamageAbility(actor, 500, 500, 100);
    actor.m_boDeath = false;
    actor.m_MsgList.Clear();
    SetField(actor, tickField, 10_000);
    SetField(actor, "m_boNativeHealthSpellDirty", true);

    run(10_500);
    Assert(GetField<bool>(actor, "m_boNativeHealthSpellDirty"),
        label + " health dirty retained at 500ms");
    Equal(10_000, GetField<int>(actor, tickField),
        label + " health tick retained at 500ms");
    Equal(0, actor.m_MsgList.Count,
        label + " health dirty not queued at 500ms");

    run(10_501);
    Assert(!GetField<bool>(actor, "m_boNativeHealthSpellDirty"),
        label + " health dirty cleared at 501ms");
    Equal(10_501, GetField<int>(actor, tickField),
        label + " health dirty cadence tick");
    var message = actor.m_MsgList.Single();
    Equal(Grobal2.RM_HEALTHSPELLCHANGED, message.wIdent,
        label + " health dirty queued ident");
    Equal(0, message.wParam, label + " health dirty wParam");
    Equal(0, message.nParam1, label + " health dirty nParam1");
    Equal(0, message.nParam2, label + " health dirty nParam2");
    Equal(0, message.nParam3, label + " health dirty nParam3");
    Assert(ReferenceEquals(actor, message.BaseObject),
        label + " health dirty self sender");

    actor.m_MsgList.Clear();
    SetField(actor, tickField, 20_000);
    SetField(actor, "m_boNativeHealthSpellDirty", true);
    actor.m_boDeath = true;
    run(20_501);
    Equal(20_000, GetField<int>(actor, tickField),
        label + " dead health dirty tick freeze");
    Assert(GetField<bool>(actor, "m_boNativeHealthSpellDirty"),
        label + " dead health dirty retained");
    Equal(0, actor.m_MsgList.Count,
        label + " dead health dirty queue");
    actor.m_boDeath = false;
    run(20_501);
    Equal(20_501, GetField<int>(actor, tickField),
        label + " revived same-tick health cadence");
    Assert(!GetField<bool>(actor, "m_boNativeHealthSpellDirty"),
        label + " revived same-tick health dirty clear");
    Equal(1, actor.m_MsgList.Count,
        label + " revived same-tick health queue");

    actor.m_MsgList.Clear();
    SetField(actor, tickField, 30_000);
    SetField(actor, "m_boNativeHealthSpellDirty", true);
    actor.m_WAbil.HP = 0;
    run(30_501);
    Equal(30_000, GetField<int>(actor, tickField),
        label + " zero-HP health dirty tick freeze");
    Assert(GetField<bool>(actor, "m_boNativeHealthSpellDirty"),
        label + " zero-HP health dirty retained");
    Equal(0, actor.m_MsgList.Count,
        label + " zero-HP health dirty queue");
    actor.m_WAbil.HP = 1;
    run(30_501);
    Equal(30_501, GetField<int>(actor, tickField),
        label + " restored-HP same-tick health cadence");
    Assert(!GetField<bool>(actor, "m_boNativeHealthSpellDirty"),
        label + " restored-HP same-tick health dirty clear");
    Equal(1, actor.m_MsgList.Count,
        label + " restored-HP same-tick health queue");

    actor.m_MsgList.Clear();
    actor.m_WAbil.HP = 1;
    SetField(actor, tickField, 40_000);
    SetField(actor, "m_boNativeHealthSpellDirty", false);
    run(40_501);
    Equal(40_501, GetField<int>(actor, tickField),
        label + " clean health cadence tick");
    Equal(0, actor.m_MsgList.Count,
        label + " clean health cadence queue");

    SetField(actor, tickField, int.MaxValue - 100);
    SetField(actor, "m_boNativeHealthSpellDirty", true);
    run(unchecked(int.MaxValue - 100 + 501));
    Assert(!GetField<bool>(actor, "m_boNativeHealthSpellDirty"),
        label + " health dirty wraparound clear");
    Equal(1, actor.m_MsgList.Count,
        label + " health dirty wraparound queue");
}

static void CheckNativeShieldShapeSources()
{
    (byte StdMode, byte Shape, int Slot, bool Standard, bool Full, bool Half,
        string Label)[] cases =
    {
        (22, 118, Grobal2.U_RINGL, true, false, false, "ring shape118"),
        (23, 206, Grobal2.U_RINGR, true, false, false, "ring shape206"),
        (24, 118, Grobal2.U_ARMRINGL, true, false, false,
            "armring shape118"),
        (26, 206, Grobal2.U_ARMRINGR, true, false, false,
            "armring shape206"),
        (30, 201, Grobal2.U_RIGHTHAND, true, false, false,
            "right weapon shape201"),
        (22, 125, Grobal2.U_RINGL, false, true, false, "ring shape125"),
        (23, 208, Grobal2.U_RINGR, false, true, false, "ring shape208"),
        (24, 125, Grobal2.U_ARMRINGL, false, true, false,
            "armring shape125"),
        (26, 208, Grobal2.U_ARMRINGR, false, true, false,
            "armring shape208"),
        (22, 121, Grobal2.U_RINGL, false, false, true, "ring shape121"),
        (23, 207, Grobal2.U_RINGR, false, false, true, "ring shape207"),
        (24, 207, Grobal2.U_ARMRINGL, false, false, true,
            "armring shape207"),
        (26, 209, Grobal2.U_ARMRINGR, false, false, true,
            "armring shape209"),
        (15, 118, Grobal2.U_HELMET, false, false, false,
            "helmet shape118 class gate"),
        (22, 201, Grobal2.U_RINGL, false, false, false,
            "ring shape201 class gate"),
        (30, 118, Grobal2.U_RIGHTHAND, false, false, false,
            "right weapon shape118 class gate"),
        (22, 209, Grobal2.U_RINGL, false, false, false,
            "ring shape209 class gate"),
        (24, 121, Grobal2.U_ARMRINGL, false, false, false,
            "armring shape121 class gate"),
        (15, 125, Grobal2.U_HELMET, false, false, false,
            "helmet shape125 class gate")
    };

    foreach (var itemCase in cases)
    {
        var player = RecalcNativeLandingItem(itemCase.StdMode,
            itemCase.Shape, itemCase.Slot,
            Array.Empty<(ushort Id, ushort Value)>(),
            Array.Empty<(ushort Id, ushort Value)>());
        Equal(itemCase.Standard, player.m_boMagicShield,
            itemCase.Label + " standard shield");
        Equal(itemCase.Full,
            GetField<bool>(player, "m_boNativeFullMagicShield"),
            itemCase.Label + " full shield");
        Equal(itemCase.Half,
            GetField<bool>(player, "m_boNativeHalfMagicShield"),
            itemCase.Label + " half shield");
    }

    foreach (var userMoveSource in new[]
             {
                 (StdMode: (byte)22, Slot: Grobal2.U_RINGL,
                     Label: "left ring shape112"),
                 (StdMode: (byte)23, Slot: Grobal2.U_RINGR,
                     Label: "right ring shape112")
             })
    {
        var player = RecalcNativeLandingItem(userMoveSource.StdMode, 112,
            userMoveSource.Slot,
            Array.Empty<(ushort Id, ushort Value)>(),
            Array.Empty<(ushort Id, ushort Value)>());
        Assert(GetField<bool>(player, "m_boNativeUserMove"),
            userMoveSource.Label + " native UserMove source");
        Assert(!player.m_boProbeNecklace,
            userMoveSource.Label + " must not grant SearchHuman");
        Assert(!GetField<bool>(player, "m_boNativeHalfMagicShield"),
            userMoveSource.Label + " must not grant half shield");
        Assert(!player.m_boTeleport,
            userMoveSource.Label + " must not grant teleport");
        player.m_UseItems[userMoveSource.Slot] = null;
        player.RecalcAbilitys();
        Assert(!GetField<bool>(player, "m_boNativeUserMove"),
            userMoveSource.Label + " UserMove removal reset");
        Assert(!player.m_boProbeNecklace,
            userMoveSource.Label + " SearchHuman removal isolation");
    }

    foreach (var searchHumanSource in new[]
             {
                 (StdMode: (byte)19, Label: "necklace mode19 shape121"),
                 (StdMode: (byte)20, Label: "necklace mode20 shape121"),
                 (StdMode: (byte)21, Label: "necklace mode21 shape121")
             })
    {
        var player = RecalcNativeLandingItem(searchHumanSource.StdMode, 121,
            Grobal2.U_NECKLACE,
            Array.Empty<(ushort Id, ushort Value)>(),
            Array.Empty<(ushort Id, ushort Value)>());
        Assert(player.m_boProbeNecklace,
            searchHumanSource.Label + " native SearchHuman source");
        Assert(!GetField<bool>(player, "m_boNativeUserMove"),
            searchHumanSource.Label + " must not grant UserMove");
        Assert(!GetField<bool>(player, "m_boNativeHalfMagicShield"),
            searchHumanSource.Label + " must not grant half shield");
        player.m_UseItems[Grobal2.U_NECKLACE] = null;
        player.RecalcAbilitys();
        Assert(!player.m_boProbeNecklace,
            searchHumanSource.Label + " SearchHuman removal reset");
        Assert(!GetField<bool>(player, "m_boNativeUserMove"),
            searchHumanSource.Label + " UserMove removal isolation");
    }

    foreach (var userMoveNegative in new[]
             {
                 (StdMode: (byte)24, Shape: (byte)112,
                     Slot: Grobal2.U_ARMRINGL, Label: "armring shape112"),
                 (StdMode: (byte)22, Shape: (byte)121,
                     Slot: Grobal2.U_RINGL, Label: "ring shape121"),
                 (StdMode: (byte)22, Shape: (byte)159,
                     Slot: Grobal2.U_RINGL, Label: "ring shape159"),
                 (StdMode: (byte)19, Shape: (byte)112,
                     Slot: Grobal2.U_NECKLACE, Label: "necklace shape112"),
                 (StdMode: (byte)19, Shape: (byte)159,
                     Slot: Grobal2.U_NECKLACE, Label: "necklace shape159")
             })
    {
        var player = RecalcNativeLandingItem(userMoveNegative.StdMode,
            userMoveNegative.Shape, userMoveNegative.Slot,
            Array.Empty<(ushort Id, ushort Value)>(),
            Array.Empty<(ushort Id, ushort Value)>());
        Assert(!GetField<bool>(player, "m_boNativeUserMove"),
            userMoveNegative.Label + " must not grant native UserMove");
        Assert(!player.m_boProbeNecklace,
            userMoveNegative.Label + " must not grant SearchHuman");
    }
}

static void CheckNativeSearchAndUserMoveCommands()
{
    var userMoveRegistration = typeof(UserMoveXYCommand)
        .GetCustomAttribute<GameSvr.CommandSystem.GameCommandAttribute>()
        ?? throw new MissingMemberException(typeof(UserMoveXYCommand).FullName,
            "GameCommandAttribute");
    var searchRegistration = typeof(SearchHumanCommand)
        .GetCustomAttribute<GameSvr.CommandSystem.GameCommandAttribute>()
        ?? throw new MissingMemberException(typeof(SearchHumanCommand).FullName,
            "GameCommandAttribute");
    // Native registry ShortString is the source name (pre-FormGMCommand.ini).
    // 0x007B65D4 `05 67 6f 77 67 6f` "gowgo"；0x007B66F4 `09 53 65 61 72 63 68 69 6e 67`
    // "Searching". "UserMoveXY" / "SearchHuman" / "UserMove" 全镜像 GBK+UTF8+UTF16LE 0 命中。
    // GetCustomAttribute 读的是类型上的 [GameCommand]，不是 ApplyNativeFormGmCommandIni
    // 改过的运行时 hash 键（生产 ini 把 idx 29 改成 sdgo 发生在 Add 之前，见 0x622575 jl）。
    Equal("gowgo", userMoveRegistration.Name,
        "UserMove registration source name");
    Equal("Searching", searchRegistration.Name,
        "SearchHuman registration source name");
    Assert(!string.Equals("UserMoveXY", userMoveRegistration.Name,
            StringComparison.OrdinalIgnoreCase),
        "UserMoveXY is not a native registry name");
    Assert(!string.Equals("UserMove", userMoveRegistration.Name,
            StringComparison.OrdinalIgnoreCase),
        "UserMove is not a native registry name");
    Assert(!string.Equals("SearchHuman", searchRegistration.Name,
            StringComparison.OrdinalIgnoreCase),
        "SearchHuman is not a native registry name");

    var overlay = typeof(GameSvr.CommandSystem.CommandManager).GetMethod(
        "ApplyNativeFormGmCommandIni",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            typeof(GameSvr.CommandSystem.CommandManager).FullName,
            "ApplyNativeFormGmCommandIni");
    Assert(overlay.IsStatic, "FormGMCommand.ini overlay is a post-register pass");

    var cooldown = typeof(SearchHumanCommand).GetMethod(
        "HasNativeSearchCooldownElapsed",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(SearchHumanCommand).FullName,
            "HasNativeSearchCooldownElapsed");
    bool Elapsed(int currentTick, int previousTick) =>
        (bool)(cooldown.Invoke(null, new object[] { currentTick, previousTick })
            ?? false);

    Assert(!Elapsed(10_000, 0),
        "SearchHuman cooldown must reject exactly 10000ms");
    Assert(Elapsed(10_001, 0),
        "SearchHuman cooldown must accept 10001ms");
    var wrapPrevious = int.MaxValue - 5_000;
    Assert(!Elapsed(unchecked(wrapPrevious + 10_000), wrapPrevious),
        "SearchHuman wrap cooldown exact boundary");
    Assert(Elapsed(unchecked(wrapPrevious + 10_001), wrapPrevious),
        "SearchHuman wrap cooldown elapsed boundary");

    var userMoveCooldown = typeof(TPlayObject).GetMethod(
        "HasNativeUserMoveCooldownElapsed",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TPlayObject).FullName,
            "HasNativeUserMoveCooldownElapsed");
    bool UserMoveElapsed(int currentTick, int previousTick) =>
        (bool)(userMoveCooldown.Invoke(null,
            new object[] { currentTick, previousTick }) ?? false);

    Assert(!UserMoveElapsed(10_000, 0),
        "UserMove cooldown must reject exactly 10000ms");
    Assert(UserMoveElapsed(10_001, 0),
        "UserMove cooldown must accept 10001ms");
    Assert(!UserMoveElapsed(unchecked(wrapPrevious + 10_000),
            wrapPrevious),
        "UserMove wrap cooldown exact boundary");
    Assert(UserMoveElapsed(unchecked(wrapPrevious + 10_001),
            wrapPrevious),
        "UserMove wrap cooldown elapsed boundary");

    var originalEngine = M2Share.UserEngine;
    var originalMapManager = M2Share.MapManager;
    var originalUserMoveTime = M2Share.g_Config.dwUserMoveTime;
    var originalShowPrefix = M2Share.g_Config.boShowPreFixMsg;
    var originalHintPrefix = M2Share.g_Config.sHintMsgPreFix;
    var originalGreenForeground = M2Share.g_Config.btGreenMsgFColor;
    var originalGreenBackground = M2Share.g_Config.btGreenMsgBColor;
    var originalBlueForeground = M2Share.g_Config.btBlueMsgFColor;
    var originalBlueBackground = M2Share.g_Config.btBlueMsgBColor;
    var originalRedForeground = M2Share.g_Config.btRedMsgFColor;
    var originalRedBackground = M2Share.g_Config.btRedMsgBColor;
    try
    {
        M2Share.UserEngine = new UserEngine();
        M2Share.MapManager = new MapManager();
        M2Share.g_Config.boShowPreFixMsg = true;
        M2Share.g_Config.sHintMsgPreFix = "[audit-prefix]";
        M2Share.g_Config.btGreenMsgFColor = 1;
        M2Share.g_Config.btGreenMsgBColor = 2;
        M2Share.g_Config.btBlueMsgFColor = 3;
        M2Share.g_Config.btBlueMsgBColor = 4;
        M2Share.g_Config.btRedMsgFColor = 5;
        M2Share.g_Config.btRedMsgBColor = 6;
        var players = GetField<IList<TPlayObject>>(M2Share.UserEngine,
            "m_PlayObjectList");
        var localMap = NewMap(64, 64);
        localMap.sMapName = "AUDIT_LOCAL";
        localMap.sMapDesc = "audit-local";
        var remoteMap = NewMap(64, 64);
        remoteMap.sMapName = "AUDIT_REMOTE";
        remoteMap.sMapDesc = "audit-remote";
        RegisterMap(localMap);
        RegisterMap(remoteMap);

        var caller = NewPlayer("search-caller", localMap);
        caller.m_nCurrX = 7;
        caller.m_nCurrY = 9;
        var localTarget = NewPlayer("search-local", localMap);
        localTarget.m_nCurrX = 37;
        localTarget.m_nCurrY = 41;
        localTarget.m_boReadyRun = true;
        var remoteTarget = NewPlayer("search-remote", remoteMap);
        remoteTarget.m_nCurrX = 51;
        remoteTarget.m_nCurrY = 53;
        remoteTarget.m_boReadyRun = true;
        var unreadySearchTarget = NewPlayer("search-unready", localMap);
        unreadySearchTarget.m_boReadyRun = false;
        var ghostSearchTarget = NewPlayer("search-ghost", localMap);
        ghostSearchTarget.m_boReadyRun = true;
        ghostSearchTarget.m_boGhost = true;
        players.Add(localTarget);
        players.Add(remoteTarget);
        players.Add(unreadySearchTarget);
        players.Add(ghostSearchTarget);

        var search = new SearchHumanCommand();
        caller.m_boProbeNecklace = true;
        caller.m_dwProbeTick = unchecked(HUtil32.GetTickCount() - 10_001);
        var unchangedTick = caller.m_dwProbeTick;
        search.SearchHuman(Array.Empty<string>(), caller);
        Equal(0, caller.m_MsgList.Count,
            "SearchHuman empty argument must be silent");
        Equal(unchangedTick, caller.m_dwProbeTick,
            "SearchHuman empty argument consumed cooldown");

        caller.m_boProbeNecklace = false;
        caller.m_btPermission = 2;
        caller.m_dwProbeTick = unchecked(HUtil32.GetTickCount() - 10_001);
        unchangedTick = caller.m_dwProbeTick;
        search.SearchHuman(new[] { localTarget.m_sCharName }, caller);
        Equal(0, caller.m_MsgList.Count,
            "SearchHuman no-permission branch must be silent");
        Equal(unchangedTick, caller.m_dwProbeTick,
            "SearchHuman no-permission branch consumed cooldown");

        caller.m_btPermission = 3;
        caller.m_dwProbeTick = HUtil32.GetTickCount();
        unchangedTick = caller.m_dwProbeTick;
        search.SearchHuman(new[] { localTarget.m_sCharName }, caller);
        Equal(0, caller.m_MsgList.Count,
            "SearchHuman GM cooldown branch must be silent");
        Equal(unchangedTick, caller.m_dwProbeTick,
            "SearchHuman GM bypassed native cooldown");

        caller.m_btPermission = 0;
        caller.m_boProbeNecklace = true;
        caller.m_dwProbeTick = unchecked(HUtil32.GetTickCount() - 10_001);
        search.SearchHuman(new[] { localTarget.m_sCharName }, caller);
        Equal("search-local 在本地图：37,41 的位置上",
            TakeSingleSystemMessage(caller, "SearchHuman same-map result",
                0xDB, 0xFF),
            "SearchHuman same-map target coordinates");

        caller.ClearNotifications();
        caller.m_dwProbeTick = unchecked(HUtil32.GetTickCount() - 10_001);
        search.SearchHuman(new[] { remoteTarget.m_sCharName }, caller);
        Equal("search-remote 在其他地图上",
            TakeSingleSystemMessage(caller, "SearchHuman other-map result",
                0xDB, 0xFF),
            "SearchHuman other-map branch");

        caller.ClearNotifications();
        caller.m_dwProbeTick = unchecked(HUtil32.GetTickCount() - 10_001);
        search.SearchHuman(new[] { "search-missing" }, caller);
        Equal("探测项链无法查出 search-missing 所在的位置",
            TakeSingleSystemMessage(caller, "SearchHuman missing result",
                0xDB, 0xFF),
            "SearchHuman missing-target branch");

        caller.ClearNotifications();
        caller.m_dwProbeTick = unchecked(HUtil32.GetTickCount() - 10_001);
        search.SearchHuman(new[] { unreadySearchTarget.m_sCharName }, caller);
        Equal("探测项链无法查出 search-unready 所在的位置",
            TakeSingleSystemMessage(caller,
                "SearchHuman not-ready result", 0xDB, 0xFF),
            "SearchHuman not-ready target must use missing result");

        caller.ClearNotifications();
        caller.m_dwProbeTick = unchecked(HUtil32.GetTickCount() - 10_001);
        search.SearchHuman(new[] { ghostSearchTarget.m_sCharName }, caller);
        Equal("探测项链无法查出 search-ghost 所在的位置",
            TakeSingleSystemMessage(caller, "SearchHuman ghost result",
                0xDB, 0xFF),
            "SearchHuman ghost target must use missing result");

        var moveCommand = new UserMoveXYCommand();

        var gmThree = NewPlayer("usermove-gm-three", localMap);
        gmThree.m_sMapName = localMap.sMapName;
        gmThree.m_btPermission = 2;
        gmThree.m_dwTeleportTick = 0x23456789;
        Place(localMap, gmThree, 3, 3);
        localMap.Flag.boNOPOSITIONMOVE = true;
        moveCommand.UserMoveXY(new[]
        {
            remoteMap.sMapName, "17", "19"
        }, gmThree);
        Assert(ReferenceEquals(remoteMap, gmThree.m_PEnvir),
            "UserMove GM three-token map");
        Equal((short)17, gmThree.m_nCurrX,
            "UserMove GM three-token X");
        Equal((short)19, gmThree.m_nCurrY,
            "UserMove GM three-token Y");
        Equal(0x23456789, gmThree.m_dwTeleportTick,
            "UserMove GM three-token cooldown bypass");
        Assert(!GetField<bool>(gmThree, "m_boNativeUserMove"),
            "UserMove GM three-token item-permission fixture");
        Assert(GetField<Envirnoment>(gmThree,
                "m_NativeUserMoveEnvir") == null,
            "UserMove GM three-token saved environment");
        Assert(gmThree.m_MsgList.All(message =>
                message.wIdent != Grobal2.RM_USERMOVE &&
                message.wIdent != Grobal2.RM_SPACEMOVE_FIRE),
            "UserMove GM three-token ordinary-chain messages");

        var gmTwo = NewPlayer("usermove-gm-two", localMap);
        gmTwo.m_sMapName = localMap.sMapName;
        gmTwo.m_btPermission = 2;
        Place(localMap, gmTwo, 5, 5);
        moveCommand.UserMoveXY(new[] { "21", "23" }, gmTwo);
        Assert(ReferenceEquals(localMap, gmTwo.m_PEnvir),
            "UserMove GM two-token current map");
        Equal((short)21, gmTwo.m_nCurrX,
            "UserMove GM two-token X");
        Equal((short)23, gmTwo.m_nCurrY,
            "UserMove GM two-token Y");
        Assert(gmTwo.m_MsgList.All(message =>
                message.wIdent != Grobal2.RM_USERMOVE),
            "UserMove GM two-token delayed event");

        var gmMap = NewPlayer("usermove-gm-map", localMap);
        gmMap.m_sMapName = localMap.sMapName;
        gmMap.m_btPermission = 2;
        Place(localMap, gmMap, 7, 7);
        moveCommand.UserMoveXY(new[] { remoteMap.sMapName }, gmMap);
        Assert(ReferenceEquals(remoteMap, gmMap.m_PEnvir),
            "UserMove GM one-token random map");
        Assert(gmMap.m_nCurrX >= 0 &&
               gmMap.m_nCurrX < remoteMap.wWidth &&
               gmMap.m_nCurrY >= 0 &&
               gmMap.m_nCurrY < remoteMap.wHeight,
            "UserMove GM one-token random coordinates");
        Assert(gmMap.m_MsgList.All(message =>
                message.wIdent != Grobal2.RM_USERMOVE),
            "UserMove GM one-token map delayed event");

        var namedTarget = NewPlayer("usermove-named-target", remoteMap);
        namedTarget.m_sMapName = remoteMap.sMapName;
        namedTarget.m_btDirection = Grobal2.DR_RIGHT;
        namedTarget.m_boReadyRun = true;
        Place(remoteMap, namedTarget, 37, 41);
        players.Add(namedTarget);
        var gmTarget = NewPlayer("usermove-gm-target", localMap);
        gmTarget.m_sMapName = localMap.sMapName;
        gmTarget.m_btPermission = 2;
        Place(localMap, gmTarget, 9, 9);
        moveCommand.UserMoveXY(new[] { namedTarget.m_sCharName }, gmTarget);
        Assert(ReferenceEquals(remoteMap, gmTarget.m_PEnvir),
            "UserMove GM one-token player map");
        Equal((short)38, gmTarget.m_nCurrX,
            "UserMove GM one-token player-front X");
        Equal((short)41, gmTarget.m_nCurrY,
            "UserMove GM one-token player-front Y");
        Assert(gmTarget.m_MsgList.All(message =>
                message.wIdent != Grobal2.RM_USERMOVE),
            "UserMove GM one-token player delayed event");

        var unreadyTarget = NewPlayer("usermove-unready-target", remoteMap);
        unreadyTarget.m_boReadyRun = false;
        players.Add(unreadyTarget);
        var ghostTarget = NewPlayer("usermove-ghost-target", remoteMap);
        ghostTarget.m_boReadyRun = true;
        ghostTarget.m_boGhost = true;
        players.Add(ghostTarget);
        var gmGuard = NewPlayer("usermove-gm-target-guard", localMap);
        gmGuard.m_sMapName = localMap.sMapName;
        gmGuard.m_btPermission = 2;
        Place(localMap, gmGuard, 13, 13);
        moveCommand.UserMoveXY(new[] { unreadyTarget.m_sCharName }, gmGuard);
        moveCommand.UserMoveXY(new[] { ghostTarget.m_sCharName }, gmGuard);
        Assert(ReferenceEquals(localMap, gmGuard.m_PEnvir),
            "UserMove GM unready/ghost target map gate");
        Equal((short)13, gmGuard.m_nCurrX,
            "UserMove GM unready/ghost target X gate");
        Equal((short)13, gmGuard.m_nCurrY,
            "UserMove GM unready/ghost target Y gate");
        Equal(0, gmGuard.m_MsgList.Count,
            "UserMove GM unready/ghost target must be silent");

        var heroOnlyTarget = new HeroObject
        {
            m_sCharName = "usermove-hero-only-target",
            m_PEnvir = remoteMap
        };
        GetField<IList<HeroObject>>(M2Share.UserEngine,
            "m_HeroObjectList").Add(heroOnlyTarget);
        var gmHeroGuard = NewPlayer("usermove-gm-hero-guard", localMap);
        gmHeroGuard.m_sMapName = localMap.sMapName;
        gmHeroGuard.m_btPermission = 2;
        Place(localMap, gmHeroGuard, 15, 15);
        moveCommand.UserMoveXY(new[] { heroOnlyTarget.m_sCharName },
            gmHeroGuard);
        Assert(ReferenceEquals(localMap, gmHeroGuard.m_PEnvir),
            "UserMove GM must not resolve hero-only name");
        Equal((short)15, gmHeroGuard.m_nCurrX,
            "UserMove GM hero-only name X");
        Equal((short)15, gmHeroGuard.m_nCurrY,
            "UserMove GM hero-only name Y");
        Equal(0, gmHeroGuard.m_MsgList.Count,
            "UserMove GM hero-only name must be silent");

        var frontPosition = typeof(UserMoveXYCommand).GetMethod(
            "GetTargetFrontPosition",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(
                typeof(UserMoveXYCommand).FullName,
                "GetTargetFrontPosition");
        (short X, short Y) TargetFront(short x, short y, byte direction)
        {
            namedTarget.m_nCurrX = x;
            namedTarget.m_nCurrY = y;
            namedTarget.m_btDirection = direction;
            var arguments = new object[]
            {
                namedTarget, remoteMap, (short)0, (short)0
            };
            frontPosition.Invoke(null, arguments);
            return ((short)arguments[2], (short)arguments[3]);
        }

        Equal(((short)1, (short)20),
            TargetFront(1, 20, Grobal2.DR_LEFT),
            "UserMove GM player-front X boundary fallback");
        Equal(((short)1, (short)1),
            TargetFront(1, 1, Grobal2.DR_UPLEFT),
            "UserMove GM player-front dual-axis boundary fallback");
        Equal(((short)1, (short)19),
            TargetFront(1, 20, Grobal2.DR_UPLEFT),
            "UserMove GM player-front independent-axis fallback");

        var gmMissing = NewPlayer("usermove-gm-missing", localMap);
        gmMissing.m_sMapName = localMap.sMapName;
        gmMissing.m_btPermission = 2;
        Place(localMap, gmMissing, 11, 11);
        moveCommand.UserMoveXY(new[] { "no-such-map-or-player" },
            gmMissing);
        Assert(ReferenceEquals(localMap, gmMissing.m_PEnvir),
            "UserMove GM missing single-token map");
        Equal((short)11, gmMissing.m_nCurrX,
            "UserMove GM missing single-token X");
        Equal((short)11, gmMissing.m_nCurrY,
            "UserMove GM missing single-token Y");
        Equal(0, gmMissing.m_MsgList.Count,
            "UserMove GM missing single-token must be silent");
        localMap.Flag.boNOPOSITIONMOVE = false;

        var deniedMove = NewPlayer("usermove-denied", localMap);
        deniedMove.m_nCurrX = 11;
        deniedMove.m_nCurrY = 13;
        deniedMove.m_sMapName = localMap.sMapName;
        deniedMove.m_dwTeleportTick = 0x12345678;
        moveCommand.UserMoveXY(new[] { "17", "19" }, deniedMove);
        Equal((short)11, deniedMove.m_nCurrX,
            "ordinary no-permission UserMove X");
        Equal((short)13, deniedMove.m_nCurrY,
            "ordinary no-permission UserMove Y");
        Equal(0x12345678, deniedMove.m_dwTeleportTick,
            "ordinary no-permission UserMove cooldown");
        Equal(0, deniedMove.m_MsgList.Count,
            "ordinary no-permission UserMove must be silent");
        Assert(GetField<Envirnoment>(deniedMove,
                "m_NativeUserMoveEnvir") == null,
            "ordinary no-permission UserMove saved environment");

        var legacyMove = NewPlayer("usermove-legacy-only", localMap);
        legacyMove.m_nCurrX = 11;
        legacyMove.m_nCurrY = 13;
        legacyMove.m_sMapName = localMap.sMapName;
        legacyMove.m_boTeleport = true;
        SetField(legacyMove, "m_boNativeUserMove", false);
        var oldTeleportTick = legacyMove.m_dwTeleportTick;
        moveCommand.UserMoveXY(new[] { "17", "19" }, legacyMove);
        Equal((short)11, legacyMove.m_nCurrX,
            "legacy teleport flag granted native UserMove X");
        Equal((short)13, legacyMove.m_nCurrY,
            "legacy teleport flag granted native UserMove Y");
        Equal(oldTeleportTick, legacyMove.m_dwTeleportTick,
            "legacy teleport flag consumed UserMove cooldown");
        Assert(legacyMove.m_MsgList.All(message =>
                message.wIdent != Grobal2.RM_SPACEMOVE_FIRE),
            "legacy teleport flag emitted UserMove effect");

        var disabledMove = NewPlayer("usermove-map-disabled", localMap);
        disabledMove.m_nCurrX = 11;
        disabledMove.m_nCurrY = 13;
        disabledMove.m_sMapName = localMap.sMapName;
        SetField(disabledMove, "m_boNativeUserMove", true);
        disabledMove.m_dwTeleportTick = unchecked(
            HUtil32.GetTickCount() - 10_001);
        oldTeleportTick = disabledMove.m_dwTeleportTick;
        localMap.Flag.boNOPOSITIONMOVE = true;
        moveCommand.UserMoveXY(new[] { "17", "19" }, disabledMove);
        Equal("在这里您无法使用",
            TakeSingleSystemMessage(disabledMove,
                "UserMove map-disabled prompt", 0xFF, 0x38),
            "UserMove map-disabled prompt text");
        Equal(oldTeleportTick, disabledMove.m_dwTeleportTick,
            "UserMove map-disabled cooldown");
        Assert(GetField<Envirnoment>(disabledMove,
                "m_NativeUserMoveEnvir") == null,
            "UserMove map-disabled saved environment");
        Assert(disabledMove.m_MsgList.All(message =>
                message.wIdent != Grobal2.RM_USERMOVE),
            "UserMove map-disabled delayed event");
        localMap.Flag.boNOPOSITIONMOVE = false;

        var coolingMove = NewPlayer("usermove-cooling", localMap);
        coolingMove.m_nCurrX = 11;
        coolingMove.m_nCurrY = 13;
        coolingMove.m_sMapName = localMap.sMapName;
        SetField(coolingMove, "m_boNativeUserMove", true);
        coolingMove.m_dwTeleportTick = HUtil32.GetTickCount();
        oldTeleportTick = coolingMove.m_dwTeleportTick;
        moveCommand.UserMoveXY(new[] { "17", "19" }, coolingMove);
        Equal(oldTeleportTick, coolingMove.m_dwTeleportTick,
            "UserMove cooling branch timestamp");
        Assert(TakeSingleSystemMessage(coolingMove,
                    "UserMove cooling branch prompt", 0xFF, 0x38)
                .EndsWith(" 秒后方可使用", StringComparison.Ordinal),
            "UserMove cooling branch prompt text");
        Assert(coolingMove.m_MsgList.All(message =>
                message.wIdent != Grobal2.RM_USERMOVE),
            "UserMove cooling branch delayed event");

        var defaultMove = NewPlayer("usermove-default-zero", localMap);
        defaultMove.m_nCurrX = 11;
        defaultMove.m_nCurrY = 13;
        defaultMove.m_sMapName = localMap.sMapName;
        SetField(defaultMove, "m_boNativeUserMove", true);
        defaultMove.m_dwTeleportTick = unchecked(
            HUtil32.GetTickCount() - 10_001);
        moveCommand.UserMoveXY(Array.Empty<string>(), defaultMove);
        var defaultQueued = defaultMove.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_USERMOVE);
        Equal(0, defaultQueued.nParam1,
            "UserMove missing X must parse as zero");
        Equal(0, defaultQueued.nParam2,
            "UserMove missing Y must parse as zero");

        var wideMove = NewPlayer("usermove-int32", localMap);
        wideMove.m_sMapName = localMap.sMapName;
        SetField(wideMove, "m_boNativeUserMove", true);
        wideMove.m_dwTeleportTick = unchecked(
            HUtil32.GetTickCount() - 10_001);
        moveCommand.UserMoveXY(new[] { "65537", "-65535" }, wideMove);
        var wideQueued = wideMove.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_USERMOVE);
        Equal(65_537, wideQueued.nParam1,
            "UserMove queued X must preserve signed Int32");
        Equal(-65_535, wideQueued.nParam2,
            "UserMove queued Y must preserve signed Int32");

        var resolver = typeof(TPlayObject).GetMethod(
            "TryResolveNativeUserMoveCoordinates",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(TPlayObject).FullName,
                "TryResolveNativeUserMoveCoordinates");
        var wideResolveArgs = new object[] { localMap, 65_537, 17 };
        Assert((bool)(resolver.Invoke(null, wideResolveArgs) ?? false),
            "UserMove Int32 resolver result");
        var resolvedWideX = (int)wideResolveArgs[1];
        var resolvedWideY = (int)wideResolveArgs[2];
        Assert(resolvedWideX > 0 && resolvedWideX < localMap.wWidth &&
               resolvedWideY > 0 && resolvedWideY < localMap.wHeight &&
               resolvedWideX != 1,
            "UserMove Int32 resolver must not short-wrap 65537 to one");

        var wallMap = NewMap(8, 8);
        Array.Fill(GetField<CellAttribute[]>(wallMap,
            "MapCellAttributes"), CellAttribute.HighWall);
        wallMap.m_PointList = new List<PointInfo> { new(2, 3) };
        var pointResolveArgs = new object[] { wallMap, 1, 1 };
        Assert((bool)(resolver.Invoke(null, pointResolveArgs) ?? false),
            "UserMove PointList resolver result");
        Equal(2, (int)pointResolveArgs[1],
            "UserMove PointList fallback X");
        Equal(3, (int)pointResolveArgs[2],
            "UserMove PointList fallback Y");

        var stagedMove = NewPlayer("usermove-two-phase", localMap);
        stagedMove.m_sMapName = localMap.sMapName;
        SetField(stagedMove, "m_boNativeUserMove", true);
        Place(localMap, stagedMove, 11, 13);
        stagedMove.m_dwTeleportTick = unchecked(
            HUtil32.GetTickCount() - 10_001);
        M2Share.g_Config.dwUserMoveTime = 77;
        var commandStartTick = HUtil32.GetTickCount();
        moveCommand.UserMoveXY(new[] { "17", "19" }, stagedMove);
        var commandEndTick = HUtil32.GetTickCount();
        Equal((short)11, stagedMove.m_nCurrX,
            "UserMove command must not immediately move X");
        Equal((short)13, stagedMove.m_nCurrY,
            "UserMove command must not immediately move Y");
        Assert(ReferenceEquals(localMap,
                GetField<Envirnoment>(stagedMove,
                    "m_NativeUserMoveEnvir")),
            "UserMove command saved source environment");
        Assert(unchecked((uint)(stagedMove.m_dwTeleportTick -
                commandStartTick)) <= unchecked((uint)(commandEndTick -
                commandStartTick)),
            "UserMove command timestamp source");

        var queued = stagedMove.m_MsgList.Single();
        Equal(10056, Grobal2.RM_USERMOVE,
            "UserMove native internal ident");
        Equal(Grobal2.RM_USERMOVE, queued.wIdent,
            "UserMove queued ident");
        Equal(0, queued.wParam, "UserMove queued wParam");
        Equal(17, queued.nParam1, "UserMove queued X");
        Equal(19, queued.nParam2, "UserMove queued Y");
        Equal(0, queued.nParam3, "UserMove queued nParam3");
        Assert(ReferenceEquals(stagedMove, queued.BaseObject),
            "UserMove queued sender");
        Assert(queued.Payload == null, "UserMove queued payload");
        Assert(queued.boLateDelivery, "UserMove queued delayed flag");
        Assert(unchecked((uint)(queued.dwDeliveryTime -
                commandStartTick)) >= 1500U,
            "UserMove queued delay lower bound");
        Assert(unchecked((uint)(queued.dwDeliveryTime - commandEndTick))
                <= 1500U,
            "UserMove queued exact 1500ms delay");
        Assert(stagedMove.m_MsgList.All(message =>
                message.wIdent != Grobal2.RM_SPACEMOVE_FIRE),
            "UserMove command emitted legacy space-move effect");

        stagedMove.m_MsgList.Clear();
        stagedMove.Operate(Process(queued));
        Equal((short)17, stagedMove.m_nCurrX,
            "UserMove 10056 same-environment completion X");
        Equal((short)19, stagedMove.m_nCurrY,
            "UserMove 10056 same-environment completion Y");
        Assert(GetField<Envirnoment>(stagedMove,
                "m_NativeUserMoveEnvir") == null,
            "UserMove 10056 same-environment pending clear");
        Assert(stagedMove.m_MsgList.Any(message =>
                message.wIdent == Grobal2.RM_NATIVE_CLEAROBJECTS),
            "UserMove completion native clear-objects ident");
        Assert(stagedMove.m_MsgList.Any(message =>
                message.wIdent == Grobal2.RM_NATIVE_CHANGEMAP),
            "UserMove completion native change-map ident");
        Assert(stagedMove.m_MsgList.All(message =>
                message.wIdent != Grobal2.RM_CLEAROBJECTS &&
                message.wIdent != Grobal2.RM_CHANGEMAP),
            "UserMove completion must not use legacy move idents");
        Assert(stagedMove.m_MsgList.All(message =>
                message.wIdent != Grobal2.RM_SPACEMOVE_FIRE),
            "UserMove 10056 emitted legacy space-move effect");

        var cancelledMove = NewPlayer("usermove-map-change", localMap);
        cancelledMove.m_sMapName = localMap.sMapName;
        SetField(cancelledMove, "m_boNativeUserMove", true);
        Place(localMap, cancelledMove, 23, 25);
        cancelledMove.m_dwTeleportTick = unchecked(
            HUtil32.GetTickCount() - 10_001);
        moveCommand.UserMoveXY(new[] { "29", "31" }, cancelledMove);
        queued = cancelledMove.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_USERMOVE);
        cancelledMove.m_MsgList.Clear();
        cancelledMove.m_PEnvir = remoteMap;
        cancelledMove.m_sMapName = remoteMap.sMapName;
        cancelledMove.Operate(Process(queued));
        Equal((short)23, cancelledMove.m_nCurrX,
            "UserMove map-change cancellation X");
        Equal((short)25, cancelledMove.m_nCurrY,
            "UserMove map-change cancellation Y");
        Assert(GetField<Envirnoment>(cancelledMove,
                "m_NativeUserMoveEnvir") == null,
            "UserMove map-change cancellation pending clear");
    }
    finally
    {
        M2Share.UserEngine = originalEngine;
        M2Share.MapManager = originalMapManager;
        M2Share.g_Config.dwUserMoveTime = originalUserMoveTime;
        M2Share.g_Config.boShowPreFixMsg = originalShowPrefix;
        M2Share.g_Config.sHintMsgPreFix = originalHintPrefix;
        M2Share.g_Config.btGreenMsgFColor = originalGreenForeground;
        M2Share.g_Config.btGreenMsgBColor = originalGreenBackground;
        M2Share.g_Config.btBlueMsgFColor = originalBlueForeground;
        M2Share.g_Config.btBlueMsgBColor = originalBlueBackground;
        M2Share.g_Config.btRedMsgFColor = originalRedForeground;
        M2Share.g_Config.btRedMsgBColor = originalRedBackground;
    }
}

static void CheckEventManagerCadence()
{
    var calls = new List<int>();
    var manager = new EventManager();
    SetField(manager, "_runTick", 1_000);
    var first = new ProbeEvent(1, calls);
    var second = new ProbeEvent(2, calls);
    var third = new ProbeEvent(3, calls) { m_boActive = false };
    manager.AddEvent(first);
    manager.AddEvent(second);
    manager.AddEvent(third);

    RunEventManager(manager, 1_250);
    Equal(0, calls.Count, "event manager ran at 250ms");

    RunEventManager(manager, 1_251);
    Assert(calls.SequenceEqual(new[] { 1, 2, 3 }),
        "event manager FIFO or inactive-node call");

    calls.Clear();
    second.CloseOnRun = true;
    RunEventManager(manager, 1_502);
    Assert(calls.SequenceEqual(new[] { 1, 2, 3 }),
        "event manager close-pass FIFO");
    var active = GetField<IList<Event>>(manager, "_eventList");
    var closed = GetField<IList<Event>>(manager, "_closedEventList");
    Assert(!active.Contains(second), "closed event remained active");
    Assert(ReferenceEquals(second, closed.Single()),
        "closed event tail migration");
    Equal(1_502, second.m_dwCloseTick,
        "closed event migration timestamp");

    RunEventManager(manager, 301_501);
    Equal(1, closed.Count, "closed event reclaimed before 300000ms");
    RunEventManager(manager, 301_502);
    Equal(0, closed.Count, "closed event not reclaimed at 300000ms");

    var map = NewMap(4, 4);
    var visible = new Event(map, 1, 1, Grobal2.ET_FIRE, 60_000, true);
    Assert(ReferenceEquals(visible,
            map.GetEvent(1, 1, Grobal2.ET_FIRE)),
        "typed map-cell event lookup");
    visible.Close();

    var rejected = new Event(map, 9, 9, Grobal2.ET_FIRE, 60_000, true);
    SetField(rejected, "m_dwOpenStartTick", 2_000);
    rejected.Run(2_001);
    Assert(rejected.m_boClosed,
        "failed map insertion did not zero event duration");
}

static void CheckFireBurnEvent()
{
    M2Share.NativeFastnessHqTable = LoadHqTable("1 0.25 100");
    var map = NewMap(64, 64);
    map.sMapName = "AUDIT_FIRE";
    var owner = new HostileSource
    {
        m_PEnvir = map,
        m_nCurrX = 10,
        m_nCurrY = 10
    };
    var target = NewDamageTarget(500, 500, 123);
    target.m_PEnvir = map;
    target.m_boObMode = true;
    target.m_nNativeHqFastness = 1;

    var magicInfo = new TMagic
    {
        wMagicID = SpellsDef.SKILL_EARTHFIRE,
        btEffect = 88,
        MaxTrain = new[] { 111, 222, 333, 333 },
        SpellMilliseconds = 0x12345,
        ColdMilliseconds = 0x23456
    };
    var userMagic = new TUserMagic
    {
        MagicInfo = magicInfo,
        btLevel = 1,
        wMagIdx = 0x4567,
        nTranPoint = 0x12345678
    };
    CheckNativeMapFireWallDuration(map, owner, userMagic);
    var direct = new FireBurnEvent(owner, userMagic, 20, 20,
        Grobal2.ET_FIRE, 60_000, 100);
    var context = GetProperty<object>(direct, "Context");
    Assert(ReferenceEquals(magicInfo,
            GetProperty<TMagic>(context, "MagicInfo")),
        "FireBurn context definition snapshot");
    Equal(0, GetProperty<int>(context, "PlusInfoReferenceCache"),
        "FireBurn missing PlusInfo cache");
    Equal(222, GetProperty<int>(context, "RequiredTrainCache"),
        "FireBurn required-train cache");
    Equal((byte)1, GetProperty<byte>(context, "Level"),
        "FireBurn level snapshot");
    Equal((ushort)0x4567, GetProperty<ushort>(context, "MagicIndex"),
        "FireBurn magic index snapshot");
    Equal(0x12345678, GetProperty<int>(context, "TrainingPoints"),
        "FireBurn training snapshot");
    Equal((ushort)0x2345,
        GetProperty<ushort>(context, "SpellMilliseconds"),
        "FireBurn spell timer low word");
    Equal((ushort)0x3456,
        GetProperty<ushort>(context, "ColdMilliseconds"),
        "FireBurn cold timer low word");
    Equal(88, direct.m_nEventParam, "FireBurn effect snapshot");
    Assert(!direct.ApplyTo(target), "FireBurn ApplyTo return contract");
    Equal(425, target.m_WAbil.HP, "FireBurn ApplyTo HP");
    Equal(123, target.m_WAbil.MP, "FireBurn ApplyTo MP");
    Equal(1, target.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_STRUCK_MAG),
        "FireBurn RM10027 count");
    var struck = target.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_STRUCK_MAG);
    Equal(75, struck.wParam, "FireBurn RM10027 damage");
    Equal(0, struck.nParam1, "FireBurn RM10027 nParam1");
    Equal(0, struck.nParam2, "FireBurn RM10027 nParam2");
    Equal(owner.ObjectId, struck.nParam3, "FireBurn RM10027 owner");
    Assert(ReferenceEquals(target, struck.BaseObject),
        "FireBurn RM10027 source object");

    var ownerHp = owner.m_WAbil.HP;
    Assert(!direct.ApplyTo(owner), "FireBurn self return");
    Equal(ownerHp, owner.m_WAbil.HP, "FireBurn self guard");

    var ghostEvent = new FireBurnEvent(owner, 21, 20, Grobal2.ET_FIRE,
        60_000, 100);
    owner.m_boGhost = true;
    Assert(!ghostEvent.ApplyTo(target), "FireBurn ghost owner return");
    Assert(ghostEvent.m_OwnBaseObject == null,
        "FireBurn ghost owner was not cleared");
    owner.m_boGhost = false;

    var deathTarget = NewDamageTarget(500, 500, 123);
    var deathEvent = new FireBurnEvent(owner, 22, 20, Grobal2.ET_FIRE,
        60_000, 100);
    owner.m_boDeath = true;
    deathEvent.Run(HUtil32.GetTickCount());
    Assert(ReferenceEquals(owner, deathEvent.m_OwnBaseObject),
        "FireBurn base run cleared dead non-ghost owner");
    Assert(!deathEvent.ApplyTo(deathTarget),
        "FireBurn dead non-ghost owner return");
    Equal(400, deathTarget.m_WAbil.HP,
        "FireBurn dead non-ghost owner damage");
    owner.m_boDeath = false;

    var runTarget = NewDamageTarget(500, 500, 123);
    runTarget.m_sCharName = "fire-run-target";
    runTarget.m_nNativeHqFastness = 1;
    runTarget.m_boObMode = true;
    Place(map, runTarget, 30, 30);
    var periodic = new FireBurnEvent(owner, 30, 30, Grobal2.ET_FIRE,
        60_000, 100);
    SetField(periodic, "m_fireRunTick", HUtil32.GetTickCount());
    periodic.Run();
    Equal(500, runTarget.m_WAbil.HP, "FireBurn ran before 3000ms");
    SetField(periodic, "m_fireRunTick",
        unchecked(HUtil32.GetTickCount() - 3_001));
    periodic.Run();
    Equal(425, runTarget.m_WAbil.HP, "FireBurn 3001ms periodic HP");
    Equal(1, runTarget.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_STRUCK_MAG),
        "FireBurn periodic RM10027 count");

    direct.Close();
    ghostEvent.Close();
    deathEvent.Close();
    periodic.Close();
}

static void CheckNativeMapFireWallDuration(Envirnoment map, TBaseObject owner,
    TUserMagic userMagic)
{
    var events = new List<Event>();

    map.Flag.MapFireWallBurnMs = 0;
    events.Add(new FireBurnEvent(owner, 40, 40, Grobal2.ET_FIRE,
        60_000, 100));
    Equal(60_000, GetField<int>(events[^1], "m_dwContinueTime"),
        "MAPFIREWALLBURN zero keeps duration");

    map.Flag.MapFireWallBurnMs = -1;
    events.Add(new FireBurnEvent(owner, 41, 40, Grobal2.ET_FIRE,
        60_000, 100));
    Equal(60_000, GetField<int>(events[^1], "m_dwContinueTime"),
        "MAPFIREWALLBURN negative keeps duration");

    map.Flag.MapFireWallBurnMs = 17_000;
    events.Add(new FireBurnEvent(owner, 42, 40, Grobal2.ET_FIRE,
        60_000, 100));
    Equal(17_000, GetField<int>(events[^1], "m_dwContinueTime"),
        "MAPFIREWALLBURN basic constructor override");
    events.Add(new FireBurnEvent(owner, userMagic, 43, 40,
        Grobal2.ET_FIRE, 60_000, 100));
    Equal(17_000, GetField<int>(events[^1], "m_dwContinueTime"),
        "MAPFIREWALLBURN magic constructor override");
    events.Add(new ProtectedFireBurnEvent(map, null, 44, 40,
        60_000, 100));
    Equal(17_000, GetField<int>(events[^1], "m_dwContinueTime"),
        "MAPFIREWALLBURN protected constructor override");

    map.Flag.MapFireWallBurnMs = 900_001;
    events.Add(new FireBurnEvent(owner, 45, 40, Grobal2.ET_FIRE,
        60_000, 100));
    Equal(900_001, GetField<int>(events[^1], "m_dwContinueTime"),
        "MAPFIREWALLBURN override happens after base clamp");

    map.Flag.MapFireWallBurnMs = 17_000;
    events.Add(new BTFireBurnEvent(map, 46, 40, 60_000,
        100, 1_000, 10, owner));
    Equal(60_000, GetField<int>(events[^1], "m_dwContinueTime"),
        "BTFireBurn derived duration wins after map override");
    events.Add(new BTFireBurnEvent(map, 47, 40, 0,
        100, 1_000, 10, owner));
    Equal(0, GetField<int>(events[^1], "m_dwContinueTime"),
        "BTFireBurn zero duration post-write");
    Equal(Grobal2.ET_BTFIREBURN, events[^1].m_nEventType,
        "BTFireBurn map override makes derived type gate reachable");

    map.Flag.MapFireWallBurnMs = 0;
    foreach (var fireEvent in events)
    {
        fireEvent.Close();
    }
}

static void CheckSourceContracts()
{
    var root = FindRepoRoot();
    var timed = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.TimedAbility.cs")));
    Contains(timed, "or43or",
        "type43 supported set");
    Contains(timed, "varinternalType=(byte)(scriptType+32);",
        "type43 internal mapping");
    Contains(timed,
        "SendRefMsg(Grobal2.RM_CHARSTATUSCHANGED,0,unchecked((ushort)m_nHitSpeed),0,0,string.Empty,GetBodyStateBuffer());",
        "type43 SM657 native parameter layout");
    Contains(timed, "火墙抗性瞬间提高", "type43 add prompt source");
    Contains(timed, "火墙抗性回复正常", "type43 remove prompt source");
    Contains(timed, "\x22(英雄)\x22+text",
        "hero type75 native prompt prefix");
    Before(timed, "SendTimedAbilityState(node,false);",
        "node.LastTick=HUtil32.GetTickCount();",
        "type43 notify before LastTick refresh");

    var bridge = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.cs")));
    Contains(bridge, "unchecked((byte)args[0].AsInt())",
        "PAS type low-byte coercion");
    Contains(bridge, "unchecked((ushort)args[1].AsInt())",
        "PAS value Word coercion");
    Contains(bridge, "unchecked((ushort)args[2].AsInt())",
        "PAS seconds Word coercion");

    var playerTimed = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.TimedAbility.cs")));
    Contains(playerTimed, "BuildTimedAbilityClientState(",
        "player 3555 shared builder");
    Contains(playerTimed, "SendSocket(state.Header,state.Body);",
        "player 3555 transport");
    foreach (var heroFile in Directory.GetFiles(Path.Combine(root, "GameSvr"),
                 "*Hero*.cs", SearchOption.AllDirectories))
    {
        NotContains(File.ReadAllText(heroFile), "SendTimedAbilityClientState(",
            "hero must not override player-only 3555");
    }

    var fire = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Events", "FireBurnEvent.cs")));
    Contains(fire, "publicoverrideboolApplyTo(TBaseObjecttarget)",
        "FireBurn ApplyTo override");
    Contains(fire, "if(owner==null||ReferenceEquals(owner,target))",
        "FireBurn null/self guard");
    Contains(fire, "if(owner.m_boGhost)", "FireBurn owner ghost guard");
    Contains(fire, "if(!owner.IsProperTarget(target))",
        "FireBurn proper-target guard");
    Contains(fire,
        "ResolveFullMagicDamage(owner,SpellsDef.SKILL_EARTHFIRE,false,Context,1,0,m_nDamage)",
        "FireBurn full resolver call");
    Contains(fire, "if(damage>0)", "FireBurn positive damage gate");
    Contains(fire, "Grobal2.RM_STRUCK_MAG,damage,0,0,owner.ObjectId",
        "FireBurn RM10027 tuple");
        // 3000ms 不再是硬编码常量，而是每子类各自的 [obj+0x54] 字段：
    //   0x007178AC c7 43 54 b8 0b 00 00  mov [ebx+0x54],0xBB8   (TFireBurnEvent)
    //   0x00717A81 c7 43 54 e8 03 00 00  mov [ebx+0x54],0x3E8   (TBTFireBurnEvent)
    //   0x007179C5 2b 43 4c              sub eax,[ebx+0x4C]
    //   0x007179C8 3b 43 54              cmp eax,[ebx+0x54]
    //   0x007179CB 76 5c                 jbe                    ; 严格大于才触发
    // 所以断言从「字面 >3000」改成「默认 0xBB8 + 对字段的严格大于比较」。
    Contains(fire, "m_fireRunInterval=0xBB8",
        "FireBurn default interval is no longer the native 0xBB8");
    Contains(fire,
        "if(unchecked((uint)(currentTick-m_fireRunTick))>unchecked((uint)m_fireRunInterval))",
        "FireBurn strict interval gate");
        // 查裸数字 "8030" 会被压缩后的字节证据注释误伤：TBTFireBurnEvent 的
    // 0x00717A81 `C7 43 54 E8 03 00 00` 去掉空白就是 "C74354E8030000"，里面
    // 正好含 "8030"。改查符号名（8030 = Grobal2.RM_MAGSTRUCK_MINE）。
    NotContains(fire, "RM_MAGSTRUCK_MINE", "FireBurn legacy 8030 path");

    var context = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Spells", "MagicDamageContext.cs")));
    Contains(context, "magicInfo.MaxTrain[userMagic.btLevel]",
        "EarthFire required-train cache snapshot");

    var dirty = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.NativeMagicDamage.cs")));
    Contains(dirty, "if(m_boDeath||m_WAbil.HP<=0){return;}",
        "player health dirty alive gate");
    Contains(dirty, "unchecked((uint)(currentTick-dwTick57C))<=500u",
        "health dirty unsigned strict cadence");
    Before(dirty, "if(m_boDeath||m_WAbil.HP<=0)",
        "dwTick57C=currentTick;",
        "player health dirty gate before tick update");
    Before(dirty, "m_boNativeHealthSpellDirty=false;",
        "SendMsg(this,Grobal2.RM_HEALTHSPELLCHANGED,0,0,0,0,string.Empty);",
        "health dirty clear before queue");

    var hero = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "HeroObject.cs")));
    Contains(hero, "if(m_boDeath||m_WAbil.HP<=0)return;",
        "hero health dirty alive gate");
    Contains(hero,
        "unchecked((uint)(currentTick-m_dwNativeHealthSpellDirtyTick))<=500u",
        "hero health dirty unsigned strict cadence");
    Before(hero, "if(m_boDeath||m_WAbil.HP<=0)",
        "m_dwNativeHealthSpellDirtyTick=currentTick;",
        "hero health dirty gate before tick update");
    Contains(hero,
        "if(ProcessMsg.wIdent==Grobal2.RM_HEALTHSPELLCHANGED){SendNativeHealthSpellChanged(ProcessMsg.BaseObject);returntrue;}",
        "hero health-spell Operate dispatch");
    Contains(hero, "varbody=newbyte[16];",
        "hero SM53 body length");
    Contains(hero,
        "BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0,4),m_WAbil.HP);",
        "hero SM53 body HP");
    Contains(hero,
        "BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4,4),m_WAbil.MaxHP);",
        "hero SM53 body MaxHP");
    Contains(hero,
        "BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8,4),m_WAbil.MP);",
        "hero SM53 body MP");
    Contains(hero,
        "BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12,4),m_WAbil.MaxMP);",
        "hero SM53 body MaxMP");
    Contains(hero,
        "Grobal2.MakeDefaultMsg(Grobal2.SM_HEALTHSPELLCHANGED,sourceObjectId,HUtil32.LoWord(m_WAbil.HP),HUtil32.LoWord(m_WAbil.MP),HUtil32.LoWord(m_WAbil.MaxHP));",
        "hero SM53 header fields");
    Contains(hero, "master.SendSocket(header,body);",
        "hero SM53 raw body transport");

    var eventManager = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Events", "EventManager.cs")));
    Contains(eventManager, "unchecked((uint)(currentTick-_runTick))>250u",
        "event manager unsigned global cadence");
    Contains(eventManager, "executeEvent.Run(currentTick);",
        "event manager unconditional FIFO run");
    Contains(eventManager, "removeCount<10",
        "event manager closed cleanup cap");
    Contains(eventManager,
        "unchecked((uint)(currentTick-closedEvent.m_dwCloseTick))<300000u",
        "event manager closed threshold");

    var projection = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeMonsterProjection.cs")));
    Contains(projection, "attacker.m_btJob",
        "SuperForce attacker job discriminator");
    NotContains(projection, "attacker.m_btRaceServer",
        "SuperForce stale attacker race discriminator");

    var effects = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeEffectAbility.cs")));
    Contains(effects,
        "ApplyNativeShieldShapeParameters(stdItem,refaddAbility);",
        "native shield Shape aggregation");
    Contains(effects,
        "if(necklace&&stdItem.Shape==121){addAbility.NativeSearchHuman=true;}",
        "necklace shape121 SearchHuman source");
    Contains(effects,
        "if(ring&&stdItem.Shape==112){addAbility.NativeUserMove=true;}",
        "ring shape112 UserMove source");
    Contains(effects, "case3:addAbility.NativeSearchHuman=true;break;",
        "property254 selector3 SearchHuman source");
    Contains(effects, "case4:addAbility.NativeUserMove=true;break;",
        "property254 selector4 UserMove source");
    NotContains(effects, "record.AsSpan(offset",
        "embedded user-item extension pairs must not be consumed");

    var ability = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.Base.cs")));
    Contains(ability,
        "m_btNativeMagicDamageReductionPercent=m_AddAbil.NativeMagicDamageReductionPercent;",
        "property141 recalc publish");
    Contains(ability,
        "m_boNativeFullMagicShield=m_AddAbil.NativeFullMagicShield;",
        "property73 recalc publish");
    Contains(ability,
        "m_boMagicShield=m_AddAbil.NativeStandardMagicShield;",
        "property72 recalc publish");
    Contains(ability,
        "m_boNativeHalfMagicShield=m_AddAbil.NativeHalfMagicShield;",
        "property254 recalc publish");
    Contains(ability,
        "m_boNativeUserMove=m_AddAbil.NativeUserMove;",
        "property254 UserMove recalc publish");
    Contains(ability,
        "m_boProbeNecklace=m_AddAbil.NativeSearchHuman;",
        "property254 SearchHuman recalc publish");
    NotContains(ability,
        "m_boProbeNecklace=m_AddAbil.NativeHalfMagicShield;",
        "native probe and half shield fields must remain distinct");

    var commandManager = Compact(File.ReadAllText(Path.Combine(root,
        "GameSvr", "Command", "CommandManager.cs")));
    Contains(commandManager,
        "M2Share.CommandConf.LoadConfig();RegisterCommandGroups();",
        "command groups registered from [GameCommand] source names");
    Contains(commandManager,
        "ApplyNativeFormGmCommandIni();",
        "FormGMCommand.ini overlay after source-name registration");
    Contains(commandManager,
        "\"FormGMCommand.ini\"",
        "native overlay file name");
    Contains(commandManager,
        "NativeGmCommandRegistry.DefaultNameByIndex.TryGetValue(idx,outvardefaultName)",
        "overlay is keyed by dispatch index not English verb");
    NotContains(commandManager, "RegisterNativePlayerCommandAliases",
        "this build has no Command.conf UserMove/Searching hop");
    NotContains(commandManager, "RegisterNativePlayerCommandAlias(",
        "this build has no per-verb alias installer");
    Contains(commandManager,
        "M2Share.CommandConf.ReloadCustomAlias();CommandMaps.Clear();",
        "hot reload rebuilds maps then reapplies FormGMCommand.ini");

    var commandConfig = Compact(File.ReadAllText(Path.Combine(root,
        "GameSvr", "Configs", "GameCmdConfig.cs")));
    Contains(commandConfig,
        "ReadString(\u0022Command\u0022,\u0022UserMove\u0022,\u0022\u0022)",
        "UserMove Command.conf key");
    Contains(commandConfig,
        "M2Share.g_GameCommand.USERMOVE.sCmd=LoadString;",
        "UserMove Command.conf publish");
    Contains(commandConfig,
        "ReadString(\u0022Command\u0022,\u0022Searching\u0022,\u0022\u0022)",
        "Searching Command.conf key");
    Contains(commandConfig,
        "M2Share.g_GameCommand.SEARCHING.sCmd=LoadString;",
        "Searching Command.conf publish");

    var grobal = Compact(File.ReadAllText(Path.Combine(root,
        "SystemModule", "Grobal2.cs")));
    Contains(grobal, "publicconstintRM_USERMOVE=10056;",
        "UserMove native internal ident constant");
    Contains(grobal, "publicconstintRM_NATIVE_CLEAROBJECTS=10117;",
        "native pointer-move clear-objects ident");
    Contains(grobal, "publicconstintRM_NATIVE_CHANGEMAP=10118;",
        "native pointer-move change-map ident");

    var userMove = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Command", "Commands", "UserMoveXYCommand.cs")));
    Contains(userMove,
        "if(PlayObject.m_btPermission>=2){PositionMoveAsGameMaster(PlayObject,@Params);return;}",
        "UserMove permission-two GM branch");
    Contains(userMove, "if(!PlayObject.m_boNativeUserMove)return;",
        "UserMove silent native permission gate");
    Before(userMove, "if(PlayObject.m_btPermission>=2)",
        "if(!PlayObject.m_boNativeUserMove)",
        "UserMove GM branch before native item gate");
    Contains(userMove,
        "if(environment.Flag.boNOPOSITIONMOVE){SendNativeUserMoveFailure(PlayObject,\x22在这里您无法使用\x22);return;}",
        "UserMove native map-disabled prompt");
    Contains(userMove,
        "varnX=HUtil32.Str_ToInt(sX,0);",
        "UserMove signed Int32 X parse");
    Contains(userMove,
        "varnY=HUtil32.Str_ToInt(sY,0);",
        "UserMove signed Int32 Y parse");
    Contains(userMove,
        "PlayObject.QueueNativeUserMove(environment,currentTick,nX,nY);",
        "UserMove ordinary delayed-stage queue");
    Contains(userMove,
        "if(!string.IsNullOrEmpty(third)){environment=M2Share.MapManager.FindMap(first);",
        "UserMove GM three-token map route");
    Contains(userMove,
        "elseif(!string.IsNullOrEmpty(second)){environment=playObject.m_PEnvir;",
        "UserMove GM two-token current-map route");
    Contains(userMove,
        "vartarget=M2Share.UserEngine.GetPlayObjectEx(first);",
        "UserMove GM single-token player lookup");
    Contains(userMove,
        "target!=null&&!target.m_boGhost&&target.m_boReadyRun",
        "UserMove GM single-token live-ready player gate");
    Contains(userMove,
        "if(targetX<=0||targetX>=environment.wWidth-1)targetX=target.m_nCurrX;",
        "UserMove GM player-front X boundary fallback");
    Contains(userMove,
        "if(targetY<=0||targetY>=environment.wHeight-1)targetY=target.m_nCurrY;",
        "UserMove GM player-front Y boundary fallback");
    Contains(userMove,
        "playObject.ExecuteNativeUserMove(environment,x,y);",
        "UserMove GM direct environment move");
    Contains(userMove,
        "playObject.SendMsg(playObject,Grobal2.RM_SYSMESSAGE,0,0xFF,0x38,0,message);",
        "UserMove fixed native failure color");
    NotContains(userMove, "SysMsg(",
        "UserMove native failures must not use configurable SysMsg");
    NotContains(userMove, "dwUserMoveTime",
        "UserMove native cooldown must remain fixed at ten seconds");
    NotContains(userMove, "CanWalkOfItem",
        "UserMove ordinary branch must not preflight destination");
    NotContains(userMove, "RM_SPACEMOVE_FIRE",
        "UserMove command must not emit legacy space-move effect");
    NotContains(userMove, "m_boTeleport",
        "UserMove must not consume legacy teleport permission");
    NotContains(userMove, "m_boProbeNecklace",
        "UserMove must not consume SearchHuman permission");

    var nativeUserMove = Compact(File.ReadAllText(Path.Combine(root,
        "GameSvr", "Players", "TPlayObject.NativeUserMove.cs")));
    Contains(nativeUserMove,
        "internalEnvirnomentm_NativeUserMoveEnvir;",
        "UserMove saved-environment field");
    Contains(nativeUserMove,
        "unchecked((uint)(currentTick-previousTick))>10000U;",
        "UserMove unsigned strict fixed cooldown");
    Contains(nativeUserMove,
        "m_dwTeleportTick=currentTick;m_NativeUserMoveEnvir=environment;",
        "UserMove timestamp then environment snapshot");
    Contains(nativeUserMove,
        "internalvoidQueueNativeUserMove(Envirnomentenvironment,intcurrentTick,intx,inty)",
        "UserMove queue preserves signed Int32 coordinates");
    Contains(nativeUserMove,
        "SendDelayMsg(this,Grobal2.RM_USERMOVE,0,x,y,0,string.Empty,1500);",
        "UserMove exact 10056 delayed tuple");
    Contains(nativeUserMove,
        "if(environment!=null&&ReferenceEquals(environment,m_PEnvir))",
        "UserMove completion environment-identity gate");
    Contains(nativeUserMove,
        "ExecuteNativeUserMove(environment,processMsg.nParam1,processMsg.nParam2);",
        "UserMove completion coordinate move");
    // MOVE-63：原生 sub_7782D0 是 11 个调用者共用的**同一个函数体**，C# 已把
    // 那份搜索收口到 TBaseObject.NativeGetRandomXY，UserMove 只留一个转发器。
    // 三条搜索契约（31 次重试 / 两个轴的非正坐标补种 / PointList 兜底）因此改到
    // 共用体上查，并在这里钉住「UserMove 必须转发、不得自带第二份搜索」。
    Contains(nativeUserMove,
        "TryResolveNativeUserMoveCoordinates(Envirnomentenvironment,refintx,refinty)=>NativeGetRandomXY(environment,refx,refy);",
        "UserMove must forward to the single native coordinate resolver");
    NotContains(nativeUserMove, "for(",
        "UserMove grew a second copy of the native coordinate search");
    var randomXy = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.cs")));
    Contains(randomXy,
        "for(varnRetry=0;nRetry<31;nRetry++)",
        "UserMove native resolver exact attempt count");
    Contains(randomXy,
        "if(nX<=0){nX=M2Share.RandomNumber.Random(Envir.wWidth)+1;}",
        "UserMove native resolver nonpositive X randomization");
    Contains(randomXy,
        "if(nY<=0){nY=M2Share.RandomNumber.Random(Envir.wHeight)+1;}",
        "UserMove native resolver nonpositive Y randomization");
    Contains(randomXy,
        "nX=unchecked((ushort)Point.nX);nY=unchecked((ushort)Point.nY);returntrue;",
        "UserMove native resolver PointList fallback");
    Contains(nativeUserMove,
        "TrySpaceMoveToEnvironment(environment,unchecked((short)x),unchecked((short)y),0,true,true);",
        "UserMove resolved native internal-message move");
    NotContains(nativeUserMove, "shortx,shorty",
        "UserMove queue must not truncate coordinates to Int16");
    Before(nativeUserMove,
        "if(environment!=null&&ReferenceEquals(environment,m_PEnvir))",
        "m_NativeUserMoveEnvir=null;",
        "UserMove completion unconditional pending clear");
    NotContains(nativeUserMove, "RM_SPACEMOVE_FIRE",
        "UserMove completion must not emit legacy space-move effect");

    var playerMessage = Compact(File.ReadAllText(Path.Combine(root,
        "GameSvr", "Players", "TPlayObject.Message.cs")));
    Contains(playerMessage,
        "caseGrobal2.RM_USERMOVE:CompleteNativeUserMove(ProcessMsg);break;",
        "UserMove 10056 player-message dispatch");
    Contains(playerMessage,
        "caseGrobal2.RM_CLEAROBJECTS:caseGrobal2.RM_NATIVE_CLEAROBJECTS:",
        "native and legacy clear-objects dispatch coexistence");
    Contains(playerMessage,
        "caseGrobal2.RM_CHANGEMAP:caseGrobal2.RM_NATIVE_CHANGEMAP:",
        "native and legacy change-map dispatch coexistence");

    var baseActor = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.cs")));
    // MOVE-52: 0x6BD3AA and 0x6BD3D3 load 0x2785/0x2786 unconditionally, so the pair
    // is the default for every teleport, not an opt-in for ExecuteNativeUserMove.
    // ExactEnvironmentMoveCheck and DynRoomMasterRelocationCheck pin the queued
    // sequence a default-argument caller actually gets; this is the signature tripwire.
    Contains(baseActor,
        "boolcoordinatesAlreadyResolved=false,booluseNativeInternalMessages=true,boolrequireLocalServerIndex=true)",
        "space-move internal idents defaulted away from the native 10117/10118 pair");
    Contains(baseActor,
        "if(!coordinatesAlreadyResolved&&!SpaceMove_GetRandXY(targetEnvironment,refm_nCurrX,refm_nCurrY))returnfalse;",
        "UserMove resolved coordinates skip legacy resolver");
    Contains(baseActor,
        "useNativeInternalMessages?Grobal2.RM_NATIVE_CLEAROBJECTS:Grobal2.RM_CLEAROBJECTS",
        "UserMove native clear-objects selection");
    Contains(baseActor,
        "useNativeInternalMessages?Grobal2.RM_NATIVE_CHANGEMAP:Grobal2.RM_CHANGEMAP",
        "UserMove native change-map selection");
    Contains(baseActor,
        "SpaceMove(M2Share.MapManager.FindMap(sMap),nX,nY,nInt);",
        "string map move delegates to environment overload");

    var searchHuman = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Command", "Commands", "SearchHumanCommand.cs")));
    Contains(searchHuman,
        "if(string.IsNullOrEmpty(sHumanName)||!PlayObject.m_boProbeNecklace&&PlayObject.m_btPermission<3)return;",
        "SearchHuman silent argument and permission gates");
    Contains(searchHuman,
        "if(!HasNativeSearchCooldownElapsed(currentTick,PlayObject.m_dwProbeTick))return;",
        "SearchHuman silent cooldown gate");
    Contains(searchHuman,
        "unchecked((uint)(currentTick-previousTick))>10000U;",
        "SearchHuman unsigned strict cooldown");
    Contains(searchHuman,
        "vartarget=M2Share.UserEngine.GetPlayObjectEx(sHumanName);",
        "SearchHuman unfiltered player lookup");
    Contains(searchHuman,
        "if(target?.m_boGhost==true||target?.m_boReadyRun!=true)target=null;",
        "SearchHuman ghost and not-ready target gate");
    NotContains(searchHuman, "GetPlayObject(sHumanName)",
        "SearchHuman must not use legacy partially-filtered lookup");
    NotContains(searchHuman, "GetHero(",
        "SearchHuman must not resolve hero names");
    NotContains(searchHuman,
        "||PlayObject.m_btPermission>=3",
        "SearchHuman GM cooldown bypass");
    Contains(searchHuman,
        "target.m_PEnvir==PlayObject.m_PEnvir",
        "SearchHuman same-map identity gate");
    Contains(searchHuman,
        "target.m_nCurrX+','+target.m_nCurrY",
        "SearchHuman target X/Y source");
    Contains(searchHuman, "在其他地图上",
        "SearchHuman other-map branch source");
    Contains(searchHuman, "探测项链无法查出",
        "SearchHuman missing-target branch source");
    Contains(searchHuman,
        "PlayObject.SendMsg(PlayObject,Grobal2.RM_SYSMESSAGE,0,0xDB,0xFF,0,result);",
        "SearchHuman fixed native system-message color");
    NotContains(searchHuman, "SysMsg(",
        "SearchHuman must not use configurable SysMsg or hint prefix");

    var actor = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.cs")));
    var moveAction = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeMoveAction.cs")));
    Contains(moveAction, "cellObject?.CellType!=CellType.OS_EVENTOBJECT",
        "walk event-object filter");
    Contains(moveAction, "cellObject.CellObjisnotEventmapEvent",
        "walk event type guard");
    Contains(moveAction, "mapEvent.ApplyTo(this);",
        "walk event ApplyTo call");

    var magic = Compact(File.ReadAllText(Path.Combine(root, "GameSvr",
        "Spells", "MagicManager.cs")));
    Before(magic, "AddFire(nX,nY);", "AddFire(nX,nY+1);",
        "EarthFire center before down");
    Before(magic, "AddFire(nX,nY+1);", "AddFire(nX,nY-1);",
        "EarthFire down before up");
    Before(magic, "AddFire(nX,nY-1);", "AddFire(nX-1,nY);",
        "EarthFire up before left");
    Before(magic, "AddFire(nX-1,nY);", "AddFire(nX+1,nY);",
        "EarthFire left before right");
    Contains(magic, "Grobal2.ET_FIRE)!=null", "EarthFire occupied-cell guard");
}

static ProbePlayer NewPlayer(string name, Envirnoment map) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_PEnvir = map,
    m_boObMode = true
};

static string TakeSingleSystemMessage(TPlayObject player, string label,
    int foreground = -1, int background = -1)
{
    var messages = player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE).ToList();
    Equal(1, messages.Count, label + " message count");
    if (foreground >= 0)
        Equal(foreground, messages[0].nParam1, label + " foreground");
    if (background >= 0)
        Equal(background, messages[0].nParam2, label + " background");
    return messages[0].Buff;
}

static TBaseObject NewDamageTarget(int hp, int maxHp, int mp)
{
    var target = new TBaseObject();
    target.m_WAbil.HP = hp;
    target.m_WAbil.MaxHP = maxHp;
    target.m_WAbil.MP = mp;
    target.m_WAbil.MaxMP = Math.Max(mp, 500);
    target.m_WAbil.MAC = HUtil32.MakeLong(0, 0);
    return target;
}

// AddToMap, not MoveToMovingObject: the original's mover sub_7797CC only reports
// success from 0x779A95, which is reached after unlinking the actor from the SOURCE
// cell. Asking it to move an actor out of a cell it was never in walks the empty list
// and falls through to `xor eax,eax` @0x779AAD, i.e. FALSE. A first placement has no
// source cell, so the mover is the wrong primitive for it.
static TBaseObject Place(Envirnoment map, TBaseObject actor, short x, short y)
{
    actor.m_PEnvir = map;
    actor.m_nCurrX = x;
    actor.m_nCurrY = y;
    actor.m_boAddToMaped = false;
    actor.m_boDelFormMaped = false;
    Assert(ReferenceEquals(actor, map.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, actor)), "map actor placement");
    return actor;
}

static Envirnoment NewMap(short width, short height)
{
    var map = new Envirnoment();
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    initialize.Invoke(map, new object[] { width, height });
    return map;
}

static void RegisterMap(Envirnoment map) =>
    GetField<IDictionary<string, Envirnoment>>(M2Share.MapManager,
        "m_MapList").Add(map.sMapName, map);

static TProcessMessage Process(SendMessage message) => new()
{
    wIdent = message.wIdent,
    wParam = message.wParam,
    nParam1 = message.nParam1,
    nParam2 = message.nParam2,
    nParam3 = message.nParam3,
    dwDeliveryTime = message.dwDeliveryTime,
    BaseObject = message.BaseObject?.ObjectId ?? message.ObjectId,
    boLateDelivery = message.boLateDelivery,
    sMsg = message.Buff ?? string.Empty,
    Payload = message.Payload
};

static int ResolveFullMagicDamage(TBaseObject target, TBaseObject source,
    int skillId, int category, int rawDamage)
{
    var method = typeof(TBaseObject).GetMethod("ResolveFullMagicDamage",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TBaseObject.ResolveFullMagicDamage");
    var parameters = method.GetParameters();
    Equal(7, parameters.Length, "full resolver parameter count");
    Equal(typeof(byte), parameters[4].ParameterType,
        "full resolver category ABI");
    Equal(typeof(int), parameters[5].ParameterType,
        "full resolver flags ABI");
    var contextType = parameters[3].ParameterType;
    var empty = contextType.GetProperty("Empty", BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                ?? throw new MissingMemberException(contextType.FullName, "Empty");
    var result = method.Invoke(target, new[]
    {
        source,
        ConvertFor(skillId, parameters[1].ParameterType),
        false,
        empty,
        ConvertFor(category, parameters[4].ParameterType),
        ConvertFor(0, parameters[5].ParameterType),
        ConvertFor(rawDamage, parameters[6].ParameterType)
    });
    return (int)(result ?? 0);
}

static int ApplyStandardEarthFireLanding(TBaseObject target, int damage)
{
    var method = typeof(TBaseObject).GetMethod(
        "ApplyStandardEarthFireLanding", BindingFlags.Instance |
        BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            "TBaseObject.ApplyStandardEarthFireLanding");
    return (int)(method.Invoke(target, new object[] { damage }) ?? 0);
}

static TAddAbility ApplyNativeEffectItemParameters(
    IReadOnlyList<(ushort Id, ushort Value)> standardProperties,
    IReadOnlyList<(ushort Id, ushort Value)> instanceProperties)
{
    var (stdItem, userItem) = CreateNativeEffectItem(15, 0,
        standardProperties, instanceProperties);

    var addAbility = new TAddAbility();
    var actor = new TBaseObject();
    var method = typeof(TBaseObject).GetMethod(
        "ApplyNativeEffectItemParameters", BindingFlags.Instance |
        BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            "TBaseObject.ApplyNativeEffectItemParameters");
    var parameters = method.GetParameters();
    Equal(3, parameters.Length,
        "native effect item parameter count");
    Equal(typeof(TAddAbility).MakeByRefType(), parameters[2].ParameterType,
        "native effect add-ability ABI");
    var arguments = new object[]
    {
        userItem,
        stdItem,
        addAbility
    };
    method.Invoke(actor, arguments);
    return (TAddAbility)arguments[2];
}

static ProbePlayer RecalcNativeLandingItem(byte stdMode, byte shape, int slot,
    IReadOnlyList<(ushort Id, ushort Value)> standardProperties,
    IReadOnlyList<(ushort Id, ushort Value)> instanceProperties)
{
    var (stdItem, userItem) = CreateNativeEffectItem(stdMode, shape,
        standardProperties, instanceProperties);
    M2Share.UserEngine.StdItemList.Clear();
    M2Share.UserEngine.StdItemList.Add(stdItem);
    var player = NewPlayer("native-landing-recalc", NewMap(16, 16));
    player.m_UseItems[slot] = userItem;
    player.RecalcAbilitys();
    return player;
}

static (GoodItem StdItem, TUserItem UserItem) CreateNativeEffectItem(
    byte stdMode, byte shape,
    IReadOnlyList<(ushort Id, ushort Value)> standardProperties,
    IReadOnlyList<(ushort Id, ushort Value)> instanceProperties)
{
    Assert(standardProperties.Count <= 6,
        "standard native property fixture capacity");
    Assert(instanceProperties.Count <= 6,
        "instance native property fixture capacity");

    var stdItem = new GoodItem
    {
        Name = $"native-landing-{stdMode}-{shape}",
        StdMode = stdMode,
        Shape = shape,
        ItemType = GoodType.ITEM_ETC
    };
    for (var index = 0; index < standardProperties.Count; index++)
    {
        stdItem.NativeItemExtAbilIdents[index] = standardProperties[index].Id;
        stdItem.NativeItemExtAbilValues[index] = standardProperties[index].Value;
    }

    var record = new byte[208];
    for (var index = 0; index < instanceProperties.Count; index++)
    {
        var offset = 0x60 + index * 4;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(offset, 2),
            instanceProperties[index].Id);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(offset + 2, 2),
            instanceProperties[index].Value);
    }

    return (stdItem, new TUserItem
    {
        wIndex = 1,
        Dura = 100,
        DuraMax = 100,
        NativeRecord = record
    });
}

static void SetDamageAbility(TBaseObject actor, int hp, int maxHp, int mp)
{
    actor.m_WAbil.HP = hp;
    actor.m_WAbil.MaxHP = maxHp;
    actor.m_WAbil.MP = mp;
    actor.m_WAbil.MaxMP = Math.Max(mp, 500);
    actor.m_WAbil.MAC = 0;
}

static int GetNativeTimedRemaining(TBaseObject actor, byte internalType)
{
    var method = typeof(TBaseObject).GetMethod(
        "GetNativeTimedAbilityRemainingMilliseconds",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            "TBaseObject.GetNativeTimedAbilityRemainingMilliseconds");
    return (int)(method.Invoke(actor, new object[] { internalType }) ?? 0);
}

static void RunNativeHealthSpellDirty(TPlayObject player, int currentTick)
{
    var method = typeof(TPlayObject).GetMethod(
        "RunNativeHealthSpellDirty", BindingFlags.Instance |
        BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            "TPlayObject.RunNativeHealthSpellDirty");
    method.Invoke(player, new object[] { currentTick });
}

static void RunHeroNativeHealthSpellDirty(HeroObject hero, int currentTick)
{
    var method = typeof(HeroObject).GetMethod(
        "RunNativeHealthSpellDirty", BindingFlags.Instance |
        BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            "HeroObject.RunNativeHealthSpellDirty");
    method.Invoke(hero, new object[] { currentTick });
}

static void RunEventManager(EventManager manager, int currentTick)
{
    var method = typeof(EventManager).GetMethod("Run",
        BindingFlags.Instance | BindingFlags.NonPublic,
        null, new[] { typeof(int) }, null)
        ?? throw new MissingMethodException("EventManager.Run(Int32)");
    method.Invoke(manager, new object[] { currentTick });
}

static object ConvertFor(int value, Type type)
{
    if (type == typeof(byte))
        return unchecked((byte)value);
    if (type == typeof(short))
        return unchecked((short)value);
    return value;
}

static NativeFastnessHqTable LoadHqTable(string contents)
{
    var path = Path.Combine(Path.GetTempPath(),
        $"m2-hq-{Guid.NewGuid():N}.txt");
    try
    {
        File.WriteAllText(path, contents);
        var table = new NativeFastnessHqTable();
        Assert(table.Load(path), "HQ fixture load");
        return table;
    }
    finally
    {
        File.Delete(path);
    }
}

static (ClientPacket Header, byte[] Body) BuildTimedAbilityPacket(byte type,
    int remaining, int value, bool removed)
{
    var method = typeof(TBaseObject).GetMethod(
        "BuildTimedAbilityClientState", BindingFlags.Static |
        BindingFlags.NonPublic)
        ?? throw new MissingMethodException("BuildTimedAbilityClientState");
    var tuple = method.Invoke(null, new object[] { type, remaining, value, removed })
        ?? throw new InvalidOperationException("3555 builder returned null");
    var tupleType = tuple.GetType();
    var header = (ClientPacket)(tupleType.GetField("Item1")?.GetValue(tuple)
        ?? throw new MissingFieldException(tupleType.FullName, "Item1"));
    var body = (byte[])(tupleType.GetField("Item2")?.GetValue(tuple)
        ?? throw new MissingFieldException(tupleType.FullName, "Item2"));
    return (header, body);
}

static byte[] HeaderBytes(ClientPacket packet) => BuildHeaderBytes(packet.Recog,
    packet.Ident, packet.Param, packet.Tag, packet.Series);

static byte[] BuildHeaderBytes(int recog, int ident, int param, int tag,
    int series)
{
    var result = new byte[ClientPacket.PackSize];
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0, 4), recog);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4, 2),
        unchecked((ushort)ident));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6, 2),
        unchecked((ushort)param));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8, 2),
        unchecked((ushort)tag));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10, 2),
        unchecked((ushort)series));
    return result;
}

static void CheckTimedState(TimedState state, byte type, int remaining,
    int value, bool removed, string label)
{
    Equal(type, state.InternalType, label + " type");
    Equal(remaining, state.RemainingMilliseconds, label + " remaining");
    Equal(value, state.Value, label + " value");
    Equal(removed, state.Removed, label + " removed");
}

static void CheckLastPrompt(ProbePlayer player, string expected, string label)
{
    var prompt = player.m_MsgList.Last(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE);
    Equal(expected, prompt.Buff, label);
}

static void SetAllTimedNodeTicks(TBaseObject actor, int tick)
{
    var node = GetField<object>(actor, "m_TimedAbilityHead");
    while (node != null)
    {
        var type = node.GetType();
        NodeField(type, "LastTick").SetValue(node, tick);
        node = NodeField(type, "Next").GetValue(node);
    }
}

static FieldInfo NodeField(Type type, string name) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic)
    ?? throw new MissingFieldException(type.FullName, name);

static FieldInfo FindField(Type type, string name)
{
    for (var current = type; current != null; current = current.BaseType)
    {
        var field = current.GetField(name, BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly);
        if (field != null)
            return field;
    }
    throw new MissingFieldException(type.FullName, name);
}

static T GetField<T>(object target, string name)
{
    var value = FindField(target.GetType(), name).GetValue(target);
    return value == null ? default : (T)value;
}

static T GetProperty<T>(object target, string name)
{
    var property = target.GetType().GetProperty(name,
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic)
        ?? throw new MissingMemberException(target.GetType().FullName, name);
    var value = property.GetValue(target);
    return value == null ? default : (T)value;
}

static void SetField(object target, string name, object value) =>
    FindField(target.GetType(), name).SetValue(target, value);

static List<PasValue> Values(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

static string Compact(string source) =>
    string.Concat(source.Where(value => !char.IsWhiteSpace(value)));

static string FindRepoRoot() => AuditRepoRoot.Resolve();

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig { nSendRefMsgRange = 12 };
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.CastleManager = new CastleManager();
    M2Share.EventManager = new EventManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
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

static void Before(string source, string first, string second, string label)
{
    var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
    var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
    Assert(firstIndex >= 0 && secondIndex > firstIndex, label);
}

static void Contains(string source, string value, string label) =>
    Assert(source.Contains(value, StringComparison.Ordinal), label);

static void NotContains(string source, string value, string label) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), label);

static void Bytes(byte[] expected, byte[] actual, string label)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{label}: expected={Convert.ToHexString(expected)}, " +
            $"actual={Convert.ToHexString(actual)}");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
}

readonly record struct TimedState(byte InternalType, int RemainingMilliseconds,
    int Value, bool Removed, bool Type43Present, bool Type0Present,
    bool DirtyAtCallback);

sealed class ProbePlayer : TPlayObject
{
    public List<TimedState> TimedStates { get; } = new();

    public bool IsAbilityRecalcPending => ReadDirty();

    public void ConsumePendingRecalcForCheck() => ConsumeAbilityRecalcPending();

    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);

    public void ClearNotifications()
    {
        TimedStates.Clear();
        m_MsgList.Clear();
        m_DefMsg = null;
    }

    protected override void SendTimedAbilityClientState(byte internalType,
        int remainingMilliseconds, int value, bool removed)
    {
        TimedStates.Add(new TimedState(internalType, remainingMilliseconds,
            value, removed, HasTimedAbility(43), HasTimedAbility(0),
            ReadDirty()));
    }

    private bool ReadDirty()
    {
        for (var type = GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField("m_boAbilityRecalcPending",
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return (bool)(field.GetValue(this) ?? false);
        }
        throw new MissingFieldException("m_boAbilityRecalcPending");
    }
}

sealed class HostileSource : TBaseObject
{
    public override bool IsProperTarget(TBaseObject target) =>
        target != null && !ReferenceEquals(this, target);
}

sealed class ProtectedFireBurnEvent : FireBurnEvent
{
    public ProtectedFireBurnEvent(Envirnoment envir, TBaseObject owner,
        int x, int y, int duration, int damage)
        : base(envir, owner, x, y, Grobal2.ET_FIRE, duration, damage)
    {
    }
}

sealed class ProbeEvent : Event
{
    private readonly int _id;
    private readonly IList<int> _calls;

    public ProbeEvent(int id, IList<int> calls)
        : base(null, id, 0, id, int.MaxValue, false)
    {
        _id = id;
        _calls = calls;
    }

    public bool CloseOnRun { get; set; }

    public override void Run(int currentTick)
    {
        _calls.Add(_id);
        if (CloseOnRun)
        {
            m_boActive = false;
            m_boClosed = true;
        }
    }
}
