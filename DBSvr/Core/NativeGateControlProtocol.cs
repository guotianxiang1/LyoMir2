using System;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public static class NativeGateControlProtocol
    {
        public const ushort RegisterRequest = NativeGameGateDbProtocol.RegisterRequest;
        public const ushort RegisterResponse = NativeGameGateDbProtocol.RegisterResponse;
        public const ushort OpenRequest = NativeGameGateDbProtocol.OpenRequest;
        public const ushort OpenResponse = NativeGameGateDbProtocol.OpenResponse;
        public const ushort DataRequest = NativeGameGateDbProtocol.DataRequest;
        public const ushort DataResponse = NativeGameGateDbProtocol.DataResponse;
        public const ushort CloseRequest = NativeGameGateDbProtocol.CloseRequest;
        public const ushort CloseResponse = NativeGameGateDbProtocol.CloseResponse;

        public static bool TryCreateResponse(YbDbLegacy77Frame request,
            int assignedGateId,
            out YbDbLegacy77Frame response)
        {
            response = null;
            if (request == null) return false;
            switch (request.Ident)
            {
                case RegisterRequest:
                    if (assignedGateId is < 1
                        or > NativeGameGateRegistrationTable.MaximumGateCount)
                        return false;
                    response = new YbDbLegacy77Frame(
                        assignedGateId, 0, RegisterResponse,
                        Array.Empty<byte>());
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
