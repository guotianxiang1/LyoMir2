namespace GameSvr
{
    public partial class TPlayObject
    {
        private const uint NativeSwitchHeroHandoffDelay = 5000;

        internal void RecordNativeSwitchHeroRequestTick(int currentTick)
        {
            switch (m_btNativeHeroRequestKind)
            {
                case 0:
                    m_dwNativeSwitchHeroKind0Tick = currentTick;
                    break;
                case 1:
                    m_dwNativeSwitchHeroKind1Tick = currentTick;
                    break;
            }
        }

        private bool IsNativeSwitchHeroRequestDue(int currentTick)
        {
            return m_btNativeHeroRequestKind switch
            {
                0 => unchecked((uint)(currentTick -
                    m_dwNativeSwitchHeroKind0Tick)) >= NativeSwitchHeroHandoffDelay,
                1 => unchecked((uint)(currentTick -
                    m_dwNativeSwitchHeroKind1Tick)) >= NativeSwitchHeroHandoffDelay,
                _ => false
            };
        }

        internal bool TryConsumeNativeSwitchHeroHandoff(int currentTick,
            out byte heroKind, out byte heroSlot)
        {
            heroKind = 0;
            heroSlot = 0;
            if (m_HeroObject?.m_boGhost == true)
                m_HeroObject = null;

            if (m_boGhost || !m_boNativeSwitchHeroHandoffPending ||
                !IsNativeSwitchHeroRequestDue(currentTick))
                return false;

            RecordNativeSwitchHeroRequestTick(currentTick);
            m_boNativeSwitchHeroHandoffPending = false;

            var environment = m_PEnvir;
            if (environment?.Flag == null || environment.Flag.boDARE ||
                environment.Flag.boNOHERO || m_HeroObject != null ||
                _nativeEquipLockActive)
                return false;

            heroKind = m_btNativeHeroRequestKind;
            heroSlot = m_btNativeHeroRequestSlot;
            return true;
        }

        private void RunNativeSwitchHeroHandoff(int currentTick)
        {
            if (TryConsumeNativeSwitchHeroHandoff(currentTick,
                    out var heroKind, out var heroSlot))
                HeroDataService.RequestLoad(this, heroKind, heroSlot);
        }
    }
}
