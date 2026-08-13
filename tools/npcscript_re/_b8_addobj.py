"""AddObject (VMT+4) / DeleteObject (VMT+0) callers.

Filter: the receiver must be a TEnvironment-ish value AND the call must pass
edx = an object.  Practical discriminator used here: the *called* code is only
ever TEnvironment/TDynEnvir/TArenaRoom, whose instances are produced by the map
manager.  So we keep sites in the game units and print them for inspection.
"""
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, md
from _b8_region import dis2

REGN = {0: "eax", 1: "ecx", 2: "edx", 3: "ebx", 4: "esp", 5: "ebp", 6: "esi", 7: "edi"}
WANT = {0x00: "DeleteObject", 0x04: "AddObject"}
# game units only (VCL/RTL live below 0x520000 and in 0x4xxxxx)
LO, HI = 0x5F0000, 0x7D0000

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
    if disp not in WANT:
        continue
    va = BASE + off
    if not (LO <= va < HI):
        continue
    prev = list(md.disasm(DATA[off - 0x30:off], va - 0x30))
    vmtload = None
    for ins in prev:
        if ins.mnemonic == "mov" and ins.op_str.startswith(REGN[rm] + ", dword ptr ["):
            vmtload = ins
    if vmtload is None:
        continue
    inner = vmtload.op_str.split("[")[1].rstrip("]")
    if "+" in inner or "-" in inner:
        continue
    print("\n==== 0x%06X  call [%s+0x%02X]   %s" % (va, REGN[rm], disp, WANT[disp]))
    print(dis2(va - 0x30, va + 8))
