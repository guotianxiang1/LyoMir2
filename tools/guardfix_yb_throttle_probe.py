# guardfix: prove the throttle branch direction -- does `jbe` reject or allow?
from capstone import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
buf = open(IMG, "rb").read()
def rd(va, n): return buf[va - BASE: va - BASE + n]
md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True

SITES = [
    ("CM 1252 MoreThanTenMs", 0x632A63, 0x632B30),
    ("CM 1257 MoreThanTwoMs", 0x632D83, 0x632D9A),
    ("CM 1256 DifferentTick", 0x632C3B, 0x632C52),
]
for name, start, end in SITES:
    print(f"=== {name} @0x{start:X} ===")
    for ins in md.disasm(rd(start, end - start), start):
        print(f"  {ins.address:08X}  {ins.bytes.hex():<16} {ins.mnemonic} {ins.op_str}")
    print()

# Where does each conditional branch land relative to the send slot?
print("=== branch target vs the send call for CM 1252 ===")
print("  jbe target 0x%X" % (0x632A69 + 6 + 0xB4))
print("  selector store 0x632B0E, emitter call 0x632B17 (ends 0x632B1C)")
for ins in md.disasm(rd(0x632B0E, 0x632B30 - 0x632B0E), 0x632B0E):
    print(f"  {ins.address:08X}  {ins.bytes.hex():<16} {ins.mnemonic} {ins.op_str}")
