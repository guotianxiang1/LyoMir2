"""Scan CODE for every instruction touching a given struct offset.

Usage: cm_b_xref.py <hexOffset> [more offsets...]
Finds any modrm disp32/disp8 form referencing +off on a register, by
disassembling every byte position and keeping instructions whose op_str
contains "+ 0x<off>]".
"""
import re
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0x7A10D0
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

offs = [int(a, 16) for a in sys.argv[1:]]
pats = {o: re.compile(r"\+ 0x%x\]" % o) for o in offs}

# encode the disp bytes to prefilter positions
hits = {o: [] for o in offs}
for o in offs:
    if o <= 0x7F:
        needles = [bytes([o])]
    else:
        needles = [o.to_bytes(4, "little")]
    for needle in needles:
        start = 0
        while True:
            i = data.find(needle, start)
            if i < 0:
                break
            start = i + 1
            va = i + BASE
            if not (CODE_LO <= va <= CODE_HI):
                continue
            # try decode starting a few bytes before the disp
            for back in range(2, 9):
                s = va - back
                got = None
                for ins in md.disasm(data[s - BASE:s - BASE + 16], s):
                    got = ins
                    break
                if got is None:
                    continue
                if got.address + got.size > va and pats[o].search(got.op_str):
                    rec = "%08X  %-20s %s %s" % (
                        got.address, got.bytes.hex().upper(), got.mnemonic, got.op_str)
                    if rec not in hits[o]:
                        hits[o].append(rec)

sys.stdout.reconfigure(encoding="utf-8")
for o in offs:
    print("==== +0x%X : %d instruction(s) ====" % (o, len(hits[o])))
    for h in sorted(set(hits[o])):
        print(h)
    print()
