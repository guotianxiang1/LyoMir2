using GameSvr.Services;

namespace GameSvr
{
    public partial class TBaseObject
    {
        public NativeType2MonsterDefinition m_NativeMonsterDefinition;
        public byte[] m_NativeMonsterActorNameBytes = Array.Empty<byte>();
        public int m_nNativeMonsterManagerLookupValue;
        public ushort m_wNativeMonsterManagerId;
        public byte m_btNativeMonsterClassification;
        public int m_nNativeMonsterClassificationValue;
        public ushort m_wNativeMonsterSpeedPoint;
        public ushort m_wNativeMonsterHitPoint;
        public int m_nNativeMonsterSuperForceMask;
        public int m_nNativeMonsterSuperForceReductionPercent;
        public int m_nNativeMonsterJobFastness;

        public void ApplyNativeType2MonsterProjection(
            NativeType2MonsterActorProjection projection)
        {
            if (projection == null)
                throw new ArgumentNullException(nameof(projection));

            m_NativeMonsterDefinition = projection.Definition;
            m_NativeMonsterActorNameBytes = projection.CopyActorNameBytes();
            m_sCharName = projection.ActorName;
            m_nNativeMonsterManagerLookupValue =
                projection.ManagerLookupValue;
            m_wNativeMonsterManagerId = projection.ManagerId;
            m_btNativeMonsterClassification = projection.Classification;
            m_nNativeMonsterClassificationValue =
                projection.ClassificationValue;
            m_wNativeMonsterSpeedPoint = projection.Speed;
            m_wNativeMonsterHitPoint = projection.Hit;
            m_btSpeedPoint = unchecked((byte)projection.Speed);
            m_wSpeedPoint = projection.Speed;
            m_btHitPoint = projection.Hit;
            m_nNativeMonsterSuperForceMask = projection.SuperForceMask;
            m_nNativeMonsterSuperForceReductionPercent =
                projection.SuperForceReductionPercent;
            m_nNativeMonsterJobFastness = projection.JobFastness;
        }

        public int ApplyNativeMonsterSuperForceReduction(
            TBaseObject attacker, int damage)
        {
            if (attacker == null) return damage;
            return NativeType2MonsterActorProjection.ApplySuperForceReduction(
                damage, m_nNativeMonsterSuperForceMask,
                m_nNativeMonsterSuperForceReductionPercent,
                attacker.m_btJob);
        }
    }
}
