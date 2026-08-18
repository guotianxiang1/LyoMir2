using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Pure raw-block model of the six closed TCharm-family VMT +0x5C bodies.
    /// Other equipment classes deliberately remain unhandled.
    /// </summary>
    public static class NativeCharmEquipmentAbilityCore
    {
        public const uint CharmFunction = 0x0076332C;
        public const uint CryCharmFunction = 0x00763424;
        public const uint HpCharmFunction = 0x00763440;
        public const uint MpCharmFunction = 0x007634C0;
        public const uint HpMpCharmFunction = 0x0076353C;
        public const uint MarkStoneFunction = 0x0076443C;
        public const uint MarkStoneScaleFunction = 0x0075F780;

        public const int PrimarySize = 0x1B0;
        public const int SecondarySize = 0x36;
        public const int CryFlagOffset = 0x19;
        public const int HpFlagOffset = 0x1A;
        public const int MpFlagOffset = 0x1B;

        private static readonly int[] MarkStoneEndpointOffsets =
        {
            0x1C, 0x20, 0x24, 0x28, 0x2C, 0x30, 0x34, 0x38
        };

        public static bool TryApply(GoodItem stdItem, byte[] secondary)
        {
            ArgumentNullException.ThrowIfNull(stdItem);
            ArgumentNullException.ThrowIfNull(secondary);
            if (secondary.Length < SecondarySize)
            {
                throw new ArgumentException(
                    $"Secondary block must contain at least {SecondarySize} bytes.",
                    nameof(secondary));
            }

            return TryApplyWithWriter(stdItem,
                (offset, value) => secondary[offset] = value);
        }

        public static bool TryApplyWithWriter(GoodItem stdItem,
            Action<int, byte> writeSecondary)
        {
            ArgumentNullException.ThrowIfNull(stdItem);
            ArgumentNullException.ThrowIfNull(writeSecondary);

            switch (NativeItemFactory.GetClassName(stdItem))
            {
                case "TCharm":
                    return true;
                case "TCryCharm":
                    writeSecondary(CryFlagOffset, 1);
                    return true;
                case "THPCharm":
                    writeSecondary(HpFlagOffset, 1);
                    return true;
                case "TMPCharm":
                    writeSecondary(MpFlagOffset, 1);
                    return true;
                case "THPMPCharm":
                    writeSecondary(HpFlagOffset, 1);
                    writeSecondary(MpFlagOffset, 1);
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryApply(TUserItem item, GoodItem stdItem,
            byte[] primary, byte[] secondary)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(stdItem);
            RequireBlock(primary, PrimarySize, nameof(primary));
            RequireBlock(secondary, SecondarySize, nameof(secondary));

            if (NativeItemFactory.GetClassName(stdItem) != "TMarkStoneCharm")
            {
                return TryApply(stdItem, secondary);
            }

            if (item.Dura == 0)
            {
                secondary[HpFlagOffset] = 0;
                secondary[MpFlagOffset] = 0;
                return true;
            }

            ushort percentage = unchecked((ushort)(
                item.NativeItemPlus102 | item.NativeItemPlus103 << 8));
            ushort[] endpoints =
            {
                stdItem.Dc, stdItem.Dc2, stdItem.Mc, stdItem.Mc2,
                stdItem.Sc, stdItem.Sc2, stdItem.Cc, stdItem.Cc2
            };
            for (var index = 0; index < endpoints.Length; index++)
            {
                uint value = endpoints[index];
                uint scaled = unchecked(value + value * percentage / 100u);
                AddUInt32(primary, MarkStoneEndpointOffsets[index], scaled);
            }

            AddUInt16(primary, 0x3C, unchecked((byte)stdItem.Source));
            AddUInt16(primary, 0x4C, stdItem.AniCount);
            AddUInt16(primary, 0x0A, stdItem.WordParam2);
            switch (stdItem.Shape)
            {
                case 6:
                    AddUInt16(primary, 0x08, stdItem.WordParam1);
                    break;
                case 7:
                    AddUInt16(primary, 0xB8, stdItem.WordParam1);
                    break;
                case 8:
                    AddUInt32(primary, 0x6C, stdItem.WordParam1);
                    break;
                case 9:
                    AddUInt16(primary, 0x0A, stdItem.WordParam1);
                    break;
            }

            secondary[HpFlagOffset] = 1;
            secondary[MpFlagOffset] = 1;
            return true;
        }

        private static void AddUInt16(byte[] block, int offset, ushort value)
        {
            ushort current = BinaryPrimitives.ReadUInt16LittleEndian(
                block.AsSpan(offset, sizeof(ushort)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                block.AsSpan(offset, sizeof(ushort)),
                unchecked((ushort)(current + value)));
        }

        private static void AddUInt32(byte[] block, int offset, uint value)
        {
            uint current = BinaryPrimitives.ReadUInt32LittleEndian(
                block.AsSpan(offset, sizeof(uint)));
            BinaryPrimitives.WriteUInt32LittleEndian(
                block.AsSpan(offset, sizeof(uint)),
                unchecked(current + value));
        }

        private static void RequireBlock(byte[] block, int minimumLength,
            string parameterName)
        {
            ArgumentNullException.ThrowIfNull(block, parameterName);
            if (block.Length < minimumLength)
            {
                throw new ArgumentException(
                    $"Native ability block must contain at least {minimumLength} bytes.",
                    parameterName);
            }
        }
    }
}
