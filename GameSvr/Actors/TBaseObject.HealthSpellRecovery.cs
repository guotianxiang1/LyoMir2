namespace GameSvr
{
    public partial class TBaseObject
    {
        protected void DecreaseHealthSpellRecoveryStep(byte amount)
        {
            m_sbHealthSpellRecoveryStep = unchecked(
                (sbyte)(m_sbHealthSpellRecoveryStep - amount));
        }
    }
}
