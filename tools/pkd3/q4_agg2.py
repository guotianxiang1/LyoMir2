#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""agg2 = container+0x1F8 (0x36 bytes) -> object+0x1B0..+0x1E6.  Who writes agg2+0x25?"""
import lib
from lib import p, callers, funcstart, findall, BASE, data, md

print("=== who references sub_76203C (vmt entry?) ===")
for a in findall((0x76203C).to_bytes(4, "little")):
    print("   dword @0x%X" % a)
print()
print("=== sub_75F878 (the base handler 0x76203C forwards to) ===")
p(0x75F878, 0x260)
