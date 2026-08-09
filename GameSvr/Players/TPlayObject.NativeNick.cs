using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        public int m_nNickLinFu;
        public int m_nIncNickLinFu;
        public int m_nDecNickLinFu;

        public void DecNativeNickLinFu(int value)
        {
            var balance = m_nNickLinFu;
            if (value > balance)
            {
                m_nDecNickLinFu = unchecked(m_nDecNickLinFu + balance);
                m_nNickLinFu = 0;
            }
            else
            {
                m_nDecNickLinFu = unchecked(m_nDecNickLinFu + value);
                m_nNickLinFu = unchecked(m_nNickLinFu - value);
            }
        }

        public void AddNativeNickLinFu(int value, bool enabled)
        {
            if (!enabled) return;
            m_nNickLinFu = unchecked(m_nNickLinFu + value);
            m_nIncNickLinFu = unchecked(m_nIncNickLinFu + value);
        }

        public void IncNativeNickLinFu(int value, int multiplier, bool enabled)
        {
            if (value <= 0 || !enabled) return;
            var increase = unchecked(value * multiplier);
            AddNativeNickLinFu(increase, true);
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0xFC, 0,
                "您获得了" + increase + "张圣殿灵符");
        }
    }
}
