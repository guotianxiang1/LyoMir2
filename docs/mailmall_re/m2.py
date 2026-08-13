"""Shared toolkit for M2Server flat_image analysis (mail + mall agent)."""
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(IMG, 'rb').read()
END = BASE + len(DATA)

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True


def off(va):
    return va - BASE


def ok(va):
    return BASE <= va < END


def rd(va, n):
    o = va - BASE
    if o < 0 or o + n > len(DATA):
        return None
    return DATA[o:o + n]


def u8(va):
    b = rd(va, 1)
    return None if b is None else b[0]


def u16(va):
    b = rd(va, 2)
    return None if b is None else struct.unpack('<H', b)[0]


def u32(va):
    b = rd(va, 4)
    return None if b is None else struct.unpack('<I', b)[0]


def i32(va):
    b = rd(va, 4)
    return None if b is None else struct.unpack('<i', b)[0]


def dis(va, n=40, stop_ret=False):
    o = va - BASE
    out = []
    for ins in md.disasm(DATA[o:o + n * 16], va):
        out.append(ins)
        if len(out) >= n:
            break
        if stop_ret and ins.mnemonic in ('ret', 'retf'):
            break
    return out


def fmt(ins):
    return "%08X  %-26s %s %s" % (ins.address, ins.bytes.hex(' '), ins.mnemonic, ins.op_str)


def show(va, n=40, stop_ret=False, f=None):
    for ins in dis(va, n, stop_ret):
        line = fmt(ins)
        if f:
            f.write(line + "\n")
        else:
            print(line)


def find_all(pat, start=0):
    res = []
    i = start
    while True:
        i = DATA.find(pat, i)
        if i < 0:
            break
        res.append(BASE + i)
        i += 1
    return res


def xrefs_call(target):
    res = []
    for i in range(len(DATA) - 5):
        b = DATA[i]
        if b in (0xE8, 0xE9):
            rel = struct.unpack_from('<i', DATA, i + 1)[0]
            if BASE + i + 5 + rel == target:
                res.append((BASE + i, 'call' if b == 0xE8 else 'jmp'))
    return res


def imm_refs(value):
    return find_all(struct.pack('<I', value & 0xFFFFFFFF))


def dstr(va):
    n = u8(va)
    if n is None:
        return None
    return (n, rd(va + 1, n))


def pstr(va):
    n = i32(va - 4)
    if n is None or n < 0 or n > 4096:
        return None
    return rd(va, n)


def gbk(b):
    if b is None:
        return None
    try:
        return b.decode('gbk')
    except Exception:
        return repr(b)
