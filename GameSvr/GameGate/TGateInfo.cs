using System.Net.Sockets;

namespace GameSvr
{
    public class TGateInfo
    {
        
        
        
        public bool boUsed;
        public Socket Socket;
        public int SocketId;
        
        
        
        public IList<TGateUserInfo> UserList;
        
        
        
        public int nUserCount;
        public bool boSendKeepAlive;
        public int nSendChecked;
        public int nSendBlockCount;
        
        
        
        public int nSendMsgCount;
        
        
        
        public int nSendRemainCount;
        
        
        
        public int dwSendTick;
        
        
        
        public int nSendMsgBytes;
        
        
        
        public int nSendBytesCount;
        
        
        
        public int nSendedMsgCount;
        
        
        
        public int nSendCount;
        
        
        
        public int dwSendCheckTick;
        public int dwCompactAckTick;
    }
}
