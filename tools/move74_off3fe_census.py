"""Exhaustive census of `[reg+disp32]` accesses with a given disp in the M2 flat image.

Usage: python tools/move74_off3fe_census.py 0x3FE [context]

Strategy: scan for the little-endian disp32 bytes, then try decoding an
instruction starting at every offset in [pos-10, pos-2] and keep the ones whose
decoded length covers the displacement bytes and whose memory operand carries
exactly that displacement. Over-reports rather than under-reports, which is what
an exhaustive census needs (a linear/seeded disassembly would silently drop
VMT-only method bodies).
"""
import os
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs, x86

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def main():
    disp = int(sys.argv[1], 16) if len(sys.argv) > 1 else 0x3FE
    ctx = int(sys.argv[2]) if len(sys.argv) > 2 else 0

    with open(IMAGE, "rb") as handle:
        data = handle.read()

    needle = disp.to_bytes(4, "little")
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True

    hits = {}
    pos = data.find(needle)
    while pos != -1:
        for back in range(2, 11):
            start = pos - back
            if start < 0:
                continue
            try:
                insn = next(md.disasm(data[start:start + 16], BASE + start, 1))
            except StopIteration:
                continue
            if start + insn.size < pos + 4:
                continue
            ok = False
            for op in insn.operands:
                if op.type == x86.X86_OP_MEM and op.mem.disp == disp:
                    ok = True
            if not ok:
                continue
            hits[insn.address] = (insn.bytes.hex().upper(), insn.mnemonic,
                                  insn.op_str, insn.size)
            break
        pos = data.find(needle, pos + 1)

    print("=== disp 0x%X : %d access site(s) ===" % (disp, len(hits)))
    for va in sorted(hits):
        b, m, o, size = hits[va]
        print("0x%06X  %-22s %s %s" % (va, b, m, o))
        if ctx:
            off = va - BASE
            lo = max(0, off - ctx)
            for ins in md.disasm(data[lo:off + size + ctx], BASE + lo):
                mark = ">>" if ins.address == va else "  "
                print("      %s 0x%06X  %-20s %s %s"
                      % (mark, ins.address, ins.bytes.hex().upper(),
                         ins.mnemonic, ins.op_str))
            print("")
    return 0


if __name__ == "__main__":
    sys.exit(main())
