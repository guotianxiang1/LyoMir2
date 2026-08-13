"""Attribute an address to its enclosing routine.

Builds the set of every E8 call target in the image, then reports the greatest
call target at or below each queried address.  A routine that is only reached
through a VMT slot has no E8 target, so the answer is a lower bound; the
`55 8B EC` prologue check flags whether the hit really looks like an entry.
"""
import sys

IMG = r'D:\loym2\staging\_reunpack_work\flat_image.bin'
BASE = 0x400000
with open(IMG, 'rb') as f:
    DATA = f.read()


def call_targets():
    ts = set()
    n = len(DATA)
    for off in range(0, n - 5):
        if DATA[off] != 0xE8:
            continue
        rel = int.from_bytes(DATA[off + 1:off + 5], 'little', signed=True)
        t = BASE + off + 5 + rel
        if BASE <= t < BASE + n:
            ts.add(t)
    return sorted(ts)


def prologue(va):
    o = va - BASE
    b = DATA[o:o + 3]
    if b[:3] == b'\x55\x8b\xec':
        return 'push ebp; mov ebp,esp'
    if b[:1] == b'\x53':
        return 'push ebx'
    return ' '.join('%02X' % x for x in b)


if __name__ == '__main__':
    import bisect
    ts = call_targets()
    print('total distinct E8 targets: %d' % len(ts))
    for arg in sys.argv[1:]:
        va = int(arg, 16)
        i = bisect.bisect_right(ts, va) - 1
        for k in range(max(0, i - 2), i + 1):
            tag = '<<' if k == i else '  '
            print('%s %08X owner-candidate for %08X  (+0x%X)  [%s]'
                  % (tag, ts[k], va, va - ts[k], prologue(ts[k])))
        print()
