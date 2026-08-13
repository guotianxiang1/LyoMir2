"""Byte-pattern scanner over the M2Server flat image.

Usage:
  sgrp3_scan.py hex <hexbytes with ?? wildcards>      raw pattern hits
  sgrp3_scan.py w8 <imm16hex>                         mov word [reg+8], imm16
  sgrp3_scan.py disp <hexdisp32>                      any modrm disp32 == value
"""
import sys
import struct

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000


def load():
    with open(IMG, "rb") as f:
        return f.read()


def ctx(data, i, back=10, fwd=14):
    lo = max(0, i - back)
    return " ".join("%02X" % b for b in data[lo:i + fwd])


def scan_hex(data, pat):
    toks = pat.split()
    n = len(toks)
    vals = [None if t == "??" else int(t, 16) for t in toks]
    first = vals[0]
    start = 0
    hits = 0
    while True:
        j = data.find(bytes([first]), start)
        if j < 0 or j + n > len(data):
            break
        if all(v is None or data[j + k] == v for k, v in enumerate(vals)):
            print("  0x%06X  %s" % (BASE + j, ctx(data, j)))
            hits += 1
        start = j + 1
    print("total %d" % hits)


def main():
    mode = sys.argv[1]
    data = load()
    if mode == "hex":
        scan_hex(data, " ".join(sys.argv[2:]))
    elif mode == "w8":
        imm = int(sys.argv[2], 16)
        lo, hi = imm & 0xFF, (imm >> 8) & 0xFF
        # 66 C7 40 08 ll hh  /  66 C7 43 08 ll hh ... any reg
        for reg in range(8):
            if reg == 4:
                continue
            scan_hex(data, "66 C7 %02X 08 %02X %02X" % (0x40 | reg, lo, hi))
    elif mode == "disp":
        want = int(sys.argv[2], 16)
        tb = struct.pack("<I", want)
        start = 0
        while True:
            j = data.find(tb, start)
            if j < 0:
                break
            print("  0x%06X  %s" % (BASE + j, ctx(data, j)))
            start = j + 1


if __name__ == "__main__":
    main()
