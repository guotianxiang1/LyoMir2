"""Rank the latter-half CM idents (3284..4651) by real production traffic.

Joins three sources:
  * native dispatch tree (walk.json, independently re-walked)
  * production gateway counters (client_ / srv_AppearTimes.ini)
  * C# `case Grobal2.CM_*` dispatch arms in GameSvr

Writes staging/m_cm_b/traffic.txt + traffic.json
"""
import json
import os
import re

CSROOT = r"D:/loym2/.claude/wt2/m-cm-b"
OUTDIR = r"D:/loym2/staging/m_cm_b"
GATE = "D:/\u5149\u5934\u5367\u9f99/mud2.0/GateServer/GameGate2/procMsgLog"

LO, HI = 3284, 4651


def load_ini(path):
    d = {}
    for ln in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r"^(\d+)=(\d+)\s*$", ln.strip())
        if m:
            d[int(m.group(1))] = int(m.group(2))
    return d


client = load_ini(os.path.join(GATE, "client_AppearTimes.ini"))
srv = load_ini(os.path.join(GATE, "srv_AppearTimes.ini"))

walk = json.load(open(os.path.join(OUTDIR, "walk.json")))
real = {int(k): int(v[0], 16) for k, v in walk["real"].items()}
mine = [i for i in sorted(real) if LO <= i <= HI]

# ---- C# side -------------------------------------------------------------
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
for base, _dirs, files in os.walk(os.path.join(CSROOT, "GameSvr")):
    for f in files:
        if not f.endswith(".cs"):
            continue
        p = os.path.join(base, f)
        rel = os.path.relpath(p, CSROOT).replace("\\", "/")
        for n, ln in enumerate(open(p, encoding="utf-8-sig", errors="replace"), 1):
            for m in re.finditer(r"case\s+(?:Grobal2\.)?(CM_[A-Za-z0-9_]+)\s*:", ln):
                cases.setdefault(m.group(1), []).append("%s:%d" % (rel, n))

rows = []
for i in mine:
    names = byval.get(i, [])
    live = [(n, cases[n]) for n in names if n in cases]
    rows.append({
        "ident": i,
        "handler": "%08X" % real[i],
        "client": client.get(i, 0),
        "srv": srv.get(i, 0),
        "consts": names,
        "cs": live[0][1][0] if live else None,
        "state": "PRESENT" if live else "MISSING",
    })

rows.sort(key=lambda r: (-r["client"], -r["srv"], r["ident"]))

out = ["latter range %d..%d  handlers=%d" % (LO, HI, len(mine)),
       "PRESENT=%d  MISSING=%d" % (sum(1 for r in rows if r["state"] == "PRESENT"),
                                   sum(1 for r in rows if r["state"] == "MISSING")),
       "with client traffic > 0 : %d" % sum(1 for r in rows if r["client"]),
       "with srv traffic > 0    : %d" % sum(1 for r in rows if r["srv"]),
       "",
       "%-6s %-9s %-10s %-9s %-8s %s" % ("ident", "handler", "client", "srv", "state", "cs")]
for r in rows:
    out.append("%-6d %-9s %-10d %-9d %-8s %s"
               % (r["ident"], r["handler"], r["client"], r["srv"], r["state"],
                  r["cs"] or (",".join(r["consts"]) or "-")))

out.append("")
out.append("=== hot list (client traffic, PRESENT -> audit for DIVERGENT) ===")
for r in rows:
    if r["client"] and r["state"] == "PRESENT":
        out.append("  %6d  %8d  %s  %s" % (r["ident"], r["client"], r["handler"], r["cs"]))
out.append("=== hot list (client traffic, MISSING -> implement) ===")
for r in rows:
    if r["client"] and r["state"] == "MISSING":
        out.append("  %6d  %8d  %s" % (r["ident"], r["client"], r["handler"]))
out.append("=== zero client traffic, MISSING (register only) ===")
out.append("  " + " ".join(str(r["ident"]) for r in rows
                           if not r["client"] and r["state"] == "MISSING"))

open(os.path.join(OUTDIR, "traffic.txt"), "w", encoding="utf-8").write("\n".join(out))
json.dump(rows, open(os.path.join(OUTDIR, "traffic.json"), "w"), indent=1)
print("\n".join(out[:5]))
