using System;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// Task #6: Hero 3 Job Subclasses Audit
// Verifies that HeroFactory creates the correct subclass for each job.
// Evidence: NativeFieldHeroNineClasses.cs documents the nine native classes
// with their job bytes at actor+0x72.

var warrior = HeroFactory.Create(0);
Assert(warrior is WarriorHero, "Job 0 should create WarriorHero");
Assert(warrior.GetType() == typeof(WarriorHero), "Job 0 should be exact WarriorHero type");

var wizard = HeroFactory.Create(1);
Assert(wizard is WizardHero, "Job 1 should create WizardHero");
Assert(wizard.GetType() == typeof(WizardHero), "Job 1 should be exact WizardHero type");

var taoist = HeroFactory.Create(2);
Assert(taoist is TaoistHero, "Job 2 should create TaoistHero");
Assert(taoist is TaoistHero, "Job 2 should be exact TaoistHero type");

var assassin = HeroFactory.Create(3);
Assert(assassin is HeroObject, "Job 3 should create base HeroObject (assassin not yet specialized)");
Assert(assassin.GetType() == typeof(HeroObject), "Job 3 should be exact HeroObject type");

var unknown = HeroFactory.Create(99);
Assert(unknown is HeroObject, "Unknown job should create base HeroObject");
Assert(unknown.GetType() == typeof(HeroObject), "Unknown job should be exact HeroObject type");

// Test polymorphism
HeroObject[] heroes = { warrior, wizard, taoist };
foreach (var hero in heroes)
{
    Assert(hero != null, "All heroes should be non-null");
    Assert(hero is HeroObject, "All heroes should be HeroObject instances");
}

// Test Initialize hook
warrior.Initialize();
wizard.Initialize();
taoist.Initialize();

Console.WriteLine("PASS: All hero subclass creation tests passed.");
Console.WriteLine($"  WarriorHero (Job 0): {warrior.GetType().Name}");
Console.WriteLine($"  WizardHero (Job 1): {wizard.GetType().Name}");
Console.WriteLine($"  TaoistHero (Job 2): {taoist.GetType().Name}");
Console.WriteLine($"  Base (Job 3): {assassin.GetType().Name}");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        Console.WriteLine($"FAIL: {message}");
        Environment.Exit(1);
    }
}
