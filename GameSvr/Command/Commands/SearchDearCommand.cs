using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("SearchDear", "此命令用于查询配偶当前所在位置", 0)]
    public class SearchDearCommand : BaseCommond
    {
        [DefaultCommand]
        public void SearchDear(TPlayObject PlayObject)
        {
            if (PlayObject.m_sDearName == "")
            {
                
                PlayObject.SysMsg(M2Share.g_sYouAreNotMarryedMsg, MsgColor.Red, MsgType.Hint);
                return;
            }

            if (PlayObject.m_DearHuman == null)
            {
                if (PlayObject.m_btGender == 0)
                {
                    
                    PlayObject.SysMsg(M2Share.g_sYourWifeNotOnlineMsg, MsgColor.Red, MsgType.Hint);
                }
                else
                {
                    
                    PlayObject.SysMsg(M2Share.g_sYourHusbandNotOnlineMsg, MsgColor.Red, MsgType.Hint);
                }

                return;
            }

            if (PlayObject.m_btGender == 0)
            {
                
                PlayObject.SysMsg(M2Share.g_sYourWifeNowLocateMsg, MsgColor.Green, MsgType.Hint);
                PlayObject.SysMsg(PlayObject.m_DearHuman.m_sCharName + ' ' + PlayObject.m_DearHuman.m_PEnvir.sMapDesc +
                                  '(' + PlayObject.m_DearHuman.m_nCurrX + ':'
                                  + PlayObject.m_DearHuman.m_nCurrY + ')', MsgColor.Green, MsgType.Hint);

                
                PlayObject.m_DearHuman.SysMsg(M2Share.g_sYourHusbandSearchLocateMsg, MsgColor.Green, MsgType.Hint);
                PlayObject.m_DearHuman.SysMsg(PlayObject.m_sCharName + ' ' + PlayObject.m_PEnvir.sMapDesc + '(' +
                                              PlayObject.m_nCurrX + ':'
                                              + PlayObject.m_nCurrY + ')', MsgColor.Green, MsgType.Hint);
            }
            else
            {
                
                PlayObject.SysMsg(M2Share.g_sYourHusbandNowLocateMsg, MsgColor.Red, MsgType.Hint);
                PlayObject.SysMsg(PlayObject.m_DearHuman.m_sCharName + ' ' + PlayObject.m_DearHuman.m_PEnvir.sMapDesc +
                                  '(' + PlayObject.m_DearHuman.m_nCurrX + ':'
                                  + PlayObject.m_DearHuman.m_nCurrY + ')', MsgColor.Green, MsgType.Hint);

                
                PlayObject.m_DearHuman.SysMsg(M2Share.g_sYourWifeSearchLocateMsg, MsgColor.Green, MsgType.Hint);
                PlayObject.m_DearHuman.SysMsg(PlayObject.m_sCharName + ' ' + PlayObject.m_PEnvir.sMapDesc + '(' +
                                              PlayObject.m_nCurrX + ':'
                                              + PlayObject.m_nCurrY + ')', MsgColor.Green, MsgType.Hint);
            }
        }
    }
}