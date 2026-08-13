"""Does anything on the logout / ghost-free path touch the group slots?

sub_728518 is the slot setter (writes [slot+0x10] = player). Enumerate its callers,
plus the login re-attach helper sub_7282C8, plus the tail of TGroup.DelMember.
"""
import io
import os
import struct

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0xB00000

with open(IMAGE, "rb") as fh:
    DATA = fh.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)


def dump(va, limit, out, label=""):
    off = va - BASE
    print("=== 0x%06X %s ===" % (va, label), file=out)
    for n, insn in enumerate(MD.disasm(DATA[off:off + limit * 8], va)):
        if n >= limit:
            break
        print("0x%06X  %-22s %s %s" % (insn.address, insn.bytes.hex().upper(),
                                       insn.mnemonic, insn.op_str), file=out)
    print("", file=out)


def e8_callers(target):
    hits = []
    for va in range(CODE_LO, CODE_HI):
        off = va - BASE
        if DATA[off] != 0xE8:
            continue
        rel = struct.unpack("<i", DATA[off + 1:off + 5])[0]
        if va + 5 + rel == target:
            hits.append(va)
    return hits


def main():
    buf = io.StringIO()
    for t, nm in [(0x728518, "slot := player"), (0x7282C8, "login group re-attach"),
                  (0x72843C, "slot -> name"), (0x7680C0, "MarkDelete area?")]:
        h = e8_callers(t)
        print("E8 callers of 0x%06X (%s): %d -> %s" % (
            t, nm, len(h), ", ".join("0x%06X" % x for x in h)), file=buf)
    print("", file=buf)
    dump(0x7282C8, 60, buf, "login group re-attach")
    dump(0x726F30, 90, buf, "TGroup.DelMember tail")
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q9_ghost.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
