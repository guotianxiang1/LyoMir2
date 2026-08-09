using System;
using System.IO;
using System.IO.Compression;

namespace DBSvr.Core
{
    /// <summary>
    /// Blob 压缩/解压工具。
    /// 模拟 Delphi 原版 TMemoryStream → Deflate(zlib) → Blob 的数据流。
    /// 支持原始 deflate (RFC 1951) 和 zlib 封装 (RFC 1950) 两种格式。
    /// </summary>
    public static class BlobCompressor
    {
        // zlib 魔数头: CMF(0x78) + FLG
        // 0x78 表示 deflate 算法, 窗口大小 32K
        private const byte ZlibCmf = 0x78;
        private const byte ZlibFlgDefault = 0x9C; // 默认压缩级别
        private const byte ZlibFlgBest = 0xDA;    // 最高压缩级别
        private const int ZlibHeaderSize = 2;
        private const int Adler32Size = 4;

        /// <summary>
        /// 压缩数据 (raw deflate, 无 zlib 头)。
        /// 对应 Delphi 原版 TStream.WriteComponent → Deflate 的结果。
        /// </summary>
        public static byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<byte>();

            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
            {
                deflate.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }

        /// <summary>
        /// 解压数据 (raw deflate, 无 zlib 头)。
        /// 对应 Delphi 原版 Inflate → TStream.ReadComponent 的结果。
        /// </summary>
        public static byte[] Decompress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<byte>();

            using var input = new MemoryStream(data);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>
        /// 压缩数据 (zlib 格式: 2 字节头 + raw deflate + 4 字节 Adler-32)。
        /// 用于与原版 Delphi DBServer 互通时使用。
        /// </summary>
        public static byte[] CompressZlib(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<byte>();

            using var output = new MemoryStream();
            // zlib header
            output.WriteByte(ZlibCmf);
            output.WriteByte(ZlibFlgDefault);

            // raw deflate
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
            {
                deflate.Write(data, 0, data.Length);
            }

            // Adler-32 checksum
            uint adler = ComputeAdler32(data);
            output.WriteByte((byte)((adler >> 24) & 0xFF));
            output.WriteByte((byte)((adler >> 16) & 0xFF));
            output.WriteByte((byte)((adler >> 8) & 0xFF));
            output.WriteByte((byte)(adler & 0xFF));

            return output.ToArray();
        }

        /// <summary>
        /// 解压数据 (zlib 格式)。
        /// </summary>
        public static byte[] DecompressZlib(byte[] data)
        {
            if (data == null || data.Length < ZlibHeaderSize + Adler32Size)
                return Array.Empty<byte>();

            // 跳过 zlib header (2 bytes) 和末尾 Adler-32 (4 bytes)
            int deflateStart = ZlibHeaderSize;
            int deflateLength = data.Length - ZlibHeaderSize - Adler32Size;

            using var input = new MemoryStream(data, deflateStart, deflateLength);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>
        /// 自动检测是否为 zlib 格式 (检查 CMF header)，选择正确的解压方式。
        /// </summary>
        public static byte[] AutoDecompress(byte[] data)
        {
            if (data == null || data.Length < 2)
                return Array.Empty<byte>();

            // zlib CMF 高4位总是 8 (deflate), 低4位是窗口大小
            // 合法 CMF 值: 0x08, 0x18, 0x28, 0x38, 0x48, 0x58, 0x68, 0x78
            byte cmf = data[0];
            if ((cmf & 0x0F) == 0x08 && cmf <= 0x78)
            {
                return DecompressZlib(data);
            }

            return Decompress(data);
        }

        /// <summary>
        /// 尝试解压；如果数据可能未压缩，直接返回原数据。
        /// </summary>
        public static byte[] TryDecompress(byte[] data)
        {
            try
            {
                return AutoDecompress(data);
            }
            catch
            {
                // 数据可能未压缩，直接返回
                return data;
            }
        }

        /// <summary>
        /// Adler-32 校验和计算 (与 zlib 兼容)。
        /// </summary>
        private static uint ComputeAdler32(byte[] data)
        {
            const uint MOD_ADLER = 65521;
            uint a = 1, b = 0;
            foreach (byte t in data)
            {
                a = (a + t) % MOD_ADLER;
                b = (b + a) % MOD_ADLER;
            }
            return (b << 16) | a;
        }
    }
}
