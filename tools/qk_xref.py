"""Scan the flat image for every instruction that references player field
offsets 0x9F4 / 0x9F8 / 0x9FC (the Qiankun-bag list triple), plus a couple of
neighbours, and report the VA, the owning "function" (nearest preceding 55 8B EC
prologue), and the decoded instruction.

Usage: qk_xref.py [off ...]   (defaults to the qiankun triple)
"""
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

TARGETS = [int(a, 0) for a in sys.argv[1:]] or [0x9F4, 0x9F8, 0x9FC]


def find_prologue(va):
    """Nearest preceding `55 8B EC` (push ebp; mov ebp,esp) within 0x1200 bytes."""
    o = va - BASE
    for p in range(o, max(0, o - 0x1200), -1):
        if data[p:p + 3] == b"\x55\x8b\xec":
            return BASE + p
    return None


def scan_for_disp(disp):
    """A dword displacement disp appears as little-endian 4 bytes inside a
    modrm+disp32 instruction. Find candidate positions, disassemble a small
    window ending near there, and keep instructions whose op_str contains the
    offset."""
    needle = struct.pack("<I", disp)
    hits = []
    start = 0
    hexoff = "0x%x" % disp
    while True:
        idx = data.find(needle, start)
        if idx < 0:
            break
        start = idx + 1
        # Disassemble a short window that could contain this disp32 as an operand.
        for back in range(2, 8):
            base = idx - back
            if base < 0:
                continue
            for insn in md.disasm(data[base:base + 12], BASE + base):
                if insn.address == BASE + base and (hexoff in insn.op_str):
                    hits.append((BASE + base, insn))
                break
    # de-dup by address
    seen = {}
    for va, insn in hits:
        seen[va] = insn
    return sorted(seen.items())


sys.stdout.reconfigure(encoding="utf-8")
for disp in TARGETS:
    print("=" * 70)
    print("XREFS to +0x%X" % disp)
    for va, insn in scan_for_disp(disp):
        fn = find_prologue(va)
        print("  %08X (fn %s)  %s %s" % (
            va, ("%08X" % fn) if fn else "????????", insn.mnemonic, insn.op_str))
