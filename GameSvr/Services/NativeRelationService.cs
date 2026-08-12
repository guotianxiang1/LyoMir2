using System.Buffers.Binary;
using System.Text;
using SystemModule;

namespace GameSvr.Services
{
    internal enum NativeRelationKind : byte
    {
        Friend = 0,
        Attention = 1,
        Blacklist = 3
    }

    internal static class NativeRelationStateBits
    {
        internal const uint Friend = 0x01;
        internal const uint FirstAttendsSecond = 0x02;
        internal const uint SecondAttendsFirst = 0x04;
        internal const uint FirstBlacklistsSecond = 0x08;
        internal const uint SecondBlacklistsFirst = 0x10;

        internal static uint ForOwner(NativeRelationKind kind, bool ownerIsFirst)
        {
            return kind switch
            {
                NativeRelationKind.Friend => Friend,
                NativeRelationKind.Attention => ownerIsFirst
                    ? FirstAttendsSecond
                    : SecondAttendsFirst,
                NativeRelationKind.Blacklist => ownerIsFirst
                    ? FirstBlacklistsSecond
                    : SecondBlacklistsFirst,
                _ => 0
            };
        }
    }

    internal sealed class NativeRelationPlayer
    {
        internal NativeRelationPlayer(long userId, string name, ushort level,
            byte job)
        {
            UserId = userId;
            Name = name ?? string.Empty;
            Level = level;
            Job = job;
        }

        internal long UserId { get; }
        internal string Name { get; }
        internal ushort Level { get; }
        internal byte Job { get; }
    }

    internal sealed class NativeRelationEntry
    {
        internal NativeRelationEntry(long playerId, string name, ushort level,
            byte job, byte focusColor)
        {
            PlayerId = playerId;
            Name = name ?? string.Empty;
            Level = level;
            Job = job;
            FocusColor = focusColor;
        }

        internal long PlayerId { get; }
        internal string Name { get; }
        internal ushort Level { get; }
        internal byte Job { get; }
        internal byte FocusColor { get; }
    }

    internal sealed class NativeRelationWireEntry
    {
        internal NativeRelationWireEntry(string name, ushort level, byte job,
            byte focusColor, string guildName, bool online)
        {
            Name = name ?? string.Empty;
            Level = level;
            Job = job;
            FocusColor = focusColor;
            GuildName = guildName ?? string.Empty;
            Online = online;
        }

        internal string Name { get; }
        internal ushort Level { get; }
        internal byte Job { get; }
        internal byte FocusColor { get; }
        internal string GuildName { get; }
        internal bool Online { get; }
    }

    internal enum NativeRelationStoreResult
    {
        Success,
        Missing,
        Duplicate,
        Full,
        Failed
    }

    internal interface INativeRelationStore
    {
        bool TryLoad(long ownerId, NativeRelationKind kind,
            out IReadOnlyList<NativeRelationEntry> entries);

        bool TryInspect(long ownerId, long targetId, NativeRelationKind kind,
            out int count, out bool contains);

        NativeRelationStoreResult TryAddDirected(NativeRelationPlayer owner,
            NativeRelationPlayer target, NativeRelationKind kind,
            byte focusColor, int limit);

        NativeRelationStoreResult TryAddFriend(NativeRelationPlayer requester,
            NativeRelationPlayer accepter, int limit);

        NativeRelationStoreResult TryRemove(long ownerId, string targetName,
            NativeRelationKind kind);

        NativeRelationStoreResult TryUpdateAttentionColor(long ownerId,
            string targetName, byte color);
    }

    internal sealed class NativeRelationService
    {
        internal const int Limit = 200;
        internal const int NoResponse = int.MinValue;

        private readonly INativeRelationStore _store;

        internal NativeRelationService(INativeRelationStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        internal bool TryQuery(long ownerId, NativeRelationKind kind,
            out IReadOnlyList<NativeRelationEntry> entries)
        {
            return _store.TryLoad(ownerId, kind, out entries);
        }

        internal int CheckFriendRequest(NativeRelationPlayer requester,
            NativeRelationPlayer target)
        {
            if (!_store.TryInspect(requester.UserId, target.UserId,
                    NativeRelationKind.Friend, out var count, out var contains))
                return -1;
            if (contains) return -3;
            if (count >= Limit) return -4;

            // FRIEND-08: Check target's friend list capacity
            if (!_store.TryInspect(target.UserId, requester.UserId,
                    NativeRelationKind.Friend, out var targetCount, out _))
                return -1;
            return targetCount >= Limit ? -4 : 0;
        }

        internal int AddDirected(NativeRelationPlayer owner,
            NativeRelationPlayer target, NativeRelationKind kind)
        {
            var result = _store.TryAddDirected(owner, target, kind,
                kind == NativeRelationKind.Attention ? byte.MaxValue : (byte)0,
                Limit);
            return result switch
            {
                NativeRelationStoreResult.Success => 0,
                NativeRelationStoreResult.Full => -4,
                NativeRelationStoreResult.Duplicate => -5,
                _ => NoResponse
            };
        }

        internal int Remove(long ownerId, string targetName,
            NativeRelationKind kind)
        {
            var result = _store.TryRemove(ownerId, targetName, kind);
            if (result == NativeRelationStoreResult.Success) return 0;
            if (result != NativeRelationStoreResult.Missing) return NoResponse;
            return kind == NativeRelationKind.Friend ? -2 : -1;
        }

        internal int UpdateAttentionColor(long ownerId, string targetName,
            byte color)
        {
            return _store.TryUpdateAttentionColor(ownerId, targetName, color)
                switch
                {
                    NativeRelationStoreResult.Success => 0,
                    NativeRelationStoreResult.Missing => -1,
                    _ => -2
                };
        }

        internal int AcceptFriend(NativeRelationPlayer requester,
            NativeRelationPlayer accepter)
        {
            return _store.TryAddFriend(requester, accepter, Limit) switch
            {
                NativeRelationStoreResult.Success => 0,
                NativeRelationStoreResult.Duplicate => -3,
                NativeRelationStoreResult.Full => -4,
                _ => -5
            };
        }
    }

    internal static class NativeRelationWireCodec
    {
        internal const int NameSize = 15;
        internal const int FriendRecordSize = 36;
        internal const int AttentionRecordSize = 22;
        internal const int BlacklistRecordSize = 20;

        private static readonly Encoding StrictGbk = Encoding.GetEncoding(
            HUtil32.GbkEncoding.CodePage, EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        internal static bool TryDecodeName(object payload, out string name)
        {
            name = string.Empty;
            if (payload == null) return true;
            if (payload is not byte[] bytes) return false;

            var length = Array.IndexOf(bytes, (byte)0);
            if (length < 0) length = bytes.Length;
            try
            {
                name = StrictGbk.GetString(bytes, 0, length);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        internal static byte[] EncodeName(string name)
        {
            return HUtil32.GbkEncoding.GetBytes(name ?? string.Empty);
        }

        internal static byte[] Encode(NativeRelationKind kind,
            IReadOnlyList<NativeRelationWireEntry> entries)
        {
            entries ??= Array.Empty<NativeRelationWireEntry>();
            var recordSize = kind switch
            {
                NativeRelationKind.Friend => FriendRecordSize,
                NativeRelationKind.Attention => AttentionRecordSize,
                NativeRelationKind.Blacklist => BlacklistRecordSize,
                _ => 0
            };
            if (recordSize == 0 || entries.Count == 0)
                return Array.Empty<byte>();

            var body = new byte[checked(recordSize * entries.Count)];
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var record = body.AsSpan(index * recordSize, recordSize);
                WriteFixedGbk(record[..NameSize], entry.Name);
                BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(16, 2),
                    entry.Level);

                switch (kind)
                {
                    case NativeRelationKind.Friend:
                        record[18] = entry.Job;
                        WriteFixedGbk(record.Slice(19, NameSize),
                            entry.GuildName);
                        record[35] = entry.Online ? (byte)1 : (byte)0;
                        break;
                    case NativeRelationKind.Attention:
                        record[18] = entry.Job;
                        record[19] = entry.FocusColor;
                        record[20] = entry.Online ? (byte)1 : (byte)0;
                        break;
                    case NativeRelationKind.Blacklist:
                        record[18] = entry.Online ? (byte)1 : (byte)0;
                        break;
                }
            }
            return body;
        }

        private static void WriteFixedGbk(Span<byte> destination, string value)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            bytes.AsSpan(0, Math.Min(bytes.Length, destination.Length))
                .CopyTo(destination);
        }
    }
}
