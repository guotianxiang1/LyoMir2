using System;
using System.Buffers.Binary;
using System.Numerics;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // ==================================================================
        // Nx-experience buff (倍经验时间) and its two sibling timed buffs.
        //
        // Live layer (战神 object fields):
        //   obj+0xBB8  Integer  remaining SECONDS of the Nx-experience buff
        //   obj+0xBBC  Integer  the multiplier N
        //   obj+0xBD0  Integer  remaining seconds of the True-Sight buff (真视)
        //   obj+0xBD4  Integer  remaining seconds of a third timed buff
        //
        // Persisted layer (save record):
        //   rec+0x110  Double   ABSOLUTE expiry, TDateTime
        //   rec+0x4C6  Word     the multiplier N
        //   rec+0x118  Double   ABSOLUTE expiry of the True-Sight buff
        //   rec+0x120  Double   ABSOLUTE expiry of the third buff
        //
        // The record stores an absolute DEADLINE, not the remaining seconds.
        // SAVE sub_6B0FF0 computes one shared "now" base first:
        //   0x6B12F9  call sub_40F0A4            ; Now()
        //   0x6B12FE  fsub qword [ebx+0x780]     ; minus obj+0x780
        //   0x6B1304  fstp qword [ebp-0x10]      ; -> shared base, reused 3x
        // then, per buff, under a `> 0` gate:
        //   0x6B1308  mov eax,[ebx+0xbb8]
        //   0x6B1310  jle 0x6B1336               ; skip BOTH slots when <= 0
        //   0x6B1315  fild dword [ebp-0x1c]
        //   0x6B1318  fdiv dword [0x6b1760]      ; = 86400.0 (raw 00c0a847)
        //   0x6B131E  fadd qword [ebp-0x10]
        //   0x6B1321  fstp qword [esi+0x110]     ; rec 0x110
        //   0x6B1328  mov ax,[ebx+0xbbc]
        //   0x6B132F  mov [esi+0x4c6],ax         ; rec 0x4C6
        // The True-Sight and third buffs repeat the identical shape at
        // 0x6B1336..0x6B134F (rec 0x118) and 0x6B1356..0x6B136F (rec 0x120).
        // Dividing seconds by 86400 and ADDING a date is what makes these
        // absolute deadlines; a remaining-time field would store the seconds.
        //
        // LOAD sub_6AFD7C inverts it:
        //   0x6B02AE  fld qword [eax+0x110]      ; eax = rec base (raw+8)
        //   0x6B02B4  fcomp dword [0x6b0f40]     ; = 0.0
        //   0x6B02BD  jbe  -> skip (unset deadline)
        //   0x6B02DD  fsub qword [eax+0xef40]    ; eax = RAW base here
        //   0x6B02EA  fcomp 0.0 / 0x6B02F3 jbe   ; already expired -> leave 0
        //   0x6B02F8  fmul dword [0x6b0f44]      ; = 86400.0
        //   0x6B02FE  call sub_403574            ; fistp -> round to Integer
        //   0x6B0306  mov [edx+0xbb8],eax
        // then clamps the multiplier and caps the seconds:
        //   0x6B031F  cmp eax,2      / 0x6B0327  mov [eax+0xbbc],2
        //   0x6B0334  cmp ...,0x64   / 0x6B0340  mov [eax+0xbbc],0x64
        //   0x6B034D/0x6B0355/0x6B0357  cmp ebx,0x83D600 (8640000s) -> reject
        // NOTE the two guards above jump PAST the multiplier block: 0x6B02BD is
        // `jbe 0x6B03B9` (the rec 0x118 block) and 0x6B02F3 is `jbe 0x6B034A`
        // (after both clamps). The clamp is therefore guarded, not unconditional.
        //
        // Base-register discipline (getting this wrong shifts every offset by 8):
        // in SAVE, esi is PRE-BIASED (0x6B100C lea esi,[eax+8]) so [esi+N] is
        // record offset N. In LOAD, [ebp-0x28] = raw+8 (0x6AFDBC..0x6AFDC2) is the
        // record base while [ebp-8] is the RAW pointer, written once at 0x6AFDA8
        // and never reassigned. Hence `fsub [eax+0xEF40]` is rec+0xEF38, which is
        // SessionSuffix+0x40 (8 + 0xEEF8 + 0x1A8 == 0xF0A8), NOT a global.
        //
        // Golden corroboration (30 inflated records written by the original
        // Delphi DBServer): rec 0x110 is nonzero in 9/30 with values
        // 46221.56..46223.26 == 2026-07-18..07-20 as TDateTime, and rec 0x4C6 is
        // nonzero in exactly those same 9 records with values {2:6, 3:3} -- inside
        // the [2,100] clamp. Both-or-neither holds in 30/30, matching the single
        // SAVE gate. See staging/_nx_probe2_out.txt and _nx_probe3_out.txt.
        // ==================================================================

        internal const int NativeExpBuffDeadlineOffset = 0x0110;   // enc 0x6B1321
        internal const int NativeExpBuffMultiplierOffset = 0x04C6; // enc 0x6B132F
        internal const int NativeTrueSightDeadlineOffset = 0x0118; // enc 0x6B134F
        internal const int NativeThirdBuffDeadlineOffset = 0x0120; // enc 0x6B136F

        /// <summary>Seconds per day, the 0x6B1760 / 0x6B0F44 single constant.</summary>
        internal const double NativeSecondsPerDay = 86400.0;

        // ------------------------------------------------------------------
        // The virtual clock these deadlines are expressed in.
        //
        // The deadline is NOT stored in local server time. LOAD establishes a
        // per-session clock offset first (sub_6AFD7C, before any buff is read):
        //   0x6B026E  call sub_40F0A4            ; Now(), local
        //   0x6B0276  fstp qword [eax+0x778]     ; obj+0x778 = login Now()
        //   0x6B0280  fld  qword [eax+0x778]
        //   0x6B0289  fsub qword [eax+0xef40]    ; minus the DB-supplied base
        //   0x6B0292  fstp qword [eax+0x780]     ; obj+0x780 = local - DB
        // ...where eax is [ebp-4] (the player) and [ebp-8] is the RAW record, so
        // 0xEF40 is SessionSuffix+0x40 as established above.
        //
        // SAVE then writes `(Now() - obj+0x780) + seconds/86400` (0x6B12F9..
        // 0x6B1321), and `Now() - (local - DB)` is exactly "now, expressed on the
        // DB clock". So both directions agree on a shared timeline that survives
        // a server whose local clock differs from the database's.
        //
        // This matters for the port rather than being trivia: with a base of 0.0
        // the subtraction in LOAD yields ~3.99e9 seconds for the real deadlines
        // observed in the golden corpus, which is far above the 8000000s cap at
        // 0x6B0357 -- so every record would be REJECTED and the buff would
        // silently never restore. Solving the cap inequality across the 9 active
        // golden deadlines pins the base to a window that brackets 2026-07-18,
        // i.e. a wall-clock date contemporary with the deadlines, confirming it is
        // a real timestamp and not a small elapsed-time value.
        // See staging/_nx_timebase_out.txt.
        // ------------------------------------------------------------------

        /// <summary>SessionSuffix offset of the DB-supplied clock base.</summary>
        internal const int NativeDbClockBaseSuffixOffset = 0x40;

        /// <summary>obj+0x778: local <c>Now()</c> captured at login (0x6B0276).</summary>
        public double m_dNativeLoginLocalTime;

        /// <summary>
        /// obj+0x780: local-minus-DB clock offset (0x6B0292). SAVE subtracts this
        /// from the current local time to get "now" on the DB clock.
        /// </summary>
        public double m_dNativeDbClockOffset;

        /// <summary>
        /// Reads the DB clock base out of the session suffix. Returns 0.0 when the
        /// suffix is absent or short, which is the same value the zero-filled
        /// buffer would have supplied.
        /// </summary>
        internal double ReadNativeDbClockBase()
        {
            var suffix = m_NativeDbSessionSuffix;
            return suffix != null
                   && suffix.Length >= NativeDbClockBaseSuffixOffset + sizeof(double)
                ? BinaryPrimitives.ReadDoubleLittleEndian(
                    suffix.AsSpan(NativeDbClockBaseSuffixOffset, sizeof(double)))
                : 0.0;
        }

        /// <summary>
        /// Establishes the per-session clock offset exactly as LOAD does at
        /// 0x6B026E..0x6B0292, and returns the DB-clock "now" that the buff
        /// deadlines are measured against.
        /// </summary>
        internal double EstablishNativeDbClock(double localNow)
        {
            m_dNativeLoginLocalTime = localNow;
            m_dNativeDbClockOffset = localNow - ReadNativeDbClockBase();
            return localNow - m_dNativeDbClockOffset;
        }

        /// <summary>
        /// The SAVE-side "now" on the DB clock: <c>Now() - obj+0x780</c>
        /// (0x6B12F9 / 0x6B12FE), computed once and shared by all three buffs.
        /// </summary>
        internal double NativeDbClockNow(double localNow)
            => localNow - m_dNativeDbClockOffset;

        /// <summary>LOAD clamp on the multiplier: 0x6B031F / 0x6B0334.</summary>
        internal const int NativeExpBuffMultiplierMin = 2;
        internal const int NativeExpBuffMultiplierMax = 0x64;

        /// <summary>
        /// LOAD rejects a decoded remaining time above this: 0x6B0357
        /// <c>cmp ebx,0x83D600</c> / 0x6B035D <c>jle</c>. 8640000s is exactly
        /// 100 days, which is the tell that it is a sanity bound on the decoded
        /// value rather than the product limit.
        /// </summary>
        internal const int NativeExpBuffLoadMaxSeconds = 0x83D600;

        /// <summary>
        /// The GRANT-side limit, which is a DIFFERENT number: 0x786474
        /// <c>cmp [ebx+0xBB8],0x7A1200</c> / 0x78647E <c>jg</c>. 8000000s
        /// (~92.6 days) is the cap on topping the buff up. Do not collapse these
        /// two constants -- they differ by 640000 seconds and gate different code.
        /// </summary>
        internal const int NativeExpBuffGrantMaxSeconds = 0x7A1200;

        /// <summary>Grant unit, one hour: 0x786493 imul eax,[ebp-8],0xE10.</summary>
        internal const int NativeExpBuffGrantUnitSeconds = 0xE10;

        /// <summary>The 10-second tick period: 0x6CCBEB cmp esi,0x2710.</summary>
        internal const int NativeTimedBuffTickMillis = 0x2710;

        /// <summary>Highest slot touched here, +2 for the Word at 0x4C6.</summary>
        private const int NativeTimedExpBuffMinimumLength =
            NativeExpBuffMultiplierOffset + sizeof(ushort);

        // Live remaining seconds. Native keeps these as plain Integers and the
        // tick floors them at 0, so a signed int is the faithful width.
        public int m_nNativeExpBuffSeconds;      // obj+0xBB8
        public int m_nNativeExpBuffMultiplier;   // obj+0xBBC
        public int m_nNativeTrueSightSeconds;    // obj+0xBD0
        public int m_nNativeThirdBuffSeconds;    // obj+0xBD4 -- the 彩色文字 timer

        // obj+0xBD0 的第二个已证消费点：等级差经验惩罚的豁免余额。
        // sub_6C02A4 @0x6C02B7 `cmp dword [ebx+0xBD0],0` / `jg` —— 余额 > 0 时
        // 整条惩罚被跳过（见 TBaseObject.CalcGetExp）。充值方是
        // TAntiDecExpProp.Use = sub_7865B4（VMT 0x77F3A8 槽 +0x18，SelfPtr 已核）：
        //   0x7865EE  imul esi,eax,0xE10        ; 小时 -> 秒
        //   0x7865F4  add  [edi+0xBD0],esi
        //   0x7865D2  cmp  [edi+0xBD0],0x7A1200 ; jg -> 拒绝充值
        // ✅ 命名冲突已裁决（2026-08-08，全镜像 obj+0xBD0 普查）：这**是同一个
        // 字段**，两条事实同时成立，不是错标。tick 到期播报的字面串就是“真视”：
        //   0x6CCC78  mov  [ebx+0xBD0],0        ; 归零
        //   0x6CCC82  mov  edx,0x6CCD08         ; 长串 = "您的真视时间结束"
        // 而机制侧确实是经验惩罚豁免。全镜像该偏移只有 6 个真实指令引用
        // （0x6B0414/0x6B041D/0x6B046F 存档三处、0x6B1336 存、0x6C02B7 消费、
        // 0x6CCC60..0x6CCC78 tick、0x7865D2/0x7865F4 充值），没有第二个 tick
        // 分支、没有第二条播报，故不存在“另一个真视字段”。保留现名以对齐原版
        // 播报文本。
        internal override int NativeFixedExpBalanceSeconds => m_nNativeTrueSightSeconds;

        /// <summary>
        /// obj+0xB86 — the 彩色文字 (colour-say) TIER byte that pairs with the
        /// obj+0xBD4 countdown above. Persisted at rec 0xD5; see
        /// TPlayObject.NativeUnmappedScalars.cs for the load/save binding.
        /// <para>
        /// Whole-image census (2026-08-08): 6 raw disp32 hits, 4 real references —
        /// <c>0x6B0495</c> (load W), <c>0x6B1376</c> (save R), <c>0x6C9442</c>
        /// (say-path R), <c>0x786845</c> (granter W). No clamp, and critically
        /// <b>no clear</b>: the tick block for 0xBD4 at
        /// <c>0x6CCC91..0x6CCCAF</c> zeroes only the countdown, so this byte is
        /// STICKY across expiry. A faithful port must not reset it.
        /// </para>
        /// <para>
        /// Granter <c>sub_786800</c> (TColorSayProp, VMT 0x77F7C8 slot +0x18,
        /// SelfPtr self-checked) derives it from the item template:
        /// <c>0x786840 mov al,[eax+0x15]</c> / <c>0x786843 sub al,0x16</c> /
        /// <c>0x786845 mov [esi+0xB86],al</c> — i.e. <c>StdItem+0x15 - 0x16</c>,
        /// with NO range validation on either side.
        /// </para>
        /// </summary>
        public byte m_btNativeColorSayTier;      // obj+0xB86

        /// <summary>
        /// Reproduces <c>sub_403574</c>, the Delphi <c>Trunc</c>/<c>Round</c> helper
        /// LOAD calls at 0x6B02FE:
        /// <code>
        ///   403574  sub  esp,8
        ///   403577  fistp qword ptr [esp]   ; 64-bit store, x87 rounding mode
        ///   40357B  pop  eax                ; LOW dword  -> the result
        ///   40357C  pop  edx                ; HIGH dword -> discarded here
        /// </code>
        /// Two details matter and both are easy to get wrong:
        ///
        /// 1. <c>fistp</c> uses the x87 rounding mode, which Delphi leaves at
        ///    round-half-to-EVEN. So 2.5 becomes 2, not 3 --
        ///    <see cref="MidpointRounding.ToEven"/>, not C#'s away-from-zero default.
        ///
        /// 2. The store is a QWORD and the caller keeps only the low dword, so an
        ///    out-of-int32-range value TRUNCATES modulo 2^32 (and can come back
        ///    negative) instead of saturating or raising. A plain checked cast
        ///    would throw and an unchecked <c>(int)</c> cast of an out-of-range
        ///    double is undefined in C#, so go through <see cref="long"/> first.
        ///    This is reachable in practice: a deadline decoded against a zero
        ///    clock base yields ~3.99e9 seconds, which wraps to -301424180 -- and
        ///    the negative result is then rejected by the <c>jl</c> at 0x6B0355,
        ///    which is exactly what native does with it.
        /// </summary>
        internal static int NativeFistpToInt(double value)
        {
            // Math.Round(double) already rounds half-to-even, which is what fistp
            // does. Do NOT write Math.Round(value, MidpointRounding.ToEven) here:
            // that binds to the (double, int) decimal-places overload, silently
            // treating the enum value 0 as "0 digits" while a nonzero enum member
            // would mean a digit count -- so the mode is not applied at all.
            var rounded = Math.Round(value);
            // Values beyond Int64 range are saturated by the conversion rather
            // than wrapping; native would store the x87 "integer indefinite"
            // value (0x8000000000000000) whose low dword is 0. Mirror that.
            if (rounded >= 9.2233720368547758e18 || rounded <= -9.2233720368547758e18
                || double.IsNaN(rounded))
                return 0;
            return unchecked((int)(long)rounded);
        }

        /// <summary>
        /// The seconds-per-day factor decomposed for exact integer arithmetic:
        /// 86400 == 2^7 * 675. See <see cref="NativeDaysToSecondsFistp"/>.
        /// </summary>
        private const int NativeSecondsPerDayOddFactor = 675;   // 86400 >> 7
        private const int NativeSecondsPerDayShift = 7;

        /// <summary>
        /// Reproduces <c>fmul dword [0x6B0F44]</c> followed by
        /// <c>call sub_403574</c> (<c>fistp qword</c>) at the x87 precision native
        /// actually runs at, which is NOT double.
        ///
        /// The FPU control word is loaded once during RTL startup:
        /// <code>
        ///   004045C0  fninit
        ///   004045C3  fldcw word ptr [0x7A2024]     ; bytes 72 13 -> CW = 0x1372
        /// </code>
        /// and re-loaded at 0x004034E8. Decoding 0x1372: PC (bits 8-9) = 3 =
        /// EXTENDED (64-bit significand), RC (bits 10-11) = 0 = round-half-to-even.
        /// So the multiply keeps a 64-bit significand while a C#
        /// <c>double</c> multiply would round the product to 53 bits FIRST.
        ///
        /// That double-rounding is observable, not theoretical. 86400 == 2^7 * 675,
        /// so the exact product of a 53-bit significand and 675 needs at most
        /// 53 + 10 == 63 bits (2^53 * 675 &lt; 2^63) -- it ALWAYS fits the extended
        /// significand exactly. Native therefore rounds the TRUE product, while the
        /// double path rounds a value that has already been nudged onto a midpoint.
        /// Measured over the half-second cases (staging/_expbuff_exact.txt):
        ///
        ///   stored remaining | exact product | native | double path
        ///   ---------------- | ------------- | ------ | -----------
        ///   1.5 s            | below 1.5     |   1    |     2
        ///   2.5 s            | above 2.5     |   3    |     2
        ///   3.5 s            | below 3.5     |   3    |     4
        ///   4.5 s            | above 4.5     |   5    |     4
        ///
        /// Four of the first eight half-second values disagree, so this is a real
        /// +/-1 second divergence rather than a rounding pedantry. .NET cannot ask
        /// for 80-bit floats, so the product is formed EXACTLY in integers instead:
        /// scale the double's mantissa by 675 and shift, which is precisely what
        /// the extended significand holds, then round half-to-even.
        /// </summary>
        internal static int NativeDaysToSecondsFistp(double days)
        {
            if (double.IsNaN(days) || double.IsInfinity(days))
                return 0;
            if (days == 0.0)
                return 0;

            // Decompose the double into sign * mantissa * 2^exponent exactly.
            var bits = BitConverter.DoubleToInt64Bits(days);
            var negative = bits < 0;
            var biasedExponent = (int)((bits >> 52) & 0x7FF);
            var mantissa = bits & 0xFFFFFFFFFFFFFL;
            int exponent;
            if (biasedExponent == 0)
            {
                // Subnormal: no implicit leading bit, exponent is fixed.
                exponent = -1074;
            }
            else
            {
                mantissa |= 0x10000000000000L;   // implicit leading 1
                exponent = biasedExponent - 1075;
            }
            if (mantissa == 0)
                return 0;

            // product = mantissa * 675 * 2^(exponent + 7), held exactly.
            var scaled = (BigInteger)mantissa * NativeSecondsPerDayOddFactor;
            var shift = exponent + NativeSecondsPerDayShift;

            BigInteger quotient;
            if (shift >= 0)
            {
                quotient = scaled << shift;     // an exact integer already
            }
            else
            {
                // Round half-to-even on the exact rational scaled / 2^-shift.
                var denominatorShift = -shift;
                quotient = scaled >> denominatorShift;
                var remainderMask = (BigInteger.One << denominatorShift) - 1;
                var remainder = scaled & remainderMask;
                var half = BigInteger.One << (denominatorShift - 1);
                if (remainder > half || (remainder == half && !quotient.IsEven))
                    quotient += 1;
            }
            if (negative)
                quotient = -quotient;

            // sub_403574 stores a QWORD and its caller keeps only the low dword
            // (0x40357B pop eax / 0x40357C pop edx), so an out-of-int32 value
            // truncates modulo 2^32 and can come back negative. Beyond int64 the
            // x87 would raise instead, and LOAD swallows that via SEH leaving the
            // field at 0 -- mirror both behaviours.
            if (quotient > long.MaxValue || quotient < long.MinValue)
                return 0;
            return unchecked((int)(long)quotient);
        }

        /// <summary>
        /// Converts a stored absolute deadline into remaining seconds exactly as
        /// LOAD does. <paramref name="deadline"/> is rec[0x110]-style TDateTime and
        /// <paramref name="timeBase"/> is the SessionSuffix+0x40 base. Returns 0
        /// for an unset (&lt;= 0) deadline and for one that has already passed --
        /// the two <c>fcomp 0.0</c> / <c>jbe</c> guards at 0x6B02BD and 0x6B02F3.
        /// </summary>
        internal static int NativeDeadlineToSeconds(double deadline, double timeBase)
        {
            if (deadline <= 0.0)
                return 0;
            var remainingDays = deadline - timeBase;
            if (remainingDays <= 0.0)
                return 0;
            // The multiply and the fistp happen at x87 extended precision, so this
            // must NOT be `NativeFistpToInt(remainingDays * NativeSecondsPerDay)`
            // -- that rounds the product to 53 bits first and shifts half-second
            // values by one. See NativeDaysToSecondsFistp.
            return NativeDaysToSecondsFistp(remainingDays);
        }

        /// <summary>
        /// The shared reject ladder each of the three LOAD blocks applies to its
        /// decoded seconds. All three are byte-identical apart from the field and
        /// the log label:
        /// <code>
        ///   0xBB8: 0x6B0353 test / 0x6B0355 jl / 0x6B0357 cmp 0x83D600 / 0x6B035D jle
        ///   0xBD0: 0x6B0423 test / 0x6B0425 jl / 0x6B0427 cmp 0x83D600 / 0x6B042D jle
        ///   0xBD4: 0x6B0505 test / 0x6B0507 jl / 0x6B0509 cmp 0x83D600 / 0x6B050F jle
        /// </code>
        /// Out of range is REJECTED to 0 (0x6B039F / 0x6B046F / 0x6B0551
        /// <c>mov [field],edx</c> with edx zeroed), never clamped down to the bound.
        /// The two sibling ladders were previously unmodelled, which let a
        /// 200-day true-sight deadline restore in full.
        /// </summary>
        internal static int NativeApplyLoadSecondsBound(int seconds)
            => seconds < 0 || seconds > NativeExpBuffLoadMaxSeconds ? 0 : seconds;

        /// <summary>
        /// Converts remaining seconds back to an absolute deadline as SAVE does:
        /// <c>fild</c> / <c>fdiv 86400.0</c> / <c>fadd base</c>. The caller is
        /// responsible for the <c>&gt; 0</c> gate, because native skips the store
        /// entirely (leaving the zero-filled buffer untouched) when the buff is
        /// inactive -- see 0x6B1310.
        /// </summary>
        internal static double NativeSecondsToDeadline(int seconds, double timeBase)
            => seconds / NativeSecondsPerDay + timeBase;

        /// <summary>
        /// The multiplier clamp LOAD applies after reading rec[0x4C6]: raise to 2
        /// (0x6B031F/0x6B0327) then lower to 100 (0x6B0334/0x6B0340).
        /// ⚠️ This clamp is reached ONLY when the deadline is set AND still in the
        /// future -- both LOAD guards branch past it (0x6B02BD jbe 0x6B03B9 and
        /// 0x6B02F3 jbe 0x6B034A). Callers must therefore apply the same guard;
        /// clamping unconditionally yields 2 where native leaves 0.
        /// </summary>
        internal static int NativeClampExpBuffMultiplier(int multiplier)
        {
            if (multiplier < NativeExpBuffMultiplierMin)
                return NativeExpBuffMultiplierMin;
            return multiplier > NativeExpBuffMultiplierMax
                ? NativeExpBuffMultiplierMax
                : multiplier;
        }

        /// <summary>
        /// Load direction. The shared DTO codec models none of these slots, so
        /// they must be read straight out of the native record.
        /// <paramref name="timeBase"/> comes from SessionSuffix+0x40.
        /// </summary>
        internal void RestoreNativeTimedExpBuff(double timeBase)
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length < NativeTimedExpBuffMinimumLength)
                return;

            // The deadline slot is read TWICE by native: once for the `> 0.0` guard
            // (0x6B02AE) and again inside the guarded body (0x6B02D4). Both reads
            // hit the same bytes, so one read here is equivalent.
            var deadline = BinaryPrimitives.ReadDoubleLittleEndian(
                raw.AsSpan(NativeExpBuffDeadlineOffset, sizeof(double)));

            // ⚠️ The multiplier read and its clamp live INSIDE both deadline
            // guards, not before them. Verified branch targets:
            //   0x6B02BD  jbe 0x6B03B9   ; deadline <= 0.0 -> jumps past the whole
            //                              block, landing in the rec 0x118 block
            //   0x6B02F3  jbe 0x6B034A   ; already expired -> lands at the bound
            //                              check, i.e. AFTER the clamp at
            //                              0x6B030C..0x6B0340
            // So an unset or expired deadline leaves obj+0xBBC untouched -- 0 on a
            // fresh object. Clamping unconditionally (as this used to) yields 2
            // where native yields 0.
            if (deadline > 0.0)
            {
                var remainingDays = deadline - timeBase;
                if (remainingDays > 0.0)
                {
                    // 0x6B02FE call sub_403574 / 0x6B0306 mov [edx+0xBB8],eax
                    m_nNativeExpBuffSeconds =
                        NativeDaysToSecondsFistp(remainingDays);
                    // 0x6B030F movzx eax,word [eax+0x4C6] / 0x6B0319 store,
                    // then the two clamps at 0x6B031F and 0x6B0334.
                    m_nNativeExpBuffMultiplier = NativeClampExpBuffMultiplier(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            raw.AsSpan(NativeExpBuffMultiplierOffset,
                                sizeof(ushort))));
                }
                // Whether or not the body ran, the bound check at 0x6B034A..
                // 0x6B035D is reached (the expired path jumps straight to it).
                m_nNativeExpBuffSeconds =
                    NativeApplyLoadSecondsBound(m_nNativeExpBuffSeconds);
            }

            // The two siblings repeat the identical shape, each with its OWN bound
            // ladder that this port previously omitted entirely:
            //   rec 0x118 -> obj+0xBD0, guards 0x6B03CB / 0x6B0401,
            //                store 0x6B0414, bound 0x6B0427
            //   rec 0x120 -> obj+0xBD4, guards 0x6B04AD / 0x6B04E3,
            //                store 0x6B04F6, bound 0x6B0509
            m_nNativeTrueSightSeconds = NativeApplyLoadSecondsBound(
                NativeDeadlineToSeconds(
                    BinaryPrimitives.ReadDoubleLittleEndian(
                        raw.AsSpan(NativeTrueSightDeadlineOffset, sizeof(double))),
                    timeBase));
            m_nNativeThirdBuffSeconds = NativeApplyLoadSecondsBound(
                NativeDeadlineToSeconds(
                    BinaryPrimitives.ReadDoubleLittleEndian(
                        raw.AsSpan(NativeThirdBuffDeadlineOffset, sizeof(double))),
                    timeBase));
        }

        /// <summary>
        /// Save direction. Native rebuilds the frame over a zero-filled buffer
        /// (sub_6B6510 0x6B65FE <c>FillChar</c> after the raw <c>GetMem</c> at
        /// 0x6B65E9), so an inactive buff must leave ZEROES behind -- not the
        /// value carried over from login by <c>NativeData.Clone()</c>. That is why
        /// the inactive branch writes zeroes explicitly instead of returning early.
        /// </summary>
        internal bool PersistNativeTimedExpBuff(double timeBase)
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length < NativeTimedExpBuffMinimumLength)
                return m_nNativeExpBuffSeconds == 0
                       && m_nNativeTrueSightSeconds == 0
                       && m_nNativeThirdBuffSeconds == 0;

            WriteNativeBuffDeadline(raw, NativeExpBuffDeadlineOffset,
                m_nNativeExpBuffSeconds, timeBase);
            // rec 0x4C6 lives under the SAME gate as rec 0x110 (0x6B1310 skips
            // both), which the golden corpus confirms: both-or-neither in 30/30.
            BinaryPrimitives.WriteUInt16LittleEndian(
                raw.AsSpan(NativeExpBuffMultiplierOffset, sizeof(ushort)),
                m_nNativeExpBuffSeconds > 0
                    ? unchecked((ushort)m_nNativeExpBuffMultiplier)
                    : (ushort)0);

            WriteNativeBuffDeadline(raw, NativeTrueSightDeadlineOffset,
                m_nNativeTrueSightSeconds, timeBase);
            WriteNativeBuffDeadline(raw, NativeThirdBuffDeadlineOffset,
                m_nNativeThirdBuffSeconds, timeBase);
            return true;
        }

        private static void WriteNativeBuffDeadline(byte[] raw, int offset,
            int seconds, double timeBase)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                raw.AsSpan(offset, sizeof(double)),
                seconds > 0 ? NativeSecondsToDeadline(seconds, timeBase) : 0.0);
        }

        /// <summary>obj+0x720: the tick latch this decrement runs off.</summary>
        public int m_dwNativeTimedBuffTick;

        /// <summary>
        /// The decrement pass, <c>sub_6CCBC4</c>, reached from the player Run loop
        /// (single caller 0x6B3B37 in sub_6B2D38). Native computes its own delta
        /// from a latch rather than being handed one:
        ///   0x6CCBDE  call sub_408340            ; GetTickCount
        ///   0x6CCBE5  sub esi,[ebx+0x720]        ; delta
        ///   0x6CCBEB  cmp esi,0x2710 / jl        ; needs a full 10s
        ///   0x6CCBF7  mov [ebx+0x720],eax        ; latch := RAW tick
        ///   0x6CCC05  idiv 1000                  ; -> whole seconds
        /// Note the latch is reset to the raw tick, NOT to `tick - (delta % 1000)`,
        /// so the sub-second remainder is discarded every pass and the countdown
        /// drifts slightly slow. That is faithful; do not "fix" it.
        ///
        /// Returns true when this call is what expired the buff, which is when
        /// native emits '倍经验时间结束' (0x6CCC3D).
        /// </summary>
        internal bool TickNativeExpBuff(int currentTick)
        {
            var elapsedMillis = unchecked(currentTick - m_dwNativeTimedBuffTick);
            if (elapsedMillis < NativeTimedBuffTickMillis)
                return false;
            m_dwNativeTimedBuffTick = currentTick;

            // 0x6CCBFD..0x6CCC07: esi := delta / 1000 via a SIGNED idiv. The one
            // esi is then shared by all three balance blocks below -- native does
            // not recompute it per buff.
            var elapsedSeconds = elapsedMillis / 1000;

            // ---- block 1: obj+0xBB8, the Nx-experience balance (0x6CCC09) ----
            // Native re-reads obj+0xBB8 AFTER latching (0x6CCC09), so the latch
            // advances even when no buff is active.
            var expired = false;
            if (m_nNativeExpBuffSeconds > 0)
            {
                // 0x6CCC13 cmp esi,eax / 0x6CCC15 jge: a delta that meets or
                // exceeds the remaining time zeroes the counter instead of going
                // negative.
                if (elapsedSeconds < m_nNativeExpBuffSeconds)
                {
                    m_nNativeExpBuffSeconds -= elapsedSeconds;
                }
                else
                {
                    m_nNativeExpBuffSeconds = 0;   // 0x6CCC1F/0x6CCC21
                    expired = true;
                    // 0x6CCC27..0x6CCC5A: a 3-part concat (0x6CCC4A edx=3) of
                    // '您的' + IntToStr(obj+0xBBC) + '倍经验时间结束', sent with
                    // cx=0xFCFF (0x6CCC52) through vmt+0xD4 (0x6CCC5A).
                    // The multiplier is read at 0x6CCC2F, i.e. AFTER the balance is
                    // zeroed and while obj+0xBBC still holds its value -- nothing
                    // in this function clears the multiplier.
                    SysMsg(M2Share.g_sNativeExpBuffExpiredPrefix
                           + m_nNativeExpBuffMultiplier
                           + M2Share.g_sNativeExpBuffExpiredSuffix,
                        MsgColor.Blue, MsgType.Hint);
                }
            }

            // ---- block 2: obj+0xBD0 (0x6CCC60) ----
            // Same shape, same esi, its own message and NO multiplier interpolation.
            if (m_nNativeTrueSightSeconds > 0)
            {
                if (elapsedSeconds < m_nNativeTrueSightSeconds)
                {
                    m_nNativeTrueSightSeconds -= elapsedSeconds;   // 0x6CCC6E
                }
                else
                {
                    m_nNativeTrueSightSeconds = 0;                 // 0x6CCC76/0x6CCC78
                    // 0x6CCC7E cx=0xFCFF / 0x6CCC82 edx=0x6CCD08
                    // '您的真视时间结束' / 0x6CCC8B call vmt+0xD4
                    SysMsg(M2Share.g_sNativeTrueSightExpired,
                        MsgColor.Blue, MsgType.Hint);
                }
            }

            // ---- block 3: obj+0xBD4 (0x6CCC91) ----
            // Identical arithmetic but SILENT: 0x6CCC99 jle / 0x6CCC9F sub /
            // 0x6CCCA7 xor / 0x6CCCA9 store, then straight to the epilogue at
            // 0x6CCCAF. There is no message EA and no colour word in this block,
            // so do not invent one.
            if (m_nNativeThirdBuffSeconds > 0)
            {
                if (elapsedSeconds < m_nNativeThirdBuffSeconds)
                    m_nNativeThirdBuffSeconds -= elapsedSeconds;
                else
                    m_nNativeThirdBuffSeconds = 0;
            }

            return expired;
        }

        /// <summary>
        /// The bonus helper <c>sub_6F7A8C</c> (a function in its OWN right --
        /// <c>sub_6F7A18</c> ends at 0x6F7A88 <c>ret</c> with 0x6F7A89
        /// <c>lea eax,[eax]</c> as alignment padding, so 0x6F7A8F is NOT inside
        /// it; its single caller is 0x6F7A3B):
        /// <code>
        /// 6F7A8C  or   ecx,0xFFFFFFFF        ; Result := -1  &lt;-- the initialiser
        /// 6F7A8F  cmp  dword [eax+0xBB8],0
        /// 6F7A96  jle  0x6F7AA1              ; inactive -> returns -1
        /// 6F7A98  mov  ecx,[eax+0xBBC]
        /// 6F7A9E  imul ecx,edx               ; 32-bit, wraps
        /// </code>
        /// Landing one instruction late loses <c>or ecx,-1</c> and inverts the
        /// contract: native returns **-1** when the buff is inactive, NOT the
        /// input exp. The caller treats the result as a bonus accumulator, not a
        /// total (0x6F7A4C <c>add</c>, then 0x6F7A54 <c>sub</c> the base), so -1
        /// is a sentinel that makes the whole bonus bucket be discarded.
        /// The live implementation of this is
        /// <c>NativeExpBuffBonus</c> in TPlayObject.NativeWinExp.cs; this method
        /// mirrors it so the model and the wired path cannot drift.
        /// Native reads the multiplier without re-clamping, so an out-of-range
        /// value that somehow reached the field is used as-is.
        /// </summary>
        internal int ApplyNativeExpBuff(int exp)
            => m_nNativeExpBuffSeconds > 0
                ? unchecked(m_nNativeExpBuffMultiplier * exp)
                : -1;

        /// <summary>
        /// The granter's item-supplied multiplier clamp, <c>sub_786390</c>
        /// 0x7863D6..0x7863E0. The raw value is a BYTE read out of the item
        /// (0x7863CF <c>mov eax,[edi+0x1c]</c> / 0x7863D2
        /// <c>movzx esi,byte [eax+0x17]</c>); anything outside [2,0x40] is
        /// replaced by 2 -- note the fallback is 2, not a clamp to the bound, so
        /// 0x41 becomes 2 rather than 0x40. This bound (0x40) is the GRANT-side
        /// limit and is deliberately tighter than the LOAD-side
        /// <see cref="NativeExpBuffMultiplierMax"/> (0x64).
        /// </summary>
        internal const int NativeExpBuffGrantMultiplierMax = 0x40;

        internal static int NativeResolveGrantMultiplier(byte itemValue)
            => itemValue >= NativeExpBuffMultiplierMin
               && itemValue <= NativeExpBuffGrantMultiplierMax
                ? itemValue
                : NativeExpBuffMultiplierMin;

        /// <summary>
        /// Substitutes Delphi <c>SysUtils.Format</c> placeholders positionally.
        /// <para>
        /// The two literals native passes to Format (0x78653C / 0x786598) use
        /// <c>%d</c> and <c>%s</c>, not .NET's <c>{0}</c>. Passing them to
        /// <c>string.Format</c> is a no-op, which would ship the raw
        /// <c>%d</c>/<c>%s</c> text to the client -- the literals are byte-exact
        /// to the binary, so the substitution has to happen here instead of by
        /// rewriting them into .NET syntax.
        /// </para>
        /// <para>
        /// Delphi consumes specifiers strictly left-to-right, so each occurrence
        /// of <c>%d</c> or <c>%s</c> takes the next argument regardless of which
        /// of the two it is. Both native call sites pass exactly as many
        /// arguments as the template has specifiers.
        /// </para>
        /// </summary>
        internal static string NativeFormatSequential(string template,
            params object[] args)
        {
            var result = new System.Text.StringBuilder(template.Length + 16);
            var next = 0;
            for (var i = 0; i < template.Length; i++)
            {
                if (template[i] == '%' && i + 1 < template.Length
                                       && (template[i + 1] == 'd'
                                           || template[i + 1] == 's')
                                       && next < args.Length)
                {
                    result.Append(args[next++]);
                    i++;
                    continue;
                }

                result.Append(template[i]);
            }

            return result.ToString();
        }

        /// <summary>
        /// Outcome of <c>sub_786390</c>, the Nx-experience granter.
        /// </summary>
        internal enum NativeExpBuffGrantOutcome
        {
            /// <summary>0x7863E5..0x7863F4: a buff is already running with a
            /// DIFFERENT multiplier. Native sends a message (0x786430, colour
            /// 0x38FF) and returns without granting.</summary>
            MultiplierConflict,

            /// <summary>0x786474/0x78647E: remaining time already exceeds the
            /// 8000000s cap, so nothing is added.</summary>
            OverCap,

            /// <summary>0x786493/0x78649A/0x7864A0: hours added, multiplier
            /// overwritten, success flag set at 0x7864A6.</summary>
            Granted,

            /// <summary>0x786443..0x78646F: the 网吧 (internet-cafe) refusal. Only
            /// reachable for multiplier == 2, and only when both the global
            /// 网吧活动 switch and this player's cafe flag are set. Native sends
            /// 0x786564 with <c>cx=0x38FF</c> and returns without granting.
            /// </summary>
            NetCafeRefusal
        }

        /// <summary>
        /// The 网吧活动 element of the global ServerSwitch set. The set is a Delphi
        /// <c>set of 0..39</c> (5 bytes, bit-packed, no alignment question), so
        /// element N lives at byte <c>N div 8</c> mask <c>1 shl (N mod 8)</c>.
        /// Native tests it as <c>0x78644D test byte [eax+3],8</c>, which is exactly
        /// element 27 -- the constants below are asserted to DERIVE that, not to
        /// match the literals by coincidence.
        /// </summary>
        internal const int NativeSwitchNetCafeActivityElement = 27;

        internal static int NativeSwitchByteOffset(int element) => element / 8;

        internal static byte NativeSwitchMask(int element) =>
            (byte)(1 << (element % 8));

        /// <summary>
        /// <c>player+0xB74</c>, the published read-only Boolean
        /// <c>IsNetCafeUser</c> backing field (TPropInfo @ 0x6AD529, nidx 79).
        /// <para>
        /// Native has exactly two writers and they both write INTO the player: the
        /// ctor zero-init (0x6AD921) and the login record-apply
        /// (0x6B0A2C..0x6B0A36 <c>test byte [ebx+0x56],0x10 / setne al / mov
        /// [edx+0xB74],al</c>, where <c>ebx = raw + 0xEF00</c> from
        /// <c>0x6B0982 lea ebx,[eax+0xEF00]</c> with <c>eax = [ebp-8]</c>, the RAW
        /// pointer). Nothing in the image writes <c>rec+0xEF56</c> at all, and the
        /// save-side counterpart never reads <c>+0xB74</c>, so the flag is
        /// recomputed from the inbound payload on every login and is read-only for
        /// the whole session.
        /// </para>
        /// <para>
        /// Because it is derived-on-read here rather than latched into a field,
        /// there is no new writer and no DTO member -- which also side-steps the
        /// known "DTO member without a decoder gets zeroed every login" hazard.
        /// <c>raw + 0xEF00</c> is precisely
        /// <c>NativeHumanDbCodec.SessionSuffixOffset</c> (8 + 0xEEF8 == 0xEF00),
        /// so the source byte is session-suffix byte 0x56. An absent or short
        /// suffix yields false, matching the ctor zero-init.
        /// </para>
        /// </summary>
        internal const int NativeNetCafeSuffixOffset = 0x56;

        internal const byte NativeNetCafeSuffixMask = 0x10;

        internal bool m_boNativeIsNetCafeUserRaw =>
            m_NativeDbSessionSuffix != null
            && m_NativeDbSessionSuffix.Length > NativeNetCafeSuffixOffset
            && (m_NativeDbSessionSuffix[NativeNetCafeSuffixOffset]
                & NativeNetCafeSuffixMask) != 0;

        /// <summary>
        /// The published property itself, <c>0x6EB28C</c>:
        /// <c>IsNetCafeUser := (27 in ServerSwitchSet) and (FIsNetCafeUser &lt;&gt; 0)</c>.
        /// The global switch gates the property, so the raw byte is meaningless
        /// while 网吧活动 is off. Native has zero direct callers of this getter (its
        /// only xref is the RTTI table entry at 0x6AD52D); it exists for
        /// published-property reflection, and the one real consumer at 0x786453
        /// re-tests both halves inline instead of calling it.
        /// </summary>
        internal bool m_boNativeIsNetCafeUser =>
            NativeIsNetCafeActivityOn() && m_boNativeIsNetCafeUserRaw;

        internal static bool NativeIsNetCafeActivityOn() =>
            M2Share.ServerSwitches?.IsBitSet(
                NativeSwitchByteOffset(NativeSwitchNetCafeActivityElement),
                NativeSwitchMask(NativeSwitchNetCafeActivityElement)) == true;

        /// <summary>
        /// The granter, <c>sub_786390</c>. Native's order of checks matters: the
        /// multiplier-conflict test comes FIRST (0x7863E5 <c>cmp [ebx+0xBB8],0</c>
        /// then 0x7863EE <c>cmp esi,[ebx+0xBBC]</c>, refusing only when a buff is
        /// live AND the multiplier differs), and only then the cap test at
        /// 0x786474. On success it adds whole hours (0x786493 <c>imul 0xE10</c>)
        /// and overwrites the multiplier (0x7864A0).
        /// </summary>
        internal NativeExpBuffGrantOutcome GrantNativeExpBuff(int hours, int multiplier)
        {
            if (m_nNativeExpBuffSeconds > 0
                && m_nNativeExpBuffMultiplier != multiplier)
                return NativeExpBuffGrantOutcome.MultiplierConflict;

            // The 网吧双倍 refusal, 0x786443..0x78646F. Ordering is load-bearing:
            // it sits BETWEEN the conflict test and the cap test, and both
            // 0x7863EC and 0x7863F4 jump straight to 0x786443 -- so a live buff
            // with the SAME multiplier falls through into this gate rather than
            // skipping it.
            //   786443  cmp esi,2                     ; multiplier == 2 only
            //   786446  jne 0x786474                  ; -> cap test
            //   786448  mov eax,[0x7D7038]            ; the ServerSwitch set
            //   78644D  test byte [eax+3],8           ; element 27 = 网吧活动
            //   786451  je  0x786474
            //   786453  cmp byte [ebx+0xB74],0        ; this player's cafe flag
            //   78645A  je  0x786474
            //   78645C  mov cx,0x38FF / 786460 mov edx,0x786564 / 786469 vmt+0xD4
            //   78646F  jmp 0x7864FD                  ; returns, granting nothing
            if (multiplier == 2 && m_boNativeIsNetCafeUser)
                return NativeExpBuffGrantOutcome.NetCafeRefusal;

            // Native compares with `jg`, i.e. it refuses only when STRICTLY
            // greater than the cap; a buff sitting exactly at 8000000s may still
            // be topped up. Note this is the GRANT cap (0x7A1200), not the LOAD
            // bound (0x83D600).
            if (m_nNativeExpBuffSeconds > NativeExpBuffGrantMaxSeconds)
                return NativeExpBuffGrantOutcome.OverCap;

            // BUFF-09: Clamp the post-grant total to the cap. Without this gate,
            // granting 10 hours when sitting at 7,999,900s yields 8,035,900s,
            // which exceeds the 8,000,000s cap by 35,900s.
            var granted = unchecked(
                m_nNativeExpBuffSeconds + hours * NativeExpBuffGrantUnitSeconds);
            m_nNativeExpBuffSeconds = granted > NativeExpBuffGrantMaxSeconds
                ? NativeExpBuffGrantMaxSeconds
                : granted;
            m_nNativeExpBuffMultiplier = multiplier;
            return NativeExpBuffGrantOutcome.Granted;
        }

        /// <summary>
        /// <c>clearmulexptime</c> / <c>sub_6E3FB0</c>: unconditionally zeroes
        /// obj+0xBB8 (0x6E3FB2). It does NOT touch the multiplier.
        /// </summary>
        internal void ClearNativeExpBuffTime() => m_nNativeExpBuffSeconds = 0;

        /// <summary>
        /// <c>TAntiDecExpProp.Use</c> = <c>sub_7865B4</c> (VMT 0x77F3A8 slot +0x18).
        /// Tops up the obj+0xBD0 balance that suppresses the level-gap experience
        /// penalty (consumer <c>0x6C02B7 cmp dword [ebx+0xBD0],0 / jg</c>).
        /// <para>
        /// Three gates, and note how they differ from the colour-say granter's:
        /// <code>
        ///   7865C3  test ecx,ecx                        ; player nil -> False
        ///   7865C7  cmp byte [ecx+0x178],0              ; race != 0  -> False
        ///   7865D2  cmp dword [edi+0xBD0],0x7A1200 / jg ; CAP, not a stacking gate
        /// </code>
        /// The third is a <b>cap on the existing balance</b>, tested with
        /// <c>jg</c>, so a balance sitting exactly at 0x7A1200 may still be topped
        /// up — and an already-active buff is simply EXTENDED
        /// (<c>0x7865F4 add [edi+0xBD0],esi</c>), never refused. That is the
        /// opposite of <see cref="GrantNativeColorSay"/>, whose
        /// <c>cmp [esi+0xBD4],0 / jne</c> makes it strictly non-stacking. Do not
        /// unify the two.
        /// </para>
        /// <para>
        /// Duration uses the same <c>word[StdItem+0x1C] / 1000</c> source as the
        /// other two granters (<c>0x7865E1</c> / unsigned <c>div</c> at
        /// <c>0x7865EC</c>) but the HOUR multiplier
        /// (<c>0x7865EE imul esi,eax,0xE10</c>), not colour-say's
        /// <c>0x15180</c> day.
        /// </para>
        /// <para>
        /// The function sends <b>no message at all</b> — there is no
        /// <c>vmt+0xD4</c> call anywhere in its 0x6B bytes. Its only outbound
        /// traffic is the internal-forward packet at <c>0x786610</c>
        /// (<c>dx=0x227</c>, discriminator <c>push 2</c>, payload = the seconds
        /// just added, not the new total), which carries no user-visible text.
        /// </para>
        /// </summary>
        /// <returns><c>true</c> exactly when native reaches
        /// <c>0x7865FA mov byte [ebp-1],1</c>.</returns>
        internal bool GrantNativeAntiDecExp(ushort duraMax)
        {
            // 7865C7 cmp byte [ecx+0x178],0 / jne -> False
            if (m_btRaceServer != 0) return false;
            // 7865D2 cmp dword [edi+0xBD0],0x7A1200 / jg -> False.
            // `jg` means strictly-greater refuses, so exactly-at-cap is allowed.
            // This is the GRANT cap (0x7A1200), NOT the LOAD bound (0x83D600).
            if (m_nNativeTrueSightSeconds > NativeExpBuffGrantMaxSeconds)
                return false;

            // BUFF-09: Clamp the post-grant total to the cap, matching the
            // sibling GrantNativeExpBuff fix.
            // 7865EC div (unsigned, truncating) / 7865EE imul eax,0xE10
            var granted = unchecked(
                m_nNativeTrueSightSeconds
                + (duraMax / 1000) * NativeExpBuffGrantUnitSeconds);
            m_nNativeTrueSightSeconds = granted > NativeExpBuffGrantMaxSeconds
                ? NativeExpBuffGrantMaxSeconds
                : granted;
            return true;
        }

        /// <summary>
        /// The 彩色文字 duration unit. Unlike the exp-buff granter's
        /// <see cref="NativeExpBuffGrantUnitSeconds"/> (0xE10 = one HOUR), the
        /// colour-say granter multiplies by <c>0x15180</c> = 86400 = one DAY:
        /// <c>0x786837 imul edi,eax,0x15180</c>.
        /// </summary>
        internal const int NativeColorSayGrantUnitSeconds = 0x15180;

        /// <summary>
        /// <c>TColorSayProp.Use</c> = <c>sub_786800</c> (VMT 0x77F7C8 slot +0x18,
        /// SelfPtr self-checked, class name reads <c>TColorSayProp</c>).
        /// <para>
        /// Three gates, all of which return the failure flag set at
        /// <c>0x78680B mov byte [ebp-1],0</c>:
        /// <code>
        ///   78680F  test ecx,ecx                  ; player nil        -> fail
        ///   786813  cmp byte [ecx+0x178],0        ; race != 0         -> fail
        ///   78681E  cmp dword [esi+0xBD4],0       ; already running   -> fail
        /// </code>
        /// The third makes the effect strictly NON-STACKING: unlike the exp buff
        /// (which tops up), an active colour-say cannot be extended or re-tiered.
        /// </para>
        /// <para>
        /// On success it computes the duration and the tier and stores both:
        /// <code>
        ///   786827  mov eax,[ebx+0x1C]            ; item -> StdItem (see below)
        ///   78682A  movzx eax,word [eax+0x1C]     ; StdItem.DuraMax
        ///   786835  div edi                       ; edi = 1000
        ///   786837  imul edi,eax,0x15180          ; whole thousands -> DAYS
        ///   78683D  mov eax,[ebx+0x1C]
        ///   786840  mov al,byte [eax+0x15]        ; Shape
        ///   786843  sub al,0x16                   ; -> tier
        ///   786845  mov [esi+0xB86],al
        ///   78684B  mov [esi+0xBD4],edi
        /// </code>
        /// Note the division is UNSIGNED (<c>div</c>, not <c>idiv</c>) and
        /// truncating, so a DuraMax below 1000 yields zero seconds — native still
        /// reports success and still overwrites the tier byte. Both the tier
        /// subtraction and the duration are unvalidated.
        /// </para>
        /// <para>
        /// The two <c>+0x1C</c>s are different records and both are corroborated
        /// independently: the item instance's <c>+0x1C</c> std-definition pointer
        /// is the same one NativeItemMerge.cs already documents, and
        /// <c>word [StdItem+0x1C]</c> = DuraMax is the identical idiom the
        /// experience granter uses for its own duration
        /// (<c>0x786480 mov eax,[edi+0x1C]</c> / <c>0x786483 movzx eax,word
        /// [eax+0x1C]</c> / <c>0x78648E div 1000</c> / <c>0x786493 imul
        /// 0xE10</c>), which this file already models as hours-from-DuraMax. That
        /// granter also reads its multiplier from <c>byte [StdItem+0x17]</c> =
        /// AniCount with the 2..0x40 clamp at 0x7863D6/0x7863DB, matching
        /// <see cref="NativeResolveGrantMultiplier"/> — so the surrounding field
        /// order (StdMode 0x14, Shape 0x15, Weight 0x16, AniCount 0x17,
        /// DuraMax 0x1C) is pinned by two separate consumers, not assumed.
        /// <para>
        /// Native then emits the internal-forward notify
        /// (<c>0x78685D mov dx,0x227</c> / sub-param 3, value = seconds, no text
        /// and no colour) through <c>vmt+0x250</c> at 0x786867. That is the same
        /// client-refresh channel the sibling granters use; it carries no
        /// user-visible message, so there is nothing to reproduce beyond the
        /// state change.
        /// </para>
        /// </summary>
        /// <returns><c>true</c> exactly when native sets its success flag at
        /// <c>0x786851 mov byte [ebp-1],1</c>.</returns>
        internal bool GrantNativeColorSay(byte shape, ushort duraMax)
        {
            // 786813 cmp byte [ecx+0x178],0 / jne -> fail
            if (m_btRaceServer != 0) return false;
            // 78681E cmp dword [esi+0xBD4],0 / jne -> fail (non-stacking)
            if (m_nNativeThirdBuffSeconds != 0) return false;

            // 786835 div edi (unsigned, truncating) / 786837 imul eax,0x15180
            m_nNativeThirdBuffSeconds = unchecked(
                (duraMax / 1000) * NativeColorSayGrantUnitSeconds);
            // 786843 sub al,0x16 -- byte arithmetic, wraps rather than clamps
            m_btNativeColorSayTier = unchecked((byte)(shape - 0x16));
            return true;
        }
    }
}
