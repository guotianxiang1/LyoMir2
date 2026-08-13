"""Full function dump: bytes + disassembly + string/callee annotation.

usage: cmbeb_fn.py <hexVA> [maxins]
       cmbeb_fn.py arm <ident>      dump the dispatcher arm for a CM ident
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
OUTDIR = r"D:/loym2/staging/cmbe_b"
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
    if n is None or not (1 <= n <= 600):
        return None
    raw = data[va - BASE:va - BASE + n]
    if b"\x00" in raw:
        return None
    try:
        return raw.decode("gbk")
    except UnicodeDecodeError:
        return None


def dump(start, maxins=400, stop_default=False):
    lines = []
    seen = set()
    todo = [start]
    order = []
    while todo:
        p = todo.pop(0)
        while len(order) < maxins:
            if p in seen or not (BASE <= p < BASE + len(data)):
                break
            seen.add(p)
            i = None
            for x in md.disasm(data[p - BASE:p - BASE + 16], p):
                i = x
                break
            if i is None:
                break
            order.append(i)
            m, ops = i.mnemonic, i.op_str
            if m.startswith("j") and ops.startswith("0x"):
                t = int(ops, 0)
                if not (stop_default and t == DEFAULT):
                    todo.append(t)
                if m == "jmp":
                    break
            if m in ("ret", "retf"):
                break
            p += i.size
    order.sort(key=lambda x: x.address)
    for i in order:
        ann = ""
        for mm in re.finditer(r"0x[0-9a-f]{6,8}", i.op_str):
            s = dstr(int(mm.group(0), 0))
            if s:
                ann = '   ; "%s"' % s
                break
        lines.append("%08X  %-26s %-9s %s%s"
                     % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str, ann))
    return lines


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    if sys.argv[1] == "arm":
        census = json.load(open(os.path.join(OUTDIR, "census.json")))
        ident = sys.argv[2]
        h = int(census["real"][ident], 16)
        print("CM %s arm @ %08X" % (ident, h))
        print("\n".join(dump(h, int(sys.argv[3]) if len(sys.argv) > 3 else 200, True)))
        return
    va = int(sys.argv[1], 16)
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 400
    print("--- %08X ---" % va)
    print("\n".join(dump(va, n)))


main()
