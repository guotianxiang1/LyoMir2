"""HITARM: census of `mov dl,<state>` immediately followed by a rel32 call.

Mirrors the technique MOVE-11 used for state 0x40. Reports every site in the
code segment together with the callee, so a state byte can be classified into
read sites / set sites / clear sites.

Usage: python tools/hitarm_state_census.py 3C 40 3E
"""
import os
import sys

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0x7A10D0


def main():
    states = [int(a, 16) for a in sys.argv[1:]]
    if not states:
        print(__doc__)
        return 1
    with open(IMAGE, "rb") as fh:
        data = fh.read()
    for st in states:
        print("=== state 0x%02X (%d) ===" % (st, st))
        # form 1: B2 <st> ... E8 rel32 within a short window
        for off in range(CODE_LO - BASE, CODE_HI - BASE):
            if data[off] != 0xB2 or data[off + 1] != st:
                continue
            va = off + BASE
            # scan forward at most 12 bytes for an E8
            hit = None
            for k in range(2, 14):
                if data[off + k] == 0xE8:
                    rel = int.from_bytes(data[off + k + 1:off + k + 5],
                                         "little", signed=True)
                    tgt = off + k + 5 + rel + BASE
                    if CODE_LO <= tgt < CODE_HI:
                        hit = (off + k + BASE, tgt)
                    break
            if hit:
                print("  0x%06X  mov dl,0x%02X   -> call 0x%06X @0x%06X"
                      % (va, st, hit[1], hit[0]))
            else:
                print("  0x%06X  mov dl,0x%02X   (no rel32 call within 12B)"
                      % (va, st))
        # form 2: 66 BA <st> 00  (mov dx, imm16)
        pat = bytes([0x66, 0xBA, st, 0x00])
        i = CODE_LO - BASE
        while True:
            i = data.find(pat, i, CODE_HI - BASE)
            if i < 0:
                break
            print("  0x%06X  mov dx,0x%02X (word form)" % (i + BASE, st))
            i += 1
        # form 3: B3 <st> (mov bl, imm8) - loop-driven state ranges
        i = CODE_LO - BASE
        while True:
            i = data.find(bytes([0xB3, st]), i, CODE_HI - BASE)
            if i < 0:
                break
            print("  0x%06X  mov bl,0x%02X (loop seed)" % (i + BASE, st))
            i += 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
