using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Magic213Audit
{
    class Program
    {
        static int Main(string[] args)
        {
            var repoRoot = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
            var magicManagerPath = Path.Combine(repoRoot, "GameSvr", "Spells", "MagicManager.cs");

            if (!File.Exists(magicManagerPath))
            {
                Console.WriteLine($"FAIL: MagicManager.cs not found at {magicManagerPath}");
                return 1;
            }

            var content = File.ReadAllText(magicManagerPath);
            var lines = File.ReadAllLines(magicManagerPath);

            // Assertion 1: SKILL_213 must appear exactly once in a case statement
            var skill213CasePattern = @"case\s+SpellsDef\.SKILL_213\s*:";
            var skill213Matches = Regex.Matches(content, skill213CasePattern);

            if (skill213Matches.Count != 1)
            {
                Console.WriteLine($"FAIL: Expected exactly 1 case for SKILL_213, found {skill213Matches.Count}");
                return 1;
            }
            Console.WriteLine($"PASS: SKILL_213 appears in exactly 1 case statement");

            // Assertion 2: SKILL_213 must be adjacent to SKILL_GROUPAMYOUNSUL (magic 48)
            var groupAmyunsulPattern = @"case\s+SpellsDef\.SKILL_GROUPAMYOUNSUL\s*:";
            var groupAmyunsulMatches = Regex.Matches(content, groupAmyunsulPattern);

            if (groupAmyunsulMatches.Count != 1)
            {
                Console.WriteLine($"FAIL: Expected exactly 1 case for SKILL_GROUPAMYOUNSUL, found {groupAmyunsulMatches.Count}");
                return 1;
            }

            // Find line numbers for both cases
            int skill213Line = -1;
            int groupAmyunsulLine = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], skill213CasePattern))
                    skill213Line = i;
                if (Regex.IsMatch(lines[i], groupAmyunsulPattern))
                    groupAmyunsulLine = i;
            }

            if (skill213Line == -1 || groupAmyunsulLine == -1)
            {
                Console.WriteLine($"FAIL: Could not find line numbers for cases");
                return 1;
            }

            // They must be adjacent (within 1 line of each other, allowing for either order)
            var distance = Math.Abs(skill213Line - groupAmyunsulLine);
            if (distance != 1)
            {
                Console.WriteLine($"FAIL: SKILL_213 (line {skill213Line}) and SKILL_GROUPAMYOUNSUL (line {groupAmyunsulLine}) are not adjacent (distance={distance})");
                return 1;
            }
            Console.WriteLine($"PASS: SKILL_213 and SKILL_GROUPAMYOUNSUL are adjacent (lines {Math.Min(skill213Line, groupAmyunsulLine)}-{Math.Max(skill213Line, groupAmyunsulLine)})");

            // Assertion 3: Both must reach MagGroupAmyounsul before hitting a break
            var startLine = Math.Min(skill213Line, groupAmyunsulLine);
            var foundMagGroupAmyounsul = false;
            var foundBreak = false;

            for (int i = startLine; i < lines.Length && i < startLine + 10; i++)
            {
                if (lines[i].Contains("MagGroupAmyounsul"))
                {
                    foundMagGroupAmyounsul = true;
                }
                if (Regex.IsMatch(lines[i].TrimStart(), @"^\s*break\s*;"))
                {
                    foundBreak = true;
                    break;
                }
            }

            if (!foundMagGroupAmyounsul)
            {
                Console.WriteLine($"FAIL: MagGroupAmyounsul not found after case statements");
                return 1;
            }
            Console.WriteLine($"PASS: MagGroupAmyounsul call found");

            if (!foundBreak)
            {
                Console.WriteLine($"FAIL: break statement not found after MagGroupAmyounsul");
                return 1;
            }
            Console.WriteLine($"PASS: break statement found after MagGroupAmyounsul");

            // Assertion 4: No other case SKILL_213 should exist (no BLOCKED stub)
            var blockedPattern = @"BLOCKED.*213|213.*BLOCKED";
            if (Regex.IsMatch(content, blockedPattern, RegexOptions.IgnoreCase))
            {
                Console.WriteLine($"FAIL: Found BLOCKED comment mentioning 213, stub may still exist");
                return 1;
            }
            Console.WriteLine($"PASS: No BLOCKED stub found for magic 213");

            Console.WriteLine($"\n=== ALL ASSERTIONS PASSED (4/4) ===");
            return 0;
        }
    }
}
