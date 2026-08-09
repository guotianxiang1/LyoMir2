using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadWhiteList", "重新加载白名单", "", 4)]
    public class ReloadWhiteListCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadWhiteList(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "ReloadWhiteList",
                "原版 WhiteList.txt 加载器尚未移植，未替换线上配置。");
        }
    }
}
