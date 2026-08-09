namespace GameSvr
{
    public partial class TPlayObject
    {
        protected override void SendTimedAbilityClientState(byte internalType,
            int remainingMilliseconds, int value, bool removed)
        {
            var state = BuildTimedAbilityClientState(internalType,
                remainingMilliseconds, value, removed);
            SendSocket(state.Header, state.Body);
        }
    }
}
