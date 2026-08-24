using SystemModule;

namespace GameSvr
{
    internal sealed class PendingClientPacket
    {
        internal PendingClientPacket(byte[] data, int length, ushort ident,
            long generation)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Length = length;
            Ident = ident;
            Generation = generation;
        }

        internal byte[] Data { get; }
        internal int Length { get; }
        internal ushort Ident { get; }
        internal long Generation { get; }
    }

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

        // Access is serialized by GateService.runSocketSection.
        internal readonly Queue<PendingClientPacket> PendingClientMessages = new();
        internal int PendingClientBytes;
        internal bool PendingClientReplayInProgress;
    }
}
