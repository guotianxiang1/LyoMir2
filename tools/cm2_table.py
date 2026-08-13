"""Resolve the M2Server client-message (CM) dispatch: opcode -> handler VA.

The dispatcher `sub_6D7D68` selects on `word [msg+4]` (Ident) at 0x6D805C using a
Delphi binary-search comparison tree spliced with eight `jmp [eax*4+table]` jump
tables. Rather than hand-reading the tree, this walks it by emulating the exact
instruction subset the tree is built from, once per candidate opcode.

Emitted: opcode -> (handler VA, "DEFAULT" when it lands on the shared 0x6DBC2C
convergence point, i.e. the case has no arm at all).
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32, x86_const

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DISPATCH_ENTRY = 0x6D805C
DEFAULT_VA = 0x6DBC2C
M = 0xFFFFFFFF

with open(IMAGE, "rb") as f:
    IMG = f.read()


def rd(va, n):
    return IMG[va - BASE: va - BASE + n]


def u32(va):
    return int.from_bytes(rd(va, 4), "little")


MD = Cs(CS_ARCH_X86, CS_MODE_32)
MD.detail = True


def decode(va):
    for i in MD.disasm(rd(va, 16), va):
        return i
    return None


def flags_sub(a, b):
    r = (a - b) & M
    return {
        "ZF": r == 0,
        "SF": (r >> 31) & 1,
        "CF": (a & M) < (b & M),
        "OF": (((a ^ b) & (a ^ r)) >> 31) & 1,
    }


def flags_add(a, b):
    r = (a + b) & M
    return {
        "ZF": r == 0,
        "SF": (r >> 31) & 1,
        "CF": (a & M) + (b & M) > M,
        "OF": ((~(a ^ b) & (a ^ r)) >> 31) & 1,
    }


COND = {
    "je": lambda f: f["ZF"],
    "jz": lambda f: f["ZF"],
    "jne": lambda f: not f["ZF"],
    "jnz": lambda f: not f["ZF"],
    "jg": lambda f: (not f["ZF"]) and f["SF"] == f["OF"],
    "jle": lambda f: f["ZF"] or f["SF"] != f["OF"],
    "jl": lambda f: f["SF"] != f["OF"],
    "jge": lambda f: f["SF"] == f["OF"],
    "ja": lambda f: (not f["CF"]) and (not f["ZF"]),
    "jbe": lambda f: f["CF"] or f["ZF"],
    "jb": lambda f: f["CF"],
    "jae": lambda f: not f["CF"],
}


def resolve(ident, trace=None):
    """Walk the dispatch tree for one opcode; return the arm's VA."""
    eax = ident
    flags = {"ZF": False, "SF": 0, "CF": False, "OF": 0}
    va = DISPATCH_ENTRY
    for _ in range(400):
        ins = decode(va)
        if ins is None:
            return va
        m, ops = ins.mnemonic, ins.op_str
        if trace is not None:
            trace.append("%08X %s %s   ; eax=%d" % (va, m, ops, eax))

        # Tree preamble: eax <- Ident. Only at the entry; an identical reload at
        # an arm body must terminate the walk, not be absorbed.
        if va == DISPATCH_ENTRY and m == "mov":
            va += ins.size
            ins2 = decode(va)
            eax = ident
            va += ins2.size
            continue

        if m == "cmp" and ops.startswith("eax, "):
            flags = flags_sub(eax, int(ops.split(", ")[1], 0) & M)
            va += ins.size
            continue
        if m == "sub" and ops.startswith("eax, "):
            imm = int(ops.split(", ")[1], 0) & M
            flags = flags_sub(eax, imm)
            eax = (eax - imm) & M
            va += ins.size
            continue
        if m == "add" and ops.startswith("eax, "):
            imm = int(ops.split(", ")[1], 0) & M
            flags = flags_add(eax, imm)
            eax = (eax + imm) & M
            va += ins.size
            continue
        if m == "dec" and ops == "eax":
            cf = flags["CF"]
            flags = flags_sub(eax, 1)
            flags["CF"] = cf
            eax = (eax - 1) & M
            va += ins.size
            continue
        if m == "inc" and ops == "eax":
            cf = flags["CF"]
            flags = flags_add(eax, 1)
            flags["CF"] = cf
            eax = (eax + 1) & M
            va += ins.size
            continue

        if m in COND:
            va = int(ops, 0) if COND[m](flags) else va + ins.size
            continue
        if m == "jmp":
            if ops.startswith("dword ptr [eax*4 + "):
                tbl = int(ops.split("+ ")[1].rstrip("]"), 0)
                return u32(tbl + eax * 4)
            if ops.startswith("0x"):
                va = int(ops, 0)
                continue
            return va  # unmodelled indirect: stop, report site

        # Anything else is the arm body: we have arrived.
        return va
    raise RuntimeError("tree walk did not converge for ident %d" % ident)


def build(lo=0, hi=0x10000):
    out = {}
    for ident in range(lo, hi):
        va = resolve(ident)
        if va != DEFAULT_VA:
            out[ident] = va
    return out


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "trace":
        tr = []
        print("ident %s -> %08X" % (sys.argv[2], resolve(int(sys.argv[2], 0), tr)))
        print("\n".join(tr))
        sys.exit(0)
    tbl = build()
    print("# CM dispatch arms resolved from sub_6D7D68 @0x6D805C, default=0x%06X" % DEFAULT_VA)
    print("# total arms: %d" % len(tbl))
    for k in sorted(tbl):
        print("%5d  0x%06X" % (k, tbl[k]))
