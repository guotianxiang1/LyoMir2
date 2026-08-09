using System.Collections.ObjectModel;
using GameSvr.Services;

namespace GameSvr
{
    public enum NativeFieldHeroSkillPlacement
    {
        None,
        BeforeCommonInitialize,
        AfterCommonInitialize
    }

    public readonly struct NativeFieldHeroSkillContract
    {
        public NativeFieldHeroSkillContract(ushort magicId, byte level)
        {
            MagicId = magicId;
            Level = level;
        }

        public ushort MagicId { get; }
        public byte Level { get; }
    }

    public readonly struct NativeFieldHeroAbilityPair
    {
        public NativeFieldHeroAbilityPair(int low, int high)
        {
            Low = low;
            High = high;
        }

        public int Low { get; }
        public int High { get; }
    }

    public sealed class NativeFieldHeroAbilityContract
    {
        public NativeFieldHeroAbilityContract(int maxHp, int maxMp,
            NativeFieldHeroAbilityPair ac,
            NativeFieldHeroAbilityPair mac,
            NativeFieldHeroAbilityPair dc,
            NativeFieldHeroAbilityPair mc,
            NativeFieldHeroAbilityPair sc,
            NativeFieldHeroAbilityPair cc,
            int? forcedCurrentHp = null,
            int? forcedCurrentMp = null)
        {
            MaxHp = maxHp;
            MaxMp = maxMp;
            AC = ac;
            MAC = mac;
            DC = dc;
            MC = mc;
            SC = sc;
            CC = cc;
            ForcedCurrentHp = forcedCurrentHp;
            ForcedCurrentMp = forcedCurrentMp;
        }

        public int MaxHp { get; }
        public int MaxMp { get; }
        public NativeFieldHeroAbilityPair AC { get; }
        public NativeFieldHeroAbilityPair MAC { get; }
        public NativeFieldHeroAbilityPair DC { get; }
        public NativeFieldHeroAbilityPair MC { get; }
        public NativeFieldHeroAbilityPair SC { get; }
        public NativeFieldHeroAbilityPair CC { get; }

        // Null means the native class only clamps the prior current value.
        public int? ForcedCurrentHp { get; }
        public int? ForcedCurrentMp { get; }
    }

    /// <summary>
    /// Dormant FieldHero runtime base. It owns a spawn-plan handle for its
    /// complete lifetime, which in turn keeps the selected publication alive.
    /// Production construction and scheduling are deliberately not exposed.
    /// </summary>
    public abstract class TFieldHero : AnimalObject
    {
        public const int OriginalVmtAddress = 0x00606F1C;
        public const int OriginalConstructorAddress = 0x006094E8;
        public const int OriginalInstanceSize = 0x69C;

        public const string ProductionNoGoReason =
            "NO-GO: FieldHero execution is dormant until the process-wide " +
            "native RNG owner, exact FieldHero magic executor, native " +
            "equipment clone/recalculation path, and complete base Run " +
            "orchestration plus deferred registration/cleanup transaction " +
            "are connected.";

        private readonly NativeType2FieldHeroSpawnPlan _spawnPlan;
        private readonly NativeType2FieldHeroMaterialization _materialization;

        private protected TFieldHero(NativeType2FieldHeroSpawnPlan spawnPlan,
            NativeType2FieldHeroMaterialization materialization)
        {
            _spawnPlan = spawnPlan ??
                throw new ArgumentNullException(nameof(spawnPlan));
            _materialization = materialization ??
                throw new ArgumentNullException(nameof(materialization));
            if (!ReferenceEquals(spawnPlan.Definition,
                    materialization.Definition) ||
                spawnPlan.Generation != materialization.Generation)
            {
                throw new InvalidOperationException(
                    "FieldHero plan and materialization must belong to the " +
                    "same runtime publication.");
            }

        }

        public static bool ProductionReady => false;

        public NativeType2FieldHeroDefinition NativeDefinition =>
            _spawnPlan.Definition;

        public long NativePublicationGeneration => _spawnPlan.Generation;
        public byte NativeEffectiveSelector => _spawnPlan.EffectiveJob;

        public IReadOnlyList<NativeType2FieldHeroRuntimeEquipmentBinding>
            NativeEquipment => _materialization.Equipment;

        public abstract NativeType2FieldHeroActorKind NativeActorKind { get; }
        public abstract string NativeClassName { get; }
        public abstract int NativeClassVmtAddress { get; }
        public abstract int NativeClassConstructorAddress { get; }
        public abstract int NativeClassInstanceSize { get; }
        public abstract NativeFieldHeroSkillPlacement NativeSkillPlacement
            { get; }
        public abstract IReadOnlyList<NativeFieldHeroSkillContract>
            NativeSkills { get; }

        public NativeFieldHeroAbilityContract PreviewNativeAbility() =>
            BuildNativeAbility(NativeDefinition.Level);

        public abstract NativeFieldHeroAbilityContract BuildNativeAbility(
            ushort level);

        protected void RequirePlanKind(NativeType2FieldHeroActorKind expected)
        {
            if (_spawnPlan.ActorKind != expected)
            {
                throw new InvalidOperationException(
                    $"FieldHero plan kind {_spawnPlan.ActorKind} cannot " +
                    $"construct {expected}.");
            }
        }

        protected static NativeFieldHeroAbilityPair Pair(int low, int high) =>
            new(low, high);

        protected static IReadOnlyList<NativeFieldHeroSkillContract> SkillSet(
            params NativeFieldHeroSkillContract[] skills) =>
            new ReadOnlyCollection<NativeFieldHeroSkillContract>(skills);

        protected static int RoundPositiveRatioToEven(long numerator,
            long denominator)
        {
            if (numerator < 0)
                throw new ArgumentOutOfRangeException(nameof(numerator));
            if (denominator <= 0)
                throw new ArgumentOutOfRangeException(nameof(denominator));

            var quotient = numerator / denominator;
            var remainder = numerator % denominator;
            var twiceRemainder = remainder * 2;
            if (twiceRemainder > denominator ||
                (twiceRemainder == denominator && (quotient & 1) != 0))
            {
                quotient++;
            }
            return unchecked((int)quotient);
        }

        public sealed override void Initialize()
        {
            throw DormantOperation(nameof(Initialize));
        }

        public sealed override void Run()
        {
            throw DormantOperation(nameof(Run));
        }

        private static InvalidOperationException DormantOperation(
            string operation) => new(
                $"{ProductionNoGoReason} Operation={operation}.");
    }
}
