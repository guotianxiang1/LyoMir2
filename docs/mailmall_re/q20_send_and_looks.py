"""Independent re-check: mail send, claim IncGold, Looks fallback, field jump table."""
import sys
sys.stdout.reconfigure(encoding='utf-8')
from m2 import *

OUT = r"D:\loym2\.claude\wt2\m-mailmall\docs\mailmall_re\q20_send_and_looks.txt"
f = open(OUT, 'w', encoding='utf-8')

def w(s=''):
    f.write(s + '\n')

def dump(va, n, title):
    w()
    w('=' * 72)
    w(title)
    w('=' * 72)
    show(va, n, f=f)

# --- 1. field jump table at 0x636FD6 ---
w('### 1. shop field jump table 0x636FD6')
for i in range(12):
    va = 0x636FD6 + i * 4
    t = u32(va)
    w(f'  [{i:2d}] @{va:08X} -> {t:08X}' if t else f'  [{i:2d}] @{va:08X} -> None')

dump(0x636D68, 120, 'sub_636D68 SendYBShopConfig / GetYBShopConfig loader (head)')
dump(0x636F80, 80, 'sub_636D68 field-split loop')
dump(0x637000, 80, 'sub_636D68 field arms + validate')
dump(0x637150, 50, 'sub_636D68 180-byte TClientShop fill')

# --- 2. Looks handler sub_639D24 ---
dump(0x639D24, 80, 'sub_639D24 (alleged Looks fill / 1101 handler)')
dump(0x639C58, 40, 'sub_639C58 (1104 groupCount 1..8)')

# --- 3. mail send sub_7092D0 ---
dump(0x7092D0, 120, 'sub_7092D0 NewFullMailEx')
dump(0x70C570, 80, 'sub_70C570 SaveMailItem')
dump(0x70BBFC, 60, 'sub_70BBFC (send helper)')

# --- 4. claim IncGold ---
dump(0x70B664, 90, 'sub_70B664 claim core')
dump(0x70B7B0, 50, 'sub_70B664 gold / IncGold region')
dump(0x6D791C, 20, 'sub_6D791C IncGold')
dump(0x6D7948, 15, 'sub_6D7948 gold overflow test')

# --- 5. delivery loop ---
dump(0x70B458, 50, 'sub_70B458 attachment delivery')

# --- 6. string search ---
w()
w('=' * 72)
w('string search')
w('=' * 72)

needles = [
    'NewFullMailEx', '@ClientBuy', '@GetYBShopConfig', '@GetLimitValue',
    'SendYBShopConfig', 'YBShopBuy_YB',
]
for n in needles:
    b = n.encode('ascii')
    hits = find_all(b)
    w(f'  ASCII {n!r}: {len(hits)} hits: {[hex(h) for h in hits[:8]]}')

# GBK chinese
for s in ['邮件', '附件', '商城', '灵符', '限购', '元宝']:
    b = s.encode('gbk')
    hits = find_all(b)
    w(f'  GBK {s!r}: {len(hits)} hits first={[hex(h) for h in hits[:6]]}')

# Delphi long strings (len prefix)
for s in ['NewFullMailEx', '@ClientBuy', '@GetYBShopConfig']:
    raw = s.encode('ascii')
    # search the string itself, then look at -4 for length
    for h in find_all(raw)[:8]:
        ln = i32(h - 4)
        w(f'    longstr {s!r} @{h:08X} prefix_len={ln}')

f.close()
print('wrote', OUT)
