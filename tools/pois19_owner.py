"""Name the function (and, when it is virtual, the class+slot) containing a VA.

Usage: python tools/pois19_owner.py <VA-hex> [more VAs...]

Walks back to the nearest `push ebp; mov ebp,esp` prologue, then looks the
prologue VA up in every Delphi VMT so a virtual method can be reported as
"<Class>.VMT+<slot>".
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pois19_vmt import BASE, enum_classes, load  # noqa: E402

PROLOGUE = bytes([0x55, 0x8B, 0xEC])


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    data = load()
    classes = enum_classes(data)

    slots = {}
    for vmt, name in classes.items():
        for off in range(0, 0x400, 4):
            v = struct.unpack_from("<I", data, vmt + off - BASE)[0]
            if 0x401000 <= v < 0x900000:
                slots.setdefault(v, []).append((name, off))

    for arg in sys.argv[1:]:
        va = int(arg, 16)
        off = va - BASE
        start = None
        for back in range(0, 0x1200):
            i = off - back
            if i < 0:
                break
            if data[i:i + 3] == PROLOGUE:
                start = i + BASE
                break
        if start is None:
            print("0x%06X  <no prologue within 0x1200>" % va)
            continue
        owners = slots.get(start, [])
        label = "; ".join("%s VMT+0x%X" % (n, o) for n, o in owners[:4]) \
            if owners else "(not a virtual slot)"
        if len(owners) > 4:
            label += " (+%d more)" % (len(owners) - 4)
        print("0x%06X  in sub_%06X  %s" % (va, start, label))
    return 0


if __name__ == "__main__":
    sys.exit(main())
