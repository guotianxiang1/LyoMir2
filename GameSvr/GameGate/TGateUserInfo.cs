using SystemModule;

namespace GameSvr
{
    public class TGateUserInfo
    {
        
        
        
        public TPlayObject PlayObject;
        public int nSessionID;
        
        
        
        public string sAccount;
        public ushort nGSocketIdx;
        
        
        
        public string sIPaddr;
        
        
        
        public bool boCertification;
        
        
        
        public string sCharName;
        
        
        
        public int nClientVersion;
        
        
        
        public TSessInfo SessInfo;
        public int nSocket;
        public long UserGeneration;
        public TFrontEngine FrontEngine;
        public UserEngine UserEngine;
        public int dwNewUserTick;
    }
}
