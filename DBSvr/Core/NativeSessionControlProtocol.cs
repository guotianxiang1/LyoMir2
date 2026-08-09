using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeAccountDisconnectRequest
    {
        public byte[] Account { get; init; } = Array.Empty<byte>();
    }

    public sealed class NativeSessionPlayStateRequest
    {
        public ushort State { get; init; }
        public long UserId { get; init; }
    }

    public static class NativeSessionControlProtocol
    {
        public const ushort DisconnectAccountCommand = 0x0045;
        public const ushort SetPlayStateCommand = 0x019E;
        public const ushort SetPlayStateResponseCommand = 0x013D;
        public const int HeaderSize = 0x48;

        public static bool TryDecodeDisconnect(LegacyDbServerFrame frame,
            out NativeAccountDisconnectRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (!HasHeader(frame, DisconnectAccountCommand))
            {
                error = "native 0x0045 envelope is invalid";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (!TryReadShortString(payload, 0x10, 20,
                    out var account, out error))
                return false;
            request = new NativeAccountDisconnectRequest { Account = account };
            return true;
        }

        public static bool TryDecodePlayState(LegacyDbServerFrame frame,
            out NativeSessionPlayStateRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (!HasHeader(frame, SetPlayStateCommand))
            {
                error = "native 0x019E envelope is invalid";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            request = new NativeSessionPlayStateRequest
            {
                State = BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.Slice(2, 2)),
                UserId = BinaryPrimitives.ReadInt64LittleEndian(
                    payload.Slice(8, 8))
            };
            return true;
        }

        public static LegacyDbServerFrame CreatePlayStateResponse(
            NativeSessionPlayStateRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                SetPlayStateResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                request.State);
            BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(8, 8),
                request.UserId);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        private static bool HasHeader(LegacyDbServerFrame frame,
            ushort command) => frame != null && frame.Type == 1
                               && frame.Payload.Length >= HeaderSize
                               && BinaryPrimitives.ReadUInt16LittleEndian(
                                   frame.Payload) == command;

        private static bool TryReadShortString(ReadOnlySpan<byte> source,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;
            var length = source[offset];
            if (length > capacity)
            {
                error = "native 0x0045 account ShortString is invalid";
                return false;
            }
            value = source.Slice(offset + 1, length).ToArray();
            return true;
        }
    }
}
