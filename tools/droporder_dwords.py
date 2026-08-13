"""Dump a dword window of the M2Server flat image, flagging Delphi VMT self-pointers.

Usage: python tools/droporder_dwords.py <VA-hex> [count]
"""
import sys

IMG = r'D:\loym2\staging\_reunpack_work\flat_image.bin'
BASE = 0x400000
with open(IMG, 'rb') as f:
    DATA = f.read()


def dw(va):
    o = va - BASE
    return int.from_bytes(DATA[o:o + 4], 'little')


def main():
    va = int(sys.argv[1], 16)
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 32
    for i in range(n):
        a = va + i * 4
        v = dw(a)
        note = ''
        # Delphi VMT self-check: dword[V-0x4C] == V
        if 0x400000 <= a + 0x4C < BASE + len(DATA) and dw(a + 0x4C) == a + 0x4C:
            note = '  <- SelfPtr of VMT %08X' % (a + 0x4C)
        if v == a:
            note += '  <- self'
        print('%08X  %08X%s' % (a, v, note))


if __name__ == '__main__':
    main()
