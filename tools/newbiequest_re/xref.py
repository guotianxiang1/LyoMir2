"""Scan the flat image for absolute references to a set of VAs (little-endian
dword) and disassemble a small window around each hit so we can see the
instruction that touches the global.

Usage: python tools/newbiequest_re/xref.py 0x7D5C60 [0x......] ...
"""
import os
import struct
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def main():
    targets = [int(a, 16) for a in sys.argv[1:]]
    if not targets:
        print(__doc__)
        return 1
    with open(IMAGE, "rb") as fh:
        data = fh.read()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    end = BASE + len(data)
    for tgt in targets:
        needle = struct.pack("<I", tgt)
        print("=== refs to 0x%X ===" % tgt)
        start = 0
        hits = 0
        while True:
            i = data.find(needle, start)
            if i < 0:
                break
            start = i + 1
            va = BASE + i
            # Only consider hits that land in the code section range and look
            # like an operand (preceded by a plausible opcode). Disassemble a
            # window ending near the hit.
            back = 16
            off = i - back
            if off < 0:
                off = 0
            window = data[off:i + 4]
            best = None
            for n, insn in enumerate(md.disasm(window, BASE + off)):
                if insn.address <= va < insn.address + insn.size:
                    best = insn
                if insn.address > va:
                    break
            if best is not None and (va + 4) <= (best.address + best.size):
                hits += 1
                print("  0x%06X  %-22s %s %s" % (best.address,
                                                 best.bytes.hex().upper(),
                                                 best.mnemonic, best.op_str))
        if hits == 0:
            print("  (no instruction-aligned references found)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
