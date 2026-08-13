#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""The equipment aggregate: who feeds container+0x48 (0x1B0) and container+0x1F8 (0x36)."""
import lib
from lib import p, callers, funcstart, show_refs

print("=== sub_75EE78 (aggregate rebuild) ===")
p(0x75EE78, 0xC0)
print()
print("=== sub_75EE04 ===")
p(0x75EE04, 0x80)
print()
print("=== sub_75FE20 head ===")
p(0x75FE20, 0xA0)
print()
print("=== callers of sub_75EE78 ===")
for a in callers(0x75EE78):
    print("   0x%X  (func 0x%X)" % (a, funcstart(a) or 0))
print()
print("=== 0x762090 .. 0x762100 (attr dispatch head) ===")
p(0x762090, 0x80)
print()
print("=== function start containing 0x7620DA ===")
fs = funcstart(0x7620DA)
print("   0x%X" % (fs or 0))
p(fs, 0x60)
print()
print("=== callers of that function ===")
for a in callers(fs):
    print("   0x%X  (func 0x%X)" % (a, funcstart(a) or 0))
