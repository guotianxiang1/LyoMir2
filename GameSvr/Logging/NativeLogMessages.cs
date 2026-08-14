using static SystemModule.HUtil32;

namespace GameSvr.Logging
{
    /// <summary>
    /// Native log messages - static methods corresponding to original Delphi log outputs.
    /// Each method calls M2Share.MainOutMessage to output the corresponding message.
    /// </summary>
    public static class NativeLogMessages
    {
        #region Initialization & Startup

        public static void LogInitializingNpcScripts()
        {
            M2Share.MainOutMessage("正在初始化NPC脚本...");
        }

        public static void LogNpcScriptsInitialized()
        {
            M2Share.MainOutMessage("初始化NPC脚本完成...");
        }

        public static void LogMonsterSpawnMatching(int matchedCount, int unmatchedCount)
        {
            M2Share.MainOutMessage($"怪物刷新配置匹配完成: 可刷({matchedCount}) 未匹配({unmatchedCount})...");
        }

        public static void LogUnmatchedMonsterSamples(string samples)
        {
            M2Share.MainOutMessage("未匹配怪物示例: " + samples);
        }

        #endregion

        #region NPC & Merchant Initialization

        public static void LogMerchantInitializeFail(string charName, string mapName)
        {
            M2Share.MainOutMessage("Merchant Initalize fail..." + charName + ' ' + mapName);
        }

        public static void LogMerchantInitializeFailEnvirNil(string charName)
        {
            M2Share.MainOutMessage(charName + " - Merchant Initalize fail... (m.PEnvir=nil)");
        }

        public static void LogNpcInitializeFail(string charName)
        {
            M2Share.MainOutMessage(charName + " Npc Initalize fail... ");
        }

        public static void LogNpcInitializeFailEnvirNil(string charName)
        {
            M2Share.MainOutMessage(charName + " Npc Initalize fail... (npc.PEnvir=nil) ");
        }

        #endregion

        #region User Engine & Player Management

        public static void LogChangeServerFail1(int serverIndex, int playerServerIndex, string mapName)
        {
            M2Share.MainOutMessage(string.Format("chg-server-fail-1 [{0}] -> [{1}] [{2}]", serverIndex, playerServerIndex, mapName));
        }

        public static void LogChangeServerFail2(int serverIndex, int playerServerIndex, string mapName)
        {
            M2Share.MainOutMessage(string.Format("chg-server-fail-2 [{0}] -> [{1}] [{2}]", serverIndex, playerServerIndex, mapName));
        }

        public static void LogChangeServerFail3(int serverIndex, int playerServerIndex, string mapName)
        {
            M2Share.MainOutMessage(string.Format("chg-server-fail-3 [{0}] -> [{1}] [{2}]", serverIndex, playerServerIndex, mapName));
        }

        public static void LogChangeServerFail4(int serverIndex, int playerServerIndex, string mapName)
        {
            M2Share.MainOutMessage(string.Format("chg-server-fail-4 [{0}] -> [{1}] [{2}]", serverIndex, playerServerIndex, mapName));
        }

        public static void LogErrorEnvirIsNil()
        {
            M2Share.MainOutMessage("[Error] PlayObject.PEnvir = nil");
        }

        public static void LogOpenMakeNil(string account, string chrName, int gateIdx, int socket)
        {
            M2Share.MainOutMessage($"[OpenMakeNil] account={account} chr={chrName} gate={gateIdx} socket={socket}");
        }

        public static void LogOpenSkipOnline(string account, string chrName, int gateIdx, int socket)
        {
            M2Share.MainOutMessage($"[OpenSkipOnline] account={account} chr={chrName} gate={gateIdx} socket={socket}");
        }

        public static void LogOnlinePlayerCount(int count)
        {
            M2Share.MainOutMessage("在线人数: " + count);
        }

        #endregion

        #region Exception Messages

        public static void LogExceptionUserEngineMakeNewHuman()
        {
            M2Share.ErrorMessage("[Exception] TUserEngine::MakeNewHuman");
        }

        public static void LogExceptionUserEngineProcessHumans1()
        {
            M2Share.ErrorMessage("[Exception] TUserEngine::ProcessHumans -> Ready, Save, Load...");
        }

        public static void LogExceptionUserEngineProcessHumans3()
        {
            M2Share.MainOutMessage("[Exception] TUserEngine::ProcessHumans ClosePlayer.Delete");
        }

        public static void LogExceptionUserEngineProcessHumans8()
        {
            M2Share.MainOutMessage("[Exception] TUserEngine::ProcessHumans");
        }

        public static void LogExceptionUserEngineProcessMerchants()
        {
            M2Share.ErrorMessage("[Exception] TUserEngine::ProcessMerchants");
        }

        public static void LogExceptionUserEngineRun()
        {
            M2Share.ErrorMessage("[Exception] TUserEngine::Run");
        }

        public static void LogExceptionUserEngineRegenMonsters()
        {
            M2Share.ErrorMessage("[Exception] TUserEngine::RegenMonsters");
        }

        public static void LogExceptionBaseObjectRun(int code)
        {
            M2Share.ErrorMessage($"[Exception] TBaseObject::Run {code}");
        }

        public static void LogExceptionBaseObjectDie(int code)
        {
            M2Share.ErrorMessage($"[Exception] TBaseObject::Die {code}");
        }

        public static void LogExceptionBaseObjectWalkTo()
        {
            M2Share.ErrorMessage("[Exception] TBaseObject::WalkTo");
        }

        public static void LogExceptionBaseObjectRunTo()
        {
            M2Share.ErrorMessage("[Exception] TBaseObject::RunTo");
        }

        public static void LogExceptionBaseObjectScatterBagItems()
        {
            M2Share.ErrorMessage("[Exception] TBaseObject::ScatterBagItems");
        }

        public static void LogExceptionBaseObjectDropUseItems()
        {
            M2Share.ErrorMessage("[Exception] TBaseObject::DropUseItems");
        }

        public static void LogExceptionBaseObjectOperate()
        {
            M2Share.ErrorMessage("[Exception] TBaseObject::Operate ");
        }

        public static void LogExceptionBaseObjectAttackDir()
        {
            M2Share.ErrorMessage("[Exception] TBaseObject::AttackDir");
        }

        public static void LogExceptionBaseObjectKillFunc()
        {
            M2Share.ErrorMessage("[Exception] TBaseObject::KillFunc");
        }

        public static void LogExceptionBaseObjectUseLamp()
        {
            M2Share.ErrorMessage("[Exception] TBaseObject::UseLamp");
        }

        public static void LogExceptionBaseObjectGetMapBaseObjects()
        {
            M2Share.ErrorMessage("[Exception] TBaseObject::GetMapBaseObjects");
        }

        public static void LogExceptionBaseObjectSendRefMsg(string name)
        {
            M2Share.ErrorMessage($"[Exception] TBaseObject::SendRefMsg Name = {name}");
        }

        public static void LogExceptionBaseObjectWalk(string name, string mapName, int x, int y)
        {
            M2Share.ErrorMessage(string.Format("[Exception] TBaseObject::Walk {0} {1} {2}:{3}", name, mapName, x, y));
        }

        public static void LogExceptionBaseObjectEnterAnotherMap()
        {
            M2Share.ErrorMessage("[Exception] TBaseObject::EnterAnotherMap");
        }

        public static void LogExceptionBaseObjectSearchViewRange(string charName, string mapName, int x, int y)
        {
            M2Share.MainOutMessage(string.Format("[Exception] TBaseObject::SearchViewRange 1-{0} {1} {2} {3}", charName, mapName, x, y));
        }

        public static void LogExceptionPlayObjectRun(int code)
        {
            M2Share.ErrorMessage($"[Exception] TPlayObject::Run {code}");
        }

        public static void LogExceptionPlayObjectHorseRunTo()
        {
            M2Share.ErrorMessage("[Exception] TPlayObject::HorseRunTo");
        }

        public static void LogExceptionPlayObjectGainExp()
        {
            M2Share.ErrorMessage("[Exception] TPlayObject::GainExp");
        }

        public static void LogExceptionPlayObjectDoSpell(int magId, int x, int y)
        {
            M2Share.ErrorMessage(string.Format("[Exception] TPlayObject.DoSpell MagID:{0} X:{1} Y:{2}", magId, x, y));
        }

        public static void LogExceptionPlayObjectProcessSayMsg(string msg)
        {
            M2Share.ErrorMessage($"[Exception] TPlayObject.ProcessSayMsg Msg = {msg}");
        }

        public static void LogExceptionPlayObjectProcessUserLineMsg(string msg)
        {
            M2Share.ErrorMessage($"[Exception] TPlayObject::ProcessUserLineMsg Msg = {msg}");
        }

        public static void LogExceptionPlayObjectClientHitXY()
        {
            M2Share.ErrorMessage("[Exception] TPlayObject::ClientHitXY");
        }

        public static void LogExceptionPlayObjectRunNotice()
        {
            M2Share.ErrorMessage("[Exception] TPlayObject::RunNotice");
        }

        public static void LogExceptionPlayObjectUserLogon()
        {
            M2Share.ErrorMessage("[Exception] TPlayObject::UserLogon");
        }

        public static void LogExceptionPlayObjectGetShowName()
        {
            M2Share.ErrorMessage("[Exception] TPlayObject::GetShowName");
        }

        public static void LogExceptionPlayObjectMakeGhost()
        {
            M2Share.ErrorMessage("[Exception] TPlayObject::MakeGhost");
        }

        public static void LogExceptionPlayObjectScatterBagItems()
        {
            M2Share.ErrorMessage("[Exception] TPlayObject::ScatterBagItems");
        }

        public static void LogExceptionPlayObjectDropUseItems()
        {
            M2Share.ErrorMessage("[Exception] TPlayObject::DropUseItems");
        }

        public static void LogExceptionPlayObjectClientGuildAlly()
        {
            M2Share.ErrorMessage("[Exception] TPlayObject::ClientGuildAlly");
        }

        #endregion

        #region Environment & Map

        public static void LogExceptionEnvirnomentAddToMap(string message)
        {
            M2Share.ErrorMessage("[Exception] TEnvirnoment::AddToMap " + message);
        }

        public static void LogExceptionEnvirnomentMoveToMovingObject()
        {
            M2Share.ErrorMessage("[Exception] TEnvirnoment::MoveToMovingObject");
        }

        public static void LogExceptionEnvirnomentDeleteFromMap(int cellType, string message)
        {
            M2Share.MainOutMessage($"[Exception] TEnvirnoment::DeleteFromMap -> Except {cellType} ** {message}");
        }

        public static void LogExceptionEnvirnomentAddToMapMineEvent(string message)
        {
            M2Share.ErrorMessage("[Exception] TEnvirnoment::AddToMapMineEvent " + message);
        }

        public static void LogExceptionEnvirnomentVerifyMapTime(string message)
        {
            M2Share.ErrorMessage("[Exception] TEnvirnoment::VerifyMapTime " + message);
        }

        #endregion

        #region Gate & Network

        public static void LogExceptionRunSocketDoClientCertification(string message)
        {
            M2Share.ErrorMessage("[Exception] TRunSocket::DoClientCertification " + message);
        }

        public static void LogExceptionRunSocketExecGateMsg(string message)
        {
            M2Share.ErrorMessage("[Exception] TRunSocket::ExecGateMsg " + message);
        }

        public static void LogExceptionRunSocketKickUser()
        {
            M2Share.ErrorMessage("[Exception] TRunSocket::KickUser");
        }

        #endregion

        #region Services & External

        public static void LogExceptionFrmSrvMsgRun()
        {
            M2Share.ErrorMessage("[Exception] TFrmSrvMsg::Run");
        }

        public static void LogExceptionFrmSrvMsgDecodeSocStr()
        {
            M2Share.ErrorMessage("[Exception] TFrmSrvMsg::DecodeSocStr");
        }

        public static void LogExceptionFrmSrvMsgGetUserServerChange()
        {
            M2Share.ErrorMessage("[Exception] TFrmSrvMsg::MsgGetUserServerChange");
        }

        public static void LogExceptionFrmIdSocDecodeSocStr()
        {
            M2Share.ErrorMessage("[Exception] TFrmIdSoc::DecodeSocStr");
        }

        public static void LogExceptionFrmIdSocGetPasswdSuccess()
        {
            M2Share.ErrorMessage("[Exception] TFrmIdSoc::GetPasswdSuccess");
        }

        public static void LogExceptionFrmIdSocGetCancelAdmission()
        {
            M2Share.ErrorMessage("[Exception] TFrmIdSoc::GetCancelAdmission");
        }

        public static void LogExceptionFrmIdSocDelSession()
        {
            M2Share.ErrorMessage("[Exception] FrmIdSoc::DelSession");
        }

        public static void LogExceptionFrontEngineExecute()
        {
            M2Share.ErrorMessage("[Exception] TFrontEngine::Execute");
        }

        #endregion

        #region Merchant & NPC

        public static void LogExceptionMerchantRefillGoods(string charName, int x, int y, string message, int code)
        {
            M2Share.MainOutMessage(string.Format("[Exception] TMerchant::RefillGoods {0}/{1}:{2} [{3}] Code:{4}",
                charName, x, y, message, code));
        }

        public static void LogExceptionMerchantUserSelect(string data)
        {
            M2Share.MainOutMessage($"[Exception] TMerchant::UserSelect... Data: {data}");
        }

        public static void LogExceptionMerchantClearData()
        {
            M2Share.ErrorMessage("[Exception] TMerchant::ClearData");
        }

        public static void LogExceptionGuildOfficialUserSelect()
        {
            M2Share.ErrorMessage("[Exception] TGuildOfficial::UserSelect... ");
        }

        public static void LogExceptionCastleOfficialUserSelect()
        {
            M2Share.ErrorMessage("[Exception] TCastleManager::UserSelect... ");
        }

        #endregion

        #region Castle & Guild

        public static void LogExceptionUserCastleRun()
        {
            M2Share.ErrorMessage("[Exception] TUserCastle::Run");
        }

        #endregion

        #region Robot AI

        public static void LogExceptionAIPlayObjectProcessSayMsg(string msg)
        {
            M2Share.MainOutMessage(string.Format("TAIPlayObject.ProcessSayMsg Msg:{0}", msg));
        }

        public static void LogExceptionAIPlayObjectSearchViewRange(string charName, string mapName, int x, int y)
        {
            M2Share.MainOutMessage(string.Format("TAIPlayObject::SearchViewRange 1-{0} {1} {2} {3}",
                charName, mapName, x, y));
        }

        public static void LogAIPlayObjectActThink(string charName, int code)
        {
            M2Share.MainOutMessage(string.Format("TAIPlayObject::ActThink Name:{0} Code:{1} ", charName, code));
        }

        public static void LogAIPlayObjectAutoSpell(int magId, int x, int y)
        {
            M2Share.MainOutMessage(string.Format("TAIPlayObject.AutoSpell MagID:{0} X:{1} Y:{2}", magId, x, y));
        }

        #endregion

        #region Speed Violations

        public static void LogBunOverSpeed(string charName, int delayTime, int msgCount)
        {
            M2Share.MainOutMessage(string.Format(M2Share.g_sBunOverSpeed, charName, delayTime, msgCount));
        }

        public static void LogHitOverSpeed(string charName, int delayTime, int msgCount)
        {
            M2Share.MainOutMessage(string.Format(M2Share.g_sHitOverSpeed, charName, delayTime, msgCount));
        }

        public static void LogSpellOverSpeed(string charName, int delayTime, int msgCount)
        {
            M2Share.MainOutMessage(string.Format(M2Share.g_sSpellOverSpeed, charName, delayTime, msgCount));
        }

        #endregion
    }
}
