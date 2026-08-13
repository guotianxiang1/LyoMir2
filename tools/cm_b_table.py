"""Build the restoration table for every latter-half CM handler (3284..4651).

For each ident emits: entry VA, packet-header fields actually read, gate
conditions (compare + branch that reaches the dispatcher default label),
callees, SM idents sent through the unicast slots, and an empty-stub
fingerprint for each callee.

Header field offsets come from the dispatcher prologue documented by the
first-half agent: [ebp-0x34] is the 12-byte message record, [ebp-4] is self,
[ebp-8] is the body string and si/edi is the body length.

Writes staging/m_cm_b/table.md / table.json / table_bodies.txt
"""
import json
import os
import re
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
DEFAULT = 0x6DBC2C
CODE_LO, CODE_HI = 0x401000, 0x7A10D0
OUTDIR = r"D:/loym2/staging/m_cm_b"
CSROOT = r"D:/loym2/.claude/wt2/m-cm-b"
LO, HI = 3284, 4651

data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

FIELD = {0x00: "Recog", 0x04: "Ident", 0x06: "Param", 0x08: "Tag", 0x0A: "Series"}
CC = {"je", "jz", "jne", "jnz", "jg", "jnle", "jl", "jnge", "jge", "jnl",
      "jle", "jng", "ja", "jnbe", "jb", "jnae", "jae", "jnb", "jbe", "jna"}


def rd32(va):
    o = va - BASE
    if o < 0 or o + 4 > len(data):
        return None
    return struct.unpack("<I", data[o:o + 4])[0]


def dstr(va):
    """Read a Delphi long string (dword length prefix at ptr-4), GBK."""
    if va is None or va - BASE - 4 < 0 or va - BASE >= len(data):
        return None
    n = rd32(va - 4)
    if n is None or not (1 <= n <= 400):
        return None
    raw = data[va - BASE:va - BASE + n]
    if b"\x00" in raw:
        return None
    try:
        return raw.decode("gbk")
    except UnicodeDecodeError:
        return None


def ins_at(va):
    o = va - BASE
    if o < 0 or o >= len(data):
        return None
    for i in md.disasm(data[o:o + 16], va):
        return i
    return None


STUBS = [
    (b"\x33\xc0\xc3", "33C0C3"),
    (b"\x31\xc0\xc3", "31C0C3"),
    (b"\xc3", "C3"),
    (b"\x55\x8b\xec\x5d\xc3", "558BEC5DC3"),
    (b"\x55\x8b\xec\x33\xc0\x5d\xc3", "558BEC33C05DC3"),
]


def stub_of(va):
    """Both empty-body shapes: constant-false and do-nothing."""
    b = data[va - BASE:va - BASE + 16]
    if b[:3] == b"\x33\xc0" + b"\xc3"[:1]:
        return "33C0C3 const-false"
    if b[:7] == b"\x55\x8b\xec\x33\xc0\x5d\xc3":
        return "558BEC33C05DC3 const-false"
    if b[:7] == b"\x55\x8b\xec\x33\xc0\x5d\xc2":
        return "558BEC33C05DC2%02X%02X const-false" % (b[7], b[8])
    if b[:1] == b"\xc3":
        return "C3 empty"
    if b[:5] == b"\x55\x8b\xec\x5d\xc3":
        return "558BEC5DC3 empty"
    if b[:10] == b"\x55\x8b\xec\x51\x89\x45\xfc\x59\x5d\xc3":
        return "558BEC5189 45FC595DC3 empty"
    return None


def profile(start, maxins=600):
    """Linear+branch sweep of one handler arm, stopping at the default label."""
    fields, gates, calls, sends, writes = [], [], [], [], []
    body = []
    seen = set()
    todo = [start]
    last_cmp = None
    pend_dx = None
    while todo:
        va = todo.pop(0)
        n = 0
        while n < maxins:
            n += 1
            if va in seen or not (CODE_LO <= va <= CODE_HI):
                break
            seen.add(va)
            i = ins_at(va)
            if i is None:
                break
            m, ops = i.mnemonic, i.op_str
            hx = i.bytes.hex().upper()
            ann = ""
            for mm in re.finditer(r"0x[0-9a-f]{6,8}", ops):
                s = dstr(int(mm.group(0), 0))
                if s:
                    ann = '   ; "%s"' % s
                    break
            body.append("%08X  %-22s %s %s%s" % (va, hx, m, ops, ann))

            # --- packet header fields, read off [ebp-0x34] into a register ---
            fm = re.search(r"\[(eax|edx|ecx|ebx|esi|edi) \+ (0x[0-9a-f]+)\]", ops)
            if fm and m in ("mov", "movzx", "cmp", "movsx", "push"):
                off = int(fm.group(2), 0)
                if off in FIELD and FIELD[off] not in fields:
                    fields.append(FIELD[off])
            if re.search(r"(mov|movzx|push).*\[(eax|edx|ecx|ebx)\]$", ops) \
                    and "ebp" not in ops and "Recog" not in fields:
                fields.append("Recog")
            if re.search(r"\bsi\b", ops) and "BodyLen" not in fields:
                fields.append("BodyLen")
            if "ebp - 8" in ops and "sMsg" not in fields:
                fields.append("sMsg")

            if m in ("cmp", "test"):
                last_cmp = "%08X %s %s" % (va, m, ops)
            if m == "mov" and re.match(r"^(dx|edx|cx|ecx), 0x[0-9a-f]+$", ops):
                pend_dx = int(ops.split(", ")[1], 0)

            wm = re.search(
                r"mov (?:byte|word|dword) ptr \[(eax|edx|ebx|esi|edi) \+ (0x[0-9a-f]+)\], ",
                ops)
            if wm:
                w = "%s" % wm.group(2)
                if w not in writes:
                    writes.append(w)

            if m == "call":
                if "0x250" in ops or "0x254" in ops:
                    slot = "0x250" if "0x250" in ops else "0x254"
                    sends.append((pend_dx, slot, "%08X" % va))
                elif ops.startswith("0x"):
                    t = int(ops, 0)
                    if t not in calls:
                        calls.append(t)
                pend_dx = None

            if m in CC:
                tgt = int(ops, 0) if ops.startswith("0x") else None
                if tgt == DEFAULT and last_cmp:
                    g = "%s -> %s default" % (last_cmp, m)
                    if g not in gates:
                        gates.append(g)
                elif tgt is not None and CODE_LO <= tgt <= CODE_HI:
                    todo.append(tgt)
                va += i.size
                continue
            if m == "jmp":
                if ops.startswith("0x"):
                    t = int(ops, 0)
                    if t == DEFAULT:
                        break
                    va = t
                    continue
                break
            if m in ("ret", "retf"):
                break
            va += i.size
    return {
        "fields": fields, "gates": gates, "writes": writes,
        "calls": ["%08X" % c for c in calls],
        "sends": [[s[0], s[1], s[2]] for s in sends],
        "stubs": {"%08X" % c: stub_of(c) for c in calls if stub_of(c)},
        "body": body,
    }


# --------------------------------------------------------------------------
walk = json.load(open(os.path.join(OUTDIR, "walk.json")))
real = {int(k): int(v[0], 16) for k, v in walk["real"].items()}
mine = [i for i in sorted(real) if LO <= i <= HI]
traffic = {r["ident"]: r for r in json.load(open(os.path.join(OUTDIR, "traffic.json")))}

rows = []
bodies = []
for ident in mine:
    h = real[ident]
    p = profile(h)
    t = traffic.get(ident, {})
    rows.append({
        "ident": ident, "handler": "%08X" % h,
        "client": t.get("client", 0), "srv": t.get("srv", 0),
        "state": t.get("state", "?"), "cs": t.get("cs"),
        "fields": p["fields"], "gates": p["gates"], "calls": p["calls"],
        "sends": p["sends"], "stubs": p["stubs"], "writes": p["writes"],
    })
    bodies.append("=" * 74)
    bodies.append("CM %d (0x%04X)  handler %08X  client=%d  %s  %s"
                  % (ident, ident, h, t.get("client", 0), t.get("state", "?"),
                     t.get("cs") or ""))
    bodies.append("fields=%s" % (",".join(p["fields"]) or "-"))
    for g in p["gates"]:
        bodies.append("gate  %s" % g)
    bodies.append("calls=%s  stubs=%s" % (",".join(p["calls"]) or "-", p["stubs"]))
    bodies.append("sends=%s" % p["sends"])
    bodies.extend(p["body"])

json.dump(rows, open(os.path.join(OUTDIR, "table.json"), "w"), indent=1)
open(os.path.join(OUTDIR, "table_bodies.txt"), "w", encoding="utf-8").write(
    "\n".join(bodies))

hdr = ("| ident | handler VA | 线上 | 状态 | 包头字段 | 门条件 | 被调函数 | 发出 SM |\n"
       "|---|---|---|---|---|---|---|---|")
lines = [hdr]
for r in rows:
    g = "<br>".join(x.replace("|", "\\|") for x in r["gates"]) or "—"
    c = ", ".join("0x" + x for x in r["calls"]) or "—"
    s = ", ".join(str(x[0]) for x in r["sends"] if x[0] is not None) or "—"
    lines.append("| %d | 0x%s | %d | %s | %s | %s | %s | %s |"
                 % (r["ident"], r["handler"], r["client"], r["state"],
                    ",".join(r["fields"]) or "—", g, c, s))
open(os.path.join(OUTDIR, "table.md"), "w", encoding="utf-8").write("\n".join(lines))

sys.stdout.reconfigure(encoding="utf-8")
print("rows=%d" % len(rows))
print("with stubs: %s" % [r["ident"] for r in rows if r["stubs"]])
print("with sends: %d" % sum(1 for r in rows if r["sends"]))
print("with gates: %d" % sum(1 for r in rows if r["gates"]))
