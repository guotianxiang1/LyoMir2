"""Find direct call/jmp xrefs to a VA by scanning rel32 encodings, plus any
absolute dword in the image equal to the VA (vtable slots / data tables).

usage: cmbeb_xref.py <hexVA>
"""
import struct
import sys

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()


def main():
    tgt = int(sys.argv[1], 16)
    calls, jmps, dwords = [], [], []
    n = len(data)
    for off in range(0, n - 5):
        b = data[off]
        if b in (0xE8, 0xE9):
            rel = struct.unpack("<i", data[off + 1:off + 5])[0]
            if BASE + off + 5 + rel == tgt:
                (calls if b == 0xE8 else jmps).append(BASE + off)
    packed = struct.pack("<I", tgt)
    i = data.find(packed)
    while i != -1 and len(dwords) < 200:
        dwords.append(BASE + i)
        i = data.find(packed, i + 1)
    sys.stdout.reconfigure(encoding="utf-8")
    print("target %08X" % tgt)
    print("call rel32 (%d): %s" % (len(calls), " ".join("%08X" % x for x in calls[:80])))
    print("jmp  rel32 (%d): %s" % (len(jmps), " ".join("%08X" % x for x in jmps[:80])))
    print("abs dword (%d): %s" % (len(dwords), " ".join("%08X" % x for x in dwords[:80])))


main()
