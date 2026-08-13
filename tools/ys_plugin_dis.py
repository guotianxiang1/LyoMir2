#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Disassemble the yanshen 2.0.8 plugin dump.

Dump: staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin
Base: 0x10000000 (flat runtime image, file offset == VA - base).

Usage: python tools/ys_plugin_dis.py VA [count] [-back N]
"""
import sys

DUMP = (r"D:\loym2\staging\yanshen208_strparam_runtime_dump_20260719"
        r"\yanshen2_0_8_dll.memory.bin")
BASE = 0x10000000

sys.stdout.reconfigure(encoding="utf-8")
_buf = open(DUMP, "rb").read()


def main():
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    a = sys.argv[1:]
    va = int(a[0], 16)
    count = int(a[1]) if len(a) > 1 and not a[1].startswith("-") else 20
    back = int(a[a.index("-back") + 1]) if "-back" in a else 0
    start = va - back
    off = start - BASE
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    shown = 0
    for ins in md.disasm(_buf[off:off + back + count * 8 + 48], start):
        print("%s %08x  %-24s %s %s" % (
            ">>" if ins.address == va else "  ", ins.address,
            ins.bytes.hex().upper(), ins.mnemonic, ins.op_str))
        if ins.address >= va:
            shown += 1
            if shown > count:
                break
    return 0


if __name__ == "__main__":
    sys.exit(main())
