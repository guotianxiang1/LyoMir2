using System.Buffers.Binary;

namespace GameSvr.Services
{
    public enum NativeType2EndpointSlotResult
    {
        Ignored,
        PrefixOverwritten
    }

    /// <summary>
    /// Original M2 Type2 110 (0x006E) opaque endpoint slots. The native
    /// receiver copies each 20-byte wire unit into slots 1 through Param2;
    /// slot 0 and slots after the copied prefix remain untouched.
    /// </summary>
    public sealed class NativeType2EndpointSlotState
    {
        public const ushort Command = 0x006E;
        public const int HeaderSize = 12;
        public const int SlotCount = 32;
        public const int SlotSize = 20;

        private readonly byte[][] _slots = CreateSlots();

        public NativeType2EndpointSlotResult Consume(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < HeaderSize
                || BinaryPrimitives.ReadUInt16LittleEndian(payload) != Command)
                return NativeType2EndpointSlotResult.Ignored;

            var count = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4));
            if (count < 1 || count > SlotCount
                || payload.Length != HeaderSize + count * SlotSize)
                return NativeType2EndpointSlotResult.Ignored;

            var body = payload.Slice(HeaderSize);
            for (var slot = 1; slot <= count; slot++)
                body.Slice((slot - 1) * SlotSize, SlotSize).CopyTo(_slots[slot]);

            return NativeType2EndpointSlotResult.PrefixOverwritten;
        }

        public byte[] CopySlot(int slot)
        {
            if (slot < 0 || slot > SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slot));
            return (byte[])_slots[slot].Clone();
        }

        private static byte[][] CreateSlots()
        {
            var slots = new byte[SlotCount + 1][];
            for (var slot = 0; slot <= SlotCount; slot++)
                slots[slot] = new byte[SlotSize];
            return slots;
        }
    }
}
