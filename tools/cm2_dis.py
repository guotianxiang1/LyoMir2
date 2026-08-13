"""Linear disassembler over flat_image.bin for the M2Server CM dispatcher study.

Usage:
  cm2_dis.py <start_va_hex> <length> [--bytes]
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000

_img = None


def img():
    global _img
    if _img is None:
        with open(IMAGE, "rb") as f:
            _img = f.read()
    return _img


def read(va, n):
    off = va - BASE
    return img()[off:off + n]


def dis(va, length, show_bytes=True, out=None):
    """Resilient linear sweep: embedded jump tables abort capstone, so resume past them."""
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = False
    data = read(va, length)
    lines = []
    pos = 0
    while pos < length:
        got = 0
        for i in md.disasm(data[pos:], va + pos):
            b = " ".join("%02X" % c for c in i.bytes)
            if show_bytes:
                lines.append("%08X  %-24s %s %s" % (i.address, b, i.mnemonic, i.op_str))
            else:
                lines.append("%08X  %s %s" % (i.address, i.mnemonic, i.op_str))
            got = (i.address + i.size) - (va + pos)
        if got == 0:
            lines.append("%08X  %-24s (undecodable)" % (va + pos, "%02X" % data[pos]))
            pos += 1
        else:
            pos += got
    text = "\n".join(lines)
    if out:
        with open(out, "w", encoding="utf-8") as f:
            f.write(text)
    return text


if __name__ == "__main__":
    va = int(sys.argv[1], 16)
    ln = int(sys.argv[2], 0)
    print(dis(va, ln))
