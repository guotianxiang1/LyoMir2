using System.Buffers.Binary;

namespace GameSvr
{
    /// <summary>
    /// Pure raw-block model of sub_78E830. It merges one native property pair
    /// into the 0x1B0-byte primary and 0x36-byte secondary ability blocks.
    /// No semantic field aliases are invented at this boundary.
    /// </summary>
    public static class NativeRawAbilityPropertyCore
    {
        public const uint OriginalFunction = 0x0078E830;
        public const int PrimarySize = 0x1B0;
        public const int SecondarySize = 0x36;

        public static void Apply(byte[] primary, byte[] secondary,
            int propertyId, int value)
        {
            var offset = AddInt32Offset(propertyId);
            if (offset >= 0)
            {
                AddInt32(primary, offset, value);
                return;
            }

            offset = AddUInt16Offset(propertyId);
            if (offset >= 0)
            {
                AddUInt16(primary, offset, value);
                return;
            }

            offset = AddByteOffset(propertyId);
            if (offset >= 0)
            {
                RequireRange(primary, offset, 1, nameof(primary));
                primary[offset] = unchecked((byte)(primary[offset] + value));
                return;
            }

            switch (propertyId)
            {
                case 55:
                    StoreMaxUInt16(secondary, 0x28, value);
                    break;
                case 56:
                    StoreMaxByte(secondary, 0x2A, value);
                    break;
                case 63:
                    SetOne(secondary, 0x18);
                    break;
                case 68:
                    SetOne(secondary, 0x2C);
                    break;
                case 69:
                    StoreMaxByte(secondary, 0x2D, value);
                    break;
                case 70:
                    SetOne(secondary, 0x2E);
                    break;
                case 72:
                    SetOne(secondary, 0x0A);
                    break;
                case 73:
                    SetOne(secondary, 0x23);
                    break;
                case 74:
                    SetOne(secondary, 0x2F);
                    break;
                case 86:
                    StoreMaxUInt16(primary, 0x118, value);
                    break;
                case 88:
                case 89:
                    SetOne(secondary, 0x07);
                    break;
                case 93:
                    StoreMaxUInt16(secondary, 0x30, value);
                    break;
                case 94:
                    StoreMaxUInt16(secondary, 0x32, value);
                    break;
                case 116:
                    RequireRange(primary, 0x15C, 1, nameof(primary));
                    primary[0x15C] = unchecked(
                        (byte)(primary[0x15C] + value));
                    if (primary[0x15C] > 7) primary[0x15C] = 7;
                    break;
                case 117:
                    StoreMaxByte(secondary, 0x20, value);
                    break;
                case 119:
                    StoreMaxByte(secondary, 0x34, value);
                    if (secondary[0x34] > 1) secondary[0x34] = 1;
                    break;
                case 254:
                    ApplyProperty254(secondary, value);
                    break;
            }
        }

        private static int AddInt32Offset(int propertyId) =>
            propertyId switch
            {
                1 => 0x1C,
                2 => 0x20,
                3 => 0x24,
                4 => 0x28,
                5 => 0x2C,
                6 => 0x30,
                7 => 0x0C,
                8 => 0x10,
                9 => 0x14,
                10 => 0x18,
                11 => 0x00,
                12 => 0x04,
                29 => 0x6C,
                34 => 0xD8,
                37 => 0x7C,
                40 => 0x88,
                41 => 0x8C,
                42 => 0x90,
                43 => 0x94,
                44 => 0x98,
                45 => 0x9C,
                48 => 0xBC,
                49 => 0xC0,
                57 => 0xDC,
                58 => 0xE4,
                59 => 0xE0,
                60 => 0xE8,
                61 => 0xEC,
                64 => 0x58,
                65 => 0xF4,
                66 => 0xAC,
                71 => 0xD4,
                79 => 0x108,
                98 => 0x124,
                99 => 0x128,
                100 => 0x12C,
                101 => 0x130,
                105 => 0x13C,
                106 => 0x140,
                107 => 0x144,
                108 => 0x148,
                109 => 0x14C,
                110 => 0x150,
                111 => 0x34,
                112 => 0x38,
                113 => 0xA0,
                114 => 0xA4,
                115 => 0x154,
                118 => 0x158,
                122 => 0x164,
                123 => 0x168,
                126 => 0x170,
                130 => 0x17C,
                133 => 0x184,
                135 => 0x18C,
                137 => 0x80,
                138 => 0xC4,
                139 => 0xC8,
                142 => 0xA8,
                143 => 0x194,
                _ => -1
            };

        private static int AddUInt16Offset(int propertyId) =>
            propertyId switch
            {
                13 => 0x08,
                14 => 0x0A,
                15 => 0x44,
                18 => 0x4C,
                19 => 0x5C,
                20 => 0x5E,
                21 => 0x60,
                22 => 0x64,
                23 => 0x66,
                24 => 0x68,
                25 => 0x6A,
                26 => 0x70,
                27 => 0x72,
                28 => 0x74,
                30 => 0x3C,
                31 => 0x4A,
                32 => 0xFA,
                33 => 0xF8,
                35 => 0x76,
                36 => 0x78,
                39 => 0x86,
                46 => 0xB0,
                47 => 0xB2,
                50 => 0xCC,
                52 => 0xCE,
                53 => 0xD0,
                54 => 0xB6,
                62 => 0xF0,
                67 => 0xF2,
                75 => 0xFE,
                76 => 0x100,
                77 => 0x102,
                78 => 0x106,
                80 => 0x10C,
                81 => 0x10E,
                82 => 0x110,
                83 => 0x112,
                84 => 0x114,
                85 => 0x116,
                87 => 0x11A,
                95 => 0x104,
                96 => 0xFC,
                102 => 0x134,
                103 => 0x136,
                104 => 0x138,
                120 => 0x15E,
                124 => 0x16C,
                125 => 0x16E,
                128 => 0x176,
                129 => 0x178,
                132 => 0x182,
                134 => 0x188,
                136 => 0x190,
                140 => 0xB4,
                144 => 0x198,
                158 => 0x1AE,
                _ => -1
            };

        private static int AddByteOffset(int propertyId) =>
            propertyId switch
            {
                16 => 0x46,
                17 => 0x47,
                90 => 0x11E,
                91 => 0x11F,
                92 => 0x120,
                121 => 0x160,
                127 => 0x174,
                131 => 0x180,
                141 => 0x192,
                _ => -1
            };

        private static void AddInt32(byte[] block, int offset, int value)
        {
            RequireRange(block, offset, sizeof(uint), nameof(block));
            var current = BinaryPrimitives.ReadUInt32LittleEndian(
                block.AsSpan(offset, sizeof(uint)));
            BinaryPrimitives.WriteUInt32LittleEndian(
                block.AsSpan(offset, sizeof(uint)),
                unchecked(current + (uint)value));
        }

        private static void AddUInt16(byte[] block, int offset, int value)
        {
            RequireRange(block, offset, sizeof(ushort), nameof(block));
            var current = BinaryPrimitives.ReadUInt16LittleEndian(
                block.AsSpan(offset, sizeof(ushort)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                block.AsSpan(offset, sizeof(ushort)),
                unchecked((ushort)(current + value)));
        }

        private static void StoreMaxUInt16(byte[] block, int offset,
            int value)
        {
            RequireRange(block, offset, sizeof(ushort), nameof(block));
            var current = BinaryPrimitives.ReadUInt16LittleEndian(
                block.AsSpan(offset, sizeof(ushort)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                block.AsSpan(offset, sizeof(ushort)),
                unchecked((ushort)Math.Max(current, value)));
        }

        private static void StoreMaxByte(byte[] block, int offset, int value)
        {
            RequireRange(block, offset, 1, nameof(block));
            block[offset] = unchecked((byte)Math.Max(block[offset], value));
        }

        private static void ApplyProperty254(byte[] secondary, int value)
        {
            var offset = (value & 0x7F) switch
            {
                0 => 0x0B,
                1 => 0x05,
                2 => 0x21,
                3 => 0x13,
                4 => 0x0C,
                5 => 0x04,
                6 => 0x06,
                _ => -1
            };
            if (offset >= 0) SetOne(secondary, offset);
        }

        private static void SetOne(byte[] block, int offset)
        {
            RequireRange(block, offset, 1, nameof(block));
            block[offset] = 1;
        }

        private static void RequireRange(byte[] block, int offset, int length,
            string parameterName)
        {
            ArgumentNullException.ThrowIfNull(block, parameterName);
            if (block.Length < offset + length)
            {
                throw new ArgumentException(
                    "Native ability block does not cover the selected " +
                    $"range +0x{offset:X}..+0x{offset + length - 1:X}.",
                    parameterName);
            }
        }
    }
}
