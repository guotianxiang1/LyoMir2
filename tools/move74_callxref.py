"""Exhaustive E8/E9 rel32 cross-reference for a target VA in the M2 flat image.

Usage: python tools/move74_callxref.py 0x768454 [more VAs...]

Scans every byte offset (not just decoded instruction boundaries) so a target
cannot be missed because the surrounding bytes failed to decode linearly.
"""
import os
import struct
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000
TEXT_LO = 0x401000
TEXT_HI = 0x7F0000


def main():
    targets = [int(a, 16) for a in sys.argv[1:]] or [0x768454]
    with open(IMAGE, "rb") as handle:
        data = handle.read()

    wanted = set(targets)
    found = {t: [] for t in targets}
    end = len(data) - 5
    for off in range(end):
        op = data[off]
        if op != 0xE8 and op != 0xE9:
            continue
        rel = struct.unpack_from("<i", data, off + 1)[0]
        va = BASE + off
        tgt = va + 5 + rel
        if tgt in wanted:
            found[tgt].append((va, "call" if op == 0xE8 else "jmp"))

    # absolute dword references (VMT slots / function pointer tables)
    for off in range(0, len(data) - 3):
        val = struct.unpack_from("<I", data, off)[0]
        if val in wanted:
            found[val].append((BASE + off, "dword"))

    for t in targets:
        print("=== target 0x%06X : %d reference(s) ===" % (t, len(found[t])))
        for va, kind in sorted(found[t]):
            print("   %-5s from 0x%06X" % (kind, va))
    return 0


if __name__ == "__main__":
    sys.exit(main())
