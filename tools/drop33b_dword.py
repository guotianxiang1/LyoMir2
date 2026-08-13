"""Find absolute dword references to a VA (VMT slots, jump tables)."""
import sys

IMG = r'D:\loym2\staging\_reunpack_work\flat_image.bin'
BASE = 0x400000
with open(IMG, 'rb') as f:
    DATA = f.read()

if __name__ == '__main__':
    for arg in sys.argv[1:]:
        va = int(arg, 16)
        needle = va.to_bytes(4, 'little')
        hits, start = [], 0
        while True:
            i = DATA.find(needle, start)
            if i < 0:
                break
            hits.append(BASE + i)
            start = i + 1
            if len(hits) > 200:
                break
        print('=== dword %08X : %d hits ===' % (va, len(hits)))
        print('  ' + ' '.join('%08X' % h for h in hits[:60]))
