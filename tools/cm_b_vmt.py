"""Find VMT-like runs of code pointers and report slots 0x250 / 0x254 / 0xD4 / 0xD8.

A Delphi VMT is a long run of dwords pointing into CODE. We locate runs and
report the requested slots so the send-slot method can be disassembled.
"""
import struct
import sys

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0x7A10D0
data = open(IMG, "rb").read()
n = len(data) // 4
words = struct.unpack("<%dI" % n, data[:n * 4])


def is_code(v):
    return CODE_LO <= v <= CODE_HI


runs = []
i = 0
while i < n:
    if is_code(words[i]):
        j = i
        while j < n and is_code(words[j]):
            j += 1
        if (j - i) * 4 >= 0x260:
            runs.append((BASE + i * 4, j - i))
        i = j
    else:
        i += 1

sys.stdout.reconfigure(encoding="utf-8")
print("candidate VMT runs (>=0x260 bytes of consecutive code pointers): %d"
      % len(runs))
want = [0xD4, 0xD8, 0x250, 0x254]
seen = {}
for va, cnt in runs:
    row = []
    ok = True
    for w in want:
        idx = (va - BASE) // 4 + w // 4
        if idx >= n:
            ok = False
            break
        row.append(words[idx])
    if not ok:
        continue
    key = tuple(row)
    seen.setdefault(key, []).append((va, cnt))

for key, vas in sorted(seen.items(), key=lambda kv: -len(kv[1])):
    print("  D4=%08X D8=%08X 250=%08X 254=%08X   x%d  first=%08X len=%d"
          % (key[0], key[1], key[2], key[3], len(vas), vas[0][0], vas[0][1]))
