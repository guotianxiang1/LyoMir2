#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Census of every GetUseItems(container, slot) call site (0x75EC20).

For each site recover the slot selector (dl) by walking back a few
instructions, and report whether the enclosing function also writes item Dura
(+0x26).  This answers "which equipment slots are read where, and which of
those paths damage durability".
"""
import sys, struct, bisect
from capstone import *
from capstone.x86 import *
from vmt import Vmts, IMG, BASE

GETUSEITEMS = 0x75EC20

SLOTNAME = {0: 'U_DRESS', 1: 'U_WEAPON', 2: 'U_RIGHTHAND', 3: 'U_NECKLACE',
            4: 'U_HELMET', 5: 'U_ARMRING_L', 6: 'U_ARMRING_R', 7: 'U_RING_L',
            8: 'U_RING_R', 9: 'U_BUJUK', 10: 'U_BELT', 11: 'U_BOOTS',
            12: 'U_CHARM', 13: 'U_HELMET2(?)', 14: 'U_14', 15: 'U_15'}

def main():
    data = open(IMG, 'rb').read()
    n = len(data)
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    v = Vmts(data)

    entries = set()
    i = 0
    while True:
        i = data.find(b'\xe8', i)
        if i < 0 or i + 5 > n:
            break
        rel = struct.unpack('<i', data[i + 1:i + 5])[0]
        t = i + 5 + rel
        if 0 <= t < n:
            entries.add(BASE + t)
        i += 1
    vmt_slot = {}
    for va, info in v.by_va.items():
        for k in range(0, 200):
            p = v.d32(va + k * 4)
            if p is None or not (0x401000 <= p < 0x7F0000):
                break
            entries.add(p)
            vmt_slot.setdefault(p, []).append((info['name'], k))
    ents = sorted(entries)

    # call sites
    sites = []
    i = 0
    while True:
        i = data.find(b'\xe8', i)
        if i < 0 or i + 5 > n:
            break
        rel = struct.unpack('<i', data[i + 1:i + 5])[0]
        if i + 5 + rel == GETUSEITEMS - BASE:
            sites.append(BASE + i)
        i += 1
    sys.stderr.write('GetUseItems call sites: %d\n' % len(sites))

    for site in sites:
        j = bisect.bisect_right(ents, site) - 1
        fn = ents[j] if j >= 0 else 0
        nxt = ents[j + 1] if j + 1 < len(ents) else fn + 0x400
        # walk the function, remember the last dl/dx/edx assignment before the call
        sel = '?'
        o = fn - BASE
        last = None
        while BASE + o < site:
            k = next(md.disasm(data[o:o + 16], BASE + o, count=1), None)
            if k is None:
                o += 1
                continue
            if k.mnemonic in ('mov', 'movzx', 'xor') and k.operands:
                d = k.operands[0]
                if d.type == X86_OP_REG and k.reg_name(d.reg) in ('dl', 'dx', 'edx'):
                    src = k.operands[1]
                    if k.mnemonic == 'xor' and src.type == X86_OP_REG and src.reg == d.reg:
                        last = 0
                    elif src.type == X86_OP_IMM:
                        last = src.imm & 0xFF
                    else:
                        last = 'reg:' + k.op_str.split(',')[1].strip()
            o += k.size
        sel = last
        # does the enclosing function write +0x26 ?
        wr26 = False
        o = fn - BASE
        end = min(o + 0x900, nxt - BASE + 0x60, n)
        while o < end:
            k = next(md.disasm(data[o:o + 16], BASE + o, count=1), None)
            if k is None:
                o += 1
                continue
            for op in k.operands:
                if (op.type == X86_OP_MEM and op.mem.base and op.mem.disp == 0x26
                        and op.size == 2 and (op.access & CS_AC_WRITE)):
                    wr26 = True
            o += k.size
        own = vmt_slot.get(fn)
        ownt = ' '.join('%s#%d' % (a, b) for a, b in (own or [])[:2])
        sname = SLOTNAME.get(sel, '') if isinstance(sel, int) else 'VARIABLE'
        print('%08X fn=%08X slot=%-16s %-9s %-38s' % (
            site, fn, ('%s' % sel), ('DURA-WR' if wr26 else ''), sname + ' ' + ownt))

if __name__ == '__main__':
    main()
