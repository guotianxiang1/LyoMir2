// TARoundList / TRoundCell - Zhanshen equivalence audit.
//
// Both classes are held per hero by THeroAct's ctor sub_6864C4:
//   0x686573  mov dl,1 / mov eax,[0x685534] / call 0x686338 -> [edi+0x630]  TARoundList
//   0x686585  mov dl,1 / mov eax,[0x68558C] / call 0x68641C -> [edi+0x634]  TRoundCell
// so they are per-hero state, not shared singletons.
//
// What this pins, and why each item is easy to "improve" wrongly later:
//   * TARoundList.Add appends only while count < 0x3F (0x6863A2 cmp ebx,0x3F / jl),
//     and once full writes through a mod-64 ring cursor at +0x210 (0x6863AD
//     and ebx,0x8000003F), so slot 63 is only ever reached via the ring path.
//   * TARoundList.IndexOf misses with -2, not -1 (0x6863EE mov edi,0xFFFFFFFE).
//   * TARoundList.Clear resets the count only (0x6863E0), leaving the records and
//     the ring cursor untouched.
//   * TRoundCell.Add has NO capacity check (0x686490..0x6864BC): the count advances
//     unconditionally even past the 9 records that fit before the count field.
//   * TRoundCell.GetByIndex copies the record out as two dwords (0x68647C), and the
//     hero mover reads word+4 of that record - field A - as a direction.
using GameSvr;

try
{
    CheckARoundListAppendsBelowTheLimit();
    CheckARoundListRingWrapsAtSixtyFour();
    CheckARoundListIndexOfScansNewestFirst();
    CheckARoundListMissIsMinusTwo();
    CheckARoundListClearResetsCountOnly();
    CheckRoundCellAddAndFetch();
    CheckRoundCellCountAdvancesPastCapacity();
    CheckRoundCellClearIsCountOnly();
    CheckSentinelsAreSeeded();

    Console.WriteLine(
        "PASS NativeARoundList TARoundList=64-slot ring (append < 0x3F, then mod-64 " +
        "cursor at +0x210); IndexOf newest-first, miss = -2 (not -1); Clear resets " +
        "count only. TRoundCell=9 records stride 8, Add has no capacity check and " +
        "advances the count regardless; GetByIndex copies {X,Y,A,B}; both ctors seed " +
        "only their two 0xFFFF sentinels.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeARoundListCheck FAIL: {exception}");
    return 1;
}

// ---------------------------------------------------------------------------
// TARoundList
// ---------------------------------------------------------------------------

static void CheckARoundListAppendsBelowTheLimit()
{
    var list = new TARoundList();
    Equal(0, list.Count, "a fresh list is empty");

    for (var i = 0; i < 10; i++)
    {
        list.Add((ushort)(100 + i), (ushort)(200 + i), (ushort)(300 + i));
    }

    Equal(10, list.Count, "each append below the limit bumps the count");
    // Newest-first scan finds the last one at index 9.
    Equal(9, list.IndexOf(109, 209), "the tenth append sits at index 9");
    Equal(0, list.IndexOf(100, 200), "the first append is still at index 0");
}

static void CheckARoundListRingWrapsAtSixtyFour()
{
    var list = new TARoundList();

    // 0x6863A2 `cmp ebx,0x3F / jl` — appends run while count < 63, so filling to 63
    // uses indices 0..62 and leaves the count pinned there.
    for (var i = 0; i < 63; i++)
    {
        list.Add((ushort)i, (ushort)i, 0);
    }
    Equal(63, list.Count, "the append path stops raising the count at 0x3F");

    // From here every Add goes through the ring cursor, which starts at 0. So the
    // next write lands on index 0 and overwrites the very first record.
    list.Add(0xAAAA, 0xBBBB, 0);
    Equal(63, list.Count, "a ring write does not raise the count");
    Equal(0, list.IndexOf(0xAAAA, 0xBBBB), "the ring write landed on index 0");
    Assert(list.IndexOf(0, 0) != 0, "index 0's original record was overwritten");

    // Cursor keeps advancing: the next two land on 1 and 2.
    list.Add(0xCCCC, 0xDDDD, 0);
    Equal(1, list.IndexOf(0xCCCC, 0xDDDD), "the cursor advanced to index 1");

    // Drive the cursor a full lap: 64 more writes bring it back to index 1.
    for (var i = 0; i < 63; i++)
    {
        list.Add((ushort)(0x1000 + i), (ushort)(0x2000 + i), 0);
    }
    list.Add(0xEEEE, 0xFFFE, 0);
    Equal(1, list.IndexOf(0xEEEE, 0xFFFE),
        "after a full lap of 64 the cursor is back on index 1");
}

static void CheckARoundListIndexOfScansNewestFirst()
{
    var list = new TARoundList();
    list.Add(7, 7, 0);
    list.Add(8, 8, 0);
    list.Add(7, 7, 0);   // same cell again, later slot

    // 0x6863F3 starts at count-1 and walks down, returning the FIRST hit, so the
    // newer duplicate wins.
    Equal(2, list.IndexOf(7, 7), "the newest matching record is returned");
}

static void CheckARoundListMissIsMinusTwo()
{
    var list = new TARoundList();
    Equal(-2, list.IndexOf(1, 1), "an empty list misses with -2");

    list.Add(1, 1, 0);
    Equal(-2, list.IndexOf(2, 2), "a populated list still misses with -2");
    Equal(TARoundList.NotFound, list.IndexOf(2, 2), "the constant agrees");
    Assert(TARoundList.NotFound == -2,
        "0x6863EE seeds -2; -1 would be a different contract");
}

static void CheckARoundListClearResetsCountOnly()
{
    var list = new TARoundList();
    for (var i = 0; i < 5; i++)
    {
        list.Add((ushort)(10 + i), (ushort)(20 + i), 0);
    }

    list.Clear();
    Equal(0, list.Count, "Clear zeroes the count");
    Equal(-2, list.IndexOf(10, 20), "cleared records are no longer visible");

    // 0x6863E0 touches nothing else, so re-adding starts back at index 0 and the
    // stale record is simply overwritten rather than having been wiped.
    list.Add(99, 99, 0);
    Equal(0, list.IndexOf(99, 99), "after Clear the next append is index 0");
}

// ---------------------------------------------------------------------------
// TRoundCell
// ---------------------------------------------------------------------------

static void CheckRoundCellAddAndFetch()
{
    var cell = new TRoundCell();
    Equal(0, cell.Count, "a fresh cell list is empty");

    cell.Add(11, 22, 33, 44);
    cell.Add(55, 66, 77, 88);
    Equal(2, cell.Count, "each Add bumps the count");

    cell.GetByIndex(0, out var x, out var y, out var a, out var b);
    Equal((ushort)11, x, "record 0 X");
    Equal((ushort)22, y, "record 0 Y");
    Equal((ushort)33, a, "record 0 A — the direction the mover reads");
    Equal((ushort)44, b, "record 0 B");

    cell.GetByIndex(1, out x, out y, out a, out b);
    Equal((ushort)55, x, "record 1 X");
    Equal((ushort)88, b, "record 1 B");
}

static void CheckRoundCellCountAdvancesPastCapacity()
{
    var cell = new TRoundCell();

    // 9 records fit between +0x004 and the count at +0x04C.
    for (var i = 0; i < TRoundCell.SlotCount; i++)
    {
        cell.Add((ushort)i, (ushort)i, (ushort)i, 0);
    }
    Equal(9, cell.Count, "nine records fill the array");
    Equal(9, TRoundCell.SlotCount, "(0x4C - 0x04) / 8 = 9");

    // Native has no bound check: 0x6864BC increments the count unconditionally, so a
    // tenth Add still advances it. This is original behaviour and must not be
    // "fixed" into a clamp.
    cell.Add(0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF);
    Equal(10, cell.Count,
        "the count advances past capacity, exactly as 0x6864BC does");

    // The stored records are untouched by that overflowing write.
    cell.GetByIndex(8, out _, out _, out var a8, out _);
    Equal((ushort)8, a8, "the last in-range record survived the overflowing Add");
}

static void CheckRoundCellClearIsCountOnly()
{
    var cell = new TRoundCell();
    cell.Add(1, 2, 3, 4);
    cell.Clear();
    Equal(0, cell.Count, "sub_686474 zeroes the count");

    cell.Add(9, 8, 7, 6);
    cell.GetByIndex(0, out var x, out _, out _, out _);
    Equal((ushort)9, x, "after Clear the next Add reuses index 0");
}

static void CheckSentinelsAreSeeded()
{
    // The ctors write nothing but their two 0xFFFF words — 0x686344/0x68634D for
    // TARoundList, 0x686428/0x68642E for TRoundCell — and everything else relies on
    // the zero fill Delphi already did. No reader for them was recovered, so the
    // only thing to assert is that they are seeded rather than left at zero.
    var list = new TARoundList();
    Equal((ushort)0xFFFF, list.SentinelA, "TARoundList +0x214 seeded 0xFFFF");
    Equal((ushort)0xFFFF, list.SentinelB, "TARoundList +0x216 seeded 0xFFFF");

    var cell = new TRoundCell();
    Equal((ushort)0xFFFF, cell.SentinelA, "TRoundCell +0x050 seeded 0xFFFF");
    Equal((ushort)0xFFFF, cell.SentinelB, "TRoundCell +0x052 seeded 0xFFFF");
}

// ---------------------------------------------------------------------------

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: {actual}, expected {expected}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}
