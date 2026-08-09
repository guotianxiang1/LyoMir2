using System.Buffers.Binary;

namespace GameSvr.Services
{
    public enum NativeType2MagicSnapshotResult
    {
        Ignored,
        RecordAppended,
        StreamCompleted,
        RecordAppendedAndCompleted
    }

    public sealed class NativeType2MagicRawRecord
    {
        private readonly byte[] _record;

        internal NativeType2MagicRawRecord(byte[] record, byte databaseJob)
        {
            _record = record ?? throw new ArgumentNullException(nameof(record));
            DatabaseJob = databaseJob;
        }

        public ushort MagicId =>
            BinaryPrimitives.ReadUInt16LittleEndian(_record.AsSpan(0x10, 2));

        public byte DatabaseJob { get; }

        public byte[] CopyRecord() => (byte[])_record.Clone();
    }

    /// <summary>
    /// Raw receiver for the original M2 Type2 101/102 startup streams.
    /// The records intentionally remain raw until their runtime consumers are
    /// independently verified.  Its lifetime is the M2 process, rather than a
    /// DB TCP connection: the original completion bits belong to the persistent
    /// DB client object and suppress duplicate streams after reconnect.
    /// </summary>
    public sealed class NativeType2MagicSnapshotState
    {
        public const ushort HumanMagicCommand = 0x0065;
        public const ushort HeroMagicCommand = 0x0066;
        public const int HeaderSize = 12;
        public const int RecordSize = 60;
        public const int PacketSize = HeaderSize + RecordSize;
        public const byte HumanCompleteFlag = 0x01;
        public const byte HeroCompleteFlag = 0x02;

        private const int MagicIdOffset = 0x10;
        private const int TrainingCapOffset = 0x1A;
        private const int NeedLevel5Offset = 0x1F;
        private const int LevelTrain4Offset = 0x2C;

        private readonly List<NativeType2MagicRawRecord> _humanRecords = new();
        private readonly List<NativeType2MagicRawRecord> _heroRecords = new();
        private readonly IReadOnlyList<NativeType2MagicRawRecord> _humanView;
        private readonly IReadOnlyList<NativeType2MagicRawRecord> _heroView;
        private byte _completionFlags;

        public NativeType2MagicSnapshotState()
        {
            _humanView = _humanRecords.AsReadOnly();
            _heroView = _heroRecords.AsReadOnly();
        }

        public IReadOnlyList<NativeType2MagicRawRecord> HumanRecords =>
            _humanView;

        public IReadOnlyList<NativeType2MagicRawRecord> HeroRecords =>
            _heroView;

        public byte CompletionFlags => _completionFlags;
        public bool HumanCompleted =>
            (_completionFlags & HumanCompleteFlag) != 0;
        public bool HeroCompleted =>
            (_completionFlags & HeroCompleteFlag) != 0;

        public NativeType2MagicSnapshotResult Consume(
            ReadOnlySpan<byte> payload)
        {
            if (payload.Length < HeaderSize)
                return NativeType2MagicSnapshotResult.Ignored;

            var command = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            var completeFlag = command switch
            {
                HumanMagicCommand => HumanCompleteFlag,
                HeroMagicCommand => HeroCompleteFlag,
                _ => (byte)0
            };
            if (completeFlag == 0 || (_completionFlags & completeFlag) != 0)
                return NativeType2MagicSnapshotResult.Ignored;

            var appended = false;
            if (payload.Length == PacketSize)
            {
                var record = payload.Slice(HeaderSize, RecordSize).ToArray();
                var databaseJob = record[TrainingCapOffset];
                if (command == HumanMagicCommand)
                {
                    ApplyHumanTrainingCap(record);
                    _humanRecords.Add(new NativeType2MagicRawRecord(
                        record, databaseJob));
                }
                else
                {
                    ApplyHeroTrainingCap(record);
                    _heroRecords.Add(new NativeType2MagicRawRecord(
                        record, databaseJob));
                }
                appended = true;
            }

            var completed = BinaryPrimitives.ReadInt32LittleEndian(
                payload.Slice(8, 4)) == 1;
            if (completed)
                _completionFlags |= completeFlag;

            return (appended, completed) switch
            {
                (true, true) =>
                    NativeType2MagicSnapshotResult.RecordAppendedAndCompleted,
                (true, false) =>
                    NativeType2MagicSnapshotResult.RecordAppended,
                (false, true) =>
                    NativeType2MagicSnapshotResult.StreamCompleted,
                _ => NativeType2MagicSnapshotResult.Ignored
            };
        }

        public void Reset()
        {
            _humanRecords.Clear();
            _heroRecords.Clear();
            _completionFlags = 0;
        }

        private static void ApplyHumanTrainingCap(Span<byte> record)
        {
            var magicId = BinaryPrimitives.ReadUInt16LittleEndian(
                record.Slice(MagicIdOffset, 2));
            var trainingCap = magicId switch
            {
                62 or 114 => 100,
                >= 60 and <= 61 => 255,
                128 => 12,
                >= 116 and <= 118 => 15,
                >= 125 and <= 127 or >= 234 and <= 236 => 9,
                115 => 7,
                3 or 6 or 11 or 12 or 25 or 31 or 48 or 59 => 4,
                >= 160 and <= 162 => 3,
                291 => 3,
                273 => 7,
                286 => 85,
                >= 287 and <= 290 or >= 314 and <= 317 => 3,
                _ => -1
            };
            if (trainingCap >= 0)
                record[TrainingCapOffset] = unchecked((byte)trainingCap);
        }

        private static void ApplyHeroTrainingCap(Span<byte> record)
        {
            var magicId = BinaryPrimitives.ReadUInt16LittleEndian(
                record.Slice(MagicIdOffset, 2));
            var trainingCap = magicId switch
            {
                3 or 6 or 11 or 12 or 13 or 25 or 26 or 31 or 35 or 48 or 59
                    => 4,
                >= 129 and <= 131 => 9,
                >= 50 and <= 55 => 10,
                >= 60 and <= 61 => 255,
                62 or 112 or 114 => 100,
                69 => 99,
                115 => 7,
                210 => 5,
                >= 160 and <= 162 => 3,
                291 => 3,
                >= 164 and <= 166 => 9,
                273 => 7,
                286 => 85,
                _ => -1
            };
            if (trainingCap >= 0)
                record[TrainingCapOffset] = unchecked((byte)trainingCap);

            record[NeedLevel5Offset] = byte.MaxValue;
            BinaryPrimitives.WriteInt32LittleEndian(
                record.Slice(LevelTrain4Offset, 4), -1);
        }
    }
}
