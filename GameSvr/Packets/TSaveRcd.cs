using SystemModule;

namespace GameSvr
{
    public class TSaveRcd
    {
        public string sAccount;
        public string sChrName;
        public int nSessionID;
        public ushort NativeSaveMode;
        public int NativeSaveParam1;
        public int NativeSaveParam2;
        public byte[] NativeSwitchExtension;
        public TPlayObject PlayObject;
        public THumDataInfo HumanRcd;
        public int nReTryCount;
        public int NextRetryTick;
        public int LastErrorLogTick;
        public long Generation;

        public TSaveRcd()
        {
            HumanRcd = new THumDataInfo();
        }
    }
}
