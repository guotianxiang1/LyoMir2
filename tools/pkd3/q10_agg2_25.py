#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""agg2 = container+0x1F8, 0x36 bytes, copied wholesale to object+0x1B0.
   object+0x1D5 == agg2[0x25].  Who writes agg2[0x25]?"""
import lib
from lib import BASE, data, md, p

d = data()
print("=== stores/reads touching [reg+0x25] in 0x750000..0x7A0000 ===")
for i in range(0x750000 - BASE, 0x7A0000 - BASE):
    ins = next(md().disasm(d[i:i + 10], i + BASE), None)
    if ins is None:
        continue
    if "+ 0x25]" in ins.op_str and ins.mnemonic in (
            "mov", "add", "or", "inc", "sub", "and", "cmp", "test", "movzx", "lea"):
        print("   0x%-8X %-6s %-44s %s" % (ins.address, ins.mnemonic, ins.op_str, ins.bytes.hex().upper()))
