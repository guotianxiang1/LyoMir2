// Golden-record codec fidelity — runs the 30 REAL save blobs written by the ORIGINAL
// Delphi DBServer through the ACTUAL DBSvr.Core.NativeHumanDataCodec (TryDecode/TryEncode).
//
// WHY THIS EXISTS (and why it is not a duplicate of the three sibling audits)
//   * NativeHumanDbCodecCheck   exercises the codec, but on a SELF-AUTHORED fixture
//                               (raw[0x3E]=1) — it can only prove the code is internally
//                               consistent, never that it matches the original engine.
//   * GoldenSaveFrameCheck      reads the real bytes, but does its OWN inline inflate and
//                               offset reads — it never calls the codec at all.
//   * M2NativeDbGoldenFrameCheck is the hero DB *frame protocol*, not the human record.
//
// The migration the user just took on ("现在全部由你接管": DBSvr is now C#) has one
// showstopper this audit is built to catch: if the C# codec cannot DECODE a blob the
// original Delphi DBServer wrote, every character fails to load on day one. No synthetic
// fixture can prove the negative; only the original engine's own bytes can.
//
// TWO independent assertions per record:
//  (1) DECODE + GROUND TRUTH — TryDecode must succeed, and the decoded Sex/Job/Level must
//      equal the authoritative values read from live mir3.user_index (30/30/30 confirmed
//      out-of-band; the table below is those columns). This is the semantic proof, not
//      just "didn't throw".
//  (2) ROUND-TRIP FIDELITY — decode -> encode -> inflate must reproduce the ORIGINAL
//      inflated 61176 bytes EXACTLY. The codec is patch-over-clone (TryEncode starts from
//      NativeData.Clone()), so any field that is WRITTEN from an un-decoded DTO member
//      would overwrite a good original byte with a default — the classic "field zeroed
//      every login" bug. A byte-exact round-trip on real data is the only thing that
//      surfaces it; a synthetic fixture leaves those offsets 0 and hides it.
//
// STORAGE SHAPE: mir3.user_data.Data = 8-byte wrapper (CRC32/EDB88320 + 0xEF00 marker +
// zlib length) followed by a zlib stream (0x78 0xDA) that inflates to 0xEEF8 = 61176.
using System.IO.Compression;
using System.Text;
using DBSvr.Core;

const int DataRecordSize = 0xEEF8;  // 61176
const int HairOffset = 0x3E;
const int UnusedByNativeOffset = 0x3B;

// idx -> (sex@0x3F, job@0x40, level@0x3C). These are the mir3.user_index columns for the
// same 30 characters; each matched the inflated record 30/30 out-of-band. Pure integers
// on purpose — character names are GBK and MUST NOT be embedded in source (encoding
// discipline); the name is instead asserted structurally (length + GBK round-trip).
var groundTruth = new Dictionary<string, (int Sex, int Job, int Level)>
{
    ["1"] = (0, 1, 1),   ["2"] = (1, 0, 65),  ["3"] = (0, 0, 65),  ["4"] = (1, 0, 66),
    ["5"] = (0, 2, 1),   ["6"] = (0, 0, 65),  ["7"] = (0, 0, 65),  ["8"] = (1, 0, 94),
    ["9"] = (0, 0, 65),  ["10"] = (1, 2, 65), ["11"] = (0, 1, 65), ["12"] = (0, 1, 65),
    ["14"] = (0, 2, 65), ["16"] = (1, 0, 65), ["17"] = (0, 1, 65), ["18"] = (0, 0, 65),
    ["20"] = (0, 0, 65), ["22"] = (0, 0, 73), ["23"] = (0, 2, 65), ["24"] = (1, 0, 65),
    ["25"] = (1, 2, 65), ["26"] = (0, 0, 65), ["27"] = (0, 1, 65), ["28"] = (0, 0, 65),
    ["29"] = (1, 0, 65), ["30"] = (1, 2, 65), ["31"] = (1, 2, 67), ["32"] = (0, 1, 65),
    ["33"] = (1, 0, 65), ["34"] = (0, 0, 65),
};

var dir = args.Length > 0 ? args[0] : @"D:\loym2\staging\golden_saves_gtwl";
if (!Directory.Exists(dir))
{
    // Exit 2 (INCOMPLETE), never 0. Returning 0 here made a missing corpus
    // indistinguishable from a passing run: the runner recorded PASS for a run
    // in which not one golden-backed assertion executed. Note the old shape was
    // also self-inconsistent -- an EMPTY corpus directory already FAILed a few
    // lines below, while a MISSING one rendered green, i.e. the weaker case was
    // the silent one.
    Console.WriteLine($"GoldenCodecFidelityCheck SKIP: no golden frames at {dir}");
    Console.WriteLine("SKIP reason: golden-backed assertions were NOT executed; " +
        "this run proves nothing about codec fidelity.");
    return 2;
}

var files = Directory.GetFiles(dir, "user_data_idx*.bin");
if (files.Length == 0)
{
    Console.WriteLine($"GoldenCodecFidelityCheck FAIL: {dir} contains no golden frames " +
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
    Console.WriteLine($"GoldenCodecFidelityCheck FAIL: GBK unavailable: {ex.Message}");
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
    using var output = new MemoryStream(DataRecordSize);
    zlib.CopyTo(output);
    return output.ToArray();
}

var failures = new List<string>();
var decoded = 0;
var roundTripped = 0;
var hairValues = new List<int>();

foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
{
    var name = Path.GetFileName(file);
    var idx = name.Replace("user_data_idx", string.Empty).Replace(".bin", string.Empty);

    byte[] blob;
    try { blob = File.ReadAllBytes(file); }
    catch (Exception ex) { failures.Add($"{name}: read failed: {ex.Message}"); continue; }

    // The independent reference: inflate the ORIGINAL blob ourselves, so the round-trip
    // has a ground-truth image to diff against that does not come from the codec.
    byte[] originalInflated;
    try { originalInflated = Inflate(blob); }
    catch (Exception ex) { failures.Add($"{name}: original inflate failed: {ex.Message}"); continue; }
    if (originalInflated == null) { failures.Add($"{name}: no zlib stream in blob"); continue; }
    if (originalInflated.Length != DataRecordSize)
    {
        failures.Add($"{name}: original inflated to {originalInflated.Length}, expected {DataRecordSize}");
        continue;
    }

    // (1) DECODE — the handover-critical assertion: the C# codec must accept a blob the
    // original Delphi DBServer produced. scriptBlob is null (bare user_data has no sidecar).
    if (!NativeHumanDataCodec.TryDecode(blob, null, out var info, out var error))
    {
        failures.Add($"{name}: TryDecode REJECTED a real original record: {error}");
        continue;
    }
    var data = info.Data;

    // (1b) GROUND TRUTH — decoded semantics must match live mir3.user_index.
    if (groundTruth.TryGetValue(idx, out var gt))
    {
        if (data.btSex != gt.Sex)
            failures.Add($"{name}: decoded sex={data.btSex} != user_index {gt.Sex}");
        if (data.btJob != gt.Job)
            failures.Add($"{name}: decoded job={data.btJob} != user_index {gt.Job}");
        if (data.Abil.Level != gt.Level)
            failures.Add($"{name}: decoded level={data.Abil.Level} != user_index {gt.Level}");
    }
    else
    {
        failures.Add($"{name}: idx {idx} has no ground-truth row");
    }

    // (1c) NAME — must be a non-empty GBK string that survives a GBK re-encode round-trip.
    // Asserted structurally so no Chinese literal is embedded in this source file.
    if (string.IsNullOrEmpty(data.sCharName))
        failures.Add($"{name}: decoded char name is empty");
    else
    {
        try
        {
            var reencoded = gbk.GetBytes(data.sCharName);
            if (reencoded.Length is < 1 or > 15)
                failures.Add($"{name}: char name re-encodes to {reencoded.Length} bytes (expected 1..15)");
            else if (gbk.GetString(reencoded) != data.sCharName)
                failures.Add($"{name}: char name is not GBK round-trip stable");
        }
        catch (Exception ex) { failures.Add($"{name}: char name not GBK: {ex.Message}"); }
    }

    // The hair fix (decode reads 0x3E), proven here against REAL bytes rather than a
    // fixture: the decoded value must equal the original byte at 0x3E, and 0x3B — which
    // native never writes — must be zero in the source record.
    if (data.btHair != originalInflated[HairOffset])
        failures.Add($"{name}: btHair={data.btHair} != inflated[0x3E]={originalInflated[HairOffset]}");
    if (originalInflated[UnusedByNativeOffset] != 0)
        failures.Add($"{name}: inflated[0x3B]={originalInflated[UnusedByNativeOffset]} (native never writes 0x3B)");
    hairValues.Add(originalInflated[HairOffset]);

    decoded++;

    // (2) ROUND-TRIP FIDELITY — encode the untouched record and inflate the result. It must
    // reproduce the original inflated image byte-for-byte. First divergence is reported with
    // its offset so a real loss is actionable, not a bare boolean.
    if (!NativeHumanDataCodec.TryEncode(info, out var reBlob, out _, out var encError))
    {
        failures.Add($"{name}: TryEncode failed on a decoded real record: {encError}");
        continue;
    }
    byte[] reInflated;
    try { reInflated = Inflate(reBlob); }
    catch (Exception ex) { failures.Add($"{name}: re-encoded inflate failed: {ex.Message}"); continue; }
    if (reInflated == null) { failures.Add($"{name}: re-encoded blob has no zlib stream"); continue; }
    if (reInflated.Length != DataRecordSize)
    {
        failures.Add($"{name}: re-encoded inflated to {reInflated.Length}, expected {DataRecordSize}");
        continue;
    }

    var firstDiff = -1;
    var diffCount = 0;
    for (var i = 0; i < DataRecordSize; i++)
    {
        if (reInflated[i] != originalInflated[i])
        {
            if (firstDiff < 0) firstDiff = i;
            diffCount++;
        }
    }
    if (firstDiff >= 0)
    {
        failures.Add($"{name}: round-trip lost fidelity — {diffCount} byte(s) differ, " +
                     $"first at 0x{firstDiff:X} (original=0x{originalInflated[firstDiff]:X2} " +
                     $"re-encoded=0x{reInflated[firstDiff]:X2})");
        continue;
    }
    roundTripped++;
}

if (failures.Count > 0)
{
    Console.WriteLine("GoldenCodecFidelityCheck FAIL:");
    foreach (var f in failures.Take(30)) Console.WriteLine("  " + f);
    return 1;
}

if (decoded == 0)
{
    Console.WriteLine("GoldenCodecFidelityCheck FAIL: no record decoded");
    return 1;
}

Console.WriteLine(
    $"GoldenCodecFidelityCheck PASS decoded={decoded} roundTripByteExact={roundTripped} " +
    $"sex/job/level=user_index-30/30/30 names=GBK-stable " +
    $"hair@0x3E={{{string.Join(",", hairValues.Distinct().OrderBy(v => v))}}} " +
    $"0x3B=zero-in-all source=live-DBServer-written-records codec=DBSvr.Core.NativeHumanDataCodec");
return 0;
