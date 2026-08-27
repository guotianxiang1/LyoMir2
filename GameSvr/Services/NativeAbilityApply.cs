using System;

namespace GameSvr
{
    /// <summary>
    /// sub_78E830 — apply one named ability bonus.
    ///
    /// 0x78E83E `cmp ecx,0xFE` / `ja 0x78F329` drops any code above 254, and
    /// 0x78E84A `jmp [ecx*4 + 0x78E851]` selects one of 255 arms; 112
    /// of those point at the bare epilogue, leaving 143 real arms. The
    /// register contract is EBX = container+0x48 (base block), [ebp-4] =
    /// container+0x1F8 (added block), ESI = value.
    ///
    /// The blocks are handled as raw spans at the native offsets rather than as
    /// named fields: every arm is a fixed-width read-modify-write at a constant
    /// displacement, so working on the bytes keeps the result identical without
    /// having to first agree on a field layout.
    ///
    /// Widths wrap exactly as the native `add` does; the Max arms go through
    /// 0x4C7004, which returns the larger of the zero-extended current value and
    /// the signed value before the result is truncated back to the field width.
    ///
    /// Table generated from flat_image.bin.
    /// </summary>
    public static class NativeAbilityApply
    {
        /// <summary>0x78E83E `cmp ecx,0xFE`.</summary>
        public const int MaxCode = 0xFE;

        /// <summary>Native container displacement of the base ability block.</summary>
        public const int BaseBlockOffset = 0x48;

        /// <summary>Native container displacement of the added ability block.</summary>
        public const int AddedBlockOffset = 0x1F8;

        /// <summary>
        /// AddedBlockOffset - BaseBlockOffset. The highest base-block byte any arm
        /// touches is 0x1AF (code 158), i.e. the table stops exactly where
        /// the added block starts, which is what pins the block boundary.
        /// </summary>
        public const int BaseBlockSize = 0x1B0;

        /// <summary>Highest added-block byte any arm touches, plus one.</summary>
        public const int AddedBlockUsed = 0x35;

        /// <summary>0x78F03F `and edx,0x7F` — code 254 keys on the low 7 bits.</summary>
        public const int NestedCode = 254;
        private const int NestedMask = 0x7F;
        private const int NestedBound = 6;

        /// <summary>0x78F052 table: each selector sets one added-block byte to 1.</summary>
        private static readonly int[] _nestedFlagOffsets = { 0x0B, 0x05, 0x21, 0x13, 0x0C, 0x04, 0x06 };

        private enum Blk : byte { Base = 0, Added = 1 }

        private enum Op : byte { Add = 0, Max = 1, SetOne = 2 }

        private readonly struct Arm
        {
            public readonly Blk Block;
            public readonly ushort Offset;
            public readonly byte Width;
            public readonly Op Kind;

            /// <summary>Native post-store ceiling (`cmp .. / jbe / mov .., K`), or -1.</summary>
            public readonly int Clamp;

            public readonly bool Live;

            public Arm(Blk block, int offset, int width, Op kind, int clamp)
            {
                Block = block;
                Offset = (ushort)offset;
                Width = (byte)width;
                Kind = kind;
                Clamp = clamp;
                Live = true;
            }
        }

        private static readonly Arm[] _arms = BuildArms();

        private static Arm[] BuildArms()
        {
            var a = new Arm[MaxCode + 1];
            a[1] = new Arm(Blk.Base, 0x01C, 4, Op.Add, -1);
            a[2] = new Arm(Blk.Base, 0x020, 4, Op.Add, -1);
            a[3] = new Arm(Blk.Base, 0x024, 4, Op.Add, -1);
            a[4] = new Arm(Blk.Base, 0x028, 4, Op.Add, -1);
            a[5] = new Arm(Blk.Base, 0x02C, 4, Op.Add, -1);
            a[6] = new Arm(Blk.Base, 0x030, 4, Op.Add, -1);
            a[7] = new Arm(Blk.Base, 0x00C, 4, Op.Add, -1);
            a[8] = new Arm(Blk.Base, 0x010, 4, Op.Add, -1);
            a[9] = new Arm(Blk.Base, 0x014, 4, Op.Add, -1);
            a[10] = new Arm(Blk.Base, 0x018, 4, Op.Add, -1);
            a[11] = new Arm(Blk.Base, 0x000, 4, Op.Add, -1);
            a[12] = new Arm(Blk.Base, 0x004, 4, Op.Add, -1);
            a[13] = new Arm(Blk.Base, 0x008, 2, Op.Add, -1);
            a[14] = new Arm(Blk.Base, 0x00A, 2, Op.Add, -1);
            a[15] = new Arm(Blk.Base, 0x044, 2, Op.Add, -1);
            a[16] = new Arm(Blk.Base, 0x046, 1, Op.Add, -1);
            a[17] = new Arm(Blk.Base, 0x047, 1, Op.Add, -1);
            a[18] = new Arm(Blk.Base, 0x04C, 2, Op.Add, -1);
            a[19] = new Arm(Blk.Base, 0x05C, 2, Op.Add, -1);
            a[20] = new Arm(Blk.Base, 0x05E, 2, Op.Add, -1);
            a[21] = new Arm(Blk.Base, 0x060, 2, Op.Add, -1);
            a[22] = new Arm(Blk.Base, 0x064, 2, Op.Add, -1);
            a[23] = new Arm(Blk.Base, 0x066, 2, Op.Add, -1);
            a[24] = new Arm(Blk.Base, 0x068, 2, Op.Add, -1);
            a[25] = new Arm(Blk.Base, 0x06A, 2, Op.Add, -1);
            a[26] = new Arm(Blk.Base, 0x070, 2, Op.Add, -1);
            a[27] = new Arm(Blk.Base, 0x072, 2, Op.Add, -1);
            a[28] = new Arm(Blk.Base, 0x074, 2, Op.Add, -1);
            a[29] = new Arm(Blk.Base, 0x06C, 4, Op.Add, -1);
            a[30] = new Arm(Blk.Base, 0x03C, 2, Op.Add, -1);
            a[31] = new Arm(Blk.Base, 0x04A, 2, Op.Add, -1);
            a[32] = new Arm(Blk.Base, 0x0FA, 2, Op.Add, -1);
            a[33] = new Arm(Blk.Base, 0x0F8, 2, Op.Add, -1);
            a[34] = new Arm(Blk.Base, 0x0D8, 4, Op.Add, -1);
            a[35] = new Arm(Blk.Base, 0x076, 2, Op.Add, -1);
            a[36] = new Arm(Blk.Base, 0x078, 2, Op.Add, -1);
            a[37] = new Arm(Blk.Base, 0x07C, 4, Op.Add, -1);
            a[39] = new Arm(Blk.Base, 0x086, 2, Op.Add, -1);
            a[40] = new Arm(Blk.Base, 0x088, 4, Op.Add, -1);
            a[41] = new Arm(Blk.Base, 0x08C, 4, Op.Add, -1);
            a[42] = new Arm(Blk.Base, 0x090, 4, Op.Add, -1);
            a[43] = new Arm(Blk.Base, 0x094, 4, Op.Add, -1);
            a[44] = new Arm(Blk.Base, 0x098, 4, Op.Add, -1);
            a[45] = new Arm(Blk.Base, 0x09C, 4, Op.Add, -1);
            a[46] = new Arm(Blk.Base, 0x0B0, 2, Op.Add, -1);
            a[47] = new Arm(Blk.Base, 0x0B2, 2, Op.Add, -1);
            a[48] = new Arm(Blk.Base, 0x0BC, 4, Op.Add, -1);
            a[49] = new Arm(Blk.Base, 0x0C0, 4, Op.Add, -1);
            a[50] = new Arm(Blk.Base, 0x0CC, 2, Op.Add, -1);
            a[52] = new Arm(Blk.Base, 0x0CE, 2, Op.Add, -1);
            a[53] = new Arm(Blk.Base, 0x0D0, 2, Op.Add, -1);
            a[54] = new Arm(Blk.Base, 0x0B6, 2, Op.Add, -1);
            a[55] = new Arm(Blk.Added, 0x028, 2, Op.Max, -1);
            a[56] = new Arm(Blk.Added, 0x02A, 1, Op.Max, -1);
            a[57] = new Arm(Blk.Base, 0x0DC, 4, Op.Add, -1);
            a[58] = new Arm(Blk.Base, 0x0E4, 4, Op.Add, -1);
            a[59] = new Arm(Blk.Base, 0x0E0, 4, Op.Add, -1);
            a[60] = new Arm(Blk.Base, 0x0E8, 4, Op.Add, -1);
            a[61] = new Arm(Blk.Base, 0x0EC, 4, Op.Add, -1);
            a[62] = new Arm(Blk.Base, 0x0F0, 2, Op.Add, -1);
            a[63] = new Arm(Blk.Added, 0x018, 1, Op.SetOne, -1);
            a[64] = new Arm(Blk.Base, 0x058, 4, Op.Add, -1);
            a[65] = new Arm(Blk.Base, 0x0F4, 4, Op.Add, -1);
            a[66] = new Arm(Blk.Base, 0x0AC, 4, Op.Add, -1);
            a[67] = new Arm(Blk.Base, 0x0F2, 2, Op.Add, -1);
            a[68] = new Arm(Blk.Added, 0x02C, 1, Op.SetOne, -1);
            a[69] = new Arm(Blk.Added, 0x02D, 1, Op.Max, -1);
            a[70] = new Arm(Blk.Added, 0x02E, 1, Op.SetOne, -1);
            a[71] = new Arm(Blk.Base, 0x0D4, 4, Op.Add, -1);
            a[72] = new Arm(Blk.Added, 0x00A, 1, Op.SetOne, -1);
            a[73] = new Arm(Blk.Added, 0x023, 1, Op.SetOne, -1);
            a[74] = new Arm(Blk.Added, 0x02F, 1, Op.SetOne, -1);
            a[75] = new Arm(Blk.Base, 0x0FE, 2, Op.Add, -1);
            a[76] = new Arm(Blk.Base, 0x100, 2, Op.Add, -1);
            a[77] = new Arm(Blk.Base, 0x102, 2, Op.Add, -1);
            a[78] = new Arm(Blk.Base, 0x106, 2, Op.Add, -1);
            a[79] = new Arm(Blk.Base, 0x108, 4, Op.Add, -1);
            a[80] = new Arm(Blk.Base, 0x10C, 2, Op.Add, -1);
            a[81] = new Arm(Blk.Base, 0x10E, 2, Op.Add, -1);
            a[82] = new Arm(Blk.Base, 0x110, 2, Op.Add, -1);
            a[83] = new Arm(Blk.Base, 0x112, 2, Op.Add, -1);
            a[84] = new Arm(Blk.Base, 0x114, 2, Op.Add, -1);
            a[85] = new Arm(Blk.Base, 0x116, 2, Op.Add, -1);
            a[86] = new Arm(Blk.Base, 0x118, 2, Op.Max, -1);
            a[87] = new Arm(Blk.Base, 0x11A, 2, Op.Add, -1);
            a[88] = new Arm(Blk.Added, 0x007, 1, Op.SetOne, -1);
            a[89] = new Arm(Blk.Added, 0x007, 1, Op.SetOne, -1);
            a[90] = new Arm(Blk.Base, 0x11E, 1, Op.Add, -1);
            a[91] = new Arm(Blk.Base, 0x11F, 1, Op.Add, -1);
            a[92] = new Arm(Blk.Base, 0x120, 1, Op.Add, -1);
            a[93] = new Arm(Blk.Added, 0x030, 2, Op.Max, -1);
            a[94] = new Arm(Blk.Added, 0x032, 2, Op.Max, -1);
            a[95] = new Arm(Blk.Base, 0x104, 2, Op.Add, -1);
            a[96] = new Arm(Blk.Base, 0x0FC, 2, Op.Add, -1);
            a[98] = new Arm(Blk.Base, 0x124, 4, Op.Add, -1);
            a[99] = new Arm(Blk.Base, 0x128, 4, Op.Add, -1);
            a[100] = new Arm(Blk.Base, 0x12C, 4, Op.Add, -1);
            a[101] = new Arm(Blk.Base, 0x130, 4, Op.Add, -1);
            a[102] = new Arm(Blk.Base, 0x134, 2, Op.Add, -1);
            a[103] = new Arm(Blk.Base, 0x136, 2, Op.Add, -1);
            a[104] = new Arm(Blk.Base, 0x138, 2, Op.Add, -1);
            a[105] = new Arm(Blk.Base, 0x13C, 4, Op.Add, -1);
            a[106] = new Arm(Blk.Base, 0x140, 4, Op.Add, -1);
            a[107] = new Arm(Blk.Base, 0x144, 4, Op.Add, -1);
            a[108] = new Arm(Blk.Base, 0x148, 4, Op.Add, -1);
            a[109] = new Arm(Blk.Base, 0x14C, 4, Op.Add, -1);
            a[110] = new Arm(Blk.Base, 0x150, 4, Op.Add, -1);
            a[111] = new Arm(Blk.Base, 0x034, 4, Op.Add, -1);
            a[112] = new Arm(Blk.Base, 0x038, 4, Op.Add, -1);
            a[113] = new Arm(Blk.Base, 0x0A0, 4, Op.Add, -1);
            a[114] = new Arm(Blk.Base, 0x0A4, 4, Op.Add, -1);
            a[115] = new Arm(Blk.Base, 0x154, 4, Op.Add, -1);
            a[116] = new Arm(Blk.Base, 0x15C, 1, Op.Add, 7);
            a[117] = new Arm(Blk.Added, 0x020, 1, Op.Max, -1);
            a[118] = new Arm(Blk.Base, 0x158, 4, Op.Add, -1);
            a[119] = new Arm(Blk.Added, 0x034, 1, Op.Max, 1);
            a[120] = new Arm(Blk.Base, 0x15E, 2, Op.Add, -1);
            a[121] = new Arm(Blk.Base, 0x160, 1, Op.Add, -1);
            a[122] = new Arm(Blk.Base, 0x164, 4, Op.Add, -1);
            a[123] = new Arm(Blk.Base, 0x168, 4, Op.Add, -1);
            a[124] = new Arm(Blk.Base, 0x16C, 2, Op.Add, -1);
            a[125] = new Arm(Blk.Base, 0x16E, 2, Op.Add, -1);
            a[126] = new Arm(Blk.Base, 0x170, 4, Op.Add, -1);
            a[127] = new Arm(Blk.Base, 0x174, 1, Op.Add, -1);
            a[128] = new Arm(Blk.Base, 0x176, 2, Op.Add, -1);
            a[129] = new Arm(Blk.Base, 0x178, 2, Op.Add, -1);
            a[130] = new Arm(Blk.Base, 0x17C, 4, Op.Add, -1);
            a[131] = new Arm(Blk.Base, 0x180, 1, Op.Add, -1);
            a[132] = new Arm(Blk.Base, 0x182, 2, Op.Add, -1);
            a[133] = new Arm(Blk.Base, 0x184, 4, Op.Add, -1);
            a[134] = new Arm(Blk.Base, 0x188, 2, Op.Add, -1);
            a[135] = new Arm(Blk.Base, 0x18C, 4, Op.Add, -1);
            a[136] = new Arm(Blk.Base, 0x190, 2, Op.Add, -1);
            a[137] = new Arm(Blk.Base, 0x080, 4, Op.Add, -1);
            a[138] = new Arm(Blk.Base, 0x0C4, 4, Op.Add, -1);
            a[139] = new Arm(Blk.Base, 0x0C8, 4, Op.Add, -1);
            a[140] = new Arm(Blk.Base, 0x0B4, 2, Op.Add, -1);
            a[141] = new Arm(Blk.Base, 0x192, 1, Op.Add, -1);
            a[142] = new Arm(Blk.Base, 0x0A8, 4, Op.Add, -1);
            a[143] = new Arm(Blk.Base, 0x194, 4, Op.Add, -1);
            a[144] = new Arm(Blk.Base, 0x198, 2, Op.Add, -1);
            a[158] = new Arm(Blk.Base, 0x1AE, 2, Op.Add, -1);
            return a;
        }

        private static long Read(ReadOnlySpan<byte> block, int offset, int width)
        {
            switch (width)
            {
                case 1: return block[offset];
                case 2: return (ushort)(block[offset] | (block[offset + 1] << 8));
                default:
                    return (uint)(block[offset] | (block[offset + 1] << 8)
                                  | (block[offset + 2] << 16) | (block[offset + 3] << 24));
            }
        }

        private static void Write(Span<byte> block, int offset, int width, long value)
        {
            var v = unchecked((ulong)value);
            block[offset] = (byte)v;
            if (width >= 2) block[offset + 1] = (byte)(v >> 8);
            if (width >= 4)
            {
                block[offset + 2] = (byte)(v >> 16);
                block[offset + 3] = (byte)(v >> 24);
            }
        }

        /// <summary>0x4C7004 — `cmp edx,eax / jl / mov eax,edx`, i.e. signed Max.</summary>
        private static int NativeMax(int a, int b) => b >= a ? b : a;

        /// <summary>
        /// Apply one bonus. <paramref name="baseBlock"/> starts at container+0x48 and
        /// <paramref name="addedBlock"/> at container+0x1F8; both are the native byte
        /// images. Codes with no arm are a no-op, exactly as 0x78F329 is.
        /// </summary>
        public static void Apply(Span<byte> baseBlock, Span<byte> addedBlock,
            int code, int value)
        {
            if ((uint)code > MaxCode)
            {
                return; // 0x78E844 `ja 0x78F329`
            }

            if (code == NestedCode)
            {
                var sel = value & NestedMask;
                if (sel > NestedBound)
                {
                    return; // 0x78F045 `ja 0x78F329`
                }
                addedBlock[_nestedFlagOffsets[sel]] = 1;
                return;
            }

            var arm = _arms[code];
            if (!arm.Live)
            {
                return;
            }

            var block = arm.Block == Blk.Base ? baseBlock : addedBlock;
            long result;
            switch (arm.Kind)
            {
                case Op.SetOne:
                    result = 1;
                    break;
                case Op.Max:
                    result = NativeMax((int)Read(block, arm.Offset, arm.Width), value);
                    break;
                default:
                    result = Read(block, arm.Offset, arm.Width) + value;
                    break;
            }

            Write(block, arm.Offset, arm.Width, result);

            if (arm.Clamp >= 0
                && Read(block, arm.Offset, arm.Width) > (uint)arm.Clamp)
            {
                // 0x78F2xx `cmp .. ,K / jbe / mov .. ,K` — unsigned ceiling applied
                // to the stored field, after the write.
                Write(block, arm.Offset, arm.Width, arm.Clamp);
            }
        }
    }
}
