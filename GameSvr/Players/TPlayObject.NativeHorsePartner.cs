namespace GameSvr
{
    public partial class TPlayObject
    {
        internal TPlayObject m_NativeHorsePartner;

        private void SyncNativeHorsePartnerAfterRun3()
        {
            if (!HasNativeActiveState(NativeHorseMountedState) ||
                m_NativeHorsePartner == null)
            {
                return;
            }

            var partner = m_NativeHorsePartner;
            partner.m_btDirection = m_btDirection;
            if (!partner.m_PEnvir.NativeRelocateMovingObjectNodeExact(
                    partner.m_nCurrX, partner.m_nCurrY, partner,
                    m_nCurrX, m_nCurrY))
            {
                return;
            }

            partner.m_nCurrX = m_nCurrX;
            partner.m_nCurrY = m_nCurrY;
            partner.RemoveNativeMovementTimedState(23);
            partner.ProcessNativeMoveActionWithoutBroadcast();
            partner.SendMapDescription();
        }

        // sub_6BBF4C, called once after every successful step in CharPushed.
        // This wrapper intentionally omits the state-23, cell-event and map-
        // description tail present in sub_6BBEE4.
        internal void SyncNativeHorsePartnerAfterPush(byte direction)
        {
            if (!HasNativeActiveState(NativeHorseMountedState) ||
                m_NativeHorsePartner == null)
            {
                return;
            }

            var partner = m_NativeHorsePartner;
            partner.m_btDirection = direction;
            if (!partner.m_PEnvir.NativeRelocateMovingObjectNodeExact(
                    partner.m_nCurrX, partner.m_nCurrY, partner,
                    m_nCurrX, m_nCurrY))
            {
                return;
            }

            partner.m_nCurrX = m_nCurrX;
            partner.m_nCurrY = m_nCurrY;
        }
    }
}
