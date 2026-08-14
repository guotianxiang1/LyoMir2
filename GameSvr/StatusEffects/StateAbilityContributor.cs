using System;

namespace GameSvr.StatusEffects
{
    /// <summary>
    /// Native state ability contribution system
    ///
    /// Source: D:/loym2/staging/status_effects_spec_20260810.md §2.B
    /// Function: sub_7733C0 - walks obj+0xDC and accumulates ability modifications
    ///
    /// 86 states (0x15..0x6A) contribute to TNakedAbility accumulator
    /// 29 unique handlers dispatch by bytetab @0x773419 / jmptab @0x77346F
    ///
    /// CRITICAL CONTRACT:
    /// - Each handler adds record[+0x0A] (value field) to specific ability offsets
    /// - State 0x2A (multiplier) only applies when value == 1
    /// - State 0x2C (double) ignores value, doubles in-place
    /// - State 0x36 (subtract) floors at 0 via max(0, current - v)
    ///
    /// RECOMPUTE TRIGGER:
    /// - 37 states set byte[obj+0x438]=1 when gained/lost (bitmap @0x77326C)
    /// - Bitmap bias +8, covers states 0x0E, 0x15..0x2C, 0x2E..0x30, 0x4B..0x4E, 0x55, 0x5A..0x68
    /// </summary>
    public static class StateAbilityContributor
    {
        /// <summary>
        /// Ability accumulator structure (TNakedAbility layout)
        /// Offsets proven by native handlers in §2.B
        /// </summary>
        public class AbilityModifiers
        {
            // Base attributes (matched by handlers)
            public int Offset_18 = 0; // DC (攻击力)
            public int Offset_1C = 0; // MC (魔法力)
            public int Offset_20 = 0; // SC (道术)
            public int Offset_24 = 0; // AC (防御)
            public int Offset_28 = 0; // MAC (魔御)
            public int Offset_2C = 0; // DC max
            public int Offset_30 = 0; // MC max
            public int Offset_34 = 0; // SC max
            public int Offset_38 = 0; // AC max
            public int Offset_3C = 0; // MAC max
            public int Offset_40 = 0; // 命中
            public int Offset_44 = 0; // 闪避
            public int Offset_4C = 0; // HP
            public int Offset_54 = 0; // MP
            public int Offset_64 = 0; // 吸血
            public int Offset_6C = 0; // 反射
            public int Offset_74 = 0; // 神圣

            // Word-sized fields
            public ushort Offset_02 = 0; // 幸运 (state 0x26)
            public ushort Offset_0C = 0; // 准确 (state 0x27)
            public ushort Offset_0E = 0; // 敏捷 (state 0x6A)
            public ushort Offset_10 = 0; // 攻击速度 (state 0x23)

            public void Reset()
            {
                Offset_18 = Offset_1C = Offset_20 = Offset_24 = 0;
                Offset_28 = Offset_2C = Offset_30 = Offset_34 = 0;
                Offset_38 = Offset_3C = Offset_40 = Offset_44 = 0;
                Offset_4C = Offset_54 = Offset_64 = Offset_6C = Offset_74 = 0;
                Offset_02 = Offset_0C = Offset_0E = Offset_10 = 0;
            }
        }

        /// <summary>
        /// Recompute trigger bitmap @0x77326C, bias +8
        /// Raw bytes: 40 60 00 FF DF 01 00 00 78 20 7C BF 01 00 00 00
        ///
        /// Set bits (37 states): 0x0E, 0x15, 0x16, 0x20..0x2C, 0x2E, 0x2F, 0x30,
        /// 0x4B, 0x4C, 0x4D, 0x4E, 0x55, 0x5A..0x65, 0x67, 0x68
        /// </summary>
        private static readonly byte[] RecomputeTriggerBitmap = new byte[]
        {
            0x40, 0x60, 0x00, 0xFF, 0xDF, 0x01, 0x00, 0x00,
            0x78, 0x20, 0x7C, 0xBF, 0x01, 0x00, 0x00, 0x00
        };

        /// <summary>
        /// Check if a state triggers ability cache recomputation
        /// EA: 773254 - sub_773254(dl=stateId): Boolean
        /// </summary>
        public static bool TriggersRecompute(byte stateId)
        {
            // EA: 773254 add dl, 0xf8  (dl -= 8, bias)
            int biasedId = stateId - 8;

            // EA: 773257 cmp dl, 0x67
            // EA: 77325A ja 0x773266  (out of range -> false)
            if (biasedId < 0 || biasedId > 0x67)
                return false;

            // EA: 77325C and edx, 0x7f
            int bitIndex = biasedId & 0x7F;

            // EA: 77325F bt dword ptr [0x77326c], edx
            int byteIndex = bitIndex >> 3;
            int bitOffset = bitIndex & 0x07;

            if (byteIndex >= RecomputeTriggerBitmap.Length)
                return false;

            bool bitSet = (RecomputeTriggerBitmap[byteIndex] & (1 << bitOffset)) != 0;

            // EA: 773266 setb al  (CF=1 -> al=1)
            return bitSet;
        }

        /// <summary>
        /// Apply a single state's contribution to the ability accumulator
        /// Based on switch dispatch table @0x7733FD
        ///
        /// Parameters:
        /// - stateId: state id (bias 0x15 for table)
        /// - value: record[+0x0A] value field
        /// - acc: accumulator to modify
        /// - objField_0x278: word[edi+0x278] for states 0x15/0x16 calculation
        /// - objField_0x264: cached ability base for state 0x2A multiplier
        /// </summary>
        public static void ApplyContribution(byte stateId, int value, AbilityModifiers acc,
            ushort objField_0x278 = 0, AbilityModifiers objField_0x264 = null)
        {
            switch (stateId)
            {
                case 0x15: // EA: 0x7734FE
                    {
                        int calc = (objField_0x278 / 7) + 2;
                        acc.Offset_24 += calc;
                    }
                    break;

                case 0x16: // EA: 0x7734E3
                    {
                        int calc = (objField_0x278 / 7) + 2;
                        acc.Offset_1C += calc;
                    }
                    break;

                case 0x20: // EA: 0x773519
                    acc.Offset_28 += value;
                    acc.Offset_2C += value;
                    break;

                case 0x21: // EA: 0x77352A
                    acc.Offset_30 += value;
                    acc.Offset_34 += value;
                    break;

                case 0x22: // EA: 0x77353B
                    acc.Offset_38 += value;
                    acc.Offset_3C += value;
                    break;

                case 0x23: // EA: 0x77357F
                    acc.Offset_10 += (ushort)value;
                    break;

                case 0x24: // EA: 0x77358C
                    acc.Offset_4C += value;
                    break;

                case 0x25: // EA: 0x773597
                    acc.Offset_54 += value;
                    break;

                case 0x26: // EA: 0x7735A2
                    acc.Offset_02 += (ushort)value;
                    break;

                case 0x27: // EA: 0x7735AF
                    acc.Offset_0C += (ushort)value;
                    break;

                case 0x28: // EA: 0x77354C
                    acc.Offset_18 += value;
                    acc.Offset_1C += value;
                    break;

                case 0x29: // EA: 0x77355D
                    acc.Offset_20 += value;
                    acc.Offset_24 += value;
                    break;

                case 0x2A: // EA: 0x773636 - MULTIPLIER (only if value == 1)
                    if (value == 1 && objField_0x264 != null)
                    {
                        // x87 extended precision x 1.2
                        acc.Offset_28 = Truncate(objField_0x264.Offset_28 * 1.2);
                        acc.Offset_2C = Truncate(objField_0x264.Offset_2C * 1.2);
                        acc.Offset_30 = Truncate(objField_0x264.Offset_30 * 1.2);
                        acc.Offset_34 = Truncate(objField_0x264.Offset_34 * 1.2);
                        acc.Offset_38 = Truncate(objField_0x264.Offset_38 * 1.2);
                        acc.Offset_3C = Truncate(objField_0x264.Offset_3C * 1.2);

                        // float32 x 1.5
                        acc.Offset_18 = Truncate(objField_0x264.Offset_18 * 1.5f);
                        acc.Offset_1C = Truncate(objField_0x264.Offset_1C * 1.5f);
                        acc.Offset_20 = Truncate(objField_0x264.Offset_20 * 1.5f);
                        acc.Offset_24 = Truncate(objField_0x264.Offset_24 * 1.5f);
                        acc.Offset_4C = Truncate(objField_0x264.Offset_4C * 1.5f);
                        acc.Offset_54 = Truncate(objField_0x264.Offset_54 * 1.5f);
                    }
                    break;

                case 0x2B: // EA: 0x77356E
                    acc.Offset_38 += value;
                    acc.Offset_3C += value;
                    break;

                case 0x2C: // EA: 0x7735C9 - DOUBLE (ignores value)
                    acc.Offset_64 += acc.Offset_64;
                    acc.Offset_6C += acc.Offset_6C;
                    acc.Offset_74 += acc.Offset_74;
                    break;

                case 0x36: // EA: 0x7735E0 - SUBTRACT, floored at 0
                    acc.Offset_18 = Math.Max(0, acc.Offset_18 - value);
                    acc.Offset_1C = Math.Max(0, acc.Offset_1C - value);
                    acc.Offset_20 = Math.Max(0, acc.Offset_20 - value);
                    acc.Offset_24 = Math.Max(0, acc.Offset_24 - value);
                    break;

                case 0x4E: // EA: 0x773625
                    acc.Offset_40 += value;
                    acc.Offset_44 += value;
                    break;

                case 0x6A: // EA: 0x7735BC
                    acc.Offset_0E += (ushort)value;
                    break;

                // States 0x55, 0x5B..0x5E, 0x60, 0x61, 0x64, 0x67, 0x68 have handlers
                // but require more context (job, other object fields)
                // For now, return without contribution (caller should implement if needed)

                default:
                    // No contribution (default sink in switch)
                    break;
            }
        }

        /// <summary>
        /// @TRUNC helper - toward zero
        /// Native: call 0x403580
        /// </summary>
        private static int Truncate(double value)
        {
            return (int)Math.Truncate(value);
        }

        private static int Truncate(float value)
        {
            return (int)Math.Truncate(value);
        }
    }
}
