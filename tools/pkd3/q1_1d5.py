#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Hunt every writer that can reach [obj+0x1D5]."""
import lib
from lib import show_refs, p, findall, BASE, data, md

# 1. direct disp32 refs to 0x1D5 anywhere in the image (not just the code window)
show_refs(0x1D5, "direct", lo=0x400000, hi=0x1000000)

# 2. neighbours: who else lives around here?
for off in range(0x1CC, 0x1E4):
    r = lib.refs_disp(off, 0x401000, 0x7A0000)
    if r:
        print("--- +0x%X : %d" % (off, len(r)))
        for a, m, o, b in r[:14]:
            print("     0x%-8X %-7s %-44s %s" % (a, m, o, b))

# 3. is there a bulk copy landing on the object that spans +0x1D5?
#    look for `rep movsb/movsd` with a preceding `lea edi,[reg+disp]` where disp <= 0x1D5
d = data()
print("\n=== lea edi,[reg+disp] with disp in 0x100..0x1D5, followed within 32 bytes by rep movs ===")
hits = 0
for i in range(0x1000, len(d) - 40):
    if d[i] != 0x8D:  # lea
        continue
    ins = next(md().disasm(d[i:i + 8], i + BASE), None)
    if ins is None or ins.mnemonic != "lea" or not ins.op_str.startswith("edi,"):
        continue
    # find rep movs in next 40 bytes
    w = d[i:i + 48]
    if b"\xf3\xa5" in w or b"\xf3\xa4" in w:
        opnd = ins.op_str
        if "0x1" in opnd or "0x2" in opnd:
            print("  0x%-8X %s %s   [%s]" % (ins.address, ins.mnemonic, opnd, ins.bytes.hex().upper()))
            hits += 1
            if hits > 60:
                break
