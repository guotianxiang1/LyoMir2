using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Drop39WeightPolarityCheck
{
    /// <summary>
    /// DROP-39 audit: IsAddWeightAvailable must use strict less-than (&lt;), not &lt;=.
    /// Native sub_73C950 @ 0x73C950 uses setl (strict &lt;) after cmp Weight vs MaxWeight.
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            var rootPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var targetFile = Path.Combine(rootPath, "GameSvr", "Players", "TPlayObject.cs");

            if (!File.Exists(targetFile))
            {
                Console.WriteLine($"BLOCKED: {targetFile} not found");
                return 2;
            }

            var content = File.ReadAllText(targetFile);
            var lines = File.ReadAllLines(targetFile);

            // Find IsAddWeightAvailable method
            var methodPattern = new Regex(
                @"public\s+bool\s+IsAddWeightAvailable\s*\([^)]*\)\s*\{[^}]*\}",
                RegexOptions.Singleline);
            var match = methodPattern.Match(content);

            if (!match.Success)
            {
                Console.WriteLine("FAIL: IsAddWeightAvailable method not found");
                return 1;
            }

            var methodBody = match.Value;

            // Extract just the return statement (skip comments)
            var returnMatch = Regex.Match(methodBody, @"return\s+[^;]+;");
            if (!returnMatch.Success)
            {
                Console.WriteLine("FAIL: No return statement found in IsAddWeightAvailable");
                return 1;
            }

            var returnStatement = returnMatch.Value;

            // Must use strict < (not <=) in the actual return statement
            if (returnStatement.Contains("<="))
            {
                Console.WriteLine("FAIL: IsAddWeightAvailable uses <= but native uses strict <");
                Console.WriteLine("      Native sub_73C950 @ 0x73C950 uses setl (set if less)");
                Console.WriteLine($"      Found: {returnStatement.Trim()}");
                return 1;
            }

            // Native overwrites dx before the cmp, so the item-weight argument
            // is unused. Adding nWeight rejects items native accepts (swallow
            // on the pickup fail arm). Must be Weight < MaxWeight, not
            // Weight + nWeight < MaxWeight.
            if (Regex.IsMatch(returnStatement, @"Weight\s*\+\s*nWeight"))
            {
                Console.WriteLine("FAIL: IsAddWeightAvailable must ignore nWeight (native 0x73C950 overwrites dx)");
                Console.WriteLine($"      Found: {returnStatement.Trim()}");
                return 1;
            }
            if (!Regex.IsMatch(returnStatement, @"Weight\s*<\s*.*MaxWeight"))
            {
                Console.WriteLine("FAIL: IsAddWeightAvailable must use: Weight < MaxWeight");
                Console.WriteLine($"      Found: {returnStatement.Trim()}");
                return 1;
            }

            // Must reference DROP-39
            if (!methodBody.Contains("DROP-39"))
            {
                Console.WriteLine("FAIL: IsAddWeightAvailable must reference DROP-39 in comment");
                return 1;
            }

            // Must mention native EA
            if (!methodBody.Contains("0x73C950") && !methodBody.Contains("sub_73C950"))
            {
                Console.WriteLine("FAIL: IsAddWeightAvailable must reference native EA 0x73C950");
                return 1;
            }

            Console.WriteLine("PASS: IsAddWeightAvailable uses Weight < MaxWeight and ignores nWeight (DROP-39 / 0x73C950 setl)");
            return 0;
        }
    }
}
