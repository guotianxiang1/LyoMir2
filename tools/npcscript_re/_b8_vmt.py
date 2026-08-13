import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, dstr, xref_imm
from _b8_region import dis2
import re

# the four map-class overrides that host the @On* dispatch
OVERRIDES = {
    0x5FD384: "OnReLive-host",
    0x5FD4D4: "OnDie-host",
    0x5FD534: "OnEnter-host",
    0x5FD574: "OnLeave-host",
}
# the base-class methods they chain to
BASES = {
    0x779F64: "base called by OnDie-host (before)",
    0x779F68: "base called by OnEnter-host (before)",
    0x77A014: "base called by OnLeave-host (after)",
}

print("=" * 78)
print("PART A -- dword xrefs (VMT slots) of the four overrides")
print("=" * 78)
for va, nm in OVERRIDES.items():
    xs = xref_imm(va)
    print("\n0x%06X %-16s xrefs: %s" % (va, nm, ", ".join("0x%06X" % x for x in xs)))

print()
print("=" * 78)
print("PART B -- dword xrefs of base methods")
print("=" * 78)
for va, nm in BASES.items():
    xs = xref_imm(va)
    print("\n0x%06X %-38s xrefs: %s" % (va, nm, ", ".join("0x%06X" % x for x in xs)))
