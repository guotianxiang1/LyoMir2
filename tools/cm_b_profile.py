"""Profile latter-half (ident 3283..4651) missing CM handlers.

Dumps handler bodies + first-level callees, SM sends via [obj+0x250]/[0x254],
stub detection (xor eax,eax; ret), and player field writes.
"""
import json
import os
import re
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
DEFAULT = 0x6DBC2C
CSROOT = r"D:/loym2/.claude/wt2/m-cm-b"
OUTDIR = r"D:/loym2/staging/m_cm_b"
os.makedirs(OUTDIR, exist_ok=True)

data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

walk = json.load(open(os.path.join(OUTDIR, "walk.json")))
latter = walk["latter"]
real = {int(k): v for k, v in walk["real"].items()}

# C# CM consts + case labels
consts = {}
g2 = os.path.join(CSROOT, "SystemModule", "Grobal2.cs")
for ln in open(g2, encoding="utf-8-sig"):
    m = re.search(r"\b(CM_[A-Za-z0-9_]+)\s*=\s*(-?\d+)\s*;", ln)
    if m:
        consts[m.group(1)] = int(m.group(2))

hits = {}
for base, dirs, files in os.walk(os.path.join(CSROOT, "GameSvr")):
    for f in files:
        if not f.endswith(".cs"):
            continue
        p = os.path.join(base, f)
        rel = os.path.relpath(p, CSROOT).replace("\\", "/")
        for n, ln in enumerate(open(p, encoding="utf-8-sig", errors="replace"), 1):
            for m in re.finditer(r"case\s+(?:Grobal2\.)?(CM_[A-Za-z0-9_]+)\s*:", ln):
                name = m.group(1)
                if name in consts:
                    hits.setdefault(consts[name], []).append((name, "%s:%d" % (rel, n)))

FIELD = {0: "Recog", 4: "Ident", 6: "Param", 8: "Tag", 0x0A: "Series"}


def ins_at(va):
    off = va - BASE
    for i in md.disasm(data[off:off + 16], va):
        return i
    return None


def disasm_range(va, nbytes=0x200):
    off = va - BASE
    lines = []
    for i in md.disasm(data[off:off + nbytes], va):
        lines.append((i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str, i.size))
    return lines


def is_stub(va):
    """Detect xor eax,eax; ret / ret N, possibly with push ebp/mov ebp,esp/pop ebp."""
    b = data[va - BASE:va - BASE + 16]
    # 33 C0 C3
    if b[:3] == b"\x33\xc0\xc3":
        return True, "33C0C3"
    # 55 8B EC 33 C0 5D C3
    if b[:7] == b"\x55\x8b\xec\x33\xc0\x5d\xc3":
        return True, "558BEC33C05DC3"
    # 55 8B EC 33 C0 5D C2 xx 00
    if b[:7] == b"\x55\x8b\xec\x33\xc0\x5d\xc2":
        return True, "558BEC33C05DC2%02X%02X" % (b[7], b[8] if len(b) > 8 else 0)
    # 33 C0 C2 xx 00
    if b[:2] == b"\x33\xc0" and b[2] == 0xC2:
        return True, "33C0C2%02X%02X" % (b[3], b[4] if len(b) > 4 else 0)
    # 31 C0 C3
    if b[:3] == b"\x31\xc0\xc3":
        return True, "31C0C3"
    return False, ""


def profile_handler(start, limit=0x500):
    fields, sends, calls, writes, imms_dx = [], [], [], [], []
    send_slots = []
    stub_callees = []
    lines = []
    va = start
    pend_dx = None
    n = 0
    visited = set()
    while n < 400:
        n += 1
        if va in visited:
            break
        visited.add(va)
        ins = ins_at(va)
        if ins is None:
            break
        m, ops = ins.mnemonic, ins.op_str
        hx = ins.bytes.hex().upper()
        lines.append("%08X  %-22s %s %s" % (va, hx, m, ops))

        mm = re.search(r"(?:word|byte) ptr \[eax \+ (0x[0-9a-f]+)\]", ops)
        if mm and m in ("movzx", "mov", "cmp"):
            off = int(mm.group(1), 0)
            if off in FIELD and FIELD[off] not in fields:
                fields.append(FIELD[off])
        if m == "mov" and ops.startswith("ecx, dword ptr [eax]"):
            if "Recog" not in fields:
                fields.append("Recog")
        if m == "mov" and re.match(r"^eax, dword ptr \[eax\]$", ops):
            if "Recog" not in fields:
                fields.append("Recog")
        # Recog via [eax] without +off
        if "[eax]" in ops and "+ " not in ops and m in ("mov", "movzx"):
            if "dword ptr [eax]" in ops or ops.endswith("[eax]"):
                if "Recog" not in fields and "ebp" not in ops:
                    pass  # already handled

        if m == "mov" and re.match(r"^(dx|edx|cx|ecx), 0x[0-9a-f]+$", ops):
            pend_dx = int(ops.split(", ")[1], 0)
            imms_dx.append(pend_dx)

        # player field writes: [eax+off] / [edx+off] after loading player from [ebp-4]
        wm = re.search(r"mov (?:byte|word|dword) ptr \[(eax|edx|ebx|esi) \+ (0x[0-9a-f]+)\],", ops)
        if wm:
            writes.append("%s+%s" % (wm.group(1), wm.group(2)))

        if m == "call":
            slot = None
            if "0x250" in ops:
                slot = "0x250"
            elif "0x254" in ops:
                slot = "0x254"
            elif "+ 0xe0]" in ops:
                slot = "0xe0"
            if slot:
                send_slots.append((slot, pend_dx))
                if pend_dx is not None:
                    sends.append(pend_dx)
            elif ops.startswith("0x"):
                tgt = int(ops, 0)
                calls.append(tgt)
                stub, sig = is_stub(tgt)
                if stub:
                    stub_callees.append((tgt, sig))
            pend_dx = None

        if m == "jmp":
            if ops.startswith("0x"):
                tgt = int(ops, 0)
                if tgt == DEFAULT:
                    break
                va = tgt
                continue
            break
        if m == "ret":
            break
        va += ins.size
    return {
        "fields": fields,
        "sends": sends,
        "send_slots": send_slots,
        "calls": ["%08X" % c for c in calls],
        "stub_callees": [["%08X" % a, s] for a, s in stub_callees],
        "writes": writes,
        "imms_dx": imms_dx,
        "disasm": lines,
    }


def disasm_func(va, nbytes=0x180):
    lines = []
    stub, sig = is_stub(va)
    if stub:
        return ["STUB %08X %s" % (va, sig)], True
    for addr, hx, m, ops, sz in disasm_range(va, nbytes):
        lines.append("%08X  %-22s %s %s" % (addr, hx, m, ops))
        if m == "ret":
            break
        if len(lines) >= 80:
            lines.append("... truncated")
            break
    return lines, False


missing = []
present = []
for ident in latter:
    h = int(real[str(ident)][0], 16) if str(ident) in real else int(real[ident][0], 16) if ident in real else None
    # walk.json keys are strings
    h = int(walk["real"][str(ident)][0], 16)
    if ident in hits:
        present.append(ident)
    else:
        missing.append(ident)

# also: ident has const but no case
const_only = [i for i in latter if i in {v: k for k, v in consts.items()} and i not in hits]
# wait, consts values
const_vals = set(consts.values())
const_no_case = [i for i in latter if i in const_vals and i not in hits]

rows = []
dump_txt = []
dump_txt.append("LATTER RANGE: ident %d..%d  count=%d" % (latter[0], latter[-1], len(latter)))
dump_txt.append("C# has case: %d   MISSING case: %d   const-but-no-case: %d"
                % (len(present), len(missing), len(const_no_case)))
dump_txt.append("MISSING: " + " ".join(str(i) for i in missing))
dump_txt.append("")

for ident in missing:
    h = int(walk["real"][str(ident)][0], 16)
    p = profile_handler(h)
    rows.append({
        "ident": ident,
        "handler": "%08X" % h,
        "fields": p["fields"],
        "sends": p["sends"],
        "send_slots": p["send_slots"],
        "calls": p["calls"],
        "stub_callees": p["stub_callees"],
        "writes": p["writes"],
    })
    dump_txt.append("=" * 72)
    dump_txt.append("CM %d (0x%04X)  handler %08X" % (ident, ident, h))
    dump_txt.append("fields=%s  SM=%s  slots=%s  stubs=%s  writes=%s"
                    % (",".join(p["fields"]) or "-",
                       ",".join(str(x) for x in p["sends"]) or "-",
                       p["send_slots"],
                       p["stub_callees"],
                       p["writes"]))
    dump_txt.append("calls=%s" % ",".join(p["calls"]))
    dump_txt.append("--- handler ---")
    dump_txt.extend(p["disasm"])
    for c in p["calls"][:4]:
        va = int(c, 16)
        # skip RTL helpers
        if va < 0x600000:
            dump_txt.append("--- callee %s (low/RTL, skip body) ---" % c)
            continue
        lines, stub = disasm_func(va)
        dump_txt.append("--- callee %s%s ---" % (c, " STUB" if stub else ""))
        dump_txt.extend(lines)

open(os.path.join(OUTDIR, "latter_missing.txt"), "w", encoding="utf-8").write("\n".join(dump_txt))
json.dump({
    "range": [latter[0], latter[-1]],
    "latter_count": len(latter),
    "present": present,
    "missing": missing,
    "const_no_case": const_no_case,
    "rows": rows,
}, open(os.path.join(OUTDIR, "latter_missing.json"), "w"), indent=1)

print("latter %d..%d n=%d" % (latter[0], latter[-1], len(latter)))
print("present=%d missing=%d" % (len(present), len(missing)))
print("MISSING:", missing)
