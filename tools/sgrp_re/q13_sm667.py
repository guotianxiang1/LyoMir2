"""SM 667 member-list builder sub_7271D0 + the alive probe sub_7270F8:
does native filter ghost members out of the roster packet?"""
import io
import os

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000

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


def main():
    buf = io.StringIO()
    dump(0x7270F8, 40, buf, "alive probe")
    dump(0x7271D0, 95, buf, "SM 667 roster builder")
    dump(0x72843C, 25, buf, "slot -> name")
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q13_sm667.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
