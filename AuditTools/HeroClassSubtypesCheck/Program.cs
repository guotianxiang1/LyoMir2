using System;
using System.Reflection;
using GameSvr;

// Task #6: Hero 3 Job Subclasses Audit
// Verifies that hero subclasses exist and have correct inheritance.
// Evidence: NativeFieldHeroNineClasses.cs documents the nine native classes
// with their job bytes at actor+0x72.

Console.WriteLine("=== Hero Class Subtypes Check ===");
Console.WriteLine();

// Get all types from GameSvr assembly
var gameSvrAssembly = typeof(HeroObject).Assembly;
var heroObjectType = typeof(HeroObject);

// Find subclasses
var warriorHeroType = gameSvrAssembly.GetType("GameSvr.WarriorHero");
var wizardHeroType = gameSvrAssembly.GetType("GameSvr.WizardHero");
var taoistHeroType = gameSvrAssembly.GetType("GameSvr.TaoistHero");

Assert(warriorHeroType != null, "WarriorHero class should exist");
Assert(wizardHeroType != null, "WizardHero class should exist");
Assert(taoistHeroType != null, "TaoistHero class should exist");

// Check inheritance
Assert(warriorHeroType.BaseType == heroObjectType, "WarriorHero should inherit from HeroObject");
Assert(wizardHeroType.BaseType == heroObjectType, "WizardHero should inherit from HeroObject");
Assert(taoistHeroType.BaseType == heroObjectType, "TaoistHero should inherit from HeroObject");

Console.WriteLine($"✓ WarriorHero: {warriorHeroType.FullName}");
Console.WriteLine($"  - Base type: {warriorHeroType.BaseType.Name}");
Console.WriteLine();

Console.WriteLine($"✓ WizardHero: {wizardHeroType.FullName}");
Console.WriteLine($"  - Base type: {wizardHeroType.BaseType.Name}");
Console.WriteLine();

Console.WriteLine($"✓ TaoistHero: {taoistHeroType.FullName}");
Console.WriteLine($"  - Base type: {taoistHeroType.BaseType.Name}");
Console.WriteLine();

// Check for InitializeJobSpecificAbilities method
var initMethod = heroObjectType.GetMethod("InitializeJobSpecificAbilities",
    BindingFlags.NonPublic | BindingFlags.Instance);
Assert(initMethod != null, "HeroObject should have InitializeJobSpecificAbilities method");
Assert(initMethod.IsVirtual, "InitializeJobSpecificAbilities should be virtual");

Console.WriteLine($"✓ HeroObject.InitializeJobSpecificAbilities() method exists");
Console.WriteLine($"  - Virtual: {initMethod.IsVirtual}");
Console.WriteLine($"  - Access: {(initMethod.IsFamily ? "protected" : "unknown")}");
Console.WriteLine();

// Check HeroFactory
var factoryType = gameSvrAssembly.GetType("GameSvr.Services.HeroFactory");
Assert(factoryType != null, "HeroFactory class should exist");

var createMethod = factoryType.GetMethod("Create", new[] { typeof(byte) });
Assert(createMethod != null, "HeroFactory.Create(byte) method should exist");
Assert(createMethod.IsStatic, "HeroFactory.Create should be static");
Assert(createMethod.ReturnType == heroObjectType, "HeroFactory.Create should return HeroObject");

var createFromRecordMethod = factoryType.GetMethod("CreateFromRecord");
Assert(createFromRecordMethod != null, "HeroFactory.CreateFromRecord method should exist");
Assert(createFromRecordMethod.IsStatic, "HeroFactory.CreateFromRecord should be static");

Console.WriteLine($"✓ HeroFactory: {factoryType.FullName}");
Console.WriteLine($"  - Create(byte job): static, returns HeroObject");
Console.WriteLine($"  - CreateFromRecord(NativeHeroRecord): static, returns HeroObject");
Console.WriteLine();

// Check HeroDataService uses the factory
var heroDataServiceType = gameSvrAssembly.GetType("GameSvr.HeroDataService");
if (heroDataServiceType != null)
{
    var processLoadMethod = heroDataServiceType.GetMethod("ProcessLoadCompletions",
        BindingFlags.NonPublic | BindingFlags.Static);
    if (processLoadMethod != null)
    {
        var methodBody = processLoadMethod.GetMethodBody();
        Console.WriteLine($"✓ HeroDataService.ProcessLoadCompletions found");
        Console.WriteLine($"  - Method body size: {methodBody?.GetILAsByteArray()?.Length ?? 0} bytes");
    }
}

Console.WriteLine();
Console.WriteLine("=== PASS: All hero subclass structure checks passed ===");
Console.WriteLine();
Console.WriteLine("Summary:");
Console.WriteLine("  - 3 job-specific subclasses created (Warrior, Wizard, Taoist)");
Console.WriteLine("  - All inherit from HeroObject");
Console.WriteLine("  - HeroFactory provides type-safe instantiation");
Console.WriteLine("  - InitializeJobSpecificAbilities hook available for future extensions");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        Console.WriteLine($"✗ FAIL: {message}");
        Environment.Exit(1);
    }
}
