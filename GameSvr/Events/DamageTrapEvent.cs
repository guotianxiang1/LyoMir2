using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>TDamageTrapEvent</c>, self-pointer 0x716C58 / VMT 0x716CA4,
    /// instance size 136, parent TBTFireBurnEvent.
    /// <para>
    /// It overrides nothing. Its VMT is a byte-for-byte copy of
    /// TBTFireBurnEvent's — +0x00 0x7172F4, +0x04 0x7172F8, +0x08 0x7179AC,
    /// +0x0C 0x717424, +0x10 0x717ABC, +0x14 0x717B3C, Destroy 0x7178EC — so Run,
    /// ApplyTo, damage growth, the 5-minute map re-assert and the 30,000,000
    /// sentinel are all literally the same code.
    /// </para>
    /// <para>
    /// The whole class is one constructor, <c>sub_717C48</c> (<c>ret 0x1C</c>),
    /// which forwards all seven stack slots to TBTFireBurnEvent.Create in the
    /// same positional order (0x717C5D-0x717C78 push [ebp+0x20] down to
    /// [ebp+8]) with ecx untouched, then restamps the type:
    /// <c>0x717C82 C6 46 0C 1C mov byte [esi+0x0C],0x1C</c>.
    /// Unlike the parent's stamp at 0x717A97, this one sits outside the
    /// <c>[obj+0x20] != 0</c> guard, so it lands even when AddToMap failed.
    /// </para>
    /// </summary>
    public class DamageTrapEvent : BTFireBurnEvent
    {
        public DamageTrapEvent(Envirnoment envir, int nX, int nY, int nTime,
            int nDamage, int nGrowInterval, int nGrowStep, TBaseObject owner)
            : base(envir, nX, nY, nTime, nDamage, nGrowInterval, nGrowStep, owner)
        {
            m_nEventType = Grobal2.ET_DAMAGETRAP;
        }
    }
}
