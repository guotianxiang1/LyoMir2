using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    /// <summary>
    /// The two 战神 LoginGate report sub-types carried by type1 0x0192 / 0x0193.
    /// The record's <c>[+4]</c> word distinguishes them (1 and 2).
    /// </summary>
    public enum NativeGateReportKind
    {
        None = 0,
        /// <summary>type1 0x0192 → LoginGate cmd 0x7DF, 0xF3-byte record, [+4]=1.</summary>
        Type1 = 1,
        /// <summary>type1 0x0193 → LoginGate cmd 0x7E0, 0x121-byte record, [+4]=2.</summary>
        Type2 = 2,
    }

    public sealed class NativeGateReportRequest
    {
        public NativeGateReportKind Kind { get; init; }
        /// <summary>ShortString at header+0x25.</summary>
        public byte[] Slot25 { get; init; } = Array.Empty<byte>();
        /// <summary>ShortString at header+0x10 — the session lookup key.</summary>
        public byte[] LookupName { get; init; } = Array.Empty<byte>();
        /// <summary>The tail bytes; the gate requires an exact length per sub-type.</summary>
        public byte[] Tail { get; init; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Native type1 commands 0x0192 and 0x0193, reversed byte-for-byte from the
    /// 战神 DBServer (handlers 0x5993A4 and 0x5993F6).
    ///
    /// Each begins with what looks like a magic constant —
    /// <c>cmp dword [ebp+8], 0x87</c> and <c>cmp dword [ebp+8], 0xB5</c> — but
    /// the dispatcher's caller sets <c>[ebp+8]</c> to the TAIL LENGTH
    /// (0x59DDC9 pushes it after <c>sub …,0x48</c> at 0x59DDBB), so these are
    /// exact fixed-size record gates, not opaque magic. Mismatches jump to the
    /// common exit 0x59953D with no reply.
    ///
    /// Both then read header+0x25 and header+0x10, look the name up in the
    /// GameGate session table on [0x5D9C4C] (0x5A1ACC / 0x5A1B4C → 0x40AEF4 key
    /// fold → 0x49BAA8 hash), and on a hit build a fixed record that goes to
    /// LoginGate via 0x5D18B0:
    ///   0x0192 → 0x5CFD50: 0xF3 bytes, [0]=4, [+4]w=1, cmd 0x7DF
    ///   0x0193 → 0x5CFE74: 0x121 bytes, [0]=4, [+4]w=2, cmd 0x7E0
    /// Cross-check: 0xF3 − 0x87 == 0x121 − 0xB5 == 0x6C, the same record/tail
    /// delta for both sub-types.
    ///
    /// Evidence: staging/dbsvr_type1_dispatch_census_20260803.md §3.
    /// </summary>
    public static class NativeGateReportProtocol
    {
        public const ushort Type1RequestCommand = 0x0192;
        public const ushort Type2RequestCommand = 0x0193;

        /// <summary>LoginGate frame command for 0x0192 (0x5CFE64 `mov dx,0x7DF`).</summary>
        public const ushort Type1LoginGateCommand = 0x07DF;
        /// <summary>LoginGate frame command for 0x0193 (0x5CFFA9 `mov dx,0x7E0`).</summary>
        public const ushort Type2LoginGateCommand = 0x07E0;

        /// <summary>0x59DDAC: the 0x48-byte type1 header.</summary>
        public const int HeaderSize = 0x48;
        /// <summary>0x5993A4: `cmp dword [ebp+8], 0x87` — exact tail length.</summary>
        public const int Type1TailLength = 0x87;
        /// <summary>0x5993F6: `cmp dword [ebp+8], 0xB5` — exact tail length.</summary>
        public const int Type2TailLength = 0xB5;
        /// <summary>0x5CFD67: `mov edx,0xF3` before the record is cleared.</summary>
        public const int Type1RecordSize = 0xF3;
        /// <summary>0x5CFE8B: `mov edx,0x121` before the record is cleared.</summary>
        public const int Type2RecordSize = 0x121;
        /// <summary>0x5CFD71 / 0x5CFE95 write 4 into the record's first byte.</summary>
        public const byte RecordTag = 4;

        /// <summary>
        /// The 16-byte header the LoginGate sender 0x5D18B0 prepends:
        /// magic / ConnID=0 / SeqID=0 / CMD@0x0C / length@0x0E / payload@0x10.
        /// Note this differs from the 12-byte GameServer-side frame header.
        /// </summary>
        public const int LoginGateHeaderSize = 0x10;

        private const int LookupNameOffset = 0x10;
        private const int Slot25Offset = 0x25;
        private const int WideShortStringCapacity = 0x14;
        private const int ShortStringCapacity = 0x0F;

        public static ushort GetRequestCommand(NativeGateReportKind kind) =>
            kind == NativeGateReportKind.Type1
                ? Type1RequestCommand
                : Type2RequestCommand;

        public static ushort GetLoginGateCommand(NativeGateReportKind kind) =>
            kind == NativeGateReportKind.Type1
                ? Type1LoginGateCommand
                : Type2LoginGateCommand;

        public static int GetRequiredTailLength(NativeGateReportKind kind) =>
            kind == NativeGateReportKind.Type1
                ? Type1TailLength
                : Type2TailLength;

        public static int GetRecordSize(NativeGateReportKind kind) =>
            kind == NativeGateReportKind.Type1
                ? Type1RecordSize
                : Type2RecordSize;

        public static bool TryDecodeRequest(LegacyDbServerFrame frame,
            out NativeGateReportRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native gate-report frame is null";
                return false;
            }
            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length < HeaderSize)
            {
                error = "native gate-report payload is truncated";
                return false;
            }

            var command = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            NativeGateReportKind kind;
            if (command == Type1RequestCommand) kind = NativeGateReportKind.Type1;
            else if (command == Type2RequestCommand) kind = NativeGateReportKind.Type2;
            else
            {
                error = "native gate-report command mismatch";
                return false;
            }

            // The gate is an EXACT length test (jne → 0x59953D, silent exit).
            var tailLength = payload.Length - HeaderSize;
            var required = GetRequiredTailLength(kind);
            if (tailLength != required)
            {
                error = $"native gate-report tail length {tailLength} "
                        + $"!= required 0x{required:X2}";
                return false;
            }

            if (!TryReadShortString(payload, LookupNameOffset,
                    WideShortStringCapacity, out var lookupName, out error)
                || !TryReadShortString(payload, Slot25Offset,
                    ShortStringCapacity, out var slot25, out error))
                return false;

            request = new NativeGateReportRequest
            {
                Kind = kind,
                LookupName = lookupName,
                Slot25 = slot25,
                Tail = payload.AsSpan(HeaderSize).ToArray(),
            };
            return true;
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> payload,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = Array.Empty<byte>();
            error = string.Empty;
            if (offset >= payload.Length) return true;
            var length = payload[offset];
            if (length > capacity)
            {
                error = $"native gate-report ShortString at 0x{offset:X2} "
                        + $"exceeds {capacity} bytes";
                return false;
            }
            if (offset + 1 + length > payload.Length)
            {
                error = $"native gate-report ShortString at 0x{offset:X2} "
                        + "runs past the header";
                return false;
            }
            value = payload.Slice(offset + 1, length).ToArray();
            return true;
        }
    }
}
