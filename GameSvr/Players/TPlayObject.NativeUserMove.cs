using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal Envirnoment m_NativeUserMoveEnvir;

        internal static bool HasNativeUserMoveCooldownElapsed(int currentTick,
            int previousTick) =>
            unchecked((uint)(currentTick - previousTick)) > 10000U;

        internal void QueueNativeUserMove(Envirnoment environment,
            int currentTick, int x, int y)
        {
            m_dwTeleportTick = currentTick;
            m_NativeUserMoveEnvir = environment;
            SendDelayMsg(this, Grobal2.RM_USERMOVE, 0, x, y, 0,
                string.Empty, 1500);
        }

        private void CompleteNativeUserMove(TProcessMessage processMsg)
        {
            var environment = m_NativeUserMoveEnvir;
            if (environment != null && ReferenceEquals(environment, m_PEnvir))
            {
                ExecuteNativeUserMove(environment, processMsg.nParam1,
                    processMsg.nParam2);
            }

            m_NativeUserMoveEnvir = null;
        }

        internal void ExecuteNativeUserMove(Envirnoment environment, int x, int y)
        {
            if (environment == null
                || M2Share.nServerIndex != environment.nServerIndex)
                return;
            if (!TryResolveNativeUserMoveCoordinates(environment, ref x, ref y))
                return;

            TrySpaceMoveToEnvironment(environment, unchecked((short)x),
                unchecked((short)y), 0, true, true);
        }

        // Native sub_7782D0 keeps command coordinates as signed Int32 until
        // it has selected a valid map cell. MOVE-63: that is one function for
        // all 11 native callers, so this forwards to the shared primitive
        // rather than carrying a second copy of the search.
        private static bool TryResolveNativeUserMoveCoordinates(
            Envirnoment environment, ref int x, ref int y) =>
            NativeGetRandomXY(environment, ref x, ref y);
    }
}
