"""Dump Delphi string constants (PChar / ShortString / AnsiString-ref) at given VAs.

Usage: python tools/mall_strings.py 0x6DBF88 0x6CBA64 ...
"""
import os
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def read(data, off, n):
    return data[off:off + n]


def dump(data, va):
    off = va - BASE
    raw = read(data, off - 8, 64)
    print("=== VA 0x%X (file 0x%X) ===" % (va, off))
    # Delphi AnsiString: [va-8]=codepage/elemsize(2+2), [va-4]=refcount, [va-? ]... actually
    # AnsiString layout: -12 codepage/elemsize, -8 refcount, -4 length, then chars, then NUL.
    length_at_m4 = int.from_bytes(read(data, off - 4, 4), "little", signed=True)
    print("  [va-4] as len =", length_at_m4)
    # Try as AnsiString (len prefix at -4)
    if 0 < length_at_m4 < 512:
        s = read(data, off, length_at_m4)
        try:
            print("  AnsiString(gbk):", s.decode("gbk", errors="replace"))
        except Exception as exc:
            print("  AnsiString bytes:", s.hex())
    # Try as raw NUL-terminated
    end = off
    while end < off + 256 and data[end] != 0:
        end += 1
    craw = data[off:end]
    try:
        print("  CString(gbk):", craw.decode("gbk", errors="replace"))
    except Exception:
        print("  CString bytes:", craw.hex())
    print("  hex around:", raw.hex())


def main():
    with open(IMAGE, "rb") as fh:
        data = fh.read()
    for a in sys.argv[1:]:
        dump(data, int(a, 16))
    return 0


if __name__ == "__main__":
    sys.exit(main())
