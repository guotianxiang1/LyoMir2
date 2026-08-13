"""Robust census of every instruction whose memory displacement is +0xA80 / +0xA7C.

Pattern-scan for the disp32 bytes then re-disassemble from a few candidate starts,
keeping only decodings whose length actually covers the displacement field.
"""
import io
import os
import struct

from capstone import CS_ARCH_X86, CS_MODE_32, Cs
from capstone.x86 import X86_OP_MEM

IMAGE = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0xB00000

with open(IMAGE, "rb") as fh:
    DATA = fh.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)
MD.detail = True


def census(disp, out, label):
    pat = struct.pack("<i", disp)
    found = {}
    start = CODE_LO - BASE
    end = CODE_HI - BASE
    while True:
        i = DATA.find(pat, start, end)
        if i < 0:
            break
        start = i + 1
        # instruction must begin 2..8 bytes before the disp field
        for back in range(2, 9):
            s = i - back
            for insn in MD.disasm(DATA[s:s + 16], BASE + s):
                if insn.size != back + 4:
                    break
                ok = any(op.type == X86_OP_MEM and op.mem.disp == disp
                         for op in insn.operands)
                if ok:
                    found[insn.address] = (insn.bytes.hex().upper(),
                                           insn.mnemonic + " " + insn.op_str)
                break
    print("### disp +0x%X (%s): %d instructions" % (disp, label, len(found)),
          file=out)
    for va in sorted(found):
        by, txt = found[va]
        print("  0x%06X  %-24s %s" % (va, by, txt), file=out)
    print("", file=out)


def dump(va, limit, out):
    off = va - BASE
    print("=== 0x%06X ===" % va, file=out)
    for n, insn in enumerate(MD.disasm(DATA[off:off + limit * 8], va)):
        if n >= limit:
            break
        print("0x%06X  %-22s %s %s" % (insn.address, insn.bytes.hex().upper(),
                                       insn.mnemonic, insn.op_str), file=out)
    print("", file=out)


def main():
    buf = io.StringIO()
    census(0xA80, buf, "TPlayObject -> TGroup")
    census(0xA7C, buf, "TPlayObject -> group leader mirror")
    dump(0x6C3200, 95, buf)      # the third create/add site (0x6C3318 / 0x6C333B)
    dump(0x726E68, 70, buf)      # TGroup.DelMember
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q7_a80.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
