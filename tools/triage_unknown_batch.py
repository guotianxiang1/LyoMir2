#!/usr/bin/env python3
"""Batch triage UNKNOWN VAs: disassemble, extract calls, scan nearby GBK strings."""
import re, struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000

UNKNOWN_VAS = sorted(set([
    0x006BE8E8, 0x004DA060, 0x0069B3A4,
    0x004354C8, 0x00459288, 0x00459400, 0x00697308, 0x005DCED8, 0x005F077C,
    0x006FEADC, 0x00497D00, 0x0069B924, 0x00486C10, 0x004873DC, 0x0049870C, 0x0061927C,
    0x0044AA94, 0x0045C498, 0x004863B4, 0x00486484, 0x00496CBC, 0x004C8024, 0x004C98AC,
    0x004D7FC0, 0x004EA92C, 0x005290E8, 0x005379B8, 0x0053C5EC, 0x00548890, 0x005489B8,
    0x0054B1C8, 0x00556344, 0x0057C4F0, 0x00586F58, 0x00587810, 0x00598088, 0x005B823C,
    0x005D98A0, 0x005DACCC, 0x00648640, 0x006E0C84, 0x006FA7C0, 0x00746D6C, 0x00798090,
    0x00798648, 0x0079B050, 0x00427268, 0x0042E8E8, 0x0043652C, 0x004387AC, 0x004578B4,
    0x00478670, 0x0047B300, 0x00487228, 0x0049B104, 0x004FC508, 0x0051AA70, 0x00527340,
    0x0052740C, 0x00536D28, 0x00538EA4, 0x0053C440, 0x00564BE4, 0x0057C82C, 0x005B6088,
    0x005D65B0, 0x005E00F4, 0x005E5A4C, 0x005E614C, 0x0063648C, 0x006424E8, 0x0064EF1C,
    0x0065019C, 0x006503C0, 0x00725C0C, 0x00533478, 0x007F31B0, 0x006D4134,
]))

with open(IMAGE, "rb") as f:
    DATA = f.read()

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False
CALL_RE = re.compile(r'0x([0-9a-f]+)', re.I)

def off(va):
    return va - BASE

def scan_gbk_near(va, radius=0x800):
    o = off(va)
    lo = max(0, o - radius)
    hi = min(len(DATA), o + radius)
    hits = []
    i = lo
    while i < hi - 4:
        # Delphi AnsiString: try length at i+4 if refcount looks small
        try:
            ln = struct.unpack_from('<i', DATA, i + 4)[0]
            if 2 <= ln <= 120:
                raw = DATA[i + 8:i + 8 + ln]
                if raw and all(b != 0 for b in raw):
                    try:
                        s = raw.decode('gbk')
                        if sum(1 for c in s if '\u4e00' <= c <= '\u9fff') >= 2:
                            hits.append((BASE + i, s[:80]))
                    except Exception:
                        pass
        except Exception:
            pass
        i += 1
    # dedupe
    seen = set()
    out = []
    for a, s in hits:
        if s not in seen:
            seen.add(s)
            out.append((a, s))
    return out[:8]

def analyze(va, n=50):
    buf = DATA[off(va):off(va) + n * 16]
    calls = []
    globals_r = []
    field_offs = []
    for ins in md.disasm(buf, va):
        if ins.mnemonic == 'call':
            m = CALL_RE.search(ins.op_str)
            if m:
                calls.append(int(m.group(1), 16))
        if 'dword ptr [0x' in ins.op_str:
            m = re.search(r'\[0x([0-9a-f]+)\]', ins.op_str, re.I)
            if m:
                globals_r.append(int(m.group(1), 16))
        m = re.search(r'\[eax \+ 0x([0-9a-f]+)\]', ins.op_str, re.I)
        if m:
            field_offs.append(int(m.group(1), 16))
        if ins.mnemonic in ('ret', 'retn') and ins.address > va + 8:
            break
    return calls[:12], sorted(set(globals_r))[:6], sorted(set(field_offs))[:10]

print(f"count={len(UNKNOWN_VAS)}")
for va in UNKNOWN_VAS:
    calls, globs, fields = analyze(va, 55)
    strs = scan_gbk_near(va)
    print(f"\n=== 0x{va:08X} ===")
    print(f"  calls: {' '.join(f'0x{c:08X}' for c in calls)}")
    print(f"  globals: {' '.join(f'0x{g:08X}' for g in globs)}")
    print(f"  fields: {fields}")
    for a, s in strs:
        print(f"  str@0x{a:08X}: {s!r}")
