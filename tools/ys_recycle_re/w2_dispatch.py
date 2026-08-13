# -*- coding: utf-8 -*-
"""Find who periodically calls MyTimer, and how 全局循环函数(+0x93c) /
循环时间_值(+0x938) install the driver.

Plugin base 0x10000000. Also scans M2Server flat_image (base 0x400000) for a
'MyTimer' / periodic script-call mechanism.
"""
import sys, io, os, struct
sys.path.insert(0, r'D:\loym2\staging\_ysimpl')
from yslib import Img, va, off, dis, fmt, func_start

HERE = os.path.dirname(os.path.abspath(__file__))
out = io.open(os.path.join(HERE, 'w2_out.txt'), 'w', encoding='utf-8')
def P(*a):
    print(*a, file=out)

img = Img()
b = img.buf

# ---- 1. status/init routine containing 0x10092207 (from func start) --------
site = 0x10092207
fs = func_start(img, site, back=0xC00) or (site - 0x400)
P(f'===== full routine around {site:#x} (func_start {fs:#x}) =====')
for ins in dis(img, fs, 320):
    line = fmt(ins, img)
    if ins.address in (0x10092205, 0x10092255, 0x1009220b, 0x10092212):
        line += '   <<<'
    P(line)
    if ins.address > site + 0x120:
        break

# ---- 2. abs xrefs to config fields +0x93c (全局循环函数) and +0x938 --------
def scan_disp(disp):
    """find .text bytes matching a mov/lea/cmp with disp32 == disp"""
    hits = []
    from ysdump import TEXT_RVA, TEXT_SIZE
    needle = struct.pack('<I', disp)
    i = TEXT_RVA
    hi = TEXT_RVA + TEXT_SIZE
    while True:
        j = b.find(needle, i, hi)
        if j < 0:
            break
        hits.append(va(j))
        i = j + 1
    return hits

for disp in (0x93c, 0x938):
    P(f'\n\n===== disp {disp:#x} raw hits in .text =====')
    for h in scan_disp(disp):
        P(f'  {h:#x}  ctx={b[off(h)-3:off(h)+4].hex()}')

# ---- 3. pointer 0x1031c1fc (= &循环时间_值) xrefs --------------------------
P('\n\n===== xrefs to cached period pointer 0x1031c1fc =====')
for x in img.xref_abs(0x1031c1fc):
    P(f'  {x:#x}')
    for ins in dis(img, x - 6, 10):
        P('    ' + fmt(ins, img))

# ---- 4. @MyTimer trampoline: find where @MyTimer string is referenced -------
P('\n\n===== @MyTimer occurrences =====')
for enc in (b'@MyTimer', b'MyTimer'):
    for h in img.find_all(enc):
        P(f'  {enc} @ {va(h):#x} prev={b[off(va(h))-1]:#x} ctx={b[off(va(h))-2:off(va(h))+12]!r}')

# ---- 5. M2Server side: search flat_image for 'MyTimer' + '循环时间' ---------
M2 = open(r'D:\loym2\staging\_reunpack_work\flat_image.bin', 'rb').read()
M2BASE = 0x400000
P('\n\n===== M2Server flat_image string hits =====')
for s in [b'MyTimer', '循环时间'.encode('gbk'), '全局循环'.encode('gbk'),
          '定时'.encode('gbk'), b'RunQuest', b'@Main', b'@_Main']:
    idxs = []
    i = M2.find(s)
    while i >= 0 and len(idxs) < 8:
        idxs.append(M2BASE + i)
        i = M2.find(s, i + 1)
    P(f'  {s!r} -> {[hex(x) for x in idxs]}')

out.close()
print('written w2_out.txt')
