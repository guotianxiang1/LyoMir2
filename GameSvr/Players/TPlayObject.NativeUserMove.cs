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
        // it has selected a valid map cell.
        private static bool TryResolveNativeUserMoveCoordinates(
            Envirnoment environment, ref int x, ref int y)
        {
            if (x <= 0)
                x = M2Share.RandomNumber.Random(environment.wWidth) + 1;
            if (y <= 0)
                y = M2Share.RandomNumber.Random(environment.wHeight) + 1;

            var step = environment.wWidth < 50 ? 2 : 3;
            var margin = environment.wHeight < 30
                ? 2
                : environment.wHeight < 250 ? 20 : 50;

            for (var attempt = 0; attempt < 31; attempt++)
            {
                if (environment.CanWalk(x, y, true))
                    return true;

                if (x < environment.wWidth - margin - 1)
                {
                    x += step;
                }
                else
                {
                    x = M2Share.RandomNumber.Random(environment.wWidth / 2)
                        + margin;
                    if (y < environment.wHeight - margin - 1)
                    {
                        y += step;
                    }
                    else
                    {
                        y = M2Share.RandomNumber.Random(environment.wHeight / 2)
                            + margin;
                    }
                }
            }

            if (environment.m_PointList == null
                || environment.m_PointList.Count == 0)
                return false;

            var point = environment.m_PointList[
                M2Share.RandomNumber.Random(environment.m_PointList.Count)];
            x = unchecked((ushort)point.nX);
            y = unchecked((ushort)point.nY);
            return true;
        }
    }
}
