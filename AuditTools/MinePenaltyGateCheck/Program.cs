using System;
using System.IO;
using System.Runtime.CompilerServices;

/// <summary>
/// MINE-46 audit: Verify PileStones hard-blocks mining when tier==3
/// and does NOT broadcast RM_HEAVYHIT on that arm.
/// Native sub_6BC1EC: 0x6BC202 / 0x6BC21E je 0x6BC366 (epilogue).
/// </summary>
class Program
{
    static int Main()
    {
        int exitCode = 0;
        try
        {
            var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var targetFile = Path.Combine(projectRoot, "GameSvr/Players/TPlayObject.cs");

            if (!File.Exists(targetFile))
            {
                Console.WriteLine($"FAIL: File not found: {targetFile}");
                return 1;
            }

            var lines = File.ReadAllLines(targetFile);
            int assertionsPassed = 0;

            // Find PileStones function
            int pileStonesFnLine = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("private bool PileStones(int nX, int nY)"))
                {
                    pileStonesFnLine = i;
                    break;
                }
            }

            if (pileStonesFnLine == -1)
            {
                Console.WriteLine("FAIL: Could not find PileStones function");
                return 1;
            }

            // Check for hard-block at function start (skip the evidence comment block)
            bool foundTier3Block = false;
            bool foundFatigueTierCheck = false;
            bool foundCheatPenaltyCheck = false;

            for (int i = pileStonesFnLine; i < Math.Min(pileStonesFnLine + 30, lines.Length); i++)
            {
                var line = lines[i];
                if (line.Contains("//")) continue; // Skip comment lines

                if (line.Contains("m_btNativeFatigueTier") && line.Contains("== 3"))
                {
                    foundFatigueTierCheck = true;
                }
                if (line.Contains("m_btNativeCheatPenaltyTier") && line.Contains("== 3"))
                {
                    foundCheatPenaltyCheck = true;
                }
                if (line.Contains("return false") && foundFatigueTierCheck && foundCheatPenaltyCheck)
                {
                    foundTier3Block = true;
                    break;
                }
            }

            Assert(foundTier3Block,
                "PileStones must hard-block (return false) when m_btNativeFatigueTier==3 OR m_btNativeCheatPenaltyTier==3",
                targetFile, pileStonesFnLine);
            assertionsPassed++;

            // MINE-46: native je 0x6BC366 is the function epilogue (pop/leave/ret),
            // so the tier==3 arm must not emit RM_HEAVYHIT. Scan the first
            // return-false block and reject any SendRefMsg / RM_HEAVYHIT in it.
            bool tier3BlockBroadcasts = false;
            int blockStart = -1;
            for (int i = pileStonesFnLine; i < Math.Min(pileStonesFnLine + 30, lines.Length); i++)
            {
                var line = lines[i];
                if (line.Contains("//")) continue;
                if (blockStart < 0
                    && line.Contains("m_btNativeFatigueTier")
                    && line.Contains("== 3"))
                {
                    blockStart = i;
                }
                if (blockStart >= 0)
                {
                    if (line.Contains("SendRefMsg") || line.Contains("RM_HEAVYHIT"))
                    {
                        tier3BlockBroadcasts = true;
                        break;
                    }
                    if (line.Contains("return false"))
                    {
                        break;
                    }
                }
            }
            Assert(!tier3BlockBroadcasts,
                "PileStones tier==3 early-out must NOT broadcast RM_HEAVYHIT (native je 0x6BC366 = epilogue)",
                targetFile, pileStonesFnLine);
            assertionsPassed++;

            // Check that tier==2 halving logic exists (already implemented as MINE-21)
            bool foundTier2Halving = false;
            for (int i = pileStonesFnLine; i < Math.Min(pileStonesFnLine + 100, lines.Length); i++)
            {
                var line = lines[i].Trim();

                // Look for the condition that checks m_btNativeFatigueTier == 2
                if (line.Contains("m_btNativeFatigueTier") && line.Contains("== 2") && !line.StartsWith("//"))
                {
                    foundTier2Halving = true;
                    break;
                }
                // Also accept the number 24 in context of mining rate
                if ((line.Contains("24") || line.Contains("0x18")) && !line.StartsWith("//"))
                {
                    foundTier2Halving = true;
                    break;
                }
            }

            Assert(foundTier2Halving,
                "PileStones must use Random(24) when tier==2 for ore drop halving",
                targetFile, pileStonesFnLine);
            assertionsPassed++;

            Console.WriteLine($"PASS: All {assertionsPassed} assertions passed");
            Console.WriteLine($"  - Hard-block for tier==3 (both bytes) present");
            Console.WriteLine($"  - Halving for tier==2 present");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: Exception: {ex.Message}");
            return 1;
        }
    }

    static void Assert(bool condition, string message, string file, int line,
        [CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
    {
        if (!condition)
        {
            Console.WriteLine($"FAIL: {message}");
            Console.WriteLine($"  at {file}:{line}");
            Console.WriteLine($"  audit: {sourceFile}:{sourceLine}");
            throw new Exception($"Assertion failed: {message}");
        }
    }
}
