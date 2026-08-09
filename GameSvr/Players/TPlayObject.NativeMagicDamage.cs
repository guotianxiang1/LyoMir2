using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal void RunNativeHealthSpellDirty(int currentTick)
        {
            if (m_boDeath || m_WAbil.HP <= 0)
            {
                return;
            }

            if (unchecked((uint)(currentTick - dwTick57C)) <= 500u)
            {
                return;
            }

            dwTick57C = currentTick;
            if (!m_boNativeHealthSpellDirty)
            {
                return;
            }

            m_boNativeHealthSpellDirty = false;
            SendMsg(this, Grobal2.RM_HEALTHSPELLCHANGED,
                0, 0, 0, 0, string.Empty);
        }
    }
}
