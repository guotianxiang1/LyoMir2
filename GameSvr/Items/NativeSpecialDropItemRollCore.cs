using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Exact managed model of <c>sub_78BCBC</c>, the per-instance roll used
    /// by the native <c>TSpecialDropItem</c> death-drop worker.
    /// </summary>
    public static class NativeSpecialDropItemRollCore
    {
        public const uint OriginalFunction = 0x0078BCBC;
        public const uint OriginalConstructor = 0x0078BCD8;
        public const byte SpecialDropStdMode = 96;
        public const int DefinitionIntParam1Offset = 0x4C;
        public const int InstanceThresholdOffset = 0x100;
        public const int RandomBound = 100;

        /// <summary>
        /// Replays <c>TSpecialDropItem.Create</c>: after the root constructor,
        /// <c>sub_78BCD8</c> copies dword <c>[std+0x4C]</c> to
        /// <c>[item+0x100]</c>. The destination is runtime-only and therefore
        /// must also be rebuilt after a 208-byte record is loaded.
        /// </summary>
        public static bool HydrateConstructorState(TUserItem item,
            GoodItem stdItem)
        {
            if (item == null || stdItem == null
                || stdItem.StdMode != SpecialDropStdMode)
            {
                return false;
            }

            var threshold = stdItem.IntParam1;
            item.NativeItemPlus100 = unchecked((byte)threshold);
            item.NativeItemPlus101 = unchecked((byte)(threshold >> 8));
            item.NativeItemPlus102 = unchecked((byte)(threshold >> 16));
            item.NativeItemPlus103 = unchecked((byte)(threshold >> 24));
            return true;
        }

        public static bool HydrateConstructorState(TUserItem item)
        {
            if (item == null || item.wIndex == 0)
            {
                return false;
            }

            return HydrateConstructorState(item,
                M2Share.UserEngine?.GetStdItem(item.wIndex));
        }

        public static bool IsSelected(TUserItem item, Func<int, int> random)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(random);

            // 0x78BCC2..0x78BCCC calls Random(100) before reading the dword.
            var draw = random(RandomBound);
            Span<byte> thresholdBytes = stackalloc byte[4]
            {
                item.NativeItemPlus100,
                item.NativeItemPlus101,
                item.NativeItemPlus102,
                item.NativeItemPlus103
            };
            var threshold = BinaryPrimitives.ReadInt32LittleEndian(
                thresholdBytes);

            // 0x78BCCC cmp eax,[ebx+0x100] / 0x78BCD2 setl al is signed.
            return draw < threshold;
        }
    }
}
