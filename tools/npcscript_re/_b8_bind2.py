import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, dstr
from _b8_region import dis2

for s in (0x77AF40, 0x77AF54, 0x77AF64, 0x776B30, 0x776B3C):
    b = dstr(s)
    print("0x%06X len=%-4s %r   gbk=%s" % (
        s, len(b) if b else None, b, b.decode("gbk", "replace") if b else None))
print("[0x7D6530] global string ptr")

print()
print("=" * 78)
print("map-flag parser arm that binds QuestNPC -- context before 0x776333")
print("=" * 78)
print(dis2(0x7762A0, 0x776360))
