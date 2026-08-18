using GameSvr;
using GameSvr.Services;

CheckAllNineSuccessfulOrders();
CheckAllNinePublicationFailureOrders();
CheckFalseSkillResultsContinue();
CheckExceptionShortCircuit();
CheckStaticContracts();

Console.WriteLine("PASS NativeFieldHeroInitializeCoreCheck " +
                  "classes=9 common=sub_60A3B4 dota=sub_60C694 " +
                  "publication-gate=post-base skills=try-continue " +
                  "writes=exact production=NO-GO");

static void CheckAllNineSuccessfulOrders()
{
    foreach (var testCase in Cases())
    {
        var result = Execute(testCase, publicationFailed: false);
        SequenceEqual(ExpectedEvents(testCase, publicationFailed: false),
            result.Events, testCase.ActorKind + " successful order");
        Equal((byte)0x93, result.Bytes[0x155],
            testCase.ActorKind + " successful name color");
        Equal(testCase.IsDota ? (byte)0 : (byte)0xFF,
            result.Bytes[0x47C], testCase.ActorKind + " successful marker");
        Equal(1003, result.Int32[0x2AC],
            testCase.ActorKind + " successful HP mirror");
        Equal(2007, result.Int32[0x2B4],
            testCase.ActorKind + " successful MP mirror");
    }
}

static void CheckAllNinePublicationFailureOrders()
{
    foreach (var testCase in Cases())
    {
        var result = Execute(testCase, publicationFailed: true);
        SequenceEqual(ExpectedEvents(testCase, publicationFailed: true),
            result.Events, testCase.ActorKind + " publication failure order");
        Equal((byte)0x22, result.Bytes[0x155],
            testCase.ActorKind + " publication failure preserves name color");
        Equal(testCase.IsDota ? (byte)0 : (byte)0xFF,
            result.Bytes[0x47C],
            testCase.ActorKind + " publication failure marker");
        Equal(17, result.Int32[0x2AC],
            testCase.ActorKind + " publication failure preserves HP");
        Equal(19, result.Int32[0x2B4],
            testCase.ActorKind + " publication failure preserves MP");
    }
}

static void CheckFalseSkillResultsContinue()
{
    foreach (var testCase in Cases())
    {
        for (var falseIndex = 0;
             falseIndex < testCase.Skills.Length;
             falseIndex++)
        {
            var result = Execute(testCase, publicationFailed: false,
                falseSkillIndex: falseIndex);
            SequenceEqual(ExpectedEvents(testCase, publicationFailed: false),
                result.Events,
                testCase.ActorKind + $" false skill result {falseIndex}");
            Equal(testCase.IsDota ? (byte)0 : (byte)0xFF,
                result.Bytes[0x47C],
                testCase.ActorKind + $" false skill marker {falseIndex}");
        }
    }
}

static void CheckExceptionShortCircuit()
{
    var sentinel = new ApplicationException("sentinel");
    var commonEvents = ExpectedCommonEvents(
        publicationFailed: false, isDota: true);
    for (var targetIndex = 0; targetIndex < commonEvents.Count; targetIndex++)
    {
        var events = new List<string>();
        var target = commonEvents[targetIndex];
        void Hit(string current)
        {
            events.Add(current);
            if (current == target) throw sentinel;
        }

        try
        {
            NativeFieldHeroInitializeCore.RunDotaCommon(
                () => Hit("inherited"),
                () =>
                {
                    Hit("read:156");
                    return false;
                },
                () => Hit("aggregate"),
                () => Hit("ability"),
                () => Hit("recalculate"),
                (offset, value) => Hit(ByteEvent(offset, value)),
                (source, destination) =>
                    Hit(CopyEvent(source, destination)));
            throw new Exception("common exception was swallowed at " + target);
        }
        catch (ApplicationException ex)
        {
            Check(ReferenceEquals(sentinel, ex),
                "exact common exception propagates at " + target);
        }

        SequenceEqual(commonEvents.Take(targetIndex + 1).ToArray(), events,
            "common exception stops after " + target);
    }

    foreach (var testCase in Cases())
    {
        var events = new List<string>();
        try
        {
            NativeFieldHeroInitializeCore.RunForClass(
                testCase.ActorKind,
                skill =>
                {
                    events.Add(SkillEvent(skill.MagicId, skill.Level));
                    return true;
                },
                () => events.Add("inherited"),
                () =>
                {
                    events.Add("read:156");
                    return false;
                },
                () =>
                {
                    events.Add("aggregate");
                    throw sentinel;
                },
                () => events.Add("ability"),
                () => events.Add("recalculate"),
                (offset, value) => events.Add(ByteEvent(offset, value)),
                (source, destination) =>
                    events.Add(CopyEvent(source, destination)));
            throw new Exception(
                testCase.ActorKind + " wrapper swallowed common exception");
        }
        catch (ApplicationException ex)
        {
            Check(ReferenceEquals(sentinel, ex),
                testCase.ActorKind + " exact common exception propagates");
        }

        var expected = new List<string>();
        if (testCase.SkillsBefore)
        {
            expected.AddRange(testCase.Skills.Select(skill =>
                SkillEvent(skill.MagicId, skill.Level)));
        }
        expected.AddRange(new[] { "inherited", "read:156", "aggregate" });
        SequenceEqual(expected, events,
            testCase.ActorKind + " wrapper common exception prefix");
    }

    foreach (var skillsBefore in Cases().Where(testCase =>
                 testCase.SkillsBefore))
    {
        for (var targetIndex = 0;
             targetIndex < skillsBefore.Skills.Length;
             targetIndex++)
        {
            var events = new List<string>();
            var skillIndex = 0;
            try
            {
                NativeFieldHeroInitializeCore.RunForClass(
                    skillsBefore.ActorKind,
                    skill =>
                    {
                        events.Add(SkillEvent(skill.MagicId, skill.Level));
                        if (skillIndex++ == targetIndex) throw sentinel;
                        return true;
                    },
                    () => events.Add("inherited"),
                    () => false,
                    () => events.Add("aggregate"),
                    () => events.Add("ability"),
                    () => events.Add("recalculate"),
                    (offset, value) => events.Add(ByteEvent(offset, value)),
                    (source, destination) =>
                        events.Add(CopyEvent(source, destination)));
                throw new Exception(skillsBefore.ActorKind +
                    " skill-before exception was swallowed at " + targetIndex);
            }
            catch (ApplicationException ex)
            {
                Check(ReferenceEquals(sentinel, ex), skillsBefore.ActorKind +
                    " exact skill-before exception propagates at " + targetIndex);
            }

            SequenceEqual(skillsBefore.Skills.Take(targetIndex + 1)
                    .Select(skill => SkillEvent(skill.MagicId, skill.Level))
                    .ToArray(),
                events, skillsBefore.ActorKind +
                        " skill-before exception prefix " + targetIndex);
        }
    }

    foreach (var skillsAfter in Cases().Where(testCase =>
                 !testCase.SkillsBefore && !testCase.NoSkills))
    {
        for (var targetIndex = 0;
             targetIndex < skillsAfter.Skills.Length;
             targetIndex++)
        {
            var events = new List<string>();
            var skillIndex = 0;
            try
            {
                NativeFieldHeroInitializeCore.RunForClass(
                    skillsAfter.ActorKind,
                    skill =>
                    {
                        events.Add(SkillEvent(skill.MagicId, skill.Level));
                        if (skillIndex++ == targetIndex) throw sentinel;
                        return true;
                    },
                    () => events.Add("inherited"),
                    () =>
                    {
                        events.Add("read:156");
                        return false;
                    },
                    () => events.Add("aggregate"),
                    () => events.Add("ability"),
                    () => events.Add("recalculate"),
                    (offset, value) => events.Add(ByteEvent(offset, value)),
                    (source, destination) =>
                        events.Add(CopyEvent(source, destination)));
                throw new Exception(skillsAfter.ActorKind +
                    " skill-after exception was swallowed at " + targetIndex);
            }
            catch (ApplicationException ex)
            {
                Check(ReferenceEquals(sentinel, ex), skillsAfter.ActorKind +
                    " exact skill-after exception propagates at " + targetIndex);
            }

            var expected = new List<string>(ExpectedCommonEvents(
                publicationFailed: false, isDota: skillsAfter.IsDota));
            expected.AddRange(skillsAfter.Skills.Take(targetIndex + 1)
                .Select(skill => SkillEvent(skill.MagicId, skill.Level)));
            SequenceEqual(expected, events, skillsAfter.ActorKind +
                " skill-after exception prefix " + targetIndex);
        }
    }
}

static void CheckStaticContracts()
{
    Equal(0x0071D904u, NativeFieldHeroInitializeCore.InheritedInitialize,
        "inherited Initialize address");
    Equal(0x0075EE78u, NativeFieldHeroInitializeCore.EquipmentAggregate,
        "equipment aggregate address");
    Equal(0x0060A5D4u, NativeFieldHeroInitializeCore.CommonRecalculate,
        "common recalculate address");
    Equal(0x155, NativeFieldHeroInitializeCore.NameColorOffset,
        "name-color offset");
    Equal(0x156, NativeFieldHeroInitializeCore.MapPublicationFailedOffset,
        "map-publication failure offset");
    Equal(0x2AC, NativeFieldHeroInitializeCore.CurrentHpOffset,
        "current HP offset");
    Equal(0x2B0, NativeFieldHeroInitializeCore.MaxHpOffset,
        "max HP offset");
    Equal(0x2B4, NativeFieldHeroInitializeCore.CurrentMpOffset,
        "current MP offset");
    Equal(0x2B8, NativeFieldHeroInitializeCore.MaxMpOffset,
        "max MP offset");
    Equal(0x47C, NativeFieldHeroInitializeCore.InitializeMarkerOffset,
        "Initialize marker offset");
    Equal(0x0060A3B4u, NativeFieldHeroNineClasses.CommonInitialize,
        "ordinary common Initialize address");
    Equal(0x0060C694u, NativeFieldHeroNineClasses.DotaCommonInitialize,
        "Dota common Initialize address");

    foreach (var testCase in Cases())
    {
        var contract = NativeFieldHeroNineClasses.Get(testCase.ActorKind);
        Equal(testCase.IsDota
                ? NativeFieldHeroCommonInitializeKind.Dota
                : NativeFieldHeroCommonInitializeKind.Ordinary,
            contract.CommonInitializeKind,
            testCase.ActorKind + " explicit common Initialize kind");
    }

    Check(!TFieldHero.ProductionReady,
        "Initialize core must not open FieldHero production");
}

static ExecutionResult Execute(InitializeCase testCase,
    bool publicationFailed, int falseSkillIndex = -1)
{
    var events = new List<string>();
    var bytes = new Dictionary<int, byte>
    {
        [0x155] = 0x22,
        [0x47C] = 0x41
    };
    var int32 = new Dictionary<int, int>
    {
        [0x2AC] = 17,
        [0x2B0] = 1003,
        [0x2B4] = 19,
        [0x2B8] = 2007
    };
    var nativePublicationFailed = !publicationFailed;
    var skillIndex = 0;

    NativeFieldHeroInitializeCore.RunForClass(
        testCase.ActorKind,
        skill =>
        {
            events.Add(SkillEvent(skill.MagicId, skill.Level));
            return skillIndex++ != falseSkillIndex;
        },
        () =>
        {
            events.Add("inherited");
            nativePublicationFailed = publicationFailed;
        },
        () =>
        {
            events.Add("read:156");
            return nativePublicationFailed;
        },
        () => events.Add("aggregate"),
        () => events.Add("ability"),
        () => events.Add("recalculate"),
        (offset, value) =>
        {
            events.Add(ByteEvent(offset, value));
            bytes[offset] = value;
        },
        (source, destination) =>
        {
            events.Add(CopyEvent(source, destination));
            int32[destination] = int32[source];
        });

    return new ExecutionResult(events, bytes, int32);
}

static IReadOnlyList<string> ExpectedEvents(InitializeCase testCase,
    bool publicationFailed)
{
    var result = new List<string>();
    if (testCase.SkillsBefore)
    {
        result.AddRange(testCase.Skills.Select(skill =>
            SkillEvent(skill.MagicId, skill.Level)));
    }

    result.AddRange(ExpectedCommonEvents(publicationFailed, testCase.IsDota));

    if (!testCase.SkillsBefore && !testCase.NoSkills)
    {
        result.AddRange(testCase.Skills.Select(skill =>
            SkillEvent(skill.MagicId, skill.Level)));
    }
    return result;
}

static IReadOnlyList<string> ExpectedCommonEvents(bool publicationFailed,
    bool isDota)
{
    var result = new List<string> { "inherited", "read:156" };
    if (!publicationFailed)
    {
        result.Add("aggregate");
        result.Add("ability");
        result.Add("recalculate");
        result.Add("write:155:93");
        result.Add("copy:2B0->2AC");
        result.Add("copy:2B8->2B4");
    }
    result.Add("write:47C:FF");
    if (isDota) result.Add("write:47C:00");
    return result;
}

static InitializeCase[] Cases() => new[]
{
    Before(NativeType2FieldHeroActorKind.FieldWarHero, false,
        S(3, 3), S(12, 3), S(26, 3), S(34, 3)),
    Before(NativeType2FieldHeroActorKind.FieldWizHero, false,
        S(11, 3), S(35, 3), S(31, 3), S(10, 3)),
    Before(NativeType2FieldHeroActorKind.FieldTaosHero, false,
        S(4, 3), S(6, 3), S(13, 3), S(36, 3)),
    After(NativeType2FieldHeroActorKind.FieldAssHero, false,
        S(260, 4), S(264, 4), S(268, 4)),
    Before(NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar, true,
        S(3, 3), S(12, 3), S(26, 3), S(34, 3)),
    Before(NativeType2FieldHeroActorKind.MirDotaMatchHumMonWiz, true,
        S(11, 3), S(35, 3), S(31, 3), S(10, 3)),
    Before(NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos, true,
        S(4, 3), S(6, 3), S(13, 3), S(36, 3)),
    After(NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss, true,
        S(260, 4), S(264, 4), S(268, 4)),
    None(NativeType2FieldHeroActorKind.ModelHero)
};

static InitializeCase Before(NativeType2FieldHeroActorKind kind, bool dota,
    params ExpectedSkill[] skills) => new(kind, dota, true, false, skills);

static InitializeCase After(NativeType2FieldHeroActorKind kind, bool dota,
    params ExpectedSkill[] skills) => new(kind, dota, false, false, skills);

static InitializeCase None(NativeType2FieldHeroActorKind kind) =>
    new(kind, false, false, true, Array.Empty<ExpectedSkill>());

static ExpectedSkill S(int magicId, int level) => new(magicId, level);

static string SkillEvent(int magicId, int level) =>
    $"skill:{magicId}:{level}";

static string ByteEvent(int offset, byte value) =>
    $"write:{offset:X}:{value:X2}";

static string CopyEvent(int source, int destination) =>
    $"copy:{source:X}->{destination:X}";

static void SequenceEqual(IReadOnlyList<string> expected,
    IReadOnlyList<string> actual, string label)
{
    Equal(expected.Count, actual.Count, label + " count");
    for (var i = 0; i < expected.Count; i++)
    {
        Equal(expected[i], actual[i], label + $"[{i}]");
    }
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"{label}: expected {expected}, got {actual}");
    }
}

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
}

sealed class ExecutionResult
{
    public ExecutionResult(List<string> events,
        Dictionary<int, byte> bytes, Dictionary<int, int> int32)
    {
        Events = events;
        Bytes = bytes;
        Int32 = int32;
    }

    public List<string> Events { get; }
    public Dictionary<int, byte> Bytes { get; }
    public Dictionary<int, int> Int32 { get; }
}

sealed class InitializeCase
{
    public InitializeCase(NativeType2FieldHeroActorKind actorKind, bool isDota,
        bool skillsBefore, bool noSkills, ExpectedSkill[] skills)
    {
        ActorKind = actorKind;
        IsDota = isDota;
        SkillsBefore = skillsBefore;
        NoSkills = noSkills;
        Skills = skills;
    }

    public NativeType2FieldHeroActorKind ActorKind { get; }
    public bool IsDota { get; }
    public bool SkillsBefore { get; }
    public bool NoSkills { get; }
    public ExpectedSkill[] Skills { get; }
}

readonly struct ExpectedSkill
{
    public ExpectedSkill(int magicId, int level)
    {
        MagicId = magicId;
        Level = level;
    }

    public int MagicId { get; }
    public int Level { get; }
}
