using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();
VerifyNativeMagicQueueShape();
VerifyNativeSingleDamageScaling();
VerifyNativeMagicHitChance();
VerifyTimedAbilityRefreshMatrix();
VerifyTimedAbilityExpiryBatch();

var actor = NewActor();
Assert(actor.SetNativeActiveState(26), "state26 set");
Assert(actor.HasNativeActiveState(26), "state26 read");
// POISON_STONE is legacy slot 5, and slot i is native state 31 - i, so this
// write and state 26 are the same thing. Native's petrify gained-arm confirms
// the identity: the state-gained dispatch @0x7418C8 sends index 26 to arm 18
// @0x741DC6, which is "你被石化了！".
actor.m_wStatusTimeArr[Grobal2.POISON_STONE] = 1;
actor.m_nCharStatus = actor.GetCharStatus();
Assert(actor.HasNativeActiveState(26), "legacy stone write did not reach state26");
Assert(actor.m_wStatusTimeArr[Grobal2.POISON_STONE] == 1,
    "legacy stone slot did not read back the state26 node");
Assert(actor.ClearNativeActiveState(26), "state26 clear");
Assert(!actor.HasNativeActiveState(26), "state26 clear read");
// This used to assert the slot still read 1 after the state was cleared - the
// 4.18 dual authority as a contract. Native has one carrier: FindState
// @0x773BB1 gates on the bitset before it walks Self+0xDC, so a record whose
// bit is clear cannot be found and reports no time.
Assert(actor.m_wStatusTimeArr[Grobal2.POISON_STONE] == 0,
    "legacy stone slot outlived the cleared state26 bit");

SetField(actor, "m_dwNativeState26Deadline", uint.MaxValue);
Assert(!CanAdd(actor, 26), "active deadline admitted state26");
Assert(CanAdd(actor, 25), "state26 deadline blocked another state");
SetField(actor, "m_dwNativeState26Deadline", 0u);
Assert(CanAdd(actor, 26), "cleared deadline rejected state26");

Assert(actor.SetNativeActiveState(18), "state18 set");
Assert(!CanAdd(actor, 26), "state18 admitted state26");
Assert(CanAdd(actor, 25), "state18 blocked another state");
actor.ClearNativeActiveState(18);

Assert(actor.SetNativeActiveState(16), "state16 set");
foreach (var type in new byte[] { 0, 13, 24, 26, 28, 29, 30, 31 })
    Assert(!CanAdd(actor, type), $"state16 admitted {type}");
Assert(CanAdd(actor, 25), "state16 blocked 25");
actor.ClearNativeActiveState(16);

var lifecycleActor = new TBaseObject();
Assert(AddInternal(lifecycleActor, 26, 0, 5000),
    "state26 lifecycle add");
Assert(lifecycleActor.HasNativeActiveState(26),
    "state26 lifecycle bit missing");
Assert(AddInternal(lifecycleActor, 18, 0, 5000),
    "state18 lifecycle add");
Assert(!lifecycleActor.HasNativeActiveState(26),
    "state18 add retained state26");
Assert(lifecycleActor.HasNativeActiveState(18),
    "state18 lifecycle bit missing");

var orphanBitActor = new TBaseObject();
Assert(orphanBitActor.SetNativeActiveState(26), "orphan state26 bit set");
Assert(!RemoveInternal(orphanBitActor, 26),
    "orphan state26 bit reported a removed node");
Assert(!orphanBitActor.HasNativeActiveState(26),
    "manual removal did not clear state26 before node lookup");

var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "GameSvr",
    "Actors", "TBaseObject.NativeState26.cs"));
Contains(source, "NativeState26Type = 26", "native type26 constant");
Contains(source, "m_dwNativeState26Deadline", "native deadline field");
Contains(source, "m_wNativeState26DeadlineBonus + 10",
    "native deadline overflow branch");
var effectSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "GameSvr",
    "Actors", "TBaseObject.NativeState26Effects.cs"));
Contains(effectSource, "!target.HasNativeActiveState(52)",
    "message10177 state52 prefilter");
Contains(effectSource, "!target.m_boAdminMode && !target.m_boStoneMode",
    "message10177 admin/stone prefilter");
Contains(effectSource, "target.m_btRaceServer != 240",
    "message10177 race240 prefilter");
Contains(effectSource, "target.m_btRaceServer != 241",
    "message10177 race241 prefilter");
Contains(effectSource, "target == null || !target.bo2B9 ||",
    "message10177 area bo2B9 prefilter");
Contains(effectSource, "ApplyNativeMagicHitHealing();",
    "message10177 positive-hit healing callback");
Contains(effectSource, "ConsumeNativeOneShotMagicDamage(payload.SkillId);",
    "message10177 one-shot damage cleanup");
Contains(effectSource, "chance <= M2Share.RandomNumber.Random(100)",
    "message10177 healing chance predicate");
Assert(!effectSource.Contains("HasNativeActiveState(102)",
        StringComparison.Ordinal),
    "stock sub_7446FC must not halve magic-hit healing under state102");
Console.WriteLine("PASS native-state26 carrier=independent-of-stone " +
    "gate=16+18+deadline deadline=125-threshold+bonus " +
    "message10177=mode+delayed-payload+positive-batch");

static void VerifyNativeMagicQueueShape()
{
    var source = new TBaseObject();
    var target = new TBaseObject();
    var contextType = typeof(TBaseObject).Assembly.GetType(
        "GameSvr.MagicDamageContext")
        ?? throw new TypeLoadException("MagicDamageContext");
    var empty = contextType.GetProperty("Empty",
        BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
        ?? throw new MissingMemberException("MagicDamageContext.Empty");
    var queue = typeof(TBaseObject).GetMethod("QueueNativeMagicEffect",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("QueueNativeMagicEffect");

    int before = HUtil32.GetTickCount();
    queue.Invoke(source, new object[]
    {
        (ushort)3, target, 123, (ushort)33, (short)10, (short)11,
        (byte)1, true, (byte)0, empty, 600
    });

    Assert(source.m_MsgList.Count == 1, "message10177 queue count");
    var message = source.m_MsgList[0];
    Assert(message.wIdent == Grobal2.RM_NATIVE_MAGIC_EFFECT,
        "message10177 ident");
    Assert(message.wParam == 3, "message10177 mode");
    Assert(message.nParam1 == 0 && message.nParam2 == 0 &&
        message.nParam3 == 0, "message10177 scalar payload leak");
    Assert(message.boLateDelivery, "message10177 was not delayed");
    uint delay = unchecked((uint)(message.dwDeliveryTime - before));
    Assert(delay >= 600 && delay < 700, "message10177 delay");
    Assert(message.Payload != null &&
        message.Payload.GetType().Name == "NativeMagicEffectMessagePayload",
        "message10177 typed payload");

    EqualPayload("Target", target);
    EqualPayload("RawDamage", 123);
    EqualPayload("SkillId", (ushort)33);
    EqualPayload("X", (short)10);
    EqualPayload("Y", (short)11);
    EqualPayload("Range", (ushort)1);
    EqualPayload("Arg0", true);
    EqualPayload("Flags", (byte)0);

    void EqualPayload(string name, object expected)
    {
        var property = message.Payload.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(name);
        Assert(Equals(expected, property.GetValue(message.Payload)),
            $"message10177 payload {name}");
    }
}

static void VerifyNativeSingleDamageScaling()
{
    var scale = typeof(TBaseObject).GetMethod("ScaleNativeSingleMagicDamage",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("ScaleNativeSingleMagicDamage");

    int Scale(int damage, byte race) => (int)(scale.Invoke(null,
        new object[] { damage, race }) ?? int.MinValue);

    Assert(Scale(101, 49) == 101, "single scale non-monster");
    Assert(Scale(101, 50) == 121, "single scale truncation");
    Assert(Scale(5, 50) == 6, "single scale exact integer");
}

static void VerifyNativeMagicHitChance()
{
    var chanceMethod = typeof(TBaseObject).GetMethod(
        "GetNativeMagicHitChance",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("GetNativeMagicHitChance");

    int Chance(ushort sourceType74, ushort targetAntiMagic) =>
        (int)(chanceMethod.Invoke(null,
            new object[] { sourceType74, targetAntiMagic }) ?? int.MinValue);

    Assert(Chance(0, 0) == 95, "type74 upper clamp");
    Assert(Chance(0, 1) == 90, "type74 integer division");
    Assert(Chance(0, 10) == 50, "type74 middle chance");
    Assert(Chance(0, 24) == 30, "type74 lower transition");
    Assert(Chance(0, ushort.MaxValue) == 30, "type74 lower clamp");
    Assert(Chance(ushort.MaxValue, 0) == 95,
        "type74 maximum source clamp");
}

static void VerifyTimedAbilityRefreshMatrix()
{
    var actor = new TBaseObject();
    Assert(AddInternal(actor, 26, 10, 5000), "refresh initial add");
    object node = GetTimedNode(actor, 26);

    SetNodeField(node, "LastTick", int.MinValue);
    Assert(AddInternal(actor, 26, 9, 9000), "refresh lower add");
    Assert(GetNodeField<int>(node, "Value") == 10,
        "refresh lower replaced value");
    Assert(GetNodeField<int>(node, "RemainingMilliseconds") == 5000,
        "refresh lower replaced duration");
    Assert(GetNodeField<int>(node, "LastTick") != int.MinValue,
        "refresh lower did not reset LastTick");

    SetNodeField(node, "LastTick", int.MinValue);
    Assert(AddInternal(actor, 26, 10, 7000), "refresh equal-longer add");
    Assert(GetNodeField<int>(node, "Value") == 10,
        "refresh equal-longer changed value");
    Assert(GetNodeField<int>(node, "RemainingMilliseconds") == 7000,
        "refresh equal-longer did not extend duration");
    Assert(GetNodeField<int>(node, "LastTick") != int.MinValue,
        "refresh equal-longer did not reset LastTick");

    Assert(AddInternal(actor, 26, 11, 3000), "refresh higher add");
    Assert(GetNodeField<int>(node, "Value") == 11,
        "refresh higher did not replace value");
    Assert(GetNodeField<int>(node, "RemainingMilliseconds") == 3000,
        "refresh higher did not replace duration");
}

static void VerifyTimedAbilityExpiryBatch()
{
    var actor = new TimedAbilityProbe();
    Assert(AddInternal(actor, 24, 1, 512), "expiry first add");
    Assert(AddInternal(actor, 25, 1, 512), "expiry second add");
    actor.WatchedTypes = new byte[] { 24, 25 };

    SetNodeField(GetTimedNode(actor, 24), "LastTick",
        unchecked((int)0xFFFFFF00));
    SetNodeField(GetTimedNode(actor, 25), "LastTick",
        unchecked((int)0xFFFFFF00));
    SetField(actor, "m_TimedAbilityProcessTick",
        unchecked((int)0xFFFFFF00));

    actor.ProcessTimedAbilities(0x100);

    Assert(!actor.HasNativeActiveState(24) &&
        !actor.HasNativeActiveState(25),
        "expiry batch retained active bits");
    Assert(actor.AllWatchedClearedAtFirstRemoval,
        "expiry callback ran before full batch detach");
    Assert(actor.RemovedTypes.SequenceEqual(new byte[] { 24, 25 }),
        "expiry callback order");
}

static TBaseObject NewActor()
{
    var actor = (TBaseObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TBaseObject));
    // m_wStatusTimeArr is a forwarding view over the node list now, so an
    // uninitialised actor already reads all-zero with nothing to allocate.
    return actor;
}

static bool CanAdd(TBaseObject actor, byte type)
{
    var method = typeof(TBaseObject).GetMethod("CanAddNativeTimedAbility",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("CanAddNativeTimedAbility");
    return (bool)(method.Invoke(actor, new object[] { type }) ?? false);
}

static bool AddInternal(TBaseObject actor, byte type, int value, int duration)
{
    var method = typeof(TBaseObject).GetMethod("AddTimedAbilityInternal",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("AddTimedAbilityInternal");
    return (bool)(method.Invoke(actor,
        new object[] { type, value, duration, (byte)0 }) ?? false);
}

static bool RemoveInternal(TBaseObject actor, byte type)
{
    var method = typeof(TBaseObject).GetMethod("RemoveTimedAbilityInternal",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("RemoveTimedAbilityInternal");
    return (bool)(method.Invoke(actor, new object[] { type }) ?? false);
}

static object GetTimedNode(TBaseObject actor, byte type)
{
    var method = typeof(TBaseObject).GetMethod("FindTimedAbilityNode",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("FindTimedAbilityNode");
    return method.Invoke(actor, new object[] { type })
        ?? throw new InvalidOperationException($"timed node {type}");
}

static T GetNodeField<T>(object node, string name)
{
    var field = node.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name);
    return (T)field.GetValue(node)!;
}

static void SetNodeField(object node, string name, object value)
{
    var field = node.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name);
    field.SetValue(node, value);
}

static void SetField(object target, string name, object value)
{
    var field = typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name);
    field.SetValue(target, value);
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

static void PrepareRuntimeConfig()
{
    var directory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
    File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
    File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
    var share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]");
    File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
}

static string FindRepoRoot() => AuditRepoRoot.Resolve();

static void Contains(string value, string needle, string label)
{
    Assert(value.Contains(needle, StringComparison.Ordinal), label);
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

sealed class TimedAbilityProbe : TBaseObject
{
    internal byte[] WatchedTypes { get; set; } = Array.Empty<byte>();
    internal List<byte> RemovedTypes { get; } = new();
    internal bool AllWatchedClearedAtFirstRemoval { get; private set; } = true;

    protected override void SendTimedAbilityClientState(byte internalType,
        int remainingMilliseconds, int value, bool removed)
    {
        if (!removed)
        {
            return;
        }

        if (RemovedTypes.Count == 0)
        {
            AllWatchedClearedAtFirstRemoval = WatchedTypes.All(
                type => !HasNativeActiveState(type));
        }
        RemovedTypes.Add(internalType);
    }
}
