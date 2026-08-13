import sys, struct
from capstone import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(IMG, "rb").read()

def rd(va, n):
    off = va - BASE
    if off < 0 or off + n > len(DATA):
        return None
    return DATA[off:off + n]

def u8(va):
    b = rd(va, 1)
    return b[0] if b else None

def u16(va):
    b = rd(va, 2)
    return struct.unpack("<H", b)[0] if b else None

def u32(va):
    b = rd(va, 4)
    return struct.unpack("<I", b)[0] if b else None

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

def dis(va, count=40, stop_at_ret=False):
    out = []
    code = rd(va, count * 16)
    if code is None:
        return out
    for i in md.disasm(code, va):
        out.append(i)
        if len(out) >= count:
            break
        if stop_at_ret and i.mnemonic in ("ret", "jmp"):
            break
    return out

def show(va, count=40, stop_at_ret=False):
    for i in dis(va, count, stop_at_ret):
        print("%08X  %-24s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))

def bytes_at(va, n):
    return rd(va, n).hex().upper()

if __name__ == "__main__":
    va = int(sys.argv[1], 16)
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 40
    show(va, n)
