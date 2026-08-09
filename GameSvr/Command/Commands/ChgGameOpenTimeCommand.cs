using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ChgGameOpenTime", "修改开服时间", "年份 月份 日期", 5)]
    public class ChgGameOpenTimeCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgGameOpenTime(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sDate = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sDate))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            NativeCommandFailure.Report(PlayObject, "ChgGameOpenTime",
                "原版开服时间持久化位置尚未确认，未修改服务器时间配置。");
        }
    }
}
