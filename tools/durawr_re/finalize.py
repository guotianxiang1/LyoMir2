#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Stage 5: compact per-writer summary (guard + arithmetic) for the census table."""
import sys, json, struct, bisect
from capstone import *
from capstone.x86 import *
from vmt import Vmts, IMG, BASE

def main():
    data = open(IMG, 'rb').read()
    n = len(data)
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    v = Vmts(data)

    rows = json.load(open(sys.argv[1]))
    for r in rows:
        va, fn = r['va'], r['fn']
        # decode the function linearly, keep a sliding window around the write
        o = fn - BASE
        seq = []
        while BASE + o <= va + 24 and o < n:
            ins = next(md.disasm(data[o:o + 16], BASE + o, count=1), None)
            if ins is None:
                o += 1
                continue
            seq.append((ins.address, '%s %s' % (ins.mnemonic, ins.op_str)))
            o += ins.size
        idx = next((k for k, (a, _) in enumerate(seq) if a == va), None)
        if idx is None:
            ctx = ['<not on linear path from fn entry>']
        else:
            lo = max(0, idx - 5)
            ctx = ['%s%s' % ('>>' if k == idx else '  ', t)
                   for k, (a, t) in enumerate(seq[lo:idx + 3], start=lo)]
        own = ' '.join('%s#%d' % (a, b) for a, b in (r['owner'] or [])[:3]) or '-'
        print('%08X  fn=%08X  %s' % (va, fn, own))
        for c in ctx:
            print('        ' + c)
        print()

if __name__ == '__main__':
    main()
