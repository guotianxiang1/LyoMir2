# -*- coding: utf-8 -*-
"""Locate the periodic driver behind 全局循环函数 / 循环时间_值 / MyTimer.

Plugin base 0x10000000 (unpacked dump). Read-only; capstone via yslib.
Writes w1_out.txt next to this file.
"""
import sys, io, os
sys.path.insert(0, r'D:\loym2\staging\_ysimpl')
from yslib import Img, va, off, dis, fmt, func_start

HERE = os.path.dirname(os.path.abspath(__file__))
out = io.open(os.path.join(HERE, 'w1_out.txt'), 'w', encoding='utf-8')
def P(*a):
    print(*a, file=out)

img = Img()

# ---- 1. config-key strings + .text xrefs -----------------------------------
KEYS = ['全局循环函数', '循环时间_值', '循环时间', '自定义循环函数',
        '高级回收', '眼神特殊函数']
P('===== config key strings + abs xrefs =====')
for k in KEYS:
    vas = img.find_str(k)
    P(f'\n== {k!r} @ {[hex(v) for v in vas]}')
    for sv in vas:
        xr = img.xref_abs(sv)
        P(f'   xrefs -> {[hex(x) for x in xr]}')

# ---- 2. parse routine windows (store into fields + defaults) ----------------
P('\n\n===== parse windows =====')
for site in [0x100d7813, 0x100d7d89, 0x100d7da1]:
    fs = func_start(img, site) or (site - 0x100)
    P(f'\n----- around {site:#x} (func_start {fs:#x}) -----')
    for ins in dis(img, site - 0x40, 60):
        line = fmt(ins, img)
        if ins.address == site:
            line += '   <<<<< XREF SITE'
        P(line)
        if ins.address > site + 0x60:
            break

# ---- 3. every use-site of [X+0x938] (循环时间_值 period) ---------------------
USE_938 = [0x10002086, 0x10092207, 0x100d817d, 0x100d86b0,
           0x100da553, 0x100da5d0, 0x100dbe9c]
P('\n\n===== [X+0x938] use sites (period reads) =====')
for site in USE_938:
    P(f'\n----- around {site:#x} -----')
    for ins in dis(img, site - 0x1e, 34):
        line = fmt(ins, img)
        if ins.address == site:
            line += '   <<<<< 0x938 SITE'
        P(line)

out.close()
print('written w1_out.txt')
