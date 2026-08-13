"""Identify NewFullMailEx callers and mailitem INSERT SQL."""
import sys
sys.stdout.reconfigure(encoding='utf-8')
from m2 import *

OUT = r"D:\loym2\.claude\wt2\m-mailmall\docs\mailmall_re\q23_senders.txt"
f = open(OUT, 'w', encoding='utf-8')

def w(s=''):
    f.write(s + '\n')

def dump(va, n, title):
    w()
    w('=' * 72)
    w(title)
    w('=' * 72)
    show(va, n, f=f)

# walk back to function prologue from each call site
for site in [0x649179, 0x6E7617, 0x708CF8]:
    # search backwards for push ebp / mov ebp,esp
    start = site
    for va in range(site, site - 0x80, -1):
        b = rd(va, 3)
        if b == bytes([0x55, 0x8B, 0xEC]):
            start = va
            break
    dump(start, 40, f'caller containing {site:08X}  (fn guessed {start:08X})')

# INSERT mailitem
pat = b'INSERT INTO %s.mailitem'
hits = find_all(pat)
w()
w(f'INSERT mailitem hits: {[hex(h) for h in hits]}')
for h in hits:
    n = i32(h - 4)
    raw = rd(h, min(n or 200, 300))
    w(f'  @{h:08X} len={n} {gbk(raw)!r}')

pat2 = b'INSERT INTO %s.Money_order'
hits2 = find_all(pat2)
w(f'Money_order hits: {[hex(h) for h in hits2]}')
for h in hits2:
    n = i32(h - 4)
    raw = rd(h, min(n or 200, 400))
    w(f'  @{h:08X} len={n} {gbk(raw)!r}')

# ident 0x1170 = 4464 around mail wrappers
w()
w('mail wrapper idents nearby 0x6E75xx:')
show(0x6E75C0, 30, f=f)

f.close()
print('wrote', OUT)
