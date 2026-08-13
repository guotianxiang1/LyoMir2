// Pins the 元宝寄售 list queries, CM 1252 / 1253 / 1256 / 1257 — the only four members of the
// CM MISSING backlog that carry real production traffic (1253 96 hits, the other three 64 each).
//
// The four arms look interchangeable: each is `mov eax,[ebp-4] / call <thunk>`, each thunk is six
// lines that hand self+0x106 to one method of the manager at [[0x7D6ABC]], and no packet field is
// read anywhere. The differences are all INSIDE the manager methods, and three of them are easy
// to get wrong by assuming the four share a value:
//
//   throttle rule   1252/1253 need > 10ms, 1257 needs > 2ms, 1256 only needs a different tick
//   throttle slot   1252/1253 share manager+0x20, 1256/1257 share manager+0x24
//   row cap         1253 caps at 4, the other three at 8
//   empty-page skip 1253/1256/1257 skip the page query on a zero count, 1252 does not
//
// A first pass at this landed 1253 and 1257 with a "> 0ms" rule that exists nowhere in the image.
// That is what this tool exists to catch: every value in NativeYbConsignmentQuery.Descriptors is
// re-derived here from the bytes it claims to quote.

using GameSvr.Services;

var asserts = 0;
var image = LoadNativeImage();

CheckDispatch();
CheckThrottleSites();
CheckCapSites();
CheckSelectorSites();
CheckReplyIdentTranslation();
CheckMapGate();
CheckWireRecordLayout();
CheckDescriptorTableMatchesImage();
CheckThrottlePredicate();
CheckThrottleSlotsAreServerWide();
CheckEmptyStoreProducesEmptyReply();

Console.WriteLine($"NativeYbConsignmentQueryCheck PASS idents=4 asserts={asserts}");

// ---------------------------------------------------------------------------
// native side
// ---------------------------------------------------------------------------

void CheckDispatch()
{
    // 0x6D8300 `05 50 FB FF FF add eax,-1200` / 0x6D8305 `83 F8 3A cmp eax,0x3A` /
    // 0x6D830E `FF 24 85 15 83 6D 00 jmp [eax*4+0x6D8315]`, so slot N is ident 1200+N.
    // The adjust is at 0x6D8300, not 0x6D82FF: a disassembly window started one byte early
    // swallows the trailing 00 of the preceding `0F 84 EF 23 00 00 je 0x6DA6EF`.
    Pin(0x006D8300, "0550FBFFFF 83F83A 0F871E390000 FF248515836D00",
        "0x6D8300 jump table base -1200, bound 0x3A, table at 0x6D8315");

    Pin(TableSlot(52), "85A66D00", "slot 52 -> 0x6DA685 (ident 1252)");
    Pin(TableSlot(53), "92A66D00", "slot 53 -> 0x6DA692 (ident 1253)");
    Pin(TableSlot(56), "D5A66D00", "slot 56 -> 0x6DA6D5 (ident 1256)");
    Pin(TableSlot(57), "E2A66D00", "slot 57 -> 0x6DA6E2 (ident 1257)");

    // Each arm in full, including the unconditional jmp to the drop stub: proof that nothing
    // else happens on the dispatch side and that no header field is touched.
    Pin(0x006DA685, "8B45FCE8AFD70000E99A150000", "CM 1252 arm -> 0x6E7E3C");
    Pin(0x006DA692, "8B45FCE8F6D70000E98D150000", "CM 1253 arm -> 0x6E7E90");
    Pin(0x006DA6D5, "8B45FCE8CFDC0000E94A150000", "CM 1256 arm -> 0x6E83AC");
    Pin(0x006DA6E2, "8B45FCE816DD0000E93D150000", "CM 1257 arm -> 0x6E8400");

    // All four thunks take the character name out of the ShortString at self+0x106.
    foreach (var thunk in new[] { 0x006E7E55, 0x006E7EA9, 0x006E83C5, 0x006E8419 })
        Pin(thunk, "8D9306010000", $"thunk at 0x{thunk:X6}: lea edx,[self+0x106]");
}

void CheckThrottleSites()
{
    // sub / cmp / conditional jump / write-back, per ident. The write-back always sits AFTER
    // the branch, so a throttled request leaves the slot alone.
    Pin(0x00632A63, "2B562083FA0A0F86B4000000894620",
        "CM 1252 throttle: slot +0x20, cmp 0x0A, jbe, then store");
    Pin(0x00632ECB, "2B562083FA0A0F86C4000000894620",
        "CM 1253 throttle: slot +0x20, cmp 0x0A, jbe, then store");
    Pin(0x00632C3B, "2B56240F84C4000000894624",
        "CM 1256 throttle: slot +0x24, no cmp at all, je on the sub's ZF, then store");
    Pin(0x00632D83, "2B562483FA020F86C4000000894624",
        "CM 1257 throttle: slot +0x24, cmp 2, jbe, then store");
}

void CheckCapSites()
{
    Pin(0x00632A8D, "837DFC087E07C745FC08000000", "CM 1252 cap 8");
    Pin(0x00632EF5, "837DFC04", "CM 1253 cap 4");
    Pin(0x00632C62, "837DFC087E07", "CM 1256 cap 8");
    Pin(0x00632DAD, "837DFC08", "CM 1257 cap 8");

    // The empty-page skip exists in three of the four.
    Pin(0x00632F2C, "837DFC007E27", "CM 1253 skips the page query when the count is 0");
    Pin(0x00632C99, "837DFC007E27", "CM 1256 skips the page query when the count is 0");
    Pin(0x00632DE4, "837DFC007E27", "CM 1257 skips the page query when the count is 0");
    // 1252 has no such test: the count goes straight into the call.
    Pin(0x00632AD7, "8B45FC50", "CM 1252 pushes the count with no zero test");
    Pin(0x00632AF1, "E8D2DDFFFF", "CM 1252 calls the page fn 0x6308C8 unconditionally");

    // Intermediate record stride, shared by all four.
    Pin(0x00632AA1, "69 45 FC 4A 08 00 00", "record stride 0x84A");
}

void CheckSelectorSites()
{
    Pin(0x00632B0E, "B97A040000", "CM 1252 selector 0x47A");
    Pin(0x00632F86, "B97B040000", "CM 1253 selector 0x47B");
    Pin(0x00632CF3, "B980040000", "CM 1256 selector 0x480");
    Pin(0x00632E3E, "B981040000", "CM 1257 selector 0x481");
}

void CheckReplyIdentTranslation()
{
    // sub_6E80CC turns the selector into the SM ident with a subtract ladder, not a table:
    // sub 0x47A / je, dec / je, sub 5 / je, dec / je, else return without sending.
    Pin(0x006E80E1, "2D7A040000 7410 48 741E 83E805 742A 48 7430",
        "0x6E80E1 selector ladder 0x47A / 0x47B / 0x480 / 0x481");
    Pin(0x006E80F3, "E9F0010000", "unknown selector -> 0x6E82E8, return with no reply");

    Pin(0x006E80F8, "C745F0B90B0000", "0x47A -> SM 0xBB9 (3001)");
    Pin(0x006E8109, "C745F0BA0B0000", "0x47B -> SM 0xBBA (3002)");
    Pin(0x006E811A, "C745F0BD0B0000", "0x480 -> SM 0xBBD (3005)");
    Pin(0x006E8123, "C745F0BE0B0000", "0x481 -> SM 0xBBE (3006)");

    // Only the two pending selectors tear the cached list down before serialising.
    Pin(0x006E8102, "E829FEFFFF", "0x47A runs the list teardown 0x6E7F30");
    Pin(0x006E8113, "E8CCFDFFFF", "0x47B runs the list teardown 0x6E7EE4");

    // The send: ident in DX, Recog 0, wParam = the row count, Tag and Series 0.
    Pin(0x006E82BB,
        "668B4508 50 6A00 6A00 8B45D4 50 8B45EC 50 33C9 668B55F0 8B45FC 8B18 FF9354020000",
        "0x6E82BB send through [vmt+0x254]: wParam = row count, Tag and Series 0");
}

void CheckMapGate()
{
    // 0x40BDCC returns a BOOLEAN. Reading it as CompareText inverts both call sites.
    Pin(0x0040BDCC, "39D0741909C0741709D274148B48FC3B4AFC750CE893FFFFFF85C07503B001C331C0C3",
        "0x40BDCC is SameText: al=1 on equal, al=0 otherwise");
    Pin(0x00632686, "84C0751F", "first map compare: non-zero -> 0x6326A9 mov bl,1");
    Pin(0x006326A5, "84C07402", "second map compare: zero -> skip mov bl,1");
    Pin(0x006326A9, "B301", "0x6326A9 mov bl,1 is the only success write");

    Pin(0x0063267C, "BAE0266300", "first literal is 0x6326E0");
    Pin(0x0063269B, "BAEC266300", "second literal is 0x6326EC");
    // Delphi length prefix then the bytes then the terminator.
    Pin(0x006326DC, "0300000067613000", "0x6326E0 declen 3 \"ga0\"");
    Pin(0x006326E8, "04000000534C444700", "0x6326EC declen 4 \"SLDG\"");

    Assert(NativeYbConsignmentQuery.AllowedMapA == "ga0", "AllowedMapA");
    Assert(NativeYbConsignmentQuery.AllowedMapB == "SLDG", "AllowedMapB");
    Assert(NativeYbConsignmentQuery.MapAllowsConsignmentQuery("GA0"), "map gate is case-insensitive");
    Assert(!NativeYbConsignmentQuery.MapAllowsConsignmentQuery("0"), "map gate rejects other maps");
    Assert(!NativeYbConsignmentQuery.MapAllowsConsignmentQuery(null), "map gate rejects nil");
}

void CheckWireRecordLayout()
{
    Pin(0x006E818B, "B10F", "wire name is a ShortString capped at 15");
    Pin(0x006E8198, "894610", "[dst+0x10] = Idx");
    Pin(0x006E81A1, "894614", "[dst+0x14] = Credit");
    Pin(0x006E81AA, "88461A", "[dst+0x1A] = [src+0x19]");
    Pin(0x006E81B3, "884619", "[dst+0x19] = [src+0x18] (ConsState)");
    Pin(0x006E81BC, "88461B", "[dst+0x1B] = [src+0x1A]");
    Pin(0x006E81C6, "6689461C", "[dst+0x1C] = userLv (word)");
    Pin(0x006E81D0, "895620", "[dst+0x20] = TDateTime low");
    Pin(0x006E81D6, "895624", "[dst+0x24] = TDateTime high");
    Pin(0x006E81D9, "C6461800", "[dst+0x18] = 0, the emitted-item counter");
    Pin(0x006E8237, "FE4618", "inc [dst+0x18] once per emitted item");

    // The source blob is a fixed ten slots of 0xD0 bytes at src+0x2A.
    Pin(0x006E81F3, "6BC31A", "imul ebx,0x1A");
    Pin(0x006E81F9, "8D44C22A", "lea [src + ebx*8*0x1A + 0x2A] = src + 0x2A + ebx*0xD0");
    Pin(0x006E823B, "83FB0A75B3", "ten slots exactly");
    Pin(0x006E8203, "6683780400 7630", "slot is emitted only when word[slot+4] > 0");

    // But the emitted payload is variable: length advances by 0x28 plus the encoder's own size.
    Pin(0x006E8275, "8B55EC83C22803C28945EC", "running length += 0x28 + encoded item bytes");

    Assert(NativeYbConsignmentQuery.WireRecordHeaderSize == 0x28, "wire header size");
    Assert(NativeYbConsignmentQuery.RecordSize == 0x84A, "record stride");
    Assert(NativeYbConsignmentQuery.RecordBlobOffset == 0x2A, "blob offset");
    Assert(NativeYbConsignmentQuery.RecordBlobSize == 0x820, "blob size = 10 * 0xD0");
    Assert(NativeYbConsignmentQuery.RecordBlobSize == 10 * 0xD0, "blob size is ten slots");
    Assert(NativeYbConsignmentQuery.RecordNameCapacity == 0x0F, "name capacity");
}

// ---------------------------------------------------------------------------
// the C# table, re-derived from the sites above
// ---------------------------------------------------------------------------

void CheckDescriptorTableMatchesImage()
{
    var expected = new (int Cm, int ManagerVa, int Selector, int Sm,
        NativeYbConsignmentQuery.ThrottleSlot Slot, NativeYbConsignmentQuery.ThrottleRule Rule,
        int Cap, bool Clears, bool SkipsPage)[]
    {
        (1252, 0x632A14, 0x47A, 3001, NativeYbConsignmentQuery.ThrottleSlot.Pending,
            NativeYbConsignmentQuery.ThrottleRule.MoreThanTenMs, 8, true, false),
        (1253, 0x632E7C, 0x47B, 3002, NativeYbConsignmentQuery.ThrottleSlot.Pending,
            NativeYbConsignmentQuery.ThrottleRule.MoreThanTenMs, 4, true, true),
        (1256, 0x632BEC, 0x480, 3005, NativeYbConsignmentQuery.ThrottleSlot.History,
            NativeYbConsignmentQuery.ThrottleRule.DifferentTick, 8, false, true),
        (1257, 0x632D34, 0x481, 3006, NativeYbConsignmentQuery.ThrottleSlot.History,
            NativeYbConsignmentQuery.ThrottleRule.MoreThanTwoMs, 8, false, true),
    };

    Assert(NativeYbConsignmentQuery.Descriptors.Count == expected.Length,
        "exactly four idents are modelled");

    foreach (var e in expected)
    {
        Assert(NativeYbConsignmentQuery.TryGetDescriptor(e.Cm, out var d), $"descriptor {e.Cm}");
        Assert(d.ManagerVa == e.ManagerVa, $"{e.Cm} manager VA");
        Assert(d.Selector == e.Selector, $"{e.Cm} selector");
        Assert(d.SmIdent == e.Sm, $"{e.Cm} reply ident");
        Assert(d.Slot == e.Slot, $"{e.Cm} throttle slot");
        Assert(d.Rule == e.Rule, $"{e.Cm} throttle rule");
        Assert(d.Cap == e.Cap, $"{e.Cm} row cap");
        Assert(d.ClearsCachedList == e.Clears, $"{e.Cm} cached-list teardown");
        Assert(d.SkipsPageWhenEmpty == e.SkipsPage, $"{e.Cm} empty-page skip");

        // Both statements name the character through a single %s and nothing else.
        Assert(CountOccurrences(d.CountSql, "%s") == 1, $"{e.Cm} count SQL has one %s");
        Assert(CountOccurrences(d.PageSql, "%s") == 1, $"{e.Cm} page SQL has one %s");
        Assert(d.PageSql.Contains("Limit " + e.Cap, StringComparison.OrdinalIgnoreCase),
            $"{e.Cm} page SQL LIMIT agrees with the cap");
    }

    // The two pending views read SellItems, the two history views read ybDealHis.
    AssertSqlTable(1252, "SellItems", "TargetName");
    AssertSqlTable(1253, "SellItems", "CharName");
    AssertSqlTable(1256, "ybDealHis", "TargetName");
    AssertSqlTable(1257, "ybDealHis", "CharName");

    // Only 1253 selects the status column, which is why only it fills ConsState.
    Assert(NativeYbConsignmentQuery.Descriptors[1253].PageSql.Contains("Status+0 as ConsState"),
        "1253 selects ConsState");
    foreach (var cm in new[] { 1252, 1256, 1257 })
        Assert(!NativeYbConsignmentQuery.Descriptors[cm].PageSql.Contains("ConsState"),
            $"{cm} does not select ConsState");
}

void AssertSqlTable(int cm, string table, string keyColumn)
{
    var d = NativeYbConsignmentQuery.Descriptors[cm];
    Assert(d.CountSql.Contains(table, StringComparison.OrdinalIgnoreCase), $"{cm} count table");
    Assert(d.PageSql.Contains(table, StringComparison.OrdinalIgnoreCase), $"{cm} page table");
    Assert(d.CountSql.Contains(keyColumn + "=\"%s\""), $"{cm} count key column");
    Assert(d.PageSql.Contains(keyColumn + "=\"%s\""), $"{cm} page key column");
}

void CheckThrottlePredicate()
{
    var tenMs = NativeYbConsignmentQuery.ThrottleRule.MoreThanTenMs;
    var twoMs = NativeYbConsignmentQuery.ThrottleRule.MoreThanTwoMs;
    var tick = NativeYbConsignmentQuery.ThrottleRule.DifferentTick;

    // `cmp edx,0x0A / jbe` rejects at exactly 10 and passes at 11.
    Assert(!NativeYbConsignmentQuery.ThrottleAllows(tenMs, 10), "10ms is rejected");
    Assert(NativeYbConsignmentQuery.ThrottleAllows(tenMs, 11), "11ms passes");
    Assert(!NativeYbConsignmentQuery.ThrottleAllows(tenMs, 0), "0ms is rejected");

    Assert(!NativeYbConsignmentQuery.ThrottleAllows(twoMs, 2), "2ms is rejected");
    Assert(NativeYbConsignmentQuery.ThrottleAllows(twoMs, 3), "3ms passes");

    // The `je` arm has no magnitude at all: 1ms is enough, 0 is not.
    Assert(!NativeYbConsignmentQuery.ThrottleAllows(tick, 0), "same tick is rejected");
    Assert(NativeYbConsignmentQuery.ThrottleAllows(tick, 1), "1ms passes the tick rule");

    // A tick that went backwards. `jbe` is unsigned AND it is the reject arm: 0x632A69
    // `jbe 0x632B23` lands on the epilogue, past the emitter call at 0x632B17, and past the
    // write-back at 0x632A6F. So `sub` yielding 0xFFFFFFFF is ABOVE 0x0A, the branch is not
    // taken, and the request goes through. All three rules let the wrapped case pass; the
    // original has no wrap guard anywhere. This block used to assert the opposite, which is
    // a behaviour the image does not contain.
    Assert(NativeYbConsignmentQuery.ThrottleAllows(tenMs, -1), "wrapped tick passes the 10ms rule");
    Assert(NativeYbConsignmentQuery.ThrottleAllows(twoMs, -1), "wrapped tick passes the 2ms rule");
    Assert(NativeYbConsignmentQuery.ThrottleAllows(tick, -1), "wrapped tick differs");
    Assert(NativeYbConsignmentQuery.ThrottleAllows(tenMs, int.MinValue),
        "the whole negative half-plane is above 0x0A once unsigned");
}

void CheckThrottleSlotsAreServerWide()
{
    NativeYbConsignmentQuery.ResetThrottleSlots();
    var pending = NativeYbConsignmentQuery.Descriptors[1252];
    var otherPending = NativeYbConsignmentQuery.Descriptors[1253];
    var history = NativeYbConsignmentQuery.Descriptors[1256];

    Assert(NativeYbConsignmentQuery.TryPassThrottle(pending, 1000), "first pending request passes");
    // manager+0x20 is a field of a singleton, so 1253 now sees 1252's tick.
    Assert(!NativeYbConsignmentQuery.TryPassThrottle(otherPending, 1005),
        "1253 is throttled by 1252's tick: the slot is server-wide");
    Assert(NativeYbConsignmentQuery.TryPassThrottle(otherPending, 1011),
        "and clears once past 10ms");
    // The history slot is independent.
    Assert(NativeYbConsignmentQuery.TryPassThrottle(history, 1011),
        "manager+0x24 is untouched by the pending pair");
    NativeYbConsignmentQuery.ResetThrottleSlots();
}

void CheckEmptyStoreProducesEmptyReply()
{
    // With no gamedata.SellItems table the count query leaves EBX at 0 (0x630C08 `48 dec eax /
    // 75 1B jne`), the serialisation loop is skipped by 0x6E8150 `85 C0 test eax,eax / jle`,
    // and the reply still goes out with wParam 0 and an empty body.
    Pin(0x00630C08, "48751B", "count fn: a failed statement leaves the count at 0");
    Pin(0x006E8150, "85C00F8E63010000", "zero rows -> skip the whole loop");

    Assert(NativeYbConsignmentQuery.Store.Count(1252, "nobody") == 0, "default store counts 0");
    Assert(NativeYbConsignmentQuery.Store.Page(1252, "nobody", 8).Count == 0,
        "default store pages 0");
    Assert(NativeYbConsignmentQuery.BuildReplyBody(Array.Empty<NativeYbConsignmentQuery.Record>())
        .Length == 0, "no rows means no body");

    // One row serialises to exactly the 0x28 header plus its payload, in that order.
    var row = new NativeYbConsignmentQuery.Record
    {
        CounterpartyName = "abc",
        Idx = 0x11223344,
        Credit = 0x55667788,
        ConsState = 2,
        UserLv = 0x0102,
        ItemCount = 1,
        ItemPayload = new byte[] { 0xAA, 0xBB },
    };
    var body = NativeYbConsignmentQuery.BuildReplyBody(new[] { row });
    Assert(body.Length == 0x28 + 2, "one row = header + payload");
    Assert(body[0] == 3 && body[1] == (byte)'a', "ShortString length prefix then the bytes");
    Assert(BitConverter.ToInt32(body, 0x10) == 0x11223344, "Idx at +0x10");
    Assert(BitConverter.ToInt32(body, 0x14) == 0x55667788, "Credit at +0x14");
    Assert(body[0x18] == 1, "emitted item count at +0x18");
    Assert(body[0x19] == 2, "ConsState at +0x19");
    Assert(BitConverter.ToUInt16(body, 0x1C) == 0x0102, "userLv at +0x1C");
    Assert(body[0x28] == 0xAA && body[0x29] == 0xBB, "payload starts at +0x28");

    // A name longer than 15 bytes is cut on a byte boundary, matching 0x4039E4 with CL = 0x0F.
    var longName = NativeYbConsignmentQuery.BuildReplyBody(new[]
    {
        new NativeYbConsignmentQuery.Record { CounterpartyName = new string('x', 40) }
    });
    Assert(longName[0] == 0x0F, "name is capped at 15 bytes");
}

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------

int TableSlot(int index) => 0x006D8315 + index * 4;

int CountOccurrences(string haystack, string needle)
{
    var n = 0;
    for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
         i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        n++;
    return n;
}

static byte[] LoadNativeImage()
{
    const string known = @"D:\loym2\staging\_reunpack_work\flat_image.bin";
    if (!File.Exists(known))
        throw new InvalidOperationException("flat_image.bin not found at " + known);
    return File.ReadAllBytes(known);
}

void Pin(int va, string expectedHex, string label)
{
    const int imageBase = 0x400000;
    var offset = va - imageBase;
    var expected = Convert.FromHexString(expectedHex.Replace(" ", string.Empty));
    Assert(offset >= 0 && offset + expected.Length <= image.Length, label + " range");
    for (var i = 0; i < expected.Length; i++)
    {
        if (image[offset + i] != expected[i])
            throw new InvalidOperationException(
                $"{label}: byte[{i}] at 0x{va + i:X6} expected={expected[i]:X2} " +
                $"actual={image[offset + i]:X2}");
    }
    asserts++;
}

void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    asserts++;
}
