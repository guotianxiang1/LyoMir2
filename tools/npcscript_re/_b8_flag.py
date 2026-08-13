import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, dstr

for s, n in ((0x776C2C, 9), (0x776C40, 10), (0x776C54, 7)):
    b = dstr(s)
    raw = DATA[s - BASE:s - BASE + n]
    print("0x%06X declared_len=%d  delphi_len=%s  raw=%r  gbk=%s" % (
        s, n, len(b) if b else None, raw, raw.decode("gbk", "replace")))
