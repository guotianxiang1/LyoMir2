"""Locate `mov dx, imm16` sites for a given SM ident (the reply-ident load that
precedes call [vmt+0x250]/[vmt+0x254]) and report the enclosing function start
plus whether that function references the corps manager global 0x7D5C60.
"""
import os
import struct
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000
DATA = open(IMAGE, "rb").read()
MD = Cs(CS_ARCH_X86, CS_MODE_32)


def find_func_start(va):
    # walk back to a `push ebp; mov ebp,esp` (55 8B EC) prologue
    off = va - BASE
    for back in range(0, 0x800):
        p = off - back
        if p < 0:
            break
        if DATA[p:p + 3] == b"\x55\x8b\xec":
            return BASE + p
    return None


def func_refs_global(start, glob, span=0x900):
    needle = struct.pack("<I", glob)
    blob = DATA[start - BASE:start - BASE + span]
    return needle in blob


def main():
    ident = int(sys.argv[1], 16)
    glob = int(sys.argv[2], 16) if len(sys.argv) > 2 else 0x7D5C60
    pat = b"\x66\xba" + struct.pack("<H", ident)
    print("=== mov dx, 0x%04X sites ===" % ident)
    start = 0
    while True:
        i = DATA.find(pat, start)
        if i < 0:
            break
        start = i + 1
        va = BASE + i
        fs = find_func_start(va)
        refs = func_refs_global(fs, glob) if fs else False
        print("  site 0x%06X  func=0x%06X  refs 0x%X=%s"
              % (va, fs or 0, glob, refs))
    return 0


if __name__ == "__main__":
    sys.exit(main())
