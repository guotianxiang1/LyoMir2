"""CM batch-3: reproduce the sibling-authoritative missing set, print quarter 3.

Consistency contract: the missing set and its quartering MUST match cm-2's
tooling exactly.  We therefore import cm-2's own build()/covered()/DEFAULT_VA
(the authoritative "口径") rather than re-deriving anything here, then slice the
same sorted list with the same bounds = [round(n*i/4) for i in range(5)].

Quarter 3 (0-indexed q==2) is this worker's slice: ascending items 51..74.
Run with the hermes venv python (global python/py are broken shells):
  & "C:\\Users\\Administrator\\AppData\\Local\\hermes\\hermes-agent\\venv\\Scripts\\python.exe" tools\\cm3_missing.py
"""
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\cm-2\tools")
from cm2_table import build, DEFAULT_VA  # noqa: E402
from cm2_csharp_cover import covered  # noqa: E402

native = build()
cs = covered()
missing = sorted(k for k in native if k not in cs)

n = len(missing)
bounds = [round(n * i / 4) for i in range(5)]

print("# native arms      : %d" % len(native))
print("# C# covered       : %d" % len(cs))
print("# missing total    : %d" % n)
print("# DEFAULT_VA       : 0x%06X" % DEFAULT_VA)
print("# bounds           : %s" % bounds)

for q in range(4):
    seg = missing[bounds[q]:bounds[q + 1]]
    tag = "  <== MINE (Q3)" if q == 2 else ""
    print("\n## quarter %d: %d items  (%d..%d)%s" % (q + 1, len(seg), seg[0], seg[-1], tag))
    if q == 2:
        for k in seg:
            print("%5d  0x%06X" % (k, native[k]))
