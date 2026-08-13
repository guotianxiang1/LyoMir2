"""Now that PEnvir is known to be [obj+0x128] (proved by 0x766341 in the
m_boDeath:=TRUE routine 0x76631C, and by 0x77AE80 in the QuestNPC binder),
re-scan the four VMT slots keeping only sites whose receiver came from +0x128."""
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, md
from _b8_region import dis2

SLOTS = {0x00: "DeleteObject -> @OnLeave", 0x04: "AddObject -> @OnEnter",
         0x08: "slot8 -> @OnDie", 0x10: "slot10 -> @OnReLive"}
REGN = {0: "eax", 1: "ecx", 2: "edx", 3: "ebx", 4: "esp", 5: "ebp", 6: "esi", 7: "edi"}
PENVIR = "0x128"

res = {s: [] for s in SLOTS}
for off in range(0x40, len(DATA) - 4):
    if DATA[off] != 0xFF:
        continue
    modrm = DATA[off + 1]
    if ((modrm >> 3) & 7) != 2:
        continue
    mod, rm = modrm >> 6, modrm & 7
    if rm in (4, 5):
        continue
    disp = 0 if mod == 0 else (DATA[off + 2] if mod == 1 else None)
    if disp not in SLOTS:
        continue
    va = BASE + off
    prev = list(md.disasm(DATA[off - 0x40:off], va - 0x40))
    vmtload = None
    for ins in prev:
        if ins.mnemonic == "mov" and ins.op_str.startswith(REGN[rm] + ", dword ptr ["):
            vmtload = ins
    if vmtload is None:
        continue
    inner = vmtload.op_str.split("[")[1].rstrip("]")
    if "+" in inner or "-" in inner:
        continue
    # backward dataflow: resolve the receiver through reg<-reg moves
    want = inner
    limit = vmtload.address
    src = None
    for _ in range(6):
        found = None
        for ins in prev:
            if ins.address >= limit:
                break
            if ins.mnemonic == "mov" and ins.op_str.startswith(want + ", "):
                found = ins
        if found is None:
            break
        rhs = found.op_str.split(", ", 1)[1]
        src, limit = rhs, found.address
        if rhs in REGN.values():
            want = rhs
            continue
        break
    if not src or PENVIR not in src:
        continue
    res[disp].append(va)

for s, nm in SLOTS.items():
    print("=" * 78)
    print("VMT+0x%02X  %-26s  %d site(s) with receiver = [obj+0x128] (PEnvir)" % (
        s, nm, len(res[s])))
    print("=" * 78)
    for va in res[s]:
        print("\n---- call site 0x%06X" % va)
        print(dis2(va - 0x2C, va + 8))
    print()
