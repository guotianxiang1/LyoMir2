"""Is there a native 30s 'verify' tick that sweeps dead/ghost group members?

C# TBaseObject.Base.cs @583 runs a 30_000 ms tick that (a) nulls m_GroupOwner when the
leader is dead/ghost and (b) drops every dead/ghost member from the leader's roster.
Look for the native counterpart: a 0x7530 interval compare anywhere in the player run
loop, and any group access near it.
"""
import io
import os
import struct

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0xB00000

with open(IMAGE, "rb") as fh:
    DATA = fh.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)


def dump(va, limit, out, label=""):
    off = va - BASE
    print("=== 0x%06X %s ===" % (va, label), file=out)
    for n, insn in enumerate(MD.disasm(DATA[off:off + limit * 8], va)):
        if n >= limit:
            break
        print("0x%06X  %-22s %s %s" % (insn.address, insn.bytes.hex().upper(),
                                       insn.mnemonic, insn.op_str), file=out)
    print("", file=out)


def find_imm32(value, lo=CODE_LO, hi=CODE_HI):
    """cmp reg,imm32 forms: 81 /7 id  and  3D id (cmp eax,imm32)."""
    pat = struct.pack("<I", value)
    hits, start = [], lo - BASE
    end = hi - BASE
    while True:
        i = DATA.find(pat, start, end)
        if i < 0:
            break
        start = i + 1
        for back in (2, 1, 3):
            s = i - back
            for insn in MD.disasm(DATA[s:s + 16], BASE + s):
                if insn.size == back + 4 and insn.mnemonic in ("cmp", "sub", "add", "mov"):
                    hits.append((insn.address, insn.bytes.hex().upper(),
                                 insn.mnemonic + " " + insn.op_str))
                break
    return hits


def main():
    buf = io.StringIO()
    print("### cmp/sub/mov with imm32 0x7530 (30000 ms) in the code range", file=buf)
    for va, by, txt in find_imm32(0x7530):
        print("  0x%06X  %-22s %s" % (va, by, txt), file=buf)
    print("", file=buf)
    dump(0x7270F8, 40, buf, "alive probe sub_7270F8")
    dump(0x7271D0, 95, buf, "SM 667 roster builder sub_7271D0")
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q14_verifytick.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
