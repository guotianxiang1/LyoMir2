"""Disassemble the N bytes preceding each given VA (linear, best effort)."""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000


def main():
    n = int(sys.argv[1], 0)
    data = open(IMG, "rb").read()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    for a in sys.argv[2:]:
        va = int(a, 16)
        # align by trying successive start offsets until we land exactly on va
        for skew in range(0, n):
            start = va - n + skew
            buf = data[start - BASE: va - BASE + 6]
            ok = False
            out = []
            for ins in md.disasm(buf, start):
                out.append("%08X  %-20s %s %s" % (
                    ins.address, "".join("%02x" % b for b in ins.bytes),
                    ins.mnemonic, ins.op_str))
                if ins.address == va:
                    ok = True
                    break
            if ok:
                print("--- before 0x%06X ---" % va)
                print("\n".join(out))
                break


if __name__ == "__main__":
    main()
