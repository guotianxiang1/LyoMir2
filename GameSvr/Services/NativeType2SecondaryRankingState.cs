using System.Buffers.Binary;

namespace GameSvr.Services
{
    public enum NativeType2SecondaryRankingResult
    {
        Ignored,
        BucketsCleared,
        RecordAppended,
        BatchFinalized
    }

    /// <summary>
    /// Original generic Type2 0x0069/0x0074 secondary stream. Bucket 3's first
    /// page is projected at finalize time for the native online top-seven
    /// notification; other record bodies remain opaque.
    /// </summary>
    public sealed class NativeType2SecondaryRankingState
    {
        public const ushort RecordCommand = 0x0069;
        public const ushort ClearCommand = 0x0074;
        public const int HeaderSize = 12;
        public const int BucketCount = 14;
        public const int FinalizeCategory = 100;

        private readonly List<NativeType2SecondaryRankingRawRecord>[] _buckets =
            CreateBuckets();
        private readonly IReadOnlyList<NativeType2SecondaryRankingRawRecord>[]
            _views;
        private readonly NativeType2SecondaryRankingPublisher _publisher;

        public NativeType2SecondaryRankingState() : this(
            NativeType2SecondaryRankingPublisher.Runtime)
        {
        }

        public NativeType2SecondaryRankingState(
            NativeType2SecondaryRankingPublisher publisher)
        {
            _publisher = publisher
                ?? throw new ArgumentNullException(nameof(publisher));
            _views = new IReadOnlyList<NativeType2SecondaryRankingRawRecord>[
                BucketCount];
            for (var category = 0; category < BucketCount; category++)
                _views[category] = _buckets[category].AsReadOnly();
        }

        public int TotalRecordCount { get; private set; }
        public ushort LastFinalizeValue { get; private set; }
        public int Level999OrHigherCount { get; private set; }

        public IReadOnlyList<NativeType2SecondaryRankingRawRecord> GetBucket(
            int category)
        {
            if (category is < 0 or >= BucketCount)
                throw new ArgumentOutOfRangeException(nameof(category));
            return _views[category];
        }

        internal bool TryCopyPage(int category, ref int page,
            out int bodyLength, out byte[] body)
        {
            bodyLength = 0;
            body = Array.Empty<byte>();
            if (category is < 0 or >= BucketCount) return false;

            var bucket = _buckets[category];
            if (page < 0 || page >= bucket.Count)
                page = bucket.Count - 1;
            if (page < 0) return true;

            bodyLength = category is >= 4 and <= 7 ? 280 : 168;
            body = bucket[page].CopyBody();
            return true;
        }

        public NativeType2SecondaryRankingResult Consume(
            ReadOnlySpan<byte> payload)
        {
            if (payload.Length < HeaderSize)
                return NativeType2SecondaryRankingResult.Ignored;

            var command = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            if (command == ClearCommand)
            {
                for (var bucketIndex = 0; bucketIndex < BucketCount; bucketIndex++)
                    _buckets[bucketIndex].Clear();
                TotalRecordCount = 0;
                return NativeType2SecondaryRankingResult.BucketsCleared;
            }

            if (command != RecordCommand)
                return NativeType2SecondaryRankingResult.Ignored;

            var category = BinaryPrimitives.ReadInt32LittleEndian(
                payload.Slice(4, 4));
            if (category is >= 0 and < BucketCount)
            {
                _buckets[category].Add(
                    new NativeType2SecondaryRankingRawRecord(
                        payload.Slice(HeaderSize).ToArray()));
                TotalRecordCount = unchecked(TotalRecordCount + 1);
                return NativeType2SecondaryRankingResult.RecordAppended;
            }

            if (category != FinalizeCategory)
                return NativeType2SecondaryRankingResult.Ignored;

            LastFinalizeValue = unchecked((ushort)
                BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)));
            _publisher.Publish(_buckets[
                NativeType2SecondaryRankingPublisher.PersonalRankingBucket]);
            Level999OrHigherCount = CountLevel999OrHigher();
            return NativeType2SecondaryRankingResult.BatchFinalized;
        }

        private int CountLevel999OrHigher()
        {
            var count = 0;
            foreach (var record in _buckets[3])
            {
                var body = record.Body;
                for (var row = 0; row < 7; row++)
                {
                    var levelOffset = row * 24 + 16;
                    if (body.Length < levelOffset + sizeof(uint)) break;
                    if (BinaryPrimitives.ReadUInt32LittleEndian(
                            body.Slice(levelOffset, sizeof(uint))) >= 999)
                        count = unchecked(count + 1);
                }
            }
            return count;
        }

        private static List<NativeType2SecondaryRankingRawRecord>[]
            CreateBuckets()
        {
            var buckets = new List<NativeType2SecondaryRankingRawRecord>[
                BucketCount];
            for (var category = 0; category < BucketCount; category++)
                buckets[category] = new List<NativeType2SecondaryRankingRawRecord>();
            return buckets;
        }
    }

    public sealed class NativeType2SecondaryRankingRawRecord
    {
        private readonly byte[] _body;

        internal NativeType2SecondaryRankingRawRecord(byte[] body)
        {
            _body = body ?? throw new ArgumentNullException(nameof(body));
        }

        internal ReadOnlySpan<byte> Body => _body;
        public byte[] CopyBody() => (byte[])_body.Clone();
    }
}
