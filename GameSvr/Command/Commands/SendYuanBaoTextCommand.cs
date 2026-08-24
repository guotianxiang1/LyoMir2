using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SendYuanBaoText", "GM自身发送烟花状的元宝样式的消息内容", "消息内容", 4)]
    public class SendYuanBaoTextCommand : BaseCommond
    {
        [DefaultCommand]
        public void SendYuanBaoText(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            if (@Params == null || @Params.Length == 0 ||
                string.IsNullOrEmpty(@Params[0]))
            {
                var help = GameCommand?.ShowHelp ??
                    "命令格式: @SendYuanBaoText 消息内容";
                PlayObject.SysMsg(help, MsgColor.Red, MsgType.Hint);
                return;
            }

            PlayObject.TryCreateNativeGmFireworkText(@Params[0]);
        }
    }
}
