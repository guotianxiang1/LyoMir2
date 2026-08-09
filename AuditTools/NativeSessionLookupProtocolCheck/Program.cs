// Asserts the native type1 0x0151 byte contract against the 战神 DBServer
// disassembly (handler 0x598C1B, gate 0x5A1A40, reply builder 0x59A0FC).
// Every constant is quoted from an instruction address in DBServer_unpacked.exe.
// Evidence: staging/dbsvr_type1_dispatch_census_20260803.md §3.
using System.Buffers.Binary;
using DBSvr.Core;
using SystemModule.Packet;

var failures = new List<string>();
Run("command words and frame sizes", CommandWords);
Run("header field slots", HeaderFieldSlots);
Run("reply framing 0x54/0x48", ReplyFraming);
Run("reply flag branch", ReplyFlagBranch);
Run("key folding is ASCII-only", KeyFolding);
Run("malformed frames rejected", MalformedFrames);
Run("sibling twin 0x0063 layout agrees", SiblingTwinLayout);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeSessionLookupProtocolCheck PASS tests=7 "
                  + "type1=0151 reply=0054 total=0x54 payload=0x48 "
                  + "slots=0x10/0x25/0x35 gate=0x5A1A40");
return 0;

void CommandWords()
{
    // Comparison chain 0x598A61 routes 0x151 to 0x598C1B.
    Equal(0x0151, NativeSessionLookupProtocol.RequestCommand, "request command");
    // 0x59A155 writes 0x54 as the reply body command word.
    Equal(0x0054, NativeSessionLookupProtocol.ResponseCommand, "reply command");
    // 0x59DDAC splits the record at 0x48.
    Equal(0x48, NativeSessionLookupProtocol.HeaderSize, "header size");
    // 0x59A10F allocates 0x54 total.
    Equal(0x54, NativeSessionLookupProtocol.ReplyTotalLength, "reply total");
}

void HeaderFieldSlots()
{
    // 0x598C1E/0x598C30/0x598C42 read header+0x25, +0x35, +0x10 (the dispatcher
    // copies the header base to [ebp-0x10] at 0x5988D8); 0x598C53 reads word@+2.
    var frame = Header(h =>
    {
        PutShortString(h, 0x10, "LookupName");
        PutShortString(h, 0x25, "slot25");
        PutShortString(h, 0x35, "slot35");
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(0x02, 2), 0x1234);
    });
    True(NativeSessionLookupProtocol.TryDecodeRequest(frame, out var req,
        out var err), "decode: " + err);
    EqualText("LookupName", Text(req.LookupName), "header+0x10");
    EqualText("slot25", Text(req.Slot25), "header+0x25");
    EqualText("slot35", Text(req.Slot35), "header+0x35");
    Equal(0x1234, req.Selector, "header+0x02 word");

    // Capacities: 0x59A184 uses cl=0x14 for the +0x10 slot, 0x59A1AA cl=0x0F
    // for the +0x25 slot.
    var wideOk = Header(h => h[0x10] = 0x14);
    True(NativeSessionLookupProtocol.TryDecodeRequest(wideOk, out _, out _),
        "+0x10 at 20 accepted");
    var wideOver = Header(h => h[0x10] = 0x15);
    False(NativeSessionLookupProtocol.TryDecodeRequest(wideOver, out _, out _),
        "+0x10 at 21 refused");
    var narrowOver = Header(h => h[0x25] = 0x10);
    False(NativeSessionLookupProtocol.TryDecodeRequest(narrowOver, out _, out _),
        "+0x25 at 16 refused");
}

void ReplyFraming()
{
    // 0x59A130 magic, 0x59A139 type=1, 0x59A142 payload=0x48, body=buf+0x0C,
    // 0x59A155 cmd=0x54, 0x59A161 word@body+2 echoes the request word,
    // 0x59A186 name → ShortString20 @body+0x10,
    // 0x59A1AC second name → ShortString15 @body+0x25.
    var frame = Header(h =>
    {
        PutShortString(h, 0x10, "charname");
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(0x02, 2), 0xABCD);
    });
    True(NativeSessionLookupProtocol.TryDecodeRequest(frame, out var req,
        out var err), "decode: " + err);

    var second = System.Text.Encoding.ASCII.GetBytes("account");
    var reply = NativeSessionLookupProtocol.CreateResponse(req, second, false);
    Equal(1, reply.Type, "reply type");
    Equal(0x48, reply.Payload.Length, "reply payload length");
    Equal(0x54, reply.Payload.Length + LegacyDbServerFrameCodec.HeaderSize,
        "reply total length");
    Equal(0x0054, BinaryPrimitives.ReadUInt16LittleEndian(reply.Payload),
        "reply body command");
    Equal(0xABCD, BinaryPrimitives.ReadUInt16LittleEndian(
        reply.Payload.AsSpan(2, 2)), "reply echoes word@+2");
    EqualText("charname", Text(ReadShortString(reply.Payload, 0x10)),
        "reply name @body+0x10");
    EqualText("account", Text(ReadShortString(reply.Payload, 0x25)),
        "reply second name @body+0x25");
}

void ReplyFlagBranch()
{
    // 0x59A1B1 `cmp byte [ebp+8],0 / je` — only a non-zero argument writes
    // [body+4]=1 (0x59A1BA). The 0x151 miss path pushes 0 at 0x598C6B, so the
    // dword stays zero there.
    var frame = Header(h => PutShortString(h, 0x10, "n"));
    True(NativeSessionLookupProtocol.TryDecodeRequest(frame, out var req,
        out var err), "decode: " + err);

    var noFlag = NativeSessionLookupProtocol.CreateResponse(
        req, Array.Empty<byte>(), false);
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(noFlag.Payload.AsSpan(4, 4)),
        "flag=false leaves body+4 zero");

    var withFlag = NativeSessionLookupProtocol.CreateResponse(
        req, Array.Empty<byte>(), true);
    Equal(1, BinaryPrimitives.ReadInt32LittleEndian(
        withFlag.Payload.AsSpan(4, 4)), "flag=true writes body+4=1");
}

void KeyFolding()
{
    // 0x40AEF4: `cmp 0x41 / cmp 0x5A / add 0x20` folds ASCII A-Z only.
    True(NativeSessionLookupProtocol.KeyEquals(
        Ascii("HeroName"), Ascii("heroname")), "ASCII case-insensitive");
    True(NativeSessionLookupProtocol.KeyEquals(Ascii("ABC"), Ascii("abc")),
        "all upper vs all lower");
    False(NativeSessionLookupProtocol.KeyEquals(Ascii("abc"), Ascii("abd")),
        "different names differ");
    False(NativeSessionLookupProtocol.KeyEquals(Ascii("abc"), Ascii("ab")),
        "length differs");

    // Bytes outside A-Z must NOT be folded, so GBK trail bytes that happen to
    // sit 0x20 apart stay distinct.
    False(NativeSessionLookupProtocol.KeyEquals(
        new byte[] { 0xB0, 0xA1 }, new byte[] { 0xB0, 0xC1 }), "GBK not folded");
    True(NativeSessionLookupProtocol.KeyEquals(
        new byte[] { 0xD5, 0xBD }, new byte[] { 0xD5, 0xBD }), "GBK equal");
    // '_' (0x5F) is just past 'Z' (0x5A) and must be left alone.
    False(NativeSessionLookupProtocol.KeyEquals(
        new byte[] { 0x5F }, new byte[] { 0x7F }), "0x5F not folded");
}

void MalformedFrames()
{
    False(NativeSessionLookupProtocol.TryDecodeRequest(null, out _, out _),
        "null frame");
    False(NativeSessionLookupProtocol.TryDecodeRequest(
            new LegacyDbServerFrame(1, 0, new byte[0x47]), out _, out _),
        "short header");
    var wrong = new byte[0x48];
    BinaryPrimitives.WriteUInt16LittleEndian(wrong, 0x0152);
    False(NativeSessionLookupProtocol.TryDecodeRequest(
            new LegacyDbServerFrame(1, 0, wrong), out _, out _),
        "wrong command");
}

void SiblingTwinLayout()
{
    // 0x59C6AC builds the same body template with command 0x63 instead of 0x54,
    // and the already-shipped C# account-storage path encodes that twin. Its
    // constants must therefore agree with the layout asserted above; if they ever
    // diverge, one of the two is wrong.
    Equal(0x48, NativeAccountStorageProtocol.HeaderSize, "twin header size");
    Equal(0x0063, NativeAccountStorageProtocol.SaveResponseCommand,
        "twin save command");
    Equal(NativeSessionLookupProtocol.HeaderSize,
        NativeAccountStorageProtocol.HeaderSize, "header sizes agree");
}

// ---- helpers ----

LegacyDbServerFrame Header(Action<byte[]> fill)
{
    var header = new byte[NativeSessionLookupProtocol.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(header,
        NativeSessionLookupProtocol.RequestCommand);
    fill?.Invoke(header);
    return new LegacyDbServerFrame(1, 0, header);
}

byte[] Ascii(string value) => System.Text.Encoding.ASCII.GetBytes(value);

void PutShortString(byte[] buffer, int offset, string value)
{
    var bytes = System.Text.Encoding.ASCII.GetBytes(value);
    buffer[offset] = (byte)bytes.Length;
    bytes.CopyTo(buffer, offset + 1);
}

byte[] ReadShortString(byte[] buffer, int offset)
{
    var length = buffer[offset];
    return buffer.AsSpan(offset + 1, length).ToArray();
}

string Text(byte[] bytes) => System.Text.Encoding.ASCII.GetString(bytes ?? []);

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

void EqualText(string expected, string actual, string what)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new Exception($"{what}: expected '{expected}', got '{actual}'");
}

void True(bool condition, string what)
{
    if (!condition) throw new Exception($"{what}: expected true");
}

void False(bool condition, string what)
{
    if (condition) throw new Exception($"{what}: expected false");
}
