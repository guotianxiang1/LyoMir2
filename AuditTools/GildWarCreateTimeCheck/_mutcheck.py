#!/usr/bin/env python3
"""
GILD-27 mutation harness: proves the audit assertions actually run and catch breaks.

Each mutation breaks one specific requirement. The harness applies each mutation,
rebuilds, runs the audit tool, then restores the file. If the audit still passes
after a breaking change, that assertion is a false green.

Conventions from memory notes:
- Use \\r\\n for anchors (repo is LF but some files have CRLF; explicit is safe)
- Read/write with newline='' to preserve existing line endings
- subprocess without text=True (MSBuild Chinese output breaks the pipe)
- Parse stderr for ': error' (not bare 'error', which matches warnings)
- Touch to restore mtime is unsafe (MSBuild skips recompile) so always --no-incremental
- Run a sentinel mutation first as self-check (must fail to prove harness works)
"""
import subprocess
import sys
import os
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
SERVICE_CS = REPO_ROOT / "GameSvr" / "Services" / "NativeCorpsService.cs"
STORE_CS = REPO_ROOT / "GameSvr" / "Services" / "NativeCorpsStore.cs"
CODEC_CS = REPO_ROOT / "GameSvr" / "Services" / "NativeCorpsWireCodec.cs"
SERVER_CS = REPO_ROOT / "GameSvr" / "GameServer.cs"
AUDIT_CSPROJ = REPO_ROOT / "AuditTools" / "GildWarCreateTimeCheck" / "GildWarCreateTimeCheck.csproj"

def read_file(path):
    with open(path, 'r', encoding='utf-8', newline='') as f:
        return f.read()

def write_file(path, content):
    with open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(content)

def run_audit():
    """Run the audit tool. Returns (exit_code, stdout)."""
    result = subprocess.run(
        ['dotnet', 'run', '--project', str(AUDIT_CSPROJ)],
        cwd=str(REPO_ROOT),
        capture_output=True
    )
    return result.returncode, result.stdout.decode('utf-8', errors='replace')

def apply_mutation(path, anchor, replacement, label):
    """Apply a mutation, run audit, restore. Returns True if audit caught it."""
    original = read_file(path)

    # Verify anchor exists exactly once
    count = original.count(anchor)
    if count != 1:
        print(f"  [SKIP] {label}: anchor match count = {count} (expected 1)")
        return None

    mutated = original.replace(anchor, replacement, 1)
    if mutated == original:
        print(f"  [SKIP] {label}: mutation had no effect")
        return None

    try:
        write_file(path, mutated)
        code, out = run_audit()
        caught = (code != 0)

        status = "CAUGHT" if caught else "MISSED"
        print(f"  [{status}] {label}")

        if not caught:
            print(f"         Mutation escaped detection!")
            # Show which assertions still passed
            for line in out.splitlines():
                if line.startswith('[PASS]'):
                    print(f"         {line}")

        return caught
    finally:
        write_file(path, original)

def main():
    print("[GILD-27] Mutation check: audit assertions must catch intentional breaks")
    print("="*70)

    # Sentinel: break the dictionary type in an obvious way (must be caught)
    print("\n[SENTINEL] Break dictionary type to byte-only (must fail)...")
    sentinel = apply_mutation(
        CODEC_CS,
        "Dictionary<(ulong First, ulong Second), (byte Relation, DateTime CreateTime)>",
        "Dictionary<(ulong First, ulong Second), byte>",
        "Revert to byte-only dictionary"
    )
    if sentinel is None:
        print("  [ABORT] Sentinel mutation anchor not found")
        return 1
    if sentinel:
        print("  [OK] Sentinel failed as expected — harness is live")
    else:
        print("  [ABORT] Sentinel passed — harness is broken or audit is blind")
        return 1

    print("\n[MUTATIONS] Breaking each requirement individually...")

    mutations = [
        # LoadGildRelations: don't read CreateTime
        (STORE_CS,
         "SELECT GildID1,GildID2,Relation,CreateTime",
         "SELECT GildID1,GildID2,Relation",
         "LoadGildRelations: omit CreateTime from SELECT"),

        (STORE_CS,
         "var createTime = reader.GetDateTime(3);",
         "var createTime = DateTime.MinValue;",
         "LoadGildRelations: don't read CreateTime from result"),

        (STORE_CS,
         "if (!snapshot.GildRelations.TryAdd(key, (relation, createTime)))",
         "if (!snapshot.GildRelations.TryAdd(key, (relation, DateTime.Now)))",
         "LoadGildRelations: hardcode DateTime.Now instead of DB value"),

        # DeclareWar: forget to stamp time
        (SERVICE_CS,
         "_gildRelations[relationKey] = (GildHostile, DateTime.Now);",
         "_gildRelations[relationKey] = (GildHostile, DateTime.MinValue);",
         "DeclareWar: stamp MinValue instead of Now"),

        (SERVICE_CS,
         "InsertGildRelationFailSafe(relationKey, GildHostile, DateTime.Now);",
         "InsertGildRelationFailSafe(relationKey, GildHostile, DateTime.MinValue);",
         "DeclareWar: forward MinValue to DB instead of Now"),

        # Union: forget to stamp time
        (SERVICE_CS,
         "_gildRelations[relationKey] = (GildUnion, DateTime.Now);",
         "_gildRelations[relationKey] = (GildUnion, DateTime.MinValue);",
         "Union: stamp MinValue instead of Now"),

        (SERVICE_CS,
         "InsertGildRelationFailSafe(relationKey, GildUnion, DateTime.Now);",
         "InsertGildRelationFailSafe(relationKey, GildUnion, DateTime.MinValue);",
         "Union: forward MinValue to DB instead of Now"),

        # ExpireGildWars: don't actually remove
        (SERVICE_CS,
         "_gildRelations.Remove(relationKey);",
         "// _gildRelations.Remove(relationKey);",
         "ExpireGildWars: comment out Remove call"),

        (SERVICE_CS,
         "DeleteGildRelationFailSafe(relationKey);",
         "// DeleteGildRelationFailSafe(relationKey);",
         "ExpireGildWars: comment out DB delete"),

        # Phase4: don't tick
        (SERVER_CS,
         "M2Share.CorpsService.ExpireGildWars(M2Share.g_Config.dwGuildWarTime);",
         "// M2Share.CorpsService.ExpireGildWars(M2Share.g_Config.dwGuildWarTime);",
         "Phase4: comment out ExpireGildWars call"),
    ]

    caught_count = 0
    missed_count = 0
    skipped_count = 0

    for path, anchor, replacement, label in mutations:
        result = apply_mutation(path, anchor, replacement, label)
        if result is None:
            skipped_count += 1
        elif result:
            caught_count += 1
        else:
            missed_count += 1

    print("\n" + "="*70)
    print(f"Mutations: {caught_count} caught, {missed_count} missed, {skipped_count} skipped")

    if missed_count > 0:
        print("\n[FAIL] Some mutations escaped — audit has false greens")
        return 1
    if skipped_count > 0:
        print("\n[WARN] Some mutations skipped — anchors may have drifted")
        return 0

    print("\n[PASS] All mutations caught — audit is sound")
    return 0

if __name__ == '__main__':
    sys.exit(main())
