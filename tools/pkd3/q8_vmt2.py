#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Proper Delphi VMT identification: SelfPtr at vmt-0x4C, ClassName at vmt-0x2C."""
import lib
from lib import p, u32, rd, BASE, data


def find_vmt(addr):
    """walk back for A with u32(A-0x4C) == A"""
    for a in range(addr & ~3, addr - 0x800, -4):
        if u32(a - 0x4C) == a:
            return a
    return None


def cname(vmt):
    ptr = u32(vmt - 0x2C)
    n = rd(ptr, 1)[0]
    return rd(ptr + 1, n).decode("latin1")


for probe in (0x75D924,):
    v = find_vmt(probe)
    print("probe 0x%X -> vmt 0x%X  class %s  slot +0x%X  instsize %d  parentptr 0x%X"
          % (probe, v, cname(v), probe - v, u32(v - 0x28), u32(u32(v - 0x24))))
    print("   slots: ", end="")
    for off in range(0x50, 0x70, 4):
        print("+0x%X=0x%X " % (off, u32(v + off)), end="")
    print()

print()
print("=== the two `call [reg+0x58]` sites ===")
print("--- 0x772FE0 ---")
p(0x772FE0, 0x60)
print("--- 0x78FDE0 ---")
p(0x78FDE0, 0x80)
