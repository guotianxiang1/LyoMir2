"""CM batch-3 agent: full disassembly of every method the Q3 arms call.

For each callee: entry bytes, linear+branch sweep, resolved Delphi string
literals, nested calls, and the SM idents pushed into the unicast send slots
(vtable offsets 0x250 / 0x254 on TPlayObject).

Writes staging/m_cm_c/callee.txt
"""
import json
import os
import re
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
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


def ann(ops):
    for mm in re.finditer(r"0x[0-9a-f]{6,8}", ops):
        s = dstr(int(mm.group(0), 0))
        if s:
            return '   ; "%s"' % s
    return ""


def sweep(start, maxins=1200):
    seen = set()
    todo = [start]
    order = []
    calls = []
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
            order.append((va, i.bytes.hex().upper(), m, ops, ann(ops)))
            if m == "call" and ops.startswith("0x"):
                t = int(ops, 0)
                if t not in calls:
                    calls.append(t)
            if m in CC:
                try:
                    t = int(ops, 0)
                except ValueError:
                    t = None
                if t is not None and CODE_LO <= t <= CODE_HI:
                    todo.append(t)
                va += i.size
                continue
            if m == "jmp":
                if ops.startswith("0x"):
                    va = int(ops, 0)
                    continue
                break
            if m in ("ret", "retf"):
                break
            va += i.size
    order.sort()
    return order, calls


inv = json.load(open(os.path.join(OUTDIR, "invent.json")))
real = {int(k): v for k, v in inv["real"].items()}
Q3 = inv["q3"]

TARGETS = json.load(open(os.path.join(OUTDIR, "q3_callees.json"))) \
    if os.path.exists(os.path.join(OUTDIR, "q3_callees.json")) else None

out = []
for ident in Q3:
    h = int(real[ident], 16)
    arm, armcalls = sweep(h, 200)
    out.append("")
    out.append("#" * 78)
    out.append("## CM %d (0x%04X) arm %08X" % (ident, ident, h))
    for c in armcalls:
        body, sub = sweep(c)
        out.append("")
        out.append("---- callee %08X  entry bytes %s  (%d ins) subcalls=%s"
                   % (c, data[c - BASE:c - BASE + 24].hex().upper(), len(body),
                      ["%08X" % s for s in sub[:24]]))
        for va, hx, m, ops, a in body:
            out.append("%08X  %-20s %s %s%s" % (va, hx, m, ops, a))

open(os.path.join(OUTDIR, "callee.txt"), "w", encoding="utf-8").write("\n".join(out))
sys.stdout.reconfigure(encoding="utf-8")
print("wrote callee.txt lines=%d" % len(out))
