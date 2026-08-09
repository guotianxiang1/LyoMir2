using SystemModule;
using SystemModule.Sockets;

namespace GameSvr
{
    
    
    
    public class SnapsmClient
    {
        private readonly object sRecvMsgLock = new object();
        private string sRecvMsg = string.Empty;
        private readonly IClientScoket _msgClient;
        private readonly MirrorMessage _groupMessageHandle;

        private static SnapsmClient instance = null;

        public static SnapsmClient Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new SnapsmClient();
                }
                return instance;
            }
        }

        public SnapsmClient()
        {
            _msgClient = new IClientScoket();
            _msgClient.OnConnected += MsgClientConnect;
            _msgClient.ReceivedDatagram += MsgClientRead;
            _msgClient.OnError += MsgClientError;
            _msgClient.OnDisconnected += MsgClientDisconnected;
            _groupMessageHandle = new MirrorMessage();
        }

        public void ConnectMsgServer()
        {
            _msgClient.Host = M2Share.g_Config.sMsgSrvAddr;
            _msgClient.Port = M2Share.g_Config.nMsgSrvPort;
            _msgClient.Connect();
        }

        public void Run()
        {
            if (_msgClient.IsConnected)
            {
                DecodeSocStr();
            }
        }

        public void CheckConnected()
        {
            if (M2Share.g_Config.nServerNumber > 0)
            {
                if (_msgClient.IsConnected)
                {
                    return;
                }
                _msgClient.Connect();
            }
        }

        private bool IsConnect()
        {
            return _msgClient.IsConnected;
        }

        private void DecodeSocStr()
        {
            string BufStr = string.Empty;
            string Str = string.Empty;
            string sNumStr = string.Empty;
            string Head = string.Empty;
            string Body = string.Empty;
            int Ident;
            int sNum;
            const string sExceptionMsg = "[Exception] TFrmSrvMsg::DecodeSocStr";
            lock (sRecvMsgLock)
            {
                if (sRecvMsg.IndexOf(')') <= 0)
                {
                    return;
                }
                try
                {
                    BufStr = sRecvMsg;
                    sRecvMsg = string.Empty;
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage(sExceptionMsg);
                    M2Share.ErrorMessage(ex.StackTrace);
                    return;
                }
            }
            while (BufStr.IndexOf(')') > -1)
            {
                BufStr = HUtil32.ArrestStringEx(BufStr, "(", ")", ref Str);
                if (!string.IsNullOrEmpty(Str))
                {
                    Body = HUtil32.GetValidStr3(Str, ref Head, HUtil32.Backslash);
                    Body = HUtil32.GetValidStr3(Body, ref sNumStr, HUtil32.Backslash);
                    Ident = HUtil32.Str_ToInt(Head, 0);
                    sNum = HUtil32.Str_ToInt(sNumStr, -1);
                    _groupMessageHandle.ProcessData(Ident, sNum, Body);
                }
                else
                {
                    break;
                }
            }
            lock (sRecvMsgLock)
            {
                sRecvMsg = BufStr + sRecvMsg;
            }
        }

        public void SendSocket(string sMsg)
        {
            if (IsConnect())
            {
                var buff = HUtil32.GetBytes("(" + sMsg + ")");
                _msgClient.Send(buff);
            }
        }

        private void MsgClientConnect(object sender, DSCClientConnectedEventArgs e)
        {
            M2Share.MainOutMessage("连接主服务器(" + e.RemoteAddress + ':' + e.RemotePort + ")成功...");
            
        }

        private void MsgClientError(object sender, DSCClientErrorEventArgs e)
        {
            switch (e.ErrorCode)
            {
                case System.Net.Sockets.SocketError.ConnectionRefused:
                    M2Share.ErrorMessage("主游戏引擎[" + _msgClient.Host + ":" + _msgClient.Port + "]拒绝链接...");
                    break;
                case System.Net.Sockets.SocketError.ConnectionReset:
                    M2Share.ErrorMessage("主游戏引擎[" + _msgClient.Host + ":" + _msgClient.Port + "]关闭连接...");
                    break;
                case System.Net.Sockets.SocketError.TimedOut:
                    M2Share.ErrorMessage("主游戏引擎[" + _msgClient.Host + ":" + _msgClient.Port + "]链接超时...");
                    break;
            }
        }

        private void MsgClientDisconnected(object sender, DSCClientConnectedEventArgs e)
        {
            M2Share.ErrorMessage("节点服务器(" + e.RemoteAddress + ':' + e.RemotePort + ")断开连接...");
        }

        
        
        
        
        
        private void MsgClientRead(object sender, DSCClientDataInEventArgs e)
        {
            lock (sRecvMsgLock) { sRecvMsg += HUtil32.GetString(e.Buff, 0, e.BuffLen); }
        }
    }
}

