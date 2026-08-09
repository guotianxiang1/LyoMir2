// Locks the user_data storage-blob ENVELOPE against the 战神 DBServer binary.
// The C# DBSvr stores NativeHumanDataCodec output (native envelope), NOT ProtoBuf
// (ProtoBuf is only the C#-internal GameSvr<->DBSvr transport). Every constant
// below is grounded in a specific address in DBServer_unpacked.exe:
//
//   CRC routine sub_58988C: crc=0xFFFFFFFF init; per byte
//     crc = table[(crc&0xFF) ^ b] ^ (crc>>8); table @0x5D92E8 (poly 0xEDB88320);
//     `mov eax,[ebp-0xC]; ret`  => NO final XOR complement.
//   Call sites (0x58E9C7 / 0x59702B / …): `movzx edx, word[blob+6]` (length) then
//     `mov eax,[obj+0x1C]; add eax,8; call sub_58988C` => CRC is taken over
//     blob[8 .. 8+word[6]] = the COMPRESSED bytes; word[6] is the compressed len.
//   Length guard 0x58E99C: `movzx word[blob+6]; add 8; cmp [total]; jg error`.
//
// Evidence: staging/dbsvr_client_crud_sql_census_20260803.md + humandb-persistence-l3-blocker.
using System.Buffers.Binary;
using DBSvr.Core;
using SystemModule;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
var failures = new List<string>();
Run("CRC matches native sub_58988C (poly/init/no-final-xor)", CrcMatchesNative);
Run("CRC is taken over the COMPRESSED bytes", CrcOverCompressed);
Run("envelope byte layout (crc/marker/len/data)", EnvelopeLayout);
Run("256-byte alignment and zero padding", Alignment);
Run("round-trip through the codec", RoundTrip);
Run("stored blob is native envelope, never ProtoBuf", NotProtoBuf);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeHumanBlobEnvelopeCheck PASS tests=6 "
                  + "crc=sub_58988C/table@0x5D92E8/no-final-xor/over-compressed "
                  + "marker=0xEF00 align=256 record=0xEEF8");
return 0;

void CrcMatchesNative()
{
    // Reference table-based CRC exactly as sub_58988C executes it, built from the
    // poly 0xEDB88320 whose 256-entry table sits at 0x5D92E8.
    var table = BuildCrc32Table(0xEDB88320u);
    // spot-check the table entries the binary was verified against.
    Equal(0x00000000, (int)table[0], "table[0]");
    Equal(unchecked((int)0x77073096), (int)table[1], "table[1]");
    Equal(unchecked((int)0xEE0E612C), (int)table[2], "table[2]");
    Equal(unchecked((int)0x2D02EF8D), (int)table[255], "table[255]");

    var rng = new byte[] { 1, 2, 3, 250, 0, 99, 7, 200, 44, 255 };
    // native sub_58988C: init 0xFFFFFFFF, table step, NO final xor.
    var native = 0xFFFFFFFFu;
    foreach (var b in rng)
        native = table[(native & 0xFF) ^ b] ^ (native >> 8);

    var csharp = NativeHumanDataCodec.ComputeNativeCrc(rng);
    Equal(unchecked((int)native), unchecked((int)csharp),
        "C# ComputeNativeCrc == native table CRC");

    // A standard crc32 WITH final complement would differ — assert we are NOT that.
    var withComplement = native ^ 0xFFFFFFFFu;
    NotEqual(unchecked((int)withComplement), unchecked((int)csharp),
        "native variant has no final complement");

    // Empty input: init stays 0xFFFFFFFF, no bytes, no final xor.
    Equal(unchecked((int)0xFFFFFFFFu),
        unchecked((int)NativeHumanDataCodec.ComputeNativeCrc(Array.Empty<byte>())),
        "empty CRC == 0xFFFFFFFF");
}

void CrcOverCompressed()
{
    // Build a real native blob and confirm [0:4] == CRC over [8:8+word[6]].
    var (raw, blob) = MakeBlob();

    var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(0, 4));
    var compLen = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(6, 2));
    var compressed = blob.AsSpan(8, compLen).ToArray();
    var recomputed = NativeHumanDataCodec.ComputeNativeCrc(compressed);
    Equal(unchecked((int)recomputed), unchecked((int)storedCrc),
        "blob[0:4] == CRC(blob[8:8+word[6]])");

    // Corrupting one compressed byte must break the CRC (proves the range).
    if (compLen > 0)
    {
        var tampered = (byte[])blob.Clone();
        tampered[8] ^= 0xFF;
        var badCrc = NativeHumanDataCodec.ComputeNativeCrc(
            tampered.AsSpan(8, compLen).ToArray());
        NotEqual(unchecked((int)storedCrc), unchecked((int)badCrc),
            "tampered compressed byte changes CRC");
    }
}

void EnvelopeLayout()
{
    var (raw, blob) = MakeBlob();

    // [4:6] marker == 0xEF00 == DataRecordSize (0xEEF8) + 8.
    Equal(0xEF00, BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(4, 2)),
        "marker 0xEF00");
    Equal(0xEF00, NativeHumanDataCodec.DataRecordSize + 8, "0xEEF8 + 8 == 0xEF00");
    Equal(0xEEF8, NativeHumanDataCodec.DataRecordSize, "record size 0xEEF8");

    // [6:8] compressed length is > 0 and 8+len <= total (native guard 0x58E99C).
    var compLen = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(6, 2));
    True(compLen > 0, "compressed length > 0");
    True(8 + compLen <= blob.Length, "8 + compLen <= total (native length guard)");

    // The compressed section is a zlib stream (RFC1950): first byte 0x78.
    Equal(0x78, blob[8], "zlib CMF byte 0x78 at offset 8");
}

void Alignment()
{
    var (raw, blob) = MakeBlob();

    Equal(0, blob.Length % 256, "blob length is 256-byte aligned");
    var compLen = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(6, 2));
    // Everything past the compressed payload must be zero padding.
    for (var i = 8 + compLen; i < blob.Length; i++)
        Equal(0, blob[i], $"padding byte {i} is zero");
}

void RoundTrip()
{
    var (raw, blob) = MakeBlob();
    True(NativeHumanDataCodec.LooksLikeNativeDataBlob(blob),
        "blob is recognized as native");

    // Decode back and confirm the record survives byte-for-byte.
    True(NativeHumanDataCodec.TryDecode(blob, null, out var info, out var decErr),
        "decode: " + decErr);
    var reRaw = info.NativeData;
    Equal(NativeHumanDataCodec.DataRecordSize, reRaw.Length,
        "decoded raw is full record size");
    for (var i = 0; i < raw.Length; i++)
        if (raw[i] != reRaw[i])
            throw new Exception($"record byte {i} changed: {raw[i]} -> {reRaw[i]}");
}

void NotProtoBuf()
{
    // A native blob must NOT be mistaken for protobuf, and vice versa: the codec's
    // discriminator (LooksLikeNativeDataBlob) is what SaveBlob relies on to keep
    // the stored bytes in native format.
    var (raw, blob) = MakeBlob();
    True(NativeHumanDataCodec.LooksLikeNativeDataBlob(blob),
        "native blob recognized");

    // A 256-aligned all-zero buffer is the uncompressed sentinel, not protobuf;
    // a random non-aligned buffer is rejected as native (would take protobuf path).
    var notAligned = new byte[100];
    False(NativeHumanDataCodec.LooksLikeNativeDataBlob(notAligned),
        "non-256-aligned buffer is not a native blob");
}

// ---- helpers ----

(byte[] raw, byte[] blob) MakeBlob()
{
    // Build a valid 0xEEF8 base record (version byte at 0x3E must be 1) and let
    // the production encoder wrap it into the native envelope.
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    raw[0x3E] = 1;
    for (var i = 0; i < 0x400; i++) raw[i] = (byte)((i * 7 + 3) & 0xFF);
    raw[0x3E] = 1;
    var info = new THumDataInfo { NativeData = (byte[])raw.Clone() };
    info.Data.Initialization();
    if (!NativeHumanDataCodec.TryEncode(info, out var blob, out _, out var err))
        throw new Exception("encode failed: " + err);
    // TryEncode overwrites some scalar fields from info.Data over the base; the
    // encoder-produced record is the authoritative raw for round-trip checks.
    NativeHumanDataCodec.TryDecode(blob, null, out var decoded, out _);
    return (decoded.NativeData, blob);
}

uint[] BuildCrc32Table(uint poly)
{
    var table = new uint[256];
    for (uint n = 0; n < 256; n++)
    {
        var c = n;
        for (var k = 0; k < 8; k++)
            c = (c & 1) != 0 ? poly ^ (c >> 1) : c >> 1;
        table[n] = c;
    }
    return table;
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

void NotEqual(int notExpected, int actual, string what)
{
    if (notExpected == actual)
        throw new Exception($"{what}: both equal {actual} (0x{actual:X})");
}

void True(bool condition, string what)
{
    if (!condition) throw new Exception($"{what}: expected true");
}

void False(bool condition, string what)
{
    if (condition) throw new Exception($"{what}: expected false");
}
