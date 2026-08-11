#!/usr/bin/env python3
"""
Mutation test for STATE-32 batch2 (band 0x2B..0x50).
Tests handlers for states 0x2B, 0x36, 0x4E (0x2C is BLOCKED).
Each mutation breaks one handler; build should fail with assertion.
"""
import subprocess
import sys
import os
from pathlib import Path

TARGET_FILE = Path("D:/loym2/LyoMir2-master/GameSvr/Actors/TBaseObject.NativeStateBandBonus.cs")

# Mutations: each tuple is (description, old_text, new_text)
MUTATIONS = [
    # State 0x2B: SC range handler
    ("0x2B: break SC low addition",
     "int scLow = HUtil32.LoWord(m_WAbil.SC) + v;",
     "int scLow = HUtil32.LoWord(m_WAbil.SC);  // MUTATION: removed + v"),

    ("0x2B: break SC high addition",
     "int scHigh = HUtil32.HiWord(m_WAbil.SC) + v;",
     "int scHigh = HUtil32.HiWord(m_WAbil.SC);  // MUTATION: removed + v"),

    ("0x2B: wrong state constant in assertion",
     "StateRecalcAuditTools.AssertStateBandHandler(0x2B, 0x77356E,",
     "StateRecalcAuditTools.AssertStateBandHandler(0x2A, 0x77356E,  // MUTATION: wrong state"),

    # State 0x36: AC/MAC subtract handler
    ("0x36: break AC low subtraction",
     "int acLow = Math.Max(HUtil32.LoWord(m_WAbil.AC) - v, 0);",
     "int acLow = Math.Max(HUtil32.LoWord(m_WAbil.AC), 0);  // MUTATION: removed - v"),

    ("0x36: break AC high subtraction",
     "int acHigh = Math.Max(HUtil32.HiWord(m_WAbil.AC) - v, 0);",
     "int acHigh = Math.Max(HUtil32.HiWord(m_WAbil.AC), 0);  // MUTATION: removed - v"),

    ("0x36: break MAC low subtraction",
     "int macLow = Math.Max(HUtil32.LoWord(m_WAbil.MAC) - v, 0);",
     "int macLow = Math.Max(HUtil32.LoWord(m_WAbil.MAC), 0);  // MUTATION: removed - v"),

    ("0x36: break MAC high subtraction",
     "int macHigh = Math.Max(HUtil32.HiWord(m_WAbil.MAC) - v, 0);",
     "int macHigh = Math.Max(HUtil32.HiWord(m_WAbil.MAC), 0);  // MUTATION: removed - v"),

    ("0x36: remove clamp (allow negative)",
     "int acLow = Math.Max(HUtil32.LoWord(m_WAbil.AC) - v, 0);",
     "int acLow = HUtil32.LoWord(m_WAbil.AC) - v;  // MUTATION: removed Math.Max clamp"),

    # State 0x4E: CC range handler
    ("0x4E: break CC low addition",
     "m_NativeCoreWorkingAbility.CCLow += v;",
     "m_NativeCoreWorkingAbility.CCLow += 0;  // MUTATION: changed v to 0"),

    ("0x4E: break CC high addition",
     "m_NativeCoreWorkingAbility.CCHigh += v;",
     "m_NativeCoreWorkingAbility.CCHigh += 0;  // MUTATION: changed v to 0"),

    # Band range checks
    ("Band check: wrong lower bound",
     "if (state < 0x2B || state > 0x50)",
     "if (state < 0x2A || state > 0x50)  // MUTATION: wrong lower bound"),

    ("Band check: wrong upper bound",
     "if (state < 0x2B || state > 0x50)",
     "if (state < 0x2B || state > 0x51)  // MUTATION: wrong upper bound"),

    # Switch dispatch
    ("Switch: remove 0x2B case",
     "case 0x2B:\n                        ApplyStateBand_0x2B_SCRange(v, callerPath);\n                        break;",
     "// case 0x2B: removed  // MUTATION"),

    ("Switch: remove 0x36 case",
     "case 0x36:\n                        ApplyStateBand_0x36_ACMACSubtract(v, callerPath);\n                        break;",
     "// case 0x36: removed  // MUTATION"),

    ("Switch: remove 0x4E case",
     "case 0x4E:\n                        ApplyStateBand_0x4E_CCRange(v, callerPath);\n                        break;",
     "// case 0x4E: removed  // MUTATION"),
]

def build_project():
    """Build GameSvr project. Returns True if successful."""
    result = subprocess.run(
        ["dotnet", "build", "GameSvr/GameSvr.csproj", "--no-incremental", "-v", "q"],
        cwd="D:/loym2/LyoMir2-master",
        capture_output=True,
        text=False,  # Don't decode as text to avoid encoding issues
        timeout=120
    )
    # Check for ": error" in output (byte search to avoid encoding issues)
    has_error = b': error' in (result.stdout or b'') or b': error' in (result.stderr or b'')
    return result.returncode == 0 and not has_error

def run_mutation_test(description, old_text, new_text):
    """Run one mutation test. Returns True if mutation was killed."""
    print(f"  Testing: {description}")

    # Read file
    with open(TARGET_FILE, 'r', encoding='utf-8', newline='') as f:
        original = f.read()

    # Verify old_text exists exactly once
    count = original.count(old_text)
    if count == 0:
        print(f"    ❌ SKIP: old_text not found")
        return None
    if count > 1:
        print(f"    ❌ SKIP: old_text appears {count} times (not unique)")
        return None

    # Apply mutation
    mutated = original.replace(old_text, new_text, 1)
    with open(TARGET_FILE, 'w', encoding='utf-8', newline='') as f:
        f.write(mutated)

    # Build
    try:
        build_success = build_project()

        if not build_success:
            print(f"    ✓ KILLED (build failed as expected)")
            result = True
        else:
            print(f"    ✗ SURVIVED (build succeeded, mutation not detected)")
            result = False
    finally:
        # Restore original
        with open(TARGET_FILE, 'w', encoding='utf-8', newline='') as f:
            f.write(original)

    return result

def main():
    print("STATE-32 batch2 mutation test")
    print(f"Target: {TARGET_FILE}")
    print(f"Mutations: {len(MUTATIONS)}")
    print()

    # Sentinel: verify clean build succeeds
    print("Sentinel: verifying clean build...")
    if not build_project():
        print("❌ FAIL: Clean build failed. Fix errors before running mutations.")
        return 1
    print("✓ Clean build succeeded\n")

    killed = 0
    survived = 0
    skipped = 0

    for i, (desc, old, new) in enumerate(MUTATIONS, 1):
        print(f"Mutation {i}/{len(MUTATIONS)}:")
        result = run_mutation_test(desc, old, new)
        if result is True:
            killed += 1
        elif result is False:
            survived += 1
        else:
            skipped += 1
        print()

    print("="*60)
    print(f"Results: {killed} killed, {survived} survived, {skipped} skipped")
    print(f"Kill rate: {killed}/{killed+survived} = {100*killed/(killed+survived) if (killed+survived) > 0 else 0:.1f}%")

    if survived > 0:
        print(f"\n❌ FAIL: {survived} mutations survived")
        return 1
    else:
        print(f"\n✓ PASS: All {killed} mutations killed")
        return 0

if __name__ == '__main__':
    sys.exit(main())
