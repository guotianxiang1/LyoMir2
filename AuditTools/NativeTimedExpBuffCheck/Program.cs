// Audit for the Nx-experience buff family: rec 0x110 / 0x4C6 (倍经验时间) plus the
// two sibling timed buffs at rec 0x118 (真视) and rec 0x120.
//
// Native evidence, 战神 flat image (ImageBase 0x400000):
//
//   SAVE sub_6B0FF0 -- one shared DB-clock "now" then three gated blocks:
//     0x6B12F9  call sub_40F0A4              ; Now()
//     0x6B12FE  fsub qword [ebx+0x780]       ; -> DB clock
//     0x6B1304  fstp qword [ebp-0x10]        ; shared base, reused 3x
//     0x6B1308  mov eax,[ebx+0xbb8] / 0x6B1310 jle   ; gate: skip BOTH slots
//     0x6B1318  fdiv dword [0x6B1760]        ; = 86400.0 (raw 00c0a847)
//     0x6B131E  fadd qword [ebp-0x10]
//     0x6B1321  fstp qword [esi+0x110]       ; rec 0x110, ABSOLUTE deadline
//     0x6B1328  mov ax,[ebx+0xbbc] / 0x6B132F mov [esi+0x4c6],ax
//     0x6B1346/0x6B134F  same shape -> rec 0x118 from obj+0xBD0
//     0x6B1366/0x6B136F  same shape -> rec 0x120 from obj+0xBD4
//
//   LOAD sub_6AFD7C -- establishes the session clock offset FIRST:
//     0x6B026E  call sub_40F0A4 / 0x6B0276 fstp [eax+0x778]   ; login local Now()
//     0x6B0289  fsub qword [eax+0xef40] / 0x6B0292 fstp [eax+0x780]
//   then per buff:
//     0x6B02AE  fld [eax+0x110] / 0x6B02B4 fcomp 0.0 / 0x6B02BD jbe  ; unset
//     0x6B02DD  fsub qword [eax+0xef40]
//     0x6B02EA  fcomp 0.0 / 0x6B02F3 jbe                            ; expired
//     0x6B02F8  fmul 86400.0 / 0x6B02FE call sub_403574 (fistp)
//     0x6B0306  mov [edx+0xbb8],eax
//     0x6B031F cmp eax,2 / 0x6B0327 mov ...,2                       ; clamp lo
//     0x6B0334 cmp ...,0x64 / 0x6B0340 mov ...,0x64                 ; clamp hi
//     0x6B0357 cmp ebx,0x83D600 / 0x6B035D jle                      ; bound
//   ...and BOTH guards jump past the multiplier block: 0x6B02BD jbe 0x6B03B9
//   (the next buff's block) and 0x6B02F3 jbe 0x6B034A (after both clamps), so the
//   clamp is NOT unconditional -- an earlier draft of this audit asserted that it
//   was and thereby locked in the divergence.
//
//   TICK sub_6CCBC4 (single caller 0x6B3B37):
//     0x6CCBDE call sub_408340 / 0x6CCBE5 sub esi,[ebx+0x720]
//     0x6CCBEB cmp esi,0x2710 / jl ; 0x6CCBF7 latch := RAW tick
//     0x6CCC05 idiv 1000 ; 0x6CCC17 sub [ebx+0xbb8],esi ; 0x6CCC21 floor to 0
//
//   CONSUMER sub_6F7A18 @0x6F7A8F: obj+0xBB8 > 0 ? exp *= obj+0xBBC : exp
//   GRANTER  sub_786390: 0x7863D2 movzx esi,byte [eax+0x17] (item value),
//     0x7863D6/0x7863DB/0x7863E0 clamp to [2,0x40] else fall back to 2,
//     0x7863E5/0x7863EE conflict check, 0x786474/0x78647E cap,
//     0x786493 imul 0xE10 (1 hour), 0x78649A add, 0x7864A0 set multiplier.
//
// Base-register discipline: in SAVE esi is PRE-BIASED (0x6B100C lea esi,[eax+8])
// so [esi+N] == record offset N. In LOAD [ebp-0x28] = raw+8 is the record base
// while [ebp-8] is the RAW pointer (written once at 0x6AFDA8, never reassigned),
// so `fsub [eax+0xEF40]` means rec+0xEF38 == SessionSuffix+0x40, because
// 8 + 0xEEF8 + 0x1A8 == 0xF0A8. Getting this wrong shifts every offset by 8.
//
// Golden corroboration (30 inflated records written by the ORIGINAL Delphi
// DBServer, staging/_nx_probe2_out.txt): rec 0x110 nonzero in 9/30 with values
// 46221.56..46223.26 == 2026-07-18..07-20 as TDateTime; rec 0x4C6 nonzero in
// exactly the same 9 with values {2:6, 3:3}, inside the [2,100] clamp; zero
// both-or-neither mismatches, matching the single SAVE gate.
//
// This audit derives the offsets from the anchors above and reads the production
// constants by REFLECTION, so it cannot drift into agreeing with a wrong value
// the way a hardcoded copy of the same literals would.
extern alias dbsvr;

using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using GameSvr;
// NOTE the dbsvr:: prefix rather than global::. Sibling audits (e.g.
// ShiMenCompatCheck) can say global::DBSvr.Core.NativeHumanDataCodec because that
// file is one of the three DBSvr/Core sources GameSvr.csproj shared-compiles, so
// the type exists in BOTH assemblies. NativeDbServerProtocol is NOT shared, so it
// lives only in the aliased reference and global:: cannot see it.
using NativeDbServerProtocol
    = dbsvr::DBSvr.Core.NativeDbServerProtocol;
using NativeHumanSessionContext
    = dbsvr::DBSvr.Core.NativeHumanSessionContext;

// ---- anchors (each is an instruction address, not a restatement of C#) ----
const int recordSize = 0xEEF8;                  // DataRecordSize, == golden length
const int nativeExpSecondsObjOffset = 0x0BB8;   // 0x6B1308 / 0x6CCC17 / 0x78649A
const int nativeExpMultObjOffset = 0x0BBC;      // 0x6B1328 / 0x7864A0
const int nativeTrueSightObjOffset = 0x0BD0;    // 0x6B1336
const int nativeThirdBuffObjOffset = 0x0BD4;    // 0x6B1356
const int nativeExpDeadlineRecOffset = 0x0110;  // 0x6B1321
const int nativeExpMultRecOffset = 0x04C6;      // 0x6B132F
const int nativeTrueSightRecOffset = 0x0118;    // 0x6B134F
const int nativeThirdBuffRecOffset = 0x0120;    // 0x6B136F
const double nativeSecondsPerDay = 86400.0;     // [0x6B1760] raw 00c0a847
const int nativeMultMin = 2;                    // 0x6B031F
const int nativeMultMax = 0x64;                 // 0x6B0334
const int nativeGrantMultMax = 0x40;            // 0x7863DB
// TWO DIFFERENT bounds, which an earlier draft of this audit wrongly collapsed
// into one. LOAD rejects a decoded remaining time above 0x83D600 (8640000s ==
// exactly 100 days) at 0x6B0357/0x6B035D; the GRANTER refuses a top-up above
// 0x7A1200 (8000000s) at 0x786474/0x78647E. They differ by 640000 seconds.
const int nativeLoadMaxSeconds = 0x83D600;      // 0x6B0357
const int nativeGrantCapSeconds = 0x7A1200;     // 0x786474
const int nativeGrantUnit = 0xE10;              // 0x786493
const int nativeTickMillis = 0x2710;            // 0x6CCBEB
const int nativeSuffixClockOffset = 0x40;       // 0xEF40 - 8 - 0xEEF8
const int humanInfoPrefix = 0x08;               // 0x6B100C lea esi,[eax+8]
const int sessionSuffixSize = 0x01A8;
const int humanInfoSize = 0xF0A8;

var failures = new List<string>();
var checks = 0;

PrepareRuntimeConfig();
// TBaseObject's constructor registers itself with these singletons.
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
// The expiry notifications go through SysMsg -> SendMsg, which takes this lock.
// Production initialises it during startup; the audit builds players by
// reflection, so seed it here or the tick throws instead of asserting.
M2Share.ProcessMsgCriticalSection ??= new object();

CheckLayoutArithmetic();
CheckOffsetConstants();
CheckDeadlineIsAbsoluteNotRemaining();
CheckMultiplierClamps();
CheckRoundTrip();
CheckInactiveBuffWritesZeroes();
CheckLoadRejectsOverCap();
CheckTickSemantics();
CheckConsumerGate();
CheckGranterLadder();
CheckGoldenShapedRecord();
CheckTickIsActuallyWired();
CheckClockBaseIsSuppliedByTheSuffixWriter();

Report();

// =====================================================================
void CheckLayoutArithmetic()
{
    // The whole offset family hinges on this: raw+0xEF40 is inside the session
    // suffix, not a global, which is why nothing in the game server writes it.
    Equal(humanInfoPrefix + recordSize + sessionSuffixSize, humanInfoSize,
        "prefix + NativeData + SessionSuffix == HumanInfoSize (0xF0A8)");
    Equal(0xEF40 - humanInfoPrefix - recordSize, nativeSuffixClockOffset,
        "raw+0xEF40 resolves to SessionSuffix+0x40");
    Equal(FieldValue<int>("NativeDbClockBaseSuffixOffset"), nativeSuffixClockOffset,
        "production clock-base offset matches the derived suffix offset");
    True(0xEF40 - humanInfoPrefix >= recordSize,
        "the clock base lies PAST the persisted NativeData region");
}

void CheckOffsetConstants()
{
    Equal(FieldValue<int>("NativeExpBuffDeadlineOffset"), nativeExpDeadlineRecOffset,
        "rec 0x110 deadline slot (enc 0x6B1321)");
    Equal(FieldValue<int>("NativeExpBuffMultiplierOffset"), nativeExpMultRecOffset,
        "rec 0x4C6 multiplier slot (enc 0x6B132F)");
    Equal(FieldValue<int>("NativeTrueSightDeadlineOffset"), nativeTrueSightRecOffset,
        "rec 0x118 true-sight slot (enc 0x6B134F)");
    Equal(FieldValue<int>("NativeThirdBuffDeadlineOffset"), nativeThirdBuffRecOffset,
        "rec 0x120 third-buff slot (enc 0x6B136F)");
    Equal(FieldValue<double>("NativeSecondsPerDay"), nativeSecondsPerDay,
        "86400.0 divisor at [0x6B1760]");
    Equal(FieldValue<int>("NativeExpBuffLoadMaxSeconds"), nativeLoadMaxSeconds,
        "LOAD bound is 0x83D600 = 8640000s = 100 days (0x6B0357)");
    Equal(FieldValue<int>("NativeExpBuffGrantMaxSeconds"), nativeGrantCapSeconds,
        "GRANT cap is 0x7A1200 = 8000000s (0x786474)");
    NotEqual(FieldValue<int>("NativeExpBuffLoadMaxSeconds"),
        FieldValue<int>("NativeExpBuffGrantMaxSeconds"),
        "the LOAD bound and the GRANT cap are NOT the same constant");
    Equal(nativeLoadMaxSeconds / 86400, 100,
        "the LOAD bound is exactly 100 days, confirming it is a sanity bound");
    Equal(FieldValue<int>("NativeExpBuffGrantUnitSeconds"), nativeGrantUnit,
        "0xE10 = one hour grant unit (0x786493)");
    Equal(FieldValue<int>("NativeTimedBuffTickMillis"), nativeTickMillis,
        "0x2710 = 10s tick gate (0x6CCBEB)");

    // The three deadline slots must be distinct and 8 bytes apart as laid out by
    // the three fstp qword stores; an off-by-8 slip would collide them.
    True(nativeExpDeadlineRecOffset + sizeof(double) <= nativeTrueSightRecOffset,
        "rec 0x110 (8 bytes) does not overlap rec 0x118");
    True(nativeTrueSightRecOffset + sizeof(double) <= nativeThirdBuffRecOffset,
        "rec 0x118 (8 bytes) does not overlap rec 0x120");
    True(nativeExpMultRecOffset + sizeof(ushort) <= recordSize,
        "rec 0x4C6 word fits inside the record");

    // Guard the specific wrong values an 8-byte base-register slip produces.
    NotEqual(FieldValue<int>("NativeExpBuffDeadlineOffset"),
        nativeExpDeadlineRecOffset + humanInfoPrefix,
        "deadline offset is NOT shifted +8 (raw-base confusion)");
    NotEqual(FieldValue<int>("NativeExpBuffDeadlineOffset"),
        nativeExpDeadlineRecOffset - humanInfoPrefix,
        "deadline offset is NOT shifted -8");
}

void CheckDeadlineIsAbsoluteNotRemaining()
{
    // This is the assertion that the retracted triage row got wrong. If the slot
    // held REMAINING time, encoding would be independent of the clock base; being
    // absolute means the SAME remaining seconds encode to DIFFERENT values as the
    // base advances -- and decode back to the same remaining time.
    const int seconds = 5 * 3600;
    var early = TPlayObject.NativeSecondsToDeadline(seconds, 46221.0);
    var late = TPlayObject.NativeSecondsToDeadline(seconds, 46223.0);
    NotEqual(early, late,
        "same remaining seconds encode differently as the clock base moves (absolute, not remaining)");
    Equal(late - early, 2.0,
        "the encoded difference equals the base difference exactly");
    Equal(TPlayObject.NativeDeadlineToSeconds(early, 46221.0), seconds,
        "decoding against its own base recovers the remaining seconds");

    // Rounding. sub_403574 is a bare fistp, so it uses the x87 rounding mode --
    // but the mode is only half the story, and an earlier draft of this audit got
    // the other half wrong in a way that locked in a real divergence.
    //
    // The control word is loaded during RTL startup: 0x004045C0 fninit /
    // 0x004045C3 fldcw [0x7A2024], and the two bytes at 0x7A2024 are 72 13, so
    // CW = 0x1372. Bits 10-11 (RC) = 0 = round-half-to-even, as assumed. But bits
    // 8-9 (PC) = 3 = EXTENDED, 64-bit significand -- so `fmul dword [0x6B0F44]`
    // keeps the product to 64 bits while a C# double multiply rounds it to 53
    // first. Because 86400 == 2^7 * 675 and 2^53 * 675 < 2^63, the exact product
    // ALWAYS fits the extended significand, so native rounds the true value and
    // the double path rounds an already-nudged one.
    //
    // These four cases are the proof, computed by exact rational arithmetic in
    // staging/_expbuff_exact.py (results in _expbuff_exact.txt), NOT by running
    // the C#. The old audit asserted the double-path answers (2 and 4) and even
    // added a NotEqual guard declaring native's real answer impossible.
    Equal(TPlayObject.NativeDeadlineToSeconds(1.5 / nativeSecondsPerDay, 0.0), 1,
        "1.5s -> 1: exact product is BELOW the midpoint (double path gives 2)");
    Equal(TPlayObject.NativeDeadlineToSeconds(2.5 / nativeSecondsPerDay, 0.0), 3,
        "2.5s -> 3: exact product is ABOVE the midpoint (double path gives 2)");
    Equal(TPlayObject.NativeDeadlineToSeconds(3.5 / nativeSecondsPerDay, 0.0), 3,
        "3.5s -> 3: exact product is BELOW the midpoint (double path gives 4)");
    Equal(TPlayObject.NativeDeadlineToSeconds(4.5 / nativeSecondsPerDay, 0.0), 5,
        "4.5s -> 5: exact product is ABOVE the midpoint (double path gives 4)");
    // Guard the double-rounded answers explicitly so a regression to a plain
    // `remainingDays * 86400.0` multiply cannot pass.
    NotEqual(TPlayObject.NativeDeadlineToSeconds(2.5 / nativeSecondsPerDay, 0.0), 2,
        "2.5s is specifically NOT the double-path 2 (PC=3, not PC=2)");
    NotEqual(TPlayObject.NativeDeadlineToSeconds(3.5 / nativeSecondsPerDay, 0.0), 4,
        "3.5s is specifically NOT the double-path 4");
    // Cases where both paths agree, to show the fix is not a blanket offset.
    Equal(TPlayObject.NativeDeadlineToSeconds(0.5 / nativeSecondsPerDay, 0.0), 0,
        "0.5s -> 0 on both paths (half-to-even to the even side)");
    Equal(TPlayObject.NativeDeadlineToSeconds(7.5 / nativeSecondsPerDay, 0.0), 8,
        "7.5s -> 8 on both paths");

    // ---- the EXACT-TIE cases, which pin the RC=0 rounding rule itself --------
    // The four cases above all land strictly OFF the midpoint -- that is what
    // makes them prove PC=3. They therefore never exercise the tie-break, and a
    // mutation to half-away-from-zero survived them (staging/_mutcheck_expbuff.py).
    // A genuine tie needs the exact product to be a half-integer. Since
    // 86400 == 2^7 * 675 and 675 is odd, that means days == m/256 with m odd:
    // the product is then exactly (m*675)/2. Both operands below are exactly
    // representable as doubles (verified in staging/_expbuff_tie.txt), so this
    // is a true tie and not a representation artefact.
    //   3/256 d -> 2025/2 == 1012.5 exactly: to-even 1012, away-from-zero 1013
    //   7/256 d -> 4725/2 == 2362.5 exactly: to-even 2362, away-from-zero 2363
    // CW 0x1372 has RC (bits 10-11) == 0 == round-half-to-even, so native takes
    // the even side and these must be 1012 and 2362.
    Equal(TPlayObject.NativeDeadlineToSeconds(3.0 / 256.0, 0.0), 1012,
        "exact tie 1012.5 rounds to EVEN 1012 (CW 0x1372 RC=0)");
    NotEqual(TPlayObject.NativeDeadlineToSeconds(3.0 / 256.0, 0.0), 1013,
        "specifically NOT half-away-from-zero, which would give 1013");
    Equal(TPlayObject.NativeDeadlineToSeconds(7.0 / 256.0, 0.0), 2362,
        "exact tie 2362.5 rounds to EVEN 2362");
    NotEqual(TPlayObject.NativeDeadlineToSeconds(7.0 / 256.0, 0.0), 2363,
        "specifically NOT 2363");
    // The opposite polarity: a tie whose floor is ODD must round UP, so a
    // mutation to "always truncate on a tie" is caught too.
    //   1/256 d -> 675/2 == 337.5 exactly, floor 337 is odd -> 338
    Equal(TPlayObject.NativeDeadlineToSeconds(1.0 / 256.0, 0.0), 338,
        "exact tie 337.5 rounds UP to even 338 (floor 337 is odd)");
    NotEqual(TPlayObject.NativeDeadlineToSeconds(1.0 / 256.0, 0.0), 337,
        "so a tie is not simply truncated");
    // The extended significand claim itself, as arithmetic rather than assertion.
    True(Math.ScaleB(675.0, 53) < 9.2233720368547758e18,
        "2^53 * 675 < 2^63, so the exact product fits the 64-bit significand");
    Equal(nativeSecondsPerDay, Math.ScaleB(675.0, 7),
        "86400 == 2^7 * 675, the decomposition the exact path relies on");

    Equal(TPlayObject.NativeDeadlineToSeconds(late, 46223.0), seconds,
        "same for the later base");

    // The exploit the triage claimed: relog with a stale deadline. Because the
    // deadline is absolute, a base advanced by 4 hours yields 4 fewer hours -- the
    // buff does NOT reset to its full duration.
    var afterFourHours = TPlayObject.NativeDeadlineToSeconds(
        early, 46221.0 + 4.0 / 24.0);
    Equal(afterFourHours, 3600,
        "re-decoding a stale deadline 4h later leaves 1h, not the original 5h");
    True(afterFourHours < seconds,
        "wall-clock always erodes the buff across a relog (no duplication exploit)");
}

void CheckMultiplierClamps()
{
    Equal(TPlayObject.NativeClampExpBuffMultiplier(0), nativeMultMin,
        "LOAD raises 0 to 2 (0x6B0327)");
    Equal(TPlayObject.NativeClampExpBuffMultiplier(1), nativeMultMin,
        "LOAD raises 1 to 2");
    Equal(TPlayObject.NativeClampExpBuffMultiplier(2), 2, "2 passes through");
    Equal(TPlayObject.NativeClampExpBuffMultiplier(100), nativeMultMax,
        "100 passes through (jle boundary at 0x6B0334)");
    Equal(TPlayObject.NativeClampExpBuffMultiplier(101), nativeMultMax,
        "101 lowers to 100");

    // The GRANT-side bound is tighter AND its out-of-range fallback is 2, not the
    // bound -- 0x7863E0 mov esi,2 rather than a clamp-to-0x40.
    Equal(TPlayObject.NativeResolveGrantMultiplier(2), 2, "grant accepts 2");
    Equal(TPlayObject.NativeResolveGrantMultiplier(0x40), nativeGrantMultMax,
        "grant accepts 0x40 (its jle boundary at 0x7863DB)");
    Equal(TPlayObject.NativeResolveGrantMultiplier(0x41), nativeMultMin,
        "grant maps 0x41 to 2, NOT to 0x40 (0x7863E0)");
    Equal(TPlayObject.NativeResolveGrantMultiplier(1), nativeMultMin,
        "grant maps 1 to 2");
    True(nativeGrantMultMax < nativeMultMax,
        "grant bound (0x40) is tighter than the load clamp (0x64)");
}

void CheckRoundTrip()
{
    var player = NewPlayer();
    var record = new byte[recordSize];
    SetRecord(player, record);

    // Establish the session clock the way LOAD does, then persist and restore.
    // With the DB base equal to the login local time the two clocks coincide
    // (offset 0), which is the simplest case to reason about.
    var localNow = 46221.5;
    SetSuffixClockBase(player, localNow);
    var dbNow = player.EstablishNativeDbClock(localNow);
    Equal(dbNow, localNow, "with base == login local time the clocks coincide");
    Equal(GetField<double>(player, "m_dNativeDbClockOffset"), 0.0,
        "and the stored offset is zero");

    // A DB clock running 2 days behind the server must shift every deadline by
    // exactly that much -- this is what makes the offset worth modelling.
    var skewed = NewPlayer();
    SetRecord(skewed, new byte[recordSize]);
    SetSuffixClockBase(skewed, localNow - 2.0);
    Equal(skewed.EstablishNativeDbClock(localNow), localNow - 2.0,
        "a DB clock 2 days behind yields a DB-now 2 days behind (0x6B0289/0x6B0292)");

    SetField(player, "m_nNativeExpBuffSeconds", 3 * 3600);
    SetField(player, "m_nNativeExpBuffMultiplier", 3);
    SetField(player, "m_nNativeTrueSightSeconds", 600);
    SetField(player, "m_nNativeThirdBuffSeconds", 60);

    True(player.PersistNativeTimedExpBuff(player.NativeDbClockNow(localNow)),
        "persist succeeds on a full-length record");

    var storedDeadline = BinaryPrimitives.ReadDoubleLittleEndian(
        record.AsSpan(nativeExpDeadlineRecOffset, sizeof(double)));
    Equal(storedDeadline, 46221.5 + 3.0 / 24.0,
        "stored deadline is dbNow + 3h expressed in days");
    Equal((int)BinaryPrimitives.ReadUInt16LittleEndian(
            record.AsSpan(nativeExpMultRecOffset, sizeof(ushort))), 3,
        "stored multiplier is the live value");

    // Reload with the clock advanced by one hour: two hours must remain. The DB
    // base advances with real time too, so the next session's base is the login
    // local time again -- keeping the offset at 0.
    var reloaded = NewPlayer();
    SetRecord(reloaded, record);
    var laterLocal = 46221.5 + 1.0 / 24.0;
    SetSuffixClockBase(reloaded, laterLocal);
    reloaded.RestoreNativeTimedExpBuff(reloaded.EstablishNativeDbClock(laterLocal));
    Equal(GetField<int>(reloaded, "m_nNativeExpBuffSeconds"), 2 * 3600,
        "one hour of wall clock consumed one hour of buff across the round trip");
    Equal(GetField<int>(reloaded, "m_nNativeExpBuffMultiplier"), 3,
        "multiplier survives the round trip");
    Equal(GetField<int>(reloaded, "m_nNativeTrueSightSeconds"), 0,
        "a 600s true-sight buff is fully expired after an hour (0x6B02F3 jbe)");
}

void CheckInactiveBuffWritesZeroes()
{
    // Native rebuilds the record over a zero-filled buffer (sub_6B6510 0x6B65FE
    // FillChar after the raw GetMem at 0x6B65E9) and SKIPS the store when the buff
    // is inactive, so the persisted bytes are ZERO. C# starts from
    // NativeData.Clone(), so it must zero them explicitly or a stale login value
    // would be re-shipped forever.
    var player = NewPlayer();
    var record = new byte[recordSize];
    BinaryPrimitives.WriteDoubleLittleEndian(
        record.AsSpan(nativeExpDeadlineRecOffset, sizeof(double)), 46221.75);
    BinaryPrimitives.WriteUInt16LittleEndian(
        record.AsSpan(nativeExpMultRecOffset, sizeof(ushort)), 3);
    BinaryPrimitives.WriteDoubleLittleEndian(
        record.AsSpan(nativeTrueSightRecOffset, sizeof(double)), 46221.80);
    BinaryPrimitives.WriteDoubleLittleEndian(
        record.AsSpan(nativeThirdBuffRecOffset, sizeof(double)), 46221.90);
    SetRecord(player, record);

    // The buff is inactive (0 seconds) but the LIVE multiplier is nonzero -- this
    // is the state a player is in after clearmulexptime, which zeroes the time and
    // deliberately leaves obj+0xBBC alone (0x6E3FB2). Without a nonzero live
    // multiplier here, "write the live value" and "write zero" are indistinguishable
    // and a mutation that re-ships the multiplier slips through.
    SetField(player, "m_nNativeExpBuffMultiplier", 5);
    True(player.PersistNativeTimedExpBuff(46222.0), "persist succeeds");
    Equal(BinaryPrimitives.ReadDoubleLittleEndian(
            record.AsSpan(nativeExpDeadlineRecOffset, sizeof(double))), 0.0,
        "inactive buff clears the carried-over deadline instead of re-shipping it");
    Equal((int)BinaryPrimitives.ReadUInt16LittleEndian(
            record.AsSpan(nativeExpMultRecOffset, sizeof(ushort))), 0,
        "inactive buff clears the carried-over multiplier (same SAVE gate)");
    Equal(BinaryPrimitives.ReadDoubleLittleEndian(
            record.AsSpan(nativeTrueSightRecOffset, sizeof(double))), 0.0,
        "inactive true-sight is cleared");
    Equal(BinaryPrimitives.ReadDoubleLittleEndian(
            record.AsSpan(nativeThirdBuffRecOffset, sizeof(double))), 0.0,
        "inactive third buff is cleared");
}

void CheckLoadRejectsOverCap()
{
    // 0x6B0355 jl / 0x6B0357 cmp 0x7A1200 / 0x6B035D jle: out-of-range remaining
    // time is REJECTED (left at 0) rather than clamped down to the cap.
    var player = NewPlayer();
    var record = new byte[recordSize];
    // A deadline 200 days out decodes to ~1.7e7 seconds, above the 100-day bound.
    BinaryPrimitives.WriteDoubleLittleEndian(
        record.AsSpan(nativeExpDeadlineRecOffset, sizeof(double)), 46421.0);
    BinaryPrimitives.WriteUInt16LittleEndian(
        record.AsSpan(nativeExpMultRecOffset, sizeof(ushort)), 3);
    SetRecord(player, record);
    player.RestoreNativeTimedExpBuff(46221.0);
    Equal(GetField<int>(player, "m_nNativeExpBuffSeconds"), 0,
        "over-bound remaining time is rejected to 0, not clamped to the bound");
    NotEqual(GetField<int>(player, "m_nNativeExpBuffSeconds"), nativeLoadMaxSeconds,
        "specifically NOT clamped to 8640000");

    // Exactly at the 100-day bound is ACCEPTED (0x6B035D is jle, not jl), and one
    // second past it is rejected. This is the assertion that distinguishes the
    // LOAD bound from the grant cap: 8000000s would wrongly reject both.
    var atBound = NewPlayer();
    var atBoundRecord = new byte[recordSize];
    BinaryPrimitives.WriteDoubleLittleEndian(
        atBoundRecord.AsSpan(nativeExpDeadlineRecOffset, sizeof(double)),
        46221.0 + 100.0);
    SetRecord(atBound, atBoundRecord);
    atBound.RestoreNativeTimedExpBuff(46221.0);
    Equal(GetField<int>(atBound, "m_nNativeExpBuffSeconds"), nativeLoadMaxSeconds,
        "exactly 100 days is accepted whole (0x6B035D jle)");
    True(nativeLoadMaxSeconds > nativeGrantCapSeconds,
        "a value the LOAD bound accepts can exceed the grant cap");

    // An unset (0.0) deadline must stay 0 -- the 0x6B02BD jbe guard.
    var unset = NewPlayer();
    var blank = new byte[recordSize];
    // Put a nonzero multiplier in the record so "skipped the clamp" and "clamped a
    // zero" are distinguishable: a wrong unconditional clamp would read this 7 and
    // keep it, while a correct skip leaves the live field at 0.
    BinaryPrimitives.WriteUInt16LittleEndian(
        blank.AsSpan(nativeExpMultRecOffset, sizeof(ushort)), 7);
    SetRecord(unset, blank);
    unset.RestoreNativeTimedExpBuff(46221.0);
    Equal(GetField<int>(unset, "m_nNativeExpBuffSeconds"), 0,
        "unset deadline yields no buff (0x6B02BD)");
    // ⚠️ CORRECTED. The old assertion here expected 2 on the stated premise that
    // "the multiplier clamp is unconditional". The bytes say otherwise: the guard
    // at 0x6B02BD is `jbe 0x6B03B9`, and 0x6B03B9 is the START of the rec 0x118
    // block -- it jumps clear PAST the multiplier read at 0x6B030C and both clamps
    // at 0x6B031F / 0x6B0334. So native never touches obj+0xBBC for an unset
    // deadline and it stays 0 on a fresh object. Verified branch target in
    // staging/_expbuff_d2.txt.
    Equal(GetField<int>(unset, "m_nNativeExpBuffMultiplier"), 0,
        "unset deadline SKIPS the multiplier block entirely (0x6B02BD jbe 0x6B03B9)");
    NotEqual(GetField<int>(unset, "m_nNativeExpBuffMultiplier"), nativeMultMin,
        "specifically NOT clamped to 2 -- the clamp is inside the guard");
    NotEqual(GetField<int>(unset, "m_nNativeExpBuffMultiplier"), 7,
        "and the stored 7 was never read at all");

    // A deadline in the past decodes to 0 via the second guard at 0x6B02F3.
    // That guard is `jbe 0x6B034A`, which lands AFTER the clamps too, so an
    // expired record likewise leaves the multiplier untouched -- a second,
    // independent path to the same conclusion.
    var past = NewPlayer();
    var pastRecord = new byte[recordSize];
    BinaryPrimitives.WriteDoubleLittleEndian(
        pastRecord.AsSpan(nativeExpDeadlineRecOffset, sizeof(double)), 46220.0);
    BinaryPrimitives.WriteUInt16LittleEndian(
        pastRecord.AsSpan(nativeExpMultRecOffset, sizeof(ushort)), 7);
    SetRecord(past, pastRecord);
    past.RestoreNativeTimedExpBuff(46221.0);
    Equal(GetField<int>(past, "m_nNativeExpBuffSeconds"), 0,
        "already-expired deadline yields 0 (0x6B02F3)");
    Equal(GetField<int>(past, "m_nNativeExpBuffMultiplier"), 0,
        "expired deadline also skips the clamp (0x6B02F3 jbe 0x6B034A, past 0x6B0340)");

    // The two SIBLING blocks have their OWN bound ladders, which this port used to
    // omit entirely -- letting a 200-day true-sight deadline restore in full.
    //   0xBD0: 0x6B0425 jl / 0x6B0427 cmp 0x83D600 / 0x6B042D jle, reject 0x6B046F
    //   0xBD4: 0x6B0507 jl / 0x6B0509 cmp 0x83D600 / 0x6B050F jle, reject 0x6B0551
    var siblings = NewPlayer();
    var siblingRecord = new byte[recordSize];
    BinaryPrimitives.WriteDoubleLittleEndian(
        siblingRecord.AsSpan(nativeTrueSightRecOffset, sizeof(double)), 46421.0);
    BinaryPrimitives.WriteDoubleLittleEndian(
        siblingRecord.AsSpan(nativeThirdBuffRecOffset, sizeof(double)), 46421.0);
    SetRecord(siblings, siblingRecord);
    siblings.RestoreNativeTimedExpBuff(46221.0);
    Equal(GetField<int>(siblings, "m_nNativeTrueSightSeconds"), 0,
        "an over-bound rec 0x118 deadline is rejected to 0 (0x6B0427)");
    Equal(GetField<int>(siblings, "m_nNativeThirdBuffSeconds"), 0,
        "an over-bound rec 0x120 deadline is rejected to 0 (0x6B0509)");
    // And exactly at the bound both siblings are accepted whole (jle).
    var siblingsAtBound = NewPlayer();
    var atBoundSibling = new byte[recordSize];
    BinaryPrimitives.WriteDoubleLittleEndian(
        atBoundSibling.AsSpan(nativeTrueSightRecOffset, sizeof(double)),
        46221.0 + 100.0);
    BinaryPrimitives.WriteDoubleLittleEndian(
        atBoundSibling.AsSpan(nativeThirdBuffRecOffset, sizeof(double)),
        46221.0 + 100.0);
    SetRecord(siblingsAtBound, atBoundSibling);
    siblingsAtBound.RestoreNativeTimedExpBuff(46221.0);
    Equal(GetField<int>(siblingsAtBound, "m_nNativeTrueSightSeconds"),
        nativeLoadMaxSeconds,
        "exactly 100 days on rec 0x118 is accepted whole (0x6B042D jle)");
    Equal(GetField<int>(siblingsAtBound, "m_nNativeThirdBuffSeconds"),
        nativeLoadMaxSeconds,
        "exactly 100 days on rec 0x120 is accepted whole (0x6B050F jle)");
}

void CheckTickSemantics()
{
    var player = NewPlayer();
    SetField(player, "m_nNativeExpBuffSeconds", 100);
    SetField(player, "m_dwNativeTimedBuffTick", 0);

    False(player.TickNativeExpBuff(nativeTickMillis - 1),
        "a sub-10s delta does not tick (0x6CCBEB jl)");
    Equal(GetField<int>(player, "m_nNativeExpBuffSeconds"), 100,
        "and leaves the counter untouched");

    False(player.TickNativeExpBuff(nativeTickMillis),
        "exactly 10s ticks without expiring a 100s buff");
    Equal(GetField<int>(player, "m_nNativeExpBuffSeconds"), 90,
        "10s delta subtracts 10 seconds (0x6CCC17)");

    // The latch is reset to the RAW tick, so a 10.9s delta discards 0.9s. Two
    // such passes must consume 20 seconds, not 21.
    var drift = NewPlayer();
    SetField(drift, "m_nNativeExpBuffSeconds", 100);
    SetField(drift, "m_dwNativeTimedBuffTick", 0);
    drift.TickNativeExpBuff(10_900);
    drift.TickNativeExpBuff(21_800);
    Equal(GetField<int>(drift, "m_nNativeExpBuffSeconds"), 80,
        "sub-second remainder is discarded each pass (latch := raw tick, 0x6CCBF7)");

    // Expiry floors at 0 and reports true once.
    var expiring = NewPlayer();
    SetField(expiring, "m_nNativeExpBuffSeconds", 5);
    SetField(expiring, "m_dwNativeTimedBuffTick", 0);
    True(expiring.TickNativeExpBuff(nativeTickMillis),
        "a delta at or beyond the remaining time reports expiry");
    Equal(GetField<int>(expiring, "m_nNativeExpBuffSeconds"), 0,
        "expiry floors at 0 rather than going negative (0x6CCC21)");
    False(expiring.TickNativeExpBuff(2 * nativeTickMillis),
        "an already-expired buff does not report expiry again");

    // The latch advances even with no buff active, because native latches before
    // reading obj+0xBB8 (0x6CCBF7 precedes 0x6CCC09).
    var idle = NewPlayer();
    SetField(idle, "m_dwNativeTimedBuffTick", 0);
    idle.TickNativeExpBuff(nativeTickMillis);
    Equal(GetField<int>(idle, "m_dwNativeTimedBuffTick"), nativeTickMillis,
        "latch advances even when no buff is active (0x6CCBF7 before 0x6CCC09)");

    // ---- the two SIBLING balances, previously not ticked at all --------------
    // sub_6CCBC4 continues past the 0xBB8 block with two more decrements that
    // share the SAME esi (one GetTickCount, one latch, one idiv):
    //   0x6CCC60 read 0xBD0 / 0x6CCC68 jle / 0x6CCC6E sub / 0x6CCC78 store 0
    //   0x6CCC91 read 0xBD4 / 0x6CCC99 jle / 0x6CCC9F sub / 0x6CCCA9 store 0
    // Before this fix both fields were loaded and persisted but never counted
    // down, so they were permanently stale within a session.
    var trio = NewPlayer();
    SetField(trio, "m_nNativeExpBuffSeconds", 100);
    SetField(trio, "m_nNativeTrueSightSeconds", 50);
    SetField(trio, "m_nNativeThirdBuffSeconds", 25);
    SetField(trio, "m_dwNativeTimedBuffTick", 0);
    trio.TickNativeExpBuff(nativeTickMillis);
    Equal(GetField<int>(trio, "m_nNativeExpBuffSeconds"), 90,
        "0xBB8 decrements by the shared elapsed (0x6CCC17)");
    Equal(GetField<int>(trio, "m_nNativeTrueSightSeconds"), 40,
        "0xBD0 decrements by the SAME esi in the same pass (0x6CCC6E)");
    Equal(GetField<int>(trio, "m_nNativeThirdBuffSeconds"), 15,
        "0xBD4 decrements by the SAME esi in the same pass (0x6CCC9F)");

    // Each sibling floors at 0 independently, and only the 0xBB8 block drives the
    // return value -- a sibling expiring is NOT reported as "the buff expired".
    var siblingExpiry = NewPlayer();
    SetField(siblingExpiry, "m_nNativeExpBuffSeconds", 0);
    SetField(siblingExpiry, "m_nNativeTrueSightSeconds", 3);
    SetField(siblingExpiry, "m_nNativeThirdBuffSeconds", 3);
    SetField(siblingExpiry, "m_dwNativeTimedBuffTick", 0);
    False(siblingExpiry.TickNativeExpBuff(nativeTickMillis),
        "a sibling expiring does not report Nx-buff expiry (return tracks 0xBB8 only)");
    Equal(GetField<int>(siblingExpiry, "m_nNativeTrueSightSeconds"), 0,
        "0xBD0 floors at 0 (0x6CCC76/0x6CCC78)");
    Equal(GetField<int>(siblingExpiry, "m_nNativeThirdBuffSeconds"), 0,
        "0xBD4 floors at 0 (0x6CCCA7/0x6CCCA9)");

    // A sub-gate delta must leave ALL THREE untouched, not just the first.
    var noTick = NewPlayer();
    SetField(noTick, "m_nNativeExpBuffSeconds", 100);
    SetField(noTick, "m_nNativeTrueSightSeconds", 50);
    SetField(noTick, "m_nNativeThirdBuffSeconds", 25);
    SetField(noTick, "m_dwNativeTimedBuffTick", 0);
    False(noTick.TickNativeExpBuff(nativeTickMillis - 1), "sub-10s delta rejected");
    Equal(GetField<int>(noTick, "m_nNativeTrueSightSeconds"), 50,
        "0xBD0 is behind the same 10s gate (0x6CCBF1 skips the whole body)");
    Equal(GetField<int>(noTick, "m_nNativeThirdBuffSeconds"), 25,
        "0xBD4 is behind the same 10s gate");

    // The 0xBB8 expiry message interpolates the multiplier, which is read at
    // 0x6CCC2F -- AFTER the balance is zeroed and with 0xBBC still intact.
    // Nothing in sub_6CCBC4 writes 0xBBC, so it must survive expiry.
    var messageState = NewPlayer();
    SetField(messageState, "m_nNativeExpBuffSeconds", 5);
    SetField(messageState, "m_nNativeExpBuffMultiplier", 3);
    SetField(messageState, "m_dwNativeTimedBuffTick", 0);
    True(messageState.TickNativeExpBuff(nativeTickMillis), "0xBB8 expires");
    Equal(GetField<int>(messageState, "m_nNativeExpBuffMultiplier"), 3,
        "the multiplier survives expiry (no write to 0xBBC in sub_6CCBC4)");
    // The message text itself, assembled from the three decoded literals.
    Equal(M2Share.g_sNativeExpBuffExpiredPrefix, "您的",
        "0x6CCCE0 len=4 decodes to the concat's first part");
    Equal(M2Share.g_sNativeExpBuffExpiredSuffix, "倍经验时间结束",
        "0x6CCCF0 len=14 decodes to the concat's third part");
    Equal(M2Share.g_sNativeTrueSightExpired, "您的真视时间结束",
        "0x6CCD08 len=16 is the 0xBD0 message (0x6CCC82 edx)");
}

void CheckConsumerGate()
{
    var player = NewPlayer();
    SetField(player, "m_nNativeExpBuffMultiplier", 3);

    // ⚠️ CORRECTED. The old assertions expected the INPUT exp back when the buff
    // is inactive. That came from reading the helper starting at 0x6F7A8F, which is
    // one instruction too late. The function actually starts at 0x6F7A8C:
    //   6F7A8C  or   ecx,0xffffffff   ; Result := -1   <-- the lost initialiser
    //   6F7A8F  cmp  dword [eax+0xBB8],0
    //   6F7A96  jle  0x6F7AA1         ; -> mov eax,ecx = -1
    // sub_6F7A18 ends at 0x6F7A88 ret with 0x6F7A89 lea eax,[eax] as padding, so
    // 0x6F7A8F cannot be inside it. Verified in staging/_expbuff_d2.txt.
    // -1 is a sentinel: the caller adds this to a bonus accumulator (0x6F7A4C)
    // and subtracts the base exp (0x6F7A54), so -1 discards the bucket.
    SetField(player, "m_nNativeExpBuffSeconds", 0);
    Equal(InvokeInt(player, "ApplyNativeExpBuff", 100), -1,
        "inactive buff returns the -1 sentinel (0x6F7A8C or ecx,-1 / 0x6F7A96 jle)");
    NotEqual(InvokeInt(player, "ApplyNativeExpBuff", 100), 100,
        "specifically NOT the input exp -- that loses the 0x6F7A8C initialiser");
    SetField(player, "m_nNativeExpBuffSeconds", -5);
    Equal(InvokeInt(player, "ApplyNativeExpBuff", 100), -1,
        "negative seconds also return -1 (signed compare at 0x6F7A8F)");
    SetField(player, "m_nNativeExpBuffSeconds", 1);
    Equal(InvokeInt(player, "ApplyNativeExpBuff", 100), 300,
        "active buff multiplies by obj+0xBBC (0x6F7A9E imul)");
}

void CheckGranterLadder()
{
    var player = NewPlayer();
    // Fresh grant: 5 hours at 3x.
    Equal(InvokeGrant(player, 5, 3), "Granted", "a fresh grant succeeds");
    Equal(GetField<int>(player, "m_nNativeExpBuffSeconds"), 5 * nativeGrantUnit,
        "hours are converted with the 0xE10 unit (0x786493)");
    Equal(GetField<int>(player, "m_nNativeExpBuffMultiplier"), 3,
        "multiplier is written (0x7864A0)");

    // Same multiplier tops up; a different one is refused BEFORE the cap test.
    Equal(InvokeGrant(player, 2, 3), "Granted", "same multiplier tops up");
    Equal(GetField<int>(player, "m_nNativeExpBuffSeconds"), 7 * nativeGrantUnit,
        "top-up ADDS to the remaining time (0x78649A add)");
    Equal(InvokeGrant(player, 1, 4), "MultiplierConflict",
        "a different multiplier while active is refused (0x7863EE)");
    Equal(GetField<int>(player, "m_nNativeExpBuffSeconds"), 7 * nativeGrantUnit,
        "the refused grant changed nothing");

    // Cap: strictly greater is refused (jg), exactly at the cap still tops up.
    var atCap = NewPlayer();
    SetField(atCap, "m_nNativeExpBuffSeconds", nativeGrantCapSeconds);
    SetField(atCap, "m_nNativeExpBuffMultiplier", 3);
    Equal(InvokeGrant(atCap, 1, 3), "Granted",
        "exactly at the cap still grants (0x78647E is jg, not jge)");

    var overCap = NewPlayer();
    SetField(overCap, "m_nNativeExpBuffSeconds", nativeGrantCapSeconds + 1);
    SetField(overCap, "m_nNativeExpBuffMultiplier", 3);
    Equal(InvokeGrant(overCap, 1, 3), "OverCap", "above the cap is refused");
    Equal(GetField<int>(overCap, "m_nNativeExpBuffSeconds"), nativeGrantCapSeconds + 1,
        "the refused grant changed nothing");

    // clearmulexptime zeroes the time and leaves the multiplier alone (0x6E3FB2).
    var cleared = NewPlayer();
    SetField(cleared, "m_nNativeExpBuffSeconds", 3600);
    SetField(cleared, "m_nNativeExpBuffMultiplier", 3);
    Invoke(cleared, "ClearNativeExpBuffTime");
    Equal(GetField<int>(cleared, "m_nNativeExpBuffSeconds"), 0,
        "clearmulexptime zeroes obj+0xBB8");
    Equal(GetField<int>(cleared, "m_nNativeExpBuffMultiplier"), 3,
        "clearmulexptime does NOT touch obj+0xBBC");
}

void CheckGoldenShapedRecord()
{
    // The nine active golden records carry deadlines 46221.56..46223.26 with
    // multipliers 2 or 3. Reproduce that shape: a real record must decode to a
    // sane remaining time under a contemporary clock base, and must be REJECTED
    // under a zero base -- which is the concrete reason the port has to source
    // the clock base from the session suffix instead of defaulting it to 0.
    var goldenDeadlines = new[]
    {
        46221.563842, 46221.569859, 46221.586143, 46221.646808, 46221.749045,
        46221.838446, 46222.052548, 46222.299870, 46223.263731
    };
    // Each deadline is decoded against a base slightly before the EARLIEST of
    // them, so all nine are still in the future and must decode in range.
    var contemporaryBase = 46221.5;
    var accepted = 0;
    foreach (var deadline in goldenDeadlines)
    {
        var seconds = TPlayObject.NativeDeadlineToSeconds(deadline, contemporaryBase);
        if (seconds > 0 && seconds <= nativeLoadMaxSeconds)
            accepted++;
    }
    Equal(accepted, goldenDeadlines.Length,
        "all 9 golden deadlines decode to an in-range remaining time under a contemporary base");

    // Under a ZERO base the raw product is ~3.99e9 seconds, which exceeds int32.
    // Native's helper stores a qword and keeps only the low dword (0x40357B pop
    // eax), so the value WRAPS NEGATIVE rather than saturating -- and the negative
    // result is then rejected by the jl at 0x6B0355. Either way the record is
    // refused, which is the concrete reason the port must source the clock base
    // from the session suffix instead of defaulting it to 0.
    foreach (var deadline in goldenDeadlines)
    {
        var zeroBased = TPlayObject.NativeDeadlineToSeconds(deadline, 0.0);
        True(zeroBased < 0 || zeroBased > nativeLoadMaxSeconds,
            "golden deadline " + deadline.ToString("F6", CultureInfo.InvariantCulture)
            + " is rejected under a zero clock base (wraps negative or over bound)");
    }
    // Pin the wrap explicitly for the earliest deadline so a future change from
    // truncation to saturation cannot pass silently.
    Equal(TPlayObject.NativeDeadlineToSeconds(46221.563842, 0.0), -301424180,
        "the zero-base product truncates modulo 2^32 (0x40357B keeps only eax)");

    // And an end-to-end proof that a zero base leaves the buff unrestored.
    var zeroBase = NewPlayer();
    var zeroRecord = new byte[recordSize];
    BinaryPrimitives.WriteDoubleLittleEndian(
        zeroRecord.AsSpan(nativeExpDeadlineRecOffset, sizeof(double)), 46221.563842);
    BinaryPrimitives.WriteUInt16LittleEndian(
        zeroRecord.AsSpan(nativeExpMultRecOffset, sizeof(ushort)), 3);
    SetRecord(zeroBase, zeroRecord);
    SetFieldObject(zeroBase, "m_NativeDbSessionSuffix", Array.Empty<byte>());
    zeroBase.RestoreNativeTimedExpBuff(zeroBase.EstablishNativeDbClock(0.0));
    Equal(GetField<int>(zeroBase, "m_nNativeExpBuffSeconds"), 0,
        "a missing session suffix leaves a real golden deadline unrestored");

    // Every observed golden multiplier survives the LOAD clamp unchanged.
    foreach (var multiplier in new[] { 2, 3 })
        Equal(TPlayObject.NativeClampExpBuffMultiplier(multiplier), multiplier,
            "golden multiplier " + multiplier + " passes the clamp unchanged");
}

void CheckTickIsActuallyWired()
{
    // A byte-perfect tick that nothing calls is still a dead feature, and this
    // whole family WAS dead: before the wiring pass, TickNativeExpBuff had zero
    // callers outside this audit, so no player's buff ever counted down in a live
    // session. Native's call is unconditional at 0x6B3B37 inside sub_6B2D38 ==
    // TPlayer.Run (VMT 0x6AC8C8 slot +0x88), the sole call to sub_6CCBC4 in the
    // image. Assert the C# counterpart by SOURCE INSPECTION -- reflection cannot
    // see a call site, and driving Run() here would need the whole engine.
    var runSource = FindRepoFile("GameSvr/Players/TPlayObject.Message.cs");
    if (runSource == null)
    {
        failures.Add("FAIL cannot locate TPlayObject.Message.cs to verify the "
                     + "tick is wired into Run (audit cannot self-certify)");
        checks++;
        return;
    }
    var text = File.ReadAllText(runSource);
    var runIndex = text.IndexOf("public override void Run()",
        StringComparison.Ordinal);
    True(runIndex >= 0, "TPlayObject.Run override still exists");

    // ⚠️ Scan LINE BY LINE and skip comment lines. A naive IndexOf of
    // "TickNativeExpBuff(" matches a commented-out call, so simply deleting the
    // wiring by commenting it out passed an earlier version of this check --
    // verified by mutation (staging/_mutcheck_expbuff2.py). That is the exact
    // false-green this assertion exists to prevent, so it must not be reachable.
    var lines = text.Split('\n');
    var runLine = -1;
    var callLine = -1;
    var callText = string.Empty;
    for (var i = 0; i < lines.Length; i++)
    {
        var trimmed = lines[i].TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal))
            continue;
        if (runLine < 0
            && trimmed.StartsWith("public override void Run()",
                StringComparison.Ordinal))
            runLine = i;
        if (callLine < 0 && runLine >= 0
            && trimmed.Contains("TickNativeExpBuff("))
        {
            callLine = i;
            callText = trimmed;
        }
    }
    True(runLine >= 0, "the Run override is found on a non-comment line");
    True(callLine > runLine,
        "TickNativeExpBuff is called from LIVE code inside TPlayObject.Run "
        + "(0x6B3B37 is unconditional in sub_6B2D38 == TPlayer.Run) -- a "
        + "commented-out call does not count");

    // Native's call sits at step marker 9 with no guard whatsoever, so the C#
    // call must not be parked behind a condition either.
    if (callLine > 0)
    {
        var beforeCall = callText.Substring(0,
            callText.IndexOf("TickNativeExpBuff(", StringComparison.Ordinal));
        False(beforeCall.Contains("if ") || beforeCall.Contains("if(")
              || beforeCall.Contains("&&") || beforeCall.Contains("?")
              || beforeCall.Contains("while "),
            "the tick call is unconditional, matching 0x6B3B37");
    }
}

// Resolve repo-relative paths from THIS file's compile-time location rather than
// from AppContext.BaseDirectory. The audit's OutputPath is ..\..\..\Build\, i.e.
// outside the repository, so walking parents up from the runtime directory never
// re-enters LyoMir2-master and every lookup would miss.
void CheckClockBaseIsSuppliedByTheSuffixWriter()
{
    // Everything above verifies the DECODE against a given clock base. This checks
    // where that base actually comes from in production, because if it is always
    // 0.0 then all of the above is exercised only by the audit and no live buff
    // ever restores.
    //
    // M2Server only ever READS raw+0xEF40: _cs_field.py EF40 finds five refs
    // (0x6B0289 / 0x6B02DD / 0x6B03EB / 0x6B04CD / 0x6B075D), all inside
    // sub_6AFD7C and all loads. So the value is supplied by DBServer, and
    // 0xEF40 - 8 - 0xEEF8 == 0x40 places it in the session suffix.
    //
    // DBSvr/** belongs to another workstream, so this only OBSERVES the real
    // writer -- it does not modify it. The point is to make the gap visible and
    // attributed rather than silently absent.
    var suffix = new byte[NativeDbServerProtocol.HumanInfoSuffixSize];
    var context = new NativeHumanSessionContext
    {
        UserIp = "127.0.0.1",
        AuthText54 = string.Empty,
        AuthText81 = string.Empty,
        AuthText102 = string.Empty,
        LoginExtension = new byte[NativeDbServerProtocol.LoginExtensionSize]
    };
    var written = NativeDbServerProtocol.TryWriteSessionSuffix(
        suffix, "auditacct", context, out var error);
    True(written, "the real session-suffix writer accepts a minimal context ("
                  + error + ")");

    var clockBase = BinaryPrimitives.ReadDoubleLittleEndian(
        suffix.AsSpan(nativeSuffixClockOffset, sizeof(double)));

    // Assert the CURRENT, HONEST state: the writer leaves 0x40 zero. When the
    // DBSvr workstream starts populating it, this assertion flips and whoever is
    // here next is forced to read the comment rather than discovering the
    // behaviour change by accident in production.
    //
    // HISTORY: this block used to assert the GAP existed (clockBase == 0.0) with
    // instructions to invert it once DBSvr landed the write. That happened on
    // 2026-08-09 and the inversion is done below. The original handoff contract,
    // with every native EA, is in
    //   staging/GAMESVR_TO_DBSVR_HANDOFF_20260809.md  (section 2, priority P0)
    // GameSvr reads suffix+0x40 as a qword FIVE times (0x6B0289 / 0x6B02DD /
    // 0x6B03EB / 0x6B04CD all `fsub qword [eax+0xEF40]`, plus 0x6B075D `fld`)
    // and never writes it, so it is strictly inbound.
    // ✅ GAP CLOSED 2026-08-09 by the DBSvr workstream. This assertion is now
    // INVERTED, exactly as the note above instructed: the writer must supply a
    // non-zero Delphi TDateTime, and the previous "consequence" block (which
    // proved a golden deadline was rejected against a zero base) is gone because
    // it no longer describes production.
    //
    // Their evidence, cited here so this side can be re-derived independently:
    // DBServer evaluates it AT SEND TIME with 0x59A9E6 `fstp qword ptr [eax+0x40]`
    // writing an UNTRUNCATED Now(); the Trunc at 0x59A9F0 lands in struct+0x58 and
    // is NOT written back to +0x40, so sub-second precision survives. Truncating
    // it would skew every expiry by up to a day, since GameSvr's
    // `fsub qword [eax+0xEF40]` subtracts the full-precision value.
    True(clockBase > 0.0,
        "the DB clock base must now be a non-zero Delphi TDateTime -- if this "
        + "fails, the DBSvr writer regressed to the pre-2026-08-09 Clear()-only "
        + "behaviour and the whole timed-buff family stops restoring");

    // A TDateTime is days since 1899-12-30, so any plausible "now" is >= 40000
    // (2009) and far below 100000. This catches a unit mix-up (seconds, ms, or a
    // Unix epoch) rather than merely asserting non-zero.
    True(clockBase > 40000.0 && clockBase < 100000.0,
        $"the clock base must be in Delphi TDateTime DAYS (got {clockBase}) -- a "
        + "seconds/ms/Unix-epoch value would be wildly out of this window and "
        + "would break every deadline comparison");

    // Sub-second precision must survive: a whole-day value means someone applied
    // the Trunc that native deliberately keeps out of +0x40.
    True(clockBase != Math.Floor(clockBase),
        "the clock base must retain its fractional (sub-second) part -- native's "
        + "Trunc at 0x59A9F0 goes to struct+0x58 and is NOT written back to +0x40, "
        + "so a whole-number value means the truncation leaked in");

    // And the payoff, proven end to end: a REAL golden deadline now decodes into
    // the acceptable range instead of wrapping negative.
    var nowBase = clockBase;
    var goldenDeadline = nowBase + 1.0;   // one day out, as a live buff would be
    var decoded = TPlayObject.NativeDeadlineToSeconds(goldenDeadline, nowBase);
    True(decoded > 0 && decoded <= nativeLoadMaxSeconds,
        $"a one-day-out deadline must decode into range (got {decoded}), proving "
        + "the base is usable rather than merely non-zero");
    Equal(86400, decoded,
        "one day out must decode to exactly 86400 seconds");

    // The context type must expose the member, so the capability is structural
    // rather than accidental.
    var hasClockMember = typeof(NativeHumanSessionContext)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Any(property => property.Name.Contains("Clock")
                         || property.Name.Contains("DbTime")
                         || property.Name.Contains("ServerTime"));
    True(hasClockMember,
        "NativeHumanSessionContext must expose a clock-base member now that the "
        + "gap is closed");
}

string FindRepoFile(string relative,
    [System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
{
    // thisFile = <repo>/AuditTools/NativeTimedExpBuffCheck/Program.cs
    var directory = Directory.GetParent(thisFile);
    while (directory != null)
    {
        var candidate = Path.Combine(directory.FullName, relative);
        if (File.Exists(candidate)) return candidate;
        directory = directory.Parent;
    }
    return null;
}

// =====================================================================
// helpers
// =====================================================================
// TPlayObject's constructor chain touches M2Share, whose static initializer reads
// these config files; the other audits in this tree seed them the same way.
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

TPlayObject NewPlayer() => (TPlayObject)Activator.CreateInstance(
    typeof(TPlayObject), nonPublic: true);

void SetRecord(TPlayObject player, byte[] record)
    => SetFieldObject(player, "m_NativeHumanData", record);

void SetSuffixClockBase(TPlayObject player, double value)
{
    var suffix = new byte[sessionSuffixSize];
    BinaryPrimitives.WriteDoubleLittleEndian(
        suffix.AsSpan(nativeSuffixClockOffset, sizeof(double)), value);
    SetFieldObject(player, "m_NativeDbSessionSuffix", suffix);
}

FieldInfo Field(string name)
{
    var field = typeof(TPlayObject).GetField(name,
        BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.Public | BindingFlags.NonPublic);
    if (field == null)
        throw new InvalidOperationException(
            "TPlayObject." + name + " not found -- renamed or removed, "
            + "so this audit can no longer verify it.");
    return field;
}

// Reads the production constant rather than restating its literal, so a wrong
// value in GameSvr cannot be mirrored here into a false green.
T FieldValue<T>(string name)
{
    var field = Field(name);
    var value = field.IsLiteral
        ? field.GetRawConstantValue()
        : field.GetValue(null);
    return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
}

T GetField<T>(TPlayObject player, string name)
    => (T)Convert.ChangeType(Field(name).GetValue(player), typeof(T),
        CultureInfo.InvariantCulture);

void SetField(TPlayObject player, string name, int value)
    => Field(name).SetValue(player, value);

void SetFieldObject(TPlayObject player, string name, object value)
    => Field(name).SetValue(player, value);

MethodInfo Method(string name)
{
    var method = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (method == null)
        throw new InvalidOperationException(
            "TPlayObject." + name + " not found -- renamed or removed.");
    return method;
}

int InvokeInt(TPlayObject player, string name, int argument)
    => (int)Method(name).Invoke(player, new object[] { argument });

void Invoke(TPlayObject player, string name)
    => Method(name).Invoke(player, Array.Empty<object>());

string InvokeGrant(TPlayObject player, int hours, int multiplier)
    => Method("GrantNativeExpBuff")
        .Invoke(player, new object[] { hours, multiplier }).ToString();

void Equal<T>(T actual, T expected, string message)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
        failures.Add("FAIL " + message + " (expected " + Show(expected)
                     + ", got " + Show(actual) + ")");
}

void NotEqual<T>(T actual, T notExpected, string message)
{
    checks++;
    if (EqualityComparer<T>.Default.Equals(actual, notExpected))
        failures.Add("FAIL " + message + " (must not be " + Show(notExpected) + ")");
}

void True(bool condition, string message)
{
    checks++;
    if (!condition) failures.Add("FAIL " + message);
}

void False(bool condition, string message)
{
    checks++;
    if (condition) failures.Add("FAIL " + message + " (expected false)");
}

string Show<T>(T value) => value is double d
    ? d.ToString("R", CultureInfo.InvariantCulture)
    : Convert.ToString(value, CultureInfo.InvariantCulture);

void Report()
{
    if (failures.Count > 0)
    {
        foreach (var failure in failures) Console.WriteLine(failure);
        Console.WriteLine("NativeTimedExpBuffCheck FAILED: "
                          + failures.Count + " of " + checks + " assertions");
        Environment.Exit(1);
    }
    Console.WriteLine("NativeTimedExpBuffCheck PASS (" + checks + " assertions)");
    Console.WriteLine("  rec 0x110 deadline / 0x4C6 multiplier / 0x118 true-sight"
                      + " / 0x120 third buff");
    Console.WriteLine("  clock base = SessionSuffix+0x" + nativeSuffixClockOffset
                      .ToString("X2") + " (raw+0xEF40, derived not assumed)");
    Console.WriteLine("  SAVE sub_6B0FF0 @0x6B1321/0x6B132F,"
                      + " LOAD sub_6AFD7C @0x6B02D4..0x6B0306,"
                      + " TICK sub_6CCBC4 @0x6CCC17,"
                      + " CONSUMER sub_6F7A18 @0x6F7A8F,"
                      + " GRANTER sub_786390 @0x78649A");
    Console.WriteLine("  deadlines are ABSOLUTE: the triage's relog-duplication"
                      + " claim is refuted by CheckDeadlineIsAbsoluteNotRemaining");
    Console.WriteLine("  rounding runs at x87 EXTENDED precision (CW [0x7A2024]"
                      + " = 0x1372, PC=3), reproduced exactly via 86400 = 2^7*675");
    Console.WriteLine("  all THREE balances tick off one shared elapsed"
                      + " (0x6CCC17 / 0x6CCC6E / 0x6CCC9F), wired at 0x6B3B37");
    Console.WriteLine("  ✅ GAP CLOSED 2026-08-09: DBSvr's TryWriteSessionSuffix"
                      + " now writes the clock base at SessionSuffix+0x40"
                      + " (their evidence: 0x59A9E6 fstp qword [eax+0x40], an"
                      + " UNTRUNCATED Now() evaluated per-send). This audit"
                      + " asserts it is a real TDateTime in DAYS and RETAINS its"
                      + " fractional part -- the Trunc at 0x59A9F0 lands in"
                      + " struct+0x58 and is NOT written back, so a whole-number"
                      + " base means truncation leaked in and every expiry skews"
                      + " by up to a day.");
}
