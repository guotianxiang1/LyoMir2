using GameSvr.CommandSystem;
using GameSvr.Mall;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadMall", "重新加载商城配置", 10)]
    public class ReloadMallCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadMall(string[] @Params, TPlayObject PlayObject)
        {
            MallManager.Instance.RefreshCache();
            PlayObject.SysMsg("商城配置已重新加载。", MsgColor.Green, MsgType.Hint);
        }
    }
}
