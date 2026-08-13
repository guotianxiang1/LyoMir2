"""Compact per-worker triage: entry gates, SM sends, global refs, callees.

For each Q3 worker, scan a window and emit only the decision-relevant signals:
  * ENTRY GATE   : cmp/test + jcc reached before the first `call` (the pre-checks
                   still inside the worker prologue that a port could reproduce)
  * SM SEND      : every `call [reg+0x250|0x254]` with the nearest prior mov dx,imm
  * GLOBAL       : distinct [0x7Cxxxx]/[0x7Dxxxx] absolute refs (subsystem anchors)
  * FIELD        : distinct [reg+0xNN] player/object field offsets >= 0x100
  * CALLEE       : distinct direct call targets
This is a signal digest to classify build / no-op / fail-closed, not a decompiler.
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
import re

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
with open(IMAGE, "rb") as f:
    IMG = f.read()
MD = Cs(CS_ARCH_X86, CS_MODE_32)
MD.detail = True


def rd(va, n):
    return IMG[va - BASE: va - BASE + n]


WORKERS = [
    (3180, 0x6E3280), (3190, 0x6E590C), (3191, 0x6E5BA8), (3208, 0x6EA5E0),
    (3209, 0x6EA858), (3282, 0x6E64BC), (3283, 0x6E67B0), (3284, 0x6E6EA4),
    (3285, 0x6E6DE8), (3286, 0x6E6B54), (3287, 0x6E8734), (3288, 0x6E8820),
    (3294, 0x6EB190), (3295, 0x6EB8E4), (3306, 0x6EFD54), (3307, 0x6CBD78),
    (3340, 0x79E78C), (3344, 0x6EC5D8), (3410, 0x6EBE50), (3503, 0x6EF970),
    (4102, 0x6B7BCC), (4105, 0x7742C0), (4105, 0x6BCE2C), (4105, 0x6EE174),
    (4123, 0x6BF908), (4124, 0x6BFA88),
]

GLOBAL_RE = re.compile(r"0x7[cd][0-9a-f]{4}", re.I)


def scan(va, count=260):
    gate = []
    sends = []
    globals_ = []
    fields = []
    callees = []
    cur = va
    last_dx = None
    first_call_seen = False
    seen_ret = False
    for _ in range(count):
        ins = list(MD.disasm(rd(cur, 16), cur))
        if not ins:
            break
        i = ins[0]
        m, ops = i.mnemonic, i.op_str
        raw = " ".join("%02X" % x for x in i.bytes)
        if seen_ret and raw.startswith("55 8B EC"):
            break
        if m == "mov" and (ops.startswith("dx,") or ops.startswith("edx,")) and "0x" in ops:
            last_dx = ops.split(", ")[-1]
        if m == "call" and ("+ 0x250]" in ops or "+ 0x254]" in ops):
            slot = "250" if "0x250" in ops else "254"
            sends.append("SM=%s via vmt+0x%s @%06X" % (last_dx, slot, i.address))
        elif m == "call" and ops.startswith("0x"):
            t = int(ops, 0)
            if t not in callees:
                callees.append(t)
            first_call_seen = True
        # gate: conditional structure before first call
        if not first_call_seen and (m.startswith("cmp") or m.startswith("test")
                                    or m == "sub" or (m.startswith("j") and m != "jmp")):
            gate.append("%06X %s %s" % (i.address, m, ops))
        for g in GLOBAL_RE.findall(ops):
            if g.lower() not in [x.lower() for x in globals_]:
                globals_.append(g)
        fm = re.search(r"\+ (0x[0-9a-f]{3,})\]", ops)
        if fm and fm.group(1) not in fields:
            fields.append(fm.group(1))
        if m in ("ret", "retn"):
            seen_ret = True
        cur += i.size
    return gate, sends, globals_, fields, callees


for ident, va in WORKERS:
    gate, sends, globals_, fields, callees = scan(va)
    print("\n== CM %d  worker 0x%06X ==" % (ident, va))
    if gate:
        print("  GATE(pre-call):")
        for g in gate[:12]:
            print("     " + g)
    print("  SM SENDS : %s" % ("; ".join(sends) if sends else "(none)"))
    print("  GLOBALS  : %s" % (", ".join(globals_) if globals_ else "(none)"))
    print("  FIELDS   : %s" % (", ".join(fields) if fields else "(none)"))
    print("  CALLEES  : %s" % (", ".join("0x%06X" % c for c in callees)))
