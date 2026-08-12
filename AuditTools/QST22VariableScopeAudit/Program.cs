using System;
using System.IO;
using System.Text.RegularExpressions;

namespace QST22VariableScopeAudit
{
    /// <summary>
    /// QST-22: Quest variable scope isolation audit.
    ///
    /// Binary evidence (sub_6DF1E4 GetV, sub_6DF288 SetV):
    /// - 0x6DF203: test esi,esi; jnz (group=0 fast path gate)
    /// - 0x6DF209: dec edx; sub edx,0x64; jae (range check 1..100)
    /// - 0x6DF20F: mov eax,[ebx+eax*4+0x808] (inline array Self+0x808)
    /// - 0x6DF1F1: mov [ebp-4],-1 (miss sentinel seed)
    /// - 0x6E427A: mov [ebp-4],-1 (keyed path miss)
    ///
    /// Requirements:
    /// 1. NativeScriptVarArgsAccepted must filter:
    ///    - V-type group=0: accept index 1..100 only
    ///    - S-type group=0: reject (no fast path)
    ///    - Keyed path: require group>0 AND index>0
    /// 2. GetPlayerVar must return:
    ///    - Stored value if key exists
    ///    - 0 for V-type group=0 unwritten (Delphi zero-init)
    ///    - -1 for keyed path miss
    /// 3. SetPlayerVar must store ALL values including 0 (QST-07)
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            var repoRoot = @"D:\loym2\LyoMir2-master";
            var pasApiBridgePath = Path.Combine(repoRoot, "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs");

            if (!File.Exists(pasApiBridgePath))
            {
                Console.WriteLine($"[FATAL] PasApiBridge.cs not found at: {pasApiBridgePath}");
                return 1;
            }

            var content = File.ReadAllText(pasApiBridgePath);
            int assertionCount = 0;
            int failCount = 0;

            Console.WriteLine("=== QST-22: Variable Scope Isolation Audit ===\n");

            // Assertion 1: NativeScriptVarArgsAccepted signature and V-type group=0 fast path
            if (!Regex.IsMatch(content, @"private\s+static\s+bool\s+NativeScriptVarArgsAccepted\s*\(\s*char\s+type\s*,\s*int\s+group\s*,\s*int\s+index\s*\)"))
            {
                Console.WriteLine("[FAIL] Assertion 1: NativeScriptVarArgsAccepted signature not found");
                failCount++;
            }
            else if (!Regex.IsMatch(content, @"group\s*==\s*0\s*&&\s*char\.ToUpperInvariant\s*\(\s*type\s*\)\s*==\s*'V'"))
            {
                Console.WriteLine("[FAIL] Assertion 1: V-type group=0 check not found");
                failCount++;
            }
            else if (!Regex.IsMatch(content, @"index\s*>=\s*1\s*&&\s*index\s*<=\s*100"))
            {
                Console.WriteLine("[FAIL] Assertion 1: Range check 1..100 not found");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 1: NativeScriptVarArgsAccepted V-type group=0 range [1..100]");
                assertionCount++;
            }

            // Assertion 2: NativeScriptVarArgsAccepted keyed path validation
            if (!Regex.IsMatch(content, @"group\s*>\s*0\s*&&\s*index\s*>\s*0"))
            {
                Console.WriteLine("[FAIL] Assertion 2: Keyed path validation (group>0 && index>0) not found");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 2: NativeScriptVarArgsAccepted keyed path validation");
                assertionCount++;
            }

            // Assertion 3: GetPlayerVar calls NativeScriptVarArgsAccepted
            if (!Regex.IsMatch(content, @"public\s+PasValue\s+GetPlayerVar\s*\(\s*char\s+type\s*,\s*int\s+group\s*,\s*int\s+index\s*\)"))
            {
                Console.WriteLine("[FAIL] Assertion 3: GetPlayerVar signature not found");
                failCount++;
            }
            else if (!Regex.IsMatch(content, @"if\s*\(\s*!NativeScriptVarArgsAccepted\s*\(\s*type\s*,\s*group\s*,\s*index\s*\)\s*\)"))
            {
                Console.WriteLine("[FAIL] Assertion 3: GetPlayerVar does not call NativeScriptVarArgsAccepted");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 3: GetPlayerVar validates args with NativeScriptVarArgsAccepted");
                assertionCount++;
            }

            // Assertion 4: GetPlayerVar returns 0 for V-type group=0 miss, -1 for keyed miss
            var getPlayerVarMatch = Regex.Match(content,
                @"public\s+PasValue\s+GetPlayerVar\s*\([^)]+\).*?" +
                @"return\s+PasValue\.FromInt\s*\(\s*group\s*==\s*0\s*&&\s*char\.ToUpperInvariant\s*\(\s*type\s*\)\s*==\s*'V'\s*\?\s*0\s*:\s*NativeScriptVarMiss\s*\)",
                RegexOptions.Singleline);

            if (!getPlayerVarMatch.Success)
            {
                Console.WriteLine("[FAIL] Assertion 4: GetPlayerVar conditional return (0 for V group=0, -1 otherwise) not found");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 4: GetPlayerVar returns 0 for V-type group=0 miss, -1 for keyed miss");
                assertionCount++;
            }

            // Assertion 5: SetPlayerVar stores 0 unconditionally (QST-07)
            var setPlayerVarMatch = Regex.Match(content,
                @"private\s+static\s+void\s+SetPlayerVar\s*\([^)]+\).*?variables\s*\[\s*flat\s*\]\s*=\s*value\.AsInt\s*\(\s*\)",
                RegexOptions.Singleline);

            if (!setPlayerVarMatch.Success)
            {
                Console.WriteLine("[FAIL] Assertion 5: SetPlayerVar unconditional store not found");
                failCount++;
            }
            else if (Regex.IsMatch(content, @"if\s*\([^)]*value[^)]*==\s*0[^)]*\).*?variables\s*\[\s*flat\s*\]\s*=", RegexOptions.Singleline))
            {
                Console.WriteLine("[FAIL] Assertion 5: SetPlayerVar has zero-value conditional (violates QST-07)");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 5: SetPlayerVar stores all values including 0 (QST-07)");
                assertionCount++;
            }

            // Assertion 6: Comment references QST-22 or QST-17 (documentation)
            if (!Regex.IsMatch(content, @"QST-2[27]|QST-1[17]"))
            {
                Console.WriteLine("[FAIL] Assertion 6: No QST-22/QST-17/QST-07/QST-11 documentation found");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 6: QST documentation present");
                assertionCount++;
            }

            Console.WriteLine($"\n=== Summary ===");
            Console.WriteLine($"Total assertions: {assertionCount + failCount}");
            Console.WriteLine($"Passed: {assertionCount}");
            Console.WriteLine($"Failed: {failCount}");

            if (failCount > 0)
            {
                Console.WriteLine("\n[AUDIT FAILED] QST-22 implementation has issues");
                return 1;
            }

            Console.WriteLine("\n[AUDIT PASSED] QST-22 implementation is correct");
            return 0;
        }
    }
}
