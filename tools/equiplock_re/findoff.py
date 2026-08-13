import sys, struct
from capstone import *
from capstone.x86 import X86_OP_MEM
import eq_re

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

# code range (typical Delphi .text)
LO = 0x401000
HI = 0xB00000
DATA = eq_re.DATA
BASE = eq_re.BASE

def find_disp(disp):
    """Find 4-byte LE occurrences of disp inside the code range, then try to
    decode an instruction that references it as a memory displacement."""
    pat = struct.pack("<I", disp)
    hits = []
    start = LO - BASE
    end = HI - BASE
    idx = start
    blob = DATA
    while True:
        j = blob.find(pat, idx, end)
        if j < 0:
            break
        # try decode starting from a few bytes before the disp to catch the whole insn
        found = None
        for back in range(2, 9):
            va = BASE + j - back
            code = eq_re.rd(va, 16)
            if not code:
                continue
            try:
                insn = next(md.disasm(code, va))
            except StopIteration:
                continue
            # does this instruction actually reference disp as a mem operand?
            ref = False
            for op in insn.operands:
                if op.type == X86_OP_MEM and op.mem.disp == disp and op.mem.base != 0:
                    ref = True
                    break
            # ensure the disp bytes fall within the instruction span
            if ref and (va <= BASE + j) and (BASE + j + 4 <= va + insn.size):
                found = insn
                break
        if found is not None:
            hits.append((found.address, found.mnemonic + " " + found.op_str, found.bytes.hex().upper()))
        idx = j + 1
    return hits

if __name__ == "__main__":
    for a in sys.argv[1:]:
        disp = int(a, 16)
        print("==== disp 0x%X ====" % disp)
        seen = set()
        for va, txt, b in find_disp(disp):
            if va in seen:
                continue
            seen.add(va)
            print("%08X  %-24s %s" % (va, b, txt))
        print()
