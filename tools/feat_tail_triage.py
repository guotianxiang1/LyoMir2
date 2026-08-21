#!/usr/bin/env python3
"""Batch triage funccat_m2_missing.tsv lines 602-901 (tail batch).

Classifies each VA as REPLICATED / NOISE / MISSING based on:
  * absent_string quality (epilogue-byte noise vs readable Chinese)
  * prologue check (55 8B EC = real function)
  * SM send / gameplay signals in first N instructions
  * C# Native VA registry hits
"""
import re
import sys
from pathlib import Path

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
TSV = r"D:\loym2\workspace-archive\evidence\m2\host-gap-scan-20260814-2008\funccat_m2_missing.tsv"
CS_ROOT = Path(r"D:\loym2\_gapwork\feat-tail")

with open(IMAGE, "rb") as f:
    IMG = f.read()

# preload C# VA mentions (narrow: GameSvr + SystemModule only)
VA_RE = re.compile(r"0x0*([0-9A-Fa-f]{6,8})\b")
cs_vas = set()
for p in list(CS_ROOT.glob("GameSvr/**/*.cs")) + list(CS_ROOT.glob("SystemModule/**/*.cs")):
    try:
        txt = p.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        continue
    for m in VA_RE.finditer(txt):
        cs_vas.add(int(m.group(1), 16))

# epilogue / prologue byte patterns misread as GBK
NOISE_FRAGS = (
    "[嬪]", "[Y]", "[YY]", "^[嬪]", "嬪]", "Y]", "YY]", "]脥@", "]脨", "脥@",
    "脣繳", "肬嬱", "兡", "婨", "軪", "奅", "鼆}", "3繸", "勠t", "脣纼", "脣来",
    "脣榔", "脙-", "SVW", "QSVW", "VW3", "塃", "鴫E", "鼖E", "麎E", "魤U", "饗",
    "鑃", "靿M", "鄩M", "魟襱", "伳", "霺", "鳹", "鬝", "餝", "饓", "鋲", "鑹",
    "蹓]", "蓧M", "缐E", "覊U", "鞁", "虊]", "袐", "驆", "諵", "疩", "籗", "莼S",
    "pT", "臥", "傪", "鞴", "駤", "趬E", "蠸", "郤", "級E", "攭M", "萐", "訴W",
    "袎", "躍", "鳶", "鲏", "鬚", "鳳", "鳼", "鸰", "鸞", "颻", "骭", "髬", "鲖",
    "麐", "饗錧", "魦錧", "魞x", "黖", "黕", "黤", "黋", "黬", "鼉", "鼔", "鼚",
    "鼱", "鼕", "鼌", "踾", "笭", "畗", "洮", "劗", "尞", "喇", "漠", "瘆", "袁",
    "慢f", "冴", "忐", "匇", "圡", "圲", "圗", "枋", "枨", "瑕", "枨", "栉弩",
)

READABLE_CN = re.compile(r"[\u4e00-\u9fff]{2,}")


def rd(va, n):
    o = va - BASE
    if o < 0 or o + n > len(IMG):
        return b""
    return IMG[o:o + n]


def is_prologue(va):
    b = rd(va, 3)
    return b == b"\x55\x8b\xec" or b[:2] == b"\x55\x8b"


def string_noise(s):
    if not s or len(s.strip()) <= 2:
        return True
    s = s.strip()
    if READABLE_CN.search(s):
        return False
    # mostly assembly fragments
    hits = sum(1 for f in NOISE_FRAGS if f in s)
    if hits >= 1 and len(s) < 30:
        return True
    # high ratio of non-CJK printable
    cjk = sum(1 for c in s if "\u4e00" <= c <= "\u9fff")
    if cjk == 0 and len(s) < 40:
        return True
    return False


def scan_signals(va, count=50):
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    buf = rd(va, count * 16)
    sm = False
    global_ref = False
    calls = []
    for ins in md.disasm(buf, va):
        ops = ins.op_str
        if ins.mnemonic == "call" and ("+ 0x250]" in ops or "+ 0x254]" in ops):
            sm = True
        if "0x7d" in ops.lower() or "0x7c" in ops.lower():
            global_ref = True
        if ins.mnemonic == "call" and ops.startswith("0x"):
            calls.append(int(ops, 0))
        if len(calls) > 20:
            break
    return sm, global_ref, calls[:8]


def triage_line(ln, va, absent_str):
    note = []
    if string_noise(absent_str):
        return "NOISE", "absent_string=asm_epilogue_fragment"
    if va in cs_vas:
        return "REPLICATED", "va_in_csharp"
    if not is_prologue(va):
        # mid-function hit from string census
        # walk back up to 64 bytes for prologue
        found = False
        for back in range(0, 64, 1):
            if is_prologue(va - back):
                va = va - back
                note.append("adjusted_prologue=0x%08X" % va)
                found = True
                break
        if not found:
            return "NOISE", "not_function_entry"
    sm, glob, calls = scan_signals(va, 40)
    if sm:
        note.append("SM_send")
    if glob:
        note.append("global_ref")
    # logging-only: calls 0x79DF74 without SM
    if 0x79DF74 in calls and not sm:
        return "NOISE", "logging_only;" + ";".join(note)
    # Delphi RTL only
    rtl = {0x404A70, 0x405774, 0x405890, 0x404690, 0x402FD0, 0x405A20}
    if calls and all(c < 0x410000 or c in rtl for c in calls):
        return "NOISE", "rtl_only;" + ";".join(note)
    if va in cs_vas:
        return "REPLICATED", "va_in_csharp_post_scan"
    # readable Chinese string but no SM -> likely log/config
    if READABLE_CN.search(absent_str) and not sm:
        return "NOISE", "readable_but_no_sm;" + ";".join(note)
    return "MISSING", "needs_review;" + ";".join(note)


def main():
    lines = Path(TSV).read_text(encoding="utf-8", errors="replace").splitlines()
    batch = lines[601:901]  # 602-901 inclusive (0-indexed 601-900)
    stats = {"REPLICATED": 0, "NOISE": 0, "MISSING": 0}
    missing = []
    out = []
    for raw in batch:
        parts = raw.split("\t")
        if len(parts) < 4:
            continue
        va = int(parts[0], 16)
        absent = parts[3]
        cls, reason = triage_line(raw, va, absent)
        stats[cls] += 1
        row = "%s\t%s\t%s\t%s" % (parts[0], cls, reason, absent[:60])
        out.append(row)
        if cls == "MISSING":
            missing.append((va, reason, absent))
    print("STATS", stats)
    print("MISSING_COUNT", len(missing))
    for m in missing:
        print("MISSING\t0x%08X\t%s\t%s" % (m[0], m[1], m[2][:80]))
    Path(r"D:\loym2\_gapwork\feat-tail\docs\feat_tail_triage_602_901.tsv").write_text(
        "va\tclass\treason\tabsent_string\n" + "\n".join(out), encoding="utf-8"
    )


if __name__ == "__main__":
    main()
