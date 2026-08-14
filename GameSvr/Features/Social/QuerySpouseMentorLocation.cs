using SystemModule;

namespace GameSvr.Features.Social
{
    /// <summary>
    /// Provides location query functionality for spouse and mentor relationships.
    /// Implements the native SearchDear and SearchMaster command logic.
    /// </summary>
    public static class QuerySpouseMentorLocation
    {
        /// <summary>
        /// Query spouse location and notify both parties.
        /// Native: SearchDearCommand implementation
        /// </summary>
        public static void QuerySpouseLocation(TPlayObject player)
        {
            // Check if player is married
            if (player.m_sDearName == "")
            {
                player.SysMsg(M2Share.g_sYouAreNotMarryedMsg, MsgColor.Red, MsgType.Hint);
                return;
            }

            // Check if spouse is online
            if (player.m_DearHuman == null)
            {
                if (player.m_btGender == 0)
                {
                    player.SysMsg(M2Share.g_sYourWifeNotOnlineMsg, MsgColor.Red, MsgType.Hint);
                }
                else
                {
                    player.SysMsg(M2Share.g_sYourHusbandNotOnlineMsg, MsgColor.Red, MsgType.Hint);
                }
                return;
            }

            // Send location info based on gender
            if (player.m_btGender == 0)
            {
                // Male player querying wife
                player.SysMsg(M2Share.g_sYourWifeNowLocateMsg, MsgColor.Green, MsgType.Hint);
                player.SysMsg(player.m_DearHuman.m_sCharName + ' ' + player.m_DearHuman.m_PEnvir.sMapDesc +
                              '(' + player.m_DearHuman.m_nCurrX + ':' + player.m_DearHuman.m_nCurrY + ')',
                              MsgColor.Green, MsgType.Hint);

                // Notify spouse
                player.m_DearHuman.SysMsg(M2Share.g_sYourHusbandSearchLocateMsg, MsgColor.Green, MsgType.Hint);
                player.m_DearHuman.SysMsg(player.m_sCharName + ' ' + player.m_PEnvir.sMapDesc +
                                          '(' + player.m_nCurrX + ':' + player.m_nCurrY + ')',
                                          MsgColor.Green, MsgType.Hint);
            }
            else
            {
                // Female player querying husband
                player.SysMsg(M2Share.g_sYourHusbandNowLocateMsg, MsgColor.Red, MsgType.Hint);
                player.SysMsg(player.m_DearHuman.m_sCharName + ' ' + player.m_DearHuman.m_PEnvir.sMapDesc +
                              '(' + player.m_DearHuman.m_nCurrX + ':' + player.m_DearHuman.m_nCurrY + ')',
                              MsgColor.Green, MsgType.Hint);

                // Notify spouse
                player.m_DearHuman.SysMsg(M2Share.g_sYourWifeSearchLocateMsg, MsgColor.Green, MsgType.Hint);
                player.m_DearHuman.SysMsg(player.m_sCharName + ' ' + player.m_PEnvir.sMapDesc +
                                          '(' + player.m_nCurrX + ':' + player.m_nCurrY + ')',
                                          MsgColor.Green, MsgType.Hint);
            }
        }

        /// <summary>
        /// Query master/apprentice location and notify both parties.
        /// Native: SearchMasterCommand implementation
        /// </summary>
        public static void QueryMasterLocation(TPlayObject player)
        {
            // Check if player has master/apprentice relationship
            if (player.m_sMasterName == "")
            {
                player.SysMsg(M2Share.g_sYouAreNotMasterMsg, MsgColor.Red, MsgType.Hint);
                return;
            }

            if (player.m_boMaster)
            {
                // Player is master, query apprentices
                if (player.m_MasterList.Count <= 0)
                {
                    player.SysMsg(M2Share.g_sYourMasterListNotOnlineMsg, MsgColor.Red, MsgType.Hint);
                    return;
                }

                player.SysMsg(M2Share.g_sYourMasterListNowLocateMsg, MsgColor.Green, MsgType.Hint);

                for (var i = 0; i < player.m_MasterList.Count; i++)
                {
                    TPlayObject apprentice = player.m_MasterList[i];
                    player.SysMsg(apprentice.m_sCharName + " " + apprentice.m_PEnvir.sMapDesc +
                                  "(" + apprentice.m_nCurrX + ":" + apprentice.m_nCurrY + ")",
                                  MsgColor.Green, MsgType.Hint);

                    // Notify apprentice
                    apprentice.SysMsg(M2Share.g_sYourMasterSearchLocateMsg, MsgColor.Green, MsgType.Hint);
                    apprentice.SysMsg(player.m_sCharName + " " + player.m_PEnvir.sMapDesc +
                                      "(" + player.m_nCurrX + ":" + player.m_nCurrY + ")",
                                      MsgColor.Green, MsgType.Hint);
                }
            }
            else
            {
                // Player is apprentice, query master
                if (player.m_MasterHuman == null)
                {
                    player.SysMsg(M2Share.g_sYourMasterNotOnlineMsg, MsgColor.Red, MsgType.Hint);
                    return;
                }

                player.SysMsg(M2Share.g_sYourMasterNowLocateMsg, MsgColor.Red, MsgType.Hint);
                player.SysMsg(player.m_MasterHuman.m_sCharName + " " + player.m_MasterHuman.m_PEnvir.sMapDesc +
                              "(" + player.m_MasterHuman.m_nCurrX + ":" + player.m_MasterHuman.m_nCurrY + ")",
                              MsgColor.Green, MsgType.Hint);

                // Notify master
                player.m_MasterHuman.SysMsg(M2Share.g_sYourMasterListSearchLocateMsg, MsgColor.Green, MsgType.Hint);
                player.m_MasterHuman.SysMsg(player.m_sCharName + " " + player.m_PEnvir.sMapDesc +
                                            "(" + player.m_nCurrX + ":" + player.m_nCurrY + ")",
                                            MsgColor.Green, MsgType.Hint);
            }
        }
    }
}
