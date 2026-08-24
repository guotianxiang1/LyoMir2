using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SystemModule.Packet
{
    /// <summary>
    /// Native GameGate type-24 focus-item response cache.
    /// </summary>
    public sealed class LegacyGateType24Cache
    {
        public const ushort MessageType = 24;
        public const int Capacity = 1024;
        public const int HashBucketCount = 2048;
        public const int MinimumPayloadLength = 13;
        public const int MaximumPayloadLength = 512;
        public const int LookupIntervalMilliseconds = 400;

        private sealed class Entry
        {
            public int Recog;
            public byte[] Payload = Array.Empty<byte>();
        }

        private readonly object _sync = new object();
        private readonly Dictionary<int, LinkedListNode<Entry>> _entries =
            new Dictionary<int, LinkedListNode<Entry>>(Capacity);
        private readonly LinkedList<Entry> _evictionOrder = new LinkedList<Entry>();

        public int Count
        {
            get
            {
                lock (_sync) return _entries.Count;
            }
        }

        public bool TryStore(byte[] payload)
        {
            if (payload == null || payload.Length < MinimumPayloadLength
                || payload.Length > MaximumPayloadLength)
                return false;

            var recog = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, sizeof(int)));
            var copy = (byte[])payload.Clone();

            lock (_sync)
            {
                if (_entries.TryGetValue(recog, out var existing))
                {
                    existing.Value.Payload = copy;
                    _evictionOrder.Remove(existing);
                    _evictionOrder.AddLast(existing);
                    return true;
                }

                if (_entries.Count == Capacity)
                {
                    var oldest = _evictionOrder.First!;
                    _evictionOrder.RemoveFirst();
                    _entries.Remove(oldest.Value.Recog);
                }

                var node = _evictionOrder.AddLast(new Entry
                {
                    Recog = recog,
                    Payload = copy
                });
                _entries.Add(recog, node);
                return true;
            }
        }

        public bool TryGet(int recog, out byte[] payload)
        {
            lock (_sync)
            {
                if (!_entries.TryGetValue(recog, out var node))
                {
                    payload = Array.Empty<byte>();
                    return false;
                }

                // The native lookup transposes a hit with its next neighbour
                // instead of moving it directly to the most-recent end.
                var next = node.Next;
                if (next != null)
                {
                    _evictionOrder.Remove(node);
                    _evictionOrder.AddAfter(next, node);
                }

                payload = (byte[])node.Value.Payload.Clone();
                return true;
            }
        }

        public static bool IsLookupDue(long nowMilliseconds, long previousMilliseconds)
        {
            return nowMilliseconds > previousMilliseconds + LookupIntervalMilliseconds;
        }
    }
}
