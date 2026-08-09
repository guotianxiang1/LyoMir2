using SystemModule;

namespace GameSvr
{
    public class TUserOpenInfo
    {
        public string sChrName;
        public TLoadDBInfo LoadUser;
        public THumDataInfo HumanRcd;
        public byte[] NativeSessionSuffix = Array.Empty<byte>();
    }
}
