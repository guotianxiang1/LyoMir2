"""Find Delphi VMTs whose class name matches a given string, plus raw
occurrences of the class-name ShortString. Delphi stores the class name as a
ShortString; the VMT field vmtClassName (VMT-0x2C) points at it. So:
  1. locate the ShortString bytes (len, chars)
  2. find dwords in the image equal to (that ShortString VA)
  3. those dwords sit at VMT-0x2C, so VMT = dword_va + 0x2C
"""
import os
import struct
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000
DATA = open(IMAGE, "rb").read()


def ss(va):
    off = va - BASE
    n = DATA[off]
    return DATA[off + 1:off + 1 + n]


def main():
    names = sys.argv[1:]
    if not names:
        names = ["TCorps", "TCorpsMan", "TCorpsManager", "TCorpsMember",
                 "TGild", "TGildMan"]
    for name in names:
        raw = name.encode("ascii")
        pat = bytes([len(raw)]) + raw
        print("=== class name %r ===" % name)
        start = 0
        found_any = False
        while True:
            i = DATA.find(pat, start)
            if i < 0:
                break
            start = i + 1
            # require the next byte after the name to be 0 (typical padding) or
            # printable boundary; accept regardless but note VA
            ssva = BASE + i
            # find dwords equal to ssva (candidate vmtClassName slots)
            needle = struct.pack("<I", ssva)
            j = 0
            while True:
                k = DATA.find(needle, j)
                if k < 0:
                    break
                j = k + 1
                slot_va = BASE + k
                vmt = slot_va + 0x2C
                # sanity: exact class name back-read
                try:
                    nm = ss(struct.unpack("<I", DATA[vmt - 0x2C - BASE:vmt - 0x2C - BASE + 4])[0])
                except Exception:
                    nm = None
                print("  ss@0x%X  vmtClassNameSlot@0x%X  => VMT=0x%X  (name=%r)"
                      % (ssva, slot_va, vmt,
                         nm.decode('latin1') if nm else None))
                found_any = True
        if not found_any:
            print("  (not found)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
