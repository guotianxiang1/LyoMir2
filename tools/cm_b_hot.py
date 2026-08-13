"""Deep-dump the traffic-carrying PRESENT handlers in ident 3284..4651.

For each hot ident: full handler body (to jmp-default / ret), then each
first-level callee's full body, plus Delphi length-prefixed string resolution
for any pushed .rdata pointer.

Writes staging/m_cm_b/hot_<ident>.txt and hot_index.txt
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
OUTDIR = r"D:/loym2/staging/m_cm_b"
CODE_LO, CODE_HI = 0x401000, 0x7A10D0

data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False


def rd32(va):
    o = va - BASE
    if o < 0 or o + 4 > len(data):
        return None
    return struct.unpack("<I", data[o:o + 4])[0]


def dstr(va):
    """Delphi long string: dword at ptr-4 is char count."""
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


def body(va, maxins=600, stop_default=True):
    """Linear disasm following unconditional jmp, stopping at ret/default."""
    out = []
    seen = set()
    n = 0
    while n < maxins:
        n += 1
        if va in seen:
            out.append("%08X  <loop back>" % va)
            break
        seen.add(va)
        o = va - BASE
        ins = None
        for i in md.disasm(data[o:o + 16], va):
            ins = i
            break
        if ins is None:
            out.append("%08X  <undecodable>" % va)
            break
        ann = ""
        # annotate immediate that resolves to a Delphi string
        for m in re.finditer(r"0x[0-9a-f]{6,8}", ins.op_str):
            s = dstr(int(m.group(0), 0))
            if s:
                ann = "   ; \"%s\"" % s
                break
        out.append("%08X  %-20s %s %s%s"
                   % (va, ins.bytes.hex().upper(), ins.mnemonic, ins.op_str, ann))
        if ins.mnemonic == "ret":
            break
        if ins.mnemonic == "jmp":
            if ins.op_str.startswith("0x"):
                t = int(ins.op_str, 0)
                if stop_default and t == DEFAULT:
                    out.append("           -> DEFAULT (silent drop)")
                    break
                va = t
                continue
            break
        va += ins.size
    return out


def callees(lines):
    r = []
    for l in lines:
        m = re.search(r"\bcall (0x[0-9a-f]+)$", l)
        if m:
            t = int(m.group(1), 0)
            if CODE_LO <= t <= CODE_HI and t not in r:
                r.append(t)
    return r


walk = json.load(open(os.path.join(OUTDIR, "walk.json")))
real = {int(k): int(v[0], 16) for k, v in walk["real"].items()}

idents = [int(x) for x in sys.argv[1:]]
index = []
for ident in idents:
    h = real[ident]
    lines = ["CM %d (0x%04X)  handler %08X" % (ident, ident, h), "=" * 70,
             "--- handler ---"]
    hb = body(h)
    lines += hb
    for c in callees(hb):
        lines.append("")
        lines.append("--- callee %08X ---" % c)
        cb = body(c, maxins=400, stop_default=False)
        lines += cb
    open(os.path.join(OUTDIR, "hot_%d.txt" % ident), "w",
         encoding="utf-8").write("\n".join(lines))
    index.append("%d -> %08X  (%d lines, %d callees)"
                 % (ident, h, len(lines), len(callees(hb))))
    print(index[-1])

open(os.path.join(OUTDIR, "hot_index.txt"), "a", encoding="utf-8").write(
    "\n".join(index) + "\n")
