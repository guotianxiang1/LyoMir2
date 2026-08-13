#!/usr/bin/env python
# -*- coding: utf-8 -*-
import lib
from lib import p, BASE, data, md

print("=== TRing [vmt+0x5C] = 0x75F728 (what sub_75EE04 calls) ===")
p(0x75F728, 0xE0)
print()
print("=== every `call dword ptr [reg+0x54]` in 0x750000..0x790000 ===")
d = data()
for i in range(0x750000 - BASE, 0x790000 - BASE):
    if d[i] == 0xFF and (d[i + 1] & 0xF8) == 0x50 and d[i + 2] == 0x54:
        ins = next(md().disasm(d[i:i + 8], i + BASE), None)
        if ins:
            print("   0x%X  %s %s" % (ins.address, ins.mnemonic, ins.op_str))
