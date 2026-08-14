namespace GameSvr
{
    public partial class TPlayObject
    {
        // sub_6B0FF0 @0x6B09AB..0x6B09C5（SaveHuman → DBSvr 人物 blob）：
        //   0x6B09AB  test byte [suffix+0x55],2  -> rec+0xB76
        //   0x6B09BB  test byte [suffix+0x56],4  -> rec+0xB77
        // ebx = raw+0xEF00 = NativeHumanDbCodec.SessionSuffixOffset 上的会话 suffix。
        internal const int NativeAccountTypeFlagOffset = 0x0B76;
        internal const int NativeAccountTypeFlag2Offset = 0x0B77;
        internal const int NativeSessionAuthByte55Offset = 0x55;
        internal const int NativeSessionAuthByte56Offset = 0x56;
        internal const byte NativeAccountTypeFlag55Mask = 0x02;
        internal const byte NativeAccountTypeFlag56Mask = 0x04;
        internal const int NativeAccountSuffixMinimumRecordLength =
            NativeAccountTypeFlag2Offset + 1;

        internal bool PersistNativeAccountSuffixTypeFlags()
        {
            var raw = m_NativeHumanData;
            if (raw == null
                || raw.Length < NativeAccountSuffixMinimumRecordLength)
                return true;

            raw[NativeAccountTypeFlagOffset] = ReadNativeAccountTypeFlag55();
            raw[NativeAccountTypeFlag2Offset] = ReadNativeAccountTypeFlag56();
            return true;
        }

        private byte ReadNativeAccountTypeFlag55()
        {
            if (m_NativeDbSessionSuffix == null
                || m_NativeDbSessionSuffix.Length <= NativeSessionAuthByte55Offset)
                return 0;
            return (m_NativeDbSessionSuffix[NativeSessionAuthByte55Offset]
                    & NativeAccountTypeFlag55Mask) != 0
                ? (byte)1 : (byte)0;
        }

        private byte ReadNativeAccountTypeFlag56()
        {
            if (m_NativeDbSessionSuffix == null
                || m_NativeDbSessionSuffix.Length <= NativeSessionAuthByte56Offset)
                return 0;
            return (m_NativeDbSessionSuffix[NativeSessionAuthByte56Offset]
                    & NativeAccountTypeFlag56Mask) != 0
                ? (byte)1 : (byte)0;
        }
    }
}
