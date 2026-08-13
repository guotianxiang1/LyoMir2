using System;
using System.Collections.Generic;

namespace SystemModule.Packet
{
    /// <summary>
    /// 77BBAA33 内部转发协议 (GameGate &lt;-&gt; M2Server)。
    ///
    /// 逐字节复刻自原版 M2Server (staging\_reunpack_work\flat_image.bin, base 0x400000)。
    /// 传输帧头 = 16 字节 (原验证 24 字节有误)。证据链:
    ///   * 发送器 0x5F61C5 (Cmd=0x0B)、0x5F65AA、0x637AC1 均 `mov ecx,0x10` 后 send/advance 0x10。
    ///   * 接收器 0x63B258 `movsd`x4 拷贝 16 字节头, 再 `lea eax,[edi+0x10]` 从 frame+0x10 取 body。
    ///   * 解析器 0x5F666A / 0x63A66C / 0x69CB... 计算 total = 0x10 + word[+0x0E]。
    ///   * 代码库既有 LegacyGateType18 (type 18 = 数据帧 Cmd 0x12) 亦为 16 字节头, 独立佐证。
    ///   * 与用户实测 DBSvr/GameGate 16 字节头一致。
    ///
    /// 头布局 (小端):
    ///   +0x00 (4) Magic   0x33AABB77                         (0x637AC1 mov [eax],77BBAA33)
    ///   +0x04 (4) ConnID  nSocket / 连接句柄                 (0x637ACE mov [eax+4],edx)
    ///   +0x08 (4) SeqID   会话/上下文 (wGSocketIdx+wUserIdx)  (0x637AD4 mov [eax+8],edx)
    ///   +0x0C (2) Cmd     ident/命令码 (switch 判别)          (0x637AC7 mov [eax+0xC],di)
    ///   +0x0E (2) BodyLen 头后 body 字节数 = 总长-16          (0x637AD7 mov [eax+0xE],bx)
    ///   +0x10..  Payload  body (BodyLen 字节)                (0x637B02 memcpy)
    ///
    /// 控制/ACK 帧: BodyLen=0, 总长恒为 16 字节 (原版无 14 字节帧; 解析器最小需 16 才能读到 +0x0E)。
    /// 数据帧 (Cmd=0x12) 的 body 首 12 字节是内层子头(Recog/Ident/Param/Tag/Series), 属 Payload,
    /// 见 <see cref="LegacyGateType18"/> —— 旧实现的 Field16/Field20 即把该内层子头误折入传输头。
    /// </summary>
    public class InternalPacket77
    {
        public const uint MAGIC = 0x33AABB77; // LE: 77 BB AA 33

        public uint Magic;       // +0x00
        public uint ConnID;      // +0x04  nSocket
        public uint SeqID;       // +0x08  会话/上下文
        public ushort Cmd;       // +0x0C  ident/命令码 (原实现错放在 +0x0E)
        public ushort FrameLen;  // 帧总长 = HEADER_SIZE + BodyLen; 序列化时由 Payload 派生, 保留供调用方读写
        public byte[] Payload;   // +0x10  body (BodyLen 字节)

        // 兼容字段: 不属于 16 字节传输头。它们是 Cmd=0x12 数据帧 body 内层子头
        // (Recog@body+0x00 / Ident@body+0x04 ...) 被旧实现误当作头字段(24字节头由此而来)。
        // 保留仅为源码兼容, *不参与线缆序列化* (ToBytes 不写、FromBytes 不读)。
        // 构造数据帧内层子头请改用 LegacyGateType18。
        public uint Field16;
        public uint Field20;

        public const int HEADER_SIZE = 16;        // 证据: mov ecx,0x10 / advance 0x10 / movsd x4 (0x63B28E)
        public const ushort ACK_FRAME_LEN = 16;   // 控制/ACK 帧 = 16 字节 (BodyLen=0)
        public const int MAX_FRAME_SIZE = 0x8000;  // 发送器上界 0x5F6B5C `cmp eax,0x8000`
        public const int MAX_PAYLOAD_SIZE = MAX_FRAME_SIZE - HEADER_SIZE;

        /// <param name="length">帧总长 (含 16 字节头)。调用方(解析器)已按 total = 16 + word[+0x0E] 求得。</param>
        public static InternalPacket77 FromBytes(byte[] buf, int offset, int length)
        {
            if (buf == null || offset < 0 || length < HEADER_SIZE
                || offset > buf.Length - length) return null;

            var bodyLen = BitConverter.ToUInt16(buf, offset + 14); // +0x0E
            var pkt = new InternalPacket77
            {
                Magic  = BitConverter.ToUInt32(buf, offset),       // +0x00
                ConnID = BitConverter.ToUInt32(buf, offset + 4),   // +0x04
                SeqID  = BitConverter.ToUInt32(buf, offset + 8),   // +0x08
                Cmd    = BitConverter.ToUInt16(buf, offset + 12),  // +0x0C
            };

            int payloadLen = Math.Min(bodyLen, length - HEADER_SIZE);
            if (payloadLen < 0) payloadLen = 0;
            if (payloadLen > 0)
            {
                pkt.Payload = new byte[payloadLen];
                Buffer.BlockCopy(buf, offset + HEADER_SIZE, pkt.Payload, 0, payloadLen);
            }
            else pkt.Payload = Array.Empty<byte>();

            pkt.FrameLen = (ushort)(HEADER_SIZE + payloadLen);
            return pkt;
        }

        public byte[] ToBytes()
        {
            int bodyLen = Payload?.Length ?? 0;         // = 线缆 +0x0E
            int totalLen = HEADER_SIZE + bodyLen;
            var buf = new byte[totalLen];
            BitConverter.TryWriteBytes(new Span<byte>(buf, 0, 4), MAGIC);          // +0x00
            BitConverter.TryWriteBytes(new Span<byte>(buf, 4, 4), ConnID);         // +0x04
            BitConverter.TryWriteBytes(new Span<byte>(buf, 8, 4), SeqID);          // +0x08
            BitConverter.TryWriteBytes(new Span<byte>(buf, 12, 2), Cmd);           // +0x0C
            BitConverter.TryWriteBytes(new Span<byte>(buf, 14, 2), (ushort)bodyLen); // +0x0E
            if (bodyLen > 0) Buffer.BlockCopy(Payload, 0, buf, HEADER_SIZE, bodyLen); // +0x10
            return buf;
        }

        /// <summary>创建 16 字节控制/ACK 帧 (BodyLen=0)。</summary>
        public static InternalPacket77 Ack(uint connId, uint seqId, ushort ackCmd)
        {
            return new InternalPacket77
            {
                Magic = MAGIC, ConnID = connId, SeqID = seqId,
                Cmd = ackCmd, FrameLen = HEADER_SIZE, Payload = Array.Empty<byte>()
            };
        }

        /// <summary>从缓冲区扫描所有 77BBAA33 帧。</summary>
        public static List<InternalPacket77> ScanAll(byte[] buffer, int offset, int length)
        {
            var list = new List<InternalPacket77>();
            int end = offset + length;
            int pos = offset;
            while (pos + HEADER_SIZE <= end)
            {
                int idx = -1;
                for (int i = pos; i + 4 <= end; i++)
                {
                    if (buffer[i] == 0x77 && buffer[i + 1] == 0xBB &&
                        buffer[i + 2] == 0xAA && buffer[i + 3] == 0x33)
                    { idx = i; break; }
                }
                if (idx < 0 || idx + HEADER_SIZE > end) break;
                int bodyLen = BitConverter.ToUInt16(buffer, idx + 14); // +0x0E
                int frameLen = HEADER_SIZE + bodyLen;
                if (idx + frameLen > end) break;
                var pkt = FromBytes(buffer, idx, frameLen);
                if (pkt != null) { list.Add(pkt); pos = idx + frameLen; }
                else pos = idx + 1;
            }
            return list;
        }

        /// <summary>从客户端数据构造转发帧 (16 字节头 + 原始 body)。</summary>
        public static InternalPacket77 FromClientFrame(uint connId, uint seqId, ushort clientCmd, byte[] payload)
        {
            var body = payload ?? Array.Empty<byte>();
            return new InternalPacket77
            {
                Magic = MAGIC, ConnID = connId, SeqID = seqId,
                Cmd = clientCmd,
                FrameLen = (ushort)(HEADER_SIZE + body.Length),
                Payload = body
            };
        }
    }
}
