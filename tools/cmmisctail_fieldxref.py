"""Find every instruction in .text that references a TPlayObject field +disp,
classify read vs write, and print with the enclosing VA. Used to decide whether
the SMS cooldown tick [self+0x18D8] and friends are ever *written* on a path a
faithful port can reach.

Usage: python tools\\cmmisctail_fieldxref.py 0x18D8 0x18D4 ...
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_MEM, X86_OP_REG, X86_OP_IMM

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
# .text of M2Server: roughly 0x401000 .. 0x7C0000 (code). Scan generously.
LO, HI = 0x401000, 0x7C0000

with open(IMAGE, "rb") as f:
    IMG = f.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)
MD.detail = True

WRITE_MNEMS = {"mov", "add", "sub", "inc", "dec", "and", "or", "xor",
               "mov ", "lea"}  # lea = address-of (neither r/w, flagged sep.)


def refs(disp):
    """disp like 0x18D8: find instructions with a mem operand disp==offset."""
    hits = []
    # linear sweep; resync on decode failure
    off = LO - BASE
    end = HI - BASE
    va = LO
    while off < end:
        chunk = IMG[off:off + 16]
        ins = next(iter(MD.disasm(chunk, va)), None)
        if ins is None:
            off += 1
            va += 1
            continue
        for op in ins.operands:
            if op.type == X86_OP_MEM and op.mem.disp == disp and \
               op.mem.base != 0 and op.mem.index == 0:
                # classify
                kind = "?"
                m = ins.mnemonic
                if m == "lea":
                    kind = "ADDR"
                elif m in ("cmp", "test", "push"):
                    kind = "READ"
                elif m in ("mov", "movzx", "movsx"):
                    # is the mem operand the destination?
                    kind = "WRITE" if ins.op_str.split(",")[0].strip().endswith("]") \
                        else "READ"
                elif m in ("inc", "dec", "add", "sub", "and", "or", "xor"):
                    kind = "RMW"
                hits.append((ins.address, kind, m, ins.op_str))
                break
        off += ins.size
        va += ins.size
    return hits


if __name__ == "__main__":
    for a in sys.argv[1:]:
        disp = int(a, 0)
        print("\n==== field +0x%X ====" % disp)
        for va, kind, m, ops in refs(disp):
            print("  %06X  %-5s  %s %s" % (va, kind, m, ops))
