using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int NativeChannelMagicCancelRefMessage = Grobal2.SM_CHANNEL_MAGIC_CANCEL;
        private const int NativeLocationChannelMagicCancelRefMessage = Grobal2.SM_LOCATION_CHANNEL_MAGIC_CANCEL;

        internal uint m_dwNativeChannelMagicTick;
        internal ushort m_wNativeChannelMagicId;
        internal ushort m_wNativeChannelMagicParam;

        internal bool m_boNativeLocationChannelActive;
        internal uint m_dwNativeLocationChannelStartTick;
        internal uint m_dwNativeLocationChannelPulseTick;
        internal int m_nNativeLocationChannelContext0;
        internal uint m_dwNativeLocationChannelDuration;
        internal int m_nNativeLocationChannelContext1;
        internal int m_nNativeLocationChannelContext2;
        internal int m_nNativeLocationChannelX;
        internal int m_nNativeLocationChannelY;
        internal int m_nNativeLocationChannelMapToken;
        internal ushort m_wNativeLocationChannelMagicId;

        internal bool m_boNativeHorseCallPending;
        internal uint m_dwNativeHorseCallTick;
        internal ushort m_wNativeHorseCallDelay;

        internal int m_nNativeUnionActivationCarrier;

        internal void CancelNativeType51PendingForTimedAbility()
        {
            if (!m_boNativeHorseCallPending)
            {
                return;
            }

            m_boNativeHorseCallPending = false;
            m_dwNativeHorseCallTick = 0;
            m_wNativeHorseCallDelay = 0;
            if (!m_boGhost && m_PEnvir != null)
            {
                SendRefMsg(Grobal2.RM_NATIVE_HORSE_CALL_STOP, 0, 0, 0, 0,
                    string.Empty);
            }
        }

        internal void CancelNativeChannelMagic()
        {
            var magicId = m_wNativeChannelMagicId;
            if (magicId == 0)
            {
                return;
            }

            m_dwNativeChannelMagicTick = 0;
            m_wNativeChannelMagicId = 0;
            m_wNativeChannelMagicParam = 0;
            if (!m_boGhost && m_PEnvir != null)
            {
                SendRefMsg(NativeChannelMagicCancelRefMessage, magicId,
                    0, 0, 0, string.Empty);
            }
        }

        internal void CancelNativeLocationChannelMagic()
        {
            m_boNativeLocationChannelActive = false;
            var magicId = m_wNativeLocationChannelMagicId;
            if (magicId == 0)
            {
                return;
            }

            m_dwNativeLocationChannelStartTick = 0;
            m_dwNativeLocationChannelPulseTick = 0;
            m_nNativeLocationChannelContext0 = 0;
            m_dwNativeLocationChannelDuration = 0;
            m_nNativeLocationChannelContext1 = 0;
            m_nNativeLocationChannelContext2 = 0;
            m_nNativeLocationChannelX = 0;
            m_nNativeLocationChannelY = 0;
            m_wNativeLocationChannelMagicId = 0;
            if (!m_boGhost && m_PEnvir != null)
            {
                SendRefMsg(NativeLocationChannelMagicCancelRefMessage,
                    magicId, 0, 0, 0, string.Empty);
            }
        }

    }
}
