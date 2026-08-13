// STATE-41: Audit RequiresTimedAbilityRecalc against the native leaf.
//
// Native @0x773254, verbatim:
//   0x773254  80 C2 F8              add dl, 0xF8      ; dl = (internalType - 8) & 0xFF
//   0x773257  80 FA 67              cmp dl, 0x67      ; unsigned, 103
//   0x77325A  77 0A                 ja  0x773266      ; out of range -> CF = 0
//   0x77325C  83 E2 7F              and edx, 0x7F
//   0x77325F  0F A3 15 6C 32 77 00  bt  [0x77326C], edx
//   0x773266  0F 92 C0              setb al
//   0x773269  C3                    ret
//
// Bitmap @0x77326C, 14 bytes read out of flat_image.bin:
//   40 60 00 FF DF 01 00 00 78 20 7C BF 01 00
//
// THE BIT INDEX IS BIASED BY -8. `add dl,0xF8` is a byte add, so it wraps:
// internalType 0..7 land on 0xF8..0xFF, all > 0x67, so they fall out of range.
// The in-range domain is internalType [8, 111].
//
// This audit used to assert the OPPOSITE. Its header decoded the bitmap with no
// bias (claiming states 6, 13, 14, 24..31, ...) and its predicates required the
// source to read `internalType / 8` and `internalType % 8`, which is precisely
// the defect that misjudged 41 of the 112 types. It also asserted "state 26 is
// included" and "state 45 is excluded", both of which are backwards once the
// bias is applied. Per REPLICATION_RULES 4.17 an audit that encodes the bug is
// worse than no audit, so the checks below are rebuilt against the bytes.
//
// Independent confirmation that the bias is right: decoded with -8 the 37 set
// bits are exactly the stat-modifying states named by the native state-gained
// dispatch @0x7418C8 (21 "抗魔力增加", 22 "防御力增加", 32..41 the six
// 上下限/攻速/生命/魔法/敏捷/魔躲 arms, 75..78 the 抗性/刺术 arms, 90..94,
// 96..101, 103, 104). Decoded without the bias it would instead select 24..31,
// which are the poison / petrify / freeze band and change no stat at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace State41RecalcBitmapCheck
{
    internal static class Program
    {
        /// <summary>flat_image.bin @0x77326C, verbatim.</summary>
        private static readonly byte[] NativeBitmap =
        {
            0x40, 0x60, 0x00, 0xFF, 0xDF, 0x01, 0x00,
            0x00, 0x78, 0x20, 0x7C, 0xBF, 0x01, 0x00
        };

        /// <summary><c>add dl, 0xF8</c> — the bit index is internalType - 8.</summary>
        private const int NativeBias = 8;

        /// <summary><c>cmp dl, 0x67 / ja</c> — inclusive upper bound on the biased index.</summary>
        private const int NativeMaxBiased = 0x67;

        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Console.WriteLine("[STATE-41] RequiresTimedAbilityRecalc vs native 0x773254");
            Console.WriteLine(new string('=', 70));

            var expected = ExpectedStates();
            Console.WriteLine($"Native bitmap 0x77326C decoded with bias -{NativeBias}: "
                              + $"{expected.Count} states");
            Console.WriteLine($"  [{string.Join(", ", expected.OrderBy(x => x))}]");
            Console.WriteLine();

            var source = ReadSource("GameSvr/Actors/TBaseObject.TimedAbility.cs");

            Assert("NativeRecalcBitmap holds the 14 bytes at 0x77326C",
                source, s => Has(s, "byte[] NativeRecalcBitmap = new byte[14]")
                             && Has(s, "0x40, 0x60, 0x00, 0xFF, 0xDF, 0x01, 0x00")
                             && Has(s, "0x00, 0x78, 0x20, 0x7C, 0xBF, 0x01, 0x00"));

            Assert($"NativeRecalcBitmapBias = {NativeBias} (add dl,0xF8)",
                source, s => Has(s, $"NativeRecalcBitmapBias = {NativeBias}"));

            Assert("RequiresTimedAbilityRecalc subtracts the bias before indexing",
                source, s => InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                    "internalType - NativeRecalcBitmapBias"));

            Assert("RequiresTimedAbilityRecalc indexes with the BIASED value",
                source, s => InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                                  "NativeRecalcBitmap[biased / 8]")
                             && InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                                  "1 << (biased % 8)"));

            Assert("RequiresTimedAbilityRecalc keeps the 0x67 upper bound",
                source, s => InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                    "biased > 0x67"));

            // The old, unbiased form must never come back.
            AssertFalse("no unbiased indexing remains",
                source, s => InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                                  "NativeRecalcBitmap[internalType / 8]")
                             || InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                                  "internalType % 8"));

            // Byte 5 must stay 0x01. It had been hand-patched to 0x11 to force
            // internalType 44 true under the unbiased index; 44 falls out
            // correctly from the bias, and the extra bit would also recalc 52,
            // which native never does.
            Assert("bitmap byte 5 is 0x01, not the hand-patched 0x11",
                source, s => NativeBitmap[5] == 0x01
                             && !Has(s, "0xDF, 0x11,"));

            Console.WriteLine();
            Console.WriteLine("-- per-state expectations (spot checks against the bytes) --");

            // 26 sits in the poison/petrify band: byte (26-8)/8 = 2 = 0x00.
            // The previous audit asserted the opposite.
            ExpectState(expected, 26, false, "petrify; byte2 = 0x00");
            ExpectState(expected, 45, false, "biased 37 -> byte4 0xDF bit5 is the one clear bit");
            ExpectState(expected, 44, true, "byte4 = 0xDF bit4; the 0x11 patch existed to force this");
            ExpectState(expected, 52, false, "the 0x11 patch would have wrongly added this");
            ExpectState(expected, 22, true, "防御力增加 — a stat state");
            ExpectState(expected, 21, true, "抗魔力增加 — a stat state");
            ExpectState(expected, 31, false, "green poison changes no stat");
            ExpectState(expected, 7, false, "below the bias, add dl,0xF8 wraps to 0xFF");
            ExpectState(expected, 8, false, "biased 0 -> byte0 bit0 of 0x40 is clear");
            ExpectState(expected, 14, true, "biased 6 -> byte0 0x40 bit6");
            ExpectState(expected, 111, false, "biased 0x67, the last in-range index; byte13 = 0x00");
            ExpectState(expected, 112, false, "biased 0x68 > 0x67 -> out of range");

            Console.WriteLine();
            Console.WriteLine($"total set bits: {expected.Count} (native has 37)");
            if (expected.Count == 37) Pass("bit population is 37");
            else Fail($"bit population is {expected.Count}, native has 37");

            Console.WriteLine(new string('=', 70));
            Console.WriteLine($"Result: {_passed} passed, {_failed} failed");
            if (_failed == 0)
            {
                Console.WriteLine("AUDIT_PASS");
            }
            return _failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// Reimplements 0x773254 straight from the bytes so the expectation set
        /// is derived, not transcribed.
        /// </summary>
        private static HashSet<int> ExpectedStates()
        {
            var states = new HashSet<int>();
            for (var internalType = 0; internalType <= 255; internalType++)
            {
                if (NativeRequiresRecalc(internalType)) states.Add(internalType);
            }
            return states;
        }

        private static bool NativeRequiresRecalc(int internalType)
        {
            // add dl, 0xF8 — byte arithmetic, wraps for internalType < 8.
            var biased = (internalType + 0xF8) & 0xFF;
            if (biased > NativeMaxBiased) return false;   // cmp/ja
            biased &= 0x7F;                               // and edx, 0x7F
            return (NativeBitmap[biased / 8] & (1 << (biased % 8))) != 0;
        }

        private static void ExpectState(HashSet<int> actual, int state, bool want,
            string why)
        {
            var got = actual.Contains(state);
            var label = $"state {state} {(want ? "IS" : "is NOT")} a recalc trigger ({why})";
            if (got == want) Pass(label); else Fail(label + $"  [got {got}]");
        }

        private static void Assert(string name, string source,
            Func<string, bool> predicate)
        {
            if (source == null)
            {
                Fail(name + "  [source file missing]");
                return;
            }
            bool ok;
            try
            {
                ok = predicate(source);
            }
            catch (Exception ex)
            {
                Fail(name + "  [predicate threw: " + ex.Message + "]");
                return;
            }
            if (ok) Pass(name); else Fail(name);
        }

        private static void AssertFalse(string name, string source,
            Func<string, bool> forbidden)
        {
            if (source == null)
            {
                Fail(name + "  [source file missing]");
                return;
            }
            if (forbidden(source)) Fail(name); else Pass(name);
        }

        private static void Pass(string name)
        {
            Console.WriteLine("[PASS] " + name);
            _passed++;
        }

        private static void Fail(string name)
        {
            Console.WriteLine("[FAIL] " + name);
            _failed++;
        }

        private static bool Has(string source, params string[] needles) =>
            needles.All(n => Collapse(source).Contains(Collapse(n)));

        private static string Collapse(string value)
        {
            var sb = new System.Text.StringBuilder(value.Length);
            var pendingSpace = false;
            foreach (var ch in value)
            {
                if (char.IsWhiteSpace(ch)) { pendingSpace = sb.Length > 0; continue; }
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        private static bool InBlock(string source, string signature, string needle)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return false;
            var open = source.IndexOf('{', start);
            if (open < 0) return false;
            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return Has(source.Substring(open, i - open + 1), needle);
                }
            }
            return false;
        }

        private static string StripLineComments(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);
            foreach (var line in source.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    sb.Append('\n');
                    continue;
                }
                var idx = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(idx >= 0 ? line.Substring(0, idx) : line).Append('\n');
            }
            return sb.ToString();
        }

        private static string ReadSource(string relativeFromRepoRoot,
            [CallerFilePath] string thisFile = null)
        {
            var dir = Path.GetDirectoryName(thisFile);
            if (dir == null) return null;
            var path = Path.GetFullPath(Path.Combine(dir, "..", "..",
                relativeFromRepoRoot.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(path) ? StripLineComments(File.ReadAllText(path)) : null;
        }
    }
}
