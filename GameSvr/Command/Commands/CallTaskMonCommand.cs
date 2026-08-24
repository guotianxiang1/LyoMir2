using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>Restores native dispatcher case 101 (0x0062505B).</summary>
    [GameCommand("CallTaskMon", "召唤任务怪物", "X坐标 Y坐标 怪物 数量", 4)]
    public sealed class CallTaskMonCommand : BaseCommond
    {
        [DefaultCommand]
        public void CallTaskMon(string[] @params, TPlayObject player)
        {
            if (player == null)
                return;

            var rawX = @params != null && @params.Length > 0
                ? @params[0]
                : null;
            var rawY = @params != null && @params.Length > 1
                ? @params[1]
                : null;
            var monsterName = @params != null && @params.Length > 2
                ? @params[2]
                : null;
            var rawCount = @params != null && @params.Length > 3
                ? @params[3]
                : null;

            var outcome = NativeGmTaskMonsterCommands.CallTaskMon(rawX,
                rawY, monsterName, rawCount);
            if (outcome.Result == NativeGmTaskMonsterCommands.CallTaskMonResult.NotArmed)
            {
                player.SysMsg("没有指定任务", MsgColor.Red, MsgType.Hint);
                return;
            }

            if (outcome.Result != NativeGmTaskMonsterCommands.CallTaskMonResult.Completed)
            {
                player.SysMsg("命令错误，应为：X坐标 Y坐标 怪物 数量",
                    MsgColor.Green, MsgType.Hint);
                return;
            }

            player.SysMsg($"{outcome.MapName}:{outcome.X}=>{outcome.Y} "
                + $"{outcome.RequestedCount} 只", MsgColor.Green, MsgType.Hint);
        }
    }
}
