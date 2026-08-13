"""Resolve the Delphi class that owns a VMT slot address.

Usage: python tools/move74_vmtowner.py 0x6AC8F8 0x685660 ...

A Delphi VMT V satisfies dword[V-0x4C] == V (vmtSelfPtr) and dword[V-0x28]
points at a ShortString holding the class name. Given the file offset of a slot
we walk backwards looking for the nearest self-pointer, then report the class
name and the slot index.
"""
import os
import struct
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def rd(data, va):
    off = va - BASE
    if off < 0 or off + 4 > len(data):
        return None
    return struct.unpack_from("<I", data, off)[0]


def name_at(data, va):
    off = va - BASE
    if off < 0 or off >= len(data):
        return "?"
    n = data[off]
    return data[off + 1:off + 1 + n].decode("latin-1")


def main():
    with open(IMAGE, "rb") as handle:
        data = handle.read()

    for arg in sys.argv[1:]:
        slot = int(arg, 16)
        hit = None
        for back in range(0, 0x400, 4):
            v = slot - back
            if rd(data, v - 0x4C) == v:
                hit = (v, back)
                break
        if hit is None:
            print("0x%06X  <no VMT self-pointer within 0x400>" % slot)
            continue
        v, back = hit
        cname = name_at(data, rd(data, v - 0x28) or 0)
        parent = rd(data, v - 0x24)
        pname = "-"
        if parent:
            pv = rd(data, parent)
            if pv:
                pname = name_at(data, rd(data, pv - 0x28) or 0)
        print("0x%06X  VMT=0x%06X slot=+0x%02X  class=%-24s parent=%s"
              % (slot, v, back, cname, pname))
    return 0


if __name__ == "__main__":
    sys.exit(main())
