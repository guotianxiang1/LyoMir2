"""Resolve the Delphi class name behind each manager global used by the Q1
workers. A global `var: TSomeClass` is filled at init by
`mov eax,<VMT const>; call <ctor>; mov [global],eax`. We find the store to the
global, walk back to the `mov eax,imm32` that loaded the VMT, and read the
Delphi ShortString class name at VMT+vmtClassName (Delphi's classic -0x38).
"""
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_OP_REG, X86_REG_EAX

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

GLOBALS = {
    0x7D5D98: "1054-1057 mgr",
    0x7D6ABC: "1254/1255/1258 consign mgr",
    0x7D7190: "1210-1214 mgr",
    0x7D62DC: "1090/1200/1217 mgr",
    0x7D5D6C: "1080/1090 mgr",
    0x7D5F20: "1061 mgr",
    0x7D6630: "1061/1080 mgr",
    0x7D5D20: "1250 task-board obj",
    0x7D6014: "cm-4 4125 tbl",
}
VMT_CLASSNAME_OFFS = [-0x38, -0x4C, 0x2C]  # try Delphi variants


def rd(va, n):
    off = va - BASE
    if off < 0 or off + n > len(DATA):
        return None
    return DATA[off:off + n]


def u32(va):
    b = rd(va, 4)
    return struct.unpack("<I", b)[0] if b else None


def shortstr(va):
    b = rd(va, 1)
    if not b:
        return None
    ln = b[0]
    if ln == 0 or ln > 63:
        return None
    s = rd(va + 1, ln)
    if not s:
        return None
    try:
        t = s.decode("ascii")
    except Exception:
        return None
    if all(32 <= ord(c) < 127 for c in t):
        return t
    return None


def classname_from_vmt(vmt):
    for off in VMT_CLASSNAME_OFFS:
        ptr = u32(vmt + off)
        if ptr is None:
            continue
        nm = shortstr(ptr)
        if nm:
            return "%s (vmt=0x%06X, +%d)" % (nm, vmt, off)
    return None


def find_stores(gva):
    """Find all instruction addresses that store EAX/reg to [gva]."""
    pat1 = b"\xA3" + struct.pack("<I", gva)          # mov [gva], eax
    hits = []
    start = 0
    while True:
        idx = DATA.find(pat1, start)
        if idx < 0:
            break
        hits.append(BASE + idx)
        start = idx + 1
    return hits


def resolve(gva):
    for store in find_stores(gva):
        # disassemble a window before the store, find last `mov eax, imm32`
        win = 0x60
        code = rd(store - win, win + 8)
        if code is None:
            continue
        last_vmt = None
        for i in md.disasm(code, store - win):
            if i.address > store:
                break
            if i.mnemonic == "mov" and len(i.operands) == 2:
                d, s = i.operands
                if d.type == X86_OP_REG and d.reg == X86_REG_EAX and s.type == X86_OP_IMM:
                    v = s.imm & 0xFFFFFFFF
                    if BASE <= v < BASE + len(DATA):
                        last_vmt = v
        if last_vmt:
            nm = classname_from_vmt(last_vmt)
            if nm:
                return "store@0x%06X vmt=0x%06X -> %s" % (store, last_vmt, nm)
    return None


for gva, label in GLOBALS.items():
    r = resolve(gva)
    print("0x%06X  %-30s %s" % (gva, label, r or "<unresolved>"))
