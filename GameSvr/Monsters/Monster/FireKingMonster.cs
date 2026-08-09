using System.Threading;
using SystemModule;

namespace GameSvr
{
    internal static class NativeFireKingEventState
    {
        private static int s_forced;
        private static int s_maxThreshold;

        internal static bool IsForced => Volatile.Read(ref s_forced) != 0;
        internal static int MaxThreshold => Volatile.Read(ref s_maxThreshold);

        internal static void ForceLocally()
        {
            Volatile.Write(ref s_forced, 1);
        }

        internal static void ObserveThreshold(int threshold)
        {
            int current = Volatile.Read(ref s_maxThreshold);
            while (threshold > current)
            {
                int observed = Interlocked.CompareExchange(
                    ref s_maxThreshold, threshold, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }

        internal static void ResetForCheck()
        {
            Volatile.Write(ref s_maxThreshold, 0);
            Volatile.Write(ref s_forced, 0);
        }
    }

    public class FireKingMonster : AnimalObject
    {
        // ✅ 战神字节证据 (Tier-1)：race = **150**（旧值 216 无原生依据）。
        // 索引表[150-0xB=0x8B]=0x44=68 → jt[68]=0x67A985 → 该 case 用 classref
        // [0x67FF34]（VMT 0x67FF80 TFireKingMonster size 1256 parent TAnimal）
        // 调 ctor sub_6821F8。归属唯一性穷尽判据：classref 加载点全镜像 1 个
        // (0x67A987)、ctor 的 E8 rel32 调用者 1 个 (0x67A98C)、把 0xD8(=216) 当
        // race 写/比的站点 0 个、工厂 4 个调用者调用前 0x40 字节内无立即数 0xD8。
        // race 216 的索引表字节是 0x00 → jt[0]=0x67AE5E = default sink(返回 nil)。
        internal const int NativeRace = 150;
        internal const int NativeCattleDamageSkill = 103;
        internal const int NativeInitializeMessage = 10161;
        internal const int NativeInitializeDelay = 10000;

        private bool m_boNativeFurious;
        private int m_nNativeFuriousThreshold;
        private int m_nNativeSkill103Count;
        private int m_nNativeAllCallbackCount;

        internal bool NativeFurious => m_boNativeFurious;
        internal int NativeFuriousThreshold => m_nNativeFuriousThreshold;
        internal int NativeSkill103Count => m_nNativeSkill103Count;
        internal int NativeAllCallbackCount => m_nNativeAllCallbackCount;

        public FireKingMonster()
            : base()
        {
            m_WAbil.HP = m_Abil.MaxHP;
            m_WAbil.MaxHP = m_Abil.MaxHP;
            ResetFuriousThreshold();
        }

        public override void RecalcAbilitys()
        {
            base.RecalcAbilitys();
            m_WAbil.HP = m_Abil.MaxHP;
            m_WAbil.MaxHP = m_Abil.MaxHP;
            ResetFuriousThreshold();
        }

        public override void Initialize()
        {
            base.Initialize();
            SetBodyState(27, false);
            ResetFuriousThreshold();
            m_boNativeFurious = false;
            SendDelayMsg(this, NativeInitializeMessage, 1, m_WAbil.MaxHP,
                m_nCurrX, m_nCurrY, string.Empty, NativeInitializeDelay);
        }

        public override void Run()
        {
            base.Run();
            if (m_boDeath)
                return;

            if (m_WAbil.HP > 0)
                m_WAbil.HP = m_WAbil.MaxHP;

            int now = HUtil32.GetTickCount();
            if (unchecked((uint)(now - m_dwSearchEnemyTick)) > 500u)
            {
                m_dwSearchEnemyTick = now;
                if (M2Share.RandomNumber.Random(5) + 5 <=
                    m_nNativeSkill103Count)
                {
                    m_nNativeSkill103Count = 0;
                    m_btDirection = (byte)M2Share.RandomNumber.Random(8);
                    SendRefMsg(Grobal2.RM_BIGHIT, m_btDirection, m_nCurrX,
                        m_nCurrY, 0, string.Empty);
                }
                else if (M2Share.RandomNumber.Random(20) + 5 <=
                    m_nNativeAllCallbackCount)
                {
                    m_nNativeAllCallbackCount = 0;
                    m_btDirection = (byte)M2Share.RandomNumber.Random(8);
                    SendRefMsg(Grobal2.RM_HIT, m_btDirection, m_nCurrX,
                        m_nCurrY, 0, string.Empty);
                }
                else if (M2Share.RandomNumber.Random(50) == 0)
                {
                    SendRefMsg(Grobal2.RM_HIT, m_btDirection, m_nCurrX,
                        m_nCurrY, 0, string.Empty);
                }
            }

            if (NativeFireKingEventState.IsForced && !m_boNativeFurious)
            {
                NativeFireKingEventState.ObserveThreshold(
                    m_nNativeFuriousThreshold);
                m_boNativeFurious = true;
                ApplyFuriousState(false);
            }
        }

        internal override int ResolveFullMagicDamage(TBaseObject source,
            int skillId, bool arg0, MagicDamageContext context, byte category,
            int flags, int rawDamage)
        {
            m_nNativeAllCallbackCount = unchecked(
                m_nNativeAllCallbackCount + 1);
            if (skillId != NativeCattleDamageSkill)
                return 0;

            m_nNativeSkill103Count = unchecked(m_nNativeSkill103Count + 1);
            if (source is not TPlayObject player)
                return 0;

            bool wasFurious = m_boNativeFurious;
            int result = player.AddNativeCattleEvent(
                m_nNativeFuriousThreshold, rawDamage,
                ref m_boNativeFurious);
            if (!wasFurious && m_boNativeFurious)
                ApplyFuriousState(true);
            return result;
        }

        private void ResetFuriousThreshold()
        {
            m_nNativeFuriousThreshold =
                M2Share.RandomNumber.Random(200) + 2000;
        }

        private void ApplyFuriousState(bool announce)
        {
            SetBodyState(27, true);
            StatusChanged();
            if (announce)
            {
                TPlayObject.BroadcastNativeCattleMessage(
                    TPlayObject.NativeCattleFuriousAnnouncement);
            }
        }
    }
}
