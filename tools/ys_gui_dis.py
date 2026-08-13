#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Disassemble M2Server around the VAs the yanshen plugin patches.

Evidence base: D:/loym2/staging/_reunpack_work/flat_image.bin, ImageBase 0x400000.
Patch atlas:   D:/loym2/staging/_ysgui2/g09.json (memcpy sites: va/len/bytes/orig)
               D:/loym2/staging/_ysgui2/g11.json (immediate stores: target/width)

Usage: python tools/ys_gui_dis.py VA [count] [-back N]
       python tools/ys_gui_dis.py --key 免毒符
"""
import json
import re
import sys
import collections

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
G09 = r"D:\loym2\staging\_ysgui2\g09.json"
G11 = r"D:\loym2\staging\_ysgui2\g11.json"
SUF = re.compile(r"\((已启动|未启动|已启用|未启用|已关闭|未关闭|已设置|未设置|已重设|待重设|改用新版)\)$")

sys.stdout.reconfigure(encoding="utf-8")
_buf = open(IMG, "rb").read()


def rd(va, n):
    off = va - BASE
    return _buf[off:off + n]


def dis(va, count=12, back=0):
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    start = va - back
    data = rd(start, back + count * 8 + 32)
    out = []
    for ins in md.disasm(data, start):
        mark = ">>" if ins.address == va else "  "
        out.append("%s %08x  %-22s %s %s" % (
            mark, ins.address, ins.bytes.hex().upper(), ins.mnemonic, ins.op_str))
        if ins.address >= va and len(out) > count + (1 if back else 0):
            break
    return out


def atlas():
    by = collections.defaultdict(list)
    for r in json.load(open(G09, encoding="utf-8")):
        lb = SUF.sub("", (r.get("label") or "")).strip()
        if lb:
            by[lb].append(r)
    return by


def main():
    a = sys.argv[1:]
    if a and a[0] == "--key":
        by = atlas()
        for row in by.get(a[1], []):
            print("-" * 72)
            print("label=%s va=%#x len=%d src=%s" % (row["label"], row["va"], row["len"], row["src"]))
            print("  new =%s" % row["bytes"])
            print("  orig=%s  (image=%s)" % (row["orig"], rd(row["va"], row["len"]).hex().upper()))
            for l in dis(row["va"], 8, 16):
                print(l)
        return 0
    va = int(a[0], 16)
    count = int(a[1]) if len(a) > 1 else 12
    back = int(a[a.index("-back") + 1]) if "-back" in a else 0
    for l in dis(va, count, back):
        print(l)
    return 0


if __name__ == "__main__":
    sys.exit(main())
