"""Locate the native GroupSetV script-API registration + handler."""
import io
import os
import re
import struct

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000

with open(IMAGE, "rb") as fh:
    DATA = fh.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)


def hexdump(va, n, out, label=""):
    off = va - BASE
    print("--- raw 0x%06X %s ---" % (va, label), file=out)
    for row in range(0, n, 16):
        chunk = DATA[off + row:off + row + 16]
        print("0x%06X  %-47s  %s" % (
            va + row, chunk.hex(" ").upper(),
            "".join(chr(c) if 32 <= c < 127 else "." for c in chunk)), file=out)
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


def imm_xrefs(target, out, label=""):
    """Every dword in the image equal to `target` (covers push imm32 / mov reg,imm32
    and plain data tables)."""
    pat = struct.pack("<I", target)
    hits = []
    start = 0
    while True:
        i = DATA.find(pat, start)
        if i < 0:
            break
        hits.append(BASE + i)
        start = i + 1
    print("DWORD-refs to 0x%06X %s : %d" % (target, label, len(hits)), file=out)
    for va in hits[:40]:
        prev = DATA[va - BASE - 1]
        print("    0x%06X  (prev byte %02X)  ctx=%s" % (
            va, prev, DATA[va - BASE - 6:va - BASE + 8].hex(" ").upper()), file=out)
    print("", file=out)
    return hits


def main():
    buf = io.StringIO()

    # the .pas-ish declaration text blob
    hexdump(0x72DA00, 0x100, buf, "GroupSetV declaration text")
    # the registration-table name
    hexdump(0x732A60, 0x80, buf, "GroupSetV registrar name area")
    hexdump(0x7325E0, 0x60, buf, "SetV/GetV registrar name area")

    imm_xrefs(0x732A98, buf, "= 'GroupSetV' name")
    imm_xrefs(0x73260C, buf, "= 'SetV' name")

    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q3_groupsetv.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
