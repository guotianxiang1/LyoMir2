using System.Buffers.Binary;
using System.Text;
using GameSvr.Services;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

try
{
    VerifyCompleteWireModel();
    VerifySequenceOrderAndFirstNameIndex();
    VerifyItemExtAbilBoundary();
    VerifyDerivedFieldAndScriptBoundaries();
    VerifyCorrelationPermissionBoundary();
    Console.WriteLine(
        "PASS NativeType2StdItemRuntimeAppendCheck " +
        "body=0x134-exact sequence=idx-equals-count " +
        "order=preserved name-index=first " +
        "item-ext-abil=six-slots-and-fail-visible " +
        "logs=original-exact script=explicit-boundary " +
        "correlation=opaque-token permission=min-4");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeType2StdItemRuntimeAppendCheck FAIL: {exception}");
    return 1;
}

static void VerifyCompleteWireModel()
{
    var packet = Packet(0, "测试剑", correlation: -123456789,
        word2: 0xA1B2, param2: 0x10203040);
    var body = Body(packet);
    WriteUInt16(body, 0x02, 0x0203);
    body[0x14] = 0x14;
    body[0x15] = 0x15;
    body[0x16] = 0x16;
    body[0x17] = 0x17;
    for (var offset = 0x18; offset <= 0x3A; offset += 2)
        WriteUInt16(body, offset, 0x1000 + offset);
    WriteInt32(body, 0x3C, -1234567);
    body[0x40] = 0x40;
    body[0x41] = 0x41;
    WriteUInt16(body, 0x42, 0x4243);
    WriteUInt16(body, 0x44, 0x4445);
    WriteUInt16(body, 0x46, 0x4647);
    WriteUInt16(body, 0x48, 0x4849);
    WriteUInt16(body, 0x4A, 0x4A4B);
    WriteInt32(body, 0x4C, unchecked((int)0x8C4C4D4E));
    WriteInt32(body, 0x50, 0x50515253);
    WriteInt32(body, 0x54, 0x54555657);
    WriteUInt16(body, 0x58, 0x5859);
    WriteUInt16(body, 0x5A, 0x5A5B);
    WriteShortString(body, 0x5C, 200, "攻击下限:7");
    body[0x125] = 0xA5;
    WriteUInt16(body, 0x126, 0x2627);
    body[0x128] = 0x28;
    body[0x129] = 0xA9;
    body[0x12A] = 0xAA;
    body[0x12B] = 0xAB;
    WriteInt32(body, 0x12C, unchecked((int)0xCC2C2D2E));
    WriteUInt16(body, 0x130, 0x3031);
    body[0x132] = 0xB2;
    body[0x133] = 0xB3;

    Equal(NativeType2StdItemRuntimeDecodeResult.Decoded,
        NativeType2StdItemRuntimeDecoder.TryDecode(packet, out var decoded),
        "decode result");
    Equal((ushort)0xA1B2, decoded.Word2, "header Word2");
    Equal(-123456789, decoded.Correlation, "header correlation");
    Equal(0x10203040, decoded.Param2, "header Param2");

    var item = decoded.Definition;
    Equal((ushort)0, item.WireIndex, "wire index");
    Equal((ushort)0x0203, item.Reserved02, "reserved +02");
    Equal("测试剑", item.Name, "GBK name");
    Equal((byte)0x14, item.StdMode, "StdMode");
    Equal((byte)0x15, item.Shape, "Shape");
    Equal((byte)0x16, item.Need, "Need");
    Equal((byte)0x17, item.Source, "Source");
    Equal((ushort)0x1018, item.Looks, "Looks");
    Equal((ushort)0x101A, item.Weight, "Weight u16");
    Equal((ushort)0x101C, item.DuraMax, "DuraMax");
    Equal((ushort)0x101E, item.AniCount, "AniCount u16");
    Equal((ushort)0x1020, item.NeedConf, "NeedConf");
    Equal((ushort)0x1022, item.NeedLevel, "NeedLevel");
    Equal((ushort)0x1024, item.Ac, "AC");
    Equal((ushort)0x1026, item.MaxAc, "MaxAC");
    Equal((ushort)0x1028, item.Mac, "MAC");
    Equal((ushort)0x102A, item.MaxMac, "MaxMAC");
    Equal((ushort)0x102C, item.Dc, "DC");
    Equal((ushort)0x102E, item.MaxDc, "MaxDC");
    Equal((ushort)0x1030, item.Mc, "MC");
    Equal((ushort)0x1032, item.MaxMc, "MaxMC");
    Equal((ushort)0x1034, item.Sc, "SC");
    Equal((ushort)0x1036, item.MaxSc, "MaxSC");
    Equal((ushort)0x1038, item.Cc, "CC");
    Equal((ushort)0x103A, item.MaxCc, "MaxCC");
    Equal(-1234567, item.Price, "Price");
    Equal((byte)0x40, item.OutLookByte, "OutLook byte");
    Equal((byte)0x41, item.AntiqueLevel, "AntiqueLv retained");
    Equal((ushort)0x4243, item.ItemScore, "itemScore retained");
    Equal((ushort)0x4445, item.SuitEquipType, "SuitEquipType");
    Equal((ushort)0x4647, item.BaseEffectId, "BaseEffectID");
    Equal((ushort)0x4849, item.WordParam1, "wParam1");
    Equal((ushort)0x4A4B, item.WordParam2, "wParam2");
    Equal(unchecked((int)0x8C4C4D4E), item.IntParam1, "intParam");
    Equal(0x50515253, item.IntParam2, "intParam2");
    Equal(0x54555657, item.IntParam3, "intParam3");
    Equal((ushort)0x5859, item.MaxSteelLevel, "MaxSteelLv");
    Equal((ushort)0x5A5B, item.MaxVeinsLevel, "MaxVeinsLv");
    Equal("攻击下限:7", Gbk(item.CopyItemExtAbilBytes()),
        "ItemExtAbil bytes");
    Equal((ushort)0x2627, item.OutLookWord, "OutLook word");
    Equal((byte)0x28, item.NeedJob, "NeedJob");
    Equal(unchecked((int)0xCC2C2D2E), item.ItemLevel, "ItemLevel");
    Equal((ushort)0x3031, item.ItemConf, "ItemConf");
    Equal(@"PsItemScript\测试剑.pas", item.ScriptRelativePath,
        "script relative path");
    SequenceEqual(body.ToArray(), item.CopyWireBody(),
        "body including reserved padding");

    var oversized = new byte[NativeType2StdItemRuntimeProtocol.PacketSize + 7];
    packet.CopyTo(oversized, 0);
    Equal(NativeType2StdItemRuntimeDecodeResult.Decoded,
        NativeType2StdItemRuntimeDecoder.TryDecode(oversized, out _),
        "trailing bytes accepted");
    Equal(NativeType2StdItemRuntimeDecodeResult.PayloadTooShort,
        NativeType2StdItemRuntimeDecoder.TryDecode(
            packet.AsSpan(0, packet.Length - 1), out _),
        "body shorter than 0x134");
    packet[0] = 0x69;
    Equal(NativeType2StdItemRuntimeDecodeResult.Ignored,
        NativeType2StdItemRuntimeDecoder.TryDecode(packet, out _),
        "foreign command ignored");
}

static void VerifySequenceOrderAndFirstNameIndex()
{
    var catalog = new NativeType2StdItemRuntimeCatalog();
    var transaction = new NativeType2StdItemAppendTransaction(catalog);

    var rejected = transaction.Apply(Packet(1, "Wrong"));
    Equal(NativeType2StdItemAppendStatus.SequenceRejected,
        rejected.Status, "idx mismatch status");
    Equal(0, rejected.ExpectedIndex, "idx mismatch expected index");
    Equal(0, catalog.Count, "idx mismatch leaves catalog unchanged");
    SequenceEqual(new[]
    {
        "[Error]: 致命错误: StdItem.DB 数据出错",
        "运行期添加道具:Wrong"
    }, rejected.Logs, "idx mismatch exact log order");

    var first = transaction.Apply(Packet(0, "Blade"));
    var second = transaction.Apply(Packet(1, "blade"));
    Equal(NativeType2StdItemAppendStatus.Appended, first.Status,
        "first append");
    Equal(NativeType2StdItemAppendStatus.Appended, second.Status,
        "duplicate append");
    Equal(2, catalog.Count, "duplicates retained");
    SequenceEqual(new[] { "Blade", "blade" },
        catalog.Entries.Select(entry => entry.Definition.Name),
        "append order preserved");
    Check(catalog.TryGetFirstByName("BLADE", out var found),
        "case-insensitive name lookup");
    Equal(0, found.CatalogIndex, "first duplicate name index");
    Check(catalog.TryGetFirstByNameBytes(GbkBytes("blade"), out found),
        "GBK name byte lookup");
    Equal(0, found.CatalogIndex, "byte lookup first duplicate");
    Equal(NativeType2StdItemNeedIdentifyStatus.ResolverUnavailable,
        first.Entry.NeedIdentifyStatus, "missing resolver explicit");
    Equal(NativeType2StdItemScriptBindingStatus.BinderUnavailable,
        first.Entry.ScriptBinding.Status, "missing binder explicit");
}

static void VerifyItemExtAbilBoundary()
{
    var invalidCatalog = new NativeType2StdItemRuntimeCatalog();
    var invalid = new NativeType2StdItemAppendTransaction(invalidCatalog)
        .Apply(Packet(0, "BadExt", itemExtAbil: "不存在:1"));
    Equal(NativeType2StdItemAppendStatus.AppendedWithExtensionError,
        invalid.Status, "unknown extension still appended");
    Equal(1, invalidCatalog.Count,
        "extension parse failure does not roll back");
    SequenceEqual(new[]
    {
        "[error]: 错误的道具属性：BadExt: 不存在:1",
        "运行期添加道具:BadExt"
    }, invalid.Logs, "extension failure exact logs");
    Check(!invalid.Entry.ItemExtAbilParsed,
        "extension failure exposed on entry");

    var validCatalog = new NativeType2StdItemRuntimeCatalog();
    var valid = new NativeType2StdItemAppendTransaction(validCatalog)
        .Apply(Packet(0, "GoodExt",
            itemExtAbil: "攻击下限:4660|传送神技:999"));
    Check(valid.Entry.ItemExtAbilParsed, "extension success exposed");
    Equal((ushort)1, valid.Entry.ExtensionSlots[0].Ident,
        "primary extension ident");
    Equal((ushort)4660, valid.Entry.ExtensionSlots[0].Value,
        "primary extension value");
    Equal((ushort)0x00FE, valid.Entry.ExtensionSlots[1].Ident,
        "secondary extension marker");
    Equal((ushort)4, valid.Entry.ExtensionSlots[1].Value,
        "secondary extension table index");

    var numericCatalog = new NativeType2StdItemRuntimeCatalog();
    ExpectThrows<NativeType2StdItemNumericException>(() =>
        new NativeType2StdItemAppendTransaction(numericCatalog).Apply(
            Packet(0, "BadNumber", itemExtAbil: "攻击下限:no")),
        "native numeric conversion escapes");
    Equal(0, numericCatalog.Count,
        "numeric conversion failure leaves catalog unchanged");

    var wrongIndexCatalog = new NativeType2StdItemRuntimeCatalog();
    var wrongIndex = new NativeType2StdItemAppendTransaction(
        wrongIndexCatalog).Apply(Packet(1, "NoParse",
        itemExtAbil: "攻击下限:no"));
    Equal(NativeType2StdItemAppendStatus.SequenceRejected,
        wrongIndex.Status, "idx checked before extension parsing");
}

static void VerifyDerivedFieldAndScriptBoundaries()
{
    var needResolver = new FixedNeedIdentifyResolver(true, 7);
    var binder = new FixedScriptBinder(
        NativeType2StdItemScriptBindingResult.CompileFailed(
            @"D:\Game\PsItemScript\Scripted.pas", "line 9"));
    var catalog = new NativeType2StdItemRuntimeCatalog();
    var result = new NativeType2StdItemAppendTransaction(catalog,
        needResolver, binder).Apply(Packet(0, "Scripted"));

    Equal(NativeType2StdItemNeedIdentifyStatus.Resolved,
        result.Entry.NeedIdentifyStatus, "NeedIdentify resolved state");
    Equal((byte)7, result.Entry.NeedIdentify, "NeedIdentify value");
    Equal(@"PsItemScript\Scripted.pas",
        Gbk(binder.RelativePathBytes), "binder relative path bytes");
    Equal(NativeType2StdItemScriptBindingStatus.CompileFailed,
        result.Entry.ScriptBinding.Status, "script failure state");
    Equal(1, catalog.Count, "script failure occurs after append");
    SequenceEqual(new[]
    {
        @"[ERROR]: 致命错误 D:\Game\PsItemScript\Scripted.pas物品脚本错误:line 9",
        "运行期添加道具:Scripted"
    }, result.Logs, "script failure exact logs");

    var noMatch = new NativeType2StdItemAppendTransaction(
        new NativeType2StdItemRuntimeCatalog(),
        new FixedNeedIdentifyResolver(false, 99)).Apply(
        Packet(0, "NoMatch"));
    Equal(NativeType2StdItemNeedIdentifyStatus.NotMatched,
        noMatch.Entry.NeedIdentifyStatus, "NeedIdentify not matched state");
    Equal((byte)0, noMatch.Entry.NeedIdentify,
        "NeedIdentify not matched native zero");

    var missingScript = new NativeType2StdItemAppendTransaction(
        new NativeType2StdItemRuntimeCatalog(), scriptBinder:
        new FixedScriptBinder(
            NativeType2StdItemScriptBindingResult.FileNotFound(
                @"D:\Game\PsItemScript\Missing.pas"))).Apply(
        Packet(0, "Missing"));
    Equal(NativeType2StdItemScriptBindingStatus.FileNotFound,
        missingScript.Entry.ScriptBinding.Status,
        "missing script explicit without invented error log");
    SequenceEqual(new[] { "运行期添加道具:Missing" },
        missingScript.Logs, "missing script has no native error log");
}

static void VerifyCorrelationPermissionBoundary()
{
    var eligible = new NativeType2StdItemAppendTransaction(
        new NativeType2StdItemRuntimeCatalog(), correlationResolver:
        new FixedCorrelationResolver(true, 4)).Apply(
        Packet(1, "AdminItem", correlation: -17));
    Equal(NativeType2StdItemAppendStatus.SequenceRejected,
        eligible.Status, "prompt independent of append failure");
    Equal(NativeType2StdItemCorrelationStatus.PromptEligible,
        eligible.CorrelationDecision.Status, "permission 4 eligible");
    Equal(-17, eligible.CorrelationDecision.Correlation,
        "negative opaque correlation preserved");
    Equal("运行期成功添加道具:AdminItem",
        eligible.CorrelationDecision.Prompt, "exact admin prompt");

    var denied = new NativeType2StdItemAppendTransaction(
        new NativeType2StdItemRuntimeCatalog(), correlationResolver:
        new FixedCorrelationResolver(true, 3)).Apply(
        Packet(0, "Denied", correlation: 10));
    Equal(NativeType2StdItemCorrelationStatus.InsufficientPermission,
        denied.CorrelationDecision.Status, "permission 3 denied");
    Equal(string.Empty, denied.CorrelationDecision.Prompt,
        "denied prompt absent");

    var missing = new NativeType2StdItemAppendTransaction(
        new NativeType2StdItemRuntimeCatalog(), correlationResolver:
        new FixedCorrelationResolver(false, 9)).Apply(
        Packet(0, "MissingActor", correlation: 11));
    Equal(NativeType2StdItemCorrelationStatus.TargetNotFound,
        missing.CorrelationDecision.Status, "correlation target missing");

    var unavailable = new NativeType2StdItemAppendTransaction(
        new NativeType2StdItemRuntimeCatalog()).Apply(
        Packet(0, "NoResolver", correlation: 12));
    Equal(NativeType2StdItemCorrelationStatus.ResolverUnavailable,
        unavailable.CorrelationDecision.Status,
        "correlation resolver unavailable explicit");

    var none = new NativeType2StdItemAppendTransaction(
        new NativeType2StdItemRuntimeCatalog()).Apply(
        Packet(0, "NoToken", correlation: 0));
    Equal(NativeType2StdItemCorrelationStatus.NotRequested,
        none.CorrelationDecision.Status, "zero correlation ignored");
}

static byte[] Packet(ushort index, string name, string itemExtAbil = "",
    int correlation = 0, ushort word2 = 0, int param2 = 0)
{
    var packet = new byte[NativeType2StdItemRuntimeProtocol.PacketSize];
    BinaryPrimitives.WriteUInt16LittleEndian(packet,
        NativeType2StdItemRuntimeProtocol.Command);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), word2);
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4),
        correlation);
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), param2);
    var body = Body(packet);
    WriteUInt16(body, 0, index);
    WriteShortString(body, 0x04, 15, name);
    WriteShortString(body, 0x5C, 200, itemExtAbil);
    return packet;
}

static Span<byte> Body(byte[] packet) => packet.AsSpan(
    NativeType2StdItemRuntimeProtocol.HeaderSize,
    NativeType2StdItemRuntimeProtocol.BodySize);

static void WriteShortString(Span<byte> destination, int offset,
    int capacity, string value)
{
    var bytes = GbkBytes(value ?? string.Empty);
    if (bytes.Length > capacity)
        throw new InvalidOperationException("fixture short string overflow");
    destination[offset] = checked((byte)bytes.Length);
    bytes.CopyTo(destination.Slice(offset + 1));
}

static void WriteUInt16(Span<byte> destination, int offset, int value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2),
        unchecked((ushort)value));

static void WriteInt32(Span<byte> destination, int offset, int value) =>
    BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4),
        value);

static byte[] GbkBytes(string value) => Encoding.GetEncoding(936)
    .GetBytes(value);

static string Gbk(byte[] value) => Encoding.GetEncoding(936)
    .GetString(value);

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
    }
}

static void SequenceEqual<T>(IEnumerable<T> expected,
    IEnumerable<T> actual, string description)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected [{string.Join(",", expected)}], " +
            $"actual [{string.Join(",", actual)}]");
    }
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

sealed class FixedNeedIdentifyResolver :
    INativeType2StdItemNeedIdentifyResolver
{
    private readonly bool _found;
    private readonly byte _value;

    public FixedNeedIdentifyResolver(bool found, byte value)
    {
        _found = found;
        _value = value;
    }

    public bool TryResolve(ReadOnlyMemory<byte> nativeNameBytes,
        out byte needIdentify)
    {
        needIdentify = _value;
        return _found;
    }
}

sealed class FixedScriptBinder : INativeType2StdItemScriptBinder
{
    private readonly NativeType2StdItemScriptBindingResult _result;

    public FixedScriptBinder(NativeType2StdItemScriptBindingResult result) =>
        _result = result;

    public byte[] RelativePathBytes { get; private set; }

    public NativeType2StdItemScriptBindingResult Bind(
        NativeType2StdItemDefinition definition,
        ReadOnlyMemory<byte> relativePathBytes)
    {
        RelativePathBytes = relativePathBytes.ToArray();
        return _result;
    }
}

sealed class FixedCorrelationResolver :
    INativeType2StdItemCorrelationResolver
{
    private readonly bool _found;
    private readonly byte _permission;

    public FixedCorrelationResolver(bool found, byte permission)
    {
        _found = found;
        _permission = permission;
    }

    public bool TryResolvePermission(int correlation,
        out byte permissionLevel)
    {
        permissionLevel = _permission;
        return _found;
    }
}
