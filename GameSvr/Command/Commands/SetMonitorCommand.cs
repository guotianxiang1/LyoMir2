using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // idx 510 perm 3 @0x006294EB -> sub_79F908(charName, monType) on off_7D62A4 monitor list
    [GameCommand("SetMonitor", "设置NPC监控", "NPC名 0/1", 3)]
    public class SetMonitorCommand : BaseCommond
    {
        [DefaultCommand]
        public void SetMonitor(string[] @params, TPlayObject playObject)
        {
            if (playObject == null)
                return;
            if (@params == null || @params.Length < 2)
            {
                playObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }

            var npcName = @params[0];
            var mode = HUtil32.Str_ToInt(@params[1], -1);
            if (string.IsNullOrEmpty(npcName) || (mode != 0 && mode != 1))
            {
                playObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }

            NativeAntiCheatHostRuntime.TrySetNpcMonitor(npcName, mode == 1, out var message);
            playObject.SysMsg(message, MsgColor.Yellow, MsgType.Hint);
        }
    }
}
