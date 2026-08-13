# guardfix: is the "CancelYBDeal" GM name-table entry a faithful transcription?
from capstone import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
buf = open(IMG, "rb").read()
def rd(va, n): return buf[va - BASE: va - BASE + n]
md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True

needle = b"CancelYBDeal"
print("=== occurrences of the ASCII name in the image ===")
i = buf.find(needle)
while i != -1:
    va = i + BASE
    prefix = buf[i-1]
    print(f"  0x{va:X}  len-prefix byte = 0x{prefix:02X} ({prefix})  "
          f"{'ShortString' if prefix == len(needle) else 'not a ShortString prefix'}")
    i = buf.find(needle, i + 1)

TABLE, STRIDE, IDX = 0x7B4654, 0x120, 96
slot = TABLE + STRIDE * IDX
print(f"\n=== table 0x{TABLE:X} stride 0x{STRIDE:X} index {IDX} -> slot 0x{slot:X} ===")
print("  first 0x40 bytes:", rd(slot, 0x40).hex())
raw = rd(slot, 0x20)
print("  as ShortString  :", raw[0], raw[1:1+raw[0]])

print("\n=== handler @0x624FF8 ===")
for ins in md.disasm(rd(0x624FF8, 0x20), 0x624FF8):
    print(f"  {ins.address:08X}  {ins.bytes.hex():<16} {ins.mnemonic} {ins.op_str}")
