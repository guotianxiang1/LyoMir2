"""Derive the CM dispatch map (opcode -> handler VA) out of flat_image.bin.

Concrete execution: for every opcode 0..0xFFFF, start at the dispatcher's
case head and single-step ONLY the instructions that make up a Delphi case
tree (arithmetic on EAX + conditional jumps + indexed jump tables). The first
instruction that is not part of the tree is the handler entry.
"""
import struct, json, sys
from capstone import *
from capstone.x86 import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(IMG, "rb").read()

# 0x6D805C mov eax,[ebp-0x34] / 0x6D805F movzx eax, word [eax+4]  (load Ident)
# 0x6D8063 cmp eax,0xCD6 ... -> the case tree proper
CASE_HEAD = 0x6D8063
DEFAULT = 0x6DBC2C
# 0x6DBC2C: 33 C0 5A 59 59 64 89 10 E9 D5 00 00 00  -> jmp 0x6DBD0E (return False)
DEFAULT_SIG = bytes.fromhex("33C05A5959648910E9")

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

_cache = {}


def ins_at(va):
    i = _cache.get(va)
    if i is None:
        off = va - BASE
        code = DATA[off:off + 16]
        i = next(md.disasm(code, va), None)
        _cache[va] = i
    return i


def u32(va):
    off = va - BASE
    return struct.unpack("<I", DATA[off:off + 4])[0]


TREE_JCC = {
    "je": lambda a, b: a == b,
    "jne": lambda a, b: a != b,
    "jg": lambda a, b: s32(a) > s32(b),
    "jge": lambda a, b: s32(a) >= s32(b),
    "jl": lambda a, b: s32(a) < s32(b),
    "jle": lambda a, b: s32(a) <= s32(b),
    "ja": lambda a, b: a > b,
    "jae": lambda a, b: a >= b,
    "jb": lambda a, b: a < b,
    "jbe": lambda a, b: a <= b,
}


def s32(v):
    return v - 0x100000000 if v & 0x80000000 else v


def resolve(opcode):
    """Return (handler_va, path) for one opcode."""
    eax = opcode
    va = CASE_HEAD
    cmp_a = cmp_b = 0
    path = []
    for _ in range(4000):
        i = ins_at(va)
        if i is None:
            return ("BADDECODE@%X" % va, path)
        m, ops = i.mnemonic, i.operands
        if m == "cmp" and len(ops) == 2 and ops[0].type == X86_OP_REG \
                and i.reg_name(ops[0].reg) == "eax" and ops[1].type == X86_OP_IMM:
            cmp_a, cmp_b = eax, ops[1].imm & 0xFFFFFFFF
            va += i.size
            continue
        if m in ("sub", "add") and len(ops) == 2 and ops[0].type == X86_OP_REG \
                and i.reg_name(ops[0].reg) == "eax" and ops[1].type == X86_OP_IMM:
            k = ops[1].imm & 0xFFFFFFFF
            eax = (eax - k if m == "sub" else eax + k) & 0xFFFFFFFF
            cmp_a, cmp_b = eax, 0
            va += i.size
            continue
        if m in ("dec", "inc") and len(ops) == 1 and ops[0].type == X86_OP_REG \
                and i.reg_name(ops[0].reg) == "eax":
            eax = (eax - 1 if m == "dec" else eax + 1) & 0xFFFFFFFF
            cmp_a, cmp_b = eax, 0
            va += i.size
            continue
        if m in TREE_JCC:
            tgt = ops[0].imm
            if TREE_JCC[m](cmp_a, cmp_b):
                path.append((va, m, tgt))
                va = tgt
            else:
                va += i.size
            continue
        if m == "jmp":
            o = ops[0]
            if o.type == X86_OP_IMM:
                path.append((va, "jmp", o.imm))
                va = o.imm
                continue
            if o.type == X86_OP_MEM and o.mem.index != 0 \
                    and i.reg_name(o.mem.index) == "eax" and o.mem.scale == 4 \
                    and o.mem.base == 0:
                tbl = o.mem.disp & 0xFFFFFFFF
                if eax > 0x1000:
                    return ("TBLOOR@%X[%d]" % (tbl, eax), path)
                tgt = u32(tbl + 4 * eax)
                path.append((va, "jmptbl", (tbl, eax, tgt)))
                va = tgt
                continue
            return ("INDIRECT@%X" % va, path)
        # not part of the case tree -> this is the handler entry
        return (va, path)
    return ("LOOP@%X" % va, path)


def is_default_clone(va):
    """Some arms fall through to a byte-identical copy of the 0x6DBC2C drop."""
    off = va - BASE
    return DATA[off:off + len(DEFAULT_SIG)] == DEFAULT_SIG


def main():
    real = {}
    for op in range(0x10000):
        h, _ = resolve(op)
        if isinstance(h, int) and (h == DEFAULT or is_default_clone(h)):
            continue
        real[op] = h
    print("opcodes with a non-default handler: %d" % len(real))
    with open(r"D:\loym2\.claude\wt2\cm-1\tools\cm1_re\_cm1_map.json", "w") as f:
        json.dump({str(k): (v if isinstance(v, str) else "%X" % v)
                   for k, v in sorted(real.items())}, f, indent=0)
    for op in sorted(real):
        v = real[op]
        print("%5d  0x%04X  ->  %s" % (op, op, v if isinstance(v, str) else "%08X" % v))


if __name__ == "__main__":
    main()
