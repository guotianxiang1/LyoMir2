using System.Net.Sockets;
using System.Threading.Channels;

namespace GameSvr
{
    public class SendQueue
    {
        public const int DefaultCapacity = 1024;

        private readonly Channel<byte[]> _sendQueue;
        private readonly Socket _sendSocket;
        private readonly CancellationTokenSource _cancellation;
        private Exception _terminalError = null!;
        private int _stopped;

        public SendQueue(Socket socket, int capacity = DefaultCapacity)
        {
            ArgumentNullException.ThrowIfNull(socket);
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            _sendQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _cancellation = new CancellationTokenSource();
            _sendSocket = socket;
        }

        public int GetQueueCount => _sendQueue.Reader.Count;

        public Exception TerminalError => Volatile.Read(ref _terminalError);

        public void AddToQueue(byte[] buffer)
        {
            ValidateBuffer(buffer);
            if (buffer.Length == 0) return;

            ThrowIfUnavailable();
            if (_sendQueue.Writer.TryWrite(buffer)) return;

            ThrowIfUnavailable();
            throw new InvalidOperationException("The send queue is full.");
        }

        public async ValueTask AddToQueueAsync(byte[] buffer,
            CancellationToken cancellationToken = default)
        {
            ValidateBuffer(buffer);
            if (buffer.Length == 0) return;

            ThrowIfUnavailable();
            try
            {
                await _sendQueue.Writer.WriteAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                ThrowIfUnavailable();
                throw;
            }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;

            _sendQueue.Writer.TryComplete();
            _cancellation.Cancel();
        }

        public async Task ProcessSendQueue()
        {
            try
            {
                while (await _sendQueue.Reader.WaitToReadAsync(_cancellation.Token)
                           .ConfigureAwait(false))
                {
                    while (_sendQueue.Reader.TryRead(out var buffer))
                    {
                        await SendAllAsync(buffer).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException exception)
            {
                if (!_cancellation.IsCancellationRequested) Fail(exception);
            }
            catch (SocketException exception)
            {
                Fail(exception);
            }
        }

        private async Task SendAllAsync(byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var sendLen = await _sendSocket.SendAsync(
                        buffer.AsMemory(offset), SocketFlags.None, _cancellation.Token)
                    .ConfigureAwait(false);
                if (sendLen <= 0)
                    throw new SocketException((int)SocketError.ConnectionReset);

                offset += sendLen;
            }
        }

        private static void ValidateBuffer(byte[] buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);
        }

        private void ThrowIfUnavailable()
        {
            var error = Volatile.Read(ref _terminalError);
            if (error != null)
                throw new InvalidOperationException("The send queue stopped after a socket error.", error);
            if (Volatile.Read(ref _stopped) != 0)
                throw new OperationCanceledException("The send queue has stopped.",
                    _cancellation.Token);
        }

        private void Fail(Exception exception)
        {
            if (Interlocked.CompareExchange(ref _terminalError, exception, null) != null) return;

            Interlocked.Exchange(ref _stopped, 1);
            _sendQueue.Writer.TryComplete(exception);
            _cancellation.Cancel();
        }
    }
}
