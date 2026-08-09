using System;
using System.Net.Sockets;

namespace SystemModule.Sockets
{
    
    
    
    public class AsyncSocketException : Exception
    {
        private const string m_asyncSocketException = "异步通讯中发生通讯异常.";
        private readonly AsyncSocketErrorCode m_errorCode;

        
        
        
        public AsyncSocketException() :
            base(m_asyncSocketException)
        {
            m_errorCode = AsyncSocketErrorCode.ServerStartFailure;
        }

        public AsyncSocketException(string message, SocketException socketException) :
            base(String.Format("{0} - {1}",
            message, m_asyncSocketException), socketException)
        {
            m_errorCode = AsyncSocketErrorCode.ThrowSocketException;
        }

        
        
        
        
        
        
        
        
        public AsyncSocketException(string message, AsyncSocketErrorCode errorCode) :
            base(String.Format("{0} - {1}",
          message, m_asyncSocketException))
        {
            m_errorCode = errorCode;
        }

        
        
        
        public AsyncSocketErrorCode ErrorCode
        {
            get
            {
                return m_errorCode;
            }
        }
    }
}