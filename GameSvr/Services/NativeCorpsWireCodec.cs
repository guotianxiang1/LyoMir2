using System.Buffers.Binary;
using System.Text;
using SystemModule;

namespace GameSvr.Services
{
    internal sealed class NativeCorpsMemberSnapshot
    {
        internal long MemberId { get; init; }
        internal string Name { get; init; } = string.Empty;
        internal ushort Level { get; set; }
        internal byte Sex { get; set; }
        internal byte Job { get; set; }
        internal string Title { get; set; } = string.Empty;
        internal DateTime LastLoginTime { get; set; }
    }

    internal sealed class NativeCorpsSnapshot
    {
        internal long Id { get; init; }
        internal DateTime CreateTime { get; init; }
        internal string Name { get; init; } = string.Empty;
        internal long OwnerId { get; set; }
        internal long ViceOwner1Id { get; set; }
        internal long ViceOwner2Id { get; set; }
        internal bool BanRecruit { get; set; }
        internal ushort RecruitLevelLimit { get; set; }
        internal byte RecruitJobSet { get; set; }
        internal byte[] Notice { get; set; } = Array.Empty<byte>();
        internal string NoticeText
        {
            get => HUtil32.GbkEncoding.GetString(
                Notice ?? Array.Empty<byte>());
            set => Notice = HUtil32.GbkEncoding.GetBytes(
                value ?? string.Empty);
        }
        internal List<NativeCorpsMemberSnapshot> Members { get; } = new();
    }

    internal sealed class NativeGildSnapshot
    {
        internal long Id { get; init; }
        internal DateTime CreateTime { get; init; }
        internal string Name { get; init; } = string.Empty;
        internal long OwnerCorpsId { get; set; }
        internal long ViceOwnerId { get; set; }
        internal byte[] Notice { get; set; } = Array.Empty<byte>();
        internal List<long> CorpsIds { get; } = new();
        // 4581 enable-union flag (native gild+0x28). Session-only: it has NO
        // gamedata.Gild column, so it is never persisted.
        // 原生 0x70633A: C6 47 28 01  mov byte [edi+0x28], 1
        // 构造函数置 TRUE — 行会默认开放联盟请求。重启后会从默认值恢复，
        // 因为没有 DB 列；会长需在每次重启后手动重置（与原版行为一致）。
        internal bool UnionEnabled { get; set; } = true;
    }

    internal sealed class NativeCorpsDataSnapshot
    {
        internal Dictionary<long, NativeCorpsSnapshot> CorpsById { get; } =
            new();
        internal Dictionary<long, NativeGildSnapshot> GildById { get; } =
            new();
        internal Dictionary<(ulong First, ulong Second), (byte Relation, DateTime CreateTime)>
            GildRelations { get; } = new();
        // gamedata.gildconcern rows: source gild id -> destination gild ids.
        internal Dictionary<long, List<long>> GildConcerns { get; } = new();

        // GILD-02: Native relation key normalization (0x5E6E8F..0x5E6ECF) compares
        // high dword SIGNED (jge at 0x5E6E9B), then low dword UNSIGNED (jae at 0x5E6E97).
        // The C# must match this exactly: cast high dword to int (signed), compare; if equal,
        // compare low dwords unsigned. Generated guild IDs are confined to [0, 2^48), so the
        // divergence (negative high dword) is unreachable in practice, but we match native
        // byte-for-byte to preserve the ordering contract for any loaded native data.
        internal static (ulong First, ulong Second) GildRelationKey(
            long first, long second)
        {
            // Split into high and low dwords
            var firstHigh = unchecked((int)(first >> 32));   // signed high dword
            var firstLow = unchecked((uint)first);           // unsigned low dword
            var secondHigh = unchecked((int)(second >> 32)); // signed high dword
            var secondLow = unchecked((uint)second);         // unsigned low dword

            // Compare high dword SIGNED
            bool firstLess;
            if (firstHigh != secondHigh)
                firstLess = firstHigh < secondHigh;
            else
                // High dwords equal, compare low dword UNSIGNED
                firstLess = firstLow < secondLow;

            var firstId = unchecked((ulong)first);
            var secondId = unchecked((ulong)second);
            return firstLess
                ? (firstId, secondId)
                : (secondId, firstId);
        }
    }

    internal readonly struct NativeCorpsActor
    {
        internal NativeCorpsActor(long id, string name, ushort level,
            byte sex, byte job)
        {
            Id = id;
            Name = name ?? string.Empty;
            Level = level;
            Sex = sex;
            Job = job;
        }

        internal long Id { get; }
        internal string Name { get; }
        internal ushort Level { get; }
        internal byte Sex { get; }
        internal byte Job { get; }
    }

    internal readonly struct NativeCorpsRecruitCondition
    {
        internal NativeCorpsRecruitCondition(byte jobs, ushort level,
            string notice)
        {
            Jobs = jobs;
            Level = level;
            Notice = notice ?? string.Empty;
        }

        internal byte Jobs { get; }
        internal ushort Level { get; }
        internal string Notice { get; }
    }

    internal readonly struct NativeCorpsLogEntry
    {
        internal NativeCorpsLogEntry(DateTime time, string text)
        {
            Time = time;
            Text = text ?? string.Empty;
        }

        internal DateTime Time { get; }
        internal string Text { get; }
    }

    internal static class NativeCorpsWireCodec
    {
        internal const int GuildIdSize = 8;
        internal const int GuildDescSize = 56;
        internal const int CorpsDescSize = 64;
        internal const int CorpsMemberSize = 48;
        internal const int CorpsRequestSize = 32;
        internal const int MemberTitleSize = 24;
        internal const int RecruitConditionSize = 60;
        internal const int LogDescSize = 64;
        internal const int GildRelationSummarySize = 24;
        internal const int GildConcernSummarySize = 32;
        internal const int GildRequestSummarySize = 56;

        private static readonly Encoding StrictGbk = Encoding.GetEncoding(
            HUtil32.GbkEncoding.CodePage, EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        internal static bool TryReadId(byte[] body, out long id,
            int offset = 0)
        {
            id = 0;
            if (body == null || offset < 0 || body.Length - offset < 8)
                return false;
            id = BinaryPrimitives.ReadInt64LittleEndian(
                body.AsSpan(offset, 8));
            return true;
        }

        internal static byte[] EncodeId(long id)
        {
            var body = new byte[GuildIdSize];
            BinaryPrimitives.WriteInt64LittleEndian(body, id);
            return body;
        }

        internal static bool TryDecodeRawText(byte[] body, out string value,
            int maximumBytes = int.MaxValue)
        {
            value = string.Empty;
            if (body == null) return false;
            var length = Array.IndexOf(body, (byte)0);
            if (length < 0) length = body.Length;
            if (length > maximumBytes) return false;
            try
            {
                value = StrictGbk.GetString(body, 0, length);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        internal static bool TryDecodeMemberTitle(byte[] body,
            out long memberId, out string title)
        {
            memberId = 0;
            title = string.Empty;
            return body != null && body.Length >= MemberTitleSize
                && TryReadId(body, out memberId)
                && TryReadShortString(body.AsSpan(8, 16), 15, out title);
        }

        internal static bool TryDecodeRecruitCondition(byte[] body,
            out NativeCorpsRecruitCondition condition)
        {
            condition = default;
            if (body == null || body.Length < RecruitConditionSize
                || !TryReadShortString(body.AsSpan(4, 56), 55,
                    out var notice))
                return false;
            condition = new NativeCorpsRecruitCondition(body[0],
                BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2, 2)),
                notice);
            return true;
        }

        internal static byte[] EncodeRecruitCondition(
            NativeCorpsSnapshot corps)
        {
            var body = new byte[RecruitConditionSize];
            body[0] = corps.RecruitJobSet;
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2, 2),
                corps.RecruitLevelLimit);
            WriteRawShortString(body.AsSpan(4, 56), 55, corps.Notice);
            return body;
        }

        internal static byte[] EncodeCorpsDescription(
            NativeCorpsSnapshot corps, string gildName, string captainName,
            int onlineCount)
        {
            var body = new byte[CorpsDescSize];
            BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(0, 8),
                corps.Id);
            WriteShortString(body.AsSpan(8, 16), 15, corps.Name);
            WriteShortString(body.AsSpan(24, 16), 15, gildName);
            WriteShortString(body.AsSpan(40, 16), 15, captainName);
            body[56] = unchecked((byte)Math.Min(byte.MaxValue,
                corps.Members.Count));
            body[57] = unchecked((byte)Math.Min(byte.MaxValue, onlineCount));
            return body;
        }

        internal static byte[] EncodeGuildDescription(NativeGildSnapshot gild,
            string presidentName, int playerCount, int onlineCount)
        {
            var body = new byte[GuildDescSize];
            BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(0, 8),
                gild.Id);
            WriteShortString(body.AsSpan(8, 16), 15, gild.Name);
            WriteShortString(body.AsSpan(24, 16), 15, presidentName);
            body[40] = unchecked((byte)Math.Min(byte.MaxValue,
                gild.CorpsIds.Count));
            body[41] = 0;
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(44, 4),
                playerCount);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(48, 4),
                onlineCount);
            return body;
        }

        internal static byte[] EncodeCorpsMembers(
            IReadOnlyList<(NativeCorpsMemberSnapshot Member, byte Position,
                bool Online)> members)
        {
            var body = new byte[checked(members.Count * CorpsMemberSize)];
            for (var index = 0; index < members.Count; index++)
            {
                var entry = members[index];
                var record = body.AsSpan(index * CorpsMemberSize,
                    CorpsMemberSize);
                BinaryPrimitives.WriteInt64LittleEndian(record[..8],
                    entry.Member.MemberId);
                WriteShortString(record.Slice(8, 16), 15,
                    entry.Member.Name);
                BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(24, 2),
                    entry.Member.Level);
                BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(26, 2),
                    entry.Online ? (ushort)1 : (ushort)0);
                record[28] = entry.Member.Job;
                record[29] = entry.Member.Sex;
                record[30] = entry.Position;
                WriteShortString(record.Slice(31, 16), 15,
                    entry.Member.Title);
            }
            return body;
        }

        internal static byte[] EncodeCorpsRequests(
            IReadOnlyList<NativeCorpsActor> requests)
        {
            var body = new byte[checked(requests.Count * CorpsRequestSize)];
            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                var record = body.AsSpan(index * CorpsRequestSize,
                    CorpsRequestSize);
                BinaryPrimitives.WriteInt64LittleEndian(record[..8],
                    request.Id);
                WriteShortString(record.Slice(8, 16), 15, request.Name);
                BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(24, 2),
                    request.Level);
                record[26] = request.Job;
                record[27] = request.Sex;
            }
            return body;
        }

        internal static byte[] EncodeLogs(
            IReadOnlyList<NativeCorpsLogEntry> entries)
        {
            var body = new byte[checked(entries.Count * LogDescSize)];
            for (var index = 0; index < entries.Count; index++)
            {
                var record = body.AsSpan(index * LogDescSize, LogDescSize);
                BinaryPrimitives.WriteInt64LittleEndian(record[..8],
                    BitConverter.DoubleToInt64Bits(entries[index].Time
                        .ToOADate()));
                WriteShortString(record.Slice(8, 56), 55,
                    entries[index].Text);
            }
            return body;
        }

        // 4575 CM_GILD_QUERY_UNION / 4580 CM_GILD_QUERY_HOSTILE record builders
        // (native sub_6F6470 / sub_6F6A14): per allied/hostile gild -> [0..7] the
        // partner gild's own id (sub_706914 = *(gild+24)), [8..23] its name as a
        // 16-byte short string (sub_4057AC + sub_4039E4, capacity 15). No relation
        // byte (the native list is already type-filtered), so 8 + 16 = 24 bytes.
        // Confirmed by the two builders' callee lists (sub_706914 + the short-string
        // pair only) filling exactly 24 bytes.
        internal static byte[] EncodeGildRelationSummaries(
            IReadOnlyList<(long Id, string Name)> gilds)
        {
            if (gilds.Count == 0) return Array.Empty<byte>();
            var body = new byte[checked(gilds.Count *
                                        GildRelationSummarySize)];
            for (var index = 0; index < gilds.Count; index++)
            {
                var record = body.AsSpan(index * GildRelationSummarySize,
                    GildRelationSummarySize);
                BinaryPrimitives.WriteInt64LittleEndian(record[..8],
                    gilds[index].Id);
                WriteShortString(record.Slice(8, 16), 15, gilds[index].Name);
            }
            return body;
        }

        // 4577 CM_GILD_QUERY_CONCERN record builder (native sub_6F66F0): per
        // concerned gild -> [0..7] the concerned gild's own id (0x6F6706/0x6F6708),
        // [8..23] its name (16-byte short string, capacity 15, written at 0x6F6729),
        // [24] the relation byte between the caller's gild and the concerned gild
        // (0x6F6750 call 0x5E7890 / 0x6F6755 `88 46 18 mov [esi+0x18],al`), [25..31]
        // zero padding = 32 bytes. The byte is the RAW map value, so besides 0 none /
        // 1 union / 2 hostile it also carries 3 for a pending alliance proposal --
        // sub_5E7890 does no filtering and neither should this.
        internal static byte[] EncodeGildConcernSummaries(
            IReadOnlyList<(long Id, string Name, byte Relation)> gilds)
        {
            if (gilds.Count == 0) return Array.Empty<byte>();
            var body = new byte[checked(gilds.Count *
                                        GildConcernSummarySize)];
            for (var index = 0; index < gilds.Count; index++)
            {
                var record = body.AsSpan(index * GildConcernSummarySize,
                    GildConcernSummarySize);
                BinaryPrimitives.WriteInt64LittleEndian(record[..8],
                    gilds[index].Id);
                WriteShortString(record.Slice(8, 16), 15, gilds[index].Name);
                record[24] = gilds[index].Relation;
            }
            return body;
        }

        // 4570 CM_GILD_QUERY_REQUEST_JOIN_LIST / 4571 CM_GILD_QUERY_REQUEST_UNION_LIST record builder
        // (native sub_70839C @0x0070839C): per pending request -> [0..7]=[4,5] requester/secondary key (the
        // applicant CORPS id for a join request), [8..15]=[8,C] UNIQUE request id (the accept/refuse key the
        // client echoes back in 4611/4572), [16..31]=name1 (16-byte short string, cap 15), [32..47]=name2
        // owner/leader (16-byte short string), [48]=resolved FLAG byte (1 if the requester resolved, else 0),
        // [49..55]=zero pad = 56 bytes. Native resolves [4,5] via the CORPS registry (sub_5EA444), so the
        // caller supplies the corps-resolved Name/OwnerName/Flag (empty/0 for a non-corps [4,5], e.g. a union
        // request whose [4,5] is a gild id — see NativeCorpsService.BuildGildRequestSummaryLocked).
        internal static byte[] EncodeGildRequestSummaries(
            IReadOnlyList<(long SecondaryKey, long UniqueId, string Name,
                string OwnerName, int Flag)> requests)
        {
            if (requests.Count == 0) return Array.Empty<byte>();
            var body = new byte[checked(requests.Count *
                                        GildRequestSummarySize)];
            for (var index = 0; index < requests.Count; index++)
            {
                var record = body.AsSpan(index * GildRequestSummarySize,
                    GildRequestSummarySize);
                BinaryPrimitives.WriteInt64LittleEndian(record[..8],
                    requests[index].SecondaryKey);
                BinaryPrimitives.WriteInt64LittleEndian(record.Slice(8, 8),
                    requests[index].UniqueId);
                WriteShortString(record.Slice(16, 16), 15,
                    requests[index].Name);
                WriteShortString(record.Slice(32, 16), 15,
                    requests[index].OwnerName);
                record[48] = unchecked((byte)requests[index].Flag);
            }
            return body;
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> source,
            int capacity, out string value)
        {
            value = string.Empty;
            if (source.Length < capacity + 1 || source[0] > capacity)
                return false;
            try
            {
                value = StrictGbk.GetString(source.Slice(1, source[0]));
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static void WriteShortString(Span<byte> destination,
            int capacity, string value)
        {
            if (destination.Length < capacity + 1)
                throw new ArgumentException("short-string destination is too small");

            destination[..(capacity + 1)].Clear();
            value ??= string.Empty;
            var byteCount = 0;
            foreach (var rune in value.EnumerateRunes())
            {
                var encoded = StrictGbk.GetBytes(rune.ToString());
                if (byteCount + encoded.Length > capacity) break;
                encoded.CopyTo(destination.Slice(1 + byteCount));
                byteCount += encoded.Length;
            }
            destination[0] = unchecked((byte)byteCount);
        }

        private static void WriteRawShortString(Span<byte> destination,
            int capacity, byte[] value)
        {
            if (destination.Length < capacity + 1)
                throw new ArgumentException("short-string destination is too small");

            destination[..(capacity + 1)].Clear();
            value ??= Array.Empty<byte>();
            var byteCount = Math.Min(capacity, value.Length);
            value.AsSpan(0, byteCount).CopyTo(destination[1..]);
            destination[0] = unchecked((byte)byteCount);
        }
    }
}
