using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadunBindItem", "重新加载解绑物品配置", "", 4)]
    public class ReloadunBindItemCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadunBindItem(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "ReloadunBindItem",
                "原版 UnbindItem.txt 加载器尚未移植，未替换线上配置。");
        }
    }
}
