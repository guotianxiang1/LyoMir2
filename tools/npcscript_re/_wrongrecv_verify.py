"""Verify: every WRONG-RECEIVER name now sits on exactly the C# switch(es) that
correspond to its native registration, and no switch has a duplicate case label."""
import sys, io, re
from collections import defaultdict
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

SRC = r"D:\loym2\.claude\wt2\m-npcscript\GameSvr\ScriptSystem\PasEngine\PasApiBridge.cs"
LINES = open(SRC, encoding="utf-8").read().split("\n")
ENTRIES = ["CallDbMethod", "GetItemProperty", "GetPlayerProperty", "SetPlayerProperty",
           "GetNpcProperty", "GetAnimalProperty", "SetNpcProperty", "CallPlayerMethod",
           "CallPlayerFunc", "CallNpcMethod", "CallNpcFunc", "CallStandaloneFunction",
           "TryCallThisPlayerFunc"]
bounds = []
for i, ln in enumerate(LINES):
    m = re.match(r"\s*public bool (\w+)\(", ln)
    if m and m.group(1) in ENTRIES:
        bounds.append((i + 1, m.group(1)))
bounds.sort()


def owner(n):
    cur = None
    for s, nm in bounds:
        if s <= n:
            cur = nm
        else:
            break
    return cur


# receiver buckets a name may legitimately live in
PLAYER = {"CallPlayerFunc", "CallPlayerMethod"}
NPC = {"CallNpcFunc", "CallNpcMethod"}
STD = {"CallStandaloneFunction"}

# native truth, from docs/m_npcscript_native_registry_20260813.txt
NATIVE = {
    "changegpswitch": {"Npc"}, "eaorderisstart": {"Std", "Npc"},
    "getaroundmonnum": {"Player", "Npc"}, "getcurrenteaidxbyname": {"Npc"},
    "getcurrenteanamebyidx": {"Std", "Npc"}, "getcurrenteaperiod": {"Npc"},
    "getcurrenteascorebyidx": {"Npc"}, "geteaorderinfo": {"Std", "Npc"},
    "getguildwargold": {"Npc"}, "getlasteaidxbyname": {"Npc"},
    "getlasteanamebyidx": {"Npc"}, "getlasteascorebyidx": {"Npc"},
    "getsomeguildpoint": {"Npc"}, "gettreatwine": {"Npc"}, "herorename": {"Npc"},
    "moveallhuminmap": {"Npc"}, "newfullmailex": {"Player", "Npc"},
    "setwinetreat": {"Npc"}, "updateeverydayactorder": {"Std", "Player", "Npc"},
    "useguildpoint": {"Npc"}, "buildguild": {"Player"},
    "chgequipmentbreaklevel": {"Player"}, "giveitemstoother": {"Player"},
    "inputdialog": {"Player", "Npc"}, "querytaskdispatch": {"Player"},
    "reqpieceupnewyearpicture": {"Player"}, "requestguildwar": {"Player"},
    "startpaodian": {"Player"}, "getscorebyname": {"Std"},
    "kickallhumtomap": {"Std"}, "playercry": {"Std"}, "playergive": {"Std"},
}

seen = defaultdict(list)
dupes = defaultdict(list)
for i, ln in enumerate(LINES):
    for m in re.finditer(r'case\s+"([^"]+)"\s*:', ln):
        o = owner(i + 1)
        dupes[(o, m.group(1))].append(i + 1)
        seen[m.group(1).lower()].append(o)

print("== duplicate case labels within one switch (would not compile) ==")
bad = {k: v for k, v in dupes.items() if len(v) > 1}
print("  none" if not bad else "\n".join("  %s : %s %s" % (k[0], k[1], v) for k, v in bad.items()))

print("\n== receiver placement ==")
ok = 0
for nm, want in sorted(NATIVE.items()):
    got = set()
    for o in seen.get(nm, []):
        got.add("Player" if o in PLAYER else "Npc" if o in NPC else
                "Std" if o in STD else o or "?")
    status = "OK " if got == want else "BAD"
    if got == want:
        ok += 1
    print("  %s %-26s want=%-22s got=%s" % (
        status, nm, ",".join(sorted(want)), ",".join(sorted(got)) or "(none)"))
print("\n  %d/%d correct" % (ok, len(NATIVE)))

# TAnimal.Level
lv = seen.get("level", [])
print("\n== TAnimal.Level 0x73AED7 + TPlayer.Level 0x72AB2F ==")
print("  case \"level\" owners: %s" % ", ".join(lv))
