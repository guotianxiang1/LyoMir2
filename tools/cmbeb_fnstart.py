"""Locate the enclosing function start for a VA by scanning back for a prologue
that linearly decodes forward onto the target address.

usage: cmbeb_fnstart.py <hexVA> [window]
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

PRO = [b"\x55\x8b\xec", b"\x53\x8b", b"\x55\x8b\xec\x83\xc4", b"\x55\x8b\xec\x81\xc4"]


def decodes_onto(start, target):
    p = start
    for _ in range(20000):
        if p == target:
            return True
        if p > target:
            return False
        i = None
        for x in md.disasm(data[p - BASE:p - BASE + 16], p):
            i = x
            break
        if i is None:
            return False
        p += i.size
    return False


def main():
    va = int(sys.argv[1], 16)
    win = int(sys.argv[2]) if len(sys.argv) > 2 else 0x4000
    hits = []
    for back in range(3, win):
        s = va - back
        if data[s - BASE:s - BASE + 3] == b"\x55\x8b\xec" and decodes_onto(s, va):
            hits.append(s)
    sys.stdout.reconfigure(encoding="utf-8")
    if not hits:
        print("no prologue found within 0x%X" % win)
        return
    print("nearest enclosing 55 8B EC prologues (closest first):")
    for h in hits[:6]:
        print("  %08X  (-0x%X)" % (h, va - h))


main()
