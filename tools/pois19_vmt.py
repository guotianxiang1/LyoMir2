"""Delphi VMT tools for the M2Server flat image.

Subcommands:
  classes                 -- enumerate Delphi VMTs by scanning for vmtSelfPtr
  slot <VMT-hex> <off>    -- read one VMT slot
  dump <VMT-hex> <n>      -- dump n user-virtual slots (offset 0..4n)
  holders <fnVA-hex>      -- find every VMT slot whose value == fnVA, name the class
  slotof <off-hex>        -- for every known class, print VMT+off value

Delphi VMT negative-offset layout (32-bit):
  -76 SelfPtr  -44 ClassName(ShortString ptr)  -40 InstanceSize  -36 Parent
User-declared virtual methods start at VMT+0.
"""
import os
import struct
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000

vmtSelfPtr = -76
vmtClassName = -44
vmtInstanceSize = -40
vmtParent = -36


def load():
    with open(IMAGE, "rb") as handle:
        return handle.read()


def ok(data, va, width=4):
    off = va - BASE
    return 0 <= off and off + width <= len(data)


def dw(data, va):
    if not ok(data, va):
        return None
    return struct.unpack_from("<I", data, va - BASE)[0]


def shortstr(data, va):
    off = va - BASE
    if not (0 <= off < len(data)):
        return None
    n = data[off]
    if n == 0 or off + 1 + n > len(data):
        return None
    try:
        s = data[off + 1:off + 1 + n].decode("latin-1")
    except Exception:
        return None
    if not all(32 <= ord(c) < 127 for c in s):
        return None
    return s


def classname(data, vmt):
    p = dw(data, vmt + vmtClassName)
    if p is None:
        return None
    return shortstr(data, p)


def enum_classes(data):
    """A Delphi VMT has [vmt-76] == vmt (SelfPtr)."""
    out = {}
    for off in range(0, len(data) - 4, 4):
        v = struct.unpack_from("<I", data, off)[0]
        va = off + BASE
        if v != va + 76:
            continue
        vmt = va + 76
        name = classname(data, vmt)
        if name and name[0] in "TE":
            out[vmt] = name
    return out


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    data = load()
    cmd = sys.argv[1]

    if cmd == "classes":
        cls = enum_classes(data)
        print("=== %d Delphi VMTs ===" % len(cls))
        for vmt in sorted(cls):
            print("  VMT 0x%06X  size=%-7s %s" %
                  (vmt, dw(data, vmt + vmtInstanceSize), cls[vmt]))
        return 0

    if cmd == "slot":
        vmt = int(sys.argv[2], 16)
        off = int(sys.argv[3], 16)
        print("VMT 0x%06X (%s) +0x%X = 0x%06X" %
              (vmt, classname(data, vmt), off, dw(data, vmt + off)))
        return 0

    if cmd == "dump":
        vmt = int(sys.argv[2], 16)
        n = int(sys.argv[3])
        print("=== VMT 0x%06X %s  parent=0x%06X size=%s ===" %
              (vmt, classname(data, vmt), dw(data, vmt + vmtParent) or 0,
               dw(data, vmt + vmtInstanceSize)))
        for i in range(n):
            print("  +0x%03X  0x%06X" % (i * 4, dw(data, vmt + i * 4)))
        return 0

    if cmd == "holders":
        fn = int(sys.argv[2], 16)
        cls = enum_classes(data)
        pat = struct.pack("<I", fn)
        print("=== VMT slots holding 0x%06X ===" % fn)
        start = 0
        while True:
            i = data.find(pat, start)
            if i < 0:
                break
            start = i + 1
            if i % 4:
                continue
            slot_va = i + BASE
            for vmt, name in cls.items():
                off = slot_va - vmt
                if 0 <= off < 0x400:
                    print("  slot 0x%06X = VMT 0x%06X +0x%03X  %s" %
                          (slot_va, vmt, off, name))
        return 0

    if cmd == "slotof":
        off = int(sys.argv[2], 16)
        cls = enum_classes(data)
        groups = {}
        for vmt, name in sorted(cls.items()):
            v = dw(data, vmt + off)
            if v is None or not (0x401000 <= v < 0x900000):
                continue
            groups.setdefault(v, []).append((vmt, name))
        print("=== VMT+0x%X across %d classes ===" % (off, len(cls)))
        for v in sorted(groups):
            print("  impl 0x%06X :" % v)
            for vmt, name in groups[v]:
                print("      VMT 0x%06X %s" % (vmt, name))
        return 0

    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.exit(main())
