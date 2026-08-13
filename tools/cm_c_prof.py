"""CM batch-3 agent: three-piece evidence (VA + bytes + disassembly) for the
Q3 slice of the MISSING list, plus the dispatcher prologue that fixes the frame
layout every arm reads through.

Writes staging/m_cm_c/prof.txt
"""
import json
import os
import re
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
DEFAULT = 0x6DBC2C
CODE_LO, CODE_HI = 0x401000, 0x7A10D0
OUTDIR = r"D:/loym2/staging/m_cm_c"

data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False
CC = {"je", "jz", "jne", "jnz", "jg", "jnle", "jl", "jnge", "jge", "jnl",
      "jle", "jng", "ja", "jnbe", "jb", "jnae", "jae", "jnb", "jbe", "jna"}


def rd32(va):
    o = va - BASE
    if o < 0 or o + 4 > len(data):
        return None
    return struct.unpack("<I", data[o:o + 4])[0]


def dstr(va):
    if va is None or va - BASE - 4 < 0 or va - BASE >= len(data):
        return None
    n = rd32(va - 4)
    if n is None or not (1 <= n <= 400):
        return None
    raw = data[va - BASE:va - BASE + n]
    if b"\x00" in raw:
        return None
    try:
        return raw.decode("gbk")
    except UnicodeDecodeError:
        return None


def ins_at(va):
    o = va - BASE
    if o < 0 or o >= len(data):
        return None
    for i in md.disasm(data[o:o + 16], va):
        return i
    return None


def ann_of(ops):
    for mm in re.finditer(r"0x[0-9a-f]{6,8}", ops):
        s = dstr(int(mm.group(0), 0))
        if s:
            return '   ; "%s"' % s
    return ""


def dump(start, maxins=400, stop_default=True):
    """Breadth walk of one arm; returns (lines, callees, reaches_default)."""
    lines, calls = [], []
    seen = set()
    todo = [start]
    reach = False
    order = []
    while todo:
        va = todo.pop(0)
        n = 0
        while n < maxins:
            n += 1
            if va in seen or not (CODE_LO <= va <= CODE_HI):
                break
            seen.add(va)
            i = ins_at(va)
            if i is None:
                break
            m, ops = i.mnemonic, i.op_str
            order.append((va, i.bytes.hex().upper(), m, ops, ann_of(ops)))
            if m == "call" and ops.startswith("0x"):
                t = int(ops, 0)
                if t not in calls:
                    calls.append(t)
            if m in CC:
                try:
                    tgt = int(ops, 0)
                except ValueError:
                    tgt = None
                if tgt == DEFAULT:
                    reach = True
                elif tgt is not None and CODE_LO <= tgt <= CODE_HI:
                    todo.append(tgt)
                va += i.size
                continue
            if m == "jmp":
                if ops.startswith("0x"):
                    t = int(ops, 0)
                    if t == DEFAULT:
                        reach = True
                        break
                    va = t
                    continue
                break
            if m in ("ret", "retf"):
                break
            va += i.size
    order.sort()
    for va, hx, m, ops, a in order:
        lines.append("%08X  %-20s %s %s%s" % (va, hx, m, ops, a))
    return lines, calls, reach


def stub_of(va):
    b = data[va - BASE:va - BASE + 16]
    if b[:3] == b"\x33\xc0\xc3":
        return "33C0C3 const-false"
    if b[:7] == b"\x55\x8b\xec\x33\xc0\x5d\xc3":
        return "558BEC33C05DC3 const-false"
    if b[:7] == b"\x55\x8b\xec\x33\xc0\x5d\xc2":
        return "558BEC33C05DC2%02X%02X const-false" % (b[7], b[8])
    if b[:1] == b"\xc3":
        return "C3 empty"
    if b[:5] == b"\x55\x8b\xec\x5d\xc3":
        return "558BEC5DC3 empty"
    if b[:10] == b"\x55\x8b\xec\x51\x89\x45\xfc\x59\x5d\xc3":
        return "558BEC518945FC595DC3 empty"
    return None


inv = json.load(open(os.path.join(OUTDIR, "invent.json")))
real = {int(k): v for k, v in inv["real"].items()}
Q3 = inv["q3"]

out = []
out.append("=== dispatcher prologue (frame layout every arm reads) ===")
p = 0x6D7FC0
for _ in range(60):
    i = ins_at(p)
    if i is None:
        break
    out.append("%08X  %-20s %s %s%s"
               % (p, i.bytes.hex().upper(), i.mnemonic, i.op_str, ann_of(i.op_str)))
    p += i.size

for ident in Q3:
    h = int(real[ident], 16)
    lines, calls, reach = dump(h)
    out.append("")
    out.append("=" * 78)
    raw = data[h - BASE:h - BASE + 32].hex().upper()
    out.append("CM %d (0x%04X)  handler VA %08X  bytes %s" % (ident, ident, h, raw))
    out.append("reaches-default=%s  callees=%s" % (reach, ["%08X" % c for c in calls]))
    for c in calls:
        s = stub_of(c)
        if s:
            out.append("  callee %08X is an empty body: %s" % (c, s))
    out.extend(lines)

open(os.path.join(OUTDIR, "prof.txt"), "w", encoding="utf-8").write("\n".join(out))
sys.stdout.reconfigure(encoding="utf-8")
print("wrote prof.txt  idents=%d" % len(Q3))
print(Q3)
