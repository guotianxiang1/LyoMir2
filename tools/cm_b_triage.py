"""Triage callee functions: walk each until a terminating ret, report size,
call targets, and Delphi-string references. Read-only.

Usage: cm_b_triage.py <hexVA> [more VAs...]
"""
import re
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False


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
        s = raw.decode("gbk")
    except UnicodeDecodeError:
        return None
    if all(31 < ord(c) < 127 or ord(c) > 0x7F for c in s):
        return s
    return None


def triage(va):
    """Linear scan from va until we hit a ret at top-level, following
    fallthrough only. Records call targets + strings. Stops at first ret
    that isn't preceded by an unconditional jmp skipping it."""
    calls = []
    strs = []
    size = 0
    end = va
    o = va - BASE
    max_scan = 0x800
    addr = va
    last_ret = None
    while addr - va < max_scan:
        chunk = data[addr - BASE:addr - BASE + 16]
        ins = None
        for i in md.disasm(chunk, addr):
            ins = i
            break
        if ins is None:
            break
        m, ops = ins.mnemonic, ins.op_str
        for mm in re.finditer(r"0x[0-9a-f]{6,8}", ops):
            s = dstr(int(mm.group(0), 0))
            if s:
                strs.append(s)
        if m == "call":
            t = ops
            if t.startswith("0x"):
                calls.append(int(t, 0))
            else:
                calls.append(ops)  # indirect (vmt)
        if m in ("ret", "retn"):
            last_ret = addr + ins.size
            end = last_ret
            break
        addr += ins.size
    size = (last_ret - va) if last_ret else None
    return size, calls, strs


sys.stdout.reconfigure(encoding="utf-8")
for a in sys.argv[1:]:
    va = int(a, 16)
    size, calls, strs = triage(va)
    direct = [c for c in calls if isinstance(c, int)]
    indirect = [c for c in calls if not isinstance(c, int)]
    print("==== %08X  size=%s  ncalls=%d ====" % (va, size, len(calls)))
    if direct:
        print("  direct: " + " ".join("%08X" % c for c in direct))
    if indirect:
        print("  vmt/ind: " + " | ".join(indirect))
    if strs:
        print("  strs: " + " | ".join(strs))
