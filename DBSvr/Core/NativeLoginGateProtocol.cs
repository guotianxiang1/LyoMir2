using System;
using System.Buffers.Binary;
using System.Net;
using System.Text;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public static class NativeLoginGateProtocol
    {
        public const ushort AuthRequestIdent = 2018;
        public const ushort AuthResponseIdent = 1003;
        public const ushort AuthFailureIdent = 1004;
        /// <summary>
        /// This is TSDKAuthHead.wAuthType, not a status: atLoginCenterAuth is the
        /// 7th member of TSDKAuthType (LG uTypes.pas:204) and uSDKAuth.pas:1476
        /// stamps it on every head that enters the 2018 path, so BOTH the 1003 and
        /// the 1004 reply carry 6. Discriminate on the frame ident, never on this.
        /// </summary>
        public const byte AuthSuccessStatus = 6;
        public const byte ProtocolVersion = 1;

        /// <summary>sizeof(TSDKAuthHead), LG uTypes.pas:205-211.</summary>
        public const int AuthHeadSize = 12;

        /// <summary>
        /// 1004 upper bound: uSDKAuth.pas:1127-1133 clamps the trailing message to
        /// StrLen+1 in [2..100] and adds sizeof(TSDKAuthHead). The LoginCenterAuth
        /// path always sends the bare 12 (:608, :766, :1624); the longer form comes
        /// from the SDPT callback only.
        /// </summary>
        public const int AuthFailureMaximumPayloadSize = 112;

        // TSDKAuthHead.nResult codes on the LoginCenterAuth path, LG uSDKAuth.pas:38-43.
        public const int LcAuthSuccess = 0;
        public const int LcAuthFailed = -1;
        public const int LcAuthTimeout = -2;
        public const int LcAuthSystemError = -3;
        public const int LcAuthSystemBusy = -4;
        public const int LcAuthNetError = -5;
        public const ushort RegistrationRequestIdent = 2000;
        public const ushort RegistrationResponseIdent = 1000;
        public const ushort ProbeRequestIdent = 1001;
        public const ushort ProbeResponseIdent = 2001;
        public const ushort Type2ControlEnabledIdent = 0x07D2;
        public const ushort Type2ControlDisabledIdent = 0x07D3;
        public const int RegistrationPayloadSize = 40;
        /// <summary>
        /// TPingMsg.GroupName length from the LG Delphi source
        /// (uTypes.pas:165 `GroupName: array[0..15] of Char`). HumCounts starts
        /// right after it at payload+0x10: [0]=锻造人数, [1..5]=GS1..GS5 online.
        /// </summary>
        public const int GroupNameSize = 16;
        public const int HumanCountsOffset = 16;
        public const int HumanCountsSlots = 6;
        public const int ProbePayloadSize = 28;
        public const int AuthRequestPayloadSize = 136;
        public const int AuthResponsePayloadSize = 124;

        private static readonly Encoding Ascii;
        private static readonly Encoding Gbk;

        static NativeLoginGateProtocol()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Ascii = Encoding.GetEncoding(Encoding.ASCII.CodePage,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        public static bool TryCreateRegistration(string serverName,
            int userCount, out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            error = string.Empty;
            byte[] nameBytes;
            try
            {
                nameBytes = Gbk.GetBytes(serverName ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = "native LoginGate server name is not GBK: " + ex.Message;
                return false;
            }
            // TPingMsg.GroupName is array[0..15] of Char = 16 bytes
            // (LG source uTypes.pas:165-168). Anything past 16 would overwrite
            // HumCounts[0] (锻造人数) and be truncated by the peer, which reads
            // only payload[0..16) — see LoginGateWireProtocol.TryParseNativeRegistration.
            if (nameBytes.Length > GroupNameSize)
            {
                error = "native LoginGate server name exceeds 16 GBK bytes";
                return false;
            }

            var payload = new byte[RegistrationPayloadSize];
            nameBytes.CopyTo(payload, 0);
            // HumCounts[0] = 锻造人数 (left 0; DBServer does not report it).
            // HumCounts[1] = this group's online count — LG's global online total
            // sums slot 1 across groups (uFormMain.pas:328 CalcTotalHumanCount).
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(HumanCountsOffset + 1 * 4, 4), userCount);
            // HumCounts[2..4] = -1 marks "no such GS" (LG blanks negative slots,
            // uFormMain.pas:247-255). HumCounts[5] stays 0.
            for (var slot = 2; slot <= 4; slot++)
                BinaryPrimitives.WriteInt32LittleEndian(
                    payload.AsSpan(HumanCountsOffset + slot * 4, 4), -1);
            // The original sender leaves the outer QueryId/Param bytes uninitialized.
            // Zero them so the compatible implementation never leaks process memory.
            frame = new YbDbLegacy77Frame(0, 0,
                RegistrationRequestIdent, payload);
            return true;
        }

        public static bool IsRegistrationResponse(YbDbLegacy77Frame frame)
        {
            return frame != null
                   && frame.Ident == RegistrationResponseIdent
                   && frame.Payload.Length == 0;
        }

        public static YbDbLegacy77Frame CreateType2Control(bool enabled) =>
            new(0, 0, enabled ? Type2ControlEnabledIdent
                : Type2ControlDisabledIdent, Array.Empty<byte>());

        public static bool TryCreateProbeResponse(YbDbLegacy77Frame request,
            string gameGateAddress, ushort gameGatePort,
            ushort zoneIndex, byte groupIndex,
            out YbDbLegacy77Frame response, out string error)
        {
            response = null;
            error = string.Empty;
            if (request == null || request.Ident != ProbeRequestIdent
                                || request.Payload.Length != ProbePayloadSize)
            {
                error = $"native LoginGate probe must be ident {ProbeRequestIdent} with {ProbePayloadSize} bytes";
                return false;
            }
            if (!IPAddress.TryParse(gameGateAddress, out var address))
            {
                error = "native LoginGate GameGate address is invalid";
                return false;
            }
            var addressBytes = address.GetAddressBytes();
            if (addressBytes.Length != 4)
            {
                error = "native LoginGate GameGate address must be IPv4";
                return false;
            }
            if (gameGatePort == 0)
            {
                error = "native LoginGate GameGate port must be non-zero";
                return false;
            }

            _ = zoneIndex;
            _ = groupIndex;

            var payload = (byte[])request.Payload.Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2),
                gameGatePort);
            addressBytes.CopyTo(payload, 12);
            // Echo wAreaID@16 and bGroupNo@18 from the request. Native DB
            // may overwrite them; C# used to stamp its own _zoneIndex/_groupIndex
            // which broke a LoginGate that fronts more than one area.
            // bErrorType@19 stays 0 (success). ciSessionID/iEnCodeIdx/
            // wSocketHandle/szPostfix are already the cloned request bytes.
            payload[19] = 0;
            response = new YbDbLegacy77Frame(0, 0,
                ProbeResponseIdent, payload);
            return true;
        }

        public static bool TryCreateAuthRequest(int queryId, string ticket,
            byte[] deviceId, string userIp, string macAddress,
            ushort areaId, ushort groupId, out YbDbLegacy77Frame frame,
            out string error)
        {
            frame = null;
            error = string.Empty;
            if (deviceId == null || deviceId.Length != 8)
            {
                error = "native LoginGate device id must be 8 bytes";
                return false;
            }

            var payload = new byte[AuthRequestPayloadSize];
            payload[0] = 0;
            payload[1] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(2, 4), queryId);
            if (!TryWriteCString(payload.AsSpan(12, 52), ticket, out error)
                || !TryWriteCString(payload.AsSpan(96, 16), userIp, out error)
                || !TryWriteCString(payload.AsSpan(112, 20), macAddress, out error))
            {
                return false;
            }
            deviceId.CopyTo(payload, 64);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(132, 2), areaId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(134, 2), groupId);
            frame = new YbDbLegacy77Frame(0, 0, AuthRequestIdent, payload);
            return true;
        }

        public static bool TryDecodeAuthResponse(YbDbLegacy77Frame frame,
            out NativeLoginGateAuthResponse response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native LoginGate response is null";
                return false;
            }
            if (frame.Ident != AuthResponseIdent)
            {
                error = $"native LoginGate response ident must be {AuthResponseIdent}";
                return false;
            }
            if (frame.Payload.Length != AuthResponsePayloadSize)
            {
                error = $"native LoginGate response payload must be {AuthResponsePayloadSize} bytes";
                return false;
            }

            try
            {
                response = new NativeLoginGateAuthResponse(
                    frame.Payload[0], frame.Payload[1],
                    BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(2, 4)),
                    frame.Payload.AsSpan(6, 6).ToArray(),
                    ReadCString(frame.Payload.AsSpan(12, 21)),
                    ReadCString(frame.Payload.AsSpan(33, 21)),
                    ReadCString(frame.Payload.AsSpan(54, 21)),
                    BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload.AsSpan(75, 2)),
                    frame.Payload[77], frame.Payload[78],
                    frame.Payload[79], frame.Payload[80],
                    ReadCString(frame.Payload.AsSpan(81, 21)),
                    ReadCString(frame.Payload.AsSpan(102, 21)),
                    frame.Payload);
                return true;
            }
            catch (DecoderFallbackException ex)
            {
                error = "invalid native LoginGate response text: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// GDM_SDK_AUTH_RESPONSE_FAIL. Accepts the whole native size range so a
        /// real LoginGate can never be dropped: 12 from the LoginCenterAuth
        /// senders, up to 112 when the SDPT callback appends an error string.
        /// </summary>
        public static bool TryDecodeAuthFailure(YbDbLegacy77Frame frame,
            out NativeLoginGateAuthFailure failure, out string error)
        {
            failure = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native LoginGate failure frame is null";
                return false;
            }
            if (frame.Ident != AuthFailureIdent)
            {
                error = $"native LoginGate failure ident must be {AuthFailureIdent}";
                return false;
            }
            if (frame.Payload.Length < AuthHeadSize
                || frame.Payload.Length > AuthFailureMaximumPayloadSize)
            {
                error = $"native LoginGate failure payload must be {AuthHeadSize}"
                        + $" to {AuthFailureMaximumPayloadSize} bytes";
                return false;
            }

            // The trailing text is raw bytes from the vendor SDK (uSDKAuth.pas:1137
            // Move()s them verbatim). Treat it as GBK like every other native
            // string, but never let a decode failure discard the head.
            var message = string.Empty;
            if (frame.Payload.Length > AuthHeadSize)
            {
                var tail = frame.Payload.AsSpan(AuthHeadSize);
                var terminator = tail.IndexOf((byte)0);
                if (terminator < 0) terminator = tail.Length;
                try { message = Gbk.GetString(tail.Slice(0, terminator)); }
                catch (DecoderFallbackException) { message = string.Empty; }
            }

            failure = new NativeLoginGateAuthFailure(
                frame.Payload[0], frame.Payload[1],
                BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(2, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(8, 4)),
                message);
            return true;
        }

        /// <summary>
        /// Diagnostic wording only — never put this on the wire. Taken from the
        /// LoginGate's own log strings for the same constants, uSDKAuth.pas:519-527.
        /// </summary>
        public static string DescribeAuthFailure(int nResult) => nResult switch
        {
            LcAuthFailed => "认证账号或密码错误",
            LcAuthTimeout => "认证超时或授权到期",
            LcAuthSystemError => "认证错误",
            LcAuthSystemBusy => "认证系统繁忙",
            LcAuthNetError => "网络错误或版本号不一致",
            LcAuthSuccess => "认证失败帧携带了 LC_AUTH_SUCCESS(0)，发送端未写 nResult",
            _ => $"未知认证结果 {nResult}"
        };

        private static bool TryWriteCString(Span<byte> destination, string value,
            out string error)
        {
            error = string.Empty;
            byte[] bytes;
            try
            {
                bytes = Ascii.GetBytes(value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = "native LoginGate text is not ASCII: " + ex.Message;
                return false;
            }
            if (bytes.Length >= destination.Length)
            {
                error = $"native LoginGate text exceeds {destination.Length - 1} bytes";
                return false;
            }
            destination.Clear();
            bytes.CopyTo(destination);
            return true;
        }

        private static string ReadCString(ReadOnlySpan<byte> source)
        {
            var terminator = source.IndexOf((byte)0);
            if (terminator < 0) terminator = source.Length;
            return Ascii.GetString(source.Slice(0, terminator));
        }
    }

    public static class NativeMobileLoginAuthCodec
    {
        public const int CapturedBodySize = 69;

        private static readonly Encoding Ascii = Encoding.GetEncoding(
            Encoding.ASCII.CodePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        public static bool TryDecode(byte[] body,
            out NativeMobileLoginAuthRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (body == null)
            {
                error = "native mobile login body is null";
                return false;
            }

            try
            {
                if (!TryReadCString(body, 0, out var ticket, out var offset, out error))
                    return false;
                if (string.IsNullOrEmpty(ticket))
                {
                    error = "native mobile login ticket is empty";
                    return false;
                }
                if (offset + 9 > body.Length)
                {
                    error = "native mobile login device id is truncated";
                    return false;
                }

                var deviceId = body.AsSpan(offset, 8).ToArray();
                offset += 8;
                if (body[offset++] != 0)
                {
                    error = "native mobile login device id separator is missing";
                    return false;
                }
                if (!TryReadCString(body, offset, out var gameType, out offset, out error)
                    || !TryReadCString(body, offset, out var deviceName, out offset, out error))
                    return false;
                if (offset != body.Length)
                {
                    error = "native mobile login body has trailing bytes";
                    return false;
                }

                request = new NativeMobileLoginAuthRequest(
                    ticket, deviceId, gameType, deviceName);
                return true;
            }
            catch (DecoderFallbackException ex)
            {
                error = "native mobile login text is not ASCII: " + ex.Message;
                return false;
            }
        }

        private static bool TryReadCString(byte[] body, int start,
            out string value, out int next, out string error)
        {
            value = string.Empty;
            next = start;
            error = string.Empty;
            if (start < 0 || start >= body.Length)
            {
                error = "native mobile login C string is truncated";
                return false;
            }
            var terminator = Array.IndexOf(body, (byte)0, start);
            if (terminator < 0)
            {
                error = "native mobile login C string is not terminated";
                return false;
            }
            value = Ascii.GetString(body, start, terminator - start);
            next = terminator + 1;
            return true;
        }
    }

    public sealed class NativeMobileLoginAuthRequest
    {
        public NativeMobileLoginAuthRequest(string ticket, byte[] deviceId,
            string gameType, string deviceName)
        {
            Ticket = ticket ?? string.Empty;
            DeviceId = deviceId ?? Array.Empty<byte>();
            GameType = gameType ?? string.Empty;
            DeviceName = deviceName ?? string.Empty;
        }

        public string Ticket { get; }
        public byte[] DeviceId { get; }
        public string GameType { get; }
        public string DeviceName { get; }
    }

    /// <summary>
    /// TSDKAuthHead carried by GDM_SDK_AUTH_RESPONSE_FAIL (1004), plus the
    /// optional SDPT error text. QueryId spans wSocketHandle+DynAuthIdent as one
    /// dword, exactly as TryCreateAuthRequest wrote it.
    /// </summary>
    public sealed class NativeLoginGateAuthFailure
    {
        public NativeLoginGateAuthFailure(byte authType, byte gateIndex,
            int queryId, int result, string message)
        {
            AuthType = authType;
            GateIndex = gateIndex;
            QueryId = queryId;
            Result = result;
            Message = message ?? string.Empty;
        }

        public byte AuthType { get; }
        public byte GateIndex { get; }
        public int QueryId { get; }

        /// <summary>TSDKAuthHead.nResult at +8; one of the LcAuth* codes.</summary>
        public int Result { get; }

        public string Message { get; }

        public string Describe() =>
            Message.Length == 0
                ? NativeLoginGateProtocol.DescribeAuthFailure(Result)
                : $"{NativeLoginGateProtocol.DescribeAuthFailure(Result)}: {Message}";
    }

    public sealed class NativeLoginGateAuthResponse
    {
        public NativeLoginGateAuthResponse(byte status, byte version,
            int queryId, byte[] reserved6To11, string account,
            string text33, string text54, ushort flags75,
            byte byte77, byte byte78, byte byte79, byte byte80,
            string text81, string text102, byte[] rawPayload)
        {
            Status = status;
            Version = version;
            QueryId = queryId;
            Reserved6To11 = reserved6To11 == null
                ? Array.Empty<byte>()
                : (byte[])reserved6To11.Clone();
            Account = account ?? string.Empty;
            Text33 = text33 ?? string.Empty;
            Text54 = text54 ?? string.Empty;
            Flags75 = flags75;
            Byte77 = byte77;
            Byte78 = byte78;
            Byte79 = byte79;
            Byte80 = byte80;
            Text81 = text81 ?? string.Empty;
            Text102 = text102 ?? string.Empty;
            RawPayload = rawPayload == null
                ? Array.Empty<byte>()
                : (byte[])rawPayload.Clone();
        }

        public byte Status { get; }
        public byte Version { get; }
        public int QueryId { get; }
        public byte[] Reserved6To11 { get; }
        public string Account { get; }
        public string Text33 { get; }
        public string Text54 { get; }
        public ushort Flags75 { get; }
        public byte Byte77 { get; }
        public byte Byte78 { get; }
        public byte Byte79 { get; }
        public byte Byte80 { get; }
        public string Text81 { get; }
        public string Text102 { get; }
        public byte[] RawPayload { get; }
    }
}
