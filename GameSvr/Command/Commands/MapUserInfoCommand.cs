using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to show player count on the current or specified map.
    /// Usage: @MapUserInfo [MapName]
    /// If no map name is given, shows count on the GM's current map.
    /// </summary>
    [GameCommand("MapUserInfo", "查看地图玩家数量信息", "地图名称(可选)", 3)]
    public class MapUserInfoCommand : BaseCommond
    {
        [DefaultCommand]
        public void MapUserInfo(string[] @Params, TPlayObject PlayObject)
        {
            var sMapName = (@Params != null && @Params.Length > 0) ? @Params[0] : "";
            if (string.IsNullOrEmpty(sMapName))
            {
                sMapName = PlayObject.m_sMapName;
            }
            var Envir = M2Share.MapManager.FindMap(sMapName);
            if (Envir == null)
            {
                PlayObject.SysMsg($"地图 [{sMapName}] 不存在。", MsgColor.Red, MsgType.Hint);
                return;
            }
            var nCount = M2Share.UserEngine.GetMapHuman(sMapName);
            PlayObject.SysMsg($"地图 [{sMapName}] ({Envir.sMapDesc}) 当前玩家数量: {nCount}", MsgColor.Green, MsgType.Hint);
        }
    }
}
