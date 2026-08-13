"""Who zeroes / rewrites [player+0xA80], and is any of it on the logout path?"""
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
    for t, nm in [(0x6C3200, "clear-own-group"), (0x6C3838, "join-existing"),
                  (0x727A74, "count-on-map"), (0x727068, "group broadcast"),
                  (0x6B7BAC, "IsGroupOwner")]:
        h = e8_callers(t)
        print("E8 callers of 0x%06X (%s): %d -> %s" % (
            t, nm, len(h), ", ".join("0x%06X" % x for x in h)), file=buf)
    print("", file=buf)
    dump(0x6B9E90, 60, buf, "around the 0x6B9EE7 [+0xA80] write")
    dump(0x6B3140, 40, buf, "around 0x6B315E/0x6B3179")
    dump(0x6B3BF0, 30, buf, "BLACKROOM tick")
    dump(0x727A74, 45, buf, "count members on map (ghost gate?)")
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q8_clear.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
