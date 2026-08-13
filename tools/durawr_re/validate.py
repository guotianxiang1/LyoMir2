#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Stage 2: keep only census hits that sit on a real instruction boundary.

Boundary set is built by linear-sweeping forward from every guaranteed
function entry until the decode fails or padding is hit.  Delphi code is dense
and contiguous so this recovers the whole .text with self-correcting alignment.

Seeds must include BOTH `call/jmp rel32` targets AND every VMT virtual-method
slot: a method that is only ever reached through the VMT (e.g. TLuckOil.vslot6
at 0x785894, which holds `dec word [eax+0x26]` @0x7858E6) is never a rel32
target, so a rel32-only seed set silently drops its whole body from the census.
"""
import sys, json, struct, bisect
from capstone import *
from capstone.x86 import *
from vmt import Vmts

IMG = r'D:\loym2\staging\_reunpack_work\flat_image.bin'
BASE = 0x400000

def main():
    data = open(IMG, 'rb').read()
    n = len(data)
    md = Cs(CS_ARCH_X86, CS_MODE_32)

    # ---- seeds: call rel32 targets + jmp rel32 targets ----
    seeds = set()
    for op in (b'\xe8', b'\xe9'):
        i = 0
        while True:
            i = data.find(op, i)
            if i < 0 or i + 5 > n:
                break
            rel = struct.unpack('<i', data[i + 1:i + 5])[0]
            t = i + 5 + rel
            if 0 <= t < n:
                seeds.add(t)
            i += 1
    rel32_seeds = len(seeds)
    v = Vmts(data)
    for va in v.by_va:
        for k in range(0, 200):
            p = v.d32(va + k * 4)
            if p is None or not (0x401000 <= p < 0x7F0000):
                break
            seeds.add(p - BASE)
    sys.stderr.write('seeds: %d (rel32 %d, +vmt %d)\n'
                     % (len(seeds), rel32_seeds, len(seeds) - rel32_seeds))

    valid = set()
    for s in sorted(seeds):
        off = s
        if off in valid:
            continue
        steps = 0
        while off < n and steps < 20000:
            if off in valid:
                break
            ins = next(md.disasm(data[off:off + 16], BASE + off, count=1), None)
            if ins is None:
                break
            valid.add(off)
            if ins.mnemonic in ('int3', 'hlt'):
                break
            off += ins.size
            steps += 1
    sys.stderr.write('valid instruction boundaries: %d\n' % len(valid))

    d = json.load(open(sys.argv[1]))
    keep = [v for v in d['writes'] if (v['va'] - BASE) in valid]
    sys.stderr.write('write hits on valid boundaries: %d\n' % len(keep))
    word = [v for v in keep if v['opsize'] == 2]
    sys.stderr.write('  of which WORD-width: %d\n' % len(word))
    json.dump(dict(writes=keep), open(sys.argv[2], 'w'), indent=1)
    for v in sorted(keep, key=lambda x: x['va']):
        print('%08X  sz%-2d %-40s func=%08X+0x%-5x %s' % (
            v['va'], v['opsize'], v['text'], v['func'] or 0,
            v['func_delta'] or 0, v['bytes']))

if __name__ == '__main__':
    main()
