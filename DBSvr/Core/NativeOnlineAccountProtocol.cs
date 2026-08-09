using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeOnlineAccountTextRequest
    {
        public byte[] Account { get; init; } = Array.Empty<byte>();
        public string Text { get; init; } = string.Empty;
    }

    public sealed class NativeOnlineAccountTimeRequest
    {
        public byte[] Account { get; init; } = Array.Empty<byte>();
        public ushort Flag { get; init; }
    }

    public static class NativeOnlineAccountProtocol
    {
        public const ushort SetTextCommand = 0x019C;
        public const ushort SetLoginTimeCommand = 0x019D;
        public const int HeaderSize = 0x48;

        public static bool TryDecodeText(LegacyDbServerFrame frame,
            out NativeOnlineAccountTextRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (!HasHeader(frame, SetTextCommand))
            {
                error = "native 0x019C envelope is invalid";
                return false;
            }
            if (!TryReadShortString(frame.Payload, 0x10, 20,
                    out var account, out error)
                || !TryReadShortString(frame.Payload, 0x25, 15,
                    out var textBytes, out error))
                return false;
            try
            {
                request = new NativeOnlineAccountTextRequest
                {
                    Account = account,
                    Text = LegacyGbkText.Decode(textBytes)
                };
                return true;
            }
            catch (ArgumentException ex)
            {
                error = "native 0x019C text is not valid GBK: " + ex.Message;
                return false;
            }
        }

        public static bool TryDecodeLoginTime(LegacyDbServerFrame frame,
            out NativeOnlineAccountTimeRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (!HasHeader(frame, SetLoginTimeCommand))
            {
                error = "native 0x019D envelope is invalid";
                return false;
            }
            if (!TryReadShortString(frame.Payload, 0x10, 20,
                    out var account, out error))
                return false;
            request = new NativeOnlineAccountTimeRequest
            {
                Account = account,
                Flag = BinaryPrimitives.ReadUInt16LittleEndian(
                    frame.Payload.AsSpan(2, 2))
            };
            return true;
        }

        public static bool IsAccountMatch(ReadOnlySpan<byte> requestAccount,
            string onlineAccount)
        {
            if (requestAccount.Length == 0 || string.IsNullOrEmpty(onlineAccount))
                return false;
            try
            {
                return NativeType3Protocol.NormalizePtidKey(requestAccount)
                    == NativeType3Protocol.NormalizePtidKey(
                        LegacyGbkText.Encode(onlineAccount));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public static long CreateLoginDateTimeBits(ushort flag,
            Func<DateTime> nowProvider = null)
        {
            if (flag != 0) return 0;
            var now = (nowProvider ?? (() => DateTime.Now))();
            return BitConverter.DoubleToInt64Bits(now.ToOADate());
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
            if (offset < 0 || offset >= source.Length)
            {
                error = "native online-account ShortString is truncated";
                return false;
            }
            var length = source[offset];
            if (length > capacity || offset + 1 + length > source.Length)
            {
                error = "native online-account ShortString is invalid";
                return false;
            }
            value = source.Slice(offset + 1, length).ToArray();
            return true;
        }
    }
}
