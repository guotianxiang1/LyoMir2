"""Disassemble a CM dispatch-tree leaf and surface its worker call(s) + gates.

Evidence base: flat_image.bin @ ImageBase 0x400000, capstone x86-32.
Each leaf begins where the selector tree of sub_6D7D68 lands (see cm3_missing).
The leaf pulls Recog/Param/Tag/Series out of the 12-byte wire record and the
body string/len out of the frame, evaluates any pre-gates, then tail-calls a
worker. We print raw bytes + mnemonics from the leaf up to the shared exit
label 0x6DBC2C (the switch default / SEH unwind) or the first ret, and list all
call targets with their own first bytes so the worker VA is unambiguous.

Usage (hermes venv python):
  python tools\\cm3_leaf.py 0x6DA405 [max_insns]
  python tools\\cm3_leaf.py all           # dump every Q3 leaf
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
EXIT_LABEL = 0x6DBC2C

with open(IMAGE, "rb") as f:
    IMG = f.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)
MD.detail = True

Q3 = [
    (3180, 0x6DA405), (3190, 0x6DA5AE), (3191, 0x6DA5C0), (3208, 0x6DA54C),
    (3209, 0x6DA56D), (3282, 0x6DA600), (3283, 0x6DA626), (3284, 0x6DA650),
    (3285, 0x6DA638), (3286, 0x6DA65D), (3287, 0x6DA895), (3288, 0x6DA8C4),
    (3294, 0x6DA613), (3295, 0x6DAA99), (3306, 0x6DAB39), (3307, 0x6DABEA),
    (3340, 0x6DAC30), (3344, 0x6DADD6), (3410, 0x6DAED9), (3503, 0x6DAF44),
    (4102, 0x6DABFC), (4105, 0x6DA005), (4123, 0x6DAE32), (4124, 0x6DAE53),
]


def rd(va, n):
    return IMG[va - BASE: va - BASE + n]


def bhex(b):
    return " ".join("%02X" % x for x in b)


def dump(va, limit=48):
    print("=" * 78)
    print("LEAF 0x%06X" % va)
    calls = []
    cur = va
    for _ in range(limit):
        ins = list(MD.disasm(rd(cur, 16), cur))
        if not ins:
            print("  %06X  <decode fail>" % cur)
            break
        i = ins[0]
        raw = bhex(i.bytes)
        print("  %06X  %-22s %s %s" % (i.address, raw, i.mnemonic, i.op_str))
        if i.mnemonic == "call":
            tgt = i.op_str
            if tgt.startswith("0x"):
                t = int(tgt, 0)
                calls.append(t)
                print("           -> callee 0x%06X first: %s" % (t, bhex(rd(t, 12))))
        if i.mnemonic in ("ret", "retn", "jmp"):
            if i.mnemonic == "jmp" and i.op_str.startswith("0x"):
                t = int(i.op_str, 0)
                if t == EXIT_LABEL:
                    print("           -> shared EXIT 0x6DBC2C (switch default / 静默)")
                    break
                # follow short local jmp within leaf
                if 0x6D8000 <= t <= 0x6DBFFF:
                    cur = t
                    continue
            break
        cur += i.size
    return calls


if __name__ == "__main__":
    if sys.argv[1] == "all":
        for ident, va in Q3:
            print("\n########## CM %d ##########" % ident)
            dump(va)
    else:
        va = int(sys.argv[1], 0)
        lim = int(sys.argv[2]) if len(sys.argv) > 2 else 48
        dump(va, lim)
