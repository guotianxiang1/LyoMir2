"""Target list: gameplay backends reached only by CM idents the C# lacks.

Coverage test per callee (any hit => already covered, drop it):
  * its entry VA is quoted somewhere in the C# tree, or
  * a distinctive GBK string it references is quoted in the C# tree.

Remaining callees are ranked (refs desc, VA asc) and split in half; this agent
owns the latter half.

Writes staging/cmbe_b/targets.txt / targets.json
"""
import json
import os
import re
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
OUTDIR = r"D:/loym2/staging/cmbe_b"
CSROOT = r"D:/loym2/.claude/wt2/cmbe-b"
RTL_HI = 0x600000

data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

census = json.load(open(os.path.join(OUTDIR, "census.json")))
missing = set(json.load(open(os.path.join(OUTDIR, "csidents.json")))["missing"])
ident_calls = {int(k): [int(x, 16) for x in v]
               for k, v in census["ident_calls"].items()}


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
        return "33C0C3"
    if b[:7] == b"\x55\x8b\xec\x33\xc0\x5d\xc3":
        return "558BEC33C05DC3"
    if b[:7] == b"\x55\x8b\xec\x33\xc0\x5d\xc2":
        return "558BEC33C05DC2"
    if b[:1] == b"\xc3":
        return "C3"
    if b[:5] == b"\x55\x8b\xec\x5d\xc3":
        return "558BEC5DC3"
    return None


def sweep(va, cap=0x4000):
    """Bounded intra-function sweep; returns (end, strings, calls)."""
    seen = set()
    todo = [va]
    end = va
    strs, calls = [], []
    while todo:
        p = todo.pop()
        for _ in range(6000):
            if p in seen or not (BASE <= p < BASE + len(data)):
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
            for mm in re.finditer(r"0x[0-9a-f]{6,8}", ops):
                s = dstr(int(mm.group(0), 0))
                if s and s not in strs:
                    strs.append(s)
            if m == "call" and ops.startswith("0x"):
                t = int(ops, 0)
                if t not in calls:
                    calls.append(t)
            if m.startswith("j") and ops.startswith("0x"):
                t = int(ops, 0)
                if va <= t < va + cap:
                    todo.append(t)
                if m == "jmp":
                    break
            if m in ("ret", "retf"):
                break
            p += i.size
    return end - va, strs, calls


# --------------------------------------------------------------- C# corpus
corpus = []
cited = set()
pat = re.compile(r"0[xX]([0-9A-Fa-f]{6})")
for root, dirs, files in os.walk(CSROOT):
    dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj", "packages")]
    for f in files:
        if not f.lower().endswith((".cs", ".md")):
            continue
        try:
            t = open(os.path.join(root, f), encoding="utf-8", errors="replace").read()
        except OSError:
            continue
        corpus.append(t)
        for m in pat.finditer(t):
            cited.add(int(m.group(1), 16))
BIG = "\n".join(corpus)


def distinctive(s):
    if len(s) < 4:
        return False
    if not re.search(r"[\u4e00-\u9fff]", s) and not re.match(r"^[@A-Za-z_][\w@]{4,}$", s):
        return False
    # reject mojibake: decoded GBK noise from code bytes
    cjk = len(re.findall(r"[\u4e00-\u9fff]", s))
    return cjk >= 3 or bool(re.match(r"^[@A-Za-z_][\w@]{4,}$", s))


rows = []
for va in sorted({c for i in missing for c in ident_calls.get(i, [])}):
    if va < RTL_HI:
        continue
    ids = sorted(i for i in missing if va in ident_calls.get(i, []))
    size, strs, calls = sweep(va)
    ds = [s for s in strs if distinctive(s)]
    hit = [s for s in ds if s in BIG]
    rows.append({
        "va": va, "n": len(ids), "idents": ids, "size": size,
        "stub": stub_of(va), "cited": va in cited,
        "strings": ds[:10], "strhit": hit[:6],
        "calls": ["%06X" % c for c in calls[:24]],
    })

open_rows = [r for r in rows
             if not r["stub"] and not r["cited"] and not r["strhit"]]
open_rows.sort(key=lambda r: (-r["n"], r["va"]))
mid = (len(open_rows) + 1) // 2
for k, r in enumerate(open_rows):
    r["half"] = "A" if k < mid else "B"

json.dump(rows, open(os.path.join(OUTDIR, "targets.json"), "w"), indent=1)
out = ["missing idents=%d ; gameplay callees=%d ; stub=%d ; VA-cited=%d ; str-hit=%d ; OPEN=%d"
       % (len(missing), len(rows), sum(1 for r in rows if r["stub"]),
          sum(1 for r in rows if r["cited"]),
          sum(1 for r in rows if r["strhit"] and not r["cited"]), len(open_rows)),
       "split A=%d B=%d" % (mid, len(open_rows) - mid), "",
       "=== OPEN, ranked ==="]
for r in open_rows:
    out.append("%s %08X refs=%d size=%-5d idents=%s"
               % (r["half"], r["va"], r["n"], r["size"],
                  ",".join(str(x) for x in r["idents"])))
    if r["strings"]:
        out.append("      str: %s" % " | ".join(r["strings"][:6]))
    out.append("      calls: %s" % " ".join(r["calls"]))
out += ["", "=== DROPPED (covered) ==="]
for r in rows:
    if r["stub"] or r["cited"] or r["strhit"]:
        why = ("stub=" + r["stub"]) if r["stub"] else ("VA-cited" if r["cited"]
                                                       else "str:" + r["strhit"][0])
        out.append("  %08X idents=%s  %s"
                   % (r["va"], ",".join(str(x) for x in r["idents"]), why))
open(os.path.join(OUTDIR, "targets.txt"), "w", encoding="utf-8").write("\n".join(out))
sys.stdout.reconfigure(encoding="utf-8")
print(out[0])
print(out[1])
