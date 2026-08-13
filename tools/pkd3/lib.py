#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""M2Server flat_image helpers.  ImageBase 0x400000."""
import sys, io
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

BASE = 0x400000
IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

_d = None


def data():
    global _d
    if _d is None:
        _d = open(IMG, "rb").read()
    return _d


def rd(va, n):
    o = va - BASE
    return data()[o:o + n]


def u32(va):
    return int.from_bytes(rd(va, 4), "little")


def u16(va):
    return int.from_bytes(rd(va, 2), "little")


def u8(va):
    return rd(va, 1)[0]


_md = None


def md():
    global _md
    if _md is None:
        _md = Cs(CS_ARCH_X86, CS_MODE_32)
        _md.detail = False
    return _md


def dis(va, n=0x80, stop=None):
    out = []
    for i in md().disasm(rd(va, n), va):
        out.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
        if stop and i.address >= stop:
            break
    return "\n".join(out)


def p(va, n=0x80, stop=None):
    print(dis(va, n, stop))


def findall(pat, start=0):
    d = data()
    res = []
    i = d.find(pat, start)
    while i != -1:
        res.append(i + BASE)
        i = d.find(pat, i + 1)
    return res


def callers(target):
    """E8 rel32 call sites"""
    d = data()
    res = []
    for i in range(len(d) - 5):
        if d[i] == 0xE8:
            rel = int.from_bytes(d[i + 1:i + 5], "little", signed=True)
            if (i + 5 + rel + BASE) == target:
                res.append(i + BASE)
    return res


def jmpers(target):
    d = data()
    res = []
    for i in range(len(d) - 5):
        if d[i] == 0xE9:
            rel = int.from_bytes(d[i + 1:i + 5], "little", signed=True)
            if (i + 5 + rel + BASE) == target:
                res.append(i + BASE)
    return res


def refs_disp(disp, lo=0x401000, hi=0x7A0000, width=4):
    """instructions whose operand string mentions `disp` as a displacement."""
    d = data()
    pat = disp.to_bytes(width, "little") if width == 4 else bytes([disp])
    want = hex(disp)
    out, seen = [], set()
    i = d.find(pat)
    while i != -1:
        for back in range(1, 12):
            s = i - back
            if s < 0:
                continue
            ins = next(md().disasm(d[s:s + 16], s + BASE), None)
            if ins is None:
                continue
            if want in ins.op_str and ins.size >= back + width:
                if lo <= ins.address <= hi and ins.address not in seen:
                    seen.add(ins.address)
                    out.append((ins.address, ins.mnemonic, ins.op_str, ins.bytes.hex().upper()))
                break
        i = d.find(pat, i + 1)
    return sorted(out)


def show_refs(disp, label="", lo=0x401000, hi=0x7A0000, width=4):
    r = refs_disp(disp, lo, hi, width)
    print("=== 0x%X %s : %d refs ===" % (disp, label, len(r)))
    for a, m, o, b in r:
        print("   0x%-8X %-7s %-44s %s" % (a, m, o, b))
    return r


def dstr(va):
    """Delphi long string at va -> (len, bytes)"""
    n = int.from_bytes(rd(va - 4, 4), "little")
    if n > 0x1000:
        return (n, b"")
    return (n, rd(va, n))


def gbk(b):
    try:
        return b.decode("gbk")
    except Exception:
        return repr(b)


def funcstart(va, limit=0x2000):
    """walk back to the nearest 55 8B EC prologue"""
    d = data()
    o = va - BASE
    for i in range(o, max(0, o - limit), -1):
        if d[i] == 0x55 and d[i + 1] == 0x8B and d[i + 2] == 0xEC:
            return i + BASE
    return None
