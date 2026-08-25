using System.Threading;

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

        public static void ResetForReturnToLogin(TUserInfo userInfo)
        {
            if (userInfo == null) return;

            userInfo.sAccount = string.Empty;
            userInfo.boChrSelected = false;
            userInfo.boChrQueryed = false;
            userInfo.NativeAuthTick = 0;
            userInfo.NativeAuthResponse = null;
            userInfo.NativeText102 = string.Empty;
            Interlocked.Exchange(ref userInfo.NativeLoginDateTimeBits, 0);
            userInfo.sReconnectID = string.Empty;
            userInfo.NativeSwitchHandoff.Reset();
        }
    }
}
