using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("ClearMission", "清除指定玩家的任务标志", "人物名称", 10)]
    public class ClearMissionCommand : BaseCommond
    {
        [DefaultCommand]
        public void ClearMission(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumanName = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sHumanName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            if (sHumanName[0] == '?')
            {
                PlayObject.SysMsg("此命令用于清除人物的任务标志。", MsgColor.Blue, MsgType.Hint);
                return;
            }
            // 诚实 fail-closed：此前实现找到玩家后仅打印"任务标志已经全部清零"却【从不清除任何标志】——
            // 又一条假成功命令。C# 无已确认的任务标志清除入口、原版清除核心亦未在现有转储中，
            // 故如实上报未移植，不再谎报成功。待逆向/确认清除入口后再接线。
            NativeCommandFailure.Report(PlayObject, "ClearMission",
                "原版清除玩家任务标志尚未移植，未清除任何任务标志。");
        }
    }
}