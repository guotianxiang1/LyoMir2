"""Exhaustive byte-pattern audit of the bodyState subsystem in the flat image.

Usage: python tools/m2_statescan.py

Answers, without relying on a full linear sweep (which desynchronises on Delphi
data islands and silently produces false negatives):

  1. Every instruction that touches the obj+0x168 bodyState bitset.
  2. Whether any immediate-form bit test on that bitset exists at all.
  3. Every instruction that references obj+0x3A4 (state 26 cooldown deadline).
  4. Whether any x87 instruction references obj+0x3A4.

Backs docs/STATE_23_24_NON_EXISTENCE.md.
"""
import os
import re

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = os.environ.get("M2_FLAT_IMAGE",
                       r"D:/loym2/staging/_reunpack_work/flat_image.bin")
BASE = 0x400000

DISP_BITSET = re.escape(b"\x68\x01\x00\x00")   # disp32 = 0x168
DISP_DEADLINE = re.escape(b"\xa4\x03\x00\x00")  # disp32 = 0x3A4

with open(IMAGE, "rb") as handle:
    DATA = handle.read()
MD = Cs(CS_ARCH_X86, CS_MODE_32)


def decode_at(off):
    for insn in MD.disasm(DATA[off:off + 16], off + BASE):
        return "0x%06X  %-22s %s %s" % (insn.address, insn.bytes.hex().upper(),
                                        insn.mnemonic, insn.op_str)
    return "0x%06X  <undecodable>" % (off + BASE)


def scan(label, pattern):
    hits = [m.start() for m in re.finditer(pattern, DATA, re.DOTALL)]
    print("=== %s : %d hits ===" % (label, len(hits)))
    for hit in hits:
        print("   " + decode_at(hit))
    print()
    return hits


# --- obj+0x168 bitset -------------------------------------------------------
# Register-indexed forms are the only ones the engine emits.
scan("bt  [reg+0x168], reg32   (0F A3, mod=10)", b"\x0f\xa3[\x80-\xbf]" + DISP_BITSET)
scan("bts [reg+0x168], reg32   (0F AB, mod=10)", b"\x0f\xab[\x80-\xbf]" + DISP_BITSET)
scan("btr [reg+0x168], reg32   (0F B3, mod=10)", b"\x0f\xb3[\x80-\xbf]" + DISP_BITSET)
scan("btc [reg+0x168], reg32   (0F BB, mod=10)", b"\x0f\xbb[\x80-\xbf]" + DISP_BITSET)

# Immediate form: expected to be 0 for EVERY state id. A zero result here is
# therefore not evidence that a particular state is unused.
scan("bt/bts/btr/btc [reg+0x168], imm8  (0F BA, ANY imm)",
     b"\x0f\xba[\xa0-\xbf]" + DISP_BITSET)

# --- obj+0x3A4 state 26 deadline -------------------------------------------
print("=== references to obj+0x3A4 ===")
for match in re.finditer(DISP_DEADLINE, DATA, re.DOTALL):
    hit = match.start()
    for back in range(2, 9):
        start = hit - back
        insn = next(iter(MD.disasm(DATA[start:start + 16], start + BASE)), None)
        if insn and insn.size in (back + 4, back + 5) and "0x3a4" in insn.op_str:
            print("   0x%06X  %-22s %s %s" % (insn.address, insn.bytes.hex().upper(),
                                              insn.mnemonic, insn.op_str))
            break
print()

scan("fstp qword ptr [reg+0x3A4]  (DD /3)", b"\xdd[\x98-\x9f]" + DISP_DEADLINE)
scan("ANY x87 op referencing +0x3A4  (D8-DF, mod=10)",
     b"[\xd8-\xdf][\x80-\xbf]" + DISP_DEADLINE)
