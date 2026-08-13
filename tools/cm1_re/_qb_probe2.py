import struct, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
from capstone import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(IMG, "rb").read()

def rd(va, n):
    off = va - BASE
    if off < 0 or off + n > len(DATA):
        return None
    return DATA[off:off + n]

def u32(va):
    b = rd(va, 4)
    return struct.unpack("<I", b)[0] if b else None

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

def gstr(va, maxlen=100):
    b = rd(va, maxlen)
    if b is None:
        return None
    end = b.find(b"\x00")
    if end >= 0:
        b = b[:end]
    if len(b) < 2:
        return None
    try:
        s = b.decode("gbk")
    except Exception:
        return None
    printable = sum(1 for c in s if c.isprintable())
    if printable >= max(2, len(s) * 0.7):
        return s
    return None

def show(va, count=80, stop_at_ret=True):
    code = rd(va, count * 16)
    n = 0
    for i in md.disasm(code, va):
        cmt = ""
        for op in i.operands:
            if op.type == CS_OP_IMM:
                v = op.imm & 0xFFFFFFFF
                if 0x6C0000 <= v <= 0x7E0000:
                    s = gstr(v)
                    if s:
                        cmt = "  ; '%s'" % s
        print("%08X  %-22s %s %s%s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str, cmt))
        n += 1
        if n >= count:
            break
        if stop_at_ret and i.mnemonic == "ret":
            break

for name, va, cnt in [
    ("BROADCAST PUSH @0x71315C", 0x71315C, 120),
]:
    print("=" * 78)
    print(name)
    print("-" * 78)
    show(va, cnt, stop_at_ret=True)
    print()
