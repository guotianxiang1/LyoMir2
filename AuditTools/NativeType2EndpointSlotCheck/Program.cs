using System.Buffers.Binary;
using GameSvr.Services;

var state = new NativeType2EndpointSlotState();
var zeroSlot = new byte[NativeType2EndpointSlotState.SlotSize];

Check(state.Consume(new byte[NativeType2EndpointSlotState.HeaderSize - 1])
      == NativeType2EndpointSlotResult.Ignored,
    "short endpoint header");
Check(state.Consume(Packet(1, 0, 0x10, command: 0x006F))
      == NativeType2EndpointSlotResult.Ignored,
    "other endpoint command");
Check(state.Consume(Packet(0, 0, 0x10))
      == NativeType2EndpointSlotResult.Ignored,
    "zero endpoint count");
Check(state.Consume(Packet(-1, 0, 0x10))
      == NativeType2EndpointSlotResult.Ignored,
    "negative endpoint count");
Check(state.Consume(Packet(NativeType2EndpointSlotState.SlotCount + 1,
        0, 0x10)) == NativeType2EndpointSlotResult.Ignored,
    "oversized endpoint count");
Check(Equal(state.CopySlot(0), zeroSlot) && Equal(state.CopySlot(1), zeroSlot),
    "invalid endpoint packets changed slots");

var first = Packet(1, unchecked((int)0x76543210), 0x21);
Check(state.Consume(first) == NativeType2EndpointSlotResult.PrefixOverwritten,
    "single endpoint prefix overwrite");
var firstExpected = CopyRecord(first, 1);
Check(Equal(state.CopySlot(1), firstExpected),
    "Param1-ignored endpoint first slot");
var param1Variant = (byte[])first.Clone();
BinaryPrimitives.WriteInt32LittleEndian(param1Variant.AsSpan(4, 4), -99);
Check(state.Consume(param1Variant)
      == NativeType2EndpointSlotResult.PrefixOverwritten
      && Equal(state.CopySlot(1), firstExpected),
    "endpoint Param1 does not affect copied bytes");
first[NativeType2EndpointSlotState.HeaderSize] = 0;
Check(Equal(state.CopySlot(1), firstExpected), "endpoint record deep copy");

var three = Packet(3, 99, 0x40);
Check(state.Consume(three) == NativeType2EndpointSlotResult.PrefixOverwritten,
    "three endpoint prefix overwrite");
var secondExpected = CopyRecord(three, 2);
var thirdExpected = CopyRecord(three, 3);
Check(Equal(state.CopySlot(1), CopyRecord(three, 1))
      && Equal(state.CopySlot(2), secondExpected)
      && Equal(state.CopySlot(3), thirdExpected),
    "endpoint prefix slots");

var replacement = Packet(1, -7, 0x70);
Check(state.Consume(replacement)
      == NativeType2EndpointSlotResult.PrefixOverwritten,
    "endpoint prefix replacement");
Check(Equal(state.CopySlot(1), CopyRecord(replacement, 1))
      && Equal(state.CopySlot(2), secondExpected)
      && Equal(state.CopySlot(3), thirdExpected),
    "endpoint tail preserved after smaller prefix");

var shortLength = Packet(2, 0, 0x80)[..^1];
var longLength = Packet(2, 0, 0x90).Concat(new byte[] { 0 }).ToArray();
Check(state.Consume(shortLength) == NativeType2EndpointSlotResult.Ignored
      && state.Consume(longLength) == NativeType2EndpointSlotResult.Ignored,
    "endpoint exact packet length");
Check(Equal(state.CopySlot(1), CopyRecord(replacement, 1))
      && Equal(state.CopySlot(2), secondExpected)
      && Equal(state.CopySlot(3), thirdExpected),
    "invalid endpoint length changed slots");

var maximum = Packet(NativeType2EndpointSlotState.SlotCount, 0, 0xA0);
Check(state.Consume(maximum) == NativeType2EndpointSlotResult.PrefixOverwritten,
    "maximum endpoint prefix overwrite");
Check(Equal(state.CopySlot(1), CopyRecord(maximum, 1))
      && Equal(state.CopySlot(NativeType2EndpointSlotState.SlotCount),
          CopyRecord(maximum, NativeType2EndpointSlotState.SlotCount)),
    "maximum endpoint slots");
Check(Equal(state.CopySlot(0), zeroSlot), "endpoint slot zero remains untouched");

Console.WriteLine("PASS NativeType2EndpointSlotCheck command=006E " +
                  "count=1..32 length=12-plus-20n prefix-overwrite " +
                  "slot0-untouched");

static byte[] Packet(int count, int param1, byte seed, ushort command =
    NativeType2EndpointSlotState.Command)
{
    var bodyLength = count is >= 1 and <= NativeType2EndpointSlotState.SlotCount
        ? count * NativeType2EndpointSlotState.SlotSize : 0;
    var payload = new byte[NativeType2EndpointSlotState.HeaderSize + bodyLength];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), param1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), count);
    for (var index = NativeType2EndpointSlotState.HeaderSize;
         index < payload.Length; index++)
        payload[index] = unchecked((byte)(seed + index -
            NativeType2EndpointSlotState.HeaderSize));
    return payload;
}

static byte[] CopyRecord(byte[] payload, int slot)
{
    return payload.AsSpan(NativeType2EndpointSlotState.HeaderSize
        + (slot - 1) * NativeType2EndpointSlotState.SlotSize,
        NativeType2EndpointSlotState.SlotSize).ToArray();
}

static bool Equal(byte[] left, byte[] right) => left.AsSpan().SequenceEqual(right);

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}
