using System.Collections.ObjectModel;
using GameSvr.Services;
using SystemModule;

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

    public static class NativeFieldHeroBaseConstructorCapture
    {
        public const int RandomMaximum = 34;
        public const int RandomOffset = 30;

        public static int CaptureRandom05F8(Func<int, int> random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            return unchecked(random(RandomMaximum) + RandomOffset);
        }

        public static int CaptureTick(Func<int> getTickCount)
        {
            if (getTickCount == null)
                throw new ArgumentNullException(nameof(getTickCount));
            return getTickCount();
        }
    }

    /// <summary>
    /// Dormant FieldHero runtime base. It owns a spawn-plan handle for its
    /// complete lifetime, which in turn keeps the selected publication alive.
    /// Production construction and scheduling are deliberately not exposed.
    /// </summary>
    public abstract class TFieldHero : AiMon
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
        private IReadOnlyList<NativeFieldHeroRuntimeDropBinding>
            _nativeBoundDropItems =
                Array.Empty<NativeFieldHeroRuntimeDropBinding>();

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

            // sub_6094E8 writes these fields in this order after TAIMon.Create.
            m_MagicList = new List<TUserMagic>();
            NativeRaw03AC = 1;
            m_nWalkSpeed = 700;
            m_nNextHitTime = 1000;
            NativeSpellCooldownInterval = 2000;
            NativeRaw0608 = 10;
            m_nViewRange = 7;
            m_btHair = 1;
            m_btRaceServer = 0x83;
            NativeRaw02E8 = 1;
            NativeRaw05F8 = NativeFieldHeroBaseConstructorCapture
                .CaptureRandom05F8(M2Share.RandomNumber.Random);
            NativeRaw05FC = 8;

            var currentTick = NativeFieldHeroBaseConstructorCapture
                .CaptureTick(HUtil32.GetTickCount);
            NativeRaw0634 = currentTick;
            NativeLastAttackTick = currentTick;
            NativeRaw060C = currentTick;
            NativeLastSpellTick = currentTick;
            NativeSpecialSkillTick = currentTick;
            NativeRaw0610 = currentTick;

            // sub_6094E8 constructs the independent actor+0x63C container
            // after the common tick fields. It is never aliased to m_UseItems.
            NativeOwnedEquipment =
                new NativeFieldHeroEquipmentContainer(this);
            NativeLifetimeRemaining = 0;
            NativeFameRank = 0;
        }

        public static bool ProductionReady => false;

        public NativeType2FieldHeroDefinition NativeDefinition =>
            _spawnPlan.Definition;

        public long NativePublicationGeneration => _spawnPlan.Generation;
        public byte NativeEffectiveSelector => _spawnPlan.EffectiveJob;

        public byte NativeRaw03AC { get; private protected set; }
        public int NativeSpellCooldownInterval { get; private protected set; }
        public int NativeRaw0608 { get; private protected set; }
        public byte NativeRaw02E8 { get; private protected set; }
        public int NativeRaw05F8 { get; private protected set; }
        public int NativeRaw05FC { get; private protected set; }
        public int NativeRaw0634 { get; private protected set; }
        public int NativeLastAttackTick { get; private protected set; }
        public int NativeRaw060C { get; private protected set; }
        public int NativeLastSpellTick { get; private protected set; }
        public int NativeSpecialSkillTick { get; private protected set; }
        public int NativeRaw0610 { get; private protected set; }
        public int NativeLifetimeRemaining { get; private protected set; }
        public int NativeFameRank { get; private protected set; }
        public NativeFieldHeroEquipmentContainer NativeOwnedEquipment
            { get; }

        public IReadOnlyList<NativeType2FieldHeroRuntimeEquipmentBinding>
            NativeEquipment => _materialization.Equipment;
        public IReadOnlyList<NativeFieldHeroRuntimeDropBinding>
            NativeDropItems => Volatile.Read(ref _nativeBoundDropItems);

        /// <summary>
        /// Future production Fill callback for the final actor+0x474 write.
        /// Only this actor's captured publication generation may be bound.
        /// </summary>
        private protected void BindNativeDropItemsFromFill(
            IReadOnlyList<NativeFieldHeroRuntimeDropBinding> dropItems)
        {
            ArgumentNullException.ThrowIfNull(dropItems);
            if (!ReferenceEquals(dropItems, _materialization.DropItems))
            {
                throw new InvalidOperationException(
                    "FieldHero Fill cannot bind a drop table from another " +
                    "runtime publication.");
            }
            Volatile.Write(ref _nativeBoundDropItems, dropItems);
        }

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
