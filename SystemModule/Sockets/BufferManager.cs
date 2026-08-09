using System.Collections.Generic;
using System.Net.Sockets;

namespace SystemModule.Sockets
{
    
    
    
    
    
    internal class BufferManager
    {
        private readonly int m_numBytes;                 
        private byte[] m_buffer;                
        private readonly Stack<int> m_freeIndexPool;     
        private int m_currentIndex;             
        private readonly int m_bufferSize;               

        
        
        
        
        
        public BufferManager(int totalBytes, int bufferSize)
        {
            m_numBytes = totalBytes;
            m_currentIndex = 0;
            m_bufferSize = bufferSize;
            m_freeIndexPool = new Stack<int>();
        }

        
        
        
        public void InitBuffer()
        {
            
            m_buffer = new byte[m_numBytes];
        }

        
        
        
        
        public bool SetBuffer(SocketAsyncEventArgs args)
        {
            if (m_freeIndexPool.Count > 0)
            {
                args.SetBuffer(m_buffer, m_freeIndexPool.Pop(), m_bufferSize);
            }
            else
            {
                if ((m_numBytes - m_bufferSize) < m_currentIndex)
                {
                    return false;
                }
                args.SetBuffer(m_buffer, m_currentIndex, m_bufferSize);
                m_currentIndex += m_bufferSize;
            }
            return true;
        }

        
        
        
        public void FreeBuffer(SocketAsyncEventArgs args)
        {
            m_freeIndexPool.Push(args.Offset);
            args.SetBuffer(null, 0, 0);
        }
    }
}