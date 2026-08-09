using System.Collections;
using System.Net;
using SystemModule;
using SystemModule.Sockets;

namespace GameSvr
{
    public class AccountService : IDisposable
    {
        private const int MaxReceiveBufferLength = 64 * 1024;
        private const int SessionTimeoutMilliseconds = 40 * 60 * 1000;
        private int _dwClearEmptySessionTick = 0;
        private readonly IList<TSessInfo> m_SessionList = null;
        private readonly object _sessionLock = new();

        private readonly IClientScoket _clientScoket;

        public AccountService()
        {
            m_SessionList = new List<TSessInfo>();
            M2Share.g_Config.boIDSocketConnected = false;
            _clientScoket = new IClientScoket();
            _clientScoket.OnConnected += IDSocketConnect;
            _clientScoket.OnDisconnected += IDSocketDisconnect;
            _clientScoket.OnError += IDSocketError;
            _clientScoket.ReceivedDatagram += IdSocketRead;
            if (M2Share.g_Config != null)
            {
                _clientScoket.Host = M2Share.g_Config.sIDSAddr;
                _clientScoket.Port = M2Share.g_Config.nIDSPort;
            }
        }

        public void CheckConnected()
        {
            if (_clientScoket.IsConnected)
            {
                return;
            }
            if (_clientScoket.IsBusy)
            {
                return;
            }
            _clientScoket.Connect(_clientScoket.Host, _clientScoket.Port);
        }

        private void IdSocketRead(object sender, DSCClientDataInEventArgs e)
        {
            HUtil32.EnterCriticalSection(M2Share.g_Config.UserIDSection);
            try
            {
                M2Share.g_Config.sIDSocketRecvText += HUtil32.GetString(e.Buff, 0, e.BuffLen);
                if (M2Share.g_Config.sIDSocketRecvText.Length > MaxReceiveBufferLength)
                {
                    M2Share.g_Config.sIDSocketRecvText = string.Empty;
                    M2Share.ErrorMessage($"登录服务器接收缓冲超过{MaxReceiveBufferLength}字节，已清空.");
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.g_Config.UserIDSection);
            }
        }

        private void IDSocketError(object sender, DSCClientErrorEventArgs e)
        {
            switch (e.ErrorCode)
            {
                case System.Net.Sockets.SocketError.ConnectionRefused:
                    M2Share.ErrorMessage("登录服务器[" + _clientScoket.Host + ":" + _clientScoket.Port + "]拒绝链接...");
                    break;
                case System.Net.Sockets.SocketError.ConnectionReset:
                    M2Share.ErrorMessage("登录服务器[" + _clientScoket.Host + ":" + _clientScoket.Port + "]关闭连接...");
                    break;
                case System.Net.Sockets.SocketError.TimedOut:
                    M2Share.ErrorMessage("登录服务器[" + _clientScoket.Host + ":" + _clientScoket.Port + "]链接超时...");
                    break;
            }
        }

        public void Initialize()
        {
            CheckConnected();
        }

        private void SendSocket(string sSendMsg)
        {
            if (_clientScoket == null || !_clientScoket.IsConnected) return;
            var data = HUtil32.GetBytes(sSendMsg);
            _clientScoket.Send(data);
        }

        public void SendHumanLogOutMsg(string sUserId, int nId)
        {
            const string sFormatMsg = "({0}/{1}/{2})";
            for (int i = 0; i < m_SessionList.Count; i++)
            {
                var sessInfo = m_SessionList[i];
                if (sessInfo.nSessionID == nId && sessInfo.sAccount == sUserId)
                {
                    break;
                }
            }
            SendSocket(string.Format(sFormatMsg, Grobal2.SS_SOFTOUTSESSION, sUserId, nId));
        }

        public void SendHumanLogOutMsgA(string sUserID, int nID)
        {
            for (var i = m_SessionList.Count - 1; i >= 0; i--)
            {
                var sessInfo = m_SessionList[i];
                if (sessInfo.nSessionID == nID && sessInfo.sAccount == sUserID)
                {
                    break;
                }
            }
        }

        public void SendLogonCostMsg(string sAccount, int nTime)
        {
            const string sFormatMsg = "({0}/{1}/{2})";
            SendSocket(string.Format(sFormatMsg, new object[] { Grobal2.SS_LOGINCOST, sAccount, nTime }));
        }

        public void SendOnlineHumCountMsg(int nCount)
        {
            const string sFormatMsg = "({0}/{1}/{2}/{3})";
            SendSocket(string.Format(sFormatMsg, Grobal2.SS_SERVERINFO, M2Share.g_Config.sServerName, M2Share.nServerIndex, nCount));
        }

        public void Run()
        {
            string sSocketText;
            var sData = string.Empty;
            var sCode = string.Empty;
            const string sExceptionMsg = "[Exception] TFrmIdSoc::DecodeSocStr";
            var Config = M2Share.g_Config;
            ClearTimeoutSessions();
            HUtil32.EnterCriticalSection(Config.UserIDSection);
            try
            {
                if (string.IsNullOrEmpty(Config.sIDSocketRecvText))
                {
                    return;
                }
                if (Config.sIDSocketRecvText.IndexOf(')') <= 0)
                {
                    return;
                }
                sSocketText = Config.sIDSocketRecvText;
                Config.sIDSocketRecvText = string.Empty;
            }
            finally
            {
                HUtil32.LeaveCriticalSection(Config.UserIDSection);
            }
            try
            {
                while (true)
                {
                    sSocketText = HUtil32.ArrestStringEx(sSocketText, "(", ")", ref sData);
                    if (string.IsNullOrEmpty(sData))
                    {
                        break;
                    }
                    var sBody = HUtil32.GetValidStr3(sData, ref sCode, HUtil32.Backslash);
                    switch (HUtil32.Str_ToInt(sCode, 0))
                    {
                        case Grobal2.SS_OPENSESSION:// 100
                            GetPasswdSuccess(sBody);
                            break;
                        case Grobal2.SS_CLOSESESSION:// 101
                            GetCancelAdmission(sBody);
                            break;
                        case Grobal2.SS_SOFTOUTSESSION:// 102
                            GetCancelAdmission(sBody);
                            break;
                        case Grobal2.SS_SERVERINFO:// 103
                            SetServerInfo(sBody);
                            break;
                        case Grobal2.SS_KEEPALIVE:// 104
                            SetTotalHumanCount(sBody);
                            break;
                        case Grobal2.UNKNOWMSG:
                            break;
                        case Grobal2.SS_KICKUSER:// 111
                            GetCancelAdmissionA(sBody);
                            break;
                        case Grobal2.SS_SERVERLOAD:// 113
                            GetServerLoad(sBody);
                            break;
                    }
                    if (sSocketText.IndexOf(')') <= 0)
                    {
                        break;
                    }
                }
                HUtil32.EnterCriticalSection(Config.UserIDSection);
                try
                {
                    Config.sIDSocketRecvText = sSocketText + Config.sIDSocketRecvText;
                }
                finally
                {
                    HUtil32.LeaveCriticalSection(Config.UserIDSection);
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg);
            }
        }

        private void GetPasswdSuccess(string sData)
        {
            var sAccount = string.Empty;
            var sSessionID = string.Empty;
            var sPayCost = string.Empty;
            var sIPaddr = string.Empty;
            var sPayMode = string.Empty;
            const string sExceptionMsg = "[Exception] TFrmIdSoc::GetPasswdSuccess";
            try
            {
                sData = HUtil32.GetValidStr3(sData, ref sAccount, HUtil32.Backslash);
                sData = HUtil32.GetValidStr3(sData, ref sSessionID, HUtil32.Backslash);
                sData = HUtil32.GetValidStr3(sData, ref sPayCost, HUtil32.Backslash);// boPayCost
                sData = HUtil32.GetValidStr3(sData, ref sPayMode, HUtil32.Backslash);// nPayMode
                sData = HUtil32.GetValidStr3(sData, ref sIPaddr, HUtil32.Backslash);// sIPaddr
                NewSession(sAccount, sIPaddr, HUtil32.Str_ToInt(sSessionID, 0), HUtil32.Str_ToInt(sPayCost, 0), HUtil32.Str_ToInt(sPayMode, 0));
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg);
            }
        }

        private void GetCancelAdmission(string sData)
        {
            var sC = string.Empty;
            const string sExceptionMsg = "[Exception] TFrmIdSoc::GetCancelAdmission";
            try
            {
                var sSessionID = HUtil32.GetValidStr3(sData, ref sC, HUtil32.Backslash);
                DelSession(sC, HUtil32.Str_ToInt(sSessionID, 0));
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
        }

        private void NewSession(string sAccount, string sIPaddr, int nSessionID, int nPayMent, int nPayMode)
        {
            var sessInfo = new TSessInfo();
            sessInfo.sAccount = sAccount;
            sessInfo.sIPaddr = sIPaddr;
            sessInfo.nSessionID = nSessionID;
            sessInfo.nPayMent = nPayMent;
            sessInfo.nPayMode = nPayMode;
            sessInfo.nSessionStatus = 0;
            sessInfo.dwStartTick = HUtil32.GetTickCount();
            sessInfo.dwActiveTick = HUtil32.GetTickCount();
            sessInfo.nRefCount = 1;
            var supersededSessions = new List<(string Account, int SessionId, int PayMode)>();

            lock (_sessionLock)
            {
                for (var i = m_SessionList.Count - 1; i >= 0; i--)
                {
                    var existing = m_SessionList[i];
                    if (existing == null || !string.Equals(existing.sAccount, sAccount,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    if (existing.nSessionID != nSessionID)
                        supersededSessions.Add((existing.sAccount, existing.nSessionID,
                            existing.nPayMode));
                    m_SessionList.RemoveAt(i);
                }
                m_SessionList.Add(sessInfo);


            }

            foreach (var stale in supersededSessions)
                M2Share.GateManager.KickUser(stale.Account, stale.SessionId, stale.PayMode);
        }

        private void DelSession(string account, int nSessionID)
        {
            var sAccount = string.Empty;
            const string sExceptionMsg = "[Exception] FrmIdSoc::DelSession";
            try
            {
                int nPayMode = 0;
                lock (_sessionLock)
                {
                    for (var i = 0; i < m_SessionList.Count; i++)
                    {
                        var sessInfo = m_SessionList[i];
                        if (sessInfo != null && sessInfo.nSessionID == nSessionID &&
                            (string.IsNullOrEmpty(account) || sessInfo.sAccount == account))
                        {
                            sAccount = sessInfo.sAccount;
                            nPayMode = sessInfo.nPayMode;
                            m_SessionList.RemoveAt(i);
                            break;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(sAccount))
                {
                    M2Share.GateManager.KickUser(sAccount, nSessionID, nPayMode);
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
        }

        private void ClearSession()
        {
            lock (_sessionLock)
            {
                m_SessionList.Clear();
            }
        }

        public TSessInfo GetAdmission(string sAccount, string sIPaddr, int nSessionID,
            ref int nPayMode, ref int nPayMent)
        {
            const string sGetFailMsg = "[非法登录] 全局会话验证失败({0}/{1}/{2})";
            nPayMent = 0;
            nPayMode = 0;
            TSessInfo result;
            lock (_sessionLock)
            {
                result = FindAdmissionLocked(sAccount, sIPaddr, nSessionID,
                    ref nPayMode, ref nPayMent);
            }
            if (M2Share.g_Config.boViewAdmissionFailure && result == null)
            {
                M2Share.ErrorMessage(string.Format(sGetFailMsg, new object[] { sAccount, sIPaddr, nSessionID }));
            }
            return result;
        }

        private TSessInfo FindAdmissionLocked(string sAccount, string sIPaddr,
            int nSessionID, ref int nPayMode, ref int nPayMent)
        {
            for (var i = 0; i < m_SessionList.Count; i++)
            {
                var sessInfo = m_SessionList[i];
                if (sessInfo == null || sessInfo.nSessionID != nSessionID ||
                    sessInfo.sAccount != sAccount ||
                    !SessionIPMatches(sessInfo.sIPaddr, sIPaddr)) continue;

                nPayMent = sessInfo.nPayMent switch
                {
                    2 => 3,
                    1 => 2,
                    0 => 1,
                    _ => 0
                };
                sessInfo.dwActiveTick = HUtil32.GetTickCount();
                nPayMode = sessInfo.nPayMode;
                return sessInfo;
            }
            return null;
        }

        private void SetTotalHumanCount(string sData)
        {
            M2Share.g_nTotalHumCount = HUtil32.Str_ToInt(sData, 0);
        }

        private void SetServerInfo(string sData)
        {
            var lastSlash = sData.LastIndexOf('/');
            if (lastSlash >= 0) sData = sData.Substring(lastSlash + 1);
            SetTotalHumanCount(sData);
        }

        private void GetCancelAdmissionA(string sData)
        {
            var sAccount = string.Empty;
            const string sExceptionMsg = "[Exception] FrmIdSoc::GetCancelAdmissionA";
            try
            {
                var sSessionID = HUtil32.GetValidStr3(sData, ref sAccount, HUtil32.Backslash);
                var nSessionID = HUtil32.Str_ToInt(sSessionID, 0);
                if (!M2Share.g_Config.boTestServer)
                {
                    M2Share.UserEngine.HumanExpire(sAccount);
                    DelSession(sAccount, nSessionID);
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg);
            }
        }

        private void GetServerLoad(string sData)
        {
            
        }

        private void IDSocketConnect(object sender, DSCClientConnectedEventArgs e)
        {
            M2Share.g_Config.boIDSocketConnected = true;
            M2Share.MainOutMessage("登录服务器[" + _clientScoket.Host + ":" + _clientScoket.Port + "]连接成功...", messageColor: ConsoleColor.Green);
            SendOnlineHumCountMsg(M2Share.UserEngine.OnlinePlayObject);
        }

        private void IDSocketDisconnect(object sender, DSCClientConnectedEventArgs e)
        {
            
            
            
            
            ClearSession();
            M2Share.g_Config.boIDSocketConnected = false;
            _clientScoket.IsConnected = false;
            M2Share.ErrorMessage("登录服务器[" + _clientScoket.Host + ":" + _clientScoket.Port + "]断开连接...");
        }

        public void Close()
        {
            _clientScoket.Disconnect();
        }

        public int GetSessionCount()
        {
            lock (_sessionLock) return m_SessionList.Count;
        }

        public void Dispose()
        {
            _clientScoket.OnConnected -= IDSocketConnect;
            _clientScoket.OnDisconnected -= IDSocketDisconnect;
            _clientScoket.OnError -= IDSocketError;
            _clientScoket.ReceivedDatagram -= IdSocketRead;
        }

        public void GetSessionList(ArrayList List)
        {
            lock (_sessionLock)
            {
                for (var i = 0; i < m_SessionList.Count; i++) List.Add(m_SessionList[i]);
            }
        }

        private void ClearTimeoutSessions()
        {
            var now = HUtil32.GetTickCount();
            if (unchecked((uint)(now - _dwClearEmptySessionTick)) < 60000u) return;
            _dwClearEmptySessionTick = now;
            var expired = new List<(string Account, int SessionId, int PayMode)>();
            lock (_sessionLock)
            {
                for (var i = m_SessionList.Count - 1; i >= 0; i--)
                {
                    var session = m_SessionList[i];
                    if (session != null &&
                        unchecked((uint)(now - session.dwStartTick)) >= (uint)SessionTimeoutMilliseconds)
                    {
                        expired.Add((session.sAccount, session.nSessionID, session.nPayMode));
                        m_SessionList.RemoveAt(i);
                    }
                }
            }
            foreach (var session in expired)
                M2Share.GateManager.KickUser(session.Account, session.SessionId, session.PayMode);
        }

        private static bool SessionIPMatches(string expected, string actual)
        {
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual)) return false;
            if (IPAddress.TryParse(expected, out var expectedIP) && IPAddress.TryParse(actual, out var actualIP))
            {
                if (expectedIP.IsIPv4MappedToIPv6) expectedIP = expectedIP.MapToIPv4();
                if (actualIP.IsIPv4MappedToIPv6) actualIP = actualIP.MapToIPv4();
                return expectedIP.Equals(actualIP);
            }
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

    }
}

namespace GameSvr
{
    public class IdSrvClient
    {
        private static AccountService instance = null;

        public static AccountService Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new AccountService();
                }
                return instance;
            }
        }
    }
}
