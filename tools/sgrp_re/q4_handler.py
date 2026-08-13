"""Disassemble the native GroupSetV handler sub_6E0830 (+ its callees)."""
import io
import os

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000

with open(IMAGE, "rb") as fh:
    DATA = fh.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)


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
    dump(0x6E0830, 90, buf)        # GroupSetV handler
    dump(0x731530, 12, buf)        # registrar call shape for SetV
    dump(0x7318A5, 12, buf)        # registrar call shape for GroupSetV
    dump(0x6E42CC, 30, buf)        # key composition group*1000+index?
    dump(0x6E4270, 40, buf)        # keyed lookup
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q4_handler.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
