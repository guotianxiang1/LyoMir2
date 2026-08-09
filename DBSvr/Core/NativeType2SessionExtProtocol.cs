using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeType2SessionExtRequest
    {
        /// <summary>dword at record+0x14 — the single int identity key
        /// (0x5AEEE8 hashes it into THumanDBManager's [self+0xC] table).</summary>
        public int Identity { get; init; }
        /// <summary>dword at record+0x18 — a second value echoed in the ack.</summary>
        public int Cookie { get; init; }
        /// <summary>The record blob the original copies into THumanInfo+0x7C
        /// (0x5AD2E1: length = the record's own first dword).</summary>
        public byte[] Blob { get; init; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Native TYPE_B (type2) command 0x0177, reversed byte-for-byte from the 战神
    /// DBServer (dispatcher <c>sub_599860</c>, jump at <c>je 0x599AEE</c>).
    ///
    /// Handler 0x599AEE looks the human up in <c>THumanDBManager</c> ([0x5D9EBC])
    /// via the gate 0x5AD298 → 0x5ABC3C → 0x5AEEE8, keyed by the int at record+0x14.
    /// On a hit it frees the human's old <c>obj+0x7C</c> blob, copies the record
    /// (length = the record's first dword) into a fresh <c>obj+0x7C</c>, and calls
    /// 0x59C8E8 to acknowledge with a 0x54-byte frame carrying body command
    /// <c>0x13A</c> and the two dwords record+0x14 / record+0x18 at body+8 / body+0xC.
    /// On a miss it does nothing (no store, no ack) — <c>je 0x599CAC</c>.
    ///
    /// This is a runtime session-extension blob store: <c>obj+0x7C</c> is an
    /// in-memory field of a loaded THumanInfo, not a MySQL column. Evidence:
    /// staging/dbsvr_type2_type3_dispatch_census_20260803.md.
    /// </summary>
    public static class NativeType2SessionExtProtocol
    {
        public const ushort RequestCommand = 0x0177;
        /// <summary>0x599C44-style `mov word [body], 0x13A` in 0x59C8E8.</summary>
        public const ushort ResponseCommand = 0x013A;

        /// <summary>0x59C8F1: `mov [ebp-0xC], 0x54` — the ack frame is 0x54 bytes.</summary>
        public const int AckTotalLength = 0x54;
        /// <summary>0x59C931: `mov [buf+8], 0x48` — the ack payload is 0x48 bytes.</summary>
        public const int AckPayloadLength = 0x48;

        private const int WireHeaderSize = LegacyDbServerFrameCodec.HeaderSize;
        private const int IdentityOffset = 0x14;
        private const int CookieOffset = 0x18;
        private const int MinimumRecordLength = 0x1C;

        public static bool TryDecodeRequest(LegacyDbServerFrame frame,
            out NativeType2SessionExtRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native type2 0177 frame is null";
                return false;
            }
            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length < 2
                || BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != RequestCommand)
            {
                error = "native type2 0177 command mismatch";
                return false;
            }
            if (payload.Length < MinimumRecordLength)
            {
                error = "native type2 0177 record shorter than 0x1C";
                return false;
            }

            request = new NativeType2SessionExtRequest
            {
                Identity = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(IdentityOffset, 4)),
                Cookie = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(CookieOffset, 4)),
                Blob = (byte[])payload.Clone(),
            };
            return true;
        }

        /// <summary>
        /// The acknowledgement built by 0x59C8E8: a 0x54-byte type1 frame with a
        /// 0x48-byte payload, body command 0x13A at payload+0, and the request's
        /// two identity dwords at payload+8 and payload+0xC (0x59C94F / 0x59C955).
        /// </summary>
        public static LegacyDbServerFrame CreateAck(
            NativeType2SessionExtRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = new byte[AckTotalLength - WireHeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ResponseCommand);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4),
                request.Identity);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0xC, 4),
                request.Cookie);
            return new LegacyDbServerFrame(1, 0, payload);
        }
    }
}
