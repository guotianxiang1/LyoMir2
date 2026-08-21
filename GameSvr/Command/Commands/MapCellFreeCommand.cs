using GameSvr.CommandSystem;

namespace GameSvr
{
    [GameCommand("MapCellFree", "GM设置其ownmap中的每个点为free状态", "", 5)]
    public sealed class MapCellFreeCommand : BaseCommond
    {
        [DefaultCommand]
        public void MapCellFree(TPlayObject player)
        {
            player?.m_PEnvir?.SetAllNativeMapCellsWalkable();
        }
    }
}
