using GameSvr.Services;

// ---------------------------------------------------------------------------
// Asserts the dormant BlockUsers.Dat deny/ban-list contract reversed from
// M2Server_unpacked_fixed.exe (m2full.i64). Fail-loud on any deviation.
// ---------------------------------------------------------------------------

// --- 1. Record geometry constants (Delphi packed record string[15] + Integer) ---
Equal(20, NativeBlockUserRecordCodec.RecordSize, "record size");
Equal(15, NativeBlockUserRecordCodec.NameCapacity, "name capacity (string[15])");
Equal(16, NativeBlockUserRecordCodec.NameFieldSize, "name field (len byte + 15 chars)");
Equal(16, NativeBlockUserRecordCodec.ValueOffset, "Integer offset");

// --- 2. Load-size guard: only exact multiples of 20 are valid ---
Equal(true, NativeBlockUserRecordCodec.IsValidImageLength(0), "empty image valid");
Equal(true, NativeBlockUserRecordCodec.IsValidImageLength(20), "one record valid");
Equal(true, NativeBlockUserRecordCodec.IsValidImageLength(200), "ten records valid");
Equal(false, NativeBlockUserRecordCodec.IsValidImageLength(19), "19 rejected");
Equal(false, NativeBlockUserRecordCodec.IsValidImageLength(21), "21 rejected");
Equal(false, NativeBlockUserRecordCodec.IsValidImageLength(-20), "negative rejected");
Equal(10, NativeBlockUserRecordCodec.RecordCount(200), "record count");

// --- 3. Single-record codec round trip (incl. truncation + full Int32 range) ---
RecordRoundTrip("abc", 42);
RecordRoundTrip("", 0);
RecordRoundTrip("123456789012345", int.MaxValue);        // exactly 15 chars
RecordRoundTrip("1234567890123456789", 100, "123456789012345"); // > 15 -> truncated
RecordRoundTrip("perm", -1);                             // negative seconds preserved
RecordRoundTrip("x", int.MinValue);

// length prefix + little-endian layout, byte-exact
{
    var buf = new byte[20];
    NativeBlockUserRecordCodec.EncodeRecord(buf, 0, System.Text.Encoding.Latin1.GetBytes("AB"), 0x04030201);
    Equal(2, buf[0], "length prefix byte");
    Equal((byte)'A', buf[1], "name byte 0");
    Equal((byte)'B', buf[2], "name byte 1");
    Equal(0, buf[3], "name padding zeroed");
    Equal(0x01, buf[16], "value LE byte0");
    Equal(0x02, buf[17], "value LE byte1");
    Equal(0x03, buf[18], "value LE byte2");
    Equal(0x04, buf[19], "value LE byte3");
}

// File adapter: all writes stay in an isolated temp directory.
{
    var root = Path.Combine(Path.GetTempPath(),
        "native-block-users-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(root, "BlockUsers.Dat");
    try
    {
        var store = new NativeBlockUserFileStore(path);
        Equal(null, store.Load(), "absent file store load");
        var image = BuildImage(("file", 17));
        store.Save(image);
        Equal(Convert.ToHexString(image), Convert.ToHexString(store.Load()),
            "file store byte-exact save/load");
        store.Delete();
        Equal(false, File.Exists(path), "file store delete");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

// --- 4. Load: valid image populates; invalid image loads nothing ---
{
    var img = BuildImage(("alice", 30), ("bob", 60));
    var store = new FakeStore { Data = img };
    var list = new NativeGmBlockUserList(store);
    list.Load(1000);
    Equal(2, list.Count, "valid image loads 2");
    Equal(true, list.Contains("alice"), "alice loaded");
    Equal(true, list.Contains("BOB"), "case-insensitive hit");

    var duplicateStore = new FakeStore
    {
        Data = BuildImage(("dup", 7), ("dup", 9))
    };
    var duplicates = new NativeGmBlockUserList(duplicateStore);
    duplicates.Load(1000);
    Equal(2, duplicates.Count, "duplicate records are retained on load");
    Equal(true, duplicates.Contains("dUp"), "duplicate ASCII-fold lookup");
    Equal(true, duplicates.Delete("dup"), "delete removes every matching duplicate");
    Equal(0, duplicates.Count, "all matching duplicate records are removed");
    Equal(1, duplicateStore.SaveCount, "first duplicate removal persists survivor");
    Equal(1, duplicateStore.DeleteCount, "last duplicate removal deletes file");

    var bad = new byte[21]; // 21 % 20 != 0
    var list2 = new NativeGmBlockUserList(new FakeStore { Data = bad });
    list2.Load(1000);
    Equal(0, list2.Count, "size%20!=0 loads nothing");

    var list3 = new NativeGmBlockUserList(new FakeStore { Data = null });
    list3.Load(1000);
    Equal(0, list3.Count, "absent file loads nothing");

    var malformed = new byte[20];
    malformed[0] = 15;
    for (var i = 0; i < 14; i++) malformed[1 + i] = (byte)('a' + i);
    malformed[15] = 0x81; // invalid trailing byte: must survive decode/save
    malformed[16] = 7;
    var malformedStore = new FakeStore { Data = (byte[])malformed.Clone() };
    var malformedList = new NativeGmBlockUserList(
        malformedStore, encoding: System.Text.Encoding.UTF8);
    malformedList.Load(1000);
    malformedList.Save();
    Equal(Convert.ToHexString(malformed), Convert.ToHexString(malformedStore.LastSaved),
        "malformed ShortString bytes round trip losslessly");
}

// --- 5. Add: new name creates+saves+flags; existing name extends WITHOUT saving ---
{
    var store = new FakeStore();
    var sink = new FakeSink();
    var list = new NativeGmBlockUserList(store, sink);

    var r1 = list.Add("carol", 100, 5000);
    Equal(100, r1, "new add returns seconds");
    Equal(1, store.SaveCount, "new add saves");
    Equal(true, sink.State["carol"], "new add sets player flag");
    Equal(20, store.LastSaved.Length, "saved one 20-byte record");

    var savesBefore = store.SaveCount;
    var r2 = list.Add("carol", 50, 6000);
    Equal(150, r2, "extend accumulates seconds");
    Equal(savesBefore, store.SaveCount, "extend does NOT save");
    Equal(1, list.Count, "extend keeps single entry");

    var nonAscii = new NativeGmBlockUserList(new FakeStore());
    nonAscii.Add("Ä", 10, 0);
    nonAscii.Add("ä", 20, 0);
    Equal(2, nonAscii.Count, "non-ASCII case variants remain distinct");
}

// --- 6. Delete: removes+saves+clears flag; absent name is a no-op ---
{
    var store = new FakeStore();
    var sink = new FakeSink();
    var list = new NativeGmBlockUserList(store, sink);
    list.Add("dave", 100, 1000);
    var savesBefore = store.SaveCount;

    Equal(false, list.Delete("nobody"), "absent delete false");
    Equal(savesBefore, store.SaveCount, "absent delete does not save");

    Equal(true, list.Delete("dave"), "present delete true");
    Equal(false, list.Contains("dave"), "entry gone");
    Equal(false, sink.State["dave"], "delete clears flag");
    // list is now empty -> Save() deletes the file
    Equal(1, store.DeleteCount, "empty list save deletes file");
}

// --- 7. Tick: gated by 10s, decrements by whole elapsed seconds, expires at <=0 ---
{
    var store = new FakeStore();
    var sink = new FakeSink();
    var list = new NativeGmBlockUserList(store, sink);
    list.Add("erin", 30, 0);            // 30s remaining, stamped at t=0

    list.Tick(5000);                    // 5s <= 10s gate -> no sweep
    Equal(30, First(list).RemainSeconds, "sub-interval tick is a no-op");

    list.Tick(20000);                   // 20s elapsed -> decrement by 20
    Equal(10, First(list).RemainSeconds, "sweep subtracts elapsed seconds");
    Equal(true, list.Contains("erin"), "still muted");

    var deletesBefore = store.DeleteCount;
    list.Tick(40000);                   // +20s -> 10-20 = -10 -> expire (sole entry -> list drains empty)
    Equal(false, list.Contains("erin"), "expired entry removed");
    Equal(false, sink.State["erin"], "expiry clears flag");
    // Native sub_622040 sweep calls the persist (sub_622630) once when anything expired
    // (v13=1). With the list now empty (a1[9] <= 0), sub_622630 DELETES BlockUsers.Dat
    // (sub_40D084) instead of writing it. So the persist side-effect of draining the last
    // entry is a file delete, not a save.
    Equal(deletesBefore + 1, store.DeleteCount, "expiry of last entry deletes the file");
}

// --- 7b. Expiry WITH a survivor: non-empty persist writes the file (sub_622630 else-branch) ---
{
    var store = new FakeStore();
    var sink = new FakeSink();
    var list = new NativeGmBlockUserList(store, sink);
    list.Add("shortlived", 5, 0);       // expires first
    list.Add("longlived", 999, 0);      // survives the sweep
    var savesBefore = store.SaveCount;
    list.Tick(20000);                   // 20s: shortlived 5-20<=0 expire, longlived 999-20 survives
    Equal(false, list.Contains("shortlived"), "short entry expired");
    Equal(true, list.Contains("longlived"), "long entry survives");
    Equal(1, list.Count, "one survivor remains");
    Equal(savesBefore + 1, store.SaveCount, "expiry with a survivor writes the file (a1[9] > 0)");
}

// Duplicate names are possible after Load. sub_622040 clears player+2969 as soon
// as either duplicate expires; it does not re-check whether another node survives.
{
    var store = new FakeStore
    {
        Data = BuildImage(("duplicate", 5), ("duplicate", 100))
    };
    var sink = new FakeSink();
    var list = new NativeGmBlockUserList(store, sink);
    list.Load(0);
    sink.SetBlocked("duplicate", true); // model an online player's login flag

    list.Sweep(20_000);
    Equal(1, list.Count, "one duplicate survives expiry sweep");
    Equal(true, list.Contains("duplicate"), "surviving duplicate remains in list");
    Equal(false, sink.State["duplicate"],
        "expired duplicate clears online flag despite surviving duplicate");

    var savesBeforeExtend = store.SaveCount;
    list.Add("duplicate", 10, 20_000);
    Equal(false, sink.State["duplicate"],
        "extending a surviving node does not restore the online flag");
    Equal(savesBeforeExtend, store.SaveCount,
        "extending a surviving duplicate remains non-persistent");
}

// unconditional Sweep body: fractional seconds truncate (integer division by 1000)
{
    var list = new NativeGmBlockUserList(new FakeStore());
    list.Add("frank", 100, 0);
    var changed = list.Sweep(1999);     // 1999ms/1000 = 1s
    Equal(false, changed, "no expiry -> not changed");
    Equal(99, First(list).RemainSeconds, "truncated elapsed seconds");
}

// 32-bit GetTickCount wrap: elapsed time is an unsigned low-word difference.
{
    var list = new NativeGmBlockUserList(new FakeStore());
    list.Add("wrap", 10, unchecked((int)0xFFFF_FC00)); // 1024ms before wrap
    list.Sweep(0);
    Equal(9, First(list).RemainSeconds,
        "sweep uses unsigned 32-bit elapsed time across wrap");

    var gated = new NativeGmBlockUserList(new FakeStore());
    var start = unchecked((int)0xFFFF_D8EF); // 10001ms before wrap
    gated.Add("wrap-gate", 100, start);
    gated.Tick(start);
    Equal(100, First(gated).RemainSeconds,
        "initial wrapped tick does not consume time");
    gated.Tick(0);
    Equal(90, First(gated).RemainSeconds,
        "10-second gate opens across wrapped interval");
}

// --- 8. Save image matches the codec exactly (round trip through a fresh list) ---
{
    var store = new FakeStore();
    var list = new NativeGmBlockUserList(store);
    list.Add("gg", 11, 0);
    list.Add("hh", 22, 0);
    Equal(40, store.LastSaved.Length, "two records = 40 bytes");

    var reloaded = new NativeGmBlockUserList(new FakeStore { Data = store.LastSaved });
    reloaded.Load(0);
    Equal(2, reloaded.Count, "reload count");
    Equal(true, reloaded.Contains("gg"), "reload gg");
    Equal(true, reloaded.Contains("hh"), "reload hh");
}

// --- 9. Command facade maps onto the list semantics ---
{
    var store = new FakeStore();
    var list = new NativeGmBlockUserList(store);
    var cmd = new NativeGmDenyListCommands(list);
    Equal(10, NativeGmDenyListCommands.DefaultDurationSeconds, "native default duration");

    cmd.DisableSendMsg("ivan", 60, 0);
    Equal(true, cmd.Hit("ivan"), "DisableSendMsg -> Hit true");
    Equal(1, cmd.DisableSendMsgList().Count, "DisableSendMsgList enumerates");
    Equal(true, cmd.EnableSendMsg("ivan"), "EnableSendMsg deletes");
    Equal(false, cmd.Hit("ivan"), "Hit false after enable");
}

// --- 10. Deny-logon lists: add/del/show/de-dup + save-on-change ---
{
    var store = new FakeStringStore();
    var list = new NativeGmDenyLogonList(store);
    Equal(true, list.Add("1.2.3.4"), "deny add new");
    Equal(false, list.Add("1.2.3.4"), "deny add duplicate ignored");
    Equal(false, list.Add(" 1.2.3.4 "), "deny add trims to duplicate");
    Equal(1, list.Count, "deny single entry after dup adds");
    Equal(1, store.SaveCount, "deny dup does not re-save");
    Equal(true, list.Contains("1.2.3.4"), "deny hit");
    Equal(1, list.Snapshot().Count, "deny show one");
    Equal(false, list.Delete("9.9.9.9"), "deny delete absent false");
    Equal(1, store.SaveCount, "deny absent delete no save");
    Equal(true, list.Delete("1.2.3.4"), "deny delete present");
    Equal(2, store.SaveCount, "deny present delete saves");
    Equal(0, list.Count, "deny empty after delete");
}

// --- 11. Deny-logon gate precedence: IP -> Account -> CharName, first match wins ---
{
    var ip = new NativeGmDenyLogonList(new FakeStringStore());
    var acc = new NativeGmDenyLogonList(new FakeStringStore());
    var chr = new NativeGmDenyLogonList(new FakeStringStore());
    var gate = new NativeGmDenyLogonGate(ip, acc, chr);

    Equal(NativeDenyLogonKind.None, gate.Check("ip", "acc", "chr"), "no lists -> None");

    chr.Add("badchar");
    Equal(NativeDenyLogonKind.CharName, gate.Check("ip", "acc", "badchar"), "charname match");

    acc.Add("badacc");
    Equal(NativeDenyLogonKind.Account, gate.Check("ip", "badacc", "badchar"),
        "account outranks charname");

    ip.Add("badip");
    Equal(NativeDenyLogonKind.IPaddr, gate.Check("badip", "badacc", "badchar"),
        "ip outranks all");
}

Console.WriteLine("NativeGmDenyListCommandsCheck PASS");
return 0;

// --- helpers ---

static void RecordRoundTrip(string name, int value, string expectName = null)
{
    var buf = new byte[20];
    NativeBlockUserRecordCodec.EncodeRecord(
        buf, 0, System.Text.Encoding.Latin1.GetBytes(name), value);
    var back = System.Text.Encoding.Latin1.GetString(
        NativeBlockUserRecordCodec.DecodeNameBytes(buf, 0));
    var backVal = NativeBlockUserRecordCodec.DecodeValue(buf, 0);
    Equal(expectName ?? name, back, $"record name round trip ({name})");
    Equal(value, backVal, $"record value round trip ({name})");
}

static byte[] BuildImage(params (string name, int value)[] rows)
{
    var data = new byte[rows.Length * 20];
    for (var i = 0; i < rows.Length; i++)
        NativeBlockUserRecordCodec.EncodeRecord(
            data, i * 20, System.Text.Encoding.Latin1.GetBytes(rows[i].name), rows[i].value);
    return data;
}

static NativeBlockUserEntry First(NativeGmBlockUserList list)
{
    foreach (var e in list.Snapshot())
        return e;
    throw new InvalidOperationException("empty list");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

sealed class FakeStore : INativeBlockUserStore
{
    public byte[] Data;
    public byte[] LastSaved;
    public int SaveCount;
    public int DeleteCount;

    public byte[] Load() => Data;

    public void Save(byte[] data)
    {
        LastSaved = data;
        Data = data;
        SaveCount++;
    }

    public void Delete()
    {
        Data = null;
        DeleteCount++;
    }
}

sealed class FakeSink : INativeBlockUserSink
{
    public readonly Dictionary<string, bool> State =
        new(StringComparer.OrdinalIgnoreCase);

    public void SetBlocked(string name, bool blocked) => State[name] = blocked;
}

sealed class FakeStringStore : INativeStringListStore
{
    public IReadOnlyList<string> Data;
    public int SaveCount;

    public IReadOnlyList<string> Load() => Data;

    public void Save(IReadOnlyList<string> lines)
    {
        Data = lines;
        SaveCount++;
    }
}
