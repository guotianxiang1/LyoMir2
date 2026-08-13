#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Stage 4: separate genuine item-Dura writes from TAbility(+0x26 = MAC.max)
false positives, then dump provenance for the survivors.

TAbility layout proven at TEquipItem.vslot12 (0x75FB14):
    [ab+0x22] += item[+0x2A]   AC.max
    [ab+0x26] += item[+0x2B]   MAC.max   <-- collides with item Dura offset
    [ab+0x2A] += item[+0x2C]   DC.max
    [ab+0x2E] += item[+0x2D]   MC.max
    [ab+0x32] += item[+0x2E]   SC.max
So: a write whose base also carries word accesses at 0x22 AND 0x2A (and often
0x2E/0x32) in the same function is an ability write, not a durability write.
"""
import sys, json, struct, bisect
from capstone import *
from capstone.x86 import *
from vmt import Vmts, IMG, BASE

ABILITY_SIBS = (0x22, 0x2A, 0x2E, 0x32)

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

    rows = json.load(open(sys.argv[1]))
    out = []
    for r in rows:
        va, fn = r['va'], r['fn']
        j = bisect.bisect_right(ents, va) - 1
        nxt = ents[j + 1] if j + 1 < len(ents) else fn + 0x400
        # our write's base register name
        wbase = None
        o = va - BASE
        ins = next(md.disasm(data[o:o + 16], va, count=1), None)
        for op in ins.operands:
            if op.type == X86_OP_MEM and op.mem.disp == 0x26:
                wbase = op.mem.base
        # walk function, collect disps seen with the same base register
        sibs, dura_sibs = set(), set()
        o = fn - BASE
        end = min(o + 0x900, nxt - BASE + 0x60, n)
        while o < end:
            k = next(md.disasm(data[o:o + 16], BASE + o, count=1), None)
            if k is None:
                o += 1
                continue
            for op in k.operands:
                if op.type == X86_OP_MEM and op.mem.base == wbase and op.size == 2:
                    if op.mem.disp in ABILITY_SIBS:
                        sibs.add(op.mem.disp)
                    if op.mem.disp == 0x28:
                        dura_sibs.add(0x28)
            o += k.size
        verdict = 'ABILITY' if len(sibs) >= 2 else 'DURA?'
        r2 = dict(r)
        r2['sibs'] = sorted(sibs)
        r2['has28'] = sorted(dura_sibs)
        r2['verdict'] = verdict
        out.append(r2)

    ab = [r for r in out if r['verdict'] == 'ABILITY']
    du = [r for r in out if r['verdict'] != 'ABILITY']
    sys.stderr.write('ability(false-positive): %d   candidate dura: %d\n' % (len(ab), len(du)))
    print('### ABILITY (TAbility MAC.max, NOT durability) - %d' % len(ab))
    for r in ab:
        print('  %08X %-38s fn=%08X sibs=%s' % (r['va'], r['text'], r['fn'],
                                                [hex(x) for x in r['sibs']]))
    print()
    print('### CANDIDATE ITEM DURA WRITES - %d' % len(du))
    for r in du:
        own = ' '.join('%s#%d' % (a, b) for a, b in (r['owner'] or [])[:3])
        print('  %08X %-38s fn=%08X %-44s ev=%s' % (
            r['va'], r['text'], r['fn'], own or '-', ','.join(r['ev'])))
    json.dump(du, open(sys.argv[2], 'w'), indent=1)

if __name__ == '__main__':
    main()
