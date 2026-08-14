using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadWhiteList", "重新加载白名单", "", 4)]
    public class ReloadWhiteListCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadWhiteList(TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;
            NativeAntiCheatHostRuntime.ReloadGmWhiteList(M2Share.g_Config.sEnvirDir);
            PlayObject.SysMsg("WhiteList.txt 已重新加载。", MsgColor.Yellow, MsgType.Hint);
        }
    }
}
