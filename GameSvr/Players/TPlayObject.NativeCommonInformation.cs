using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const int NativeCommonInformationFlagsOffset = 0x060C;
        internal const int NativeCommonInformationModeOffset = 0x0610;

        public ushort m_wNativeCommonInformationFlags;
        public byte m_btNativeCommonInformationMode;

        internal void RestoreNativeCommonInformation()
        {
            m_wNativeCommonInformationFlags = m_NativeHumanData != null
                                              && m_NativeHumanData.Length >=
                                              NativeCommonInformationFlagsOffset + sizeof(ushort)
                ? BinaryPrimitives.ReadUInt16LittleEndian(
                    m_NativeHumanData.AsSpan(NativeCommonInformationFlagsOffset, sizeof(ushort)))
                : (ushort)0;
            m_btNativeCommonInformationMode = m_NativeHumanData != null
                                              && m_NativeHumanData.Length >
                                              NativeCommonInformationModeOffset
                ? m_NativeHumanData[NativeCommonInformationModeOffset]
                : (byte)0;
        }

        internal bool PersistNativeCommonInformation()
        {
            if (m_NativeHumanData == null
                || m_NativeHumanData.Length <= NativeCommonInformationModeOffset)
            {
                return m_wNativeCommonInformationFlags == 0
                       && m_btNativeCommonInformationMode == 0;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(
                m_NativeHumanData.AsSpan(NativeCommonInformationFlagsOffset, sizeof(ushort)),
                m_wNativeCommonInformationFlags);
            m_NativeHumanData[NativeCommonInformationModeOffset] =
                m_btNativeCommonInformationMode;
            return true;
        }

        private void ClientCommonInformation(TProcessMessage processMsg)
        {
            var value = processMsg.nParam1;
            switch (unchecked((ushort)processMsg.nParam2))
            {
                case 1:
                case 2:
                case 3:
                {
                    var hero = m_HeroObject;
                    if (hero == null || hero.m_boDeath) return;
                    switch (unchecked((ushort)processMsg.nParam2))
                    {
                        case 1:
                            hero.m_boNativeCommonInformationOption1 = value > 0;
                            break;
                        case 2:
                            hero.m_nNativeCommonInformationOption2 = value == 0 ? 1 : value;
                            break;
                        case 3:
                            hero.m_boNativeCommonInformationOption3 = value > 0;
                            break;
                    }
                    break;
                }
                case 4:
                    switch (unchecked((ushort)processMsg.nParam3))
                    {
                        case 0:
                            m_wNativeCommonInformationFlags |= 1;
                            if (value != 0) m_wNativeCommonInformationFlags |= 2;
                            else m_wNativeCommonInformationFlags &= unchecked((ushort)~2);
                            break;
                        case 1:
                            m_wNativeCommonInformationFlags |= 1;
                            if (value != 0) m_wNativeCommonInformationFlags |= 4;
                            else m_wNativeCommonInformationFlags &= unchecked((ushort)~4);
                            break;
                        default:
                            return;
                    }
                    PersistNativeCommonInformation();
                    FeatureChanged();
                    m_HeroObject?.FeatureChanged();
                    break;
                case 5:
                    if ((uint)value < 2)
                    {
                        m_btNativeCommonInformationMode = (byte)value;
                        PersistNativeCommonInformation();
                    }
                    break;
            }
        }
    }
}
