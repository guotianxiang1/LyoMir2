using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Verified definition-to-actor projection performed by native M2
    /// sub_71EA04. It deliberately excludes definition +0x50 ForceValue,
    /// which is read by DynMonGen and is not copied into the actor.
    /// </summary>
    public sealed class NativeType2MonsterActorProjection
    {
        public const int ActorNameCapacity = 14;

        private readonly byte[] _actorNameBytes;

        private NativeType2MonsterActorProjection(
            NativeType2MonsterDefinition definition,
            NativeType2MonsterManagerFields managerFields)
        {
            Definition = definition;
            var definitionName = definition.CopyNameBytes();
            _actorNameBytes = definitionName.AsSpan(0,
                Math.Min(ActorNameCapacity, definitionName.Length)).ToArray();
            ActorName = HUtil32.GbkEncoding.GetString(_actorNameBytes);

            ManagerLookupValue = managerFields.ManagerLookupValue;
            ManagerId = managerFields.ManagerId;
            Classification = managerFields.Classification;
            ClassificationValue = managerFields.ClassificationValue;
            Speed = definition.Speed;
            Hit = definition.Hit;
            SuperForceMask = definition.SuperForceExperience;
            SuperForceReductionPercent = definition.SuperForceLevel;
            JobFastness = definition.JobFastness;
        }

        public NativeType2MonsterDefinition Definition { get; }
        public string ActorName { get; }
        public int ManagerLookupValue { get; }
        public ushort ManagerId { get; }
        public byte Classification { get; }
        public byte ClassificationValue { get; }
        public ushort Speed { get; }
        public ushort Hit { get; }
        public int SuperForceMask { get; }
        public int SuperForceReductionPercent { get; }
        public int JobFastness { get; }

        public byte[] CopyActorNameBytes() =>
            (byte[])_actorNameBytes.Clone();

        public int ApplySuperForceReduction(int damage, byte attackerJob) =>
            ApplySuperForceReduction(damage, SuperForceMask,
                SuperForceReductionPercent, attackerJob);

        public static NativeType2MonsterActorProjection Create(
            NativeType2MonsterDefinition definition,
            NativeType2MonsterManagerTables managerTables)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            var fields = managerTables == null
                ? default
                : managerTables.Resolve(definition);
            return new NativeType2MonsterActorProjection(definition, fields);
        }

        /// <summary>
        /// Exact signed integer order from native sub_767CBC. CDQ after IMUL
        /// discards the high product word, so division consumes the wrapped
        /// Int32 product and truncates toward zero.
        /// </summary>
        public static int ApplySuperForceReduction(int damage, int mask,
            int reductionPercent, byte attackerJob)
        {
            if (mask == 0 || reductionPercent == 0 || attackerJob >= 4
                || (mask & (1 << attackerJob)) == 0)
                return damage;

            var product = unchecked(damage * reductionPercent);
            var quotient = product / 100;
            var reduced = unchecked(damage - quotient);
            return Math.Max(0, reduced);
        }
    }
}
