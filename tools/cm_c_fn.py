"""Dump ONE native function cleanly (no RTL recursion), with Delphi string
annotations and send-slot decoding.  Usage: cm_c_fn.py VA [VA ...]"""
import re
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
CC = {"je", "jz", "jne", "jnz", "jg", "jnle", "jl", "jnge", "jge", "jnl",
      "jle", "jng", "ja", "jnbe", "jb", "jnae", "jae", "jnb", "jbe", "jna"}
SLOT = {0x250: "SendDefMessage", 0x254: "SendDefMessage+body",
        0x260: "refresh", 0xD4: "SysMsg?", 0xD8: "RM?", 0xE0: "RM?"}


def rd32(va):
    o = va - BASE
    return struct.unpack("<I", data[o:o + 4])[0] if 0 <= o and o + 4 <= len(data) else None


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


def ann(m, ops):
    a = ""
    for mm in re.finditer(r"0x[0-9a-f]{6,8}", ops):
        s = dstr(int(mm.group(0), 0))
        if s:
            a = '   ; "%s"' % s
            break
    sm = re.search(r"\[e?[a-z]x \+ (0x[0-9a-f]+)\]$", ops)
    if m == "call" and sm:
        off = int(sm.group(1), 0)
        if off in SLOT:
            a += "   ; VMT+%s %s" % (sm.group(1), SLOT[off])
    if m in ("mov",) and re.match(r"^(dx|edx), 0x[0-9a-f]+$", ops):
        v = int(ops.split(", ")[1], 0)
        if 100 <= v <= 66000:
            a += "   ; = %d" % v
    return a


def fn(start, limit=900):
    """Follow the function: linear, taking every intra-function branch, stopping
    at the last ret reachable.  Bounded by the next function prologue."""
    seen, todo, order = set(), [start], []
    while todo:
        va = todo.pop(0)
        for _ in range(limit):
            if va in seen:
                break
            seen.add(va)
            i = ins_at(va)
            if i is None:
                break
            m, ops = i.mnemonic, i.op_str
            order.append((va, i.bytes.hex().upper(), m, ops, ann(m, ops)))
            if m in CC:
                try:
                    t = int(ops, 0)
                    if abs(t - start) < 0x4000:
                        todo.append(t)
                except ValueError:
                    pass
                va += i.size
                continue
            if m == "jmp":
                if ops.startswith("0x"):
                    t = int(ops, 0)
                    if abs(t - start) < 0x4000:
                        va = t
                        continue
                break
            if m in ("ret", "retf"):
                break
            va += i.size
    order.sort()
    return order


sys.stdout.reconfigure(encoding="utf-8")
for a in sys.argv[1:]:
    v = int(a, 16)
    print("=" * 76)
    print("FUNC %08X  bytes %s" % (v, data[v - BASE:v - BASE + 24].hex().upper()))
    for va, hx, m, ops, an in fn(v):
        print("%08X  %-22s %s %s%s" % (va, hx, m, ops, an))
