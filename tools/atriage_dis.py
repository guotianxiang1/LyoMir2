#!/usr/bin/env python3
"""Disassemble a VA range out of a flat image (default: the M2Server baseline)."""
import argparse

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

M2 = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
GG = r"D:\loym2\staging\_gg_reunpack_work\dump_gg2025\flat_image.bin"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("va", type=lambda s: int(s, 0))
    ap.add_argument("length", type=lambda s: int(s, 0), nargs="?", default=0x80)
    ap.add_argument("--image", default=M2)
    ap.add_argument("--base", type=lambda s: int(s, 0), default=0x400000)
    ap.add_argument("--raw", action="store_true", help="hex dump instead of disasm")
    args = ap.parse_args()

    with open(args.image, "rb") as fh:
        fh.seek(args.va - args.base)
        data = fh.read(args.length)

    if args.raw:
        for off in range(0, len(data), 16):
            chunk = data[off:off + 16]
            print("%08X  %-47s  %s" % (
                args.va + off,
                " ".join("%02X" % b for b in chunk),
                "".join(chr(b) if 32 <= b < 127 else "." for b in chunk)))
        return

    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = False
    for ins in md.disasm(data, args.va):
        print("%08X  %-24s %s %s" % (
            ins.address,
            " ".join("%02X" % b for b in ins.bytes),
            ins.mnemonic,
            ins.op_str))


if __name__ == "__main__":
    main()
