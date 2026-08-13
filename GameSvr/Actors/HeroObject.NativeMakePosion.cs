namespace GameSvr
{
    public partial class HeroObject
    {
        // STATE-19 / POIS-19 — the native hero classes are THumanKind descendants
        // and inherit both overrides unchanged:
        //   THeroAct 0x685630, TWarHero 0x685968, TTaosHero 0x685CA0,
        //   TMagHero 0x685FD8, TSecWarHero 0x5F55A8, TSecTaosHero 0x5F58E4,
        //   TSecMagHero 0x5F5C24 — all carry +0xC8 = 0x746604 and
        //   +0x1E8 = 0x7465D4.
        // TFieldHero 0x606F1C and TShadowHero 0x719F78 are NOT in that family;
        // they keep TCreature's 0x76B3C8 / 0x772F84, so any future port of those
        // must not inherit from this class.

        internal override bool NativeMakePosion(byte stateId, ushort seconds,
            ushort point)
        {
            return NativeHumanKindMakePosion(stateId, seconds, point);
        }

        internal override bool CanAddNativeTimedAbility(byte internalType)
        {
            return CanAddNativeTimedAbilityHumanKind(internalType);
        }
    }
}
