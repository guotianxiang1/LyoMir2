using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("GMActCtrl", "GM活动控制", "活动类型 开关(0/1)", 4)]
    public class GMActCtrlCommand : BaseCommond
    {
        [DefaultCommand]
        public void GMActCtrl(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sActType = @Params.Length > 0 ? @Params[0] : "";
            var nState = @Params.Length > 1 ? HUtil32.Str_ToInt(@Params[1], 0) : 0;
            if (string.IsNullOrEmpty(sActType))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            NativeCommandFailure.Report(PlayObject, "GMActCtrl",
                "原版活动配置和动作状态机尚未移植，未修改活动状态。");
        }
    }
}
