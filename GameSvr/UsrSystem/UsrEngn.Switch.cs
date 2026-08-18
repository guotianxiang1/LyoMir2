using SystemModule;

namespace GameSvr
{
    public partial class UserEngine
    {
        private TSwitchDataInfo GetSwitchData(string sChrName, int nCode)
        {
            TSwitchDataInfo result = null;
            TSwitchDataInfo SwitchData = null;
            for (var i = 0; i < m_ChangeServerList.Count; i++)
            {
                SwitchData = m_ChangeServerList[i];
                if (string.Compare(SwitchData.sChrName, sChrName, StringComparison.OrdinalIgnoreCase) == 0 && SwitchData.nCode == nCode)
                {
                    result = SwitchData;
                    break;
                }
            }
            return result;
        }

        private void LoadSwitchData(TSwitchDataInfo SwitchData, ref TPlayObject PlayObject)
        {
            int nCount;
            TSlaveInfo SlaveInfo;
            if (SwitchData.boC70)
            {

            }
            PlayObject.m_boBanShout = SwitchData.boBanShout;
            PlayObject.m_boHearWhisper = SwitchData.boHearWhisper;
            PlayObject.m_boBanGuildChat = SwitchData.boBanGuildChat;
            PlayObject.m_boBanGuildChat = SwitchData.boBanGuildChat;
            PlayObject.m_boAdminMode = SwitchData.boAdminMode;
            PlayObject.m_boObMode = SwitchData.boObMode;
            nCount = 0;
            while (SwitchData.BlockWhisperArr != null
                   && nCount < SwitchData.BlockWhisperArr.Count)
            {
                var blockedName = SwitchData.BlockWhisperArr[nCount];
                if (string.IsNullOrEmpty(blockedName)) break;
                PlayObject.m_BlockWhisperList.Add(blockedName);
                nCount++;
            }

            // sub_6B188C @0x6B1928..0x6B197E scans all five fixed slots.
            // An empty slot is skipped, not a terminator, so sparse records keep
            // every later summon. The native delay is 0x5DC = 1500 ms.
            for (nCount = 0; nCount < TSwitchDataInfo.NativeSlaveSlotCount; nCount++)
            {
                SlaveInfo = SwitchData.SlaveArr != null
                            && nCount < SwitchData.SlaveArr.Length
                    ? SwitchData.SlaveArr[nCount]
                    : null;
                if (SlaveInfo == null || string.IsNullOrEmpty(SlaveInfo.sSlaveName))
                    continue;
                PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_10401,
                    0, 0, 0, 0, "", 1500, SlaveInfo);
            }

            for (nCount = 0; nCount < TSwitchDataInfo.NativeStatusSlotCount; nCount++)
            {
                PlayObject.m_wStatusArrValue[nCount] = SwitchData.StatusValue != null
                                                       && nCount < SwitchData.StatusValue.Length
                    ? SwitchData.StatusValue[nCount]
                    : (ushort)0;
                PlayObject.m_dwStatusArrTimeOutTick[nCount] =
                    SwitchData.StatusTimeOut != null
                    && nCount < SwitchData.StatusTimeOut.Length
                        ? SwitchData.StatusTimeOut[nCount]
                        : 0;
            }
        }

        public void AddSwitchData(TSwitchDataInfo SwitchData)
        {
            SwitchData.dwWaitTime = HUtil32.GetTickCount();
            m_ChangeServerList.Add(SwitchData);
        }

        private void DelSwitchData(TSwitchDataInfo SwitchData)
        {
            TSwitchDataInfo SwitchDataInfo;
            for (var i = 0; i < m_ChangeServerList.Count; i++)
            {
                SwitchDataInfo = m_ChangeServerList[i];
                if (SwitchDataInfo == SwitchData)
                {
                    SwitchDataInfo = null;
                    m_ChangeServerList.RemoveAt(i);
                    break;
                }
            }
        }

        private bool SendSwitchData(TPlayObject PlayObject, int nServerIndex)
        {
            TSwitchDataInfo SwitchData = null;
            MakeSwitchData(PlayObject, ref SwitchData);
            var flName = "$_" + M2Share.nServerIndex + "_$_" + M2Share.ShareFileNameNum + ".shr";
            PlayObject.m_sSwitchDataTempFile = flName;
            SendServerGroupMsg(Grobal2.ISM_USERSERVERCHANGE, nServerIndex, flName);//发送消息切换服务器
            M2Share.ShareFileNameNum++;
            return true;
        }

        private void MakeSwitchData(TPlayObject PlayObject, ref TSwitchDataInfo SwitchData)
        {
            SwitchData = new TSwitchDataInfo();
            SwitchData.sChrName = PlayObject.m_sCharName;
            SwitchData.sMap = PlayObject.m_sMapName;
            SwitchData.wX = PlayObject.m_nCurrX;
            SwitchData.wY = PlayObject.m_nCurrY;
            SwitchData.Abil = PlayObject.m_Abil;
            SwitchData.nCode = PlayObject.m_nSessionID;
            SwitchData.boBanShout = PlayObject.m_boBanShout;
            SwitchData.boHearWhisper = PlayObject.m_boHearWhisper;
            SwitchData.boBanGuildChat = PlayObject.m_boBanGuildChat;
            SwitchData.boBanGuildChat = PlayObject.m_boBanGuildChat;
            SwitchData.boAdminMode = PlayObject.m_boAdminMode;
            SwitchData.boObMode = PlayObject.m_boObMode;
            for (var i = 0; i < PlayObject.m_BlockWhisperList.Count; i++)
            {
                SwitchData.BlockWhisperArr.Add(PlayObject.m_BlockWhisperList[i]);
            }
            var written = 0;
            for (var i = 0; i < PlayObject.m_SlaveList.Count; i++)
            {
                var baseObject = PlayObject.m_SlaveList[i];
                if (baseObject == null || baseObject.m_boDeath || baseObject.m_boGhost)
                    continue;
                if (PlayObject.m_HeroObject?.IsNativeHeroSummonSlave(baseObject) == true)
                    continue;
                if (written >= TSwitchDataInfo.NativeSlaveSlotCount)
                    break;

                var target = SwitchData.SlaveArr[written++];
                target.sSlaveName = baseObject.m_sCharName;
                target.nKillCount = baseObject.m_nKillMonCount;
                target.btSlaveLevel = baseObject.m_btSlaveMakeLevel;
                target.btSlaveExpLevel = baseObject.m_btSlaveExpLevel;
                target.dwRoyaltySec = unchecked((int)(unchecked((uint)(
                    baseObject.m_dwMasterRoyaltyTick - HUtil32.GetTickCount())) / 1000u));
                target.nHP = unchecked((ushort)baseObject.m_WAbil.HP);
                target.nMP = unchecked((ushort)baseObject.m_WAbil.MP);
            }
            for (var i = 0; i < TSwitchDataInfo.NativeStatusSlotCount; i++)
            {
                if (i < PlayObject.m_wStatusArrValue.Length
                    && i < PlayObject.m_dwStatusArrTimeOutTick.Length
                    && PlayObject.m_wStatusArrValue[i] > 0)
                {
                    SwitchData.StatusValue[i] = PlayObject.m_wStatusArrValue[i];
                    SwitchData.StatusTimeOut[i] = PlayObject.m_dwStatusArrTimeOutTick[i];
                }
            }
        }


        public void CheckSwitchServerTimeOut()
        {
            for (var i = m_ChangeServerList.Count - 1; i >= 0; i--)
            {
                if ((HUtil32.GetTickCount() - m_ChangeServerList[i].dwWaitTime) > 30 * 1000)
                {
                    m_ChangeServerList[i] = null;
                    m_ChangeServerList.RemoveAt(i);
                }
            }
        }

    }
}
