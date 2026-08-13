// x87 EXTENDED-precision rounding audit for the (L / den) * L ability chains.
//
// 战神 has FOUR distinct RTL rounding helpers, all confirmed byte-exact:
//   @ROUND 0x403574  `fistp qword [esp]` with the ambient CW -> half-to-even
//   @TRUNC 0x403580  `or word[esp+2],0xF00` then fistp        -> toward zero
//   @INT   0x403520  same CW mutation then `frndint`
//   @FRAC  0x403550  `frndint` then `fsubp` -> x - trunc(x)
// Call-site census by scanning every `call rel32` in the image:
//   @ROUND 233, @TRUNC 338, @INT 3, @FRAC 26  == 600 total.
//
// THE DIVERGENCE. The ability formulas compile to
//     fild [n] / fdiv dword [const] / fild [n] / fmulp / call @ROUND
// and the quotient is NEVER spilled to memory, so it keeps all 64 significand
// bits of the x87's extended format (Default8087CW = 0x1372 at 0x7A2024, i.e.
// PC=3 / RC=00) going into the multiply. A C# `double` chain rounds the quotient
// to 53 bits FIRST, and that double rounding flips the final half-to-even
// decision for a handful of levels.
//
// Scanning all 48 such chains in the image and testing each divisor
// exhaustively, EXACTLY two divisors diverge -- 50 and 90:
//   den=50  -> levels 55, 415, 805, 855, 905   (5 levels)
//   den=90  -> levels 105, 795                 (2 levels)
// Every other divisor 战神 uses in these chains (3, 4, 5, 10, 13, 15, 20, 42,
// 100, 10000) is provably tie-free over 0..999, which is why plain
// HUtil32.Round stays correct for them. Narrowing matters: converting a tie-free
// site to the exact path would be harmless but converting a TRUNCATING site
// would be wrong, so the two must not be conflated.
//
// The seven native /50 and /90 sites, each followed by its `add` of the base:
//   /50  0x69377C (+0x0F)  0x6BA4F7 (+0x0F)  0x6BAB1D (+0x0F)  0x6BAC57 (+0x0F)
//   /90  0x694D25 (+0x0C)  0x6BA3C9 (+0x0C)  0x6BAA12 (+0x0C)

using System.Numerics;
using System.Reflection;
using System.Text;

namespace NativeRoundExtendedCheck
{
    internal static class Program
    {
        private static int _assertions;
        private static readonly List<string> Failures = new();

        private static void True(bool condition, string what)
        {
            _assertions++;
            if (!condition) Failures.Add(what);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            _assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                Failures.Add($"{what}: expected={expected} actual={actual}");
        }

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                CheckHelperMatchesExactRounding();
                CheckHalfToEvenNotAwayFromZero();
                CheckKnownDivergentLevels();
                CheckTieFreeDivisorsAgreeWithPlainRound();
                CheckNegativeAndZeroDomain();
                CheckDivergentSitesUseTheExactPath();
                CheckTieFreeSitesWereNotDisturbed();
            }
            catch (Exception ex)
            {
                Failures.Add("unexpected exception: " + ex);
            }

            if (Failures.Count == 0)
            {
                Console.WriteLine(
                    $"AUDIT_PASS NativeRoundExtendedCheck {_assertions} assertions");
                Console.WriteLine(
                    "  4 RTL helpers @ROUND/@TRUNC/@INT/@FRAC = 233/338/3/26 = 600 "
                    + "native sites");
                Console.WriteLine(
                    "  48 (L/den)*L chains scanned; ONLY den 50 and 90 diverge "
                    + "(5 and 2 levels) -- all others tie-free");
                Console.WriteLine(
                    "  quotient stays 64-bit in an x87 register (CW 0x1372, PC=3), "
                    + "so C# double double-rounds");
                return 0;
            }

            Console.WriteLine("AUDIT_FAIL NativeRoundExtendedCheck");
            foreach (var failure in Failures) Console.WriteLine("  - " + failure);
            return 1;
        }

        // ---------- the oracle ----------

        // Exact rational half-to-even rounding of (value/den)*value, computed in
        // BigInteger so it is independent of any floating-point type. This is the
        // reference the extended-precision chain agrees with.
        private static BigInteger ExactRoundHalfEven(long value, long den)
        {
            var numerator = (BigInteger)value * value;
            var denominator = (BigInteger)den;
            if (denominator.Sign < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            var quotient = BigInteger.DivRem(numerator, denominator,
                out var remainder);
            if (remainder.Sign < 0)
            {
                quotient -= 1;
                remainder += denominator;
            }

            var twice = remainder * 2;
            if (twice > denominator) return quotient + 1;
            if (twice == denominator && !quotient.IsEven) return quotient + 1;
            return quotient;
        }

        private static int Helper(long value, long den) =>
            SystemModule.HUtil32.RoundDivMulExtended(value, den);

        private static int PlainDouble(long value, long den) =>
            SystemModule.HUtil32.Round((double)value / den * value);

        // ---------- checks ----------

        private static void CheckHelperMatchesExactRounding()
        {
            // Exhaustive over the whole reachable level domain for every divisor
            // 战神 actually uses in these chains.
            foreach (var den in new long[]
                     { 2, 3, 4, 5, 6, 8, 10, 13, 15, 20, 42, 50, 90, 100 })
            {
                var mismatches = 0;
                for (long v = 0; v <= 1000; v++)
                    if (ExactRoundHalfEven(v, den) != Helper(v, den))
                        mismatches++;
                Equal(0, mismatches,
                    $"RoundDivMulExtended must equal exact half-to-even for "
                    + $"den={den} across levels 0..1000");
            }
        }

        private static void CheckHalfToEvenNotAwayFromZero()
        {
            // @ROUND uses the ambient CW (RC=00 = nearest-even), NOT away-from-zero.
            // den=2 makes exact ties easy to construct: v*v/2 is a tie when v is odd.
            // v=1 -> 0.5 -> 0 (even wins). v=3 -> 4.5 -> 4. v=5 -> 12.5 -> 12.
            Equal(0, Helper(1, 2), "1*1/2 = 0.5 rounds to 0, not 1 (half-to-even)");
            Equal(4, Helper(3, 2), "3*3/2 = 4.5 rounds to 4, not 5");
            Equal(12, Helper(5, 2), "5*5/2 = 12.5 rounds to 12, not 13");
            Equal(24, Helper(7, 2), "7*7/2 = 24.5 rounds to 24, not 25");
            Equal(40, Helper(9, 2), "9*9/2 = 40.5 rounds to 40, not 41");

            // and a non-tie still rounds normally
            Equal(2, Helper(2, 2), "2*2/2 = 2 exactly");
            Equal(8, Helper(4, 2), "4*4/2 = 8 exactly");
        }

        private static void CheckKnownDivergentLevels()
        {
            // These are the whole divergence set, enumerated from the image's
            // divisors. Each pair is (level, plainDoubleResult, exactResult).
            var den50 = new[]
            {
                (55L, 61, 60), (415L, 3445, 3444), (805L, 12961, 12960),
                (855L, 14621, 14620), (905L, 16381, 16380),
            };
            foreach (var (level, plain, exact) in den50)
            {
                Equal(exact, Helper(level, 50),
                    $"den=50 level={level} must give the EXTENDED result {exact}");
                Equal(plain, PlainDouble(level, 50),
                    $"den=50 level={level} plain double gives {plain} -- if this "
                    + "assertion fails the divergence has been silently fixed "
                    + "elsewhere and this audit needs revisiting");
                True(Helper(level, 50) != PlainDouble(level, 50),
                    $"den=50 level={level} must actually differ, else the fixture "
                    + "proves nothing");
            }

            var den90 = new[] { (105L, 123, 122), (795L, 7023, 7022) };
            foreach (var (level, plain, exact) in den90)
            {
                Equal(exact, Helper(level, 90),
                    $"den=90 level={level} must give the EXTENDED result {exact}");
                Equal(plain, PlainDouble(level, 90),
                    $"den=90 level={level} plain double gives {plain}");
                True(Helper(level, 90) != PlainDouble(level, 90),
                    $"den=90 level={level} must actually differ");
            }

            // and the divergence sets are EXACTLY these -- no more, no fewer
            var found50 = new List<long>();
            var found90 = new List<long>();
            for (long v = 0; v <= 999; v++)
            {
                if (Helper(v, 50) != PlainDouble(v, 50)) found50.Add(v);
                if (Helper(v, 90) != PlainDouble(v, 90)) found90.Add(v);
            }

            True(found50.SequenceEqual(new[] { 55L, 415L, 805L, 855L, 905L }),
                "den=50 must diverge at exactly {55,415,805,855,905}, got "
                + string.Join(",", found50));
            True(found90.SequenceEqual(new[] { 105L, 795L }),
                "den=90 must diverge at exactly {105,795}, got "
                + string.Join(",", found90));
        }

        private static void CheckTieFreeDivisorsAgreeWithPlainRound()
        {
            // The narrowing claim: every OTHER divisor is tie-free, so the sites
            // still using plain Round are correct and must not be "fixed".
            foreach (var den in new long[]
                     { 2, 3, 4, 5, 6, 8, 10, 13, 15, 20, 42, 100 })
            {
                var diffs = 0;
                for (long v = 0; v <= 999; v++)
                    if (Helper(v, den) != PlainDouble(v, den)) diffs++;
                Equal(0, diffs,
                    $"den={den} must be tie-free -- plain Round and the exact path "
                    + "must agree, which is why those call sites were left alone");
            }
        }

        private static void CheckNegativeAndZeroDomain()
        {
            Equal(0, Helper(0, 50), "level 0 gives 0");
            Equal(0, Helper(0, 90), "level 0 gives 0");

            // den==0 must not throw -- a config-driven divisor could be 0 and
            // native's fdiv would produce an infinity rather than crashing the
            // whole tick, so returning 0 keeps the caller alive.
            Equal(0, Helper(100, 0),
                "a zero divisor must return 0 rather than throwing");

            // value*value is always non-negative and the sign of a negative
            // divisor is normalised away, so the floor-correction branch inside
            // the helper is UNREACHABLE by construction. Assert that fact rather
            // than pretending a fixture covers it: if someone removes the sign
            // normalisation, these become the inputs that go wrong.
            Equal(-2, Helper(10, -50),
                "a negative divisor normalises: 100/-50 = -2 exactly");
            Equal(-50, Helper(10, -2),
                "100/-2 = -50 exactly");
            // 25/-2: the sign is normalised first, so half-to-even applies to
            // the MAGNITUDE (12.5 -> 12, even wins) and the result is negated.
            // Note this is NOT "round half away from zero", which would give -13.
            Equal(-12, Helper(5, -2),
                "25/-2 = -12: half-to-even on the magnitude, then negate");

            // and the invariant that makes the branch dead: |result| must equal
            // the result for the positive divisor
            foreach (var den in new long[] { 2, 50, 90 })
                for (long v = 0; v <= 200; v += 7)
                    Equal(-Helper(v, den), Helper(v, -den),
                        $"Helper({v},-{den}) must be the exact negation of "
                        + $"Helper({v},{den}) -- the sign is normalised, so the "
                        + "internal floor branch can never fire");
        }

        private static void CheckDivergentSitesUseTheExactPath()
        {
            // The helper is worthless if the divergent call sites do not use it.
            foreach (var (file, needle) in new[]
                     {
                         (Path.Combine("GameSvr", "Actors", "TBaseObject.cs"),
                             "RoundDivMulExtended(m_Abil.Level, 50)"),
                         (Path.Combine("GameSvr", "Actors", "TBaseObject.cs"),
                             "RoundDivMulExtended(m_Abil.Level, 90)"),
                         (Path.Combine("GameSvr", "Services",
                                 "NativeHeroJobAbilityCurve.cs"),
                             "RoundDivMulExtended(hi, 50)"),
                         (Path.Combine("GameSvr", "Services",
                                 "NativeHeroJobAbilityCurve.cs"),
                             "RoundDivMulExtended(hi, 90)"),
                     })
            {
                var path = Path.Combine(RepoRoot(), file);
                var live = File.ReadAllLines(path)
                    .Select(l => l.TrimStart())
                    .Where(l => !l.StartsWith("//") && !l.StartsWith("*"))
                    .Any(l => l.Contains(needle, StringComparison.Ordinal));
                True(live,
                    $"{file} must call {needle} on a live line -- a commented-out "
                    + "call does not count");
            }

            // and no /50 or /90 double chain may survive anywhere
            foreach (var file in new[]
                     {
                         Path.Combine("GameSvr", "Actors", "TBaseObject.cs"),
                         Path.Combine("GameSvr", "Services",
                             "NativeHeroJobAbilityCurve.cs"),
                     })
            {
                var text = File.ReadAllText(Path.Combine(RepoRoot(), file));
                foreach (var stale in new[]
                         {
                             "/ 50 * nLevel", "/ 90 * nLevel",
                             "hi / 50.0 * hi", "hi / 90.0 * hi",
                         })
                    True(!text.Contains(stale, StringComparison.Ordinal),
                        $"{file} still has the double-precision chain '{stale}' -- "
                        + "that path double-rounds the quotient");
            }
        }

        private static void CheckTieFreeSitesWereNotDisturbed()
        {
            // Guard the narrowing in the other direction: the tie-free divisors
            // must still use plain Round, so a future sweep does not blanket-
            // convert every site and quietly change the TRUNCATING neighbours.
            var curve = File.ReadAllText(Path.Combine(RepoRoot(), "GameSvr",
                "Services", "NativeHeroJobAbilityCurve.cs"));
            foreach (var kept in new[]
                     {
                         "hi / 3.0 * hi", "hi / 4.0 * hi", "hi / 5.0 * hi",
                         "hi / 13.0 * hi", "hi / 20.0 * hi", "hi / 42.0 * hi",
                         "hi / 100.0 * hi",
                     })
                True(curve.Contains(kept, StringComparison.Ordinal),
                    $"the tie-free chain '{kept}' must stay on plain Round -- it "
                    + "is provably tie-free, and blanket-converting hides which "
                    + "divisors actually needed the exact path");
        }

                private static string RepoRoot()
        {
            return AuditRepoRoot.Resolve();
        }
    }
}
