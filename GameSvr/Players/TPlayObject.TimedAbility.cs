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

        // 战神 sub_6E99B8 sends SM 3554 (the whole timed-ability list) once per login
        // via the login-burst virtual sub_6E9A98. Called from UserLogon. See
        // TBaseObject.BuildTimedAbilityListState for the frame/record evidence.
        private void SendNativeTimedAbilityListOnLogon()
        {
            var state = BuildTimedAbilityListState();
            SendSocket(state.Header, state.Body);
        }
    }
}
