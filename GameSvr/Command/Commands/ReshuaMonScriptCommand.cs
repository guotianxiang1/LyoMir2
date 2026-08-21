using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("reshuaMonScript", "重新加载怪物脚本", "", 5)]
    public sealed class ReshuaMonScriptCommand : BaseCommond
    {
        internal const string NativeStartMessage = "开始刷新怪物脚本";
        internal const string NativeEndMessage = "刷新怪物脚本结束";

        [DefaultCommand]
        public void ReshuaMonScript(TPlayObject player)
        {
            if (player == null) return;

            player.SysMsg(NativeStartMessage, MsgColor.Green, MsgType.Hint);
            M2Share.PasEngine?.ReloadActiveMonsterScripts();
            player.SysMsg(NativeEndMessage, MsgColor.Green, MsgType.Hint);
        }
    }
}
