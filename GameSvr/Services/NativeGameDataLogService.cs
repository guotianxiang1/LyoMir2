using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GameSvr.Services
{
    internal readonly record struct NativeGameDataLogRecord(
        byte LogType,
        string MapName,
        ushort X,
        ushort Y,
        string CharacterName,
        string ItemName,
        int MakeIndex,
        int Quantity,
        string Reason);

    internal static class NativeGameDataLogCodec
    {
        public const int RecordSize = 0xC4;
        public const int BodySize = 0xBC;
        public const uint Magic = 0x33AABB77;

        private static readonly Encoding Gbk;

        static NativeGameDataLogCodec()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936);
        }

        public static byte[] Encode(in NativeGameDataLogRecord record)
        {
            var result = new byte[RecordSize];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x00, 4),
                Magic);
            result[0x04] = 1;
            result[0x05] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x06, 2),
                BodySize);

            WriteShortString(result, 0x08, 20, record.MapName);
            result[0x1D] = record.LogType;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x20, 4),
                record.X);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x24, 4),
                record.Y);
            WriteShortString(result, 0x28, 20, record.CharacterName);
            WriteShortString(result, 0x3D, 20, record.ItemName);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x54, 4),
                record.MakeIndex);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x58, 4),
                record.Quantity);
            WriteShortString(result, 0x5C, 100, record.Reason);
            return result;
        }

        private static void WriteShortString(byte[] destination, int offset,
            int capacity, string value)
        {
            var bytes = Gbk.GetBytes(value ?? string.Empty);
            var length = Math.Min(bytes.Length, capacity);
            destination[offset] = (byte)length;
            bytes.AsSpan(0, length).CopyTo(
                destination.AsSpan(offset + 1, capacity));
        }
    }

    internal sealed class NativeGameDataLogBuffer
    {
        public const int AggregateCapacity = 0x1000;

        private readonly object _sync = new();
        private readonly Queue<byte[]> _records = new();
        private readonly byte[] _pending = new byte[AggregateCapacity];
        private int _pendingCount;

        public int QueuedRecordCount
        {
            get
            {
                lock (_sync) return _records.Count;
            }
        }

        public int PendingByteCount
        {
            get
            {
                lock (_sync) return _pendingCount;
            }
        }

        public void Enqueue(byte[] record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.Length <= 0 || record.Length > AggregateCapacity)
                throw new ArgumentOutOfRangeException(nameof(record));

            lock (_sync) _records.Enqueue((byte[])record.Clone());
        }

        public bool TryGetDatagram(out byte[] datagram, out int byteCount)
        {
            lock (_sync)
            {
                while (_records.Count > 0)
                {
                    var next = _records.Peek();
                    if (_pendingCount > 0
                        && _pendingCount + next.Length > AggregateCapacity)
                        break;
                    if (next.Length > AggregateCapacity - _pendingCount)
                        break;

                    _records.Dequeue();
                    next.CopyTo(_pending, _pendingCount);
                    _pendingCount += next.Length;
                }

                if (_pendingCount <= 0)
                {
                    datagram = null;
                    byteCount = 0;
                    return false;
                }

                // Only the worker mutates the staging area; producers append to
                // the FIFO. Returning this fixed buffer models native +0x98 and
                // avoids allocating on every 5 ms WSAEWOULDBLOCK retry.
                datagram = _pending;
                byteCount = _pendingCount;
                return true;
            }
        }

        public void CommitSent(int byteCount)
        {
            lock (_sync)
            {
                if (byteCount <= 0) return;
                if (byteCount > _pendingCount)
                    throw new ArgumentOutOfRangeException(nameof(byteCount));

                _pendingCount -= byteCount;
                if (_pendingCount > 0)
                {
                    Buffer.BlockCopy(_pending, byteCount, _pending, 0,
                        _pendingCount);
                }
                Array.Clear(_pending, _pendingCount,
                    _pending.Length - _pendingCount);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _records.Clear();
                Array.Clear(_pending);
                _pendingCount = 0;
            }
        }
    }

    internal sealed class NativeGameDataLogService : IDisposable
    {
        private const int WorkerIntervalMilliseconds = 5;
        private const int DisconnectedWorkerIntervalMilliseconds = 20;
        private const int ReconnectIntervalMilliseconds = 30_000;

        public static NativeGameDataLogService Instance { get; } = new();

        private readonly object _stateLock = new();
        private readonly AutoResetEvent _wake = new(false);
        private readonly NativeGameDataLogBuffer _buffer = new();

        private Thread _worker;
        private Socket _socket;
        private IPEndPoint _remoteEndPoint;
        private string _host = "127.0.0.1";
        private int _port = 10000;
        private int _started;
        private int _stopping;
        private uint _lastReconnectAttemptTick;

        public bool Connected
        {
            get
            {
                lock (_stateLock) return _socket != null;
            }
        }

        public int QueuedRecordCount => _buffer.QueuedRecordCount;
        public int PendingByteCount => _buffer.PendingByteCount;
        internal bool WorkerRunning
        {
            get
            {
                lock (_stateLock) return _worker?.IsAlive == true;
            }
        }

        public void Start(string host, int port)
        {
            lock (_stateLock)
            {
                if (_started != 0) return;
                _host = string.IsNullOrWhiteSpace(host)
                    ? "127.0.0.1"
                    : host.Trim();
                _port = port;
                _stopping = 0;
                _started = 1;
                _lastReconnectAttemptTick = 0;
                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "NativeGameDataLogUdp"
                };
                _worker.Start();
            }
        }

        public bool TryEnqueue(in NativeGameDataLogRecord record)
        {
            return TryEnqueueRaw(NativeGameDataLogCodec.Encode(record));
        }

        internal bool TryEnqueueRaw(byte[] payload)
        {
            lock (_stateLock)
            {
                if (_started == 0 || _stopping != 0 || _socket == null)
                    return false;
                _buffer.Enqueue(payload);
            }
            return true;
        }

        public void Stop()
        {
            RequestStop();
            WaitForStop();
        }

        internal void RequestStop()
        {
            lock (_stateLock)
            {
                if (_started == 0 || _stopping != 0) return;
                _stopping = 1;
                CloseSocketLocked();
            }

            _wake.Set();
        }

        internal void WaitForStop()
        {
            RequestStop();

            Thread worker;
            lock (_stateLock)
            {
                if (_started == 0) return;
                worker = _worker;
            }

            if (worker != null) worker.Join();

            lock (_stateLock)
            {
                if (_started == 0) return;
                _worker = null;
                _started = 0;
                _stopping = 0;
                _lastReconnectAttemptTick = 0;
                _buffer.Clear();
            }
        }

        public void Dispose()
        {
            Stop();
            _wake.Dispose();
        }

        internal static bool IsSoftSendError(SocketError error) =>
            error == SocketError.WouldBlock || error == SocketError.Interrupted;

        internal static bool IsReconnectDue(uint now, uint lastAttempt) =>
            unchecked(now - lastAttempt) >= ReconnectIntervalMilliseconds;

        internal static IPAddress ResolveNativeIpv4(string host)
        {
            if (IPAddress.TryParse(host, out var address)
                && address.AddressFamily == AddressFamily.InterNetwork)
                return address;

            // inet_addr returns INADDR_NONE (0xFFFFFFFF) on invalid text and the
            // native opener does not reject it. 255.255.255.255 is the matching
            // sockaddr value; a later send may still fail without SO_BROADCAST.
            return IPAddress.Broadcast;
        }

        private void WorkerLoop()
        {
            while (Volatile.Read(ref _stopping) == 0)
            {
                Socket socket;
                IPEndPoint remote;
                var waitMilliseconds = WorkerIntervalMilliseconds;
                lock (_stateLock)
                {
                    if (_socket == null)
                    {
                        var now = unchecked((uint)Environment.TickCount);
                        if (IsReconnectDue(now, _lastReconnectAttemptTick))
                        {
                            _lastReconnectAttemptTick = now;
                            TryOpenSocketLocked();
                        }
                    }
                    socket = _socket;
                    remote = _remoteEndPoint;
                    if (socket == null)
                    {
                        waitMilliseconds =
                            DisconnectedWorkerIntervalMilliseconds;
                    }
                }

                if (socket != null && remote != null
                    && _buffer.TryGetDatagram(out var datagram,
                        out var byteCount))
                {
                    try
                    {
                        var sent = socket.SendTo(datagram, 0, byteCount,
                            SocketFlags.None, remote);
                        if (sent > 0) _buffer.CommitSent(sent);
                    }
                    catch (SocketException ex)
                    {
                        if (!IsSoftSendError(ex.SocketErrorCode))
                        {
                            lock (_stateLock)
                            {
                                if (ReferenceEquals(socket, _socket))
                                {
                                    CloseSocketLocked();
                                }
                            }
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                _wake.WaitOne(waitMilliseconds);
            }
        }

        private void TryOpenSocketLocked()
        {
            if (_socket != null || _stopping != 0) return;
            try
            {
                var address = ResolveNativeIpv4(_host);
                var socket = new Socket(AddressFamily.InterNetwork,
                    SocketType.Dgram, ProtocolType.Udp)
                {
                    Blocking = false
                };
                _remoteEndPoint = new IPEndPoint(address,
                    unchecked((ushort)_port));
                _socket = socket;
            }
            catch
            {
                CloseSocketLocked();
            }
        }

        private void CloseSocketLocked()
        {
            var socket = _socket;
            _socket = null;
            _remoteEndPoint = null;
            if (socket == null) return;
            try
            {
                socket.Close();
            }
            catch
            {
            }
        }
    }
}
