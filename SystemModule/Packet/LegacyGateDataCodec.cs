using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Raw 12-byte message payload carried by the original DBServer's 5100
    /// Ident=4/14 frames.
    /// </summary>
    public static class LegacyGateDataCodec
    {
        public const ushort RequestIdent = 4;
        public const ushort ResponseIdent = 14;
        public const int MessageHeaderSize = 12;

        public static bool TryDecodeRequest(YbDbLegacy77Frame frame,
            out LegacyGateDataMessage message, out string error)
        {
            message = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "legacy gate frame is null";
                return false;
            }
            if (frame.Ident != RequestIdent)
            {
                error = $"legacy gate data request ident must be {RequestIdent}";
                return false;
            }
            if (frame.Payload.Length < MessageHeaderSize)
            {
                error = $"legacy gate data payload is shorter than {MessageHeaderSize} bytes";
                return false;
            }

            var payload = frame.Payload.AsSpan();

            // 原版 fn_5CDFxx 的 payload 边界，逐字：
            //   0x5CDFB2  cmp dword [ebp-0xc], 0xc / 0x5CDFB6 jl  -> len < 12 拒绝
            //   0x5CDFC2  mov eax,[ebp-0xc]
            //   0x5CDFC5  sub eax, 0xc          ; len - 12
            //   0x5CDFC8  dec eax               ; ★再减 1 => payloadLen = len - 13
            //   0x5CDFCC  cmp [ebp-0x18], 0 / 0x5CDFD0 jle 0x5CDFDD
            //   0x5CDFD5  add eax, 0xc          ; ptr = msg + 0xC
            //   0x5CDFDD  xor eax,eax (x2)      ; 否则 ptr = NULL 且 len = 0
            // ⇒ payload 指针非空**当且仅当 len >= 14**。
            //
            // 那个 `dec eax` 剥掉的是**串尾 NUL 终止符**。判据来自
            // DbSvrServiceRegressionCheck 里的真实抓包：payloadLength = 0x11 = 17，
            // body 为 5 字节 `C1 FA C9 F1 00` —— 末字节正是 NUL。
            // 按原版公式 17-13 = 4 ⇒ `C1 FA C9 F1`，恰好不含终止符。
            //
            // ⚠️ 此前这里用 `payload.Length == MessageHeaderSize` 判空、且整段尾巴
            // 都当 body，比原版**多带一个字节**，且 len == 13 时原版给空 body、
            // C# 给 1 字节。两处都已按字节修正。
            //
            // 附：原版部分子命令（grp2/4/5/6）把 payload 送进 0x404DF0
            // （逐 4 字节 strlen 后 jmp 0x404CE8 LStrFromBuf），串长由**首个 NUL**
            // 决定、payloadLen 不参与 —— 那些路径对本差异不敏感。但按 payloadLen
            // 直接取字节的路径是敏感的，所以必须照原版算。
            var bodyLength = payload.Length - MessageHeaderSize - 1;
            message = new LegacyGateDataMessage(
                BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2)),
                bodyLength <= 0
                    ? Array.Empty<byte>()
                    : payload.Slice(MessageHeaderSize, bodyLength).ToArray());
            return true;
        }

        public static YbDbLegacy77Frame CreateResponse(int queryId, int recog,
            ushort ident, ushort param, ushort tag, ushort series, byte[] body)
        {
            body ??= Array.Empty<byte>();
            var payload = new byte[MessageHeaderSize + body.Length];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), recog);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), ident);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), param);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), series);
            body.CopyTo(payload, MessageHeaderSize);
            return new YbDbLegacy77Frame(queryId, 0, ResponseIdent, payload);
        }
    }

    public sealed class LegacyGateDataMessage
    {
        public LegacyGateDataMessage(int recog, ushort ident, ushort param,
            ushort tag, ushort series, byte[] body)
        {
            Recog = recog;
            Ident = ident;
            Param = param;
            Tag = tag;
            Series = series;
            Body = body ?? Array.Empty<byte>();
        }

        public int Recog { get; }
        public ushort Ident { get; }
        public ushort Param { get; }
        public ushort Tag { get; }
        public ushort Series { get; }
        public byte[] Body { get; }
    }
}
