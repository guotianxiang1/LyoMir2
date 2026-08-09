using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeGlobalRelayRegistration
    {
        /// <summary>The sender's serverType byte (TServerInfo+0x40A0).</summary>
        public byte ServerType { get; init; }
        /// <summary>ShortString at header+0x10.</summary>
        public byte[] Name { get; init; } = Array.Empty<byte>();
        /// <summary>dword at header+0x04.</summary>
        public int Value { get; init; }
    }

    public sealed class NativeGlobalRelayQuery
    {
        /// <summary>ShortString at header+0x35.</summary>
        public byte[] Name { get; init; } = Array.Empty<byte>();
        /// <summary>Low word of the dword at header+0x04.</summary>
        public ushort Selector { get; init; }
        /// <summary>High word of the dword at header+0x04 (via 0x4080B0 = shr 16).</summary>
        public ushort Argument { get; init; }
        /// <summary>word at header+0x02.</summary>
        public ushort Tag { get; init; }
    }

    /// <summary>
    /// Native type1 commands 0x0156 and 0x0173, reversed byte-for-byte from the
    /// 战神 DBServer. Both are methods on the <c>TGlobalSocket</c> singleton
    /// ([0x5DA22C], class name recovered from its ctor 0x5A2EB4), i.e. the
    /// outbound link to the external global/cross-server service.
    ///
    /// 0x0156 (handler 0x598E03 → 0x5A3324): pushes the sender's serverType byte
    /// (TServerInfo+0x40A0), the header+0x10 ShortString, and the dword at
    /// header+0x04. 0x5A3339 then branches on <c>byte [Self+0x40]</c>:
    ///   link up   → build a 0x4C-byte record and send it straight out with
    ///               <c>[buf+4]w = 0x1F42</c> (8002) and payload length 0x40;
    ///   link down → queue command 0x274D (10061) onto [0x5D9EE8] via 0x5D1CF8.
    ///
    /// 0x0173 (handler 0x599231 → 0x5A3590): same packed dword split as 0x0174 —
    /// 0x5992xx-style <c>mov cx, word[hdr+4]</c> for the low half and
    /// <c>call 0x4080B0</c> (= <c>shr eax,0x10</c>) for the high half — plus the
    /// header+0x35 ShortString and <c>word[hdr+2]</c>. It calls 0x5A3450 twice,
    /// which queues command 0x2750 (10064) with a 0x41-byte payload.
    ///
    /// Neither command replies to the GameServer; both end at the shared exit
    /// 0x59953D.
    ///
    /// Evidence: staging/dbsvr_type1_dispatch_census_20260803.md §3之三.
    /// </summary>
    public static class NativeGlobalRelayProtocol
    {
        public const ushort RegistrationCommand = 0x0156;
        public const ushort QueryCommand = 0x0173;

        /// <summary>0x59DDAC: the 0x48-byte type1 header.</summary>
        public const int HeaderSize = 0x48;

        /// <summary>0x5A3359: `mov word [buf+4], 0x1F42` on the direct-send path.</summary>
        public const ushort DirectSendCommand = 0x1F42;
        /// <summary>0x5A335F: `mov dword [buf+8], 0x40` — direct payload length.</summary>
        public const int DirectSendPayloadLength = 0x40;
        /// <summary>0x5A3348: the direct record is 0x4C bytes total.</summary>
        public const int DirectRecordSize = 0x4C;

        /// <summary>0x5A3440: `mov dx, 0x274D` — queued command for 0x0156.</summary>
        public const ushort RegistrationQueueCommand = 0x274D;
        /// <summary>0x5A3481: `mov dx, 0x2750` — queued command for 0x0173.</summary>
        public const ushort QueryQueueCommand = 0x2750;
        /// <summary>0x5A346D: `push 0x41` — queued payload length for 0x0173.</summary>
        public const int QueryQueuePayloadLength = 0x41;

        /// <summary>0x5D1D08: the async queue node is 0x1C bytes.</summary>
        public const int QueueNodeSize = 0x1C;

        private const int ValueOffset = 0x04;
        private const int TagOffset = 0x02;
        private const int RegistrationNameOffset = 0x10;
        private const int QueryNameOffset = 0x35;
        private const int WideShortStringCapacity = 0x14;
        private const int ShortStringCapacity = 0x0F;

        public static bool TryDecodeRegistration(LegacyDbServerFrame frame,
            byte serverType, out NativeGlobalRelayRegistration request,
            out string error)
        {
            request = null;
            if (!TryValidate(frame, RegistrationCommand, out var payload,
                    out error))
                return false;
            if (!TryReadShortString(payload, RegistrationNameOffset,
                    WideShortStringCapacity, out var name, out error))
                return false;

            request = new NativeGlobalRelayRegistration
            {
                ServerType = serverType,
                Name = name,
                Value = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(ValueOffset, 4)),
            };
            return true;
        }

        public static bool TryDecodeQuery(LegacyDbServerFrame frame,
            out NativeGlobalRelayQuery request, out string error)
        {
            request = null;
            if (!TryValidate(frame, QueryCommand, out var payload, out error))
                return false;
            if (!TryReadShortString(payload, QueryNameOffset,
                    ShortStringCapacity, out var name, out error))
                return false;

            var packed = BinaryPrimitives.ReadUInt32LittleEndian(
                payload.AsSpan(ValueOffset, 4));
            request = new NativeGlobalRelayQuery
            {
                Name = name,
                Selector = unchecked((ushort)packed),
                Argument = unchecked((ushort)(packed >> 16)),
                Tag = BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.AsSpan(TagOffset, 2)),
            };
            return true;
        }

        private static bool TryValidate(LegacyDbServerFrame frame,
            ushort expectedCommand, out byte[] payload, out string error)
        {
            payload = Array.Empty<byte>();
            error = string.Empty;
            if (frame == null)
            {
                error = "native global-relay frame is null";
                return false;
            }
            payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length < HeaderSize)
            {
                error = "native global-relay payload is truncated";
                return false;
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != expectedCommand)
            {
                error = "native global-relay command mismatch";
                return false;
            }
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
                error = $"native global-relay ShortString at 0x{offset:X2} "
                        + $"exceeds {capacity} bytes";
                return false;
            }
            if (offset + 1 + length > payload.Length)
            {
                error = $"native global-relay ShortString at 0x{offset:X2} "
                        + "runs past the header";
                return false;
            }
            value = payload.Slice(offset + 1, length).ToArray();
            return true;
        }
    }
}
