"""Send-mail limits, SQL format strings, Looks log string, group counter."""
import sys
sys.stdout.reconfigure(encoding='utf-8')
from m2 import *

OUT = r"D:\loym2\.claude\wt2\m-mailmall\docs\mailmall_re\q22_send_limits.txt"
f = open(OUT, 'w', encoding='utf-8')

def w(s=''):
    f.write(s + '\n')

def dump(va, n, title):
    w()
    w('=' * 72)
    w(title)
    w('=' * 72)
    show(va, n, f=f)

def longstr(va):
    n = i32(va - 4)
    raw = rd(va, n) if n and 0 < n < 4096 else None
    return n, gbk(raw) if raw else None

w('### format / log strings')
for va in [0x709558, 0x63A084, 0x63A08C, 0x70C814, 0x70BE7C, 0x70BE90, 0x70BEAC,
           0x637350, 0x6CB940, 0x6CC768, 0x70B8A8, 0x70B8CC]:
    n, s = longstr(va)
    raw = rd(va, min(n, 200) if n else 64)
    w(f'  @{va:08X} len={n} gbk={s!r} raw={raw[:80] if raw else None}')

w()
w('### xrefs to NewFullMailEx 0x7092D0')
for va, kind in xrefs_call(0x7092D0):
    w(f'  {kind} @{va:08X}')

w()
w('### xrefs to overflow test 0x6D7948')
for va, kind in xrefs_call(0x6D7948)[:20]:
    w(f'  {kind} @{va:08X}')

dump(0x709048, 40, 'sub_709048 item-group counter (called before cmp eax,6)')
dump(0x7090BC, 40, 'sub_7090BC parse itemInfo tokens')
dump(0x70CF34, 40, 'sub_70CF34 set MoneyCount')
dump(0x709476, 50, 'sub_7092D0 continuation after StdMode==7 dura clamp')

# 1101 ident? 0x708 = 1800. Also check nearby callers of 0x639D24
w()
w('### xrefs to Looks filler 0x639D24')
for va, kind in xrefs_call(0x639D24):
    w(f'  {kind} @{va:08X}')

# CM send mail search: ident immediates near mail wrappers 0x6E76A4 etc
dump(0x6E76A4, 25, 'sub_6E76A4 CM_CLEAR_ALLMAIL wrapper')
dump(0x6E7810, 30, 'sub_6E7810 CM_FETCH_ATTACH wrapper')

# search ascii NewFullMail
w()
w('### NewFullMail string hits')
for h in find_all(b'NewFullMail'):
    n, s = longstr(h)
    w(f'  @{h:08X} len={n} {s!r}')

f.close()
print('wrote', OUT)
