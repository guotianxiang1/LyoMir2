
namespace SystemModule
{
    public class Misc
    {
        private const byte bySeed = 0xAC;
        private const byte byBase = 0x3C;

        public static string EncodeString(string Str)
        {
            byte[] EncBuf = new byte[4096];
            var tempBuf = HUtil32.GetBytes(Str);
            var buffLen = EncodeBuf(tempBuf, Str.Length, EncBuf);
            return HUtil32.GetString(EncBuf, 0, buffLen);
        }

        public static string DecodeString(string Str)
        {
            var tempBuf = HUtil32.GetBytes(Str);
            var buffLen = 0;
            var encBuf = DecodeBuf(tempBuf, Str.Length, ref buffLen);
            return HUtil32.GetString(encBuf, 0, buffLen);
        }

        
        
        
        
        public static int EncodeBuf(byte[] Buf, int Len, byte[] DstBuf, int dstOffset = 0)
        {
            var no = 2;
            byte remainder = 0;
            var pos = 0;
            var dstPos = dstOffset;
            for (var i = 0; i < Len; i++)
            {
                var c = (byte)(Buf[pos] ^ bySeed);
                pos++;
                if (no == 6)
                {
                    DstBuf[dstPos] = (byte)((c & 0x3F) + byBase);
                    dstPos++;
                    remainder = (byte)(remainder | ((c >> 2) & 0x30));
                    DstBuf[dstPos] = (byte)(remainder + byBase);
                    dstPos++;
                    remainder = 0;
                }
                else
                {
                    var temp = (byte)(c >> 2);
                    DstBuf[dstPos] = (byte)(((temp & 0x3C) | (c & 0x3)) + byBase);
                    dstPos++;
                    remainder = (byte)((remainder << 2) | (temp & 0x3));
                }
                no = no % 6 + 2;
            }
            if (no != 2)
            {
                DstBuf[dstPos] = (byte)(remainder + byBase);
                dstPos++;
            }
            var result = dstPos - dstOffset;
            return result;
        }

        public static byte[] DecodeBuf(byte[] Buf, int Len, ref int decodeLen)
        {
            byte temp;
            byte remainder;
            byte c;
            var nCycles = Len / 4;
            var nBytesLeft = Len % 4;
            var dstPos = 0;
            decodeLen = GetDecodeLen(nCycles, nBytesLeft);
            var dstBuffer = new byte[decodeLen];
            for (var i = 0; i < nCycles; i++)
            {
                var curCycleBegin = i * 4;
                remainder = (byte)((Buf[curCycleBegin + 3]) - byBase);
                temp = (byte)(Buf[curCycleBegin] - byBase);
                c = (byte)(((temp << 2) & 0xF0) | (remainder & 0x0C) | (temp & 0x3));
                dstBuffer[dstPos] = (byte)(c ^ bySeed);
                dstPos++;
                temp = (byte)((Buf[curCycleBegin + 1]) - byBase);
                c = (byte)(((temp << 2) & 0xF0) | ((remainder << 2) & 0x0C) | (temp & 0x3));
                dstBuffer[dstPos] = (byte)(c ^ bySeed);
                dstPos++;
                temp = (byte)(Buf[curCycleBegin + 2] - byBase);
                c = (byte)(temp | ((remainder << 2) & 0xC0));
                dstBuffer[dstPos] = (byte)(c ^ bySeed);
                dstPos++;
            }
            if (nBytesLeft == 2)
            {
                remainder = (byte)(Buf[Len - 1] - byBase);
                temp = (byte)(Buf[Len - 2] - byBase);
                c = (byte)(((temp << 2) & 0xF0) | ((remainder << 2) & 0x0C) | (temp & 0x3));
                dstBuffer[dstPos] = (byte)(c ^ bySeed);
            }
            else if (nBytesLeft == 3)
            {
                remainder = (byte)(Buf[Len - 1] - byBase);
                temp = (byte)(Buf[Len - 3] - byBase);
                c = (byte)(((temp << 2) & 0xF0) | (remainder & 0x0C) | (temp & 0x3));
                dstBuffer[dstPos] = (byte)(c ^ bySeed);
                dstPos++;
                temp = (byte)(Buf[Len - 2] - byBase);
                c = (byte)(((temp << 2) & 0xF0) | ((remainder << 2) & 0x0C) | (temp & 0x3));
                dstBuffer[dstPos] = (byte)(c ^ bySeed);
            }
            return dstBuffer;
        }

        private static int GetDecodeLen(int cycles, int bytesLeft)
        {
            var dstPos = cycles * 3;
            switch (bytesLeft)
            {
                case 2:
                    dstPos++;
                    break;
                case 3:
                    dstPos += 2;
                    break;
            }
            return dstPos;
        }

        /// <summary>
        /// 6-bit encode without XOR, matching C++ LegendMir5 client Encode6BitBuf / Decode6BitBuf.
        /// </summary>
        public static int Encode6BitBufDirect(byte[] src, int srcLen, byte[] dst, int dstOffset = 0)
        {
            byte btMade;
            byte btRest = 0;
            int nRestCount = 0;
            int nDestPos = dstOffset;
            int nDestLen = dst.Length;

            for (int i = 0; i < srcLen; i++)
            {
                if (nDestPos >= nDestLen)
                    break;

                byte btCh = src[i];
                btMade = (byte)((btRest | (btCh >> (2 + nRestCount))) & 0x3F);
                btRest = (byte)(((btCh << (8 - (2 + nRestCount))) >> 2) & 0x3F);
                nRestCount += 2;

                if (nRestCount < 6)
                {
                    dst[nDestPos] = (byte)(btMade + byBase);
                    nDestPos++;
                }
                else
                {
                    if (nDestPos < nDestLen - 1)
                    {
                        dst[nDestPos] = (byte)(btMade + byBase);
                        dst[nDestPos + 1] = (byte)(btRest + byBase);
                        nDestPos += 2;
                    }
                    else
                    {
                        dst[nDestPos] = (byte)(btMade + byBase);
                        nDestPos++;
                    }
                    nRestCount = 0;
                    btRest = 0;
                }
            }

            if (nRestCount > 0)
            {
                if (nDestPos < nDestLen)
                {
                    dst[nDestPos] = (byte)(btRest + byBase);
                    nDestPos++;
                }
            }

            return nDestPos - dstOffset;
        }

        /// <summary>
        /// Convenience: encode string with no-XOR 6-bit for LegendMir5 C++ client.
        /// </summary>
        public static string EncodeStringDirect(string str)
        {
            var bSrc = HUtil32.GetBytes(str);
            var encBuf = new byte[bSrc.Length * 2 + 4];
            var len = Encode6BitBufDirect(bSrc, bSrc.Length, encBuf);
            return HUtil32.GetString(encBuf, 0, len);
        }

        /// <summary>
        /// 6-bit decode without XOR, matching C++ LegendMir5 client Decode6BitBuf.
        /// Inverse of Encode6BitBufDirect.
        /// </summary>
        public static byte[] Decode6BitBufDirect(byte[] source, int srcLen)
        {
            int decodeLen = 0;
            return Decode6BitBufDirect(source, srcLen, ref decodeLen);
        }

        public static byte[] Decode6BitBufDirect(byte[] source, int srcLen, ref int decodeLen)
        {
            byte[] masks = { 0, 0, 0xFC, 0xF8, 0xF0, 0xE0, 0xC0 };
            int nBitPos = 2;
            int nMadeBit = 0;
            int nBufPos = 0;
            int btTmp = 0;
            var buf = new byte[srcLen + 8];

            for (int i = 0; i < srcLen; i++)
            {
                int btCh = source[i] - byBase;
                if (btCh < 0)
                    break;

                if ((nMadeBit + 6) >= 8)
                {
                    byte btByte = (byte)(btTmp | ((btCh & 0x3F) >> (6 - nBitPos)));
                    if (nBufPos >= buf.Length)
                        break;
                    buf[nBufPos++] = btByte;
                    nMadeBit = 0;
                    if (nBitPos < 6)
                    {
                        nBitPos += 2;
                    }
                    else
                    {
                        nBitPos = 2;
                        continue;
                    }
                }

                btTmp = (btCh << nBitPos) & masks[nBitPos];
                nMadeBit += 8 - nBitPos;
            }

            decodeLen = nBufPos;
            var result = new byte[nBufPos];
            System.Buffer.BlockCopy(buf, 0, result, 0, nBufPos);
            return result;
        }

    }
}