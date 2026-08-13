# guardfix: exhaustively hunt for 3001/3002/3005/3006 as wire idents in the M2 image.
import sys
from collections import defaultdict
from capstone import *
from capstone.x86 import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0x7A10D0
buf = open(IMG, "rb").read()
def rd(va, n): return buf[va - BASE: va - BASE + n]
md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True

TARGETS = [3001, 3002, 3005, 3006]

print("=== A. raw 16-bit immediate byte patterns across WHOLE image ===")
for t in TARGETS:
    w = t.to_bytes(2, "little")
    pats = {
        "mov dx,imm16   66 BA": b"\x66\xBA" + w,
        "mov cx,imm16   66 B9": b"\x66\xB9" + w,
        "mov ax,imm16   66 B8": b"\x66\xB8" + w,
        "mov bx,imm16   66 BB": b"\x66\xBB" + w,
    }
    for name, p in pats.items():
        hits = []
        i = buf.find(p)
        while i != -1:
            hits.append(hex(i + BASE)); i = buf.find(p, i + 1)
        print(f"  {t} {name}: {len(hits)} {hits[:12]}")

print()
print("=== B. 32-bit immediate occurrences (mov r32,imm32 / push imm32 / cmp) ===")
for t in TARGETS:
    d = t.to_bytes(4, "little")
    hits = []
    i = buf.find(d)
    while i != -1:
        va = i + BASE
        pre = buf[i-1] if i > 0 else 0
        pre2 = buf[i-2] if i > 1 else 0
        kind = None
        if 0xB8 <= pre <= 0xBF: kind = f"mov r32,imm32 (op {pre:02X}) @{hex(va-1)}"
        elif pre == 0x68: kind = f"push imm32 @{hex(va-1)}"
        elif pre2 == 0x81: kind = f"grp1 r32,imm32 @{hex(va-2)}"
        elif pre2 == 0xC7: kind = f"mov rm,imm32 @{hex(va-2)}"
        if kind and CODE_LO <= va < CODE_HI:
            hits.append(kind)
        i = buf.find(d, i + 1)
    print(f"  {t}: {len(hits)} code-shaped {hits[:14]}")

print()
print("=== C. disassemble 0x6E80CC .. 0x6E8200 (claimed translator) ===")
for ins in md.disasm(rd(0x6E80CC, 0x6E8200 - 0x6E80CC), 0x6E80CC):
    print(f"  {ins.address:08X}  {ins.bytes.hex():<20} {ins.mnemonic} {ins.op_str}")
