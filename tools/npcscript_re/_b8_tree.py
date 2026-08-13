import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE
import re

V_CLASSNAME, V_INSTSIZE, V_PARENT = -0x2C, -0x28, -0x24


def dw(va):
    o = va - BASE
    if not (0 <= o <= len(DATA) - 4):
        return 0
    return int.from_bytes(DATA[o:o + 4], "little")


def sstr(va):
    o = va - BASE
    if not (0 <= o < len(DATA)):
        return None
    n = DATA[o]
    if n == 0 or n > 64:
        return None
    try:
        return DATA[o + 1:o + 1 + n].decode("ascii")
    except Exception:
        return None


def cname(vmt):
    return sstr(dw(vmt + V_CLASSNAME))


# --- enumerate every VMT in the image: dw(X) == X + 0x4C and classname parses
ALL = {}
for m in re.finditer(rb"(?=.)", b""):
    pass
for off in range(0, len(DATA) - 4, 4):
    va = BASE + off
    if int.from_bytes(DATA[off:off + 4], "little") == va + 0x4C:
        vmt = va + 0x4C
        nm = cname(vmt)
        if nm and nm[0] == "T":
            ALL[vmt] = nm

print("total VMTs found: %d" % len(ALL))

TENV = 0x77477C
TDYN = 0x5FB264


def parent_of(vmt):
    pp = dw(vmt + V_PARENT)
    if not (0x400000 < pp < 0x1400000):
        return 0
    return dw(pp)


kids = {}
for vmt, nm in ALL.items():
    p = parent_of(vmt)
    kids.setdefault(p, []).append((vmt, nm))


def show(root, depth=0):
    for vmt, nm in sorted(kids.get(root, [])):
        print("%s%-30s VMT=0x%06X InstanceSize=%d (0x%X)" % (
            "    " * depth, nm, vmt, dw(vmt + V_INSTSIZE), dw(vmt + V_INSTSIZE)))
        show(vmt, depth + 1)


print("\n--- TEnvironment subtree ---")
print("%-30s VMT=0x%06X InstanceSize=%d (0x%X)" % (
    cname(TENV), TENV, dw(TENV + V_INSTSIZE), dw(TENV + V_INSTSIZE)))
show(TENV, 1)

# class globals referenced by the base AddObject
print("\n--- class globals used by TEnvironment.AddObject (0x779F68) ---")
for g in (0x73BBE8, 0x63CFA8):
    v = dw(g)
    print("  [0x%06X] = 0x%06X  -> %s" % (g, v, cname(v)))
