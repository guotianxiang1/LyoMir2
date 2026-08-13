"""HITARM: dump raw bytes / Delphi literal around a VA in the M2 flat image.

Usage: python tools/hitarm_str.py 0x64D22C [len]
Prints the 8 bytes before the VA (Delphi AnsiString refcount+length header)
and then `len` bytes from the VA as hex plus a GBK/ASCII rendering.
"""
import os
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def main():
    va = int(sys.argv[1], 16)
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 32
    with open(IMAGE, "rb") as fh:
        data = fh.read()
    off = va - BASE
    head = data[off - 8:off]
    body = data[off:off + n]
    print("VA 0x%06X  header[-8:] = %s  (len dword @-4 = %d)"
          % (va, head.hex().upper(),
             int.from_bytes(head[4:8], "little")))
    print("  bytes: %s" % body.hex().upper())
    for enc in ("ascii", "gbk"):
        try:
            print("  %-5s: %r" % (enc, body.split(b"\x00")[0].decode(enc)))
        except Exception as exc:  # noqa: BLE001
            print("  %-5s: <%s>" % (enc, exc))
    return 0


if __name__ == "__main__":
    sys.exit(main())
