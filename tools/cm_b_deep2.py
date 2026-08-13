"""Follow-up dumps: 3290 writer, 4106 callee, 4408/4410, 4647, 4629 send tail."""
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False
OUT = r"D:/loym2/staging/m_cm_b/deep2.txt"

def dump(va, n=80, nbytes=0x300):
    lines = ["=== %08X ===" % va]
    c = 0
    for i in md.disasm(data[va-BASE:va-BASE+nbytes], va):
        lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
        c += 1
        if c >= n:
            break
        if i.mnemonic == "ret" and c > 3:
            break
    return lines

lines = []
# 3290 second xref
lines.append("=== around 0x792C40 (7D70E4 xref) ===")
va = 0x792C30
for i in md.disasm(data[va-BASE:va-BASE+0x80], va):
    lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
    if i.address > 0x792C80:
        break

lines += dump(0x6EE2F0, 100, 0x280)
lines += dump(0x6F37EC, 80, 0x200)  # 4408/4410
lines += dump(0x6F38A8, 80, 0x200)  # 4409/4411
lines += dump(0x6FB6FC, 60, 0x180)  # 4647
lines += dump(0x6F7DE3, 80, 0x200)  # 4629 fail/send tail (from jne target)

# 4408 vs 4410 handlers
lines.append("=== CM 4408 handler 0x6DB08A ===")
va = 0x6DB08A
for i in md.disasm(data[va-BASE:va-BASE+0x40], va):
    lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
    if i.mnemonic == "jmp" and "6dbc2c" in i.op_str:
        break
lines.append("=== CM 4410 handler 0x6DB0D0 ===")
va = 0x6DB0D0
for i in md.disasm(data[va-BASE:va-BASE+0x40], va):
    lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
    if i.mnemonic == "jmp" and "6dbc2c" in i.op_str:
        break
lines.append("=== CM 4409 handler 0x6DB0B2 ===")
va = 0x6DB0B2
for i in md.disasm(data[va-BASE:va-BASE+0x30], va):
    lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
    if i.mnemonic == "jmp" and "6dbc2c" in i.op_str:
        break
lines.append("=== CM 4411 handler 0x6DB0F8 ===")
va = 0x6DB0F8
for i in md.disasm(data[va-BASE:va-BASE+0x30], va):
    lines.append("%08X  %-22s %s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str))
    if i.mnemonic == "jmp" and "6dbc2c" in i.op_str:
        break

# 4446 0x712be4
lines += dump(0x712BE4, 40, 0x80)

# 4102 readers of +0x18DC
import struct
needle = struct.pack("<I", 0x18DC)
# search mov word [r+0x18DC] encodings: 66 89 xx DC 18 00 00 or 66 8B
lines.append("\n=== hits of imm32 0x18DC in CODE ===")
off = 0x1000
count = 0
while count < 25:
    i = data.find(needle, off, 0x3A10D0)
    if i < 0:
        break
    va = BASE + i
    # context
    ctx_va = va - 8
    bits = []
    for ins in md.disasm(data[ctx_va-BASE:ctx_va-BASE+24], ctx_va):
        mark = " <<<" if ins.address <= va < ins.address + ins.size else ""
        bits.append("%08X %s %s%s" % (ins.address, ins.mnemonic, ins.op_str, mark))
        if ins.address > va + 4:
            break
    lines.append("hit %08X" % va)
    lines.extend("  " + b for b in bits)
    off = i + 1
    count += 1

open(OUT, "w", encoding="utf-8").write("\n".join(lines))
print("wrote", OUT)
