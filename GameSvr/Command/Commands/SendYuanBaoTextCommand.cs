using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SendYuanBaoText", "发送元宝广播文本", "文本内容", 4)]
    public class SendYuanBaoTextCommand : BaseCommond
    {
        [DefaultCommand]
        public void SendYuanBaoText(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sText = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sText))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            NativeCommandFailure.Report(PlayObject, "SendYuanBaoText",
                "原版烟花元宝消息协议尚未确认，未发送广播。");
        }
    }
}
