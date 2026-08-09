using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace SystemModule.Sockets
{
    
    
    
    internal class SocketAsyncEventArgsPool
    {
        private readonly Stack<SocketAsyncEventArgs> m_pool;

        
        
        
        
        public SocketAsyncEventArgsPool(int capacity)
        {
            m_pool = new Stack<SocketAsyncEventArgs>(capacity);
        }

        
        
        
        
        public void Push(SocketAsyncEventArgs item)
        {
            if (item == null) { throw new ArgumentNullException("要被添加到SocketAsyncEventArgs池的项目不能为空(null)"); }
            lock (m_pool)
            {
                m_pool.Push(item);
            }
        }

        
        
        
        
        public SocketAsyncEventArgs Pop()
        {
            lock (m_pool)
            {
                return m_pool.Pop();
            }
        }

        
        
        
        public int Count
        {
            get { return m_pool.Count; }
        }
    }
}