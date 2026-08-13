import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, dstr
from _b8_region import dis2

# TEnvironment base implementations of the four overridden slots
FUNCS = [
    (0x77A014, 0x77A0E0, "TEnvironment VMT+0x00  (base of OnLeave-host 0x5FD574)"),
    (0x779F68, 0x77A014, "TEnvironment VMT+0x04  (base of OnEnter-host 0x5FD534)"),
    (0x779F64, 0x779F68, "TEnvironment VMT+0x08  (base of OnDie-host  0x5FD4D4)"),
    (0x77BB38, 0x77BBB0, "TEnvironment VMT+0x10  (base of OnReLive-host 0x5FD384) -- contains 0x77BB66"),
]
for a, b, nm in FUNCS:
    print("=" * 78)
    print("%s   0x%06X .. 0x%06X" % (nm, a, b))
    print("=" * 78)
    print(dis2(a, b))
    print()
