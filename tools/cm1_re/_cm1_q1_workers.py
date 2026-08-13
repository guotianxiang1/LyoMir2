"""Dump each Q1 worker (and key helpers) as a CFG-followed body with string
literals and callee lists, so feasibility (model vs fail-closed) is judged from
the image. One section per address; long functions are range-bounded.
"""
import sys
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(IMG, "rb").read()

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

# unique workers behind the 25 Q1 leaves + the shared helpers they touch
TARGETS = [
    ("1054/1055 worker",       0x6D3694),
    ("throttle GetTick?",      0x408340),
    ("1054/1055 getter",       0x7481F4),
    ("1056 worker",            0x6CB9B4),
    ("1057 worker",            0x6CB9F0),
    ("1059 worker",            0x6D7794),
    ("1061 worker",            0x6CBDD4),
    ("1068 worker",            0x6D1780),
    ("1080 worker",            0x6CF49C),
    ("1084 worker",            0x6D1AB8),
    ("1090/1200 worker",       0x6BD674),
    ("1210 worker",            0x6E3974),
    ("1211 worker",            0x6E39C8),
    ("1212 worker",            0x6E3A34),
    ("1213 worker",            0x6E3A4C),
    ("1214 worker",            0x6E3A88),
    ("1213 gate a",            0x6151CC),
    ("1213 gate b",            0x6152B8),
    ("1217 worker",            0x6C53B8),
    ("1248 worker",            0x6E5384),
    ("1250 worker",            0x6E1CEC),
    ("1251 worker",            0x6E7E0C),
    ("1254 worker",            0x6F9538),
    ("1255 worker",            0x6E8350),
    ("1258 worker",            0x6E82F4),
    ("1259 worker",            0x6E8454),
    ("1260 worker",            0x6E84BC),
]


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


def walk(start, span=0x600, limit=1200):
    seen = set()
    todo = [start]
    body = {}
    lo, hi = start, start + span
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
            if m == "ret":
                break
            if m == "jmp":
                o = i.operands[0]
                if o.type == X86_OP_IMM and lo <= o.imm < hi:
                    va = o.imm
                    continue
                break
            if m.startswith("j"):
                o = i.operands[0]
                if o.type == X86_OP_IMM and lo <= o.imm < hi:
                    todo.append(o.imm)
            va += i.size
    return body


def dump(label, start, span=0x600):
    body = walk(start, span)
    print("\n" + "=" * 78)
    print("%s  @0x%06X   bytes: %s" % (label, start, rd(start, 10).hex().upper()))
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
        print(line + extra)
    print("  callees: %s" % ", ".join("0x%06X" % c for c in sorted(set(calls))))


if __name__ == "__main__":
    if len(sys.argv) > 1:
        dump("adhoc", int(sys.argv[1], 16), int(sys.argv[2], 0) if len(sys.argv) > 2 else 0x600)
    else:
        for label, va in TARGETS:
            dump(label, va)
