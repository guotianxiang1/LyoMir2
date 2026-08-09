using System.Text;
using GameSvr;

Equal("Envent_Sky.txt",
    NativeDynamicRoomEventDescriptorLoader.BuildFileName("Sky"),
    "native event descriptor file name");

var root = Path.Combine(Path.GetTempPath(),
    "lyomir-dynroom-event-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    File.WriteAllText(Path.Combine(root, "Event_Sky.txt"), "1 2 3,4");
    Assert(NativeDynamicRoomEventDescriptorLoader.TryLoad(root, "Sky",
            out var descriptors, out var diagnostics),
        "missing Envent_Sky.txt was reported as an error");
    Equal(0, descriptors.Count, "missing descriptor file result");
    Equal(0, diagnostics.Count, "missing descriptor file diagnostics");

    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    File.WriteAllBytes(Path.Combine(root, "Envent_Sky.txt"),
        Encoding.GetEncoding(936).GetBytes("""
            # native comment
            ; second native comment
            257 bad 10,11|10,11|0,12|13,0|bad,14|15,bad|16,17
             # 4 18,19
            -1 -2 20,21
            65536 3 22,23
            invalid invalid 24,25
            7 8 26,27 extra fields are ignored
            1 2
            """));

    Assert(NativeDynamicRoomEventDescriptorLoader.TryLoad(root, "Sky",
            out descriptors, out diagnostics),
        "valid GBK descriptor file was rejected: "
        + string.Join(" | ", diagnostics));
    Equal(6, descriptors.Count, "parsed descriptor count");

    var wrapped = descriptors[0];
    Equal((ushort)257, wrapped.RawEventType, "raw 16-bit event type");
    Equal((byte)1, wrapped.EffectiveEventType, "low-byte event type");
    Equal(1, wrapped.DurationSeconds, "invalid duration default");
    Equal(3, wrapped.SourceLine, "source line after comments");
    Equal(3, wrapped.Coordinates.Count, "valid coordinate count");
    Coordinate(10, 11, wrapped.Coordinates[0], "first coordinate");
    Coordinate(10, 11, wrapped.Coordinates[1], "duplicate coordinate");
    Coordinate(16, 17, wrapped.Coordinates[2], "last valid coordinate");

    var indentedComment = descriptors[1];
    Equal((ushort)0, indentedComment.RawEventType,
        "indented comment type default");
    Equal((byte)0, indentedComment.EffectiveEventType,
        "indented comment effective type");
    Equal(4, indentedComment.DurationSeconds,
        "indented comment duration");
    Coordinate(18, 19, indentedComment.Coordinates.Single(),
        "indented comment coordinate");

    var negative = descriptors[2];
    Equal(ushort.MaxValue, negative.RawEventType,
        "negative raw type unchecked conversion");
    Equal(byte.MaxValue, negative.EffectiveEventType,
        "negative type low byte");
    Equal(-2, negative.DurationSeconds, "negative duration preservation");

    Equal((ushort)0, descriptors[3].RawEventType,
        "16-bit event type wrap");
    Equal((byte)0, descriptors[3].EffectiveEventType,
        "wrapped event type low byte");
    Equal((ushort)0, descriptors[4].RawEventType,
        "invalid event type default");
    Equal(1, descriptors[4].DurationSeconds,
        "invalid duration second default");
    Coordinate(26, 27, descriptors[5].Coordinates.Single(),
        "third-field coordinate extraction");
    Assert(diagnostics.Any(value => value.Contains("invalid coordinate",
            StringComparison.Ordinal))
           && diagnostics.Any(value => value.Contains("fewer than 3 fields",
               StringComparison.Ordinal)),
        "audit diagnostics did not retain ignored malformed input");

    Assert(NativeDynamicRoomEventDescriptorLoader.TryParse(
            "256 1 1,1|1,1\r\n", out descriptors, out diagnostics),
        "direct parse failed");
    Equal((ushort)256, descriptors.Single().RawEventType,
        "direct parse raw type");
    Equal((byte)0, descriptors.Single().EffectiveEventType,
        "direct parse low byte");
    Equal(2, descriptors.Single().Coordinates.Count,
        "direct parse duplicate preservation");

    Assert(NativeDynamicRoomEventDescriptorLoader.TryParse("""
            $101 x2 $A,0xB
            x102 X3 xC,Xd
            X103 0x4 0Xe,$F
            0x104 0X5 1,2
            $FFFFFFFF $FFFFFFFF 3,4
            +$105 -$FFFFFFFF 5,6
            2147483648 2147483648 7,8
            0x100000000 0x100000000 9,10
            7x 8x 11,12
            """, out descriptors, out diagnostics),
        "native numeric parse failed");
    Equal(9, descriptors.Count, "native numeric descriptor count");
    NumericDescriptor(descriptors[0], 0x101, 1, 2, 10, 11,
        "$ prefix");
    NumericDescriptor(descriptors[1], 0x102, 2, 3, 12, 13,
        "lowercase x prefix");
    NumericDescriptor(descriptors[2], 0x103, 3, 4, 14, 15,
        "uppercase X prefix");
    NumericDescriptor(descriptors[3], 0x104, 4, 5, 1, 2,
        "0x and 0X prefixes");
    NumericDescriptor(descriptors[4], ushort.MaxValue, byte.MaxValue, -1,
        3, 4, "full 32-bit hexadecimal pattern");
    NumericDescriptor(descriptors[5], 0x105, 5, 1, 5, 6,
        "signed hexadecimal pattern");
    NumericDescriptor(descriptors[6], 0, 0, 1, 7, 8,
        "decimal overflow defaults");
    NumericDescriptor(descriptors[7], 0, 0, 1, 9, 10,
        "hexadecimal overflow defaults");
    NumericDescriptor(descriptors[8], 0, 0, 1, 11, 12,
        "trailing characters default");
}
finally
{
    Directory.Delete(root, true);
}

Console.WriteLine("DynRoomEventDescriptorLoaderCheck PASS "
    + "filename=Envent comments=raw defaults=ok type=low8 coordinates=ordered");

static void Coordinate(int expectedX, int expectedY,
    NativeDynamicRoomEventCoordinate actual, string message)
{
    Equal(expectedX, actual.X, message + " X");
    Equal(expectedY, actual.Y, message + " Y");
}

static void NumericDescriptor(NativeDynamicRoomEventDescriptor descriptor,
    int rawEventType, int effectiveEventType, int durationSeconds,
    int x, int y, string message)
{
    Equal(unchecked((ushort)rawEventType), descriptor.RawEventType,
        message + " raw type");
    Equal(unchecked((byte)effectiveEventType), descriptor.EffectiveEventType,
        message + " effective type");
    Equal(durationSeconds, descriptor.DurationSeconds,
        message + " duration");
    Coordinate(x, y, descriptor.Coordinates.Single(), message + " coordinate");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
