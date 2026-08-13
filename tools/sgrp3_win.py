"""Find code windows that reference two byte-patterns close together.

Usage: sgrp3_win.py <window> <patA hex> -- <patB hex>
"""
import sys

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000


def find_all(data, pat):
    toks = pat.split()
    vals = [None if t == "??" else int(t, 16) for t in toks]
    n = len(vals)
    out = []
    first = vals[0]
    start = 0
    while True:
        j = data.find(bytes([first]), start)
        if j < 0 or j + n > len(data):
            break
        if all(v is None or data[j + k] == v for k, v in enumerate(vals)):
            out.append(j)
        start = j + 1
    return out


def main():
    win = int(sys.argv[1])
    rest = sys.argv[2:]
    sep = rest.index("--")
    a = find_all(open(IMG, "rb").read(), " ".join(rest[:sep]))
    data = open(IMG, "rb").read()
    b = find_all(data, " ".join(rest[sep + 1:]))
    bs = set(b)
    hits = 0
    for ja in a:
        near = [jb for jb in range(ja - win, ja + win) if jb in bs]
        if near:
            print("  A@0x%06X  B@%s" % (
                BASE + ja, ",".join("0x%06X" % (BASE + x) for x in near)))
            hits += 1
    print("pairs %d  (A=%d B=%d)" % (hits, len(a), len(b)))


if __name__ == "__main__":
    main()
