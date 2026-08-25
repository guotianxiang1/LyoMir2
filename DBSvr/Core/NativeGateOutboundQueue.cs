using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DBSvr.Core
{
    /// <summary>
    /// One ordered writer per native GameGate connection. State locks only
    /// enqueue immutable frames; a slow socket can stall its own writer only.
    /// </summary>
    public sealed class NativeGateOutboundQueue
    {
        private readonly Channel<byte[]> _frames;
        private readonly Action<byte[]> _send;
        private readonly Action<Exception> _onError;
        private readonly Task _worker;

        public NativeGateOutboundQueue(Action<byte[]> send,
            Action<Exception> onError = null)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _onError = onError ?? (_ => { });
            _frames = Channel.CreateUnbounded<byte[]>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
            _worker = Task.Run(ProcessAsync);
        }

        public bool TryEnqueue(byte[] frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            return _frames.Writer.TryWrite((byte[])frame.Clone());
        }

        public void Complete() => _frames.Writer.TryComplete();

        public bool WaitForCompletion(int millisecondsTimeout) =>
            _worker.Wait(millisecondsTimeout);

        private async Task ProcessAsync()
        {
            try
            {
                await foreach (var frame in _frames.Reader.ReadAllAsync())
                    _send(frame);
            }
            catch (Exception ex)
            {
                _frames.Writer.TryComplete(ex);
                _onError(ex);
            }
        }
    }
}
