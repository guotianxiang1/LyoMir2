using System.Buffers.Binary;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const int NativeCurrentPersonalRankingOffset = 0x0058;
        internal const int NativeOverallPersonalRankingOffset = 0x005A;
        internal const int NativeApprenticeRankingOffset = 0x005C;
        internal const int NativePreviousPersonalRankingOffset = 0x0172;

        public ushort m_wNativeCurrentPersonalRanking;

        internal ushort GetNativeCurrentPersonalRanking()
        {
            return m_wNativeCurrentPersonalRanking != 0
                ? m_wNativeCurrentPersonalRanking
                : ReadNativeRanking(NativeCurrentPersonalRankingOffset);
        }

        internal ushort GetNativeOverallPersonalRanking() =>
            ReadNativeRanking(NativeOverallPersonalRankingOffset);

        internal ushort GetNativeApprenticeRanking() =>
            ReadNativeRanking(NativeApprenticeRankingOffset);

        internal ushort GetNativePreviousPersonalRanking()
        {
            return m_NativeHumanData != null
                   && m_NativeHumanData.Length >=
                   NativePreviousPersonalRankingOffset + sizeof(ushort)
                ? BinaryPrimitives.ReadUInt16LittleEndian(
                    m_NativeHumanData.AsSpan(
                        NativePreviousPersonalRankingOffset, sizeof(ushort)))
                : (ushort)0;
        }

        internal void SetNativePreviousPersonalRanking(ushort ranking)
        {
            if (m_NativeHumanData == null
                || m_NativeHumanData.Length <
                NativePreviousPersonalRankingOffset + sizeof(ushort))
                return;
            BinaryPrimitives.WriteUInt16LittleEndian(
                m_NativeHumanData.AsSpan(
                    NativePreviousPersonalRankingOffset, sizeof(ushort)),
                ranking);
        }

        private ushort ReadNativeRanking(int offset)
        {
            return m_NativeDbSessionSuffix != null
                   && m_NativeDbSessionSuffix.Length >= offset + sizeof(ushort)
                ? BinaryPrimitives.ReadUInt16LittleEndian(
                    m_NativeDbSessionSuffix.AsSpan(offset, sizeof(ushort)))
                : (ushort)0;
        }
    }
}
