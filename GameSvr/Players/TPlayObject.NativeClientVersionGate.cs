using System.Globalization;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const uint NativeClientVersionCheckInterval = 15_000;
        private const int NativeClientVersionDisconnectIdent = 10_000;
        private const int NativeClientVersionDisconnectDelay = 500;
        private const ushort NativeClientVersionPenaltySeconds = 600;
        private const int NativeClientVersionLowLevelMuteSeconds = 3_600;
        private const int NativeClientVersionTextCapacity = 15;

        /// <summary>
        /// Native pre-dispatch arm <c>0x6D7E68..0x6D7F9B</c>. The first 1018
        /// is consumed after producing the version string and B75 result.
        /// </summary>
        internal bool ShouldDispatchNativeClientMessage(ClientPacket packet)
        {
            if (packet == null)
            {
                return false;
            }

            if (m_boNativeClientVersionHandshakeDone)
            {
                return true;
            }

            // 0x6D7E85 tests byte 3 bit 5 of the native ServerSwitch set before
            // admitting 3340 ahead of the 1018 handshake.
            if (packet.Ident == Grobal2.CM_3340)
            {
                return NativeClientVersionPolicy
                    .IsClientInfoCollectionEnabled();
            }

            if (packet.Ident != Grobal2.CM_LOGINNOTICEOK)
            {
                return false;
            }

            m_boNativeClientVersionHandshakeDone = true;
            m_sNativeClientVersion = BuildNativeClientVersion(packet);
            m_boNativeSwitchOffsetB75 =
                NativeClientVersionPolicy.IsAllowed(m_sNativeClientVersion);
            return false;
        }

        internal void InitializeNativeClientVersionRunGate(int currentTick)
        {
            // 0x6B2335 seeds +0x738 after login initialization.
            m_dwNativeClientVersionCheckTick = currentTick;

            // 0x6B21D4 cmp permission,3 / 0x6B2215 mov [self+B75],1.
            // Ordinary players are deliberately not cleared: mode-2 restore may
            // already have supplied the original B75 value.
            if (m_btPermission >= 3)
            {
                m_boNativeSwitchOffsetB75 = true;
            }
        }

        internal void ApplyNativeClientVersionReconnectBypass(bool restored)
        {
            // sub_6B9A2C checks suffix+0xEFA4 (the mode-2 serial) and, after
            // restoring the extension and finding a valid map, writes +0x680.
            if (restored)
            {
                m_boNativeClientVersionHandshakeDone = true;
            }
        }

        /// <summary>
        /// Native <c>TPlayer.Run 0x6B377C..0x6B3874</c>. The clock advances
        /// before B75 is inspected, including the allowed-client path.
        /// </summary>
        internal void RunNativeClientVersionGate(int currentTick)
        {
            var elapsed = unchecked((uint)(currentTick -
                m_dwNativeClientVersionCheckTick));
            if (elapsed < NativeClientVersionCheckInterval)
            {
                return;
            }

            m_dwNativeClientVersionCheckTick = currentTick;
            if (m_boNativeSwitchOffsetB75)
            {
                return;
            }

            NativeMakePosion(25, NativeClientVersionPenaltySeconds, 0);

            // Native also clears +7B0/+7B4/+7C3/+7C4 here. The quiz poser and
            // answer-window driver are dormant in this port, so those four
            // values have no live carrier and are already permanently zero.
            if (m_Abil.Level < 8)
            {
                NativeMirrorChatBan.Add(m_sCharName,
                    NativeClientVersionLowLevelMuteSeconds);
            }

            SysMsg("客户端版本错误，游戏中断...",
                MsgColor.Red, MsgType.Hint);
            SendDelayMsg(this, NativeClientVersionDisconnectIdent,
                0, 0, 0, 0, string.Empty,
                NativeClientVersionDisconnectDelay);
        }

        private static string BuildNativeClientVersion(ClientPacket packet)
        {
            var version = string.Format(CultureInfo.InvariantCulture,
                "{0}.{1}.{2}.{3}", packet.Recog, packet.Param,
                packet.Tag, packet.Series);
            return version.Length <= NativeClientVersionTextCapacity
                ? version
                : version[..NativeClientVersionTextCapacity];
        }
    }
}
