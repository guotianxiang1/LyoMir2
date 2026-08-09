// Asserts the native type1 0x0174 (跨服转区积分) byte contract against the 战神
// DBServer disassembly: handler 0x599274, record builder 0x595CF4, index/filer
// 0x595DCC, SQL consumer 0x595FE8. Constants are quoted from instruction
// addresses in DBServer_unpacked.exe.
// Evidence: staging/dbsvr_type1_dispatch_census_20260803.md §3之三.
using System.Buffers.Binary;
using DBSvr.Core;
using SystemModule.Packet;

var failures = new List<string>();
Run("command word and record size", CommandAndRecord);
Run("packed dword splits into index/delta", PackedDwordSplit);
Run("character name at header+0x35", CharacterNameSlot);
Run("score index range 1..3", ScoreIndexRange);
Run("delta spread hits exactly one column", DeltaSpread);
Run("out-of-range index accrues nothing", OutOfRangeAccruesNothing);
Run("malformed frames rejected", MalformedFrames);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeTransferScoreAccrualCheck PASS tests=7 "
                  + "type1=0174 rec=0x27 idx=lo16@0x04 delta=hi16@0x04 "
                  + "name=0x35 range=1..3 additive");
return 0;

void CommandAndRecord()
{
    // Jump table B @0x598B23 routes 0x174 to 0x599274.
    Equal(0x0174, NativeTransferScoreAccrualProtocol.RequestCommand,
        "request command");
    // 0x59DDAC splits the record at 0x48.
    Equal(0x48, NativeTransferScoreAccrualProtocol.HeaderSize, "header size");
    // 0x595D25: `mov eax,0x27` before the allocation.
    Equal(0x27, NativeTransferScoreAccrualProtocol.PendingRecordSize,
        "pending record size");
    // 0x596026-0x59602E accepts 1..3.
    Equal(1, NativeTransferScoreAccrualProtocol.MinimumScoreIndex, "min index");
    Equal(3, NativeTransferScoreAccrualProtocol.MaximumScoreIndex, "max index");
}

void PackedDwordSplit()
{
    // 0x5992A8 `mov cx, word [hdr+4]` → record+0x20 (index);
    // 0x599288 `call 0x4080B0` is `shr eax,0x10` → record+0x22 (delta).
    // So ONE dword at header+0x04 carries both: low=index, high=delta.
    var frame = Header(h =>
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x04, 4), 0x04D20002));
    True(NativeTransferScoreAccrualProtocol.TryDecodeRequest(frame, out var req,
        out var err), "decode: " + err);
    Equal(2, req.ScoreIndex, "low word is the score index");
    Equal(0x04D2, req.Delta, "high word is the delta");

    // Boundary: a delta of 0xFFFF must survive intact (no sign folding).
    var maxDelta = Header(h =>
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x04, 4), 0xFFFF0001));
    True(NativeTransferScoreAccrualProtocol.TryDecodeRequest(maxDelta,
        out var maxReq, out _), "max delta decode");
    Equal(1, maxReq.ScoreIndex, "index with max delta");
    Equal(0xFFFF, maxReq.Delta, "delta 0xFFFF preserved");
}

void CharacterNameSlot()
{
    // 0x59929A reads header+0x35 via 0x404E5C, cl capacity 0x0F elsewhere.
    var frame = Header(h =>
    {
        PutShortString(h, 0x35, "TransferGuy");
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x04, 4), 0x00070003);
    });
    True(NativeTransferScoreAccrualProtocol.TryDecodeRequest(frame, out var req,
        out var err), "decode: " + err);
    EqualText("TransferGuy", Text(req.CharacterName), "name at header+0x35");
    Equal(3, req.ScoreIndex, "index alongside name");
    Equal(7, req.Delta, "delta alongside name");

    var over = Header(h => h[0x35] = 0x10);
    False(NativeTransferScoreAccrualProtocol.TryDecodeRequest(over, out _, out _),
        "name over 15 refused");
}

void ScoreIndexRange()
{
    False(NativeTransferScoreAccrualProtocol.IsScoreIndexInRange(0), "0 out");
    True(NativeTransferScoreAccrualProtocol.IsScoreIndexInRange(1), "1 in");
    True(NativeTransferScoreAccrualProtocol.IsScoreIndexInRange(2), "2 in");
    True(NativeTransferScoreAccrualProtocol.IsScoreIndexInRange(3), "3 in");
    False(NativeTransferScoreAccrualProtocol.IsScoreIndexInRange(4), "4 out");
    False(NativeTransferScoreAccrualProtocol.IsScoreIndexInRange(0xFFFF),
        "0xFFFF out");
}

void DeltaSpread()
{
    // 0x596042 writes the delta into ONE slot; the SQL's other two `+%d` args
    // stay 0, so exactly one column accrues per request.
    NativeTransferScoreAccrualProtocol.SpreadDelta(1, 500,
        out var a1, out var a2, out var a3);
    Equal(500, a1, "index 1 → Score1");
    Equal(0, a2, "index 1 leaves Score2");
    Equal(0, a3, "index 1 leaves Score3");

    NativeTransferScoreAccrualProtocol.SpreadDelta(2, 500,
        out var b1, out var b2, out var b3);
    Equal(0, b1, "index 2 leaves Score1");
    Equal(500, b2, "index 2 → Score2");
    Equal(0, b3, "index 2 leaves Score3");

    NativeTransferScoreAccrualProtocol.SpreadDelta(3, 500,
        out var c1, out var c2, out var c3);
    Equal(0, c1, "index 3 leaves Score1");
    Equal(0, c2, "index 3 leaves Score2");
    Equal(500, c3, "index 3 → Score3");
}

void OutOfRangeAccruesNothing()
{
    // `jae 0x596047` skips the assignment entirely for 0 and >3.
    foreach (var bad in new[] { 0, 4, 0xFFFF })
    {
        NativeTransferScoreAccrualProtocol.SpreadDelta(bad, 999,
            out var s1, out var s2, out var s3);
        Equal(0, s1, $"index {bad} Score1");
        Equal(0, s2, $"index {bad} Score2");
        Equal(0, s3, $"index {bad} Score3");
    }
}

void MalformedFrames()
{
    False(NativeTransferScoreAccrualProtocol.TryDecodeRequest(null, out _, out _),
        "null frame");
    False(NativeTransferScoreAccrualProtocol.TryDecodeRequest(
            new LegacyDbServerFrame(1, 0, new byte[0x47]), out _, out _),
        "short header");
    var wrong = new byte[0x48];
    BinaryPrimitives.WriteUInt16LittleEndian(wrong, 0x0176);
    False(NativeTransferScoreAccrualProtocol.TryDecodeRequest(
            new LegacyDbServerFrame(1, 0, wrong), out _, out _),
        "wrong command (0x0176 is the separate TransferScore op)");
}

// ---- helpers ----

LegacyDbServerFrame Header(Action<byte[]> fill)
{
    var header = new byte[NativeTransferScoreAccrualProtocol.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(header,
        NativeTransferScoreAccrualProtocol.RequestCommand);
    fill?.Invoke(header);
    return new LegacyDbServerFrame(1, 0, header);
}

void PutShortString(byte[] buffer, int offset, string value)
{
    var bytes = System.Text.Encoding.ASCII.GetBytes(value);
    buffer[offset] = (byte)bytes.Length;
    bytes.CopyTo(buffer, offset + 1);
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
