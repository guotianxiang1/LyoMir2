"""Linear disassembly of the M2Server flat image at a given virtual address.

Usage:  python tools/m2_disasm.py <VA-hex> [instr-count] [max-bytes]
Example: python tools/m2_disasm.py 0x772F84 40

Requires capstone. The flat image is the unpacked 16.8 MB M2Server binary with
ImageBase 0x400000, so file offset == VA - 0x400000.
"""
import os
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    va = int(sys.argv[1], 16)
    count = int(sys.argv[2]) if len(sys.argv) > 2 else 60
    maxbytes = int(sys.argv[3]) if len(sys.argv) > 3 else 500

    with open(IMAGE, "rb") as handle:
        data = handle.read()

    md = Cs(CS_ARCH_X86, CS_MODE_32)
    off = va - BASE
    print("=== disasm VA 0x%X (file off 0x%X) ===" % (va, off))
    for n, insn in enumerate(md.disasm(data[off:off + maxbytes], va)):
        if n >= count:
            break
        print("0x%06X  %-24s %s %s" % (insn.address, insn.bytes.hex().upper(),
                                       insn.mnemonic, insn.op_str))
    return 0


if __name__ == "__main__":
    sys.exit(main())
