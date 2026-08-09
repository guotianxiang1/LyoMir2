using System;
using System.Collections.Generic;
using System.Text;

namespace SystemModule.Packet
{
    /// <summary>
    /// 77BBAA33 内部转发协议 - GameGate↔M2Server 端口5000
    /// 从 loopback pcap 10,581条消息完整还原
    /// </summary>
    public class InternalPacket77
    {
        public const uint MAGIC = 0x33AABB77; // LE: 77 BB AA 33
        
        public uint Magic;       // [0-3]   0x33AABB77
        public uint ConnID;      // [4-7]   连接句柄
        public uint SeqID;       // [8-11]  序列号
        public ushort FrameLen;  // [12-13] 帧总长 (14=ACK无payload)
        public ushort Cmd;       // [14-15] 命令码(与44FF44FF CMD一致)
        public uint Field16;     // [16-19] 时间戳/上下文
        public uint Field20;     // [20-23] 内层数据长度
        public byte[] Payload;   // [24+]   变长数据

        public const int HEADER_SIZE = 24;
        public const ushort ACK_FRAME_LEN = 14; // 最小ACK帧长度
        public const int MAX_FRAME_SIZE = 0x8000;
        public const int MAX_PAYLOAD_SIZE = MAX_FRAME_SIZE - HEADER_SIZE;

        public static InternalPacket77 FromBytes(byte[] buf, int offset, int length)
        {
            if (buf == null || offset < 0 || length < ACK_FRAME_LEN
                || offset > buf.Length - length) return null;
            var frameLen = BitConverter.ToUInt16(buf, offset + 12);
            if (frameLen == ACK_FRAME_LEN)
            {
                return new InternalPacket77
                {
                    Magic = BitConverter.ToUInt32(buf, offset),
                    ConnID = BitConverter.ToUInt32(buf, offset + 4),
                    SeqID = BitConverter.ToUInt32(buf, offset + 8),
                    FrameLen = ACK_FRAME_LEN,
                    Cmd = 0x0C,
                    Payload = Array.Empty<byte>()
                };
            }
            if (frameLen < HEADER_SIZE || length < HEADER_SIZE) return null;
            var pkt = new InternalPacket77
            {
                Magic    = BitConverter.ToUInt32(buf, offset),
                ConnID   = BitConverter.ToUInt32(buf, offset + 4),
                SeqID    = BitConverter.ToUInt32(buf, offset + 8),
                FrameLen = frameLen,
                Cmd      = BitConverter.ToUInt16(buf, offset + 14),
                Field16  = BitConverter.ToUInt32(buf, offset + 16),
                Field20  = BitConverter.ToUInt32(buf, offset + 20),
            };
            int payloadLen = Math.Min(pkt.FrameLen - HEADER_SIZE, length - HEADER_SIZE);
            if (payloadLen > 0)
            {
                pkt.Payload = new byte[payloadLen];
                Buffer.BlockCopy(buf, offset + HEADER_SIZE, pkt.Payload, 0, payloadLen);
            }
            else pkt.Payload = Array.Empty<byte>();
            return pkt;
        }

        public byte[] ToBytes()
        {
            if (FrameLen == ACK_FRAME_LEN && Cmd == 0x0C
                && (Payload == null || Payload.Length == 0))
            {
                var ack = new byte[ACK_FRAME_LEN];
                BitConverter.TryWriteBytes(new Span<byte>(ack, 0, 4), MAGIC);
                BitConverter.TryWriteBytes(new Span<byte>(ack, 4, 4), ConnID);
                BitConverter.TryWriteBytes(new Span<byte>(ack, 8, 4), SeqID);
                BitConverter.TryWriteBytes(new Span<byte>(ack, 12, 2), ACK_FRAME_LEN);
                return ack;
            }
            int totalLen = HEADER_SIZE + (Payload?.Length ?? 0);
            var buf = new byte[totalLen];
            BitConverter.TryWriteBytes(new Span<byte>(buf, 0, 4), MAGIC);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 4, 4), ConnID);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 8, 4), SeqID);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 12, 2), (ushort)totalLen);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 14, 2), Cmd);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 16, 4), Field16);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 20, 4), Field20);
            if (Payload?.Length > 0) Buffer.BlockCopy(Payload, 0, buf, HEADER_SIZE, Payload.Length);
            return buf;
        }

        /// <summary>创建ACK帧</summary>
        public static InternalPacket77 Ack(uint connId, uint seqId, ushort ackCmd)
        {
            return new InternalPacket77
            {
                Magic = MAGIC, ConnID = connId, SeqID = seqId,
                FrameLen = ACK_FRAME_LEN, Cmd = 0x0C, Payload = Array.Empty<byte>()
            };
        }

        /// <summary>从payload扫描所有77BBAA33消息</summary>
        public static List<InternalPacket77> ScanAll(byte[] buffer, int offset, int length)
        {
            var list = new List<InternalPacket77>();
            int pos = offset;
            byte[] magicBytes = { 0x77, 0xBB, 0xAA, 0x33 };
            while (pos + ACK_FRAME_LEN <= offset + length)
            {
                int idx = -1;
                for (int i = pos; i + 4 <= offset + length; i++)
                {
                    if (buffer[i]==magicBytes[0] && buffer[i+1]==magicBytes[1] &&
                        buffer[i+2]==magicBytes[2] && buffer[i+3]==magicBytes[3])
                    { idx = i; break; }
                }
                if (idx < 0) break;
                var pkt = FromBytes(buffer, idx, offset + length - idx);
                if (pkt != null) list.Add(pkt);
                pos = idx + Math.Max((int)(pkt?.FrameLen ?? 1), 1);
            }
            return list;
        }

        /// <summary>从44FF44FF帧构造内部转发包</summary>
        public static InternalPacket77 FromClientFrame(uint connId, uint seqId, ushort clientCmd, byte[] payload)
        {
            int totalLen = HEADER_SIZE + (payload?.Length ?? 0);
            return new InternalPacket77
            {
                Magic = MAGIC, ConnID = connId, SeqID = seqId,
                FrameLen = (ushort)totalLen, Cmd = clientCmd,
                Field16 = (uint)Environment.TickCount,
                Field20 = (uint)(payload?.Length ?? 0),
                Payload = payload ?? Array.Empty<byte>()
            };
        }
    }
}
