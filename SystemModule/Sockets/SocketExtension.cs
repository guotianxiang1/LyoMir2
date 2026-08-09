using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SystemModule.Sockets
{
    public static class SocketExtension
    {
        public static bool Send(this Socket socket, string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return false;
            }
            if (socket.Connected)
            {
                var buff = HUtil32.GbkEncoding.GetBytes(str);
                return SendAll(socket, buff);
            }
            return false;
        }

        public static bool SendText(this Socket socket, string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return false;
            }
            if (socket.Connected)
            {
                var buff = HUtil32.GbkEncoding.GetBytes(str);
                return SendAll(socket, buff);
            }
            return false;
        }

        private static bool SendAll(Socket socket, byte[] buffer)
        {
            try
            {
                lock (socket)
                {
                    var offset = 0;
                    while (offset < buffer.Length)
                    {
                        var sent = socket.Send(buffer, offset, buffer.Length - offset, SocketFlags.None);
                        if (sent <= 0) return false;
                        offset += sent;
                    }
                }
                return true;
            }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }

        public static string GetIPAddress(this EndPoint endPoint)
        {
            if (endPoint == null)
            {
                throw new Exception("endPoint is null");
            }
            var ipEndPoint = ((IPEndPoint)endPoint);
            return ipEndPoint.ToString();
        }

        public static int GetPort(this EndPoint endPoint)
        {
            if (endPoint == null)
            {
                throw new Exception("endPoint is null");
            }
            var ipEndPoint = ((IPEndPoint)endPoint);
            return ipEndPoint.Port;
        }
    }
}
