"""Multi-encoding literal probe against the M2Server flat image."""
import sys

IMG = r'D:\loym2\staging\_reunpack_work\flat_image.bin'
BASE = 0x400000
with open(IMG, 'rb') as f:
    DATA = f.read()
LOWER = DATA.lower()


def probe(text):
    rows = []
    enc = [
        ('ascii', text.encode('latin-1', 'ignore')),
        ('ascii-ci', text.encode('latin-1', 'ignore').lower()),
        ('utf16le', text.encode('utf-16-le')),
    ]
    try:
        enc.append(('gbk', text.encode('gbk')))
    except Exception:
        pass
    for name, needle in enc:
        if not needle:
            continue
        hay = LOWER if name == 'ascii-ci' else DATA
        hits, start = [], 0
        while True:
            i = hay.find(needle, start)
            if i < 0:
                break
            hits.append(BASE + i)
            start = i + 1
            if len(hits) > 12:
                break
        rows.append((name, hits))
    return rows


if __name__ == '__main__':
    for text in sys.argv[1:]:
        print('=== %r ===' % text)
        for name, hits in probe(text):
            print('  %-9s %d  %s' % (name, len(hits),
                                     ' '.join('%08X' % h for h in hits)))
