using System.Buffers.Binary;

namespace GameSvr.Services
{
    public enum NativeType2MonsterSnapshotResult
    {
        Ignored,
        InvalidRecord,
        RecordCreated,
        RecordUpdated,
        StreamCompleted,
        InvalidRecordAndCompleted,
        RecordCreatedAndCompleted,
        RecordUpdatedAndCompleted
    }

    /// <summary>
    /// Original M2 Type2 103 (0x0067) monster stream. This intentionally keeps
    /// the native 32-bit fields separate from TMonInfo until every consumer has
    /// a verified non-truncating mapping.
    /// </summary>
    public sealed class NativeType2MonsterSnapshotState
    {
        public const ushort Command = 0x0067;
        public const int HeaderSize = 12;
        public const int MinimumBodySize = 0x5C;
        public const int NativeRecordSize = 0x68;

        private readonly List<NativeType2MonsterRecord> _records = new();
        private readonly IReadOnlyList<NativeType2MonsterRecord> _view;
        private readonly Dictionary<string, NativeType2MonsterRecord> _byName =
            new(StringComparer.Ordinal);

        public NativeType2MonsterSnapshotState()
        {
            _view = _records.AsReadOnly();
        }

        public bool Completed { get; private set; }
        public bool HasInvalidRecord { get; private set; }
        public IReadOnlyList<NativeType2MonsterRecord> Records => _view;

        public NativeType2MonsterSnapshotResult Consume(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < HeaderSize
                || BinaryPrimitives.ReadUInt16LittleEndian(payload) != Command
                || Completed)
                return NativeType2MonsterSnapshotResult.Ignored;

            var body = payload.Slice(HeaderSize);
            var result = NativeType2MonsterSnapshotResult.Ignored;
            if (body.Length >= MinimumBodySize)
            {
                if (!NativeType2MonsterRecord.TryGetNameKey(body, out var nameKey))
                {
                    HasInvalidRecord = true;
                    result = NativeType2MonsterSnapshotResult.InvalidRecord;
                }
                else if (_byName.TryGetValue(nameKey, out var existing))
                {
                    existing.Apply(body);
                    result = NativeType2MonsterSnapshotResult.RecordUpdated;
                }
                else
                {
                    var created = new NativeType2MonsterRecord(body);
                    _records.Add(created);
                    _byName.Add(nameKey, created);
                    result = NativeType2MonsterSnapshotResult.RecordCreated;
                }
            }

            if (BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)) != 1)
                return result;

            Completed = true;
            return result switch
            {
                NativeType2MonsterSnapshotResult.RecordCreated =>
                    NativeType2MonsterSnapshotResult.RecordCreatedAndCompleted,
                NativeType2MonsterSnapshotResult.RecordUpdated =>
                    NativeType2MonsterSnapshotResult.RecordUpdatedAndCompleted,
                NativeType2MonsterSnapshotResult.InvalidRecord =>
                    NativeType2MonsterSnapshotResult.InvalidRecordAndCompleted,
                _ => NativeType2MonsterSnapshotResult.StreamCompleted
            };
        }

        public void Reset()
        {
            _records.Clear();
            _byName.Clear();
            Completed = false;
            HasInvalidRecord = false;
        }
    }

    public sealed class NativeType2MonsterRecord
    {
        private const int NameOffset = 0x04;
        private const int NameCapacity = 15;
        private readonly byte[] _nativeFields = new byte[
            NativeType2MonsterSnapshotState.NativeRecordSize];
        private readonly byte[] _nameShortString = new byte[NameCapacity + 1];

        internal NativeType2MonsterRecord(ReadOnlySpan<byte> body)
        {
            Apply(body);
        }

        public ReadOnlyMemory<byte> NameShortString => _nameShortString;

        /// <summary>
        /// Returns the original M2 target record fields, including the 32-bit
        /// values at 0x50/0x5C/0x60/0x64. The manager-owned ID at offset zero is
        /// intentionally not synthesized here.
        /// </summary>
        public byte[] CopyNativeFields() => (byte[])_nativeFields.Clone();

        internal static bool TryGetNameKey(ReadOnlySpan<byte> body,
            out string nameKey)
        {
            nameKey = string.Empty;
            if (body.Length < NameOffset + NameCapacity + 1) return false;
            var length = body[NameOffset];
            if (length > NameCapacity) return false;
            nameKey = Convert.ToBase64String(body.Slice(NameOffset,
                length + 1));
            return true;
        }

        internal void Apply(ReadOnlySpan<byte> body)
        {
            // The native handler copies these fields from source +0x04 through
            // +0x3F, leaves +0x40..+0x4F untouched, then clears target +0x48.
            body.Slice(0x04, 0x3C).CopyTo(_nativeFields.AsSpan(0x04, 0x3C));
            body.Slice(NameOffset, NameCapacity + 1).CopyTo(_nameShortString);
            _nativeFields.AsSpan(0x48, sizeof(int)).Clear();

            // The four later source fields are copied to non-contiguous target
            // offsets by the original 32-bit M2 receiver.
            body.Slice(0x44, sizeof(int)).CopyTo(_nativeFields.AsSpan(0x50));
            body.Slice(0x50, sizeof(int)).CopyTo(_nativeFields.AsSpan(0x5C));
            body.Slice(0x54, sizeof(int)).CopyTo(_nativeFields.AsSpan(0x60));
            body.Slice(0x58, sizeof(int)).CopyTo(_nativeFields.AsSpan(0x64));
        }
    }
}
