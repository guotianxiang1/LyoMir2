"""Cross-check CM downstream callees against the C# tree.

A callee counts as "already covered" when its VA appears anywhere in the C#
sources (the project annotates native provenance as 0xXXXXXX in comments).
Emits the uncovered list ranked by distinct-ident reference count, then splits
it in half so the two backend agents do not collide.

Writes staging/cmbe_b/gap.txt / gap.json
"""
import json
import os
import re
import struct
import subprocess
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
OUTDIR = r"D:/loym2/staging/cmbe_b"
CSROOT = r"D:/loym2/.claude/wt2/cmbe-b"
RTL_HI = 0x600000          # below this is Delphi RTL / VCL / third-party units

data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

census = json.load(open(os.path.join(OUTDIR, "census.json")))
refs = {int(k, 16): v for k, v in census["refs"].items()}


def rd32(va):
    o = va - BASE
    if o < 0 or o + 4 > len(data):
        return None
    return struct.unpack("<I", data[o:o + 4])[0]


def dstr(va):
    if va is None or va - BASE - 4 < 0 or va - BASE >= len(data):
        return None
    n = rd32(va - 4)
    if n is None or not (1 <= n <= 600):
        return None
    raw = data[va - BASE:va - BASE + n]
    if b"\x00" in raw:
        return None
    try:
        return raw.decode("gbk")
    except UnicodeDecodeError:
        return None


def stub_of(va):
    b = data[va - BASE:va - BASE + 16]
    if b[:3] == b"\x33\xc0\xc3":
        return "33C0C3 const-false"
    if b[:7] == b"\x55\x8b\xec\x33\xc0\x5d\xc3":
        return "558BEC33C05DC3 const-false"
    if b[:7] == b"\x55\x8b\xec\x33\xc0\x5d\xc2":
        return "558BEC33C05DC2 const-false"
    if b[:1] == b"\xc3":
        return "C3 empty"
    if b[:5] == b"\x55\x8b\xec\x5d\xc3":
        return "558BEC5DC3 empty"
    return None


def fn_size(va, cap=20000):
    """Crude extent: scan to the deepest ret reachable by linear/branch sweep."""
    end = va
    seen = set()
    todo = [va]
    while todo:
        p = todo.pop()
        for _ in range(4000):
            if p in seen or p - BASE >= len(data):
                break
            seen.add(p)
            i = None
            for x in md.disasm(data[p - BASE:p - BASE + 16], p):
                i = x
                break
            if i is None:
                break
            end = max(end, p + i.size)
            m, ops = i.mnemonic, i.op_str
            if m.startswith("j") and ops.startswith("0x"):
                t = int(ops, 0)
                if va <= t < va + cap:
                    todo.append(t)
                if m == "jmp":
                    break
            if m in ("ret", "retf"):
                break
            p += i.size
    return end - va


def strings_of(va, limit=4000):
    out = []
    p = va
    n = 0
    while n < limit and p - BASE < len(data):
        n += 1
        i = None
        for x in md.disasm(data[p - BASE:p - BASE + 16], p):
            i = x
            break
        if i is None:
            break
        for mm in re.finditer(r"0x[0-9a-f]{6,8}", i.op_str):
            s = dstr(int(mm.group(0), 0))
            if s and s not in out:
                out.append(s)
        if i.mnemonic in ("ret", "retf"):
            break
        p += i.size
    return out[:8]


# ------------------------------------------------- which VAs the C# tree cites
cited = set()
pat = re.compile(rb"0[xX]([0-9A-Fa-f]{6})")
for root, dirs, files in os.walk(CSROOT):
    dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj", "packages")]
    for f in files:
        if not f.lower().endswith((".cs", ".md", ".txt", ".json", ".py")):
            continue
        try:
            b = open(os.path.join(root, f), "rb").read()
        except OSError:
            continue
        for m in pat.finditer(b):
            try:
                cited.add(int(m.group(1), 16))
            except ValueError:
                pass

rows = []
for va, ids in refs.items():
    if va < RTL_HI:
        continue
    rows.append({
        "va": va, "n": len(ids), "idents": ids,
        "cited": va in cited,
        "stub": stub_of(va),
        "size": fn_size(va),
        "strings": strings_of(va),
    })
rows.sort(key=lambda r: (-r["n"], r["va"]))

gap = [r for r in rows if not r["cited"] and not r["stub"]]
mid = (len(gap) + 1) // 2
for k, r in enumerate(gap):
    r["half"] = "A" if k < mid else "B"

json.dump(rows, open(os.path.join(OUTDIR, "gap.json"), "w"), indent=1)
out = ["gameplay callees (VA >= %06X): %d ; cited-in-C#: %d ; native-stub: %d ; GAP: %d"
       % (RTL_HI, len(rows), sum(1 for r in rows if r["cited"]),
          sum(1 for r in rows if r["stub"]), len(gap)),
       "gap split: first half A=%d, latter half B=%d" % (mid, len(gap) - mid), ""]
out.append("=== GAP (uncited, non-stub) ranked ===")
for r in gap:
    out.append("%s %08X refs=%2d size=%-6d idents=%s" %
               (r["half"], r["va"], r["n"], r["size"],
                ",".join(str(x) for x in r["idents"][:14])))
    if r["strings"]:
        out.append("      str: %s" % " | ".join(r["strings"]))
out.append("")
out.append("=== CITED (already have C# provenance) ===")
for r in rows:
    if r["cited"]:
        out.append("  %08X refs=%2d idents=%s" %
                   (r["va"], r["n"], ",".join(str(x) for x in r["idents"][:14])))
out.append("")
out.append("=== NATIVE STUBS ===")
for r in rows:
    if r["stub"]:
        out.append("  %08X %-24s idents=%s" %
                   (r["va"], r["stub"], ",".join(str(x) for x in r["idents"][:14])))
open(os.path.join(OUTDIR, "gap.txt"), "w", encoding="utf-8").write("\n".join(out))
sys.stdout.reconfigure(encoding="utf-8")
print(out[0])
print(out[1])
