#!/usr/bin/env python3
"""Locate literals in a flat image and every 4-byte little-endian reference to them."""
import argparse
import re
import struct

M2 = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
GG = r"D:\loym2\staging\_gg_reunpack_work\dump_gg2025\flat_image.bin"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("needle")
    ap.add_argument("--image", default=M2)
    ap.add_argument("--base", type=lambda s: int(s, 0), default=0x400000)
    ap.add_argument("--encoding", default="ascii")
    ap.add_argument("--hex", action="store_true", help="needle is a hex byte string")
    ap.add_argument("--norefs", action="store_true")
    ap.add_argument("--icase", action="store_true")
    args = ap.parse_args()

    data = open(args.image, "rb").read()
    if args.hex:
        pat = bytes.fromhex(args.needle.replace(" ", ""))
    else:
        pat = args.needle.encode(args.encoding)

    flags = re.IGNORECASE if args.icase else 0
    hits = [m.start() for m in re.finditer(re.escape(pat), data, flags)]
    print(f"# {len(hits)} literal hit(s) for {args.needle!r}")
    for off in hits:
        va = off + args.base
        ctx = data[max(0, off - 8):off + len(pat) + 8]
        print("  VA=%08X  ctx=%s" % (
            va, "".join(chr(b) if 32 <= b < 127 else "." for b in ctx)))
        if args.norefs:
            continue
        # Delphi string literals are usually referenced by their data VA directly.
        for cand in (va, va - 1, va - 4, va - 8, va - 0x0C, va - 0x10):
            ref = struct.pack("<I", cand)
            for m in re.finditer(re.escape(ref), data):
                print("      ref->%08X from code VA=%08X (delta=%d)"
                      % (cand, m.start() + args.base, cand - va))


if __name__ == "__main__":
    main()
