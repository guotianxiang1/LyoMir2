"""TGroup.GroupSetV = sub_727754 (called from the script handler sub_6E0830)."""
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
    dump(0x727754, 70, buf)     # TGroup GroupSetV
    dump(0x728518, 20, buf)     # slot setter used by ctor/add
    dump(0x728404, 20, buf)     # slot allocator
    dump(0x727FB0, 100, buf)    # DelMember / leader transfer
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q5_tgroup_setv.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
