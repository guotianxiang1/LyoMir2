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
            // Native SysMsg receives the packed WORD 0xFFDB directly.  The
            // regular MsgColor enum maps through configurable foreground and
            // background bytes, which would change the wire colour.
            PlayObject.SendMsg(PlayObject, Grobal2.RM_SYSMESSAGE, 0,
                NativeGmItemExtraCommands.ColorInfo & 0xFF,
                (NativeGmItemExtraCommands.ColorInfo >> 8) & 0xFF, 0,
                outcome.Message);
        }
    }
}
