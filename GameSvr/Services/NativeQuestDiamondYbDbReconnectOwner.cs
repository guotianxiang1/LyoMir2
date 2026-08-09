using SystemModule.Packet;

namespace GameSvr
{
    /// <summary>
    /// Dormant model of the native YBDB receive lifetime used by Ident 1122.
    /// A generation token rejects callbacks from a replaced socket, but a
    /// complete frame loses its socket provenance once it enters the FIFO.
    /// </summary>
    public sealed class NativeQuestDiamondYbDbReconnectOwner
    {
        private readonly object _sync = new();
        private readonly YbDbLegacy77StreamParser _parser = new();
        private readonly Queue<YbDbLegacy77Frame> _frames = new();

        private long _currentGeneration;
        private bool _connected;

        public long BeginConnection()
        {
            lock (_sync)
            {
                _currentGeneration = unchecked(_currentGeneration + 1);
                _connected = true;
                return _currentGeneration;
            }
        }

        public bool EndConnection(long generation)
        {
            lock (_sync)
            {
                if (!_connected || generation != _currentGeneration)
                    return false;
                _connected = false;
                return true;
            }
        }

        public bool Append(long generation, ReadOnlySpan<byte> data)
        {
            lock (_sync)
            {
                if (!_connected || generation != _currentGeneration)
                    return false;

                _parser.Append(data, frame => _frames.Enqueue(frame));
                return true;
            }
        }

        public bool TryDequeue(out YbDbLegacy77Frame frame)
        {
            lock (_sync)
            {
                if (_frames.Count == 0)
                {
                    frame = null;
                    return false;
                }

                frame = _frames.Dequeue();
                return true;
            }
        }

        /// <summary>
        /// Captures the connection used by an ACK at completion-consume time.
        /// It intentionally does not accept the source generation of a frame.
        /// </summary>
        public bool TryCaptureCurrentSendGeneration(out long generation)
        {
            lock (_sync)
            {
                generation = _currentGeneration;
                return _connected;
            }
        }

        public bool Connected
        {
            get
            {
                lock (_sync) return _connected;
            }
        }

        public long CurrentGeneration
        {
            get
            {
                lock (_sync) return _currentGeneration;
            }
        }

        public int BufferedLength
        {
            get
            {
                lock (_sync) return _parser.BufferedLength;
            }
        }

        public int PendingFrameCount
        {
            get
            {
                lock (_sync) return _frames.Count;
            }
        }
    }
}
