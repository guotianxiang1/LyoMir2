using SystemModule;
using System.Runtime.CompilerServices;

namespace GameSvr
{
    public class TFrontEngine
    {
        private sealed class LoadRequestState
        {
            public readonly object SyncRoot = new();
            public volatile bool Cancelled;
        }

        private readonly object m_UserCriticalSection = null;
        private IList<TLoadDBInfo> m_LoadRcdList = null;
        private readonly IList<TSaveRcd> m_SaveRcdList = null;
        private readonly IList<TGoldChangeInfo> m_ChangeGoldList = null;
        private IList<TLoadDBInfo> m_LoadRcdTempList = null;
        private readonly IList<TSaveRcd> m_SaveRcdTempList = null;
        private readonly Thread _frontEngine;
        private volatile bool _stopRequested;
        private int _dbDisconnectedLogTick;
        private long _saveGeneration;
        private readonly ConditionalWeakTable<TLoadDBInfo, LoadRequestState> _loadRequestStates = new();
        private TLoadDBInfo _activeLoadRcd;

        public TFrontEngine()
        {
            m_UserCriticalSection = new object();
            m_LoadRcdList = new List<TLoadDBInfo>();
            m_SaveRcdList = new List<TSaveRcd>();
            m_ChangeGoldList = new List<TGoldChangeInfo>();
            m_LoadRcdTempList = new List<TLoadDBInfo>();
            m_SaveRcdTempList = new List<TSaveRcd>();
            _frontEngine = new Thread(Execute)
            {
                IsBackground = true
            };
        }

        public void Start()
        {
            _stopRequested = false;
            _frontEngine.Start();
        }

        public void Stop()
        {
            _stopRequested = true;
            if (_frontEngine.IsAlive && _frontEngine != Thread.CurrentThread)
                _frontEngine.Join();
        }

        private void Execute()
        {
            const string sExceptionMsg = "[Exception] TFrontEngine::Execute";
            while (!_stopRequested)
            {
                try
                {
                    ProcessGameDate();
                    GetGameTime();
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage(sExceptionMsg);
                    M2Share.ErrorMessage(ex.StackTrace);
                }
                Thread.Sleep(1);
            }
        }

        private void GetGameTime()
        {
            M2Share.g_nGameTime = GetGameTimeValue(DateTime.Now.Hour);
        }

        private static int GetGameTimeValue(int hour) => hour switch
        {
            4 or 15 => 0,
            >= 5 and <= 10 or >= 16 and <= 22 => 1,
            11 or 23 => 2,
            >= 0 and <= 3 or >= 12 and <= 14 => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(hour))
        };

        public bool IsIdle()
        {
            var result = false;
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                if (m_SaveRcdList.Count == 0 && m_ChangeGoldList.Count == 0)
                {
                    result = true;
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }
            return result;
        }

        public int SaveListCount()
        {
            var result = 0;
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                result = m_SaveRcdList.Count;
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }
            return result;
        }

        public int GoldChangeListCount()
        {
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                return m_ChangeGoldList.Count;
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }
        }

        private void ProcessGameDate()
        {
            IList<TGoldChangeInfo> ChangeGoldList = null;
            TSaveRcd SaveRcd = null;
            var boReTryLoadDB = false;
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                m_SaveRcdTempList.Clear();
                if (m_SaveRcdList.Any())
                {
                    for (var i = 0; i < m_SaveRcdList.Count; i++)
                    {
                        m_SaveRcdTempList.Add(m_SaveRcdList[i]);
                    }
                }
                IList<TLoadDBInfo> TempList = m_LoadRcdTempList;
                m_LoadRcdTempList = m_LoadRcdList;
                m_LoadRcdList = TempList;
                if (m_ChangeGoldList.Any())
                {
                    ChangeGoldList = new List<TGoldChangeInfo>();
                    for (var i = 0; i < m_ChangeGoldList.Count; i++)
                    {
                        ChangeGoldList.Add(m_ChangeGoldList[i]);
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }
            if (HumDataService.DBSocketConnected())
            {
                for (var i = 0; i < m_SaveRcdTempList.Count; i++)
                {
                    SaveRcd = m_SaveRcdTempList[i];
                    if (SaveRcd == null)
                    {
                        continue;
                    }
                    var currentTick = HUtil32.GetTickCount();
                    if (currentTick - SaveRcd.NextRetryTick < 0)
                        continue;
                    if (HumDataService.SaveHumRcdToDB(SaveRcd.sAccount,
                             SaveRcd.sChrName, SaveRcd.NativeSaveMode,
                             SaveRcd.NativeSaveParam1, SaveRcd.NativeSaveParam2,
                             SaveRcd.HumanRcd, SaveRcd.NativeSwitchExtension))
                    {
                        var removedCurrent = false;
                        HUtil32.EnterCriticalSection(m_UserCriticalSection);
                        try
                        {
                            for (var j = 0; j < m_SaveRcdList.Count; j++)
                            {
                                if (m_SaveRcdList[j] == SaveRcd)
                                {
                                    m_SaveRcdList.RemoveAt(j);
                                    removedCurrent = true;
                                    DisPose(SaveRcd);
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            HUtil32.LeaveCriticalSection(m_UserCriticalSection);
                        }
                        if (removedCurrent && SaveRcd.PlayObject != null
                            && (!SaveRcd.PlayObject.m_boSwitchData
                                || SaveRcd.NativeSaveMode == 2))
                            SaveRcd.PlayObject.m_boRcdSaved = true;
                    }
                    else
                    {
                        var retry = Math.Min(5, ++SaveRcd.nReTryCount);
                        var delay = retry >= 5 ? 15_000 : 1_000 << (retry - 1);
                        currentTick = HUtil32.GetTickCount();
                        SaveRcd.NextRetryTick = currentTick + delay;
                        HUtil32.EnterCriticalSection(m_UserCriticalSection);
                        try
                        {
                            for (var j = 0; j < m_SaveRcdList.Count; j++)
                            {
                                var current = m_SaveRcdList[j];
                                if (!SameSaveKey(current, SaveRcd) || ReferenceEquals(current, SaveRcd))
                                    continue;
                                current.nReTryCount = Math.Max(current.nReTryCount, SaveRcd.nReTryCount);
                                current.NextRetryTick = Math.Max(current.NextRetryTick, SaveRcd.NextRetryTick);
                                current.LastErrorLogTick = Math.Max(current.LastErrorLogTick,
                                    SaveRcd.LastErrorLogTick);
                                break;
                            }
                        }
                        finally
                        {
                            HUtil32.LeaveCriticalSection(m_UserCriticalSection);
                        }
                        if (currentTick - SaveRcd.LastErrorLogTick >= 10_000)
                        {
                            SaveRcd.LastErrorLogTick = currentTick;
                            M2Share.ErrorMessage(
                                $"[FrontEngine] 人物存档失败，保留队列等待重试: " +
                                $"{SaveRcd.sChrName}, retry={SaveRcd.nReTryCount}, delay={delay}ms");
                        }
                    }
                }
            }
            else
            {
                var currentTick = HUtil32.GetTickCount();
                var saveCount = SaveListCount();
                if (saveCount > 0 && currentTick - _dbDisconnectedLogTick > 10_000)
                {
                    _dbDisconnectedLogTick = currentTick;
                    M2Share.ErrorMessage(
                        $"DBSvr 断开连接，保留 {saveCount} 条人物存档等待重试。");
                }
            }
            m_SaveRcdTempList.Clear();
            while (true)
            {
                TLoadDBInfo LoadDBInfo;
                HUtil32.EnterCriticalSection(m_UserCriticalSection);
                try
                {
                    if (m_LoadRcdTempList.Count == 0)
                    {
                        break;
                    }
                    LoadDBInfo = m_LoadRcdTempList[0];
                    m_LoadRcdTempList.RemoveAt(0);
                    _activeLoadRcd = LoadDBInfo;
                }
                finally
                {
                    HUtil32.LeaveCriticalSection(m_UserCriticalSection);
                }

                if (LoadDBInfo == null)
                {
                    continue;
                }

                var loadState = _loadRequestStates.GetValue(LoadDBInfo,
                    static _ => new LoadRequestState());
                try
                {
                    if (loadState.Cancelled)
                    {
                        continue;
                    }

                    boReTryLoadDB = false;
                    if (!LoadHumFromDB(LoadDBInfo, ref boReTryLoadDB))
                    {
                        if (loadState.Cancelled)
                        {
                            continue;
                        }
                        if (boReTryLoadDB)
                        {
                            // 角色正在保存或仍在在线表中时，下一轮重试，避免误踢导致黑屏。
                            TryRequeueLoad(LoadDBInfo, loadState);
                        }
                        else
                        {
                            M2Share.MainOutMessage($"[LoadClose] account={LoadDBInfo.sAccount} chr={LoadDBInfo.sCharName}");
                            M2Share.GateManager.CloseUser(LoadDBInfo.nGateIdx,
                                LoadDBInfo.nSocket, LoadDBInfo.UserGeneration);
                        }
                    }
                    else if (!boReTryLoadDB)
                    {
                        DisPose(LoadDBInfo);
                    }
                    else
                    {
                        TryRequeueLoad(LoadDBInfo, loadState);
                    }
                }
                finally
                {
                    HUtil32.EnterCriticalSection(m_UserCriticalSection);
                    try
                    {
                        if (ReferenceEquals(_activeLoadRcd, LoadDBInfo))
                        {
                            _activeLoadRcd = null;
                        }
                        if (loadState.Cancelled)
                        {
                            RemoveLoadRecord(m_LoadRcdList, LoadDBInfo);
                        }
                    }
                    finally
                    {
                        HUtil32.LeaveCriticalSection(m_UserCriticalSection);
                    }
                }
            }
            if (ChangeGoldList != null)
            {
                var attemptedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < ChangeGoldList.Count; i++)
                {
                    TGoldChangeInfo GoldChangeInfo = ChangeGoldList[i];
                    if (GoldChangeInfo == null ||
                        !attemptedUsers.Add(GoldChangeInfo.sGetGoldUser ?? string.Empty))
                    {
                        continue;
                    }
                    var currentTick = HUtil32.GetTickCount();
                    if (currentTick - GoldChangeInfo.NextRetryTick < 0)
                        continue;

                    var changeResult = ChangeUserGoldInDB(GoldChangeInfo, out var failureReason);
                    if (changeResult != GoldChangeResult.Retry)
                    {
                        GoldChangeInfo.Succeeded = changeResult == GoldChangeResult.Success;
                        GoldChangeInfo.FailureReason = failureReason ?? string.Empty;
                        HUtil32.EnterCriticalSection(m_UserCriticalSection);
                        try
                        {
                            for (var j = 0; j < m_ChangeGoldList.Count; j++)
                            {
                                if (!ReferenceEquals(m_ChangeGoldList[j], GoldChangeInfo)) continue;
                                m_ChangeGoldList.RemoveAt(j);
                                break;
                            }
                        }
                        finally
                        {
                            HUtil32.LeaveCriticalSection(m_UserCriticalSection);
                        }
                        M2Share.UserEngine.sub_4AE514(GoldChangeInfo);
                        continue;
                    }

                    var retry = Math.Min(5, ++GoldChangeInfo.RetryCount);
                    var delay = retry >= 5 ? 15_000 : 1_000 << (retry - 1);
                    GoldChangeInfo.NextRetryTick = currentTick + delay;
                    if (currentTick - GoldChangeInfo.LastErrorLogTick >= 10_000)
                    {
                        GoldChangeInfo.LastErrorLogTick = currentTick;
                        M2Share.ErrorMessage(
                            $"[FrontEngine] 离线金币调整失败，保留队列等待重试: " +
                            $"{GoldChangeInfo.sGetGoldUser}, retry={GoldChangeInfo.RetryCount}, delay={delay}ms");
                    }
                }
            }
        }

        public bool IsFull()
        {
            var result = false;
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                if (m_SaveRcdList.Count >= 2000)
                {
                    result = true;
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }
            return result;
        }

        public void AddToLoadRcdList(string sAccount, string sChrName,
            string sIPaddr, bool boFlag, int nSessionID, int nPayMent,
            int nPayMode, int nSoftVersionDate, int nSocket,
            ushort nGSocketIdx, int nGateIdx, long userGeneration = 0)
        {
            TLoadDBInfo LoadRcdInfo = new TLoadDBInfo
            {
                sAccount = sAccount,
                sCharName = sChrName,
                sIPaddr = sIPaddr,
                nSessionID = nSessionID,
                nSoftVersionDate = nSoftVersionDate,
                nPayMent = nPayMent,
                nPayMode = nPayMode,
                nSocket = nSocket,
                UserGeneration = userGeneration,
                nGSocketIdx = nGSocketIdx,
                nGateIdx = nGateIdx,
                dwNewUserTick = HUtil32.GetTickCount(),
                PlayObject = null,
                nReLoadCount = 0
            };
            _loadRequestStates.Add(LoadRcdInfo, new LoadRequestState());
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                m_LoadRcdList.Add(LoadRcdInfo);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }
        }

        private bool LoadHumFromDB(TLoadDBInfo LoadUser, ref bool boReTry)
        {
            THumDataInfo HumanRcd = null;
            NativeHumanLoadData NativeLoad = null;
            var result = false;
            boReTry = false;
            if (string.IsNullOrEmpty(LoadUser.sCharName))
            {
                M2Share.GateManager.SendOutConnectMsg(LoadUser.nGateIdx,
                    LoadUser.nSocket, LoadUser.nGSocketIdx,
                    LoadUser.UserGeneration);
                return false;
            }
            if (InSaveRcdList(LoadUser.sCharName))
            {
                boReTry = true;// 反回TRUE,则重新加入队?
                return result;
            }
            if (M2Share.UserEngine.GetPlayObjectEx(LoadUser.sCharName) != null)
            {
                // Player already exists from reconnect — just kick old and retry
                M2Share.UserEngine.KickPlayObjectEx(LoadUser.sCharName);
                boReTry = true;
                return result;
            }
            if (!HumDataService.LoadHumRcdFromDB(LoadUser.sAccount,
                    LoadUser.sCharName, LoadUser.sIPaddr, ref HumanRcd,
                    LoadUser.nSessionID, out NativeLoad))
            {
                M2Share.MainOutMessage($"[LoadFail] db load failed account={LoadUser.sAccount} chr={LoadUser.sCharName} session={LoadUser.nSessionID}");
                M2Share.GateManager.SendOutConnectMsg(LoadUser.nGateIdx,
                    LoadUser.nSocket, LoadUser.nGSocketIdx,
                    LoadUser.UserGeneration);
            }
            else
            {
                // M2Share.MainOutMessage($"[LoadOk] account={LoadUser.sAccount} chr={LoadUser.sCharName} session={LoadUser.nSessionID}");
                result = TryPublishLoadedHuman(LoadUser, HumanRcd, NativeLoad);
            }
            return result;
        }

        private bool TryPublishLoadedHuman(TLoadDBInfo loadUser,
            THumDataInfo humanRcd, NativeHumanLoadData nativeLoad)
        {
            var loadState = _loadRequestStates.GetValue(loadUser,
                static _ => new LoadRequestState());
            lock (loadState.SyncRoot)
            {
                if (loadState.Cancelled)
                {
                    return false;
                }
                if (loadUser.UserGeneration != 0 &&
                    !M2Share.GateManager.IsCurrentUser(loadUser.nGateIdx,
                        loadUser.nSocket, loadUser.UserGeneration))
                {
                    loadState.Cancelled = true;
                    return false;
                }
                M2Share.UserEngine.AddUserOpenInfo(new TUserOpenInfo
                {
                    sChrName = loadUser.sCharName,
                    LoadUser = loadUser,
                    HumanRcd = humanRcd,
                    NativeSessionSuffix = CloneNativeSessionSuffix(nativeLoad)
                });
                return true;
            }
        }

        private static byte[] CloneNativeSessionSuffix(NativeHumanLoadData load)
        {
            var suffix = load?.SessionSuffix;
            return suffix is { Length: > 0 }
                ? (byte[])suffix.Clone()
                : Array.Empty<byte>();
        }

        public bool InSaveRcdList(string sChrName)
        {
            var result = false;
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                for (var i = 0; i < m_SaveRcdList.Count; i++)
                {
                    if (m_SaveRcdList[i].sChrName == sChrName)
                    {
                        result = true;
                        break;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }
            return result;
        }

        public void AddChangeGoldList(string sGameMasterName, string sGetGoldUserName, int nGold)
        {
            TGoldChangeInfo GoldInfo = new TGoldChangeInfo
            {
                sGameMasterName = sGameMasterName,
                sGetGoldUser = sGetGoldUserName,
                nGold = nGold
            };
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                m_ChangeGoldList.Add(GoldInfo);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }
        }

        public void AddToSaveRcdList(TSaveRcd SaveRcd)
        {
            if (SaveRcd == null) return;
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                SaveRcd.Generation = Interlocked.Increment(ref _saveGeneration);
                for (var i = m_SaveRcdList.Count - 1; i >= 0; i--)
                {
                    var existing = m_SaveRcdList[i];
                    if (!SameSaveKey(existing, SaveRcd)) continue;
                    SaveRcd.nReTryCount = existing.nReTryCount;
                    SaveRcd.NextRetryTick = existing.NextRetryTick;
                    SaveRcd.LastErrorLogTick = existing.LastErrorLogTick;
                    m_SaveRcdList[i] = SaveRcd;
                    return;
                }
                m_SaveRcdList.Add(SaveRcd);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }
        }

        private bool TryRequeueLoad(TLoadDBInfo loadRcdInfo,
            LoadRequestState loadState)
        {
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                if (loadState.Cancelled)
                {
                    return false;
                }
                m_LoadRcdList.Add(loadRcdInfo);
                return true;
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }
        }

        private static void RemoveLoadRecord(IList<TLoadDBInfo> records,
            TLoadDBInfo target)
        {
            for (var i = records.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(records[i], target))
                {
                    records.RemoveAt(i);
                }
            }
        }

        public void DeleteHuman(int nGateIndex, int nSocket,
            long userGeneration = 0)
        {
            var cancelledStates = new HashSet<LoadRequestState>();
            HUtil32.EnterCriticalSection(m_UserCriticalSection);
            try
            {
                CancelQueuedLoads(m_LoadRcdList, nGateIndex, nSocket,
                    userGeneration,
                    cancelledStates);
                CancelQueuedLoads(m_LoadRcdTempList, nGateIndex, nSocket,
                    userGeneration,
                    cancelledStates);
                if (MatchesLoad(_activeLoadRcd, nGateIndex, nSocket,
                        userGeneration))
                {
                    var activeState = _loadRequestStates.GetValue(_activeLoadRcd,
                        static _ => new LoadRequestState());
                    activeState.Cancelled = true;
                    cancelledStates.Add(activeState);
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(m_UserCriticalSection);
            }

            // Wait for a publication that already crossed its linearization
            // point; after DeleteHuman returns no cancelled load can publish.
            foreach (var loadState in cancelledStates)
            {
                lock (loadState.SyncRoot)
                {
                }
            }
            M2Share.UserEngine?.CancelUserOpen(nGateIndex, nSocket,
                userGeneration);
        }

        private void CancelQueuedLoads(IList<TLoadDBInfo> records,
            int gateIndex, int socket, long userGeneration,
            ISet<LoadRequestState> cancelledStates)
        {
            for (var i = records.Count - 1; i >= 0; i--)
            {
                var loadRcdInfo = records[i];
                if (!MatchesLoad(loadRcdInfo, gateIndex, socket,
                        userGeneration))
                {
                    continue;
                }
                var loadState = _loadRequestStates.GetValue(loadRcdInfo,
                    static _ => new LoadRequestState());
                loadState.Cancelled = true;
                cancelledStates.Add(loadState);
                records.RemoveAt(i);
            }
        }

        private static bool MatchesLoad(TLoadDBInfo loadRcdInfo,
            int gateIndex, int socket, long userGeneration = 0)
        {
            return loadRcdInfo != null && loadRcdInfo.nGateIdx == gateIndex &&
                   loadRcdInfo.nSocket == socket &&
                   (userGeneration == 0 ||
                    loadRcdInfo.UserGeneration == userGeneration);
        }

        private enum GoldChangeResult
        {
            Retry,
            Success,
            Rejected
        }

        private GoldChangeResult ChangeUserGoldInDB(TGoldChangeInfo GoldChangeInfo,
            out string failureReason)
        {
            // Native DBServer only pushes 0x0050 after the gate has selected a
            // character. It has no M2 request that can load an arbitrary offline
            // record, so retaining the old synchronous path would retry forever.
            failureReason = "原生DB协议不支持M2主动读取离线人物档案，离线金币调整已拒绝";
            return GoldChangeResult.Rejected;
        }

        private static bool SameSaveKey(TSaveRcd left, TSaveRcd right)
        {
            return left != null && right != null &&
                   string.Equals(left.sAccount, right.sAccount, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.sChrName, right.sChrName, StringComparison.OrdinalIgnoreCase) &&
                   (left.NativeSaveMode == 2) == (right.NativeSaveMode == 2);
        }

        private void DisPose(object obj)
        {
            obj = null;
        }
    }
}
