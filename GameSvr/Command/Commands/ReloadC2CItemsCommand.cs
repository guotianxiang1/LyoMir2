using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadC2CItems", "重新加载C2C物品配置", "", 5)]
    public class ReloadC2CItemsCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadC2CItems(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "ReloadC2CItems",
                "原版 c2cForbidItems.txt 加载器尚未移植，未替换线上配置。");
        }
    }
}
