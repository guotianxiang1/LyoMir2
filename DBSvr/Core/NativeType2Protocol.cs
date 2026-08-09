using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public static class NativeType2Protocol
    {
        public const int HeaderSize = 12;
        public const ushort HeartbeatCommand = 0x003C;
        public const ushort RegisterCommand = 0x003D;
        public const ushort RankingReloadCommand = 0x003E;
        public const ushort RelayCommand = 0x003F;
        public const ushort LoginGateControlCommand = 0x0042;
        public const ushort WhitelistReloadCommand = 0x0184;
        public const ushort ResetAllTransferLocksCommand = 0x0185;
        public const ushort ResetTransferLockCommand = 0x0186;
        public const ushort SetVipYbConsumeCommand = 0x0191;
        public const ushort WhitelistReloadResponseCommand = 0x0132;
        public const ushort RelayResponseCommand = 0x006F;
        public const int WhitelistReloadResponseBaseSize = 0x48;
        private const int WhitelistReloadNameOffset = 0x25;
        private const int WhitelistReloadNameCapacity = 15;
        private static readonly byte[] WhitelistReloadSuccessText =
            Convert.FromHexString(
                "57686974654C6973742E747874BCD3D4D8B3C9B9A6A3A1");

        public static bool IsSilentNoOpCommand(ushort command) => command is
            // These entries share the original Type2 dispatcher default target
            // at 0x599C7D and return without a response.
            0x0181 or 0x0182 or 0x0183
            or 0x0188 or 0x0189 or 0x018A or 0x018B
            or 0x018C or 0x018D or 0x018E or 0x018F or 0x0190;

        public static bool TryDecode(LegacyDbServerFrame frame,
            out NativeType2Message message, out string error)
        {
            message = null;
            error = string.Empty;
            if (frame == null || frame.Type != 2)
            {
                error = "native type2 envelope is invalid";
                return false;
            }
            if (frame.Payload.Length < HeaderSize)
            {
                error = "native type2 payload is shorter than 12 bytes";
                return false;
            }

            var payload = frame.Payload.AsSpan();
            message = new NativeType2Message
            {
                Command = BinaryPrimitives.ReadUInt16LittleEndian(payload),
                Word2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2)),
                Param1 = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4)),
                Param2 = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)),
                Suffix = payload.Slice(HeaderSize).ToArray()
            };
            return true;
        }

        public static bool TryGetRegistrationServerType(
            NativeType2Message message, out byte serverType)
        {
            serverType = 0;
            if (message == null || message.Command != RegisterCommand
                                || message.Param2 <= 0)
                return false;
            serverType = unchecked((byte)message.Param2);
            return true;
        }

        public static bool TryCreateRelayFrame(NativeType2Message request,
            byte senderType, out LegacyDbServerFrame response, out byte targetType,
            out string error)
        {
            response = null;
            targetType = 0;
            error = string.Empty;
            if (request == null || request.Command != RelayCommand)
            {
                error = "native type2 message is not a relay request";
                return false;
            }

            targetType = unchecked((byte)request.Param1);
            var suffix = request.Suffix ?? Array.Empty<byte>();
            var payload = new byte[HeaderSize + suffix.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, RelayResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), request.Word2);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), senderType);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), request.Param2);
            suffix.CopyTo(payload, HeaderSize);
            response = new LegacyDbServerFrame(2, 0, payload);
            return true;
        }

        public static bool TryGetLoginGateControlEnabled(
            NativeType2Message message, out bool enabled)
        {
            enabled = false;
            if (message == null || message.Command != LoginGateControlCommand)
                return false;
            enabled = message.Param1 == 1;
            return true;
        }

        public static bool ShouldReloadWhiteLists(NativeType2Message message) =>
            message != null && message.Command == WhitelistReloadCommand
                            && message.Param1 == 0;

        public static bool TryCreateWhitelistReloadResponse(
            NativeType2Message request, out LegacyDbServerFrame response,
            out string error)
        {
            response = null;
            error = string.Empty;
            if (!ShouldReloadWhiteLists(request))
            {
                error = "native type2 message is not a whitelist reload request";
                return false;
            }

            var payload = new byte[WhitelistReloadResponseBaseSize
                                   + WhitelistReloadSuccessText.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                payload, WhitelistReloadResponseCommand);
            var suffix = request.Suffix ?? Array.Empty<byte>();
            var nameLength = Math.Min(WhitelistReloadNameCapacity, suffix.Length);
            payload[WhitelistReloadNameOffset] = (byte)nameLength;
            suffix.AsSpan(0, nameLength).CopyTo(
                payload.AsSpan(WhitelistReloadNameOffset + 1));
            WhitelistReloadSuccessText.CopyTo(
                payload, WhitelistReloadResponseBaseSize);
            response = new LegacyDbServerFrame(1, 0, payload);
            return true;
        }

        public static bool ShouldRelay(byte senderType, byte peerType, byte targetType)
        {
            if (peerType == 9) return false;
            return targetType == 0 ? peerType != senderType : peerType == targetType;
        }
    }

    public sealed class NativeType2Message
    {
        public ushort Command { get; init; }
        public ushort Word2 { get; init; }
        public int Param1 { get; init; }
        public int Param2 { get; init; }
        public byte[] Suffix { get; init; } = Array.Empty<byte>();
    }
}
