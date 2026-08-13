"""ID3035: exhaustively emulate the M2Server client-message dispatch tree.

The dispatcher is sub_6D7D68. Its case tree is seeded at 0x6D805C with
    0x6D805C  8B 45 CC        mov   eax,[ebp-0x34]      ; the TDefaultMessage
    0x6D805F  0F B7 40 04     movzx eax,word [eax+4]    ; eax = Ident
and from there is a pure function of eax built out of a fixed instruction
vocabulary (cmp/sub/add/dec/inc + jcc + one `jmp [eax*4+table]` form).

Rather than reason about the tree shape, this walks it concretely: for every
candidate ident it single-steps the real bytes until control reaches an
address whose instruction is outside that vocabulary. That address is the
handler. Emulation is exact, so the resulting map is evidence, not inference.

Usage:
    python tools/id3035_dispatch_map.py                 # whole map
    python tools/id3035_dispatch_map.py 3035 4108       # trace these idents
"""
import os
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_OP_REG

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000
TREE_ENTRY = 0x6D805C
DEFAULT_ARM = 0x6DBC2C
IDENT_LO, IDENT_HI = 0, 0x4000
MAX_STEPS = 400

# Exact eflags model. Delphi's case trees mix `cmp`-relative signed jumps with
# borrow-relative unsigned ones (`sub` then `jb`), so ZF/CF/SF/OF all have to be
# tracked separately -- collapsing them into one "greater" bit silently turns a
# `jb` range test into "!= 0" and swallows whole ident ranges.
BRANCHES = {
    "je": lambda f: f["zf"],
    "jne": lambda f: not f["zf"],
    "jg": lambda f: not f["zf"] and f["sf"] == f["of"],
    "jle": lambda f: f["zf"] or f["sf"] != f["of"],
    "jl": lambda f: f["sf"] != f["of"],
    "jge": lambda f: f["sf"] == f["of"],
    "ja": lambda f: not f["cf"] and not f["zf"],
    "jbe": lambda f: f["cf"] or f["zf"],
    "jb": lambda f: f["cf"],
    "jae": lambda f: not f["cf"],
    "js": lambda f: f["sf"],
    "jns": lambda f: not f["sf"],
}


class NotTreeCode(Exception):
    pass


def u32(value):
    return value & 0xFFFFFFFF


def s32(value):
    value &= 0xFFFFFFFF
    return value - 0x100000000 if value & 0x80000000 else value


def flags_sub(lhs, rhs):
    """Flags for `lhs - rhs` on 32-bit operands, as cmp/sub set them."""
    raw = lhs - rhs
    res = u32(raw)
    return {
        "zf": res == 0,
        "cf": u32(lhs) < u32(rhs),
        "sf": bool(res & 0x80000000),
        "of": s32(lhs) - s32(rhs) != s32(res),
    }


def flags_add(lhs, rhs):
    raw = u32(lhs) + u32(rhs)
    res = u32(raw)
    return {
        "zf": res == 0,
        "cf": raw > 0xFFFFFFFF,
        "sf": bool(res & 0x80000000),
        "of": s32(lhs) + s32(rhs) != s32(res),
    }


def load():
    with open(IMAGE, "rb") as handle:
        return handle.read()


def step(md, data, va):
    off = va - BASE
    for insn in md.disasm(data[off:off + 16], va):
        return insn
    raise NotTreeCode("undecodable at 0x%X" % va)


def run(md, data, ident, trace=None):
    """Emulate the tree for one ident; return the handler VA."""
    eax = ident
    va = TREE_ENTRY + 3          # skip the `mov eax,[ebp-0x34]` load
    flags = {"zf": False, "cf": False, "sf": False, "of": False}
    for _ in range(MAX_STEPS):
        insn = step(md, data, va)
        mnem, ops = insn.mnemonic, insn.operands
        nxt = insn.address + insn.size
        if trace is not None:
            trace.append("0x%06X  %-22s %s %s   ; eax=%d" % (
                insn.address, insn.bytes.hex().upper(), mnem, insn.op_str,
                eax if eax < 0x80000000 else eax - 0x100000000))

        if mnem == "movzx" and insn.address == TREE_ENTRY + 3:
            va = nxt
            continue
        if mnem == "cmp" and ops[0].type == X86_OP_REG and \
                insn.reg_name(ops[0].reg) == "eax" and ops[1].type == X86_OP_IMM:
            flags = flags_sub(eax, u32(ops[1].imm))
            va = nxt
            continue
        if mnem in ("sub", "add") and ops[0].type == X86_OP_REG and \
                insn.reg_name(ops[0].reg) == "eax" and ops[1].type == X86_OP_IMM:
            delta = u32(ops[1].imm)
            if mnem == "sub":
                flags = flags_sub(eax, delta)
                eax = u32(eax - delta)
            else:
                flags = flags_add(eax, delta)
                eax = u32(eax + delta)
            va = nxt
            continue
        if mnem in ("dec", "inc") and ops[0].type == X86_OP_REG and \
                insn.reg_name(ops[0].reg) == "eax":
            carry = flags["cf"]          # dec/inc leave CF untouched
            if mnem == "dec":
                flags = flags_sub(eax, 1)
                eax = u32(eax - 1)
            else:
                flags = flags_add(eax, 1)
                eax = u32(eax + 1)
            flags["cf"] = carry
            va = nxt
            continue
        if mnem in BRANCHES and ops[0].type == X86_OP_IMM:
            va = ops[0].imm if BRANCHES[mnem](flags) else nxt
            continue
        if mnem == "jmp" and ops[0].type == X86_OP_IMM:
            va = ops[0].imm
            if va == DEFAULT_ARM:
                return va
            continue
        if mnem == "jmp" and ops[0].type == X86_OP_MEM:
            mem = ops[0].mem
            if insn.reg_name(mem.index) != "eax" or mem.scale != 4 or mem.base:
                raise NotTreeCode("odd table jmp at 0x%X" % insn.address)
            slot = u32(mem.disp) + 4 * eax
            va = int.from_bytes(data[slot - BASE:slot - BASE + 4], "little")
            if trace is not None:
                trace.append("        table[0x%06X + 4*%d] -> 0x%06X"
                             % (u32(mem.disp), eax, va))
            if va == DEFAULT_ARM:
                return va
            continue
        return insn.address       # left the tree vocabulary => handler


def main():
    data = load()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True

    if len(sys.argv) > 1:
        for arg in sys.argv[1:]:
            ident = int(arg, 0)
            trace = []
            handler = run(md, data, ident, trace)
            print("=== ident %d (0x%X) -> handler 0x%06X%s ===" % (
                ident, ident, handler,
                "  [DEFAULT/silent]" if handler == DEFAULT_ARM else ""))
            print("\n".join(trace))
            print()
        return 0

    hits = {}
    for ident in range(IDENT_LO, IDENT_HI):
        handler = run(md, data, ident)
        if handler != DEFAULT_ARM:
            hits.setdefault(handler, []).append(ident)
    print("# handled idents: %d, distinct handlers: %d"
          % (sum(len(v) for v in hits.values()), len(hits)))
    for handler in sorted(hits):
        print("0x%06X  %s" % (handler, " ".join(str(i) for i in hits[handler])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
