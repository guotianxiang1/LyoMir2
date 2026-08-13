import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE

# Delphi VMT negative fields
V_SELF, V_CLASSNAME, V_INSTSIZE, V_PARENT = -0x4C, -0x2C, -0x28, -0x24


def dw(va):
    o = va - BASE
    return int.from_bytes(DATA[o:o + 4], "little")


def sstr(va):
    o = va - BASE
    if not (0 <= o < len(DATA)):
        return None
    n = DATA[o]
    if n == 0 or n > 64:
        return None
    s = DATA[o + 1:o + 1 + n]
    try:
        return s.decode("ascii")
    except Exception:
        return None


def cname(vmt):
    p = dw(vmt + V_CLASSNAME)
    return sstr(p)


def find_vmt(slot_va, back=0x800):
    for va in range(slot_va, slot_va - back, -4):
        if dw(va) == va + 0x4C and cname(va + 0x4C):
            return va + 0x4C
    return None


def chain(vmt):
    out = []
    seen = set()
    while vmt and vmt not in seen:
        seen.add(vmt)
        out.append((vmt, cname(vmt), dw(vmt + V_INSTSIZE)))
        pp = dw(vmt + V_PARENT)
        if not (0x400000 < pp < 0x1400000):
            break
        vmt = dw(pp)
    return out


SLOTS = {0x5F7B58: None, 0x5F9934: None, 0x5FB264: None, 0x77477C: None}
for t in SLOTS:
    v = find_vmt(t)
    SLOTS[t] = v
    print("slot 0x%06X -> VMT 0x%06X  %-24s  slotoff=+0x%X" % (
        t, v or 0, cname(v) if v else "?", (t - v) if v else 0))
    for vmt, nm, isz in chain(v) if v else []:
        print("        %-26s VMT=0x%06X InstanceSize=%d (0x%X)" % (nm, vmt, isz, isz))
    print()

print("=" * 70)
print("VMT slot table for the derived map class and its parent")
print("=" * 70)
D = SLOTS[0x5FB264]
P = SLOTS[0x77477C]
for i in range(0, 0x40, 4):
    d, p = dw(D + i), dw(P + i)
    mark = "  <== OVERRIDE" if d != p else ""
    print("  +0x%02X  derived=0x%06X  parent=0x%06X%s" % (i, d, p, mark))
