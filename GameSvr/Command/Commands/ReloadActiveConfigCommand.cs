using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>@ReloadactiveConfig — native dispatch 727 @0x0062AD90 → sub_6198B0.</summary>
    [GameCommand("ReloadactiveConfig", "重载玩家临时信用分配置", "", 2)]
    public class ReloadActiveConfigCommand : BaseCommond
    {
        private static readonly string PlayerActivePointFile = Path.Combine(
            M2Share.sRootPath, "Share", "EngineConfig",
            "\u4fe1\u7528\u5206\u7ba1\u7406", "PlayerActivePoint.xml");

        [DefaultCommand]
        public void ReloadActiveConfig(string[] @params, TPlayObject playObject)
        {
            if (NativeActivityPointManager.TryLoad(PlayerActivePointFile,
                    out var manager, out var error))
            {
                M2Share.ActivityPointManager = manager;
                playObject.SysMsg("重载成功", MsgColor.Green, MsgType.Hint);
                return;
            }

            M2Share.ActivityPointManager = null;
            playObject.SysMsg($"重载失败: {error}", MsgColor.Red, MsgType.Hint);
        }
    }
}
