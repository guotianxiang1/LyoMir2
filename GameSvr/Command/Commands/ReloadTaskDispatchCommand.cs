using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Restores native dispatcher case 459 (0x00628C39).
    /// </summary>
    [GameCommand("reloadTaskDispatch", "重载任务发布脚本", "", 4)]
    public sealed class ReloadTaskDispatchCommand : BaseCommond
    {
        internal const string NativeCompletionMessage = "重载任务发布脚本结束";

        [DefaultCommand]
        public void ReloadTaskDispatch(TPlayObject player)
        {
            if (player == null)
                return;

            M2Share.PasEngine?.ReloadTaskDispatch();
            player.SysMsg(NativeCompletionMessage, MsgColor.Green,
                MsgType.Hint);
        }
    }
}
