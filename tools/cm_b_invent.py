"""Find C# CM dispatch arms in 3284..4651 that the native tree does not reach.

Three buckets for every C# `case Grobal2.CM_*` whose value falls in the range:
  REAL         native jump table points at a real handler
  PLACEHOLDER  native table slot exists but targets the default label 0x6DBC2C
               (REPLICATION_RULES 5.1.6: a bare `break;` there is FAITHFUL)
  ABSENT       ident is not reachable in the native tree at all -> INVENTED
               candidate, subject to the whole-image multi-encoding scan

Also flags constants that share one numeric value under several names, which is
how the mobile-protocol aliases reuse a native ident for a different meaning.
"""
import json
import os
import re
import sys

CSROOT = r"D:/loym2/.claude/wt2/m-cm-b"
OUTDIR = r"D:/loym2/staging/m_cm_b"
LO, HI = 3284, 4651

walk = json.load(open(os.path.join(OUTDIR, "walk.json")))
real = {int(k) for k in walk["real"]}
placeholders = set(walk["placeholders"])

consts = {}
g2 = os.path.join(CSROOT, "SystemModule", "Grobal2.cs")
for ln in open(g2, encoding="utf-8-sig"):
    m = re.search(r"\b(CM_[A-Za-z0-9_]+)\s*=\s*(0x[0-9A-Fa-f]+|-?\d+)\s*;", ln)
    if m:
        consts[m.group(1)] = int(m.group(2), 0)

byval = {}
for n, v in consts.items():
    byval.setdefault(v, []).append(n)

cases = {}
for base, _d, files in os.walk(os.path.join(CSROOT, "GameSvr")):
    for f in files:
        if not f.endswith(".cs"):
            continue
        p = os.path.join(base, f)
        rel = os.path.relpath(p, CSROOT).replace("\\", "/")
        for n, ln in enumerate(open(p, encoding="utf-8-sig", errors="replace"), 1):
            for m in re.finditer(r"case\s+(?:Grobal2\.)?(CM_[A-Za-z0-9_]+)\s*:", ln):
                cases.setdefault(m.group(1), []).append("%s:%d" % (rel, n))

out = []
buckets = {"REAL": [], "PLACEHOLDER": [], "ABSENT": []}
for name, sites in sorted(cases.items()):
    v = consts.get(name)
    if v is None or not (LO <= v <= HI):
        continue
    b = "REAL" if v in real else "PLACEHOLDER" if v in placeholders else "ABSENT"
    buckets[b].append((v, name, sites[0], byval.get(v, [])))

out.append("C# CM dispatch arms with ident in %d..%d" % (LO, HI))
for b in ("ABSENT", "PLACEHOLDER", "REAL"):
    out.append("")
    out.append("=== %s : %d ===" % (b, len(buckets[b])))
    for v, name, site, aliases in sorted(buckets[b]):
        al = (" aliases=%s" % ",".join(a for a in aliases if a != name)) \
            if len(aliases) > 1 else ""
        out.append("  %5d  %-34s %s%s" % (v, name, site, al))

# multi-name constants in range, regardless of whether they have a case arm
out.append("")
out.append("=== constants in range sharing one value ===")
for v, names in sorted(byval.items()):
    if LO <= v <= HI and len(names) > 1:
        state = "REAL" if v in real else "PLACEHOLDER" if v in placeholders else "ABSENT"
        out.append("  %5d  %-12s %s" % (v, state, ", ".join(sorted(names))))

# native real handlers with no C# arm at all
have = {consts[n] for n in cases if n in consts}
out.append("")
out.append("=== native REAL handlers in range with no C# case arm ===")
miss = [i for i in sorted(real) if LO <= i <= HI and i not in have]
out.append("  count=%d" % len(miss))
out.append("  " + " ".join(str(i) for i in miss))

open(os.path.join(OUTDIR, "invent.txt"), "w", encoding="utf-8").write("\n".join(out))
sys.stdout.reconfigure(encoding="utf-8")
print("\n".join(out))
