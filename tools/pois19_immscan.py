"""Scan the M2Server flat image for instructions that load a given immediate.

For every occurrence of the raw little-endian bytes of the value (2-byte and
4-byte forms) the scanner backs up 1..14 bytes and disassembles; a hit is only
reported when capstone decodes an instruction whose immediate operand equals the
value and whose encoded immediate field actually covers the matched bytes. This
avoids counting data/rel32 coincidences as immediate loads.

Usage: python tools/pois19_immscan.py <value-hex> [more values...]
"""
import os
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs
from capstone.x86 import X86_OP_IMM

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000
MAX_BACKUP = 14


def find_all(data, pat):
    out = []
    start = 0
    while True:
        i = data.find(pat, start)
        if i < 0:
            return out
        out.append(i)
        start = i + 1


def scan(data, md, value):
    raw_hits = set()
    for width in (2, 4):
        pat = value.to_bytes(width, "little")
        for off in find_all(data, pat):
            raw_hits.add((off, width))

    imm_hits = {}
    for off, width in sorted(raw_hits):
        for back in range(1, MAX_BACKUP + 1):
            start = off - back
            if start < 0:
                continue
            for insn in md.disasm(data[start:start + 16], BASE + start):
                if insn.address != BASE + start:
                    break
                # immediate field must cover the matched bytes
                imm_start = insn.size - width
                got = None
                for op in insn.operands:
                    if op.type == X86_OP_IMM and (op.imm & 0xFFFFFFFF) == value:
                        got = op
                if got is not None and imm_start == back:
                    imm_hits[insn.address] = (insn.bytes.hex().upper(),
                                              insn.mnemonic, insn.op_str)
                break
    return raw_hits, imm_hits


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    with open(IMAGE, "rb") as handle:
        data = handle.read()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True

    for arg in sys.argv[1:]:
        value = int(arg, 16)
        raw_hits, imm_hits = scan(data, md, value)
        n2 = len([h for h in raw_hits if h[1] == 2])
        n4 = len([h for h in raw_hits if h[1] == 4])
        print("=== value 0x%X (%d) : raw word hits=%d dword hits=%d ; "
              "IMMEDIATE-LOAD hits=%d ===" % (value, value, n2, n4,
                                              len(imm_hits)))
        for va in sorted(imm_hits):
            b, m, o = imm_hits[va]
            print("  0x%06X  %-20s %s %s" % (va, b, m, o))
    return 0


if __name__ == "__main__":
    sys.exit(main())
