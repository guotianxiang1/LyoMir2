"""Narrow the virtual-dispatch scan: report, for each candidate, the field offset the
receiver was loaded from (mov <base>,[<r2>+disp]) so we can spot the PEnvir field."""
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, md
from _b8_region import dis2
from collections import Counter

SLOTS = {0x00: "DeleteObject(->@OnLeave)", 0x04: "AddObject(->@OnEnter)",
         0x08: "slot8(->@OnDie)", 0x10: "slot10(->@OnReLive)"}
REGN = {0: "eax", 1: "ecx", 2: "edx", 3: "ebx", 4: "esp", 5: "ebp", 6: "esi", 7: "edi"}


def scan():
    out = []
    for off in range(0x20, len(DATA) - 4):
        if DATA[off] != 0xFF:
            continue
        modrm = DATA[off + 1]
        if ((modrm >> 3) & 7) != 2:
            continue
        mod, rm = modrm >> 6, modrm & 7
        if rm in (4, 5):
            continue
        if mod == 0:
            disp = 0
        elif mod == 1:
            disp = DATA[off + 2]
        else:
            continue
        if disp not in SLOTS:
            continue
        va = BASE + off
        # decode the 0x20 bytes before, find the last  mov <rm>, [X]  (VMT load)
        prev = list(md.disasm(DATA[off - 0x20:off], va - 0x20))
        vmtload = None
        for ins in prev:
            if ins.mnemonic == "mov" and ins.op_str.startswith(REGN[rm] + ", dword ptr ["):
                vmtload = ins
        if vmtload is None:
            continue
        # what register holds Self?
        inner = vmtload.op_str.split("[")[1].rstrip("]")
        if "+" in inner or "-" in inner:
            continue                       # VMT load must be [self] with no disp
        selfreg = inner
        # now find where selfreg came from
        src = None
        for ins in prev:
            if ins.address >= vmtload.address:
                break
            if ins.mnemonic == "mov" and ins.op_str.startswith(selfreg + ", "):
                src = ins.op_str
        out.append((disp, va, selfreg, src))
    return out


H = scan()
for s, nm in SLOTS.items():
    sub = [h for h in H if h[0] == s]
    print("=" * 78)
    print("VMT+0x%02X %-28s  %d sites with a clean [self] VMT load" % (s, nm, len(sub)))
    c = Counter(h[3] for h in sub)
    for k, v in c.most_common(18):
        print("    %4d x   self <- %s" % (v, k))
    print()
