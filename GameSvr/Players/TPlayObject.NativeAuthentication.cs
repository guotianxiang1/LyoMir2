namespace GameSvr
{
    public partial class TPlayObject
    {
        private const string NativeAuthenticationSuccess = "信用分验证成功";
        private const string NativeAuthenticationFailure = "信用分验证失败";
        private const string NativeDelAuthenticationSuccess = "删除信用分验证成功";
        private const string NativeDelAuthenticationFailure = "删除信用分验证失败";
        private const string NativeHelpOtherSuccess = "申请验证小号成功";
        private long _nativeAuthenticationPlayerId;
        private byte[] _nativeAuthenticationPtid = Array.Empty<byte>();
        private bool _nativeAuthenticationIdentityLoaded;
        private readonly object _nativeAuthenticationSync = new();

        internal void ClearNativeAuthenticationIdentity()
        {
            _nativeAuthenticationPlayerId = 0;
            _nativeAuthenticationPtid = Array.Empty<byte>();
            _nativeAuthenticationIdentityLoaded = false;
        }

        internal void SetNativeAuthenticationIdentity(long playerId, byte[] ptid)
        {
            _nativeAuthenticationPlayerId = playerId;
            _nativeAuthenticationPtid = ptid?.ToArray() ?? Array.Empty<byte>();
            _nativeAuthenticationIdentityLoaded = true;
        }

        internal bool TryGetNativeAuthenticationIdentity(out long playerId,
            out byte[] ptid)
        {
            playerId = _nativeAuthenticationPlayerId;
            ptid = _nativeAuthenticationPtid;
            return _nativeAuthenticationIdentityLoaded;
        }

        internal int ActiveNativeAuthentication100()
        {
            return ActiveNativeAuthentication100(
                status => M2Share.AuthenticationManager?.PersistStatus1(this, status) ?? -1,
                SendNativeAuthenticationStatus,
                WriteNativeAuthenticationLog);
        }

        private int ActiveNativeAuthentication100(Func<byte, int> persistStatus,
            Action sendStatus, Action<int, string> writeLog)
        {
            lock (_nativeAuthenticationSync)
            {
                if (!M2Share.g_Config.boAuthOpen)
                    return 0;

                int result;
                if ((_nativeAuthStatus1 & 0x1F) == 0x1F)
                {
                    result = 2;
                }
                else
                {
                    var savedStatus1 = _nativeAuthStatus1;
                    var savedStatus2 = _nativeAuthStatus2;
                    _nativeAuthStatus1 |= 0x1F;
                    result = persistStatus(_nativeAuthStatus1);
                    if (result != 1)
                    {
                        _nativeAuthStatus1 = savedStatus1;
                        _nativeAuthStatus2 = savedStatus2;
                    }
                }

                if (result == 1)
                {
                    ApplyNativeAuthenticationLimits();
                    sendStatus();
                }
                writeLog(result, result == 1
                    ? NativeAuthenticationSuccess
                    : NativeAuthenticationFailure);
                return result;
            }
        }

        private void WriteNativeAuthenticationLog(int result, string description)
        {
            M2Share.AddGameDataLog("95\t" + m_sMapName + "\t" + m_nCurrX +
                "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + m_sCharName +
                "\t100\t" + result + "\t" + description);
        }

        internal int DelActiveNativeAuthentication100()
        {
            return DelActiveNativeAuthentication100(
                status => M2Share.AuthenticationManager?.PersistStatus1(this, status) ?? -1,
                WriteNativeAuthenticationLog);
        }

        // Native ActiveDelAuthen (sub_6F9888) — the delete-mirror of ActiveNativeAuthentication100.
        // Gate byte [*off_7D6534+8] == boAuthOpen (disabled -> return 0, no output). The level-100
        // validator sub_6F9A28 checks "already cleared" ((status1 & byte_6F9AA8[0x1F]) == 0 ->
        // code 2), else clears the order-1 auth byte to byte_6F9AAC (0x00) and persists via
        // sub_618438 (== PersistStatus1). Codes: 0 disabled / 1 ok / 2 already-cleared /
        // (persist result). On success it runs sub_6F9FFC (ApplyLimits) and always writes the
        // game-data-log via sub_768BE0 ("删除信用分验证成功/失败"). Unlike ActiveAuthen it has NO
        // active-only sub_6FA080 post-processor, so it sends NO SM_PLAYER_AUTHEN status packet.
        private int DelActiveNativeAuthentication100(Func<byte, int> persistStatus,
            Action<int, string> writeLog)
        {
            lock (_nativeAuthenticationSync)
            {
                if (!M2Share.g_Config.boAuthOpen)
                    return 0;

                int result;
                if ((_nativeAuthStatus1 & 0x1F) == 0)
                {
                    result = 2;
                }
                else
                {
                    var savedStatus1 = _nativeAuthStatus1;
                    var savedStatus2 = _nativeAuthStatus2;
                    _nativeAuthStatus1 = 0;
                    result = persistStatus(_nativeAuthStatus1);
                    if (result != 1)
                    {
                        _nativeAuthStatus1 = savedStatus1;
                        _nativeAuthStatus2 = savedStatus2;
                    }
                }

                if (result == 1)
                    ApplyNativeAuthenticationLimits();
                writeLog(result, result == 1
                    ? NativeDelAuthenticationSuccess
                    : NativeDelAuthenticationFailure);
                return result;
            }
        }

        internal int HelpOtherNativeAuthentication()
        {
            return HelpOtherNativeAuthentication(
                () => M2Share.AuthenticationManager?.MarkHelpOther(this) ?? 0,
                WriteNativeHelpOtherLog);
        }

        private int HelpOtherNativeAuthentication(Func<int> markHelpOther,
            Action writeLog)
        {
            lock (_nativeAuthenticationSync)
            {
                var result = markHelpOther();
                if (result == 1)
                    writeLog();
                return result;
            }
        }

        private void WriteNativeHelpOtherLog()
        {
            M2Share.AddGameDataLog("94\t" + m_sMapName + "\t" + m_nCurrX +
                "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + m_sCharName +
                "\t1\t0\t" + NativeHelpOtherSuccess);
        }
    }
}
