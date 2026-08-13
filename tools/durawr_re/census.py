#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""DURA-37..44: exhaustive census of writes to [reg+0x26] in the M2Server flat image.

Method: 0x26 can only be a disp8 if it directly follows the ModRM (or SIB) byte.
So for every occurrence of byte 0x26 we retry decoding from the 6 preceding
offsets and keep decodes whose memory operand really has disp == 0x26.
Also handles disp32 == 0x00000026.
"""
import sys, json, struct
from capstone import *
from capstone.x86 import *

IMG = r'D:\loym2\staging\_reunpack_work\flat_image.bin'
BASE = 0x400000

def load():
    with open(IMG, 'rb') as f:
        return f.read()

def make_md():
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    return md

def scan(data, md, disp_val=0x26):
    """Return list of dicts for every instruction touching a memory operand
    with displacement == disp_val."""
    hits = {}
    n = len(data)
    # ---- disp8 form ----
    start = 0
    positions = []
    while True:
        i = data.find(bytes([disp_val]), start)
        if i < 0:
            break
        positions.append(i)
        start = i + 1
    sys.stderr.write('disp8 candidate positions: %d\n' % len(positions))
    for i in positions:
        for back in range(1, 8):
            off = i - back
            if off < 0:
                continue
            try:
                insns = list(md.disasm(data[off:off + 16], BASE + off, count=1))
            except Exception:
                continue
            if not insns:
                continue
            ins = insns[0]
            if off + ins.size <= i:
                continue  # does not even cover the disp byte
            for op in ins.operands:
                if op.type != X86_OP_MEM:
                    continue
                if op.mem.disp != disp_val:
                    continue
                if op.mem.base == 0 and op.mem.index == 0:
                    continue  # absolute address, not a struct field
                key = ins.address
                if key in hits and hits[key]['size'] >= ins.size:
                    continue
                hits[key] = mk(ins, op, data, off)
    # ---- disp32 form ----
    pat = struct.pack('<I', disp_val)
    start = 0
    while True:
        i = data.find(pat, start)
        if i < 0:
            break
        start = i + 1
        for back in range(1, 8):
            off = i - back
            if off < 0:
                continue
            try:
                insns = list(md.disasm(data[off:off + 16], BASE + off, count=1))
            except Exception:
                continue
            if not insns:
                continue
            ins = insns[0]
            if off + ins.size < i + 4:
                continue
            for op in ins.operands:
                if op.type != X86_OP_MEM or op.mem.disp != disp_val:
                    continue
                if op.mem.base == 0 and op.mem.index == 0:
                    continue
                key = ins.address
                if key in hits and hits[key]['size'] >= ins.size:
                    continue
                hits[key] = mk(ins, op, data, off)
    return hits

def mk(ins, op, data, off):
    acc = op.access if hasattr(op, 'access') else 0
    return dict(
        va=ins.address,
        size=ins.size,
        bytes=data[off:off + ins.size].hex(),
        text='%s %s' % (ins.mnemonic, ins.op_str),
        mnem=ins.mnemonic,
        opsize=op.size,
        write=bool(acc & CS_AC_WRITE),
        read=bool(acc & CS_AC_READ),
        base=ins.reg_name(op.mem.base) if op.mem.base else None,
        index=ins.reg_name(op.mem.index) if op.mem.index else None,
    )

def call_targets(data, md):
    """Every E8 rel32 target inside the image = a real function entry."""
    tgts = set()
    n = len(data)
    i = 0
    while True:
        i = data.find(b'\xe8', i)
        if i < 0 or i + 5 > n:
            break
        rel = struct.unpack('<i', data[i + 1:i + 5])[0]
        t = i + 5 + rel
        if 0 <= t < n:
            tgts.add(t)
        i += 1
    return tgts

def main():
    data = load()
    md = make_md()
    hits = scan(data, md)
    sys.stderr.write('total insns touching disp 0x26: %d\n' % len(hits))
    writes = {k: v for k, v in hits.items() if v['write']}
    sys.stderr.write('writes: %d\n' % len(writes))
    tg = sorted(call_targets(data, md))
    sys.stderr.write('call targets: %d\n' % len(tg))
    import bisect
    out = []
    for va in sorted(writes):
        v = writes[va]
        off = va - BASE
        j = bisect.bisect_right(tg, off) - 1
        v['func'] = (BASE + tg[j]) if j >= 0 else None
        v['func_delta'] = (off - tg[j]) if j >= 0 else None
        out.append(v)
    with open(sys.argv[1] if len(sys.argv) > 1 else 'dura_writes.json', 'w') as f:
        json.dump(dict(writes=out, all_count=len(hits)), f, indent=1)
    # summary to stdout
    for v in out:
        print('%08X  sz%d  %-42s  func=%s +0x%x  %s' % (
            v['va'], v['opsize'], v['text'],
            ('%08X' % v['func']) if v['func'] else '????',
            v['func_delta'] or 0, v['bytes']))

if __name__ == '__main__':
    main()
