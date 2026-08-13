"""The remaining links: sub_7284E8 (clear slot), sub_7280C0 (3rd slot-setter caller),
sub_72792C (roster name probe used by the login re-attach)."""
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
    for t, nm in [(0x7284E8, "clear slot"), (0x7280C0, "3rd slot-setter host"),
                  (0x72792C, "roster name probe"), (0x726C14, "IsGroupOwner-ish")]:
        h = e8_callers(t)
        print("E8 callers of 0x%06X (%s): %d -> %s" % (
            t, nm, len(h), ", ".join("0x%06X" % x for x in h)), file=buf)
    print("", file=buf)
    dump(0x7284E8, 25, buf, "clear slot")
    dump(0x7280C0, 60, buf, "3rd slot-setter host")
    dump(0x72792C, 45, buf, "roster name probe")
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q10_slot.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
