#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""The 0x36-byte copy at 0x73D63D lands on obj+0x1B0..+0x1E6, which SPANS +0x1D5."""
import lib
from lib import p, show_refs

print("=== sub_73D500 : 0x73D600 .. 0x73D6C0  (the two rep movsd) ===")
p(0x73D600, 0xC0)
print()
print("=== 0x73D520 .. 0x73D560  (prologue: locals + FillChar) ===")
p(0x73D520, 0x50)
print()
print("=== 0x73D8E0 .. 0x73D920  (the third lea per PKD-20) ===")
p(0x73D8E0, 0x40)
print()
print("=== 0x73DEA0 .. 0x73DEE8  (the [+0x1D5] gate) ===")
p(0x73DEA0, 0x48)
print()
print("=== 0x73DAA0 .. 0x73DAD8  ([+0x18C] write) ===")
p(0x73DAA0, 0x38)
