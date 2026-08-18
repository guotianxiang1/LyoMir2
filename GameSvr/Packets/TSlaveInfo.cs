namespace GameSvr
{
    public class TSlaveInfo
    {
        public string sSlaveName = string.Empty;
        public int dwRoyaltySec;
        public int nKillCount;
        public byte btSlaveExpLevel;
        public int nHP;
        public int nMP;

        private byte _slaveMakeLevel;

        /// <summary>Native 32-byte record +0x1D.</summary>
        public byte btSlaveLevel
        {
            get => _slaveMakeLevel;
            set => _slaveMakeLevel = value;
        }

        /// <summary>Compatibility alias for the historical misspelling.</summary>
        public byte btSalveLevel
        {
            get => _slaveMakeLevel;
            set => _slaveMakeLevel = value;
        }

        public TSlaveInfo()
        {

        }
    }
}
