using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("LogSwitch", "日志开关控制", "日志类型 状态(0/1)", 4)]
    public class LogSwitchCommand : BaseCommond
    {
        [DefaultCommand]
        public void LogSwitch(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sLogType = @Params.Length > 0 ? @Params[0] : "";
            var nState = @Params.Length > 1 ? HUtil32.Str_ToInt(@Params[1], 0) : 0;
            if (string.IsNullOrEmpty(sLogType))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            NativeCommandFailure.Report(PlayObject, "LogSwitch",
                "原版 LogSwitch.Bin 位映射尚未移植，未修改日志开关。");
        }
    }
}
