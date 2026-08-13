"""Scan the flat image for 4-byte little-endian references to a target VA.

Usage: python tools/mall_xref.py 0x7D5D98
Prints every file offset / VA whose 4 bytes equal the target, with a small
disassembly window before it so the instruction that references it is visible.
"""
import os
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def main():
    target = int(sys.argv[1], 16)
    needle = target.to_bytes(4, "little")
    with open(IMAGE, "rb") as fh:
        data = fh.read()

    md = Cs(CS_ARCH_X86, CS_MODE_32)
    hits = []
    start = 0
    while True:
        idx = data.find(needle, start)
        if idx < 0:
            break
        hits.append(idx)
        start = idx + 1

    print("target 0x%X : %d hits" % (target, len(hits)))
    for idx in hits:
        va = idx + BASE
        # only report those in the .text-ish range (executable code below ~0xB00000)
        region = "CODE" if idx < 0xB00000 else "DATA"
        print("---- ref @ file 0x%X  VA 0x%X  [%s] ----" % (idx, va, region))
        # disasm a window starting a bit before, aligned by scanning back up to 16 bytes
        back = max(0, idx - 16)
        for insn in md.disasm(data[back:idx + 8], back + BASE):
            if insn.address + insn.size > va:
                print("   0x%06X  %-22s %s %s" % (insn.address,
                      insn.bytes.hex().upper(), insn.mnemonic, insn.op_str))
                if insn.address > va:
                    break


if __name__ == "__main__":
    sys.exit(main())
