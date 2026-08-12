using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

class Program
{
    static int Main()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var targetFile = Path.Combine(basePath, "GameSvr/ScriptSystem/PasEngine/PasApiBridge.cs");

        if (!File.Exists(targetFile))
        {
            Console.WriteLine($"ERROR: File not found: {targetFile}");
            return 1;
        }

        var content = File.ReadAllText(targetFile);
        var lines = File.ReadAllLines(targetFile);
        int errorCount = 0;

        // QST-11 Assertion 1: GetPlayerVar must use m_nVal[] for fast path
        Console.WriteLine("QST-11-A1: GetPlayerVar V-type group=0 must use m_nVal[] fast path");
        var getVFastPathPattern = @"if\s*\(\s*group\s*==\s*0\s*&&\s*char\.ToUpperInvariant\s*\(\s*type\s*\)\s*==\s*'V'\s*\)[\s\S]{0,200}?CurrentPlayer\.m_nVal\s*\[\s*index\s*\]";
        if (!Regex.IsMatch(content, getVFastPathPattern))
        {
            Console.WriteLine("  FAIL: GetPlayerVar does not implement fast path with m_nVal[]");
            Console.WriteLine("  Expected: if (group == 0 && type == 'V') return m_nVal[index]");
            errorCount++;
        }
        else
        {
            Console.WriteLine("  PASS: Fast path detected in GetPlayerVar");
        }

        // QST-11 Assertion 2: SetPlayerVar must use m_nVal[] for fast path
        Console.WriteLine("QST-11-A2: SetPlayerVar V-type group=0 must use m_nVal[] fast path");
        var setVFastPathPattern = @"if\s*\(\s*group\s*==\s*0\s*&&\s*char\.ToUpperInvariant\s*\(\s*type\s*\)\s*==\s*'V'\s*\)[\s\S]{0,200}?player\.m_nVal\s*\[\s*index\s*\]\s*=";
        if (!Regex.IsMatch(content, setVFastPathPattern))
        {
            Console.WriteLine("  FAIL: SetPlayerVar does not implement fast path with m_nVal[]");
            Console.WriteLine("  Expected: if (group == 0 && type == 'V') player.m_nVal[index] = value");
            errorCount++;
        }
        else
        {
            Console.WriteLine("  PASS: Fast path detected in SetPlayerVar");
        }

        // QST-11 Assertion 3: GetPlayerVar must NOT use dictionary for V-type group=0
        Console.WriteLine("QST-11-A3: GetPlayerVar V-type group=0 must bypass dictionary lookup");
        var getVMethod = ExtractMethod(lines, "public PasValue GetPlayerVar");
        if (getVMethod != null)
        {
            // Check if fast path returns early before dictionary lookup
            var fastPathIndex = getVMethod.IndexOf("if (group == 0 && char.ToUpperInvariant(type) == 'V')");
            var dictLookupIndex = getVMethod.IndexOf("m_ScriptVVars");

            if (fastPathIndex >= 0 && dictLookupIndex >= 0 && fastPathIndex < dictLookupIndex)
            {
                // Check if there's a return statement before dictionary lookup
                var betweenFastAndDict = getVMethod.Substring(fastPathIndex, dictLookupIndex - fastPathIndex);
                if (betweenFastAndDict.Contains("return"))
                {
                    Console.WriteLine("  PASS: Fast path returns early, bypassing dictionary");
                }
                else
                {
                    Console.WriteLine("  FAIL: Fast path does not return early - dictionary still accessed");
                    errorCount++;
                }
            }
            else if (fastPathIndex < 0)
            {
                Console.WriteLine("  FAIL: No fast path found");
                errorCount++;
            }
        }

        // QST-11 Assertion 4: SetPlayerVar must NOT use dictionary for V-type group=0
        Console.WriteLine("QST-11-A4: SetPlayerVar V-type group=0 must bypass dictionary write");
        var setVMethod = ExtractMethod(lines, "private static void SetPlayerVar(TPlayObject player");
        if (setVMethod != null)
        {
            var fastPathIndex = setVMethod.IndexOf("if (group == 0 && char.ToUpperInvariant(type) == 'V')");
            var dictWriteIndex = setVMethod.IndexOf("variables[flat]");

            if (fastPathIndex >= 0 && dictWriteIndex >= 0 && fastPathIndex < dictWriteIndex)
            {
                var betweenFastAndDict = setVMethod.Substring(fastPathIndex, dictWriteIndex - fastPathIndex);
                if (betweenFastAndDict.Contains("return"))
                {
                    Console.WriteLine("  PASS: Fast path returns early, bypassing dictionary");
                }
                else
                {
                    Console.WriteLine("  FAIL: Fast path does not return early - dictionary still written");
                    errorCount++;
                }
            }
            else if (fastPathIndex < 0)
            {
                Console.WriteLine("  FAIL: No fast path found");
                errorCount++;
            }
        }

        // QST-11 Assertion 5: EA references must be documented
        Console.WriteLine("QST-11-A5: Fast path must reference native EA addresses");
        if (!content.Contains("0x6DF20F") || !content.Contains("0x6DF2A8"))
        {
            Console.WriteLine("  FAIL: Missing EA references (0x6DF20F for read, 0x6DF2A8 for write)");
            errorCount++;
        }
        else
        {
            Console.WriteLine("  PASS: EA references documented");
        }

        Console.WriteLine();
        if (errorCount == 0)
        {
            Console.WriteLine($"QST-11 AUDIT PASS: All {5} assertions verified");
            return 0;
        }
        else
        {
            Console.WriteLine($"QST-11 AUDIT FAIL: {errorCount} assertion(s) failed");
            return 1;
        }
    }

    static string ExtractMethod(string[] lines, string methodSignature)
    {
        int startLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(methodSignature))
            {
                startLine = i;
                break;
            }
        }

        if (startLine < 0) return null;

        int braceCount = 0;
        bool started = false;
        var method = new System.Text.StringBuilder();

        for (int i = startLine; i < lines.Length; i++)
        {
            method.AppendLine(lines[i]);
            foreach (char c in lines[i])
            {
                if (c == '{') { braceCount++; started = true; }
                if (c == '}') braceCount--;
            }
            if (started && braceCount == 0) break;
        }

        return method.ToString();
    }
}
