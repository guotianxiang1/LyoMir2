"""Confirm the host of 0x6F517A (the slot re-bind call) is the login path."""
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
    dump(0x6F50F0, 60, buf, "host of the 0x6F517A slot re-bind")
    dump(0x6B9E10, 45, buf, "host of the 0x6B9EE2 group re-attach")
    for t, nm in [(0x6F50DC, "?"), (0x6F5100, "?")]:
        pass
    # who calls the enclosing function of 0x6F517A / 0x6B9EE2 once we know their starts
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q11_relogin.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
