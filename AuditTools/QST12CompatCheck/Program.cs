using System;
using System.IO;
using System.Diagnostics;
using GameSvr;
using GameSvr.PasEngine;

namespace QST12CompatCheck
{
    /// <summary>
    /// QST-12 audit: Task count aggregation (inline array group=0 fast path).
    ///
    /// Native evidence:
    /// - GetV sub_6DF1E4 at 0x6DF20F: mov eax, [ebx+eax*4+0x808] (inline array access)
    /// - SetV sub_6DF288 at 0x6DF2A8: mov [ebx+esi*4+0x808], eax (inline array write)
    /// - Range check: 0x6DF209 dec edx; 0x6DF20A sub edx,0x64; 0x6DF20D jae (1 &lt;= index &lt;= 100)
    /// - Key calculation: sub_6E42CC: imul eax,edx,0x3E8; add eax,ecx; ret (flat = group*1000+index)
    ///
    /// Requirements:
    /// 1. V-type group=0 accepts index 1..100 (inline array)
    /// 2. V-type group=0 unwritten slots return 0 (zero-init array)
    /// 3. Other groups/types unwritten slots return -1 (keyed path miss sentinel)
    /// 4. SetV stores 0 as real value (QST-07: no zero removal)
    /// 5. Invalid arguments return -1 immediately
    /// </summary>
    class Program
    {
        static int Main()
        {
            PrepareRuntimeConfig();

            int failures = 0;

            // Test group=0 boundary checks
            failures += Test_Group0_Index1_LowerBound();
            failures += Test_Group0_Index100_UpperBound();
            failures += Test_Group0_Index0_BelowRange();
            failures += Test_Group0_Index101_AboveRange();

            // Test unwritten slot defaults
            failures += Test_Group0_Unwritten_ReturnsZero();
            failures += Test_Group1_Unwritten_ReturnsNegativeOne();

            // Test zero storage (QST-07)
            failures += Test_SetV_Group0_StoresZero();
            failures += Test_SetV_Group0_ZeroThenRead();

            // Test S-type rejection for group=0
            failures += Test_SType_Group0_Rejected();

            // Test keyed path positivity
            failures += Test_KeyedPath_Group0_Index0_Rejected();
            failures += Test_KeyedPath_GroupNeg_Rejected();

            if (failures > 0)
            {
                Console.WriteLine($"FAIL: {failures} test(s) failed");
                return 1;
            }

            Console.WriteLine("PASS: All QST-12 tests passed");
            return 0;
        }

        static int Test_Group0_Index1_LowerBound()
        {
            var (bridge, _) = CreateContext();
            bridge.SetPlayerVar('V', 0, 1, PasValue.FromInt(42));
            var result = bridge.GetPlayerVar('V', 0, 1).AsInt();
            return Verify(result == 42, "Group0 index=1 (lower bound) should accept");
        }

        static int Test_Group0_Index100_UpperBound()
        {
            var (bridge, _) = CreateContext();
            bridge.SetPlayerVar('V', 0, 100, PasValue.FromInt(999));
            var result = bridge.GetPlayerVar('V', 0, 100).AsInt();
            return Verify(result == 999, "Group0 index=100 (upper bound) should accept");
        }

        static int Test_Group0_Index0_BelowRange()
        {
            var (bridge, _) = CreateContext();
            bridge.SetPlayerVar('V', 0, 0, PasValue.FromInt(123));
            var result = bridge.GetPlayerVar('V', 0, 0).AsInt();
            return Verify(result == -1, "Group0 index=0 should reject (return -1)");
        }

        static int Test_Group0_Index101_AboveRange()
        {
            var (bridge, _) = CreateContext();
            bridge.SetPlayerVar('V', 0, 101, PasValue.FromInt(777));
            var result = bridge.GetPlayerVar('V', 0, 101).AsInt();
            return Verify(result == -1, "Group0 index=101 should reject (return -1)");
        }

        static int Test_Group0_Unwritten_ReturnsZero()
        {
            var (bridge, _) = CreateContext();
            // Read without writing first
            var result = bridge.GetPlayerVar('V', 0, 50).AsInt();
            return Verify(result == 0, "Group0 unwritten slot should return 0 (inline array zero-init)");
        }

        static int Test_Group1_Unwritten_ReturnsNegativeOne()
        {
            var (bridge, _) = CreateContext();
            // Read without writing first
            var result = bridge.GetPlayerVar('V', 1, 50).AsInt();
            return Verify(result == -1, "Group1 unwritten slot should return -1 (keyed path miss)");
        }

        static int Test_SetV_Group0_StoresZero()
        {
            var (bridge, player) = CreateContext();
            bridge.SetPlayerVar('V', 0, 5, PasValue.FromInt(0));
            // Native sub_6E4140 has NO zero test (0x6E4187: mov [eax], edx unconditional)
            int flat = 0 * 1000 + 5;
            bool stored = player.m_ScriptVVars.ContainsKey(flat);
            return Verify(stored && player.m_ScriptVVars[flat] == 0,
                "SetV(0,5,0) must store 0 as real value (QST-07)");
        }

        static int Test_SetV_Group0_ZeroThenRead()
        {
            var (bridge, _) = CreateContext();
            bridge.SetPlayerVar('V', 0, 7, PasValue.FromInt(0));
            var result = bridge.GetPlayerVar('V', 0, 7).AsInt();
            return Verify(result == 0,
                "After SetV(0,7,0), GetV(0,7) must return 0 not -1 (QST-07)");
        }

        static int Test_SType_Group0_Rejected()
        {
            var (bridge, _) = CreateContext();
            bridge.SetPlayerVar('S', 0, 50, PasValue.FromInt(100));
            var result = bridge.GetPlayerVar('S', 0, 50).AsInt();
            // S-type has no group=0 fast path (0x6DF1BE/0x6DF1C2 require group>0)
            return Verify(result == -1, "S-type group=0 should reject (no fast path for S)");
        }

        static int Test_KeyedPath_Group0_Index0_Rejected()
        {
            var (bridge, _) = CreateContext();
            // Already covered above, but verify keyed path logic
            bridge.SetPlayerVar('V', 1, 0, PasValue.FromInt(50));
            var result = bridge.GetPlayerVar('V', 1, 0).AsInt();
            return Verify(result == -1, "Keyed path (group=1, index=0) should reject");
        }

        static int Test_KeyedPath_GroupNeg_Rejected()
        {
            var (bridge, _) = CreateContext();
            bridge.SetPlayerVar('V', -1, 10, PasValue.FromInt(60));
            var result = bridge.GetPlayerVar('V', -1, 10).AsInt();
            return Verify(result == -1, "Keyed path (group<0) should reject");
        }

        static (PasApiBridge, TPlayObject) CreateContext()
        {
            var player = new TPlayObject();
            player.m_sCharName = "QST12TestPlayer";
            var bridge = new PasApiBridge();
            bridge.CurrentPlayer = player;
            return (bridge, player);
        }

        static int Verify(bool condition, string description)
        {
            if (condition)
            {
                Console.WriteLine($"[PASS] {description}");
                return 0;
            }
            else
            {
                Console.WriteLine($"[FAIL] {description}");
                return 1;
            }
        }

        /// <summary>
        /// The M2Share static constructor (M2Share.cs:1682) resolves !Setup.txt
        /// against AppContext.BaseDirectory, i.e. this audit's own bin directory,
        /// and IniFile.Load throws when it is absent. The first `new TPlayObject()`
        /// therefore aborted the run with TypeInitializationException before any
        /// assertion executed. Same minimal skeleton the other audits lay down.
        /// </summary>
        static void PrepareRuntimeConfig()
        {
            var runtimeDirectory = AppContext.BaseDirectory;
            File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
                "[Server]" + Environment.NewLine);
            File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
                "[Command]" + Environment.NewLine);

            var shareDirectory = Path.Combine(Path.GetFullPath(
                Path.Combine(runtimeDirectory, "..")), "Share");
            Directory.CreateDirectory(shareDirectory);
            File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
                "[PlayerLevelExp]" + Environment.NewLine);
            File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
                "[Integer]" + Environment.NewLine);
        }

    }
}
