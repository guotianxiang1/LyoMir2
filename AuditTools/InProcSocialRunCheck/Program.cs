using System.Buffers.Binary;
using System.Reflection;
using DBSvr.Core;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// In-process isolated-engine SOCIAL harness (machine-safety FIRST: SINGLE process, NO network stack,
// NO DBSvr, NO MySQL, NO background engine threads; strictly serial; Environment.Exit at the end).
// Same technique as InProcEngineRunCheck: construct the M2Share engine singletons directly (bypassing
// GameApp.Initialize / StartEngine and the 30s DBSvr native-definition gate) and drive the REAL engine
// social flows, capturing the real in-memory state mutations (not model stubs).
//
// Domains:
//   TEAM / GROUP : the real 战神 two-step party pipeline. ClientCreateGroup / ClientAddGroupMember
//                  (CM 1020 / 1021) only QUEUE an invite - their exhaustive E8-callee sets contain
//                  no group-mutating callee, their single state change is sub_6F39B4 - so the
//                  harness then drives the real CM 4412 accept through Operate, which is where
//                  sub_6C3648 allocates the TGroup and both sides' m_GroupOwner is set. Members are
//                  resolved through the real UserEngine.GetPlayObject and the real
//                  m_GroupMembers / m_GroupOwner state is mutated. RUNS.
//   CHANNEL      : the real NativeChannelManager.CreatePublic + Enter + QueryById on the shared
//                  in-memory manager, mutating the real channel membership set. RUNS.
//   RELATION     : SKIPPED (documented, not faked). The only relation store is NativeRelationMySqlStore;
//                  INativeRelationStore and NativeRelationService are internal with no in-memory
//                  implementation, so a REAL relation mutation requires MySQL, which machine-safety
//                  forbids in-process. The front-half name validation is reflection-invokable but the
//                  actual add mutates only via MySQL, so no genuine relation run is claimed.
//   CORPS        : SKIPPED (documented, not faked). NativeCorpsStore is MySQL-backed (OpenConnection
//                  for TryLoad and every write); there is no in-memory corps store, so a REAL corps
//                  create/join hard-requires the DB store.
//
// Evidence goes to stdout and inproc_social_evidence.txt next to the executable.

int rc = 0;
var evidence = new List<string>();
void Log(string s) { evidence.Add(s); Console.WriteLine("  " + s); }
void Assert(bool cond, string msg) { if (!cond) throw new Exception("ASSERT FAILED: " + msg); }

// non-public real entry points driven by reflection
var miCreateGroup = typeof(TPlayObject).GetMethod("ClientCreateGroup",
    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientCreateGroup");
var miAddGroupMember = typeof(TPlayObject).GetMethod("ClientAddGroupMember",
    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientAddGroupMember");
var miPersistAntiCheatPenalty = typeof(TPlayObject).GetMethod(
    "PersistNativeAntiCheatPenalty", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("TPlayObject.PersistNativeAntiCheatPenalty");
var miRestoreAntiCheatPenalty = typeof(TPlayObject).GetMethod(
    "RestoreNativeAntiCheatPenalty", BindingFlags.Instance | BindingFlags.NonPublic,
    null, new[] { typeof(double) }, null)
    ?? throw new MissingMethodException("TPlayObject.RestoreNativeAntiCheatPenalty");
var miTruncateAntiCheatDay = typeof(TPlayObject).GetMethod(
    "NativeTruncateDay64", BindingFlags.Static | BindingFlags.NonPublic,
    null, new[] { typeof(double) }, null)
    ?? throw new MissingMethodException("TPlayObject.NativeTruncateDay64");
var miGetAntiCheatDay = typeof(TPlayObject).GetMethod(
    "GetNativeTruncDaysOnline", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("TPlayObject.GetNativeTruncDaysOnline");
var fiCheatReportPolicyTier = typeof(TPlayObject).GetField(
    "NativeCheatReportPolicyTier", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingFieldException("TPlayObject.NativeCheatReportPolicyTier");

try
{
    PrepareConfig();
    BootSingletons();
    Log("BOOT singletons: g_Config/RandomNumber/ObjectManager/UserEngine/MapManager constructed "
        + "(no GameApp.Initialize, no DBSvr gate, no network, no background threads)");

    var map = CreateBlankMap(48, 48, "social-harness-map");
    Log($"MAP built in-memory '{map.sMapName}' {map.wWidth}x{map.wHeight} (real Envirnoment.Initialize)");

    RunAdminReload(map);
    RunCrossServerWhisper(map);
    RunCrossServerChatBan();
    RunIdent247FailClosed();
    RunSingleQuoteScanMirror();
    RunGlobalAntiCheatMode();
    RunCrossServerAntiCheatPenalty(map);
    RunCastleAttackerMirror(map);
    RunMentorReputation(map);
    RunPlayerNotice(map);
    RunMentorRechargeReward(map);
    RunGroup(map);
    RunChannel();
    RunRelationSkip();
    RunCorpsSkip();

    Console.WriteLine(
        "PASS InProcSocialRunCheck team=REAL(1020/1021-queue-only + 4412-accept->m_GroupMembers) "
        + "channel=REAL(NativeChannelManager.CreatePublic+Enter->members) relation=SKIP(mysql-only-store) "
        + "corps=SKIP(mysql-backed-store) single-process no-network no-DBSvr no-MySQL");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL InProcSocialRunCheck: " + ex);
    rc = 1;
}

try { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "inproc_social_evidence.txt"), evidence); }
catch { /* evidence file is best-effort */ }

// Hard-exit so no lingering engine state can keep the process alive.
Environment.Exit(rc);

// ===================== flows =====================

void RunAdminReload(Envirnoment map)
{
    string testRoot = Path.Combine(AppContext.BaseDirectory,
        "ident213_" + Guid.NewGuid().ToString("N"));
    string envirDirectory = Path.Combine(testRoot, "Envir");
    string shareDirectory = Path.Combine(testRoot, "Share");
    string adminFile = Path.Combine(envirDirectory, "AdminList.txt");
    string feastFile = Path.Combine(shareDirectory, "FeastDays.ini");

    string savedConfigPath = M2Share.sConfigPath;
    string savedRootPath = M2Share.sRootPath;
    string savedEnvirDir = M2Share.g_Config.sEnvirDir;
    string savedBaseDir = M2Share.g_Config.sBaseDir;
    LocalDB savedLocalDb = M2Share.LocalDB;
    NativeFestivalConfig savedFestivalConfig = M2Share.FestivalConfig;
    var savedAdmins = M2Share.UserEngine.m_AdminList.ToList();
    TPlayObject livePlayer = null;

    try
    {
        Directory.CreateDirectory(envirDirectory);
        Directory.CreateDirectory(shareDirectory);
        M2Share.sConfigPath = testRoot;
        M2Share.sRootPath = testRoot;
        M2Share.g_Config.sEnvirDir = "Envir";
        M2Share.g_Config.sBaseDir = "Share";
        M2Share.LocalDB = new LocalDB();
        M2Share.FestivalConfig = null;

        string[] adminLines =
        {
            "; native first-byte comment",
            " * LeadingRejected",
            "3 Rejected",
            "* Alpha",
            "1 Bravo",
            "2 Charlie",
            "* Dup",
            "1 dUP",
            "* LongABCDEFGHIJKLMNO",
            "* ＡCase"
        };
        File.WriteAllBytes(adminFile, HUtil32.GbkEncoding.GetBytes(
            string.Join("\r\n", adminLines)));
        WriteFeastDays(feastFile, stopOnStart: true);

        livePlayer = NewPlayer("Alpha", map);
        livePlayer.m_btPermission = 77;
        RegisterInEngine(livePlayer);

        var mirror = new MirrorMessage();
        mirror.ProcessData(Grobal2.ISM_RELOADADMIN, 0, string.Empty);

        Assert(M2Share.UserEngine.m_AdminList.Count == 7,
            "ident 213 did not accept only */1/2 first-byte admin rows");
        AssertPermission("ALPHA", true, 4, true);
        AssertPermission("bravo", true, 3, true);
        AssertPermission("CHARLIE", true, 2, true);
        AssertPermission("DUP", true, 3, true);
        AssertPermission("Rejected", false, 0, false);
        AssertPermission("LeadingRejected", false, 0, false);
        AssertPermission("ＡCASE", true, 4, true);
        AssertPermission("ａcase", false, 0, false);
        AssertPermission("LongABCDEFGHIJKLMNO", false, 0, false);
        Assert(livePlayer.m_btPermission == 77,
            "ident 213 unexpectedly refreshed an already-online player's permission");

        var longEntry = M2Share.UserEngine.m_AdminList.Single(info =>
            info.sChrName.StartsWith("long", StringComparison.Ordinal));
        Assert(longEntry.NativeChrNameBytes.Length == 14,
            "ident 213 admin ShortString did not truncate to 14 GBK bytes");

        string resolvedFeastPath = NativeFestivalConfig.ResolveDefaultPath(
            testRoot, "Share");
        Assert(Path.GetFullPath(resolvedFeastPath) == Path.GetFullPath(feastFile),
            "ident 213 probed a config subdirectory instead of Share/FeastDays.ini");
        Assert(NativeFestivalConfig.MaximumEntries == 100,
            "ident 213 FeastDays loop cap is not the native 100 entries");
        Assert(M2Share.FestivalConfig is { SourceLoaded: true }
            && M2Share.FestivalConfig.Entries.Count == 2,
            "ident 213 did not stop FeastDays at the first Start=0 row");
        Assert(M2Share.FestivalConfig.Entries[0].Start ==
               ((28 - 1) * 24 * 60 * 60),
            "ident 213 did not parse Start as native second-of-year");
        Assert(M2Share.FestivalConfig.Entries[1].Start ==
               ((31 + 28 + 31) * 24 * 60 * 60) &&
               M2Share.FestivalConfig.Entries[1].End ==
               ((31 + 28 + 31 + 30) * 24 * 60 * 60),
            "ident 213 did not accept native hexadecimal date fields");
        Assert(M2Share.FestivalConfig.Entries[0].NativeFileNameBytes.Length ==
               NativeFestivalConfig.FileNameMaximumGbkBytes,
            "FeastDays FileName did not truncate to native ShortString[31]");

        string numericFeastFile = Path.Combine(shareDirectory, "FeastNumbers.ini");
        File.WriteAllText(numericFeastFile,
            "[Feast1]\r\nFileName=word-wrap.txt\r\n"
            + "Start=$800007DB-1-1 1:1:0\r\n"
            + "End=67547-65537-65537 65537:65537:65536\r\n",
            HUtil32.GbkEncoding);
        Assert(NativeFestivalConfig.TryLoad(numericFeastFile,
                out var numericFeast, out var numericFeastError) &&
               numericFeastError.Length == 0 && numericFeast.Entries.Count == 1 &&
               numericFeast.Entries[0].Start == 3660 &&
               numericFeast.Entries[0].End == 3660,
            "ident 213 did not preserve native high-bit parsing and WORD date fields");
        foreach (string nativeZeroDate in new[]
                 { "2147483648", "$FFFFFFFF", "-0x80000000" })
        {
            File.WriteAllText(numericFeastFile,
                "[Feast1]\r\nFileName=stop.txt\r\nStart=" + nativeZeroDate
                + "-1-1 1:1:0\r\nEnd=2011-03-01 00:00:00\r\n",
                HUtil32.GbkEncoding);
            Assert(NativeFestivalConfig.TryLoad(numericFeastFile,
                    out var zeroDateFeast, out var zeroDateError) &&
                   zeroDateError.Length == 0 && zeroDateFeast.SourceLoaded &&
                   zeroDateFeast.Entries.Count == 0,
                "ident 213 did not preserve native numeric zero-date boundary "
                + nativeZeroDate);
        }

        WriteFeastDays(feastFile, stopOnStart: false);
        mirror.ProcessData(Grobal2.ISM_RELOADADMIN, 0, string.Empty);
        Assert(M2Share.FestivalConfig.Entries.Count == 4,
            "ident 213 did not append FeastDays entries on repeated reload");

        foreach (string emptyFeastContent in new[]
                 { string.Empty, ";; comment", "; comment", "/* comment\r\n*/" })
        {
            File.WriteAllText(feastFile, emptyFeastContent, HUtil32.GbkEncoding);
            Assert(NativeFestivalConfig.TryLoad(feastFile, out var emptyFeast,
                    out var emptyFeastError) && emptyFeastError.Length == 0 &&
                   emptyFeast.SourceLoaded && emptyFeast.Entries.Count == 0,
                "ident 213 did not treat an existing empty/comment FeastDays.ini as an empty source");
        }
        mirror.ProcessData(Grobal2.ISM_RELOADADMIN, 0, string.Empty);
        Assert(M2Share.FestivalConfig.Entries.Count == 4,
            "ident 213 empty/comment FeastDays.ini changed the existing feast list");

        File.Delete(adminFile);
        File.Delete(feastFile);
        mirror.ProcessData(Grobal2.ISM_RELOADADMIN, 0, string.Empty);
        Assert(M2Share.UserEngine.m_AdminList.Count == 0,
            "ident 213 missing AdminList.txt did not clear the old admin list");
        Assert(M2Share.FestivalConfig.Entries.Count == 4,
            "ident 213 missing FeastDays.ini changed the existing feast list");

        Log("MIRROR 213: raw GBK admin parsing/fold/truncate, */1/2 levels, "
            + "head-prepend duplicate precedence, missing-file clear, online no-refresh, "
            + "Share FeastDays 100-cap/zero-stop/31-byte truncate/append/missing-preserve verified");
    }
    finally
    {
        M2Share.sConfigPath = savedConfigPath;
        M2Share.sRootPath = savedRootPath;
        M2Share.g_Config.sEnvirDir = savedEnvirDir;
        M2Share.g_Config.sBaseDir = savedBaseDir;
        M2Share.LocalDB = savedLocalDb;
        M2Share.FestivalConfig = savedFestivalConfig;
        M2Share.UserEngine.m_AdminList.Clear();
        foreach (var admin in savedAdmins)
            M2Share.UserEngine.m_AdminList.Add(admin);

        if (livePlayer != null)
        {
            var listField = typeof(UserEngine).GetField("m_PlayObjectList",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (listField?.GetValue(M2Share.UserEngine) is System.Collections.IList players)
                players.Remove(livePlayer);
        }
        if (Directory.Exists(testRoot))
            Directory.Delete(testRoot, true);
    }

    void AssertPermission(string name, bool expectedFound,
        byte expectedPermission, bool expectEmptyIp)
    {
        string ipAddress = "unchanged";
        byte permission = 0xFF;
        bool found = M2Share.UserEngine.GetHumPermission(
            name, ref ipAddress, ref permission);
        Assert(found == expectedFound && permission == expectedPermission,
            $"ident 213 permission mismatch for '{name}': found={found}, level={permission}");
        if (expectEmptyIp)
            Assert(ipAddress.Length == 0,
                $"ident 213 invented an IP field for '{name}'");
    }

    static void WriteFeastDays(string path, bool stopOnStart)
    {
        string stopRow = stopOnStart
            ? "Start=2011-02-30 00:00:00\r\nEnd=2011-06-01 00:00:00\r\n"
            : "Start=2011-06-01 00:00:00\r\nEnd=2011-02-30 00:00:00\r\n";
        string content =
            "[Feast1]\r\n"
            + "FileName=12345678901234567890123456789012345.txt\r\n"
            + "Start=$7DB-01-28 00:00:00\r\nEnd=2011-03-01 00:00:00\r\n"
            + "[Feast2]\r\nFileName=second.txt\r\n"
            + "Start=0x7DB-04-01 00:00:00\r\nEnd=X7DB-05-01 00:00:00\r\n"
            + "[Feast3]\r\nFileName=stop.txt\r\n" + stopRow
            + "[Feast4]\r\nFileName=must-not-load.txt\r\n"
            + "Start=2011-07-01 00:00:00\r\nEnd=2011-08-01 00:00:00\r\n";
        File.WriteAllText(path, content, HUtil32.GbkEncoding);
    }
}

void RunCrossServerWhisper(Envirnoment map)
{
    var recipient = NewPlayer("mirror-whisper-target", map);
    recipient.m_boHearWhisper = true;
    recipient.m_dwChatShieldMask = 0;
    RegisterInEngine(recipient);

    var encoder = typeof(UserEngine).GetMethod("EncodeServerGroupMessage",
        BindingFlags.Static | BindingFlags.NonPublic, null,
        new[] { typeof(int), typeof(int), typeof(int), typeof(string) }, null)
        ?? throw new MissingMethodException("UserEngine.EncodeServerGroupMessage/4");
    var wire = (string)encoder.Invoke(null, new object[]
    {
        Grobal2.ISM_WHISPER, 7, 73,
        "mirror-whisper-target/mirror-sender=> hello"
    });
    Assert(wire == "203/7/73/mirror-whisper-target/mirror-sender=> hello",
        "ISM 203 sender did not emit nCode/server/level/body");

    var mirror = new MirrorMessage();
    foreach (var (wireLevel, expectedLevel) in new (int, int)[]
    {
        (0, 0), (1, 1), (73, 73), (65535, 65535), (-1, 65535), (65536, 0)
    })
    {
        recipient.m_MsgList.Clear();
        mirror.ProcessData(Grobal2.ISM_WHISPER, M2Share.nServerIndex,
            wireLevel + "/mirror-whisper-target/mirror-sender=> hello");
        Assert(recipient.m_MsgList.Count == 1
            && recipient.m_MsgList[0].wIdent == Grobal2.RM_WHISPER,
            $"ISM 203 level {wireLevel} did not queue exactly one RM_WHISPER");
        var message = recipient.m_MsgList[0];
        Assert(message.wParam == expectedLevel,
            $"ISM 203 level {wireLevel} expected low word {expectedLevel}, got {message.wParam}");
        Assert(message.Buff == "mirror-sender=> hello",
            $"ISM 203 level {wireLevel} changed the whisper body");
    }

    recipient.m_MsgList.Clear();
    mirror.ProcessData(Grobal2.ISM_WHISPER, M2Share.nServerIndex,
        "mirror-whisper-target/mirror-sender=> legacy");
    Assert(recipient.m_MsgList.Count == 1
        && recipient.m_MsgList[0].wParam == 0
        && recipient.m_MsgList[0].Buff == "mirror-sender=> legacy",
        "legacy three-field ISM 203 did not fall back to sender level 0");

    var numericRecipient = NewPlayer("12345", map);
    numericRecipient.m_boHearWhisper = true;
    numericRecipient.m_dwChatShieldMask = 0;
    RegisterInEngine(numericRecipient);
    mirror.ProcessData(Grobal2.ISM_WHISPER, M2Share.nServerIndex,
        "12345/mirror-sender=> legacy/with-slash");
    Assert(numericRecipient.m_MsgList.Count == 1
        && numericRecipient.m_MsgList[0].wParam == 0
        && numericRecipient.m_MsgList[0].Buff
            == "mirror-sender=> legacy/with-slash",
        "numeric legacy recipient was mistaken for a four-field sender level");

    Log("MIRROR 203: four-field sender level round-trips as low word; legacy and numeric-recipient frames fall back to 0");
}

void RunCrossServerChatBan()
{
    var fixtureRoot = Path.Combine(AppContext.BaseDirectory,
        "chat-ban-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(fixtureRoot);
    var blockFile = Path.Combine(fixtureRoot, "BlockUsers.Dat");
    M2Share.g_DenySayMsgList = NativeMirrorChatBan.CreateStore();
    try
    {
        // Startup load is the real BlockUsers.Dat path, not a dictionary fixture.
        File.WriteAllBytes(blockFile, BuildBlockImage(("seed", 30)));
        Assert(NativeMirrorChatBan.TryInitializePersistentStore(fixtureRoot,
                out var loadedCount, out var loadError) && loadedCount == 1,
            "BlockUsers.Dat startup load failed: " + loadError);
        Assert(NativeMirrorChatBan.Contains("SEED"),
            "loaded BlockUsers.Dat row did not hit by native ASCII fold");
        Assert(NativeMirrorChatBan.Remove("seed") && !File.Exists(blockFile),
            "removing the last loaded row did not delete BlockUsers.Dat");

        var mirror = new MirrorMessage();
        mirror.ProcessData(Grobal2.ISM_CHATPROHIBITION, 0, "1/PlayerA");
        Assert(M2Share.g_DenySayMsgList.TryGetValue("pLAYERa", out var firstExpiry),
            "ISM 209 did not use the native ASCII-only case fold");
        Assert(File.Exists(blockFile) && File.ReadAllBytes(blockFile).Length == 20,
            "new 209 row did not persist one native record");

        mirror.ProcessData(Grobal2.ISM_CHATPROHIBITION, 0, "65537/PLAYERA");
        Assert(M2Share.g_DenySayMsgList.Count == 1,
            "ISM 209 created a duplicate for an ASCII case variant");
        Assert(M2Share.g_DenySayMsgList["playera"] == firstExpiry + 1000,
            "ISM 209 did not add the third dword's low 16-bit seconds");
        Assert(NativeBlockUserRecordCodec.DecodeValue(File.ReadAllBytes(blockFile), 0) == 1,
            "existing native add incorrectly re-saved the extended duration");

        const string nonAsciiUpper = "\u7A46";
        const string nonAsciiLower = "\u70E8";
        mirror.ProcessData(Grobal2.ISM_CHATPROHIBITION, 0,
            "1/" + nonAsciiUpper);
        Assert(!NativeMirrorChatBan.Contains(nonAsciiLower),
            "native ASCII fold was broadened to non-ASCII case folding");

        mirror.ProcessData(Grobal2.ISM_CHATPROHIBITION, 0,
            "1/ABCDEFGHIJKLMNOP");
        Assert(M2Share.g_DenySayMsgList.ContainsKey("abcdefghijklmno")
            && !M2Share.g_DenySayMsgList.ContainsKey("abcdefghijklmnop"),
            "ISM 209 did not store the native 15-byte folded name");

        mirror.ProcessData(Grobal2.ISM_CHATPROHIBITION, 0, "1/");
        Assert(NativeMirrorChatBan.Contains(string.Empty),
            "ISM 209 incorrectly rejected an empty ShortString name");

        mirror.ProcessData(Grobal2.ISM_CHATPROHIBITIONCANCEL, 0, "pLaYeRa");
        Assert(!NativeMirrorChatBan.Contains("PLAYERA")
            && NativeMirrorChatBan.Contains(nonAsciiUpper),
            "ISM 210 did not remove exactly the ASCII-folded name");

        mirror.ProcessData(Grobal2.ISM_CHATPROHIBITIONCANCEL, 0, "abcdefghijklmno");
        Assert(!NativeMirrorChatBan.Contains("ABCDEFGHIJKLMNOP"),
            "ISM 210 did not remove the folded/truncated name");

        var expireName = "expire-row";
        mirror.ProcessData(Grobal2.ISM_CHATPROHIBITION, 0, "1/" + expireName);
        var expireRow = NativeMirrorChatBan.Snapshot().Single(e =>
            string.Equals(e.Name, expireName, StringComparison.OrdinalIgnoreCase));
        var removed = NativeMirrorChatBan.Tick(
            unchecked((int)expireRow.LastTickMs + 10001L));
        Assert(removed.Any(name => string.Equals(name, expireName,
                    StringComparison.OrdinalIgnoreCase))
            && !File.Exists(blockFile),
            "native tick expiry did not remove the last row and delete the file");

        var wrappedDeadline = unchecked((int)0x0000_0100);
        Assert(!NativeMirrorChatBan.IsExpired(0, wrappedDeadline)
            && NativeMirrorChatBan.IsExpired(0x200, wrappedDeadline),
            "live mute expiry did not use unsigned 32-bit tick arithmetic");
        Assert(NativeMirrorChatBan.HasElapsed(0, unchecked((int)0xFFFF_D8EF), 10000U),
            "live 10-second gate did not open across tick wrap");

        Log("MIRROR 209/210: BlockUsers.Dat load/save/delete, ASCII-only case fold, "
            + "low-word duration, duplicate-safe canonical rows, empty/truncated names, "
            + "and tick expiry verified");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
            Directory.Delete(fixtureRoot, true);
    }
}

static byte[] BuildBlockImage(params (string name, int value)[] rows)
{
    var data = new byte[rows.Length * NativeBlockUserRecordCodec.RecordSize];
    for (var i = 0; i < rows.Length; i++)
        NativeBlockUserRecordCodec.EncodeRecord(data,
            i * NativeBlockUserRecordCodec.RecordSize,
            HUtil32.GbkEncoding.GetBytes(rows[i].name), rows[i].value);
    return data;
}

void RunIdent247FailClosed()
{
    const int flatIndex = 102;
    const int sentinel = 0x12345678;
    M2Share.g_Config.GlobalVal[flatIndex] = sentinel;

    new MirrorMessage().ProcessData(Grobal2.ISM_IDENT_247, 0, "1/2/99");

    Assert(M2Share.g_Config.GlobalVal[flatIndex] == sentinel,
        "ident 247 accepted an invented slash-text frame");
    Log("MIRROR 247: slash-text frame is silently rejected until raw 13-byte ingress exists");
}

void RunSingleQuoteScanMirror()
{
    var share = Path.Combine(AppContext.BaseDirectory, "social-switch-fixture");
    var config = Path.Combine(share, "Config");
    Directory.CreateDirectory(config);
    var switchFile = Path.Combine(config, "ServerSwitch.Bin");
    File.WriteAllBytes(switchFile,
        new byte[] { 0x78, 0x56, 0x34, 0x12, 0x15 });
    Assert(NativeServerSwitchStore.TryLoad(share, out var store, out var error),
        "ISM 207 fixture store failed to load: " + error);
    M2Share.ServerSwitches = store;

    const uint expectedLow = 0x89ABCDEFu;
    var wireParam = unchecked((int)expectedLow).ToString(
        System.Globalization.CultureInfo.InvariantCulture);
    var mirror = new MirrorMessage();
    mirror.ProcessData(Grobal2.ISM_SINGLEQUOTE_SCAN, 0,
        wireParam + "/ignored");

    var snapshot = store.GetSnapshot();
    Assert(BinaryPrimitives.ReadUInt32LittleEndian(snapshot) == expectedLow
        && snapshot[4] == 0x15,
        "ISM 207 did not replace the low dword while preserving the uncarried fifth byte");
    var persisted = File.ReadAllBytes(switchFile);
    Assert(persisted.Length >= 5
        && BinaryPrimitives.ReadUInt32LittleEndian(persisted) == expectedLow
        && persisted[4] == 0x15,
        "ISM 207 did not persist the five-byte switch snapshot");

    mirror.ProcessData(Grobal2.ISM_SINGLEQUOTE_SCAN, 0,
        wireParam + "/ignored-again");
    Assert(store.GetSnapshot().SequenceEqual(snapshot),
        "ISM 207 same-mask path changed the switch bitmap");
    Log("MIRROR 207: signed P2 bit-pattern replaces/persists low dword; fifth byte is deterministically preserved (FAIL-CLOSED)");
}

void RunGlobalAntiCheatMode()
{
    var mirror = new MirrorMessage();
    foreach (var (wireParam, expectedTier) in new[]
    {
        (0, (byte)1), (1, (byte)2), (2, (byte)3)
    })
    {
        fiCheatReportPolicyTier.SetValue(null, (byte)9);
        mirror.ProcessData(Grobal2.ISM_GLOBAL_MODE_SET, 0,
            wireParam + "/ignored");
        Assert((byte)fiCheatReportPolicyTier.GetValue(null)! == expectedTier,
            $"ISM 214 parameter {wireParam} did not map to tier {expectedTier}");
    }

    foreach (var invalid in new[] { -1, 3, int.MinValue, int.MaxValue })
    {
        fiCheatReportPolicyTier.SetValue(null, (byte)9);
        new MirrorMessage().ProcessData(Grobal2.ISM_GLOBAL_MODE_SET, 0,
            invalid + "/ignored");
        Assert((byte)fiCheatReportPolicyTier.GetValue(null)! == 9,
            $"ISM 214 out-of-range parameter {invalid} changed the global tier");
    }

    fiCheatReportPolicyTier.SetValue(null, (byte)0);
    Log("MIRROR 214: P2 values 0/1/2 map to global anti-cheat tiers 1/2/3; all other int32 values are inert");
}

void RunCrossServerAntiCheatPenalty(Envirnoment sourceMap)
{
    var blackRoom = CreateBlankMap(24, 24, "anti-blackroom");
    blackRoom.Flag.boBLACKROOM = true;
    var player = NewPlayer("anti-player", sourceMap);
    player.m_sUserID = "AccountA";
    player.m_nCurrX = 1;
    player.m_nCurrY = 1;
    player.m_Abil.Level = 40;
    Assert(ReferenceEquals(sourceMap.AddToMap(1, 1,
            CellType.OS_MOVINGOBJECT, player), player),
        "anti-cheat fixture could not register player on source map");
    RegisterInEngine(player);
    M2Share.LogStringList.Clear();

    var mirror = new MirrorMessage();
    var expectedDayBefore = GetAntiCheatDay(player);
    mirror.ProcessData(Grobal2.ISM_ANTICHEAT_PENALTY, 0,
        "3/aCCOUNTa");

    var expectedDayAfter = GetAntiCheatDay(player);
    Assert(player.m_btNativeCheatPenaltyTier == 3
        && (player.m_nNativeCheatPenaltyExpiryDay
                == unchecked(expectedDayBefore + 4)
            || player.m_nNativeCheatPenaltyExpiryDay
                == unchecked(expectedDayAfter + 4)),
        "ISM 202 did not apply native tier/expiry formula");
    Assert(ReferenceEquals(player.m_PEnvir, blackRoom),
        "ISM 202 did not prefer the first BLACKROOM map");
    Assert(M2Share.LogStringList.Count == 1,
        "ISM 202 positive branch did not emit exactly one game-data log");
    var setLog = M2Share.LogStringList[0]?.ToString()?.Split('\t')
        ?? Array.Empty<string>();
    Assert(setLog.Length == 9 && setLog[0] == "29"
        && setLog[5] == "设置外挂惩罚" && setLog[6] == "3"
        && setLog[7] == "2" && setLog[8] == "aCCOUNTa",
        "ISM 202 positive game-data log fields differ from native");

    M2Share.LogStringList.Clear();
    mirror.ProcessData(Grobal2.ISM_ANTICHEAT_PENALTY, 0,
        "0/AccountA");
    Assert(player.m_btNativeCheatPenaltyTier == 0
        && player.m_nNativeCheatPenaltyExpiryDay == 0,
        "ISM 202 non-positive branch did not clear penalty state");
    var clearLog = M2Share.LogStringList[0]?.ToString()?.Split('\t')
        ?? Array.Empty<string>();
    Assert(clearLog.Length == 9 && clearLog[0] == "29"
        && clearLog[5] == "清除外挂惩罚" && clearLog[6] == "1"
        && clearLog[7] == "2" && clearLog[8] == "AccountA",
        "ISM 202 clear game-data log fields differ from native");

    foreach (var value in new[] { 0x12345678, int.MinValue, int.MaxValue })
    {
        player.m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
        player.m_nNativeCheatPenaltyExpiryDay = value;
        Assert(PersistAntiCheatPenalty(player),
            $"ISM 202 could not persist signed value {value}");
        Assert(BinaryPrimitives.ReadInt32LittleEndian(
                player.m_NativeHumanData.AsSpan(0x1E8, sizeof(int))) == value,
            $"ISM 202 changed signed value {value} at rec+0x1E8");
    }

    player.m_NativeHumanData = null;
    player.m_nNativeCheatPenaltyExpiryDay = 0x12345678;
    Assert(PersistAntiCheatPenalty(player)
        && player.m_NativeHumanData?.Length == NativeHumanDataCodec.DataRecordSize
        && BinaryPrimitives.ReadInt32LittleEndian(
            player.m_NativeHumanData.AsSpan(0x1E8, sizeof(int))) == 0x12345678,
        "ISM 202 did not allocate and populate a full native record");

    var shortRaw = Enumerable.Repeat((byte)0xA5, 0x1EC).ToArray();
    player.m_NativeHumanData = shortRaw;
    player.m_nNativeCheatPenaltyExpiryDay = 1;
    Assert(!PersistAntiCheatPenalty(player)
        && shortRaw.All(value => value == 0xA5),
        "ISM 202 accepted or modified a non-native-size active record");
    player.m_btNativeCheatPenaltyTier = 2;
    Assert(!RestoreAntiCheatPenalty(player, 100.0)
        && player.m_nNativeCheatPenaltyExpiryDay == 0
        && player.m_btNativeCheatPenaltyTier == 2,
        "ISM 202 restore accepted a non-native-size record");
    player.m_nNativeCheatPenaltyExpiryDay = 0;
    Assert(PersistAntiCheatPenalty(player)
        && shortRaw.All(value => value == 0xA5),
        "ISM 202 inactive short-record policy changed bytes or failed");
    player.m_NativeHumanData = Enumerable.Repeat((byte)0x5A,
        NativeHumanDataCodec.DataRecordSize + 1).ToArray();
    player.m_nNativeCheatPenaltyExpiryDay = 1;
    Assert(!PersistAntiCheatPenalty(player)
        && player.m_NativeHumanData.All(value => value == 0x5A),
        "ISM 202 accepted or modified an oversized active record");

    Assert(TruncateAntiCheatDay(123.9) == 123
        && TruncateAntiCheatDay(-123.9) == -123,
        "ISM 202 day conversion is not truncation toward zero");
    foreach (var invalid in new[]
    {
        double.NaN, double.PositiveInfinity, double.NegativeInfinity,
        9223372036854775808.0d, -9223372036854775808.0d
    })
    {
        Assert(TruncateAntiCheatDay(invalid) == long.MinValue,
            $"ISM 202 x87 integer-indefinite mismatch for {invalid}");
    }

    CheckRestore(storedDay: 0, level: 35, dbClockNow: 100.0,
        shouldBeActive: false);
    CheckRestore(storedDay: -1, level: 35, dbClockNow: 100.0,
        shouldBeActive: false);
    CheckRestore(storedDay: int.MinValue, level: 35, dbClockNow: 100.0,
        shouldBeActive: false);
    CheckRestore(storedDay: 100, level: 34, dbClockNow: 100.0,
        shouldBeActive: false);
    CheckRestore(storedDay: 100, level: 35, dbClockNow: 99.9,
        shouldBeActive: true);
    CheckRestore(storedDay: 100, level: 35, dbClockNow: 100.9,
        shouldBeActive: true);
    CheckRestore(storedDay: 100, level: 35, dbClockNow: 106.9,
        shouldBeActive: true);
    CheckRestore(storedDay: 100, level: 35, dbClockNow: 107.0,
        shouldBeActive: false);
    CheckRestore(storedDay: 110, level: 35, dbClockNow: 100.0,
        shouldBeActive: true);
    CheckRestore(storedDay: 1, level: 35, dbClockNow: double.NaN,
        shouldBeActive: false);

    var wrappedDayBefore = GetAntiCheatDay(player);
    mirror.ProcessData(Grobal2.ISM_ANTICHEAT_PENALTY, 0,
        int.MaxValue + "/AccountA");
    var wrappedDayAfter = GetAntiCheatDay(player);
    Assert(player.m_btNativeCheatPenaltyTier == 3
        && (player.m_nNativeCheatPenaltyExpiryDay
                == unchecked(wrappedDayBefore + 7 - int.MaxValue)
            || player.m_nNativeCheatPenaltyExpiryDay
                == unchecked(wrappedDayAfter + 7 - int.MaxValue)),
        "ISM 202 positive int32 parameter did not preserve native wraparound");
    mirror.ProcessData(Grobal2.ISM_ANTICHEAT_PENALTY, 0, "-1/AccountA");
    Assert(player.m_btNativeCheatPenaltyTier == 0
        && player.m_nNativeCheatPenaltyExpiryDay == 0,
        "ISM 202 negative parameter did not take the native clear branch");

    Log("MIRROR 202: account match, BLACKROOM move, logs, int32 record round-trip and level/day restore gates verified");

    bool PersistAntiCheatPenalty(TPlayObject target)
        => (bool)miPersistAntiCheatPenalty.Invoke(target, null)!;

    bool RestoreAntiCheatPenalty(TPlayObject target, double dbClockNow)
        => (bool)miRestoreAntiCheatPenalty.Invoke(target,
            new object[] { dbClockNow })!;

    long TruncateAntiCheatDay(double value)
        => (long)miTruncateAntiCheatDay.Invoke(null, new object[] { value })!;

    int GetAntiCheatDay(TPlayObject target)
        => (int)miGetAntiCheatDay.Invoke(target, null)!;

    void CheckRestore(int storedDay, ushort level, double dbClockNow,
        bool shouldBeActive)
    {
        player.m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
        BinaryPrimitives.WriteInt32LittleEndian(
            player.m_NativeHumanData.AsSpan(0x1E8, sizeof(int)), storedDay);
        player.m_Abil.Level = level;
        player.m_btNativeCheatPenaltyTier = 2;

        Assert(RestoreAntiCheatPenalty(player, dbClockNow),
            "ISM 202 rejected a full native record during restore");
        if (shouldBeActive)
        {
            Assert(player.m_nNativeCheatPenaltyExpiryDay == storedDay
                && player.m_btNativeCheatPenaltyTier == 3,
                $"ISM 202 restore rejected active stored={storedDay}, level={level}, db={dbClockNow}");
        }
        else
        {
            Assert(player.m_nNativeCheatPenaltyExpiryDay == 0
                && player.m_btNativeCheatPenaltyTier == 2,
                $"ISM 202 restore changed the wrong field for stored={storedDay}, level={level}, db={dbClockNow}");
        }
    }
}

void RunMentorReputation(Envirnoment map)
{
    var master = NewPlayer("mentor-reputation-master", map);
    master.m_nShengWan = 10;
    RegisterInEngine(master);

    var mirror = new MirrorMessage();
    mirror.ProcessData(Grobal2.ISM_MENTOR_REPUTATION, 0,
        "5/mentor-reputation-master/student-a");
    const string expected = "恭喜：您的徒弟: student-a"
        + " 等级提升，给您带来了: 5 点声望增加";
    Assert(master.m_nShengWan == 15
        && master.m_MsgList.Count == 1
        && master.m_MsgList[0].wIdent == Grobal2.RM_SYSMESSAGE
        && master.m_MsgList[0].Buff == expected,
        "ISM 224 did not add reputation and emit the exact native message only");

    master.m_MsgList.Clear();
    mirror.ProcessData(Grobal2.ISM_MENTOR_REPUTATION, 0,
        "0/mentor-reputation-master/student-a");
    mirror.ProcessData(Grobal2.ISM_MENTOR_REPUTATION, 0,
        "-1/mentor-reputation-master/student-a");
    mirror.ProcessData(Grobal2.ISM_MENTOR_REPUTATION, 0, "5//student-a");
    Assert(master.m_nShengWan == 15 && master.m_MsgList.Count == 0,
        "ISM 224 non-positive or empty-master gate changed state");

    master.m_nShengWan = int.MaxValue;
    mirror.ProcessData(Grobal2.ISM_MENTOR_REPUTATION, 0,
        "1/mentor-reputation-master/");
    Assert(master.m_nShengWan == int.MinValue
        && master.m_MsgList.Count == 1
        && master.m_MsgList[0].Buff == "恭喜：您的徒弟:  等级提升，给您带来了: 1 点声望增加",
        "ISM 224 did not preserve unchecked add or empty-student behavior");
    Log("MIRROR 224: master/student split, positive gate, unchecked reputation add and exact Blue/Hint text verified");
}

void RunCastleAttackerMirror(Envirnoment hostedMap)
{
    var guildManager = new AssociationManager();
    var guildListField = typeof(AssociationManager).GetField("GuildList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("AssociationManager.GuildList");
    var guildList = (System.Collections.IList)guildListField.GetValue(guildManager);
    var caseGuild = new Association("CaseGuild");
    var hostGuild = new Association("HostGuild");
    var nonAsciiGuild = new Association("\u00C4Guild");
    guildList.Add(caseGuild);
    guildList.Add(hostGuild);
    guildList.Add(nonAsciiGuild);
    M2Share.GuildManager = guildManager;

    var castleManager = new CastleManager();
    var castleListField = typeof(CastleManager).GetField("_castleList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("CastleManager._castleList");
    var castleList = (System.Collections.IList)castleListField.GetValue(castleManager);
    M2Share.CastleManager = castleManager;

    var mirror = new MirrorMessage();
    var userEngine = M2Share.UserEngine;
    M2Share.UserEngine = null;
    mirror.ProcessData(Grobal2.ISM_RELOADCASTLEINFO, 0, "caseguild");

    var castle = new TUserCastle("0");
    castleList.Add(castle);

    var attackListField = typeof(TUserCastle).GetField("m_AttackWarList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("TUserCastle.m_AttackWarList");
    var attackList = (System.Collections.IList)attackListField.GetValue(castle);
    var attackFile = Path.Combine(M2Share.sConfigPath,
        M2Share.g_Config.sCastleDir, "AttackSabukWall.txt");
    if (File.Exists(attackFile)) File.Delete(attackFile);

    mirror.ProcessData(Grobal2.ISM_RELOADCASTLEINFO, 0, null);
    mirror.ProcessData(Grobal2.ISM_RELOADCASTLEINFO, 0, string.Empty);
    mirror.ProcessData(Grobal2.ISM_RELOADCASTLEINFO, 0, "missing-guild");
    mirror.ProcessData(Grobal2.ISM_RELOADCASTLEINFO, 0, "\u00E4guild");
    Assert(attackList.Count == 0,
        "ident 212 changed state for null/empty/unknown/non-ASCII-case body");

    var dayBefore = DateTime.Now.AddDays(3.0).Date;
    mirror.ProcessData(Grobal2.ISM_RELOADCASTLEINFO, 0, "caseguild");
    var dayAfter = DateTime.Now.AddDays(3.0).Date;
    Assert(attackList.Count == 1
        && ReferenceEquals(((TAttackerInfo)attackList[0]).Guild, caseGuild),
        "ident 212 did not resolve the ASCII case variant into Castle[0]");
    Assert(((TAttackerInfo)attackList[0]).AttackDate.Date == dayBefore
        || ((TAttackerInfo)attackList[0]).AttackDate.Date == dayAfter,
        "ident 212 did not schedule the attacker at native Now+3 days");
    Assert(!File.Exists(attackFile),
        "ident 212 persisted AttackSabukWall.txt while castle+0x1C was nil");

    mirror.ProcessData(Grobal2.ISM_RELOADCASTLEINFO, 0, "CASEGUILD");
    mirror.ProcessData(Grobal2.ISM_RELOADCASTLEINFO, 0, "CaseGuildX");
    Assert(attackList.Count == 1,
        "ident 212 duplicate or prefix lookup added another attacker");

    castle.m_MapCastle = hostedMap;
    mirror.ProcessData(Grobal2.ISM_RELOADCASTLEINFO, 0, "HOSTGUILD");
    Assert(attackList.Count == 2
        && ReferenceEquals(((TAttackerInfo)attackList[1]).Guild, hostGuild),
        "ident 212 hosted receiver did not append the second guild");
    Assert(File.Exists(attackFile),
        "ident 212 hosted receiver did not persist AttackSabukWall.txt");
    var persisted = File.ReadAllText(attackFile, HUtil32.GbkEncoding);
    Assert(persisted.Contains("CaseGuild", StringComparison.Ordinal)
        && persisted.Contains("HostGuild", StringComparison.Ordinal),
        "ident 212 persisted attacker list lost a guild name");
    M2Share.UserEngine = userEngine;
    Log("MIRROR 212: ASCII-folded lookup, Castle[0], pointer duplicate gate, Now+3, host-only persistence and no re-broadcast verified");
}

void RunMentorRechargeReward(Envirnoment map)
{
    var master = NewPlayer("mentor-master", map);
    master.m_Abil.Exp = 123;
    master.m_Abil.MaxExp = 1_000_000;
    master.m_MsgList.Clear();
    RegisterInEngine(master);

    var mirror = new MirrorMessage();
    mirror.ProcessData(Grobal2.ISM_MENTOR_RECHARGE_REWARD, 0,
        "999/mentor-master/mentor-student");
    Assert(master.m_Abil.Exp == 123,
        "ident 228 accepted an experience reward below the native 1000 gate");
    Assert(master.m_MsgList.Count == 0,
        "ident 228 queued messages below the native 1000 gate");

    mirror.ProcessData(Grobal2.ISM_MENTOR_RECHARGE_REWARD, 0,
        "1000/mentor-master/mentor-student");

    const string expected = "恭喜，您曾经的徒弟mentor-student"
        + "实力又进一步，“比奇国王”特赠您经验值1000";
    var winExp = master.m_MsgList.SingleOrDefault(
        message => message.wIdent == Grobal2.RM_WINEXP);
    var sysMsg = master.m_MsgList.SingleOrDefault(
        message => message.wIdent == Grobal2.RM_SYSMESSAGE);
    Assert(master.m_Abil.Exp == 1123,
        "ident 228 did not grant the native third-dword experience value");
    Assert(master.m_MsgList.Count == 2,
        "ident 228 should queue exactly RM_WINEXP and RM_SYSMESSAGE");
    Assert(winExp.nParam1 == 1000 && winExp.wParam == 0,
        "ident 228 RM_WINEXP does not carry the awarded experience");
    Assert(sysMsg.Buff == expected,
        "ident 228 mentor-recharge text differs from the native concatenation");
    Assert(sysMsg.nParam1 == M2Share.g_Config.btBlueMsgFColor
        && sysMsg.nParam2 == M2Share.g_Config.btBlueMsgBColor,
        "ident 228 mentor-recharge message is not Blue/Hint");
    Log("MIRROR 228: 999 rejected; 1000 granted; exact mentor text and Blue/Hint queued");
}

void RunPlayerNotice(Envirnoment map)
{
    var recipient = NewPlayer("player-notice-target", map);
    recipient.m_btPermission = 0;
    var leadingSlashDecoy = NewPlayer("body-after-leading-slash", map);
    RegisterInEngine(recipient, leadingSlashDecoy);

    var mirror = new MirrorMessage();
    mirror.ProcessData(Grobal2.ISM_PLAYER_NOTICE, 0,
        "player-notice-target/exact/body/with/slashes");
    Assert(recipient.m_MsgList.Count == 1,
        "ident 227 did not queue exactly one notice for an online recipient");
    var message = recipient.m_MsgList[0];
    Assert(message.wIdent == Grobal2.RM_SYSMESSAGE
        && message.Buff == "exact/body/with/slashes",
        "ident 227 changed the text after the first slash");
    Assert(message.nParam1 == M2Share.g_Config.btBlueMsgFColor
        && message.nParam2 == M2Share.g_Config.btBlueMsgBColor,
        "ident 227 notice is not Blue/Hint");

    recipient.m_MsgList.Clear();
    mirror.ProcessData(Grobal2.ISM_PLAYER_NOTICE, 0,
        "player-notice-target/");
    Assert(recipient.m_MsgList.Count == 1
        && string.IsNullOrEmpty(recipient.m_MsgList[0].Buff),
        "ident 227 rejected the native empty-text case");

    recipient.m_MsgList.Clear();
    mirror.ProcessData(Grobal2.ISM_PLAYER_NOTICE, 0, string.Empty);
    mirror.ProcessData(Grobal2.ISM_PLAYER_NOTICE, 0,
        "/body-after-leading-slash");
    mirror.ProcessData(Grobal2.ISM_PLAYER_NOTICE, 0,
        "missing-player/ignored");
    Assert(recipient.m_MsgList.Count == 0
        && leadingSlashDecoy.m_MsgList.Count == 0,
        "ident 227 did not preserve empty-body/empty-recipient/offline no-op gates");
    Log("MIRROR 227: first-slash split, slash-preserving/empty text, no GM gate, Blue/Hint and no-op gates verified");
}

void RunGroup(Envirnoment map)
{
    // Three real TPlayObject are registered in the real UserEngine player list so the real
    // GetPlayObject(name) lookup inside ClientCreateGroup/ClientAddGroupMember resolves them. Offline
    // flag keeps SendSocket a no-op (early return) while m_boGhost=false keeps GetPlayObject resolving.
    var leader = NewPlayer("group-leader", map);
    var m1 = NewPlayer("group-m1", map);
    var m2 = NewPlayer("group-m2", map);
    RegisterInEngine(leader, m1, m2);
    m1.m_boAllowGroup = true;
    m2.m_boAllowGroup = true;

    Assert(ReferenceEquals(M2Share.UserEngine.GetPlayObject("group-m1"), m1),
        "real UserEngine.GetPlayObject resolves a registered player by name");

    // ---------------------------------------------------------------------------------
    // 战神 forms a party in TWO steps and this harness has to drive both, because CM 1020
    // /1021 do NOT create anything. Exhaustive E8-callee enumeration of the two bodies:
    //   sub_6C341C (1020) = {405500 4059C0 40C140 652784 6C3380 6C33CC 6F39B4}
    //   sub_6C34EC (1021) = {405500 4059C0 40C140 652784 6B7BAC 6BBE84 6C33CC 6F39B4}
    // Neither set contains sub_726B80 (TGroup.Create), sub_7272EC (insert member) or
    // sub_6C3648 (create-on-accept); their single state change is
    //   6C348A  E8 25 05 03 00   call 0x6F39B4    (1020, target in eax at 6C3488)
    //   6C3572  E8 3D 04 03 00   call 0x6F39B4    (1021, target in eax at 6C3570)
    // which only queues a pending request. Membership materialises exclusively from the
    // CM 4412 accept: sub_6F3EA8 @6F3F21 cmp [edi+0xA80],0 / je -> 6F3F2E call 0x6C3648,
    // and it is sub_6C3648 that allocates and wires the group:
    //   6C36AB  E8 D0 34 06 00   call 0x726B80        ; TGroup.Create(ecx = inviter)
    //   6C36B5  89 B0 80 0A 00 00 mov [eax+0xA80],esi ; inviter -> the new group
    //   6C36BF  E8 28 3C 06 00   call 0x7272EC        ; insert the accepter
    //   72739A  89 98 80 0A 00 00 mov [eax+0xA80],ebx ; accepter -> the same group
    //   6C36E5  66 BA 94 02      mov dx,0x294         ; SM 660 to the INVITER
    // and TGroup.Create pins ownership: 726BA6 mov [ebx+0x3C],edi (owner = inviter) /
    // 726BE2 call 0x728518 (slot 0 := owner). So m_GroupOwner = the inviter for BOTH.
    // ---------------------------------------------------------------------------------
    miCreateGroup.Invoke(leader, new object[] { "group-m1" });
    Assert(leader.m_GroupOwner == null && m1.m_GroupOwner == null
        && leader.m_GroupMembers.Count == 0,
        "real ClientCreateGroup formed a group by itself; 战神 sub_6C341C only queues "
        + "(6C348A call 0x6F39B4) and never calls sub_726B80/sub_7272EC/sub_6C3648");
    Assert(HasPendingRequest(m1, leader, 0),
        "real ClientCreateGroup did not queue a type-0 request ON THE TARGET "
        + "(6C3484 xor ecx,ecx = type 0; 6C3488 mov eax,esi = target receives it)");
    Log("TEAM ClientCreateGroup(leader,m1): queued type-0 invite on m1, no group yet "
        + $"(leader.m_GroupMembers={leader.m_GroupMembers.Count}) — matches sub_6C341C");

    // The accept is the real CM 4412 handler, reached through the real Operate dispatcher.
    m1.Operate(NativeMessage(Grobal2.CM_REPLY_GROUP_MESSAGE, 1, 0, leader.m_sCharName));
    bool createdOk = leader.m_GroupMembers != null && leader.m_GroupMembers.Count == 2
        && leader.m_GroupMembers.Contains(m1)
        && ReferenceEquals(m1.m_GroupOwner, leader)
        && ReferenceEquals(leader.m_GroupOwner, leader);
    Log($"TEAM 4412 accept(m1 -> leader): m_GroupMembers={leader.m_GroupMembers?.Count} "
        + $"leader.m_GroupOwner={(ReferenceEquals(leader.m_GroupOwner, leader) ? "leader" : "?")} "
        + $"m1.m_GroupOwner={(ReferenceEquals(m1.m_GroupOwner, leader) ? "leader" : "?")}");
    Assert(createdOk,
        "the real 1020-invite + 4412-accept chain did not form a 2-member group with "
        + "m1.m_GroupOwner=leader (6F3F2E call 0x6C3648 -> 726BA6 mov [ebx+0x3C],edi)");

    // Same two-step shape for 1021: queue, then accept.
    miAddGroupMember.Invoke(leader, new object[] { "group-m2" });
    Assert(m2.m_GroupOwner == null && leader.m_GroupMembers.Count == 2,
        "real ClientAddGroupMember joined the target without consent; 战神 sub_6C34EC's "
        + "only state change is 6C3572 call 0x6F39B4");
    Assert(HasPendingRequest(m2, leader, 0),
        "real ClientAddGroupMember did not queue a type-0 request on the target "
        + "(6C356C xor ecx,ecx)");

    m2.Operate(NativeMessage(Grobal2.CM_REPLY_GROUP_MESSAGE, 1, 0, leader.m_sCharName));
    bool addedOk = leader.m_GroupMembers.Count == 3 && leader.m_GroupMembers.Contains(m2)
        && ReferenceEquals(m2.m_GroupOwner, leader);
    Log($"TEAM 4412 accept(m2 -> leader): m_GroupMembers={leader.m_GroupMembers.Count} "
        + $"m2.m_GroupOwner={(ReferenceEquals(m2.m_GroupOwner, leader) ? "leader" : "?")} "
        + $"roster=[{string.Join(",", leader.m_GroupMembers.Select(p => p.m_sCharName))}]");
    Assert(addedOk,
        "the real 1021-invite + 4412-accept chain did not add m2 (m_GroupMembers=3, "
        + "m2.m_GroupOwner=leader; native 6F3F3B call 0x6C3838 -> sub_7272EC)");
}

void RunChannel()
{
    // The real shared NativeChannelManager runs CreatePublic (owner needs Level>=35) + Enter, mutating
    // the real in-memory channel membership set; QueryById reads the real snapshot back.
    var mgr = NativeChannelManager.Shared;
    var owner = new NativeChannelActor(90001, "chan-owner", 40, true);
    var member = new NativeChannelActor(90002, "chan-member", 25, true);

    var create = mgr.CreatePublic(owner,
        new NativeChannelCreateRequest("harness-channel", false, 0, 20));
    Assert(create.Code == 0 && create.ChannelId > 0, "real CreatePublic returned success + channel id");
    int chId = create.ChannelId;

    var enterOwner = mgr.Enter(owner, chId, 0);
    var enterMember = mgr.Enter(member, chId, 0);
    Assert(enterOwner.Code == 0, "real Enter(owner) succeeded");
    Assert(enterMember.Code == 0, "real Enter(member) succeeded");

    var q = mgr.QueryById(chId);
    Assert(q.Code == 0 && q.Snapshot != null, "real QueryById returned the channel snapshot");
    bool ownerFlagged = q.Snapshot.Members.Any(m => m.Identity == 90001 && m.IsOwner);
    bool memberPresent = q.Snapshot.Members.Any(m => m.Identity == 90002);
    Log($"CHANNEL CreatePublic id={chId} code={create.Code}; Enter owner={enterOwner.Code} "
        + $"member={enterMember.Code}; snapshot MemberCount={q.Snapshot.MemberCount} "
        + $"owner-flagged={ownerFlagged} member-present={memberPresent} name='{q.Snapshot.Name}'");
    Assert(q.Snapshot.MemberCount == 2 && ownerFlagged && memberPresent,
        "real channel holds 2 members (owner flagged) via real in-memory mutation");
}

void RunRelationSkip()
{
    Log("RELATION SKIP: only NativeRelationMySqlStore exists; INativeRelationStore + NativeRelationService "
        + "are internal with no in-memory implementation, so a REAL relation add mutates only via MySQL "
        + "(forbidden in-process). No genuine relation run claimed — not faked.");
}

void RunCorpsSkip()
{
    Log("CORPS SKIP: NativeCorpsStore is MySQL-backed (OpenConnection for TryLoad + all writes); no "
        + "in-memory corps store exists, so a REAL corps create/join hard-requires the DB store. Not faked.");
}

// ===================== helpers =====================

void PrepareConfig()
{
    // The M2Share static ctor loads its string/config .ini files on first access; write minimal ones
    // (same set the InProcEngineRunCheck template uses) so construction does not throw.
    var baseDir = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
    var share = Path.GetFullPath(Path.Combine(baseDir, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"), "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
}

void BootSingletons()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.MapManager = new MapManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.g_Config.nGroupMembersMax = 10;   // real default; ClientAddGroupMember capacity gate
    M2Share.g_Config.sGroupMsgPreFix = "";     // SendGroupText prefix
}

Envirnoment CreateBlankMap(short w, short h, string name)
{
    var map = new Envirnoment { sMapName = name, sMapDesc = name, m_sMapFileName = name };
    var init = typeof(Envirnoment).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    init.Invoke(map, new object[] { w, h });
    map.Flag = new TMapFlag();
    var mapListField = typeof(MapManager).GetField("m_MapList", BindingFlags.Instance | BindingFlags.NonPublic);
    if (mapListField?.GetValue(M2Share.MapManager) is System.Collections.IDictionary dict && !dict.Contains(name))
        dict.Add(name, map);
    return map;
}

TPlayObject NewPlayer(string name, Envirnoment map)
{
    var p = new TPlayObject
    {
        m_boOffLineFlag = true, m_boGhost = false, m_boDeath = false,
        m_sCharName = name, m_sMapName = map.sMapName, m_PEnvir = map
    };
    p.m_Abil.Level = 30;
    return p;
}

void RegisterInEngine(params TPlayObject[] players)
{
    var listField = typeof(UserEngine).GetField("m_PlayObjectList", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("UserEngine.m_PlayObjectList");
    var list = (System.Collections.IList)listField.GetValue(M2Share.UserEngine);
    foreach (var p in players) list.Add(p);
}

// The 4412-4416 family carries its name 6-bit encoded, unlike the legacy 1019-1022 family
// which passes the raw string in sMsg (dispatch 0x6D907B call 0x405708).
TProcessMessage NativeMessage(int ident, int recog, int param, string name)
{
    return new TProcessMessage
    {
        wIdent = ident,
        nParam1 = recog,
        nParam2 = param,
        Payload = EDcode.EncodeBuffer(HUtil32.GbkEncoding.GetBytes(name))
    };
}

bool HasPendingRequest(TPlayObject recipient, TPlayObject requester, byte type)
{
    var method = typeof(TPlayObject).GetMethod("HasNativeGroupRequest",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TPlayObject.HasNativeGroupRequest");
    return (bool)method.Invoke(recipient, new object[] { requester, type });
}
