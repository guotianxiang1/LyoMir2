"""Dump one CM dispatch arm: bytes + disassembly up to its terminating jump."""
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\cm-2\tools")
from cm2_dis import read  # noqa: E402
from capstone import Cs, CS_ARCH_X86, CS_MODE_32  # noqa: E402

MD = Cs(CS_ARCH_X86, CS_MODE_32)


def arm(va, maxlen=0x200):
    data = read(va, maxlen)
    out = []
    raw = []
    for i in MD.disasm(data, va):
        b = " ".join("%02X" % c for c in i.bytes)
        raw.extend(i.bytes)
        out.append("%08X  %-26s %s %s" % (i.address, b, i.mnemonic, i.op_str))
        if i.mnemonic == "jmp" and i.op_str.startswith("0x"):
            break
        if i.mnemonic == "ret":
            break
    return out, bytes(raw)


if __name__ == "__main__":
    for a in sys.argv[1:]:
        va = int(a, 0)
        lines, raw = arm(va)
        print("### arm 0x%06X  (%d bytes)" % (va, len(raw)))
        print("bytes: " + " ".join("%02X" % c for c in raw))
        print("\n".join(lines))
        print()
