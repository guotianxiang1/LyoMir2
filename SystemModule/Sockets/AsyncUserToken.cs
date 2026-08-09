using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SystemModule.Sockets
{
    
    
    
    public class AsyncUserToken : EventArgs
    {
        private Socket m_socket;//Socket
        private int m_connectionId;//内部连接ID
        private IPEndPoint m_endPoint;//终结点
        private byte[] m_receiveBuffer;//缓冲区
        private int m_count;
        private int m_offset;//偏移量
        private int m_bytesReceived;//已经接收到的字节数
        private SocketAsyncEventArgs m_readEventArgs;// SocketAsyncEventArgs读对象
        private object m_operation;
        private readonly object m_lifecycleSync = new object();
        private bool m_closing;
        private bool m_disconnectReady;
        private bool m_readReturned;
        private bool m_receiveActive;
        private int m_pendingSends;
        private readonly object m_callbackSync = new object();
        private bool m_connectDispatching;
        private bool m_connectNotified;
        private bool m_disconnectStarted;
        private Action<bool> m_pendingDisconnect;
        private readonly AsyncLocal<object> m_sendOperation = new();

        public AsyncUserToken()
            : this((Socket)null)
        {

        }

        
        
        
        public object Operation
        {
            set { m_operation = value; }
            get { return m_sendOperation.Value ?? m_operation; }
        }

        
        
        
        public byte[] ReceiveBuffer => m_receiveBuffer;

        
        
        
        public int Offset => m_offset;

        
        
        
        public int BytesReceived => m_bytesReceived;

        
        
        
        public SocketAsyncEventArgs ReadEventArgs
        {
            set { m_readEventArgs = value; }
            get { return m_readEventArgs; }
        }

        
        
        
        
        public AsyncUserToken(Socket socket)
        {
            m_readEventArgs = new SocketAsyncEventArgs();
            m_readEventArgs.UserToken = this;
            if (null != socket)
            {
                m_socket = socket;
                this.m_endPoint = (IPEndPoint)socket.RemoteEndPoint;
            }
        }

        internal AsyncUserToken(SocketAsyncEventArgs readEventArgs)
        {
            m_readEventArgs = readEventArgs
                ?? throw new ArgumentNullException(nameof(readEventArgs));
            m_readEventArgs.UserToken = this;
            SetBuffer(m_readEventArgs.Buffer, m_readEventArgs.Offset,
                m_readEventArgs.Count);
        }

        
        
        
        public Socket Socket
        {
            get { return m_socket; }
            set
            {
                if (value != null)
                {
                    m_socket = value;
                    m_endPoint = (IPEndPoint)m_socket.RemoteEndPoint;
                }
            }
        }

        public int SocHandle => (int)Socket.Handle;

        
        
        
        public int ConnectionId//内部连接ID
        {
            get { return this.m_connectionId; }
            set { this.m_connectionId = value; }
        }

        
        
        
        public IPEndPoint EndPoint => this.m_endPoint; 

        
        
        
        public string RemoteIPaddr => EndPoint?.Address.ToString();

        
        
        
        public int RemotePort
        {
            get
            {
                if (EndPoint == null)
                {
                    return 0;
                }
                return EndPoint.Port;
            }
        }

        
        
        
        
        public void SetBytesReceived(int bytesReceived)
        {
            m_bytesReceived = bytesReceived;
        }

        
        
        
        
        
        
        public void SetBuffer(byte[] buffer, int offset, int count)
        {
            m_receiveBuffer = buffer;
            m_offset = offset;
            m_count = count;
            m_bytesReceived = 0;
        }

        internal void ResetConnectionLifecycle()
        {
            lock (m_lifecycleSync)
            {
                m_closing = false;
                m_disconnectReady = false;
                m_readReturned = false;
                m_receiveActive = false;
                m_pendingSends = 0;
                m_operation = null;
            }
            lock (m_callbackSync)
            {
                m_connectDispatching = false;
                m_connectNotified = false;
                m_disconnectStarted = false;
                m_pendingDisconnect = null;
            }
        }

        internal SocketAsyncEventArgs DetachReadEventArgs()
        {
            lock (m_lifecycleSync)
            {
                var result = m_readEventArgs;
                m_readEventArgs = null;
                return result;
            }
        }

        internal bool InvokeConnectNotification(Func<bool> callback)
        {
            lock (m_callbackSync)
            {
                if (m_disconnectStarted) return false;
                m_connectDispatching = true;
                m_connectNotified = true;
            }

            try
            {
                return callback();
            }
            finally
            {
                Action<bool> pending;
                lock (m_callbackSync)
                {
                    m_connectDispatching = false;
                    pending = m_pendingDisconnect;
                    m_pendingDisconnect = null;
                }
                pending?.Invoke(true);
            }
        }

        internal void InvokeDisconnectNotification(Action<bool> callback)
        {
            bool connectNotified;
            lock (m_callbackSync)
            {
                m_disconnectStarted = true;
                if (m_connectDispatching)
                {
                    m_pendingDisconnect = callback;
                    return;
                }
                connectNotified = m_connectNotified;
            }
            callback(connectNotified);
        }

        internal bool InvokeSendNotification(object operation, Func<bool> callback)
        {
            var previous = m_sendOperation.Value;
            m_sendOperation.Value = operation;
            try
            {
                return callback();
            }
            finally { m_sendOperation.Value = previous; }
        }

        internal bool BeginReceiveOperation()
        {
            lock (m_lifecycleSync)
            {
                if (m_closing) return false;
                m_receiveActive = true;
                return true;
            }
        }

        internal void CompleteReceiveOperation()
        {
            lock (m_lifecycleSync) m_receiveActive = false;
        }

        internal bool TryBeginSend()
        {
            lock (m_lifecycleSync)
            {
                if (m_closing) return false;
                m_pendingSends++;
                return true;
            }
        }

        internal void CompleteSend()
        {
            lock (m_lifecycleSync)
                if (m_pendingSends > 0) m_pendingSends--;
        }

        internal void BeginClose()
        {
            lock (m_lifecycleSync) m_closing = true;
        }

        internal void MarkDisconnectReady()
        {
            lock (m_lifecycleSync) m_disconnectReady = true;
        }

        internal bool IsClosing
        {
            get { lock (m_lifecycleSync) return m_closing; }
        }

        internal bool TryMarkReadReusable()
        {
            lock (m_lifecycleSync)
            {
                if (!m_closing || !m_disconnectReady || m_receiveActive
                    || m_pendingSends != 0 || m_readReturned)
                    return false;
                m_readReturned = true;
                return true;
            }
        }
    }
}
