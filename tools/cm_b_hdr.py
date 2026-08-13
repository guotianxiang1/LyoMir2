"""Find the 12-byte CM/SM header builder: a window that stores word fields at
+4, +6, +8, +0x0A of the same base register, plus a dword at +0.

Reports each site with surrounding disassembly so the parameter mapping
(Recog / Ident / Param / Tag / Series) can be read off directly.
"""
import re
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0x7A10D0
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

W = re.compile(r"^word ptr \[(e[a-z][a-z]) \+ (0x[0-9a-f]+)\], ")
sites = []

# sweep every aligned start; collect instruction streams in 64-instruction windows
pos = CODE_LO
step = 1
seen_starts = set()
va = CODE_LO
buf = []
for ins in md.disasm(data[CODE_LO - BASE:CODE_HI - BASE], CODE_LO):
    buf.append(ins)
    if len(buf) > 40:
        buf.pop(0)
    if ins.mnemonic != "mov":
        continue
    m = W.match(ins.op_str)
    if not m or int(m.group(2), 0) != 0x0A:
        continue
    reg = m.group(1)
    offs = {}
    for b in buf:
        if b.mnemonic != "mov":
            continue
        mm = re.match(r"^(?:word|dword) ptr \[%s(?: \+ (0x[0-9a-f]+))?\], (\S+)$"
                      % reg, b.op_str)
        if mm:
            o = int(mm.group(1), 0) if mm.group(1) else 0
            offs[o] = (b.address, b.bytes.hex().upper(), b.op_str)
    if {0x04, 0x06, 0x08, 0x0A}.issubset(offs.keys()):
        sites.append((buf[0].address, ins.address, reg, dict(offs)))

sys.stdout.reconfigure(encoding="utf-8")
print("header-builder sites: %d" % len(sites))
for start, end, reg, offs in sites:
    print("---- window %08X .. %08X  base=%s" % (start, end, reg))
    for o in sorted(offs):
        a, hx, ops = offs[o]
        print("   +0x%02X  %08X %-14s mov %s" % (o, a, hx, ops))
