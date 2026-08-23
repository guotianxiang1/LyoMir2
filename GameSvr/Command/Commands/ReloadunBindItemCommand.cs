using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadunBindItem", "重新加载解绑物品配置", "", 4)]
    public sealed class ReloadunBindItemCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadunBindItem(TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var loaded = NativeUnbindItemConfig.Shared.TryReload(
                out var sectionCount, out _);
            var outcome = NativeGmItemExtraReloads.ReloadUnBindItem(loaded,
                sectionCount);
            PlayObject.SysMsg(outcome.Message, MsgColor.Green, MsgType.Hint);
        }
    }
}
