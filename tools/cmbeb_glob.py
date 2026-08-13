"""Identify a Delphi global object slot: find writers, then resolve the class
name through the constructor's VMT argument (VMT-0x38 -> ShortString name).

usage: cmbeb_glob.py <hexGlobalVA>
"""
import re
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False


def rd32(va):
    o = va - BASE
    if o < 0 or o + 4 > len(data):
        return None
    return struct.unpack("<I", data[o:o + 4])[0]


def shortstr(va):
    o = va - BASE
    if o < 0 or o >= len(data):
        return None
    n = data[o]
    if not (1 <= n <= 60):
        return None
    try:
        return data[o + 1:o + 1 + n].decode("gbk")
    except UnicodeDecodeError:
        return None


def vmt_name(vmt):
    p = rd32(vmt - 0x38)
    return shortstr(p) if p else None


def main():
    g = int(sys.argv[1], 16)
    sys.stdout.reconfigure(encoding="utf-8")
    # A3 imm32 = mov [imm32], eax ; 89 05 imm32 = mov [imm32], r32
    pats = [(b"\xa3" + struct.pack("<I", g), 5, "mov [g],eax"),
            (b"\x89\x05" + struct.pack("<I", g), 6, "mov [g],eax(89 05)"),
            (b"\x89\x1d" + struct.pack("<I", g), 6, "mov [g],ebx"),
            (b"\x89\x35" + struct.pack("<I", g), 6, "mov [g],esi")]
    writers = []
    for pb, sz, tag in pats:
        i = data.find(pb)
        while i != -1:
            writers.append((BASE + i, tag))
            i = data.find(pb, i + 1)
    print("global %08X  current dword=%s" % (g, "%08X" % (rd32(g) or 0)))
    print("writers: %s" % " ".join("%08X(%s)" % (a, t) for a, t in writers[:20]))
    for a, _t in writers[:8]:
        lo = a - 0x60
        out = []
        for i in md.disasm(data[lo - BASE:a - BASE + 8], lo):
            out.append("%08X  %-9s %s" % (i.address, i.mnemonic, i.op_str))
            m = re.match(r"^eax, (0x[0-9a-f]+)$", i.op_str)
            if i.mnemonic == "mov" and m:
                v = int(m.group(1), 0)
                nm = vmt_name(v)
                if nm:
                    out[-1] += "    ; VMT %s" % nm
        print("--- context %08X ---" % a)
        print("\n".join(out[-22:]))


main()
