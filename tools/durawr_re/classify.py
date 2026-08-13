#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Stage 3: attribute every word-width [reg+0x26] writer to a function/class and
decide whether the base register really is a TBaseItem descendant.

Function entries = call rel32 targets  UNION  every VMT virtual-method slot.
(VMT-only methods are never call targets, which is why a call-target-only index
mis-attributes e.g. 0x788DAD.)

"is item" evidence collected per function:
  +0x28 (DuraMax) touched on any base        -> strong
  call GetUseItems 0x75EC20                  -> equip slot access
  call SetDura/GetDura 0x784584/0x7845A0     -> strong
  +0x1C then +0x?? (pStdItem deref)          -> item
  IsClass against a VMT global of an item    -> item
"""
import sys, json, struct, bisect
from capstone import *
from capstone.x86 import *
from vmt import Vmts, IMG, BASE

ITEM_ROOTS = ('TBaseItem',)

def main():
    data = open(IMG, 'rb').read()
    n = len(data)
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    v = Vmts(data)

    # ---- function entry set ----
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
    sys.stderr.write('function entries: %d (vmt-only methods: %d)\n'
                     % (len(ents), len(vmt_slot)))

    # item class set
    item_classes = set()
    for info in v.by_va.values():
        ch = [info['name']] + v.chain(info['parent'])
        if any(r in ch for r in ITEM_ROOTS):
            item_classes.add(info['va'])
            item_classes.add(info['name'])

    d = json.load(open(sys.argv[1]))
    rows = []
    for w in sorted(d['writes'], key=lambda x: x['va']):
        if w['opsize'] != 2:
            continue
        va = w['va']
        j = bisect.bisect_right(ents, va) - 1
        fn = ents[j] if j >= 0 else 0
        nxt = ents[j + 1] if j + 1 < len(ents) else fn + 0x400
        owner = vmt_slot.get(fn)
        # scan function body for evidence
        ev = set()
        o = fn - BASE
        end = min(o + 0x900, nxt - BASE + 0x40, n)
        seen_dura_max = False
        while o < end:
            ins = next(md.disasm(data[o:o + 16], BASE + o, count=1), None)
            if ins is None:
                o += 1
                continue
            for op in ins.operands:
                if op.type == X86_OP_MEM and op.mem.base and op.mem.disp == 0x28 and op.size == 2:
                    ev.add('+0x28(DuraMax)')
                if op.type == X86_OP_MEM and op.mem.base and op.mem.disp == 0x1C:
                    ev.add('+0x1C(pStdItem)')
            if ins.mnemonic == 'call' and ins.operands and ins.operands[0].type == X86_OP_IMM:
                t = ins.operands[0].imm & 0xFFFFFFFF
                if t == 0x75EC20:
                    ev.add('GetUseItems')
                elif t in (0x784584,):
                    ev.add('SetDura')
                elif t in (0x7845A0, 0x7845A8):
                    ev.add('GetDura')
                elif t == 0x404828:
                    ev.add('IsClass')
            for op in ins.operands:
                if op.type == X86_OP_MEM and op.mem.base == 0 and op.mem.index == 0:
                    p = v.d32(op.mem.disp & 0xFFFFFFFF)
                    if p and p in item_classes:
                        ev.add('IsA:' + v.name_of(p))
            o += ins.size
        rows.append(dict(va=va, text=w['text'], bytes=w['bytes'], fn=fn,
                         owner=owner, ev=sorted(ev)))

    for r in rows:
        own = ''
        if r['owner']:
            own = ' '.join('%s#%d' % (a, b) for a, b in r['owner'][:3])
        print('%08X %-40s fn=%08X %-46s %s' % (
            r['va'], r['text'], r['fn'], own or '-', ','.join(r['ev'])))
    json.dump(rows, open(sys.argv[2], 'w'), indent=1)

if __name__ == '__main__':
    main()
