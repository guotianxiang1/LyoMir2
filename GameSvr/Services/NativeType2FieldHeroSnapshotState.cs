using System.Buffers.Binary;

namespace GameSvr.Services
{
    public enum NativeType2FieldHeroSnapshotResult
    {
        Ignored,
        RecordAppended,
        StreamCompleted,
        RecordAppendedAndCompleted
    }

    /// <summary>
    /// Original M2 Type2 108 (0x006C) FieldHero stream. This is intentionally
    /// a raw staging state: no C# FieldHero runtime model exists yet, so it
    /// must not turn network-provided names into filesystem paths.
    /// </summary>
    public sealed class NativeType2FieldHeroSnapshotState
    {
        public const ushort Command = 0x006C;
        public const int HeaderSize = 12;
        public const int BodySize = 0x13C;
        public const int PacketSize = HeaderSize + BodySize;

        private readonly List<NativeType2FieldHeroRawRecord> _records = new();
        private readonly IReadOnlyList<NativeType2FieldHeroRawRecord> _view;
        private Action<NativeType2FieldHeroSnapshotState> _completionCallback;

        public NativeType2FieldHeroSnapshotState()
        {
            _view = _records.AsReadOnly();
        }

        public bool Completed { get; private set; }
        public IReadOnlyList<NativeType2FieldHeroRawRecord> Records => _view;

        /// <summary>
        /// Mirrors the native one-shot completion callback. The owning Type2
        /// receiver is process-lifetime, so TCP reconnection does not clear an
        /// uncalled callback or an incomplete stream.
        /// </summary>
        public void SetCompletionCallback(
            Action<NativeType2FieldHeroSnapshotState> callback)
        {
            _completionCallback = callback;
        }

        public NativeType2FieldHeroSnapshotResult Consume(
            ReadOnlySpan<byte> payload)
        {
            if (payload.Length < HeaderSize
                || BinaryPrimitives.ReadUInt16LittleEndian(payload) != Command
                || Completed)
                return NativeType2FieldHeroSnapshotResult.Ignored;

            var appended = false;
            if (payload.Length == PacketSize)
            {
                _records.Add(new NativeType2FieldHeroRawRecord(
                    payload.Slice(HeaderSize, BodySize).ToArray()));
                appended = true;
            }

            if (BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)) != 1)
                return appended
                    ? NativeType2FieldHeroSnapshotResult.RecordAppended
                    : NativeType2FieldHeroSnapshotResult.Ignored;

            Completed = true;
            var callback = _completionCallback;
            try
            {
                callback?.Invoke(this);
            }
            finally
            {
                _completionCallback = null;
            }

            return appended
                ? NativeType2FieldHeroSnapshotResult.RecordAppendedAndCompleted
                : NativeType2FieldHeroSnapshotResult.StreamCompleted;
        }

        public void Reset()
        {
            _records.Clear();
            _completionCallback = null;
            Completed = false;
        }
    }

    /// <summary>
    /// Immutable copy of the 0x13C-byte wire body. Offset 0x138 remains the
    /// wire value; native M2 replaces that slot with a process-local pointer.
    /// </summary>
    public sealed class NativeType2FieldHeroRawRecord
    {
        private readonly byte[] _wireBody;

        internal NativeType2FieldHeroRawRecord(byte[] wireBody)
        {
            _wireBody = wireBody ?? throw new ArgumentNullException(nameof(wireBody));
        }

        public byte[] CopyWireBody() => (byte[])_wireBody.Clone();
    }
}
