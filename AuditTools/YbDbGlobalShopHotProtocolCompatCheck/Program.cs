using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
TestResponseAbiAndNativeGates();
TestCachePatchAndWholeBodyCopy();
TestEmptyCache();
TestDormantBoundary();

Console.WriteLine(
    "YbDbGlobalShopHotProtocolCompatCheck PASS response=1102/900 " +
    "records=5x180 page=10 client=815-later completion-broadcast=none " +
    "request=unproven runtime=closed");
return;

static void TestResponseAbiAndNativeGates()
{
    var payload = BuildPayload();
    var frame = new YbDbLegacy77Frame(int.MinValue, int.MaxValue,
        YbDbGlobalShopHotProtocol.ResponseIdent, payload);
    Assert(YbDbGlobalShopHotProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    Equal(int.MinValue, response.IgnoredQueryId, "ignored QueryId");
    Equal(int.MaxValue, response.IgnoredHeaderParam, "ignored Param");
    Equal(2, response.ActivePrefixCount, "active prefix count");
    Equal(5, response.Records.Length, "all fixed records decoded");
    Equal(900, response.RawPayload.Length, "raw payload length");
    Equal("热销一", response.Records[0].Name, "record zero name");
    Equal("热销二", response.Records[1].Name, "record one name");
    Equal(string.Empty, response.Records[2].Name, "zero terminator name");
    Equal("空槽后仍保留", response.Records[3].Name,
        "trailing record remains inspectable");
    Assert(!response.UsesRoleOrSessionRouting,
        "1102 must not route by role/session");
    Assert(!response.CreatesPendingRequest,
        "1102 must not create pending request");

    Assert(YbDbLegacy77Codec.TryEncode(frame, out var wire, out error), error);
    Equal(916, wire.Length, "wire length");
    Equal((ushort)1102,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12, 2)),
        "wire Ident");
    Equal((ushort)900,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(14, 2)),
        "wire BodyLength");

    foreach (var size in new[] { 0, 899, 901, 1800 })
        Reject(new YbDbLegacy77Frame(0, 0, 1102, new byte[size]),
            "invalid body length " + size);
    Reject(new YbDbLegacy77Frame(0, 0, 1101, new byte[900]),
        "wrong Ident");
    Reject(null, "null frame");
}

static void TestCachePatchAndWholeBodyCopy()
{
    var response = Decode(BuildPayload());
    var resolutions = new[]
    {
        new YbDbGlobalShopHotProtocol.StandardItemResolution(true, 777),
        new YbDbGlobalShopHotProtocol.StandardItemResolution(false, 999)
    };
    Assert(YbDbGlobalShopHotProtocol.TryEvaluateCache(response,
        resolutions, out var decision, out var error), error);

    Equal(2, decision.LoadedRecordCount, "loaded record count");
    Equal("==> 加载商城热销榜:2", decision.ConsoleMessage,
        "console message");
    Equal(900, decision.PatchedPayload.Length, "cache copy length");
    Equal((ushort)777, ReadWord(decision.PatchedPayload, 32),
        "found item canonical Looks");
    Equal((ushort)10, ReadWord(decision.PatchedPayload, 34),
        "found item hot page");
    Equal((ushort)222, ReadWord(decision.PatchedPayload, 180 + 32),
        "missing item keeps original Looks");
    Equal((ushort)10, ReadWord(decision.PatchedPayload, 180 + 34),
        "missing item hot page");

    Equal((ushort)333, ReadWord(decision.PatchedPayload, 3 * 180 + 32),
        "record after zero keeps Looks");
    Equal((ushort)6, ReadWord(decision.PatchedPayload, 3 * 180 + 34),
        "record after zero is not patched to page ten");
    Assert(decision.PatchedPayload.AsSpan(2 * 180, 180)
            .SequenceEqual(response.RawPayload.AsSpan(2 * 180, 180)),
        "zero terminator record must be copied unchanged");
    Assert(decision.PatchedPayload.AsSpan(3 * 180, 2 * 180)
            .SequenceEqual(response.RawPayload.AsSpan(3 * 180, 2 * 180)),
        "entire trailing body must be copied unchanged");

    Assert(decision.AllocateCacheIfMissing, "cache allocation decision");
    Assert(decision.OverwriteEntireCache, "whole-cache overwrite decision");
    Assert(!decision.CreatesSpecialItemCompanions,
        "1102 must not create 1101 special companion entries");
    Assert(!decision.EmitsCompletionBroadcast,
        "1102 must not emit the 1101 completion broadcast");
    Assert(!decision.SendsClient815Directly,
        "1102 handler must not directly send client 815");
    Assert(!decision.SendsAck, "1102 must not ACK");
    Assert(!decision.MutatesPlayerAccountInventoryOrDatabase,
        "1102 handler must not mutate player/account/inventory/database");
    Assert(!decision.WritesBusinessGameLog,
        "1102 handler must not write business game log");
    Assert(!decision.MutatesRuntime, "dormant decision mutated runtime");

    Assert(!YbDbGlobalShopHotProtocol.TryEvaluateCache(response,
            resolutions.Take(1).ToArray(), out _, out error),
        "resolution count mismatch unexpectedly succeeded");
    Assert(!string.IsNullOrWhiteSpace(error),
        "resolution mismatch missing error");
}

static void TestEmptyCache()
{
    var response = Decode(new byte[900]);
    Equal(0, response.ActivePrefixCount, "empty active prefix");
    Assert(YbDbGlobalShopHotProtocol.TryEvaluateCache(response,
        Array.Empty<YbDbGlobalShopHotProtocol.StandardItemResolution>(),
        out var decision, out var error), error);
    Equal("==> 加载商城热销榜:0", decision.ConsoleMessage,
        "empty console message");
    Assert(decision.PatchedPayload.All(value => value == 0),
        "empty 900-byte cache changed");
    Assert(decision.AllocateCacheIfMissing,
        "native allocates cache even for an all-zero valid body");
}

static void TestDormantBoundary()
{
    Assert(typeof(YbDbGlobalShopHotProtocol).GetField("RequestIdent",
        BindingFlags.Public | BindingFlags.Static) == null,
        "unproven request Ident was guessed");

    var root = FindRepositoryRoot();
    var protocol = File.ReadAllText(Path.Combine(root, "SystemModule", "Packet",
        "YbDbGlobalShopHotProtocol.cs"));
    var client = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "YbDbClient.cs"));
    RejectText(protocol, "RequestIdent", "codec guessed a matching request");
    RejectText(protocol, "TPlayObject", "dormant codec gained player dependency");
    RejectText(protocol, "MallManager", "dormant codec gained live cache dependency");
    RejectText(protocol, "SendMsg(", "dormant codec gained client messaging");
    RejectText(protocol, "IsNativeSpecialItemName",
        "1102 incorrectly gained 1101 special-name companion logic");
    RejectText(client, "YbDbGlobalShopHotProtocol.ResponseIdent",
        "1102 runtime dispatch was opened");
    RejectText(client, "RequestGlobalShopHot",
        "unproven hot-shop request sender was opened");
    Assert(!Regex.IsMatch(client,
            @"(?:frame|queued\.Frame)\.Ident\s*==?\s*1102"),
        "1102 literal runtime dispatch was opened");
}

static byte[] BuildPayload()
{
    var payload = new byte[900];
    WriteRecord(payload, 0, "热销一", 111, 4, 100, 90, 0xAAAA5555);
    WriteRecord(payload, 1, "热销二", 222, 5, 200, 180, 0x1234ABCD);
    WriteRecord(payload, 3, "空槽后仍保留", 333, 6, 300, 270, 0x56781234);
    WriteRecord(payload, 4, "最后一槽", 444, 7, 400, 360, 0x99998888);
    return payload;
}

static YbDbGlobalShopHotProtocol.Response Decode(byte[] payload)
{
    Assert(YbDbGlobalShopHotProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(123, -456, 1102, payload),
        out var response, out var error), error);
    return response;
}

static void WriteRecord(byte[] payload, int index, string name,
    ushort looks, ushort page, ushort price, ushort currentPrice,
    uint effectOffset)
{
    var offset = index * 180;
    WriteShortString(payload, offset, 15, name);
    WriteShortString(payload, offset + 16, 15, "热销");
    WriteWord(payload, offset + 32, looks);
    WriteWord(payload, offset + 34, page);
    WriteWord(payload, offset + 36, price);
    WriteWord(payload, offset + 38, currentPrice);
    WriteWord(payload, offset + 40, 1);
    WriteWord(payload, offset + 42, 2);
    WriteWord(payload, offset + 44, 3);
    BinaryPrimitives.WriteUInt32LittleEndian(
        payload.AsSpan(offset + 48, 4), effectOffset);
    WriteShortString(payload, offset + 52, 127, "热销说明");
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
    Assert(!YbDbGlobalShopHotProtocol.TryDecodeResponse(frame,
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
