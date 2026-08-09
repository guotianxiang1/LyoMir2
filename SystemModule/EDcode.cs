using System;
using SystemModule.Packet;

namespace SystemModule
{
    public class EDcode
    {
        private const int BUFFERSIZE = 200000; // 原 10000 太小, 角色数据 ProtoBuf 序列化可达 11KB+
        public const int LegendDefBlockSize = 22;




        public static ClientPacket DecodePacket(string str)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));
            var tempBuf = HUtil32.GetBytes(str);
            var encBuf = Misc.Decode6BitBufDirect(tempBuf, str.Length);
            return Packets.ToPacket<ClientPacket>(encBuf);
        }




        public static ClientPacket DecodePacket(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var encBuf = Misc.Decode6BitBufDirect(data, data.Length);
            return Packets.ToPacket<ClientPacket>(encBuf);
        }

        public static byte[] DecodeBuff(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Misc.Decode6BitBufDirect(data, data.Length);
        }




        public static string DeCodeString(string str)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));
            var bSrc = HUtil32.GetBytes(str);
            var encBuf = Misc.Decode6BitBufDirect(bSrc, bSrc.Length);
            return HUtil32.GetString(encBuf, 0, encBuf.Length);
        }

        public static byte[] DecodeBuffer(string strSrc)
        {
            if (strSrc == null) throw new ArgumentNullException(nameof(strSrc));
            var bSrc = HUtil32.GetBytes(strSrc);
            return Misc.Decode6BitBufDirect(bSrc, bSrc.Length);
        }

        public static byte[] DecodeBuffer(string src, int size)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            var bSrc = HUtil32.GetBytes(src);
            return Misc.Decode6BitBufDirect(bSrc, bSrc.Length);
        }





        public static string EncodeString(string str)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));
            // Use no-XOR 6-bit encoding matching C++ LegendMir5 client Decode6BitBuf.
            var bSrc = HUtil32.GetBytes(str);
            var encBuf = new byte[bSrc.Length * 2 + 4];
            var destLen = Misc.Encode6BitBufDirect(bSrc, bSrc.Length, encBuf);
            return HUtil32.GetString(encBuf, 0, destLen);
        }

        public static string EncodeBuffer<T>(T obj) where T : Packets, new()
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            var result = string.Empty;
            var data = obj.GetBuffer();
            var buffSize = data.Length;
            if (buffSize <= 0) return result;
            if (buffSize < BUFFERSIZE)
            {
                var encBuf = new byte[buffSize * 2 + 4];
                var tempBuf = new byte[buffSize];
                Buffer.BlockCopy(data, 0, tempBuf, 0, buffSize);
                var destLen = Misc.Encode6BitBufDirect(tempBuf, buffSize, encBuf);
                return HUtil32.GetString(encBuf, 0, destLen);
            }
            return result;
        }

        
        
        
        
        public static byte[] EncodeBuffer(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var buffSize = data.Length;
            if (buffSize >= BUFFERSIZE) return Array.Empty<byte>();
            var encBuf = new byte[buffSize * 2 + 4];
            var destLen = Misc.Encode6BitBufDirect(data, buffSize, encBuf);
            return encBuf[..destLen];
        }

        
        
        
        public static string EncodeBuffer(byte[] data, int bufsize)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var tempBuf = new byte[data.Length];
            var encBuf = new byte[tempBuf.Length * 2 + 4];
            if (bufsize < BUFFERSIZE)
            {
                Buffer.BlockCopy(data, 0, tempBuf, 0, bufsize);
                var destLen = Misc.Encode6BitBufDirect(tempBuf, bufsize, encBuf);
                return HUtil32.GetString(encBuf, 0, destLen);
            }
            return string.Empty;
        }

        
        
        
        
        public static int EncodeMessage(byte[] msgBuf, ref byte[] encBuff)
        {
            if (msgBuf == null) throw new ArgumentNullException(nameof(msgBuf));
            return Misc.Encode6BitBufDirect(msgBuf, 12, encBuff);
        }

        
        
        
        
        public static string EncodeMessage(ClientPacket packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            var packetData = packet.GetBuffer();
            var encBuf = new byte[packetData.Length * 2 + 4];
            var destLen = Misc.Encode6BitBufDirect(packetData, ClientPacket.PackSize, encBuf);
            return HUtil32.GetString(encBuf, 0, destLen);
        }

        public static LegendClientPacket DecodeLegendPacket(string str)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));
            var tempBuf = HUtil32.GetBytes(str);
            var encBuf = Misc.Decode6BitBufDirect(tempBuf, str.Length);
            return Packets.ToPacket<LegendClientPacket>(encBuf);
        }

        public static string EncodeLegendMessage(ClientPacket packet, int sessionId)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));

            var legendPacket = new LegendClientPacket
            {
                Recog = packet.Recog,
                Ident = packet.Ident,
                Param = packet.Param,
                Tag = packet.Tag,
                Series = packet.Series,
                SessionID = sessionId
            };

            var packetData = legendPacket.GetBuffer();
            var encBuf = new byte[packetData.Length * 2 + 4];
            var destLen = Misc.Encode6BitBufDirect(packetData, LegendClientPacket.PackSize, encBuf);
            return HUtil32.GetString(encBuf, 0, destLen);
        }
    }
}
