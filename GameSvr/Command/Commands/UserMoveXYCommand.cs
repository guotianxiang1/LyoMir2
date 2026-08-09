using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("UserMoveXY", "移动地图指定座标(需要戴传送装备)", 0)]
    public class UserMoveXYCommand : BaseCommond
    {
        [DefaultCommand]
        public void UserMoveXY(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || PlayObject == null)
            {
                return;
            }
            var sX = @Params.Length > 0 ? @Params[0] : "";
            var sY = @Params.Length > 1 ? @Params[1] : "";

            if (PlayObject.m_btPermission >= 2)
            {
                PositionMoveAsGameMaster(PlayObject, @Params);
                return;
            }

            if (!PlayObject.m_boNativeUserMove)
                return;

            var environment = PlayObject.m_PEnvir;
            if (environment == null)
                return;
            if (environment.Flag.boNOPOSITIONMOVE)
            {
                SendNativeUserMoveFailure(PlayObject, "在这里您无法使用");
                return;
            }

            var currentTick = HUtil32.GetTickCount();
            var elapsed = unchecked((uint)(currentTick - PlayObject.m_dwTeleportTick));
            if (!TPlayObject.HasNativeUserMoveCooldownElapsed(currentTick,
                    PlayObject.m_dwTeleportTick))
            {
                SendNativeUserMoveFailure(PlayObject,
                    10U - elapsed / 1000U + " 秒后方可使用");
                return;
            }

            var nX = HUtil32.Str_ToInt(sX, 0);
            var nY = HUtil32.Str_ToInt(sY, 0);
            PlayObject.QueueNativeUserMove(environment, currentTick, nX, nY);
        }

        private static void SendNativeUserMoveFailure(TPlayObject playObject,
            string message) =>
            playObject.SendMsg(playObject, Grobal2.RM_SYSMESSAGE, 0,
                0xFF, 0x38, 0, message);

        private static void PositionMoveAsGameMaster(TPlayObject playObject,
            string[] parameters)
        {
            var first = parameters.Length > 0 ? parameters[0] : string.Empty;
            var second = parameters.Length > 1 ? parameters[1] : string.Empty;
            var third = parameters.Length > 2 ? parameters[2] : string.Empty;
            Envirnoment environment = null;
            var x = 0;
            var y = 0;

            if (!string.IsNullOrEmpty(third))
            {
                environment = M2Share.MapManager.FindMap(first);
                x = HUtil32.Str_ToInt(second, 0);
                y = HUtil32.Str_ToInt(third, 0);
            }
            else if (!string.IsNullOrEmpty(second))
            {
                environment = playObject.m_PEnvir;
                x = HUtil32.Str_ToInt(first, 0);
                y = HUtil32.Str_ToInt(second, 0);
            }
            else if (!string.IsNullOrEmpty(first))
            {
                environment = M2Share.MapManager.FindMap(first);
                if (environment != null)
                {
                    x = M2Share.RandomNumber.Random(environment.wWidth);
                    y = M2Share.RandomNumber.Random(environment.wHeight);
                }
                else
                {
                    var target = M2Share.UserEngine.GetPlayObjectEx(first);
                    environment = target != null && !target.m_boGhost &&
                                  target.m_boReadyRun
                        ? target.m_PEnvir
                        : null;
                    if (environment != null)
                    {
                        GetTargetFrontPosition(target, environment,
                            out var targetX, out var targetY);
                        x = targetX;
                        y = targetY;
                    }
                }
            }

            if (environment != null)
                playObject.ExecuteNativeUserMove(environment, x, y);
        }

        private static void GetTargetFrontPosition(TPlayObject target,
            Envirnoment environment, out short x, out short y)
        {
            var (offsetX, offsetY) = (target.m_btDirection & 7) switch
            {
                Grobal2.DR_UP => (0, -1),
                Grobal2.DR_UPRIGHT => (1, -1),
                Grobal2.DR_RIGHT => (1, 0),
                Grobal2.DR_DOWNRIGHT => (1, 1),
                Grobal2.DR_DOWN => (0, 1),
                Grobal2.DR_DOWNLEFT => (-1, 1),
                Grobal2.DR_LEFT => (-1, 0),
                _ => (-1, -1)
            };
            var targetX = target.m_nCurrX + offsetX;
            var targetY = target.m_nCurrY + offsetY;
            if (targetX <= 0 || targetX >= environment.wWidth - 1)
                targetX = target.m_nCurrX;
            if (targetY <= 0 || targetY >= environment.wHeight - 1)
                targetY = target.m_nCurrY;
            x = (short)targetX;
            y = (short)targetY;
        }
    }
}
