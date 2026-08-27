// CM 4125 / ShenYou attribute table - Zhanshen equivalence audit.
//
// Native chain:
//   leaf   0x6DAE25  `8B 45 FC` / `E8 07 BE 06 00 call 0x746C34` / jmp shared exit
//   worker 0x746C34
//     0x746C45  eax = [[0x7D6014]]+0x18                 ; row count
//     0x746C4A  0F 8E 14 01 00 00 jle 0x746D64          ; empty table -> no frames
//     0x746C56  6B C0 2B imul eax,eax,0x2B              ; body length
//     0x746C5F  call 0x402FA0 GetMem                    ; NOT zeroed
//     0x746C77/83/8F  Lock (0x49EE7C) / First (0x49EE4C) / Next (0x49EE54)
//     0x746CB4  call 0x403260 Move, ecx=0x2B            ; one row per iteration
//     0x746CE3  call 0x49F0DC Unlock
//     0x746D0F  66 BA C0 0F mov dx,0xFC0
//     0x746D18  FF 93 54 02 00 00 call [vmt+0x254]      ; Recog=count, Param=0,
//                                                       ; Tag=word[[0x7D5AEC]], Series=0
//     0x746D1E  A1 38 69 7D 00 / 80 38 00                ; if byte[[0x7D6938]] <> 0
//     0x746D28  6A 01 ... 66 BA C6 0F / call [vmt+0x250] ; Param=1
//     0x746D43  6A 00 ... 66 BA C6 0F / call [vmt+0x250] ; Param=0
//
// Stack arguments are pushed left to right (see the sender note in
// TPlayObject.NativeCmTailProtocol.cs), so the first push is Param and the last two
// pushes on the 0x254 slot are Buf and Len.
//
// Row source: sub_755350 @0x755350 parses Config\ShenYou.txt into GetMem(0x2B) blocks
// and appends them with 0x49EC5C, which links onto a tail chain - so First/Next walk
// them in file order and that is the order this body must use.
//
// The last unknown, byte[[0x7D6938]], is mir2Actor.ini [setup]/ShenYouAbilSwitch:
//   0x7555F1  push 0 / ecx='ShenYouAbilSwitch' / edx='setup' / call [ini+0x10] ReadBool
//   0x755604  mov edx,[0x7D6938] / mov [edx],al
// reached from all three exits of the loader (0x75557C success, 0x7555BD exception,
// and the missing-file fallthrough at 0x7555BF), and rewritten by the GM arm
// 0x628AA3 -> 0x6BF658 which also persists it with WriteBool.
using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using SystemModule;

try
{
    PrepareRuntime();

    CheckConstants();
    CheckShippedFormatParsesAsNative();
    CheckCommentAndBlankLinesSkipped();
    CheckSlotCapDirectiveAlwaysResolvesToFour();
    CheckSlotCapStartsAtZeroBeforeAnyLoad();
    CheckZeroIdRowIsDropped();
    CheckDuplicateIdKeepsBothRowsAndLastWinsOnLookup();
    CheckMalformedFieldsDoNotAbortTheLoad();
    CheckNameTruncatedAtThirtyBytes();
    CheckMissingFileLeavesPreviousRowsStanding();
    CheckRecordBufferLayout();
    CheckAbilSwitchUsesDelphiReadBoolSemantics();
    CheckAbilSwitchRefreshedOnEveryLoadOutcome();
    CheckCm4125EmptyTableSendsNothing();
    CheckCm4125SendsBothFramesExactly();
    CheckCm4125SwitchDrivesSecondFrameParam();
    CheckRegistryEntryIsSuperseded();

    Console.WriteLine(
        "PASS NativeShenYouAttribute row=0x2B(int,int,int,ShortString[0x1E]) " +
        "order=file(0x49EC5C tail-append) cap=4(word[[0x7D5AEC]]) " +
        "switch=mir2Actor.ini[setup]/ShenYouAbilSwitch(byte[[0x7D6938]]) " +
        "CM4125: count<=0 -> silence; else SM4032(Recog=count,Tag=4,body=count*0x2B) " +
        "then SM4038(Param=switch)");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeShenYouAttributeCheck FAIL: {exception}");
    return 1;
}

// ---------------------------------------------------------------------------
// Constants straight off the image.
// ---------------------------------------------------------------------------

static void CheckConstants()
{
    Equal(0x2B, NativeShenYouAttributeConfig.NativeRecordSize,
        "record size (0x755488 mov eax,0x2B)");
    Equal(0x1E, NativeShenYouAttributeConfig.NativeNameCapacity,
        "name capacity (0x75552A mov cl,0x1E)");
    Equal(4, NativeShenYouAttributeConfig.NativeDefaultSlotCap,
        "slot cap default (0x7553F9 mov dword[eax],4)");
    Equal(4032, Grobal2.SM_4032, "SM 4032 (0x746D0F mov dx,0xFC0)");
    Equal(4038, Grobal2.SM_4038, "SM 4038 (0x746D32 mov dx,0xFC6)");
    Equal(4125, Grobal2.CM_4125, "CM 4125 leaf 0x6DAE25");
    Equal(12 + 1 + NativeShenYouAttributeConfig.NativeNameCapacity,
        NativeShenYouAttributeConfig.NativeRecordSize,
        "three int32 plus a 0x1E ShortString exactly fill the record");
}

// ---------------------------------------------------------------------------
// Parser. The shipped file has no '=' on a data line, so a parser that split on
// '=' first would reject every row; these fix the native shape in place.
// ---------------------------------------------------------------------------

static void CheckShippedFormatParsesAsNative()
{
    // Transcribed from the shipped Config file: two comment styles, the '=' directive,
    // then Id|Base|Param|Name rows.
    var config = LoadRows(
        "//format",
        ";id|point|absorb 10",
        "=4",
        "1|5|0|antiboom 1",
        "2|50|1|mp cap 500",
        "16|50|1|mp regen 3");

    Equal(3, config.Count, "shipped-shape row count");
    Equal(4, config.SlotCap, "shipped-shape slot cap");

    Equal(1, config.Rows[0].Id, "row0 id");
    Equal(5, config.Rows[0].BaseValue, "row0 base");
    Equal(0, config.Rows[0].Param3, "row0 param");
    Equal("antiboom 1", config.Rows[0].Name, "row0 name");

    Equal(2, config.Rows[1].Id, "row1 id");
    Equal(50, config.Rows[1].BaseValue, "row1 base");
    Equal(1, config.Rows[1].Param3, "row1 param");
    Equal("mp cap 500", config.Rows[1].Name, "row1 name");

    Equal(16, config.Rows[2].Id, "row2 id");
    Equal("mp regen 3", config.Rows[2].Name, "row2 name");

    // 0x49EC5C appends, so the ordered walk is file order and not id order.
    Assert(config.Rows[0].Id == 1 && config.Rows[1].Id == 2 && config.Rows[2].Id == 16,
        "rows keep file order");
}

static void CheckCommentAndBlankLinesSkipped()
{
    var config = LoadRows(
        "",
        ";comment",
        "//comment",
        "/single-slash is also a comment (0x755441)",
        "7|1|2|name");

    Equal(1, config.Count, "only the data line survives");
    Equal(7, config.Rows[0].Id, "surviving row id");
}

static void CheckSlotCapDirectiveAlwaysResolvesToFour()
{
    // 0x755452 converts the text BEFORE the first '=', which on a '='-leading line is
    // always empty, so 0x755476's StrToIntDef always returns its default of 4. A port
    // that read "=8" as eight would put an 8 in the SM 4032 Tag that native never sends.
    foreach (var directive in new[] { "=4", "=8", "=0", "=abc", "=" })
    {
        var config = LoadRows(directive, "1|1|1|n");
        Equal(4, config.SlotCap, "slot cap after '" + directive + "'");
    }

    // A line that merely contains '=' is a data line, not a directive. Its first field
    // is then "9=1", which Delphi's StrToIntDef rejects whole rather than reading as 9,
    // so the id is 0 and 0x755531 drops the row.
    var data = LoadRows("9=1|2|3|n");
    Equal(4, data.SlotCap, "'9=1|...' does not touch the cap");
    Equal(0, data.Count, "'9=1|...' converts to id 0 and is dropped");

    var plain = LoadRows("9|2|3|n");
    Equal(1, plain.Count, "the same line without '=' is a normal row");
    Equal(9, plain.Rows[0].Id, "plain row id");
}

static void CheckZeroIdRowIsDropped()
{
    // 0x755531 `test eax,eax` / `je 0x755547` - the block is allocated and then leaked.
    var config = LoadRows("0|5|1|zero", "abc|5|1|unparsable-id", "3|5|1|kept");
    Equal(1, config.Count, "only the non-zero id survives");
    Equal(3, config.Rows[0].Id, "surviving id");
}

static void CheckDuplicateIdKeepsBothRowsAndLastWinsOnLookup()
{
    // Add never dedupes; it appends to the tail chain AND pushes onto the front of the
    // bucket chain, so the walk sees both rows while 0x49F0EC finds the later one.
    var config = LoadRows("5|10|0|first", "5|20|0|second");
    Equal(2, config.Count, "both duplicate rows are stored");
    Equal("first", config.Rows[0].Name, "walk order keeps the first");
    Equal("second", config.Rows[1].Name, "walk order keeps the second");

    Assert(config.TryGet(5, out var found), "lookup finds the duplicated id");
    Equal(20, found.BaseValue, "lookup returns the LAST added row");
}

static void CheckMalformedFieldsDoNotAbortTheLoad()
{
    // Every field goes through StrToIntDef with a default of 0 (0x7554B5/0x7554DD/
    // 0x755506), so a bad field is a zero, never a load failure.
    var config = LoadRows("11|xx|yy|name", "12|3|4|ok");
    Equal(2, config.Count, "a malformed field does not stop the load");
    Equal(0, config.Rows[0].BaseValue, "unparsable base becomes 0");
    Equal(0, config.Rows[0].Param3, "unparsable param becomes 0");
    Equal(3, config.Rows[1].BaseValue, "the following row still parses");
}

static void CheckNameTruncatedAtThirtyBytes()
{
    var longName = new string('A', 40);
    var config = LoadRows("1|0|0|" + longName);
    Equal(30, config.Rows[0].NameBytes.Length, "ASCII name capped at 0x1E bytes");

    // The cap counts bytes, so GBK double-byte characters are cut mid-character; the
    // stored bytes, not the string, are what has to match.
    var gbkName = new string('\u4e2d', 20);
    var gbk = LoadRows("1|0|0|" + gbkName);
    Equal(30, gbk.Rows[0].NameBytes.Length, "GBK name capped at 0x1E bytes");
}

static void CheckSlotCapStartsAtZeroBeforeAnyLoad()
{
    // word[[0x7D5AEC]] is BSS, and the only write that raises it to 4 (0x7553F9) is
    // inside the file-exists branch taken at 0x7553AC. The runtime capture proves the
    // state is reachable: 0x7DCF44 reads 0 while the table object at 0x7DCF40 already
    // holds a live heap pointer, i.e. the table was constructed but never loaded.
    var fresh = new NativeShenYouAttributeConfig();
    Equal(0, fresh.SlotCap, "cap before any load (BSS, 0x7553F9 not yet run)");
    Equal(0, fresh.Count, "no rows before any load");

    // The same is true after a failed first load: the missing-file leg jumps straight
    // from 0x7553AC to 0x7555BF and never reaches 0x7553F9.
    var missingFirst = Path.Combine(TempDir(), "never-written.txt");
    fresh.Reload(missingFirst, TempDir(), out _);
    Equal(0, fresh.SlotCap, "cap still 0 after a first load that found no file");

    // Only a load that actually reads the file raises it.
    Equal(4, LoadRows("1|1|1|n").SlotCap, "cap is 4 once 0x7553F9 has run");
}

static void CheckMissingFileLeavesPreviousRowsStanding()
{
    // 0x7553A2 tests FileExists before the list is created and the clear at 0x7553EF
    // sits after LoadFromFile, so a later missing file cannot wipe a good load.
    var config = LoadRows("1|5|0|kept");
    Equal(1, config.Count, "seed load");

    var missing = Path.Combine(TempDir(), "definitely-absent.txt");
    var loaded = config.Reload(missing, TempDir(), out var error);
    Assert(!loaded, "missing file reports failure");
    Assert(error.StartsWith(NativeShenYouAttributeConfig.MissingFileMessage,
            StringComparison.Ordinal),
        "missing-file message matches the 0x7556A8 template");
    Equal(1, config.Count, "missing file leaves the previous rows");
    Equal(4, config.SlotCap, "missing file leaves the previous cap");
}

// ---------------------------------------------------------------------------
// The 0x2B-byte wire record.
// ---------------------------------------------------------------------------

static void CheckRecordBufferLayout()
{
    var config = LoadRows("258|66051|-1|AB", "7|8|9|");
    var body = config.BuildNativeRecordBuffer();

    Equal(2 * 0x2B, body.Length, "body is count*0x2B");

    Equal(258, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0)), "row0 +0x00");
    Equal(66051, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4)), "row0 +0x04");
    Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(8)), "row0 +0x08");
    Equal((byte)2, body[0x0C], "row0 ShortString length byte");
    Equal((byte)'A', body[0x0D], "row0 name[0]");
    Equal((byte)'B', body[0x0E], "row0 name[1]");

    // Native leaves the rest of the field as uninitialised GetMem residue; there is no
    // value to copy, so this port writes zeros and this pins that choice.
    for (var i = 0x0F; i < 0x2B; i++)
        Equal((byte)0, body[i], "row0 name pad byte " + i);

    Equal(7, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0x2B)), "row1 +0x00");
    Equal(8, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0x2B + 4)), "row1 +0x04");
    Equal(9, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0x2B + 8)), "row1 +0x08");
    Equal((byte)0, body[0x2B + 0x0C], "row1 empty name length byte");

    // Byte order on the wire must follow the file, not the id.
    var ordered = LoadRows("30|0|0|c", "2|0|0|a", "11|0|0|b");
    var orderedBody = ordered.BuildNativeRecordBuffer();
    Equal(30, BinaryPrimitives.ReadInt32LittleEndian(orderedBody.AsSpan(0)),
        "first record on the wire is the first file row");
    Equal(2, BinaryPrimitives.ReadInt32LittleEndian(orderedBody.AsSpan(0x2B)),
        "second record on the wire is the second file row");
    Equal(11, BinaryPrimitives.ReadInt32LittleEndian(orderedBody.AsSpan(2 * 0x2B)),
        "third record on the wire is the third file row");
}

// ---------------------------------------------------------------------------
// mir2Actor.ini [setup]/ShenYouAbilSwitch.
// ---------------------------------------------------------------------------

static void CheckAbilSwitchUsesDelphiReadBoolSemantics()
{
    // Delphi TIniFile.ReadBool is ReadInteger <> 0 with a default of Ord(False).
    Equal(false, ReadSwitch(null), "no ini file");
    Equal(false, ReadSwitch(Ini("setup")), "key absent");
    Equal(false, ReadSwitch(Ini("setup", "ShenYouAbilSwitch=0")), "0");
    Equal(true, ReadSwitch(Ini("setup", "ShenYouAbilSwitch=1")), "1");
    Equal(true, ReadSwitch(Ini("setup", "ShenYouAbilSwitch=2")), "2 is non-zero");
    Equal(true, ReadSwitch(Ini("setup", "ShenYouAbilSwitch=-1")), "-1 is non-zero");
    // "TRUE" is not an integer, so Delphi falls back to the default rather than
    // treating it as a boolean word.
    Equal(false, ReadSwitch(Ini("setup", "ShenYouAbilSwitch=TRUE")), "TRUE is not 1");
    Equal(false, ReadSwitch(Ini("other", "ShenYouAbilSwitch=1")), "wrong section");
}

static void CheckAbilSwitchRefreshedOnEveryLoadOutcome()
{
    var dir = FreshDir("switch-outcomes");
    var configFile = Path.Combine(dir, "shenyou.txt");
    File.WriteAllText(configFile, "1|5|0|n" + Environment.NewLine, HUtil32.GbkEncoding);
    WriteIni(dir, Ini("setup", "ShenYouAbilSwitch=1"));

    var config = NativeShenYouAttributeConfig.Shared;
    Assert(config.Reload(configFile, dir, out _), "successful load");
    Equal(true, config.AbilSwitch, "switch read on the success exit (0x75557C)");

    // The missing-file exit at 0x7555BF falls through to the same read.
    WriteIni(dir, Ini("setup", "ShenYouAbilSwitch=0"));
    Assert(!config.Reload(Path.Combine(dir, "gone.txt"), dir, out _), "missing load");
    Equal(false, config.AbilSwitch, "switch still read on the missing-file exit");
}

// ---------------------------------------------------------------------------
// CM 4125 terminal action.
// ---------------------------------------------------------------------------

static void CheckCm4125EmptyTableSendsNothing()
{
    // 0x746C4A `jle 0x746D64` returns before the allocation, so neither frame goes out.
    LoadRows();
    var player = Dispatch();
    Equal(0, player.RawSocketMessages.Count, "empty table sends no raw frame");
    Equal(0, player.StringSocketMessages.Count, "empty table sends no string frame");

    // A file made only of comments is the same case.
    LoadRows(";only", "//comments");
    var commentsOnly = Dispatch();
    Equal(0, commentsOnly.RawSocketMessages.Count, "comment-only table sends nothing");
}

static void CheckCm4125SendsBothFramesExactly()
{
    var config = LoadRowsWithSwitch(false, "1|5|0|AB", "2|50|1|C");

    var player = Dispatch();
    Equal(2, player.RawSocketMessages.Count, "two frames leave the 0x250/0x254 slots");

    var (first, firstBody) = player.RawSocketMessages[0];
    Equal((ushort)Grobal2.SM_4032, first.Ident, "frame 1 ident (0x746D0F)");
    Equal(2, first.Recog, "frame 1 Recog = row count (0x746D0C)");
    Equal((ushort)0, first.Param, "frame 1 Param = 0 (first push at 0x746CF0)");
    Equal((ushort)4, first.Tag, "frame 1 Tag = word[[0x7D5AEC]]");
    Equal((ushort)0, first.Series, "frame 1 Series = 0");
    Equal(2 * 0x2B, firstBody.Length, "frame 1 body length (0x746C56 imul 0x2B)");
    AssertSequenceEqual(config.BuildNativeRecordBuffer(), firstBody, "frame 1 body bytes");

    var (second, secondBody) = player.RawSocketMessages[1];
    Equal((ushort)Grobal2.SM_4038, second.Ident, "frame 2 ident (0x746D32/0x746D4D)");
    Equal(0, second.Recog, "frame 2 Recog = 0 (xor ecx,ecx)");
    Equal((ushort)0, second.Param, "frame 2 Param = 0 when the switch is off (0x746D43)");
    Equal((ushort)0, second.Tag, "frame 2 Tag = 0");
    Equal((ushort)0, second.Series, "frame 2 Series = 0");
    Equal(0, secondBody.Length, "frame 2 body is empty");
}

static void CheckCm4125SwitchDrivesSecondFrameParam()
{
    LoadRowsWithSwitch(true, "1|5|0|n");
    var on = Dispatch();
    Equal(2, on.RawSocketMessages.Count, "switch on still sends both frames");
    Equal((ushort)1, on.RawSocketMessages[1].Packet.Param,
        "switch on -> Param 1 (0x746D28 push 1)");

    LoadRowsWithSwitch(false, "1|5|0|n");
    var off = Dispatch();
    Equal((ushort)0, off.RawSocketMessages[1].Packet.Param,
        "switch off -> Param 0 (0x746D43 push 0)");

    // Only the second frame moves; the first is unaffected by the switch.
    Equal(on.RawSocketMessages[0].Packet.Recog, off.RawSocketMessages[0].Packet.Recog,
        "frame 1 Recog independent of the switch");
    Equal(on.RawSocketMessages[0].Packet.Tag, off.RawSocketMessages[0].Packet.Tag,
        "frame 1 Tag independent of the switch");
}

// ---------------------------------------------------------------------------
// Wiring.
// ---------------------------------------------------------------------------

static void CheckRegistryEntryIsSuperseded()
{
    // The arm must reach the implementation, not the fail-closed drop.
    var source = ReadRepoFile(
        @"unified\LyoMir2\GameSvr\Players\TPlayObject.NativeCmTailProtocol.cs");
    Contains(source, "case Grobal2.CM_4125:", "CM 4125 dispatch arm");
    Contains(source, "ClientNativeShenYouAttributeQuery();", "arm calls the handler");
    Assert(!source.Contains("NativeCmTailFailClosed.Drop(Grobal2.CM_4125",
            StringComparison.Ordinal),
        "CM 4125 no longer drops");

    var registry = ReadRepoFile(
        @"unified\LyoMir2\GameSvr\Services\NativeCmTailFailClosed.cs");
    Contains(registry, "Add(4125,", "registry keeps the 4125 row for Drop validation");
}

// ---------------------------------------------------------------------------
// Harness.
// ---------------------------------------------------------------------------

static NativeShenYouAttributeConfig LoadRows(params string[] lines)
    => LoadRowsWithSwitch(false, lines);

// The switch is seeded through the real mir2Actor.ini read rather than by poking the
// property, so every dispatch case also exercises the 0x7555F1 -> 0x755604 path.
static NativeShenYouAttributeConfig LoadRowsWithSwitch(bool switchOn, params string[] lines)
{
    var dir = FreshDir("rows");
    var file = Path.Combine(dir, "shenyou.txt");
    File.WriteAllText(file, string.Join(Environment.NewLine, lines) + Environment.NewLine,
        HUtil32.GbkEncoding);
    WriteIni(dir, Ini("setup", "ShenYouAbilSwitch=" + (switchOn ? "1" : "0")));

    var config = NativeShenYouAttributeConfig.Shared;
    if (!config.Reload(file, dir, out var error))
        throw new InvalidOperationException("seed load failed: " + error);
    Equal(switchOn, config.AbilSwitch, "switch seeded");
    return config;
}

static bool ReadSwitch(string iniText)
{
    var dir = FreshDir("switch");
    if (iniText != null)
        WriteIni(dir, iniText);
    return NativeShenYouAttributeConfig.ReadAbilSwitch(dir);
}

static string Ini(string section, params string[] entries)
    => "[" + section + "]" + Environment.NewLine
       + string.Concat(entries.Select(e => e + Environment.NewLine));

static ProbePlayer Dispatch()
{
    var player = new ProbePlayer
    {
        m_boOffLineFlag = true,
        m_sCharName = "probe",
        m_btRaceServer = Grobal2.RC_PLAYOBJECT
    };
    var message = new TProcessMessage { wIdent = Grobal2.CM_4125, sMsg = string.Empty };
    var handler = typeof(TPlayObject).GetMethod("TryHandleNativeCmTailProtocol",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryHandleNativeCmTailProtocol missing");
    var handled = (bool)handler.Invoke(player, new object[] { message });
    Assert(handled, "CM 4125 is claimed by the tail dispatcher");
    return player;
}

static void PrepareRuntime()
{
    // The M2Share static constructor loads a handful of ini files eagerly and throws
    // when one is missing or empty, so they have to exist before it is first touched.
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

    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.LogonCostLogList = new System.Collections.ArrayList();
}

static void WriteIni(string directory, string text)
    => File.WriteAllText(
        Path.Combine(directory, NativeShenYouAttributeConfig.AbilSwitchFileName),
        text, HUtil32.GbkEncoding);

static string TempDir()
{
    var dir = Path.Combine(Path.GetTempPath(), "NativeShenYouAttributeCheck");
    Directory.CreateDirectory(dir);
    return dir;
}

static string FreshDir(string name)
{
    var dir = Path.Combine(TempDir(), name + "-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
}

static string ReadRepoFile(string relativePath)
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null
           && !File.Exists(Path.Combine(directory.FullName, relativePath)))
    {
        directory = directory.Parent;
    }
    if (directory == null)
        throw new InvalidOperationException("cannot locate " + relativePath);
    return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
}

static void Contains(string haystack, string needle, string label)
{
    if (!haystack.Contains(needle, StringComparison.Ordinal))
        throw new InvalidOperationException(label + ": missing \"" + needle + "\"");
}

static void AssertSequenceEqual(byte[] expected, byte[] actual, string label)
{
    if (expected.Length != actual.Length)
        throw new InvalidOperationException(
            $"{label}: length {actual.Length}, expected {expected.Length}");
    for (var i = 0; i < expected.Length; i++)
    {
        if (expected[i] != actual[i])
            throw new InvalidOperationException(
                $"{label}: byte {i} = 0x{actual[i]:X2}, expected 0x{expected[i]:X2}");
    }
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: {actual}, expected {expected}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

sealed class ProbePlayer : TPlayObject
{
    internal List<(ClientPacket Packet, byte[] Body)> RawSocketMessages { get; } = new();
    internal List<(ClientPacket Packet, string Body)> StringSocketMessages { get; } = new();

    internal override void SendSocket(ClientPacket defMsg, byte[] body)
        => RawSocketMessages.Add((defMsg, body));

    internal override void SendSocket(ClientPacket defMsg, string message)
        => StringSocketMessages.Add((defMsg, message));
}
