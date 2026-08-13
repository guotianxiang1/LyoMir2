"""Who touches [player+0xA80] (the TGroup pointer) and who calls TGroup.DelMember?

Answers the standing BLOCKED question: does native drop a logging-out player from
the group slot array, or does it leave the slot and filter on ghost [+0x73]?
"""
import io
import os
import struct

from capstone import CS_ARCH_X86, CS_MODE_32, Cs
from capstone.x86 import X86_OP_MEM, X86_OP_IMM, X86_OP_REG

IMAGE = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
CODE_LO, CODE_HI = 0x401000, 0xB00000

with open(IMAGE, "rb") as fh:
    DATA = fh.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)
MD.detail = True


def e8_callers(target):
    """Every E8 rel32 whose resolved target == `target`, over the code range."""
    hits = []
    for va in range(CODE_LO, CODE_HI):
        off = va - BASE
        if DATA[off] != 0xE8:
            continue
        rel = struct.unpack("<i", DATA[off + 1:off + 5])[0]
        if va + 5 + rel == target:
            hits.append(va)
    return hits


def touches_offset(disp, out, label):
    """Linear sweep for any instruction with a memory displacement == disp."""
    reads, writes = [], []
    off = CODE_LO - BASE
    end = CODE_HI - BASE
    # linear disasm from the code start; Delphi images sync quickly
    for insn in MD.disasm(DATA[off:end], CODE_LO):
        for i, op in enumerate(insn.operands):
            if op.type == X86_OP_MEM and op.mem.disp == disp and op.mem.base != 0:
                rec = (insn.address, insn.bytes.hex().upper(),
                       insn.mnemonic + " " + insn.op_str)
                if i == 0 and insn.mnemonic in ("mov", "and", "or", "xor", "add",
                                                "sub", "inc", "dec"):
                    writes.append(rec)
                else:
                    reads.append(rec)
                break
    print("### disp 0x%X %s : %d write-form, %d read-form" % (
        disp, label, len(writes), len(reads)), file=out)
    print("-- WRITE FORM --", file=out)
    for va, by, txt in writes:
        print("  0x%06X  %-22s %s" % (va, by, txt), file=out)
    print("-- READ FORM --", file=out)
    for va, by, txt in reads:
        print("  0x%06X  %-22s %s" % (va, by, txt), file=out)
    print("", file=out)


def main():
    buf = io.StringIO()
    for target, name in [(0x726E68, "TGroup.DelMember"),
                         (0x6C35E8, "free-group helper"),
                         (0x727754, "TGroup.GroupSetV"),
                         (0x7272EC, "TGroup.AddMember"),
                         (0x726B80, "TGroup.Create"),
                         (0x727FB0, "TGroup leader-transfer")]:
        hits = e8_callers(target)
        print("E8 callers of 0x%06X (%s): %d -> %s" % (
            target, name, len(hits), ", ".join("0x%06X" % h for h in hits)),
            file=buf)
    print("", file=buf)
    touches_offset(0xA80, buf, "TPlayObject group pointer")
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q6_logout.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
