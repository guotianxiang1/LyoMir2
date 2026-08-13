using System;
using System.Buffers.Binary;
using System.Text;

namespace SystemModule.Packet
{
    /// <summary>
    /// Codec for the legacy 16-byte 77 control frame used by YBDB and the
    /// original DBServer's 5100/5600 links. Business payloads remain separate.
    ///
    /// On the LoginGate link this frame is the Delphi record TServerMessage
    /// (LG source uTypes.pas:124-131). The property names here do NOT line up
    /// with the Delphi field names, and two of them are outright swapped:
    ///
    ///   +0  Sign: Cardinal        -> FrameMagic  ($33AABB77, uTypes.pas:70)
    ///   +4  rSocketHandle: integer-> QueryId
    ///   +8  Ident: integer        -> Param    &lt;-- Delphi "Ident" is Param here
    ///   +12 Cmd: Word             -> Ident    &lt;-- Delphi "Cmd" is Ident here
    ///   +14 DataLength: Word      -> payload length
    ///
    /// The byte layout is correct; only the names diverge. Do not "fix" the
    /// names by moving values between fields.
    ///
    /// Param (+8) is not dead weight: uPigListen.pas:197-199 and
    /// uDBListen.pas:111 put LM_PIG_MULTI_MSG/222/224 there while Cmd stays
    /// GDM_PIG_MESSAGE (1002), so a PIG implementation has to populate it.
    /// </summary>
    public static class YbDbLegacy77Codec
    {
        public const uint FrameMagic = 0x33AABB77;
        public const int HeaderSize = 16;
        public const int MaximumFrameLength = 0x8000;
        public const int MaximumPayloadLength = MaximumFrameLength - HeaderSize;

        public const int IdentitySize = 64;
        public const int IdentityField0Offset = 0;
        public const int IdentityField0Capacity = 10;
        public const int IdentityField11Offset = 11;
        public const int IdentityField11Capacity = 20;
        public const int IdentityRoleNameOffset = 32;
        public const int IdentityRoleNameCapacity = 15;
        public const int IdentityField48Offset = 48;
        public const int IdentityField48Capacity = 15;

        private static readonly Encoding Gbk;

        static YbDbLegacy77Codec()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        public static bool TryEncode(YbDbLegacy77Frame frame,
            out byte[] data, out string error)
        {
            data = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "legacy YBDB frame is null";
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length > MaximumPayloadLength)
            {
                error = $"legacy YBDB payload exceeds {MaximumPayloadLength} bytes";
                return false;
            }

            data = new byte[HeaderSize + payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), FrameMagic);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4, 4), frame.QueryId);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8, 4), frame.Param);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12, 2), frame.Ident);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14, 2),
                (ushort)payload.Length);
            payload.CopyTo(data, HeaderSize);
            return true;
        }

        public static bool TryDecode(ReadOnlySpan<byte> data,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (data.Length < HeaderSize)
            {
                error = "legacy YBDB frame is truncated";
                return false;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4)) != FrameMagic)
            {
                error = "legacy YBDB frame magic mismatch";
                return false;
            }

            var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14, 2));
            var frameLength = HeaderSize + payloadLength;
            if (frameLength > MaximumFrameLength)
            {
                error = $"legacy YBDB frame exceeds {MaximumFrameLength} bytes";
                return false;
            }
            if (data.Length != frameLength)
            {
                error = "legacy YBDB frame payload length mismatch";
                return false;
            }

            frame = new YbDbLegacy77Frame(
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2)),
                payloadLength == 0
                    ? Array.Empty<byte>()
                    : data.Slice(HeaderSize, payloadLength).ToArray());
            return true;
        }

        public static bool TryEncodeIdentity(YbDbLegacy77Identity identity,
            out byte[] data, out string error)
        {
            data = null;
            error = string.Empty;
            if (identity == null)
            {
                error = "legacy YBDB identity is null";
                return false;
            }

            data = new byte[IdentitySize];
            if (TryWriteShortString(data, IdentityField0Offset,
                    IdentityField0Capacity, identity.Field0, out error)
                && TryWriteShortString(data, IdentityField11Offset,
                    IdentityField11Capacity, identity.Field11, out error)
                && TryWriteShortString(data, IdentityRoleNameOffset,
                    IdentityRoleNameCapacity, identity.RoleName, out error)
                && TryWriteShortString(data, IdentityField48Offset,
                    IdentityField48Capacity, identity.Field48, out error))
            {
                return true;
            }

            data = null;
            return false;
        }

        // Native ShortString copies CP936 bytes first, then truncates by slot bytes.
        public static bool TryEncodeNativeIdentity(YbDbLegacy77Identity identity,
            out byte[] data, out string error)
        {
            data = null;
            error = string.Empty;
            if (identity == null)
            {
                error = "legacy YBDB identity is null";
                return false;
            }

            data = new byte[IdentitySize];
            if (TryWriteNativeShortString(data, IdentityField0Offset,
                    IdentityField0Capacity, identity.Field0, out error)
                && TryWriteNativeShortString(data, IdentityField11Offset,
                    IdentityField11Capacity, identity.Field11, out error)
                && TryWriteNativeShortString(data, IdentityRoleNameOffset,
                    IdentityRoleNameCapacity, identity.RoleName, out error)
                && TryWriteNativeShortString(data, IdentityField48Offset,
                    IdentityField48Capacity, identity.Field48, out error))
            {
                return true;
            }

            data = null;
            return false;
        }

        public static bool TryDecodeIdentity(ReadOnlySpan<byte> data,
            out YbDbLegacy77Identity identity, out string error)
        {
            identity = null;
            error = string.Empty;
            if (data.Length != IdentitySize)
            {
                error = $"legacy YBDB identity length must be {IdentitySize} bytes";
                return false;
            }

            try
            {
                identity = new YbDbLegacy77Identity
                {
                    Field0 = ReadShortString(data, IdentityField0Offset,
                        IdentityField0Capacity),
                    Field11 = ReadShortString(data, IdentityField11Offset,
                        IdentityField11Capacity),
                    RoleName = ReadShortString(data, IdentityRoleNameOffset,
                        IdentityRoleNameCapacity),
                    Field48 = ReadShortString(data, IdentityField48Offset,
                        IdentityField48Capacity)
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException
                                       || ex is DecoderFallbackException)
            {
                error = "invalid legacy YBDB identity: " + ex.Message;
                identity = null;
                return false;
            }
        }

        public static bool TryDecodeShortString(ReadOnlySpan<byte> data, int offset,
            int maximumLength, out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;
            if (offset < 0 || maximumLength < 0 || offset >= data.Length
                || maximumLength > data.Length - offset - 1)
            {
                error = "legacy YBDB short string slot is outside the payload";
                return false;
            }

            try
            {
                value = ReadShortString(data, offset, maximumLength);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException
                                       || ex is DecoderFallbackException)
            {
                error = "invalid legacy YBDB short string: " + ex.Message;
                value = string.Empty;
                return false;
            }
        }

        private static bool TryWriteShortString(Span<byte> destination, int offset,
            int maximumLength, string value, out string error)
        {
            error = string.Empty;
            byte[] bytes;
            try
            {
                bytes = Gbk.GetBytes(value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = "legacy YBDB string is not GBK: " + ex.Message;
                return false;
            }
            if (bytes.Length > maximumLength)
            {
                error = $"legacy YBDB string exceeds {maximumLength} GBK bytes";
                return false;
            }

            destination.Slice(offset, maximumLength + 1).Clear();
            destination[offset] = (byte)bytes.Length;
            bytes.CopyTo(destination.Slice(offset + 1));
            return true;
        }

        private static bool TryWriteNativeShortString(Span<byte> destination, int offset,
            int maximumLength, string value, out string error)
        {
            error = string.Empty;
            byte[] bytes;
            try
            {
                bytes = Gbk.GetBytes(value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = "legacy YBDB string is not GBK: " + ex.Message;
                return false;
            }

            var length = Math.Min(bytes.Length, maximumLength);
            destination.Slice(offset, maximumLength + 1).Clear();
            destination[offset] = (byte)length;
            bytes.AsSpan(0, length).CopyTo(destination.Slice(offset + 1));
            return true;
        }

        private static string ReadShortString(ReadOnlySpan<byte> data, int offset,
            int maximumLength)
        {
            var length = data[offset];
            if (length > maximumLength)
                throw new ArgumentException(
                    $"short string length {length} exceeds {maximumLength} at 0x{offset:X}");
            return Gbk.GetString(data.Slice(offset + 1, length).ToArray());
        }
    }

    public sealed class YbDbLegacy77Frame
    {
        public YbDbLegacy77Frame(int queryId, int param, ushort ident, byte[] payload)
        {
            QueryId = queryId;
            Param = param;
            Ident = ident;
            Payload = payload ?? Array.Empty<byte>();
        }

        public int QueryId { get; }
        public int Param { get; }
        public ushort Ident { get; }
        public byte[] Payload { get; }
    }

    public sealed class YbDbLegacy77Identity
    {
        // Field0 and Field11 are the same PTID with native 10/20-byte capacities.
        // Offset names remain explicit because they describe the fixed wire layout.
        public string Field0 { get; set; } = string.Empty;
        public string Field11 { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string Field48 { get; set; } = string.Empty;
    }
}
