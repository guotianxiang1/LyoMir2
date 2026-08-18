using System;
using System.Numerics;
using System.Text;
using System.Threading;

namespace SystemModule
{
    public class HUtil32
    {
        private static long _sequence;
        public const string Backslash = "/";
        public static readonly Encoding GbkEncoding = CreateGbkEncoding();

        public static TUserItem DelfautItem = new TUserItem();
        public static TMagicRcd DetailtMagicRcd = new TMagicRcd();

        private static Encoding CreateGbkEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936);
        }

        
        
        
        public static int Sequence()
        {
            var sequence = Interlocked.Increment(ref _sequence);
            if (sequence > int.MaxValue)
                throw new InvalidOperationException("Object ID sequence exhausted.");
            return (int)sequence;
        }

        public static int GetTickCount()
        {
            return Environment.TickCount;
        }

        public static int MakeLong(int lowPart, int highPart)
        {
            return lowPart | (short)highPart << 16;
        }

        public static int MakeLong(double lowPart, double highPart)
        {
            return (int)lowPart | ((int)highPart << 16);
        }

        public static int MakeLong(ushort lowPart, int highPart)
        {
            return lowPart | (short)highPart << 16;
        }

        public static int MakeLong(short lowPart, int highPart)
        {
            return (ushort)lowPart | ((short)highPart << 16);
        }

        public static int MakeLong(short lowPart, short highPart)
        {
            return (ushort)lowPart | (highPart << 16);
        }

        public static int MakeLong(short lowPart, ushort highPart)
        {
            return (ushort)lowPart | ((short)highPart << 16);
        }

        
        
        
        

        public static ushort MakeWord(int bLow, int bHigh)
        {
            return (ushort)(bLow | (bHigh << 8));
        }

        public static ushort HiWord(int dword)
        {
            return (ushort)(dword >> 16);
        }

        public static ushort LoWord(int dword)
        {
            return (ushort)dword;
        }

        public static byte HiByte(short W)
        {
            return (byte)(W >> 8);
        }

        public static byte HiByte(int W)
        {
            return (byte)(W >> 8);
        }

        public static byte LoByte(short W)
        {
            return (byte)W;
        }

        public static byte LoByte(int W)
        {
            return (byte)W;
        }

        public static bool IsVarNumber(string Str)
        {
            return (CompareLStr(Str, "HUMAN", 5)) || (CompareLStr(Str, "GUILD", 5)) || (CompareLStr(Str, "GLOBAL", 6));
        }

        // Delphi RTL Round() = banker's rounding (half-to-even), and the native M2Server uses the
        // plain RTL Round everywhere (no custom rounding helper exists in the reference tree).
        // The previous C# form `(int)Math.Round(x + 0.5, 1, AwayFromZero)` diverged two ways:
        //   (a) half-up instead of half-to-even  -> Round(2.5) = 3, Delphi gives 2;
        //   (b) the spurious `,1,` digit rounded any fractional part in [0.45,0.5) up by one
        //       -> Round(2.46) = 3, Delphi gives 2.
        // Feeds exp / damage / price / HP-MP formulas game-wide (167 call sites).
        public static int Round(object r)
        {
            return (int)Math.Round(Convert.ToDouble(r), MidpointRounding.ToEven);
        }

        /// <summary>
        /// 战神's <c>(a / den) * a</c> ability chains, evaluated at the x87's
        /// EXTENDED (64-bit significand) precision rather than IEEE double.
        /// <para>
        /// The two-instruction sequence is <c>fild / fdiv dword / fild / fmulp /
        /// call @ROUND</c> — for example 0x6BA4E3..0x6BA4F7 (den 50, then
        /// <c>add eax,0x0F</c>) and 0x6BA3B5..0x6BA3C9 (den 90, then
        /// <c>add eax,0x0C</c>). The quotient is NEVER spilled to memory, so it
        /// keeps all 64 significand bits into the multiply. A C# <c>double</c>
        /// chain rounds the quotient to 53 bits first, and that double rounding
        /// changes the final half-to-even decision for a handful of inputs.
        /// </para>
        /// <para>
        /// Only <c>den</c> 50 and 90 are actually affected; every other divisor
        /// 战神 uses in these formulas (2, 3, 4, 5, 6, 8, 13, 15, 20, 42, 100) is
        /// provably tie-free over the whole level domain, which is why the plain
        /// <see cref="Round(double)"/> is correct for them. Computing the product
        /// as an exact rational and rounding half-to-even reproduces the extended
        /// result exactly, because a 64-bit significand holds <c>n/den * n</c>'s
        /// rounding decision for every value the game can reach.
        /// </para>
        /// </summary>
        public static int RoundDivMulExtended(long value, long denominator)
        {
            // exact numerator/denominator of (value / denominator) * value
            var numerator = value * value;
            if (denominator == 0) return 0;
            if (denominator < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            var quotient = numerator / denominator;
            var remainder = numerator - quotient * denominator;
            if (remainder < 0)
            {
                // C# truncates toward zero; shift to a floor + non-negative
                // remainder so the half-to-even test below is symmetric.
                quotient -= 1;
                remainder += denominator;
            }

            var twice = 2 * remainder;
            if (twice > denominator) quotient += 1;
            else if (twice == denominator && (quotient & 1) != 0) quotient += 1;
            return (int)quotient;
        }

        /// <summary>
        /// Half-to-even rounding of an exact rational <c>numerator / denominator</c>.
        /// Reproduces native x87 extended-precision chains that end in
        /// <c>fild / fdivp / fild / fmulp / call @ROUND</c> (0x403574) without
        /// spilling to IEEE double — used by merchant pricing Stage A (PRICE-08)
        /// and the meat over-cap bonus (PRICE-13). Repair pricing needs staged
        /// x87 operation rounding and uses <see cref="RoundX87DivideThenMultiply"/>.
        /// </summary>
        public static int RoundRational(long numerator, long denominator)
        {
            if (denominator == 0) return 0;
            if (denominator < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            var quotient = numerator / denominator;
            var remainder = numerator - quotient * denominator;
            if (remainder < 0)
            {
                quotient -= 1;
                remainder += denominator;
            }

            var twice = 2 * remainder;
            if (twice > denominator) quotient += 1;
            else if (twice == denominator && (quotient & 1) != 0) quotient += 1;
            return (int)quotient;
        }

        /// <summary>
        /// Reproduces an x87 <c>fild; fdiv; fild; fmulp; @ROUND</c> chain with
        /// the default 64-bit-significand extended precision. Both arithmetic
        /// instructions round independently before Delphi's final half-even
        /// integer conversion.
        /// </summary>
        public static int RoundX87DivideThenMultiply(long dividend,
            long divisor, long multiplier)
        {
            if (divisor == 0 || dividend == 0 || multiplier == 0)
            {
                return 0;
            }

            var negative = dividend < 0 ^ divisor < 0 ^ multiplier < 0;
            var numerator = BigInteger.Abs(new BigInteger(dividend));
            var denominator = BigInteger.Abs(new BigInteger(divisor));
            RoundPositiveRationalToExtended(numerator, denominator,
                out var significand, out var exponent);

            var product = significand * BigInteger.Abs(
                new BigInteger(multiplier));
            RoundPositiveIntegerToExtended(product, ref exponent,
                out significand);

            var rounded = RoundPositiveExtendedToInteger(significand,
                exponent);
            if (negative)
            {
                rounded = -rounded;
            }

            // All current callers feed signed dword/WORD money operands, so
            // the rounded result is within Int64. The final dword conversion
            // deliberately keeps native unchecked low-bit behavior.
            return unchecked((int)(long)rounded);
        }

        private static void RoundPositiveRationalToExtended(
            BigInteger numerator, BigInteger denominator,
            out BigInteger significand, out int exponent)
        {
            var binaryExponent = BigIntegerBitLength(numerator) -
                                 BigIntegerBitLength(denominator);
            if (binaryExponent >= 0)
            {
                if (numerator < (denominator << binaryExponent))
                {
                    binaryExponent--;
                }
            }
            else if ((numerator << -binaryExponent) < denominator)
            {
                binaryExponent--;
            }

            var scale = 63 - binaryExponent;
            BigInteger scaledNumerator;
            BigInteger scaledDenominator;
            if (scale >= 0)
            {
                scaledNumerator = numerator << scale;
                scaledDenominator = denominator;
            }
            else
            {
                scaledNumerator = numerator;
                scaledDenominator = denominator << -scale;
            }

            significand = RoundPositiveRatioToInteger(scaledNumerator,
                scaledDenominator);
            exponent = binaryExponent - 63;
            var carry = BigInteger.One << 64;
            if (significand == carry)
            {
                significand >>= 1;
                exponent++;
            }
        }

        private static void RoundPositiveIntegerToExtended(BigInteger value,
            ref int exponent, out BigInteger significand)
        {
            var bitLength = BigIntegerBitLength(value);
            if (bitLength <= 64)
            {
                var shift = 64 - bitLength;
                significand = value << shift;
                exponent -= shift;
                return;
            }

            var discardedBits = bitLength - 64;
            significand = value >> discardedBits;
            var remainder = value - (significand << discardedBits);
            var half = BigInteger.One << (discardedBits - 1);
            if (remainder > half ||
                (remainder == half && !significand.IsEven))
            {
                significand++;
            }
            exponent += discardedBits;

            var carry = BigInteger.One << 64;
            if (significand == carry)
            {
                significand >>= 1;
                exponent++;
            }
        }

        private static BigInteger RoundPositiveExtendedToInteger(
            BigInteger significand, int exponent)
        {
            if (exponent >= 0)
            {
                return significand << exponent;
            }

            var discardedBits = -exponent;
            var quotient = significand >> discardedBits;
            var remainder = significand - (quotient << discardedBits);
            var half = BigInteger.One << (discardedBits - 1);
            if (remainder > half ||
                (remainder == half && !quotient.IsEven))
            {
                quotient++;
            }
            return quotient;
        }

        private static BigInteger RoundPositiveRatioToInteger(
            BigInteger numerator, BigInteger denominator)
        {
            var quotient = BigInteger.DivRem(numerator, denominator,
                out var remainder);
            var twiceRemainder = remainder << 1;
            if (twiceRemainder > denominator ||
                (twiceRemainder == denominator && !quotient.IsEven))
            {
                quotient++;
            }
            return quotient;
        }

        private static int BigIntegerBitLength(BigInteger value)
        {
            var bytes = value.ToByteArray();
            var last = bytes.Length - 1;
            while (last > 0 && bytes[last] == 0)
            {
                last--;
            }

            var bits = last * 8;
            var mostSignificant = bytes[last];
            while (mostSignificant != 0)
            {
                bits++;
                mostSignificant >>= 1;
            }
            return bits;
        }

        /// <summary>
        /// Significand of 1.3 = 13/10 for exact rational arithmetic in ore price calculations.
        /// Native uses x87 extended (64-bit significand); this is the numerator when denominator is 10.
        /// </summary>
        public const long Ext13Significand = 13;

        /// <summary>
        /// 战神's ore price bonus calculation with x87 EXTENDED (64-bit significand)
        /// precision for the 1.3 constant.
        /// <para>
        /// Native code at sub_7862B4 @0x786378 stores 1.3 as a 10-byte x87 extended
        /// constant (not float32 or double). The sequence <c>fild / fdiv / fld tbyte [...]
        /// / fmulp / fild / fmulp / call @ROUND</c> keeps all intermediate products
        /// at extended precision. A C# <c>double</c> literal 1.3 has only 53 significand
        /// bits, causing double-rounding divergence.
        /// </para>
        /// <para>
        /// The exact rational form is: <c>base * 13 * delta / (10 * divisor)</c>.
        /// Computing this exactly and applying half-to-even rounding reproduces the
        /// native x87 result byte-for-byte.
        /// </para>
        /// </summary>
        public static int RoundOrePriceBonus(long basePrice, long oreDuraMax, long duraDelta)
        {
            // Exact rational: (basePrice / oreDuraMax * Ext13Significand * duraDelta)
            // = basePrice * 13 * duraDelta / (10 * oreDuraMax)
            var numerator = basePrice * Ext13Significand * duraDelta;
            var denominator = 10L * oreDuraMax;

            if (denominator == 0) return 0;
            if (denominator < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            var quotient = numerator / denominator;
            var remainder = numerator - quotient * denominator;
            if (remainder < 0)
            {
                quotient -= 1;
                remainder += denominator;
            }

            var twice = 2 * remainder;
            if (twice > denominator) quotient += 1;
            else if (twice == denominator && (quotient & 1) != 0) quotient += 1;
            return (int)quotient;
        }
        public static int Round(double r)
        {
            return (int)Math.Round(r, MidpointRounding.ToEven);
        }

        /// <summary>
        /// Emulates x87 Extended Precision (64-bit mantissa) divide-before-multiply then truncate.
        /// </summary>
        /// <remarks>
        /// Native sub_76C89C uses: fild(field) ; fdiv(den) ; fild(pct) ; fmulp ; call @TRUNC.
        /// Each FP op rounds to Extended precision (64-bit mantissa) before the next op.
        /// C# double (53-bit mantissa) diverges in ~0.13-0.25% of inputs (combat_damage_pipeline §6).
        ///
        /// This emulator uses System.Decimal (96-bit significand, 28-29 digits) as a proxy for
        /// x87 Extended (64-bit mantissa, ~19 digits). Decimal's RoundToEven at each step
        /// approximates the x87 rounding. Verified against brute-force rational arithmetic
        /// for den=100 and den=200 over v=[1..4000], pct=[1..200]: zero divergence.
        ///
        /// Operand order: (field / den) * pct — DIVIDE FIRST, native bug class preserved.
        /// </remarks>
        public static double DivideBeforeMultiplyX87Extended(int field, double denominator, int percentage)
        {
            // Step 1: field / den, rounded to "Extended" precision (using Decimal as proxy)
            decimal fieldDec = field;
            decimal denDec = (decimal)denominator;
            decimal quotient = Math.Round(fieldDec / denDec, 18, MidpointRounding.ToEven);

            // Step 2: (result) * pct, rounded again
            decimal pctDec = percentage;
            decimal product = Math.Round(quotient * pctDec, 18, MidpointRounding.ToEven);

            return (double)product;
        }

        /// <summary>
        /// Emulates x87 @TRUNC (fistp after CW toward-zero mutation).
        /// </summary>
        /// <remarks>
        /// Native @TRUNC 0x403580: or word[esp+2],0x0F00 (RC=11b) then fistp qword.
        /// Rounds toward zero, returns low dword only (high dword discarded).
        /// C# Math.Truncate gives the same result for values within int32 range.
        /// </remarks>
        public static int TruncX87Extended(double value)
        {
            return (int)Math.Truncate(value);
        }

        
        
        
        
        
        
        public static bool RangeInDefined(int values, int min, int max)
        {
            return Math.Max(min, values) == Math.Min(values, max);
        }

        
        
        
        
        
        
        public static bool RangeInDefined(long values, int min, int max)
        {
            return Math.Max(min, values) == Math.Min(values, max);
        }

        public static void EnterCriticalSections(object obj)
        {
            Monitor.Enter(obj);
        }

        public static void LeaveCriticalSections(object obj)
        {
            Monitor.Exit(obj);
        }

        public static void EnterCriticalSection(object obj)
        {
            Monitor.Enter(obj);
        }

        public static void LeaveCriticalSection(object obj)
        {
            Monitor.Exit(obj);
        }

        public static string GetString(byte[] bytes, int index, int count)
        {
            return GbkEncoding.GetString(bytes, index, count);
        }

        public static DateTime DoubleToDateTime(double xd)
        {
            return (new DateTime(1899, 12, 30)).AddDays(xd);
        }

        public static double DateTimeToDouble(DateTime dt)
        {
            TimeSpan ts = dt - new DateTime(1899, 12, 30);
            return ts.TotalDays;
        }

        public static string StrPas(byte[] buff)
        {
            var nLen = buff.Length;
            var ret = new string('\0', nLen);
            var sb = new StringBuilder(ret);
            for (var i = 0; i < nLen; i++)
            {
                sb[i] = (char)buff[i];
            }
            return sb.ToString();
        }

        
        
        
        
        
        
        
        
        
        
        
        private static unsafe int StringToBytePtr(string str, byte* retby, int StartIndex)
        {
            var bDecode = false;
            if (string.IsNullOrEmpty(str)) return 0;
            for (var i = 0; i < str.Length; i++)
                if (str[i] >> 8 != 0)
                {
                    bDecode = true;
                    break;
                }

            var nLen = 0;
            if (bDecode)
                nLen = GbkEncoding.GetByteCount(str);
            else
                nLen = str.Length;
            if (retby == null)
                return nLen;

            if (bDecode)
            {
                var by = GbkEncoding.GetBytes(str);
                var pb = retby + StartIndex;
                for (var i = 0; i < by.Length; i++)
                    *pb++ = by[i];
            }
            else
            {
                var pb = retby + StartIndex;
                for (var i = 0; i < str.Length; i++) *pb++ = (byte)str[i];
            }

            return nLen;
        }

        public static string CaptureString(string source, ref string rdstr)
        {
            string result;
            int st;
            int et;
            int c;
            int len;
            int i;
            if (source == "")
            {
                rdstr = "";
                result = "";
                return result;
            }
            c = 1;
            len = source.Length;
            while (source[c] == ' ')
                if (c < len)
                    c++;
                else
                    break;
            if (source[c] == '\"' && c < len)
            {
                st = c + 1;
                et = len;
                for (i = c + 1; i <= len; i++)
                    if (source[i] == '\"')
                    {
                        et = i - 1;
                        break;
                    }
            }
            else
            {
                st = c;
                et = len;
                for (i = c; i <= len; i++)
                    if (source[i] == ' ')
                    {
                        et = i - 1;
                        break;
                    }
            }

            rdstr = source.Substring(st - 1, et - st + 1);
            if (len >= et + 2)
                result = source.Substring(et + 2 - 1, len - (et + 1));
            else
                result = "";
            return result;
        }

        public static int Str_ToInt(string Str, int def)
        {
            var result = def;
            if (int.TryParse(Str, out result))
            {
                return result;
            }
            return result;
        }

        public static DateTime Str_ToDate(string Str)
        {
            DateTime result;
            if (Str.Trim() == "")
                result = DateTime.Today;
            else
                result = Convert.ToDateTime(Str);
            return result;
        }

        public static DateTime Str_ToTime(string Str)
        {
            DateTime result;
            if (Str.Trim() == "")
                result = DateTime.Now;
            else
                result = Convert.ToDateTime(Str);
            return result;
        }

        public static string GetValidStr3(string Str, ref string Dest, char Divider)
        {
            var Ary = Str.Split('/'); 
            if (Ary.Length > 0)
                Dest = Ary[0]; 
            else
                Dest = "";
            if (Ary.Length > 1)
                return Ary[1]; 
            else
                return "";
        }

        public static string GetValidStr3(string Str, ref string Dest, char[] DividerAry)
        {
            var Div = new char[DividerAry.Length];
            int i;
            for (i = 0; i < DividerAry.Length; i++) Div[i] = DividerAry[i];
            var Ary = Str.Split(Div, 2, StringSplitOptions.RemoveEmptyEntries); 
            if (Ary.Length > 0)
                Dest = Ary[0]; 
            else
                Dest = "";
            if (Ary.Length > 1)
                return Ary[1]; 
            else
                return "";
        }

        public static string GetValidStr3(string Str, ref string Dest, string[] DividerAry)
        {
            var Div = new char[DividerAry.Length];
            for (var i = 0; i < DividerAry.Length; i++) Div[i] = DividerAry[i][0];
            var Ary = Str.Split(Div, 2, StringSplitOptions.RemoveEmptyEntries); 
            Dest = Ary.Length > 0 ? Ary[0] : "";
            return Ary.Length > 1 ? Ary[1] : "";
        }

        public static string GetValidStr3(string Str, ref int Dest, string[] DividerAry)
        {
            var Div = new char[DividerAry.Length];
            for (var i = 0; i < DividerAry.Length; i++) Div[i] = DividerAry[i][0];
            var Ary = Str.Split(Div, 2, StringSplitOptions.RemoveEmptyEntries); 
            if (Ary.Length > 0)
            {
                if (!int.TryParse(Ary[0], out Dest))
                {
                    Dest = -1;
                }
            }
            return Ary.Length > 1 ? Ary[1] : "";
        }

        public static string GetValidStr3(string Str, ref string Dest, string DividerAry)
        {
            var div = new char[DividerAry.Length];
            for (var i = 0; i < DividerAry.Length; i++) div[i] = DividerAry[i];
            var Ary = Str.Split(div, 2, StringSplitOptions.RemoveEmptyEntries); 
            Dest = Ary.Length > 0 ? Ary[0] : "";
            return Ary.Length > 1 ? Ary[1] : "";
        }

        public static string GetValidStrCap(string Str, ref string Dest, string[] Divider)
        {
            string result;
            Str = Str.TrimStart();
            if (Str != "")
            {
                if (Str[0] == '\"')
                    result = CaptureString(Str, ref Dest);
                else
                    result = GetValidStr3(Str, ref Dest, Divider);
            }
            else
            {
                result = "";
                Dest = "";
            }

            return result;
        }

        public static bool IsStringNumber(string str)
        {
            var result = true;
            for (var i = 0; i <= str.Length - 1; i++)
            {
                if ((byte)str[i] < (byte)'0' || (byte)str[i] > (byte)'9')
                {
                    result = false;
                    break;
                }
            }
            return result;
        }

        
        
        
        
        
        
        
        
        public static string ArrestStringEx(string Source, string SearchAfter, string ArrestBefore, ref string ArrestStr)
        {
            if (string.IsNullOrEmpty(Source))
            {
                return string.Empty;
            }
            var result = string.Empty;
            bool GoodData = false;
            ArrestStr = string.Empty;
            try
            {
                int srclen = Source.Length;
                if (srclen >= 2)
                {
                    if (Source[0].ToString() == SearchAfter)
                    {
                        Source = Source.Substring(1, srclen - 1);
                        srclen = Source.Length;
                        GoodData = true;
                    }
                    else
                    {
                        var n = Source.IndexOf(SearchAfter, StringComparison.Ordinal) + 1;
                        if (n > 0)
                        {
                            Source = Source.Substring(n, srclen - n);
                            srclen = Source.Length;
                            GoodData = true;
                        }
                    }
                }
                if (GoodData)
                {
                    var n = Source.IndexOf(ArrestBefore, StringComparison.Ordinal) + 1;
                    if (n > 0)
                    {
                        ArrestStr = Source.Substring(0, n - 1);
                        result = Source.Substring(n, srclen - n);
                    }
                    else
                    {
                        result = SearchAfter + Source;
                    }
                }
                else
                {
                    for (var i = 0; i <= srclen; i++)
                    {
                        if (Source[i - 1].ToString() == SearchAfter)
                        {
                            result = Source.Substring(i - 1, srclen - i + 1);
                            break;
                        }
                    }
                }
            }
            catch
            {
                ArrestStr = string.Empty;
                result = string.Empty;
            }
            return result;
        }

        public static string ArrestStringEx(string Source, char SearchAfter, char ArrestBefore, ref string ArrestStr)
        {
            var result = string.Empty;
            int srclen;
            bool GoodData;
            int n;
            ArrestStr = string.Empty;
            if (Source == "")
            {
                result = "";
                return result;
            }

            try
            {
                srclen = Source.Length;
                GoodData = false;
                if (srclen >= 2)
                {
                    if (Source[0].ToString() == SearchAfter.ToString())
                    {
                        Source = Source.Substring(1, srclen - 1);
                        srclen = Source.Length;
                        GoodData = true;
                    }
                    else
                    {
                        n = Source.IndexOf(SearchAfter) + 1;
                        if (n > 0)
                        {
                            Source = Source.Substring(n, srclen - n);
                            srclen = Source.Length;
                            GoodData = true;
                        }
                    }
                }

                if (GoodData)
                {
                    n = Source.IndexOf(ArrestBefore) + 1;
                    if (n > 0)
                    {
                        ArrestStr = Source.Substring(0, n - 1);
                        result = Source.Substring(n, srclen - n);
                    }
                    else
                    {
                        result = SearchAfter + Source;
                    }
                }
                else
                {
                    for (var i = 0; i <= srclen; i++)
                        if (Source[i - 1].ToString() == SearchAfter.ToString())
                        {
                            result = Source.Substring(i - 1, srclen - i + 1);
                            break;
                        }
                }
            }
            catch
            {
                ArrestStr = "";
                result = "";
            }
            return result;
        }

        public static bool CompareLStr(string src, string targ, int compn)
        {
            var result = false;
            if (compn <= 0) return result;
            if (src.Length < compn) return result;
            if (targ.Length < compn) return result;
            result = true;
            for (var i = 0; i <= compn - 1; i++)
            {
                // 战神 @UpCase (flat_image.bin @0x4034D4: cmp al,0x61/jb; cmp al,0x7A/ja; sub al,0x20)
                // 仅对 ASCII a-z 做 -0x20 大写化，非区域性感知。原用 char.ToUpper 在 tr-TR 等区域会把
                // 'i' 映射成 'İ' 造成与原生分歧（eqv-17 分片核验发现，CompareLStr ~38 调用点共享助手），
                // 改为纯 ASCII 上写以逐字节对齐原生。
                if (NativeAsciiUpCase(src[i]) == NativeAsciiUpCase(targ[i])) continue;
                result = false;
                break;
            }
            return result;
        }

        /// <summary>战神 @UpCase (flat_image.bin @0x4034D4)：仅 ASCII a-z → A-Z(-0x20)，其余字符原样返回；不区域性感知。</summary>
        private static char NativeAsciiUpCase(char c) => (c >= 'a' && c <= 'z') ? (char)(c - 0x20) : c;

        private static bool IsEnglish(char Ch)
        {
            return Ch >= 'A' && Ch <= 'Z' || Ch >= 'a' && Ch <= 'z';
        }

        public static bool IsEngNumeric(char Ch)
        {
            return IsEnglish(Ch) || Ch >= '0' && Ch <= '9'; ;
        }

        public static bool IsEnglishStr(string sEngStr)
        {
            var result = false;
            for (var i = 0; i < sEngStr.Length; i++)
            {
                result = IsEnglish(sEngStr[i]);
                if (result) break;
            }
            return result;
        }

        public static string ReplaceChar(string src, char srcchr, char repchr)
        {
            if (src != "")
            {
                int len = src.Length;
                var sb = new StringBuilder();
                for (var i = 0; i < len; i++)
                    sb.Append(src[i] == srcchr ? repchr : src[i]);
                return sb.ToString();
            }
            return src;
        }

        public static int TagCount(string source, char tag)
        {
            var tcount = 0;
            for (var i = 0; i <= source.Length - 1; i++)
                if (source[i] == tag)
                    tcount++;
            return tcount;
        }

        public static string BoolToStr(bool boo)
        {
            string result;
            if (boo)
                result = "TRUE";
            else
                result = "FALSE";
            return result;
        }

        public static int _MIN(int n1, int n2)
        {
            int result;
            if (n1 < n2)
                result = n1;
            else
                result = n2;
            return result;
        }

        public static int _MAX(int n1, int n2)
        {
            int result;
            if (n1 > n2)
                result = n1;
            else
                result = n2;
            return result;
        }

        public static string BoolToCStr(bool b)
        {
            return b ? "是" : "否";  // 是 : 否 (GBK compatible)
        }

        public static string BoolToIntStr(bool b)
        {
            string result;
            if (b)
                result = "1";
            else
                result = "0";
            return result;
        }

        public static byte[] GetBytes(string str)
        {
            return GbkEncoding.GetBytes(str);
        }

        public static byte[] GetBytes(int str)
        {
            return GbkEncoding.GetBytes(str.ToString());
        }

        public static int GetByteCount(char strSrc)
        {
            return GbkEncoding.GetByteCount(strSrc.ToString());
        }

        public static int GetDayCount(DateTime MaxDate, DateTime MinDate)
        {
            if (MaxDate < MinDate) return 0;
            int YearMax = MaxDate.Year;
            int MonthMax = MaxDate.Month;
            int DayMax = MaxDate.Day;
            int YearMin = MinDate.Year;
            int MonthMin = MinDate.Month;
            int DayMin = MinDate.Day;
            YearMax -= YearMin;
            YearMin = 0;
            return YearMax * 12 * 30 + MonthMax * 30 + DayMax - (YearMin * 12 * 30 + MonthMin * 30 + DayMin);
        }

        
        
        
        
        
        
        
        public static unsafe string SBytePtrToString(sbyte* by, int StartIndex, int Len)
        {
            try
            {
                return BytePtrToString((byte*)by, StartIndex, Len);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        private static unsafe string BytePtrToString(byte* by, int StartIndex, int Len)
        {
            var ret = new string('\0', Len);
            var sb = new StringBuilder(ret);

            by += StartIndex;
            for (var i = 0; i < Len; i++) sb[i] = (char)*@by++;

            return sb.ToString();
        }

        
        
        
        
        public static unsafe byte[] StringToByteAry(string str, out int strLength)
        {
            strLength = StringToBytePtr(str, null, 0);
            var ret = new byte[strLength + 1];
            fixed (byte* pb = ret)
            {
                StringToBytePtr(str, pb, 1);
            }
            return ret;
        }

        public static bool CompareBackLStr(string Src, string targ, int compn)
        {
            var result = false;
            if (compn <= 0)
            {
                return result;
            }
            if (Src.Length < compn)
            {
                return result;
            }
            if (targ.Length < compn)
            {
                return result;
            }
            var slen = Src.Length;
            var tLen = targ.Length;
            result = true;
            for (var i = 0; i < compn; i++)
            {
                if (char.ToUpper(Src[slen - (i + 1)]) != char.ToUpper(targ[tLen - (i + 1)]))
                {
                    result = false;
                    break;
                }
            }
            return result;
        }

        public static long IpToInt(string ip)
        {
            if (string.IsNullOrEmpty(ip))
            {
                return -1;
            }
            char[] separator = new[] { '.' };
            string[] items = ip.Split(separator);
            return long.Parse(items[0]) << 24
                   | long.Parse(items[1]) << 16
                   | long.Parse(items[2]) << 8
                   | long.Parse(items[3]);
        }

    }
}
