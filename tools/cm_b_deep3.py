"""Prove 3290 payload origin; decode 4647 strings; dump 0x40F0A4; xref 0x792Cxx func."""
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False
OUT = r"D:/loym2/staging/m_cm_b/deep3.txt"
lines = []

def dump(va, n=50, nbytes=0x120):
    lines.append("=== %08X ===" % va)
    c = 0
    for i in md.disasm(data[va-BASE:va-BASE+nbytes], va):
        lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
        c += 1
        if c >= n:
            break
        if i.mnemonic == "ret" and c > 2:
            break

def gbk_str(va):
    ln = struct.unpack("<I", data[va-BASE-4:va-BASE])[0]
    raw = data[va-BASE:va-BASE+min(ln, 120)]
    try:
        return ln, raw.decode("gbk")
    except Exception:
        return ln, repr(raw)

dump(0x40F0A4, 40, 0x80)
dump(0x4034AC, 20, 0x40)

# find function start of 0x792C46 by scanning back for 55 8B EC
start = None
for va in range(0x792C46, 0x792C46 - 0x400, -1):
    if data[va-BASE:va-BASE+3] == b"\x55\x8b\xec":
        start = va
        break
lines.append("func start for 792C46: %s" % ("%08X" % start if start else "NONE"))
if start:
    dump(start, 30, 0x80)
    # xref the function start as relative call targets is hard; search imm32 of start
    needle = struct.pack("<I", start)
    hits = []
    p = 0x1000
    while len(hits) < 20:
        i = data.find(needle, p, 0x3A10D0)
        if i < 0:
            break
        hits.append(BASE + i)
        p = i + 1
    lines.append("imm32 hits of func start: " + " ".join("%08X" % h for h in hits))

for sva in (0x6FB814, 0x6FB82C, 0x6FB848, 0x6FB860, 0x6EE420, 0x6EE448):
    ln, t = gbk_str(sva)
    lines.append("str %08X len=%d %s" % (sva, ln, t))

# 0x250 vs 0x254: look at a known SendDefMessage wrapper if we know it
# search readers of +0x18DC as 66 8B 80 DC 18 00 00 or 66 8B 90 / 0FB7
pat = bytes.fromhex("DC180000")
off = 0x1000
n = 0
lines.append("=== 66 8B/0F B7 ... DC 18 00 00 (reads of +0x18DC) ===")
while n < 30:
    i = data.find(pat, off, 0x3A10D0)
    if i < 0:
        break
    va = BASE + i
    pre = data[i-3:i]
    lines.append("%08X pre=%s" % (va, pre.hex()))
    off = i + 1
    n += 1

# 4629: 0x772DA8
dump(0x772DA8, 25, 0x60)

open(OUT, "w", encoding="utf-8").write("\n".join(lines))
print("wrote", OUT)
