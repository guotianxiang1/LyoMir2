using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeSessionLookupRequest
    {
        /// <summary>ShortString at header+0x10 — the lookup key (case-insensitive).</summary>
        public byte[] LookupName { get; init; } = Array.Empty<byte>();
        /// <summary>ShortString at header+0x25.</summary>
        public byte[] Slot25 { get; init; } = Array.Empty<byte>();
        /// <summary>ShortString at header+0x35.</summary>
        public byte[] Slot35 { get; init; } = Array.Empty<byte>();
        /// <summary>word at header+0x02, passed to both the gate and the reply.</summary>
        public ushort Selector { get; init; }
    }

    /// <summary>
    /// Native type1 command 0x0151, reversed byte-for-byte from the 战神 DBServer
    /// handler at 0x598C1B.
    ///
    /// The handler reads three ShortStrings and a word from the HEADER base
    /// (dispatcher [ebp-0x10], which 0x5988D8 copies from [ebp-8]), calls the
    /// GameGate-session lookup gate 0x5A1A40 on [0x5D9C4C], and:
    ///   gate returns true  → it already reported to LoginGate (cmd 0x7DC via
    ///                        0x5CF968) and the handler exits at 0x59953D;
    ///   gate returns false → it answers the requesting GameServer with the
    ///                        0x54-byte reply built by 0x59A0FC.
    ///
    /// The gate key is lowercased by 0x40AEF4 (`cmp 0x41 / cmp 0x5A / add 0x20`)
    /// before the hash lookup 0x49BAA8 → 0x49B678, so matching is
    /// case-insensitive. Evidence: staging/dbsvr_type1_dispatch_census_20260803.md §3.
    /// </summary>
    public static class NativeSessionLookupProtocol
    {
        public const ushort RequestCommand = 0x0151;
        /// <summary>0x59A155 writes 0x54 as the reply body command word.</summary>
        public const ushort ResponseCommand = 0x0054;

        /// <summary>0x59DDAC: the 0x48-byte type1 header.</summary>
        public const int HeaderSize = 0x48;
        /// <summary>0x59A10F allocates 0x54 total; 0x59A142 sets payload 0x48.</summary>
        public const int ReplyTotalLength = 0x54;

        private const int WireHeaderSize = LegacyDbServerFrameCodec.HeaderSize;
        private const int SelectorOffset = 0x02;
        private const int LookupNameOffset = 0x10;
        private const int Slot25Offset = 0x25;
        private const int Slot35Offset = 0x35;
        private const int WideShortStringCapacity = 0x14;
        private const int ShortStringCapacity = 0x0F;

        public static bool TryDecodeRequest(LegacyDbServerFrame frame,
            out NativeSessionLookupRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native 0151 frame is null";
                return false;
            }
            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length < HeaderSize)
            {
                error = "native 0151 payload is truncated";
                return false;
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != RequestCommand)
            {
                error = "native 0151 command mismatch";
                return false;
            }

            if (!TryReadShortString(payload, LookupNameOffset,
                    WideShortStringCapacity, out var lookupName, out error)
                || !TryReadShortString(payload, Slot25Offset,
                    ShortStringCapacity, out var slot25, out error)
                || !TryReadShortString(payload, Slot35Offset,
                    ShortStringCapacity, out var slot35, out error))
                return false;

            request = new NativeSessionLookupRequest
            {
                LookupName = lookupName,
                Slot25 = slot25,
                Slot35 = slot35,
                Selector = BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.AsSpan(SelectorOffset, 2)),
            };
            return true;
        }

        /// <summary>
        /// Reply built by 0x59A0FC: 0x54 total / 0x48 payload / body command 0x54 /
        /// the request's word@+2 echoed at body+2 / the +0x10 lookup name as a
        /// 20-byte ShortString at body+0x10 / a 15-byte ShortString at body+0x25 /
        /// and body+4 set to 1 only when the boolean argument is non-zero
        /// (0x59A1B1 `cmp byte [ebp+8],0 / je`).
        /// </summary>
        public static LegacyDbServerFrame CreateResponse(
            NativeSessionLookupRequest request, byte[] secondName, bool flag)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = new byte[ReplyTotalLength - WireHeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                request.Selector);
            if (flag)
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 1);
            WriteShortString(payload, LookupNameOffset, WideShortStringCapacity,
                request.LookupName);
            WriteShortString(payload, Slot25Offset, ShortStringCapacity,
                secondName);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        /// <summary>
        /// Case-insensitive comparison matching the gate's key normalisation at
        /// 0x40AEF4, which folds only ASCII A-Z (`cmp 0x41` / `cmp 0x5A` /
        /// `add 0x20`) and leaves every other byte — including GBK lead/trail
        /// bytes — untouched. The same key function feeds the table insert at
        /// 0x5A14DF, so lookups and inserts agree.
        ///
        /// Native folds to lower case and the sibling
        /// <see cref="NativeForceLevelProtocol.NormalizeCharacterNameKey"/> folds
        /// to upper; both are ASCII-only folds, so they induce the same equality.
        /// </summary>
        public static bool KeyEquals(byte[] left, byte[] right)
        {
            var a = left ?? Array.Empty<byte>();
            var b = right ?? Array.Empty<byte>();
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (LowerAscii(a[i]) != LowerAscii(b[i])) return false;
            return true;
        }

        private static byte LowerAscii(byte value) =>
            value >= 0x41 && value <= 0x5A ? (byte)(value + 0x20) : value;

        private static bool TryReadShortString(ReadOnlySpan<byte> payload,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = Array.Empty<byte>();
            error = string.Empty;
            if (offset >= payload.Length) return true;
            var length = payload[offset];
            if (length > capacity)
            {
                error = $"native 0151 ShortString at 0x{offset:X2} "
                        + $"exceeds {capacity} bytes";
                return false;
            }
            if (offset + 1 + length > payload.Length)
            {
                error = $"native 0151 ShortString at 0x{offset:X2} "
                        + "runs past the header";
                return false;
            }
            value = payload.Slice(offset + 1, length).ToArray();
            return true;
        }

        private static void WriteShortString(byte[] payload, int offset,
            int capacity, byte[] value)
        {
            var source = value ?? Array.Empty<byte>();
            var length = Math.Min(source.Length, capacity);
            if (offset >= payload.Length) return;
            payload[offset] = (byte)length;
            if (length > 0 && offset + 1 + length <= payload.Length)
                source.AsSpan(0, length).CopyTo(payload.AsSpan(offset + 1));
        }
    }
}
