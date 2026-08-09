using SystemModule;
using GameSvr.Services;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int NativeYbRefreshIntervalMilliseconds = 10_000;
        private const int NativeYbInitialRetryIntervalMilliseconds = 15_000;
        private const int NativeYbDealOpenIdent = 3009;
        private const int NativeYbDealProtectIdent = 3010;

        public bool m_boNativeYbAccountLoaded;
        public uint m_dwNativeYbInitialRetryTick;
        public uint m_dwNativeYbRefreshTick;
        public int m_nNativeYbTotalConsumed;
        public int m_nNativeYbRemainingSeconds;
        public int m_nNativeYbDividendConsumed;
        public bool m_boNativeFirstUsedGiftQualified;
        public bool m_boNativeYbDealOpened;
        public ushort m_wNativeYbDealProtect = 100;

        public void BeginNativeYbCreditLoad(uint currentTick)
        {
            if (YbDbClient.Instance.RequestInitialCredit(this) && bo6AB)
                m_boNativeYbAccountLoaded = true;

            m_dwNativeYbInitialRetryTick = currentTick;
            m_dwNativeYbRefreshTick = currentTick;
        }

        public void RunNativeYbCreditLoad(uint currentTick)
        {
            if (unchecked(currentTick - m_dwNativeYbInitialRetryTick) <
                NativeYbInitialRetryIntervalMilliseconds)
                return;

            m_dwNativeYbInitialRetryTick = currentTick;
            if (!m_boNativeYbAccountLoaded)
                YbDbClient.Instance.RequestInitialCredit(this);
        }

        public bool TryBeginNativeYbCreditRefresh(uint currentTick)
        {
            if (!m_boNativeYbAccountLoaded ||
                unchecked(currentTick - m_dwNativeYbRefreshTick) <
                NativeYbRefreshIntervalMilliseconds)
                return false;

            m_dwNativeYbRefreshTick = currentTick;
            m_boNativeYbAccountLoaded = false;
            return true;
        }

        public void ApplyNativeYb1103Snapshot(int currentYuanbao,
            int totalConsumed, int remainingSeconds, int dividendConsumed,
            bool responseParamIsOne)
        {
            var wasLoaded = m_boNativeYbAccountLoaded;
            var increase = unchecked(currentYuanbao - m_nGameGold);
            if (wasLoaded && increase > 0)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0,
                    increase + " 个元宝增加");
            }

            m_nGameGold = currentYuanbao;
            m_nNativeYbTotalConsumed = totalConsumed;
            m_nNativeYbRemainingSeconds = remainingSeconds;
            m_nNativeYbDividendConsumed = dividendConsumed;
            RefreshNativeLingFu();
            m_boNativeYbAccountLoaded = true;

            foreach (var packet in TakeNativeYbDealPackets(responseParamIsOne))
                SendSocket(packet, Array.Empty<byte>());
        }

        private ClientPacket[] TakeNativeYbDealPackets(bool responseParamIsOne)
        {
            if (!responseParamIsOne || m_boNativeYbDealOpened)
                return Array.Empty<ClientPacket>();

            m_boNativeYbDealOpened = true;
            return BuildNativeYbDealPackets(m_wNativeYbDealProtect);
        }

        private static ClientPacket[] BuildNativeYbDealPackets(ushort protection)
        {
            return new[]
            {
                Grobal2.MakeDefaultMsg(NativeYbDealOpenIdent, 0, 0, 0, 0),
                Grobal2.MakeDefaultMsg(NativeYbDealProtectIdent, 0,
                    protection, 0, 0)
            };
        }
    }
}
