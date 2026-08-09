using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr.Command.Commands
{
    
    
    
    [GameCommand("ReloadItemDB", "重新加载物品数据库", 4)]

    public class ReloadGameItemCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadMonItems(TPlayObject PlayObject)
        {
            M2Share.CommonDB.LoadItemsDB();
            // 原生 sub_622820 的 @Reload 分支随物品加载重跑 sub_74DEDC，一并刷新
            // Share/config/powerupItem.ini 的物品使用 mode-1(英雄护符填充) refill 表。
            M2Share.UserEngine?.LoadNativePowerupItems();
            PlayObject.SysMsg("物品数据库重新加载完成。", MsgColor.Green, MsgType.Hint);
        }
    }
}