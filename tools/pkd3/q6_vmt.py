#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Which VMT slot is 0x76203C, and what are the max offsets its 33 arms touch?"""
import lib
from lib import p, u32, rd, dstr, gbk, BASE, data, md

ref = 0x75D924
print("=== dword 0x76203C lives at 0x%X ; probe the VMT around it ===" % ref)
for slot in range(-0x10, 0x10):
    va = ref + slot * 4
    print("   +%04X  0x%X -> 0x%08X" % (slot * 4, va, u32(va)))

print()
print("=== assume it is VMT+0x5C -> vmt base 0x%X ; Delphi class name at vmt-0x38 ===" % (ref - 0x5C))
for cand in (0x5C, 0x58, 0x60, 0x64):
    base = ref - cand
    namep = u32(base - 0x38)
    if 0x400000 < namep < 0x800000:
        n = rd(namep, 1)[0]
        print("   slot +0x%X -> vmt 0x%X  name@0x%X = %r" % (cand, base, namep, rd(namep + 1, n)))

print()
print("=== the 33-arm jump table at 0x762190 ===")
arms = [u32(0x762190 + i * 4) for i in range(33)]
for i, a in enumerate(arms):
    print("   slot %2d -> 0x%X" % (i, a))

print()
print("=== 152-entry type->slot map at 0x7620F8 (type = index + 57) ===")
tbl = rd(0x7620F8, 152)
for i, s in enumerate(tbl):
    if s:
        print("   type %3d (0x%02X) -> slot %d" % (i + 57, i + 57, s))

print()
print("=== arm for slot 27 ===")
p(arms[27], 0x30)
