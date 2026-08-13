"""Independent CM dispatch walk + downstream callee census.

Stage 1: restore ident -> handler VA from the binary-search tree rooted at
         0x6D805C (ident is word [ebp-0x34 + 4]).
Stage 2: for every handler arm do a bounded CFG sweep, collect direct call
         targets (depth 1) plus a second level for thin forwarder frames.
Stage 3: rank callees by how many distinct CM idents reach them.

Writes staging/cmbe_b/census.json + census.txt
"""
import json
import os
import re
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
ROOT = 0x6D805C
DEFAULT = 0x6DBC2C
CODE_LO, CODE_HI = 0x401000, 0x7A10D0
OUTDIR = r"D:/loym2/staging/cmbe_b"
os.makedirs(OUTDIR, exist_ok=True)

data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False
_c = {}


def ins_at(va):
    if va in _c:
        return _c[va]
    o = va - BASE
    r = None
    if 0 <= o < len(data):
        for i in md.disasm(data[o:o + 16], va):
            r = i
            break
    _c[va] = r
    return r


def rd32(va):
    o = va - BASE
    if o < 0 or o + 4 > len(data):
        return None
    return struct.unpack("<I", data[o:o + 4])[0]


def s32(v):
    return v - 0x100000000 if v >= 0x80000000 else v


CC = {"je", "jz", "jne", "jnz", "jg", "jnle", "jl", "jnge", "jge", "jnl",
      "jle", "jng", "ja", "jnbe", "jb", "jnae", "jae", "jnb", "jbe", "jna",
      "js", "jns", "jo", "jno", "jp", "jnp"}

# ---------------------------------------------------------------- stage 1
cases = {}
trace = []
seen = set()


def imm_eax(ops):
    p = ops.split(", ")
    if len(p) != 2 or p[0] != "eax":
        return None
    try:
        return s32(int(p[1], 0))
    except ValueError:
        return None


def rec(ident, handler, ev):
    if ident in cases and cases[ident][0] != handler:
        trace.append("!! CONFLICT %d: %08X vs %08X at %08X"
                     % (ident, cases[ident][0], handler, ev))
        return
    cases.setdefault(ident, (handler, ev))


def walk(va, acc, depth):
    if depth > 90:
        trace.append("!! depth %08X" % va)
        return
    key = (va, acc)
    if key in seen:
        return
    seen.add(key)
    last = None
    for _ in range(600):
        i = ins_at(va)
        if i is None:
            trace.append("!! undecodable %08X" % va)
            return
        m, ops = i.mnemonic, i.op_str
        nxt = va + i.size
        v = imm_eax(ops)
        if m == "cmp" and v is not None:
            last = v
            va = nxt
            continue
        if m == "sub" and v is not None:
            acc += v
            last = 0
            va = nxt
            continue
        if m == "add" and v is not None:
            acc -= v
            last = 0
            va = nxt
            continue
        if m == "dec" and ops == "eax":
            acc += 1
            last = 0
            va = nxt
            continue
        if m == "inc" and ops == "eax":
            acc -= 1
            last = 0
            va = nxt
            continue
        if m == "test" and ops == "eax, eax":
            last = 0
            va = nxt
            continue
        if m == "mov" and ops == "eax, dword ptr [ebp - 0x34]":
            va = nxt
            continue
        if m == "movzx" and ops == "eax, word ptr [eax + 4]":
            acc = 0
            last = None
            va = nxt
            continue
        if m in CC:
            tgt = int(ops, 0)
            if m in ("je", "jz"):
                if last is None:
                    trace.append("!! je unknown cmp %08X" % va)
                else:
                    rec(last + acc, tgt, va)
                va = nxt
                continue
            ni = ins_at(nxt)
            if m in ("ja", "jnbe") and last is not None and ni is not None \
                    and ni.mnemonic == "jmp" and "*4" in ni.op_str:
                tbl = int(ni.op_str.split("+")[-1].replace("]", "").strip(), 0)
                for k in range(last + 1):
                    rec(k + acc, rd32(tbl + 4 * k), tbl + 4 * k)
                trace.append("tbl %08X base=%d n=%d default=%08X"
                             % (tbl, acc, last + 1, tgt))
                return
            if tgt != DEFAULT:
                walk(tgt, acc, depth + 1)
            va = nxt
            continue
        if m == "jmp":
            if "*4" in ops:
                trace.append("!! bare jmp-table %08X %s acc=%d" % (va, ops, acc))
                return
            tgt = int(ops, 0)
            if tgt != DEFAULT:
                walk(tgt, acc, depth + 1)
            return
        trace.append("body %08X acc=%d : %s %s" % (va, acc, m, ops))
        return


walk(ROOT, 0, 0)
real = {k: v for k, v in cases.items() if v[0] != DEFAULT}

# ---------------------------------------------------------------- stage 2
# Arm sweep: follow the arm's own basic blocks; stop at the dispatcher default
# label, at ret, and at any jump that lands back inside the compare tree.
TREE_LO, TREE_HI = 0x6D805C, 0x6DBC2C


def arm_calls(start, budget=4000):
    calls = []
    visited = set()
    todo = [start]
    n = 0
    while todo and n < budget:
        va = todo.pop()
        while n < budget:
            n += 1
            if va in visited or not (CODE_LO <= va <= CODE_HI):
                break
            visited.add(va)
            i = ins_at(va)
            if i is None:
                break
            m, ops = i.mnemonic, i.op_str
            if m == "call" and ops.startswith("0x"):
                t = int(ops, 0)
                if t not in calls:
                    calls.append(t)
            if m in CC:
                if ops.startswith("0x"):
                    t = int(ops, 0)
                    if t != DEFAULT and not (TREE_LO <= t < TREE_HI):
                        todo.append(t)
                va += i.size
                continue
            if m == "jmp":
                if ops.startswith("0x"):
                    t = int(ops, 0)
                    if t == DEFAULT or (TREE_LO <= t < TREE_HI):
                        break
                    va = t
                    continue
                break
            if m in ("ret", "retf", "retn"):
                break
            va += i.size
    return calls, visited


# RTL/system helpers that are not gameplay backends
RTL_HI = 0x6A0000

ident_calls = {}
for ident, (h, _ev) in sorted(real.items()):
    cs, _ = arm_calls(h)
    ident_calls[ident] = cs

# ---------------------------------------------------------------- stage 3
from collections import defaultdict
refs = defaultdict(set)
for ident, cs in ident_calls.items():
    for c in cs:
        refs[c].add(ident)

rows = []
for c, ids in refs.items():
    rows.append((len(ids), c, sorted(ids)))
rows.sort(key=lambda r: (-r[0], r[1]))

json.dump({
    "root": "%08X" % ROOT,
    "default": "%08X" % DEFAULT,
    "n_idents": len(real),
    "real": {str(k): "%08X" % v[0] for k, v in real.items()},
    "ident_calls": {str(k): ["%08X" % x for x in v] for k, v in ident_calls.items()},
    "refs": {"%08X" % c: sorted(ids) for c, ids in refs.items()},
}, open(os.path.join(OUTDIR, "census.json"), "w"), indent=0)

out = ["ROOT %08X DEFAULT %08X idents=%d callees=%d"
       % (ROOT, DEFAULT, len(real), len(refs)), ""]
for n, c, ids in rows:
    out.append("%08X  refs=%3d  %s" % (c, n, ",".join(str(x) for x in ids[:24])))
out += ["", "=== TRACE ==="] + trace
open(os.path.join(OUTDIR, "census.txt"), "w", encoding="utf-8").write("\n".join(out))
sys.stdout.reconfigure(encoding="utf-8")
print(out[0])
print("top: %s" % ["%08X:%d" % (c, n) for n, c, _ in rows[:12]])
