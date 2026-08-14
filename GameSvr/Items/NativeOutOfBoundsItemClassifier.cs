using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 战神 <c>sub_74DAE4</c> @0x74DAE4 — after the 208-byte record copy
    /// (<c>0x74DB3A lea edi,[ebx+0x20]</c> / <c>0x74DB42 rep movsd</c>), the factory
    /// may set <c>[item+0xFC]</c> when instance attribute bytes exceed the limits for
    /// the template StdMode/Shape class.  Non-zero <c>+0xFC</c> rejects trade mode 2
    /// (<c>0x783911 cmp byte [ebx+0xFC],0</c>) and inverts drop mode 5
    /// (<c>0x783940</c> — reject unless <c>+0xFC!=0</c>).
    /// </summary>
    internal static class NativeOutOfBoundsItemClassifier
    {
        /// <summary><c>0x74DBA6 test byte [[0x7D7038]+1],4</c> — classifier runs only when set.</summary>
        private const int ConfigByteOffset = 1;

        private const byte ConfigByteMask = 0x04;

        /// <summary>
        /// <c>sub_7849E4</c> @0x7849EB — returns true when <c>Reserved02 &amp; 0x0080</c>,
        /// which skips the classifier (<c>0x74DBB9 jne 0x74DDF6</c>).
        /// </summary>
        private const ushort StdSkipMask = 0x0080;

        internal static void Apply(TUserItem item, GoodItem stdItem)
        {
            if (item == null || stdItem == null)
            {
                return;
            }

            item.NativeClassFc = 0;

            if ((stdItem.NativeReserved02 & StdSkipMask) != 0)
            {
                return;
            }

            if (M2Share.ServerSwitches?.IsBitSet(ConfigByteOffset, ConfigByteMask) != true)
            {
                return;
            }

            item.NativeClassFc = Evaluate(stdItem.StdMode, stdItem.Shape, item.btValue);
        }

        /// <summary>Public for unit-style audit without constructing items.</summary>
        internal static byte Evaluate(byte stdMode, byte shape, byte[] btValue)
        {
            if (btValue == null || btValue.Length < 8)
            {
                return 0;
            }

            var caseIndex = stdMode switch
            {
                5 or 6 => 1,
                10 or 11 or 15 or 19 or 20 or 21 or 22 or 23 or 24 or 26 => 2,
                27 or 28 => 3,
                30 => 4,
                _ => 0
            };

            return caseIndex switch
            {
                0 => 0,
                1 => EvaluateCase1(btValue),
                2 => EvaluateCase2(stdMode, shape, btValue),
                3 => EvaluateCase3(shape, btValue),
                4 => EvaluateLimits(btValue, 6, 6, 6, 6, 6),
                _ => 0
            };
        }

        // case 1 @0x74DC13 — StdMode 5/6 (weapon / weapon-like).
        private static byte EvaluateCase1(byte[] v)
        {
            if (v[0] > 0x14 || v[1] > 0x14 || v[2] > 0x14)
            {
                return 1;
            }

            if (Abs(v[5]) > 6 || Abs(v[6]) > 0x0E || Abs(v[7]) > 7)
            {
                return 1;
            }

            return 0;
        }

        // case 2 @0x74DC63 — StdMode 10..26 subset.
        private static byte EvaluateCase2(byte stdMode, byte shape, byte[] v)
        {
            // 0x74DC66 add dl,0xF6 / 0x74DC69 sub dl,2 / 0x74DC6C jae — fall through
            // only when dl was 0 or 1 before sub (StdMode 10/11).
            var dl = (byte)(stdMode + 0xF6);
            if (dl < 2)
            {
                // 0x74DC79 mov [ebx+0xFC],0 when Shape is 0x1C or 0x26.
                if (shape is 0x1C or 0x26)
                {
                    return 0;
                }
            }

            if (stdMode == 0x0F && shape == 0x84)
            {
                return EvaluateLimits(v, 0x0C, 8, 6, 6, 6);
            }

            if (stdMode == 0x16 && shape == 0x82)
            {
                return EvaluateLimits(v, 6, 6, 0x0C, 0x0C, 0x0C);
            }

            if (stdMode == 0x1A && shape == 0x83)
            {
                return EvaluateLimits(v, 8, 8, 8, 8, 8);
            }

            return EvaluateLimits(v, 6, 6, 6, 6, 6);
        }

        // case 3 @0x74DD65 — StdMode 27/28 (belt/boots).
        private static byte EvaluateCase3(byte shape, byte[] v)
        {
            var t = (byte)(shape + 0x4C);
            t = (byte)(t - 3);
            if ((sbyte)t < 0)
            {
                return EvaluateLimits(v, 8, 8, 8, 8, 8);
            }

            t = (byte)(t + 0xF9);
            t = (byte)(t - 3);
            if (t >= 0x80)
            {
                return EvaluateLimits(v, 6, 6, 6, 6, 6);
            }

            return EvaluateLimits(v, 8, 8, 8, 8, 8);
        }

        private static byte EvaluateLimits(byte[] v, int l0, int l1, int l2, int l3, int l4)
        {
            if (v[0] > l0 || v[1] > l1 || v[2] > l2 || v[3] > l3 || v[4] > l4)
            {
                return 1;
            }

            return 0;
        }

        private static int Abs(byte value)
        {
            var signed = unchecked((sbyte)value);
            return signed < 0 ? -signed : signed;
        }
    }
}
