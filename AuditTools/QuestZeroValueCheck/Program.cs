using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

namespace AuditTools.QuestZeroValueCheck
{
    /// <summary>
    /// QST-07 Audit: Verify quest zero-value storage is correctly implemented.
    ///
    /// Native behavior (sub_6E4140):
    ///   - Stores ALL values including zero (four write points: 0x6E4187/0x6E41C2/0x6E4231/0x6E4260)
    ///   - No zero-value checks before writing
    ///   - Returns TRUE unconditionally
    ///
    /// Fixed in commit e3bfb80:
    ///   - Writer (PasApiBridge.SetPlayerVar): Removed variables.Remove(flat) for zero
    ///   - Loader (UsrEngn.LoadHumanRcd): Removed 'if (variable.Value != 0)' skip
    ///
    /// This audit ensures the fix remains in place.
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            // M2Share.cctor (M2Share.cs:1678) loads !Setup.txt from
            // AppContext.BaseDirectory on the first TPlayObject construction.
            // Without the skeleton this process used to die in type init; if a
            // native AV is still hiding behind that, the DIAG line below is the
            // last thing stdout will hold.
            try
            {
                Diagnose("enter-main");
                Diagnose("prepare-runtime-config");
                PrepareRuntimeConfig();
                Diagnose("before-new-TPlayObject");
                _ = new TPlayObject();
                Diagnose("after-new-TPlayObject");
                // The loader half of the round trip is an instance method on UserEngine.
                M2Share.UserEngine ??= new UserEngine();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "INCOMPLETE: TPlayObject construction/type-init failed before QST-07 assertions.");
                Console.Error.WriteLine(ex.ToString());
                return 2;
            }

            Console.WriteLine("================================================================================");
            Console.WriteLine("QST-07 Audit: Quest Zero-Value Storage");
            Console.WriteLine("================================================================================");
            Console.WriteLine();

            int failures = 0;

            // Test 1: Verify SetPlayerVar doesn't remove zero values
            Console.WriteLine("[1] Checking PasApiBridge.SetPlayerVar for zero-value handling...");
            failures += CheckSetPlayerVarImplementation();

            // Test 2: Verify loader doesn't skip zero values
            Console.WriteLine();
            Console.WriteLine("[2] Checking UsrEngn loader for zero-value restoration...");
            failures += CheckLoaderImplementation();

            // Test 3: Runtime behavior test
            Console.WriteLine();
            Console.WriteLine("[3] Runtime behavior test...");
            failures += TestRuntimeBehavior();

            Console.WriteLine();
            Console.WriteLine("================================================================================");
            if (failures == 0)
            {
                Console.WriteLine("PASS: All QST-07 audit checks passed.");
                Console.WriteLine("      Zero values are correctly stored and loaded.");
                return 0;
            }
            else
            {
                Console.WriteLine($"FAIL: {failures} audit check(s) failed.");
                Console.WriteLine("      Quest zero-value storage is broken!");
                return 1;
            }
        }

        // The real script-variable writer: PasApiBridge.SetPlayerVar(player, type, group,
        // index, value) — private static, so a bare TPlayObject is enough to drive it.
        static readonly MethodInfo SetPlayerVarMethod =
            typeof(PasApiBridge).GetMethod("SetPlayerVar",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(TPlayObject), typeof(char), typeof(int), typeof(int), typeof(PasValue) },
                null);

        // The real script-variable reader (native GetV/GetS, sub_6DF1E4): a keyed miss
        // yields NativeScriptVarMiss (-1), which is what a wrongly-erased 0 degrades into.
        static readonly MethodInfo GetPlayerVarMethod =
            typeof(PasApiBridge).GetMethod("GetPlayerVar",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(TPlayObject), typeof(char), typeof(int), typeof(int) },
                null);

        static bool SetVar(TPlayObject player, char bank, int group, int index, int value) =>
            (bool)SetPlayerVarMethod.Invoke(null,
                new object[] { player, bank, group, index, PasValue.FromInt(value) });

        static int GetVar(TPlayObject player, char bank, int group, int index) =>
            ((PasValue)GetPlayerVarMethod.Invoke(null,
                new object[] { player, bank, group, index })).AsInt();

        static int CheckSetPlayerVarImplementation()
        {
            // This used to write into a locally constructed Dictionary<int,int> and then
            // assert that Dictionary had stored the zero — a tautology that passed no
            // matter what the product did. It now drives the REAL writer and the REAL
            // reader, so re-introducing the QST-30 zero erasure fails it.
            Console.WriteLine("  Driving the real PasApiBridge.SetPlayerVar / GetPlayerVar...");

            if (SetPlayerVarMethod == null || GetPlayerVarMethod == null)
            {
                Console.WriteLine("  [FAIL] PasApiBridge.SetPlayerVar/GetPlayerVar not found — "
                    + "the writer this audit guards no longer exists in that shape");
                return 1;
            }

            var player = new TPlayObject();

            // Keyed banks only: group-0 V is the inline +0x808 array, a different store.
            SetVar(player, 'V', 1, 5, 0);
            SetVar(player, 'S', 2, 10, 0);
            SetVar(player, 'V', 1, 6, 77);   // control: a non-zero neighbour must survive

            int failures = 0;

            if (player.m_ScriptVVars != null && player.m_ScriptVVars.TryGetValue(1005, out var vStored)
                && vStored == 0)
            {
                Console.WriteLine("  [PASS] real SetV(1,5,0) filed key 1005 with value 0");
            }
            else
            {
                Console.WriteLine("  [FAIL] real SetV(1,5,0) did not file a zero at key 1005 "
                    + $"(present={player.m_ScriptVVars?.ContainsKey(1005)})");
                failures++;
            }

            if (player.m_ScriptSVars != null && player.m_ScriptSVars.TryGetValue(2010, out var sStored)
                && sStored == 0)
            {
                Console.WriteLine("  [PASS] real SetS(2,10,0) filed key 2010 with value 0");
            }
            else
            {
                Console.WriteLine("  [FAIL] real SetS(2,10,0) did not file a zero at key 2010 "
                    + $"(present={player.m_ScriptSVars?.ContainsKey(2010)})");
                failures++;
            }

            // The player-visible consequence: an erased 0 reads back as the -1 miss value,
            // which flips every `= 0` quest condition in the scripts.
            var vRead = GetVar(player, 'V', 1, 5);
            var sRead = GetVar(player, 'S', 2, 10);
            if (vRead == 0 && sRead == 0)
            {
                Console.WriteLine("  [PASS] real GetV/GetS return 0, not the -1 keyed miss");
            }
            else
            {
                Console.WriteLine($"  [FAIL] real GetV(1,5)={vRead}, GetS(2,10)={sRead}; "
                    + "a stored 0 must not read back as the -1 miss");
                failures++;
            }

            if (GetVar(player, 'V', 1, 6) == 77)
            {
                Console.WriteLine("  [PASS] non-zero neighbour V(1,6)=77 unaffected");
            }
            else
            {
                Console.WriteLine("  [FAIL] non-zero neighbour V(1,6) was disturbed");
                failures++;
            }

            return failures == 0 ? 0 : 1;
        }

        static int CheckLoaderImplementation()
        {
            // Was: copy one local Dictionary into another and assert the copy worked.
            // Now: the REAL save -> load round trip, which is the only way to catch the
            // QST-30 shape where the writer keeps 0 but the loader drops it on next login
            // (write / persist / load must all agree — REPLICATION_RULES 4.19).
            Console.WriteLine("  Driving the real MakeSaveRcd -> UserEngine.GetHumData round trip...");

            var getHumData = typeof(UserEngine).GetMethod("GetHumData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (getHumData == null)
            {
                Console.WriteLine("  [FAIL] UserEngine.GetHumData not found — "
                    + "the loader this audit guards no longer exists in that shape");
                return 1;
            }

            var saved = new TPlayObject();
            SetVar(saved, 'V', 1, 2, 0);     // the zero under test
            SetVar(saved, 'V', 1, 1, 10);    // controls either side of it
            SetVar(saved, 'V', 1, 3, 25);
            SetVar(saved, 'S', 2, 2, 0);
            SetVar(saved, 'S', 2, 1, 100);

            var record = new THumDataInfo();
            saved.MakeSaveRcd(ref record);

            var reloaded = new TPlayObject();
            getHumData.Invoke(M2Share.UserEngine, new object[] { reloaded, record });

            int failures = 0;

            if (GetVar(reloaded, 'V', 1, 2) == 0 && GetVar(reloaded, 'V', 1, 1) == 10
                && GetVar(reloaded, 'V', 1, 3) == 25)
            {
                Console.WriteLine("  [PASS] ScriptV survives save+load with the zero intact");
            }
            else
            {
                Console.WriteLine("  [FAIL] ScriptV round trip lost the zero: "
                    + $"V(1,1)={GetVar(reloaded, 'V', 1, 1)} "
                    + $"V(1,2)={GetVar(reloaded, 'V', 1, 2)} (must be 0, not -1) "
                    + $"V(1,3)={GetVar(reloaded, 'V', 1, 3)}");
                failures++;
            }

            if (GetVar(reloaded, 'S', 2, 2) == 0 && GetVar(reloaded, 'S', 2, 1) == 100)
            {
                Console.WriteLine("  [PASS] ScriptS survives save+load with the zero intact");
            }
            else
            {
                Console.WriteLine("  [FAIL] ScriptS round trip lost the zero: "
                    + $"S(2,1)={GetVar(reloaded, 'S', 2, 1)} "
                    + $"S(2,2)={GetVar(reloaded, 'S', 2, 2)} (must be 0, not -1)");
                failures++;
            }

            return failures == 0 ? 0 : 1;
        }

        static int TestRuntimeBehavior()
        {
            // Was: a local Dictionary written and read back by the test itself. Now the
            // same four cases go through the real writer/reader pair, so a stored 0 and an
            // absent key are distinguished by the product, not by the harness.
            Console.WriteLine("  Testing the real write-read cycle...");

            var player = new TPlayObject();
            SetVar(player, 'V', 5, 123, 42);   // Normal value
            SetVar(player, 'V', 7, 1, 0);      // Zero value
            SetVar(player, 'V', 9, 999, -1);   // Negative value

            int read1 = GetVar(player, 'V', 5, 123);
            int read2 = GetVar(player, 'V', 7, 1);
            int read3 = GetVar(player, 'V', 9, 999);
            int read4 = GetVar(player, 'V', 8, 888);  // never written

            bool test1 = (read1 == 42);
            bool test2 = (read2 == 0);   // Zero stored, not -1
            bool test3 = (read3 == -1);
            bool test4 = (read4 == -1);  // Missing returns -1

            if (test1)
                Console.WriteLine("  [PASS] Normal value 42 read correctly");
            else
            {
                Console.WriteLine($"  [FAIL] Normal value incorrect (expected=42, got={read1})");
                return 1;
            }

            if (test2)
                Console.WriteLine("  [PASS] Zero value 0 read correctly (distinct from missing)");
            else
            {
                Console.WriteLine($"  [FAIL] Zero value incorrect (expected=0, got={read2})");
                Console.WriteLine("         This would flip all '= 0' quest checks!");
                return 1;
            }

            if (test3)
                Console.WriteLine("  [PASS] Negative value -1 read correctly");
            else
            {
                Console.WriteLine($"  [FAIL] Negative value incorrect (expected=-1, got={read3})");
                return 1;
            }

            if (test4)
                Console.WriteLine("  [PASS] Missing key returns -1");
            else
            {
                Console.WriteLine($"  [FAIL] Missing key incorrect (expected=-1, got={read4})");
                return 1;
            }

            return 0;
        }

        static void Diagnose(string step)
        {
            Console.WriteLine("DIAG step=" + step);
            Console.Out.Flush();
            Console.Error.Flush();
        }

        /// <summary>
        /// M2Share.cctor (M2Share.cs:1682) resolves !Setup.txt against
        /// AppContext.BaseDirectory. Same minimal skeleton the other audits lay down
        /// before the first <c>new TPlayObject()</c>.
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

            // TBaseObject's ctor ends in M2Share.ObjectManager.RegisterConstructed(this)
            // (TBaseObject.cs:903), so the singleton must exist before a real actor can be
            // built. Same minimal set the InProc harnesses boot: no threads, no network.
            M2Share.g_Config ??= new GameSvrConfig();
            M2Share.RandomNumber ??= RandomNumber.GetInstance();
            M2Share.ObjectManager ??= new ObjectManager();
        }

    }
}
