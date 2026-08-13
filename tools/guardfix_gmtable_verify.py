# guardfix: verify the transcribed GM name table against the image, entry by entry.
import re

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
REG = r"D:\loym2\.claude\wt3\guardfix\GameSvr\Command\NativeGmCommandRegistry.cs"
BASE = 0x400000
buf = open(IMG, "rb").read()
def rd(va, n): return buf[va - BASE: va - BASE + n]

# Anchor: index 88 sits at 0x7BB254 (verified: ShortString 'GM前撞'), stride 0x120.
ANCHOR_IDX, ANCHOR_VA, STRIDE = 88, 0x7BB254, 0x120
def slot_of(idx): return ANCHOR_VA + (idx - ANCHOR_IDX) * STRIDE

entries = []
for line in open(REG, encoding="utf-8"):
    m = re.match(r'\s*\[(\d+)\]\s*=\s*"([^"]*)"', line)
    if m:
        entries.append((int(m.group(1)), m.group(2)))

ok = bad = 0
mismatches = []
for idx, name in entries:
    va = slot_of(idx)
    if not (BASE <= va < BASE + len(buf) - 0x20):
        continue
    raw = rd(va, 0x20)
    got = raw[1:1 + raw[0]]
    try:
        got_s = got.decode("gbk")
    except UnicodeDecodeError:
        got_s = repr(got)
    if got_s == name:
        ok += 1
    else:
        bad += 1
        if len(mismatches) < 12:
            mismatches.append((idx, name, got_s, hex(va)))

print(f"registry entries parsed: {len(entries)}")
print(f"match image: {ok}    mismatch: {bad}")
for idx, want, got, va in mismatches:
    print(f"  idx {idx} @{va}: registry={want!r} image={got!r}")

print()
for idx in (88, 95, 96, 97):
    va = slot_of(idx)
    raw = rd(va, 0x20)
    print(f"  idx {idx} @0x{va:X}: len={raw[0]} {raw[1:1+raw[0]]!r}")
