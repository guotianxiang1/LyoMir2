# -*- coding: utf-8 -*-
"""Disassemble the period-tick function around 0x1008c7e7 and find its callers,
plus how it fires the script loop (MyTimer) and the s(1,127)/s(1,128) cache."""
import sys, io, os
sys.path.insert(0, r'D:\loym2\staging\_ysimpl')
from yslib import Img, va, off, dis, fmt, func_start

HERE = os.path.dirname(os.path.abspath(__file__))
out = io.open(os.path.join(HERE, 'w3_out.txt'), 'w', encoding='utf-8')
def P(*a):
    print(*a, file=out)

img = Img()

site = 0x1008c7e7
fs = func_start(img, site, back=0x800) or (site - 0x200)
P(f'===== tick function: func_start {fs:#x}, site {site:#x} =====')
for ins in dis(img, fs, 400):
    line = fmt(ins, img)
    if ins.address == site:
        line += '   <<<<< PERIOD READ'
    P(line)
    if ins.address > site + 0x260:
        break

# callers of the tick function
P(f'\n\n===== rel32 callers of {fs:#x} =====')
for c in img.xref_call(fs):
    P(f'  caller {c:#x}')
    cfs = func_start(img, c, back=0x400) or (c - 0x40)
    for ins in dis(img, c - 0x30, 20):
        line = fmt(ins, img)
        if ins.address == c:
            line += '   <<< CALL'
        P('    ' + line)
P(f'  abs refs to {fs:#x}: {[hex(x) for x in img.xref_abs(fs)]}')

out.close()
print('written w3_out.txt')
