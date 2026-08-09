using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SystemModule.Sockets
{
    
    
    
    public class ISocketServer
    {
        private IdWorker _idWorker;
        
        
        
        private Object m_bufferLock = new Object();
        
        
        
        private Object m_readPoolLock = new Object();
        
        
        
        private Object m_writePoolLock = new Object();
        private readonly object m_lifecycleLock = new object();
        
        
        
        ConcurrentDictionary<long, AsyncUserToken> m_tokens;
        
        
        
        int m_numConnections;
        
        
        
        int m_BufferSize;
        
        
        
        BufferManager m_bufferManager;
        
        
        
        const int opsToPreAlloc = 2;
        
        
        
        Socket listenSocket;
        long listenGeneration;
        
        
        
        SocketAsyncEventArgsPool m_readPool;
        
        
        
        SocketAsyncEventArgsPool m_writePool;
        volatile bool isActive = false;
        bool isStopping;

        private sealed class SendContext
        {
            internal readonly AsyncUserToken Token;
            internal readonly object Operation;
            private int _completed;

            internal SendContext(AsyncUserToken token, object operation)
            {
                Token = token;
                Operation = operation;
            }

            internal bool TryComplete() => Interlocked.Exchange(ref _completed, 1) == 0;
        }
        
        
        
        
        long m_totalBytesRead;
        
        
        
        long m_totalBytesWrite;
        
        
        
        long m_numConnectedSockets;
        
        
        
        Semaphore m_maxNumberAcceptedClients;

        
        
        
        public long NumConnectedSockets
        {
            get { return m_numConnectedSockets; }
        }
        
        
        
        public long TotalBytesRead
        {
            get { return m_totalBytesRead; }
        }
        
        
        
        public long TotalBytesWrite
        {
            get { return m_totalBytesWrite; }
        }
        
        
        
        public event EventHandler<AsyncUserToken> OnClientConnect;
        
        
        
        public event EventHandler<AsyncSocketErrorEventArgs> OnClientError;
        
        
        
        public event EventHandler<AsyncUserToken> OnClientRead;
        
        
        
        public event EventHandler<AsyncUserToken> OnDataSendCompleted;
        
        
        
        public event EventHandler<AsyncUserToken> OnClientDisconnect;

        
        
        
        
        
        public bool IsOnline(int connectionId)
        {
            if (!this.m_tokens.ContainsKey(connectionId))
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        public IList<AsyncUserToken> GetSockets()
        {
            return this.m_tokens.Values.ToList();
        }

        public IList<Socket> GetClientSockets()
        {
            lock (m_lifecycleLock)
                return this.m_tokens.Values.Select(token => token.Socket).ToList();
        }

        
        
        
        
        
        public ISocketServer(int numConnections, int BufferSize)//构造函数
        {
            
            m_totalBytesRead = 0;
            m_totalBytesWrite = 0;
            m_numConnectedSockets = 0;
            
            m_numConnections = numConnections;
            
            m_BufferSize = BufferSize;

            

            m_bufferManager = new BufferManager(BufferSize * numConnections * opsToPreAlloc, BufferSize);

            
            m_readPool = new SocketAsyncEventArgsPool(numConnections);
            m_writePool = new SocketAsyncEventArgsPool(numConnections);

            
            m_tokens = new ConcurrentDictionary<long, AsyncUserToken>();

            
            m_maxNumberAcceptedClients = new Semaphore(numConnections, numConnections);

            _idWorker = new IdWorker(new Random().Next(10));
        }

        
        
        
        public void Init()
        {
            
            m_bufferManager.InitBuffer();

            
            SocketAsyncEventArgs readWriteEventArg;
            AsyncUserToken token;
            

            
            for (int i = 0; i < m_numConnections; i++)
            {
                
                
                
                
                token = new AsyncUserToken();
                
                
                
                token.ReadEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);
                

                
                
                m_bufferManager.SetBuffer(token.ReadEventArgs);
                
                
                token.SetBuffer(token.ReadEventArgs.Buffer, token.ReadEventArgs.Offset, token.ReadEventArgs.Count);
                
                
                m_readPool.Push(token.ReadEventArgs);
            }
            
            for (int i = 0; i < m_numConnections; i++)
            {
                
                readWriteEventArg = new SocketAsyncEventArgs();
                readWriteEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);
                readWriteEventArg.UserToken = null;

                
                m_bufferManager.SetBuffer(readWriteEventArg);
                

                
                m_writePool.Push(readWriteEventArg);
            }
        }

        
        
        
        
        
        public void Start(string ip, int port)
        {
            lock (m_lifecycleLock)
            {
                if (isStopping)
                    throw new InvalidOperationException("socket server is stopping");
                if (isActive) return;
                isActive = true;
                try
                {
                    if (ip == "*" || ip == "all")
                        Start(new IPEndPoint(IPAddress.Any, port));
                    else
                        Start(new IPEndPoint(IPAddress.Parse(ip), port));
                }
                catch
                {
                    isActive = false;
                    throw;
                }
            }
            StartAccept(null);
        }

        
        
        
        
        
        public void Start(int port)
        {
            lock (m_lifecycleLock)
            {
                if (isStopping)
                    throw new InvalidOperationException("socket server is stopping");
                if (isActive) return;
                isActive = true;
                try { Start(new IPEndPoint(IPAddress.Any, port)); }
                catch { isActive = false; throw; }
            }
            StartAccept(null);
        }

        public bool Active
        {
            get { return isActive; }
        }

        
        
        
        
        private void Start(IPEndPoint localEndPoint)// 启动
        {
            try
            {
                
                try { listenSocket?.Dispose(); } catch { }
                listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                listenSocket.Bind(localEndPoint);
                Interlocked.Increment(ref listenGeneration);
                
                listenSocket.NoDelay = false;
                listenSocket.ReceiveBufferSize = 4096;
                listenSocket.SendBufferSize = 4096;
                listenSocket.ReceiveTimeout = 20000;
                listenSocket.SendTimeout = 10000;
                listenSocket.Listen(1000);
            }
            catch (ObjectDisposedException)
            {

            }
            catch (SocketException ex)
            {
                
                
                if (ex.ErrorCode == (int)SocketError.AddressNotAvailable)
                {
                    throw new AsyncSocketException(ex.Message, ex);
                }
                else if (ex.ErrorCode == 48 || ex.ErrorCode == 10048)
                {
                    throw new AsyncSocketException("Socket端口被占用", AsyncSocketErrorCode.ServerStartFailure);
                }
                else
                {
                    throw new AsyncSocketException("服务器启动失败", AsyncSocketErrorCode.ServerStartFailure);
                }
            }
            catch (Exception exception_debug)
            {
                Debug.WriteLine("调试：" + exception_debug.Message);
                throw exception_debug;
            }
            

            Debug.WriteLine("服务器启动成功....");
        }

        
        
        
        
        private void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (!isActive)
            {
                acceptEventArg?.Dispose();
                return;
            }
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
                acceptEventArg.UserToken = Volatile.Read(ref listenGeneration);
            }
            else
            {
                if (!Equals(acceptEventArg.UserToken, Volatile.Read(ref listenGeneration)))
                {
                    acceptEventArg.Dispose();
                    return;
                }
                
                acceptEventArg.AcceptSocket = null;
            }
            bool permitAcquired = false;
            try
            {
                m_maxNumberAcceptedClients.WaitOne();
                permitAcquired = true;
                Socket listener;
                lock (m_lifecycleLock)
                {
                    if (!isActive
                        || !Equals(acceptEventArg.UserToken, listenGeneration)
                        || listenSocket == null)
                    {
                        m_maxNumberAcceptedClients.Release();
                        permitAcquired = false;
                        acceptEventArg.Dispose();
                        return;
                    }
                    listener = listenSocket;
                }
                bool willRaiseEvent = listener.AcceptAsync(acceptEventArg);
                if (!willRaiseEvent)
                {
                    ProcessAccept(acceptEventArg);
                }
            }
            catch (ObjectDisposedException)
            {
                if (permitAcquired) m_maxNumberAcceptedClients.Release();
                acceptEventArg.Dispose();
            }
            catch (SocketException socketException)
            {
                if (permitAcquired) m_maxNumberAcceptedClients.Release();
                RaiseErrorEvent(null, new AsyncSocketException("服务器接受客户端请求发生一次异常", socketException));
                if (isActive) ThreadPool.QueueUserWorkItem(_ => StartAccept(acceptEventArg));
                else acceptEventArg.Dispose();
            }
            catch (Exception exception_debug)
            {
                if (permitAcquired) m_maxNumberAcceptedClients.Release();
                Debug.WriteLine("调试：" + exception_debug.Message);
                if (isActive) ThreadPool.QueueUserWorkItem(_ => StartAccept(acceptEventArg));
                else acceptEventArg.Dispose();
            }
        }

        
        
        
        void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            var acceptGeneration = e.UserToken is long value ? value : 0;
            if (acceptGeneration != Volatile.Read(ref listenGeneration))
            {
                try { e.AcceptSocket?.Dispose(); } catch { }
                try { m_maxNumberAcceptedClients.Release(); } catch (SemaphoreFullException) { }
                e.Dispose();
                return;
            }
            SocketAsyncEventArgs readEventArg = null;
            AsyncUserToken token = null;
            bool registered = false;
            try
            {
                if (e.SocketError != SocketError.Success || e.AcceptSocket == null)
                {
                    e.AcceptSocket?.Dispose();
                    m_maxNumberAcceptedClients.Release();
                    return;
                }

                lock (m_readPool)
                {
                    readEventArg = m_readPool.Pop();
                }

                token = (AsyncUserToken)readEventArg.UserToken;
                token.ResetConnectionLifecycle();
                token.Socket = e.AcceptSocket;
                token.ConnectionId = (int)_idWorker.nextId();//Guid.NewGuid().ToString("N");
                if ((token.ConnectionId <= 0 || token.ConnectionId > ushort.MaxValue) && token.Socket != null)
                {
                    token.ConnectionId = (int)token.Socket.Handle;
                }
                if (token.ConnectionId <= 0 || token.ConnectionId > ushort.MaxValue)
                {
                    Console.WriteLine("生成SocketId异常.");
                    RejectAcceptedSocket(token.Socket, readEventArg);
                    readEventArg = null;
                    return;
                }
                lock (m_lifecycleLock)
                {
                    if (!isActive || acceptGeneration != listenGeneration
                        || !token.BeginReceiveOperation()
                        || !this.m_tokens.TryAdd(token.ConnectionId, token))
                    {
                        Console.WriteLine("Socket链接异常");
                        RejectAcceptedSocket(token.Socket, readEventArg);
                        readEventArg = null;
                        return;
                    }
                    registered = true;
                    Interlocked.Increment(ref m_numConnectedSockets);
                }

                EventHandler<AsyncUserToken> handler = OnClientConnect;
                if (!token.InvokeConnectNotification(
                        () => InvokeClientEvent(handler, token, "connect")))
                    throw new InvalidOperationException("client connect handler failed");

                if (token.IsClosing)
                {
                    FinishReceiveOperation(token);
                    return;
                }

                bool willRaiseEvent = token.Socket.ReceiveAsync(readEventArg);
                if (!willRaiseEvent)
                {
                    ProcessReceive(readEventArg);
                }
            }
            catch (ObjectDisposedException)
            {
                if (registered)
                {
                    RaiseDisconnectedEvent(token);
                    FinishReceiveOperation(token);
                }
                else RejectAcceptedSocket(e.AcceptSocket, readEventArg);
            }
            catch (SocketException socketException)
            {
                if (registered)
                {
                    if (socketException.ErrorCode != (int)SocketError.ConnectionReset)
                        RaiseErrorEvent(token, new AsyncSocketException("在SocketAsyncEventArgs对象上执行异步接收数据操作时发生SocketException异常", socketException));
                    RaiseDisconnectedEvent(token);
                    FinishReceiveOperation(token);
                }
                else RejectAcceptedSocket(e.AcceptSocket, readEventArg);
            }
            catch (Exception exception_debug)
            {
                Debug.WriteLine("调试：" + exception_debug.Message);
                if (registered)
                {
                    RaiseDisconnectedEvent(token);
                    FinishReceiveOperation(token);
                }
                else RejectAcceptedSocket(e.AcceptSocket, readEventArg);
            }
            finally
            {
                if (isActive && acceptGeneration == Volatile.Read(ref listenGeneration)) StartAccept(e);
                else e.Dispose();
            }
        }

        private void RejectAcceptedSocket(Socket socket, SocketAsyncEventArgs readEventArg)
        {
            try { socket?.Dispose(); }
            catch { }
            if (readEventArg != null)
            {
                lock (m_readPool) m_readPool.Push(readEventArg);
            }
            m_maxNumberAcceptedClients.Release();
        }

        
        
        
        
        void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
                default:
                    throw new ArgumentException("最后一次在Socket上的操作不是接收或者发送操作");
            }
        }

        
        
        
        
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            if (token.IsClosing)
            {
                FinishReceiveOperation(token);
                return;
            }

            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                Interlocked.Add(ref m_totalBytesRead, e.BytesTransferred);
                Debug.WriteLine($"服务器读取字节总数:{BytesToReadableValue(m_totalBytesRead)}");
                token.SetBytesReceived(e.BytesTransferred);

                EventHandler<AsyncUserToken> handler = OnClientRead;
                if (!InvokeClientEvent(handler, token, "read"))
                {
                    RaiseDisconnectedEvent(token);
                    FinishReceiveOperation(token);
                    return;
                }
                if (token.IsClosing)
                {
                    FinishReceiveOperation(token);
                    return;
                }

                try
                {
                    var willRaiseEvent = token.Socket.ReceiveAsync(e);
                    if (!willRaiseEvent)
                        ProcessReceive(e);
                }
                catch (ObjectDisposedException)
                {
                    RaiseDisconnectedEvent(token);
                    FinishReceiveOperation(token);
                }
                catch (SocketException socketException)
                {
                    if (socketException.ErrorCode != (int)SocketError.ConnectionReset)
                        RaiseErrorEvent(token, new AsyncSocketException("在SocketAsyncEventArgs对象上执行异步接收数据操作时发生SocketException异常", socketException));
                    RaiseDisconnectedEvent(token);//引发断开连接事件
                    FinishReceiveOperation(token);
                }
                catch (Exception exception_debug)
                {
                    Debug.WriteLine("调试：" + exception_debug.Message);
                    RaiseDisconnectedEvent(token);
                    FinishReceiveOperation(token);
                }
            }
            else
            {
                RaiseDisconnectedEvent(token);
                FinishReceiveOperation(token);
            }
        }

        public void SendAsync(int connectionId, byte[] buffer)
        {
            AsyncUserToken token;
            
            
            
            
            
            if (!this.m_tokens.TryGetValue(connectionId, out token))
            {
                throw new AsyncSocketException($"客户端:{connectionId}已经关闭或者未连接", AsyncSocketErrorCode.ClientSocketNoExist);
                
            }
            SocketAsyncEventArgs writeEventArgs;
            lock (m_writePool)
            {
                writeEventArgs = m_writePool.Pop();// 分配一个写SocketAsyncEventArgs对象
            }
            var sendContext = new SendContext(token, null);
            writeEventArgs.UserToken = sendContext;
            if (!token.TryBeginSend())
            {
                ReturnWriteEventArgs(writeEventArgs);
                throw new AsyncSocketException($"客户端:{connectionId}已经关闭或者未连接",
                    AsyncSocketErrorCode.ClientSocketNoExist);
            }
            try
            {
                if (buffer.Length <= m_BufferSize)
                {
                    Array.Copy(buffer, 0, writeEventArgs.Buffer,
                        writeEventArgs.Offset, buffer.Length);
                    writeEventArgs.SetBuffer(writeEventArgs.Buffer,
                        writeEventArgs.Offset, buffer.Length);
                }
                else
                {
                    lock (m_bufferLock) m_bufferManager.FreeBuffer(writeEventArgs);
                    writeEventArgs.SetBuffer(buffer, 0, buffer.Length);
                }
            }
            catch
            {
                sendContext.TryComplete();
                token.CompleteSend();
                ReturnWriteEventArgs(writeEventArgs);
                TryReturnReadToken(token);
                throw;
            }

            
            bool willRaiseEvent;
            try
            {
                willRaiseEvent = token.Socket.SendAsync(writeEventArgs);
            }
            catch (ObjectDisposedException)
            {
                sendContext.TryComplete();
                RaiseDisconnectedEvent(token);
                token.CompleteSend();
                ReturnWriteEventArgs(writeEventArgs);
                TryReturnReadToken(token);
                return;
            }
            catch (SocketException socketException)
            {
                sendContext.TryComplete();
                if (socketException.ErrorCode == (int)SocketError.ConnectionReset)//10054一个建立的?颖辉冻讨骰?啃泄乇蕴
                {
                    RaiseDisconnectedEvent(token);//引发断开连接事件
                }
                else
                {
                    RaiseErrorEvent(token, new AsyncSocketException("在SocketAsyncEventArgs对象上执行异步发送数据操作时发生SocketException异常", socketException)); ;
                }
                token.CompleteSend();
                ReturnWriteEventArgs(writeEventArgs);
                TryReturnReadToken(token);
                return;
            }
            catch (Exception exception_debug)
            {
                sendContext.TryComplete();
                token.CompleteSend();
                ReturnWriteEventArgs(writeEventArgs);
                TryReturnReadToken(token);
                Debug.WriteLine("调试：" + exception_debug.Message);
                throw;
            }
            if (!willRaiseEvent) ProcessSend(writeEventArgs);
        }

        
        
        
        
        
        
        public void SendAsync(int connectionId, byte[] buffer, object operation)
        {
            AsyncUserToken token;
            
            
            
            
            
            if (!this.m_tokens.TryGetValue(connectionId, out token))
            {
                throw new AsyncSocketException($"客户端:{connectionId}已经关闭或者未连接", AsyncSocketErrorCode.ClientSocketNoExist);
                
            }
            SocketAsyncEventArgs writeEventArgs;
            lock (m_writePool)
            {
                writeEventArgs = m_writePool.Pop();// 分配一个写SocketAsyncEventArgs对象
            }
            var sendContext = new SendContext(token, operation);
            writeEventArgs.UserToken = sendContext;
            if (!token.TryBeginSend())
            {
                ReturnWriteEventArgs(writeEventArgs);
                throw new AsyncSocketException($"客户端:{connectionId}已经关闭或者未连接",
                    AsyncSocketErrorCode.ClientSocketNoExist);
            }
            try
            {
                if (buffer.Length <= m_BufferSize)
                {
                    Array.Copy(buffer, 0, writeEventArgs.Buffer,
                        writeEventArgs.Offset, buffer.Length);
                    writeEventArgs.SetBuffer(writeEventArgs.Buffer,
                        writeEventArgs.Offset, buffer.Length);
                }
                else
                {
                    lock (m_bufferLock) m_bufferManager.FreeBuffer(writeEventArgs);
                    writeEventArgs.SetBuffer(buffer, 0, buffer.Length);
                }
            }
            catch
            {
                sendContext.TryComplete();
                token.CompleteSend();
                ReturnWriteEventArgs(writeEventArgs);
                TryReturnReadToken(token);
                throw;
            }

            
            bool willRaiseEvent;
            try
            {
                willRaiseEvent = token.Socket.SendAsync(writeEventArgs);
            }
            catch (ObjectDisposedException)
            {
                sendContext.TryComplete();
                RaiseDisconnectedEvent(token);
                token.CompleteSend();
                ReturnWriteEventArgs(writeEventArgs);
                TryReturnReadToken(token);
                return;
            }
            catch (SocketException socketException)
            {
                sendContext.TryComplete();
                if (socketException.ErrorCode == (int)SocketError.ConnectionReset)//10054一个建立的连接被远程主机强行关闭
                {
                    RaiseDisconnectedEvent(token);//引发断开连接事件
                }
                else
                {
                    RaiseErrorEvent(token, new AsyncSocketException("在SocketAsyncEventArgs对象上执行异步发送数据操作时发生SocketException异常", socketException)); ;
                }
                token.CompleteSend();
                ReturnWriteEventArgs(writeEventArgs);
                TryReturnReadToken(token);
                return;
            }
            catch (Exception exception_debug)
            {
                sendContext.TryComplete();
                token.CompleteSend();
                ReturnWriteEventArgs(writeEventArgs);
                TryReturnReadToken(token);
                Debug.WriteLine("调试：" + exception_debug.Message);
                throw;
            }
            if (!willRaiseEvent) ProcessSend(writeEventArgs);
        }

        
        
        
        
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            
            var context = (SendContext)e.UserToken;
            if (!context.TryComplete()) return;
            AsyncUserToken token = context.Token;
            
            var bytesTransferred = e.BytesTransferred;
            var socketError = e.SocketError;
            Interlocked.Add(ref m_totalBytesWrite, bytesTransferred);
            ReturnWriteEventArgs(e);

            if (socketError == SocketError.Success)
            {
                Debug.WriteLine($"发送总字节数:{BytesToReadableValue(bytesTransferred)}");
                
                
                
                
                
                
                
                
                
                token.InvokeSendNotification(context.Operation,
                    () => InvokeClientEvent(OnDataSendCompleted, token, "send-completed"));

                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
                
            }
            else
            {
                
                
                
                
                
                
                
                

                RaiseDisconnectedEvent(token);//引发断开连接事件
            }
            token.CompleteSend();
            TryReturnReadToken(token);
        }

        public void Disconnect(int connectionId)//断开连接(形参 连接ID)
        {
            AsyncUserToken token;
            if (!this.m_tokens.TryGetValue(connectionId, out token))
            {
                throw new AsyncSocketException($"客户端:{connectionId}已经关闭或者未连接", AsyncSocketErrorCode.ClientSocketNoExist);
                
            }
            RaiseDisconnectedEvent(token);//抛出断开连接事件            
        }

        private void RaiseDisconnectedEvent(AsyncUserToken token)//引发断开连接事件
        {
            if (token == null) return;
            AsyncUserToken removedToken;
            lock (m_lifecycleLock)
            {
                if (!this.m_tokens.TryGetValue(token.ConnectionId, out var current)
                    || !ReferenceEquals(current, token))
                    return;
                token.BeginClose();
                if (!this.m_tokens.TryRemove(token.ConnectionId, out removedToken)
                    || !ReferenceEquals(removedToken, token))
                    return;
            }

            CloseClientSocket(removedToken);
            Interlocked.Decrement(ref m_numConnectedSockets);
            removedToken.InvokeDisconnectNotification(connectNotified =>
            {
                try
                {
                    if (connectNotified)
                        InvokeClientEvent(OnClientDisconnect, removedToken, "disconnect");
                }
                finally
                {
                    removedToken.MarkDisconnectReady();
                    TryReturnReadToken(removedToken);
                }
            });
        }

        private void CloseClientSocket(AsyncUserToken token)
        {
            
            if (token == null)
            {
                return;
            }

            
            try
            {
                token.Socket.Shutdown(SocketShutdown.Both);
                token.Socket.Close();
            }
            
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
                try { token.Socket.Close(); } catch { }
            }
            catch (Exception exception_debug)
            {
                try { token.Socket.Close(); } catch { }
                Debug.WriteLine("调试:" + exception_debug.Message);
            }
        }

        private void RaiseErrorEvent(AsyncUserToken token, AsyncSocketException exception)
        {
            EventHandler<AsyncSocketErrorEventArgs> handler = OnClientError;
            if (handler == null) return;
            var eventArgs = new AsyncSocketErrorEventArgs(exception);
            foreach (EventHandler<AsyncSocketErrorEventArgs> subscriber
                     in handler.GetInvocationList())
            {
                try { subscriber(token, eventArgs); }
                catch (Exception ex) { Debug.WriteLine("socket error handler: " + ex.Message); }
            }
        }

        private bool InvokeClientEvent(EventHandler<AsyncUserToken> handler,
            AsyncUserToken token, string eventName)
        {
            if (handler == null) return true;
            var success = true;
            foreach (EventHandler<AsyncUserToken> subscriber in handler.GetInvocationList())
            {
                try { subscriber(this, token); }
                catch (Exception ex)
                {
                    success = false;
                    Debug.WriteLine($"socket {eventName} handler: {ex.Message}");
                }
            }
            return success;
        }

        private void FinishReceiveOperation(AsyncUserToken token)
        {
            if (token == null) return;
            token.CompleteReceiveOperation();
            TryReturnReadToken(token);
        }

        private void TryReturnReadToken(AsyncUserToken token)
        {
            if (token == null || !token.TryMarkReadReusable()) return;
            var readEventArgs = token.DetachReadEventArgs();
            if (readEventArgs == null) return;
            _ = new AsyncUserToken(readEventArgs);
            lock (m_readPool) m_readPool.Push(readEventArgs);
            m_maxNumberAcceptedClients.Release();
        }

        private void ReturnWriteEventArgs(SocketAsyncEventArgs eventArgs)
        {
            if (eventArgs == null) return;
            if (eventArgs.Count > m_BufferSize)
            {
                lock (m_bufferLock) m_bufferManager.SetBuffer(eventArgs);
            }
            eventArgs.UserToken = null;
            lock (m_writePool) m_writePool.Push(eventArgs);
        }

        public void Shutdown()
        {
            Socket listener;
            List<AsyncUserToken> stoppingTokens;
            lock (m_lifecycleLock)
            {
                if (isStopping) return;
                isStopping = true;
                isActive = false;
                Interlocked.Increment(ref listenGeneration);
                listener = Interlocked.Exchange(ref listenSocket, null);
                stoppingTokens = this.m_tokens.Values.ToList();
            }
            try { listener?.Close(); } catch { }
            foreach (AsyncUserToken token in stoppingTokens)
            {
                try
                {
                    RaiseDisconnectedEvent(token);
                }
                
                
                
                catch (Exception exception_debug)
                {
                    Debug.WriteLine("调试:" + exception_debug.Message);
                }
            }
            lock (m_lifecycleLock) isStopping = false;
        }

        
        
        
        
        private string BytesToReadableValue(long length)
        {
            int byteConversion = 1024;
            double bytes = Convert.ToDouble(length);
            
            if (bytes >= Math.Pow(byteConversion, 6)) 
            {
                return string.Concat(Math.Round(bytes / Math.Pow(byteConversion, 6), 2), " EB");
            }
            if (bytes >= Math.Pow(byteConversion, 5)) 
            {
                return string.Concat(Math.Round(bytes / Math.Pow(byteConversion, 5), 2), " PB");
            }
            if (bytes >= Math.Pow(byteConversion, 4)) 
            {
                return string.Concat(Math.Round(bytes / Math.Pow(byteConversion, 4), 2), " TB");
            }
            if (bytes >= Math.Pow(byteConversion, 3)) 
            {
                return string.Concat(Math.Round(bytes / Math.Pow(byteConversion, 3), 2), " GB");
            }
            if (bytes >= Math.Pow(byteConversion, 2)) 
            {
                return string.Concat(Math.Round(bytes / Math.Pow(byteConversion, 2), 2), " MB");
            }
            if (bytes >= byteConversion) 
            {
                return string.Concat(Math.Round(bytes / byteConversion, 2), " KB");
            }
            return string.Concat(bytes, " Bytes");// Bytes
        }
    }
}
