#!/usr/bin/env python
# -*- coding: utf-8 -*-
import lib
from lib import p, u32, rd

print("=== arms 19 (0x762318) and 24 (0x76236F) ===")
p(0x762300, 0x60)
print()
print("=== the second dispatcher around 0x762B26 / 0x762B6A ===")
p(0x762AF0, 0xA0)
print()
print("=== which function contains 0x762B26 ===")
print("   funcstart 0x%X" % (lib.funcstart(0x762B26) or 0))
p(lib.funcstart(0x762B26), 0x40)
