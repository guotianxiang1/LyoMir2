# guardfix: does [ebp-0x10] (holding 3001/3002/3005/3006) reach a send slot?
from capstone import *
from capstone.x86 import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
buf = open(IMG, "rb").read()
def rd(va, n): return buf[va - BASE: va - BASE + n]
md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True

START, END = 0x6E80CC, 0x6E8340
print("=== full body 0x6E80CC .. 0x6E8340, marking ebp-0x10 uses and send slots ===")
for ins in md.disasm(rd(START, END - START), START):
    line = f"{ins.address:08X}  {ins.bytes.hex():<20} {ins.mnemonic} {ins.op_str}"
    mark = ""
    if "ebp - 0x10" in ins.op_str:
        mark = "   <== IDENT LOCAL"
    if ins.mnemonic == "call" and ("+ 0x254" in ins.op_str or "+ 0x250" in ins.op_str
                                   or "+ 0xe0" in ins.op_str):
        mark = "   <== SEND SLOT"
    print(line + mark)
