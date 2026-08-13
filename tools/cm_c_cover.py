"""Audit the coverage side of cm_c_invent: show, for every native real ident,
exactly which C# case arm claims it, so bogus literal matches in unrelated
switches can be spotted before anything is declared MISSING."""
import json
import os
import re
import sys

CSROOT = r"D:/loym2/.claude/wt2/cm-3"
OUTDIR = r"D:/loym2/staging/m_cm_c"
walk = json.load(open(os.path.join(OUTDIR, "walk.json")))
real = {int(k): v[0] for k, v in walk["real"].items()}

consts = {}
for sub in ("SystemModule", "GameSvr"):
    for root, _d, files in os.walk(os.path.join(CSROOT, sub)):
        if "\\obj\\" in root or "\\bin\\" in root:
            continue
        for f in files:
            if not f.endswith(".cs"):
                continue
            for ln in open(os.path.join(root, f), encoding="utf-8-sig", errors="replace"):
                m = re.search(r"\b(?:const\s+\w+\s+)?((?:CM_|SM_|RM_)[A-Za-z0-9_]+|[A-Za-z][A-Za-z0-9_]*)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*;", ln)
                if m:
                    consts.setdefault(m.group(1), int(m.group(2), 0))

hits = {}
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
                    hits.setdefault(consts[nm], []).append(("name", nm, "%s:%d" % (rel, n)))
            for m in re.finditer(r"case\s+(0x[0-9A-Fa-f]+|\d+)\s*:", ln):
                hits.setdefault(int(m.group(1), 0), []).append(("lit", m.group(1), "%s:%d" % (rel, n)))

out = []
litonly = []
for i in sorted(real):
    h = hits.get(i)
    if not h:
        continue
    kinds = {k for k, _v, _s in h}
    if kinds == {"lit"}:
        litonly.append(i)
        out.append("LIT-ONLY %6d 0x%04X -> %s" % (i, i, real[i]))
        for k, v, s in h:
            out.append("            %s %s  %s" % (k, v, s))
out.insert(0, "native real idents covered ONLY by a numeric case literal: %d" % len(litonly))
open(os.path.join(OUTDIR, "cover.txt"), "w", encoding="utf-8").write("\n".join(out))
sys.stdout.reconfigure(encoding="utf-8")
print("\n".join(out))
