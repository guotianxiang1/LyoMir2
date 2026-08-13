namespace GameSvr
{
    public partial class TPlayObject
    {
        // STATE-19 / POIS-19 — native TPlayer (VMT 0x6AC8C8) takes both of the
        // THumanKind overrides:
        //   +0xC8  = 0x746604 (MakePosion)   instead of TCreature's 0x76B3C8
        //   +0x1E8 = 0x7465D4 (CanAddState)  instead of TCreature's 0x772F84
        // Bodies and byte evidence live on the shared helpers in
        // TBaseObject.NativeMakePosion.cs.

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
