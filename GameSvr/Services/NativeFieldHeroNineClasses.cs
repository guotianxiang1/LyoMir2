using System.Collections.ObjectModel;

namespace GameSvr.Services
{
    // NativeType2FieldHeroActorKind (the nine concrete selector outcomes) is
    // defined in NativeType2FieldHeroSpawnPlanFactory.cs and reused here.

    /// <summary>
    /// One ability-block field the native VMT+0x2C initializer touches. Offsets
    /// are relative to the actor ability block at actor+0x1E8, except the three
    /// working mirrors which live directly on the actor.
    /// </summary>
    public enum NativeFieldHeroAbilitySlot
    {
        AcLow = 0x18,
        AcHigh = 0x1C,
        MacLow = 0x20,
        MacHigh = 0x24,
        DcLow = 0x28,
        DcHigh = 0x2C,
        McLow = 0x30,
        McHigh = 0x34,
        ScLow = 0x38,
        ScHigh = 0x3C,
        CcLow = 0x40,
        CcHigh = 0x44,
        CurrentHp = 0x48,
        MaxHp = 0x4C,
        CurrentMp = 0x50,
        MaxMp = 0x54,

        // Working mirrors on the actor (not inside the ability block).
        WorkingMaxHpMirror = 0x2B0, // TModelHero only
        WorkingHpMirror = 0x2AC,
        WorkingMpMirror = 0x2B4
    }

    /// <summary>
    /// How the native code applies a computed value to a slot. Assign is a
    /// plain store. ClampDownTo mirrors the native
    /// <c>if (new &lt; existing) existing = new;</c> guard used for current
    /// HP/MP and their working mirrors: it only lowers, never raises.
    /// </summary>
    public enum NativeFieldHeroAbilityWriteMode
    {
        Assign,
        ClampDownTo
    }

    /// <summary>
    /// One deterministic write performed by a class VMT+0x2C ability
    /// initializer, in native execution order. This is evidence of exactly
    /// which slots the initializer touches: a slot with no write is left at its
    /// prior value (equipment aggregation output or construction zero-fill),
    /// which the native Dota initializers rely on for AC/MAC.
    /// </summary>
    public readonly struct NativeFieldHeroAbilityWrite
    {
        public NativeFieldHeroAbilityWrite(NativeFieldHeroAbilitySlot slot,
            NativeFieldHeroAbilityWriteMode mode, int value)
        {
            Slot = slot;
            Mode = mode;
            Value = value;
        }

        public NativeFieldHeroAbilitySlot Slot { get; }
        public NativeFieldHeroAbilityWriteMode Mode { get; }
        public int Value { get; }
    }

    /// <summary>
    /// The full ordered write list a class VMT+0x2C initializer performs for a
    /// given level. Applying it over a slot map reproduces the native ability
    /// block exactly (including the intentional current-MP asymmetry of
    /// TModelHero and the unwritten AC/MAC of the Dota classes).
    /// </summary>
    public sealed class NativeFieldHeroAbilityResult
    {
        private readonly ReadOnlyCollection<NativeFieldHeroAbilityWrite> _writes;

        internal NativeFieldHeroAbilityResult(int level,
            NativeFieldHeroAbilityWrite[] writes)
        {
            Level = level;
            _writes = Array.AsReadOnly(writes);
        }

        /// <summary>The ushort actor+0x1FC level the formulas consumed.</summary>
        public int Level { get; }

        /// <summary>Writes in native execution order.</summary>
        public IReadOnlyList<NativeFieldHeroAbilityWrite> Writes => _writes;

        /// <summary>
        /// Applies the writes over an initial slot map (defaulting missing slots
        /// to zero), honouring Assign and ClampDownTo, and returns the resulting
        /// slot values. Slots the initializer never writes are absent from the
        /// result unless present in <paramref name="initial"/>.
        /// </summary>
        public IReadOnlyDictionary<NativeFieldHeroAbilitySlot, int> Apply(
            IReadOnlyDictionary<NativeFieldHeroAbilitySlot, int> initial)
        {
            var state = new Dictionary<NativeFieldHeroAbilitySlot, int>();
            if (initial != null)
            {
                foreach (var pair in initial) state[pair.Key] = pair.Value;
            }

            foreach (var write in _writes)
            {
                if (write.Mode == NativeFieldHeroAbilityWriteMode.Assign)
                {
                    state[write.Slot] = write.Value;
                    continue;
                }

                // ClampDownTo: lower an existing value only. An absent prior
                // value is treated as native zero-fill, so a positive maximum
                // never overwrites it.
                var existing = state.TryGetValue(write.Slot, out var current)
                    ? current
                    : 0;
                if (write.Value < existing) state[write.Slot] = write.Value;
            }
            return state;
        }
    }

    /// <summary>One learned magic: the native (magicId, level) append pair.</summary>
    public readonly struct NativeFieldHeroSkill
    {
        public NativeFieldHeroSkill(int magicId, int level)
        {
            MagicId = magicId;
            Level = level;
        }

        public int MagicId { get; }
        public int Level { get; }
    }

    /// <summary>
    /// Where the class Initialize override (VMT+0x78) appends its learned magic
    /// relative to the common Initialize body.
    /// </summary>
    public enum NativeFieldHeroInitOrder
    {
        /// <summary>Append skills, then call the common Initialize.</summary>
        SkillsBeforeInitialize,

        /// <summary>Call the common Initialize, then append skills (assassin).</summary>
        InitializeBeforeSkills,

        /// <summary>No skill-init override; the inherited Initialize runs alone.</summary>
        NoSkills
    }

    /// <summary>The common VMT+0x78 body selected by a concrete class.</summary>
    public enum NativeFieldHeroCommonInitializeKind
    {
        Ordinary,
        Dota
    }

    /// <summary>
    /// The deterministic construction/initialization contract of one concrete
    /// FieldHero class: identity, native addresses, constructor job byte, the
    /// ordered learned-magic set, the skill/Initialize ordering, and the exact
    /// VMT+0x2C ability writes as a pure function of the actor level.
    ///
    /// This models only what is a pure function of the level and of static
    /// class identity. The per-class runtime AI (attack/cast/special-attack
    /// slots +0x218/+0x21C/+0x220/+0x224), the base-constructor RNG field
    /// +0x5F8, the fame selector mutation, and placement RNG are non-deterministic
    /// or global-consumer and are documented as blockers in
    /// staging/fieldhero_nine_classes_ai_20260731.md rather than modelled here.
    /// </summary>
    public sealed class NativeFieldHeroClassContract
    {
        private readonly ReadOnlyCollection<NativeFieldHeroSkill> _skills;
        private readonly Func<int, NativeFieldHeroAbilityResult> _abilities;

        internal NativeFieldHeroClassContract(
            NativeType2FieldHeroActorKind actorKind, string rttiName,
            int? selector, byte jobByte, uint classPointerVar, uint vmt,
            uint constructor, int instanceSize, uint abilityInit,
            uint? skillInit, NativeFieldHeroInitOrder initOrder,
            NativeFieldHeroCommonInitializeKind commonInitializeKind,
            NativeFieldHeroSkill[] skills,
            Func<int, NativeFieldHeroAbilityResult> abilities)
        {
            ActorKind = actorKind;
            RttiName = rttiName;
            Selector = selector;
            JobByte = jobByte;
            ClassPointerVariable = classPointerVar;
            Vmt = vmt;
            Constructor = constructor;
            InstanceSize = instanceSize;
            AbilityInitializer = abilityInit;
            SkillInitializer = skillInit;
            InitOrder = initOrder;
            CommonInitializeKind = commonInitializeKind;
            _skills = Array.AsReadOnly(skills);
            _abilities = abilities;
        }

        public NativeType2FieldHeroActorKind ActorKind { get; }
        public string RttiName { get; }

        /// <summary>The fame selector byte (0..7); null for the default TModelHero.</summary>
        public int? Selector { get; }

        /// <summary>The value the constructor writes to actor+0x72 (0..3); 0 for TModelHero (unset).</summary>
        public byte JobByte { get; }

        public uint ClassPointerVariable { get; }
        public uint Vmt { get; }
        public uint Constructor { get; }
        public int InstanceSize { get; }

        /// <summary>Class VMT+0x2C ability initializer address.</summary>
        public uint AbilityInitializer { get; }

        /// <summary>Class VMT+0x78 Initialize override; null when inherited (TModelHero).</summary>
        public uint? SkillInitializer { get; }

        public NativeFieldHeroInitOrder InitOrder { get; }
        public NativeFieldHeroCommonInitializeKind CommonInitializeKind { get; }

        /// <summary>Learned magic in native append order.</summary>
        public IReadOnlyList<NativeFieldHeroSkill> Skills => _skills;

        /// <summary>
        /// The exact VMT+0x2C ability writes for a level. The level is the
        /// ushort at actor+0x1FC (FillDBData copies definition+0x12). Values are
        /// clamped to the ushort domain the native <c>movzx</c> load produces.
        /// </summary>
        public NativeFieldHeroAbilityResult ComputeAbilities(int level)
        {
            if (level < 0 || level > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(level),
                    "FieldHero level is the ushort actor+0x1FC (0..65535).");
            }
            return _abilities(level);
        }
    }

    /// <summary>
    /// Dormant, fail-closed catalog of the nine concrete FieldHero (战神) classes
    /// the fame selector table instantiates: four ordinary TFieldHero subclasses,
    /// four TMirDotaMatchHumMon subclasses, and the default TModelHero.
    ///
    /// This is a static reversing model, not wired into any spawn path. It
    /// performs no I/O, holds no runtime state, and mutates nothing global.
    /// Lookups for an unmodelled actor kind throw (fail-closed).
    ///
    /// Original image: M2Server.exe, SHA-256
    /// 5540f43bc58d8d67673927c4186941e253403bb7d3a2a0b40ebfcf049670b14e,
    /// image base 0x00400000. All addresses are runtime virtual addresses.
    /// Cross-checked against staging/fieldhero_base_nine_classes_exact_20260731.md
    /// and staging/fieldhero_model_factory_exact_20260731.md.
    /// </summary>
    public static class NativeFieldHeroNineClasses
    {
        /// <summary>Ability block base offset on the actor (actor+0x1E8).</summary>
        public const int AbilityBlockOffset = 0x1E8;

        /// <summary>Level field: ushort at actor+0x1FC (ability block +0x14).</summary>
        public const int LevelOffset = 0x1FC;

        /// <summary>Learned-magic append helper: sub_60913C(actor, magicId, level).</summary>
        public const uint SkillAppendHelper = 0x0060913C;

        /// <summary>x87 rounder sub_403574: fistp qword, round-half-to-even.</summary>
        public const uint RounderFunction = 0x00403574;

        /// <summary>Signed max helper sub_4C7004(a, b).</summary>
        public const uint MaxFunction = 0x004C7004;

        /// <summary>Common Initialize sub_60A3B4 (TFieldHero VMT+0x78).</summary>
        public const uint CommonInitialize = 0x0060A3B4;

        /// <summary>Dota common Initialize sub_60C694 (Dota VMT+0x78).</summary>
        public const uint DotaCommonInitialize = 0x0060C694;

        /// <summary>Base TFieldHero constructor sub_6094E8.</summary>
        public const uint BaseConstructor = 0x006094E8;

        /// <summary>Dota common base constructor sub_60C5BC.</summary>
        public const uint DotaBaseConstructor = 0x0060C5BC;

        private static readonly ReadOnlyDictionary<
            NativeType2FieldHeroActorKind, NativeFieldHeroClassContract> Catalog =
            BuildCatalog();

        /// <summary>The nine contracts, keyed by selector outcome.</summary>
        public static IReadOnlyDictionary<
            NativeType2FieldHeroActorKind, NativeFieldHeroClassContract>
            Classes => Catalog;

        /// <summary>Fail-closed lookup; throws for an unmodelled kind.</summary>
        public static NativeFieldHeroClassContract Get(
            NativeType2FieldHeroActorKind actorKind)
        {
            if (!Catalog.TryGetValue(actorKind, out var contract))
            {
                throw new KeyNotFoundException(
                    "No native FieldHero contract for actor kind " + actorKind
                    + ".");
            }
            return contract;
        }

        /// <summary>
        /// The native x87 rounding contract: sub_403574 does
        /// <c>fistp qword ptr</c> under control word 0x037F (round-half-to-even,
        /// 64-bit precision), and callers consume only EAX (the low 32 bits).
        /// Computing the formula in double following the native operation order
        /// and rounding to even reproduces the native integer for every level in
        /// 0..65535 (verified against exact-rational arithmetic using the exact
        /// 80-bit 2.2 constant).
        /// </summary>
        public static int RoundToNativeInt(double value)
            => unchecked((int)(long)Math.Round(value, MidpointRounding.ToEven));

        /// <summary>
        /// Signed max helper sub_4C7004(a, b): returns b when b &gt;= a else a.
        /// Used as a lower-bound clamp on attack pairs. Ties resolve to b, which
        /// is irrelevant to the result but mirrors the native branch.
        /// </summary>
        public static int NativeMax(int a, int b) => b >= a ? b : a;

        private static ReadOnlyDictionary<
            NativeType2FieldHeroActorKind, NativeFieldHeroClassContract>
            BuildCatalog()
        {
            var map = new Dictionary<
                NativeType2FieldHeroActorKind, NativeFieldHeroClassContract>
            {
                [NativeType2FieldHeroActorKind.FieldWarHero] =
                    new NativeFieldHeroClassContract(
                        NativeType2FieldHeroActorKind.FieldWarHero,
                        "TFieldWarHero", 0, 0, 0x00607180, 0x006071CC,
                        0x0060B6EC, 0x6A8, 0x0060B8BC, 0x0060B870u,
                        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
                        NativeFieldHeroCommonInitializeKind.Ordinary,
                        new[]
                        {
                            new NativeFieldHeroSkill(3, 3),
                            new NativeFieldHeroSkill(12, 3),
                            new NativeFieldHeroSkill(26, 3),
                            new NativeFieldHeroSkill(34, 3)
                        },
                        OrdinaryWarAbilities),

                [NativeType2FieldHeroActorKind.FieldWizHero] =
                    new NativeFieldHeroClassContract(
                        NativeType2FieldHeroActorKind.FieldWizHero,
                        "TFieldWizHero", 1, 1, 0x006076F0, 0x0060773C,
                        0x0060C1DC, 0x6A4, 0x0060C3FC, 0x0060C30Cu,
                        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
                        NativeFieldHeroCommonInitializeKind.Ordinary,
                        new[]
                        {
                            new NativeFieldHeroSkill(11, 3),
                            new NativeFieldHeroSkill(35, 3),
                            new NativeFieldHeroSkill(31, 3),
                            new NativeFieldHeroSkill(10, 3)
                        },
                        OrdinaryWizAbilities),

                [NativeType2FieldHeroActorKind.FieldTaosHero] =
                    new NativeFieldHeroClassContract(
                        NativeType2FieldHeroActorKind.FieldTaosHero,
                        "TFieldTaosHero", 2, 2, 0x006079A8, 0x006079F4,
                        0x0060BD88, 0x6A8, 0x0060BF14, 0x0060BEC0u,
                        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
                        NativeFieldHeroCommonInitializeKind.Ordinary,
                        new[]
                        {
                            new NativeFieldHeroSkill(4, 3),
                            new NativeFieldHeroSkill(6, 3),
                            new NativeFieldHeroSkill(13, 3),
                            new NativeFieldHeroSkill(36, 3)
                        },
                        OrdinaryTaosAbilities),

                [NativeType2FieldHeroActorKind.FieldAssHero] =
                    new NativeFieldHeroClassContract(
                        NativeType2FieldHeroActorKind.FieldAssHero,
                        "TFieldAssHero", 3, 3, 0x00607438, 0x00607484,
                        0x00608D68, 0x6A0, 0x00608ED8, 0x00608E98u,
                        NativeFieldHeroInitOrder.InitializeBeforeSkills,
                        NativeFieldHeroCommonInitializeKind.Ordinary,
                        new[]
                        {
                            new NativeFieldHeroSkill(260, 4),
                            new NativeFieldHeroSkill(264, 4),
                            new NativeFieldHeroSkill(268, 4)
                        },
                        OrdinaryAssAbilities),

                [NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar] =
                    new NativeFieldHeroClassContract(
                        NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar,
                        "TMirDotaMatchHumMon_War", 4, 0, 0x006081E4, 0x00608230,
                        0x0060CDDC, 0x6B8, 0x0060D134, 0x0060CE1Cu,
                        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
                        NativeFieldHeroCommonInitializeKind.Dota,
                        new[]
                        {
                            new NativeFieldHeroSkill(3, 3),
                            new NativeFieldHeroSkill(12, 3),
                            new NativeFieldHeroSkill(26, 3),
                            new NativeFieldHeroSkill(34, 3)
                        },
                        level => DotaAbilities(level, 50000, 5000,
                            NativeFieldHeroAbilitySlot.DcLow, 500)),

                [NativeType2FieldHeroActorKind.MirDotaMatchHumMonWiz] =
                    new NativeFieldHeroClassContract(
                        NativeType2FieldHeroActorKind.MirDotaMatchHumMonWiz,
                        "TMirDotaMatchHumMon_Wiz", 5, 1, 0x00608774, 0x006087C0,
                        0x0060D644, 0x6B4, 0x0060D850, 0x0060D764u,
                        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
                        NativeFieldHeroCommonInitializeKind.Dota,
                        new[]
                        {
                            new NativeFieldHeroSkill(11, 3),
                            new NativeFieldHeroSkill(35, 3),
                            new NativeFieldHeroSkill(31, 3),
                            new NativeFieldHeroSkill(10, 3)
                        },
                        level => DotaAbilities(level, 5000, 50000,
                            NativeFieldHeroAbilitySlot.McLow, 800)),

                [NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos] =
                    new NativeFieldHeroClassContract(
                        NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos,
                        "TMirDotaMatchHumMon_Taos", 6, 2, 0x00608A3C, 0x00608A88,
                        0x0060DA0C, 0x6B8, 0x0060DB50, 0x0060DB04u,
                        NativeFieldHeroInitOrder.SkillsBeforeInitialize,
                        NativeFieldHeroCommonInitializeKind.Dota,
                        new[]
                        {
                            new NativeFieldHeroSkill(4, 3),
                            new NativeFieldHeroSkill(6, 3),
                            new NativeFieldHeroSkill(13, 3),
                            new NativeFieldHeroSkill(36, 3)
                        },
                        level => DotaAbilities(level, 25000, 25000,
                            NativeFieldHeroAbilitySlot.ScLow, 1000)),

                [NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss] =
                    new NativeFieldHeroClassContract(
                        NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss,
                        "TMirDotaMatchHumMon_Ass", 7, 3, 0x006084AC, 0x006084F8,
                        0x0060D3C0, 0x6B0, 0x0060D51C, 0x0060D4DCu,
                        NativeFieldHeroInitOrder.InitializeBeforeSkills,
                        NativeFieldHeroCommonInitializeKind.Dota,
                        new[]
                        {
                            new NativeFieldHeroSkill(260, 4),
                            new NativeFieldHeroSkill(264, 4),
                            new NativeFieldHeroSkill(268, 4)
                        },
                        level => DotaAbilities(level, 25000, 25000,
                            NativeFieldHeroAbilitySlot.CcLow, 500)),

                [NativeType2FieldHeroActorKind.ModelHero] =
                    new NativeFieldHeroClassContract(
                        NativeType2FieldHeroActorKind.ModelHero,
                        "TModelHero", null, 0, 0x00607C60, 0x00607CAC,
                        0x00609038, 0x6A0, 0x00609094, null,
                        NativeFieldHeroInitOrder.NoSkills,
                        NativeFieldHeroCommonInitializeKind.Ordinary,
                        Array.Empty<NativeFieldHeroSkill>(),
                        ModelHeroAbilities)
            };
            return new ReadOnlyDictionary<
                NativeType2FieldHeroActorKind, NativeFieldHeroClassContract>(map);
        }

        // -- Ordinary ability initializers (VMT+0x2C) ---------------------------
        //
        // Each transcribes its native function's writes in execution order. The
        // level L is loaded with movzx (0..65535); attack divisions use unsigned
        // div; the HP/MP maxima come from sub_403574 (round-half-to-even) plus a
        // 32-bit integer add; the trailing clamps lower current HP/MP and the
        // two working mirrors to the new maxima.

        // sub_60B8BC. MaxHP=R(L*(L/2+10+L/20))+50, -3*(L-60) when L>60;
        // MaxMP=R(3.5*L)+11; AC=(0,L/7); DC=(max(L/5-1,1),max(L/5,1)); rest 0.
        private static NativeFieldHeroAbilityResult OrdinaryWarAbilities(int level)
        {
            var maxHp = unchecked(
                RoundToNativeInt(level * (level / 2.0 + 10.0 + level / 20.0)) + 50);
            if (level > 60) maxHp = unchecked(maxHp - 3 * (level - 60));
            var maxMp = unchecked(RoundToNativeInt(3.5 * level) + 11);
            var n = level / 5;

            var writes = new List<NativeFieldHeroAbilityWrite>
            {
                Assign(NativeFieldHeroAbilitySlot.MaxHp, maxHp),
                Assign(NativeFieldHeroAbilitySlot.MaxMp, maxMp),
                Assign(NativeFieldHeroAbilitySlot.DcLow, NativeMax(n - 1, 1)),
                Assign(NativeFieldHeroAbilitySlot.DcHigh, NativeMax(n, 1)),
                Assign(NativeFieldHeroAbilitySlot.McLow, 0),
                Assign(NativeFieldHeroAbilitySlot.McHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.ScLow, 0),
                Assign(NativeFieldHeroAbilitySlot.ScHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.CcLow, 0),
                Assign(NativeFieldHeroAbilitySlot.CcHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.AcLow, 0),
                Assign(NativeFieldHeroAbilitySlot.AcHigh, level / 7),
                Assign(NativeFieldHeroAbilitySlot.MacLow, 0),
                Assign(NativeFieldHeroAbilitySlot.MacHigh, 0)
            };
            AppendOrdinaryClamps(writes, maxHp, maxMp);
            return new NativeFieldHeroAbilityResult(level, writes.ToArray());
        }

        // sub_60C3FC. MaxHP=R(L*(L/15+5))+50, +30*(L-60) when L>60;
        // MaxMP=R((L/5+2)*2.2*L)+13; DC=MC=(max(L/7-1,0),max(L/7,1)); rest 0.
        private static NativeFieldHeroAbilityResult OrdinaryWizAbilities(int level)
        {
            var maxHp = unchecked(
                RoundToNativeInt(level * (level / 15.0 + 5.0)) + 50);
            if (level > 60) maxHp = unchecked(maxHp + 30 * (level - 60));
            var maxMp = unchecked(
                RoundToNativeInt((level / 5.0 + 2.0) * 2.2 * level) + 13);
            var n = level / 7;
            var low = NativeMax(n - 1, 0);
            var high = NativeMax(n, 1);

            var writes = new List<NativeFieldHeroAbilityWrite>
            {
                Assign(NativeFieldHeroAbilitySlot.MaxHp, maxHp),
                Assign(NativeFieldHeroAbilitySlot.MaxMp, maxMp),
                Assign(NativeFieldHeroAbilitySlot.DcLow, low),
                Assign(NativeFieldHeroAbilitySlot.DcHigh, high),
                Assign(NativeFieldHeroAbilitySlot.McLow, low),
                Assign(NativeFieldHeroAbilitySlot.McHigh, high),
                Assign(NativeFieldHeroAbilitySlot.ScLow, 0),
                Assign(NativeFieldHeroAbilitySlot.ScHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.CcLow, 0),
                Assign(NativeFieldHeroAbilitySlot.CcHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.AcLow, 0),
                Assign(NativeFieldHeroAbilitySlot.AcHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.MacLow, 0),
                Assign(NativeFieldHeroAbilitySlot.MacHigh, 0)
            };
            AppendOrdinaryClamps(writes, maxHp, maxMp);
            return new NativeFieldHeroAbilityResult(level, writes.ToArray());
        }

        // sub_60BF14. MaxHP=R(L*(L/6+10))+50, +33*(L-60) when L>60;
        // MaxMP=R((L/8)*2.2*L)+13; DC=SC=(max(L/7-1,0),max(L/7,1));
        // MAC=(L/12,L/6+1); rest 0.
        private static NativeFieldHeroAbilityResult OrdinaryTaosAbilities(int level)
        {
            var maxHp = unchecked(
                RoundToNativeInt(level * (level / 6.0 + 10.0)) + 50);
            if (level > 60) maxHp = unchecked(maxHp + 33 * (level - 60));
            var maxMp = unchecked(
                RoundToNativeInt(level / 8.0 * 2.2 * level) + 13);
            var n = level / 7;
            var low = NativeMax(n - 1, 0);
            var high = NativeMax(n, 1);
            var n6 = level / 6;

            var writes = new List<NativeFieldHeroAbilityWrite>
            {
                Assign(NativeFieldHeroAbilitySlot.MaxHp, maxHp),
                Assign(NativeFieldHeroAbilitySlot.MaxMp, maxMp),
                Assign(NativeFieldHeroAbilitySlot.DcLow, low),
                Assign(NativeFieldHeroAbilitySlot.DcHigh, high),
                Assign(NativeFieldHeroAbilitySlot.McLow, 0),
                Assign(NativeFieldHeroAbilitySlot.McHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.ScLow, low),
                Assign(NativeFieldHeroAbilitySlot.ScHigh, high),
                Assign(NativeFieldHeroAbilitySlot.CcLow, 0),
                Assign(NativeFieldHeroAbilitySlot.CcHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.AcLow, 0),
                Assign(NativeFieldHeroAbilitySlot.AcHigh, 0),
                // native: edi=L/6; MAC low = edi>>1 (== L/12 for L>=0); MAC high = edi+1
                Assign(NativeFieldHeroAbilitySlot.MacLow, n6 / 2),
                Assign(NativeFieldHeroAbilitySlot.MacHigh, n6 + 1)
            };
            AppendOrdinaryClamps(writes, maxHp, maxMp);
            return new NativeFieldHeroAbilityResult(level, writes.ToArray());
        }

        // sub_608ED8. v=R(L*(L/2+10+L/20))+50; MaxHP=MaxMP=v; AC=(0,L/7);
        // CC=(L/5-1,L/5) with the low value left unclamped (may be -1); rest 0.
        private static NativeFieldHeroAbilityResult OrdinaryAssAbilities(int level)
        {
            var v = unchecked(
                RoundToNativeInt(level * (level / 2.0 + 10.0 + level / 20.0)) + 50);
            var n = level / 5;

            var writes = new List<NativeFieldHeroAbilityWrite>
            {
                Assign(NativeFieldHeroAbilitySlot.MaxHp, v),
                Assign(NativeFieldHeroAbilitySlot.MaxMp, v),
                Assign(NativeFieldHeroAbilitySlot.DcLow, 0),
                Assign(NativeFieldHeroAbilitySlot.DcHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.McLow, 0),
                Assign(NativeFieldHeroAbilitySlot.McHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.ScLow, 0),
                Assign(NativeFieldHeroAbilitySlot.ScHigh, 0),
                Assign(NativeFieldHeroAbilitySlot.CcLow, n - 1),
                Assign(NativeFieldHeroAbilitySlot.CcHigh, n),
                Assign(NativeFieldHeroAbilitySlot.AcLow, 0),
                Assign(NativeFieldHeroAbilitySlot.AcHigh, level / 7),
                Assign(NativeFieldHeroAbilitySlot.MacLow, 0),
                Assign(NativeFieldHeroAbilitySlot.MacHigh, 0)
            };
            AppendOrdinaryClamps(writes, v, v);
            return new NativeFieldHeroAbilityResult(level, writes.ToArray());
        }

        // -- Dota ability initializers (VMT+0x2C) -------------------------------
        //
        // sub_60D134/60D850/60DB50/60D51C. MaxHP/MaxMP and one attack pair are
        // 32-bit integer multiples of L; DC/MC/SC/CC are each written (to zero or
        // to the pair multiple); AC and MAC are NEVER written, so they retain the
        // equipment-aggregation / construction-zero value. Same clamp epilogue.
        private static NativeFieldHeroAbilityResult DotaAbilities(int level,
            int hpMult, int mpMult, NativeFieldHeroAbilitySlot pairLow,
            int pairMult)
        {
            var maxHp = unchecked(hpMult * level);
            var maxMp = unchecked(mpMult * level);
            var pair = unchecked(pairMult * level);

            var writes = new List<NativeFieldHeroAbilityWrite>
            {
                Assign(NativeFieldHeroAbilitySlot.MaxHp, maxHp),
                Assign(NativeFieldHeroAbilitySlot.MaxMp, maxMp),
                Assign(NativeFieldHeroAbilitySlot.DcLow,
                    pairLow == NativeFieldHeroAbilitySlot.DcLow ? pair : 0),
                Assign(NativeFieldHeroAbilitySlot.DcHigh,
                    pairLow == NativeFieldHeroAbilitySlot.DcLow ? pair : 0),
                Assign(NativeFieldHeroAbilitySlot.McLow,
                    pairLow == NativeFieldHeroAbilitySlot.McLow ? pair : 0),
                Assign(NativeFieldHeroAbilitySlot.McHigh,
                    pairLow == NativeFieldHeroAbilitySlot.McLow ? pair : 0),
                Assign(NativeFieldHeroAbilitySlot.ScLow,
                    pairLow == NativeFieldHeroAbilitySlot.ScLow ? pair : 0),
                Assign(NativeFieldHeroAbilitySlot.ScHigh,
                    pairLow == NativeFieldHeroAbilitySlot.ScLow ? pair : 0),
                Assign(NativeFieldHeroAbilitySlot.CcLow,
                    pairLow == NativeFieldHeroAbilitySlot.CcLow ? pair : 0),
                Assign(NativeFieldHeroAbilitySlot.CcHigh,
                    pairLow == NativeFieldHeroAbilitySlot.CcLow ? pair : 0)
                // AC (+0x18/+0x1C) and MAC (+0x20/+0x24) intentionally unwritten.
            };
            AppendOrdinaryClamps(writes, maxHp, maxMp);
            return new NativeFieldHeroAbilityResult(level, writes.ToArray());
        }

        // -- TModelHero ability initializer (VMT+0x2C) --------------------------
        //
        // sub_609094. Fixed model stats, level-independent. Current MP is set to
        // MaxHP (5000), not MaxMP (1000): the asymmetry is original. Assigns (not
        // clamps) current HP/MP and the +0x2B0/+0x2AC mirrors; +0x2B4 untouched.
        private static NativeFieldHeroAbilityResult ModelHeroAbilities(int level)
        {
            const int maxHp = 5000;
            var writes = new List<NativeFieldHeroAbilityWrite>
            {
                Assign(NativeFieldHeroAbilitySlot.MaxHp, maxHp),
                Assign(NativeFieldHeroAbilitySlot.MaxMp, 1000),
                Assign(NativeFieldHeroAbilitySlot.AcHigh, 1000),
                Assign(NativeFieldHeroAbilitySlot.MacHigh, 1000),
                Assign(NativeFieldHeroAbilitySlot.CurrentHp, maxHp),
                Assign(NativeFieldHeroAbilitySlot.CurrentMp, maxHp),
                Assign(NativeFieldHeroAbilitySlot.WorkingMaxHpMirror, maxHp),
                Assign(NativeFieldHeroAbilitySlot.WorkingHpMirror, maxHp)
            };
            return new NativeFieldHeroAbilityResult(level, writes.ToArray());
        }

        // Shared ordinary/Dota clamp epilogue: current HP/MP and the two working
        // mirrors are lowered (never raised) to the new maxima.
        private static void AppendOrdinaryClamps(
            List<NativeFieldHeroAbilityWrite> writes, int maxHp, int maxMp)
        {
            writes.Add(Clamp(NativeFieldHeroAbilitySlot.CurrentHp, maxHp));
            writes.Add(Clamp(NativeFieldHeroAbilitySlot.CurrentMp, maxMp));
            writes.Add(Clamp(NativeFieldHeroAbilitySlot.WorkingHpMirror, maxHp));
            writes.Add(Clamp(NativeFieldHeroAbilitySlot.WorkingMpMirror, maxMp));
        }

        private static NativeFieldHeroAbilityWrite Assign(
            NativeFieldHeroAbilitySlot slot, int value)
            => new NativeFieldHeroAbilityWrite(slot,
                NativeFieldHeroAbilityWriteMode.Assign, value);

        private static NativeFieldHeroAbilityWrite Clamp(
            NativeFieldHeroAbilitySlot slot, int value)
            => new NativeFieldHeroAbilityWrite(slot,
                NativeFieldHeroAbilityWriteMode.ClampDownTo, value);
    }
}
