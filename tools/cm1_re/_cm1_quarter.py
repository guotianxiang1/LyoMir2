"""Q1 selector: reproduce the sibling (cm-2) authoritative build()/covered(),
diff to the missing set, quarter 25/25/24/25, and print quarter 1 (lowest 25)
with each arm's leaf VA. Cross-check quarter 4 against cm-4's known result
(idents 4125..4651) to prove the quartering is consistent across batches.
"""
import os
import sys

CM2_TOOLS = r"D:\loym2\.claude\wt2\cm-2\tools"
sys.path.insert(0, CM2_TOOLS)

from cm2_table import build, DEFAULT_VA        # noqa: E402  native arm build()
from cm2_csharp_cover import covered, consts   # noqa: E402  C# covered()

native = build()
cs = covered()                                  # authoritative: reads cm-2 root
missing = sorted(k for k in native if k not in cs)

n = len(missing)
bounds = [round(n * i / 4) for i in range(5)]
quarters = [missing[bounds[q]:bounds[q + 1]] for q in range(4)]

print("# native arms      : %d" % len(native))
print("# C# covered       : %d" % len(cs))
print("# missing total    : %d" % n)
print("# bounds           : %s" % bounds)
for q in range(4):
    seg = quarters[q]
    print("# quarter %d: %d items  (%d..%d)" % (q + 1, len(seg), seg[0], seg[-1]))

# Consistency check: cm-4 reported it took quarter 4 = idents 4125..4651 (25).
q4 = quarters[3]
print("\n# --- consistency check vs cm-4 (should be 4125..4651, 25 items) ---")
print("# q4 first=%d last=%d count=%d  OK=%s"
      % (q4[0], q4[-1], len(q4), (q4[0] == 4125 and q4[-1] == 4651 and len(q4) == 25)))

print("\n# ===== QUARTER 1 (lowest %d) =====" % len(quarters[0]))
for k in quarters[0]:
    print("%5d  0x%06X" % (k, native[k]))
