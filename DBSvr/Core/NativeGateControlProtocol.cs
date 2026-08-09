using System;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public static class NativeGateControlProtocol
    {
        public const ushort RegisterRequest = 3;
        public const ushort RegisterResponse = 13;
        public const ushort OpenRequest = 1;
        public const ushort OpenResponse = 11;
        public const ushort DataRequest = 4;
        public const ushort DataResponse = 14;
        public const ushort CloseRequest = 6;
        public const ushort CloseResponse = 16;

        public static bool TryCreateResponse(YbDbLegacy77Frame request, int gateIndex,
            out YbDbLegacy77Frame response)
        {
            response = null;
            if (request == null) return false;
            switch (request.Ident)
            {
                case RegisterRequest:
                    response = new YbDbLegacy77Frame(
                        checked(gateIndex + 1), 0, RegisterResponse, Array.Empty<byte>());
                    return true;
                case OpenRequest:
                    response = new YbDbLegacy77Frame(
                        request.QueryId, 0, OpenResponse, Array.Empty<byte>());
                    return true;
                case CloseRequest:
                    response = new YbDbLegacy77Frame(
                        request.QueryId, request.Param, CloseResponse, Array.Empty<byte>());
                    return true;
                default:
                    return false;
            }
        }
    }
}
