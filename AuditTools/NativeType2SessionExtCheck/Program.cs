// Asserts the native TYPE_B (type2) command 0x0177 byte contract against the 战神
// DBServer disassembly: dispatcher sub_599860 (je 0x599AEE), gate 0x5AD298,
// ack builder 0x59C8E8. Constants are quoted from instruction addresses in
// DBServer_unpacked.exe.
// Evidence: staging/dbsvr_type2_type3_dispatch_census_20260803.md.
using System.Buffers.Binary;
using DBSvr.Core;
using SystemModule.Packet;

var failures = new List<string>();
Run("command words and ack sizes", CommandWords);
Run("identity and cookie offsets", IdentityOffsets);
Run("blob captured verbatim", BlobCaptured);
Run("ack echoes identity dwords", AckEchoes);
Run("short record refused", ShortRecordRefused);
Run("malformed frames rejected", MalformedFrames);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeType2SessionExtCheck PASS tests=6 "
                  + "type2=0177 ack=013A total=0x54 payload=0x48 "
                  + "id@0x14 cookie@0x18 echo@body+8/+C");
return 0;

void CommandWords()
{
    // sub_599860: `cmp eax,0x177 / je 0x599AEE`.
    Equal(0x0177, NativeType2SessionExtProtocol.RequestCommand, "request 0177");
    // 0x59C944-style `mov word [body], 0x13A`.
    Equal(0x013A, NativeType2SessionExtProtocol.ResponseCommand, "ack 013A");
    // 0x59C8F1 `mov [ebp-0xC], 0x54`; 0x59C931 `mov [buf+8], 0x48`.
    Equal(0x54, NativeType2SessionExtProtocol.AckTotalLength, "ack total 0x54");
    Equal(0x48, NativeType2SessionExtProtocol.AckPayloadLength,
        "ack payload 0x48");
    // 0x0C header + 0x48 payload == 0x54.
    Equal(NativeType2SessionExtProtocol.AckTotalLength,
        LegacyDbServerFrameCodec.HeaderSize
        + NativeType2SessionExtProtocol.AckPayloadLength, "0x0C+0x48==0x54");
}

void IdentityOffsets()
{
    // Gate 0x5AD298 pushes [rec+0x18] then [rec+0x14]; the ack action 0x599B05
    // pushes the same pair. record+0x14 is the int identity, +0x18 the cookie.
    var frame = Record(r =>
    {
        BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(0x14, 4), 0x0BADF00D);
        BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(0x18, 4), 0x1337C0DE);
    });
    True(NativeType2SessionExtProtocol.TryDecodeRequest(frame, out var req,
        out var err), "decode: " + err);
    Equal(0x0BADF00D, req.Identity, "identity @rec+0x14");
    Equal(0x1337C0DE, req.Cookie, "cookie @rec+0x18");
}

void BlobCaptured()
{
    // 0x5AD298 copies the record into THumanInfo+0x7C; the C# store keeps the
    // whole record as the blob (including the command word at bytes 0-1).
    var frame = Record(r =>
    {
        for (var i = 2; i < r.Length; i++) r[i] = (byte)(i ^ 0x5A);
    }, 0x40);
    True(NativeType2SessionExtProtocol.TryDecodeRequest(frame, out var req,
        out var err), "decode: " + err);
    Equal(0x40, req.Blob.Length, "blob length == record length");
    Equal(0x0177, BinaryPrimitives.ReadUInt16LittleEndian(req.Blob),
        "blob keeps command word");
    for (var i = 2; i < 0x40; i++)
        Equal((byte)(i ^ 0x5A), req.Blob[i], $"blob byte {i}");
    // The blob must be a copy, not an alias of the frame payload.
    req.Blob[2] = 0xFF;
    Equal((byte)(2 ^ 0x5A), frame.Payload[2], "blob is a copy");
}

void AckEchoes()
{
    // 0x59C94F `[body+8]=arg1`, 0x59C955 `[body+0xC]=arg2`; body command at
    // body+0. body+2/+4 stay zero (the frame is memset at 0x59C908).
    var frame = Record(r =>
    {
        BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(0x14, 4), 0x11112222);
        BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(0x18, 4), 0x33334444);
    });
    True(NativeType2SessionExtProtocol.TryDecodeRequest(frame, out var req,
        out _), "decode");
    var ack = NativeType2SessionExtProtocol.CreateAck(req);
    Equal(1, ack.Type, "ack type 1");
    Equal(0x48, ack.Payload.Length, "ack payload 0x48");
    Equal(0x013A, BinaryPrimitives.ReadUInt16LittleEndian(ack.Payload),
        "ack command @body+0");
    Equal(0x11112222, BinaryPrimitives.ReadInt32LittleEndian(
        ack.Payload.AsSpan(8, 4)), "identity @body+8");
    Equal(0x33334444, BinaryPrimitives.ReadInt32LittleEndian(
        ack.Payload.AsSpan(0xC, 4)), "cookie @body+0xC");
    // body+2 and body+4 are untouched by the original.
    Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ack.Payload.AsSpan(2, 2)),
        "body+2 zero");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(ack.Payload.AsSpan(4, 4)),
        "body+4 zero");
}

void ShortRecordRefused()
{
    // Fields at 0x14/0x18 require at least 0x1C bytes.
    var frame = new LegacyDbServerFrame(2, 0, MakeHeader(0x1B));
    False(NativeType2SessionExtProtocol.TryDecodeRequest(frame, out _, out _),
        "0x1B record refused");
    var ok = new LegacyDbServerFrame(2, 0, MakeHeader(0x1C));
    True(NativeType2SessionExtProtocol.TryDecodeRequest(ok, out _, out _),
        "0x1C record accepted");
}

void MalformedFrames()
{
    False(NativeType2SessionExtProtocol.TryDecodeRequest(null, out _, out _),
        "null frame");
    var wrong = MakeHeader(0x40);
    BinaryPrimitives.WriteUInt16LittleEndian(wrong, 0x0176);
    False(NativeType2SessionExtProtocol.TryDecodeRequest(
            new LegacyDbServerFrame(2, 0, wrong), out _, out _),
        "wrong command");
}

// ---- helpers ----

byte[] MakeHeader(int length)
{
    var buf = new byte[length];
    if (length >= 2)
        BinaryPrimitives.WriteUInt16LittleEndian(buf,
            NativeType2SessionExtProtocol.RequestCommand);
    return buf;
}

LegacyDbServerFrame Record(Action<byte[]> fill, int length = 0x20)
{
    var buf = MakeHeader(length);
    fill?.Invoke(buf);
    return new LegacyDbServerFrame(2, 0, buf);
}

void Run(string name, Action test)
{
    try { test(); }
    catch (Exception ex) { failures.Add($"FAIL [{name}] {ex.Message}"); }
}

void Equal(int expected, int actual, string what)
{
    if (expected != actual)
        throw new Exception($"{what}: expected {expected} (0x{expected:X}), "
                            + $"got {actual} (0x{actual:X})");
}

void True(bool condition, string what)
{
    if (!condition) throw new Exception($"{what}: expected true");
}

void False(bool condition, string what)
{
    if (condition) throw new Exception($"{what}: expected false");
}
