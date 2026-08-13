import sys, io, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

PATH = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(PATH, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

# Delphi VMT negative offsets (bytes objects hold point at the VMT)
V_SELFPTR = -0x4C
V_CLASSNAME = -0x2C   # -44 : shortstring
V_INSTSIZE = -0x28    # -40 : dword
V_PARENT = -0x24      # -36 : double indirect pointer to parent VMT


def dw(va):
    o = va - BASE
    if not (0 <= o <= len(DATA) - 4):
        return 0
    return int.from_bytes(DATA[o:o + 4], "little")


def by(va):
    o = va - BASE
    if not (0 <= o < len(DATA)):
        return 0
    return DATA[o]


def rd(va, n):
    o = va - BASE
    return DATA[o:o + n]


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


def parent_vmt(vmt):
    pp = dw(vmt + V_PARENT)
    if not (0x400000 < pp < 0x1400000):
        return 0
    return dw(pp)


# ---- enumerate every VMT: dword[V-0x4C] == V and classname parses ----
ALL = {}          # vmt -> name
for off in range(0, len(DATA) - 4, 4):
    va = BASE + off
    if int.from_bytes(DATA[off:off + 4], "little") == va + 0x4C:
        vmt = va + 0x4C
        nm = cname(vmt)
        if nm and nm[0] == "T":
            ALL[vmt] = nm

NAME2VMT = {}
for v, n in ALL.items():
    NAME2VMT.setdefault(n, v)

kids = {}
for vmt in ALL:
    p = parent_vmt(vmt)
    kids.setdefault(p, []).append(vmt)


def subtree(root_vmt):
    out = []
    def walk(v, depth):
        out.append((v, depth))
        for c in sorted(kids.get(v, [])):
            walk(c, depth + 1)
    walk(root_vmt, 0)
    return out


def slot_count(vmt):
    """number of positive vtable slots before next VMT/negative struct.
    Delphi: slots start at vmt+0, we scan dwords that look like code ptrs."""
    n = 0
    va = vmt
    while True:
        d = dw(va)
        if 0x401000 <= d < 0x7B0000:   # CODE range
            n += 4
            va += 4
        else:
            break
        if n > 0x400:
            break
    return n


def overridden_slots(vmt, pvmt, maxslot=0x400):
    """slots where child VMT differs from parent VMT (positive slots only)."""
    diffs = []
    for off in range(0, maxslot, 4):
        cv = dw(vmt + off)
        pv = dw(pvmt + off)
        if not (0x401000 <= cv < 0x7B0000):
            break
        if cv != pv:
            diffs.append((off, cv, pv))
    return diffs


# ---- factory sub_679F8C jump-table decode ----
FAC_BYTETAB = 0x67A026
FAC_JMPTAB = 0x67A115
FAC_BIAS = 0xB
FAC_MAX = 0xEE
FAC_DEFAULT = 0x67AE5E


def case_target(race):
    idx = race - FAC_BIAS
    if idx < 0 or idx > FAC_MAX:
        return FAC_DEFAULT, None
    slot = by(FAC_BYTETAB + idx)
    tgt = dw(FAC_JMPTAB + slot * 4)
    return tgt, slot


def decode_case(va, maxb=0x40):
    """scan a case body for classref-global (mov eax,[imm32]) + ctor (call rel32)."""
    classref = None
    vmt = None
    ctor = None
    body = []
    for ins in md.disasm(rd(va, maxb), va):
        body.append("%08X %-20s %s %s" % (ins.address, ins.bytes.hex(), ins.mnemonic, ins.op_str))
        if ins.mnemonic == "mov" and ins.op_str.startswith("eax, dword ptr [0x") and classref is None:
            g = int(ins.op_str.split("[")[1].rstrip("]"), 16)
            classref = g
            vmt = dw(g)
        if ins.mnemonic == "call" and ctor is None and classref is not None:
            try:
                ctor = int(ins.op_str, 16)
            except Exception:
                pass
        if ins.mnemonic == "jmp" and ins.op_str.startswith("0x"):
            break
        if ins.address - va > maxb - 8:
            break
    return classref, vmt, ctor, body


# slot -> label (verified anchors from prior byte work; ambiguous ones marked '?')
SLOT_LABEL = {
    0x00C: "fld00C", 0x010: "fld010", 0x014: "fld014",
    0x018: "Operate", 0x020: "TargetInView?", 0x024: "fld024",
    0x030: "WalkTo", 0x040: "CanAct",
    0x064: "fld064", 0x068: "fld068",
    0x078: "CoolEyeRoll", 0x080: "DelTarget", 0x084: "Die",
    0x088: "Run", 0x08C: "CopyAbility", 0x090: "Wondering",
    0x0C8: "RecalcAbilitys", 0x0F0: "fld0F0",
    0x194: "fld194", 0x198: "IsProperTargetA", 0x19C: "IsProperTarget",
    0x1A0: "IsFriend?", 0x1A4: "IsProperTargetB?", 0x1A8: "fld1A8",
    0x1B4: "Initialize", 0x1B8: "ComeOut", 0x1C0: "SpaceMove",
    0x1D4: "fld1D4", 0x1E8: "ApplyAbil", 0x1EC: "fld1EC",
    0x1FC: "ScatterDrops", 0x200: "AttackTarget", 0x204: "Attack",
    0x208: "Struck/DieHook", 0x20C: "Think", 0x210: "s210", 0x214: "s214",
    0x218: "s218", 0x21C: "s21C", 0x220: "s220", 0x224: "s224",
    0x228: "s228", 0x22C: "s22C", 0x230: "s230", 0x234: "s234",
    0x238: "s238", 0x23C: "s23C",
}


def lbl(off):
    return SLOT_LABEL.get(off, "+0x%X" % off)


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "tree"
    if cmd == "tree":
        root = sys.argv[2] if len(sys.argv) > 2 else "TAnimal"
        rv = NAME2VMT.get(root)
        print("total VMTs: %d ; root %s VMT=0x%06X" % (len(ALL), root, rv or 0))
        st = subtree(rv)
        print("subtree size: %d" % len(st))
        for v, d in st:
            print("%s%-28s VMT=0x%06X size=%d(0x%X) parent=%s" % (
                "  " * d, ALL[v], v, dw(v + V_INSTSIZE), dw(v + V_INSTSIZE),
                cname(parent_vmt(v)) or "-"))
    elif cmd == "slots":
        name = sys.argv[2]
        vmt = NAME2VMT[name]
        pvmt = parent_vmt(vmt)
        print("%s VMT=0x%06X parent=%s(0x%06X) size=0x%X" % (
            name, vmt, cname(pvmt), pvmt, dw(vmt + V_INSTSIZE)))
        for off, cv, pv in overridden_slots(vmt, pvmt):
            print("  +0x%03X  %06X  (parent %06X)" % (off, cv, pv))
    elif cmd == "byname":
        name = sys.argv[2]
        for v, n in sorted(ALL.items()):
            if name.lower() in n.lower():
                print("%-30s VMT=0x%06X size=0x%X parent=%s" % (
                    n, v, dw(v + V_INSTSIZE), cname(parent_vmt(v)) or "-"))
    elif cmd == "factory":
        lo = int(sys.argv[2]) if len(sys.argv) > 2 else 0
        hi = int(sys.argv[3]) if len(sys.argv) > 3 else 255
        print("race -> class (factory sub_679F8C jump table)")
        for race in range(lo, hi + 1):
            tgt, slot = case_target(race)
            if tgt == FAC_DEFAULT:
                continue
            cr, vmt, ctor, body = decode_case(tgt)
            nm = cname(vmt) if vmt else "?"
            print("race %3d  slot=%-3s case=0x%06X  class=%-24s vmt=0x%06X ctor=0x%06X classref=0x%06X" % (
                race, slot, tgt, nm, vmt or 0, ctor or 0, cr or 0))
    elif cmd == "dumpslots":
        name = sys.argv[2]
        lo = int(sys.argv[3], 16) if len(sys.argv) > 3 else 0
        hi = int(sys.argv[4], 16) if len(sys.argv) > 4 else 0x220
        vmt = NAME2VMT[name]
        print("%s VMT=0x%06X" % (name, vmt))
        for off in range(lo, hi, 4):
            print("  +0x%03X = 0x%06X" % (off, dw(vmt + off)))
    elif cmd == "classify":
        # for every missing class, classify each override: forwarder / const / body
        cs_races = {11,12,20,51,52,53,55,80,81,82,83,84,85,86,87,88,89,90,91,92,
                    93,94,95,96,97,99,100,101,102,103,104,105,106,107,108,110,111,
                    112,113,114,115,116,117,118,119,120,130,131,132,133,134,150,
                    200,201,203,206,208,209,210,214,215,247}
        native = {}
        for race in range(0, 256):
            tgt, slot = case_target(race)
            if tgt == FAC_DEFAULT:
                continue
            cr, vmt, ctor, body = decode_case(tgt)
            if vmt:
                native[race] = (cname(vmt), vmt, ctor)
        def classify_fn(fn, parentfn):
            insns = list(md.disasm(rd(fn, 48), fn))
            # skip prologue push ebp/mov ebp,esp
            core = [i for i in insns if not (i.mnemonic in ("push","pop") and i.op_str in ("ebp","ebx","esi","edi")) and not (i.mnemonic=="mov" and i.op_str in ("ebp, esp","esp, ebp"))]
            # const return?
            if core and core[0].mnemonic in ("xor","mov") and "eax" in core[0].op_str and core[0].address<=fn+6:
                nxt = core[1] if len(core)>1 else None
                if nxt and nxt.mnemonic=="ret":
                    return "CONST(%s)" % core[0].op_str
            # forwarder to parent slot?
            calls = [i for i in insns if i.mnemonic=="call" and i.op_str.startswith("0x")]
            if calls:
                tgts = [int(c.op_str,16) for c in calls]
                if parentfn in tgts and len(tgts)<=1:
                    return "FWD->parent"
                if len(tgts)==1:
                    return "FWD->0x%06X" % tgts[0]
            # count real instructions
            return "BODY(%d)" % len([i for i in insns if i.address-fn<48])
        for r in sorted(k for k in native if k not in cs_races):
            nm, vmt, ctor = native[r]
            pvmt = parent_vmt(vmt)
            ov = overridden_slots(vmt, pvmt)
            parts = []
            for off, cv, pv in ov:
                parts.append("%s=%s" % (lbl(off), classify_fn(cv, pv)))
            ctorc = "ctorSHARED" if ctor in (dw(pvmt+0)-0 for _ in [0]) else ""
            print("race %3d %-22s < %-16s  %s" % (r, nm, cname(pvmt) or "-", "  ".join(parts) if parts else "(no overrides)"))
    elif cmd == "triage":
        # C# races currently wired (from AddBaseObject switch)
        cs_races = {11,12,20,51,52,53,55,80,81,82,83,84,85,86,87,88,89,90,91,92,
                    93,94,95,96,97,99,100,101,102,103,104,105,106,107,108,110,111,
                    112,113,114,115,116,117,118,119,120,130,131,132,133,134,150,
                    200,201,203,206,208,209,210,214,215,247}
        # native race -> class from factory
        native = {}
        for race in range(0, 256):
            tgt, slot = case_target(race)
            if tgt == FAC_DEFAULT:
                continue
            cr, vmt, ctor, body = decode_case(tgt)
            if vmt:
                native[race] = (cname(vmt), vmt, ctor)
        missing = [r for r in native if r not in cs_races]
        print("=== MISSING native races (no C# case): %d ===" % len(missing))
        for r in sorted(missing):
            nm, vmt, ctor = native[r]
            pvmt = parent_vmt(vmt)
            ov = overridden_slots(vmt, pvmt)
            ovs = " ".join(lbl(o) for o, _, _ in ov)
            print("race %3d  %-22s < %-18s ctor=0x%06X  overrides[%d]: %s" % (
                r, nm, cname(pvmt) or "-", ctor, len(ov), ovs))
    elif cmd == "pure":
        # For each missing class: classify ctor (parent-Create target + any EXTRA field
        # writes after it) and each override (fwd/const/body).  Lets us spot classes that
        # are behaviourally identical to an existing C# parent (pure rename).
        cs_races = {11,12,20,51,52,53,55,80,81,82,83,84,85,86,87,88,89,90,91,92,
                    93,94,95,96,97,99,100,101,102,103,104,105,106,107,108,110,111,
                    112,113,114,115,116,117,118,119,120,130,131,132,133,134,150,
                    200,201,203,206,208,209,210,214,215,247}
        native = {}
        for race in range(0, 256):
            tgt, slot = case_target(race)
            if tgt == FAC_DEFAULT:
                continue
            cr, vmt, ctor, body = decode_case(tgt)
            if vmt:
                native[race] = (cname(vmt), vmt, ctor)

        def ctor_profile(ctor):
            """return (parent_create_ea, [(off,imm_or_?)...] writes after parent call)."""
            insns = list(md.disasm(rd(ctor, 0x120), ctor))
            parent = None
            writes = []
            seen_parent = False
            for ins in insns:
                if ins.mnemonic == "call" and ins.op_str.startswith("0x"):
                    tgt = int(ins.op_str, 16)
                    if parent is None:
                        parent = tgt
                        seen_parent = True
                        continue
                    # a later call (e.g. GetTickCount) -> note as write source
                    if seen_parent:
                        writes.append((None, "call 0x%06X" % tgt))
                    continue
                if ins.mnemonic in ("mov", "and", "or") and seen_parent:
                    op = ins.op_str
                    # match "... ptr [reg + 0xNN], imm"  (a field write); `this` is esi/edi
                    m = re.search(r"\[e[a-z]{2} \+ (0x[0-9a-f]+)\], (0x[0-9a-f]+|-?\d+)$", op)
                    if m:
                        writes.append((int(m.group(1), 16), m.group(2)))
                        continue
                    m2 = re.search(r"\[e[a-z]{2} \+ (0x[0-9a-f]+)\], e", op)
                    if m2:
                        writes.append((int(m2.group(1), 16), "reg"))
                        continue
                if ins.mnemonic == "ret":
                    break
            return parent, writes

        def classify_fn(fn, parentfn):
            insns = list(md.disasm(rd(fn, 64), fn))
            calls = [i for i in insns if i.mnemonic == "call" and i.op_str.startswith("0x")]
            tgts = [int(c.op_str, 16) for c in calls]
            body = [i for i in insns if not (i.mnemonic in ("push","pop") and i.op_str in ("ebp","ebx","esi","edi")) and not (i.mnemonic=="mov" and i.op_str in ("ebp, esp","esp, ebp"))]
            if body and body[0].mnemonic in ("xor","mov") and "eax" in body[0].op_str:
                nxt = body[1] if len(body) > 1 else None
                if nxt and nxt.mnemonic == "ret":
                    return "CONST"
            # empty forwarder: exactly 1 call == parent slot, nothing else meaningful
            meaningful = [i for i in body if i.mnemonic not in ("ret","nop","lea") and not (i.mnemonic=="mov" and i.op_str.startswith("eax, e"))]
            if tgts == [parentfn] and len([i for i in body if i.mnemonic not in ("ret","nop")]) <= 2:
                return "FWD->parent(EMPTY)"
            if len(tgts) == 1 and tgts[0] == parentfn:
                return "FWD->parent+extra"
            if len(tgts) == 1:
                return "FWD->0x%06X" % tgts[0]
            return "BODY(calls=%d)" % len(tgts)

        for r in sorted(k for k in native if k not in cs_races):
            nm, vmt, ctor = native[r]
            pvmt = parent_vmt(vmt)
            pctor, writes = ctor_profile(ctor)
            ov = overridden_slots(vmt, pvmt)
            ovparts = []
            for off, cv, pv in ov:
                ovparts.append("%s=%s" % (lbl(off), classify_fn(cv, pv)))
            wtxt = " ".join("[+0x%X]=%s" % (o, v) if o is not None else v for o, v in writes)
            print("race %3d %-22s < %-16s ctor->0x%06X writes:{%s}  ov: %s" % (
                r, nm, cname(pvmt) or "-", pctor or 0, wtxt, "  ".join(ovparts) or "(none)"))
    elif cmd == "case":
        race = int(sys.argv[2])
        tgt, slot = case_target(race)
        cr, vmt, ctor, body = decode_case(tgt, 0x80)
        print("race %d slot=%s case=0x%06X class=%s vmt=0x%06X ctor=0x%06X" % (
            race, slot, tgt, cname(vmt) if vmt else "?", vmt or 0, ctor or 0))
        print("\n".join(body))
