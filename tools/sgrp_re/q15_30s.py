"""The two 30_000 ms interval gates inside the player Run tick (0x6B35F5, 0x6B3B54):
do either of them sweep dead/ghost members out of the group?"""
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
    dump(0x6B35E0, 70, buf, "30s gate #1")
    dump(0x6B3B40, 70, buf, "30s gate #2 (immediately before the BLACKROOM block)")
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q15_30s.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
