using GameSvr.CommandSystem;
using GameSvr.Services;

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
            NativeWhitelistReloadClient.SendRequest(PlayObject);
        }
    }
}
