"""Linear-disassemble a CM worker for triage: SM sends, field/global refs, calls.

Evidence base: flat_image.bin @ ImageBase 0x400000, capstone x86-32.
Delphi worker functions are near-linear (prologue / SEH / body / teardown / ret).
We disassemble a window from the entry, annotate:
  * call [reg+0x250] / [reg+0x254]  -> the two unicast reply slots (SendDefMessage)
  * mov dx,imm / mov edx,imm        -> candidate SM ident feeding a reply
  * refs to [0x7Dxxxx] globals and [reg+0xNNN] player fields
  * direct calls (with callee first bytes)
Stops after `count` instructions or when a plausible epilogue ret is reached at
depth 0. This is triage, not a decompiler; branches are shown, not followed.

Usage: python tools\\cm3_worker.py 0x6E3280 [count]
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000

with open(IMAGE, "rb") as f:
    IMG = f.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)
MD.detail = True


def rd(va, n):
    return IMG[va - BASE: va - BASE + n]


def bhex(b):
    return " ".join("%02X" % x for x in b)


def dump(va, count=200):
    print("WORKER 0x%06X" % va)
    cur = va
    last_dx = None
    seen_ret = False
    for _ in range(count):
        ins = list(MD.disasm(rd(cur, 16), cur))
        if not ins:
            print("  %06X  <decode fail>" % cur)
            break
        i = ins[0]
        raw = bhex(i.bytes)
        note = ""
        m, ops = i.mnemonic, i.op_str
        # next-function prologue after we've already seen a ret -> stop
        if seen_ret and raw.startswith("55 8B EC"):
            print("  ---- (next function prologue) ----")
            break
        if m == "mov" and (ops.startswith("dx,") or ops.startswith("edx,")) and "0x" in ops:
            last_dx = ops.split(", ")[-1]
            note = "   ; <= SM ident? dx=%s" % last_dx
        if m == "call" and ("+ 0x250]" in ops or "+ 0x254]" in ops):
            slot = "0x250(ret10)" if "0x250" in ops else "0x254(ret14)"
            note = "   ; *** SM SEND via vmt+%s  (dx=%s) ***" % (slot, last_dx)
        elif m == "call" and ops.startswith("0x"):
            t = int(ops, 0)
            note = "   -> 0x%06X %s" % (t, bhex(rd(t, 8)))
        elif "0x7d" in ops.lower() or "0x7c" in ops.lower():
            note = "   ; <global>"
        print("  %06X  %-24s %s %s%s" % (i.address, raw, m, ops, note))
        if m in ("ret", "retn"):
            seen_ret = True
            print("  ---- ret 0x%s ----" % (ops if ops else "0"))
        cur += i.size


WORKERS = [
    (3180, [0x6E3280]), (3190, [0x6E590C]), (3191, [0x6E5BA8]),
    (3208, [0x6EA5E0]), (3209, [0x6EA858]), (3282, [0x6E64BC]),
    (3283, [0x6E67B0]), (3284, [0x6E6EA4]), (3285, [0x6E6DE8]),
    (3286, [0x6E6B54]), (3287, [0x6E8734]), (3288, [0x6E8820]),
    (3294, [0x6EB190]), (3295, [0x6EB8E4]), (3306, [0x6EFD54]),
    (3307, [0x6CBD78]), (3340, [0x79E78C]), (3344, [0x6EC5D8]),
    (3410, [0x6EBE50]), (3503, [0x6EF970]), (4102, [0x6B7BCC]),
    (4105, [0x7742C0, 0x6BCE2C, 0x6EE174]), (4123, [0x6BF908]),
    (4124, [0x6BFA88]),
]

if __name__ == "__main__":
    if sys.argv[1] == "all":
        cnt = int(sys.argv[2]) if len(sys.argv) > 2 else 200
        for ident, vas in WORKERS:
            for va in vas:
                print("\n########## CM %d  worker 0x%06X ##########" % (ident, va))
                dump(va, cnt)
    else:
        va = int(sys.argv[1], 0)
        cnt = int(sys.argv[2]) if len(sys.argv) > 2 else 160
        dump(va, cnt)
