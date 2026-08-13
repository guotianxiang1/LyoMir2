// ScriptData sections 2/6/7/8 — 战神 equivalence audit.
//
// Native (GAME side, not DBServer):
//   builder TPlayer.BuildScriptData = sub_6E4CD8, sole caller 0x6B65DC
//   parser  TPlayer.LoadScriptData  = sub_6E448C, sole caller 0x6547D1
//   framing 7-byte header: magic 0xABCDEFAA / UInt16 len / byte type
//           (0x6E4E39..0x6E4E47 emit, 0x6E44EE..0x6E4510 parse)
//   ladder  sizing order 0,1,2,6,7,8 (0x6E4CF5..0x6E4D8D)
//
//   type 2 shenYou     obj+0x5A4  24B verbatim  size sub_6E4B4C = const 0x18
//   type 6 bodyState   obj+0xDC   10B/elem      size sub_6E4B70
//   type 7 coldTime    obj+0x504  0xFAFA+12B/e  size sub_6E4C28
//   type 8 FirstDoSome obj+0x1938 4B dword      size sub_6E4CB4 = const 4
//
// NOT audit-blind. mir3.user_data has a SECOND blob column, ScriptData, populated
// in all 34 rows — the earlier golden extraction only ever selected ud.Data, so it
// had never been looked at. goldens/ holds those 34 blobs verbatim (8-byte wrapper
// + zlib), written by the ORIGINAL Delphi DBServer, and the core assertion below is
// a byte-exact decode/re-encode round trip over all of them.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

try
{
    PrepareRuntimeFiles();

    Equal(0xABCDEFAAu, Const<uint>("NativeScriptSectionMagic"), "section magic");
    Equal(7, Const<int>("NativeScriptSectionHeaderSize"), "header size");
    Equal(2, (int)Const<byte>("NativeScriptTypeShenYou"), "type shenYou");
    Equal(6, (int)Const<byte>("NativeScriptTypeBodyState"), "type bodyState");
    Equal(7, (int)Const<byte>("NativeScriptTypeColdTime"), "type coldTime");
    Equal(8, (int)Const<byte>("NativeScriptTypeFirstDoSome"), "type FirstDoSome");
    Equal(0x18, Const<int>("NativeShenYouBlockSize"), "shenYou size (sub_6E4B4C)");
    Equal(4, Const<int>("NativeFirstDoSomeSize"), "FirstDoSome size (sub_6E4CB4)");
    Equal(10, Const<int>("NativeBodyStateElementSize"), "bodyState elem (0x6E4C07)");
    Equal(12, Const<int>("NativeColdTimeElementSize"), "coldTime elem (0x6E4C9C)");
    Equal(0x0000FAFAu, Const<uint>("NativeColdTimeInnerMagic"),
        "coldTime inner magic (0x6E4C5A)");

    CheckFilterPolarityAgainstBinaryBitmap();
    CheckGoldenRoundTripMatchesNativeReversal();
    CheckGoldenObservedStateIdsAllPersist();
    CheckShenYouExactLengthGate();
    CheckFirstDoSomeExactLengthGate();
    CheckFirstDoSomeBitAccessors();
    CheckColdTimeInnerMagicAlwaysWritten();
    CheckColdTimeLegacyFormatIsReadOnly();
    CheckColdTimeEmptyListOmitsSection();
    CheckBodyStateRejectsIdAtOrAbove107();
    CheckBodyStateFiltersNonPersistentOnEmit();
    CheckBodyStatePadByteIsZero();
    CheckBodyStateRoundTripReversesOrder();
    CheckLadderOrderOnInsert();
    CheckNeverEmitsRetiredTypes();
    CheckMalformedBlobIsRejectedNotRewritten();
    CheckWiring();

    Console.WriteLine(
        "PASS NativeScriptSections magic=0xABCDEFAA hdr=7 ladder=0,1,2,6,7,8 " +
        "t2=0x18@0x5A4 t6=10B@0xDC t7=0xFAFA+12B@0x504 t8=4B@0x1938 " +
        "filter=sub_791D54(bit-clear=persist) " +
        "goldens=34/34 exact-modulo-native-t6-reversal, 2nd-trip-identical");
    return 0;
}
// Corpus absent => INCOMPLETE (exit 2), never a silent green and never a FAIL.
// A corpus that is PRESENT but the wrong size still falls through to the
// generic handler below and fails, which is intended: present-but-wrong is a
// real defect, absent is an environment gap.
catch (GoldensUnavailableException unavailable)
{
    Console.WriteLine($"SKIP NativeScriptSections: {unavailable.Message}");
    Console.WriteLine("SKIP reason: golden-backed assertions were NOT executed; " +
        "this run proves nothing about ScriptData round-trip fidelity.");
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeScriptSectionsCheck FAIL: {exception}");
    return 1;
}

// ---------------------------------------------------------------------------
// sub_791D54 polarity — the single most reversible-looking fact here.
// ---------------------------------------------------------------------------

// The bitmap image at 0x791D6C, transcribed from flat_image.bin. Bits SET mark
// states that are NOT persisted (Delphi `not (x in [set])`), and `ja` at 0x791D56
// bypasses the bt with CF clear so ids > 0x67 ARE persisted.
static void CheckFilterPolarityAgainstBinaryBitmap()
{
    byte[] bitmap =
    {
        0xFF, 0xFF, 0x0F, 0x90, 0x00, 0x00, 0xFE, 0xFF,
        0xFF, 0x00, 0xC0, 0x03, 0x40, 0x00, 0x00, 0x00
    };
    for (var id = 0; id <= 255; id++)
    {
        bool expected;
        if (id > 0x67)
        {
            expected = true;
        }
        else
        {
            var index = id & 0x7F;
            expected = ((bitmap[index >> 3] >> (index & 7)) & 1) == 0;
        }
        var actual = IsPersistent((byte)id);
        if (actual != expected)
            throw new InvalidOperationException(
                $"sub_791D54 polarity for id {id}: expected={expected}, actual={actual}");
    }

    // Spot-check the semantics that make the corrected reading credible: poisons
    // and the two mount states are EXCLUDED, plain shields are INCLUDED.
    foreach (var excluded in new byte[] { 0, 1, 13, 28, 31, 51, 52, 102 })
        if (IsPersistent(excluded))
            throw new InvalidOperationException(
                $"state {excluded} must NOT persist (bitmap bit is set)");
    foreach (var included in new byte[] { 20, 21, 22, 23, 32, 33, 34, 78, 104, 105, 106 })
        if (!IsPersistent(included))
            throw new InvalidOperationException(
                $"state {included} MUST persist (bitmap bit is clear)");
}

// ---------------------------------------------------------------------------
// The golden corpus: 34 blobs written by the ORIGINAL Delphi DBServer.
// ---------------------------------------------------------------------------

// The exact fidelity claim over the 34 real blobs.
//
// A single load+save is NOT byte-identical, and must not be: native's own parser
// PREPENDS each type-6 node (0x6E4370-0x6E437F) while its emitter walks the list
// head-first (0x6E4C14), so one round trip REVERSES type-6 element order. The
// goldens show it directly — idx1 carries ids 41,40,37,36,34,33,32 descending, and
// a faithful port rewrites them ascending.
//
// So the invariants that actually hold are:
//   (a) one round trip == the golden with type-6 elements reversed, and every
//       other byte (prefix, all headers, section order, types 0/1/2/8 payloads)
//       identical; and
//   (b) a SECOND round trip is byte-identical to the first — the reversal is an
//       involution, so the blob is stable from then on and never drifts.
// Both are checked against bytes this codebase did not author.
static void CheckGoldenRoundTripMatchesNativeReversal()
{
    var goldens = GoldenBlobs();
    if (goldens.Count != 34)
        throw new InvalidOperationException(
            $"expected 34 golden ScriptData blobs, found {goldens.Count}");

    var sawReversal = false;
    foreach (var (name, blob) in goldens)
    {
        var once = RoundTrip(name, blob);
        var expected = ReverseBodyStateElements(blob);
        if (once.Length != expected.Length)
            throw new InvalidOperationException(
                $"{name}: length changed {expected.Length} -> {once.Length}");
        for (var i = 0; i < expected.Length; i++)
            if (once[i] != expected[i])
                throw new InvalidOperationException(
                    $"{name}: byte {i} (0x{i:X}) is 0x{once[i]:X2}, expected " +
                    $"0x{expected[i]:X2} (golden with type-6 order reversed)");

        // (b) stability: reversing twice returns to the golden bytes exactly.
        var twice = RoundTrip(name, once);
        if (!twice.SequenceEqual(blob))
            throw new InvalidOperationException(
                $"{name}: a second round trip must restore the golden bytes exactly");

        if (Sections(blob).Any(s => s.Type == 6 && s.Payload.Length > 10))
            sawReversal = true;
    }
    if (!sawReversal)
        throw new InvalidOperationException(
            "no golden had a multi-element type 6 section, so the reversal " +
            "invariant was never actually exercised");
}

static byte[] RoundTrip(string name, byte[] blob)
{
    var player = NewPlayer();
    SetScript(player, (byte[])blob.Clone());
    Call(player, "RestoreNativeScriptSections");
    if (!CallFor<bool>(player, "PersistNativeScriptSections"))
        throw new InvalidOperationException(
            $"{name}: PersistNativeScriptSections rejected a real native blob");
    return GetScript(player);
}

// Rebuilds a blob with only the type-6 element order flipped, leaving every other
// byte alone.
static byte[] ReverseBodyStateElements(byte[] blob)
{
    var sections = Sections(blob);
    var rebuilt = new List<(byte, byte[])>();
    foreach (var (type, payload) in sections)
    {
        if (type != 6 || payload.Length < 10)
        {
            rebuilt.Add((type, payload));
            continue;
        }
        var count = payload.Length / 10;
        var flipped = new byte[payload.Length];
        for (var i = 0; i < count; i++)
            Array.Copy(payload, i * 10, flipped, (count - 1 - i) * 10, 10);
        rebuilt.Add((type, flipped));
    }
    return BuildBlob(rebuilt.ToArray());
}

// Every stateId the original server actually chose to persist must pass our filter.
// 252 elements over 34 records; the inverse reading fails all 252.
static void CheckGoldenObservedStateIdsAllPersist()
{
    var seen = new SortedSet<byte>();
    var elements = 0;
    foreach (var (name, blob) in GoldenBlobs())
    {
        foreach (var (type, payload) in Sections(blob))
        {
            if (type != 6) continue;
            if (payload.Length % 10 != 0)
                throw new InvalidOperationException(
                    $"{name}: type 6 length {payload.Length} is not a multiple of 10");
            for (var i = 0; i + 10 <= payload.Length; i += 10)
            {
                var id = payload[i];
                elements++;
                seen.Add(id);
                if (!IsPersistent(id))
                    throw new InvalidOperationException(
                        $"{name}: original DBServer persisted state {id} but our " +
                        "filter drops it — sub_791D54 polarity is inverted");
            }
        }
    }
    if (elements != 252)
        throw new InvalidOperationException(
            $"expected 252 golden type-6 elements, saw {elements}");
    // Guards against a filter that just returns true for everything.
    if (seen.Count < 10)
        throw new InvalidOperationException(
            $"expected a spread of golden state ids, saw only {seen.Count}");
}

// All 34 goldens carry sections in exactly this order, and type 7 never appears
// (its list is empty, so the 0x6E4E94 size gate drops it).
static void CheckLadderOrderOnInsert()
{
    foreach (var (name, blob) in GoldenBlobs())
    {
        var order = Sections(blob).Select(s => (int)s.Type).ToArray();
        var expected = new[] { 0, 1, 2, 6, 8 };
        if (!order.SequenceEqual(expected))
            throw new InvalidOperationException(
                $"{name}: golden section order [{string.Join(",", order)}] " +
                $"!= [{string.Join(",", expected)}]");
    }

    // A fresh character with a cooldown must land type 7 BETWEEN 6 and 8, matching
    // the sizing ladder in sub_6E4CD8 rather than appending at the end.
    var player = NewPlayer();
    SetScript(player, null);
    Call(player, "RestoreNativeScriptSections");
    AddColdTime(player, 0x1234u, 5000, 9000);
    AddBodyState(player, 20, 1, 2);
    if (!CallFor<bool>(player, "PersistNativeScriptSections"))
        throw new InvalidOperationException("fresh blob rebuild failed");
    var types = Sections(GetScript(player)).Select(s => (int)s.Type).ToArray();
    var wanted = new[] { 2, 6, 7, 8 };
    if (!types.SequenceEqual(wanted))
        throw new InvalidOperationException(
            $"fresh ladder [{string.Join(",", types)}] != [{string.Join(",", wanted)}]");

    // The goldens all arrive with their sections already in ladder order, and a
    // fresh build happens to add them in ascending order too, so neither exercises
    // the RANKED-INSERT path. Start from a blob whose only native sections sort
    // AFTER the ones we are about to add, plus the C#-only 0x79 sidecar which is
    // > 8 and must stay last.
    var sparse = NewPlayer();
    SetScript(sparse, BuildBlob(
        (0, new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
        (8, new byte[] { 7, 0, 0, 0 }),
        (0x79, new byte[] { 0xAB })));
    Call(sparse, "RestoreNativeScriptSections");
    AddColdTime(sparse, 9u, 1, 2);
    AddBodyState(sparse, 21, 3, 4);
    if (!CallFor<bool>(sparse, "PersistNativeScriptSections"))
        throw new InvalidOperationException("sparse ladder rebuild failed");
    var inserted = Sections(GetScript(sparse)).Select(s => (int)s.Type).ToArray();
    var insertedWanted = new[] { 0, 2, 6, 7, 8, 0x79 };
    if (!inserted.SequenceEqual(insertedWanted))
        throw new InvalidOperationException(
            $"inserted ladder [{string.Join(",", inserted)}] != " +
            $"[{string.Join(",", insertedWanted)}] — new sections must be placed by " +
            "rank (sub_6E4CD8 order 0,1,2,6,7,8), not appended");
}

// ---------------------------------------------------------------------------
// Per-type gates
// ---------------------------------------------------------------------------

// 0x6E42DC `cmp edx,0x18 / je`: any other length leaves obj+0x5A4 at ctor zeros.
static void CheckShenYouExactLengthGate()
{
    foreach (var badLength in new[] { 0, 1, 23, 25, 48 })
    {
        var player = LoadWithSection(2, Fill(badLength, 0xAB));
        var block = (byte[])Field("m_NativeShenYouBlock").GetValue(player);
        Equal(0x18, block.Length, $"shenYou stays 24 bytes for len {badLength}");
        foreach (var b in block)
            if (b != 0)
                throw new InvalidOperationException(
                    $"shenYou len {badLength} must be REJECTED, leaving zeros");
    }
    var good = LoadWithSection(2, Fill(0x18, 0x5A));
    var accepted = (byte[])Field("m_NativeShenYouBlock").GetValue(good);
    foreach (var b in accepted)
        if (b != 0x5A)
            throw new InvalidOperationException("shenYou exactly-24 must be accepted");
}

// 0x6E4467 `cmp edx,4 / jne`: any other length leaves obj+0x1938 at 0.
static void CheckFirstDoSomeExactLengthGate()
{
    foreach (var badLength in new[] { 0, 1, 3, 5, 8 })
    {
        var player = LoadWithSection(8, Fill(badLength, 0xFF));
        Equal(0u, (uint)Field("m_dwNativeFirstDoSome").GetValue(player),
            $"FirstDoSome rejects len {badLength}");
    }
    var good = LoadWithSection(8, new byte[] { 0x09, 0x00, 0x00, 0x00 });
    Equal(9u, (uint)Field("m_dwNativeFirstDoSome").GetValue(good),
        "FirstDoSome accepts exactly 4");
}

// sub_6F6CE4 bounds at `cmp cl,0x1F`; sub_6F6CB8 returns early when already set.
static void CheckFirstDoSomeBitAccessors()
{
    var player = NewPlayer();
    Field("m_dwNativeFirstDoSome").SetValue(player, 0u);

    Equal(true, CallFor<bool>(player, "SetNativeFirstDoSome", 0), "set bit 0");
    Equal(false, CallFor<bool>(player, "SetNativeFirstDoSome", 0),
        "set bit 0 twice is idempotent (0x6F6CC5 tests first)");
    Equal(true, CallFor<bool>(player, "HasNativeFirstDoSome", 0), "bit 0 set");
    Equal(true, CallFor<bool>(player, "SetNativeFirstDoSome", 31), "set bit 31");
    Equal(false, CallFor<bool>(player, "SetNativeFirstDoSome", 32),
        "bit 32 is out of range (cmp cl,0x1F / ja)");
    Equal(false, CallFor<bool>(player, "HasNativeFirstDoSome", 32),
        "bit 32 never reads true");
    Equal(false, CallFor<bool>(player, "SetNativeFirstDoSome", -1),
        "negative index rejected");
    Equal(0x80000001u, (uint)Field("m_dwNativeFirstDoSome").GetValue(player),
        "bits 0 and 31 only");
}

// 0x6E4C5A writes 0xFAFA unconditionally. Omitting it makes the native parser take
// the legacy 8-byte branch, mis-parsing every element and returning True with NO
// log line — silent corruption, the worst failure available here.
static void CheckColdTimeInnerMagicAlwaysWritten()
{
    var player = NewPlayer();
    SetScript(player, null);
    Call(player, "RestoreNativeScriptSections");
    AddColdTime(player, 0xAABBCCDDu, 1234, 5678);
    AddColdTime(player, 0x11u, 7, 9);
    if (!CallFor<bool>(player, "PersistNativeScriptSections"))
        throw new InvalidOperationException("coldTime rebuild failed");

    var payload = Sections(GetScript(player)).Single(s => s.Type == 7).Payload;
    Equal(4 + 24, payload.Length, "coldTime payload = 4 + 12*N");
    Equal(0x0000FAFAu,
        BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4)),
        "coldTime inner magic must be written");
    Equal(0xAABBCCDDu,
        BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4)), "key 0");
    Equal(1234, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8, 4)),
        "remaining 0");
    Equal(5678, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(12, 4)),
        "total 0");
}

// 0x6E4410: no magic -> 8-byte elements with Total zero-filled (0x6E442A). We must
// READ that form but never WRITE it.
static void CheckColdTimeLegacyFormatIsReadOnly()
{
    var legacy = new byte[16];
    BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(0, 4), 0x55u);
    BinaryPrimitives.WriteInt32LittleEndian(legacy.AsSpan(4, 4), 600);
    BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(8, 4), 0x66u);
    BinaryPrimitives.WriteInt32LittleEndian(legacy.AsSpan(12, 4), 700);

    var player = LoadWithSection(7, legacy);
    var entries = ColdTimes(player);
    Equal(2, entries.Count, "legacy 8-byte elements are read");
    Equal(0x55u, entries[0].key, "legacy key 0");
    Equal(600, entries[0].remaining, "legacy remaining 0");
    Equal(0, entries[0].total, "legacy total is zero-filled (0x6E442A)");
    Equal(0x66u, entries[1].key, "legacy key 1");

    // Re-emitting must upgrade to the modern form.
    if (!CallFor<bool>(player, "PersistNativeScriptSections"))
        throw new InvalidOperationException("legacy upgrade rebuild failed");
    var payload = Sections(GetScript(player)).Single(s => s.Type == 7).Payload;
    Equal(0x0000FAFAu,
        BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4)),
        "legacy input must be re-emitted in the modern 0xFAFA form");
    Equal(4 + 24, payload.Length, "two entries re-emitted as 12 bytes each");
}

// sub_6E4C28 returns 0 for an empty list, and the 0x6E4E94 gate then omits the
// whole section — which is why type 7 is absent from all 34 goldens.
static void CheckColdTimeEmptyListOmitsSection()
{
    var player = NewPlayer();
    SetScript(player, null);
    Call(player, "RestoreNativeScriptSections");
    if (!CallFor<bool>(player, "PersistNativeScriptSections"))
        throw new InvalidOperationException("empty rebuild failed");
    if (Sections(GetScript(player)).Any(s => s.Type == 7))
        throw new InvalidOperationException(
            "empty coldTime list must omit the section entirely");

    // And an existing section must disappear once the list empties.
    var seeded = LoadWithSection(7, ColdTimePayload((0x1u, 5, 6)));
    Equal(1, ColdTimes(seeded).Count, "seeded one cooldown");
    ClearColdTimes(seeded);
    if (!CallFor<bool>(seeded, "PersistNativeScriptSections"))
        throw new InvalidOperationException("drain rebuild failed");
    if (Sections(GetScript(seeded)).Any(s => s.Type == 7))
        throw new InvalidOperationException(
            "draining the list must remove the type 7 section");
}

// 0x6E432D `sub al,0x6B` / 0x6E432F `jae` -> the ENTIRE section is rejected, not
// just the offending element.
static void CheckBodyStateRejectsIdAtOrAbove107()
{
    var payload = BodyStatePayload((20, 1, 2), (107, 3, 4), (21, 5, 6));
    var player = LoadWithSection(6, payload);
    Equal(0, BodyStates(player).Count,
        "one id >= 107 rejects the whole type 6 section");

    var ok = LoadWithSection(6, BodyStatePayload((20, 1, 2), (106, 3, 4)));
    Equal(2, BodyStates(ok).Count, "id 106 is in range and persists");
}

// 0x6E4BDC/0x6E4BE3 on emit and 0x6E4333/0x6E433A on parse: non-persistent ids are
// silently skipped on BOTH sides.
static void CheckBodyStateFiltersNonPersistentOnEmit()
{
    // 13 = stPoisonBlue (bit set => excluded), 20 = stMagicShield (included).
    var player = LoadWithSection(6, BodyStatePayload((20, 11, 12), (13, 21, 22)));
    var loaded = BodyStates(player);
    Equal(1, loaded.Count, "non-persistent id dropped on parse");
    Equal((byte)20, loaded[0].id, "surviving id is the persistent one");

    // Inject a non-persistent entry directly and confirm emit drops it too.
    AddBodyState(player, 31, 1, 2);
    if (!CallFor<bool>(player, "PersistNativeScriptSections"))
        throw new InvalidOperationException("bodyState rebuild failed");
    var payload = Sections(GetScript(player)).Single(s => s.Type == 6).Payload;
    Equal(10, payload.Length, "non-persistent id dropped on emit");
    Equal((byte)20, payload[0], "only the persistent id is emitted");
}

// 0x6E4BED `mov byte [ebp-0x0D],0` — an explicit pad, zero in 252/252 goldens.
static void CheckBodyStatePadByteIsZero()
{
    var player = NewPlayer();
    SetScript(player, null);
    Call(player, "RestoreNativeScriptSections");
    AddBodyState(player, 34, 0xDEADBEEF, 0x11223344);
    if (!CallFor<bool>(player, "PersistNativeScriptSections"))
        throw new InvalidOperationException("pad rebuild failed");
    var payload = Sections(GetScript(player)).Single(s => s.Type == 6).Payload;
    Equal(10, payload.Length, "one element is 10 bytes");
    Equal((byte)34, payload[0], "stateId at +0x00");
    Equal((byte)0, payload[1], "pad byte at +0x01 is zero");
    Equal(0xDEADBEEFu,
        BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2, 4)),
        "value at +0x02");
    Equal(0x11223344u,
        BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(6, 4)),
        "duration at +0x06");

    foreach (var (name, blob) in GoldenBlobs())
        foreach (var (type, p) in Sections(blob))
        {
            if (type != 6) continue;
            for (var i = 0; i + 10 <= p.Length; i += 10)
                if (p[i + 1] != 0)
                    throw new InvalidOperationException(
                        $"{name}: golden pad byte at element {i / 10} is not zero");
        }
}

// The emitter walks head-first while the parser PREPENDS (0x6E4370-0x6E437F), so a
// single round trip REVERSES order and a second restores it. Native behaviour;
// reproduced deliberately rather than fixed.
static void CheckBodyStateRoundTripReversesOrder()
{
    var wire = BodyStatePayload((20, 1, 1), (21, 2, 2), (22, 3, 3));
    var player = LoadWithSection(6, wire);
    var ids = BodyStates(player).Select(e => (int)e.id).ToArray();
    if (!ids.SequenceEqual(new[] { 22, 21, 20 }))
        throw new InvalidOperationException(
            $"parse must PREPEND, giving 22,21,20; got {string.Join(",", ids)}");

    if (!CallFor<bool>(player, "PersistNativeScriptSections"))
        throw new InvalidOperationException("reverse rebuild failed");
    var emitted = Sections(GetScript(player)).Single(s => s.Type == 6).Payload;
    var emittedIds = new[] { emitted[0], emitted[10], emitted[20] }
        .Select(b => (int)b).ToArray();
    if (!emittedIds.SequenceEqual(new[] { 22, 21, 20 }))
        throw new InvalidOperationException(
            "emit must walk head-first, writing 22,21,20");
}

// Types 3/4/5 route to the unknown-type error sink 0x6E4856; emitting one makes an
// ORIGINAL server log on every login.
static void CheckNeverEmitsRetiredTypes()
{
    var player = NewPlayer();
    SetScript(player, null);
    Call(player, "RestoreNativeScriptSections");
    AddColdTime(player, 1u, 1, 1);
    AddBodyState(player, 20, 1, 1);
    if (!CallFor<bool>(player, "PersistNativeScriptSections"))
        throw new InvalidOperationException("retired-type rebuild failed");
    foreach (var (type, _) in Sections(GetScript(player)))
        if (type is 3 or 4 or 5)
            throw new InvalidOperationException(
                $"type {type} routes to the error sink 0x6E4856 and must never be emitted");

    // A retired type already present in a blob is preserved, not dropped: the
    // native parser skips it and CONTINUES the scan.
    var carried = BuildBlob((0, new byte[] { 1 }), (4, new byte[] { 9, 9 }),
        (8, new byte[] { 1, 0, 0, 0 }));
    var keeper = NewPlayer();
    SetScript(keeper, carried);
    Call(keeper, "RestoreNativeScriptSections");
    if (!CallFor<bool>(keeper, "PersistNativeScriptSections"))
        throw new InvalidOperationException("carry rebuild failed");
    if (!Sections(GetScript(keeper)).Any(s => s.Type == 4))
        throw new InvalidOperationException(
            "an existing retired-type section must be carried through untouched");
}

// A blob we cannot parse must be left exactly as-is rather than reframed, so a
// malformed record is never made worse.
static void CheckMalformedBlobIsRejectedNotRewritten()
{
    var broken = new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 0x01, 0x02, 0x03 };
    var player = NewPlayer();
    SetScript(player, (byte[])broken.Clone());
    Call(player, "RestoreNativeScriptSections");
    Equal(false, CallFor<bool>(player, "PersistNativeScriptSections"),
        "a malformed blob must be reported, not rewritten");
    var after = GetScript(player);
    if (!after.SequenceEqual(broken))
        throw new InvalidOperationException(
            "a malformed blob must be left byte-identical");

    // Bad magic is equally fatal (0x6E44F4 aborts the whole scan).
    var badMagic = BuildBlob((2, Fill(0x18, 0)));
    badMagic[4] = 0x00;
    var second = NewPlayer();
    SetScript(second, badMagic);
    Equal(false, CallFor<bool>(second, "PersistNativeScriptSections"),
        "bad section magic must be reported");
}

static void CheckWiring()
{
    var root = FindRepositoryRoot();
    RequireContains(Path.Combine(root, "GameSvr", "UsrSystem", "UsrEngn.cs"),
        "RestoreNativeScriptSections();", "load path restores sections");
    RequireContains(Path.Combine(root, "GameSvr", "UsrSystem", "UsrEngn.cs"),
        "PersistNativeScriptSections()", "save path rebuilds sections");
}

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------

static List<(string name, byte[] blob)> GoldenBlobs()
{
    var directory = GoldenDirectoryCandidates(out var candidates);
    if (directory == null)
        throw new GoldensUnavailableException(
            "golden ScriptData corpus not found or empty; expected 34 *.bin at " +
            CanonicalGoldenDirectory() + Environment.NewLine +
            "  searched: " + string.Join(Environment.NewLine + "            ",
                candidates));
    var result = new List<(string, byte[])>();
    foreach (var path in Directory.GetFiles(directory, "*.bin").OrderBy(p => p))
    {
        // The DB blob is an 8-byte wrapper followed by a zlib stream, same as the
        // Data column.
        var stored = File.ReadAllBytes(path);
        using var input = new MemoryStream(stored, 8, stored.Length - 8);
        using var inflate = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);
        result.Add((Path.GetFileName(path), output.ToArray()));
    }
    return result;
}

static List<(byte Type, byte[] Payload)> Sections(byte[] blob)
{
    var result = new List<(byte, byte[])>();
    if (blob == null || blob.Length < 4) return result;
    var declared = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(0, 4));
    if (declared != blob.Length - 4)
        throw new InvalidOperationException(
            $"blob prefix {declared} != length-4 {blob.Length - 4}");
    var offset = 4;
    while (offset + 7 <= blob.Length)
    {
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        if (magic != 0xABCDEFAA)
            throw new InvalidOperationException(
                $"bad section magic 0x{magic:X8} at {offset}");
        var length = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(offset + 4, 2));
        var type = blob[offset + 6];
        offset += 7;
        result.Add((type, blob.AsSpan(offset, length).ToArray()));
        offset += length;
    }
    return result;
}

static byte[] BuildBlob(params (byte type, byte[] payload)[] sections)
{
    var total = sections.Sum(s => 7 + s.payload.Length);
    var raw = new byte[4 + total];
    BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0, 4), total);
    var offset = 4;
    foreach (var (type, payload) in sections)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(offset, 4), 0xABCDEFAA);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(offset + 4, 2),
            (ushort)payload.Length);
        raw[offset + 6] = type;
        offset += 7;
        payload.CopyTo(raw, offset);
        offset += payload.Length;
    }
    return raw;
}

static TPlayObject LoadWithSection(byte type, byte[] payload)
{
    var player = NewPlayer();
    SetScript(player, BuildBlob((type, payload)));
    Call(player, "RestoreNativeScriptSections");
    return player;
}

static byte[] BodyStatePayload(params (byte id, uint value, uint duration)[] elements)
{
    var payload = new byte[elements.Length * 10];
    var offset = 0;
    foreach (var (id, value, duration) in elements)
    {
        payload[offset] = id;
        payload[offset + 1] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 2, 4), value);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 6, 4), duration);
        offset += 10;
    }
    return payload;
}

static byte[] ColdTimePayload(params (uint key, int remaining, int total)[] entries)
{
    var payload = new byte[4 + entries.Length * 12];
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x0000FAFAu);
    var offset = 4;
    foreach (var (key, remaining, total) in entries)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), key);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 4, 4), remaining);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 8, 4), total);
        offset += 12;
    }
    return payload;
}

static byte[] Fill(int length, byte value)
{
    var raw = new byte[length];
    Array.Fill(raw, value);
    return raw;
}

static bool IsPersistent(byte id)
    => (bool)typeof(TPlayObject).GetMethod("IsNativePersistentBodyState",
           BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
       .Invoke(null, new object[] { id });

static List<(byte id, uint value, uint duration)> BodyStates(TPlayObject player)
{
    var list = (System.Collections.IEnumerable)
        Field("m_NativeBodyStates").GetValue(player);
    var result = new List<(byte, uint, uint)>();
    foreach (var entry in list)
    {
        var t = entry.GetType();
        result.Add(((byte)t.GetField("StateId").GetValue(entry),
            (uint)t.GetField("Value").GetValue(entry),
            (uint)t.GetField("Duration").GetValue(entry)));
    }
    return result;
}

static List<(uint key, int remaining, int total)> ColdTimes(TPlayObject player)
{
    var list = (System.Collections.IEnumerable)
        Field("m_NativeColdTimes").GetValue(player);
    var result = new List<(uint, int, int)>();
    foreach (var entry in list)
    {
        var t = entry.GetType();
        result.Add(((uint)t.GetField("Key").GetValue(entry),
            (int)t.GetField("Remaining").GetValue(entry),
            (int)t.GetField("Total").GetValue(entry)));
    }
    return result;
}

static void AddBodyState(TPlayObject player, byte id, uint value, uint duration)
{
    var listField = Field("m_NativeBodyStates");
    var list = listField.GetValue(player);
    var entryType = list.GetType().GetGenericArguments()[0];
    var entry = Activator.CreateInstance(entryType);
    entryType.GetField("StateId").SetValue(entry, id);
    entryType.GetField("Value").SetValue(entry, value);
    entryType.GetField("Duration").SetValue(entry, duration);
    list.GetType().GetMethod("Add").Invoke(list, new[] { entry });
}

static void AddColdTime(TPlayObject player, uint key, int remaining, int total)
{
    var list = Field("m_NativeColdTimes").GetValue(player);
    var entryType = list.GetType().GetGenericArguments()[0];
    var entry = Activator.CreateInstance(entryType);
    entryType.GetField("Key").SetValue(entry, key);
    entryType.GetField("Remaining").SetValue(entry, remaining);
    entryType.GetField("Total").SetValue(entry, total);
    list.GetType().GetMethod("Add").Invoke(list, new[] { entry });
}

static void ClearColdTimes(TPlayObject player)
{
    var list = Field("m_NativeColdTimes").GetValue(player);
    list.GetType().GetMethod("Clear").Invoke(list, null);
}

static TPlayObject NewPlayer()
{
    var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TPlayObject));
    player.m_PEnvir = new Envirnoment { Flag = new TMapFlag() };
    player.m_MsgList = new List<SendMessage>();
    return player;
}

// Touching M2Share runs its static ctor, which loads config files off disk.
static void PrepareRuntimeFiles()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static FieldInfo Field(string name)
    => typeof(TPlayObject).GetField(name,
           BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
       ?? throw new MissingFieldException(typeof(TPlayObject).FullName, name);

static void SetScript(TPlayObject player, byte[] raw)
    => Field("m_NativeScriptData").SetValue(player, raw);

static byte[] GetScript(TPlayObject player)
    => (byte[])Field("m_NativeScriptData").GetValue(player);

static MethodInfo Method(string name)
    => typeof(TPlayObject).GetMethod(name,
           BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
       ?? throw new MissingMethodException(typeof(TPlayObject).FullName, name);

static void Call(TPlayObject player, string name)
    => Method(name).Invoke(player, null);

static T CallFor<T>(TPlayObject player, string name, params object[] args)
    => (T)Method(name).Invoke(player, args.Length == 0 ? null : args);

static T Const<T>(string name)
{
    var field = typeof(TPlayObject).GetField(name,
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingFieldException(typeof(TPlayObject).FullName, name);
    return (T)Convert.ChangeType(field.GetRawConstantValue(), typeof(T));
}

static void RequireContains(string path, string needle, string label)
{
    if (!File.Exists(path))
        throw new InvalidOperationException($"{label}: missing file {path}");
    if (!File.ReadAllText(path).Contains(needle, StringComparison.Ordinal))
        throw new InvalidOperationException($"{label}: '{needle}' not found in {path}");
}

// Resolve the repository root from THIS SOURCE FILE's compile-time path, not
// from AppContext.BaseDirectory. The binary runs out of
// AuditTools/<name>/bin/Debug/net8.0-windows/, so a BaseDirectory-relative
// walk depends on how deep the output happens to be and breaks whenever the
// TFM or output layout changes. [CallerFilePath] is fixed at compile time and
// points at AuditTools/NativeScriptSectionsCheck/Program.cs.
static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

// Where the corpus actually lives today: a sibling of the checkout, NOT inside
// it. The checkout is D:/loym2/LyoMir2-master; the 34 blobs are at
// D:/loym2/staging/golden_scriptdata. Reported verbatim in the SKIP message.
static string CanonicalGoldenDirectory()
{
    var found = ProbeGoldenDirectories(out _);
    return found ?? Path.Combine(@"D:\loym2", "staging", "golden_scriptdata");
}

// Probe order: explicit M2_GOLDEN_SCRIPTDATA override, then every ancestor's
// staging/golden_scriptdata (worktrees sit several levels below D:\loym2),
// then the in-project goldens/ directory. A directory that exists but holds
// no *.bin is treated as absent, so an empty corpus cannot silently pass.
static string GoldenDirectoryCandidates(out string[] candidates)
{
    var fromEnvironment = Environment.GetEnvironmentVariable(
        "M2_GOLDEN_SCRIPTDATA");
    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
        // The override is AUTHORITATIVE, not merely first in line. Falling
        // through to the default locations when an explicit override turns out
        // to be empty or missing is a false-green generator: the run reports
        // goldens=34/34 from a corpus the operator did not select, so pointing
        // this variable at the wrong path looks like a pass. If it is set, it is
        // the only candidate, and an empty/absent directory yields SKIP+exit 2.
        candidates = new[] { Path.GetFullPath(fromEnvironment) };
        return Directory.Exists(candidates[0])
               && Directory.GetFiles(candidates[0], "*.bin").Length > 0
            ? candidates[0]
            : null;
    }
    return ProbeGoldenDirectories(out candidates);
}

static string ProbeGoldenDirectories(out string[] candidates)
{
    var root = FindRepositoryRoot();
    var probes = new List<string>();
    for (var dir = new DirectoryInfo(root); dir != null; dir = dir.Parent)
        probes.Add(Path.Combine(dir.FullName, "staging", "golden_scriptdata"));
    probes.Add(Path.Combine(root, "AuditTools", "NativeScriptSectionsCheck",
        "goldens"));
    candidates = probes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    foreach (var probe in candidates)
        if (Directory.Exists(probe) && Directory.GetFiles(probe, "*.bin").Length > 0)
            return probe;
    return null;
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

// Thrown when the corpus is absent, so the entry point can report SKIP and exit
// 2 (INCOMPLETE) instead of either throwing (FAIL) or quietly passing. A
// missing corpus must never render green. Declared last: in a top-level-
// statements file every type declaration must follow all local functions.
sealed class GoldensUnavailableException : Exception
{
    public GoldensUnavailableException(string message) : base(message) { }
}
