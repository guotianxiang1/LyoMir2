"""Resolve soul-wash globals + find writers of key struct offsets."""
import io
import sys
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
CODE_LO, CODE_HI = 0x401000, 0x7A10D0


def rd32(va):
    if not (BASE <= va < BASE + len(data)):
        return None
    return struct.unpack("<I", data[va - BASE:va - BASE + 4])[0]


def xref_imm32(value, limit=60):
    needle = struct.pack("<I", value)
    hits = []
    p = CODE_LO - BASE
    endoff = CODE_HI - BASE
    while True:
        i = data.find(needle, p, endoff)
        if i < 0:
            break
        hits.append(BASE + i)
        p = i + 1
        if len(hits) >= limit:
            break
    return hits


def ctx(va, before=16, after=8):
    out = []
    start = va - before
    for i in md.disasm(data[start - BASE:start - BASE + before + after + 8], start):
        mark = " <<<" if i.address <= va < i.address + i.size else ""
        out.append("  %08X  %-7s %s%s" % (i.address, i.mnemonic, i.op_str, mark))
        if i.address > va + after:
            break
    return out


for name, g in [("0x7D5AEC max-slot ptr", 0x7D5AEC), ("0x7D6014 table ptr", 0x7D6014),
                ("0x7D5AEC->[]", None)]:
    if g is None:
        continue
    val = rd32(g)
    print("%s = %08X" % (name, val if val is not None else 0))
    if val is not None and BASE <= val < BASE + len(data):
        inner = rd32(val)
        print("   [%08X] = %08X" % (val, inner if inner is not None else 0))

print("\n=== writers of [0x7D5AEC] (the max-slot config) ===")
for h in xref_imm32(0x7D5AEC, 40):
    for l in ctx(h):
        print(l)
    print("  --")

print("\n=== writers/readers of [0x7D6014] (table singleton) ===")
for h in xref_imm32(0x7D6014, 40):
    for l in ctx(h):
        print(l)
    print("  --")
