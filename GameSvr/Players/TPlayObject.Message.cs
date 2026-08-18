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

        /// <summary>
        /// 原生 CM_HERO_LOGON 的副将槽门（<c>Param == 1</c>）：脚本变量 <c>V(87,3)</c>
        /// 必须恰好等于 100，否则拒绝并提示「请先召唤一次主将英雄」。
        /// <code>
        /// 6D933E  B9 03 00 00 00     mov  ecx,3        ; index
        /// 6D9343  BA 57 00 00 00     mov  edx,0x57     ; group 87
        /// 6D934B  E8 94 5E 00 00     call 0x6DF1E4     ; GetV
        /// 6D9350  83 F8 64           cmp  eax,0x64
        /// 6D9353  75 30              jne  0x6D9385
        /// 6D9385  66 B9 FF 38        mov  cx,0x38FF
        /// 6D9389  BA 68 BF 6D 00     mov  edx,0x6DBF68 ; declen 20 GBK
        /// 6D9393  FF 93 D4 00 00 00  call [vmt+0xD4]   ; sub_73C8F4 -> RM 0x2774 拆成 FColor/BColor
        /// </code>
        /// 引擎自身从不写 <c>V(87,3)</c>（全镜像 SetV 调用点里没有 <c>edx=0x57</c>），
        /// 它由脚本在玩家首次召唤主将后置位——这正是提示语的字面含义。
        /// </summary>
        private bool NativeViceHeroSummonAllowed()
        {
            // 0x6DF1F1 mov [ebp-4],0xFFFFFFFF：GetV 未命中答 -1，同样不等于 100。
            if (!TryGetScriptVar('V', 87, 3, out var nFlag))
                nFlag = -1;
            if (nFlag == 100)
                return true;
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0,
                "请先召唤一次主将英雄");
            return false;
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
            var isStall = mapEvent.m_nEventType == Grobal2.ET_STALL;
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
                RunNativeSwitchHeroHandoff(currentTick);
                RunNativeClientVersionGate(currentTick);
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
                PollNativeBurstStateExpiry();
                TickNativeBravePowerBuffs(currentTick);
                TickNativeBlessBuff(currentTick);
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
                TryExpireNativeFireHitSkill(currentTick);
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
                // 眼神「全局循环函数」周期驱动：与上面 @OnTimer 同族的 per-player 节拍，
                // 按 循环时间_值（生产 2000ms）敲脚本 MyTimer→Ys_HuiShou→AutoRecycle。
                // 全部判定（开关/周期/节流）在 YanshenRecycleDriver 独立文件，此处仅一行挂载。
                YanshenRecycleDriver.Tick(this, currentTick);
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
                // MOVE-73/74 —— 原生 sub_6B2D38 在 0x6B308B..0x6B30E1 无条件重算穿透
                // 判定、仅变化时回写 Obj[+0x3FE] 并发 0xB05。位置在挤人块(0x6B3149)
                // 之前、且不在任何时间闸内，故这里也放在 3000ms 块之前、每 Run 一次。
                NativeTickThroughOccupancyTransition();
                // 0x6B3143 uses the Run tick cached in [ebp-8] and an unsigned
                // `jbe`; the same cached tick feeds every duplicate-cell edge.
                if (NativeBeginDuplicateOccupancyPoll(currentTick))
                {
                    GetStartPoint();
                    // 0x6B31B5 calls sub_778858 (GetMovObjCount), whose
                    // liveness/visibility filters differ from GetXYObjCount.
                    tObjCount = m_PEnvir.GetNativeMovObjCount(
                        m_nCurrX, m_nCurrY);
                    var duplicateElapsed =
                        NativeUpdateDuplicateOccupancyLatch(
                            currentTick, tObjCount);
                    if (NativeShouldAutoPushDuplicateOccupancy(
                            tObjCount, duplicateElapsed))
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
                    // MOVE-74 —— 这里原先用 InSafeArea() 驱动 0xB05，并且整块被
                    // m_MyGuild.GuildWarList.Count > 0 包住。两者都不是原生：原生
                    // 0x6B308B..0x6B30E1 由 sub_768454(穿透判定)驱动、无任何行会条件、
                    // 也不调 RefNameColor()，且不在 1000ms 闸内。已移到本方法上方的
                    // NativeTickThroughOccupancyTransition()。
                    if (castle == null || !castle.m_boUnderWar)
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
                    if (m_boSoftClose && !m_boReconnection)
                        OnNativeHostPlayerLogout();
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
                // 屏蔽元宝数据库日志 @0x70F6DC stub sub_70F6DC。
                if (M2Share.g_boGameLogGameGold
                    && !Plugins.YanshenPangu1Patches.ShouldSuppressGameGoldDbLog())
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
                if (M2Share.g_boGameLogGameGold
                    && !Plugins.YanshenPangu1Patches.ShouldSuppressGameGoldDbLog())
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
                    if (M2Share.g_boGameLogGameGold
                        && !Plugins.YanshenPangu1Patches.ShouldSuppressGameGoldDbLog())
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
                    if (M2Share.g_boGameLogGameGold
                        && !Plugins.YanshenPangu1Patches.ShouldSuppressGameGoldDbLog())
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
            // Native TPlayer.Run ends with sub_6C3ABC at 0x6B3CC5.
            RunNativeGroupRequestExpiry();
        }

        internal bool TryExpireNativeFireHitSkill(int currentTick)
        {
            if (!m_boFireHitSkill ||
                unchecked((uint)(currentTick - m_dwLatestFireHitTick)) <= 20000u)
            {
                return false;
            }

            // sub_6B2D38 @0x6B2F13..0x6B2F50 clears +0x96 and sends only
            // SM 626/Recog 1. There is no companion text packet.
            m_boFireHitSkill = false;
            SendSocket(Grobal2.MakeDefaultMsg(
                Grobal2.SM_FIREHITSKILL, 1, 0, 0, 0));
            return true;
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
                case NativeClientVersionDisconnectIdent:
                    // Self-message 10000 reaches 0x6B44A5 in the native RM
                    // dispatcher and raises player+0x4BB (m_boSoftClose).
                    m_boSoftClose = true;
                    break;
                case NativeMagicProducerPushIdent:
                    TryHandleNativeMagicProducerMessage(ProcessMsg);
                    break;
                case Grobal2.RM_LINGFU_CHANGED:
                    SendNativeCapitalInfo();
                    break;
                case Grobal2.RM_NATIVE_EXP_CONTINUE:
                    GrantNativePlayerExperience(ProcessMsg.nParam1, ProcessMsg.nParam2 != 0,
                        ProcessMsg.nParam3 != 0, ProcessMsg.wParam);
                    break;
                case Grobal2.RM_NATIVE_MOOTEBO_CONTINUE:
                    ContinueNativeMotaeboForcedMove(ProcessMsg);
                    break;
                case Grobal2.RM_NATIVE_LOGON_STATE_SYNC:
                    SendNativeLogonStateSync();
                    break;
                case Grobal2.RM_NATIVE_CHARGE_LAND:
                    ProcessNativeSkill68ChargeLanding(ProcessMsg.nParam1,
                        ProcessMsg.nParam2, ProcessMsg.wParam,
                        ProcessMsg.Payload);
                    break;
                case Grobal2.RM_NATIVE_CHARGE_MOVE:
                    m_DefMsg = Grobal2.MakeDefaultMsg(
                        Grobal2.SM_NATIVE_CHARGE_MOVE, ProcessMsg.BaseObject,
                        ProcessMsg.nParam1, ProcessMsg.nParam2,
                        ProcessMsg.wParam);
                    SendSocket(m_DefMsg);
                    break;
                case Grobal2.RM_NATIVE_BLINK_MOVE:
                    m_DefMsg = Grobal2.MakeDefaultMsg(
                        Grobal2.SM_NATIVE_BLINK_MOVE, ProcessMsg.BaseObject,
                        ProcessMsg.nParam1, ProcessMsg.nParam2,
                        ProcessMsg.wParam);
                    SendSocket(m_DefMsg);
                    break;
                // 0x6B6065: gate on sub_774288 first, then `66 BA 1E 00` +
                // `6A 01 / 6A 00 / 6A 00 / 6A 00` through the unicast slot
                // VMT+0x250, with the caster as Recog.
                case Grobal2.RM_NATIVE_STEALTH_VANISH:
                    if (BaseObject != null &&
                        BaseObject.IsNativeStealthedFrom(this))
                    {
                        SendDefMessage(Grobal2.SM_DISAPPEAR,
                            ProcessMsg.BaseObject, 0, 0, 0, "");
                    }
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
                case Grobal2.CM_205:
                    ClientNativeCheatSelfReport(ProcessMsg.wParam, ProcessMsg.nParam2);
                    break;
                case Grobal2.CM_1239:
                    // Native 0x6DA3A2, whole handler:
                    //   0x6DA3A5  66 83 78 06 00        cmp  word [msg+6],0   ; Param
                    //   0x6DA3AA  75 0F                 jne  0x6DA3BB
                    //   0x6DA3AF  C6 80 98 18 00 00 01  mov  byte [self+0x1898],1
                    //   0x6DA3B6  E9 71 18 00 00        jmp  0x6DBC2C
                    //   0x6DA3BE  C6 80 98 18 00 00 00  mov  byte [self+0x1898],0
                    // No callee, no packet, no other field. Param is the DEFAULT-case
                    // nParam2 (ProcessUserMessage maps Recog/Param/Tag/Series onto
                    // nParam1/nParam2/nParam3/wParam), and the wire field is a ushort so
                    // the native `jne` against a word is a plain equality test.
                    m_boNativeHeroCapHintEnabled = ProcessMsg.nParam2 == 0;
                    break;
                case Grobal2.CM_1281:
                    // Native 0x6DA9C8, whole handler:
                    //   0x6DA9CB  66 8B 40 06           mov  ax, word [msg+6]  ; Param
                    //   0x6DA9CF  66 85 C0 / 75 0F      test ax,ax / jne 0x6DA9E3
                    //   0x6DA9D7  C6 80 AC 18 00 00 00  mov  byte [self+0x18AC],0
                    //   0x6DA9E3  66 83 F8 01           cmp  ax,1
                    //   0x6DA9E7  0F 85 3F 12 00 00     jne  0x6DBC2C          ; leave as-is
                    //   0x6DA9F0  C6 80 AC 18 00 00 01  mov  byte [self+0x18AC],1
                    // Unlike CM 1239 this is a THREE-way test: only 0 and 1 write, every
                    // other Param falls through to the default label without touching the
                    // flag, so it must not be written as a boolean assignment.
                    if (ProcessMsg.nParam2 == 0)
                        m_boNativeHeroRecordShared = false;
                    else if (ProcessMsg.nParam2 == 1)
                        m_boNativeHeroRecordShared = true;
                    break;
                case Grobal2.CM_YB_CONSIGN_INBOX:
                case Grobal2.CM_YB_CONSIGN_OUTBOX:
                case Grobal2.CM_YB_DEAL_BUY_HISTORY:
                case Grobal2.CM_YB_DEAL_SELL_HISTORY:
                    // Four two-instruction arms that share one shape; the per-ident differences
                    // (throttle slot, throttle comparison, row cap, SQL, reply ident) live in
                    // NativeYbConsignmentQuery.Descriptors.
                    ClientYbConsignmentQuery(ProcessMsg.wIdent);
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
                    // 盘古穿戴触发 @ChangeEquip：眼神 trampoline 挂在分发器 0x6D8E35，
                    // 即 call ClientTakeOnItems(0x6B7E9C) 返回之后（该原生处理器唯一调用者），
                    // 无条件（不论穿戴成功与否）派发一次再回默认标签。惰性门在 FireChangeEquip
                    // 内（插件缺席时零派发）。见 YanshenTriggerDispatch。
                    GameSvr.Plugins.YanshenTriggerDispatch.FireChangeEquip(this);
                    break;
                case Grobal2.CM_TAKEOFFITEM:
                    ClientTakeOffItems((byte)ProcessMsg.nParam2, ProcessMsg.nParam1, ProcessMsg.sMsg);
                    // 盘古穿戴触发 @ChangeEquip：眼神 trampoline 挂在分发器 0x6D8E4D，
                    // 即 call ClientTakeOffItems(0x6B8188) 返回之后（该原生处理器唯一调用者），
                    // 无条件派发一次再回默认标签。惰性门在 FireChangeEquip 内。
                    GameSvr.Plugins.YanshenTriggerDispatch.FireChangeEquip(this);
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
                    // Handler 0x6D8ED0 is four instructions and reads NO packet field:
                    //   0x6D8ED0  8B 45 FC              mov  eax,[ebp-4]
                    //   0x6D8ED3  C6 80 10 07 00 00 01  mov  byte [self+0x710],1
                    //   0x6D8EDA  8B 45 FC              mov  eax,[ebp-4]
                    //   0x6D8EDD  C6 80 BB 04 00 00 01  mov  byte [self+0x4BB],1
                    //   0x6D8EE4  E9 43 2D 00 00        jmp  0x6DBC2C
                    // so the `wParam == 1 -> m_boEmergencyClose` arm that used to sit here had
                    // no native counterpart, and it let the client pick the close mode.
                    //
                    // Both offsets are now pinned, which is what previously blocked the call.
                    // The run loop at 0x651969 guards logout with the same three flags C# does
                    // and then relocates exactly the same three fields:
                    //   0x651969  cmp byte[p+0x4BB],0 / 75 1C jne 0x65198E
                    //   0x651975  cmp byte[p+0x4BD],0 / 75 10 jne 0x65198E
                    //   0x651981  cmp byte[p+0x4BC],0 / 0F 84 .. je (not logging out)
                    //   0x651991  cmp byte[p+0x4BA],0 / 74 41 je            ; m_boSwitchData
                    //   0x65199A  add eax,0x115 / 0x6519A5 add edx,0xC28    ; map name
                    //   0x6519B5  [p+0xC38] -> [p+0x12C]                    ; CurrX
                    //   0x6519C7  [p+0xC3C] -> [p+0x130]                    ; CurrY
                    // which is line-for-line the `m_boEmergencyClose || m_boKickFlag ||
                    // m_boSoftClose` guard and the m_boSwitchData block above at 463-470.
                    // m_boEmergencyClose therefore lives in {0x4BB, 0x4BC, 0x4BD}, and 0x4BB is
                    // already fixed as m_boSoftClose (RM 10000 writes it at 0x6B44A8).
                    //
                    // 0x710 is outside that trio. It has two writers — this handler and the
                    // switch-server setter 0x6BD0AE, which also stores the target name/x/y and
                    // raises 0x4BA and 0x4BB — and one reader, 0x6B64E4, choosing disconnect
                    // reason 1 over 3 for sub_6B6510. C#'s m_boReconnection has the same two
                    // writers (here and the GetMultiServerAddrPort path in UsrEngn) and is
                    // likewise outside the trio. The mapping is no longer an inference.
                    if (!m_boOffLineFlag)
                    {
                        m_boReconnection = true;
                        m_boSoftClose = true;
                    }
                    break;
                case Grobal2.CM_CLICKNPC:
                    // Native handler 0x6D8EE9 opens with a two-seat-mount gate that C#
                    // was missing, so a player riding pillion could still open NPC
                    // dialogs here while 战神 drops the click outright:
                    //   0x6D8EE9  B2 34              mov  dl,0x34
                    //   0x6D8EEB  8B 45 FC           mov  eax,[ebp-4]
                    //   0x6D8EEE  E8 6D 9A 09 00     call 0x772960   ; HasNativeActiveState
                    //   0x6D8EF3  84 C0              test al,al
                    //   0x6D8EF5  0F 85 31 2D 00 00  jne  0x6DBC2C   ; mounted -> silent drop
                    // State 0x34 is the two-seat mount (SET 0x6EE8AF/0x6EE8B3, CLEAR
                    // 0x6EEBC2-0x6EEBC6), the same one the group prechecks read at
                    // 0x6BBEA0. Only after the gate does native call 0x6B8B28 with
                    // edx = Recog (the NPC id) and ecx = Tag.
                    if (HasNativeActiveState(0x34))
                        break;
                    // nParam3 is Tag on the default ingress arm (UsrEngn.ProcessUserMessage
                    // passes DefMsg.Tag as lParam3), and 0x6D8EFE `0F B7 48 08 movzx ecx,
                    // word [msg+8]` makes Tag the second argument to 0x6B8B28, where it picks
                    // between the monster registry and the NPC registry.
                    ClientClickNPC(ProcessMsg.nParam1, ProcessMsg.nParam3);
                    break;
                case Grobal2.CM_MERCHANTDLGSELECT:
                    ClientMerchantDlgSelect(ProcessMsg.nParam1, ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_MERCHANTQUERYSELLPRICE:
                    ClientMerchantQuerySellPrice(ProcessMsg.nParam1, HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3), ProcessMsg.sMsg);
                    break;
                case Grobal2.CM_USERSELLITEM:
                    ClientUserSellItem(ProcessMsg.nParam1, HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3));
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
                    // The first 1018 is consumed by the native pre-dispatch
                    // version arm in UserEngine.ProcessUserMessage. Repeated
                    // 1018 packets reach this final native default/no-op arm.
                    break;
                case Grobal2.CM_1325:
                    // Unlike CM_LOGINNOTICEOK above, 1325 DOES have a real dispatch arm -
                    // jump-table slot 0x6D8482[0] points at handler 0x6DAC1C:
                    //   0x6DAC1F  66 8B 50 06  mov dx, word [msg+6]   ; Param
                    //   0x6DAC23  8B 45 FC     mov eax,[ebp-4]        ; Self
                    //   0x6DAC26  E8 F1 34 01 00  call 0x6EE11C
                    //   0x6DAC2B  E9 FC 0F 00 00  jmp 0x6DBC2C
                    // but 0x6EE11C is an empty procedure in full:
                    //   55 8B EC 51 89 45 FC 59 5D C3
                    //   push ebp / mov ebp,esp / push ecx / mov [ebp-4],eax / pop ecx / pop ebp / ret
                    // It never reads dx and returns nothing. A whole-image scan finds
                    // exactly one caller (0x6DAC26), so there is no other body that could
                    // give the routine meaning. Param is therefore discarded and the
                    // opcode has no server-side effect at all.
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
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_GOOD, 0, 0, 0, 0);
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            // MOVE-25: native turn(3010) refusal 0x6D9B94 pushes FOUR
                            // ZEROS before `mov dx,0x276` (0x6D9B9E) / call [ebx+0x250]
                            // — the turn correction carries NO coordinates, unlike
                            // walk/run (0x6D9C26/0x6D9D42) which send X/+0x12C, Y/+0x130,
                            // Dir/+0x154. SendMoveActionFail() emits the walk/run shape,
                            // so turn must issue the four-zero SM_ACT_FAIL(0x276) directly.
                            SendDefMessage(Grobal2.SM_ACT_FAIL, 0, 0, 0, 0, "");
                        }
                        else
                        {
                            nMsgCount = GetTurnMsgCount();
                            if (nMsgCount >= M2Share.g_Config.nMaxTurnMsgCount)
                            {
                                // MOVE-22: Native never disconnects, kicks or logs a fast
                                // client. MOVE-25: the turn correction is unconditionally
                                // four-zero (0x6D9B94), so this shares the same shape.
                                SendDefMessage(Grobal2.SM_ACT_FAIL, 0, 0, 0, 0, "");
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
                    // MOVE-10: 双人坐骑乘客态(state 0x34)静默移动闸。原生跳表 0x6D8592 只在
                    // walk(3011)/run(3013) 两臂带此闸（turn/pose 没有）；置位点唯一 —— 0x6EE8AF/
                    // 0x6EE8B3(bts) 夹在 0x6EE8A0 写同伴指针 [+0x3C0] 与 0x6EE8DC 搬到驾驶者格
                    // 之间，故 0x34 = 乘客态（0x33 = 驾驶者），本端为 NativeHorseBlockedState(52)。
                    // 命中即静默 break：不能塞进 ClientWalkXY，因其返回 false 会发 0x276、
                    // 返回 true 会发 0x275，都不是原生的"整臂丢弃、不发包"。
                    if (IsNativeMoveBlockedByPassengerState())
                    {
                        break;
                    }
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
                case Grobal2.CM_RUN:
                    // MOVE-11: 原生 run 臂的第一件事不是乘客闸，而是 0x6D9CE4
                    // `mov eax,[ebp-4]` / 0x6D9CE7 `call 0x7742C0` —— 隐身态(0x40)
                    // 揭示钩子。次序不能与下面的 MOVE-10 对调：0x6D9CE7 < 0x6D9CEC，
                    // 故乘客态被静默丢弃时也已经揭示过了。CM_WALK 没有这一步
                    // （0x6D9BD0 直接 `mov dl,0x34`）—— 走路保持隐身，跑步破隐。
                    BreakNativeStealthOnAction();
                    // MOVE-10: 同 CM_WALK —— 乘客态(state 0x34)静默丢弃整臂，不发任何包。
                    // 跳表 0x6D8592 证明该闸只覆盖 walk(3011)/run(3013)。
                    if (IsNativeMoveBlockedByPassengerState())
                    {
                        break;
                    }
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
                // ID3035: 3035 是 CASE1 的第十一个 ident，不是骑乘跑。派发器经累减链
                //   0x6D85F0 sub eax,0xBD4 / 0x6D85FB sub eax,2 /
                //   0x6D8604 sub eax,2     / 0x6D860D sub eax,3 /
                //   0x6D8610 0F 84 99 18 00 00  je 0x6D9EAF
                // 到达它；sub_6EC078 的窗口也恰好收在它上面（0x6EC15D
                // `add eax,-0xBBA` / 0x6EC162 `cmp eax,0x21`），字节表
                // 0x6EC178[33]=0x09 选中槽 0x6EC19A[9]=0x6EC29C `mov cx,0x3F9`
                // = 动作码 1017，比 CM_CRSHIT 的 0x3FA 小一号。原生骑乘跑是
                // CM_RUN3(4108) @0x6D9D99，门在 bodyState 0x33。
                // 原先那条独立臂走 ClientHorseRunXY，**一道骑乘态检查都没有**，
                // 于是 3035 能白拿一次未骑乘的三格 HorseRunTo —— 已删除。
                case Grobal2.CM_HORSERUN:
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
                    // HIT-ARM: 原生 HIT 族是**两条**臂，前置闸的次序按 ident 分叉，
                    // 整条阶梯移进 RunNativeHitArmGates（TPlayObject.NativeHitArmGates.cs）：
                    //   CASE1 0x6D9EAF（3002/3014/3015/3016/3018/3019/3024/3025/3026/3028）
                    //     0x6D9EB4 call 0x6F2D48 揭示钩子 → 0x6D9EBC 骑乘闸(命中静默)
                    //     → 0x6D9ED3 call 0x6BCE2C 取消通道 → 0x6D9EDF can-act 闸
                    //   CASE2 0x6D9F4B（3027 = CM_3037，jcc 全扫证明它是唯一入口）
                    //     0x6D9F50 揭示钩子 → 0x6D9F58 骑乘闸(命中发 0x276)
                    //     → 0x6D9F6C can-act 闸 → 0x6D9F7D 取消通道
                    // 即 sub_6BCE2C 在 CASE1 排在 can-act 之前、CASE2 之后，单点插入
                    // 无法同时忠实，故由 helper 按 ident 分叉。
                    // MINE-49 的骑乘闸并入该阶梯；CM_3037 骑乘态下原生走 0x6D9FE7 的
                    // 0x276，现按 Refuse 落到下方 dwDelayTime==0 分支，旧注释登记的
                    // 「CM_3037 少发一个 SM_ACT_FAIL 更正包」有界偏差随之消除。
                    // can-act 闸 0x6D9EDF/0x6D9F6C = `B2 01 mov dl,1` + `FF 51 40
                    // call [ecx+0x40]` = TPlayer VMT 0x6AC8C8+0x40 = 0x6E6700；本端
                    // IsNativeCanActBlocked(1) 早已在位（MOVE-14/15），只是从未在
                    // HIT 路径上被查询过。
                    // Refuse 不自行发包：原生两条拒绝边与 sub_6EC078 失败共用同一个
                    // 0x276 块（0x6D9EE4 与 0x6D9F0D 同落 0x6D9F0F），故复用下方
                    // 「ClientHitXY 返回 false」的分支；dwDelayTime 在本 switch 内
                    // 保持 :934 的初值 0（与 MOVE-90 的 CM_SPELL 短路同理）。
                    int nHitGate = RunNativeHitArmGates(ProcessMsg.wIdent);
                    if (nHitGate == NativeHitGateConsume)
                    {
                        break;
                    }
                    if (nHitGate == NativeHitGateProceed && ClientHitXY(
                            ProcessMsg.wIdent == Grobal2.CM_3037
                                ? ProcessMsg.nParam3
                                : ProcessMsg.wIdent,
                            ProcessMsg.nParam1, ProcessMsg.nParam2, (byte)(ProcessMsg.wParam & 7), ProcessMsg.boLateDelivery, ref dwDelayTime))
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_GOOD, 0, 0, 0, 0);
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            if (ProcessMsg.wIdent == Grobal2.CM_3037)
                            {
                                // CASE2 @0x6D9FAB sends only the four-zero
                                // SM_ACT_FAIL frame. It has no RM_MOVEFAIL leg.
                                SendDefMessage(Grobal2.SM_ACT_FAIL, 0, 0, 0,
                                    0, "");
                            }
                            else
                            {
                                SendRefMsg(Grobal2.RM_MOVEFAIL, 0, 0, 0, 0,
                                    "");
                                SendDefMessage(Grobal2.SM_ACT_FAIL,
                                    (int)ProcessMsg.wIdent, 0, 0, 0, "");
                            }
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
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_GOOD, 0, 0, 0, 0);
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        // Native 0x6D9C8B..0x6D9CA4 emits exactly one VMT+0x250
                        // failure frame with four zero fields. There is no
                        // RM_MOVEFAIL broadcast and no interval/redelivery arm.
                        SendDefMessage(Grobal2.SM_ACT_FAIL, 0, 0, 0, 0, "");
                    }
                    break;
                case Grobal2.CM_SPELL:
                    // HIT-ARM: 原生 3017 臂 0x6DA04A 的第一件事是
                    //   0x6DA04D  0F B7 50 0A     movzx edx,word [msg+0x0A]   ; Series
                    //   0x6DA054  E8 EF 8C 01 00  call 0x6F2D48
                    // 即带 0x10B 豁免的揭示钩子，排在 0x6DA059 的 state 0x33 骑乘闸之前。
                    // [msg+0x0A] 是施法魔法号，UsrEngn 的 default 臂把 Series 放进
                    // SendMsg 的第 3 参 wParam（同一个值下面 ClientSpellXY 当 nKey 收），
                    // 所以这里传 ProcessMsg.wParam。唯有 magic 267(0x10B) 不破隐身 0x40；
                    // 隐藏态 0x3C 照破（0x6F2D53 排在 0x6F2D58 的比较之前）。
                    NotifyNativeActionReveal(ProcessMsg.wParam);
                    // STATE-50: 0x6DA09D..0x6DA0BC first calls the TPlayer
                    // VMT+0x40 can-act slot with DL=1. A blocked caster may
                    // continue only through sub_7725FC: magic 0x72, or 0xD3
                    // while state 0x1A is active. That exceptional branch calls
                    // ClientSpellXY directly and therefore bypasses NOMAGIC.
                    bool nativeCanActForSpell = !IsNativeCanActBlocked(1);
                    bool nativeSpellBypassesCanAct = !nativeCanActForSpell
                        && CanNativeSpellBypassCanActGate(ProcessMsg.wParam);
                    // MOVE-90: NOMAGIC 地图禁施法门。原生 CM_SPELL 派发器 sub_6D7D68 在调
                    // DoSpell(sub_6BC510) 之前先测地图旗标：
                    //   006DA125  8B 80 28 01 00 00     mov eax,[eax+0x128]      ; player.m_PEnvir
                    //   006DA12B  80 B8 81 00 00 00 00  cmp byte [eax+0x81],0    ; boNOMAGIC
                    //   006DA132  0F 85 42 00 00 00     jne 0x6DA17A             ; 置位→静默拒绝
                    //   006DA17A  ... mov dx,0x276 / call [vtbl+0x250]           ; 只发失败应答,不施法,无文本
                    // 短路后 dwDelayTime 保持 0；下方失败臂只发原版的四零
                    // SM_ACT_FAIL(0x276)，不额外广播 RM_MOVEFAIL。
                    if ((nativeSpellBypassesCanAct
                            || nativeCanActForSpell
                            && !NativeNoMagicMapForbidsSpell())
                        && ClientSpellXY((short)ProcessMsg.wIdent,
                            ProcessMsg.wParam, ProcessMsg.nParam1,
                            ProcessMsg.nParam2,
                            M2Share.ObjectManager.Get(ProcessMsg.nParam3),
                            ProcessMsg.boLateDelivery,
                            nativeSpellBypassesCanAct, ref dwDelayTime))
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_GOOD, 0, 0, 0, 0);
                        SendSocket(M2Share.GetGoodTick);
                    }
                    else
                    {
                        if (dwDelayTime == 0)
                        {
                            SendDefMessage(Grobal2.SM_ACT_FAIL, 0, 0, 0, 0, "");
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
                        ProcessUserLineMsg(ProcessMsg.sMsg,
                            ProcessMsg.Payload as byte[], ProcessMsg.nBodyLen);
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
                        var nativePayload = ProcessMsg.Payload as
                            NativeSpellRelayPayload;
                        var spellBody = new byte[8];
                        Buffer.BlockCopy(BitConverter.GetBytes(nativePayload == null
                                ? (int)HUtil32.LoWord(ProcessMsg.nParam3)
                                : ProcessMsg.nParam3),
                            0, spellBody, 0, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(nativePayload == null
                                ? (int)HUtil32.HiWord(ProcessMsg.nParam3)
                                : nativePayload.EffectiveLevel),
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
                // RM_41 (9041) and RM_43 (9043) are below the dispatcher's window: native
                // does 0x6B3EF8 `add eax,0xFFFFD8F0` then 0x6B3EFD `cmp eax,0x86` /
                // 0x6B3F02 `ja 0x6B6241`, so both wrap to a huge unsigned value and land on
                // the default label without sending anything. The labels stay because this
                // switch's own default is not silent.
                case Grobal2.RM_41:
                case Grobal2.RM_43:
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
                    // MOVE-85: 原生 sub_6B6BEC 在发完 0x2C4(SM_MYSTATUS) 特征字后，紧接着发
                    // 进图通告（超负重 @0x6B6D10 + 三档巅峰状态 @0x6B6D40/0x6B6D7C/0x6B6DB4，
                    // 按 byte[Envir+0xB8](BreakLevel)+word[Envir+0xBA](CrazyBreakLevel) 分档
                    // 0x96/0x32/0x0A）。本调用点 = 原生进图 caller 0x6B954D（SM_LOGON=50 之后）。
                    SendNativeMapEntryStateMessages();
                    SendMapDescription();
                    break;
                case Grobal2.RM_NATIVE_REVIVE_MESSAGE:
                    // sub_73C910 queues native RM 12308 with wParam=1 and the
                    // revive/rebirth text. Handler 0x6B4E77 forwards the buffer
                    // byte-for-byte as SM 213: Recog=BaseObject, Param=wParam,
                    // Tag=Series=0.
                    if (m_boGhost)
                        break;
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_REVIVE_MESSAGE,
                        ProcessMsg.BaseObject, ProcessMsg.wParam, 0, 0);
                    if (ProcessMsg.Payload is byte[] rawReviveBody)
                        SendSocket(m_DefMsg, rawReviveBody);
                    else
                        SendSocket(m_DefMsg, ProcessMsg.sMsg);
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
                    // Native tag 10099 owns jump-table slot 99 at 0x6B3F0F+99*4, and that
                    // slot points at the default label 0x6B6241 - the arm discards the
                    // message without sending anything.
                    if (ProcessMsg.wIdent == Grobal2.RM_MOVEMESSAGE)
                        break;
                    switch (ProcessMsg.wIdent)
                    {
                        case Grobal2.RM_HEAR:
                            // 0x6B4A70 68 00 FF 00 00 push 0xFF00 — Param is hardcoded.
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_HEAR,
                                ProcessMsg.BaseObject, 0xFF00, 0, 1);
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
                            // The recipient's whisper monitor is served here, ahead of
                            // the packet, and it is a SysMsg rather than a second 103:
                            //   0x6B4A9C 8B B0 44 19 00 00  mov esi,[eax+0x1944]
                            //   0x6B4AA6 80 7E 73 00        cmp byte [esi+0x73],0
                            //   0x6B4AB2 8B 43 10           mov eax,[ebx+0x10]  ; body
                            //   0x6B4AC6 BA E0 63 6B 00     mov edx,0x6B63E0    ; "聆听私聊 "
                            //   0x6B4AD8 66 B9 FF 38        mov cx,0x38FF
                            //   0x6B4ADE FF 96 D4 00 00 00  call [VMT+0xD4]
                            // Doing it on the arm rather than in Whisper() is what makes
                            // the cross-server path (0x6C9793) reach the monitor too.
                            if (m_GetWhisperHuman != null && !m_GetWhisperHuman.m_boGhost)
                            {
                                m_GetWhisperHuman.SendMsg(m_GetWhisperHuman,
                                    Grobal2.RM_SYSMESSAGE, 0,
                                    WhisperMonitorFColor, WhisperMonitorBColor, 0,
                                    WhisperMonitorPrefix + ProcessMsg.sMsg);
                            }
                            // Native RM 10031 arm, the only send point for ident 103:
                            //   0x6B4AE4 68 FC FF 00 00     push 0xFFFC        -> Param  (literal)
                            //   0x6B4AE9 66 8B 43 02        mov ax,[ebx+2]     -> Tag    = wParam
                            //   0x6B4AEE 66 8B 43 04        mov ax,[ebx+4]     -> Series = nParam1
                            //   0x6B4AFC 8B 4B 24           mov ecx,[ebx+0x24] -> Recog  = BaseObject
                            //   0x6B4AFF 66 BA 67 00        mov dx,0x67
                            //   0x6B4B08 FF 93 54 02 00 00  call [VMT+0x254]
                            // Param is an immediate with no alternate arm - a full-image
                            // sweep finds exactly two RM 10031 producers (0x6C960C, 0x6C9793)
                            // and one ident-103 send, so there is no colour-tier selector.
                            // wParam carries the speaker's level (word[speaker+0x278]).
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_WHISPER,
                                ProcessMsg.BaseObject, 0xFFFC,
                                ProcessMsg.wParam, ProcessMsg.nParam1);
                            break;
                        case Grobal2.RM_CRY:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_CRY,
                                ProcessMsg.BaseObject, 0x9700, 0, 1);
                            break;
                        case Grobal2.RM_CATTLE_SYSMESSAGE:
                            // 0x743B70 mov ax,[ebx+2] / push eax => Param <- wParam, then
                            // 6A 00 / 6A 00 => Tag = Series = 0, 0x743B88 mov ecx,[ebx+4]
                            // => Recog <- nParam1, 0x743B8B mov dx,0xB0C.
                            // The enqueue helper sub_743C34 puts its ecx in the wParam slot
                            // ([ebp+0x1C] -> word[rec+2]) and Self in nParam1, and both
                            // native callers load `mov cx,0xFB` (0x7159D6, 0x715D4E) - the
                            // same 0xFB this tree's producer passes. So the colour byte
                            // belongs in Param; it was going out in Series, which native
                            // leaves at zero. Recog was already right: nParam1 == Self.
                            m_DefMsg = Grobal2.MakeDefaultMsg(
                                Grobal2.SM_CATTLE_SYSMESSAGE,
                                ProcessMsg.BaseObject, ProcessMsg.wParam,
                                0, 0);
                            break;
                        case Grobal2.RM_SYSMESSAGE:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SYSMESSAGE,
                                ProcessMsg.BaseObject,
                                ProcessMsg.Payload is byte[]
                                    ? ProcessMsg.wParam
                                    : HUtil32.MakeWord(ProcessMsg.nParam1, ProcessMsg.nParam2),
                                0, 1);
                            break;
                        case Grobal2.RM_GROUPMESSAGE:
                            // 0x6B4EA0 68 C4 FF 00 00 / 0x6B4EB5 66 BA 64 00
                            // Group wire ident is SM 100, Param=0xFFC4 hardcoded.
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SYSMESSAGE,
                                ProcessMsg.BaseObject, 0xFFC4, 0, 1);
                            break;
                        case Grobal2.RM_GUILDMESSAGE:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_GUILDMESSAGE, ProcessMsg.BaseObject, 0xFFD4, 0, 1);
                            break;
                        case Grobal2.RM_MERCHANTSAY:
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_MERCHANTSAY, ProcessMsg.BaseObject, HUtil32.MakeWord(ProcessMsg.nParam1, ProcessMsg.nParam2), 0, 1);
                            break;
                    }
                    if (ProcessMsg.wIdent == Grobal2.RM_MERCHANTSAY &&
                        ProcessMsg.Payload is byte[] rawMerchantBody)
                        SendSocket(m_DefMsg, rawMerchantBody);
                    else if (ProcessMsg.wIdent == Grobal2.RM_SYSMESSAGE &&
                             ProcessMsg.Payload is byte[] rawSystemBody)
                    {
                        if (!m_boGhost)
                            SendSocket(m_DefMsg, rawSystemBody);
                    }
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
                    // MOVE-85: 原生换图 caller 0x6B96C2（SM_CHANGEMAP=634 之后）同样调 sub_6B6BEC，
                    // 特征字之后紧跟进图通告。详见 TPlayObject.NativeMapEntryStatus.cs 字节表。
                    SendNativeMapEntryStateMessages();
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
                        var nativePayload = ProcessMsg.Payload as
                            NativeMagicFireRelayPayload;
                        m_DefMsg = nativePayload == null
                            ? Grobal2.MakeDefaultMsg(Grobal2.SM_MAGICFIRE,
                                ProcessMsg.BaseObject,
                                HUtil32.LoWord(ProcessMsg.nParam1),
                                HUtil32.HiWord(ProcessMsg.nParam1),
                                ProcessMsg.wParam)
                            : Grobal2.MakeDefaultMsg(Grobal2.SM_MAGICFIRE,
                                ProcessMsg.BaseObject, ProcessMsg.nParam1,
                                ProcessMsg.nParam2, ProcessMsg.wParam);
                        var body = new byte[8];
                        Buffer.BlockCopy(BitConverter.GetBytes(ProcessMsg.nParam3), 0, body, 0, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(nativePayload == null
                                ? ProcessMsg.nParam2
                                : nativePayload.EffectiveLevel),
                            0, body, 4, 4);
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
                    // 0x6B5277 push word[rec+8] (Param) / 0x6B527C push 0 (Tag) /
                    // 0x6B527E push 1 (Series): Param carries LoWord(nParam2) and Series
                    // is the literal 1, not the other way round.
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SENDGOODSLIST,
                        ProcessMsg.nParam1, HUtil32.LoWord(ProcessMsg.nParam2), 0, 1);
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
                    // 0x6B538F push word[rec+8] (Param) / 0x6B5394 push word[rec+0xC] (Tag)
                    // / 0x6B5399 push 0 (Series): Param carries LoWord(nParam2) and Series
                    // is zero.
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SENDDETAILGOODSLIST,
                        ProcessMsg.nParam1, HUtil32.LoWord(ProcessMsg.nParam2),
                        HUtil32.LoWord(ProcessMsg.nParam3), 0);
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
                        // RM 10146 arm 0x006B5568: nParam2 goes in Series, Param stays 0.
                    //   006B55B1  6A 00        push 0                     ; Param  = 0
                    //   006B55B3  6A 00        push 0                     ; Tag    = 0
                    //   006B55B5  66 8B 43 08  mov ax,[ebx+8] / 50 push   ; Series = LoWord(nParam2)
                    //   006B55BA  6A 00        push 0                     ; sMsg   = nil
                    //   006B55BC  8B 4B 04     mov ecx,[ebx+4]            ; Recog  = nParam1
                    //   006B55BF  66 BA BC 02  mov dx,0x2BC
                    //   006B55C8  FF 93 50 02 00 00  call [ebx+0x250]
                    SendDefMessage(Grobal2.SM_SENDUSERSTORAGEITEM, ProcessMsg.nParam1, 0, 0, ProcessMsg.nParam2, "");
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
                    if (ProcessMsg.Payload is byte[] nativeBody)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(
                            Grobal2.SM_DELITEMS, ProcessMsg.nParam1, 0, 0, 0);
                        SendSocket(m_DefMsg, nativeBody);
                    }
                    else if (ProcessMsg.Payload is IList<TDeleteItem> delItemList)
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
                    // Native has exactly one ident-708 emitter, sub_6B6BEC, and it puts the
                    // accumulated zone bitmask in nRecog with Param/Tag/Series all zero:
                    //   006B6BF4  33 F6                 xor esi,esi
                    //   006B6C02  83 CE 01              or  esi,1        ; Envir[+0x5D]
                    //   006B6C0B  83 CE 02              or  esi,2        ; Envir[+0x5C]
                    //   006B6C4E  83 CE 08              or  esi,8
                    //   006B6C7E  83 CE 20              or  esi,0x20
                    //   006B6C81  6A 00 x4              ; Param=Tag=Series=0, sMsg=nil
                    //   006B6C89  8B CE                 mov ecx,esi      ; nRecog = bitmask
                    //   006B6C8B  66 BA C4 02           mov dx,0x2C4
                    //   006B6C93  FF 96 50 02 00 00     call [esi+0x250]
                    // RefUserState() is that function. Sending GetMyStatus() (hunger, 0..4)
                    // in Param instead made the client read hunger as zone bits.
                    RefUserState();
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
                    // Series is wParam verbatim, no light byte.  RM 10331 arm 0x006B59E6:
                    //   006B59E6  66 8B 43 04  mov ax,[ebx+4]  / push   ; Param  = nParam1
                    //   006B59EB  66 8B 43 08  mov ax,[ebx+8]  / push   ; Tag    = nParam2
                    //   006B59F0  66 8B 43 02  mov ax,[ebx+2]  / push   ; Series = wParam
                    //   006B59F8  66 B9 21 03  mov cx,0x321    / call 0x006BCE54
                    // The sole producer of RM 10331/10336 is 0x00768ED6, and it zero-extends
                    // the direction into the wParam slot:
                    //   00768EEE  33 C9              xor ecx,ecx
                    //   00768EF0  8A 8B 54 01 00 00  mov cl,[ebx+0x154]   ; direction
                    //   00768EFC  FF 96 D8 00 00 00  call [esi+0xD8] (=0x0076533C)
                    // 0x0076533C forwards it unchanged (0x00765501 mov ax,[ebp-6] / push) to
                    // 0x00765E68, which stores it at 0x00765E9D mov [rec+2],ax.  High byte 0.
                    if (ProcessMsg.wIdent == Grobal2.RM_SPACEMOVE_SHOW)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SPACEMOVE_SHOW, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
                    }
                    else
                    {
                        // RM 10336 arm 0x006B5B7A is byte-identical to the 10331 arm:
                        //   006B5B7A/7F/84  mov ax,[ebx+4]/[ebx+8]/[ebx+2] / push
                        //   006B5B8C  66 B9 27 03  mov cx,0x327 / call 0x006BCE54
                        // Same producer 0x00768ED0, same zero-extended direction in wParam.
                        m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SPACEMOVE_SHOW2, ProcessMsg.BaseObject, ProcessMsg.nParam1, ProcessMsg.nParam2, ProcessMsg.wParam);
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
                    // 眼神「下线宝宝死亡」就打在这一条分支的守卫上：
                    //   0x006B5B9D  83 7B 04 00        cmp dword [ebx+4], 0
                    //   0x006B5BA1  0F 84 A5 06 00 00  je  0x006B624C
                    //   0x006B5BA7  8B 53 04           mov edx,[ebx+4]
                    //   0x006B5BAD  E8 12 5B 01 00     call 0x006CB6C4   ← 恢复从宠
                    // 开关打开时 0x0076...→ 0x006B5BA1 被换成
                    //   E9 A6 06 00 00 90  jmp 0x006B624C + nop
                    // （安装点 0x100AB10B，还原支 0x100AB19B 写回 0F 84 A5 06 00 00，
                    //   门控 0x100AB0AA cmp [edi+0xBF0],0 / je 0x100AB13D），
                    // 也就是这条分支恒不执行、存档里的从宠不再被重建。
                    // 0x006CB6C4 全镜像只有 0x006B5BAD 一个调用者，与 C# 侧
                    // ChangeServerMakeSlave 只被这里调用一一对应；两者形状一致
                    //（0x006CB6E7 cmp byte[eax+0x72],2 → 1 : 5 == m_btJob==jTaos → 1 : 5）。
                    if (ProcessMsg.Payload is TSlaveInfo slaveInfo &&
                        !new YanshenApi(this, null, M2Share.PluginManager).IsPetDieOffline())
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
                    // RM 10414 arm 0x006B5C23 pushes HP first and the literal 1 as Series:
                    //   006B5C38  66 8B 86 AC 02 00 00  mov ax,[esi+0x2AC] / push  ; Param  = HP
                    //   006B5C40  66 8B 86 B0 02 00 00  mov ax,[esi+0x2B0] / push  ; Tag    = MaxHP
                    //   006B5C48  6A 01                 push 1                     ; Series = 1
                    //   006B5C4A  8D 45 F0              lea eax,[ebp-0x10] / push  ; Buf
                    //   006B5C4E  6A 08                 push 8                     ; Len
                    // The two offsets are pinned by the SM 1100 arm, which C# already matches
                    // as Param=HP / Tag=MaxHP.
                    m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_INSTANCEHEALGUAGE,
                        ProcessMsg.BaseObject, HUtil32.LoWord(gaugeHp),
                        HUtil32.LoWord(gaugeMaxHp), 1);
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
                    // Native handler 0x6DBB8A -> sub_6F9648(eax=self, edx=Recog,
                    // ecx=MakeLong(Tag,Param), [ebp+8]=Series). The gate chain is
                    // exactly these seven steps, every failure falling to the silent
                    // exit at 0x6F9711:
                    //   0x6F9672  E8 E1 03 F5 FF  call 0x649A58   ; esi = object by Recog
                    //   0x6F9679  85 F6 / 0F 84   test esi,esi    ; null -> out
                    //   0x6F9681  8B 8B D8 0C 00 00  mov ecx,[self+0xCD8]
                    //   0x6F9687  85 C9 / 0F 84   test ecx,ecx    ; no open NPC -> out
                    //   0x6F968F  3B BB D8 0C 00 00  cmp edi,[self+0xCD8]
                    //   0x6F9695  75 7A              jne             ; not the open NPC -> out
                    //   0x6F969D  3B 83 28 01 00 00  cmp [npc+0x128],[self+0x128]
                    //   0x6F96BA  83 F8 0F / 7F 52   cmp abs(dX),0xF / jg  ; reject only when > 15
                    //   0x6F96D0  83 F8 0F / 7F 3C   cmp abs(dY),0xF / jg
                    //   0x6F96DA  E8 29 38 04 00     call 0x73CF08   ; item by id, null -> out
                    // sub_6F9648 reads neither [self+0x73] nor [self+0x74], and an
                    // exhaustive scan of the dispatcher body 0x6D7D68..0x6DBC2C finds
                    // zero references to either offset, so there is no death/ghost gate
                    // on this path at any level -- CM 4629's callee 0x6F7C40 does its own
                    // test at 0x6F7C8D/0x6F7C9A precisely because the dispatcher has none.
                    // The handler also never pushes the body string, so the native item
                    // match at 0x73CF40 `cmp [item+0x18],id` is by id alone.
                    if (M2Share.PasEngine == null)
                    {
                        break;
                    }

                    var npc = GetMerchantQueryNpc(ProcessMsg.nParam1);
                    if (npc == null || m_NPC == null || !ReferenceEquals(npc, m_NPC))
                    {
                        break;
                    }
                    if (npc.m_PEnvir != m_PEnvir ||
                        Math.Abs(npc.m_nCurrX - m_nCurrX) > 15 ||
                        Math.Abs(npc.m_nCurrY - m_nCurrY) > 15)
                    {
                        break;
                    }

                    var clientItemId = HUtil32.MakeLong(ProcessMsg.nParam2, ProcessMsg.nParam3);
                    var commitItem = FindClientItemIn(m_ItemList, clientItemId, false)
                                     ?? FindClientItemIn(m_ItemList, clientItemId, true);
                    if (commitItem == null)
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
                    // 原生分发臂 0x6D9324 起，门序就是下面这三步：
                    //   6D9327  83 B8 B0 0B 00 00 00  cmp dword [player+0xBB0],0
                    //   6D932E  0F 85 F8 28 00 00     jne 0x6DBC2C      ; 英雄已在场 -> 静默
                    //   6D9334  8B 45 CC              mov eax,[msg]
                    //   6D9337  66 83 78 06 01        cmp word [msg+6],1 ; Param==1 = 副将槽
                    //   6D933C  75 60                 jne 0x6D939E      ; 主将槽跳过下面这道门
                    //   6D933E  B9 03 00 00 00        mov ecx,3          ; index
                    //   6D9343  BA 57 00 00 00        mov edx,0x57       ; group 87
                    //   6D934B  E8 94 5E 00 00        call 0x6DF1E4      ; GetV
                    //   6D9350  83 F8 64              cmp eax,0x64       ; == 100 ?
                    //   6D9353  75 30                 jne 0x6D9385       ; -> 拒绝并提示
                    //   6D9385  66 B9 FF 38 / BA 68 BF 6D 00 / call [vmt+0xD4]
                    // 0x6DBF68 是 declen 20 的 GBK 串「请先召唤一次主将英雄」。
                    // GetV(sub_6DF1E4) 的线性槽是 0x6E42CC `imul eax,edx,0x3E8 / add eax,ecx`，
                    // 即 group*1000+index，所以 edx=group=87、ecx=index=3。未命中时
                    // 0x6DF1F1 `mov [ebp-4],0xFFFFFFFF` 让它答 -1，而 -1 != 100 也是拒绝。
                    // Param/Tag -> [player+0x9BD]/[player+0x9BE]（0x6D93A7/0x6D93B6），
                    // 即 DB 请求 0x160 里的 HeroKind/HeroSlot 两个字段。
                    if (m_HeroObject == null)
                    {
                        if (ProcessMsg.nParam2 == 1 && !NativeViceHeroSummonAllowed())
                            break;
                        // Recog 的校验在原生 sub_6CC7C8 内部（0x6CC874 cmp ebx,[ebp-4]）。
                        if (ProcessMsg.nParam1 == ObjectId)
                        {
                            m_btNativeHeroRequestKind = (byte)ProcessMsg.nParam2;
                            m_btNativeHeroRequestSlot = (byte)ProcessMsg.nParam3;
                            HeroDataService.RequestLoad(this,
                                (byte)ProcessMsg.nParam2, (byte)ProcessMsg.nParam3);
                        }
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
                    var physicalFrame = ProcessMsg.Payload as
                        NativePhysicalAttackFramePayload;
                    if (ProcessMsg.BaseObject != ObjectId ||
                        physicalFrame?.IncludeSource == true)
                    {
                        m_DefMsg = Grobal2.MakeDefaultMsg(
                            Grobal2.SM_PHYSICAL_ATT,
                            ProcessMsg.BaseObject, ProcessMsg.wParam,
                            ProcessMsg.nParam1, ProcessMsg.nParam2);
                        SendSocket(m_DefMsg, physicalFrame?.Body ??
                            GetQueuedPayloadBytes(ProcessMsg));
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
                    break;
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
                    if (!TryHandleInlayCm(ProcessMsg)
                        && !TryHandleQiankunCm(ProcessMsg)
                        && !TryHandleItemTransferCm(ProcessMsg)
                        && !TryHandleStallWriteCm(ProcessMsg)
                        && !TryHandleEquipLockCm(ProcessMsg)
                        && !TryHandleQuizBroadcastCm(ProcessMsg)
                        && !TryHandleCloneNpcCm(ProcessMsg)
                        && !TryHandleMallCm(ProcessMsg)
                        && !TryHandleNameQueryCm(ProcessMsg)
                        && !TryHandleNewbieQuestCm(ProcessMsg)
                        && !TryHandleSoulWashCm(ProcessMsg)
                        && !TryHandleYbConsignWriteCm(ProcessMsg)
                        && !TryHandleMemberRosterCm(ProcessMsg)
                        && !TryHandleHeroSpiritBeadCm(ProcessMsg)
                        && !TryHandleRewardCm(ProcessMsg)
                        && !TryHandleMessageBoardCm(ProcessMsg)
                        && !TryHandleFreeRecycleCm(ProcessMsg)
                        && !TryHandleTimedActivityCm(ProcessMsg)
                        && !TryHandleSkillStoneCm(ProcessMsg)
                        && !TryHandleHeroNotifyCm(ProcessMsg)
                        && !TryHandleHorseTokenCm(ProcessMsg)
                        && !TryHandleCmMiscTail(ProcessMsg)
                        && !TryHandleTaskBoardScriptCm(ProcessMsg)
                        && !TryHandleNativeSocialProtocol(ProcessMsg)
                        && !TryHandleNativeCmTailProtocol(ProcessMsg)
                        && !TryHandleNativeCmQ1(ProcessMsg)
                        && !TryHandleNativeCmQ2(ProcessMsg)
                        && !TryHandleNativeCmQ3(ProcessMsg))
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
                // 战神 sub_73FC70 的序言同样没有任何早退。0x73FC70..0x73FCB6：
                //   73FC70  55 / 8B EC / 83 C4 8C          栈帧
                //   73FC76  53 56 57                       push ebx,esi,edi
                //   73FC79  33 D2 + 七条 mov [ebp-…],edx   七个局部清零
                //   73FC90  8B F0                          esi := self
                //   73FC94  55 / 68 01 00 74 00 / 64 FF 30 / 64 89 20   SEH 帧
                //   73FCA0  33 C0 / 89 45 F4 / C6 45 FF 00 计数与红名局部清零
                //   73FCA9  A1 AC 5F 7D 00 / 8B 00 / 3B 86 60 01 00 00
                //   73FCB6  7D 09                          jge 0x73FCC1  ← 全函数第一条条件跳转
                // 原先这里的 `m_boAngryRing || m_boNoDropUseItem` 早退在原生无对应，
                // 全镜像多编码零命中（GBK / 裸 ASCII 大小写不敏感 / UTF-16LE 三路皆 0）。
                // 按 §3.1 删除：原版死亡就是照掉装备，没有不死戒指这一说。
                GoodItem StdItem;
                // 人物爆率调整 patches sub_73FC70, not a runtime multiplier:
                //   0x100B9CCC A3 BB FC 73 00 -> imm32 of 0x73FCB8 C7 45 F8 15 00 00 00 (red K)
                //   0x100B9C5E A2 C9 FC 73 00 -> imm8  of 0x73FCC7 83 C0 5A             (non-red K)
                //   0x100B9D3A A2 6C FF 73 00 -> imm8  of 0x73FF69 83 7D F4 02          (max-1)
                // Off leaves C#'s existing 15/30 path (host 21/90 is a separate BLOCKED).
                // PKD-01 红名判据。战神 sub_73FC70 @0x73FCA9:
                //   73FCA9  A1 AC 5F 7D 00     mov eax,[0x7D5FAC]   ; -> 0x7DCF00 = 200
                //   73FCAE  8B 00              mov eax,[eax]
                //   73FCB0  3B 86 60 01 00 00  cmp eax,[esi+0x160]  ; 阈值 vs MyPKpoint
                //   73FCB6  7D 09              jge 0x73FCC1         ; 阈值 >= PK -> 非红名
                //   73FCB8  C7 45 F8 15 …      mov [ebp-8],0x15     ; 红名分母 21
                // `jge` 只在 阈值 < PK 时不跳，所以红名判据是**严格** PK > 200。
                // 旧写法 `PKLevel()>2` 等价于 PK >= 300，PK 落在 201..299 的玩家整段判错。
                // 注意与背包 worker sub_740078 @0x7400BE `setle` (PK >= 200) 差一点，
                // 原生这两处本来就不一致，不能统一。
                var dropCount = 0;
                var nativeRedName = m_nPkPoint > M2Share.g_Config.nPKPunishPoint;
                var deathDropPatched = new YanshenApi(this, null, M2Share.PluginManager)
                    .TryGetDeathEquipDropPatch(nativeRedName, out var patchedRate, out var patchedCap);
                // PKD-02 落地件数上限。战神 0x73FF69 `83 7D F4 02  cmp [ebp-0xC],2` /
                // 0x73FF6D `7F 0A  jg 0x73FF79` —— 这条**无条件存在**，不是眼神补丁加的；
                // 眼神只改了那个立即数 (0x100B9D3A A2 6C FF 73 00 -> imm8 of 0x73FF69)。
                // C# 之前只在补丁生效时才计数并 break，没插件的服务器 16 个装备位全过筛，
                // 一次死亡最多能爆 16 件而原生最多 3 件。
                var nativeDropCap = deathDropPatched ? patchedCap : 2;
                var nativeFeatureChanged = false;
                var nativeEncounteredEquip = false;
                // 分母。战神 sub_73FC70 @0x73FCA9-0x73FD0D：红名恒 21（0x73FCB8 的 imm32
                // 0x15），非红是 `[self+0x18C] + 0x5A(90)`（0x73FCC1/0x73FCC7），随后在
                // LastHiter 是 THumanKind 时减 `byte [LastHiter+0x579]`（0x73FD02/0x73FD08），
                // 最后 0x73FD0B 下钳 0。两个输入的完整来源链见
                // TBaseObject.NativeDeathDropDenominator.cs 的文件头。
                // 这里原先读的是 nDieRedDropUseItemRate(15) / nDieDropUseItemRate(30)：
                // 两个配置名在全镜像 GBK、裸 ASCII（大小写不敏感）、UTF-16LE 三路皆 0 命中，
                // 原生没有这对旋钮，数值也不对（15/30 vs 21/90）。
                var nRate = deathDropPatched
                    ? patchedRate
                    : NativeDeathEquipDropDenominator(nativeRedName, m_LastHiter);
                for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
                {
                    // PKD-03 抽签次数对齐。战神一次循环走 16 个装备位:
                    //   73FD2D  8B 86 C0 04 00 00  mov eax,[esi+0x4C0]   ; 装备容器
                    //   73FD33  E8 …               call sub_75EC20       ; 取第 ebx 格
                    //   73FD3A  85 FF              test edi,edi
                    //   73FD3C  0F 84 2D 02 00 00  je 0x73FF6F           ; 空格 -> 直接下一格
                    //   …                                               ; Reserved&8 走销毁支，也不抽
                    //   73FD96  8B 45 F8           mov eax,[ebp-8]
                    //   73FD99  E8 AE 3D CC FF     call sub_403B4C       ; ← 抽签在这里
                    // 即**只有非空且不带 Reserved&8 的格子才消耗一次 Random**。
                    // C# 原来把 Random 放在循环第一句，空格、以及上面刚被清成 wIndex=0
                    // 的格子也各抽一次，整条 LCG 序列相对原生错位，后续所有掉落判定全歪。
                    var candidate = m_UseItems[i];
                    if (candidate == null)
                    {
                        continue;
                    }
                    // 0x73FD3A test item / 0x73FD42 mov [ebp-2],1 occurs before
                    // the StdItem lookup. Even a corrupt/zero-index managed shell is
                    // therefore an encountered equipment object for the caller tail.
                    nativeEncounteredEquip = true;
                    if (candidate.wIndex <= 0)
                    {
                        continue;
                    }
                    StdItem = M2Share.UserEngine.GetStdItem(candidate.wIndex);
                    if (StdItem == null)
                    {
                        continue;
                    }

                    // 0x73FD46..0x73FD91: std[+2]&8 is handled in this slot,
                    // before Random(K). It removes the equipment immediately and
                    // jumps to the next slot without applying the cap check.
                    if ((StdItem.NativeReserved02 & 0x0008) != 0)
                    {
                        delList ??= new List<TDeleteItem>();
                        delList.Add(new TDeleteItem
                        {
                            MakeIndex = candidate.MakeIndex,
                            ClientItemID = EnsureClientItemId(candidate)
                        });

                        m_UseItems[i] = null;              // 0x75F2BB clears container[slot]
                        RecalcAbilitys();                  // 0x75F2C4..0x75F2D9
                        SendDelItems(candidate);           // 0x75F2FA call [vmt+0x268]
                        M2Share.AddNativeGameDataLog(this, 0x0A, StdItem.Name,
                            candidate.MakeIndex, 1, "死亡爆出消失");

                        if (i is 0 or 1 or 4 or 13)
                        {
                            // sub_75F27C performs this once; sub_73FC70 repeats it
                            // at the common tail through its [ebp-1] flag.
                            FeatureChanged();
                            nativeFeatureChanged = true;
                        }

                        Dispose(candidate);
                        dropCount++;
                        continue;
                    }

                    // 0x73FD99..0x73FDA9: every eligible slot consumes the
                    // draw, but item+0xFC bypasses a non-zero result.
                    if (M2Share.RandomNumber.Random(nRate) != 0
                        && candidate.NativeClassFc == 0)
                    {
                        continue;
                    }
                    // 原先这里还有一次 `InDisableTakeOffList(wIndex)` 查表。战神
                    // sub_73FC70 的整个循环体 0x73FD29..0x73FF73 里没有任何按物品编号
                    // 查表的动作，抽签之后紧接的就是分流：
                    //   73FD99  E8 AE 3D CC FF        call sub_403B4C   ; Random(K)
                    //   73FD9E  85 C0 / 74 0D         test eax,eax / je -> 通过
                    //   73FDA2  80 BF FC 00 00 00 00  cmp byte [item+0xFC],0 / 0F 84 …
                    //   73FDAF  80 BE 78 01 00 00 00  cmp byte [self+0x178],0
                    //   73FDB6  0F 85 12 01 00 00     jne 0x73FECE      ; 非玩家 -> 落地支
                    //   73FDBC  A1 34 65 7D 00 …      sub_617A38(…, cl=4) 实名认证
                    //   73FDD0  80 BF D8 00 00 00 00  cmp byte [item+0xD8],0  ; 赠品
                    // 剩下的过滤全部走 `[std+2]` / `[std+3]` 的位与 sub_78389C，
                    // 没有第二张按 wIndex 索引的名单。
                    // 全镜像多编码零命中：DisableTakeOffList / DisableTakeOffList.txt /
                    // TakeOffList 三个名字在 GBK、裸 ASCII（大小写不敏感）、UTF-16LE
                    // 三路皆 0。按 §3.1 删除。
                    var authenticated = NativeItemDropDestroyAuthenticated();
                    if (NativeItemDropDestroy.ShouldDestroy(
                            m_btRaceServer == Grobal2.RC_PLAYOBJECT,
                            authenticated, candidate))
                    {
                        // 0x73FDDD..0x73FDFE: the auth/gift arm destroys only
                        // std[+2]&0x10 items, and mode 5 may keep them first.
                        if ((StdItem.NativeReserved02 & 0x0010) == 0
                            || NativeItemDropDestroy.CheckTransferPermission(
                                candidate, StdItem,
                                NativeItemDropDestroy.TransferModeDrop) != 0)
                        {
                            continue;
                        }

                        delList ??= new List<TDeleteItem>();
                        delList.Add(new TDeleteItem
                        {
                            sItemName = M2Share.UserEngine.GetStdItemName(candidate.wIndex),
                            MakeIndex = candidate.MakeIndex,
                            ClientItemID = EnsureClientItemId(candidate)
                        });
                        TryNotifyNativeItemMovementSms(this, StdItem, candidate,
                            NativeItemMovementSmsDeathEvent);
                        var notice = NativeItemDropDestroy.BuildDestroyNotice(
                            NativeItemDropDestroyAuthenticated(), candidate,
                            NativeItemDropDestroy.DeathEquipUnverifiedNotice,
                            NativeItemDropDestroy.DeathEquipGiftNotice);
                        if (!string.IsNullOrEmpty(notice))
                        {
                            SysMsg(notice + " "
                                + M2Share.UserEngine.GetStdItemName(candidate.wIndex),
                                MsgColor.Red, MsgType.Hint);
                        }
                        // 0x73FEC4 frees the object but does not clear the native
                        // equipment slot; Dispose has the same no-clear behavior here.
                        Dispose(candidate);
                        dropCount++;
                        if (i is 0 or 1)
                        {
                            nativeFeatureChanged = true;
                        }
                        continue; // destroy skips the 0x73FF69 cap check
                    }

                    // 0x73FECE..0x73FED5: the normal ground arm has the
                    // opposite std[+2]&0x10 polarity.
                    if ((StdItem.NativeReserved02 & 0x0010) != 0)
                    {
                        continue;
                    }

                    // sub_73FC70 pushes nil for sub_7688A0's floor-owner argument;
                    // LastHiter only contributes to the separately built log label.
                    if (DropItemDown(candidate, 2, true, null, this))
                    {
                        delList ??= new List<TDeleteItem>();
                        delList.Add(new TDeleteItem
                        {
                            sItemName = M2Share.UserEngine.GetStdItemName(candidate.wIndex),
                            MakeIndex = candidate.MakeIndex,
                            ClientItemID = EnsureClientItemId(candidate)
                        });
                        // 0x73FF0B call sub_75F3E8(ecx=0): detach the slot only.
                        // The same item object now belongs to the ground map entry.
                        m_UseItems[i] = null;
                        if (i is 0 or 1 or 4 or 13)
                        {
                            nativeFeatureChanged = true;
                        }
                        TryNotifyNativeItemMovementSms(this, StdItem, candidate,
                            NativeItemMovementSmsDeathEvent);
                        // native 0x73FF66 FF 45 F4 inc [ebp-0xc] / 0x73FF69 83 7D F4 02 / 7F 0A jg
                        // 上限恒定生效（见 PKD-02）；眼神补丁只替换那个立即数。
                        dropCount++;
                        if (dropCount > nativeDropCap) break;
                    }
                }
                if (delList != null)
                {
                    SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0,
                        delList.Count, 0, 0, "", delList);
                }
                if (nativeFeatureChanged)
                {
                    FeatureChanged();
                }
                // 0x73FFB3..0x73FFD4: any non-null equipment object, then the
                // VMT+B4 owner (self for players) must have at least one bag item.
                if (nativeEncounteredEquip && m_ItemList.Count > 0)
                {
                    TryNativeDeathDropAreaNotice(dropCount, this);
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
            //   0x6D09E3  cmp byte [ebx+0x73],0   ; m_boGhost   -> -1
            //   0x6D09ED  cmp byte [ebx+0x461],0  ; m_boDealing -> -1   <-- WAS MISSING
            //   0x6D09FA  cmp dword [ebx+0xBB0],0 ; hero == nil -> -1
            //   0x6D0A0D  call sub_772DA8         ; hero death [+0x74] -> -1
            // Without the m_boDealing gate a player could stage an item in a trade and
            // then shunt the same object reference into the hero bag: the deal list and
            // the hero bag both hold it, the deal completes and hands it to the
            // counterparty while the hero bag keeps its copy -> two-container dupe.
            if (m_HeroObject == null || m_boGhost || m_boDealing
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
            //   0x6D0B13  cmp byte [ebx+0x73],0   ; m_boGhost   -> -1
            //   0x6D0B1D  cmp byte [ebx+0x461],0  ; m_boDealing -> -1   <-- WAS MISSING
            //   0x6D0B2A  cmp dword [ebx+0xBB0],0 ; hero == nil -> -1
            //   0x6D0B3D  call sub_772DA8         ; hero death [+0x74] -> -1
            if (m_HeroObject == null || m_boGhost || m_boDealing
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
