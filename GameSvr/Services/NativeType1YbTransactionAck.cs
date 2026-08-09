using System.Buffers.Binary;
using SystemModule.Packet;

namespace GameSvr.Services
{
    /// <summary>
    /// Native DB Type1 0x0060/0x0061 completion forwarded to the YBDB link.
    /// </summary>
    public static class NativeType1YbTransactionAck
    {
        public const ushort BagInjectionResponseCommand = 0x0060;
        public const ushort AwardPlayerResponseCommand = 0x0061;
        public const int HeaderSize = 0x48;
        public const int YbDbQueryId = 0x0468;
        public const ushort SuccessIdent = 105;
        public const ushort FailureIdent = 106;

        public static bool TryCreateForwardFrame(LegacyDbServerFrame frame,
            out YbDbLegacy77Frame forward)
        {
            forward = null;
            if (frame == null || frame.Type != 1 || frame.Payload == null
                || frame.Payload.Length < HeaderSize)
                return false;

            var payload = frame.Payload.AsSpan();
            var command = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            if (command is not BagInjectionResponseCommand
                and not AwardPlayerResponseCommand)
                return false;

            var success = BinaryPrimitives.ReadUInt16LittleEndian(
                payload.Slice(2, 2)) == 1;
            var correlation = BinaryPrimitives.ReadInt32LittleEndian(
                payload.Slice(4, 4));
            forward = new YbDbLegacy77Frame(YbDbQueryId, correlation,
                success ? SuccessIdent : FailureIdent, Array.Empty<byte>());
            return true;
        }

        public static bool TryProcessResponse(LegacyDbServerFrame frame) =>
            TryProcessResponse(frame,
                YbDbClient.Instance.EnqueueNativeDbTransactionAck);

        public static bool TryProcessResponse(LegacyDbServerFrame frame,
            Func<YbDbLegacy77Frame, bool> enqueue)
        {
            if (enqueue == null) throw new ArgumentNullException(nameof(enqueue));
            return TryCreateForwardFrame(frame, out var forward)
                   && enqueue(forward);
        }
    }
}
