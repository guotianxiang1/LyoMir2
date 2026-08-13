import struct, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
from capstone import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(IMG, "rb").read()

# Find all 4-byte little-endian occurrences of each quiz-state displacement,
# then disassemble a small window backwards to show the instruction.
disps = {
    0x7b0: "cooldown/timer dword",
    0x7b4: "speak-count dword",
    0x7b8: "correct-answer char[] / flag",
    0x7c3: "quiz-active flag byte",
    0x7c4: "answer-pending flag byte",
}
md = Cs(CS_ARCH_X86, CS_MODE_32)

def find_all(needle):
    out = []
    start = 0
    while True:
        i = DATA.find(needle, start)
        if i < 0:
            break
        out.append(i + BASE)
        start = i + 1
    return out

for disp, desc in disps.items():
    needle = struct.pack("<I", disp)
    hits = find_all(needle)
    print("=" * 78)
    print("disp +0x%X (%s): %d raw hits" % (disp, desc, len(hits)))
    print("-" * 78)
    shown = 0
    for va in hits:
        # try to disassemble the instruction that ends with this displacement:
        # scan back up to 10 bytes to find an instruction covering va..va+4
        for back in range(1, 12):
            start = va - back
            code = DATA[start - BASE: start - BASE + 16]
            try:
                insn = next(md.disasm(code, start))
            except StopIteration:
                continue
            if insn.address <= va < insn.address + insn.size and insn.address + insn.size >= va + 4:
                # displacement is within this instruction
                if ("0x%x" % disp) in insn.op_str:
                    print("  %08X  %-20s %s %s" % (insn.address, insn.bytes.hex().upper(), insn.mnemonic, insn.op_str))
                    shown += 1
                    break
    if shown == 0:
        print("  (no clean instruction decode; raw VAs: %s)" % ", ".join("%08X" % v for v in hits[:12]))
    print()
