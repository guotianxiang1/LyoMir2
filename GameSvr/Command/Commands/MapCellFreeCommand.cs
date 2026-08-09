using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("MapCellFree", "地图单元格释放/清理", "地图名称", 5)]
    public class MapCellFreeCommand : BaseCommond
    {
        [DefaultCommand]
        public void MapCellFree(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sMapName = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sMapName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            NativeCommandFailure.Report(PlayObject, "MapCellFree",
                "原版 ownmap 点状态模型尚未移植，未清理地图单元格。");
        }
    }
}
