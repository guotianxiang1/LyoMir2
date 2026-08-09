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
            NativeCommandFailure.Report(PlayObject, "ReloadSmsUserList",
                "原版 SmsUserList.txt 加载器尚未移植，未替换线上配置。");
        }
    }
}
