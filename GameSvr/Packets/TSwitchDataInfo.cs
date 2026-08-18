using SystemModule;

namespace GameSvr
{
    public class TSwitchDataInfo
    {
        public const int NativeSlaveSlotCount = 5;
        public const int NativeStatusSlotCount = 6;

        public string sMap;
        public short wX;
        public short wY;
        public TAbility Abil;
        public string sChrName;
        public int nCode;
        public bool boC70;
        public bool boBanShout;
        public bool boHearWhisper;
        public bool boBanGuildChat;
        public bool boAdminMode;
        public bool boObMode;
        public IList<string> BlockWhisperArr;
        public TSlaveInfo[] SlaveArr;
        public ushort[] StatusValue;
        public int[] StatusTimeOut;
        public int dwWaitTime;

        public TSwitchDataInfo()
        {
            sMap = string.Empty;
            sChrName = string.Empty;
            BlockWhisperArr = new List<string>();
            SlaveArr = new TSlaveInfo[NativeSlaveSlotCount];
            for (var i = 0; i < SlaveArr.Length; i++)
                SlaveArr[i] = new TSlaveInfo();
            StatusValue = new ushort[NativeStatusSlotCount];
            StatusTimeOut = new int[NativeStatusSlotCount];
        }
    }
}
