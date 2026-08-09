using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// The original M2 has no safe live map replacement path. Map changes require a restart.
    /// </summary>
    [GameCommand("ReloadMap", "重新加载地图数据", 10)]
    public class ReloadMapCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadMap(TPlayObject PlayObject)
        {
            PlayObject.SysMsg("原版不支持运行时重载地图，请重启 M2。", MsgColor.Red, MsgType.Hint);
        }
    }
}
