"""Per-handler evidence dump: disassembly + Delphi string literals + callee list."""
import sys, struct, subprocess, re
from capstone import *
from capstone.x86 import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(IMG, "rb").read()
EXIT_VA = 0x6DBC2C

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True


def rd(va, n):
    off = va - BASE
    if off < 0 or off + n > len(DATA):
        return None
    return DATA[off:off + n]


def dstr(va):
    """Delphi long string: [va-8]=refcount, [va-4]=len, then chars."""
    h = rd(va - 8, 8)
    if not h:
        return None
    ref, ln = struct.unpack("<iI", h)
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
    """Linear sweep of a handler body up to its jump to the shared epilogue."""
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
                    if t == EXIT_VA or t < start or t > start + 0x1200:
                        break
                    va = t
                    continue
                break
            if m == "ret":
                break
            if m.startswith("j"):
                o = i.operands[0]
                if o.type == X86_OP_IMM and start <= o.imm <= start + 0x1200:
                    todo.append(o.imm)
            va += i.size
    return body


def dump(start, limit=400):
    body = walk(start, limit)
    calls, strings = [], {}
    for va in sorted(body):
        i = body[va]
        line = "%08X  %-22s %s %s" % (va, i.bytes.hex().upper(), i.mnemonic, i.op_str)
        extra = ""
        if i.mnemonic == "call" and i.operands[0].type == X86_OP_IMM:
            calls.append(i.operands[0].imm)
        for o in i.operands:
            if o.type == X86_OP_IMM and 0x400000 < o.imm < BASE + len(DATA):
                s = dstr(o.imm)
                if s:
                    strings[o.imm] = s
                    extra = "   ; '%s'" % s
            if o.type == X86_OP_MEM and o.mem.base == 0 and o.mem.index == 0:
                d = o.mem.disp & 0xFFFFFFFF
                if 0x400000 < d < BASE + len(DATA):
                    s = dstr(d)
                    if s:
                        strings[d] = s
                        extra = "   ; '%s'" % s
        print(line + extra)
    print("\n--- direct callees ---")
    for c in sorted(set(calls)):
        print("  0x%08X" % c)
    if strings:
        print("\n--- strings ---")
        for a, s in sorted(strings.items()):
            print("  0x%08X  '%s'" % (a, s))


if __name__ == "__main__":
    dump(int(sys.argv[1], 16), int(sys.argv[2]) if len(sys.argv) > 2 else 400)
