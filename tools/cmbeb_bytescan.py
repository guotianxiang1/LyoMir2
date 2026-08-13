"""Scan the flat image for a byte pattern (hex, '??' wildcards) and show a
small disassembly window at each hit.

usage: cmbeb_bytescan.py "80 B8 82 00 00 00 00" [before] [after] [max]
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False


def main():
    toks = sys.argv[1].split()
    before = int(sys.argv[2]) if len(sys.argv) > 2 else 24
    after = int(sys.argv[3]) if len(sys.argv) > 3 else 24
    cap = int(sys.argv[4]) if len(sys.argv) > 4 else 40
    pat = [None if t == "??" else int(t, 16) for t in toks]
    n = len(pat)
    hits = []
    for off in range(0, len(data) - n):
        ok = True
        for k in range(n):
            if pat[k] is not None and data[off + k] != pat[k]:
                ok = False
                break
        if ok:
            hits.append(BASE + off)
            if len(hits) >= cap:
                break
    sys.stdout.reconfigure(encoding="utf-8")
    print("hits=%d" % len(hits))
    for h in hits:
        print("--- %08X ---" % h)
        lo = h - before
        for i in md.disasm(data[lo - BASE:h - BASE + after], lo):
            mark = ">>" if i.address == h else "  "
            print("%s %08X  %-9s %s" % (mark, i.address, i.mnemonic, i.op_str))


main()
