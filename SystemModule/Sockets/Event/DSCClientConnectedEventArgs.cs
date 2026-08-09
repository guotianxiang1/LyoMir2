using System;
using System.Net;
using System.Net.Sockets;

namespace SystemModule.Sockets
{
    public class DSCClientConnectedEventArgs : EventArgs
    {
        public Socket socket;
        public string RemoteAddress;
        public int RemotePort;

        public DSCClientConnectedEventArgs()
        {
        }

        public DSCClientConnectedEventArgs(Socket soc)
        {
            socket = soc;
            if (soc == null || soc.Connected == false)
            {
                return;
            }
            if (soc.RemoteEndPoint != null)
            {
                var endPoint = (IPEndPoint)soc.RemoteEndPoint;
                if (endPoint != null)
                {
                    this.RemoteAddress = endPoint.Address?.ToString();
                    this.RemotePort = endPoint.Port;
                }
            }
        }
    }
}
