"""Dump the native group-formation chain byte-by-byte.

Functions of interest (VA, ImageBase 0x400000):
  0x6C341C  CM 1020 ClientCreateGroup
  0x6C34EC  CM 1021 ClientAddGroupMember
  0x6C3648  create-on-accept (sends SM 660 at 0x6C36E5)
  0x6F3EA8  CM 4412 group reply
  0x6F39B4  queue pending request
  0x726B80  TGroup ctor / allocate
  0x7272EC  insert member
"""
import io
import os
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000

with open(IMAGE, "rb") as fh:
    DATA = fh.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)
MD.detail = False


def dump(va, limit=400, out=sys.stdout, stop_at_ret=True):
    off = va - BASE
    print("=== 0x%06X ===" % va, file=out)
    n = 0
    seen_ret_depth = 0
    for insn in MD.disasm(DATA[off:off + limit * 8], va):
        print("0x%06X  %-22s %s %s" % (insn.address, insn.bytes.hex().upper(),
                                       insn.mnemonic, insn.op_str), file=out)
        n += 1
        if n >= limit:
            break
        if stop_at_ret and insn.mnemonic in ("ret", "retn") :
            seen_ret_depth += 1
            if seen_ret_depth >= 1:
                # keep going a little to catch the tail blocks Delphi emits after ret
                pass
    print("", file=out)


def callees(va, limit=600):
    """Exhaustive E8 target enumeration over a linear range."""
    off = va - BASE
    out = set()
    for insn in MD.disasm(DATA[off:off + limit * 8], va):
        if insn.mnemonic == "call" and insn.bytes[0] == 0xE8:
            try:
                out.add(int(insn.op_str, 16))
            except ValueError:
                pass
        if insn.mnemonic == "ret":
            break
    return sorted(out)


def main():
    buf = io.StringIO()
    for va, size in [(0x6C341C, 90), (0x6C34EC, 110), (0x6C3648, 120),
                     (0x6F39B4, 120), (0x6F3EA8, 160),
                     (0x726B80, 60), (0x7272EC, 90)]:
        dump(va, size, buf)
        print("  callees: %s" % ", ".join("0x%06X" % c for c in callees(va, size)),
              file=buf)
        print("", file=buf)
    txt = buf.getvalue()
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q1_group.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(txt)
    print(dst)


if __name__ == "__main__":
    main()
