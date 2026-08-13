"""Disassemble a VA range out of the M2Server flat image.

Usage: mirror2_disasm.py <hex VA> <count bytes> [--raw]
ImageBase 0x400000, file_off = VA - 0x400000.
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000


def load():
    with open(IMG, "rb") as f:
        return f.read()


def main():
    va = int(sys.argv[1], 16)
    n = int(sys.argv[2], 0)
    raw = "--raw" in sys.argv
    data = load()
    off = va - BASE
    buf = data[off:off + n]
    if raw:
        for i in range(0, len(buf), 16):
            chunk = buf[i:i + 16]
            print("%08X  %s  %s" % (
                va + i,
                " ".join("%02X" % b for b in chunk).ljust(47),
                "".join(chr(b) if 32 <= b < 127 else "." for b in chunk)))
        return
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = False
    for ins in md.disasm(buf, va):
        print("%08X  %-24s %s %s" % (
            ins.address,
            "".join("%02x" % b for b in ins.bytes),
            ins.mnemonic, ins.op_str))


if __name__ == "__main__":
    main()
