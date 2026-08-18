using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Text;
using System.Collections.Concurrent;
using DBSvr;
using DBSvr.Core;
using GameGate.Core;
using GameGate.Models;
using SystemModule;
using SystemModule.Packet;
using SystemModule.Sockets;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var failures = new List<string>();
var skipped = new List<string>();

await Run("44FF44 garbage + partial frame", TestFrame44);
await Run("44FF44 bounded stream buffer", TestFrame44StreamParser);
await Run("mobile frame native header + large payload", TestMobileCodec);
await Run("percent-dollar split/coalesced frames", TestPercentDollar);
await Run("77BBAA33 split/coalesced/compact ACK", TestInternalPacket77Frames);
await Run("request-server split/coalesced frames", TestRequestServerFrames);
await Run("sparse character protobuf transport", TestSparseCharacterTransport);
await Run("legacy 13-slot equipment protobuf upgrade", TestLegacyEquipmentTransport);
await Run("native latin1_bin GBK name codec", TestLegacyGbkText);
await Run("latin1_bin name caches preserve case", TestCaseSensitiveNameCaches);
await Run("native human 16-slot equipment + eye sidecar + bind", TestNativeHumanBind);
await Run("5600 account session replacement + IP admission", TestSessionReplacement);
await Run("5600 private admission requires an M2 peer", TestPrivateAdmissionDelivery);
await Run("5600 native client registration + auth + probe", TestNativeLoginGateClientMode);
await Run("GameGate ignored native type17 runtime", TestIgnoredType17Runtime);
await Run("GameGate DB reconnect invalidates clients", TestSharedBackendDbReconnect);
await Run("GameGate shared backend multiplexing", TestSharedBackendHub);
await Run("GameGate pooled session release", TestSessionPoolRelease);
await Run("GameGate delayed queue byte bound", TestDelayQueueBound);
await Run("IClientScoket disconnect + reconnect", TestClientSocketReconnect);
await Run("ISocketServer shutdown + restart", TestServerSocketRestart);
await Run("GameSvr send queue complete writes", TestGameSendQueue);
await Run("DBSvr rejects unproven GDM commands", TestUnsupportedGameSocCommands);
await Run("native hero 160 index selection", TestNativeHeroSelection);

// ---------------------------------------------------------------------------
// 以下四项是本程序里**唯一**接触真 MySQL 和活原版 DBServer 存档往返的测试。
// 其余全部是进程内自测（C# 编码器喂 C# 解码器），只能证明"与自己自洽"，
// 不能证明"与原版字节等价"。
//
// 它们此前写成「缺 CLI 标志就不注册」，于是不带标志运行时 failures 为空，
// 程序直接打印 "All DB/gate regression checks passed." 并 return 0 ——
// **没有 SKIP 行、输出无任何痕迹**，2412 行审计静默退化成进程内自测却宣称全通过。
// 全仓 grep 这四个标志名，除本文件与一份救援副本外零引用，即它们很可能从未跑过。
//
// 判据（本项目教训）：**断言数不涨 = 根本没跑**；SKIP-ANCHOR 必须当红灯。
// 故改为：缺标志时显式登记 SKIP，并在收尾用**可区分的退出码**报告，
// 让"没跑"在任何情况下都不能伪装成"通过"。
// ---------------------------------------------------------------------------
async Task RunGated(string name, string flag, Func<string, Task> body)
{
    var at = Array.FindIndex(args, value => value == flag);
    if (at >= 0 && at + 1 < args.Length)
    {
        await Run(name, () => body(args[at + 1]));
        return;
    }
    skipped.Add($"{name}  (needs {flag} <value>)");
    Console.WriteLine($"SKIP {name}: no {flag}");
}

async Task RunGatedPort(string name, string flag, Func<int, Task> body)
    => await RunGated(name, flag, raw =>
    {
        if (!int.TryParse(raw, out var port))
            throw new Exception($"{flag} expects an integer port, got '{raw}'");
        return body(port);
    });

await RunGated("live DB canonical character index (read-only)",
    "--db-ini", ini => TestDatabase(ini));
await RunGatedPort("native Gs1 character records (read-only)",
    "--native-port", TestNativeDatabase);
await RunGatedPort("native character save/reload preservation",
    "--native-write-port", TestNativeWritableDatabase);
await RunGatedPort("native startup cleanup boundary",
    "--native-cleanup-port", TestNativeCleanup);

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

if (skipped.Count > 0)
{
    // 退出码 2 ≠ 0（通过）且 ≠ 1（失败）。按退出码判绿的流水线不会再把
    // "根本没跑"读成"通过"，同时也不会与真实断言失败混淆。
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        $"INCOMPLETE: {failures.Count} failed, {skipped.Count} of the 4 live-DB / "
        + "native-round-trip checks never ran:");
    foreach (var name in skipped)
        Console.Error.WriteLine("  - " + name);
    Console.Error.WriteLine(
        "These are the only checks that touch a real MySQL and a live original "
        + "DBServer save/reload round trip. Everything else is in-process self-test, "
        + "so a run without them does NOT establish byte equivalence with the original.");
    return 2;
}

Console.WriteLine(
    "All DB/gate regression checks passed (including all 4 live-DB / "
    + "native-round-trip checks).");
return 0;

async Task Run(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures.Add("FAIL " + name + ": " + ex.Message);
    }
}

static Task TestFrame44()
{
    var frame = new Frame44FF44(0x13, 0xFA, 7, new byte[] { 1, 2, 3, 4 }).ToBytes();
    Equal((byte)0xFA, frame[4], "wire flags");
    Equal((byte)0x13, frame[5], "wire command");
    Equal((ushort)4, BitConverter.ToUInt16(frame, 6), "wire data length");
    var input = new byte[3 + frame.Length];
    input[0] = 0x11; input[1] = 0x22; input[2] = 0x33;
    Buffer.BlockCopy(frame, 0, input, 3, frame.Length);
    var parsed = Frame44FF44.ScanAll(input, 0, input.Length, out var consumed);
    Equal(1, parsed.Count, "full frame count");
    Equal((byte)0x13, parsed[0].Cmd, "parsed command");
    Equal((byte)0xFA, parsed[0].Flag, "parsed flags");
    Equal((ushort)0x13FA, parsed[0].Marker, "parsed packed marker");
    Equal(input.Length, consumed, "garbage-aware consumed length");

    var partial = new byte[3 + frame.Length / 2];
    Buffer.BlockCopy(input, 0, partial, 0, partial.Length);
    parsed = Frame44FF44.ScanAll(partial, 0, partial.Length, out consumed);
    Equal(0, parsed.Count, "partial frame count");
    Equal(3, consumed, "partial frame keeps marker but drops prefix");
    return Task.CompletedTask;
}

static Task TestFrame44StreamParser()
{
    var body = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();
    var large = new Frame44FF44(0x17, 0, 10, body).ToBytes();
    var small = new Frame44FF44(0x19, 0xFA, 11).ToBytes();
    var input = new byte[] { 1, 2, 3 }.Concat(large).Concat(small).ToArray();
    var parser = new Frame44FF44StreamParser();
    Check(parser.TryAppend(input, 0, 9, out var first, out var error), error);
    Equal(0, first.Count, "stream partial frame count");
    Check(parser.TryAppend(input, 9, input.Length - 9, out var frames, out error), error);
    Equal(2, frames.Count, "stream coalesced frame count");
    Check(frames[0].Payload.SequenceEqual(body), "stream large payload");
    Equal(0, parser.BufferedLength, "stream parser drained");
    Check(parser.BufferCapacity <= 2048, "stream parser shrinks after drain");

    var bounded = new Frame44FF44StreamParser(64);
    Check(!bounded.TryAppend(new byte[65], 0, 65, out _, out error),
        "stream parser rejects bounded overflow");
    Equal(0, bounded.BufferedLength, "stream parser resets after overflow");
    return Task.CompletedTask;
}

static Task TestMobileCodec()
{
    var inner = new MobileCodec.InnerHeader
    {
        Recog = 1234,
        Ident = 4017,
        Param = 2,
        Tag = 3,
        Series = 4
    };
    var body = Enumerable.Range(0, 600).Select(i => (byte)i).ToArray();
    var frame = MobileCodec.WriteFrame(inner, body, 0x11223344, MobileCodec.MARKER_DATA);
    Equal((byte)0, frame[4], "mobile flags");
    Equal((byte)0x17, frame[5], "mobile command");
    Equal((ushort)(MobileCodec.INNER_SIZE + body.Length),
        BitConverter.ToUInt16(frame, 6), "mobile data length");
    Equal((uint)0x11223344, BitConverter.ToUInt32(frame, 8), "mobile identifier");

    var prefixed = new byte[] { 0x01, 0x02, 0x03 }.Concat(frame).ToArray();
    Check(MobileCodec.TryReadFrame(prefixed, 0, prefixed.Length, out var parsed,
        out var consumed), "large mobile frame parse");
    Equal(prefixed.Length, consumed, "large mobile frame consumed");
    Equal((byte)0x17, parsed.Header.Cmd, "mobile parsed command");
    Equal((ushort)(MobileCodec.INNER_SIZE + body.Length), parsed.Header.DataLen,
        "mobile parsed length");
    Equal(inner.Ident, parsed.Inner.Ident, "mobile inner ident");
    Check(parsed.Body.SequenceEqual(body), "mobile body roundtrip");

    Check(!MobileCodec.TryReadFrame(prefixed, 0, prefixed.Length - 10, out _,
        out consumed), "partial mobile frame rejected");
    Equal(3, consumed, "partial mobile frame drops only garbage prefix");
    return Task.CompletedTask;
}

static Task TestPercentDollar()
{
    var parser = new PercentDollarFrameParser(256);
    var all = Encoding.ASCII.GetBytes("%O1/a/a$%A1/#abc!$%X1$");
    Check(parser.TryAppend(all, 0, 5, out var first, out var error), error);
    Equal(0, first.Count, "first fragment");
    Check(parser.TryAppend(all, 5, all.Length - 5, out var rest, out error), error);
    Equal(3, rest.Count, "coalesced frame count");
    Equal("%X1$", Encoding.ASCII.GetString(rest[2]), "last frame");

    var oversized = new byte[257];
    Array.Fill(oversized, (byte)'A');
    oversized[0] = (byte)'%';
    oversized[^1] = (byte)'$';
    Check(!parser.TryAppend(oversized, 0, oversized.Length, out _, out error),
        "oversized percent-dollar frame rejected");
    Equal(0, parser.BufferedLength, "oversized percent-dollar frame resets parser");

    const int maximumFrameLength = 64 * 1024;
    var maximumFrame = new byte[maximumFrameLength];
    Array.Fill(maximumFrame, (byte)'A');
    maximumFrame[0] = (byte)'%';
    maximumFrame[^1] = (byte)'$';
    var trailing = Encoding.ASCII.GetBytes("%X2$");
    var maximumThenTrailing = maximumFrame.Concat(trailing).ToArray();
    Check(maximumThenTrailing.Length > maximumFrameLength,
        "coalesced gateway buffer exceeds per-frame limit");
    parser = new PercentDollarFrameParser(128 * 1024, maximumFrameLength);
    Check(parser.TryAppend(maximumThenTrailing, 0, maximumThenTrailing.Length,
        out var maximumFrames, out error), error);
    Equal(2, maximumFrames.Count, "maximum gateway frame plus trailing frame");
    Equal(maximumFrameLength, maximumFrames[0].Length,
        "maximum gateway frame preserved");
    Equal("%X2$", Encoding.ASCII.GetString(maximumFrames[1]),
        "gateway trailing frame preserved");
    Equal(0, parser.BufferedLength, "maximum gateway coalesced frames drained");
    return Task.CompletedTask;
}

static Task TestInternalPacket77Frames()
{
    var ack = InternalPacket77.Ack(0x81234567, 9, 0x0C).ToBytes();
    Equal(InternalPacket77.ACK_FRAME_LEN, ack.Length, "compact ACK length");
    var data = new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = 0x12345678,
        SeqID = 10,
        FrameLen = InternalPacket77.HEADER_SIZE + 4,
        Cmd = Grobal2.GM_DATA,
        Field16 = 11,
        Field20 = 4,
        Payload = new byte[] { 1, 2, 3, 4 }
    }.ToBytes();
    var input = new byte[] { 0x10, 0x77, 0xBB }
        .Concat(new byte[] { 0x01, 0x02 })
        .Concat(ack)
        .Concat(data)
        .ToArray();
    var parser = new InternalPacket77FrameParser(512);
    Check(parser.TryAppend(input, 0, 3, out var head, out var error), error);
    Equal(0, head.Count, "marker prefix fragment");
    Check(parser.TryAppend(input, 3, 7, out var middle, out error), error);
    Equal(0, middle.Count, "compact ACK fragment");
    Check(parser.TryAppend(input, 10, input.Length - 10, out var frames, out error), error);
    Equal(2, frames.Count, "coalesced packet count");
    Equal((ushort)0x0C, frames[0].Cmd, "compact ACK command");
    Equal((uint)0x81234567, frames[0].ConnID, "compact ACK route");
    Equal(Grobal2.GM_DATA, frames[1].Cmd, "data command");
    Check(frames[1].Payload.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "data payload");
    Equal(0, parser.BufferedLength, "parser drained");

    const int maximumFrameLength = 0x8000;
    var oversizedHeader = new byte[InternalPacket77.HEADER_SIZE];
    BitConverter.TryWriteBytes(oversizedHeader.AsSpan(0, 4), InternalPacket77.MAGIC);
    // BodyLen lives at +0x0E, not +0x0C (+0x0C is Cmd). Native:
    //   0x5F6679 66 81 78 0E 00 30  cmp word [eax+0x0E],0x3000
    //   0x63A674 8D 46 10           lea eax,[esi+0x10]
    //   0x63A677 0F B7 57 0E        movzx edx,word [edi+0x0E]
    //   0x63A67B 03 C2              add eax,edx        ; total = 0x10 + BodyLen
    // Writing the oversize at +0x0C only set Cmd and left BodyLen = 0, so the header
    // parsed as a perfectly valid 16-byte frame and the bound was never exercised.
    BitConverter.TryWriteBytes(oversizedHeader.AsSpan(14, 2),
        (ushort)(maximumFrameLength + 1 - InternalPacket77.HEADER_SIZE));
    var trailingAck = InternalPacket77.Ack(0x76543210, 12, 0x0C).ToBytes();
    var invalidThenAck = oversizedHeader.Concat(trailingAck).ToArray();
    var frameBounded = new InternalPacket77FrameParser(64 * 1024, maximumFrameLength);
    Check(frameBounded.TryAppend(invalidThenAck, 0, invalidThenAck.Length,
        out var afterOversized, out error), error);
    Equal(1, afterOversized.Count, "oversized frame header rejected");
    Equal((uint)0x76543210, afterOversized[0].ConnID,
        "valid ACK after oversized frame header");
    Equal(0, frameBounded.BufferedLength, "oversized frame header drained");

    var maximumPayload = new byte[maximumFrameLength - InternalPacket77.HEADER_SIZE];
    Array.Fill(maximumPayload, (byte)0x5A);
    var maximumFrame = new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = 0x01020304,
        SeqID = 13,
        Cmd = Grobal2.GM_DATA,
        Field20 = (uint)maximumPayload.Length,
        Payload = maximumPayload
    }.ToBytes();
    Equal(maximumFrameLength, maximumFrame.Length, "maximum legal frame length");
    var maximumThenAck = maximumFrame.Concat(trailingAck).ToArray();
    Check(maximumThenAck.Length > maximumFrameLength,
        "coalesced buffer exceeds the per-frame limit");
    frameBounded = new InternalPacket77FrameParser(64 * 1024, maximumFrameLength);
    Check(frameBounded.TryAppend(maximumThenAck, 0, maximumThenAck.Length,
        out var maximumFrames, out error), error);
    Equal(2, maximumFrames.Count, "maximum frame plus trailing ACK");
    Equal(maximumPayload.Length, maximumFrames[0].Payload.Length,
        "maximum frame payload preserved");
    Equal((uint)0x76543210, maximumFrames[1].ConnID,
        "trailing ACK after maximum frame");
    Equal(0, frameBounded.BufferedLength, "maximum coalesced frames drained");
    return Task.CompletedTask;
}

static Task TestRequestServerFrames()
{
    var first = BuildRequest(11, new byte[] { 1, 2, 3 });
    var second = BuildRequest(12, new byte[] { 4, 5 });
    var joined = first.Concat(second).ToArray();
    var parser = new RequestServerFrameParser(1024);
    Check(parser.TryAppend(joined, 0, 7, out var head, out var error), error);
    Equal(0, head.Count, "request first fragment");
    Check(parser.TryAppend(joined, 7, joined.Length - 7, out var frames, out error), error);
    Equal(2, frames.Count, "request coalesced count");
    Equal(11, Packets.ToPacket<RequestServerPacket>(frames[0])!.QueryId, "request id 1");
    Equal(12, Packets.ToPacket<RequestServerPacket>(frames[1])!.QueryId, "request id 2");
    return Task.CompletedTask;
}

static Task TestSparseCharacterTransport()
{
    var human = new THumDataInfo();
    human.Data.HumItems[2] = new TUserItem
    {
        MakeIndex = 77,
        wIndex = 9,
        Dura = 10,
        DuraMax = 20,
        NativeRecord = new byte[NativeHumanDataCodec.ItemRecordSize]
    };
    human.Data.HumItems[15] = new TUserItem
    {
        MakeIndex = 0x15263748,
        wIndex = 34,
        Dura = 321,
        DuraMax = 654,
        NativeRecord = new byte[NativeHumanDataCodec.ItemRecordSize]
    };
    human.Data.Magic[4] = new TMagicRcd { wMagIdx = 12, btLevel = 3 };
    var envelope = new SaveHumDataPacket
    {
        sAccount = "account",
        sCharName = "character",
        HumDataInfo = human
    };
    var encoded = ProtoBufDecoder.Serialize(envelope);
    Check(encoded != null && encoded.Length > 0, "sparse character serialized");
    Check(human.Data.HumItems[0] == null && human.Data.Magic[0] == null,
        "source sparse slots restored after serialization");

    var decoded = ProtoBufDecoder.DeSerialize<SaveHumDataPacket>(encoded);
    Check(decoded?.HumDataInfo?.Data != null, "sparse character deserialized");
    var data = decoded!.HumDataInfo.Data;
    Equal(16, data.HumItems.Length, "equipment slot count");
    Equal(48, data.BagItems.Length, "bag slot count");
    Equal(192, data.StorageItems.Length, "storage slot count");
    Equal(Grobal2.MAXMAGIC, data.Magic.Length, "magic slot count");
    Check(data.HumItems[0] == null,
        "empty equipment slot remains null");
    Equal((ushort)9, data.HumItems[2].wIndex,
        "sparse equipment index preserved");
    Equal(0x15263748, data.HumItems[15].MakeIndex,
        "equipment slot 15 make index preserved");
    Equal((ushort)34, data.HumItems[15].wIndex,
        "equipment slot 15 item index preserved");
    Equal((ushort)321, data.HumItems[15].Dura,
        "equipment slot 15 durability preserved");
    Equal((ushort)654, data.HumItems[15].DuraMax,
        "equipment slot 15 maximum durability preserved");
    Check(data.Magic[0] == null, "empty magic slot remains null");
    Equal((ushort)12, data.Magic[4].wMagIdx,
        "sparse magic index preserved");
    return Task.CompletedTask;
}

static Task TestLegacyEquipmentTransport()
{
    var legacy = new THumDataInfo();
    legacy.Data.HumItems = Enumerable.Range(0, 13)
        .Select(_ => new TUserItem())
        .ToArray();
    legacy.Data.HumItems[12] = new TUserItem
    {
        MakeIndex = 0x10203040,
        wIndex = 77,
        Dura = 88,
        DuraMax = 99
    };
    legacy.Data.BagItems = Enumerable.Range(0, 48)
        .Select(_ => new TUserItem())
        .ToArray();
    legacy.Data.StorageItems = Enumerable.Range(0, 192)
        .Select(_ => new TUserItem())
        .ToArray();
    legacy.Data.Magic = Enumerable.Range(0, Grobal2.MAXMAGIC)
        .Select(_ => new TMagicRcd())
        .ToArray();

    using var stream = new MemoryStream();
    ProtoBuf.Serializer.Serialize(stream, legacy);
    var upgraded = ProtoBufDecoder.DeSerialize<THumDataInfo>(stream.ToArray());

    var upgradedItems = upgraded?.Data?.HumItems;
    Check(upgradedItems != null, "legacy equipment protobuf decoded");
    Equal(16, upgradedItems!.Length, "legacy equipment expanded to 16 slots");
    Equal(0x10203040, upgradedItems[12].MakeIndex,
        "legacy final equipment slot preserved");
    Check(upgradedItems[13] == null
          && upgradedItems[14] == null
          && upgradedItems[15] == null,
        "new equipment slots start empty");
    return Task.CompletedTask;
}

static Task TestLegacyGbkText()
{
    const string name = "俞刚毅";
    var expected = Convert.FromHexString("D3E1B8D5D2E3");
    Check(LegacyGbkText.Encode(name).SequenceEqual(expected), "GBK name bytes");
    Equal(name, LegacyGbkText.Decode(expected), "GBK name decode");
    Equal(name, LegacyGbkText.Decode(expected.Concat(new byte[] { 0, 0 }).ToArray()),
        "GBK name NUL padding");
    return Task.CompletedTask;
}

static Task TestCaseSensitiveNameCaches()
{
    AssertCaseSensitiveCache(new MySqlPlayRecordService(), "QuickList");
    AssertCaseSensitiveCache(new MySqlPlayDataService(), "MirQuickList");
    AssertCaseSensitiveCache(new MySqlHeroRecordService(), "_quickIndex");
    return Task.CompletedTask;
}

static void AssertCaseSensitiveCache(object service, string fieldName)
{
    var field = service.GetType().GetField(fieldName,
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var value = field?.GetValue(service);
    Check(value is ConcurrentDictionary<string, int>,
        "missing name cache " + fieldName);
    var cache = (ConcurrentDictionary<string, int>)value!;
    cache["CaseName"] = 11;
    cache["casename"] = 22;
    Equal(2, cache.Count, fieldName + " merged case-distinct names");
    Equal(11, cache["CaseName"], fieldName + " upper lookup");
    Equal(22, cache["casename"], fieldName + " lower lookup");
}

static Task TestNativeHumanBind()
{
    var legacyZeroStorageBlob = new byte[NativeHumanDataCodec.DataRecordSize + 8];
    BinaryPrimitives.WriteInt32LittleEndian(
        legacyZeroStorageBlob.AsSpan(4, 4), NativeHumanDataCodec.DataRecordSize);
    legacyZeroStorageBlob[8 + 0x3E] = 1;
    Check(NativeHumanDataCodec.TryDecode(legacyZeroStorageBlob, null,
            out var legacyZeroStorage, out var legacyStorageError),
        "legacy zero-storage native decode: " + legacyStorageError);
    Equal(0, legacyZeroStorage.Data.StorageSpaceCount,
        "legacy zero storage word preserved by DB codec");
    // The DB codec preserves record+0x50E verbatim. M2 applies the runtime
    // container rule (`stored > 48` overrides its baseline) in GetHumData.
    foreach (var stored in new[] { 0, 24, 48, 49, 192, 193, 65535 })
    {
        var storageBlob = new byte[NativeHumanDataCodec.DataRecordSize + 8];
        BinaryPrimitives.WriteInt32LittleEndian(
            storageBlob.AsSpan(4, 4), NativeHumanDataCodec.DataRecordSize);
        storageBlob[8 + 0x3E] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(
            storageBlob.AsSpan(8 + 0x050E, 2), (ushort)stored);
        Check(NativeHumanDataCodec.TryDecode(storageBlob, null,
                out var storageInfo, out var storageError),
            $"stored storage capacity {stored} native decode: {storageError}");
        Equal(stored, storageInfo.Data.StorageSpaceCount,
            $"stored storage capacity {stored} preserved by DB codec");
    }

    var bound = new THumDataInfo();
    bound.Data.Abil.HP = 240573;
    bound.Data.Abil.MP = 131119;
    bound.Data.Abil.MaxHP = 260000;
    bound.Data.Abil.MaxMP = 140000;
    bound.Data.ForceLv = unchecked((int)0x89ABCDEFu);
    bound.Data.ForceExp = 0x10203040;
    bound.Data.FightPoints = unchecked((int)0xF1234567u);
    bound.Data.sfLevel = -123456789;
    bound.Data.NativeHeroIntimacy = 321.75;
    bound.Data.NativeHeroExperienceAccumulator = Convert.FromHexString(
        "010002000300040000943577010000000200000003000000");
    bound.Data.btSecHeroPracticeRewardMode = 3;
    bound.Data.btSecHeroPracticeCostTier = 2;
    bound.Data.wSecHeroPracticeLevel = 0xC3D4;
    bound.Data.nLingFu = 0x12345678;
    bound.Data.nUsedLingFu = 0x23456789;
    // Non-default value used to prove that the stored word survives the round trip.
    bound.Data.StorageSpaceCount = 96;
    var shieldNativeRecord = Enumerable.Range(0, NativeHumanDataCodec.ItemRecordSize)
        .Select(i => (byte)(i * 17 + 3))
        .ToArray();
    var expectedEquipment = new Dictionary<int, TUserItem>();
    for (var slot = Grobal2.U_MASK; slot <= Grobal2.U_HORSE; slot++)
    {
        var item = new TUserItem
        {
            MakeIndex = 0x20000000 + slot,
            wIndex = (ushort)(100 + slot),
            Dura = (ushort)(200 + slot),
            DuraMax = (ushort)(300 + slot),
            Bind = (byte)(slot - Grobal2.U_MASK + 1),
            ys1 = 0x30000000 + slot,
            ys2 = (byte)(40 + slot),
            ys17 = (byte)(80 + slot),
            jp1 = (byte)(90 + slot),
            jp6 = (byte)(100 + slot),
            pname = "equipment-slot-" + slot,
            desc1 = "eye-slot-" + slot
        };
        if (slot == Grobal2.U_HORSE)
        {
            item.UpgradeFlags = shieldNativeRecord[0x27];
            item.NativeRecord = (byte[])shieldNativeRecord.Clone();
        }
        bound.Data.HumItems[slot] = item;
        expectedEquipment[slot] = new TUserItem(item);
    }
    bound.Data.BagItems[0] = new TUserItem
    {
        MakeIndex = 0x12345678,
        wIndex = 1,
        Dura = 10,
        DuraMax = 20,
        Bind = 1,
        ys1 = 0x10203040,
        ys2 = 2,
        ys3 = 3,
        ys4 = 4,
        ys5 = 5,
        ys6 = 6,
        ys7 = 7,
        ys8 = 8,
        ys9 = 9,
        ys10 = 10,
        ys11 = 11,
        ys12 = 12,
        ys13 = 13,
        ys14 = 14,
        ys15 = 15,
        ys16 = 16,
        ys17 = 17,
        jp1 = 21,
        jp2 = 22,
        jp3 = 23,
        jp4 = 24,
        jp5 = 25,
        jp6 = 26,
        pname = "source-player",
        desc1 = "line-one",
        desc2 = "line-two",
        sourceTime = "2026-07-15 01:02",
        killerName = "monster-a",
        mapName = "map-a"
    };
    var expected = new TUserItem(bound.Data.BagItems[0]);
    Check(NativeHumanDataCodec.TryEncode(bound, out var boundBlob, out var scriptBlob,
            out var error),
        $"bound native item encode: {error}");
    Check(scriptBlob?.Length > 0, "eye sidecar ScriptData envelope");
    Equal(16, NativeHumanDataCodec.EquippedItemCount,
        "native equipment slot count");
    const int equippedBase = 0x0F68;
    foreach (var entry in expectedEquipment)
    {
        var offset = equippedBase + entry.Key * NativeHumanDataCodec.ItemRecordSize;
        Equal(entry.Value.MakeIndex, BitConverter.ToInt32(bound.NativeData, offset),
            $"native equipment slot {entry.Key} MakeIndex");
        Equal(entry.Value.wIndex, BitConverter.ToUInt16(bound.NativeData, offset + 4),
            $"native equipment slot {entry.Key} wIndex");
        Equal(entry.Value.Bind, bound.NativeData[offset + 0xB8],
            $"native equipment slot {entry.Key} bind +0xB8");
    }
    const int shieldRecordOffset = 0x1B98;
    var encodedShieldRecord = bound.Data.HumItems[Grobal2.U_HORSE].NativeRecord;
    Equal(NativeHumanDataCodec.ItemRecordSize, encodedShieldRecord.Length,
        "native equipment slot 15 record size");
    Check(bound.NativeData.AsSpan(shieldRecordOffset, NativeHumanDataCodec.ItemRecordSize)
            .SequenceEqual(encodedShieldRecord),
        "native equipment slot 15 full record written at 0x1B98");
    Equal((byte)1, bound.NativeData[0x2BF6 + 0xB8],
        "bound native bag item byte +0xB8");
    Equal(bound.Data.ForceLv, BitConverter.ToInt32(bound.NativeData, 0x04E0),
        "native ForceLv raw +0x4E0");
    Equal(bound.Data.ForceExp, BitConverter.ToInt32(bound.NativeData, 0x04E4),
        "native ForceExp raw +0x4E4");
    Equal(bound.Data.FightPoints, BitConverter.ToInt32(bound.NativeData, 0x04E8),
        "native FightPoints raw +0x4E8");
    Equal(bound.Data.sfLevel, BitConverter.ToInt32(bound.NativeData, 0x04EC),
        "native sfLevel raw +0x4EC");
    Equal(BitConverter.DoubleToInt64Bits(bound.Data.NativeHeroIntimacy),
        BitConverter.ToInt64(bound.NativeData, 0x01E0),
        "native hero intimacy double raw +0x1E0");
    Check(bound.NativeData.AsSpan(0x04C8, 24).SequenceEqual(
            bound.Data.NativeHeroExperienceAccumulator),
        "native hero experience accumulator raw +0x4C8");
    Equal(bound.Data.btSecHeroPracticeRewardMode, bound.NativeData[0x04F0],
        "native secondary-hero practice reward mode raw +0x4F0");
    Equal(bound.Data.btSecHeroPracticeCostTier, bound.NativeData[0x04F1],
        "native secondary-hero practice cost tier raw +0x4F1");
    Equal(bound.Data.wSecHeroPracticeLevel, BitConverter.ToUInt16(bound.NativeData, 0x04F2),
        "native secondary-hero practice level raw +0x4F2");
    // ⚠️ 这两条此前断言 0xF8/0xFC，偏移是**审计自己写错的**，不是 codec 的 bug。
    // M2Server 逐字证据（SAVE sub_6B0FF0 / LOAD sub_6AFD7C）：
    //   0x6B1288  mov eax,[ebx+0xbd8]      ; obj+0xBD8 = nLingFu
    //   0x6B128E  mov [esi+0xf0],eax       ; -> rec+0xF0
    //   0x6B1294  mov eax,[ebx+0xbdc]      ; obj+0xBDC = MyUsedLfNum
    //   0x6B129A  mov [esi+0xf4],eax       ; -> rec+0xF4
    //   0x6B0240  mov eax,[eax+0xf4] / 0x6B0249 mov [edx+0xbdc],eax   (LOAD 回填)
    // C# NativeHumanDataCodec 的 LingFuOffset=0x00F0 / UsedLingFuOffset=0x00F4
    // 与原版一致，先前 actual=0 是**正确行为**，红灯是假红。
    //
    // rec+0xF8 另有其人：M2 SAVE 0x6B14FF `mov dword ptr [esi+0xf8],eax`，
    // 源 = sub_714334([obj+0x1824]) = [p+8]+[p+0xC]（**和**，非单字段），
    // 装载器 0x714340 跑 `select Idx,Value,UsedValue,Value2 from CreditCard where CharName="%s";`
    // Value→+0x8、Value2→+0xC ⇒ rec+0xF8 = CreditCard Value+Value2 的存档快照，与灵符无关。
    // 且 LOAD 侧**无人读** rec+0xF8（1165 条指令穷举 disp，0xF8 命中 0，
    // 同扫描 0xF0/0xF4 各 1 命中，灵敏度对照通过）。
    Equal(bound.Data.nLingFu, BitConverter.ToInt32(bound.NativeData, 0x00F0),
        "native LingFu raw +0xF0 (obj+0xBD8; @0x6B128E)");
    Equal(bound.Data.nUsedLingFu, BitConverter.ToInt32(bound.NativeData, 0x00F4),
        "native used LingFu raw +0xF4 (obj+0xBDC; @0x6B129A)");
    Equal((ushort)bound.Data.StorageSpaceCount,
        BitConverter.ToUInt16(bound.NativeData, 0x050E),
        "native storage capacity raw +0x50E (0x6B112F mov [esi+0x50E],ax)");
    Equal(bound.Data.Abil.HP, BitConverter.ToInt32(bound.NativeData, 0x48),
        "native 32-bit HP raw +0x48");
    Equal(bound.Data.Abil.MP, BitConverter.ToInt32(bound.NativeData, 0x4C),
        "native 32-bit MP raw +0x4C");
    var transportBytes = ProtoBufDecoder.Serialize(bound);
    var transported = ProtoBufDecoder.DeSerialize<THumDataInfo>(transportBytes);
    Equal(bound.Data.btSecHeroPracticeRewardMode,
        transported.Data.btSecHeroPracticeRewardMode,
        "secondary-hero practice reward mode protobuf roundtrip");
    Equal(bound.Data.btSecHeroPracticeCostTier,
        transported.Data.btSecHeroPracticeCostTier,
        "secondary-hero practice cost tier protobuf roundtrip");
    Equal(bound.Data.wSecHeroPracticeLevel, transported.Data.wSecHeroPracticeLevel,
        "secondary-hero practice level protobuf roundtrip");
    Equal(bound.Data.nLingFu, transported.Data.nLingFu,
        "native LingFu protobuf roundtrip");
    Equal(bound.Data.nUsedLingFu, transported.Data.nUsedLingFu,
        "native used LingFu protobuf roundtrip");
    Equal(bound.Data.Abil.HP, transported.Data.Abil.HP,
        "32-bit HP protobuf roundtrip");
    Equal(bound.Data.Abil.MP, transported.Data.Abil.MP,
        "32-bit MP protobuf roundtrip");
    Equal(bound.Data.Abil.MaxHP, transported.Data.Abil.MaxHP,
        "32-bit MaxHP protobuf roundtrip");
    Equal(bound.Data.Abil.MaxMP, transported.Data.Abil.MaxMP,
        "32-bit MaxMP protobuf roundtrip");
    Check(NativeHumanDataCodec.TryDecode(boundBlob, scriptBlob, out var roundTrip, out error),
        $"bound native item decode: {error}");
    Equal(bound.Data.ForceLv, roundTrip.Data.ForceLv, "native ForceLv roundtrip");
    Equal(bound.Data.ForceExp, roundTrip.Data.ForceExp, "native ForceExp roundtrip");
    Equal(bound.Data.FightPoints, roundTrip.Data.FightPoints,
        "native FightPoints roundtrip");
    Equal(bound.Data.sfLevel, roundTrip.Data.sfLevel, "native sfLevel roundtrip");
    Equal(bound.Data.NativeHeroIntimacy, roundTrip.Data.NativeHeroIntimacy,
        "native hero intimacy roundtrip");
    Check(bound.Data.NativeHeroExperienceAccumulator.AsSpan().SequenceEqual(
            roundTrip.Data.NativeHeroExperienceAccumulator),
        "native hero experience accumulator roundtrip");
    Equal(bound.Data.btSecHeroPracticeRewardMode,
        roundTrip.Data.btSecHeroPracticeRewardMode,
        "native secondary-hero practice reward mode roundtrip");
    Equal(bound.Data.btSecHeroPracticeCostTier, roundTrip.Data.btSecHeroPracticeCostTier,
        "native secondary-hero practice cost tier roundtrip");
    Equal(bound.Data.wSecHeroPracticeLevel, roundTrip.Data.wSecHeroPracticeLevel,
        "native secondary-hero practice level roundtrip");
    Equal(bound.Data.nLingFu, roundTrip.Data.nLingFu,
        "native LingFu roundtrip");
    Equal(bound.Data.nUsedLingFu, roundTrip.Data.nUsedLingFu,
        "native used LingFu roundtrip");
    Equal(bound.Data.StorageSpaceCount, roundTrip.Data.StorageSpaceCount,
        "native storage capacity roundtrip");
    Equal(bound.Data.Abil.HP, roundTrip.Data.Abil.HP,
        "native 32-bit HP roundtrip");
    Equal(bound.Data.Abil.MP, roundTrip.Data.Abil.MP,
        "native 32-bit MP roundtrip");
    Equal((byte)1, roundTrip.Data.BagItems[0].Bind,
        "bound native bag item roundtrip");
    AssertYanshenItem(expected, roundTrip.Data.BagItems[0], "native eye sidecar roundtrip");
    foreach (var entry in expectedEquipment)
    {
        var actual = roundTrip.Data.HumItems[entry.Key];
        Check(actual != null, $"native equipment slot {entry.Key} roundtrip item");
        Equal(entry.Value.MakeIndex, actual!.MakeIndex,
            $"native equipment slot {entry.Key} roundtrip MakeIndex");
        Equal(entry.Value.wIndex, actual.wIndex,
            $"native equipment slot {entry.Key} roundtrip wIndex");
        Equal(entry.Value.Bind, actual.Bind,
            $"native equipment slot {entry.Key} roundtrip bind");
        AssertYanshenItem(entry.Value, actual,
            $"native equipment slot {entry.Key} eye sidecar roundtrip");
    }
    Check(roundTrip.Data.HumItems[Grobal2.U_HORSE].NativeRecord.AsSpan()
            .SequenceEqual(encodedShieldRecord),
        "native equipment slot 15 full 208-byte record preserved");
    Check(roundTrip.NativeData.AsSpan(shieldRecordOffset,
            NativeHumanDataCodec.ItemRecordSize).SequenceEqual(encodedShieldRecord),
        "native equipment slot 15 decoded raw bytes remain at 0x1B98");

    Check(YanshenItemSidecarCodec.TryEncode(bound.Data.HumItems, bound.Data.BagItems,
            bound.Data.StorageItems, out var sidecar, out error),
        "standalone eye sidecar encode: " + error);
    var movedEquipment = new TUserItem[Grobal2.HUMAN_EQUIPPED_ITEM_COUNT];
    foreach (var entry in expectedEquipment)
    {
        movedEquipment[entry.Key] = new TUserItem
        {
            MakeIndex = entry.Value.MakeIndex,
            wIndex = entry.Value.wIndex,
            Dura = entry.Value.Dura,
            DuraMax = entry.Value.DuraMax
        };
    }
    var movedBag = new TUserItem[48];
    var movedStorage = new TUserItem[192];
    movedStorage[7] = new TUserItem
    {
        MakeIndex = expected.MakeIndex,
        wIndex = expected.wIndex,
        Dura = expected.Dura,
        DuraMax = expected.DuraMax
    };
    Check(YanshenItemSidecarCodec.TryApply(sidecar, movedEquipment, movedBag,
            movedStorage, out error), "eye sidecar unique identity fallback: " + error);
    AssertYanshenItem(expected, movedStorage[7], "eye sidecar moved item");

    var truncatedTarget = new TUserItem[48];
    truncatedTarget[0] = new TUserItem
    {
        MakeIndex = expected.MakeIndex,
        wIndex = expected.wIndex,
        ys1 = 777
    };
    Check(!YanshenItemSidecarCodec.TryApply(sidecar[..^1],
            new TUserItem[Grobal2.HUMAN_EQUIPPED_ITEM_COUNT],
            truncatedTarget, new TUserItem[192], out _),
        "truncated eye sidecar must be rejected");
    Equal(777, truncatedTarget[0].ys1,
        "rejected eye sidecar must not partially mutate items");

    var duplicateBag = new TUserItem[48];
    var duplicateStorage = new TUserItem[192];
    duplicateBag[0] = new TUserItem(expected);
    duplicateStorage[0] = new TUserItem(expected);
    Check(!YanshenItemSidecarCodec.TryEncode(
            new TUserItem[Grobal2.HUMAN_EQUIPPED_ITEM_COUNT], duplicateBag,
            duplicateStorage, out _, out _),
        "duplicate eye item identity must be rejected");

    Check(YanshenItemSidecarCodec.TryApply(Array.Empty<byte>(), roundTrip.Data.HumItems,
            roundTrip.Data.BagItems, roundTrip.Data.StorageItems, out error),
        "clear eye sidecar fields: " + error);
    Check(!YanshenItemSidecarCodec.HasExtensionData(roundTrip.Data.BagItems[0]),
        "clearing the eye sidecar must empty the in-memory item");
    Check(YanshenItemSidecarCodec.TryEncode(roundTrip.Data.HumItems,
              roundTrip.Data.BagItems, roundTrip.Data.StorageItems,
              out var clearedSidecar, out error) && clearedSidecar.Length == 0,
        "cleared items must not produce an eye sidecar section: " + error);
    Check(NativeHumanDataCodec.TryEncode(roundTrip, out var clearedBlob,
            out var clearedScriptBlob, out error), "remove eye sidecar section: " + error);
    Check(NativeHumanDataCodec.TryDecode(clearedBlob, clearedScriptBlob,
            out var clearedRoundTrip, out error), "decode removed eye sidecar: " + error);
    var clearedBag = clearedRoundTrip.Data.BagItems[0];
    // Everything YanshenNativeItemLayout.Pack rewrites unconditionally must come back
    // exactly as the wipe left it — a stale 0x79 section resurfacing here is the bug this
    // guards.
    Equal(0, clearedBag.ys1, "removed eye sidecar restored a stale ys1");
    Check(new byte[]
    {
        clearedBag.ys2, clearedBag.ys3, clearedBag.ys4, clearedBag.ys5,
        clearedBag.ys6, clearedBag.ys7, clearedBag.ys8, clearedBag.ys9,
        clearedBag.ys10, clearedBag.ys11, clearedBag.ys12, clearedBag.ys13,
        clearedBag.ys14, clearedBag.ys15, clearedBag.ys16, clearedBag.ys17
    }.All(value => value == 0), "removed eye sidecar restored stale ys2-ys17");
    Check(new[]
    {
        clearedBag.jp1, clearedBag.jp2, clearedBag.jp3,
        clearedBag.jp4, clearedBag.jp5, clearedBag.jp6
    }.All(value => value == 0), "removed eye sidecar restored stale jp1-jp6");
    // The origin block is a different matter: it is not re-derivable, and 战神 carries all
    // 208 item bytes verbatim —
    //   SAVE 0x6B170F 8D 70 20 lea esi,[eax+0x20] / 0x6B1712 B9 34 00 00 00 mov ecx,0x34
    //        0x6B1717 F3 A5 rep movsd     (0x34 dwords = 208, 48-slot loop 0x6B171B cmp edi,0x30)
    //   LOAD 0x74DB3A 8D 7B 20            / 0x74DB3D B9 34 00 00 00 / 0x74DB42 F3 A5
    // while ScriptData type 0x79 is not a native section at all
    //   0x6E4510 83 F8 08 cmp eax,8 / 0x6E4513 0F 87 3D 03 00 00 ja 0x6E4856
    // (the jump table at 0x6E4520 only covers types 0..8), i.e. the sidecar is a C#-side
    // migration overlay, never the authority. So dropping the overlay does NOT erase the
    // carried origin bytes — but it must not let them drift either: another save/load has
    // to be a fixed point. (What the record can represent is pinned by the sidecar
    // roundtrip above; the record cannot hold a drop origin and a custom description at
    // the same time, item+0x74 selects one branch.)
    Check(NativeHumanDataCodec.TryEncode(clearedRoundTrip, out var stableBlob,
            out var stableScriptBlob, out error), "re-encode cleared record: " + error);
    Check(NativeHumanDataCodec.TryDecode(stableBlob, stableScriptBlob,
            out var stableRoundTrip, out error), "re-decode cleared record: " + error);
    AssertYanshenItem(clearedBag, stableRoundTrip.Data.BagItems[0],
        "carried eye origin is a save/load fixed point without the sidecar");
    return Task.CompletedTask;
}

static void AssertYanshenItem(TUserItem expected, TUserItem actual, string area)
{
    Equal(expected.ys1, actual.ys1, area + " ys1");
    var expectedYs = new[]
    {
        expected.ys2, expected.ys3, expected.ys4, expected.ys5,
        expected.ys6, expected.ys7, expected.ys8, expected.ys9,
        expected.ys10, expected.ys11, expected.ys12, expected.ys13,
        expected.ys14, expected.ys15, expected.ys16, expected.ys17
    };
    var actualYs = new[]
    {
        actual.ys2, actual.ys3, actual.ys4, actual.ys5,
        actual.ys6, actual.ys7, actual.ys8, actual.ys9,
        actual.ys10, actual.ys11, actual.ys12, actual.ys13,
        actual.ys14, actual.ys15, actual.ys16, actual.ys17
    };
    Check(expectedYs.SequenceEqual(actualYs), area + " ys2-ys17");
    var expectedJp = new[] { expected.jp1, expected.jp2, expected.jp3,
        expected.jp4, expected.jp5, expected.jp6 };
    var actualJp = new[] { actual.jp1, actual.jp2, actual.jp3,
        actual.jp4, actual.jp5, actual.jp6 };
    Check(expectedJp.SequenceEqual(actualJp), area + " jp1-jp6");
    Equal(expected.pname, actual.pname, area + " pname");
    Equal(expected.desc1, actual.desc1, area + " desc1");
    Equal(expected.desc2, actual.desc2, area + " desc2");
    Equal(expected.sourceTime, actual.sourceTime, area + " sourceTime");
    Equal(expected.killerName, actual.killerName, area + " killerName");
    Equal(expected.mapName, actual.mapName, area + " mapName");
}

static Task TestSessionReplacement()
{
    var service = new LoginSvrService(new ConfigManager(
        Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini")));
    try
    {
        service.OpenMobileSession("account-a", "192.168.1.10", 101);
        Check(service.CheckSession("account-a", "192.168.1.10", 101),
            "initial session admission");

        service.OpenMobileSession("account-a", "192.168.1.11", 102);
        Check(!service.CheckSession("account-a", "192.168.1.10", 101),
            "superseded account session rejected");
        Check(service.CheckSession("account-a", "192.168.1.11", 102),
            "replacement session admitted");

        service.OpenMobileSession("account-a", "192.168.1.12", 102);
        Check(!service.CheckSession("account-a", "192.168.1.11", 102),
            "same session old IP rejected");
        Check(service.CheckSession("account-a", "192.168.1.12", 102),
            "same session current IP admitted");
    }
    finally { service.Stop(); }
    return Task.CompletedTask;
}

static async Task TestNativeLoginGateClientMode()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var iniPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini");
    File.WriteAllText(iniPath,
        "[LoginGate]\r\nIP=127.0.0.1\r\n"
        + $"Port={port}\r\nReconnectIntervalMs=100\r\nAuthTimeoutMs=1000\r\n");

    var oldServerName = DBShare.sServerName;
    var oldZone = DBShare.nZoneIdx;
    var oldGroup = DBShare.nGroupIdx;
    var oldPublicGateAddress = DBShare.g_sPublicGateAddr;
    var oldPublicGatePort = DBShare.g_nPublicGatePort;
    LoginSvrService? service = null;
    try
    {
        DBShare.sServerName = LegacyGbkText.Decode(
            Convert.FromHexString("C2EAB7A8CCE5D1E9B7FE"));
        DBShare.nZoneIdx = 180;
        DBShare.nGroupIdx = 1;
        DBShare.g_sPublicGateAddr = "124.221.96.15";
        DBShare.g_nPublicGatePort = 7100;
        service = new LoginSvrService(new ConfigManager(iniPath));
        Equal(LoginGateTransportMode.Native77Client, service.Mode,
            "native 5600 mode");
        Check(service.QueueNativeType2Control(true),
            "native type2 control was not retained while disconnected");

        const System.Reflection.BindingFlags nativeFields =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        var parserLock = typeof(LoginSvrService).GetField("_nativeParserLock",
                nativeFields)?.GetValue(service)
            ?? throw new MissingFieldException("LoginSvrService._nativeParserLock");
        var controlSocketField = typeof(LoginSvrService).GetField(
                "_nativeControlSocket", nativeFields)
            ?? throw new MissingFieldException("LoginSvrService._nativeControlSocket");
        var accept = listener.AcceptTcpClientAsync();
        TcpClient acceptedPeer;
        lock (parserLock)
        {
            service.Start();
            acceptedPeer = accept.WaitAsync(TimeSpan.FromSeconds(3))
                .GetAwaiter().GetResult();
            Check(SpinWait.SpinUntil(
                    () => controlSocketField.GetValue(service) != null, 3000),
                "native connection was not published before registration");
            Check(service.QueueNativeType2Control(false),
                "native control rejected during registration publication");
        }
        using var peer = acceptedPeer;
        var stream = peer.GetStream();
        var registration = await ReadLegacy77Frame(stream)
            .WaitAsync(TimeSpan.FromSeconds(3));
        Equal(NativeLoginGateProtocol.RegistrationRequestIdent,
            registration.Ident, "native registration ident");
        Equal(0, registration.QueryId, "native registration sanitized query");
        Equal(0, registration.Param, "native registration sanitized param");
        Equal(40, registration.Payload.Length, "native registration payload");
        var retainedControl = await ReadLegacy77Frame(stream)
            .WaitAsync(TimeSpan.FromSeconds(3));
        Equal(NativeLoginGateProtocol.Type2ControlEnabledIdent,
            retainedControl.Ident, "retained native type2 enable control");
        Equal(0, retainedControl.QueryId, "native type2 control query");
        Equal(0, retainedControl.Param, "native type2 control param");
        Equal(0, retainedControl.Payload.Length, "native type2 control payload");
        var registrationRaceControl = await ReadLegacy77Frame(stream)
            .WaitAsync(TimeSpan.FromSeconds(3));
        Equal(NativeLoginGateProtocol.Type2ControlDisabledIdent,
            registrationRaceControl.Ident,
            "native control queued during connect follows registration");

        Check(!service.TryAuthenticateNative(2358,
                "c413abef0d1b671c58da593ab93d7c96",
                Convert.FromHexString("4CF2D1CFFFFFFFFF"),
                "223.160.203.135", "mobile-mac-address",
                (_, _, _) => { }, out var preRegistrationError),
            "native auth accepted before registration ACK");
        Check(preRegistrationError.Contains("registration", StringComparison.Ordinal),
            "native pre-registration auth error");

        var registrationAck = new YbDbLegacy77Frame(
            unchecked((int)0x00F0C1D4), 0x22B8,
            NativeLoginGateProtocol.RegistrationResponseIdent, Array.Empty<byte>());
        var generationField = typeof(LoginSvrService).GetField(
                "_nativeControlGeneration", nativeFields)
            ?? throw new MissingFieldException("LoginSvrService._nativeControlGeneration");
        var processNativeFrame = typeof(LoginSvrService).GetMethod(
                "ProcessNativeLoginGateFrame", nativeFields)
            ?? throw new MissingMethodException(
                "LoginSvrService.ProcessNativeLoginGateFrame");
        var nativeSocket = (Socket)(controlSocketField.GetValue(service)
            ?? throw new InvalidOperationException("native control socket missing"));
        var currentGeneration = (long)generationField.GetValue(service)!;
        processNativeFrame.Invoke(service,
            new object[] { registrationAck, nativeSocket, currentGeneration - 1 });
        Check(!service.IsNativeRegistered,
            "stale native generation accepted a registration ACK");
        Check(YbDbLegacy77Codec.TryEncode(registrationAck,
            out var ackBytes, out var error), error);
        await stream.WriteAsync(ackBytes);
        for (var i = 0; i < 50 && !service.IsNativeRegistered; i++)
            await Task.Delay(10);
        Check(service.IsNativeRegistered, "native registration ACK state");
        Check(service.QueueNativeType2Control(false),
            "connected native type2 disable control rejected");
        Check(service.QueueNativeType2Control(true)
              && service.QueueNativeType2Control(false),
            "connected native type2 control burst rejected");
        var expectedControls = new[]
        {
            NativeLoginGateProtocol.Type2ControlDisabledIdent,
            NativeLoginGateProtocol.Type2ControlEnabledIdent,
            NativeLoginGateProtocol.Type2ControlDisabledIdent
        };
        foreach (var expectedControl in expectedControls)
        {
            var liveControl = await ReadLegacy77Frame(stream)
                .WaitAsync(TimeSpan.FromSeconds(3));
            Equal(expectedControl, liveControl.Ident,
                "live native type2 control burst order");
        }

        var completion = new TaskCompletionSource<(
            NativeLoginGateAuthResponse? Response, string? Error,
            long LoginDateTimeBits)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Check(service.TryAuthenticateNative(2359,
                "c413abef0d1b671c58da593ab93d7c96",
                Convert.FromHexString("4CF2D1CFFFFFFFFF"),
                "223.160.203.135", "mobile-mac-address",
                (response, authError, loginDateTimeBits) =>
                    completion.TrySetResult((response, authError,
                        loginDateTimeBits)),
                out error), error);
        var authRequest = await ReadLegacy77Frame(stream)
            .WaitAsync(TimeSpan.FromSeconds(3));
        Equal(NativeLoginGateProtocol.AuthRequestIdent, authRequest.Ident,
            "native auth request ident");
        Equal(2359, BitConverter.ToInt32(authRequest.Payload, 2),
            "native auth payload query");
        var loginDateTimeBits = unchecked((long)0x8877665544332211UL);
        Check(!service.SetPendingNativeLoginDateTimeBits(
                9999, loginDateTimeBits),
            "native pending date accepted unknown query");
        Check(service.SetPendingNativeLoginDateTimeBits(
                2359, loginDateTimeBits),
            "native pending date rejected live query");

        var authResponseBytes = Convert.FromHexString(
            "77BBAA330000000000000000EB037C0006013709000000000000000070746964763335626C7265737A6A37786C366A7A0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
        await stream.WriteAsync(authResponseBytes.AsMemory(0, 7));
        await stream.WriteAsync(authResponseBytes.AsMemory(7));
        var authResult = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(string.IsNullOrEmpty(authResult.Error),
            "native auth completion error: " + authResult.Error);
        Equal("ptidv35blreszj7xl6jz", authResult.Response!.Account,
            "native auth completion account");
        Equal(loginDateTimeBits, authResult.LoginDateTimeBits,
            "native auth completion date bits");
        Check(!service.SetPendingNativeLoginDateTimeBits(
                2359, 0),
            "native completed query remained pending");

        var probeRequest = Convert.FromHexString(
            "77BBAA330000000000000000E9031C004407000019FCFFFF4F0B000000000000B40001000000000000000000");
        await stream.WriteAsync(probeRequest.AsMemory(0, 19));
        await stream.WriteAsync(probeRequest.AsMemory(19));
        var probeResponse = await ReadLegacy77Frame(stream)
            .WaitAsync(TimeSpan.FromSeconds(3));
        Equal(NativeLoginGateProtocol.ProbeResponseIdent, probeResponse.Ident,
            "native probe response ident");
        Check(probeResponse.Payload.SequenceEqual(Convert.FromHexString(
                "4407000019FCFFFF4F0BBC1B7CDD600FB40001000000000000000000")),
            "native probe response GameGate route/zone bytes");
    }
    finally
    {
        service?.Stop();
        DBShare.sServerName = oldServerName;
        DBShare.nZoneIdx = oldZone;
        DBShare.nGroupIdx = oldGroup;
        DBShare.g_sPublicGateAddr = oldPublicGateAddress;
        DBShare.g_nPublicGatePort = oldPublicGatePort;
        File.Delete(iniPath);
    }
}

static async Task TestPrivateAdmissionDelivery()
{
    using var portLease = new TcpListener(IPAddress.Loopback, 0);
    portLease.Start();
    var port = ((IPEndPoint)portLease.LocalEndpoint).Port;
    portLease.Stop();
    var iniPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini");
    File.WriteAllText(iniPath,
        "[LoginGate]\r\nMode=PrivateListener\r\nIP=127.0.0.1\r\n" +
        $"Port={port}\r\n");

    var service = new LoginSvrService(new ConfigManager(iniPath));
    try
    {
        Equal(LoginGateTransportMode.PrivateListener, service.Mode,
            "private 5600 mode");
        Check(!service.TrySendSocketMsg(Grobal2.SS_OPENSESSION,
                "account/2/0/5/127.0.0.1"),
            "private admission reported delivery without an M2 peer");

        service.Start();
        using var peer = new TcpClient { NoDelay = true };
        await peer.ConnectAsync(IPAddress.Loopback, port)
            .WaitAsync(TimeSpan.FromSeconds(3));
        Check(SpinWait.SpinUntil(
                () => service.TrySendSocketMsg(Grobal2.SS_OPENSESSION,
                    "account/2/0/5/127.0.0.1"), 3000),
            "private admission was not delivered to the connected M2 peer");

        var buffer = new byte[256];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var count = await peer.GetStream().ReadAsync(buffer, timeout.Token);
        Equal("(100/account/2/0/5/127.0.0.1)",
            Encoding.ASCII.GetString(buffer, 0, count),
            "private admission wire payload");
    }
    finally
    {
        service.Stop();
        File.Delete(iniPath);
    }
}

static async Task TestIgnoredType17Runtime()
{
    using var dbListener = new TcpListener(IPAddress.Loopback, 0);
    using var gameListener = new TcpListener(IPAddress.Loopback, 0);
    dbListener.Start();
    gameListener.Start();
    var dbAccept = dbListener.AcceptTcpClientAsync();
    var gameAccept = gameListener.AcceptTcpClientAsync();
    using var hub = new SharedBackendHub(new GateConfig
    {
        BackendIP = "127.0.0.1",
        BackendPort2 = ((IPEndPoint)dbListener.LocalEndpoint).Port,
        GameBackendIP = "127.0.0.1",
        BackendPort = ((IPEndPoint)gameListener.LocalEndpoint).Port
    }, (_, _) => { });
    hub.Start();

    var opening = hub.OpenRouteAsync(117, "127.0.0.17", 1, () => { },
        CancellationToken.None);
    using var dbPeer = await dbAccept.WaitAsync(TimeSpan.FromSeconds(3));
    using var gamePeer = await gameAccept.WaitAsync(TimeSpan.FromSeconds(3));
    var route = await opening.WaitAsync(TimeSpan.FromSeconds(3));
    Check(route != null, "type17 runtime route open");
    await ReadPercentFrames(dbPeer.GetStream(), 1);
    await ReadInternalFrames(gamePeer.GetStream(), 1);

    var state = new[]
    {
        BuildInternalReply(117, Grobal2.GM_SERVERUSERINDEX, BitConverter.GetBytes(37)),
        BuildInternalReply(117, Grobal2.GM_DATA,
            new ClientPacket { Recog = 1701, Ident = Grobal2.SM_NEWMAP }.GetBuffer())
    }.SelectMany(frame => frame).ToArray();
    await gamePeer.GetStream().WriteAsync(state);
    await route!.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    await route.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));

    var ignored = BuildIgnoredLegacyType17(0xFFFFFFFF, 0x80000000,
        new byte[] { 0x41, 0x77, 0xBB, 0xAA, 0x33, 0x42 });
    var tail = BuildInternalReply(117, 0x4444, new byte[] { 0x55 });
    var sticky = ignored.Concat(tail).ToArray();
    await gamePeer.GetStream().WriteAsync(sticky.AsMemory(0, 7));
    await gamePeer.GetStream().WriteAsync(sticky.AsMemory(7));
    var aligned = await route.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    Equal((ushort)0x4444, aligned.Cmd, "type17 runtime following command");
    await Task.Delay(50);
    Check(!route.GameResponses.Reader.TryRead(out _),
        "type17 runtime produced a logical client response");
    Equal(1701, Volatile.Read(ref route.NativePlayerRecog),
        "type17 runtime changed player recog");
    Equal(37, Volatile.Read(ref route.NativeServerUserIndex),
        "type17 runtime changed server user index");

    await hub.CloseRouteAsync(route);
    await hub.StopAsync();
}

static async Task TestSharedBackendHub()
{
    using var dbListener = new TcpListener(IPAddress.Loopback, 0);
    using var gameListener = new TcpListener(IPAddress.Loopback, 0);
    dbListener.Start();
    gameListener.Start();
    var dbPort = ((IPEndPoint)dbListener.LocalEndpoint).Port;
    var gamePort = ((IPEndPoint)gameListener.LocalEndpoint).Port;
    var dbAccept = dbListener.AcceptTcpClientAsync();
    var gameAccept = gameListener.AcceptTcpClientAsync();
    using var hub = new SharedBackendHub(new GateConfig
    {
        BackendIP = "127.0.0.1",
        BackendPort2 = dbPort,
        GameBackendIP = "127.0.0.1",
        BackendPort = gamePort
    }, (_, _) => { });
    hub.Start();

    var route1AbortCount = 0;
    var route2AbortCount = 0;
    var open1 = hub.OpenRouteAsync(101, "127.0.0.1", 1,
        () => Interlocked.Increment(ref route1AbortCount),
        CancellationToken.None);
    using var dbPeer = await dbAccept.WaitAsync(TimeSpan.FromSeconds(3));
    using var gamePeer = await gameAccept.WaitAsync(TimeSpan.FromSeconds(3));
    var route1 = await open1.WaitAsync(TimeSpan.FromSeconds(3));
    Check(route1 != null, "first shared route");
    var route2 = await hub.OpenRouteAsync(102, "127.0.0.2", 2,
        () => Interlocked.Increment(ref route2AbortCount),
        CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
    Check(route2 != null, "second shared route");

    var dbOpens = await ReadPercentFrames(dbPeer.GetStream(), 2);
    Equal(2, dbOpens.Count, "DB logical open count");
    Check(dbOpens.Any(f => Encoding.ASCII.GetString(f).StartsWith("%O101/")),
        "DB route 101 open");
    Check(dbOpens.Any(f => Encoding.ASCII.GetString(f).StartsWith("%O102/")),
        "DB route 102 open");
    var gameOpens = await ReadInternalFrames(gamePeer.GetStream(), 2);
    Equal(2, gameOpens.Count, "Game logical open count");
    Check(gameOpens.Any(p => p.ConnID == 101 && p.Cmd == Grobal2.GM_OPEN),
        "Game route 101 open");
    Check(gameOpens.Any(p => p.ConnID == 102 && p.Cmd == Grobal2.GM_OPEN),
        "Game route 102 open");
    Check(gameOpens.All(p => (p.SeqID >> 16) == (p.ConnID & 0xFFFF)),
        "Game sequence high word carries native connection id");

    Check(await hub.SendGameHeartbeatOnceAsync(CancellationToken.None),
        "Game heartbeat send");
    var heartbeat = (await ReadInternalFrames(gamePeer.GetStream(), 1)).Single();
    Equal((uint)0, heartbeat.ConnID, "Game heartbeat connection id");
    Equal((ushort)Grobal2.GM_CHECKCLIENT, heartbeat.Cmd,
        "Game heartbeat direction");
    Equal(0, heartbeat.Payload.Length, "Game heartbeat payload");

    await gamePeer.GetStream().WriteAsync(BuildInternalReply(0,
        Grobal2.GM_RECEIVE_OK, Array.Empty<byte>()));
    var flowAck = (await ReadInternalFrames(gamePeer.GetStream(), 1)).Single();
    Equal((uint)0, flowAck.ConnID, "Game flow acknowledgement connection id");
    Equal((ushort)Grobal2.GM_RECEIVE_OK, flowAck.Cmd,
        "Game flow acknowledgement command");
    Equal(0, flowAck.Payload.Length, "Game flow acknowledgement payload");

    var dbReplies = Encoding.ASCII.GetBytes("%101/db-one$%102/db-two$");
    await dbPeer.GetStream().WriteAsync(dbReplies.AsMemory(0, 7));
    await dbPeer.GetStream().WriteAsync(dbReplies.AsMemory(7));
    var db1 = await route1!.DbResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    var db2 = await route2!.DbResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    Equal("%101/db-one$", Encoding.ASCII.GetString(db1), "DB response route 101");
    Equal("%102/db-two$", Encoding.ASCII.GetString(db2), "DB response route 102");

    var game1 = BuildInternalReply(101, 31, new byte[] { 1 });
    var game2 = BuildInternalReply(102, 32, new byte[] { 2, 3 });
    var gameReplies = game1.Concat(game2).ToArray();
    await gamePeer.GetStream().WriteAsync(gameReplies.AsMemory(0, 11));
    await gamePeer.GetStream().WriteAsync(gameReplies.AsMemory(11));
    var routed1 = await route1.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    var routed2 = await route2.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    Equal((uint)101, routed1.ConnID, "Game response route 101");
    Equal((ushort)31, routed1.Cmd, "Game response command 101");
    Equal((uint)102, routed2.ConnID, "Game response route 102");
    Equal((ushort)32, routed2.Cmd, "Game response command 102");

    var nativeStateFrames = new[]
    {
        BuildInternalReply(101, Grobal2.GM_SERVERUSERINDEX, BitConverter.GetBytes(17)),
        BuildInternalReply(102, Grobal2.GM_SERVERUSERINDEX, BitConverter.GetBytes(23)),
        BuildInternalReply(101, Grobal2.GM_DATA,
            new ClientPacket { Recog = 1001, Ident = Grobal2.SM_NEWMAP }.GetBuffer()),
        BuildInternalReply(102, Grobal2.GM_DATA,
            new ClientPacket { Recog = 1002, Ident = Grobal2.SM_NEWMAP }.GetBuffer())
    }.SelectMany(frame => frame).ToArray();
    await gamePeer.GetStream().WriteAsync(nativeStateFrames);
    await route1.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    await route2.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    await route1.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    await route2.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    Equal(1001, Volatile.Read(ref route1.NativePlayerRecog),
        "Game route 101 native player recog");
    Equal(17, Volatile.Read(ref route1.NativeServerUserIndex),
        "Game route 101 native server user index");
    Equal(1002, Volatile.Read(ref route2.NativePlayerRecog),
        "Game route 102 native player recog");
    Equal(23, Volatile.Read(ref route2.NativeServerUserIndex),
        "Game route 102 native server user index");

    var shopFinish = new LegacyGateType18
    {
        Recog = 0,
        Ident = 4497,
        FilterUserIndex = 0
    };
    Check(hub.TryDispatchLegacyType18(shopFinish),
        "legacy type18 broadcast had no eligible routes");
    var shopFinish1 = await route1.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    var shopFinish2 = await route2.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    CheckLegacyClientPacket(shopFinish1, 101, 0, 4497, Array.Empty<byte>(),
        "legacy type18 broadcast route 101");
    CheckLegacyClientPacket(shopFinish2, 102, 0, 4497, Array.Empty<byte>(),
        "legacy type18 broadcast route 102");

    var targetedText = Encoding.GetEncoding(936).GetBytes("商城刷新");
    var targeted = new LegacyGateType18
    {
        FilterUserIndex = 23,
        Recog = 7788,
        Ident = 100,
        Param = 0x38FF,
        TextBytes = targetedText
    };
    Check(hub.TryDispatchLegacyType18(targeted),
        "legacy type18 targeted route was not dispatched");
    var targeted2 = await route2.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    CheckLegacyClientPacket(targeted2, 102, 7788, 100,
        targetedText.Concat(new byte[] { 0 }).ToArray(),
        "legacy type18 targeted route 102");
    await Task.Delay(50);
    Check(!route1.GameResponses.Reader.TryRead(out _),
        "legacy type18 filter leaked to route 101");

    var largestRelay = new LegacyGateType18
    {
        Ident = 4497,
        TextBytes = new byte[LegacyGateType18.MaximumClientRelayLengthExclusive
            - LegacyGateType18.ClientRelayHeaderSize
            - LegacyGateType18.ClientPacketSize - 2]
    };
    Check(hub.TryDispatchLegacyType18(largestRelay),
        "largest native type18 client relay was dropped");
    var largestRelay1 = await route1.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    var largestRelay2 = await route2.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    Equal(LegacyGateType18.MaximumClientRelayLengthExclusive
          - LegacyGateType18.ClientRelayHeaderSize - 1,
        largestRelay1.Payload.Length, "largest relay route 101 payload");
    Equal(largestRelay1.Payload.Length, largestRelay2.Payload.Length,
        "largest relay route payload parity");

    var oversizedRelay = new LegacyGateType18
    {
        Ident = 4497,
        TextBytes = new byte[largestRelay.TextBytes.Length + 1]
    };
    Check(!hub.TryDispatchLegacyType18(oversizedRelay),
        "native type18 client relay accepted 0x8000-byte envelope");
    Check(!route1.GameResponses.Reader.TryRead(out _)
          && !route2.GameResponses.Reader.TryRead(out _),
        "oversized legacy type18 produced route packets");

    var rebindRoute1 = BuildInternalReply(101, Grobal2.GM_SERVERUSERINDEX,
        BitConverter.GetBytes(31));
    await gamePeer.GetStream().WriteAsync(rebindRoute1);
    await route1.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    Equal(31, Volatile.Read(ref route1.NativeServerUserIndex),
        "route 101 rebound native server user index");
    Equal(0, Volatile.Read(ref route1.NativePlayerRecog),
        "route 101 rebind did not clear native player recog");
    Check(hub.TryDispatchLegacyType18(shopFinish),
        "rebind eligibility did not retain route 102");
    var postRebindRoute2 = await route2.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    CheckLegacyClientPacket(postRebindRoute2, 102, 0, 4497, Array.Empty<byte>(),
        "route 102 after route 101 rebind");
    Check(!route1.GameResponses.Reader.TryRead(out _),
        "type18 leaked to route with cleared player recog");

    var restoreRoute1 = BuildInternalReply(101, Grobal2.GM_DATA,
        new ClientPacket { Recog = 2001, Ident = Grobal2.SM_NEWMAP }.GetBuffer());
    await gamePeer.GetStream().WriteAsync(restoreRoute1);
    await route1.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(3));
    Equal(2001, Volatile.Read(ref route1.NativePlayerRecog),
        "route 101 player recog after rebound new-map");

    var gameReconnectAccept = gameListener.AcceptTcpClientAsync();
    gamePeer.Close();
    Check(SpinWait.SpinUntil(
            () => Volatile.Read(ref route1AbortCount) == 1
                  && Volatile.Read(ref route2AbortCount) == 1, 3000),
        "Game disconnect did not abort established client routes");
    Check(route1.IsGameInvalidated && route2.IsGameInvalidated,
        "Game disconnect did not invalidate route generations");
    Check(!hub.TryGetRoute(101, 1, out _)
          && !hub.TryGetRoute(102, 2, out _),
        "invalidated Game routes remained available");
    Check(!await hub.SendGameAsync(route1, BuildInternalReply(101,
            Grobal2.GM_DATA, Array.Empty<byte>())),
        "invalidated Game route accepted upstream data");
    using var gamePeer2 = await gameReconnectAccept.WaitAsync(TimeSpan.FromSeconds(5));
    await Task.Delay(250);
    Check(!gamePeer2.GetStream().DataAvailable,
        "Game reconnect replayed stale logical opens");
    Equal(0, Volatile.Read(ref route1.NativePlayerRecog),
        "Game reconnect retained route 101 player recog");
    Equal(0, Volatile.Read(ref route1.NativeServerUserIndex),
        "Game reconnect retained route 101 server user index");
    Equal(0, Volatile.Read(ref route2.NativePlayerRecog),
        "Game reconnect retained route 102 player recog");
    Equal(0, Volatile.Read(ref route2.NativeServerUserIndex),
        "Game reconnect retained route 102 server user index");
    Check(!hub.TryDispatchLegacyType18(shopFinish),
        "legacy type18 crossed Game backend generation");

    await hub.CloseRouteAsync(route1);
    await hub.CloseRouteAsync(route2);
    await hub.StopAsync();
    await Task.Delay(50);
    Check(!dbListener.Pending(), "only one shared DB connection");
    Check(!gameListener.Pending(), "only one shared Game connection");
}

static async Task TestSharedBackendDbReconnect()
{
    using var dbListener = new TcpListener(IPAddress.Loopback, 0);
    using var gameListener = new TcpListener(IPAddress.Loopback, 0);
    dbListener.Start();
    gameListener.Start();
    var dbPort = ((IPEndPoint)dbListener.LocalEndpoint).Port;
    var gamePort = ((IPEndPoint)gameListener.LocalEndpoint).Port;
    var dbAccept = dbListener.AcceptTcpClientAsync();
    var gameAccept = gameListener.AcceptTcpClientAsync();
    using var hub = new SharedBackendHub(new GateConfig
    {
        BackendIP = "127.0.0.1",
        BackendPort2 = dbPort,
        GameBackendIP = "127.0.0.1",
        BackendPort = gamePort
    }, (_, _) => { });
    hub.Start();

    var abortCount = 0;
    var open = hub.OpenRouteAsync(201, "127.0.0.3", 3,
        () => Interlocked.Increment(ref abortCount), CancellationToken.None);
    using var dbPeer = await dbAccept.WaitAsync(TimeSpan.FromSeconds(3));
    using var gamePeer = await gameAccept.WaitAsync(TimeSpan.FromSeconds(3));
    var route = await open.WaitAsync(TimeSpan.FromSeconds(3));
    Check(route != null, "DB reconnect test route");
    await ReadPercentFrames(dbPeer.GetStream(), 1);
    await ReadInternalFrames(gamePeer.GetStream(), 1);

    var dbReconnectAccept = dbListener.AcceptTcpClientAsync();
    dbPeer.Close();
    Check(SpinWait.SpinUntil(() => Volatile.Read(ref abortCount) == 1, 3000),
        "DB disconnect did not abort the established client route");
    Check(route!.IsDbInvalidated && route.IsInvalidated,
        "DB disconnect did not invalidate the route generation");
    Check(!hub.TryGetRoute(201, 3, out _),
        "invalidated DB route remained available");
    Check(!await hub.SendDbAsync(route, Encoding.ASCII.GetBytes("%201/test$")),
        "invalidated DB route accepted upstream data");
    Check(!await hub.SendGameAsync(route, BuildInternalReply(201,
            Grobal2.GM_DATA, Array.Empty<byte>())),
        "DB-invalidated route accepted GameSvr data");

    using var dbPeer2 = await dbReconnectAccept.WaitAsync(TimeSpan.FromSeconds(5));
    await Task.Delay(250);
    Check(!dbPeer2.GetStream().DataAvailable,
        "DB reconnect replayed stale logical opens");

    await hub.CloseRouteAsync(route);
    await hub.StopAsync();
    await Task.Delay(50);
    Check(!dbListener.Pending(), "only one replacement shared DB connection");
    Check(!gameListener.Pending(), "only one shared Game connection in DB reconnect test");
}

static Task TestSessionPoolRelease()
{
    var sessions = new SessionManager(1);
    var first = sessions.Acquire("127.0.0.1", 7001);
    Check(first != null, "first pooled session acquired");
    var generation = first!.Generation;
    first.Account = "account";
    first.CharName = "character";
    first.HWID = "hardware";
    first.TcpClient = new TcpClient();
    first.BackendRouteId = 123;

    Check(sessions.Release(first.SessionId, generation), "pooled session released");
    Equal(0, sessions.ActiveCount, "pooled active count after release");
    Check(first.State == SessionState.FREE, "pooled session marked free");
    Check(first.Account == null && first.CharName == null && first.HWID == null,
        "pooled identity references cleared");
    Check(first.TcpClient == null && first.BackendRouteId == 0,
        "pooled network references cleared");
    Check(!sessions.Release(first.SessionId, generation), "stale duplicate release rejected");

    var second = sessions.Acquire("127.0.0.2", 7002);
    Check(ReferenceEquals(first, second), "pooled object reused");
    Check(second!.Generation != generation, "pooled generation advanced");
    Check(sessions.Get(second.SessionId, generation) == null,
        "stale generation cannot resolve reused session");
    Check(sessions.Release(second.SessionId, second.Generation), "reused session released");
    return Task.CompletedTask;
}

static Task TestDelayQueueBound()
{
    using var queue = new DelayQueue(60000);
    var payload = new byte[1024 * 1024];
    for (var i = 0; i < 17; i++)
        queue.Enqueue(new DelayedPacket { Data = payload, SessionId = i, Generation = i });
    Equal(16, queue.Count, "delayed packet count byte bound");
    Equal(16L * 1024 * 1024, queue.QueuedBytes, "delayed queue byte bound");
    queue.Dispose();
    Equal(0, queue.Count, "delayed queue count after dispose");
    Equal(0L, queue.QueuedBytes, "delayed queue bytes after dispose");
    queue.Enqueue(new DelayedPacket { Data = payload });
    Equal(0, queue.Count, "disposed delayed queue rejects enqueue");
    return Task.CompletedTask;
}

static byte[] BuildInternalReply(uint connId, ushort command, byte[] payload) =>
    new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = connId,
        SeqID = connId,
        FrameLen = (ushort)(InternalPacket77.HEADER_SIZE + payload.Length),
        Cmd = command,
        Field20 = (uint)payload.Length,
        Payload = payload
    }.ToBytes();

static byte[] BuildIgnoredLegacyType17(uint field4, uint field8, byte[] payload)
{
    var result = new byte[LegacyGateType18.HeaderSize + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4),
        InternalPacket77.MAGIC);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), field4);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), field8);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12, 2), 17);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14, 2),
        checked((ushort)payload.Length));
    payload.CopyTo(result, LegacyGateType18.HeaderSize);
    return result;
}

static void CheckLegacyClientPacket(InternalPacket77 packet, uint connId,
    int recog, ushort ident, byte[] expectedBody, string name)
{
    Equal(connId, packet.ConnID, name + " connection");
    Equal((ushort)Grobal2.GM_DATA, packet.Cmd, name + " command");
    Equal(ClientPacket.PackSize + expectedBody.Length, packet.Payload.Length,
        name + " payload length");
    Equal(recog, BitConverter.ToInt32(packet.Payload, 0), name + " recog");
    Equal(ident, BitConverter.ToUInt16(packet.Payload, 4), name + " ident");
    Check(packet.Payload.AsSpan(ClientPacket.PackSize).SequenceEqual(expectedBody),
        name + " body");
}

static async Task<List<byte[]>> ReadPercentFrames(NetworkStream stream, int expected)
{
    var parser = new PercentDollarFrameParser();
    var frames = new List<byte[]>();
    var buffer = new byte[256];
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while (frames.Count < expected)
    {
        var count = await stream.ReadAsync(buffer, timeout.Token);
        if (count <= 0) throw new EndOfStreamException();
        Check(parser.TryAppend(buffer, 0, count, out var parsed, out var error), error);
        frames.AddRange(parsed);
    }
    return frames;
}

static async Task<List<InternalPacket77>> ReadInternalFrames(NetworkStream stream, int expected)
{
    var parser = new InternalPacket77FrameParser();
    var frames = new List<InternalPacket77>();
    var buffer = new byte[256];
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while (frames.Count < expected)
    {
        var count = await stream.ReadAsync(buffer, timeout.Token);
        if (count <= 0) throw new EndOfStreamException();
        Check(parser.TryAppend(buffer, 0, count, out var parsed, out var error), error);
        frames.AddRange(parsed);
    }
    return frames;
}

static async Task TestClientSocketReconnect()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var client = new IClientScoket();
    var connected = NewSignal();
    var disconnected = NewSignal();
    var disconnectCount = 0;
    client.OnConnected += (_, _) => connected.TrySetResult();
    client.OnDisconnected += (_, _) =>
    {
        Interlocked.Increment(ref disconnectCount);
        disconnected.TrySetResult();
    };

    var accept = listener.AcceptTcpClientAsync();
    client.Connect("127.0.0.1", port);
    using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(3));
    await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));

    var payload = Enumerable.Range(0, 256 * 1024).Select(i => (byte)i).ToArray();
    var read = ReadExactly(peer.GetStream(), payload.Length);
    client.Send(payload);
    var received = await read.WaitAsync(TimeSpan.FromSeconds(5));
    Check(payload.AsSpan().SequenceEqual(received), "ordered full send");

    var queuedA = Enumerable.Repeat((byte)0xA5, 2 * 1024 * 1024).ToArray();
    var queuedB = Enumerable.Repeat((byte)0x3C, 4096).ToArray();
    var synchronous = Enumerable.Repeat((byte)0x5A, 4096).ToArray();
    var mixedRead = ReadExactly(peer.GetStream(),
        queuedA.Length + queuedB.Length + synchronous.Length);
    var queuedACompletion = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var queuedBCompletion = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    Check(client.QueueSend(queuedA, null,
        success => queuedACompletion.TrySetResult(success)),
        "first queued/synchronous serialization enqueue");
    Check(client.QueueSend(queuedB, null,
        success => queuedBCompletion.TrySetResult(success)),
        "second queued/synchronous serialization enqueue");
    var synchronousSend = Task.Run(() => client.Send(synchronous));
    var mixed = await mixedRead.WaitAsync(TimeSpan.FromSeconds(10));
    await synchronousSend.WaitAsync(TimeSpan.FromSeconds(10));
    Check(await queuedACompletion.Task.WaitAsync(TimeSpan.FromSeconds(10))
          && await queuedBCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10)),
        "queued/synchronous serialization completion");
    Check(mixed.AsSpan(0, queuedA.Length).SequenceEqual(queuedA)
          && mixed.AsSpan(queuedA.Length, queuedB.Length).SequenceEqual(queuedB)
          && mixed.AsSpan(queuedA.Length + queuedB.Length).SequenceEqual(synchronous),
        "mixed send APIs did not preserve submission FIFO");

    const System.Reflection.BindingFlags instanceFields =
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.NonPublic;
    var queuedState = typeof(IClientScoket).GetField("_queuedSendState",
            instanceFields)?.GetValue(client)
        ?? throw new MissingFieldException("IClientScoket._queuedSendState");
    var stateType = queuedState.GetType();
    var sendOrder = stateType.GetField("SendOrder", instanceFields)
        ?.GetValue(queuedState)
        ?? throw new MissingFieldException("QueuedSendState.SendOrder");
    var sendOrderType = sendOrder.GetType();
    var reserve = sendOrderType.GetMethod("Reserve", instanceFields)
        ?? throw new MissingMethodException("OrderedSendGate.Reserve");
    var complete = sendOrderType.GetMethod("Complete", instanceFields)
        ?? throw new MissingMethodException("OrderedSendGate.Complete");
    var servingField = sendOrderType.GetField("_serving", instanceFields)
        ?? throw new MissingFieldException("OrderedSendGate._serving");
    var queue = stateType.GetField("Queue", instanceFields)?.GetValue(queuedState)
        ?? throw new MissingFieldException("QueuedSendState.Queue");
    var syncRoot = stateType.GetField("SyncRoot", instanceFields)
        ?.GetValue(queuedState)
        ?? throw new MissingFieldException("QueuedSendState.SyncRoot");
    var isSending = stateType.GetField("IsSending", instanceFields)
        ?? throw new MissingFieldException("QueuedSendState.IsSending");
    var queueCount = queue.GetType().GetProperty("Count")
        ?? throw new MissingMemberException("QueuedSendState.Queue.Count");

    var callbackGateTicket = (long)(reserve.Invoke(sendOrder, null)
        ?? throw new InvalidOperationException("Reserve returned null"));
    var callbackQueued = Enumerable.Repeat((byte)0x17, 4096).ToArray();
    var callbackFollower = Enumerable.Repeat((byte)0x28, 4096).ToArray();
    var callbackSynchronous = Enumerable.Repeat((byte)0x39, 4096).ToArray();
    var callbackRead = ReadExactly(peer.GetStream(),
        callbackQueued.Length + callbackFollower.Length + callbackSynchronous.Length);
    var callbackCompletion = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var followerCompletion = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var callbackEnqueue = Task.Run(() => client.QueueSend(callbackQueued, null, success =>
        {
            if (success) client.Send(callbackSynchronous);
            callbackCompletion.TrySetResult(success);
        }));
    Check(SpinWait.SpinUntil(() =>
        {
            lock (syncRoot)
                return (bool)isSending.GetValue(queuedState)!
                       && (int)queueCount.GetValue(queue)! == 0;
        }, 3000), "reentrant completion send did not reach ticket wait");
    Check(client.QueueSend(callbackFollower, null,
        success => followerCompletion.TrySetResult(success)),
        "reentrant completion follower enqueue");
    complete.Invoke(sendOrder, new object[] { callbackGateTicket });
    Check(await callbackEnqueue.WaitAsync(TimeSpan.FromSeconds(5)),
        "reentrant completion send enqueue rejected");
    var callbackWire = await callbackRead.WaitAsync(TimeSpan.FromSeconds(5));
    Check(await callbackCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5))
          && await followerCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5)),
        "reentrant completion send did not finish");
    Check(callbackWire.AsSpan(0, callbackQueued.Length).SequenceEqual(callbackQueued)
          && callbackWire.AsSpan(callbackQueued.Length, callbackFollower.Length)
              .SequenceEqual(callbackFollower)
          && callbackWire.AsSpan(callbackQueued.Length + callbackFollower.Length)
              .SequenceEqual(callbackSynchronous),
        "reentrant completion send broke ticket order");

    Check(SpinWait.SpinUntil(() =>
        {
            lock (syncRoot)
                return !(bool)isSending.GetValue(queuedState)!
                       && (int)queueCount.GetValue(queue)! == 0;
        }, 3000), "queued sender did not become idle before publication fault");
    var queueArray = queue.GetType().GetField("_array", instanceFields)
        ?? throw new MissingFieldException("Queue._array");
    var originalQueueArray = queueArray.GetValue(queue);
    var publicationThrew = false;
    lock (syncRoot) queueArray.SetValue(queue, null);
    try
    {
        client.QueueSend(new byte[] { 0xFA }, null);
    }
    catch (NullReferenceException)
    {
        publicationThrew = true;
    }
    finally
    {
        lock (syncRoot) queueArray.SetValue(queue, originalQueueArray);
    }
    Check(publicationThrew, "queue publication fault was not injected");
    var recoveredPayload = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
    var recoveredRead = ReadExactly(peer.GetStream(), recoveredPayload.Length);
    var recoveredCompletion = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    Check(client.QueueSend(recoveredPayload, null,
        success => recoveredCompletion.TrySetResult(success)),
        "send after queue publication fault rejected");
    Check((await recoveredRead.WaitAsync(TimeSpan.FromSeconds(3)))
              .SequenceEqual(recoveredPayload)
          && await recoveredCompletion.Task.WaitAsync(TimeSpan.FromSeconds(3)),
        "queue publication fault left a ticket hole");

    var gateTicket = (long)(reserve.Invoke(sendOrder, null)
        ?? throw new InvalidOperationException("Reserve returned null"));
    var failedSendCompletion = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var failedSendCompletionCount = 0;
    var blockedEnqueue = Task.Run(() => client.QueueSend(
        new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, null, success =>
        {
            Interlocked.Increment(ref failedSendCompletionCount);
            failedSendCompletion.TrySetResult(success);
        }));

    var dequeueDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
    while (DateTime.UtcNow < dequeueDeadline)
    {
        var dequeued = false;
        lock (syncRoot)
            dequeued = (bool)isSending.GetValue(queuedState)!
                       && (int)queueCount.GetValue(queue)! == 0;
        if (dequeued) break;
        await Task.Delay(10);
    }
    lock (syncRoot)
        Check((bool)isSending.GetValue(queuedState)!
              && (int)queueCount.GetValue(queue)! == 0,
            "queued send did not reach the post-dequeue ticket wait");

    client.Disconnect();
    await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Equal(1, disconnectCount, "disconnect event count");
    complete.Invoke(sendOrder, new object[] { gateTicket });
    Check(await blockedEnqueue.WaitAsync(TimeSpan.FromSeconds(3)),
        "post-dequeue disconnect enqueue was rejected");
    Check(!await failedSendCompletion.Task.WaitAsync(TimeSpan.FromSeconds(3)),
        "post-dequeue disconnect reported a successful send");
    await Task.Delay(50);
    Equal(1, failedSendCompletionCount,
        "post-dequeue disconnect completion count");
    Equal(gateTicket + 2, (long)servingField.GetValue(sendOrder)!,
        "post-dequeue disconnect left the current ticket blocked");

    connected = NewSignal();
    disconnected = NewSignal();
    accept = listener.AcceptTcpClientAsync();
    client.Connect("127.0.0.1", port);
    using var peer2 = await accept.WaitAsync(TimeSpan.FromSeconds(3));
    await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var one = ReadExactly(peer2.GetStream(), 4);
    client.Send(new byte[] { 9, 8, 7, 6 });
    Check((await one).SequenceEqual(new byte[] { 9, 8, 7, 6 }), "send after reconnect");
    client.Disconnect();
}

static async Task TestServerSocketRestart()
{
    var portProbe = new TcpListener(IPAddress.Loopback, 0);
    portProbe.Start();
    var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
    portProbe.Stop();

    var server = new ISocketServer(2, 1024);
    server.Init();
    var connected = NewSignal();
    var disconnected = NewSignal();
    AsyncUserToken? connectedToken = null;
    server.OnClientConnect += (_, token) =>
    {
        connectedToken = token;
        connected.TrySetResult();
    };
    server.OnClientDisconnect += (_, _) =>
        throw new InvalidOperationException("intentional disconnect handler failure");
    server.OnClientDisconnect += (_, _) => disconnected.TrySetResult();

    server.Start("127.0.0.1", port);
    using (var client = new TcpClient())
    {
        await client.ConnectAsync(IPAddress.Loopback, port);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
    var firstToken = connectedToken;
    Check(firstToken != null, "first server connection token missing");
    await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
    server.Shutdown();

    connected = NewSignal();
    disconnected = NewSignal();
    connectedToken = null;
    server.Start("127.0.0.1", port);
    using (var client = new TcpClient())
    {
        await client.ConnectAsync(IPAddress.Loopback, port);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
    Check(connectedToken != null && !ReferenceEquals(firstToken, connectedToken),
        "server reused a mutable connection token across restart");
    await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
    server.Shutdown();
}

static async Task TestGameSendQueue()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var receiver = new TcpClient { ReceiveBufferSize = 1024 };
        var acceptTask = listener.AcceptSocketAsync();
        await receiver.ConnectAsync(IPAddress.Loopback, port);
        using var sender = await acceptTask;
        sender.SendBufferSize = 1024;

        var queue = new GameSvr.SendQueue(sender);
        var sendTask = Task.Run(queue.ProcessSendQueue);
        var payload = new byte[2 * 1024 * 1024];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 31);

        queue.AddToQueue(payload);
        var received = await ReadExactly(receiver.GetStream(), payload.Length)
            .WaitAsync(TimeSpan.FromSeconds(15));
        Check(payload.AsSpan().SequenceEqual(received), "queued socket write was truncated");

        const int producerCount = 8;
        const int framesPerProducer = 64;
        const int frameLength = 1024;
        var frameCount = producerCount * framesPerProducer;
        using var start = new ManualResetEventSlim(false);
        var concurrentRead = ReadExactly(receiver.GetStream(), frameCount * frameLength);
        var producers = Enumerable.Range(0, producerCount).Select(producer => Task.Run(() =>
        {
            start.Wait();
            for (var index = 0; index < framesPerProducer; index++)
            {
                var id = producer * framesPerProducer + index;
                var frame = new byte[frameLength];
                Array.Fill(frame, unchecked((byte)(id * 17 + 3)));
                BitConverter.TryWriteBytes(frame.AsSpan(0, 4), 0xC0DEC0DEu);
                BitConverter.TryWriteBytes(frame.AsSpan(4, 4), id);
                queue.AddToQueue(frame);
            }
        })).ToArray();
        start.Set();
        await Task.WhenAll(producers);
        var concurrentBytes = await concurrentRead.WaitAsync(TimeSpan.FromSeconds(15));
        var observed = new HashSet<int>();
        for (var offset = 0; offset < concurrentBytes.Length; offset += frameLength)
        {
            Equal(0xC0DEC0DEu, BitConverter.ToUInt32(concurrentBytes, offset),
                "concurrent queue frame boundary");
            var id = BitConverter.ToInt32(concurrentBytes, offset + 4);
            Check(id >= 0 && id < frameCount, "concurrent queue frame id");
            Check(observed.Add(id), "concurrent queue duplicate frame");
            var expected = unchecked((byte)(id * 17 + 3));
            Check(concurrentBytes.AsSpan(offset + 8, frameLength - 8)
                    .IndexOfAnyExcept(expected) < 0,
                "concurrent queue frame bytes interleaved");
        }
        Equal(frameCount, observed.Count, "concurrent queue frame count");

        queue.Stop();
        try { await sendTask.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) { }
    }
    finally
    {
        listener.Stop();
    }
}

static Task TestUnsupportedGameSocCommands()
{
    var type = typeof(GameSocService);
    Check(type.Assembly.GetType("DBSvr.Core.RenameManager", throwOnError: false) == null,
        "unproven RenameManager returned");
    var flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic;
    foreach (var name in new[]
             {
                 "RestoreCharacterRcd", "DeleteStorageRcd", "UnlockUserAccount",
                 "DB_RESTORECHRD", "DB_DELETESTORAGE", "DB_UNLOCKUSER",
                 "LoadStorageRcd", "SaveStorageRcd",
                 "LoadPetRcd", "SavePetRcd", "QueryZongpaiRcd", "UpdateZongpaiRcd",
                 "QueryTransferRcd", "UpsertTransferRcd",
                 "DB_LOADHERORCD", "DB_SAVEHERORCD", "DB_LOADSTORAGE", "DB_SAVESTORAGE",
                 "DB_LOADPET", "DB_SAVEPET", "DB_QUERYZONGPAI", "DB_UPDATEZONGPAI",
                 "DB_QUERYTRANSFER", "DB_UPSERTTRANSFER"
             })
    {
        Check(type.GetMethod(name, flags) == null && type.GetField(name, flags) == null,
            $"unproven GDM handler returned: {name}");
    }
    // ⚠️ `_zongpaiService` 已从 deny-list 移出并转为**正向**断言。
    // 它此前被当作\"未经原版证据支撑的依赖\"，那是本 deny-list 写于 0803 宗派逆向
    // **完成之前**的过时快照 —— 这条红是**假红**。
    //
    // 原版活性铁证（DBServer_repaired_20260803.exe，我已逐字复核）：
    //   type1 opcode 0x170 = 宗派/师门子协议
    //     索引：add eax,0xfffffe96 (= -0x16A) / cmp eax,0x34 / jmp [eax*4+0x598B23]
    //     TABLE B idx6 @0x598B3B -> 0x599206 -> 0x59C51C -> 0x594070
    //     一级派发 = 0x5940E0 的 14 字节 byteMap（值域 0..4）+ 0x5940EE 的 5 项 dword 表
    //     （0x594122 只是 group-1 的二级表；其 sub 0/9/10/11/12 五项经 byteMap 不可达 = 死项）
    //   启动路径无条件播种（连库成功门之后）：
    //     0x592103  cmp byte ptr [eax+0x10], 0      ; 连库成功门
    //     0x59210C  call 0x592D68                   ; 播种 zongpairole
    //     0x592114  call 0x5926EC                   ; 三表各一条 SELECT
    //   19 条活业务 SQL + 3 条 DDL（gamedata.ZongpaiBase / ZongpaiRole / ZongpaiMember）
    //
    // ⚠️ 三条 DDL 自身零 dword-ref，但**不据此判死**：本镜像里所有
    // `CREATE TABLE IF NOT EXISTS` 常量一律零引用，包括确定活着的 mir3.* 那批，
    // 故\"零 xref\"不携带死/活信息。表活性由上面的启动链独立证明。
    // DDL 如何送进 MySQL 仍标 UNPROVEN。
    Check(type.GetField("_zongpaiService", flags) != null,
        "native 0x170 zongpai dependency is missing (proven live: opcode 0x170 "
        + "dispatch @0x598B3B->0x599206->0x59C51C->0x594070, startup seeding "
        + "@0x59210C/0x592114, 19 live SQL + 3 DDL)");

    // `_transferService` 留在 deny-list：跨区传送侧尚未做同等级的活性举证。
    // （已知 [0x5E0A9C] 那个带锁 SQL 队列承载 IsTransLock/DesZoneId/TransferModal
    //   三条模板，但\"C# 这个字段对应原版哪条链\"未经核验，故不转正向断言。）
    Check(type.GetField("_transferService", flags) == null,
        "unproven GDM dependency returned: _transferService");
    Check(type.GetMethods(flags).Any(method => method.Name == "LoadHeroRcd")
          && type.GetMethods(flags).Any(method => method.Name == "SaveHeroRcd")
          && type.GetMethods(flags).Any(method => method.Name == "CreateHeroRcd")
          && type.GetMethods(flags).Any(method => method.Name == "DeleteHeroRcd"),
        "native 0x160/0x161/0x162/0x163 hero handlers are missing");
    Check(type.GetField("_heroDataService", flags) != null
          && type.GetField("_heroRecordService", flags) != null,
        "native hero database dependencies are missing");
    return Task.CompletedTask;
}

static Task TestNativeHeroSelection()
{
    var method = typeof(GameSocService).GetMethod("SelectHeroIndex",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("GameSocService.SelectHeroIndex");
    var ordinary = new HeroIndexInfo
    {
        HeroName = "ordinary", Job = 0, Consignation = 0, HeroType = 1
    };
    var consigned = new HeroIndexInfo
    {
        HeroName = "consigned", Job = 1, Consignation = 1, HeroType = 2
    };
    var special = new HeroIndexInfo
    {
        HeroName = "special", Job = byte.MaxValue, Consignation = 0, HeroType = 2
    };

    var normalRequest = new NativeHeroLoadRequest { HeroKind = 0, HeroSlot = 0 };
    var selected = method.Invoke(null,
        new object[] { new List<HeroIndexInfo> { ordinary }, normalRequest });
    Check(ReferenceEquals(selected, ordinary),
        "zero-parameter client summon did not select an ordinary hero");

    selected = method.Invoke(null,
        new object[] { new List<HeroIndexInfo> { consigned, ordinary }, normalRequest });
    Check(ReferenceEquals(selected, ordinary),
        "normal summon selected a consigned hero without a special sentinel");

    selected = method.Invoke(null,
        new object[] { new List<HeroIndexInfo> { consigned, special }, normalRequest });
    Check(ReferenceEquals(selected, consigned),
        "normal summon did not honor the special-hero override");

    var specialRequest = new NativeHeroLoadRequest { HeroKind = 1, HeroSlot = 2 };
    selected = method.Invoke(null,
        new object[] { new List<HeroIndexInfo> { ordinary, special }, specialRequest });
    Check(ReferenceEquals(selected, special),
        "HeroKind=1 did not select the Job=255 special hero");

    selected = method.Invoke(null,
        new object[] { new List<HeroIndexInfo> { ordinary }, specialRequest });
    Check(selected == null, "HeroKind=1 selected a hero without a Job=255 row");

    var deletedSpecial = new HeroIndexInfo
    {
        HeroName = "deleted-special", Job = byte.MaxValue, IsDelete = true
    };
    selected = method.Invoke(null,
        new object[] { new List<HeroIndexInfo> { consigned, deletedSpecial }, normalRequest });
    Check(selected == null, "a deleted Job=255 row relaxed normal hero selection");

    selected = method.Invoke(null,
        new object[]
        {
            new List<HeroIndexInfo> { ordinary, consigned, special }, normalRequest
        });
    Check(selected == null, "more than two active hero rows were silently truncated");
    return Task.CompletedTask;
}

static Task TestDatabase(string iniPath)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadLines(iniPath))
    {
        var parts = line.Split('=', 2);
        if (parts.Length == 2)
            values[parts[0].Trim()] = parts[1].Trim();
    }
    values.TryGetValue("DBUser", out var user);
    values.TryGetValue("DBPassword", out var password);
    user = string.IsNullOrEmpty(user) ? "root" : user;
    DBShare.DBConnection = $"server=127.0.0.1;uid={user};pwd={password};database=mir3;charset=latin1;Pooling=false;";

    var records = new MySqlPlayRecordService();
    var data = new MySqlPlayDataService();
    records.LoadQuickList();
    data.LoadQuickList();

    using var conn = new MySql.Data.MySqlClient.MySqlConnection(DBShare.DBConnection);
    conn.Open();
    using var cmd = new MySql.Data.MySqlClient.MySqlCommand(
        @"SELECT i.idx, HEX(i.ChrName), i.level, d.Data, d.ScriptData
          FROM mir3.user_index i
          INNER JOIN mir3.user_data d ON d.Idx=i.Idx
          WHERE i.IsDelete=0 AND d.Status=0 ORDER BY i.idx", conn);
    using var reader = cmd.ExecuteReader();
    var loadedCount = 0;
    var rejectedCount = 0;
    while (reader.Read())
    {
        var idx = reader.GetInt32(0);
        var nameBytes = Convert.FromHexString(reader.GetString(1));
        var name = Encoding.GetEncoding(936).GetString(nameBytes).TrimEnd('\0');
        Equal(idx, records.Index(name), "record index " + name);
        Equal(idx, data.Index(name), "data index " + name);
        THumDataInfo loaded = null!;
        var loadResult = data.Get(idx, ref loaded);
        if (loadResult == 1)
        {
            Check(!reader.IsDBNull(3), "readable record has Data blob " + name);
            Check(loaded?.Data?.Abil != null, "ability present " + name);
            Equal((ushort)reader.GetInt32(2), loaded!.Data.Abil.Level, "persisted level " + name);
            Equal(NativeHumanDataCodec.DataRecordSize, loaded.NativeData.Length,
                "native data raw length " + name);
            Check(NativeHumanDataCodec.TryDecodeRaw(
                    loaded.NativeData, loaded.NativeScriptData,
                    out var rawRoundTrip, out var rawError),
                "decode native raw save record " + name + ": " + rawError);
            Check(rawRoundTrip.NativeData.SequenceEqual(loaded.NativeData),
                "native raw save data identity " + name);
            Check(rawRoundTrip.NativeScriptData.SequenceEqual(loaded.NativeScriptData),
                "native raw save ScriptData identity " + name);
            Check(NativeHumanDataCodec.TryEncode(rawRoundTrip,
                    out var saveDataBlob, out var saveScriptBlob, out rawError),
                "encode native raw save record " + name + ": " + rawError);
            Check(NativeHumanDataCodec.TryDecode(saveDataBlob, saveScriptBlob,
                    out var persistedRoundTrip, out rawError),
                "decode persisted native save record " + name + ": " + rawError);
            Check(persistedRoundTrip.NativeData.SequenceEqual(loaded.NativeData),
                "persisted native save data identity " + name);
            Check(persistedRoundTrip.NativeScriptData.SequenceEqual(loaded.NativeScriptData),
                "persisted native save ScriptData identity " + name);
            Check((loaded.NativeScriptData?.Length ?? 0)
                  <= NativeDbServerProtocol.ScriptDataSlotSize,
                "native ScriptData fits selected-human slot " + name);
            Check(NativeDbServerProtocol.TryCreateLoadHumanFrame(
                    loaded.Data.sAccount, loaded.Data.sCharName,
                    loaded.NativeData, loaded.NativeScriptData,
                    new NativeHumanSessionContext(),
                    out var selectedFrame, out var selectedError),
                "build native selected-human frame " + name + ": " + selectedError);
            Equal(NativeDbServerProtocol.LoadHumanPayloadSize,
                selectedFrame.Payload.Length, "selected-human payload length " + name);
            Check(selectedFrame.Payload.AsSpan(NativeDbServerProtocol.NativeDataOffset,
                    NativeHumanDataCodec.DataRecordSize).SequenceEqual(loaded.NativeData),
                "selected-human native data bytes " + name);
            if (loaded.NativeScriptData != null)
                Check(selectedFrame.Payload.AsSpan(NativeDbServerProtocol.ScriptDataOffset,
                        loaded.NativeScriptData.Length).SequenceEqual(loaded.NativeScriptData),
                    "selected-human ScriptData bytes " + name);
            if (loaded.NativeScriptData != null)
            {
                // This probe used to poke ScriptData file-offset 0x52, calling it
                // a "hero state byte". Measured against the 31 real records the
                // original Delphi DBServer wrote (2026-08-13): section 0 starts at
                // file offset 4 and its payload at 11, and DecodeKeyValues proves
                // that payload is a strict array of 8-byte (int32 key, int32 value)
                // records. 0x52 = 82, so 82-11 = 71 = record 8, byte 7, i.e. the
                // MOST-SIGNIFICANT BYTE OF VALUE for key 1009 - not a state byte.
                // Every record has it as 0. Poking 0x03 there means "set script
                // variable 1009 to 0x03000000", which the encoder legitimately
                // canonicalises away when it rebuilds section 0 from ScriptV, so
                // the old assertion could only ever fail. There is no hero-state
                // byte at a fixed ScriptData offset; hero state lives in the
                // structured sections. Assert what the codec actually contracts:
                // the ScriptV key/value set survives an encode/decode round trip.
                var probeKey = loaded.Data.ScriptV != null && loaded.Data.ScriptV.Count > 0
                    ? loaded.Data.ScriptV.Keys.First()
                    : -1;
                Check(NativeHumanDataCodec.TryEncode(loaded,
                        out var stateData, out var stateScript, out var stateError),
                    "encode ScriptV round trip " + name + ": " + stateError);
                Check(NativeHumanDataCodec.TryDecode(stateData, stateScript,
                        out var stateRoundTrip, out stateError),
                    "decode ScriptV round trip " + name + ": " + stateError);
                if (probeKey != -1)
                    Equal(loaded.Data.ScriptV[probeKey],
                        stateRoundTrip.Data.ScriptV[probeKey],
                        "ScriptV value preserved for key " + probeKey + " " + name);
                Check(stateRoundTrip.NativeScriptData.SequenceEqual(loaded.NativeScriptData),
                    "ScriptData bytes preserved across round trip " + name);
            }
            loadedCount++;
        }
        else
        {
            Equal(-1, loadResult, "reject unknown/corrupt blob " + name);
            Check(loaded == null, "rejected blob must not synthesize level-1 data " + name);
            rejectedCount++;
        }
    }
    Check(loadedCount > 0, "live DB contains at least one valid native record");
    Console.WriteLine($"  live readable records={loadedCount}, rejected unknown/corrupt={rejectedCount}");
    return Task.CompletedTask;
}

static Task TestNativeDatabase(int port)
{
    const int equippedBase = 0x0F68;
    const int bagBase = 0x2BF6;
    const int storageBase = 0x52F6;
    const int magicBase = 0x06D0;
    var connectionString =
        $"server=127.0.0.1;port={port};uid=root;database=mir3_backup;charset=latin1;Pooling=false;SslMode=None;";
    using var conn = new MySql.Data.MySqlClient.MySqlConnection(connectionString);
    conn.Open();
    using var cmd = new MySql.Data.MySqlClient.MySqlCommand(
        @"SELECT d.Idx, d.Data, d.ScriptData, i.Level, i.Job, i.Sex, i.Exp
          FROM mir3_backup.user_data d
          INNER JOIN mir3_backup.user_index i ON i.Idx=d.Idx
          ORDER BY d.Idx", conn);
    using var reader = cmd.ExecuteReader();
    var count = 0;
    var expectedExp = new Dictionary<int, uint>();
    byte[]? firstBlob = null;
    byte[]? firstScript = null;
    while (reader.Read())
    {
        count++;
        var idx = Convert.ToInt32(reader[0]);
        expectedExp[idx] = Convert.ToUInt32(reader[6]);
        var dataBlob = (byte[])reader[1];
        var scriptBlob = reader.IsDBNull(2) ? null : (byte[])reader[2];
        firstBlob ??= (byte[])dataBlob.Clone();
        firstScript ??= scriptBlob == null ? null : (byte[])scriptBlob.Clone();
        Check(NativeHumanDataCodec.TryDecode(dataBlob, scriptBlob, out var human, out var error),
            $"decode idx={idx}: {error}");
        Equal(NativeHumanDataCodec.DataRecordSize, human.NativeData.Length, $"raw size idx={idx}");
        Equal(NativeHumanDataCodec.EquippedItemCount, human.Data.HumItems.Length, $"equipment slots idx={idx}");
        Equal(NativeHumanDataCodec.BagItemCount, human.Data.BagItems.Length, $"bag slots idx={idx}");
        Equal(NativeHumanDataCodec.StorageItemCount, human.Data.StorageItems.Length, $"storage slots idx={idx}");
        Equal(NativeHumanDataCodec.MagicCount, human.Data.Magic.Length, $"magic slots idx={idx}");
        Equal(Convert.ToUInt16(reader[3]), human.Data.Abil.Level, $"level idx={idx}");
        Equal(Convert.ToByte(reader[4]), human.Data.btJob, $"job idx={idx}");
        Equal(Convert.ToByte(reader[5]), human.Data.btSex, $"sex idx={idx}");
        Equal(Convert.ToUInt32(reader[6]), unchecked((uint)human.Data.Abil.Exp), $"exp idx={idx}");

        CheckItemRecords(human.Data.HumItems, human.NativeData, equippedBase,
            NativeHumanDataCodec.EquippedItemCount, $"equipment idx={idx}");
        CheckItemRecords(human.Data.BagItems, human.NativeData, bagBase,
            NativeHumanDataCodec.BagItemCount, $"bag idx={idx}");
        CheckItemRecords(human.Data.StorageItems, human.NativeData, storageBase,
            NativeHumanDataCodec.StorageItemCount, $"storage idx={idx}");
        for (var i = 0; i < NativeHumanDataCodec.MagicCount; i++)
        {
            var magic = human.Data.Magic[i];
            if (magic == null) continue;
            Check(magic.NativeRecord != null && magic.NativeRecord.AsSpan().SequenceEqual(
                    human.NativeData.AsSpan(magicBase + i * NativeHumanDataCodec.MagicRecordSize,
                        NativeHumanDataCodec.MagicRecordSize)),
                $"magic native tail idx={idx} slot={i}");
        }

        var originalData = (byte[])human.NativeData.Clone();
        var originalScript = human.NativeScriptData == null
            ? null
            : (byte[])human.NativeScriptData.Clone();
        Check(NativeHumanDataCodec.TryEncode(human, out var encodedData, out var encodedScript,
            out error), $"encode idx={idx}: {error}");
        Check(NativeHumanDataCodec.TryDecode(encodedData, encodedScript, out var roundTrip, out error),
            $"roundtrip decode idx={idx}: {error}");
        Check(originalData.AsSpan().SequenceEqual(roundTrip.NativeData), $"data byte roundtrip idx={idx}");
        Check((originalScript == null && roundTrip.NativeScriptData == null)
              || (originalScript != null && roundTrip.NativeScriptData != null
                  && originalScript.AsSpan().SequenceEqual(roundTrip.NativeScriptData)),
            $"script byte roundtrip idx={idx}");
    }
    reader.Close();
    Equal(144, count, "native record count");

    using (var schema = new MySql.Data.MySqlClient.MySqlCommand(
        "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='mir3'", conn))
    {
        if (Convert.ToInt32(schema.ExecuteScalar()) > 0)
        {
            DBShare.DBConnection = connectionString;
            var pageNames = new Dictionary<int, string>();
            using (var names = new MySql.Data.MySqlClient.MySqlCommand(
                       "SELECT Idx, HEX(ChrName) FROM mir3.user_index ORDER BY Idx", conn))
            using (var namesReader = names.ExecuteReader())
            {
                while (namesReader.Read())
                    pageNames[namesReader.GetInt32(0)] = LegacyGbkText.Decode(
                        Convert.FromHexString(namesReader.GetString(1)));
            }
            var recordService = new MySqlPlayRecordService();
            var page = recordService.GetCharacterPage(0, 5000);
            Equal(144, page.Count, "native character page count");
            foreach (var entry in page)
            {
                Equal(expectedExp[entry.Idx], unchecked((uint)entry.Exp),
                    $"native character page exp idx={entry.Idx}");
                Equal(pageNames[entry.Idx], entry.ChrName,
                    $"native character page GBK name idx={entry.Idx}");
            }
        }
    }

    Check(firstBlob != null, "native test source blob");
    var corrupt = (byte[])firstBlob!.Clone();
    corrupt[10] ^= 0x01;
    Check(!NativeHumanDataCodec.TryDecode(corrupt, firstScript, out _, out _),
        "corrupt native CRC must be rejected");

    var bound = new THumDataInfo();
    bound.Data.BagItems[0] = new TUserItem
    {
        MakeIndex = 0x12345678,
        wIndex = 1,
        Dura = 10,
        DuraMax = 20,
        Bind = 1
    };
    Check(NativeHumanDataCodec.TryEncode(bound, out var boundBlob, out _, out var bindError),
        $"bound native item encode: {bindError}");
    Equal((byte)1, bound.NativeData[0x2BF6 + 0xB8],
        "bound native bag item byte +0xB8");
    Check(NativeHumanDataCodec.TryDecode(boundBlob, null, out var boundRoundTrip,
        out bindError), $"bound native item decode: {bindError}");
    Equal((byte)1, boundRoundTrip.Data.BagItems[0].Bind,
        "bound native bag item roundtrip");
    return Task.CompletedTask;
}

static Task TestNativeWritableDatabase(int port)
{
    const int equippedBase = 0x0F68;
    var connectionString =
        $"server=127.0.0.1;port={port};uid=root;database=mir3;charset=latin1;Pooling=false;SslMode=None;";
    byte[]? originalData = null;
    byte[]? originalScript = null;
    var originalIndex = new Dictionary<string, object?>();
    var idx = 0;

    using var conn = new MySql.Data.MySqlClient.MySqlConnection(connectionString);
    conn.Open();
    using (var select = new MySql.Data.MySqlClient.MySqlCommand(
        @"SELECT d.Idx, d.Data, d.ScriptData,
                 i.Level, i.Exp, i.Job, i.Sex, i.ForceLv, i.ForceExp,
                 i.FightPoints, i.sfLevel, i.ApprenticeNum, i.HeroCardLv,
                 i.PlatinaChrLv, i.ModifyDate
          FROM mir3.user_data d
          INNER JOIN mir3.user_index i ON i.Idx=d.Idx
          WHERE d.Status=0 AND d.Data IS NOT NULL
          ORDER BY d.Idx LIMIT 1", conn))
    using (var reader = select.ExecuteReader())
    {
        Check(reader.Read(), "writable native source row");
        idx = Convert.ToInt32(reader["Idx"]);
        originalData = ((byte[])reader["Data"]).ToArray();
        originalScript = reader.IsDBNull(reader.GetOrdinal("ScriptData"))
            ? null
            : ((byte[])reader["ScriptData"]).ToArray();
        foreach (var field in new[]
        {
            "Level", "Exp", "Job", "Sex", "ForceLv", "ForceExp", "FightPoints",
            "sfLevel", "ApprenticeNum", "HeroCardLv", "PlatinaChrLv", "ModifyDate"
        })
            originalIndex[field] = reader.IsDBNull(reader.GetOrdinal(field))
                ? null
                : reader[field];
    }

    try
    {
        var expectedUnmapped = new Dictionary<string, object?>
        {
            ["ApprenticeNum"] = 17,
            ["HeroCardLv"] = 23,
            ["PlatinaChrLv"] = 29
        };
        using (var seed = new MySql.Data.MySqlClient.MySqlCommand(
            @"UPDATE mir3.user_index SET ApprenticeNum=17, HeroCardLv=23,
                     PlatinaChrLv=29 WHERE Idx=@idx", conn))
        {
            seed.Parameters.AddWithValue("@idx", idx);
            Equal(1, seed.ExecuteNonQuery(), "seed unmapped native index fields");
        }

        DBShare.DBConnection = connectionString;
        var dataService = new MySqlPlayDataService();
        var recordService = new MySqlPlayRecordService();
        dataService.LoadQuickList();
        recordService.LoadQuickList();

        THumDataInfo loaded = null!;
        Equal(1, dataService.Get(idx, ref loaded), "writable source load");
        Check(loaded?.Data?.Abil != null, "writable source ability");
        var equipment = loaded!.NativeData.AsSpan(equippedBase,
            NativeHumanDataCodec.EquippedItemCount * NativeHumanDataCodec.ItemRecordSize).ToArray();
        var newLevel = loaded.Data.Abil.Level == ushort.MaxValue
            ? (ushort)(ushort.MaxValue - 1)
            : (ushort)(loaded.Data.Abil.Level + 1);
        const uint newExpBits = 0xF1234567u;
        loaded.Data.Abil.Level = newLevel;
        loaded.Data.Abil.Exp = unchecked((int)newExpBits);

        var serialized = ProtoBufDecoder.Serialize(loaded);
        Check(serialized != null, "native protobuf save envelope");
        var decodedForSave = ProtoBufDecoder.DeSerialize<THumDataInfo>(serialized);
        Check(NativeHumanDataCodec.TryEncode(decodedForSave, out _, out _, out var saveError),
            "native protobuf codec preflight: " + saveError);
        Check(dataService.SaveBlob(idx, serialized), "native blob service save");
        Check(dataService.SaveBlob(idx, serialized), "idempotent native blob service save");
        Check(recordService.UpdateCharIndex(idx, newLevel, loaded.Data.Abil.Exp,
                loaded.Data.btJob, loaded.Data.btSex, loaded.Data.ForceLv,
                loaded.Data.ForceExp, loaded.Data.FightPoints, loaded.Data.sfLevel),
            "native index service save");

        var reloadedService = new MySqlPlayDataService();
        reloadedService.LoadQuickList();
        THumDataInfo reloaded = null!;
        Equal(1, reloadedService.Get(idx, ref reloaded), "native service reload");
        Equal(newLevel, reloaded.Data.Abil.Level, "saved level survives relogin");
        Equal(newExpBits, unchecked((uint)reloaded.Data.Abil.Exp),
            "saved experience survives relogin");
        Check(equipment.AsSpan().SequenceEqual(reloaded.NativeData.AsSpan(equippedBase,
                NativeHumanDataCodec.EquippedItemCount * NativeHumanDataCodec.ItemRecordSize)),
            "equipment bytes survive character save");
        Check((loaded.NativeScriptData == null && reloaded.NativeScriptData == null)
              || (loaded.NativeScriptData != null && reloaded.NativeScriptData != null
                  && loaded.NativeScriptData.AsSpan().SequenceEqual(reloaded.NativeScriptData)),
            "script data survives character save");

        using var verify = new MySql.Data.MySqlClient.MySqlCommand(
            @"SELECT ApprenticeNum, HeroCardLv, PlatinaChrLv
              FROM mir3.user_index WHERE Idx=@idx", conn);
        verify.Parameters.AddWithValue("@idx", idx);
        using var after = verify.ExecuteReader();
        Check(after.Read(), "saved index row still exists");
        foreach (var field in new[] { "ApprenticeNum", "HeroCardLv", "PlatinaChrLv" })
        {
            var value = after.IsDBNull(after.GetOrdinal(field)) ? null : after[field];
            Check(DatabaseValuesEqual(expectedUnmapped[field], value),
                $"unmapped index field preserved: {field}");
        }
    }
    finally
    {
        var restoreDataSql = originalScript == null
            ? "UPDATE mir3.user_data SET Data=UNHEX(@data), ScriptData=NULL WHERE Idx=@idx"
            : "UPDATE mir3.user_data SET Data=UNHEX(@data), ScriptData=UNHEX(@script) WHERE Idx=@idx";
        using (var restoreData = new MySql.Data.MySqlClient.MySqlCommand(restoreDataSql, conn))
        {
            restoreData.Parameters.Add("@data", MySql.Data.MySqlClient.MySqlDbType.LongText)
                .Value = Convert.ToHexString(originalData!);
            if (originalScript != null)
                restoreData.Parameters.Add("@script", MySql.Data.MySqlClient.MySqlDbType.LongText)
                    .Value = Convert.ToHexString(originalScript);
            restoreData.Parameters.AddWithValue("@idx", idx);
            Equal(1, restoreData.ExecuteNonQuery(), "restore native data row");
        }

        using var restoreIndex = new MySql.Data.MySqlClient.MySqlCommand(
            @"UPDATE mir3.user_index SET Level=@Level, Exp=@Exp, Job=@Job, Sex=@Sex,
                     ForceLv=@ForceLv, ForceExp=@ForceExp, FightPoints=@FightPoints,
                     sfLevel=@sfLevel, ApprenticeNum=@ApprenticeNum,
                     HeroCardLv=@HeroCardLv, PlatinaChrLv=@PlatinaChrLv,
                     ModifyDate=@ModifyDate
              WHERE Idx=@idx", conn);
        foreach (var pair in originalIndex)
            restoreIndex.Parameters.AddWithValue("@" + pair.Key, pair.Value ?? DBNull.Value);
        restoreIndex.Parameters.AddWithValue("@idx", idx);
        Equal(1, restoreIndex.ExecuteNonQuery(), "restore native index row");
    }

    using var restored = new MySql.Data.MySqlClient.MySqlCommand(
        "SELECT Data, ScriptData FROM mir3.user_data WHERE Idx=@idx", conn);
    restored.Parameters.AddWithValue("@idx", idx);
    using var restoredReader = restored.ExecuteReader();
    Check(restoredReader.Read(), "restored native row exists");
    Check(originalData.AsSpan().SequenceEqual((byte[])restoredReader["Data"]),
        "native data restored byte-exact");
    var restoredScript = restoredReader.IsDBNull(restoredReader.GetOrdinal("ScriptData"))
        ? null
        : (byte[])restoredReader["ScriptData"];
    Check((originalScript == null && restoredScript == null)
          || (originalScript != null && restoredScript != null
              && originalScript.AsSpan().SequenceEqual(restoredScript)),
        "native script restored byte-exact");
    return Task.CompletedTask;
}

static Task TestNativeCleanup(int port)
{
    var connectionString =
        $"server=127.0.0.1;port={port};uid=root;database=mir3;charset=latin1;Pooling=false;SslMode=None;";
    using var conn = new MySql.Data.MySqlClient.MySqlConnection(connectionString);
    conn.Open();

    long Count(string sql)
    {
        using var command = new MySql.Data.MySqlClient.MySqlCommand(sql, conn);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    const string stale = "NOW()>DATE_ADD(ModifyDate, INTERVAL 15 DAY)";
    var nativeTargets = Count(
        $"SELECT COUNT(*) FROM mir3.user_index WHERE Level<8 AND {stale}");
    var protectedTargets = Count(
        $"SELECT COUNT(*) FROM mir3.user_index WHERE Level>=8 AND Level<30 AND {stale}");
    var beforeIndex = Count("SELECT COUNT(*) FROM mir3.user_index");
    var beforeData = Count("SELECT COUNT(*) FROM mir3.user_data");

    Check(nativeTargets > 0, "cleanup clone must contain native targets");
    Check(protectedTargets > 0, "cleanup clone must distinguish the old level-30 bug");

    var cleanup = new CleanupService(connectionString);
    Equal(nativeTargets, (long)cleanup.CleanInactiveCharacters(), "native cleanup deleted count");
    Equal(0L, Count($"SELECT COUNT(*) FROM mir3.user_index WHERE Level<8 AND {stale}"),
        "native cleanup target rows remain");
    Equal(protectedTargets,
        Count($"SELECT COUNT(*) FROM mir3.user_index WHERE Level>=8 AND Level<30 AND {stale}"),
        "level 8-29 rows must be preserved");
    Equal(beforeIndex - nativeTargets, Count("SELECT COUNT(*) FROM mir3.user_index"),
        "cleanup index row boundary");
    Equal(beforeData - nativeTargets, Count("SELECT COUNT(*) FROM mir3.user_data"),
        "cleanup data row boundary");
    return Task.CompletedTask;
}

static bool DatabaseValuesEqual(object? expected, object? actual)
{
    if (expected == null || actual == null) return expected == null && actual == null;
    if (expected is IConvertible && actual is IConvertible)
        return Convert.ToDecimal(expected) == Convert.ToDecimal(actual);
    return Equals(expected, actual);
}

static void CheckItemRecords(TUserItem[] items, byte[] raw, int offset, int count, string area)
{
    for (var i = 0; i < count; i++)
    {
        var item = items[i];
        if (item == null) continue;
        Check(item.NativeRecord != null && item.NativeRecord.AsSpan().SequenceEqual(
                raw.AsSpan(offset + i * NativeHumanDataCodec.ItemRecordSize,
                    NativeHumanDataCodec.ItemRecordSize)),
            $"{area} native tail slot={i}");
    }
}

static byte[] BuildRequest(int id, byte[] payload)
{
    var packet = new RequestServerPacket
    {
        QueryId = id,
        Message = new byte[] { 1 },
        Packet = payload,
        CheckKey = EDcode.EncodeBuffer(BitConverter.GetBytes(HUtil32.MakeLong(id ^ 170, payload.Length + 7)))
    };
    return packet.GetBuffer();
}

static async Task<byte[]> ReadExactly(NetworkStream stream, int length)
{
    var result = new byte[length];
    var offset = 0;
    while (offset < length)
    {
        var read = await stream.ReadAsync(result.AsMemory(offset));
        if (read <= 0) throw new EndOfStreamException();
        offset += read;
    }
    return result;
}

static async Task<YbDbLegacy77Frame> ReadLegacy77Frame(NetworkStream stream)
{
    var header = await ReadExactly(stream, YbDbLegacy77Codec.HeaderSize);
    var payloadLength = BitConverter.ToUInt16(header, 14);
    var payload = await ReadExactly(stream, payloadLength);
    var wire = new byte[header.Length + payload.Length];
    header.CopyTo(wire, 0);
    payload.CopyTo(wire, header.Length);
    Check(YbDbLegacy77Codec.TryDecode(wire, out var frame, out var error), error);
    return frame;
}

static TaskCompletionSource NewSignal() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected={expected}, actual={actual}");
}
