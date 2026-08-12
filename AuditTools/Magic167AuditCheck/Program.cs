using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Magic167AuditCheck
{
    class Program
    {
        static int Main(string[] args)
        {
            const string filePath = @"..\..\GameSvr\Players\TPlayObject.NativeMagic167.cs";

            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"ERROR: File not found: {Path.GetFullPath(filePath)}");
                return 1;
            }

            var content = File.ReadAllText(filePath);
            int assertionCount = 0;

            // Core assertions about the implementation
            var assertions = new[]
            {
                // State checks
                ("HasNativeActiveState(0x33)", "Must check mounted state 0x33"),
                ("HasNativeActiveState(0x3E)", "Must check frozen state 0x3E"),
                ("HasNativeActiveState(0x1A)", "Must check paralyzed state 0x1A"),
                ("HasNativeActiveState(0x18)", "Must check webbed state 0x18"),

                // Messages
                ("当前未骑马", "Must have unmounted error message"),
                ("当前处于凝冰状态", "Must have frozen error message"),
                ("当前处于麻痹状态", "Must have paralyzed error message"),
                ("当前处于蛛网状态", "Must have webbed error message"),

                // Constants
                ("cellDuration = 5000", "Cell duration must be 5000ms"),
                ("cooldownDuration = 300000", "Cooldown must be 300000ms (5 minutes)"),
                ("QueryNativeColdTime(167)", "Must query coldTime key 167"),
                ("ArmNativeColdTime(167", "Must arm coldTime key 167"),

                // Ring
                ("(  0,  3)", "Ring must contain offset (0, 3)"),
                ("(  3,  0)", "Ring must contain offset (3, 0)"),
                ("(  0, -3)", "Ring must contain offset (0, -3)"),
                ("( -3,  0)", "Ring must contain offset (-3, 0)"),
                ("( -3,  3)", "Ring must contain offset (-3, 3)"),
                ("(  3, -3)", "Ring must contain offset (3, -3)"),

                // Event creation
                ("new PrisonEvent", "Must create PrisonEvent"),
                ("Grobal2.ET_PRISON", "Must use ET_PRISON constant"),
                ("GetEvent(targetX, targetY, Grobal2.ET_PRISON)", "Must check for existing prison"),
                ("EventManager.AddEvent", "Must add event to manager"),
            };

            foreach (var (pattern, description) in assertions)
            {
                if (!content.Contains(pattern))
                {
                    Console.Error.WriteLine($"FAIL: {description}");
                    Console.Error.WriteLine($"      Missing pattern: {pattern}");
                    return 1;
                }
                assertionCount++;
            }

            // Verify ring has exactly 24 entries
            var ringLines = content.Split('\n')
                .SkipWhile(l => !l.Contains("var ringOffsets"))
                .Skip(1)
                .TakeWhile(l => !l.Contains("};"))
                .Where(l => l.Contains("(") && l.Contains(")"))
                .ToList();

            if (ringLines.Count != 24)
            {
                Console.Error.WriteLine($"FAIL: Ring must have exactly 24 entries, found {ringLines.Count}");
                return 1;
            }
            assertionCount++;

            Console.WriteLine($"PASS: All {assertionCount} assertions verified");
            Console.WriteLine("Magic 167 (画地为牢) implementation is correct");
            return 0;
        }
    }
}
