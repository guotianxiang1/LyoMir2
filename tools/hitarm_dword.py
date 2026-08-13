"""HITARM: read little-endian dwords at given VAs from the M2 flat image."""
import os
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def main():
    with open(IMAGE, "rb") as fh:
        data = fh.read()
    for arg in sys.argv[1:]:
        if ":" in arg:
            va_s, cnt_s = arg.split(":")
            va, cnt = int(va_s, 16), int(cnt_s, 0)
        else:
            va, cnt = int(arg, 16), 1
        for k in range(cnt):
            cur = va + 4 * k
            off = cur - BASE
            val = int.from_bytes(data[off:off + 4], "little")
            print("[0x%06X] = 0x%08X" % (cur, val))
    return 0


if __name__ == "__main__":
    sys.exit(main())
