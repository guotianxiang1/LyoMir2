"""Census direct E8/E9 relative callers of a target VA in the M2Server flat image."""
import sys

IMG = r'D:\loym2\staging\_reunpack_work\flat_image.bin'
BASE = 0x400000
with open(IMG, 'rb') as f:
    DATA = f.read()


def scan(target):
    hits = []
    n = len(DATA)
    for off in range(0, n - 5):
        b = DATA[off]
        if b != 0xE8 and b != 0xE9:
            continue
        rel = int.from_bytes(DATA[off + 1:off + 5], 'little', signed=True)
        site = BASE + off
        if site + 5 + rel == target:
            hits.append((site, 'call' if b == 0xE8 else 'jmp'))
    return hits


if __name__ == '__main__':
    for arg in sys.argv[1:]:
        t = int(arg, 16)
        hits = scan(t)
        print('=== target %08X : %d hits ===' % (t, len(hits)))
        for site, kind in hits:
            print('  %08X  %s' % (site, kind))
