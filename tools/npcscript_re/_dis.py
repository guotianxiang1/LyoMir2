import sys, re
from capstone import *

PATH = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(PATH, "rb").read()

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True


def rd(va, n):
    off = va - BASE
    return DATA[off:off + n]


def dis(va, n=64, count=0):
    out = []
    for ins in md.disasm(rd(va, n), va):
        out.append("%08X  %-24s %s %s" % (ins.address, ins.bytes.hex(), ins.mnemonic, ins.op_str))
        if count and len(out) >= count:
            break
    return "\n".join(out)


def dstr(va):
    """Delphi long string at va: dword at va-4 = length."""
    off = va - BASE
    if off < 4:
        return None
    ln = int.from_bytes(DATA[off - 4:off], "little")
    if ln <= 0 or ln > 4096:
        return None
    return DATA[off:off + ln]


def find(pat):
    return [BASE + m.start() for m in re.finditer(re.escape(pat), DATA)]


def find_re(pat):
    return [BASE + m.start() for m in re.finditer(pat, DATA)]


def xref_imm(va):
    """find dwords equal to va"""
    b = va.to_bytes(4, "little")
    return [BASE + m.start() for m in re.finditer(re.escape(b), DATA)]


if __name__ == "__main__":
    a = int(sys.argv[1], 16)
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 96
    print(dis(a, n))
