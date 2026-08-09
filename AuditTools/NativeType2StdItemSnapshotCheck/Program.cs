using System.Buffers.Binary;
using System.Text;
using GameSvr.Services;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var verifiedStartup = NativeType2StdItemSnapshotState
    .CreateForVerifiedOriginalStartup();
Equal(NativeType2StdItemSnapshotState.VerifiedOriginalStartupListCount,
    verifiedStartup.InitialNativeListCount, "verified startup list count");
Equal(1, verifiedStartup.ExpectedWireIndex,
    "verified startup first wire index");

var state = new NativeType2StdItemSnapshotState(initialNativeListCount: 0);
var callbackCount = 0;
state.SetCompletionCallback(_ => callbackCount++);

var first = CreateBody(0, "测试物品",
    " 攻击下限:123|灵媒:$7|战意麻痹神技:ignored|魔法命中:0x2A");
Equal(NativeType2StdItemSnapshotResult.RecordAppended,
    state.Consume(CreatePacket(first, completed: false)),
    "first indexed record");
Equal(1, state.Records.Count, "first record count");
var firstSlots = state.Records[0].CopyExtensionSlots();
Equal((ushort)1, ReadUInt16(firstSlots, 0), "primary attribute code");
Equal((ushort)123, ReadUInt16(firstSlots, 2), "decimal value");
Equal((ushort)255, ReadUInt16(firstSlots, 4), "lingmei special code");
Equal((ushort)7, ReadUInt16(firstSlots, 6), "lingmei hex value");
Equal((ushort)0x00FE, ReadUInt16(firstSlots, 8), "secondary marker");
Equal((ushort)1, ReadUInt16(firstSlots, 10), "secondary table index");
Equal((ushort)158, ReadUInt16(firstSlots, 12), "last primary table code");
Equal((ushort)42, ReadUInt16(firstSlots, 14), "0x hex value");
Check(state.Records[0].ItemExtAbilParsed, "valid extension parse");
Check(state.TryGetLatestByNameBytes(Gbk("测试物品"), out var named)
      && ReferenceEquals(named, state.Records[0]), "native name lookup");

var duplicateWithUnknown = CreateBody(1, "测试物品", "攻击下限:1|未知:2");
Equal(NativeType2StdItemSnapshotResult.RecordAppendedWithExtensionError,
    state.Consume(CreatePacket(duplicateWithUnknown, completed: false)),
    "unknown attribute still inserts");
Equal(2, state.Records.Count, "unknown attribute record count");
var partialSlots = state.Records[1].CopyExtensionSlots();
Equal((ushort)1, ReadUInt16(partialSlots, 0), "partial code retained");
Equal((ushort)1, ReadUInt16(partialSlots, 2), "partial value retained");
Equal((ushort)0, ReadUInt16(partialSlots, 4), "failed slot remains zero");
Check(state.TryGetLatestByNameBytes(Gbk("测试物品"), out named)
      && ReferenceEquals(named, state.Records[1]), "duplicate name prefers latest");

var overSix = CreateBody(2, "六段", "攻击下限:1|攻击下限:2|攻击下限:3|攻击下限:4|攻击下限:5|攻击下限:6|攻击下限:7");
Equal(NativeType2StdItemSnapshotResult.RecordAppendedWithExtensionError,
    state.Consume(CreatePacket(overSix, completed: false)),
    "seventh extension is a normal parse failure");
var overSixSlots = state.Records[2].CopyExtensionSlots();
Equal((ushort)6, ReadUInt16(overSixSlots, 22), "sixth slot survives failure");

var rejectedSecondary = CreateBody(3, "副表", "八卦护身神技:ignored");
Equal(NativeType2StdItemSnapshotResult.RecordAppendedWithExtensionError,
    state.Consume(CreatePacket(rejectedSecondary, completed: false)),
    "secondary table index zero is rejected");

ExpectThrows<NativeType2StdItemNumericException>(() =>
    state.Consume(CreatePacket(CreateBody(4, "坏数值", "攻击下限:"), completed: true)),
    "empty numeric value escapes");
Equal(4, state.Records.Count, "numeric failure does not insert");
Check(!state.Completed && callbackCount == 0,
    "numeric failure skips terminal finalizer");

Equal(NativeType2StdItemSnapshotResult.RecordAppendedAndCompleted,
    state.Consume(CreatePacket(CreateBody(4, "结束", "攻击下限:-0x7"), completed: true)),
    "valid terminal record");
Check(state.Completed && callbackCount == 1, "terminal callback once");
Equal((ushort)0xFFF9, ReadUInt16(state.Records[4].CopyExtensionSlots(), 2),
    "negative hex low word");
Equal(NativeType2StdItemSnapshotResult.Ignored,
    state.Consume(CreatePacket(CreateBody(5, "忽略", "攻击下限:1"), completed: false)),
    "post-terminal packet");

var sequence = new NativeType2StdItemSnapshotState(initialNativeListCount: 7);
Equal(NativeType2StdItemSnapshotResult.SequenceRejected,
    sequence.Consume(CreatePacket(CreateBody(6, "错序", ""), completed: false)),
    "explicit baseline rejects wrong first index");
Equal(NativeType2StdItemSnapshotResult.RecordAppended,
    sequence.Consume(CreatePacket(CreateBody(7, "正确", ""), completed: false)),
    "wrong index does not resynchronize");

var shortTerminal = new NativeType2StdItemSnapshotState(initialNativeListCount: 42);
var shortCallbackCount = 0;
shortTerminal.SetCompletionCallback(_ => shortCallbackCount++);
Equal(NativeType2StdItemSnapshotResult.StreamCompleted,
    shortTerminal.Consume(CreatePacket(Array.Empty<byte>(), completed: true)),
    "short terminal completes stream");
Check(shortTerminal.Completed && shortCallbackCount == 1,
    "short terminal callback");

var numericForms = new NativeType2StdItemSnapshotState(initialNativeListCount: 0);
Equal(NativeType2StdItemSnapshotResult.RecordAppended,
    numericForms.Consume(CreatePacket(CreateBody(0, "数值",
        "攻击下限: +10|攻击上限:x2A|魔法下限:X2B|魔法上限:$2C|道术下限:0x2D"),
        completed: false)), "native numeric forms");
var numericSlots = numericForms.Records[0].CopyExtensionSlots();
Equal((ushort)10, ReadUInt16(numericSlots, 2), "leading-space plus decimal");
Equal((ushort)42, ReadUInt16(numericSlots, 6), "x hex");
Equal((ushort)43, ReadUInt16(numericSlots, 10), "X hex");
Equal((ushort)44, ReadUInt16(numericSlots, 14), "dollar hex");
Equal((ushort)45, ReadUInt16(numericSlots, 18), "zero-x hex");

var overflow = new NativeType2StdItemSnapshotState(initialNativeListCount: 0);
ExpectThrows<NativeType2StdItemNumericException>(() =>
    overflow.Consume(CreatePacket(CreateBody(0, "溢出", "攻击下限:2147483648"),
        completed: false)), "positive Int32 overflow escapes");
Equal(0, overflow.Records.Count, "overflow does not insert");

state.Reset();
Check(!state.Completed && state.Records.Count == 0 && state.ExpectedWireIndex == 0,
    "reset retains explicit baseline and clears stream");

Console.WriteLine("PASS NativeType2StdItemSnapshotCheck command=0068 " +
                  "baseline=verified-original-1/explicit parser=GBK-raw numeric=throws " +
                  "unknown=append duplicates=latest terminal=param2-equals-1");

static byte[] CreatePacket(byte[] body, bool completed)
{
    var payload = new byte[NativeType2StdItemSnapshotState.HeaderSize + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2StdItemSnapshotState.Command);
    if (completed)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    body.CopyTo(payload, NativeType2StdItemSnapshotState.HeaderSize);
    return payload;
}

static byte[] CreateBody(int index, string name, string itemExtAbil)
{
    var body = new byte[NativeType2StdItemSnapshotState.BodySize];
    BinaryPrimitives.WriteUInt16LittleEndian(body, unchecked((ushort)index));
    WriteShortString(body, 0x04, 15, name);
    WriteShortString(body, 0x5C, 200, itemExtAbil);
    return body;
}

static byte[] Gbk(string value) => Encoding.GetEncoding(936).GetBytes(value);

static void WriteShortString(byte[] destination, int offset, int maximumLength,
    string value)
{
    var encoded = Gbk(value);
    if (encoded.Length > maximumLength)
        throw new InvalidOperationException("fixture short string overflow");
    destination[offset] = unchecked((byte)encoded.Length);
    encoded.CopyTo(destination, offset + 1);
}

static ushort ReadUInt16(byte[] value, int offset) =>
    BinaryPrimitives.ReadUInt16LittleEndian(value.AsSpan(offset, 2));

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void ExpectThrows<TException>(Action action, string description)
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

    throw new InvalidOperationException(description);
}
