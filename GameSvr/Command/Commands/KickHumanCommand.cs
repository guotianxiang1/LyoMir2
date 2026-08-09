using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("KickHuman", "剔除指定玩家下线", M2Share.g_sGameCommandHumanLocalHelpMsg, 2)]
    public class KickHumanCommand : BaseCommond
    {
        [DefaultCommand]
        public void KickHuman(string[] @params, TPlayObject PlayObject)
        {
            if (@params == null)
            {
                return;
            }
            var sHumName = @params.Length > 0 ? @params[0] : "";
            if (sHumName == "")
            {
                return;
            }
            var m_PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
            if (m_PlayObject != null)
            {
                m_PlayObject.m_boKickFlag = true;
                m_PlayObject.m_boEmergencyClose = true;
                
                
            }
            else
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumName), MsgColor.Red, MsgType.Hint);
            }
        }
    }
}