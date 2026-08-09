using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SystemModule.Sockets
{
    public class IClientScoket
    {
        private const int BufferSize = 1024;
        private readonly object _stateLock = new object();
        private Socket cli;
        private QueuedSendState _queuedSendState;
        private ClientConnectionLifecycle _connectionLifecycle;
        private int _disconnectRaised;

        public bool IsConnected;
        public string Host = string.Empty;
        public int Port;
        public bool IsBusy;

        public event DSCClientOnConnectedHandler OnConnected;
        public event DSCClientOnErrorHandler OnError;
        public event DSCClientOnDataInHandler ReceivedDatagram;
        public event DSCClientOnDisconnectedHandler OnDisconnected;

        public void Connect()
        {
            if (string.IsNullOrEmpty(Host) || Port <= 0)
                throw new Exception("IP地址或端口号错误");
            Connect(Host, Port);
        }

        public void Connect(string ip, int port)
        {
            ConnectCore(ip, port, replacePendingConnection: false);
        }

        public void ConnectReplacingPending(string ip, int port)
        {
            ConnectCore(ip, port, replacePendingConnection: true);
        }

        private void ConnectCore(string ip, int port, bool replacePendingConnection)
        {
            if (string.IsNullOrWhiteSpace(ip) || port <= 0)
                throw new ArgumentException("IP地址或端口号错误");

            Socket previous;
            Socket socket;
            lock (_stateLock)
            {
                Host = ip;
                Port = port;
                if ((!replacePendingConnection && IsBusy)
                    || IsConnected && cli?.Connected == true)
                    return;

                previous = cli;
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                    SendTimeout = 10000
                };
                cli = socket;
                _queuedSendState = new QueuedSendState(socket);
                _connectionLifecycle = new ClientConnectionLifecycle();
                IsConnected = false;
                IsBusy = true;
                _disconnectRaised = 0;
            }

            try { previous?.Dispose(); } catch { }

            try
            {
                var remote = new IPEndPoint(IPAddress.Parse(ip), port);
                socket.BeginConnect(remote, HandleConnect, socket);
            }
            catch (SocketException ex)
            {
                RaiseErrorEvent(socket, ex);
                DisconnectSocket(socket);
            }
            catch
            {
                DisconnectSocket(socket);
                throw;
            }
        }

        private void HandleConnect(IAsyncResult result)
        {
            var socket = (Socket)result.AsyncState;
            try
            {
                socket.EndConnect(result);
                ClientConnectionLifecycle lifecycle;
                lock (_stateLock)
                {
                    if (!ReferenceEquals(cli, socket))
                    {
                        socket.Dispose();
                        return;
                    }
                    IsConnected = true;
                    IsBusy = false;
                    lifecycle = _connectionLifecycle;
                }

                if (lifecycle != null && lifecycle.InvokeConnect(() =>
                        OnConnected?.Invoke(this,
                            new DSCClientConnectedEventArgs(socket)))
                    && IsCurrentConnected(socket))
                    StartWaitingForData(new ReceiveState(socket));
            }
            catch (SocketException ex)
            {
                RaiseErrorEvent(socket, ex);
                DisconnectSocket(socket);
            }
            catch (ObjectDisposedException)
            {
                DisconnectSocket(socket);
            }
        }

        private void StartWaitingForData(ReceiveState state)
        {
            try
            {
                state.Socket.BeginReceive(state.Buffer, 0, state.Buffer.Length,
                    SocketFlags.None, HandleIncomingData, state);
            }
            catch (SocketException ex)
            {
                RaiseErrorEvent(state.Socket, ex);
                DisconnectSocket(state.Socket);
            }
            catch (ObjectDisposedException)
            {
                DisconnectSocket(state.Socket);
            }
        }

        private void HandleIncomingData(IAsyncResult result)
        {
            var state = (ReceiveState)result.AsyncState;
            try
            {
                var length = state.Socket.EndReceive(result);
                if (length <= 0)
                {
                    DisconnectSocket(state.Socket);
                    return;
                }
                if (!IsCurrentConnected(state.Socket)) return;

                var data = new byte[length];
                Buffer.BlockCopy(state.Buffer, 0, data, 0, length);
                if (!IsCurrentConnected(state.Socket)) return;
                ReceivedDatagram?.Invoke(this, new DSCClientDataInEventArgs(state.Socket, data));

                if (IsCurrentConnected(state.Socket))
                    StartWaitingForData(state);
            }
            catch (SocketException ex)
            {
                RaiseErrorEvent(state.Socket, ex);
                DisconnectSocket(state.Socket);
            }
            catch (ObjectDisposedException)
            {
                DisconnectSocket(state.Socket);
            }
        }

        public void SendText(string str)
        {
            if (!string.IsNullOrEmpty(str)) Send(HUtil32.GbkEncoding.GetBytes(str));
        }

        public void Send(byte[] buffer)
        {
            Send(buffer, null);
        }

        public void Send(byte[] buffer, Socket expectedSocket)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length == 0) return;

            Socket socket;
            QueuedSendState state;
            long ticket;
            lock (_stateLock)
            {
                socket = cli;
                if (!IsConnected || socket?.Connected != true
                    || expectedSocket != null && !ReferenceEquals(socket, expectedSocket))
                    return;
                state = _queuedSendState;
                if (state == null || !ReferenceEquals(state.Socket, socket)) return;
                ticket = state.SendOrder.Reserve();
            }

            try
            {
                state.SendOrder.WaitTurn(ticket);
                try
                {
                    if (!IsCurrentConnected(socket)) return;
                    var offset = 0;
                    while (offset < buffer.Length)
                    {
                        var sent = socket.Send(buffer, offset, buffer.Length - offset, SocketFlags.None);
                        if (sent <= 0) throw new SocketException((int)SocketError.ConnectionReset);
                        offset += sent;
                    }
                }
                finally { state.SendOrder.Complete(ticket); }
            }
            catch (SocketException ex)
            {
                RaiseErrorEvent(socket, ex);
                DisconnectSocket(socket);
            }
            catch (ObjectDisposedException)
            {
                DisconnectSocket(socket);
            }
        }

        public bool QueueSend(byte[] buffer, Socket expectedSocket)
            => QueueSend(buffer, expectedSocket, null);

        public bool QueueSend(byte[] buffer, Socket expectedSocket,
            Action<bool> completion)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length == 0)
            {
                try { completion?.Invoke(true); } catch { }
                return true;
            }

            QueuedSendState state;
            Socket socket;
            lock (_stateLock)
            {
                socket = cli;
                if (!IsConnected || socket?.Connected != true
                    || expectedSocket != null && !ReferenceEquals(socket, expectedSocket))
                    return false;
                state = _queuedSendState;
                if (state == null || !ReferenceEquals(state.Socket, socket)) return false;
            }

            var item = new QueuedSendItem(buffer, completion);
            var startSender = false;
            lock (_stateLock)
            {
                if (!IsConnected || !ReferenceEquals(cli, socket)
                    || socket.Connected != true
                    || expectedSocket != null && !ReferenceEquals(socket, expectedSocket))
                    return false;
                if (!ReferenceEquals(state, _queuedSendState)) return false;
                lock (state.SyncRoot)
                {
                    var ticket = state.SendOrder.Reserve();
                    try
                    {
                        item.Publish(ticket);
                        state.Queue.Enqueue(item);
                        if (!state.IsSending)
                        {
                            state.IsSending = true;
                            startSender = true;
                        }
                    }
                    catch
                    {
                        state.SendOrder.Complete(ticket);
                        throw;
                    }
                }
            }
            if (startSender)
                _ = ProcessQueuedSendsAsync(state);
            return true;
        }

        private async Task ProcessQueuedSendsAsync(QueuedSendState state)
        {
            QueuedSendItem current = null;
            try
            {
                while (true)
                {
                    lock (state.SyncRoot)
                    {
                        if (state.Queue.Count == 0)
                        {
                            state.IsSending = false;
                            return;
                        }
                        current = state.Queue.Dequeue();
                    }

                    var offset = 0;
                    state.SendOrder.WaitTurn(current.Ticket);
                    try
                    {
                        while (offset < current.Buffer.Length)
                        {
                            if (!IsCurrentConnected(state.Socket))
                            {
                                current.Complete(false);
                                AbandonQueuedSends(state);
                                return;
                            }
                            var count = Math.Min(0x2000,
                                current.Buffer.Length - offset);
                            var sent = await state.Socket.SendAsync(
                                new ArraySegment<byte>(current.Buffer, offset, count),
                                SocketFlags.None).ConfigureAwait(false);
                            if (sent <= 0)
                                throw new SocketException(
                                    (int)SocketError.ConnectionReset);
                            offset += sent;
                        }
                    }
                    finally { state.SendOrder.Complete(current.Ticket); }
                    if (!IsCurrentConnected(state.Socket))
                    {
                        current.Complete(false);
                        current = null;
                        AbandonQueuedSends(state);
                        return;
                    }
                    current.Complete(true);
                    current = null;
                }
            }
            catch (SocketException ex)
            {
                current?.Complete(false);
                AbandonQueuedSends(state);
                RaiseErrorEvent(state.Socket, ex);
                DisconnectSocket(state.Socket);
            }
            catch (ObjectDisposedException)
            {
                current?.Complete(false);
                AbandonQueuedSends(state);
                DisconnectSocket(state.Socket);
            }
        }

        private static void AbandonQueuedSends(QueuedSendState state)
        {
            var abandoned = new List<QueuedSendItem>();
            lock (state.SyncRoot)
            {
                while (state.Queue.Count != 0)
                    abandoned.Add(state.Queue.Dequeue());
                state.IsSending = false;
            }
            foreach (var item in abandoned)
            {
                state.SendOrder.Complete(item.Ticket);
                item.Complete(false);
            }
        }

        public void Disconnect()
        {
            Socket socket;
            lock (_stateLock) socket = cli;
            if (socket != null) DisconnectSocket(socket);
        }

        public void Disconnect(Socket expectedSocket)
        {
            if (expectedSocket != null) DisconnectSocket(expectedSocket);
        }

        public bool IsCurrentConnection(Socket socket) =>
            socket != null && IsCurrentConnected(socket);

        public bool IsCurrentSocket(Socket socket)
        {
            lock (_stateLock)
                return socket != null && ReferenceEquals(cli, socket);
        }

        private bool IsCurrentConnected(Socket socket)
        {
            lock (_stateLock)
                return ReferenceEquals(cli, socket) && IsConnected && socket.Connected;
        }

        private void DisconnectSocket(Socket socket)
        {
            if (socket == null) return;

            DSCClientConnectedEventArgs args = null;
            ClientConnectionLifecycle lifecycle = null;
            var notify = false;
            lock (_stateLock)
            {
                if (ReferenceEquals(cli, socket))
                {
                    try { args = new DSCClientConnectedEventArgs(socket); } catch { }
                    cli = null;
                    _queuedSendState = null;
                    lifecycle = _connectionLifecycle;
                    _connectionLifecycle = null;
                    IsConnected = false;
                    IsBusy = false;
                    notify = _disconnectRaised == 0;
                    _disconnectRaised = 1;
                }
            }

            try { socket.Shutdown(SocketShutdown.Both); } catch { }
            try { socket.Dispose(); } catch { }
            if (notify)
            {
                var eventArgs = args ?? new DSCClientConnectedEventArgs();
                var callback = new Action(() =>
                    OnDisconnected?.Invoke(this, eventArgs));
                if (lifecycle != null) lifecycle.InvokeDisconnect(callback);
                else callback();
            }
        }

        private void RaiseErrorEvent(Socket socket, SocketException error)
        {
            string host;
            int port;
            lock (_stateLock)
            {
                if (socket == null || !ReferenceEquals(cli, socket)) return;
                host = Host;
                port = Port;
            }
            OnError?.Invoke(this, new DSCClientErrorEventArgs(socket, host, port,
                error.ErrorCode, error));
        }

        private sealed class ReceiveState
        {
            public readonly Socket Socket;
            public readonly byte[] Buffer = new byte[BufferSize];

            public ReceiveState(Socket socket) => Socket = socket;
        }

        private sealed class QueuedSendState
        {
            public readonly Socket Socket;
            public readonly object SyncRoot = new object();
            public readonly Queue<QueuedSendItem> Queue = new Queue<QueuedSendItem>();
            public readonly OrderedSendGate SendOrder = new OrderedSendGate();
            public bool IsSending;

            public QueuedSendState(Socket socket) => Socket = socket;
        }

        private sealed class QueuedSendItem
        {
            private readonly Action<bool> _completion;
            private int _completed;

            public readonly byte[] Buffer;
            public long Ticket { get; private set; } = -1;

            public QueuedSendItem(byte[] buffer, Action<bool> completion)
            {
                Buffer = new byte[buffer.Length];
                System.Buffer.BlockCopy(buffer, 0, Buffer, 0, buffer.Length);
                _completion = completion;
            }

            public void Publish(long ticket) => Ticket = ticket;

            public void Complete(bool success)
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0) return;
                if (_completion == null) return;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { _completion(success); }
                    catch { }
                });
            }
        }

        private sealed class OrderedSendGate
        {
            private readonly object _sync = new object();
            private readonly HashSet<long> _completed = new HashSet<long>();
            private long _nextTicket;
            private long _serving;

            public long Reserve()
            {
                lock (_sync) return _nextTicket++;
            }

            public void WaitTurn(long ticket)
            {
                lock (_sync)
                    while (ticket != _serving)
                        Monitor.Wait(_sync);
            }

            public void Complete(long ticket)
            {
                lock (_sync)
                {
                    if (ticket < _serving || !_completed.Add(ticket)) return;
                    while (_completed.Remove(_serving)) _serving++;
                    Monitor.PulseAll(_sync);
                }
            }
        }

        private sealed class ClientConnectionLifecycle
        {
            private readonly object _sync = new object();
            private bool _connectDispatching;
            private bool _disconnectStarted;
            private bool _disconnectNotified;
            private Action _pendingDisconnect;

            public bool InvokeConnect(Action callback)
            {
                lock (_sync)
                {
                    if (_disconnectStarted) return false;
                    _connectDispatching = true;
                }

                try { callback?.Invoke(); }
                finally
                {
                    Action disconnect = null;
                    lock (_sync)
                    {
                        _connectDispatching = false;
                        if (_disconnectStarted && !_disconnectNotified)
                        {
                            _disconnectNotified = true;
                            disconnect = _pendingDisconnect;
                            _pendingDisconnect = null;
                        }
                    }
                    disconnect?.Invoke();
                }
                return true;
            }

            public void InvokeDisconnect(Action callback)
            {
                var invoke = false;
                lock (_sync)
                {
                    if (_disconnectStarted) return;
                    _disconnectStarted = true;
                    if (_connectDispatching)
                    {
                        _pendingDisconnect = callback;
                        return;
                    }
                    _disconnectNotified = true;
                    invoke = true;
                }
                if (invoke) callback?.Invoke();
            }
        }
    }
}
