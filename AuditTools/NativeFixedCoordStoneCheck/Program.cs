// 定位石 (TFixedCoordStone) recall — 战神 equivalence audit.
//
// Native halves:
//   SETTER   CM 3420 (0xD5C) -> sub_6E9BAC  (dispatch 0x6D873F sub eax,0xD5C /
//            0x6D8745 je 0x6DADE3; body calls the setter at 0x6DAE1B)
//   CONSUMER TFixedCoordStone VMT slot +0x18 (ptr 0x7827D4, VMT 0x7827BC)
//            = sub_78A014
//   REPLAY   logon 0x6B23E3 cmp byte [esi+0x18f8],0 -> re-enqueue tag 0x3026, wire ident 3420
//
// Runtime fields (TPlayer, instSize 0x1948):
//   obj+0x18F8 ShortString[15] map, obj+0x1908 word X, obj+0x190A word Y
// Record: rec[0x5AC] / rec[0x5BC] / rec[0x5BE].
//
// ⚠️ AUDIT-BLIND CLUSTER: all 30 golden records
// (staging/golden_saves_gtwl/user_data_idx*.bin) are ZERO across 0x5AC..0x5C0, so a
// byte-exact round-trip over goldens proves nothing here. Every assertion below is
// therefore constructed, and each one was mutation-tested (break the product code,
// confirm this audit goes red, restore).
using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

// rec payload is 0xEEF8; anything at/after 0xEF00 is the session tail.
const int NativeRecordLength = 0xEEF8;

try
{
    PrepareRuntimeFiles();
    Equal(3420, Grobal2.CM_SETFIXEDCOORD, "CM_SETFIXEDCOORD constant");
    // 0x3026 is the internal queue tag that sub_765E68 stores at record+0, not an
    // ident that ever reaches a socket. The RM handler 0x6B6036 reads that record
    // back and sends 0x6B6051 `mov dx,0xD5C` = 3420, the only ident load for this
    // reply in the image.
    Equal(3420, Grobal2.SM_FIXEDCOORD, "SM_FIXEDCOORD constant (wire 0xD5C)");

    // Offsets are the load-bearing facts; 0x6E9CB1 `mov cl,0x0F` fixes the capacity.
    Equal(0x05AC, Const<int>("NativeFixedCoordMapOffset"), "map offset");
    Equal(0x05BC, Const<int>("NativeFixedCoordXOffset"), "X offset");
    Equal(0x05BE, Const<int>("NativeFixedCoordYOffset"), "Y offset");
    Equal(0x0F, Const<int>("NativeFixedCoordNameCapacity"), "name capacity");

    CheckEmptinessIsLengthByte();
    CheckShortStringAssignDoesNotZeroFill();
    CheckRoundTripThroughRecord();
    CheckRestoreClearsWhenRecordTooShort();
    CheckBannedMapGate();
    CheckReplayOnlyWhenSet();
    CheckWiring();
    CheckBlacklistSurvivesAMissingFile();
    CheckExhaustionLegSendsClientIdNotMakeIndex();
    CheckExhaustionLegLogsAndStaysSilent();
    CheckGm401IsRegistered();

    Console.WriteLine(
        "PASS NativeFixedCoordStone cm=3420 sm=3420 rec=0x5AC/0x5BC/0x5BE " +
        "cap=0x0F emptiness=lenbyte assign=no-zerofill gate=NORECALL+传送石禁用地图 " +
        "replay=0x6B23E3");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeFixedCoordStoneCheck FAIL: {exception}");
    return 1;
}

// 0x78A082 / 0x6B23E3 both `cmp byte ptr [...+0x18F8],0` — the ShortString LENGTH
// byte. A derived-name test would misread a slot whose length is 0 but whose first
// data byte is not (and vice versa).
static void CheckEmptinessIsLengthByte()
{
    var player = NewPlayer();
    var raw = new byte[NativeRecordLength];
    // len=0 but residual data present -> native reads EMPTY.
    raw[0x5AC] = 0;
    raw[0x5AD] = (byte)'X';
    SetRecord(player, raw);
    Call(player, "RestoreNativeFixedCoord");
    Equal(false, CallFor<bool>(player, "HasNativeFixedCoord"),
        "len byte 0 with residual data must read EMPTY (0x78A082)");

    // len>0 -> occupied, regardless of what follows.
    raw = new byte[NativeRecordLength];
    raw[0x5AC] = 1;
    raw[0x5AD] = (byte)'3';
    SetRecord(player, raw);
    Call(player, "RestoreNativeFixedCoord");
    Equal(true, CallFor<bool>(player, "HasNativeFixedCoord"),
        "len byte 1 must read OCCUPIED");
    Equal("3", CallFor<string>(player, "GetNativeFixedCoordMapName"),
        "single-char map name decode");
}

// 0x6E9CB3 -> sub_4039E4 truncates at cl and does NOT zero-fill the tail. Writing a
// shorter name over a longer one must leave the residue intact, or the save byte-diffs
// against 战神. (Same ShortString semantics as M2 0x4039E4 == DBSvr 0x4035D8.)
static void CheckShortStringAssignDoesNotZeroFill()
{
    var player = NewPlayer();
    SetRecord(player, new byte[NativeRecordLength]);
    StoreName(player, "0123456789ABCDE");   // exactly 15 = capacity
    StoreName(player, "AB");                // 2 chars; tail must survive

    CallFor<bool>(player, "PersistNativeFixedCoord");
    var raw = GetRecord(player);
    Equal(2, raw[0x5AC], "length byte after short overwrite");
    Equal((byte)'A', raw[0x5AD], "byte 1 after short overwrite");
    Equal((byte)'B', raw[0x5AE], "byte 2 after short overwrite");
    // Residue from the longer prior value, NOT zeros.
    Equal((byte)'2', raw[0x5AF], "tail residue must survive (sub_4039E4 no zero-fill)");
    Equal((byte)'E', raw[0x5BB], "final tail byte must survive");
    Equal("AB", CallFor<string>(player, "GetNativeFixedCoordMapName"),
        "decode must stop at the length byte, ignoring residue");

    // Over-length input truncates at 15 and never overruns into X at 0x5BC.
    StoreName(player, "0123456789ABCDEFGHIJ");
    CallFor<bool>(player, "PersistNativeFixedCoord");
    raw = GetRecord(player);
    Equal(0x0F, raw[0x5AC], "over-length name truncates to cl=0x0F");
    Equal(0, raw[0x5BC], "truncation must not overrun into X at 0x5BC");
}

static void CheckRoundTripThroughRecord()
{
    var player = NewPlayer();
    SetRecord(player, new byte[NativeRecordLength]);
    StoreName(player, "3");
    SetShort(player, "m_nNativeFixedCoordX", 845);
    SetShort(player, "m_nNativeFixedCoordY", 674);
    Equal(true, CallFor<bool>(player, "PersistNativeFixedCoord"), "persist result");

    var raw = GetRecord(player);
    // Little-endian words at the exact native offsets.
    Equal(845, raw[0x5BC] | (raw[0x5BD] << 8), "X word at 0x5BC");
    Equal(674, raw[0x5BE] | (raw[0x5BF] << 8), "Y word at 0x5BE");

    // A fresh player hydrating the same record must see identical values: this is the
    // login leg that TryDecode does NOT cover (pure clone-carry, no DTO member).
    var reloaded = NewPlayer();
    SetRecord(reloaded, raw);
    Call(reloaded, "RestoreNativeFixedCoord");
    Equal("3", CallFor<string>(reloaded, "GetNativeFixedCoordMapName"), "reloaded map");
    Equal((short)845, GetShort(reloaded, "m_nNativeFixedCoordX"), "reloaded X");
    Equal((short)674, GetShort(reloaded, "m_nNativeFixedCoordY"), "reloaded Y");
}

static void CheckRestoreClearsWhenRecordTooShort()
{
    var player = NewPlayer();
    SetRecord(player, new byte[0x100]);
    StoreName(player, "3");
    SetShort(player, "m_nNativeFixedCoordX", 5);
    Call(player, "RestoreNativeFixedCoord");
    Equal(false, CallFor<bool>(player, "HasNativeFixedCoord"),
        "short record must clear the name");
    Equal((short)0, GetShort(player, "m_nNativeFixedCoordX"),
        "short record must clear X");

    // Persist over a short record reports failure only when there is data to lose.
    Equal(true, CallFor<bool>(player, "PersistNativeFixedCoord"),
        "empty state over a short record is not a loss");
    StoreName(player, "3");
    Equal(false, CallFor<bool>(player, "PersistNativeFixedCoord"),
        "real state over a short record must report failure");
}

// Setter gate 0x6E9C00 `cmp byte [esi+0x67],0` (NORECALL) and 0x6E9C12 IndexOf on the
// 传送石禁用地图 list, which is built CaseSensitive=False (0x792397 xor edx,edx ->
// sub_428588), so CompareStrings takes the case-insensitive leg at 0x49F637.
static void CheckBannedMapGate()
{
    M2Share.g_FixedCoordDisableMapList = new List<string> { "D2071", "0" };
    Equal(true, M2Share.IsNativeFixedCoordBannedMap("D2071"), "exact match banned");
    Equal(true, M2Share.IsNativeFixedCoordBannedMap("d2071"),
        "match must be case-INSENSITIVE (0x49F637 je -> sub_40BD78)");
    Equal(false, M2Share.IsNativeFixedCoordBannedMap("D2072"), "unlisted map allowed");
    Equal(false, M2Share.IsNativeFixedCoordBannedMap(""), "empty name allowed");
    Equal(false, M2Share.IsNativeFixedCoordBannedMap(null), "null name allowed");

    // 0x7944FC: a missing file leaves the list EMPTY, so nothing is banned.
    M2Share.g_FixedCoordDisableMapList = new List<string>();
    Equal(false, M2Share.IsNativeFixedCoordBannedMap("D2071"),
        "empty list bans nothing (missing-file leg at 0x7944FC)");
    M2Share.g_FixedCoordDisableMapList = null;
    Equal(false, M2Share.IsNativeFixedCoordBannedMap("D2071"),
        "unloaded list bans nothing");
}

static void CheckReplayOnlyWhenSet()
{
    // m_boOffLineFlag short-circuits the gate socket, so SendDefMessage still stamps
    // m_DefMsg (the observable packet) without needing a live GateManager.
    var player = NewPlayer();
    player.m_boOffLineFlag = true;
    SetRecord(player, new byte[NativeRecordLength]);
    Call(player, "RestoreNativeFixedCoord");

    var sent = CaptureDefMessages(player,
        () => Call(player, "ReplayNativeFixedCoordOnLogon"));
    Equal(0, sent.Count,
        "empty slot must NOT replay (0x6B23EA je skips the push)");

    StoreName(player, "3");
    SetShort(player, "m_nNativeFixedCoordX", 845);
    SetShort(player, "m_nNativeFixedCoordY", 674);
    sent = CaptureDefMessages(player,
        () => Call(player, "ReplayNativeFixedCoordOnLogon"));
    Equal(1, sent.Count, "occupied slot must replay exactly once");
    // Argument order comes from the six pushes at 0x6E9CD4-0x6E9CFC into sub_765E68
    // (ret 0x18), repeated byte-identically by the logon replay at 0x6B23EC-0x6B2414:
    // [ebp+0x14]=X -> Param, [ebp+0x10]=Y -> Tag.
    Equal((ushort)Grobal2.SM_FIXEDCOORD, sent[0].Ident, "replay wire ident 3420");
    // Hop 2 (the RM 0x3026 handler) re-orders the record before it hits the wire:
    //   6B6036  66 8B 43 02   mov ax,[rec+2]   -> Param  = wParam = 0
    //   6B603C  8B 43 08      mov eax,[rec+8]  -> Tag    = nParam2 = X
    //   6B6040  66 8B 43 0C   mov ax,[rec+0xC] -> Series = nParam3 = Y
    //   6B6051  66 BA 5C 0D   mov dx,0xD5C     -> ident 3420
    // so X lands in Tag, not Param. The enqueue at 0x6B23F0/0x6B23F8 only decides
    // which record slot each coordinate occupies.
    Equal(0, sent[0].Param, "replay Param is wParam=0 (0x6B6036 mov ax,[rec+2])");
    Equal(845, sent[0].Tag, "replay X lands in Tag (0x6B603C mov eax,[rec+8])");
    Equal(674, sent[0].Series,
        "replay Y lands in Series (0x6B6040 mov ax,[rec+0xC])");
}

static List<ClientPacket> CaptureDefMessages(TPlayObject player, Action action)
{
    var field = typeof(TPlayObject).GetField("m_DefMsg",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingFieldException(typeof(TPlayObject).FullName, "m_DefMsg");
    var sentinel = Grobal2.MakeDefaultMsg(0xFFFF, 0x7FFFFFFF, 0, 0, 0);
    field.SetValue(player, sentinel);
    action();
    var current = (ClientPacket)field.GetValue(player);
    var result = new List<ClientPacket>();
    if (current != null && !ReferenceEquals(current, sentinel))
        result.Add(current);
    return result;
}

// The four entry points are useless unwired; native reaches each one unconditionally,
// so a missing call site is a silent behavioural gap that no unit assertion would catch.
static void CheckWiring()
{
    var root = FindRepositoryRoot();

    RequireContains(Path.Combine(root, "GameSvr", "GameApp.cs"),
        "M2Share.LoadFixedCoordDisableMap();",
        "startup must load 传送石禁用地图.txt");

    var usrEngn = Path.Combine(root, "GameSvr", "UsrSystem", "UsrEngn.cs");
    RequireContains(usrEngn, "PlayObject.RestoreNativeFixedCoord();",
        "login must restore the recall anchor (TryDecode never surfaces it)");
    RequireContains(usrEngn, "PlayObject.PersistNativeFixedCoord()",
        "save must write the recall anchor back");

    RequireContains(Path.Combine(root, "GameSvr", "Players", "TPlayObject.Base.cs"),
        "ReplayNativeFixedCoordOnLogon();",
        "logon must replay the fixed-coord reply (0x6B23E3)");

    RequireContains(Path.Combine(root, "GameSvr", "Players", "TPlayObject.Message.cs"),
        "ClientSetFixedCoord(ProcessMsg.nParam1);",
        "CM 3420 must reach the setter with the wire Recog");

    // Consumer must hang off the class dispatch, mirroring VMT+0x18.
    var operate = Path.Combine(root, "GameSvr", "Players", "TPlayObject.Operate.cs");
    RequireContains(operate, "case \"TFixedCoordStone\":",
        "item-use dispatch must handle TFixedCoordStone (VMT+0x18 = sub_78A014)");
    RequireContains(operate, "UseNativeFixedCoordStone(item)",
        "TFixedCoordStone case must call the consumer");
}

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------

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

static void SetRecord(TPlayObject player, byte[] raw)
    => Field("m_NativeHumanData").SetValue(player, raw);

static byte[] GetRecord(TPlayObject player)
    => (byte[])Field("m_NativeHumanData").GetValue(player);

static void SetShort(TPlayObject player, string name, short value)
    => Field(name).SetValue(player, value);

static short GetShort(TPlayObject player, string name)
    => (short)Field(name).GetValue(player);

static void StoreName(TPlayObject player, string mapName)
    => Method("StoreNativeFixedCoordName").Invoke(player, new object[] { mapName });

static MethodInfo Method(string name)
    => typeof(TPlayObject).GetMethod(name,
           BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
       ?? throw new MissingMethodException(typeof(TPlayObject).FullName, name);

static void Call(TPlayObject player, string name)
    => Method(name).Invoke(player, null);

static T CallFor<T>(TPlayObject player, string name)
    => (T)Method(name).Invoke(player, null);

static T Const<T>(string name)
{
    var field = typeof(TPlayObject).GetField(name,
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingFieldException(typeof(TPlayObject).FullName, name);
    return (T)Convert.ChangeType(field.GetRawConstantValue(), typeof(T));
}

// ---------------------------------------------------------------------------
// GM 401 (0x6281AE) + the exhaustion leg (0x6E9D27..0x6E9D82)
// ---------------------------------------------------------------------------

// Both native loaders test FileExists BEFORE touching the list, and it is
// TStrings.LoadFromFile that clears:
//   startup 0x7944F5 call FileExists / 0x7944FA test al,al / 0x7944FC je 0x794528
//   GM 401  0x6281CC call FileExists / 0x6281D1 test al,al / 0x6281D3 je 0x628204
// So a reload with the file missing leaves the previously-loaded blacklist
// INTACT. Clearing first would silently un-ban every map -- a permissive
// divergence on a gate, and only observable on the reload path.
static void CheckBlacklistSurvivesAMissingFile()
{
    var root = FindRepositoryRoot();
    // Build the path exactly as the loader does, so the fixture cannot drift
    // from production and silently test nothing.
    // Build the path exactly as the loader does, so the fixture cannot drift
    // from production and silently test nothing.
    // Pin the two path components so the fixture is deterministic, then build
    // the path exactly as the loader does -- if production's concatenation ever
    // changes, this fixture stops finding the file and the check goes red rather
    // than silently testing nothing.
    M2Share.sConfigPath = AppContext.BaseDirectory;
    M2Share.g_Config.sEnvirDir = "./Envir/";
    var file = M2Share.sConfigPath + M2Share.g_Config.sEnvirDir
                                   + "传送石禁用地图.txt";
    var envirDir = Path.GetDirectoryName(file);
    if (!string.IsNullOrEmpty(envirDir)) Directory.CreateDirectory(envirDir);

    File.WriteAllText(file, "矿区" + Environment.NewLine,
        HUtil32.GbkEncoding);
    Equal(true, M2Share.LoadFixedCoordDisableMap(), "present file loads");
    var loadedCount = M2Share.g_FixedCoordDisableMapList.Count;
    if (loadedCount == 0)
        throw new InvalidOperationException(
            "fixture failed: the list should be populated before the reload test");

    // now reload with the file gone
    File.Delete(file);
    Equal(false, M2Share.LoadFixedCoordDisableMap(), "missing file reports false");
    Equal(loadedCount, M2Share.g_FixedCoordDisableMapList.Count,
        "a missing file must leave the blacklist INTACT (0x6281D3 je jumps past "
        + "the LoadFromFile at 0x6281FC, and LoadFromFile is what clears)");

    // and the entry must still actually gate
    Equal(true, M2Share.IsNativeFixedCoordBannedMap("矿区"),
        "the surviving entry must still ban its map");

    // A successful reload must REPLACE, not append -- TStrings.LoadFromFile
    // clears internally (0x794525 / 0x6281FC), so an entry dropped from the file
    // must stop banning. Without this the "clear after the check" placement could
    // be deleted entirely and the missing-file test above would still pass.
    File.WriteAllText(file, "封魔谷" + Environment.NewLine,
        HUtil32.GbkEncoding);
    Equal(true, M2Share.LoadFixedCoordDisableMap(), "second load succeeds");
    Equal(1, M2Share.g_FixedCoordDisableMapList.Count,
        "a successful reload REPLACES the list (LoadFromFile clears) -- entries "
        + "must not accumulate across reloads");
    Equal(false, M2Share.IsNativeFixedCoordBannedMap("矿区"),
        "a map dropped from the file must stop being banned after a reload");
    Equal(true, M2Share.IsNativeFixedCoordBannedMap("封魔谷"),
        "the newly-listed map must be banned");

    File.Delete(file);
    M2Share.g_FixedCoordDisableMapList.Clear();
}

// 0x6E9D1A/0x6E9D68 both feed the send from the ITEM pointer's +0x18, which is
// the session-local client id, NOT the server MakeIndex (item+0x20, getter
// 0x78455C). Passing MakeIndex hands the client an id it never saw.
static void CheckExhaustionLegSendsClientIdNotMakeIndex()
{
    var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GameSvr",
        "Players", "TPlayObject.NativeFixedCoordStone.cs"));

    foreach (var ident in new[] { "SM_BAGITEMDURACHG", "SM_DELITEM" })
    {
        var at = source.IndexOf("Grobal2." + ident, StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidOperationException($"{ident} send is missing");
        var tail = source.Substring(at, Math.Min(120, source.Length - at));
        if (!tail.Contains("EnsureClientItemId(item)", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{ident} must carry EnsureClientItemId(item) -- native reads "
                + "[esi+0x18] (client id), not item+0x20 (MakeIndex)");
        if (tail.Contains("item.MakeIndex", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{ident} must NOT pass item.MakeIndex as the Recog");
    }
}

// The exhaustion leg 0x6E9D2E..0x6E9D82 contains NO vmt+0xD4 call and no colour
// word, so it is silent; but it DOES write a type-0xB item log at 0x6E9D5B.
static void CheckExhaustionLegLogsAndStaysSilent()
{
    // BEHAVIOURAL, not textual: invoke the private logger and inspect the row
    // that lands in the shared log channel. A source scan would pass on a
    // commented-out call, and would not notice the wrong column order.
    var method = typeof(TPlayObject).GetMethod(
        "WriteFixedCoordStoneExhaustionLog",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    if (method == null)
        throw new InvalidOperationException(
            "the exhaustion leg must write the type-0xB item log (0x6E9D5B "
            + "call sub_768BE0 with dx=0xB)");

    // the call must be live, not commented out
    var lines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "GameSvr",
        "Players", "TPlayObject.NativeFixedCoordStone.cs"));
    var liveCall = false;
    var silentAfterLog = true;
    for (var i = 0; i < lines.Length; i++)
    {
        var code = lines[i].TrimStart();
        if (code.StartsWith("//") || code.StartsWith("*")) continue;
        if (!code.Contains("WriteFixedCoordStoneExhaustionLog(item);")) continue;
        liveCall = true;
        // native runs remove -> log -> SM_DELITEM -> free, with NO vmt+0xD4 call
        // anywhere in 0x6E9D2E..0x6E9D82, so any message here is invented
        for (var j = i + 1; j < Math.Min(i + 12, lines.Length); j++)
        {
            var after = lines[j].TrimStart();
            if (after.StartsWith("//")) continue;
            if (after.Contains("SysMsg(")) silentAfterLog = false;
        }
    }

    if (!liveCall)
        throw new InvalidOperationException(
            "the exhaustion log call must be LIVE -- a commented-out call does "
            + "not count (0x6E9D5B is unconditional on this leg)");
    if (!silentAfterLog)
        throw new InvalidOperationException(
            "the exhaustion leg must stay SILENT -- there is no vmt+0xD4 call "
            + "between 0x6E9D2E and 0x6E9D82, so any message is invented");

    // now drive the logger and check the emitted row
    M2Share.LogStringList ??= new System.Collections.ArrayList();
    M2Share.LogMsgCriticalSection ??= new object();
    var before = M2Share.LogStringList.Count;
    var player = NewPlayer();
    player.m_sMapName = "MAPX";
    player.m_nCurrX = 11;
    player.m_nCurrY = 22;
    player.m_sCharName = "WHO";
    var item = new TUserItem { wIndex = 0, MakeIndex = 4242 };
    method.Invoke(player, new object[] { item });

    // wIndex 0 has no StdItem, so native's own guard means nothing is written
    Equal(before, M2Share.LogStringList.Count,
        "an item with no StdItem must not produce a log row (sub_768BE0's own "
        + "name lookup is what supplies the column)");

    // The type code and column order are the parts a porter gets wrong, and the
    // behavioural path above cannot reach them without a populated item table.
    // Assert them against the source's emitted row instead, anchored on the
    // AddGameDataLog call so a renamed helper cannot hide it.
    var body = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GameSvr",
        "Players", "TPlayObject.NativeFixedCoordStone.cs"));
    var rowAt = body.IndexOf("AddGameDataLog(string.Join(", StringComparison.Ordinal);
    if (rowAt < 0)
        throw new InvalidOperationException(
            "the log row must reach the same channel the sibling 0x44/0x45/0x46 "
            + "rows use (M2Share.AddGameDataLog)");
    var row = body.Substring(rowAt, Math.Min(300, body.Length - rowAt));
    if (!row.Contains("0x0B", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "the log row must carry native's type code 0xB (0x6E9D55 mov dx,0xB) "
            + "as its first column");
    foreach (var column in new[]
             {
                 "m_sMapName", "m_nCurrX", "m_nCurrY", "m_sCharName",
                 "stdItem.Name", "item.MakeIndex",
             })
        if (!row.Contains(column, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"the log row is missing the {column} column -- sub_768BE0 "
                + "prepends map/X/Y (obj+0x106/0x12C/0x130) before the caller's "
                + "own arguments");
    if (!row.Contains("\"0\"", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "the trailing column is the 0x6E9DE4 literal, an AnsiString of "
            + "length 1 holding '0'");
}

// Registry record 0x7B6ED4: name ShortString (18 GBK bytes) = 重载传送石禁用地图,
// dispatch index dword 401 at +0x18, permission dword 4 at +0x1C. Cross-checked
// against the jump table: [0x622B1C + 401*4] == 0x6281AE.
static void CheckGm401IsRegistered()
{
    var root = FindRepositoryRoot();
    var command = Path.Combine(root, "GameSvr", "Command", "Commands",
        "ReloadFixedCoordDisableMapCommand.cs");
    if (!File.Exists(command))
        throw new InvalidOperationException(
            "GM dispatch index 401 (handler 0x6281AE) has no C# command");

    var source = File.ReadAllText(command);
    RequireContains(command, "M2Share.LoadFixedCoordDisableMap()",
        "GM 401 must actually reload the blacklist (0x6281FC)");
    RequireContains(command, "4)",
        "GM 401 permission is 4 (registry 0x7B6ED4 +0x1C)");

    // success is silent (0x6281FF jumps to the shared epilogue), so the command
    // may speak ONLY on the failure leg
    if (source.Contains("MsgColor.Green", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "GM 401 success is SILENT at 0x6281FF -- a green confirmation is "
            + "invented");

    // the failure literal must be the byte-exact 0x62D204 text
    RequireContains(command, "g_sNativeFixedCoordDisableMapMissing",
        "the failure leg must send the 0x62D204 literal");
    var expected = "传送石禁用地图.txt "
                   + "文件不存在！";
    Equal(expected, M2Share.g_sNativeFixedCoordDisableMapMissing,
        "the missing-file literal must match 0x62D204 exactly (len 31 GBK)");
    Equal(31, HUtil32.GbkEncoding.GetByteCount(
            M2Share.g_sNativeFixedCoordDisableMapMissing),
        "0x62D204's length dword at 0x62D200 is 31");
}

static void RequireContains(string path, string needle, string label)
{
    if (!File.Exists(path))
        throw new InvalidOperationException($"{label}: missing file {path}");
    if (!File.ReadAllText(path).Contains(needle, StringComparison.Ordinal))
        throw new InvalidOperationException($"{label}: '{needle}' not found in {path}");
}

static string FindRepositoryRoot()
    => AuditRepoRoot.Resolve();

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}
