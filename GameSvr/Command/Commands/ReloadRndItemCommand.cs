using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to reload random item drop configuration.
    /// Usage: @ReloadRndItem
    /// </summary>
    [GameCommand("ReloadRndItem", "重新加载随机物品配置", 4)]
    public class ReloadRndItemCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadRndItem(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "ReloadRndItem",
                "原版 config\\randItems.txt 对象及加载器尚未移植，未替换线上配置。");
        }
    }
}
