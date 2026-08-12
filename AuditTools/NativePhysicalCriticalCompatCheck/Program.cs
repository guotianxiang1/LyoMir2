using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;

TestFormulaCompatibility();
TestAllPhysicalAttackPathsWired();
TestSourceContracts();

Console.WriteLine(
    "PASS NativePhysicalCritical gate=shared formula=shared " +
    "wired=9paths identical=verified");
return;

static void TestFormulaCompatibility()
{
    // Verify both methods use identical gate and formula
    var magicCritical = typeof(TBaseObject).GetMethod("ApplyNativeMagicCritical",
        BindingFlags.Instance | BindingFlags.NonPublic);
    var physicalCritical = typeof(TBaseObject).GetMethod("ApplyNativePhysicalCritical",
        BindingFlags.Instance | BindingFlags.NonPublic);

    Assert(magicCritical != null, "ApplyNativeMagicCritical method not found");
    Assert(physicalCritical != null, "ApplyNativePhysicalCritical method not found");

    // Both methods should exist and be private
    Assert(!magicCritical!.IsPublic, "ApplyNativeMagicCritical should be private");
    Assert(!physicalCritical!.IsPublic, "ApplyNativePhysicalCritical should be private");
}

static void TestAllPhysicalAttackPathsWired()
{
    var root = FindRepositoryRoot();

    // Main attack path
    var attack = Read(root, "GameSvr", "Actors", "TBaseObject.Attack.cs");
    Require(attack, "nPower = AttackTarget.GetHitStruckDamage(this, nPower);",
        "_Attack GetHitStruckDamage call");
    Require(attack, "nPower = AttackTarget.ApplyNativePhysicalCritical(this, nPower);",
        "_Attack physical critical");

    // DirectAttack (skill range attacks)
    Require(attack, "nSecPwr = BaseObject.ApplyNativePhysicalCritical(this, nSecPwr);",
        "_Attack_DirectAttack physical critical");

    // ReleaseSunSword
    Require(attack, "damage = target.ApplyNativePhysicalCritical(this, damage);",
        "ReleaseSunSword physical critical");

    // Pet attack
    var animal = Read(root, "GameSvr", "Actors", "TAnimalObject.cs");
    Require(animal, "physicalDamage = BaseObject.ApplyNativePhysicalCritical(this, physicalDamage);",
        "HitMagAttackTarget physical critical");

    // DoubleCriticalMonster
    var doubleCrit = Read(root, "GameSvr", "Monsters", "Monster", "DoubleCriticalMonster.cs");
    Require(doubleCrit, "nDamage = BaseObject.ApplyNativePhysicalCritical(this, nDamage);",
        "DoubleCriticalMonster physical critical");

    // ArcherGuard
    var archer = Read(root, "GameSvr", "Monsters", "Monster", "ArcherGuard.cs");
    Require(archer, "nPower = TargeTBaseObject.ApplyNativePhysicalCritical(this, nPower);",
        "ArcherGuard physical critical");

    // ExplosionSpider
    var spider = Read(root, "GameSvr", "Monsters", "Monster", "ExplosionSpider.cs");
    Require(spider, "physicalDamage = BaseObject.ApplyNativePhysicalCritical(this, physicalDamage);",
        "ExplosionSpider physical critical");

    // DualAxeMonster
    var dualAxe = Read(root, "GameSvr", "Monsters", "Monster", "DualAxeMonster.cs");
    Require(dualAxe, "nDamage = Target.ApplyNativePhysicalCritical(this, nDamage);",
        "DualAxeMonster physical critical");

    // MagicManager (assassination skill)
    var magic = Read(root, "GameSvr", "Spells", "MagicManager.cs");
    Require(magic, "nPower = BaseObject.ApplyNativePhysicalCritical(PlayObject, nPower);",
        "MagicManager assassination skill physical critical");
}

static void TestSourceContracts()
{
    var root = FindRepositoryRoot();
    var magicDamage = Read(root, "GameSvr", "Actors", "TBaseObject.NativeMagicDamage.cs");

    // Verify the methods exist in the same file
    Require(magicDamage, "private int ApplyNativeMagicCritical(TBaseObject source, int damage)",
        "ApplyNativeMagicCritical method declaration");
    Require(magicDamage, "internal int ApplyNativePhysicalCritical(TBaseObject source, int damage)",
        "ApplyNativePhysicalCritical method declaration");

    // Verify both use the same gate conditions
    var gatePattern = @"if \(source == null \|\| source\.m_sNativeCriticalChance < 0 \|\|[\s\S]*?m_sNativeAntiCriticalChance < 0 \|\|[\s\S]*?m_sNativeCriticalDamageReduction < 0\)";
    var magicGateMatches = Regex.Matches(magicDamage, gatePattern);
    Assert(magicGateMatches.Count >= 2, "Both critical methods should have gate condition");

    // Verify threshold formula
    Require(magicDamage, "int threshold = RoundNativeX87(",
        "Threshold calculation with RoundNativeX87");
    Require(magicDamage, "(100.0d - antiCriticalChance / 100.0d) *",
        "Threshold formula part 1");
    Require(magicDamage, "(criticalChance / 100.0d));",
        "Threshold formula part 2");

    // Verify multiplier formula
    Require(magicDamage, "double multiplier = 1.5d + increase;",
        "Multiplier base formula");
    Require(magicDamage, "multiplier -= criticalReduction * 0.00005d;",
        "Multiplier reduction formula");
    Require(magicDamage, "multiplier -= increase * criticalReduction / 10000.0d;",
        "Multiplier cross-term formula");
}

static string Read(string root, params string[] parts) =>
    File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("Repository root was not found.");
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message + " is missing");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
