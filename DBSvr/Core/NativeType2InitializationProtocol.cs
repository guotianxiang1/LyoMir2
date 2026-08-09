using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public static class NativeType2InitializationProtocol
    {
        public const ushort GameGateSnapshotCommand = 0x006E;
        public const ushort PrimaryEndCommand = 0x00C8;
        public const ushort SecondaryBeginCommand = 0x0074;
        public const ushort SecondaryEndCommand = 0x0069;
        public const int GameGateRecordSize = 20;

        private static readonly IReadOnlyDictionary<ushort, int>
            PrimaryRecordLengths = new Dictionary<ushort, int>
            {
                [0x006C] = 0x148,
                [0x006D] = 0x50,
                [0x0066] = 0x48,
                [0x0065] = 0x48,
                [0x0067] = 0x74,
                [0x0068] = 0x140,
                [0x0073] = 0xB6,
                [0x0075] = 0x8C,
                [0x0076] = 0x48
            };

        public static List<NativeGameGateEndpoint> ReadGameGates(
            ConfigManager config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var result = new List<NativeGameGateEndpoint>(32);
            for (var slot = 1; slot <= 32; slot++)
            {
                var specification = config.ReadString(
                    "GameGates", $"GameGate{slot}", "127.0.0.1");
                if (!TryParseGameGate(specification, out var endpoint)) continue;
                result.Add(endpoint);
            }
            return result;
        }

        public static bool TryParseGameGate(string specification,
            out NativeGameGateEndpoint endpoint)
        {
            endpoint = null;
            specification ??= string.Empty;
            var host = specification;
            var port = 7100;
            var colon = specification.IndexOf(':');
            if (colon >= 0)
            {
                host = specification[..colon];
                var portLength = Math.Min(5,
                    specification.Length - colon - 1);
                var portText = specification.Substring(
                    colon + 1, portLength);
                if (!TryParseDelphiInteger(portText, out port))
                    port = 7100;
            }
            if (port <= 0) return false;
            endpoint = new NativeGameGateEndpoint(host, port);
            return true;
        }

        public static LegacyDbServerFrame CreateGameGateSnapshot(
            byte registeredType, IReadOnlyList<NativeGameGateEndpoint> endpoints)
        {
            endpoints ??= Array.Empty<NativeGameGateEndpoint>();
            var payload = new byte[checked(NativeType2Protocol.HeaderSize
                                           + endpoints.Count * GameGateRecordSize)];
            BinaryPrimitives.WriteUInt16LittleEndian(
                payload, GameGateSnapshotCommand);
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(4, 4), registeredType);
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(8, 4), endpoints.Count);
            for (var i = 0; i < endpoints.Count; i++)
            {
                var endpoint = endpoints[i]
                               ?? new NativeGameGateEndpoint(string.Empty, 0);
                var offset = NativeType2Protocol.HeaderSize
                             + i * GameGateRecordSize;
                var host = LegacyGbkText.Encode(endpoint.Host);
                var length = Math.Min(15, host.Length);
                payload[offset] = (byte)length;
                host.AsSpan(0, length).CopyTo(payload.AsSpan(offset + 1));
                BinaryPrimitives.WriteInt32LittleEndian(
                    payload.AsSpan(offset + 16, 4), endpoint.Port);
            }
            return new LegacyDbServerFrame(2, 0, payload);
        }

        public static List<LegacyDbServerFrame> CreatePrimaryFrames(
            IReadOnlyList<byte[]> records)
        {
            var result = CreateRecordFrames(records, PrimaryRecordLengths);
            result.Add(CreateControlFrame(PrimaryEndCommand, 0, 0));
            return result;
        }

        public static List<LegacyDbServerFrame> CreateSecondaryFrames(
            bool rankingsLoading, IReadOnlyList<byte[]> records)
        {
            var result = new List<LegacyDbServerFrame>();
            if (rankingsLoading) return result;
            result.Add(CreateControlFrame(SecondaryBeginCommand, 0, 0));
            result.AddRange(CreateRankingFrames(records));
            result.Add(CreateControlFrame(SecondaryEndCommand, 100, 0));
            return result;
        }

        private static List<LegacyDbServerFrame> CreateRecordFrames(
            IReadOnlyList<byte[]> records,
            IReadOnlyDictionary<ushort, int> expectedLengths)
        {
            var result = new List<LegacyDbServerFrame>(records?.Count ?? 0);
            if (records == null) return result;
            foreach (var source in records)
            {
                if (source == null || source.Length < 2)
                    throw new ArgumentException("native type2 cached record is truncated");
                var command = BinaryPrimitives.ReadUInt16LittleEndian(source);
                if (!expectedLengths.TryGetValue(command, out var expectedLength)
                    || source.Length != expectedLength)
                    throw new ArgumentException(
                        $"native type2 cached record 0x{command:X4} length {source.Length} is invalid");
                result.Add(new LegacyDbServerFrame(2, 0, (byte[])source.Clone()));
            }
            return result;
        }

        private static List<LegacyDbServerFrame> CreateRankingFrames(
            IReadOnlyList<byte[]> records)
        {
            var result = new List<LegacyDbServerFrame>(records?.Count ?? 0);
            if (records == null) return result;
            foreach (var source in records)
            {
                if (source == null || source.Length < NativeType2Protocol.HeaderSize)
                    throw new ArgumentException(
                        "native type2 ranking record is truncated");
                var command = BinaryPrimitives.ReadUInt16LittleEndian(source);
                var category = BinaryPrimitives.ReadInt32LittleEndian(
                    source.AsSpan(4, 4));
                var expectedLength = category is >= 4 and <= 7
                    ? 0x124 : 0xB4;
                if (command != SecondaryEndCommand
                    || category is < 0 or > 13 or 11 or 12
                    || source.Length != expectedLength)
                    throw new ArgumentException(
                        $"native type2 ranking record category {category} length {source.Length} is invalid");
                result.Add(new LegacyDbServerFrame(
                    2, 0, (byte[])source.Clone()));
            }
            return result;
        }

        private static bool TryParseDelphiInteger(string value, out int result)
        {
            result = 0;
            if (value == null) return false;
            var index = 0;
            while (index < value.Length && value[index] == ' ') index++;
            var negative = false;
            if (index < value.Length && value[index] is '+' or '-')
            {
                negative = value[index] == '-';
                index++;
            }
            var numberBase = 10;
            if (index < value.Length && value[index] == '$')
            {
                numberBase = 16;
                index++;
            }
            else if (index < value.Length && value[index] is 'x' or 'X')
            {
                numberBase = 16;
                index++;
            }
            else if (index + 1 < value.Length && value[index] == '0'
                     && value[index + 1] is 'x' or 'X')
            {
                numberBase = 16;
                index += 2;
            }
            var digitStart = index;
            long parsed = 0;
            while (index < value.Length)
            {
                var ch = value[index];
                var digit = ch is >= '0' and <= '9' ? ch - '0'
                    : ch is >= 'A' and <= 'F' ? ch - 'A' + 10
                    : ch is >= 'a' and <= 'f' ? ch - 'a' + 10
                    : -1;
                if (digit < 0 || digit >= numberBase) return false;
                parsed = parsed * numberBase + digit;
                if (parsed > (long)int.MaxValue + (negative ? 1L : 0L))
                    return false;
                index++;
            }
            if (index == digitStart) return false;
            var signed = negative ? -parsed : parsed;
            result = (int)signed;
            return true;
        }

        private static LegacyDbServerFrame CreateControlFrame(ushort command,
            int param1, int param2)
        {
            var payload = new byte[NativeType2Protocol.HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), param1);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), param2);
            return new LegacyDbServerFrame(2, 0, payload);
        }
    }

    public sealed class NativeGameGateEndpoint
    {
        public NativeGameGateEndpoint(string host, int port)
        {
            Host = host ?? string.Empty;
            Port = port;
        }

        public string Host { get; }
        public int Port { get; }
    }

    public sealed class NativeType2InitializationCache
    {
        private readonly object _sync = new();
        private List<byte[]> _primary = new();
        private List<byte[]> _secondary = new();
        private bool _rankingsLoading;
        private long _rankingGeneration;

        public void ReplacePrimary(IReadOnlyList<byte[]> records)
        {
            var validated = NativeType2InitializationProtocol.CreatePrimaryFrames(
                records);
            validated.RemoveAt(validated.Count - 1);
            lock (_sync) _primary = ClonePayloads(validated);
        }

        public bool TryBeginRankingReload()
        {
            lock (_sync)
            {
                if (_rankingsLoading) return false;
                _rankingsLoading = true;
                return true;
            }
        }

        public void BeginRankingReload() => TryBeginRankingReload();

        public void PublishRankings(IReadOnlyList<byte[]> records)
        {
            NativeType2InitializationProtocol.CreateSecondaryFrames(
                false, records);
            lock (_sync)
            {
                _secondary = CloneRecords(records);
                _rankingGeneration++;
                _rankingsLoading = false;
            }
        }

        public void AppendStdItems(IReadOnlyList<byte[]> records)
        {
            if (records == null || records.Count == 0) return;
            var validated = new List<byte[]>(records.Count);
            foreach (var source in records)
            {
                if (source == null || source.Length != 0x140
                    || BinaryPrimitives.ReadUInt16LittleEndian(source)
                    != NativeType2StaticRecordBuilder.StdItemsCommand)
                    throw new ArgumentException(
                        "native stditems cache record is invalid",
                        nameof(records));
                validated.Add((byte[])source.Clone());
            }
            lock (_sync)
            {
                var previous = -1;
                for (var index = 0; index < _primary.Count; index++)
                {
                    var source = _primary[index];
                    if (source == null || source.Length < 12
                        || BinaryPrimitives.ReadUInt16LittleEndian(source)
                        != NativeType2StaticRecordBuilder.StdItemsCommand)
                        continue;
                    if (BinaryPrimitives.ReadInt32LittleEndian(
                            source.AsSpan(8, 4)) != 1)
                        continue;
                    previous = index;
                    break;
                }
                if (previous < 0) return;
                var insertAt = previous + 1;
                BinaryPrimitives.WriteInt32LittleEndian(
                    _primary[previous].AsSpan(8, 4), 0);
                for (var index = 0; index < validated.Count; index++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        validated[index].AsSpan(8, 4),
                        index == validated.Count - 1 ? 1 : 0);
                    _primary.Insert(insertAt + index, validated[index]);
                }
            }
        }

        public NativeType2InitializationSnapshot Snapshot()
        {
            lock (_sync)
                return new NativeType2InitializationSnapshot(
                    CloneRecords(_primary), CloneRecords(_secondary),
                    _rankingsLoading, _rankingGeneration);
        }

        private static List<byte[]> ClonePayloads(
            IEnumerable<LegacyDbServerFrame> frames)
        {
            var result = new List<byte[]>();
            foreach (var frame in frames)
                result.Add((byte[])frame.Payload.Clone());
            return result;
        }

        private static List<byte[]> CloneRecords(IEnumerable<byte[]> records)
        {
            var result = new List<byte[]>();
            if (records == null) return result;
            foreach (var record in records)
                result.Add(record == null ? null : (byte[])record.Clone());
            return result;
        }
    }

    public sealed class NativeType2InitializationSnapshot
    {
        public NativeType2InitializationSnapshot(IReadOnlyList<byte[]> primary,
            IReadOnlyList<byte[]> secondary, bool rankingsLoading,
            long rankingGeneration)
        {
            Primary = primary;
            Secondary = secondary;
            RankingsLoading = rankingsLoading;
            RankingGeneration = rankingGeneration;
        }

        public IReadOnlyList<byte[]> Primary { get; }
        public IReadOnlyList<byte[]> Secondary { get; }
        public bool RankingsLoading { get; }
        public long RankingGeneration { get; }
    }
}
