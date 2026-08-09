using System;
using System.Globalization;
using System.Text;

namespace SystemModule.Packet
{
    /// <summary>
    /// Dormant codec and pure response-route model for the native
    /// @CancelYBDeal 124/1124 exchange. The external 6108 authority is not
    /// available, so this type must not be wired into the live transport.
    /// </summary>
    public static class YbDbCancelDealProtocol
    {
        public const ushort RequestIdent = 124;
        public const ushort ResponseIdent = 1124;
        public const int RequestQueryId = 0;
        public const int RequestParam = 0;

        public const int RequestIdentitySize = YbDbLegacy77Codec.IdentitySize;
        public const int RequestTargetOffset = RequestIdentitySize;
        public const int RequestTerminatorSize = 1;
        public const int RequestFixedPayloadSize =
            RequestIdentitySize + RequestTerminatorSize;
        public const int MaximumTargetByteLength =
            YbDbLegacy77Codec.MaximumPayloadLength - RequestFixedPayloadSize;

        public const int ResponsePayloadSize = 64;
        public const int ResponseRoleNameOffset = 32;
        public const int ResponseTargetNameOffset = 48;
        public const int ResponseNameCapacity = 15;
        public const int SuccessResult = 1;
        public const ushort NativeMessageKind = 0xFFDB;

        private static readonly Encoding Gbk;

        static YbDbCancelDealProtocol()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }

        public static bool TryCreateRequest(YbDbLegacy77Identity identity,
            string targetRoleName, out YbDbLegacy77Frame frame,
            out string error)
        {
            frame = null;
            error = string.Empty;
            if (string.IsNullOrEmpty(targetRoleName))
            {
                error = "legacy YBDB cancel-deal target role is empty";
                return false;
            }
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var identityBytes, out error))
                return false;

            byte[] targetBytes;
            try
            {
                targetBytes = Gbk.GetBytes(targetRoleName);
            }
            catch (EncoderFallbackException ex)
            {
                error = "legacy YBDB cancel-deal target is not GBK: " +
                        ex.Message;
                return false;
            }
            if (targetBytes.Length > MaximumTargetByteLength)
            {
                error = $"legacy YBDB cancel-deal target exceeds " +
                        $"{MaximumTargetByteLength} GBK bytes";
                return false;
            }

            var payload = new byte[RequestFixedPayloadSize +
                                   targetBytes.Length];
            identityBytes.CopyTo(payload, 0);
            targetBytes.CopyTo(payload, RequestTargetOffset);
            // The zero-filled final byte is the native NUL terminator.
            frame = new YbDbLegacy77Frame(RequestQueryId, RequestParam,
                RequestIdent, payload);
            return true;
        }

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out CancelDealResult result, out string error)
        {
            result = null;
            error = string.Empty;
            if (frame == null || frame.Ident != ResponseIdent)
            {
                error = "legacy YBDB cancel-deal response Ident must be 1124";
                return false;
            }
            if (frame.Payload.Length != ResponsePayloadSize)
            {
                error = "legacy YBDB cancel-deal response payload must be 64 bytes";
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(frame.Payload,
                    ResponseRoleNameOffset, ResponseNameCapacity,
                    out var roleName, out error)
                || !YbDbLegacy77Codec.TryDecodeShortString(frame.Payload,
                    ResponseTargetNameOffset, ResponseNameCapacity,
                    out var targetRoleName, out error))
                return false;

            result = new CancelDealResult(roleName, targetRoleName,
                frame.QueryId, frame.Param);
            return true;
        }

        public static ResponseRoute EvaluateResponse(CancelDealResult result,
            bool roleEntryFound, bool roleGhostFlag,
            bool roleReadyFlagAtD2C)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var emitMessage = !string.IsNullOrEmpty(result.RoleName)
                              && roleEntryFound
                              && !roleGhostFlag
                              && roleReadyFlagAtD2C;
            return new ResponseRoute(emitMessage,
                emitMessage ? result.Message : string.Empty);
        }

        public sealed class CancelDealResult
        {
            internal CancelDealResult(string roleName, string targetRoleName,
                int resultCode, int ignoredHeaderParam)
            {
                RoleName = roleName;
                TargetRoleName = targetRoleName;
                ResultCode = resultCode;
                IgnoredHeaderParam = ignoredHeaderParam;
            }

            public string RoleName { get; }
            public string TargetRoleName { get; }
            public int ResultCode { get; }
            public int IgnoredHeaderParam { get; }
            public bool IsSuccess => ResultCode == SuccessResult;
            public string Message => IsSuccess
                ? "取消 " + TargetRoleName + " 的元宝交易成功"
                : "取消 " + TargetRoleName + " 的元宝交易失败(" +
                  ResultCode.ToString(CultureInfo.InvariantCulture) + ")";
        }

        public sealed class ResponseRoute
        {
            internal ResponseRoute(bool emitMessage, string message)
            {
                EmitMessage = emitMessage;
                Message = message;
            }

            public bool EmitMessage { get; }
            public string Message { get; }
            public ushort MessageKind => NativeMessageKind;
            public bool MatchesByRoleNameOnly => true;
            public bool ValidatesObjectIdAccountPtidOrSession => false;
            public bool RegistersPendingRequest => false;
            public bool SendsAcknowledgement => false;
            public bool RetriesRequest => false;
            public bool MutatesPlayerDealOrAccountState => false;
            public bool MutatesInventoryOrDatabase => false;
            public bool WritesBusinessOrGameLog => false;
        }
    }
}
