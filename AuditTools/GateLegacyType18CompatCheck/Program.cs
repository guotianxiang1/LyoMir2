using System.Buffers.Binary;
using System.Text;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936,
    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

var tests = new (string Name, Action Run)[]
{
    ("native type18 golden frame", NativeType18GoldenFrame),
    ("zero-text type18 frame", ZeroTextType18Frame),
    ("byte-wise split type18", ByteWiseSplitType18),
    ("mixed sticky stream", MixedStickyStream),
    ("short type18 is consumed as native", ShortType18IsConsumed),
    ("magic inside payload remains framed", MagicInsidePayload),
    ("raw trailing bytes round-trip", RawTrailingBytesRoundTrip),
    ("two-level type18 length boundary", TwoLevelLengthBoundary),
    ("native type17 zero payload is parsed", Type17ZeroPayloadParsed),
    ("native type17 split sticky alignment", Type17SplitStickyAlignment),
    ("native type17 outer boundary", Type17OuterBoundary),
    ("split magic and false suffix", SplitMagicAndFalseSuffix),
    ("bounded input and reset", BoundedInputAndReset)
};

foreach (var test in tests) test.Run();
Console.WriteLine(
    $"GateLegacyType18CompatCheck PASS tests={tests.Length} legacy=16+12+GBK/NUL " +
    "internal=16B+ACK16 split=bytewise sticky=mixed");

void NativeType18GoldenFrame()
{
    var text = gbk.GetBytes("系统奖励测试");
    var encoded = BuildLegacy(0x11223344, 0x55667788,
        unchecked((int)0x89ABCDEF), 100, 0x38FF, 0, 0, text);
    Equal(16 + 12 + text.Length + 1, encoded.Length, "golden total length");
    Equal(LegacyGateType18.MagicValue,
        BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0, 4)), "magic +0");
    Equal(0x11223344u,
        BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4, 4)), "ignored connection +4");
    Equal(0x55667788u,
        BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(8, 4)), "filter +8");
    Equal((ushort)18,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(12, 2)), "type +12");
    Equal((ushort)(12 + text.Length + 1),
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(14, 2)), "payload length +14");
    Equal(unchecked((int)0x89ABCDEF),
        BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(16, 4)), "recog +16");
    Equal((ushort)100,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(20, 2)), "ident +20");
    Equal((ushort)0x38FF,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(22, 2)), "param +22");
    Equal((ushort)0,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(24, 2)), "tag +24");
    Equal((ushort)0,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(26, 2)), "series +26");
    Equal((byte)0, encoded[^1], "NUL terminator");

    var parsed = LegacyGateType18.FromBytes(encoded, 0, encoded.Length);
    NotNull(parsed, "golden decode");
    Equal(0x11223344u, parsed.IgnoredConnectionId, "decoded ignored connection");
    Equal(0x55667788u, parsed.FilterUserIndex, "decoded filter");
    Equal(unchecked((int)0x89ABCDEF), parsed.Recog, "decoded recog");
    Equal("系统奖励测试", gbk.GetString(parsed.TextBytes), "decoded GBK text");
    var clientPayload = parsed.ToClientPayload();
    BytesEqual(encoded.AsSpan(LegacyGateType18.HeaderSize), clientPayload,
        "decoded client payload round-trip");
}

void ZeroTextType18Frame()
{
    var encoded = BuildLegacy(0, 0, 0, 100, 0x38FF, 0, 0, null);
    Equal(28, encoded.Length, "zero-text total length");
    Equal((ushort)12,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(14, 2)),
        "zero-text payload length");
    var frames = ParseOnce(encoded);
    Equal(1, frames.Count, "zero-text frame count");
    NotNull(frames[0].LegacyType18, "zero-text legacy classification");
    Equal(0, frames[0].LegacyType18.TextBytes.Length, "zero-text body");
    Equal(LegacyGateType18.ClientPacketSize,
        frames[0].LegacyType18.ToClientPayload().Length,
        "zero-text client payload length");
}

void ByteWiseSplitType18()
{
    var encoded = BuildLegacy(0, 17, 0, 100, 0x38FF, 0, 0,
        gbk.GetBytes("逐字节"));
    var parser = new GameGateServerFrameParser();
    var frames = new List<GameGateServerFrame>();
    for (var i = 0; i < encoded.Length; i++)
    {
        True(parser.TryAppend(encoded, i, 1, out var batch, out var error), error);
        frames.AddRange(batch);
        Equal(i == encoded.Length - 1 ? 1 : 0, frames.Count,
            $"byte-wise count at {i}");
    }
    Equal(0, parser.BufferedLength, "byte-wise final buffer");
}

void MixedStickyStream()
{
    var ack = InternalPacket77.Ack(9, 10, 0x0C).ToBytes();
    var internalFrame = BuildInternal(11, 12, 0x1234,
        new byte[] { 1, 2, 3, 4 });
    var legacy = BuildLegacy(99, 7, 0, 100, 0x38FF, 0, 0,
        gbk.GetBytes("粘包"));
    var frames = ParseOnce(Join(new byte[] { 0x42, 0x43 }, ack, legacy, internalFrame));
    Equal(3, frames.Count, "mixed sticky count");
    NotNull(frames[0].Internal77, "ACK classification");
    Equal(InternalPacket77.ACK_FRAME_LEN, frames[0].Internal77.FrameLen, "ACK length");
    NotNull(frames[1].LegacyType18, "legacy classification");
    NotNull(frames[2].Internal77, "internal classification");
    Equal((ushort)0x1234, frames[2].Internal77.Cmd, "internal command");
}

void ShortType18IsConsumed()
{
    var encoded = BuildInternal(0x10203040, 22, 18,
        new byte[] { 0x77, 0xBB, 0xAA, 0x33, 0, 1, 2, 3 });
    Equal((ushort)18,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(12, 2)),
        "type18 discriminator");
    Equal((ushort)8,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(14, 2)),
        "short type18 body length");

    var tail = BuildInternal(23, 24, 25, Array.Empty<byte>());
    var frames = ParseOnce(Join(encoded, tail));
    Equal(2, frames.Count, "short type18 sticky count");
    NotNull(frames[0].LegacyType18, "short type18 classification");
    Equal(8, frames[0].LegacyType18.ToClientPayload().Length,
        "short type18 payload length");
    BytesEqual(encoded, frames[0].LegacyType18.ToBytes(),
        "short type18 round-trip");
    NotNull(frames[1].Internal77, "short type18 sticky tail");
    Equal(23u, frames[1].Internal77.ConnID, "short type18 tail connection");
}

void MagicInsidePayload()
{
    var payload = new byte[40];
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4),
        InternalPacket77.MAGIC);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16, 2), 18);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(18, 2), 12);
    var first = BuildInternal(31, 32, 33, payload);
    var tail = BuildLegacy(0, 0, 0, 100, 0x38FF, 0, 0, null);
    var frames = ParseOnce(Join(first, tail));
    Equal(2, frames.Count, "payload magic count");
    BytesEqual(payload, frames[0].Internal77.Payload, "payload magic preserved");
    NotNull(frames[1].LegacyType18, "payload magic tail legacy");
}

void RawTrailingBytesRoundTrip()
{
    var noTerminator = BuildLegacy(0, 0, 0, 100, 0x38FF, 0, 0,
        gbk.GetBytes("无结尾"));
    noTerminator[^1] = 0x41;
    var frames = ParseOnce(noTerminator);
    Equal(1, frames.Count, "non-NUL frame count");
    BytesEqual(noTerminator.AsSpan(LegacyGateType18.HeaderSize),
        frames[0].LegacyType18.ToClientPayload(), "non-NUL payload preserved");

    var nulOnly = BuildLegacy(0, 0, 0, 100, 0x38FF, 0, 0, null);
    Array.Resize(ref nulOnly, nulOnly.Length + 1);
    BinaryPrimitives.WriteUInt16LittleEndian(nulOnly.AsSpan(14, 2),
        LegacyGateType18.ClientPacketSize + 1);
    frames = ParseOnce(nulOnly);
    Equal(1, frames.Count, "NUL-only frame count");
    BytesEqual(nulOnly.AsSpan(LegacyGateType18.HeaderSize),
        frames[0].LegacyType18.ToClientPayload(), "NUL-only payload preserved");
}

void TwoLevelLengthBoundary()
{
    var acceptedTotals = new[] { 0x7FFF, 0x8000, 0x8003, 0x8004, 0xFFFF };
    foreach (var total in acceptedTotals)
    {
        var accepted = BuildType18Total(total);
        var frames = ParseOnce(accepted, maximumBufferedLength: 0x10000,
            maximumInternalFrameLength: ushort.MaxValue);
        Equal(1, frames.Count, $"accepted frame count 0x{total:X}");
        NotNull(frames[0].LegacyType18,
            $"accepted type18 classification 0x{total:X}");
        Equal(total - LegacyGateType18.HeaderSize,
            frames[0].LegacyType18.ToClientPayload().Length,
            $"accepted body length 0x{total:X}");

        var relayLength = frames[0].LegacyType18.ToClientPayload().Length
                          + LegacyGateType18.ClientRelayHeaderSize;
        Equal(total <= 0x8003, relayLength
            < LegacyGateType18.MaximumClientRelayLengthExclusive,
            $"relay eligibility 0x{total:X}");
    }

    var rejectedHeader = new byte[16];
    BinaryPrimitives.WriteUInt32LittleEndian(rejectedHeader.AsSpan(0, 4),
        InternalPacket77.MAGIC);
    BinaryPrimitives.WriteUInt16LittleEndian(rejectedHeader.AsSpan(12, 2), 18);
    BinaryPrimitives.WriteUInt16LittleEndian(rejectedHeader.AsSpan(14, 2),
        checked((ushort)(LegacyGateType18.MaximumFrameLengthExclusive
                         - LegacyGateType18.HeaderSize)));
    var tail = BuildInternal(51, 52, 53, Array.Empty<byte>());
    var rejectedFrames = ParseOnce(Join(rejectedHeader, tail));
    Equal(0, rejectedFrames.Count, "0x10000 drops current receive buffer");
}

void Type17ZeroPayloadParsed()
{
    var encoded = BuildType17(0x11223344, 0x55667788, Array.Empty<byte>());
    Equal(LegacyGateType17.HeaderSize, encoded.Length, "type17 zero total length");
    var frames = ParseOnce(encoded);
    Equal(1, frames.Count, "type17 zero emitted frame count");
    NotNull(frames[0].LegacyType17, "type17 zero classification");
    Equal(0x11223344u, frames[0].LegacyType17.ConnectionId,
        "type17 zero field4");
    Equal(0x55667788u, frames[0].LegacyType17.TargetGate,
        "type17 zero target");
}

void Type17SplitStickyAlignment()
{
    var payload = new byte[]
    {
        0x41, 0x77, 0xBB, 0xAA, 0x33, 0x42, 0x43, 0x44, 0x45
    };
    var encoded = BuildType17(0xFFFFFFFF, 0x80000000, payload);
    var tail = BuildInternal(61, 62, 63, new byte[] { 64, 65 });
    var sticky = Join(encoded, tail);
    var parser = new GameGateServerFrameParser();
    var frames = new List<GameGateServerFrame>();
    for (var i = 0; i < sticky.Length; i++)
    {
        True(parser.TryAppend(sticky, i, 1, out var batch, out var error), error);
        frames.AddRange(batch);
    }
    Equal(2, frames.Count, "type17 sticky emitted frame count");
    NotNull(frames[0].LegacyType17, "type17 sticky classification");
    Equal(0x80000000u, frames[0].LegacyType17.TargetGate,
        "type17 sticky target");
    NotNull(frames[1].Internal77, "type17 sticky tail classification");
    Equal(61u, frames[1].Internal77.ConnID, "type17 sticky tail connection");
    Equal((ushort)63, frames[1].Internal77.Cmd, "type17 sticky tail command");
    Equal(0, parser.BufferedLength, "type17 sticky final buffer");
}

void Type17OuterBoundary()
{
    var acceptedPayload = new byte[
        LegacyGateType17.MaximumFrameLengthExclusive
        - LegacyGateType17.HeaderSize - 1];
    Array.Fill(acceptedPayload, (byte)0x41);
    var accepted = BuildType17(1, 2, acceptedPayload);
    Equal(0xFFFF, accepted.Length, "type17 largest accepted total length");
    var frames = ParseOnce(accepted, maximumBufferedLength: 0x10000,
        maximumInternalFrameLength: ushort.MaxValue);
    Equal(1, frames.Count, "type17 largest accepted emitted frame count");
    NotNull(frames[0].LegacyType17, "type17 largest accepted classification");

    var rejectedHeader = BuildType17(3, 4, Array.Empty<byte>());
    BinaryPrimitives.WriteUInt16LittleEndian(rejectedHeader.AsSpan(14, 2),
        checked((ushort)(LegacyGateType17.MaximumFrameLengthExclusive
                         - LegacyGateType17.HeaderSize)));
    var tail = BuildInternal(71, 72, 73, Array.Empty<byte>());
    frames = ParseOnce(Join(rejectedHeader, tail));
    Equal(0, frames.Count, "type17 0x10000 drops current receive buffer");
}

void SplitMagicAndFalseSuffix()
{
    var encoded = BuildLegacy(0, 0, 0, 100, 0x38FF, 0, 0, null);
    for (var split = 1; split <= 3; split++)
    {
        var parser = new GameGateServerFrameParser();
        True(parser.TryAppend(encoded, 0, split, out var before, out var error), error);
        Equal(0, before.Count, $"split magic initial count {split}");
        Equal(split, parser.BufferedLength, $"split magic buffer {split}");
        True(parser.TryAppend(encoded, split, encoded.Length - split,
            out var after, out error), error);
        Equal(1, after.Count, $"split magic completion {split}");
    }

    var falseSuffix = new GameGateServerFrameParser();
    True(falseSuffix.TryAppend(new byte[] { 0x77, 0x00, 0xBB, 0xAA }, 0, 4,
        out var frames, out var falseError), falseError);
    Equal(0, frames.Count, "false suffix frame count");
    Equal(0, falseSuffix.BufferedLength, "false suffix discarded");
}

void BoundedInputAndReset()
{
    var parser = new GameGateServerFrameParser(maximumBufferedLength: 32);
    var partial = BuildLegacy(0, 0, 0, 100, 0x38FF, 0, 0, null);
    True(parser.TryAppend(partial, 0, 20, out _, out var error), error);
    Equal(20, parser.BufferedLength, "partial buffer before reset");
    parser.Reset();
    Equal(0, parser.BufferedLength, "reset buffer");
    False(parser.TryAppend(new byte[33], 0, 33, out var frames, out error),
        "oversized append accepted");
    Equal(0, frames.Count, "oversized append frames");
    Equal(0, parser.BufferedLength, "oversized append resets");
    True(error.Length > 0, "oversized append error missing");
    False(parser.TryAppend(new byte[1], -1, 1, out _, out error),
        "invalid range accepted");
    Equal(0, parser.BufferedLength, "invalid range resets");
}

static byte[] BuildLegacy(uint ignoredConnection, uint filterUserIndex, int recog,
    ushort ident, ushort param, ushort tag, ushort series, byte[] text)
{
    var textLength = text == null ? 0 : text.Length + 1;
    var payloadLength = LegacyGateType18.ClientPacketSize + textLength;
    var result = new byte[LegacyGateType18.HeaderSize + payloadLength];
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4),
        LegacyGateType18.MagicValue);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), ignoredConnection);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), filterUserIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12, 2),
        LegacyGateType18.MessageType);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14, 2),
        checked((ushort)payloadLength));
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16, 4), recog);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20, 2), ident);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(22, 2), param);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(24, 2), tag);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(26, 2), series);
    if (text != null) text.CopyTo(result, 28);
    return result;
}

static byte[] BuildType17(uint field4, uint field8, byte[] payload)
{
    var result = new byte[LegacyGateType17.HeaderSize + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4),
        InternalPacket77.MAGIC);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), field4);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), field8);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12, 2),
        LegacyGateType17.MessageType);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14, 2),
        checked((ushort)payload.Length));
    payload.CopyTo(result, LegacyGateType17.HeaderSize);
    return result;
}

static byte[] BuildType18Total(int totalLength)
{
    if (totalLength < LegacyGateType18.HeaderSize
        || totalLength >= LegacyGateType18.MaximumFrameLengthExclusive)
        throw new ArgumentOutOfRangeException(nameof(totalLength));

    var result = new byte[totalLength];
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4),
        LegacyGateType18.MagicValue);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12, 2),
        LegacyGateType18.MessageType);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14, 2),
        checked((ushort)(totalLength - LegacyGateType18.HeaderSize)));
    if (totalLength > LegacyGateType18.HeaderSize)
        result.AsSpan(LegacyGateType18.HeaderSize).Fill(0x41);
    return result;
}

static byte[] BuildInternal(uint connId, uint seqId, ushort command, byte[] payload)
{
    return new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = connId,
        SeqID = seqId,
        FrameLen = checked((ushort)(InternalPacket77.HEADER_SIZE + payload.Length)),
        Cmd = command,
        Field16 = 0x10203040,
        Field20 = checked((uint)payload.Length),
        Payload = payload
    }.ToBytes();
}

static List<GameGateServerFrame> ParseOnce(byte[] data,
    int maximumBufferedLength = InternalPacket77FrameParser.DefaultMaximumBufferedLength,
    int maximumInternalFrameLength = GameGateServerFrameParser.NativeMaximumFrameLength)
{
    var parser = new GameGateServerFrameParser(maximumBufferedLength,
        maximumInternalFrameLength);
    True(parser.TryAppend(data, 0, data.Length, out var frames, out var error), error);
    Equal(0, parser.BufferedLength, "one-shot final buffer");
    return frames;
}

static byte[] Join(params byte[][] arrays)
{
    var result = new byte[arrays.Sum(x => x.Length)];
    var offset = 0;
    foreach (var array in arrays)
    {
        array.CopyTo(result, offset);
        offset += array.Length;
    }
    return result;
}

static void BytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual,
    string name)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"{name}: byte sequence differs");
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void False(bool condition, string message)
{
    if (condition) throw new InvalidOperationException(message);
}

static void NotNull(object value, string name)
{
    if (value == null) throw new InvalidOperationException($"{name}: expected value");
}

static void Equal<T>(T expected, T actual, string name) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{name}: expected {expected}, got {actual}");
}
