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

def gstr(va, maxlen=80):
    b = rd(va, maxlen)
    if b is None:
        return "?"
    end = b.find(b"\x00")
    if end >= 0:
        b = b[:end]
    try:
        return b.decode("gbk")
    except Exception:
        return b.decode("latin1", "replace")

def show(va, count=60, stop_at_ret=True):
    code = rd(va, count * 16)
    n = 0
    for i in md.disasm(code, va):
        cmt = ""
        # annotate immediate that points to a string in .data/.text
        for op in i.operands:
            if op.type == CS_OP_IMM:
                v = op.imm & 0xFFFFFFFF
                if 0x6C0000 <= v <= 0x7E0000:
                    s = gstr(v)
                    if s and any(ord(c) > 0x1F for c in s) and ('\ufffd' not in s or len(s) > 2):
                        # only show if it looks like text
                        printable = sum(1 for c in s if c.isprintable())
                        if printable >= max(2, len(s)//2):
                            cmt = "  ; '%s'" % s
        print("%08X  %-22s %s %s%s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str, cmt))
        n += 1
        if n >= count:
            break
        if stop_at_ret and i.mnemonic == "ret":
            break

targets = {
    "LEAF 1090 @0x6D9732": 0x6D9732,
    "LEAF 1200 @0x6DA21F": 0x6DA21F,
    "LEAF 1217 @0x6DA372": 0x6DA372,
}
for name, va in targets.items():
    print("=" * 78)
    print(name)
    print("-" * 78)
    show(va, 40, stop_at_ret=True)
    print()

print("=" * 78)
print("STRINGS in 0x6BD674 worker")
print("-" * 78)
for a in (0x6BD89C, 0x6BD8C4, 0x6BD8E4):
    print("%08X: '%s'" % (a, gstr(a)))
print()

print("=" * 78)
print("SINGLETON [0x7D62DC]")
print("-" * 78)
p = u32(0x7D62DC)
print("  [0x7D62DC] = 0x%08X (data-slot value at rest, usually 0/uninit in image)" % (p or 0))
