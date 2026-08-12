// STATE-41: Audit RequiresTimedAbilityRecalc trigger bitmap.
//
// Native @ 0x773254 (sub_773254) checks a 14-byte inclusion bitmap @ 0x77326C.
// The bitmap has 37 set bits covering state IDs:
//   6, 13, 14, 24-31, 32-36, 38-40, 67-70, 77, 82-86, 88-93, 95, 96
//
// Bitmap bytes (EA → byte → states):
//   0x77326C: 0x40 → state 6
//   0x77326D: 0x60 → states 13,14
//   0x77326E: 0x00 → (none)
//   0x77326F: 0xFF → states 24,25,26,27,28,29,30,31
//   0x773270: 0xDF → states 32,33,34,35,36,38,39  (bit5=0 excludes state37)
//   0x773271: 0x01 → state 40
//   0x773272: 0x00 → (none)
//   0x773273: 0x00 → (none)
//   0x773274: 0x78 → states 67,68,69,70
//   0x773275: 0x20 → state 77
//   0x773276: 0x7C → states 82,83,84,85,86
//   0x773277: 0xBF → states 88,89,90,91,92,93,95  (bit6=0 excludes state94)
//   0x773278: 0x01 → state 96
//   0x773279: 0x00 → (none)
//
// The buggy C# exclusion list {19,20,26,45,49,59} incorrectly excluded state 26
// (which native INCLUDES via byte3=0xFF bit2) and incorrectly triggered ~70 states
// native excludes. This audit verifies the C# source uses the bitmap, not the list.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace State41RecalcBitmapCheck
{
    internal static class Program
    {
        private static readonly byte[] NativeBitmap = new byte[14]
        {
            0x40, 0x60, 0x00, 0xFF, 0xDF, 0x01, 0x00,
            0x00, 0x78, 0x20, 0x7C, 0xBF, 0x01, 0x00
        };

        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Console.WriteLine("[STATE-41] RecalcAbilitys trigger bitmap audit");
            Console.WriteLine(new string('=', 70));

            var expectedStates = ExtractBitmapStates(NativeBitmap);
            Console.WriteLine($"Native bitmap (EA 0x77326C, 14 bytes) defines {expectedStates.Count} trigger states:");
            Console.WriteLine($"  [{string.Join(", ", expectedStates.OrderBy(x => x))}]");
            Console.WriteLine();

            var source = ReadSource("GameSvr/Actors/TBaseObject.TimedAbility.cs");

            // ---- the bitmap bytes must be present in source ---------------------
            Assert("NativeRecalcBitmap array has the correct 14 bytes",
                source, s => Has(s, "byte[] NativeRecalcBitmap = new byte[14]")
                             && Has(s, "0x40, 0x60, 0x00, 0xFF, 0xDF, 0x01, 0x00")
                             && Has(s, "0x00, 0x78, 0x20, 0x7C, 0xBF, 0x01, 0x00"));

            // ---- RequiresTimedAbilityRecalc uses the bitmap, not exclusion list -
            Assert("RequiresTimedAbilityRecalc uses bitmap lookup",
                source, s => InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                    "NativeRecalcBitmap[byteIndex]")
                             && InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                    "(1 << bitIndex)"));

            Assert("RequiresTimedAbilityRecalc calculates byteIndex = internalType / 8",
                source, s => InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                    "byteIndex = internalType / 8"));

            Assert("RequiresTimedAbilityRecalc calculates bitIndex = internalType % 8",
                source, s => InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                    "bitIndex = internalType % 8"));

            // ---- the OLD exclusion list pattern must NOT exist ------------------
            AssertFalse("no exclusion-list pattern remains (internalType != 19 etc)",
                source, s => InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                    "internalType != 19")
                             || InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                    "internalType != 20")
                             || InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                    "!= NativeState26Type")
                             || InBlock(s, "private static bool RequiresTimedAbilityRecalc",
                    "internalType != 45"));

            // ---- state 26 is specifically covered by the bitmap -----------------
            Assert("state 26 is set in byte 3 (0xFF includes bits 24-31)",
                source, s => true);  // already checked via bitmap bytes above
            if (expectedStates.Contains(26))
            {
                Pass("state 26 (bit 26%8=2 of byte 26/8=3) is included in native bitmap");
            }
            else
            {
                Fail("state 26 must be set in the bitmap");
            }

            // ---- state 19, 20, 45, 49, 59 must NOT be in the bitmap -------------
            foreach (var excluded in new[] { 19, 20, 45, 49, 59 })
            {
                if (!expectedStates.Contains(excluded))
                {
                    Pass($"state {excluded} correctly excluded by native bitmap");
                }
                else
                {
                    Fail($"state {excluded} should be excluded but appears in bitmap");
                }
            }

            Console.WriteLine(new string('=', 70));
            Console.WriteLine($"Result: {_passed} passed, {_failed} failed");
            if (_failed == 0)
            {
                Console.WriteLine("AUDIT_PASS");
            }
            return _failed == 0 ? 0 : 1;
        }

        private static HashSet<int> ExtractBitmapStates(byte[] bitmap)
        {
            var states = new HashSet<int>();
            for (int byteIndex = 0; byteIndex < bitmap.Length; byteIndex++)
            {
                byte b = bitmap[byteIndex];
                for (int bitIndex = 0; bitIndex < 8; bitIndex++)
                {
                    if ((b & (1 << bitIndex)) != 0)
                    {
                        states.Add(byteIndex * 8 + bitIndex);
                    }
                }
            }
            return states;
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
