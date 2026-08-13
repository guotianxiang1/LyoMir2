"""Locate every reference to struct disp 0x60C / 0x610 and classify width + R/W.

Find the 4-byte little-endian displacement in CODE, then disassemble a short
window at several candidate instruction starts so the covering instruction is
decoded correctly; report width and whether the memory operand is the write
destination.
"""
import io
import sys
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_MEM

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
CODE_LO, CODE_HI = 0x401000, 0x7A10D0
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

WRITE_MNEM = {"mov", "or", "and", "xor", "add", "sub", "inc", "dec"}


def decode_at(va):
    for ins in md.disasm(data[va - BASE:va - BASE + 16], va):
        return ins
    return None


for target in (0x60C, 0x610):
    needle = struct.pack("<I", target)
    print("=== disp 0x%X references ===" % target)
    p = CODE_LO - BASE
    end = CODE_HI - BASE
    seen = set()
    while True:
        i = data.find(needle, p, end)
        if i < 0:
            break
        p = i + 1
        disp_va = BASE + i
        # the disp usually sits 2..6 bytes into the instruction; try starts.
        for back in range(2, 8):
            va = disp_va - back
            ins = decode_at(va)
            if ins is None:
                continue
            if not (ins.address <= disp_va < ins.address + ins.size):
                continue
            hit = None
            for op in ins.operands:
                if op.type == X86_OP_MEM and op.mem.disp == target and op.mem.base != 0:
                    hit = op
                    break
            if hit is None:
                continue
            base_reg = ins.reg_name(hit.mem.base)
            if base_reg in ("ebp", "esp"):
                break
            width = {1: "byte", 2: "word", 4: "dword"}.get(hit.size, "?")
            is_write = ins.mnemonic in WRITE_MNEM and ins.operands[0].type == X86_OP_MEM \
                and ins.operands[0].mem.disp == target
            if ins.address in seen:
                break
            seen.add(ins.address)
            print("  %08X  %-6s %-26s width=%-5s %s base=%s" %
                  (ins.address, ins.mnemonic, ins.op_str, width,
                   "WRITE" if is_write else "read ", base_reg))
            break
    print()
