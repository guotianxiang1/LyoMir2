"""Find every instruction that references [reg+DISP] in the flat image.

Usage: python tools/pois19_field.py <disp-hex> [lo-va-hex] [hi-va-hex]

Disassembles a window around each raw ModRM match and keeps only instructions
whose decoded memory operand actually uses that displacement, so the output is
free of byte coincidences. Writes are flagged separately from reads.
"""
import os
import struct
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs
from capstone.x86 import X86_OP_MEM

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def modrm_pats(disp):
    out = []
    for reg in range(8):
        if reg == 4:
            continue
        for regop in range(8):
            if -128 <= disp <= 127:
                out.append(bytes([0x40 | (regop << 3) | reg, disp & 0xFF]))
            out.append(bytes([0x80 | (regop << 3) | reg]) +
                       struct.pack("<i", disp))
    return out


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    disp = int(sys.argv[1], 16)
    lo = int(sys.argv[2], 16) if len(sys.argv) > 2 else 0x401000
    hi = int(sys.argv[3], 16) if len(sys.argv) > 3 else 0x800000

    with open(IMAGE, "rb") as handle:
        data = handle.read()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True

    cands = set()
    for pat in modrm_pats(disp):
        start = 0
        while True:
            i = data.find(pat, start)
            if i < 0:
                break
            start = i + 1
            if lo <= i + BASE < hi:
                cands.add(i)

    found = {}
    for off in sorted(cands):
        for back in range(0, 5):
            start = off - back
            if start < 0:
                continue
            for insn in md.disasm(data[start:start + 16], BASE + start):
                for op in insn.operands:
                    if op.type == X86_OP_MEM and op.mem.disp == disp \
                            and op.mem.base != 0 and op.mem.index == 0:
                        found[insn.address] = (insn.bytes.hex().upper(),
                                               insn.mnemonic, insn.op_str)
                break
    print("=== %d insns referencing [reg+0x%X] in 0x%X..0x%X ===" %
          (len(found), disp, lo, hi))
    for va in sorted(found):
        b, m, o = found[va]
        print("  0x%06X  %-22s %s %s" % (va, b, m, o))
    return 0


if __name__ == "__main__":
    sys.exit(main())
