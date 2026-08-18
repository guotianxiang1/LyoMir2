namespace GameSvr
{
    internal sealed class NativeSpellRelayPayload
    {
        internal NativeSpellRelayPayload(int effectiveLevel)
        {
            EffectiveLevel = effectiveLevel;
        }

        internal int EffectiveLevel { get; }
    }
}
