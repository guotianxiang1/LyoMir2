"""Cross-check: recompute covered() against the cm-1 worktree root and confirm
the missing set (and quarter 1) is identical to the cm-2 authoritative run.
Guards against the local tree having drifted from the sibling baseline.
"""
import os
import re
import sys

CM2_TOOLS = r"D:\loym2\.claude\wt2\cm-2\tools"
sys.path.insert(0, CM2_TOOLS)
from cm2_table import build                      # noqa: E402
import cm2_csharp_cover as cov                    # noqa: E402

native = build()


def covered_for(root):
    # same logic as cov.covered() but parametrised on root
    CONST_RE = cov.CONST_RE
    CASE_RE = cov.CASE_RE
    cm = {}
    for dirpath, _, names in os.walk(os.path.join(root, "SystemModule")):
        for nm in names:
            if not nm.endswith(".cs"):
                continue
            txt = open(os.path.join(dirpath, nm), encoding="utf-8", errors="replace").read()
            for name, val in CONST_RE.findall(txt):
                try:
                    cm[name] = int(val, 0)
                except ValueError:
                    pass
    seen = {}
    for rel in cov.FILES:
        p = os.path.join(root, rel)
        if not os.path.exists(p):
            continue
        txt = open(p, encoding="utf-8", errors="replace").read()
        for raw in CASE_RE.findall(txt):
            expr = raw.strip()
            val = None
            if expr.startswith("Grobal2."):
                val = cm.get(expr.split(".", 1)[1])
            else:
                try:
                    val = int(expr, 0)
                except ValueError:
                    val = cm.get(expr)
            if val is not None:
                seen.setdefault(val, []).append(rel)
    return seen


def miss_q1(root):
    cs = covered_for(root)
    missing = sorted(k for k in native if k not in cs)
    n = len(missing)
    b = [round(n * i / 4) for i in range(5)]
    return missing, missing[b[0]:b[1]]


m2, q1_cm2 = miss_q1(r"D:\loym2\.claude\wt2\cm-2")
m1, q1_cm1 = miss_q1(r"D:\loym2\.claude\wt2\cm-1")

print("cm-2 missing=%d  cm-1 missing=%d  identical_missing=%s"
      % (len(m2), len(m1), m2 == m1))
print("q1 identical=%s" % (q1_cm2 == q1_cm1))
print("q1=%s" % q1_cm1)
