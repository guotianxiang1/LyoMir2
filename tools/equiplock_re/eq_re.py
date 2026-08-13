import sys, struct, io
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
    b = rd(va, 1); return b[0] if b else None
def u16(va):
    b = rd(va, 2); return struct.unpack("<H", b)[0] if b else None
def u32(va):
    b = rd(va, 4); return struct.unpack("<I", b)[0] if b else None
def i32(va):
    b = rd(va, 4); return struct.unpack("<i", b)[0] if b else None

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

def dis(va, count=60):
    out = []
    code = rd(va, count * 16)
    if code is None:
        return out
    for i in md.disasm(code, va):
        out.append(i)
        if len(out) >= count:
            break
    return out

def show(va, count=60):
    for i in dis(va, count):
        print("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))

def dstr(va):
    """Decode a Delphi long string whose data pointer is va."""
    off = va - BASE
    if off < 8:
        return None
    ln = struct.unpack("<I", DATA[off-4:off])[0]
    if ln < 0 or ln > 4096 or off + ln > len(DATA):
        return None
    raw = DATA[off:off+ln]
    try:
        return raw.decode("gbk", errors="replace")
    except Exception:
        return raw.hex()

def scan_ref(target, lo=0x401000, hi=0xB00000):
    """Scan for absolute dword references == target and rel32 calls/jmps to target."""
    hits = []
    # absolute dword
    for off in range(lo - BASE, hi - BASE - 4):
        v = struct.unpack("<I", DATA[off:off+4])[0]
        if v == target:
            hits.append((BASE + off, "abs"))
    return hits

def scan_disp(disp, lo=0x401000, hi=0xB00000):
    """Disassemble a range and print instructions whose operand displacement == disp
       and base register is a general reg (heuristic for [reg+disp])."""
    res = []
    code = DATA[lo-BASE:hi-BASE]
    for i in md.disasm(code, lo):
        for op in i.operands:
            if op.type == X86_OP_MEM:
                if op.mem.disp == disp and op.mem.base != 0 and op.mem.index == 0:
                    res.append((i.address, i.mnemonic + " " + i.op_str, i.bytes.hex().upper()))
                    break
    return res

if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "dis":
        show(int(sys.argv[2], 16), int(sys.argv[3]) if len(sys.argv) > 3 else 60)
    elif cmd == "str":
        for a in sys.argv[2:]:
            va = int(a, 16)
            print("0x%08X : %s" % (va, dstr(va)))
    elif cmd == "ref":
        for va, k in scan_ref(int(sys.argv[2], 16)):
            print("%08X %s" % (va, k))
    elif cmd == "disp":
        disp = int(sys.argv[2], 16)
        lo = int(sys.argv[3], 16) if len(sys.argv) > 3 else 0x401000
        hi = int(sys.argv[4], 16) if len(sys.argv) > 4 else 0xB00000
        for va, txt, b in scan_disp(disp, lo, hi):
            print("%08X  %-22s %s" % (va, b, txt))
