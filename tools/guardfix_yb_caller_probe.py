# guardfix: back-solve ECX (selector) at every direct caller of 0x6E80CC,
# and confirm the CM dispatch arms 1252/1253/1256/1257 reach those callers.
import bisect
from collections import defaultdict
from capstone import *
from capstone.x86 import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0x7A10D0
buf = open(IMG, "rb").read()
def rd(va, n): return buf[va - BASE: va - BASE + n]
md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True

def callers_of(target):
    out = []
    i = buf.find(b"\xE8", CODE_LO - BASE, CODE_HI - BASE)
    while i != -1:
        va = i + BASE
        rel = int.from_bytes(buf[i+1:i+5], "little", signed=True)
        if ((va + 5 + rel) & 0xFFFFFFFF) == target:
            ins = next(md.disasm(rd(va, 5), va), None)
            if ins is not None and ins.id == X86_INS_CALL and ins.size == 5:
                out.append(va)
        i = buf.find(b"\xE8", i + 1, CODE_HI - BASE)
    return out

def ctx(va, back=48):
    lines = []
    best = []
    for b in range(back, 3, -1):
        s = va - b
        seq = []
        ok = False
        for ins in md.disasm(rd(s, b), s):
            if ins.address + ins.size > va: break
            seq.append(ins)
            if ins.address + ins.size == va: ok = True; break
        if ok and len(seq) > len(best): best = seq
    for ins in best:
        lines.append(f"      {ins.address:08X}  {ins.bytes.hex():<16} {ins.mnemonic} {ins.op_str}")
    return lines

TR = 0x6E80CC
cs = callers_of(TR)
print(f"=== direct callers of 0x{TR:X}: {len(cs)} ===")
for c in cs:
    print(f"  caller site 0x{c:X}")
    for l in ctx(c): print(l)
    print()

print("=== CM thunks claimed by Grobal2 comment ===")
for name, thunk in [("1252", 0x6E7E3C), ("1253", 0x6E7E90), ("1256", 0x6E83AC), ("1257", 0x6E8400)]:
    print(f"  CM {name} thunk 0x{thunk:X}:")
    n = 0
    for ins in md.disasm(rd(thunk, 0x60), thunk):
        print(f"      {ins.address:08X}  {ins.bytes.hex():<16} {ins.mnemonic} {ins.op_str}")
        n += 1
        if ins.mnemonic == "ret" or n > 18: break
    print()
