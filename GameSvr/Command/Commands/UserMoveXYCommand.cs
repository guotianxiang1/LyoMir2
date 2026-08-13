using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 注册表记录 0x007B65D4 `05 "gowgo"`，+0x18 = 29，+0x1C = 0。
    // jt[29] @0x00622B90 = 37 3b 62 00 -> 0x00623B37，该 case 只是
    // `push p3 / ecx=p2 / edx=p1 / eax=self / call 0x006CE400`，下面的分支全在 0x006CE400 里：
    //   0x006CE422 cmp byte [ebx+0x675],2 / jb  -> 权限 >= 2 走 GM 定点移动 (call 0x006D06C0)
    //   0x006CE43F cmp byte [ebx+0x1BC],0 / je  -> 未戴传送装备则静默返回
    //   0x006CE452 cmp byte [eax+0x6B],0  / jne -> 0x006CE4FE "在这里您无法使用" (cx=0x38FF)
    //   0x006CE46D cmp eax,0x2710 / jbe        -> 未满 10 秒走 0x006CE4B7 倒计时提示
    // 旧命令名 UserMoveXY / UserMove 在全镜像 GBK+UTF8+UTF16LE 三编码 0 命中。
    [GameCommand("gowgo", "移动(GMLevel >= 2)，同一地图可以不指定地图名",
        "[地图名|无] X坐标 Y坐标", 0)]
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
