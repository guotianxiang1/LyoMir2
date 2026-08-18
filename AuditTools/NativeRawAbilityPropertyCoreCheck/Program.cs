using System.Buffers.Binary;
using GameSvr;

internal static class Program
{
public static void Main()
{
    CheckAllAddMappingsAndWrap();
    CheckMaxMappingsAndTruncation();
    CheckFlagsAndProperty254();
    CheckCappedByteOrder();
    CheckCompleteNoOpDomain();
    CheckEntryContracts();

    Console.WriteLine("PASS NativeRawAbilityPropertyCoreCheck " +
                      "function=sub_78E830 ids=0..65535 mappings=complete " +
                      "integer=unchecked max=signed-then-truncate");
}

static readonly IReadOnlyDictionary<ushort, int> AddInt32 =
    new Dictionary<ushort, int>
    {
        [1]=0x1C,[2]=0x20,[3]=0x24,[4]=0x28,[5]=0x2C,[6]=0x30,
        [7]=0x0C,[8]=0x10,[9]=0x14,[10]=0x18,[11]=0x00,[12]=0x04,
        [29]=0x6C,[34]=0xD8,[37]=0x7C,[40]=0x88,[41]=0x8C,
        [42]=0x90,[43]=0x94,[44]=0x98,[45]=0x9C,[48]=0xBC,
        [49]=0xC0,[57]=0xDC,[58]=0xE4,[59]=0xE0,[60]=0xE8,
        [61]=0xEC,[64]=0x58,[65]=0xF4,[66]=0xAC,[71]=0xD4,
        [79]=0x108,[98]=0x124,[99]=0x128,[100]=0x12C,[101]=0x130,
        [105]=0x13C,[106]=0x140,[107]=0x144,[108]=0x148,
        [109]=0x14C,[110]=0x150,[111]=0x34,[112]=0x38,[113]=0xA0,
        [114]=0xA4,[115]=0x154,[118]=0x158,[122]=0x164,
        [123]=0x168,[126]=0x170,[130]=0x17C,[133]=0x184,
        [135]=0x18C,[137]=0x80,[138]=0xC4,[139]=0xC8,
        [142]=0xA8,[143]=0x194
    };

static readonly IReadOnlyDictionary<ushort, int> AddUInt16 =
    new Dictionary<ushort, int>
    {
        [13]=0x08,[14]=0x0A,[15]=0x44,[18]=0x4C,[19]=0x5C,
        [20]=0x5E,[21]=0x60,[22]=0x64,[23]=0x66,[24]=0x68,
        [25]=0x6A,[26]=0x70,[27]=0x72,[28]=0x74,[30]=0x3C,
        [31]=0x4A,[32]=0xFA,[33]=0xF8,[35]=0x76,[36]=0x78,
        [39]=0x86,[46]=0xB0,[47]=0xB2,[50]=0xCC,[52]=0xCE,
        [53]=0xD0,[54]=0xB6,[62]=0xF0,[67]=0xF2,[75]=0xFE,
        [76]=0x100,[77]=0x102,[78]=0x106,[80]=0x10C,[81]=0x10E,
        [82]=0x110,[83]=0x112,[84]=0x114,[85]=0x116,[87]=0x11A,
        [95]=0x104,[96]=0xFC,[102]=0x134,[103]=0x136,
        [104]=0x138,[120]=0x15E,[124]=0x16C,[125]=0x16E,
        [128]=0x176,[129]=0x178,[132]=0x182,[134]=0x188,
        [136]=0x190,[140]=0xB4,[144]=0x198,[158]=0x1AE
    };

static readonly IReadOnlyDictionary<ushort, int> AddByte =
    new Dictionary<ushort, int>
    {
        [16]=0x46,[17]=0x47,[90]=0x11E,[91]=0x11F,[92]=0x120,
        [121]=0x160,[127]=0x174,[131]=0x180,[141]=0x192
    };

static readonly IReadOnlyDictionary<ushort, (bool Primary, int Offset)>
    MaxUInt16 = new Dictionary<ushort, (bool, int)>
    {
        [55]=(false,0x28),[86]=(true,0x118),[93]=(false,0x30),
        [94]=(false,0x32)
    };

static readonly IReadOnlyDictionary<ushort, int> MaxByte =
    new Dictionary<ushort, int>
    {
        [56]=0x2A,[69]=0x2D,[117]=0x20
    };

static readonly IReadOnlyDictionary<ushort, int> Flags =
    new Dictionary<ushort, int>
    {
        [63]=0x18,[68]=0x2C,[70]=0x2E,[72]=0x0A,[73]=0x23,
        [74]=0x2F,[88]=0x07,[89]=0x07
    };

static void CheckAllAddMappingsAndWrap()
{
    foreach (var pair in AddInt32)
    {
        var (primary, secondary) = Blocks();
        WriteUInt32(primary, pair.Value, 0xFFFFFFFE);
        NativeRawAbilityPropertyCore.Apply(primary, secondary, pair.Key, 5);
        Equal(3u, ReadUInt32(primary, pair.Value),
            $"ID {pair.Key} dword wrap at +{pair.Value:X}");
        CheckOnly(primary, pair.Value, 4, $"ID {pair.Key} dword target");
        CheckZero(secondary, $"ID {pair.Key} secondary untouched");
    }

    foreach (var pair in AddUInt16)
    {
        var (primary, secondary) = Blocks();
        WriteUInt16(primary, pair.Value, 0xFFFE);
        NativeRawAbilityPropertyCore.Apply(primary, secondary, pair.Key, 5);
        Equal((ushort)3, ReadUInt16(primary, pair.Value),
            $"ID {pair.Key} word wrap at +{pair.Value:X}");
        CheckOnly(primary, pair.Value, 2, $"ID {pair.Key} word target");
        CheckZero(secondary, $"ID {pair.Key} secondary untouched");
    }

    foreach (var pair in AddByte)
    {
        var (primary, secondary) = Blocks();
        primary[pair.Value] = 0xFE;
        NativeRawAbilityPropertyCore.Apply(primary, secondary, pair.Key, 5);
        Equal((byte)3, primary[pair.Value],
            $"ID {pair.Key} byte wrap at +{pair.Value:X}");
        CheckOnly(primary, pair.Value, 1, $"ID {pair.Key} byte target");
        CheckZero(secondary, $"ID {pair.Key} secondary untouched");
    }
}

static void CheckMaxMappingsAndTruncation()
{
    foreach (var pair in MaxUInt16)
    {
        var (primary, secondary) = Blocks();
        var block = pair.Value.Primary ? primary : secondary;
        WriteUInt16(block, pair.Value.Offset, 9);
        NativeRawAbilityPropertyCore.Apply(primary, secondary, pair.Key, -1);
        Equal((ushort)9, ReadUInt16(block, pair.Value.Offset),
            $"ID {pair.Key} signed max preserves larger current value");
        NativeRawAbilityPropertyCore.Apply(primary, secondary, pair.Key,
            0x12345);
        Equal((ushort)0x2345, ReadUInt16(block, pair.Value.Offset),
            $"ID {pair.Key} truncates after signed max");
    }

    foreach (var pair in MaxByte)
    {
        var (primary, secondary) = Blocks();
        secondary[pair.Value] = 9;
        NativeRawAbilityPropertyCore.Apply(primary, secondary, pair.Key, -1);
        Equal((byte)9, secondary[pair.Value],
            $"ID {pair.Key} signed max preserves byte");
        NativeRawAbilityPropertyCore.Apply(primary, secondary, pair.Key,
            0x123);
        Equal((byte)0x23, secondary[pair.Value],
            $"ID {pair.Key} truncates after signed max");
    }
}

static void CheckFlagsAndProperty254()
{
    foreach (var pair in Flags)
    {
        var (primary, secondary) = Blocks();
        secondary[pair.Value] = 0xA5;
        NativeRawAbilityPropertyCore.Apply(primary, secondary, pair.Key,
            int.MinValue);
        Equal((byte)1, secondary[pair.Value],
            $"ID {pair.Key} overwrites flag +{pair.Value:X}");
        CheckZero(primary, $"ID {pair.Key} primary untouched");
        CheckOnly(secondary, pair.Value, 1,
            $"ID {pair.Key} flag target", expectedOutside: 0);
    }

    var offsets = new[] { 0x0B, 0x05, 0x21, 0x13, 0x0C, 0x04, 0x06 };
    for (var selector = 0; selector <= 6; selector++)
    {
        foreach (var value in new[] { selector, selector | 0x80 })
        {
            var (primary, secondary) = Blocks();
            secondary[offsets[selector]] = 0xA5;
            NativeRawAbilityPropertyCore.Apply(primary, secondary, 254,
                value);
            Equal((byte)1, secondary[offsets[selector]],
                $"ID 254 selector {value:X} target");
            CheckZero(primary, "ID 254 primary untouched");
            CheckOnly(secondary, offsets[selector], 1,
                $"ID 254 selector {value:X}", expectedOutside: 0);
        }
    }

    foreach (var value in new[] { 7, 0x7F, -1 })
    {
        var (primary, secondary) = PatternBlocks();
        var beforePrimary = (byte[])primary.Clone();
        var beforeSecondary = (byte[])secondary.Clone();
        NativeRawAbilityPropertyCore.Apply(primary, secondary, 254, value);
        Sequence(beforePrimary, primary, $"ID 254 invalid {value} primary");
        Sequence(beforeSecondary, secondary,
            $"ID 254 invalid {value} secondary");
    }
}

static void CheckCappedByteOrder()
{
    var (primary, secondary) = Blocks();
    primary[0x15C] = 250;
    NativeRawAbilityPropertyCore.Apply(primary, secondary, 116, 10);
    Equal((byte)4, primary[0x15C],
        "ID 116 wraps byte before seven clamp");
    primary[0x15C] = 6;
    NativeRawAbilityPropertyCore.Apply(primary, secondary, 116, 2);
    Equal((byte)7, primary[0x15C], "ID 116 clamps wrapped value above seven");

    secondary[0x34] = 0;
    NativeRawAbilityPropertyCore.Apply(primary, secondary, 119, 0x100);
    Equal((byte)0, secondary[0x34],
        "ID 119 truncates max before one clamp");
    NativeRawAbilityPropertyCore.Apply(primary, secondary, 119, 2);
    Equal((byte)1, secondary[0x34], "ID 119 clamps byte above one");
    NativeRawAbilityPropertyCore.Apply(primary, secondary, 119, -1);
    Equal((byte)1, secondary[0x34],
        "ID 119 signed max preserves current byte");
}

static void CheckCompleteNoOpDomain()
{
    var active = AddInt32.Keys.Concat(AddUInt16.Keys).Concat(AddByte.Keys)
        .Concat(MaxUInt16.Keys).Concat(MaxByte.Keys).Concat(Flags.Keys)
        .Concat(new ushort[] { 116, 119, 254 }).ToHashSet();
    for (var raw = 0; raw <= ushort.MaxValue; raw++)
    {
        var propertyId = (ushort)raw;
        if (active.Contains(propertyId)) continue;
        var (primary, secondary) = PatternBlocks();
        var beforePrimary = (byte[])primary.Clone();
        var beforeSecondary = (byte[])secondary.Clone();
        NativeRawAbilityPropertyCore.Apply(primary, secondary, propertyId,
            unchecked((int)0x89ABCDEF));
        Sequence(beforePrimary, primary, $"no-op ID {propertyId} primary");
        Sequence(beforeSecondary, secondary,
            $"no-op ID {propertyId} secondary");
    }
}

static void CheckEntryContracts()
{
    var (primary, secondary) = PatternBlocks();
    var beforePrimary = (byte[])primary.Clone();
    var beforeSecondary = (byte[])secondary.Clone();

    foreach (var noOp in new[] { -1, 0, 38, 255, 65536, int.MaxValue })
    {
        NativeRawAbilityPropertyCore.Apply(null, null, noOp, 1);
    }
    ExpectThrows<ArgumentNullException>(() =>
            NativeRawAbilityPropertyCore.Apply(null, null, 1, 1),
        "primary branch dereferences primary");
    ExpectThrows<ArgumentNullException>(() =>
            NativeRawAbilityPropertyCore.Apply(null, null, 63, 1),
        "secondary branch dereferences secondary");

    var primaryOnly = new byte[0x20];
    NativeRawAbilityPropertyCore.Apply(primaryOnly, null, 1, 1);
    Equal(1u, ReadUInt32(primaryOnly, 0x1C),
        "primary branch does not dereference secondary");
    var secondaryOnly = new byte[0x19];
    NativeRawAbilityPropertyCore.Apply(null, secondaryOnly, 63, 1);
    Equal((byte)1, secondaryOnly[0x18],
        "secondary branch does not dereference primary");

    ExpectThrows<ArgumentException>(() =>
            NativeRawAbilityPropertyCore.Apply(new byte[0x1AF], null,
                158, 1),
        "selected primary range must be addressable");
    ExpectThrows<ArgumentException>(() =>
            NativeRawAbilityPropertyCore.Apply(null, new byte[0x34],
                119, 1),
        "selected secondary range must be addressable");
    NativeRawAbilityPropertyCore.Apply(new byte[0x1B1], null, 158, 1);
    NativeRawAbilityPropertyCore.Apply(null, new byte[0x37], 119, 1);
    Sequence(beforePrimary, primary, "entry guards preserve primary");
    Sequence(beforeSecondary, secondary, "entry guards preserve secondary");
    Equal(0x0078E830u, NativeRawAbilityPropertyCore.OriginalFunction,
        "native function address");
    Equal(0x1B0, NativeRawAbilityPropertyCore.PrimarySize,
        "primary block size");
    Equal(0x36, NativeRawAbilityPropertyCore.SecondarySize,
        "secondary block size");
}

static (byte[] Primary, byte[] Secondary) Blocks() =>
    (new byte[NativeRawAbilityPropertyCore.PrimarySize],
        new byte[NativeRawAbilityPropertyCore.SecondarySize]);

static (byte[] Primary, byte[] Secondary) PatternBlocks()
{
    var result = Blocks();
    for (var index = 0; index < result.Primary.Length; index++)
        result.Primary[index] = unchecked((byte)(index * 37 + 11));
    for (var index = 0; index < result.Secondary.Length; index++)
        result.Secondary[index] = unchecked((byte)(index * 53 + 19));
    return result;
}

static void CheckOnly(byte[] block, int offset, int length, string label,
    byte expectedOutside = 0)
{
    for (var index = 0; index < block.Length; index++)
    {
        if (index >= offset && index < offset + length) continue;
        Equal(expectedOutside, block[index], label + $" outside +{index:X}");
    }
}

static void CheckZero(byte[] block, string label)
{
    for (var index = 0; index < block.Length; index++)
        Equal((byte)0, block[index], label + $" +{index:X}");
}

static ushort ReadUInt16(byte[] block, int offset) =>
    BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(offset, 2));

static void WriteUInt16(byte[] block, int offset, ushort value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(offset, 2), value);

static uint ReadUInt32(byte[] block, int offset) =>
    BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(offset, 4));

static void WriteUInt32(byte[] block, int offset, uint value) =>
    BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(offset, 4), value);

static void Sequence(IReadOnlyList<byte> expected,
    IReadOnlyList<byte> actual, string label)
{
    Equal(expected.Count, actual.Count, label + " count");
    for (var index = 0; index < expected.Count; index++)
        Equal(expected[index], actual[index], label + $"[{index:X}]");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{label}: expected={expected}, actual={actual}");
}

static void ExpectThrows<TException>(Action action, string label)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new Exception(label);
}
}
