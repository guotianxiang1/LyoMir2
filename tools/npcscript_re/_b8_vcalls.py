"""Find Delphi virtual-dispatch sites for TEnvironment VMT slots +0x00/+0x04/+0x08/+0x10.

Delphi emits:  mov <r>, [self]      (8B /r, mod=00)
               call dword ptr [<r> + disp8]      (FF /2, mod=01)
For disp 0 the encoding collapses to  FF /2 mod=00  ->  FF 10..13.
"""
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE
from _b8_region import dis2
import re

SLOTS = {0x00: "OnLeave-host / DeleteObject",
         0x04: "OnEnter-host / AddObject",
         0x08: "OnDie-host",
         0x10: "OnReLive-host"}

# regs eax,ecx,edx,ebx,esp?,ebp?,esi,edi -> modrm rm field
REGN = {0: "eax", 1: "ecx", 2: "edx", 3: "ebx", 6: "esi", 7: "edi"}

hits = {s: [] for s in SLOTS}

for off in range(len(DATA) - 4):
    if DATA[off] != 0xFF:
        continue
    modrm = DATA[off + 1]
    reg = (modrm >> 3) & 7
    if reg != 2:            # /2 == CALL r/m32
        continue
    mod = modrm >> 6
    rm = modrm & 7
    if rm == 4 or rm == 5:  # SIB / disp32-abs
        continue
    if mod == 0:
        disp, ln = 0, 2
    elif mod == 1:
        disp, ln = DATA[off + 2], 3
    else:
        continue
    if disp not in SLOTS:
        continue
    va = BASE + off
    # require a VMT load of the same register within the preceding 16 bytes:
    #   8B <mod=00 reg=rm rm=base>   e.g. 8B 10 = mov edx,[eax]
    win = DATA[off - 16:off]
    ok = False
    for i in range(len(win) - 1):
        if win[i] == 0x8B and (win[i + 1] >> 6) == 0 and ((win[i + 1] >> 3) & 7) == rm \
                and (win[i + 1] & 7) not in (4, 5):
            ok = True
    if not ok:
        continue
    hits[disp].append((va, REGN.get(rm, "r%d" % rm)))

for s, nm in SLOTS.items():
    print("=" * 78)
    print("VMT+0x%02X  %-32s  %d indirect call sites" % (s, nm, len(hits[s])))
    print("=" * 78)
    for va, r in hits[s]:
        print("\n---- 0x%06X  call [%s+0x%02X]" % (va, r, s))
        print(dis2(va - 0x18, va + 6))
    print()
