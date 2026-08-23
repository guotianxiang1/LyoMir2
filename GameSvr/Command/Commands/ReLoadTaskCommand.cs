using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>Restores native dispatcher case 234 (0x00625DEF).</summary>
    [GameCommand("ReLoadTask", "重载任务脚本 (@LogonQuest)", "", 4)]
    public sealed class ReLoadTaskCommand : BaseCommond
    {
        internal const string NativeSuffix = " task Is Reload";

        [DefaultCommand]
        public void ReLoadTask(TPlayObject player)
        {
            if (player == null || M2Share.PasEngine == null)
                return;

            var count = M2Share.PasEngine.ReloadTaskScripts();
            player.SysMsg(count + NativeSuffix, MsgColor.Green, MsgType.Hint);
        }
    }
}
