using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("Shutup", "将指定人物禁言", M2Share.g_sGameCommandShutupHelpMsg, 2)]
    public class ShutupCommand : BaseCommond
    {
        [DefaultCommand]
        public void Shutup(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumanName = @Params.Length > 0 ? @Params[0] : "";
            var sTime = @Params.Length > 1 ? @Params[1] : "";
            if (sTime == "" || string.IsNullOrEmpty(sHumanName) || sHumanName[0] == '?')
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandParamUnKnow, this.GameCommand.Name, M2Share.g_sGameCommandShutupHelpMsg), MsgColor.Red, MsgType.Hint);
                return;
            }
            var dwTime = (uint)HUtil32.Str_ToInt(sTime, 5);
            HUtil32.EnterCriticalSection(M2Share.g_DenySayMsgList);
            try
            {
                M2Share.g_DenySayMsgList[sHumanName] =
                    (long)HUtil32.GetTickCount() + dwTime * 60_000L;
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.g_DenySayMsgList);
            }
            PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandShutupHumanMsg, sHumanName, dwTime), MsgColor.Red, MsgType.Hint);
        }
    }
}
