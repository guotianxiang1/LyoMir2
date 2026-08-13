"""Dump a Delphi AnsiString / raw bytes at a VA of the M2Server flat image.

Delphi AnsiString literals in .rdata carry a -8 refcount dword and a -4 length
dword, so print both the header and the GBK decoding of the payload.
"""
import sys

IMG = r'D:\loym2\staging\_reunpack_work\flat_image.bin'
BASE = 0x400000
with open(IMG, 'rb') as f:
    DATA = f.read()


def rd(va, n):
    o = va - BASE
    return DATA[o:o + n]


def show(va):
    hdr = rd(va - 8, 8)
    refcnt = int.from_bytes(hdr[0:4], 'little', signed=True)
    length = int.from_bytes(hdr[4:8], 'little', signed=True)
    print('=== VA %08X ===' % va)
    print('  header  refcnt=%d len=%d' % (refcnt, length))
    if 0 < length < 512:
        payload = rd(va, length)
        print('  bytes   %s' % ' '.join('%02X' % b for b in payload))
        for enc in ('gbk', 'ascii'):
            try:
                print('  %-6s  %s' % (enc, payload.decode(enc)))
            except Exception as exc:
                print('  %-6s  <%s>' % (enc, exc))
    raw = rd(va, 48)
    print('  raw48   %s' % ' '.join('%02X' % b for b in raw))


if __name__ == '__main__':
    for arg in sys.argv[1:]:
        show(int(arg, 16))
        print()
