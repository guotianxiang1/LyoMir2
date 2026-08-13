"""Independently restore native CM dispatch tree and split latter 50%.

Writes:
  D:/loym2/staging/m_cm_b/walk.txt
  D:/loym2/staging/m_cm_b/walk.json
  D:/loym2/staging/m_cm_b/split.txt
"""
import json
import os
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
ROOT = 0x6D805C
DEFAULT = 0x6DBC2C
OUTDIR = r"D:/loym2/staging/m_cm_b"
os.makedirs(OUTDIR, exist_ok=True)

data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False
_cache = {}


def ins_at(va):
    if va in _cache:
        return _cache[va]
    off = va - BASE
    r = None
    for i in md.disasm(data[off:off + 16], va):
        r = i
        break
    _cache[va] = r
    return r


def rd32(va):
    off = va - BASE
    return struct.unpack("<I", data[off:off + 4])[0]


def s32(v):
    return v - 0x100000000 if v >= 0x80000000 else v


def imm(ops):
    p = ops.split(", ")
    if len(p) != 2 or p[0] != "eax":
        return None
    t = p[1]
    try:
        return s32(int(t, 0))
    except ValueError:
        return None


cases = {}
trace = []
seen = set()
CC = {"je", "jz", "jne", "jnz", "jg", "jnle", "jl", "jnge", "jge", "jnl",
      "jle", "jng", "ja", "jnbe", "jb", "jnae", "jae", "jnb", "jbe", "jna"}


def rec(ident, handler, ev):
    if ident in cases and cases[ident][0] != handler:
        trace.append("!! CONFLICT %d: %08X vs %08X (at %08X)"
                     % (ident, cases[ident][0], handler, ev))
        return
    cases.setdefault(ident, (handler, ev))


def walk(va, acc, depth):
    if depth > 80:
        trace.append("!! depth limit at %08X" % va)
        return
    key = (va, acc)
    if key in seen:
        return
    seen.add(key)
    last = None
    for _ in range(500):
        i = ins_at(va)
        if i is None:
            trace.append("!! undecodable %08X" % va)
            return
        m, ops = i.mnemonic, i.op_str
        nxt = va + i.size
        v = imm(ops)

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
                    trace.append("!! je with unknown cmp at %08X" % va)
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
                trace.append("!! bare jmp-table %08X : %s acc=%d" % (va, ops, acc))
                return
            tgt = int(ops, 0)
            if tgt != DEFAULT:
                walk(tgt, acc, depth + 1)
            return

        trace.append("body %08X acc=%d : %s %s" % (va, acc, m, ops))
        return


walk(ROOT, 0, 0)

real = {k: v for k, v in cases.items() if v[0] != DEFAULT}
idents = sorted(real)
n = len(idents)
# Split must match the first-half agent's convention: (311+1)//2 = 156 handlers
# in the first half (ident 80..3283), leaving 155 in the latter (3284..4651).
mid = (n + 1) // 2
first = idents[:mid]
latter = idents[mid:]

out = ["ROOT %08X DEFAULT %08X  table-entries=%d  non-default=%d"
       % (ROOT, DEFAULT, len(cases), len(real)),
       "split: n=%d mid_index=%d first=%d..%d latter=%d..%d"
       % (n, mid, first[0], first[-1], latter[0], latter[-1]),
       "first count=%d latter count=%d" % (len(first), len(latter)),
       "",
       "=== NON-DEFAULT CASES ==="]
for k in idents:
    tag = "L" if k in latter else "F"
    out.append("%s %6d 0x%04X -> %08X   ev %08X" % (tag, k, k, real[k][0], real[k][1]))
out += ["", "=== DEFAULT-TARGET ENTRIES ==="]
out.append(" ".join(str(k) for k in sorted(cases) if cases[k][0] == DEFAULT))
out += ["", "=== TRACE ==="] + trace
open(os.path.join(OUTDIR, "walk.txt"), "w", encoding="utf-8").write("\n".join(out))
json.dump({
    "n": n, "mid": mid,
    "first_range": [first[0], first[-1]],
    "latter_range": [latter[0], latter[-1]],
    "first": first, "latter": latter,
    "real": {str(k): ["%08X" % v[0], "%08X" % v[1]] for k, v in real.items()},
    "placeholders": [k for k in sorted(cases) if cases[k][0] == DEFAULT],
}, open(os.path.join(OUTDIR, "walk.json"), "w"), indent=0)
print(out[0])
print(out[1])
print(out[2])
