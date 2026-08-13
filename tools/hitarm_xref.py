"""HITARM: find every reference to a set of VAs in the M2 flat image.

Reports:
  * absolute dword occurrences (jump-table slots / vtable slots)
  * E8 rel32 (call) and E9 rel32 (jmp) sites
  * 0F 8x rel32 (jcc) sites
  * 7x rel8 (short jcc) and EB rel8 sites

Usage: python tools/hitarm_xref.py 0x6D9EAF 0x6D9F4B
"""
import os
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000


def main():
    targets = [int(a, 16) for a in sys.argv[1:]]
    if not targets:
        print(__doc__)
        return 1
    with open(IMAGE, "rb") as fh:
        data = fh.read()
    n = len(data)
    for tgt in targets:
        print("=== target 0x%X ===" % tgt)
        raw = tgt.to_bytes(4, "little")
        # absolute dwords
        i = 0
        while True:
            i = data.find(raw, i)
            if i < 0:
                break
            print("  ABS   at VA 0x%06X (file 0x%X)" % (i + BASE, i))
            i += 1
        # rel32 forms
        for off in range(0, n - 6):
            b0 = data[off]
            if b0 in (0xE8, 0xE9):
                rel = int.from_bytes(data[off + 1:off + 5], "little", signed=True)
                if off + 5 + rel + BASE == tgt:
                    print("  %s   at VA 0x%06X  %s" % (
                        "CALL" if b0 == 0xE8 else "JMP ", off + BASE,
                        data[off:off + 5].hex().upper()))
            elif b0 == 0x0F and 0x80 <= data[off + 1] <= 0x8F:
                rel = int.from_bytes(data[off + 2:off + 6], "little", signed=True)
                if off + 6 + rel + BASE == tgt:
                    print("  JCC32 at VA 0x%06X  %s" % (
                        off + BASE, data[off:off + 6].hex().upper()))
            elif (0x70 <= b0 <= 0x7F) or b0 == 0xEB:
                rel = int.from_bytes(data[off + 1:off + 2], "little", signed=True)
                if off + 2 + rel + BASE == tgt:
                    print("  JCC8  at VA 0x%06X  %s" % (
                        off + BASE, data[off:off + 2].hex().upper()))
    return 0


if __name__ == "__main__":
    sys.exit(main())
