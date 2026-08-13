"""Deeper reverse of latter-half missing CM handlers: SM in callees, xrefs, strings."""
import os
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
OUT = r"D:/loym2/staging/m_cm_b/deep.txt"
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

def rd32(va):
    return struct.unpack("<I", data[va-BASE:va-BASE+4])[0]

def ins_at(va):
    for i in md.disasm(data[va-BASE:va-BASE+16], va):
        return i
    return None

def xref_imm32(value, start=0x401000, end=0x7A10D0):
    needle = struct.pack("<I", value)
    hits = []
    off = start - BASE
    endoff = end - BASE
    p = off
    while True:
        i = data.find(needle, p, endoff)
        if i < 0:
            break
        hits.append(BASE + i)
        p = i + 1
        if len(hits) >= 40:
            break
    return hits

def gbk_at(va, n=64):
    raw = data[va-BASE:va-BASE+n]
    # Delphi string: if looks like ptr, try -4 length
    try:
        return raw.split(b"\x00")[0].decode("gbk", errors="replace")
    except Exception:
        return repr(raw[:32])

def walk_sends(start, limit=0x800, max_ins=250):
    """Linear-ish walk collecting mov dx,imm / call [ebx+0x250/254] and call 0x..."""
    sends = []
    calls = []
    va = start
    pend_dx = None
    n = 0
    seen = set()
    while n < max_ins:
        n += 1
        if va in seen:
            break
        seen.add(va)
        ins = ins_at(va)
        if ins is None:
            break
        m, ops = ins.mnemonic, ins.op_str
        if m == "mov" and ops.startswith("dx, 0x"):
            pend_dx = int(ops.split(", ")[1], 0)
        if m == "mov" and ops.startswith("edx, 0x") and "ptr" not in ops:
            try:
                pend_dx = int(ops.split(", ")[1], 0)
            except Exception:
                pass
        if m == "call":
            if "0x250" in ops or "0x254" in ops:
                slot = "250" if "0x250" in ops else "254"
                sends.append((va, slot, pend_dx))
            elif ops.startswith("0x"):
                calls.append(int(ops, 0))
            pend_dx = None
        if m == "ret":
            break
        if m == "jmp" and ops.startswith("0x"):
            va = int(ops, 0)
            continue
        va += ins.size
        if va - start > limit:
            break
    return sends, calls

lines = []

# 0x7D70E4 xrefs (the dword itself in CODE)
lines.append("=== xref 0x7D70E4 (CM 3290 payload ptr) ===")
hits = xref_imm32(0x7D70E4)
for h in hits:
    # disasm around
    ctx = []
    va = max(h - 12, 0x401000)
    for i in md.disasm(data[va-BASE:va-BASE+32], va):
        mark = " <<<" if i.address <= h < i.address + i.size else ""
        ctx.append("  %08X  %s %s%s" % (i.address, i.mnemonic, i.op_str, mark))
        if i.address > h + 8:
            break
    lines.append("hit %08X" % h)
    lines.extend(ctx)

# 4106 handler
lines.append("\n=== CM 4106 handler 0x6DA030 ===")
va = 0x6DA030
for i in md.disasm(data[va-BASE:va-BASE+0x40], va):
    lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
    if i.mnemonic == "jmp" and i.op_str.startswith("0x6dbc"):
        break

# 0x408D40
lines.append("\n=== 0x408D40 ===")
va = 0x408D40
for i in md.disasm(data[va-BASE:va-BASE+0x30], va):
    lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
    if i.mnemonic == "ret":
        break

# payload bytes at [0x7D70E4] - it's a pointer global. Read the pointer value in the image.
ptr = rd32(0x7D70E4)
lines.append("\n=== [0x7D70E4] static pointer = %08X ===" % ptr)
if 0x400000 <= ptr < 0x400000 + len(data):
    blob = data[ptr-BASE:ptr-BASE+16]
    lines.append("bytes: " + blob.hex())
    lines.append("as double-le: " + str(struct.unpack("<d", blob[:8])[0]))
    lines.append("as 2x u32: %08X %08X" % struct.unpack("<II", blob[:8]))

# SM sends in key callees
callees = {
    3283: 0x6E67B0, 3284: 0x6E6EA4, 3285: 0x6E6DE8, 3286: 0x6E6B54,
    3287: 0x6E8734, 3288: 0x6E8820, 3294: 0x6EB190, 3295: 0x6EB8E4,
    3306: 0x6EFD54, 3307: 0x6CBD78, 3340: 0x79E78C, 3344: 0x6EC5D8,
    3410: 0x6EBE50, 3503: 0x6EF970, 4102: 0x6B7BCC, 4123: 0x6BF908,
    4124: 0x6BFA88, 4125: 0x746C34, 4126: 0x6BF75C, 4127: 0x74730C,
    4128: 0x6B7184, 4150: 0x6F2924, 4151: 0x6999D4, 4173: 0x6E600C,
    4204: 0x6F03E8, 4205: 0x6F01E4, 4215: 0x6E8684, 4218: 0x6F3104,
    4408: 0x6F37EC, 4409: 0x6F38A8, 4417: 0x699EB4, 4446: 0x6F75C4,
    4496: 0x6FAC8C, 4626: 0x6AE260, 4629: 0x6F7C40, 4646: 0x6FBB90,
    4647: 0x6FB6FC, 4648: 0x6FB874, 4649: 0x6FBB28, 4650: 0x6FB51C,
    4651: 0x6FC054, 4105: 0x6EE174,
}
lines.append("\n=== SM sends in first-level callees ===")
for ident, va in sorted(callees.items()):
    sends, calls = walk_sends(va)
    sm = ",".join("%s:%s" % (s, d) for _, s, d in sends) or "-"
    lines.append("CM %d callee %08X  SM=%s  ncalls=%d" % (ident, va, sm, len(calls)))
    # also scan first 6 game callees one more level
    for c in calls[:8]:
        if c < 0x600000:
            continue
        s2, _ = walk_sends(c, limit=0x400, max_ins=120)
        if s2:
            lines.append("    via %08X  SM=%s" % (c, ",".join("%s:%s" % (s, d) for _, s, d in s2)))

# 4629 deeper: dump until we see send or 120 ins
lines.append("\n=== CM 4629 callee 0x6F7C40 first 120 ins ===")
va = 0x6F7C40
n = 0
for i in md.disasm(data[va-BASE:va-BASE+0x400], va):
    lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
    n += 1
    if n >= 120:
        break

# strings at 0x6f7618 and 0x6ee248
for sva, name in [(0x6F7618, "4446 fail str"), (0x6EE248, "4105 NORIDE str")]:
    lines.append("\n=== string %s @ %08X ===" % (name, sva))
    # Delphi long string: ptr points to chars, length at ptr-4
    ln = rd32(sva - 4) if sva >= BASE + 4 else 0
    lines.append("len-prefix=%d  text=%r" % (ln, gbk_at(sva, min(ln, 80) if 0 < ln < 200 else 64)))

# stub scan of all callees
lines.append("\n=== stub scan ===")
for ident, va in sorted(callees.items()):
    b = data[va-BASE:va-BASE+12]
    if b[0] == 0xC3 or b[:2] == b"\x33\xc0" or b[:3] == b"\x55\x8b\xec" and b[3:5] == b"\x33\xc0":
        lines.append("CM %d %08X bytes %s" % (ident, va, b[:9].hex()))

# 4314/4315 confirm neighbors
lines.append("\n=== around 0x6F2924..0x6F2950 ===")
va = 0x6F2920
for i in md.disasm(data[va-BASE:va-BASE+0x40], va):
    lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))

open(OUT, "w", encoding="utf-8").write("\n".join(lines))
print("wrote", OUT, "lines", len(lines))
