using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>Restores native dispatcher case 100 (0x00625048).</summary>
    [GameCommand("DoTask", "设置任务攻击目标", "X Y", 4)]
    public sealed class DoTaskCommand : BaseCommond
    {
        [DefaultCommand]
        public void DoTask(string[] @params, TPlayObject player)
        {
            var rawX = @params != null && @params.Length > 0
                ? @params[0]
                : null;
            var rawY = @params != null && @params.Length > 1
                ? @params[1]
                : null;

            if (!NativeGmTaskMonsterCommands.TryArmTaskTarget(player, rawX,
                    rawY, out var targetX, out var targetY))
            {
                player?.SysMsg("任务设置失败！", MsgColor.Red, MsgType.Hint);
                return;
            }

            player?.SysMsg($"任务设置：攻击目标 {targetX} : {targetY}",
                MsgColor.Green, MsgType.Hint);
        }
    }
}
