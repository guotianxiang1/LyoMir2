namespace GameSvr
{
    public partial class HeroObject
    {
        // Native THeroAct+0x6D9. CM3503 overwrites this byte on every
        // hero-present branch; its wider business meaning is not inferred.
        internal byte m_btNativeDragonBreakState6D9;
    }
}
