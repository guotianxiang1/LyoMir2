using System.Buffers.Binary;
using System.Globalization;
using SystemModule;

namespace GameSvr.Services
{
    public sealed class NativeChannelActor
    {
        private readonly bool _online;

        public NativeChannelActor(long identity, string name, int level,
            bool online = true)
            : this(identity, name, level, online, null)
        {
        }

        internal NativeChannelActor(long identity, string name, int level,
            bool online, TPlayObject player)
        {
            Identity = identity;
            Name = name ?? string.Empty;
            Level = level;
            _online = online;
            Player = player;
        }

        public long Identity { get; }

        public string Name { get; }

        public int Level { get; }

        public bool IsOnline => Player == null ? _online : !Player.m_boGhost;

        internal TPlayObject Player { get; }

        internal static NativeChannelActor FromPlayer(TPlayObject player)
        {
            if (player == null) return null;
            return new NativeChannelActor(player.GetCachedNativeUserId(),
                player.m_sCharName, player.m_Abil?.Level ?? 0,
                !player.m_boGhost, player);
        }
    }

    public sealed class NativeChannelCreateRequest
    {
        public NativeChannelCreateRequest(string name, bool hasPassword,
            long password, byte capacity)
        {
            Name = name ?? string.Empty;
            HasPassword = hasPassword;
            Password = password;
            Capacity = capacity;
        }

        public string Name { get; }

        public bool HasPassword { get; }

        public long Password { get; }

        public byte Capacity { get; }

        public byte Type => HasPassword ? (byte)1 : (byte)0;
    }

    public readonly struct NativeChannelMembership
    {
        public NativeChannelMembership(string key, string channelName)
        {
            Key = key ?? string.Empty;
            ChannelName = channelName ?? string.Empty;
        }

        public string Key { get; }

        public string ChannelName { get; }
    }

    public interface INativeChannelMembershipResolver
    {
        bool TryResolve(NativeChannelActor actor, byte type,
            out NativeChannelMembership membership);
    }

    public sealed class NativeChannelMemberSnapshot
    {
        internal NativeChannelMemberSnapshot(long identity, string name,
            bool isOwner, bool isMuted, bool isOnline)
        {
            Identity = identity;
            Name = name ?? string.Empty;
            IsOwner = isOwner;
            IsMuted = isMuted;
            IsOnline = isOnline;
        }

        public long Identity { get; }

        public string Name { get; }

        public bool IsOwner { get; }

        public bool IsMuted { get; }

        public bool IsOnline { get; }
    }

    public sealed class NativeChannelSnapshot
    {
        internal NativeChannelSnapshot(int channelId, byte type, string name,
            string ownerName, byte mode, byte capacity, ushort nativeSlot,
            bool isClosed, IReadOnlyList<NativeChannelMemberSnapshot> members)
        {
            ChannelId = channelId;
            Type = type;
            Name = name ?? string.Empty;
            OwnerName = ownerName ?? string.Empty;
            Mode = mode;
            Capacity = capacity;
            NativeSlot = nativeSlot;
            IsClosed = isClosed;
            Members = members ?? Array.Empty<NativeChannelMemberSnapshot>();
        }

        public int ChannelId { get; }

        public byte Type { get; }

        public string Name { get; }

        public string OwnerName { get; }

        public byte Mode { get; }

        public byte Capacity { get; }

        public ushort NativeSlot { get; }

        public bool IsClosed { get; }

        public IReadOnlyList<NativeChannelMemberSnapshot> Members { get; }

        public int MemberCount => Members.Count;
    }

    public static class NativeChannelWireCodec
    {
        public const int CreatePayloadSize = 25;
        public const int ListRecordSize = 44;
        public const int MembersHeaderSize = 24;
        public const int MemberRecordSize = 18;

        private const int FixedTextSize = 16;
        private const int FixedTextMaximumBytes = 15;

        public static bool TryDecodeCreatePayload(ReadOnlySpan<byte> payload,
            out NativeChannelCreateRequest request, out int errorCode)
        {
            request = null;
            errorCode = -99;
            if (payload.Length < CreatePayloadSize) return false;

            var name = DecodeFixedText(payload.Slice(0, FixedTextSize));
            var hasPassword = payload[16] != 0;
            var password = 0L;
            if (hasPassword)
            {
                var passwordText = DecodeText(payload.Slice(17, 7));
                if (!long.TryParse(passwordText, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out password)
                    || password == -1)
                {
                    errorCode = -5;
                    return false;
                }
            }

            var capacity = payload[24];
            if (capacity < 2 || capacity > 200)
            {
                errorCode = -6;
                return false;
            }

            request = new NativeChannelCreateRequest(name, hasPassword,
                password, capacity);
            errorCode = 0;
            return true;
        }

        public static long ParseInt64OrDefault(ReadOnlySpan<byte> payload,
            long defaultValue)
        {
            return long.TryParse(DecodeText(payload), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value)
                ? value
                : defaultValue;
        }

        public static string DecodeText(ReadOnlySpan<byte> payload)
        {
            var terminator = payload.IndexOf((byte)0);
            if (terminator >= 0) payload = payload.Slice(0, terminator);
            return payload.IsEmpty
                ? string.Empty
                : HUtil32.GbkEncoding.GetString(payload);
        }

        public static byte[] EncodeChannelList(
            IReadOnlyList<NativeChannelSnapshot> channels)
        {
            if (channels == null || channels.Count == 0)
                return Array.Empty<byte>();

            var payload = new byte[checked(channels.Count * ListRecordSize)];
            for (var index = 0; index < channels.Count; index++)
            {
                var channel = channels[index];
                var record = payload.AsSpan(index * ListRecordSize,
                    ListRecordSize);
                BinaryPrimitives.WriteInt32LittleEndian(record.Slice(0, 4),
                    channel.ChannelId);
                WriteFixedText(record.Slice(4, FixedTextSize), channel.Name);
                BinaryPrimitives.WriteInt32LittleEndian(record.Slice(20, 4),
                    channel.MemberCount);
                WriteFixedText(record.Slice(24, FixedTextSize),
                    channel.OwnerName);
                record[40] = channel.Type;
                record[41] = channel.Capacity;
                BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(42, 2),
                    channel.NativeSlot);
            }
            return payload;
        }

        public static byte[] EncodeMembers(NativeChannelSnapshot channel,
            out int onlineMemberCount)
        {
            var onlineMembers = new List<NativeChannelMemberSnapshot>();
            if (channel != null)
            {
                foreach (var member in channel.Members)
                {
                    if (member.IsOnline) onlineMembers.Add(member);
                }
            }

            onlineMemberCount = onlineMembers.Count;
            if (channel == null) return Array.Empty<byte>();

            var payload = new byte[checked(MembersHeaderSize
                + onlineMembers.Count * MemberRecordSize)];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4),
                channel.ChannelId);
            WriteFixedText(payload.AsSpan(4, FixedTextSize), channel.Name);
            payload[20] = channel.Mode;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(22, 2),
                channel.NativeSlot);

            for (var index = 0; index < onlineMembers.Count; index++)
            {
                var member = onlineMembers[index];
                var record = payload.AsSpan(MembersHeaderSize
                    + index * MemberRecordSize, MemberRecordSize);
                WriteFixedText(record.Slice(0, FixedTextSize), member.Name);
                record[16] = member.IsOwner ? (byte)1 : (byte)0;
                record[17] = member.IsMuted ? (byte)1 : (byte)0;
            }
            return payload;
        }

        public static ushort BuildMembersSeries(byte channelType,
            int onlineMemberCount)
        {
            return (ushort)(((channelType & 0x7F) << 8)
                | (onlineMemberCount & 0xFF));
        }

        private static string DecodeFixedText(ReadOnlySpan<byte> source)
        {
            if (source.Length > FixedTextMaximumBytes)
                source = source.Slice(0, FixedTextMaximumBytes);
            return DecodeText(source);
        }

        private static void WriteFixedText(Span<byte> destination,
            string value)
        {
            destination.Clear();
            if (destination.IsEmpty || string.IsNullOrEmpty(value)) return;

            var maximumBytes = Math.Min(FixedTextMaximumBytes,
                destination.Length - 1);
            if (maximumBytes <= 0) return;

            var encoder = HUtil32.GbkEncoding.GetEncoder();
            encoder.Convert(value.AsSpan(), destination.Slice(0, maximumBytes),
                true, out _, out _, out _);
        }
    }
}
