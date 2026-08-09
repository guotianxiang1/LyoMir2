// LoginRelationFlagHealCheck — pins 战神's login relation-flag self-healing.
//
// 战神 login reconciliation is sub_6CCE40.  It is reached from the ONE-SHOT logon
// routine sub_6B1D64 (TPlayer VMT+0x78; _vmt_slot 0x6AC940 -> base 0x6AC8C8
// class=TPlayer instSize=6472) at 0x6B21CF.  sub_6B1AA0 dispatches that slot once,
// behind the obj+0xD2C latch:
//   0x6B1B81 cmp byte [eax+0xD2C],0 ; 0x6B1B94 mov byte [eax+0xD2C],1
//   0x6B1B9E mov edx,[eax] ; 0x6B1BA0 call dword [edx+0x78]
//
// Legs A and B repair a flag whose social-block name slot is empty:
//   0x6CCE67  cmp byte [ebx+0xB94], 0   ; boMarried set?           (rec 0x0DB)
//   0x6CCE70  cmp byte [ebx+0xC48], 0   ; spouse slot empty?       (rec 0x650)
//   0x6CCE77  je  0x6CCE8A
//   0x6CCE8A  mov byte [ebx+0xB94], 0   ; CLEAR boMarried
//   0x6CCE91  cmp byte [ebx+0xB95], 0   ; boStudent set?           (rec 0x0DC,
//                                       ;   RTTI 006AD20F IsAStudent FF000B95)
//   0x6CCE9A  cmp byte [ebx+0xC58], 0   ; master slot empty?       (rec 0x660)
//   0x6CCEA1  je  0x6CCED3
//   0x6CCED3  mov byte [ebx+0xB95], 0   ; CLEAR boStudent
//
// Each leg is a SINGLE store.  Native emits no message, leaves the name slot
// alone, does not touch the 0xBF4 counter and does not save.  Emptiness is decided
// by the slot's LENGTH BYTE, not by a parsed name — which is why the C# heal reads
// m_NativeHumanData directly (see assertion HealIgnoresDerivedName below).
using System.Reflection;
using System.Text;
using GameSvr;

// Social-block slot offsets in the INFLATED record.  obj+0xC48 == 0x650 (block
// base), slots are 16 bytes each: +0x00 spouse, +0x10 master, +0x20 companion,
// +0x30..+0x70 students[0..4].
const int SpouseSlot = 0x650;
const int MasterSlot = 0x660;
const int CompanionSlot = 0x670;
// 战神 record size; NativeHumanDataCodec.DataRecordSize is ambiguous here because
// the type exists in both DBSvr and GameSvr, so the literal is used instead.  The
// value is pinned by RecordSizeMatchesCodec() below.
const int DataRecordSize = 0xEEF8;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
RecordSizeMatchesCodec();

HealClearsMarriedWhenSpouseSlotEmpty();
HealClearsStudentWhenMasterSlotEmpty();
HealKeepsMarriedWhenSlotHasOversizeLengthByte();
HealKeepsStudentWhenMasterSlotPopulated();
HealDoesNotTouchFlagsThatAreAlreadyFalse();
HealNeverRepairsTheReverseInconsistency();
HealDoesNothingWithoutARawRecord();
HealTouchesNothingElse();
HealIgnoresDerivedName();
HealRunsBeforeRelinkLegs();

// ---- leg C (student-array reconciliation, 0x6CCEDA..0x6CD01F) ----
LegCSkippedBelowShouTuLevel();
LegCSkippedWhenStoredCountIsZero();
LegCRepairsCountToLiveSlots();
LegCCountsOversizeLengthByteAsOccupied();
LegCLeavesCountAloneWhenAlreadyCorrect();
LegCBumpsApprenticeNumOnlyOnRepair();

Console.WriteLine(
    "PASS LoginRelationFlagHeal boMarried@0x6CCE8A boStudent@0x6CCED3 " +
    "empty=length-byte(0x6CCE70/0x6CCE9A) no-reverse-heal single-store " +
    "ordered-before-CheckMarry/CheckMaster " +
    "legC=count-fixup@0x6CD013+ApprenticeNum@0x6CCF63/0x6CD019");
return;

// ------------------------------------------------------------------------ leg C
//
// Outer gate, both required (0x6CCEDA..0x6CCEF6):
//   0x6CCEDA movzx eax,word [ebx+0x278]   ; Level
//   0x6CCEE1 mov edx,[0x7D6468]           ; SETKEY_SHOUTU (key ASCII @0x79A704)
//   0x6CCEE7 cmp eax,[edx] / 0x6CCEE9 jl 0x6CD01F
//   0x6CCEEF cmp byte [ebx+0xB97],0 / 0x6CCEF6 jbe 0x6CD01F
// Count fixup (0x6CD003..0x6CD019):
//   0x6CD00B cmp storedCount,liveCount / 0x6CD00E je -> no change
//   0x6CD013 mov byte [ebx+0xB97],liveCount ; 0x6CD019 inc dword [ebx+0xBF4]

// 0x6CCEE9: below SETKEY_SHOUTU -> the whole loop AND the fixup are skipped, so a
// wrong stored count is deliberately left wrong.
void LegCSkippedBelowShouTuLevel()
{
    var player = NewPlayer();
    M2Share.g_Config.nMinMasterLevel = 35;
    player.m_Abil.Level = 34;
    player.m_nStudentCount = 3;          // wrong on purpose: 0 slots are occupied
    Heal(player);
    Equal(3, player.m_nStudentCount,
        "0x6CCEE9: below SETKEY_SHOUTU must skip the leg C fixup entirely");
}

// 0x6CCEF6 is `jbe`, so only a stored count of 0 skips.
void LegCSkippedWhenStoredCountIsZero()
{
    var player = NewPlayer();
    M2Share.g_Config.nMinMasterLevel = 35;
    player.m_Abil.Level = 40;
    player.m_nStudentCount = 0;
    SetSlot(player, StudentSlot(0), "Alice");   // occupied but count says 0
    Heal(player);
    Equal(0, player.m_nStudentCount,
        "0x6CCEF6: stored count 0 skips leg C even with an occupied slot");
}

// The core heal: storedCount is repaired down to the number of non-empty slots.
void LegCRepairsCountToLiveSlots()
{
    var player = NewPlayer();
    M2Share.g_Config.nMinMasterLevel = 35;
    M2Share.g_Config.nMasterOKLevel = 35;
    player.m_Abil.Level = 40;
    player.m_nStudentCount = 4;
    SetSlot(player, StudentSlot(0), "Alice");
    SetSlot(player, StudentSlot(2), "Bob");
    Heal(player);
    Equal(2, player.m_nStudentCount,
        "0x6CD013: student count must be repaired to the live slot count");
}

// 0x6CCF07 tests the LENGTH BYTE only. A slot whose length byte is > 15 (the
// foreign-overflow state that occurs in real records) is NON-empty to native, so
// it must still be counted — a name-driven implementation would drop it.
void LegCCountsOversizeLengthByteAsOccupied()
{
    var player = NewPlayer();
    M2Share.g_Config.nMinMasterLevel = 35;
    M2Share.g_Config.nMasterOKLevel = 35;
    player.m_Abil.Level = 40;
    player.m_nStudentCount = 2;
    SetSlot(player, StudentSlot(0), "Alice");
    player.m_NativeHumanData[StudentSlot(1)] = 0x3A;   // ':' as a length byte
    Heal(player);
    Equal(2, player.m_nStudentCount,
        "0x6CCF07: a length byte > 15 is OCCUPIED, not empty");
}

// 0x6CD00E: when the count already matches, native takes the je and does NOT
// touch the counter.
void LegCLeavesCountAloneWhenAlreadyCorrect()
{
    var player = NewPlayer();
    M2Share.g_Config.nMinMasterLevel = 35;
    M2Share.g_Config.nMasterOKLevel = 35;
    player.m_Abil.Level = 40;
    player.m_nStudentCount = 1;
    SetSlot(player, StudentSlot(0), "Alice");
    var before = ReadApprenticeNum(player);
    Heal(player);
    Equal(1, player.m_nStudentCount, "count already correct stays put");
    Equal(before, ReadApprenticeNum(player),
        "0x6CD00E: no repair means no 0xBF4 increment");
}

// 0x6CD019 increments the persisted ApprenticeNum (rec 0x174) on repair only.
void LegCBumpsApprenticeNumOnlyOnRepair()
{
    var player = NewPlayer();
    M2Share.g_Config.nMinMasterLevel = 35;
    M2Share.g_Config.nMasterOKLevel = 35;
    player.m_Abil.Level = 40;
    player.m_nStudentCount = 3;
    SetSlot(player, StudentSlot(0), "Alice");
    WriteApprenticeNum(player, 7);
    Heal(player);
    Equal(1, player.m_nStudentCount, "repaired to 1 live slot");
    Equal(8, ReadApprenticeNum(player),
        "0x6CD019: ApprenticeNum at rec 0x174 must be incremented on repair");
}

int StudentSlot(int index) => 0x680 + index * 0x10;

int ReadApprenticeNum(TPlayObject player) =>
    System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
        player.m_NativeHumanData.AsSpan(0x0174, 4));

void WriteApprenticeNum(TPlayObject player, int value) =>
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
        player.m_NativeHumanData.AsSpan(0x0174, 4), value);

// ---------------------------------------------------------------- leg A / leg B

// 0x6CCE67 gate true + 0x6CCE70 slot empty -> 0x6CCE8A clears the flag.
void HealClearsMarriedWhenSpouseSlotEmpty()
{
    var player = NewPlayer();
    player.m_boMarried = true;
    SetSlot(player, SpouseSlot, string.Empty);
    // Isolation: keep the student leg's gate (0x6CCE91) false so only leg A fires.
    player.m_boStudent = false;

    Heal(player);
    Assert(!player.m_boMarried,
        "0x6CCE8A: boMarried must be cleared when the 0x650 spouse slot is empty");
}

// 0x6CCE91 gate true + 0x6CCE9A slot empty -> 0x6CCED3 clears the flag.
void HealClearsStudentWhenMasterSlotEmpty()
{
    var player = NewPlayer();
    player.m_boStudent = true;
    SetSlot(player, MasterSlot, string.Empty);
    player.m_boMarried = false;

    Heal(player);
    Assert(!player.m_boStudent,
        "0x6CCED3: boStudent must be cleared when the 0x660 master slot is empty");
}

// THE R1 CATCHER.  Native's test is `cmp byte [ebx+0xC48],0` — a length byte of
// 0x3A is NON-empty, so the flag survives.  The codec's ReadBlockName
// (NativeHumanDataCodec.cs, `if (length > DearNameCapacity) return string.Empty`)
// yields "" for exactly this slot, and in 30/30 golden records the external
// ':'/'$' companion string really does put 0x3A (= 58) in a length position.  An
// implementation healing off the derived name wipes a valid marriage here.
void HealKeepsMarriedWhenSlotHasOversizeLengthByte()
{
    var player = NewPlayer();
    player.m_boMarried = true;
    player.m_NativeHumanData[SpouseSlot] = 0x3A;   // 58 > 15 => ReadBlockName -> ""
    player.m_boStudent = false;

    Heal(player);
    Assert(player.m_boMarried,
        "0x6CCE70 tests the LENGTH BYTE: a 0x3A slot is non-empty to 战神, so " +
        "boMarried must survive (healing off the parsed name would wipe it)");
}

// 0x6CCEA1 not taken -> falls through to the level compare / announce, never to
// the clear at 0x6CCED3.
void HealKeepsStudentWhenMasterSlotPopulated()
{
    var player = NewPlayer();
    player.m_boStudent = true;
    SetSlot(player, MasterSlot, "ShiFu");

    Heal(player);
    Assert(player.m_boStudent,
        "0x6CCEA1: a populated 0x660 master slot must NOT clear boStudent");
}

// 0x6CCE6E / 0x6CCE98 skip each leg entirely when the flag is already false.
void HealDoesNotTouchFlagsThatAreAlreadyFalse()
{
    var player = NewPlayer();
    player.m_boMarried = false;
    player.m_boStudent = false;
    SetSlot(player, SpouseSlot, string.Empty);
    SetSlot(player, MasterSlot, string.Empty);

    Heal(player);
    Assert(!player.m_boMarried && !player.m_boStudent,
        "0x6CCE6E/0x6CCE98: already-false flags stay false");
}

// §5 of the RE: NOTHING in the binary clears a NAME slot because a flag is false,
// and graduation mode 1 deliberately CREATES this state (0x6C6099/0x6C609D skip
// the 0x6C60B4 slot clear that only mode 0 performs).  So the heal is strictly
// one-directional: flag follows name, never the reverse.
void HealNeverRepairsTheReverseInconsistency()
{
    var player = NewPlayer();
    player.m_boMarried = false;
    player.m_boStudent = false;
    SetSlot(player, SpouseSlot, "PeiOu");
    SetSlot(player, MasterSlot, "ShiFu");
    player.m_sDearName = "PeiOu";
    player.m_sMasterName = "ShiFu";

    Heal(player);
    Equal("PeiOu", ReadSlot(player, SpouseSlot),
        "no reverse heal: the 0x650 spouse slot must survive a false boMarried");
    Equal("ShiFu", ReadSlot(player, MasterSlot),
        "no reverse heal: the 0x660 master slot must survive a false boStudent");
    Equal("PeiOu", player.m_sDearName, "no reverse heal: m_sDearName untouched");
    Equal("ShiFu", player.m_sMasterName, "no reverse heal: m_sMasterName untouched");
}

// Fail-safe: with no raw record there is no evidence a slot is empty, so nothing
// may be cleared.  Native always has the record (sub_6AFD7C applied it before the
// object was ever ticked), so this case is C#-only defensive behaviour and must
// never destroy persisted state.
void HealDoesNothingWithoutARawRecord()
{
    var player = NewPlayer();
    player.m_NativeHumanData = null;
    player.m_boMarried = true;
    player.m_boStudent = true;

    Heal(player);
    Assert(player.m_boMarried && player.m_boStudent,
        "absent raw record must not clear either flag");
}

// Both legs are a single `mov byte [ebx+0xNNN],0`.  Native does not clear the
// name slot, does not increment obj+0xBF4 (the 0x6CCF63/0x6CD019 counter belongs
// to the student-array leg only), and does not refresh the name plate.
void HealTouchesNothingElse()
{
    var player = NewPlayer();
    player.m_boMarried = true;
    player.m_boStudent = true;
    SetSlot(player, SpouseSlot, string.Empty);
    SetSlot(player, MasterSlot, string.Empty);
    player.m_sDearName = "Stale";
    player.m_sMasterName = "Stale";
    player.m_nStudentCount = 3;
    var creditBefore = player.m_btCreditPoint;
    var bonusBefore = player.m_nBonusPoint;
    var companionBefore = ReadSlot(player, CompanionSlot);

    Heal(player);
    Equal("Stale", player.m_sDearName,
        "leg A is one store: m_sDearName must NOT be cleared");
    Equal("Stale", player.m_sMasterName,
        "leg B is one store: m_sMasterName must NOT be cleared");
    Equal(3, player.m_nStudentCount,
        "legs A/B must not touch the student count (that is the 0x6CD013 leg)");
    Equal(creditBefore, player.m_btCreditPoint,
        "no credit award: 0x6CCE8A/0x6CCED3 are bare stores");
    Equal(bonusBefore, player.m_nBonusPoint,
        "no bonus award: 0x6CCE8A/0x6CCED3 are bare stores");
    Equal(companionBefore, ReadSlot(player, CompanionSlot),
        "the 0x670 companion slot is foreign data and must be untouched");
}

// The heal must consult the record bytes, not the derived strings.  Populated
// names with EMPTY slots is the state a name-driven implementation would keep and
// a byte-driven one must clear.
void HealIgnoresDerivedName()
{
    var player = NewPlayer();
    player.m_boMarried = true;
    player.m_boStudent = true;
    SetSlot(player, SpouseSlot, string.Empty);
    SetSlot(player, MasterSlot, string.Empty);
    player.m_sDearName = "GhostSpouse";
    player.m_sMasterName = "GhostMaster";

    Heal(player);
    Assert(!player.m_boMarried,
        "heal must key off raw 0x650, not m_sDearName");
    Assert(!player.m_boStudent,
        "heal must key off raw 0x660, not m_sMasterName");

    var root = FindRepositoryRoot();
    var slots = Read(root, "GameSvr", "Players", "TPlayObject.NativeSocialSlots.cs");
    Require(slots, "m_NativeHumanData[recordOffset] == 0",
        "the emptiness test must be the raw length byte");
    Require(slots, "NativeSocialBlockRecordOffset = 0x0650",
        "social block base offset");
    Require(slots, "m_boMarried = false", "leg A store");
    Require(slots, "m_boStudent = false", "leg B store");
    Reject(slots, "ReadBlockName", "heal must not use the tolerant name reader");
    Reject(slots, "IsNullOrEmpty(m_sDearName)", "heal must not test the derived name");
    Reject(slots, "IsNullOrEmpty(m_sMasterName)", "heal must not test the derived name");
}

// Native clears at 0x6CCE8A BEFORE the spouse announce, and at 0x6CCED3 BEFORE
// both the graduation compare (0x6CCEB0) and the master announce (0x6CCECC).  So
// the C# call must precede CheckMarry/CheckMaster.  It also cannot live inside
// CheckMarry, whose `!IsNullOrEmpty(m_sDearName)` guard excludes the healing case.
void HealRunsBeforeRelinkLegs()
{
    var root = FindRepositoryRoot();
    var login = Read(root, "GameSvr", "Players", "TPlayObject.Base.cs");

    var heal = login.IndexOf("HealNativeRelationFlags();", StringComparison.Ordinal);
    Assert(heal >= 0, "the login sequence must call HealNativeRelationFlags()");
    var marry = login.IndexOf("CheckMarry();", StringComparison.Ordinal);
    var master = login.IndexOf("CheckMaster();", StringComparison.Ordinal);
    Assert(marry >= 0 && master >= 0, "login must still call CheckMarry/CheckMaster");
    Assert(heal < marry,
        "0x6CCE8A precedes the spouse announce: the heal must run before CheckMarry");
    Assert(heal < master,
        "0x6CCED3 precedes the master announce: the heal must run before CheckMaster");

    // The raw record is hydrated onto the play object before the login tick, so
    // the byte test is available when the heal runs.
    var loader = Read(root, "GameSvr", "UsrSystem", "UsrEngn.cs");
    Require(loader, "PlayObject.m_NativeHumanData = HumanRcd.NativeData",
        "raw record hydration the heal depends on");
}

// ------------------------------------------------------------------- fixtures

// The literal DataRecordSize above must track the codec constant.  Resolved by
// reflection because the type name is ambiguous between DBSvr and GameSvr.
void RecordSizeMatchesCodec()
{
    var codec = typeof(TPlayObject).Assembly.GetType(
        "DBSvr.Core.NativeHumanDataCodec");
    Assert(codec != null, "DBSvr.Core.NativeHumanDataCodec was not found");
    var field = codec.GetField("DataRecordSize",
        BindingFlags.Public | BindingFlags.Static);
    Assert(field != null, "NativeHumanDataCodec.DataRecordSize was not found");
    Equal(DataRecordSize, (int)field.GetRawConstantValue(),
        "fixture record size must match the codec");
}

TPlayObject NewPlayer()
{
    var player = new TPlayObject();
    player.m_NativeHumanData = new byte[DataRecordSize];
    player.m_sCharName = "TestChar";
    player.m_sDearName = string.Empty;
    player.m_sMasterName = string.Empty;
    // Foreign ':'-style content in the companion slot, as every real record has.
    SetSlot(player, CompanionSlot, ":companion");
    return player;
}

void Heal(TPlayObject player) =>
    Invoke(player, "HealNativeRelationFlags");

void SetSlot(TPlayObject player, int offset, string value)
{
    var bytes = Encoding.GetEncoding(936).GetBytes(value ?? string.Empty);
    if (bytes.Length > 15)
        throw new InvalidOperationException("fixture slot value exceeds 15 bytes");
    player.m_NativeHumanData.AsSpan(offset, 16).Clear();
    player.m_NativeHumanData[offset] = (byte)bytes.Length;
    bytes.CopyTo(player.m_NativeHumanData.AsSpan(offset + 1));
}

string ReadSlot(TPlayObject player, int offset)
{
    var length = player.m_NativeHumanData[offset];
    if (length > 15) return string.Empty;
    return Encoding.GetEncoding(936).GetString(
        player.m_NativeHumanData, offset + 1, length);
}

void Invoke(TPlayObject player, string name)
{
    var method = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert(method != null, "TPlayObject." + name + " is missing");
    try
    {
        method.Invoke(player, null);
    }
    catch (TargetInvocationException ex) when (ex.InnerException != null)
    {
        throw ex.InnerException;
    }
}

// -------------------------------------------------------------------- helpers

void PrepareRuntimeConfig()
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

string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("Repository root was not found.");
}

string Read(string root, params string[] parts) =>
    File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message + " is missing");
}

void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message + " is present");
}

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}
