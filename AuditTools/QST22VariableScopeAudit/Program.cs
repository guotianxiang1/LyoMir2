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
            var repoRoot = AuditRepoRoot.Resolve(args);
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

            // Assertions 4/5 used to pin the old shape, where the bridge inlined both
            // banks itself (`if (group == 0) return ...m_ScriptVGroup0[index]` and
            // `variables[flat] = ...`). The two banks now live behind the single
            // resolver pair TPlayObject.TryGetScriptVar / SetScriptVar, so the shape
            // check moved with them. The contract is unchanged and is checked on both
            // halves: the bridge must delegate and must not recompute the flat key,
            // and the resolver must serve/write group-0 V from the inline slots.
            var basePathForResolver = Path.Combine(repoRoot, "GameSvr", "Players",
                "TPlayObject.Base.cs");
            var resolverSource = File.Exists(basePathForResolver)
                ? File.ReadAllText(basePathForResolver) : string.Empty;

            // Assertion 4: group-0 V is served from the inline slots, not the dictionary.
            // Reading a never-written slot must therefore yield 0 (Delphi zero-init at
            // 0x6DF20F) rather than the -1 a dictionary miss produces.
            var getPlayerVarBody = Regex.Match(content,
                @"internal\s+static\s+PasValue\s+GetPlayerVar\s*\([^)]*TPlayObject[^)]*\)\s*\{.*?\n        \}",
                RegexOptions.Singleline);
            var resolverRead = Regex.Match(resolverSource,
                @"public\s+bool\s+TryGetScriptVar\s*\([^)]+\)\s*\{.*?\n        \}",
                RegexOptions.Singleline);
            if (!getPlayerVarBody.Success)
            {
                Console.WriteLine("[FAIL] Assertion 4: GetPlayerVar(TPlayObject, ...) not found");
                failCount++;
            }
            else if (!Regex.IsMatch(getPlayerVarBody.Value,
                         @"TryGetScriptVar\s*\(\s*type\s*,\s*group\s*,\s*index\s*,"))
            {
                Console.WriteLine("[FAIL] Assertion 4: GetPlayerVar does not go through TryGetScriptVar");
                failCount++;
            }
            else if (Regex.IsMatch(StripComments(getPlayerVarBody.Value),
                         @"group\s*\*\s*1000"))
            {
                Console.WriteLine("[FAIL] Assertion 4: GetPlayerVar recomputes the flat key - group 0 is never in the dictionary");
                failCount++;
            }
            else if (!resolverRead.Success ||
                     !Regex.IsMatch(resolverRead.Value,
                         @"group\s*==\s*0.*?m_ScriptVGroup0\s*\[\s*index\s*\]",
                         RegexOptions.Singleline))
            {
                Console.WriteLine("[FAIL] Assertion 4: TryGetScriptVar does not read group-0 V from the inline slots");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 4: GetPlayerVar reads group-0 V from the inline slots (0 default)");
                assertionCount++;
            }

            // Assertion 5: SetPlayerVar stores 0 unconditionally (QST-07). Native SetV
            // has no zero test on either arm - 0x6DF2A5 `mov eax,[ebp+8]` /
            // 0x6DF2A8 `mov [ebx+esi*4+0x808],eax` for group 0, and the keyed upsert
            // sub_6E4140 writes the value straight through.
            var setPlayerVarBody = Regex.Match(content,
                @"private\s+static\s+bool\s+SetPlayerVar\s*\([^)]*TPlayObject[^)]*\)\s*\{.*?\n        \}",
                RegexOptions.Singleline);
            var resolverWrite = Regex.Match(resolverSource,
                @"public\s+void\s+SetScriptVar\s*\([^)]+\)\s*\{.*?\n        \}",
                RegexOptions.Singleline);
            if (!setPlayerVarBody.Success)
            {
                Console.WriteLine("[FAIL] Assertion 5: SetPlayerVar(TPlayObject, ...) not found");
                failCount++;
            }
            else if (!Regex.IsMatch(setPlayerVarBody.Value,
                         @"SetScriptVar\s*\(\s*type\s*,\s*group\s*,\s*index\s*,\s*value\.AsInt\s*\(\s*\)\s*\)"))
            {
                Console.WriteLine("[FAIL] Assertion 5: SetPlayerVar unconditional store not found");
                failCount++;
            }
            else if (Regex.IsMatch(StripComments(setPlayerVarBody.Value),
                         @"value[^;]*==\s*0|group\s*\*\s*1000"))
            {
                Console.WriteLine("[FAIL] Assertion 5: SetPlayerVar has a zero-value conditional or recomputes the flat key (violates QST-07)");
                failCount++;
            }
            else if (!resolverWrite.Success ||
                     !Regex.IsMatch(resolverWrite.Value,
                         @"m_ScriptVGroup0\s*\[\s*index\s*\]\s*=\s*value\s*;"))
            {
                Console.WriteLine("[FAIL] Assertion 5: SetScriptVar does not write group-0 V into the inline slots");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 5: SetPlayerVar stores all values including 0 (QST-07)");
                assertionCount++;
            }

            // Assertion 6: the inline slots are 101 wide so the native index 1..100 can
            // address them directly (0x6DF2A8 `mov [ebx+esi*4+0x808], eax`).
            var basePath = Path.Combine(repoRoot, "GameSvr", "Players", "TPlayObject.Base.cs");
            var baseSource = File.Exists(basePath) ? File.ReadAllText(basePath) : string.Empty;
            if (!Regex.IsMatch(baseSource, @"m_ScriptVGroup0\s*=\s*new\s+int\s*\[\s*101\s*\]"))
            {
                Console.WriteLine("[FAIL] Assertion 6: m_ScriptVGroup0 is not allocated as int[101]");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 6: m_ScriptVGroup0 allocated as int[101]");
                assertionCount++;
            }

            // Assertion 7: the inline slots must stay session-scoped. The save decoder
            // sub_6E448C touches +0x804 and +0x808, the two dictionaries, and makes no
            // reference to the +0x80C..+0x99B region, so nothing may copy the array into
            // the character record on the way out.
            var playObjectPath = Path.Combine(repoRoot, "GameSvr", "Players", "TPlayObject.cs");
            var playObjectSource = File.Exists(playObjectPath)
                ? File.ReadAllText(playObjectPath) : string.Empty;
            var persisters = Regex.Matches(playObjectSource, @"HumData\.\w+\s*=[^;]*m_ScriptVGroup0");
            if (persisters.Count > 0)
            {
                Console.WriteLine($"[FAIL] Assertion 7: m_ScriptVGroup0 is copied into HumData ({persisters.Count} site(s)) - native never saves it");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 7: m_ScriptVGroup0 is not persisted into HumData");
                assertionCount++;
            }

            // Assertion 8: PlayDice sub_645200 is the one native internal consumer of the
            // group-0 bank (0x645237 `xor edx,edx` / 0x64523B `call 0x6DF1E4`, indices
            // 1..10 per 0x645234 `lea ecx,[esi+1]` and 0x645246 `cmp esi,0xA`). A scan of
            // every E8 call to 0x6DF1E4 / 0x6DF288 / 0x6DF1B4 / 0x6DF240 finds 29 sites and
            // this is the only one passing group 0. It must not read the keyed dictionary,
            // which cannot hold that bank at all.
            var packDiceMatch = Regex.Match(content,
                @"private\s+static\s+int\s+PackDiceValues\s*\([^)]+\)\s*\{.*?\}",
                RegexOptions.Singleline);
            if (!packDiceMatch.Success)
            {
                Console.WriteLine("[FAIL] Assertion 8: PackDiceValues not found");
                failCount++;
            }
            else if (packDiceMatch.Value.Contains("m_ScriptVVars"))
            {
                Console.WriteLine("[FAIL] Assertion 8: PackDiceValues reads the keyed dictionary - group 0 is never in it");
                failCount++;
            }
            else if (!Regex.IsMatch(packDiceMatch.Value,
                @"TryGetScriptVar\s*\(\s*'V'\s*,\s*0\s*,"))
            {
                Console.WriteLine("[FAIL] Assertion 8: PackDiceValues does not read group-0 V through TryGetScriptVar");
                failCount++;
            }
            else
            {
                Console.WriteLine("[PASS] Assertion 8: PackDiceValues reads group-0 V through the single accessor");
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

        // The forbidden-pattern checks describe code, not prose: the byte-evidence
        // comments in the production sources name the very shapes being banned.
        static string StripComments(string source) =>
            Regex.Replace(source, @"//[^\n]*", string.Empty);
    }
}
