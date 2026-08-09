using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadMonitemsTreeCfg", "重新加载怪物爆率树配置", "", 4)]
    public class ReloadMonitemsTreeCfgCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadMonitemsTreeCfg(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "ReloadMonitemsTreeCfg",
                "原版 MonItemsTree.txt 加载器尚未移植，未替换线上配置。");
        }
    }
}
