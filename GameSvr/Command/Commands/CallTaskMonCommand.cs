using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("CallTaskMon", "召唤任务怪物", "怪物名称 地图 X Y", 4)]
    public class CallTaskMonCommand : BaseCommond
    {
        [DefaultCommand]
        public void CallTaskMon(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sMonName = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sMonName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            NativeCommandFailure.Report(PlayObject, "CallTaskMon",
                "原版怪物攻城生成参数与所有权尚未移植，未生成怪物。");
        }
    }
}
