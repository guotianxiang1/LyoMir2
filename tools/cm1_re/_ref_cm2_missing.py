"""Diff native CM dispatch arms against the C# case coverage, then quarter the gap."""
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\cm-2\tools")
from cm2_table import build, DEFAULT_VA  # noqa: E402
from cm2_csharp_cover import covered  # noqa: E402

native = build()
cs = covered()
missing = sorted(k for k in native if k not in cs)

print("# native arms      : %d" % len(native))
print("# C# covered       : %d" % len(cs))
print("# missing (native arm, no C# case): %d" % len(missing))

n = len(missing)
bounds = [round(n * i / 4) for i in range(5)]
for q in range(4):
    seg = missing[bounds[q]:bounds[q + 1]]
    print("\n## quarter %d: %d items  (%d..%d)" % (q + 1, len(seg), seg[0], seg[-1]))
    if q == 1:
        for k in seg:
            print("%5d  0x%06X" % (k, native[k]))

# Also: opcodes the C# handles but native routes to DEFAULT (informational).
extra = sorted(k for k in cs if k not in native and 0 < k < 0x10000)
print("\n# C# cases with no native arm (native DEFAULT / other dispatchers): %d" % len(extra))
