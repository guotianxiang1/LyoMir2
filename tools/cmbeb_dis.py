"""Raw capstone disassembly window over flat_image.bin (ImageBase 0x400000).

usage: cmbeb_dis.py <hexVA> [count] [--bytes]
"""
import sys
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False


def rd32(va):
    o = va - BASE
    if o < 0 or o + 4 > len(data):
        return None
    return struct.unpack("<I", data[o:o + 4])[0]


def dstr(va):
    """Delphi long string: dword length at ptr-4, GBK payload."""
    if va is None or va - BASE - 4 < 0 or va - BASE >= len(data):
        return None
    n = rd32(va - 4)
    if n is None or not (1 <= n <= 600):
        return None
    raw = data[va - BASE:va - BASE + n]
    if b"\x00" in raw:
        return None
    try:
        return raw.decode("gbk")
    except UnicodeDecodeError:
        return None


def main():
    va = int(sys.argv[1], 16)
    cnt = int(sys.argv[2]) if len(sys.argv) > 2 else 60
    off = va - BASE
    out = []
    for i in md.disasm(data[off:off + cnt * 16], va):
        ann = ""
        for tok in i.op_str.replace(",", " ").replace("[", " ").replace("]", " ").split():
            if tok.startswith("0x") and len(tok) >= 8:
                s = dstr(int(tok, 0))
                if s:
                    ann = '   ; "%s"' % s
                    break
        out.append("%08X  %-24s %-8s %s%s"
                   % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str, ann))
        if len(out) >= cnt:
            break
    sys.stdout.reconfigure(encoding="utf-8")
    print("\n".join(out))


main()
