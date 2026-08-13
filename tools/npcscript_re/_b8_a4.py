import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, md
from _b8_region import dis2
import re

print("=" * 78)
print("A) writes to [reg+0xA4]  (mov [reg+0xA4], reg32)  -- QuestNPC binding")
print("=" * 78)
# 89 /r with disp32 0xA4: modrm = 10 rrr mmm  -> 0x80..0xBF
for m in re.finditer(rb"\x89[\x80-\xBF]\xA4\x00\x00\x00", DATA):
    va = BASE + m.start()
    print("\n---- 0x%06X" % va)
    print(dis2(va - 0x20, va + 0x10))

print()
print("=" * 78)
print("B) 'mov [reg+0xA4], 0' style (C7 /0 disp32 imm32)")
print("=" * 78)
for m in re.finditer(rb"\xC7[\x80-\x87]\xA4\x00\x00\x00", DATA):
    va = BASE + m.start()
    print("\n---- 0x%06X" % va)
    print(dis2(va - 0x18, va + 0x14))
