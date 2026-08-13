"""Dump every leaf of the Q1 opcode set with byte-level evidence + callee list,
so each ident's disposition can be judged from the image, not from old notes.
"""
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(IMG, "rb").read()
EXIT_VA = 0x6DBC2C

# Q1 opcode -> leaf VA (from _cm1_map.json, cross-checked by _cm1_quarter.py)
Q1 = [
    (1054, 0x6D942F), (1055, 0x6D9492), (1056, 0x6D953A), (1057, 0x6D9547),
    (1059, 0x6D9554), (1061, 0x6D9579), (1068, 0x6D959B), (1080, 0x6D95D6),
    (1084, 0x6D95C9), (1090, 0x6D9732), (1200, 0x6DA21F), (1210, 0x6DA418),
    (1211, 0x6DA45D), (1212, 0x6DA49B), (1213, 0x6DA4BF), (1214, 0x6DA529),
    (1217, 0x6DA372), (1248, 0x6DA58E), (1250, 0x6DA5A1), (1251, 0x6DA66A),
    (1254, 0x6DA69F), (1255, 0x6DA6B1), (1258, 0x6DA6C3), (1259, 0x6DA6EF),
    (1260, 0x6DA6FC),
]

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True


def rd(va, n):
    off = va - BASE
    if off < 0 or off + n > len(DATA):
        return None
    return DATA[off:off + n]


def dstr(va):
    h = rd(va - 8, 8)
    if not h:
        return None
    _ref, ln = struct.unpack("<iI", h)
    if ln == 0 or ln > 300:
        return None
    b = rd(va, ln)
    if b is None:
        return None
    try:
        s = b.decode("gbk")
    except Exception:
        try:
            s = b.decode("latin1")
        except Exception:
            return None
    if any(ord(c) < 9 for c in s):
        return None
    return s


def walk(start, limit=400):
    seen = set()
    todo = [start]
    body = {}
    while todo:
        va = todo.pop(0)
        if va in seen:
            continue
        n = 0
        while n < limit:
            if va in body:
                break
            code = rd(va, 16)
            if code is None:
                break
            i = next(md.disasm(code, va), None)
            if i is None:
                break
            body[va] = i
            seen.add(va)
            n += 1
            m = i.mnemonic
            if m == "jmp":
                o = i.operands[0]
                if o.type == X86_OP_IMM:
                    t = o.imm
                    if t == EXIT_VA or t < start or t > start + 0x1400:
                        break
                    va = t
                    continue
                break
            if m == "ret":
                break
            if m.startswith("j"):
                o = i.operands[0]
                if o.type == X86_OP_IMM and start <= o.imm <= start + 0x1400:
                    todo.append(o.imm)
            va += i.size
    return body


def dump(ident, start):
    body = walk(start)
    print("\n" + "=" * 78)
    print("CM %d  leaf 0x%06X   (bytes: %s)" % (ident, start, rd(start, 8).hex().upper()))
    print("-" * 78)
    calls = []
    for va in sorted(body):
        i = body[va]
        line = "%08X  %-20s %s %s" % (va, i.bytes.hex().upper(), i.mnemonic, i.op_str)
        extra = ""
        if i.mnemonic == "call" and i.operands[0].type == X86_OP_IMM:
            calls.append(i.operands[0].imm)
        for o in i.operands:
            if o.type == X86_OP_IMM and BASE < o.imm < BASE + len(DATA):
                s = dstr(o.imm)
                if s:
                    extra = "   ; '%s'" % s
            if o.type == X86_OP_MEM and o.mem.base == 0 and o.mem.index == 0:
                d = o.mem.disp & 0xFFFFFFFF
                if BASE < d < BASE + len(DATA):
                    s = dstr(d)
                    if s:
                        extra = "   ; '%s'" % s
        # flag the shared-exit jump explicitly
        if i.mnemonic == "jmp" and i.operands[0].type == X86_OP_IMM and i.operands[0].imm == EXIT_VA:
            extra += "   ; -> shared exit 0x6DBC2C"
        print(line + extra)
    print("  direct callees: %s" % ", ".join("0x%06X" % c for c in sorted(set(calls))))


if __name__ == "__main__":
    for ident, va in Q1:
        dump(ident, va)
