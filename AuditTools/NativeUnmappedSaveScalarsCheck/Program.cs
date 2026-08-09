// Audit for the save-record scalar slots that sub_6B0FF0 writes and the shared
// DTO codec (DBSvr/Core/NativeHumanDataCodec.cs) models on neither side.
//
// Native evidence (encoder sub_6B0FF0 / decoder sub_6AFD7C, both re-derived from
// D:/loym2/staging/M2Server_reunpacked_20260803.exe and cross-checked against the
// published RTTI direct-field table):
//
//   rec+0x0C8 <-> obj+0x160  MyPKpoint    Integer   enc 0x6B116B  dec 0x6AFEF3/0x6AFEFC
//   rec+0x0CC <-> obj+0x164  LuckNum      Integer   enc 0x6B1177  dec 0x6AFF05/0x6AFF0E
//   rec+0x0D0 <-> obj+0xAED  MyAttackMode Byte      enc 0x6B1183  dec 0x6AFFE1/0x6AFFEA
//   rec+0x0D4 <-> obj+0x67E  FightZoneDieCount Byte enc 0x6B11AA  dec 0x6B00D7/0x6B00E0
//   rec+0x16E <-> obj+0xB85  PlatLv       Byte      enc 0x6B1388  dec 0x6B056E/0x6B0577
//   rec+0x5E8 <-> obj+0xAF0  JiaYouPoint  Cardinal  enc 0x6B12B2  dec 0x6AFF2B/0x6AFF34
//   rec+0x50C <-> obj+0x18A0 TradeProtect Word      enc 0x6B12B8/0x6B12BF dec 0x6B06D0
//   rec+0x534 <-> obj+0x18A4 YuanBaoAccum Word      enc 0x6B12C6/0x6B12CD dec 0x6B06BC
//   rec+0x537 <-> obj+0x578  DamageShare  Byte      enc 0x6B12ED/0x6B12F3 dec 0x6B07ED/0x6B07F6
//
// The three added 2026-08-09 sit in the same straight-line group as 0x5E8: the
// encoder does 0x6B12B2 (0x5E8) then 0x6B12BF (0x50C) then 0x6B12CD (0x534), all
// unconditional. 0x537 follows at 0x6B12F3, CFG-proven unconditional on every
// entry->ret path.
//
// The 0x50C/0x534 pair is the 元宝 trade family: 0x18A0 is named by the setter's
// own literals (0x6D1581 -> 0x6D1718 and 0x6D15A9 -> 0x6D173C), and 0x18A4 is its
// accumulator with a 0x1F4 = 500 cap whose overflow RESETS TO ZERO rather than
// clamping (0x633D7C jbe / 0x633D87 mov word 0, twinned at 0x6F1652/0x6F165D).
// 0x537 is 伤害分担, set by GM dispatch index 359 (0x628036) and consumed at
// 0x73DEB1 with an explicit zero-extend, so it is unsigned.
//
// The load half exists because the codec never DECODES these slots either: the DTO
// member arrives 0 and UsrEngn.GetHumData would zero the live field on every login.
using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

const int RecordSize = 0xEEF8;
const int PkPointOffset = 0x00C8;      // enc 0x6B116B
const int LuckNumOffset = 0x00CC;      // enc 0x6B1177
const int AttackModeOffset = 0x00D0;   // enc 0x6B1183
const int FightZoneDieCountOffset = 0x00D4; // enc 0x6B11AA
const int PlatLvOffset = 0x016E;       // enc 0x6B1388
const int JiaYouPointOffset = 0x05E8;  // enc 0x6B12B2
const int TradeProtectOffset = 0x050C; // enc 0x6B12BF
const int YuanBaoAccumOffset = 0x0534; // enc 0x6B12CD
const int DamageShareOffset = 0x0537;  // enc 0x6B12F3
const int YuanBaoAccumCap = 0x01F4;    // 0x633D7C / 0x6F1652

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();

CheckOffsetConstants();
CheckLoadThroughGetHumData();
CheckLoadMutationDetects();
CheckSaveWritesLiveValues();
CheckSaveMutationDetects();
CheckAttackModeIsRawLikeNative();
CheckShortRecordIsFailSafe();
CheckNewTrioRoundTrips();
CheckYuanBaoAccumulatorResetsNotClamps();
CheckNewTrioWidthsAndNeighbours();

Console.WriteLine(
    "NativeUnmappedSaveScalarsCheck PASS slots=9 " +
    "pk=0x0C8 luck=0x0CC atkmode=0x0D0 fzdie=0x0D4 platlv=0x16E jiayou=0x5E8 " +
    "tradeprotect=0x50C yuanbaoaccum=0x534(cap0x1F4=RESET-not-clamp) " +
    "dmgshare=0x537(unsigned) " +
    "enc=6B116B/6B1177/6B1183/6B11AA/6B1388/6B12B2/6B12BF/6B12CD/6B12F3 " +
    "dec=6AFEFC/6AFF0E/6AFFEA/6B00E0/6B0577/6AFF34/6B06D0/6B06BC/6B07ED");

// The constants the production code uses must equal the native EAs above.
static void CheckOffsetConstants()
{
    Equal(PkPointOffset, ConstOf("NativePkPointOffset"),
        "rec+0x0C8 constant (enc 0x6B116B mov [esi+0xC8],eax <- [ebx+0x160])");
    Equal(LuckNumOffset, ConstOf("NativeLuckNumOffset"),
        "rec+0x0CC constant (enc 0x6B1177 <- [ebx+0x164] LuckNum)");
    Equal(AttackModeOffset, ConstOf("NativeAttackModeOffset"),
        "rec+0x0D0 constant (enc 0x6B1183 <- [ebx+0xAED] MyAttackMode)");
    Equal(FightZoneDieCountOffset, ConstOf("NativeFightZoneDieCountOffset"),
        "rec+0x0D4 constant (enc 0x6B11AA <- [ebx+0x67E] FightZoneDieCount)");
    Equal(PlatLvOffset, ConstOf("NativePlatLvOffset"),
        "rec+0x16E constant (enc 0x6B1388 <- [ebx+0xB85] PlatLv)");
    Equal(JiaYouPointOffset, ConstOf("NativeJiaYouPointOffset"),
        "rec+0x5E8 constant (enc 0x6B12B2 <- [ebx+0xAF0] JiaYouPoint)");
}

// Full load path: a native record with nonzero values must reach the live object
// even though the DTO members are all zero (the codec models none of them).
static void CheckLoadThroughGetHumData()
{
    var raw = new byte[RecordSize];
    BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(PkPointOffset, 4), 1234);
    BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(LuckNumOffset, 4), -7);
    raw[AttackModeOffset] = 3;
    raw[FightZoneDieCountOffset] = 2;
    raw[PlatLvOffset] = 9;
    BinaryPrimitives.WriteUInt32LittleEndian(
        raw.AsSpan(JiaYouPointOffset, 4), 4000000000u);

    var player = LoadInto(raw);

    Equal(1234, player.m_nPkPoint, "PK point restored from rec+0x0C8");
    Equal(-7, player.m_nBodyLuckLevel, "LuckNum restored from rec+0x0CC (signed)");
    Equal((byte)3, player.m_btAttatckMode, "attack mode restored from rec+0x0D0");
    Equal(2, player.m_nFightZoneDieCount,
        "FightZoneDieCount restored from rec+0x0D4");
    Equal((byte)9, player.m_btPlatLv, "PlatLv restored from rec+0x16E");
    Equal(4000000000u, player.m_dwJiaYouPoint,
        "JiaYouPoint restored from rec+0x5E8 (Cardinal, unsigned)");
}

// MUTATION: reading from a neighbouring offset must FAIL the load assertions.
// This is what makes the offsets load-bearing rather than decorative.
static void CheckLoadMutationDetects()
{
    foreach (var (offset, label) in new[]
             {
                 (PkPointOffset, "rec+0x0C8 PK point"),
                 (LuckNumOffset, "rec+0x0CC LuckNum"),
                 (JiaYouPointOffset, "rec+0x5E8 JiaYouPoint"),
             })
    {
        var raw = new byte[RecordSize];
        // Put the value one slot AWAY from where native writes it. If the
        // production code read the wrong offset it would find this and pass.
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(offset + 4, 4), 999);
        var player = LoadInto(raw);
        var actual = offset switch
        {
            PkPointOffset => player.m_nPkPoint,
            LuckNumOffset => player.m_nBodyLuckLevel,
            _ => (int)player.m_dwJiaYouPoint
        };
        Equal(0, actual, label + " must not read its neighbour (+4)");
    }

    foreach (var (offset, label) in new[]
             {
                 (AttackModeOffset, "rec+0x0D0 attack mode"),
                 (FightZoneDieCountOffset, "rec+0x0D4 FightZoneDieCount"),
                 (PlatLvOffset, "rec+0x16E PlatLv"),
             })
    {
        var raw = new byte[RecordSize];
        raw[offset + 1] = 0x5A;
        var player = LoadInto(raw);
        var actual = offset switch
        {
            AttackModeOffset => player.m_btAttatckMode,
            FightZoneDieCountOffset => (byte)player.m_nFightZoneDieCount,
            _ => player.m_btPlatLv
        };
        Equal((byte)0, actual, label + " must not read its neighbour (+1)");
    }
}

// Save path: native rebuilds the frame from the LIVE object, so an in-RAM change
// after login must reach the record, not the stale loaded byte.
static void CheckSaveWritesLiveValues()
{
    var raw = new byte[RecordSize];
    BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(PkPointOffset, 4), 11);
    raw[AttackModeOffset] = 1;

    var player = NewPlayer();
    player.m_NativeHumanData = raw;
    player.m_nPkPoint = 4321;
    player.m_nBodyLuckLevel = -10;
    player.m_btAttatckMode = 5;
    player.m_nFightZoneDieCount = 3;
    player.m_btPlatLv = 200;
    player.m_dwJiaYouPoint = 123456789u;

    Assert(Persist(player), "persist must succeed on a full-size record");

    Equal(4321, BinaryPrimitives.ReadInt32LittleEndian(
        raw.AsSpan(PkPointOffset, 4)), "live PK point reaches rec+0x0C8");
    Equal(-10, BinaryPrimitives.ReadInt32LittleEndian(
        raw.AsSpan(LuckNumOffset, 4)), "live LuckNum reaches rec+0x0CC");
    Equal((byte)5, raw[AttackModeOffset], "live attack mode reaches rec+0x0D0");
    Equal((byte)3, raw[FightZoneDieCountOffset],
        "live FightZoneDieCount reaches rec+0x0D4");
    Equal((byte)200, raw[PlatLvOffset], "live PlatLv reaches rec+0x16E");
    Equal(123456789u, BinaryPrimitives.ReadUInt32LittleEndian(
        raw.AsSpan(JiaYouPointOffset, 4)), "live JiaYouPoint reaches rec+0x5E8");
}

// MUTATION: the bytes native does NOT assign from these fields must stay
// untouched, so a wrong-offset write is detectable as collateral damage.
static void CheckSaveMutationDetects()
{
    var raw = new byte[RecordSize];
    var player = NewPlayer();
    player.m_NativeHumanData = raw;
    player.m_nPkPoint = unchecked((int)0xAABBCCDD);
    player.m_nBodyLuckLevel = 0x11223344;
    player.m_btAttatckMode = 0x42;
    player.m_nFightZoneDieCount = 0x44;
    player.m_btPlatLv = 0x43;
    player.m_dwJiaYouPoint = 0x55667788u;
    Assert(Persist(player), "persist must succeed");

    // rec+0x3B / 0x3E are the hair/version bytes owned by the DBSvr session; and
    // 0xD1..0xD3 are IncHealth/IncSpell/IncHealing, which this module must not own.
    foreach (var untouched in new[] { 0x3B, 0x3E, 0xC4, 0xD1, 0xD2, 0xD3,
                                      0x16D, 0x172, 0x5E4, 0x5EC })
        Equal((byte)0, raw[untouched],
            $"rec+0x{untouched:X} must not be written by this module");

    // and the exact widths: one byte past each byte-field stays zero
    // rec+0x0D0..0x0D4 are five adjacent BYTE slots; 0xD1..0xD3 belong to
    // IncHealth/IncSpell/IncHealing (BLOCKED, not owned here) and must stay 0,
    // which the loop above already asserts. Width proof for the two we do own:
    Equal((byte)0x42, raw[AttackModeOffset],
        "rec+0x0D0 is a BYTE (enc 0x6B1183 mov byte ptr)");
    Equal((byte)0x44, raw[FightZoneDieCountOffset],
        "rec+0x0D4 is a BYTE (enc 0x6B11AA mov byte ptr)");
    Equal((byte)0, raw[PlatLvOffset + 1],
        "rec+0x16E is a BYTE (enc 0x6B1388 mov byte ptr) - must not spill");
}

// Native's decoder copies rec+0x0D0 RAW (0x6AFFE1 -> 0x6AFFEA) with no range
// guard; the 0..5 guard lives only in the setter sub_6F2D10 (0x6F2D19 sub al,6 /
// jae) and the GM cycle handler (0x6239FD cmp [eax+0xAED],5).
static void CheckAttackModeIsRawLikeNative()
{
    var raw = new byte[RecordSize];
    raw[AttackModeOffset] = 200;
    var player = LoadInto(raw);
    Equal((byte)200, player.m_btAttatckMode,
        "attack mode load is raw like sub_6AFD7C (no invented clamp)");
}

// Fail-safe: a truncated record must not throw and must not claim success while
// silently dropping a nonzero value.
static void CheckShortRecordIsFailSafe()
{
    var player = NewPlayer();
    player.m_NativeHumanData = new byte[0x100];
    player.m_nPkPoint = 5;
    Assert(!Persist(player), "short record with a nonzero field must report failure");

    var clean = NewPlayer();
    clean.m_NativeHumanData = new byte[0x100];
    Assert(Persist(clean), "short record with all-zero fields is vacuously fine");

    var nullRecord = NewPlayer();
    nullRecord.m_NativeHumanData = null;
    nullRecord.m_nPkPoint = 0;
    Assert(Persist(nullRecord), "null record must not throw");
    Restore(nullRecord);
    Equal(0, nullRecord.m_nPkPoint, "null record restore must not throw");
}

// ---- the three slots added 2026-08-09 ----

static void CheckNewTrioRoundTrips()
{
    Equal(TradeProtectOffset, ConstOf("NativeTradeProtectAmountOffset"),
        "trade-protect offset must be 0x050C (enc 0x6B12BF / dec 0x6B06D0)");
    Equal(YuanBaoAccumOffset, ConstOf("NativeYuanBaoTradeAccumOffset"),
        "yuanbao accumulator offset must be 0x0534 (enc 0x6B12CD / dec 0x6B06BC)");
    Equal(DamageShareOffset, ConstOf("NativeDamageShareOffset"),
        "damage-share offset must be 0x0537 (enc 0x6B12F3 / dec 0x6B07ED)");

    // LOAD: both codec halves are raw copies with no clamp on either side, so an
    // over-cap record value must survive the trip unchanged. The 0x1F4 cap lives
    // only in the accumulate paths, never in sub_6AFD7C.
    var raw = new byte[RecordSize];
    BinaryPrimitives.WriteUInt16LittleEndian(
        raw.AsSpan(TradeProtectOffset, 2), 4321);
    BinaryPrimitives.WriteUInt16LittleEndian(
        raw.AsSpan(YuanBaoAccumOffset, 2), 60000);   // far over the 500 cap
    raw[DamageShareOffset] = 0xFE;

    var loaded = LoadInto(raw);
    Equal((ushort)4321, loaded.m_nNativeTradeProtectAmount,
        "trade-protect restored from rec+0x050C as a word");
    Equal((ushort)60000, loaded.m_nNativeYuanBaoTradeAccum,
        "an over-cap accumulator value must load UNCHANGED -- the 0x1F4 cap is in "
        + "the accumulate paths (0x633D7C / 0x6F1652), not in the codec");
    Equal((byte)0xFE, loaded.m_btNativeDamageShare,
        "damage share restored from rec+0x0537 as an UNSIGNED byte (0x73DEB1 "
        + "zero-extends it before use, so 0xFE is 254 not -2)");

    // SAVE: live values, unconditional.
    var player = NewPlayer();
    player.m_NativeHumanData = new byte[RecordSize];
    player.m_nNativeTradeProtectAmount = 777;
    player.m_nNativeYuanBaoTradeAccum = 499;
    player.m_btNativeDamageShare = 0x2A;
    Assert(Persist(player), "persist must succeed on a full-length record");
    Equal((ushort)777, BinaryPrimitives.ReadUInt16LittleEndian(
            player.m_NativeHumanData.AsSpan(TradeProtectOffset, 2)),
        "save must write the LIVE trade-protect value");
    Equal((ushort)499, BinaryPrimitives.ReadUInt16LittleEndian(
            player.m_NativeHumanData.AsSpan(YuanBaoAccumOffset, 2)),
        "save must write the LIVE accumulator value");
    Equal((byte)0x2A, player.m_NativeHumanData[DamageShareOffset],
        "save must write the LIVE damage-share byte");
}

// THE trap in this family: exceeding the cap RESETS TO ZERO. A clamp would let a
// capped account keep accumulating, which is the opposite of native.
static void CheckYuanBaoAccumulatorResetsNotClamps()
{
    Equal(YuanBaoAccumCap, 0x01F4,
        "the cap constant is 0x1F4 = 500 (0x633D7C cmp word ...,0x1F4)");

    // at the cap: `jbe` keeps it, so exactly-500 survives
    var atCap = NewPlayer();
    atCap.m_nNativeYuanBaoTradeAccum = 400;
    atCap.AccumulateNativeYuanBaoTrade(100);
    Equal((ushort)500, atCap.m_nNativeYuanBaoTradeAccum,
        "exactly at the cap must SURVIVE -- 0x633D85 is `jbe`, so 500 is kept");

    // one over: reset to zero, NOT clamped to 500
    var over = NewPlayer();
    over.m_nNativeYuanBaoTradeAccum = 400;
    over.AccumulateNativeYuanBaoTrade(101);
    Equal((ushort)0, over.m_nNativeYuanBaoTradeAccum,
        "one over the cap must RESET TO ZERO (0x633D87 mov word 0), not clamp to "
        + "500 -- clamping would let a capped account keep accumulating");

    // far over: still zero
    var farOver = NewPlayer();
    farOver.m_nNativeYuanBaoTradeAccum = 500;
    farOver.AccumulateNativeYuanBaoTrade(9000);
    Equal((ushort)0, farOver.m_nNativeYuanBaoTradeAccum,
        "a large overshoot also resets to zero");

    // the add itself is a 16-bit `add word`, so it wraps BEFORE the compare
    var wrap = NewPlayer();
    wrap.m_nNativeYuanBaoTradeAccum = 65535;
    wrap.AccumulateNativeYuanBaoTrade(1);
    Equal((ushort)0, wrap.m_nNativeYuanBaoTradeAccum,
        "65535 + 1 wraps to 0 in the `add word`, and 0 is at-or-under the cap so "
        + "it is kept -- not treated as an overflow reset");

    var wrapLow = NewPlayer();
    wrapLow.m_nNativeYuanBaoTradeAccum = 65535;
    wrapLow.AccumulateNativeYuanBaoTrade(11);
    Equal((ushort)10, wrapLow.m_nNativeYuanBaoTradeAccum,
        "65535 + 11 wraps to 10, under the cap, so it is kept");
}

static void CheckNewTrioWidthsAndNeighbours()
{
    // 0x534 and 0x537 are 3 bytes apart with 0x536 unreferenced, so a word write
    // at 0x534 must NOT reach 0x537, and the byte at 0x537 must not disturb 0x536.
    // Pre-fill the neighbours with a sentinel so a too-WIDE write is detected by
    // the sentinel being ERASED. Writing 0xFFFF into a zero record and then
    // checking the neighbour is 0 proves nothing: a dword write of 0xFFFF leaves
    // its high bytes zero anyway, so the assertion could never fire.
    const byte Sentinel = 0x5A;

    var player = NewPlayer();
    player.m_NativeHumanData = new byte[RecordSize];
    player.m_NativeHumanData[YuanBaoAccumOffset + 2] = Sentinel;
    player.m_NativeHumanData[DamageShareOffset] = Sentinel;
    player.m_nNativeYuanBaoTradeAccum = 0xFFFF;
    player.m_btNativeDamageShare = 0x11;
    Assert(Persist(player), "persist must succeed");
    Equal(Sentinel, player.m_NativeHumanData[YuanBaoAccumOffset + 2],
        "the accumulator is a WORD -- a wider write would erase the sentinel at "
        + "rec+0x0536");
    Equal((byte)0x11, player.m_NativeHumanData[DamageShareOffset],
        "rec+0x0537 must hold the damage-share byte, not accumulator spill");

    // and the trade-protect word must not reach into 0x50E (a different field:
    // the known storage-space count)
    var p2 = NewPlayer();
    p2.m_NativeHumanData = new byte[RecordSize];
    p2.m_NativeHumanData[TradeProtectOffset + 2] = Sentinel;
    p2.m_NativeHumanData[TradeProtectOffset + 3] = Sentinel;
    p2.m_nNativeTradeProtectAmount = 0xFFFF;
    Assert(Persist(p2), "persist must succeed");
    Equal(Sentinel, p2.m_NativeHumanData[TradeProtectOffset + 2],
        "trade-protect is a WORD -- a wider write would erase the sentinel at "
        + "rec+0x050E, which is a different field (storage-space count)");
    Equal(Sentinel, p2.m_NativeHumanData[TradeProtectOffset + 3],
        "nor may it reach rec+0x050F");
}

static TPlayObject LoadInto(byte[] raw)
{
    var human = new THumDataInfo { NativeData = raw };
    human.Data.Initialization();
    human.Header = new TRecordHeader
    {
        dCreateDate = new DateTime(2020, 1, 1).ToOADate()
    };
    var player = NewPlayer();
    var getHumData = typeof(UserEngine).GetMethod("GetHumData",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(getHumData != null, "GetHumData not found");
    getHumData!.Invoke(M2Share.UserEngine, new object[] { player, human });
    return player;
}

static bool Persist(TPlayObject player) =>
    (bool)Invoke(player, "PersistNativeUnmappedScalars");

static void Restore(TPlayObject player) =>
    Invoke(player, "RestoreNativeUnmappedScalars");

static object Invoke(TPlayObject player, string name)
{
    var method = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, name + " not found");
    return method!.Invoke(player, Array.Empty<object>());
}

static int ConstOf(string name)
{
    var field = typeof(TPlayObject).GetField(name,
        BindingFlags.Static | BindingFlags.NonPublic);
    Assert(field != null, name + " constant not found");
    return (int)field!.GetRawConstantValue();
}

static TPlayObject NewPlayer() => new();

static void PrepareRuntimeConfig()
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

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}
