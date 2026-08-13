"""BLOCKED-B8: trace the callers of the four @On* dispatchers.

Dispatchers (each a ~10 instruction stub that tests [npc+0x595..0x598]):
    0x6468C8 @OnEnter   0x6468F8 @OnLeave   0x646928 @OnDie   0x646954 @OnReLive

Known call sites: 0x5FD56A / 0x5FD5A3 / 0x5FD50A / 0x5FD3B2 / 0x77BB66.
Goal: find the enclosing functions, then find who calls those.
"""
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, dis, dstr, rd, find_re
import re

DISPATCHERS = {
    0x6468C8: "@OnEnter",
    0x6468F8: "@OnLeave",
    0x646928: "@OnDie",
    0x646954: "@OnReLive",
}


def call_sites(target):
    """all E8 rel32 whose target == `target`."""
    out = []
    for m in re.finditer(rb"\xE8", DATA):
        off = m.start()
        if off + 5 > len(DATA):
            continue
        rel = int.from_bytes(DATA[off + 1:off + 5], "little", signed=True)
        if BASE + off + 5 + rel == target:
            out.append(BASE + off)
    return out


def func_start(va, maxback=0x1200):
    """scan back for a Delphi prologue: 55 8B EC or 53 56 57 preceded by alignment."""
    off = va - BASE
    cands = []
    for i in range(off, max(0, off - maxback), -1):
        # push ebp; mov ebp,esp
        if DATA[i:i + 3] == b"\x55\x8B\xEC":
            cands.append(BASE + i)
        # push ebx/esi/edi entry after alignment padding
    return cands


print("=" * 78)
print("PART 1 -- call sites of each dispatcher")
print("=" * 78)
for d, name in DISPATCHERS.items():
    cs = call_sites(d)
    print("\n%s  dispatcher 0x%06X   call sites: %s" % (
        name, d, ", ".join("0x%06X" % c for c in cs) or "(none)"))
    print(dis(d, 48))
