using SystemModule;
using GameSvr.PasEngine;
using GameSvr.Plugins;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private void SendMoveActionFail()
        {
            SendDefMessage(Grobal2.SM_ACT_FAIL, 0, m_nCurrX, m_nCurrY, m_btDirection, "");
        }

        private void SendNativeAbilityPacket()
        {
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ABILITY, m_nGold,
                m_btJob, 0, 0);
            SendSocket(m_DefMsg, GetMobileAbility());
        }

        private static ClientPacket BuildMasterRelationPacket(
            TProcessMessage processMessage)
        {
            return Grobal2.MakeDefaultMsg(Grobal2.SM_MASTERRELATION,
                processMessage.nParam1, processMessage.wParam,
                HUtil32.LoWord(processMessage.nParam2),
                HUtil32.LoWord(processMessage.nParam3));
        }

        private static byte[] BuildMasterRelationBody(
            TProcessMessage processMessage)
        {
            return HUtil32.GetBytes(
                (processMessage.sMsg ?? string.Empty) + "\0");
        }

        private void RejectUnavailableStallRequest(short responseIdent = 0, int responseCode = 0)
        {
            SysMsg("摆摊功能当前不可用。", MsgColor.Red, MsgType.Hint);
            if (responseIdent != 0 && responseCode != 0)
                SendDefMessage(responseIdent, responseCode, 0, 0, 0, "");
        }

        private static byte[] BuildMapNpcRecord(string name, int x, int y)
        {
            var record = new byte[20];
            var nameBytes = HUtil32.GetBytes(name ?? string.Empty);
            var nameLength = Math.Min(15, nameBytes.Length);
            record[0] = (byte)nameLength;
            Buffer.BlockCopy(nameBytes, 0, record, 1, nameLength);
            BitConverter.GetBytes(unchecked((ushort)x)).CopyTo(record, 16);
            BitConverter.GetBytes(unchecked((ushort)y)).CopyTo(record, 18);
            return record;
        }

        private static string GetLogonMapName(string mapName)
        {
            mapName ??= string.Empty;
            var separator = mapName.IndexOf('~');
            return separator >= 0 ? mapName[..separator] : mapName;
        }

        private void SendMapNpcList(TProcessMessage processMsg)
        {
            var environment = processMsg.nParam2 == 0
                ? m_PEnvir
                : M2Share.MapManager.FindMap((processMsg.sMsg ?? string.Empty).TrimEnd('\0'));
            var npcs = new List<NormNpc>();
            var objectIds = new HashSet<int>();

            if (environment != null && M2Share.UserEngine != null)
            {
                M2Share.UserEngine.SnapshotNpcRegistry(out var merchants,
                    out var questNpcs);
                foreach (var merchant in merchants)
                {
                    if (merchant?.m_PEnvir == environment
                        && !merchant.m_boGhost && !merchant.m_boIsHide
                        && objectIds.Add(merchant.ObjectId))
                        npcs.Add(merchant);
                }

                foreach (var npc in questNpcs)
                {
                    if (npc?.m_PEnvir == environment
                        && !npc.m_boGhost && !npc.m_boIsHide
                        && objectIds.Add(npc.ObjectId))
                        npcs.Add(npc);
                }
            }

            using var stream = new MemoryStream(npcs.Count * 20);
            foreach (var npc in npcs)
            {
                var record = BuildMapNpcRecord(npc.m_sCharName, npc.m_nCurrX, npc.m_nCurrY);
                stream.Write(record, 0, record.Length);
            }

            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_QUERY_MAP_NPC, 0, 0, npcs.Count, 0);
            SendSocket(m_DefMsg, stream.ToArray());
        }

        private byte[] BuildMobileNewStateBody(TBaseObject baseObject, string displayName)
        {
            return BuildMobileNewStateRecord(
                (uint)baseObject.GetFeature(this),
                baseObject,
                GetCharColor(baseObject),
                baseObject.GetMobileFeature(),
                baseObject.m_btJob,
                displayName);
        }

        private static byte[] BuildMobileNewStateRecord(
            uint feature,
            TBaseObject baseObject,
            byte nameColor,
            byte[] mobileFeature,
            byte job,
            string displayName)
        {
            var nameBytes = HUtil32.GetBytes(displayName);
            using var stream = new System.IO.MemoryStream(42 + nameBytes.Length);
            using var writer = new System.IO.BinaryWriter(stream);
            writer.Write((short)41);
            writer.Write(feature);
            baseObject.WriteBodyState(writer);
            writer.Write((uint)nameColor);
            writer.Write(0);
            writer.Write(mobileFeature);
            writer.Write(job);
            writer.Write(nameBytes);
            writer.Write((byte)0);
            return stream.ToArray();
        }

        private static byte[] BuildMobileStruckBody(int attackerId, bool magic, int hp, int maxHp, int mp, int maxMp)
        {
            using var stream = new System.IO.MemoryStream(32);
            using var writer = new System.IO.BinaryWriter(stream);
            writer.Write(0);
            writer.Write(0);
            writer.Write(attackerId);
            writer.Write(magic ? 1 : 0);
            writer.Write(hp);
            writer.Write(maxHp);
            writer.Write(mp);
            writer.Write(maxMp);
            return stream.ToArray();
        }

        private static byte[] BuildMobileActorStateBody(int feature, TBaseObject baseObject)
        {
            using var stream = new System.IO.MemoryStream(32);
            using var writer = new System.IO.BinaryWriter(stream);
            writer.Write(feature);
            baseObject.WriteBodyState(writer);
            writer.Write(baseObject.GetMobileFeature());
            writer.Write((ushort)0);
            return stream.ToArray();
        }

        private static byte[] BuildInstanceHealGaugeBody(int hp, int maxHp)
        {
            var body = new byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                body.AsSpan(0, 4), hp);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                body.AsSpan(4, 4), maxHp);
            return body;
        }

        private static byte[] GetQueuedPayloadBytes(TProcessMessage processMsg)
        {
            return processMsg.Payload as byte[] ?? Array.Empty<byte>();
        }

        private static byte[] BuildShowEventBody(Event mapEvent, int packedEventParam)
        {
            var isStall = mapEvent.m_nEventType == 41;
            var body = new byte[isStall ? 64 : 12];
            var elapsed = unchecked((uint)(HUtil32.GetTickCount() - mapEvent.OpenStartTick));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(0, 2), unchecked((ushort)HUtil32.HiWord(packedEventParam)));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(2, 2), unchecked((ushort)mapEvent.m_nEventParam));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(4, 4), elapsed);
            WriteEventShortString(body, 8, isStall ? 14 : 3,
                mapEvent.m_sEventOwnerName, mapEvent.m_EventOwnerNameBytes);
            if (isStall)
            {
                WriteEventShortString(body, 23, 30, mapEvent.m_sEventStallName);
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                    body.AsSpan(56, 8), mapEvent.m_lEventOwnerId);
            }
            return body;
        }

        private static void WriteEventShortString(byte[] body, int offset,
            int capacity, string value, byte[] rawBytes = null)
        {
            var bytes = rawBytes ??
                        HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            var count = Math.Min(bytes.Length, capacity);
            body[offset] = (byte)count;
            Buffer.BlockCopy(bytes, 0, body, offset + 1, count);
        }

        public override void Run()
        {
            int tObjCount;
            int nInteger;
            TProcessMessage ProcessMsg = null;
            const string sPayMentExpire = "您的帐户充值时间已到期!!!";
            const string sDisConnectMsg = "游戏被强行中断!!!";
            const string sExceptionMsg1 = "[Exception] TPlayObject::Run -> Operate 1";
            const string sExceptionMsg2 = "[Exception] TPlayObject::Run -> Operate 2 # %s Ident:%d Sender:%d wP:%d nP1:%d nP2:%d np3:%d Msg:%s";
            const string sExceptionMsg3 = "[Exception] TPlayObject::Run -> GetHighHuman";
            const string sExceptionMsg4 = "[Exception] TPlayObject::Run -> ClearObj";
            try
            {
                var currentTick = HUtil32.GetTickCount();
                RunNativeCattle();
                RunNativeYbCreditLoad(unchecked((uint)currentTick));
                RunSecHeroPracticeTimer(currentTick);
                RunNativeMagicTraining(currentTick);
                // 战神 sub_6B2D38 == TPlayer.Run (VMT 0x6AC8C8 slot +0x88, class
                // name resolved through the SelfPtr convention dword[vmt-0x4C]).
                // It calls the timed-buff decrement pass UNCONDITIONALLY at
                // 0x6B3B37, immediately after step marker 9 (0x6B3B2D
                // mov [ebp-0xc],9) and just before the m_DealCreat sweep at
                // 0x6B3B3F. That is the only call to sub_6CCBC4 in the whole image
                // and there is no dword reference to it, so it is not virtual.
                //
                // Native's slot is later in the pass than this line, but the callee
                // gates itself on its own latch (obj+0x720 vs 0x2710) and touches
                // only obj+0xBB8/0xBD0/0xBD4 plus two notifications, none of which
                // any earlier or later step in Run reads. So position within the
                // pass is not observable; cadence is (one Run iteration, 10s gate).
                TickNativeExpBuff(currentTick);
                M2Share.CreditCardService?.TrySaveDue(this, currentTick);
                // TRADE-48: 这段属于 Run，不是登出路径，不要外迁。判据在原生
                // sub_6B2D38 @0x6B2E76-0x6B2EB2，而 sub_6B2D38 就是 TPlayer VMT
                // 0x6AC8C8 槽 +0x88（每 tick）。同一函数、同一 [ebp-4] self 槽内，
                // 0x6B3B68-0x6B3B87 正是 TRADE-47 的 m_DealCreat 清扫；两者共用
                // 0x6B2D76 压入的外层 SEH 句柄 0x6B3D64，故同属一个函数体。
                // 真正的登出取消是另一条：0x6518C8 `call 0x6B2C7C`，而 sub_6B2C7C
                // 是 `mov edx,[eax+0x2AC]` / `mov [eax+0x230],edx` /
                // `E8 call 0x6C43C4` / `ret` 的直线代码，无条件取消。
                //
                // 原生判据（cancel 当且仅当 dealing 且三者之一成立）：
                //   0x6B2E79 cmp byte [eax+0x461],0  / je  0x6B2EB7  未在交易 → 跳过
                //   0x6B2E85 cmp dword [eax+0xBAC],0 / je  0x6B2EAF  对端为空 → 取消
                //   0x6B2E91 call 0x767E80 (前方对象)
                //   0x6B2E99 cmp eax,[edx+0xBAC]     / jne 0x6B2EAF  前方非对端 → 取消
                //   0x6B2EAA cmp eax,[ebp-4]         / jne 0x6B2EB7  对端非自己 → 不取消
                //   0x6B2EAF call 0x6C43C4
                if (m_boDealing)
                {
                    if (GetPoseCreate() != m_DealCreat || m_DealCreat == this || m_DealCreat == null)
                    {
                        DealCancel();
                    }
                }
                if (m_boExpire)
                {
                    SysMsg(sPayMentExpire, MsgColor.Red, MsgType.Hint);
                    SysMsg(sDisConnectMsg, MsgColor.Red, MsgType.Hint);
                    m_boEmergencyClose = true;
                    m_boExpire = false;
                }
                if (m_boFireHitSkill && (HUtil32.GetTickCount() - m_dwLatestFireHitTick) > 20 * 1000)
                {
                    m_boFireHitSkill = false;
                    SysMsg(M2Share.sSpiritsGone, MsgColor.Red, MsgType.Hint);
                    SendSocket("+UFIR");
                }
                if (m_boTwinHitSkill && (HUtil32.GetTickCount() - m_dwLatestTwinHitTick) > 60 * 1000)
                {
                    m_boTwinHitSkill = false;
                    SendSocket("+UTWN");
                }
                if (m_boSunSwordReady &&
                    unchecked(HUtil32.GetTickCount() - m_dwLatestSunSwordTick) > 10 * 1000)
                {
                    m_boSunSwordReady = false;
                    SendSocket(Grobal2.MakeDefaultMsg(
                        Grobal2.SM_SWORDHIT_ON, 1, 0, 0, 0));
                }
                if (m_boTimeRecall && HUtil32.GetTickCount() > m_dwTimeRecallTick) 
                {
                    m_boTimeRecall = false;
                    SpaceMove(m_sMoveMap, m_nMoveX, m_nMoveY, 0);
                }
                for (int i = 0; i < 20; i++) 
                {
                    if (AutoTimerStatus[i] > 500)
                    {
                        if ((HUtil32.GetTickCount() - AutoTimerTick[i]) > AutoTimerStatus[i])
                        {
                            if (M2Share.g_ManageNPC != null)
                            {
                                AutoTimerTick[i] = HUtil32.GetTickCount();
                                m_nScriptGotoCount = 0;
                                M2Share.g_ManageNPC.GotoLable(this, "@OnTimer" + i, false);
                            }
                        }
                    }
                }
                if (m_boTimeGoto && (HUtil32.GetTickCount() > m_dwTimeGotoTick)) 
                {
                    m_boTimeGoto = false;
                    if (m_TimeGotoNPC as Merchant != null)
                    {
                        (m_TimeGotoNPC as Merchant).GotoLable(this, m_sTimeGotoLable, false);
                    }
                }
                
                if (m_boOffLineFlag && HUtil32.GetTickCount() > m_dwKickOffLineTick)
                {
                    m_boOffLineFlag = false;
                    m_boSoftClose = true;
                }
                if (m_boDelayCall && (HUtil32.GetTickCount() - m_dwDelayCallTick) > m_nDelayCall)
                {
                    m_boDelayCall = false;
                    NormNpc normNpc = (Merchant)M2Share.UserEngine.FindMerchant(m_DelayCallNPC);
                    if (normNpc == null)
                    {
                        normNpc = (NormNpc)M2Share.UserEngine.FindNPC(m_DelayCallNPC);
                    }
                    if (normNpc != null)
                    {
                        normNpc.GotoLable(this, m_sDelayCallLabel, false);
                    }
                }
                if ((HUtil32.GetTickCount() - m_dwCheckDupObjTick) > 3000)
                {
                    m_dwCheckDupObjTick = HUtil32.GetTickCount();
                    GetStartPoint();
                    tObjCount = m_PEnvir.GetXYObjCount(m_nCurrX, m_nCurrY);
                    if (tObjCount >= 2)
                    {
                        if (!bo2F0)
                        {
                            bo2F0 = true;
                            m_dwDupObjTick = HUtil32.GetTickCount();
                        }
                    }
                    else
                    {
                        bo2F0 = false;
                    }
                    if ((tObjCount >= 3 && ((HUtil32.GetTickCount() - m_dwDupObjTick) > 3000) || tObjCount == 2
                        && ((HUtil32.GetTickCount() - m_dwDupObjTick) > 10000)) && ((HUtil32.GetTickCount() - m_dwDupObjTick) < 20000))
                    {
                        CharPushed((byte)M2Share.RandomNumber.Random(8), 1);
                    }
                }
                var castle = M2Share.CastleManager.InCastleWarArea(this);
                if (castle != null && castle.m_boUnderWar)
                {
                    ChangePKStatus(true);
                }
                if ((HUtil32.GetTickCount() - dwTick578) > 1000)
                {
                    dwTick578 = HUtil32.GetTickCount();
                    var wHour = DateTime.Now.Hour;
                    var wMin = DateTime.Now.Minute;
                    var wSec = DateTime.Now.Second;
                    var wMSec = DateTime.Now.Millisecond;
                    if (M2Share.g_Config.boDiscountForNightTime && (wHour == M2Share.g_Config.nHalfFeeStart || wHour == M2Share.g_Config.nHalfFeeEnd))
                    {
                        if (wMin == 0 && wSec <= 30 && (HUtil32.GetTickCount() - m_dwLogonTick) > 60000)
                        {
                            LogonTimcCost();
                            m_dwLogonTick = HUtil32.GetTickCount();
                            m_dLogonTime = DateTime.Now;
                        }
                    }
                    if (m_MyGuild != null)
                    {
                        if (m_MyGuild.GuildWarList.Count > 0)
                        {
                            var boInSafeArea = InSafeArea();
                            if (boInSafeArea != m_boInSafeArea)
                            {
                                m_boInSafeArea = boInSafeArea;
                                RefNameColor();
                                // Notify 战神 client of safe zone entry/exit
                                SendDefMessage(Grobal2.SM_COMMON_INFORMATION,
                                    boInSafeArea ? 1 : 0, m_nCurrX, m_nCurrY, 0,
                                    boInSafeArea ? "safe_enter" : "safe_exit");
                            }
                        }
                    }
                    if (castle != null && castle.m_boUnderWar)
                    {
                        if (m_PEnvir == castle.m_MapPalace && m_MyGuild != null)
                        {
                            if (!castle.IsMember(this))
                            {
                                if (castle.IsAttackGuild(m_MyGuild))
                                {
                                    if (castle.CanGetCastle(m_MyGuild))
                                    {
                                        castle.GetCastle(m_MyGuild);
                                        M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_211, M2Share.nServerIndex, m_MyGuild.sGuildName);
                                        if (castle.InPalaceGuildCount() <= 1)
                                        {
                                            castle.StopWallconquestWar();
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        ChangePKStatus(false);
                    }
                    if (m_boNameColorChanged)
                    {
                        m_boNameColorChanged = false;
                        RefUserState();
                        RefShowName();
                    }
                }
                RunNativeHealthSpellDirty(currentTick);
            }
            catch
            {
                M2Share.MainOutMessage(sExceptionMsg1);
            }
            try
            {
                m_dwGetMsgTick = HUtil32.GetTickCount();
                while (((HUtil32.GetTickCount() - m_dwGetMsgTick) < M2Share.g_Config.dwHumanGetMsgTime) && GetMessage(ref ProcessMsg))
                {
                    if (!Operate(ProcessMsg))
                    {
                        break;
                    }
                }
                if (m_boEmergencyClose || m_boKickFlag || m_boSoftClose)
                {
                    if (m_boSwitchData)
                    {
                        m_sMapName = m_sSwitchMapName;
                        m_nCurrX = m_nSwitchMapX;
                        m_nCurrY = m_nSwitchMapY;
                    }
                    MakeGhost();
                    if (m_boKickFlag)
                    {
                        SendDefMessage(Grobal2.SM_OUTOFCONNECTION, 0, 0, 0, 0, "");
                    }
                    if (!m_boReconnection && m_boSoftClose)
                    {
                        m_MyGuild = M2Share.GuildManager.MemberOfGuild(m_sCharName);
                        if (m_MyGuild != null)
                        {
                            m_MyGuild.SendGuildMsg(m_sCharName + " �Ѿ��˳���Ϸ.");
                            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_208, M2Share.nServerIndex, m_MyGuild.sGuildName + '/' + "" + '/' + m_sCharName + " has exited the game.");
                        }
                        IdSrvClient.Instance.SendHumanLogOutMsg(m_sUserID, m_nSessionID);
                    }
                }
            }
            catch (Exception e)
            {
                if (ProcessMsg.wIdent == 0)
                {
                    MakeGhost(); 
                }
                M2Share.ErrorMessage(format(sExceptionMsg2, m_sCharName, ProcessMsg.wIdent, ProcessMsg.BaseObject, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, ProcessMsg.sMsg));
                M2Share.ErrorMessage(e.Message);
            }
            var boTakeItem = false;
            
            for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
            {
                if (m_UseItems[i] != null && m_UseItems[i].wIndex > 0)
                {
                    var StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[i].wIndex);
                    if (StdItem != null)
                    {
                        if (!CheckItemsNeed(StdItem))
                        {
                            
                            var UserItem = m_UseItems[i];
                            if (AddItemToBag(UserItem))
                            {
                                SendAddItem(UserItem);
                                WeightChanged();
                                boTakeItem = true;
                            }
                            else
                            {
                                if (DropItemDown(m_UseItems[i], 1, false, null, this))
                                {
                                    boTakeItem = true;
                                }
                            }
                            if (boTakeItem)
                            {
                                SendDelItems(m_UseItems[i]);
                                m_UseItems[i].wIndex = 0;
                                RecalcAbilitys();
                            }
                        }
                    }
                    else
                    {
                        m_UseItems[i].wIndex = 0;
                    }
                }
            }
            tObjCount = m_nGameGold;
            if (m_boDecGameGold && (HUtil32.GetTickCount() - m_dwDecGameGoldTick) > m_dwDecGameGoldTime)
            {
                m_dwDecGameGoldTick = HUtil32.GetTickCount();
                if (m_nGameGold >= m_nDecGameGold)
                {
                    m_nGameGold -= m_nDecGameGold;
                    nInteger = m_nDecGameGold;
                }
                else
                {
                    nInteger = m_nGameGold;
                    m_nGameGold = 0;
                    m_boDecGameGold = false;
                    MoveToHome();
                }
                if (M2Share.g_boGameLogGameGold)
                {
                    M2Share.AddGameDataLog(format(M2Share.g_sGameLogMsg1, Grobal2.LOG_GAMEGOLD, m_sMapName, m_nCurrX, m_nCurrY, m_sCharName, M2Share.g_Config.sGameGoldName, nInteger, '-', "Auto"));
                }
            }
            if (m_boIncGameGold && (HUtil32.GetTickCount() - m_dwIncGameGoldTick) > m_dwIncGameGoldTime)
            {
                m_dwIncGameGoldTick = HUtil32.GetTickCount();
                if (m_nGameGold + m_nIncGameGold < 2000000)
                {
                    m_nGameGold += m_nIncGameGold;
                    nInteger = m_nIncGameGold;
                }
                else
                {
                    m_nGameGold = 2000000;
                    nInteger = 2000000 - m_nGameGold;
                    m_boIncGameGold = false;
                }
                if (M2Share.g_boGameLogGameGold)
                {
                    M2Share.AddGameDataLog(format(M2Share.g_sGameLogMsg1, Grobal2.LOG_GAMEGOLD, m_sMapName, m_nCurrX, m_nCurrY, m_sCharName, M2Share.g_Config.sGameGoldName, nInteger, '-', "Auto"));
                }
            }
            if (!m_boDecGameGold && m_PEnvir.Flag.boDECGAMEGOLD)
            {
                if ((HUtil32.GetTickCount() - m_dwDecGameGoldTick) > m_PEnvir.Flag.nDECGAMEGOLDTIME * 1000)
                {
                    m_dwDecGameGoldTick = HUtil32.GetTickCount();
                    if (m_nGameGold >= m_PEnvir.Flag.nDECGAMEGOLD)
                    {
                        m_nGameGold -= m_PEnvir.Flag.nDECGAMEGOLD;
                        nInteger = m_PEnvir.Flag.nDECGAMEGOLD;
                    }
                    else
                    {
                        nInteger = m_nGameGold;
                        m_nGameGold = 0;
                        m_boDecGameGold = false;
                        MoveToHome();
                    }
                    if (M2Share.g_boGameLogGameGold)
                    {
                        M2Share.AddGameDataLog(format(M2Share.g_sGameLogMsg1, Grobal2.LOG_GAMEGOLD, m_sMapName, m_nCurrX, m_nCurrY, m_sCharName, M2Share.g_Config.sGameGoldName, nInteger, '-', "Map"));
                    }
                }
            }
            if (!m_boIncGameGold && m_PEnvir.Flag.boINCGAMEGOLD)
            {
                if ((HUtil32.GetTickCount() - m_dwIncGameGoldTick) > (m_PEnvir.Flag.nINCGAMEGOLDTIME * 1000))
                {
                    m_dwIncGameGoldTick = HUtil32.GetTickCount();
                    if (m_nGameGold + m_PEnvir.Flag.nINCGAMEGOLD <= 2000000)
                    {
                        m_nGameGold += m_PEnvir.Flag.nINCGAMEGOLD;
                        nInteger = m_PEnvir.Flag.nINCGAMEGOLD;
                    }
                    else
                    {
                        nInteger = 2000000 - m_nGameGold;
                        m_nGameGold = 2000000;
                    }
                    if (M2Share.g_boGameLogGameGold)
                    {
                        M2Share.AddGameDataLog(format(M2Share.g_sGameLogMsg1, Grobal2.LOG_GAMEGOLD, m_sMapName, m_nCurrX, m_nCurrY, m_sCharName, M2Share.g_Config.sGameGoldName, nInteger, '+', "Map"));
                    }
                }
            }
            if (tObjCount != m_nGameGold)
            {
                SendUpdateMsg(this, Grobal2.RM_GAMEGOLDCHANGED, 0, 0, 0, 0, "");
            }
            if (m_PEnvir.Flag.boINCGAMEPOINT)
            {
                if ((HUtil32.GetTickCount() - m_dwIncGamePointTick) > (m_PEnvir.Flag.nINCGAMEPOINTTIME * 1000))
                {
                    m_dwIncGamePointTick = HUtil32.GetTickCount();
                    if (m_nGamePoint + m_PEnvir.Flag.nINCGAMEPOINT <= 2000000)
                    {
                        m_nGamePoint += m_PEnvir.Flag.nINCGAMEPOINT;
                        nInteger = m_PEnvir.Flag.nINCGAMEPOINT;
                    }
                    else
                    {
                        m_nGamePoint = 2000000;
                        nInteger = 2000000 - m_nGamePoint;
                    }
                    if (M2Share.g_boGameLogGamePoint)
                    {
                        M2Share.AddGameDataLog(format(M2Share.g_sGameLogMsg1, Grobal2.LOG_GAMEPOINT, m_sMapName, m_nCurrX, m_nCurrY, m_sCharName, M2Share.g_Config.sGamePointName, nInteger, '+', "Map"));
                    }
                }
            }
            if (m_PEnvir.Flag.boDECHP && (HUtil32.GetTickCount() - m_dwDecHPTick) > (m_PEnvir.Flag.nDECHPTIME * 1000))
            {
                m_dwDecHPTick = HUtil32.GetTickCount();
                if (m_WAbil.HP > m_PEnvir.Flag.nDECHPPOINT)
                {
                    m_WAbil.HP -= m_PEnvir.Flag.nDECHPPOINT;
                }
                else
                {
                    m_WAbil.HP = 0;
                }
                HealthSpellChanged();
            }
            if (m_PEnvir.Flag.boINCHP && (HUtil32.GetTickCount() - m_dwIncHPTick) > (m_PEnvir.Flag.nINCHPTIME * 1000))
            {
                m_dwIncHPTick = HUtil32.GetTickCount();
                if ((long)m_WAbil.HP + m_PEnvir.Flag.nDECHPPOINT < m_WAbil.MaxHP)
                {
                    m_WAbil.HP = ClampAbility((long)m_WAbil.HP
                        + m_PEnvir.Flag.nDECHPPOINT);
                }
                else
                {
                    m_WAbil.HP = m_WAbil.MaxHP;
                }
                HealthSpellChanged();
            }
            
            if (M2Share.g_Config.boHungerSystem)
            {
                if ((HUtil32.GetTickCount() - m_dwDecHungerPointTick) > 1000)
                {
                    m_dwDecHungerPointTick = HUtil32.GetTickCount();
                    if (m_nHungerStatus > 0)
                    {
                        tObjCount = GetMyStatus();
                        m_nHungerStatus -= 1;
                        if (tObjCount != GetMyStatus())
                        {
                            RefMyStatus();
                        }
                    }
                    else
                    {
                        if (M2Share.g_Config.boHungerDecHP)
                        {
                            
                            m_nHealthTick -= 60;
                            m_nSpellTick -= 10;
                            m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
                            DecreaseHealthSpellRecoveryStep(1);
                            if (m_WAbil.HP > m_WAbil.HP / 100)
                            {
                                m_WAbil.HP -= HUtil32._MAX(1, m_WAbil.HP / 100);
                            }
                            else
                            {
                                if (m_WAbil.HP <= 2)
                                {
                                    m_WAbil.HP = 0;
                                }
                            }
                            HealthSpellChanged();
                        }
                    }
                }
            }
            if ((HUtil32.GetTickCount() - m_dwRateTick) > 1000)
            {
                m_dwRateTick = HUtil32.GetTickCount();
                if (m_dwKillMonExpRateTime > 0)
                {
                    m_dwKillMonExpRateTime -= 1;
                    if (m_dwKillMonExpRateTime == 0)
                    {
                        m_nKillMonExpRate = 100;
                        SysMsg("经验倍数恢复正常...", MsgColor.Red, MsgType.Hint);
                    }
                }
                if (m_dwPowerRateTime > 0)
                {
                    m_dwPowerRateTime -= 1;
                    if (m_dwPowerRateTime == 0)
                    {
                        m_nPowerRate = 100;
                        SysMsg("当前服务器降级为无消息节点模式运行...", MsgColor.Red, MsgType.Hint);
                    }
                }
            }
            try
            {
                lock (M2Share.HighStatLock)
                {
                if (M2Share.g_HighLevelHuman == this && (m_boDeath || m_boGhost))
                {
                    M2Share.g_HighLevelHuman = null;
                }
                if (M2Share.g_HighPKPointHuman == this && (m_boDeath || m_boGhost))
                {
                    M2Share.g_HighPKPointHuman = null;
                }
                if (M2Share.g_HighDCHuman == this && (m_boDeath || m_boGhost))
                {
                    M2Share.g_HighDCHuman = null;
                }
                if (M2Share.g_HighMCHuman == this && (m_boDeath || m_boGhost))
                {
                    M2Share.g_HighMCHuman = null;
                }
                if (M2Share.g_HighSCHuman == this && (m_boDeath || m_boGhost))
                {
                    M2Share.g_HighSCHuman = null;
                }
                if (M2Share.g_HighOnlineHuman == this && (m_boDeath || m_boGhost))
                {
                    M2Share.g_HighOnlineHuman = null;
                }
                if (m_btPermission < 6)
                {
                    if (M2Share.g_HighLevelHuman == null || (M2Share.g_HighLevelHuman as TPlayObject).m_boGhost)
                    {
                        M2Share.g_HighLevelHuman = this;
                    }
                    else
                    {
                        if (m_Abil.Level > (M2Share.g_HighLevelHuman as TPlayObject).m_Abil.Level)
                        {
                            M2Share.g_HighLevelHuman = this;
                        }
                    }

                    if (M2Share.g_HighPKPointHuman == null || (M2Share.g_HighPKPointHuman as TPlayObject).m_boGhost)
                    {
                        if (m_nPkPoint > 0)
                        {
                            M2Share.g_HighPKPointHuman = this;
                        }
                    }
                    else
                    {
                        if (m_nPkPoint > (M2Share.g_HighPKPointHuman as TPlayObject).m_nPkPoint)
                        {
                            M2Share.g_HighPKPointHuman = this;
                        }
                    }

                    if (M2Share.g_HighDCHuman == null || (M2Share.g_HighDCHuman as TPlayObject).m_boGhost)
                    {
                        M2Share.g_HighDCHuman = this;
                    }
                    else
                    {
                        if (HUtil32.HiWord(m_WAbil.DC) > HUtil32.HiWord((M2Share.g_HighDCHuman as TPlayObject).m_WAbil.DC))
                        {
                            M2Share.g_HighDCHuman = this;
                        }
                    }

                    if (M2Share.g_HighMCHuman == null || (M2Share.g_HighMCHuman as TPlayObject).m_boGhost)
                    {
                        M2Share.g_HighMCHuman = this;
                    }
                    else
                    {
                        if (HUtil32.HiWord(m_WAbil.MC) > HUtil32.HiWord((M2Share.g_HighMCHuman as TPlayObject).m_WAbil.MC))
                        {
                            M2Share.g_HighMCHuman = this;
                        }
                    }

                    if (M2Share.g_HighSCHuman == null || (M2Share.g_HighSCHuman as TPlayObject).m_boGhost)
                    {
                        M2Share.g_HighSCHuman = this;
                    }
                    else
                    {
                        if (HUtil32.HiWord(m_WAbil.SC) > HUtil32.HiWord((M2Share.g_HighSCHuman as TPlayObject).m_WAbil.SC))
                        {
                            M2Share.g_HighSCHuman = this;
                        }
                    }

                    if (M2Share.g_HighOnlineHuman == null || (M2Share.g_HighOnlineHuman as TPlayObject).m_boGhost)
                    {
                        M2Share.g_HighOnlineHuman = this;
                    }
                    else
                    {
                        if (m_dwLogonTick < (M2Share.g_HighOnlineHuman as TPlayObject).m_dwLogonTick)
                        {
                            M2Share.g_HighOnlineHuman = this;
                        }
                    }
                }
                }
            }
            catch (Exception)
            {
                M2Share.MainOutMessage(sExceptionMsg3);
            }
            if (M2Share.g_Config.boReNewChangeColor && m_btReLevel > 0 && (HUtil32.GetTickCount() - m_dwReColorTick) > M2Share.g_Config.dwReNewNameColorTime)
            {
                m_dwReColorTick = HUtil32.GetTickCount();
                m_btReColorIdx++;
                if (m_btReColorIdx > M2Share.g_Config.ReNewNameColor.GetUpperBound(0))
                {
                    m_btReColorIdx = 0;
                }
                m_btNameColor = M2Share.g_Config.ReNewNameColor[m_btReColorIdx];
                RefNameColor();
            }
            
            if (m_GetWhisperHuman != null)
            {
                if (m_GetWhisperHuman.m_boDeath || m_GetWhisperHuman.m_boGhost)
                {
                    m_GetWhisperHuman = null;
                }
            }
            ProcessSpiritSuite();
            try
            {
                if ((HUtil32.GetTickCount() - m_dwClearObjTick) > 10000)
                {
                    m_dwClearObjTick = HUtil32.GetTickCount();
                    if (m_DearHuman != null && (m_DearHuman.m_boDeath || m_DearHuman.m_boGhost))
                    {
                        m_DearHuman = null;
                    }
                    if (m_boMaster)
                    {
                        for (var i = m_MasterList.Count - 1; i >= 0; i--)
                        {
                            var PlayObject = m_MasterList[i];
                            if (PlayObject.m_boDeath || PlayObject.m_boGhost)
                            {
                                m_MasterList.RemoveAt(i);
                            }
                        }
                    }
                    else
                    {
                        if (m_MasterHuman != null && (m_MasterHuman.m_boDeath || m_MasterHuman.m_boGhost))
                        {
                            m_MasterHuman = null;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg4);
                M2Share.ErrorMessage(e.Message);
            }
            if (!m_boClientFlag && m_nStep >= 9 && M2Share.g_Config.boCheckFail)
            {
                if (m_nClientFlagMode == 1)
                {
                    M2Share.g_Config.nTestLevel = M2Share.RandomNumber.Random(M2Share.MAXUPLEVEL + 1);
                }
                else
                {
                    
                    M2Share.UserEngine.ClearItemList();
                }
            }
            if (m_nAutoGetExpPoint > 0 && (m_AutoGetExpEnvir == null || m_AutoGetExpEnvir == m_PEnvir) && (HUtil32.GetTickCount() - m_dwAutoGetExpTick) > m_nAutoGetExpTime)
            {
                m_dwAutoGetExpTick = HUtil32.GetTickCount();
                if (!m_boAutoGetExpInSafeZone || m_boAutoGetExpInSafeZone && InSafeZone())
                {
                    GetExp(m_nAutoGetExpPoint);
                }
            }
            SendMobileHeartbeat();
            base.Run();
        }

        public override bool Operate(TProcessMessage ProcessMsg)
        {
            // PERF: diagnostic write removed from hot path (per-packet)
            // if (ProcessMsg.wIdent == Grobal2.CM_CLICKNPC || ProcessMsg.wIdent == Grobal2.RM_MERCHANTSAY)
            //     System.IO.File.AppendAllText("gateservice_diag.log", $"[Operate] ident={ProcessMsg.wIdent}(0x{ProcessMsg.wIdent:X4}) nParam1={ProcessMsg.nParam1}\n");
            TMessageBodyWL MessageBodyWL = null;
            var dwDelayTime = 0;
            int nMsgCount;
            var result = true;
            TBaseObject BaseObject = null;
            if (ProcessMsg.BaseObject > 0)
            {
                BaseObject = M2Share.ObjectManager.Get(ProcessMsg.BaseObject);
            }
            switch (ProcessMsg.wIdent)
            {
                case NativeMagicProducerPushIdent:
                    TryHandleNativeMagicProducerMessage(ProcessMsg);
                    break;
                case Grobal2.SM_LINGFU_CHANGED:
                    SendNativeCapitalInfo();
                    break;
                case Grobal2.RM_NATIVE_EXP_CONTINUE:
                    GrantNativePlayerExperience(ProcessMsg.nParam1, ProcessMsg.nParam2 != 0,
                        ProcessMsg.nParam3 != 0, ProcessMsg.wParam);
                    break;
                case Grobal2.RM_NATIVE_MOOTEBO_CONTINUE:
                    ContinueNativeMotaeboForcedMove(ProcessMsg);
                    break;
                case Grobal2.RM_USERMOVE:
                    CompleteNativeUserMove(ProcessMsg);
                    break;
                case Grobal2.RM_USERSAVEITEM:
                    SendDefMessage(Grobal2.SM_2821, ProcessMsg.nParam1,
                        ProcessMsg.wParam, ProcessMsg.nParam2,
                        ProcessMsg.nParam3, ProcessMsg.sMsg);
                    break;
                case Grobal2.RM_GLORYFEALTY:
                    SendDefMessage(Grobal2.SM_GLORYFEALTY, 0,
                        HUtil32.LoWord(ProcessMsg.nParam1),
                        HUtil32.LoWord(ProcessMsg.nParam2), 0, string.Empty);
                    break;
                case Grobal2.CM_ATTACKMODE:
                    ChangeNativeAttackMode(ProcessMsg.nParam3);
                    break;
                case Grobal2.CM_COMMON_INFORMATION:
                    ClientCommonInformation(ProcessMsg);
                    break;
                case Grobal2.CM_YANHUA_TEXT:
                    ClientNativeFireworkText(ProcessMsg);
                    break;
                case Grobal2.CM_CATTLE_REVEAL_PRIZE:
                    if (!ClientNativeCattleRevealPrize() &&
                        TrySelectNativeNeedKeyBox(out var cattleBoxSlot))
                    {
                        SendDefMessage((short)Grobal2.SM_CATTLE_PRIZE_REVEAL,
                            cattleBoxSlot, 0, 0, 3, string.Empty);
                    }
                    break;
                case Grobal2.CM_CATTLE_CLAIM_PRIZE:
                    if ((_nativeNeedKeyBoxSelectedReward?.Length ?? 0) != 0)
                        ClientNativeNeedKeyBoxClaimPrize();
                    else
                        ClientNativeCattleClaimPrize();
                    break;
                case Grobal2.CM_MERCHANTQUERYEXCHGBOOK:
                    ClientMerchantQueryExchgBook(ProcessMsg.nParam1,
                        HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3));
                    break;
                case Grobal2.CM_EXCHANGEBOOK_ROTATE:
                    ClientExchangeBookRotate(ProcessMsg.nParam1,
                        HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3));
                    break;
                case Grobal2.CM_EXCHANGEBOOK_GET_PRIZE:
                    ClientExchangeBookGetPrize();
                    break;
                case Grobal2.CM_EXCHANGEBOOK_CLOSE:
                    ClientExchangeBookClose();
                    break;
                case Grobal2.CM_QUERYUSERNAME:
                    ClientQueryUserName(ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3);
                    break;
                case Grobal2.CM_QUERYBAGITEMS: 
                    if ((HUtil32.GetTickCount() - m_dwQueryBagItemsTick) > 30 * 1000)
                    {
                        m_dwQueryBagItemsTick = HUtil32.GetTickCount();
                        ClientQueryBagItems();
                    }
                    else
                    {
                        SysMsg(M2Share.g_sQUERYBAGITEMS, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case Grobal2.CM_CLICK_BACKHOME:
                    ClientClickBackHome();
                    break;
                case Grobal2.CM_V_POWERSTONE:
                    break;
                case Grobal2.CM_QUERYUSERSTATE:
                    ClientQueryUserState(ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3);
                    break;
                // CM_QUERYUSERSET (3040) is not dispatched, because native does not
                // dispatch it. The subtree that owns this range is
                //   0x6D85E3  3D EB 0B 00 00     cmp eax,0xBEB     ; 3051
                //   0x6D85E8  7F 31              jg  0x6D861B
                //   0x6D85EA  0F 84 CE 1C 00 00  je  0x6DA2BE
                //   0x6D85F0  2D D4 0B 00 00     sub eax,0xBD4     ; 3028 -> 0x6D9EAF
                //   0x6D85FB  83 E8 02           sub eax,2         ; 3030 -> 0x6DA1B1
                //   0x6D8604  83 E8 02           sub eax,2         ; 3032 -> 0x6DA1D5
                //   0x6D860D  83 E8 03           sub eax,3         ; 3035 -> 0x6D9EAF
                //   0x6D8616  E9 11 36 00 00     jmp 0x6DBC2C      ; everything else
                // 3040 has no arm in that chain, and no encoding of 3040 appears anywhere
                // in CODE as an instruction immediate: the single byte-pattern hit at
                // 0x6D2465 has zero converging decode starts because those bytes are the
                // tail of `FF 84 BE E0 0B 00 00 inc [esi+edi*4+0xBE0]`.
                case Grobal2.CM_DROPITEM:
                    if (ClientDropItem(ProcessMsg.sMsg, ProcessMsg.nParam1))
                    {
                        SendDefMessage(Grobal2.SM_DROPITEM_SUCCESS, ProcessMsg.nParam1, 0, 0, 0, "");
                    }
                    else
                    {
                        SendDefMessage(Grobal2.SM_DROPITEM_FAIL, ProcessMsg.nParam1, 0, 0, 0, "");
                    }
                    break;
                case Grobal2.CM_PICKUP:
                    if (m_nCurrX == ProcessMsg.nParam2 && m_nCurrY == ProcessMsg.nParam3)
                    {
                        ClientPickUpItem();
                    }
                    break;
                case Grobal2.CM_PICKUP_RANGE:
                    ClientPickUpRange();
                    break;
                case Grobal2.CM_OPENDOOR:
                    ClientOpenDoor(ProcessMsg.nParam2, ProcessMsg.nParam3);
                    break;
                case Grobal2.CM_TAKEONITEM:
                    ClientTakeOnItems((byte)ProcessMsg.nParam2, ProcessMsg.nParam1, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_TAKEOFFITEM:
                    ClientTakeOffItems((byte)ProcessMsg.nParam2, ProcessMsg.nParam1, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_EAT:
                case Grobal2.CM_1069:
                    // Native sub_6B8380 reads the use-mode from the wire Param word [+6]
                    // (caller 0x006D8E52: movzx ecx, word ptr [eax+6]); Recog [+0] is the item id.
                    // The default CM decode maps Param[+6]->nParam2 and Series[+0x0A]->wParam, so the
                    // mode is nParam2 (matching sibling CM_TAKEONITEM at :1003). Previously wParam.
                    ClientUseItems(ProcessMsg.nParam1, ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_SETFIXEDCOORD:
                    // 战神 CM 3420 (0xD5C) -> sub_6E9BAC setter. Key from msg[+0x00]
                    // (0x6DAE13-0x6DAE16: `mov eax,[ebp-0x34]; mov edx,[eax]`).
                    // The default dispatch leg (UsrEngn.cs default:) forwards
                    // Recog into wParam and the message as-is, so nParam1 carries
                    // the wire Recog (== MakeIndex bag match key).
                    ClientSetFixedCoord(ProcessMsg.nParam1);
                    break;
                case Grobal2.CM_BUTCH:
                    if (!ClientGetButchItem(ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, (byte)ProcessMsg.wParam, ref dwDelayTime))
                    {
                        if (dwDelayTime != 0)
                        {
                            nMsgCount = GetDigUpMsgCount();
                            if (nMsgCount >= M2Share.g_Config.nMaxDigUpMsgCount)
                            {
                                m_nOverSpeedCount++;
                                if (m_nOverSpeedCount > M2Share.g_Config.nOverSpeedKickCount)
                                {
                                    if (M2Share.g_Config.boKickOverSpeed)
                                    {
                                        SysMsg(M2Share.g_sKickClientUserMsg, MsgColor.Red, MsgType.Hint);
                                        m_boEmergencyClose = true;
                                    }
                                    if (M2Share.g_Config.boViewHackMessage)
                                    {
                                        M2Share.MainOutMessage(format(M2Share.g_sBunOverSpeed, m_sCharName, dwDelayTime, nMsgCount));
                                    }
                                }
                                SendRefMsg(Grobal2.RM_MOVEFAIL, 0, 0, 0, 0, "");// ����������͹���ʧ����Ϣ
                            }
                            else
                            {
                                if (dwDelayTime < M2Share.g_Config.dwDropOverSpeed)
                                {
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("速度异常 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                    SendSocket(M2Share.GetGoodTick);
                                }
                                else
                                {
                                    SendDelayMsg(this, ProcessMsg.wIdent, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, "", dwDelayTime);
                                    result = false;
                                }
                            }
                        }
                    }
                    break;
                case Grobal2.CM_MAGICKEYCHANGE:
                    ClientChangeMagicKey(ProcessMsg.nParam1, ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_SOFTCLOSE:
                    if (!m_boOffLineFlag)
                    {
                        m_boReconnection = true;
                        m_boSoftClose = true;
                        if (ProcessMsg.wParam == 1)
                        {
                            m_boEmergencyClose = true;
                        }
                    }
                    break;
                case Grobal2.CM_CLICKNPC:
                    ClientClickNPC(ProcessMsg.nParam1);
                    break;
                case Grobal2.CM_MERCHANTDLGSELECT:
                    ClientMerchantDlgSelect(ProcessMsg.nParam1, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_MERCHANTQUERYSELLPRICE:
                    ClientMerchantQuerySellPrice(ProcessMsg.nParam1, HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3), ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_USERSELLITEM:
                    ClientUserSellItem(ProcessMsg.nParam1, HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3), ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_USERBUYITEM:
                    ClientUserBuyItem(ProcessMsg.wIdent, ProcessMsg.nParam1, HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3), 0, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_USERGETDETAILITEM:
                    ClientUserBuyItem(ProcessMsg.wIdent, ProcessMsg.nParam1, 0, ProcessMsg.nParam2, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_DROPGOLD:
                    if (ProcessMsg.nParam1 > 0)
                    {
                        ClientDropGold(ProcessMsg.nParam1);
                    }
                    break;
                case Grobal2.CM_1017:
                    // Native sub_6D5E50 = item-stack MERGE into use-slot U_BUJUK(9). Gated (default OFF):
                    // while dormant, keep the legacy stale ack. Recog=nParam1, wParam=nParam2 (default
                    // client-message decode). See TPlayObject.NativeItemMerge.cs for the dupe/loss audit.
                    if (!TryClientNativeItemMergeGated(ProcessMsg.nParam1, ProcessMsg.nParam2))
                        SendDefMessage(1, 0, 0, 0, 0, "");
                    break;
                case Grobal2.CM_LOGINNOTICEOK:
                    // Native dispatch tree routes opcode 1018 (0x3FA) to DEFAULT handler at 0x6DBC2C.
                    // The DEFAULT handler is a cleanup routine (xor eax,eax; pop edx; pop ecx; pop ecx;
                    // mov fs:[eax],edx; jmp exit) that performs no message-specific logic. The client
                    // sends CM_LOGINNOTICEOK when the player acknowledges the login notice/MOTD dialog;
                    // the server does not need to take any action in response. This explicit no-op case
                    // documents the native behavior (silent acknowledgment, no server-side state change).
                    break;
                case Grobal2.CM_GROUPMODE:
                    if (ProcessMsg.nParam2 == 0)
                    {
                        ClientGroupClose();
                    }
                    else
                    {
                        m_boAllowGroup = true;
                    }
                    if (m_boAllowGroup)
                    {
                        SendDefMessage(Grobal2.SM_GROUPMODECHANGED, 0, 1, 0, 0, "");
                    }
                    else
                    {
                        SendDefMessage(Grobal2.SM_GROUPMODECHANGED, 0, 0, 0, 0, "");
                    }
                    break;
                case Grobal2.CM_CREATEGROUP:
                    ClientCreateGroup(ProcessMsg.sMsg.Trim());
                    break;
                case Grobal2.CM_ADDGROUPMEMBER:
                    ClientAddGroupMember(ProcessMsg.sMsg.Trim());
                    break;
                case Grobal2.CM_DELGROUPMEMBER:
                    ClientDelGroupMember(ProcessMsg.sMsg.Trim());
                    break;
                case Grobal2.CM_USERREPAIRITEM:
                    ClientRepairItem(ProcessMsg.nParam1, HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3), ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_MERCHANTQUERYREPAIRCOST:
                    ClientQueryRepairCost(ProcessMsg.nParam1, HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3), ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_DEALTRY:
                    ClientDealTry(ProcessMsg.sMsg.Trim());
                    break;
                case Grobal2.CM_DEALADDITEM:
                    ClientAddDealItem(ProcessMsg.nParam1, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_DEALDELITEM:
                    ClientDelDealItem(ProcessMsg.nParam1, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_DEALCANCEL:
                    ClientCancelDeal();
                    break;
                case Grobal2.CM_DEALCHGGOLD:
                    ClientChangeDealGold(ProcessMsg.nParam1);
                    break;
                case Grobal2.CM_DEALEND:
                    ClientDealEnd();
                    break;
                case Grobal2.CM_USERSTORAGEITEM:
                    switch (ProcessMsg.wParam)
                    {
                        case 0:
                            ClientStorageItem(ProcessMsg.nParam1, HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3), ProcessMsg.sMsg);
                            break;
                        case 1:
                            ClientNativeAccountStorageItem(ProcessMsg.nParam1,
                                HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3));
                            break;
                        case 2:
                            RejectUnsupportedStorageItem(2);
                            break;
                    }
                    break;
                case Grobal2.CM_USERTAKEBACKSTORAGEITEM:
                    switch (ProcessMsg.wParam)
                    {
                        case 0:
                            ClientTakeBackStorageItem(ProcessMsg.nParam1, HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3), ProcessMsg.sMsg);
                            break;
                        case 1:
                            ClientNativeAccountTakeBackStorageItem(ProcessMsg.nParam1,
                                HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3));
                            break;
                        case 2:
                            RejectUnsupportedTakeBackStorageItem(2);
                            break;
                    }
                    break;
                case Grobal2.CM_WANTMINIMAP:
                    ClientGetMinMap();
                    break;
                case Grobal2.CM_USERMAKEDRUGITEM:
                    ClientMakeDrugItem(ProcessMsg.nParam1, ProcessMsg.sMsg);
                    break;
                // 1035-1041 and 1044/1045 (the pre-GILD guild protocol) are not part of
                // this engine's wire surface and are no longer dispatched here. The
                // 1016-based jump table at 0x6D8159 covers 19 entries only
                // (0x6D8144 `05 08 FC FF FF add eax,-0x3F8`, 0x6D8149 `83 F8 12
                // cmp eax,0x12`, 0x6D814C `ja 0x6DBC2C`), so 1035-1041 fall off the end
                // into the default arm; the 1043-based table at 0x6D81BA has entries for
                // 1044/1045 but both hold 0x6DBC2C, the default label itself
                // (`xor eax,eax; pop edx; pop ecx; pop ecx; mov fs:[eax],edx; jmp exit`).
                // A CODE-wide scan for every immediate encoding of 1035/1036/1037/1040/
                // 1041/1044/1045 returns zero hits, and 1038/1039 hit only RTL constants
                // (0x462A75 `push 0x40E` in an exception path, 0x40E021 `mov ecx,0x40F`
                // in float digit clamping). The guild wire protocol this engine really
                // uses is CM_GILD_* 4560-4588, all of which have live handlers
                // (0x6DB5DE..0x6DB9AC) and live C# arms.
                case Grobal2.CM_SPEEDHACKUSER:
                    M2Share.MainOutMessage("[Warning]: [使用加速外挂程序(客户端)] ");
                    break;
                case Grobal2.CM_ADJUST_BONUS:
                    ClientAdjustBonus(ProcessMsg.nParam1, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_TURN:
                    // 0x6D9B76 `8A 40 0A mov al,[msg+0x0A]` / 0x6D9B79 `24 07 and al,7`
                    // before sub_6BBC60; same masking gap as CM_SITDOWN below.
                    if (ClientChangeDir((short)ProcessMsg.wIdent, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam & 7, ref dwDelayTime))
                    {
                        m_dwActionTick = HUtil32.GetTickCount();
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            SendMoveActionFail();
                        }
                        else
                        {
                            nMsgCount = GetTurnMsgCount();
                            if (nMsgCount >= M2Share.g_Config.nMaxTurnMsgCount)
                            {
                                // MOVE-22: Native never disconnects, kicks or logs a fast client.
                                // Simply send correction back to client.
                                SendMoveActionFail();
                            }
                            else
                            {
                                if (dwDelayTime < M2Share.g_Config.dwDropOverSpeed)
                                {
                                    SendSocket(M2Share.GetGoodTick);
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("速度异常 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                }
                                else
                                {
                                    SendDelayMsg(this, (short)ProcessMsg.wIdent, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, "", dwDelayTime);
                                    result = false;
                                }
                            }
                        }
                    }
                    break;
                case Grobal2.CM_WALK:
                    if (ClientWalkXY(ProcessMsg.wIdent, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.boLateDelivery, ref dwDelayTime))
                    {
                        m_dwActionTick = HUtil32.GetTickCount();
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_GOOD, 0, 0, 0, 0);
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            SendMoveActionFail();
                        }
                        else
                        {
                            nMsgCount = GetWalkMsgCount();
                            if (nMsgCount >= M2Share.g_Config.nMaxWalkMsgCount)
                            {
                                // MOVE-22: Native never disconnects, kicks or logs a fast client.
                                // Simply send correction back to client.
                                SendMoveActionFail();
                                if (m_boTestSpeedMode)
                                {
                                    SysMsg(format("速度异常 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                }
                            }
                            else
                            {
                                if (dwDelayTime > M2Share.g_Config.dwDropOverSpeed && M2Share.g_Config.btSpeedControlMode == 1 && m_boFilterAction)
                                {
                                    SendMoveActionFail();
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("速度异常 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                }
                                else
                                {
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("操作延迟 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                    SendDelayMsg(this, (short)ProcessMsg.wIdent, (short)ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, "", dwDelayTime);
                                    result = false;
                                }
                            }
                        }
                    }
                    break;
                case Grobal2.CM_HORSERUN:
                    if (ClientHorseRunXY((short)ProcessMsg.wIdent, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.boLateDelivery, ref dwDelayTime))
                    {
                        m_dwActionTick = HUtil32.GetTickCount();
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_GOOD, 0, 0, 0, 0);
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            SendMoveActionFail();
                        }
                        else
                        {
                            nMsgCount = GetRunMsgCount();
                            if (nMsgCount >= M2Share.g_Config.nMaxRunMsgCount)
                            {
                                // MOVE-22: Native never disconnects, kicks or logs a fast client.
                                // Simply send correction back to client.
                                SendMoveActionFail();
                                if (m_boTestSpeedMode)
                                {
                                    SysMsg(format("速度异常 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                }
                            }
                            else
                            {
                                if (m_boTestSpeedMode)
                                {
                                    SysMsg(format("操作延迟 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                }
                                SendDelayMsg(this, (short)ProcessMsg.wIdent, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, "", dwDelayTime);
                                result = false;
                            }
                        }
                    }
                    break;
                case Grobal2.CM_RUN:
                    // Native 0x6D9CE4: CM_RUN(3013) shares the WEIGHT/RUNFLAG/CanRun
                    // ladder with CM_RUN3(4108) but NOT the inner mover. The twins
                    // differ in two places, not one:
                    //   3013 sub_76756C  0x7675E0 add edi,edi        ×2
                    //                    0x76763F mov dx,0x0D        ident 13
                    //   4108 sub_767694  0x767708 lea edi,[edi+edi*2] ×3
                    //                    0x767769 mov dx,0xD58       ident 3416
                    // Handler 0x6D9CE4 never tests bodyState 0x33; only 4108 does
                    // (0x6D9D99 mov dl,0x33 / call 0x772960 / je fail). Routing
                    // 3013 through ClientNativeRun3 made a mounted runner take
                    // the 3-step mover and broadcast 3416. HasNativeActiveState(51)
                    // therefore must not select the mover — opcode does.
                    if (ClientRunXY(ProcessMsg.wIdent, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, ref dwDelayTime))
                    {
                        m_dwActionTick = HUtil32.GetTickCount();
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_GOOD, 0, 0, 0, 0);
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            SendMoveActionFail();
                        }
                        else
                        {
                            nMsgCount = GetRunMsgCount();
                            if (nMsgCount >= M2Share.g_Config.nMaxRunMsgCount)
                            {
                                // MOVE-22: Native never disconnects, kicks or logs a fast client.
                                // Simply send correction back to client.
                                SendMoveActionFail();
                            }
                            else
                            {
                                if (dwDelayTime > M2Share.g_Config.dwDropOverSpeed && M2Share.g_Config.btSpeedControlMode == 1 && m_boFilterAction)
                                {
                                    SendMoveActionFail();
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("速度异常 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                }
                                else
                                {
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("操作延迟 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                    SendDelayMsg(this, (short)ProcessMsg.wIdent, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, Grobal2.CM_RUN, "", dwDelayTime);
                                    result = false;
                                }
                            }
                        }
                    }
                    break;
                case Grobal2.CM_SHANGMA_OK:
                    ClientNativeHorseReady();
                    break;
                case Grobal2.CM_XIAMA:
                    ClientNativeHorseDismount();
                    break;
                case Grobal2.CM_RUN3:
                    if (ClientNativeRun3(ProcessMsg.nParam1, ProcessMsg.nParam2))
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(
                            Grobal2.SM_ACT_GOOD, 0, 0, 0, 0);
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        SendMoveActionFail();
                    }
                    break;
                case Grobal2.CM_YAOQING_SHANGMA:
                    ClientNativeHorseInvite(ProcessMsg.nParam1);
                    break;
                case Grobal2.CM_INVITE_HORSE:
                    ClientNativeHorseInviteResponse(ProcessMsg.nParam1,
                        ProcessMsg.wParam);
                    break;
                case Grobal2.CM_RIDER_DOWN:
                    ClientNativeHorseRiderDown();
                    break;
                case Grobal2.CM_HIT:
                case Grobal2.CM_HEAVYHIT:
                case Grobal2.CM_BIGHIT:
                case Grobal2.CM_POWERHIT:
                case Grobal2.CM_LONGHIT:
                case Grobal2.CM_WIDEHIT:
                case Grobal2.CM_CRSHIT:
                case Grobal2.CM_TWINHIT:
                case Grobal2.CM_FIREHIT:
                case Grobal2.CM_SWORD_HIT:
                case Grobal2.CM_3037:
                    // Native masks the wire direction to 3 bits before handing it
                    // to the shared attack handler sub_6EC078. Both dispatch arms
                    // do it, byte-identically:
                    //   0x6D9EF1  8A 40 0A  mov al, byte [msg+0x0A]   ; Series low
                    //   0x6D9EF4  24 07     and al, 7
                    //   0x6D9EF6  50        push eax
                    // and again at 0x6D9F8D / 0x6D9F90 / 0x6D9F92.
                    // Without the mask a forged packet can drive the direction to
                    // 0..255 and index past the 8-entry direction tables. Same
                    // defect class as the CM_RUN direction fix (MOVE-19).
                    //
                    // 3027's arm 0x6D9F4B differs from the shared arm in exactly one
                    // instruction - 0x6D9F9B `mov dx,[msg+8]` (Tag) against 0x6D9EFF
                    // `mov dx,[msg+4]` (Ident) - so for 3027 the client names the
                    // action in Tag and sub_6EC078 range-checks that value instead.
                    // UsrEngn carries it here in nParam3.
                    if (ClientHitXY(
                            ProcessMsg.wIdent == Grobal2.CM_3037
                                ? ProcessMsg.nParam3
                                : ProcessMsg.wIdent,
                            ProcessMsg.nParam1, ProcessMsg.nParam2, (byte)(ProcessMsg.wParam & 7), ProcessMsg.boLateDelivery, ref dwDelayTime))
                    {
                        m_dwActionTick = HUtil32.GetTickCount();
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_GOOD, 0, 0, 0, 0);
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            SendRefMsg(Grobal2.RM_MOVEFAIL, 0, 0, 0, 0, "");
                            SendDefMessage(Grobal2.SM_ACT_FAIL, (int)ProcessMsg.wIdent, 0, 0, 0, "");
                        }
                        else
                        {
                            nMsgCount = GetHitMsgCount();
                            if (nMsgCount >= M2Share.g_Config.nMaxHitMsgCount)
                            {
                                m_nOverSpeedCount++;
                                if (m_nOverSpeedCount > M2Share.g_Config.nOverSpeedKickCount)
                                {
                                    if (M2Share.g_Config.boKickOverSpeed)
                                    {
                                        SysMsg(M2Share.g_sKickClientUserMsg, MsgColor.Red, MsgType.Hint);
                                        m_boEmergencyClose = true;
                                    }
                                    if (M2Share.g_Config.boViewHackMessage)
                                    {
                                        M2Share.MainOutMessage(format(M2Share.g_sHitOverSpeed, m_sCharName, dwDelayTime, nMsgCount));
                                    }
                                }
                                SendRefMsg(Grobal2.RM_MOVEFAIL, 0, 0, 0, 0, "");// ����������͹���ʧ����Ϣ
                            }
                            else
                            {
                                if (dwDelayTime > M2Share.g_Config.dwDropOverSpeed && M2Share.g_Config.btSpeedControlMode == 1 && m_boFilterAction)
                                {
                                    SendSocket(M2Share.GetGoodTick);
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("速度异常 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                }
                                else
                                {
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg("操作延迟 Ident: " + ProcessMsg.wIdent + " Time: " + dwDelayTime, MsgColor.Red, MsgType.Hint);
                                    }
                                    SendDelayMsg(this, (short)ProcessMsg.wIdent, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, "", dwDelayTime);
                                    result = false;
                                }
                            }
                        }
                    }
                    break;
                case Grobal2.CM_SITDOWN:
                    // 0x6D9CAC `8A 40 0A mov al,[msg+0x0A]` / 0x6D9CAF `24 07 and al,7`
                    // masks the direction before sub_6BBF9C, exactly like the hit family
                    // at 0x6D9EF4 and CM_TURN at 0x6D9B79. The mask was only being applied
                    // up in the UsrEngn mapping layer, so anything reaching this arm by
                    // another route - the `default` forward, or a re-delivered delay
                    // message - could still carry a 0..65535 direction.
                    if (ClientSitDownHit(ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam & 7, ref dwDelayTime))
                    {
                        m_dwActionTick = HUtil32.GetTickCount();
                        SendSocket(M2Share.GetGoodTick);
                        SendDefMessage(Grobal2.SM_SITDOWN, m_nCurrX, m_nCurrY, m_btDirection, 0, "");
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            SendRefMsg(Grobal2.RM_MOVEFAIL, 0, 0, 0, 0, "");
                            SendDefMessage(Grobal2.SM_ACT_FAIL, (int)ProcessMsg.wIdent, 0, 0, 0, "");
                        }
                        else
                        {
                            nMsgCount = GetSiteDownMsgCount();
                            if (nMsgCount >= M2Share.g_Config.nMaxSitDonwMsgCount)
                            {
                                m_nOverSpeedCount++;
                                if (m_nOverSpeedCount > M2Share.g_Config.nOverSpeedKickCount)
                                {
                                    if (M2Share.g_Config.boKickOverSpeed)
                                    {
                                        SysMsg(M2Share.g_sKickClientUserMsg, MsgColor.Red, MsgType.Hint);
                                        m_boEmergencyClose = true;
                                    }
                                    if (M2Share.g_Config.boViewHackMessage)
                                    {
                                        M2Share.MainOutMessage(format(M2Share.g_sBunOverSpeed, m_sCharName, dwDelayTime, nMsgCount));
                                    }
                                }
                                SendRefMsg(Grobal2.RM_MOVEFAIL, 0, 0, 0, 0, "");// ����������͹���ʧ����Ϣ
                            }
                            else
                            {
                                if (dwDelayTime < M2Share.g_Config.dwDropOverSpeed)
                                {
                                    SendSocket(M2Share.GetGoodTick);
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("速度异常 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                }
                                else
                                {
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("操作延迟 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                    SendDelayMsg(this, (short)ProcessMsg.wIdent, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, "", dwDelayTime);
                                    result = false;
                                }
                            }
                        }
                    }
                    break;
                case Grobal2.CM_SPELL:
                    if (ClientSpellXY((short)ProcessMsg.wIdent, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, M2Share.ObjectManager.Get(ProcessMsg.nParam3), ProcessMsg.boLateDelivery, ref dwDelayTime))
                    {
                        m_dwActionTick = HUtil32.GetTickCount();
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            SendRefMsg(Grobal2.RM_MOVEFAIL, 0, 0, 0, 0, "");
                            SendDefMessage(Grobal2.SM_ACT_FAIL, (int)ProcessMsg.wIdent, 0, 0, 0, "");
                        }
                        else
                        {
                            nMsgCount = GetSpellMsgCount();
                            if (nMsgCount >= M2Share.g_Config.nMaxSpellMsgCount)
                            {
                                m_nOverSpeedCount++;
                                if (m_nOverSpeedCount > M2Share.g_Config.nOverSpeedKickCount)
                                {
                                    if (M2Share.g_Config.boKickOverSpeed)
                                    {
                                        SysMsg(M2Share.g_sKickClientUserMsg, MsgColor.Red, MsgType.Hint);
                                        m_boEmergencyClose = true;
                                    }
                                    if (M2Share.g_Config.boViewHackMessage)
                                    {
                                        M2Share.MainOutMessage(format(M2Share.g_sSpellOverSpeed, m_sCharName, dwDelayTime, nMsgCount));
                                    }
                                }
                                SendRefMsg(Grobal2.RM_MOVEFAIL, 0, 0, 0, 0, "");// ����������͹���ʧ����Ϣ
                            }
                            else
                            {
                                if (dwDelayTime > M2Share.g_Config.dwDropOverSpeed && M2Share.g_Config.btSpeedControlMode == 1 && m_boFilterAction)
                                {
                                    SendRefMsg(Grobal2.RM_MOVEFAIL, 0, 0, 0, 0, "");
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("速度异常 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                }
                                else
                                {
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg(format("操作延迟 Ident: {0} Time: {1}", ProcessMsg.wIdent, dwDelayTime), MsgColor.Red, MsgType.Hint);
                                    }
                                    SendDelayMsg(this, (short)ProcessMsg.wIdent, ProcessMsg.wParam, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, "", dwDelayTime);
                                    result = false;
                                }
                            }
                        }
                    }
                    break;
                case Grobal2.CM_SAY:
                    if (!string.IsNullOrEmpty(ProcessMsg.sMsg))
                    {
                        ProcessUserLineMsg(ProcessMsg.sMsg);
                    }
                    break;
                case Grobal2.CM_SWITCH_LISTEN:
                    ProcessSwitchListen(ProcessMsg);
                    break;
                case Grobal2.CM_PASSWORD:
                    ProcessClientPassword(ProcessMsg);
                    break;
                case Grobal2.RM_WALK:
                    if (BaseObject != null && ProcessMsg.BaseObject != ObjectId)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_WALK, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, HUtil32.MakeWord(ProcessMsg.wParam, BaseObject.m_nLight));
                        SendMobileMovement(m_DefMsg, BaseObject);
                    }
                    break;
                case Grobal2.RM_RUN:
                    if (BaseObject != null && ProcessMsg.BaseObject != ObjectId)
                    {
                        var runIdent = BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT ? Grobal2.SM_RUN : Grobal2.SM_WALK;
                        m_DefMsg = Grobal2.MakeDefaultMsg(runIdent, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, HUtil32.MakeWord(ProcessMsg.wParam, BaseObject.m_nLight));
                        SendMobileMovement(m_DefMsg, BaseObject);
                    }
                    break;
                case Grobal2.RM_RUN3:
                    if (BaseObject != null && ProcessMsg.BaseObject != ObjectId)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_RUN3,
                            ProcessMsg.BaseObject, ProcessMsg.nParam1,
                            ProcessMsg.nParam2,
                            HUtil32.MakeWord(ProcessMsg.wParam,
                                BaseObject.m_nLight));
                        SendMobileMovement(m_DefMsg, BaseObject);
                    }
                    break;
                case Grobal2.RM_HIT:
                case Grobal2.RM_HEAVYHIT:
                case Grobal2.RM_BIGHIT:
                case Grobal2.RM_POWERHIT:
                case Grobal2.RM_LONGHIT:
                case Grobal2.RM_WIDEHIT:
                case Grobal2.RM_FIREHIT:
                case Grobal2.RM_CRSHIT:
                case Grobal2.RM_TWINHIT:
                case Grobal2.RM_SWORD_HIT:
                    if (ProcessMsg.BaseObject != this.ObjectId)
                    {
                        var subCode = ProcessMsg.wIdent;
                        switch (subCode) { case Grobal2.RM_HEAVYHIT: subCode = Grobal2.SM_HEAVYHIT; break; case Grobal2.RM_BIGHIT: subCode = Grobal2.SM_BIGHIT; break; case Grobal2.RM_POWERHIT: subCode = Grobal2.SM_POWERHIT; break; case Grobal2.RM_LONGHIT: subCode = Grobal2.SM_LONGHIT; break; case Grobal2.RM_WIDEHIT: subCode = Grobal2.SM_WIDEHIT; break; case Grobal2.RM_FIREHIT: subCode = Grobal2.SM_FIREHIT; break; case Grobal2.RM_CRSHIT: subCode = Grobal2.SM_CRSHIT; break; case Grobal2.RM_TWINHIT: subCode = Grobal2.SM_TWINHIT; break; case Grobal2.RM_SWORD_HIT: subCode = Grobal2.SM_SWORD_HIT; break; default: subCode = Grobal2.SM_HIT; break; }
                        m_DefMsg = Grobal2.MakeDefaultMsg((short)subCode, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                        if (ProcessMsg.wIdent == Grobal2.RM_LONGHIT ||
                            ProcessMsg.wIdent == Grobal2.RM_WIDEHIT)
                        {
                            SendSocket(m_DefMsg, BitConverter.GetBytes(ProcessMsg.nParam3));
                        }
                        else if (ProcessMsg.wIdent == Grobal2.RM_FIREHIT &&
                                 (ProcessMsg.nParam3 & 0x8000) != 0)
                        {
                            SendSocket(m_DefMsg, BitConverter.GetBytes(5));
                        }
                        else
                        {
                            SendSocket(m_DefMsg);
                        }
                    }
                    break;
                case Grobal2.RM_SPELL:
                    if (BaseObject != null &&
                        (BaseObject.GetBodyStateWord(1) & ((1 << 19) | (1 << 20))) == 0 &&
                        (ProcessMsg.BaseObject != ObjectId ||
                         (uint)(ProcessMsg.nParam1 - 60) < 6 ||
                         !string.IsNullOrEmpty(ProcessMsg.sMsg)))
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SPELL, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                        var spellBody = new byte[8];
                        Buffer.BlockCopy(BitConverter.GetBytes((int)HUtil32.LoWord(ProcessMsg.nParam3)),
                            0, spellBody, 0, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes((int)HUtil32.HiWord(ProcessMsg.nParam3)),
                            0, spellBody, 4, 4);
                        SendSocket(m_DefMsg, spellBody);
                    }
                    break;
                case Grobal2.RM_MOVEFAIL:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_MOVEFAIL, ObjectId, m_nCurrX, m_nCurrY, m_btDirection);
                    SendSocket(m_DefMsg);
                    break;
                case Grobal2.RM_NATIVE_HORSE_CALL_STOP:
                    m_DefMsg = Grobal2.MakeDefaultMsg(
                        Grobal2.SM_NATIVE_HORSE_CALL_STOP,
                        ProcessMsg.BaseObject, 0, 0, 0);
                    SendSocket(m_DefMsg);
                    break;
                case Grobal2.RM_SHANGMA_OK:
                    var horseFeatureBody = ProcessMsg.Payload as byte[]
                                           ?? BaseObject?.GetMobileFeature()
                                           ?? Array.Empty<byte>();
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SHANGMA_OK,
                        ProcessMsg.BaseObject, ProcessMsg.wParam,
                        horseFeatureBody.Length, 0);
                    SendSocket(m_DefMsg, horseFeatureBody);
                    break;
                case Grobal2.RM_NATIVE_INVITE_HORSE:
                    m_DefMsg = Grobal2.MakeDefaultMsg(
                        Grobal2.SM_INVITE_HORSE, ProcessMsg.BaseObject,
                        0, 0, 0);
                    SendSocket(m_DefMsg);
                    break;
                case Grobal2.RM_NATIVE_SHANGMA_OK2:
                    var horsePairBody = ProcessMsg.Payload as byte[]
                                        ?? Array.Empty<byte>();
                    m_DefMsg = Grobal2.MakeDefaultMsg(
                        Grobal2.SM_SHANGMA_OK2, ProcessMsg.BaseObject,
                        ProcessMsg.wParam, horsePairBody.Length,
                        ProcessMsg.nParam3);
                    SendSocket(m_DefMsg, horsePairBody);
                    break;
                case Grobal2.RM_NATIVE_XIAMA_OK:
                case Grobal2.RM_NATIVE_XIAMA_2:
                    var horseDismountBody = ProcessMsg.Payload as byte[]
                                             ?? Array.Empty<byte>();
                    m_DefMsg = Grobal2.MakeDefaultMsg(
                        ProcessMsg.wIdent == Grobal2.RM_NATIVE_XIAMA_OK
                            ? Grobal2.SM_XIAMA_OK
                            : Grobal2.SM_XIAMA_2,
                        ProcessMsg.BaseObject, ProcessMsg.wParam,
                        horseDismountBody.Length, ProcessMsg.nParam3);
                    SendSocket(m_DefMsg, horseDismountBody);
                    break;
                case Grobal2.RM_41:
                    if (ProcessMsg.BaseObject != this.ObjectId)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_41, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                        SendSocket(m_DefMsg);
                    }
                    break;
                case Grobal2.RM_43:
                    if (ProcessMsg.BaseObject != this.ObjectId)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_43, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                        SendSocket(m_DefMsg);
                    }
                    break;
                case Grobal2.RM_TURN:
                case Grobal2.RM_PUSH:
                case Grobal2.RM_RUSH:
                case Grobal2.RM_RUSHKUNG:
                    if (ProcessMsg.BaseObject != this.ObjectId || ProcessMsg.wIdent == Grobal2.RM_PUSH || ProcessMsg.wIdent == Grobal2.RM_RUSH || ProcessMsg.wIdent == Grobal2.RM_RUSHKUNG)
                    {
                        switch (ProcessMsg.wIdent)
                        {
                            case Grobal2.RM_PUSH:
                                m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_BACKSTEP, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, HUtil32.MakeWord(ProcessMsg.wParam, BaseObject.m_nLight));
                                break;
                            case Grobal2.RM_RUSH:
                                m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_RUSH, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, HUtil32.MakeWord(ProcessMsg.wParam, BaseObject.m_nLight));
                                break;
                            case Grobal2.RM_RUSHKUNG:
                                m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_RUSHKUNG, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, HUtil32.MakeWord(ProcessMsg.wParam, BaseObject.m_nLight));
                                break;
                            default:
                                m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_TURN, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, HUtil32.MakeWord(ProcessMsg.wParam, BaseObject.m_nLight));
                                break;
                        }
                        if (ProcessMsg.wIdent == Grobal2.RM_TURN && !string.IsNullOrEmpty(ProcessMsg.sMsg))
                        {
                            SendSocket(m_DefMsg, BuildMobileNewStateBody(BaseObject, ProcessMsg.sMsg));
                        }
                        else
                        {
                            SendSocket(m_DefMsg,
                                BuildMobileActorStateBody(BaseObject.GetFeature(this), BaseObject));
                        }
                    }
                    break;
                case Grobal2.RM_STRUCK:
                case Grobal2.RM_STRUCK_MAG:
                    if (ProcessMsg.wParam > 0)
                    {
                        if (ProcessMsg.BaseObject == ObjectId)
                        {
                            if (M2Share.ObjectManager.Get(ProcessMsg.nParam3) != null)
                            {
                                if (M2Share.ObjectManager.Get(ProcessMsg.nParam3).m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                {
                                    SetPKFlag(M2Share.ObjectManager.Get(ProcessMsg.nParam3));
                                }
                                SetLastHiter(M2Share.ObjectManager.Get(ProcessMsg.nParam3));
                            }
                            if (M2Share.CastleManager.IsCastleMember(this) != null && M2Share.ObjectManager.Get(ProcessMsg.nParam3) != null)
                            {
                                M2Share.ObjectManager.Get(ProcessMsg.nParam3).bo2B0 = true;
                                M2Share.ObjectManager.Get(ProcessMsg.nParam3).m_dw2B4Tick = HUtil32.GetTickCount();
                            }
                            m_nHealthTick = 0;
                            m_nSpellTick = 0;
                            DecreaseHealthSpellRecoveryStep(1);
                            m_dwStruckTick = HUtil32.GetTickCount();
                        }
                        if (ProcessMsg.BaseObject != 0)
                        {
                            if (ProcessMsg.BaseObject == ObjectId && M2Share.g_Config.boDisableSelfStruck || BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT && M2Share.g_Config.boDisableStruck)
                            {
                                BaseObject.SendRefMsg(Grobal2.RM_HEALTHSPELLCHANGED, 0, 0, 0, 0, "");
                            }
                            else
                            {
                                var struckMaxHp = ProcessMsg.nParam2;
                                var struckHp = ProcessMsg.nParam1;
                                if (struckMaxHp <= 0 || struckHp < 0 || struckHp > struckMaxHp)
                                {
                                    struckHp = BaseObject.m_WAbil.HP;
                                    struckMaxHp = BaseObject.m_WAbil.MaxHP;
                                }

                                m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_STRUCK, ProcessMsg.BaseObject, struckHp, struckMaxHp, ProcessMsg.wParam);
                                var struckBody = BuildMobileStruckBody(
                                    ProcessMsg.nParam3,
                                    ProcessMsg.wIdent == Grobal2.RM_STRUCK_MAG,
                                    struckHp,
                                    struckMaxHp,
                                    BaseObject.m_WAbil.MP,
                                    BaseObject.m_WAbil.MaxMP);
                                SendSocket(m_DefMsg, struckBody);
                            }
                        }
                    }
                    break;
                case Grobal2.RM_DEATH:
                    if (ProcessMsg.nParam3 == 1)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_NOWDEATH, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                        if (ProcessMsg.BaseObject == ObjectId)
                        {
                            if (M2Share.g_FunctionNPC != null)
                            {
                                M2Share.g_FunctionNPC.GotoLable(this, "@OnDeath", false);
                            }
                        }
                    }
                    else
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_DEATH, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                    }
                    SendSocket(m_DefMsg, BuildMobileActorStateBody(BaseObject.GetFeature(this), BaseObject));
                    break;
                case Grobal2.RM_DISAPPEAR:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_DISAPPEAR, ProcessMsg.BaseObject, 0, 0, 0);
                    SendSocket(m_DefMsg);
                    break;
                case Grobal2.RM_SKELETON:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SKELETON, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                    SendSocket(m_DefMsg,
                        BuildMobileActorStateBody(BaseObject.GetFeature(this), BaseObject));
                    break;
                case Grobal2.RM_USERNAME:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_USERNAME, ProcessMsg.BaseObject, GetCharColor(BaseObject), 0, 0);
                    SendSocket(m_DefMsg, ProcessMsg.sMsg);
                    break;
                case Grobal2.RM_WINEXP:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_WINEXP, m_Abil.Exp, HUtil32.LoWord(ProcessMsg.nParam1), HUtil32.HiWord(ProcessMsg.nParam1), ProcessMsg.wParam);
                    SendSocket(m_DefMsg);
                    break;
                case Grobal2.RM_LEVELUP:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_LEVELUP, m_Abil.Exp, m_Abil.Level, 0, 0);
                    SendSocket(m_DefMsg);
                    SendNativeAbilityPacket();
                    break;
                case Grobal2.RM_CHANGENAMECOLOR:
                    SendDefMessage(Grobal2.SM_CHANGENAMECOLOR, ProcessMsg.BaseObject, GetCharColor(BaseObject), 0, 0, "");
                    break;
                case Grobal2.RM_LOGON:
                    var logonBright = m_PEnvir.Flag.boDarkness || m_btBright != 1
                        ? (byte)(m_PEnvir.Flag.boDarkness ? 1 : 2)
                        : (byte)0;
                    if (m_PEnvir.Flag.boDayLight) logonBright = 0;
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_NEWMAP,
                        ObjectId, m_nCurrX, m_nCurrY, logonBright);
                    var mapName = GetLogonMapName(m_sMapName);
                    SendSocket(m_DefMsg, mapName);
                    SendLogon();
                    ClientQueryUserName(ObjectId, m_nCurrX, m_nCurrY);
                    RefUserState();
                    SendMapDescription();
                    break;
                case Grobal2.RM_HEAR:
                case Grobal2.RM_WHISPER:
                case Grobal2.RM_CRY:
                case Grobal2.RM_CATTLE_SYSMESSAGE:
                case Grobal2.RM_SYSMESSAGE:
                case Grobal2.RM_GROUPMESSAGE:
                case Grobal2.RM_SYSMESSAGE2:
                case Grobal2.RM_GUILDMESSAGE:
                case Grobal2.RM_SYSMESSAGE3:
                case Grobal2.RM_MOVEMESSAGE:
                case Grobal2.RM_MERCHANTSAY:
                case Grobal2.RM_COLORHEAR:
                    // 彩色文字 deliberately does NOT consult the block-public-chat
                    // bit. Native proves this twice: the RM handler for ident 105
                    // (0x6B4B3C) carries no obj+0xB9C test, unlike the ident-40
                    // handler at 0x6B4A63 (`test byte [eax+0xB9C],2 / jne`); and
                    // the per-recipient filter sub_6DC068 only recognises idents
                    // {40,102,104} (0x6DC07E/0x6DC084/0x6DC08A), so 105 falls
                    // through to the deliver exit. A listener who muted public
                    // chat still hears coloured speech.
                    if (ProcessMsg.wIdent == Grobal2.RM_HEAR
                        && (m_dwChatShieldMask & 0x02u) != 0)
                        break;
                    if (ProcessMsg.wIdent == Grobal2.RM_CRY
                        && (m_dwChatShieldMask & 0x04u) != 0)
                        break;
                    if (ProcessMsg.wIdent == Grobal2.RM_GUILDMESSAGE
                        && (m_dwChatShieldMask & 0x08u) != 0)
                        break;
                    switch (ProcessMsg.wIdent)
                    {
                        case Grobal2.RM_HEAR:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HEAR, ProcessMsg.BaseObject, HUtil32.MakeWord(ProcessMsg.nParam1, ProcessMsg.nParam2), 0, 1);
                            break;
                        case Grobal2.RM_COLORHEAR:
                            // 0x6C9485 mov cx,0x69 -- same payload shape as
                            // SM_HEAR, only the ident and the colour differ.
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_COLORHEAR,
                                ProcessMsg.BaseObject,
                                HUtil32.MakeWord(ProcessMsg.nParam1, ProcessMsg.nParam2),
                                0, 1);
                            break;
                        case Grobal2.RM_WHISPER:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_WHISPER, ProcessMsg.BaseObject, HUtil32.MakeWord(ProcessMsg.nParam1, ProcessMsg.nParam2), 0, 1);
                            break;
                        case Grobal2.RM_CRY:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_CRY,
                                ProcessMsg.BaseObject, 0x9700, 0, 1);
                            break;
                        case Grobal2.RM_CATTLE_SYSMESSAGE:
                            m_DefMsg = Grobal2.MakeDefaultMsg(
                                Grobal2.SM_CATTLE_SYSMESSAGE,
                                ProcessMsg.BaseObject, 0, 0,
                                ProcessMsg.wParam);
                            break;
                        case Grobal2.RM_SYSMESSAGE:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SYSMESSAGE, ProcessMsg.BaseObject, HUtil32.MakeWord(ProcessMsg.nParam1, ProcessMsg.nParam2), 0, 1);
                            break;
                        case Grobal2.RM_GROUPMESSAGE:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SYSMESSAGE, ProcessMsg.BaseObject, HUtil32.MakeWord(ProcessMsg.nParam1, ProcessMsg.nParam2), 0, 1);
                            break;
                        case Grobal2.RM_GUILDMESSAGE:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_GUILDMESSAGE, ProcessMsg.BaseObject, 0xFFD4, 0, 1);
                            break;
                        case Grobal2.RM_MERCHANTSAY:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_MERCHANTSAY, ProcessMsg.BaseObject, HUtil32.MakeWord(ProcessMsg.nParam1, ProcessMsg.nParam2), 0, 1);
                            break;
                        case Grobal2.RM_MOVEMESSAGE:
                            this.m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_MOVEMESSAGE, ProcessMsg.BaseObject, HUtil32.MakeWord(ProcessMsg.nParam1, ProcessMsg.nParam2), ProcessMsg.nParam3, ProcessMsg.wParam);
                            break;
                    }
                    if (ProcessMsg.wIdent == Grobal2.RM_MERCHANTSAY &&
                        ProcessMsg.Payload is byte[] rawMerchantBody)
                        SendSocket(m_DefMsg, rawMerchantBody);
                    else
                        SendSocket(m_DefMsg, ProcessMsg.sMsg);
                    break;
                case Grobal2.RM_ABILITY:
                    SendNativeAbilityPacket();
                    break;
                case Grobal2.RM_HEALTHSPELLCHANGED:
                    {
                        var body = new byte[16];
                        if (!BaseObject.m_boGhost)
                        {
                            using var ms = new MemoryStream(body);
                            using var bw = new BinaryWriter(ms);
                            bw.Write(BaseObject.m_WAbil.HP);
                            bw.Write(BaseObject.m_WAbil.MaxHP);
                            bw.Write(BaseObject.m_WAbil.MP);
                            bw.Write(BaseObject.m_WAbil.MaxMP);
                        }
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HEALTHSPELLCHANGED,
                            ProcessMsg.BaseObject, HUtil32.LoWord(BitConverter.ToInt32(body, 0)),
                            HUtil32.LoWord(BitConverter.ToInt32(body, 8)),
                            HUtil32.LoWord(BitConverter.ToInt32(body, 4)));
                        SendSocket(m_DefMsg, body);
                    }
                    break;
                case Grobal2.RM_DAYCHANGING:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_DAYCHANGING, 0, m_btBright, DayBright(), 0);
                    SendSocket(m_DefMsg);
                    break;
                case Grobal2.RM_ITEMSHOW:
                {
                    var rawName = ProcessMsg.sMsg ?? "";
                    var atIdx = rawName.LastIndexOf('@');
                    var name = atIdx >= 0 ? rawName.Substring(0, atIdx) : rawName;
                    var state = 0;
                    if (atIdx >= 0 && atIdx + 1 < rawName.Length && int.TryParse(rawName.Substring(atIdx + 1), out var parsed))
                    {
                        state = parsed;
                    }

                    using var memoryStream = new MemoryStream();
                    using var writer = new BinaryWriter(memoryStream);
                    WriteClientFixedGbkString(writer, name, 15);
                    AlignWriter(writer, 4);
                    var mapItem = m_PEnvir?.GetItem(ProcessMsg.nParam2, ProcessMsg.nParam3, ProcessMsg.nParam1);
                    var itemOwner = mapItem?.OfBaseObject as TBaseObject;
                    writer.Write(itemOwner?.ObjectId ?? 0);
                    writer.Write((byte)Math.Max(byte.MinValue, Math.Min(byte.MaxValue, state)));
                    AlignWriter(writer, 4);

                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ITEMSHOW, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, ProcessMsg.wParam);
                    SendSocket(m_DefMsg, memoryStream.ToArray());
                    break;
                }
                case Grobal2.RM_ITEMHIDE:
                    SendDefMessage(Grobal2.SM_ITEMHIDE, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, 0, "");
                    break;
                case Grobal2.RM_DOOROPEN:
                    SendDefMessage(Grobal2.SM_OPENDOOR_OK, 0, ProcessMsg.nParam1, ProcessMsg.nParam2, 0, "");
                    break;
                case Grobal2.RM_DOORCLOSE:
                    SendDefMessage(Grobal2.SM_CLOSEDOOR, 0, ProcessMsg.nParam1, ProcessMsg.nParam2, 0, "");
                    break;
                case Grobal2.RM_SENDUSEITEMS:
                    // Send equipment data to client (both PC and mobile)
                    SendUseitems();
                    break;
                case Grobal2.RM_WEIGHTCHANGED:
                    SendDefMessage(Grobal2.SM_WEIGHTCHANGED, m_WAbil.Weight, m_WAbil.WearWeight, m_WAbil.HandWeight, 0, "");
                    break;
                case Grobal2.RM_FEATURECHANGED:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_FEATURECHANGED,
                        ProcessMsg.BaseObject, HUtil32.LoWord(ProcessMsg.nParam1),
                        HUtil32.HiWord(ProcessMsg.nParam1), 0);
                    var featureBody = ProcessMsg.Payload as byte[] ?? Array.Empty<byte>();
                    SendSocket(m_DefMsg, featureBody);
                    break;
                case Grobal2.RM_CLEAROBJECTS:
                case Grobal2.RM_NATIVE_CLEAROBJECTS:
                    SendDefMessage(Grobal2.SM_CLEAROBJECTS, 0, 0, 0, 0, "");
                    break;
                case Grobal2.RM_CHANGEMAP:
                case Grobal2.RM_NATIVE_CHANGEMAP:
                    SendDefMessage(Grobal2.SM_CHANGEMAP, ObjectId, m_nCurrX, m_nCurrY, DayBright(), ProcessMsg.sMsg);
                    RefUserState();
                    SendMapDescription();
                    break;
                case Grobal2.RM_BUTCH:
                    if (ProcessMsg.BaseObject != 0)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_BUTCH, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                        SendSocket(m_DefMsg);
                    }
                    break;
                case Grobal2.RM_MAGICFIRE:
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_MAGICFIRE,
                            ProcessMsg.BaseObject, HUtil32.LoWord(ProcessMsg.nParam1),
                            HUtil32.HiWord(ProcessMsg.nParam1), ProcessMsg.wParam);
                        var body = new byte[8];
                        Buffer.BlockCopy(BitConverter.GetBytes(ProcessMsg.nParam3), 0, body, 0, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(ProcessMsg.nParam2), 0, body, 4, 4);
                        SendSocket(m_DefMsg, body);
                    }
                    break;
                case Grobal2.RM_MAGICFIREFAIL:
                    SendDefMessage(Grobal2.SM_MAGICFIRE_FAIL, ProcessMsg.BaseObject, 0, 0, 0, "");
                    break;
                case Grobal2.RM_SENDMYMAGIC:
                    SendUseMagic();
                    break;
                case Grobal2.RM_USERAZSETUP:
                    break;
                case Grobal2.RM_MAGIC_LVEXP:
                {
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_MAGIC_LVEXP,
                        ProcessMsg.wParam, HUtil32.LoWord(ProcessMsg.nParam1),
                        HUtil32.LoWord(ProcessMsg.nParam2), HUtil32.HiWord(ProcessMsg.nParam2));
                    if (ProcessMsg.nParam3 != 0)
                    {
                        SendSocket(m_DefMsg, BitConverter.GetBytes(ProcessMsg.nParam3));
                    }
                    else
                    {
                        SendSocket(m_DefMsg);
                    }
                    break;
                }
                case Grobal2.RM_DURACHANGE:
                    SendDefMessage(Grobal2.SM_DURACHANGE, ProcessMsg.nParam1, ProcessMsg.wParam, HUtil32.LoWord(ProcessMsg.nParam2), HUtil32.HiWord(ProcessMsg.nParam2), "");
                    break;
                case Grobal2.RM_MASTERRELATION:
                    m_DefMsg = BuildMasterRelationPacket(ProcessMsg);
                    SendSocket(m_DefMsg,
                        BuildMasterRelationBody(ProcessMsg));
                    break;
                case Grobal2.RM_MERCHANTDLGCLOSE:
                    SendDefMessage(Grobal2.SM_MERCHANTDLGCLOSE, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_SENDGOODSLIST:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SENDGOODSLIST,
                        ProcessMsg.nParam1, 1, 0,
                        HUtil32.LoWord(ProcessMsg.nParam2));
                    var goodsBody = GetQueuedPayloadBytes(ProcessMsg);
                    SendSocket(m_DefMsg, goodsBody);
                    break;
                case Grobal2.RM_SENDUSERSELL:
                    SendDefMessage(Grobal2.SM_SENDUSERSELL, ProcessMsg.nParam1, ProcessMsg.nParam2, 0, 0, ProcessMsg.sMsg);
                    break;
                case Grobal2.RM_SENDBUYPRICE:
                    SendDefMessage(Grobal2.SM_SENDBUYPRICE, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_USERSELLITEM_OK:
                    SendDefMessage(Grobal2.SM_USERSELLITEM_OK, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_USERSELLITEM_FAIL:
                    SendDefMessage(Grobal2.SM_USERSELLITEM_FAIL, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_BUYITEM_SUCCESS:
                    SendDefMessage(Grobal2.SM_BUYITEM_SUCCESS, ProcessMsg.nParam1, HUtil32.LoWord(ProcessMsg.nParam2), HUtil32.HiWord(ProcessMsg.nParam2), 0, "");
                    break;
                case Grobal2.RM_BUYITEM_FAIL:
                    SendDefMessage(Grobal2.SM_BUYITEM_FAIL, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_SENDDETAILGOODSLIST:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SENDDETAILGOODSLIST,
                        ProcessMsg.nParam1, 0, HUtil32.LoWord(ProcessMsg.nParam3),
                        HUtil32.LoWord(ProcessMsg.nParam2));
                    var detailGoodsBody = GetQueuedPayloadBytes(ProcessMsg);
                    SendSocket(m_DefMsg, detailGoodsBody);
                    break;
                case Grobal2.RM_GOLDCHANGED:
                    SendDefMessage(Grobal2.SM_GOLDCHANGED, m_nGold, 0, 0, 0, "");
                    break;
                case Grobal2.RM_GAMEGOLDCHANGED:
                    break;
                case Grobal2.RM_CHANGELIGHT:
                    SendDefMessage(Grobal2.SM_CHANGELIGHT, ProcessMsg.BaseObject, 4, 0, 0, "");
                    break;
                case Grobal2.RM_LAMPCHANGEDURA:
                    SendDefMessage(Grobal2.SM_LAMPCHANGEDURA, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_CHARSTATUSCHANGED:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_CHARSTATUSCHANGED,
                        ProcessMsg.BaseObject, ProcessMsg.wParam,
                        HUtil32.HiWord(ProcessMsg.nParam1),
                        HUtil32.LoWord(ProcessMsg.nParam1));
                    var charStatusBody = ProcessMsg.Payload as byte[] ?? Array.Empty<byte>();
                    SendSocket(m_DefMsg, charStatusBody);
                    break;
                case Grobal2.RM_GROUPCANCEL:
                    SendDefMessage(Grobal2.SM_GROUPCANCEL, 0, 0, 0, 0, "");
                    m_boAllowGroup = false;
                    SendDefMessage(Grobal2.SM_GROUPMODECHANGED, 0, 0, 0, 0, "");
                    break;
                case Grobal2.RM_SENDUSERREPAIR:
                case Grobal2.RM_SENDUSERSREPAIR:
                    SendDefMessage(Grobal2.SM_SENDUSERREPAIR, ProcessMsg.nParam1, ProcessMsg.nParam2, 0, 0, "");
                    break;
                case Grobal2.RM_USERREPAIRITEM_OK:
                    SendDefMessage(Grobal2.SM_USERREPAIRITEM_OK, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, 0, "");
                    break;
                case Grobal2.RM_SENDREPAIRCOST:
                    SendDefMessage(Grobal2.SM_SENDREPAIRCOST, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_USERREPAIRITEM_FAIL:
                    SendDefMessage(Grobal2.SM_USERREPAIRITEM_FAIL, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_USERSTORAGEITEM:
                    if (ProcessMsg.nParam2 == 1)
                        OpenNativeAccountStorageForDeposit(ProcessMsg.nParam1);
                    else
                    {
                        SendDefMessage(Grobal2.SM_SENDUSERSTORAGEITEM, ProcessMsg.nParam1, ProcessMsg.nParam2, 0, 0, "");
                        SendSaveItemList(ProcessMsg.nParam1);
                    }
                    break;
                case Grobal2.RM_USERGETBACKITEM:
                    if (ProcessMsg.nParam2 == 2)
                        OpenNativeAccountStorageForRetrieval(ProcessMsg.nParam1);
                    else
                        SendSaveItemList(ProcessMsg.nParam1);
                    break;
                case Grobal2.RM_SENDDELITEMLIST:
                    if (ProcessMsg.Payload is IList<TDeleteItem> delItemList)
                    {
                        SendDelItemList(delItemList, ProcessMsg.nParam1);
                    }
                    break;
                case Grobal2.RM_USERMAKEDRUGITEMLIST:
                    SendDefMessage(Grobal2.SM_SENDUSERMAKEDRUGITEMLIST, ProcessMsg.nParam1, ProcessMsg.nParam2, 0, 0, ProcessMsg.sMsg);
                    break;
                case Grobal2.RM_MAKEDRUG_SUCCESS:
                    SendDefMessage(Grobal2.SM_MAKEDRUG_SUCCESS, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_MAKEDRUG_FAIL:
                    SendDefMessage(Grobal2.SM_MAKEDRUG_FAIL, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_ALIVE:
                    if (BaseObject != null)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ALIVE, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                        SendSocket(m_DefMsg,
                            BuildMobileActorStateBody(BaseObject.GetFeature(this), BaseObject));
                    }
                    break;
                case Grobal2.RM_DIGUP:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_DIGUP, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, HUtil32.MakeWord(ProcessMsg.wParam, BaseObject.m_nLight));
                    SendSocket(m_DefMsg,
                        BuildMobileActorStateBody(BaseObject.GetFeature(this), BaseObject));
                    break;
                case Grobal2.RM_DIGDOWN:
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_DIGDOWN, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, 0);
                    SendSocket(m_DefMsg);
                    break;
                case Grobal2.RM_FLYAXE:
                    if (M2Share.ObjectManager.Get(ProcessMsg.nParam3) != null)
                    {
                        var MessageBodyW = new TMessageBodyW();
                        MessageBodyW.Param1 = (ushort)M2Share.ObjectManager.Get(ProcessMsg.nParam3).m_nCurrX;
                        MessageBodyW.Param2 = (ushort)M2Share.ObjectManager.Get(ProcessMsg.nParam3).m_nCurrY;
                        MessageBodyW.Tag1 = HUtil32.LoWord(ProcessMsg.nParam3);
                        MessageBodyW.Tag2 = HUtil32.HiWord(ProcessMsg.nParam3);
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_FLYAXE, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                        SendSocket(m_DefMsg, MessageBodyW.GetBuffer());
                    }
                    break;
                case Grobal2.RM_LIGHTING:
                    if (M2Share.ObjectManager.Get(ProcessMsg.nParam3) != null)
                    {
                        MessageBodyWL = new TMessageBodyWL();
                        MessageBodyWL.lParam1 = M2Share.ObjectManager.Get(ProcessMsg.nParam3).m_nCurrX;
                        MessageBodyWL.lParam2 = M2Share.ObjectManager.Get(ProcessMsg.nParam3).m_nCurrY;
                        MessageBodyWL.lTag1 = ProcessMsg.nParam3;
                        MessageBodyWL.lTag2 = ProcessMsg.wParam;
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_LIGHTING, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, BaseObject.m_btDirection);
                        SendSocket(m_DefMsg, MessageBodyWL.GetBuffer());
                    }
                    break;
                case Grobal2.RM_10205:
                    SendDefMessage(Grobal2.SM_716, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.nParam3, "");
                    break;
                case Grobal2.RM_CHANGEGUILDNAME:
                    SendChangeGuildName();
                    break;
                case Grobal2.RM_BUILDGUILD_OK:
                    SendDefMessage(Grobal2.SM_BUILDGUILD_OK, 0, 0, 0, 0, "");
                    break;
                case Grobal2.RM_BUILDGUILD_FAIL:
                    SendDefMessage(Grobal2.SM_BUILDGUILD_FAIL, ProcessMsg.nParam1, 0, 0, 0, "");
                    break;
                case Grobal2.RM_DONATE_OK:
                    // Native CODE has zero 16-bit dx/cx loads of 764 (0x02FC)
                    // reaching a send slot. srv_AppearTimes.ini 764=0.
                    break;
                case Grobal2.RM_MYSTATUS:
                    SendDefMessage(Grobal2.SM_MYSTATUS, 0, (short)GetMyStatus(), 0, 0, "");
                    break;
                case Grobal2.RM_MENU_OK:
                    SendDefMessage(Grobal2.SM_MENU_OK, ProcessMsg.nParam1, 0, 0, 0, ProcessMsg.sMsg);
                    break;
                case Grobal2.RM_SPACEMOVE_FIRE:
                case Grobal2.RM_SPACEMOVE_FIRE2:
                    if (ProcessMsg.wIdent == Grobal2.RM_SPACEMOVE_FIRE &&
                        ProcessMsg.BaseObject == ObjectId)
                    {
                        break;
                    }
                    if (ProcessMsg.wIdent == Grobal2.RM_SPACEMOVE_FIRE)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SPACEMOVE_HIDE, ProcessMsg.BaseObject, 0, 0, 0);
                    }
                    else
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SPACEMOVE_HIDE2, ProcessMsg.BaseObject, 0, 0, 0);
                    }
                    SendSocket(m_DefMsg);
                    break;
                case Grobal2.RM_SPACEMOVE_SHOW:
                case Grobal2.RM_SPACEMOVE_SHOW2:
                    if (ProcessMsg.wIdent == Grobal2.RM_SPACEMOVE_SHOW)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SPACEMOVE_SHOW, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, HUtil32.MakeWord(ProcessMsg.wParam, BaseObject.m_nLight));
                    }
                    else
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SPACEMOVE_SHOW2, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, HUtil32.MakeWord(ProcessMsg.wParam, BaseObject.m_nLight));
                    }
                    SendSocket(m_DefMsg,
                        BuildMobileActorStateBody(BaseObject.GetFeature(this), BaseObject));
                    break;
                case Grobal2.RM_RECONNECTION:
                    m_boReconnection = true;
                    // Native CODE has zero 16-bit dx/cx loads of 802 (0x0322)
                    // reaching a send slot. srv_AppearTimes.ini 802=0.
                    break;
                case Grobal2.RM_HIDEEVENT:
                    SendDefMessage(Grobal2.SM_HIDEEVENT, ProcessMsg.nParam1, ProcessMsg.wParam, ProcessMsg.nParam2, ProcessMsg.nParam3, "");
                    break;
                case Grobal2.RM_SHOWEVENT:
                    if (ProcessMsg.Payload is not Event mapEvent)
                    {
                        break;
                    }
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SHOWEVENT, ProcessMsg.nParam1, ProcessMsg.wParam, ProcessMsg.nParam2, ProcessMsg.nParam3);
                    SendSocket(m_DefMsg, BuildShowEventBody(mapEvent, ProcessMsg.nParam2));
                    break;
                case Grobal2.RM_ADJUST_BONUS:
                    SendAdjustBonus();
                    break;
                case Grobal2.RM_10401:
                    if (ProcessMsg.Payload is TSlaveInfo slaveInfo)
                    {
                        ChangeServerMakeSlave(slaveInfo);
                    }
                    break;
                case Grobal2.RM_OPENHEALTH:
                    SendDefMessage(Grobal2.SM_OPENHEALTH, ProcessMsg.BaseObject, BaseObject.m_WAbil.HP, BaseObject.m_WAbil.MaxHP, 0, "");
                    break;
                case Grobal2.RM_CLOSEHEALTH:
                    SendDefMessage(Grobal2.SM_CLOSEHEALTH, ProcessMsg.BaseObject, 0, 0, 0, "");
                    break;
                case Grobal2.RM_BREAKWEAPON:
                    SendDefMessage(Grobal2.SM_BREAKWEAPON, ProcessMsg.BaseObject, 0, 0, 0, "");
                    break;
                case Grobal2.RM_10414:
                    var gaugeHp = BaseObject.m_WAbil.HP;
                    var gaugeMaxHp = BaseObject.m_WAbil.MaxHP;
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_INSTANCEHEALGUAGE,
                        ProcessMsg.BaseObject, 1, HUtil32.LoWord(gaugeMaxHp),
                        HUtil32.LoWord(gaugeHp));
                    SendSocket(m_DefMsg,
                        BuildInstanceHealGaugeBody(gaugeHp, gaugeMaxHp));
                    break;
                case Grobal2.RM_CHANGEFACE:
                    if (ProcessMsg.nParam1 != 0 && ProcessMsg.nParam2 != 0)
                    {
                        SendDefMessage(Grobal2.SM_CHANGEFACE, ProcessMsg.nParam2,
                            HUtil32.LoWord(ProcessMsg.nParam1),
                            HUtil32.HiWord(ProcessMsg.nParam1), 0, "");
                    }
                    break;
                case Grobal2.RM_PASSWORD:
                    SendDefMessage(Grobal2.SM_PASSWORD, 0, 0, 0, 0, "");
                    break;
                case Grobal2.RM_PLAYDICE:
                    var diceText = HUtil32.GetBytes(ProcessMsg.sMsg);
                    if (diceText.Length == 0 || diceText.Length >= 0x20)
                    {
                        break;
                    }
                    MessageBodyWL = new TMessageBodyWL();
                    MessageBodyWL.lParam1 = ProcessMsg.nParam1;
                    MessageBodyWL.lParam2 = ProcessMsg.nParam2;
                    MessageBodyWL.lTag1 = ProcessMsg.nParam3;
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_PLAYDICE, ProcessMsg.BaseObject, ProcessMsg.wParam, 0, 0);
                    var diceHeader = MessageBodyWL.GetBuffer();
                    var diceBody = new byte[diceHeader.Length + diceText.Length];
                    Buffer.BlockCopy(diceHeader, 0, diceBody, 0, diceHeader.Length);
                    Buffer.BlockCopy(diceText, 0, diceBody, diceHeader.Length, diceText.Length);
                    SendSocket(m_DefMsg, diceBody);
                    break;
                // === Task/Quest System ===
                case Grobal2.CM_QUEST_ORDER:
                    HandleNativeQuestOrder(ProcessMsg.nParam1,
                        unchecked((byte)ProcessMsg.nParam2));
                    break;
                case Grobal2.CM_QUERY_TASK_ALL:
                    SendAllTaskDetails(ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_QUERY_TASK_DETAIL:
                    SendTaskDetailAndProgress(ProcessMsg.nParam2, ProcessMsg.nParam1);
                    break;
                case Grobal2.CM_DO_TASK_COMMAND:
                    ExecuteTaskCommand(ProcessMsg.nParam2, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_QUERY_SINGLE_TASK:
                    AddTaskToUIList(ProcessMsg.nParam2, 1);
                    break;

                case Grobal2.CM_FETCH_MAIL_LIST:
                    ClientFetchNativeMailList(ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_FETCH_MAIL_INFO:
                    ClientFetchNativeMailInfo(ProcessMsg.nParam1, ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_FETCH_ATTACH:
                    ClientFetchNativeMailAttachments(ProcessMsg.nParam1, ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_DEL_MAIL:
                    ClientDeleteNativeMail(ProcessMsg.nParam1, ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_FETCH_ATTACH_OFFTM:
                    ClientFetchNativeMailAttachmentsOffline(ProcessMsg.nParam1);
                    break;
                case Grobal2.CM_CLEAR_ALLMAIL:
                    ClientClearAllNativeMail(ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_SYSTEM_NEWMAIL:
                    TriggerNativeMailQuest();
                    break;

                // Native stall WRITE ops: route to NativeStallWriteTransaction + the injected store when
                // a faithful context can be built; otherwise keep the existing RejectUnavailableStallRequest
                // fallback. The in-memory stall manager (owner->record + srvData codec + recovery) is not yet
                // modeled, so TryRouteNativeStallWrite currently returns false and behaviour is unchanged (#83).
                case Grobal2.CM_QUERY_STALL:
                    // Browse/list READ (sub_6E7B2C). Gated on the injected manager (dormant by default ->
                    // the existing RejectUnavailableStallRequest fallback, behaviour unchanged).
                    if (!TryHandleNativeStallQuery(ProcessMsg, Grobal2.SM_QUERY_STALL))
                        RejectUnavailableStallRequest(Grobal2.SM_QUERY_STALL, -3);
                    break;
                case Grobal2.CM_SET_STALL_TIMELV:
                    if (!TryRouteNativeStallWrite(NativeStallOp.SetTimeLevel, ProcessMsg, Grobal2.SM_SET_STALL_TIMELV))
                        RejectUnavailableStallRequest();
                    break;
                case Grobal2.CM_SET_STALL_NAME:
                    if (!TryRouteNativeStallWrite(NativeStallOp.SetName, ProcessMsg, Grobal2.SM_SET_STALL_NAME))
                        RejectUnavailableStallRequest();
                    break;
                case Grobal2.CM_DEL_STALLITEM:
                    if (!TryRouteNativeStallWrite(NativeStallOp.DelItem, ProcessMsg, Grobal2.SM_DEL_STALLITEM))
                        RejectUnavailableStallRequest();
                    break;
                case Grobal2.CM_PAUSE_STALL:
                    if (!TryRouteNativeStallWrite(NativeStallOp.PauseStall, ProcessMsg, Grobal2.SM_PAUSE_STALL))
                        RejectUnavailableStallRequest();
                    break;
                // These IDs are declared by the client, but the native M2
                // dispatcher routes both entries to its silent default branch.
                case Grobal2.CM_CANCEL_STALL:
                case Grobal2.CM_QUERY_STALL_STATUS:
                    break;
                case Grobal2.CM_ADD_STALLITEM:
                    if (!TryRouteNativeStallWrite(NativeStallOp.AddItem, ProcessMsg, Grobal2.SM_ADD_STALLITEM))
                        RejectUnavailableStallRequest(Grobal2.SM_ADD_STALLITEM, -1);
                    break;
                case Grobal2.CM_START_STALL:
                    if (!TryRouteNativeStallWrite(NativeStallOp.StartStall, ProcessMsg, Grobal2.SM_START_STALL))
                        RejectUnavailableStallRequest(Grobal2.SM_START_STALL, -4);
                    break;
                case Grobal2.CM_BUY_STALLITEM:
                    if (!TryRouteNativeStallWrite(NativeStallOp.BuyItem, ProcessMsg, Grobal2.SM_BUY_STALLITEM))
                        RejectUnavailableStallRequest(Grobal2.SM_BUY_STALLITEM, -5);
                    break;
                case Grobal2.CM_MESSAGE_STALL:
                    if (!TryRouteNativeStallWrite(NativeStallOp.MessageStall, ProcessMsg, Grobal2.SM_MESSAGE_STALL))
                        RejectUnavailableStallRequest(Grobal2.SM_MESSAGE_STALL, -1);
                    break;

                case Grobal2.CM_STRENGTHEN_EQUIP_QUEST:
                    // idat 2026-08-02 (staging update_clothes_4637_ida_work/wf2_out.txt):
                    // DEFINITIVELY DEAD in native. sub_6D7D68 case 4465 enqueues onto the equip-synthesis
                    // manager dword_7DC210 via sub_6103DC, but that enqueue is gated on [mgr+0x10] != 0
                    // which is NEVER set (ctor sub_60F404 leaves it 0; dword_7DC210 has no writer xref
                    // anywhere; sub_610E88 is a finalizer, not a setter). The recipe table [mgr+0x24] is
                    // also created empty and no config loader populates it — SuperEquipSmeltNew.txt feeds
                    // a DIFFERENT container (UserEngine[+0x3C4]). So the queue is permanently disabled,
                    // handler sub_60F5C0 is never reached, and native sends the client NOTHING (no SM,
                    // no text). Faithful match: silent no-op. The old red SysMsg + SM were C# inventions.
                    // The dormant recipe-query gate stays OFF and MUST never be enabled (enabling it would
                    // send a response native never sends); when off the call below is a pure no-op.
                    TryClientStrengthenEquipQuestGated(ProcessMsg);
                    break;

                case Grobal2.CM_STRENGTHEN_EQUIP:
                    // idat 2026-08-02: same permanently-disabled enqueue (case 4466 -> sub_6103DC, gate
                    // [mgr+0x10] never set). Handler sub_60F7AC is never reached; native sends the client
                    // NOTHING. Faithful match: silent no-op (the SysMsg + SM here were C# inventions).
                    break;

                case Grobal2.CM_UPDATE_CLOTHES:
                    SendDefMessage(Grobal2.SM_UPDATE_CLOTHES, 0, 0, 0, 0, "");
                    break;

                // === Shop / Misc ===
                case Grobal2.CM_REQSEESHOP:
                {
                    ClientQueryWhitePigMall(ProcessMsg.nParam1);
                }
                break;

                case Grobal2.CM_RENEWSEESHOP:
                {
                    ClientRefreshWhitePigMall(ProcessMsg.nParam1);
                }
                break;

                case Grobal2.CM_DOSHOP:
                {
                    ClientBuyWhitePigMall(ProcessMsg.sMsg, ProcessMsg.nParam1);
                }
                break;

                // === Title / NPC / Item Commit ===
                case Grobal2.CM_QUERY_TITLE:
                    break;

                case Grobal2.CM_QUERY_MAP_NPC:
                    SendMapNpcList(ProcessMsg);
                    break;

                case Grobal2.CM_COMMIT_ITEM:
                {
                    if (m_boDeath || m_boGhost || M2Share.PasEngine == null)
                    {
                        break;
                    }

                    var npc = GetMerchantQueryNpc(ProcessMsg.nParam1);
                    if (npc == null ||
                        !(npc.m_boIsHide || npc.m_PEnvir == m_PEnvir &&
                            Math.Abs(npc.m_nCurrX - m_nCurrX) < 15 &&
                            Math.Abs(npc.m_nCurrY - m_nCurrY) < 15))
                    {
                        break;
                    }

                    var clientItemId = HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3);
                    var commitItem = FindClientItemIn(m_ItemList, clientItemId, false)
                                     ?? FindClientItemIn(m_ItemList, clientItemId, true);
                    if (commitItem == null || !string.Equals(ItmUnit.GetItemName(commitItem),
                            ProcessMsg.sMsg, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    M2Share.PasEngine.TryCallNpcItemProcedure(
                        npc, "CommitItem", this, commitItem, out _,
                        PasValue.FromInt(ProcessMsg.wParam & 0xFFFF));
                }
                break;

                case Grobal2.CM_QUERY_FOCUS_ITEM:
                    break;

                // === Hero System Client Messages (CM_HERO_*) ===
                case Grobal2.CM_HERO_LOGON:
                    if (ProcessMsg.nParam1 == ObjectId)
                    {
                        if (m_HeroObject == null)
                            HeroDataService.RequestLoad(this,
                                (byte)ProcessMsg.nParam2, (byte)ProcessMsg.nParam3);
                    }
                    break;
                case Grobal2.CM_HERO_LOGOUT:
                    if (m_HeroObject != null && ProcessMsg.nParam1 == m_HeroObject.ObjectId &&
                        HUtil32.GetTickCount() - m_dwHeroLogoutTick >= 10_000)
                    {
                        m_dwHeroLogoutTick = HUtil32.GetTickCount();
                        if (M2Share.UserEngine?.RemoveHero(this) == true)
                            SendDefMessage(Grobal2.SM_HERO_LOGOUT, 0, 0, 0, 0, "");
                    }
                    break;
                case Grobal2.CM_SECHERO_PRACTICE:
                    ClientSecHeroPractice((byte)ProcessMsg.nParam3,
                        (byte)ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_HERO_TOHEROBAG:
                    if (m_HeroObject != null) ClientHeroMoveToHeroBag(ProcessMsg);
                    break;
                case Grobal2.CM_HERO_TOHUMBAG:
                    if (m_HeroObject != null) ClientHeroMoveToHumBag(ProcessMsg);
                    break;
                case Grobal2.CM_HERO_TAKEON:
                    if (m_HeroObject != null) m_HeroObject.ClientHeroTakeOn(ProcessMsg);
                    break;
                case Grobal2.CM_HERO_TAKEOFF:
                    if (m_HeroObject != null) m_HeroObject.ClientHeroTakeOff(ProcessMsg);
                    break;
                case Grobal2.CM_HERO_EAT:
                    if (m_HeroObject != null) m_HeroObject.ClientHeroUseItem(ProcessMsg);
                    break;
                case Grobal2.CM_HERO_APPTARG:
                    if (m_HeroObject != null) m_HeroObject.ClientHeroAppTarg(ProcessMsg);
                    break;
                case Grobal2.CM_HERO_DROPITEM:
                    if (m_HeroObject != null) m_HeroObject.ClientHeroDropItem(ProcessMsg);
                    break;
                case Grobal2.CM_HERO_CHGSTATE:
                    if (m_HeroObject != null) m_HeroObject.ClientHeroChgState(ProcessMsg);
                    break;
                case Grobal2.CM_HERO_POWERUP:
                    if (HasNativeActiveState(0x33) || HasNativeActiveState(0x34))
                        break;
                    CancelNativeChannelMagic();
                    CancelNativeLocationChannelMagic();
                    CancelNativeType51PendingForTimedAbility();
                    if (m_PEnvir == null ||
                        !m_PEnvir.IsSkillAllowedAt(m_nCurrX, m_nCurrY, 0))
                        break;
                    if (m_HeroObject == null ||
                        m_HeroObject.m_btNativeUnionState != 1)
                        break;
                    m_nNativeUnionActivationCarrier = 0;
                    m_HeroObject.ClientHeroPowerUp(ProcessMsg);
                    break;
                case Grobal2.CM_HERO_SKILL_HOTKEY:
                    if (m_HeroObject != null)
                        m_HeroObject.ClientHeroSkillHotkey(ProcessMsg.nParam1, ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_MERCHANT_QUERY:
                    ClientMerchantQuery(ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_PILEUPITEM:
                    ClientPileUpItem(ProcessMsg.nParam1,
                        HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3), ProcessMsg.wParam);
                    break;
                case Grobal2.CM_SPLITITEM:
                    ClientSplitItem(ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                    break;

                // TODO: SM_TEST(65037) — test message, not used in production
                // TODO: SM_ACTION_MIN(65070)/MAX(65071)/2_MIN(65072)/2_MAX(65073) — action range boundary constants

                // === 战神 client combat/visual RM_ handlers ===
                case Grobal2.RM_WWJATTACK:
                case Grobal2.RM_WSJATTACK:
                case Grobal2.RM_WTJATTACK:
                    if (ProcessMsg.BaseObject != ObjectId)
                    {
                        var smId = ProcessMsg.wIdent switch
                        {
                            Grobal2.RM_WSJATTACK => Grobal2.SM_WSJATTACK,
                            Grobal2.RM_WTJATTACK => Grobal2.SM_WTJATTACK,
                            _ => Grobal2.SM_WWJATTACK
                        };
                        m_DefMsg = Grobal2.MakeDefaultMsg((short)smId,
                            ProcessMsg.BaseObject, ProcessMsg.nParam1,
                            ProcessMsg.nParam2, ProcessMsg.wParam);
                        SendSocket(m_DefMsg);
                    }
                    break;

                case Grobal2.RM_PHYSICAL_ATT:
                    if (ProcessMsg.BaseObject != ObjectId)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(
                            Grobal2.SM_PHYSICAL_ATT,
                            ProcessMsg.BaseObject, ProcessMsg.wParam,
                            ProcessMsg.nParam1, ProcessMsg.nParam2);
                        SendSocket(m_DefMsg, GetQueuedPayloadBytes(ProcessMsg));
                    }
                    break;

                // Native CM 4314 handler 0x6DB040 loads Param into DX and calls
                // 0x6F293C, whose entire body is `C3 ret` (bytes at 0x6F293C).
                // No SM, no field write. Explicit case so Operate does not fall
                // through to base.Operate.
                case Grobal2.CM_4314:
                    break;
                // Native CM 4315 handler 0x6DB054 loads Param into DX and calls
                // 0x6F2940, whose entire body is `C3 ret` (bytes at 0x6F2940).
                // No SM, no field write. Same empty-callee shape as 4314.
                case Grobal2.CM_4315:
                    break;
                case Grobal2.CM_3290:
                    ClientNativeCm3290ClockSnapshot();
                    break;
                case Grobal2.CM_4629:
                    ClientNativeCm4629GroupPositions();
                case Grobal2.SM_CHANNEL_MAGIC_CANCEL:
                    SendDefMessage(Grobal2.SM_CHANNEL_MAGIC_CANCEL,
                        ProcessMsg.BaseObject, ProcessMsg.wParam, 0, 0, "");
                    break;

                case Grobal2.SM_LOCATION_CHANNEL_MAGIC_CANCEL:
                    SendDefMessage(Grobal2.SM_LOCATION_CHANNEL_MAGIC_CANCEL,
                        ProcessMsg.BaseObject, ProcessMsg.wParam, 0, 0, "");
                    break;

                // === 战神协议: 客户端发送但服务端仅确认的 CM_（不需要服务端逻辑）===
                case Grobal2.CM_42HIT:
                case Grobal2.CM_CHANGEPASSWORD:
                case Grobal2.CM_CHECKTIME:
                    break;  // 客户端处理，服务端仅 ack

                default:
                    if (!TryHandleNativeSocialProtocol(ProcessMsg))
                    {
                        result = base.Operate(ProcessMsg);
                    }
                    break;
            }
            return result;
        }

        private void ProcessSwitchListen(TProcessMessage processMessage)
        {
            uint mask = processMessage.nParam2 switch
            {
                1 => 0x02u,
                2 => 0x04u,
                3 => 0x08u,
                4 => 0x01u,
                _ => 0u
            };
            if (mask == 0) return;
            if (processMessage.nParam1 == 0)
                m_dwChatShieldMask &= ~mask;
            else if (processMessage.nParam1 == 1)
                m_dwChatShieldMask |= mask;
            else
                return;
            ApplyChatShieldMaskToAllowFlags();
        }

        public override void Disappear()
        {
            CleanupNativeHorseOnExit();
            if (m_boReadyRun)
            {
                DisappearA();
            }
            if (m_boTransparent && m_boHideMode)
            {
                m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 0;
            }
            if (m_GroupOwner != null)
            {
                m_GroupOwner.DelMember(this);
            }
            if (m_MyGuild != null)
            {
                m_MyGuild.DelHumanObj(this);
            }
            LogonTimcCost();
            base.Disappear();
        }

        protected override void DropUseItems(TBaseObject BaseObject)
        {
            const string sExceptionMsg = "[Exception] TPlayObject::DropUseItems";
            IList<TDeleteItem> delList = null;
            try
            {
                if (m_boAngryRing || m_boNoDropUseItem)
                {
                    return;
                }
                GoodItem StdItem;
                // 人物爆率调整 patches sub_73FC70, not a runtime multiplier:
                //   0x100B9CCC A3 BB FC 73 00 -> imm32 of 0x73FCB8 C7 45 F8 15 00 00 00 (red K)
                //   0x100B9C5E A2 C9 FC 73 00 -> imm8  of 0x73FCC7 83 C0 5A             (non-red K)
                //   0x100B9D3A A2 6C FF 73 00 -> imm8  of 0x73FF69 83 7D F4 02          (max-1)
                // Off leaves C#'s existing 15/30 path (host 21/90 is a separate BLOCKED).
                var dropCount = 0;
                var deathDropPatched = new YanshenApi(this, null, M2Share.PluginManager)
                    .TryGetDeathEquipDropPatch(PKLevel() > 2, out var patchedRate, out var patchedCap);
                for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
                {
                    if (m_UseItems[i] == null)
                    {
                        continue;
                    }
                    StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[i].wIndex);
                    if (StdItem != null)
                    {
                        if ((StdItem.Reserved & 8) != 0)
                        {
                            if (delList == null)
                            {
                                delList = new List<TDeleteItem>();
                            }
                            delList.Add(new TDeleteItem()
                            {
                                MakeIndex = this.m_UseItems[i].MakeIndex,
                                ClientItemID = EnsureClientItemId(m_UseItems[i])
                            });
                            if (StdItem.NeedIdentify == 1)
                            {
                                M2Share.AddGameDataLog("16" + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + m_UseItems[i].MakeIndex + "\t" + HUtil32.BoolToIntStr(m_btRaceServer == Grobal2.RC_PLAYOBJECT) + "\t" + '0');
                            }
                            m_UseItems[i].wIndex = 0;
                            // native 0x73FD74 FF 45 F4 inc [ebp-0xc] then jmp 0x73FF6F
                            // (Reserved&8 skips the cap check, but the count still eats the budget)
                            if (deathDropPatched) dropCount++;
                        }
                    }
                }
                var nRate = deathDropPatched
                    ? patchedRate
                    : (PKLevel() > 2 ? M2Share.g_Config.nDieRedDropUseItemRate : M2Share.g_Config.nDieDropUseItemRate);
                for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
                {
                    if (M2Share.RandomNumber.Random(nRate) != 0)
                    {
                        continue;
                    }
                    if (m_UseItems[i] != null && M2Share.InDisableTakeOffList(m_UseItems[i].wIndex))
                    {
                        continue;
                    }
                    
                    if (DropItemDown(m_UseItems[i], 2, true, BaseObject, this))
                    {
                        StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[i].wIndex);
                        if (StdItem != null)
                        {
                            if ((StdItem.Reserved & 10) == 0)
                            {
                                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                {
                                    if (delList == null)
                                    {
                                        delList = new List<TDeleteItem>();
                                    }
                                    delList.Add(new TDeleteItem()
                                    {
                                        sItemName = M2Share.UserEngine.GetStdItemName(m_UseItems[i].wIndex),
                                        MakeIndex = this.m_UseItems[i].MakeIndex,
                                        ClientItemID = EnsureClientItemId(m_UseItems[i])
                                    });
                                }
                                m_UseItems[i].wIndex = 0;
                            }
                        }
                        // native 0x73FF66 FF 45 F4 inc [ebp-0xc] / 0x73FF69 83 7D F4 xx / 7F 0A jg
                        if (deathDropPatched)
                        {
                            dropCount++;
                            if (dropCount > patchedCap) break;
                        }
                    }
                }
                if (delList != null)
                {
                    SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0,
                        delList.Count, 0, 0, "", delList);
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
        }

        // ====================================================================
        // Hero Bag Transfer Methods
        // ====================================================================

        /// <summary>Move an item from master bag to hero bag (CM_HERO_TOHEROBAG).</summary>
        private void ClientHeroMoveToHeroBag(TProcessMessage ProcessMsg)
        {
            var requestClientItemId = ProcessMsg.nParam1;
            // 战神 sub_6D09D0 gate order, verbatim:
            //   0x6D09E3  cmp byte [ebx+0x73],0   ; m_boDeath   -> -1
            //   0x6D09ED  cmp byte [ebx+0x461],0  ; m_boDealing -> -1   <-- WAS MISSING
            //   0x6D09FA  cmp dword [ebx+0xBB0],0 ; hero == nil -> -1
            //   0x6D0A0D  call sub_772DA8         ; hero ghost  -> -1
            // Without the m_boDealing gate a player could stage an item in a trade and
            // then shunt the same object reference into the hero bag: the deal list and
            // the hero bag both hold it, the deal completes and hands it to the
            // counterparty while the hero bag keeps its copy -> two-container dupe.
            if (m_HeroObject == null || m_boDeath || m_boDealing
                || m_HeroObject.m_boDeath)
            {
                SendDefMessage(Grobal2.SM_TOHEROBAG_FAIL, -1, 0, 0, 0, "");
                return;
            }

            var item = FindClientItemIn(m_ItemList, requestClientItemId, false);
            var oldClientItemId = requestClientItemId;
            if (item != null)
            {
                oldClientItemId = EnsureClientItemId(item);
            }
            else
            {
                item = FindClientItemIn(m_ItemList, requestClientItemId, true);
            }

            if (item == null)
            {
                SendDefMessage(Grobal2.SM_TOHEROBAG_FAIL, 0, 0, 0, 0, "");
                return;
            }
            if (m_HeroObject.m_ItemList.Count >= HeroObject.GetHeroBagCapacity(m_HeroObject.m_Abil.Level))
            {
                SendDefMessage(Grobal2.SM_TOHEROBAG_FAIL, -2, 0, 0, 0, "");
                return;
            }

            // 战神 sub_6D09D0 @0x6D0A5E: `call sub_73D0F4(hero, item)` — the ADD runs
            // FIRST and is gated; only on success @0x6D0A70 `call sub_424B30` removes the
            // item from the master bag.  On failure @0x6D0AA6 writes -2 and the master bag
            // is left untouched.  C# removed first, so any gate inside the add step would
            // have destroyed the item.
            m_HeroObject.m_ItemList.Add(item);
            m_ItemList.Remove(item);                    // 0x6D0A70
            var newClientItemId = ReassignClientItemId(item);
            WeightChanged();
            m_HeroObject.WeightChanged();
            SendDefMessage(Grobal2.SM_TOHEROBAG_OK, oldClientItemId,
                HUtil32.LoWord(newClientItemId), HUtil32.HiWord(newClientItemId), 0, "");
        }

        /// <summary>Move an item from hero bag to master bag (CM_HERO_TOHUMBAG).</summary>
        private void ClientHeroMoveToHumBag(TProcessMessage ProcessMsg)
        {
            var requestClientItemId = ProcessMsg.nParam1;
            // 战神 sub_6D0B00 has the identical gate ladder:
            //   0x6D0B13  cmp byte [ebx+0x73],0   ; m_boDeath   -> -1
            //   0x6D0B1D  cmp byte [ebx+0x461],0  ; m_boDealing -> -1   <-- WAS MISSING
            //   0x6D0B2A  cmp dword [ebx+0xBB0],0 ; hero == nil -> -1
            //   0x6D0B3D  call sub_772DA8         ; hero ghost  -> -1
            if (m_HeroObject == null || m_boDeath || m_boDealing
                || m_HeroObject.m_boDeath)
            {
                SendDefMessage(Grobal2.SM_TOHUMBAG_FAIL, -1, 0, 0, 0, "");
                return;
            }

            var item = FindClientItemIn(m_HeroObject.m_ItemList, requestClientItemId, false);
            var oldClientItemId = requestClientItemId;
            if (item != null)
            {
                oldClientItemId = EnsureClientItemId(item);
            }
            else
            {
                item = FindClientItemIn(m_HeroObject.m_ItemList, requestClientItemId, true);
            }

            if (item == null)
            {
                SendDefMessage(Grobal2.SM_TOHUMBAG_FAIL, 0, 0, 0, 0, "");
                return;
            }
            if (m_ItemList.Count >= BagCapacity.Of(this))
            {
                SendDefMessage(Grobal2.SM_TOHUMBAG_FAIL, -3, 0, 0, 0, "");
                return;
            }

            // 战神 sub_6D0B00 @0x6D0B98: `mov dl,1; call [vmt+0x244]` (the master bag-space
            // gate) `; test al,al; je 0x6D0BF0` -> -3, and ONLY then @0x6D0BB1
            // `call sub_424B30` removes from the hero bag followed by @0x6D0BBA
            // `call sub_73D0C0` (the master add).  The gate above is that check; keep the
            // remove+add adjacent so the item is never in neither container.
            m_HeroObject.m_ItemList.Remove(item);       // 0x6D0BB1
            m_ItemList.Add(item);                       // 0x6D0BBA sub_73D0C0
            var newClientItemId = ReassignClientItemId(item);
            WeightChanged();
            m_HeroObject.WeightChanged();
            SendDefMessage(Grobal2.SM_TOHUMBAG_OK, oldClientItemId,
                HUtil32.LoWord(newClientItemId), HUtil32.HiWord(newClientItemId), 0, "");
        }

    }
}
