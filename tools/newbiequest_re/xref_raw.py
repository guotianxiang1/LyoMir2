"""Raw byte scan for a LE dword; print the 3 bytes preceding each hit so we can
distinguish reads (A1) from writes (A3 / C7 05 / 89 xx) and imm loads (B8..BF).
"""
import os
import struct
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def main():
    targets = [int(a, 16) for a in sys.argv[1:]]
    with open(IMAGE, "rb") as fh:
        data = fh.read()
    for tgt in targets:
        needle = struct.pack("<I", tgt)
        print("=== raw hits for 0x%X ===" % tgt)
        start = 0
        while True:
            i = data.find(needle, start)
            if i < 0:
                break
            start = i + 1
            pre = data[max(0, i - 3):i]
            va = BASE + i
            tag = ""
            if pre[-1:] == b"\xA3":
                tag = "  <== WRITE mov moffs32,eax"
            elif pre[-2:-1] == b"\xC7" and pre[-1:] == b"\x05":
                tag = "  <== WRITE mov dword,imm"
            elif pre[-1:] in (b"\xB8", b"\xB9", b"\xBA", b"\xBB",
                              b"\xBC", b"\xBD", b"\xBE", b"\xBF"):
                tag = "  <== mov reg,imm32 (addr-of)"
            elif pre[-1:] == b"\xA1":
                tag = "  (read mov eax,moffs32)"
            print("  0x%06X  pre=%s%s" % (va, pre.hex().upper(), tag))
    return 0


if __name__ == "__main__":
    sys.exit(main())
