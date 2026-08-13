"""CM batch-3 agent: independently restore the native CM/RM dispatch tree.

Does NOT trust any earlier agent's ROOT/DEFAULT constants.  Instead it:
  1. locates every `jmp dword ptr [reg*4 + T]` in the image whose table T holds
     >= 8 plausible code pointers, and every long `cmp eax,imm / je` ladder;
  2. picks the tree whose entry loads the 16-bit ident out of the 12-byte
     TProcessMessage record ([ebp-0x34] + 4) -- that is TPlayObject.Operate;
  3. walks it with an accumulator so `sub eax,imm` / `dec eax` re-basing is
     folded back into the true ident;
  4. reports the shared convergence label (the switch default) separately.

Writes staging/m_cm_c/walk.txt + walk.json
"""
import json
import os
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
OUTDIR = r"D:/loym2/staging/m_cm_c"
os.makedirs(OUTDIR, exist_ok=True)

data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False
_cache = {}

CODE_LO, CODE_HI = 0x401000, 0x7A10D0
CC = {"je", "jz", "jne", "jnz", "jg", "jnle", "jl", "jnge", "jge", "jnl",
      "jle", "jng", "ja", "jnbe", "jb", "jnae", "jae", "jnb", "jbe", "jna"}


def ins_at(va):
    if va in _cache:
        return _cache[va]
    off = va - BASE
    r = None
    if 0 <= off < len(data):
        for i in md.disasm(data[off:off + 16], va):
            r = i
            break
    _cache[va] = r
    return r


def rd32(va):
    off = va - BASE
    if off < 0 or off + 4 > len(data):
        return None
    return struct.unpack("<I", data[off:off + 4])[0]


def s32(v):
    return v - 0x100000000 if v >= 0x80000000 else v


def imm_eax(ops):
    p = ops.split(", ")
    if len(p) != 2 or p[0] != "eax":
        return None
    try:
        return s32(int(p[1], 0))
    except ValueError:
        return None


# ---------------------------------------------------------------- discovery
def find_ident_switch():
    """Scan for `movzx e?x, word ptr [e?x + 4]` followed within 64 bytes by the
    head of a compare ladder / jump table.  Return candidate entry VAs."""
    cands = []
    # 0F B7 40 04 = movzx eax, word ptr [eax+4]
    pat = b"\x0f\xb7\x40\x04"
    start = 0
    while True:
        k = data.find(pat, start)
        if k < 0:
            break
        start = k + 1
        va = BASE + k
        if not (CODE_LO <= va <= CODE_HI):
            continue
        # count how many cmp/je pairs follow in the next 512 bytes
        p = va + 4
        n = 0
        for _ in range(160):
            i = ins_at(p)
            if i is None:
                break
            if i.mnemonic == "cmp" and imm_eax(i.op_str) is not None:
                n += 1
            if i.mnemonic == "jmp" and "*4" in i.op_str:
                n += 40
                break
            p += i.size
        if n >= 12:
            cands.append((n, va))
    cands.sort(reverse=True)
    return cands


# ---------------------------------------------------------------- walk
def build(root):
    cases = {}
    trace = []
    seen = set()
    default_votes = {}

    def rec(ident, handler, ev):
        if ident in cases and cases[ident][0] != handler:
            trace.append("!! CONFLICT %d: %08X vs %08X (at %08X)"
                         % (ident, cases[ident][0], handler, ev))
            return
        cases.setdefault(ident, (handler, ev))

    def walk(va, acc, depth):
        if depth > 90:
            trace.append("!! depth limit at %08X" % va)
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
                try:
                    tgt = int(ops, 0)
                except ValueError:
                    trace.append("!! indirect cc %08X : %s" % (va, ops))
                    return
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
                    default_votes[tgt] = default_votes.get(tgt, 0) + (last + 1)
                    for k in range(last + 1):
                        rec(k + acc, rd32(tbl + 4 * k), tbl + 4 * k)
                    trace.append("tbl %08X base=%d n=%d default=%08X"
                                 % (tbl, acc, last + 1, tgt))
                    return
                walk(tgt, acc, depth + 1)
                va = nxt
                continue

            if m == "jmp":
                if "*4" in ops:
                    trace.append("!! bare jmp-table %08X : %s acc=%d" % (va, ops, acc))
                    return
                tgt = int(ops, 0)
                default_votes[tgt] = default_votes.get(tgt, 0) + 1
                walk(tgt, acc, depth + 1)
                return

            trace.append("body %08X acc=%d : %s %s" % (va, acc, m, ops))
            return

    walk(root, 0, 0)
    return cases, trace, default_votes


cands = find_ident_switch()
sys.stdout.reconfigure(encoding="utf-8")
print("ident-switch candidates (score, VA):")
for n, va in cands[:8]:
    print("  %4d  %08X" % (n, va))

# Widest tree wins: the CM/RM dispatcher is by far the largest switch on the
# TProcessMessage.Ident field.
best = None
for n, va in cands[:8]:
    c, t, dv = build(va)
    print("  root %08X -> %d table entries" % (va, len(c)))
    if best is None or len(c) > len(best[1]):
        best = (va, c, t, dv)

ROOT, cases, trace, dv = best
# The default label is the target every arm-less ident lands on.  It is the
# most-voted fallthrough target AND appears as the value of many table slots.
slotcount = {}
for k, (h, _e) in cases.items():
    slotcount[h] = slotcount.get(h, 0) + 1
DEFAULT = max(slotcount, key=lambda k: slotcount[k])
print("ROOT=%08X DEFAULT=%08X (%d slots) default_votes=%s"
      % (ROOT, DEFAULT, slotcount[DEFAULT],
         sorted(dv.items(), key=lambda x: -x[1])[:3]))

real = {k: v for k, v in cases.items() if v[0] != DEFAULT}
idents = sorted(real)
out = ["ROOT %08X DEFAULT %08X  table-entries=%d  non-default=%d"
       % (ROOT, DEFAULT, len(cases), len(real)),
       "", "=== NON-DEFAULT CASES ==="]
for k in idents:
    out.append("%6d 0x%04X -> %08X   ev %08X" % (k, k, real[k][0], real[k][1]))
out += ["", "=== DEFAULT-TARGET ENTRIES (native no-op) ==="]
out.append(" ".join(str(k) for k in sorted(cases) if cases[k][0] == DEFAULT))
out += ["", "=== TRACE ==="] + trace
open(os.path.join(OUTDIR, "walk.txt"), "w", encoding="utf-8").write("\n".join(out))
json.dump({
    "root": "%08X" % ROOT, "default": "%08X" % DEFAULT,
    "real": {str(k): ["%08X" % v[0], "%08X" % v[1]] for k, v in real.items()},
    "placeholders": [k for k in sorted(cases) if cases[k][0] == DEFAULT],
}, open(os.path.join(OUTDIR, "walk.json"), "w"), indent=0)
print(out[0])
