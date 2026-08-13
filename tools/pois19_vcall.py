"""Find every indirect virtual call `call dword ptr [reg+DISP]` in the flat image.

Usage: python tools/pois19_vcall.py <disp-hex> [context-insns]

Covers both disp8 and disp32 ModRM forms for all 8 base registers, plus the
SIB-encoded esp form. Each hit is printed with a few preceding instructions so
the receiver/arguments are visible.
"""
import os
import struct
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0x800000


def patterns(disp):
    """Return raw byte patterns for `call [reg+disp]` (opcode FF /2)."""
    out = []
    for reg in range(8):
        if -128 <= disp <= 127:  # disp8, mod=01
            modrm = 0x40 | (2 << 3) | reg
            if reg == 4:
                out.append(bytes([0xFF, modrm, 0x24, disp & 0xFF]))
            else:
                out.append(bytes([0xFF, modrm, disp & 0xFF]))
        # disp32, mod=10
        modrm = 0x80 | (2 << 3) | reg
        d32 = struct.pack("<i", disp)
        if reg == 4:
            out.append(bytes([0xFF, modrm, 0x24]) + d32)
        else:
            out.append(bytes([0xFF, modrm]) + d32)
    return out


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    disp = int(sys.argv[1], 16)
    ctx = int(sys.argv[2]) if len(sys.argv) > 2 else 8

    with open(IMAGE, "rb") as handle:
        data = handle.read()
    md = Cs(CS_ARCH_X86, CS_MODE_32)

    hits = []
    for pat in patterns(disp):
        start = 0
        while True:
            i = data.find(pat, start)
            if i < 0:
                break
            start = i + 1
            va = i + BASE
            if CODE_LO <= va < CODE_HI:
                hits.append((va, len(pat)))

    hits.sort()
    print("=== %d `call [reg+0x%X]` sites in code range ===" % (len(hits), disp))
    for va, size in hits:
        print("\n---- call site 0x%06X ----" % va)
        # back up far enough to catch the argument setup, then align by
        # disassembling forward until we land exactly on the call.
        for back in range(40, 4, -1):
            lines = []
            landed = False
            for insn in md.disasm(data[va - back - BASE:va - BASE + size], va - back):
                lines.append("  0x%06X  %-22s %s %s" %
                             (insn.address, insn.bytes.hex().upper(),
                              insn.mnemonic, insn.op_str))
                if insn.address == va:
                    landed = True
                    break
            if landed:
                print("\n".join(lines[-(ctx + 1):]))
                break
        else:
            print("  (could not align) 0x%06X" % va)
    return 0


if __name__ == "__main__":
    sys.exit(main())
