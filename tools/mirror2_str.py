"""Print Delphi literals at the given VAs as UTF-8 (source bytes are GBK)."""
import io
import struct
import sys

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", newline="\n")


def main():
    with open(IMG, "rb") as f:
        data = f.read()
    for a in sys.argv[1:]:
        va = int(a, 16)
        off = va - BASE
        ln = struct.unpack_from("<i", data, off - 4)[0]
        rc = struct.unpack_from("<i", data, off - 8)[0]
        raw = data[off:off + ln] if 0 < ln < 512 else b""
        print("0x%06X  len=%-4d rc=%-3d  %-46s  %r" % (
            va, ln, rc,
            raw.decode("gbk", "replace"),
            raw[:40]))


if __name__ == "__main__":
    main()
