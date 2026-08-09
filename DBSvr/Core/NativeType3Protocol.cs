using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public static class NativeType3Protocol
    {
        public const ushort QueryCharactersCommand = 0x0188;
        public const ushort QueryCharactersResponseCommand = 0x00C9;
        public const int HeaderSize = 0x40;
        public const int CharacterEntrySize = 0x3C;

        private const int RouteOffset = 0x08;
        private const int RouteCapacity = 32;
        private const int PtidOffset = 0x29;
        private const int PtidCapacity = 20;
        private static readonly Encoding Gbk = Encoding.GetEncoding(936,
            new EncoderReplacementFallback("?"),
            DecoderFallback.ReplacementFallback);

        public static bool TryDecodeQuery(LegacyDbServerFrame frame,
            out NativeType3Query request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 3)
            {
                error = "native type3 envelope is invalid";
                return false;
            }
            if (frame.Payload.Length < HeaderSize)
            {
                error = "native type3 payload is shorter than 64 bytes";
                return false;
            }

            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != QueryCharactersCommand)
            {
                error = "native type3 message is not a character query";
                return false;
            }
            var routeBytes = ReadShortStringBytes(payload, RouteOffset);
            var ptidBytes = ReadShortStringBytes(payload, PtidOffset);

            request = new NativeType3Query
            {
                Route = Gbk.GetString(routeBytes),
                Ptid = Gbk.GetString(ptidBytes),
                RouteBytes = routeBytes,
                PtidBytes = ptidBytes
            };
            return true;
        }

        public static bool TryCreateQueryResponse(NativeType3Query request,
            IReadOnlyList<NativeType3Character> characters,
            out LegacyDbServerFrame response, out string error)
        {
            response = null;
            error = string.Empty;
            if (request == null)
            {
                error = "native type3 query is missing";
                return false;
            }

            characters ??= Array.Empty<NativeType3Character>();
            if (characters.Count > ushort.MaxValue)
            {
                error = "native type3 character count exceeds one word";
                return false;
            }
            int payloadLength;
            try
            {
                payloadLength = checked(HeaderSize
                                        + characters.Count * CharacterEntrySize);
            }
            catch (OverflowException)
            {
                error = "native type3 character response is too large";
                return false;
            }
            if (payloadLength > NativeDbServerProtocol.MaximumFrameLength
                                - LegacyDbServerFrameCodec.HeaderSize)
            {
                error = "native type3 character response is too large";
                return false;
            }

            var payload = new byte[payloadLength];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                QueryCharactersResponseCommand);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4),
                unchecked((ushort)characters.Count));
            WriteShortStringBytes(payload, RouteOffset, RouteCapacity,
                request.RouteBytes.Length == 0
                    ? Gbk.GetBytes(request.Route ?? string.Empty)
                    : request.RouteBytes);
            WriteShortStringBytes(payload, PtidOffset, PtidCapacity,
                request.PtidBytes.Length == 0
                    ? Gbk.GetBytes(request.Ptid ?? string.Empty)
                    : request.PtidBytes);

            for (var i = 0; i < characters.Count; i++)
            {
                var character = characters[i] ?? new NativeType3Character();
                var entry = payload.AsSpan(
                    HeaderSize + i * CharacterEntrySize, CharacterEntrySize);
                var userIdBits = unchecked((ulong)character.UserId);
                BinaryPrimitives.WriteUInt32LittleEndian(entry,
                    unchecked((uint)(userIdBits >> 32)));
                BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(4, 4),
                    unchecked((uint)userIdBits));
                WriteShortStringBytes(entry, 0x08, 15,
                    character.CharacterNameBytes.Length == 0
                        ? Gbk.GetBytes(character.CharacterName ?? string.Empty)
                        : character.CharacterNameBytes);
                BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(0x18, 2),
                    unchecked((ushort)character.Level));
                entry[0x1A] = unchecked((byte)character.Sex);
                WriteShortStringBytes(entry, 0x1B, 4,
                    Gbk.GetBytes(GetJobText(character.Job)));
            }

            response = new LegacyDbServerFrame(3, 0, payload);
            return true;
        }

        public static bool ShouldBroadcastResponse(byte senderGroup, byte peerGroup)
        {
            return senderGroup == 0
                ? peerGroup != 9
                : peerGroup == senderGroup;
        }

        public static List<T> SelectBroadcastTargets<T>(byte senderGroup,
            IEnumerable<T> peers, Func<T, byte> groupSelector)
        {
            var targets = new List<T>();
            foreach (var peer in peers)
            {
                if (!ShouldBroadcastResponse(senderGroup, groupSelector(peer)))
                    continue;
                targets.Add(peer);
                if (senderGroup != 0) break;
            }
            return targets;
        }

        public static long CreateFallbackUserId(int zoneIndex, int groupIndex,
            int characterIndex) => unchecked(
            ((long)zoneIndex * 1000L + groupIndex) * 1_000_000_000L
            + characterIndex);

        public static string NormalizePtidKey(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (chars[i] is >= 'A' and <= 'Z') chars[i] += (char)32;
            return new string(chars);
        }

        public static string NormalizePtidKey(ReadOnlySpan<byte> value)
        {
            var normalized = value.ToArray();
            for (var i = 0; i < normalized.Length; i++)
                if (normalized[i] is >= (byte)'A' and <= (byte)'Z')
                    normalized[i] += 0x20;
            return Convert.ToHexString(normalized);
        }

        public static byte[] EncodeAnsi(string value) =>
            Gbk.GetBytes(value ?? string.Empty);

        public static string DecodeAnsi(ReadOnlySpan<byte> value) =>
            Gbk.GetString(value);

        public static int NormalizeDeleteState(int value) => unchecked((byte)value);

        private static string GetJobText(int job) => job switch
        {
            0 => "战士",
            1 => "法师",
            2 => "道士",
            3 => "刺客",
            _ => string.Empty
        };

        private static byte[] ReadShortStringBytes(ReadOnlySpan<byte> source,
            int offset)
        {
            var available = Math.Max(0, source.Length - offset - 1);
            var length = Math.Min(source[offset], available);
            return source.Slice(offset + 1, length).ToArray();
        }

        private static void WriteShortStringBytes(Span<byte> destination,
            int offset, int capacity, ReadOnlySpan<byte> value)
        {
            var length = Math.Min(value.Length, capacity);
            destination[offset] = unchecked((byte)length);
            value.Slice(0, length).CopyTo(destination.Slice(offset + 1, length));
        }
    }

    public sealed class NativeType3Query
    {
        public string Route { get; init; } = string.Empty;
        public string Ptid { get; init; } = string.Empty;
        public byte[] RouteBytes { get; init; } = Array.Empty<byte>();
        public byte[] PtidBytes { get; init; } = Array.Empty<byte>();
    }

    public sealed class NativeType3Character
    {
        public long UserId { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public byte[] CharacterNameBytes { get; init; } = Array.Empty<byte>();
        public int Level { get; init; }
        public int Sex { get; init; }
        public int Job { get; init; }
    }
}
