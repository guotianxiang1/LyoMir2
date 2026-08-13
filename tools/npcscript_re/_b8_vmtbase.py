import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE

def dw(va):
    o = va - BASE
    return int.from_bytes(DATA[o:o + 4], "little")

def shortstr(va):
    o = va - BASE
    n = DATA[o]
    return DATA[o + 1:o + 1 + n].decode("gbk", "replace")

def find_vmt_start(slot_va, back=0x800):
    """Delphi vmtSelfPtr = -76 (-0x4C): dword at (VMT-0x4C) equals VMT."""
    hits = []
    for va in range(slot_va, slot_va - back, -4):
        if dw(va) == va + 0x4C:
            hits.append(va)
    return hits

TARGETS = [0x5F7B58, 0x5F9934, 0x5FB264, 0x77477C]
for t in TARGETS:
    print("=" * 70)
    print("slot region 0x%06X" % t)
    for sp in find_vmt_start(t):
        vmt = sp + 0x4C
        cn = dw(vmt - 0x28)
        try:
            name = shortstr(cn)
        except Exception as e:
            name = "?" + str(e)
        isz = dw(vmt - 0x24)
        par = dw(vmt - 0x20)
        parname = ""
        if 0x400000 < par < 0x1400000:
            try:
                parname = shortstr(dw(dw(par) - 0x28))
            except Exception:
                parname = "?"
        print("  SelfPtr@0x%06X -> VMT=0x%06X  ClassName=%-28s InstanceSize=0x%X  slotoff=+0x%X" % (
            sp, vmt, name, isz, t - vmt))
        print("      parentptr@0x%06X -> %s" % (par, parname))
