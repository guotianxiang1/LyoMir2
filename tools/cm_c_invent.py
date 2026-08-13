"""CM batch-3 agent: which native dispatch idents have no C# arm.

Cross-references the independently restored native tree (cm_c_walk.py) against
every `case <ident>:` reachable from the C# TPlayObject.Operate switch family.
Classifies each native ident as COVERED / MISSING, and separately lists the
DEFAULT-target idents (native no-op) so a bare `break;` can be recognised as
faithful rather than as a hole.

Writes staging/m_cm_c/invent.txt + invent.json
"""
import json
import os
import re
import sys

CSROOT = r"D:/loym2/.claude/wt2/cm-3"
OUTDIR = r"D:/loym2/staging/m_cm_c"

walk = json.load(open(os.path.join(OUTDIR, "walk.json")))
real = {int(k): v[0] for k, v in walk["real"].items()}
placeholders = set(walk["placeholders"])

# ---- constants -----------------------------------------------------------
consts = {}
for root, _d, files in os.walk(os.path.join(CSROOT, "SystemModule")):
    for f in files:
        if not f.endswith(".cs"):
            continue
        for ln in open(os.path.join(root, f), encoding="utf-8-sig", errors="replace"):
            m = re.search(r"\b(CM_|SM_|RM_)([A-Za-z0-9_]+)\s*=\s*(0x[0-9A-Fa-f]+|-?\d+)\s*;", ln)
            if m:
                consts[m.group(1) + m.group(2)] = int(m.group(3), 0)
for root, _d, files in os.walk(os.path.join(CSROOT, "GameSvr")):
    for f in files:
        if not f.endswith(".cs"):
            continue
        for ln in open(os.path.join(root, f), encoding="utf-8-sig", errors="replace"):
            m = re.search(r"\b(?:const\s+(?:int|ushort|short|byte)\s+)([A-Za-z0-9_]+)\s*=\s*(0x[0-9A-Fa-f]+|-?\d+)\s*;", ln)
            if m:
                consts.setdefault(m.group(1), int(m.group(2), 0))

byval = {}
for n, v in consts.items():
    byval.setdefault(v, []).append(n)

# ---- case arms -----------------------------------------------------------
covered = {}
for root, _d, files in os.walk(os.path.join(CSROOT, "GameSvr")):
    if "\\obj\\" in root or "\\bin\\" in root:
        continue
    for f in files:
        if not f.endswith(".cs"):
            continue
        p = os.path.join(root, f)
        rel = os.path.relpath(p, CSROOT).replace("\\", "/")
        for n, ln in enumerate(open(p, encoding="utf-8-sig", errors="replace"), 1):
            for m in re.finditer(r"case\s+(?:Grobal2\.)?([A-Za-z_][A-Za-z0-9_.]*)\s*:", ln):
                nm = m.group(1).split(".")[-1]
                if nm in consts:
                    covered.setdefault(consts[nm], []).append("%s:%d %s" % (rel, n, nm))
            for m in re.finditer(r"case\s+(0x[0-9A-Fa-f]+|\d+)\s*:", ln):
                covered.setdefault(int(m.group(1), 0), []).append("%s:%d lit" % (rel, n))

# ---- classify ------------------------------------------------------------
missing = [i for i in sorted(real) if i not in covered]
present = [i for i in sorted(real) if i in covered]

out = ["native tree: real=%d placeholders=%d" % (len(real), len(placeholders)),
       "C# covered idents overlapping native real: %d" % len(present),
       "MISSING (native real handler, no C# case arm): %d" % len(missing),
       ""]

q = len(missing)
b = [(q * k) // 4 for k in range(5)]
out.append("quartile split of MISSING by ascending ident:")
for k in range(4):
    seg = missing[b[k]:b[k + 1]]
    out.append("  Q%d n=%d  %d..%d" % (k + 1, len(seg), seg[0], seg[-1]))
out.append("")
out.append("=== MISSING detail ===")
for k in range(4):
    seg = missing[b[k]:b[k + 1]]
    out.append("--- Q%d ---" % (k + 1))
    for i in seg:
        nm = ",".join(sorted(byval.get(i, []))) or "(no C# constant)"
        ph = " PLACEHOLDER" if i in placeholders else ""
        out.append("  %6d 0x%04X -> %s  %s%s" % (i, i, real[i], nm, ph))

out.append("")
out.append("=== native DEFAULT-target idents (native no-op) ===")
out.append(" ".join(str(i) for i in sorted(placeholders)))

open(os.path.join(OUTDIR, "invent.txt"), "w", encoding="utf-8").write("\n".join(out))
json.dump({"missing": missing, "bounds": b,
           "q3": missing[b[2]:b[3]],
           "real": {str(k): v for k, v in real.items()},
           "names": {str(k): sorted(byval.get(k, [])) for k in real}},
          open(os.path.join(OUTDIR, "invent.json"), "w"), indent=0)
sys.stdout.reconfigure(encoding="utf-8")
print("\n".join(out[:12]))
