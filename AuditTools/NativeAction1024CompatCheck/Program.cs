using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

PrepareRuntimeConfig();
InitializeRuntime();

VerifyClientRoute();
VerifyClientDispatchAndFrame();
VerifyResultOneFrameAndZeroTail();
VerifyResultZeroOrdinaryFallback();
VerifyWorkerResultLadder();
VerifyRepeatAndPostHitChain();
VerifyRootedRetryTables();
VerifySkill260ForcedProcAndLevelReload();
VerifyPositivePowerConsumesImmuneLanding();
VerifyZeroPowerPreservesCharge();

Console.WriteLine(
    "NativeAction1024CompatCheck: PASS route=CM_HIT-job3 action=0x400 " +
    "frame=12-byte-observer-only result=0/1/2 zero-tail " +
    "repeat=same-power post=root/260/poison/263 " +
    "skill154=positive-pre-delivery-power-once");
return;

static void VerifyClientRoute()
{
    string root = FindRepoRoot();
    string source = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.Attack.cs"));
    string action = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeAction1024.cs"));

    Ordered(source, "case Grobal2.CM_HIT:", "if (m_btJob == 3)",
        "RunNativeAction1024();", "AttackDir(null, 0, nDir);");
    Ordered(action, "ApplyNativeDirectMagicEffect(target",
        "RollNativeAction1024RepeatHit()",
        "TryApplyNativeAction1024Root(target);",
        "TryApplyNativeAction1024Skill260State();",
        "TryApplyNativeAction1024Poison(target);",
        "TrainNativePhysicalMagic(GetMagicInfo(263), 3);",
        "ConsumeNativeSkill154StrikeAfterPositiveAttackPower(attackPower);");
    Ordered(action, "int tailPower = 0;",
        "int result = RunNativeAction1024Swing(initialTarget);",
        "if (result == 2)",
        "RunNativePhysicalAttackCommonTail(initialTarget, tailPower);");
    Ordered(action, "TrainNativePhysicalMagic(magic,",
        "DamageSpell(unchecked((ushort)spellPoint));",
        "HealthSpellChanged();",
        "effectiveLevel = NativeEffectiveMagicLevel(magic);");
}

static void VerifyClientDispatchAndFrame()
{
    Envirnoment map = NewMap("action1024-client-route");
    Action1024Source source = NewSource(job: 3, cc: 40);
    Action1024Target right = NewTarget();
    Action1024Target left = NewTarget();
    AddActor(map, source, 10, 10);
    AddActor(map, right, 11, 10);
    AddActor(map, left, 9, 10);
    source.m_boCanHit = true;
    source.m_btDirection = Grobal2.DR_LEFT;
    source.m_wNativePhysicalTailRate = 100;
    source.m_nNativePhysicalTailAccumulator = 2;

    MethodInfo clientHit = typeof(TPlayObject).GetMethod("ClientHitXY",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[]
        {
            typeof(int), typeof(int), typeof(int), typeof(byte),
            typeof(bool), typeof(int).MakeByRefType()
        }, null) ?? throw new MissingMethodException(nameof(TPlayObject),
        "ClientHitXY");

    object[] args =
    {
        Grobal2.CM_HIT, source.m_nCurrX, source.m_nCurrY,
        (byte)Grobal2.DR_RIGHT, true, 0
    };
    WithRandom(new[] { 0, 0, 0 }, random =>
    {
        Equal(true, (bool)clientHit.Invoke(source, args)!,
            "CM_HIT accepted result");
        EqualSequence(new[] { 1, 1, 5 }, random.Bounds,
            "CM_HIT action1024 RNG");
        random.AssertExhausted("CM_HIT action1024");
    });

    Equal(Grobal2.DR_RIGHT, source.m_btDirection,
        "CM_HIT direction write before target probe");
    Equal(1, right.Calls.Count, "right-facing target carrier count");
    Equal(0, left.Calls.Count, "stale-direction target carrier count");
    AssertCall(right.Calls[0], source, 40);
    Assert(ReferenceEquals(right, source.m_TargetCret),
        "result-two common-tail target");
    Equal(2, source.m_nNativePhysicalTailAccumulator,
        "action1024 common tail must retain zero-power accumulator");
    AssertFrame(source, TBaseObject.NativeAction1024Code, 0,
        Grobal2.DR_RIGHT, 10, 10);
}

static void VerifyResultOneFrameAndZeroTail()
{
    Envirnoment map = NewMap("action1024-result-one");
    Action1024Source source = NewSource(job: 3, cc: 40);
    Action1024Target target = NewTarget();
    AddActor(map, source, 10, 10);
    AddActor(map, target, 11, 10);
    source.m_btDirection = Grobal2.DR_RIGHT;
    source.m_btHitPoint = 5;
    target.m_wSpeedPoint = 10;
    source.m_wNativePhysicalTailRate = 100;
    source.m_nNativePhysicalTailAccumulator = 2;

    WithRandom(new[] { 5 }, random =>
    {
        Equal(1, source.RunNativeAction1024(),
            "accuracy-equality dispatcher result");
        EqualSequence(new[] { 10 }, random.Bounds,
            "accuracy-equality dispatcher RNG");
        random.AssertExhausted("accuracy-equality dispatcher");
    });

    Equal(0, target.Calls.Count, "accuracy-equality carrier count");
    Equal(2, source.m_nNativePhysicalTailAccumulator,
        "result-one must skip common tail");
    Assert(source.m_TargetCret == null,
        "result-one must not set common-tail target");
    AssertFrame(source, TBaseObject.NativeAction1024Code, 0,
        Grobal2.DR_RIGHT, 10, 10);
}

static void VerifyResultZeroOrdinaryFallback()
{
    Envirnoment map = NewMap("action1024-result-zero");
    Action1024Source source = NewSource(job: 0, cc: 40);
    Action1024Target target = NewTarget();
    AddActor(map, source, 10, 10);
    AddActor(map, target, 11, 10);
    source.m_btDirection = Grobal2.DR_RIGHT;
    source.m_WAbil.DC = HUtil32.MakeLong(40, 40);

    WithRandom(new[] { 0, 0, 0 }, random =>
    {
        Equal(2, source.RunNativeAction1024(),
            "wrong-job ordinary-fallback result");
        EqualSequence(new[] { 1, 1, 5 }, random.Bounds,
            "wrong-job ordinary-fallback RNG");
        random.AssertExhausted("wrong-job ordinary fallback");
    });

    Equal(1, target.Calls.Count,
        "wrong-job ordinary-fallback carrier count");
    Action1024Call call = target.Calls[0];
    Assert(ReferenceEquals(call.Source, source),
        "ordinary-fallback direct source");
    Equal(1000, call.SkillId, "ordinary-fallback direct skill");
    Equal(true, call.Arg0, "ordinary-fallback direct arg0");
    Equal((byte)4, call.Category,
        "ordinary-fallback direct category");
    Equal(0, call.Flags, "ordinary-fallback direct flags");
    Equal(40, call.RawDamage, "ordinary-fallback rerolled DC");
    Equal((ushort)0, call.MagicIndex,
        "ordinary-fallback null magic context");
    Assert(ReferenceEquals(target, source.m_TargetCret),
        "ordinary-fallback common-tail target");
    AssertFrame(source, 1000, 0, Grobal2.DR_RIGHT, 10, 10);
}

static void VerifyWorkerResultLadder()
{
    var wrongJob = NewSource(job: 0, cc: 40);
    var target = NewTarget();
    Equal(0, wrongJob.RunNativeAction1024Swing(target),
        "wrong-job result");

    var noTarget = NewSource(job: 3, cc: 40);
    Equal(1, noTarget.RunNativeAction1024Swing(null),
        "job3 null-target result");

    var miss = NewSource(job: 3, cc: 40);
    miss.m_btHitPoint = 0;
    var missTarget = NewTarget();
    WithRandom(new[] { 0 }, random =>
    {
        Equal(1, miss.RunNativeAction1024Swing(missTarget),
            "accuracy refusal result");
        EqualSequence(new[] { 1 }, random.Bounds,
            "accuracy refusal RNG");
        random.AssertExhausted("accuracy refusal");
    });

    var hit = NewSource(job: 3, cc: 40);
    var hitTarget = NewTarget();
    WithRandom(new[] { 0, 0 }, random =>
    {
        Equal(2, hit.RunNativeAction1024Swing(hitTarget),
            "single-hit result");
        EqualSequence(new[] { 1, 1 }, random.Bounds,
            "single-hit accuracy/power RNG");
        random.AssertExhausted("single hit");
    });
    Equal(1, hitTarget.Calls.Count, "single-hit direct carrier count");
    AssertCall(hitTarget.Calls[0], hit, 40);
}

static void VerifyRepeatAndPostHitChain()
{
    var source = NewSource(job: 3, cc: 40);
    var target = NewTarget();
    AddTimed(source, 0x47, 9, -1, 0);
    AddTimed(source, 0x42, 0, -1, 0);
    AddMagic(source, 260, 0, 4);
    TUserMagic train263 = AddMagic(source, 263, 0, 4);
    source.m_WAbil.MP = 25;

    SetField(source, "m_nNativeSkill154StrikeCount", (ushort)2);
    WithRandom(new[]
    {
        0, // accuracy Random(1)
        0, // CC power Random(1)
        99, // repeat Random(100): level9 threshold 100
        0, // fresh root Random(100)
        0, // skill260 Random(100), level0 threshold10
        2, // skill260 training Random(3)+1
        0  // poison Random(100), state42 level0 threshold5
    }, random =>
    {
        Equal(2, source.RunNativeAction1024Swing(target),
            "repeat/post-hit result");
        EqualSequence(new[] { 1, 1, 100, 100, 100, 3, 100 },
            random.Bounds, "repeat/post-hit RNG order");
        random.AssertExhausted("repeat/post-hit");
    });

    Equal(2, target.Calls.Count, "repeat direct carrier count");
    AssertCall(target.Calls[0], source, 40);
    AssertCall(target.Calls[1], source, 40);
    Assert(target.HasNativeActiveState(0x2D), "fresh root state");
    Equal(4000, target.GetNativeTimedAbilityRemainingMilliseconds(0x2D),
        "fresh root level9 duration");
    Assert(target.HasNativeActiveState(0x43), "poison proc state");
    Equal(1000, target.GetNativeTimedAbilityRemainingMilliseconds(0x43),
        "poison duration");
    Assert(source.HasNativeActiveState(0x41), "skill260 state41");
    Equal(2000, source.GetNativeTimedAbilityRemainingMilliseconds(0x41),
        "skill260 state41 duration");
    Equal(0, source.m_WAbil.MP, "skill260 mana cost");
    Equal(3, source.GetMagicInfo(260).nTranPoint,
        "skill260 training points");
    Equal(3, train263.nTranPoint, "skill263 fixed training points");
    Equal((ushort)1, GetField<ushort>(source,
        "m_nNativeSkill154StrikeCount"),
        "repeat hit consumes one skill154 charge");
}

static void VerifyRootedRetryTables()
{
    var success = NewSource(job: 3, cc: 40);
    var successTarget = NewTarget();
    AddTimed(success, 0x47, 1, -1, 0);
    AddTimed(successTarget, 0x2D, 1, 500, 0);
    WithRandom(new[] { 0, 0, 99, 1 }, random =>
    {
        Equal(2, success.RunNativeAction1024Swing(successTarget),
            "rooted retry success result");
        EqualSequence(new[] { 1, 1, 100, 100 }, random.Bounds,
            "rooted retry success RNG");
        random.AssertExhausted("rooted retry success");
    });
    Equal(1000,
        successTarget.GetNativeTimedAbilityRemainingMilliseconds(0x2D),
        "rooted level-one duration");

    var equality = NewSource(job: 3, cc: 40);
    var equalityTarget = NewTarget();
    AddTimed(equality, 0x47, 1, -1, 0);
    AddTimed(equalityTarget, 0x2D, 1, 500, 0);
    WithRandom(new[] { 0, 0, 99, 2 }, random =>
    {
        Equal(2, equality.RunNativeAction1024Swing(equalityTarget),
            "rooted retry equality result");
        EqualSequence(new[] { 1, 1, 100, 100 }, random.Bounds,
            "rooted retry equality RNG");
        random.AssertExhausted("rooted retry equality");
    });
    Equal(500,
        equalityTarget.GetNativeTimedAbilityRemainingMilliseconds(0x2D),
        "rooted chance equality must fail");
}

static void VerifySkill260ForcedProcAndLevelReload()
{
    var forced = NewSource(job: 3, cc: 40);
    var forcedTarget = NewTarget();
    TUserMagic forcedMagic = AddMagic(forced, 260, 4, 4);
    forced.m_WAbil.MP = 40;
    AddTimed(forced, 0x46, 1, -1, 0);
    WithRandom(new[] { 0, 0, 99, 0 }, random =>
    {
        Equal(2, forced.RunNativeAction1024Swing(forcedTarget),
            "state46 forced-proc result");
        EqualSequence(new[] { 1, 1, 100, 3 }, random.Bounds,
            "state46 forced-proc RNG");
        random.AssertExhausted("state46 forced proc");
    });
    Assert(!forced.HasNativeActiveState(0x46),
        "state46 without magic264 must be removed");
    Assert(forced.HasNativeActiveState(0x41),
        "state46 forced proc state41");
    Equal(10000,
        forced.GetNativeTimedAbilityRemainingMilliseconds(0x41),
        "state46 level-four state41 duration");
    Equal(0, forced.m_WAbil.MP, "state46 exact mana payment");
    Equal(1, forcedMagic.nTranPoint, "state46 training points");

    var leveled = NewSource(job: 3, cc: 40);
    var leveledTarget = NewTarget();
    TUserMagic leveledMagic = AddMagic(leveled, 260, 0, 4);
    leveledMagic.MagicInfo.MaxTrain[0] = 1;
    leveled.m_WAbil.MP = 25;
    WithRandom(new[] { 0, 0, 0, 0 }, random =>
    {
        Equal(2, leveled.RunNativeAction1024Swing(leveledTarget),
            "skill260 level-reload result");
        EqualSequence(new[] { 1, 1, 100, 3 }, random.Bounds,
            "skill260 level-reload RNG");
        random.AssertExhausted("skill260 level reload");
    });
    Equal((byte)1, leveledMagic.btLevel,
        "skill260 training level-up");
    Equal(0, leveledMagic.nTranPoint,
        "skill260 post-level training remainder");
    Equal(0, leveled.m_WAbil.MP, "skill260 level-up exact mana payment");
    Equal(4000,
        leveled.GetNativeTimedAbilityRemainingMilliseconds(0x41),
        "skill260 duration must reload post-training level");
}

static void VerifyPositivePowerConsumesImmuneLanding()
{
    var source = NewSource(job: 3, cc: 40);
    var target = NewTarget();
    target.SetNativeActiveState(55);
    SetField(source, "m_nNativeSkill154StrikeCount", (ushort)1);

    WithRandom(new[] { 0, 0 }, random =>
    {
        Equal(2, source.RunNativeAction1024Swing(target),
            "immune-target worker result");
        EqualSequence(new[] { 1, 1 }, random.Bounds,
            "immune-target accuracy/power RNG");
        random.AssertExhausted("immune target");
    });
    Equal((ushort)0, GetField<ushort>(source,
        "m_nNativeSkill154StrikeCount"),
        "positive rolled power must consume despite zero applied damage");
}

static void VerifyZeroPowerPreservesCharge()
{
    var source = NewSource(job: 3, cc: 0);
    var target = NewTarget();
    SetField(source, "m_nNativeSkill154StrikeCount", (ushort)1);

    WithRandom(new[] { 0, 0 }, random =>
    {
        Equal(2, source.RunNativeAction1024Swing(target),
            "zero-power worker result");
        EqualSequence(new[] { 1, 1 }, random.Bounds,
            "zero-power accuracy/power RNG");
        random.AssertExhausted("zero power");
    });
    Equal(1, target.Calls.Count, "zero-power direct carrier count");
    AssertCall(target.Calls[0], source, 0);
    Equal((ushort)1, GetField<ushort>(source,
        "m_nNativeSkill154StrikeCount"),
        "zero rolled power must preserve skill154 charge");
}

static Action1024Source NewSource(byte job, int cc)
{
    var actor = new Action1024Source
    {
        m_btJob = job,
        m_btHitPoint = 100,
        m_boObMode = true,
        m_sCharName = "action1024-source"
    };
    actor.m_Abil.Level = 100;
    actor.m_WAbil.Level = 100;
    SetNativeCc(actor, cc, cc);
    actor.m_WAbil.MP = 100;
    actor.m_WAbil.MaxMP = 100;
    return actor;
}

static Envirnoment NewMap(string name)
{
    var map = new Envirnoment
    {
        sMapName = name,
        sMapDesc = name,
        m_sMapFileName = name
    };
    MethodInfo initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingMethodException(nameof(Envirnoment), "Initialize");
    initialize.Invoke(map, new object[] { (short)32, (short)32 });
    return map;
}

static void AddActor(Envirnoment map, TBaseObject actor, short x, short y)
{
    actor.m_PEnvir = map;
    actor.m_sMapName = map.sMapName;
    actor.m_nCurrX = x;
    actor.m_nCurrY = y;
    map.AddToMap(x, y, CellType.OS_MOVINGOBJECT, actor);
}

static Action1024Target NewTarget()
{
    return new Action1024Target
    {
        m_boObMode = true,
        m_wSpeedPoint = 1,
        m_sCharName = "action1024-target",
        m_WAbil = { HP = 10000, MaxHP = 10000 }
    };
}

static TUserMagic AddMagic(TBaseObject actor, int id, byte level,
    byte trainLevel)
{
    var magic = new TUserMagic
    {
        btLevel = level,
        wMagIdx = unchecked((ushort)id),
        MagicInfo = new TMagic
        {
            wMagicID = unchecked((ushort)id),
            btTrainLv = trainLevel,
            TrainLevel = new byte[] { 0, 0, 0, 0, 0 },
            MaxTrain = new int[] { int.MaxValue, int.MaxValue,
                int.MaxValue, int.MaxValue, int.MaxValue }
        }
    };
    actor.m_MagicList.Add(magic);
    return magic;
}

static void AssertCall(Action1024Call call, TBaseObject source, int power)
{
    Assert(ReferenceEquals(call.Source, source), "direct source");
    Equal(TBaseObject.NativeAction1024Code, call.SkillId, "direct skill");
    Equal(true, call.Arg0, "direct arg0");
    Equal((byte)4, call.Category, "direct category");
    Equal(0, call.Flags, "direct flags");
    Equal(power, call.RawDamage, "direct shared power");
    Equal((ushort)0, call.MagicIndex, "direct null magic context");
}

static void AssertFrame(TBaseObject source, int action, int level,
    byte direction, short x, short y)
{
    SendMessage message = source.m_MsgList.Single(entry =>
        entry.wIdent == Grobal2.RM_PHYSICAL_ATT);
    Equal(action, message.wParam, "physical-frame action param");
    Equal((int)x, message.nParam1, "physical-frame x param");
    Equal((int)y, message.nParam2, "physical-frame y param");
    var payload = message.Payload as NativePhysicalAttackFramePayload ??
        throw new InvalidOperationException("physical-frame payload type");
    Equal(false, payload.IncludeSource,
        "physical-frame include-source flag");
    Equal(12, payload.Body.Length, "physical-frame body length");
    Equal(unchecked((ushort)action),
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Body.AsSpan(0, 2)),
        "physical-frame body action");
    Equal(unchecked((ushort)level),
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Body.AsSpan(2, 2)),
        "physical-frame body level");
    Equal((ushort)0,
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Body.AsSpan(4, 2)),
        "physical-frame body reserved");
    Equal(unchecked((ushort)direction),
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Body.AsSpan(6, 2)),
        "physical-frame body direction");
    Equal(unchecked((ushort)x),
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Body.AsSpan(8, 2)),
        "physical-frame body x");
    Equal(unchecked((ushort)y),
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Body.AsSpan(10, 2)),
        "physical-frame body y");
}

static void AddTimed(TBaseObject actor, byte type, int value, int duration,
    byte flag)
{
    Assert(actor.AddTimedAbilityInternal(type, value, duration, flag),
        $"state {type:X2} setup");
}

static void SetNativeCc(TBaseObject actor, int low, int high)
{
    object ability = GetField<object>(actor, "m_NativeCoreWorkingAbility");
    Type type = ability.GetType();
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                               BindingFlags.NonPublic;
    type.GetField("CCLow", flags)!.SetValue(ability, low);
    type.GetField("CCHigh", flags)!.SetValue(ability, high);
    SetField(actor, "m_NativeCoreWorkingAbility", ability);
}

static void WithRandom(IEnumerable<int> values,
    Action<RecordingRandom> action)
{
    RandomNumber original = M2Share.RandomNumber;
    var random = new RecordingRandom(values);
    M2Share.RandomNumber = random;
    try { action(random); }
    finally { M2Share.RandomNumber = original ?? RandomNumber.GetInstance(); }
}

static void Ordered(string source, params string[] values)
{
    int position = -1;
    foreach (string value in values)
    {
        int next = source.IndexOf(value, position + 1,
            StringComparison.Ordinal);
        Assert(next > position, "source order missing: " + value);
        position = next;
    }
}

static T GetField<T>(object instance, string name)
{
    FieldInfo field = instance.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic) ?? typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic) ?? throw new MissingFieldException(name);
    return (T)field.GetValue(instance)!;
}

static void SetField(object instance, string name, object value)
{
    FieldInfo field = instance.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic) ?? typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic) ?? throw new MissingFieldException(name);
    field.SetValue(instance, value);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void EqualSequence(IEnumerable<int> expected,
    IEnumerable<int> actual, string label)
{
    int[] left = expected.ToArray();
    int[] right = actual.ToArray();
    if (!left.SequenceEqual(right))
        throw new InvalidOperationException(
            $"{label}: expected=[{string.Join(',', left)}], " +
            $"actual=[{string.Join(',', right)}]");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

static string FindRepoRoot()
{
    foreach (string start in new[]
             { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
            {
                return directory.FullName;
            }
        }
    }
    throw new DirectoryNotFoundException("repo root");
}

static void PrepareRuntimeConfig()
{
    string directory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
    File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
    File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
    string share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]");
    File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

sealed class Action1024Source : TPlayObject
{
    public override bool IsAttackTarget(TBaseObject target) =>
        target != null && !ReferenceEquals(target, this);

    internal override void SendSocket(ClientPacket packet, string text) { }
    internal override void SendSocket(ClientPacket packet, byte[] body) { }
}

sealed class Action1024Target : TBaseObject
{
    internal List<Action1024Call> Calls { get; } = new();

    internal override int ResolveFullMagicDamage(TBaseObject source,
        int skillId, bool arg0, MagicDamageContext context, byte category,
        int flags, int rawDamage)
    {
        Calls.Add(new Action1024Call(source, skillId, arg0, category,
            flags, rawDamage, context?.MagicIndex ?? 0));
        return HasNativeActiveState(55) ? 0 : rawDamage;
    }
}

readonly record struct Action1024Call(TBaseObject Source, int SkillId,
    bool Arg0, byte Category, int Flags, int RawDamage, ushort MagicIndex);

sealed class RecordingRandom : RandomNumber
{
    private readonly Queue<int> _values;

    internal RecordingRandom(IEnumerable<int> values)
    {
        _values = new Queue<int>(values);
    }

    internal List<int> Bounds { get; } = new();

    public override int Random(int value)
    {
        Bounds.Add(value);
        if (_values.Count == 0)
            throw new InvalidOperationException("unexpected RNG call");
        int result = _values.Dequeue();
        if (result < 0 || result >= value)
            throw new ArgumentOutOfRangeException(nameof(result));
        return result;
    }

    public override int Random() => throw new InvalidOperationException(
        "unexpected Random() call");
    public override int Random(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected Random(min,max) call");
    public override int GetRandomNumber(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected GetRandomNumber call");

    internal void AssertExhausted(string label)
    {
        if (_values.Count != 0)
            throw new InvalidOperationException(
                $"{label}: {_values.Count} unused RNG value(s)");
    }
}
