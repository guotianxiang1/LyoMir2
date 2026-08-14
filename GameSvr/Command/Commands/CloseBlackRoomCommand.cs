using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // sub_6AC524 @0x006AC524 — GM关小黑屋: find player, teleport to configured map/x/y
    [GameCommand("CloseBlackRoom", "关玩家进小黑屋", "角色名 地图 X Y", 4)]
    public class CloseBlackRoomCommand : BaseCommond
    {
        [DefaultCommand]
        public void CloseBlackRoom(string[] @params, TPlayObject playObject)
        {
            if (playObject == null)
                return;
            if (@params == null || @params.Length < 4)
            {
                playObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }

            var name = @params[0];
            var map = @params[1];
            var x = HUtil32.Str_ToInt(@params[2], -1);
            var y = HUtil32.Str_ToInt(@params[3], -1);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(map) || x < 0 || y < 0)
            {
                playObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }

            NativeAntiCheatHostRuntime.TryCloseBlackRoom(playObject, name, map, x, y, true);
        }
    }
}
