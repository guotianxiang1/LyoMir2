using System;
using SystemModule.Packet;

namespace DBSvr.Core
{
    /// <summary>
    /// State transition for the native 4041 (CM_LOGIN_ALREADY_ONLINE) return
    /// to the login screen.
    ///
    /// The 2.08 handler clears the account at Self+0x24, the selected
    /// character at Self+0x44, and its state byte before returning false.  The
    /// caller subsequently emits the ordinary 4018 response.  This helper
    /// only models the user-connection state that exists in the C# object; it
    /// deliberately does not close the global LoginSoc session because the
    /// native handler does not prove that operation at this call site.
    /// </summary>
    public static class NativeLoginAlreadyOnlineProtocol
    {
        public const ushort Command = 4041;
        public const ushort KickoutCommand = 4040;
        public const ushort RouteClearCommand = 12;

        // fn_5D0714 stores DL and branches on that byte. The high byte of the
        // 16-bit wire parameter is ignored by the native handler.
        public static bool UsesReturnToLoginLeg(ushort parameter) =>
            unchecked((byte)parameter) == 0;

        public static bool TryCreateRouteClearFrame(ushort connectionId,
            out byte[] wire)
        {
            return YbDbLegacy77Codec.TryEncode(new YbDbLegacy77Frame(
                connectionId, 0, RouteClearCommand, Array.Empty<byte>()),
                out wire, out _);
        }

        public static void ResetForReturnToLogin(TUserInfo userInfo)
        {
            if (userInfo == null) return;

            userInfo.sAccount = string.Empty;
            userInfo.NativeCurrentCharName = string.Empty;
            userInfo.NativeSessionState = 0;
        }
    }
}
