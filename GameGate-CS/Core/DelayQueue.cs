using System.Collections.Concurrent;
using Timer = System.Threading.Timer;

namespace GameGate.Core;

/// <summary>
/// DelaySend queue — delays penalized packets by the default action interval.
/// Speed violations drop or delay packets; this implements the delay path.
/// Matches Delphi's m_DelayMessageList behavior.
/// </summary>
public sealed class DelayQueue : IDisposable
{
    private const int MaxQueuedPackets = 10000;
    private const long MaxQueuedBytes = 16L * 1024 * 1024;
    private readonly ConcurrentQueue<DelayedPacket> _queue = new();
    private readonly object _enqueueLock = new();
    private readonly Timer _timer;
    private readonly int _delayMs;
    private volatile bool _running;
    private int _count;
    private long _queuedBytes;
    private int _processing;
    private int _disposed;

    public event Func<DelayedPacket, Task>? OnDequeue;

    public int Count => Volatile.Read(ref _count);
    public long QueuedBytes => Interlocked.Read(ref _queuedBytes);

    public DelayQueue(int delayMs = 1000)
    {
        _delayMs = delayMs;
        _running = true;
        _timer = new Timer(ProcessQueue, null, _delayMs, _delayMs);
    }

    public void Enqueue(DelayedPacket packet)
    {
        lock (_enqueueLock)
        {
            if (!_running) return;
            var packetBytes = packet.Data?.Length ?? 0;
            if (!TryReserveBytes(packetBytes)) return;
            if (Interlocked.Increment(ref _count) > MaxQueuedPackets)
            {
                Interlocked.Decrement(ref _count);
                ReleaseBytes(packetBytes);
                return;
            }
            _queue.Enqueue(packet);
        }
    }

    private async void ProcessQueue(object? state)
    {
        if (!_running || Interlocked.Exchange(ref _processing, 1) != 0) return;
        try
        {
            while (_running && _queue.TryDequeue(out var packet))
            {
                Interlocked.Decrement(ref _count);
                ReleaseBytes(packet.Data?.Length ?? 0);
                var handler = OnDequeue;
                if (handler == null) continue;
                try { await handler(packet); }
                catch { }
            }
        }
        finally { Volatile.Write(ref _processing, 0); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _running = false;
        _timer.Dispose();
        OnDequeue = null;
        lock (_enqueueLock)
        {
            while (_queue.TryDequeue(out _))
                Interlocked.Decrement(ref _count);
            Interlocked.Exchange(ref _queuedBytes, 0);
        }
    }

    private bool TryReserveBytes(int count)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _queuedBytes);
            if (count > MaxQueuedBytes - current) return false;
            if (Interlocked.CompareExchange(ref _queuedBytes, current + count, current)
                == current) return true;
        }
    }

    private void ReleaseBytes(int count)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _queuedBytes);
            var next = Math.Max(0, current - count);
            if (Interlocked.CompareExchange(ref _queuedBytes, next, current) == current)
                return;
        }
    }
}

public struct DelayedPacket
{
    public byte[] Data;
    public int SessionId;
    public long Generation;
    public bool IsUpstream;
}
