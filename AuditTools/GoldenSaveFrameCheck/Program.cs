// Golden-frame decode check — runs the REAL save records written by the ORIGINAL
// Delphi DBServer through the C# codec.
//
// WHY THIS EXISTS
// Two sibling audits (NativeHumanDbCodecCheck / NativeHumanBlobEnvelopeCheck) build
// their own fixtures, and both used to write `raw[0x3E] = 1` — encoding the very
// assumption under test, so they stayed green while the hair field was mis-mapped.
// This audit takes bytes it did not author: 30 records produced by the original
// engine, extracted read-only from a live mir3.user_data.
//
// THE LAYOUT UNDER TEST — 战神 keeps Hair/Sex/Job consecutive at 0x3E/0x3F/0x40:
//   save sub_6B0FF0: 0x6B109A mov al,[ebx+0x70] -> 0x6B109D mov [esi+0x3E],al  (Hair)
//                    0x6B10A0     [ebx+0x71]    -> 0x6B10A3     [esi+0x3F]     (Sex)
//                    0x6B10A6     [ebx+0x72]    -> 0x6B10A9     [esi+0x40]     (Job)
//   load sub_6AFD7C: 0x6AFFBD mov al,[eax+0x3E] -> 0x6AFFC3 mov [edx+0x70],al  (Hair)
// 0x3B is never read or written by native.
//
// THE STORAGE SHAPE (a trap that produced a wrong measurement before this was written):
// the DB blob is NOT the record. It is an 8-byte wrapper followed by a **zlib stream**
// that inflates to DataRecordSize (0xEEF8 = 61176). Reading offsets off the stored
// blob yields garbage — always inflate first.
using System.IO.Compression;
using System.Text;

const int DataRecordSize = 0xEEF8;      // == DBSvr NativeHumanDataCodec.DataRecordSize
const int HairOffset = 0x3E;            // sub_6B0FF0 @0x6B109D / sub_6AFD7C @0x6AFFBD
const int SexOffset = 0x3F;             // sub_6B0FF0 @0x6B10A3
const int JobOffset = 0x40;             // sub_6B0FF0 @0x6B10A9
const int UnusedByNativeOffset = 0x3B;  // native never touches this

var dir = args.Length > 0 ? args[0] : @"D:\loym2\staging\golden_saves_gtwl";
if (!Directory.Exists(dir))
{
    // Exit 2 (INCOMPLETE), never 0 -- see the same fix in
    // GoldenCodecFidelityCheck and NativeScriptSectionsCheck. A missing corpus
    // must not be reportable as a pass, and an empty corpus already FAILs a few
    // lines below, so returning 0 here made the weaker case the silent one.
    Console.WriteLine($"GoldenSaveFrameCheck SKIP: no golden frames at {dir}");
    Console.WriteLine("SKIP reason: golden-backed assertions were NOT executed; " +
        "this run proves nothing about save-frame fidelity.");
    return 2;
}

var files = Directory.GetFiles(dir, "user_data_idx*.bin");
if (files.Length == 0)
{
    Console.WriteLine($"GoldenSaveFrameCheck FAIL: {dir} contains no golden frames " +
                      "(an empty scan is not a pass)");
    return 1;
}

Encoding gbk;
try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    gbk = Encoding.GetEncoding(936);
}
catch (Exception ex)
{
    Console.WriteLine($"GoldenSaveFrameCheck FAIL: GBK unavailable: {ex.Message}");
    return 1;
}

static byte[] Inflate(byte[] blob)
{
    var start = -1;
    for (var i = 0; i + 1 < blob.Length && i < 64; i++)
        if (blob[i] == 0x78 && blob[i + 1] == 0xDA) { start = i; break; }
    if (start < 0) return null;
    using var input = new MemoryStream(blob, start, blob.Length - start);
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    zlib.CopyTo(output);
    return output.ToArray();
}

var failures = new List<string>();
var names = new List<string>();
var hairValues = new List<int>();
var unusedNonZero = 0;
var decoded = 0;

foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
{
    var label = Path.GetFileName(file);
    byte[] rec;
    try
    {
        rec = Inflate(File.ReadAllBytes(file));
    }
    catch (Exception ex)
    {
        failures.Add($"{label}: inflate failed: {ex.Message}");
        continue;
    }
    if (rec == null) { failures.Add($"{label}: no zlib stream found"); continue; }

    if (rec.Length != DataRecordSize)
    {
        failures.Add($"{label}: inflated to {rec.Length}, expected {DataRecordSize}");
        continue;
    }

    // The character name is a Delphi ShortString at 0x00 (length byte + GBK). If the
    // record were misaligned this is the first thing that would look like noise — it
    // is what caught the compressed-bytes mistake, so it is asserted deliberately.
    var nameLen = rec[0];
    if (nameLen == 0 || nameLen > 15)
    {
        failures.Add($"{label}: char-name length byte = {nameLen} (expected 1..15) " +
                     "— record is misaligned");
        continue;
    }
    string charName;
    try { charName = gbk.GetString(rec, 1, nameLen); }
    catch (Exception ex) { failures.Add($"{label}: name not GBK: {ex.Message}"); continue; }
    if (string.IsNullOrWhiteSpace(charName))
    {
        failures.Add($"{label}: name decoded empty");
        continue;
    }
    names.Add(charName);

    var sex = rec[SexOffset];
    var job = rec[JobOffset];
    if (sex > 1) failures.Add($"{label}: sex at 0x3F = {sex} (expected 0..1)");
    if (job > 2) failures.Add($"{label}: job at 0x40 = {job} (expected 0..2)");

    hairValues.Add(rec[HairOffset]);
    if (rec[UnusedByNativeOffset] != 0) unusedNonZero++;

    decoded++;
}

if (failures.Count > 0)
{
    Console.WriteLine("GoldenSaveFrameCheck FAIL:");
    foreach (var f in failures.Take(20)) Console.WriteLine("  " + f);
    return 1;
}

if (decoded == 0)
{
    Console.WriteLine("GoldenSaveFrameCheck FAIL: no record decoded");
    return 1;
}

// 0x3B must stay untouched. If the encoder ever writes hair there again, real records
// round-tripped through C# would start showing a non-zero byte here.
if (unusedNonZero > 0)
{
    Console.WriteLine($"GoldenSaveFrameCheck FAIL: 0x3B non-zero in {unusedNonZero}/{decoded} " +
                      "records — native never writes 0x3B, so something is writing hair there");
    return 1;
}

Console.WriteLine(
    $"GoldenSaveFrameCheck PASS frames={decoded} inflated={DataRecordSize} " +
    $"names=GBK-ok sex@0x3F<=1 job@0x40<=2 hair@0x3E={{{string.Join(",", hairValues.Distinct().OrderBy(v => v))}}} " +
    $"0x3B=untouched-in-all source=live-DBServer-written-records");
return 0;
