using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadSmsUserList", "重新加载短信用户列表", "", 4)]
    public class ReloadSmsUserListCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadSmsUserList(TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var loaded = M2Share.UserEngine?.ReloadNativeSmsUserList() == true;
            PlayObject.SysMsg(
                loaded ? "加载SmsUserList.txt成功" : "加载SmsUserList.txt失败",
                MsgColor.Green,
                MsgType.Hint);
        }
    }
}
