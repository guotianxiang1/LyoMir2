"""Locate every WRONG-RECEIVER case label inside PasApiBridge.cs and print the
exact case block so it can be moved to the switch its native registration
actually belongs to."""
import sys, io, re, os
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

SRC = r"D:\loym2\.claude\wt2\m-npcscript\GameSvr\ScriptSystem\PasEngine\PasApiBridge.cs"
LINES = open(SRC, encoding="utf-8").read().split("\n")

ENTRIES = [
    "CallDbMethod", "GetItemProperty", "GetPlayerProperty", "SetPlayerProperty",
    "GetNpcProperty", "GetAnimalProperty", "SetNpcProperty", "CallPlayerMethod",
    "CallPlayerFunc", "CallNpcMethod", "CallNpcFunc", "CallStandaloneFunction",
    "TryCallThisPlayerFunc",
]
bounds = []
for i, ln in enumerate(LINES):
    m = re.match(r"\s*public bool (\w+)\(", ln)
    if m and m.group(1) in ENTRIES:
        bounds.append((i + 1, m.group(1)))
bounds.sort()


def owner(lineno):
    cur = None
    for start, nm in bounds:
        if start <= lineno:
            cur = nm
        else:
            break
    return cur


# native registration -> the C# switch it must live in
WANT = {
    # native TPsNpc  (This_Npc)  -> CallNpcFunc / CallNpcMethod
    "changegpswitch": "Npc", "eaorderisstart": "Npc", "getaroundmonnum": "Npc",
    "getcurrenteaidxbyname": "Npc", "getcurrenteanamebyidx": "Npc",
    "getcurrenteaperiod": "Npc", "getcurrenteascorebyidx": "Npc",
    "geteaorderinfo": "Npc", "getguildwargold": "Npc",
    "getlasteaidxbyname": "Npc", "getlasteanamebyidx": "Npc",
    "getlasteascorebyidx": "Npc", "getsomeguildpoint": "Npc",
    "gettreatwine": "Npc", "herorename": "Npc", "moveallhuminmap": "Npc",
    "newfullmailex": "Both", "setwinetreat": "Npc",
    "updateeverydayactorder": "Npc", "useguildpoint": "Npc",
    # native TPlayer (This_Player) -> CallPlayerFunc / CallPlayerMethod
    "buildguild": "Player", "chgequipmentbreaklevel": "Player",
    "giveitemstoother": "Player", "inputdialog": "Both",
    "querytaskdispatch": "Player", "reqpieceupnewyearpicture": "Player",
    "requestguildwar": "Player", "startpaodian": "Player",
    # native global function -> CallStandaloneFunction
    "getscorebyname": "Standalone", "kickallhumtomap": "Standalone",
    "playercry": "Standalone", "playergive": "Standalone",
    # TAnimal.Level
    "level": "Animal",
}

found = {}
for i, ln in enumerate(LINES):
    for m in re.finditer(r'case\s+"([^"]+)"\s*:', ln):
        nm = m.group(1)
        if nm.lower() in WANT:
            found.setdefault(nm.lower(), []).append((i + 1, owner(i + 1), ln.strip()))

for nm in sorted(WANT):
    hits = found.get(nm, [])
    print("%-28s want=%-10s  %s" % (
        nm, WANT[nm],
        "; ".join("%s:%d" % (o, l) for l, o, _ in hits) or "*** NO CASE FOUND ***"))
