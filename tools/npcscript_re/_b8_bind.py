import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, dstr
from _b8_region import dis2

print("--- strings used by the 0x776342 map-flag arm ---")
for s in (0x776B30, 0x776B3C):
    b = dstr(s)
    print("  0x%06X len=%s  %r  gbk=%s" % (
        s, len(b) if b else None, b, b.decode("gbk", "replace") if b else None))

print()
print("=" * 78)
print("binder 0x77ADD4  (eax=map, edx=name) -> returns the TPsNpc stored into [map+0xA4]")
print("=" * 78)
print(dis2(0x77ADD4, 0x77AF00))
