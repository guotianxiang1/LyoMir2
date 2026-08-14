using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("LoadValidFunc", "加载有效功能配置", "", 4)]
    public class LoadValidFuncCommand : BaseCommond
    {
        [DefaultCommand]
        public void LoadValidFunc(TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;
            NativeGmSystemCommands.ValidFuncReloadOk = true;
            NativeAntiCheatHostRuntime.ValidateTaskListDirectory(M2Share.g_Config.sEnvirDir,
                out _, out _);
            var seized = 0;
            foreach (var player in M2Share.UserEngine.PlayObjects)
                seized += NativeAntiCheatHostRuntime.SeizeIllegalBagItems(player);
            PlayObject.SysMsg("validScriptFunc 已加载，收缴非法物品 " + seized + " 件。",
                MsgColor.Yellow, MsgType.Hint);
        }
    }
}
