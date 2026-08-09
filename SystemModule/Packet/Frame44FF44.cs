using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SystemModule.Packet
{
    /// <summary>
    /// 44FF44FF 二进制帧 — 移动客户端协议
    /// 从手游抓包 + GameGate反汇编完整还原
    /// </summary>
    public class Frame44FF44
    {
        public const uint SIGN = 0xFF44FF44; // 线序: 44 FF 44 FF
        public const uint LEGACY_SIGN = 0xFF3A3A44; // 旧版签名: 44 3A 3A FF
        public const int MAX_PAYLOAD_SIZE = 0x8000;
        public const int MAX_FRAME_SIZE = HEADER_SIZE + MAX_PAYLOAD_SIZE;

        public static bool IsKnownSign(uint sign) => sign == SIGN || sign == LEGACY_SIGN;

        // 固定帧头 12字节
        public uint   Sign;      // [0-3]   0xFF44FF44
        public ushort Marker;    // [4-5]   Flag | (Cmd << 8), retained for routing compatibility
        public byte   Cmd;       // [5]     command
        public byte   Flag;      // [4]     flags/reserved
        public ushort DataLen;   // [6-7]   payload length
        public uint   Seq;       // [8-11]  sequence/identifier

        // Payload
        public byte[] Payload;   // [12+]   变长数据

        public const int HEADER_SIZE = 12;

        // Marker 常量
        public const ushort MARKER_DATA        = 0x1700; // 游戏数据帧
        public const ushort MARKER_CONNECT     = 0x1802; // 连接请求
        public const ushort MARKER_PING        = 0x1900; // 心跳请求
        public const ushort MARKER_PONG        = 0x19FA; // 心跳应答
        public const ushort MARKER_DISCONNECT  = 0x1D00; // 断开连接

        public Frame44FF44()
        {
            Sign = SIGN;
        }

        public Frame44FF44(byte cmd, byte flag, uint seq, byte[] payload = null)
        {
            Sign = SIGN;
            Cmd = cmd;
            Flag = flag;
            Marker = (ushort)(flag | (cmd << 8));
            Seq = seq;
            Payload = payload ?? Array.Empty<byte>();
        }

        /// <summary>从字节流中扫描并解析第一个44FF44FF帧（Delphi风格: DataLen决定帧边界）</summary>
        public static Frame44FF44 Scan(byte[] buffer, int offset, int length, out int consumed)
        {
            consumed = 0;
            int pos = offset;
            int end = offset + length;
            while (pos + sizeof(uint) <= end)
            {
                uint sign = BitConverter.ToUInt32(buffer, pos);
                // Fix 2: 识别已知签名 (44FF44FF 和旧版 FF3A3A44)
                if (IsKnownSign(sign))
                {
                    if (pos + HEADER_SIZE > end)
                    {
                        // The complete signature is present, but the rest of the header
                        // has not arrived. Drop only bytes before the signature.
                        consumed = pos - offset;
                        return null;
                    }

                    // Fix 1: Delphi -> DataLen = PWORD(CurPtr+6)^; FrameSize = DataLen + 12
                    ushort dataLen = BitConverter.ToUInt16(buffer, pos + 6);
                    int frameSize = dataLen + HEADER_SIZE;

                    if (dataLen > MAX_PAYLOAD_SIZE)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Frame44FF44] Frame overflow DataLen={dataLen} > max={MAX_PAYLOAD_SIZE}, skipping sign at pos={pos}");
                        pos++; // 跳过此伪签名，继续扫描
                        continue;
                    }

                    // Fix 6: 半包检测 — 缓冲区数据不足完整帧
                    if (pos + frameSize > end)
                    {
                        // Discard only garbage before the partial frame. The frame marker
                        // itself must remain buffered for the next receive.
                        consumed = pos - offset;
                        return null;
                    }

                    var frame = new Frame44FF44
                    {
                        Sign = sign,
                        Marker = BitConverter.ToUInt16(buffer, pos + 4),
                        Flag  = buffer[pos + 4],
                        Cmd   = buffer[pos + 5],
                        DataLen = dataLen,
                        Seq   = BitConverter.ToUInt32(buffer, pos + 8),
                    };

                    // Payload 从 offset 12 开始, 长度为 DataLen
                    if (dataLen > 0)
                    {
                        frame.Payload = new byte[dataLen];
                        Buffer.BlockCopy(buffer, pos + HEADER_SIZE, frame.Payload, 0, dataLen);
                    }
                    else
                    {
                        frame.Payload = Array.Empty<byte>();
                    }

                    consumed = pos - offset + frameSize;
                    return frame;
                }
                pos++;
            }
            // A signature may be split across receives. Retain at most the final three
            // bytes and consume all preceding garbage so the receive buffer cannot grow.
            consumed = Math.Max(0, length - (sizeof(uint) - 1));
            return null;
        }

        /// <summary>扫描buffer中所有44FF44FF帧</summary>
        public static List<Frame44FF44> ScanAll(byte[] buffer, int offset, int length)
        {
            return ScanAll(buffer, offset, length, out _);
        }

        /// <summary>扫描buffer中所有44FF44FF帧, 返回总消费字节数（用于保留半包数据）</summary>
        public static List<Frame44FF44> ScanAll(byte[] buffer, int offset, int length, out int totalConsumed)
        {
            var frames = new List<Frame44FF44>();
            int pos = offset;
            while (pos < offset + length)
            {
                var frame = Scan(buffer, pos, offset + length - pos, out int consumed);
                if (frame != null)
                {
                    frames.Add(frame);
                    pos += consumed;
                }
                else
                {
                    // consumed==0: 半包, 保留数据; consumed>0: 垃圾数据已跳过
                    pos += consumed;
                    break;
                }
            }
            totalConsumed = pos - offset;
            return frames;
        }

        /// <summary>序列化为字节数组</summary>
        public byte[] ToBytes()
        {
            int payloadLength = Payload?.Length ?? 0;
            if (payloadLength > MAX_PAYLOAD_SIZE)
                throw new InvalidDataException($"44FF44FF payload exceeds {MAX_PAYLOAD_SIZE} bytes");
            DataLen = checked((ushort)payloadLength);
            Marker = (ushort)(Flag | (Cmd << 8));
            int totalLen = HEADER_SIZE + payloadLength;
            var buf = new byte[totalLen];
            BitConverter.GetBytes(SIGN).CopyTo(buf, 0);          // 44 FF 44 FF
            buf[4] = Flag;
            buf[5] = Cmd;
            BitConverter.GetBytes(DataLen).CopyTo(buf, 6);
            BitConverter.GetBytes(Seq).CopyTo(buf, 8);
            if (Payload != null && Payload.Length > 0)
                Buffer.BlockCopy(Payload, 0, buf, HEADER_SIZE, Payload.Length);
            return buf;
        }

        /// <summary>创建控制帧 (无Payload)</summary>
        public static Frame44FF44 Control(ushort marker, uint seq)
        {
            return new Frame44FF44
            {
                Sign = SIGN,
                Marker = marker,
                Cmd = (byte)(marker >> 8),
                Flag = (byte)marker,
                Seq = seq,
                Payload = Array.Empty<byte>()
            };
        }

        /// <summary>创建PING帧</summary>
        public static Frame44FF44 Ping(uint seq) => Control(MARKER_PING, seq);

        /// <summary>创建PONG帧</summary>
        public static Frame44FF44 Pong(uint seq) => Control(MARKER_PONG, seq);

        /// <summary>创建CONNECT应答帧</summary>
        public static Frame44FF44 ConnectAck(uint seq) => Control(MARKER_DATA, seq);

        /// <summary>从payload中提取GBK CString (null-terminated)</summary>
        public static string ReadCString(byte[] data, int offset, out int consumed)
        {
            consumed = 0;
            if (data == null || offset >= data.Length) return string.Empty;
            int end = offset;
            while (end < data.Length && data[end] != 0) end++;
            consumed = end - offset + 1; // +1 for null terminator
            if (end == offset) return string.Empty;
            return Encoding.GetEncoding(936).GetString(data, offset, end - offset);
        }

        /// <summary>写入GBK CString到buffer</summary>
        public static int WriteCString(byte[] buffer, int offset, string text)
        {
            var gbkBytes = Encoding.GetEncoding(936).GetBytes(text ?? string.Empty);
            Buffer.BlockCopy(gbkBytes, 0, buffer, offset, gbkBytes.Length);
            buffer[offset + gbkBytes.Length] = 0; // null terminator
            return gbkBytes.Length + 1;
        }

        /// <summary>从payload中读取内嵌的44FF44FF帧列表</summary>
        public List<Frame44FF44> ScanInner()
        {
            if (Payload == null || Payload.Length < HEADER_SIZE) return new List<Frame44FF44>();
            return ScanAll(Payload, 0, Payload.Length);
        }

        public override string ToString()
        {
            string markerName = Marker switch
            {
                MARKER_DATA => "DATA",
                MARKER_CONNECT => "CONNECT",
                MARKER_PING => "PING",
                MARKER_PONG => "PONG",
                MARKER_DISCONNECT => "DISCONNECT",
                _ => $"0x{Marker:X4}"
            };
            return $"44FF44FF [{markerName}] Cmd=0x{Cmd:X02} Flag=0x{Flag:X02} Seq={Seq} Len={Payload?.Length ?? 0}";
        }
    }
}
