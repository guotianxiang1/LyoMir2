"""Dump a table of Delphi long-string pointers (dword array) as GBK text.

usage: cmbeb_strtab.py <hexTableVA> <count>
"""
import struct
import sys

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()


def rd32(va):
    o = va - BASE
    if o < 0 or o + 4 > len(data):
        return None
    return struct.unpack("<I", data[o:o + 4])[0]


def dstr(va):
    if va is None or va - BASE - 4 < 0 or va - BASE >= len(data):
        return None
    n = rd32(va - 4)
    if n is None or not (1 <= n <= 600):
        return None
    raw = data[va - BASE:va - BASE + n]
    if b"\x00" in raw:
        return None
    try:
        return raw.decode("gbk")
    except UnicodeDecodeError:
        return None


def main():
    tbl = int(sys.argv[1], 16)
    n = int(sys.argv[2])
    sys.stdout.reconfigure(encoding="utf-8")
    for k in range(n):
        p = rd32(tbl + 4 * k)
        print("[%2d] byte%d bit%d  %08X -> %s"
              % (k, k // 8, k % 8, p or 0, dstr(p)))


main()
