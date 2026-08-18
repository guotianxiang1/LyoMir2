using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public sealed class TFieldWarHero : TFieldHero
    {
        public const int OriginalVmt = 0x006071CC;
        public const int OriginalConstructor = 0x0060B6EC;
        public const int OriginalSize = 0x6A8;
        public const NativeFieldHeroSkillPlacement SkillPlacementContract =
            NativeFieldHeroSkillPlacement.BeforeCommonInitialize;

        private static readonly IReadOnlyList<NativeFieldHeroSkillContract>
            s_skills = SkillSet(
                new NativeFieldHeroSkillContract(3, 3),
                new NativeFieldHeroSkillContract(12, 3),
                new NativeFieldHeroSkillContract(26, 3),
                new NativeFieldHeroSkillContract(34, 3));

        internal TFieldWarHero(NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
            : base(spawnPlan, materialization)
        {
            RequirePlanKind(NativeType2FieldHeroActorKind.FieldWarHero);
            m_btJob = 0;
            m_Abil.Level = 45;
            m_WAbil.Level = 45;
        }

        public override NativeType2FieldHeroActorKind NativeActorKind =>
            NativeType2FieldHeroActorKind.FieldWarHero;
        public override string NativeClassName => nameof(TFieldWarHero);
        public override int NativeClassVmtAddress => OriginalVmt;
        public override int NativeClassConstructorAddress => OriginalConstructor;
        public override int NativeClassInstanceSize => OriginalSize;
        public override NativeFieldHeroSkillPlacement NativeSkillPlacement =>
            SkillPlacementContract;
        public override IReadOnlyList<NativeFieldHeroSkillContract>
            NativeSkills => s_skills;
        public static IReadOnlyList<NativeFieldHeroSkillContract>
            SkillContracts => s_skills;

        public static NativeFieldHeroAbilityContract CalculateNativeAbility(
            ushort level)
        {
            long l = level;
            var maxHp = unchecked(RoundPositiveRatioToEven(11 * l * l, 20)
                + 10 * level + 50);
            if (level > 60)
                maxHp = unchecked(maxHp - 3 * (level - 60));
            var maxMp = unchecked(RoundPositiveRatioToEven(7 * l, 2) + 11);
            var dc = level / 5;
            return new NativeFieldHeroAbilityContract(maxHp, maxMp,
                Pair(0, level / 7), Pair(0, 0),
                Pair(Math.Max(dc - 1, 1), Math.Max(dc, 1)),
                Pair(0, 0), Pair(0, 0), Pair(0, 0));
        }

        public override NativeFieldHeroAbilityContract BuildNativeAbility(
            ushort level) => CalculateNativeAbility(level);
    }

    public sealed class TFieldWizHero : TFieldHero
    {
        public const int OriginalVmt = 0x0060773C;
        public const int OriginalConstructor = 0x0060C1DC;
        public const int OriginalSize = 0x6A4;
        public const NativeFieldHeroSkillPlacement SkillPlacementContract =
            NativeFieldHeroSkillPlacement.BeforeCommonInitialize;

        private static readonly IReadOnlyList<NativeFieldHeroSkillContract>
            s_skills = SkillSet(
                new NativeFieldHeroSkillContract(11, 3),
                new NativeFieldHeroSkillContract(35, 3),
                new NativeFieldHeroSkillContract(31, 3),
                new NativeFieldHeroSkillContract(10, 3));

        internal TFieldWizHero(NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
            : base(spawnPlan, materialization)
        {
            RequirePlanKind(NativeType2FieldHeroActorKind.FieldWizHero);
            m_btJob = 1;
            m_Abil.Level = 45;
            m_WAbil.Level = 45;
        }

        public override NativeType2FieldHeroActorKind NativeActorKind =>
            NativeType2FieldHeroActorKind.FieldWizHero;
        public override string NativeClassName => nameof(TFieldWizHero);
        public override int NativeClassVmtAddress => OriginalVmt;
        public override int NativeClassConstructorAddress => OriginalConstructor;
        public override int NativeClassInstanceSize => OriginalSize;
        public override NativeFieldHeroSkillPlacement NativeSkillPlacement =>
            SkillPlacementContract;
        public override IReadOnlyList<NativeFieldHeroSkillContract>
            NativeSkills => s_skills;
        public static IReadOnlyList<NativeFieldHeroSkillContract>
            SkillContracts => s_skills;

        public static NativeFieldHeroAbilityContract CalculateNativeAbility(
            ushort level)
        {
            long l = level;
            var maxHp = unchecked(RoundPositiveRatioToEven(l * l, 15)
                + 5 * level + 50);
            if (level > 60)
                maxHp = unchecked(maxHp + 30 * (level - 60));
            var maxMp = unchecked(RoundPositiveRatioToEven(
                11 * l * (l + 10), 25) + 13);
            var attack = level / 7;
            var pair = Pair(Math.Max(attack - 1, 0), Math.Max(attack, 1));
            return new NativeFieldHeroAbilityContract(maxHp, maxMp,
                Pair(0, 0), Pair(0, 0), pair, pair,
                Pair(0, 0), Pair(0, 0));
        }

        public override NativeFieldHeroAbilityContract BuildNativeAbility(
            ushort level) => CalculateNativeAbility(level);
    }

    public sealed class TFieldTaosHero : TFieldHero
    {
        public const int OriginalVmt = 0x006079F4;
        public const int OriginalConstructor = 0x0060BD88;
        public const int OriginalSize = 0x6A8;
        public const NativeFieldHeroSkillPlacement SkillPlacementContract =
            NativeFieldHeroSkillPlacement.BeforeCommonInitialize;

        private static readonly IReadOnlyList<NativeFieldHeroSkillContract>
            s_skills = SkillSet(
                new NativeFieldHeroSkillContract(4, 3),
                new NativeFieldHeroSkillContract(6, 3),
                new NativeFieldHeroSkillContract(13, 3),
                new NativeFieldHeroSkillContract(36, 3));

        internal TFieldTaosHero(NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
            : base(spawnPlan, materialization)
        {
            RequirePlanKind(NativeType2FieldHeroActorKind.FieldTaosHero);
            m_btJob = 2;
            m_Abil.Level = 45;
            m_WAbil.Level = 45;
            NativeRaw06A4 = HUtil32.GetTickCount();
        }

        public int NativeRaw06A4 { get; }

        public override NativeType2FieldHeroActorKind NativeActorKind =>
            NativeType2FieldHeroActorKind.FieldTaosHero;
        public override string NativeClassName => nameof(TFieldTaosHero);
        public override int NativeClassVmtAddress => OriginalVmt;
        public override int NativeClassConstructorAddress => OriginalConstructor;
        public override int NativeClassInstanceSize => OriginalSize;
        public override NativeFieldHeroSkillPlacement NativeSkillPlacement =>
            SkillPlacementContract;
        public override IReadOnlyList<NativeFieldHeroSkillContract>
            NativeSkills => s_skills;
        public static IReadOnlyList<NativeFieldHeroSkillContract>
            SkillContracts => s_skills;

        public static NativeFieldHeroAbilityContract CalculateNativeAbility(
            ushort level)
        {
            long l = level;
            var maxHp = unchecked(RoundPositiveRatioToEven(l * l, 6)
                + 10 * level + 50);
            if (level > 60)
                maxHp = unchecked(maxHp + 33 * (level - 60));
            var maxMp = unchecked(RoundPositiveRatioToEven(11 * l * l, 40)
                + 13);
            var attack = level / 7;
            var pair = Pair(Math.Max(attack - 1, 0), Math.Max(attack, 1));
            return new NativeFieldHeroAbilityContract(maxHp, maxMp,
                Pair(0, 0), Pair(level / 12, level / 6 + 1),
                pair, Pair(0, 0), pair, Pair(0, 0));
        }

        public override NativeFieldHeroAbilityContract BuildNativeAbility(
            ushort level) => CalculateNativeAbility(level);
    }

    public sealed class TFieldAssHero : TFieldHero
    {
        public const int OriginalVmt = 0x00607484;
        public const int OriginalConstructor = 0x00608D68;
        public const int OriginalSize = 0x6A0;
        public const NativeFieldHeroSkillPlacement SkillPlacementContract =
            NativeFieldHeroSkillPlacement.AfterCommonInitialize;

        private static readonly IReadOnlyList<NativeFieldHeroSkillContract>
            s_skills = SkillSet(
                new NativeFieldHeroSkillContract(260, 4),
                new NativeFieldHeroSkillContract(264, 4),
                new NativeFieldHeroSkillContract(268, 4));

        internal TFieldAssHero(NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
            : base(spawnPlan, materialization)
        {
            RequirePlanKind(NativeType2FieldHeroActorKind.FieldAssHero);
            m_nNextHitTime = 500;
            m_btJob = 3;
            m_Abil.Level = 45;
            m_WAbil.Level = 45;
        }

        public override NativeType2FieldHeroActorKind NativeActorKind =>
            NativeType2FieldHeroActorKind.FieldAssHero;
        public override string NativeClassName => nameof(TFieldAssHero);
        public override int NativeClassVmtAddress => OriginalVmt;
        public override int NativeClassConstructorAddress => OriginalConstructor;
        public override int NativeClassInstanceSize => OriginalSize;
        public override NativeFieldHeroSkillPlacement NativeSkillPlacement =>
            SkillPlacementContract;
        public override IReadOnlyList<NativeFieldHeroSkillContract>
            NativeSkills => s_skills;
        public static IReadOnlyList<NativeFieldHeroSkillContract>
            SkillContracts => s_skills;

        public static NativeFieldHeroAbilityContract CalculateNativeAbility(
            ushort level)
        {
            long l = level;
            var value = unchecked(RoundPositiveRatioToEven(11 * l * l, 20)
                + 10 * level + 50);
            var cc = level / 5;
            return new NativeFieldHeroAbilityContract(value, value,
                Pair(0, level / 7), Pair(0, 0), Pair(0, 0), Pair(0, 0),
                Pair(0, 0), Pair(cc - 1, cc));
        }

        public override NativeFieldHeroAbilityContract BuildNativeAbility(
            ushort level) => CalculateNativeAbility(level);
    }

    public sealed class TModelHero : TFieldHero
    {
        public const int OriginalVmt = 0x00607CAC;
        public const int OriginalConstructor = 0x00609038;
        public const int OriginalSize = 0x6A0;
        public const NativeFieldHeroSkillPlacement SkillPlacementContract =
            NativeFieldHeroSkillPlacement.None;

        private static readonly IReadOnlyList<NativeFieldHeroSkillContract>
            s_skills = Array.Empty<NativeFieldHeroSkillContract>();

        internal TModelHero(NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
            : base(spawnPlan, materialization)
        {
            RequirePlanKind(NativeType2FieldHeroActorKind.ModelHero);
            NativeRaw02E1 = 1;
            NativeRaw02E0 = 1;
        }

        public byte NativeRaw02E0 { get; }
        public byte NativeRaw02E1 { get; }
        public override NativeType2FieldHeroActorKind NativeActorKind =>
            NativeType2FieldHeroActorKind.ModelHero;
        public override string NativeClassName => nameof(TModelHero);
        public override int NativeClassVmtAddress => OriginalVmt;
        public override int NativeClassConstructorAddress => OriginalConstructor;
        public override int NativeClassInstanceSize => OriginalSize;
        public override NativeFieldHeroSkillPlacement NativeSkillPlacement =>
            SkillPlacementContract;
        public override IReadOnlyList<NativeFieldHeroSkillContract>
            NativeSkills => s_skills;
        public static IReadOnlyList<NativeFieldHeroSkillContract>
            SkillContracts => s_skills;

        public static NativeFieldHeroAbilityContract CalculateNativeAbility(
            ushort level) => new(5000, 1000,
                Pair(0, 1000), Pair(0, 1000), Pair(0, 0), Pair(0, 0),
                Pair(0, 0), Pair(0, 0), 5000, 5000);

        public override NativeFieldHeroAbilityContract BuildNativeAbility(
            ushort level) => CalculateNativeAbility(level);
    }

    public abstract class TMirDotaMatchHumMon : TFieldHero
    {
        public new const int OriginalVmtAddress = 0x00607F5C;
        public new const int OriginalConstructorAddress = 0x0060C5BC;
        public new const int OriginalInstanceSize = 0x6AC;

        private protected TMirDotaMatchHumMon(
            NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
            : base(spawnPlan, materialization)
        {
            NativeRaw0608 = 0;
            NativeRaw05F8 = 0;
            NativeRaw05FC = 0;
            NativeRaw05F4 = 0;
            NativeRaw06A0Length = 1;
            NativeRaw06A4 = 0;
            NativeRaw06A8 = 0;
            NativeLifetimeRemaining = 0;
            m_nViewRange = 9;
        }

        public byte NativeRaw05F4 { get; }
        public int NativeRaw06A0Length { get; }
        public int NativeRaw06A4 { get; }
        public int NativeRaw06A8 { get; }
    }

    public sealed class TMirDotaMatchHumMon_War : TMirDotaMatchHumMon
    {
        public const int OriginalVmt = 0x00608230;
        public const int OriginalConstructor = 0x0060CDDC;
        public const int OriginalSize = 0x6B8;
        public const NativeFieldHeroSkillPlacement SkillPlacementContract =
            NativeFieldHeroSkillPlacement.BeforeCommonInitialize;

        private static readonly IReadOnlyList<NativeFieldHeroSkillContract>
            s_skills = SkillSet(
                new NativeFieldHeroSkillContract(3, 3),
                new NativeFieldHeroSkillContract(12, 3),
                new NativeFieldHeroSkillContract(26, 3),
                new NativeFieldHeroSkillContract(34, 3));

        internal TMirDotaMatchHumMon_War(
            NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
            : base(spawnPlan, materialization)
        {
            RequirePlanKind(
                NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar);
            m_btJob = 0;
        }

        public override NativeType2FieldHeroActorKind NativeActorKind =>
            NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar;
        public override string NativeClassName =>
            nameof(TMirDotaMatchHumMon_War);
        public override int NativeClassVmtAddress => OriginalVmt;
        public override int NativeClassConstructorAddress => OriginalConstructor;
        public override int NativeClassInstanceSize => OriginalSize;
        public override NativeFieldHeroSkillPlacement NativeSkillPlacement =>
            SkillPlacementContract;
        public override IReadOnlyList<NativeFieldHeroSkillContract>
            NativeSkills => s_skills;
        public static IReadOnlyList<NativeFieldHeroSkillContract>
            SkillContracts => s_skills;

        public static NativeFieldHeroAbilityContract CalculateNativeAbility(
            ushort level) => new(
                unchecked(50000 * (int)level),
                unchecked(5000 * (int)level),
                Pair(0, 0), Pair(0, 0),
                Pair(unchecked(500 * (int)level),
                    unchecked(500 * (int)level)),
                Pair(0, 0), Pair(0, 0), Pair(0, 0));

        public override NativeFieldHeroAbilityContract BuildNativeAbility(
            ushort level) => CalculateNativeAbility(level);
    }

    public sealed class TMirDotaMatchHumMon_Wiz : TMirDotaMatchHumMon
    {
        public const int OriginalVmt = 0x006087C0;
        public const int OriginalConstructor = 0x0060D644;
        public const int OriginalSize = 0x6B4;
        public const NativeFieldHeroSkillPlacement SkillPlacementContract =
            NativeFieldHeroSkillPlacement.BeforeCommonInitialize;

        private static readonly IReadOnlyList<NativeFieldHeroSkillContract>
            s_skills = SkillSet(
                new NativeFieldHeroSkillContract(11, 3),
                new NativeFieldHeroSkillContract(35, 3),
                new NativeFieldHeroSkillContract(31, 3),
                new NativeFieldHeroSkillContract(10, 3));

        internal TMirDotaMatchHumMon_Wiz(
            NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
            : base(spawnPlan, materialization)
        {
            RequirePlanKind(
                NativeType2FieldHeroActorKind.MirDotaMatchHumMonWiz);
            m_btJob = 1;
        }

        public override NativeType2FieldHeroActorKind NativeActorKind =>
            NativeType2FieldHeroActorKind.MirDotaMatchHumMonWiz;
        public override string NativeClassName =>
            nameof(TMirDotaMatchHumMon_Wiz);
        public override int NativeClassVmtAddress => OriginalVmt;
        public override int NativeClassConstructorAddress => OriginalConstructor;
        public override int NativeClassInstanceSize => OriginalSize;
        public override NativeFieldHeroSkillPlacement NativeSkillPlacement =>
            SkillPlacementContract;
        public override IReadOnlyList<NativeFieldHeroSkillContract>
            NativeSkills => s_skills;
        public static IReadOnlyList<NativeFieldHeroSkillContract>
            SkillContracts => s_skills;

        public static NativeFieldHeroAbilityContract CalculateNativeAbility(
            ushort level) => new(
                unchecked(5000 * (int)level),
                unchecked(50000 * (int)level),
                Pair(0, 0), Pair(0, 0), Pair(0, 0),
                Pair(unchecked(800 * (int)level),
                    unchecked(800 * (int)level)),
                Pair(0, 0), Pair(0, 0));

        public override NativeFieldHeroAbilityContract BuildNativeAbility(
            ushort level) => CalculateNativeAbility(level);
    }

    public sealed class TMirDotaMatchHumMon_Taos : TMirDotaMatchHumMon
    {
        public const int OriginalVmt = 0x00608A88;
        public const int OriginalConstructor = 0x0060DA0C;
        public const int OriginalSize = 0x6B8;
        public const NativeFieldHeroSkillPlacement SkillPlacementContract =
            NativeFieldHeroSkillPlacement.BeforeCommonInitialize;

        private static readonly IReadOnlyList<NativeFieldHeroSkillContract>
            s_skills = SkillSet(
                new NativeFieldHeroSkillContract(4, 3),
                new NativeFieldHeroSkillContract(6, 3),
                new NativeFieldHeroSkillContract(13, 3),
                new NativeFieldHeroSkillContract(36, 3));

        internal TMirDotaMatchHumMon_Taos(
            NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
            : base(spawnPlan, materialization)
        {
            RequirePlanKind(
                NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos);
            m_btJob = 2;
            NativeRaw06B4 = HUtil32.GetTickCount();
        }

        public int NativeRaw06B4 { get; }

        public override NativeType2FieldHeroActorKind NativeActorKind =>
            NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos;
        public override string NativeClassName =>
            nameof(TMirDotaMatchHumMon_Taos);
        public override int NativeClassVmtAddress => OriginalVmt;
        public override int NativeClassConstructorAddress => OriginalConstructor;
        public override int NativeClassInstanceSize => OriginalSize;
        public override NativeFieldHeroSkillPlacement NativeSkillPlacement =>
            SkillPlacementContract;
        public override IReadOnlyList<NativeFieldHeroSkillContract>
            NativeSkills => s_skills;
        public static IReadOnlyList<NativeFieldHeroSkillContract>
            SkillContracts => s_skills;

        public static NativeFieldHeroAbilityContract CalculateNativeAbility(
            ushort level) => new(
                unchecked(25000 * (int)level),
                unchecked(25000 * (int)level),
                Pair(0, 0), Pair(0, 0), Pair(0, 0), Pair(0, 0),
                Pair(unchecked(1000 * (int)level),
                    unchecked(1000 * (int)level)),
                Pair(0, 0));

        public override NativeFieldHeroAbilityContract BuildNativeAbility(
            ushort level) => CalculateNativeAbility(level);
    }

    public sealed class TMirDotaMatchHumMon_Ass : TMirDotaMatchHumMon
    {
        public const int OriginalVmt = 0x006084F8;
        public const int OriginalConstructor = 0x0060D3C0;
        public const int OriginalSize = 0x6B0;
        public const NativeFieldHeroSkillPlacement SkillPlacementContract =
            NativeFieldHeroSkillPlacement.AfterCommonInitialize;

        private static readonly IReadOnlyList<NativeFieldHeroSkillContract>
            s_skills = SkillSet(
                new NativeFieldHeroSkillContract(260, 4),
                new NativeFieldHeroSkillContract(264, 4),
                new NativeFieldHeroSkillContract(268, 4));

        internal TMirDotaMatchHumMon_Ass(
            NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
            : base(spawnPlan, materialization)
        {
            RequirePlanKind(
                NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss);
            m_nNextHitTime = 500;
            m_btJob = 3;
        }

        public override NativeType2FieldHeroActorKind NativeActorKind =>
            NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss;
        public override string NativeClassName =>
            nameof(TMirDotaMatchHumMon_Ass);
        public override int NativeClassVmtAddress => OriginalVmt;
        public override int NativeClassConstructorAddress => OriginalConstructor;
        public override int NativeClassInstanceSize => OriginalSize;
        public override NativeFieldHeroSkillPlacement NativeSkillPlacement =>
            SkillPlacementContract;
        public override IReadOnlyList<NativeFieldHeroSkillContract>
            NativeSkills => s_skills;
        public static IReadOnlyList<NativeFieldHeroSkillContract>
            SkillContracts => s_skills;

        public static NativeFieldHeroAbilityContract CalculateNativeAbility(
            ushort level) => new(
                unchecked(25000 * (int)level),
                unchecked(25000 * (int)level),
                Pair(0, 0), Pair(0, 0), Pair(0, 0), Pair(0, 0),
                Pair(0, 0),
                Pair(unchecked(500 * (int)level),
                    unchecked(500 * (int)level)));

        public override NativeFieldHeroAbilityContract BuildNativeAbility(
            ushort level) => CalculateNativeAbility(level);
    }
}
