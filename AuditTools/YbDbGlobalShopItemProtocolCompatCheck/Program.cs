using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
TestBatchAbi();
TestNativeGates();
TestRecordDecisions();
TestSpecialItemNames();
TestCompletion();
TestDormantBoundary();

Console.WriteLine(
    "YbDbGlobalShopItemProtocolCompatCheck PASS response=1101 " +
    "batch=1800/10x180 categories=0..7 end=10000 gate=4497 " +
    "request=unproven runtime=closed");
return;

static void TestBatchAbi()
{
    var payload = new byte[YbDbGlobalShopItemProtocol.BatchPayloadSize];
    WriteRecord(payload, 0, "气血石(小)", "补给", 123, 6, 300, 250,
        7, 8, 9, 0x1234ABCD, "测试说明");
    WriteRecord(payload, 1, "普通木剑", "武器", 321, 2, 500, 450,
        10, 11, 12, 0x56789ABC, "第二件");
    // Record 2 remains zero; native parsing must stop and ignore later slots.
    WriteRecord(payload, 3, "不得解析", "错误", 1, 1, 1, 1,
        1, 1, 1, 1, "尾部");

    var frame = new YbDbLegacy77Frame(6, -123456,
        YbDbGlobalShopItemProtocol.ResponseIdent, payload);
    Assert(YbDbGlobalShopItemProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    Equal(YbDbGlobalShopItemProtocol.ResponseKind.CategoryBatch,
        response.Kind, "response kind");
    Equal(6, response.QueryId, "category QueryId");
    Equal(-123456, response.IgnoredHeaderParam, "ignored Param");
    Equal(1800, response.IgnoredPayloadLength, "payload length");
    Equal(2, response.Records.Length, "early zero terminator");

    var item = response.Records[0];
    Equal("气血石(小)", item.Name, "name");
    Equal("补给", item.TypeName, "type name");
    Equal((ushort)123, item.Looks, "looks");
    Equal((ushort)6, item.Category, "wire category");
    Equal((ushort)300, item.Price, "price");
    Equal((ushort)250, item.CurrentPrice, "current price");
    Equal((ushort)7, item.Type, "type");
    Equal((ushort)8, item.Count, "count");
    Equal((ushort)9, item.EffectCount, "effect count");
    Equal(0x1234ABCDu, item.EffectOffset, "effect offset");
    Equal("测试说明", item.Description, "description");
    Equal(180, item.Raw.Length, "raw record length");
    Assert(!response.UsesRoleOrSessionRouting, "1101 must not route by role/session");
    Assert(!response.CreatesPendingRequest, "1101 must not create pending request");

    Assert(YbDbLegacy77Codec.TryEncode(frame, out var wire, out error), error);
    Equal(1816, wire.Length, "wire length");
    Equal((ushort)1101,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12, 2)),
        "wire Ident");
    Equal((ushort)1800,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(14, 2)),
        "wire BodyLength");
}

static void TestNativeGates()
{
    foreach (var category in new[] { 0, 7 })
    {
        Assert(YbDbGlobalShopItemProtocol.TryDecodeResponse(
            new YbDbLegacy77Frame(category, int.MaxValue, 1101,
                new byte[1800]), out _, out var error), error);
    }

    foreach (var category in new[] { -1, 8, 9999, 10001 })
        Reject(new YbDbLegacy77Frame(category, 0, 1101, new byte[1800]),
            "invalid category " + category);
    foreach (var size in new[] { 0, 1799, 1801 })
        Reject(new YbDbLegacy77Frame(0, 0, 1101, new byte[size]),
            "invalid normal body length " + size);
    Reject(new YbDbLegacy77Frame(0, 0, 1100, new byte[1800]),
        "wrong Ident");
    Reject(null, "null frame");

    foreach (var size in new[] { 0, 1, 1800 })
    {
        Assert(YbDbGlobalShopItemProtocol.TryDecodeResponse(
            new YbDbLegacy77Frame(10000, int.MinValue, 1101,
                new byte[size]), out var end, out var error), error);
        Equal(YbDbGlobalShopItemProtocol.ResponseKind.EndMarker,
            end.Kind, "end kind");
        Equal(size, end.IgnoredPayloadLength,
            "native end marker ignores BodyLength");
        Equal(int.MinValue, end.IgnoredHeaderParam,
            "native end marker ignores Param");
    }
}

static void TestRecordDecisions()
{
    var item = DecodeSingle("气血石(小)", 0x5678ABCD, 345);
    var canonical = YbDbGlobalShopItemProtocol.EvaluateRecord(item,
        4, 20, 3, true, 777);
    Assert(canonical.Append, "canonical record should append");
    Assert(!canonical.LogMissingStandardItem, "canonical missing log");
    Assert(!canonical.LogCapacityOverflow, "canonical overflow log");
    Equal((ushort)777, canonical.ResolvedLooks, "canonical Looks");
    Equal((ushort)777, ReadWord(canonical.PatchedRecord, 32),
        "patched canonical Looks");
    Equal((ushort)4, ReadWord(canonical.PatchedRecord, 34),
        "patched category");
    Equal("气血石(小)", canonical.Companion.Name, "companion name");
    Assert(canonical.Companion.SpecialItem, "companion special flag");
    Equal(345u, canonical.Companion.CurrentPrice, "companion current price");
    Assert(!canonical.MutatesRuntime, "pure decision mutated runtime");
    Assert(!canonical.SendsClientMessageOrAck,
        "record decision must not send client messages or ACKs");
    Assert(!canonical.MutatesPlayerAccountInventoryOrDatabase,
        "record decision must not mutate player/account/inventory/database");
    Assert(!canonical.WritesBusinessGameLog,
        "record decision must not write a business game log");

    var fallback = YbDbGlobalShopItemProtocol.EvaluateRecord(item,
        2, 5, 4, false, 999);
    Assert(fallback.Append, "fallback record should append");
    Assert(fallback.LogMissingStandardItem, "missing-item console log");
    Equal((ushort)0xABCD, fallback.ResolvedLooks, "fallback low WORD Looks");
    Equal((ushort)0xABCD, ReadWord(fallback.PatchedRecord, 32),
        "patched fallback Looks");

    foreach (var pair in new[] { (Capacity: 4, Count: 4), (Capacity: -1, Count: 0) })
    {
        var overflow = YbDbGlobalShopItemProtocol.EvaluateRecord(item,
            1, pair.Capacity, pair.Count, false, 0);
        Assert(!overflow.Append, "capacity <= count must overflow");
        Assert(overflow.LogMissingStandardItem,
            "lookup failure occurs before overflow");
        Assert(overflow.LogCapacityOverflow, "overflow console log");
        Assert(overflow.Companion == null, "overflow must not create companion");
        Assert(overflow.PatchedRecord.All(value => value == 0),
            "overflow must zero the entire source record");
    }
}

static void TestSpecialItemNames()
{
    var names = new[]
    {
        "气血石(小)", "气血石(中)", "气血石(大)",
        "幻魔石(小)", "幻魔石(中)", "幻魔石(大)",
        "比奇传送石", "魔血石(大)", "双倍秘籍", "双倍宝典",
        "双倍卷轴", "修复神水"
    };
    foreach (var name in names)
        Assert(YbDbGlobalShopItemProtocol.IsNativeSpecialItemName(name),
            "missing native special name: " + name);
    Assert(!YbDbGlobalShopItemProtocol.IsNativeSpecialItemName("气血石（小）"),
        "full-width punctuation must not match");
    Assert(!YbDbGlobalShopItemProtocol.IsNativeSpecialItemName("气血石(小) "),
        "trailing space must not match");
    Assert(!YbDbGlobalShopItemProtocol.IsNativeSpecialItemName(null),
        "null must not match");
}

static void TestCompletion()
{
    Assert(YbDbGlobalShopItemProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(10000, 456, 1101, new byte[3]),
        out var end, out var error), error);
    var completion = YbDbGlobalShopItemProtocol.EvaluateCompletion(end);
    Assert(completion.EmitGateBroadcast, "end marker gate broadcast");
    Equal((ushort)0x1191, completion.GateCommand, "gate command");
    Assert(!completion.SendsClient812Or815, "1101 must not directly send 812/815");
    Assert(!completion.SendsPurchase816, "1101 must not send purchase 816");
    Assert(!completion.SendsAck, "1101 must not ACK");
    Assert(!completion.MutatesDatabaseAccountOrInventory,
        "1101 must not mutate DB/account/inventory");
    Assert(!completion.WritesBusinessGameLog,
        "1101 completion must not write a business game log");

    var batch = DecodeBatch();
    Assert(!YbDbGlobalShopItemProtocol.EvaluateCompletion(batch)
        .EmitGateBroadcast, "normal batch must not emit completion");
}

static void TestDormantBoundary()
{
    Assert(typeof(YbDbGlobalShopItemProtocol).GetField("RequestIdent",
        BindingFlags.Public | BindingFlags.Static) == null,
        "unproven request Ident was guessed");

    var root = FindRepositoryRoot();
    var protocol = File.ReadAllText(Path.Combine(root, "SystemModule", "Packet",
        "YbDbGlobalShopItemProtocol.cs"));
    var client = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "YbDbClient.cs"));
    RejectText(protocol, "TPlayObject", "dormant codec gained player dependency");
    RejectText(protocol, "SendMsg(", "dormant codec gained client messaging");
    RejectText(protocol, "RequestIdent", "codec guessed a matching request");
    RejectText(protocol, "YBGoods_Buy_Log", "codec gained purchase logging");
    RejectText(client, "YbDbGlobalShopItemProtocol.ResponseIdent",
        "1101 runtime dispatch was opened");
    RejectText(client, "RequestGlobalShop", "unproven shop request sender was opened");
    Assert(!System.Text.RegularExpressions.Regex.IsMatch(client,
            @"(?:frame|queued\.Frame)\.Ident\s*==?\s*1101"),
        "1101 literal runtime dispatch was opened");
}

static YbDbGlobalShopItemProtocol.Response DecodeBatch()
{
    Assert(YbDbGlobalShopItemProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, 0, 1101, new byte[1800]),
        out var response, out var error), error);
    return response;
}

static YbDbGlobalShopItemProtocol.ItemRecord DecodeSingle(
    string name, uint effectOffset, ushort currentPrice)
{
    var payload = new byte[1800];
    WriteRecord(payload, 0, name, "分类", 1, 0, 2, currentPrice,
        3, 4, 5, effectOffset, "说明");
    Assert(YbDbGlobalShopItemProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, 0, 1101, payload),
        out var response, out var error), error);
    Equal(1, response.Records.Length, "single record count");
    return response.Records[0];
}

static void WriteRecord(byte[] payload, int index, string name,
    string typeName, ushort looks, ushort category, ushort price,
    ushort currentPrice, ushort type, ushort count, ushort effectCount,
    uint effectOffset, string description)
{
    var offset = index * YbDbGlobalShopItemProtocol.RecordSize;
    WriteShortString(payload, offset, 15, name);
    WriteShortString(payload, offset + 16, 15, typeName);
    WriteWord(payload, offset + 32, looks);
    WriteWord(payload, offset + 34, category);
    WriteWord(payload, offset + 36, price);
    WriteWord(payload, offset + 38, currentPrice);
    WriteWord(payload, offset + 40, type);
    WriteWord(payload, offset + 42, count);
    WriteWord(payload, offset + 44, effectCount);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 48, 4),
        effectOffset);
    WriteShortString(payload, offset + 52, 127, description);
}

static void WriteShortString(byte[] payload, int offset, int capacity,
    string value)
{
    var bytes = Encoding.GetEncoding(936).GetBytes(value ?? string.Empty);
    Assert(bytes.Length <= capacity, "test ShortString exceeds native slot");
    payload[offset] = (byte)bytes.Length;
    bytes.CopyTo(payload, offset + 1);
}

static void WriteWord(byte[] payload, int offset, ushort value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset, 2), value);

static ushort ReadWord(byte[] payload, int offset) =>
    BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset, 2));

static void Reject(YbDbLegacy77Frame frame, string context)
{
    Assert(!YbDbGlobalShopItemProtocol.TryDecodeResponse(frame,
        out _, out var error), context + " unexpectedly decoded");
    Assert(!string.IsNullOrWhiteSpace(error), context + " missing error");
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SystemModule",
                    "SystemModule.csproj"))
                && File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("repository root not found");
}

static void RejectText(string text, string value, string message) =>
    Assert(text.IndexOf(value, StringComparison.Ordinal) < 0, message);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string context)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{context}: expected={expected}, actual={actual}");
}
