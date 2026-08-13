"""Find direct callers (E8 rel32) of a target VA across CODE."""
import io
import sys
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
CODE_LO, CODE_HI = 0x401000, 0x7A10D0
md = Cs(CS_ARCH_X86, CS_MODE_32)

targets = [int(a, 0) for a in sys.argv[1:]] or [0x747CF4]
for target in targets:
    print("=== callers of %08X ===" % target)
    for i in range(CODE_LO - BASE, CODE_HI - BASE - 5):
        if data[i] != 0xE8:
            continue
        rel = struct.unpack("<i", data[i + 1:i + 5])[0]
        callva = i + BASE
        dest = callva + 5 + rel
        if dest == target:
            ins = None
            for ci in md.disasm(data[i:i + 6], callva):
                ins = ci
                break
            print("  %08X  call %08X" % (callva, target))
    print()
