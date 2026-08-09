using DBSvr.Core;

namespace GameSvr
{
    public partial class TPlayObject
    {
        protected override ReadOnlySpan<byte> GetNativeFixedAbilityRecord()
        {
            return m_NativeHumanData != null &&
                m_NativeHumanData.Length == NativeHumanDataCodec.DataRecordSize
                ? m_NativeHumanData
                : ReadOnlySpan<byte>.Empty;
        }
    }
}
