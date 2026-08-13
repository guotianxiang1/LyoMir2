"""Linear disassembler with Delphi-string annotation.

Usage: cm_b_dis.py <hexVA> <nbytes> [more VA/len pairs...]
Prints to stdout; caller redirects. Does NOT follow jumps -- shows every arm.
"""
import re
import struct
import sys
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
    if va is None or va - BASE - 4 < 0 or va - BASE >= len(data):
        return None
    n = rd32(va - 4)
    if n is None or not (1 <= n <= 400):
        return None
    raw = data[va - BASE:va - BASE + n]
    if b"\x00" in raw:
        return None
    try:
        s = raw.decode("gbk")
    except UnicodeDecodeError:
        return None
    if all(31 < ord(c) < 127 or ord(c) > 0x7F for c in s):
        return s
    return None


def dump(va, n):
    out = []
    for i in md.disasm(data[va - BASE:va - BASE + n], va):
        ann = ""
        for m in re.finditer(r"0x[0-9a-f]{6,8}", i.op_str):
            s = dstr(int(m.group(0), 0))
            if s:
                ann = '   ; "%s"' % s
                break
        out.append("%08X  %-20s %s %s%s"
                   % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str, ann))
    return out


args = sys.argv[1:]
lines = []
for k in range(0, len(args), 2):
    va = int(args[k], 16)
    n = int(args[k + 1], 0)
    lines.append("==== %08X .. %08X ====" % (va, va + n))
    lines += dump(va, n)
sys.stdout.reconfigure(encoding="utf-8")
print("\n".join(lines))
