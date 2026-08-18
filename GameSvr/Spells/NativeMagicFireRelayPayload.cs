namespace GameSvr
{
    internal sealed class NativeMagicFireRelayPayload
    {
        internal NativeMagicFireRelayPayload(int effectiveLevel)
        {
            EffectiveLevel = effectiveLevel;
        }

        internal int EffectiveLevel { get; }
    }
}
