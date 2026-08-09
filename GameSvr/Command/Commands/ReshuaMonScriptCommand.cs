using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReshuaMonScript", "刷新怪物脚本配置", "", 5)]
    public class ReshuaMonScriptCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReshuaMonScript(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "ReshuaMonScript",
                "原版怪物脚本缓存与重载入口尚未移植，未替换线上脚本。");
        }
    }
}
