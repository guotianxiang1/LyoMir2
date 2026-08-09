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
            if (partner.m_PEnvir == null ||
                partner.m_PEnvir.MoveToMovingObject(partner.m_nCurrX,
                    partner.m_nCurrY, partner, m_nCurrX, m_nCurrY,
                    true) <= 0)
            {
                return;
            }

            partner.m_nCurrX = m_nCurrX;
            partner.m_nCurrY = m_nCurrY;
            partner.RemoveNativeMovementTimedState(23);
            partner.ProcessNativeMoveActionWithoutBroadcast();
            partner.SearchViewRange();
        }
    }
}
