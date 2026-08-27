// 神佑 named-ability block - Zhanshen equivalence audit.
//
// Three native functions are covered.
//
// sub_746D6C (build), per slot i in 0..9, into the 0x28 bytes at container+0x375
// that 0x746D96 FillChars to zero first:
//   0x746DC1  di = word[self + i*2 + 0x5A8]
//   0x746DCC  je            -> entry stays zero
//   0x746DD8  sub_49F0EC(id); 0x746DE4 je -> entry stays zero
//   0x746DEF  s  = ShortString at record+0xC
//   0x746E00  sub_4C6AEC(s, ' ') -> head [ebp-0xC], rest [ebp-0x10]
//   0x746E16  sub_40C140 Trim(rest)
//   0x746E2F  sub_78FB6C(head)          -> word[block + i*4]
//   0x746E44  sub_40CA18(rest, 10)      -> word[block + i*4 + 2]   (low word only)
//
// sub_78E830 (apply): EAX = container+0x48, EDX = container+0x1F8, ECX = code,
// value on the stack, `ret 4`. 0x78E83E `cmp ecx,0xFE` / `ja 0x78F329` drops
// anything above 254 and 112 of the 255 table entries are the bare epilogue.
//
// sub_75F548 (consume): walks the 10 entries, skips code 0 (`test ax,ax` / `jbe`
// is `je` because test clears CF), and after EVERY slot - applied or skipped -
// tests `cmp edi,[[0x7D5AEC]]` / `je` and stops on equality.
using System.Buffers.Binary;
using GameSvr;
using SystemModule;

try
{
    PrepareRuntime();

    CheckBlockGeometry();
    CheckEmptyWindowBuildsAnEmptyBlock();
    CheckUnknownSlotIdLeavesTheEntryZero();
    CheckNameAndValueSplit();
    CheckValueDefaultsToTenNotZero();
    CheckValueKeepsOnlyTheLowWord();
    CheckUnknownAbilityNameStoresTheNotFoundCode();
    CheckSentinelName();
    CheckApplyAddsAtTheNativeOffset();
    CheckApplyWrapsAtTheFieldWidth();
    CheckApplyMaxArmTakesTheLarger();
    CheckApplyIgnoresCodesAboveTheTableBound();
    CheckConsumerSkipsEmptyEntries();
    CheckConsumerStopsOnTheSlotCap();
    CheckConsumerCapZeroEdgeCases();

    Console.WriteLine(
        "PASS NativeShenYouAbilityBlock block=10x{word code, word value}@container+0x375; " +
        "build splits the config name on ' ', Trim()s the rest and StrToIntDef(.,10)s it; " +
        "apply = sub_78E830(base@+0x48, added@+0x1F8, code, value) with codes >0xFE dropped; " +
        "consume stops when the applied count EQUALS [[0x7D5AEC]], tested after every slot");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeShenYouAbilityBlockCheck FAIL: {exception}");
    return 1;
}

// ---------------------------------------------------------------------------
// Geometry, straight off the image.
// ---------------------------------------------------------------------------

static void CheckBlockGeometry()
{
    Equal(10, NativeShenYouAbilityBlock.SlotCount, "10 entries (0x746E52 cmp ebx,0xA)");
    Equal(4, NativeShenYouAbilityBlock.EntrySize, "stride 4 (ebx*4 in both 0x746E34 and 0x75F554)");
    Equal(0x28, NativeShenYouAbilityBlock.BlockSize, "0x746D96 mov edx,0x28");
    Equal(0x375, NativeShenYouAbilityBlock.ContainerOffset, "0x746D8F add eax,0x375");
    Equal(10, NativeShenYouAbilityBlock.ValueDefault, "0x746E3C mov edx,0xA");

    Equal(0x48, NativeAbilityApply.BaseBlockOffset, "base block at container+0x48");
    Equal(0x1B0, NativeAbilityApply.BaseBlockSize, "0x75F515 mov edx,0x1B0");
    Equal(0x1F8, NativeAbilityApply.AddedBlockOffset, "added block at container+0x1F8");
    Equal(0x36, NativeAbilityApply.AddedBlockSize, "0x75F527 mov edx,0x36");
    Equal(NativeAbilityApply.AddedBlockOffset,
        NativeAbilityApply.BaseBlockOffset + NativeAbilityApply.BaseBlockSize,
        "the base block ends exactly where the added block starts");
    Equal(0xFE, NativeAbilityApply.MaxCode, "0x78E83E cmp ecx,0xFE");
}

// ---------------------------------------------------------------------------
// sub_746D6C.
// ---------------------------------------------------------------------------

static void CheckEmptyWindowBuildsAnEmptyBlock()
{
    LoadConfig("1|5|0|攻击上限 3");
    var block = Build();
    Equal(NativeShenYouAbilityBlock.BlockSize, block.Length, "block is always 0x28 bytes");
    Assert(NativeShenYouAbilityBlock.IsEmpty(block), "no slot -> no entry");
}

static void CheckUnknownSlotIdLeavesTheEntryZero()
{
    LoadConfig("1|5|0|攻击上限 3");
    // 0x746DE4 `je 0x746E51`: a lookup miss skips both stores, so the FillChar zeros stand.
    var block = Build(999);
    Assert(NativeShenYouAbilityBlock.IsEmpty(block), "unknown slot id -> entry stays zero");
}

static void CheckNameAndValueSplit()
{
    LoadConfig("1|5|0|攻击上限 3");
    var block = Build(1);
    Equal(Code("攻击上限"), Entry(block, 0).Code, "head token resolves through sub_78FB6C");
    Equal(3, Entry(block, 0).Value, "tail token is the value");

    // Trim only matters on the tail; the head keeps whatever sub_4C6AEC handed back.
    LoadConfig("1|5|0|攻击上限   12  ");
    Equal(12, Entry(Build(1), 0).Value, "the tail is Trim()ed before StrToIntDef");
}

static void CheckValueDefaultsToTenNotZero()
{
    // 0x746E3C loads 10 as the StrToIntDef default, so a name with no value is 10.
    LoadConfig("1|5|0|攻击上限");
    Equal(10, Entry(Build(1), 0).Value, "missing value -> 10");

    LoadConfig("1|5|0|攻击上限 abc");
    Equal(10, Entry(Build(1), 0).Value, "unparsable value -> 10");
}

static void CheckValueKeepsOnlyTheLowWord()
{
    // 0x746E49 `mov word [edi+ebx*4+0x377], ax` stores AX, not EAX.
    LoadConfig("1|5|0|攻击上限 65540");
    Equal(65540 & 0xFFFF, Entry(Build(1), 0).Value, "the value is truncated to a word");
}

static void CheckUnknownAbilityNameStoresTheNotFoundCode()
{
    LoadConfig("1|5|0|不存在的属性 7");
    var entry = Entry(Build(1), 0);
    Equal(NativeAbilityNameTable.NotFoundCode, entry.Code,
        "an unknown name stores sub_78FB6C's not-found value verbatim");

    // It is non-zero, so the consumer still counts it against the cap even though
    // 0x78E844 `ja 0x78F329` then throws the apply away.
    var container = FreshContainer();
    NativeShenYouAbilityBlock.Apply(container, Build(1), slotCap: 10);
    Assert(IsAllZero(container), "a not-found code changes nothing");
}

static void CheckSentinelName()
{
    Equal(0xFF, NativeAbilityNameTable.SentinelCode, "the sentinel code");
    Equal(0xFF, NativeAbilityNameTable.Lookup(NativeAbilityNameTable.SentinelName),
        "the sentinel name resolves to 0xFF, inside the 0xFE bound only by ja/jbe");
}

// ---------------------------------------------------------------------------
// sub_78E830 through the block.
// ---------------------------------------------------------------------------

static void CheckApplyAddsAtTheNativeOffset()
{
    // Code 11 is `add dword [ebx+0], esi` -> container+0x48.
    LoadConfig("1|5|0|" + Name(11) + " 7");
    var container = FreshContainer();
    NativeShenYouAbilityBlock.Apply(container, Build(1), slotCap: 10);
    Equal(7, BinaryPrimitives.ReadInt32LittleEndian(container.AsSpan(0x48)),
        "code 11 lands on container+0x48");

    // Code 158 is the last live arm, at base+0x1AE -> container+0x1F6, the final two
    // bytes before the added block.
    LoadConfig("1|5|0|" + Name(158) + " 9");
    container = FreshContainer();
    NativeShenYouAbilityBlock.Apply(container, Build(1), slotCap: 10);
    Equal((ushort)9, BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(0x1F6)),
        "code 158 lands on the last word of the base block");
}

static void CheckApplyWrapsAtTheFieldWidth()
{
    // Code 13 is `add word [ebx+8], si`; two adds of 40000 wrap modulo 0x10000.
    LoadConfig("1|5|0|" + Name(13) + " 40000");
    var container = FreshContainer();
    var block = Build(1, 1);
    NativeShenYouAbilityBlock.Apply(container, block, slotCap: 10);
    Equal((ushort)((40000 * 2) & 0xFFFF),
        BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(0x48 + 8)),
        "a word field wraps exactly as the native add does");
}

static void CheckApplyMaxArmTakesTheLarger()
{
    // Code 86 is a Max arm on the base block at +0x118 -> container+0x160.
    LoadConfig("1|5|0|" + Name(86) + " 5", "2|5|0|" + Name(86) + " 2");
    var container = FreshContainer();
    NativeShenYouAbilityBlock.Apply(container, Build(1, 2), slotCap: 10);
    Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(0x160)),
        "Max keeps 5 when 2 arrives second (sub_4C7004)");

    container = FreshContainer();
    NativeShenYouAbilityBlock.Apply(container, Build(2, 1), slotCap: 10);
    Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(0x160)),
        "and reaches 5 when 5 arrives second");
}

static void CheckApplyIgnoresCodesAboveTheTableBound()
{
    var container = FreshContainer();
    NativeAbilityApply.Apply(
        container.AsSpan(NativeAbilityApply.BaseBlockOffset, NativeAbilityApply.BaseBlockSize),
        container.AsSpan(NativeAbilityApply.AddedBlockOffset, NativeAbilityApply.AddedBlockSize),
        0xFF, 5);
    Assert(IsAllZero(container), "0x78E844 ja drops code 0xFF");

    NativeAbilityApply.Apply(
        container.AsSpan(NativeAbilityApply.BaseBlockOffset, NativeAbilityApply.BaseBlockSize),
        container.AsSpan(NativeAbilityApply.AddedBlockOffset, NativeAbilityApply.AddedBlockSize),
        38, 5);
    Assert(IsAllZero(container), "code 38 (中毒恢复) routes to the bare epilogue");
}

// ---------------------------------------------------------------------------
// sub_75F548.
// ---------------------------------------------------------------------------

static void CheckConsumerSkipsEmptyEntries()
{
    LoadConfig("1|5|0|" + Name(11) + " 7");
    var container = FreshContainer();
    // Slot 0 empty, slot 1 filled: the empty entry must not consume a cap unit while
    // the cap is high enough for the walk to reach slot 1.
    NativeShenYouAbilityBlock.Apply(container, Build(0, 1), slotCap: 10);
    Equal(7, BinaryPrimitives.ReadInt32LittleEndian(container.AsSpan(0x48)),
        "a leading empty entry does not stop the walk");
}

static void CheckConsumerStopsOnTheSlotCap()
{
    LoadConfig("1|5|0|" + Name(11) + " 7");
    var container = FreshContainer();
    NativeShenYouAbilityBlock.Apply(container, Build(1, 1, 1, 1, 1), slotCap: 2);
    Equal(2 * 7, BinaryPrimitives.ReadInt32LittleEndian(container.AsSpan(0x48)),
        "the walk stops the moment the applied count equals the cap");
}

static void CheckConsumerCapZeroEdgeCases()
{
    LoadConfig("1|5|0|" + Name(11) + " 7");

    // Cap 0 with an empty first entry: applied is already 0, so the equality test at
    // 0x75F574 fires before anything is applied.
    var container = FreshContainer();
    NativeShenYouAbilityBlock.Apply(container, Build(0, 1, 1), slotCap: 0);
    Assert(IsAllZero(container), "cap 0 with an empty first slot applies nothing");

    // Cap 0 with a filled first entry: applied becomes 1 and never equals 0 again, so
    // the walk runs to the end. This asymmetry is native's, not a modelling choice.
    container = FreshContainer();
    NativeShenYouAbilityBlock.Apply(container, Build(1, 1, 1), slotCap: 0);
    Equal(3 * 7, BinaryPrimitives.ReadInt32LittleEndian(container.AsSpan(0x48)),
        "cap 0 with a filled first slot applies every entry");
}

// ---------------------------------------------------------------------------
// Harness.
// ---------------------------------------------------------------------------

static byte[] Build(params int[] slotIds)
{
    Span<ushort> slots = stackalloc ushort[NativeShenYouAbilityBlock.SlotCount];
    for (var i = 0; i < slotIds.Length && i < slots.Length; i++)
        slots[i] = (ushort)slotIds[i];
    return NativeShenYouAbilityBlock.Build(slots, NativeShenYouAttributeConfig.Shared);
}

static (int Code, int Value) Entry(byte[] block, int index)
{
    var at = index * NativeShenYouAbilityBlock.EntrySize;
    return (BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(at)),
        BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(at + 2)));
}

static byte[] FreshContainer()
    => new byte[NativeAbilityApply.AddedBlockOffset + NativeAbilityApply.AddedBlockSize];

static bool IsAllZero(byte[] buffer)
{
    for (var i = 0; i < buffer.Length; i++)
        if (buffer[i] != 0) return false;
    return true;
}

static string Name(int code)
    => NativeAbilityNameTable.GetName(code)
       ?? throw new InvalidOperationException("no name for code " + code);

static int Code(string name) => NativeAbilityNameTable.Lookup(name);

static void LoadConfig(params string[] rows)
{
    var dir = Path.Combine(Path.GetTempPath(), "NativeShenYouAbilityBlockCheck",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var file = Path.Combine(dir, "shenyou.txt");
    File.WriteAllText(file, string.Join(Environment.NewLine, rows) + Environment.NewLine,
        HUtil32.GbkEncoding);
    File.WriteAllText(
        Path.Combine(dir, NativeShenYouAttributeConfig.AbilSwitchFileName),
        "[setup]" + Environment.NewLine + "ShenYouAbilSwitch=0" + Environment.NewLine,
        HUtil32.GbkEncoding);

    if (!NativeShenYouAttributeConfig.Shared.Reload(file, dir, out var error))
        throw new InvalidOperationException("seed load failed: " + error);
}

static void PrepareRuntime()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);

    M2Share.g_Config = new GameSvrConfig();
    M2Share.RandomNumber = RandomNumber.GetInstance();
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: {actual}, expected {expected}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}
