// Magic id 167 (画地为牢), native sub_6EEE70.
//
// This audit used to open ..\..\GameSvr\Players\TPlayObject.NativeMagic167.cs with a path
// relative to the current directory and match the spelling of that file's source. Both halves
// rotted: the implementation moved to GameSvr/Actors/TBaseObject.NativeSkill167Prison.cs, and
// the relative path resolved to nothing under any harness that does not launch the tool from
// its own project folder, so it reported FAIL without reading a line. Twenty of its twenty-two
// probes were spellings the rewrite no longer uses (cellDuration = 5000 became a named
// constant 0x1388, Grobal2.ET_PRISON became 0x1D, the ring became brace-initialised, and so
// on) -- none of which says anything about behaviour.
//
// Everything asserted below is now a value read out of flat_image.bin at ImageBase 0x400000:
//
//   0x6EEE7F  B2 33              mov dl,0x33     ; required state, probe INVERTED (jne skips
//                                                ; the refusal, so 0x33 must be present)
//   0x6EEEA4  B2 3E              mov dl,0x3E     ; frozen
//   0x6EEEC9  B2 1A              mov dl,0x1A     ; paralysed
//   0x6EEEEE  B2 18              mov dl,0x18     ; webbed
//   0x6EEE8C/0x6EEEB1/0x6EEED6/0x6EEEFB  66 B9 FF FC  mov cx,0xFCFF   ; hint colour
//   0x6EEF13  C7 45 EC 88 13 00 00  mov [ebp-0x14],0x1388   ; per-cell lifetime, 5000 ms
//   0x6EEF1A  BA A7 00 00 00     mov edx,0xA7    ; coldtime key 167
//   0x6EEF33  mov ecx,0x493E0                    ; cooldown, 300000 ms
//   0x7198E4  push 0x1D          cell event type, same literal as the search key at 0x6EEF64
//   0x7D3CE4  24 records of two dwords: the Chebyshev radius-3 ring, listed below
//
// The four state probes all route through 0x772960, the flat per-object state bitset.

using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Magic167AuditCheck
{
    internal static class Program
    {
        // 0x7D3CE4, read raw: starts at (0,+3) and winds clockwise. The order is load-bearing
        // because the loop claims cells first-come-first-served, so a ring with the same 24
        // members in a different rotation lands differently once part of it is occupied.
        private static readonly int[,] NativeRing =
        {
            {  0,  3 }, {  1,  3 }, {  2,  3 }, {  3,  3 },
            {  3,  2 }, {  3,  1 }, {  3,  0 }, {  3, -1 },
            {  3, -2 }, {  3, -3 }, {  2, -3 }, {  1, -3 },
            {  0, -3 }, { -1, -3 }, { -2, -3 }, { -3, -3 },
            { -3, -2 }, { -3, -1 }, { -3,  0 }, { -3,  1 },
            { -3,  2 }, { -3,  3 }, { -2,  3 }, { -1,  3 }
        };

        private static int _failed;

        private static int Main(string[] args)
        {
            var path = FindSource(args, Path.Combine("GameSvr", "Actors",
                "TBaseObject.NativeSkill167Prison.cs"));
            if (path == null)
            {
                Console.Error.WriteLine(
                    "ERROR: GameSvr/Actors/TBaseObject.NativeSkill167Prison.cs not found from "
                    + Directory.GetCurrentDirectory() + " or " + AppContext.BaseDirectory);
                return 1;
            }

            var source = File.ReadAllText(path);

            ConstantIs(source, "NativeSkill167ColdTimeKey", 0xA7, "0x6EEF1A mov edx,0xA7");
            ConstantIs(source, "NativeSkill167CooldownMilliseconds", 0x493E0,
                "0x6EEF33 mov ecx,0x493E0");
            ConstantIs(source, "NativeSkill167CellMilliseconds", 0x1388,
                "0x6EEF13 mov [ebp-0x14],0x1388");
            ConstantIs(source, "NativeSkill167CellEventType", 0x1D, "0x7198E4 push 0x1D");
            ConstantIs(source, "NativeSkill167RequiredState", 0x33, "0x6EEF7F mov dl,0x33");
            ConstantIs(source, "NativeSkill167HintColorLow", 0xFF, "mov cx,0xFCFF low byte");
            ConstantIs(source, "NativeSkill167HintColorHigh", 0xFC, "mov cx,0xFCFF high byte");

            // The 0x33 probe is inverted; the other three refuse when the state is present.
            Require(source, @"if\s*\(\s*!\s*HasNativeActiveState\(\s*NativeSkill167RequiredState\s*\)\s*\)",
                "0x6EEE7F: the mounted probe must be inverted (jne skips the refusal)");
            foreach (var state in new[] { "0x3E", "0x1A", "0x18" })
            {
                Require(source, @"if\s*\(\s*HasNativeActiveState\(\s*" + state + @"\s*\)\s*\)",
                    "missing non-inverted state probe " + state);
            }

            foreach (var text in new[]
                     {
                         "当前未骑马", "当前处于凝冰状态",
                         "当前处于麻痹状态", "当前处于蛛网状态"
                     })
            {
                Require(source, Regex.Escape(text), "missing refusal message " + text);
            }

            // 0x6EEF23 VMT+0x1F4 then 0x6EEF2B `jne 0x6EEFB8` lands on the exit with ebx still
            // 0: the cooldown refusal sends nothing, unlike all four state refusals.
            Require(source,
                @"GetNativeColdTimeRemaining\(NativeSkill167ColdTimeKey\)\s*!=\s*0\s*\)\s*\{\s*return false;",
                "cooldown refusal must be silent (no hint before the return)");
            Require(source,
                @"SetNativeColdTime\(NativeSkill167ColdTimeKey,\s*NativeSkill167CooldownMilliseconds",
                "cooldown must be armed with the native key and duration");

            // 0x6EEFA9 call sub_7199B8: an occupied cell is restarted, never stacked.
            Require(source, @"RefreshOpenStartTick\(", "occupied cell must be refreshed");
            Require(source, @"new PrisonEvent\(", "cell event must be a PrisonEvent");
            Require(source, @"EventManager\.AddEvent\(", "cell event must be registered");

            // 0x6EEFB6 `mov bl,1` sits after the loop, so nothing inside it can fail the cast:
            // the only refusals are the four state probes and the cooldown, all before it.
            var refusals = Regex.Matches(source, @"return false;").Count;
            if (refusals != 5)
                Fail($"expected 5 refusals (0x33/0x3E/0x1A/0x18 + cooldown), found {refusals}: "
                     + "a per-cell placement failure must not fail the cast (0x6EEFB6)");

            CheckRing(source);

            if (_failed != 0)
            {
                Console.Error.WriteLine($"Magic167AuditCheck FAIL ({_failed} assertions)");
                return 1;
            }

            Console.WriteLine(
                "Magic167AuditCheck PASS states=0x33-inverted/0x3E/0x1A/0x18 colour=0xFCFF "
                + "coldtime=0xA7/0x493E0 cell=0x1388/type-0x1D ring=0x7D3CE4/24-clockwise "
                + "occupied=refresh cooldown=silent");
            return 0;
        }

        private static void CheckRing(string source)
        {
            var start = source.IndexOf("NativeSkill167Ring", StringComparison.Ordinal);
            var open = start < 0 ? -1 : source.IndexOf('{', start);
            var end = open < 0 ? -1 : source.IndexOf("};", open, StringComparison.Ordinal);
            if (end < 0)
            {
                Fail("ring table NativeSkill167Ring was not found");
                return;
            }

            var matches = Regex.Matches(source.Substring(open, end - open),
                @"\{\s*(-?\d+)\s*,\s*(-?\d+)\s*\}");
            if (matches.Count != NativeRing.GetLength(0))
            {
                Fail($"ring must hold {NativeRing.GetLength(0)} offsets (0x7D3CE4), "
                     + $"found {matches.Count}");
                return;
            }

            for (var index = 0; index < matches.Count; index++)
            {
                var x = int.Parse(matches[index].Groups[1].Value, CultureInfo.InvariantCulture);
                var y = int.Parse(matches[index].Groups[2].Value, CultureInfo.InvariantCulture);
                if (x == NativeRing[index, 0] && y == NativeRing[index, 1]) continue;
                Fail($"ring offset {index} is ({x},{y}), 0x7D3CE4 record {index} is "
                     + $"({NativeRing[index, 0]},{NativeRing[index, 1]})");
                return;
            }
        }

        private static void ConstantIs(string source, string name, int expected, string evidence)
        {
            var match = Regex.Match(source,
                Regex.Escape(name)
                + @"\s*=\s*(0[xX][0-9A-Fa-f]+|-?\d+|Grobal2\.[A-Za-z_][A-Za-z0-9_]*)\s*;");
            if (!match.Success)
            {
                Fail($"constant {name} was not found ({evidence})");
                return;
            }
            var text = match.Groups[1].Value;
            if (!TryReadInt(text, out var actual))
            {
                Fail($"{name} is {text}, whose value could not be resolved ({evidence})");
                return;
            }
            if (actual != expected)
                Fail($"{name} is {text} but {evidence} gives 0x{expected:X}");
        }

        // The right-hand side may be a literal or a published Grobal2 constant
        // (c7600445 moved the prison event type onto Grobal2.ET_PRISON). Chase the
        // symbol into Grobal2.cs so the numeric assertion still bites.
        private static bool TryReadInt(string text, out int value)
        {
            if (text.StartsWith("Grobal2.", StringComparison.Ordinal))
            {
                var symbol = text.Substring("Grobal2.".Length);
                var grobal2 = _grobal2Source ??= ReadGrobal2Source();
                if (grobal2 == null)
                {
                    value = 0;
                    return false;
                }
                var declaration = Regex.Match(grobal2,
                    @"\b(?:const|static\s+readonly)\s+\w+\s+"
                    + Regex.Escape(symbol)
                    + @"\s*=\s*(0[xX][0-9A-Fa-f]+|-?\d+)\s*;");
                if (!declaration.Success)
                {
                    value = 0;
                    return false;
                }
                text = declaration.Groups[1].Value;
            }
            value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.Parse(text.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture)
                : int.Parse(text, CultureInfo.InvariantCulture);
            return true;
        }

        private static string _grobal2Source;

        private static string ReadGrobal2Source()
        {
            var path = FindSource(Array.Empty<string>(),
                Path.Combine("SystemModule", "Grobal2.cs"));
            return path == null ? null : File.ReadAllText(path);
        }

        private static void Require(string source, string pattern, string message)
        {
            if (!Regex.IsMatch(source, pattern, RegexOptions.Singleline))
                Fail(message);
        }

        private static void Fail(string message)
        {
            Console.Error.WriteLine("FAIL: " + message);
            _failed++;
        }

        private static string FindSource(string[] args, string relativePath)
        {
            var starts = new[]
            {
                args.Length > 0 ? args[0] : null,
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory
            };
            foreach (var start in starts)
            {
                if (string.IsNullOrEmpty(start)) continue;
                for (var directory = new DirectoryInfo(Path.GetFullPath(start));
                     directory != null; directory = directory.Parent)
                {
                    var candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }
    }
}
