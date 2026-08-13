"""Chase live pointers inside the memory-snapshot flat image.

Given a VA, treat it as a Delphi object pointer and print its VMT class name
(Delphi vmtClassName = VMT-0x2C -> ShortString) plus a hex dump of the first
0x40 bytes. Also resolves nested pointers on request.

Usage:
  python tools/newbiequest_re/chase.py cls  0x7DC170
  python tools/newbiequest_re/chase.py dump 0x7DC170 0x40
  python tools/newbiequest_re/chase.py str  0x5E8630
"""
import os
import struct
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000
DATA = open(IMAGE, "rb").read()


def rd(va, n):
    off = va - BASE
    if off < 0 or off + n > len(DATA):
        return None
    return DATA[off:off + n]


def rdu32(va):
    b = rd(va, 4)
    return struct.unpack("<I", b)[0] if b else None


def shortstr(va):
    b = rd(va, 1)
    if not b:
        return None
    ln = b[0]
    s = rd(va + 1, ln)
    try:
        return s.decode("gbk", errors="replace")
    except Exception:
        return s


def ansistr(va):
    # AnsiString: pointer points at chars; length at [va-4].
    if va == 0:
        return ""
    ln = rdu32(va - 4)
    if ln is None or ln > 4096:
        return "<?>"
    s = rd(va, ln)
    try:
        return s.decode("gbk", errors="replace")
    except Exception:
        return repr(s)


def classname(va):
    vmt = rdu32(va)
    if not vmt:
        return None, None
    namep = rdu32(vmt - 0x2C)
    return vmt, shortstr(namep) if namep else None


def dump(va, n):
    b = rd(va, n)
    if b is None:
        print("  <out of range>")
        return
    for i in range(0, n, 16):
        row = b[i:i + 16]
        hexs = " ".join("%02X" % c for c in row)
        dwords = ""
        if i + 16 <= n:
            ds = struct.unpack("<4I", row)
            dwords = "  | " + " ".join("%08X" % d for d in ds)
        print("  +0x%02X  %-47s %s" % (i, hexs, dwords))


def main():
    cmd = sys.argv[1]
    va = int(sys.argv[2], 16)
    if cmd == "cls":
        vmt, name = classname(va)
        print("obj 0x%X: VMT=0x%X class=%r" % (va, vmt or 0, name))
    elif cmd == "dump":
        n = int(sys.argv[3], 16) if len(sys.argv) > 3 else 0x40
        print("dump 0x%X (%d bytes):" % (va, n))
        dump(va, n)
    elif cmd == "str":
        print("shortstr @0x%X: %r" % (va, shortstr(va)))
    elif cmd == "ansi":
        print("ansistr @0x%X: %r" % (va, ansistr(va)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
