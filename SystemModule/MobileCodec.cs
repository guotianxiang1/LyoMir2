using System;
using System.IO;
using System.Text;

namespace SystemModule
{
    /// <summary>
    /// 移动客户端二进制帧编解码 — 与抓包协议完全一致
    /// 帧格式: Sign(4) Flags(1) Cmd(1) DataLen(2) Ident(4) [Payload]
    /// </summary>
    public static class MobileCodec
    {
        public const uint SIGN = 0xFF44FF44;
        public const uint LEGACY_SIGN = 0xFF3A3A44;
        public const int HEADER_SIZE = 12;
        public const int INNER_SIZE = 12;
        public const int MAX_PAYLOAD_SIZE = 0x8000;

        // Marker — 2字节帧类型
        public const ushort MARKER_DATA       = 0x1700; // 游戏数据帧 (payload=12B inner + body)
        public const ushort MARKER_CONNECT    = 0x1802; // 连接请求 (12B, 无payload)
        public const ushort MARKER_PING       = 0x1900; // 心跳请求 (12B, 无payload)
        public const ushort MARKER_PONG       = 0x19FA; // 心跳应答 (12B, 无payload)
        public const ushort MARKER_DISCONNECT = 0x1D00; // 断开连接 (12B, 无payload)

        // 协议内层 CMD (DATA帧的Cmd字段)
        public const byte CMD_ACK         = 0x0C; // ACK/游戏数据
        public const byte CMD_SELECT_CHAR = 0x11; // 选择角色
        public const byte CMD_CHAR_LIST   = 0x13; // 角色列表
        public const byte CMD_CLIENT_CMD  = 0x14; // 客户端命令
        public const byte CMD_ENTER_GAME  = 0x15; // 进入游戏
        public const byte CMD_CONFIRM     = 0x17; // 确认选区
        public const byte CMD_STATUS      = 0x1C; // 状态更新
        public const byte CMD_MAP         = 0x24; // 地图数据
        public const byte CMD_SERVER_INFO = 0x2C; // 服务器信息
        public const byte CMD_ITEMS       = 0x3E; // 装备物品
        public const byte CMD_SYSMSG      = 0x42; // 系统消息
        public const byte CMD_AUTH_V1     = 0x59; // 登录认证v1
        public const byte CMD_AUTH_V2     = 0x53; // 登录认证v2
        public const byte CMD_AUTH_V3     = 0x54; // 登录认证v3
        public const byte CMD_AUTH_RESP   = 0x64; // 认证响应
        public const byte CMD_CLIENT_INFO = 0x5D; // 客户端信息上报
        public const byte CMD_ANNOUNCE    = 0x26; // 玩家公告

        // ── 结构 ──
        public struct MobileHeader
        {
            public uint Sign; public ushort Marker; public byte Cmd;
            public byte Flag; public ushort DataLen; public uint Seq;
        }

        public struct InnerHeader
        {
            public int Recog; public ushort Ident;
            public ushort Param; public ushort Tag; public ushort Series;
        }

        public struct MobileFrame
        {
            public MobileHeader Header; public InnerHeader Inner;
            public byte[] Body; public int TotalLen;
        }

        public static Encoding Gbk = Encoding.GetEncoding(936);

        // ═══════════════ 读帧 ═══════════════

        public static bool TryReadFrame(byte[] buffer, int offset, int count, out MobileFrame frame, out int consumed)
        {
            frame = default; consumed = 0;
            if (count < HEADER_SIZE) return false;

            int start = -1;
            for (int i = offset; i + 4 <= offset + count; i++)
                if (IsKnownSign(BitConverter.ToUInt32(buffer, i))) { start = i; break; }
            if (start < 0)
            {
                consumed = Math.Max(0, count - (sizeof(uint) - 1));
                return false;
            }
            if (start + HEADER_SIZE > offset + count)
            {
                consumed = start - offset;
                return false;
            }

            var h = ReadHeader(buffer, start);
            if (!IsKnownSign(h.Sign)) { consumed = start - offset + 1; return false; }

            // Fix 1: Use DataLen at offset 6 for frame sizing (Delphi: PWORD(CurPtr+6)^)
            ushort dataLen = BitConverter.ToUInt16(buffer, start + 6);

            // Delphi GameGate uses a 0x8000-byte receive buffer per active session.
            int frameSize = dataLen + HEADER_SIZE;
            if (dataLen > MAX_PAYLOAD_SIZE) { consumed = start - offset + 1; return false; }

            // Fix 6: Half-packet detection
            if (start + frameSize > offset + count)
            {
                consumed = start - offset;
                return false;
            }

            consumed = start - offset + frameSize;
            byte[] body = Array.Empty<byte>();
            var inner = default(InnerHeader);

            int payloadLen = dataLen;
            int payloadStart = start + HEADER_SIZE;

            // DATA帧(0x1700): payload包含 inner header(12B) + body
            if (h.Marker == MARKER_DATA && payloadLen >= INNER_SIZE)
            {
                inner = ReadInner(buffer, payloadStart);
                int bodyLen = payloadLen - INNER_SIZE;
                if (bodyLen > 0)
                {
                    body = new byte[bodyLen];
                    Buffer.BlockCopy(buffer, payloadStart + INNER_SIZE, body, 0, bodyLen);
                }
            }
            else if (payloadLen > 0)
            {
                body = new byte[payloadLen];
                Buffer.BlockCopy(buffer, payloadStart, body, 0, payloadLen);
            }

            frame = new MobileFrame { Header = h, Inner = inner, Body = body, TotalLen = frameSize };
            return true;
        }

        public static MobileHeader ReadHeader(byte[] buf, int off) => new MobileHeader
        {
            Sign = BitConverter.ToUInt32(buf, off),
            Marker = BitConverter.ToUInt16(buf, off + 4),
            Flag = buf[off + 4],
            Cmd = buf[off + 5],
            DataLen = BitConverter.ToUInt16(buf, off + 6),
            Seq = BitConverter.ToUInt32(buf, off + 8)
        };

        public static InnerHeader ReadInner(byte[] buf, int off) => new InnerHeader
        {
            Recog = BitConverter.ToInt32(buf, off),
            Ident = BitConverter.ToUInt16(buf, off + 4),
            Param = BitConverter.ToUInt16(buf, off + 6),
            Tag = BitConverter.ToUInt16(buf, off + 8),
            Series = BitConverter.ToUInt16(buf, off + 10)
        };

        // ═══════════════ 写帧 ═══════════════

        /// <summary>写DATA帧 (Marker=0x1700, 包含Inner+Body)</summary>
        public static byte[] WriteFrame(InnerHeader inner, byte[] body, uint seq)
            => WriteFrame(inner, body, seq, MARKER_DATA);

        public static byte[] WriteFrame(InnerHeader inner, byte[] body, uint seq, ushort marker)
        {
            int bodyLen = body?.Length ?? 0;
            int payloadLen = INNER_SIZE + bodyLen;
            if (payloadLen > MAX_PAYLOAD_SIZE)
                throw new InvalidDataException($"44FF44FF payload exceeds {MAX_PAYLOAD_SIZE} bytes");
            int totalLen = HEADER_SIZE + payloadLen;
            var buf = new byte[totalLen];

            BitConverter.TryWriteBytes(new Span<byte>(buf, 0, 4), SIGN);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 4, 2), marker);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 6, 2), (ushort)payloadLen);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 8, 4), seq);

            BitConverter.TryWriteBytes(new Span<byte>(buf, HEADER_SIZE, 4), inner.Recog);
            BitConverter.TryWriteBytes(new Span<byte>(buf, HEADER_SIZE + 4, 2), (short)inner.Ident);
            BitConverter.TryWriteBytes(new Span<byte>(buf, HEADER_SIZE + 6, 2), (short)inner.Param);
            BitConverter.TryWriteBytes(new Span<byte>(buf, HEADER_SIZE + 8, 2), (short)inner.Tag);
            BitConverter.TryWriteBytes(new Span<byte>(buf, HEADER_SIZE + 10, 2), (short)inner.Series);
            if (bodyLen > 0) Buffer.BlockCopy(body, 0, buf, HEADER_SIZE + INNER_SIZE, bodyLen);
            return buf;
        }

        /// <summary>写简单DATA帧 (Marker=0x1700, Cmd+Flag指定, 无Inner)</summary>
        public static byte[] WriteSimpleFrame(byte cmd, byte flag, byte[] body, uint seq)
        {
            int bodyLen = body?.Length ?? 0;
            if (bodyLen > MAX_PAYLOAD_SIZE)
                throw new InvalidDataException($"44FF44FF payload exceeds {MAX_PAYLOAD_SIZE} bytes");
            var buf = new byte[HEADER_SIZE + bodyLen];
            BitConverter.TryWriteBytes(new Span<byte>(buf, 0, 4), SIGN);
            buf[4] = flag;
            buf[5] = cmd;
            BitConverter.TryWriteBytes(new Span<byte>(buf, 6, 2), (ushort)bodyLen);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 8, 4), seq);
            if (bodyLen > 0) Buffer.BlockCopy(body, 0, buf, HEADER_SIZE, bodyLen);
            return buf;
        }

        /// <summary>写控制帧 (无payload, 12B)</summary>
        public static byte[] WriteControlFrame(ushort marker, uint seq)
        {
            var buf = new byte[HEADER_SIZE];
            BitConverter.TryWriteBytes(new Span<byte>(buf, 0, 4), SIGN);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 4, 2), marker);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 6, 2), (ushort)0);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 8, 4), seq);
            return buf;
        }

        public static byte[] WritePing(uint seq) => WriteControlFrame(MARKER_PING, seq);
        public static byte[] WritePong(uint seq) => WriteControlFrame(MARKER_PONG, seq);
        public static byte[] WriteConnect(uint seq) => WriteControlFrame(MARKER_CONNECT, seq);
        public static byte[] WriteDisconnect(uint seq) => WriteControlFrame(MARKER_DISCONNECT, seq);

        public static bool IsKnownSign(uint sign) => sign == SIGN || sign == LEGACY_SIGN;

        // ═══════════════ GBK ═══════════════
        public static byte[] EncodeGbk(string s) => Gbk.GetBytes(s ?? "");
        public static string DecodeGbk(byte[] buf, int off, int len)
        {
            int end = off + len, p = off;
            while (p < end && buf[p] != 0) p++;
            return p > off ? Gbk.GetString(buf, off, p - off) : "";
        }
        public static void WriteFixedStr(byte[] buf, int off, int n, string s)
        {
            var b = EncodeGbk(s);
            int cp = Math.Min(b.Length, n);
            if (cp > 0) Buffer.BlockCopy(b, 0, buf, off, cp);
        }

        public static Action<string> DebugLog;
        internal static void LogDebug(string msg) => DebugLog?.Invoke(msg);
    }
}
