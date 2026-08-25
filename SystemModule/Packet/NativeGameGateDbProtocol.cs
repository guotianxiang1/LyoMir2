using System;
using System.Text;

namespace SystemModule.Packet
{
    /// <summary>
    /// Native 2.08 GameGate-to-DBServer control family carried by the
    /// 0x33AABB77 envelope.
    /// </summary>
    public static class NativeGameGateDbProtocol
    {
        public const ushort OpenRequest = 1;
        public const ushort RegisterRequest = 3;
        public const ushort DataRequest = 4;
        public const ushort CloseRequest = 6;
        public const ushort OpenResponse = 11;
        public const ushort RegisterResponse = 13;
        public const ushort DataResponse = 14;
        public const ushort CloseResponse = 16;

        public const int MinimumAssignedGateId = 1;
        public const int MaximumAssignedGateId = byte.MaxValue;
        public static bool IsValidAssignedGateId(int gateId) =>
            gateId is >= MinimumAssignedGateId and <= MaximumAssignedGateId;

        public static int ComposeRouteId(int gateId, ushort sessionId)
        {
            if (!IsValidAssignedGateId(gateId))
                throw new ArgumentOutOfRangeException(nameof(gateId));
            return unchecked((gateId << 17) | sessionId);
        }

        public static bool TryCreateRegistration(int gatePort,
            out byte[] wire, out string error)
        {
            if (gatePort <= 0)
                return Fail("native DB gate port must be positive",
                    out wire, out error);
            return TryEncode(new YbDbLegacy77Frame(gatePort, 0,
                RegisterRequest, Array.Empty<byte>()), out wire, out error);
        }

        public static bool TryDecodeRegistrationResponse(
            YbDbLegacy77Frame frame, out int assignedGateId)
        {
            assignedGateId = 0;
            if (frame == null || frame.Ident != RegisterResponse)
                return false;
            assignedGateId = frame.QueryId & byte.MaxValue;
            return IsValidAssignedGateId(assignedGateId);
        }

        public static bool TryCreateOpen(ushort sessionId, int gateId,
            string clientIp, out byte[] wire, out string error)
        {
            wire = null;
            error = string.Empty;
            clientIp ??= string.Empty;
            if (clientIp.IndexOf('\0') >= 0)
                return Fail("native DB client IP contains NUL",
                    out wire, out error);
            for (var i = 0; i < clientIp.Length; i++)
                if (clientIp[i] > 0x7F)
                    return Fail("native DB client IP is not ASCII",
                        out wire, out error);

            var ascii = Encoding.ASCII.GetBytes(clientIp);
            var payload = new byte[ascii.Length + 1];
            ascii.CopyTo(payload, 0);
            return TryEncode(new YbDbLegacy77Frame(sessionId,
                ComposeRouteId(gateId, sessionId), OpenRequest, payload),
                out wire, out error);
        }

        public static bool TryCreateData(ushort sessionId, uint routeId,
            SystemModule.ClientPacket message, byte[] body,
            out byte[] wire, out string error)
        {
            wire = null;
            error = string.Empty;
            if (message == null)
                return Fail("native DB client packet is null",
                    out wire, out error);
            if (routeId == 0 || routeId > int.MaxValue)
                return Fail("native DB route id is out of range",
                    out wire, out error);
            var request = LegacyGateDataCodec.CreateRequest(sessionId,
                checked((int)routeId), message.Recog,
                message.Ident, message.Param, message.Tag, message.Series,
                body);
            return TryEncode(request, out wire, out error);
        }

        public static bool TryCreateClose(ushort sessionId,
            int backendContext,
            out byte[] wire, out string error) =>
            TryEncode(new YbDbLegacy77Frame(sessionId,
                backendContext, CloseRequest, Array.Empty<byte>()),
                out wire, out error);

        public static bool IsOpenResponse(YbDbLegacy77Frame frame,
            out int result)
        {
            result = 0;
            if (frame == null || frame.Ident != OpenResponse)
                return false;
            result = frame.Param;
            return true;
        }

        public static bool IsCloseResponse(YbDbLegacy77Frame frame,
            int backendContext) =>
            frame != null && frame.Ident == CloseResponse
            && frame.Param == backendContext
            && frame.Payload.Length == 0;

        private static bool TryEncode(YbDbLegacy77Frame frame,
            out byte[] wire, out string error) =>
            YbDbLegacy77Codec.TryEncode(frame, out wire, out error);

        private static bool Fail(string message, out byte[] wire,
            out string error)
        {
            wire = null;
            error = message;
            return false;
        }
    }
}
