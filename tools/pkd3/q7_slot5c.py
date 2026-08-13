#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""TRing VMT+0x5C = 0x761C30 is what sub_75EE04 actually calls.  Walk it."""
import lib
from lib import p, u32, rd, BASE, data, md

print("=== TRing VMT base 0x75D8CC ; slots ===")
for off in (0x54, 0x58, 0x5C, 0x60):
    print("   +0x%X -> 0x%08X" % (off, u32(0x75D8CC + off)))
print()
print("=== 0x761C30 (the real [vmt+0x5C]) ===")
p(0x761C30, 0xC0)
print()
print("=== every `call dword ptr [reg+0x58]` in 0x750000..0x790000 ===")
d = data()
for i in range(0x750000 - BASE + BASE - BASE, 0):
    pass
lo, hi = 0x750000, 0x790000
for i in range(lo - BASE, hi - BASE):
    if d[i] == 0xFF and (d[i + 1] & 0xF8) == 0x50 and d[i + 2] == 0x58:
        ins = next(md().disasm(d[i:i + 8], i + BASE), None)
        if ins:
            print("   0x%X  %s %s" % (ins.address, ins.mnemonic, ins.op_str))
