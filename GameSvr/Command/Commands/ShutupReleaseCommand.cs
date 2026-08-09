using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ShutupRelease", "恢复禁言", M2Share.g_sGameCommandShutupReleaseHelpMsg, 2)]
    public class ShutupReleaseCommand : BaseCommond
    {
        [DefaultCommand]
        public void ShutupRelease(string[] @params, TPlayObject PlayObject)
        {
            if (@params == null)
            {
                return;
            }
            var sHumanName = @params.Length > 0 ? @params[0] : "";
            if (string.IsNullOrEmpty(sHumanName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            HUtil32.EnterCriticalSection(M2Share.g_DenySayMsgList);
            try
            {
                if (M2Share.g_DenySayMsgList.TryRemove(sHumanName, out _))
                {
                    PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandShutupReleaseHumanCanSendMsg,
                        sHumanName), MsgColor.Green, MsgType.Hint);
                }
                else
                {
                    PlayObject.SysMsg($"{sHumanName} 不在禁言列表中。", MsgColor.Red, MsgType.Hint);
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.g_DenySayMsgList);
            }
        }
    }
}
