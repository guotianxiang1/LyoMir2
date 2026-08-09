using System;

namespace SystemModule.Sockets
{
    
    
    
    public class AsyncSocketErrorEventArgs : EventArgs
    {
        private AsyncSocketException _exception;

        
        
        
        
        public AsyncSocketErrorEventArgs(AsyncSocketException exception)
        {
            this._exception = exception;
        }

        public AsyncSocketException Exception
        {
            get { return _exception; }
        }
    }
}