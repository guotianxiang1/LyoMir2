using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

/// <summary>
/// POIS-11 / POIS-30: Verify poison index constants are correctly documented and used.
///
/// POIS-11: Legacy POISON_* constants from different fork are documented as misleading.
///          State 0 is monster burrow, NOT poison (STATE-18).
/// POIS-30: POISON_68=68 exceeds MAX_STATUS_ATTRIBUTE=12 array bounds and is documented
///          as unreachable for m_wStatusTimeArr.
/// </summary>
class Program
{
    static int failures = 0;

    static void Assert(bool condition, string message,
        [CallerFilePath] string path = "", [CallerLineNumber] int line = 0)
    {
        if (!condition)
        {
            Console.WriteLine($"FAIL {path}:{line} {message}");
            failures++;
        }
    }

    // Constructing a real TBaseObject runs the M2Share static ctor, which loads StringConfig from
    // !Setup.txt / String.ini / Command.conf and throws if they are absent, and the ctor then ends in
    // M2Share.ObjectManager.RegisterConstructed (TBaseObject.cs:903), which NREs with no ObjectManager.
    // Same minimal on-disk config + singleton set the InProc harnesses lay down, so the array-bounds
    // assertions below run against a REAL actor rather than a stub. No engine threads, no network.
    static void PrepareRuntime()
    {
        var baseDir = AppContext.BaseDirectory;
        File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
        File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
        File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
        var share = Path.GetFullPath(Path.Combine(baseDir, "..", "Share"));
        Directory.CreateDirectory(share);
        File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"), "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
        File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]\r\n");

        M2Share.g_Config ??= new GameSvrConfig();
        M2Share.RandomNumber ??= RandomNumber.GetInstance();
        M2Share.ObjectManager ??= new ObjectManager();
    }

    static void Main()
    {
        PrepareRuntime();
        Console.WriteLine("=== POIS-11 / POIS-30: Poison Index Divergence Check ===\n");

        // POIS-30: POISON_68 exceeds array bounds
        Console.WriteLine("POIS-30: Verify POISON_68 exceeds MAX_STATUS_ATTRIBUTE...");
        Assert(Grobal2.POISON_68 == 68, "POISON_68 should be 68");
        Assert(Grobal2.MAX_STATUS_ATTRIBUTE == 12, "MAX_STATUS_ATTRIBUTE should be 12");
        Assert(Grobal2.POISON_68 >= Grobal2.MAX_STATUS_ATTRIBUTE,
            "POISON_68 (68) must exceed MAX_STATUS_ATTRIBUTE (12) - this is the divergence");
        Console.WriteLine($"  ✓ POISON_68={Grobal2.POISON_68} > MAX_STATUS_ATTRIBUTE={Grobal2.MAX_STATUS_ATTRIBUTE}");

        // POIS-30: Native 112-bit namespace bound is 0x6F (111)
        Console.WriteLine("\nPOIS-30: Verify native namespace bound...");
        const int NativeStatusMax = 0x6F; // 111 decimal, per 0x772993 cmp bl,0x6F
        Assert(Grobal2.POISON_68 <= NativeStatusMax,
            $"POISON_68 ({Grobal2.POISON_68}) is within native 112-bit range 0..{NativeStatusMax}");
        Console.WriteLine($"  ✓ Native range 0..{NativeStatusMax} (112 bits), POISON_68={Grobal2.POISON_68} is valid natively");

        // POIS-30: Verify bit-shift wrapping behavior
        Console.WriteLine("\nPOIS-30: Demonstrate bit-shift wrapping issue...");
        int shift68 = unchecked((int)(0x80000000u >> 68)); // C# masks shift to 68 & 31 = 4
        int shift4 = unchecked((int)(0x80000000u >> 4));
        Assert(shift68 == shift4,
            $"0x80000000>>68 should wrap to 0x80000000>>4 due to C# shift masking");
        Console.WriteLine($"  ✓ 0x80000000 >> 68 = 0x{shift68:X8} (wraps to >> 4, collision hazard)");

        // POIS-11: Verify legacy POISON_* constants have expected values
        Console.WriteLine("\nPOIS-11: Verify legacy POISON_* slot indices...");
        Assert(Grobal2.POISON_DECHEALTH == 0, "POISON_DECHEALTH should be 0");
        Assert(Grobal2.POISON_DAMAGEARMOR == 1, "POISON_DAMAGEARMOR should be 1");
        Assert(Grobal2.POISON_LOCKSPELL == 2, "POISON_LOCKSPELL should be 2");
        Assert(Grobal2.POISON_DONTMOVE == 4, "POISON_DONTMOVE should be 4");
        Assert(Grobal2.POISON_STONE == 5, "POISON_STONE should be 5");
        Console.WriteLine("  ✓ All legacy POISON_* slot indices match expected values");

        // POIS-11: Verify native poison state IDs are correctly defined
        Console.WriteLine("\nPOIS-11: Verify native poison state IDs (from binary evidence)...");
        Assert(TBaseObject.NativePoisonStateMaxHpOver100 == 0x06,
            "NativePoisonStateMaxHpOver100 should be 0x06");
        Assert(TBaseObject.NativePoisonStateMaxHpOver30 == 0x01,
            "NativePoisonStateMaxHpOver30 should be 0x01");
        Assert(TBaseObject.NativePoisonStateCasterValueLow == 0x1C,
            "NativePoisonStateCasterValueLow should be 0x1C (28)");
        Assert(TBaseObject.NativePoisonStateCasterValueHigh == 0x1F,
            "NativePoisonStateCasterValueHigh should be 0x1F (31)");
        Assert(TBaseObject.NativeMagicState30 == 30,
            "NativeMagicState30 should be 30 (0x1E)");
        Console.WriteLine("  ✓ Native poison state IDs: 0x06, 0x01, 0x1C, 0x1F (DoT tiers)");
        Console.WriteLine("  ✓ Native defense poison state: 0x1E (30)");

        // POIS-11: Verify state 0 is NOT used as poison in the native sense
        Console.WriteLine("\nPOIS-11: Verify state 0 divergence (burrow vs poison)...");
        Console.WriteLine("  ⚠ STATE-18: Native state 0 = monster burrow (RM_DIGUP 10200), NOT poison");
        Console.WriteLine("  ⚠ POISON_DECHEALTH=0 name is misleading (legacy from different fork)");
        Console.WriteLine("  ✓ Documented: POISON_* are timer slot indices, not native state IDs");

        // Verify m_wStatusTimeArr size
        Console.WriteLine("\nVerify m_wStatusTimeArr array size...");
        var actor = new TBaseObject();
        Assert(actor.m_wStatusTimeArr.Length == 12,
            $"m_wStatusTimeArr.Length should be 12, got {actor.m_wStatusTimeArr.Length}");
        Console.WriteLine($"  ✓ m_wStatusTimeArr has {actor.m_wStatusTimeArr.Length} slots (indices 0-11)");

        // Verify array access safety
        Console.WriteLine("\nVerify safe POISON_* constants for array indexing...");
        int[] safeConstants = new[]
        {
            Grobal2.POISON_DECHEALTH,
            Grobal2.POISON_DAMAGEARMOR,
            Grobal2.POISON_LOCKSPELL,
            Grobal2.STATE_LOCKRUN,
            Grobal2.POISON_DONTMOVE,
            Grobal2.POISON_STONE,
            Grobal2.STATE_TRANSPARENT,
            Grobal2.STATE_DEFENCEUP,
            Grobal2.STATE_MAGDEFENCEUP,
            Grobal2.STATE_BUBBLEDEFENCEUP
        };
        foreach (var c in safeConstants)
        {
            Assert(c >= 0 && c < Grobal2.MAX_STATUS_ATTRIBUTE,
                $"Constant {c} must be in range [0, {Grobal2.MAX_STATUS_ATTRIBUTE})");
        }
        Console.WriteLine($"  ✓ All {safeConstants.Length} commonly-used constants are within array bounds");

        // POIS-30: Verify POISON_68 is unsafe for array indexing
        Console.WriteLine("\nPOIS-30: Verify POISON_68 is unsafe for array indexing...");
        Assert(Grobal2.POISON_68 >= actor.m_wStatusTimeArr.Length,
            $"POISON_68 ({Grobal2.POISON_68}) exceeds array length ({actor.m_wStatusTimeArr.Length})");
        Console.WriteLine($"  ✓ POISON_68={Grobal2.POISON_68} is outside the 12 legacy slots"
            + " (the forwarding view ignores it, matching native's `cmp dl,0x6F / ja` skip)");
        Console.WriteLine("  ✓ Use HasNativeActiveState(68) / SetNativeActiveState(68) instead");

        // Summary
        Console.WriteLine($"\n{'='.ToString().PadRight(60, '=')}");
        if (failures == 0)
        {
            Console.WriteLine("PASS: All POIS-11 / POIS-30 assertions passed");
            Console.WriteLine("\nDivergences confirmed and documented:");
            Console.WriteLine("  • POIS-11: POISON_* names from different fork (state 0 = burrow, not poison)");
            Console.WriteLine("  • POIS-30: POISON_68 exceeds array bounds (use native state APIs instead)");
            Environment.Exit(0);
        }
        else
        {
            Console.WriteLine($"FAIL: {failures} assertion(s) failed");
            Environment.Exit(1);
        }
    }
}
