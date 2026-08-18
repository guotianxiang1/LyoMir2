using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const int NativeItemMovementSmsSuffixOffset = 0x56;
        internal const byte NativeItemMovementSmsSuffixMask = 0x01;
        internal const string NativeItemMovementSmsLoginNotice =
            "您已经开通了极品装备异动短信提醒功能！";

        internal bool RestoreNativeItemMovementSmsState()
        {
            m_boNativeItemMovementSmsEnabled = m_NativeDbSessionSuffix != null
                && m_NativeDbSessionSuffix.Length >
                    NativeItemMovementSmsSuffixOffset
                && (m_NativeDbSessionSuffix[NativeItemMovementSmsSuffixOffset]
                    & NativeItemMovementSmsSuffixMask) != 0;
            return m_NativeDbSessionSuffix != null
                && m_NativeDbSessionSuffix.Length >
                    NativeItemMovementSmsSuffixOffset;
        }

        internal void SendNativeItemMovementSmsLoginNotice()
        {
            // UserLogon 0x6B21D4 gates the containing admin block at permission >= 3;
            // 0x6B221C then tests actor+0x4C6 and sends cx=0xFFDB (green).
            if (m_btPermission >= 3 && m_boNativeItemMovementSmsEnabled)
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0xFFDB, 0, 0, 0,
                    NativeItemMovementSmsLoginNotice,
                    BuildNativeTerminatedTextBody(
                        NativeItemMovementSmsLoginNotice));
        }
    }
}
